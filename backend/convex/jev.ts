import {
  CLASSES,
  COLORS,
  isA,
  OBJECT_EFFECTS,
  PHYSICS_EFFECTS,
  PLAYER_EFFECTS,
  POPULATION_EFFECTS,
  SKY_EFFECTS,
  THEMES,
} from "./operatorCatalogue";
import type { Answers, ChoiceAnswer, View } from "./operatorRules";

export const JEV_URL = "https://ai-gateway.vercel.sh/v1/decisions";
export const JEV_MODEL = "typesafe-ai/jev";
export const JEV_TIMEOUT_MS = 2_000;

type Choice = { value: string; description: string };
type Question =
  | { type: "choice"; name: string; instructions: string; choices: Choice[] }
  | { type: "predicate"; name: string; instructions: string };

export type ActiveForPrompt = { effect: string; target: string; targetClass: string; secondsLeft: number };

const NONE: Choice = { value: "none", description: "none of these" };

const options = (effects: Record<string, { description: string }>, extra: Choice[] = []): Choice[] => [
  NONE,
  ...Object.entries(effects).map(([value, { description }]) => ({ value, description })),
  ...extra,
];

const CLASS_DESCRIPTIONS: Record<string, string> = {
  vehicle: "any vehicle",
  car: "a car",
  waymo: "a Waymo robotaxi",
  zoox: "a Zoox robotaxi",
  bus: "a bus",
  bike: "a bike, scooter or motorbike",
  person: "any person",
  pedestrian: "a pedestrian",
  delivery_rider: "a delivery rider",
  plant: "any plant",
  tree: "a tree",
  building: "a building",
  streetlight: "a streetlight or lamp post",
  billboard: "a billboard or sign",
};

const THEME_DESCRIPTIONS: Record<string, string> = {
  beach_party: "beach party: swimwear, surfboards",
  tech_bros: "tech bros: hoodies and vests",
  elvis: "everyone dressed as Elvis",
  delivery_riders: "everyone a delivery rider",
};

