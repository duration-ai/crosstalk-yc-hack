import { describe, expect, it } from "vitest";
import { OBJECT_EFFECTS, PLAYABLE, PLAYER_EFFECTS, WORLD_EFFECTS } from "./operatorCatalogue";
import { decide, route, type ActiveSummary, type Answers, type ChoiceAnswer, type View, type ViewEntity } from "./operatorRules";

// These tests cover the routing rules for the whole catalogue; operatorPlayable.test.ts covers the
// built-effects gate with the real shared list.
for (const id of Object.keys({ ...PLAYER_EFFECTS, ...WORLD_EFFECTS, ...OBJECT_EFFECTS })) (PLAYABLE as Set<string>).add(id);

type Spec = {
  scope?: string | Record<string, number>;
  player_effect?: string;
  world_group?: string;
  sky_effect?: string;
  physics_effect?: string;
  population_effect?: string;
  object_effect?: string;
  class?: string | Record<string, number>;
  target?: string;
  described?: number;
  self_ref?: number;
  other_player?: number;
  strength?: string;
  color?: string;
  theme?: string;
  vehicle?: string;
  reject_reason?: string;
  confidence?: Record<string, number>;
};

function choice(value: string | Record<string, number>, confidence = 0.97): ChoiceAnswer {
  if (typeof value === "string") return { choice: value, confidence, probabilities: { [value]: confidence } };
  const [top] = Object.entries(value).sort(([, p], [, q]) => q - p);
  return { choice: top[0], confidence: top[1], probabilities: value };
}

function answers(spec: Spec): Answers {
  const c = (name: keyof Spec, fallback: string) =>
    choice((spec[name] as string | Record<string, number> | undefined) ?? fallback, spec.confidence?.[name]);
  return {
    scope: c("scope", "reject"),
    player_effect: c("player_effect", "none"),
    world_group: c("world_group", "none"),
    sky_effect: c("sky_effect", "none"),
    physics_effect: c("physics_effect", "none"),
    population_effect: c("population_effect", "none"),
    object_effect: c("object_effect", "none"),
    class: c("class", "none"),
    target: c("target", "none"),
    described: spec.described ?? 0.05,
    self_ref: spec.self_ref ?? 0.05,
    other_player: spec.other_player ?? 0.02,
    strength: c("strength", "medium"),
    color: c("color", "none"),
    theme: c("theme", "none"),
    vehicle: c("vehicle", "none"),
    reject_reason: c("reject_reason", "none"),
  };
}

const entity = (id: string, cls: string, rank: number, extra: Partial<ViewEntity> = {}): ViewEntity => ({
  id,
  class: cls,
  desc: id,
  color: "white",
  screen: "centre, large",
  distance_m: 10 * rank,
  view_rank: rank,
  protected: false,
  controlledBy: "",
  ...extra,
});

const onFoot: View = {
  mode: "onFoot",
  timeOfDay: "day",
  weather: "clear",
  entities: [
    entity("veh_12", "waymo", 1),
    entity("car:p2", "car", 2, { controlledBy: "player" }),
    entity("bld_yc", "building", 3, { protected: true }),
    entity("tree_3", "tree", 4),
    entity("veh_40", "waymo", 5, { color: "black" }),
    entity("ped_9", "pedestrian", 6),
    entity("bld_7", "building", 7),
  ],
};

const driving: View = {
  ...onFoot,
  mode: "driving",
  entities: [entity("veh_me", "car", 0, { controlledBy: "me" }), ...onFoot.entities],
};

const rain: ActiveSummary = { effect: "rain", target: "", targetClass: "", instant: false };
const treeFire: ActiveSummary = { effect: "set_fire", target: "tree_3", targetClass: "tree", instant: false };

const effect = (fields: object) => expect.objectContaining({ kind: "effect", ...fields });
const refused = (reason: string) => ({ kind: "refuse", reason });

