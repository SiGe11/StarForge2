# CLAUDE.md

Unity **6000.6.1f1** (URP 17.6) rebuild of the C++/Metal StarForge RTS (`~/repositories/StarForge`), tuned for the MacBook Neo (A18 Pro, 8 GB), which is also the dev machine. Rules and adaptive AI are ported faithfully; terrain, navigation, materials, effects and UI use Unity-native tools. `README.md` is the long-form description (kept current, with measured performance and AI evaluation numbers).

## Where things live (area notes load on demand)

This file holds only cross-cutting rules. **Lessons for an area are in that folder's `CLAUDE.md`: read it before changing the area** (it loads by itself when you read a file there; Bash/MCP work does not trigger it, so open it yourself).

| Area | File |
|---|---|
| Commander, Perception, MechBrain, speech, bay edge cases | `Assets/StarForge/Scripts/AI/CLAUDE.md` |
| Rules, nav areas, vegetation, fire, felling, wind | `Assets/StarForge/Scripts/World/CLAUDE.md` |
| Map generation, grove check, base connectivity | `Assets/StarForge/Scripts/World/MapGen/CLAUDE.md` |
| Mech rules, parts, armoury, tracks, balance | `Assets/StarForge/Scripts/World/Mech/CLAUDE.md` |
| FX, smoke, audio playback, wrecks, fauna, pressure | `Assets/StarForge/Scripts/View/CLAUDE.md` |
| Benchmark/evaluation lore, fps baselines, adaptive resolution | `Assets/StarForge/Scripts/Game/CLAUDE.md` |
| Shader and rendering constraints | `Assets/StarForge/Shaders/CLAUDE.md` |
| Generators, URP quirks | `Assets/StarForge/Editor/CLAUDE.md` |
| Asset fetching/packers, audio build, music measuring | `Tools/CLAUDE.md` |
| Blender→Unity contract, FBX export, scanned plants | `Tools/blender/CLAUDE.md` |

**Keeping this tidy:** a new lesson goes in the area file it belongs to, as the rule + a one-clause reason + any acceptance target (not the story of how it was found). Only rules that apply before you open any file belong here. Measured results that are records, not targets, go in the README.

## Licences are not optional

**This repository is public. Nothing goes into it whose licence has not been checked and written down.** Standing instruction from the user, for every round of work whether or not the request mentions assets; it covers anything from outside: model, texture, sound, font, animation, shader snippet, package, code sample.

1. **Check the licence before using it, not after.** It must be free *and* allow redistribution in a public repo (`github.com/SiGe11/StarForge2`): in practice CC0 for art and audio. The Unity Asset Store EULA, CC-BY-NC and "free for personal use" are unusable, and so is anything whose terms you could not find. The UI fonts (Inter, OFL 1.1; Roboto Mono, Apache 2.0) are the only assets carrying conditions; their licence texts live in `LICENSES/`.
2. **Record it in the same change that adds it**, in three places: fetch it in `Tools/fetch_assets.py`; register it in `THIRD-PARTY.md` (author, source URL, licence, which committed files come out of it); credit it in the README. An asset in the tree nobody recorded the origin of is a bug.
3. **Run `python3 Tools/check_licences.py` before reporting a round that touched assets**, and say what it said. It re-checks every source page against the register and fails if any image, model, font or sound under `Assets/` is in neither the register nor its list of what we make ourselves. `--offline` skips the network half.
4. **Say so plainly when something is doubtful** (unclear terms, an uploader who may not have held the rights, a "free" asset with strings): leave it out and tell the user.

Own code and art are MIT (`LICENSE`, © Simon Gergely); third-party terms sit beside that, not under it.
**Third-party assets are welcome** (standing instruction): use external models, textures, sounds, animations, packages rather than home-made procedural art whenever one fits; check for one before writing a generator (the procedural deer, crowns and synthesised music were all rejected as home-made). Each goes through the rules above. Sources and packers: `Tools/CLAUDE.md`.

## Commands

No test suite or linter. Verification = play mode, the AI evaluation, the player benchmark. All scripts are in one `Assembly-CSharp` / `Assembly-CSharp-Editor` pair (no asmdefs).

