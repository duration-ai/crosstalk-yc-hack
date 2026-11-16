import { v } from "convex/values";
import { mutation, query } from "./_generated/server";
import { fail, getPlayer, toPlayer } from "./model";

function displayNameOf(displayName: string) {
  const name = displayName.trim();
  if (name.length < 1 || name.length > 32) {
    fail("INVALID_ARGUMENT", "displayName must be 1 to 32 characters");
  }
  return name;
}

export const create = mutation({
  args: { displayName: v.string() },
  handler: async (ctx, { displayName }) => {
    const id = await ctx.db.insert("players", { displayName: displayNameOf(displayName), createdAt: Date.now() });
    return toPlayer((await ctx.db.get(id))!);
  },
});

export const get = query({
  args: { playerId: v.string() },
  handler: async (ctx, { playerId }) => toPlayer(await getPlayer(ctx, playerId)),
});

// Keeps the player's id; the rooms they are still in and their world presence show the new name
// at once. Past events keep the name they were fired under.
export const rename = mutation({
  args: { playerId: v.string(), displayName: v.string() },
  handler: async (ctx, { playerId, displayName }) => {
    const name = displayNameOf(displayName);
    const player = await getPlayer(ctx, playerId);
    await ctx.db.patch(player._id, { displayName: name });
    const seats = await ctx.db
      .query("roomPlayers")
      .withIndex("by_player", (q) => q.eq("playerId", player._id))
      .collect();
    for (const seat of seats) if (!seat.left) await ctx.db.patch(seat._id, { displayName: name });
    const presence = await ctx.db
      .query("worldPresence")
      .withIndex("by_player", (q) => q.eq("playerId", player._id))
      .unique();
    if (presence) await ctx.db.patch(presence._id, { displayName: name });
    return toPlayer((await ctx.db.get(player._id))!);
  },
});
