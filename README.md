# StarForge — Unity edition

A real-time strategy game against an adaptive opponent that scouts, reads your
play style, changes its plan to counter it — and remembers you between matches.

Note: This whole project is for testing Claude capabilities and Unity integration.


This is a rebuild of the C++/Metal StarForge (`~/repositories/StarForge`) in
**Unity 6 (URP)**, tuned for the **MacBook Neo** (Apple A18 Pro, 5-core GPU,
8 GB unified memory, 2408×1506 display). The game rules and the adaptive AI are
carried over faithfully; everything the engine can do better — terrain,
navigation, materials, effects, UI — is done with Unity's own tools instead of
being ported.

![AI vs AI battle on the MacBook Neo, AI inspector open](docs/battle.png)

The look is built the same way as the rest: as few authored assets as possible,
and as much as can be derived. The ground grows its own grass and pebbles from
the terrain's splat weights and remembers where the fighting happened; the sun
casts drifting cloud shadows; mist collects in the low ground; units are worn
metal under a shader that finds their bevels by curvature rather than by a
hand-painted map; buildings print themselves into existence and come apart into
pre-cut pieces; and explosions throw lit debris that bounces off the terrain
behind a ring that bends the picture. It holds ~60 fps on the Neo.

## Quick start

1. Open this folder with Unity **6000.6.0f1**.
2. Open `Assets/StarForge/Scenes/Battlefield.unity` and press Play.
3. Pick an opponent level on the title screen and **Deploy**.

Everything generated (render pipeline, materials, prefabs, the map scene) can be
rebuilt from the **StarForge** menu; **StarForge ▸ Build All** runs every step in order.

## Controls

| | |
|---|---|
| **Camera** | Arrow keys or push the pointer to a screen edge · `Q`/`E` rotate · wheel zoom · middle-drag · `Space` centre on selection |
| **Select** | Click · drag a box · double-click selects that type on screen · `Shift` adds · `F2` whole army |
| **Groups** | `Ctrl`/`Cmd`+`1`–`0` assign · `1`–`0` recall (double-tap to centre) |
| **Orders** | Right-click: move / attack / mine / rally · `A` attack-move · `S` stop · `H` hold |
| **Build** | Digger selected: `B` Bunkhouse · `G` Garrison · `W` Workshop · `N` Sentinel · `F` Foundry |
| **Train** | Foundry `D` · Garrison `T` Trooper, `K` Skimmer · Workshop `M` · `X` cancel |
| **Other** | `I` AI inspector · `P` pause · `,` `.` game speed · `Esc` unwinds placement → selection → menu |

The command card shows every command for the current selection with its hotkey
and ore cost; hover for supply, build time and a description.

## Units

| Unit | Cost | Supply | HP | Role |
|---|---|---|---|---|
| Digger | 50 | 1 | 60 | Mines ore, constructs structures |
| Trooper | 50 | 1 | 55 | Cheap ranged infantry |
| Mauler | 150 | 3 | 180 | Tank; long-range splash artillery, outranges Sentinels |
| **Skimmer** *(new)* | 75 | 2 | 75 | Fast hover raider, wide sensors, double damage vs Diggers |

| Structure | Cost | HP | Provides |
|---|---|---|---|
| Foundry | 400 | 1500 | Trains Diggers, ore drop-off, +10 supply |
| Bunkhouse | 100 | 400 | +8 supply |
| Garrison | 150 | 1000 | Trains Troopers and Skimmers |
| Workshop | 200 | 1250 | Trains Maulers (needs a Garrison) |
| **Sentinel** *(new)* | 125 | 500 | Defensive turret (needs a Garrison) |

**Veterancy** *(new)*: units rank up at 2, 5 and 10 kills (+15% damage and +10%
health per rank) and slowly repair once out of combat.

All numbers live in `Assets/StarForge/Data/Units/*.asset` and can be tuned in the
Inspector; rebuilding prefabs keeps your values.

## The opponent AI

The AI is the original's architecture, ported line for line and extended:

```
Perception ──► OpponentModel ──► StrategySelector ──┬──► Macro
 (fog-limited   (Bayesian filter    (contextual       ├──► Scouting
  memory)        + behaviour         bandit over       ├──► Tactics
                 profile)            8 plans)          └──► Micro (focus fire)
```

It plays under the same restrictions you do, enforced in code:

- **Fog of war.** It reads enemy units only through `GameWorld.Visible`, from the
  same per-team visibility grid that drives your fog. It guesses your base from
  map symmetry and has to scout to confirm it.
