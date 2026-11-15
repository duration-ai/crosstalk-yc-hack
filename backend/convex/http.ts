import { httpRouter } from "convex/server";
import { ConvexError } from "convex/values";
import { api, internal } from "./_generated/api";
import { httpAction, type ActionCtx } from "./_generated/server";
import { checkPlayerId } from "./avatars";
import { COLORS } from "./raceModel";

type Handler = (ctx: ActionCtx, input: Record<string, any>) => Promise<unknown>;

const STATUS: Record<string, number> = {
  INVALID_ARGUMENT: 400,
  NOT_FOUND: 404,
  CONFLICT: 409,
  RETAKE: 422,
  RATE_LIMITED: 429,
  UNAVAILABLE: 503,
  DECISION_UNAVAILABLE: 503,
};

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function errorResponse(error: unknown) {
  if (error instanceof ConvexError && typeof error.data?.code === "string") {
    return json(STATUS[error.data.code] ?? 500, { error: error.data });
  }
  const message = error instanceof Error ? error.message : String(error);
  if (/ArgumentValidationError|Validator error/.test(message)) {
    return json(400, { error: { code: "INVALID_ARGUMENT", message } });
  }
  console.error(error);
  return json(500, { error: { code: "INTERNAL", message: "Internal error" } });
}

async function readInput(request: Request) {
  if (request.method === "GET") {
    return Object.fromEntries(new URL(request.url).searchParams);
  }
  const body = await request.json().catch(() => undefined);
  if (typeof body !== "object" || body === null || Array.isArray(body)) {
    throw new ConvexError({ code: "INVALID_ARGUMENT", message: "Request body must be a JSON object" });
  }
  return body as Record<string, unknown>;
}

function endpoint(handler: Handler, successStatus = 200, read: (request: Request) => Promise<Record<string, any>> = readInput) {
  return httpAction(async (ctx, request) => {
    try {
      return json(successStatus, await handler(ctx, await read(request)));
    } catch (error) {
      return errorResponse(error);
    }
  });
}

const http = httpRouter();

http.route({
  path: "/api/players",
  method: "POST",
  handler: endpoint(
    async (ctx, { displayName }) => ({
      player: await ctx.runMutation(api.players.create, { displayName }),
    }),
    201,
  ),
});

http.route({
  path: "/api/players",
  method: "GET",
  handler: endpoint(async (ctx, { id }) => ({
    player: await ctx.runQuery(api.players.get, { playerId: id }),
  })),
});

http.route({
  path: "/api/player-state",
  method: "GET",
  handler: endpoint(async (ctx, { playerId }) => ({
    state: await ctx.runQuery(api.playerState.load, { playerId }),
  })),
});

http.route({
  path: "/api/player-state",
  method: "PUT",
  handler: endpoint(async (ctx, { playerId, position, heading, mode, vehicleId }) => ({
    state: await ctx.runMutation(api.playerState.save, { playerId, position, heading, mode, vehicleId }),
  })),
});

http.route({
  path: "/api/vehicles",
  method: "GET",
  handler: endpoint(async (ctx) => ({ vehicles: await ctx.runQuery(api.vehicles.list, {}) })),
});

http.route({
  path: "/api/vehicles/enter",
  method: "POST",
  handler: endpoint((ctx, { playerId, vehicleId }) =>
    ctx.runMutation(api.vehicles.enter, { playerId, vehicleId }),
  ),
});

http.route({
  path: "/api/vehicles/exit",
  method: "POST",
  handler: endpoint((ctx, { playerId, vehicleId, position, heading }) =>
    ctx.runMutation(api.vehicles.exit, { playerId, vehicleId, position, heading }),
  ),
});

http.route({
  path: "/api/vehicles/update",
  method: "POST",
  handler: endpoint((ctx, { playerId, vehicleId, position, heading }) =>
    ctx.runMutation(api.vehicles.update, { playerId, vehicleId, position, heading }),
  ),
});

http.route({
  path: "/api/rooms",
  method: "POST",
  handler: endpoint(
    (ctx, { playerId, route, finishRadius, minPlayers, asks, pickups, pickupRadius }) =>
      ctx.runMutation(api.rooms.create, { playerId, route, finishRadius, minPlayers, asks, pickups, pickupRadius }),
    201,
  ),
});

http.route({
  path: "/api/rooms/join",
  method: "POST",
  handler: endpoint((ctx, { playerId, code }) => ctx.runMutation(api.rooms.join, { playerId, code })),
});

