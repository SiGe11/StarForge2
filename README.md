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

![AI vs AI battle on the MacBook Neo, shown with the developer view's AI inspector](docs/battle.png)

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
| **Camera** | `W` `A` `S` `D`, arrow keys or push the pointer to a screen edge · `Q`/`E` rotate · wheel zoom · middle-drag · `Space` centre on selection |
| **Select** | Click · drag a box · double-click selects that type on screen · `Shift` adds · `F2` whole army |
| **Groups** | `Ctrl`/`Cmd`+`1`–`0` assign · `1`–`0` recall (double-tap to centre) |
| **Orders** | Right-click: move / attack / mine / rally · `R` attack-move · `C` stop · `H` hold |
| **Build** | Digger selected: `B` Bunkhouse · `G` Garrison · `V` Workshop · `N` Sentinel · `F` Foundry |
| **Train** | Foundry `U` Digger · Garrison `T` Trooper, `K` Skimmer · Workshop `M` · `X` cancel |
| **Other** | `P` pause · `,` `.` game speed · `Esc` unwinds placement → selection → menu |

The command card shows every command for the current selection with its hotkey
and ore cost; hover for supply, build time and a description.

**Testing cheat** (not shown anywhere in the game): `Ctrl`/`Cmd` + `Shift` + `M`
during a match adds 5000 ore. A match it was used in is not folded into the AI's
memory of how you play.

## Units

| Unit | Cost | Supply | HP | Role |
|---|---|---|---|---|
| Digger | 50 | 1 | 60 | Mines ore, constructs structures |
| Trooper | 50 | 1 | 55 | Cheap ranged infantry |
| Mauler | 150 | 3 | 180 | Tank; long-range splash artillery, outranges Sentinels; drives through boulders and trees, crushing them |
| **Skimmer** *(new)* | 75 | 2 | 75 | Fast hover raider, wide sensors, double damage vs Diggers |

| Structure | Cost | HP | Provides |
|---|---|---|---|
| Foundry | 400 | 1500 | Trains Diggers, ore drop-off, +10 supply |
| Bunkhouse | 100 | 400 | +8 supply |
| Garrison | 150 | 1000 | Trains Troopers and Skimmers |
| Workshop | 200 | 1250 | Trains Maulers (needs a Garrison) |
| **Sentinel** *(new)* | 125 | 500 | Defensive turret (needs a Garrison) |

**Terrain** *(new)*: units wade into shallow water (up to 1 m deep) at about half
speed, Skimmers skim over it at full speed, and deeper water stops everyone.
Boulders block everything except Maulers, which crush them and open the ground
for all.

**Veterancy** *(new)*: units rank up at 2, 5 and 10 kills (+15% damage and +10%
health per rank) and slowly repair once out of combat.

All numbers live in `Assets/StarForge/Data/Units/*.asset` and can be tuned in the
Inspector; rebuilding prefabs keeps your values.

## The opponent AI

*This is what a player needs to know; the title screen shows the same text under
**About the opponent**.*

- **It plays by your rules.** It sees only what its own units see, gives orders
  through the same commands you do, and has a limited number of actions a minute:
  about 90 on Recruit, 180 on Veteran and 330 on Commander, with reactions from
  nearly a second down to a fifth of one.
- **It scouts.** It keeps a scout out whenever its picture of you is going stale,
  and a Skimmer is its favourite for the job. Kill the scout or hide your army and
  it has to guess.
- **It reads you.** From what it has seen it decides whether you are rushing,
  harassing, turtling, expanding, teching or building a big economy, and picks a
  plan against that: an early Trooper rush, raids on your Diggers, a timing push,
  a Sentinel wall while it techs to Maulers, a second ore line, a counter-attack
  while your army is away, or a feint. When its picture of you changes, so does
  its plan.
- **It fights like a player.** It focuses fire on the weakest target that can
  shoot back, pulls its army home when a fight turns against it, sends raiders at
  your workers rather than your army, and comes back to defend when you hit its base.
- **It remembers you.** Between matches it keeps a record of how you tend to play
  and which of its plans worked against you, and each new match starts from that.
  It still scouts every game, so if you change how you play, it will notice.
  There is no switch for this in the game: the opponent learning you is the game.

### How it works

The AI is the original's architecture, ported line for line and extended:

```
Perception ──► OpponentModel ──► StrategySelector ──┬──► Macro
 (fog-limited   (Bayesian filter    (contextual       ├──► Scouting
  memory)        + behaviour         bandit over       ├──► Tactics
                 profile)            8 plans)          └──► Micro (focus fire)
```

The rules above are enforced in code, not just promised:

- **Fog of war.** It reads enemy units only through `GameWorld.Visible`, from the
  same per-team visibility grid that drives your fog. It guesses your base from
  map symmetry and has to scout to confirm it.
- **It clicks.** Every order goes through the same `GameWorld.Cmd*` methods the
  mouse uses.
- **Hands, not hertz.** Orders are paid from an APM token bucket, and reactions
  to what it sees are delayed per difficulty.

New in this edition:

- **Memory.** The strategy bandit's learned weights, the long-run read of the
  player's style and the match record persist between matches (under
  `Application.persistentDataPath`) and seed the next game's priors — the
  cross-game adaptation the original listed as out of scope. It stays a prior:
  every match still scouts and updates on what it actually sees.
