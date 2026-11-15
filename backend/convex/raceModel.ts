import { v } from "convex/values";
import type { Doc, Id } from "./_generated/dataModel";
import type { MutationCtx, QueryCtx } from "./_generated/server";
import { fail, type Vec3 } from "./model";

export const COLORS = [
  { color: "blue", colorHex: "#2D8CFF" },
  { color: "purple", colorHex: "#A35BFF" },
  { color: "orange", colorHex: "#FF8A1E" },
  { color: "green", colorHex: "#2EC27E" },
] as const;

export const MAX_PLAYERS = COLORS.length;
export const COUNTDOWN_MS = 3_000;
export const AUTO_START_MS = 10_000;
export const MISSION_COUNTDOWN_MS = 10_000;
export const CLOSE_AFTER_FIRST_FINISH_MS = 30_000;
export const PHOTO_FINISH_MS = 1_000;
export const DEFAULT_FINISH_RADIUS = 15;
export const MAX_ROUTE_POINTS = 512;
export const EVENTS_PER_POLL = 100;
export const DEFAULT_ASKS = 2;
export const MAX_ASKS = 9;
export const MAX_PICKUPS = 64;
export const DEFAULT_PICKUP_RADIUS = 4;
/** Slack on top of the pickup radius for the racer's reported position, which lags the car a little. */
export const COLLECT_SLACK = 3;
export const DEFAULT_ROUTE: Vec3[] = [
  { x: 0, y: 0, z: 0 },
  { x: 0, y: 0, z: 800 },
];

export const roomStatus = v.union(
  v.literal("lobby"),
  v.literal("countdown"),
  v.literal("racing"),
  v.literal("finished"),
);

export type RoomStatus = Doc<"rooms">["status"];
type Room = Doc<"rooms">;
type RoomPlayer = Doc<"roomPlayers">;

/** The room's status at `now`: countdown turns into racing at startsAt, racing into finished at closesAt. */
export function phase(room: Room, now: number): RoomStatus {
  let status = room.status;
  if (status === "countdown" && now >= room.startsAt) status = "racing";
  if (status === "racing" && room.closesAt > 0 && now >= room.closesAt) status = "finished";
  return status;
}

export function isLocked(room: Room, now: number) {
  return phase(room, now) === "countdown" && room.startsAt - now <= COUNTDOWN_MS;
}

export async function getRoom(ctx: QueryCtx, roomId: string) {
  const id = ctx.db.normalizeId("rooms", roomId);
  const room = id && (await ctx.db.get(id));
  if (!room) fail("NOT_FOUND", `Room ${roomId} not found`);
  return room;
}

export function roomPlayers(ctx: QueryCtx, roomId: Id<"rooms">) {
  return ctx.db
    .query("roomPlayers")
    .withIndex("by_room", (q) => q.eq("roomId", roomId))
    .collect();
}

export async function getMembership(ctx: QueryCtx, room: Room, playerId: Id<"players">) {
  const row = await ctx.db
    .query("roomPlayers")
    .withIndex("by_player", (q) => q.eq("playerId", playerId).eq("roomId", room._id))
    .unique();
  if (!row) fail("CONFLICT", `Player is not in room ${room.code}`);
  return row;
}

function distanceXZ(a: Vec3, b: Vec3) {
  return Math.hypot(a.x - b.x, a.z - b.z);
}

export function routeLength(route: Vec3[]) {
  let total = 0;
  for (let i = 1; i < route.length; i++) total += distanceXZ(route[i - 1], route[i]);
  return total;
}

/** Meters along the route (x/z) of the route point closest to `position`. */
export function routeProgress(route: Vec3[], position: Vec3) {
  let best = { distance: Infinity, progress: 0 };
  let along = 0;
  for (let i = 1; i < route.length; i++) {
    const a = route[i - 1];
    const b = route[i];
    const dx = b.x - a.x;
    const dz = b.z - a.z;
    const length = Math.hypot(dx, dz);
    const t =
      length === 0
        ? 0
        : Math.min(1, Math.max(0, ((position.x - a.x) * dx + (position.z - a.z) * dz) / (length * length)));
    const distance = Math.hypot(a.x + t * dx - position.x, a.z + t * dz - position.z);
    if (distance < best.distance) best = { distance, progress: along + t * length };
    along += length;
  }
  return best.progress;
}

export function finishPoint(room: Room) {
  return room.route[room.route.length - 1];
}

export function isAtFinish(room: Room, position: Vec3) {
  return distanceXZ(position, finishPoint(room)) <= room.finishRadius;
}

/** Finished racers by finish time, then racers still going by progress, then leavers; ties to the lower slot. */
export function placeOrder(rows: RoomPlayer[]) {
  const rank = (row: RoomPlayer) => (row.finishedAt > 0 ? 0 : row.left ? 2 : 1);
  return [...rows].sort(
    (a, b) =>
      rank(a) - rank(b) ||
      (rank(a) === 0 ? a.finishedAt - b.finishedAt : b.progress - a.progress) ||
      a.slot - b.slot,
  );
}

