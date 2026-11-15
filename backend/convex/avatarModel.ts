import {
  AGES,
  BASES,
  BUILD_STEPS,
  BUILDS,
  COSTUMES,
  EXPRESSIONS,
  FIGURES,
  GARMENT,
  HAIR,
  HAIR_ZONES,
  NO_COSTUME,
  NONE_SWATCH,
  OUTFITS,
  POLYFORK_CDN,
  SKIN,
  SKIN_ZONES,
  THRESHOLDS,
  type Slot,
  type Swatch,
} from "./avatarData";
import type { DecisionQuestion } from "./jev";

export const PROMPT =
  "A player took this photo of themselves to turn into a low-poly avatar in a racing game. " +
  "Answer every question about the main person: the largest, most central person in the photo. " +
  "Judge garments by their type and shape, and colours by the colour of the fabric itself, allowing for the lighting.";

const swatchChoices = (swatches: Swatch[]) => swatches.map(({ value, description }) => ({ value, description }));

export function buildQuestions(): DecisionQuestion[] {
  return [
    {
      type: "predicate",
      name: "real_person",
      instructions:
        "Is this a photograph that clearly shows a real, living person? Answer no for drawings, paintings, statues, mannequins, toys, an empty costume or suit, or a photo with no person in it.",
    },
    {
      type: "choice",
      name: "outfit",
      instructions:
        "Which outfit is closest to what the person is wearing? Judge only the type and cut of the clothes, never their colours or the person's body.",
      choices: OUTFITS.map(({ value, description }) => ({ value, description })),
    },
    {
      type: "choice",
      name: "figure",
      instructions: "Which body shape is closer to the person's?",
      choices: FIGURES.map(({ value, description }) => ({ value, description })),
    },
    {
      type: "choice",
      name: "age",
      instructions: "Which age band does the person look to be in?",
      choices: AGES.map(({ value, description }) => ({ value, description })),
    },
    {
      type: "score",
      name: "build",
      instructions: "How slim or heavy is the person's build?",
      levels: BUILDS.map((label) => ({ label })),
    },
    {
      type: "choice",
      name: "skin",
      instructions: "Which swatch is closest to the person's skin tone on the face and hands?",
      choices: swatchChoices(SKIN),
    },
    {
      type: "choice",
      name: "hair",
      instructions:
        "Which swatch is closest to the colour of the person's hair? Look past any hat to hair showing at the sides or back; answer hidden only when no hair shows at all.",
      choices: swatchChoices(HAIR),
    },
    {
      type: "choice",
      name: "top",
      instructions:
        "What is the main colour of the top: the shirt, T-shirt, sweater, hoodie or dress on the upper body, under any jacket or coat?",
      choices: swatchChoices(GARMENT),
    },
    {
      type: "choice",
      name: "outer",
      instructions:
        "What is the main colour of the outer layer worn over the top: a jacket, coat, blazer, cardigan, open overshirt, vest or apron? Answer none if there is no outer layer.",
      choices: swatchChoices([NONE_SWATCH, ...GARMENT]),
    },
    {
      type: "choice",
      name: "bottoms",
      instructions: "What is the main colour of the trousers, jeans, shorts, skirt or leggings? For a dress, answer the dress colour.",
      choices: swatchChoices(GARMENT),
    },
    {
      type: "choice",
      name: "headwear",
      instructions: "What is the main colour of a hat, cap, beanie, helmet or headscarf? Answer none if the head is bare.",
      choices: swatchChoices([NONE_SWATCH, ...GARMENT]),
    },
    {
      type: "choice",
      name: "expression",
      instructions: "Which is closest to the person's facial expression?",
      choices: EXPRESSIONS.map(({ value, description }) => ({ value, description })),
    },
    {
      type: "choice",
      name: "costume",
      instructions:
        "Is the person dressed in one of these recognised costumes? Only answer a costume when its whole description fits; a single item that happens to match is not the costume.",
      choices: [NO_COSTUME, ...COSTUMES.map(({ value, description }) => ({ value, description }))],
    },
  ];
}

