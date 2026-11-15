/**
 * End-to-end voice test: speaks each phrase with macOS \`say\`, streams it to ElevenLabs Scribe realtime
 * through a token from operator.mintScribeToken (as the Unity client does), commits on "release", sends the
 * transcript to operator.call and times release → transcript → effect visible in the activeEffects subscription.
 *
 *   CONVEX_URL=https://<deployment>.convex.cloud npx tsx scripts/live-voice.mts [--pad 300] [--json out.json]
 * Needs macOS (say) and ffmpeg.
 */
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { ConvexClient } from "convex/browser";
import { api } from "../convex/_generated/api.js";

const arg = (name: string, fallback: string) => {
  const i = process.argv.indexOf(name);
  return i > 0 ? process.argv[i + 1] : fallback;
};
const PAD_MS = Number(arg("--pad", "0"));
const RATE = 16000;
const CHUNK_MS = 100;
const PREROLL_MS = 500;

const PHRASES: [string, string][] = [
  ["make it rain", "rain"],
  ["super speed", "vehicle_faster"],
  ["nitro", "nitro"],
  ["low gravity", "gravity_lower"],
  ["make it night", "night"],
  ["big heads", "big_heads"],
  ["launch that bus", "launch"],
  ["make that Waymo pink", "recolor"],
  ["set the Zoox on fire", "set_fire"],
  ["earthquake", "earthquake"],
  ["let us fly", "flight"],
  ["make it snow", "snow"],
  ["the floor is lava", "floor_is_lava"],
  ["stop that jogger", "stop"],
  ["slow motion", "slow_motion"],
  ["rush hour", "more_traffic"],
];

const url = process.env.CONVEX_URL;
if (!url) throw new Error("Set CONVEX_URL");
const client = new ConvexClient(url);
const tim = await client.mutation(api.players.create, { displayName: "Voice Tim" });
const { room } = await client.mutation(api.rooms.create, { playerId: tim.id });

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
    e("veh_14", "zoox", 2, "Zoox robotaxi", "grey"),
    e("bus_2", "bus", 3, "Muni bus", "red"),
    e("ped_9", "pedestrian", 4, "jogger", "yellow"),
    e("bld_yc", "building", 5, "YC office", "grey", { protected: true }),
  ],
};

let latest: { effects: { id: string; endsAt: number }[] } = { effects: [] };
const waiters: { id: string; endsAt: number; resolve: (t: number) => void }[] = [];
client.onUpdate(api.operator.activeEffects, { roomId: room.id }, (value) => {
  latest = value;
  for (let i = waiters.length - 1; i >= 0; i--) {
    if (value.effects.some((x) => x.id === waiters[i].id && x.endsAt >= waiters[i].endsAt)) {
      waiters[i].resolve(performance.now());
      waiters.splice(i, 1);
    }
  }
});
const seen = (id: string, endsAt: number) =>
  latest.effects.some((x) => x.id === id && x.endsAt >= endsAt)
    ? Promise.resolve(performance.now())
    : new Promise<number>((resolve) => {
        waiters.push({ id, endsAt, resolve });
        setTimeout(() => resolve(NaN), 5000);
      });

const dir = mkdtempSync(join(tmpdir(), "voice-"));
function speak(phrase: string) {
  const aiff = join(dir, "p.aiff");
  const pcm = join(dir, "p.pcm");
  execFileSync("say", ["-r", "200", "-o", aiff, phrase]);
  execFileSync("ffmpeg", ["-y", "-loglevel", "error", "-i", aiff, "-ar", String(RATE), "-ac", "1", "-f", "s16le", pcm]);
  return readFileSync(pcm);
}

