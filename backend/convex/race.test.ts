import { convexTest } from "convex-test";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, internal } from "./_generated/api";
import schema from "./schema";
import { AUTO_START_MS, CLOSE_AFTER_FIRST_FINISH_MS, COUNTDOWN_MS } from "./raceModel";
import { WORLD_CHANGE_TYPES, newsText, type WorldChange } from "./worldChanges";

const modules = import.meta.glob("./**/*.*s");
const T0 = Date.UTC(2026, 10, 15, 20, 0, 0);

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(T0);
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllEnvs();
});

const at = (z: number, x = 0) => ({ x, y: 0.5, z });

async function fourPlayers() {
  const t = convexTest(schema, modules);
  const names = ["Tim", "Maya", "Jun", "Rosa"];
  const players = [];
  for (const displayName of names) players.push(await t.mutation(api.players.create, { displayName }));
  const [tim, maya, jun, rosa] = players;
  const created = await t.mutation(api.rooms.create, { playerId: tim.id });
  const roomId = created.room.id;
  for (const p of [maya, jun, rosa]) await t.mutation(api.rooms.join, { playerId: p.id, code: created.room.code });
  return { t, roomId, code: created.room.code, tim, maya, jun, rosa, players };
}

async function startRace(setup: Awaited<ReturnType<typeof fourPlayers>>) {
  const { t, roomId, players } = setup;
  let room;
  for (const p of players) room = (await t.mutation(api.rooms.setReady, { playerId: p.id, roomId, ready: true })).room;
  vi.setSystemTime(room!.startsAt);
  return room!;
}

function poll(t: ReturnType<typeof convexTest>, roomId: string, after?: number) {
  return t.query(internal.rooms.poll, { roomId, after, now: Date.now() });
}

describe("rooms", () => {
  it("creates a room and fills it in join order with player colors", async () => {
    const { t, roomId, code, tim, maya } = await fourPlayers();
    expect(code).toMatch(/^[A-Z]{4}$/);
    const state = await poll(t, roomId);
    expect(state.room).toMatchObject({ status: "lobby", playerCount: 4, readyCount: 0, startsAt: 0, raceNumber: 1 });
    expect(state.racers.map((r) => [r.displayName, r.color, r.colorHex, r.slot])).toEqual([
      ["Tim", "blue", "#2D8CFF", 0],
      ["Maya", "purple", "#A35BFF", 1],
      ["Jun", "orange", "#FF8A1E", 2],
      ["Rosa", "green", "#2EC27E", 3],
    ]);

    const again = await t.mutation(api.rooms.join, { playerId: maya.id, code: code.toLowerCase() });
    expect(again.you).toMatchObject({ playerId: maya.id, color: "purple" });
    expect(again.route).toHaveLength(2);

    const fifth = await t.mutation(api.players.create, { displayName: "Chris" });
    await expect(t.mutation(api.rooms.join, { playerId: fifth.id, code })).rejects.toThrow(/full/);
    await expect(t.mutation(api.rooms.join, { playerId: fifth.id, code: "ZZZZ" })).rejects.toThrow(/not found/);
    await expect(t.mutation(api.rooms.create, { playerId: tim.id, route: [at(0)] })).rejects.toThrow(/route/);
  });

  it("frees a leaver's color and moves a player who joins another room", async () => {
    const { t, roomId, code, maya, jun } = await fourPlayers();
    await t.mutation(api.rooms.leave, { playerId: maya.id, roomId });
    expect(await t.mutation(api.rooms.leave, { playerId: maya.id, roomId })).toEqual({ left: true });
    const other = await t.mutation(api.rooms.create, { playerId: jun.id });
    expect(other.you.color).toBe("blue");
    const state = await poll(t, roomId);
    expect(state.racers.map((r) => r.displayName)).toEqual(["Tim", "Rosa"]);

    const back = await t.mutation(api.rooms.join, { playerId: maya.id, code });
    expect(back.you).toMatchObject({ color: "purple", slot: 1 });
  });
});

