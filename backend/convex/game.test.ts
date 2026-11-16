import { convexTest } from "convex-test";
import { describe, expect, it } from "vitest";
import { api, internal } from "./_generated/api";
import schema from "./schema";

const modules = import.meta.glob("./**/*.*s");

const origin = { x: 0, y: 0, z: 0 };
const pier70 = { x: 120, y: 0.5, z: -40 };

async function setup() {
  const t = convexTest(schema, modules);
  const player = await t.mutation(api.players.create, { displayName: "Tim" });
  await t.mutation(internal.vehicles.seed, {
    vehicles: [
      { model: "hatchback", position: pier70, heading: 90 },
      { model: "pickup", position: origin, heading: 0 },
    ],
  });
  const [hatchback, pickup] = await t.query(api.vehicles.list, {});
  return { t, player, hatchback, pickup };
}

async function call(t: ReturnType<typeof convexTest>, method: string, path: string, body?: unknown) {
  const response = await t.fetch(path, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : typeof body === "string" ? body : JSON.stringify(body),
  });
  return { status: response.status, body: await response.json() };
}

describe("players", () => {
  it("creates and gets a player", async () => {
    const t = convexTest(schema, modules);
    const created = await t.mutation(api.players.create, { displayName: "  Tim  " });
    expect(created.displayName).toBe("Tim");
    expect(await t.query(api.players.get, { playerId: created.id })).toEqual(created);
  });

  it("renames a player in place, and the rooms they are in follow", async () => {
    const t = convexTest(schema, modules);
    const tim = await t.mutation(api.players.create, { displayName: "Racer 42" });
    const { room } = await t.mutation(api.rooms.create, { playerId: tim.id });
    const renamed = await t.mutation(api.players.rename, { playerId: tim.id, displayName: "  Sudhir " });
    expect(renamed).toEqual({ ...tim, displayName: "Sudhir" });
    expect(await t.query(api.players.get, { playerId: tim.id })).toEqual(renamed);
    expect(await t.run((ctx) => ctx.db.query("players").collect())).toHaveLength(1);
    const seats = await t.run((ctx) => ctx.db.query("roomPlayers").collect());
    expect(seats.map((s) => [s.roomId, s.displayName])).toEqual([[room.id, "Sudhir"]]);
  });

  it("rejects a blank or too long rename and keeps the old name", async () => {
    const t = convexTest(schema, modules);
    const tim = await t.mutation(api.players.create, { displayName: "Tim" });
    for (const displayName of ["   ", "x".repeat(33)]) {
      await expect(t.mutation(api.players.rename, { playerId: tim.id, displayName })).rejects.toThrow(/INVALID_ARGUMENT|displayName/);
    }
    expect((await t.query(api.players.get, { playerId: tim.id })).displayName).toBe("Tim");
  });

  it("renames over HTTP", async () => {
    const t = convexTest(schema, modules);
    const tim = await t.mutation(api.players.create, { displayName: "Racer 7" });
    const ok = await call(t, "POST", "/api/players/rename", { playerId: tim.id, displayName: "Sudhir" });
    expect(ok).toEqual({ status: 200, body: { player: { ...tim, displayName: "Sudhir" } } });
    const bad = await call(t, "POST", "/api/players/rename", { playerId: tim.id, displayName: " " });
    expect(bad.status).toBe(400);
    expect(bad.body.error.code).toBe("INVALID_ARGUMENT");
  });

  it("rejects an empty display name", async () => {
    const t = convexTest(schema, modules);
    await expect(t.mutation(api.players.create, { displayName: "   " })).rejects.toThrow(
      /displayName/,
    );
  });
});

