import builtEffects from "../../unity/Assets/Resources/Operator/built-effects.json";

export type Strength = "small" | "medium" | "large";
export type Scope = "players" | "world" | "object" | "class";
export type WorldGroup = "sky" | "physics" | "population";
export type Range = readonly [number, number];

export const CLASS_PARENT: Record<string, string | null> = {
  vehicle: null,
  car: "vehicle",
  waymo: "car",
  zoox: "car",
  bus: "vehicle",
  bike: "vehicle",
  person: null,
  pedestrian: "person",
  delivery_rider: "person",
  plant: null,
  tree: "plant",
  building: null,
  streetlight: null,
  billboard: null,
};

export const CLASSES = Object.keys(CLASS_PARENT);

/** True when `cls` is `ancestor` or one of its subclasses. */
export function isA(cls: string, ancestor: string): boolean {
  for (let c: string | null = cls; c; c = CLASS_PARENT[c] ?? null) if (c === ancestor) return true;
  return false;
}

export const COLORS = [
  "red", "orange", "yellow", "green", "blue", "purple", "pink", "white", "black", "gold", "silver", "rainbow",
] as const;

export const THEMES = ["beach_party", "tech_bros", "elvis", "delivery_riders"] as const;

type Effect = { label: string; description: string; duration?: Range };

export const PLAYER_EFFECTS: Record<string, Effect> = {
  run_faster: { label: "Fast feet", description: "players run faster", duration: [15, 23] },
  run_slower: { label: "Slow feet", description: "players run slower", duration: [15, 23] },
  jump_higher: { label: "Big jumps", description: "players jump higher", duration: [15, 27] },
  slow_fall: { label: "Slow fall", description: "players fall slowly, like a feather", duration: [15, 27] },
  bigger: { label: "Giants", description: "the racers' whole bodies and cars become bigger", duration: [15, 23] },
  smaller: { label: "Tiny", description: "the racers' whole bodies and cars become smaller, tiny", duration: [15, 23] },
  chunky: { label: "Chunky", description: "players become wide and chunky", duration: [18, 34] },
  skinny: { label: "Skinny", description: "players become thin and skinny", duration: [18, 34] },
  flight: { label: "Flight", description: "players can fly", duration: [15, 18] },
  vehicle_faster: { label: "Fast cars", description: "players' vehicles go faster (super speed)", duration: [15, 23] },
  vehicle_slower: { label: "Slow cars", description: "players' vehicles go slower", duration: [15, 23] },
  nitro: { label: "Nitro", description: "players' vehicles get a nitro boost", duration: [15, 18] },
  grip_up: { label: "Grippy tyres", description: "players' vehicles grip the road better", duration: [15, 23] },
  grip_down: { label: "Slidey tyres", description: "players' vehicles lose grip and slide", duration: [15, 23] },
  tougher: { label: "Armour", description: "players and their vehicles become tougher", duration: [18, 34] },
  vehicle_hop: { label: "Car hop", description: "players' vehicles can hop or jump", duration: [15, 27] },
};

const WEATHER: Range = [23, 45];

export const SKY_EFFECTS: Record<string, Effect> = {
  night: { label: "Night", description: "make it night time", duration: WEATHER },
  day: { label: "Day", description: "make it daytime", duration: WEATHER },
  sunset: { label: "Sunset", description: "make it sunset or dusk", duration: WEATHER },
  fast_day_cycle: { label: "Time warp", description: "days and nights fly by", duration: WEATHER },
  rain: { label: "Rain", description: "make it rain", duration: WEATHER },
  snow: { label: "Snow", description: "make it snow", duration: WEATHER },
  fog: { label: "Fog", description: "roll in fog", duration: WEATHER },
  storm: { label: "Storm", description: "thunderstorm with lightning", duration: WEATHER },
  sandstorm: { label: "Sandstorm", description: "a desert sandstorm", duration: WEATHER },
  heatwave: { label: "Heatwave", description: "scorching heat haze", duration: WEATHER },
  blackout: { label: "Blackout", description: "all city lights go out", duration: [18, 34] },
  clear_weather: { label: "Clear skies", description: "stop the weather, clear the sky" },
  lights_on: { label: "Lights on", description: "turn the city lights back on" },
};

export const PHYSICS_EFFECTS: Record<string, Effect> = {
  gravity_lower: { label: "Low gravity", description: "lower gravity, moon gravity", duration: [15, 27] },
  gravity_higher: { label: "Heavy gravity", description: "stronger gravity, everything heavy", duration: [15, 27] },
  slippery_ground: { label: "Slippery", description: "the ground turns slippery or icy", duration: [18, 34] },
  sticky_ground: { label: "Sticky", description: "the ground turns sticky like glue", duration: [18, 34] },
  thick_air: { label: "Thick air", description: "the air turns thick like syrup", duration: [15, 23] },
  slow_motion: { label: "Slow-mo", description: "the whole world in slow motion", duration: [15, 23] },
  fast_forward: { label: "Fast forward", description: "the whole world sped up", duration: [15, 23] },
  flood: { label: "Flood", description: "flood the streets with water", duration: [23, 45] },
  floor_is_lava: { label: "Floor is lava", description: "the floor is lava", duration: [15, 23] },
  earthquake: { label: "Earthquake", description: "the ground shakes", duration: [15, 18] },
  cars_fly: { label: "Flying cars", description: "cars can fly", duration: [18, 34] },
  cars_on_water: { label: "Boat cars", description: "cars drive on water", duration: [23, 45] },
  floaty_crashes: { label: "Floaty crashes", description: "crashes send things floating", duration: [18, 34] },
  gravity_normal: { label: "Normal gravity", description: "put gravity back to normal" },
  normal_speed: { label: "Normal speed", description: "put time back to normal speed" },
  drain: { label: "Drained", description: "drain the flood water" },
};