describe("ready and countdown", () => {
  it("auto-starts 10 s after two are ready, shortens to 3 s when all are, and locks the last 3 s", async () => {
    const { t, roomId, tim, maya, jun, rosa, code } = await fourPlayers();
    await t.mutation(api.rooms.setReady, { playerId: tim.id, roomId, ready: true });
    expect((await poll(t, roomId)).room).toMatchObject({ status: "lobby", startsAt: 0, readyCount: 1 });

    vi.setSystemTime(T0 + 1_000);
    const two = await t.mutation(api.rooms.setReady, { playerId: maya.id, roomId, ready: true });
    expect(two.room).toMatchObject({ status: "countdown", startsAt: T0 + 1_000 + AUTO_START_MS });

    vi.setSystemTime(T0 + 2_000);
    const cancelled = await t.mutation(api.rooms.setReady, { playerId: maya.id, roomId, ready: false });
    expect(cancelled.room).toMatchObject({ status: "lobby", startsAt: 0 });
    vi.setSystemTime(T0 + 3_000);
    await t.mutation(api.rooms.setReady, { playerId: maya.id, roomId, ready: true });
    vi.setSystemTime(T0 + 4_000);
    await t.mutation(api.rooms.setReady, { playerId: jun.id, roomId, ready: true });
    expect((await poll(t, roomId)).room.startsAt).toBe(T0 + 3_000 + AUTO_START_MS);

    vi.setSystemTime(T0 + 5_000);
    const all = await t.mutation(api.rooms.setReady, { playerId: rosa.id, roomId, ready: true });
    expect(all.room).toMatchObject({ status: "countdown", startsAt: T0 + 5_000 + COUNTDOWN_MS, readyCount: 4 });

    await expect(t.mutation(api.rooms.setReady, { playerId: jun.id, roomId, ready: false })).rejects.toThrow(
      /about to start/,
    );
    await expect(t.mutation(api.rooms.leave, { playerId: jun.id, roomId })).rejects.toThrow(/about to start/);
    const late = await t.mutation(api.players.create, { displayName: "Chris" });
    await expect(t.mutation(api.rooms.join, { playerId: late.id, code })).rejects.toThrow(/full|about to start/);

    vi.setSystemTime(T0 + 5_000 + COUNTDOWN_MS);
    const racing = await poll(t, roomId);
    expect(racing.room.status).toBe("racing");
    expect(racing.serverTime).toBe(T0 + 5_000 + COUNTDOWN_MS);
    await expect(t.mutation(api.rooms.join, { playerId: late.id, code })).rejects.toThrow(/already started/);
  });

  it("starts a solo room when minPlayers is 1", async () => {
    const t = convexTest(schema, modules);
    const tim = await t.mutation(api.players.create, { displayName: "Tim" });
    const { room } = await t.mutation(api.rooms.create, { playerId: tim.id, minPlayers: 1 });
    vi.setSystemTime(T0 + 1_000);
    const ready = await t.mutation(api.rooms.setReady, { playerId: tim.id, roomId: room.id, ready: true });
    expect(ready.room).toMatchObject({ status: "countdown", startsAt: T0 + 1_000 + COUNTDOWN_MS });
  });
});