- **It clicks.** Every order goes through the same `GameWorld.Cmd*` methods the
  mouse uses.
- **Hands, not hertz.** Orders are paid from an APM token bucket: 90 APM
  (Recruit), 180 (Veteran) or 330 with 0.22 s reactions (Commander, sized
  against a top StarCraft II professional).

New in this edition:

- **It remembers you.** The strategy bandit's learned weights, the long-run read
  of your style and the match record persist between matches (under
  `Application.persistentDataPath`) and seed the next game's priors — the
  cross-game adaptation the original listed as out of scope. It stays a prior:
  every match still scouts and updates on what it actually sees. Toggle it or
  wipe it from the title screen.
- **It uses the new units.** Skimmers scout and raid worker lines; turtling
  plans and any smell of early aggression put Sentinels on the approach; seeing
  your Sentinels and Skimmers is evidence in the opponent model.
- **GPU influence field.** The spatial influence map runs as a Metal compute
  kernel with asynchronous readback, with the CPU path as fallback.
- **AI inspector** (`I`): the current plan and why, the posterior over your six
  possible styles, every plan's bandit score, your behaviour profile, APM and
  how many intended actions the cap refused. On the minimap it marks where the
  AI believes your base is and where its scout is heading.
- **After the match** the end screen shows the AI's dossier on you.
- **Watch AI vs AI** from the title screen.

**StarForge ▸ Evaluate AI** plays the adaptive AI against four scripted
archetypes (rusher, macro, turtle, harasser) in the real scene and reports win
rate, how often and how fast it identifies each one, the plans it chose and its APM.

A quick run on the MacBook Neo (Commander, memory off, one match per opponent,
300 s cap — single matches, so the percentages are noisy):

| Opponent | Result | Read correctly | First correct read | Read as | APM avg / peak |
|---|---|---|---|---|---|
| Rusher | time cap | 42% | 126 s | rushing 42%, harassing 26%, turtling 17% | 36 / 240 |
| Macro | time cap | 14% | 30 s | turtling 48%, expanding 38%, macro 14% | 29 / 120 |
| Turtle | time cap | 85% | 71 s | turtling 85%, macro 15% | 27 / 96 |
| Harasser | time cap | 66% | 82 s | harassing 66%, turtling 20%, macro 14% | 38 / 228 |

No losses, and no wins inside this cap: an earlier run with a 420 s cap beat the
rusher at 416 s and the harasser at 372 s, both past the 300 s used here, so
read the wins column as "not decided in five minutes" rather than as a change.
The shape matches the original's own evaluation: **aggressive versus passive is
read reliably** — the aggressive opponents are read as rushing or harassing and
the passive ones never are — while the finer label inside each pair is hard to
observe. A rush and a raid look alike from outside the enemy base, and the
scripted macro player's second Foundry makes it read as *expanding*. The APM cap
refused 11% of intended actions against the rusher, where fighting is constant,
and 2-3% otherwise.

## How it is built

