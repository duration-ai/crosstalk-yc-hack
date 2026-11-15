# crosstalk-yc-hack

A low-poly open-world game set in San Francisco's Dogpatch neighborhood, built as a native Unity 6.3 iOS app.

Walk the real street grid, tap a parked car to get in, and drive down Tennessee, 3rd or 22nd Street. The city is generated in C# at startup from OpenStreetMap streets and DataSF building footprints, dressed with flat-shaded Polyfork models.

![Top-down preview of the processed Dogpatch data](data/preview.png)

## Layout

| Path | What |
| --- | --- |
| `unity/` | Unity 6000.3.28f1 project. One generated bootstrap scene; everything else is built from code and data. |
| `unity/Assets/Resources/World/dogpatch.json` | Processed street, rail, park, land and building extract (committed, so startup needs no network). |
| `unity/Assets/Resources/Polyfork/` | Polyfork GLBs (as `.bytes`) and `catalog.json`. |
| `tools/DataPrep/` | .NET 8 tool that fetches OSM, DataSF and Polyfork and writes the files above. |
| `scripts/` | Headless Unity install, license activation, and build scripts. |
| `backend/` | Convex backend: players, Speed Dial rooms and races, the voice Operator. |
| `tools/race-bot/` | Bot client that joins a race room by code and drives the real route, for testing multiplayer alone. |

## How it works

- **World** (`Runtime/World`): `WorldBuilder` triangulates land and parks, lays streets as mitred ribbons with sidewalks and centre lines, extrudes each footprint to its DataSF height (median LiDAR height `hgt_median_m`, then OSM `height`/`building:levels`, else 7 m) with window bands, and chunks everything into 200 m meshes. `PropPlacer` scatters street lights, trees, hydrants, traffic lights, benches and rooftop units along the real streets and merges them per chunk.
- **Polyfork models** (`Runtime/Glb`): Unity's importer ignores glTF `COLOR_0`, which is where Polyfork keeps all colour, so `GlbAsset` parses the GLB bytes directly (nodes, meshes, skins, animation clips) and bakes `COLOR_0` into `Mesh.colors`. The player and NPCs are skinned and play the file's Idle/Walk/Run clips through `GlbAnimator`.
- **Look**: one shader, `Dogpatch/FlatVertexColor`, reads vertex colour and computes a per-face normal from screen-space derivatives, so every triangle is flat-shaded. It also takes the sun, hard shadows and fog.
- **Controls** (`Runtime/Gameplay`): one drag-anywhere control. The drag direction (relative to the camera at touch-down) is the direction of travel, and a longer drag goes faster. Releasing lets you coast to a stop. A tap jumps on foot. Tapping a car gets in, and tapping your car while driving gets out. A third-person camera follows.

## Backend seam

Everything a server would own sits behind `Dogpatch.Backend.IGameBackend` (`unity/Assets/Scripts/Runtime/Backend`): the player profile and id, the player's position, heading and mode (`onFoot`/`driving`), which vehicle they're in, the state of every vehicle, and saving and loading. There is one async method per endpoint of the Convex HTTP contract (`POST/GET /api/players`, `GET/PUT /api/player-state`, `GET /api/vehicles`, `POST /api/vehicles/enter|exit|update`). The wire classes in `BackendModels.cs` use the contract's JSON field names, so `JsonUtility` can read Convex responses directly. Failures throw `BackendException` with the contract's error code.

The app ships with `InMemoryGameBackend`, which enforces the same rules and error codes and makes no network calls. `GameController` is the only code that touches the backend: it creates and stores the player id, seeds the parked cars, enters and exits vehicles through the interface, and checkpoints position every 2 s. Switching to Convex means writing an HTTP implementation of `IGameBackend` and constructing it in `GameController.Start`.

## Voice Operator

Holding the pink Call plate talks to the Operator on the Convex backend (`backend/convex/operator.ts`). `Runtime/Operator/OperatorRig` wires it in once the race is set up, with no scene changes:

