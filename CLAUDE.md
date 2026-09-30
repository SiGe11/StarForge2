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
| `StarForge/Evaluate AI/Quick` or `Full` | Play-mode AI vs four scripted archetypes (720 s / 900 s caps since the Mechs lengthened matches); report in the console and `persistentDataPath/starforge_ai_eval.txt`, each match's line with both sides' Mech Bay and Mech and how the AI's Mech spent its time by intent. `AIEvalMenu.Replay` (set `ReplayKind`/`ReplayGame`, call through `editor.sh call`) replays one game on its seed and logs both Mechs' reasoning every 30 s -- but a replay does not repeat the match exactly (frame timing differs), so read it for behaviour, not for the result |
| `StarForge/Build macOS Player (Apple silicon)` | Writes `Builds/StarForge.app` (Mono in practice; IL2CPP needs full Xcode). Cached rebuilds ~20 s |
| `StarForge/Debug/AI Internals` | Editor toggle for the developer-only AI inspector and memory controls |
| `StarForge/Debug/Check Map Blocking` | Samples the NavMesh under every scenery piece, boulder, tree trunk (and in play, ore and structures) and reports any a ground unit could walk through (`MapChecks.Report`) |

Benchmark the built player (AI vs AI; keep the editor idle while it runs). `-sfplay` keeps vsync and
the 60 cap, as a match runs -- the frame rate a player sees, and the one to compare builds on;
without it the run is uncapped with vsync off, which on 6000.6.1 comes out bimodal. Mechs land at
about four minutes, so use 420 s to measure them:

```bash
Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 420 -sfplay -sfbenchout /tmp/starforge_bench.txt
```

`-sfgallery <dir>` (with `-sfgalleryquick` to stop early) takes the same screenshots every run on the
default map -- base close and mid, ore, a shore, a grove before, during and after it is set alight, a
an animal, a songbird flock feeding and then flushed, the overview, the first fire-fight, then the Mechs
(`9_mech_close`/`9b_mech_mid`, the other side's `9c_mech_other`, three frames of a Mech firing
`9d_mech_fight_*` and the bay `9e_mech_bay`) -- so art changes can be compared before/after. Use the
player's gallery for looks, not editor captures: the Editor's Game view is about 810x375.
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
Every preset aims adaptive resolution at one 60 Hz refresh (`targetMs` 16.9). With vsync a frame is
shown for 16.7 or 33.3 ms, so `AdaptiveResolution` judges load under vsync by the share of frames
that miss a refresh (`SyncedUpdate`: down past `missHigh`, up after a clean spell under `missLow`,
both set per preset -- High 0.28/0.10, about 47 fps; the others 0.15/0.04, about 52):
the length rule could never see room to climb (a vsynced frame never measures under 16.7 ms), and
one heavy fight left a match at its floor. *High* once aimed at 21.5 ms ("~46 fps", 160 m shadows,
floor 0.80): 47.6 fps uncapped, but 39 in play. Baseline on `-sfplay` 420 s, plugged in, battery not
charging: *High* (150 m shadows, full grass, floor 0.55, `missHigh` 0.28 -- about 47 fps -- so the
45 fps allowance goes into pixels) 49.3 fps, 47.3 with a Mech in view, render scale 0.62 (two
runs agreeing within 0.2); with a 0.60 floor, before the Mechs were fitted out at the drop,
49.8 / 50.1 -- the armoury's rotary cannons and missile racks are the busiest effects there are,
so missile trails puff every 1.5 m for ~1 s and the rotary cannon's hits show every sixth round; 160 m
came to 45.3 with a Mech in view, and a 0.70 floor to 42.6. Runs made while the battery charged
came out ~3 fps lower, so note `pmset -g batt` with every number. **A locked screen caps the
player at 20 fps** (every frame 50 ms): when the keep-awake ended the Mac locked, and two runs
came out at 20.0 fps. Check `ioreg -n Root -d1 -a | plutil -extract IOConsoleUsers json -o - -`
for `CGSSessionScreenIsLocked` before believing a slow run; unlocking needs the user. The Mech fights are bound by cost that does not shrink
with resolution (160 m/1.0 grass 43.3 fps with a Mech in view, 110/1.0 46.1, 110/0.7 49.2, all at
the 0.60 floor): measure shadow distance and grass with `-sfshadows N` / `-sfdensity X`. The report's
"simulation ..." and "frames over 40 ms ..." lines (`GameWorld.SimMs/TickedMs`,
`GameBootstrap.MechBrainMs`, GCs, NavMesh island builds) say whether a long frame had a slow
simulation step; the `frame timing:` line (`FrameTimingManager`, `BuildMac` sets
`enableFrameTimingStats`) gives main/render thread and present-wait, but its GPU time is not usable
on Metal (frames overlap: GPU times longer than the frame). On battery this Mac turns Low Power Mode
on (its battery profile), and a long evaluation drains it even on the charger: check
`pmset -g batt` before believing a slow run.
The Neo is fanless and drifts about a millisecond as it heats, so compare rendering changes in
alternating pairs against the previous build (`-sfcap60` matches builds from before the uncapping fix).

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
`Art/Source/` (git-ignored): Poly Haven ground, rock and plant scans (the plants as .blend, ~700 MB;
pass asset ids to fetch only some), ambientCG leaf atlases, Kenney sound packs,
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