| Area | Approach |
|---|---|
| Map | A **Unity Terrain**, generated once by the original terraced-heightfield algorithm (renormalised fBm, terraces, rim mountains, carved connecting ramp) and then an ordinary asset you can sculpt and paint. Custom terrain material samples cliffs biplanar so rock strata follow the terraces; baked relief AO; backdrop mountains beyond the rim. Moss grows in patches with bare gravel between them, rock breaks through on the steep faces, and scree gathers below it. |
| Scenery | Rock spires and shelves, a crashed dropship and ruined pylons (`Tools/blender/build_env.py`), placed only where 80% of the footprint is ground no unit can use — cliff faces and the rim — and kept out of the NavMesh bake, so the map looks inhabited without a single path changing. |
| Ground cover | Grass and pebbles, grown at load from the terrain's own splat weights and drawn GPU-instanced per 32 m chunk. Blades are real geometry, not alpha-cut cards: `discard` would switch off the hidden-surface removal that keeps overdraw cheap on Apple GPUs. Wind is a travelling gust plus per-blade flutter. |
| Battle damage | A map-wide mask (`GroundMask`) records what the fight has done: flattened under structures and ore, charred where explosions landed (healing over minutes), churned along the tracks of moving units (fading in seconds). Grass bends, parts and burns on it; the terrain darkens scorched and churned soil. |
| Navigation | **NavMesh** (AI Navigation). Structures and ore carve the mesh; water is not walkable; Diggers skip avoidance so mineral lines never jam; Maulers slow down to turn instead of strafing. |
| Models | Authored in **Blender** by script (`Tools/blender`) — the original ten, the Skimmer and Sentinel, and the scenery — exported to FBX with named material slots that map to URP materials. The exporter also bakes ambient occlusion into vertex colours (cast against the whole model and the ground), exports movable parts as child objects with their origin on the pivot, and writes a second FBX of pre-cut chunks for each structure. `Tools/blender/starforge_models.blend` has them all laid out for editing. |
| Unit surfaces | One custom lit shader (`SF_Unit`): the original armour photograph triplanar in object space as an x2 detail multiply normalised by its own average, edge wear found by screen-space curvature (bevels turn the normal fast, flat plates do not) and chipped by that texture, grime climbing from the ground and settling in cavities, a cool sky rim for silhouette. Per renderer it also carries damage charring with glowing seams, the construction hologram, the hit flash and burning — none of it with `discard`, which would cost early depth testing on every unit. |
| Animation | Parts move procedurally from the simulation's own state: legs swing with the stride, the gun and the tank barrels recoil on the shot, the Digger's arm dips and its cutter spins while mining and its drum turns while hauling, the Foundry's control head sweeps and the Workshop's crane trolley travels. |
| Destruction | A destroyed structure is swapped for its pre-cut chunks as rigid bodies, thrown outward glowing hot, tumbling and settling on the terrain, then sunk out of sight. |
| Fog of war | One **URP full-screen render feature** reconstructs world position from depth and applies the player's visibility texture — explored ground dims and cools, unexplored goes dark, with a shimmer at the vision edge. |
| Atmosphere | The same pass integrates exponential height fog along the view ray (mist pools over the lakes and in the low ground, plateaus stay clear), distance haze and sun in-scattering, and refracts the image through each live blast ring. It already has every pixel's world position and the colour buffer, so all of it is free of extra passes. Cloud shadows are the sun's light cookie, a remapped cloud photograph drifting on the wind. |
| Occlusion | **SSAO** at half resolution, normals reconstructed from the depth texture the fog pass already needs, applied after opaques as a single multiply — the cheap path on a tile-based GPU. |
| Effects | Layered explosions: fireball, sparks, lit debris chunks that trail fire and bounce off the terrain, a dust ring rolling outward, drifting embers, a ground shockwave, a point-light flash, scorch marks and camera shake — with structures coming apart in a short chain of secondary blasts. Plus a single instanced depth-decal shader for selection rings, footprints, order markers and scorch marks, and instanced health bars and tracer streaks. |
| Audio | Synthesized at startup (the original had none): weapons, explosions, crystal shatter, alerts, ambience. |
| UI | **UI Toolkit** (`Assets/StarForge/UI`): HUD, minimap, command card, AI inspector, title, pause and end screens. |

### Folder map

```
Assets/StarForge/
  Scripts/Core     math, deterministic RNG, noise
  Scripts/Sim      UnitDef ScriptableObject + catalog
  Scripts/World    GameWorld (rules + command API + visibility), Unit, UnitView, MapInfo
  Scripts/AI       Perception, OpponentModel, InfluenceMap (+GPU), StrategySelector,
                   Commander, AIMemory, ScriptedOpponent
  Scripts/Game     GameBootstrap (match lifecycle), AIEvalRunner, BenchmarkRunner
  Scripts/View     RTSCamera, PlayerController, FXDirector, AudioDirector,
                   FogOfWarRenderer, AdaptiveResolution
  UI/              UXML, USS, HUD controller, Painter2D elements
  Shaders/         terrain, water, fog of war, ground decals, billboards, particles
  Editor/          generators: RenderSetup, SFMaterialLibrary, PrefabBuilder,
                   MapBuilder, SceneAssembler, BuildMac, AIEvalMenu
Tools/blender/     model authoring + FBX export
```

### Generators

| Menu | Produces |
|---|---|
| Build ▸ 0 Render Pipeline | `Settings/SF_URP_Neo` URP asset + renderer with the fog-of-war feature |
| Build ▸ 1 Materials | URP materials for every Blender material slot, per team where needed |
| Build ▸ 2 Unit Definitions, Prefabs & Icons | `Data/Units`, `Prefabs`, command-card icons rendered from the models |
| Build ▸ 3 Map Scene | Terrain, water, backdrop, ore, boulders, lighting, post-processing, NavMesh |
| Build ▸ 4 Assemble Game Scene | World, AI bootstrap, camera, input, effects, ground cover, audio, HUD |
| Build ▸ 5 Record Shader Variants | After playing a match in the editor: saves the variants it actually rendered and registers them as preloaded shaders, so the player warms them at load instead of stalling on the first explosion |

