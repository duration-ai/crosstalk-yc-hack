import { ConvexError, v } from "convex/values";
import type { Doc, Id } from "./_generated/dataModel";
import { internal } from "./_generated/api";
import { action, internalMutation, internalQuery, mutation, query, type MutationCtx, type QueryCtx } from "./_generated/server";
import { askJev, buildInput, buildQuestions, JevUnavailable } from "./jev";
import { fail, getPlayer, movementMode } from "./model";
import { COUNTERS, EXCLUSIVE, SELF_EFFECTS, effectInfo, isA, rollDuration } from "./operatorCatalogue";
import { CAR_EFFECTS, decide, forEveryone, nextBest, REFUSAL_MESSAGES, standInLine, type Answers, type Command, type EffectCommand, type RefusalReason, type View } from "./operatorRules";
import { getMembership, getRoom, phase } from "./raceModel";

const MAX_TRANSCRIPT = 300;
const MAX_ENTITIES = 8;
const CONFIRM_WINDOW_MS = 10_000;
/** Instant effects stay listed this long so every subscribed client sees them once. */
const INSTANT_WINDOW_MS = 3_000;
const TOKEN_MINTS_PER_MINUTE = 10;

const viewEntity = v.object({
  id: v.string(),
  class: v.string(),
  desc: v.optional(v.string()),
  color: v.optional(v.string()),
  screen: v.optional(v.string()),
  distance_m: v.optional(v.number()),
  view_rank: v.optional(v.number()),
  protected: v.optional(v.boolean()),
  controlledBy: v.optional(v.string()),
});

export const viewValidator = v.object({
  mode: movementMode,
  timeOfDay: v.optional(v.string()),
  weather: v.optional(v.string()),
  entities: v.array(viewEntity),
});

const timingsValidator = v.object({
  jevMs: v.number(),
  attempts: v.number(),
  sttMs: v.optional(v.number()),
  startedAt: v.number(),
});

type ViewArg = typeof viewValidator.type;
type Row = Doc<"activeEffects">;
type EffectFields = Omit<Row, "_id" | "_creationTime" | "status" | "startsAt" | "endsAt" | "confirmBy" | "expiryJob" | "endedBy">;

function normalizeView(view: ViewArg): View {
  return {
    mode: view.mode,
    timeOfDay: view.timeOfDay ?? "",
    weather: view.weather ?? "",
    entities: view.entities.slice(0, MAX_ENTITIES).map((e, i) => ({
      id: e.id,
      class: e.class,
      desc: e.desc ?? "",
      color: e.color ?? "",
      screen: e.screen ?? "",
      distance_m: e.distance_m ?? 0,
      view_rank: e.view_rank ?? i + 1,
      protected: e.protected ?? false,
      controlledBy: e.controlledBy ?? "",
    })),
  };
}

export function toEffect(row: Row) {
  return {
    id: row._id,
    effect: row.effect,
    scope: row.scope,
    target: row.target,
    targetClass: row.targetClass,
    strength: row.strength,
    color: row.color,
    theme: row.theme,
    vehicle: row.vehicle ?? "",
    instant: row.instant,
    status: row.status,
    startsAt: row.startsAt,
    endsAt: row.endsAt,
    durationS: row.durationS,
    label: effectInfo(row.effect)?.label ?? row.effect,
    requestedBy: row.requestedBy as string,
    transcript: row.transcript,
  };
}

export type ActiveEffect = ReturnType<typeof toEffect>;

export type CallResult = {
  outcome: "applied" | "pending" | "refused";
  effect: ActiveEffect | null;
  refusal: { reason: string; message: string } | null;
  line: string;
  serverNow: number;
  timings: { jevMs: number; totalMs: number; attempts: number };
};

async function activeMembership(ctx: QueryCtx, room: Doc<"rooms">, playerId: Id<"players">) {
  const row = await getMembership(ctx, room, playerId);
  if (row.left) fail("CONFLICT", "Player left this room");
}

