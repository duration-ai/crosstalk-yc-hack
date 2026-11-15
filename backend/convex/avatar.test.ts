import { convexTest } from "convex-test";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "./_generated/api";
import { BASES, COSTUMES, DECISION, GARMENT, HAIR, OUTFITS, SKIN } from "./avatarData";
import { hedgedDecision, DecisionUnavailable } from "./avatarDecision";
import { buildQuestions, readAnswers, shade, snapAge, snapBuild, toAvatar, type RawAnswer } from "./avatarModel";
import schema from "./schema";

vi.mock("sharp", () => ({
  default: (input: Uint8Array) => {
    const chain = { rotate: () => chain, resize: () => chain, jpeg: () => chain, toBuffer: async () => Buffer.from(input) };
    return chain;
  },
}));

const modules = import.meta.glob("./**/*.*s");
const PRIMARY = DECISION.primaryModel;
const BACKUP = DECISION.backupModel;

type Picks = Partial<Record<string, string | number | Record<string, number>>>;

const DEFAULT_PICKS: Picks = {
  real_person: 0.97,
  outfit: "tee",
  figure: "feminine",
  age: "adult",
  build: 2,
  skin: "medium",
  hair: "brown",
  top: "red",
  outer: "none",
  bottoms: "denim",
  headwear: "none",
  expression: "smiling",
  costume: { none: 0.96 },
};

function reply(overrides: Picks = {}) {
  const picks = { ...DEFAULT_PICKS, ...overrides };
  const answers: RawAnswer[] = buildQuestions().filter((question) => picks[question.name] !== undefined).map((question) => {
    const pick = picks[question.name];
    if (question.type === "predicate") return { type: "predicate", name: question.name, probability: pick as number };
    if (question.type === "score") return { type: "score", name: question.name, score: pick as number, confidence: 0.6 };
    const probabilities = typeof pick === "object" ? pick : { [String(pick)]: 0.9 };
    const [choice, confidence] = Object.entries(probabilities).sort(([, a], [, b]) => b - a)[0];
    return {
      type: "choice",
      name: question.name,
      choice,
      confidence,
      probabilities: Object.entries(probabilities).map(([value, probability]) => ({ value, probability })),
    };
  });
  return { model: PRIMARY, answers };
}

type Plan = { delayMs?: number; fail?: boolean; picks?: Picks };
let plans: Record<string, Plan> = {};
let calls: { model: string; body: any }[] = [];

function delay(ms: number, signal?: AbortSignal | null) {
  return new Promise<void>((resolve, reject) => {
    const timer = setTimeout(resolve, ms);
    signal?.addEventListener("abort", () => {
      clearTimeout(timer);
      reject(new Error("aborted"));
    });
  });
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "Date"] });
  vi.stubEnv("AI_GATEWAY_API_KEY", "test-key");
  plans = {};
  calls = [];
  vi.stubGlobal("fetch", async (url: string, init: RequestInit) => {
    const body = JSON.parse(String(init.body));
    calls.push({ model: body.model, body });
    const plan = plans[body.model] ?? {};
    await delay(plan.delayMs ?? 300, init.signal);
    if (plan.fail) return new Response("upstream error", { status: 502 });
    return new Response(JSON.stringify(reply(plan.picks)), { status: 200 });
  });
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

const reading = (overrides: Picks = {}) => readAnswers(reply(overrides).answers);
const hex = (list: { value: string; hex: string }[], value: string) => list.find((swatch) => swatch.value === value)!.hex;

async function upload(t: ReturnType<typeof convexTest>, playerId = "tim-1") {
  const pending = t.fetch(`/api/avatar/photo?playerId=${playerId}`, {
    method: "POST",
    headers: { "Content-Type": "image/jpeg" },
    body: new Uint8Array([0xff, 0xd8, 0xff, 0xd9]),
  });
  let done = false;
  pending.finally(() => (done = true));
  while (!done) {
    await vi.advanceTimersByTimeAsync(50);
    await new Promise((resolve) => setImmediate(resolve));
  }
  const response = await pending;
  return { status: response.status, body: await response.json() };
}