Run later steps after earlier ones. **Step 3 regenerates the scene** — once you
start hand-editing the map, stop re-running it.

To regenerate the models (Blender 5.x):

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
  --python Tools/blender/export_fbx.py -- Assets/StarForge/Art/Models Tools/blender/starforge_models.blend
```

## MacBook Neo tuning

- Forward+ rendering, 2× MSAA (tile memory on Apple GPUs), B10G11R11 HDR,
  two 2048 shadow cascades over 150 m, depth texture on, opaque texture off,
  SRP Batcher.
- **Adaptive resolution** lowers render scale under sustained load with FSR
  upscaling, and backs off exponentially after failed climbs so it cannot
  oscillate (the lesson from the original's controller).
- **Three graphics presets** on the title screen, remembered between runs, each
  trading the three things that actually cost on a fanless laptop: how far
  shadows are drawn, how much ground cover is grown, and how far the adaptive
  controller may drop before it gives frames back. *High* is the measurement
  below; *Balanced* shortens shadows and thins the grass; *Battery* also drops
  MSAA and caps the render scale.
- **Preloaded shader variants** (`StarForge ▸ Build ▸ 5`): the recorded set of
  variants a real match renders is warmed at load, instead of Metal compiling the
  first explosion's pipeline state while it is on screen.
- Two upscalers were considered and one kept. **STP** (temporal) would be
  sharper at a given render scale, but it reprojects with motion vectors, and
  the terrain, unit and grass shaders here are hand-written without a motion
  vector pass — moving units would smear. **Adaptive Probe Volumes** were left
  out for the same kind of reason: the three custom shaders would each need
  their GI path reworked and the map rebaked, for bounce light that this open,
  sunlit map barely shows. Both are written down rather than half-done.
- Instanced overlays and pooled particles keep draw calls flat as battles grow;
  a spatial hash keeps target acquisition from going quadratic.
- UI scales from a 1920×1080 reference, ~1.3× on the Neo's panel.

**StarForge ▸ Build macOS Player (Apple silicon)** writes `Builds/StarForge.app`.
To benchmark the player:

```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 120 -sfbenchout /tmp/starforge_bench.txt
```

It plays AI vs AI with vsync off and writes average and percentile frame times.
Measured on the MacBook Neo (Mono build, full screen at 2816×1762, *High*
preset, 200 s of a Commander-vs-Commander match, the same build and map the
screenshots come from):

| | |
|---|---|
| Average | 17.6 ms (56.7 fps) |
| p50 / p95 / p99 | 16.7 / 27.5 / 33.3 ms |
| Worst frame | 149 ms |
| Peak load | 126 entities, 4 projectiles in flight |
| Render scale | settled at 0.70 (adaptive, FSR-upscaled) |

The panel refreshes at 60 Hz, so the median is a full 60 Hz frame and the
average is really a measure of how often that is missed. For scale: the build
*before* this edition's lighting, ground cover, unit shader, destruction and
atmosphere work, benchmarked the same way on the same machine an hour earlier
(100 s rather than 200 s, so the match content differs), averaged 17.7 ms
(56.6 fps) — all of it together costs about a frame in a hundred, because the
expensive parts were
chosen to reuse passes that already existed (the fog-of-war pass carries the
atmosphere and the blast refraction; SSAO reads the depth texture that pass
already needs; clutter is instanced per chunk; cloud shadows are a light cookie).

One hitch remains: the worst frame in a match is around 150 ms. Preloading the
recorded shader variants and warming the debris prefabs at match start removed
the other two (a 1.4 s first-explosion stall and a 200 ms one when the first
structure came apart); this last one is not yet identified.

![A blast: fireball, thrown debris, scorched ground and a damaged Sentinel glowing through its seams](docs/explosion.png)

## Scope, honestly

- Single player against the AI (or AI vs AI). No multiplayer, campaign or saves.
- Units animate by moving whole parts (legs, arms, barrels, turrets, hover and
  banking) from simulation state; there is no skeletal animation or blending.
- The AI's learning is per machine and per player profile file; there is no
  trained neural network — the opponent model is Bayesian inference and the
  strategy layer a contextual bandit, as in the original.

# Test