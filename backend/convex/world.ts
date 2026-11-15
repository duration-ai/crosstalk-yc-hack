import { v, type ObjectType } from "convex/values";
import type { Doc, Id } from "./_generated/dataModel";
import { internalMutation, internalQuery, type QueryCtx } from "./_generated/server";
import { fail, getPlayer, movementMode, vec3 } from "./model";
import { COLORS, DEFAULT_FINISH_RADIUS, DEFAULT_ROUTE, MAX_PLAYERS, routeLength } from "./raceModel";
import { addRacer, newRoomCode } from "./rooms";
import type { MutationCtx } from "./_generated/server";

// The shared world: every online client is in one world shard by default and posts its presence
// at about 8 Hz. A shard holds SHARD_CAP players; the next player past that opens the next shard.
// Presence older than PRESENCE_TIMEOUT_MS no longer counts, so a closed app drops out on its own.
export const SHARD_CAP = 16;
export const PRESENCE_TIMEOUT_MS = 10_000;
const MAX_VEHICLE_TYPE = 64;
const SWEEP_PER_BEAT = 8;

export const presenceArgs = {
  playerId: v.string(),
  position: vec3,
  heading: v.number(),
  speed: v.optional(v.number()),
  mode: v.optional(movementMode),
  vehicleType: v.optional(v.string()),
};

function finite(value: number, name: string) {
  if (!Number.isFinite(value)) fail("INVALID_ARGUMENT", name + " must be a finite number");
}

function activeInShard(ctx: QueryCtx, shard: number, now: number) {
  return ctx.db
    .query("worldPresence")
    .withIndex("by_shard_updated", (q) => q.eq("shard", shard).gt("updatedAt", now - PRESENCE_TIMEOUT_MS));
}

export function findPresence(ctx: QueryCtx, playerId: Id<"players">) {
  return ctx.db
    .query("worldPresence")
    .withIndex("by_player", (q) => q.eq("playerId", playerId))
    .unique();
}

export function isActive(row: Doc<"worldPresence">, now: number) {
  return row.updatedAt > now - PRESENCE_TIMEOUT_MS;
}

export function toWorldPlayer(row: Doc<"worldPresence">) {
  return {
    playerId: row.playerId,
    displayName: row.displayName,
    ...COLORS[row.slot % COLORS.length],
    position: row.position,
    heading: row.heading,
    speed: row.speed,
    mode: row.mode,
    vehicleType: row.vehicleType,
    updatedAt: row.updatedAt,
  };
}

// Writes this player's presence. An active row only patches itself, so steady heartbeats never
// contend with each other; joining (or coming back after the timeout) picks the first shard with
// room and the player colour fewest active players there have.
const heartbeatArgs = { ...presenceArgs, now: v.number() };

export async function beat(ctx: MutationCtx, args: ObjectType<typeof heartbeatArgs>) {
  const player = await getPlayer(ctx, args.playerId);
  finite(args.position.x, "position.x");
  finite(args.position.y, "position.y");
  finite(args.position.z, "position.z");
  finite(args.heading, "heading");
  const speed = args.speed ?? 0;
  finite(speed, "speed");
  const vehicleType = (args.vehicleType ?? "").slice(0, MAX_VEHICLE_TYPE);
  const fields = {
    displayName: player.displayName,
    position: args.position,
    heading: args.heading,
    speed,
    mode: args.mode ?? ("onFoot" as const),
    vehicleType,
    updatedAt: args.now,
  };
  const row = await findPresence(ctx, player._id);
  if (row && isActive(row, args.now)) {
    await ctx.db.patch(row._id, fields);
    await sweep(ctx, row.shard, args.now);
    return { shard: row.shard, slot: row.slot, worldRoomId: row.worldRoomId };
  }
  if (row) await dropPresence(ctx, row);

  let shard = 0;
  let others: Doc<"worldPresence">[] = [];
  for (;; shard++) {
    others = (await activeInShard(ctx, shard, args.now).take(SHARD_CAP + 1)).filter((r) => r.playerId !== player._id);
    if (others.length < SHARD_CAP) break;
  }
  const used = COLORS.map((_, i) => others.filter((r) => r.slot === i).length);
  const slot = used.indexOf(Math.min(...used));
  const room = await worldRoom(ctx, shard, args.now);
  await enterWorldRoom(ctx, room, player, slot, args.now);
  await ctx.db.insert("worldPresence", { ...fields, playerId: player._id, shard, slot, worldRoomId: room._id, joinedAt: args.now });
  await sweep(ctx, shard, args.now);
  return { shard, slot, worldRoomId: room._id };
}