function activeRows(ctx: MutationCtx, roomId: Id<"rooms">) {
  return ctx.db
    .query("activeEffects")
    .withIndex("by_room_status", (q) => q.eq("roomId", roomId).eq("status", "active"))
    .collect();
}

/** OPERATOR_SELF_SCOPE (default true): car and avatar changes land only on the caller; the environment stays shared. */
const operatorSelfScope = () => process.env.OPERATOR_SELF_SCOPE !== "false";

/**
 * SELF effects tag the caller ("self:<playerId>"); car effects aimed at a whole class of vehicles or
 * at another racer's car go on the caller's own car instead, with no confirm.
 */
export function selfScoped(command: EffectCommand, playerId: string, everyone = false): EffectCommand {
  const own = `car:${playerId}`;
  if (everyone && (SELF_EFFECTS.has(command.effect) || CAR_EFFECTS.has(command.effect))) {
    return { ...command, scope: "players", target: "", targetClass: "", confirm: false, who: "EVERYONE" };
  }
  if (SELF_EFFECTS.has(command.effect) && (command.scope === "players" || command.scope === "world")) {
    return { ...command, target: `self:${playerId}`, targetClass: "", confirm: false };
  }
  if (!CAR_EFFECTS.has(command.effect)) return command;
  if (command.scope === "class" && isA(command.targetClass, "vehicle")) return { ...command, scope: "object", target: own, targetClass: "car", confirm: false };
  if (command.scope === "object" && command.target.startsWith("car:")) return { ...command, target: own, confirm: false };
  return command;
}

/** OPERATOR_ANYWHERE (default true, for testing): calls outside a running race are free and never refused as race_over. */
const operatorAnywhere = () => process.env.OPERATOR_ANYWHERE !== "false";

/** Charges the caller one ask (internal.race.spendAsk) for an effect about to go live; the refusal when it can't. */
async function chargeAsk(ctx: MutationCtx, room: Doc<"rooms">, playerId: Id<"players">, now: number): Promise<RefusalReason | null> {
  const at = phase(room, now);
  if (operatorAnywhere() && at !== "racing") return null;
  if (at === "finished") return "race_over";
  try {
    await ctx.runMutation(internal.race.spendAsk, { roomId: room._id, playerId });
    return null;
  } catch (caught) {
    if (caught instanceof ConvexError && caught.data?.code === "CONFLICT") return "no_asks";
    throw caught;
  }
}

async function end(ctx: MutationCtx, row: Row, endedBy: string) {
  if (row.expiryJob) await ctx.scheduler.cancel(row.expiryJob);
  await ctx.db.patch(row._id, { status: "expired", endedBy });
}

async function schedule(ctx: MutationCtx, id: Id<"activeEffects">, endsAt: number) {
  const job = await ctx.scheduler.runAt(endsAt, internal.operator.expire, { effectId: id, endsAt });
  await ctx.db.patch(id, { expiryJob: job });
}

