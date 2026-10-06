---
name: measure-performance
description: Benchmark the StarForge player, compare fps between builds, measure flicker/shimmer, take the gallery screenshot set, or run the AI evaluation. Use for any fps number, benchmark, gallery, burst or evaluation request.
---

# Measuring StarForge (player benchmark, flicker, gallery, evaluation)

Reasons, baselines and the adaptive-resolution rules: `Assets/StarForge/Scripts/Game/CLAUDE.md` — read it before interpreting a number or touching a quality preset.

## Before any run
1. `pmset -g batt` — record it with the number (a charging battery costs ~3 fps; Low Power Mode on battery).
2. Screen not locked: `ioreg -n Root -d1 -a | plutil -extract IOConsoleUsers json -o - -` and look for `CGSSessionScreenIsLocked` (a locked screen caps the player at 20 fps; only the user can unlock).
3. Editor idle and **not in play mode**; no other session using it (`ListAgents`, `Tools/editor.sh state`; the full slot protocol is step 3 of the `verify-change` skill).
4. Long runs: `caffeinate -d -i -t 7200` in the background (display sleep stalls the Editor; the player needs the display too).
5. Rebuild first if code changed: `Tools/editor.sh menu "StarForge/Build macOS Player (Apple silicon)"` (cached ~20 s).

## Player benchmark (AI vs AI, seed 1000; Mechs land at ~4 min, so 420 s)
```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 420 -sfplay -sfbenchout /tmp/starforge_bench.txt
```
- Always `-sfplay` (vsync + 60 cap = what a player sees). Without it the run is uncapped and bimodal (~9/60 ms).
- Compare averages against a build measured the same way, in **alternating pairs** (the Neo is fanless and drifts ~1 ms as it heats). `-sfcap60` matches builds from before the uncapping fix.
- Read the report's `simulation ...`, `frames over 40 ms ...` and `frame timing:` lines before blaming rendering; the `frame timing:` GPU time is not usable on Metal.
- Presets and probes: `-sfquality high|balanced|battery` (runs a preset without saving it), `-sfshadows N`, `-sfdensity X`, `-sfseed N`, `-sfdebug`, `-sfshot <png> -sfshotat <s>`.

## Flicker / shimmer (built player, camera still; never trust editor captures)
```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 22 -sfquality high -sfburst /tmp/high.raw -sfburstat 8 -sfburstframes 60
python3 Tools/analyse_burst.py /tmp/high.raw
```
`-sfburstzoom 150` zooms out. Prints the share of vibrating pixels (up/down on consecutive frames) and flashes (single-frame bright pops; still ground counted apart from water and moving units) and writes a heatmap BMP.

## Gallery (before/after looks; use this, not editor captures — the Game view is ~810x375)
`-sfgallery <dir>` (`-sfgalleryquick` to stop early). Same shots every run: base close/mid, ore, shore, grove before/during/after fire, animal, songbird flock, overview, first fire-fight, Mechs (`9_mech_close`, `9b_mech_mid`, `9c_mech_other`, `9d_mech_fight_*`, `9e_mech_bay`). The terminal cannot record the screen; `-sfscreentest` (with `-logFile`) logs the sizes after four fullscreen/window switches.

## AI evaluation (Editor, play mode)
`Tools/editor.sh menu "StarForge/Evaluate AI/Quick"` (720 s cap) or `.../Full` (900 s): AI vs four scripted archetypes; report in the console and `persistentDataPath/starforge_ai_eval.txt` (per match: both sides' Mech Bay/Mech, the AI Mech's time by intent). Clear statics first (`Commander.Suspended`/`IgnoreSites`; the runners reset them). Bring the Editor frontmost (App Nap): `open -a /Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app`. `AIEvalMenu.Replay` replays a game for behaviour only, not the result.

## Report
State the number, the preset/flags, the build, `pmset -g batt`, lock check, and which earlier build it was paired with.
