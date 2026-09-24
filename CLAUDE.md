# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Unity **6000.6.1f1** (URP 17.6) rebuild of the C++/Metal StarForge RTS at `~/repositories/StarForge`,
tuned for the MacBook Neo (Apple A18 Pro, 8 GB), which is also the development machine. The game rules
and the adaptive AI are ported faithfully from the original; engine-level work (terrain, navigation,
materials, effects, UI) uses Unity-native tools instead of ported code. `README.md` is the long-form
description and is kept current, including measured performance and AI evaluation numbers.

## Licences are not optional

**This repository is public. Nothing goes into it whose licence has not been checked and written down.**
Standing instruction from the user; it holds for every round of work, whether or not the request mentions
assets, and it applies to anything that comes from outside — a model, texture, sound, font, animation,
shader snippet, package or code sample.

1. **Check the licence before using it, not after.** It must be free *and* allow redistribution in a
   public repo (`github.com/SiGe11/StarForge2`): in practice CC0 for art and audio. The Unity Asset
   Store EULA, CC-BY-NC and "free for personal use" are unusable here however generous they sound, and
   so is anything whose terms you could not find. The UI fonts (Inter, OFL 1.1; Roboto Mono, Apache 2.0)
   are the only assets carrying conditions; their licence texts live in `LICENSES/`, which is the
   condition being met.
2. **Record it in the same change that adds it**, in three places: fetch it in `Tools/fetch_assets.py`,
   register it in `THIRD-PARTY.md` (author, source URL, licence, and which committed files come out of
   it), and credit it in the README. An asset in the tree that nobody recorded the origin of is a bug.
3. **Run `python3 Tools/check_licences.py` before reporting a round that touched assets**, and say in
   the report what it said. It re-checks every source page against the register and fails if any image,
   model, font or sound under `Assets/` is in neither the register nor its list of what we make
   ourselves. `--offline` skips the network half.
4. **Say so plainly when something is doubtful** — unclear terms, an uploader who may not have held the
   rights, a "free" asset with strings. Leave it out and tell the user, rather than shipping it and
   hoping.

The project's own code and art are MIT (`LICENSE`, © Simon Gergely); the third-party terms sit beside
that, not under it.

## Commands

There is no test suite or linter. Verification is play mode, the AI evaluation and the player benchmark.
All scripts live in the single `Assembly-CSharp` / `Assembly-CSharp-Editor` pair (no asmdefs).

The editor is normally open on this project, so drive it through the Unity MCP tools rather than
`-batchmode` (Unity refuses a second instance on an open project). When the MCP bridge is down (it
drops after long idle waits), `Tools/editor.sh` drives the open Editor through files instead
(`Editor/AgentInbox.cs`): `Tools/editor.sh menu "StarForge/Build All"`, `refresh`, `play`, `stop`,
`state`, or `call Namespace.Type.Method` for any public static no-argument method (its string result
and the console output come back). The inbox also refreshes the asset database when a file under
Assets/ changes, so scripts compile without focusing the Editor window. Editor entry points are
menu items:

| Menu | Does |
|---|---|
| `StarForge/Build All` | Runs Build steps 0–4 in order |
| `StarForge/Build/0 … 4` | Render pipeline → materials → unit defs/prefabs/icons → map scene → scene assembly |
| `StarForge/Build/5 Record Shader Variants` | Run *after* playing a match in the editor; rewrites `Settings/SF_ShaderVariants` preloaded by the player |
| `StarForge/Evaluate AI/Quick` or `Full` | Play-mode AI vs four scripted archetypes; report in the console and `persistentDataPath/starforge_ai_eval.txt` |
| `StarForge/Build macOS Player (Apple silicon)` | Writes `Builds/StarForge.app` (Mono in practice; IL2CPP needs full Xcode). Cached rebuilds ~20 s |
| `StarForge/Debug/AI Internals` | Editor toggle for the developer-only AI inspector and memory controls |
| `StarForge/Debug/Check Map Blocking` | Samples the NavMesh under every scenery piece, boulder, tree trunk (and in play, ore and structures) and reports any a ground unit could walk through (`MapChecks.Report`) |