describe("race", () => {
  it("orders places by route progress and finishes racers in order", async () => {
    const setup = await fourPlayers();
    const { t, roomId, tim, maya, jun, rosa } = setup;
    const room = await startRace(setup);

    await t.mutation(api.race.update, { playerId: tim.id, roomId, position: at(100, 6), heading: 0 });
    await t.mutation(api.race.update, { playerId: maya.id, roomId, position: at(300, -4), heading: 0 });
    await t.mutation(api.race.update, { playerId: jun.id, roomId, position: at(200), heading: 0 });
    const own = await t.mutation(api.race.update, { playerId: rosa.id, roomId, position: at(50), heading: 0 });
    expect(own).toEqual({ status: "racing", progress: 50, metersToFinish: 750, finished: false, finishMs: 0 });

    const mid = await poll(t, roomId);
    expect(mid.racers.map((r) => [r.displayName, r.place, r.progress])).toEqual([
      ["Maya", 1, 300],
      ["Jun", 2, 200],
      ["Tim", 3, 100],
      ["Rosa", 4, 50],
    ]);
    expect(mid.racers[0]).toMatchObject({ position: at(300, -4), metersToFinish: 500 });

    vi.setSystemTime(room.startsAt + 70_000);
    const timDone = await t.mutation(api.race.update, { playerId: tim.id, roomId, position: at(790), heading: 0 });
    expect(timDone).toMatchObject({ finished: true, finishMs: 70_000, status: "racing" });
    vi.setSystemTime(room.startsAt + 70_500);
    await t.mutation(api.race.update, { playerId: maya.id, roomId, position: at(799), heading: 0 });
    vi.setSystemTime(room.startsAt + 75_000);
    await t.mutation(api.race.update, { playerId: rosa.id, roomId, position: at(800, 5), heading: 0 });

    const three = await poll(t, roomId);
    expect(three.room).toMatchObject({ status: "racing", closesAt: room.startsAt + 70_000 + CLOSE_AFTER_FIRST_FINISH_MS });
    expect(three.racers.map((r) => [r.displayName, r.place, r.finished])).toEqual([
      ["Tim", 1, true],
      ["Maya", 2, true],
      ["Rosa", 3, true],
      ["Jun", 4, false],
    ]);
    expect(three.results).toEqual([]);

    const after = await t.mutation(api.race.update, { playerId: tim.id, roomId, position: at(0), heading: 180 });
    expect(after).toMatchObject({ finished: true, finishMs: 70_000, progress: 790 });

    vi.setSystemTime(room.startsAt + 90_000);
    const last = await t.mutation(api.race.update, { playerId: jun.id, roomId, position: at(805), heading: 0 });
    expect(last).toMatchObject({ status: "finished", finished: true, finishMs: 90_000 });

    const done = await poll(t, roomId);
    expect(done.room.status).toBe("finished");
    expect(done.results.map((r) => [r.place, r.displayName, r.finishMs, r.award])).toEqual([
      [1, "Tim", 70_000, "PHOTO FINISH"],
      [2, "Maya", 70_500, "PHOTO FINISH"],
      [3, "Rosa", 75_000, "PODIUM FINISH"],
      [4, "Jun", 90_000, "SCENIC ROUTE"],
    ]);
  });

  it("closes the race 60 s after the first finish and gives awards", async () => {
    const setup = await fourPlayers();
    const { t, roomId, tim, maya, jun, rosa } = setup;
    const room = await startRace(setup);
    await t.mutation(api.events.fire, { playerId: jun.id, roomId, type: "fog", params: { level: "thick" } });
    await t.mutation(api.events.fire, { playerId: jun.id, roomId, type: "ramp", params: {} });
    await t.mutation(api.events.fire, { playerId: rosa.id, roomId, type: "ice", params: { level: "on" } });

    await t.mutation(api.race.update, { playerId: maya.id, roomId, position: at(400), heading: 0 });
    vi.setSystemTime(room.startsAt + 50_000);
    await t.mutation(api.race.update, { playerId: tim.id, roomId, position: at(800), heading: 0 });
    vi.setSystemTime(room.startsAt + 52_000);
    await t.mutation(api.race.update, { playerId: jun.id, roomId, position: at(800), heading: 0 });
    await t.mutation(api.rooms.leave, { playerId: rosa.id, roomId });

    vi.setSystemTime(room.startsAt + 50_000 + CLOSE_AFTER_FIRST_FINISH_MS);
    const done = await poll(t, roomId);
    expect(done.room.status).toBe("finished");
    expect(done.results.map((r) => [r.displayName, r.place, r.finished, r.calls, r.award])).toEqual([
      ["Tim", 1, true, 0, "FIRST TO YC"],
      ["Jun", 2, true, 2, "CHAOS CALLER"],
      ["Maya", 3, false, 0, "STILL DRIVING"],
      ["Rosa", 4, false, 1, "STILL DRIVING"],
    ]);
    expect(done.racers[3]).toMatchObject({ displayName: "Rosa", left: true });
    await expect(
      t.mutation(api.events.fire, { playerId: tim.id, roomId, type: "ramp", params: {} }),
    ).rejects.toThrow(/over/);
    await expect(t.mutation(api.race.update, { playerId: rosa.id, roomId, position: at(0), heading: 0 })).rejects.toThrow(
      /left/,
    );
  });

  it("does not finish anyone before the start", async () => {
    const setup = await fourPlayers();
    const { t, roomId, tim } = setup;
    const lobby = await t.mutation(api.race.update, { playerId: tim.id, roomId, position: at(800), heading: 0 });
    expect(lobby).toMatchObject({ status: "lobby", finished: false, progress: 800 });
  });

  it("rematches into a fresh lobby with the next race number", async () => {
    const t = convexTest(schema, modules);
    const tim = await t.mutation(api.players.create, { displayName: "Tim" });
    const { room } = await t.mutation(api.rooms.create, { playerId: tim.id, minPlayers: 1 });
    const ready = await t.mutation(api.rooms.setReady, { playerId: tim.id, roomId: room.id, ready: true });
    vi.setSystemTime(ready.room.startsAt);
    await t.mutation(api.events.fire, { playerId: tim.id, roomId: room.id, type: "time", params: { timeOfDay: "dusk" } });
    await t.mutation(api.race.update, { playerId: tim.id, roomId: room.id, position: at(800), heading: 0 });
    expect((await poll(t, room.id)).room.status).toBe("finished");

    vi.setSystemTime(T0 + 20_000);
    const rematch = await t.mutation(api.rooms.setReady, { playerId: tim.id, roomId: room.id, ready: true });
    expect(rematch.room).toMatchObject({ raceNumber: 2, status: "countdown", startsAt: T0 + 20_000 + COUNTDOWN_MS });
    const fresh = await poll(t, room.id);
    expect(fresh.events).toEqual([]);
    expect(fresh.cursor).toBe(1);
    expect(fresh.racers[0]).toMatchObject({ finished: false, progress: 0, ready: true });
  });
});

