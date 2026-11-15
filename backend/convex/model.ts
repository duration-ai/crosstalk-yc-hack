import { ConvexError, v, type Infer } from "convex/values";
import type { Doc, Id } from "./_generated/dataModel";
import type { MutationCtx, QueryCtx } from "./_generated/server";

export const vec3 = v.object({ x: v.number(), y: v.number(), z: v.number() });
export const movementMode = v.union(v.literal("onFoot"), v.literal("driving"));

export type ErrorCode = "INVALID_ARGUMENT" | "NOT_FOUND" | "CONFLICT";

export function fail(code: ErrorCode, message: string): never {
  throw new ConvexError({ code, message });
}

export async function getPlayer(ctx: QueryCtx, playerId: string) {
  const id = ctx.db.normalizeId("players", playerId);
  const player = id && (await ctx.db.get(id));
  if (!player) fail("NOT_FOUND", `Player ${playerId} not found`);
  return player;
}

export async function getVehicle(ctx: QueryCtx, vehicleId: string) {
  const id = ctx.db.normalizeId("vehicles", vehicleId);
  const vehicle = id && (await ctx.db.get(id));
  if (!vehicle) fail("NOT_FOUND", `Vehicle ${vehicleId} not found`);
  return vehicle;
}

export function findPlayerState(ctx: QueryCtx, playerId: Id<"players">) {
  return ctx.db
    .query("playerStates")
    .withIndex("by_player", (q) => q.eq("playerId", playerId))
    .unique();
}

type PlayerStateFields = Omit<Doc<"playerStates">, "_id" | "_creationTime" | "playerId" | "updatedAt">;

export async function writePlayerState(
  ctx: MutationCtx,
  playerId: Id<"players">,
  fields: PlayerStateFields,
) {
  const existing = await findPlayerState(ctx, playerId);
  const next = { ...fields, playerId, updatedAt: Date.now() };
  const id = existing
    ? (await ctx.db.replace(existing._id, next), existing._id)
    : await ctx.db.insert("playerStates", next);
  return (await ctx.db.get(id))!;
}

export function toPlayer(player: Doc<"players">) {
  return { id: player._id, displayName: player.displayName, createdAt: player.createdAt };
}

export function toPlayerState(state: Doc<"playerStates">) {
  return {
    playerId: state.playerId,
    position: state.position,
    heading: state.heading,
    mode: state.mode,
    vehicleId: state.vehicleId ?? null,
    updatedAt: state.updatedAt,
  };
}

export function toVehicle(vehicle: Doc<"vehicles">) {
  return {
    id: vehicle._id,
    model: vehicle.model,
    position: vehicle.position,
    heading: vehicle.heading,
    occupiedBy: vehicle.occupiedBy ?? null,
    updatedAt: vehicle.updatedAt,
  };
}

export type Vec3 = Infer<typeof vec3>;