export const heartbeat = internalMutation({ args: heartbeatArgs, handler: beat });

// Presence that has timed out leaves the shard: its row goes, and so does its membership in the
// world room, so voice effects stop reaching a closed app. Each heartbeat clears a few.
async function sweep(ctx: MutationCtx, shard: number, now: number) {
  const stale = await ctx.db
    .query("worldPresence")
    .withIndex("by_shard_updated", (q) => q.eq("shard", shard).lte("updatedAt", now - PRESENCE_TIMEOUT_MS))
    .take(SWEEP_PER_BEAT);
  for (const row of stale) await dropPresence(ctx, row);
}

async function dropPresence(ctx: MutationCtx, row: Doc<"worldPresence">) {
  const member = await ctx.db
    .query("roomPlayers")
    .withIndex("by_player", (q) => q.eq("playerId", row.playerId).eq("roomId", row.worldRoomId))
    .unique();
  if (member) await ctx.db.delete(member._id);
  await ctx.db.delete(row._id);
}

// The shard's world room, made on first use.
async function worldRoom(ctx: MutationCtx, shard: number, now: number) {
  const existing = await ctx.db
    .query("rooms")
    .withIndex("by_world_shard", (q) => q.eq("worldShard", shard))
    .filter((q) => q.eq(q.field("kind"), "world"))
    .first();
  if (existing) return existing;
  const id = await ctx.db.insert("rooms", {
    code: await newRoomCode(ctx),
    status: "lobby",
    raceNumber: 1,
    minPlayers: MAX_PLAYERS,
    startsAt: 0,
    closesAt: 0,
    route: DEFAULT_ROUTE,
    routeLength: routeLength(DEFAULT_ROUTE),
    finishRadius: DEFAULT_FINISH_RADIUS,
    eventSeq: 0,
    createdAt: now,
    kind: "world",
    worldShard: shard,
    freeAsks: true,
  });
  return (await ctx.db.get(id))!;
}

// Puts the player in this shard's world room (outside the 4-racer cap, which only races use)
// and takes them out of any other shard's. World-room rows are deleted rather than going through
// removeFromRoom, so the world room's status never moves.
async function enterWorldRoom(ctx: MutationCtx, room: Doc<"rooms">, player: Doc<"players">, slot: number, now: number) {
  const rows = await ctx.db
    .query("roomPlayers")
    .withIndex("by_player", (q) => q.eq("playerId", player._id))
    .collect();
  let inRoom = false;
  for (const row of rows) {
    if (row.roomId === room._id) {
      inRoom = true;
      if (row.left) await ctx.db.patch(row._id, { left: false, updatedAt: now });
      continue;
    }
    const other = (await ctx.db.get(row.roomId))!;
    if (other.kind === "world") await ctx.db.delete(row._id);
  }
  if (!inRoom) await addRacer(ctx, room, player, slot, now);
}

// Everyone else active in the shard, for the presence response.
const snapshotArgs = { shard: v.number(), playerId: v.string(), now: v.number() };

export async function others(ctx: QueryCtx, args: ObjectType<typeof snapshotArgs>) {
  const rows = await activeInShard(ctx, args.shard, args.now).take(SHARD_CAP * 2);
  return rows.filter((r) => r.playerId !== args.playerId).map(toWorldPlayer);
}

export const snapshot = internalQuery({ args: snapshotArgs, handler: others });

export const leave = internalMutation({
  args: { playerId: v.string() },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const row = await findPresence(ctx, player._id);
    if (row) await dropPresence(ctx, row);
    return { left: true };
  },
});
