import { defineSchema, defineTable } from "convex/server";
import { v } from "convex/values";
import { movementMode, vec3 } from "./model";
import { roomStatus } from "./raceModel";
import { worldChangeParams } from "./worldChanges";

export default defineSchema({
  players: defineTable({
    displayName: v.string(),
    createdAt: v.number(),
  }),
  playerStates: defineTable({
    playerId: v.id("players"),
    position: vec3,
    heading: v.number(),
    mode: movementMode,
    vehicleId: v.optional(v.id("vehicles")),
    updatedAt: v.number(),
  }).index("by_player", ["playerId"]),
  vehicles: defineTable({
    model: v.string(),
    position: vec3,
    heading: v.number(),
    occupiedBy: v.optional(v.id("players")),
    updatedAt: v.number(),
  }).index("by_occupant", ["occupiedBy"]),
  rooms: defineTable({
    code: v.string(),
    status: roomStatus,
    raceNumber: v.number(),
    minPlayers: v.number(),
    startsAt: v.number(),
    closesAt: v.number(),
    route: v.array(vec3),
    routeLength: v.number(),
    finishRadius: v.number(),
    eventSeq: v.number(),
    createdAt: v.number(),
  }).index("by_code", ["code"]),
  roomPlayers: defineTable({
    roomId: v.id("rooms"),
    playerId: v.id("players"),
    displayName: v.string(),
    slot: v.number(),
    ready: v.boolean(),
    position: vec3,
    heading: v.number(),
    progress: v.number(),
    finishedAt: v.number(),
    smash: v.optional(v.number()),
    left: v.boolean(),
    updatedAt: v.number(),
  })
    .index("by_room", ["roomId"])
    .index("by_player", ["playerId", "roomId"]),
  roomEvents: defineTable({
    roomId: v.id("rooms"),
    seq: v.number(),
    raceNumber: v.number(),
    type: v.string(),
    params: worldChangeParams,
    playerId: v.id("players"),
    displayName: v.string(),
    slot: v.number(),
    serverTime: v.number(),
    news: v.string(),
  })
    .index("by_room_seq", ["roomId", "seq"])
    .index("by_room_race", ["roomId", "raceNumber"]),
  activeEffects: defineTable({
    roomId: v.id("rooms"),
    effect: v.string(),
    scope: v.union(v.literal("players"), v.literal("world"), v.literal("object"), v.literal("class")),
    target: v.string(),
    targetClass: v.string(),
    strength: v.string(),
    color: v.string(),
    theme: v.string(),
    instant: v.boolean(),
    status: v.union(v.literal("pending"), v.literal("active"), v.literal("expired")),
    startsAt: v.number(),
    endsAt: v.number(),
    durationS: v.number(),
    confirmBy: v.number(),
    requestedBy: v.id("players"),
    transcript: v.string(),
    expiryJob: v.optional(v.id("_scheduled_functions")),
    endedBy: v.optional(v.string()),
  }).index("by_room_status", ["roomId", "status"]),
  operatorCalls: defineTable({
    roomId: v.id("rooms"),
    playerId: v.id("players"),
    transcript: v.string(),
    view: v.any(),
    input: v.string(),
    answers: v.any(),
    command: v.any(),
    outcome: v.string(),
    reason: v.optional(v.string()),
    effectId: v.optional(v.id("activeEffects")),
    error: v.optional(v.string()),
    timings: v.object({
      jevMs: v.number(),
      totalMs: v.number(),
      attempts: v.number(),
      sttMs: v.optional(v.number()),
    }),
    createdAt: v.number(),
  }).index("by_room", ["roomId"]),
  scribeTokenMints: defineTable({
    playerId: v.id("players"),
    at: v.number(),
  }).index("by_player_at", ["playerId", "at"]),
});