describe("rule 1: routing", () => {
  it("takes the top scope when its branch picked an effect", () => {
    const a = answers({ scope: "world", world_group: "sky", sky_effect: "rain" });
    expect(route(a)).toBe("world");
    expect(decide(a, onFoot, [])).toEqual({
      kind: "effect",
      scope: "world",
      effect: "rain",
      target: "",
      targetClass: "",
      strength: "medium",
      color: "",
      theme: "",
      vehicle: "",
      confirm: false,
    });
  });

  it("rescues a weak reject with a near-certain effect from the next scope", () => {
    const a = answers({
      scope: { reject: 0.55, world: 0.4, players: 0.05 },
      world_group: "sky",
      sky_effect: "rain",
      confidence: { sky_effect: 0.96 },
      reject_reason: "vague",
    });
    expect(decide(a, onFoot, [])).toEqual(effect({ effect: "rain" }));
  });

  it("keeps a weak reject when no other branch is near-certain", () => {
    const a = answers({
      scope: { reject: 0.55, world: 0.4, players: 0.05 },
      world_group: "sky",
      sky_effect: "rain",
      confidence: { sky_effect: 0.9 },
      reject_reason: "vague",
    });
    expect(decide(a, onFoot, [])).toEqual(refused("vague"));
  });

  it("keeps a confident reject even when a branch has an effect", () => {
    const a = answers({ scope: { reject: 0.8, players: 0.2 }, player_effect: "flight", reject_reason: "teleport" });
    expect(decide(a, onFoot, [])).toEqual(refused("teleport"));
  });

  it("skips a top scope whose branch is none", () => {
    const a = answers({ scope: { players: 0.6, world: 0.3, reject: 0.1 }, world_group: "physics", physics_effect: "gravity_lower" });
    expect(decide(a, onFoot, [])).toEqual(effect({ scope: "world", effect: "gravity_lower" }));
  });

  it("falls through to reject when no branch picked anything", () => {
    const a = answers({ scope: { world: 0.6, players: 0.3, reject: 0.1 }, reject_reason: "not_a_request" });
    expect(decide(a, onFoot, [])).toEqual(refused("not_a_request"));
  });

  it("refuses a generic undo in character and never turns it into an effect", () => {
    expect(decide(answers({ scope: "reject", reject_reason: "undo" }), onFoot, [rain])).toEqual(refused("undo"));
    expect(decide(answers({ scope: "world", world_group: "undo" }), onFoot, [rain])).toEqual(refused("vague"));
  });
});

describe("rule 2: players with a named class", () => {
  it("becomes an object request when it is not about the player", () => {
    const a = answers({ scope: "players", player_effect: "vehicle_faster", class: "waymo", object_effect: "speed_up", self_ref: 0.2 });
    expect(decide(a, onFoot, [])).toEqual(effect({ scope: "object", effect: "speed_up", target: "veh_12" }));
  });

  it("stays players when the player talks about themself", () => {
    const a = answers({ scope: "players", player_effect: "vehicle_faster", class: "car", object_effect: "speed_up", self_ref: 0.9 });
    expect(decide(a, driving, [])).toEqual(effect({ scope: "players", effect: "vehicle_faster" }));
  });
});

describe("rule 3: decoys", () => {
  it("refuses gravity direction as unsupported", () => {
    const a = answers({ scope: "world", world_group: "physics", physics_effect: "x_gravity_direction" });
    expect(decide(a, onFoot, [])).toEqual(refused("unsupported"));
  });

  it("refuses turning things into other things", () => {
    const a = answers({ scope: "object", object_effect: "x_transform", class: "bus" });
    expect(decide(a, onFoot, [])).toEqual(refused("transformation"));
  });
});

describe("rule 4: everything", () => {
  it("refuses an object effect on everything", () => {
    const a = answers({ scope: "class", object_effect: "set_fire", class: { everything: 0.95, none: 0.05 }, confidence: { object_effect: 0.85 } });
    expect(decide(a, onFoot, [])).toEqual(refused("too_broad"));
  });

  it("lets a less certain everything fall to the class rules", () => {
    const a = answers({ scope: "class", object_effect: "set_fire", class: { everything: 0.6, none: 0.4 } });
    expect(decide(a, onFoot, [])).toEqual(refused("too_broad"));
    const b = answers({ scope: "object", object_effect: "set_fire", class: { everything: 0.6, tree: 0.4 } });
    expect(decide(b, onFoot, [])).toEqual(effect({ scope: "object", target: "veh_12" }));
  });
});

describe("rule 5: other players", () => {
  it("promotes a slow-down aimed at another player to everyone, with confirm", () => {
    const a = answers({ scope: "object", object_effect: "slow_down", other_player: 0.9 });
    expect(decide(a, driving, [])).toEqual(effect({ scope: "players", effect: "vehicle_slower", confirm: true }));
    expect(decide(a, onFoot, [])).toEqual(effect({ scope: "players", effect: "run_slower", confirm: true }));
  });

  it("maps grow, shrink, icy, rock_solid and speed_up", () => {
    const promoted = (object_effect: string) =>
      decide(answers({ scope: "world", world_group: "physics", physics_effect: "slow_motion", object_effect, other_player: 0.85 }), driving, []);
    expect(promoted("grow")).toEqual(effect({ effect: "bigger" }));
    expect(promoted("shrink")).toEqual(effect({ effect: "smaller" }));
    expect(promoted("icy")).toEqual(effect({ effect: "grip_down" }));
    expect(promoted("rock_solid")).toEqual(effect({ effect: "tougher" }));
    expect(promoted("speed_up")).toEqual(effect({ effect: "vehicle_faster" }));
  });

  it("refuses anything else aimed at a player", () => {
    const a = answers({ scope: "object", object_effect: "set_fire", other_player: 0.9 });
    expect(decide(a, onFoot, [])).toEqual(refused("player_target"));
  });
});

