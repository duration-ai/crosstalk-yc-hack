import {
  COUNTERS,
  OBJECT_EFFECTS,
  PLAYABLE,
  PLAYER_EFFECTS,
  VEHICLES,
  WORLD_EFFECTS,
  isA,
  type Scope,
  type Strength,
} from "./operatorCatalogue";

export type ChoiceAnswer = { choice: string; confidence: number; probabilities: Record<string, number> };

export type Answers = {
  scope: ChoiceAnswer;
  player_effect: ChoiceAnswer;
  world_group: ChoiceAnswer;
  sky_effect: ChoiceAnswer;
  physics_effect: ChoiceAnswer;
  population_effect: ChoiceAnswer;
  object_effect: ChoiceAnswer;
  class: ChoiceAnswer;
  target: ChoiceAnswer;
  described: number;
  self_ref: number;
  other_player: number;
  strength: ChoiceAnswer;
  color: ChoiceAnswer;
  theme: ChoiceAnswer;
  vehicle: ChoiceAnswer;
  reject_reason: ChoiceAnswer;
  nearest: ChoiceAnswer;
  nearest_vehicle: ChoiceAnswer;
  stretch: number;
};

export type ViewEntity = {
  id: string;
  class: string;
  desc: string;
  color: string;
  screen: string;
  distance_m: number;
  view_rank: number;
  protected: boolean;
  controlledBy: string;
};

export type View = { mode: "onFoot" | "driving"; timeOfDay: string; weather: string; entities: ViewEntity[] };

export type ActiveSummary = { effect: string; target: string; targetClass: string; instant: boolean };

export type RefusalReason =
  | "unsupported"
  | "transformation"
  | "spawning"
  | "teleport"
  | "deferred"
  | "cheat"
  | "vague"
  | "not_a_request"
  | "too_broad"
  | "player_target"
  | "no_op"
  | "incompatible"
  | "not_visible"
  | "protected"
  | "unavailable"
  | "undo"
  | "not_built"
  | "no_asks"
  | "race_over";

export type EffectCommand = {
  kind: "effect";
  scope: Scope;
  effect: string;
  target: string;
  targetClass: string;
  strength: Strength | "";
  color: string;
  theme: string;
  vehicle: string;
  confirm: boolean;
  substitute?: boolean;
};

export type Command = EffectCommand | { kind: "refuse"; reason: RefusalReason };

/** Toast and Operator bar text; the style guide caps Operator HUD text at four words. */
export const REFUSAL_MESSAGES: Record<RefusalReason, string> = {
  unsupported: "Can't do that one.",
  transformation: "Can't transform things.",
  spawning: "Can't add new things.",
  teleport: "No teleporting, sorry.",
  deferred: "Only right now, sorry.",
  cheat: "Nice try, cheater.",
  vague: "Didn't catch that.",
  not_a_request: "Was that a request?",
  too_broad: "Too much at once.",
  player_target: "Not on players, sorry.",
  no_op: "Nothing to change.",
  incompatible: "That won't work there.",
  not_visible: "Can't see one.",
  protected: "That landmark's protected.",
  unavailable: "Operator busy. Retry.",
  undo: "No take-backs, sugar.",
  not_built: "Can't do that one yet.",
  no_asks: "Grab a phone first!",
  race_over: "The race is over.",
};

const NONE = "none";
const EVERYTHING = "everything";

const refuse = (reason: RefusalReason): Command => ({ kind: "refuse", reason });

function worldEffect(a: Answers): ChoiceAnswer {
  switch (a.world_group.choice) {
    case "sky":
      return a.sky_effect;
    case "physics":
      return a.physics_effect;
    case "population":
      return a.population_effect;
    default:
      return { choice: NONE, confidence: 0, probabilities: {} };
  }
}