export function buildQuestions(view: View): Question[] {
  const targets: Choice[] = view.entities.map((e) => ({ value: e.id, description: `${e.color} ${e.class}: ${e.desc}`.trim() }));
  if (view.mode === "driving" && !view.entities.some((e) => e.id === "veh_me")) {
    targets.push({ value: "veh_me", description: "the player's own car" });
  }
  targets.push({ value: "none", description: "no specific visible thing" });

  return [
    {
      type: "choice",
      name: "scope",
      instructions:
        "What does the player's FIRST request change, right now? Answer for the first request only. Changing what something is made of (ice, rubber, glass, jelly, stone) is a property of that thing, not a transformation. A bare effect name (nitro, rush hour, earthquake) is a request. A request with a delay or a condition (in five minutes, later, after the race, when someone wins) is reject.",
      choices: [
        { value: "players", description: "the racers themselves (me, us, everyone): their size, shape, running, jumping, flying, or how their cars drive (super speed, nitro, grip, hop)" },
        { value: "world", description: "the whole world: time of day, weather, lights, gravity, ground, time speed, water, traffic, pedestrians and crowds (numbers, mood, costumes, big heads), or undo" },
        { value: "object", description: "one specific thing the player can see (that car, this building, the burning tree, my car, his car)" },
        { value: "class", description: "every object of one named kind at once (all the buses, every tree, all Waymos)" },
        { value: "reject", description: "impossible, or delayed or conditional (in five minutes, later, when I win); otherwise only if nothing else fits" },
      ],
    },
    { type: "choice", name: "player_effect", instructions: "Which change to all players is asked for?", choices: options(PLAYER_EFFECTS) },
    {
      type: "choice",
      name: "world_group",
      instructions: "Which part of the world does the request change?",
      choices: [
        NONE,
        { value: "sky", description: "time of day, weather, sky, city lights" },
        { value: "physics", description: "gravity, ground, air, slow motion or fast forward, flood water, lava, earthquake, cars flying or floating" },
        { value: "population", description: "traffic (rush hour, jams, empty roads), traffic lights, pedestrians, crowd mood or costumes, big heads" },
        { value: "undo", description: "undo or cancel the last change" },
      ],
    },
    { type: "choice", name: "sky_effect", instructions: "Which time, weather or lighting change is asked for?", choices: options(SKY_EFFECTS) },
    {
      type: "choice",
      name: "physics_effect",
      instructions: "Which physics change is asked for?",
      choices: options(PHYSICS_EFFECTS, [
        { value: "x_gravity_direction", description: "change which way gravity points: sideways, upside down, reversed" },
      ]),
    },
    { type: "choice", name: "population_effect", instructions: "Which traffic or crowd change is asked for?", choices: options(POPULATION_EFFECTS) },
    {
      type: "choice",
      name: "object_effect",
      instructions: "Which change to a thing or kind of thing is asked for? Material changes (ice, rubber, glass, jelly, stone) are properties, not transformations.",
      choices: options(OBJECT_EFFECTS, [
        { value: "x_transform", description: "turn an existing thing into a different kind of thing (a car into a boat); creating something new is not this" },
      ]),
    },
    {
      type: "choice",
      name: "class",
      instructions: "Which kind of thing does the request name?",
      choices: [
        { value: "none", description: "no kind of thing is named" },
        ...CLASSES.map((value) => ({ value, description: CLASS_DESCRIPTIONS[value] })),
        { value: "everything", description: "everything, all things at once" },
      ],
    },
    { type: "choice", name: "target", instructions: "Which visible thing does the player mean?", choices: targets },
    {
      type: "predicate",
      name: "described",
      instructions: "Does the player pick out one particular thing by its colour, position, name or state (the red car, the bus on the left, the YC building, the one on fire)?",
    },
    {
      type: "predicate",
      name: "self_ref",
      instructions: "Does the request refer to the player themself, their own car, or everyone (me, my car, us, everyone)?",
    },
    {
      type: "predicate",
      name: "other_player",
      instructions: "Does the request target another player (him, her, them, that player, their car, or a car driven by the OTHER player)?",
    },
    {
      type: "choice",
      name: "strength",
      instructions: "How strong should the change be?",
      choices: [
        { value: "small", description: "a little, slightly" },
        { value: "medium", description: "normal, not specified" },
        { value: "large", description: "a lot, super, mega, extreme" },
      ],
    },
    {
      type: "choice",
      name: "color",
      instructions: "Which colour does the player ask for?",
      choices: [NONE, ...COLORS.map((value) => ({ value, description: value }))],
    },
    {
      type: "choice",
      name: "theme",
      instructions: "Which costume theme for pedestrians is asked for?",
      choices: [NONE, ...THEMES.map((value) => ({ value, description: THEME_DESCRIPTIONS[value] }))],
    },
    {
      type: "choice",
      name: "reject_reason",
      instructions: "If the operator cannot do this, why not?",
      choices: [
        { value: "none", description: "the operator can do it" },
        { value: "unsupported", description: "a change the game does not have, when no other reason fits" },
        { value: "transformation", description: "turn a thing into a different kind of thing" },
        { value: "spawning", description: "create, spawn, summon or add something that is not there (a dragon, a UFO, ramps)" },
        { value: "teleport", description: "move someone or something somewhere else instantly" },
        { value: "deferred", description: "do something later: in five minutes, after the race, when someone wins" },
        { value: "cheat", description: "win, skip ahead, finish the race, get points, or remove other players" },
        { value: "vague", description: "unclear what change is wanted" },
        { value: "not_a_request", description: "chit-chat, a question or noise; a bare effect name like rush hour is a request" },
      ],
    },
  ];
}

