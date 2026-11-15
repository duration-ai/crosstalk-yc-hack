import { v } from "convex/values";
import { internalMutation } from "./_generated/server";
import { getPlayer, vec3 } from "./model";
import { COLORS, MAX_PLAYERS, isLocked, missionPatch, phase, roomPlayers } from "./raceModel";
import { addRacer, insertRoom, joinedResponse, leaveOtherRooms } from "./rooms";
import { findPresence } from "./world";

const CANDIDATES = 30;

// Stepping into a mission's ring: the player joins the open race room for that mission in their
// world shard, or opens one. The room counts down once it has minPlayers (default 2) and restarts
// the countdown on every join, so the race launches through the ordinary race room.
export const enter = internalMutation({
  args: {
    playerId: v.string(),
    mission: v.string(),
    route: v.optional(v.array(vec3)),
    finishRadius: v.optional(v.number()),
    minPlayers: v.optional(v.number()),
    asks: v.optional(v.number()),
    pickups: v.optional(v.array(vec3)),
    pickupRadius: v.optional(v.number()),
  },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const now = Date.now();
    const shard = (await findPresence(ctx, player._id))?.shard ?? 0;
    const recent = await ctx.db
      .query("rooms")
      .withIndex("by_world_shard", (q) => q.eq("worldShard", shard))
      .order("desc")
      .filter((q) => q.and(q.eq(q.field("kind"), "mission"), q.eq(q.field("mission"), args.mission)))
      .take(CANDIDATES);

    for (const room of recent) {
      const status = phase(room, now);
      if (status !== "lobby" && status !== "countdown") continue;
      const rows = await roomPlayers(ctx, room._id);
      const mine = rows.find((row) => row.playerId === player._id && !row.left);
      if (mine) return joinedResponse(ctx, room._id, player._id, now);
      if (isLocked(room, now) || rows.length >= MAX_PLAYERS) continue;
      await leaveOtherRooms(ctx, player._id, room._id, now);
      const taken = new Set(rows.map((row) => row.slot));
      await addRacer(ctx, room, player, COLORS.findIndex((_, i) => !taken.has(i)), now);
      await ctx.db.patch(room._id, missionPatch(room, await roomPlayers(ctx, room._id), now));
      return joinedResponse(ctx, room._id, player._id, now);
    }

    await leaveOtherRooms(ctx, player._id, null, now);
    const roomId = await insertRoom(ctx, args, now, { kind: "mission", mission: args.mission, worldShard: shard });
    const room = (await ctx.db.get(roomId))!;
    await addRacer(ctx, room, player, 0, now);
    await ctx.db.patch(roomId, missionPatch(room, await roomPlayers(ctx, roomId), now));
    return joinedResponse(ctx, roomId, player._id, now);
  },
});
