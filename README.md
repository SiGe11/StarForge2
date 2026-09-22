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

![The battlefield from above: scanned ground and cliffs, groves, a lake with reed beds, a burned-out grove and the ore fields](docs/overview.jpg)

The look starts from real surfaces and derives the rest. The ground, the cliffs
and the rocks are photo-scanned (CC0 scans from Poly Haven), the recorded sounds
come from Kenney's CC0 packs, the leaves are photographed leaf atlases from
ambientCG, the animals are Quaternius's animated models and the music is CC0
tracks from OpenGameArt; the trees and buildings are grown and built in Blender
by script, and the flame and hull textures and the ambience are made in Python. The ground grows its own grass and pebbles from
the terrain's splat weights and remembers where the fighting happened; the sun
casts drifting cloud shadows; mist collects in the low ground; units are worn
metal under a shader that finds their bevels by curvature rather than by a
hand-painted map; buildings print themselves into existence and come apart into
pre-cut pieces; and explosions throw lit debris that bounces off the terrain
behind a ring that bends the picture; wolves and foxes roam the meadows, flocks of
songbirds feed on the open ground and eagles circle overhead, all fleeing the
fighting and the fires, and the music moves with the match. It holds ~55–60 fps on the Neo.

![A base on the stony dirt, Diggers at the teal ore, birches and flowering shrubs behind](docs/base.jpg)

![A fire running along the lake shore: flame tongues through the spruces and the reed beds, charred trunks glowing in the cracks, smoke leaning with the wind](docs/wildfire.jpg)

![A flock of songbirds feeding on open ground beside an ore seam](docs/birds.jpg)

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
- **It does not play the same match twice.** Each match it opens differently and
  leans its own way: more Troopers or more Maulers, an early push or a late one.
  Its attacks go for different things (your base, your production, an outlying
  Foundry, your Diggers), come in straight or round a flank, gather before they
  go in, and sometimes split to hit two places at once. A plan that is not paying
  off is dropped, and a way in that you beat is not tried again soon.
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

A full run on the MacBook Neo after the AI learned to vary its play (Commander,
memory off, five matches per opponent on seeds 1000, 1037, 1074, 1111 and 1148,
600 s cap, on the default map):

| Opponent | Result | Read correctly | First correct read | Read as | APM avg / peak |
|---|---|---|---|---|---|
| Rusher | **5 won** (avg 385 s) | 26% | 205 s | harassing 43%, rushing 26%, macro 18%, turtling 12% | 34 / 204 |
| Macro | **2 won**, 3 time cap | 13% | 30 s | expanding 49%, turtling 35%, macro 13% | 29 / 240 |
| Turtle | 5 time cap | 77% | 86 s | turtling 77%, macro 12%, teching 11% | 33 / 240 |
| Harasser | **5 won** (avg 296 s) | 60% | 76 s | harassing 59%, rushing 22%, macro 20% | 34 / 240 |

**12 of 20 won, none lost.** The quick run just before (one match each, 420 s)
won two of four with no losses; earlier quick runs won at most one. Each match
drew its own style from its seed — four opened Skimmer-first and one fast-expand
(the five seeds happen to cluster; over twenty seeds the four openings come up
about equally, and in play the seed is new every match and the last two
openings are avoided), with the trooper share shifted between −0.13 and +0.16,
push timing between ×0.86 and ×1.26 — and its waves went for production, the
base and the worker line, mostly round a flank after gathering at a staging
point, from one wave in a quick win to eleven in a long game against the macro
player. The reads keep the aggressive/passive split — the rusher and harasser are
read as rushing or harassing and the turtle as turtling — but the rusher is now
read as a harasser more often than as a rusher: beaten back early by a varied,
attacking opponent, its waves look like raids from outside its base.

The shape matches the original's own evaluation: **aggressive versus passive is
read reliably** — the aggressive opponents are read as rushing or harassing and
the passive ones never are — while the finer label inside each pair is hard to
observe. A rush and a raid look alike from outside the enemy base, and the
scripted macro player's second Foundry makes it read as *expanding*. The APM cap
refused 2–6% of intended actions, and 12% against the harasser, where fighting is
constant.