describe("player state", () => {
  it("returns null before the first save, then round-trips a save", async () => {
    const { t, player } = await setup();
    expect(await t.query(api.playerState.load, { playerId: player.id })).toBeNull();

    const saved = await t.mutation(api.playerState.save, {
      playerId: player.id,
      position: pier70,
      heading: 45,
      mode: "onFoot",
      vehicleId: null,
    });
    expect(saved).toMatchObject({ position: pier70, heading: 45, mode: "onFoot", vehicleId: null });
    expect(await t.query(api.playerState.load, { playerId: player.id })).toEqual(saved);
  });

  it("refuses driving state for a vehicle the player is not in", async () => {
    const { t, player, hatchback } = await setup();
    await expect(
      t.mutation(api.playerState.save, {
        playerId: player.id,
        position: origin,
        heading: 0,
        mode: "driving",
        vehicleId: hatchback.id,
      }),
    ).rejects.toThrow(/enter it first/);
  });

  it("moves the occupied vehicle with a driving save", async () => {
    const { t, player, hatchback } = await setup();
    await t.mutation(api.vehicles.enter, { playerId: player.id, vehicleId: hatchback.id });
    const moved = { x: 200, y: 0.5, z: -10 };
    await t.mutation(api.playerState.save, {
      playerId: player.id,
      position: moved,
      heading: 30,
      mode: "driving",
      vehicleId: hatchback.id,
    });

    const listed = await t.query(api.vehicles.list, {});
    expect(listed.find((v) => v.id === hatchback.id)).toMatchObject({ position: moved, heading: 30 });

    const exited = await t.mutation(api.vehicles.exit, { playerId: player.id, vehicleId: hatchback.id });
    expect(exited.state).toMatchObject({ mode: "onFoot", position: moved, heading: 30 });
  });

  it("refuses on-foot state while the player occupies a vehicle", async () => {
    const { t, player, hatchback } = await setup();
    await t.mutation(api.vehicles.enter, { playerId: player.id, vehicleId: hatchback.id });
    await expect(
      t.mutation(api.playerState.save, {
        playerId: player.id,
        position: origin,
        heading: 0,
        mode: "onFoot",
      }),
    ).rejects.toThrow(/exit it first/);
  });
});

describe("vehicles", () => {
  it("seeds only an empty table", async () => {
    const { t } = await setup();
    const again = await t.mutation(internal.vehicles.seed, {
      vehicles: [{ model: "bus", position: origin, heading: 0 }],
    });
    expect(again).toEqual({ inserted: 0 });
    expect(await t.query(api.vehicles.list, {})).toHaveLength(2);
  });

  it("enters, drives, and exits a vehicle", async () => {
    const { t, player, hatchback } = await setup();

    const entered = await t.mutation(api.vehicles.enter, {
      playerId: player.id,
      vehicleId: hatchback.id,
    });
    expect(entered.vehicle.occupiedBy).toBe(player.id);
    expect(entered.state).toMatchObject({ mode: "driving", vehicleId: hatchback.id, position: pier70 });

    const moved = { x: 150, y: 0.5, z: -40 };
    const driven = await t.mutation(api.vehicles.update, {
      playerId: player.id,
      vehicleId: hatchback.id,
      position: moved,
      heading: 180,
    });
    expect(driven.vehicle).toMatchObject({ position: moved, heading: 180 });
    expect(driven.state).toMatchObject({ position: moved, heading: 180, mode: "driving" });

    const curb = { x: 150, y: 0, z: -38 };
    const exited = await t.mutation(api.vehicles.exit, {
      playerId: player.id,
      vehicleId: hatchback.id,
      position: curb,
    });
    expect(exited.vehicle).toMatchObject({ occupiedBy: null, position: moved });
    expect(exited.state).toMatchObject({ mode: "onFoot", vehicleId: null, position: curb, heading: 180 });

    const listed = await t.query(api.vehicles.list, {});
    expect(listed.find((v) => v.id === hatchback.id)).toEqual(exited.vehicle);
  });

  it("blocks a second driver and a second vehicle", async () => {
    const { t, player, hatchback, pickup } = await setup();
    const other = await t.mutation(api.players.create, { displayName: "Sudhir" });
    await t.mutation(api.vehicles.enter, { playerId: player.id, vehicleId: hatchback.id });

    await expect(
      t.mutation(api.vehicles.enter, { playerId: other.id, vehicleId: hatchback.id }),
    ).rejects.toThrow(/occupied/);
    await expect(
      t.mutation(api.vehicles.enter, { playerId: player.id, vehicleId: pickup.id }),
    ).rejects.toThrow(/already in vehicle/);
    await expect(
      t.mutation(api.vehicles.update, {
        playerId: other.id,
        vehicleId: hatchback.id,
        position: origin,
        heading: 0,
      }),
    ).rejects.toThrow(/Only the driver/);
    await expect(
      t.mutation(api.vehicles.exit, { playerId: other.id, vehicleId: hatchback.id }),
    ).rejects.toThrow(/not in vehicle/);
  });

  it("lets the same player re-enter idempotently", async () => {
    const { t, player, hatchback } = await setup();
    await t.mutation(api.vehicles.enter, { playerId: player.id, vehicleId: hatchback.id });
    const again = await t.mutation(api.vehicles.enter, { playerId: player.id, vehicleId: hatchback.id });
    expect(again.vehicle.occupiedBy).toBe(player.id);
  });
});

