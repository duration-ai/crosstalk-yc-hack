import { v } from "convex/values";
import { mutation, query } from "./_generated/server";
import {
  fail,
  findPlayerState,
  getPlayer,
  getVehicle,
  movementMode,
  toPlayerState,
  vec3,
} from "./model";

export const load = query({
  args: { playerId: v.string() },
  handler: async (ctx, { playerId }) => {
    const player = await getPlayer(ctx, playerId);
    const state = await findPlayerState(ctx, player._id);
    return state && toPlayerState(state);
  },
});

export const save = mutation({
  args: {
    playerId: v.string(),
    position: vec3,
    heading: v.number(),
    mode: movementMode,
    vehicleId: v.optional(v.union(v.string(), v.null())),
  },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const occupied = await ctx.db
      .query("vehicles")
      .withIndex("by_occupant", (q) => q.eq("occupiedBy", player._id))
      .first();

    if (args.mode === "onFoot") {
      if (args.vehicleId) fail("INVALID_ARGUMENT", "vehicleId must be empty when mode is onFoot");
      if (occupied) fail("CONFLICT", `Player is driving vehicle ${occupied._id}; exit it first`);
    } else {
      if (!args.vehicleId) fail("INVALID_ARGUMENT", "vehicleId is required when mode is driving");
      const vehicle = await getVehicle(ctx, args.vehicleId);
      if (vehicle.occupiedBy !== player._id) {
        fail("CONFLICT", `Player is not in vehicle ${vehicle._id}; enter it first`);
      }
      await ctx.db.patch(vehicle._id, {
        position: args.position,
        heading: args.heading,
        updatedAt: Date.now(),
      });
    }

    const existing = await findPlayerState(ctx, player._id);
    const next = {
      playerId: player._id,
      position: args.position,
      heading: args.heading,
      mode: args.mode,
      vehicleId: occupied?._id,
      updatedAt: Date.now(),
    };
    const id = existing
      ? (await ctx.db.replace(existing._id, next), existing._id)
      : await ctx.db.insert("playerStates", next);
    return toPlayerState((await ctx.db.get(id))!);
  },
});