describe("rule 6: reject", () => {
  it("uses the reason, and none becomes unsupported", () => {
    expect(decide(answers({ scope: "reject", reject_reason: "cheat" }), onFoot, [])).toEqual(refused("cheat"));
    expect(decide(answers({ scope: "reject", reject_reason: "none" }), onFoot, [])).toEqual(refused("unsupported"));
  });
});

describe("rule 7: players", () => {
  it("confirms when another player may be meant and carries strength", () => {
    const a = answers({ scope: "players", player_effect: "nitro", other_player: 0.6, strength: "large" });
    expect(decide(a, driving, [])).toEqual(effect({ scope: "players", effect: "nitro", confirm: true, strength: "large" }));
    const b = answers({ scope: "players", player_effect: "nitro", other_player: 0.3 });
    expect(decide(b, driving, [])).toEqual(effect({ confirm: false, strength: "medium" }));
  });
});

describe("rule 8: world", () => {
  it("refuses a counter with nothing to counter", () => {
    const a = answers({ scope: "world", world_group: "sky", sky_effect: "clear_weather" });
    expect(decide(a, onFoot, [])).toEqual(refused("no_op"));
    expect(decide(a, onFoot, [rain])).toEqual(effect({ effect: "clear_weather", strength: "" }));
  });

  it("carries a pedestrian theme", () => {
    const a = answers({ scope: "world", world_group: "population", population_effect: "ped_theme", theme: "elvis" });
    expect(decide(a, onFoot, [])).toEqual(effect({ effect: "ped_theme", theme: "elvis" }));
    expect(decide(answers({ scope: "world", world_group: "population", population_effect: "ped_theme" }), onFoot, [])).toEqual(
      refused("vague"),
    );
  });

  it("puts the caller in the asked-for vehicle, even when the scope reads it as spawning", () => {
    const ride = { player_effect: "change_vehicle", vehicle: "sports_car", self_ref: 0.9 };
    expect(decide(answers({ scope: "players", ...ride }), driving, [])).toEqual(
      effect({ scope: "players", effect: "change_vehicle", vehicle: "sports_car", strength: "" }),
    );
    expect(decide(answers({ scope: "reject", reject_reason: "spawning", ...ride }), driving, [])).toEqual(
      effect({ effect: "change_vehicle", vehicle: "sports_car" }),
    );
    expect(decide(answers({ scope: "reject", reject_reason: "deferred", ...ride }), driving, [])).toEqual(refused("deferred"));
    expect(decide(answers({ scope: "players", player_effect: "change_vehicle" }), driving, [])).toEqual(refused("vague"));
  });
});

describe("rule 9: class", () => {
  it("applies to every thing of a named class", () => {
    const a = answers({ scope: "class", object_effect: "set_fire", class: "tree" });
    expect(decide(a, onFoot, [])).toEqual(effect({ scope: "class", effect: "set_fire", targetClass: "tree", target: "" }));
  });

  it("refuses no class, everything, and incompatible effects", () => {
    expect(decide(answers({ scope: "class", object_effect: "grow" }), onFoot, [])).toEqual(refused("too_broad"));
    expect(decide(answers({ scope: "class", object_effect: "grow", class: "everything" }), onFoot, [])).toEqual(refused("too_broad"));
    expect(decide(answers({ scope: "class", object_effect: "set_fire", class: "pedestrian" }), onFoot, [])).toEqual(refused("incompatible"));
  });

  it("extinguishes only what burns", () => {
    const a = answers({ scope: "class", object_effect: "extinguish", class: "tree" });
    expect(decide(a, onFoot, [])).toEqual(refused("no_op"));
    expect(decide(a, onFoot, [treeFire])).toEqual(effect({ effect: "extinguish", targetClass: "tree" }));
  });
});