The Editor is normally open on this project: drive it through the Unity MCP tools, not `-batchmode` (Unity refuses a second instance). When the MCP bridge is down (it drops after long idle waits), `Tools/editor.sh` drives the open Editor through files (`Editor/AgentInbox.cs`): `menu "StarForge/Build All"`, `refresh`, `play`, `stop`, `state`, `call Namespace.Type.Method` (any public static no-argument method; its string result and console output come back). The inbox also refreshes the asset database when a file under `Assets/` changes, so scripts compile without focusing the Editor.

| Menu | Does |
|---|---|
| `StarForge/Build All` | Build steps 0–4 in order |
| `StarForge/Build/0 … 4` | Render pipeline → materials → unit defs/prefabs/icons → map scene → scene assembly (**run in order**; after changing a generator re-run its step and every later one) |
| `StarForge/Build/5 Record Shader Variants` | Run *after* playing a match in the editor; rewrites `Settings/SF_ShaderVariants` |
| `StarForge/Evaluate AI/Quick` or `Full` | Play-mode AI vs four scripted archetypes (720 s / 900 s caps); console + `persistentDataPath/starforge_ai_eval.txt` |
| `StarForge/Build macOS Player (Apple silicon)` | `Builds/StarForge.app` (Mono; IL2CPP needs full Xcode). Cached rebuilds ~20 s |
| `StarForge/Debug/AI Internals` | Toggle developer-only AI inspector and memory controls |
| `StarForge/Debug/Check Map Blocking` | Samples the NavMesh under scenery, boulders, trunks (in play also ore, structures); reports what a ground unit could walk through (`MapChecks.Report`) |

**Any fps number, benchmark, flicker/shimmer burst, gallery set or AI evaluation: invoke the `measure-performance` skill first** (commands, pre-run checklist, flags; baselines and reasons in `Assets/StarForge/Scripts/Game/CLAUDE.md`). The essentials: measure on the built player with `-sfplay`, never from editor captures; compare averages in alternating pairs; record `pmset -g batt`; a locked screen caps the player at 20 fps. Benchmark and evaluation use fixed seed 1000. Fallback if the skill is not listed: `.claude/skills/measure-performance/SKILL.md`; core command `Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 420 -sfplay -sfbenchout /tmp/starforge_bench.txt`.
Testing cheat: `Ctrl`/`Cmd`+`Shift`+`M` (+5000 ore, marks `GameWorld.cheated`). **WASD pans the camera**, so command hotkeys must not use W, A, S or D.

## Cross-cutting rules

- **Never edit a script while the Editor is in play mode.** Unity recompiles and reloads the domain mid-play: `[NonSerialized]` arrays are gone, running trials lose state. Stop, edit, compile, play. After writing a script wait for `compiling=False` before `play`, or play starts on the old assembly.
- **Statics survive play sessions** (domain reload off): `Commander.Suspended`/`IgnoreSites` left on by a trial skew the next evaluation. Clear what you set.
- **The Editor stops ticking when the display sleeps** (an evaluation left running with the screen off stalls and resumes when poked): run `caffeinate -d -i -t 7200` in the background for long runs. It also crawls when not the frontmost app (App Nap; a player run takes the foreground): `open -a /Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app` brings it forward (check `InternalEditorUtility.isApplicationActive`). MCP calls wake it briefly; the file inbox does not.
- **Parallel sessions share one Editor** (see memory): before play mode or writing `.cs`, check `Tools/editor.sh state` and `ListAgents`; announce Editor windows.
- **AI invariants:** enemy state only through `GameWorld.Visible`/`VisibleCell`; every order through the public `GameWorld.Cmd*` the mouse uses, paid from `ActionBudget`. **MechBrain is exempt by design** (omniscient, ~1,200 APM); the Commander is not. AI internals visible to players are forbidden: gate on `MatchSettings.debugAI`. Details: `Scripts/AI/CLAUDE.md`.
- **`UnitView` is presentation only and never writes to the `Unit`**; gameplay effects leave the world only as `GameWorld.Event`s. Never read "no path" as arrival (a NavMesh rebuild takes paths away). Details: `Scripts/World/CLAUDE.md`.
- **The map is rebuilt at every scene start** (`MapRuntime`, order -1000): read the map in `Awake`/`Start` of order > -1000, never cache across scene loads; map-generation changes go in `MapGenerator`, not `MapBuilder`.
- **Generated content** (materials, prefabs, icons, map scene, pipeline asset) comes from `Editor/` generators: change the generator, never hand-edit output; Step 3 regenerates `Scenes/Battlefield.unity` from scratch; Step 2 preserves tuned `Data/Units/*.asset` values (hotkeys too: edit the asset as well).
- **Shaders:** after adding shaders/keywords play a match and re-run Build step 5; alpha test only on tree foliage; no shader writes motion vectors (so no TAA/STP). Others: `Shaders/CLAUDE.md`.
- **Measure, don't guess.** Nothing here can be listened to or watched from the agent's side: use the named `AgentPlay.*`/`MechTrials` helpers, burst capture and numeric checks, and report numbers.