## How it is built

| Area | Approach |
|---|---|
| Map | **A new battlefield every match.** When the scene starts, `MapRuntime` runs the original terraced-heightfield algorithm (renormalised fBm, terraces, rim mountains, carved connecting ramp) with a fresh seed and builds everything from it before the rest of the scene wakes up: the **Unity Terrain**, its splat weights and relief occlusion, the water and deep-water volumes, the land beyond the rim, the start plateaus, ore fields and mirrored expansions, boulders, scenery, groves and bushes, and the NavMesh — under half a second on the MacBook Neo, the per-texel work on worker threads. Restart or return to the title screen and the next match gets another map. `-sfseed N` (or `MatchSettings.mapSeed`) fixes the seed; the benchmark and the AI evaluation stay on the default map so their numbers compare. The editor's map builder runs the same generator once, so the saved scene has the default map to look at and edit; untick *New Map Every Match* on the Map to play the saved one. The ground is four photo-scanned surfaces from Poly Haven (CC0): mossy earth with small stones where the grass grows, grey-brown stony dirt for most of the map, lichen-streaked rock slabs on the cliffs and beach sand on the shores and dry ridges, each packed with its scanned height by `Tools/blender/pack_textures.py`. The custom terrain material blends them on that height, so stones stand out of the sand and moss fills the hollows between them, darkens the hollows the scans' colour leaves unshaded, samples each layer twice at different scales and angles swapped over in broad patches so nothing repeats in a grid from above, adds faint folds tens of metres across so broad slopes are not smooth as dunes, and tints moist hollows and shores darker and greener and exposed ridges paler. Cliffs are sampled biplanar so rock strata follow the terraces; baked relief AO. (Before, the whole map was one orange dirt photograph tinted four ways under a warm grade, with the photograph itself imported as its own normal map.) Moss grows in patches with bare gravel between them, rock breaks through on the steep faces and high ground, and scree gathers below it. (Until this edition none of that showed: the splat weights were lost when the terrain asset was saved, so the whole map rendered, and grew grass, as moss. The editor's builder now writes them straight into the splat texture as its last step; a match's map is never saved, so there the weights are simply set.) Beyond the rim the land continues as a ring mesh that starts exactly at the terrain's edge height, is textured by the same splat rules and relief shading, and climbs into ridges, so there is no seam, cliff or colour change at the border; the fog of war follows the border ground out there instead of smearing its edge into streaks. Terracing walls every lake with cliffs, so stretches of shore picked by a slow noise field are slumped into sandy beaches running down into the water, and the rest keep their cliffs. |
| Wind | Every match draws its own weather from the map seed: a heading, a strength (a still day or a blustery one) and its own gust rhythm, all pure functions of the match clock, so a seed blows the same way every time. Everything agrees about it. The trees lean and the grass ripples in gusts that travel across the map along the wind, the twigs flutter and each leaf card shivers on its own; the cloud shadows drift with it; smoke and embers lean downwind; fire runs downwind and moves fast in a gust and hardly at all in a lull; a tree leans with it as it falls, and a snag burned through at the foot goes over the way the wind pushes it. When it blows hard, leaves tear out of the crowns and are carried away downwind. |
| Vegetation | Groves of spruces, spreading broadleaves, slender poplars and twin-stemmed birches in the meadows, lone trees, dead snags on the bare ground, conifers on heights no unit reaches, bushes through the green, drifts of flowering shrubs, ferns in the shade of the groves and reed beds along the shores — 880 plants on the default map, placed by the map generator and kept out of the bases, ore fields, expansions and narrow passes. A tree closes the ground under it (the trunk, its low boughs and a unit's own radius) as *Rubble*, the same area a boulder stands on: infantry, Diggers and Skimmers go round it, a Mauler drives through and knocks the tree down ahead of its hull. (The footprint used to be the bare trunk; Rubble is an area, not an obstacle, so the NavMesh let a unit's centre right up to its edge and units walked half inside the trees.) Groves are solid woods, so each one is checked as it grows: if it would cut a base, an expansion or an ore field off from the rest of the map, it is not planted. A felled tree is a rod pivoting on its stump: gravity's pull grows as it leans, so it starts slowly, comes down faster the further it goes, and a tall tree takes longer to fall than a short one — measured, 2.4–3.1 s when a Mauler leans on it against 1.5–1.7 s when a blast throws it. How hard it was shoved sets how fast it starts (a shell close by against a snag that simply gives), the wind leans it on the way down, it comes to rest on its own boughs rather than flat on the ground, and it thumps, springs back and settles. If another tree stands in its way it hangs up in that crown part-way down and creaks there until it slips off — or until the tree holding it comes down too. The dust, the shower of leaves and splinters and the camera shake all follow the speed the crown was travelling when it hit. Explosions throw trees over away from the blast and flatten bushes, and set what they reach alight. **Everything green burns**, each at its own rate — dead snags and dry reeds readily, resinous spruces hard, green broadleaf slowly — and **the grass between them carries the fire**: a coarse fuel grid grown from the same meadow layer the ground cover grows on, burning cell to cell and lighting whatever is standing in it, which is how a fire crosses open ground from one stand of trees to the next. A fire runs downwind: it passes to what lies that way far more readily, and from further, than across or against it, so a blaze works along a meadow as a front rather than a circle. **Damp ground hardly takes at all** — the water level against the ground at a spot and five metres around it thins the grass's fuel and a plant's chance of catching, so shores, reed beds and the grass along a lake resist while the dry terraces above them go up. How far one fire gets is drawn when it starts, and each one has a budget: most are over in a handful of plants, a few run through dozens, and no single fire can take more than about a sixth of the map's growth. Several can: 30 fires lit one at a time on a test map took 1–59 plants each (median 5) and left 73% of the plants and 65% of the grass burnt. Burnt grass does not come back — black stubble on ashy ground for the rest of the match — and only 45 plants and 150 patches of grass burn at once. A burning plant is licked by tongues of flame from a generated flipbook (`Tools/make_flame_sheet.py`, drawn upright so they stay vertical) with a hot fire at the foot and now and then a rolling billow, under a column of smoke that leans with the wind — dark and thick while the leaves burn, thinner and greyer once only wood is left — and embers carried downwind; its firelight breathes and gutters, the wood chars black with embers glowing in the cracks, the crown burns away, and when it is out the black snag smoulders for a while with a thin pale wisp. The trees are grown in Blender by script (`Tools/blender/build_flora.py`): a trunk with a root flare splits into limbs and twigs by a seeded recursion, and the crown is **leaf cards** — photographed leaf sprays (oak, birch, beech and spruce, composed by `Tools/blender/make_leaf_cards.py` from ambientCG's CC0 leaf atlases, each leaf cut out of the atlas and laid along a twig with its own normal map) at every twig tip and over the crown's outline, cut out on their alpha and lit as one mass, over a darker solid canopy inside so the sky never shows through. The spruce is tiers of drooping needle-spray boughs round a dark core. (Earlier versions — jittered blobs, then grown clusters under a generated leaf texture — read as green rocks and green balls.) Birch bark is pale with dark marks, the flowering shrubs are tinted with pink blossom, and ferns and reeds are too small to cast shadows or to be drawn far away. A far copy of each, built with fewer, larger cards, is drawn beyond 70 m. All of it is drawn GPU-instanced by one shader (`SF_Tree`), a draw per kind for bark and one for foliage: warmer on top and cooler underneath, lit through from behind looking toward the sun, and swaying in a gust that travels across the map. Under fog of war the player sees each tree as they last saw it. A crown that comes too close to the camera shrinks out of the way. |
| Wildlife | Packs of wolves and lone foxes roam the meadows, flocks of songbirds feed in the open, and eagles circle overhead: Quaternius's animated low-poly animals (CC0, from OpenGameArt), coloured by the scene builder (their materials come plain grey) and playing their own walk and flight cycles. The ground animals walk only where a unit could — every step is checked against the NavMesh, so they go round trees, rocks and water — keep a few metres between packmates, wander from spot to spot and stop now and then. **Songbirds** live on the ground between flights: a flock feeds on open ground clear of the crowns, each bird hopping, pecking and looking round, until something comes near or it grows restless; then the whole flock flushes and flies low to another open patch with a small bird's bounding flight — a burst of wingbeats climbing, wings shut for a short fall, a burst more — and lands one by one. The bird is Quaternius's, re-posed and re-animated for it (`Tools/blender/build_songbird.py`): the model is a perched wagtail whose only cycle is a flutter of folded wings, so flying it as it came read as an upright dark blob flapping in place; it now has a flight cycle with its wings spread and beating, its legs tucked and its tail shortened to a finch's, a fold, a perch and a peck, and its colours come through one small palette texture (chestnut back, slate head, rusty breast, dark flight feathers with pale coverts that flicker as the wings beat — a plain brown bird disappears against brown ground from above). It is drawn about twice life size, or it would be a few pixels lost in the grass. Units coming close, gunfire, explosions, deaths and burning plants frighten them all: a pack bolts away and settles on new ground far from everything that has happened lately, a flock flushes and flies off downwind of the trouble, and eagles swerve away, climb and keep their distance while the danger lasts. Flyers hold a steady height over the highest ground under their whole circle and change it slowly, so they no longer bob up and down over every terrace. Pure scenery: nothing targets them, they block nothing and they do not burn; they are shown only where the player can see, and they take fright only at what the player could see too, so a pack bolting at the edge of the fog never gives away an unseen enemy. |
| Rocks and scenery | Boulders are six photo-scanned rocks (Poly Haven, CC0), decimated in Blender to 900 triangles each by `Tools/blender/build_rocks.py` with the scan's normal map keeping the detail, drawn by their own shader (`SF_Rock`) with dust settling on their lower flanks; each boulder on the map is one of them at random. Scanned crags and slab groups stand where procedural rock spires and shelves did, beside a crashed dropship and ruined pylons (`Tools/blender/build_env.py`), placed only where 80% of the footprint is ground no unit can use — cliff faces and the rim. They are kept out of the NavMesh bake, but the part of a piece that stands on usable ground is marked not walkable, so no unit walks through the edge of a crag or a wreck. **StarForge ▸ Debug ▸ Check Map Blocking** samples the NavMesh under every object: it also tests a ring just outside every trunk (where a unit's centre would put its body in the bark) and that the two bases are still connected. On the default map 22/22 scenery pieces, 55/55 boulders and 164 of 165 trees block ground units round the trunk, and the bases are connected (Maulers still crush boulders and trees). Boulders' Rubble is padded by a unit's radius too. |
| Ground cover | Grass and pebbles, grown at load from the terrain's own splat weights and drawn GPU-instanced per 32 m chunk. Blades are real geometry, not alpha-cut cards: `discard` would switch off the hidden-surface removal that keeps overdraw cheap on Apple GPUs. Wind is one slow travelling gust per clump; per-blade flutter twinkled at RTS distance. Blade attributes are sampled at the centroid: with MSAA, a blade seen almost edge-on was shaded from a point off the blade, and the extrapolated values blew single pixels up into sparks that bloom turned into flashing white lights. |
| Battle damage | A map-wide mask (`GroundMask`) records what the fight has done: flattened under structures and ore, charred where explosions and burning trees were (healing over minutes), churned along the tracks of moving units (fading in seconds). Grass bends, parts and burns on it; the terrain darkens scorched and churned soil. Tracked vehicles also leave tread marks, two cleated bands per Digger or Mauler, that fade over 40 s. Shells and wrecked vehicles dent the terrain itself: a shallow, smooth **crater** (35 cm for a shell), drawn as a scorched centre inside a ring of lighter thrown earth with ragged edges. Craters do not stack — the ground only goes down to the deepest single bowl covering it, so a spot shelled all match is a wide dip, not a pit (deep pits turned their walls into cliff rock) — and never open under structures or ore, or below the water line on dry land. The match works on a copy of the terrain data; grass, pebbles, rocks and trees settle into a crater, and units sink into it as they cross. Explosions also break the boulders they reach. |
| Navigation | **NavMesh** (AI Navigation). Structures and ore carve the mesh; Diggers skip avoidance so mineral lines never jam; Maulers slow down to turn instead of strafing. Units wade into water up to 1 m deep, at about half speed; deeper water is marked not walkable with box volumes, and a Skimmer skims over the shallows instead of diving to the bed. Boulders and tree trunks stand on a *Rubble* area that only Maulers may path through: a Mauler drives straight at a rock or tree and crushes it, and the NavMesh tiles under it are rebuilt in the background (on a copy of the baked data, so a match never edits the asset) so everyone can use the ground; the same happens when an explosion or fire brings one down. Structures need dry ground. |
| Models | Authored in **Blender** by script (`Tools/blender`) — the original ten, the Skimmer and Sentinel, the scenery, and the trees and bushes — exported to FBX with named material slots that map to URP materials. The exporter also bakes ambient occlusion into vertex colour R (cast against the whole model and the ground) and writes per-vertex shading data into G and B where a part asks for it (per ore shard, a random value and the height along the shard; per tree clump, a random value and how freely it sways), exports movable parts as child objects with their origin on the pivot, and writes a second FBX of pre-cut chunks for each structure. `Tools/blender/starforge_models.blend` has them all laid out for editing. |
| Unit surfaces | One custom lit shader (`SF_Unit`): hull plating triplanar in object space as an x2 detail multiply normalised by its own average — rectangular plates split the way hull panels are, recessed seams with a lit lip, rows of rivets, hatches and vents, grime in the seams, generated by `Tools/make_panel_texture.py` (the original's armour photograph it replaced read as brickwork at the units' scale) — edge wear found by screen-space curvature (bevels turn the normal fast, flat plates do not) and chipped by that texture, grime climbing from the ground and settling in cavities, a cool sky rim for silhouette. Ore is eerie, living crystal: veins of light seen deep inside each shard with two depths of parallax, so they shift against the surface as the camera moves and the crystal reads as a volume; a dark, glassy, near-black teal body between them; a spectral teal-green glow under a violet rim; a slow breath that swells the whole seam every five seconds on its own phase, with a wave of light climbing each shard; and slender needles bristling among the prisms. Motes of light rise off the seam and circle it as they go, more as it breathes in, over a pool of teal light on the ground that swells in time with it; a mined-out seam gives up its light in a burst of shards and motes that hang in the air. A Digger's hopper and drum windows are dark ore glass that lights up teal with the load it carries: the glow stirs while it cuts, swells in over a second or so once the load is aboard, flares as the hopper seals and fades out slowly at the drop-off (it used to switch on and off), the light rolling slowly round the drum, and the pool of light it spills on the ground follows the same curve. Per renderer it also carries damage charring with glowing seams, the construction hologram, the hit flash and burning — none of it with `discard`, which would cost early depth testing on every unit. |
| Animation | Parts move procedurally from the simulation's own state: legs swing with the stride, the gun and the tank barrels recoil on the shot, the Digger's arm dips and its cutter spins while mining, ore heaps up in its hopper and its drum turns while hauling, an ore seam's crystals shrink as it is mined out, the Foundry's control head sweeps and the Workshop's crane trolley travels. |
| Destruction | A destroyed structure is swapped for its pre-cut chunks as rigid bodies, thrown outward glowing hot, tumbling and settling on the terrain, then sunk out of sight. |
| Water | Its own shader (`SF_Water`) with no repeating pattern: three layers of the original's ripple map at scales and angles with no common period (a smoother synthesised ripple map was tried and dropped: its long waves looked worse), through UVs bent by a slow noise field, over three long analytic swells. It shows the lake bed through the surface from the camera's opaque colour copy, bent by the ripples (never picking up a unit standing in front of it), absorbed channel by channel along the path through the water, red first, with the water's own scattered light filling in: clear, green-tinted shallows over the sand, dark blue-green deeps. Depth drives foam crests that roll in toward the shore and lacy foam lapping at the waterline — broken up by noise into patches that come and go along the shore, and lit as a surface rather than a light, where it used to outline every lake with a glowing white stroke — and lights soft caustics on the bed in the shallows. Glints are broad and dim and ripples calm with distance, so the surface does not sparkle at RTS range, and the sky it reflects is slightly blurred so neighbouring ripples do not pick different patches of cloud. The surface covers every hollow of the terrain below the water line (built from the 2 m generator grid, it had left dry pits beside the lakes). Units moving through the water leave a wake — rings dropped every metre or so that spread and overlap into a V, and foam that opens up and dissolves — and splash where they go in or come out; shells landing in the water throw up a white column and a spreading ring instead of earth. |
| Fog of war | One **URP full-screen render feature** reconstructs world position from depth and applies the player's visibility texture — explored ground dims and cools, unexplored goes dark, with a shimmer at the vision edge. |
| Atmosphere | The same pass integrates exponential height fog along the view ray (mist pools over the lakes and in the low ground, plateaus stay clear), distance haze and sun in-scattering, and refracts the image through each live blast ring. It already has every pixel's world position and the colour buffer, so all of it is free of extra passes. Cloud shadows are the sun's light cookie, a remapped cloud photograph drifting on the wind. |
| Occlusion | **SSAO** at half resolution, normals reconstructed from the depth texture the fog pass already needs, applied after opaques as a single multiply — the cheap path on a tile-based GPU. Its sampling noise is fixed per pixel: URP's default blue noise is re-rolled every frame for TAA to average out, and without TAA it made the grass boil. |
| Effects | Layered explosions: a white-hot flash, a fireball of several tinted flipbook puffs, dark smoke that billows while the fire burns and rises into a column as it dies (smoke, dust and mist are lit cloud puffs, `SF_Smoke`: four cauliflower clusters with their own normals, occlusion and ragged coverage, made by `Tools/make_smoke_puffs.py`, lit by the sun and from above so the tops of the billows catch the light and the folds stay dark, and eaten into wisps as they age — the photographed smoke sheet drew every puff as the same flat grey sprite), short hot sparks, small charred debris that trails fire and bounces off the terrain, a fountain of earth and a dust ring for ground bursts, drifting embers, a ground shockwave, a point-light flash, scorch marks and camera shake — with structures coming apart in a short chain of secondary blasts. Guns fire shaped muzzle blasts (flares drawn with the streaks): the Mauler's is a white-hot core in a long orange blast with jets from the muzzle brake, a cone of smoke dragged to a stop and dust kicked off the ground, and its shell flies as a glowing slug leaving a smoke trail. A Mauler crushing a boulder throws rock and dust and leaves a scorch; a tree coming down shakes out leaves and splinters and throws up dust all along the trunk where it lands; burning trees carry flames, smoke, embers and a flickering firelight. Plus a single instanced depth-decal shader for selection rings, footprints, order markers, scorch marks, tread marks and the light under ore, and instanced health bars and tracer streaks. |
| Audio | **Weapons** are real firearms, from the CC0 Free Firearm Sound Library: every gun in it was recorded from beside the shooter and again from a distance, and a shot is mixed from both — the crack from the near take, the report rolling back off the ground from the far one a moment later — with a slap-back off the terrain behind. A Trooper's rifle is an AR-15, an SKS, a Savage and a Tikka (four takes, so a firing line is not one sample repeated); the Mauler's gun is a 12-gauge and a .30-06 pitched down an octave and rolled off above 900 Hz, which is what a gun that size sounds like from across a valley; the Sentinel's bolt and the Skimmer's plasma keep their Kenney energy sound with a real muzzle crack under it, which is what they were missing. Every take of a sound is levelled to the same loudness with its peaks rounded off, so a volley does not lurch about. **A tree** cracks and groans as it goes over and crashes with a rush of leaves as it lands (a felled tree, a tree creaking and wood breaks, all CC0); a bush flattened under a Mauler just rustles. The rest are Kenney's CC0 packs (crunching explosions over a low-frequency rumble for structures, glass for crystal, mining knocks, engine loops under moving Maulers and Skimmers, interface clicks and alerts), several takes of each picked at random and pitched a little; anything missing falls back to the sounds the game used to synthesise. **Ambience** is synthesised into seamless loops by `Tools/make_audio.py`: wind that gusts (stronger zoomed out), water lapping when the view holds a lake, a fire's roar and crackle near burning plants, and birdsong over the green that falls silent for half a minute after an explosion nearby. **Music** is recorded CC0 tracks from OpenGameArt, one mood at a time: *At Home* (wolfgang), *First Light Particles* (yoiyami) and *Contemplation* (Joth) — warm orchestral, piano over pads, and drifting ambience, played in turn — when nothing is happening; *Insistent* (yd), a dark, quiet loop, when armies gather or the enemy comes into sight; *Battle Theme A* (cynicmusic), strings and horns, while fighting is on screen. A mood has to hold a few seconds before the music follows it (quicker going up than coming down), the moods crossfade over several seconds, and every track is played at one measured loudness well under the effects. (The music used to be synthesised; its plucks and tremolo beeped. The calm set that replaced it was minor-key piano, which sounded creepy rather than peaceful under a quiet base; the tracks there now are major and consonant, measured with `Tools/measure_music.py`.) Everything is heard only if the player could see it. |
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
  Scripts/View     RTSCamera, PlayerController, FXDirector, AudioDirector, Fauna,
                   VegetationRenderer, FogOfWarRenderer, AdaptiveResolution
  UI/              UXML, USS, HUD controller, Painter2D elements
  Shaders/         terrain, rock, water, trees, creatures, fog of war, ground decals,
                   billboards, particles, smoke
  Audio/           Sfx (Kenney recordings), Ambience and Music (synthesised)
  Art/Textures/    Terrain and Rocks (packed scans), generated leaf, flame, water maps
  Editor/          generators: RenderSetup, SFMaterialLibrary, PrefabBuilder,
                   MapBuilder, SceneAssembler, BuildMac, AIEvalMenu, MapChecks