/** Recomputes the lobby countdown after a join, leave or ready change. */
export function countdownPatch(room: Room, rows: RoomPlayer[], now: number) {
  const ready = rows.filter((row) => row.ready).length;
  const running = room.status === "countdown";
  if (rows.length > 0 && rows.length >= room.minPlayers && ready === rows.length) {
    const startsAt = now + COUNTDOWN_MS;
    return { status: "countdown" as const, startsAt: running ? Math.min(room.startsAt, startsAt) : startsAt };
  }
  if (ready >= Math.max(2, room.minPlayers)) {
    return { status: "countdown" as const, startsAt: running ? room.startsAt : now + AUTO_START_MS };
  }
  return { status: "lobby" as const, startsAt: 0 };
}

/** A mission lobby counts down once it has minPlayers, restarting the countdown on every join or leave. */
export function missionPatch(room: Room, rows: RoomPlayer[], now: number) {
  if (rows.filter((row) => !row.left).length >= room.minPlayers) {
    return { status: "countdown" as const, startsAt: now + MISSION_COUNTDOWN_MS };
  }
  return { status: "lobby" as const, startsAt: 0 };
}

export function lobbyPatch(room: Room, rows: RoomPlayer[], now: number) {
  return room.kind === "mission" ? missionPatch(room, rows, now) : countdownPatch(room, rows, now);
}

/** Ends the race once nobody still in it is driving. */
export async function finishIfDone(ctx: MutationCtx, room: Room) {
  const rows = await roomPlayers(ctx, room._id);
  if (rows.every((row) => row.left || row.finishedAt > 0)) {
    await ctx.db.patch(room._id, { status: "finished" });
  }
}

/** Takes a player out of a room: removed before the race, marked as left (DNF) once it has started. */
export async function removeFromRoom(ctx: MutationCtx, room: Room, row: RoomPlayer, now: number) {
  const status = phase(room, now);
  if (status === "lobby" || status === "countdown") {
    await ctx.db.delete(row._id);
    await ctx.db.patch(room._id, lobbyPatch(room, await roomPlayers(ctx, room._id), now));
    return;
  }
  await ctx.db.patch(row._id, { left: true, ready: false, updatedAt: now });
  if (status === "racing") await finishIfDone(ctx, room);
}

export function toRoom(room: Room, rows: RoomPlayer[], now: number) {
  return {
    id: room._id,
    code: room.code,
    status: phase(room, now),
    raceNumber: room.raceNumber,
    minPlayers: room.minPlayers,
    playerCount: rows.filter((row) => !row.left).length,
    readyCount: rows.filter((row) => row.ready).length,
    startsAt: room.startsAt,
    closesAt: room.closesAt,
    routeLength: room.routeLength,
    finish: finishPoint(room),
    finishRadius: room.finishRadius,
    createdAt: room.createdAt,
    asks: startingAsks(room),
    pickups: room.pickups ?? [],
    pickupRadius: room.pickupRadius ?? DEFAULT_PICKUP_RADIUS,
  };
}

export function startingAsks(room: Room) {
  return room.asks ?? DEFAULT_ASKS;
}

/** The racer's asks; rows from before asks existed start with the room's count. */
export function asksOf(room: Room, row: RoomPlayer) {
  return row.asks ?? startingAsks(room);
}

export function finishMs(room: Room, row: RoomPlayer) {
  return row.finishedAt > 0 ? row.finishedAt - room.startsAt : 0;
}

export function toRacer(room: Room, row: RoomPlayer, place: number) {
  return {
    playerId: row.playerId,
    displayName: row.displayName,
    ...COLORS[row.slot],
    slot: row.slot,
    ready: row.ready,
    position: row.position,
    heading: row.heading,
    progress: row.progress,
    metersToFinish: Math.max(0, room.routeLength - row.progress),
    place,
    finished: row.finishedAt > 0,
    finishMs: finishMs(room, row),
    smash: row.smash ?? 0,
    asks: asksOf(room, row),
    collected: row.collected ?? [],
    left: row.left,
    updatedAt: row.updatedAt,
  };
}

const PLACE_AWARDS = ["FIRST TO YC", "SO CLOSE", "PODIUM FINISH", "SCENIC ROUTE"];

/** One award per racer, from rows already in place order. */
export function results(room: Room, ordered: RoomPlayer[], calls: Map<Id<"players">, number>) {
  const callsOf = (row: RoomPlayer) => calls.get(row.playerId) ?? 0;
  const smashOf = (row: RoomPlayer) => row.smash ?? 0;
  const leader = (score: (row: RoomPlayer) => number) => {
    const most = Math.max(0, ...ordered.map(score));
    return most > 0 ? ordered.find((row) => score(row) === most) : undefined;
  };
  const chaosCaller = leader(callsOf);
  const wreckingBall = leader(smashOf);
  const photoFinish = (i: number) =>
    [ordered[i - 1], ordered[i + 1]].some(
      (other) =>
        other &&
        other.finishedAt > 0 &&
        ordered[i].finishedAt > 0 &&
        Math.abs(other.finishedAt - ordered[i].finishedAt) <= PHOTO_FINISH_MS,
    );

  return ordered.map((row, i) => ({
    place: i + 1,
    playerId: row.playerId,
    displayName: row.displayName,
    color: COLORS[row.slot].color,
    finished: row.finishedAt > 0,
    finishMs: finishMs(room, row),
    calls: callsOf(row),
    smash: smashOf(row),
    award:
      row === chaosCaller
        ? "CHAOS CALLER"
        : row === wreckingBall
          ? "WRECKING BALL"
          : photoFinish(i)
          ? "PHOTO FINISH"
          : row.finishedAt > 0
            ? PLACE_AWARDS[i]
            : "STILL DRIVING",
  }));
}