const { token } = await client.action(api.operator.mintScribeToken, { playerId: tim.id });
const params = new URLSearchParams({
  model_id: "scribe_v2_realtime",
  token,
  audio_format: "pcm_16000",
  commit_strategy: "manual",
  language_code: "en",
});
for (const term of ["Waymo", "Zoox", "Lyft", "YC", "nitro"]) params.append("keyterms", term);
const ws = new WebSocket(`wss://api.elevenlabs.io/v1/speech-to-text/realtime?${params}`);
let onMessage: (m: any) => void = () => {};
ws.onmessage = (event) => onMessage(JSON.parse(String(event.data)));
ws.onerror = (event) => console.error("socket error", event);
await new Promise<void>((resolve, reject) => {
  onMessage = (m) => {
    if (m.message_type === "session_started") resolve();
    else reject(new Error(JSON.stringify(m)));
  };
});

const send = (pcm: Buffer, commit = false) =>
  ws.send(JSON.stringify({ message_type: "input_audio_chunk", audio_base_64: pcm.toString("base64"), commit, sample_rate: RATE }));
const silence = (ms: number) => Buffer.alloc(Math.round((RATE * ms) / 1000) * 2);
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
const bytesPerChunk = (RATE * CHUNK_MS) / 1000 * 2;

const rows = [];
for (const [phrase, want] of PHRASES) {
  const audio = speak(phrase);
  let partial = "";
  let committed: string | null = null;
  let committedAt = 0;
  onMessage = (m) => {
    if (m.message_type === "partial_transcript") partial = m.text;
    else if (m.message_type === "committed_transcript") {
      committed = m.text;
      committedAt = performance.now();
    } else if (m.message_type !== "committed_transcript_with_timestamps") console.error("scribe:", JSON.stringify(m));
  };
  send(silence(PREROLL_MS));
  for (let i = 0; i < audio.length; i += bytesPerChunk) {
    send(audio.subarray(i, i + bytesPerChunk));
    await sleep(CHUNK_MS);
  }
  const release = performance.now();
  send(silence(Math.max(PAD_MS, 10)), true);
  while (committed === null && performance.now() - release < 1500) await sleep(5);
  const transcript = (committed ?? partial).trim();
  const sttMs = Math.round((committed === null ? performance.now() : committedAt) - release);
  let got = "no transcript";
  let shownMs = NaN;
  let callMs = NaN;
  if (transcript) {
    const t0 = performance.now();
    const result: any = await client.action(api.operator.call, {
      roomId: room.id, playerId: tim.id, transcript, view, clientTimings: { sttMs },
    });
    callMs = Math.round(performance.now() - t0);
    got = result.outcome === "refused" ? `refused:${result.refusal.reason}` : `${result.outcome}:${result.effect.effect}`;
    const shown = result.outcome === "applied" ? await seen(result.effect.id, result.effect.endsAt) : performance.now();
    shownMs = Math.round(shown - release);
  }
  const pass = got === `applied:${want}`;
  rows.push({ phrase, transcript, source: committed === null ? "partial" : "committed", audioMs: Math.round((audio.length / 2 / RATE) * 1000), sttMs, callMs, shownMs, got, pass });
  console.log(`${pass ? "PASS" : "FAIL"} ${String(shownMs).padStart(5)} ms (stt ${String(sttMs).padStart(4)}, call ${String(callMs).padStart(4)})  ${phrase.padEnd(22)} → "${transcript}" → ${got}`);
  await sleep(300);
}
ws.close();

const pct = (xs: number[], p: number) => [...xs].sort((a, b) => a - b)[Math.min(xs.length - 1, Math.floor(p * xs.length))];
const ok = (xs: number[]) => xs.filter((x) => !Number.isNaN(x));
const summary = {
  padMs: PAD_MS,
  passed: rows.filter((r) => r.pass).length,
  total: rows.length,
  committed: rows.filter((r) => r.source === "committed").length,
  releaseToEffectP50: pct(ok(rows.map((r) => r.shownMs)), 0.5),
  releaseToEffectP90: pct(ok(rows.map((r) => r.shownMs)), 0.9),
  sttP50: pct(rows.map((r) => r.sttMs), 0.5),
  callP50: pct(ok(rows.map((r) => r.callMs)), 0.5),
};
console.log(JSON.stringify(summary));
const out = process.argv.indexOf("--json");
if (out > 0) writeFileSync(process.argv[out + 1], JSON.stringify({ summary, rows }, null, 2));
await client.close();
