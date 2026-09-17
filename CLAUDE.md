# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Unity **6000.6.0f1** (URP 17.6) rebuild of the C++/Metal StarForge RTS at `~/repositories/StarForge`,
tuned for the MacBook Neo (Apple A18 Pro, 8 GB), which is also the development machine. The game rules
and the adaptive AI are ported faithfully from the original; engine-level work (terrain, navigation,
materials, effects, UI) uses Unity-native tools instead of ported code. `README.md` is the long-form
description and is kept current, including measured performance and AI evaluation numbers.

## Commands

There is no test suite or linter. Verification is play mode, the AI evaluation and the player benchmark.
All scripts live in the single `Assembly-CSharp` / `Assembly-CSharp-Editor` pair (no asmdefs).

The editor is normally open on this project, so drive it through the Unity MCP tools rather than
`-batchmode` (Unity refuses a second instance on an open project). Editor entry points are menu items:

| Menu | Does |
|---|---|
| `StarForge/Build All` | Runs Build steps 0–4 in order |
| `StarForge/Build/0 … 4` | Render pipeline → materials → unit defs/prefabs/icons → map scene → scene assembly |
| `StarForge/Build/5 Record Shader Variants` | Run *after* playing a match in the editor; rewrites `Settings/SF_ShaderVariants` preloaded by the player |
| `StarForge/Evaluate AI/Quick` or `Full` | Play-mode AI vs four scripted archetypes; report in the console and `persistentDataPath/starforge_ai_eval.txt` |
| `StarForge/Build macOS Player (Apple silicon)` | Writes `Builds/StarForge.app` (Mono in practice; IL2CPP needs full Xcode). Cached rebuilds ~20 s |
| `StarForge/Debug/AI Internals` | Editor toggle for the developer-only AI inspector and memory controls |

Benchmark the built player (AI vs AI, vsync off; keep the editor idle while it runs):

```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 120 -sfbenchout /tmp/starforge_bench.txt
```

Optional `-sfshot <png> -sfshotat <s>` for a mid-run screenshot, `-sfdebug` to show AI internals,
`-sfquality high|balanced|battery` to run a preset without saving it. `-sfscreentest` (no `-sfbench`)
switches full screen and window four times, logs the sizes the player reports and quits; pass
`-logFile` to read them. The terminal has no screen-recording permission, so that log is the check.
The testing cheat (README) is `Ctrl`/`Cmd`+`Shift`+`M`, +5000 ore, and marks `GameWorld.cheated`.
WASD pans the camera (unless a modifier is held), so command hotkeys must not use W, A, S or D.
The High-preset baseline is ~17.6 ms average (56.7 fps) with render scale settling at 0.70; compare any
rendering change against it.

Measure flicker/shimmer on the built player, with the camera held still (`-sfburstzoom 150` to zoom out):

```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 22 -sfquality high -sfburst /tmp/high.raw -sfburstat 8 -sfburstframes 60
python3 Tools/analyse_burst.py /tmp/high.raw
```

It prints the share of "vibrating" pixels (up/down on consecutive frames) and "flashes" (single-frame
bright pops, with the ones on still ground counted separately from water and moving units), and writes a
heatmap BMP.
Editor captures (`Camera.Render` into a RenderTexture) did not reproduce shimmer the player showed, so
don't trust them for this.

The app icon is `Art/AppIcon.png`, regenerated from `Assets/icon.png` by `python3 Tools/make_app_icon.py`;
keep Apple's 824 px rounded-square template (macOS 26 puts any other shape on a grey plate).