/** The effect answer a scope routes to; reject has no branch and always counts as chosen. */
function branchEffect(a: Answers, scope: string): ChoiceAnswer | null {
  switch (scope) {
    case "players":
      return a.player_effect;
    case "world":
      return worldEffect(a);
    case "object":
    case "class":
      return a.object_effect;
    default:
      return null;
  }
}

/** Rule 1. Returns the routed scope, "reject", or null when nothing fits. */
export function route(a: Answers): Scope | "reject" | null {
  const ranked = Object.entries(a.scope.probabilities)
    .sort(([, p], [, q]) => q - p)
    .map(([scope]) => scope);
  const chosen = (scope: string, minConfidence = 0) => {
    const effect = branchEffect(a, scope);
    return effect === null || (effect.choice !== NONE && effect.confidence >= minConfidence);
  };
  if (ranked[0] === "reject" && (a.scope.probabilities.reject ?? 0) < 0.7) {
    const rescue = ranked.slice(1).find((scope) => scope !== "reject" && chosen(scope, 0.95));
    if (rescue) return rescue as Scope;
  }
  return (ranked.find((scope) => chosen(scope)) as Scope | "reject" | undefined) ?? null;
}

function strengthOf(a: Answers): Strength {
  const s = a.strength.choice;
  return s === "small" || s === "large" ? s : "medium";
}

function effectCommand(
  a: Answers,
  scope: Scope,
  effect: string,
  fields: Partial<Pick<EffectCommand, "target" | "targetClass" | "confirm">> = {},
): Command {
  const info = PLAYER_EFFECTS[effect] ?? WORLD_EFFECTS[effect] ?? OBJECT_EFFECTS[effect];
  if (!info) return refuse("vague");
  if (!PLAYABLE.has(effect)) return refuse("not_built");
  let color = "";
  let theme = "";
  if (effect === "recolor") {
    if (a.color.choice === NONE) return refuse("vague");
    color = a.color.choice;
  }
  if (effect === "ped_theme") {
    if (a.theme.choice === NONE) return refuse("vague");
    theme = a.theme.choice;
  }
  let vehicle = "";
  if (effect === "change_vehicle") {
    if (a.vehicle.choice === NONE) return refuse("vague");
    vehicle = a.vehicle.choice;
  }
  return {
    kind: "effect",
    scope,
    effect,
    target: fields.target ?? "",
    targetClass: fields.targetClass ?? "",
    strength: info.duration ? strengthOf(a) : "",
    color,
    theme,
    vehicle,
    confirm: fields.confirm ?? false,
  };
}

const PROMOTED: Record<string, (onFoot: boolean) => string> = {
  slow_down: (onFoot) => (onFoot ? "run_slower" : "vehicle_slower"),
  speed_up: (onFoot) => (onFoot ? "run_faster" : "vehicle_faster"),
  grow: () => "bigger",
  shrink: () => "smaller",
  icy: () => "grip_down",
  rock_solid: () => "tougher",
};

const promotes = (objectEffect: string) => objectEffect in PROMOTED;

/** Rule 5: an object effect aimed at the other racers becomes the matching players effect. */
function promote(a: Answers, view: View, objectEffect: string, confirm: boolean): Command {
  const effect = PROMOTED[objectEffect]?.(view.mode === "onFoot");
  if (!effect) return refuse("player_target");
  return effectCommand(a, "players", effect, { confirm });
}

/** Object effects that work on a racer's own car rather than becoming a players effect. */
export const CAR_EFFECTS = new Set(["ghost", "see_through", "bouncy", "icy", "rock_solid", "soft", "grow", "shrink", "recolor", "launch"]);

function onCar(a: Answers, effect: string, car: ViewEntity, confirm: boolean): Command {
  return effectCommand(a, "object", effect, { target: car.id, targetClass: car.class, confirm });
}

function allows(effect: string, cls: string) {
  return OBJECT_EFFECTS[effect].classes.some((allowed) => isA(cls, allowed));
}

const namedClass = (a: Answers) => a.class.choice !== NONE && a.class.choice !== EVERYTHING;