describe("HTTP API", () => {
  it("walks the full client flow over HTTP", async () => {
    const t = convexTest(schema, modules);
    await t.mutation(internal.vehicles.seed, {
      vehicles: [{ model: "hatchback", position: pier70, heading: 90 }],
    });

    const created = await call(t, "POST", "/api/players", { displayName: "Tim" });
    expect(created.status).toBe(201);
    const playerId = created.body.player.id;

    const fetched = await call(t, "GET", `/api/players?id=${playerId}`);
    expect(fetched).toEqual({ status: 200, body: { player: created.body.player } });

    expect(await call(t, "GET", `/api/player-state?playerId=${playerId}`)).toEqual({
      status: 200,
      body: { state: null },
    });

    const saved = await call(t, "PUT", "/api/player-state", {
      playerId,
      position: origin,
      heading: 10,
      mode: "onFoot",
      vehicleId: "",
    });
    expect(saved.status).toBe(200);
    expect(saved.body.state).toMatchObject({ playerId, mode: "onFoot", vehicleId: null });

    const listed = await call(t, "GET", "/api/vehicles");
    expect(listed.status).toBe(200);
    const vehicleId = listed.body.vehicles[0].id;

    const entered = await call(t, "POST", "/api/vehicles/enter", { playerId, vehicleId });
    expect(entered.status).toBe(200);
    expect(entered.body.state).toMatchObject({ mode: "driving", vehicleId });

    const driven = await call(t, "POST", "/api/vehicles/update", {
      playerId,
      vehicleId,
      position: { x: 1, y: 2, z: 3 },
      heading: 270,
    });
    expect(driven.body.vehicle).toMatchObject({ position: { x: 1, y: 2, z: 3 }, heading: 270 });

    const exited = await call(t, "POST", "/api/vehicles/exit", { playerId, vehicleId });
    expect(exited.status).toBe(200);
    expect(exited.body.state).toMatchObject({ mode: "onFoot", position: { x: 1, y: 2, z: 3 } });

    const loaded = await call(t, "GET", `/api/player-state?playerId=${playerId}`);
    expect(loaded.body.state).toEqual(exited.body.state);
  });

  it("maps errors to status codes with a JSON error body", async () => {
    const { t, player, hatchback } = await setup();
    const other = await t.mutation(api.players.create, { displayName: "Sudhir" });
    await t.mutation(api.vehicles.enter, { playerId: player.id, vehicleId: hatchback.id });

    const missing = await call(t, "GET", "/api/players?id=nope");
    expect(missing.status).toBe(404);
    expect(missing.body.error.code).toBe("NOT_FOUND");

    const occupied = await call(t, "POST", "/api/vehicles/enter", {
      playerId: other.id,
      vehicleId: hatchback.id,
    });
    expect(occupied.status).toBe(409);
    expect(occupied.body.error.code).toBe("CONFLICT");

    const badName = await call(t, "POST", "/api/players", { displayName: "" });
    expect(badName.status).toBe(400);
    expect(badName.body.error.code).toBe("INVALID_ARGUMENT");

    const badShape = await call(t, "PUT", "/api/player-state", {
      playerId: player.id,
      position: { x: "far" },
      heading: 0,
      mode: "onFoot",
    });
    expect(badShape.status).toBe(400);
    expect(badShape.body.error.code).toBe("INVALID_ARGUMENT");

    const notJson = await call(t, "POST", "/api/players", "not json");
    expect(notJson.status).toBe(400);
    expect(notJson.body.error.code).toBe("INVALID_ARGUMENT");
  });
});
