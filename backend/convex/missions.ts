import { v } from "convex/values";
import { internalMutation } from "./_generated/server";
import { getPlayer, vec3 } from "./model";
import { COLORS, MAX_PLAYERS, isLocked, missionPatch, phase, roomPlayers } from "./raceModel";
import { addRacer, insertRoom, joinedResponse, leaveOtherRooms } from "./rooms";
import { findPresence } from "./world";

const CANDIDATES = 30;

// Stepping into a mission's ring: the player joins the open race room for that mission in their
// world shard, or opens one. The room counts down once it has minPlayers (default 2) and restarts
// the countdown on every join, so the race launches through the ordinary race room. A racer takes
// the slot of their world colour when it is free, so their colour doesn't change in the race.
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
    for (const row of await ctx.db.query("roomPlayers").withIndex("by_player", (q) => q.eq("playerId", player._id)).collect()) {
      if (row.left) continue;
      const room = (await ctx.db.get(row.roomId))!;
      if (room.kind === "mission" && room.mission === args.mission && phase(room, now) !== "finished") {
        return joinedResponse(ctx, room._id, player._id, now);
      }
    }
    const presence = await findPresence(ctx, player._id);
    const shard = presence?.shard ?? 0;
    const color = (presence?.slot ?? 0) % COLORS.length;
    const slotFor = (taken: Set<number>) => (taken.has(color) ? COLORS.findIndex((_, i) => !taken.has(i)) : color);
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
      if (isLocked(room, now) || rows.length >= MAX_PLAYERS) continue;
      await leaveOtherRooms(ctx, player._id, room._id, now);
      const taken = new Set(rows.map((row) => row.slot));
      await addRacer(ctx, room, player, slotFor(taken), now);
      await ctx.db.patch(room._id, missionPatch(room, await roomPlayers(ctx, room._id), now));
      return joinedResponse(ctx, room._id, player._id, now);
    }

    await leaveOtherRooms(ctx, player._id, null, now);
    const roomId = await insertRoom(ctx, args, now, { kind: "mission", mission: args.mission, worldShard: shard });
    const room = (await ctx.db.get(roomId))!;
    await addRacer(ctx, room, player, color, now);
    await ctx.db.patch(roomId, missionPatch(room, await roomPlayers(ctx, roomId), now));
    return joinedResponse(ctx, roomId, player._id, now);
  },
});