describe("world-change events", () => {
  it("appends events in order and returns them after a cursor", async () => {
    const { t, roomId, maya, jun } = await fourPlayers();
    vi.setSystemTime(T0 + 1_000);
    const first = await t.mutation(api.events.fire, {
      playerId: maya.id,
      roomId,
      type: "gravity",
      params: { level: "low" },
    });
    expect(first).toMatchObject({
      seq: 1,
      raceNumber: 1,
      type: "gravity",
      params: { level: "low" },
      playerId: maya.id,
      displayName: "Maya",
      color: "purple",
      serverTime: T0 + 1_000,
      news: "MAYA LOWERED GRAVITY",
    });
    const second = await t.mutation(api.events.fire, {
      playerId: jun.id,
      roomId,
      type: "recolor",
      params: { color: "yellow", hex: "#000000" },
    });
    expect(second).toMatchObject({ seq: 2, params: { color: "yellow", hex: "#FFCB1F" }, news: "JUN PAINTED CARS YELLOW" });
    const swap = await t.mutation(api.events.fire, {
      playerId: jun.id,
      roomId,
      type: "swap",
      params: { targetPlayerId: maya.id },
    });
    expect(swap).toMatchObject({ params: { playerId: jun.id, targetPlayerId: maya.id }, news: "JUN SWAPPED WITH MAYA" });

    const all = await poll(t, roomId);
    expect(all.events.map((e) => e.seq)).toEqual([1, 2, 3]);
    expect(all.cursor).toBe(3);
    const tail = await poll(t, roomId, 1);
    expect(tail.events.map((e) => e.type)).toEqual(["recolor", "swap"]);
    expect((await poll(t, roomId, 3)).events).toEqual([]);
    expect((await poll(t, roomId, 3)).cursor).toBe(3);
  });

  it("accepts JsonUtility's full params object and writes rain and snow news", async () => {
    const { t, roomId, maya } = await fourPlayers();
    const full = { level: "", timeOfDay: "", color: "", hex: "", playerId: "", targetPlayerId: "", size: "", count: 0 };
    const rain = await t.mutation(api.events.fire, { playerId: maya.id, roomId, type: "rain", params: { ...full, level: "on" } });
    expect(rain).toMatchObject({ params: { level: "on" }, news: "MAYA MADE IT RAIN" });
    const snow = await t.mutation(api.events.fire, { playerId: maya.id, roomId, type: "snow", params: { ...full, level: "off" } });
    expect(snow).toMatchObject({ params: { level: "off" }, news: "MAYA STOPPED THE SNOW" });
    const ramp = await t.mutation(api.events.fire, { playerId: maya.id, roomId, type: "ramp", params: full });
    expect(ramp.params).toEqual({ count: 3 });
  });

  it("rejects unknown types, bad params and outsiders", async () => {
    const { t, roomId, maya } = await fourPlayers();
    const fire = (type: string, params: object, playerId = maya.id) =>
      t.mutation(api.events.fire, { playerId, roomId, type, params });
    await expect(fire("earthquake", {})).rejects.toThrow(/type must be one of/);
    await expect(fire("gravity", { level: "sideways" })).rejects.toThrow(/params.level/);
    await expect(fire("recolor", { color: "plaid" })).rejects.toThrow(/params.color/);
    await expect(fire("ramp", { count: 9 })).rejects.toThrow(/params.count/);
    await expect(fire("swap", { targetPlayerId: maya.id })).rejects.toThrow(/yourself/);
    const outsider = await t.mutation(api.players.create, { displayName: "Chris" });
    await expect(fire("swap", { targetPlayerId: outsider.id })).rejects.toThrow(/another racer/);
    await expect(fire("ice", { level: "on" }, outsider.id)).rejects.toThrow(/not in room/);
  });

  it("keeps every news line to four words or fewer", () => {
    const changes: WorldChange[] = [
      { type: "gravity", params: { level: "low" } },
      { type: "gravity", params: { level: "high" } },
      { type: "gravity", params: { level: "normal" } },
      { type: "ice", params: { level: "on" } },
      { type: "ice", params: { level: "off" } },
      { type: "fog", params: { level: "thick" } },
      { type: "fog", params: { level: "clear" } },
      ...["day", "golden", "dusk", "night"].map((timeOfDay) => ({ type: "time" as const, params: { timeOfDay } })),
      { type: "traffic", params: { level: "heavy" } },
      { type: "traffic", params: { level: "light" } },
      { type: "recolor", params: { color: "lilac" } },
      { type: "swap", params: { targetPlayerId: "x" } },
      ...["small", "big", "normal"].map((size) => ({ type: "resize" as const, params: { size } })),
      { type: "ramp", params: { count: 3 } },
      ...["rain", "snow"].flatMap((type) =>
        ["on", "off"].map((level) => ({ type: type as "rain" | "snow", params: { level } })),
      ),
    ];
    expect(new Set(changes.map((c) => c.type))).toEqual(new Set(WORLD_CHANGE_TYPES));
    for (const change of changes) {
      const news = newsText(change, "Maximilian Longname", "Rosa Parks");
      expect(news.split(" ").length, news).toBeLessThanOrEqual(4);
      expect(news).toBe(news.toUpperCase());
    }
  });
});

