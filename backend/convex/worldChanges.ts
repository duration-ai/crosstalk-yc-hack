import { v, type Infer } from "convex/values";
import { fail } from "./model";

export const RECOLOR_HEX: Record<string, string> = {
  yellow: "#FFCB1F",
  pink: "#FF3E8E",
  blue: "#2D8CFF",
  purple: "#A35BFF",
  orange: "#FF8A1E",
  green: "#2EC27E",
  red: "#E2603F",
  white: "#F7F8F9",
  black: "#1C2347",
  mint: "#8CE3BF",
  lilac: "#BBA6F7",
};

export const WORLD_CHANGE_TYPES = [
  "gravity",
  "ice",
  "fog",
  "time",
  "traffic",
  "recolor",
  "swap",
  "resize",
  "ramp",
  "rain",
  "snow",
] as const;

export type WorldChangeType = (typeof WORLD_CHANGE_TYPES)[number];

export const worldChangeParams = v.object({
  level: v.optional(v.string()),
  timeOfDay: v.optional(v.string()),
  color: v.optional(v.string()),
  hex: v.optional(v.string()),
  playerId: v.optional(v.string()),
  targetPlayerId: v.optional(v.string()),
  size: v.optional(v.string()),
  count: v.optional(v.number()),
});

export type WorldChangeParams = Infer<typeof worldChangeParams>;

export type WorldChange = { type: WorldChangeType; params: WorldChangeParams };

const LEVELS: Partial<Record<WorldChangeType, readonly string[]>> = {
  gravity: ["low", "high", "normal"],
  ice: ["on", "off"],
  rain: ["on", "off"],
  snow: ["on", "off"],
  fog: ["thick", "clear"],
  traffic: ["heavy", "light"],
};
const TIMES_OF_DAY = ["day", "golden", "dusk", "night"];
const SIZES = ["small", "big", "normal"];

function oneOf(field: string, value: unknown, allowed: readonly string[]) {
  if (typeof value !== "string" || !allowed.includes(value)) {
    fail("INVALID_ARGUMENT", `params.${field} must be one of ${allowed.join(", ")}`);
  }
  return value;
}

/** Validates a command and returns it with only the fields its type uses. swap.playerId is the firer. */
export function normalizeWorldChange(
  type: string,
  params: WorldChangeParams,
  firerId: string,
): WorldChange {
  switch (type) {
    case "gravity":
    case "ice":
    case "rain":
    case "snow":
    case "fog":
    case "traffic":
      return { type, params: { level: oneOf("level", params.level, LEVELS[type]!) } };
    case "time":
      return { type, params: { timeOfDay: oneOf("timeOfDay", params.timeOfDay, TIMES_OF_DAY) } };
    case "recolor": {
      const color = oneOf("color", params.color, Object.keys(RECOLOR_HEX));
      return { type, params: { color, hex: RECOLOR_HEX[color] } };
    }
    case "swap": {
      if (typeof params.targetPlayerId !== "string" || params.targetPlayerId === "") {
        fail("INVALID_ARGUMENT", "params.targetPlayerId is required for swap");
      }
      if (params.targetPlayerId === firerId) fail("INVALID_ARGUMENT", "Cannot swap with yourself");
      return { type, params: { playerId: firerId, targetPlayerId: params.targetPlayerId } };
    }
    case "resize":
      return { type, params: { size: oneOf("size", params.size, SIZES) } };
    case "ramp": {
      const count = params.count || 3;
      if (!Number.isInteger(count) || count < 1 || count > 5) {
        fail("INVALID_ARGUMENT", "params.count must be an integer from 1 to 5");
      }
      return { type, params: { count } };
    }
    default:
      fail("INVALID_ARGUMENT", `type must be one of ${WORLD_CHANGE_TYPES.join(", ")}`);
  }
}

export function newsName(displayName: string) {
  return (displayName.trim().split(/\s+/)[0] || "SOMEONE").toUpperCase().slice(0, 12);
}

/** The news bar text: uppercase, at most four words. */
export function newsText(change: WorldChange, firerName: string, targetName?: string) {
  const who = newsName(firerName);
  const p = change.params;
  switch (change.type) {
    case "gravity":
      return `${who} ${{ low: "LOWERED", high: "RAISED", normal: "RESET" }[p.level!]} GRAVITY`;
    case "ice":
      return p.level === "on" ? `${who} ICED THE ROADS` : `${who} MELTED THE ICE`;
    case "fog":
      return p.level === "thick" ? `${who} CALLED THE FOG` : `${who} CLEARED THE FOG`;
    case "time":
      return {
        day: `${who} BROUGHT BACK DAY`,
        golden: `${who} CALLED GOLDEN HOUR`,
        dusk: `${who} CALLED DUSK`,
        night: `${who} MADE IT NIGHT`,
      }[p.timeOfDay!]!;
    case "traffic":
      return p.level === "heavy" ? `${who} CALLED IN TRAFFIC` : `${who} CLEARED TRAFFIC`;
    case "recolor":
      return `${who} PAINTED CARS ${p.color!.toUpperCase()}`;
    case "swap":
      return `${who} SWAPPED WITH ${newsName(targetName ?? "")}`;
    case "resize":
      return { small: `${who} SHRANK ALL CARS`, big: `${who} SUPERSIZED ALL CARS`, normal: `${who} RESIZED CARS BACK` }[
        p.size!
      ]!;
    case "ramp":
      return `${who} DROPPED RAMPS`;
    case "rain":
    case "snow":
      return p.level === "on" ? `${who} MADE IT ${change.type.toUpperCase()}` : `${who} STOPPED THE ${change.type.toUpperCase()}`;
  }
}