describe("rule 10: object targets", () => {
  it("picks the first visible thing of the class", () => {
    const a = answers({ scope: "object", object_effect: "recolor", class: "waymo", color: "pink" });
    expect(decide(a, onFoot, [])).toEqual(effect({ scope: "object", effect: "recolor", target: "veh_12", targetClass: "waymo", color: "pink" }));
  });

  it("uses the described one of that class", () => {
    const a = answers({ scope: "object", object_effect: "recolor", class: "waymo", color: "gold", described: 0.9, target: "veh_40" });
    expect(decide(a, onFoot, [])).toEqual(effect({ target: "veh_40" }));
    const wrongClass = answers({ scope: "object", object_effect: "recolor", class: "waymo", color: "gold", described: 0.9, target: "tree_3" });
    expect(decide(wrongClass, onFoot, [])).toEqual(effect({ target: "veh_12" }));
  });

  it("refuses a described thing that isn't in view instead of picking the nearest", () => {
    const offScreen = answers({ scope: "object", object_effect: "demolish", class: "building", described: 0.9, target: "none" });
    expect(decide(offScreen, onFoot, [])).toEqual(refused("not_visible"));
    const noClass = answers({ scope: "object", object_effect: "set_fire", described: 0.8, target: "none" });
    expect(decide(noClass, driving, [])).toEqual(refused("not_visible"));
  });

  it("refuses a class nobody can see", () => {
    const a = answers({ scope: "object", object_effect: "stop", class: "bus" });
    expect(decide(a, onFoot, [])).toEqual(refused("not_visible"));
  });

  it("without a class uses the described thing, then your car, then the top-ranked thing", () => {
    const described = answers({ scope: "object", object_effect: "set_fire", described: 0.8, target: "tree_3" });
    expect(decide(described, driving, [])).toEqual(effect({ target: "tree_3" }));
    const vague = answers({ scope: "object", object_effect: "launch", target: "tree_3" });
    expect(decide(vague, onFoot, [])).toEqual(effect({ effect: "launch", target: "veh_12" }));
    const grow = answers({ scope: "object", object_effect: "grow" });
    expect(decide(grow, driving, [])).toEqual(effect({ scope: "players", effect: "bigger", confirm: false }));
  });

  it("finds your own car when the view left it out", () => {
    const noOwnCar: View = { ...onFoot, mode: "driving" };
    const a = answers({ scope: "object", object_effect: "recolor", class: "car", color: "gold", described: 0.9, target: "veh_me", self_ref: 0.9 });
    expect(decide(a, noOwnCar, [])).toEqual(effect({ target: "veh_me", color: "gold" }));
    const grow = answers({ scope: "object", object_effect: "grow" });
    expect(decide(grow, noOwnCar, [])).toEqual(effect({ scope: "players", effect: "bigger" }));
  });

  it("recolors your own car locally", () => {
    const a = answers({ scope: "object", object_effect: "recolor", color: "rainbow" });
    expect(decide(a, driving, [])).toEqual(effect({ scope: "object", target: "veh_me", color: "rainbow", strength: "" }));
  });

  it("promotes another player's car with confirm", () => {
    const a = answers({ scope: "object", object_effect: "shrink", described: 0.9, target: "car:p2" });
    expect(decide(a, onFoot, [])).toEqual(effect({ scope: "players", effect: "smaller", confirm: true }));
  });

  it("refuses protected and incompatible targets", () => {
    const yc = answers({ scope: "object", object_effect: "demolish", class: "building", described: 0.9, target: "bld_yc" });
    expect(decide(yc, onFoot, [])).toEqual(refused("protected"));
    const launchTree = answers({ scope: "object", object_effect: "launch", class: "tree" });
    expect(decide(launchTree, onFoot, [])).toEqual(refused("incompatible"));
  });

  it("refuses extinguishing something that isn't burning", () => {
    const a = answers({ scope: "object", object_effect: "extinguish", class: "tree" });
    expect(decide(a, onFoot, [])).toEqual(refused("no_op"));
    expect(decide(a, onFoot, [treeFire])).toEqual(effect({ effect: "extinguish", target: "tree_3" }));
    const classFire = { ...treeFire, target: "", targetClass: "plant" };
    expect(decide(a, onFoot, [classFire])).toEqual(effect({ effect: "extinguish", target: "tree_3" }));
  });
});

describe("rule 11: parameters", () => {
  it("refuses a recolor without a colour and gives one-shots no strength", () => {
    expect(decide(answers({ scope: "object", object_effect: "recolor", class: "tree" }), onFoot, [])).toEqual(refused("vague"));
    expect(decide(answers({ scope: "object", object_effect: "launch", class: "pedestrian", strength: "large" }), onFoot, [])).toEqual(
      effect({ target: "ped_9", strength: "" }),
    );
    expect(decide(answers({ scope: "object", object_effect: "grow", class: "tree", strength: "small" }), onFoot, [])).toEqual(
      effect({ strength: "small" }),
    );
  });
});
