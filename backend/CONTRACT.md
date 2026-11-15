# Dogpatch backend HTTP contract

This is the API the Unity client's `IGameBackend` will call when it moves from the local in-memory implementation to Convex. Unity has no official Convex SDK, so everything is plain HTTP and JSON against the deployment's HTTP actions URL.

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
| 404 | `NOT_FOUND` | The `playerId` or `vehicleId` does not exist (including malformed ids). |
| 409 | `CONFLICT` | The request contradicts current state: the vehicle is taken, the player is in another vehicle, or the player is not the driver. |
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
- Out of scope today: realtime subscriptions, multiplayer sync, and auth. Those would use a Convex client or WebSocket and are not part of this contract.
