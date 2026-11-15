# Audio credits

Every clip in `unity/Assets/Resources/Audio/` is either a real recording from an open library or a
synthesized fallback. Every source is free for commercial use in a shipped app with **no attribution
required**, so nothing is credited on screen; this file is the record. No CC-BY, CC-NC or
personal-use-only content, no ripped game audio and no AI-generated music is used.

## Licences

| Licence | Terms |
| --- | --- |
| Sonniss GDC | Sonniss #GameAudioGDC bundle licence: royalty-free, commercial use, no attribution, unlimited projects (https://sonniss.com/gdc-bundle-license/). It forbids redistributing the sounds as sound effects and using them for AI training, so the original downloads are not in this repo. Bundles: GDC 2024 (https://sonniss.com/gameaudiogdc/) and GDC 2026 (https://gdc.sonniss.com/). |
| CC0 1.0 | Public domain dedication (https://creativecommons.org/publicdomain/zero/1.0/). Used for Kenney packs, Freesound sounds filtered to CC0, and OpenGameArt tracks whose licence field lists CC0. |
| CC0 1.0 (this repo) | Synthesized by `tools/AudioGen/generate.py`. |

## How the clips are built

`tools/AudioGen/recorded/process.py` reads `tools/AudioGen/recorded/specs.json`. Each entry names
its source file (a path substring under the download folders in `AUDIO_SOURCES`, colon-separated), the section or the Nth hit to take, layering offsets and gains, and whether to make
a crossfaded loop. Clips are resampled to 22.05 kHz mono (music: 32 kHz stereo), trimmed, faded,
high-passed and DC-corrected. Each replacement is level-matched to the active RMS of the synthesized
clip it replaced (with at most 3 dB of peak limiting, ceiling -1 dBFS), so the mix from #39 keeps its
balance. Music is loudness-normalized to -16 LUFS (the results sting to -14 LUFS).

The original downloads stay out of the repo. To rebuild, download the sources below and run
`python3 tools/AudioGen/recorded/process.py tools/AudioGen/recorded/specs.json unity/Assets/Resources/Audio`.

Music came from OpenGameArt rather than Pixabay. Pixabay's site sat behind an interactive human
check, so none of its tracks were used, and no track here is registered with YouTube Content ID as
far as their pages state.

## Recorded clips

A clip listed on several rows is a layer of those sources.

| Clip | Source | Author | URL | Licence |
| --- | --- | --- | --- | --- |
| `Engines/race_low` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle3of9.zip): Pole Position - Citroen C3 R5 WRC2 2019 / `Citroen C3 R5 WRC2 - t3 - ONBRD - STEADY RPMS - Drive - ENGINE - VP88.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/race_high` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle3of9.zip): Pole Position - Citroen C3 R5 WRC2 2019 / `Citroen C3 R5 WRC2 - t3 - ONBRD - STEADY RPMS - Drive - ENGINE - VP88.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/sedan_low` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): Dramatic Cat - Audi Q2 / `VEHCar_Audi Q2 ONBOARD Driving Sequence With Revs Up And Down EXHAUST RIGHT_DRCA_AUQ2_MKH8040.wav` | Dramatic Cat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/sedan_high` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): Dramatic Cat - Audi Q2 / `VEHCar_Audi Q2 ONBOARD Driving Sequence With Revs Up And Down EXHAUST RIGHT_DRCA_AUQ2_MKH8040.wav` | Dramatic Cat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/hatchback_low` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Justsoundeffects - Renault R4 / `VEHCar_Renault R4 GTL Drive Various Speed Gear Down Exterior_JSE_RR4_Engine Compartment Mic_Mono.wav` | Justsoundeffects | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/hatchback_high` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Justsoundeffects - Renault R4 / `VEHCar_Renault R4 GTL Drive Various Speed Gear Down Exterior_JSE_RR4_Engine Compartment Mic_Mono.wav` | Justsoundeffects | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/cab_low` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle5of9.zip): Pole Position - Range Rover 4.4 SDV8 / `Range Rover SDV8 - t10 - ONBRD - CIRCLING - Start Drive Stop Off - EXHAUST - Right - RE50.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/cab_high` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle5of9.zip): Pole Position - Range Rover 4.4 SDV8 / `Range Rover SDV8 - t12 - ONBRD - FAST - Drive Various - EXHAUST - Left - BETA58.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/bus_low` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): Dramatic Cat - Magnum 7250 / `VEHFarm_Tractor Magnum 7250 ONBOARD Driving Sequence Idle And Revving ENGINE LEFT_DRCA_MAGN_UsiPro.wav` | Dramatic Cat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/bus_high` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): Dramatic Cat - Magnum 7250 / `VEHFarm_Tractor Magnum 7250 ONBOARD Driving Sequence Idle And Revving ENGINE LEFT_DRCA_MAGN_UsiPro.wav` | Dramatic Cat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/tire_roll` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Justsoundeffects - Renault R4 / `VEHTire_Renault R4 GTL Drive and Roll Fast Across Street Speed Bumps Exterior_JSE_RR4_Wheel Mic_Mono.wav` | Justsoundeffects | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Engines/tire_skid` | Freesound #71739 "Chrysler LHS tire squeal 04 (04-25-2009).wav" (10.503-13.500 s) | audible-edge | https://freesound.org/s/71739/ | CC0 1.0 |
| `Vehicles/door_open` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle4of9.zip): Pole Position - Citroen C5 2002 / `Citroen C5 - t9 - VAR SFX - Door Open and Close from the Inside - MS Decoded - RSM191.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/door_close` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle4of9.zip): Pole Position - Citroen C5 2002 / `Citroen C5 - t9 - VAR SFX - Door Open and Close from the Inside - MS Decoded - RSM191.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/horn_sedan` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): Dramatic Cat - Audi Q7 / `VEHHorn_Audi Q7 EXTERIOR Horn Short And Long MONO_DRCA_AUQ7_Kmr81i.wav` | Dramatic Cat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/horn_truck` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): Dramatic Cat - Audi Q7 / `VEHHorn_Audi Q7 EXTERIOR Horn Short And Long MONO_DRCA_AUQ7_Kmr81i.wav` | Dramatic Cat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/horn_hatchback` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Justsoundeffects - Renault R4 / `VEHHorn_Renault R4 GTL Horn Signal 01 Interior_JSE_RR4_Mono.wav` | Justsoundeffects | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/horn_scooter` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Justsoundeffects - Renault R4 / `VEHHorn_Renault R4 GTL Horn Signal 01 Interior_JSE_RR4_Mono.wav` | Justsoundeffects | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/horn_race` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle4of9.zip): Pole Position - Ferrari 458 2013 / `Ferrari 458 Straight Pipes - t11 - VAR SFX - Horn Various - Wide AB - MKH8060.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/horn_cab` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle5of9.zip): Pole Position - McLaren 720S Spider SSG 2019 / `McLaren 720S - t14 - VAR SFX - Horn Various - ENGINE - DPA4061.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/horn_bus` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Pole Position - The Vehicle Doors and More Library / `Man Lions Coach - VAR SFX - Horn Various - Mono - RSM191.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/bike_bell` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Sound Effects Collection / `BELLHand_Metallic Bell_ 22_MWSFX_SEC.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/tram_bell` | Freesound #479980 "R04-39-Trolley Bell.wav" (0.129-2.200 s) | craigsmith | https://freesound.org/s/479980/ | CC0 1.0 |
| `Vehicles/bus_air_brake` | Freesound #705390 "Air Brake Applied" (0.355-1.784 s) | chungus43A | https://freesound.org/s/705390/ | CC0 1.0 |
| `Vehicles/boost_whoosh` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Rescopic Sound - Distinct Whooshes / `WHSH_Pure SciFi-Whoosh Fast 03_RSCPC_DW.wav` | Rescopic Sound | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/crunch_heavy_1` | Freesound #151624 "Clank Car Crash Collision" (0.000-0.540 s) | qubodup | https://freesound.org/s/151624/ | CC0 1.0 |
| `Vehicles/crunch_heavy_2` | Freesound #332058 "Collision" (0.001-0.905 s) | qubodup | https://freesound.org/s/332058/ | CC0 1.0 |
| `Vehicles/crunch_heavy_3` | Freesound #675466 "S37-14 Two cars crash foley; good metallic bump.wav" (0.055-1.300 s) | craigsmith | https://freesound.org/s/675466/ | CC0 1.0 |
| `Vehicles/crunch_light_1` | Freesound #332056 "Fast Collision" (0.000-0.309 s) | qubodup | https://freesound.org/s/332056/ | CC0 1.0 |
| `Vehicles/crunch_light_2` | Freesound #340539 "Car_Door_Hit.wav" (0.097-0.442 s) | mickyman5000 | https://freesound.org/s/340539/ | CC0 1.0 |
| `Vehicles/crunch_light_3` | Kenney Impact Sounds / `impactPlate_heavy_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Vehicles/crunch_light_3` | Kenney Impact Sounds / `impactMetal_medium_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Vehicles/scrape_1` | Freesound #385939 "S023_Metal_Rail_Scrape_Mono.wav" (0.049-2.000 s) | Pól | https://freesound.org/s/385939/ | CC0 1.0 |
| `Vehicles/scrape_2` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Torturing Metal / `METLFric_Screeching Metal Rub-11_MWSFX_TM.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Vehicles/scrape_3` | Sonniss GDC 2026 bundle (Sonniss.com-GDC2026-GameAudioBundle1of5.zip): 344 Audio - Extreme Winds Vol. 1 / `METLFric_Large Metal Box, Drag, Geofon_344 Audio_Extreme Winds Vol 1.wav` | 344 Audio | https://gdc.sonniss.com/ | Sonniss GDC |
| `Vehicles/wreck` | Freesound #592388 "Car Crash (with Glass)" (0.003-2.172 s) | magnuswaker | https://freesound.org/s/592388/ | CC0 1.0 |
| `Vehicles/wreck` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Torturing Metal / `METLCrsh_Drop Fall Metal Rattle Scrap Debris_06_MWSFX_TM.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Impacts/body_1` | Kenney Impact Sounds / `impactSoft_heavy_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/glass_1` | Kenney Impact Sounds / `impactGlass_heavy_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/metal_1` | Kenney Impact Sounds / `impactMetal_heavy_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/plastic_1` | Kenney Impact Sounds / `impactPlate_light_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/wood_1` | Kenney Impact Sounds / `impactWood_heavy_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/foliage_1` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle8of9.zip): Sonic Bat - Videogame Foley Essentials Vol. II / `SBvfe2_Dry Tree Branches Contact 024.wav` | Sonic Bat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Impacts/body_2` | Kenney Impact Sounds / `impactSoft_heavy_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/glass_2` | Kenney Impact Sounds / `impactGlass_heavy_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/metal_2` | Kenney Impact Sounds / `impactMetal_heavy_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/plastic_2` | Kenney Impact Sounds / `impactPlate_light_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/wood_2` | Kenney Impact Sounds / `impactWood_heavy_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/foliage_2` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle8of9.zip): Sonic Bat - Videogame Foley Essentials Vol. II / `SBvfe2_Dry Tree Branches Contact 024.wav` | Sonic Bat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Impacts/body_3` | Kenney Impact Sounds / `impactSoft_heavy_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/glass_3` | Kenney Impact Sounds / `impactGlass_heavy_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/metal_3` | Kenney Impact Sounds / `impactMetal_heavy_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/plastic_3` | Kenney Impact Sounds / `impactPlate_light_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/wood_3` | Kenney Impact Sounds / `impactWood_heavy_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/foliage_3` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle8of9.zip): Sonic Bat - Videogame Foley Essentials Vol. II / `SBvfe2_Dry Tree Branches Contact 024.wav` | Sonic Bat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Impacts/concrete_1` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): BluezoneCorp - Stone Impact / `Bluezone_BC0297_stone_impact_015.wav` | BluezoneCorp | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Impacts/concrete_2` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): BluezoneCorp - Stone Impact / `Bluezone_BC0297_stone_impact_041.wav` | BluezoneCorp | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Impacts/concrete_3` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): BluezoneCorp - Stone Impact / `Bluezone_BC0297_stone_impact_hammer_015.wav` | BluezoneCorp | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Impacts/splash` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): InMotionAudio - Submerge / `WATRImpt_Impact20_InMotionAudio_Submerge.wav` | InMotionAudio | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/barrier` | Kenney Impact Sounds / `impactPlate_heavy_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/barrier` | Kenney Impact Sounds / `impactWood_medium_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/bench` | Kenney Impact Sounds / `impactWood_heavy_003.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/bench` | Kenney Impact Sounds / `impactMetal_medium_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/bike_dock` | Kenney Impact Sounds / `impactMetal_medium_003.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/bike_dock` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Pole Position - The Metal Hit Sweeteners Library / `Toolbox - DROP.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/billboard` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Torturing Metal / `METLImpt_Metal Impact-03_MWSFX_TM.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/billboard` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): DavidDumais - Explosion SFX Pack / `WOODCrsh_Designed Wood Crash And Debris 13_DDUMAIS_NONE.wav` | DavidDumais | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/bus_shelter` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Glass / `GLASBrk_Glass Break Hit_04_MWSFX_GL.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/bus_shelter` | Kenney Impact Sounds / `impactMetal_heavy_003.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/cone` | Kenney Impact Sounds / `impactSoft_medium_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/cone` | Kenney Impact Sounds / `impactPlate_light_004.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/crates` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): DavidDumais - Explosion SFX Pack / `WOODCrsh_Designed Wood Crash And Debris 13_DDUMAIS_NONE.wav` | DavidDumais | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/fence` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): InMotionAudio - Wood / `WOODBrk_Snap09_InMotionAudio_Wood.wav` | InMotionAudio | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/fence` | Kenney Impact Sounds / `impactWood_medium_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/fruit_stand` | Kenney Impact Sounds / `impactWood_heavy_004.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/fruit_stand` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle8of9.zip): Sonic Bat - Videogame Foley Essentials Vol. II / `SBvfe2_Shaking Small Wooden Box 030.wav` | Sonic Bat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/fruit_stand` | Kenney Impact Sounds / `impactSoft_medium_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/generic` | Kenney Impact Sounds / `impactGeneric_light_003.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/hydrant_break` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Pole Position - The Metal Hit Sweeteners Library / `Oil Barrel - HIT - Hammer - Take 2.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/hydrant_break` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): InMotionAudio - Submerge / `WATRImpt_Impact20_InMotionAudio_Submerge.wav` | InMotionAudio | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/hydrant_gush` | Freesound #637125 "WATRFlow_ Fire Hydrant Flushing to street MS RAW 01.wav" (10.000-16.000 s) | jdr3productions | https://freesound.org/s/637125/ | CC0 1.0 |
| `Breakables/mailbox` | Kenney Impact Sounds / `impactMetal_heavy_004.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/mailbox` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Pole Position - The Metal Hit Sweeteners Library / `Toolbox - DROP.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/newspaper_box` | Kenney Impact Sounds / `impactMetal_medium_004.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/newspaper_box` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Cardboard and Paper / `PAPRHndl_Paper Sheet Crumple_ 04_MWSFX_CDAP.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/newspaper_box` | Kenney Impact Sounds / `impactGlass_light_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/parking_meter` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Pole Position - The Metal Hit Sweeteners Library / `Iron - Thick - HIT - Hammer.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/parking_meter` | Kenney Impact Sounds / `impactBell_heavy_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/payphone` | Kenney Impact Sounds / `impactMetal_medium_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/payphone` | Kenney Impact Sounds / `impactBell_heavy_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/planter` | Kenney Impact Sounds / `impactMining_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/planter` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle8of9.zip): Sonic Bat - Videogame Foley Essentials Vol. II / `SBvfe2_Dry Tree Branches Contact 024.wav` | Sonic Bat | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/sign` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Pole Position - The Metal Hit Sweeteners Library / `Spade - HIT - Drumstick - Ring - Mute.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/sign` | Kenney Impact Sounds / `impactMetal_light_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/storefront_glass` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Glass / `GLASBrk_Glass Break Hit_04_MWSFX_GL.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/storefront_glass` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): BluezoneCorp - Alien Tripod / `Bluezone_BC0292_alien_tripod_debris_glass_falling_003.wav` | BluezoneCorp | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/street_light` | Kenney Impact Sounds / `impactMetal_heavy_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/street_light` | Kenney Impact Sounds / `impactGlass_medium_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/street_light` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Torturing Metal / `METLCrsh_Drop Fall Metal Rattle Scrap Debris_06_MWSFX_TM.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/traffic_light` | Kenney Impact Sounds / `impactMetal_heavy_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/traffic_light` | Kenney Impact Sounds / `impactGlass_light_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Breakables/traffic_light` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Pole Position - The Metal Hit Sweeteners Library / `Toolbox - DROP.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/trash_can` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Torturing Metal / `METLCrsh_Drop Fall Metal Rattle Scrap Debris_06_MWSFX_TM.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Breakables/tree` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Mechanical Wave - Lumberjack / `WOODBrk_Tree Crack and Fall-13_MWSFX_TJ.wav` | Mechanical Wave | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `People/step_asphalt_1` | Kenney Impact Sounds / `footstep_concrete_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_concrete_1` | Kenney Impact Sounds / `footstep_concrete_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_grass_1` | Kenney Impact Sounds / `footstep_grass_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_wood_1` | Kenney Impact Sounds / `footstep_wood_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_metal_1` | Kenney Impact Sounds / `impactMetal_light_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_asphalt_2` | Kenney Impact Sounds / `footstep_concrete_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_concrete_2` | Kenney Impact Sounds / `footstep_concrete_003.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_grass_2` | Kenney Impact Sounds / `footstep_grass_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_wood_2` | Kenney Impact Sounds / `footstep_wood_001.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_metal_2` | Kenney Impact Sounds / `impactMetal_light_003.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_asphalt_3` | Kenney Impact Sounds / `footstep_concrete_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_concrete_3` | Kenney Impact Sounds / `footstep_concrete_004.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_grass_3` | Kenney Impact Sounds / `footstep_grass_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_wood_3` | Kenney Impact Sounds / `footstep_wood_002.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/step_metal_3` | Kenney Impact Sounds / `impactMetal_light_004.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/land` | Kenney Impact Sounds / `impactSoft_heavy_003.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/shove` | Kenney Impact Sounds / `impactPunch_medium_000.ogg` | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `People/hey_1` | Freesound #345431 "Male_Heyyy_01" (0.076-0.813 s) | Artmasterrich | https://freesound.org/s/345431/ | CC0 1.0 |
| `People/hey_2` | Freesound #557375 "Voice_Man_hey_calling_far_away_shout.wav" (1.441-2.277 s) | ValentinPetiteau | https://freesound.org/s/557375/ | CC0 1.0 |
| `People/hey_3` | Freesound #57204 "Men Shouting Hey.wav" (0.049-0.677 s) | Jace | https://freesound.org/s/57204/ | CC0 1.0 |
| `People/whoa_1` | Freesound #323438 "wow/whoa" (0.027-0.976 s) | Alivvie | https://freesound.org/s/323438/ | CC0 1.0 |
| `People/whoa_2` | Freesound #644747 "whoa!.wav" (0.777-3.151 s) | HappyYoLucky303 | https://freesound.org/s/644747/ | CC0 1.0 |
| `People/woo_1` | Freesound #468898 "woohoo" (0.113-1.787 s) | unfa | https://freesound.org/s/468898/ | CC0 1.0 |
| `People/yikes_1` | Freesound #689626 "Yikes!.wav" (0.000-1.116 s) | _nuel | https://freesound.org/s/689626/ | CC0 1.0 |
| `People/oops_1` | Freesound #445964 "Cartoon - Uh-Oh!" (0.427-1.300 s) | Breviceps | https://freesound.org/s/445964/ | CC0 1.0 |
| `People/jump_hup_1` | Freesound #808213 "jump_grunt_01" (0.004-0.290 s) | arther1974 | https://freesound.org/s/808213/ | CC0 1.0 |
| `People/jump_hup_2` | Freesound #808216 "jump_grunt_03" (0.031-0.367 s) | arther1974 | https://freesound.org/s/808216/ | CC0 1.0 |
| `Ambience/city_base` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle9of9.zip): UberDuo - The ‘hood Audio Playset / `AMBUrbn_City, Buffalo, Residential, Traffic, General_UberDuo_HOOD.wav` | UberDuo | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ambience/third_st_traffic` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle1of9.zip): InMotionAudio - Manchester Ambience / `AMBUrbn_CityAmbience17_InMotionAudio_ManchesterAmbience.wav` | InMotionAudio | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ambience/freeway_hum` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): InMotionAudio - UK Motorway / `AMBTraf_Day09_Traffic_NTG4_InMotionAudio_UKMotorwaySamples.wav` | InMotionAudio | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ambience/bay_water` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Jake Fielding - Industrial Harbor / `WATRWave_Water Lapping against port_Jake Fielding_Industrial Harbor.wav` | Jake Fielding | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ambience/pier_wind` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Pole Position - Wind In Trees / `Wind - Cherry Tree - Strong - Bed - On Ground - ORTF - CMC6.wav` | Pole Position | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ambience/pier_wind` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Jake Fielding - Industrial Harbor / `AMBInd_Distant Port Atmos, Dover, Industrial, Vehicles,_Jake Fielding_Industrial Harbor.wav` | Jake Fielding | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ambience/rain` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle8of9.zip): Systematic Sound - General Ambience Series - Nightscapes 01 / `AMBUrbn_Nighttime-Urban Side Street Empty Rain Dops Hum_SYSO_SYSO009.wav` | Systematic Sound | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ambience/night_crickets` | Sonniss GDC 2026 bundle (Sonniss.com-GDC2026-GameAudioBundle1of5.zip): 344 Audio - East Coast America Vol. 1 / `AMBSubn_Ambience, Forest Crickets, Birds, Connecticut 02_344 Audio_East Coast America.wav` | 344 Audio | https://gdc.sonniss.com/ | Sonniss GDC |
| `Ambience/day_birds` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle2of9.zip): Justsoundeffects - Forest Ambiences / `AMBForst_Spring Afternoon Edge of Mixed Forest Yellowhammer Eurasian Blackcap Tree Pipit Insects_JSE_FA.wav` | Justsoundeffects | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ambience/gull_1` | Freesound #263786 "Seagull single call" (0.004-0.395 s) | steaq | https://freesound.org/s/263786/ | CC0 1.0 |
| `Ambience/gull_2` | Freesound #75195 "Seagull trill and whine.wav" (0.136-1.925 s) | nigelcoop | https://freesound.org/s/75195/ | CC0 1.0 |
| `Ambience/gull_3` | Freesound #73497 "Gull.wav" (0.041-1.800 s) | nigelcoop | https://freesound.org/s/73497/ | CC0 1.0 |
| `Ambience/foghorn` | Freesound #140898 "Foghorn_GoldenGateBridge_0312.wav" (0.521-6.300 s) | exuberate | https://freesound.org/s/140898/ | CC0 1.0 |
| `Ambience/caltrain_horn` | Freesound #455157 "Train horn" (0.000-3.600 s) | L83 | https://freesound.org/s/455157/ | CC0 1.0 |
| `Ambience/caltrain_pass` | Freesound #608267 "Commuter train passing.mp3" (1.000-11.000 s) | PirateScottie | https://freesound.org/s/608267/ | CC0 1.0 |
| `Ambience/distant_siren` | Freesound #469363 "Distant Ambulance Siren" (7.000-17.000 s) | brunoboselli | https://freesound.org/s/469363/ | CC0 1.0 |
| `Ui/ui_tap` | Kenney Interface Sounds / `click_002.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/ui_ready` | Kenney Interface Sounds / `confirmation_001.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/toast` | Kenney Interface Sounds / `pluck_001.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/pause_open` | Kenney Interface Sounds / `open_001.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/pause_close` | Kenney Interface Sounds / `close_001.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/place_up` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Rescopic Sound - User Interaction / `UIAlert_Confirm Middle 12_RSCPC_USIN.wav` | Rescopic Sound | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ui/place_down` | Kenney Interface Sounds / `back_002.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/news_whoosh` | Sonniss GDC 2024 bundle (Sonniss.com-GDC2024-GameAudioBundle6of9.zip): Rescopic Sound - Distinct Whooshes / `WHSH_Airy-Whoosh Wind Gust 11_RSCPC_DW.wav` | Rescopic Sound | https://sonniss.com/gameaudiogdc/ | Sonniss GDC |
| `Ui/operator_on` | Kenney Interface Sounds / `bong_001.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/payphone_pickup` | Sonniss GDC 2026 bundle (Sonniss.com-GDC2026-GameAudioBundle1of5.zip): 344 Audio - Antique Telephone / `COMTelph_Antique Telephone Pick Up & Drop Ear Piece_344 Audio_Antiques - Telephone_05.wav` | 344 Audio | https://gdc.sonniss.com/ | Sonniss GDC |
| `Ui/ring_hum` | Kenney Sci-Fi Sounds / `forceField_000.ogg` (crossfaded into a loop) | Kenney | https://kenney.nl/assets/sci-fi-sounds | CC0 1.0 |
| `Ui/ring_whoosh` | Kenney Interface Sounds / `maximize_003.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/ring_join` | Kenney Interface Sounds / `confirmation_002.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/ring_tick` | Kenney Interface Sounds / `tick_002.ogg` | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `Ui/ring_charge` | Kenney Digital Audio / `powerUp3.ogg` | Kenney | https://kenney.nl/assets/digital-audio | CC0 1.0 |
| `Ui/ring_launch` | Kenney Digital Audio / `zapThreeToneUp.ogg` | Kenney | https://kenney.nl/assets/digital-audio | CC0 1.0 |
| `Ui/call_hangup` | Sonniss GDC 2026 bundle (Sonniss.com-GDC2026-GameAudioBundle1of5.zip): 344 Audio - Antique Telephone / `COMTelph_Antique Telephone Pick Up & Drop Ear Piece_344 Audio_Antiques - Telephone_05.wav` | 344 Audio | https://gdc.sonniss.com/ | Sonniss GDC |
| `Music/music_roam` | "Funky Disco Beats to Boogie/Woogie to" (48.030-117.848 s (32 bars at 110 BPM; 10 ms crossfade from 117.848-117.858 into the loop head)) | fupi (opengameart.org/users/fupi) | https://opengameart.org/content/funky-disco-beats-to-boogiewoogie-to | CC0 1.0 |
| `Music/music_race` | "Neon sign Circuit (main, bpm145)" (0.000-52.966 s, the first 32 bars, 40 ms loop crossfade) | MintoDog (opengameart.org/users/mintodog) | https://opengameart.org/content/neon-sign-circuit | CC0 1.0 |
| `Music/music_final` | "Neon sign Circuit (climax, bpm160)" (0.000-48.000 s, the first 32 bars, 40 ms loop crossfade) | MintoDog (opengameart.org/users/mintodog) | https://opengameart.org/content/neon-sign-circuit | CC0 1.0 |
| `Music/results_sting` | "Racing Game Finish & Jingle: Race Finish Best (Jingle)" (0.000-5.000 s (whole file; 350 ms fade-out added)) | MintoDog (opengameart.org/users/mintodog) | https://opengameart.org/content/racing-game-finish-jingle | CC0 1.0 |

## Synthesized fallbacks

These are still synthesized by `tools/AudioGen/generate.py` (CC0, this repo), because no open
recording fitted better: `Ambience/snow`, `Engines/cart_rattle`, `Engines/cart_squeak_1`, `Engines/cart_squeak_2`, `Engines/cart_squeak_3`, `Engines/ebike_whir`, `Engines/ev_hum`, `Engines/ev_whine`, `Engines/robotaxi_whine`, `Engines/scooter_whir`, `Engines/tram_run`, `Engines/wind_speed`, `People/step_water_1`, `People/step_water_2`, `People/step_water_3`, `Ui/call_ring`, `Ui/countdown_beep`, `Ui/countdown_go`, `Ui/operator_line_bed`, `Vehicles/robotaxi_chime`. They are the electric
drivetrains (EV whine and hum, robotaxi, T Third motor, e-bike, scooter, shopping cart), the speed
wind layer, the snow bed, wet footsteps, the countdown beeps and GO slam, the payphone ring, the
Operator line bed and the robotaxi chime. `generate.py` skips every clip that `specs.json` builds,
so rerunning it only rewrites these.