/** A ride change for the caller wins over a spawning or transformation reading of the same words. */
function asksForRide(a: Answers) {
  return (
    a.player_effect.choice === "change_vehicle" &&
    a.player_effect.confidence >= 0.8 &&
    a.vehicle.choice !== NONE &&
    a.other_player < 0.5 &&
    !["deferred", "undo", "cheat", "not_a_request"].includes(a.reject_reason.choice)
  );
}

export function decide(a: Answers, view: View, active: ActiveSummary[]): Command {
  if (asksForRide(a)) return effectCommand(a, "players", "change_vehicle");
  let scope = route(a);
  if (!scope) return refuse("vague");

  if (scope === "players" && namedClass(a) && a.object_effect.choice !== NONE && a.self_ref < 0.5) {
    scope = "object";
  }

  const objectLike = scope === "object" || scope === "class";
  if (scope === "world" && a.world_group.choice === "physics" && a.physics_effect.choice === "x_gravity_direction") {
    return refuse("unsupported");
  }
  if (objectLike && a.object_effect.choice === "x_transform") return refuse("transformation");

  if (
    objectLike &&
    (a.class.probabilities[EVERYTHING] ?? 0) >= 0.9 &&
    a.object_effect.choice !== NONE &&
    a.object_effect.confidence >= 0.8
  ) {
    return refuse("too_broad");
  }

  if (scope !== "players" && scope !== "reject" && a.other_player >= 0.8) {
    const effect = a.object_effect.choice;
    const racer = [...view.entities].sort((x, y) => x.view_rank - y.view_rank).find((e) => e.controlledBy === "player");
    if (CAR_EFFECTS.has(effect) && racer && !promotes(effect)) return onCar(a, effect, racer, true);
    return promote(a, view, effect, true);
  }

  switch (scope) {
    case "reject": {
      const reason = a.reject_reason.choice;
      return refuse(reason === NONE || !(reason in REFUSAL_MESSAGES) ? "unsupported" : (reason as RefusalReason));
    }
    case "players": {
      const effect = a.player_effect.choice;
      if (effect === NONE || !PLAYER_EFFECTS[effect]) return refuse("vague");
      return effectCommand(a, "players", effect, { confirm: a.other_player >= 0.5 });
    }
    case "world": {
      const effect = worldEffect(a).choice;
      if (effect === NONE || !WORLD_EFFECTS[effect]) return refuse("vague");
      const countered = COUNTERS[effect];
      if (countered && !active.some((e) => countered.includes(e.effect))) return refuse("no_op");
      return effectCommand(a, "world", effect);
    }
    case "class": {
      const cls = a.class.choice;
      if (!namedClass(a)) return refuse("too_broad");
      const effect = a.object_effect.choice;
      if (!OBJECT_EFFECTS[effect]) return refuse("vague");
      if (!allows(effect, cls)) return refuse("incompatible");
      if (effect === "extinguish" && !active.some((e) => e.effect === "set_fire" && (isA(e.targetClass, cls) || isA(cls, e.targetClass)))) {
        return refuse("no_op");
      }
      return effectCommand(a, "class", effect, { targetClass: cls });
    }
    case "object":
      return decideObject(a, view, active);
  }
}

/** Refusals that only mean the game lacks the exact thing: the Operator hands over the closest built effect instead. */
const SUBSTITUTED: ReadonlySet<RefusalReason> = new Set(["unsupported", "transformation", "spawning", "teleport", "vague", "not_built", "incompatible"]);

/** Built effects the Operator can hand over as a stand-in: no target, colour or theme needed, and never a counter. */
export const STAND_INS = [...Object.keys(PLAYER_EFFECTS), ...Object.keys(WORLD_EFFECTS)].filter(
  (effect) => PLAYABLE.has(effect) && !(effect in COUNTERS) && effect !== "ped_theme",
);

