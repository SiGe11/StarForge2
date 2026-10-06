---
name: verify-change
description: Verify a StarForge code change in the shared Unity Editor - pick the level and the named check (trial, MapChecks, AgentPlay helper, evaluation), take the Editor slot from peer sessions, compile, play, run, read numbers against the area-note target, stop and clean up. Use before reporting any change under Assets/ as done, when reproducing a bug, and when writing a new play-mode trial.
---

# Verify a change in the Editor

Performance, fps, flicker, gallery and the player benchmark: use the `measure-performance` skill instead. This one covers correctness in play mode.

## 1. Pick the level (before editing: name the check you will run)
| Level | Change | Verification |
|---|---|---|
| 1 | Docs, comments, area notes, README | `git diff -- <paths>` read through |
| 2 | Contained code: one area, behaviour unchanged (refactor, text, tool script, Editor helper) | compiles clean + the area's closest check or a one-off `call` + diff read |
| 3 | Behaviour in gameplay, AI, World, View, shaders or a generator | level 2 + the named check(s) below with numbers against target + `reviewer` agent |
| 4 | Cross-area, balance, Commander/MechBrain, anything a gate covers | level 3 + `StarForge/Evaluate AI/Full` (perf too if per-frame cost changed) + final `git diff -- <your paths>` |

Bug fix: reproduce first with the check that shows it (numbers), fix, the same check shows it gone. Report as REPRODUCED / NOT REPRODUCED, then FIXED AND VERIFIED / NOT VERIFIED.

## 2. Pick the check
The area notes name each check and its target; take the target from there, never from memory. Entry points: `grep -n "public static string" Assets/StarForge/Editor/<File>.cs`; all are called as `Tools/editor.sh call StarForge.EditorTools.<Type>.<Method>` (MechSpeech is `StarForge.AI.MechSpeech`). Staged trials answer "staging; call XReport ..."; call the `...Report` method after the time it names.

| Change touches | Check | Target lives in |
|---|---|---|
| Commander, Perception, OpponentModel, StrategySelector | `Evaluate AI/Quick` (Full to gate), `AIReview.Perceive`/`PerceiveReport` | `Scripts/AI/CLAUDE.md`, README AI section |
| MechBrain, bay, Mech speech | `BayEdgeCases.Run` (+`Only`), `MechTrials.Watch`, `MechSpeech.Check`/`JournalReport`; Full eval | `Scripts/AI/CLAUDE.md` |
| Mech rules, parts, balance, tracks | `MechDuel.Run*Fresh*`, `MechDesigns.ArmourySurvey`, `MechTrackRide.Run` | `Scripts/World/Mech/CLAUDE.md` |
| Unit rules, targeting, harvest, commands, HUD cards | `BugTrials.*` (or a new `BugTrial`), `BugTrials.Cards`/`Quality` | `Scripts/World/CLAUDE.md` |
| Navigation, pathing, blocking | `AgentPlay.OrderTrial`/`StallTrial`, `MapChecks.Report` (edit mode too) | `Scripts/World/CLAUDE.md` |
| Fire, felling, wind, craters | `AgentPlay.FireTrial`/`FellTrial`/`BuildingFireTrial`/`PushLook`/`GrassFuelCheck` | `Scripts/World/CLAUDE.md` |
| Morale, desertion | `MoraleTrials.CampSeeds`/`Desert`/`Punish`; Full eval `morale:` line | `Scripts/World/CLAUDE.md` |
| Map generation | `MapChecks.Report`, `AgentPlay.GroveBatch`/`IslandBatch` | `Scripts/World/MapGen/CLAUDE.md` |
| FX, smoke, wrecks, audio, fauna | `AgentPlay.*Look` (captures in `Temp/*.png`), `FxLook.Run`, `AgentPlay.RifleCheck`/`AudioReport`/`FXCounts` | `Scripts/View/CLAUDE.md` |
| Generators (`Editor/*Builder*`, `SFMaterialLibrary`, `RenderSetup`, `SceneAssembler`) | re-run that Build step and every later one, then the check of what it produces | `Editor/CLAUDE.md` |
| Shaders, quality presets, anything per frame | `measure-performance` skill | `Scripts/Game/CLAUDE.md` |

No check covers it? Write one (see [new-trial.md](new-trial.md)) rather than reporting "should work". Look captures are judged by reading the PNG with Read, and by a number where one exists.

## 3. Take the Editor slot
The Editor is one shared resource for every session: a `.cs` write recompiles under anyone's play mode and wrecks their trial.
1. `pgrep -f "Unity.app/Contents/MacOS/Unity"`: no Editor means no play-mode check (`editor.sh` would wait 900 s). Start it only if the user or task needs it: `open -na /Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app --args -projectPath /Users/sige/starforge` (minutes to import).
2. `ListAgents`: a busy peer that announced an Editor window or trial has it; message it and wait, do not stop its play mode. Subagents cannot do this step: the main session takes the slot.
3. `SF_EDITOR_TIMEOUT=30 Tools/editor.sh state` → want `playing=False compiling=False`. Bring the Editor frontmost if it crawls (App Nap): `open -a /Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app`.
4. Busy peers: one `SendMessage` each — what you run, for how long, "no .cs writes until I say".

## 4. Compile, play, run
1. All `.cs` edits first, in one batch. `Tools/editor.sh refresh`, then `state` until `compiling=False`.
2. Did it compile? Errors after the last reload mean no:
   `awk '/Domain Reload Profiling/{r=NR} /error CS/{e=NR;m=$0} END{print (e>r ? "COMPILE ERRORS: " m : "compiled")}' Logs/Editor.log`
   A failure in a file you did not touch is a peer's: tell them, do not fix it.
3. `Tools/editor.sh play` (MCP `Unity_ManageEditor` Action=Play also works), then `call` the check; for staged trials wait, then `call ...Report`. Runs over a few minutes: run in the background with `caffeinate -d -i -t 7200` alongside, and do not write under `Assets/` until it ends.
4. Exceptions during the run: `grep -n "Exception" Logs/Editor.log | grep -v SuperProxy | tail -20` (only lines after your play started count; `SuperProxyClient` lines are the AI Assistant package's own network errors).

## 5. Clean up and release
`Tools/editor.sh stop`. Reset every static the run set (`Commander.Suspended`, `Commander.IgnoreSites`, `MechCore.LegacyBalance`, trial fields like `MechDuel.Upgrades/Seeds/Fresh`, `BayEdgeCases.Only`; the runners reset some, a one-off command must reset its own). Tell the peers you messaged that the Editor is free.

## 6. Report
Per check: name, the numbers, the target and where it came from, PASS / FAIL / NOT RUN (with why). Say what was not exercised. A failed check is a finding: read the exception or the report, fix, re-run the same check; do not re-run unchanged code hoping for a pass. Where a number is noisy (Trooper counts, match results), the area notes say how many seeds or designs to compare over.