- **It uses the new units.** Skimmers scout and raid worker lines; turtling
  plans and any smell of early aggression put Sentinels on the approach; seeing
  your Sentinels and Skimmers is evidence in the opponent model.
- **GPU influence field.** The spatial influence map runs as a Metal compute
  kernel with asynchronous readback, with the CPU path as fallback.
- **Watch AI vs AI** from the title screen.

### Developer view

The AI's internals are hidden from players and kept for development. Turn them on
with **StarForge ▸ Debug ▸ AI Internals** in the editor, or launch the built
player with `-sfdebug`:

```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfdebug
```

That brings back:

- the **AI inspector** (`I`, and always open when spectating): the current plan
  and why, the posterior over the six player styles, every plan's bandit score,
  the behaviour profile, APM and how many intended actions the cap refused, plus
  minimap marks for where the AI believes your base is and where its scout is heading;
- the **memory toggle** and **"Make the AI forget me"** on the title screen, and
  the memory state in the top bar;
- the **AI's dossier** on the end screen: how it read you and which plans it used.

**StarForge ▸ Evaluate AI** plays the adaptive AI against four scripted
archetypes (rusher, macro, turtle, harasser) in the real scene and reports win
rate, how often and how fast it identifies each one, the plans it chose and its APM.

A quick run on the MacBook Neo (Commander, memory off, one match per opponent,
420 s cap, on the default map — every match now gets a new map, but the evaluation
stays on seed 1000 so runs compare — with beaches, wading, crushable boulders, groves
of trees and craters; single matches, so the percentages are noisy):

| Opponent | Result | Read correctly | First correct read | Read as | APM avg / peak |
|---|---|---|---|---|---|
| Rusher | time cap | 66% | 122 s | rushing 66%, turtling 13%, harassing 11%, macro 10% | 37 / 108 |
| Macro | time cap | 10% | 30 s | expanding 73%, turtling 17%, macro 10% | 29 / 216 |
| Turtle | time cap | 71% | 70 s | turtling 71%, teching 19% | 30 / 156 |
| Harasser | **won at 392 s** | 70% | 62 s | harassing 70%, rushing 22% | 37 / 240 |

No losses. Across the last five runs (before the terrain changes, after them, after
the Mauler shell fix, with the trees, and with the grown trees and craters that
do not stack) the rusher was read as rushing 42%, 39%, 21%, 56% and 66% and as
harassing most of the rest, the harasser 66–85% and the turtle 71–90%: single-match
noise, with the aggressive/passive split holding every time. Read the wins column
as "not decided within the cap" rather than as a change.
The shape matches the original's own evaluation: **aggressive versus passive is
read reliably** — the aggressive opponents are read as rushing or harassing and
the passive ones never are — while the finer label inside each pair is hard to
observe. A rush and a raid look alike from outside the enemy base, and the
scripted macro player's second Foundry makes it read as *expanding*. The APM cap
refused 2-6% of intended actions, and 22% against the harasser, where fighting is
constant.

## How it is built