Tools/             fetch_assets.py (downloads the CC0 sources into Art/Source/, not
                   committed), make_audio.py, make_leaf_textures.py,
                   make_flame_sheet.py, make_smoke_puffs.py, make_panel_texture.py,
                   pack_fauna.py
Tools/blender/     model authoring + FBX export, pack_textures.py, build_rocks.py,
                   make_leaf_cards.py
```

### Third-party assets

Everything committed here is free to **redistribute**, not merely free to use:
the repository is public. The art and the audio are all CC0 (public domain
dedication — credited here anyway, because the people who made them deserve it);
the two font families are OFL 1.1 and Apache 2.0, whose licence texts travel with
them in [`LICENSES/`](LICENSES). [**THIRD-PARTY.md**](THIRD-PARTY.md) is the full
register: every source, its author, its licence, and which committed file came
out of it.

- **Poly Haven** (polyhaven.com): the ground scans *Coast Sand Rocks 02*, *Forest
  Ground 04*, *Aerial Rocks 02* and *Coast Sand 01*, and the rock scans *Rock Moss
  Set 01*, *Rock Moss Set 02* and *Boulder 01*. `Tools/fetch_assets.py` records
  each one's authors and real-world size in `Art/Source/polyhaven/manifest.json`.
- **ambientCG** (ambientcg.com): the leaf atlases *Leaf Set 014*, *016*, *019* and *024*.
- **Kenney** (kenney.nl): *Sci-Fi Sounds*, *Impact Sounds* and *Interface Sounds*.
- **Quaternius** (via opengameart.org): *Animals Pack* and *Animal Pack Vol. 2* (wolf,
  fox, eagle, songbird).
- **OpenGameArt** music: *At Home* by wolfgang, *First Light Particles* by yoiyami
  and *Contemplation* by Joth (the calm set), *Insistent* by yd and *Battle Theme A*
  by cynicmusic.
- **OpenGameArt** recorded sound the weapons and felled trees are built from: *The
  Free Firearm Sound Library* by Ben Jaszczak, Brian Nelson, Kevin Heras and
  Matthew Nanney (every gun recorded from beside the shooter and again at a
  distance), *tree chop fall thud* by kheetor, *Tree Creaking* by AntumDeluge
  (from a sample by Department64), and *100 CC0 metal and wood SFX* and *75 CC0
  breaking / falling / hit SFX* by rubberduck.
- **Fonts**: *Inter* by the Inter Project Authors (SIL Open Font License 1.1) and
  *Roboto Mono* by the Roboto Mono Project Authors (Apache License 2.0).

`python3 Tools/fetch_assets.py` downloads them into `Art/Source/` (ignored by
git, checked against Poly Haven's published md5s); the packers turn them into
what is committed under `Assets/`. `python3 Tools/check_licences.py` re-checks
every source page against the register, and fails if an image, model, font or
sound under `Assets/` is not accounted for by it.

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

To rebuild the art from its sources (the outputs are committed, so this is only
needed after changing a tool):

```bash
python3 Tools/fetch_assets.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/pack_textures.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/build_rocks.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_leaf_cards.py
python3 Tools/pack_fauna.py
python3 Tools/make_leaf_textures.py && python3 Tools/make_flame_sheet.py
python3 Tools/make_panel_texture.py
python3 Tools/make_audio.py
```

`python3 Tools/measure_music.py <track>` reports what a piece of music is doing
(key, how much of it is in a minor harmony, dissonance, roughness, note density,
how far it swells), which is how the calm set was chosen: warm and major, not the
dark minor loops it started with.

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

On Unity 6000.6.1f1 the benchmark changed shape: frames capped at 60 come out
quantised to whole refreshes (16.7 or 33.3 ms), and uncapped ones alternate
between about 9 and 60 ms, so percentiles no longer compare across versions and
the average is the number to watch. (The benchmark also turned out to have run
capped all along: `GameBootstrap` sets the play cap after the benchmark had lifted
it; it now lifts it again once everything has started, and `-sfcap60` keeps the
cap for comparisons with older builds.) With the scanned ground and rocks, the
new flora (880 plants, up from 507), the animals, the new fire, ore and water,
and the recorded audio and music, paired 90 s runs alternating with the build
from just before them, both capped at 60 on the same warm machine, averaged
18.99 / 18.79 ms against 19.17 / 18.31 ms: the same, within the noise. Getting
there took two savings: the terrain samples only the layers present at a pixel,
at the one scale in use (sampling all four layers at both scales had cost about
a millisecond), and ferns and reeds cast no shadows and are not drawn beyond
about 100 m. Uncapped on a cooled-down machine the final build averaged 17.3 ms (57.7 fps),
worst frame 99 ms; in the gallery run that reached a battle, the music's combat
layer was up for 21 s of it. Building the map now takes about 0.55 s of the load
(terrain 99 ms, textures 161 ms, objects 163 ms, NavMesh 130 ms). The Neo has no
fan, so back-to-back runs drift by a millisecond as it warms: compare builds in
alternating pairs. The round after that — fire in every kind of plant, the real
gun recordings, the felled-tree sounds and the ground-feeding songbird flocks —
cost nothing measurable: two uncapped 60 s runs averaged 17.4 and 18.6 ms, which
is the machine's own cool-to-warm drift.

After replacing the crowns with leaf cards, the deer with the animated
animals and the music with recorded tracks, alternating 90 s capped runs came to
19.35 / 19.59 ms against 18.89 / 18.30 ms for the build from before all of this
round's art: about a millisecond, of which the animals are about 0.3 ms
(`-sfnofauna` leaves them out for measuring) and most of the rest the leaf
cards, the one alpha-tested surface in the game.

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
