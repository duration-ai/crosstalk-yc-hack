#!/usr/bin/env node
// Speed Dial bot client: a fake racer that joins a room over the Convex race contract
// (backend/CONTRACT.md, "Speed Dial races"), drives the room's real route at race speed, fires a
// world change mid-race, finishes and prints the results. Node 18+, no dependencies.
//
//   node tools/race-bot/race-bot.mjs --join QMTX                 join a room by its code
//   node tools/race-bot/race-bot.mjs --host --min 2              create a room and print its code
//   node tools/race-bot/race-bot.mjs --join QMTX --name Rosa --fire rain --at 0.5 --speed 16
//
// Options:
//   --url <base>       deployment (default $CONVEX_SITE_URL or https://rapid-gnat-965.convex.site)
//   --join <code>      room code to join
//   --host             create a room instead (sends tools/race-bot/route.json, the game's route)
//   --min <n>          minPlayers for --host (default 2)
//   --name <name>      display name (default Bot <nn>)
//   --fire <type>      world change to fire mid-race (default rain; none to skip)
//   --at <0..1>        fraction of the route where it fires (default 0.5)
//   --speed <m/s>      cruise speed (default 15, slower in corners)
//   --lane <m>         offset from the route centre line (default by slot)
//   --races <n>        races to run, asking for a rematch between them (default 1)
//   --hz <n>           update and poll rate (default 8)
//   --code-file <path> with --host, also write the room code to this file

import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const args = process.argv.slice(2);
const opt = (name, fallback) => {
  const i = args.indexOf("--" + name);
  if (i < 0) return fallback;
  const v = args[i + 1];
  return v === undefined || v.startsWith("--") ? true : v;
};

