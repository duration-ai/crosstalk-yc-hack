import { convexTest } from "convex-test";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, internal } from "./_generated/api";
import schema from "./schema";
import { PRESENCE_TIMEOUT_MS, SHARD_CAP } from "./world";

const modules = import.meta.glob("./**/*.*s");
const T0 = Date.UTC(2026, 10, 15, 21, 0, 0);

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(T0);
});
afterEach(() => vi.useRealTimers());

const here = { x: 1, y: 0, z: 2 };

async function beat(t: ReturnType<typeof convexTest>, playerId: string, now = Date.now(), extra = {}) {
  const me = await t.mutation(internal.world.heartbeat, { playerId, position: here, heading: 90, speed: 4, mode: "onFoot", vehicleType: "", now, ...extra });
  const players = await t.query(internal.world.snapshot, { shard: me.shard, playerId, now });
  return { ...me, players };
}

describe("world presence", () => {
  it("puts players in one shard where they see each other in different colours", async () => {
    const t = convexTest(schema, modules);
    const a = await t.mutation(api.players.create, { displayName: "Tim" });
    const b = await t.mutation(api.players.create, { displayName: "Maya" });
    const first = await beat(t, a.id);
    const second = await beat(t, b.id, Date.now(), { mode: "driving", vehicleType: "sedan", speed: 12 });
    expect(first.shard).toBe(0);
    expect(second.shard).toBe(0);
    expect(second.slot).not.toBe(first.slot);
    expect(second.worldRoomId).toBe(first.worldRoomId);
    const seen = (await beat(t, a.id)).players;
    expect(seen).toHaveLength(1);
    expect(seen[0]).toMatchObject({ playerId: b.id, displayName: "Maya", mode: "driving", vehicleType: "sedan", speed: 12, heading: 90 });
    expect(seen[0].colorHex).toMatch(/^#/);
  });

  it("drops presence after the timeout and rejoins on the next heartbeat", async () => {
    const t = convexTest(schema, modules);
    const a = await t.mutation(api.players.create, { displayName: "Tim" });
    const b = await t.mutation(api.players.create, { displayName: "Maya" });
    await beat(t, a.id);
    await beat(t, b.id);
    const later = T0 + PRESENCE_TIMEOUT_MS + 1;
    expect((await beat(t, a.id, later)).players).toHaveLength(0);
    expect((await beat(t, b.id, later)).players).toHaveLength(1);
  });

  it("opens a second shard past the cap", async () => {
    const t = convexTest(schema, modules);
    const shards: number[] = [];
    for (let i = 0; i <= SHARD_CAP; i++) {
      const p = await t.mutation(api.players.create, { displayName: "P" + i });
      shards.push((await beat(t, p.id)).shard);
    }
    expect(shards.filter((s) => s === 0)).toHaveLength(SHARD_CAP);
    expect(shards[SHARD_CAP]).toBe(1);
  });

  it("keeps world-room membership when the player joins a race room", async () => {
    const t = convexTest(schema, modules);
    const a = await t.mutation(api.players.create, { displayName: "Tim" });
    const { worldRoomId } = await beat(t, a.id);
    await t.mutation(api.rooms.create, { playerId: a.id });
    const rows = await t.run((ctx) => ctx.db.query("roomPlayers").collect());
    const world = rows.find((r) => r.roomId === worldRoomId)!;
    expect(world.left).toBe(false);
    const room = await t.run((ctx) => ctx.db.get(worldRoomId));
    expect(room).toMatchObject({ kind: "world", worldShard: 0, freeAsks: true, status: "lobby" });
  });

  it("takes a timed-out player out of the world room and refuses race actions on it", async () => {
    const t = convexTest(schema, modules);
    const a = await t.mutation(api.players.create, { displayName: "Tim" });
    const b = await t.mutation(api.players.create, { displayName: "Maya" });
    const { worldRoomId } = await beat(t, a.id);
    await beat(t, b.id);
    const members = () => t.run(async (ctx) => (await ctx.db.query("roomPlayers").collect()).filter((r) => r.roomId === worldRoomId).map((r) => r.playerId));
    expect(await members()).toHaveLength(2);
    const code = (await t.run((ctx) => ctx.db.get(worldRoomId)))!.code;
    await expect(t.mutation(api.rooms.join, { playerId: b.id, code })).rejects.toThrow(/not a race/);
    await expect(t.mutation(api.rooms.setReady, { playerId: a.id, roomId: worldRoomId, ready: true })).rejects.toThrow(/not a race/);
    await beat(t, b.id, T0 + PRESENCE_TIMEOUT_MS + 1);
    await t.mutation(internal.world.sweepStale, { now: T0 + PRESENCE_TIMEOUT_MS + 1 });
    expect(await members()).toEqual([b.id]);
    expect(await t.run((ctx) => ctx.db.query("worldPresence").collect())).toHaveLength(1);
    const back = await beat(t, a.id, T0 + PRESENCE_TIMEOUT_MS + 2);
    expect(back.worldRoomId).toBe(worldRoomId);
    expect((await members()).sort()).toEqual([a.id, b.id].sort());
    expect(await t.run((ctx) => ctx.db.get(worldRoomId))).toMatchObject({ status: "lobby" });
  });

  it("leaving the world removes the world-room membership", async () => {
    const t = convexTest(schema, modules);
    const a = await t.mutation(api.players.create, { displayName: "Tim" });
    await beat(t, a.id);
    await t.mutation(internal.world.leave, { playerId: a.id });
    expect(await t.run((ctx) => ctx.db.query("roomPlayers").collect())).toHaveLength(0);
  });

  it("rejects non-finite positions", async () => {
    const t = convexTest(schema, modules);
    const a = await t.mutation(api.players.create, { displayName: "Tim" });
    await expect(beat(t, a.id, Date.now(), { heading: Number.NaN })).rejects.toThrow(/finite/);
  });
});