- **Speech** (`Operator/Voice`): when the Call plate slides in, `ScribeVoice` gets a single-use token from `operator:mintScribeToken` (the ElevenLabs key stays on the server), opens ElevenLabs Scribe realtime and starts the microphone, keeping 0.5 s of pre-roll. Pressing the plate streams 16 kHz mono int16 chunks every 150 ms. Releasing commits, and the app uses `committed_transcript`, or the last partial after 1.5 s. Partials show above the plate while you talk.
- **View** (`Operator/View`): `EntityTagger` tags buildings (`bld_<index>`, with the YC building protected), trees, lights, billboards, parked and traffic cars, locals and racers' cars (`car:<playerId>`). On press, `ViewSnapshot` keeps tagged things within 120 m that are in the frustum and not occluded, and `ViewRanker` picks the top 8. When you're driving, your car is always in as `veh_me`.
- **Convex** (`Operator/Convex`): one `IConvexClient` with two transports. `ConvexDotnetClient` uses the unofficial Convex.Client package from `Assets/Plugins/ConvexClient` (websocket and live subscriptions), compiled in because `Assets/csc.rsp` defines `CONVEX_DOTNET`. `ConvexHttpClient` uses `/api/action|mutation|query` and polls subscriptions every 250 ms. `transport: auto` tries Convex.Client first. To drop the package, delete `Assets/Plugins/ConvexClient` and `Assets/csc.rsp`.
- **Effects** (`Operator/Effects`): `OperatorEffects` applies the effect from the `operator:call` answer right away, then follows `operator:activeEffects`. Rows are keyed by id, a row is reverted when it leaves the list, and instant rows are applied once. `EffectRegistry` maps every catalogue id to an implementation. rain, night, gravity_lower, run_faster, vehicle_hop, recolor, set_fire, launch, stop and big_heads are real, the counters happen on the server, and everything else is a logged no-op.
- **HUD** (`OperatorHud`): countdown chips, refusal toasts, and the confirm sheet ("This will affect everyone, including you." calls `operator:confirm` within 10 s). There's also a debug text field.

Settings live in `Resources/Operator/operator.json`: Convex URL, transport, room code, Scribe model, `commitStrategy` (`manual` or `vad`), `silencePadMs`, keyterms, chunk and pre-roll sizes. You can override them with `-operator-convex <url>`, `-operator-local`, `-operator-transport`, `-operator-room <code>`, `-operator-commit`, `-operator-pad-ms`, `-operator-say "<words>"` or `CONVEX_URL`. The app makes its own Convex player (kept in PlayerPrefs) and room. If Convex is unreachable, if you pass `-operator-local`, or in the scripted demo, the on-device `LocalOperator` answers instead. Deep links skip the microphone: `speeddial://operator?say=make%20it%20rain` and `speeddial://operator?room=ABCD`. Run `./scripts/operator-selftest.sh` for headless checks of the resampler, ranking, wire JSON and registry.

## Multiplayer races

Speed Dial races up to four players on their own phones through the Convex race backend (`backend/CONTRACT.md`, "Speed Dial races"). At startup `RaceDirector` tries the deployment (`https://rapid-gnat-965.convex.site` by default; override with `-convex-url <url>` or `DOGPATCH_CONVEX_URL`). If it answers within 6 s the race runs on `ConvexRaceSession` (HTTP polling at about 8 Hz); if not, or with `-offline`, it runs on `LocalRaceSession` with the bots Maya, Jun and Rosa as before.

Online, the bottom-right corner shows **HOST** and **JOIN**. HOST creates a room and puts its 4-letter code big in the bottom-left corner; JOIN opens the keyboard for a code. Tap your name tab (top left) before joining to change your name. In the lobby everyone's name tab shows their colour block and Ready tick; the host can tap **BOTS** to fill empty slots with bots (off by default), and anyone can **LEAVE**. When everyone is ready the server counts down 3 s (10 s once two are ready), then the race starts on every phone at once. Other players appear on foot in the lobby and in their car during the race, drawn about 150 ms behind the server with a floating name tab; bumping a remote car pushes yours aside locally. World changes from the room's event log apply on every phone in order, the server's results show on every phone, and Ready on the results screen asks for a rematch.

### Three phones in one room

1. Install the same build on all three phones (all default to `rapid-gnat-965`).
2. Phone A: tap HOST. The room code appears bottom left, for example `QMTX`.
3. Phones B and C: tap JOIN and type `QMTX`. Each name tab appears on every phone within a second.
4. Everyone walks onto the yellow start ring and taps Ready. The countdown runs, everyone drives to YC, and the results come from the server. Tap Ready on the results for a rematch.

