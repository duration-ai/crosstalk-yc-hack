import { v } from "convex/values";
import { mutation, query } from "./_generated/server";
import { COLORS } from "./raceModel";
import { pollRoom } from "./rooms";
import { beat, others, presenceArgs } from "./world";

// Public functions for the Unity client's Convex WebSocket (ConvexSocket.cs), the pushed
// counterparts of POST /api/world/presence and GET /api/rooms/poll. The presence mutation only
// writes this player's row and returns no other players, so heartbeats never read each other's
// rows and never conflict; everyone else arrives through the world subscription.
export const presence = mutation({
  args: presenceArgs,
  handler: async (ctx, args) => {
    const now = Date.now();
    const me = await beat(ctx, { ...args, now });
    return { serverTime: now, shard: me.shard, worldRoomId: me.worldRoomId, you: COLORS[me.slot % COLORS.length], players: [] };
  },
});

export const world = query({
  args: { shard: v.number(), playerId: v.string() },
  handler: async (ctx, args) => {
    const now = Date.now();
    return { serverTime: now, players: await others(ctx, { ...args, now }) };
  },
});

export const room = query({
  args: { roomId: v.string() },
  handler: (ctx, args) => pollRoom(ctx, { roomId: args.roomId, now: Date.now() }),
});
