import { v } from "convex/values";
import { internalMutation, mutation, query } from "./_generated/server";
import {
  fail,
  getPlayer,
  getVehicle,
  toPlayerState,
  toVehicle,
  vec3,
  writePlayerState,
} from "./model";

export const list = query({
  args: {},
  handler: async (ctx) => (await ctx.db.query("vehicles").collect()).map(toVehicle),
});

export const enter = mutation({
  args: { playerId: v.string(), vehicleId: v.string() },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const vehicle = await getVehicle(ctx, args.vehicleId);
    if (vehicle.occupiedBy && vehicle.occupiedBy !== player._id) {
      fail("CONFLICT", `Vehicle ${vehicle._id} is occupied by another player`);
    }
    const current = await ctx.db
      .query("vehicles")
      .withIndex("by_occupant", (q) => q.eq("occupiedBy", player._id))
      .first();
    if (current && current._id !== vehicle._id) {
      fail("CONFLICT", `Player is already in vehicle ${current._id}; exit it first`);
    }

    const updatedAt = Date.now();
    await ctx.db.patch(vehicle._id, { occupiedBy: player._id, updatedAt });
    const state = await writePlayerState(ctx, player._id, {
      position: vehicle.position,
      heading: vehicle.heading,
      mode: "driving",
      vehicleId: vehicle._id,
    });
    return {
      vehicle: toVehicle({ ...vehicle, occupiedBy: player._id, updatedAt }),
      state: toPlayerState(state),
    };
  },
});

export const exit = mutation({
  args: {
    playerId: v.string(),
    vehicleId: v.string(),
    position: v.optional(vec3),
    heading: v.optional(v.number()),
  },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const vehicle = await getVehicle(ctx, args.vehicleId);
    if (vehicle.occupiedBy !== player._id) {
      fail("CONFLICT", `Player is not in vehicle ${vehicle._id}`);
    }

    const updatedAt = Date.now();
    await ctx.db.patch(vehicle._id, { occupiedBy: undefined, updatedAt });
    const state = await writePlayerState(ctx, player._id, {
      position: args.position ?? vehicle.position,
      heading: args.heading ?? vehicle.heading,
      mode: "onFoot",
    });
    return {
      vehicle: toVehicle({ ...vehicle, occupiedBy: undefined, updatedAt }),
      state: toPlayerState(state),
    };
  },
});

export const update = mutation({
  args: {
    playerId: v.string(),
    vehicleId: v.string(),
    position: vec3,
    heading: v.number(),
  },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const vehicle = await getVehicle(ctx, args.vehicleId);
    if (vehicle.occupiedBy !== player._id) {
      fail("CONFLICT", `Only the driver can update vehicle ${vehicle._id}`);
    }

    const updatedAt = Date.now();
    await ctx.db.patch(vehicle._id, { position: args.position, heading: args.heading, updatedAt });
    const state = await writePlayerState(ctx, player._id, {
      position: args.position,
      heading: args.heading,
      mode: "driving",
      vehicleId: vehicle._id,
    });
    return {
      vehicle: toVehicle({ ...vehicle, position: args.position, heading: args.heading, updatedAt }),
      state: toPlayerState(state),
    };
  },
});

export const seed = internalMutation({
  args: {
    vehicles: v.array(v.object({ model: v.string(), position: vec3, heading: v.number() })),
  },
  handler: async (ctx, args) => {
    if (await ctx.db.query("vehicles").first()) return { inserted: 0 };
    const updatedAt = Date.now();
    for (const vehicle of args.vehicles) {
      await ctx.db.insert("vehicles", { ...vehicle, updatedAt });
    }
    return { inserted: args.vehicles.length };
  },
});