| Area | Approach |
|---|---|
| Map | **A new battlefield every match.** When the scene starts, `MapRuntime` runs the original terraced-heightfield algorithm (renormalised fBm, terraces, rim mountains, carved connecting ramp) with a fresh seed and builds everything from it before the rest of the scene wakes up: the **Unity Terrain**, its splat weights and relief occlusion, the water and deep-water volumes, the land beyond the rim, the start plateaus, ore fields and mirrored expansions, boulders, scenery, groves and bushes, and the NavMesh — under half a second on the MacBook Neo, the per-texel work on worker threads. Restart or return to the title screen and the next match gets another map. `-sfseed N` (or `MatchSettings.mapSeed`) fixes the seed; the benchmark and the AI evaluation stay on the default map so their numbers compare. The editor's map builder runs the same generator once, so the saved scene has the default map to look at and edit; untick *New Map Every Match* on the Map to play the saved one. Custom terrain material samples cliffs biplanar so rock strata follow the terraces; baked relief AO. Moss grows in patches with bare gravel between them, rock breaks through on the steep faces and high ground, and scree gathers below it. (Until this edition none of that showed: the splat weights were lost when the terrain asset was saved, so the whole map rendered, and grew grass, as moss. The editor's builder now writes them straight into the splat texture as its last step; a match's map is never saved, so there the weights are simply set.) Beyond the rim the land continues as a ring mesh that starts exactly at the terrain's edge height, is textured by the same splat rules and relief shading, and climbs into ridges, so there is no seam, cliff or colour change at the border; the fog of war follows the border ground out there instead of smearing its edge into streaks. Terracing walls every lake with cliffs, so stretches of shore picked by a slow noise field are slumped into sandy beaches running down into the water, and the rest keep their cliffs. |
| Vegetation | Groves of spruces, spreading broadleaves and slender poplars in the meadows, lone trees, dead snags on the bare ground, conifers on heights no unit reaches and bushes through the green, placed by the map generator and kept out of the bases, ore fields, expansions and narrow passes. A trunk stands on the same *Rubble* ground as a boulder: infantry, Diggers and Skimmers go round it, a Mauler drives through and knocks the tree down ahead of its hull. Explosions throw trees over away from the blast and flatten bushes, and set what they reach alight; fire spreads from crown to crown for a few generations and then dies out, chars the trees black (their crowns burn away, embers glow in the cracks, the grass round them scorches) and may bring a burned-out tree down. Felled trees lie where they fell for half a minute, then settle into the ground. The trees are grown in Blender by script (`Tools/blender/build_flora.py`): a trunk with a root flare splits into limbs and twigs by a seeded recursion and every twig carries a cluster of leaves, so a broadleaf crown is dozens of soft clusters with light between them and branches showing through; the spruce is whorls of drooping, ridged boughs. Each leaf cluster is exported with rounded normals, so it shades as one soft mass rather than showing its facets (the first trees, a few big jittered blobs, read as green rocks). A decimated copy of each is drawn beyond 70 m. All of it is drawn GPU-instanced by one shader (`SF_Tree`), a draw per kind for bark and one for foliage, casting shadows: leaves mottled with noise, warmer on top and cooler underneath, lit through from behind looking toward the sun, and swaying in a gust that travels across the map. Under fog of war the player sees each tree as they last saw it. A crown that comes too close to the camera shrinks out of the way. |
| Scenery | Rock spires and shelves, a crashed dropship and ruined pylons (`Tools/blender/build_env.py`), placed only where 80% of the footprint is ground no unit can use — cliff faces and the rim — and kept out of the NavMesh bake, so the map looks inhabited without a single path changing. |
| Ground cover | Grass and pebbles, grown at load from the terrain's own splat weights and drawn GPU-instanced per 32 m chunk. Blades are real geometry, not alpha-cut cards: `discard` would switch off the hidden-surface removal that keeps overdraw cheap on Apple GPUs. Wind is one slow travelling gust per clump; per-blade flutter twinkled at RTS distance. Blade attributes are sampled at the centroid: with MSAA, a blade seen almost edge-on was shaded from a point off the blade, and the extrapolated values blew single pixels up into sparks that bloom turned into flashing white lights. |
| Battle damage | A map-wide mask (`GroundMask`) records what the fight has done: flattened under structures and ore, charred where explosions and burning trees were (healing over minutes), churned along the tracks of moving units (fading in seconds). Grass bends, parts and burns on it; the terrain darkens scorched and churned soil. Tracked vehicles also leave tread marks, two cleated bands per Digger or Mauler, that fade over 40 s. Shells and wrecked vehicles dent the terrain itself: a shallow, smooth **crater** (35 cm for a shell), drawn as a scorched centre inside a ring of lighter thrown earth with ragged edges. Craters do not stack — the ground only goes down to the deepest single bowl covering it, so a spot shelled all match is a wide dip, not a pit (deep pits turned their walls into cliff rock) — and never open under structures or ore, or below the water line on dry land. The match works on a copy of the terrain data; grass, pebbles, rocks and trees settle into a crater, and units sink into it as they cross. Explosions also break the boulders they reach. |
| Navigation | **NavMesh** (AI Navigation). Structures and ore carve the mesh; Diggers skip avoidance so mineral lines never jam; Maulers slow down to turn instead of strafing. Units wade into water up to 1 m deep, at about half speed; deeper water is marked not walkable with box volumes, and a Skimmer skims over the shallows instead of diving to the bed. Boulders and tree trunks stand on a *Rubble* area that only Maulers may path through: a Mauler drives straight at a rock or tree and crushes it, and the NavMesh tiles under it are rebuilt in the background (on a copy of the baked data, so a match never edits the asset) so everyone can use the ground; the same happens when an explosion or fire brings one down. Structures need dry ground. |
| Models | Authored in **Blender** by script (`Tools/blender`) — the original ten, the Skimmer and Sentinel, the scenery, and the trees and bushes — exported to FBX with named material slots that map to URP materials. The exporter also bakes ambient occlusion into vertex colour R (cast against the whole model and the ground) and writes per-vertex shading data into G and B where a part asks for it (per ore shard, a random value and the height along the shard; per tree clump, a random value and how freely it sways), exports movable parts as child objects with their origin on the pivot, and writes a second FBX of pre-cut chunks for each structure. `Tools/blender/starforge_models.blend` has them all laid out for editing. |
| Unit surfaces | One custom lit shader (`SF_Unit`): the original armour photograph triplanar in object space as an x2 detail multiply normalised by its own average, edge wear found by screen-space curvature (bevels turn the normal fast, flat plates do not) and chipped by that texture, grime climbing from the ground and settling in cavities, a cool sky rim for silhouette. Ore is cut, glowing gem: a dark, saturated blue body under the glow, each flat facet at its own brightness so the shards read as faceted crystal, the tips burning brightest, a slow pulse of light climbing each shard on its own phase, motes of light drifting up off the seam, over a breathing pool of light on the ground. The ore models were rebuilt with longer, sharper terminations and a ring of small shards round the big ones. A Digger's hopper and drum windows are dark ore glass that lights up blue with the load it carries, the light rolling slowly round the drum, and a loaded Digger spills a pool of that light on the ground as it drives home. Per renderer it also carries damage charring with glowing seams, the construction hologram, the hit flash and burning — none of it with `discard`, which would cost early depth testing on every unit. |
| Animation | Parts move procedurally from the simulation's own state: legs swing with the stride, the gun and the tank barrels recoil on the shot, the Digger's arm dips and its cutter spins while mining, ore heaps up in its hopper and its drum turns while hauling, an ore seam's crystals shrink as it is mined out, the Foundry's control head sweeps and the Workshop's crane trolley travels. |
| Destruction | A destroyed structure is swapped for its pre-cut chunks as rigid bodies, thrown outward glowing hot, tumbling and settling on the terrain, then sunk out of sight. |
| Water | Its own shader (`SF_Water`) with no repeating pattern: three ripple layers at scales and angles with no common period, through UVs bent by a slow noise field, over three long analytic swells. It shows the lake bed through the surface from the camera's opaque colour copy, bent by the ripples (never picking up a unit standing in front of it), absorbed channel by channel along the path through the water, red first, with the water's own scattered light filling in: clear, green-tinted shallows over the sand, dark blue-green deeps. Depth drives foam crests that roll in toward the shore and lacy foam lapping at the waterline, and lights caustics on the bed in the shallows. Glints and ripples calm with distance so the surface does not sparkle at RTS range. The surface covers every hollow of the terrain below the water line (built from the 2 m generator grid, it had left dry pits beside the lakes). Units moving through the water leave a wake — rings dropped every metre or so that spread and overlap into a V, and foam that opens up and dissolves — and splash where they go in or come out; shells landing in the water throw up a white column and a spreading ring instead of earth. |
| Fog of war | One **URP full-screen render feature** reconstructs world position from depth and applies the player's visibility texture — explored ground dims and cools, unexplored goes dark, with a shimmer at the vision edge. |
| Atmosphere | The same pass integrates exponential height fog along the view ray (mist pools over the lakes and in the low ground, plateaus stay clear), distance haze and sun in-scattering, and refracts the image through each live blast ring. It already has every pixel's world position and the colour buffer, so all of it is free of extra passes. Cloud shadows are the sun's light cookie, a remapped cloud photograph drifting on the wind. |
| Occlusion | **SSAO** at half resolution, normals reconstructed from the depth texture the fog pass already needs, applied after opaques as a single multiply — the cheap path on a tile-based GPU. Its sampling noise is fixed per pixel: URP's default blue noise is re-rolled every frame for TAA to average out, and without TAA it made the grass boil. |
| Effects | Layered explosions: a white-hot flash, a fireball of several tinted flipbook puffs, dark smoke that billows while the fire burns and rises into a column as it dies (smoke, dust and mist are lit cloud puffs, `SF_Smoke`: four cauliflower clusters with their own normals, occlusion and ragged coverage, made by `Tools/make_smoke_puffs.py`, lit by the sun and from above so the tops of the billows catch the light and the folds stay dark, and eaten into wisps as they age — the photographed smoke sheet drew every puff as the same flat grey sprite), short hot sparks, small charred debris that trails fire and bounces off the terrain, a fountain of earth and a dust ring for ground bursts, drifting embers, a ground shockwave, a point-light flash, scorch marks and camera shake — with structures coming apart in a short chain of secondary blasts. Guns fire shaped muzzle blasts (flares drawn with the streaks): the Mauler's is a white-hot core in a long orange blast with jets from the muzzle brake, a cone of smoke dragged to a stop and dust kicked off the ground, and its shell flies as a glowing slug leaving a smoke trail. A Mauler crushing a boulder throws rock and dust and leaves a scorch; a tree coming down shakes out leaves and splinters and throws up dust all along the trunk where it lands; burning trees carry flames, smoke, embers and a flickering firelight. Plus a single instanced depth-decal shader for selection rings, footprints, order markers, scorch marks, tread marks and the light under ore, and instanced health bars and tracer streaks. |
| Audio | Synthesized at startup (the original had none): weapons, explosions, crystal shatter, alerts, ambience. |
| UI | **UI Toolkit** (`Assets/StarForge/UI`): HUD, minimap, command card, title (with *About the opponent*), pause and end screens, and the developer-only AI inspector. |

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
| Build ▸ 3 Map Scene | The map kit (terrain and water materials, scenery prefabs, plant kinds), lighting, post-processing, the camera, and the default map (seed 1000) built by the same generator a match uses, with its NavMesh |
| Build ▸ 4 Assemble Game Scene | World, AI bootstrap, camera, input, effects, ground cover, audio, HUD |
| Build ▸ 5 Record Shader Variants | After playing a match in the editor: saves the variants it actually rendered and registers them as preloaded shaders, so the player warms them at load instead of stalling on the first explosion |

