import { defineSchema, defineTable } from "convex/server";
import { v } from "convex/values";
import { movementMode, vec3 } from "./model";

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
});