### Testing alone with the bot client

`tools/race-bot/race-bot.mjs` (Node 18+, no install) is a fake racer: it joins a room by code, readies up, drives the room's route at race speed, fires a world change halfway (rain by default), finishes and prints the results.

```
node tools/race-bot/race-bot.mjs --join QMTX --name Rosa            # join the room your phone hosts
node tools/race-bot/race-bot.mjs --join QMTX --name Jun --fire fog --at 0.3
node tools/race-bot/race-bot.mjs --host --min 2 --name Maya         # bot hosts; join its code on the phone
```

Host on the phone, start one or two bots with its code, then tap Ready: the bots ready up as soon as another racer is in the room. `--host` sends `tools/race-bot/route.json`, the game's route written by the multiplayer check below. Run `--help`-style options from the header of the script (`--speed`, `--lane`, `--races`, `--hz`, `--url`).

The Linux player runs the same check headless: `Dogpatch.x86_64 -speeddial-host -speeddial-autopilot -speeddial-code-file code.txt -speeddial-mp-out shots/` hosts a room, waits for two other racers, readies, races on autopilot and saves screenshots of the lobby, a remote racer, each world change and the results (`MultiplayerCheck`). The client mapping has contract tests: `Unity -batchmode -nographics -quit -projectPath unity -executeMethod Dogpatch.EditorTools.ConvexRaceSessionTests.Run`.

## Rebuild the data

```
dotnet run --project tools/DataPrep -c Release            # uses data/cache/ when present
dotnet run --project tools/DataPrep -c Release -- --refresh
```

This needs the .NET 8 SDK. Bounds are constants in `tools/DataPrep/Program.cs`. Polyfork model ids by role are in `tools/DataPrep/models.json`. See `tools/DataPrep/README.md`.

## Build

All builds are headless (`-batchmode -nographics -executeMethod`).

1. Install Unity. On Linux, run `./scripts/install-unity-linux.sh`, which installs the editor and iOS module into `~/Unity/6000.3.28f1`. On macOS, install 6000.3.28f1 with iOS support through the Hub, or set `UNITY_PATH` to the editor binary.
2. Activate a license with `./scripts/unity-activate.sh`:
   - Personal: set `UNITY_EMAIL` and `UNITY_PASSWORD`. The script activates a Personal seat on the machine through Unity's licensing client. A `.ulf` from another machine will not work, because it is machine-bound.
   - Pro/Plus: also set `UNITY_SERIAL`.
3. Export the iOS Xcode project with `./scripts/build-ios.sh`, which writes `Builds/iOS-<sdk>`:

   | Variable | Default | Meaning |
   | --- | --- | --- |
   | `IOS_SDK` | `simulator` | `simulator` builds for the arm64 iOS Simulator SDK. `device` builds a signing-ready project for the device SDK. |
   | `IOS_BUNDLE_ID` | `com.crosstalk.dogpatch` | Bundle identifier. |
   | `APPLE_TEAM_ID` | empty | Team for automatic signing. Set it for device builds. |
   | `BUILD_OUTPUT` | `Builds/iOS-<sdk>` | Output folder. |

4. Build the app on a Mac:
   - Simulator: run `./scripts/stage-ios-remote.sh`, which packs the export into `Builds/ios-remote/` (about 63 MB, under Revyl's 100 MB upload cap). Then run `./build.sh` in that folder on macOS. It produces `build/Build/Products/Release-iphonesimulator/Dogpatch.app`.
   - Device: open `Builds/iOS-device/Unity-iPhone.xcodeproj` and run on a connected iPhone. Automatic signing uses `APPLE_TEAM_ID`. Alternatively, run `xcodebuild -allowProvisioningUpdates`.
5. Optional: `./scripts/build-linux.sh` builds a Linux desktop player (`Builds/Linux/Dogpatch.x86_64`). It's handy for checking the city on a Linux box, and the mouse stands in for touch.

## Data and attribution

Streets © OpenStreetMap contributors (ODbL). Building footprints come from DataSF Building Footprints (`ynuv-fyni`). Low-poly models come from [polyfork.dev](https://polyfork.dev).
