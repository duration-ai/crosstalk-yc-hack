export const DECISION = {
  endpoint: "https://ai-gateway.vercel.sh/v1/decisions",
  primaryModel: "typesafe-ai/jev-1.14.0",
  backupModel: "perplexity/pplx-decider-flash-v1.3",
  hedgeAfterMs: 1500,
  requestTimeoutMs: 8000,
  imageMaxSide: 1024,
  imageQuality: 82,
  imageDetail: "high",
} as const;

export const THRESHOLDS = {
  realPerson: 0.5,
  costume: 0.7,
} as const;

export const BUILD_STEPS = 5;

export const POLYFORK_CDN = "https://polyfork.dev/cdn";

export type Swatch = { value: string; hex: string; description: string };

export const SKIN: Swatch[] = [
  { value: "porcelain", hex: "#f6d7c3", description: "very pale skin with pink undertones" },
  { value: "fair", hex: "#eec1a0", description: "fair, light skin" },
  { value: "light", hex: "#e0a882", description: "light skin with warm or olive undertones" },
  { value: "medium", hex: "#c98d62", description: "medium, golden or olive-tan skin" },
  { value: "tan", hex: "#b07448", description: "tan to light brown skin" },
  { value: "brown", hex: "#8d5636", description: "brown skin" },
  { value: "dark", hex: "#6b3d24", description: "dark brown skin" },
  { value: "deep", hex: "#4a2817", description: "very dark, deep brown skin" },
];

export const HAIR: Swatch[] = [
  { value: "hidden", hex: "", description: "hair is not visible: fully covered by a hat, hood, helmet or scarf, or cropped out of the photo" },
  { value: "bald", hex: "", description: "bald or a shaved head with no visible hair" },
  { value: "black", hex: "#1e1a1a", description: "black hair" },
  { value: "dark-brown", hex: "#3b2618", description: "dark brown hair" },
  { value: "brown", hex: "#6b4226", description: "medium brown hair" },
  { value: "light-brown", hex: "#9a6a3e", description: "light brown or dark blond hair" },
  { value: "blonde", hex: "#e2b85c", description: "golden or honey blonde hair" },
  { value: "platinum", hex: "#f2e6c2", description: "very pale platinum blonde hair" },
  { value: "auburn", hex: "#8e3b1f", description: "auburn or reddish-brown hair" },
  { value: "ginger", hex: "#d2662a", description: "bright ginger or orange-red hair" },
  { value: "grey", hex: "#9c9a98", description: "grey or salt-and-pepper hair" },
  { value: "white", hex: "#edebe6", description: "white or silver hair" },
  { value: "pink", hex: "#ff8fb8", description: "hair dyed pink" },
  { value: "blue", hex: "#3e7bfa", description: "hair dyed blue" },
  { value: "purple", hex: "#9b5cf0", description: "hair dyed purple or violet" },
  { value: "green", hex: "#3cc07a", description: "hair dyed green" },
];

export const GARMENT: Swatch[] = [
  { value: "black", hex: "#23252b", description: "black" },
  { value: "charcoal", hex: "#4a4e57", description: "charcoal or dark grey" },
  { value: "grey", hex: "#9aa0a8", description: "mid or light grey, heather grey" },
  { value: "white", hex: "#f4f5f2", description: "white or off-white" },
  { value: "cream", hex: "#efe3c8", description: "cream, ivory or oatmeal" },
  { value: "khaki", hex: "#cdb48a", description: "beige, khaki, sand or camel" },
  { value: "brown", hex: "#7a5235", description: "brown, chocolate or tan leather" },
  { value: "navy", hex: "#22346e", description: "navy or dark blue" },
  { value: "blue", hex: "#2d8cff", description: "bright or royal blue" },
  { value: "light-blue", hex: "#9ed7ff", description: "light, sky or baby blue" },
  { value: "denim", hex: "#4d6fa3", description: "mid-wash blue denim" },
  { value: "teal", hex: "#17b2d0", description: "teal, turquoise or aqua" },
  { value: "green", hex: "#2ec27e", description: "green, kelly or emerald" },
  { value: "olive", hex: "#6e7a3a", description: "olive, khaki-green or army green" },
  { value: "yellow", hex: "#ffcb1f", description: "yellow, mustard or neon yellow" },
  { value: "orange", hex: "#ff8a1e", description: "orange, rust or neon orange" },
  { value: "red", hex: "#e2453a", description: "red or scarlet" },
  { value: "burgundy", hex: "#8c2238", description: "burgundy, maroon or wine" },
  { value: "pink", hex: "#ff8fb8", description: "pink, coral or salmon" },
  { value: "purple", hex: "#a35bff", description: "purple, violet or lilac" },
];

