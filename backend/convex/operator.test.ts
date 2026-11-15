import { convexTest } from "convex-test";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, internal } from "./_generated/api";
import schema from "./schema";

const modules = import.meta.glob("./**/*.*s");
const T0 = Date.UTC(2026, 10, 15, 20, 0, 0);

const CHOICES = [
  "scope", "player_effect", "world_group", "sky_effect", "physics_effect", "population_effect", "object_effect",
  "class", "target", "strength", "color", "theme", "reject_reason",
];
const DEFAULTS: Record<string, string> = { scope: "reject", strength: "medium" };

function jevReply(picks: Record<string, string | number>) {
  return {
    model: "typesafe-ai/jev",
    answers: [
      ...CHOICES.map((name) => {
        const choice = String(picks[name] ?? DEFAULTS[name] ?? "none");
        return { type: "choice", name, choice, confidence: 0.97, probabilities: [{ value: choice, probability: 0.97 }] };
      }),
      ...["described", "self_ref", "other_player"].map((name) => ({ type: "predicate", name, probability: picks[name] ?? 0.02 })),
    ],
  };
}

let replies: Array<Record<string, string | number> | Error> = [];
let requests: { url: string; body: any }[] = [];

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(T0);
  vi.stubEnv("AI_GATEWAY_API_KEY", "test-key");
  vi.stubEnv("ELEVENLABS_API_KEY", "test-eleven");
  replies = [];
  requests = [];
  vi.stubGlobal("fetch", async (url: string, init: RequestInit) => {
    requests.push({ url, body: init.body ? JSON.parse(String(init.body)) : null });
    if (url.includes("elevenlabs")) return new Response(JSON.stringify({ token: "sutkn_1" }), { status: 200 });
    const next = replies.shift();
    if (!next) throw new Error("no Jev reply queued");
    if (next instanceof Error) throw next;
    return new Response(JSON.stringify(jevReply(next)), { status: 200 });
  });
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

const view = (mode: "onFoot" | "driving" = "onFoot") => ({
  mode,
  timeOfDay: "day",
  weather: "clear",
  entities: [
    ...(mode === "driving" ? [{ id: "veh_me", class: "car", desc: "your car", color: "blue", screen: "centre, large", distance_m: 0, view_rank: 0, protected: false, controlledBy: "me" }] : []),
    { id: "veh_12", class: "waymo", desc: "Waymo robotaxi", color: "white", screen: "centre, large", distance_m: 18, view_rank: 1, protected: false, controlledBy: "" },
    { id: "bld_yc", class: "building", desc: "YC office", color: "grey", screen: "left, large", distance_m: 60, view_rank: 2, protected: true, controlledBy: "" },
  ],
});

async function room() {
  const t = convexTest(schema, modules);
  const tim = await t.mutation(api.players.create, { displayName: "Tim" });
  const maya = await t.mutation(api.players.create, { displayName: "Maya" });
  const created = await t.mutation(api.rooms.create, { playerId: tim.id });
  await t.mutation(api.rooms.join, { playerId: maya.id, code: created.room.code });
  const roomId = created.room.id;
  const say = (transcript: string, picks: Record<string, string | number> | Error, playerId = tim.id, mode: "onFoot" | "driving" = "onFoot") => {
    replies.push(picks);
    return t.action(api.operator.call, { roomId, playerId, transcript, view: view(mode) });
  };
  const effects = async () => (await t.query(api.operator.activeEffects, { roomId })).effects;
  return { t, tim, maya, roomId, say, effects };
}

const RAIN = { scope: "world", world_group: "sky", sky_effect: "rain" };