Regenerate models (Blender 5.x, headless), then re-run Build step 2:

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/export_fbx.py -- Assets/StarForge/Art/Models Tools/blender/starforge_models.blend
```

## Architecture

Namespaces map to folders under `Assets/StarForge`: `Sim` (UnitDef ScriptableObjects, `Defs`),
`World` (rules), `AI`, `Game` (match lifecycle, eval, benchmark), `View` (camera, input, FX, audio,
rendering helpers), `UI` (UI Toolkit HUD), `EditorTools` (generators).

**Simulation.** `GameWorld` (`[DefaultExecutionOrder(-50)]`) owns all match state and ticks every
`Unit` itself in `Update` with `dt` clamped to 0.1 s: cache positions → rebuild the spatial hash →
`Unit.Tick` → compact dead units and rebuild the hash again → projectiles → visibility (every 0.12 s)
→ supply → win check → `Ticked`. `Unit` is order logic on a `NavMeshAgent`; `UnitView` is presentation
only and must never write to the `Unit`. Gameplay effects leave the world only as `GameWorld.Event`
(`GameEvent`), consumed by `FXDirector`, `AudioDirector`, `GroundMask` and `HUDController`.

**AI.** `GameBootstrap.StartMatch` builds a `Commander` for team 1 (and a second one for team 0 when
spectating) and ticks it from `GameWorld.Ticked`. Pipeline: `Perception` (fog-limited memory) →
`OpponentModel` (Bayesian filter over six player styles) → `StrategySelector` (contextual bandit over
eight plans) → `Commander` (macro, scouting, tactics, micro). `AIMemory` persists bandit weights and the
player profile to `persistentDataPath/starforge_ai_memory.json` and seeds the next match's priors.
Invariants that must hold for any AI change:

- Enemy state is read only through `GameWorld.Visible` / `VisibleCell`, never from `units` directly.
- Every order goes through the public `GameWorld.Cmd*` methods the mouse uses, paid from `ActionBudget`
  (APM token bucket) and delayed by the per-difficulty reaction time.

**Navigation areas.** Boulders (`World/Boulder.cs`) and the trunks of trees on walkable ground are left
out of the bake and stand on the `Rubble` area (`NavMeshModifierVolume`s; the trees' all sit on
`Map/Vegetation`); every agent except the Mauler excludes it. `GameWorld` crushes a rock and
`Vegetation` fells a tree when a Mauler reaches it or a blast does, then `GameWorld.RequestNavRebuild`
rebuilds the NavMesh tiles with `NavMeshSurface.UpdateNavMesh`.
`GameWorld.Awake` swaps in a copy of the baked data first, so a match never edits the asset. Water is
not baked: the lake bed is ground (units wade up to `MapBuilder.WadeDepth`), deeper water is Not
Walkable volumes on `Map/DeepWater`. Placement and walkability queries use `GameWorld.GroundAreas`, and
structures need dry ground.

**Vegetation and craters.** `World/Vegetation` holds the plants MapBuilder placed (serialized
`Plant` structs, per-kind meshes and colours) and their runtime state: felling, fire spreading for a
few generations, burning out. It is ticked by `GameWorld`, which also owns `Blast(...)` (what an
explosion does to rocks, plants and the ground; called from splash hits and `Kill`). Drawing is
`View/VegetationRenderer`: GPU-instanced per kind with `SF_Tree`, remembering each plant as the player
last saw it. `World/GroundDeformer` (added by `GameWorld.Awake`) digs craters into a runtime copy of
the TerrainData; units ride the unchanged NavMesh, so `UnitView` sinks models by `Drop()`, and ground
clutter, boulders and plants move by `LastChange()` on `Deformed`.

**AI internals are developer-only.** Anything that exposes the AI's beliefs, plans, or lets the player
disable or reset its memory must be gated on `MatchSettings.debugAI` (`-sfdebug` or the editor menu
above). What players see about the AI is `HUDController.OpponentHelp` ("About the opponent" on the title
screen) and the README's "The opponent AI" section; keep both consistent with what `Commander` does.

**Generated content.** Materials, prefabs, icons, the map scene and the render pipeline asset are
produced by the `Editor/` generators, so change the generator and re-run that step and every later one
(prefabs need materials, the map needs prefabs, assembly needs the map scene). Step 2 preserves tuned
values in `Data/Units/*.asset` (including hotkeys: edit the asset too when changing one). Step 3
regenerates `Scenes/Battlefield.unity` from scratch, which discards hand edits to the map.

**The map is rebuilt at every scene start.** `World/MapGen/MapRuntime` (`[DefaultExecutionOrder(-1000)]`,
on the Map) calls `MapGenerator.Generate` with a new seed before anything else wakes, reusing the Map's
objects (`MapInfo` holds references to the terrain, water, deep water, backdrop, ore/boulder/scenery
containers and `Vegetation`). Anything that reads the map must do so in `Awake`/`Start` of order > -1000
or later, never cache it across scene loads. Old objects are switched off and unparented before
`Destroy`, because the rest of the scene wakes in the same frame. `MapGenerator` is runtime code
shared with the editor's `MapBuilder`, which only adds the `MapKit` asset (materials, layers, scenery
prefabs, plant kinds), the static scene parts and saving; so map-generation changes go in
`MapGenerator`, not `MapBuilder`. The benchmark (`-sfbench`) and AI evaluation use seed 1000;
`-sfseed N` fixes any run. Scene components (ground cover, atmosphere, quality presets, warmup,
HUD) are wired by `SceneAssembler`, not added by hand.

**Blender → Unity contract.** `Tools/blender/sf_model.py` and `build_models.py` come from the original;
`build_models_ext.py` (Skimmer, Sentinel), `build_env.py` (scenery) and `build_flora.py` (trees and
bushes, bark slot before foliage, crown shape into `models.json`) are new. The exporter:

- names material slots after the palette (`armor`, `team`, `glow_cyan`, …), which `SFMaterialLibrary`
  maps to URP materials;
- bakes AO into vertex colour R, and writes G/B from `sf_model.set_vdata` where a part sets them;
- exports `Model.group` parts as child objects that `UnitView` animates by name (`LegL`, `LegR`, `Gun`,
  `Arm`, `Cutter`, `Drum`, `Barrel(s)`, `Head`, `Trolley`, the Digger's `Load`, the ore seam's
  `Crystals`), so renaming a group breaks its animation;
- writes `SF_<NAME>_CHUNKS.fbx` for structures, which become `Debris_*` prefabs for `DebrisBurst`.

## Rendering constraints

- All StarForge materials use hand-written shaders in `Shaders/` (`SF_Unit`, `SF_Terrain`, `SF_Grass`,
  `SF_Water`, `SF_FogOfWar`, …), not URP Lit. None use `discard`/alpha test: it disables early depth
  testing on Apple's tile-based GPUs. Blades, debris and holograms are opaque geometry instead.
- No shader writes motion vectors, which is why STP/TAA are not used (moving units would smear).
  So any URP effect whose noise is re-rolled each frame for TAA to average out boils on screen. SSAO
  uses interleaved-gradient noise for this reason; its default blue noise made the grass vibrate.
- The fog-of-war pass (a URP `FullScreenPassRendererFeature` named `FogOfWar` running `SF_FogOfWar`,
  created by `RenderSetup`) also does height fog, haze, sun scattering and blast-ring refraction. Its globals are set by `FogOfWarRenderer`, `Atmosphere` and
  `FXDirector.UploadShockwaves`, and the ground mask by `GroundMask`. Extend that pass rather than adding
  new full-screen passes.
- URP 17.6 quirks handled in `Editor/RenderSetup.cs`: `upscalerName` is compiled out (set the obsolete
  `upscalingFilter` enum instead), and SSAO is configured through the renderer feature's `m_Settings`
  via `SerializedObject` because its volume override is compiled out.
- After adding shaders or keywords, play a match and re-run Build step 5, or the player hitches while
  Metal compiles the new variant. Unity's recorded list only covers what rendered since the last script
  reload, so step 5 merges into the saved collection; check the new shader's GUID is in it.
- Sub-pixel detail flickers at RTS camera distance (per-blade grass flutter did). Check visual changes
  with a still camera, not only in motion, using the burst capture above.
- High and Balanced use 2x MSAA, which shades a pixel from its centre even when only a sample touches
  the triangle. On thin geometry seen edge-on, interpolants then extrapolate far outside their range;
  the grass blew single pixels into HDR sparks that bloom made into flashing lights. Grass interpolants
  are `centroid` and clamped; do the same for any new thin, instanced geometry.
- Coplanar faces z-fight from the RTS camera. `sf_model.ring_flat` collars used to be wound inside out
  and flush with the drum below; check new models for same-facing coplanar faces across materials.
- `TerrainData.SetAlphamaps` does not survive the asset being saved: the splat weights were silently
  lost and the map rendered as pure moss for a long time. `MapBuilder.PaintSplat` writes the splat
  texture's pixels directly as the last build step and logs the layer shares; check that line. A
  match's map is never saved, so `MapGenerator` just calls `SetAlphamaps`.
- Leaf clusters carry custom normals from the exporter (`sf_model.set_normal_hint`, applied in
  `export_fbx.apply_normal_hints`), which is what keeps them from reading as faceted rocks. Flora also
  get `SF_<NAME>_LOD1.fbx` (Blender decimate), drawn beyond `VegetationRenderer.lodDistance`.
- Craters only lower the ground to the deepest single bowl over it (`GroundDeformer.Crater`); the
  blasted look is `GroundMask` channel A in `SF_Terrain`.
- The backdrop beyond the rim uses `SF_Terrain`'s `_SF_AUTOSPLAT` path with splat weights in vertex
  colour and AO/mottling in uv2, computed by `MapGenerator.SplatAt`, the same rules as the terrain.
- Editor effect captures: `Camera.Render` into a RenderTexture from `Unity_RunCommand` misses particles
  and overlays drawn in `LateUpdate`. Use `ScreenCapture.CaptureScreenshot` with a low `Time.timeScale`,
  and check `EditorApplication.isPlaying` first: `GameBootstrap.StartMatch` in edit mode spawns units
  into the open scene.
- Smoke, dust and mist use `SF_Smoke`: puffs from `Art/Textures/smoke_puffs.png` (normal, occlusion,
  coverage; regenerate with `python3 Tools/make_smoke_puffs.py`), lit by the sun and from above, picked
  and eroded per particle through custom vertex streams (`FXDirector.SmokeStreams`: stable random, age).
  With the sun behind the camera every billow faces it and the cloud goes flat; the light from above is
  what keeps the volume. Only fire and glows still use the photographed sheets (`SF_Particle`).
- Glow is emission read by bloom (threshold 1, clamped at 24). Ore uses `SF_Unit`'s `_Crystal` path,
  which reads per-shard data from vertex colour G/B (`sf_model.set_vdata`); the Digger's `ore_glow`
  material follows `_OreGlow`, set per renderer by `UnitView`. The crystal emission, not the albedo,
  carries the colour: glossy facets reflecting the pale sky washed it to white.
- The opaque texture is on, for the water's refraction (`SF_Water` samples it); nothing else may rely
  on it without accounting for the copy's cost.
- A new or changed shader compiles asynchronously in the editor: instanced trees drew only their
  shadows for a few seconds. Wait before judging an editor capture.

## Working through the Unity MCP

- Writing any `.cs` under `Assets/` triggers a recompile. MCP calls return "Unity not detected" until it
  finishes, so batch edits and wait for `Domain Reload Profiling` in `Logs/Editor.log`. Don't write
  scripts while in play mode.
- Enter play mode with `Unity_ManageEditor` (Action=Play); setting `EditorApplication.isPlaying` from
  `Unity_RunCommand` does not take effect.
- `Unity_RunCommand` rejects `System.Reflection`. Put reflection-heavy editor code in a project Editor
  script and call it. In RunCommand, write `UnityEngine.Mesh` in full (`Mesh` resolves to a namespace) and
  call UI Toolkit queries as `UQueryExtensions.Q(...)`.
- An MCP round trip takes 5–10 s, so short-lived effects must be triggered and captured in one command:
  step particles with `ParticleSystem.Simulate`, and physics with `Physics.simulationMode = Script` plus
  `Physics.Simulate`.