Benchmark the built player (AI vs AI, vsync off; keep the editor idle while it runs):

```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 120 -sfbenchout /tmp/starforge_bench.txt
```

`-sfgallery <dir>` (with `-sfgalleryquick` to stop early) takes the same screenshots every run on the
default map -- base close and mid, ore, a shore, a grove before, during and after it is set alight, a
an animal, a songbird flock feeding and then flushed, the overview, then the first fire-fight -- so art changes can be compared before/after.
The benchmark and evaluation use a fixed match seed (`MatchSettings.fixedSeed`); in play every match
draws a new one, which is what gives the AI its per-match personality.
On 6000.6.1 capped frames come out quantised to whole refreshes (16.7/33.3 ms) and uncapped ones
bimodal (~9/60 ms), so compare builds by the average, and against a build measured the same way.
Optional `-sfshot <png> -sfshotat <s>` for a mid-run screenshot, `-sfdebug` to show AI internals,
`-sfquality high|balanced|battery` to run a preset without saving it. `-sfscreentest` (no `-sfbench`)
switches full screen and window four times, logs the sizes the player reports and quits; pass
`-logFile` to read them. The terminal has no screen-recording permission, so that log is the check.
The testing cheat (README) is `Ctrl`/`Cmd`+`Shift`+`M`, +5000 ore, and marks `GameWorld.cheated`.
WASD pans the camera (unless a modifier is held), so command hotkeys must not use W, A, S or D.
The High-preset baseline is ~17.3 ms average uncapped on a cool machine, ~18.9 ms capped once it is
warm, render scale settling at 0.70. The Neo is fanless and drifts about a millisecond as it heats, so
compare rendering changes in alternating pairs against the previous build (`-sfcap60` matches builds
from before the uncapping fix).

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

**Third-party assets are welcome.** The user's standing instruction: use external assets — models,
textures, sounds, animations, packages — rather than home-made procedural art, whenever one fits. Check
for a suitable asset before writing a generator: the procedural deer, the procedural crowns and the
synthesised music were all rejected for looking or sounding home-made. Every one of them goes through
*Licences are not optional* above: verified, fetched, registered in `THIRD-PARTY.md`, credited, checked.

Third-party sources (CC0 only: the repo is public) are fetched by `python3 Tools/fetch_assets.py` into
`Art/Source/` (git-ignored): Poly Haven ground and rock scans, ambientCG leaf atlases, Kenney sound packs,
Quaternius animated animals, OpenGameArt music, and the recorded gunshots, tree falls and wood breaks
(the Free Firearm Sound Library, kheetor, AntumDeluge, rubberduck) the weapons and felled trees are built
from. There is no 7z in the standard library; macOS's `tar` (libarchive) reads the firearm library's .7z. Packers turn them
into the committed assets: `Tools/blender/pack_textures.py` (terrain colour + scanned height in alpha,
normals), `Tools/blender/build_rocks.py` (decimated rock FBXs + `rocks.json`, merged into
`ModelFactory.Meta`), `Tools/blender/make_leaf_cards.py` (leaf-spray card textures from the ambientCG
atlases), `Tools/pack_fauna.py` (copies the animal FBXs), `Tools/blender/build_songbird.py` (re-poses and
re-animates Quaternius's perched bird into a flying one with a palette texture), `Tools/make_audio.py`
(copies Kenney effects and the music under game names with `music.json` loudness, synthesises ambience
loops, and builds the guns and the wood: each shot mixed from a near and a far recording of the same gun
with a slap-back, every take levelled to one loudness with `match()`). Generated textures:
`make_leaf_textures.py` (the inner-canopy leaf pile), `make_flame_sheet.py`, `make_smoke_puffs.py`,
`make_panel_texture.py` (the hull plating `armor.png` / `armor_n.png` SF_Unit uses; `_n` is its height,
turned into normals on import). The water keeps the original's `water_n.jpg` ripples: a synthesised
replacement was rejected ("waves were bad"), its foam, glint and reflection changes were kept.
Blender is the image library for scripts that need one (the system Python has only numpy).

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
`Plant` structs, per-kind meshes and colours) and their runtime state: felling, catching fire, passing
it on through the plants and the grass grid, burning out. It is ticked by `GameWorld`, which also owns `Blast(...)` (what an
explosion does to rocks, plants and the ground; called from splash hits and `Kill`). Drawing is
`View/VegetationRenderer`: GPU-instanced per kind with `SF_Tree`, remembering each plant as the player
last saw it. `World/GroundDeformer` (added by `GameWorld.Awake`) digs craters into a runtime copy of
the TerrainData; units ride the unchanged NavMesh, so `UnitView` sinks models by `Drop()`, and ground
clutter, boulders and plants move by `LastChange()` on `Deformed`.

