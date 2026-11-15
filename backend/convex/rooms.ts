import { v } from "convex/values";
import type { Doc, Id } from "./_generated/dataModel";
import { internalQuery, mutation, type MutationCtx } from "./_generated/server";
import { fail, getPlayer, vec3 } from "./model";
import {
  COLORS,
  DEFAULT_ASKS,
  DEFAULT_FINISH_RADIUS,
  DEFAULT_PICKUP_RADIUS,
  MAX_ASKS,
  MAX_PICKUPS,
  DEFAULT_ROUTE,
  EVENTS_PER_POLL,
  MAX_PLAYERS,
  MAX_ROUTE_POINTS,
  countdownPatch,
  getMembership,
  getRoom,
  isLocked,
  phase,
  placeOrder,
  removeFromRoom,
  results,
  roomPlayers,
  routeLength,
  startingAsks,
  toRacer,
  toRoom,
} from "./raceModel";
import { toEvent } from "./events";

const CODE_LETTERS = "ABCDEFGHJKLMNPQRSTUVWXYZ";

async function leaveOtherRooms(ctx: MutationCtx, playerId: Id<"players">, keep: Id<"rooms"> | null, now: number) {
  const rows = await ctx.db
    .query("roomPlayers")
    .withIndex("by_player", (q) => q.eq("playerId", playerId))
    .collect();
  for (const row of rows) {
    if (row.roomId === keep || row.left) continue;
    const room = (await ctx.db.get(row.roomId))!;
    if (room.kind === "world") continue;
    await removeFromRoom(ctx, room, row, now);
  }
}

export async function newRoomCode(ctx: MutationCtx) {
  for (;;) {
    const code = Array.from({ length: 4 }, () => CODE_LETTERS[Math.floor(Math.random() * CODE_LETTERS.length)]).join("");
    const taken = await ctx.db
      .query("rooms")
      .withIndex("by_code", (q) => q.eq("code", code))
      .first();
    if (!taken) return code;
  }
}

async function joinedResponse(ctx: MutationCtx, roomId: Id<"rooms">, playerId: Id<"players">, now: number) {
  const room = (await ctx.db.get(roomId))!;
  const rows = await roomPlayers(ctx, room._id);
  const ordered = placeOrder(rows);
  const you = ordered.findIndex((row) => row.playerId === playerId);
  return {
    room: toRoom(room, rows, now),
    route: room.route,
    you: toRacer(room, ordered[you], you + 1),
  };
}

export async function addRacer(ctx: MutationCtx, room: Doc<"rooms">, player: Doc<"players">, slot: number, now: number) {
  await ctx.db.insert("roomPlayers", {
    roomId: room._id,
    playerId: player._id,
    displayName: player.displayName,
    slot,
    ready: false,
    position: room.route[0],
    heading: 0,
    progress: 0,
    finishedAt: 0,
    asks: startingAsks(room),
    collected: [],
    left: false,
    updatedAt: now,
  });
}

export const create = mutation({
  args: {
    playerId: v.string(),
    route: v.optional(v.array(vec3)),
    finishRadius: v.optional(v.number()),
    minPlayers: v.optional(v.number()),
    asks: v.optional(v.number()),
    pickups: v.optional(v.array(vec3)),
    pickupRadius: v.optional(v.number()),
  },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const route = args.route ?? DEFAULT_ROUTE;
    if (route.length < 2 || route.length > MAX_ROUTE_POINTS) {
      fail("INVALID_ARGUMENT", `route must have 2 to ${MAX_ROUTE_POINTS} points`);
    }
    const length = routeLength(route);
    if (!(length > 0)) fail("INVALID_ARGUMENT", "route must have a nonzero length");
    const finishRadius = args.finishRadius ?? DEFAULT_FINISH_RADIUS;
    if (!(finishRadius >= 1 && finishRadius <= 100)) fail("INVALID_ARGUMENT", "finishRadius must be 1 to 100");
    const minPlayers = args.minPlayers ?? 2;
    if (!Number.isInteger(minPlayers) || minPlayers < 1 || minPlayers > MAX_PLAYERS) {
      fail("INVALID_ARGUMENT", `minPlayers must be an integer from 1 to ${MAX_PLAYERS}`);
    }
    const asks = args.asks || DEFAULT_ASKS;
    if (!Number.isInteger(asks) || asks < 1 || asks > MAX_ASKS) fail("INVALID_ARGUMENT", `asks must be an integer from 1 to ${MAX_ASKS}`);
    const pickups = args.pickups ?? [];
    if (pickups.length > MAX_PICKUPS) fail("INVALID_ARGUMENT", `pickups must have at most ${MAX_PICKUPS} points`);
    const pickupRadius = args.pickupRadius || DEFAULT_PICKUP_RADIUS;
    if (!(pickupRadius >= 1 && pickupRadius <= 20)) fail("INVALID_ARGUMENT", "pickupRadius must be 1 to 20");

    const now = Date.now();
    await leaveOtherRooms(ctx, player._id, null, now);
    const roomId = await ctx.db.insert("rooms", {
      code: await newRoomCode(ctx),
      status: "lobby",
      raceNumber: 1,
      minPlayers,
      startsAt: 0,
      closesAt: 0,
      route,
      routeLength: length,
      finishRadius,
      eventSeq: 0,
      createdAt: now,
      asks,
      pickups,
      pickupRadius,
    });
    await addRacer(ctx, (await ctx.db.get(roomId))!, player, 0, now);
    return joinedResponse(ctx, roomId, player._id, now);
  },
});