/** Makes an effect live: counters and contradicting effects end what they replace, and a repeat extends. */
async function activate(ctx: MutationCtx, fields: EffectFields, now: number, pendingId?: Id<"activeEffects">) {
  const active = await activeRows(ctx, fields.roomId);
  const sameTarget = (row: Row) => row.target === fields.target && row.targetClass === fields.targetClass;

  const countered = COUNTERS[fields.effect];
  if (countered) {
    for (const row of active) if (countered.includes(row.effect)) await end(ctx, row, "countered");
  }
  if (fields.effect === "extinguish") {
    const fires = active.filter((row) => row.effect === "set_fire");
    const doused = fields.target
      ? (fires.find((row) => row.target === fields.target) ?? fires.find((row) => row.target === "" && isA(fields.targetClass, row.targetClass)))
      : undefined;
    for (const row of fires) {
      const overlaps = fields.target
        ? row === doused
        : isA(row.targetClass, fields.targetClass) || (row.target === "" && isA(fields.targetClass, row.targetClass));
      if (overlaps) await end(ctx, row, "extinguished");
    }
  }
  if (!fields.instant) {
    const repeat = active.find((row) => row.effect === fields.effect && sameTarget(row));
    if (repeat) {
      if (pendingId) await ctx.db.patch(pendingId, { status: "expired", endedBy: "merged", expiryJob: undefined });
      if (repeat.expiryJob) await ctx.scheduler.cancel(repeat.expiryJob);
      const endsAt = Math.max(repeat.endsAt, now) + fields.durationS * 1000;
      await ctx.db.patch(repeat._id, {
        endsAt,
        durationS: repeat.durationS + fields.durationS,
        strength: fields.strength,
        theme: fields.theme,
      });
      await schedule(ctx, repeat._id, endsAt);
      return (await ctx.db.get(repeat._id))!;
    }
    const rivals = EXCLUSIVE.find((set) => set.includes(fields.effect)) ?? [];
    for (const row of active) {
      if (row.effect !== fields.effect && rivals.includes(row.effect) && sameTarget(row)) await end(ctx, row, "replaced");
    }
  }

  const endsAt = now + (fields.instant ? INSTANT_WINDOW_MS : fields.durationS * 1000);
  const live = { status: "active" as const, startsAt: now, endsAt, confirmBy: 0 };
  let id = pendingId;
  if (id) await ctx.db.patch(id, live);
  else id = await ctx.db.insert("activeEffects", { ...fields, ...live });
  await schedule(ctx, id, endsAt);
  return (await ctx.db.get(id))!;
}

export const context = internalQuery({
  args: { playerId: v.string(), roomId: v.string(), now: v.number() },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const room = await getRoom(ctx, args.roomId);
    await activeMembership(ctx, room, player._id);
    const active = await ctx.db
      .query("activeEffects")
      .withIndex("by_room_status", (q) => q.eq("roomId", room._id).eq("status", "active"))
      .collect();
    return {
      playerId: player._id,
      roomId: room._id,
      active: active.map((e) => ({
        effect: e.effect,
        target: e.target,
        targetClass: e.targetClass,
        instant: e.instant,
        secondsLeft: Math.max(0, Math.round((e.endsAt - args.now) / 1000)),
      })),
    };
  },
});

export const call = action({
  args: {
    roomId: v.string(),
    playerId: v.string(),
    transcript: v.string(),
    view: viewValidator,
    clientTimings: v.optional(v.object({ sttMs: v.optional(v.number()) })),
  },
  handler: async (ctx, args): Promise<CallResult> => {
    const startedAt = Date.now();
    const transcript = args.transcript.trim();
    if (!transcript || transcript.length > MAX_TRANSCRIPT) {
      fail("INVALID_ARGUMENT", `transcript must be 1 to ${MAX_TRANSCRIPT} characters`);
    }
    const view = normalizeView(args.view);
    const { playerId, roomId, active } = await ctx.runQuery(internal.operator.context, {
      playerId: args.playerId,
      roomId: args.roomId,
      now: startedAt,
    });
    const input = buildInput(transcript, view, active);

    let command: Command;
    let answers: unknown = null;
    let error: string | undefined;
    let jevMs = 0;
    let attempts = 0;
    try {
      const apiKey = process.env.AI_GATEWAY_API_KEY;
      if (!apiKey) throw new JevUnavailable("AI_GATEWAY_API_KEY is not set");
      const jev = await askJev(apiKey, input, buildQuestions(view));
      ({ answers, jevMs, attempts } = jev);
      command = nextBest(jev.answers, decide(jev.answers, view, active), transcript);
    } catch (caught) {
      if (!(caught instanceof JevUnavailable)) throw caught;
      error = caught.message;
      jevMs = Date.now() - startedAt;
      attempts = 2;
      command = { kind: "refuse", reason: "unavailable" };
    }
    if (command.kind === "effect" && command.target === "veh_me") command = { ...command, target: `car:${playerId}` };
    if (command.kind === "effect" && operatorSelfScope()) command = selfScoped(command, playerId, !!answers && forEveryone(answers as Answers));
    if (command.kind === "effect" && !command.who && (command.target.startsWith("self:") || command.target === `car:${playerId}`)) command = { ...command, who: "ME" };

    const result: CallResult = await ctx.runMutation(internal.operator.apply, {
      roomId,
      playerId,
      transcript,
      view,
      input,
      answers,
      command,
      error,
      timings: { jevMs, attempts, sttMs: args.clientTimings?.sttMs, startedAt },
    });
    return result;
  },
});

