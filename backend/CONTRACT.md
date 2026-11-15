# Dogpatch backend HTTP contract

This is the API the Unity client's `IGameBackend` will call when it moves from the local in-memory implementation to Convex, plus the Speed Dial race API for `IRaceSession` and `IOperator` ([Speed Dial races](#speed-dial-races)). Unity has no official Convex SDK, so everything is plain HTTP and JSON against the deployment's HTTP actions URL.

## Basics

- **Base URL**: the deployment's HTTP actions URL, `https://<deployment-name>.convex.site` (note `.site`, not `.cloud`). Locally, `npx convex dev` prints it as `CONVEX_SITE_URL` (usually `http://127.0.0.1:3211`).
- **Requests**: `GET` takes query parameters. `POST` and `PUT` take a JSON object body; send `Content-Type: application/json`.
- **Responses**: always a JSON object (never a top-level array, so Unity's `JsonUtility` can parse every response into a class).
- **Auth**: none yet. A `playerId` is trusted as sent; keep it on the device. Auth providers are out of scope for this scaffold.
- **IDs** are opaque strings minted by Convex. Store and send them back unchanged.
- **Timestamps** (`createdAt`, `updatedAt`) are Unix epoch milliseconds set by the server.
- **Units**: `position` is Unity world space in meters (`{ "x", "y", "z" }`, y up). `heading` is the yaw in degrees around +Y, as in `transform.eulerAngles.y`. The server stores both as given and does not normalize them.
- **Null-ish vehicle ids**: in requests, `vehicleId` may be omitted, `null`, or `""` to mean "no vehicle" (`JsonUtility` writes `""` for a null string). In responses, "no vehicle" is always `null`, which `JsonUtility` reads back as `""`.

## Objects

```jsonc
// Player
{ "id": "jd7…", "displayName": "Tim", "createdAt": 1794768818288 }

// PlayerState: the player's save. One per player, overwritten on every save.
{
  "playerId": "jd7…",
  "position": { "x": 120, "y": 0.5, "z": -40 },
  "heading": 90,
  "mode": "onFoot",          // "onFoot" | "driving"
  "vehicleId": null,         // the vehicle id when mode is "driving", else null
  "updatedAt": 1794768818366
}

// Vehicle
{
  "id": "j97…",
  "model": "hatchback",      // free-form model key the client maps to a prefab
  "position": { "x": 120, "y": 0.5, "z": -40 },
  "heading": 90,
  "occupiedBy": null,        // the driver's player id, or null when parked
  "updatedAt": 1794768818166
}
```

There is no separate saves table: `PlayerState` is the save, and a parked vehicle's `position` and `heading` persist on the vehicle itself.

## Errors

Every failure returns this body with the status below:

```json
{ "error": { "code": "NOT_FOUND", "message": "Player jd7… not found" } }
```

| Status | `code` | When |
| --- | --- | --- |
| 400 | `INVALID_ARGUMENT` | The body is not a JSON object, a field is missing or has the wrong type, or a value breaks a rule (for example an empty `displayName`). |
| 404 | `NOT_FOUND` | The `playerId`, `vehicleId`, `roomId` or room `code` does not exist (including malformed ids). |
| 409 | `CONFLICT` | The request contradicts current state: the vehicle is taken, the player is in another vehicle, or the player is not the driver; the room is full, started, finished or in its countdown lock, or the player is not in it. |
| 429 | `RATE_LIMITED` | More than 10 Scribe tokens a minute for one player. |
| 503 | `UNAVAILABLE` | The speech service could not mint a token, or its key is not set. |
| 500 | `INTERNAL` | Anything unexpected. The message is always `Internal error`. |

`message` is for logs, not for branching; branch on `code` and the status. A path or method that matches no route gets Convex's plain-text `404 No matching routes found` instead of the JSON body.

## Endpoints

| IGameBackend operation | Method and path | Convex function |
| --- | --- | --- |
| Create player | `POST /api/players` | `players.create` |
| Get player | `GET /api/players?id=…` | `players.get` |
| Load player state | `GET /api/player-state?playerId=…` | `playerState.load` |
| Save player state | `PUT /api/player-state` | `playerState.save` |
| List vehicles | `GET /api/vehicles` | `vehicles.list` |
| Enter vehicle | `POST /api/vehicles/enter` | `vehicles.enter` |
| Exit vehicle | `POST /api/vehicles/exit` | `vehicles.exit` |
| Update vehicle state | `POST /api/vehicles/update` | `vehicles.update` |

### Create player: `POST /api/players`

Request:

```json
{ "displayName": "Tim" }
```

`displayName` is trimmed and must be 1 to 32 characters. Response `201`:

```json
{ "player": { "id": "jd7…", "displayName": "Tim", "createdAt": 1794768818288 } }
```

Errors: `400`.

### Get player: `GET /api/players?id=<playerId>`

Response `200`: `{ "player": Player }`. Errors: `404` (unknown id), `400` (missing `id`).

### Load player state: `GET /api/player-state?playerId=<playerId>`

Response `200`: `{ "state": PlayerState }`, or `{ "state": null }` when the player has never saved, in which case the client spawns at its default point. Errors: `404` (unknown player), `400`.

### Save player state: `PUT /api/player-state`

Upserts the player's state. Use it for on-foot movement and periodic checkpoints. Mode changes go through enter and exit, so `mode` here must match what the server already knows:

```json
{
  "playerId": "jd7…",
  "position": { "x": 0, "y": 0, "z": 0 },
  "heading": 10,
  "mode": "onFoot",
  "vehicleId": ""
}
```

- `mode: "onFoot"` requires no `vehicleId` and that the player is not driving (else `409`).
- `mode: "driving"` requires the `vehicleId` the player has entered (else `409`; missing `vehicleId` is `400`). The vehicle moves to the saved `position` and `heading` in the same write, so a driving save and `POST /api/vehicles/update` leave the same state.

Response `200`: `{ "state": PlayerState }`. Errors: `400`, `404`, `409`.

### List vehicles: `GET /api/vehicles`

Response `200`:

```json
{ "vehicles": [ Vehicle, … ] }
```

Returns every vehicle, parked or driven. Vehicles are created by the `vehicles:seed` internal function (see the README), not over HTTP.

### Enter vehicle: `POST /api/vehicles/enter`

```json
{ "playerId": "jd7…", "vehicleId": "j97…" }
```

Marks the player as the vehicle's driver and moves the player state to `mode: "driving"` at the vehicle's position and heading. Entering the vehicle you already drive is a no-op that succeeds. Response `200`:

```json
{ "vehicle": Vehicle, "state": PlayerState }
```

Errors: `404`; `409` when another player occupies the vehicle or this player is already in a different one.

### Exit vehicle: `POST /api/vehicles/exit`

```json
{
  "playerId": "jd7…",
  "vehicleId": "j97…",
  "position": { "x": 150, "y": 0, "z": -38 },
  "heading": 180
}
```

`position` and `heading` are optional and set where the player stands after getting out; each defaults to the vehicle's. The vehicle stays parked where it was last updated. Response `200`: `{ "vehicle": Vehicle, "state": PlayerState }` with `occupiedBy: null` and `mode: "onFoot"`.

Errors: `404`; `409` when the player is not in this vehicle.

### Update vehicle state: `POST /api/vehicles/update`

```json
{
  "playerId": "jd7…",
  "vehicleId": "j97…",
  "position": { "x": 150, "y": 0.5, "z": -40 },
  "heading": 180
}
```

Only the driver can move a vehicle. The same call moves the driver's player state, so while driving the client sends only this, not a separate state save. Response `200`: `{ "vehicle": Vehicle, "state": PlayerState }`.

Errors: `404`; `409` when the player is not the driver.

## Client notes

- Call rate: every call is a Convex mutation or query. Send driving updates at a checkpoint cadence (for example every few seconds and on exit), not every frame.
- Ordering: the server applies writes in arrival order. Await each call before sending the next for the same player.
- Multiplayer sync is by polling (see Speed Dial races below). Realtime subscriptions and auth are out of scope; subscriptions would need a Convex client or WebSocket.

## Speed Dial races

Four-player races through Dogpatch: rooms and a lobby, race state at 5–10 Hz, server-side places and results, an append-only log of world changes, and the Operator. Everything above (base URL, JSON bodies, no auth, opaque ids, epoch-millisecond server times, Unity world-space meters, the error body) applies here too.

Players come from `POST /api/players`. A player is in at most one room: creating or joining a room first leaves any other room the player is in (with the same rules as an explicit leave, except that the countdown lock does not apply).

### JsonUtility rules

- No top-level arrays and no polymorphic objects.
- **Numbers are never null.** A time that is not set yet is `0` (`startsAt: 0` in the lobby, `finishMs: 0` before finishing).
- Lists are always present, possibly empty (`events: []`, `results: []`).
- World-change `params` is one flat object. One C# class with every field (`level`, `timeOfDay`, `color`, `hex`, `playerId`, `targetPlayerId`, `size`, `count`) reads all of them, and the client may send that class whole: fields a type does not use are ignored, an empty string or `0` counts as unset, and `hex` and swap's `playerId` are always filled in by the server.

### Objects

```jsonc
// Room
{
  "id": "k57…",
  "code": "QMTX",            // 4 letters (no I or O), what players type to join; case-insensitive
  "status": "lobby",         // "lobby" | "countdown" | "racing" | "finished", always current to serverTime
  "raceNumber": 1,           // increments on rematch; reset world changes when it changes
  "minPlayers": 2,           // players needed before the room can start (1–4)
  "playerCount": 3,          // racers in the room who have not left
  "readyCount": 2,
  "startsAt": 0,             // server time of GO, 0 when no countdown is running
  "closesAt": 0,             // server time the race ends for stragglers (first finish + 60 s), 0 until someone finishes
  "routeLength": 700,        // meters along the route
  "finish": { "x": 300, "y": 0, "z": 400 },   // the route's last point
  "finishRadius": 10,
  "createdAt": 1794768818288
}

// Racer: one per player in the room
{
  "playerId": "jd7…",
  "displayName": "Maya",
  "color": "purple",         // "blue" | "purple" | "orange" | "green", by join order, never shared
  "colorHex": "#A35BFF",
  "slot": 1,                 // 0–3, the start-ring slot; same index as the color
  "ready": true,
  "position": { "x": 0, "y": 0.5, "z": 140 },
  "heading": 0,
  "progress": 140,           // meters along the route, computed by the server from position
  "metersToFinish": 560,     // routeLength - progress, for the HUD's "640 M"
  "place": 2,                // 1–4, computed by the server (see Places, finish and results)
  "finished": false,
  "finishMs": 0,             // race time in ms from startsAt, 0 until finished
  "smash": 0,                // smash points this race, the best value the racer has sent
  "left": false,             // true if the player left after the start (DNF)
  "updatedAt": 1794768818366
}

// Event: one world change in the room's append-only log
{
  "seq": 7,                  // per room, strictly increasing from 1 across races; apply in seq order
  "raceNumber": 1,
  "type": "gravity",
  "params": { "level": "low" },
  "playerId": "jd7…",        // who fired it
  "displayName": "Maya",
  "color": "purple",         // for the color block at the start of the news bar
  "serverTime": 1794768830000,
  "news": "MAYA LOWERED GRAVITY"   // uppercase, at most four words, ready for the news bar
}

// Result: one per racer once the room is finished, in place order
{
  "place": 1,
  "playerId": "jd7…",
  "displayName": "Maya",
  "color": "purple",
  "finished": true,
  "finishMs": 74210,
  "calls": 3,                // world changes this racer fired this race
  "smash": 12,               // smash points this race
  "award": "CHAOS CALLER"    // one award per racer
}
```

### World changes

| `type` | `params` | Example news |
| --- | --- | --- |
| `gravity` | `{ "level": "low" \| "high" \| "normal" }` | MAYA LOWERED GRAVITY |
| `ice` | `{ "level": "on" \| "off" }` | JUN ICED THE ROADS |
| `fog` | `{ "level": "thick" \| "clear" }` | ROSA CALLED THE FOG |
| `time` | `{ "timeOfDay": "day" \| "golden" \| "dusk" \| "night" }` | TIM MADE IT NIGHT |
| `traffic` | `{ "level": "heavy" \| "light" }` | JUN CALLED IN TRAFFIC |
| `recolor` | `{ "color": "yellow", "hex": "#FFCB1F" }` | JUN PAINTED CARS YELLOW |
| `swap` | `{ "playerId": "<firer>", "targetPlayerId": "<racer>" }` | ROSA SWAPPED WITH TIM |
| `resize` | `{ "size": "small" \| "big" \| "normal" }` | ROSA SHRANK ALL CARS |
| `ramp` | `{ "count": 1–5 }` (default 3) | TIM DROPPED RAMPS |
| `rain` | `{ "level": "on" \| "off" }` | MAYA MADE IT RAIN / MAYA STOPPED THE RAIN |
| `snow` | `{ "level": "on" \| "off" }` | MAYA MADE IT SNOW / MAYA STOPPED THE SNOW |

- **Recolor colors:** yellow #FFCB1F, pink #FF3E8E, blue #2D8CFF, purple #A35BFF, orange #FF8A1E, green #2EC27E, red #E2603F, white #F7F8F9, black #1C2347, mint #8CE3BF, lilac #BBA6F7. Send `color` only; the server fills `hex`.
- **Swap:** send `targetPlayerId`, another racer still in the room; the server sets `playerId` to whoever fired it. Each client moves the two cars; the server picks up the new positions from each racer's next `race/update`.
- **Effects are the client's call.** How low gravity goes and how long fog lasts are decided in Unity. The server only validates, orders and records the changes.
- The news names the firer by the first word of their display name, uppercased and cut to 12 characters.

### Lobby and countdown

- Colors and slots go by join order: blue, purple, orange, green. A leaver's color goes to the next player who joins.
- When **every** player in the room is ready and there are at least `minPlayers`, `status` becomes `countdown` with `startsAt` at most **3 s** away.
- When at least `max(2, minPlayers)` players are ready but not all, the **auto-start** countdown runs: `startsAt = now + 10 s`, set once. It only moves when everyone becomes ready, which shortens it to 3 s.
- If un-readies or leaves drop the ready count below that, the countdown is cancelled (`status: "lobby"`, `startsAt: 0`).
- The last 3 s are **locked**: joins, leaves and un-readies get `409`. The race starts at `startsAt` with everyone in the room, ready or not.
- `status` turns `racing` at `startsAt` on its own; no call is needed. Correct for clock skew with the poll's `serverTime`: `localGo = startsAt - serverTime + localTimeWhenTheResponseArrived`.

### Places, finish and results

- `progress` is the racer's position projected onto the room's route polyline (x and z only), in meters from the start.
- **Place order:** finished racers first by `finishMs`, then racers still driving by `progress` (highest first), then racers who left. Ties go to the lower slot.
- A racer **finishes** on the first `race/update` during `racing` whose position is within `finishRadius` meters (x/z) of the route's last point. `finishMs` is that update's server time minus `startsAt`. Updates before the start never finish anyone.
- The room is **finished** when every racer still in it has finished, or at `closesAt` (60 s after the first finish), whichever is first. Anyone still driving is then a DNF (`finished: false`).
- **Awards**, one per racer, the first rule that applies:
  1. **CHAOS CALLER**: the racer with the most calls this race (at least one); a tie goes to the better place.
  2. **WRECKING BALL**: the racer with the most smash points this race (at least 1); a tie goes to the better place.
  3. **PHOTO FINISH**: finished within 1 s of the racer just ahead or just behind.
  4. By place: 1st **FIRST TO YC**, 2nd **SO CLOSE**, 3rd **PODIUM FINISH**, 4th **SCENIC ROUTE**.
  5. DNF: **STILL DRIVING**.

### Race endpoints

| Operation | Method and path | Convex function |
| --- | --- | --- |
| Create room | `POST /api/rooms` | `rooms.create` |
| Join room | `POST /api/rooms/join` | `rooms.join` |
| Leave room | `POST /api/rooms/leave` | `rooms.leave` |
| Set ready, rematch | `POST /api/rooms/ready` | `rooms.setReady` |
| Poll everything | `GET /api/rooms/poll?roomId=…&after=…` | `rooms.poll` (internal) |
| Send my position | `POST /api/race/update` | `race.update` |
| Fire a world change | `POST /api/rooms/events` | `events.fire` |
| Call the Operator (text) | `POST /api/operator` | `operator.call` (action) |

A typical client loop: create or join, then poll at 5–10 Hz from the lobby onward; send `ready` from the Ready button; once `status` is `countdown` or `racing`, also send `race/update` at 5–10 Hz; apply `events` in `seq` order as they arrive; show `results` when `status` is `finished`.

#### Create room: `POST /api/rooms`

```jsonc
{
  "playerId": "jd7…",
  "route": [ { "x": 0, "y": 0, "z": 0 }, { "x": 0, "y": 0, "z": 400 }, { "x": 300, "y": 0, "z": 400 } ],
  "finishRadius": 10,   // optional, 1–100, default 15
  "minPlayers": 2       // optional, integer 1–4, default 2 (1 lets one player test alone)
}
```

`route` is optional, 2–512 points with a nonzero length: the start ring first, the YC finish last. The client owns the map, so it should always send its route (the street path from the start ring to YC). Without one the server uses a placeholder straight line from (0, 0, 0) to (0, 0, 800).

Response `201`: `{ "room": Room, "route": [ Vec3, … ], "you": Racer }`. The creator is in the room as blue, slot 0, not ready. Errors: `400`, `404` (player).

#### Join room: `POST /api/rooms/join`

```json
{ "playerId": "jd7…", "code": "qmtx" }
```

Response `200`: `{ "room": Room, "route": [ Vec3, … ], "you": Racer }`. Joining a room you are already in returns the same thing. Errors: `404` (player or code), `409` (room full, race started or finished, countdown locked).

#### Leave room: `POST /api/rooms/leave`

```json
{ "playerId": "jd7…", "roomId": "k57…" }
```

Response `200`: `{ "left": true }`. In the lobby or countdown the racer is removed. After the start the racer stays in the standings as `left: true` (DNF) and is removed at the rematch. Leaving is idempotent: leaving again, or leaving a room you are not in, succeeds, so a lost response can be retried. Errors: `404`, `409` (countdown locked).

#### Set ready: `POST /api/rooms/ready`

```json
{ "playerId": "jd7…", "roomId": "k57…", "ready": true }
```

Response `200`: `{ "room": Room }` with the countdown already updated. Sending the flag the player already has is a no-op that succeeds, even while locked.

During `finished`, `ready: true` is the **rematch**: the room goes back to `lobby` with `raceNumber + 1`, everyone's ready flag, progress and finish cleared, leavers removed, and then this player marked ready (so a solo room starts its countdown at once). Errors: `404`, `409` (not in the room, left, race running, countdown locked).

#### Poll: `GET /api/rooms/poll?roomId=<id>&after=<seq>`

The one call to make at 5–10 Hz, in the lobby and in the race. `after` is the last `cursor` you got. Omit it on the first poll, after a rejoin, or when `raceNumber` changes, to get every event of the current race so far.

```jsonc
{
  "serverTime": 1794768830123,
  "room": Room,
  "racers": [ Racer, … ],   // in place order, place 1 first
  "events": [ Event, … ],   // seq > after, oldest first, at most 100
  "cursor": 7,              // pass back as after; unchanged when nothing is new
  "results": [ Result, … ]  // empty until room.status is "finished"
}
```

If `events` has 100 entries, poll again straight away. A typical four-racer response is about 1.5 KB. Errors: `400` (`after` is not a non-negative integer), `404` (room).

#### Send my position: `POST /api/race/update`

```json
{ "playerId": "jd7…", "roomId": "k57…", "position": { "x": 0, "y": 0.5, "z": 140 }, "heading": 0, "smash": 12 }
```

`smash` is optional: the racer's cumulative smash points this race (breaks, barges, dives, wrecks, combos), a non-negative integer. The server keeps the highest value sent, so a missing value, `0` or a lower number changes nothing. It counts only while `status` is `racing` and is frozen once the room is finished; the rematch resets it to 0.

Send at 5–10 Hz from the lobby onward and await each call before sending the next. The response is small on purpose; places come from the poll.

```jsonc
{ "status": "racing", "progress": 140, "metersToFinish": 560, "finished": false, "finishMs": 0 }
```

The update that reaches the finish returns `finished: true` and its `finishMs`, and the last finisher's update already returns `status: "finished"`. After finishing, or once the room is finished, updates still store the position but never change progress or the result. Errors: `400` (including a negative or fractional `smash`), `404`, `409` (not in the room, or left).

#### Fire a world change: `POST /api/rooms/events`

For world changes that do not go through the Operator: a debug menu, pickups, bots a client runs.

```json
{ "playerId": "jd7…", "roomId": "k57…", "type": "gravity", "params": { "level": "low" } }
```

`params` may be omitted for `ramp`. Response `201`: `{ "event": Event }`. World changes are accepted in the lobby, countdown and race. Errors: `400` (unknown type, bad or unknown params field, swap target not in the room or yourself), `404`, `409` (not in the room, left, room finished).

#### Call the Operator: `POST /api/operator`

The same as the `operator:call` Convex function in [The voice Operator](#the-voice-operator), for clients without a Convex client:

```json
{ "playerId": "jd7…", "roomId": "k57…", "text": "make it rain", "view": View }
```

`text` (or `transcript`) is 1–300 characters after trimming. `view` is optional; without it the Operator sees an on-foot player with nothing in view, so object requests are refused as `not_visible`. The response is a `CallResult`. The Operator no longer writes to the world-change log: its effects live in `activeEffects`. `POST /api/operator/audio` is gone, because speech-to-text runs on the device through Scribe. Errors: `400`, `404`, `409` (not in the room, left).

### The voice Operator

While a player holds the Call plate, the client streams the microphone to ElevenLabs Scribe and shows partial transcripts. On release it sends the final transcript and a snapshot of what the player sees to `operator:call`. The action asks `typesafe-ai/jev` 16 typed questions in one decision, and the routing rules in `convex/operatorRules.ts` turn the answers into exactly one effect or one refusal. Effects are rows in `activeEffects`. Every client subscribes to `operator:activeEffects` and applies them. Keys live only in the deployment's env vars: `AI_GATEWAY_API_KEY` for Jev and `ELEVENLABS_API_KEY` for Scribe tokens.

Call these as Convex functions, through a Convex client or the HTTP API on the deployment's `.convex.cloud` URL (`POST /api/action`, `/api/mutation` and `/api/query` with `{ "path": "operator:call", "args": {…}, "format": "json" }`):

| Function | Kind | Args | Returns |
| --- | --- | --- | --- |
| `operator:mintScribeToken` | action | `{ playerId }` | `{ token, serverNow }`, a single-use Scribe realtime token. At most 10 a minute per player (`RATE_LIMITED`). |
| `operator:call` | action | `{ roomId, playerId, transcript, view, clientTimings? }` | `CallResult` |
| `operator:confirm` | mutation | `{ playerId, effectId }` | `ActiveEffect`, now active. Only the requester, within 10 s of the call (`CONFLICT` otherwise). |
| `operator:activeEffects` | query (reactive) | `{ roomId }` | `{ effects: ActiveEffect[] }`, only active rows |

```jsonc
// View: taken when the Call plate is pressed. At most 8 entities, most prominent first.
{
  "mode": "driving",                 // "onFoot" | "driving"
  "timeOfDay": "day",
  "weather": "clear",
  "entities": [
    {
      "id": "veh_12",                // stable and identical on every client; the caller's car is "veh_me", other racers' cars "car:<playerId>"
      "class": "waymo",              // leaf class: waymo, zoox, car, bus, bike, pedestrian, delivery_rider, tree, plant, building, streetlight, billboard
      "desc": "Waymo robotaxi",
      "color": "white",
      "screen": "centre, large",
      "distance_m": 18,
      "view_rank": 1,
      "protected": false,            // the YC building is protected
      "controlledBy": ""             // "me" | "player" | ""
    }
  ]
}

// CallResult
{
  "outcome": "applied",              // "applied" | "pending" (needs operator:confirm) | "refused"
  "effect": ActiveEffect,            // null when refused
  "refusal": null,                   // { "reason": "protected", "message": "That landmark's protected." }
  "line": "Rain!",                   // four words at most, for the Operator's pink bar
  "serverNow": 1794772800000,
  "timings": { "jevMs": 140, "totalMs": 170, "attempts": 1 }
}

// ActiveEffect: strings are "" rather than null
{
  "id": "k97…",
  "effect": "recolor",
  "scope": "object",                 // "players" (every racer) | "world" | "object" (target) | "class" (every targetClass)
  "target": "veh_12",                // "car:<playerId>" for a racer's car
  "targetClass": "waymo",
  "strength": "",                    // "small" | "medium" | "large" on lasting effects
  "color": "pink",                   // recolor only
  "theme": "",                       // ped_theme only
  "instant": true,
  "status": "active",
  "startsAt": 1794772800000,
  "endsAt": 1794772803000,
  "durationS": 0,
  "label": "Repainted",
  "requestedBy": "jd7…",
  "transcript": "make that Waymo pink"
}
```

**Lifetimes.** The server rolls a lasting effect's duration from the catalogue range and expires it at `endsAt` with `ctx.scheduler.runAt`. The row leaves the subscription then, and that is when clients revert it. The same effect on the same target extends the existing row (same id, later `endsAt`). A counter (`clear_weather`, `lights_on`, `gravity_normal`, `normal_speed`, `drain`) ends the effects it counters. A contradicting effect (night after day, `gravity_higher` after `gravity_lower`) ends its rival. `undo_last` ends the newest lasting effect. Instant effects (counters, `undo_last`, `demolish`, `explode`, `launch`, `recolor`, `extinguish`) stay listed for 3 s so every client sees them once. Apply them once per id and never revert them.

**Confirm.** An object effect aimed at another racer becomes the matching players effect (`slow_down` → `vehicle_slower`, `grow` → `bigger`, and so on), and the call comes back `pending`. Show "This will affect everyone, including you." and call `operator:confirm` within 10 s, or the effect lapses.

**Refusals.** `refusal.reason` is one of `unsupported`, `transformation`, `spawning`, `teleport`, `deferred`, `cheat`, `vague`, `not_a_request`, `too_broad`, `player_target`, `no_op`, `incompatible`, `not_visible`, `protected`, or `unavailable` (Jev failed twice or timed out after 2 s). `message` is a short, friendly toast line of four words or fewer.

**Log.** Every call writes an `operatorCalls` row with the transcript, view, Jev input, answers, command, outcome and timings.

The catalogue (effects, classes, durations, allowed classes) is `convex/operatorCatalogue.ts`. The questions are `convex/jev.ts`.

### Race client notes

- **Measured on rapid-gnat-965** from a cloud machine, with four racers updating and four clients polling in parallel: about 107 ms mean (151 ms p95) per update and 94 ms (126 ms p95) per poll, with no errors. The curl harness that awaited each call sustained 6.7 updates per second per racer, and a round trip that short leaves room for about 9 Hz, so the 5–10 Hz target holds on that link. Voice Operator calls take about 300 ms from `operator:call` to the effect showing up in the subscription (Jev about 140 ms of it).
- **Trust:** there is no auth or anti-cheat. Positions, finishes and world changes are taken as sent; Operator calls are not rate-limited; Scribe tokens are.
- **Rooms are never deleted.** Codes are unique across all rooms ever created, so a code always means the same room.
