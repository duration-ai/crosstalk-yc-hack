import { httpRouter } from "convex/server";
import { ConvexError } from "convex/values";
import { api, internal } from "./_generated/api";
import { httpAction, type ActionCtx } from "./_generated/server";

type Handler = (ctx: ActionCtx, input: Record<string, any>) => Promise<unknown>;

const STATUS: Record<string, number> = {
  INVALID_ARGUMENT: 400,
  NOT_FOUND: 404,
  CONFLICT: 409,
  NOT_IMPLEMENTED: 501,
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

async function readAudio(request: Request) {
  const mimeType = request.headers.get("Content-Type") ?? "";
  if (!mimeType.startsWith("audio/")) {
    throw new ConvexError({ code: "INVALID_ARGUMENT", message: "Content-Type must be audio/*" });
  }
  return {
    ...Object.fromEntries(new URL(request.url).searchParams),
    audio: await request.arrayBuffer(),
    mimeType,
  };
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
    (ctx, { playerId, route, finishRadius, minPlayers }) =>
      ctx.runMutation(api.rooms.create, { playerId, route, finishRadius, minPlayers }),
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
  path: "/api/operator",
  method: "POST",
  handler: endpoint((ctx, { playerId, roomId, text }) =>
    ctx.runAction(api.operator.call, { playerId, roomId, text: text ?? "" }),
  ),
});

http.route({
  path: "/api/operator/audio",
  method: "POST",
  handler: endpoint(
    (ctx, { playerId, roomId, audio, mimeType }) =>
      ctx.runAction(api.operator.call, { playerId, roomId, audio, mimeType }),
    200,
    readAudio,
  ),
});

export default http;