export const join = mutation({
  args: { playerId: v.string(), code: v.string() },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const code = args.code.trim().toUpperCase();
    const room = await ctx.db
      .query("rooms")
      .withIndex("by_code", (q) => q.eq("code", code))
      .unique();
    if (!room) fail("NOT_FOUND", `Room ${code} not found`);
    if (room.kind === "world") fail("CONFLICT", "The world room is not a race");

    const now = Date.now();
    const rows = await roomPlayers(ctx, room._id);
    const mine = rows.find((row) => row.playerId === player._id);
    if (mine && !mine.left) return joinedResponse(ctx, room._id, player._id, now);

    const status = phase(room, now);
    if (status === "racing" || status === "finished") fail("CONFLICT", `Room ${code} has already started`);
    if (isLocked(room, now)) fail("CONFLICT", `Room ${code} is about to start`);
    if (rows.length >= MAX_PLAYERS) fail("CONFLICT", `Room ${code} is full`);

    await leaveOtherRooms(ctx, player._id, room._id, now);
    const taken = new Set(rows.map((row) => row.slot));
    const slot = COLORS.findIndex((_, i) => !taken.has(i));
    await addRacer(ctx, room, player, slot, now);
    await ctx.db.patch(room._id, countdownPatch(room, await roomPlayers(ctx, room._id), now));
    return joinedResponse(ctx, room._id, player._id, now);
  },
});

export const leave = mutation({
  args: { playerId: v.string(), roomId: v.string() },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const room = await getRoom(ctx, args.roomId);
    if (room.kind === "world") fail("CONFLICT", "The world room is not a race");
    const row = await ctx.db
      .query("roomPlayers")
      .withIndex("by_player", (q) => q.eq("playerId", player._id).eq("roomId", room._id))
      .unique();
    const now = Date.now();
    if (!row || row.left) return { left: true };
    if (isLocked(room, now)) fail("CONFLICT", "The race is about to start");
    await removeFromRoom(ctx, room, row, now);
    return { left: true };
  },
});

export const setReady = mutation({
  args: { playerId: v.string(), roomId: v.string(), ready: v.boolean() },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const room = await getRoom(ctx, args.roomId);
    if (room.kind === "world") fail("CONFLICT", "The world room is not a race");
    const row = await getMembership(ctx, room, player._id);
    if (row.left) fail("CONFLICT", "Player left this room");
    const now = Date.now();
    const status = phase(room, now);
    const respond = async () =>
      ({ room: toRoom((await ctx.db.get(room._id))!, await roomPlayers(ctx, room._id), now) });

    if (status === "finished" && args.ready) {
      for (const other of await roomPlayers(ctx, room._id)) {
        if (other.left) await ctx.db.delete(other._id);
        else {
          await ctx.db.patch(other._id, {
            ready: other._id === row._id,
            progress: 0,
            finishedAt: 0,
            smash: 0,
            asks: startingAsks(room),
            collected: [],
          });
        }
      }
      const reset = { status: "lobby" as const, raceNumber: room.raceNumber + 1, startsAt: 0, closesAt: 0 };
      await ctx.db.patch(room._id, reset);
      await ctx.db.patch(room._id, countdownPatch({ ...room, ...reset }, await roomPlayers(ctx, room._id), now));
      return respond();
    }
    if (row.ready === args.ready) return respond();
    if (status === "racing") fail("CONFLICT", "The race is running");
    if (status !== "finished" && isLocked(room, now)) fail("CONFLICT", "The race is about to start");

    await ctx.db.patch(row._id, { ready: args.ready });
    if (status !== "finished") {
      await ctx.db.patch(room._id, countdownPatch(room, await roomPlayers(ctx, room._id), now));
    }
    return respond();
  },
});

/** Takes `now` from the caller because Convex caches query results and a query's own clock would go stale. */
export const poll = internalQuery({
  args: { roomId: v.string(), after: v.optional(v.number()), now: v.number() },
  handler: async (ctx, args) => {
    if (args.after !== undefined && !(Number.isInteger(args.after) && args.after >= 0)) {
      fail("INVALID_ARGUMENT", "after must be a non-negative integer");
    }
    const room = await getRoom(ctx, args.roomId);
    const now = args.now;
    const rows = placeOrder(await roomPlayers(ctx, room._id));

    const events =
      args.after === undefined
        ? await ctx.db
            .query("roomEvents")
            .withIndex("by_room_race", (q) => q.eq("roomId", room._id).eq("raceNumber", room.raceNumber))
            .take(EVENTS_PER_POLL)
        : await ctx.db
            .query("roomEvents")
            .withIndex("by_room_seq", (q) => q.eq("roomId", room._id).gt("seq", args.after!))
            .take(EVENTS_PER_POLL);
    const cursor = events.length > 0 ? events[events.length - 1].seq : (args.after ?? room.eventSeq);

    let standings: ReturnType<typeof results> = [];
    if (phase(room, now) === "finished") {
      const raceEvents = await ctx.db
        .query("roomEvents")
        .withIndex("by_room_race", (q) => q.eq("roomId", room._id).eq("raceNumber", room.raceNumber))
        .collect();
      const calls = new Map<Id<"players">, number>();
      for (const event of raceEvents) calls.set(event.playerId, (calls.get(event.playerId) ?? 0) + 1);
      const operatorCalls = await ctx.db
        .query("operatorCalls")
        .withIndex("by_room_race", (q) => q.eq("roomId", room._id).eq("raceNumber", room.raceNumber))
        .collect();
      for (const call of operatorCalls) {
        if (call.outcome !== "refused") calls.set(call.playerId, (calls.get(call.playerId) ?? 0) + 1);
      }
      standings = results(room, rows, calls);
    }

    return {
      serverTime: now,
      room: toRoom(room, rows, now),
      racers: rows.map((row, i) => toRacer(room, row, i + 1)),
      events: events.map(toEvent),
      cursor,
      results: standings,
    };
  },
});
