import { v } from "convex/values";
import { api, internal } from "./_generated/api";
import { action, internalQuery } from "./_generated/server";
import { toEvent } from "./events";
import { fail, getPlayer } from "./model";
import { getMembership, getRoom, phase, placeOrder, roomPlayers } from "./raceModel";
import {
  configuredProvider,
  keywordProvider,
  type OperatorInput,
  type OperatorRacer,
  type OperatorReply,
} from "./operatorProviders";
import { normalizeWorldChange } from "./worldChanges";

const MAX_TEXT = 500;
const MAX_AUDIO_BYTES = 5 * 1024 * 1024;

export const context = internalQuery({
  args: { playerId: v.string(), roomId: v.string(), now: v.number() },
  handler: async (ctx, args): Promise<{ caller: OperatorRacer; racers: OperatorRacer[] }> => {
    const player = await getPlayer(ctx, args.playerId);
    const room = await getRoom(ctx, args.roomId);
    const row = await getMembership(ctx, room, player._id);
    if (row.left) fail("CONFLICT", "Player left this room");
    if (phase(room, args.now) === "finished") fail("CONFLICT", "The race is over");
    const racers = placeOrder(await roomPlayers(ctx, room._id)).flatMap((other, i) =>
      other.left ? [] : [{ playerId: other.playerId as string, displayName: other.displayName, place: i + 1 }],
    );
    return { caller: racers.find((racer) => racer.playerId === player._id)!, racers };
  },
});

export const call = action({
  args: {
    playerId: v.string(),
    roomId: v.string(),
    text: v.optional(v.string()),
    audio: v.optional(v.bytes()),
    mimeType: v.optional(v.string()),
  },
  handler: async (
    ctx,
    args,
  ): Promise<{
    line: string;
    command: { type: string; params: OperatorReply["command"]["params"] };
    event: ReturnType<typeof toEvent>;
    provider: string;
  }> => {
    const text = args.text?.trim();
    if (args.audio === undefined) {
      if (!text || text.length > MAX_TEXT) fail("INVALID_ARGUMENT", `text must be 1 to ${MAX_TEXT} characters`);
    } else if (args.audio.byteLength === 0 || args.audio.byteLength > MAX_AUDIO_BYTES) {
      fail("INVALID_ARGUMENT", "audio must be 1 byte to 5 MB");
    }

    const { caller, racers } = await ctx.runQuery(internal.operator.context, {
      playerId: args.playerId,
      roomId: args.roomId,
      now: Date.now(),
    });
    const input: OperatorInput = {
      text,
      audio: args.audio && { data: args.audio, mimeType: args.mimeType ?? "application/octet-stream" },
      caller,
      racers,
    };

    let provider = configuredProvider();
    if (input.audio && !provider.acceptsAudio) {
      fail("NOT_IMPLEMENTED", `The ${provider.name} Operator understands text only; send text or configure a voice provider`);
    }
    let reply: OperatorReply;
    try {
      reply = await provider.respond(input);
      normalizeWorldChange(reply.command.type, reply.command.params, caller.playerId);
      if (!reply.line?.trim()) throw new Error("Operator line is empty");
    } catch (error) {
      if (provider === keywordProvider || input.text === undefined) throw error;
      console.error(`Operator provider ${provider.name} failed; falling back to keyword`, error);
      provider = keywordProvider;
      reply = await provider.respond(input);
    }

    const event = await ctx.runMutation(api.events.fire, {
      playerId: args.playerId,
      roomId: args.roomId,
      type: reply.command.type,
      params: reply.command.params,
    });
    return { line: reply.line, command: { type: event.type, params: event.params }, event, provider: provider.name };
  },
});