export const NONE_SWATCH: Swatch = { value: "none", hex: "", description: "not worn" };

export const FIGURES = [
  { value: "masculine", description: "masculine body shape: broader shoulders, straighter hips" },
  { value: "feminine", description: "feminine body shape: narrower shoulders, wider hips or a defined waist" },
] as const;

export const AGES = [
  { value: "kid", description: "a child, roughly under 12" },
  { value: "teen", description: "a teenager, roughly 12 to 19" },
  { value: "adult", description: "an adult, roughly 20 to 59" },
  { value: "old", description: "an older adult, roughly 60 or over" },
] as const;

export const BUILDS = ["very slim", "slim", "average", "sturdy", "heavy"] as const;

export const EXPRESSIONS = [
  { value: "neutral", knob: "neutral", description: "relaxed, neutral face" },
  { value: "smiling", knob: "warm", description: "smiling or laughing" },
  { value: "serious", knob: "stern", description: "serious, stern or frowning" },
  { value: "tired", knob: "weary", description: "tired, sad or bored" },
  { value: "surprised", knob: "alert", description: "surprised, excited or wide-eyed" },
  { value: "squinting", knob: "squint", description: "squinting, for example into the sun" },
] as const;

export type Slot = "skin" | "hair" | "top" | "outer" | "bottoms" | "headwear";

export type SlotZones = Partial<Record<Slot, { zones: string[]; shade?: string[] }>>;

export type Outfit = {
  value: string;
  base: string;
  description: string;
  slots: SlotZones;
  knobs?: Record<string, string | number | boolean>;
};