## Reading and context discipline

- **Grep first, then read a range.** Files of 1k+ lines: `AgentInbox.cs`, `FXDirector.cs`, `Commander.cs`, `MapGenerator.cs`, `MechTrials.cs`, `GameWorld.cs`, `MechSpeech.cs` (mostly line data), `Vegetation.cs`, `HUDController.cs`. Find the symbol with Grep (`--include=*.cs`, `.meta` files add noise), then Read with `offset`/`limit`. Never read a whole one to find one method.
- **Logs:** `tail`/`grep` `Logs/Editor.log`, never read it whole. Long bench/eval/trial output goes to a file; read the summary lines. Bash output beyond 40k chars is saved to a file by setting; read that file in slices.
- Generated assets (`Map/*.asset`, `Scenes/*.unity`) and binaries (`.blend`, `__pycache__`) are read-denied on purpose: change the generator and re-run the Build step instead.
- Give each change a verification target before editing (a named trial, the burst numbers, `MapChecks.Report`, an evaluation line) and run it; do not report "should work".

## Compact instructions

When compacting, keep: the task and what is done/left; files changed and whether committed; Editor state (play mode, compiling, which peer session holds it); every measured number with its preset, build and `pmset -g batt`; licence facts for assets touched and whether `check_licences.py` has run; which area `CLAUDE.md` files were already read. Drop file dumps and passing-test output.

## Architecture map

Namespaces map to folders under `Assets/StarForge`: `Sim` (UnitDef ScriptableObjects, `Defs`), `World` (rules; `GameWorld` owns all match state and ticks every `Unit`), `AI` (Perception → OpponentModel → StrategySelector → Commander; MechBrain), `Game` (match lifecycle, evaluation, benchmark), `View` (camera, input, FX, audio, rendering helpers), `UI` (UI Toolkit HUD), `EditorTools` (generators). Mechs: one Mech Bay and one autonomous Mech a side per match (README "Mechs"; `World/GameWorld.Mech.cs`, `World/Mech/`, `AI/MechBrain.cs`).

## Working through the Unity MCP

- Writing any `.cs` under `Assets/` triggers a recompile; MCP calls return "Unity not detected" until it finishes (10-60 s). Batch edits and wait for `Domain Reload Profiling` in `Logs/Editor.log`. Don't write scripts in play mode. If MCP calls hang, switch to `Tools/editor.sh` rather than waiting out the tool timeout (the bridge can die for good after long idle waits).
- Enter play mode with `Unity_ManageEditor` (Action=Play); setting `EditorApplication.isPlaying` from `Unity_RunCommand` does not take effect.
- `Unity_RunCommand` rejects `System.Reflection` (put reflection-heavy code in a project Editor script and call it). Write `UnityEngine.Mesh` in full (`Mesh` resolves to a namespace); call UI Toolkit queries as `UQueryExtensions.Q(...)`; `System.Diagnostics.Stopwatch` is unavailable (use `EditorApplication.timeSinceStartup`).
- An MCP round trip takes 5–10 s, so short-lived effects must be triggered and captured in one command: step particles with `ParticleSystem.Simulate`, physics with `Physics.simulationMode = Script` plus `Physics.Simulate`. `AssetDatabase.Refresh()` in play mode can return a half-reimported frame (grey materials): recapture before believing it.
