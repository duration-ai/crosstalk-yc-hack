/**
 * Live test of the deployed Operator: sends each phrase through operator.call on CONVEX_URL,
 * checks the outcome against what a person would expect, and times call → subscription update.
 *
 *   CONVEX_URL=https://<deployment>.convex.cloud npx tsx scripts/live-operator.mts [--json out.json]
 */
import { writeFileSync } from "node:fs";
import { ConvexClient } from "convex/browser";
import { api } from "../convex/_generated/api.js";
import { PLAYABLE } from "../convex/operatorCatalogue.js";

type Expect = { effect?: string; refuse?: string | string[]; target?: string; targetClass?: string; color?: string; theme?: string; vehicle?: string; outcome?: string };
type Case = [string, Expect, string, typeof view?];

const url = process.env.CONVEX_URL;
if (!url) throw new Error("Set CONVEX_URL");
const client = new ConvexClient(url);

const tim = await client.mutation(api.players.create, { displayName: "Live Tim" });
const maya = await client.mutation(api.players.create, { displayName: "Live Maya" });
const { room } = await client.mutation(api.rooms.create, { playerId: tim.id, freeAsks: true });
await client.mutation(api.rooms.join, { playerId: maya.id, code: room.code });

const e = (id: string, cls: string, rank: number, desc: string, color: string, extra: object = {}) => ({
  id, class: cls, desc, color, screen: rank <= 2 ? "centre, large" : "right, small", distance_m: 12 * rank, view_rank: rank,
  protected: false, controlledBy: "", ...extra,
});
const view = {
  mode: "driving" as const,
  timeOfDay: "day",
  weather: "clear",
  entities: [
    e("veh_me", "car", 0, "your blue racer", "blue", { controlledBy: "me" }),
    e("veh_12", "waymo", 1, "Waymo robotaxi", "white"),
    e(`car:${maya.id}`, "car", 2, "purple racer", "purple", { controlledBy: "player" }),
    e("bus_2", "bus", 3, "Muni bus", "red"),
    e("tree_3", "tree", 4, "street tree", "green"),
    e("ped_9", "pedestrian", 5, "jogger", "yellow"),
    e("bld_yc", "building", 6, "YC office", "grey", { protected: true }),
    e("bld_7", "building", 7, "warehouse", "brown"),
  ],
};

