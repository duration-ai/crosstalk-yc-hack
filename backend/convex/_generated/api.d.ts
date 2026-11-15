/* eslint-disable */
/**
 * Generated `api` utility.
 *
 * THIS CODE IS AUTOMATICALLY GENERATED.
 *
 * To regenerate, run `npx convex dev`.
 * @module
 */

import type * as events from "../events.js";
import type * as http from "../http.js";
import type * as model from "../model.js";
import type * as operator from "../operator.js";
import type * as operatorProviders from "../operatorProviders.js";
import type * as playerState from "../playerState.js";
import type * as players from "../players.js";
import type * as race from "../race.js";
import type * as raceModel from "../raceModel.js";
import type * as rooms from "../rooms.js";
import type * as vehicles from "../vehicles.js";
import type * as worldChanges from "../worldChanges.js";

import type {
  ApiFromModules,
  FilterApi,
  FunctionReference,
} from "convex/server";

declare const fullApi: ApiFromModules<{
  events: typeof events;
  http: typeof http;
  model: typeof model;
  operator: typeof operator;
  operatorProviders: typeof operatorProviders;
  playerState: typeof playerState;
  players: typeof players;
  race: typeof race;
  raceModel: typeof raceModel;
  rooms: typeof rooms;
  vehicles: typeof vehicles;
  worldChanges: typeof worldChanges;
}>;

/**
 * A utility for referencing Convex functions in your app's public API.
 *
 * Usage:
 * ```js
 * const myFunctionReference = api.myModule.myFunction;
 * ```
 */
export declare const api: FilterApi<
  typeof fullApi,
  FunctionReference<any, "public">
>;

/**
 * A utility for referencing Convex functions in your app's internal API.
 *
 * Usage:
 * ```js
 * const myFunctionReference = internal.myModule.myFunction;
 * ```
 */
export declare const internal: FilterApi<
  typeof fullApi,
  FunctionReference<any, "internal">
>;

export declare const components: {};