export const OUTFITS: Outfit[] = [
  {
    value: "tee",
    base: "suburban-resident-a9db9c",
    description: "a T-shirt, polo or short-sleeved casual shirt with long trousers or jeans, and no jacket",
    slots: { top: { zones: ["shirt", "cuff"] }, bottoms: { zones: ["denim", "cargo"], shade: ["waistband"] } },
  },
  {
    value: "flannel",
    base: "player-in-flannel-d0bae4",
    description: "a checked, plaid or flannel button-up shirt, worn open over a T-shirt or buttoned",
    slots: { top: { zones: ["shirt"] }, outer: { zones: ["coat"], shade: ["check"] }, bottoms: { zones: ["denim"] } },
    knobs: { gloves: false },
  },
  {
    value: "hoodie",
    base: "teen-player-37865e",
    description: "a hoodie or a pullover sweatshirt worn as the outermost layer",
    slots: { top: { zones: ["coat"] }, bottoms: { zones: ["denim"] } },
    knobs: { d20: false },
  },
  {
    value: "casual-jacket",
    base: "cafe-customer-ca7e8e",
    description: "a short casual jacket (denim, bomber, leather, track or field jacket) over a top",
    slots: { top: { zones: ["shirt"] }, outer: { zones: ["coat"], shade: ["coatTrim"] }, bottoms: { zones: ["denim"], shade: ["waistband"] } },
  },
  {
    value: "blazer",
    base: "school-teacher-8b8bfc",
    description: "a tailored blazer or suit jacket over a shirt, with or without a tie",
    slots: { top: { zones: ["shirt", "cuff"] }, outer: { zones: ["coat", "flap"], shade: ["coatTrim"] }, bottoms: { zones: ["denim"], shade: ["waistband"] } },
  },
  {
    value: "overcoat",
    base: "commuter-381e7f",
    description: "a long coat that reaches the thigh or knee: overcoat, trench coat or long wool coat",
    slots: { top: { zones: ["shirt"] }, outer: { zones: ["coat"], shade: ["coatTrim", "ventShade"] }, bottoms: { zones: ["denim"], shade: ["waistband"] } },
  },
  {
    value: "puffer",
    base: "spectator-d086fe",
    description: "a padded puffer, parka or heavy winter jacket",
    slots: { outer: { zones: ["coat"] }, bottoms: { zones: ["denim"] }, headwear: { zones: ["scarf"], shade: ["scarfB"] } },
  },
  {
    value: "cardigan",
    base: "player-in-a-cardigan-b558c9",
    description: "an open knit cardigan or a long knitted sweater over a top",
    slots: { top: { zones: ["shirt"] }, outer: { zones: ["coat"] }, bottoms: { zones: ["denim"] } },
    knobs: { bracelet: false },
  },
  {
    value: "tee-shorts",
    base: "lifeguard-321302",
    description: "summer clothes: a T-shirt or tank top with shorts",
    slots: { top: { zones: ["shirt", "coat"] }, bottoms: { zones: ["denim"] } },
  },
  {
    value: "sports-kit",
    base: "outfield-player-2b742a",
    description: "sportswear: a sports jersey, running top, leggings, tracksuit or athletic shorts",
    slots: { top: { zones: ["shirt"] }, bottoms: { zones: ["denim"] } },
  },
  {
    value: "dress",
    base: "diner-waitress-60f6a1",
    description: "a dress, or a skirt with a top",
    slots: { top: { zones: ["shirt"] }, headwear: { zones: ["cap"] } },
    knobs: { coffee: false },
  },
  {
    value: "lab-coat",
    base: "doctor-d49de0",
    description: "a white lab coat or a long white work coat",
    slots: { top: { zones: ["scrub"] }, outer: { zones: ["coat"], shade: ["coatTrim", "ventShade"] }, bottoms: { zones: ["denim"], shade: ["waistband"] } },
  },
  {
    value: "apron",
    base: "barista-505240",
    description: "an apron worn over everyday clothes",
    slots: { top: { zones: ["shirt"], shade: ["cuff"] }, outer: { zones: ["apron"] }, bottoms: { zones: ["denim"], shade: ["waistband"] } },
  },
  {
    value: "hi-vis",
    base: "construction-worker-c1ad8c",
    description: "workwear with a high-visibility vest or jacket, often with a hard hat",
    slots: { top: { zones: ["shirt"] }, outer: { zones: ["coat"] }, bottoms: { zones: ["denim"], shade: ["waistband"] }, headwear: { zones: ["helmet"], shade: ["helmetRim"] } },
  },
];

export type Costume = {
  value: string;
  base: string;
  description: string;
  expression: string;
  colors: Record<string, string>;
  knobs?: Record<string, string | number | boolean>;
  skin?: string[];
  hair?: string[];
};