describe("data file", () => {
  it("every outfit and costume points at a base model with the zones it paints", () => {
    for (const outfit of OUTFITS) {
      const zones = BASES[outfit.base].zones;
      for (const slot of Object.values(outfit.slots)) {
        for (const zone of [...slot!.zones, ...(slot!.shade ?? [])]) expect(zones, `${outfit.value}.${zone}`).toHaveProperty(zone);
      }
    }
    for (const costume of COSTUMES) {
      for (const zone of Object.keys(costume.colors)) expect(BASES[costume.base].zones, `${costume.value}.${zone}`).toHaveProperty(zone);
    }
  });

  it("asks one question per answer the mapping reads", () => {
    expect(buildQuestions().map((question) => question.name)).toEqual([
      "real_person", "outfit", "figure", "age", "build", "skin", "hair", "top", "outer", "bottoms", "headwear", "expression", "costume",
    ]);
  });
});

describe("answers to avatar params", () => {
  it("loads the chosen outfit's base model and paints swatches onto its zones", () => {
    const avatar = toAvatar(reading({ outfit: "blazer", top: "white", outer: "navy", bottoms: "khaki", skin: "brown", hair: "black" }));
    const zones = BASES["school-teacher-8b8bfc"].zones;
    expect(avatar.base).toBe("school-teacher-8b8bfc");
    expect(avatar.costume).toBeNull();
    const to = (zone: string) => avatar.recolor.find((entry) => entry.zone === zone);
    expect(to("skin")).toEqual({ zone: "skin", from: zones.skin, to: hex(SKIN, "brown") });
    expect(to("hair")).toEqual({ zone: "hair", from: zones.hair, to: hex(HAIR, "black") });
    expect(to("shirt")?.to).toBe(hex(GARMENT, "white"));
    expect(to("coat")?.to).toBe(hex(GARMENT, "navy"));
    expect(to("coatTrim")?.to).toBe(shade(hex(GARMENT, "navy")));
    expect(to("denim")?.to).toBe(hex(GARMENT, "khaki"));
    expect(avatar.fallbackGlbUrl).toBe("https://polyfork.dev/cdn/school-teacher-8b8bfc.glb");
  });

  it("gives a missing outer layer the top colour and keeps hidden hair and bare heads stock", () => {
    const avatar = toAvatar(reading({ outfit: "hi-vis", top: "orange", outer: "none", hair: "hidden", headwear: "none" }));
    const zones = avatar.recolor.map((entry) => entry.zone);
    expect(avatar.recolor.find((entry) => entry.zone === "coat")?.to).toBe(hex(GARMENT, "orange"));
    expect(zones).not.toContain("hair");
    expect(zones).not.toContain("helmet");
  });

  it("never maps one source colour to two targets", () => {
    for (const outfit of OUTFITS) {
      const avatar = toAvatar(reading({ outfit: outfit.value, top: "red", outer: "blue", bottoms: "black", headwear: "yellow" }));
      const froms = avatar.recolor.map((entry) => entry.from);
      expect(new Set(froms).size, outfit.value).toBe(froms.length);
    }
  });

  it("snaps body answers into the range each model supports", () => {
    expect(snapAge("old", ["kid", "teen", "adult"])).toBe("adult");
    expect(snapAge("kid", ["adult", "old"])).toBe("adult");
    expect(snapAge("teen", ["teen", "adult", "old"])).toBe("teen");
    const range = { min: 0.86, max: 1.18, step: 0.02 };
    expect(snapBuild(0, range)).toBe(0.86);
    expect(snapBuild(4, range)).toBe(1.18);
    expect(snapBuild(2, range)).toBe(1.02);
    expect(snapBuild(2.4, range)).toBe(1.02);
    expect(snapBuild(3.6, range)).toBe(1.18);
    const avatar = toAvatar(reading({ outfit: "hoodie", age: "old", figure: "masculine", build: 4, expression: "serious" }));
    expect(avatar).toMatchObject({ base: "teen-player-37865e", age: "adult", figure: "masculine", build: 1.18, expression: "stern" });
    expect(JSON.parse(decodeURIComponent(avatar.glbUrl.split("?p=")[1]))).toEqual({
      age: "adult", build: 1.18, d20: false, expression: "stern", figure: "masculine",
    });
  });

  it("leaves knobs a model lacks out of the remix", () => {
    const avatar = toAvatar(reading({ outfit: "flannel", figure: "feminine", age: "kid" }));
    expect(avatar).toMatchObject({ base: "player-in-flannel-d0bae4", figure: null, age: null });
    expect(JSON.parse(decodeURIComponent(avatar.glbUrl.split("?p=")[1]))).not.toHaveProperty("figure");
  });
});