const base = String(opt("url", process.env.CONVEX_SITE_URL || "https://rapid-gnat-965.convex.site")).replace(/\/$/, "");
const name = String(opt("name", "Bot " + Math.floor(Math.random() * 90 + 10)));
const fireType = String(opt("fire", "rain"));
const fireAt = Number(opt("at", 0.5));
const cruise = Number(opt("speed", 15));
const hz = Number(opt("hz", 8));
const races = Number(opt("races", 1));
const tag = "[" + name + "]";
const log = (...m) => console.log(new Date().toISOString().slice(11, 23), tag, ...m);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function call(method, path, body) {
  const res = await fetch(base + path, {
    method,
    headers: body ? { "content-type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  const json = await res.json().catch(() => ({}));
  if (!res.ok) {
    const e = new Error(method + " " + path + " -> " + res.status + " " + json?.error?.code + ": " + json?.error?.message);
    e.code = json?.error?.code;
    throw e;
  }
  return json;
}

const PARAMS = {
  rain: { level: "on" },
  snow: { level: "on" },
  fog: { level: "thick" },
  gravity: { level: "low" },
  ice: { level: "on" },
  time: { timeOfDay: "night" },
  traffic: { level: "heavy" },
  recolor: { color: "yellow" },
  resize: { size: "small" },
  ramp: { count: 3 },
};

// Route geometry: cumulative lengths so the bot can sit at any distance along the polyline.
function makePath(route) {
  const pts = route.map((p) => ({ x: p.x, y: p.y, z: p.z }));
  const cum = [0];
  for (let i = 1; i < pts.length; i++) cum.push(cum[i - 1] + Math.hypot(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z));
  const length = cum[cum.length - 1];
  const seg = (d) => {
    d = Math.max(0, Math.min(length, d));
    let i = 1;
    while (i < pts.length - 1 && cum[i] < d) i++;
    return { i, t: (d - cum[i - 1]) / Math.max(1e-6, cum[i] - cum[i - 1]) };
  };
  const dirAt = (d) => {
    const { i } = seg(d);
    const a = pts[i - 1], b = pts[i];
    const l = Math.hypot(b.x - a.x, b.z - a.z) || 1;
    return { x: (b.x - a.x) / l, z: (b.z - a.z) / l };
  };
  const at = (d, lane) => {
    const { i, t } = seg(d);
    const a = pts[i - 1], b = pts[i];
    const dir = dirAt(d);
    return { x: a.x + (b.x - a.x) * t + dir.z * lane, y: a.y + (b.y - a.y) * t + 0.3, z: a.z + (b.z - a.z) * t - dir.x * lane };
  };
  // How sharp the route turns over the next 25 m: 0 straight, 1 a right angle or worse.
  const bend = (d) => {
    const a = dirAt(d), b = dirAt(d + 25);
    return Math.min(1, Math.acos(Math.max(-1, Math.min(1, a.x * b.x + a.z * b.z))) / (Math.PI / 2));
  };
  return { length, at, dirAt, bend };
}

const heading = (dir) => ((Math.atan2(dir.x, dir.z) * 180) / Math.PI + 360) % 360;

async function main() {
  const { player } = await call("POST", "/api/players", { displayName: name });
  log("player " + player.id + " on " + base);

  let joined;
  if (opt("host", false)) {
    const here = dirname(fileURLToPath(import.meta.url));
    const route = JSON.parse(readFileSync(join(here, "route.json"), "utf8")).route;
    joined = await call("POST", "/api/rooms", { playerId: player.id, route, minPlayers: Number(opt("min", 2)), finishRadius: 15 });
    log("created room " + joined.room.code + " (" + joined.room.id + "), route " + joined.room.routeLength.toFixed(0) + " m");
    const file = opt("code-file", null);
    if (file && file !== true) writeFileSync(String(file), joined.room.code + "\n");
  } else {
    const code = opt("join", null);
    if (!code || code === true) throw new Error("pass --join <code> or --host");
    joined = await call("POST", "/api/rooms/join", { playerId: player.id, code: String(code) });
    log("joined room " + joined.room.code + " as " + joined.you.color + " (slot " + joined.you.slot + "), route " + joined.room.routeLength.toFixed(0) + " m");
  }

  const roomId = joined.room.id;
  const me = player.id;
  const path = makePath(joined.route);
  const slot = joined.you.slot;
  const lane = Number(opt("lane", slot % 2 === 0 ? -1.9 : 1.9));
  let cursor = null;
  let raceNumber = joined.room.raceNumber;
  let raced = 0;

  let leaving = false;
  process.on("SIGINT", async () => {
    if (leaving) process.exit(1);
    leaving = true;
    await call("POST", "/api/rooms/leave", { playerId: me, roomId }).catch(() => {});
    log("left the room");
    process.exit(0);
  });

  while (raced < races) {
    let progress = Math.max(0, 6 - slot * 4);
    let speed = 0;
    let fired = fireType === "none";
    let readied = false;
    let gone = false;
    let finished = false;
    let resultsShown = false;
    let lastStandings = 0;
    let last = Date.now();
    while (true) {
      const t0 = Date.now();
      const dt = Math.min(0.5, (t0 - last) / 1000);
      last = t0;
      const poll = await call("GET", "/api/rooms/poll?roomId=" + roomId + (cursor === null ? "" : "&after=" + cursor));
      cursor = poll.cursor;
      for (const e of poll.events) log("event #" + e.seq + " " + e.type + " " + JSON.stringify(e.params) + ": " + e.news);
      const room = poll.room;
      if (room.raceNumber !== raceNumber) {
        raceNumber = room.raceNumber;
        cursor = null;
        log("rematch: race " + raceNumber);
        break;
      }
      const others = poll.racers.filter((r) => r.playerId !== me && !r.left).length;
      if (!readied && room.status === "lobby" && (others > 0 || room.minPlayers <= 1)) {
        readied = true;
        await sleep(400 + slot * 300);
        await call("POST", "/api/rooms/ready", { playerId: me, roomId, ready: true }).catch((e) => log("ready: " + e.message));
        log("ready");
      }
      if (room.status === "racing" && !gone) {
        gone = true;
        log("GO");
      }
      if (gone && !finished && room.status === "racing") {
        const target = cruise * (1 - 0.55 * path.bend(progress));
        speed += Math.max(-9 * dt, Math.min(5 * dt, target - speed));
        progress = Math.min(path.length, progress + speed * dt);
        if (!fired && progress >= path.length * fireAt) {
          fired = true;
          try {
            const { event } = await call("POST", "/api/rooms/events", { playerId: me, roomId, type: fireType, params: PARAMS[fireType] ?? {} });
            log("fired " + fireType + ": #" + event.seq + " " + event.news);
          } catch (e) {
            log("fire refused: " + e.message);
          }
        }
        if (Date.now() - lastStandings > 3000) {
          lastStandings = Date.now();
          log("standings " + poll.racers.map((r) => r.place + "." + r.displayName + " " + r.progress.toFixed(0) + "m").join("  "));
        }
      }
      if (room.status !== "finished") {
        const up = await call("POST", "/api/race/update", { playerId: me, roomId, position: path.at(progress, lane), heading: heading(path.dirAt(progress)) });
        if (up.finished && !finished) {
          finished = true;
          log("finished in " + (up.finishMs / 1000).toFixed(1) + " s");
        }
      }
      if (room.status === "finished" && poll.results.length && !resultsShown) {
        resultsShown = true;
        log("results:");
        for (const r of poll.results) log("  " + r.place + ". " + r.displayName.padEnd(12) + " " + (r.finished ? (r.finishMs / 1000).toFixed(1) + " s" : "DNF") + "  " + r.award + " (" + r.calls + " calls)");
        raced++;
        if (raced >= races) break;
        await sleep(3000);
        await call("POST", "/api/rooms/ready", { playerId: me, roomId, ready: true }).catch((e) => log("rematch: " + e.message));
        log("asked for a rematch");
      }
      await sleep(Math.max(0, 1000 / hz - (Date.now() - t0)));
    }
  }
  await call("POST", "/api/rooms/leave", { playerId: me, roomId }).catch(() => {});
  log("done, left the room");
}

main().catch((e) => {
  console.error(tag, e.message);
  process.exit(1);
});