export const COSTUMES: Costume[] = [
  {
    value: "astronaut",
    base: "astronaut-explorer-323482",
    description: "an astronaut costume: a one-piece spacesuit, usually with a space helmet or life-support backpack. A space-themed T-shirt or a single patch does not count",
    expression: "alert",
    colors: { suit: "#f7f8f9", helmet: "#f7f8f9", pack: "#f7f8f9", suitBand: "#ff8a1e", suitTrim: "#6c758b", visor: "#2d8cff", glove: "#1c2347", boot: "#f7f8f9" },
  },
  {
    value: "pirate",
    base: "pirate-captain-91f545",
    description: "a pirate costume with at least two of: a tricorn or skull hat, an eyepatch, a long frock coat, a sash, a toy cutlass. A striped top or a bandana alone does not count",
    expression: "warm",
    colors: { coat: "#c94a32", coatTrim: "#1c2347", brass: "#ffcb1f", shirt: "#eee7da", strap: "#2d8cff", sashStripe: "#1c2347", denim: "#1c2347", boot: "#4a2e1c", plume: "#ffcb1f" },
  },
  {
    value: "knight",
    base: "armoured-knight-hero-07121f",
    description: "a knight costume: plate armour, chainmail or a full suit of armour covering the torso, often with a helmet. A plastic sword alone does not count",
    expression: "stern",
    colors: { helmet: "#d7dce2", plate: "#b7bec8", platePad: "#9aa2ad", tunic: "#2d8cff", denim: "#1c2347", strap: "#7a5235", helmetTrim: "#ffcb1f" },
    knobs: { weapon: "empty", visor: "raised" },
  },
  {
    value: "chef",
    base: "carving-chef-102bf5",
    description: "chef's whites: a white double-breasted chef jacket together with a tall chef's toque. An apron alone does not count",
    expression: "warm",
    colors: { coat: "#f7f8f9", cap: "#ffffff", scarf: "#e2453a", gingham: "#1c2347", boot: "#1c2347" },
    knobs: { knife: false },
    hair: ["hair"],
  },
  {
    value: "firefighter",
    base: "firefighter-b13b36",
    description: "firefighter turnout gear: a heavy fire coat with reflective bands together with a firefighter's helmet. A red T-shirt or a toy helmet alone does not count",
    expression: "alert",
    colors: { coat: "#ffcb1f", reflect: "#f7f8f9", helmet: "#e2453a", glove: "#1c2347", boot: "#1c2347" },
    knobs: { axe: false },
  },
  {
    value: "police",
    base: "police-officer-1cc379",
    description: "a police officer uniform: a matching dark uniform shirt and trousers with a badge, plus a police cap or a duty belt. A dark shirt alone does not count",
    expression: "stern",
    colors: { shirt: "#22346e", capTrim: "#1c2347", brass: "#ffcb1f", buckleMetal: "#d7dce2" },
  },
  {
    value: "cowboy",
    base: "sheriff-986181",
    description: "a cowboy or sheriff costume: a cowboy hat together with cowboy boots, a waistcoat, chaps, a bandana or a star badge. A cowboy hat alone or plaid shirt alone does not count",
    expression: "squint",
    colors: { coat: "#7a5235", straw: "#cdb48a", scarf: "#e2453a", star: "#ffcb1f", shirt: "#9ed7ff", denim: "#4d6fa3", boot: "#4a2e1c" },
  },
  {
    value: "magician",
    base: "stage-magician-2ecd48",
    description: "a stage magician costume: a top hat together with a tailcoat or a cape. A suit alone does not count",
    expression: "warm",
    colors: { cape: "#1c2347", coat: "#1c2347", straw: "#1c2347", denim: "#1c2347", lining: "#a35bff", waistcoat: "#a35bff", shirt: "#f7f8f9", glove: "#f7f8f9" },
  },
  {
    value: "skeleton",
    base: "skeleton-mariachi-c1b4d5",
    description: "a skeleton or calavera costume: bones printed or painted over the whole body, or a skull-painted face with a matching outfit. A single skull print on a T-shirt does not count",
    expression: "neutral",
    colors: { coat: "#1c2347", denim: "#1c2347", straw: "#1c2347", shirt: "#f7f8f9", tie: "#ff8a1e" },
    knobs: { instrument: "none", marigolds: false, veladora: false, lit: false },
    skin: [],
  },
  {
    value: "scuba",
    base: "scuba-diver-bdd1c8",
    description: "a scuba or diving outfit: a full wetsuit together with a diving mask, snorkel, fins or air tank. A plain swimsuit does not count",
    expression: "alert",
    colors: { suit: "#1c2347", suitTrim: "#17b2d0", suitBand: "#ffcb1f", fin: "#ffcb1f", lens: "#b4e2fc", pack: "#ffcb1f" },
  },
  {
    value: "graduate",
    base: "graduate-in-gown-c72050",
    description: "an academic graduation gown together with a mortarboard cap",
    expression: "warm",
    colors: { coat: "#1c2347", hood: "#ffcb1f", cap: "#1c2347" },
  },
  {
    value: "mascot",
    base: "team-mascot-abf426",
    description: "a full-body furry animal or team mascot suit with a large character head",
    expression: "warm",
    colors: { fur: "#ff8a1e", furDark: "#c9661a", shirt: "#ffcb1f", shirtHalf: "#2d8cff" },
    knobs: { animal: "wildcat" },
  },
];

