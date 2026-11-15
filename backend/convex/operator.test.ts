import { convexTest } from "convex-test";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, internal } from "./_generated/api";
import { selfScoped } from "./operator";
import { OBJECT_EFFECTS, PLAYABLE, PLAYER_EFFECTS, WORLD_EFFECTS } from "./operatorCatalogue";
import schema from "./schema";

// These tests cover the routing rules for the whole catalogue; operatorPlayable.test.ts covers the
// built-effects gate with the real shared list.
for (const id of Object.keys({ ...PLAYER_EFFECTS, ...WORLD_EFFECTS, ...OBJECT_EFFECTS })) (PLAYABLE as Set<string>).add(id);

const modules = import.meta.glob("./**/*.*s");
const T0 = Date.UTC(2026, 10, 15, 20, 0, 0);

const CHOICES = [
  "scope", "player_effect", "world_group", "sky_effect", "physics_effect", "population_effect", "object_effect",
  "class", "target", "strength", "color", "theme", "vehicle", "reject_reason",
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

async function room(asks: { asks?: number; freeAsks?: boolean } = { freeAsks: true }) {
  const t = convexTest(schema, modules);
  const tim = await t.mutation(api.players.create, { displayName: "Tim" });
  const maya = await t.mutation(api.players.create, { displayName: "Maya" });
  const created = await t.mutation(api.rooms.create, { playerId: tim.id, ...asks });
  await t.mutation(api.rooms.join, { playerId: maya.id, code: created.room.code });
  const roomId = created.room.id;
  const say = (transcript: string, picks: Record<string, string | number> | Error, playerId = tim.id, mode: "onFoot" | "driving" = "onFoot") => {
    replies.push(picks);
    return t.action(api.operator.call, { roomId, playerId, transcript, view: view(mode) });
  };
  const effects = async () => (await t.query(api.operator.activeEffects, { roomId })).effects;
  const asksOf = async (playerId: string) =>
    (await t.query(internal.rooms.poll, { roomId, now: Date.now() })).racers.find((r) => r.playerId === playerId)!.asks;
  return { t, tim, maya, roomId, say, effects, asksOf };
}

const RAIN = { scope: "world", world_group: "sky", sky_effect: "rain" };

describe("operator.call", () => {
  it("asks Jev once with the quote, view and questions, then writes the effect", async () => {
    const { t, say, effects, roomId } = await room();
    const result = await say("make it rain", RAIN);
    expect(result).toMatchObject({ outcome: "applied", line: "Rain!", refusal: null, timings: { attempts: 1 } });
    expect(result.effect).toMatchObject({ effect: "rain", scope: "world", status: "active", strength: "medium", instant: false });
    expect(result.effect!.durationS).toBeGreaterThanOrEqual(23);
    expect(result.effect!.durationS).toBeLessThanOrEqual(45);
    expect(result.effect!.endsAt - result.effect!.startsAt).toBe(result.effect!.durationS * 1000);

    expect(requests).toHaveLength(1);
    expect(requests[0].url).toBe("https://ai-gateway.vercel.sh/v1/decisions");
    expect(requests[0].body.model).toBe("typesafe-ai/jev");
    expect(requests[0].body.input).toContain('The player said: "make it rain"');
    expect(requests[0].body.input).toContain("bld_yc: building");
    expect(requests[0].body.input).toContain("protected landmark");
    expect(requests[0].body.questions.map((q: { name: string }) => q.name)).toEqual([
      "scope", "player_effect", "world_group", "sky_effect", "physics_effect", "population_effect", "object_effect",
      "class", "target", "described", "self_ref", "other_player", "strength", "color", "theme", "vehicle", "reject_reason",
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

  it("refuses a counter with nothing to counter, and any undo", async () => {
    const { say, effects } = await room();
    expect(await say("clear skies", { scope: "world", world_group: "sky", sky_effect: "clear_weather" })).toMatchObject({
      outcome: "refused",
      refusal: { reason: "no_op", message: "Nothing to change." },
    });
    await say("make it rain", RAIN);
    await say("low gravity", { scope: "world", world_group: "physics", physics_effect: "gravity_lower" });
    const undo = await say("undo that", { scope: "reject", reject_reason: "undo" });
    expect(undo).toMatchObject({ outcome: "refused", effect: null, refusal: { reason: "undo" }, line: "No take-backs, sugar." });
    expect((await effects()).map((e) => e.effect).sort()).toEqual(["gravity_lower", "rain"]);
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
    vi.stubEnv("OPERATOR_SELF_SCOPE", "false");
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
    vi.stubEnv("OPERATOR_SELF_SCOPE", "false");
    const { t, tim, say, effects } = await room();
    const pending = await say("shrink her", { scope: "object", object_effect: "shrink", other_player: 0.9 });
    vi.setSystemTime(T0 + 10_500);
    await expect(t.mutation(api.operator.confirm, { playerId: tim.id, effectId: pending.effect!.id })).rejects.toThrow(/closed/);
    await t.finishAllScheduledFunctions(vi.runAllTimers);
    expect(await t.run((ctx) => ctx.db.get(pending.effect!.id as any))).toMatchObject({ status: "expired", endedBy: "unconfirmed" });
    expect(await effects()).toEqual([]);
  });

  it("puts out the fire it extinguishes", async () => {
    const { say, effects } = await room();
    const fire = { scope: "object", object_effect: "set_fire", class: "waymo" };
    await say("set the Waymo on fire", fire);
    expect((await effects()).map((e) => e.effect)).toEqual(["set_fire"]);
    const out = await say("put out the fire", { scope: "object", object_effect: "extinguish", class: "waymo" });
    expect(out.effect).toMatchObject({ effect: "extinguish", target: "veh_12" });
    expect((await effects()).map((e) => e.effect)).toEqual(["extinguish"]);
    expect(await say("put out the fire", { scope: "object", object_effect: "extinguish", class: "waymo" })).toMatchObject({
      refusal: { reason: "no_op" },
    });
  });

  it("switches the theme when a theme party is asked for again", async () => {
    const { say, effects } = await room();
    const party = (theme: string) => ({ scope: "world", world_group: "population", population_effect: "ped_theme", theme });
    const elvis = (await say("everyone dress like elvis", party("elvis"))).effect!;
    const beach = (await say("beach party", party("beach_party"))).effect!;
    expect(beach).toMatchObject({ id: elvis.id, theme: "beach_party" });
    expect((await effects()).map((e) => e.theme)).toEqual(["beach_party"]);
  });

  it("drops the call and the confirm of a player who left the room", async () => {
    vi.stubEnv("OPERATOR_SELF_SCOPE", "false");
    const { t, tim, roomId, say, effects } = await room();
    const pending = await say("slow him down", { scope: "object", object_effect: "slow_down", other_player: 0.95 }, tim.id, "driving");
    await t.mutation(api.rooms.leave, { playerId: tim.id, roomId });
    await expect(t.mutation(api.operator.confirm, { playerId: tim.id, effectId: pending.effect!.id })).rejects.toThrow(/not in room|left/);
    await expect(
      t.mutation(internal.operator.apply, {
        roomId: roomId as any,
        playerId: tim.id as any,
        transcript: "make it rain",
        view: view(),
        input: "",
        answers: null,
        command: { kind: "effect", scope: "world", effect: "rain", target: "", targetClass: "", strength: "medium", color: "", theme: "", vehicle: "", confirm: false },
        timings: { jevMs: 0, attempts: 1, startedAt: Date.now() },
      }),
    ).rejects.toThrow(/not in room|left/);
    expect(await effects()).toEqual([]);
  });

  it("spends one ask per applied effect and refuses cleanly at zero", async () => {
    vi.stubEnv("OPERATOR_ANYWHERE", "false");
    const { t, tim, maya, say, effects, asksOf } = await room({ asks: 2 });
    expect(await asksOf(tim.id)).toBe(2);
    await say("make it rain", RAIN);
    expect(await say("spawn a dragon", { scope: "reject", reject_reason: "spawning" })).toMatchObject({ outcome: "refused" });
    expect(await asksOf(tim.id)).toBe(1);
    await say("night", { scope: "world", world_group: "sky", sky_effect: "night" });
    expect(await asksOf(tim.id)).toBe(0);

    const broke = await say("roll in the fog", { scope: "world", world_group: "sky", sky_effect: "fog" });
    expect(broke).toMatchObject({ outcome: "refused", effect: null, refusal: { reason: "no_asks" }, line: "Grab a phone first!" });
    expect((await effects()).map((e) => e.effect).sort()).toEqual(["night", "rain"]);
    expect(await asksOf(tim.id)).toBe(0);
    expect(await asksOf(maya.id)).toBe(2);
    const log = await t.run((ctx) => ctx.db.query("operatorCalls").collect());
    expect(log[log.length - 1]).toMatchObject({ transcript: "roll in the fog", outcome: "refused", reason: "no_asks" });
  });

  it("charges a promoted effect when it is confirmed, not when it is asked for", async () => {
    vi.stubEnv("OPERATOR_SELF_SCOPE", "false");
    vi.stubEnv("OPERATOR_ANYWHERE", "false");
    const { t, tim, say, effects, asksOf } = await room({ asks: 1 });
    const slow = { scope: "object", object_effect: "slow_down", other_player: 0.95 };
    const first = await say("slow him down", slow, tim.id, "driving");
    expect(await asksOf(tim.id)).toBe(1);
    await t.mutation(api.operator.confirm, { playerId: tim.id, effectId: first.effect!.id });
    expect(await asksOf(tim.id)).toBe(0);

    const second = await say("shrink her", { scope: "object", object_effect: "shrink", other_player: 0.9 }, tim.id, "driving");
    expect(second).toMatchObject({ outcome: "pending" });
    await expect(t.mutation(api.operator.confirm, { playerId: tim.id, effectId: second.effect!.id })).rejects.toThrow(/Grab a phone/);
    expect((await effects()).map((e) => e.effect)).toEqual(["vehicle_slower"]);
  });

  it("calls for free outside a running race while OPERATOR_ANYWHERE is on", async () => {
    const { tim, say, asksOf } = await room({ asks: 1 });
    await say("make it rain", RAIN);
    expect(await say("night", { scope: "world", world_group: "sky", sky_effect: "night" })).toMatchObject({ outcome: "applied" });
    expect(await asksOf(tim.id)).toBe(1);
  });

  it("lands car and avatar changes on the caller only while OPERATOR_SELF_SCOPE is on", async () => {
    const { tim, maya, say, effects } = await room();
    const fly = { scope: "world", world_group: "physics", physics_effect: "cars_fly" };
    await say("cars fly", fly);
    await say("cars fly", fly, maya.id);
    await say("make it rain", RAIN);
    const rows = await effects();
    expect(rows.filter((e) => e.effect === "cars_fly").map((e) => e.target).sort()).toEqual([`self:${maya.id}`, `self:${tim.id}`].sort());
    expect(rows.find((e) => e.effect === "rain")).toMatchObject({ target: "" });
  });

  it("puts class-wide and other-racer car effects on the caller's car", () => {
    const bouncy = { kind: "effect", scope: "class", effect: "bouncy", target: "", targetClass: "vehicle", strength: "medium", color: "", theme: "", vehicle: "", confirm: false } as const;
    expect(selfScoped(bouncy, "p1")).toMatchObject({ scope: "object", target: "car:p1", targetClass: "car" });
    expect(selfScoped({ ...bouncy, scope: "object", target: "car:p2", targetClass: "car", confirm: true }, "p1")).toMatchObject({ target: "car:p1", confirm: false });
    expect(selfScoped({ ...bouncy, effect: "set_fire", scope: "object", target: "bld_1", targetClass: "building" }, "p1")).toMatchObject({ target: "bld_1" });
    expect(selfScoped({ ...bouncy, effect: "vehicle_slower", scope: "players", targetClass: "", confirm: true }, "p1")).toMatchObject({ target: "self:p1", confirm: false });
  });

  it("never runs a freeAsks room out of asks", async () => {
    const { tim, say, asksOf } = await room({ asks: 1, freeAsks: true });
    for (const effect of ["rain", "night", "fog"]) {
      expect(await say(effect, { scope: "world", world_group: "sky", sky_effect: effect })).toMatchObject({ outcome: "applied" });
    }
    expect(await asksOf(tim.id)).toBe(1);
  });

  it("counts applied Operator calls toward the race's CHAOS CALLER", async () => {
    vi.stubEnv("OPERATOR_ANYWHERE", "false");
    const { t, tim, maya, roomId, say } = await room();
    let started;
    for (const p of [tim, maya]) started = (await t.mutation(api.rooms.setReady, { playerId: p.id, roomId, ready: true })).room;
    vi.setSystemTime(started!.startsAt);
    await say("make it rain", RAIN, maya.id);
    await say("night", { scope: "world", world_group: "sky", sky_effect: "night" }, maya.id);
    await say("spawn a dragon", { scope: "reject", reject_reason: "spawning" }, tim.id);
    await t.mutation(api.race.update, { playerId: tim.id, roomId, position: { x: 0, y: 0.5, z: 800 }, heading: 0 });
    await t.mutation(api.race.update, { playerId: maya.id, roomId, position: { x: 0, y: 0.5, z: 800 }, heading: 0 });
    const done = await t.query(internal.rooms.poll, { roomId, now: Date.now() + 1 });
    expect(done.room.status).toBe("finished");
    expect(Object.fromEntries(done.results.map((r) => [r.displayName, [r.calls, r.award]]))).toMatchObject({
      Tim: [0, expect.any(String)],
      Maya: [2, "CHAOS CALLER"],
    });
    vi.setSystemTime(Date.now() + 1);
    expect(await say("make it rain", RAIN, maya.id)).toMatchObject({ outcome: "refused", refusal: { reason: "race_over" } });
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