export type RawAnswer = {
  type: string;
  name: string;
  probability?: number;
  choice?: string | boolean;
  confidence?: number;
  score?: number;
  probabilities?: { value: string | number | boolean; probability: number; label?: string }[];
};

export type ChoiceReading = { choice: string; confidence: number; probabilities: Record<string, number> };

export type Reading = {
  realPerson: number;
  build: number;
  outfit: ChoiceReading;
  figure: ChoiceReading;
  age: ChoiceReading;
  skin: ChoiceReading;
  hair: ChoiceReading;
  top: ChoiceReading;
  outer: ChoiceReading;
  bottoms: ChoiceReading;
  headwear: ChoiceReading;
  expression: ChoiceReading;
  costume: ChoiceReading;
};

const CHOICE_NAMES = ["outfit", "figure", "age", "skin", "hair", "top", "outer", "bottoms", "headwear", "expression", "costume"] as const;

export class IncompleteDecision extends Error {}

export function readAnswers(raw: RawAnswer[]): Reading {
  const byName = new Map(raw.map((answer) => [answer.name, answer]));
  const choice = (name: string): ChoiceReading => {
    const answer = byName.get(name);
    if (!answer || answer.type !== "choice" || answer.choice === undefined) throw new IncompleteDecision(`No answer for ${name}`);
    const probabilities = Object.fromEntries((answer.probabilities ?? []).map((p) => [String(p.value), p.probability]));
    const picked = String(answer.choice);
    return { choice: picked, confidence: answer.confidence ?? probabilities[picked] ?? 1, probabilities };
  };
  const person = byName.get("real_person");
  const build = byName.get("build");
  if (person?.type !== "predicate" || typeof person.probability !== "number") throw new IncompleteDecision("No answer for real_person");
  if (build?.type !== "score" || typeof build.score !== "number") throw new IncompleteDecision("No answer for build");
  return {
    realPerson: person.probability,
    build: build.score,
    ...(Object.fromEntries(CHOICE_NAMES.map((name) => [name, choice(name)])) as Pick<Reading, (typeof CHOICE_NAMES)[number]>),
  };
}

export type Recolor = { zone: string; from: string; to: string };

export type AvatarParams = {
  base: string;
  outfit: string;
  costume: string | null;
  figure: string | null;
  age: string | null;
  build: number | null;
  expression: string | null;
  glbUrl: string;
  fallbackGlbUrl: string;
  recolor: Recolor[];
  swatches: { skin: string; hair: string; top: string; outer: string; bottoms: string; headwear: string };
};

export class RetakeRequired extends Error {}

const SHADE = 0.78;

export function shade(hex: string) {
  const n = parseInt(hex.slice(1), 16);
  const channel = (shift: number) => Math.round(((n >> shift) & 255) * SHADE).toString(16).padStart(2, "0");
  return `#${channel(16)}${channel(8)}${channel(0)}`;
}

const hexOf = (swatches: Swatch[], value: string) => swatches.find((swatch) => swatch.value === value)?.hex ?? "";

export function snapAge(age: string, options: string[]) {
  const order = AGES.map((band) => band.value as string);
  const want = order.indexOf(age);
  return [...options].sort(
    (a, b) => Math.abs(order.indexOf(a) - want) - Math.abs(order.indexOf(b) - want) || Math.abs(order.indexOf(a) - 2) - Math.abs(order.indexOf(b) - 2),
  )[0];
}

export function snapBuild(score: number, range: { min: number; max: number; step: number }) {
  const levels = BUILDS.length - 1;
  const t = Math.round((Math.min(Math.max(score, 0), levels) / levels) * (BUILD_STEPS - 1)) / (BUILD_STEPS - 1);
  const steps = Math.round((t * (range.max - range.min)) / range.step);
  return Number(Math.min(range.max, range.min + steps * range.step).toFixed(2));
}

