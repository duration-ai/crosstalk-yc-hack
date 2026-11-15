import { v } from "convex/values";
import type { Doc } from "./_generated/dataModel";
import { mutation } from "./_generated/server";
import { fail, getPlayer } from "./model";
import { COLORS, getMembership, getRoom, phase, roomPlayers } from "./raceModel";
import { newsText, normalizeWorldChange, worldChangeParams } from "./worldChanges";

export function toEvent(event: Doc<"roomEvents">) {
  return {
    seq: event.seq,
    raceNumber: event.raceNumber,
    type: event.type,
    params: event.params,
    playerId: event.playerId,
    displayName: event.displayName,
    color: COLORS[event.slot].color,
    serverTime: event.serverTime,
    news: event.news,
  };
}

/** Appends a world change to the room's log. The Operator action lands its command through this too. */
export const fire = mutation({
  args: { playerId: v.string(), roomId: v.string(), type: v.string(), params: worldChangeParams },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const room = await getRoom(ctx, args.roomId);
    const row = await getMembership(ctx, room, player._id);
    if (row.left) fail("CONFLICT", "Player left this room");
    const now = Date.now();
    if (phase(room, now) === "finished") fail("CONFLICT", "The race is over");

    const change = normalizeWorldChange(args.type, args.params, player._id);
    let targetName: string | undefined;
    if (change.type === "swap") {
      const target = (await roomPlayers(ctx, room._id)).find(
        (other) => other.playerId === change.params.targetPlayerId && !other.left,
      );
      if (!target) fail("INVALID_ARGUMENT", "params.targetPlayerId must be another racer in this room");
      targetName = target.displayName;
    }

    const seq = room.eventSeq + 1;
    await ctx.db.patch(room._id, { eventSeq: seq });
    const id = await ctx.db.insert("roomEvents", {
      roomId: room._id,
      seq,
      raceNumber: room.raceNumber,
      type: change.type,
      params: change.params,
      playerId: player._id,
      displayName: row.displayName,
      slot: row.slot,
      serverTime: now,
      news: newsText(change, row.displayName, targetName),
    });
    return toEvent((await ctx.db.get(id))!);
  },
});
