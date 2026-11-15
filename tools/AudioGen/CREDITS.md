# Audio credits

Every sound and music track in `unity/Assets/Resources/Audio/` is synthesized from code by
`tools/AudioGen/generate.py` (numpy + scipy, fixed seeds). No samples, recordings, loops or
third-party tracks are used, so there is nothing to attribute and nothing copyrighted.

| Source | What | License |
| --- | --- | --- |
| `tools/AudioGen/generate.py` (this repo) | All 158 clips: engines, vehicles, impacts, breakables, ambience, people, UI, the 3-stem soundtrack and the results sting | CC0 1.0 (public domain dedication) |

The pedestrian voices ("hey!", "whoa", "woo", "yikes", "oops", the jump "hup") are formant
synthesis, not recordings of people. The soundtrack is an original 140 BPM progression and melody
written for this game.

Regenerate with `python3 tools/AudioGen/generate.py` (add a section name such as `music` to
rebuild one group). This file is documentation only and is not shown in the game.
