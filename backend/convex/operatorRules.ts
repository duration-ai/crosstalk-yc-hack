import {
  COUNTERS,
  OBJECT_EFFECTS,
  PLAYABLE,
  PLAYER_EFFECTS,
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
  reject_reason: ChoiceAnswer;
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
  | "not_built";

export type EffectCommand = {
  kind: "effect";
  scope: Scope;
  effect: string;
  target: string;
  targetClass: string;
  strength: Strength | "";
  color: string;
  theme: string;
  confirm: boolean;
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
  return {
    kind: "effect",
    scope,
    effect,
    target: fields.target ?? "",
    targetClass: fields.targetClass ?? "",
    strength: info.duration ? strengthOf(a) : "",
    color,
    theme,
    confirm: fields.confirm ?? false,
  };
}

/** Rule 5: an object effect aimed at a player becomes the matching players effect. */
function promote(a: Answers, view: View, objectEffect: string, confirm: boolean): Command {
  const onFoot = view.mode === "onFoot";
  const effect = {
    slow_down: onFoot ? "run_slower" : "vehicle_slower",
    speed_up: onFoot ? "run_faster" : "vehicle_faster",
    grow: "bigger",
    shrink: "smaller",
    icy: "grip_down",
    rock_solid: "tougher",
  }[objectEffect];
  if (!effect) return refuse("player_target");
  return effectCommand(a, "players", effect, { confirm });
}

function allows(effect: string, cls: string) {
  return OBJECT_EFFECTS[effect].classes.some((allowed) => isA(cls, allowed));
}

const namedClass = (a: Answers) => a.class.choice !== NONE && a.class.choice !== EVERYTHING;

export function decide(a: Answers, view: View, active: ActiveSummary[]): Command {
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
    return promote(a, view, a.object_effect.choice, true);
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
  if (entity.controlledBy === "player") return promote(a, view, effect, true);
  if (entity.controlledBy === "me" && effect !== "recolor") return promote(a, view, effect, false);
  if (!allows(effect, entity.class)) return refuse("incompatible");
  if (
    effect === "extinguish" &&
    !active.some((e) => e.effect === "set_fire" && (e.target === entity.id || (e.target === "" && isA(entity.class, e.targetClass))))
  ) {
    return refuse("no_op");
  }
  return effectCommand(a, "object", effect, { target: entity.id, targetClass: entity.class });
}
