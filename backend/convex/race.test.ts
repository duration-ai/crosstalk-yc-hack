import { convexTest } from "convex-test";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, internal } from "./_generated/api";
import type { Id } from "./_generated/dataModel";
import schema from "./schema";
import { AUTO_START_MS, CLOSE_AFTER_FIRST_FINISH_MS, COUNTDOWN_MS, DEFAULT_ASKS, MAX_ASKS } from "./raceModel";
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
      ["Tim", 1, true, 0, "FIRST PAST THE FLAG"],
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

  it("keeps the best smash score and gives WRECKING BALL after CHAOS CALLER", async () => {
    const setup = await fourPlayers();
    const { t, roomId, tim, maya, jun, rosa } = setup;
    await t.mutation(api.race.update, { playerId: tim.id, roomId, position: at(0), heading: 0, smash: 50 });
    const room = await startRace(setup);
    const smash = (playerId: string, z: number, points?: number) =>
      t.mutation(api.race.update, { playerId, roomId, position: at(z), heading: 0, smash: points });

    await smash(maya.id, 100, 4);
    await smash(maya.id, 120, 2);
    await smash(maya.id, 130);
    await smash(jun.id, 90, 4);
    await t.mutation(api.events.fire, { playerId: jun.id, roomId, type: "ramp", params: {} });
    await expect(smash(rosa.id, 10, -1)).rejects.toThrow(/smash/);
    await expect(smash(rosa.id, 10, 1.5)).rejects.toThrow(/smash/);
    const mid = await poll(t, roomId);
    expect(Object.fromEntries(mid.racers.map((r) => [r.displayName, r.smash]))).toEqual({ Tim: 0, Maya: 4, Jun: 4, Rosa: 0 });

    for (const [i, p] of [tim, maya, jun, rosa].entries()) {
      vi.setSystemTime(room.startsAt + 60_000 + i * 5_000);
      await smash(p.id, 800);
    }
    await smash(tim.id, 800, 99);
    const done = await poll(t, roomId);
    expect(done.results.map((r) => [r.displayName, r.smash, r.calls, r.award])).toEqual([
      ["Tim", 0, 0, "FIRST PAST THE FLAG"],
      ["Maya", 4, 0, "WRECKING BALL"],
      ["Jun", 4, 1, "CHAOS CALLER"],
      ["Rosa", 0, 0, "SCENIC ROUTE"],
    ]);

    await t.mutation(api.rooms.setReady, { playerId: tim.id, roomId, ready: true });
    expect((await poll(t, roomId)).racers.every((r) => r.smash === 0)).toBe(true);
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
    const second = await call(t, "POST", "/api/rooms/events", { playerId: ids[3], roomId, type: "ramp" });
    expect(second.body.event).toMatchObject({ seq: 2, news: "ROSA DROPPED RAMPS" });

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

  });
});

describe("asks", () => {
  const pickups = [at(100, 60), at(400, -80)];

  async function soloRace(asks?: number) {
    const t = convexTest(schema, modules);
    const tim = await t.mutation(api.players.create, { displayName: "Tim" });
    const { room, you } = await t.mutation(api.rooms.create, { playerId: tim.id, minPlayers: 1, asks, pickups, pickupRadius: 4 });
    return { t, tim, room, you };
  }

  async function go(t: ReturnType<typeof convexTest>, playerId: string, roomId: string) {
    const ready = await t.mutation(api.rooms.setReady, { playerId, roomId, ready: true });
    vi.setSystemTime(ready.room.startsAt);
  }

  it("starts every racer with the room's asks and publishes the pickups", async () => {
    const { t, room, you } = await soloRace();
    expect(room).toMatchObject({ asks: DEFAULT_ASKS, pickups, pickupRadius: 4 });
    expect(you).toMatchObject({ asks: DEFAULT_ASKS, collected: [] });
    const rosa = await t.mutation(api.players.create, { displayName: "Rosa" });
    const joined = await t.mutation(api.rooms.join, { playerId: rosa.id, code: room.code });
    expect(joined.you).toMatchObject({ asks: DEFAULT_ASKS, collected: [] });
    expect((await soloRace(3)).you.asks).toBe(3);
  });

  it("rejects bad ask settings", async () => {
    const t = convexTest(schema, modules);
    const tim = await t.mutation(api.players.create, { displayName: "Tim" });
    await expect(t.mutation(api.rooms.create, { playerId: tim.id, asks: MAX_ASKS + 1 })).rejects.toThrow(/asks/);
    await expect(t.mutation(api.rooms.create, { playerId: tim.id, pickupRadius: 50 })).rejects.toThrow(/pickupRadius/);
    const many = Array.from({ length: 65 }, (_, i) => at(i));
    await expect(t.mutation(api.rooms.create, { playerId: tim.id, pickups: many })).rejects.toThrow(/pickups/);
  });

  it("collects each pickup once a race, only while racing and only up close", async () => {
    const { t, tim, room } = await soloRace();
    const collect = (pickup: number, position = pickups[pickup]) =>
      t.mutation(api.race.collect, { playerId: tim.id, roomId: room.id, pickup, position });
    await expect(collect(0)).rejects.toThrow(/while racing/);
    await go(t, tim.id, room.id);

    await expect(collect(0, at(100, 40))).rejects.toThrow(/Too far/);
    await expect(collect(2, at(0))).rejects.toThrow(/pickup must index/);
    expect(await collect(0, at(103, 63))).toEqual({ collected: true, asks: DEFAULT_ASKS + 1 });
    expect(await collect(0)).toEqual({ collected: false, asks: DEFAULT_ASKS + 1 });
    expect(await collect(1)).toEqual({ collected: true, asks: DEFAULT_ASKS + 2 });
    expect((await poll(t, room.id)).racers[0]).toMatchObject({ asks: DEFAULT_ASKS + 2, collected: [0, 1] });
  });

  it("spends asks for Operator requests until none are left", async () => {
    const { t, tim, room } = await soloRace(2);
    const spend = () => t.mutation(internal.race.spendAsk, { playerId: tim.id as Id<"players">, roomId: room.id as Id<"rooms"> });
    expect(await spend()).toEqual({ asks: 1 });
    expect(await spend()).toEqual({ asks: 0 });
    await expect(spend()).rejects.toThrow(/No asks left/);
    expect((await poll(t, room.id)).racers[0].asks).toBe(0);
  });

  it("gives everyone their asks back and clears pickups on the rematch", async () => {
    const { t, tim, room } = await soloRace(2);
    await go(t, tim.id, room.id);
    await t.mutation(api.race.collect, { playerId: tim.id, roomId: room.id, pickup: 0, position: pickups[0] });
    await t.mutation(internal.race.spendAsk, { playerId: tim.id as Id<"players">, roomId: room.id as Id<"rooms"> });
    await t.mutation(api.race.update, { playerId: tim.id, roomId: room.id, position: at(800), heading: 0 });
    expect((await poll(t, room.id)).room.status).toBe("finished");
    await expect(t.mutation(internal.race.spendAsk, { playerId: tim.id as Id<"players">, roomId: room.id as Id<"rooms"> })).rejects.toThrow(/over/);

    vi.setSystemTime(T0 + 30_000);
    await t.mutation(api.rooms.setReady, { playerId: tim.id, roomId: room.id, ready: true });
    expect((await poll(t, room.id)).racers[0]).toMatchObject({ asks: 2, collected: [] });
  });
});
