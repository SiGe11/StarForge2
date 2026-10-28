// BugTrials.cs — staged checks for fixes to the rules, run in play mode:
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Targeting   (then TargetingReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Harvest     (then HarvestReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.RoadGun     (then RoadGunReport; the opponent AI must run)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.HudAlloc    (then HudAllocReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.HoldPause   (then HoldPauseReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Deserters   (then DesertersReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.MineSound   (then MineSoundReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.PunishSeeds (answers at once)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.PurgeEnd    (then PurgeEndReport; ends the match)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.DeserterJam (then DeserterJamReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.CampDrift   (then CampDriftReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.PlaceTwice  (answers at once)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.EvalAbortState (answers at once, in or out of play)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.MinedOut    (then MinedOutReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.HardpointFit (answers at once)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.ProngHome   (then ProngHomeReport; may end the match)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.BayPing     (then BayPingReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.MechWreckMat (then MechWreckMatReport; run in two play sessions)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.CardAlloc   (answers at once)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.PenJam      (then PenJamReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.FleeTrace   (then FleeTraceReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Fuzz        (then FuzzReport; FuzzNextSeed for another run)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.LongMatch   (then LongMatchReport; LongMatchNextMap for another map; reloads the scene)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.HiddenLast  (then HiddenLastReport; ends the match)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.HiddenLastFar (then HiddenLastReport; ends the match)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.PauseSpeed  (answers at once)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Cards       (answers at once)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Quality     (answers at once; title screen)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Sight       (then SightReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Statics     (answers at once, in or out of play)
// Targeting and Harvest silence the opponent AI (Commander.Suspended) and run at 3x while
// they run, through a hold count shared by the trials here: saved on the first HoldStatics,
// put back on the last ReleaseStatics -- OnDestroy included, because Unity skips a
// coroutine's finally when its object goes (play stopped, a trial staged again). (The
// names are their own: MoraleTrials derives from BugTrial with a Hold/Release/Listen of
// its own, and its own OnDestroy, which Unity calls instead of this one.) Unit.StickyTargets,
// the Targeting A/B, is also cleared on every entry into play mode: these statics survive
// play sessions (domain reload off), and left on it would undo the fix in every later
// match, evaluations included. Each leaves the match running.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.AI;
using StarForge.Game;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class BugTrials
    {
        /// <summary>A Sentinel and a holding Trooper of team 1 pick up a Skimmer of team 0
        /// parked in their sight but out of their reach; then a Trooper of team 0 walks up
        /// and shoots the tower. Run twice: auto-acquired targets kept until they die (as
        /// before) and let go for an enemy in reach. Reports what each fired at.</summary>
        public static string Targeting() => Stage<TargetingTrial>("Targeting");
        public static string TargetingReport() => Report<TargetingTrial>();

        /// <summary>Team 0's Foundry is destroyed with its Diggers at work: how much ore they
        /// take from the nodes with nowhere to bring it, and whether they bring it in once a
        /// Foundry stands again.</summary>
        public static string Harvest() => Stage<HarvestTrial>("Harvest");
        public static string HarvestReport() => Report<HarvestTrial>();

        /// <summary>The AI's last Foundry falls with a Digger's load on the road and a tower of
        /// ours in reach of it but not of its seam: whether the AI moves it (step 5b judged a
        /// Digger by its seam, so one stopped on the road under a gun stood there). Needs the
        /// opponent AI running; the Digger is made hard to kill so the answer is the order.</summary>
        public static string RoadGun() => Stage<RoadGunTrial>("RoadGun");
        public static string RoadGunReport() => Report<RoadGunTrial>();

        /// <summary>Managed memory the HUD's Update allocates a call with nothing selected, a
        /// Digger, an idle Foundry and one with a queue: the selection panel rebuilt its queue
        /// row and formatted its text every frame.</summary>
        public static string HudAlloc() => Stage<HudAllocTrial>("HudAlloc");
        public static string HudAllocReport() => Report<HudAllocTrial>();

        /// <summary>The trials' hold puts back the speed the match's state calls for: paused during
        /// a hold, the match stays paused; resumed during one, it runs (a saved speed froze it).</summary>
        public static string HoldPause() => Stage<HoldPauseTrial>("HoldPause");
        public static string HoldPauseReport() => Report<HoldPauseTrial>();

        /// <summary>Deserters, four ways: theirs in sight of the AI (Perception should not remember
        /// them as army); ours taken off outright (the morale prune should mark them gone); four
        /// breaking from the middle of a knot of 24 at 8x (where they settle); and one settled
        /// whose spot is made unreachable (it should take a new spot, not re-ask all match).</summary>
        public static string Deserters() => Stage<DesertersTrial>("Deserters");
        public static string DesertersReport() => Report<DesertersTrial>();

        /// <summary>The mining sound while our Diggers mine with an empty load, then with a part
        /// load kept in their packs: the share of frames a mining take is playing.</summary>
        public static string MineSound() => Stage<MineSoundTrial>("MineSound");
        public static string MineSoundReport() => Report<MineSoundTrial>();

        /// <summary>MoraleTrials.Punish staged, and the match surrendered once the purge is on:
        /// whether the purge and the pursuit stop with the match. Ends the match: restart play after.</summary>
        public static string PurgeEnd() => Stage<PurgeEndTrial>("PurgeEnd");
        public static string PurgeEndReport() => Report<PurgeEndTrial>();

        /// <summary>The first verdict roll on deserters of each side's Mech, for the evaluation's five
        /// seeds against each opponent: from the seed alone (before) and reseeded per opponent as
        /// AIEvalRunner now does (MechBrain.ReseedPunish; the mix is AIEvalRunner's, repeated here).</summary>
        public static string PunishSeeds()
        {
            if (!EditorApplication.isPlaying || GameBootstrap.Instance == null) return "not playing";
            var brains = GameBootstrap.Instance.MechBrains;
            var field = typeof(MechBrain).GetField("punishRng", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var reseed = typeof(MechBrain).GetMethod("ReseedPunish");
            if (field == null || brains[1] == null) return "no MechBrain or no punishRng";
            var sb = new StringBuilder();
            for (int t = 0; t < 2; t++)
            {
                sb.AppendLine($"team {t} (go roll; vs Rusher, Macro, Turtle, Harasser):");
                for (int g = 0; g < 5; g++)
                {
                    uint seed = 1000u + 37u * (uint)g;
                    var old = new Rng(seed ^ 0x9D1E5u ^ (uint)(t * 31337));
                    old.Range(4f, 9f);
                    var rolls = new List<string>();
                    for (uint k = 0; k < 4 && reseed != null; k++)
                    {
                        reseed.Invoke(brains[t], new object[] { seed ^ (k + 1u) * 0x85EBCA6Bu });
                        var r = (Rng)field.GetValue(brains[t]);
                        r.Range(4f, 9f);
                        rolls.Add(r.F01().ToString("0.00"));
                    }
                    sb.AppendLine($"  seed {seed}: from the seed alone {old.F01():0.00} against all four; reseeded per opponent {(reseed == null ? "(no ReseedPunish)" : string.Join(" ", rolls))}");
                }
                reseed?.Invoke(brains[t], new object[] { MatchSettings.seed });
            }
            return sb.ToString();
        }

        /// <summary>One soldier held still on a good path (its agent given no acceleration) far from the camp
        /// deserts, at 8x: it should settle where it is after its retries, not flee for ever.</summary>
        public static string DeserterJam() => Stage<DeserterJamTrial>("DeserterJam");
        public static string DeserterJamReport() => Report<DeserterJamTrial>();

        /// <summary>Three deserters settled in the camp, then raided every 4 s for 60 s (an enemy
        /// rifleman 22 m out hits one, and is taken off 1.5 s later): how far their spots drift.</summary>
        public static string CampDrift() => Stage<CampDriftTrial>("CampDrift");
        public static string CampDriftReport() => Report<CampDriftTrial>();

        /// <summary>A Bunkhouse site raised and, in the same frame, CanPlace asked about the same spot
        /// and one half a footprint over: both should be refused (the spatial hash is built once a
        /// tick, so a site raised this tick was missed and two AI sites in one pass could overlap).</summary>
        public static string PlaceTwice()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var w = GameWorld.Instance;
            if (w == null || !w.running) return "no match running";
            Vector2 home = w.Map.StartPos(0), p = home;
            bool found = false;
            for (int k = 0; k < 80 && !found; k++)
            {
                float a = k * 0.7f, r = 14f + (k % 5) * 4f;
                p = home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                found = w.CanPlace(UnitType.Bunkhouse, p);
            }
            if (!found) return "no spot for a Bunkhouse near our base";
            var site = w.Spawn(UnitType.Bunkhouse, 0, p, false);
            float rad = site.def.radius;
            bool same = w.CanPlace(UnitType.Bunkhouse, p);
            bool half = w.CanPlace(UnitType.Bunkhouse, p + new Vector2(rad, 0f));
            site.BeginDeath();
            return $"site raised at {(p - home).magnitude:0} m; same frame, CanPlace on the same spot: {same}, half a footprint over: {half} (want False, False)";
        }

        /// <summary>The player's last structure hidden away from every ore field and base site, the
        /// rest of the side gone, and the AI (running) given an army: whether it finds it and the
        /// match ends. Ends the match: restart play after.</summary>
        public static string HiddenLast() { HiddenLastTrial.FarFromAI = false; return Stage<HiddenLastTrial>("HiddenLast"); }
        /// <summary>The same, hidden as far from the AI's base as it can be while 40 m or more from every ore field and base site.</summary>
        public static string HiddenLastFar() { HiddenLastTrial.FarFromAI = true; return Stage<HiddenLastTrial>("HiddenLast"); }
        public static string HiddenLastReport() => Report<HiddenLastTrial>();

        /// <summary>Whether an evaluation is pending or running and the match settings as they stand:
        /// for checking that one stopped part-way does not take over the next Play.</summary>
        public static string EvalAbortState()
        {
            var boot = GameBootstrap.Instance;
            var runner = Object.FindAnyObjectByType<AIEvalRunner>();
            return $"playing={EditorApplication.isPlaying} state={(boot != null ? boot.State.ToString() : "-")} timeScale={Time.timeScale:0.#} " +
                   $"evalPending={AIEvalRunner.Pending} runnerOn={(runner != null && runner.enabled)} fixedSeed={MatchSettings.fixedSeed} " +
                   $"aiMemory={MatchSettings.aiMemory} difficulty={MatchSettings.difficulty}";
        }

        /// <summary>Every ore node gone while our Diggers mine with part loads: they should bring the
        /// loads in, not stand idle holding them.</summary>
        public static string MinedOut() => Stage<MinedOutTrial>("MinedOut");
        public static string MinedOutReport() => Report<MinedOutTrial>();

        /// <summary>The Mech fitted out (MechArmoury.Compose at the drop call) while an Extra Hardpoint
        /// is still being researched, on 40 pilots: how many designs come out with no spare mount
        /// for it (its ore spent on nothing). Team 0's state is put back after.</summary>
        public static string HardpointFit()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var w = GameWorld.Instance;
            if (w == null || !w.running) return "no match running";
            var fit = typeof(GameWorld).GetMethod("FitOutMech", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (fit == null) return "no FitOutMech";
            var F = w.factions[0];
            var design = F.design; bool chosen = F.designChosen; int researching = F.researching;
            int lvl = F.upgrades[(int)MechUpgrade.Hardpoint];
            string armoury = F.armouryNote;
            int none = 0, n = 0;
            for (uint k = 0; k < 40; k++)
            {
                F.design = MechParts.Generate(7919u * (k + 1) ^ 0x51ED27u);
                F.designChosen = false;
                F.researching = (int)MechUpgrade.Hardpoint;
                F.upgrades[(int)MechUpgrade.Hardpoint] = 0;
                fit.Invoke(w, new object[] { 0 });
                n++;
                if (F.design.reserve.Count == 0) none++;
            }
            F.design = design; F.designChosen = chosen; F.researching = researching;
            F.upgrades[(int)MechUpgrade.Hardpoint] = lvl; F.armouryNote = armoury;
            return $"fitted out with a hardpoint being researched, {n} pilots: {none} designs with no spare mount for it (want 0)";
        }

        /// <summary>The AI (running) splits a prong off a wave; then a raid on its base calls the army
        /// home: does the prong come home too? (Ordered before it was folded back in, it walked on.)
        /// May end the match: restart play after.</summary>
        public static string ProngHome() => Stage<ProngHomeTrial>("ProngHome");
        public static string ProngHomeReport() => Report<ProngHomeTrial>();

        /// <summary>Our Mech on the field; an enemy Mech Bay destroyed, then one of ours: how far each
        /// comms ping lands from its bay (the position went over as (x, height) and pinged the map's edge).</summary>
        public static string BayPing() => Stage<BayPingTrial>("BayPing");
        public static string BayPingReport() => Report<BayPingTrial>();

        /// <summary>A Mech of ours destroyed in sight, and the physics material on its wreck's box:
        /// run it in two play sessions running (the static material outlived the first session's native one).</summary>
        public static string MechWreckMat() => Stage<MechWreckMatTrial>("MechWreckMat");
        public static string MechWreckMatReport() => Report<MechWreckMatTrial>();

        /// <summary>Managed memory PlayerController.Update allocates a call with a Digger and the Foundry
        /// selected and no key down (it built the whole command card every frame to match hotkeys):
        /// 200 calls in one frame, Mono heap across them.</summary>
        public static string CardAlloc()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var w = GameWorld.Instance;
            var pc = Object.FindAnyObjectByType<PlayerController>();
            if (w == null || !w.running || pc == null) return "no match running";
            Unit digger = null, foundry = null;
            foreach (var u in w.units)
            {
                if (!Unit.Live(u) || u.team != pc.team) continue;
                if (digger == null && u.Type == UnitType.Worker) digger = u;
                if (foundry == null && u.Type == UnitType.Foundry) foundry = u;
            }
            pc.Selection.Clear();
            if (digger != null) pc.Selection.Add(digger);
            if (foundry != null) pc.Selection.Add(foundry);
            var update = typeof(PlayerController).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            update.Invoke(pc, null);
            long before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < 200; i++) update.Invoke(pc, null);
            long d = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - before;
            pc.Selection.Clear();
            return d < 0 ? "a collection ran during the measure: run it again" : $"PlayerController.Update with a Digger and the Foundry selected, no key down: {d / 200f:0} B a call (200 calls)";
        }

        /// <summary>A genuine jam: a soldier in a pen of carving walls whose one 3 m gap is filled by a
        /// holding Mauler (3 m across; a Trooper gives way to a Mauler), far from the camp, deserts:
        /// its path runs out through the gap, its body cannot. It should settle in the pen, not flee for ever.</summary>
        public static string PenJam() => Stage<PenJamTrial>("PenJam");
        public static string PenJamReport() => Report<PenJamTrial>();

        /// <summary>Deserters' flights from three fields at 8x, the second cut off mid-flight by a 40 m
        /// carving wall across its way (a detour): share of ticks the agent's way left reads Infinity, the
        /// largest stall a deserter built while free to walk, and where each ended (in the camp or not).</summary>
        public static string FleeTrace() => Stage<FleeTraceTrial>("FleeTrace");
        public static string FleeTraceReport() => Report<FleeTraceTrial>();

        /// <summary>Edge-case fuzz: five game minutes at 8x with both AIs playing, every public command
        /// fired with random and hostile input -- dead, dying, enemy, neutral and deserted units in the
        /// selection, points off the map and in the water, builds anywhere, trains and cancels on any
        /// building, Mech orders and upgrades, desertions, big damage -- while every exception is caught
        /// and invariants are read each second (ore and supply never negative, no NaN position, no unit
        /// off the NavMesh for long). FuzzSeed picks the run, so a failure replays.</summary>
        public static string Fuzz() => Stage<FuzzTrial>("Fuzz");
        public static string FuzzReport() => Report<FuzzTrial>();
        public static string FuzzNextSeed() { FuzzTrial.Seed++; return $"fuzz seed now {FuzzTrial.Seed}"; }
        /// <summary>The fuzz with the match's lifecycle thrown in: pause and resume, speed changes, and a
        /// surrender at 200 s with the commands going on behind the end screen. Ends the match.</summary>
        public static string FuzzLife() { FuzzTrial.Lifecycle = true; return Stage<FuzzTrial>("Fuzz"); }

        /// <summary>Meshes under the Map's NavMeshSurface that a runtime rebuild would read but cannot in a
        /// player (no Read/Write): every MeshFilter not under an active, enabled NavMeshModifier with
        /// ignoreFromBuild, whose mesh is not readable, grouped by mesh name and owner.</summary>
        public static string NavSources()
        {
            var surface = Object.FindAnyObjectByType<Unity.AI.Navigation.NavMeshSurface>();
            if (surface == null) return "no NavMeshSurface";
            var found = new Dictionary<string, int>();
            int total = 0, ignored = 0;
            foreach (var mf in surface.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null) continue;
                total++;
                bool skip = false;
                for (var t = mf.transform; t != null && !skip; t = t.parent)
                    foreach (var m in t.GetComponents<Unity.AI.Navigation.NavMeshModifier>())
                        if (m.isActiveAndEnabled && m.ignoreFromBuild && (t == mf.transform || m.applyToChildren)) { skip = true; break; }
                if (skip) { ignored++; continue; }
                if (mf.sharedMesh.isReadable) continue;
                var owner = mf.GetComponentInParent<Unit>();
                string key = $"{mf.sharedMesh.name} on {mf.gameObject.name}" + (owner != null ? $" (unit {owner.def?.type}, dying {owner.dying}, modifiers {owner.GetComponents<Unity.AI.Navigation.NavMeshModifier>().Length}, enabled {(owner.GetComponent<Unity.AI.Navigation.NavMeshModifier>() is var mm && mm != null ? mm.enabled.ToString() : "-")})" : "");
                found.TryGetValue(key, out int c); found[key] = c + 1;
            }
            var lines = new List<string>();
            foreach (var kv in found) lines.Add($"{kv.Value} x {kv.Key}");
            return $"{total} meshes under the Map, {ignored} ignored by a modifier; unreadable and collected: {(lines.Count == 0 ? "none" : string.Join("; ", lines))}";
        }

        /// <summary>Forces what a runtime NavMesh rebuild now and then did on its own: one ore node's
        /// ignore modifier switched off, a rebuild asked for, and the "does not allow read access"
        /// errors counted (a player cannot read an unreadable mesh; the Editor can, so the two differed).</summary>
        public static string OreSourceRead() => Stage<OreSourceReadTrial>("OreSourceRead");
        public static string OreSourceReadReport() => Report<OreSourceReadTrial>();
        /// <summary>Reimports the ore model under the current import rules (it is kept readable).</summary>
        public static string ReimportOreModel()
        {
            if (EditorApplication.isPlaying) return "stop play first";
            const string path = "Assets/StarForge/Art/Models/SF_ORE.fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            return $"reimported {path}: mesh {(mesh != null ? mesh.name + (mesh.isReadable ? " readable" : " NOT readable") : "not found")}";
        }

        /// <summary>A long AI-vs-AI match (spectated: a Commander on each side) on the map of
        /// LongMatchMapSeed, played at 8x to a winner or LongMatchMinutes of game time, watched each game
        /// minute: per side structures, Diggers, army, ore and supply; ore left on the map; deaths;
        /// projectiles; the AIs' memory sizes; the managed heap; the worst simulation step; exceptions.
        /// The evaluations stop at 15 minutes on one map, so the late game (ore running out, the supply
        /// ceiling, lists and memory growing) was never played. Reloads the scene; restart play after.</summary>
        public static string LongMatch()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            if (Object.FindAnyObjectByType<LongMatchTrial>() != null) return "a long match is already running";
            new GameObject("LongMatchTrial").AddComponent<LongMatchTrial>();
            return $"long match on map {LongMatchTrial.MapSeed} starting; LongMatchReport when it ends ([LongMatch] lines each game minute)";
        }
        public static string LongMatchReport() => LongMatchTrial.Last ?? "no long match run yet";
        public static string LongMatchNextMap() { LongMatchTrial.MapSeed = LongMatchTrial.MapSeed * 7919u + 104729u; return $"next map seed {LongMatchTrial.MapSeed}"; }
        /// <summary>The next LongMatch with every ore node cut to 15% at the start, so the map runs dry
        /// within minutes and the late game (no ore anywhere) is played.</summary>
        public static string LongMatchThinOre() { LongMatchTrial.ThinOre = true; return "next long match: ore cut to 15%"; }
        /// <summary>The next LongMatch replays the map and match seed of a stall the long matches found
        /// (map 1954835525: one side's Diggers wiped out twice in the late game, then both sides broke);
        /// frame timing differs at 8x, so it plays the same opening, not the same match.</summary>
        public static string LongMatchStallMap() { LongMatchTrial.MapSeed = 1954835525u; LongMatchTrial.FixedMatchSeed = 1020132137u; return "next long match: map 1954835525, match seed 1020132137"; }
        /// <summary>As LongMatchStallMap, the thin-ore stall (map 4096760149, ore cut to 15%).</summary>
        public static string LongMatchThinStallMap() { LongMatchTrial.MapSeed = 4096760149u; LongMatchTrial.FixedMatchSeed = 1742148745u; LongMatchTrial.ThinOre = true; return "next long match: map 4096760149 thin, match seed 1742148745"; }

        /// <summary>A dead economy on both sides: a spectated AI-vs-AI match, and at 60 s every Digger,
        /// Mech Bay and soldier of both sides gone and their ore set to 30 (a Digger costs 50); team 1
        /// then gets 8 riflemen, team 0 2. Can team 1, which can never grow, still attack? (In long
        /// matches two such sides stood 27 minutes, armies up, nobody moving.) Start it from the
        /// title screen (restart play first).</summary>
        public static string BrokeStall() { BrokeStallTrial.Tower = false; return Stage<BrokeStallTrial>("BrokeStall"); }
        /// <summary>As BrokeStall, with two Sentinels of ours beside our Foundry, so team 1's wave meets
        /// odds it retreats from: does it regroup (a wave every 25 s or slower), or walk back into the
        /// fight every 8 s?</summary>
        public static string BrokeStallTower() { BrokeStallTrial.Tower = true; return Stage<BrokeStallTrial>("BrokeStall"); }
        public static string BrokeStallReport() => Report<BrokeStallTrial>();

        /// <summary>The opponent AI (running) kept without an army -- every soldier it trains is removed
        /// at once -- against our base held by two Sentinels, from 60 s for four game minutes: how many of
        /// its Diggers came within 45 m of our base's middle and how many died there (it took a Digger as
        /// its scout whenever its picture of our base went stale, about 37 s after its last look, and it
        /// had no Skimmer and under six riflemen: 49 of one side's 80 Diggers in a long match died so).</summary>
        public static string DiggerScouts() => Stage<DiggerScoutTrial>("DiggerScouts");
        public static string DiggerScoutsReport() => Report<DiggerScoutTrial>();

        /// <summary>An enemy Mech standing in the AI's mining line, as ours does when it raids: at 240 s a
        /// Mech of ours (no AI drives it: it stands and shoots, and is kept from dying) is put halfway
        /// between the AI's main Foundry and the ore most of its Diggers mine, for two game minutes. How
        /// many Diggers does the AI lose and train meanwhile, and how many stand in the Mech's reach (in a
        /// 40-minute match one side trained Diggers into a Mech parked in its base, 111 lost, until it was
        /// broke).</summary>
        public static string MechInOre() => StageMechInOre(leave: false, walk: false, second: false, stand: 0f, outlying: false, tower: false);
        /// <summary>As MechInOre, but the Mech is taken away after 60 s: does the AI train Diggers again?</summary>
        public static string MechInOreLeave() => StageMechInOre(leave: true, walk: false, second: false, stand: 0f, outlying: false, tower: false);
        /// <summary>As MechInOreLeave, with a finished Foundry of the AI's raised by an ore field 45-100 m
        /// from its main one, out of the Mech's reach, before the Mech goes in: its Diggers are moved to that
        /// line; do they come home once the Mech has gone (step 5c)?</summary>
        public static string MechInOreLeave2() => StageMechInOre(leave: true, walk: false, second: true, stand: 0f, outlying: false, tower: false);
        /// <summary>As MechInOre, but the Mech is put 70 m out from the AI's main Foundry, on our side, and
        /// walks in to the same spot: how many Diggers die more than 3 s after the AI first had it in its
        /// memory (the only ones moving them could save; a Digger sees 22 m, a Foundry 32, the Mech reaches 34).</summary>
        public static string MechInOreWalk() => StageMechInOre(leave: false, walk: true, second: false, stand: 0f, outlying: false, tower: false);
        /// <summary>Our Mech stood 50 m from the AI's main Foundry (toward our base) for the two minutes,
        /// as one guarding a forward bay: past its reach of the seams, inside the 60 m the AI keeps away
        /// from at outlying lines. Reports idle Digger-seconds and ore mined, for its economy.</summary>
        public static string MechInOreStand() => StageMechInOre(leave: false, walk: false, second: false, stand: 50f, outlying: false, tower: false);
        /// <summary>A second Foundry of the AI's raised 45-100 m out, six of its Diggers sent to mine there, then our
        /// Mech put down by that field at 30% hull and left to die (no bay: our side is idle): the bayless,
        /// battered Mech that hunted outlying lines in the evaluation. Reports Diggers lost at that line, the
        /// most of the AI's army within 35 m of our Mech, and whether it was killed.</summary>
        public static string MechInOreOutlying() => StageMechInOre(leave: false, walk: false, second: true, stand: 0f, outlying: true, tower: false);
        /// <summary>The same with a Sentinel of ours (held standing) halfway between that line and the AI's main
        /// Foundry: Diggers fleeing our Mech must not be sent home through its reach. Reports Diggers it killed.</summary>
        public static string MechInOreOutlyingTower() => StageMechInOre(leave: false, walk: false, second: true, stand: 0f, outlying: true, tower: true);
        /// <summary>The outlying line again, with our Mech given to our side (its own MechBrain drives it; our side
        /// has no bay, so it is the bayless Mech that hunted on in the evaluation), held at 30% hull or under, 180 s.
        /// Reports Diggers lost anywhere and at that line, how far our Mech went, and whether it was killed.</summary>
        public static string MechInOreHunting() => StageMechInOre(leave: false, walk: false, second: true, stand: 0f, outlying: true, tower: false, hunting: true);
        /// <summary>The tower case with no safe ore anywhere (every node off the outlying line emptied once the
        /// Diggers are there), so 5b must clear them rather than move them: home, unless the way runs through the
        /// Sentinel. Pair with AIReview.WayHomeOld/New; reports Diggers lost in the Sentinel's reach.</summary>
        public static string MechInOreOutlyingTowerNoSafe() => StageMechInOre(leave: false, walk: false, second: true, stand: 0f, outlying: true, tower: true, noSafe: true);
        /// <summary>The plain outlying case with the AI's ore held at 2,500 or more through the window: whether its
        /// trains take step 5b's hands (5b runs after step 5 and stops at an empty budget or MaxPerPass).</summary>
        public static string MechInOreOutlyingRich() => StageMechInOre(leave: false, walk: false, second: true, stand: 0f, outlying: true, tower: false, rich: true);
        public static string MechInOreReport() => Report<MechInOreTrial>();
        /// <summary>Hunting at 80% hull: over the 40% at which the AI sends its hurt-Mech wave, as the bayless Mechs
        /// of the evaluation were at 23-91%. Reports what sent its army (waves started, hurt-Mech waves).</summary>
        public static string MechInOreHuntingWhole() => StageMechInOre(leave: false, walk: false, second: true, stand: 0f, outlying: true, tower: false, hunting: true, hull: 0.8f);

        /// <summary>Every MechInOre entry sets every switch (one left from a staging that never ran -- called out of
        /// play mode -- would otherwise carry into the next).</summary>
        static string StageMechInOre(bool leave, bool walk, bool second, float stand, bool outlying, bool tower,
                                     bool hunting = false, bool noSafe = false, bool rich = false, float hull = 0.3f)
        {
            MechInOreTrial.Leave = leave; MechInOreTrial.Walk = walk; MechInOreTrial.Second = second; MechInOreTrial.Stand = stand;
            MechInOreTrial.Outlying = outlying; MechInOreTrial.TowerOnWay = tower; MechInOreTrial.Hunting = hunting;
            MechInOreTrial.NoSafeOre = noSafe; MechInOreTrial.Rich = rich; MechInOreTrial.Hull = hull;
            return Stage<MechInOreTrial>("MechInOre");
        }

        /// <summary>The AI's first scouting trip, from the title screen (restart play first): a new match,
        /// our base kept standing (nobody plays our side), five game minutes at 8x. When does the AI first
        /// know our base and first remember our Foundry, how far off it believes our base is, and what
        /// becomes of the Digger that came nearest our base: how near, whether it came home and lived.
        /// (Stopped on the first building it saw, a Bunkhouse or tower out from the Foundry, the base was
        /// believed 10-26 m off.)</summary>
        public static string ScoutTrip() { ScoutTripTrial.Outer = false; return Stage<ScoutTripTrial>("ScoutTrip"); }
        /// <summary>As ScoutTrip, with a Bunkhouse of ours 22 m out from our Foundry toward theirs, so the
        /// scout sees it before the Foundry.</summary>
        public static string ScoutTripOuter() { ScoutTripTrial.Outer = true; return Stage<ScoutTripTrial>("ScoutTrip"); }
        public static string ScoutTripReport() => Report<ScoutTripTrial>();

        /// <summary>The AI's home ore running dry: at 240 s every seam within 25 m of its Foundries is cut
        /// to 300 (under the 4,500 at which it saves for a second ore line), our base kept standing. How
        /// long until its expansion Foundry goes up, and where its ore sat meanwhile (with the Diggers'
        /// reserve gating only the purchase, the saving ended at 400 and the build waited for 450: the ore
        /// went on soldiers in between and the line never went up).</summary>
        public static string DryExpand() { DryExpandTrial.CutAt = 240f; DryExpandTrial.Blind = false; return Stage<DryExpandTrial>("DryExpand"); }
        /// <summary>As DryExpand, with the seams cut at 450 s, about when home ore runs dry in a match.</summary>
        public static string DryExpandLate() { DryExpandTrial.CutAt = 450f; DryExpandTrial.Blind = false; return Stage<DryExpandTrial>("DryExpand"); }
        /// <summary>As DryExpand, and at the cut the AI forgets having seen anything more than 30 m from
        /// its Foundries, with every soldier it trains removed (only its Diggers see): with no field it
        /// has seen to go to, does it go and look?</summary>
        public static string DryExpandBlind() { DryExpandTrial.CutAt = 240f; DryExpandTrial.Blind = true; return Stage<DryExpandTrial>("DryExpand"); }
        public static string DryExpandReport() => Report<DryExpandTrial>();

        /// <summary>The AI's memory file corrupted: NaN, Infinity and out-of-range values, a truncated file,
        /// garbage, an empty file, bad history indices. For each, what <c>AIMemory.Load</c> hands the next
        /// match (a NaN weight stays NaN through every update and save: the plan choice broken for good).
        /// Edit mode or play; the real file is copied aside first and put back after.</summary>
        public static string MemoryFile()
        {
            string path = AIMemory.FilePath, backup = path + ".trialbak";
            bool had = System.IO.File.Exists(path);
            var sb = new StringBuilder();
            int n = (int)Strategy.Count * StrategySelector.NFEAT;
            var w = new string[n];
            for (int i = 0; i < n; i++) w[i] = i == 3 ? "NaN" : "0.5";
            string weights = "[" + string.Join(",", w) + "]";
            var cases = new (string name, string json)[]
            {
                ("NaN weight, NaN histogram, NaN and huge profile, wins over games",
                 "{\"version\":1,\"games\":5,\"aiWins\":9,\"weights\":" + weights + ",\"styleHistogram\":[NaN,0,0,0,0,0],\"aggression\":NaN,\"expansion\":1e39,\"defensive\":-3}"),
                ("Infinity in the plan times", "{\"version\":1,\"games\":2,\"strategySeconds\":[Infinity,0,0,0,0,0,0,0]}"),
                ("bad history indices", "{\"version\":1,\"games\":3,\"recentPlans\":[99,-3],\"recentOpenings\":[42],\"recentWins\":[7]}"),
                ("truncated", "{\"version\":1,\"games\":"),
                ("garbage", "not json at all"),
                ("empty", ""),
            };
            try
            {
                if (had) System.IO.File.Copy(path, backup, true);
                foreach (var c in cases)
                {
                    System.IO.File.WriteAllText(path, c.json);
                    AIMemoryData d;
                    try { d = AIMemory.Load(); } catch (System.Exception e) { sb.AppendLine($"{c.name}: Load threw {e.GetType().Name}"); continue; }
                    bool wFinite = true; foreach (var v in d.weights ?? new float[0]) if (float.IsNaN(v) || float.IsInfinity(v)) wFinite = false;
                    bool hFinite = true; foreach (var v in d.styleHistogram ?? new float[0]) if (float.IsNaN(v) || float.IsInfinity(v)) hFinite = false;
                    bool tFinite = true; foreach (var v in d.strategySeconds ?? new float[0]) if (float.IsNaN(v) || float.IsInfinity(v)) tFinite = false;
                    float[] prof = { d.aggression, d.expansion, d.defensive, d.harass, d.teching };
                    bool pOk = true; foreach (var v in prof) if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f || v > 1f) pOk = false;
                    // What the learner would do with it: a selector built from these weights scores every plan.
                    var sel = new StrategySelector();
                    sel.Init(1000u, d.weights, AIMemory.Trust(d));
                    bool exported = true; foreach (var v in sel.ExportWeights()) if (float.IsNaN(v) || float.IsInfinity(v)) exported = false;
                    sb.AppendLine($"{c.name}: games {d.games}, wins {d.aiWins}; weights {d.weights?.Length ?? -1} {(wFinite ? "finite" : "NOT FINITE")}, histogram {(hFinite ? "finite" : "NOT FINITE")}, " +
                                  $"plan times {(tFinite ? "finite" : "NOT FINITE")}, profile {(pOk ? "in 0-1" : "OUT OF RANGE")}; the selector's weights {(exported ? "finite" : "NOT FINITE")}");
                }
            }
            finally
            {
                if (had) System.IO.File.Copy(backup, path, true); else if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                if (System.IO.File.Exists(backup)) System.IO.File.Delete(backup);
            }
            sb.AppendLine($"real memory file {(had ? "put back" : "absent before, removed")}");
            return sb.ToString();
        }

        /// <summary>Map generation over many random seeds (every match draws a new one): per seed the
        /// generator's own report (places no unit reaches, corridors carved, its limit of four used up,
        /// expansions) and, on the baked NavMesh, a ground path from base A to base B and from a base to
        /// every ore seam. A seed failing any of these is a match a player could get that cannot be won,
        /// or an ore field nobody can mine. One seed a frame; the scene is reloaded at the end.</summary>
        public static string MapSweep() { MapSweepTrial.Count = 200; MapSweepTrial.Last = null; return Stage<MapSweepTrial>("MapSweep"); }
        public static string MapSweepReport() => MapSweepTrial.Last ?? Report<MapSweepTrial>();

        /// <summary>A map mined out: a spectated AI-vs-AI match, every ore node depleted at 300 s, then up to
        /// 480 s: each side's ore at the depletion and a minute later, waves each side started after it, and
        /// how the match ended (two sides with idle Diggers and armies under their thresholds stood to the
        /// cap). From the title screen (restart play first).</summary>
        public static string MinedOutStall() => Stage<MinedOutStallTrial>("MinedOutStall");
        public static string MinedOutStallReport() => Report<MinedOutStallTrial>();

        /// <summary>The next evaluation run plays the AI at Recruit / Veteran (every run had been at
        /// Commander, the level with the most actions a minute; players meet the others). Then start
        /// Evaluate AI from the menu; the runner puts Commander back when the run ends.</summary>
        public static string EvalAtRecruit() { StarForge.Game.AIEvalRunner.EvalDifficulty = AIDifficulty.Recruit; return "next evaluation at Recruit"; }
        public static string EvalAtVeteran() { StarForge.Game.AIEvalRunner.EvalDifficulty = AIDifficulty.Veteran; return "next evaluation at Veteran"; }

        /// <summary>When a unit gives way to an enemy in reach (GiveWayTrial): old rule and current.</summary>
        public static string GiveWay() => Stage<GiveWayTrial>("GiveWay");
        public static string GiveWayReport() => Report<GiveWayTrial>();

        /// <summary>The AI's eye: ten Troopers of team 0 parked in a clump in sight of a holding
        /// rifleman of team 1, and how many its Perception remembers there (one memory per
        /// sighting; it used to fold a clump into one or two); then, in sight of another, an
        /// enemy Mech Bay site and the same bay finished, and what each adds to the threat
        /// channel of its influence map (a site has no gun; a finished bay's gun counts).</summary>
        public static string Sight() => Stage<SightTrial>("Sight");
        public static string SightReport() => Report<SightTrial>();

        /// <summary>The Full evaluation's five games against one opponent (its seeds), on the
        /// current targeting rule; the OldRule twins play them with Unit.StickyTargets on for
        /// the whole run (set as play starts, cleared as it ends), for an A/B of the rule in
        /// real matches. Call out of play mode; read the [AIEval] lines when it stops.</summary>
        public static string EvalMacro() => EvalOne(ScriptedOpponent.Kind.Macro, false);
        public static string EvalMacroOldRule() => EvalOne(ScriptedOpponent.Kind.Macro, true);
        /// <summary>The fifth Macro game alone (seed 1148), where the current rule lost twice: one game a call.</summary>
        public static string EvalMacroGame5() => EvalOne(ScriptedOpponent.Kind.Macro, false, 4, 1);
        public static string EvalMacroGame5OldRule() => EvalOne(ScriptedOpponent.Kind.Macro, true, 4, 1);

        static string EvalOne(ScriptedOpponent.Kind kind, bool oldRule, int first = 0, int games = 5)
        {
            if (EditorApplication.isPlaying) return "stop play first";
            PlayerPrefs.SetInt(AIEvalRunner.PrefOnly, (int)kind);
            PlayerPrefs.SetInt(AIEvalRunner.PrefFirst, first);
            EditorApplication.playModeStateChanged -= StickyForEval;
            if (oldRule) EditorApplication.playModeStateChanged += StickyForEval;
            AIEvalMenu.Run(games, 900f);
            return $"evaluating {kind} games {first + 1}-{first + games} with the {(oldRule ? "old" : "current")} targeting rule";
        }

        static void StickyForEval(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode) Unit.StickyTargets = true;
            else if (s == PlayModeStateChange.ExitingPlayMode)
            {
                Unit.StickyTargets = false;
                EditorApplication.playModeStateChanged -= StickyForEval;
            }
        }

        /// <summary>The statics the trials here change, as they stand now (in or out of play):
        /// all should read off once no trial runs, however the last one ended.</summary>
        public static string Statics() =>
            $"Commander.Suspended={Commander.Suspended} Unit.StickyTargets={Unit.StickyTargets} timeScale={Time.timeScale:0.##} " +
            $"trials running {Object.FindObjectsByType<BugTrial>().Length} playing={EditorApplication.isPlaying}";

        /// <summary>The game speed survives a pause: x2, pause, resume.</summary>
        public static string PauseSpeed()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.State != MatchState.Playing) return "no match playing";
            float was = boot.Speed, scale = Time.timeScale;
            boot.SetSpeed(2f);
            boot.TogglePause();
            float paused = Time.timeScale;
            boot.TogglePause();
            float resumed = Time.timeScale;
            boot.SetSpeed(was);
            Time.timeScale = scale;   // a trial's 3x, or an evaluation's 8x, as it was
            return $"x2, paused {paused:0.##}, resumed {resumed:0.##} (want 0 and 2); back to x{Time.timeScale:0.##}";
        }

        /// <summary>The command card for Diggers with a finished Mech Bay, the bay alone,
        /// and a Digger ordered from the minimap and the card while the game is paused.</summary>
        public static string Cards()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var boot = GameBootstrap.Instance;
            var world = GameWorld.Instance;
            var pc = Object.FindAnyObjectByType<PlayerController>();
            if (boot == null || world == null || pc == null || boot.State != MatchState.Playing) return "no match playing";
            Unit digger = null;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == pc.team && u.Type == UnitType.Worker) { digger = u; break; }
            if (digger == null) return "no Digger";
            var home = world.Map.StartPos(pc.team);
            Vector2 spot = home;
            bool found = false;
            for (int i = 0; i < 48 && !found; i++)
            {
                float a = i * 0.7f, r = 14f + i * 0.5f;
                spot = home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                found = world.CanPlace(UnitType.MechBay, spot);
            }
            if (!found) return "no room for a bay";
            var bay = world.Spawn(UnitType.MechBay, pc.team, spot, true);
            var cards = new List<CardAction>();
            var sb = new StringBuilder();
            try
            {
                pc.Selection.Clear(); pc.Selection.Add(digger); pc.Selection.Add(bay);
                pc.BuildCard(cards);
                sb.AppendLine($"Digger + bay: {cards.Count} cards (12 slots): {Kinds(cards)}");
                pc.Selection.Clear(); pc.Selection.Add(bay);
                pc.BuildCard(cards);
                sb.AppendLine($"bay alone: {cards.Count} cards: {Kinds(cards)}");

                pc.Selection.Clear(); pc.Selection.Add(digger);
                var before = digger.order;
                var beforePos = digger.orderPos;
                boot.TogglePause();
                pc.MinimapCommand(digger.pos + new Vector2(20f, 0f));
                pc.Execute(new CardAction { kind = CardKind.Stop });
                bool changed = digger.order != before || digger.orderPos != beforePos;
                boot.TogglePause();
                sb.AppendLine($"paused: minimap move and Stop {(changed ? "CHANGED the Digger's order" : "were ignored")} ({before} kept)");
            }
            finally
            {
                if (boot.State == MatchState.Paused) boot.TogglePause();
                pc.Selection.Clear();
                bay.BeginDeath();
            }
            return sb.ToString();
        }

        /// <summary>A preset picked on the title screen changes the grass there and then:
        /// the instances grown under each preset, then the player's own put back.</summary>
        public static string Quality()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var scatter = Object.FindAnyObjectByType<GroundScatter>();
            if (scatter == null) return "no GroundScatter";
            var was = QualityController.Current;
            var sb = new StringBuilder();
            try
            {
                foreach (var q in new[] { SFQuality.High, SFQuality.Balanced, SFQuality.Battery })
                {
                    QualityController.Current = q;
                    sb.Append($"{q} {scatter.InstanceCount} instances (density {scatter.density:0.##}); ");
                }
            }
            finally { QualityController.Current = was; }
            sb.Append($"back to {was}: {scatter.InstanceCount}");
            return sb.ToString();
        }

        static string Kinds(List<CardAction> cards)
        {
            var parts = new List<string>();
            foreach (var c in cards) parts.Add(c.kind == CardKind.Build || c.kind == CardKind.Train ? $"{c.kind} {c.type}" : c.kind == CardKind.MechUpgrade ? $"{c.upgrade}" : c.kind.ToString());
            return string.Join(", ", parts);
        }

        static string Stage<T>(string name) where T : BugTrial
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<T>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject(name + "Trial").AddComponent<T>();
            return $"staging; call {name}Report in about a minute";
        }

        static string Report<T>() where T : BugTrial
        {
            var r = Object.FindAnyObjectByType<T>();
            return r == null ? "no trial running" : r.report;
        }
    }

    public abstract class BugTrial : MonoBehaviour
    {
        public string report = "staging";

        protected GameWorld world;
        protected MapInfo map;

        static int holds;
        static bool savedSuspended;
        static float savedScale = 1f;
        static bool scaleOwned;
        bool holding;
        System.Action<GameEvent> listening;

        /// <summary>Silence the opponent AI and run at 3x until <see cref="ReleaseStatics"/>.</summary>
        protected void HoldStatics()
        {
            if (holding) return;
            holding = true;
            if (holds++ == 0)
            {
                savedSuspended = Commander.Suspended;
                savedScale = Time.timeScale;
                // Not the speed the match's state calls for: someone else set it (an evaluation's
                // 8x, a duel's 6x), and that is what goes back.
                scaleOwned = !Mathf.Approximately(savedScale, StateScale());
            }
            Commander.Suspended = true;
            Time.timeScale = 3f;
        }

        protected void ReleaseStatics()
        {
            if (listening != null && world != null) world.Event -= listening;
            listening = null;
            if (!holding) return;
            holding = false;
            if (--holds > 0) return;
            Commander.Suspended = savedSuspended;
            Unit.StickyTargets = false;
            // The speed the match's own state calls for, not the one saved at the hold: paused or
            // resumed while a trial ran, the saved one froze a running match (0) or ran a paused one.
            Time.timeScale = scaleOwned || GameBootstrap.Instance == null ? savedScale : StateScale();
        }

        /// <summary>What GameBootstrap sets for its state (SetState, and EndMatch's 1 for the end screen).</summary>
        static float StateScale()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) return Time.timeScale;
            return boot.State == MatchState.Playing ? boot.Speed : boot.State == MatchState.Paused ? 0f : 1f;
        }

        /// <summary>Listen to the world's events until <see cref="ReleaseStatics"/> (or ListenWhileHeld(null)).</summary>
        protected void ListenWhileHeld(System.Action<GameEvent> handler)
        {
            if (listening != null && world != null) world.Event -= listening;
            listening = handler;
            if (handler != null && world != null) world.Event += handler;
        }

        void OnDestroy()
        {
            ClearSpawned();
            ReleaseStatics();
        }

        [InitializeOnEnterPlayMode]
        static void ClearOnPlay()
        {
            holds = 0;
            Unit.StickyTargets = false;
            // MoraleTrials keeps a hold count of its own: overlapping one of its trials with
            // one of these, the later release could put back a Suspended the other had set,
            // and the next play -- the user's own included -- had a silent opponent. No trial
            // runs at play entry, so whatever is left on goes here.
            Commander.Suspended = false;
            // Trials (these, MechTrials, FxLook, GroundLook) start their matches with the AI's memory
            // off and none put it back: the user's next match in the editor ran with no memory, and
            // outside the debug view there is no toggle to see it by. Players cannot turn it off.
            if (!MatchSettings.debugAI) MatchSettings.aiMemory = true;
        }

        protected IEnumerator EnsureMatch()
        {
            var boot = GameBootstrap.Instance;
            world = GameWorld.Instance;
            map = MapInfo.Instance;
            if (boot != null && world != null && !world.running)
            {
                MatchSettings.aiMemory = false;
                MatchSettings.spectate = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
        }

        protected bool Ready => world != null && map != null && world.running;

        /// <summary>Wait this much match time.</summary>
        protected IEnumerator Wait(float seconds)
        {
            float until = world.time + seconds;
            while (world.time < until) yield return null;
        }

        /// <summary>Dry, open ground away from both bases, with room for a Sentinel.</summary>
        protected bool FindField(System.Random rng, out Vector2 at, out Vector2 dir)
        {
            at = dir = default;
            for (int tries = 0; tries < 200; tries++)
            {
                var b = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                if (!map.InBounds(b, 40f) || map.WaterDepth(b) > -0.3f) continue;
                if ((b - map.StartPos(0)).magnitude < 60f || (b - map.StartPos(1)).magnitude < 60f) continue;
                if (!world.CanPlace(UnitType.Sentinel, b)) continue;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                bool open = true;
                foreach (float r in new[] { -34f, -12f, 4f, 25f })
                    if (!world.Walkable(b + d * r, 1f)) { open = false; break; }
                if (!open || !world.Walkable(b + new Vector2(-d.y, d.x) * 4f, 1f)) continue;
                at = b; dir = d;
                return true;
            }
            return false;
        }

        protected static void Remove(Unit u)
        {
            if (Unit.Live(u)) u.BeginDeath();
        }

        /// <summary>What the trial put on the field: taken off again when it ends, or when it is
        /// cut short (OnDestroy), so a re-staged trial does not find the last one's pieces.</summary>
        protected readonly List<Unit> spawned = new List<Unit>();

        protected Unit Put(UnitType type, int team, Vector2 at, bool complete = true)
        {
            var u = world.Spawn(type, team, at, complete);
            spawned.Add(u);
            return u;
        }

        protected void ClearSpawned()
        {
            foreach (var u in spawned) Remove(u);
            spawned.Clear();
        }
    }

    public sealed class TargetingTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(4711);
            HoldStatics();
            try
            {
                foreach (bool sticky in new[] { true, false })
                {
                    Unit.StickyTargets = sticky;
                    if (!FindField(rng, out var b, out var d)) { sb.AppendLine("no open field found"); break; }
                    var tower = Put(UnitType.Sentinel, 1, b);
                    var holder = Put(UnitType.Trooper, 1, b + new Vector2(-d.y, d.x) * 4f);
                    world.CmdHold(new List<Unit> { holder });
                    var scout = Put(UnitType.Skimmer, 0, b + d * 25f);
                    world.CmdHold(new List<Unit> { scout });
                    yield return Wait(3f);
                    string locked = $"tower on {Name(tower.target, scout)}, holder on {Name(holder.target, scout)}";

                    var attacker = Put(UnitType.Trooper, 0, b - d * 34f);
                    world.CmdMove(new List<Unit> { attacker }, b - d * 12f, false);
                    int towerAtAttacker = 0, towerOther = 0, holderAtAttacker = 0, holderOther = 0;
                    ListenWhileHeld(e =>
                    {
                        if (e.kind != GameEventKind.Fire) return;
                        if (e.unit == tower) { if (tower.target == attacker) towerAtAttacker++; else towerOther++; }
                        if (e.unit == holder) { if (holder.target == attacker) holderAtAttacker++; else holderOther++; }
                    });
                    float t0 = world.time, towerHp = tower.hp, killedAt = -1f;
                    while (world.time - t0 < 20f)
                    {
                        if (killedAt < 0f && !Unit.Live(attacker)) killedAt = world.time - t0;
                        yield return null;
                    }
                    ListenWhileHeld(null);
                    sb.AppendLine($"{(sticky ? "kept to the death (before)" : "let go for one in reach (after)")}: {locked}");
                    sb.AppendLine($"  tower fired {towerAtAttacker} at the attacker, {towerOther} elsewhere; " +
                                  $"holder {holderAtAttacker} at the attacker, {holderOther} elsewhere; " +
                                  $"attacker {(killedAt >= 0f ? $"down after {killedAt:0.0} s" : $"alive at {attacker.hp:0}/{attacker.MaxHp:0} hp")}; " +
                                  $"tower lost {towerHp - tower.hp:0} hp");
                    ClearSpawned();
                    yield return Wait(3f);
                }
            }
            finally { ReleaseStatics(); }
            report = sb.ToString();
        }

        static string Name(Unit t, Unit scout) => t == null ? "nothing" : t == scout ? "the parked Skimmer" : t.def.displayName;
    }

    public sealed class SightTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var ai = GameBootstrap.Instance != null ? GameBootstrap.Instance.AI : null;
            if (ai == null) { report = "no opponent AI"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(9157);
            HoldStatics();
            try
            {
                // A clump: ten riflemen within a few metres, in sight of one of theirs 25 m off.
                if (!FindField(rng, out var b, out var d)) { report = "no open field found"; yield break; }
                var eye = Put(UnitType.Trooper, 1, b + d * 25f);
                world.CmdHold(new List<Unit> { eye });
                var clump = new List<Unit>();
                for (int i = 0; i < 10; i++)
                {
                    float a = i * 2.39996f, r = 1.1f * Mathf.Sqrt(i + 0.5f);
                    clump.Add(Put(UnitType.Trooper, 0, b + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r));
                }
                world.CmdHold(clump);
                yield return Wait(1f);
                ai.Perception.Update(0.1f);
                int seen = 0;
                float spread = 0f;
                foreach (var u in clump)
                {
                    if (world.Visible(1, u.pos)) seen++;
                    spread = Mathf.Max(spread, (u.pos - b).magnitude);
                }
                sb.AppendLine($"clump: 10 Troopers within {spread:0.0} m of its middle, {seen} in team 1's sight; " +
                              $"Perception remembers {TroopersNear(ai, b)} there");
                // A few passes more: nothing dropped as "looked, and it is gone".
                for (int k = 0; k < 5; k++) { yield return Wait(0.3f); ai.Perception.Update(0.3f); }
                sb.AppendLine($"  five passes later: {TroopersNear(ai, b)}");
                ClearSpawned();
                yield return Wait(2f);

                // A Mech Bay 28 m from one of theirs: in its sight, out of the gun's 24 m.
                if (!FindField(rng, out var c, out var e)) { sb.AppendLine("no second field found"); report = sb.ToString(); yield break; }
                var eye2 = Put(UnitType.Trooper, 1, c + e * 28f);
                world.CmdHold(new List<Unit> { eye2 });
                yield return Wait(1f);
                float bare = ThreatAt(ai, c);
                var bay = Put(UnitType.MechBay, 0, c, false);
                yield return Wait(1f);
                float atSite = ThreatAt(ai, c);
                string siteSeen = BayMemory(ai, c);
                // Finished in place (not through CompleteConstruction: that would start team 0's drop).
                bay.buildProgress = 1f;
                bay.hp = bay.MaxHp;
                yield return Wait(1f);
                float atBay = ThreatAt(ai, c);
                sb.AppendLine($"threat channel at the bay's spot: nothing there {bare:0.00}, a site {atSite:0.00} ({siteSeen}), " +
                              $"the finished bay {atBay:0.00} ({BayMemory(ai, c)})");
            }
            finally
            {
                ClearSpawned();
                ReleaseStatics();
            }
            report = sb.ToString();
        }

        static int TroopersNear(Commander ai, Vector2 at)
        {
            int n = 0;
            foreach (var r in ai.Perception.Enemies)
                if (r.type == UnitType.Trooper && (r.pos - at).magnitude < 8f) n++;
            return n;
        }

        float ThreatAt(Commander ai, Vector2 at)
        {
            ai.Perception.Update(0.1f);
            ai.Influence.Build(ai.Perception, world, 1);
            // The GPU backend answers a frame or two late (asynchronous readback): work this
            // field out now, from the same list Build put together, which is what is under test.
            ai.Influence.ComputeCPU();
            return ai.Influence.Sample(1, at);
        }

        static string BayMemory(Commander ai, Vector2 at)
        {
            foreach (var r in ai.Perception.Enemies)
                if (r.type == UnitType.MechBay && (r.pos - at).magnitude < 3f) return r.unfinished ? "remembered as a site" : "remembered finished";
            return "not remembered";
        }
    }

    /// <summary>When a unit gives way to an enemy in reach, each with the old rule
    /// (StickyTargets) and the current one: an attack-move on a Sentinel standing beside a
    /// Foundry (it should not trade the tower for the wall), a tower locked on a scout with
    /// an enemy Garrison whose edge is in its reach but whose middle is not, and a squad whose
    /// attack target dies, the next thing it sees a Skimmer making off and a rifleman in reach
    /// behind it.</summary>
    public sealed class GiveWayTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            try
            {
                // Each scenario draws its ground from its own seed, so both rules fight on the same fields.
                foreach (bool sticky in new[] { true, false })
                {
                    Unit.StickyTargets = sticky;
                    string rule = sticky ? "old rule" : "current";
                    Vector2 b, d, p;

                    // 1. Attack-move on a Sentinel with a Foundry beside it, on three fields.
                    for (int rep = 0; rep < 3; rep++)
                    {
                        if (!FindField(new System.Random(2718 + rep), out b, out d)) { sb.AppendLine("no open field found"); break; }
                        p = new Vector2(-d.y, d.x);
                        var tower = Put(UnitType.Sentinel, 1, b);
                        var wall = Put(UnitType.Foundry, 1, b + p * 7.5f);
                        var squad = new List<Unit>();
                        for (int i = 0; i < 6; i++) squad.Add(Put(UnitType.Trooper, 0, b - d * 34f + p * ((i - 2.5f) * 1.4f)));
                        world.CmdMove(squad, b + d * 4f, true);
                        int atTower = 0, atWall = 0, atWallAfter = 0, atOther = 0;
                        ListenWhileHeld(e =>
                        {
                            if (e.kind != GameEventKind.Fire || e.unit == null || !squad.Contains(e.unit)) return;
                            if (e.unit.target == tower) atTower++;
                            else if (e.unit.target == wall) { if (Unit.Live(tower)) atWall++; else atWallAfter++; }
                            else atOther++;
                        });
                        float towerHp = tower.hp, wallHp = wall.hp;
                        // How each one came to be on the wall while the tower stood: its first pick
                        // (the nearest when it first saw them, the same under either rule) or a switch.
                        var last = new Dictionary<Unit, Unit>();
                        int firstOnWall = 0, switchedToWall = 0;
                        float t1 = world.time;
                        while (world.time - t1 < 25f)
                        {
                            foreach (var u in squad)
                            {
                                if (!Unit.Live(u)) continue;
                                last.TryGetValue(u, out var was);
                                if (u.target == was) continue;
                                if (u.target == wall && Unit.Live(tower)) { if (was == null) firstOnWall++; else switchedToWall++; }
                                last[u] = u.target;
                            }
                            yield return null;
                        }
                        ListenWhileHeld(null);
                        int alive = 0;
                        foreach (var u in squad) if (Unit.Live(u)) alive++;
                        sb.AppendLine($"{rule}: attack-move on a Sentinel beside a Foundry: shots at the tower {atTower}, the Foundry {atWall} while the tower stood and {atWallAfter} after, else {atOther}; " +
                                      $"onto the wall while the tower stood: {firstOnWall} first picks, {switchedToWall} switches; tower {(Unit.Live(tower) ? "standing" : "destroyed")}, " +
                                      $"tower lost {towerHp - tower.hp:0}, Foundry {wallHp - wall.hp:0}; {alive}/6 alive");
                        ClearSpawned();
                        yield return Wait(3f);
                    }

                    // 2. A tower locked on a scout; an enemy Garrison's edge 18 m off (middle 22 m).
                    if (!FindField(new System.Random(3141), out b, out d)) { sb.AppendLine("no open field found"); break; }
                    var tower2 = Put(UnitType.Sentinel, 1, b);
                    var scout = Put(UnitType.Skimmer, 0, b + d * 25f);
                    world.CmdHold(new List<Unit> { scout });
                    yield return Wait(3f);
                    float edge = tower2.def.range - 2f;
                    var garrison = Put(UnitType.Garrison, 0, b - d * (edge + Defs.Get(UnitType.Garrison).radius));
                    int atGarrison = 0, atScout = 0;
                    ListenWhileHeld(e =>
                    {
                        if (e.kind != GameEventKind.Fire || e.unit != tower2) return;
                        if (tower2.target == garrison) atGarrison++; else if (tower2.target == scout) atScout++;
                    });
                    float gHp = garrison.hp;
                    yield return Wait(10f);
                    ListenWhileHeld(null);
                    sb.AppendLine($"{rule}: tower on a parked scout, a Garrison's edge {edge:0} m off: shots at the Garrison {atGarrison}, the scout {atScout}; Garrison lost {gHp - garrison.hp:0}");
                    ClearSpawned();
                    yield return Wait(3f);

                    // 2b. The same tower, then a rifleman shooting it from 16 m and an enemy Garrison
                    //     whose wall is nearer (its middle 19 m off): the rifleman should come first.
                    if (!FindField(new System.Random(3142), out b, out d)) { sb.AppendLine("no open field found"); break; }
                    p = new Vector2(-d.y, d.x);
                    var tower3 = Put(UnitType.Sentinel, 1, b);
                    var scout3 = Put(UnitType.Skimmer, 0, b + d * 25f);
                    world.CmdHold(new List<Unit> { scout3 });
                    yield return Wait(3f);
                    var wall3 = Put(UnitType.Garrison, 0, b + p * 19f);
                    var rifle = Put(UnitType.Trooper, 0, b - d * 16f);
                    world.CmdHold(new List<Unit> { rifle });
                    int atRifle = 0, atWall3 = 0, atScout3 = 0;
                    float rifleDownAt = -1f, t3 = world.time;
                    ListenWhileHeld(e =>
                    {
                        if (e.kind != GameEventKind.Fire || e.unit != tower3) return;
                        if (tower3.target == rifle) atRifle++; else if (tower3.target == wall3) atWall3++; else if (tower3.target == scout3) atScout3++;
                    });
                    float tower3Hp = tower3.hp;
                    while (world.time - t3 < 12f)
                    {
                        if (rifleDownAt < 0f && !Unit.Live(rifle)) rifleDownAt = world.time - t3;
                        yield return null;
                    }
                    ListenWhileHeld(null);
                    sb.AppendLine($"{rule}: tower on a parked scout, a rifleman shooting it from 16 m, a Garrison's wall nearer: shots at the rifleman {atRifle}, the Garrison {atWall3}, the scout {atScout3}; " +
                                  $"rifleman {(rifleDownAt >= 0f ? $"down after {rifleDownAt:0.0} s" : "alive")}, tower lost {tower3Hp - tower3.hp:0}");
                    ClearSpawned();
                    yield return Wait(3f);

                    // 3. The attack target dies; next a Skimmer 25 m off makes off, a rifleman stands 12 m behind.
                    if (!FindField(new System.Random(1618), out b, out d)) { sb.AppendLine("no open field found"); break; }
                    p = new Vector2(-d.y, d.x);
                    var shooters = new List<Unit>();
                    for (int i = 0; i < 3; i++) shooters.Add(Put(UnitType.Trooper, 0, b + p * ((i - 1) * 1.2f)));
                    var first = Put(UnitType.Trooper, 1, b + d * 4f);
                    first.hp = 1f;
                    world.CmdHold(new List<Unit> { first });
                    var runner = Put(UnitType.Skimmer, 1, b + d * 25f);
                    world.CmdHold(new List<Unit> { runner });
                    world.CmdAttack(shooters, first);
                    float until = world.time + 5f;
                    while (Unit.Live(first) && world.time < until) yield return null;
                    yield return null;   // a tick to pick the next one up
                    var behind = Put(UnitType.Trooper, 1, b - d * 12f);
                    world.CmdHold(new List<Unit> { behind });
                    world.CmdMove(new List<Unit> { runner }, b + d * 70f, false);
                    int atBehind = 0, atRunner = 0;
                    ListenWhileHeld(e =>
                    {
                        if (e.kind != GameEventKind.Fire || e.unit == null || !shooters.Contains(e.unit)) return;
                        if (e.unit.target == behind) atBehind++; else if (e.unit.target == runner) atRunner++;
                    });
                    float behindHp = behind.hp;
                    yield return Wait(10f);
                    ListenWhileHeld(null);
                    float moved = 0f;
                    foreach (var u in shooters) if (Unit.Live(u)) moved = Mathf.Max(moved, (u.pos - b).magnitude);
                    sb.AppendLine($"{rule}: attack target killed, a Skimmer making off, a rifleman 12 m behind: shots at the rifleman {atBehind}, the Skimmer {atRunner}; " +
                                  $"rifleman lost {behindHp - behind.hp:0}; squad went up to {moved:0} m");
                    ClearSpawned();
                    yield return Wait(3f);
                }
            }
            finally { ReleaseStatics(); }
            report = sb.ToString();
        }
    }

    public sealed class HarvestTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            try
            {
                if (world.time < 20f) yield return Wait(20f - world.time);
                Unit foundry = null;
                foreach (var u in world.units)
                    if (Unit.Live(u) && u.team == 0 && u.Type == UnitType.Foundry) { foundry = u; break; }
                if (foundry == null) { report = "team 0 has no Foundry"; yield break; }
                // Strike while loads are on the road: 2+ Diggers bringing ore in, 5+ m off their
                // seam and 3+ m off the Foundry's edge (the road is short: "off the seam" alone
                // was often a Digger at the door, delivering in the next second).
                for (float waited = 0f; waited < 90f && OnTheRoad(foundry) < 2; waited += 0.1f) yield return Wait(0.1f);
                Vector2 at = foundry.pos;
                float yaw = foundry.yaw;
                var F = world.factions[0];
                // One of them sent to its ore with the load while the Foundry stands: it brings
                // the load in first, and falling on the way, stops like the rest (kept from the
                // order, the flag sent it back to the ore after all).
                Unit sentFirst = null;
                float sentOut = 0f;
                foreach (var u in world.units)
                    if (OnRoad(u, foundry) && u.carrying >= Unit.OrePerTrip && u.Dist(foundry) > sentOut) { sentFirst = u; sentOut = u.Dist(foundry); }
                if (sentFirst != null)
                {
                    world.CmdHarvest(new List<Unit> { sentFirst }, sentFirst.harvestNode);
                    yield return Wait(0.15f);   // the order taken (Harvest, then Return), not delivered
                }
                var sentFrom = sentFirst != null ? sentFirst.pos : Vector2.zero;
                int sentLoad = sentFirst != null ? sentFirst.carrying : 0;
                var sentOrder = sentFirst != null ? sentFirst.order : Order.Idle;
                float sentToOre = sentFirst != null && Unit.Live(sentFirst.harvestNode) ? sentFirst.Dist(sentFirst.harvestNode) : 0f;
                sb.AppendLine($"t {world.time:0}s: Foundry destroyed; nodes near it hold {OreNear(at)}, {Diggers()}");
                // The loads on the road when it falls should stop where they are: turned back to
                // their ore, they walked into whatever had just taken the Foundry.
                var road = new List<(Unit u, Vector2 from, float toOre)>();
                foreach (var u in world.units)
                    if (Unit.Live(u) && u != sentFirst && u.team == 0 && u.Type == UnitType.Worker && u.order == Order.Return && Unit.Live(u.harvestNode))
                        road.Add((u, u.pos, u.Dist(u.harvestNode)));
                world.Damage(foundry, 1e7f, foundry.pos, null);
                int oreBefore = OreNear(at);
                for (int k = 0; k < 4; k++)
                {
                    yield return Wait(10f);
                    sb.AppendLine($"  +{10 * (k + 1)}s: nodes {OreNear(at)} ({oreBefore - OreNear(at)} taken), side's ore {F.ore}, {Diggers()}");
                    if (k == 0) sb.AppendLine("  " + RoadReport(road));
                    if (k == 0 && Unit.Live(sentFirst))
                        sb.AppendLine($"  sent to its ore with the load while the Foundry stood: 10 s after the fall moved {(sentFirst.pos - sentFrom).magnitude:0.0} m, " +
                                      $"m to its ore {sentToOre:0}->{(Unit.Live(sentFirst.harvestNode) ? sentFirst.Dist(sentFirst.harvestNode) : 0f):0}, waitAtOre {sentFirst.waitAtOre}; " +
                                      $"at the kill {sentOrder} carrying {sentLoad}, now {sentFirst.order} carrying {sentFirst.carrying}");
                }
                // A loaded Digger sent to ore with nowhere to bring it should go there and wait
                // (flipped to waiting, it stood where it was -- under a gun, say).
                Unit sent = null, node = null;
                float far = 0f;
                foreach (var u in world.units)
                    if (Unit.Live(u) && u.team == 0 && u.Type == UnitType.Worker && u.carrying > 0) { sent = u; break; }
                if (sent != null)
                    foreach (var n in world.units)
                        if (Unit.Live(n) && n.Type == UnitType.Ore && n.oreLeft > 0 && sent.Dist(n) > far && sent.Dist(n) < 45f) { far = sent.Dist(n); node = n; }
                if (sent != null && node != null)
                {
                    world.CmdHarvest(new List<Unit> { sent }, node);
                    yield return Wait(9f);
                    sb.AppendLine($"  a loaded Digger sent to ore {far:0} m off: {sent.Dist(node):0.0} m from it 9 s later, {sent.order}, carrying {sent.carrying}");
                }
                var rebuilt = world.Spawn(UnitType.Foundry, 0, at, true, yaw);
                int bankBefore = F.ore;
                yield return Wait(25f);
                sb.AppendLine($"Foundry back: {F.ore - bankBefore} ore brought in over 25 s, {Diggers()}");
                if (!Unit.Live(rebuilt)) sb.AppendLine("  (the rebuilt Foundry did not stand)");
            }
            finally { ReleaseStatics(); }
            report = sb.ToString();
        }

        int OnTheRoad(Unit foundry)
        {
            int n = 0;
            foreach (var u in world.units) if (OnRoad(u, foundry)) n++;
            return n;
        }

        static bool OnRoad(Unit u, Unit foundry) =>
            Unit.Live(u) && u.team == 0 && u.Type == UnitType.Worker && u.order == Order.Return && u.carrying > 0
            && Unit.Live(u.harvestNode) && u.Dist(u.harvestNode) > 5f && u.Dist(foundry) - foundry.def.radius > 3f;

        static string RoadReport(List<(Unit u, Vector2 from, float toOre)> road)
        {
            if (road.Count == 0) return "on the road at the kill: none";
            float minMoved = 1e9f, maxMoved = 0f;
            int closer = 0, alive = 0;
            var ore = new List<string>();
            foreach (var (u, from, toOre) in road)
            {
                if (!Unit.Live(u)) continue;
                alive++;
                float moved = (u.pos - from).magnitude;
                minMoved = Mathf.Min(minMoved, moved);
                maxMoved = Mathf.Max(maxMoved, moved);
                float now = Unit.Live(u.harvestNode) ? u.Dist(u.harvestNode) : toOre;
                if (now < toOre - 3f) closer++;
                ore.Add($"{toOre:0}->{now:0}");
            }
            if (alive == 0) return $"on the road at the kill: {road.Count}, none alive 10 s later";
            return $"on the road at the kill: {road.Count} in Return; 10 s later moved {minMoved:0.0}-{maxMoved:0.0} m, " +
                   $"{closer} of {alive} closed on their ore by 3+ m (m to it: {string.Join(", ", ore)})";
        }

        int OreNear(Vector2 at)
        {
            int sum = 0;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.Type == UnitType.Ore && (u.pos - at).magnitude < 45f) sum += u.oreLeft;
            return sum;
        }

        string Diggers()
        {
            var counts = new Dictionary<string, int>();
            int carrying = 0, n = 0;
            foreach (var u in world.units)
            {
                if (!Unit.Live(u) || u.team != 0 || u.Type != UnitType.Worker) continue;
                n++;
                carrying += u.carrying;
                string k = u.order.ToString();
                counts.TryGetValue(k, out int c);
                counts[k] = c + 1;
            }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{kv.Value} {kv.Key}");
            return $"{n} Diggers ({string.Join(", ", parts)}) carrying {carrying}";
        }
    }

    public sealed class RoadGunTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            if (Commander.Suspended) { report = "the opponent AI is silenced: let the trial holding it finish first"; yield break; }
            var sb = new StringBuilder();
            if (world.time < 30f) yield return Wait(30f - world.time);
            Unit foundry = null, worker = null, node = null;
            foreach (var u in world.units)
            {
                if (!Unit.Live(u) || u.team != 1) continue;
                if (foundry == null && u.Type == UnitType.Foundry) foundry = u;
                if (worker == null && u.Type == UnitType.Worker) worker = u;
            }
            if (foundry == null || worker == null) { report = "team 1 has no Foundry or no Digger"; yield break; }
            float best = float.MaxValue;
            foreach (var n in world.units)
                if (Unit.Live(n) && n.Type == UnitType.Ore && n.oreLeft > 0 && (n.pos - foundry.pos).magnitude < best) { best = (n.pos - foundry.pos).magnitude; node = n; }
            if (node == null) { report = "no ore by their Foundry"; yield break; }
            // A spot 12-16 m out from the seam, a tower of ours 14 m beyond it: the tower reaches
            // the Digger (range 20) but not the seam (the AI keeps range + 5 = 25 m clear of it).
            Vector2 at = Vector2.zero, gunAt = Vector2.zero;
            bool found = false;
            for (int i = 0; i < 16 && !found; i++)
            {
                float a = i * Mathf.PI / 8f;
                at = world.NearestReachable(worker.pos, node.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 14f);
                float out1 = (at - node.pos).magnitude;
                if (out1 < 12f || out1 > 16f) continue;
                gunAt = at + (at - node.pos).normalized * 14f;
                if ((gunAt - node.pos).magnitude < 27f || (gunAt - foundry.pos).magnitude < 8f) continue;
                found = true;
            }
            if (!found) { report = "no spot 12-16 m off their seam with room for a tower beyond it"; yield break; }
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Foundry) world.Damage(u, 1e7f, u.pos, null);
            yield return Wait(0.5f);
            // Stopped on the road with its load, as a Digger is when its last Foundry falls.
            var dig = Put(UnitType.Worker, 1, at);
            dig.carrying = Unit.OrePerTrip;
            dig.harvestNode = node;
            dig.order = Order.Return;
            dig.hp = 1e5f;
            var gun = Put(UnitType.Sentinel, 0, gunAt);
            var seam = node;
            sb.AppendLine($"t {world.time:0}s: their Foundry down; a loaded Digger of theirs {(at - node.pos).magnitude:0} m off its seam, " +
                          $"our tower {(gunAt - at).magnitude:0} m from it and {(gunAt - node.pos).magnitude:0} m from the seam");
            var orders = new List<string>();
            Order last = dig.order;
            float inReach = 0f;
            for (float t = 0f; t < 25f; t += 0.5f)
            {
                yield return Wait(0.5f);
                if (!Unit.Live(dig) || !Unit.Live(gun)) break;
                if (dig.order != last) { orders.Add($"{dig.order} at +{t + 0.5f:0.0}s"); last = dig.order; }
                if (dig.Dist(gun) <= gun.def.range + dig.def.radius) inReach += 0.5f;
            }
            if (!Unit.Live(dig)) sb.AppendLine("  the Digger died");
            else if (!Unit.Live(gun)) sb.AppendLine("  the tower fell first");
            else
                sb.AppendLine($"  25 s later: {(dig.pos - at).magnitude:0.0} m from where it stopped, {dig.Dist(gun):0} m from the tower, {inReach:0.0} s of 25 in its reach, " +
                              $"{1e5f - dig.hp:0} hp taken (a Digger has {dig.def.hp:0}); orders: {(orders.Count == 0 ? "none" : string.Join(", ", orders))}; " +
                              $"{dig.order}, {(Unit.Live(dig.harvestNode) ? dig.Dist(dig.harvestNode) : -1f):0} m from {(dig.harvestNode == seam ? "its own seam" : "another seam")}, waitAtOre {dig.waitAtOre}");
            // That Digger keeps team 1 from ever losing (no Foundry, a Digger 1e5 hp strong).
            ClearSpawned();
            report = sb.ToString();
        }
    }

    public sealed class HudAllocTrial : BugTrial
    {
        float avg, peak;

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var player = Object.FindAnyObjectByType<PlayerController>();
            if (player == null) { report = "no PlayerController"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            try
            {
                Unit digger = null, foundry = null;
                foreach (var u in world.units)
                {
                    if (!Unit.Live(u) || u.team != player.team) continue;
                    if (digger == null && u.Type == UnitType.Worker) digger = u;
                    if (foundry == null && u.Type == UnitType.Foundry) foundry = u;
                }
                if (digger == null || foundry == null) { report = "no Digger or Foundry of ours"; yield break; }
                player.Selection.Clear();
                yield return Measure();
                sb.AppendLine($"nothing selected: {avg:0} B a call on average, {peak:0} at most (the HUD's Update alone)");
                player.SelectOnly(digger);
                yield return Measure();
                sb.AppendLine($"a Digger: {avg:0} B, {peak:0} at most");
                foundry.queue.Clear();
                player.SelectOnly(foundry);
                yield return Measure();
                sb.AppendLine($"a Foundry, nothing queued: {avg:0} B, {peak:0} at most");
                world.factions[player.team].ore += 3000;
                for (int i = 0; i < 5; i++) world.CmdTrain(foundry, UnitType.Worker);
                int queued = foundry.queue.Count;
                yield return Measure();
                sb.AppendLine($"a Foundry, {queued} queued ({foundry.queue.Count} left after): {avg:0} B, {peak:0} at most");
                // Still right: one slot per queued unit, the bar moving, rebuilt when one comes out.
                var hud = Object.FindAnyObjectByType<StarForge.UI.HUDController>();
                var row = typeof(StarForge.UI.HUDController).GetField("queue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(hud) as UnityEngine.UIElements.VisualElement;
                if (row != null)
                {
                    int c0 = row.childCount, q0 = foundry.queue.Count;
                    float v0 = UnityEngine.UIElements.UQueryExtensions.Q<StarForge.UI.BarElement>(row)?.Value ?? -1f;
                    yield return Wait(1.5f);
                    float v1 = UnityEngine.UIElements.UQueryExtensions.Q<StarForge.UI.BarElement>(row)?.Value ?? -1f;
                    sb.AppendLine($"  queue row: {c0} slots for {q0} queued, bar {v0:0.00} -> {v1:0.00} over 1.5 s");
                    for (float w = 0f; w < 40f && foundry.queue.Count >= q0; w += 0.25f) yield return Wait(0.25f);
                    yield return null;
                    yield return null;
                    sb.AppendLine($"  once one came out: {row.childCount} slots for {foundry.queue.Count} queued, bar " +
                                  $"{UnityEngine.UIElements.UQueryExtensions.Q<StarForge.UI.BarElement>(row)?.Value ?? -1f:0.00}");
                }
                foundry.queue.Clear();
                // A Mech Bay researching: the row is its bar and the upgrade's name.
                var bay = Put(UnitType.MechBay, player.team, foundry.pos + new Vector2(22f, 0f));
                world.CompleteConstruction(bay);   // registered as its side's bay, as a finished build is (research ticks only there)
                world.factions[player.team].ore += 5000;
                bool started = false;
                for (int u = 0; u < (int)MechUpgrade.Count && !started; u++)
                    if ((MechUpgrade)u != MechUpgrade.Hardpoint) started = world.CmdMechUpgrade(bay, (MechUpgrade)u);
                player.SelectOnly(bay);
                yield return Measure();
                sb.AppendLine($"a Mech Bay researching ({(started ? "started" : "refused")}): {avg:0} B, {peak:0} at most");
                if (row != null && started)
                {
                    float r0 = UnityEngine.UIElements.UQueryExtensions.Q<StarForge.UI.BarElement>(row)?.Value ?? -1f;
                    int rc = row.childCount;
                    yield return Wait(2f);
                    float r1 = UnityEngine.UIElements.UQueryExtensions.Q<StarForge.UI.BarElement>(row)?.Value ?? -1f;
                    var F = world.factions[player.team];
                    F.researchLeft = 0.01f;
                    for (float w = 0f; w < 5f && F.researching >= 0; w += 0.1f) yield return Wait(0.1f);
                    yield return null;
                    yield return null;
                    sb.AppendLine($"  research row: {rc} row(s), bar {r0:0.00} -> {r1:0.00} over 2 s; once done: {row.childCount} row(s), researching {F.researching}");
                }
                player.Selection.Clear();
            }
            finally { ReleaseStatics(); }
            report = sb.ToString();
        }

        /// <summary>180 frames after 10 to settle. The whole frame's count is mostly the Editor's
        /// own (28-60 KB a frame, nothing selected above a Foundry), so the HUD is measured
        /// alone: its Update called once more each frame, the bytes this thread allocated
        /// across that call (the call itself, by reflection, is in the nothing-selected line).</summary>
        IEnumerator Measure()
        {
            var hud = Object.FindAnyObjectByType<StarForge.UI.HUDController>();
            var update = typeof(StarForge.UI.HUDController).GetMethod("Update",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            for (int i = 0; i < 10; i++) yield return null;
            double sum = 0; peak = 0f;
            int n = 0;
            for (int i = 0; i < 180; i++)
            {
                yield return null;
                if (hud == null || update == null) continue;
                // Mono's heap in use across the call (GC.GetAllocatedBytesForCurrentThread reads
                // 0 on Mono); a collection inside the call shows as a drop and is skipped.
                long before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                update.Invoke(hud, null);
                long d = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - before;
                if (d < 0) continue;
                sum += d;
                n++;
                peak = Mathf.Max(peak, d);
            }
            avg = n > 0 ? (float)(sum / n) : -1f;
            if (hud == null || update == null) avg = peak = -1f;
        }
    }

    public sealed class HoldPauseTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var boot = GameBootstrap.Instance;
            if (boot.State != MatchState.Playing) { report = $"match is {boot.State}, not Playing"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            boot.TogglePause();
            yield return new WaitForSecondsRealtime(0.3f);
            ReleaseStatics();
            sb.AppendLine($"paused during a hold: {boot.State}, timeScale {Time.timeScale:0.##} after the release (want Paused, 0)");
            HoldStatics();
            boot.TogglePause();
            yield return new WaitForSecondsRealtime(0.3f);
            ReleaseStatics();
            sb.AppendLine($"resumed during a hold: {boot.State}, timeScale {Time.timeScale:0.##} after the release (want Playing, {boot.Speed:0.##})");
            Time.timeScale = 6f;   // someone else's speed (a duel's 6x) at the hold
            HoldStatics();
            yield return new WaitForSecondsRealtime(0.3f);
            ReleaseStatics();
            sb.AppendLine($"held at someone else's 6x: timeScale {Time.timeScale:0.##} after the release (want 6)");
            Time.timeScale = boot.Speed;
            HoldStatics();
            boot.Surrender();
            yield return new WaitForSecondsRealtime(0.3f);
            ReleaseStatics();
            sb.AppendLine($"match ended during a hold: {boot.State}, timeScale {Time.timeScale:0.##} after the release (want Ended, 1; restart play after this)");
            report = sb.ToString();
        }
    }

    public sealed class DesertersTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var ai = GameBootstrap.Instance.AI;
            if (ai == null) { report = "no opponent AI"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(4242);
            HoldStatics();
            try
            {
                // Theirs in sight of the AI.
                if (!FindField(rng, out var b, out var d)) { report = "no open field found"; yield break; }
                var group = new List<Unit>();
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 2.39996f, r = 1.1f * Mathf.Sqrt(i + 0.5f);
                    group.Add(Put(UnitType.Trooper, 0, b + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r));
                }
                int broke = world.Desert(0, b, group, 6);
                var eye = Put(UnitType.Trooper, 1, b + d * 12f);
                world.CmdHold(new List<Unit> { eye });
                yield return Wait(0.5f);
                ai.Perception.Update(0.1f);
                int seen = 0;
                foreach (var u in group) if (Unit.Live(u) && u.deserted && world.Visible(1, u.pos)) seen++;
                int remembered = 0;
                foreach (var r in ai.Perception.Enemies) if (r.type == UnitType.Trooper && (r.pos - b).magnitude < 15f) remembered++;
                sb.AppendLine($"their deserters in sight: {broke} broke, {seen} in team 1's sight; Perception remembers {remembered} Troopers there, enemy army {ai.Perception.Snap.eArmyValue:0}");

                // Ours taken off outright (no Kill): the prune is all that sees them go.
                var M = world.MoraleOf(0);
                float goneBefore = M.desertersGoneAt;
                foreach (var u in group) Remove(u);
                Remove(eye);
                yield return Wait(0.5f);
                sb.AppendLine($"  taken off outright: {M.deserters.Count} on the list, desertersGoneAt {(M.desertersGoneAt > goneBefore ? $"set at {M.desertersGoneAt:0} s" : $"unchanged ({M.desertersGoneAt:0})")}");

                // Four from the middle of a knot of 24 holding riflemen, at 8x.
                if (!FindField(rng, out var kb, out _)) { report = sb + "no second field"; yield break; }
                var knot = new List<Unit>();
                for (int i = 0; i < 24; i++)
                {
                    float a = i * 2.39996f, r = 0.9f * Mathf.Sqrt(i + 0.5f);
                    knot.Add(Put(UnitType.Trooper, 0, kb + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r));
                }
                world.CmdHold(knot);
                yield return Wait(1f);
                var inner = knot.GetRange(0, 4);
                int ran = world.Desert(0, kb, inner, 4);
                Time.timeScale = 8f;
                var settled = new Dictionary<Unit, float>();
                float t0 = world.time;
                while (world.time - t0 < 60f && settled.Count < ran)
                {
                    yield return null;
                    foreach (var u in inner)
                        if (Unit.Live(u) && u.deserted && !u.fleeing && !settled.ContainsKey(u)) settled[u] = world.time - t0;
                }
                Vector2 camp = world.MoraleOf(0).camp;
                var where = new List<string>();
                int inCamp = 0;
                foreach (var u in inner)
                {
                    if (!Unit.Live(u)) { where.Add("dead"); continue; }
                    if (!settled.TryGetValue(u, out float at)) { where.Add($"still fleeing, {(u.pos - camp).magnitude:0} m from camp"); continue; }
                    float fromCamp = (u.pos - camp).magnitude;
                    if (fromCamp < 10f) inCamp++;
                    where.Add($"{at:0} s, {fromCamp:0} m from camp, {(u.pos - kb).magnitude:0} m from the knot");
                }
                sb.AppendLine($"knot of 24 at 8x, {ran} broke from its middle: {inCamp} settled in the camp; {string.Join("; ", where)}");

                // One settled whose spot cannot be reached: our Foundry's middle (inside its footprint).
                Unit foundry = null;
                foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && u.Type == UnitType.Foundry) { foundry = u; break; }
                Unit one = null;
                foreach (var u in inner) if (Unit.Live(u) && u.deserted && !u.fleeing) { one = u; break; }
                if (foundry != null && one != null)
                {
                    one.orderPos = foundry.pos;
                    float t1 = world.time;
                    yield return Wait(40f);
                    bool gaveUp = (one.orderPos - foundry.pos).sqrMagnitude > 0.01f;
                    sb.AppendLine($"settled one sent a spot inside our Foundry: 40 s later {(gaveUp ? $"took a new spot {(one.orderPos - one.pos).magnitude:0.0} m from where it stands, {(one.pos - foundry.pos).magnitude:0} m from the Foundry's middle" : $"still after the old one, {one.Dist(foundry):0} m from it")}");
                }
                ClearSpawned();
            }
            finally { ReleaseStatics(); }
            report = sb.ToString();
        }
    }

    public sealed class MineSoundTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var audio = Object.FindAnyObjectByType<AudioDirector>();
            if (audio == null || audio.bank.mine == null || audio.bank.mine.Length == 0) { report = "no AudioDirector or no mining takes"; yield break; }
            var takes = new HashSet<AudioClip>(audio.bank.mine);
            var sb = new StringBuilder();
            HoldStatics();
            try
            {
                if (world.time < 25f) yield return Wait(25f - world.time);
                for (int pass = 0; pass < 2; pass++)
                {
                    bool partLoad = pass == 1;
                    int frames = 0, audible = 0, mining = 0;
                    float t0 = world.time;
                    while (world.time - t0 < 20f)
                    {
                        yield return null;
                        frames++;
                        foreach (var u in world.units)
                        {
                            if (!Unit.Live(u) || u.team != 0 || u.Type != UnitType.Worker || u.order != Order.Harvest) continue;
                            if (partLoad && u.carrying < 4) u.carrying = 4;   // a part load in the pack while it mines
                            if (u.working) mining++;
                        }
                        foreach (var src in Object.FindObjectsByType<AudioSource>())
                            if (src.isPlaying && src.clip != null && takes.Contains(src.clip)) { audible++; break; }
                    }
                    sb.AppendLine($"{(partLoad ? "part load" : "empty load")}: a mining take playing in {100f * audible / Mathf.Max(1, frames):0}% of {frames} frames ({mining / Mathf.Max(1, frames):0.0} Diggers mining a frame)");
                }
            }
            finally { ReleaseStatics(); }
            report = sb.ToString();
        }
    }

    public sealed class PurgeEndTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var boot = GameBootstrap.Instance;
            report = "staging MoraleTrials.Punish: " + MoraleTrials.Punish();
            float waited = 0f;
            while (waited < 120f && !world.Purging(0)) { yield return new WaitForSecondsRealtime(0.25f); waited += 0.25f; }
            if (!world.Purging(0)) { report = "the purge never came on in 120 s"; yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            var brain = boot.MechBrains[0];
            string before = $"purge on, punishing {brain.Punishing}, {world.MoraleOf(0).deserters.Count} deserters";
            boot.Surrender();
            yield return new WaitForSecondsRealtime(2f);
            report = $"{before}; 2 s after the match ended: purge {(world.Purging(0) ? "on" : "off")}, punishing {brain.Punishing}, " +
                     $"{world.MoraleOf(0).deserters.Count} deserters, {boot.State} (restart play now)";
        }
    }

    public sealed class DeserterJamTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(7311);
            HoldStatics();
            try
            {
                world.MoraleOf(0);
                Vector2 c = Vector2.zero;
                bool ok = false;
                for (int i = 0; i < 12 && !ok; i++)
                {
                    if (!FindField(rng, out c, out _)) break;
                    ok = (c - map.StartPos(0)).magnitude > 40f;
                }
                if (!ok) { report = "no open field 40+ m from our base"; yield break; }
                var d = Put(UnitType.Trooper, 0, c);
                world.CmdHold(new List<Unit> { d });
                yield return Wait(1.5f);
                int ran = world.Desert(0, c, new List<Unit> { d }, 1);
                // Held where it stands on a good path: its agent cannot pick up speed. Riflemen
                // round it gave way and non-carving posts let it through (Trooper avoidance steers
                // round), so neither staged a jam.
                if (d.agent != null) { d.agent.acceleration = 0f; d.agent.velocity = Vector3.zero; }
                Vector2 camp = world.MoraleOf(0).camp;
                Time.timeScale = 8f;
                float t0 = world.time, settledAt = -1f;
                int asks = 0;
                bool wasPending = false;
                while (world.time - t0 < 60f)
                {
                    yield return null;
                    if (!Unit.Live(d)) break;
                    bool pending = d.agent != null && d.agent.enabled && d.agent.pathPending;
                    if (pending && !wasPending) asks++;
                    wasPending = pending;
                    if (!d.fleeing) { settledAt = world.time - t0; break; }
                }

                sb.AppendLine($"held still on its path (no acceleration), {(c - camp).magnitude:0} m from the camp: {ran} deserted; " +
                              (settledAt >= 0f ? $"settled after {settledAt:0.0} s, {(d.pos - c).magnitude:0.0} m from where it stood" : $"still fleeing after 60 s, {(d.pos - c).magnitude:0.0} m from where it stood") +
                              $"; path requests seen {asks}");
                ClearSpawned();
            }
            finally { ReleaseStatics(); }
            report = sb.ToString();
        }
    }

    public sealed class CampDriftTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(5519);
            HoldStatics();
            try
            {
                if (!FindField(rng, out var b, out _)) { report = "no open field found"; yield break; }
                var group = new List<Unit>();
                for (int i = 0; i < 3; i++) group.Add(Put(UnitType.Trooper, 0, b + new Vector2(i * 1.5f, 0f)));
                yield return Wait(0.5f);
                world.Desert(0, b, group, 3);
                foreach (var u in group) u.hp = 1e4f;
                Time.timeScale = 8f;
                float t0 = world.time;
                while (world.time - t0 < 90f)
                {
                    yield return null;
                    bool all = true;
                    foreach (var u in group) if (Unit.Live(u) && u.fleeing) all = false;
                    if (all) break;
                }
                Vector2 camp = world.MoraleOf(0).camp;
                var spot = new Dictionary<Unit, Vector2>();
                foreach (var u in group) if (Unit.Live(u) && !u.fleeing) spot[u] = u.orderPos;
                if (spot.Count == 0) { report = "none settled in 90 s"; yield break; }
                Time.timeScale = 3f;
                int raids = 0;
                for (float t = 0f; t < 60f; t += 4f)
                {
                    Vector2 dir = Random.insideUnitCircle.normalized;
                    var raider = Put(UnitType.Trooper, 1, world.NearestReachable(camp, camp + dir * 22f));
                    world.CmdHold(new List<Unit> { raider });
                    Unit hit = null;
                    foreach (var u in spot.Keys) if (Unit.Live(u)) { hit = u; break; }
                    if (hit != null) { world.Damage(hit, 1f, hit.Ground, raider); raids++; }
                    yield return Wait(1.5f);
                    Remove(raider);
                    yield return Wait(2.5f);
                }
                yield return Wait(10f);
                float drift = 0f, away = 0f;
                foreach (var kv in spot)
                {
                    if (!Unit.Live(kv.Key)) continue;
                    drift = Mathf.Max(drift, (kv.Key.orderPos - kv.Value).magnitude);
                    away = Mathf.Max(away, (kv.Key.pos - kv.Value).magnitude);
                }
                sb.AppendLine($"{spot.Count} settled in the camp, {raids} raids 4 s apart: spots moved at most {drift:0.0} m; 10 s after, at most {away:0.0} m from their first spots");
                ClearSpawned();
            }
            finally { ReleaseStatics(); }
            report = sb.ToString();
        }
    }

    public sealed class HiddenLastTrial : BugTrial
    {
        public static bool FarFromAI;

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            if (Commander.Suspended) { report = "the opponent AI is silenced: let the trial holding it finish first"; yield break; }
            // The spot furthest from every ore field and both base sites that takes a Bunkhouse.
            Vector2 a = map.StartPos(0), b = map.StartPos(1), hide = a;
            float best = -1f, size = world.MapSize;
            for (float x = 12f; x < size - 12f; x += 6f)
                for (float z = 12f; z < size - 12f; z += 6f)
                {
                    var p = new Vector2(x, z);
                    float d = Mathf.Min((p - a).magnitude, (p - b).magnitude);
                    foreach (var n in world.units)
                        if (Unit.Live(n) && n.Type == UnitType.Ore) d = Mathf.Min(d, (n.pos - p).magnitude);
                    if (FarFromAI) { if (d < 40f) continue; d = (p - b).magnitude; }
                    if (d <= best || !world.CanPlace(UnitType.Bunkhouse, p)) continue;
                    var q = world.NearestReachable(b, p);
                    if ((q - p).sqrMagnitude > 6f * 6f) continue;
                    best = d; hide = p;
                }
            if (best < 0f) { report = "no hiding place found"; yield break; }
            var hidden = world.Spawn(UnitType.Bunkhouse, 0, hide, true);
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 0 && u != hidden && u.Type != UnitType.Ore) world.Damage(u, 1e7f, u.pos, null);
            for (int i = 0; i < 12; i++) world.Spawn(UnitType.Trooper, 1, b + new Vector2((i % 4) * 2f - 3f, (i / 4) * 2f + 8f), true);
            world.factions[1].ore += 3000;
            float t0 = world.time, seenAt = -1f;
            Time.timeScale = 8f;
            while (world.time - t0 < 900f && world.winner < 0)
            {
                yield return null;
                if (seenAt < 0f && world.Visible(1, hide)) seenAt = world.time - t0;
            }
            Time.timeScale = 1f;
            var ai = GameBootstrap.Instance.AI;
            report = $"hidden Bunkhouse {(FarFromAI ? $"{best:0} m from the AI's base" : $"{best:0} m from the nearest ore field or base site")}, ({hide.x:0},{hide.y:0}); " +
                     (seenAt >= 0f ? $"first seen by the AI after {seenAt:0} s; " : "never seen by the AI; ") +
                     (world.winner >= 0 ? $"match over after {world.time - t0:0} s, winner {world.winner}" : $"no winner after {world.time - t0:0} s (Bunkhouse {(Unit.Live(hidden) ? "standing" : "down")})") +
                     $"; AI waves {ai.Dbg.waves}, last {ai.Dbg.wave} (restart play now)";
        }
    }

    public sealed class MinedOutTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            try
            {
                if (world.time < 20f) yield return Wait(20f - world.time);
                var F = world.factions[0];
                var diggers = new List<Unit>();
                foreach (var u in world.units)
                    if (Unit.Live(u) && u.team == 0 && u.Type == UnitType.Worker) { diggers.Add(u); u.carrying = 4; if (u.order != Order.Harvest) world.CmdHarvest(new List<Unit> { u }, world.NearestFreeNode(u.pos, 0)); }
                int bank = F.ore;
                var ore = new List<Unit>();
                foreach (var n in world.units) if (Unit.Live(n) && n.Type == UnitType.Ore) ore.Add(n);
                foreach (var n in ore) world.Deplete(n);
                yield return Wait(30f);
                int held = 0, idleHolding = 0;
                foreach (var u in diggers) if (Unit.Live(u)) { held += u.carrying; if (u.order == Order.Idle && u.carrying > 0) idleHolding++; }
                sb.AppendLine($"{ore.Count} ore nodes gone with {diggers.Count} Diggers carrying 4 each: 30 s later {F.ore - bank} ore brought in, {held} still carried, {idleHolding} standing idle with a load");
            }
            finally { ReleaseStatics(); }
            report = sb.ToString();
        }
    }

    public sealed class ProngHomeTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            if (Commander.Suspended) { report = "the opponent AI is silenced: let the trial holding it finish first"; yield break; }
            var ai = GameBootstrap.Instance.AI;
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var prongF = typeof(Commander).GetField("prong", bf);
            if (prongF == null) { report = "no prong field"; yield break; }
            var prong = (List<Unit>)prongF.GetValue(ai);
            bool eval = AIEvalRunner.Pending;   // inside an evaluation game: its speed is its own
            if (!eval)
            {
                // Out of an evaluation: make a prong likely -- one splits off a wave only with army to
                // spare (1.6x the push threshold) and then at 0.25 + 0.5 x its taste for raids.
                ai.Personality.raidTaste = 1f;
                Vector2 h = map.StartPos(1);
                for (int i = 0; i < 30; i++) world.Spawn(UnitType.Trooper, 1, world.NearestReachable(h, h + new Vector2((i % 6) * 2f - 5f, 10f + (i / 6) * 2f)), true);
                Time.timeScale = 8f;
            }
            world.factions[1].ore += 4000;
            float t0 = world.time;
            if (!eval)
            {
                // Splitting one needs two targets 25+ m apart, which a lone starting base does not
                // give (six tries, no prong): once a wave is on, four of it are made its prong, as
                // StartWave does (out of main, prongOn, prongAt 60 m off, ordered there). Their base
                // is made to hold, or the wave ended the match first.
                foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && u.def.building) u.hp = 1e6f;
                var waveOnF = typeof(Commander).GetField("waveOn", bf);
                while (world.time - t0 < 600f && world.winner < 0 && !(bool)waveOnF.GetValue(ai)) yield return null;
                var mainL = (List<Unit>)typeof(Commander).GetField("main", bf).GetValue(ai);
                // Away from home first: staging by its base, the army there beat the raid on the spot
                // and nothing was called home.
                Vector2 hm = map.StartPos(1);
                for (float w = 0f; w < 200f && world.winner < 0; w += 0.5f)
                {
                    Vector2 mc = Vector2.zero; int k = 0;
                    foreach (var u in mainL) if (Unit.Live(u)) { mc += u.pos; k++; }
                    if (k > 0 && (mc / k - hm).magnitude > 75f) break;
                    yield return Wait(0.5f);
                }
                var picked = new List<Unit>();
                for (int i = mainL.Count - 1; i >= 0 && picked.Count < 4; i--)
                    if (Unit.Live(mainL[i]) && mainL[i].Type != UnitType.Mauler) { picked.Add(mainL[i]); mainL.RemoveAt(i); }
                prong.Clear();
                prong.AddRange(picked);
                Vector2 c = Vector2.zero;
                foreach (var u in picked) c += u.pos;
                c /= Mathf.Max(1, picked.Count);
                Vector2 side = new Vector2(-(map.StartPos(0) - c).y, (map.StartPos(0) - c).x).normalized;
                Vector2 prongAt = world.NearestReachable(c, c + side * 60f);
                typeof(Commander).GetField("prongOn", bf).SetValue(ai, prong.Count >= 2);
                typeof(Commander).GetField("prongAt", bf).SetValue(ai, prongAt);
                world.CmdMove(picked, prongAt, true);
            }
            while (world.time - t0 < 1200f && world.winner < 0 && prong.Count < 2) yield return null;
            if (prong.Count < 2) { if (!eval) Time.timeScale = 1f; report = $"no prong of 2+ in {world.time - t0:0} s (winner {world.winner})"; yield break; }
            yield return Wait(3f);   // its order given and on its way
            if (prong.Count < 2) { if (!eval) Time.timeScale = 1f; report = "the prong was folded back within 3 s"; yield break; }
            var those = new List<Unit>(prong);
            var mainF = typeof(Commander).GetField("main", bf);
            var rest = new List<Unit>();
            if (mainF != null) foreach (var u in (List<Unit>)mainF.GetValue(ai)) if (Unit.Live(u) && !those.Contains(u)) rest.Add(u);
            Vector2 home = map.StartPos(1);
            Vector2 raidAt = world.NearestReachable(home, home + new Vector2(0f, -18f));
            var fromP = new Dictionary<Unit, float>();
            foreach (var u in those) fromP[u] = (u.pos - raidAt).magnitude;
            // A raid on its base, worth more than it lets pass.
            var raiders = new List<Unit>();
            for (int i = 0; i < 14; i++) raiders.Add(world.Spawn(UnitType.Trooper, 0, world.NearestReachable(home, raidAt + new Vector2((i % 5) * 2f - 4f, -(i / 5) * 2f)), true));
            // Standing where they are: a raid that walks about moves the point it is met at, and the
            // order goes out again on a later pass anyway (it reached the prong that way, 3 of 3 runs).
            world.CmdHold(raiders);
            // Ordered to the raid at any moment of the next 25 s (once it is beaten the army is sent
            // on, so a look at the end alone missed the call home).
            var sent = new HashSet<Unit>();
            var closest = new Dictionary<Unit, float>();
            for (float t = 0f; t < 25f; t += 0.25f)
            {
                yield return Wait(0.25f);
                foreach (var u in those) Sample(u);
                foreach (var u in rest) Sample(u);
            }
            void Sample(Unit u)
            {
                if (!Unit.Live(u)) return;
                if ((u.orderPos - raidAt).magnitude < 25f && (u.order == Order.AttackMove || u.order == Order.Move || u.order == Order.Attack)) sent.Add(u);
                float d = (u.pos - raidAt).magnitude;
                if (!closest.TryGetValue(u, out float c) || d < c) closest[u] = d;
            }
            if (!eval) Time.timeScale = 1f;
            report = $"prong of {those.Count} at {world.time - t0:0} s, {Avg(fromP):0} m from the raid on average when it came; in the next 25 s: prong {Toward(those, sent, closest, fromP)}; " +
                     $"rest of the army ({rest.Count}) {Toward(rest, sent, closest, null)}; prong list now {prong.Count}";
        }

        static string Toward(List<Unit> us, HashSet<Unit> sent, Dictionary<Unit, float> closest, Dictionary<Unit, float> from)
        {
            int n = 0, s = 0, closed = 0;
            foreach (var u in us)
            {
                if (!closest.ContainsKey(u)) continue;
                n++;
                if (sent.Contains(u)) s++;
                if (from != null && from.TryGetValue(u, out float f0) && closest[u] < f0 - 10f) closed++;
            }
            return from != null ? $"{s} of {n} ordered to the raid, {closed} closed 10+ m on it" : $"{s} of {n} ordered to the raid";
        }

        static float Avg(Dictionary<Unit, float> d) { float s = 0f; foreach (var v in d.Values) s += v; return d.Count > 0 ? s / d.Count : 0f; }
    }

    public sealed class BayPingTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(2207);
            HoldStatics();
            Unit mech = null;
            try
            {
                if (!FindField(rng, out var b, out var d)) { report = "no open field found"; yield break; }
                mech = world.TrialMech(0, MechParts.Generate(4242u), b, 0f, false, true);
                yield return Wait(1f);
                var pings = new List<(string text, Vector3 pos)>();
                ListenWhileHeld(e => { if (e.kind == GameEventKind.MechAdvice && e.team == 0) pings.Add((e.text, e.pos)); });
                foreach (int side in new[] { 1, 0 })
                {
                    if (!FindField(rng, out var at, out _)) continue;
                    var bay = Put(UnitType.MechBay, side, at);
                    yield return Wait(0.5f);
                    int before = pings.Count;
                    Vector3 bayAt = bay.Ground;
                    world.Damage(bay, 1e7f, bay.pos, null);
                    yield return Wait(1f);
                    if (pings.Count == before) { sb.AppendLine($"{(side == 1 ? "their" : "our")} bay down: no comms line"); continue; }
                    var p = pings[pings.Count - 1];
                    var flat = new Vector2(p.pos.x - bayAt.x, p.pos.z - bayAt.z);
                    sb.AppendLine($"{(side == 1 ? "their" : "our")} bay down at ({bayAt.x:0},{bayAt.z:0}): \"{p.text.Substring(0, Mathf.Min(30, p.text.Length))}...\" pinged at ({p.pos.x:0},{p.pos.z:0}), {flat.magnitude:0} m from the bay");
                }
            }
            finally
            {
                if (Unit.Live(mech)) mech.BeginDeath();
                ReleaseStatics();
            }
            report = sb.ToString();
        }
    }

    public sealed class MechWreckMatTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            HoldStatics();
            try
            {
                Vector2 at = world.NearestReachable(map.StartPos(0), map.StartPos(0) + new Vector2(14f, 14f));
                var mech = world.TrialMech(0, MechParts.Generate(777u), at, 0f, false);
                yield return Wait(2f);
                world.Damage(mech, 1e7f, mech.pos, null);
                yield return Wait(3f);
                var names = new List<string>();
                foreach (var wreck in Object.FindObjectsByType<MechWreck>())
                    foreach (var box in wreck.GetComponentsInChildren<BoxCollider>())
                        names.Add(box.sharedMaterial == null ? "none (destroyed or unset)" : box.sharedMaterial.name);
                report = names.Count == 0 ? "no Mech wreck found" : $"Mech wreck box material: {string.Join(", ", names)}";
            }
            finally { ReleaseStatics(); }
        }
    }

    public sealed class PenJamTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(6151);
            HoldStatics();
            var walls = new List<GameObject>();
            try
            {
                Vector2 c = Vector2.zero, d = Vector2.right;
                bool ok = false;
                for (int i = 0; i < 12 && !ok; i++)
                {
                    if (!FindField(rng, out c, out _)) break;
                    ok = (c - map.StartPos(0)).magnitude > 45f;
                }
                if (!ok) { report = "no open field 45+ m from our base"; yield break; }
                // The gap is the pen's only way out, so whichever way it faces the path runs through it.
                d = (map.StartPos(0) - c).normalized;
                Vector2 side = new Vector2(-d.y, d.x);
                const float half = 4f, gap = 3f, thick = 0.6f;
                void Wall(Vector2 mid, Vector2 along, float len)
                {
                    var go = new GameObject("PenWall");
                    go.transform.position = map.Ground(mid) + Vector3.up * 1f;
                    go.transform.rotation = Quaternion.LookRotation(new Vector3(along.x, 0f, along.y));
                    var ob = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                    ob.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                    ob.size = new Vector3(thick, 3f, len);
                    ob.carving = true;
                    ob.carveOnlyStationary = false;
                    walls.Add(go);
                }
                Wall(c - d * half, side, 2f * half + thick);                 // back
                Wall(c + side * half, d, 2f * half + thick);                 // sides
                Wall(c - side * half, d, 2f * half + thick);
                float seg = half - gap * 0.5f;                               // front, with the gap in its middle
                Wall(c + d * half + side * (gap * 0.5f + seg * 0.5f), side, seg + thick);
                Wall(c + d * half - side * (gap * 0.5f + seg * 0.5f), side, seg + thick);
                var plug = Put(UnitType.Mauler, 0, c + d * (half + 0.2f));
                world.CmdHold(new List<Unit> { plug });
                var t = Put(UnitType.Trooper, 0, c);
                world.CmdHold(new List<Unit> { t });
                yield return Wait(2f);
                int ran = world.Desert(0, c, new List<Unit> { t }, 1);
                Time.timeScale = 8f;
                float t0 = world.time, settledAt = -1f;
                int asks = 0; bool was = false; float maxMoved = 0f;
                // Corner changes while it stands still (moved under 1 m over the last 2 s): each one
                // restarts the stall's best, so a corner flickering in a jam could keep it fleeing.
                var cornerF = typeof(Unit).GetField("fleeCorner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Vector2 lastCorner = (Vector2)cornerF.GetValue(t);
                var trail = new Queue<(float time, Vector2 pos)>();
                int stillRebases = 0; float stillTime = 0f;
                while (world.time - t0 < 90f)
                {
                    yield return null;
                    if (!Unit.Live(t)) break;
                    trail.Enqueue((world.time, t.pos));
                    while (trail.Count > 0 && world.time - trail.Peek().time > 2f) trail.Dequeue();
                    bool still = world.time - t0 > 2f && trail.Count > 0 && (t.pos - trail.Peek().pos).magnitude < 1f;
                    var corner = (Vector2)cornerF.GetValue(t);
                    if (still) stillTime += Time.deltaTime > 0f ? Mathf.Min(0.1f, Time.deltaTime * Time.timeScale) : 0f;
                    if ((corner - lastCorner).sqrMagnitude > 1f) { if (still) stillRebases++; lastCorner = corner; }
                    bool pending = t.agent != null && t.agent.enabled && t.agent.pathPending;
                    if (pending && !was) asks++;
                    was = pending;
                    maxMoved = Mathf.Max(maxMoved, (t.pos - c).magnitude);
                    if (!t.fleeing) { settledAt = world.time - t0; break; }
                }
                bool inPen = (t.pos - c).magnitude < half;
                Vector2 campAt = world.MoraleOf(0).camp;
                sb.AppendLine($"penned {(c - campAt).magnitude:0} m from the camp, its gap held by a Mauler ({(plug.pos - c).magnitude:0.0} m from the middle at the end): {ran} deserted; " +
                              (settledAt >= 0f ? $"settled after {settledAt:0.0} s" : "still fleeing after 90 s") +
                              $", {(inPen ? "in the pen" : "out of the pen")} ({(t.pos - c).magnitude:0.0} m from where it stood, at most {maxMoved:0.0}); path requests seen {asks}; corner changes while it stood still {stillRebases} in {stillTime:0.0} s");
                ClearSpawned();
            }
            finally
            {
                foreach (var w in walls) if (w != null) Object.Destroy(w);
                ReleaseStatics();
            }
            report = sb.ToString();
        }
    }

    public sealed class FleeTraceTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var rng = new System.Random(8821);
            var stallF = typeof(Unit).GetField("fleeStall", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            HoldStatics();
            var walls = new List<GameObject>();
            try
            {
                for (int field = 0; field < 3; field++)
                {
                    Vector2 c = Vector2.zero; bool ok = false;
                    for (int i = 0; i < 12 && !ok; i++) { if (!FindField(rng, out c, out _)) break; ok = (c - map.StartPos(0)).magnitude > 60f; }
                    if (!ok) { sb.AppendLine($"field {field + 1}: none 60+ m from our base"); continue; }
                    var group = new List<Unit>();
                    var kinds = field == 2 ? new[] { UnitType.Trooper, UnitType.Mauler, UnitType.Skimmer } : new[] { UnitType.Trooper, UnitType.Trooper, UnitType.Trooper };
                    for (int i = 0; i < 3; i++) group.Add(Put(kinds[i], 0, c + new Vector2(i * 3f, 0f)));
                    yield return Wait(0.5f);
                    world.Desert(0, c, group, 3);
                    Vector2 camp = world.MoraleOf(0).camp;
                    Time.timeScale = 8f;
                    int ticks = 0, inf = 0; float maxStall = 0f, t0 = world.time; bool walled = false;
                    while (world.time - t0 < 120f)
                    {
                        yield return null;
                        bool any = false;
                        foreach (var u in group)
                        {
                            if (!Unit.Live(u) || !u.fleeing) continue;
                            any = true;
                            ticks++;
                            if (u.agent != null && u.agent.enabled && u.agent.hasPath && float.IsInfinity(u.agent.remainingDistance)) inf++;
                            maxStall = Mathf.Max(maxStall, (float)stallF.GetValue(u));
                        }
                        if (!any) break;
                        // The detour: a 40 m carving wall across their way, 20 m ahead of the first of them.
                        if (field == 1 && !walled && world.time - t0 > 3f)
                        {
                            walled = true;
                            Unit lead = null; foreach (var u in group) if (Unit.Live(u) && u.fleeing) { lead = u; break; }
                            if (lead != null)
                            {
                                Vector2 dir = (camp - lead.pos).normalized;
                                var go = new GameObject("DetourWall");
                                go.transform.position = map.Ground(lead.pos + dir * 20f) + Vector3.up;
                                go.transform.rotation = Quaternion.LookRotation(new Vector3(-dir.y, 0f, dir.x));
                                var ob = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                                ob.shape = UnityEngine.AI.NavMeshObstacleShape.Box; ob.size = new Vector3(0.8f, 3f, 40f);
                                ob.carving = true; ob.carveOnlyStationary = false;
                                walls.Add(go);
                            }
                        }
                    }
                    Time.timeScale = 3f;
                    var ends = new List<string>();
                    foreach (var u in group)
                    {
                        if (!Unit.Live(u)) { ends.Add("dead"); continue; }
                        float fc = (u.pos - camp).magnitude;
                        string reach = "";
                        if (fc >= 12f)
                        {
                            var path = new UnityEngine.AI.NavMeshPath();
                            UnityEngine.AI.NavMesh.CalculatePath(u.Ground, map.Ground(camp), u.agent != null ? u.agent.areaMask : UnityEngine.AI.NavMesh.AllAreas, path);
                            reach = path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete ? ", camp reachable" : ", camp NOT reachable";
                        }
                        ends.Add(u.fleeing ? $"still fleeing {fc:0} m out{reach}" : fc < 12f ? $"in camp ({fc:0} m)" : $"settled {fc:0} m out{reach}");
                    }
                    sb.AppendLine($"field {field + 1}{(field == 1 ? " (wall across its way at 3 s)" : field == 2 ? " (a Trooper, a Mauler, a Skimmer)" : "")}, {(c - camp).magnitude:0} m from the camp: way left unknown (Infinity) on {100f * inf / Mathf.Max(1, ticks):0}% of fleeing ticks; largest stall {maxStall:0.0} s; {string.Join(", ", ends)}");
                    foreach (var w in walls) if (w != null) Object.Destroy(w);
                    walls.Clear();
                    ClearSpawned();
                    yield return Wait(1f);
                }
            }
            finally
            {
                foreach (var w in walls) if (w != null) Object.Destroy(w);
                ReleaseStatics();
            }
            report = sb.ToString();
        }
    }

    public sealed class FuzzTrial : BugTrial
    {
        public static int Seed = 1;
        public static bool Lifecycle;
        bool life;
        readonly List<string> errors = new List<string>();
        int exceptions, errorLogs;

        void OnLog(string cond, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            if (type == LogType.Exception) exceptions++; else errorLogs++;
            string first = cond.Length > 160 ? cond.Substring(0, 160) : cond;
            string at = "";
            foreach (var line in stack.Split('\n')) if (line.Contains("StarForge")) { at = line.Trim(); break; }
            string key = first + " @ " + (at.Length > 140 ? at.Substring(0, 140) : at);
            if (errors.Count < 6 && !errors.Contains(key)) errors.Add(key);
        }

        void OnDisable() => Application.logMessageReceived -= OnLog;

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            if (Commander.Suspended) { report = "the opponent AI is silenced: let the trial holding it finish first"; yield break; }
            Application.logMessageReceived += OnLog;
            life = Lifecycle; Lifecycle = false;
            var boot = GameBootstrap.Instance;
            int pauses = 0, speeds = 0; bool surrendered = false;
            var rng = new System.Random(Seed);
            var seen = new List<Unit>();
            var actions = new Dictionary<string, int>();
            var offMesh = new Dictionary<Unit, float>();
            int negOre = 0, negSupply = 0, nanPos = 0, stuckOff = 0, peakUnits = 0;
            float size = world.MapSize, t0 = world.time, nextCheck = 0f;
            Time.timeScale = 8f;
            void Count(string k) { actions.TryGetValue(k, out int c); actions[k] = c + 1; }
            Vector2 RandPoint()
            {
                switch (rng.Next(6))
                {
                    case 0: return new Vector2((float)rng.NextDouble() * size * 1.4f - size * 0.2f, (float)rng.NextDouble() * size * 1.4f - size * 0.2f);   // off the map too
                    case 1: return new Vector2(rng.Next(2) * size, (float)rng.NextDouble() * size);   // an edge
                    case 2: return Vector2.zero;
                    default: return new Vector2((float)rng.NextDouble() * size, (float)rng.NextDouble() * size);
                }
            }
            Unit RandUnit()
            {
                if (seen.Count == 0) return null;
                return rng.Next(12) == 0 ? null : seen[rng.Next(seen.Count)];
            }
            List<Unit> RandSel()
            {
                var sel = new List<Unit>();
                int n = 1 + rng.Next(8);
                for (int i = 0; i < n; i++) { var u = RandUnit(); if (u != null) sel.Add(u); }
                return sel;
            }
            var types = (UnitType[])System.Enum.GetValues(typeof(UnitType));
            while (world.time - t0 < 300f)
            {
                yield return Wait(0.2f);
                if (life && boot != null)
                {
                    // Pause and resume (real seconds: the match clock stands while paused), the speed
                    // keys, and a surrender at 200 s with the commands going on behind the end screen.
                    if (rng.Next(60) == 0 && boot.State == MatchState.Playing)
                    {
                        boot.TogglePause(); pauses++;
                        for (int f = 0; f < 3 + rng.Next(10); f++)
                        {
                            try { world.CmdMove(RandSel(), RandPoint(), false); world.CmdTrain(RandUnit(), types[rng.Next(types.Length)]); } catch (System.Exception ex) { exceptions++; if (errors.Count < 6) errors.Add("paused: " + ex.GetType().Name + ": " + ex.Message); }
                            yield return null;
                        }
                        boot.TogglePause();
                        Time.timeScale = 8f;
                    }
                    if (rng.Next(40) == 0) { boot.SetSpeed(0.5f + (float)rng.NextDouble() * 2.5f); speeds++; Time.timeScale = 8f; }
                    if (!surrendered && world.time - t0 > 200f) { surrendered = true; boot.Surrender(); Time.timeScale = 8f; }
                }
                foreach (var u in world.units) if (u != null && !seen.Contains(u)) seen.Add(u);
                if (seen.Count > 4000) seen.RemoveRange(0, 1000);
                for (int k = 0; k < 3; k++)
                {
                    int a = rng.Next(16);
                    try
                    {
                        switch (a)
                        {
                            case 0: world.CmdMove(RandSel(), RandPoint(), rng.Next(2) == 0); Count("move"); break;
                            case 1: world.CmdAttack(RandSel(), RandUnit()); Count("attack"); break;
                            case 2: world.CmdHarvest(RandSel(), RandUnit()); Count("harvest"); break;
                            case 3: world.CmdStop(RandSel()); Count("stop"); break;
                            case 4: world.CmdHold(RandSel()); Count("hold"); break;
                            case 5: world.CmdRally(RandSel(), RandPoint()); Count("rally"); break;
                            case 6: world.CmdBuild(RandSel(), types[rng.Next(types.Length)], RandPoint()); Count("build"); break;
                            case 7: { var b = RandUnit(); if (b != null) world.CmdTrain(b, types[rng.Next(types.Length)]); Count("train"); break; }
                            case 8: { var b = RandUnit(); if (b != null) { if (rng.Next(2) == 0) world.CmdCancelTrain(b); else world.CmdCancelConstruction(b); } Count("cancel"); break; }
                            case 9: world.CmdSmart(RandSel(), RandPoint(), RandUnit()); Count("smart"); break;
                            case 10: { var m = world.MechOf(rng.Next(2)); if (m != null) { int c = rng.Next(3); if (c == 0) world.CmdMechMove(m, RandPoint()); else if (c == 1) world.CmdMechFocus(m, RandUnit()); else world.CmdMechHalt(m); } Count("mech"); break; }
                            case 11: { var b = RandUnit(); if (b != null) { if (rng.Next(2) == 0) world.CmdMechUpgrade(b, (MechUpgrade)rng.Next((int)MechUpgrade.Count)); else world.CmdCancelMechUpgrade(b); } Count("upgrade"); break; }
                            case 12: { if (rng.Next(6) == 0) { var sel = RandSel(); if (sel.Count > 0) world.Desert(sel[0].team, sel[0].pos, sel, 1 + rng.Next(3)); Count("desert"); } break; }
                            case 13: { var u = RandUnit(); if (u != null && rng.Next(3) == 0) { world.Damage(u, rng.Next(2) == 0 ? 1e7f : (float)rng.NextDouble() * 200f, u.pos, RandUnit()); Count("damage"); } break; }
                            case 14: { if (rng.Next(20) == 0) { world.CheatOre(rng.Next(2), 3000); Count("ore"); } break; }
                            default: { var u = RandUnit(); if (u != null) world.CmdSmart(new List<Unit> { u }, u.pos, u); Count("self-smart"); break; }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        exceptions++;
                        string key = $"{ex.GetType().Name}: {ex.Message} (action {a})";
                        if (errors.Count < 6 && !errors.Contains(key)) errors.Add(key + " @ " + ex.StackTrace?.Split('\n')[0]);
                    }
                }
                if (world.time >= nextCheck)
                {
                    nextCheck = world.time + 1f;
                    for (int t = 0; t < 2; t++)
                    {
                        if (world.factions[t].ore < 0) negOre++;
                        if (world.factions[t].supplyUsed < 0 || world.factions[t].supplyCap < 0) negSupply++;
                    }
                    int live = 0;
                    foreach (var u in world.units)
                    {
                        if (!Unit.Live(u)) continue;
                        live++;
                        if (float.IsNaN(u.pos.x) || float.IsNaN(u.pos.y) || float.IsInfinity(u.pos.x)) nanPos++;
                        bool off = u.agent != null && u.agent.enabled && !u.agent.isOnNavMesh;
                        if (off) { offMesh.TryGetValue(u, out float since); offMesh[u] = since + 1f; if (since + 1f == 6f) stuckOff++; }
                        else offMesh.Remove(u);
                    }
                    peakUnits = Mathf.Max(peakUnits, live);
                }
            }
            Time.timeScale = 1f;
            Application.logMessageReceived -= OnLog;
            var acts = new List<string>();
            int total = 0;
            foreach (var kv in actions) { acts.Add($"{kv.Key} {kv.Value}"); total += kv.Value; }
            report = $"fuzz seed {Seed}{(life ? $" with the lifecycle ({pauses} pauses, {speeds} speed changes, {(surrendered ? "surrendered at 200 s" : "no surrender")}, ended {boot?.State})" : "")}: {total} commands over {world.time - t0:0} s game time ({string.Join(", ", acts)}); peak {peakUnits} units, winner {world.winner}\n" +
                     $"  exceptions {exceptions}, error logs {errorLogs}; ore below 0 {negOre}, supply below 0 {negSupply}, NaN positions {nanPos}, units off the NavMesh 6 s+ {stuckOff}" +
                     (errors.Count > 0 ? "\n  " + string.Join("\n  ", errors) : "");
        }
    }

    public sealed class OreSourceReadTrial : BugTrial
    {
        int readErrors;
        void OnLog(string cond, string stack, LogType type) { if (cond.Contains("does not allow read access")) readErrors++; }
        void OnDisable() => Application.logMessageReceived -= OnLog;

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            Unit ore = null;
            foreach (var u in world.units) if (Unit.Live(u) && u.Type == UnitType.Ore) { ore = u; break; }
            var mod = ore != null ? ore.GetComponent<Unity.AI.Navigation.NavMeshModifier>() : null;
            if (mod == null) { report = "no ore node with a modifier"; yield break; }
            Application.logMessageReceived += OnLog;
            mod.enabled = false;
            world.RequestNavRebuild();
            yield return new WaitForSecondsRealtime(3f);
            mod.enabled = true;
            Application.logMessageReceived -= OnLog;
            var mf = ore.GetComponentInChildren<MeshFilter>();
            report = $"ore node's ignore modifier off, a rebuild asked for: {readErrors} \"does not allow read access\" errors; its mesh {(mf != null && mf.sharedMesh != null ? (mf.sharedMesh.isReadable ? "readable" : "not readable") : "?")}";
        }
    }

    public sealed class LongMatchTrial : MonoBehaviour
    {
        public static uint MapSeed = 2468u;
        public static bool ThinOre;
        public static uint FixedMatchSeed;
        public static float Minutes = 40f;
        public static string Last;
        int exceptions;
        readonly List<string> errors = new List<string>();
        int deathsThisMinute;

        void OnLog(string cond, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            if (cond.Contains("NoSubscription")) return;   // Unity's AI-generator package, not the game
            exceptions++;
            string at = "";
            foreach (var line in stack.Split('\n')) if (line.Contains("StarForge")) { at = line.Trim(); break; }
            string key = (cond.Length > 140 ? cond.Substring(0, 140) : cond) + " @ " + (at.Length > 120 ? at.Substring(0, 120) : at);
            if (errors.Count < 6 && !errors.Contains(key)) errors.Add(key);
        }

        bool seedHeld; bool userFixed; uint userSeed;
        int diggerLines;

        void OnEvent(GameEvent e)
        {
            if (e.kind != GameEventKind.Death || e.team > 1) return;
            deathsThisMinute++;
            // Every Digger lost: what stood nearest that could have shot it, and how far it was from its
            // ore, its side's nearest Foundry and the enemy's start (ore under the enemy's guns, or a raid).
            var u = e.unit; var world = GameWorld.Instance; var map = MapInfo.Instance;
            if (u == null || e.type != UnitType.Worker || world == null || map == null || diggerLines >= 400) return;
            diggerLines++;
            Unit gun = null, tower = null; float gd = 1e9f, td = 1e9f;
            foreach (var o in world.units)
            {
                if (!Unit.Live(o) || o.team != 1 - u.team || !o.def.Armed) continue;
                float d = (o.pos - u.pos).magnitude - o.def.radius;
                if (o.def.building) { if (d < td) { td = d; tower = o; } }
                else if (d < gd) { gd = d; gun = o; }
            }
            var home = world.NearestDropoff(u);
            string node = Unit.Live(u.harvestNode) || u.harvestNode != null ? $"{(u.harvestNode.pos - u.pos).magnitude:0} m" : "none";
            Debug.Log($"[LongMatchDigger] {world.time / 60f:0.0} min team {u.team}: {u.order}{(u.waitAtOre ? " waiting" : "")}, its ore {node}, " +
                      $"Foundry {(home != null ? $"{(home.pos - u.pos).magnitude:0} m" : "none")}, enemy start {(map.StartPos(1 - u.team) - u.pos).magnitude:0} m; " +
                      $"nearest enemy gun {(gun != null ? $"{gun.Type} {gd:0} m{(gun.deserted ? " deserter" : "")}" : "none")}, tower {(tower != null ? $"{tower.Type} {td:0} m{(tower.Complete ? "" : " site")}" : "none")}");
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            MatchSettings.mapSeed = 0;
            MatchSettings.spectate = false;
            if (seedHeld) { MatchSettings.fixedSeed = userFixed; MatchSettings.seed = userSeed; seedHeld = false; }
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            Application.logMessageReceived += OnLog;
            uint mapSeed = MapSeed;
            MatchSettings.mapSeed = mapSeed;
            if (FixedMatchSeed != 0)
            {
                seedHeld = true; userFixed = MatchSettings.fixedSeed; userSeed = MatchSettings.seed;
                MatchSettings.fixedSeed = true; MatchSettings.seed = FixedMatchSeed; FixedMatchSeed = 0;
            }
            MatchSettings.spectate = true;
            MatchSettings.aiMemory = false;
            MatchSettings.skipMenu = false;
            Last = $"map {mapSeed}: loading";
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            yield return null; yield return null;
            GameBootstrap boot = null;
            for (int i = 0; i < 600 && (boot == null || GameWorld.Instance == null); i++) { yield return null; boot = GameBootstrap.Instance; }
            if (boot == null) { Last = $"map {mapSeed}: no GameBootstrap after the reload"; Destroy(gameObject); yield break; }
            yield return new WaitForSecondsRealtime(1f);
            if (boot.State != MatchState.Playing) boot.StartMatch();
            yield return new WaitForSecondsRealtime(1f);
            var world = GameWorld.Instance;
            bool thin = ThinOre; ThinOre = false;
            if (thin) foreach (var u in world.units) if (Unit.Live(u) && u.Type == UnitType.Ore) u.oreLeft = Mathf.Max(1, (int)(u.oreLeft * 0.15f));
            world.Event += OnEvent;
            Time.timeScale = 8f;
            float t0 = world.time, end = Minutes * 60f, nextMinute = 60f, worstSim = 0f, worstSimMinute = 0f;
            long heap0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(), heapMax = heap0;
            int quiet = 0, quietMax = 0; float oreGoneAt = -1f; int minute = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"map {mapSeed}, match seed {MatchSettings.MatchSeed()}: spectated AI vs AI at 8x, up to {Minutes:0} game minutes{(thin ? ", ore cut to 15%" : "")}");
            while (world.time - t0 < end && world.winner < 0)
            {
                yield return null;
                worstSimMinute = Mathf.Max(worstSimMinute, GameWorld.SimMs);
                if (world.time - t0 < nextMinute) continue;
                nextMinute += 60f; minute++;
                int[] structs = new int[2], diggers = new int[2], army = new int[2];
                int oreLeft = 0, units = 0;
                foreach (var u in world.units)
                {
                    if (!Unit.Live(u)) continue;
                    units++;
                    if (u.Type == UnitType.Ore) { oreLeft += u.oreLeft; continue; }
                    if (u.team > 1) continue;
                    if (u.def.building) structs[u.team]++;
                    else if (u.Type == UnitType.Worker) diggers[u.team]++;
                    else if (!u.deserted) army[u.team]++;
                }
                if (oreLeft == 0 && oreGoneAt < 0f) oreGoneAt = world.time - t0;
                long heap = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                heapMax = System.Math.Max(heapMax, heap);
                worstSim = Mathf.Max(worstSim, worstSimMinute);
                bool armies = army[0] > 0 && army[1] > 0;
                if (deathsThisMinute == 0 && armies) { quiet++; quietMax = Mathf.Max(quietMax, quiet); } else quiet = 0;
                var ai1 = boot.AI; var ai0 = boot.PlayerProxy;
                string line = $"{minute,2} min: structures {structs[0]}/{structs[1]}, Diggers {diggers[0]}/{diggers[1]}, army {army[0]}/{army[1]}, ore {world.factions[0].ore}/{world.factions[1].ore}, " +
                              $"supply {world.factions[0].supplyUsed}/{world.factions[0].supplyCap} {world.factions[1].supplyUsed}/{world.factions[1].supplyCap}, map ore {oreLeft}, deaths {deathsThisMinute}, " +
                              $"projectiles {world.projectiles.Count}, units {units}, AI memory {(ai0 != null ? ai0.Perception.Enemies.Count : -1)}/{(ai1 != null ? ai1.Perception.Enemies.Count : -1)}, " +
                              $"waves {(ai0 != null ? ai0.Dbg.waves : -1)}/{(ai1 != null ? ai1.Dbg.waves : -1)}, heap {heap / 1048576f:0} MB, worst sim step {worstSimMinute:0.0} ms";
                Debug.Log("[LongMatch] " + line);
                deathsThisMinute = 0; worstSimMinute = 0f;
            }
            world.Event -= OnEvent;
            Time.timeScale = 1f;
            Application.logMessageReceived -= OnLog;
            sb.AppendLine($"  ended at {world.time - t0:0} s game time, winner {world.winner}; exceptions/errors {exceptions}; worst sim step {worstSim:0.0} ms; heap {heap0 / 1048576f:0} -> {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576f:0} MB (max {heapMax / 1048576f:0}); " +
                          $"longest stretch with both armies standing and no death: {quietMax} min; map ore gone {(oreGoneAt >= 0f ? $"at {oreGoneAt:0} s" : "never")}");
            foreach (var e in errors) sb.AppendLine("  " + e);
            Last = sb.ToString();
            Debug.Log("[LongMatch] " + Last);
            MatchSettings.mapSeed = 0;
            MatchSettings.spectate = false;
            if (seedHeld) { MatchSettings.fixedSeed = userFixed; MatchSettings.seed = userSeed; seedHeld = false; }
            Destroy(gameObject);
        }
    }

    public sealed class BrokeStallTrial : BugTrial
    {
        public static bool Tower;
        int deaths;
        void OnEvent(GameEvent e) { if (e.kind == GameEventKind.Death && e.team < 2) deaths++; }

        IEnumerator Start()
        {
            world = GameWorld.Instance; map = MapInfo.Instance;
            var boot = GameBootstrap.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            if (boot.State == MatchState.Playing) { report = "a match is running: restart play and call it from the title screen"; yield break; }
            MatchSettings.spectate = true;
            MatchSettings.aiMemory = false;
            try { boot.StartMatch(); } finally { }
            yield return new WaitForSecondsRealtime(1f);
            Time.timeScale = 8f;
            yield return Wait(60f);
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team < 2 && !u.def.building || Unit.Live(u) && u.team < 2 && u.Type == UnitType.MechBay)
                    world.Damage(u, 1e7f, u.pos, null);
            // Nothing left in a queue either, and the ore cut at once: a Digger coming out later
            // restarted an economy (queued in the second the old ore was still there).
            world.factions[0].ore = 30; world.factions[1].ore = 30;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team < 2 && u.def.building && u.queue.Count > 0) u.queue.Clear();
            yield return Wait(1f);
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team < 2 && u.def.building && u.queue.Count > 0) u.queue.Clear();
            world.factions[0].ore = 30; world.factions[1].ore = 30;
            int diggersAfter = 0; foreach (var u in world.units) if (Unit.Live(u) && u.team < 2 && u.Type == UnitType.Worker) diggersAfter++;
            Vector2 h0 = map.StartPos(0), h1 = map.StartPos(1);
            for (int i = 0; i < 8; i++) world.Spawn(UnitType.Trooper, 1, world.NearestReachable(h1, h1 + (h0 - h1).normalized * 14f + new Vector2((i % 4) * 2f, (i / 4) * 2f)), true);
            for (int i = 0; i < 2; i++) world.Spawn(UnitType.Trooper, 0, world.NearestReachable(h0, h0 + (h1 - h0).normalized * 14f + new Vector2(i * 2f, 0f)), true);
            bool tower = Tower;
            int towers = 0;
            if (tower)
            {
                Unit f0 = null; foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && u.Type == UnitType.Foundry) { f0 = u; break; }
                if (f0 != null)
                    for (int k = 0; k < 36 && towers < 2; k++)
                    {
                        float a = k * 0.6f, r = 9f + (k % 4) * 2f;
                        var at = f0.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        if (((at - h1).magnitude < (f0.pos - h1).magnitude) && world.CanPlace(UnitType.Sentinel, at)) { world.Spawn(UnitType.Sentinel, 0, at, true); towers++; }
                    }
            }
            int retreats0 = boot.AI.Retreats;
            int structs0 = 0; foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && u.def.building) structs0++;
            int waves0 = boot.AI.Dbg.waves;
            world.Event += OnEvent;
            float t0 = world.time, maxAway = 0f, nextLog = 0f;
            var trace = new StringBuilder();
            while (world.time - t0 < 480f && world.winner < 0)
            {
                yield return Wait(0.5f);
                if (world.time - t0 >= nextLog && nextLog <= 120f)
                {
                    nextLog += 10f;
                    int d0 = 0, d1 = 0;
                    foreach (var u in world.units) if (Unit.Live(u) && u.Type == UnitType.Worker) { if (u.team == 0) d0++; else if (u.team == 1) d1++; }
                    trace.Append($" | {world.time - t0:0}s ore {world.factions[0].ore}/{world.factions[1].ore} mined {world.factions[0].oreMined}/{world.factions[1].oreMined} Diggers {d0}/{d1}");
                }
                Vector2 c = Vector2.zero; int n = 0;
                foreach (var u in world.units) if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Trooper) { c += u.pos; n++; }
                if (n > 0) maxAway = Mathf.Max(maxAway, (c / n - h1).magnitude);
            }
            world.Event -= OnEvent;
            int structs = 0; foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && u.def.building) structs++;
            Time.timeScale = 1f;
            MatchSettings.spectate = false;
            int diggersEnd = 0; foreach (var u in world.units) if (Unit.Live(u) && u.team < 2 && u.Type == UnitType.Worker) diggersEnd++;
            report = (tower ? $"{towers} Sentinels of ours by our Foundry; team 1 retreated {boot.AI.Retreats - retreats0} times; " : "") + $"(Diggers on the field after the purge {diggersAfter}, at the end {diggersEnd}) both economies dead at 60 s (no Diggers, 30 ore), team 1 given 8 riflemen and team 0 2: over {world.time - t0:0} s team 1 started {boot.AI.Dbg.waves - waves0} waves, " +
                     $"its riflemen got at most {maxAway:0} m from home (their base {(h0 - h1).magnitude:0} m off); team 0 structures {structs0} -> {structs}; deaths {deaths}; winner {world.winner}; trace" + trace;
        }

        void OnDestroy() { MatchSettings.spectate = false; }
    }

    public sealed class DiggerScoutTrial : BugTrial
    {
        int diedThere;
        Vector2 baseAt;

        void OnEvent(GameEvent e)
        {
            if (e.kind == GameEventKind.Death && e.team == 1 && e.type == UnitType.Worker && e.unit != null && (e.unit.pos - baseAt).magnitude < 45f) diedThere++;
        }

        void OnDisable() { if (world != null) world.Event -= OnEvent; }

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            if (Commander.Suspended) { report = "the opponent AI is silenced"; yield break; }
            Time.timeScale = 8f;
            baseAt = map.StartPos(0);
            if (world.time < 60f) yield return Wait(60f - world.time);
            int towers = 0;
            for (int ring = 0; ring < 6 && towers < 2; ring++)
                for (int k = 0; k < 12 && towers < 2; k++)
                {
                    float a = k * Mathf.PI / 6f + ring * 0.3f;
                    var at = baseAt + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (10f + ring * 3f);
                    if (towers == 1 && spawned.Count > 0 && (spawned[0].pos - at).magnitude < 12f) continue;
                    if (world.CanPlace(UnitType.Sentinel, at)) { Put(UnitType.Sentinel, 0, at); towers++; }
                }
            world.Event += OnEvent;
            var came = new HashSet<Unit>();
            float t0 = world.time; int removed = 0, diggersLeast = 99;
            while (world.time - t0 < 240f && world.winner < 0)
            {
                yield return null;
                int d = 0;
                foreach (var u in world.units)
                {
                    if (!Unit.Live(u) || u.team != 1 || u.def.building) continue;
                    if (u.Type == UnitType.Worker)
                    {
                        d++;
                        if ((u.pos - baseAt).magnitude < 45f) came.Add(u);
                    }
                    else if (u.Type != UnitType.Mech) { world.Damage(u, 1e7f, u.pos, null); removed++; }
                }
                diggersLeast = Mathf.Min(diggersLeast, d);
            }
            world.Event -= OnEvent;
            int end = 0; foreach (var u in world.units) if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Worker) end++;
            var snap = GameBootstrap.Instance != null && GameBootstrap.Instance.AI != null ? GameBootstrap.Instance.AI.Perception.Snap : null;
            Time.timeScale = 1f;
            report = $"AI kept without an army ({removed} soldiers removed) against {towers} Sentinels in our base, {world.time - t0:0} s from {t0:0} s: " +
                     $"{came.Count} of its Diggers came within 45 m of our base, {diedThere} died there; Diggers {diggersLeast} at the least, {end} at the end; " +
                     $"our base known {(snap != null ? snap.enemyBaseKnown.ToString() : "?")}, its picture {(snap != null ? snap.scoutConfidence.ToString("0.00") : "?")}";
        }
    }

    public sealed class MechInOreTrial : BugTrial
    {
        public static bool Leave, Walk, Second;
        public static float Stand;
        public static bool Outlying, TowerOnWay;
        public static bool Hunting, NoSafeOre, Rich;
        /// <summary>The outlying variants' hull cap for our Mech (share of its full hull).</summary>
        public static float Hull = 0.3f;
        Unit outlyingLine, wayTower;
        bool towerKnown;
        // Diggers sent home from far out (a move order ending by the main Foundry, given 35+ m from it).
        readonly HashSet<Unit> sentHome = new HashSet<Unit>();
        int lostAtLine, lostToTower;
        // Diggers whose ore changed from the outlying line's to ore 20+ m from it (5b's moves, and any other
        // re-pick: step 6, a seam run dry), where they were sent, and how they died.
        readonly Dictionary<Unit, Vector2> movedTo = new Dictionary<Unit, Vector2>();
        readonly Dictionary<Unit, Unit> lastNode = new Dictionary<Unit, Unit>();
        int movedDiedOnWay, movedDiedAtNode, movedDiedBack;
        float moveNodeFromMech, moveWayNearest = 1e9f, moveWayNearestSum, moveSentDist;
        int diggersLost, soldiersLost, lostAfterSeen;
        float firstSeen = -1f;

        void OnEvent(GameEvent e)
        {
            if (e.kind != GameEventKind.Death || e.team != 1) return;
            if (e.type == UnitType.Worker)
            {
                diggersLost++;
                if (firstSeen >= 0f && world.time - firstSeen > 3f) lostAfterSeen++;
                if (outlyingLine != null && (new Vector2(e.pos.x, e.pos.z) - outlyingLine.pos).magnitude < 35f) lostAtLine++;
                if (wayTower != null && (new Vector2(e.pos.x, e.pos.z) - wayTower.pos).magnitude < wayTower.def.range + 3f) lostToTower++;
                // A Digger 5b moved off the line: where it died -- on its walk, at the ore it was sent to, or
                // back at the line.
                if (e.unit != null && movedTo.TryGetValue(e.unit, out var mv))
                {
                    var at = new Vector2(e.pos.x, e.pos.z);
                    if ((at - mv).magnitude < 8f) movedDiedAtNode++;
                    else if (outlyingLine != null && (at - outlyingLine.pos).magnitude < 35f && e.unit.harvestNode != null && (e.unit.harvestNode.pos - mv).sqrMagnitude > 1f) movedDiedBack++;
                    else movedDiedOnWay++;
                }
            }
            else if (e.unit != null && !e.unit.def.building) soldiersLost++;
        }

        void OnDisable() { if (world != null) world.Event -= OnEvent; }

        IEnumerator Start()
        {
            bool hunting = Hunting, noSafe = NoSafeOre, rich = Rich;
            float hullCap = Hull;
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            if (Commander.Suspended) { report = "the opponent AI is silenced"; yield break; }
            Time.timeScale = 8f;
            // Nobody plays our side in a trial: its structures are kept standing, or the AI won first.
            while (world.time < 240f && world.winner < 0) { yield return null; HoldOurBase(); }
            if (world.winner >= 0) { report = $"the match ended at {world.time:0} s (winner {world.winner}) before the Mech went in"; yield break; }
            Vector2 home = map.StartPos(1);
            Unit foundry = null; float fd = 1e9f;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Foundry && u.Complete)
                {
                    float d = (u.pos - home).magnitude;
                    if (d < fd) { fd = d; foundry = u; }
                }
            if (foundry == null) { report = "the AI has no Foundry"; yield break; }
            var mining = new Dictionary<Unit, int>();
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Worker && Unit.Live(u.harvestNode))
                {
                    mining.TryGetValue(u.harvestNode, out int c);
                    mining[u.harvestNode] = c + 1;
                }
            Unit node = null; int most = 0;
            foreach (var kv in mining)
                if (kv.Value > most && (kv.Key.pos - foundry.pos).magnitude < 45f) { most = kv.Value; node = kv.Key; }
            if (node == null) { report = "none of its Diggers mine by its main Foundry"; yield break; }
            Vector2 at = world.NearestReachable(foundry.pos, Vector2.Lerp(foundry.pos, node.pos, 0.5f));
            if (Stand > 0f) at = world.NearestReachable(foundry.pos, foundry.pos + (map.StartPos(0) - foundry.pos).normalized * Stand);
            bool walk = Walk, second = Second;
            string secondNote = "";
            Unit line2 = null;
            if (second)
            {
                // An ore field 45-100 m out and 60+ m from where the Mech will stand, a finished Foundry by it.
                Unit far = null; float best = float.MaxValue;
                foreach (var n in world.units)
                {
                    if (!Unit.Live(n) || n.Type != UnitType.Ore || n.oreLeft < 200) continue;
                    float d = (n.pos - foundry.pos).magnitude;
                    if (d < 45f || d > 100f || (n.pos - at).magnitude < 60f) continue;
                    if (d < best) { best = d; far = n; }
                }
                if (far != null)
                    for (int k = 0; k < 40 && line2 == null; k++)
                    {
                        float a = k * 0.5f, r = 8f + (k % 5) * 2f;
                        var p2 = far.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        if (world.CanPlace(UnitType.Foundry, p2)) line2 = world.Spawn(UnitType.Foundry, 1, p2, true);
                    }
                if (line2 == null) { report = "no room for a second Foundry 45-100 m out"; yield break; }
                secondNote = $"a second Foundry of theirs {(line2.pos - foundry.pos).magnitude:0} m from the main one; ";
                yield return Hold(2f);
                if (Outlying)
                {
                    // Its home seams emptied, so its own step 6 sends its miners to that line (sent there by
                    // the trial, the Commander took them straight back home); then our Mech beside it.
                    int emptied = 0;
                    foreach (var n in world.units)
                        if (Unit.Live(n) && n.Type == UnitType.Ore && (n.pos - foundry.pos).magnitude < 30f) { n.oreLeft = 0; emptied++; }
                    // Our base held through this too: let go, the AI's army took it and the match was over before the Mech went in.
                    yield return Hold(40f);
                    if (world.winner >= 0) { report = $"the match ended at {world.time:0} s (winner {world.winner}) before the Mech went in"; yield break; }
                    outlyingLine = line2;
                    at = world.NearestReachable(line2.pos, far.pos + (far.pos - line2.pos).normalized * 6f);
                    if (TowerOnWay)
                    {
                        // On the way home, a little nearer the line than halfway, and never with home in its reach
                        // (5b's guns list it at range + 5 m: covering home, both rules turn the home-first clear down).
                        Vector2 mid = Vector2.Lerp(line2.pos, foundry.pos, 0.42f);
                        float homeClear = Defs.Get(UnitType.Sentinel).range + 5f + 4f;
                        for (int k = 0; k < 24 && wayTower == null; k++)
                        {
                            var p3 = mid + new Vector2(Mathf.Cos(k * 0.8f), Mathf.Sin(k * 0.8f)) * (k / 6) * 3f;
                            if ((p3 - foundry.pos).magnitude > homeClear && world.CanPlace(UnitType.Sentinel, p3)) wayTower = world.Spawn(UnitType.Sentinel, 0, p3, true);
                        }
                        if (wayTower == null) { report = "no room for a Sentinel on the way home clear of home"; yield break; }
                        spawned.Add(wayTower);
                        // The AI must have seen it for any rule to weigh it: one of its riflemen walks up to it (it
                        // sees 20+ m; the tower shoots it), as a Digger or soldier passing by would.
                        var looker = world.Spawn(UnitType.Trooper, 1, world.NearestReachable(foundry.pos, Vector2.MoveTowards(wayTower.pos, foundry.pos, 14f)), true);
                        spawned.Add(looker);
                        yield return Hold(4f);
                        var aiS = GameBootstrap.Instance != null ? GameBootstrap.Instance.AI : null;
                        if (aiS != null)
                            foreach (var r in aiS.Perception.Enemies)
                                if (r.type == UnitType.Sentinel && (r.pos - wayTower.pos).magnitude < 3f) towerKnown = true;
                        secondNote += $"a Sentinel of ours {(wayTower.pos - foundry.pos).magnitude:0} m from its main Foundry, {(wayTower.pos - line2.pos).magnitude:0} m from that line's, " +
                                      $"{(towerKnown ? "seen by the AI" : "NOT seen by the AI")}; ";
                    }
                    secondNote += $"its {emptied} home seams emptied, {MinersBy(line2)} of its Diggers mining at that line 40 s later; ";
                    if (noSafe)
                    {
                        int others = 0;
                        foreach (var n in world.units)
                            if (Unit.Live(n) && n.Type == UnitType.Ore && n.oreLeft > 0 && (n.pos - line2.pos).magnitude > 25f) { n.oreLeft = 0; others++; }
                        secondNote += $"every other node emptied ({others}): no safe ore; way home {(Commander.LegacyWayHome ? "old rule" : "new rule")}; ";
                    }
                }
            }
            Vector2 from = at;
            if (walk)
            {
                Vector2 toUs = (map.StartPos(0) - foundry.pos).normalized;
                from = world.NearestReachable(foundry.pos, foundry.pos + toUs * 70f);
            }
            if (Outlying) from = at;
            var mech = world.TrialMech(0, MechParts.Generate(777u), from, 0f, false, hunting);
            float travelled = 0f; Vector2 lastPos = mech.pos;
            spawned.Add(mech);
            bool outlying = Outlying, hullSet = false;
            Vector2 homeSpot = world.NearestReachable(foundry.pos, foundry.pos);
            float nearestMiner = -1f;
            int armyNearMost = 0; float mechDiedAt = -1f;
            float reach = mech.mech != null ? mech.mech.MaxRange : 25f;
            var known = new HashSet<Unit>();
            int diggers0 = 0;
            foreach (var u in world.units)
            {
                if (u != null) known.Add(u);
                if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Worker) diggers0++;
            }
            int HomeMiners() { int m = 0; foreach (var u in world.units) if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Worker && Unit.Live(u.harvestNode) && (u.harvestNode.pos - foundry.pos).magnitude < 25f) m++; return m; }
            int homeBefore = HomeMiners(), homeWhileThere = -1;
            int ore0 = world.factions[1].ore, trained = 0, samples = 0, inReach = 0, inReachMost = 0, trainedAfter = 0;
            int mined0 = world.factions[1].oreMined, idleSeconds = 0;
            bool leave = Leave; float firstAfter = -1f;
            world.Event += OnEvent;
            float t0 = world.time, nextSample = 0f;
            var aiC = GameBootstrap.Instance != null ? GameBootstrap.Instance.AI : null;
            int cleared0 = aiC != null ? aiC.Dbg.diggersCleared : 0, moved0 = aiC != null ? aiC.Dbg.diggersMoved : 0, denied0 = aiC != null ? aiC.Budget.denied : 0;
            // What its actions went on in the window, by the step that asked (the evaluation line's
            // tally is the whole match's), and how often 5b could have run at all.
            var spent0 = BudgetTally(aiC, "spentBy"); var refused0 = BudgetTally(aiC, "refusedBy");
            int seenSeconds = 0, lowBudgetSeconds = 0;
            if (walk) world.CmdMechMove(mech, at);
            while (world.time - t0 < (hunting ? 180f : 120f) && world.winner < 0)
            {
                yield return null;
                HoldOurBase();
                if (Unit.Live(mech)) { travelled += (mech.pos - lastPos).magnitude; lastPos = mech.pos; }
                // Rich all through: its production has ore for every idle building, every pass.
                if (rich && world.factions[1].ore < 2500) world.factions[1].ore = 2500;
                if (firstSeen < 0f && aiC != null)
                    foreach (var r in aiC.Perception.Enemies) if (r.type == UnitType.Mech) { firstSeen = r.firstSeen; break; }
                if (walk && Unit.Live(mech) && mech.order != Order.Move && (mech.pos - at).magnitude > 4f) world.CmdMechMove(mech, at);
                if (walk)
                    foreach (var u in world.units)
                        if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Foundry && u.Complete) u.hp = u.def.hp;
                if (leave && Unit.Live(mech) && world.time - t0 >= 60f) { homeWhileThere = HomeMiners(); ClearSpawned(); }
                // Leaving, it is a raid on the Diggers: their Foundries are kept standing, or nothing could be trained after.
                if (leave && Unit.Live(mech))
                    foreach (var u in world.units)
                        if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Foundry && u.Complete) u.hp = u.def.hp;
                if (Unit.Live(mech) && mech.mech != null && !outlying) mech.hp = mech.mech.MaxHp;
                if (outlying)
                {
                    // Battered and kept so (its design's nanites mended it back to full in two minutes); it can
                    // still be killed. Read once its core is up: set at the spawn, MaxHp read 0 and it was gone.
                    if (Unit.Live(mech) && mech.mech != null && mech.mech.MaxHp > 0f)
                    {
                        if (!hullSet) { hullSet = true; nearestMiner = NearestMiner(mech.pos); }
                        mech.hp = Mathf.Min(mech.hp, mech.mech.MaxHp * hullCap);
                    }
                    if (hullSet && !Unit.Live(mech) && mechDiedAt < 0f) mechDiedAt = world.time - t0;
                    // Each Digger whose ore changes from the line's to another: where it was sent, how near
                    // the straight way passes our Mech, how far that ore is from it.
                    if (outlyingLine != null)
                        foreach (var u in world.units)
                        {
                            if (!Unit.Live(u) || u.team != 1 || u.Type != UnitType.Worker) continue;
                            if (u.order == Order.Move && (u.orderPos - homeSpot).magnitude < 6f && (u.pos - homeSpot).magnitude > 35f) sentHome.Add(u);
                            var nodeNow = Unit.Live(u.harvestNode) ? u.harvestNode : null;
                            lastNode.TryGetValue(u, out var was);
                            if (nodeNow != null && was != null && nodeNow != was && (was.pos - outlyingLine.pos).magnitude < 35f &&
                                (nodeNow.pos - outlyingLine.pos).magnitude > 20f && Unit.Live(mech) && !movedTo.ContainsKey(u))
                            {
                                movedTo[u] = nodeNow.pos;
                                Vector2 a = u.pos, ab = nodeNow.pos - u.pos;
                                float t = Mathf.Clamp01(Vector2.Dot(mech.pos - a, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
                                float closest = (a + ab * t - mech.pos).magnitude;
                                moveWayNearest = Mathf.Min(moveWayNearest, closest);
                                moveWayNearestSum += closest;
                                moveNodeFromMech += (nodeNow.pos - mech.pos).magnitude;
                                moveSentDist += ab.magnitude;
                            }
                            if (nodeNow != null) lastNode[u] = nodeNow;
                        }
                    if (Unit.Live(mech))
                    {
                        int near = 0;
                        foreach (var u in world.UnitsNear(mech.pos, 35f))
                            if (Unit.Live(u) && u.team == 1 && !u.def.building && u.Type != UnitType.Worker) near++;
                        armyNearMost = Mathf.Max(armyNearMost, near);
                    }
                }
                bool sample = world.time - t0 >= nextSample;
                if (sample) { nextSample += 1f; samples++; }
                int n = 0;
                foreach (var u in world.units)
                {
                    if (!Unit.Live(u) || u.team != 1 || u.Type != UnitType.Worker) continue;
                    if (known.Add(u))
                    {
                        trained++;
                        if (leave && world.time - t0 >= 60f) { trainedAfter++; if (firstAfter < 0f) firstAfter = world.time - t0 - 60f; }
                    }
                    if (sample && Unit.Live(mech) && (u.pos - mech.pos).magnitude < reach) n++;
                    if (sample && u.order == Order.Idle) idleSeconds++;
                }
                if (sample) { inReach += n; inReachMost = Mathf.Max(inReachMost, n); }
                if (sample && aiC != null)
                {
                    var sn = aiC.Perception.Snap;
                    if (sn != null && sn.eMech && sn.eMechSeenAgo < 4f) seenSeconds++;
                    if (!aiC.Budget.Can(2)) lowBudgetSeconds++;
                }
            }
            world.Event -= OnEvent;
            // Read before the spawned units are cleared below.
            string mechEnd = !outlying ? "" : mechDiedAt >= 0f ? $"killed {mechDiedAt:0} s in" : Unit.Live(mech) && mech.mech != null ? $"alive at {mech.hp / mech.mech.MaxHp * 100f:0}%" : "gone";
            int end = 0;
            foreach (var u in world.units) if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Worker) end++;
            int homeAfter = HomeMiners();
            bool alive = Unit.Live(mech) || leave;
            int foundries = 0; foreach (var u in world.units) if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Foundry && u.Complete) foundries++;
            var snapEnd = GameBootstrap.Instance != null && GameBootstrap.Instance.AI != null ? GameBootstrap.Instance.AI.Perception.Snap : null;
            string after = $"; its main Foundry {(Unit.Live(foundry) ? "standing" : "destroyed")}, {foundries} Foundries" +
                           (snapEnd != null ? $", their Mech {(snapEnd.eMech ? $"remembered, seen {snapEnd.eMechSeenAgo:0} s ago" : "not remembered")}" : "");
            ClearSpawned();
            Time.timeScale = 1f;
            string economy = $"; idle Digger-seconds {idleSeconds}, ore mined {world.factions[1].oreMined - mined0}";
            string orders = economy + (aiC != null ? $"; step 5b cleared {aiC.Dbg.diggersCleared - cleared0} and moved {aiC.Dbg.diggersMoved - moved0} Diggers, {aiC.Budget.denied - denied0} actions refused for the APM cap" +
                                                     $"; their Mech seen in the last 4 s at {seenSeconds} of {samples} samples, budget under 2 at {lowBudgetSeconds}" +
                                                     $"; actions in the window: spent {TallyDelta(aiC, "spentBy", spent0)}; refused {TallyDelta(aiC, "refusedBy", refused0)}" : "");
            string lineNote = line2 != null ? $"; mining at the second line at the end {MinersBy(line2)}" : "";
            if (outlying)
                lineNote += (wayTower != null ? $"; {lostToTower} Diggers lost in the Sentinel's reach, {sentHome.Count} sent home from far out" : "") + $"; our Mech put down {(at - outlyingLine.pos).magnitude:0} m from that line's Foundry, the nearest of its Diggers {nearestMiner:0} m off, reach {reach:0} m" +
                            $"; at that line {lostAtLine} Diggers lost, the AI's army within 35 m of our Mech {armyNearMost} at most, our Mech {mechEnd}" +
                            (rich ? "; the AI's ore held at 2,500 or more" : "") +
                            (hunting ? $"; our Mech hunting on its own (no bay), travelled {travelled:0} m, Diggers lost anywhere {diggersLost}" : "") +
                            (movedTo.Count > 0 ? $"; moved off the line {movedTo.Count}: sent {moveSentDist / movedTo.Count:0} m on average to ore {moveNodeFromMech / movedTo.Count:0} m from our Mech, the straight way passing it at {moveWayNearestSum / movedTo.Count:0} m on average ({moveWayNearest:0} at the nearest); of them lost on the way {movedDiedOnWay}, at that ore {movedDiedAtNode}, back at the line {movedDiedBack}" : "; moved off the line 0");
            string seen = secondNote + (walk ? $"walked in from {(from - foundry.pos).magnitude:0} m (the AI first had it in memory {(firstSeen >= 0f ? $"{firstSeen - t0:0} s in" : "never")}; Diggers lost more than 3 s after that {lostAfterSeen}), " : "");
            report = seen + $"our Mech ({reach:0} m reach{(alive ? "" : ", killed")}) stood {(at - foundry.pos).magnitude:0} m from the AI's main Foundry, {(at - node.pos).magnitude:0} m from the ore {most} of its Diggers mined, " +
                     $"{world.time - t0:0} s from {t0:0} s: the AI lost {diggersLost} Diggers and trained {trained} (Diggers {diggers0} -> {end}), lost {soldiersLost} soldiers; " +
                     $"Diggers in the Mech's reach {(samples > 0 ? (float)inReach / samples : 0f):0.0} on average, {inReachMost} at most; ore {ore0} -> {world.factions[1].ore}; winner {world.winner}" +
                     orders + lineNote + (leave ? $"; the Mech taken away at 60 s: {trainedAfter} Diggers trained in the 60 s after, the first {(firstAfter >= 0f ? $"{firstAfter:0} s after" : "never")}; Diggers mining by the main Foundry {homeBefore} before, {homeWhileThere} when it left, {homeAfter} at the end" : "") + after;
        }

        void HoldOurBase()
        {
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 0 && u.def.building && u.Complete) u.hp = u.def.hp;
        }

        const System.Reflection.BindingFlags Priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        static Dictionary<int, int> BudgetTally(Commander c, string field)
        {
            if (c == null) return new Dictionary<int, int>();
            return new Dictionary<int, int>((Dictionary<int, int>)typeof(Commander).GetField(field, Priv).GetValue(c));
        }

        static string TallyDelta(Commander c, string field, Dictionary<int, int> before)
        {
            var now = BudgetTally(c, field);
            var names = (Dictionary<int, string>)typeof(Commander).GetField("siteNames", Priv).GetValue(c);
            var l = new List<KeyValuePair<int, int>>();
            foreach (var kv in now)
            {
                before.TryGetValue(kv.Key, out int had);
                if (kv.Value > had) l.Add(new KeyValuePair<int, int>(kv.Key, kv.Value - had));
            }
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (int i = 0; i < l.Count && i < 6; i++) parts.Add($"{(names.TryGetValue(l[i].Key, out var nm) ? nm : l[i].Key.ToString())} {l[i].Value}");
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        IEnumerator Hold(float seconds)
        {
            float until = world.time + seconds;
            while (world.time < until && world.winner < 0) { yield return null; HoldOurBase(); }
        }

        float NearestMiner(Vector2 p)
        {
            float best = 1e9f;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Worker) best = Mathf.Min(best, (u.pos - p).magnitude);
            return best;
        }

        int MinersBy(Unit f)
        {
            int m = 0;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Worker && Unit.Live(u.harvestNode) && (u.harvestNode.pos - f.pos).magnitude < 25f) m++;
            return m;
        }
    }

    public sealed class ScoutTripTrial : BugTrial
    {
        public static bool Outer;

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            if (Commander.Suspended) { report = "the opponent AI is silenced"; yield break; }
            if (world.time > 5f) { report = $"the match is {world.time:0} s old: restart play and call it from the title screen"; yield break; }
            var ai = GameBootstrap.Instance != null ? GameBootstrap.Instance.AI : null;
            if (ai == null) { report = "no opponent Commander"; yield break; }
            Time.timeScale = 8f;
            Vector2 ours = map.StartPos(0), theirs = map.StartPos(1);
            string outer = "";
            if (Outer)
            {
                Vector2 toward = (theirs - ours).normalized;
                for (int k = 0; k < 24 && outer.Length == 0; k++)
                {
                    float a = (k % 2 == 0 ? 1 : -1) * (k / 2) * 0.12f;
                    var at = ours + new Vector2(toward.x * Mathf.Cos(a) - toward.y * Mathf.Sin(a), toward.x * Mathf.Sin(a) + toward.y * Mathf.Cos(a)) * 22f;
                    if (world.CanPlace(UnitType.Bunkhouse, at)) { Put(UnitType.Bunkhouse, 0, at); outer = $", a Bunkhouse of ours {(at - ours).magnitude:0} m out toward them"; }
                }
                if (outer.Length == 0) outer = ", no room for the Bunkhouse out front";
            }
            float tKnown = -1f, tFoundry = -1f, offAtFoundry = -1f, offAtKnown = -1f, nearest = 1e9f, tHome = -1f;
            Unit scout = null;
            var came = new HashSet<Unit>();
            while (world.time < 300f && world.winner < 0)
            {
                yield return null;
                foreach (var u in world.units)
                    if (Unit.Live(u) && u.team == 0 && u.def.building && u.Complete) u.hp = u.def.hp;
                var snap = ai.Perception.Snap;
                if (snap == null) continue;
                if (tKnown < 0f && snap.enemyBaseKnown) { tKnown = world.time; offAtKnown = (snap.enemyBase - ours).magnitude; }
                if (tFoundry < 0f && snap.eFoundries > 0) { tFoundry = world.time; offAtFoundry = (snap.enemyBase - ours).magnitude; }
                foreach (var u in world.units)
                {
                    if (!Unit.Live(u) || u.team != 1 || u.Type != UnitType.Worker) continue;
                    float d = (u.pos - ours).magnitude;
                    if (d < 45f) came.Add(u);
                    if (d < nearest) { nearest = d; scout = u; }
                }
                if (tFoundry >= 0f && tHome < 0f && scout != null && Unit.Live(scout) && (scout.pos - theirs).magnitude < 50f) tHome = world.time;
            }
            var end = ai.Perception.Snap;
            Time.timeScale = 1f;
            string fate = scout == null ? "no Digger left home" :
                          !Unit.Live(scout) ? "died" :
                          $"alive, {(scout.pos - ours).magnitude:0} m from our base and {(scout.pos - theirs).magnitude:0} m from its own, {scout.order}{(Unit.Live(scout.harvestNode) ? " (mining)" : "")}";
            report = $"{world.time:0} s{outer}: our base known at {(tKnown >= 0f ? $"{tKnown:0} s ({offAtKnown:0} m off)" : "never")}, our Foundry remembered at {(tFoundry >= 0f ? $"{tFoundry:0} s" : "never")} " +
                     $"(base then believed {offAtFoundry:0} m off, at the end {(end != null ? (end.enemyBase - ours).magnitude : -1f):0} m); " +
                     $"its nearest Digger came {nearest:0} m from our base's middle, {(tHome >= 0f ? $"home at {tHome:0} s" : "never home")}, {fate}; {came.Count} of its Diggers came within 45 m; winner {world.winner}";
        }
    }

    public sealed class DryExpandTrial : BugTrial
    {
        public static float CutAt = 240f;
        public static bool Blind;

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            if (Commander.Suspended) { report = "the opponent AI is silenced"; yield break; }
            Time.timeScale = 8f;
            float cutAt = CutAt;
            while (world.time < cutAt && world.winner < 0) { yield return null; HoldOurBase(); }
            var before = new HashSet<Unit>();
            foreach (var u in world.units) if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Foundry) before.Add(u);
            if (before.Count == 0) { report = "the AI has no Foundry"; yield break; }
            int cut = 0, left = 0;
            foreach (var n in world.units)
            {
                if (!Unit.Live(n) || n.Type != UnitType.Ore || n.oreLeft <= 0) continue;
                bool near = false;
                foreach (var f in before) if ((n.pos - f.pos).magnitude < 25f) near = true;
                if (!near) continue;
                if (n.oreLeft > 300) { n.oreLeft = 300; cut++; }
                left += n.oreLeft;
            }
            bool blind = Blind;
            int forgot = 0;
            if (blind)
            {
                var grid = typeof(GameWorld).GetField("explored", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(world) as byte[][];
                if (grid == null) { report = "could not reach the explored grid"; yield break; }
                float cell = world.VisCell;
                for (int z = 0; z < GameWorld.VIS; z++)
                    for (int x = 0; x < GameWorld.VIS; x++)
                    {
                        var c = new Vector2((x + 0.5f) * cell, (z + 0.5f) * cell);
                        bool near = false;
                        foreach (var f in before) if ((c - f.pos).magnitude < 30f) near = true;
                        if (!near && grid[1][z * GameWorld.VIS + x] != 0) { grid[1][z * GameWorld.VIS + x] = 0; forgot++; }
                    }
            }
            var F = world.factions[1];
            var ai = GameBootstrap.Instance != null ? GameBootstrap.Instance.AI : null;
            var siteTest = typeof(Commander).GetMethod("ExpansionSite", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float t0 = world.time, rich = -1f, placed = -1f, nextSample = 0f;
            int oreMin = int.MaxValue, oreMax = 0, richPasses = 0, samples = 0, noSite = 0, busy = 0, able = 0, ableRich = 0, unplaceable = 0, noBuilder = 0;
            Vector2 lastSite = default; int lastRing = -1;
            while (world.time - t0 < 240f && world.winner < 0)
            {
                yield return null;
                HoldOurBase();
                if (blind)
                    foreach (var u in world.units)
                        if (Unit.Live(u) && u.team == 1 && !u.def.building && u.Type != UnitType.Worker && u.Type != UnitType.Mech) world.Damage(u, 1e7f, u.pos, null);
                oreMin = Mathf.Min(oreMin, F.ore); oreMax = Mathf.Max(oreMax, F.ore);
                if (F.ore >= 400) { richPasses++; if (rich < 0f) rich = world.time - t0; }
                // Once a second, before it is placed: could it expand (an explored site, under two
                // buildings going up), and did it hold the price of a Foundry and a Digger's reserve?
                if (placed < 0f && ai != null && siteTest != null && world.time - t0 >= nextSample)
                {
                    nextSample += 1f; samples++;
                    var args = new object[] { ai.Perception.Home, null };
                    bool site = (bool)siteTest.Invoke(ai, args);
                    var snap = ai.Perception.Snap;
                    if (!site) noSite++;
                    else if (snap != null && snap.pending >= 2) busy++;
                    else
                    {
                        able++; if (F.ore >= Defs.Get(UnitType.Foundry).cost + 100) ableRich++;
                        // Can a Foundry go up round that site at all (the 8-16 m ring step 3 tries at random)?
                        var siteAt = (Vector2)args[1];
                        int ring = 0;
                        for (int ri = 0; ri < 5; ri++)
                            for (int ai2 = 0; ai2 < 24; ai2++)
                            {
                                float rr = 8f + ri * 2f, aa = ai2 * Mathf.PI / 12f;
                                if (world.CanPlace(UnitType.Foundry, siteAt + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * rr)) ring++;
                            }
                        if (ring == 0) unplaceable++;
                        lastSite = siteAt; lastRing = ring;
                        int free = 0;
                        foreach (var u in world.units) if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Worker && u.order != Order.Build) free++;
                        if (free == 0) noBuilder++;
                    }
                }
                if (placed < 0f)
                    foreach (var u in world.units)
                        if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Foundry && !before.Contains(u)) { placed = world.time - t0; break; }
                if (placed >= 0f && world.time - t0 > placed + 5f) break;
            }
            int diggers = 0; foreach (var u in world.units) if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Worker) diggers++;
            Time.timeScale = 1f;
            report = (blind ? $"blind ({forgot} cells forgotten, its soldiers removed): " : "") + $"AI's home seams cut at {t0:0} s ({cut} seams, {left} ore left by its {before.Count} Foundries): its ore first reached 400 {(rich >= 0f ? $"{rich:0} s after" : "never")}, " +
                     $"its expansion Foundry {(placed >= 0f ? $"placed {placed:0} s after" : $"not placed in {world.time - t0:0} s")}; ore between {oreMin} and {oreMax} meanwhile; Diggers {diggers}; winner {world.winner}; " +
                     $"before it went up, of {samples} seconds: no explored site {noSite}, two buildings going up {busy}, able {able} (holding {Defs.Get(UnitType.Foundry).cost + 100}+ ore in {ableRich}; its site had no spot a Foundry fits in {unplaceable}, no free Digger {noBuilder}; " +
                     $"last site {lastSite.x:0},{lastSite.y:0}, {(lastSite - map.StartPos(1)).magnitude:0} m from its base, {lastRing} of 120 ring spots free)";
        }

        void HoldOurBase()
        {
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 0 && u.def.building && u.Complete) u.hp = u.def.hp;
        }
    }

    public sealed class MapSweepTrial : BugTrial
    {
        public static int Count = 200;
        /// <summary>The finished report: the scene reload at the end takes this trial with it.</summary>
        public static string Last;

        IEnumerator Start()
        {
            var info = MapInfo.Instance;
            var rt = info != null ? info.GetComponent<MapRuntime>() : null;
            if (rt == null || rt.kit == null) { report = "no map runtime"; yield break; }
            int count = Count, mask = GameWorld.GroundAreas;
            var rng = new System.Random(20261021);
            int notJoined = 0, unreachable = 0, oreCut = 0, corridorsMaxed = 0, fewExp = 0, done = 0, offMesh = 0;
            var bad = new StringBuilder();
            var path = new UnityEngine.AI.NavMeshPath();
            float t0 = Time.realtimeSinceStartup;
            for (int k = 0; k < count; k++)
            {
                uint seed = (uint)rng.Next(1, int.MaxValue);
                MapGenerator.Result r = null;
                string threw = null;
                try { r = MapGenerator.Generate(info, rt.kit, seed, bakeNavMesh: true, sharedMaterials: false); }
                catch (System.Exception e) { threw = $"{e.GetType().Name}: {e.Message}"; }
                done++;
                if (threw != null) { bad.Append($"\n{seed}: Generate threw {threw}"); notJoined++; yield return null; continue; }
                var problems = new List<string>();
                if (r.unreachable.Count > 0) { unreachable++; problems.Add($"{r.unreachable.Count} places the generator could not join"); }
                if (r.corridors >= 4) { corridorsMaxed++; problems.Add($"{r.corridors} corridors carved (the limit)"); }
                int ores = 0; foreach (Transform o in info.oreRoot) ores++;
                if (ores < 40) { fewExp++; problems.Add($"{ores} ore seams (fewer than 4 expansions)"); }
                Vector3 a3 = default, b3 = default;
                bool aOn = UnityEngine.AI.NavMesh.SamplePosition(info.Ground(info.StartPos(0)), out var ha, 8f, mask);
                bool bOn = UnityEngine.AI.NavMesh.SamplePosition(info.Ground(info.StartPos(1)), out var hb, 8f, mask);
                if (aOn) a3 = ha.position; if (bOn) b3 = hb.position;
                if (!aOn || !bOn) { offMesh++; problems.Add($"start position off the NavMesh ({(aOn ? "" : "A ")}{(bOn ? "" : "B")})"); }
                else
                {
                    UnityEngine.AI.NavMesh.CalculatePath(a3, b3, mask, path);
                    if (path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) { notJoined++; problems.Add($"no ground path A to B ({path.status})"); }
                    int cut = 0;
                    foreach (Transform o in info.oreRoot)
                    {
                        var g = new Vector2(o.position.x, o.position.z);
                        if (!UnityEngine.AI.NavMesh.SamplePosition(info.Ground(g), out var ho, 8f, mask)) { cut++; continue; }
                        UnityEngine.AI.NavMesh.CalculatePath(a3, ho.position, mask, path);
                        if (path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) continue;
                        UnityEngine.AI.NavMesh.CalculatePath(b3, ho.position, mask, path);
                        if (path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) cut++;
                    }
                    if (cut > 0) { oreCut++; problems.Add($"{cut} ore seams no base reaches"); }
                }
                if (problems.Count > 0) bad.Append($"\n{seed}: {string.Join("; ", problems)}");
                report = $"staging ({done}/{count})";
                yield return null;
            }
            report = $"{done} random map seeds in {Time.realtimeSinceStartup - t0:0} s: bases not joined by ground {notJoined}, a start position off the NavMesh {offMesh}, " +
                     $"seeds with ore seams no base reaches {oreCut}, places the generator could not join {unreachable}, corridor limit used up {corridorsMaxed}, fewer than 4 expansions {fewExp}" + bad;
            Last = report;
            Debug.Log("[MapSweep] " + report);
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }

    public sealed class MinedOutStallTrial : BugTrial
    {
        IEnumerator Start()
        {
            world = GameWorld.Instance; map = MapInfo.Instance;
            var boot = GameBootstrap.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            if (boot.State == MatchState.Playing) { report = "a match is running: restart play and call it from the title screen"; yield break; }
            MatchSettings.spectate = true;
            MatchSettings.aiMemory = false;
            boot.StartMatch();
            yield return new WaitForSecondsRealtime(1f);
            Time.timeScale = 8f;
            yield return Wait(300f);
            int nodes = 0;
            foreach (var n in world.units.ToArray()) if (Unit.Live(n) && n.Type == UnitType.Ore) { world.Deplete(n); nodes++; }
            var a0 = boot.PlayerProxy; var a1 = boot.AI;
            int w0 = a0 != null ? a0.Dbg.waves : 0, w1 = a1 != null ? a1.Dbg.waves : 0;
            string oreAt = $"{world.factions[0].ore}/{world.factions[1].ore}", oreLater = "";
            float t0 = world.time, firstWave = -1f;
            while (world.time - t0 < 480f && world.winner < 0)
            {
                yield return Wait(1f);
                if (oreLater.Length == 0 && world.time - t0 >= 60f) oreLater = $"{world.factions[0].ore}/{world.factions[1].ore}";
                if (firstWave < 0f && ((a0 != null && a0.Dbg.waves > w0) || (a1 != null && a1.Dbg.waves > w1))) firstWave = world.time - t0;
            }
            int[] army = new int[2], structs = new int[2];
            foreach (var u in world.units)
            {
                if (!Unit.Live(u) || u.team > 1) continue;
                if (u.def.building) structs[u.team]++;
                else if (u.Type != UnitType.Worker && !u.deserted) army[u.team]++;
            }
            Time.timeScale = 1f;
            MatchSettings.spectate = false;
            report = $"{nodes} ore nodes depleted at {t0:0} s; ore {oreAt} then, {oreLater} a minute later; waves after it {(a0 != null ? a0.Dbg.waves - w0 : -1)}/{(a1 != null ? a1.Dbg.waves - w1 : -1)}, " +
                     $"the first {(firstWave >= 0f ? $"{firstWave:0} s after" : "never")}; {world.time - t0:0} s watched, winner {world.winner}; at the end army {army[0]}/{army[1]}, structures {structs[0]}/{structs[1]}";
        }

        void OnDestroy() { MatchSettings.spectate = false; }
    }
}