const CASES: Case[] = [
  ["make it rain", { effect: "rain" }, "world"],
  ["make it night", { effect: "night" }, "world"],
  ["give us a sunset", { effect: "sunset" }, "world"],
  ["let it snow", { effect: "snow" }, "world"],
  ["roll in the fog", { effect: "fog" }, "world"],
  ["turn off all the city lights", { effect: "blackout" }, "world"],
  ["turn the lights back on", { effect: "lights_on" }, "world"],
  ["stop the weather", { effect: "clear_weather" }, "world"],
  ["low gravity", { effect: "gravity_lower" }, "world"],
  ["back to normal gravity", { effect: "gravity_normal" }, "world"],
  ["slow motion", { effect: "slow_motion" }, "world"],
  ["the floor is lava", { effect: "floor_is_lava" }, "world"],
  ["flood the streets", { effect: "flood" }, "world"],
  ["earthquake", { effect: "earthquake" }, "world"],
  ["rush hour traffic", { effect: "more_traffic" }, "world"],
  ["dress everyone like elvis", { effect: "ped_theme", theme: "elvis" }, "world"],
  ["give me a sports car", { effect: "change_vehicle", vehicle: "sports_car" }, "players"],
  ["put me in a bus", { effect: "change_vehicle", vehicle: "bus" }, "players"],
  ["I want a bike", { effect: "change_vehicle", vehicle: "bike" }, "players"],
  ["give me a flying car", { effect: "change_vehicle", vehicle: "flying_car" }, "players"],
  ["give me a cable car", { effect: "change_vehicle", vehicle: "cable_car" }, "players"],
  ["give me a lowrider", { effect: "change_vehicle", vehicle: "lowrider" }, "players"],
  ["give me a Burning Man art car", { effect: "change_vehicle", vehicle: "art_car" }, "players"],
  ["send the Waymos after them", { effect: "waymo_swarm" }, "world"],
  ["Waymo attack", { effect: "waymo_swarm" }, "world"],
  ["sic the robotaxis on everyone", { effect: "waymo_swarm" }, "world"],
  ["give everyone big heads", { effect: "big_heads" }, "world"],
  ["make all the traffic lights green", { effect: "green_lights" }, "world"],
  ["super speed", { effect: "vehicle_faster" }, "players"],
  ["let us fly", { effect: "flight" }, "players"],
  ["make everyone tiny", { effect: "smaller" }, "players"],
  ["nitro", { effect: "nitro" }, "players"],
  ["make our cars hop", { effect: "vehicle_hop" }, "players"],
  ["make that Waymo pink", { effect: "recolor", target: "veh_12", color: "pink" }, "object"],
  ["set the tree on fire", { effect: "set_fire", target: "tree_3" }, "object"],
  ["put out the fire", { effect: "extinguish", target: "tree_3" }, "object"],
  ["launch that bus", { effect: "launch", target: "bus_2" }, "object"],
  ["stop that jogger", { effect: "stop", target: "ped_9" }, "object"],
  ["make the Waymo bouncy", { effect: "bouncy", target: "veh_12" }, "object"],
  ["paint my car gold", { effect: "recolor", target: `car:${tim.id}`, color: "gold" }, "object"],
  ["make all the buses purple", { effect: "recolor", targetClass: "bus", color: "purple" }, "class"],
  ["set every tree on fire", { effect: "set_fire", targetClass: "tree" }, "class"],
  ["slow him down", { effect: "vehicle_slower", outcome: "pending" }, "other player"],
  ["set her car on fire", { refuse: "player_target" }, "other player"],
  ["demolish the YC building", { refuse: "protected" }, "refusal"],
  ["demolish the YC building", { refuse: "not_visible" }, "refusal", { ...view, entities: view.entities.filter((x) => x.id !== "bld_yc") }],
  ["make gravity go sideways", { refuse: "unsupported" }, "refusal"],
  ["teleport me to the finish line", { refuse: ["teleport", "cheat"] }, "refusal"],
  ["spawn a dragon", { refuse: "spawning" }, "refusal"],
  ["turn the bus into a boat", { refuse: "transformation" }, "refusal"],
  ["let me win the race", { refuse: "cheat" }, "refusal"],
  ["make it rain in five minutes", { refuse: "deferred" }, "refusal"],
  ["hey how's it going", { refuse: "not_a_request" }, "refusal"],
  ["set everything on fire", { refuse: "too_broad" }, "refusal"],
  ["um do the thing", { refuse: ["vague", "not_a_request"] }, "refusal"],
  ["undo that", { refuse: "undo" }, "refusal"],
  ["take it back", { refuse: "undo" }, "refusal"],
  ["never mind, cancel that", { refuse: "undo" }, "refusal"],
];

// Effects the client has not built yet are refused (built-effects.json); a counter for one of them has
// nothing to counter.
for (const c of CASES) {
  if (c[1].effect && !PLAYABLE.has(c[1].effect)) c[1] = { refuse: "not_built" };
  if (c[1].effect === "lights_on" && !PLAYABLE.has("blackout")) c[1] = { refuse: "no_op" };
}

let latest: { effects: { id: string; endsAt: number }[] } = { effects: [] };
const waiters: { id: string; endsAt?: number; resolve: (t: number) => void }[] = [];
client.onUpdate(api.operator.activeEffects, { roomId: room.id }, (value) => {
  latest = value;
  const now = performance.now();
  for (let i = waiters.length - 1; i >= 0; i--) {
    const w = waiters[i];
    if (value.effects.some((x) => x.id === w.id && (w.endsAt === undefined || x.endsAt >= w.endsAt))) {
      w.resolve(now);
      waiters.splice(i, 1);
    }
  }
});
await new Promise((r) => setTimeout(r, 1000));

