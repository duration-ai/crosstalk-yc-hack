import { describe, expect, it } from "vitest";
import builtEffects from "../../unity/Assets/Resources/Operator/built-effects.json";
import { COUNTERS, OBJECT_EFFECTS, PLAYABLE, PLAYER_EFFECTS, WORLD_EFFECTS } from "./operatorCatalogue";
import { decide, type Answers, type ChoiceAnswer, type View } from "./operatorRules";

const CATALOGUE = { ...PLAYER_EFFECTS, ...WORLD_EFFECTS, ...OBJECT_EFFECTS };
const pick = (choice: string): ChoiceAnswer => ({ choice, confidence: 0.97, probabilities: { [choice]: 0.97 } });
const view: View = { mode: "onFoot", timeOfDay: "day", weather: "clear", entities: [] };

function worldAnswers(group: string, field: "sky_effect" | "physics_effect" | "population_effect", effect: string): Answers {
  const none = pick("none");
  return {
    scope: pick("world"), player_effect: none, world_group: pick(group), sky_effect: none, physics_effect: none,
    population_effect: none, object_effect: none, class: none, target: none, described: 0, self_ref: 0,
    other_player: 0, strength: pick("medium"), color: none, theme: none, reject_reason: none, [field]: pick(effect),
  };
}

describe("built-effects gate", () => {
  it("lists only catalogue ids in the shared built list", () => {
    for (const id of builtEffects.built) expect(CATALOGUE[id], id).toBeDefined();
  });

  it("plays built effects and the server-side counters", () => {
    for (const id of [...builtEffects.built, ...Object.keys(COUNTERS), "extinguish"]) expect(PLAYABLE.has(id), id).toBe(true);
    expect(decide(worldAnswers("sky", "sky_effect", "rain"), view, [])).toMatchObject({ kind: "effect", effect: "rain" });
  });

  it("refuses an effect the client has not built yet, in character", () => {
    expect(PLAYABLE.has("undo_last")).toBe(false);
    const unbuilt = ["floor_is_lava", "earthquake", "blackout"].filter((id) => !PLAYABLE.has(id));
    for (const id of unbuilt) {
      const group = id === "blackout" ? "sky" : "physics";
      expect(decide(worldAnswers(group, group === "sky" ? "sky_effect" : "physics_effect", id), view, []), id).toEqual({ kind: "refuse", reason: "not_built" });
    }
  });
});

describe("effect durations", () => {
  it("keeps every lasting effect up for at least 15 s", () => {
    const lasting = Object.entries(CATALOGUE).filter(([, e]) => e.duration);
    expect(lasting.length).toBeGreaterThan(40);
    for (const [id, e] of lasting) {
      expect(e.duration![0], id).toBeGreaterThanOrEqual(15);
      expect(e.duration![1], id).toBeGreaterThanOrEqual(e.duration![0]);
    }
  });
});
