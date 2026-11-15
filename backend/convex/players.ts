import { v } from "convex/values";
import { mutation, query } from "./_generated/server";
import { fail, getPlayer, toPlayer } from "./model";

export const create = mutation({
  args: { displayName: v.string() },
  handler: async (ctx, { displayName }) => {
    const name = displayName.trim();
    if (name.length < 1 || name.length > 32) {
      fail("INVALID_ARGUMENT", "displayName must be 1 to 32 characters");
    }
    const id = await ctx.db.insert("players", { displayName: name, createdAt: Date.now() });
    return toPlayer((await ctx.db.get(id))!);
  },
});

export const get = query({
  args: { playerId: v.string() },
  handler: async (ctx, { playerId }) => toPlayer(await getPlayer(ctx, playerId)),
});