export function remixUrl(base: string, knobs: Record<string, string | number | boolean>) {
  const sorted = Object.fromEntries(Object.entries(knobs).sort(([a], [b]) => a.localeCompare(b)));
  return `${POLYFORK_CDN}/${base}-remix.glb?p=${encodeURIComponent(JSON.stringify(sorted))}`;
}

export function pickCostume(costume: ChoiceReading) {
  const [value, probability] = Object.entries(costume.probabilities)
    .filter(([value]) => value !== NO_COSTUME.value)
    .sort(([, a], [, b]) => b - a)[0] ?? [costume.choice, costume.choice === NO_COSTUME.value ? 0 : costume.confidence];
  return probability >= THRESHOLDS.costume ? COSTUMES.find((preset) => preset.value === value) ?? null : null;
}

export function toAvatar(reading: Reading): AvatarParams {
  if (reading.realPerson < THRESHOLDS.realPerson) throw new RetakeRequired("The photo does not clearly show a person");
  const outfit = OUTFITS.find((candidate) => candidate.value === reading.outfit.choice) ?? OUTFITS[0];
  const costume = pickCostume(reading.costume);
  const base = costume?.base ?? outfit.base;
  const model = BASES[base];
  const { geometry, zones } = model;

  const recolor: Recolor[] = [];
  const paint = (zoneNames: string[], to: string) => {
    for (const zone of zoneNames) {
      const from = zones[zone];
      if (!from || !to || recolor.some((entry) => entry.from === from)) continue;
      if (from !== to) recolor.push({ zone, from, to });
      else recolor.push({ zone, from, to });
    }
  };

  const skinHex = hexOf(SKIN, reading.skin.choice);
  const hair = reading.hair.choice;
  paint(costume?.skin ?? SKIN_ZONES, skinHex);
  if (hair === "bald") paint(["hair"], skinHex);
  else if (hair !== "hidden") paint(costume?.hair ?? HAIR_ZONES, hexOf(HAIR, hair));

  if (costume) {
    for (const [zone, hex] of Object.entries(costume.colors)) paint([zone], hex);
  } else {
    const top = hexOf(GARMENT, reading.top.choice);
    const colors: Partial<Record<Slot, string>> = {
      top,
      bottoms: outfit.value === "dress" ? top : hexOf(GARMENT, reading.bottoms.choice),
      outer: reading.outer.choice === "none" ? (outfit.slots.top ? top : "") : hexOf(GARMENT, reading.outer.choice),
      headwear: reading.headwear.choice === "none" ? "" : hexOf(GARMENT, reading.headwear.choice),
    };
    for (const slot of ["top", "bottoms", "outer", "headwear"] as const) {
      const target = outfit.slots[slot];
      const hex = colors[slot];
      if (!target || !hex) continue;
      paint(target.zones, hex);
      paint(target.shade ?? [], shade(hex));
    }
  }

  const figure = geometry.figure?.options.includes(reading.figure.choice) ? reading.figure.choice : null;
  const age = geometry.age ? snapAge(reading.age.choice, geometry.age.options) : null;
  const build = geometry.build ? snapBuild(reading.build, geometry.build) : null;
  const wanted = costume?.expression ?? EXPRESSIONS.find((option) => option.value === reading.expression.choice)?.knob ?? "neutral";
  const expression = geometry.expression?.options.includes(wanted) ? wanted : null;

  const knobs: Record<string, string | number | boolean> = { ...(costume?.knobs ?? outfit.knobs) };
  if (figure) knobs.figure = figure;
  if (age) knobs.age = age;
  if (build !== null) knobs.build = build;
  if (expression) knobs.expression = expression;

  return {
    base,
    outfit: outfit.value,
    costume: costume?.value ?? null,
    figure,
    age,
    build,
    expression,
    glbUrl: remixUrl(base, knobs),
    fallbackGlbUrl: `${POLYFORK_CDN}/${base}.glb`,
    recolor,
    swatches: {
      skin: reading.skin.choice,
      hair,
      top: reading.top.choice,
      outer: reading.outer.choice,
      bottoms: reading.bottoms.choice,
      headwear: reading.headwear.choice,
    },
  };
}