export const NO_COSTUME = { value: "none", description: "no costume: everyday clothes, workwear or a uniform that is not one of the costumes listed" };

export const SKIN_ZONES = ["skin"];
export const HAIR_ZONES = ["hair", "brow", "stache", "beard"];

export type BaseModel = {
  geometry: {
    figure?: { options: string[]; default: string };
    age?: { options: string[]; default: string };
    build?: { min: number; max: number; step: number; default: number };
    expression?: { options: string[]; default: string };
  };
  zones: Record<string, string>;
};

export const BASES: Record<string, BaseModel> = {
  "suburban-resident-a9db9c": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "squint"}},
    zones: { shirt: "#62798c", cuff: "#62798c", undershirt: "#b9b0a2", denim: "#4f3d30", waistband: "#46352a", cargo: "#4f3d30", boot: "#9c7856", sole: "#54402f", skin: "#ceaa88", hair: "#6e3613", brow: "#6e3613", eye: "#241c18", mouth: "#ceaa88", briefs: "#a59e89", bra: "#a59e89", crease: "#8e6a4e" },
  },
  "player-in-flannel-d0bae4": {
    geometry: {build: {min: 0.86, max: 1.18, step: 0.02, default: 0.9}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "warm"}},
    zones: { coat: "#6a3237", check: "#47292c", shirt: "#847d77", denim: "#9c825b", boot: "#6e5242", glove: "#72503a", hair: "#ae915f", stubble: "#b79569", skin: "#e6b697" },
  },
  "teen-player-37865e": {
    geometry: {build: {min: 0.86, max: 1.18, step: 0.02, default: 1}, figure: {options: ["feminine", "masculine"], default: "feminine"}, age: {options: ["kid", "teen", "adult"], default: "teen"}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { coat: "#3a977a", graphicA: "#d2a834", denim: "#2c2b2b", boot: "#ededeb", hair: "#2b2a29", ribbon: "#c0372f", skin: "#d6b293" },
  },
  "cafe-customer-ca7e8e": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "feminine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { shirt: "#8cc4a4", coat: "#c99a3a", coatTrim: "#b88a32", denim: "#5f7285", waistband: "#52657a", boot: "#7a4a2e", sole: "#4e3020", pack: "#4a3a2e", packTrim: "#433428", packStrap: "#2e231b", strap: "#2e231b", pouch: "#8f9496", skin: "#e0a96d", hair: "#4a3825", eye: "#3a2618", brow: "#2e2118", mouth: "#5c3a30", briefs: "#a59e89", bra: "#a59e89", crease: "#8e6a4e", stubble: "#4a3527" },
  },
  "school-teacher-8b8bfc": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { coat: "#6f533f", coatTrim: "#745845", flap: "#6f533f", shirt: "#ceb792", cuff: "#ceb792", chalk: "#edeae2", tie: "#4a3120", button: "#4a3120", tube: "#4a3120", card: "#929090", chrome: "#7e7978", denim: "#9c8260", waistband: "#8d7556", boot: "#664c39", sole: "#5b422f", belt: "#523926", strap: "#523926", buckleMetal: "#7e7978", hair: "#654a31", stache: "#99795d", brow: "#6f533a", eye: "#312924", mouth: "#7d6248", skin: "#e3be9a", crease: "#c9a682", bookCover: "#543928", bookPage: "#d2be9c", briefs: "#e0dace", bra: "#e0dace" },
  },
  "commuter-381e7f": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { shirt: "#43413e", cuff: "#5e5d5b", cuffFold: "#393837", coat: "#525150", coatTrim: "#474645", ventShade: "#2c2b2a", scarf: "#b6b4b1", denim: "#443933", waistband: "#2f2925", boot: "#4a372a", sole: "#2e211a", skin: "#eed3b8", hair: "#a68657", brow: "#4e3a22", eye: "#2a2320", mouth: "#9e8d7a", briefs: "#a59e89", bra: "#a59e89", crease: "#8e6a4e", case: "#323029", cup: "#ddd6cf", cupSleeve: "#d5af8a", cupLid: "#937a61" },
  },
  "spectator-d086fe": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "teen"}, build: {min: 0.8, max: 1.1, step: 0.02, default: 0.85}},
    zones: { scarf: "#7a262f", scarfB: "#4c86ad", coat: "#33475f", denim: "#6b4a33", boot: "#72503b", jeans: "#4a5760", trainer: "#cfcbc2", skin: "#d0a683", hair: "#553820" },
  },
  "player-in-a-cardigan-b558c9": {
    geometry: {build: {min: 0.86, max: 1.18, step: 0.02, default: 1}, figure: {options: ["feminine", "masculine"], default: "feminine"}, age: {options: ["adult", "old"], default: "adult"}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { coat: "#d6a24a", shirt: "#f4ddb6", denim: "#403e38", boot: "#6c3832", bead: "#48a062", hair: "#3e2a1e", skin: "#8f5532" },
  },
  "lifeguard-321302": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.9}},
    zones: { shirt: "#e8c73a", denim: "#c63a2e", sole: "#c63a2e", thong: "#6b4630", tube: "#1e1d1b", lens: "#2a2826", chrome: "#c6c2b8", coat: "#e0b52c", cuff: "#c63a2e", hair: "#1f1e1c", skin: "#d7ab86" },
  },
  "outfield-player-2b742a": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.8, max: 1.1, step: 0.02, default: 0.85}},
    zones: { shirt: "#6aa3bc", denim: "#434c61", hose: "#6aa3bc", coat: "#434c61", cuff: "#6aa3bc", stripe: "#6aa3bc", boot: "#434c61", skin: "#c8a282", hair: "#1d2024" },
  },
  "diner-waitress-60f6a1": {
    geometry: {figure: {options: ["feminine", "masculine"], default: "feminine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 1}},
    zones: { shirt: "#a8e4c8", shirtCollar: "#f4f2e4", apron: "#efebdb", cap: "#f4f2e6", boot: "#f6f4ea", hair: "#b8653a", skin: "#ebc09a", potCoffee: "#a65a12" },
  },
  "doctor-d49de0": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { scrub: "#8fc0dc", coat: "#f2f2f0", coatTrim: "#dcdedd", denim: "#55575a", waistband: "#45474a", boot: "#5a3a28", sole: "#3e2a1e", tube: "#14395c", chrome: "#8b98a3", skin: "#c8a87c", hair: "#5a3e28", eye: "#1a1f26", mouth: "#5c3a30", briefs: "#a59e89", bra: "#a59e89", crease: "#8e6a4e", stubble: "#4a3527", cuff: "#8fc0dc", ventShade: "#c6cacd", card: "#5f6266", print: "#dfe2e5", tablet: "#3f4247", tabletGlass: "#2a2c2f" },
  },
  "barista-505240": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "feminine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { shirt: "#d8c49a", cuff: "#c8b184", apron: "#4a3020", apronTie: "#4a3020", denim: "#3a2a20", waistband: "#30221a", boot: "#2e2018", sole: "#241914", skin: "#c8956a", hair: "#5a3a24", brow: "#5a3a24", eye: "#1c1f1e", mouth: "#8e5a48", briefs: "#a59e89", bra: "#a59e89", crease: "#8e6a4e", stubble: "#4a3527" },
  },
  "construction-worker-c1ad8c": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { shirt: "#30495c", cuff: "#30495c", coat: "#e0bc1e", coatTrim: "#e0bc1e", reflect: "#808080", piping: "#c8701f", cap: "#dbbb27", capTrim: "#a8764f", helmet: "#dbbb27", helmetRim: "#c7a621", hatBand: "#97614a", denim: "#6d4c35", waistband: "#5c3f2c", boot: "#d9a32e", sole: "#55392a", belt: "#463b37", buckleMetal: "#9f9890", skin: "#d8ae8b", hair: "#533921", eye: "#211f1d", mouth: "#bc8a74", brow: "#291a0a", underCollar: "#d8ae8b", paper: "#5a87a0", paperCore: "#3b6177", ribbon: "#5a3a26", briefs: "#a59e89", bra: "#a59e89", crease: "#8e6a4e", check: "#8b3d26" },
  },
  "astronaut-explorer-323482": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { suit: "#dfd2be", suitTrim: "#8c8880", suitPatch: "#8c8880", suitBand: "#c86e30", reflect: "#c86e30", suitCap: "#c86e30", suitPanel: "#c86e30", panelBar: "#8c8880", panelFace: "#c3bcb0", signal: "#62ada5", epaulette: "#8c8880", radio: "#8c8880", radioFace: "#c3bcb0", helmet: "#dfd2be", helmetRim: "#d2c6b2", helmetPod: "#c86e30", visor: "#62ada5", pack: "#dfd2be", packTrim: "#d2c5b1", packStrap: "#8c8880", packAccent: "#c86e30", roll: "#c86e30", strap: "#8c8880", glove: "#57534e", gloveCuff: "#57534e", gloveRim: "#57534e", boot: "#dfd2be", sole: "#c7bba7", steel: "#b0aaa2", gunmetal: "#413a33", lens: "#62ada5", skin: "#eed3b8", hair: "#5a4a3c", brow: "#4a3d33", eye: "#1d1e26", mouth: "#5c3a30", briefs: "#a59e89", bra: "#a59e89", crease: "#8e6a4e", stubble: "#4a3527" },
  },
  "pirate-captain-91f545": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { cuff: "#9c6b3c", coat: "#b0392e", coatTrim: "#7e2620", brass: "#e0b33c", button: "#e0b33c", straw: "#7e2620", hatBand: "#e0b33c", sole: "#3a2a1e", buckleMetal: "#e0b33c", skin: "#e0a579", eye: "#2a2320", mouth: "#5c3a30", briefs: "#a59e89", bra: "#a59e89", crease: "#8e6a4e", stubble: "#4a3527", shirt: "#59452f", strap: "#4467b2", sashStripe: "#2e4a86", hair: "#574732", brow: "#3e3022", denim: "#50647d", waistband: "#445468", boot: "#63452b", bootCuff: "#4e3622", plume: "#c13d40", frog: "#7a1e1e" },
  },
  "armoured-knight-hero-07121f": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.92}},
    zones: { helmet: "#a5a8a7", helmetTrim: "#65696e", helmetVoid: "#212429", plate: "#898d91", platePad: "#7a7e82", tunic: "#b08a5a", denim: "#414349", strap: "#7a5034", boot: "#6f5e4f", sole: "#313339", mitts: "#6e4a32", baton: "#8a6440", skin: "#9a7050" },
  },
  "carving-chef-102bf5": {
    geometry: {build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["adult", "old"], default: "old"}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { coat: "#f7f7f2", cap: "#f7f7f0", scarf: "#7a1020", button: "#111114", denim: "#e8e4dc", gingham: "#9c9a95", pinstripe: "#111114", boot: "#16151a", skin: "#e0b089", hair: "#dcd8ce", beard: "#e8e4dc" },
  },
  "firefighter-b13b36": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.88}},
    zones: { coat: "#cba87b", denim: "#cdab7e", reflect: "#d6d3ae", reflectMid: "#b4aa9c", helmet: "#26231f", helmetTrim: "#a9a69e", strap: "#9e8066", glove: "#9c8766", boot: "#887050", maceHead: "#a8362f", haft: "#b88a5c", skin: "#e0b892", hair: "#4a2e1c" },
  },
  "police-officer-1cc379": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { shirt: "#333749", cuff: "#333749", shirtCollar: "#2b2f40", cap: "#333749", capTrim: "#211f1d", brass: "#808080", button: "#333749", denim: "#333749", waistband: "#282b3b", boot: "#211f1d", sole: "#151312", belt: "#211f1d", strap: "#1c1b19", buckleMetal: "#7b8b8f", pouchStud: "#6e6e6e", skin: "#c8946a", hair: "#463b37", eye: "#211f1d", mouth: "#b98560", briefs: "#a59e89", bra: "#a59e89", crease: "#8a6244", beard: "#9e9e98" },
  },
  "sheriff-986181": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.01, default: 1.05}},
    zones: { coat: "#5a3f2c", scarf: "#8c1f22", star: "#d9d2c4", shirt: "#cdb894", denim: "#9e8664", boot: "#6b4a2e", strap: "#6b4a2c", pouch: "#8a5c3b", straw: "#9a9184", hatBand: "#6b4a2f", longCoat: "#a57c4d", skin: "#d9a87e", hair: "#2b2320" },
  },
  "stage-magician-2ecd48": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.94}},
    zones: { cape: "#2a2a2c", coat: "#2a2a28", facing: "#34343a", lining: "#5a2475", waistcoat: "#6d2e8d", hatBand: "#6d2e8c", straw: "#2a2a2b", shirt: "#f0efea", tie: "#16161a", glove: "#e8e7e4", denim: "#2a2a29", boot: "#141415", capeLining: "#5a2476", skin: "#dcb898", hair: "#141416", stache: "#141418" },
  },
  "skeleton-mariachi-c1b4d5": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 1.12}},
    zones: { bone: "#cdc2a6", socket: "#0f1428", hair: "#0f1429", stache: "#0f142a", straw: "#2a2a34", hatBand: "#ddd3c1", shirt: "#f4efe2", tie: "#8f2233", coat: "#2a2a30", braid: "#ddd3c0", flap: "#ddd3c4", button: "#ddd3c3", denim: "#2a2a31", stud: "#ddd3c6", strap: "#0f142e", buckleMetal: "#ddd3c5", boot: "#0f142c", sole: "#0f142d", petal: "#e3760f", bloom: "#ffc23d", dry: "#8c4529", wax: "#f4efe5", candleDish: "#7a5335", wick: "#0f1430", flame: "#ffc23e", flameTip: "#ffd9a0", wood: "#7a5336", soundboard: "#cf9f6a", fretboard: "#56504a", soundhole: "#0f1431", guitarStrap: "#b5613c", brass: "#c98b3f" },
  },
  "scuba-diver-bdd1c8": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.02, default: 0.86}, expression: {options: ["neutral", "warm", "stern", "weary", "alert", "squint"], default: "neutral"}},
    zones: { suit: "#3e4660", suitTrim: "#2c3246", wrap: "#2e2e30", wrapTrim: "#2e2e30", frame: "#2a2a2c", maskFrame: "#2a2a2c", lens: "#b4f4f4", strap: "#1e1e20", pack: "#3a3a3c", packTrim: "#a8f4f4", packValve: "#c8cacc", reflect: "#1e1e20", suitBand: "#262c3c", grease: "#6b6a3a", fin: "#3a3b3e", glove: "#3a4258", gloveCuff: "#1e1e20", sole: "#2a2a2c", boot: "#262c3c", skin: "#e8d9b5", hair: "#2a2320", eye: "#2a2320", mouth: "#5c3a30", briefs: "#a59e89", bra: "#a59e89", crease: "#8e6a4e", stubble: "#4a3527" },
  },
  "graduate-in-gown-c72050": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "feminine"}, age: {options: ["kid", "teen", "adult", "old"], default: "adult"}, build: {min: 0.86, max: 1.18, step: 0.04, default: 0.86}},
    zones: { coat: "#46484c", hood: "#7e2434", shirt: "#1a1a1c", denim: "#141416", boot: "#161618", cap: "#2c2e32", tassel: "#141416", paper: "#e4d6ba", ribbon: "#141416", skin: "#edc2a0", hair: "#121214" },
  },
  "team-mascot-abf426": {
    geometry: {figure: {options: ["masculine", "feminine"], default: "masculine"}, age: {options: ["teen", "adult"], default: "adult"}, build: {min: 0.85, max: 1.15, step: 0.05, default: 1}},
    zones: { shirt: "#e8c23a", shirtHalf: "#243f6e", cuff: "#243f6e", badgePatch: "#4a4744", fur: "#8c6246", furDark: "#6e4a33", mascotEye: "#1d2024", mascotBeak: "#f2a71b", mascotCrest: "#f4f6f3", skin: "#e3b595", hair: "#4a3628" },
  },
};
