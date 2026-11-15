import type { WorldChangeParams } from "./worldChanges";

export type OperatorRacer = { playerId: string; displayName: string; place: number };

export type OperatorInput = {
  text?: string;
  audio?: { data: ArrayBuffer; mimeType: string };
  caller: OperatorRacer;
  /** Everyone in the race in place order, the caller included. */
  racers: OperatorRacer[];
};

export type OperatorReply = {
  /** What the Operator says back, short enough for her bar. */
  line: string;
  /** Exactly one world change; the action validates it before it lands in the room's log. */
  command: { type: string; params: WorldChangeParams };
};

export interface OperatorProvider {
  name: string;
  acceptsAudio: boolean;
  respond(input: OperatorInput): Promise<OperatorReply>;
}

type Reply = (match: RegExpExecArray, text: string, input: OperatorInput) => OperatorReply;

const capitalize = (word: string) => word[0].toUpperCase() + word.slice(1);

const ramp: OperatorReply = { line: "Ramps ahead!", command: { type: "ramp", params: { count: 3 } } };

const RULES: { pattern: RegExp; reply: Reply }[] = [
  {
    pattern: /\b(yellow|pink|blue|purple|orange|green|red|white|black|mint|lilac)\b/,
    reply: ([, color]) => ({ line: `${capitalize(color)} it is!`, command: { type: "recolor", params: { color } } }),
  },
  {
    pattern: /\b(gravity|float\w*|moon|heavy|heavier|weightless)\b/,
    reply: (_, text) =>
      /\b(normal|reset|back)\b/.test(text)
        ? { line: "Gravity's back.", command: { type: "gravity", params: { level: "normal" } } }
        : /\b(heavy|heavier|raise|more|crush\w*)\b/.test(text) && !/\b(low|lower|less|no|zero)\b/.test(text)
          ? { line: "Heavy it is!", command: { type: "gravity", params: { level: "high" } } }
          : { line: "Gravity, off!", command: { type: "gravity", params: { level: "low" } } },
  },
  {
    pattern: /\b(ice|icy|iced|slippery|slick|skat\w*|freeze|frozen|melt\w*|thaw\w*)\b/,
    reply: (_, text) =>
      /\b(melt\w*|thaw\w*)\b/.test(text)
        ? { line: "Ice melted.", command: { type: "ice", params: { level: "off" } } }
        : { line: "Roads on ice!", command: { type: "ice", params: { level: "on" } } },
  },
  {
    pattern: /\b(fog|foggy|karl|mist|misty)\b/,
    reply: (_, text) =>
      /\b(clear|lift|no|away|gone)\b/.test(text)
        ? { line: "Fog's gone.", command: { type: "fog", params: { level: "clear" } } }
        : { line: "Fog's rolling in!", command: { type: "fog", params: { level: "thick" } } },
  },
  {
    pattern: /\b(night|dark|midnight|dusk|sunset|evening|golden|sunrise|dawn|daytime|daylight|noon|morning|day)\b/,
    reply: ([word]) => {
      const [timeOfDay, line] = /night|dark|midnight/.test(word)
        ? ["night", "Lights out!"]
        : /dusk|sunset|evening/.test(word)
          ? ["dusk", "Dusk it is!"]
          : /golden|sunrise|dawn/.test(word)
            ? ["golden", "Golden hour!"]
            : ["day", "Good morning!"];
      return { line, command: { type: "time", params: { timeOfDay } } };
    },
  },
  {
    pattern: /\b(traffic|rush hour|gridlock|jam)\b/,
    reply: (_, text) =>
      /\b(clear|less|no|away)\b/.test(text)
        ? { line: "Roads cleared.", command: { type: "traffic", params: { level: "light" } } }
        : { line: "Rush hour!", command: { type: "traffic", params: { level: "heavy" } } },
  },
  {
    pattern: /\b(swap\w*|switch\w*|trade\w*|teleport\w*)\b/,
    reply: (_, text, { caller, racers }) => {
      const others = racers.filter((racer) => racer.playerId !== caller.playerId);
      const words = new Set(text.split(/[^\p{L}\p{N}]+/u));
      const named = others.find((racer) => {
        const name = racer.displayName.split(/\s+/)[0].toLowerCase().replace(/[^\p{L}\p{N}]/gu, "");
        return name !== "" && words.has(name);
      });
      const target =
        named ??
        others.find((racer) => racer.place === caller.place - 1) ??
        others.find((racer) => racer.place === caller.place + 1);
      if (!target) return { ...ramp, line: "Nobody to swap with. Ramps!" };
      return {
        line: `Swapping you with ${target.displayName}!`,
        command: { type: "swap", params: { targetPlayerId: target.playerId } },
      };
    },
  },
  {
    pattern: /\b(shrink\w*|tiny|small|smaller|mini|little|giant|huge|big|bigger|grow|massive|monster)\b/,
    reply: ([word]) =>
      /shrink|tiny|small|mini|little/.test(word)
        ? { line: "Tiny cars!", command: { type: "resize", params: { size: "small" } } }
        : { line: "Big cars!", command: { type: "resize", params: { size: "big" } } },
  },
  { pattern: /\b(ramp|ramps|jump|jumps|launch\w*|fly|flying)\b/, reply: () => ramp },
  {
    pattern: /\b(rain|rainy|raining|storm\w*|wet|drizzle)\b/,
    reply: (_, text) =>
      /\b(stop\w*|no|off|end|enough)\b/.test(text)
        ? { line: "Rain's stopped.", command: { type: "rain", params: { level: "off" } } }
        : { line: "Make it rain!", command: { type: "rain", params: { level: "on" } } },
  },
  {
    pattern: /\b(snow|snowy|snowing|blizzard|flurries)\b/,
    reply: (_, text) =>
      /\b(stop\w*|no|off|end|enough)\b/.test(text)
        ? { line: "Snow's done.", command: { type: "snow", params: { level: "off" } } }
        : { line: "Let it snow!", command: { type: "snow", params: { level: "on" } } },
  },
];