**AI variety.** `Commander.DrawPersonality` draws a `Personality` from the match seed (opening, unit
mix shifts, push timing, flank and second-prong taste, per-plan score offsets), avoiding the openings
of the last two learned matches (`AIMemoryData.recentOpenings/recentPlans/recentWins`);
`StrategySelector.SetBias` nudges away from recent plans and a plan running two minutes without
paying off grows stale. Attack waves (`StartWave/RunWave/EndWave`) pick a target and an approach from
the influence map, stage, and may split a prong. With memory off (benchmark, evaluation) only the
seed varies it.

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
  `SF_Water`, `SF_FogOfWar`, …), not URP Lit (the animals use URP Lit: skinned, few, cheap). Only the
  tree foliage uses alpha test (`clip`, leaf cards, bark and inner canopy never clip): it disables early
  depth testing on Apple's tile-based GPUs, and costs most of the ~1 ms the card crowns added. Blades,
  debris and holograms are opaque geometry instead; do not add alpha test anywhere else.
- No shader writes motion vectors, which is why STP/TAA are not used (moving units would smear).
  So any URP effect whose noise is re-rolled each frame for TAA to average out boils on screen. SSAO
  uses interleaved-gradient noise for this reason; its default blue noise made the grass vibrate.
- The fog-of-war pass (a URP `FullScreenPassRendererFeature` named `FogOfWar` running `SF_FogOfWar`,
  created by `RenderSetup`) also does height fog, haze and sun scattering. Its globals are set by `FogOfWarRenderer` and `Atmosphere`,
  and the ground mask by `GroundMask`. Extend that pass rather than adding
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
- Terrain layers carry their scanned height in alpha (`_ch` textures), which drives the height blend
  and the hollow darkening; `_nrm` textures are imported as-is. Splat order is still lichen, gravel,
  cliff, ash (meadow, dirt, cliff, sand). The AO texture's B channel is moisture (`MapGenerator.Moisture`).
- Scanned rocks use `SF_Rock` (their own UVs and maps), not `SF_Unit`; its shared passes are not
  instancing-aware, so its materials keep GPU instancing off (SRP-batched). Scenery gets a Not Walkable
  volume over its footprint (`MapGenerator.BlockFootprint`), tall enough for pieces set into slopes.