describe("costume presets", () => {
  it("applies the preset at 0.70 and keeps only skin and hair from the photo", () => {
    const avatar = toAvatar(reading({ costume: { pirate: 0.7, none: 0.3 }, top: "green", bottoms: "white", skin: "deep", hair: "ginger", expression: "tired" }));
    const pirate = COSTUMES.find((costume) => costume.value === "pirate")!;
    const zones = BASES[pirate.base].zones;
    expect(avatar).toMatchObject({ base: pirate.base, costume: "pirate", expression: pirate.expression });
    const expected = [
      { zone: "skin", from: zones.skin, to: hex(SKIN, "deep") },
      { zone: "hair", from: zones.hair, to: hex(HAIR, "ginger") },
      { zone: "brow", from: zones.brow, to: hex(HAIR, "ginger") },
      ...Object.entries(pirate.colors).map(([zone, to]) => ({ zone, from: zones[zone], to })),
    ];
    expect(avatar.recolor).toEqual(expected);
    expect(avatar.recolor.map((entry) => entry.to)).not.toContain(hex(GARMENT, "green"));
    expect(avatar.recolor.map((entry) => entry.to)).not.toContain(hex(GARMENT, "white"));
  });

  it("builds a normal avatar just below 0.70", () => {
    const avatar = toAvatar(reading({ costume: { pirate: 0.69, none: 0.31 }, outfit: "overcoat" }));
    expect(avatar).toMatchObject({ base: "commuter-381e7f", costume: null, outfit: "overcoat" });
  });
});

describe("hedged decision", () => {
  const ask = () => {
    const pending = hedgedDecision("key", "input", buildQuestions(), (answers) => readAnswers(answers as RawAnswer[]));
    return { pending, settle: () => vi.advanceTimersByTimeAsync(20_000) };
  };

  it("uses Jev alone when it answers in time", async () => {
    plans[PRIMARY] = { delayMs: 300 };
    const { pending, settle } = ask();
    await settle();
    const result = await pending;
    expect(result).toMatchObject({ provider: PRIMARY, hedged: false, latencyMs: 300 });
    expect(calls.map((call) => call.model)).toEqual([PRIMARY]);
  });

  it("asks the backup after 1.5 s and takes it when Jev is slower", async () => {
    plans[PRIMARY] = { delayMs: 4000 };
    plans[BACKUP] = { delayMs: 450 };
    const { pending, settle } = ask();
    await settle();
    const result = await pending;
    expect(result).toMatchObject({ provider: BACKUP, hedged: true, latencyMs: DECISION.hedgeAfterMs + 450 });
    expect(calls.map((call) => call.model)).toEqual([PRIMARY, BACKUP]);
  });

  it("still takes Jev if it lands before the backup", async () => {
    plans[PRIMARY] = { delayMs: 1700 };
    plans[BACKUP] = { delayMs: 450 };
    const { pending, settle } = ask();
    await settle();
    expect(await pending).toMatchObject({ provider: PRIMARY, hedged: true, latencyMs: 1700 });
  });

  it("goes to the backup at once when Jev fails", async () => {
    plans[PRIMARY] = { delayMs: 100, fail: true };
    plans[BACKUP] = { delayMs: 450 };
    const { pending, settle } = ask();
    await settle();
    expect(await pending).toMatchObject({ provider: BACKUP, hedged: true, latencyMs: 550 });
  });

  it("throws DecisionUnavailable when both fail", async () => {
    plans[PRIMARY] = { delayMs: 100, fail: true };
    plans[BACKUP] = { delayMs: 200, fail: true };
    const { pending, settle } = ask();
    const outcome = pending.catch((error) => error);
    await settle();
    const error = await outcome;
    expect(error).toBeInstanceOf(DecisionUnavailable);
    expect(error.message).toContain(PRIMARY);
    expect(error.message).toContain(BACKUP);
  });

  it("treats an incomplete answer set as a failure", async () => {
    plans[PRIMARY] = { delayMs: 100, picks: { outfit: undefined as never } };
    plans[BACKUP] = { delayMs: 200 };
    const { pending, settle } = ask();
    await settle();
    expect(await pending).toMatchObject({ provider: BACKUP });
  });
});