export const apply = internalMutation({
  args: {
    roomId: v.id("rooms"),
    playerId: v.id("players"),
    transcript: v.string(),
    view: v.any(),
    input: v.string(),
    answers: v.any(),
    command: v.any(),
    error: v.optional(v.string()),
    timings: timingsValidator,
  },
  handler: async (ctx, args): Promise<CallResult> => {
    const now = Date.now();
    const room = await getRoom(ctx, args.roomId);
    await activeMembership(ctx, room, args.playerId);
    let command = args.command as Command;
    if (command.kind === "effect" && !command.confirm) {
      const broke = await chargeAsk(ctx, room, args.playerId, now);
      if (broke) command = { kind: "refuse", reason: broke };
    }
    let result: Omit<CallResult, "timings" | "serverNow">;
    if (command.kind === "refuse") {
      const message = REFUSAL_MESSAGES[command.reason];
      result = { outcome: "refused", effect: null, refusal: { reason: command.reason, message }, line: message };
    } else {
      const row = await createEffect(ctx, args.roomId, args.playerId, args.transcript, command, now);
      const effect = toEffect(row);
      result =
        row.status === "pending"
          ? { outcome: "pending", effect, refusal: null, line: "Confirm for everyone?" }
          : { outcome: "applied", effect, refusal: null, line: (command.substitute ? standInLine(args.transcript, effect.label, effect.vehicle) : `${effect.label}!`) + (command.who ? ` · ${command.who}` : "") };
    }
    const timings = { jevMs: args.timings.jevMs, attempts: args.timings.attempts, totalMs: now - args.timings.startedAt };
    await ctx.db.insert("operatorCalls", {
      roomId: args.roomId,
      raceNumber: room.raceNumber,
      playerId: args.playerId,
      transcript: args.transcript,
      view: args.view,
      input: args.input,
      answers: args.answers,
      command,
      outcome: result.outcome,
      reason: result.refusal?.reason,
      effectId: result.effect?.id,
      error: args.error,
      timings: { ...timings, sttMs: args.timings.sttMs },
      createdAt: now,
    });
    return { ...result, serverNow: now, timings };
  },
});

async function createEffect(
  ctx: MutationCtx,
  roomId: Id<"rooms">,
  playerId: Id<"players">,
  transcript: string,
  command: EffectCommand,
  now: number,
) {
  const range = effectInfo(command.effect)?.duration;
  const fields: EffectFields = {
    roomId,
    effect: command.effect,
    scope: command.scope,
    target: command.target,
    targetClass: command.targetClass,
    strength: command.strength,
    color: command.color,
    theme: command.theme,
    vehicle: command.vehicle,
    instant: !range,
    durationS: range ? rollDuration(range) : 0,
    requestedBy: playerId,
    transcript,
  };
  if (!command.confirm) return activate(ctx, fields, now);

  const confirmBy = now + CONFIRM_WINDOW_MS;
  const id = await ctx.db.insert("activeEffects", { ...fields, status: "pending", startsAt: 0, endsAt: 0, confirmBy });
  const job = await ctx.scheduler.runAt(confirmBy, internal.operator.expire, { effectId: id, endsAt: 0 });
  await ctx.db.patch(id, { expiryJob: job });
  return (await ctx.db.get(id))!;
}