Run later steps after earlier ones. **Step 3 regenerates the scene.** Every match
builds its own map anyway; to play a hand-edited map, untick *New Map Every Match*
on the scene's Map object and stop re-running step 3.

To regenerate the models (Blender 5.x):

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
  --python Tools/blender/export_fbx.py -- Assets/StarForge/Art/Models Tools/blender/starforge_models.blend
```

## MacBook Neo tuning

- A new map every match costs under half a second at load in the player: heights,
  splat weights, relief occlusion and the land beyond the rim are computed on
  worker threads, and the NavMesh bake of a 256 m map takes about 0.1 s.
- Forward+ rendering, 2× MSAA (tile memory on Apple GPUs), B10G11R11 HDR,
  two 2048 shadow cascades over 150 m, depth texture on, opaque texture on (the
  water refracts the lake bed through it), SRP Batcher.
- **Adaptive resolution** lowers render scale under sustained load with FSR
  upscaling, and backs off exponentially after failed climbs so it cannot
  oscillate (the lesson from the original's controller).
- **Three graphics presets** on the title screen, remembered between runs, each
  trading the three things that actually cost on a fanless laptop: how far
  shadows are drawn, how much ground cover is grown, and how far the adaptive
  controller may drop before it gives frames back. *High* is the measurement
  below; *Balanced* shortens shadows and thins the grass; *Battery* also drops
  MSAA and caps the render scale. Bloom is clamped, so a single overbright pixel
  cannot bloom into a light of its own.
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
- Switching between full screen and a window (the pause menu's button, or the
  window's own controls) always sets a resolution of the display's shape.
  Switching the mode alone had left a window the size of the display, which
  macOS shrank to fit under the menu bar (2816×1526); going back to full screen
  stretched that over the panel and the stretched size was saved for the next
  launch. A stretched size saved by an older build is corrected at launch, and
  the window can now be resized. `-sfscreentest` switches four times, logs what
  the player reports and quits.

**StarForge ▸ Build macOS Player (Apple silicon)** writes `Builds/StarForge.app`.
To benchmark the player:

```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 120 -sfbenchout /tmp/starforge_bench.txt
```

It plays AI vs AI with vsync off and writes average and percentile frame times.
`-sfquality high|balanced|battery` runs a preset without saving it. For visual
stability, `-sfburst /tmp/high.raw -sfburstat 8 -sfburstframes 60` (optionally
`-sfburstzoom 150`) holds the camera still and records consecutive final frames,
and `python3 Tools/analyse_burst.py /tmp/high.raw` reports and maps the pixels
that flicker: *vibrating* ones (up and down on consecutive frames) and *flashes*
(a pixel that turns bright for a frame or two), counting those on otherwise still
ground separately from water and moving units. On the same scene at *High*, the
grass fix took flashes on still ground from 137 to 7 in 120 frames (*Battery*,
without MSAA, had 38).

The app icon is `Assets/StarForge/Art/AppIcon.png`, made from `Assets/icon.png` by
`python3 Tools/make_app_icon.py` in the shape macOS 26 expects (a different shape
is shown shrunk onto a grey plate). The build re-registers the app with Launch
Services, since macOS otherwise keeps the icon it cached from the first build.

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

After the beaches, wading, crushable boulders, the new water, glowing ore and
tread marks, a 120 s run of the same benchmark averaged 17.7 ms (56.6 fps), with
p50 / p95 / p99 of 17.3 / 21.1 / 24.1 ms and a worst frame of 66 ms. With the new
weapon effects, the surround beyond the rim and the real splat map as well, it
averaged 17.7 ms (56.6 fps) again, worst frame 67 ms. With the trees and
bushes, craters, the refracting water (which turns on the opaque colour copy),
wakes and the new ore, it averaged 18.0 ms (55.6 fps), p50 / p95 / p99 of
16.7 / 33.1 / 33.4 ms and a worst frame of 67 ms: the median still holds 60 Hz,
but about one frame in twenty now misses it. With the grown trees and their
decimated far copies, the lit smoke, the gentler craters and the map built at
load (on the benchmark's fixed seed), it averaged 17.6 ms (56.8 fps), p50 / p95 /
p99 of 17.1 / 22.3 / 25.0 ms and a worst frame of 33 ms — back to where it was
before the vegetation. Building the map took 0.45 s of the load (terrain 89 ms,
textures 133 ms, objects 114 ms, NavMesh 112 ms).

Mauler shells did no damage at 60 fps before this edition: a shell's flight time
lands it on an aim point above the target's base, and it was retired there
without ever touching the ground, so it vanished without exploding. Only the long
frames of the AI evaluation's accelerated clock carried shells into the ground.
Shells now burst when their flight time is up.

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