const SURPRISES: OperatorReply[] = [
  ramp,
  { line: "Fog's rolling in!", command: { type: "fog", params: { level: "thick" } } },
  { line: "Roads on ice!", command: { type: "ice", params: { level: "on" } } },
  { line: "Gravity, off!", command: { type: "gravity", params: { level: "low" } } },
  { line: "Lights out!", command: { type: "time", params: { timeOfDay: "night" } } },
  { line: "Rush hour!", command: { type: "traffic", params: { level: "heavy" } } },
  { line: "Tiny cars!", command: { type: "resize", params: { size: "small" } } },
  { line: "Pink it is!", command: { type: "recolor", params: { color: "pink" } } },
];

/** No keys, no network: the earliest keyword in the utterance wins; anything else gets a surprise picked from the text. */
export const keywordProvider: OperatorProvider = {
  name: "keyword",
  acceptsAudio: false,
  async respond(input) {
    const text = (input.text ?? "").toLowerCase();
    let best: { index: number; reply: OperatorReply } | undefined;
    for (const rule of RULES) {
      const match = rule.pattern.exec(text);
      if (match && (!best || match.index < best.index)) {
        best = { index: match.index, reply: rule.reply(match, text, input) };
      }
    }
    if (best) return best.reply;
    let hash = 0;
    for (const char of text) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
    const surprise = SURPRISES[hash % SURPRISES.length];
    return { ...surprise, line: `Didn't catch that. ${surprise.line}` };
  },
};

/**
 * Providers by the name OPERATOR_PROVIDER selects. To add an LLM or voice provider, implement OperatorProvider in
 * its own module (reading its API key from its own env var), register it here, and set OPERATOR_PROVIDER to its name.
 */
const PROVIDERS: Record<string, OperatorProvider> = {
  keyword: keywordProvider,
};

export function configuredProvider() {
  const name = process.env.OPERATOR_PROVIDER?.trim() || "keyword";
  const provider = PROVIDERS[name];
  if (provider) return provider;
  console.error(`Unknown OPERATOR_PROVIDER "${name}"; using the keyword provider`);
  return keywordProvider;
}