const CROWD: Range = [23, 45];

export const POPULATION_EFFECTS: Record<string, Effect> = {
  more_traffic: { label: "Rush hour", description: "more traffic: rush hour, busy roads, traffic jams", duration: CROWD },
  less_traffic: { label: "Empty roads", description: "less traffic: empty, quiet roads", duration: CROWD },
  aggressive_traffic: { label: "Road rage", description: "traffic drives aggressively", duration: CROWD },
  green_lights: { label: "Green lights", description: "every traffic light turns green", duration: CROWD },
  more_pedestrians: { label: "Crowds", description: "more people on the streets", duration: CROWD },
  fewer_pedestrians: { label: "Ghost town", description: "fewer people on the streets", duration: CROWD },
  angry_npcs: { label: "Angry crowd", description: "people on the street get angry", duration: CROWD },
  calm_npcs: { label: "Chill crowd", description: "people on the street calm down", duration: CROWD },
  panic: { label: "Panic", description: "people on the street run around in panic", duration: [18, 34] },
  ped_theme: { label: "Theme party", description: "dress every pedestrian in a costume theme", duration: CROWD },
  big_heads: { label: "Big heads", description: "everyone, racers and pedestrians, gets giant cartoon heads", duration: CROWD },
};

const ANY = CLASSES;
const BURNABLE = ["vehicle", "plant", "building", "billboard"];
const MOVERS = ["vehicle", "person"];

type ObjectEffect = Effect & { classes: readonly string[] };

export const OBJECT_EFFECTS: Record<string, ObjectEffect> = {
  set_fire: { label: "On fire", description: "set it on fire", classes: BURNABLE, duration: [18, 34] },
  extinguish: { label: "Extinguished", description: "put out a fire", classes: BURNABLE },
  demolish: { label: "Demolished", description: "demolish or knock down a building", classes: ["building"] },
  explode: { label: "Boom", description: "blow up a vehicle", classes: ["vehicle"] },
  launch: { label: "Launched", description: "launch it into the air", classes: MOVERS },
  stop: { label: "Stopped", description: "make it stop moving", classes: MOVERS, duration: [15, 23] },
  speed_up: { label: "Sped up", description: "make it move faster", classes: MOVERS, duration: [15, 23] },
  slow_down: { label: "Slowed", description: "make it move slower", classes: MOVERS, duration: [15, 23] },
  grow: { label: "Grown", description: "make it bigger", classes: ANY, duration: [23, 45] },
  shrink: { label: "Shrunk", description: "make it smaller", classes: ANY, duration: [23, 45] },
  recolor: {
    label: "Repainted",
    description: "change its colour",
    classes: ["vehicle", "building", "plant", "billboard", "streetlight"],
  },
  see_through: { label: "See-through", description: "make it transparent, like glass", classes: ANY, duration: [18, 34] },
  bouncy: { label: "Bouncy", description: "give it a bouncy, rubbery material", classes: ANY, duration: [18, 34] },
  icy: { label: "Icy", description: "give it a slippery, icy surface", classes: ANY, duration: [18, 34] },
  rock_solid: { label: "Rock solid", description: "make it hard and indestructible like stone", classes: ANY, duration: [18, 34] },
  soft: { label: "Squishy", description: "make it soft and squishy, like jelly", classes: ANY, duration: [18, 34] },
  ghost: {
    label: "Ghosted",
    description: "make it a ghost you pass through",
    classes: ["vehicle", "plant", "streetlight", "billboard", "building"],
    duration: [15, 23],
  },
};

/** Counters end every active effect they list. */
export const COUNTERS: Record<string, readonly string[]> = {
  clear_weather: ["rain", "snow", "fog", "storm", "sandstorm", "heatwave"],
  lights_on: ["blackout"],
  gravity_normal: ["gravity_lower", "gravity_higher"],
  normal_speed: ["slow_motion", "fast_forward"],
  drain: ["flood"],
};

/**
 * Effects the Operator may apply: the ones the Unity client implements (the list shared with it in
 * unity/Assets/Resources/Operator/built-effects.json) plus the counters and extinguish, which the server
 * carries out by ending rows. Anything else in the catalogue is refused as not built yet.
 */
export const PLAYABLE: ReadonlySet<string> = new Set([...builtEffects.built, ...Object.keys(COUNTERS), "extinguish"]);

/** Effects in one set contradict each other, so a new one ends the others. */
export const EXCLUSIVE: readonly (readonly string[])[] = [
  ["night", "day", "sunset", "fast_day_cycle"],
  ["gravity_lower", "gravity_higher"],
  ["slippery_ground", "sticky_ground"],
  ["slow_motion", "fast_forward"],
  ["run_faster", "run_slower"],
  ["bigger", "smaller"],
  ["chunky", "skinny"],
  ["vehicle_faster", "vehicle_slower"],
  ["grip_up", "grip_down"],
  ["more_traffic", "less_traffic"],
  ["more_pedestrians", "fewer_pedestrians"],
  ["angry_npcs", "calm_npcs"],
  ["grow", "shrink"],
  ["stop", "speed_up", "slow_down"],
];

export const WORLD_EFFECTS: Record<string, Effect> = {
  ...SKY_EFFECTS,
  ...PHYSICS_EFFECTS,
  ...POPULATION_EFFECTS,
};

export function effectInfo(effect: string): Effect | undefined {
  return PLAYER_EFFECTS[effect] ?? WORLD_EFFECTS[effect] ?? OBJECT_EFFECTS[effect];
}

export function rollDuration(range: Range, random = Math.random) {
  return range[0] + Math.floor(random() * (range[1] - range[0] + 1));
}
