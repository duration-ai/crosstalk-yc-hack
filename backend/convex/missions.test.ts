import { convexTest } from "convex-test";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, internal } from "./_generated/api";
import schema from "./schema";
import { MISSION_COUNTDOWN_MS } from "./raceModel";

const modules = import.meta.glob("./**/*.*s");
const T0 = Date.UTC(2026, 10, 15, 22, 0, 0);

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(T0);
});
afterEach(() => vi.useRealTimers());

const here = { x: 1, y: 0, z: 2 };

describe("mission lobby", () => {
  it("puts players who enter the ring in one room, counts down only once everyone is ready, and holds again on a join", async () => {
    const t = convexTest(schema, modules);
    const ids = [];
    for (const name of ["Tim", "Maya", "Jun"]) {
      const p = await t.mutation(api.players.create, { displayName: name });
      await t.mutation(internal.world.heartbeat, { playerId: p.id, position: here, heading: 0, now: Date.now() });
      ids.push(p.id);
    }
    const a = await t.mutation(internal.missions.enter, { playerId: ids[0], mission: "yc" });
    expect(a.room.status).toBe("lobby");
    expect(a.room.playerCount).toBe(1);
    const again = await t.mutation(internal.missions.enter, { playerId: ids[0], mission: "yc" });
    expect(again.room.id).toBe(a.room.id);
    const b = await t.mutation(internal.missions.enter, { playerId: ids[1], mission: "yc" });
    expect(b.room.id).toBe(a.room.id);
    expect(b.room.status).toBe("lobby");
    const oneReady = await t.mutation(api.rooms.setReady, { playerId: ids[0], roomId: a.room.id, ready: true });
    expect(oneReady.room).toMatchObject({ status: "lobby", startsAt: 0, readyCount: 1 });
    vi.setSystemTime(T0 + 60_000);
    expect((await t.query(internal.rooms.poll, { roomId: a.room.id, now: Date.now() })).room.status).toBe("lobby");
    const bothReady = await t.mutation(api.rooms.setReady, { playerId: ids[1], roomId: a.room.id, ready: true });
    expect(bothReady.room.status).toBe("countdown");
    expect(bothReady.room.startsAt - (T0 + 60_000) - MISSION_COUNTDOWN_MS).toBeLessThan(50);
    expect(b.you.slot).not.toBe(a.you.slot);
    for (const [id, joined] of [[ids[0], a], [ids[1], b]] as const) {
      const world = await t.run((ctx) => ctx.db.query("worldPresence").collect());
      expect(joined.you.slot).toBe(world.find((w) => w.playerId === id)!.slot);
    }
    vi.setSystemTime(T0 + 64_000);
    const c = await t.mutation(internal.missions.enter, { playerId: ids[2], mission: "yc" });
    expect(c.room.id).toBe(a.room.id);
    expect(c.room).toMatchObject({ status: "lobby", startsAt: 0 });
    await expect(t.mutation(api.rooms.join, { playerId: ids[2], code: c.room.code })).rejects.toThrow(/location/);

    await t.mutation(api.rooms.leave, { playerId: ids[2], roomId: c.room.id });
    await t.mutation(api.rooms.leave, { playerId: ids[1], roomId: c.room.id });
    const poll = await t.query(internal.rooms.poll, { roomId: c.room.id, now: Date.now() });
    expect(poll.room.status).toBe("countdown");
  });

  it("opens a fresh room once the last one is racing, and keeps world membership", async () => {
    const t = convexTest(schema, modules);
    const ids = [];
    let worldRoomId: string = "";
    for (const name of ["Tim", "Maya", "Rosa"]) {
      const p = await t.mutation(api.players.create, { displayName: name });
      worldRoomId = (await t.mutation(internal.world.heartbeat, { playerId: p.id, position: here, heading: 0, now: Date.now() })).worldRoomId;
      ids.push(p.id);
    }
    const first = await t.mutation(internal.missions.enter, { playerId: ids[0], mission: "yc" });
    await t.mutation(internal.missions.enter, { playerId: ids[1], mission: "yc" });
    for (const playerId of ids.slice(0, 2)) await t.mutation(api.rooms.setReady, { playerId, roomId: first.room.id, ready: true });
    vi.setSystemTime(T0 + MISSION_COUNTDOWN_MS + 1);
    const late = await t.mutation(internal.missions.enter, { playerId: ids[2], mission: "yc" });
    expect(late.room.id).not.toBe(first.room.id);
    const retry = await t.mutation(internal.missions.enter, { playerId: ids[0], mission: "yc" });
    expect(retry.room.id).toBe(first.room.id);
    expect(retry.room.status).toBe("racing");
    const world = await t.run(async (ctx) => (await ctx.db.query("roomPlayers").collect()).filter((r) => r.roomId === worldRoomId && !r.left));
    expect(world).toHaveLength(3);
  });
});