function seen(id: string, endsAt: number) {
  if (latest.effects.some((x) => x.id === id && x.endsAt >= endsAt)) return Promise.resolve(performance.now());
  return new Promise<number>((resolve) => {
    waiters.push({ id, endsAt, resolve });
    setTimeout(() => resolve(NaN), 5000);
  });
}

function check(expect: Expect, result: any, timId: string) {
  if (expect.refuse) {
    const ok = [expect.refuse].flat();
    return result.outcome === "refused" && ok.includes(result.refusal.reason);
  }
  if (result.outcome !== (expect.outcome ?? "applied")) return false;
  const fx = result.effect;
  return (
    fx.effect === expect.effect &&
    (expect.target === undefined || fx.target === expect.target) &&
    (expect.targetClass === undefined || fx.targetClass === expect.targetClass) &&
    (expect.color === undefined || fx.color === expect.color) &&
    (expect.theme === undefined || fx.theme === expect.theme) &&
    (expect.vehicle === undefined || fx.vehicle === expect.vehicle)
  );
}

const rows = [];
for (const [phrase, expect, group, caseView] of CASES) {
  const t0 = performance.now();
  const result: any = await client.action(api.operator.call, { roomId: room.id, playerId: tim.id, transcript: phrase, view: caseView ?? view });
  const t1 = performance.now();
  const shown = result.outcome === "applied" ? await seen(result.effect.id, result.effect.endsAt) : t1;
  const got = result.outcome === "refused" ? `refused:${result.refusal.reason}` : `${result.outcome}:${result.effect.effect}${result.effect.target ? "@" + result.effect.target : ""}${result.effect.targetClass && !result.effect.target ? "@every " + result.effect.targetClass : ""}${result.effect.color ? " " + result.effect.color : ""}${result.effect.theme ? " " + result.effect.theme : ""}`;
  const want = expect.refuse ? `refused:${[expect.refuse].flat().join("|")}` : `${expect.outcome ?? "applied"}:${expect.effect}`;
  const pass = check(expect, result, tim.id);
  rows.push({ phrase, group, want, got, pass, line: result.line, callMs: Math.round(t1 - t0), shownMs: Math.round(shown - t0), jevMs: result.timings.jevMs, serverMs: result.timings.totalMs });
  console.log(`${pass ? "PASS" : "FAIL"} ${String(Math.round(shown - t0)).padStart(5)} ms  ${phrase.padEnd(34)} ${got}${pass ? "" : "   (wanted " + want + ")"}`);
}

const pct = (xs: number[], p: number) => [...xs].sort((a, b) => a - b)[Math.min(xs.length - 1, Math.floor(p * xs.length))];
const shown = rows.map((r) => r.shownMs).filter((x) => !Number.isNaN(x));
const jev = rows.map((r) => r.jevMs);
const passed = rows.filter((r) => r.pass).length;
const summary = {
  passed,
  total: rows.length,
  accuracy: passed / rows.length,
  shownP50: pct(shown, 0.5),
  shownP90: pct(shown, 0.9),
  jevP50: pct(jev, 0.5),
  jevP90: pct(jev, 0.9),
  serverP50: pct(rows.map((r) => r.serverMs), 0.5),
};
console.log(JSON.stringify(summary));

const broke = await client.mutation(api.rooms.create, { playerId: maya.id, asks: 1 });
const spent = await client.action(api.operator.call, { roomId: broke.room.id, playerId: maya.id, transcript: "make it rain", view });
const refused: any = await client.action(api.operator.call, { roomId: broke.room.id, playerId: maya.id, transcript: "make it night", view });
const asksPass = spent.outcome === "applied" && refused.outcome === "refused" && refused.refusal?.reason === "no_asks";
console.log(`${asksPass ? "PASS" : "FAIL"} asks: 1 ask -> ${spent.outcome}, then ${refused.outcome}:${refused.refusal?.reason} "${refused.line}"`);
const out = process.argv.indexOf("--json");
if (out > 0) writeFileSync(process.argv[out + 1], JSON.stringify({ summary, rows }, null, 2));
await client.close();