describe("Operator", () => {
  const cases: [string, string, object, string][] = [
    ["make every car yellow", "recolor", { color: "yellow", hex: "#FFCB1F" }, "Yellow it is!"],
    ["turn off gravity", "gravity", { level: "low" }, "Gravity, off!"],
    ["more gravity, crush them", "gravity", { level: "high" }, "Heavy it is!"],
    ["make the roads icy", "ice", { level: "on" }, "Roads on ice!"],
    ["bring in the fog", "fog", { level: "thick" }, "Fog's rolling in!"],
    ["make it night", "time", { timeOfDay: "night" }, "Lights out!"],
    ["rush hour traffic please", "traffic", { level: "heavy" }, "Rush hour!"],
    ["shrink everyone", "resize", { size: "small" }, "Tiny cars!"],
    ["give me ramps", "ramp", { count: 3 }, "Ramps ahead!"],
    ["make it rain", "rain", { level: "on" }, "Make it rain!"],
    ["stop the storm", "rain", { level: "off" }, "Rain's stopped."],
    ["blizzard time", "snow", { level: "on" }, "Let it snow!"],
    ["no more snow", "snow", { level: "off" }, "Snow's done."],
  ];

  it.each(cases)("maps %j to %s", async (text, type, params, line) => {
    const { t, roomId, tim } = await fourPlayers();
    const reply = await t.action(api.operator.call, { playerId: tim.id, roomId, text });
    expect(reply).toMatchObject({ line, command: { type, params }, provider: "keyword" });
    expect(reply.event).toMatchObject({ seq: 1, type, params, playerId: tim.id });
    expect((await poll(t, roomId)).events).toEqual([reply.event]);
  });

  it("picks the earliest keyword, swaps with a named or the racer ahead, and always returns a change", async () => {
    const setup = await fourPlayers();
    const { t, roomId, tim, maya, jun } = setup;
    await startRace(setup);
    await t.mutation(api.race.update, { playerId: maya.id, roomId, position: at(300), heading: 0 });
    await t.mutation(api.race.update, { playerId: jun.id, roomId, position: at(200), heading: 0 });
    await t.mutation(api.race.update, { playerId: tim.id, roomId, position: at(100), heading: 0 });

    const call = (text: string) => t.action(api.operator.call, { playerId: tim.id, roomId, text });
    expect((await call("pink fog")).command).toEqual({ type: "recolor", params: { color: "pink", hex: "#FF3E8E" } });
    expect(await call("swap me with maya")).toMatchObject({
      line: "Swapping you with Maya!",
      command: { type: "swap", params: { playerId: tim.id, targetPlayerId: maya.id } },
    });
    expect((await call("switch places")).command.params.targetPlayerId).toBe(jun.id);
    expect((await call("swap me with maya")).event.news).toBe("TIM SWAPPED WITH MAYA");

    const surprise = await call("uhh hello?");
    expect(surprise.line).toMatch(/^Didn't catch that\. /);
    expect((await call("uhh hello?")).command).toEqual(surprise.command);
  });

  it("ignores names that cannot be matched and matches non-ASCII names", async () => {
    const t = convexTest(schema, modules);
    const ids = [];
    for (const displayName of ["Tim", "李", "Maya", "Zoë"]) ids.push((await t.mutation(api.players.create, { displayName })).id);
    const { room } = await t.mutation(api.rooms.create, { playerId: ids[0] });
    for (const id of ids.slice(1)) await t.mutation(api.rooms.join, { playerId: id, code: room.code });
    const swap = (playerId: string, text: string) => t.action(api.operator.call, { playerId, roomId: room.id, text });
    expect((await swap(ids[2], "swap me with tim")).command.params.targetPlayerId).toBe(ids[0]);
    expect((await swap(ids[0], "swap with zoë")).command.params.targetPlayerId).toBe(ids[3]);
    expect((await swap(ids[0], "swap me with 李")).command.params.targetPlayerId).toBe(ids[1]);
  });

  it("rejects empty text and refuses audio without a voice provider", async () => {
    const { t, roomId, tim } = await fourPlayers();
    await expect(t.action(api.operator.call, { playerId: tim.id, roomId, text: "  " })).rejects.toThrow(/text/);
    await expect(
      t.action(api.operator.call, { playerId: tim.id, roomId, audio: new Uint8Array([1, 2, 3]).buffer, mimeType: "audio/wav" }),
    ).rejects.toThrow(/text only/);
  });

  it("falls back to the keyword provider for an unknown OPERATOR_PROVIDER", async () => {
    vi.stubEnv("OPERATOR_PROVIDER", "some-llm");
    const { t, roomId, tim } = await fourPlayers();
    const reply = await t.action(api.operator.call, { playerId: tim.id, roomId, text: "yellow" });
    expect(reply).toMatchObject({ provider: "keyword", command: { type: "recolor" } });
  });
});

async function call(t: ReturnType<typeof convexTest>, method: string, path: string, body?: unknown, headers = {}) {
  const response = await t.fetch(path, {
    method,
    headers: { "Content-Type": "application/json", ...headers },
    body: body === undefined ? undefined : body instanceof Uint8Array ? (body as BodyInit) : JSON.stringify(body),
  });
  return { status: response.status, body: await response.json() };
}

describe("race HTTP API", () => {
  it("runs a four-player race over HTTP", async () => {
    const t = convexTest(schema, modules);
    const ids: string[] = [];
    for (const displayName of ["Tim", "Maya", "Jun", "Rosa"]) {
      ids.push((await call(t, "POST", "/api/players", { displayName })).body.player.id);
    }
    const route = [at(0), at(400), at(400, 300)];
    const created = await call(t, "POST", "/api/rooms", { playerId: ids[0], route, finishRadius: 10 });
    expect(created.status).toBe(201);
    expect(created.body.room).toMatchObject({ routeLength: 700, finish: at(400, 300), finishRadius: 10 });
    const { id: roomId, code } = created.body.room;
    for (const playerId of ids.slice(1)) {
      expect((await call(t, "POST", "/api/rooms/join", { playerId, code })).status).toBe(200);
    }
    let ready;
    for (const playerId of ids) ready = await call(t, "POST", "/api/rooms/ready", { playerId, roomId, ready: true });
    expect(ready!.body.room.status).toBe("countdown");
    vi.setSystemTime(ready!.body.room.startsAt);

    const update = await call(t, "POST", "/api/race/update", {
      playerId: ids[1],
      roomId,
      position: at(400, 100),
      heading: 90,
    });
    expect(update).toEqual({
      status: 200,
      body: { status: "racing", progress: 500, metersToFinish: 200, finished: false, finishMs: 0 },
    });

    const fired = await call(t, "POST", "/api/rooms/events", { playerId: ids[2], roomId, type: "ramp" });
    expect(fired.status).toBe(201);
    expect(fired.body.event.news).toBe("JUN DROPPED RAMPS");
    const op = await call(t, "POST", "/api/operator", { playerId: ids[3], roomId, text: "everything yellow!" });
    expect(op.status).toBe(200);
    expect(op.body).toMatchObject({ line: "Yellow it is!", command: { type: "recolor" }, event: { seq: 2 } });

    const polled = await call(t, "GET", `/api/rooms/poll?roomId=${roomId}&after=1`);
    expect(polled.status).toBe(200);
    expect(polled.body.racers[0]).toMatchObject({ displayName: "Maya", place: 1 });
    expect(polled.body.events.map((e: { seq: number }) => e.seq)).toEqual([2]);
    expect(polled.body.cursor).toBe(2);

    for (const playerId of ids) {
      await call(t, "POST", "/api/race/update", { playerId, roomId, position: at(400, 295), heading: 90 });
    }
    const results = await call(t, "GET", `/api/rooms/poll?roomId=${roomId}&after=2`);
    expect(results.body.room.status).toBe("finished");
    expect(results.body.results).toHaveLength(4);
  });

  it("maps race errors to status codes", async () => {
    const t = convexTest(schema, modules);
    const playerId = (await call(t, "POST", "/api/players", { displayName: "Tim" })).body.player.id;
    const roomId = (await call(t, "POST", "/api/rooms", { playerId })).body.room.id;
    const other = (await call(t, "POST", "/api/players", { displayName: "Maya" })).body.player.id;

    expect((await call(t, "GET", `/api/rooms/poll?roomId=${roomId}&after=-1`)).status).toBe(400);
    expect((await call(t, "GET", `/api/rooms/poll?roomId=${roomId}&after=x`)).status).toBe(400);
    expect((await call(t, "GET", "/api/rooms/poll?roomId=nope")).status).toBe(404);
    expect((await call(t, "POST", "/api/rooms/join", { playerId, code: "ZZZZ" })).status).toBe(404);
    expect((await call(t, "POST", "/api/race/update", { playerId: other, roomId, position: at(0), heading: 0 })).status).toBe(409);
    const badType = await call(t, "POST", "/api/rooms/events", { playerId, roomId, type: "meteor" });
    expect(badType).toMatchObject({ status: 400, body: { error: { code: "INVALID_ARGUMENT" } } });
    expect((await call(t, "POST", "/api/operator", { playerId, roomId })).status).toBe(400);

    const audio = await call(
      t,
      "POST",
      `/api/operator/audio?playerId=${playerId}&roomId=${roomId}`,
      new Uint8Array([82, 73, 70, 70]),
      { "Content-Type": "audio/wav" },
    );
    expect(audio).toMatchObject({ status: 501, body: { error: { code: "NOT_IMPLEMENTED" } } });
  });
});