export function buildInput(transcript: string, view: View, active: ActiveForPrompt[]): string {
  const burning = (id: string, cls: string) =>
    active.some((a) => a.effect === "set_fire" && (a.target === id || (a.target === "" && isA(cls, a.targetClass))));
  const lines = [
    `The player said: "${transcript}"`,
    `The player is ${view.mode === "driving" ? "driving their car" : "on foot"}. Time: ${view.timeOfDay || "day"}. Weather: ${view.weather || "clear"}.`,
    `Active effects: ${
      active.length
        ? active.map((e) => `${e.effect}${e.target ? ` on ${e.target}` : e.targetClass ? ` on every ${e.targetClass}` : ""} (${e.secondsLeft} s left)`).join(", ")
        : "none"
    }.`,
    "Visible things, most prominent first:",
  ];
  if (view.entities.length === 0) lines.push("(nothing in view)");
  for (const e of [...view.entities].sort((a, b) => a.view_rank - b.view_rank)) {
    const flags = [
      e.controlledBy === "me" ? "the player's own car" : "",
      e.controlledBy === "player" ? "driven by the OTHER player" : "",
      e.protected ? "protected landmark" : "",
      burning(e.id, e.class) ? "on fire" : "",
    ].filter(Boolean);
    lines.push(
      `${e.view_rank}. ${e.id}: ${e.class}, "${e.desc}"${e.color ? `, ${e.color}` : ""}, ${e.screen}, ${Math.round(e.distance_m)} m${flags.length ? ` (${flags.join(", ")})` : ""}`,
    );
  }
  return lines.join("\n");
}

type RawAnswer =
  | { type: "choice"; name: string; choice: string | boolean; confidence?: number; probabilities?: { value: string | boolean; probability: number }[] }
  | { type: "predicate"; name: string; probability: number }
  | { type: "refusal"; name: string };

const PREDICATES = ["described", "self_ref", "other_player"] as const;

export function parseAnswers(raw: RawAnswer[]): Answers {
  const byName = new Map(raw.map((answer) => [answer.name, answer]));
  const choice = (name: string): ChoiceAnswer => {
    const answer = byName.get(name);
    if (!answer || answer.type !== "choice") return { choice: "none", confidence: 0, probabilities: {} };
    const probabilities = Object.fromEntries((answer.probabilities ?? []).map((p) => [String(p.value), p.probability]));
    const picked = String(answer.choice);
    return { choice: picked, confidence: answer.confidence ?? probabilities[picked] ?? 1, probabilities };
  };
  const predicate = (name: string) => {
    const answer = byName.get(name);
    return answer?.type === "predicate" ? answer.probability : 0;
  };
  const answers = Object.fromEntries(
    [
      "scope", "player_effect", "world_group", "sky_effect", "physics_effect", "population_effect", "object_effect",
      "class", "target", "strength", "color", "theme", "reject_reason",
    ].map((name) => [name, choice(name)]),
  ) as Omit<Answers, (typeof PREDICATES)[number]>;
  return { ...answers, ...Object.fromEntries(PREDICATES.map((name) => [name, predicate(name)])) } as Answers;
}

export class JevUnavailable extends Error {}

/** One Jev decision with a 2 s timeout and one retry. */
export async function askJev(apiKey: string, input: string, questions: Question[]) {
  const body = JSON.stringify({ model: JEV_MODEL, input, questions });
  const errors: string[] = [];
  const started = Date.now();
  for (let attempt = 1; attempt <= 2; attempt++) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), JEV_TIMEOUT_MS);
    try {
      const response = await fetch(JEV_URL, {
        method: "POST",
        headers: { Authorization: `Bearer ${apiKey}`, "Content-Type": "application/json" },
        body,
        signal: controller.signal,
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}: ${(await response.text()).slice(0, 300)}`);
      const json = (await response.json()) as { answers: RawAnswer[] };
      return { raw: json.answers, answers: parseAnswers(json.answers), attempts: attempt, jevMs: Date.now() - started };
    } catch (error) {
      errors.push(controller.signal.aborted ? `timeout after ${JEV_TIMEOUT_MS} ms` : String(error));
    } finally {
      clearTimeout(timer);
    }
  }
  throw new JevUnavailable(errors.join("; "));
}
