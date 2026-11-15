import { v } from "convex/values";
import type { Doc } from "./_generated/dataModel";
import { internalMutation, query } from "./_generated/server";
import { fail } from "./model";

export const PLAYER_ID = /^[A-Za-z0-9_\-:.]{1,64}$/;

export function checkPlayerId(playerId: string) {
  if (!PLAYER_ID.test(playerId)) fail("INVALID_ARGUMENT", "playerId must be 1 to 64 characters of letters, digits, _ - : .");
}

export const avatarParams = v.object({
  base: v.string(),
  outfit: v.string(),
  costume: v.union(v.string(), v.null()),
  figure: v.union(v.string(), v.null()),
  age: v.union(v.string(), v.null()),
  build: v.union(v.number(), v.null()),
  expression: v.union(v.string(), v.null()),
  glbUrl: v.string(),
  fallbackGlbUrl: v.string(),
  recolor: v.array(v.object({ zone: v.string(), from: v.string(), to: v.string() })),
  swatches: v.object({
    skin: v.string(),
    hair: v.string(),
    top: v.string(),
    outer: v.string(),
    bottoms: v.string(),
    headwear: v.string(),
  }),
});

export const decisionSummary = v.object({
  provider: v.string(),
  latencyMs: v.number(),
  hedged: v.boolean(),
  realPerson: v.number(),
  costume: v.string(),
  costumeProbability: v.number(),
});

export function toAvatar(doc: Doc<"avatars">) {
  return { playerId: doc.playerId, ...doc.params, updatedAt: doc.updatedAt };
}

export const save = internalMutation({
  args: { playerId: v.string(), params: avatarParams, decision: decisionSummary },
  handler: async (ctx, { playerId, params, decision }) => {
    const existing = await ctx.db
      .query("avatars")
      .withIndex("by_player", (q) => q.eq("playerId", playerId))
      .unique();
    const next = { playerId, params, decision, updatedAt: Date.now() };
    const id = existing ? (await ctx.db.replace(existing._id, next), existing._id) : await ctx.db.insert("avatars", next);
    return toAvatar((await ctx.db.get(id))!);
  },
});

export const get = query({
  args: { playerId: v.string() },
  handler: async (ctx, { playerId }) => {
    checkPlayerId(playerId);
    const doc = await ctx.db
      .query("avatars")
      .withIndex("by_player", (q) => q.eq("playerId", playerId))
      .unique();
    return { avatar: doc ? toAvatar(doc) : null };
  },
});