/**
 * Never a dead end for an ask the game can't do exactly: a capability refusal becomes Jev's closest
 * built effect, and a granted ask that only stretches toward the words is marked as a stand-in too,
 * so the Operator's line owns up to the swap. Safety and scope refusals pass through.
 */
export function nextBest(a: Answers, command: Command): Command {
  if (command.kind === "effect") return a.stretch >= 0.7 ? { ...command, substitute: true } : command;
  if (!SUBSTITUTED.has(command.reason)) return command;
  const effect = a.nearest.choice;
  if (!STAND_INS.includes(effect)) return command;
  const vehicle = effect === "change_vehicle" ? a.nearest_vehicle.choice : "";
  if (effect === "change_vehicle" && !(VEHICLES as readonly string[]).includes(vehicle)) return command;
  const info = PLAYER_EFFECTS[effect] ?? WORLD_EFFECTS[effect];
  return {
    kind: "effect",
    scope: PLAYER_EFFECTS[effect] ? "players" : "world",
    effect,
    target: "",
    targetClass: "",
    strength: info.duration ? strengthOf(a) : "",
    color: "",
    theme: "",
    vehicle,
    confirm: false,
    substitute: true,
  };
}

const STAND_IN_LINES = [
  (thing: string) => `No can do, sugar. How about ${thing}?`,
  (thing: string) => `Fresh out of those, sugar. Here's ${thing}!`,
  (thing: string) => `Can't swing that, hon. Closest I've got: ${thing}!`,
  (thing: string) => `That's not on my switchboard, sugar. Here's ${thing}!`,
];

/** The Operator owning up to a swap, playfully, in under a dozen words; the transcript picks the phrasing. */
export function standInLine(transcript: string, label: string, vehicle: string) {
  const thing = vehicle ? `a ${vehicle.replace(/_/g, " ")}` : label;
  let hash = 0;
  for (const ch of transcript) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
  return STAND_IN_LINES[hash % STAND_IN_LINES.length](thing);
}

const OWN_CAR: ViewEntity = {
  id: "veh_me",
  class: "car",
  desc: "the player's own car",
  color: "",
  screen: "",
  distance_m: 0,
  view_rank: 0,
  protected: false,
  controlledBy: "me",
};

/** Rule 10. */
function decideObject(a: Answers, view: View, active: ActiveSummary[]): Command {
  const effect = a.object_effect.choice;
  if (!OBJECT_EFFECTS[effect]) return refuse("vague");
  const visible = [...view.entities].sort((x, y) => x.view_rank - y.view_rank);
  if (view.mode === "driving" && !visible.some((e) => e.controlledBy === "me")) visible.unshift(OWN_CAR);
  if (a.described >= 0.5 && a.target.choice === NONE) return refuse("not_visible");
  const described = a.described >= 0.5 ? visible.find((e) => e.id === a.target.choice) : undefined;

  let entity: ViewEntity | undefined;
  if (namedClass(a)) {
    const ofClass = visible.filter((e) => isA(e.class, a.class.choice));
    entity = described && ofClass.includes(described) ? described : ofClass[0];
  } else {
    entity =
      described ??
      (view.mode === "driving" ? visible.find((e) => e.controlledBy === "me") : undefined) ??
      visible[0];
  }
  if (!entity) return refuse("not_visible");
  if (entity.protected) return refuse("protected");
  if (entity.controlledBy && CAR_EFFECTS.has(effect)) return onCar(a, effect, entity, entity.controlledBy === "player");
  if (entity.controlledBy === "player") return promote(a, view, effect, true);
  if (entity.controlledBy === "me") return promote(a, view, effect, false);
  if (!allows(effect, entity.class)) return refuse("incompatible");
  if (
    effect === "extinguish" &&
    !active.some((e) => e.effect === "set_fire" && (e.target === entity.id || (e.target === "" && isA(entity.class, e.targetClass))))
  ) {
    return refuse("no_op");
  }
  return effectCommand(a, "object", effect, { target: entity.id, targetClass: entity.class });
}
