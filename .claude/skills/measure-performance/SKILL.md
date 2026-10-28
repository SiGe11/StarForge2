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
4. Long runs: `caffeinate -d -i -t 7200` in the background (display sleep stalls the Editor; the player needs the display too). `editor.sh eval` holds it itself.
5. Rebuild first if code changed: `Tools/editor.sh menu "StarForge/Build macOS Player (Apple silicon)"` (cached ~20 s).
6. Runs are silent by default: the test flags below start the player muted (listener volume 0; voices and mixer still run, so numbers should compare; no muted/unmuted fps pair yet) and Editor play started through `editor.sh` is muted (root `CLAUDE.md`, "Agent runs are silent"). Add `-sfsound` / call `AgentAudio.Hear` only when the user is meant to listen.

## Player benchmark (AI vs AI, seed 1000; Mechs land at ~4 min, so 420 s)
```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 420 -sfplay -sfbenchout /tmp/starforge_bench.txt
```
- Always `-sfplay` (vsync + 60 cap = what a player sees). Without it the run is uncapped and bimodal (~9/60 ms).
- Compare averages against a build measured the same way, in **alternating pairs** (the Neo is fanless and drifts ~1 ms as it heats). `-sfcap60` matches builds from before the uncapping fix.
- Read the report's `simulation ...`, `frames over 40 ms ...` and `frame timing:` lines before blaming rendering; the `frame timing:` GPU time is not usable on Metal.
- Presets and probes: `-sfquality high|balanced|battery` (runs a preset without saving it), `-sfshadows N`, `-sfdensity X`, `-sfseed N`, `-sfdebug`, `-sfshot <png> -sfshotat <s>`; A/B switches `-sfnocloth` (the Mech banner rigid), `-sfnocapes` (no soldiers' capes), `-sfallcapes` (every soldier caped from training: the cape budget full), `-sfnofauna`.

## Flicker / shimmer (built player, camera still; never trust editor captures)
```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 22 -sfquality high -sfburst /tmp/high.raw -sfburstat 8 -sfburstframes 60
python3 Tools/analyse_burst.py /tmp/high.raw
```
`-sfburstzoom 150` zooms out. Prints the share of vibrating pixels (up/down on consecutive frames) and flashes (single-frame bright pops; still ground counted apart from water and moving units) and writes a heatmap BMP.

## Gallery (before/after looks; use this, not editor captures — the Game view is ~810x375)
`-sfgallery <dir>` (`-sfgalleryquick` to stop early). Same shots every run: base close/mid, ore, shore, grove before/during/after fire, animal, songbird flock, overview, first fire-fight, Mechs (`9_mech_close`, `9b_mech_mid`, `9c_mech_other`, `9d_mech_fight_*`, `9e_mech_bay`). The terminal cannot record the screen; `-sfscreentest` (with `-logFile`) logs the sizes after four fullscreen/window switches.

## AI evaluation (Editor, play mode)
`Tools/editor.sh eval quick` (720 s cap, ~6 min) or `eval full` (900 s, 5 games per opponent, ~27-35 min), **run in the background** (`run_in_background`): it starts the run, keeps the display awake, and when the run ends prints the report (`persistentDataPath/starforge_ai_eval.txt`; per match: both sides' Mech Bay/Mech, the AI Mech's time by intent), one line per game and the exception count; exit 1 if the run stops without a report. Do not poll it. The runner clears the trial statics (`Commander.Suspended`/`IgnoreSites`, ...) itself. The time scale (8x), caps, game counts and seeds are what every recorded result was measured at: do not change them to save time (it runs at 7-7.9x already; a faster scale moves the agents further per frame than the world's 0.1 s step, and the baselines would no longer compare). Behind other apps it keeps ~7.6x only at Interaction Mode No Throttling (`editor.sh call StarForge.EditorTools.EditorThrottle.State`; `.NoThrottling` sets it). If it still crawls, bring it frontmost: `open -a /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app`, unless the user is using the Mac (memory: no focus stealing). `AIEvalMenu.Replay` replays a game for behaviour only, not the result.

## Report
State the number, the preset/flags, the build, `pmset -g batt`, lock check, and which earlier build it was paired with.
