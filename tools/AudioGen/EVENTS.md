# Sound and feel event map

Every gameplay signal is mapped to the sound it plays and the feel it adds (shake, hit-stop, haptic). The tables in `unity/Assets/Scripts/Runtime/Audio/GameSounds.cs`, `VehicleSounds.cs`, `Ambience.cs`, `GameBridge.cs` and `Feel/FeelDirector.cs` are the source of truth. Clip names are the files in `unity/Assets/Resources/Audio/`; a name without a number picks a random variant (`crunch_heavy` → `crunch_heavy_1..3`).

## Vehicles (every `CarController`, attached automatically)

| Signal | Sound | Feel (player's car only) |
| --- | --- | --- |
| Engine running (driven, or moving) | Combustion: `<class>_low` + `<class>_high` crossfaded and pitched by a gearbox rpm model. Electric: whine pitched by speed plus a hum | — |
| Engine class by model or type id | hatchback, sedan, cab, truck (`cab_*` lower), race car (`race_*`), Muni bus (`bus_*`), Cybertruck (`ev_whine` + `ev_hum`), robotaxi (`robotaxi_whine`), T Third (`tram_run`), e-bike (`ebike_whir`), scooter (`scooter_whir`), shopping cart (`cart_rattle` + `cart_squeak`) | — |
| Driven car speed | `tire_roll` and `wind_speed` layers | — (CROS-16's `CameraFx` does the FOV kick) |
| Lateral g > 9 m/s² or braking > 9 m/s² | `tire_skid` loop | — |
| Enter or exit (`Driven` changes) | `door_open`, then `door_close` | — |
| `GameEvents.RaiseHorn` (CROS-21 `VehicleAudio.Signal`) | the class horn: `horn_hatchback/sedan/cab/truck/race/bus/scooter`, `robotaxi_chime`, `tram_bell`, `bike_bell` | — |
| Collision, other is a car | `crunch_light` (< 7 m/s) or `crunch_heavy`, plus `glass` on hard hits | shake 0.15 to 0.75, haptic light/medium/heavy, 50 ms hit-stop over 16 m/s |
| Collision, other is the world | `metal`, `concrete`, `wood`, `plastic`, `glass`, `foliage`, `body`, `splash` by surface (`SoundSurface` tag, else object names) | same as above |
| Sliding along a wall | `scrape` | — |
| Car lands (`MotionFx.Landed` while driving) | concrete impact | shake |
| `MotionFx.Boosted` | `boost_whoosh` | soft haptic |

## Smash (CROS-23)

| Signal | Sound | Feel |
| --- | --- | --- |
| `SmashScore.Scored` Break, by tuning label | hydrant → `hydrant_break` + 9 s `hydrant_gush`; traffic light → `traffic_light`; street light/lamp → `street_light`; trash/bin → `trash_can`; newspaper → `newspaper_box`; meter → `parking_meter`; bench → `bench`; shelter → `bus_shelter`; tree → `tree`; fence → `fence`; cone → `cone`; barrier → `barrier`; mail → `mailbox`; dock/rack → `bike_dock`; payphone → `payphone`; billboard → `billboard`; sign → `sign`; planter/pot → `planter`; crate/box/pallet → `crates`; storefront/window/glass → `storefront_glass`; fruit/stand → `fruit_stand`; anything else → `generic` | player's breaks: shake 0.28, rigid haptic; big ones (hydrant, shelter, storefront, billboard) shake 0.5, heavy haptic, 70 ms hit-stop |
| `SmashScore.Scored` NearMiss | pedestrian bark | — |
| `VehicleDamage.Wrecked` | `wreck` | player's car: full shake, 120 ms hit-stop, heavy haptic |

## People

| Signal | Sound | Feel |
| --- | --- | --- |
| Player walking | `step_<surface>`, where surface is asphalt, concrete, grass or metal from the street and park meshes, and water when rain > 0.4 | — |
| `PlayerController.Jumped` | `jump_hup` | — |
| `PlayerController.Landed` | `land`, scaled by drop | 3% squash; drops over 11 m/s get a 7–10% squash, a light shake and a light haptic |
| `GameEvents.RaisePedestrianDive` (CROS-20) | `hey`, `whoa`, `woo_1`, `yikes_1` or `oops_1` | — |
| `GameEvents.RaiseShove` | `shove`, plus a bark 60% of the time | light haptic |

## Race, calls and UI (CROS-17)

| Signal | Sound | Feel |
| --- | --- | --- |
| Lobby / gridded / started / final 300 m / results | music mood Lobby / Countdown / Racing / FinalStretch / Results | — |
| Countdown 3, 2, 1 | `countdown_beep` | light haptic |
| GO | `countdown_go` | heavy haptic, shake 0.25 |
| Your place changes | `place_up` / `place_down` | selection haptic when you gain a place |
| Results in | `results_sting` | success haptic for 1st, else warning |
| Race event with news | `news_whoosh` | — |
| Payphone ringing within 70 m | 3D `call_ring` at the phone | — |
| Handset grabbed (Holding) | `payphone_pickup`, `operator_line_bed` loop | medium haptic |
| Talking | the mix ducks | — |
| Operator's line (Answered) | `operator_on`; music, SFX, vehicles and ambience duck | — |
| Call ends | `call_hangup` | — |
| `Hud` pause / resume | `pause_open` / `pause_close`; world audio pauses, music softens | — |
| `GameEvents.RaiseUiTap` / `RaiseUiReady` | `ui_tap` / `ui_ready` | — |

## Music (`MusicDirector`)

Three looping tracks, crossfaded by mood; a track starts from its top when it fades in. `music_roam` is a 110 BPM funk loop (free roam, lobby, results bed), `music_race` a 145 BPM synthwave race loop, and `music_final` the same tune's 160 BPM climax for the final stretch. Sources are in `CREDITS.md`.

| Mood | roam / race / final |
| --- | --- |
| FreeRoam | 0.8 to 1 (rises from 8 to 20 m/s when driving) / 0 / 0 |
| Lobby | 0.8 / 0 / 0 |
| Countdown | 0 / 0.7 / 0 |
| Racing | 0 / 1 / 0 |
| FinalStretch | 0 / 0 / 1 |
| Results | 0.6 / 0 / 0, plus `results_sting` |
| Paused | whatever is playing, at 0.45 and low-passed to 900 Hz |

## World ambience (`Ambience`, from listener position, hour and weather)

| Layer | When |
| --- | --- |
| `city_base` | always, low |
| `third_st_traffic` | near 3rd St, quieter at night |
| `pier_wind` | near Pier 70 and the shore, more in rain |
| `bay_water` | near the shoreline (from the land polygons) |
| `freeway_hum` | always faint, louder toward I-280 |
| `rain` / `snow` | `WorldEnvironment.Rain` / `Snow` |
| `night_crickets` | night, dry |
| `day_birds` | day, dry, away from 3rd St |
| `gull` one-shots | day, near water or Pier 70, every 3–9 s |
| `foghorn` | fog density > 0.5, every 25–40 s, from the bay |
| `caltrain_pass` + `caltrain_horn` | at the 22nd St station every 90–150 s |
| `distant_siren` | every 2–4 min, 350 m away |

## Mix

Six buses (music, SFX, vehicles, ambience, VO, UI) with `SoundSettings` volumes. VO and the Operator duck music to 0.35, SFX to 0.6, vehicles to 0.5 and ambience to 0.45. A master peak limiter on the listener (ceiling 0.89) catches pile-ups. There are 28 one-shot voices and 24 loop voices with distance and priority culling.