- Crowns are leaf cards (vertex alpha 1, `CardUV` from build_flora's `card()`, kept by the exporter) over
  a solid inner canopy (vertex alpha 0) surfaced with the leaf pile (`_LeafTex`/`_NeedleTex`). The card
  texture, tint, leaf style, blossom and bark style come through the renderer's property blocks
  (`PlantKind.cardTex/cardNormal/cardTint/needles/bloom/birchBark/leafTiling`); foliage is two-sided
  (`_Cull` 0). Card crowns cannot be decimated: far copies come from `build_flora.LOD_BUILDERS`.
- Wind (`World/Wind.cs`): one heading, one strength and one gust rhythm for the whole match, drawn
  from the map seed in `MapGenerator.Generate` and evaluated as a pure function of the match clock, so
  it needs no state and a seed replays identically. Everything that should agree reads it: fire spreads
  downwind and faster in a gust, smoke and embers lean with it, a falling tree leans with it, leaves
  tear off the crowns when it blows hard (`FXDirector.WindBlown`), the cloud shadows drift with it, and
  `Atmosphere` uploads it as `_SF_Wind` (xy heading, z strength, w gust phase) for SF_Tree and SF_Grass.
  Both shaders fall back to a steady breeze when that global is zero, so nothing stands frozen in the
  scene view. Sway amplitude is per material (`_WindStrength`) times that strength.
- **Blast pressure** (`Shaders/SF_Wind.hlsl`, shared by SF_Tree and SF_Grass): a muzzle blast or a burst
  queues a front in `FXDirector.PressureWave`, which expands at 52 m/s and spends itself as it spreads;
  `UploadGusts` puts the four strongest into `_SF_Gusts` (xy origin, z the front's radius now, w the
  shove in metres) and `_SF_GustShape` (xy heading, z how much it favours it, w the front's thickness),
  with `_SF_GustCount` 0 the rest of the time so the loop costs a compare. A plant reads the fronts from
  its own root position, so there is no per-plant state and a seed replays identically. The shove goes
  with the square of a blade's height and with a tree vertex's distance from the trunk's foot, so roots
  stay planted, and both shaders renormalise the displaced vertex to its old distance from the root, so a
  hard push bends a blade or a crown over instead of stretching it. Clear the globals when the match
  stops, or the field stays bent over on the end screen.
  **The pressure draws nothing of its own** -- no drawn ring (`SF_GroundDecal` kind 5, retired) and no
  screen-space refraction (removed from the fog-of-war pass). What it does is *move things*, as it
  arrives at them: `FXDirector.PressureSweep` follows each front's annulus (`Gust.swept`) and ruffles the
  water (small dark rings -- a roughened surface reflects less sky -- a few faint bright ones and spray;
  the wake's white rings made the lake look like snow), lifts dust off dry ground with clods close in,
  tears leaves from the crowns it crosses, and jolts the camera when it reaches `RTSCamera.Focus`.
  Anything new it should move goes there; anything that would only *show* the front does not.
- A felled tree (`Vegetation.Topple`) is a rod pivoting on its stump: angular acceleration
  `1.5 g sin(theta) / L`, so it starts slowly, comes down faster the further it goes, and a tall tree
  takes longer than a short one (measured: 2.4-3.1 s from a Mauler's lean, 1.5-1.7 s when a blast
  throws it). `Fell(..., push)` is the shove in radians a second (blast 0.5-1.3 by distance, Mauler 0.3,
  a burned-through snag 0.05, which then goes downwind). It rests on its own boughs (`Live.rest`), and
  bounces once or twice before settling. `LodgeCheck` hangs it up in a neighbour's crown if one stands
  in the way (`Live.lodged`, `slipAt`), until it slips or that tree goes. `GameEvent.speed` carries the
  crown's speed at the impact, which scales the dust, the debris and the camera shake.
- Fire (`Vegetation.Ignite/Spread/TickGrass`): every kind burns at `PlantKind.burns`, spread runs
  downwind (`Vegetation.Wind`, the heading FXDirector leans its smoke with), and each blaze draws a
  `vigour` that its spread inherits, so most fires die in a couple of plants and a few run; vigorous
  ones throw embers downwind to cross gaps. The grass carries fire between stands: a fuel grid (`GrassCell` 3 m) built on the first tick,
  burning cell to cell and lighting the plants it reaches. **Fire burns only where grass grows**: the
  fuel comes from `MapGenerator.GrassChance`, the same rule GroundScatter places its tufts by, over the
  terrain's own splat weights with the same water, slope and boulder cut-outs, looked at in nine 1 m
  squares a cell (`grassMask`). A cell burns only if `MinExpectedTufts` (2.5) tufts are expected on it at
  full density; flames stand only on its grown squares (`GrassTuft`), and SF_Terrain lays burnt ash only
  on the lichen layer. The old fuel (meadow weight plus a quarter of the gravel) let 4,051 cells burn, 1,661
  of them bare ground with no tuft drawn; now 1,783, 4 of them bare (`AgentPlay.GrassFuelCheck` counts the
  tufts drawn in every burnable cell and sweeps the threshold). The drawn tufts are random and thinner on
  the Balanced and Battery presets, so the match cannot be tuft-for-tuft; the simulation must not depend
  on the preset anyway. Grass under structures and ore seams is cleared from the fuel
  (`ClearGrassUnder`, from `GameWorld.Spawn` and `EnsureGrass`). The grid's fields are `[NonSerialized]`:
  entering play mode the editor round-trips private fields, turning a null array into an empty one that
  looked built and indexed -1. Anything damp resists: `Wetness` (the water level against the
  ground at the spot and 5 m around it) cuts a cell's fuel and a plant's chance to catch, so shores and
  reed beds barely take. Limits are per fire, not per map — each blaze may take `blazeCapPlants` /
  `blazeCapCells` (a sixth of the map's growth at most), so one fire cannot burn everything but a
  match's fires together can; `MaxBurning` (45) plants and `MaxGrassFires` (150) cells burn at once.
  Burnt grass never comes back: `GroundMask` uploads the grid as `_SF_BurntGrass`, which SF_Grass reads
  as stubble and char and SF_Terrain as ash, and which does not heal the way the mask's burn does.
  `AgentPlay.FireTrial`/`FireTrialReport` light 30 fires in play mode at 8x and report each one's plants
  and grass cells: aim for a median of a few plants, a long tail, and most of the map gone after 30.
  `Vegetation` state that needs the map (grass fuel, wetness) is built in `EnsureGrass` on the first
  tick, not `Awake`: `MapInfo.Instance` may not be set yet when this component wakes.
- **Structures catch from wildfire** (`GameWorld.StructureFires`, `Unit.burnUntil`): while a burning plant's
  crown or burning grass is within `FireReach` (1.5 m) of a building's footprint, every `FireCheck`
  (0.5 s) rolls `1 - exp(-CatchRate * heat * 0.5)` (`Vegetation.FireNear` gives the heat). A fire that
  reaches a wall delivers 7-24 heat-seconds (`AgentPlay.BuildingFireTrial` stages Bunkhouses in meadows and
  lights the grass upwind), so at `CatchRate` 0.025 about 29% of them set it alight; judge a change by that
  expected share, which the trial prints, not by its caught-count, which over 8-16 fires is noise. Alight,
  it burns 13-19 s for 6-12% of its health, and never below 1 hp: fire alone does not destroy a building.
  Then it is safe for 45 s. Its own random stream (`fireRng`, seeded from the match seed), so a fire does not
  shift what the rest of a seeded match draws. `StructureIgnited` drives the alert ("... is on fire"), the
  flare, `FXDirector.BuildingFire` and the fire bed in `AudioDirector`. The flame sprite's base is the bottom
  of its quad, so tongues stand on the top of the middle and low on the outer walls: buildings are stepped
  rounds, and at full height over the lower ring the tongues hung in the air; inside the footprint the
  building hides them (the meshes are not readable in the player, so the roof cannot be sampled).
  `AgentPlay.BuildingFireLook` photographs the Foundry and a Bunkhouse burning (`Temp/building_burn_*.png`).
- Trees close `PlantKind.blockRadius` (trunk + low boughs + a unit's radius) as Rubble; groves that would
  disconnect a base, expansion or ore field are dropped (`MapGenerator.Blockage`). MapChecks tests a
  ring 0.4 m outside each trunk and base-to-base connectivity. Undergrowth kinds set `drawDistance` and
  `castShadows = false`. Plant kind indices are MapGenerator's constants; append new kinds, never reorder.
- Fauna (`View/Fauna.cs`): Quaternius FBX in `Art/Fauna/`, imported Legacy-animated with no materials;
  `SceneAssembler.BuildFauna` colours the parts, turns each model to face +Z (the rigs' bones are unnamed,
  so each model's facing is given), sizes it from its skeleton (the imported renderer bounds are
  metres off) and saves `Prefabs/Fauna_*`. Ground animals step only on NavMesh for ground units; flyers
  keep level over the highest ground under their circle. Songbirds (`perches`) are a third case: they
  feed on the ground (hop, peck, look round), flush as a flock, fly a bounding flight to another open
  patch and land, so they need `fold`/`peck` clips and open spots (`Vegetation.UnderCrown`). Animals
  are drawn about twice life size or they vanish into the grass at this camera. View-only and fog-fair:
  shown and frightened only by what the player could see. `-sfnofauna` leaves them out (~0.3 ms).
- Smoke is the thing most easily got wrong here. `FX_Smoke` runs at `_Density` 1.4 and `_SoftFade` 1.0
  (set in `SceneAssembler`): the atlas feathers so wide that at 1.0 density every plume was too faint on
  sunlit ground. The soft fade blends a puff out where geometry sits just behind it, so smoke *born on*
  geometry is invisible: the Mauler's exhaust started on its engine deck and could not be seen at all
  until it was moved half a metre above the stacks. `AgentPlay.MaulerShowReport` counts the particles
  above the deck, which is how that was found -- measure before re-tuning something you cannot see.
  `Tools/make_smoke_puffs.py` writes an atlas whose
  coverage **never reaches opaque** and whose outline is eroded at two scales; `SF_Smoke` must not
  rescale that coverage back up to 1 (it used to divide by 0.6), or one puff draws as a solid sprite and
  a burst is a hard dark lump. Billows are warped ellipses over a domain-warped field, not hemispheres:
  the first version's were countable, and its value noise showed a star in the middle of every puff.
  Particle colours are grey (0.3-0.5), not near-black: the sky term in the shader carries the form, and
  a 0.17 grey under it came out as a hole in the scene. When cutting smoke back, cut what comes off
  moving units -- exhaust, track dust, shell trails -- not what an explosion or a fire makes, which is
  where the drama lives.
- Artillery that keeps missing is moved (`Commander.RepositionStuck`). `Unit.shotsMissed` counts splash
  shells in a row, fired from within 5 m of `missFrom`, that burst further than splash + radius from their
  target; it must not reset on `MoveTo` (units re-path every 1.2 s, so it never grew). After three, the
  tank gets a plain *move* (an attack-move halts at once when the target is in range) to the nearest spot
  round its target from which `GameWorld.ShellFallsShort` says the arc arrives. That function follows
  Fire's arc against the terrain and only counts two metres under the ground as blocked, because the real
  shell tests where it lands each frame and jumps thinner crests. `AgentPlay.BlockedShot` stages an AI
  Mauler behind a ridge from a rifleman and prints where each shell burst; `ShellTrialOffSwitch` flips the
  behaviour for the A/B. Measured: off, 21 shells into the ridge in 45 s, none within 9 m; on, moved after
  three and killed him. Match-level hit rates (`ShellTrial`, `Faction.shellHits/shellMisses`) diverge too
  much between runs to show it.
- Blasts fell trees by **chance**, not by radius (`Vegetation.Blast`): the roll falls off with distance
  over `radius * 1.35` and with trunk thickness. `AgentPlay.FellTrial` shells a wood 60 times and prints
  the rate per distance band -- aim for something like 80/35/17/8% out to 4.3 m for a Mauler's shell.
- The Mauler's weight is `FXDirector.MaulerEffects` (engine smoke off the two stacks, dust and stones off
  the tracks, the barrel smoking after a shot) plus the hull's recoil rock in `UnitView` (`RecoilRock`, a
  damped swing; the turret takes its yaw as a *local* rotation so it rides the hull). The stack and track
  positions are the model's (`Tools/blender/build_models.py`: stacks 0.62 m out on the engine deck, track
  runs 1.30 m out), so moving them in Blender means moving them here. Engine smoke goes through the
  `trail` system, not `smoke`: a base on fire fills `smoke` on its own, and a burning structure is the
  cue the player needs at a distance. The rate thins with camera distance (`RTSCamera.distance`). It
  took three rounds to land: a cloud the size of the tank, then a haze nobody saw, then a wisp at idle and
  a grey-black plume under load. The ground keeps the trail: tread marks last 60 s, and `GroundMask`
  stamps channel A (a crater's thrown soil) along a Mauler's path, healing over minutes. Its sound is
  `AudioDirector.TankVoice`: the three loudest Maulers in earshot get an engine and a track source each,
  engine pitch and level following the load, the clatter following the speed (`AgentPlay.TankVoices`).
  `AgentPlay.MaulerShow`/`MaulerShowReport` stage one in a grove in play mode -- idling, driving, firing,
  the shell landing -- and photograph each moment (`Temp/mauler_*.png`); `AgentPlay.PressureLook` holds a
  front still at several strengths with FXDirector switched off, each against the next frame with it off,
  which is the only way to see what the shove alone did (`Temp/pressure_*.png`). `AgentPlay.SmokeLook`
  photographs a shell burst, a structure going up and a tree burning as their smoke rises
  (`Temp/smoke_*.png`), and `AgentPlay.AudioReport` prints what the scene's sound bank actually loaded,
  which is the only check that make_audio.py's output is wired in. None of them restarts a match that is
  already running: units left over from a restarted match throw from `UnitView.LateUpdate` every frame
  for the rest of the session. After writing a script, wait for `compiling=False` before `play`, or the
  Editor enters play mode on the old assembly and the helper you just added is not there.
- Ore colour lives in two places that must agree: `SFMaterialLibrary` (crystal and ore_glow emission,
  violet rim) and `FXDirector.OreLight/OreRim`; the ground pool's breath (`FXDirector.OreBreath`) uses
  the crystal shader's phase and period.
- Leaf cards and canopy clusters carry custom normals from the exporter (`sf_model.set_normal_hint`,
  applied in `export_fbx.apply_normal_hints`), pointing out of the crown, so a crown lights as one mass.
  Flora also get `SF_<NAME>_LOD1.fbx`, drawn beyond `VegetationRenderer.lodDistance`.
- Craters only lower the ground to the deepest single bowl over it (`GroundDeformer.Crater`); the
  blasted look is `GroundMask` channel A in `SF_Terrain`.
- The backdrop beyond the rim uses `SF_Terrain`'s `_SF_AUTOSPLAT` path with splat weights in vertex
  colour and AO/mottling in uv2, computed by `MapGenerator.SplatAt`, the same rules as the terrain.
- Editor effect captures: `Camera.Render` into a RenderTexture from `Unity_RunCommand` misses particles
  and overlays drawn in `LateUpdate`. Use `ScreenCapture.CaptureScreenshot` with a low `Time.timeScale`,
  and check `EditorApplication.isPlaying` first: `GameBootstrap.StartMatch` in edit mode spawns units
  into the open scene.
- Burning plants draw tongues from `flames.png` (`FXDirector.MakeFlames`, vertical billboards,
  `startSize3D` for tall quads); the flame fills about half its quad. Smouldering after a fire goes
  out is tracked in FXDirector, not the simulation.
- Audio: `AudioDirector.bank` (a `SoundBank` SceneAssembler fills from Audio/) with synth fallbacks.
  Weapons, blasts and the engine beds are *built* by `Tools/make_audio.py` from registered CC0
  recordings, not copied: `build_weapon`/`build_blast` add a falling sub `thump()` and a `rolling()`
  report to the source, then `hipass_lin(..., 45)` takes off everything below 45 Hz. That high-pass
  matters -- a sine at 40 Hz carries enormous energy for its loudness, a laptop speaker cannot move it,
  and `match()` then turns the audible part down to make room for it: the first pass at these had the
  cannon 95% below 120 Hz and it came out as a quiet thud. Check a new sound with the share of its
  energy under 120 Hz and its spectral centroid, not by ear alone (nothing here can be listened to from
  the agent's side): rifle ~800 Hz, cannon ~180, the engine bed ~220, a shell landing 240-320, a
  structure 150-200. The engine was 83 Hz before its knock was soft-clipped and the bed high-passed.
  Music is `music_<calm|tension|combat>_<n>` tracks played one mood at a time on two crossfading
  sources, each at `musicLoudness` / its RMS from `music.json`; the benchmark report prints how much each
  mood played. The calm set (three tracks, played in turn) has to be warm: a minor-key piano loop under
  a quiet base reads as creepy rather than peaceful, which is what the first two calm tracks did. Judge
  a candidate with `python3 Tools/measure_music.py <file>` (key and how much of the time the harmony is
  minor, dissonant-interval and beating content, brightness, note density, swell; `--segments` catches a
  dark passage inside a warm track), not by its title — none of it can be listened to from here. Aim for
  a major key, minor_share well under 0.5, dissonance under ~0.05 and swell under 3; the two tracks the
  calm set started with were 0.68 and 0.46 minor and sounded creepy. `Tools/blender/encode_audio.py`
  Vorbis-encodes WAV sources (macOS has no ogg encoder).
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