export const confirm = mutation({
  args: { playerId: v.string(), effectId: v.string() },
  handler: async (ctx, args) => {
    const id = ctx.db.normalizeId("activeEffects", args.effectId);
    const row = id && (await ctx.db.get(id));
    if (!row) fail("NOT_FOUND", `Effect ${args.effectId} not found`);
    if (row.requestedBy !== args.playerId) fail("CONFLICT", "Only the player who asked can confirm");
    const now = Date.now();
    if (row.status !== "pending" || now > row.confirmBy) fail("CONFLICT", "The confirm window has closed");
    const room = (await ctx.db.get(row.roomId))!;
    await activeMembership(ctx, room, row.requestedBy);
    const broke = await chargeAsk(ctx, room, row.requestedBy, now);
    if (broke) fail("CONFLICT", REFUSAL_MESSAGES[broke]);
    if (row.expiryJob) await ctx.scheduler.cancel(row.expiryJob);
    const { _id, _creationTime, status, startsAt, endsAt, confirmBy, expiryJob, endedBy, ...fields } = row;
    return toEffect(await activate(ctx, fields, now, row._id));
  },
});

export const expire = internalMutation({
  args: { effectId: v.id("activeEffects"), endsAt: v.number() },
  handler: async (ctx, { effectId, endsAt }) => {
    const row = await ctx.db.get(effectId);
    if (!row) return;
    if (row.status === "pending" && endsAt === 0) await ctx.db.patch(effectId, { status: "expired", endedBy: "unconfirmed" });
    if (row.status === "active" && row.endsAt === endsAt) await ctx.db.patch(effectId, { status: "expired", endedBy: "timer" });
  },
});

export const activeEffects = query({
  args: { roomId: v.string() },
  handler: async (ctx, args) => {
    const roomId = ctx.db.normalizeId("rooms", args.roomId);
    if (!roomId || !(await ctx.db.get(roomId))) fail("NOT_FOUND", `Room ${args.roomId} not found`);
    const rows = await ctx.db
      .query("activeEffects")
      .withIndex("by_room_status", (q) => q.eq("roomId", roomId).eq("status", "active"))
      .collect();
    return { effects: rows.map(toEffect) };
  },
});

export const noteTokenMint = internalMutation({
  args: { playerId: v.string() },
  handler: async (ctx, args) => {
    const player = await getPlayer(ctx, args.playerId);
    const now = Date.now();
    const recent = await ctx.db
      .query("scribeTokenMints")
      .withIndex("by_player_at", (q) => q.eq("playerId", player._id).gt("at", now - 60_000))
      .collect();
    if (recent.length >= TOKEN_MINTS_PER_MINUTE) fail("RATE_LIMITED", "Too many speech tokens; wait a minute");
    await ctx.db.insert("scribeTokenMints", { playerId: player._id, at: now });
  },
});

export const mintScribeToken = action({
  args: { playerId: v.string() },
  handler: async (ctx, args): Promise<{ token: string; serverNow: number }> => {
    await ctx.runMutation(internal.operator.noteTokenMint, { playerId: args.playerId });
    const apiKey = process.env.ELEVENLABS_API_KEY;
    if (!apiKey) fail("UNAVAILABLE", "ELEVENLABS_API_KEY is not set");
    const response = await fetch("https://api.elevenlabs.io/v1/single-use-token/realtime_scribe", {
      method: "POST",
      headers: { "xi-api-key": apiKey },
    });
    if (!response.ok) {
      console.error(`ElevenLabs token mint failed: ${response.status} ${(await response.text()).slice(0, 300)}`);
      fail("UNAVAILABLE", "Speech service unavailable");
    }
    const { token } = (await response.json()) as { token: string };
    return { token, serverNow: Date.now() };
  },
});
