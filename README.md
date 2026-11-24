# StarForge — Unity edition

A real-time strategy game against an adaptive opponent that scouts, reads your
play style, changes its plan to counter it — and remembers you between matches.
Each side may also raise one **Mech Bay**, which calls down a **Mech** from orbit: a
war machine worth half a dozen tanks, assembled from a random draw of parts, with a mind of
its own — an ally that fights for you by its own judgement and tells you what it sees
([Mechs](#mechs)).

Note: This whole project is for testing Claude capabilities and Unity integration.


This is a rebuild of the C++/Metal StarForge (`~/repositories/StarForge`) in
**Unity 6 (URP)**, tuned for the **MacBook Neo** (Apple A18 Pro, 5-core GPU,
8 GB unified memory, 2408×1506 display). The game rules and the adaptive AI are
carried over faithfully; everything the engine can do better — terrain,
navigation, materials, effects, UI — is done with Unity's own tools instead of
being ported.

![The battlefield from above: scanned ground and cliffs, groves, a lake with reed beds, a burned-out grove and the ore fields](docs/overview.jpg)

The look starts from real surfaces and derives the rest. The ground, the cliffs,
the rocks, the trees, the shrubs and the ferns are photo-scanned (CC0 scans from
Poly Haven; the plants baked down to game size in Blender), the recorded sounds
come from Kenney's CC0 packs and OpenGameArt, the animals are Quaternius's
animated models and the music is CC0 tracks from OpenGameArt; the buildings are
built in Blender by script, and the flame and hull textures and the ambience are
made in Python. The ground grows its own grass and pebbles from
the terrain's splat weights and remembers where the fighting happened; the sun
casts drifting cloud shadows; mist collects in the low ground; units are worn
metal under a shader that finds their bevels by curvature rather than by a
hand-painted map; buildings print themselves into existence and come apart into
pre-cut pieces; and explosions throw lit debris that bounces off the terrain
behind a ring that bends the picture; wolves and foxes roam the meadows, flocks of
songbirds feed on the open ground and eagles soar in circles overhead, all fleeing the
fighting and the fires, and the music moves with the match. Played as a match
actually runs -- vsync on, 60 Hz -- it holds ~42 fps on the Neo's *High* preset -- the full
shadow distance and ground cover it had before the Mechs, at a render scale of about 0.76 -- and
about as much with Mechs fighting on screen (35 is the bar, and every frame it can spare above
that goes into a sharper picture).

![A base on the stony dirt, Diggers at the teal ore, birches and flowering shrubs behind](docs/base.jpg)

![A fire running along the lake shore: flame tongues through the spruces and the reed beds, charred trunks glowing in the cracks, smoke leaning with the wind](docs/wildfire.jpg)

![A flock of songbirds feeding on open ground beside an ore seam](docs/birds.jpg)

## The world

The Empire of the Brightest Star has kept its own peace for seven centuries, ruled in the
star's name by two hands that may never become one: the Khan, who holds, and the Holy
Mother, who blesses. Long before it, the Makers worked **starfire**, a craft that gave made
things a purpose of their own; that fire went out in a single night a thousand years ago.
Now surveyors on the emptied farm-world of Vesper have found that its seams of teal ore all
run back toward one origin, and concluded that the **Starforge**, the first hearth, is
there. The Empire's army is kept in equal wings so that no captain commands all of it:
two at the founding, four since a cautious Khan split them. Blue would find the Starforge
and relight it; Red would seal it cold; Green would carry it to the seat-world for both hands
to decide; Yellow would give it back to the knightly orders. Blue and Red landed first and
the other two a season later, neither ruler has said a word, and four quarters of one army,
under one oath, are fighting over a thing no living person has seen.

You command a field for the Blue Wing, against Red. The officer across it is served by a staff that
studied you in peacetime and amends its file on you after every engagement. And the knightly
orders in orbit, older than the Empire, owe each side that raises a gantry one knight and
one ancient core: the Mech.

The whole of it, with the rules for anything the game says or shows, is in
[docs/lore/starforge-lore.md](docs/lore/starforge-lore.md); its Old Language, the tongue of its
liturgy, oaths and mottos, in [docs/lore/old-language.md](docs/lore/old-language.md); the prompts
for its pictures and its intro clip are in [docs/lore/prompts.md](docs/lore/prompts.md).

### Night maps and the Night of the Quenching

About one map in three is fought at night (the map's own draw, and only in matches deployed from the menu: benchmarks, evaluations and trials stay by day). The match opens at dusk; at vespers, the first star, a bell rings, the camps light their lamps one by one, and a field celebrant in a white alb stands at each base. On about one night map in eight comes **the Night of the Quenching**: the lamps of both camps go out one by one and **nobody fires until lauds**, four minutes later, when dawn comes up and the fighting resumes. Soldiers keep moving and taking cover, and building and training go on; the opposing staff brings its army home for the night. Rules: `Scripts/World/GameWorld.Night.cs`; look: `Scripts/View/FXDirector.Night.cs`.

On about one night map in nine without a Quenching the Great Liturgy reaches its height mid-match: every lamp but the Foundries' goes out one by one and the light is passed out again from them, nearest first (the Taking of the Light; nothing else changes). By night each soldier's two field-net masts show a faint light. Deserters cut their leads first, so theirs are dark, and the field net reports contact lost.

The Mech's knight judges deserters with his order's repute leaning him (the Sable Oath almost always punishes, the Emberguard seldom does), and now and then the core's quiet line disagrees with the knight's call.

### The orders, the iron and a halved oath

The Field Manual has a page for the eight knightly orders, and the Chronicle tells the soldiers' lore (birth-stars, the Ninth Watch, Twins, the soldiers' words). **One soldier selected shows the names scratched inside their iron**: the wearer first, then those who wore it before. A fallen soldier's iron goes to the stores of whichever side had more soldiers within 15 m of the body, and that side's next recruit wears it; taken by the other wing it reads *painted-over*, and nobody reads out the names inside. It is told, not played: nothing in a match depends on it (`UI/IronLedger.cs`). **When both Mechs come from one order** the player's knight says so once and speaks soberly of the other's fall ("He was of my order: I carry his standard home"); the match itself is the same match (`AI/MechBrain.cs`). Checks: `LoreTrials.Iron`, `Orders`, `IronField`, `Halved` (`Editor/LoreTrials.cs`).

## Quick start

1. Open this folder with Unity **6000.6.10f1**.
2. Open `Assets/StarForge/Scenes/Battlefield.unity` and press Play.
3. Press any key at the title, choose **Skirmish**, pick an opponent in *Operation setup* and **Deploy**.

Everything generated (render pipeline, materials, prefabs, the map scene) can be
rebuilt from the **StarForge** menu; **StarForge ▸ Build All** runs every step in order.

## Menus

The title sits over the live battlefield, the camera drifting through slow shots of the
new map. The splash and the loading screen carry key art, and the opponent appears as a hologram
for each of its three tiers (on Operation setup, in the field manual and in the after-action
report): pictures the author made for StarForge with an image model (Google Gemini; see
THIRD-PARTY.md). The built game opens on a ten-second clip of the key art set in motion (made
the same way; any key or click skips it, and `-sfnointro` leaves it out). Arrows or WASD, Enter and Esc drive every page (Q and E or Tab switch tabs), and so
does the mouse.

- **Skirmish** opens *Operation setup*: the battlefield from above with both bases and the
  ore, the opponent (Recruit, Veteran, Commander, with its actions a minute and reaction
  time), the starting game speed, and **New map** for another battlefield.
  **Observe** is the same page for two AIs.
- **Field manual**: every unit and structure from the game's own data (cost, what trains it,
  what it needs, hit points, damage a second, range, speed, sight), how the opponent thinks,
  the keys, the **Chronicle**: the world in twenty-three short entries, six of them with a
  painted plate (made like the key art), and **The Orders**: the eight knightly orders, each with
  its standard, its motto in the Old Language, its teaching and its repute.
- **Service record**: victories, defeats, win rate, time in command and the last eight
  engagements. Only matches started from Operation setup (and their rematches) count:
  never spectated ones, ones played with the cheat, or anything the benchmark, the
  evaluation or a trial runs.
- **Settings**, saved as they change: edge panning, camera pan and turn speed, starting game
  speed, the frame-rate readout; the graphics preset and full screen or window; master,
  music and interface volume. *Reset to defaults* puts back how the game came.
- **Credits**: the people and the free assets behind the game.
- **Esc** in a match opens the pause menu (resume, settings, field manual, restart,
  surrender, main menu, quit), with a situation report; anything that loses the match asks
  first. The match ends in an **after-action report** (both sides' ore, units, structures,
  kills and losses), with *View battlefield* to look round the field before a rematch.

## Controls

| | |
|---|---|
| **Camera** | `W` `A` `S` `D`, arrow keys or push the pointer to a screen edge · `Q`/`E` rotate · wheel zoom · middle-drag · `Space` centre on selection · `Ctrl`+`Option`+`1`–`4` save a camera spot, `Option`+`1`–`4` go back to it (StarCraft II's `F5`–`F8` too, with `fn` on a MacBook; `Ctrl`+`F5`–`F8` saves where macOS does not keep it for keyboard navigation) · `Tab` the latest alert (again: the ones before) · `Backspace` the next Foundry |
| **Select** | Click · drag a box · double-click or `Ctrl`+click selects that type on screen · `Shift` adds · `F1` next idle Digger (`Shift`+`F1` all of them; `fn` on a MacBook; or the IDLE button, `Ctrl`-click for all) · `F2` whole army |
| **Groups** | `Ctrl`/`Cmd`+`1`–`0` assign · `Shift`+`1`–`0` add the selection · `1`–`0` recall (double-tap to centre) · the bar over the selection: click selects, double-click goes there, `Shift`-click adds to the selection |
| **Orders** | Right-click: move / attack / mine / rally · double right-click: hurry · `R` attack-move · `C` stop · `H` hold |
| **Build** | Digger selected: `B` basic structures (`F` Foundry · `B` Bunkhouse · `G` Garrison · `N` Sentinel · `V` Workshop) · `V` advanced structures (`U` Armory · `L` Airfield · `O` Biolab · `Y` Mech Bay) · `X` or `Esc` back |
| **Train** | Foundry `U` Digger, `J` Hauler, `K` Prospector · Garrison `T` Trooper, `K` Skimmer, `F` Scorcher, `M` Thumper, `N` Marksman, `B` Rocketeer, `G` Gunner · Workshop `M` Mauler, `L` Lancer, `F` Aegis, `T` Ironclad, `K` Mech-Hunter · Airfield `G` Kestrel, `F` Swift · Biolab `Z` Skitter, `T` Spitter, `V` Razorwing, `G` Gorehorn · `X` cancel. With Diggers in the selection the card shows only their builds |
| **Armory** | The Armory selected on its own: `T` Infantry Weapons · `G` Infantry Armour · `V` Vehicle Weapons · `B` Vehicle Plating · `N` Creature Venom · `M` Creature Carapace · `K` Mech-Hunter Programme · `X` cancel |
| **Mech Bay** | The bay selected on its own: `V` Servo Actuators · `B` Ablative Armour · `N` Weapon Overcharge · `M` Extra Hardpoint · `K` Targeting Uplink · `X` cancel · click the Mech strip (top left) to find the Mech |
| **Other** | `P` pause (no orders go in until you resume) · `,` `.` game speed, kept through a pause · `Esc` unwinds placement → build card → selection → menu |

The command card shows every command for the current selection with its hotkey
and ore cost; hover for supply, build time and a description. It holds twelve
cards in four columns of three. A Digger's structures are on two cards of their own,
as in StarCraft (basic: the economy and the core army; advanced: the tech); with all
nine and the orders on one card the last row ran off the panel.

**Testing cheat** (not shown anywhere in the game until you use it): `Ctrl`/`Cmd` +
`Shift` + `M` during a match adds 5000 ore and 100 supply (past the 200 ceiling) with every
press, and turns 5x training on, then off, then on. A small red CHEAT MODE stays on screen
from the first press to the end of the match, and the match is not folded into the AI's
memory of how you play.

And you cannot win that way. Where the enemy's last structure and Digger would have given you
the match, CHEAT MODE grows and blinks, 60% of your units desert for the camp (aircraft cannot,
so what walks makes up their share), and every animal on the map, red and a third bigger,
comes for what stays loyal: nothing can target or hurt them, a bite is 600 damage, they leave
the deserters alone, and they do not stop until you have lost.

## Units

| Unit | Cost | Supply | HP | Role |
|---|---|---|---|---|
| Digger | 50 | 1 | 60 | Mines ore, constructs structures |
| Trooper | 50 | 1 | 55 | Cheap ranged infantry |
| Mauler | 150 | 3 | 180 | Tank; long-range splash artillery (38 m), outranges Sentinels and riflemen; drives through boulders and trees, crushing them |
| **Skimmer** *(new)* | 75 | 2 | 75 | Fast hover raider, wide sensors, double damage vs Diggers |
| **Scorcher** | 75 | 1 | 95 | Flamethrower infantry: 6 m jet that burns every enemy on the ground in it; strong against infantry, Diggers and creatures, weak against armour; sets grass alight |
| **Thumper** | 100 | 2 | 70 | Mortar infantry: lobs shells 26 m over cover with splash; cannot fire inside 7 m or at aircraft (needs an Armory) |
| **Marksman** | 125 | 2 | 55 | Sniper: 30 m, one shot fells a rifleman; slow to fire, fragile, poor against armour; shoots at aircraft (needs an Armory) |
| **Rocketeer** | 90 | 2 | 90 | Anti-armour infantry: a shoulder rocket (19 m, 1.6x against armour) that wrecks tanks, Mechs and buildings; poor against infantry, cannot fire at aircraft |
| **Gunner** | 110 | 2 | 80 | Machine gunner (Garrison, needs an Armory): a belt-fed gun that pins whole squads from 40 m and also fires at aircraft, but must stop to fire and is slow to reload; little use against armour or walls |
| **Lancer** | 175 | 3 | 280 | Tank destroyer: armour-piercing cannon, 1.5x against armour, wasteful against infantry |
| **Aegis** | 150 | 3 | 200 | Anti-aircraft tank: flak that bursts round aircraft (2.2x against them), light work against ground targets |
| **Ironclad** | 300 | 6 | 480 | House-wars walker (Workshop, needs an Armory): twin shoulder cannons against the ground, missile pods against aircraft; slow, very hard to stop |
| **Kestrel** | 175 | 3 | 150 | Gunship (Airfield): flies over cliffs, water and woods, rockets ground targets; cannot fire at aircraft |
| **Swift** | 125 | 2 | 110 | Interceptor (Airfield): the fastest thing in the sky, made to hunt aircraft. A plane: it never hovers, but makes firing runs -- in along its nose, over the target, out and round for the next -- and circles where it is sent (on Hold or idle it makes runs only at what comes near that spot); strafes the ground for little |
| **Hauler** | 125 | 1 | 280 | Armoured ore carrier (Foundry, nothing else needed): slow and costly but light on supply (about 1.6 Diggers' ore for one supply), 20 ore a trip, shrugs off raids; cannot build |
| **Prospector** | 100 | 2 | 150 | Armed ore truck (Foundry, nothing else needed): six wheels and armour, a drill under the nose and a machine gun on the cab that fights back at its work; 5 ore a trip, a poor miner; cannot build |
| **Mech-Hunter** | 250 | 3 | 160 | Mech killer (Workshop, needs an Armory and its Mech-Hunter Programme): a light four-wheeled vehicle under the order's flag, with a coil lance (60 a shot, 0.35x against light targets, 0.6x against armour) that does 4.2x its damage to Mechs and Ironclads (an Ironclad in two shots, a bare Mech in about seven) and little to anything else; thinly armoured and easily destroyed, and an enemy Mech shoots it first. One in the field a side at a time, and a 75 s wait after it falls |
| **Skitter** | 35 | 1 | 45 | Bred swarmer (Biolab): cheap and very fast, bites what it reaches; cannot reach aircraft |
| **Spitter** | 90 | 2 | 100 | Bred acid-spitter (Biolab): hops into range, hits ground and air |
| **Razorwing** | 150 | 2 | 130 | Bred flyer (Biolab): fast raider with a scything sting; hits ground and air |
| **Gorehorn** | 275 | 5 | 520 | Bred armoured beast (Biolab, needs an Armory): charges in and sweeps its tusks through everything round what it strikes; cannot reach aircraft |

| Structure | Cost | HP | Provides |
|---|---|---|---|
| Foundry | 400 | 1500 | Trains Diggers, ore drop-off, +10 supply |
| Bunkhouse | 100 | 400 | +8 supply |
| Garrison | 150 | 1000 | Trains Troopers and Skimmers |
| Workshop | 200 | 1250 | Trains Maulers (needs a Garrison) |
| **Sentinel** *(new)* | 125 | 500 | Defensive turret (needs a Garrison) |
| **Mech Bay** *(new)* | 100 | 1800 | Calls down the side's Mech; gun tower, self-repair, repairs the Mech, fits its upgrades. One a match, never rebuilt ([Mechs](#mechs)) |
| **Armory** | 150 | 900 | Research: weapons (+10% damage) and armour (-10% damage taken) for infantry, vehicles and creatures, two levels each; and the one-off Mech-Hunter Programme (200 ore, 50 s); unlocks the Thumper, the Marksman, the Ironclad and the Gorehorn (needs a Garrison) |
| **Airfield** | 200 | 1100 | Builds the Kestrel and the Swift (needs a Workshop) |
| **Biolab** | 175 | 1000 | Breeds Skitters, Spitters, Razorwings and Gorehorns (needs a Garrison) |

**Aircraft and creatures**: Kestrels, Swifts and Razorwings fly over cliffs, water, woods
and rocks. Only guns that can shoot upward reach them -- Troopers, Marksmen, Prospectors,
the Aegis, the Swift, the Spitter, the Razorwing, Sentinels, the Mech Bay's gun and every
Mech weapon but the mortar and the flamers, and the Ironclad's missile pods; shells
bursting on the ground do not. A downed aircraft falls where it was. The Biolab's
creatures are Quaternius's animated spider, frog, wasp and cow made monstrous in Blender
(`Tools/blender/build_creatures.py`: fangs, tusks and horns, spined backs, armoured
carapaces, glowing glands, all moving with their own cycles), coloured for their side, and
throw themselves into a blow: the Skitter darts in to bite and springs back, the Spitter
throws its head into the spit, the Gorehorn drives its head down and tosses its tusks
through a sweep (a crescent drawn through its reach), the Razorwing swoops on its prey;
a bite rakes three quick streaks across what it struck;
they bleed rather than burn, and neither they nor aircrews ever desert. **Upgrades** from the Armory are a side's for good once
researched; two Armories can research two lines at once, never the same one.

**Balance of the wider roster** is measured, not guessed (`Editor/RosterTrials.cs`). In
equal-ore duels (600 ore a side, every pair of the 17 fighting types) each unit wins some
and loses some, mostly 7-10 of 16: Rocketeers and Lancers break armour, Scorchers and
Thumpers break crowds of infantry, the Aegis and the Swift break aircraft, Skitter swarms
overrun tanks and the Ironclad, and the Ironclad -- the strongest single unit -- was cut
twice until Rocketeers, Spitters and Skitters beat it. Per 100 ore spent a Digger mines
260 ore a minute, a Hauler 171 (but 213 per supply, and it shrugs off raids), a
Prospector 77 (an armed truck, a fighter first). In the AI evaluation (20 matches against the four
scripted players, which keep to the original units) the opponent AI using the whole
roster won all 20, lost none (17 of 20 before), trained every new type but the Aegis (the
scripted players field no aircraft) and researched 4-5 Armory levels a match. Raided from the
air instead (AI against AI on seed 1000, the other side given extra Kestrels from 200 s, topped
up to six and sent at its base), it trained 6 and 4 Aegis tanks and 4 and 1 Marksmen in two
runs; the same match without the Kestrels, none of either. Quick evaluations on
the final code (after the review fixes) won 3 of 4 (the Macro game running to the 720 s cap
with both Mechs standing) and, with the capes in, 4 of 4; none lost, 0 exceptions. The
evaluation's five seeds draw only two of the AI's four openings; three more seeds that draw the
other two (Standard twice, EarlyGarrison once) won all 12 matches against the four scripted
players, 0 exceptions.

**Terrain** *(new)*: units wade into shallow water (up to 1 m deep) at about half
speed, Skimmers skim over it at full speed, and deeper water stops everyone.
Boulders block everything except Maulers, which crush them and open the ground
for all.

**Veterancy** *(new)*: units rank up at 2, 5 and 10 kills (+15% damage and +10%
health per rank) and slowly repair once out of combat. **A soldier may earn a cape**: at
each promotion a Trooper, Thumper, Scorcher, Marksman or Rocketeer without one has an 81%
chance of one (so about 96% wear one by their second stripe, 99% by the third; the
Prospector, an armoured ore truck, does not). It hangs from the shoulders
under the pauldrons with the pack worn over it and falls nearly straight to the ankles in a
few broad folds, placed differently on every soldier: woven cloth in a deep shade of the
side's colour, edged down both sides and along the hem with an embroidered trim whose colour
marks the rank (dark, bronze, gold), a palmette in each lower corner. The fabric texture (`Art/Textures/cape_fabric.png`)
was made for this project by the author with an image model. Near the camera it is real cloth (Unity's `Cloth`), swinging as the soldier
walks and stops and blown by the match's wind; further off, past 24 at once, for a second
after a stutter, or in the AI evaluation's 8x it hangs as a rigid cape that sways back as he
moves, and when he falls it bunches up on his back. It is only for show and is hidden with
the soldier by the fog of war.

**Targets**: a unit sent after something (right-click, or attack-move onto it) stays on
it. One that picked its own target -- idle, holding, on attack-move, or a Sentinel or a
Mech Bay's gun -- switches to an enemy within reach when the one it picked is out of
reach, and lets it go once it leaves its sight; once the target it was sent after falls,
what it picks next is its own. Reach is measured to the edge of what it shoots at, so a
big structure is fired on as soon as its walls are in range. Something that fights back comes
first, and closing on one, a unit trades it only for another that does: a squad going for
a Sentinel does not stop to shoot the Foundry beside it, and a tower shoots the rifleman
firing at it before the wall behind him. (They used to keep a target until
it died: a tower that caught a scout at the edge of its sight held on to it and stood
silent, whatever came up to its walls, and a squad whose target fell chased the next
thing it saw across the map.)

**Diggers without a Foundry** keep their load and wait until one stands again, then bring
it in. One sent to ore with a full load goes there and waits; one on its way in when the
last Foundry falls stops where it is rather than turning back into whatever took it. (They
used to mine on and drop each load for the next, stripping the ore fields for nothing.)

**Morale** *(new)*: a side that is losing badly may see a few of its soldiers break and
run. It is rare: nothing breaks in the first five minutes, a side breaks at most once a
match, and it takes all of these at once -- the side's army worth under 70% of the
enemy's (the Mechs are left out of that sum), a minute in which it lost at least one and a half times what it dealt, and a fight
going badly right now: two or more of its soldiers under fire together, outgunned more than
1.4 to one round them, two or more of them dead there in the last 30 seconds. Each such
crisis gets a single roll of the dice (15-35%, worse the more lopsided the war). In the AI
evaluations (20 matches each, AI against scripted opponents) a side reached a crisis 5-17
times a run; with higher rolls soldiers deserted in 15 of 80 matches, almost always on the
side being beaten, and at the shipped rules in 6 of 80 -- about one match in thirteen. Most of
those were a lone soldier shot in the fight he broke from before he got clear, which is why
the rear of a fight now breaks before its front: staged, 19 of 20 deserters got clear, against
14 of 20 before. Those who
run are about one in seven of the soldiers in that fight (one to four): Troopers before
Skimmers before Mauler crews, the wounded first (but not the dying), the rear of the fight
before its front and not the soldier being shot at that moment, rank-1 veterans seldom,
rank-2 and rank-3 veterans and Diggers never. They make for the corner of the walkable ground behind their own
base -- usually 40-70 m from its middle and at least 25 m clear of its buildings (up to 85 m
where that corner is built over or cut off), near it but not in it -- and stay there: they
take no orders and no longer count against supply. They fire only when an enemy attacks one
of them near where they sit, and never go more than 14 m from their spot to do it; while
still running they do not stop to fight. You are told: a warning ("2 Troopers are deserting
-- running for the south-west corner") with the alert sound, pale pulses on the minimap
where they broke and where they ran, a dashed pale ring round the camp, and a pale health
bar over each deserter (its status reads *Deserted · takes no orders*). They drop out of
group selections and control groups; click one to look at it. An enemy desertion is
announced only if you can see the fight, and its camp ringed only while you can see it.

**The Mech and deserters**: a few seconds after soldiers of its side desert, the Mech gives
its verdict, whatever it is doing at the time -- even standing in its bay's gantry being
mended -- and says so on the comms (a Mech still coming down when they ran does not judge
them). The verdict comes from its pilot's temper (a cold pilot
punishes more often), pride, and mood (how the war and its own hull are going: the worse,
the harsher). If it means to punish them it goes when it can spare the time -- 60% hull or
better, out of a fight, the base not under attack, the enemy Mech not close -- and turns
back if it is called away (saying so the first time); it goes again when it is free. Too
hurt to go but otherwise free, it tops up at its bay first; if no moment comes for five
minutes it lets them be, and says that. At the camp it
speaks to them, and its guns -- only its guns, never anyone loyal -- treat them as enemies
until they are dead; then it tells its side it is done (or, if the enemy got to them first,
says that). After two and a half minutes of chasing them it gives up. The deserters do not
fire on it. If it lets them be, it may say that too. Only your own Mech's words reach you:
the enemy's Mech decides the same way, silently. In an AI evaluation every desertion
whose side still had its Mech got a verdict (four: two "go", two "let them be"), and one of
the punishments put its deserter down; a side with no Mech left lost its deserters to the
enemy, usually within a minute.

**The match** is lost by the side with no structure and no Digger left; if both go in
the same moment it is a draw.

All numbers live in `Assets/StarForge/Data/Units/*.asset` and can be tuned in the
Inspector; rebuilding prefabs keeps your values.

## Mechs

Each side may raise one **Mech Bay** (Digger, `Y`; 100 ore, no prerequisites). Seven
minutes after it stands (it was two until 2026-11-04), a **Mech** comes down from orbit beside it on its retro-rockets:
a seven-to-nine-metre war machine worth a company of tanks -- enough to turn a fight, not to
win the war alone. Each side gets **one Mech and one
Mech Bay a match**: a bay that falls is never rebuilt, and a Mech that falls is never
replaced. Destroy the enemy's bay before its Mech lands and it never comes -- and with seven
minutes to do it in, that is a real plan: until the drop the bay, with its own gun tower, is the
one thing of theirs that matters, and the count over it ("Mech drop in 7:00") shows how long
you have. AI against AI on the default map (seed 1000) the bays stood at 2:04 and 2:11 and the Mechs
landed at 9:07 and 9:15. A bay
called off before it stands does not count, and nor does a site shot down before any
Digger started work on it: the bay may be placed again (the ore of a site shot down is
gone). A structure's site appears as soon as it is ordered, so a bay ordered into the
enemy's base from afar is usually shot down before its Digger gets there.

![A blue tracked Mech opens fire up a ravine at a red four-legged Mech standing over a destroyed Sentinel, the wood between them burning](docs/mech_fight.jpg)

**The Mech is an ally, not a unit.** Its pilot is a knight of one of the eight orders, which
swear to neither wing but owe one knight and one ancient core to any side that raises a bay
(its gantry). Its banner is your side's cloth with its order's standard sewn on, and the same
device stands beside its name in the HUD (a Mech-Hunter flies the standard of whichever order
licensed it); it fights on your side by its own judgement and takes no
orders -- you can select it and read its status, but not command it. Its own AI sees the
whole battlefield and acts as fast as it likes (about 1,200 actions a minute, where the
opponent AI is held to a human's hands). Yours is a guardian, cautious and meaning to live:
it defends the base when it is attacked, walks with your army when your army attacks, hunts
what it can beat cleanly, meets the enemy Mech when the odds favour it (and draws it onto
your guns when they do not), keeps out of reach of anything it outranges, and walks back to
its bay to be repaired when it is hurt -- more defending than attacking, and attacking when
the moment calls for it. **The opponent's Mech is a mind of its own with the same
judgement**: just as careful, but each match with a temper of its own -- steadier, warier or
a little bolder in when it goes in and when it turns for repairs -- so the two never play
as each other's copy. It says nothing you can hear: what it sees, it passes to its own
side's commander, a call and a place.

Yours **talks to you**. A comms panel (top left) carries its **suggestions**, tagged as
such -- attack ("their army is out in the field -- strike their Foundry now"), defend ("3
Maulers and 5 Troopers moving on your Workshop from the east") and pull back -- its
**warnings** and **reports** (the enemy Mech on the field, going for repairs), each with its
tag, and a call it has made lately about the same place is not repeated. In between, and
kept rare -- one every few minutes at most, never on top of a call -- it keeps your spirits
up, set apart in a quieter, quoted style: the voice of an old machine that has seen more
wars than you have (in the world's own telling, the ancient core the knight is sworn to),
about five hundred lines, a hundred of them about the world it remembers and two that only
a Mech of its own order says, chosen for the
moment -- a fight won or lost, at home or on their side and at what cost, the battle
turning, being surrounded or seeing the base taken, a quiet spell, the end of the match --
by whether you are winning or losing, attacking or defending, and how it is going. A line
that names something (their retreat, their shells, being surrounded, the dead) comes far
more often when it is true, no line repeats until its situation has run out of new ones,
and each pilot has its own temper: some warm, some cold, some prouder than others. Click
a suggestion to look at the place it names; whether you act on it is up to you. The strip above the
comms shows its hull, shield and what it is doing; click it to find the Mech.

**Every Mech is different, and fitted out for the fight it drops into.** It is built from a
fixed list of parts, spending a fixed budget of 100 points (whatever a design cannot spend on
parts becomes extra armour, so no draw is simply weaker). Only the pilot is known when the bay
goes up. The parts are picked when the drop is called, nine seconds before it lands: the
pilot's order draws a dozen designs and weighs each against what the enemy fields at that
moment -- a crowd of rifles calls for fire and splash, a column of tanks for lasers and the
railgun, an enemy Mech for armour-piercing guns -- then picks by weighted chance rather than
always the top score, and avoids repeating the other side's Mech. The order sees the whole
field, as the Mech's own AI does; the opponent AI never reads it.

| Part | Options |
|---|---|
| **Locomotion** | *Strider* (reverse-jointed legs; balanced) · *Arachnid* (four legs; slow, steadiest platform, +8% range) · *Juggernaut* (twin tracks; toughest, slow to turn) · *Wraith* (grav skirt; fast, skims over shallows, lightly armoured) |
| **Frame** | *Vanguard* (light, 3 hardpoints) · *Citadel* (line, 4) · *Colossus* (siege, 5) |
| **Weapons** | Autocannon (all-round bursts) · Rotary Cannon (hoses infantry) · Missile Rack (homing salvos onto a crowd) · Siege Mortar (longest reach, useless up close) · Beam Cannon (burns through armour) · Inferno Projector (an arm flamer: sets infantry and the field alight) · Railgun (a slug through everything in its line) · Flame Tower (a flame turret on its own ring, on a shoulder or the back: it turns on its own and burns what comes close, all the way round) |
| **Utility** | Shield Projector · Repair Nanites · Reactive Armour · Sensor Mast (or none) |

Arm guns aim with the torso; launchers on the shoulders and back fire upward and shoot
whichever way it faces, and the Flame Tower swings round on its ring onto whatever comes
near, behind the Mech as readily as in front. Its guns are for killing what fights back: against structures they
do under half their damage, except the Siege Mortar's, so a Mech in your base gives you
time to answer it rather than flattening the Foundry in the seconds it takes to kill a tank. Every weapon picks its own target several times a second -- the
rotary cannon and the flamer go for infantry, the laser and the railgun for armour, the
launchers for a crowd.

**The Mech Bay** guards itself with a gun tower, mends itself (quickly once left
alone), repairs the Mech standing in its gantry -- whole again from a bad beating in
about a minute, and only a trickle while it is being shot at -- and sells the Mech's
upgrades, bought by you (or the opponent AI), never by the Mech. An upgrade still in the
works when the Mech or the bay falls is refunded:

| Upgrade | Levels (ore) | Effect |
|---|---|---|
| Servo Actuators `V` | 2 (50, 90) | +15% speed and turning a level |
| Ablative Armour `B` | 3 (60, 95, 130) | +5% hull and 2% less damage taken a level (plates appear on the Mech) |
| Weapon Overcharge `N` | 3 (60, 95, 130) | +5% damage a level |
| Extra Hardpoint `M` | 1 (120) | Mounts one more weapon on an empty or auxiliary mount (the tooltip says which) |
| Targeting Uplink `K` | 1 (70) | +6% range, +10 m sight (a dish appears on its back) |

**Where you raise it** is up to you -- anywhere a Digger can build, the enemy's base included --
and both AIs treat a bay in a strange place as the gamble it is. The opponent goes for a bay (or
a Sentinel) going up beside its base with whatever army it has, and for a finished one once its
army can take on the gun and whatever stands guard beside it, rather than feeding it a few rifles
at a time; meanwhile it moves its
Diggers to ore the gun cannot reach, and avoids expanding under it. Your Mech gets the same
respect: Diggers it is seen among are moved to ore out of its reach, or sent home when there is
none (straight away from it when it, or a tower or bay of yours, stands in the way); none is
trained at a Foundry it stands by;
no Digger is sent to mine, finish a half-built structure, expand or look for ore where it was seen
in the last half minute, and they go back to work once it has been gone that long. Your Mech is
mended only at a
bay on your half of the map, or at one where nothing out-guns it; if it keeps losing hull in a
forward bay's gantry it says so and stays away for a while, and hurt with no safe bay to go to,
it falls back to your lines. With its bay destroyed it fights on -- no repair is coming, so
waiting gains nothing -- taking only long odds as its hull falls. It guards your base, not a bay
far forward.

`BayEdgeCases` (Editor/MechTrials.cs) checks ten cases on the default map, AI against AI (every
number in this passage was measured with the old two-minute drop; the trial keeps it for both sides'
bays in every case, since its 420 s runs are shorter than the game's seven minutes). A
Digger walked into the enemy base at a minute and a half was shot as it began work, and its site
fell within four seconds; 45 m outside, the Digger lasted 15-18 s and the bay was never finished
(without the opponent's answer to a site it took 19-39 s, and one bay in three was finished and
cost them 4 Diggers and 13 soldiers). A bay put down whole 12 m inside their base at 30 s --
sooner than a Digger could raise one -- fell 109-124 s later in six of the eight runs made while
this was worked on, all but the last before its Mech came down (that one landed four seconds
before the bay fell); in the other two, on earlier versions, the gun shot their economy away
first and the Mech that came down there finished them. On a lake shore 66 m from their base the Mech lived through all five runs (it had died in
three of five while it sat in the gantry being shot from 45 m). In a corner, a wood and far out
on a flank the bay stood and the Mech lived. A bay dropped into their base at four minutes with
its Mech landing beside it at once fell 20-176 s later; a tower of ours put up inside their ring
of buildings as well lasted 7-62 s. Since the Mech was cut down (Balance, below), a finished bay
dropped 44-66 m outside their base at 30 s usually holds: their Mech used to break it at 196-311 s.
At first the opponent sent its whole army at anything near that bay and fed it away a handful at
a time; it now leaves what stands guard beside a bay or tower at the edge of its base alone until
its army can take all of it (anything that comes into the base itself it meets at once), and no
longer loses those matches outright. A bay a Digger
tries to raise there is still shot down before it stands. The opponent won 10 of 16 such runs within the seven minutes
and was behind at the end of two. How it answers a Mech among its own buildings (straight away
with what it has, or only with an army big enough to beat the Mech) hardly ever comes up: its
towers have shot the intruder down before the choice arises. No run raised an exception.

**Your army counts against a Mech.** Three rules make sure what an army does to one sticks:
- **Battle damage tells.** Below 60% of its hull a Mech's guns reload slower, up to 45%
  longer at 15% (it fires about a third slower). Select it and its card says so -- yours or
  theirs ("Battle-damaged: firing 22% slower"). A Mech at a fifth of its hull used to fight as
  hard as a fresh one, so nothing an army did counted until the last shot.
- **The bay mends slowly.** A Mech back at 38% needs about a minute in the gantry to be whole
  (12 hull a second, under 2 while it is being shot at; on the hull before the cut below that
  was 22 and 6, and before that 50 and 15, when what an army had cost it was gone in twenty
  seconds).
- **Upgrades are steps, not leaps.** Ablative Armour and Weapon Overcharge give 5% a level, the
  Extra Hardpoint mounts one more gun, not two, and the Targeting Uplink adds 6% range, not 12
  (out-ranging a crowd of rifles by that much was worth more than any other upgrade against
  them). With every upgrade bought -- which the opponent does by mid-match -- a Mech used to
  take 2.4 times the tanks and well over twice the rifles a bare one did.

Measured with the duel below on six designs, before the cut described under Balance, each fight on a freshly loaded map (fought one
after another on one site, the burnt grass, craters and felled trees of the earlier fights
skewed the later ones), against the old rules in the same session: a fully upgraded Mech now
takes **26 Maulers or 61 Troopers** on average to break (19-35 / 39-98 by design), where it took
**41 Maulers and more than 100 Troopers** (25-50; five designs of six beat the 108 the trial
goes up to); a bare one takes the same tanks as before (17) and fewer rifles (37, was 45:
battle damage slows it now, and a crowd of rifles wears it down long enough for that to tell;
it is also the noisiest measure there is, a design's number moving by a fifth from one run to
the next). So upgrades now take a Mech to about 1.6 times its bare strength, not 2.4 or more. A
first measurement had looked better against rifles than this: a gun's reload threw away the
time past its end, so a battered Mech's flamer and rotary cannon -- reloading every tenth of a
second -- lost half their fire at the evaluation's speed; the reload now carries that time
over, and the numbers above are with it. They hold at the duel's and the evaluation's tenth of
a second a step; at a played frame rate the rotary cannon gets its full 11 rounds a second, a
tenth more than the duel saw (a gun fires at most once a step), and the flamer its full ten,
about a sixth more than it managed in play before the fix (the duels already saw ten).

**Balance.** A bare Mech is meant to fall to six or seven Maulers that come at it from out of
its reach, or to about ten Troopers. A Mech Bay costs 100 ore, and a Mech that took twenty-one
Maulers -- 3,150 ore of tanks -- decided matches on its own while the armies round it looked
on. So the whole Mech was cut to an army's measure (`MechParts.HullScale` 0.55 and `GunScale`
0.28): every frame's hull, and with it the shield, the nanites and the gantry's quiet repair, to 55%,
and every gun's damage to 28%. Damage was cut harder than hull: it still takes a beating like
a war machine but no longer erases a squad in seconds, and a fight between the two Mechs lasts
long enough for the armies round them to tip it. That is measured, not guessed: `MechTrials`'
duel (Editor/MechTrials.cs) stands a Mech with no upgrades on a flat, open corridor of the
default map and sends N Maulers or N Troopers at it from 55 m, beyond the reach of most of its
guns, fought to the end, bisecting for the smallest N that kills it, each fight on a freshly
loaded map. Over twelve drawn designs that is **6.3 Maulers** on average (4-8 by design: a
light frame on legs or a grav skirt falls to four or five, a heavy frame on tracks to eight; a
second pass gave 6.4) and **10.2 Troopers** (7-13; bisected to two, so up to one high). It took two passes: hull 45% and damage 24%
was a little too weak (5.0 Maulers), 55% and 32% a little too strong (7.2). Every upgrade at
its top takes six designs from 5.2 to 8.2 Maulers (1.59x) and from 9.2 to 16.3 Troopers
(1.78x): three tanks or seven rifles more. At the old prices (2,600 ore) that was a sixth of
what the ore buys of either, so the upgrades were repriced with the Mech: all of them now cost
900, about twice what they add, as before the cut. In matches the armies now decide: in two AI evaluations on the cut (20 matches each
against the four scripted players) the AI won 19 of 20 both times and lost none (the
twentieth both times the macro player's hardest seed, which ran to the time cap with the
scripted side's army gone), the Mechs made 0-34 kills a match where they had made 40-159,
the scripted side's Mech fell in 15 and 18 of the 20, and most of the macro player's games
ended at 9-11 minutes instead of running to the cap with its Mech holding its bay.

Before that cut a bare Mech was tuned to be worth 20-25 Maulers or 30-40 Troopers: over twelve
designs **21 Maulers** (20.7: 16.8 for the first six, lighter designs, 24.5 for the other six,
mostly heavy frames) and **40 Troopers** (36.8 and 43), each fight on a freshly loaded map, on
the rules above that make an army count (before them, fought one after another on one site:
21 and 36). In that earlier run the Flame
Tower's designs took 29-45 Troopers. Adding it reshuffled what the generator draws, and the first survey after it found
the arm flamer overtuned against a crowd: every design above 45 Troopers carried it, one at
86 (a heavy frame on a grav skirt with the flamer, a rotary cannon and two launchers), so it
does less to infantry now and plays a narrower cone. The spread is what the parts are for: a
light frame on a grav skirt is a raider that a tank company can bring down; a heavy frame
on tracks with a mortar and a flamer holds off a battalion of rifles. It took six rounds
to land, each a full re-run: the first cut was far too strong against infantry (70-110
Troopers), because splash and anti-infantry bonuses compound against a crowd -- so every
weapon now does much less to light targets than to heavy ones, a railgun slug loses a
third of its punch through each body it passes and stops after four, and the Arachnid's
range bonus came down from 12% to 8%. A duel site with 9 m of relief measured the
terrain rather than the Mech (a Mauler's shell flies almost flat, and a rise eats it), so
the trial now looks for flat ground the whole way. And fixing two things that made the
Mech *worse* than it should have been -- its flamer reached 12 m, less than a rifle, and
never fired; its arm guns held targets outside the torso's arc -- made it stronger again,
and the numbers came down a final notch after. Upgraded, it grows: every upgrade at its top (2,600 ore)
takes it from 17 to 26 Maulers and from 37 to 61 Troopers on six designs -- nine tanks or 24
rifles more, about half what that ore buys of either (41 Maulers and over 100 Troopers before
the round above made an army count; the first cut of the upgrades, +15% a level and a
cheaper second hardpoint, was worth more still). The duel is the
open field; in play a Mech is worth more, most of all at home. With its own AI kiting what
it outranges, the bay's tower beside it and the gantry mending it between waves, a Mech
holding its base in the AI evaluation took 65-168 kills before the match ran out, and in seven of
the nine matches that ran to the cap the defending Mech still stood. The gantry was
slowed for that (a Mech back at 38% is now whole in about a minute, not under twenty
seconds), and the opponent AI learned not to feed one: bring enough, and bring your own. In the
latest run three of the four matches that reached the cap still ended with the defender's
Mech standing (128-146 kills), but the opponent's Mech fell in 13 of the 20.

**Physics.** A tracked Mech rides on its tracks: the hull settles onto the ground under both
runs, bridging the small bumps, and it steers for its path like a heavy vehicle, easing into a
turn and out of it. (It used to snap to four ground samples and rock and bob as it drove like a
tumbler toy, and on a side slope it leaned the wrong way, so its uphill track sank into the
hill -- measured over the same routes, a track was more than 5 cm into the ground in 92% of
frames and up to 1.6 m deep; now never more than 5 cm. Its roll rate is about half what it was
and its heading no longer swings back and forth about once a second.) A Mech wades through a wood knocking the trees over ahead of it (and one that
comes down in a wood throws the trees under it outward as it lands, so none stands through
it), crushes
boulders, and presses fallen trunks into the ground. Every footfall kicks up dust (or
water), leaves a footprint and sends a ripple through the grass; heavy blasts throw the
infantry round them off their feet. It lands in a burst of dust that flattens the grass for
fifty metres and hurts whatever it lands beside; it dies in a reactor blast, its torso
heaved off the waist, its guns torn away and its legs buckling, and burns for half a minute.

![A four-legged Mech on a ridge above a lake, over the burning, charred wreck of a Mauler it has just destroyed, with fallen infantry behind it](docs/mech_ridge.jpg)

**Sound.** Every footfall is a heavy steel plate meeting the ground -- a low punch, a crunch
of stones and the leg ringing -- with a servo whine as the leg swings. The guns are built the
way the Mauler's is, from real recordings in the CC0 Free Firearm Sound Library: the
autocannon from a 1917 rifle, the rotary cannon from a PPSh, the mortar from a 12-gauge an
octave and a half down with a steel tube ringing in it, each with a falling thump under the
crack and the report rolling away; the laser, the missiles and the railgun lay Kenney's CC0
sci-fi sounds over a real muzzle crack. The drop is heard coming -- a spacecraft engine
and retro-rocket thrusters swelling for several seconds before touchdown -- and it lands with a
crash of plate and rock under a long thump. A tracked Mech gets the tanks' engine and track
voices, and every message on the comms comes with a radio chirp. Measured, like the rest of
the game's sound, so it carries on a laptop speaker: footfalls centre at 220-320 Hz, the
autocannon at 300-430, the rotary cannon at 800-1,100, the railgun at 700-930, the laser at
2.5 kHz, and the mortar at 155 Hz with 55-59% of its energy under 120 Hz -- the same family
as the Mauler's cannon (158-192 Hz, 41-58%), the deepest sounds in the game.

## The opponent AI

*This is what a player needs to know; the **Field manual** (from the main menu or the
pause menu) shows the same text under *The opponent*.*

- **It plays by your rules.** It sees only what its own units see, gives orders
  through the same commands you do, and has a limited number of actions a minute:
  about 90 on Recruit, 180 on Veteran and 330 on Commander, with reactions from
  nearly a second down to a fifth of one.
- **It scouts.** It keeps a scout out whenever its picture of you is going stale,
  and a Skimmer is its favourite for the job; a Digger goes out only to find your
  base, never to look at it again (or, with its own ore running out, to look at an
  ore field it has not seen yet). Kill the scout or hide your army and it has to
  guess.
- **It reads you.** From what it has seen it decides whether you are rushing,
  harassing, turtling, expanding, teching or building a big economy, and picks a
  plan against that: an early Trooper rush, raids on your Diggers, a timing push,
  a Sentinel wall while it techs to Maulers, a second ore line, a counter-attack
  while your army is away, or a feint. When its picture of you changes, so does
  its plan.
- **It uses the whole roster.** Each match it leans to armour, aircraft or the
  Biolab's creatures, raises an Armory and researches weapons and armour for whatever
  it fields most, and answers what it has seen of yours: Aegis tanks, Swifts,
  Spitters and Marksmen against your aircraft, Lancers and Rocketeers against your
  armour, Ironclads and Gorehorns to break your line, Scorchers against massed infantry, Thumpers and Maulers against your towers, and
  gunships or Razorwings where little of yours can shoot up (and Gunners once its Armory
  stands). It adds Haulers to its
  ore lines from four minutes, and Prospectors -- armed ore trucks -- while you raid
  them. Once it knows of your Mech, its bay or your Ironclads it buys the Armory's
  Mech-Hunter Programme and keeps a Mech-Hunter in the field.
- **It fights like a player.** It focuses fire on the weakest target that can
  shoot back and lets one that runs go rather than chase it across the map, pulls its
  army home when a fight turns against it and gathers it again before going back in,
  sends raiders at your Diggers rather than your army -- the most exposed ones, resting
  a while when driven off -- and comes back to defend when you hit its base. When the
  ore by its Foundries runs low it saves for a new one at another field, it raises a new
  Foundry if it loses its own, and once it has lost track of everything of yours it
  searches the ore fields and base sites it has looked at least lately, then the open
  ground. Once every Foundry of yours it has seen is down and nothing of yours it knows
  of is a match for its army, it goes after what is left with whatever army it has.
  A Mauler whose shells keep landing short -- on a ridge between it and its target,
  or where a moving target used to be -- is moved after three misses from the same
  spot, to the nearest place round the target from which the shell's actual arc
  reaches it. A Mauler's shell flies almost flat (at full range it climbs about a
  metre over the line), so a low rise is enough to stop it. Staged behind a ridge
  from a rifleman: without the move it put 21 shells into the ridge in 45 seconds,
  every one about 9.6 m short, and never touched him; with it, three missed, it
  drove round, and the next shell landed 0.3 m from him.
- **It does not play the same match twice.** Each match it opens differently and
  leans its own way: more Troopers or more Maulers, an early push or a late one.
  Its attacks go for different things (your base, your production, an outlying
  Foundry, your Diggers), come in straight or round a flank, gather before they
  go in, and sometimes split to hit two places at once. About half the time it keeps
  a pair of Skimmers raiding your Diggers whatever else it is doing. A plan that is not
  paying off is dropped, and a way in that you beat is not tried again soon.
- **It has a Mech too.** It raises a Mech Bay early (its Mech lands seven minutes
  later), buys its Mech upgrades from spare ore once that Mech is two minutes from
  landing -- guns and hardpoints when it means to attack, armour when it means to
  hold -- and goes for your Mech Bay while your Mech has not yet landed. Its Mech is
  not under its command any more than yours is under yours (it fights as carefully as
  yours, with a temper of its own): it listens to the calls its Mech sends it,
  silently, a kind and a place, and sometimes acts on them -- meeting a raid its Mech saw coming,
  striking when its Mech says the moment has come -- and sometimes does not. Around
  your Mech it keeps its army together and puts everything it has on it, and it does
  not feed it: it waits for an army that can break your Mech (less once it has seen
  your Mech hurt, less again with its own Mech going in), each attack your Mech throws
  back makes it wait longer -- after two it stops trickling in and masses a far bigger
  army, or as big as it can get, before its next planned push -- and it does not
  forget your Mech just because it has not seen it lately. When it sees your Mech
  badly hurt it strikes at once, before your bay can mend it. It builds against your
  Mech once it has seen its guns -- tanks
  against one made to burn infantry, a swarm of rifles against one made to kill armour
  -- strikes your base when your Mech is out in the field far from home, going for
  your Mech Bay then (it is what would mend your Mech), and gathers its attacks on its
  own Mech so that they go in together. A Mech Bay or a tower raised beside its base it
  treats as an attack: it goes for one still going up with whatever it has, clears a
  finished one once it has the army to take the gun and whatever stands guard by it, and
  keeps its Diggers off ore its gun covers
  (and out from under it, if they stopped there with a load when their Foundry fell).
- **It remembers you.** Between matches it keeps a record of how you tend to play
  and which of its plans worked against you, and each new match starts from that.
  It still scouts every game, so if you change how you play, it will notice.
  There is no switch for this in the game: the opponent learning you is the game.

**The Night of the Quenching.** On the night maps where every lamp goes out it keeps the truce like everyone else: no shot until lauds; it calls a wave home, holds its army at its post (meeting, without firing, anyone who comes within 55 m of its base) and starts the next wave after dawn.

### How it works

The AI is the original's architecture, ported line for line and extended:

```
Perception ──► OpponentModel ──► StrategySelector ──┬──► Macro
 (fog-limited   (Bayesian filter    (contextual       ├──► Scouting
  memory)        + behaviour         bandit over       ├──► Tactics
                 profile)            8 plans)          └──► Micro (focus fire)
```

The rules above are enforced in code, not just promised:

- **Fog of war.** It reads enemy units only through `GameWorld.Sees` (whether its side has spotted
  them, as yours does) and ground through the same per-team visibility grid that drives your fog. It guesses your base from
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
- **Observe** (AI vs AI) from the main menu.

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
- the **memory toggle** and **"Forget me"** in *Operation setup*, and
  the memory state in the top bar;
- the **AI's dossier** in the after-action report: how it read you and which plans it used.

**StarForge ▸ Evaluate AI** plays the adaptive AI against four scripted
archetypes (rusher, macro, turtle, harasser) in the real scene and reports win
rate, how often and how fast it identifies each one, the plans it chose and its APM.

A full run on the MacBook Neo with the Mechs (Commander, memory off, five matches per
opponent on seeds 1000, 1037, 1074, 1111 and 1148, on the default map; the cap is now 900 s,
since a Mech holding its base makes matches longer and 600 s left most of them undecided):

| Opponent | Result | Mechs | Read correctly | First correct read | Read as | APM avg / peak |
|---|---|---|---|---|---|---|
| Rusher | **4 won** (avg 795 s), 1 time cap | theirs fell in 3 | 32% | 124 s | harassing 32%, rushing 30%, turtling 20% | 39 / 216 |
| Macro | **2 won** (avg 815 s), 3 time cap | theirs fell in 1 | 10% | 30 s | expanding 53%, turtling 26%, rushing 10%, macro 10% | 37 / 228 |
| Turtle | **5 won** (avg 737 s) | theirs fell in 4 | 70% | 82 s | turtling 69%, teching 20%, macro 11% | 37 / 228 |
| Harasser | **5 won** (avg 465 s) | theirs fell in 5 | 60% | 75 s | harassing 60%, macro 15%, teching 12%, rushing 11% | 36 / 240 |

**16 of 20 won, none lost** (14 before the balance round that made an army count against a
Mech, 12 of 20 before the Mechs at a 600 s cap); the AI's own Mech survived every match, the
opponent's fell in 13 (11 before that round), and no match raised an exception. Of the four
that ran to the cap, three were against the macro player, whose Mech still held its base at
32-61% hull with 128-146 kills to its name and 9-12 structures behind it, and the fourth ended
with the rusher down to its Mech Bay alone, its Mech gone: a Mech defending its home, with the
bay's tower beside it and the gantry mending it between waves, is the hardest thing on the map
to crack, and the scripted opponents turtle behind theirs. Of its Mech's calls to attack,
defend or pull back the AI acted on 42 and ignored 18; it struck while the enemy Mech was away
from home 28 times, and gathered its waves on its own Mech 78 times. Both sides' Mechs were
fitted out at the drop against what the other fielded. No soldier deserted in that run
(under the first morale rules each side reached a crisis once). Ten Full runs since the morale
rules were retuned (see Units, Morale) came to 13-16 wins with one loss in all, in a match where
nobody deserted on either side; the last four, at the shipped rules: 13, 16, 15 and 15 wins, no
losses, a soldier deserting in 2, 2, 1 and 1 matches; and with the opponent's Mech on a temper of
its own and silent (see Mechs), 15 wins and no losses, its Mech alive in all 20 and its calls
heard by its commander in all 20. (The evaluation reuses five seeds against every opponent, so its morale
rolls are now drawn per opponent: from the seed alone, every match got one of five fixed
sequences of rolls, and at the shipped odds none could break before its third crisis.)

**Reading a rush** (2026-11-19). A rusher who sends on whatever he has just trained arrives a few units at a time, as a raid does, and the AI read
the scripted rusher as "harassing" more often than "rushing". It now also counts what has come onto its half of the map over the last
minute and a half, each unit once, and a steady stream reads as a rush. A full run on that (Commander, 900 s cap, the same five seeds):
**20 of 20 won, none lost**, no exception; the rusher read correctly 63% of the time (31% in the run the evening before on the same
code without it), the harasser 71% (71%), the turtle 84% (85%), the macro player 11% (13%: once its second Foundry is seen it reads as
"expanding", which the AI answers the same way).

An audit of the AI (2026-11-22) found that since the combat rewrite its waves were almost never booked as beaten: in 48 logged
evaluation matches 141 waves ended in 3 retreats and 91 call-offs, so the pause to regroup and the massing against a Mech that keeps
winning never came on. A wave now pulls out as beaten when it is down to under half of the most it was worth with the enemy still on
the field, or ends under its plan's threshold having lost three tenths of that; its retreat is run, not bounded from cover to cover.
The AI also remembers for a few seconds a soldier who has gone to ground in plain view (seeing the ground is no longer seeing the
man), waits out its reaction time before turning its fire or a raid on what it has just seen, and reads you at the same pace on every
difficulty. Its Mech weighs its own side's guns against what actually stands there (a Mech-Hunter beside it had counted as five tanks
against riflemen), and counts a gun's fire by its magazine and reload (a Rocketeer had counted as nineteen tanks against a Mech). A
full run on these rules, but for one later one-line fix (Commander, 900 s cap, the same five seeds): **19 of 20 won, none lost**, no
exception (rusher 5, macro 5, turtle 4, harasser 5 of 5), 14 of 79 waves booked as beaten where there had been none; a call home or a
change of plan still ends most waves (46), and the massing never began. With every fix in, on Unity 6000.6.10f1, a second full run
won 16 of 20, none lost, four ending at the time cap. One thing tried was left off: holding reinforcements back to send them as
groups. A full run with it on gave 16 of 20, none lost; on the turtle's five seeds the like-for-like pair won 4 of 5 either way, at
766 s on average with it and 675 s without, which five matches cannot tell apart, so the old way stays. So did the old plan
doctrine: a reworked one that gives every plan its turn cut Timing Push from 49% of the AI's time to 29% and brought the unused plans
in, but against the turtle it spent 84% of the late game feinting and won 1 of 5 where the old one won 2. With a feint held to
one minute and then rested three, feinting fell to 23% and the turtle was beaten 4 of 5 times, but expanding then took the macro
player's late game and two of those matches ran to the time cap. It stays an option for testing, off.

Runs vary: the same twenty seeds gave 13, 15, 12, 15 and 11 wins in the round that added
the Mechs, 15, 10 and 12 in the round that added the loadouts picked at the drop, the
Flame Tower and the anti-Mech tactics below, and 14 once its enemy memory
counted a clump of units as the units in it and it expanded when its ore ran low, 16 (the
table above) after the Mech balance round, and 15, 14 and 13 after it, the last two (2026-10-21)
once it stopped forgetting your Mech between looks and kept side raids going, and the third once
a Mech stopped hunting up to the other's reach and turning back (no losses in
any), because a match's frame timing is not the same twice. That last round: forgotten like a
rifleman 50 s after the last look, the opponent's Mech had dropped out of the AI's reckoning
4-27% of the time it was alive, and 36 of 78 waves into it in five matches against the macro
player started while the AI believed it gone; now 0-10% and 2 of 141 in the whole run. A pair
of Skimmers raided Diggers beside the plan in 8 of 20 matches (none before: the plan was never
Harass against these opponents), and a fix to how its orders queue for the APM cap (a loop went
on asking past an empty budget, and each pass's refusals were counted again) took the refused
share from 7-37% to 1-2%. So a single change is judged over a whole run, not a match, and a
difference of two or three wins is noise. The first cut of the "strike while their Mech is
away" window called its wave off whenever their Mech went out of sight for a while, and the
army walked to their base and back every nine seconds (54-104 such waves in a match); a wave
through the window now presses on until their Mech is seen back near its base, then pulls
out. Two things came out of those
runs. The AI fed a defending Mech one wave at a time (22 waves in one match, into a Mech
on 156 kills); it now waits for a bigger army after each wave that Mech throws back, and
does not attack a Mech-held base while its own Mech is being repaired. And its own Mech
stood beside those waves without a word -- a third of its time "their Mech has the
edge", because even with the army the odds were against it -- while its call to pull
back only fired from 30 m away. It now calls the regroup when its army is going at a
Mech they cannot beat together.

The shape matches the original's own evaluation in one way and not in another. The
passive opponents are read as passive -- the turtle as turtling, the macro player as
expanding (its second Foundry) or turtling -- and the harasser as a harasser. The rusher
is read as a turtle a fifth of the time and as a harasser a third, and later
(124 s to a first correct read): from outside, its Mech holding its base looks like a
turtle, and once beaten back early its waves look like raids. The APM cap refused 2-8%
of intended actions.

## How it is built

| Area | Approach |
|---|---|
| Map | **A new battlefield every match.** When the scene starts, `MapRuntime` runs the original terraced-heightfield algorithm (renormalised fBm, terraces, rim mountains, carved connecting ramp) with a fresh seed and builds everything from it before the rest of the scene wakes up: the **Unity Terrain**, its splat weights and relief occlusion, the water and deep-water volumes, the land beyond the rim, the start plateaus, ore fields and mirrored expansions, boulders, scenery, groves and bushes, and the NavMesh — under half a second on the MacBook Neo, the per-texel work on worker threads. **The two bases are always joined by ground a unit can drive.** The generator checks that on its own coarse cells, but the terrain is a smoothed surface through them that climbs a single terrace step more steeply than units can, so on two maps in forty the whole other half of the map was reached only through a crack one unit wide; the check now runs on the ground as the units see it, and where it fails a ramp is carved. For the same reason a boulder is never left in the mouth of a pass (on one map in forty a rock left a gap of a metre or two beside a slope). Maps that were fine, the default among them, are unchanged. Restart or return to the title screen and the next match gets another map. `-sfseed N` (or `MatchSettings.mapSeed`) fixes the seed; the benchmark and the AI evaluation stay on the default map so their numbers compare. The editor's map builder runs the same generator once, so the saved scene has the default map to look at and edit; untick *New Map Every Match* on the Map to play the saved one. The ground is four photo-scanned surfaces from Poly Haven (CC0): mossy earth with small stones where the grass grows, grey-brown stony dirt for most of the map, lichen-streaked rock slabs on the cliffs and beach sand on the shores and dry ridges, each packed with its scanned height by `Tools/blender/pack_textures.py`. The custom terrain material blends them on that height, so stones stand out of the sand and moss fills the hollows between them, darkens the hollows the scans' colour leaves unshaded, samples each layer twice at different scales and angles swapped over in broad patches so nothing repeats in a grid from above, adds faint folds tens of metres across so broad slopes are not smooth as dunes, and tints moist hollows and shores darker and greener and exposed ridges paler. Cliffs are sampled from the side with height as the texture's second axis, so a wall shows rock instead of a smear from above, with the rock drawn taller than wide so it reads as upright joints; baked relief AO. (Before, the whole map was one orange dirt photograph tinted four ways under a warm grade, with the photograph itself imported as its own normal map.) Moss grows in patches with bare gravel between them, rock breaks through on the steep faces and high ground, and scree gathers below it. (Until this edition none of that showed: the splat weights were lost when the terrain asset was saved, so the whole map rendered, and grew grass, as moss. The editor's builder now writes them straight into the splat texture as its last step; a match's map is never saved, so there the weights are simply set.) Beyond the rim the land continues as a ring mesh that starts exactly at the terrain's edge height, is textured by the same splat rules and relief shading, and climbs into ridges, so there is no seam, cliff or colour change at the border; the fog of war follows the border ground out there instead of smearing its edge into streaks. Terracing walls every lake with cliffs, so stretches of shore picked by a slow noise field are slumped into sandy beaches running down into the water, and the rest keep their cliffs. |
| Wind | Every match draws its own weather from the map seed: a heading, a strength (a still day or a blustery one) and its own gust rhythm, all pure functions of the match clock, so a seed blows the same way every time. Everything agrees about it. The trees lean and the grass ripples in gusts that travel across the map along the wind, the twigs flutter and each leaf card shivers on its own; the cloud shadows drift with it — with the day's wind, not the gusts, at a steady one to three metres a second (they used to follow every gust at up to 14 m/s, and raced across the map); smoke and embers lean downwind; fire runs downwind and moves fast in a gust and hardly at all in a lull; a tree leans with it as it falls, and a snag burned through at the foot goes over the way the wind pushes it. When it blows hard, leaves tear out of the crowns and are carried away downwind. **Blasts move the air too.** A Mauler's muzzle blast and a shell bursting throw out a wall of it that races over the ground at about 50 m/s: the grass and the bushes it reaches are laid flat away from it and stand up again behind it, the crowns are thrown over and bend from the foot rather than stretch, leaves are torn out of them, and the air draws back a little as the front passes. It also moves everything else loose in its path, as it arrives there rather than all at once: the water ruffles in small rings, mostly darker than the lake (a roughened surface reflects less sky) with a little spray; dust lifts off dry ground and rolls outward, with clods and grit thrown close in; and when the front reaches the middle of the view the camera takes a jolt, a beat after the blast by the time the air took to get there. It draws nothing of its own — no ring on the ground, no ripple in the image (there used to be both). Moving air is invisible; you see it only in what it moves. A gun's is thrown forward out of the muzzle, so the growth in front of it goes over and what stands behind the tank hardly moves; a burst goes all round. The four strongest fronts alive at any moment are uploaded to the vegetation shaders (`Shaders/SF_Wind.hlsl`), which is all the state there is — a plant works out what the pressure is doing to it from its own position, so nothing has to be stored per plant and a seed replays identically. |
| Vegetation | Groves of spruces, gnarled broadleaves, tall leaning trees and spreading jacarandas in the meadows, lone trees, dead snags on the bare ground, conifers on heights no unit reaches, bushes through the green, drifts of flowering shrubs, ferns in the shade of the groves and reed beds along the shores — 880 plants on the default map, placed by the map generator and kept out of the bases, ore fields, expansions and narrow passes. A tree closes the ground under it (the trunk, its low boughs and a unit's own radius) as *Rubble*, the same area a boulder stands on: infantry, Diggers and Skimmers go round it, a Mauler drives through and knocks the tree down ahead of its hull. (The footprint used to be the bare trunk; Rubble is an area, not an obstacle, so the NavMesh let a unit's centre right up to its edge and units walked half inside the trees.) Groves are solid woods, so each one is checked as it grows: if it would cut a base, an expansion or an ore field off from the rest of the map, it is not planted. (That check used to judge the ground by the height field's coarse cells, far stricter about slopes than the NavMesh units actually drive on, and on about two maps in five it found something already cut off before the first tree — the rim of a base's plateau, a narrow pass, or an expansion in a basin nobody could enter — and turned every grove away, leaving a bare map of lone trees. It now reads the same slopes the NavMesh does, takes the narrowest passes from the NavMesh itself and keeps the woods clear of them, and expansions are only placed where every ore field can be reached: over 40 maps, 3 lost a grove, 4 in all.) A felled tree is a rod pivoting on its stump: gravity's pull grows as it leans, so it starts slowly, comes down faster the further it goes, and a tall tree takes longer to fall than a short one — measured, 1.5–1.7 s when a blast throws it. **A Mauler shoulders a tree over.** The trunk is held against its glacis — however far the hull has come past the foot of the trunk, the tree leans at least that far — until it goes over faster than the tank drives and falls away ahead of it. The blow takes the way off the tank (4.1 m/s down to 1.4–2.0 m/s against the scanned trunks, more the thicker the trunk), its nose rides up against the trunk and the engine works hard, and the tree is down 1.4–1.8 s after the hull met it; the tank then rolls over the log, which is pressed into the ground and splinters under the tracks. (It used to start the tree over slowly and drive on through the trunk while it still stood nearly upright; a staged push now keeps at least a quarter of a metre between the trunk and the glacis throughout.) How hard it was shoved sets how fast it starts (a shell close by against a snag that simply gives), the wind leans it on the way down, it comes to rest on its own boughs rather than flat on the ground, and it thumps, springs back a little and settles (a little, however hard it landed: a tree flung down at full tilt used to rebound 45 degrees and fall a second time). If another tree stands in its way it hangs up in that crown part-way down and creaks there until it slips off — or until the tree holding it comes down too. The dust, the shower of leaves and splinters and the camera shake all follow the speed the crown was travelling when it hit. Explosions throw trees over away from the blast and flatten bushes, and set what they reach alight. Whether a given tree goes over is a **chance**, not a rule: a shell landing on top of one takes it down about four times in five, a couple of metres off about a third of the time, and out at the edge of the blast's reach (a little over four metres for a Mauler's shell) about one in twelve — and a thick trunk stands where a slender one snaps. Measured over 60 bursts in a wood: 66% / 45% / 21% / 6% across the four bands out to 4.3 m, 27% of everything in reach (the scanned broadleaves and jacarandas have thicker trunks than the drawn ones did, and the thickness scale was moved so the broadleaf keeps its old odds; before, 80 / 35 / 17 / 8% and 23%). So shelling a wood thins it rather than flattening it, and the same shot twice is not the same picture. **Everything green burns**, each at its own rate — dead snags and dry reeds readily, resinous spruces hard, green broadleaf slowly — and **the grass between them carries the fire**: a coarse fuel grid burning cell to cell and lighting whatever is standing in it, which is how a fire crosses a meadow from one stand of trees to the next. **Only what grows burns.** The fuel is worked out from the same rule the ground cover places its tufts by, so a fire stops where the grass does: bare gravel, sand, rock and the trampled ground under buildings and ore carry nothing, flames stand only on the grass of a patch that is only partly grown, and the ash it leaves stops at the grass's edge. (The first fuel grid also counted a quarter of the gravel, and let bare ground burn: 1,661 of the 4,051 cells it would burn had no grass drawn on them. Now 4 of 1,783 do, at the thin, random edges of meadows.) A fire runs downwind: it passes to what lies that way far more readily, and from further, than across or against it, so a blaze works along a meadow as a front rather than a circle. **Damp ground hardly takes at all** — the water level against the ground at a spot and five metres around it thins the grass's fuel and a plant's chance of catching, so shores, reed beds and the grass along a lake resist while the dry terraces above them go up. How far one fire gets is drawn when it starts, and each one has a budget: most are over in a handful of plants, a few run through dozens, and no single fire can take more than about a sixth of the map's growth. Several can: 30 fires lit one at a time on a test map took 1–66 plants each (median 3) and left 66% of the plants and 86% of the grass burnt. **A wildfire can set a building alight.** While a burning tree or burning grass is at a structure's walls, each moment carries a small chance of it catching, the more the harder the fire burns: measured on buildings set down in meadows with the grass lit upwind, a fire that reaches the walls sets about three in ten alight. A burning building burns for a quarter of a minute and loses 6–12% of its health — visible on its bar, never enough to destroy it on its own — and its owner is told it is on fire. The fire takes hold on the side the wildfire reached it from and works round the roof over six seconds: tongues of flame stand on the building's own roof line (each model's roof is measured from its mesh by `Tools/blender/roof_profile.py`, so flames sit on the eaves and the dome, not in the air beside them), billows roll up once it burns hard, sparks fly, two firelights flicker on the walls and the ground, and a heavy dark column leans with the wind. When it is out the roof smoulders for 25 seconds, and the building stays scorched for the rest of the match. An enemy building burning in the fog shows none of it until it is seen. Bases stand on trampled ground, so it is the outlying structures near meadows and woods that are at risk. Burnt grass does not come back — black stubble on ashy ground for the rest of the match — and only 45 plants and 150 patches of grass burn at once. A burning plant is licked by tongues of flame from a simulated flipbook (JangaFX's CC0 *Small Camp Fire* rendered in Blender by `Tools/blender/render_flipbooks.py`; each tongue plays its own stretch of the fire, mirrored half the time, drawn upright so they stay vertical) with a hot fire at the foot and now and then a rolling billow of flame through a tree's crown (a bush flares in tongues: fireballs low in the grass read as little explosions), under a column of smoke that leans with the wind — dark and thick while the leaves burn, thinner and greyer once only wood is left — and embers carried downwind; its firelight breathes and gutters, the wood chars black with embers glowing in the cracks, the crown burns away, and when it is out the black snag smoulders for a while with a thin pale wisp (a slender pine or young broadleaf burns down to a broken stump a third to three-fifths of its height, each its own: they used to stand as bare black poles 10-15 m tall). The plants are **photo-scanned**: Poly Haven's CC0 scans of a gnarled island tree (the broadleaves), a fir (the spruces), a slender young broadleaf that leans (the tall trees), a jacaranda (the spreading, many-stemmed trees), two searsia shrubs (the bushes and the flowering shrubs) and a fern — each a real trunk with its own bark photograph and up to two million leaves and needles, far more than the game can draw hundreds of times. `Tools/blender/bake_scanned_flora.py` keeps the trunk and limbs, decimated to a few hundred to 1,800 triangles with the scan's own UVs and bark, and bakes the foliage: it groups the leaves and twigs into clusters about a metre across, renders each one in Blender from the side the RTS camera sees it (out of the crown and from above) into a tile of an atlas, colour and normals, and puts one card where the cluster was. Each card shows all the foliage within a sphere round its cluster, its rim ragged where leaves drop out one by one, so neighbouring cards overlap the way sprays on a bough do (showing only its own cluster, a card covered a sixth of its tile and the crowns looked burnt). A broadleaf is 150 cards on 1,600 triangles of trunk, a fir 160, a jacaranda 170, a bush 40 cards. Each scan's leaves are tinted onto a green that sits with the grass (their mean colour is measured from the atlas, so a re-bake keeps it), and the flowering shrubs carry clusters of pale pink blossom among the leaves. (The trees before were grown by script: a branch skeleton hung with photographed leaf sprays that belonged to no tree in particular, over a solid dark canopy — blocky crowns of a few large cards, and bushes of 64 triangles that looked like agaves. That generator stays as the fallback for a kind with no scan baked.) The cards bend with the wind and are thrown over by blast pressure exactly as before — the models carry the same sway weights — and the far copy drawn beyond 70 m keeps every card and thins the trunk. Ferns and reeds are too small to cast shadows or to be drawn far away. Measured against the previous build (AI-vs-AI benchmark, alternating runs), the scanned plants cost nothing measurable: 16.7 against 16.3–16.9 ms a frame on a cool machine, 18.1 against 18.1 ms once warm. All of it is drawn GPU-instanced by one shader (`SF_Tree`), a draw per kind for bark and one for foliage: warmer on top and cooler underneath, lit through from behind looking toward the sun, and swaying in a gust that travels across the map. Leaves have their own lighting (not the engine's surface model): the sun wraps round a crown, a leaf in shadow keeps some sky light, the inside of a crown is darker than its outside, and each tree leans a little toward yellow or blue-green, so a wood is many trees, not one green mass. Under fog of war the player sees each tree as they last saw it. A crown that comes too close to the camera shrinks out of the way. |
| Wildlife | Packs of wolves and lone foxes roam the meadows, flocks of songbirds feed in the open, and eagles circle overhead: Quaternius's animated low-poly animals (CC0, from OpenGameArt), coloured by the scene builder (their materials come plain grey) and playing their own walk and flight cycles. The ground animals walk only where a unit could — every step is checked against the NavMesh, so they go round trees, rocks and water — keep a few metres between packmates, wander from spot to spot and stop now and then. **Songbirds** live on the ground between flights: a flock feeds on open ground clear of the crowns, each bird hopping, pecking and looking round, until something comes near or it grows restless; then the whole flock flushes and flies low to another open patch with a small bird's bounding flight — a burst of wingbeats climbing, wings shut for a short fall, a burst more — and lands one by one. The bird is Quaternius's, re-posed and re-animated for it (`Tools/blender/build_songbird.py`): the model is a perched wagtail whose only cycle is a flutter of folded wings, so flying it as it came read as an upright dark blob flapping in place; it now has a flight cycle with its wings spread and beating, its legs tucked and its tail shortened to a finch's, a fold, a perch and a peck, and its colours come through one small palette texture (chestnut back, slate head, rusty breast, dark flight feathers with pale coverts that flicker as the wings beat — a plain brown bird disappears against brown ground from above). It is drawn about twice life size, or it would be a few pixels lost in the grass. Units coming close, gunfire, explosions, deaths and burning plants frighten them all: a pack bolts away and settles on new ground far from everything that has happened lately, a flock flushes and flies off downwind of the trouble, and eagles swerve away, climb and keep their distance while the danger lasts. Flyers hold a steady height over the highest ground under their whole circle and change it slowly, so they no longer bob up and down over every terrace. Scenery, the cheat's reckoning aside: nothing targets them, they block nothing and they do not burn; they are shown only where the player can see, and they take fright only at what the player could see too, so a pack bolting at the edge of the fog never gives away an unseen enemy. |
| Rocks and scenery | Boulders are six photo-scanned rocks (Poly Haven, CC0), decimated in Blender to 900 triangles each by `Tools/blender/build_rocks.py` with the scan's normal map keeping the detail, drawn by their own shader (`SF_Rock`) with dust settling on their lower flanks; each boulder on the map is one of them at random. Scanned crags and slab groups stand where procedural rock spires and shelves did, beside a crashed dropship and ruined pylons (`Tools/blender/build_env.py`), placed only where 80% of the footprint is ground no unit can use — cliff faces and the rim. They are kept out of the NavMesh bake, but the part of a piece that stands on usable ground is marked not walkable, so no unit walks through the edge of a crag or a wreck. **StarForge ▸ Debug ▸ Check Map Blocking** samples the NavMesh under every object: it also tests a ring just outside every trunk (where a unit's centre would put its body in the bark) and that the two bases are still connected. On a generated map 22/22 scenery pieces, 55/55 boulders and 184 of 185 trees block ground units round the trunk (measured with the scanned trunks' own radii), and the bases are connected (Maulers still crush boulders and trees). Boulders' Rubble is padded by a unit's radius too. |
| Ground cover | Grass, wildflowers and pebbles, grown at load from the terrain's own splat weights and drawn GPU-instanced per 32 m chunk (one instance buffer for the map, a draw per chunk). Blades are real geometry, not alpha-cut cards: `discard` would switch off the hidden-surface removal that keeps overdraw cheap on Apple GPUs. **The meadow is a turf, not tufts on dirt:** under the tufts the terrain lays a turf of the grass's own colour (the scanned ground is sand and stones, and between the tufts it showed through as bare brown), and turf and blades take their colour from the same broad patches, lusher here and drier and yellower there. Far from the camera every second clump is left out and the remaining blades are drawn wider, so a distant meadow keeps its cover without the sparkle of sub-pixel blades. White, yellow and violet flower heads stand in loose drifts (opaque geometry like the blades, gone by 70 m). Burnt grass is short black stubble on grey ash. The turf's surface and the ash come from three top-down ground pictures made for this project by the author with an image model (Google Gemini), packed into tiling detail textures by `Tools/blender/pack_turf.py`: each is its picture over its own mean colour, so it says where the turf is lighter, darker, greener or yellower while the shader's own colours say what colour a meadow is, and from far off, where the detail blurs away, the meadow is the colour it was. Wind is one slow travelling gust per clump; per-blade flutter twinkled at RTS distance. Blade attributes are sampled at the centroid: with MSAA, a blade seen almost edge-on was shaded from a point off the blade, and the extrapolated values blew single pixels up into sparks that bloom turned into flashing white lights. |
| Battle damage | A map-wide mask (`GroundMask`) records what the fight has done: flattened under structures and ore, charred where explosions and burning trees were (healing over minutes), churned along the tracks of moving units (fading in seconds), and turned to raw earth under a Mauler's path (fading over minutes). Grass bends, parts and burns on it; the terrain darkens scorched and churned soil. Tracked vehicles also leave tread marks, two cleated bands per Digger or Mauler, that fade over a minute: pressed into the ground they cross: the ground itself darkened, with a trace of the soil's colour there, and the cleats' imprint in it, ragged at the edges, darker while the ground is wet, and the ground's own texture showing through (they used to be uniform dark stripes, hard-edged, the same colour everywhere). A lane vehicles shuttle along is marked once and kept fresh, not marked again on every trip, and a mark only darkens the ground under it: laid every trip at the soil's own unlit colour, a Digger lane added up to a flat grey band with the ground gone from it. Where vehicles turn and no heading repeats, a pixel takes the three newest marks and no more. Shells and wrecked vehicles dent the terrain itself: a shallow, smooth **crater** (35 cm for a shell), drawn as a scorched centre inside a ring of lighter thrown earth with ragged edges. Craters do not stack — the ground only goes down to the deepest single bowl covering it, so a spot shelled all match is a wide dip, not a pit (deep pits turned their walls into cliff rock) — and never open under structures or ore, or below the water line on dry land. The match works on a copy of the terrain data; grass, pebbles, rocks and trees settle into a crater, and units sink into it as they cross. Explosions also break the boulders they reach. |
| Navigation | **NavMesh** (AI Navigation). Structures and ore carve the mesh; Diggers skip avoidance so mineral lines never jam; Maulers slow down to turn instead of strafing. Units wade into water up to 1 m deep, at about half speed; deeper water is marked not walkable with box volumes, and a Skimmer skims over the shallows instead of diving to the bed. Boulders and tree trunks stand on a *Rubble* area that only Maulers may path through: a Mauler drives straight at a rock or tree and crushes it, and the NavMesh tiles under it are rebuilt in the background (on a copy of the baked data, so a match never edits the asset) so everyone can use the ground; the same happens when an explosion or fire brings one down. **A move order survives the NavMesh changing under it.** A rebuild takes the path from every unit whose way crossed the tiles it changed — and a Mauler in a wood fells its own — and that used to count as arriving: the unit stopped where it was and stood until ordered again, and a Mauler crossing a wood could wait up to nine seconds for a new path while its long requests were started over by each rebuild. Now a unit that loses its path asks again, and stops only at its goal or at the end of a partial path (as near as the ground allows); path requests get 1,000 search steps a frame rather than Unity's 100, so nearly every one is answered in the frame it is made; and the tiles are rebuilt at most every 1.5 s. Measured with an army sent through woods and over rocks every few seconds for three minutes: from 11 stops of 1.5–4 s to none, the slowest order answered in 0.54 s rather than 2.5 s; in AI-vs-AI play, from 12 stands of up to 29 s to none. **Units are only sent where they can get to.** The nearest NavMesh to a point is often a patch nothing can walk onto — a plateau top, a clearing ringed by trees and boulders, a sliver between a building and a cliff: on ten generated maps 17–41% of the ground NavMesh (29% on average) is cut off from the bases. The AI used to snap its staging points, guard posts and scouting targets to that nearest NavMesh, and a unit sent there walked to the end of a partial path and stood, asking for the same partial path every second or so; a wave told to gather there never gathered. Over those ten maps a fifth of its candidate staging points landed on cut-off ground, and in one AI-vs-AI match 321 of the 329 orders to a staging point went to a plateau no unit could climb. The game now works out which stretches of the NavMesh join up (from the navigation graph itself, and again when a Mauler opens a way or a building goes up or comes down); every move order, the player's and the AI's, goes to the nearest point on the ground the unit stands on, and the AI keeps its own gathering points on ground its base connects to. On the same map, none of 2,510 orders in a match went anywhere its unit could not reach; in the stall trial's last runs there no unit stood longer than 1.9 s while ordered to move (0, 3 and 7 such pauses of 1.2–1.9 s), the ones left mostly a unit waiting out its 1.2 s re-path after a NavMesh rebuild took its path. An order into a building's footprint now ends beside it instead of leaving the unit standing short of it. A trained unit comes out onto the ground the bases share: on one map a building's first free exit spot was an 81 m² pocket it had closed off against the terrain, and every unit it trained there (18 in a match) was stranded; on that map now, none of 2,278 orders in a match went to ground its unit could not reach, and with the pocket made on purpose (a pen of walls closed against a Garrison) the old rule put its rifleman inside on three maps out of three, the new one outside with a way to the enemy base. Quick AI evaluations on the same code, the old snapping against the new: 3 of 4 won against 2 of 4 (one Turtle game won at 541 s against capped at 720 s), none lost either way — one match a side is within the noise; Full evaluations on code with the change in have gone 12–16 of 20 with no losses (14 of 20 on the final code, none lost, the rest time caps); none has been run with the old snapping on the current code. Structures need dry ground. |
| Models | Authored in **Blender** by script (`Tools/blender`) — the original ten, the Skimmer and Sentinel, the scenery, and the trees and bushes — exported to FBX with named material slots that map to URP materials. The exporter also bakes ambient occlusion into vertex colour R (cast against the whole model and the ground) and writes per-vertex shading data into G and B where a part asks for it (per ore shard, a random value and the height along the shard; per tree clump, a random value and how freely it sways), exports movable parts as child objects with their origin on the pivot, and writes a second FBX of pre-cut chunks for each structure. `Tools/blender/starforge_models.blend` has them all laid out for editing. |
| Unit surfaces | One custom lit shader (`SF_Unit`): hull plating triplanar in object space as an x2 detail multiply normalised by its own average — rectangular plates split the way hull panels are, recessed seams with a lit lip, rows of rivets, hatches and vents, grime in the seams, generated by `Tools/make_panel_texture.py` (the original's armour photograph it replaced read as brickwork at the units' scale) — edge wear found by screen-space curvature (bevels turn the normal fast, flat plates do not) and chipped by that texture, grime climbing from the ground and settling in cavities, a cool sky rim for silhouette. Ore is eerie, living crystal: veins of light seen deep inside each shard with two depths of parallax, so they shift against the surface as the camera moves and the crystal reads as a volume; a dark, glassy, near-black teal body between them; a spectral teal-green glow under a violet rim; a slow breath that swells the whole seam every five seconds on its own phase, with a wave of light climbing each shard; and slender needles bristling among the prisms. Motes of light rise off the seam and circle it as they go, more as it breathes in, over a pool of teal light on the ground that swells in time with it; a mined-out seam gives up its light in a burst of shards and motes that hang in the air. A Digger's hopper and drum windows are dark ore glass that lights up teal with the load it carries: the glow stirs while it cuts, swells in over a second or so once the load is aboard, flares as the hopper seals and fades out slowly at the drop-off (it used to switch on and off), the light rolling slowly round the drum, and the pool of light it spills on the ground follows the same curve. Per renderer it also carries damage (a hurt hull shows first where a mask in the plating texture's alpha says it was struck — scorches, bullet strikes, gouges, cracks, from a picture made with an image model — then chars more widely, and under heavy damage a few of the worst wounds smoulder dully; the mask is read with the panels, at no texture read of its own), the construction hologram, the hit flash and burning — none of it with `discard`, which would cost early depth testing on every unit. |
| Animation | **Soldiers are skinned and rigged**: all six infantry types share one skeleton and 25 clips (walking, running, crouching, crawling, aiming, firing, reloading, the pistol, the throw, the knife, the dive, the vault) whose legs and trunk move as KayKit's animations do, crossfading from one to the next, the clip's pace following the ground so the feet do not slide. The Biolab's creatures and the wildlife play their own skinned cycles, crossfaded the same way. **The Mech and the Ironclad walk procedurally**: each foot stays planted where it landed until the hip has passed it, and the legs, rigid plates on pinned joints, reach the feet by two-bone IK. **Machines move by their rigid parts**, from the simulation's own state, as armour plate should: the gun and the tank barrels recoil on the shot, the Digger's arm dips and its cutter spins while mining, ore heaps up in its hopper and its drum turns while hauling, an ore seam's crystals shrink as it is mined out, the Foundry's control head sweeps and the Workshop's crane trolley travels. |
| Destruction | A destroyed structure is swapped for its pre-cut chunks as rigid bodies, thrown outward glowing hot and trailing smoke and embers, tumbling and settling on the terrain — each hard landing throws up a puff of dust and clods, or a splash in water — then sunk out of sight. **A blast shoves whatever lies loose** around it: a later explosion throws the chunks still tumbling and the wrecks and bodies lying nearby, a settled wreck woken to be thrown again — a rifleman's body further than a Digger, a tank's hull only rocked on its tracks, and anything thrown gets its time to settle again, so nothing freezes in mid-air (a staged shell bursting among three bodies and a tank wreck, six runs: bodies within 4 m of it were thrown 0.5–3.4 m, those 4.5 m and further moved a few tenths of a metre at most, and the tank wreck usually rocked up to half a metre; the throw does not fall off evenly with distance, since a body lands on whatever lies in its way. The test lays them on the edge of a plateau, and a body thrown over it went much further: 14.5 m in all in one run, seen going over the edge in the pictures, 11 m in another, 9.3 m with a 2.6 m drop in a third. A Bunkhouse's chunks reported 7–10 hard landings). All of it is presentation: the simulation never reads it. **A unit that dies leaves a body or a wreck** (`UnitWreck`), played out with rigid bodies on the terrain: a rifleman is knocked off his feet away from the shot — dropped where he stands by a round, thrown a few metres by a shell bursting beside him — and goes down as a ragdoll, his legs limbs on hip joints (Unity's `CharacterJoint`) that fold, splay and trail as he falls (in a staged check his hips came to rest anywhere from -21 to +106 degrees, kicked back to folded forward), while his rifle flies out of his hands; a later blast, or a Mech's foot coming down beside him, throws him again with his legs flailing; a Mauler's ammunition goes up, heaving the hull off its tracks and blowing the turret clean off to land beside it (or leaving it wrenched round on its ring); a Digger is rolled over onto its side; a Skimmer drops out of its hover still going and slides to a stop on its belly, throwing up dust — or goes under in deep water. Machines burn where they come to rest, charred with glowing cracks that cool, under flames and a column of smoke leaning with the wind. A wreck lies a quarter of a minute and a body about eight seconds, then sinks into the ground; past 36 on the field the oldest go early. (Units used to sink into the ground inside a fireball, and a rifleman blew up like a vehicle; he now goes down with sparks off his armour and a puff of dust where he lands.) Only what the player saw die leaves anything. |
| Water | Its own shader (`SF_Water`) with no repeating pattern: three layers of the original's ripple map at scales and angles with no common period (a smoother synthesised ripple map was tried and dropped: its long waves looked worse), through UVs bent by a slow noise field, over three long analytic swells. The ripples are read a mip and a half down and the sky's reflection sees a calmer surface than the refraction does: at the RTS camera a crest is a pixel or two wide and reflects several times the sky a level stretch does, so read sharp the whole lake was strewn with pale scribbles. It shows the lake bed through the surface from the camera's opaque colour copy, bent by the ripples (never picking up a unit standing in front of it), absorbed channel by channel along the path through the water, red first, with the water's own scattered light filling in: clear, green-tinted shallows over the sand, dark blue-green deeps. Depth drives foam crests that roll in toward the shore (each with a soft front and fading out as the camera pulls back: drawn with a hard leading edge and kept at a third from far away, they read as contour lines ruled across the lake) and lacy foam lapping at the waterline — broken up by noise into patches that come and go along the shore, and lit as a surface rather than a light, where it used to outline every lake with a glowing white stroke — and lights soft caustics on the bed in the shallows. Glints are broad and dim and ripples calm with distance, so the surface does not sparkle at RTS range, and the sky it reflects is slightly blurred so neighbouring ripples do not pick different patches of cloud. The surface covers every hollow of the terrain below the water line (built from the 2 m generator grid, it had left dry pits beside the lakes). Units moving through the water leave a wake — rings dropped every metre or so that spread and overlap into a V, and foam that opens up and dissolves — and splash where they go in or come out; shells landing in the water throw up a white column and a spreading ring instead of earth. |
| Fog of war | One **URP full-screen render feature** reconstructs world position from depth and applies the player's visibility texture — explored ground dims and cools, unexplored goes dark under a faint survey grid with contour lines (a chart with nothing on it yet; the lines come from a picture made with an image model, and are read only where ground has never been seen), with a shimmer at the vision edge. |
| Atmosphere | The same pass integrates exponential height fog along the view ray (mist pools over the lakes and in the low ground, plateaus stay clear), distance haze and sun in-scattering. It already has every pixel's world position, so all of it is free of extra passes. Cloud shadows are the sun's light cookie, a remapped cloud photograph drifting slowly with the day's wind. **Weather** is drawn from the map seed and is presentation only (fire and the battle run as on a dry day): gusts lift low sheets of dust off dry, open ground and run them along with the wind, and on a windy day a dust devil now and then spins up on bare ground and wanders downwind before it falls apart. About three days in ten bring passing showers of a minute or three: rain slanting with the wind, rings on the lakes and splashes on the ground, while the ground, grass, rocks and machines darken and take a sheen as they wet (in about twenty seconds) and dry off slowly after (a minute and a half); the sun dims and the air thickens while it rains. Seed 1000, the benchmark's, is a clear day; `-sfweather rain` or `clear` forces it (a shower costs nothing measurable: forced to rain on seed 1000, with a two-and-a-half-minute shower across the Mech fight, the benchmark averaged 50.2 and 51.4 fps against 49.7 on a clear day, with a Mech in view 49.3 and 51.3 against 49.3, plugged in with the battery full, with the present 8,000 streaks; with the first 6,000, 49.0 and 50.0 against 49.9). |
| Occlusion | **SSAO** at half resolution, normals reconstructed from the depth texture the fog pass already needs, applied after opaques as a single multiply — the cheap path on a tile-based GPU. Its sampling noise is fixed per pixel: URP's default blue noise is re-rolled every frame for TAA to average out, and without TAA it made the grass boil. |
| Effects | Layered explosions: a white-hot flash, then a **simulated fireball** — a real fluid simulation (JangaFX's CC0 *Ground Explosion*, made in EmberGen) rendered in Blender into a flipbook that plays from the flash through a rolling fireball into the smoke column it leaves, glowing at its foot as it dies. Each frame of the sheet carries the smoke lit by the sun and by the sky separately, the fire's own light and its coverage, so the game relights the smoke with the scene's sun and colours the fire by its heat (dark red at the fringe, orange, a yellow heart that bloom picks up; a paler core came out cream under the tone mapper), and the sheet's frames crossfade so a short-lived burst does not step. Its frames are 384 pixels across (at 256 the simulation's grain showed as blotches with the camera close). It is composited premultiplied, so the smoke in it darkens what is behind it: the old photographed fireball was drawn additively, its smoke could only ever brighten the scene, and every explosion read as a flat orange blob. Burning trees, grass, buildings and wrecks are licked by flames from the same kind of sheet (the CC0 *Small Camp Fire* simulation), mirrored at random so no two tongues match. Around the fireball, dark smoke billows while the fire burns and rises into a column as it dies (smoke, dust and mist are lit cloud puffs, `SF_Smoke`: four cauliflower clusters with their own normals, occlusion and ragged coverage, made by `Tools/make_smoke_puffs.py`, lit by the sun and from above so the tops of the billows catch the light and the folds stay dark, and eaten into wisps as they age — the photographed smoke sheet drew every puff as the same flat grey sprite. **A puff never reaches opaque**: its coverage is soft-edged and eaten at two scales, so a cloud is built from many faint puffs over one another rather than from one solid sprite, and its outline stays ragged close up and far away. The first version of the atlas got both wrong — perfect hemispheres you could count, under a coverage that saturated in the core — and a burst drew as a hard dark lump of rock sitting on the ground. Colours are grey rather than near-black, too: dark smoke over a sunlit landscape still reads grey, and the old values came out as a hole in the scene), short hot sparks that strike the ground and skip off it, small charred debris that trails fire and bounces off the terrain, a fountain of earth and a dust ring for ground bursts, drifting embers, a point-light flash, scorch marks (four blast marks from pictures made with an image model: a round burst, a shell's streak, a ragged burn, a small crater with thrown rays) and camera shake — with structures coming apart in a short chain of secondary blasts. Guns fire shaped muzzle blasts (flares drawn with the streaks): the Mauler's is a white-hot core in a long orange blast with jets from the muzzle brake, a cone of smoke dragged to a stop and dust kicked off the ground, and its shell flies as a glowing slug leaving a smoke trail. **The Mauler is meant to feel heavy.** Its engine smokes from the two stacks on the deck — a wisp at idle, a grey-black plume under load, a belch as it pulls away from a standstill, and dirtiest while it leans on a boulder or a tree with the engine at full and the hull going nowhere — laid out behind the hull as it drives and leaning with the wind. (It took three rounds: the first poured out a cloud the size of the tank, the second was a haze nobody could see. The puffs were also being born on the engine deck, where the smoke shader's soft edge — which lets smoke meet the ground without a hard line — faded them out against the hull behind them; they now start half a metre above the stacks.) Its tracks roll a pale dust trail off the back of each run, more the faster it goes and none at all off wet ground, leave a low wake hanging over the ruts for a few seconds, and flick stones out from under the cleats. The ground remembers it: the cleated tread marks last a minute, and under them the tank churns a band of turned earth that heals over minutes, like a crater's thrown soil. When it fires, the whole tank is shoved: the hull is thrown back on its suspension and the nose comes up, rocks down past level and settles, a heavier flash and a bigger cone of smoke go out of the muzzle and hang in the air for seconds, the gun goes on smoking for a couple more, and the blast throws a wall of air forward over the growth in front of it. Its shell lands a size up from its splash — the heaviest thing in the game short of a building going up — and throws its own front all round. Zoomed out the engine smoke thins, since a plume nobody can see is not worth the particle budget. A Mauler crushing a boulder throws rock and dust and leaves a scorch; a tree coming down shakes out leaves and splinters and throws up dust all along the trunk where it lands; burning trees carry flames, smoke, embers and a flickering firelight. A Mech's autocannon and rotary cannon throw their spent brass out of the breech, tumbling end over end and bouncing and rolling on the ground. Its feet throw up the ground they land on, read from the same rule the terrain is painted by: a pale plume off sand, torn turf and little dust off a meadow, brown dust and stones off gravel, chips off rock, a puff of ash off burnt grass, mud and no dust at the water's edge or after a shower; a tracked Mech's runs leave dust of the same colours, and a foot coming down beside a body or a wreck kicks it. Each Mech carries its side's standard as real cloth (Unity's cloth simulation, pinned to a crossbar), thrown about by the Mech's stride and the gusts of the same wind the grass and the smoke follow. Plus a single instanced depth-decal shader for selection rings, footprints, order markers, scorch marks, tread marks and the light under ore, and instanced health bars and tracer streaks. |
| Audio | **Weapons** are real firearms, from the CC0 Free Firearm Sound Library: every gun in it was recorded from beside the shooter and again from a distance, and a shot is mixed from both — the crack from the near take, the report rolling back off the ground from the far one a moment later — with a slap-back off the terrain behind. A Trooper's rifle is an AK-47 (two takes, a little apart in pitch), an SKS and an AR-15, with a short thump of body under each crack (four takes, so a firing line is not one sample repeated). (Before, the rifles were the thinnest takes in the library at a fifth of the level of the music, and players heard no shot at all: measured, every shot did play, it was simply buried. Now the shot is full-bodied, played at 0.4 against the music's 0.54 rather than 0.15, and a new shot takes a free voice, or else the one closest to finishing, instead of cutting off whichever voice came round next; the tank engines' idle loops are paused while no Mauler needs them. Staged fire-fight: 16 shots, every one heard.) The Mauler's gun is a 12-gauge, a .30-06 and a pump gun pitched down more than an octave and rolled off below a kilohertz, which is what a gun that size sounds like from across a valley, played louder than anything but a structure going up, with a **falling sub thump** under the crack and the **report rolling away** over the ground for two seconds after it (a loud bang is not frightening; the thing you feel and the thing that goes on after it are); the Sentinel's bolt and the Skimmer's plasma keep their Kenney energy sound with a real muzzle crack under it, which is what they were missing. Every take of a sound is levelled to the same loudness with its peaks rounded off, so a volley does not lurch about. **A tree** cracks and groans as it goes over and crashes with a rush of leaves as it lands (a felled tree, a tree creaking and wood breaks, all CC0); a bush flattened under a Mauler just rustles, and a fallen trunk splits under its tracks. A rifleman does not explode: his death is the round ringing off his armour and the thud of him going down. **Shells landing and things blowing up** are built the same way, from Kenney's explosion crunches: the crunch carries the detail, and under it go the thump and the long rolling report the recordings have no low end for — three takes for a shell (3.2 s), two deeper and slower ones for a structure (5 s). **The Mauler's engine** is Kenney's low engine dropped an octave with a harsh diesel knock under it and a growl, and its **tracks clatter** over the top, built from rubberduck's CC0 metal hits — the giveaway that a thing is tracked rather than wheeled, and the part of a tank you hear before you see it. **Each Mauler is heard as itself**: the three loudest in earshot get a voice each, panned to where they are on screen, the engine murmuring at idle and climbing in pitch and level as it works (a standing start, a hill, a boulder being shouldered aside), the clatter coming in with speed and running faster the faster it goes. Measured on one tank: idle, engine at 0.08 and pitch 0.80 with the tracks silent; flat out at 4.1 m/s, engine 0.26 at pitch 1.14 and tracks 0.20 at 1.23. (They used to share one bed that could not say where the tank coming at you was, or that it had just started to move.) All of it is aimed at 80–400 Hz rather than at 40: a laptop speaker moves no air down there, so weight put in the sub-bass is not felt, it is simply lost, and the rest gets turned down to make room for it. Measured, the family runs bright to deep — a rifle's energy centres at 800 Hz, the cannon at about 180, the engine at 220, a shell landing at 240–320, a structure going up at 150–200. The rest are Kenney's CC0 packs (glass for crystal, mining knocks, the Skimmer's drive, interface clicks and alerts), several takes of each picked at random and pitched a little; anything missing falls back to the sounds the game used to synthesise. **Ambience** is synthesised into seamless loops by `Tools/make_audio.py`: wind that gusts (stronger zoomed out), water lapping when the view holds a lake, a fire's roar and crackle near burning plants, and birdsong over the green that falls silent for half a minute after an explosion nearby. **Music** is thirteen recorded CC0 tracks from OpenGameArt, one mood at a time: six calm ones played in turn when nothing is happening — *At Home* (wolfgang), *First Light Particles* (yoiyami), *Contemplation* (Joth), *Peaceful Forest* (Samza), *Calm Ambient 1* (cynicmusic) and *A Place I Call Home* (Juhani Junkala): warm orchestral, piano over pads, drifting ambience and soft synth pads, all major-key and consonant by `Tools/measure_music.py`; three dark ones by yd — *Insistent*, *Pressure*, *Menace* — when armies gather or the enemy comes into sight; and four battle pieces while fighting is on screen — *Battle Theme A* (cynicmusic), *Determined Pursuit* and *QaziJamJam* (Emma_MA) and *Epic Boss Battle* (Juhani Junkala), strings, horns, choir and drums. Each time the music turns tense or to battle it picks one of that mood's tracks at random and loops it. A mood has to hold a few seconds before the music follows it (quicker going up than coming down), the moods crossfade over several seconds, and every track is played at one measured loudness well under the effects. (The music used to be synthesised; its plucks and tremolo beeped. The calm set that replaced it was minor-key piano, which sounded creepy rather than peaceful under a quiet base; the tracks there now are major and consonant, measured with `Tools/measure_music.py`.) Everything is heard only if the player could see it. |
| UI | **UI Toolkit** (`Assets/StarForge/UI`): HUD, minimap, command card and the developer-only AI inspector (`HUDController`); in the Host's look (grey iron, bronze edges, gilt headings in Cinzel), alerts come as field-net dispatches with a heading in the world's names and, where they have a place, `Tab` goes there; a control-group bar with an idle-Digger button sits on the selection (`HUDController.UX`; the keys in `PlayerController.Hotkeys`); the menus — title, main menu, Operation setup, Settings, Field manual, Service record, Credits, pause menu and after-action report — in `StarForgeMenu.uxml`/`.uss` and `MenuController`, drawn without textures (`MenuElements`: gradients, chamfered frames, the emblem, the operation map). |

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
  Audio/           Sfx (Kenney and CC0 recordings), Ambience (synthesised) and Music (CC0 tracks)
  Art/Textures/    Terrain and Rocks (packed scans), generated leaf, flame, water maps
  Editor/          generators: RenderSetup, SFMaterialLibrary, PrefabBuilder,
                   MapBuilder, SceneAssembler, BuildMac, AIEvalMenu, MapChecks
Tools/             fetch_assets.py (downloads the CC0 sources into Art/Source/, not
                   committed), make_audio.py, make_leaf_textures.py,
                   make_flame_sheet.py, make_smoke_puffs.py, make_panel_texture.py,
                   pack_fauna.py
Tools/blender/     model authoring + FBX export, pack_textures.py, build_rocks.py
```

### Third-party assets

Everything committed here is free to **redistribute**, not merely free to use:
the repository is public. The third-party art and audio are all CC0 (public domain
dedication — credited here anyway, because the people who made them deserve it);
the pictures the author made with an image model (each listed in THIRD-PARTY.md)
are the project's own;
the three font families are OFL 1.1 and Apache 2.0, whose licence texts travel with
them in [`LICENSES/`](LICENSES). [**THIRD-PARTY.md**](THIRD-PARTY.md) is the full
register: every source, its author, its licence, and which committed file came
out of it.

- **Poly Haven** (polyhaven.com): the ground scans *Coast Sand Rocks 02*, *Forest
  Ground 04*, *Aerial Rocks 02* and *Coast Sand 01*; the rock scans *Rock Moss
  Set 01*, *Rock Moss Set 02* and *Boulder 01*; and the plant scans *Island Tree
  01*, *Fir Tree 01*, *Jacaranda Tree* and *Fern 02* by Rob Tuytel and Rico
  Cilliers, *Tree Small 02* by Rico Cilliers, and *Searsia Burchellii* and
  *Searsia Lucida* by James Ray Cock and Jenelle van Heerden.
  `Tools/fetch_assets.py` records each one's authors and real-world size in
  `Art/Source/polyhaven/manifest.json`.
- **Kenney** (kenney.nl): *Sci-Fi Sounds*, *Impact Sounds* and *Interface Sounds* (the
  Mech's footfalls, hydraulics, launches, lasers and drop are built from the first two).
- **Quaternius** (via opengameart.org): *Animals Pack* and *Animal Pack Vol. 2* (wolf,
  fox, eagle, songbird); (via itch.io) *Easy Animated Enemy Pack* and (via opengameart.org) *Farm Animals Pack*
  (the spider, frog, wasp and cow the Biolab's Skitter, Spitter, Razorwing and Gorehorn are
  made from, given horns, plates, spines and glands in Blender).
- **KayKit** (Kay Lousberg, via itch.io): *Character Animations*, the skeleton and the motion of the
  legs and trunk under the rigged soldier.
- **Blender Studio** (blender.org): *Human Base Meshes* (its *Body Female - Stylized* by Julien Kaspar), the body
  of the field celebrant on night maps (her alb, sleeves, cord, veil and lamp made in Blender).
- **Freesound**: *Short Church Bell Ring with Rural Ambience & Birds* by TSP-Talk (the vespers bell) and
  *Humming in A Minor* by xkeril (the chant at the Quenching), both CC0.
- **JangaFX** (jangafx.com): the EmberGen simulations *Small Camp Fire* and *Ground
  Explosion* from their free VDB library, rendered in Blender into the fire and
  explosion flipbooks.
- **OpenGameArt** music: *At Home* by wolfgang, *First Light Particles* by yoiyami,
  *Contemplation* by Joth, *Peaceful Forest* by Samza, *Calm Ambient 1 (Synthwave 4k)*
  by cynicmusic and *A Place I Call Home* (JRPG Pack 4 Calm) by Juhani Junkala (the
  calm set); *Insistent*, *Pressure* and *Menace* by yd (tension); *Battle Theme A* by
  cynicmusic, *Determined Pursuit* and *QaziJamJam* by Emma_MA and *Epic Boss Battle*
  (Boss Battle Music) by Juhani Junkala (combat).
- **OpenGameArt** recorded sound the weapons (the Mech's included) and felled trees are built from: *The
  Free Firearm Sound Library* by Ben Jaszczak, Brian Nelson, Kevin Heras and
  Matthew Nanney (every gun recorded from beside the shooter and again at a
  distance), *tree chop fall thud* by kheetor, *Tree Creaking* by AntumDeluge
  (from a sample by Department64), and *100 CC0 metal and wood SFX* and *75 CC0
  breaking / falling / hit SFX* by rubberduck.
- **Fonts**: *Inter* by the Inter Project Authors (SIL Open Font License 1.1),
  *Roboto Mono* by the Roboto Mono Project Authors (Apache License 2.0) and
  *Cinzel* by the Cinzel Project Authors, Natanael Gama (SIL Open Font License 1.1;
  the HUD's headings).

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

## Tactical combat

Combat was rebuilt so that soldiers fight like soldiers: the player gives the same orders, and units
decide how to carry them out. This is the game's only combat since phase 8 (the old rules were deleted
then); where an older part of this file says otherwise about fighting, this section is the one that holds.
So far, for infantry (rules and numbers: `Assets/StarForge/Scripts/World/CLAUDE.md`):

- **Shots can miss.** A round goes where it goes: an aim error, then the first thing on its line. A far
  or low target is a small one, and cover works by stopping the round. Rounds never hurt their own
  side; they fly past friends into whatever is beyond.
- **Sight is real.** Hills, rocks, buildings and trees hide what is behind them, for both sides and the
  AI alike. Soldiers lying down, standing still or in the woods are noticed from less far off, and a
  gun firing gives its owner away.
- **The pace is slower and the guns reach further:** a rifleman walks 3.5 m/s, shoots to 35 m, sees to
  45 m and dies in three or four hits.
- **Soldiers think for themselves.** Fire near a man makes him crouch, then pins him flat, sooner the less
  brave he is; the timid may freeze or bolt for a few seconds. In contact he runs for cover, leans out to
  shoot and ducks back, lies down in the open, and on an attack bounds from cover to cover. Orders come
  first: a new one takes him out of cover at once.
- **Soldiers near each other are a squad.** There is nothing to train, select or manage: men sent together
  or standing within about 15 m act together, in eights, and leave by drifting past about 30 m. In contact
  they take turns, half moving while the other half stops and shoots, and one half works round the flank.
  On the way up a man goes ahead with the rest a little behind; a man alone goes crouched.
- **A plain right-click lets them choose the approach; a double right-click means hurry.** Sent somewhere
  plainly, a soldier in contact keeps low, bounds from cover to cover towards the place and shoots while
  stopped. Hurried, he runs at 6 m/s and looks at nothing, and only being pinned stops him.
- **They tell you how it is going.** "We should fall back" and "we need air support" arrive as text in the
  comms feed, with no button to accept. Clicking one looks at them and selects them; what to do is yours.
- **They carry more than a gun.** All five infantry types are on the new rules. A Trooper has a pistol, a knife, two frags, a smoke grenade and a flashbang; the others their own gun, pistol, knife, a frag and a smoke grenade, all restocked near base. Nobody orders any of it: a frag goes at men behind cover or bunched up, flash at men about to be rushed, smoke when pinned or caught in the open, and it blocks sight for both sides. Grenades and shells can hurt friends, so a man looks where they will be first, and his squad waits for it to go off. Guns have magazines, never an empty pouch: a Trooper reloads every twenty rounds, a Marksman every five, and a man reloading near an enemy draws his pistol. The pistol is for when the main gun cannot be used, reloading, on the run or too close for it; the knife is for arm's length. Pistol, knife and grenade are in his hand when he uses them.
- **The Gunner** carries a belt-fed gun: slow, stops to fire, slow to reload, and pins a man at 26 m in two or three seconds where a rifle takes far longer.
- **Wrecks and tanks are cover.** A tank's wreck stands where it died, stopping rounds and sight, for at least a minute and for as long as someone is behind it or a fight goes on round it; a friend's tank shelters the men behind it.
- **Nobody is perfect.** Now and then a man picks poor cover, stays put, pushes on under fire or throws a grenade at a lone target, and a squad's turn goes wrong; a green squad does it about one judgement in four, veterans one in twenty-five, and a green man with a veteran in his squad judges as the veteran.
- **A report says who and where** ("Juliet: 4 men 150 m north-east of our base"), and pointing at it rings those men on the map.
- **They have real bodies:** a rigged soldier crouching, crawling, aiming, firing, reloading and drawing a pistol, its 25 clips moving
  the legs and trunk from KayKit's animations with the rifle in hand-defined poses, one material, so
  200 of them cost a fight about 3 fps. A real match with all of this ran 41.3 fps on High, at the sharper resolution High now aims for, and stayed above 36 at its worst.
  Every other unit and building now draws the same way, one material a part with its surfaces in a small
  palette texture, which took 896 draws a pass down to 233 across the models and, with no visible change,
  a benchmark match from 49.9 to 51.0 fps (48.8 to 51.4 with a Mech in view; these and the rigged
  numbers above were measured before *High* gave its spare frames to resolution, see Performance).
- **The ground is something to fight over.** Every map gets low field walls, the broken
  outlines of ruined houses and a few ditches, laid out when the match begins. The low walls stand 1.2 m: a man ducked behind one is out of a round's way (in a trial not one of
  9,000 rounds from 10-30 m struck him, against a quarter to nine tenths of them in front of the same wall),
  and he comes up over it to fire, when he can be hit, then goes down again; soldiers vault low walls
  (vehicles go round, a Mauler drives through and breaks them), a ruin's tall corners stop everyone, and a
  man lying in a ditch or a shell crater, or behind a fallen tree, has something between him and the
  rounds. Over 200 random maps the walls never cut a base off from the other or from its ore, and no ditch
  made a vehicle go round.
- **Not there yet:** cover does not win fights by itself. Six behind a wall win about half their fights
  against eight coming over open ground (3 of 5 on the final code; the aim was 4 of 5, and about half is
  now the accepted result). Traced: attackers
  lying prone in the open at 30 m are about as hard to hit as a man firing over a wall, so the extra two
  rifles decide it. Four changes to how men fire from and at a wall were tried and taken out again, since
  none moved the result.
- **Tanks fight like tanks.** The Mauler, Lancer, Aegis and Ironclad carry a roof machine gun of their own
  that takes the infantry while the main gun turns on armour or a building, and two smoke launchers popped
  the moment an anti-tank round is fired at them (refilled at a depot or a Workshop). A tank holds its
  shell while its own men are by the target, since a shell can hurt friends now, and a wall or rock between
  a burst and a soldier shelters him as it would from a grenade. Idle or badly hurt, a tank backs away from
  rocket men out of their reach (unhurt, it holds instead while its machine gun is already on one), and,
  unless sent after a target, reverses out of its own
  smoke, front and turret toward the threat.
  Rounds fired straight at a soldier by a vehicle or the Mech are decided when fired, so cover stops them as
  it stops a rifleman's: in a trial a man behind a wall lost nothing to a Lancer and a Skimmer that cost the
  same man in front of it 1,307 hit points (on another map one wall let 793 through against 1,391; not yet
  traced).
- **Everything else has instincts.** Unarmed workers run from enemies they see within 20 m and go back to
  work, except within 25 m of their own Foundry, where they work on; creatures that are badly hurt break off and come back; the Mech's guns fire only at what its side
  can see, its shells can hurt friends, and its hitscan guns stop at cover.
- **Dogfights.** The Swift flies with speed and height as energy, chases from behind, is built to break hard
  when an enemy plane is on its tail (seen in a staged tail chase: it breaks within about a second), fires a cannon whose hits depend on how well its nose is on the target,
  carries two missiles and two flares, and rearms by its Airfield; the Kestrel stays a hovering gunship with
  rockets, a machine gun and flares. Two Swifts duel for about 35-50 s over eight to ten passes, alike at 1x
  and 8x.
- **The switch-over.** The new combat is what every match plays, and the old rules are gone. The AI
  commander hears its squads' reports: a squad that must fall back, away from home and outside a wave or a feint, is pulled back (rare: once in 20 evaluation games), and a squad held by
  armour gets the idle aircraft that can help.
- **After the first play (2026-11-23).** Smoke has an answer: a rifleman or machine gunner who loses his
  man behind a cloud keeps firing, slower and wider, at where his side last saw him, and a mortar drops a
  few bombs there (what the field manuals teach: fire into and through it, put indirect fire on it). A
  round strikes only what is really there, so this tells nobody where the man went. **Fire at ground**
  (F, or the card) holds the selection and puts 12 s of rifle and machine-gun fire, or five mortar bombs,
  on the spot you click, smoke or not. Smoke is thrown less: a squad waits 30 s between smoke grenades and
  throws one in the open only when nearly pinned, and a tank's two charges come back one per 30 s. The
  Swift fires four rockets in pairs at ground targets. Rounds no longer circle their target at low frame
  rates or when the game is sped up (a homing round looped at 3x in 17 of 150 shots), and rifle fire at
  aircraft is judged when fired rather than all but always hitting: six riflemen hit a circling Swift
  with 48% of their rounds (99% before). **The Mech's sensors see through the fog of war**: within its
  sight (48 m, more with Targeting or a Sensor Mast) nothing hides from it, lying down, in the woods, in
  smoke or behind a rise, and what it senses its whole side sees. Its guns reach 1.3 times as far (never
  past its sight; the flamers as before), since rifles and the Mauler had come to out-reach most of them,
  and a gun that cover stops picks a target it can actually hit; it also hits armour a little harder and
  its hull is a little thicker. A bare Mech now takes about 6 Maulers or 10-11 riflemen to bring down (it
  had fallen to under 4 and about 6).

`Editor/TacticalTrials.cs` holds the checks; `Assets/StarForge/Scripts/World/CLAUDE.md` the rules and
their numbers.

## MacBook Neo tuning

- A new map every match costs under half a second at load in the player: heights,
  splat weights, relief occlusion and the land beyond the rim are computed on
  worker threads, and the NavMesh bake of a 256 m map takes about 0.1 s.
- Forward+ rendering, 2× MSAA (tile memory on Apple GPUs), B10G11R11 HDR,
  two 2048 shadow cascades over 150 m, depth texture on, opaque texture on (the
  water refracts the lake bed through it), SRP Batcher.
- **Adaptive resolution** lowers render scale under sustained load with FSR
  upscaling, and backs off exponentially after failed climbs so it cannot
  oscillate (the lesson from the original's controller). With vsync on -- as a match
  always is -- a frame is shown for one refresh or two, so it judges the load by how
  often a frame misses its refresh (past the preset's limit -- 60% on *High*, 15% on the
  others -- it steps down; after a clean spell under a lower share, 35% on *High*, it tries a
  step up), not by frame length: a vsynced frame never measures under
  16.7 ms however light the load, so the old rule could never see room to climb, and one
  heavy fight left the picture at its floor for the rest of the match. The share settles
  well under the limit: on *High*, near 44%, about 42 fps.
- **Three graphics presets** in Settings ▸ Graphics, remembered between runs, each
  trading the three things that actually cost on a fanless laptop: how far
  shadows are drawn, how much ground cover is grown, and how far the adaptive
  controller may drop before it gives frames back. *High* is the measurement
  below: the 150 m of shadow and full grass it had before the Mechs, with the render
  scale free to go down to 0.55 in the heaviest fights and let miss up to 60% of refreshes
  before it steps down: 35 fps is enough here, and the rest goes into a sharper picture;
  *Balanced* shortens shadows to 95 m and thins the grass to 0.6; *Battery* also drops
  MSAA and caps the render scale. Every preset aims at one 60 Hz refresh. (For a while,
  *High* aimed at 21.5 ms -- "about 46 fps", with 160 m of shadow, full grass and a floor
  of 0.80 -- which measured 47.6 fps uncapped; in a real match, with vsync, a 21.5 ms frame
  waits for the second refresh, and it ran at 39.) Bloom is clamped, so a single
  overbright pixel cannot bloom into a light of its own.
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
- Switching between full screen and a window (Settings ▸ Graphics ▸ Display, or the
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
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 420 -sfplay -sfbenchout /tmp/starforge_bench.txt
```

It plays AI vs AI and writes average and percentile frame times, what the simulation
cost, and the render scale. Add `-sfplay` to run with vsync and the 60 cap, as a match does:
that is the frame rate a player sees, and the one to compare builds on. Without it, it runs
uncapped with vsync off.
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

**With the Mechs**, measured the way a match plays. The benchmark used to run uncapped with
vsync off, which is not how anyone plays: `-sfplay` keeps vsync and the 60 cap. Over 420 s --
long enough, with the two-minute drop it had then, for both Mechs to land at about four minutes and fight, the camera following the
heaviest action -- on the MacBook Neo, plugged in. (Since 2026-11-04 the drop takes seven
minutes and the Mechs land at about nine, so a 420 s run no longer has one: a run that is to
include them for as long as before needs about 740 s, and no number in this section was measured that way.)

**The frame-rate aim today.** Once 35 fps was enough (2026-11-04), *High* stopped holding about
50 and gave the difference to resolution: it now steps down past 60% missed refreshes and tries
a step up under 35%, where it was 28% and 10%. Seed 1000, Unity 6000.6.8f1, battery full, one
build, each aim twice in mirror order:

| Steps down past / climbs under | Average | Frames with a Mech in view | Render scale (with a Mech in view) | Worst 5 s | Frames over 40 ms |
|---|---|---|---|---|---|
| **60% / 35%, *High* today** | **41.5, 41.8 fps** | **41.3, 42.2 fps** | **0.76, 0.77 (0.72, 0.73)** | **36.4, 36.8 fps** | **39, 39** |
| 28% / 10%, before | 51.4, 51.2 fps | 51.7, 50.6 fps | 0.63, 0.64 (0.59, 0.61) | 46.2, 45.2 fps | 40, 28 |
| 70% / 45%, measured and not taken: its worst 5 s fell under 35 | 38.8, 39.4 fps | 40.2, 38.9 fps | 0.83, 0.80 (0.77, 0.77) | 34.6, 34.2 fps | 36, 48 |

That is 1.45x the pixels for about 10 fps, in a Mech fight as in the match's average. The preset
itself, with no aim flags: 42.0 fps, 41.8 with a Mech in view, scale 0.78 (0.74), worst 5 s 36.6;
a 20-minute match on another seed, 41.9, 41.6, 0.78 (0.73), 37.0; in the rain, 41.7, 41.9, 0.77
(0.72), 36.6. At 42 fps on a 60 Hz panel frames cannot all stay on screen equally long, so expect
motion less even as well as slower. With 200
extra rigged soldiers following the fight, one run each: 41.4 fps (41.9 with a Mech in view),
scale 0.67 (0.62), worst 5 s 37.0, against 49.0 (46.5), 0.56 (0.55), 41.6 at the old aim, which
had run out of resolution: there the soldiers came off the frame rate, at the new aim off the
picture, with 0.05 of its scale to spare.

**Before that**, all at or around the old aim:

| | Average | Frames with a Mech in view | Render scale (average) |
|---|---|---|---|
| **High** (150 m shadows, full grass, steps down past ~28% missed refreshes, floor 0.55), two runs | **20.3 ms (49.3 fps)** | **21.2 ms (47.3 fps)** | 0.62 (0.56 with a Mech in view) |
| the same with a 0.60 floor, before the Mechs were fitted out at the drop | 20.1 ms (49.8 fps) | 20.0 ms (50.1 fps) | 0.69 |
| the same with 160 m shadows | 21.2 ms (47.1 fps) | 22.1 ms (45.3 fps) | 0.68 |
| the same with 160 m and a 0.70 floor | 22.1 ms (45.3 fps) | 23.5 ms (42.6 fps) | 0.74 |
| 120 m, grass 0.85, steps down past 15% | 19.0 ms (52.6 fps) | 19.9 ms (50.1 fps) | 0.64 |
| *Balanced* (95 m, grass 0.6), battery charging | 19.6 ms (51.1 fps) | 20.7 ms (48.4 fps) | 0.63 |

The first row was that round's final build. Once the Mechs were fitted out for the fight at the drop,
the benchmark's got rotary cannons and missile racks against the armies they faced -- the
busiest weapons there are -- and frames with a Mech in view fell to 43.4-46.0 fps at the 0.60
floor. A missile now lays a smoke puff every 1.5 m for about a second (every 0.9 m for two,
several hundred soft puffs a salvo), a rotary cannon throws up a hit every sixth round rather
than every fourth, and the floor went down to 0.55, where only the heaviest fights take it.
(Before the second row, the same values given on the command line measured 50.0 / 49.6 fps. Two runs in between came out at exactly 20 fps: the Mac had locked
its screen, and behind a lock screen the game only gets about 20 frames a second shown.)
Runs made while the battery was charging came out about 3 fps lower than with it full, so
note the power state with every number.
The simulated fire and explosion flipbooks (with the explosion's 384-pixel frames), the blast
shoves, the falling casings and the cloth banners (then on the Foundries) cost nothing measurable: run in turn against
the build from before them (30 September), on *High*, plugged in with the battery full, the
old build averaged 49.0 and 49.7 fps and the new one 49.4 and 50.2 (with a Mech in view
46.3 / 49.3 against 47.2 / 45.5, within the old build's own spread). Once more with the final build (banner
check once a second, a cap on how long blasts keep a wreck lying, a quieter forest fire):
50.0 against 50.4 fps, with a Mech in view 47.6 / 47.0.
**The wider roster** (the new infantry, tanks, aircraft, workers, creatures and their
buildings, with both AIs fielding them) costs nothing measurable either. On Unity 6000.6.7,
High, plugged in with the battery full, the roster build averaged 50.0 fps (49.6 with a Mech
in view, render scale 0.67) against 49.5 (48.7, 0.63) for the build from 21 October before it.
On a second map (seed 1074, where one side goes the creature path; 540 s, run old, new, new,
old) it averaged 46.5 and 47.9 against 45.0 and 45.1 (with a Mech in view 43.9 / 47.5 against
41.3 / 42.7); a lone first run of it at 43.2 had come straight after two other benchmarks,
on a warm Mac. **The capes** (`-sfnocapes` turns them off) cost nothing measurable in a match: on seed
1000 with them, without, with them again, 51.1, 51.2 and 50.3 fps -- a match there promotes
few soldiers (3-5 capes at most, 2-3 of them cloth at once). The full budget, 24 capes as
cloth at once, has not been measured on the player; in the Editor, 24 against none in a crowd
of 200 made no difference beyond its frame-to-frame spread of a few milliseconds, and the
trimmed cape (210 cloth particles, was 182) the same: 12.8 / 13.3, 13.4 / 13.6 ms with cloth /
at rest, with 40 soldiers standing 0 frames over 0.1 s either way. 0
exceptions in all eleven runs.

**The birds, the trees' leaf cards and unit surfaces** (2026-11-19, Unity 6000.6.9). Eagles now soar on held wings and lean into their
turn, songbirds stand with their wings shut, a leaf card seen edge-on fades out instead of smearing into streaks round the crown, and a
unit's hit flash, damage, construction line and ore glow go on materials of its own instead of per-renderer overrides (which took every
working Digger, hurt unit and building site off the fast batched path: frozen 243 s into a match, 164 of 384 unit renderers, and
set-pass calls a frame fell from about 340 to about 120). On this benchmark none of it costs or gains anything that can be measured:
with the render scale held at 0.75, 44.1 and 44.5 fps without the changes against 44.2 and 43.7 with them (alternating runs, battery
charging), the render thread at 3.0-3.1 ms either way.

**The meadow, the leaves and fire on buildings** (2026-11-20). A turf under the grass, narrower blades that widen with distance, drifts
of colour and wildflowers, grass drawn from one GPU buffer, leaves with their own lighting, and a building fire that stands on the real
roof. Against the build of the day before, with the render scale held at 0.75 and the old combat rules on both (7-minute runs,
alternating, plugged in at 100%): 45.4, 45.4 and 44.9 fps before, 44.6 and 45.0 after. The first version read 44.7 and 44.3: its turf
worked out a noise for every terrain pixel, and taking the drifts from the variation texture the terrain already reads gave that back. So the
new look costs nothing that can be measured, and nothing was gained either: with the camera held on the densest wood, halving the
grass, a cheaper leaf shadow and skipping the opaque-colour copy each moved the frame rate by less than the runs differ among
themselves. Still-camera bursts at that wood: 1.7% of pixels vibrating with the grass's sun shadow taken per vertex or per pixel alike.

**The turf's own surface** (2026-11-20, a later build with the day's other changes in it). Under the grass and on burnt ground the
terrain now takes its detail from three top-down ground pictures made for this project with an image model. On one build, with the
textures on, off, off and on again (7-minute runs, render scale held at 0.75, plugged in at 100%): 39.9 and 40.9 fps with them, 40.9
and 39.9 without. Two runs of the same setting differ by more than the two settings do, so they cost nothing that can be measured.
Still-camera bursts at the wood: 1.6-2.0% of pixels vibrating with them over two runs, 1.7% without. A rule added with them draws any
face steeper than a unit can climb as rock whatever the ground painted there says; it changes almost nothing on the test map. One
cliff face that looks streaked from close up was traced to the rock picture being drawn 1.8 times taller than wide on walls. That
stretch stays: from the distances a match is played at it is what makes a terrace read as a cliff, and unstretched the same walls
came out darker and flatter.

**Battle marks, the lakes and damaged hulls** (2026-11-20, the same day's last round). Tread marks that lie once along a lane and at
most three deep on any pixel, blast marks and a damage mask for the hulls from pictures made for this project, a calmer lake surface
whose caustics are read only in the shallows, and a survey grid over ground nobody has seen. Against the build just before them
(7-minute runs, old, new, new and old again, render scale held at 0.75, plugged in at 100%): 42.8 and 41.5 fps before, 41.6 and 41.7
after, the render thread at 2.6-2.8 ms either way, and 43 and 26 frames longer than 41.7 ms before against 19 and 17 after. Half a
frame a second apart, less than the two old runs differ between themselves: nothing that can be measured, in either direction. The
survey grid is not in that figure, since a benchmark is watched without fog of war; what it costs in a played match has not been
measured. Still-camera bursts at a base, four a build: 12.7-14.7% of pixels vibrating before, 14.0-14.9% after (a base is full of moving
vehicles and waving grass, so far more than at the wood, and the same on both). Single-frame flashes on still ground came out anywhere
from 258 to 491 before and from 272 to 678 after; placed on their frames (722 of the 755 in the new build's last two bursts looked at),
they are the lit edges of vehicles driving past, not sparkle on the ground or on the tread marks.

The median frame is a full 60 Hz refresh (16.7 ms), and the average is how often one is
missed. Getting there took three findings, each measured on this benchmark:

- *High* had been re-aimed at 21.5 ms ("about 46 fps") and measured uncapped at 47.6 fps.
  With vsync, a frame that misses 16.7 ms is shown for 33.3: it ran at **39 fps** (35.6 with
  a Mech in view), the render scale pinned at its floor. Aimed at one refresh
  again, 45.2.
- The adaptive controller could not climb back under vsync (see above), so every match
  after its first big fight stayed at the lowest resolution.
- A Mech in the picture costs about 5 ms of GPU -- mostly the fight it is in: mortar and
  railgun blasts, felled trees, fire -- and at 0.60 of the resolution the frame still missed
  its refresh, so the cost that does not shrink with resolution had to come down. With a
  Mech in view: 43.3 fps at 160 m of shadow and full grass, 46.1 at 110 m, 49.2 at 110 m
  with grass at 0.7.

The report also says what each long frame spent where: of 1,879 frames over 40 ms in a
420 s uncapped run, 14 had a slow simulation step (the AIs and both Mech brains together stay
under 6 ms at p99, the Mech brains under 1 ms), 10 a garbage collection and 7 a NavMesh island
build. Uncapped runs on this Unity version come out bimodal -- frames of 8 ms and of 60-80 ms
-- so compare builds on `-sfplay`. Unity's per-frame GPU timings on Metal are not usable for
this: they overlap from one frame to the next (a GPU time longer than the frame).

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

## Measurement records

Dated measurements, A/B histories and evaluation tallies that the area notes (the folder `CLAUDE.md` files) point to as `README › <area>: <block>`. The rule, reason or target each one supports stays in its area file; these are the records behind them, kept as they were written.

### World: Simulation

*From the World notes (No managed garbage in per-frame paths, and UnityEngine.Object.name is garbage (a new strin…):*

2026-11-20, AI against AI: 28.0 KB and ~200 allocations a frame before, 8.1 KB and 50 after (the rest: `NavIslands` triangulation, `Commander.OwnOf`, `Perception.Forget`'s `RemoveAll` closure, the HUD).

### World: Targets, Diggers, match end

*From the World notes (A target a unit picked up itself gives way (Unit.UpdateCombat): idle, holding, attack-movi…):*

-- current rule 6 wins in 25 (0/5 in a Full eval, then 1/5, 1/5, 3/5 and 1 of 5 seed-1148 replays), old rule 5 in 20 (2/5, 1/5, 1/5, then 1 of 5 replays); the current rule lost twice, both on seed 1148 in a Mech base trade (its army collapsing as their Mech pushed in), but five replays of that seed under each rule came out the same (1 win, 4 caps, no loss), so 2 losses in 25 against 0 in 20 is chance at this sample. Every cap under either rule is the Macro side's Mech holding its bay (43-95%, 40-159 kills);

### World: Tactical combat

*From the World notes (Fight does not reach its target on today's maps. Six among cover against eight over open g…):*

An earlier 10 of 12 was an artefact: plain soldiers kept suppression for good, and their aim with it.

*From the World notes (Checks (Editor, 2026-11-04, map random per play, TacticalTrials): Squads: five together on…):*

- Checks (Editor, 2026-11-04, map random per play, `TacticalTrials`): `Squads`: five together one squad, three 50 m off another; two of five sent off stay two after 1.7 s, three left; a squad and a fresh man sent, the same squad remembering; plain Move 3.5 m/s, hurried 6.3; out of ToCover hurried running after 0.4-0.6 s, pinned under three rifles still down after 10 s; **eight attack-moving at six: 1 squad, over half moving for more than 0.3 s in 1.3% of 2,076 contact frames (target under 5%), moved in 21 of 21 turns, 26 m gained a man in 40 s; plain Move through them 0.0% of 2,134, 20 of 21 turns, 31 m, 2 of 24 there after 40 s (both sides at 400 hp), 1,092 rounds**; the halves 4.0 and 6.0 m off the line, 3.6 and 5.0: **flanking is slight on open ground**; four against ten: FallBack after 2.1 s, once in 25 s, a click on the line selects the 2 alive; four held by a Mauler: NeedAir once in 16 s. `SquadWalk`: 3.5 m/s ahead, 2.8 the rest, 5-6 m ahead; a man stopped to shoot a Foundry, or on a way round, stays behind; **sent after one 38 m off, 0 of 8 lost the order or target in 20 s with 33-45 stops, three fields**. `Orders`: **Move from cover under three rifles left at 0.1 s but pinned and 21 m off after 25 s** (it got there in 12.3 s before Move was careful: hurry is the answer, and only suppressing them unpins him); Hold 0.2 m, 11 shots; the same attack-move asked every 0.5 s under cover or pinned 93%, 1.1 m; Idle stayed Idle. `Match`: 480 s at 8x, 0 exceptions, 1,440 rounds, 80% struck, kills 103 and 83, 5.2 squads formed a minute, **reports 10 and 7, 2.1 a minute both sides**. Switch off, Quick on this code: 3 of 4 won, Macro capped at 720 s, 0 lost, 0 exceptions (the last Quick 4 of 4; caps against Macro came in earlier Full runs too, so one game says little; the off path was also read through by the reviewer: every new branch inert). `CoverGoal` (sent under three rifles from 12 m off to 1.2 m behind a rock, five rocks): on three pinned on the way, order still Move after 25 s, 1.9-10.2 m short; on two the order ended after 2.6-3.6 s, one staying put, one moving 4.8 m on to other cover as an Idle man (target under 2 m, not met); none walked out of cover for the last metres. `Restart`: three matches in one session by the menu's way back, the new rules each time, 0 exceptions. **Unity 6000.6.8f1 with the merged models:** Quick with the new combat on (then asked for with `EvalNewCombat`, deleted since; the Commander was not tuned to it): 3 won, Turtle capped at 720 s, 0 lost, 0 exceptions, 1,583-3,330 rounds, 37-74 squads formed, 10-21 reports a match (about 2 a minute both sides). Player `-sfbench 420 -sfplay -sftactical`, High, AC charged, one run, not paired: 51.0 fps, 51.0 with a Mech in view, scale 0.65 (0.60), 1,788 rounds, sight 0.100 + lines 0.046 ms a frame, worst 78.7 ms, no stall. With squads on: `Cost` 253 units, 200 soldiers standing, sight rules 0.290 ms a frame (target under 0.5); `Walls` behind its own Garrison 1 round on the wall in 38 (0 in 15 runs before), the way round 33-60 m by path, first shot after 4.8-11.0 s, gave up never, 3 of 3; on ground with no path stood firing where a round reached them, 4 of 4. `FightWide` (`UseSeed1000` first; about 11 minutes): six among cover against eight, both thinking, with squads on, 3 of 12 (2 of 12 without; target 4 of 5, still missed: phase 5's low walls judge it), both plain 1 of 12 (4 of 12 before: the plain sides have no squads, so that is chance), 27-72 s against 11-27 s, and the same one field never makes contact.

*From the World notes (Checks (Editor/TacticalArmsTrials.cs, Editor, 6000.6.8f1, 2026-11-04, map random per play)…):*

- **Checks** (`Editor/TacticalArmsTrials.cs`, Editor, 6000.6.8f1, 2026-11-04, map random per play). `Grenades`: 6 open fights of 22 s, 13 frags and 13 smoke (20 smoke before the squad's wait went to 15 s), **0 frags with a friend in reach at landing or at burst, 0 friends hurt**; friends mixed among the enemy 18 m off: 1 frag, 0 with a friend in reach; a frag thrown at the middle of a 1.6 m rock cost a man 3.1 m from it 52 hp; a frag on a friend at 10 hp: dead, +1 friend hurt, +1 lost, 0 enemy kills, 0 alarms. `SmokeSight`: three fields, seen before, unseen both ways during, the ground behind unseen, 0 rounds through it in 6 s, seen after; a man under three rifles popped his own 4 of 4, after 2-7.5 s. `Flash`: 3 and 5.5 m stunned, 10 m not, behind a rock not (men who ran first were not, as they should not be). `Sidearms`: a Thumper at 5 m 14 pistol rounds and no mortar, at 20 m the mortar; a Rocketeer or Marksman at 4-5 m the pistol until he has run clear, then his gun; a Scorcher at 14 m the pistol; at 1.4 m the knife 8 times in 8 s; a Rocketeer by a Mauler 3 rockets, no pistol; a Thumper 2-3 m from a man behind a rock takes the one in the open with its mortar (48-72 hp); a Trooper hurried past an enemy 7 pistol rounds. `Restock`: 2/1/1 back by a Garrison in 45 s, 0/0/0 in the field. `Types`: a Thumper 0 shells with a friend by the target, 5 without; a long rifle strikes 94% at 30 m against the rifle's 47%. `Look` (`Temp/arms_look_*.png`): five rigged types in three stances, frag, flash, cloud. Rule trials on this code: `Orders` Move from cover there after 11.1 s, Hold 0.6 m, repeated attack-move 78% under cover or pinned; a Move while pinned reached cover 7 m short of its place and ended there (`CoverIsThere`, 6 m). `Pin` three rifles 2.2-3.8 s, one 4.3-13 s. `Hits` real firing at 20 m 66-70% at 1x, 77% at 8x. **`FightWide`: 4 of 12** (3 with squads alone, 2 before; target 4 of 5, for phase 5's low walls), 12 frags, 23 smoke, 4 flash, 19 pistol rounds, 0 stabs, 0 friends hurt, 2 men stunned; it can take over 11 minutes (`wait` with a long limit). **Player** `-sfbench 420 -sfplay -sftactical`, High at 0.60/0.35, AC charged, one run: 41.3 fps, 42.9 with a Mech in view, scale 0.72 (0.64), worst 5 s 36.6, worst frame 67 ms, no stall, 0 exceptions (41.6, 41.4 before the review's fixes). Quick with it on: 4/4, 0 exceptions. **Switch off, Full on the final code: 19 of 20 won, 1 cap (Macro), 0 lost, 0 exceptions, desertion in 1 of 20.** **The last batch** (2026-11-18, all 0 exceptions, after the review's fixes; the rule trials run with mistakes off, `RulesOnly`): `Magazines` in 30 s: Trooper 41 rounds at 20 m (at most 43 by its cycle), Marksman 9 (9), Rocketeer 15 (16), Scorcher 78 ticks (83), Thumper 10 (11), Gunner 110 (107 on average: it starts on a full belt), each 2 reloads or one a round; a Trooper with an enemy 11 m off 10 pistol rounds while reloading (overlapped reloads before the fix: Marksman 11, Rocketeer 17, over what the old cadence allows). `Mistakes` (three fights of twelve a rank): 23/18/9/3% of judgements; a green with a rank-3 man in his squad judges as rank 3. `Hulks`: a dead Mauler leaves 1 record (radius 1.3, height 1.5) and 1 wreck; on the field with the least else to get behind (0 rocks or trunks within 13 m) a man 5 m beside it under three rifles is in cover 1.9 m from its middle after 18 rounds, the place behind it walkable, reachable and sheltering 3 of 3 lines; on fields with folds or rocks he may take those instead (5.9 and 11.5 m off: a fold he could shoot from scores higher, by design); with a fight 9 m off it is still there 84 s after the death; gone 20 s after the fight ends (target 20-22), its wreck 15 s later; a dead Gorehorn leaves 0; a man by a friend's live Mauler under three rifles is in cover 1.9-2.1 m from it in 4 of 5 runs, the tank taking 252-399 hp of rounds. `Gunner`: pins a man 26 m off in 1.7-2.8 s, 4 of 4, against a Trooper's 1 of 4 in 20 s (each run prints rounds, peak suppression and how long he was seen: a man lying down at 26 m drops from sight); `GunnerClassic` refused (a trial deleted with the old combat). `Squads` (mistakes off): over half moving 0.2-1.1% and 0.6-1.0% of contact frames; four against ten FallBack after 2.2-3.3 s, the line rings those alive while pointed at and 0 after, a click selects them; four held by a Mauler NeedAir (FallBack-outgunned before the order rule). `Sidearms`, `Grenades` (13-20 frags, 0 with a friend in reach at landing or burst, 0 friends hurt), `Types`, `SquadWalk` as before; with mistakes on, `Sidearms` saw a Marksman 30 m off draw his pistol after a poor cover pick took him within 15 m. **Not exercised:** a thrower dead before his burst and a deserter in a burst (read, not run); capes on the rigged types; up to 12 clouds' cost; a Marksman retargeting from a man behind a rock (the staged field gave him no line to the other either); `Duels` under the new rules (phase 8).

*From the World notes (Phase 5 checks (Editor, Unity 6000.6.9f1, 2026-11-18, after the review's fixes, 0 exceptio…):*

- **Phase 5 checks** (Editor, Unity 6000.6.9f1, 2026-11-18, after the review's fixes, 0 exceptions). `BugTrials.MapSweepCover` (200 seeds, 170 s; the deformer is `Init`ed for each generated map, so the ditches are really dug: before, they went into the first map's detached copy and the sweep said nothing about them): bases not joined 0, a start off the NavMesh 0, ore unreachable 0, clusters dropped 0, wall middles a ground unit stands on 0, seeds with no soldier's path 0, **ditches a ground unit goes round 0** (of 687); 10.4 clusters and 3.4 ditches a map, 33 seeds under 8 clusters (4-7). `MapChecks.Report` with cover, map 1000: low walls 38/38, ruin walls 21/21, ore and structures 43/43, bases joined; trees 158/159 (as on the classic map). `TacticalCover.Map`: of 32 low walls a round at a crouching man behind stopped 32, one from a standing gun over it clear 21 (the rest: that far point on uneven ground, or another wall of the cluster). `TacticalCover.Vault`: 4 of 4 vaulted and there, the vehicles' way the longer each time; a round at a man lying on a ditch's bottom stopped 5 of 5. **`TacticalCover.Shots`** (rounds by `DecideShot` at a man on a low wall's cover spot, from a standing rifle across it): ducked 0.0/0.1/0.8% at 10/20/30 m (2,700-3,000 rounds each, `DuckedClear` 1.02; 0.0% at 1.04; with his head at 1.15 m 0.7-8.8%, and the review's 15% at 20 m with the old 1.0 m walls), up over it 78/30/17% (73/20/10% with a 1.6 m rise, tried 2026-11-20 and taken out, below), the same man in front of the wall 96/48/28%; a man in a wall's slab has a line wherever he has one just in front of it (38 of 38), and fired rounds struck him 81% from its faces against 84% in front of it (they stopped in the wall before `inside`). **`TacticalCover.Crush`**: a Mauler through a low wall 3 of 3, smashed after 2.0-2.4 s, the piece gone, rounds and the ground NavMesh through the gap 2.5 s later, a friend on its band unhurt. `AgentPlay.IslandReport` on a cover map: 51 islands, 0 joined to the main one. `Cost` 0.190 ms a frame (before the fixes). Quick with the switch on after the first fixes: 3 of 4, Macro capped, 0 lost; and after the second: 3 of 4, Macro capped at 720 s, 0 lost, 0 exceptions. Switch off, Full on the code before the fixes (they touch no off path): 17 of 20, 3 caps (Macro 1, Turtle 2), 0 lost, desertion in 2 of 20. **Player** `-sfbench 420 -sfplay -sftactical`, High at 0.60/0.35, 6000.6.9f1, AC charging at 75%, one run, map 1000 (10 clusters, 5 ditches): 41.0 fps, scale 0.76 (0.70), worst 5 s 37.0, worst frame 125 ms, 1,180 rounds, sight 0.112 + lines 0.095 ms a frame, 0 exceptions. **`TacticalTrials.FightWalls` (six behind a wall of the map's, eight attack-moving at them from 48 m, both thinking): 2 of 5 fields and a draw over four maps after the two reviews' fixes (1 of 3 and a draw after the first, 1 of 1 and 0 of 1 after the second; 1 of 9 before; target 4 of 5, carried from phase 2): missed.** The six were in cover only 23-63% of each fight even with room for three a wall and the pinned keeping their cover, and in cover struck 6-22% (mostly while up over it); one field a map is thin evidence. Why so much of the fight is spent out of cover is not traced: phase 8, or the user's call when playing. **Finishing pass (2026-11-20, `TacticalTrials.FightWallsDiag`: the both-thinking pass alone, saying for the six what each state cost them, when each first got into cover and who fired first; one field or none a map, so fresh maps by `editor.sh stop`/`play` in a loop, `Art/Source/staged/combat/fightdiag_*.txt`):** on the phase 8 code 3 of 5: missed, and **the user accepted about half as the target (2026-11-20)**, so it is not to be tuned further. Two rules were tried and taken out after the review, neither showing a measured benefit (Fisher p 1.0 and 0.42): staying up over a wall to fire while suppression is under 0.15 (6 of 10; its return to the rhythm could duck a man for one 0.26 s look, and it reached boulders, ditches and crater rims as well as walls, since `RiseOver` is a line's geometry, not a wall's tag), and counting a risen man 1.6 m tall (with it 6 of 14 and 2 draws; it left his eye, his spotting point and the drawn rig at full height)); and, last, a man up over cover resting his rifle on it with a prone man's steadiness (8 of 14 and a draw, 57%). Per round the rise did what it says (`Shots` above), but the fights did not move: **the eight, stopped beyond 15 m, lie prone, and a prone man at 30 m is about as hard to hit as one firing over a wall** (rounds struck about 20% either way), so eight against six is decided by numbers. Per share of the fight, up over a wall cost the six 6-8.5 hp a percent, at a rock's edge 2-3.5, down behind it about 1; the fields the six lost they fired fewer rounds than the eight (39-83 against 82-110). Marking upright men before prone ones at range made it worse (3 of 11: the eight then took the risen six) and was taken out. Call `ClearSeed` first: `UseSeed1000` holds for every Play after it, and map 1000 offers no wall site the trial takes.

*From the World notes (Tanks (phase 6, Unit.Vehicle.cs, GameWorld.Vehicles.cs, TacticalRules.IsTank: Mauler, Lanc…):*

Checks (`Editor/TacticalVehicleTrials.cs`, `TacticalVehicles.*`, 6000.6.9f1, 2026-11-20, 0 exceptions): `Guns` machine gun 100% of 93 rounds at the men, main gun 91% of 11 shells at the Lancer (targets 80%+; since the range pass its Lancer, 30 m now, shot first and the Mauler smoked itself in, 7 rounds and 1 shell a field, so the Lancer holds fire in it: 100% of 80 and 88% of 8 after the deletion, with the machine gun's longer reach); `Smoke` 5 of 5 within 0.1 s of the first rocket, 2 charges, a Workshop's refill; `Friends` 0 shells while a friend stood by the target, 5 once he moved off; `Backoff` (an idle Mauler, two Rocketeers sent at it; the fields follow the map, so read it over several plays) **with hold-and-suppress 4, 3, 4, 2, 3 and 2 of 5 over six maps** (18 of 30 fields; the first three: in their reach 61% of the time against 78%, hull lost 192 against 258; target 4 of 5 a map, met on two of six). The pure back-off read 5 of 5 on one map and then 2-4 of 5, 8 of 15 better on both counts, its hull 221 against 186: it cut the time in reach but not the hits.; `Instinct` Diggers ran 5 of 5 and 4 of 5 went back to work, Skitters at 25% broke off and came back 3 of 3 (after the deletion: the trial's Diggers all mine within 25 m of their Foundry, where they work on by rule, so the trial now also puts one Digger to ore 35 m+ from every Foundry of ours: it ran from the rifleman and went back to work; Skitters broke off 2-3 of 3 and came back 3 of 3); `CoverRounds` (guns held 10-15 m off, a spotter of theirs) behind a wall 0 hull lost against 1,307 in front on one map, 793 against 1,391 on another (open: a wall that leaks). On-path balance (`RosterTrials.DuelsPhase6`; `MechDuel.RunBaseFreshAll`; while the switch lived, `TacticalTrials.NewCombatOn` first): before phase 6 Mauler 10-6, Lancer 11-5, Aegis 7-9, Ironclad 9-7, Skitter 5-11, Spitter 11-4-1, Gorehorn 6-10, the bare Mech to 6.3 Maulers and 6.4 Troopers (10.2 under the old rules); after its rounds (before the review's fixes and phase 8's balance) Aegis 12-4, Ironclad 12-4, Skitter 2-14, Gorehorn 4-12, the Mech to 6.3 Maulers and 4.9 Troopers; **on the final code** (2026-11-20, after phase 8's balance and every fix) Mauler 10-6, Lancer 10-6, Aegis 8-7-1, Ironclad 10-6, Skitter 8-7-1, Spitter 10-6, Gorehorn 7-8-1: the x0.6 and x1.3 hold. `Guns` on the final code: machine gun 100% of 75 and 94 rounds at the men, main gun 88% of 8 and 90% of 10 at the Lancer. `TacticalVehicles.Outranged` (an idle Mauler, then a lone Sentinel, three riflemen sent at it from 40 m): answered within 10 s of the first rifle round in 5 of 5 and 4 of 5 fields, but the riflemen close in to 10-28 m on their attack order, so it reads the tank's or tower's own reach, not an answer from beyond it.

*From the World notes (The four-line budgets are spent best first, on a deferred queue (NearestShootable, TankGun…):*

**Tried and taken out, in order:** the grid's order (four men behind a wall used every look); best first with a hard four (four nearer men behind cover and the gun was silent every look); a place in the list handed on and aged after 1 s (committed fc5e0e5: with five or more armed men blocked the fall-back never reached a Digger, a Lancer between rounds looked at the same four, and risers let the fall-back hand over an unarmed target while an armed man showed); a 2.5 s clock memory (a riser stayed blocked through his next time up); a memory stamped on place (any movement voided it: the hard four again for movers); the first queue (a 1 s wait before a retry left every other look empty with four or fewer, and a crowd that kept changing stance starved the rest); a 30 s reuse of memory slots (it evicted men still in the list).

*From the World notes (The four-line budgets are spent best first, on a deferred queue (NearestShootable, TankGun…):*

**Checks** (`TacticalVehicles.*`, map 3469857940 by `UseLineBudgetSeed`, which holds for every Play until `TacticalTrials.ClearSeed`; the rest on map 1000; 2026-11-21, committed rotation against the queue on one tree): `LineBudgetFallback` (five men flat behind the wall, an enemy Digger past its end farther than they are; a field without the rifleman's line to it is dropped) never, never and 1.0 s against 0.8, 0.5 and 0.6 s (target 1.5 s); `LineBudgetRiseFlat` (four flat at the wall, four rising and ducking behind them) the gun on a man while he is up 20% against 42% on the field whose ducked men are hidden (pass 42%), 25% and 25% both ways on two fields where the ducked men keep a line (the staging, not the rule); `LineBudgetRise` 27-46% against 33-43% (the model reads them equal; 10 s fields); `LineBudgetWalk` (two men walking past the wall's ends; noMind men do follow `CmdMove`) 98-100% against 100%; `LineBudgetHide` 0 changes both; flat, mirror and crouched 0, 1 of 40, 0 both; `Guns` 100% of 84 and 88% of 8 against 100% of 115 and 100% of 11; `CoverRounds` 515 against 1,237 and 507 against 1,249; `CostMoving` in alternating pairs, walking lines 0.231 and 0.395 ms against 0.467 and 0.386 (the run-to-run spread), pickers 0.023 and 0.048 against 0.055 and 0.045; Quick 3 of 4, Turtle capped, 0 lost, 0 exceptions. **After the seventh review's fixes** (the line-free candidate, no slot reuse; the probes repaired: risers held in cover each frame and their places checked, 30 s fields, the gun's share of any-up frames; Walk's two lying flat behind four flat men; Fallback only where all five are seen), queue on map 3469857940 (the rotation's readings of these three ran on other maps, the recompile that swapped the rule in having reset the seed: they show the direction, not field-for-field pairs): `LineBudgetRiseFlat` the gun on a man who is up 66 / 59 / 66% and 75 / 54 / 60% of any-up frames, **no target while one was up 0, 0 and 5 of about 630 frames** (the rotation 43 / 0 / 22%, one field silent the whole 30 s); the share itself depends on the field's list order and phase (four looks are one rise cycle), so a gun with unlimited lines reads 27-90% there: judge by the frames with no target; `LineBudgetWalk` 91 / 93 / 95% (rotation 79 / 93 / 98%); `LineBudgetFallback` 0.3-0.9 s on three valid fields (rotation never; the same-map pair above: never, never, 1.0 s against 0.8, 0.5, 0.6 s); `LineBudgetRise` cannot tell the rules apart (four men, four lines a look): means 54, 64 and 68% against 65 and 65%, noise; `LineBudgetHide` 0 changes; `Guns` 100% of 115 and 100% of 12 (after the follow-ups below: 100% of 77 and 104, main gun 7 of 9 and 10 of 11 shells, 17 of 20 pooled; the follow-ups cannot reach that trial, nobody dies and `PickOpen` runs only from `CoverStops`); Quick 2 of 4, Macro and Turtle capped at 720 s, 0 lost, 0 exceptions.

*From the World notes (The four-line budgets are spent best first, on a deferred queue (NearestShootable, TankGun…):*

**With the queue in** (2026-11-22, 0 errors and 0 exceptions): `RosterTrials.DuelsPhase6` Mauler 11-5, Lancer 10-6, Aegis 7-9, Ironclad 8-7-1, Skitter 10-6, Spitter 11-5, Gorehorn 9-6-1, within the duels' run-to-run noise (the final phase 8 code's two runs, differing only in the Mauler's reach, came out differently in 15 of the ~75 fights without a Mauler, and the queue's run differs from the second in 14), so the Aegis and Ironclad x0.6 stands. Full 15 of 20 won, 0 lost, 0 exceptions, desertion in 2 of 20: Rusher 5, Macro 4, Turtle 1, Harasser 5 of 5. **Turtle undecided:** the direct baseline (F10 on the rotation, fc5e0e5) won Turtle 5 of 5 at 749 s on average, the queue 1 of 5 at 857 s (about p 0.05); over six recorded Fulls Turtle read 4, 4, 4, 1, 5, 1, and the 1 of 5 before the queue was on the code before F10. A plausible way in: `ShootableNow`'s fall-back to anything now comes within a second where the rotation never fell back with five or more armed men blocked, so a besieger that gave up its target may take a structure and keep it rather than walk on. **Decided by the A/B** (`TacticalVehicles.EvalTurtle`: the Full's five Turtle seeds, queue / rotation / rotation / queue, 0 exceptions): the queue 7 of 10 won, 3 caps, 0 lost, 803 s on average; the rotation 7 of 10, 3 caps, 0 lost, 746 s (wins p 1.0; length p 0.38 by permutation, per seed -124 to +322 s; with the two Fulls 8 of 15 against 12 of 15, p 0.25).

*From the World notes (Switch-over and balance (phase 8). The new combat became the game's default, and then its…):*

**After the deletion** (2026-11-20, 6000.6.9f1, 0 exceptions): `MenuTrials.Walk` 0 outside and 0 too wide on 18 pages; Quick 3 of 4, Macro capped, 0 lost; `TacticalAir.Dogfight` 1x 35 s, 8x 44 s, 0.25-0.27 hits a shot; Full 17 of 20 won, 0 lost, 3 caps (Rusher 3, Macro 5, Turtle 3), 0 exceptions, desertion in 3 of 20.

*From the World notes (Switch-over and balance (phase 8). The new combat became the game's default, and then its…):*

Full on it **19 of 20 won, 0 lost**, 1 cap (Turtle 2), 0 exceptions, desertion in 4 of 20 (run with the tank-answer rule below still in: it almost never fired); **on the final tree** a Quick 2 of 4, Macro and Turtle capped at 720 s, 0 lost, 0 exceptions, `Guns` machine gun 100% of 85, main gun 100% of 8, `Outranged` 4 of 5 (its miss is the known gap seen: riflemen held at 32.7 m, just beyond an idle Mauler's reach, and it never answered, 448 hull lost), `RosterTrials.AIvsAIAir` 601 s, 0 errors; the squad calls the Commander acted on, now on the evaluation line: 1 fall-back in 20 games, 0 calls for air

*From the World notes (Switch-over and balance (phase 8). The new combat became the game's default, and then its…):*

: `RosterTrials.DuelsPhase6` Mauler 10-6 (the same as at 32), `Outranged` 5 of 5 (at 32 its miss was riflemen held at 32.7 m), the Full with it 17 of 20 won, 0 lost, 3 caps (Macro 3 and 5, Turtle 4), 0 exceptions, desertion in 3 of 20.

*From the World notes (Switch-over and balance (phase 8). The new combat became the game's default, and then its…):*

Checks (2026-11-20, 6000.6.9f1, 0 exceptions): Quick under the new default 3 of 4 (Turtle capped), Full after the range pass (Sentinel 34, Mauler 32, Lancer/Aegis/Ironclad 30) and the home fall-back fix 18 of 20, **0 lost**, 2 caps, 0 exceptions (2026-11-20; before them 18 of 20 with 1 lost: Rusher game 2, the AI's army never passed 640, 22 Diggers lost, plan Turtle/Tech 96%; the final review's likeliest cause: every infantry gun but the Scorcher's now outranges the towers and tanks, Trooper 35, Thumper 45, Marksman 55 against Sentinel 20, Mauler 24: **a range pass for towers and tanks is phase 8's open item**) and 1 capped, desertion in 2 of 20; `MechDuel.RunBaseFreshAll` 6.6 Maulers (target 6-7), 6.0 Troopers; player `-sfbench 420 -sfplay`, High, AC charged, one run each: new default 40.7 fps (scale 0.76, worst 5 s 36.8), `-sfclassic` 42.6 (0.79, 35.6). Before the worker rule (25 m of their Foundry) the Quick was 1 of 4 (the Rusher game lost 42 Diggers).

*From the World notes (Checks (Editor/TacticalTrials.cs, from a fresh Play at the menu, Editor, 2026-11-03, map r…):*

Player, the code before the second review's fixes, `-sfbench 420 -sfplay -sftactical`, High, AC charged: 49.9, 49.6 fps (50.4, 48.8 with a Mech in view), 1,090 and 1,388 rounds, sight 0.086 + lines 0.009 ms a frame, 0 exceptions. Switch off, Full evaluation on the first review's code (nothing since changes an off match): 18/20 won, 0 lost, 2 caps, 0 exceptions.

*From the World notes (Checks (Editor/TacticalTrials.cs, from a fresh Play at the menu, Editor, 2026-11-03, map r…):*

Player with thinking, rigged soldiers (before `Orders`' fixes, which touch no drawing), `-sfbench 420 -sfplay -sftactical`, High, AC charged: 47.9 fps, 46.0 with a Mech in view, 1,057 rounds, sight 0.107 + lines 0.059 ms a frame, worst frame 125 ms, 0 exceptions. Switch off on the final code, Quick: 4/4 won, 0 lost, 0 exceptions.

**The user's first play, 2026-11-23/24** (Unity 6000.6.10f1, Editor, `Editor/FeedbackTrials.cs`, every change A/B'd by its `TacticalRules.Legacy*` switch). `RoundLoops` at 3x: 17 of 150 homing rounds looped, 0 of 143 now (1x 0 either way). `AirRounds`: six riflemen at a circling Swift 99% struck before, 48% now, at a hovering Kestrel 100% / 99%; two Gunners at the Swift 101% / 18%; a Swift over six riflemen lives 1.5 s / 4.0 s. `SwiftStrike` (rockets): at six riflemen 0 down, 0 hull / 1 down, 93 hull; at a Mauler 139 hull in 35 s / down in 21 s. `RosterTrials.Duels`: the Swift 8-8 / 10-6, Prospector 10-6 / 8-8. `BlindFire` (three fields, after the episode fix): rounds and bombs into the smoke 0 / 21, 0 out of or through their own smoke, blind rounds 0.00 m from a spot a man was seen at and 3.1 m from where he went. `FireAt` 3 of 3 fields (35-42 rounds and 5 bombs an order, 0 after Hold or a Move, the man 6 m off answered). Smoke used less: `Grenades` smoke 14 / 12 in six fights; a tank kept on one charge lost about 70% more hull to rocket men on the same fields (`Backoff`), so it keeps two, one back per 30 s. **The Mech** (`MechDuel.RunBaseFreshAll`, bare, twelve designs, fresh map): 3.6 Maulers / 6.4 Troopers on the tree as it was; guns 0.30 and x1.3 at soldiers 3.5 / 5.8; that with the sensors 3.5 / 6.7; with the 1.3x reach too 5.3 / 10.3; with x1.15 against armour and x1.10 hull too (2026-11-24) **6.3 / 10.6** (target 6-7 / ~10). `MechSensors`: new senses see a man in smoke, one lying down and one in deep woods (old: the smoke and woods men unseen); behind a boulder 2 of 3 clean (0 rounds stopped by cover, the open man hit), the third 24 rounds stopped with the hidden man unhurt (the trial does not test the open man's line against trunks, so whose rounds is not told). **Full evaluations:** before the round 16/20; with smoke, rounds, rockets and the Mech's power (fb1) 18/20, 0 lost, 2 caps; with the sensors and reach too **20/20, 0 caps, 0 lost, 0 exceptions**, games 60-80 s shorter (Rusher 487 s, Macro 723, Turtle 753, Harasser 491 against 563, 806, 825, 553), desertion in 2 of 20; with the armour tune too (both sides' Mechs; the tree then also held another session's uncommitted deserter-verdict change) 18/20, 0 lost, 2 time caps against Macro, 0 exceptions, games 562 / 800 / 758 / 518 s: back to the fb1 Full's level; the AI audit's two traced Fulls on the same rules just after went 20/20 each, 0 caps, 0 lost, so the caps were no trend. **What the round costs in the player** (one build, guid baa0cd38, 2026-11-24 14:35-15:25; `-sffeedbackoff` sets every switch of the round to the old rules; `-sfbench 740 -sfplay -sfquality high -sfholdscale 0.75`, seed 1000, on / off / off / on, AC attached and not charging, battery 82-80%, unlocked, the player in front, 0 exceptions; as played: the old rules play a different match): **new 43.6 and 40.3 fps (42.0), old 41.9 and 43.0 (42.5)**, so -0.5 fps against 1.1-3.3 between runs of the same rules; worst 5 s 33.4 / 31.4 against 32.4 / 35.6; main thread 16.0 / 16.5 ms against 16.5 / 16.3; sight rules 0.115 / 0.114 ms a frame against 0.107 / 0.141, shots' lines 0.035 / 0.083 against 0.057 / 0.042; simulation p99 4.6 / 6.1 against 6.2 / 6.1 ms. The spread follows the match (949-1,885 rounds fired; on1 had no Mech in view, its enemy's bay fell; with a Mech in view 35.2 fps over 2,665 frames of on2's heavy fight against 42.9 and 44.5 over 8,000+ frames of the old rules'): **no cost of the round shows**. Caveats: another session compiled offline twice during on1 (about a minute each), and the load stood at 10.4 as off1 started.

### World: Wider roster

*From the World notes (Balance is measured, not guessed (Editor/RosterTrials.cs, play mode): Duels (every pair of…):*

-- final round before the last Ironclad cut: Trooper 9-6, Skimmer 2-14 (a raider, unchanged), Mauler 10-5, Thumper 6-9, Scorcher 9-7, Marksman 5-11, Lancer 8-8, Aegis 9-7, Kestrel 8-8, Swift 10-6, Prospector 5-11, Skitter 8-7, Spitter 7-8, Razorwing 9-7, Rocketeer 7-9, Ironclad 13-3 (then 560 -> 480 hp, 26 -> 24 damage), Gorehorn 8-7;

### World: Morale

*From the World notes (Rare by design. Every 2 s each side is read; it is in a crisis only when at once: its army…):*

At 55% / 400 / 2x the war gates passed only once the army was already gone (Full eval: 3006 of 4294 losing-side readings stopped at strength), so the decisive fight came before the side counted as losing: 0 desertions in 20 matches. With the war gates at 70% / 300 / 1.5x a crisis came 3 times in 20 matches, and at `BreakChance` 0.25 none broke.

*From the World notes (Rare by design. Every 2 s each side is read; it is in a crisis only when at once: its army…):*

With Mechs counted in the war and `BreakChance` 0.6: 2 of 20 (8 crises), both sides' Mechs already lost. With Mechs left out: 12 crises and 4 of 20 at 0.6; `BreakChance` 0.4: 3 of 20 (12 crises), then 2 of 20 (5 crises); wins 13-15/20 (one loss in one run, a match with no desertion on either side).

*From the World notes (Rare by design. Every 2 s each side is read; it is in a crisis only when at once: its army…):*

**Final `BreakChance` 0.25, rolls per opponent: 2 of 20 (13 crises), 13/20 wins; 2 of 20 (4 crises), 16/20; with the dying left out, 1 of 20 (7 crises), 15/20; with the rear-first pick, 1 of 20 (6 crises), 15/20; 0 losses and 0 exceptions in all four** -- 6 in 80 (about 1 in 13), each a lone soldier; before the rear-first pick 4 of 5 died 0-2 s after breaking, the one since 5 s after (its side was being routed, lost at 429 s).

*From the World notes (Checks (Editor/MoraleTrials.cs, play mode; trials hold Commander.Suspended/3x through a st…):*

; on the rear-first pick: rolls 0.43 then 0.24 against 0.35, broke at the second, verdict 8.8 s later; again 0.62/0.86/0.71/0.59/0.14, verdict 8.1 s later, put down each time

*From the World notes (Checks (Editor/MoraleTrials.cs, play mode; trials hold Commander.Suspended/3x through a st…):*

Before the Mech was left out of `SideStrength` and the verdict made patient, the punishment never followed a natural desertion (0 in 60 Full-eval matches).

### AI: Perception

*From the AI notes (A rusher who streams his units is read by the flow, not the peak (Perception.commitFlow ->…):*

Full 2026-11-19 (Commander, 900 s, battery charging, 6000.6.9f1, 1,431 s wall): **20/20 won, 0 lost, 0 exceptions; Rusher read correctly 63% (45-78% a game) against 31% in the Full of 2026-11-18 on the same five seeds, Harasser 71% (71%), Turtle 84% (85%), Macro 11% (13%)**; flow mean / peak: Rusher 395 / 873, Harasser 155 / 425, Macro 24 / 138, Turtle 0 / 0.

### AI: Wider roster

*From the AI notes (The wider roster (Commander: TechBuildingWanted, PickInfantry/PickTank/PickAircraft/PickCr…):*

Quick 2026-10-29 on this round: 4/4 won, 0 exceptions, a Mech-Hunter in 2 of 4 games, Haulers in all four; `AIvsAIAir` twice: 0 errors, Kestrels and Prospectors in the second, the programme bought by the air side in both but no Mech-Hunter fielded (open: it was ready late, or their Mech and bay were no longer known).

*From the AI notes (The wider roster (Commander: TechBuildingWanted, PickInfantry/PickTank/PickAircraft/PickCr…):*

Full 2026-10-27 on the roster: **20/20 won, 0 lost, 0 exceptions** (Rusher, Macro, Turtle, Harasser 5/5 each; before the roster 17/20), every new type but the Aegis trained (the scripted players field no aircraft, so it never needs one; `RosterTrials.AntiAir` -- AI against AI on seed 1000, team 0 given extra Kestrels from 200 to 560 s, topped up to six and attack-moved at team 1's base when idle (team 0's own Commander may take them into its squads too) -- has team 1, armour path, train 6 / 4 Aegis and 4 / 1 Marksmen in two runs; `AntiAirControl`, the same match with none given, 0 and 0; 0 errors; both put the match statics back when stopped part-way): Scorcher 240 in 20 games, Rocketeer 188 in 20, Thumper 68, Lancer 64, Hauler 61, Skitter 56, Spitter 41, Marksman 36, Kestrel 25, Prospector 23, Gorehorn 18, Razorwing 18, Ironclad 12, Swift 7; Armory research 90 levels (4.5 a game). Quick 2026-10-28 on the final roster code (review fixes and the WayBlocked clear-home branch in): 3/4 won (Macro capped at 720 s, both Mechs standing, theirs 62 kills), 0 lost, 0 exceptions, the roster trained in every game (Scorcher 13-22, Rocketeer 7-11, Lancer 1-13, Armory research 2-8); again with the capes in: 4/4, 0 lost, 0 exceptions.

### AI: Variety and raids

*From the AI notes (Variety. Commander.DrawPersonality draws a Personality from the match seed (opening, unit-…):*

-- 2026-10-28 on the roster (fa7ccb5): 12/12 won (Rusher 383-657 s, Macro 697-875, Turtle 442-711, Harasser 341-534), 0 lost, 0 exceptions, 1 desertion in 12.

*From the AI notes (Side raids (RunTactics, sideRaid): a personality with raidTaste > 0.5 (about half) builds…):*

; Full 2026-10-21: raids beside another plan in 8 of 20 matches, 52 sorties.

### AI: Budget and clocks

*From the AI notes (Order loops stop at an empty budget (macro steps 5, 5b, 5c test Budget.Can(2)): past it ev…):*

: Recruit Quick 2026-10-21 2/4 won, 0 lost, 12-27% refused, the rest the 90-APM cap working (Build's 3 a try, RunWave's re-orders).

*From the AI notes (The Commander's timers keep their overshoot (Commander.Update, 2026-11-20, the bug hunt's…):*

Full on one tree, Commander, 900 s, 0 exceptions both: before 16/20, 0 lost, 4 caps (Turtle), desertion in 4 of 20, read correctly Rusher 50% / Macro 10% / Turtle 89% / Harasser 34%, refused 2/2/2/4%; after 17/20, 0 lost, 3 caps (Macro), desertion in 3 of 20, 52/10/89/31%, refused 4/5/3/2%.

### AI: Diggers and their Mech

*From the AI notes (A Digger scouts only to find their base (RunScouts; looking for ore to expand to is SendEx…):*

Full eval on the final rule: 12/20, 0 losses, 0 exceptions (7 of 8 caps their Mech holding its bay, as before); with the Mech training gate and the Digger reserve too: 15/20, 0 losses, 0 exceptions (all 5 caps their Mech alive), command fuzz seeds 2-5 clean, and both long-match stall maps decided (958 s, 400 s; 0 Diggers lost scouting).

*From the AI notes (Their Mech's last sighting stays a danger for 30 s (MechLingers: within its 40 m reach of…):*

Five Macro games, Diggers their Mech killed per game: before [24,0,28,37,0], [18,26,1,0,3], [0,18,0,0,30] (12.3 a game), the builder rule alone [24,28,0,0,21], the rules before the review's fixes [0,0,15,1,4] and [12,0,1,16,0] (4.9).

*From the AI notes (Their Mech's last sighting stays a danger for 30 s (MechLingers: within its 40 m reach of…):*

Full gate on the final code (2026-10-27, Commander, 900 s, Editor unthrottled in the background): 17/20 won, 0 lost, 0 exceptions (Rusher 4/5, Macro 3/5, Turtle 5/5, Harasser 5/5); their Mech killed [0,0,28,4,0] Diggers in its Macro games -- 5.4 a game over the 15 Macro games on the rules. The bad games (Macro 3: 28, Rusher 5: 16) are their Mech outliving its lost bay and hunting on at 23-91% hull (68-184 kills). **On the wider roster that has shrunk**: Full 2026-10-28 on HEAD (fa7ccb5, Commander, 900 s, Editor in front, AC charged): 20/20 won, 0 lost, 0 exceptions; their Mech killed 24 AI Diggers in 20 games (Turtle 3: 11, Macro 3: 6, Harasser 4: 3, Rusher 5: 2, Turtle 1-2: 1 each, the other 14 games 0; Macro 1.2 a game against 5.4).

*From the AI notes (Their Mech's last sighting stays a danger for 30 s (MechLingers: within its 40 m reach of…):*

-- Full 12/20 (Macro 0/5), 0 lost, fired 0-1 times a game

*From the AI notes (Their Mech's last sighting stays a danger for 30 s (MechLingers: within its 40 m reach of…):*

(Full 15/20, 0 lost, on the units refactor's assembly)

### AI: Audit round

*From the AI notes (A wave that loses is beaten, not called off (bit 2): spent -- under SpentShare 0.45 of the…):*

Before: 141 waves in 48 logged games, 3 retreats, 91 called off, none beaten, massing in none.

*From the AI notes (A wave that loses is beaten, not called off (bit 2): spent -- under SpentShare 0.45 of the…):*

**Full on the defaults 2026-11-23: 14 of 79 beaten (5 spent), 7 retreats, 46 called off, massing in none**; the five Turtle seeds: 7 of 16 beaten (2 spent), 4 called off, massing began in 1 game.

*From the AI notes (Reinforcing groups are OFF (Commander.ReserveGroups, bit 1 turns them on: units trained du…):*

The like-for-like pair on the Full's five Turtle seeds: 4 of 5 won either way, 766 s on average with them and 675 s without, per seed -381 to +430 s (five games cannot tell them apart); a Full with them 16/20, without 19/20, 0 lost in both. Pooled over runs that also differ in other switches: 14 of 20 at 806 s with, 8 of 10 at 666 s without.

*From the AI notes (An idle straggler re-orders the wave every IdleReorder 2 s, not every pass (bit 4). It did…):*

With the reserve on the Turtle runs read 14, 114 and 210; the Full on the defaults 20 a game against 42 in the 48 games before the round.

*From the AI notes (The opponent model weighs evidence by the second (bit 128, EvidenceEvery 0.5 s, Recruit's…):*

Read correctly in the Full: Rusher 60%, Harasser 34%, Macro 12%, Turtle 88% (the 63 / 71 / 11 / 84 above were the old combat's; `FlowLow`/`FlowHigh` are untuned for the new one).

*From the AI notes (Gates on this round (Commander, 900 s, 6000.6.9f1, AC, Editor behind the user's work): Ful…):*

- Gates on this round (Commander, 900 s, 6000.6.9f1, AC, Editor behind the user's work): **Full on the defaults 2026-11-23: 19/20 won, 0 lost, 0 exceptions** (Rusher 5, Macro 5, Turtle 4, Harasser 5; desertion in 3 of 20; read correctly 55 / 12 / 87 / 38%), run before the last one-line fix (a scout taken from a prong leaves it under `!LegacyWaveEnd`); the Full with the reserve on 16/20, 0 lost, 0 exceptions (Rusher 5, Macro 3, Turtle 3, Harasser 5); Quick on the defaults 2/4, 0 lost, 0 exceptions (Macro and Turtle capped at 720 s, as the Quick before the round). **Not run:** `AIReview.RaidTrial`, Recruit and Veteran. On 6000.6.10f1 (2026-11-23): `BugTrials.BrokeStallTower` 6 waves and 6 retreats in 481 s (one about every 80 s: the 25 s regroup holds, no start/stop loop); `AIReview.StallTrial` ran but its staging never reached the stall case (the AI already held 2,540 against a massed target of 858), so the stall rule itself is unexercised; two traced Fulls (README › AI: Audit round). `BugTrials.ProngHome` 4 of 4; `AIReview.MassTrial` two massed waves, one called off after 15 s, no start/stop loop.

*From the AI notes (The plan doctrine is the legacy one by default; a varied one is opt-in):*

Traced Fulls on 6000.6.10f1 (Commander, 900 s, the same five seeds, Editor muted, AC charged), 2026-11-23. Legacy doctrine (13:06): **16/20 won, 0 lost, 4 caps** (Rusher 5, Macro 4, Turtle 2, Harasser 5 of 5), desertion in 2 of 20; plan time Timing Push 48.7%, Turtle/Tech 24.6%, Expand 14.1%, Feint 5.7%, Economy 4.8%, Harass 1.6%, Counter-Attack 0.6%, Trooper Rush 0; after 480 s against the turtle Feint 26%, Timing Push 21%; massing began in 1 game. Varied doctrine (17:58): **16/20 won, 0 lost, 4 caps** (Rusher 5, Macro 5, Turtle 1, Harasser 5), 0 exception lines, desertion in 4 of 20; Timing Push 28.5%, Turtle/Tech 25.1%, Feint 23.0%, Expand 12.3%, Economy 3.7%, Trooper Rush 3.0%, Counter-Attack 2.5%, Harass 1.8% (no plan at 0%); after 480 s against the turtle Feint 84%, Timing Push 2% (against the macro player 38% / 38%); massing in no game; Turtle games 889 s on average against 737 s; Diggers at 180 s against the harasser 13.8 against 16.0 (Trooper Rush holds them at 12), ore mined higher against three of the four opponents (the rusher's level). Replay of the varied doctrine over the legacy trace had predicted Timing Push 34.6%, Feint 10.9% (24% against the turtle). Quick on the varied doctrine 2/4, 0 lost (Macro and Turtle capped at 720 s, as the legacy Quicks). Since 18:30 the same day the legacy doctrine is the default again and the varied one is mask bit 1024 (`AuditTrials.EvalFullVariedDoctrine`).

*From the AI notes (It met the variety target and is left off, now because Expand owns the macro player's late game):*

The Feint cap's gate, 2026-11-23 22:56 (traced Full, varied doctrine with `FeintRun` 60 / `FeintRest` 180, Commander, 900 s, 6000.6.10f1, the tree also carrying the combat session's uncommitted fb1 rules, wall 1,693 s): **17/20 won, 0 lost, 3 caps** (Rusher 5, Macro 3 with 2 caps, Turtle 4 with 1, Harasser 5), 0 exception lines, desertion in 4 of 20, massing in no game. Base on the same rules (legacy doctrine, the combat session's Full that evening): 18/20, 0 lost, 2 caps (Turtle 3 of 5), massing in no game. Plan time Turtle/Tech 30.2%, Timing Push 28.5%, Expand 24.0%, Feint 6.3%, Economy 3.9%, Trooper Rush 3.7%, Harass 2.5%, Counter-Attack 0.8% (no plan at 0%); 21 Feint runs, the longest 63 s. After 480 s: against the turtle Feint 23%, Timing Push 52%, Expand 25% (legacy trace 26 / 21 / 53%); against the macro player Expand 64%, Turtle/Tech 22%, Feint 8%, Timing Push 4% (legacy 15 / 0 / 25 / 56%). Turtle games 413, 851, 900, 851, 790 s (base 824, 900, 900, 825, 676). The 23% is the cap binding (a ~63 s run and a ~183 s bar hold Feint to ~25%): the same trace replayed with rule legality and no cap gives Feint 63% after 480 s against the turtle (per-game mean; the replay over the uncapped 17:58 trace had predicted 19% with the cap and 62% without, against 84% recorded there, pooled). The gate written before the run asked for at most 2 caps: failed by one, so the varied doctrine stays off.

### AI: Mech AI

*From the AI notes (Two minds, one judgement (MechDoctrine.cs; the user's call): both Mechs fight the same cau…):*

Gate: Full 2026-10-21 (Commander, 900 s) 15/20 won, 0 lost (5 caps: 4 Macro, 1 Rusher), the AI's Mech alive in 20/20, its calls heard (heeded or ignored) in 20/20, 0 exceptions -- the earlier Full runs at 13-16 are the bar; 16 Quick games at Commander, Recruit and Veteran: 0 lost, Mech alive and heard in all. Quick 2026-10-07: 2/4 wins, 0 losses; the AI Mech's time stayed cautious (vs Macro: guarding 25%, defending 9%, supporting 15%, hunting 21%, withdrawing 12%, repairing 13%); BayEdgeCases case 0: bay in its base down in 101 s, 0 fighting units lost.

*From the AI notes (A hunt does not lead into their Mech's reach (MechBrain.FindPrey: prey within their Mech's…):*

Five Macro games: withdrawing ~33% to ~18% of its time on average, waves 74 to 43, wins 1/5 either way -- the

*From the AI notes (A hunt does not lead into their Mech's reach (MechBrain.FindPrey: prey within their Mech's…):*

Gate: Full 2026-10-21 13/20, 0 lost, 0 exceptions (that day 15 before the round, 14 without this rule; 13-16 is the spread).

*From the AI notes (A hunt does not lead into their Mech's reach (MechBrain.FindPrey: prey within their Mech's…):*

BayEdgeCases on the final code: cases 0-9 0 exceptions, case 0's bay down in 111 s (1 fighting unit lost), cases 6/7 as before (the Digger dies on its way, the bay is never finished); one pass lost case 1 at 263 s, which six case-1 runs did not repeat (new rule bay down in 272-292 s, 13-17 units lost round it, no loss; old 277-322 s, 11-15, no loss): the spread of one match.

*From the AI notes (Both Mechs go in beside a big attack on their Mech (MechDoctrine.commitWithArmy, true for…):*

Five Macro games: 1/5, duelling 0-7% of the time -- near

*From the AI notes (Both Mechs go in beside a big attack on their Mech (MechDoctrine.commitWithArmy, true for…):*

Gate: Full 2026-10-22 14/20, 0 lost, 0 exceptions; the evaluation line's "their Mech's time" (the player's doctrine, for the scripted side) shows it hunting 12-78% of its time.

*From the AI notes (Punishing deserters (MechBrain.Punish, rules in ../World/CLAUDE.md Morale): on its side's…):*

**In a Full eval every desertion with a Mech on the field got a verdict (4 of 4: 2 punish, 2 spare), and one punishment put its deserter down itself** (Turtle game 1); the other "go" never found a free moment before its Mech fell 289 s later. Without a Mech the deserters mostly died to the enemy within a minute.

### AI: Commander vs the enemy Mech

*From the AI notes (A Mech lands seven minutes after its bay stands (GameWorld.MechDropDelay 420 s since 2026-…):*

**Quick on this code (2026-11-04, Commander, 720 s, AC power and charged, 261 s wall): 4/4 won (447, 711, 541, 322 s), 0 lost, 0 exceptions**; their bay fell in all four, before its Mech in three; Rusher and Harasser ended before the AI's own Mech landed (its bay still up, 2 and 0 upgrades bought), Macro had both Mechs down (the AI's alive at 93% on 2 kills, 9 upgrades), Turtle the AI's alone (100%, 5 upgrades). **Full on the final code (same day, Commander, 900 s, AC power and charged, 1,549 s wall): 19/20 won, 0 lost, 0 exceptions** (Rusher 5/5 at 371-473 s, Macro 4/5 -- game 2, seed 1037, capped at 900 s with their Mech lost and their bay up, the AI's Mech alive at 77% -- Turtle 5/5, Harasser 5/5; 19/20 and 20/20 are the earlier Fulls' range); **both Mechs landed in 8 of the 20 games (the five Macro, three Turtle) and neither in the other 12, which ended before nine minutes: their bay fell before its Mech in every one of those 12**, in 19 of 20 games in all; the AI bought 7-10 upgrades in the games its Mech landed in, 0-3 in the rest; desertion in 2 of 20.

*From the AI notes (MechNeed (450-950 by its guns since the Mech's scale round, 1,750-3,150 before; x its seen…):*

In evaluations it is rare: massing began in 3 of 20 games in the Full of 2026-10-22, and none fired the stall rule. With the rule in `Plan` (since changed), five Macro games 2/5 against 1/5 without -- inside the spread; what it showed was fewer kills for their Mech (42 and 85 in two caps, 110-153 before). Full gate on the final code (2026-10-22, Commander, 900 s): 17/20 won, 0 lost, 0 exceptions (Rusher 5/5, Macro 5/5, Turtle 3/5, Harasser 4/5); massing began in 3 games and sent its massed wave in 2 (Turtle, at 565 and 835 s), none on a stalled army; Macro won 5/5 with massing in only one of them (the gain is not this rule's alone).

*From the AI notes (Home defence counted units only and ignored a bay in the base. It now counts an enemy Mech…):*

: 8 case-9 runs, team 1 won 6 (447-645 s), one 0 v 0 draw, one behind at the end (gated, the Mech roamed 118 m and took 23 Diggers; the rule decided once). Case 8's eight runs: team 1 won 4, ahead at the end in 3, behind in 1; bay down in 20-176 s.

*From the AI notes (Home defence counted units only and ignored a bay in the base. It now counts an enemy Mech…):*

Full on the scale round (2026-10-29, Commander, 900 s, Editor behind Safari at No Throttling, AC charged): **19/20 won, 0 lost, 0 exceptions** (Rusher, Turtle, Harasser 5/5, Macro 4/5: game 3 capped with the opponent at 3 structures, 0 Diggers, no army or Mech, against 3,055); Mech kills 1-34 a match (40-159 before), their Mech lost in 18/20 after 27-509 s, Macro wins at 551-636 s; no desertion in 20. Then with

*From the AI notes (Home defence counted units only and ignored a bay in the base. It now counts an enemy Mech…):*

: again **19/20, 0 lost, 0 exceptions**, the cap again Macro game 3 (seed 1148, five earlier replays of it 1 win and 4 caps under either rule: the opponent at 11 structures and 23 Diggers but an army of 50 against 875, out-mined 27k to 14k, massing from 561 s); Mech kills 0-32, their Mech lost in 15/20, 1 desertion in 20 (put down by its own Mech).

*From the AI notes (Home defence counted units only and ignored a bay in the base. It now counts an enemy Mech…):*

On HEAD (fa7ccb5 game files, this session): case 1 bay down at 293 / 196 s, 12 / 17 units lost; case 3 at 260 / 256 s, 5 / 5 lost, the AI's army and Diggers intact. On the cut: case 1's bay stood all match in 5 of 5 runs and the AI side collapsed (armies 0 at the end in 4, lost outright at 251 and 282 s), 13-49 units and 2-18 Diggers lost round it; case 3's stood in 3 of 3, 14-28 lost. The nerfed Mech rules with HEAD's AI values: case 3's bay down at 300-311 s, case 1 still bad (one loss at 282 s) -- so

*From the AI notes (Home defence counted units only and ignored a bay in the base. It now counts an enemy Mech…):*

On the code before the cut (3d766c3, one `RunAll`) the bay's side was wiped in case 2 and the other in cases 0, 4, 5 and 9; what the cut changed is cases 1 and 3 (bay down at 196-311 s and all 17 Diggers kept before).

*From the AI notes (Home defence counted units only and ignored a bay in the base. It now counts an enemy Mech…):*

Full with the first cut of the gate and the upgrades repriced: **20/20, 0 lost, 0 exceptions** (Macro 5/5, game 3 won at 562 s), Mech kills 3-37, 6-10 upgrades bought a game, 1 desertion in 20; on the final rule again **20/20, 0 lost, 0 exceptions** (Macro 5/5 at 637-730 s), Mech kills 2-36, 2 desertions in 20.

*From the AI notes (Home defence counted units only and ignored a bay in the base. It now counts an enemy Mech…):*

: `MechSpeech.Check` 406 lines, 0 problems; `MoraleTrials.PunishNatural` verdict 4.5 s after the break, unforced; Quick 4/4, 0 exceptions, Mech kills 4-11.

### View: Capes

*From the View notes (InfantryCape (made and ticked by UnitView, soldiers only: Eligible -- infantry class, not…):*

(two runs: Trooper 0 / 22, Thumper 6 / 6, Marksman 8 / 7, Scorcher and Rocketeer 0 / 0; more where the Editor's frames ran long)

*From the View notes (InfantryCape (made and ticked by UnitView, soldiers only: Eligible -- infantry class, not…):*

Cost: player benchmark on seed 1000 with/without/with 51.1 / 51.2 / 50.3 fps (3-5 capes in that match); the full budget of 24 cloths is unmeasured on the player (a `-sfallcapes`/`-sfnocapes` series, run only while the user is away, had every run cut short) -- in the Editor (vsync off) 24 against none differed by less than its own few-ms spread, run to run (trimmed cape: 12.8 / 13.3, 13.4 / 13.6 ms cloth / rest).

### View: Ironclad walk

*From the View notes (Check IroncladTrials.Walk/WalkReport (play mode, ~2.5 min; wait ... "wrecks done"): 1x and…):*

**2026-11-23, round 4 (the third review's D and F), map 1000, 6000.6.9f1, against round 3 on the same fields with the same trial** (straight / about turn / stop-start / 8-degree slope / 25-degree steep): **1x** drift 0.0 cm, landing and reach misses 0, one-sample swings 0, both up 0, re-lifts 0, standing 0 steps, in both; landing jumps 0.10/0.15/0.15/0.70/0.58 m against 0.18/0.18/0.23/1.22/0.81, one over the bound on the steep slope in both; speed jumps 0/0/0/1/0 against 0/0/0/0/1 (5 against 3 on hitch frames); the old swing pops 11 against 21 (round 2 on these fields had 0 swing pops and 3-6 landing slams a walk at 1.18-1.55 m); crouch 0.33 at most on flat. **8x**: landing misses 0/0 against 76.7 cm (8 degrees) and 19.5 (25), drift 0.0 against 19.5 cm, both feet up 0 against 6, standing 0 steps; re-lifts 49 against 43 and speed jumps 90 against 74 are a step of 2-3 frames of 150-200 ms. Earlier, on other fields: round 2's 8x standing took 17 steps (the twist rule on the hull's wandering yaw), and every version up to round 3 missed a landing on a 31-degree slope at 8x by 1.1-1.8 m (round 4 has not walked that field). 12 walking 0.157 ms a frame, ~13 us an Ironclad (Editor, Mono); both wrecks made, thrown and settled, the unseen one hidden; 0 exceptions. **Round 5 (`LongFrame` 0.05 s, the fourth review's notes) on 6000.6.10f1, same fields, round 4 run again first on the same engine:** 1x both rounds 0 everywhere (drift, misses, landing jumps over the bound, speed jumps, one-sample swings, both up, re-lifts, standing steps), landing jumps 0.12-0.19 m; 8x drift and landing misses 0, both up 1 each, landing pops 8 against 6, re-lifts 12 against 8; 10.6 against 10.0 us an Ironclad.

*From the View notes (Check IroncladTrials.Walk/WalkReport (play mode, ~2.5 min; wait ... "wrecks done"): 1x and…):*

**Player fps** (one build, `-sfnoironcladik` off/on/on/off, `-sfbench 420 -sfplay -sfholdscale 0.75`, seed 1000, High, AC charging 82-97%, unlocked, in front, 2026-11-23, on round 2's walker): 42.9 / 42.2 / 41.6 / 41.4 fps, worst 5 s 37.2 / 36.0 / 35.0 / 33.0, render thread 2.9-3.1 ms: no collateral cost; no Ironclad was trained in those four matches (one in 2 of 11 earlier reports), so the walk's own cost is the Editor's line above.

**Round 5 on the 31-degree field, and the walk in the player (2026-11-23, 6000.6.10f1, map 1000; the tree then also carried peers' uncommitted AI and tooling changes):** the trial with the steeper field appended walks every earlier field as before (1x 0 on every target, landing jumps 0.11-0.37 m; 8x drift and misses 0). 31 degrees, 12 m down and up: **1x** 26 steps, drift 0.0 cm, landing miss 0, landing jump up to 0.16 m (0 over the bound), speed jumps 0, swing pops 17 (foot step up to 0.39 m a sample), re-lifts 0, both up 0, crouch 0.71 m; **8x** drift 0.0, miss 0, both up 0, re-lift 1, speed jumps 0 (+26 on frames over 50 ms), landing jump up to 1.65 m (3 over the bound, which is a 1x target only), crouch 0.75 m. 12 walking 0.106 ms a frame, 8.8 us an Ironclad (Editor). **Player** (`-sfbench 150 -sfplay -sfholdscale 0.75 -sfironclads N -sflook 91.2,99.8`, seed 1000, High, the Commanders silenced, one build, AC 100% charged, unlocked, the player in front at every 10 s sample, 0 exceptions, 17:33-17:51): **12 walking, on / off / off / on: 51.0 / 49.8 / 49.4 / 49.1 fps** (worst 5 s 49.0 / 44.8 / 47.0 / 45.0, main thread 15.0 / 15.2 / 15.2 / 15.5 ms, render thread 3.1-3.3): nothing measurable; **40, off / on: 47.7 / 46.8** (20.95 against 21.37 ms a frame, main thread 15.8 / 15.7; one pair, and the 12's series drifted as far, 51.0 to 49.1, as the Mac warmed); timed (`-sfironcladcost`, 60 s, 12 walking): 0.163 ms a frame, **13.6 us an Ironclad in the player** (38,772 poses) against 8.8-10.6 in the Editor. The AI fields few (12 Ironclads in the 20 games of the 2026-10-27 Full), so 12 at once is already more than a match shows. Reports `Builds/bench_ab/ironclad6_*`.

### View: Infantry guns

*From the View notes (Budget RosterTrials.ShotBudget (10 Scorchers, 10 Rocketeers, 6 Thumpers firing on the came…):*

Player benchmark on seed 1000, one run, not paired, battery charging: 50.9 fps, 50.3 with a Mech in view (51.1 / 50.3 on the last pairs; `../Game/CLAUDE.md`); that match never stages 26 guns on the camera.

### View: Unit surfaces

*From the View notes (Flash, damage, the construction line, burning and a Digger's glow go on materials of the u…):*

: `-sfbench 420 -sfplay`, seed 1000, *High*, 6000.6.9f1, one build, battery charging (67-99%), player in front, 2026-11-19, old-new-new-old (old = `-sfblocks -sfnograze`): **with the scale held at 0.75 (`-sfholdscale`), old 44.1, 44.5 fps, new 44.2, 43.7**; render thread 3.0, 3.0 against 3.0, 3.1 ms, main thread less the wait to present 9.9, 9.9 against 10.0, 10.1, frames over 40 ms 32, 30 against 27, 27, worst 5 s 37.0, 38.6 against 36.4, 35.2, 0 exceptions. With the controller on the two settled apart and say less: old 40.5, 40.7 fps at scale 0.83, 0.80, new 41.7, 42.0 at 0.78, 0.77 (the new pair is the preset's usual 42 at 0.77-0.78). (The player runs were on the build before the second review's fixes, which change no drawing.)

### Game: Old-aim baselines

*From the Game notes (The numbers in this section are at High's old aim, 0.28/0.10, and so are fps records in ot…):*

*High* (150 m shadows, full grass, floor 0.55, `missHigh` 0.28): **49.3 fps, 47.3 with a Mech in view**, render scale 0.62 (two runs within 0.2); the wider roster on 6000.6.7f1 (2026-10-28, AC charged): 50.0 / 49.6, scale 0.67, against the 21 October build 49.5 / 48.7 (one pair, roster first); seed 1074 (540 s, old-new-new-old): 46.5 and 47.9 against 45.0 and 45.1 --

*From the Game notes (The numbers in this section are at High's old aim, 0.28/0.10, and so are fps records in ot…):*

; capes on/off/on on seed 1000: 51.1 / 51.2 / 50.3 (3-5 capes at most in that match); with the Scorcher's every-frame jet, the Thumper's Mech-sized mortar and the Rocketeer's trail (2026-11-03, one run, not paired, battery charging, 13 Scorchers, 10 Rocketeers, 4 Thumpers trained): 50.9, 50.3 with a Mech in view, scale 0.64; with a 0.60 floor before Mechs were fitted out at the drop: 49.8 / 50.1.

*From the Game notes (The numbers in this section are at High's old aim, 0.28/0.10, and so are fps records in ot…):*

With a Mech in view: 160 m shadows 45.3, a 0.70 floor 42.6; 160 m/1.0 grass 43.3, 110/1.0 46.1, 110/0.7 49.2 (all at the 0.60 floor).

### Game: Rigged soldiers

*From the Game notes (Rigged soldiers (SoldierRigProbe, nothing in a match reads it): -sfbench 120 -sfplay -sfri…):*

Seed 1000, *High*, AC 100% charged, 120 s, quiet map (peak 1 projectile, no Mech, no fight), 2026-11-03, alternating none / rigged / rigid twice: none 50.6, 50.4 (render scale 0.70); 200 rigged 48.7, 49.4 (0.56, **the 0.55 floor reached**, so further cost comes straight off the frame rate: 1.7-2.0 ms of margin to 45); 200 rigid 46.9, 47.4 (0.56, 0.60); 100 rigged, one run, 53.1 (0.60).

*From the Game notes (Rigged soldiers (SoldierRigProbe, nothing in a match reads it): -sfbench 120 -sfplay -sfri…):*

Frame timing, averages: render thread none 2.9, rigged 5.45, rigid 7.65 ms (9 draws a soldier a pass against about 20); main thread less the wait to present none 10.8, rigged 12.5, rigid 12.3 ms; frames over 40 ms none 6-8, rigged 21-27, rigid 14-18.

*From the Game notes (Rigged soldiers (SoldierRigProbe, nothing in a match reads it): -sfbench 120 -sfplay -sfri…):*

**In a fight the target FAILED** (`-sfbench 420 -sfplay -sfrigfollow`, seed 1000, *High*, AC charged, alternating, 2026-11-03): none 51.0, 49.7 fps (50.8, 47.6 with a Mech in view; scale 0.65-0.66); 200 rigged 42.2, 43.6 (37.4, 39.8; scale 0.56-0.57, at the floor); main thread less the wait to present +3 ms, render thread +3 ms, frames over 40 ms 41-46 against 85-222. **The split, same fight, one run each, not alternated:** 200 rigid 44.7 fps (41.0 with a Mech in view); 200 rigged frozen with Animators off (`-sfrigfreeze`) 44.6 (39.8); rigged animated, a third run, 42.8 (37.0). Average of main thread less the wait, and render thread: none 12.05 and 3.9 ms; rigid 13.0 and 7.3; frozen 13.7 and 6.7; animated 15.0 and 7.0.

*From the Game notes (Rigged soldiers (SoldierRigProbe, nothing in a match reads it): -sfbench 120 -sfplay -sfri…):*

**With one material a soldier (`Tools/blender/CLAUDE.md`, one draw a pass for nine): 47.3, 47.0 fps (43.4, 43.0 with a Mech in view, scale 0.58) against none 49.8 (48.6, 0.64), AC charged, alternating; main thread less the wait 12.9 against 11.9, render thread 3.8-4.2 against 3.8 (7.0 before). Target met.** The picture: a pixel diff of the frozen pose sheet at an 8x box filter, old against merged, soldiers mean 1.43 and 2.62, two old runs against each other 1.22, merged 2.46; `SFMaterialLibrary.CheckPalette` compares both teams' palettes with the slot materials, 0 of 15 slots off.

### Game: One material a model

*From the Game notes (One material a model, for everything SF_Unit draws (Tools/blender/CLAUDE.md): 896 draws a…):*

`-sfbench 420 -sfplay`, seed 1000, *High*, built with one DLL (old and merged `Assembly-CSharp.dll` the same), AC 100% charged, alternating, player in front, 2026-11-04: old 50.1, 49.6 fps (48.8, 48.7 with a Mech in view, scale 0.63, 0.62 and 0.56 with a Mech); merged 51.2, 50.8 (51.6, 51.1; 0.63, 0.64 and 0.57); an earlier merged run 50.4 (51.0). Render thread 3.9, 4.0 against 2.8, 2.8 ms; main thread less the wait to present 12.2, 12.8 against 12.3, 12.1; frames over 40 ms 48, 60 against 62, 33.

### Game: Graphics rounds 2026-11-19 and 20

*From the Game notes (Birds, the edge-on leaf-card fade and unit surfaces on own materials (2026-11-19, the engi…):*

`-sfbench 420 -sfplay`, seed 1000, *High*, 6000.6.9f1, one build, battery charging (67-99%), player in front, 2026-11-19, old-new-new-old (old = `-sfblocks -sfnograze`): **with the scale held at 0.75 (`-sfholdscale`), old 44.1, 44.5 fps, new 44.2, 43.7**; render thread 3.0, 3.0 against 3.0, 3.1 ms, main thread less the wait to present 9.9, 9.9 against 10.0, 10.1, frames over 40 ms 32, 30 against 27, 27, worst 5 s 37.0, 38.6 against 36.4, 35.2, 0 exceptions. With the controller on the two settled apart and say less: old 40.5, 40.7 fps at scale 0.83, 0.80, new 41.7, 42.0 at 0.78, 0.77 (the new pair is the preset's usual 42 at 0.77-0.78).

*From the Game notes (Meadow turf, grass from one GPU buffer, wildflowers, leaf lighting, building fire (2026-11…):*

`-sfbench 420 -sfplay`, seed 1000, *High*, held 0.75, AC 100% charged, unlocked, player in front; old = the 19 November build (`Builds/veg/StarForge_before.app`, classic combat), new with `-sfclassic` (deleted with the old combat on 2026-11-20: a new build no longer compares with that one on equal rules). First build, old-new-new-old: old 45.4, 45.4 fps, new 44.7, 44.3. **After the turf's patches were taken from the terrain's variation texture instead of a noise per terrain pixel** (order new, old, new-combat, new): new 44.6, 45.0, old 44.9; render thread 2.7, 2.8 against 2.9 ms; worst 5 s 36.2, 41.4 against 38.8.

*From the Game notes (Meadow turf, grass from one GPU buffer, wildflowers, leaf lighting, building fire (2026-11…):*

The new combat on the same build, one run: 42.5 fps (worst 5 s 35.8, worst 30 s 37.3), about 2.3 under classic.

*From the Game notes (Meadow turf, grass from one GPU buffer, wildflowers, leaf lighting, building fire (2026-11…):*

**Levers tried with the camera held on the densest wood** (`-sfvegprobe [zoom]`, 120 s, held 0.75, one run each: read under ~1 fps as noise, the same setting gave 46.7, 47.3 and 47.5): half the grass (`-sfdensity 0.5`) 46.8; Unity's instancing for the grass (`-sfgrassold`) 46.3; the grass's sun shadow per pixel (`-sftune _SF_GrassPixelShadow=1`) 45.8; the turf off (`-sftune _SF_TuneA=-1`) 48.6 with the noise, 46.4 against 47.5 without it; a one-tap leaf shadow 46.7 and no opaque-colour copy at all 46.9 (both removed again: nothing to gain; a gate that skipped the copy with no water in view also never closed, its frustum test saw the water under the hills).

*From the Game notes (Meadow turf, grass from one GPU buffer, wildflowers, leaf lighting, building fire (2026-11…):*

**The turf's and the ash's detail textures (2026-11-20, a later build: combat's deletion and the menus in it, new combat only), on and off on one build** (`Builds/veg/StarForge_turf.app`, off = `-sftune _SF_TuneC=-1,0,-1,0 -sftune _SF_TuneD=-1`, order on, off, off, on, same conditions): on 39.9, 40.9 fps, off 40.9, 39.9 (worst 5 s 28.6, 33.6 against 32.4, 28.4; render thread 2.5-2.7 ms): nothing measurable, and

*From the Game notes (Meadow turf, grass from one GPU buffer, wildflowers, leaf lighting, building fire (2026-11…):*

Bursts at the wood (`-sfvegprobe 40`, held 0.77): on 1.958, 1.625% vibrating, off 1.650, 1.665%, half strength 1.480%. That build's 40.4 against the morning's 42.5 for the new combat (one run, an earlier build) is the day's other code, not isolated.

*From the Game notes (Battle marks, the lake's surface, the hulls' damage mask (2026-11-20, the graphics round):…):*

`-sfbench 420 -sfplay`, seed 1000, *High*, held 0.75, AC 100% charged, unlocked, player in front, old-new-new-old; old = `Builds/veg/StarForge_turf.app` (the line above's build), new = the 15:18 build, the same with the tread marks' lane rule and per-pixel count, the repacked `scorch.jpg`, SF_Unit's damage mask and SF_Water's blurred ripple and gated caustics: **old 42.8, 41.5 fps, new 41.6, 41.7** (render thread 2.6, 2.8 against 2.7, 2.7 ms; worst 5 s 32.6, 35.0 against 33.2, 34.6; frames over 41.7 ms 43, 26 against 19, 17; 0 exceptions): 0.5 fps apart, inside the old pair's own 1.3, so nothing measurable either way.

*From the Game notes (Battle marks, the lake's surface, the hulls' damage mask (2026-11-20, the graphics round):…):*

: vibrating old 14.4, 12.7, 14.6, 14.7%, new 14.0, 14.7, 14.4, 14.9%; flashes on still ground old 258, 280, 423, 491, new 678, 400, 272, 483.

### Shaders: Leaf-card fade and meadow

*From the Shaders notes (A leaf card seen edge-on fades out (SF_Tree CardFacing, material vector _CardGraze 0.10..0…):*

: scale held at 0.75, 44.2, 43.7 fps with it against 44.1, 44.5 without (`../Scripts/View/CLAUDE.md`, Unit surfaces).

*From the Shaders notes (The meadow's ground and its grass agree through SF_Meadow.hlsl (included by SF_Terrain's f…):*

: with one the build read 44.7, 44.3 fps against the previous build's 45.4, 45.4 over 420 s at a held 0.75; from the texture 44.6, 45.0 against 44.9; `../Scripts/Game/CLAUDE.md`

### blender: Rigged soldier

*From the blender notes (Keep a hand on the rifle under 0.95 of the arm's length, and ease the reach, never cut it…):*

: 2026-11-03 misses 0.5-1.7 cm, reach 0.94 at most on the rifle (Reload's belt trip 0.97, Crawl's free hand 0.99), every loop's seam 0.000, Fire's elbow step 0.018 m; 2026-11-04 with the bind fixed and the side-arm clips,

## Scope, honestly

- Single player against the AI (or AI vs AI). No multiplayer, campaign or saves.
- Soldiers, creatures and wildlife are skinned and play crossfaded clips, and the
  Mech and the Ironclad walk by IK, but a soldier plays one clip at a time: one
  layer, no blend trees, no upper body aiming over the legs. Machines move only by
  their rigid parts.
- The AI's learning is per machine and per player profile file; there is no
  trained neural network — the opponent model is Bayesian inference and the
  strategy layer a contextual bandit, as in the original.
