# StarForge — Unity edition

A real-time strategy game against an adaptive opponent that scouts, reads your
play style, changes its plan to counter it — and remembers you between matches.
Each side may also raise one **Mech Bay**, which calls down a **Mech** from orbit: a
war machine worth twenty-odd tanks, assembled from a random draw of parts, with a mind of
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
songbirds feed on the open ground and eagles circle overhead, all fleeing the
fighting and the fires, and the music moves with the match. Played as a match
actually runs -- vsync on, 60 Hz -- it holds ~50 fps on the Neo's *High* preset -- the full
shadow distance and ground cover it had before the Mechs -- and about as much with Mechs
fighting on screen (45 is the target).

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
| **Build** | Digger selected: `B` Bunkhouse · `G` Garrison · `V` Workshop · `N` Sentinel · `F` Foundry · `Y` Mech Bay |
| **Train** | Foundry `U` Digger · Garrison `T` Trooper, `K` Skimmer · Workshop `M` · `X` cancel |
| **Mech Bay** | The bay selected on its own: `V` Servo Actuators · `B` Ablative Armour · `N` Weapon Overcharge · `M` Extra Hardpoint · `K` Targeting Uplink · `X` cancel · click the Mech strip (top left) to find the Mech |
| **Other** | `P` pause (no orders go in until you resume) · `,` `.` game speed, kept through a pause · `Esc` unwinds placement → selection → menu |

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
| **Mech Bay** *(new)* | 100 | 1800 | Calls down the side's Mech; gun tower, self-repair, repairs the Mech, sells its upgrades. One a match, never rebuilt ([Mechs](#mechs)) |

**Terrain** *(new)*: units wade into shallow water (up to 1 m deep) at about half
speed, Skimmers skim over it at full speed, and deeper water stops everyone.
Boulders block everything except Maulers, which crush them and open the ground
for all.

**Veterancy** *(new)*: units rank up at 2, 5 and 10 kills (+15% damage and +10%
health per rank) and slowly repair once out of combat.

**Targets**: a unit sent after something (right-click, or attack-move onto it) stays on
it. One that picked its own target -- idle, holding, on attack-move, or a Sentinel or a
Mech Bay's gun -- switches to an enemy within reach when the one it picked is out of
reach, and lets it go once it leaves its sight. (They used to keep it until it died: a
tower that caught a scout at the edge of its sight held on to it and stood silent,
whatever came up to its walls.)

**Diggers without a Foundry** keep their load and wait until one stands again, then
bring it in. (They used to mine on and drop each load for the next, stripping the ore
fields for nothing.)