**Mechs.** One Mech Bay and one Mech a side, a match (README "Mechs"). Rules live in
`World/GameWorld.Mech.cs` (the bay: drop timer `MechDropDelay` 120 s, self-repair, repairing
the Mech, upgrades `CmdMechUpgrade`; the Mech's guns `FireMechGun`; `Faction.bay/mech/design/
upgrades/researching`), `World/Mech/MechParts.cs` (the fixed parts list, the 100-point budget,
`Generate(seed)`, upgrade costs -- the balance sheet) and `World/Mech/MechCore.cs` (on the
Mech's GameObject: stats from parts and upgrades, shield and armour in `Absorb`, every gun
picking its own target). `Unit.Tick` hands a Mech to `TickMech`; `Unit.Untargetable` is a Mech
still coming down. Designs are drawn in `BeginMatch` from the match seed, so a seed replays.
The Mech takes **no orders from its side**: every player `Cmd*` skips `def.Autonomous`; its
own AI (`AI/MechBrain.cs`, one per side, ticked by `GameBootstrap`) drives it only through
`CmdMechMove`/`CmdMechFocus`/`CmdMechHalt` and speaks through `MechAdvise`
(`GameEventKind.MechAdvice`), which the HUD shows as comms and `Commander.OnWorldEvent`
weighs (heed or ignore, by `Personality.adviceTrust`). Its morale lines are `AI/MechSpeech.cs`:
406 lines tagged by situation and tone (the user's 400 plus the six it had), picked by a reading
of the battle about once a second (standing 0.7 armies-and-Mechs, 0.3 bases -- on all assets it
read "even" through a match one side was losing; fights tracked by area, closed after 15 s with
no death within 60 m -- one map-wide feed never went quiet; turning points, extremis, near
defeat, calm global or local to a guarding Mech) with weights and cues. It has its
own `Rng` and speaks through `MechBrain.Speak`, which does **not** push `nextAdvice` -- speech
must never delay the calls the Commander acts on -- and it is kept rare (never within 30 s of
any comms line, 90 s between its own, at most 4 in ten minutes). `MechSpeech.Check` (the
table: all there, filled in, every bucket reachable) and `MechSpeech.JournalReport` (what the
player's Mech said this match and why, its calls, repeats and lines a minute) run through
`editor.sh call`. **The two Mechs are separate minds** (`AI/MechDoctrine.cs`): team 0's (the
player's) is the guardian with the numbers MechBrain always had; team 1's (the opponent's) is
a hunter, raider or brawler per match with jitter -- repairs at ~30%, lower caution, readier
duels, hunts from ~55-62% hull out to 170-210 m, Diggers and Foundries worth more, guard post
36-45 m out -- except while `homeThreatened` (it then neither hunts nor stands further out,
so a bay raised in its base still pulls it home: BayEdgeCases case 0). **Only the player's
Mech talks**: team 1's `Say` raises no event -- its Attack/Defend/Regroup go straight to
`Commander.HearMech` (`MechBrain.Listener`, set by `GameBootstrap`), the same kind and place
the event carried, and it has no MechSpeech. The player's comms mark a call about the same
place within 150 s as `GameEvent.repeat` (a 1.6x bigger incoming force is news): the HUD and
the radio leave it out, a Commander hearing the event still gets it. The HUD tags calls
(SUGGESTION / WARNING / REPORT) and shows flavour (Morale) quoted and muted
(`comms-line--flair`), never pushing a call off the feed. While the enemy Mech is about the
Commander waits for a bigger army before pushing (`Plan`: x1.5 with its own Mech, x1.9 without,
and +20% for each wave that Mech beat back, `mechRepulses`, up to four): before the last, it fed a
defending Mech one wave at a time and matches ran to the cap. The bay mends a Mech at
`MechCore.BayRepairRate` (50/s once left alone for 4 s, 15 under fire); at 75 a Mech holding its
base was back to full in under twenty seconds. The evaluation line gives the AI Mech's time by
intent: withdrawing 30-49% of it at full hull was the brain dithering at the enemy Mech's reach
(support walked it in, "their Mech has the edge" walked it out), so a Mech that backs off from
the enemy Mech keeps outside its reach for 12 s (`MechBrain.Shy`), goes in with its own
attacking army (`withArmy`) and stays in a duel it started while the odds hold (`hold`); and the
Commander does not start a wave at a Mech-held base while its own Mech is under 60%. The line
also gives its time by reason (`intentText` without numbers): what was left was "their Mech has
the edge" -- standing with its army because the odds were under 0.8 even together -- while its
army attacked on and the regroup call stayed silent (it only fired from 30 m away). Now it
reads as Holding once it is with the army, and `WarnOff` calls the regroup when its army is
going at a Mech it cannot beat. **MechBrain is omniscient and has a
~1,200 APM budget by design** -- the Commander invariants below (fog, APM) do not apply to
it, and must still hold for the Commander, which learns only what the advice says.
**Parts are picked at the drop.** `BeginMatch` draws only the pilot (`Faction.design` is a
placeholder until `designChosen`); at the inbound call `GameWorld.FitOutMech` runs
`MechArmoury.Compose`: 12 candidates from `MechParts.Generate`, scored against
`MechArmoury.Read` (the enemy's infantry/armour/Mech/structure value -- omniscient, like
MechBrain; the Commander never reads it) as offence against that mix x toughness^0.5, less for
a design with no answer to part of it or that repeats the other side's body, then picked by
weight (score/best)^3.5, so it adapts but still varies. Extra Hardpoints bought before the drop
assume two spare mounts (every frame has two; Compose skips candidates with fewer).
`MechDesigns.ArmourySurvey` measures it over 200 seeds and four enemy mixes: 375-385 distinct
loadouts in 400, the two sides share a body 3-4% of the time, a flame weapon on 68% of Mechs
against infantry and 47% against armour, a laser or railgun on 72% and 85%.
**The Flame Tower** (`MechWeapon.FlameTower`, appended) is a top-mounted turret: `WeaponPart.turret`
/ `turnRate`, `MechGun.yaw` (relative to the torso) turned in `TickGuns`, `Aimed` and the fire
cone follow it, `MechCore.Muzzle` rotates the muzzle with it, and `MechView` turns the kit's
`Turret` group (the other way on a mirrored left-hand mount). Adding it reshuffled the generator
and the survey found the arm flamer overtuned against a crowd (flamer designs 50 Troopers on
average, one at 86): it now does 0.16 to infantry in a 16-degree cone.
**A tracked Mech rides on its tracks** (`MechView.Roll`): the hull settles onto a plane fitted
through the ground along both runs (7 samples each over the flat length), through a critically
damped `Spring` with its rate and tilt capped -- up fast, down gently -- its height smoothed as a
world height (the agent's NavMesh height steps), and is then lifted so no point of either run is
under the ground at the tilt it actually has. **A positive Euler z lifts the right side**: the
roll used to be given minus the slope, so a tracked Mech always leaned the wrong way on a side
slope and its uphill track sank into the hill; and the landing squat (`crouch`) now drops only
the torso onto the suspension, not the hull into the ground. `Unit.UpdateFacing` steers a tracked
Mech for `agent.steeringTarget` with a built-up turn rate (`yawVel`), not the agent's velocity of
the moment. `MechTrackRide` measures all of it against the old motion (`MechView.LegacyTrackRide`)
over the same three routes, sampling at the end of the frame (a coroutine resumed after Update
sees the agent moved and the hull not): tracks into the ground at most 162 cm (more than 5 cm in
92% of frames) -> 5 cm (0%), roll rate 7.9 -> 4.2 deg/s, roll reversals 0.68 -> 0.15/s, max roll
25 -> 16 degrees, heading reversals from about one a second to 0.05.
**The Commander plays against the Mech it has seen**: `Perception` reads a visible enemy Mech's
guns (`Snapshot.eMechAntiLight`, `eMechDesignKnown`, `eMechSeenAgo`); `Plan` leans the Trooper
share against it (armour against an anti-infantry Mech, rifles against an anti-armour one);
`MechAway` (seen within 15 s, over 70 m from their base) opens a window wave at the plan's
Mech-less threshold, weighted to their base and bay; and a wave stages on its own Mech when it is
fit (`stageWithMech`, waiting for it at the staging point). `Dbg.mechWindows/stagedWithMech`
count them on the evaluation line.
**A bay can go up anywhere a Digger can build**, the enemy's base included. `BayEdgeCases`
(Editor/MechTrials.cs; `Only` picks one case) raises team 0's bay 12 m inside the enemy base, 45 m
out, in a corner, on a lake shore, in a wood and far out on a flank (map seed 1000, 420 s at 8x)
and reports the bay's and the Mech's fate, the Mech's time by reason, repair trips (a new docking
after 10 s away from the gantry) and, for a Mech that fell, its last intents with what had it in
reach. What it found and what was changed: the Commander ignored a bay in its base (home defence
counted units only) -- it now counts an enemy Mech Bay (400) or Sentinel (250) within 55 m of its
base, but only with an army worth `max(450, pushThreshold / 2)`, because without that gate it fed
Troopers to the bay's tower one at a time and lost the match. MechBrain's `HomeThreat` counts an
enemy bay (600) or tower (300) beside its structures. A Mech mends only at a bay on its own half
or one nothing out-guns it at (`BaySafe`, which counts everything whose reach covers the gantry,
not a fixed 32 m -- it sat in the gantry at 5-13% while their Mech shot it from 45 m, and died
there in 3 of 5 lake-shore runs); losing hull for 6 s in a forward bay's gantry sends it away for
20-120 s (`bayShunUntil`, doubling); hurt with no bay to go to it falls back to its own lines
(`Guard(null)`), and it guards its base, not a forward bay.
Cases 6 and 7 walk a Digger in at 90 s and place the bay when it arrives, as a player would (a
site goes up the moment it is ordered, so one ordered from afar is shot down first). A site with
no work done on it does not use up the side's bay (`MechBookkeeping`, `buildProgress <= 0`), like
one called off. `Remembered.unfinished` lets the Commander answer a site going up by its base
with whatever army it has (`site`; the gate above is for finished guns): 45 m out, its Digger died
15-16 s after the order and it was never finished, against 19-39 s and finished once in three
with `Commander.IgnoreSites` (the A/B switch; the runners reset it). The bay's gun (1,800 hp,
22 damage every 0.6 s, 24 m) kills a Digger in two shots, so `RunMacro` step 5b moves Diggers off
ore an enemy gun covers (`guns`, `SafeOre`: node, its Foundry and the way between out of reach)
and expansions avoid it; with no safe ore they stand clear, unless the gun covers the main
Foundry, when they mine on at a loss -- stood clear there, the army never grew and the base fell.
Every Mech weapon carries three damage multipliers (`WeaponPart.vsLight/vsHeavy/vsStructure`,
`Projectile.ClassMul`); **the projectile pool is shared**, so `GameWorld.Fire` resets them to 1 --
before it did, a rifle round reused from a Mech's shell kept its 0.5x against infantry.
Mech values are kept out of `Snapshot.armyValue/eArmyValue` and the player-style read
(`mechAlive`, `eMech`, `eMechBay` instead). Presentation: `View/MechView.cs` assembles the
Mech from `Resources/MechKit` (per-part prefabs and points, `Editor/MechKitBuilder.cs`, run
by Build step 2 from `Art/Models/mechs.json`), walks it by two-bone IK with planted feet,
raises `MechView.Footfall`, and hands it to `View/MechWreck.cs` on death. Its springs must hold at
any frame length -- the evaluation runs the game at 8x and more: the banner's, stepped once a frame,
went to NaN in one long frame and logged an error every frame after, which stalled a Full evaluation
for hours (it now steps 20 ms at a time and resets on NaN);
`View/FXDirector.Mech.cs` has its effects, `AudioDirector` its sounds (`make_audio.py --mech`
builds only those). Models are drawn `MechCore.ModelScale` (1.2) larger than the kit; the
sim's muzzle points follow. Balance is measured, not guessed: `MechTrials`/`MechDuel`
(Editor/MechTrials.cs) fights a Mech against N Maulers or Troopers on a flat corridor of map
seed 1000 and bisects for the break-even N (targets: 20-25 Maulers, 30-40 Troopers);
`MechTrials.Watch` logs an AI-vs-AI match's bays, Mechs, upgrades and advice;
`MechShowcase.Run` and `MechCombatLook.Run` photograph them (`Temp/mech*.png`).
Trials silence the opponent AI with `Commander.Suspended`, a static: with domain reload off it
survives into the next play session, and a quick evaluation after a trial that left it on lost
3 of 4 to an AI that never moved. `AIEvalRunner` and `BenchmarkRunner` now clear it; a one-off
`Unity_RunCommand` trial must too.

**The Editor stops ticking when the display sleeps.** Its play loop runs off the display
refresh, so an evaluation or trial left running with the screen off stalls (a replay managed
69 s of play in 40 minutes) and resumes the moment something pokes the Editor. Run
`caffeinate -d -i -t 7200` in the background for long runs. It also crawls when it is not the
frontmost app (App Nap): after a benchmark or gallery run the player had taken the foreground and a
quick evaluation managed 18 s of play in ten minutes. MCP calls wake it for a moment (the file
inbox does not); `open -a /Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app` brings the running
Editor to the front (check `InternalEditorUtility.isApplicationActive`).

**Never edit a script while the Editor is in play mode.** Unity recompiles and reloads the
domain mid-play: every `[NonSerialized]` array is gone (GroundDeformer threw from every
`UnitView` each frame) and any running trial loses its state. Stop, edit, compile, play.

**Navigation areas.** Boulders (`World/Boulder.cs`) and the trunks of trees on walkable ground are left
out of the bake and stand on the `Rubble` area (`NavMeshModifierVolume`s; the trees' all sit on
`Map/Vegetation`); every agent except the Mauler excludes it. `GameWorld` crushes a rock and
`Vegetation` fells a tree when a Mauler reaches it or a blast does, then `GameWorld.RequestNavRebuild`
rebuilds the NavMesh tiles with `NavMeshSurface.UpdateNavMesh`.
`GameWorld.Awake` swaps in a copy of the baked data first, so a match never edits the asset. **A rebuild
takes the path from every agent whose way crossed a changed tile** (it comes back `PathInvalid`, no
path, nothing pending), and a Mauler fells its own trees: never read "no path" as arrival.
`Unit.KeepGoing` re-requests it and ends a move order only at the goal, at the end of a partial path or
after four empty answers; before it did, orders were dropped mid-journey and units stood until clicked
again (the "units stop reacting to clicks" bug). `GameWorld.Awake` sets
`NavMesh.pathfindingIterationsPerFrame` to 1000 (Unity's 100 let a rebuild restart long requests, so
Maulers in a wood waited up to 9 s for a path), and rebuilds start at most every 1.5 s
(`navRebuildNotBefore`). `AgentPlay.OrderTrial` sends a player army through woods and rocks for three
minutes and reports order latency, stands and drops with the agent's state; `AgentPlay.StallTrial` does
the same for an AI-vs-AI match (and counts path requests by unit and order). Water is
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

**Nested groups do not survive FBX export.** With `bake_space_transform` the FBX writer
mangles a grandchild's transform (the Digger's `Arm/Cutter` came into Unity turned 270
degrees and metres out of place). `export_fbx.export_groups` therefore writes every group as
a direct child of the root (`Arm/Cutter` becomes `Cutter` beside `Arm`), and the game chains
them again, each keeping where it stands (`UnitView.Bind` for the cutter, `MechView.RigLegs`
for thigh > shin > foot). `Tools/blender/build_mechs.py` has its own entry point and exports
only `SF_MECH_*` and `mechs.json`, so the other FBXs are not rewritten.

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
- Plants are Poly Haven scans baked by `Tools/blender/bake_scanned_flora.py -- <KIND>` (Cycles on the
  CPU, 1-6 min a kind; fetch first with `python3 Tools/fetch_assets.py island_tree_01 ...`): the trunk
  decimated with its own UVs and bark photograph (`Leaves/scan_<KIND>_bark.jpg`, SF_Tree `_BarkStyle` 2,
  `PlantKind.barkTex`), the leaves and twigs k-means-clustered, each cluster rendered orthographically
  from out of the crown and up into a tile of `scan_<KIND>_col.png` / `_nrm.png` (normals in the card's
  frame). A card shows *all* the foliage within a sphere round its cluster (`SPHERE`), with a ragged rim
  (each leaf island gets its own reach), so neighbouring cards overlap like sprays: showing only its own
  cluster, a card covered 15-19% of its tile and the crowns read as burnt. Leaf coverage is grown by a
  texel (`GROW`) against the 0.45 cut-off. The layout goes to `Tools/blender/scanned/<KIND>.json`
  (committed, game space) and `scanned_flora.py` builds the model from it inside `build_flora`'s kind
  builders; the procedural builders remain the fallback for a kind with no bake. Cards bring their own
  occlusion by crown depth (`sf_ao`), which `export_fbx.bake_ao` keeps: ray-cast against a hundred
  overlapping quads it blackened the crowns. Wind and blast pressure need nothing new, because the vertex
  colour contract is the procedural one (B the sway weight, A 1 on cards, G random per card). Foliage is
  two-sided (`_Cull` 0); far copies (`LOD_BUILDERS`) keep every card and thin the trunk. The export
  rewrites every FBX byte-for-byte differently; restore the ones you did not mean to change.
  `AgentPlay.TreeGallery` photographs the most isolated plant of each kind at the closest zoom, and the
  same plant in a held pressure front (`Temp/tree_<KIND>[_pushed].png`, 3x resolution).
- Wind (`World/Wind.cs`): one heading, one strength and one gust rhythm for the whole match, drawn
  from the map seed in `MapGenerator.Generate` and evaluated as a pure function of the match clock, so
  it needs no state and a seed replays identically. Everything that should agree reads it: fire spreads
  downwind and faster in a gust, smoke and embers lean with it, a falling tree leans with it, leaves
  tear off the crowns when it blows hard (`FXDirector.WindBlown`), the cloud shadows drift with it, and
  `Atmosphere` uploads it as `_SF_Wind` (xy heading, z strength, w gust phase) for SF_Tree and SF_Grass.
  Both shaders fall back to a steady breeze when that global is zero, so nothing stands frozen in the
  scene view. Sway amplitude is per material (`_WindStrength`) times that strength. The clouds are the
  exception to the gusts: they drift with `Wind.Weather` (the day's strength), steadily, at
  `Atmosphere.cloudDrift` (3.2 m/s) on the windiest day. Driven by `Wind.Speed` at 14 m/s they surged
  and stalled with every gust and raced across the map ("wind is too fast"). The gust phase advances at
  `0.45 + 0.95 x speed` (it was `0.5 + 1.4 x`, which thrashed on a windy day).
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
  takes longer than a short one (measured 1.5-1.7 s when a blast throws it). `Fell(..., push)` is the
  shove in radians a second (blast 0.5-1.3 by distance, a burned-through snag 0.05, which then goes
  downwind). It rests on its own boughs (`Live.rest`), and bounces once or twice before settling; the
  rebound is capped at 0.45 rad/s (proportional to the impact, a tree flung down at 6 rad/s sprang back
  45 degrees and fell again).
- **A Mauler shoulders a tree over** (`Vegetation.CrushUnderMaulers`), a kinematic contact rather than a
  shove: when the trunk meets the hull's nose box (`HullFront` 2.2, `HullSide` 1.6 m, from the model's
  track runs) it is `Fell` with no push and `Live.pushedBy` the tank, and while it is held there its lean
  is at least `atan(past / GlacisHeight)` -- `past` being how far the nose has come beyond the trunk's
  foot -- so the trunk never enters the hull; gravity takes over and it falls away ahead. The blow
  multiplies `agent.velocity` by 0.3-0.7 by trunk radius, and `Unit.pushLoad` (reset per tick, the roots'
  resistance) cuts `Unit.UpdateFacing`'s pace by up to 75%, works the engine (`FXDirector.MaulerEffects`)
  and lifts the hull's nose (`UnitView`). Slowed only through its top speed, the tank lost nothing before
  the tree was over and flung it down at full tilt. A trunk lying under the hull's footprint is pressed
  in (`Live.crush`, sinking the pose by up to 0.9 trunk radii) and raises `GameEventKind.PlantCrushed`
  (splinters, `bank.crush`). `AgentPlay.PushLook` drives one into a lone blocking tree across the camera's
  view and prints the lean against the hull's position, the tank's speed and the closest the trunk came
  to the glacis (measured: 4.1 m/s down to 1.4-2.0, down 1.4-1.8 s after contact, gap never below
  0.27 m), photographing it (`Temp/push_*.png`). `LodgeCheck` hangs it up in a neighbour's crown if one stands
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
- **The grove check's grid must agree with the NavMesh**, or it turns every grove away: on 17 of 40
  seeds it did (291-395 "groves dropped", 38 blocking trees, all of them lone trees and snags), because
  it had lost a target before the first tree. `Blockage` is a 1 m grid of where a unit's *centre* may
  go: terrain from `WalkableGround` (the bake's own rules on the real heights -- no heightmap triangle
  within the agent's radius steeper than its slope, not deep water), with no margin of its own, and
  scenery, boulders and trees as padded discs/stamps that must keep a cell clear round every cell a
  unit passes. Do not go back to `HeightfieldGenerator.PassableCell`: its 2 m corner-spread rule allows
  only 29 degrees on a diagonal and walled base B's plateau rim off where units drive over it. Where the
  NavMesh threads a pass too narrow for 1 m cells, the NavMesh is baked once without trees and its path
  is written into the grid as a `Link` that trees must keep clear of (logged as "narrow passes kept
  clear"). That is now a fallback (0 of GroveBatch's 40 seeds): the 3 that needed it were a crack in the
  terrain (next bullet) or a boulder in a pass mouth. A target within
  `ReachSlack` (3 m, a Digger's harvest reach) of a reached cell counts; a 7 m square counted ore at the
  foot of a slope as reached from the terrace above. Expansions are placed only where both sides and all
  twelve ore fields are reached from a base (a quarter of maps had one in a basin nobody could enter);
  anything still unreachable is left out of the check with a warning. `AgentPlay.GroveBatch` regenerates
  40 seeds in play mode and reports drops, blocking trees, links, unreachable places and expansions
  (measured: 3 seeds dropped a grove, 4 in all; blocking 127-206, mean 168; seed 1000 unchanged at 867
  plants, 159 blocking). `AgentPlay.GroveGrid` then `GroveGridReport` draws one seed's grid against the
  NavMesh with its path to base B (`Temp/grove_grid_<seed>.png`).
- **The bases are joined on the ground units get, not on the generator's cells.** `HeightfieldGenerator`
  tests A-B on its 2 m `PassableCell` grid (corner spread <= 1.6 m), but the terrain is the Catmull-Rom
  surface through those corners (`SmoothHeightAt`), which climbs a single terrace step about half again
  as steeply as its average, past the agent's 40 degrees; seeds 986964719 and 2090223571 passed the
  generator's test while the NavMesh joined the bases only through a crack 0-0.4 m wide. So
  `MapGenerator.Generate` floods `WalkableGround` from base A (terrain only, the grove check's grid) and,
  while base B is not reached, calls `gen.Reconnect` (another `CarveCorridor`, 4.5 cells wide rather than
  the original's 3.2, which left the narrowest point of the route 3 cells across; on 986964719 the ramp
  is now 10-20 m wide on the NavMesh, 5.7-14 m at 3.2) and rebuilds the
  heights (`Result.corridors`, logged). `ComputePassability`'s `pass` is untouched, so a seed that is not
  carved lays out exactly as before. Boulders must not close a pass either (1217350130: a rock in the
  mouth of a 7 m pass left 1.2-2.3 m beside a 40-degree slope): one that cuts the grid's way from base A
  to anything reached before it -- base B, an expansion, an ore field -- is placed elsewhere.
  `Blockage.MayCut` decides most rocks locally (the rim of a square round the disc still joins up) and
  keeps the reach map exact without a flood (checked cell for cell against a fresh flood after every
  boulder on 40 seeds); 0-2 floods a map. On any seed whose grid already reached everything, no rock is
  moved. Measured on GroveBatch's 40 seeds: 37 identical (seed 1000 included), 2 carved, 1 rock moved,
  0 links.
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
- **Unit deaths** are `View/UnitWreck`: on the frame a mobile unit dies, `UnitView` hands its `body` (and
  the Mauler's turret or the Trooper's `Gun`, its legs) to a new wreck object and does nothing more, so
  `GameWorld` still destroys the unit after 1.3 s and the simulation is unchanged. The wreck is rigid
  bodies on the terrain collider (which follows the craters): a box from the mesh bounds (readable even
  when the mesh data is not), centre of mass low, raised out of the ground if the NavMesh had it
  buried, then frozen kinematic once still (or at 7 s) and sunk. It falls the way `Unit.killDir` points
  and as hard as `killForce` (set by `GameWorld.Kill`: away from a shell's burst when `Damage(..., splash)`,
  else from the shooter; the fallback direction comes from the unit id, never the match's random
  stream, or a seeded match would shift). A Trooper's topple is given about his feet (linear velocity
  `fall x spin x hips`): spun about his middle, his feet dug in and he stopped at 25 degrees. Machines
  char through `_Damage` (0.9 falling to 0.6 so the glowing cracks go out) with a low `_Burn` (0.32 down
  to 0): at the debris' full `_Burn` a hull glowed like lava and bloomed into a yellow lump.
  `FXDirector.Wrecks` burns and smokes them by `Heat` and dusts a skidding Skimmer; `TrooperDown`
  replaces the fireball a rifleman used to die in (AudioDirector and GroundMask skip the boom and the
  scorch for him too). Only what the player saw die leaves a wreck; past 36 the oldest sink early.
  `AgentPlay.DeathLook` kills one of each on the flattest ground near the base and photographs the
  deaths (`Temp/death_*.png`, 2x) with each wreck's tilt, travel and heat.
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
  The odds scale with `0.38 / trunkRadius` (thicker stands), so a kind's `trunkRadius` is gameplay as
  well as the blocking ring: with the scanned trunks (broadleaf 0.48 m, jacaranda 0.40) it measured
  66/45/21/6%, 27% overall, on a generated map.
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
  Loudness is relative: "the rifle has no sound" was a shot playing at 0.15 under music at 0.54, not a
  missing clip. `AgentPlay.RifleCheck`/`RifleCheckReport` stage a fire-fight and report every shot's
  source (started, audible, virtualised) before anything is changed. `PlayAt` takes a free voice, or
  the one nearest its end (`FreeVoice`), never simply the next in turn, and idle tank loops are paused.
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
