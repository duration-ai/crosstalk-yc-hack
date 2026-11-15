import { v } from "convex/values";
import { internalMutation, mutation } from "./_generated/server";
import { fail, getPlayer, vec3 } from "./model";
import {
  CLOSE_AFTER_FIRST_FINISH_MS,
  COLLECT_SLACK,
  DEFAULT_PICKUP_RADIUS,
  asksOf,
  finishIfDone,
  finishMs,
  getMembership,
  getRoom,
  isAtFinish,
  phase,
  routeProgress,
} from "./raceModel";

export const update = mutation({
  args: {
    playerId: v.string(),
    roomId: v.string(),
    position: vec3,
    heading: v.number(),
    smash: v.optional(v.number()),
  },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const room = await getRoom(ctx, args.roomId);
    const row = await getMembership(ctx, room, player._id);
    if (row.left) fail("CONFLICT", "Player left this room");
    if (args.smash !== undefined && !(Number.isInteger(args.smash) && args.smash >= 0)) {
      fail("INVALID_ARGUMENT", "smash must be a non-negative integer");
    }

    const now = Date.now();
    const status = phase(room, now);
    const progress = routeProgress(room.route, args.position);
    const finishing = status === "racing" && row.finishedAt === 0 && isAtFinish(room, args.position);
    const next = {
      ...row,
      position: args.position,
      heading: args.heading,
      progress: row.finishedAt > 0 || status === "finished" ? row.progress : progress,
      finishedAt: finishing ? now : row.finishedAt,
      smash: status === "racing" ? Math.max(row.smash ?? 0, args.smash ?? 0) : row.smash,
      updatedAt: now,
    };
    await ctx.db.replace(row._id, next);

    if (finishing) {
      if (room.closesAt === 0) await ctx.db.patch(room._id, { closesAt: now + CLOSE_AFTER_FIRST_FINISH_MS });
      await finishIfDone(ctx, room);
    }

    return {
      status: finishing ? phase((await ctx.db.get(room._id))!, now) : status,
      progress: next.progress,
      metersToFinish: Math.max(0, room.routeLength - next.progress),
      finished: next.finishedAt > 0,
      finishMs: finishMs(room, next),
    };
  },
});

/**
 * Collects ask pickup `pickup` (an index into room.pickups) for one more Operator request. Only while
 * racing, from within the pickup's radius; each racer can take each pickup once a race, and taking one
 * again answers collected: false.
 */
export const collect = mutation({
  args: { playerId: v.string(), roomId: v.string(), pickup: v.number(), position: vec3 },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const room = await getRoom(ctx, args.roomId);
    const row = await getMembership(ctx, room, player._id);
    if (row.left) fail("CONFLICT", "Player left this room");
    if (phase(room, Date.now()) !== "racing") fail("CONFLICT", "Pickups only count while racing");
    const pickups = room.pickups ?? [];
    if (!Number.isInteger(args.pickup) || args.pickup < 0 || args.pickup >= pickups.length) {
      fail("INVALID_ARGUMENT", "pickup must index room.pickups");
    }
    const asks = asksOf(room, row);
    const collected = row.collected ?? [];
    if (collected.includes(args.pickup)) return { collected: false, asks };
    const at = pickups[args.pickup];
    const reach = (room.pickupRadius ?? DEFAULT_PICKUP_RADIUS) + COLLECT_SLACK;
    if (Math.hypot(args.position.x - at.x, args.position.z - at.z) > reach) fail("CONFLICT", "Too far from the pickup");
    await ctx.db.patch(row._id, { asks: asks + 1, collected: [...collected, args.pickup] });
    return { collected: true, asks: asks + 1 };
  },
});

/**
 * Spends one of the racer's asks for an Operator request; CONFLICT when none are left. operator.call runs
 * it. A freeAsks room (the voice Operator's solo room, test rooms) never runs out.
 */
export const spendAsk = internalMutation({
  args: { playerId: v.id("players"), roomId: v.id("rooms") },
  handler: async (ctx, args) => {
    const room = await getRoom(ctx, args.roomId);
    const row = await getMembership(ctx, room, args.playerId);
    if (row.left) fail("CONFLICT", "Player left this room");
    if (phase(room, Date.now()) === "finished") fail("CONFLICT", "The race is over");
    const asks = asksOf(room, row);
    if (room.freeAsks) return { asks };
    if (asks <= 0) fail("CONFLICT", "No asks left");
    await ctx.db.patch(row._id, { asks: asks - 1 });
    return { asks: asks - 1 };
  },
});
