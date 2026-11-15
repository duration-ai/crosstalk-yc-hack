import { httpRouter } from "convex/server";
import { ConvexError } from "convex/values";
import { api } from "./_generated/api";
import { httpAction, type ActionCtx } from "./_generated/server";

type Handler = (ctx: ActionCtx, input: Record<string, any>) => Promise<unknown>;

const STATUS: Record<string, number> = { INVALID_ARGUMENT: 400, NOT_FOUND: 404, CONFLICT: 409 };

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

function endpoint(handler: Handler, successStatus = 200) {
  return httpAction(async (ctx, request) => {
    try {
      return json(successStatus, await handler(ctx, await readInput(request)));
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

export default http;