describe("HTTP", () => {
  it("saves the avatar for the player and serves it to other racers", async () => {
    const t = convexTest(schema, modules);
    plans[PRIMARY] = { picks: { outfit: "puffer", outer: "red" } };
    const { status, body } = await upload(t);
    expect(status).toBe(200);
    expect(body.decision).toMatchObject({ provider: PRIMARY, hedged: false, realPerson: 0.97, costume: "none" });
    expect(body.avatar).toMatchObject({ playerId: "tim-1", base: "spectator-d086fe", outfit: "puffer" });
    expect(calls[0].body.input[0].content[1].image_url).toMatch(/^data:image\/jpeg;base64,/);
    const fetched = await (await t.fetch("/api/avatar?playerId=tim-1")).json();
    expect(fetched.avatar).toEqual(body.avatar);
    expect((await (await t.fetch("/api/avatar?playerId=nobody")).json()).avatar).toBeNull();
  });

  it("refuses a photo below 0.5 with RETAKE and keeps the previous avatar", async () => {
    const t = convexTest(schema, modules);
    await upload(t);
    plans[PRIMARY] = { picks: { real_person: 0.49 } };
    const { status, body } = await upload(t);
    expect(status).toBe(422);
    expect(body.error).toMatchObject({ code: "RETAKE", realPerson: 0.49 });
    expect((await (await t.fetch("/api/avatar?playerId=tim-1")).json()).avatar.outfit).toBe("tee");
  });

  it("accepts exactly 0.5", async () => {
    const t = convexTest(schema, modules);
    plans[PRIMARY] = { picks: { real_person: 0.5 } };
    expect((await upload(t)).status).toBe(200);
  });

  it("replaces the avatar on a retake", async () => {
    const t = convexTest(schema, modules);
    await upload(t);
    plans[PRIMARY] = { picks: { outfit: "dress", top: "yellow" } };
    await upload(t);
    const { avatar } = await (await t.fetch("/api/avatar?playerId=tim-1")).json();
    expect(avatar).toMatchObject({ outfit: "dress", base: "diner-waitress-60f6a1" });
    const count = await t.run((ctx) => ctx.db.query("avatars").collect());
    expect(count).toHaveLength(1);
  });

  it("returns DECISION_UNAVAILABLE when both models fail", async () => {
    const t = convexTest(schema, modules);
    plans[PRIMARY] = { fail: true };
    plans[BACKUP] = { fail: true };
    const { status, body } = await upload(t);
    expect(status).toBe(503);
    expect(body.error.code).toBe("DECISION_UNAVAILABLE");
  });

  it("rejects bad input", async () => {
    const t = convexTest(schema, modules);
    const bad = await t.fetch("/api/avatar/photo?playerId=x", { method: "POST", headers: { "Content-Type": "text/plain" }, body: "hi" });
    expect(bad.status).toBe(400);
    const badId = await t.fetch("/api/avatar/photo?playerId=a%20b", { method: "POST", headers: { "Content-Type": "image/jpeg" }, body: new Uint8Array([1]) });
    expect(badId.status).toBe(400);
    expect((await t.query(api.avatars.get, { playerId: "tim-1" })).avatar).toBeNull();
  });
});
