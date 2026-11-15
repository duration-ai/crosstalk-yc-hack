import { v } from "convex/values";
import { mutation } from "./_generated/server";
import { fail, getPlayer, vec3 } from "./model";
import {
  CLOSE_AFTER_FIRST_FINISH_MS,
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