describe("operator.call", () => {
  it("asks Jev once with the quote, view and questions, then writes the effect", async () => {
    const { t, say, effects, roomId } = await room();
    const result = await say("make it rain", RAIN);
    expect(result).toMatchObject({ outcome: "applied", line: "Rain!", refusal: null, timings: { attempts: 1 } });
    expect(result.effect).toMatchObject({ effect: "rain", scope: "world", status: "active", strength: "medium", instant: false });
    expect(result.effect!.durationS).toBeGreaterThanOrEqual(10);
    expect(result.effect!.durationS).toBeLessThanOrEqual(20);
    expect(result.effect!.endsAt - result.effect!.startsAt).toBe(result.effect!.durationS * 1000);

    expect(requests).toHaveLength(1);
    expect(requests[0].url).toBe("https://ai-gateway.vercel.sh/v1/decisions");
    expect(requests[0].body.model).toBe("typesafe-ai/jev");
    expect(requests[0].body.input).toContain('The player said: "make it rain"');
    expect(requests[0].body.input).toContain("bld_yc: building");
    expect(requests[0].body.input).toContain("protected landmark");
    expect(requests[0].body.questions.map((q: { name: string }) => q.name)).toEqual([
      "scope", "player_effect", "world_group", "sky_effect", "physics_effect", "population_effect", "object_effect",
      "class", "target", "described", "self_ref", "other_player", "strength", "color", "theme", "reject_reason",
    ]);
    expect(await effects()).toEqual([result.effect]);

    const log = await t.run((ctx) => ctx.db.query("operatorCalls").collect());
    expect(log).toHaveLength(1);
    expect(log[0]).toMatchObject({ transcript: "make it rain", outcome: "applied", roomId });
    expect(log[0].answers.scope.choice).toBe("world");
  });

  it("expires effects on their timer", async () => {
    const { t, say, effects } = await room();
    const { effect } = await say("make it rain", RAIN);
    vi.setSystemTime(effect!.endsAt - 1);
    await t.finishInProgressScheduledFunctions();
    expect(await effects()).toHaveLength(1);
    vi.setSystemTime(effect!.endsAt + 1);
    await t.finishAllScheduledFunctions(vi.runAllTimers);
    expect(await effects()).toEqual([]);
    const row = await t.run((ctx) => ctx.db.get(effect!.id as any));
    expect(row).toMatchObject({ status: "expired", endedBy: "timer" });
  });

  it("extends the same effect on the same target", async () => {
    const { say, effects } = await room();
    const first = (await say("make it rain", RAIN)).effect!;
    vi.setSystemTime(T0 + 2_000);
    const second = (await say("more rain", { ...RAIN, strength: "large" })).effect!;
    expect(second.id).toBe(first.id);
    expect(second.endsAt).toBe(first.endsAt + (second.durationS - first.durationS) * 1000);
    expect(second.strength).toBe("large");
    expect(await effects()).toHaveLength(1);
  });

  it("ends what a counter counters and what a rival replaces", async () => {
    const { t, say, effects } = await room();
    await say("make it rain", RAIN);
    await say("night time", { scope: "world", world_group: "sky", sky_effect: "night" });
    await say("make it day", { scope: "world", world_group: "sky", sky_effect: "day" });
    expect((await effects()).map((e) => e.effect).sort()).toEqual(["day", "rain"]);

    const cleared = await say("stop the rain", { scope: "world", world_group: "sky", sky_effect: "clear_weather" });
    expect(cleared.effect).toMatchObject({ effect: "clear_weather", instant: true, strength: "" });
    expect((await effects()).map((e) => e.effect).sort()).toEqual(["clear_weather", "day"]);

    vi.setSystemTime(T0 + 3_001);
    await t.finishAllScheduledFunctions(vi.runAllTimers);
    expect((await effects()).map((e) => e.effect)).toEqual([]);
  });

  it("refuses a counter with nothing to counter and an undo with nothing to undo", async () => {
    const { say, effects } = await room();
    expect(await say("clear skies", { scope: "world", world_group: "sky", sky_effect: "clear_weather" })).toMatchObject({
      outcome: "refused",
      refusal: { reason: "no_op", message: "Nothing to change." },
    });
    expect(await say("undo", { scope: "world", world_group: "undo" })).toMatchObject({ refusal: { reason: "no_op" } });
    await say("make it rain", RAIN);
    await say("low gravity", { scope: "world", world_group: "physics", physics_effect: "gravity_lower" });
    const undo = await say("undo that", { scope: "world", world_group: "undo" });
    expect(undo.outcome).toBe("applied");
    expect((await effects()).map((e) => e.effect).sort()).toEqual(["rain", "undo_last"]);
  });

  it("targets a visible Waymo and keeps your own car's recolor local", async () => {
    const { tim, say } = await room();
    const waymo = await say("make that Waymo pink", { scope: "object", object_effect: "recolor", class: "waymo", color: "pink" });
    expect(waymo.effect).toMatchObject({ scope: "object", effect: "recolor", target: "veh_12", targetClass: "waymo", color: "pink", instant: true });
    const mine = await say("paint my car gold", { scope: "object", object_effect: "recolor", color: "gold", self_ref: 0.9 }, tim.id, "driving");
    expect(mine.effect).toMatchObject({ target: `car:${tim.id}`, color: "gold" });
  });

  it("refuses the protected YC building", async () => {
    const { say } = await room();
    const result = await say("demolish the YC building", {
      scope: "object", object_effect: "demolish", class: "building", described: 0.95, target: "bld_yc",
    });
    expect(result).toMatchObject({ outcome: "refused", effect: null, refusal: { reason: "protected" }, line: "That landmark's protected." });
  });

  it("holds a promoted effect until the requester confirms within 10 s", async () => {
    const { t, tim, maya, say, effects } = await room();
    const pending = await say("slow him down", { scope: "object", object_effect: "slow_down", other_player: 0.95 }, tim.id, "driving");
    expect(pending).toMatchObject({ outcome: "pending", line: "Confirm for everyone?" });
    expect(pending.effect).toMatchObject({ effect: "vehicle_slower", scope: "players", status: "pending" });
    expect(await effects()).toEqual([]);

    await expect(t.mutation(api.operator.confirm, { playerId: maya.id, effectId: pending.effect!.id })).rejects.toThrow(/Only the player/);
    vi.setSystemTime(T0 + 4_000);
    const active = await t.mutation(api.operator.confirm, { playerId: tim.id, effectId: pending.effect!.id });
    expect(active).toMatchObject({ status: "active", startsAt: T0 + 4_000 });
    expect(await effects()).toEqual([active]);
  });

  it("lets an unconfirmed effect lapse after 10 s", async () => {
    const { t, tim, say, effects } = await room();
    const pending = await say("shrink her", { scope: "object", object_effect: "shrink", other_player: 0.9 });
    vi.setSystemTime(T0 + 10_500);
    await expect(t.mutation(api.operator.confirm, { playerId: tim.id, effectId: pending.effect!.id })).rejects.toThrow(/closed/);
    await t.finishAllScheduledFunctions(vi.runAllTimers);
    expect(await t.run((ctx) => ctx.db.get(pending.effect!.id as any))).toMatchObject({ status: "expired", endedBy: "unconfirmed" });
    expect(await effects()).toEqual([]);
  });

  it("retries Jev once, then refuses as unavailable and logs the error", async () => {
    const { t, say } = await room();
    replies.push(new Error("socket hang up"));
    const retried = await say("make it rain", RAIN);
    expect(retried).toMatchObject({ outcome: "applied", timings: { attempts: 2 } });

    replies.push(new Error("socket hang up"));
    const failed = await say("make it rain", new Error("boom"));
    expect(failed).toMatchObject({ outcome: "refused", refusal: { reason: "unavailable", message: "Operator busy. Retry." } });
    const log = await t.run((ctx) => ctx.db.query("operatorCalls").collect());
    expect(log[1]).toMatchObject({ outcome: "refused", reason: "unavailable" });
    expect(log[1].error).toMatch(/socket hang up; Error: boom/);
  });

  it("refuses as unavailable without a gateway key", async () => {
    vi.stubEnv("AI_GATEWAY_API_KEY", "");
    const { say } = await room();
    expect(await say("make it rain", RAIN)).toMatchObject({ refusal: { reason: "unavailable" } });
    expect(requests).toHaveLength(0);
  });

  it("rejects outsiders and empty transcripts", async () => {
    const { t, roomId, tim } = await room();
    const outsider = await t.mutation(api.players.create, { displayName: "Zed" });
    await expect(t.action(api.operator.call, { roomId, playerId: outsider.id, transcript: "rain", view: view() })).rejects.toThrow(/not in room/);
    await expect(t.action(api.operator.call, { roomId, playerId: tim.id, transcript: "   ", view: view() })).rejects.toThrow(/transcript/);
  });

  it("answers over HTTP at /api/operator", async () => {
    const { t, tim, roomId } = await room();
    replies.push(RAIN);
    const response = await t.fetch("/api/operator", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ playerId: tim.id, roomId, text: "make it rain" }),
    });
    expect(response.status).toBe(200);
    expect(await response.json()).toMatchObject({ outcome: "applied", effect: { effect: "rain" } });
  });
});

describe("operator.mintScribeToken", () => {
  it("mints through ElevenLabs and rate-limits per player", async () => {
    const { t, tim } = await room();
    for (let i = 0; i < 10; i++) {
      expect(await t.action(api.operator.mintScribeToken, { playerId: tim.id })).toEqual({ token: "sutkn_1", serverNow: T0 });
    }
    expect(requests[0].url).toBe("https://api.elevenlabs.io/v1/single-use-token/realtime_scribe");
    await expect(t.action(api.operator.mintScribeToken, { playerId: tim.id })).rejects.toThrow(/Too many/);
    vi.setSystemTime(T0 + 60_001);
    expect(await t.action(api.operator.mintScribeToken, { playerId: tim.id })).toMatchObject({ token: "sutkn_1" });
  });
});