**Morale** *(new)*: a side that is losing badly may see a few of its soldiers break and
run. It is rare: nothing breaks in the first five minutes, and a side breaks at most once a
match. It takes all of these at once: the side's army and Mech worth under 55% of the
enemy's, a minute in which it lost at least twice what it dealt, and a fight going badly
right now -- three or more of its soldiers under fire together, outgunned more than 1.6 to
one round them, two or more of them dead there in the last 20 seconds. Even then each such
crisis gets a single roll of the dice (15-35%, worse the more lopsided the war). Those who
run are about one in seven of the soldiers in that fight (one to four): Troopers before
Skimmers before Mauler crews, the wounded first, rank-1 veterans seldom, rank-2 and rank-3
veterans and Diggers never. They make for the corner of the walkable ground behind their own
base -- 40-70 m from its middle and clear of its buildings, near it but not in it -- and stay
there: they take no orders, no longer count against supply, fire only when an enemy attacks
their camp, and never go more than 14 m from it. You are told: a warning ("2 Troopers are
deserting -- running for the south-west corner") with the alert sound, pale pulses on the
minimap where they broke and where they ran, a dashed pale ring round the camp, and a pale
health bar over each deserter (its status reads *Deserted · takes no orders*). They drop out
of group selections and control groups; click one to look at it. An enemy desertion is
announced only if you can see the fight.

**The Mech and deserters**: a few seconds after soldiers of its side desert, the Mech gives
its verdict -- by its pilot's temper (a cold pilot more often), pride, and how the war is
going for it (the worse, the harsher) -- once it is whole enough (60% hull), out of a fight
and the base is not under attack. If it goes, it says so on the comms, walks to the camp,
speaks to them when it arrives, and its guns -- only its guns, never anyone loyal -- treat
them as enemies until they are dead; then it tells its side it is done. It gives up if the
base is attacked, the enemy Mech comes within 60 m, it has to go for repairs, or after two
and a half minutes. The deserters do not fire on it. If it lets them be, it may say that too.

**The match** is lost by the side with no structure and no Digger left; if both go in
the same moment it is a draw.

All numbers live in `Assets/StarForge/Data/Units/*.asset` and can be tuned in the
Inspector; rebuilding prefabs keeps your values.

## Mechs

Each side may raise one **Mech Bay** (Digger, `Y`; 100 ore, no prerequisites). Two
minutes after it stands, a **Mech** comes down from orbit beside it on its retro-rockets:
a seven-to-nine-metre war machine worth a whole army. Each side gets **one Mech and one
Mech Bay a match**: a bay that falls is never rebuilt, and a Mech that falls is never
replaced. Destroy the enemy's bay before its Mech lands and it never comes. A bay
called off before it stands does not count, and nor does a site shot down before any
Digger started work on it: the bay may be placed again (the ore of a site shot down is
gone). A structure's site appears as soon as it is ordered, so a bay ordered into the
enemy's base from afar is usually shot down before its Digger gets there.

![A blue tracked Mech opens fire up a ravine at an orange four-legged Mech standing over a destroyed Sentinel, the wood between them burning](docs/mech_fight.jpg)

**The Mech is an ally, not a unit.** Its pilot is a knight of one of the orders sworn to
whichever side raises a bay; it fights for your cause by its own judgement and takes no
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
kept rare -- about one every two minutes, never on top of a call -- it keeps your spirits
up, set apart in a quieter, quoted style: the voice of an old machine that has seen more
wars than you have, about four hundred lines, chosen for the
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
| **Frame** | *Vanguard* (light, 3 hardpoints) · *Warden* (line, 4) · *Colossus* (siege, 5) |
| **Weapons** | Autocannon (all-round bursts) · Rotary Cannon (hoses infantry) · Missile Rack (homing salvos onto a crowd) · Siege Mortar (longest reach, useless up close) · Laser Lance (burns through armour) · Inferno Projector (an arm flamer: sets infantry and the field alight) · Railgun (a slug through everything in its line) · Flame Tower (a flame turret on its own ring, on a shoulder or the back: it turns on its own and burns what comes close, all the way round) |
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
| Servo Actuators `V` | 2 (150, 250) | +15% speed and turning a level |
| Ablative Armour `B` | 3 (175, 275, 375) | +5% hull and 2% less damage taken a level (plates appear on the Mech) |
| Weapon Overcharge `N` | 3 (175, 275, 375) | +5% damage a level |
| Extra Hardpoint `M` | 1 (350) | Mounts one more weapon on an empty or auxiliary mount (the tooltip says which) |
| Targeting Uplink `K` | 1 (200) | +12% range, +10 m sight (a dish appears on its back) |

**Where you raise it** is up to you -- anywhere a Digger can build, the enemy's base included --
and both AIs treat a bay in a strange place as the gamble it is. The opponent goes for a bay (or
a Sentinel) going up beside its base with whatever army it has, and for a finished one once its
army can take the gun on, rather than feeding it a few rifles at a time; meanwhile it moves its
Diggers to ore the gun cannot reach, and avoids expanding under it. Your Mech is mended only at a
bay on your half of the map, or at one where nothing out-guns it; if it keeps losing hull in a
forward bay's gantry it says so and stays away for a while, and hurt with no bay to go to, it
falls back to your lines. It guards your base, not a bay far forward.

`BayEdgeCases` (Editor/MechTrials.cs) checks eight cases on the default map, AI against AI. A
Digger walked into the enemy base at a minute and a half was shot as it began work, and its site
fell within four seconds; 45 m outside, the Digger lasted 15-18 s and the bay was never finished
(without the opponent's answer to a site it took 19-39 s, and one bay in three was finished and
cost them 4 Diggers and 13 soldiers). A bay put down whole 12 m inside their base at 30 s --
sooner than a Digger could raise one -- fell 109-124 s later in six of the eight runs made while
this was worked on, all but the last before its Mech came down (that one landed four seconds
before the bay fell); in the other two, on earlier versions, the gun shot their economy away
first and the Mech that came down there finished them. On a lake shore 66 m from their base the Mech lived through all five runs (it had died in
three of five while it sat in the gantry being shot from 45 m). In a corner, a wood and far out
on a flank the bay stood and the Mech lived. No run raised an exception.

**Your army counts against a Mech.** Three rules make sure what an army does to one sticks:
- **Battle damage tells.** Below 60% of its hull a Mech's guns reload slower, up to 45%
  longer at 15% (it fires about a third slower). Select it and its card says so -- yours or
  theirs ("Battle-damaged: firing 22% slower"). A Mech at a fifth of its hull used to fight as
  hard as a fresh one, so nothing an army did counted until the last shot.
- **The bay mends slowly.** A Mech back at 38% needs about a minute in the gantry to be whole
  (22 hull a second, 6 while it is being shot at; it was 50 and 15, and what an army had cost
  it was gone in twenty seconds).
- **Upgrades are steps, not leaps.** Ablative Armour and Weapon Overcharge give 5% a level and
  the Extra Hardpoint mounts one more gun, not two. With every upgrade bought -- which the
  opponent does by mid-match -- a Mech used to take 2.2-2.7 times the army a bare one did.

Measured with the duel below on six designs, each fight on a freshly loaded map (fought one
after another on one site, the burnt grass, craters and felled trees of the earlier fights
skewed the later ones), against the old rules in the same session: a fully upgraded Mech now
takes **28 Maulers or 56 Troopers** on average to break (21-36 / 39-70 by design), where it took
**39 and 82** (23-48 / 54-109); a bare one 15.5 Maulers (was 17.5), and 30-36 Troopers either
way (a crowd of rifles is the noisiest measure there is: a design's number moves by a fifth
from one run to the next).

**Balance.** A bare Mech is meant to be worth roughly 20-25 Maulers or 30-40 Troopers in a
straight fight, and that is measured, not guessed: `MechTrials`' duel (Editor/MechTrials.cs)
stands a Mech with no upgrades on a flat, open corridor of the default map and sends N
Maulers or N Troopers at it, fought to the end, bisecting for the smallest N that kills it.
Over twelve drawn designs the break-even is **21 Maulers** on average (median 22; 11-28 by
design) and **36 Troopers** (median 37.5; 23-48). The Flame Tower's designs took 29-45
Troopers. Adding it reshuffled what the generator draws, and the first survey after it found
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
and the numbers came down a final notch after. Upgraded, it grows past all this: two
levels of everything (about 2,250 ore) roughly doubles the force it takes -- from 17 to 34
Maulers and from 27 to 48 Troopers on six designs -- which is about what that ore would
have bought in tanks. The first cut of the upgrades (+15% a level, a cheaper second
hardpoint) more than doubled it: a fully upgraded Mech was worth forty tanks. The duel is the
open field; in play a Mech is worth more, most of all at home. With its own AI kiting what
it outranges, the bay's tower beside it and the gantry mending it between waves, a Mech
holding its base in the AI evaluation took 65-168 kills before the match ran out, and in seven of
the nine matches that ran to the cap the defending Mech still stood. The gantry was
slowed for that (a Mech back at 38% is whole in about half a minute, not under twenty
seconds), and the opponent AI learned not to feed one: bring enough, and bring your own.

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
  go in, and sometimes split to hit two places at once. A plan that is not paying
  off is dropped, and a way in that you beat is not tried again soon.
- **It has a Mech too.** It raises a Mech Bay early, buys its Mech upgrades from
  spare ore -- guns and hardpoints when it means to attack, armour when it means to
  hold -- and goes for your Mech Bay while your Mech has not yet landed. Its Mech is
  not under its command any more than yours is under yours (it fights as carefully as
  yours, with a temper of its own): it listens to the calls its Mech sends it,
  silently, a kind and a place, and sometimes acts on them -- meeting a raid its Mech saw coming,
  striking when its Mech says the moment has come -- and sometimes does not. Around
  your Mech it keeps its army together and puts everything it has on it, and it does
  not feed it: each attack your Mech throws back makes it wait for a bigger army
  before the next. It builds against your Mech once it has seen its guns -- tanks
  against one made to burn infantry, a swarm of rifles against one made to kill armour
  -- strikes your base when your Mech is out in the field far from home, going for
  your Mech Bay then (it is what would mend your Mech), and gathers its attacks on its
  own Mech so that they go in together. A Mech Bay or a tower raised beside its base it
  treats as an attack: it goes for one still going up with whatever it has, clears a
  finished one once it has the army to, and keeps its Diggers off ore its gun covers.
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

A full run on the MacBook Neo with the Mechs (Commander, memory off, five matches per
opponent on seeds 1000, 1037, 1074, 1111 and 1148, on the default map; the cap is now 900 s,
since a Mech holding its base makes matches longer and 600 s left most of them undecided):

| Opponent | Result | Mechs | Read correctly | First correct read | Read as | APM avg / peak |
|---|---|---|---|---|---|---|
| Rusher | **4 won** (avg 576 s), 1 time cap | theirs fell in 2 | 25% | 125 s | harassing 34%, turtling 29%, rushing 23%, macro 12% | 37 / 240 |
| Macro | **2 won** (avg 583 s), 3 time cap | theirs fell in 1 | 12% | 30 s | expanding 48%, turtling 39%, macro 12% | 36 / 240 |
| Turtle | **4 won** (avg 687 s), 1 time cap | theirs fell in 5 | 72% | 85 s | turtling 70%, teching 17%, macro 12% | 33 / 240 |
| Harasser | **4 won** (avg 553 s), 1 time cap | theirs fell in 3 | 42% | 78 s | harassing 41%, turtling 24%, macro 16%, teching 14% | 35 / 240 |

**14 of 20 won, none lost** (before the Mechs, 12 of 20 at a 600 s cap); the AI's own Mech
survived every match, and no match raised an exception. Of the six that ran to the cap, five
ended with the opponent's Mech still standing at its bay, 133-152 kills to its name, and the
sixth with two of the opponent's structures left and no Mech: a Mech defending its home, with
the bay's tower beside it and the gantry mending it between waves, is the hardest thing on the
map to crack, and the scripted opponents turtle behind theirs. Of its Mech's calls to attack,
defend or pull back the AI acted on 27 and ignored 22; it struck while the enemy Mech was away
from home 25 times (up to four a match), and gathered its waves on its own Mech 103 times. Both
sides' Mechs were fitted out at the drop against what the other fielded.

Runs vary: the same twenty seeds gave 13, 15, 12, 15 and 11 wins in the round that added
the Mechs, 15, 10 and 12 in the round that added the loadouts picked at the drop, the
Flame Tower and the anti-Mech tactics below, and 14 (the table above) once its enemy memory
counted a clump of units as the units in it and it expanded when its ore ran low (no losses
in any), because a match's frame timing is not the same twice. So a single change is judged over a whole run, not a match, and a
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
is now read as a turtle a third of the time, and later
(169 s to a first correct read): from outside, its Mech holding its base looks like a
turtle, and once beaten back early its waves look like raids. The APM cap refused 1-7%
of intended actions.

## How it is built

| Area | Approach |
|---|---|
| Map | **A new battlefield every match.** When the scene starts, `MapRuntime` runs the original terraced-heightfield algorithm (renormalised fBm, terraces, rim mountains, carved connecting ramp) with a fresh seed and builds everything from it before the rest of the scene wakes up: the **Unity Terrain**, its splat weights and relief occlusion, the water and deep-water volumes, the land beyond the rim, the start plateaus, ore fields and mirrored expansions, boulders, scenery, groves and bushes, and the NavMesh — under half a second on the MacBook Neo, the per-texel work on worker threads. **The two bases are always joined by ground a unit can drive.** The generator checks that on its own coarse cells, but the terrain is a smoothed surface through them that climbs a single terrace step more steeply than units can, so on two maps in forty the whole other half of the map was reached only through a crack one unit wide; the check now runs on the ground as the units see it, and where it fails a ramp is carved. For the same reason a boulder is never left in the mouth of a pass (on one map in forty a rock left a gap of a metre or two beside a slope). Maps that were fine, the default among them, are unchanged. Restart or return to the title screen and the next match gets another map. `-sfseed N` (or `MatchSettings.mapSeed`) fixes the seed; the benchmark and the AI evaluation stay on the default map so their numbers compare. The editor's map builder runs the same generator once, so the saved scene has the default map to look at and edit; untick *New Map Every Match* on the Map to play the saved one. The ground is four photo-scanned surfaces from Poly Haven (CC0): mossy earth with small stones where the grass grows, grey-brown stony dirt for most of the map, lichen-streaked rock slabs on the cliffs and beach sand on the shores and dry ridges, each packed with its scanned height by `Tools/blender/pack_textures.py`. The custom terrain material blends them on that height, so stones stand out of the sand and moss fills the hollows between them, darkens the hollows the scans' colour leaves unshaded, samples each layer twice at different scales and angles swapped over in broad patches so nothing repeats in a grid from above, adds faint folds tens of metres across so broad slopes are not smooth as dunes, and tints moist hollows and shores darker and greener and exposed ridges paler. Cliffs are sampled biplanar so rock strata follow the terraces; baked relief AO. (Before, the whole map was one orange dirt photograph tinted four ways under a warm grade, with the photograph itself imported as its own normal map.) Moss grows in patches with bare gravel between them, rock breaks through on the steep faces and high ground, and scree gathers below it. (Until this edition none of that showed: the splat weights were lost when the terrain asset was saved, so the whole map rendered, and grew grass, as moss. The editor's builder now writes them straight into the splat texture as its last step; a match's map is never saved, so there the weights are simply set.) Beyond the rim the land continues as a ring mesh that starts exactly at the terrain's edge height, is textured by the same splat rules and relief shading, and climbs into ridges, so there is no seam, cliff or colour change at the border; the fog of war follows the border ground out there instead of smearing its edge into streaks. Terracing walls every lake with cliffs, so stretches of shore picked by a slow noise field are slumped into sandy beaches running down into the water, and the rest keep their cliffs. |
| Wind | Every match draws its own weather from the map seed: a heading, a strength (a still day or a blustery one) and its own gust rhythm, all pure functions of the match clock, so a seed blows the same way every time. Everything agrees about it. The trees lean and the grass ripples in gusts that travel across the map along the wind, the twigs flutter and each leaf card shivers on its own; the cloud shadows drift with it — with the day's wind, not the gusts, at a steady one to three metres a second (they used to follow every gust at up to 14 m/s, and raced across the map); smoke and embers lean downwind; fire runs downwind and moves fast in a gust and hardly at all in a lull; a tree leans with it as it falls, and a snag burned through at the foot goes over the way the wind pushes it. When it blows hard, leaves tear out of the crowns and are carried away downwind. **Blasts move the air too.** A Mauler's muzzle blast and a shell bursting throw out a wall of it that races over the ground at about 50 m/s: the grass and the bushes it reaches are laid flat away from it and stand up again behind it, the crowns are thrown over and bend from the foot rather than stretch, leaves are torn out of them, and the air draws back a little as the front passes. It also moves everything else loose in its path, as it arrives there rather than all at once: the water ruffles in small rings, mostly darker than the lake (a roughened surface reflects less sky) with a little spray; dust lifts off dry ground and rolls outward, with clods and grit thrown close in; and when the front reaches the middle of the view the camera takes a jolt, a beat after the blast by the time the air took to get there. It draws nothing of its own — no ring on the ground, no ripple in the image (there used to be both). Moving air is invisible; you see it only in what it moves. A gun's is thrown forward out of the muzzle, so the growth in front of it goes over and what stands behind the tank hardly moves; a burst goes all round. The four strongest fronts alive at any moment are uploaded to the vegetation shaders (`Shaders/SF_Wind.hlsl`), which is all the state there is — a plant works out what the pressure is doing to it from its own position, so nothing has to be stored per plant and a seed replays identically. |
| Vegetation | Groves of spruces, gnarled broadleaves, tall leaning trees and spreading jacarandas in the meadows, lone trees, dead snags on the bare ground, conifers on heights no unit reaches, bushes through the green, drifts of flowering shrubs, ferns in the shade of the groves and reed beds along the shores — 880 plants on the default map, placed by the map generator and kept out of the bases, ore fields, expansions and narrow passes. A tree closes the ground under it (the trunk, its low boughs and a unit's own radius) as *Rubble*, the same area a boulder stands on: infantry, Diggers and Skimmers go round it, a Mauler drives through and knocks the tree down ahead of its hull. (The footprint used to be the bare trunk; Rubble is an area, not an obstacle, so the NavMesh let a unit's centre right up to its edge and units walked half inside the trees.) Groves are solid woods, so each one is checked as it grows: if it would cut a base, an expansion or an ore field off from the rest of the map, it is not planted. (That check used to judge the ground by the height field's coarse cells, far stricter about slopes than the NavMesh units actually drive on, and on about two maps in five it found something already cut off before the first tree — the rim of a base's plateau, a narrow pass, or an expansion in a basin nobody could enter — and turned every grove away, leaving a bare map of lone trees. It now reads the same slopes the NavMesh does, takes the narrowest passes from the NavMesh itself and keeps the woods clear of them, and expansions are only placed where every ore field can be reached: over 40 maps, 3 lost a grove, 4 in all.) A felled tree is a rod pivoting on its stump: gravity's pull grows as it leans, so it starts slowly, comes down faster the further it goes, and a tall tree takes longer to fall than a short one — measured, 1.5–1.7 s when a blast throws it. **A Mauler shoulders a tree over.** The trunk is held against its glacis — however far the hull has come past the foot of the trunk, the tree leans at least that far — until it goes over faster than the tank drives and falls away ahead of it. The blow takes the way off the tank (4.1 m/s down to 1.4–2.0 m/s against the scanned trunks, more the thicker the trunk), its nose rides up against the trunk and the engine works hard, and the tree is down 1.4–1.8 s after the hull met it; the tank then rolls over the log, which is pressed into the ground and splinters under the tracks. (It used to start the tree over slowly and drive on through the trunk while it still stood nearly upright; a staged push now keeps at least a quarter of a metre between the trunk and the glacis throughout.) How hard it was shoved sets how fast it starts (a shell close by against a snag that simply gives), the wind leans it on the way down, it comes to rest on its own boughs rather than flat on the ground, and it thumps, springs back a little and settles (a little, however hard it landed: a tree flung down at full tilt used to rebound 45 degrees and fall a second time). If another tree stands in its way it hangs up in that crown part-way down and creaks there until it slips off — or until the tree holding it comes down too. The dust, the shower of leaves and splinters and the camera shake all follow the speed the crown was travelling when it hit. Explosions throw trees over away from the blast and flatten bushes, and set what they reach alight. Whether a given tree goes over is a **chance**, not a rule: a shell landing on top of one takes it down about four times in five, a couple of metres off about a third of the time, and out at the edge of the blast's reach (a little over four metres for a Mauler's shell) about one in twelve — and a thick trunk stands where a slender one snaps. Measured over 60 bursts in a wood: 66% / 45% / 21% / 6% across the four bands out to 4.3 m, 27% of everything in reach (the scanned broadleaves and jacarandas have thicker trunks than the drawn ones did, and the thickness scale was moved so the broadleaf keeps its old odds; before, 80 / 35 / 17 / 8% and 23%). So shelling a wood thins it rather than flattening it, and the same shot twice is not the same picture. **Everything green burns**, each at its own rate — dead snags and dry reeds readily, resinous spruces hard, green broadleaf slowly — and **the grass between them carries the fire**: a coarse fuel grid burning cell to cell and lighting whatever is standing in it, which is how a fire crosses a meadow from one stand of trees to the next. **Only what grows burns.** The fuel is worked out from the same rule the ground cover places its tufts by, so a fire stops where the grass does: bare gravel, sand, rock and the trampled ground under buildings and ore carry nothing, flames stand only on the grass of a patch that is only partly grown, and the ash it leaves stops at the grass's edge. (The first fuel grid also counted a quarter of the gravel, and let bare ground burn: 1,661 of the 4,051 cells it would burn had no grass drawn on them. Now 4 of 1,783 do, at the thin, random edges of meadows.) A fire runs downwind: it passes to what lies that way far more readily, and from further, than across or against it, so a blaze works along a meadow as a front rather than a circle. **Damp ground hardly takes at all** — the water level against the ground at a spot and five metres around it thins the grass's fuel and a plant's chance of catching, so shores, reed beds and the grass along a lake resist while the dry terraces above them go up. How far one fire gets is drawn when it starts, and each one has a budget: most are over in a handful of plants, a few run through dozens, and no single fire can take more than about a sixth of the map's growth. Several can: 30 fires lit one at a time on a test map took 1–66 plants each (median 3) and left 66% of the plants and 86% of the grass burnt. **A wildfire can set a building alight.** While a burning tree or burning grass is at a structure's walls, each moment carries a small chance of it catching, the more the harder the fire burns: measured on buildings set down in meadows with the grass lit upwind, a fire that reaches the walls sets about three in ten alight. A burning building flames on its roof and walls under a column of smoke for a quarter of a minute and loses 6–12% of its health — visible on its bar, never enough to destroy it on its own — and its owner is told it is on fire. Bases stand on trampled ground, so it is the outlying structures near meadows and woods that are at risk. Burnt grass does not come back — black stubble on ashy ground for the rest of the match — and only 45 plants and 150 patches of grass burn at once. A burning plant is licked by tongues of flame from a generated flipbook (`Tools/make_flame_sheet.py`, drawn upright so they stay vertical) with a hot fire at the foot and now and then a rolling billow, under a column of smoke that leans with the wind — dark and thick while the leaves burn, thinner and greyer once only wood is left — and embers carried downwind; its firelight breathes and gutters, the wood chars black with embers glowing in the cracks, the crown burns away, and when it is out the black snag smoulders for a while with a thin pale wisp. The plants are **photo-scanned**: Poly Haven's CC0 scans of a gnarled island tree (the broadleaves), a fir (the spruces), a slender young broadleaf that leans (the tall trees), a jacaranda (the spreading, many-stemmed trees), two searsia shrubs (the bushes and the flowering shrubs) and a fern — each a real trunk with its own bark photograph and up to two million leaves and needles, far more than the game can draw hundreds of times. `Tools/blender/bake_scanned_flora.py` keeps the trunk and limbs, decimated to a few hundred to 1,800 triangles with the scan's own UVs and bark, and bakes the foliage: it groups the leaves and twigs into clusters about a metre across, renders each one in Blender from the side the RTS camera sees it (out of the crown and from above) into a tile of an atlas, colour and normals, and puts one card where the cluster was. Each card shows all the foliage within a sphere round its cluster, its rim ragged where leaves drop out one by one, so neighbouring cards overlap the way sprays on a bough do (showing only its own cluster, a card covered a sixth of its tile and the crowns looked burnt). A broadleaf is 150 cards on 1,600 triangles of trunk, a fir 160, a jacaranda 170, a bush 40 cards. Each scan's leaves are tinted onto a green that sits with the grass (their mean colour is measured from the atlas, so a re-bake keeps it), and the flowering shrubs carry clusters of pale pink blossom among the leaves. (The trees before were grown by script: a branch skeleton hung with photographed leaf sprays that belonged to no tree in particular, over a solid dark canopy — blocky crowns of a few large cards, and bushes of 64 triangles that looked like agaves. That generator stays as the fallback for a kind with no scan baked.) The cards bend with the wind and are thrown over by blast pressure exactly as before — the models carry the same sway weights — and the far copy drawn beyond 70 m keeps every card and thins the trunk. Ferns and reeds are too small to cast shadows or to be drawn far away. Measured against the previous build (AI-vs-AI benchmark, alternating runs), the scanned plants cost nothing measurable: 16.7 against 16.3–16.9 ms a frame on a cool machine, 18.1 against 18.1 ms once warm. All of it is drawn GPU-instanced by one shader (`SF_Tree`), a draw per kind for bark and one for foliage: warmer on top and cooler underneath, lit through from behind looking toward the sun, and swaying in a gust that travels across the map. Under fog of war the player sees each tree as they last saw it. A crown that comes too close to the camera shrinks out of the way. |
| Wildlife | Packs of wolves and lone foxes roam the meadows, flocks of songbirds feed in the open, and eagles circle overhead: Quaternius's animated low-poly animals (CC0, from OpenGameArt), coloured by the scene builder (their materials come plain grey) and playing their own walk and flight cycles. The ground animals walk only where a unit could — every step is checked against the NavMesh, so they go round trees, rocks and water — keep a few metres between packmates, wander from spot to spot and stop now and then. **Songbirds** live on the ground between flights: a flock feeds on open ground clear of the crowns, each bird hopping, pecking and looking round, until something comes near or it grows restless; then the whole flock flushes and flies low to another open patch with a small bird's bounding flight — a burst of wingbeats climbing, wings shut for a short fall, a burst more — and lands one by one. The bird is Quaternius's, re-posed and re-animated for it (`Tools/blender/build_songbird.py`): the model is a perched wagtail whose only cycle is a flutter of folded wings, so flying it as it came read as an upright dark blob flapping in place; it now has a flight cycle with its wings spread and beating, its legs tucked and its tail shortened to a finch's, a fold, a perch and a peck, and its colours come through one small palette texture (chestnut back, slate head, rusty breast, dark flight feathers with pale coverts that flicker as the wings beat — a plain brown bird disappears against brown ground from above). It is drawn about twice life size, or it would be a few pixels lost in the grass. Units coming close, gunfire, explosions, deaths and burning plants frighten them all: a pack bolts away and settles on new ground far from everything that has happened lately, a flock flushes and flies off downwind of the trouble, and eagles swerve away, climb and keep their distance while the danger lasts. Flyers hold a steady height over the highest ground under their whole circle and change it slowly, so they no longer bob up and down over every terrace. Pure scenery: nothing targets them, they block nothing and they do not burn; they are shown only where the player can see, and they take fright only at what the player could see too, so a pack bolting at the edge of the fog never gives away an unseen enemy. |
| Rocks and scenery | Boulders are six photo-scanned rocks (Poly Haven, CC0), decimated in Blender to 900 triangles each by `Tools/blender/build_rocks.py` with the scan's normal map keeping the detail, drawn by their own shader (`SF_Rock`) with dust settling on their lower flanks; each boulder on the map is one of them at random. Scanned crags and slab groups stand where procedural rock spires and shelves did, beside a crashed dropship and ruined pylons (`Tools/blender/build_env.py`), placed only where 80% of the footprint is ground no unit can use — cliff faces and the rim. They are kept out of the NavMesh bake, but the part of a piece that stands on usable ground is marked not walkable, so no unit walks through the edge of a crag or a wreck. **StarForge ▸ Debug ▸ Check Map Blocking** samples the NavMesh under every object: it also tests a ring just outside every trunk (where a unit's centre would put its body in the bark) and that the two bases are still connected. On a generated map 22/22 scenery pieces, 55/55 boulders and 184 of 185 trees block ground units round the trunk (measured with the scanned trunks' own radii), and the bases are connected (Maulers still crush boulders and trees). Boulders' Rubble is padded by a unit's radius too. |
| Ground cover | Grass and pebbles, grown at load from the terrain's own splat weights and drawn GPU-instanced per 32 m chunk. Blades are real geometry, not alpha-cut cards: `discard` would switch off the hidden-surface removal that keeps overdraw cheap on Apple GPUs. Wind is one slow travelling gust per clump; per-blade flutter twinkled at RTS distance. Blade attributes are sampled at the centroid: with MSAA, a blade seen almost edge-on was shaded from a point off the blade, and the extrapolated values blew single pixels up into sparks that bloom turned into flashing white lights. |
| Battle damage | A map-wide mask (`GroundMask`) records what the fight has done: flattened under structures and ore, charred where explosions and burning trees were (healing over minutes), churned along the tracks of moving units (fading in seconds), and turned to raw earth under a Mauler's path (fading over minutes). Grass bends, parts and burns on it; the terrain darkens scorched and churned soil. Tracked vehicles also leave tread marks, two cleated bands per Digger or Mauler, that fade over 40 s. Shells and wrecked vehicles dent the terrain itself: a shallow, smooth **crater** (35 cm for a shell), drawn as a scorched centre inside a ring of lighter thrown earth with ragged edges. Craters do not stack — the ground only goes down to the deepest single bowl covering it, so a spot shelled all match is a wide dip, not a pit (deep pits turned their walls into cliff rock) — and never open under structures or ore, or below the water line on dry land. The match works on a copy of the terrain data; grass, pebbles, rocks and trees settle into a crater, and units sink into it as they cross. Explosions also break the boulders they reach. |
| Navigation | **NavMesh** (AI Navigation). Structures and ore carve the mesh; Diggers skip avoidance so mineral lines never jam; Maulers slow down to turn instead of strafing. Units wade into water up to 1 m deep, at about half speed; deeper water is marked not walkable with box volumes, and a Skimmer skims over the shallows instead of diving to the bed. Boulders and tree trunks stand on a *Rubble* area that only Maulers may path through: a Mauler drives straight at a rock or tree and crushes it, and the NavMesh tiles under it are rebuilt in the background (on a copy of the baked data, so a match never edits the asset) so everyone can use the ground; the same happens when an explosion or fire brings one down. **A move order survives the NavMesh changing under it.** A rebuild takes the path from every unit whose way crossed the tiles it changed — and a Mauler in a wood fells its own — and that used to count as arriving: the unit stopped where it was and stood until ordered again, and a Mauler crossing a wood could wait up to nine seconds for a new path while its long requests were started over by each rebuild. Now a unit that loses its path asks again, and stops only at its goal or at the end of a partial path (as near as the ground allows); path requests get 1,000 search steps a frame rather than Unity's 100, so nearly every one is answered in the frame it is made; and the tiles are rebuilt at most every 1.5 s. Measured with an army sent through woods and over rocks every few seconds for three minutes: from 11 stops of 1.5–4 s to none, the slowest order answered in 0.54 s rather than 2.5 s; in AI-vs-AI play, from 12 stands of up to 29 s to none. Structures need dry ground. |
| Models | Authored in **Blender** by script (`Tools/blender`) — the original ten, the Skimmer and Sentinel, the scenery, and the trees and bushes — exported to FBX with named material slots that map to URP materials. The exporter also bakes ambient occlusion into vertex colour R (cast against the whole model and the ground) and writes per-vertex shading data into G and B where a part asks for it (per ore shard, a random value and the height along the shard; per tree clump, a random value and how freely it sways), exports movable parts as child objects with their origin on the pivot, and writes a second FBX of pre-cut chunks for each structure. `Tools/blender/starforge_models.blend` has them all laid out for editing. |
| Unit surfaces | One custom lit shader (`SF_Unit`): hull plating triplanar in object space as an x2 detail multiply normalised by its own average — rectangular plates split the way hull panels are, recessed seams with a lit lip, rows of rivets, hatches and vents, grime in the seams, generated by `Tools/make_panel_texture.py` (the original's armour photograph it replaced read as brickwork at the units' scale) — edge wear found by screen-space curvature (bevels turn the normal fast, flat plates do not) and chipped by that texture, grime climbing from the ground and settling in cavities, a cool sky rim for silhouette. Ore is eerie, living crystal: veins of light seen deep inside each shard with two depths of parallax, so they shift against the surface as the camera moves and the crystal reads as a volume; a dark, glassy, near-black teal body between them; a spectral teal-green glow under a violet rim; a slow breath that swells the whole seam every five seconds on its own phase, with a wave of light climbing each shard; and slender needles bristling among the prisms. Motes of light rise off the seam and circle it as they go, more as it breathes in, over a pool of teal light on the ground that swells in time with it; a mined-out seam gives up its light in a burst of shards and motes that hang in the air. A Digger's hopper and drum windows are dark ore glass that lights up teal with the load it carries: the glow stirs while it cuts, swells in over a second or so once the load is aboard, flares as the hopper seals and fades out slowly at the drop-off (it used to switch on and off), the light rolling slowly round the drum, and the pool of light it spills on the ground follows the same curve. Per renderer it also carries damage charring with glowing seams, the construction hologram, the hit flash and burning — none of it with `discard`, which would cost early depth testing on every unit. |
| Animation | Parts move procedurally from the simulation's own state: legs swing with the stride, the gun and the tank barrels recoil on the shot, the Digger's arm dips and its cutter spins while mining, ore heaps up in its hopper and its drum turns while hauling, an ore seam's crystals shrink as it is mined out, the Foundry's control head sweeps and the Workshop's crane trolley travels. |
| Destruction | A destroyed structure is swapped for its pre-cut chunks as rigid bodies, thrown outward glowing hot, tumbling and settling on the terrain, then sunk out of sight. **A unit that dies leaves a body or a wreck** (`UnitWreck`), played out with rigid bodies on the terrain: a rifleman is knocked off his feet away from the shot — dropped where he stands by a round, thrown a few metres by a shell bursting beside him — his legs give and his rifle flies out of his hands; a Mauler's ammunition goes up, heaving the hull off its tracks and blowing the turret clean off to land beside it (or leaving it wrenched round on its ring); a Digger is rolled over onto its side; a Skimmer drops out of its hover still going and slides to a stop on its belly, throwing up dust — or goes under in deep water. Machines burn where they come to rest, charred with glowing cracks that cool, under flames and a column of smoke leaning with the wind. A wreck lies a quarter of a minute and a body about eight seconds, then sinks into the ground; past 36 on the field the oldest go early. (Units used to sink into the ground inside a fireball, and a rifleman blew up like a vehicle; he now goes down with sparks off his armour and a puff of dust where he lands.) Only what the player saw die leaves anything. |
| Water | Its own shader (`SF_Water`) with no repeating pattern: three layers of the original's ripple map at scales and angles with no common period (a smoother synthesised ripple map was tried and dropped: its long waves looked worse), through UVs bent by a slow noise field, over three long analytic swells. It shows the lake bed through the surface from the camera's opaque colour copy, bent by the ripples (never picking up a unit standing in front of it), absorbed channel by channel along the path through the water, red first, with the water's own scattered light filling in: clear, green-tinted shallows over the sand, dark blue-green deeps. Depth drives foam crests that roll in toward the shore and lacy foam lapping at the waterline — broken up by noise into patches that come and go along the shore, and lit as a surface rather than a light, where it used to outline every lake with a glowing white stroke — and lights soft caustics on the bed in the shallows. Glints are broad and dim and ripples calm with distance, so the surface does not sparkle at RTS range, and the sky it reflects is slightly blurred so neighbouring ripples do not pick different patches of cloud. The surface covers every hollow of the terrain below the water line (built from the 2 m generator grid, it had left dry pits beside the lakes). Units moving through the water leave a wake — rings dropped every metre or so that spread and overlap into a V, and foam that opens up and dissolves — and splash where they go in or come out; shells landing in the water throw up a white column and a spreading ring instead of earth. |
| Fog of war | One **URP full-screen render feature** reconstructs world position from depth and applies the player's visibility texture — explored ground dims and cools, unexplored goes dark, with a shimmer at the vision edge. |
| Atmosphere | The same pass integrates exponential height fog along the view ray (mist pools over the lakes and in the low ground, plateaus stay clear), distance haze and sun in-scattering. It already has every pixel's world position, so all of it is free of extra passes. Cloud shadows are the sun's light cookie, a remapped cloud photograph drifting slowly with the day's wind. |
| Occlusion | **SSAO** at half resolution, normals reconstructed from the depth texture the fog pass already needs, applied after opaques as a single multiply — the cheap path on a tile-based GPU. Its sampling noise is fixed per pixel: URP's default blue noise is re-rolled every frame for TAA to average out, and without TAA it made the grass boil. |
| Effects | Layered explosions: a white-hot flash, a fireball of several tinted flipbook puffs, dark smoke that billows while the fire burns and rises into a column as it dies (smoke, dust and mist are lit cloud puffs, `SF_Smoke`: four cauliflower clusters with their own normals, occlusion and ragged coverage, made by `Tools/make_smoke_puffs.py`, lit by the sun and from above so the tops of the billows catch the light and the folds stay dark, and eaten into wisps as they age — the photographed smoke sheet drew every puff as the same flat grey sprite. **A puff never reaches opaque**: its coverage is soft-edged and eaten at two scales, so a cloud is built from many faint puffs over one another rather than from one solid sprite, and its outline stays ragged close up and far away. The first version of the atlas got both wrong — perfect hemispheres you could count, under a coverage that saturated in the core — and a burst drew as a hard dark lump of rock sitting on the ground. Colours are grey rather than near-black, too: dark smoke over a sunlit landscape still reads grey, and the old values came out as a hole in the scene), short hot sparks, small charred debris that trails fire and bounces off the terrain, a fountain of earth and a dust ring for ground bursts, drifting embers, a point-light flash, scorch marks and camera shake — with structures coming apart in a short chain of secondary blasts. Guns fire shaped muzzle blasts (flares drawn with the streaks): the Mauler's is a white-hot core in a long orange blast with jets from the muzzle brake, a cone of smoke dragged to a stop and dust kicked off the ground, and its shell flies as a glowing slug leaving a smoke trail. **The Mauler is meant to feel heavy.** Its engine smokes from the two stacks on the deck — a wisp at idle, a grey-black plume under load, a belch as it pulls away from a standstill, and dirtiest while it leans on a boulder or a tree with the engine at full and the hull going nowhere — laid out behind the hull as it drives and leaning with the wind. (It took three rounds: the first poured out a cloud the size of the tank, the second was a haze nobody could see. The puffs were also being born on the engine deck, where the smoke shader's soft edge — which lets smoke meet the ground without a hard line — faded them out against the hull behind them; they now start half a metre above the stacks.) Its tracks roll a pale dust trail off the back of each run, more the faster it goes and none at all off wet ground, leave a low wake hanging over the ruts for a few seconds, and flick stones out from under the cleats. The ground remembers it: the cleated tread marks last a minute, and under them the tank churns a band of turned earth that heals over minutes, like a crater's thrown soil. When it fires, the whole tank is shoved: the hull is thrown back on its suspension and the nose comes up, rocks down past level and settles, a heavier flash and a bigger cone of smoke go out of the muzzle and hang in the air for seconds, the gun goes on smoking for a couple more, and the blast throws a wall of air forward over the growth in front of it. Its shell lands a size up from its splash — the heaviest thing in the game short of a building going up — and throws its own front all round. Zoomed out the engine smoke thins, since a plume nobody can see is not worth the particle budget. A Mauler crushing a boulder throws rock and dust and leaves a scorch; a tree coming down shakes out leaves and splinters and throws up dust all along the trunk where it lands; burning trees carry flames, smoke, embers and a flickering firelight. Plus a single instanced depth-decal shader for selection rings, footprints, order markers, scorch marks, tread marks and the light under ore, and instanced health bars and tracer streaks. |
| Audio | **Weapons** are real firearms, from the CC0 Free Firearm Sound Library: every gun in it was recorded from beside the shooter and again from a distance, and a shot is mixed from both — the crack from the near take, the report rolling back off the ground from the far one a moment later — with a slap-back off the terrain behind. A Trooper's rifle is an AK-47 (two takes, a little apart in pitch), an SKS and an AR-15, with a short thump of body under each crack (four takes, so a firing line is not one sample repeated). (Before, the rifles were the thinnest takes in the library at a fifth of the level of the music, and players heard no shot at all: measured, every shot did play, it was simply buried. Now the shot is full-bodied, played at 0.4 against the music's 0.54 rather than 0.15, and a new shot takes a free voice, or else the one closest to finishing, instead of cutting off whichever voice came round next; the tank engines' idle loops are paused while no Mauler needs them. Staged fire-fight: 16 shots, every one heard.) The Mauler's gun is a 12-gauge, a .30-06 and a pump gun pitched down more than an octave and rolled off below a kilohertz, which is what a gun that size sounds like from across a valley, played louder than anything but a structure going up, with a **falling sub thump** under the crack and the **report rolling away** over the ground for two seconds after it (a loud bang is not frightening; the thing you feel and the thing that goes on after it are); the Sentinel's bolt and the Skimmer's plasma keep their Kenney energy sound with a real muzzle crack under it, which is what they were missing. Every take of a sound is levelled to the same loudness with its peaks rounded off, so a volley does not lurch about. **A tree** cracks and groans as it goes over and crashes with a rush of leaves as it lands (a felled tree, a tree creaking and wood breaks, all CC0); a bush flattened under a Mauler just rustles, and a fallen trunk splits under its tracks. A rifleman does not explode: his death is the round ringing off his armour and the thud of him going down. **Shells landing and things blowing up** are built the same way, from Kenney's explosion crunches: the crunch carries the detail, and under it go the thump and the long rolling report the recordings have no low end for — three takes for a shell (3.2 s), two deeper and slower ones for a structure (5 s). **The Mauler's engine** is Kenney's low engine dropped an octave with a harsh diesel knock under it and a growl, and its **tracks clatter** over the top, built from rubberduck's CC0 metal hits — the giveaway that a thing is tracked rather than wheeled, and the part of a tank you hear before you see it. **Each Mauler is heard as itself**: the three loudest in earshot get a voice each, panned to where they are on screen, the engine murmuring at idle and climbing in pitch and level as it works (a standing start, a hill, a boulder being shouldered aside), the clatter coming in with speed and running faster the faster it goes. Measured on one tank: idle, engine at 0.08 and pitch 0.80 with the tracks silent; flat out at 4.1 m/s, engine 0.26 at pitch 1.14 and tracks 0.20 at 1.23. (They used to share one bed that could not say where the tank coming at you was, or that it had just started to move.) All of it is aimed at 80–400 Hz rather than at 40: a laptop speaker moves no air down there, so weight put in the sub-bass is not felt, it is simply lost, and the rest gets turned down to make room for it. Measured, the family runs bright to deep — a rifle's energy centres at 800 Hz, the cannon at about 180, the engine at 220, a shell landing at 240–320, a structure going up at 150–200. The rest are Kenney's CC0 packs (glass for crystal, mining knocks, the Skimmer's drive, interface clicks and alerts), several takes of each picked at random and pitched a little; anything missing falls back to the sounds the game used to synthesise. **Ambience** is synthesised into seamless loops by `Tools/make_audio.py`: wind that gusts (stronger zoomed out), water lapping when the view holds a lake, a fire's roar and crackle near burning plants, and birdsong over the green that falls silent for half a minute after an explosion nearby. **Music** is recorded CC0 tracks from OpenGameArt, one mood at a time: *At Home* (wolfgang), *First Light Particles* (yoiyami) and *Contemplation* (Joth) — warm orchestral, piano over pads, and drifting ambience, played in turn — when nothing is happening; *Insistent* (yd), a dark, quiet loop, when armies gather or the enemy comes into sight; *Battle Theme A* (cynicmusic), strings and horns, while fighting is on screen. A mood has to hold a few seconds before the music follows it (quicker going up than coming down), the moods crossfade over several seconds, and every track is played at one measured loudness well under the effects. (The music used to be synthesised; its plucks and tremolo beeped. The calm set that replaced it was minor-key piano, which sounded creepy rather than peaceful under a quiet base; the tracks there now are major and consonant, measured with `Tools/measure_music.py`.) Everything is heard only if the player could see it. |
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
  Ground 04*, *Aerial Rocks 02* and *Coast Sand 01*; the rock scans *Rock Moss
  Set 01*, *Rock Moss Set 02* and *Boulder 01*; and the plant scans *Island Tree
  01*, *Fir Tree 01*, *Jacaranda Tree* and *Fern 02* by Rob Tuytel and Rico
  Cilliers, *Tree Small 02* by Rico Cilliers, and *Searsia Burchellii* and
  *Searsia Lucida* by James Ray Cock and Jenelle van Heerden.
  `Tools/fetch_assets.py` records each one's authors and real-world size in
  `Art/Source/polyhaven/manifest.json`.
- **ambientCG** (ambientcg.com): the leaf atlases *Leaf Set 014*, *016*, *019* and
  *024*, which dress the procedural plants kept as the fallback for a kind with no
  scan baked.
- **Kenney** (kenney.nl): *Sci-Fi Sounds*, *Impact Sounds* and *Interface Sounds* (the
  Mech's footfalls, hydraulics, launches, lasers and drop are built from the first two).
- **Quaternius** (via opengameart.org): *Animals Pack* and *Animal Pack Vol. 2* (wolf,
  fox, eagle, songbird).
- **JangaFX** (jangafx.com): the EmberGen simulations *Small Camp Fire* and *Ground
  Explosion* from their free VDB library, rendered in Blender into the fire and
  explosion flipbooks.
- **OpenGameArt** music: *At Home* by wolfgang, *First Light Particles* by yoiyami
  and *Contemplation* by Joth (the calm set), *Insistent* by yd and *Battle Theme A*
  by cynicmusic.
- **OpenGameArt** recorded sound the weapons (the Mech's included) and felled trees are built from: *The
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
  oscillate (the lesson from the original's controller). With vsync on -- as a match
  always is -- a frame is shown for one refresh or two, so it judges the load by how
  often a frame misses its refresh (past the preset's limit -- 28% on *High*, about 47 fps,
  15% on the others, about 52 -- it steps down; after a clean spell, it tries a step up),
  not by frame length: a vsynced frame never measures under
  16.7 ms however light the load, so the old rule could never see room to climb, and one
  heavy fight left the picture at its floor for the rest of the match.
- **Three graphics presets** on the title screen, remembered between runs, each
  trading the three things that actually cost on a fanless laptop: how far
  shadows are drawn, how much ground cover is grown, and how far the adaptive
  controller may drop before it gives frames back. *High* is the measurement
  below: the 150 m of shadow and full grass it had before the Mechs, with the render
  scale free to go down to 0.60 in the heaviest fights (it was 0.70) and let miss up to
  ~28% of refreshes -- about 47 fps -- before it steps down: 45 is enough here, and the rest
  goes into a sharper picture;
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
long enough for both Mechs to land at about four minutes and fight, the camera following the
heaviest action -- on the MacBook Neo, plugged in:

| | Average | Frames with a Mech in view | Render scale (average) |
|---|---|---|---|
| **High** (150 m shadows, full grass, steps down past ~28% missed refreshes, floor 0.55), two runs | **20.3 ms (49.3 fps)** | **21.2 ms (47.3 fps)** | 0.62 (0.56 with a Mech in view) |
| the same with a 0.60 floor, before the Mechs were fitted out at the drop | 20.1 ms (49.8 fps) | 20.0 ms (50.1 fps) | 0.69 |
| the same with 160 m shadows | 21.2 ms (47.1 fps) | 22.1 ms (45.3 fps) | 0.68 |
| the same with 160 m and a 0.70 floor | 22.1 ms (45.3 fps) | 23.5 ms (42.6 fps) | 0.74 |
| 120 m, grass 0.85, steps down past 15% | 19.0 ms (52.6 fps) | 19.9 ms (50.1 fps) | 0.64 |
| *Balanced* (95 m, grass 0.6), battery charging | 19.6 ms (51.1 fps) | 20.7 ms (48.4 fps) | 0.63 |

The first row is the final build. Once the Mechs were fitted out for the fight at the drop,
the benchmark's got rotary cannons and missile racks against the armies they faced -- the
busiest weapons there are -- and frames with a Mech in view fell to 43.4-46.0 fps at the 0.60
floor. A missile now lays a smoke puff every 1.5 m for about a second (every 0.9 m for two,
several hundred soft puffs a salvo), a rotary cannon throws up a hit every sixth round rather
than every fourth, and the floor went down to 0.55, where only the heaviest fights take it.
(Before the second row, the same values given on the command line measured 50.0 / 49.6 fps. Two runs in between came out at exactly 20 fps: the Mac had locked
its screen, and behind a lock screen the game only gets about 20 frames a second shown.)
Runs made while the battery was charging came out about 3 fps lower than with it full, so
note the power state with every number.

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

## Scope, honestly

- Single player against the AI (or AI vs AI). No multiplayer, campaign or saves.
- Units animate by moving whole parts (legs, arms, barrels, turrets, hover and
  banking) from simulation state; there is no skeletal animation or blending.
- The AI's learning is per machine and per player profile file; there is no
  trained neural network — the opponent model is Bayesian inference and the
  strategy layer a contextual bandit, as in the original.