http.route({
  path: "/api/rooms/leave",
  method: "POST",
  handler: endpoint((ctx, { playerId, roomId }) => ctx.runMutation(api.rooms.leave, { playerId, roomId })),
});

http.route({
  path: "/api/rooms/ready",
  method: "POST",
  handler: endpoint((ctx, { playerId, roomId, ready }) =>
    ctx.runMutation(api.rooms.setReady, { playerId, roomId, ready }),
  ),
});

http.route({
  path: "/api/rooms/poll",
  method: "GET",
  handler: endpoint((ctx, { roomId, after }) =>
    ctx.runQuery(internal.rooms.poll, {
      roomId,
      after: after === undefined || after === "" ? undefined : Number(after),
      now: Date.now(),
    }),
  ),
});

http.route({
  path: "/api/rooms/events",
  method: "POST",
  handler: endpoint(
    async (ctx, { playerId, roomId, type, params }) => ({
      event: await ctx.runMutation(api.events.fire, { playerId, roomId, type, params: params ?? {} }),
    }),
    201,
  ),
});

http.route({
  path: "/api/race/update",
  method: "POST",
  handler: endpoint((ctx, { playerId, roomId, position, heading, smash }) =>
    ctx.runMutation(api.race.update, { playerId, roomId, position, heading, smash }),
  ),
});

http.route({
  path: "/api/race/collect",
  method: "POST",
  handler: endpoint((ctx, { playerId, roomId, pickup, position }) =>
    ctx.runMutation(api.race.collect, { playerId, roomId, pickup, position }),
  ),
});

http.route({
  path: "/api/world/presence",
  method: "POST",
  handler: endpoint(async (ctx, { playerId, position, heading, speed, mode, vehicleType }) => {
    const now = Date.now();
    const me = await ctx.runMutation(internal.world.heartbeat, { playerId, position, heading, speed, mode, vehicleType, now });
    const players = await ctx.runQuery(internal.world.snapshot, { shard: me.shard, playerId, now });
    return { serverTime: now, shard: me.shard, worldRoomId: me.worldRoomId, you: COLORS[me.slot % COLORS.length], players };
  }),
});

http.route({
  path: "/api/world/leave",
  method: "POST",
  handler: endpoint((ctx, { playerId }) => ctx.runMutation(internal.world.leave, { playerId })),
});

const NO_VIEW = { mode: "onFoot" as const, entities: [] };

http.route({
  path: "/api/operator",
  method: "POST",
  handler: endpoint((ctx, { playerId, roomId, text, transcript, view, clientTimings }) =>
    ctx.runAction(api.operator.call, {
      playerId,
      roomId,
      transcript: transcript ?? text ?? "",
      view: view ?? NO_VIEW,
      clientTimings,
    }),
  ),
});

const MAX_PHOTO_BYTES = 8 * 1024 * 1024;

async function readPhoto(request: Request) {
  const mimeType = request.headers.get("Content-Type") ?? "";
  if (!mimeType.startsWith("image/")) {
    throw new ConvexError({ code: "INVALID_ARGUMENT", message: "Content-Type must be image/jpeg (or another image/*)" });
  }
  const photo = await request.arrayBuffer();
  if (photo.byteLength === 0 || photo.byteLength > MAX_PHOTO_BYTES) {
    throw new ConvexError({ code: "INVALID_ARGUMENT", message: `The photo must be 1 byte to ${MAX_PHOTO_BYTES} bytes` });
  }
  return { ...Object.fromEntries(new URL(request.url).searchParams), photo };
}

http.route({
  path: "/api/avatar/photo",
  method: "POST",
  handler: endpoint(async (ctx, { playerId, photo }) => {
    checkPlayerId(playerId ?? "");
    const result = await ctx.runAction(internal.avatarPhoto.analyze, { playerId, photo });
    if (result.kind === "invalid") throw new ConvexError({ code: "INVALID_ARGUMENT", message: result.message });
    if (result.kind === "unavailable") throw new ConvexError({ code: "DECISION_UNAVAILABLE", message: result.message });
    if (result.kind === "retake") {
      throw new ConvexError({ code: "RETAKE", message: result.message, realPerson: result.decision.realPerson });
    }
    return { avatar: result.avatar, decision: result.decision, answers: result.answers };
  }, 200, readPhoto),
});

http.route({
  path: "/api/avatar",
  method: "GET",
  handler: endpoint((ctx, { playerId }) => ctx.runQuery(api.avatars.get, { playerId: playerId ?? "" })),
});

export default http;
