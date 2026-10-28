// AIReview.cs — play-mode checks on the opponent AI (Commander), called through the agent
// inbox (Tools/editor.sh call StarForge.EditorTools.AIReview.<Method>).
//
//   Perceive / PerceiveReport   an AI-vs-AI match at 8x on the evaluation's first seed (or
//                               the match already running): every tick, each Commander's
//                               enemy army units in sight against the memory entries it holds
//                               as in sight now. With one entry per unit the two agree; when a
//                               clump folded into one entry, the AI saw a rush as a raid.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.AI;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class AIReview
    {
        public static string Perceive()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            // A trial may have left the opponent AI silenced (a static that survives play sessions).
            Commander.Suspended = false;
            var old = Object.FindAnyObjectByType<PerceptionProbe>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("PerceptionProbe").AddComponent<PerceptionProbe>();
            return "probing; call PerceiveReport";
        }

        public static string PerceiveReport()
        {
            var p = Object.FindAnyObjectByType<PerceptionProbe>();
            return p == null ? "no probe running" : p.Report();
        }

        /// <summary>Side raids in an AI-vs-AI match at 8x (seed 1000), both sides given a taste for
        /// raids: what each raid went for against what the old rule (the Digger nearest their base's
        /// middle) would have, how long it rested after being driven off, what the squad was made
        /// of, and the Diggers that died near it against the raiders lost. Call RaidReport.</summary>
        public static string RaidTrial()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            Commander.Suspended = false;
            var old = Object.FindAnyObjectByType<RaidProbe>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("RaidProbe").AddComponent<RaidProbe>();
            return "raid trial running; call RaidReport";
        }

        public static string RaidReport()
        {
            var p = Object.FindAnyObjectByType<RaidProbe>();
            return p == null ? "no raid trial running" : p.Report();
        }

        /// <summary>BayEdgeCases case 1 alone (our bay 44 m from their base, our Mech guarding it),
        /// with the hunt rule as it is (New) or as it was (Old: hunting up to their Mech's reach);
        /// read BayEdgeCases.Report, then ResetHunt.</summary>
        public static string HuntCase1New() { MechBrain.LegacyHunt = false; BayEdgeCases.Only = 1; return BayEdgeCases.Run(); }
        public static string HuntCase1Old() { MechBrain.LegacyHunt = true; BayEdgeCases.Only = 1; return BayEdgeCases.Run(); }
        public static string ResetHunt() { MechBrain.LegacyHunt = false; BayEdgeCases.Only = -1; return "hunt rule as it is; all cases"; }

        /// <summary>The player's side cut to one Digger standing as far as it can from every base site
        /// and ore field (as one stopped on the road when its Foundry fell), once the AI has seen their
        /// Foundry, the AI given 12 riflemen: how long until it finds and kills it, with the mop-up and
        /// the open-map search (New) or without (Old). Read HiddenDiggerReport; restart play after.
        /// Every entry sets every switch it reads (a run cut short leaves them).</summary>
        public static string HiddenDiggerNew() => StartHidden(false, null, UnitType.Worker, 12);
        public static string HiddenDiggerOld() => StartHidden(true, null, UnitType.Worker, 12);
        /// <summary>The same two at the spot the first old run used, (240,102), for a like-for-like A/B.</summary>
        public static string HiddenDiggerNewAtEdge() => StartHidden(false, Edge, UnitType.Worker, 12);
        public static string HiddenDiggerOldAtEdge() => StartHidden(true, Edge, UnitType.Worker, 12);
        /// <summary>A Bunkhouse at that spot instead of the Digger, and 3 riflemen.</summary>
        public static string HiddenBunkNew() => StartHidden(false, Edge, UnitType.Bunkhouse, 3);
        public static string HiddenBunkOld() => StartHidden(true, Edge, UnitType.Bunkhouse, 3);
        /// <summary>A Foundry there, the AI's army replaced by 3 riflemen and a Skimmer and its ore held
        /// at 0 (under its push threshold for good): a mop-up wave must go on to the Foundry it finds,
        /// which ends the mop-up's own condition (an army under the threshold was called off there).</summary>
        public static string HiddenFoundryNew() => StartHidden(false, Edge, UnitType.Foundry, 3, true);
        public static string HiddenFoundryOld() => StartHidden(true, Edge, UnitType.Foundry, 3, true);
        /// <summary>The Digger hidden at that spot, the AI starved as above; once a mop-up wave is out, a
        /// Foundry of theirs is put down 18 m ahead of it: the wave must go on and take it, though the
        /// Foundry ends the mop-up's own condition and the army is under the push threshold.</summary>
        public static string MopUpFindsFoundry() => StartHidden(false, Edge, UnitType.Worker, 3, true, true);
        static readonly Vector2 Edge = new Vector2(240f, 102f);
        static string StartHidden(bool legacy, Vector2? at, UnitType hideType, int troopers, bool starve = false, bool foundryAhead = false)
        {
            HiddenDiggerProbe.FoundryAhead = foundryAhead;
            if (!EditorApplication.isPlaying) return "not playing";
            Commander.Suspended = false;
            Commander.LegacySweep = legacy;
            HiddenDiggerProbe.HideAt = at;
            HiddenDiggerProbe.HideType = hideType;
            HiddenDiggerProbe.Troopers = troopers;
            HiddenDiggerProbe.Starve = starve;
            var old = Object.FindAnyObjectByType<HiddenDiggerProbe>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("HiddenDiggerProbe").AddComponent<HiddenDiggerProbe>();
            return $"hidden {hideType} trial running ({(legacy ? "old" : "new")} search); call HiddenDiggerReport";
        }
        public static string HiddenDiggerReport()
        {
            var p = Object.FindAnyObjectByType<HiddenDiggerProbe>();
            return p == null ? "no hidden Digger trial running" : p.report;
        }

        /// <summary>A massed wave at a Mech-held base (seed 1000, AI against AI at 8x; team 1 watched):
        /// once the AI has their Mech in mind at home and no wave out, two repulses are set and it is
        /// given Maulers to just over its massed army. Over 300 s: waves started, called off and beaten
        /// back, each wave's army at its start and the least it held while on, and how it ended. One wave
        /// going on below the massed army is the rule; a wave called off at its first losses and sent
        /// again is the loop it must not make. Read MassReport; restart play after.</summary>
        public static string MassTrial()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            Commander.Suspended = false;
            Commander.LegacyNoMaxOut = false;
            Commander.LegacySweep = false;
            var old = Object.FindAnyObjectByType<MassProbe>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("MassProbe").AddComponent<MassProbe>();
            return "massed wave trial running; call MassReport";
        }
        public static string MassReport()
        {
            var p = Object.FindAnyObjectByType<MassProbe>();
            return p == null ? "no massed wave trial running" : p.report;
        }

        /// <summary>The stall rule (seed 1000, AI against AI at 8x; team 1 watched): once the AI has their
        /// Mech in mind at home and no wave out, two repulses are set (it masses) and it is given Maulers to
        /// 1.3x its plan threshold, under the massed army; from then its ore is held at 0, so the army stops
        /// growing. No plan wave should start for the first 60 s (still massing), then a massed wave on the
        /// stalled army. Reports when each wave started and why, and the massed/stalled counts. Read
        /// StallReport; restart play after.</summary>
        public static string StallTrial()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            Commander.Suspended = false;
            Commander.LegacyNoMaxOut = false;
            Commander.LegacySweep = false;
            var old = Object.FindAnyObjectByType<StallProbe>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("StallProbe").AddComponent<StallProbe>();
            return "stall rule trial running; call StallReport";
        }
        public static string StallReport()
        {
            var p = Object.FindAnyObjectByType<StallProbe>();
            return p == null ? "no stall rule trial running" : p.report;
        }

        /// <summary>Diggers against their Mech as before (Old) or as now (New), for the BugTrials trials
        /// that follow (MechInOre*). Not for an evaluation: AIEvalRunner resets it every game. It stays
        /// set for later plays until New or an evaluation.</summary>
        public static string DiggerSafetyOld() { Commander.LegacyDiggerSafety = true; return "Digger safety: old rule"; }
        public static string DiggerSafetyNew() { Commander.LegacyDiggerSafety = false; return "Digger safety: new rule"; }
        /// <summary>The way home from their Mech as before (Old: home whatever lies on the way) or as now (New:
        /// not through a tower's or bay's reach, nor past the Mech), for the BugTrials trials that follow.</summary>
        public static string WayHomeOld() { Commander.LegacyWayHome = true; return "way home: old rule"; }
        public static string WayHomeNew() { Commander.LegacyWayHome = false; return "way home: new rule"; }

        /// <summary>The Full evaluation's five games against one opponent only (0 Rusher, 1 Macro,
        /// 2 Turtle, 3 Harasser): the Macro player, who buys its Mech upgrades and turtles behind
        /// it, is where most matches run to the cap.</summary>
        public static int EvalKind = 1;
        public static string EvalOne()
        {
            if (EditorApplication.isPlaying) return "playing: stop first";
            PlayerPrefs.SetInt(AIEvalRunner.PrefOnly, EvalKind);
            PlayerPrefs.SetInt(AIEvalRunner.PrefFirst, 0);
            AIEvalMenu.Run(5, 900f);
            return $"evaluating 5 games against {(ScriptedOpponent.Kind)EvalKind}, 900 s cap";
        }

        /// <summary>The openings a Full never plays: games 5-7 of the evaluation's seed series (1000 + 37 x game,
        /// counted from 0; the evaluation line prints them as games 6-8) draw Standard, Standard and EarlyGarrison,
        /// where games 0-4 draw SkimmerFirst four times and FastExpand once. Three games against each of the four
        /// opponents, 900 s cap (~17 min).</summary>
        public static string EvalOtherOpenings()
        {
            if (EditorApplication.isPlaying) return "playing: stop first";
            PlayerPrefs.DeleteKey(AIEvalRunner.PrefOnly);
            PlayerPrefs.SetInt(AIEvalRunner.PrefFirst, 5);
            AIEvalMenu.Run(3, 900f);
            return "evaluating games 5-7 (printed 6-8: Standard, Standard, EarlyGarrison) against every opponent, 900 s cap";
        }
    }

    public sealed class HiddenDiggerProbe : MonoBehaviour
    {
        public string report = "staging";
        /// <summary>Hide the Digger here instead of the furthest spot (null: furthest).</summary>
        public static Vector2? HideAt;
        /// <summary>What is hidden; riflemen given to the AI; Starve: the AI's army replaced by those
        /// and a Skimmer (its scout), its ore held at 0.</summary>
        public static UnitType HideType = UnitType.Worker;
        public static int Troopers = 12;
        public static bool Starve;
        /// <summary>Put a Foundry of theirs ahead of the first mop-up wave (MopUpFindsFoundry).</summary>
        public static bool FoundryAhead;

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) { report = "no bootstrap"; yield break; }
            if (GameWorld.Instance == null || !GameWorld.Instance.running)
            {
                MatchSettings.spectate = false;
                MatchSettings.aiMemory = false;
                MatchSettings.fixedSeed = true;
                MatchSettings.seed = 1000;
                boot.StartMatch();
                MatchSettings.fixedSeed = false;
                yield return null;
            }
            var world = GameWorld.Instance;
            var map = world.Map;
            var ai = boot.AI;
            foreach (var cam in FindObjectsByType<Camera>()) cam.cullingMask = 0;
            Application.targetFrameRate = -1;
            Time.timeScale = 8f;
            // Until the AI has seen their Foundry (its scout), at most 400 s.
            while (ai.Perception.Snap.eFoundries == 0 && world.time < 400f) { report = $"waiting for the AI to see their Foundry ({world.time:0}s)"; yield return null; }
            if (ai.Perception.Snap.eFoundries == 0) { report = "the AI never saw their Foundry"; Time.timeScale = 1f; yield break; }
            // ...and its tactics have taken it in (the mop-up's latch), a few passes later.
            float seenFoundryAt = world.time;
            var sawField = typeof(Commander).GetField("sawFoundry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            while (!(bool)sawField.GetValue(ai) && world.time - seenFoundryAt < 10f) yield return null;
            // The walkable spot furthest from both base sites and every ore field, reachable from the AI's base.
            Vector2 a = map.StartPos(0), b = map.StartPos(1), hide = a;
            float best = -1f, size = world.MapSize;
            for (float x = 12f; x < size - 12f; x += 6f)
                for (float z = 12f; z < size - 12f; z += 6f)
                {
                    var p = new Vector2(x, z);
                    float d = Mathf.Min((p - a).magnitude, (p - b).magnitude);
                    foreach (var n in world.units)
                        if (Unit.Live(n) && n.Type == UnitType.Ore) d = Mathf.Min(d, (n.pos - p).magnitude);
                    if (d <= best || !world.Walkable(p, 0.6f)) continue;
                    var q = world.NearestReachable(b, p);
                    if ((q - p).sqrMagnitude > 3f * 3f) continue;
                    best = d; hide = p;
                }
            if (best < 0f) { report = "no hiding place found"; Time.timeScale = 1f; yield break; }
            // A fixed spot for an A/B (the furthest spot moves with the ore mined by then).
            if (HideAt.HasValue) { hide = world.NearestReachable(b, HideAt.Value); best = -1f; }
            var hidden = world.Spawn(HideType, 0, hide, true);
            string what = HideType.ToString();
            bool mechLanded = world.MechOf(0) != null;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 0 && u != hidden && u.Type != UnitType.Ore) world.Damage(u, 1e7f, u.pos, null);
            if (Starve)
                foreach (var u in world.units)
                    if (Unit.Live(u) && u.team == 1 && (u.Type == UnitType.Trooper || u.Type == UnitType.Mauler || u.Type == UnitType.Skimmer))
                        world.Damage(u, 1e7f, u.pos, null);
            for (int i = 0; i < Troopers; i++) world.Spawn(UnitType.Trooper, 1, b + new Vector2((i % 4) * 2f - 3f, (i / 4) * 2f + 8f), true);
            if (Starve) world.Spawn(UnitType.Skimmer, 1, b + new Vector2(0f, 14f), true);
            // Killed out of its sight, the AI would believe their base and towers stand until it looks
            // again, which a starved army under its threshold never does: with a Foundry put ahead of
            // the mop-up, it is given what it would have seen (everything of theirs gone).
            if (FoundryAhead)
                ((System.Collections.IList)typeof(Perception).GetField("mem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ai.Perception)).Clear();
            else world.factions[1].ore += 3000;
            var waveOnField = typeof(Commander).GetField("waveOn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            bool WaveOn() => (bool)waveOnField.GetValue(ai);
            var mopUpField = typeof(Commander).GetField("mopUpWave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Unit ahead = null;
            float aheadAt = -1f, aheadArmy = 0f, aheadNeed = 0f, aheadDead = -1f;
            int calledAtAhead = 0;
            bool aheadWaveAfter = false, aheadChecked = false;
            float nextTrace = 0f;
            float t0 = world.time, seenAt = -1f, armyAtSight = 0f, needAtSight = 0f;
            bool waveAtSight = false, waveAfter = false, checkedAfter = false;
            int calledAtSight = 0, sweeps0 = ai.Dbg.sweeps;
            while (world.time - t0 < 900f && world.winner < 0)
            {
                if (Starve) world.factions[1].ore = 0;
                if (FoundryAhead && ahead == null && (bool)mopUpField.GetValue(ai) && WaveOn())
                {
                    Vector2 c = Vector2.zero; int n = 0;
                    foreach (var u in world.units)
                        if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Trooper) { c += u.pos; n++; }
                    if (n > 0)
                    {
                        c /= n;
                        Vector2 dir = c - b; dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector2.right;
                        ahead = world.Spawn(UnitType.Foundry, 0, world.NearestReachable(b, c + dir * 18f), true);
                        aheadAt = world.time - t0; aheadArmy = ai.Perception.Snap.armyValue; aheadNeed = ai.Dbg.pushThreshold;
                        calledAtAhead = ai.Dbg.wavesCalledOff;
                    }
                }
                if (FoundryAhead && world.time >= nextTrace)
                {
                    nextTrace = world.time + 20f;
                    const System.Reflection.BindingFlags P = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var sn = ai.Perception.Snap;
                    var mainList = (System.Collections.IList)typeof(Commander).GetField("main", P).GetValue(ai);
                    Debug.Log($"[MopUpTrace] {world.time - t0:0}s eFoundries {sn.eFoundries} sawFoundry {typeof(Commander).GetField("sawFoundry", P).GetValue(ai)} eMech {sn.eMech} " +
                              $"strength {typeof(Commander).GetMethod("TheirStrength", P).Invoke(ai, new object[] { sn })} army {sn.armyValue} main {mainList.Count} waveOn {WaveOn()} " +
                              $"threshold {ai.Dbg.pushThreshold} waves {ai.Dbg.waves} plan {ai.Dbg.strategy} remembered {ai.Perception.Enemies.Count}");
                }
                if (ahead != null && !aheadChecked && world.time - t0 >= aheadAt + 15f) { aheadChecked = true; aheadWaveAfter = WaveOn(); }
                if (ahead != null && aheadDead < 0f && !Unit.Live(ahead)) aheadDead = world.time - t0;
                if (FoundryAhead && aheadDead >= 0f && aheadChecked) break;
                if (seenAt < 0f && Unit.Live(hidden) && world.Visible(1, hidden.pos))
                {
                    seenAt = world.time - t0;
                    armyAtSight = ai.Perception.Snap.armyValue; needAtSight = ai.Dbg.pushThreshold;
                    waveAtSight = WaveOn(); calledAtSight = ai.Dbg.wavesCalledOff;
                }
                if (seenAt >= 0f && !checkedAfter && world.time - t0 >= seenAt + 15f) { checkedAfter = true; waveAfter = WaveOn() || world.winner >= 0; }
                report = $"running {world.time - t0:0}s (search {(Commander.LegacySweep ? "old" : "new")})";
                yield return null;
            }
            Time.timeScale = 1f;
            report = $"done: search {(Commander.LegacySweep ? "old" : "new")}; hidden {what} {best:0} m from the nearest ore field or base site, at ({hide.x:0},{hide.y:0}), hidden at {t0:0}s (their Mech {(mechLanded ? "landed" : "not landed")}); " +
                     (seenAt >= 0f ? $"first seen by the AI after {seenAt:0} s with its army {armyAtSight:0} against a push threshold of {needAtSight:0}, a wave {(waveAtSight ? "on" : "not on")}, 15 s later {(checkedAfter ? (waveAfter ? "on" : "not on") : "n/a")}, waves called off since {ai.Dbg.wavesCalledOff - calledAtSight}; " : "never seen by the AI; ") +
                     (world.winner >= 0 ? $"match over after {world.time - t0:0} s, winner {world.winner}" : $"no winner after {world.time - t0:0} s ({what} {(Unit.Live(hidden) ? "alive" : "dead")})") +
                     (FoundryAhead ? (ahead == null ? "; no mop-up wave went out" : $"; a Foundry put 18 m ahead of the mop-up wave at {aheadAt:0} s (army {aheadArmy:0} against a push threshold of {aheadNeed:0}): wave on 15 s later {(aheadChecked ? (aheadWaveAfter ? "yes" : "no") : "n/a")}, called off since {ai.Dbg.wavesCalledOff - calledAtAhead}, the Foundry {(aheadDead >= 0f ? $"down {aheadDead - aheadAt:0} s after" : "standing")}") : "") +
                     $"; AI waves {ai.Dbg.waves}, last {ai.Dbg.wave}; sweeps {ai.Dbg.sweeps - sweeps0}, longest {ai.Dbg.sweepMsMax:0.00} ms (restart play now)";
            Commander.LegacySweep = false;
        }
    }

    public sealed class MassProbe : MonoBehaviour
    {
        public string report = "staging";
        // Spectating is the trial's, not the next match's.
        void OnDestroy() => MatchSettings.spectate = false;

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) { report = "no bootstrap"; yield break; }
            if (GameWorld.Instance == null || !GameWorld.Instance.running)
            {
                MatchSettings.spectate = true;   // AI against AI: the other side raises a bay and its Mech lands
                MatchSettings.aiMemory = false;
                MatchSettings.fixedSeed = true;
                MatchSettings.seed = 1000;
                boot.StartMatch();
                MatchSettings.fixedSeed = false;
                yield return null;
            }
            var world = GameWorld.Instance;
            var ai = boot.AI;
            foreach (var cam in FindObjectsByType<Camera>()) cam.cullingMask = 0;
            Application.targetFrameRate = -1;
            Time.timeScale = 8f;
            const System.Reflection.BindingFlags Priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var waveOnField = typeof(Commander).GetField("waveOn", Priv);
            var repulsesField = typeof(Commander).GetField("mechRepulses", Priv);
            bool WaveOn() => (bool)waveOnField.GetValue(ai);
            // Their Mech landed and in the AI's mind, no wave out; then two repulses, until it masses
            // (their Mech at home: MaxingOut). At most 900 s.
            while (world.time < 900f)
            {
                var m = world.MechOf(0);
                if (m != null && m.mech != null && m.mech.Landed && ai.Perception.Snap.eMech && !WaveOn())
                {
                    repulsesField.SetValue(ai, 2);
                    if (ai.Dbg.massedArmy > 0f) break;
                }
                report = $"waiting for their Mech at home in the AI's mind ({world.time:0}s)";
                yield return null;
            }
            if (ai.Dbg.massedArmy <= 0f) { report = "never massing (their Mech never seen at home)"; Time.timeScale = 1f; yield break; }
            float need = ai.Dbg.massedArmy, have = ai.Perception.Snap.armyValue;
            int maulers = Mathf.Max(0, Mathf.CeilToInt((need * 1.08f - have) / 150f));
            Vector2 b = world.Map.StartPos(1);
            for (int i = 0; i < maulers; i++) world.Spawn(UnitType.Mauler, 1, b + new Vector2((i % 6) * 3f - 7.5f, (i / 6) * 3f + 10f), true);
            float t0 = world.time;
            int w0 = ai.Dbg.waves, c0 = ai.Dbg.wavesCalledOff, r0 = ai.Retreats, m0 = ai.Dbg.massedWaves;
            var waves = new StringBuilder();
            bool on = false;
            float startArmy = 0f, minArmy = 0f, startT = 0f;
            int calls = c0, rets = r0;
            while (world.time - t0 < 300f && world.winner < 0)
            {
                float army = ai.Perception.Snap.armyValue;
                bool now = WaveOn();
                if (now && !on) { startArmy = minArmy = army; startT = world.time - t0; }
                if (now) minArmy = Mathf.Min(minArmy, army);
                if (!now && on)
                {
                    string how = ai.Dbg.wavesCalledOff > calls ? "called off" : ai.Retreats > rets ? "beaten back" : "ended";
                    waves.Append($"[{startT:0}-{world.time - t0:0}s, army {startArmy:0} -> least {minArmy:0}, {how}] ");
                }
                calls = ai.Dbg.wavesCalledOff; rets = ai.Retreats;
                on = now;
                report = $"running {world.time - t0:0}s: waves {ai.Dbg.waves - w0}";
                yield return null;
            }
            if (on) waves.Append($"[{startT:0}s-, army {startArmy:0} -> least {minArmy:0}, still on] ");
            Time.timeScale = 1f;
            var their = world.MechOf(0);
            report = $"done: massed army {need:0} (it had {have:0}, given {maulers} Maulers), plan threshold {ai.Dbg.pushThreshold:0}; over {world.time - t0:0} s: " +
                     $"waves {ai.Dbg.waves - w0} (massed {ai.Dbg.massedWaves - m0}, {ai.Dbg.stalledWaves} on a stalled army, first at {(ai.Dbg.firstMassedAt >= 0f ? ai.Dbg.firstMassedAt - t0 : -1f):0} s), called off {ai.Dbg.wavesCalledOff - c0}, beaten back {ai.Retreats - r0}, repulses now {repulsesField.GetValue(ai)}; " +
                     $"{waves}; winner {world.winner}; their Mech {(their != null ? $"{their.hp / their.MaxHp * 100f:0}%, {their.kills} kills" : "gone")}; " +
                     $"army now {ai.Perception.Snap.armyValue:0} (restart play now)";
        }
    }

    public sealed class StallProbe : MonoBehaviour
    {
        public string report = "staging";
        void OnDestroy() => MatchSettings.spectate = false;

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) { report = "no bootstrap"; yield break; }
            if (GameWorld.Instance == null || !GameWorld.Instance.running)
            {
                MatchSettings.spectate = true;
                MatchSettings.aiMemory = false;
                MatchSettings.fixedSeed = true;
                MatchSettings.seed = 1000;
                boot.StartMatch();
                MatchSettings.fixedSeed = false;
                yield return null;
            }
            var world = GameWorld.Instance;
            var ai = boot.AI;
            foreach (var cam in FindObjectsByType<Camera>()) cam.cullingMask = 0;
            Application.targetFrameRate = -1;
            Time.timeScale = 8f;
            const System.Reflection.BindingFlags Priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var waveOnField = typeof(Commander).GetField("waveOn", Priv);
            var repulsesField = typeof(Commander).GetField("mechRepulses", Priv);
            bool WaveOn() => (bool)waveOnField.GetValue(ai);
            while (world.time < 900f)
            {
                var m = world.MechOf(0);
                if (m != null && m.mech != null && m.mech.Landed && ai.Perception.Snap.eMech && !WaveOn())
                {
                    repulsesField.SetValue(ai, 2);
                    if (ai.Dbg.massedArmy > 0f) break;
                }
                report = $"waiting for their Mech at home in the AI's mind ({world.time:0}s)";
                yield return null;
            }
            if (ai.Dbg.massedArmy <= 0f) { report = "never massing (their Mech never seen at home)"; Time.timeScale = 1f; yield break; }
            float need = ai.Dbg.massedArmy, threshold = ai.Dbg.pushThreshold, have = ai.Perception.Snap.armyValue;
            float aim = Mathf.Min(threshold * 1.3f, need * 0.85f);
            int maulers = Mathf.Max(0, Mathf.CeilToInt((aim - have) / 150f));
            Vector2 b = world.Map.StartPos(1);
            for (int i = 0; i < maulers; i++) world.Spawn(UnitType.Mauler, 1, b + new Vector2((i % 6) * 3f - 7.5f, (i / 6) * 3f + 10f), true);
            float t0 = world.time;
            int w0 = ai.Dbg.waves, c0 = ai.Dbg.wavesCalledOff, r0 = ai.Retreats, m0 = ai.Dbg.massedWaves, s0 = ai.Dbg.stalledWaves;
            var by0 = (int[])ai.Dbg.mechWavesBy.Clone();
            var waves = new StringBuilder();
            bool on = false;
            float startArmy = 0f, startT = 0f;
            int calls = c0, rets = r0, lastBy = 0;
            string[] why = { "threshold", "its Mech's call", "their Mech away", "their Mech hurt", "broke" };
            while (world.time - t0 < 240f && world.winner < 0)
            {
                world.factions[1].ore = 0;   // nothing more to train: the army stops growing
                if ((int)repulsesField.GetValue(ai) < 2) repulsesField.SetValue(ai, 2);
                bool now = WaveOn();
                if (now && !on)
                {
                    startArmy = ai.Perception.Snap.armyValue; startT = world.time - t0;
                    string cause = "not at a Mech";
                    for (int k = 0; k < 5; k++) if (ai.Dbg.mechWavesBy[k] > by0[k]) { cause = why[k]; by0[k] = ai.Dbg.mechWavesBy[k]; }
                    waves.Append($"[{startT:0}s, army {startArmy:0}, {cause}{(ai.Dbg.stalledWaves > s0 + lastBy ? ", stalled" : "")}");
                    lastBy = ai.Dbg.stalledWaves - s0;
                }
                if (!now && on)
                {
                    string how = ai.Dbg.wavesCalledOff > calls ? "called off" : ai.Retreats > rets ? "beaten back" : "ended";
                    waves.Append($" -> {how} at {world.time - t0:0}s] ");
                }
                calls = ai.Dbg.wavesCalledOff; rets = ai.Retreats;
                on = now;
                report = $"running {world.time - t0:0}s: waves {ai.Dbg.waves - w0}";
                yield return null;
            }
            if (on) waves.Append(" -> still on] ");
            Time.timeScale = 1f;
            report = $"done: plan threshold {threshold:0}, massed army {need:0}, it had {have:0} and was given {maulers} Maulers (aim {aim:0}), ore held at 0; over {world.time - t0:0} s: " +
                     $"waves {ai.Dbg.waves - w0} (massed {ai.Dbg.massedWaves - m0}, {ai.Dbg.stalledWaves - s0} on a stalled army, first massed at {(ai.Dbg.firstMassedAt >= t0 ? ai.Dbg.firstMassedAt - t0 : -1f):0} s), " +
                     $"called off {ai.Dbg.wavesCalledOff - c0}, beaten back {ai.Retreats - r0}; {waves}; winner {world.winner} (restart play now)";
        }
    }

    public sealed class RaidProbe : MonoBehaviour
    {
        GameWorld world;
        readonly int[] picks = new int[2], picksSameAsOld = new int[2], sorties = new int[2], badSquad = new int[2], squadTicks = new int[2];
        readonly double[] infNew = new double[2], infOld = new double[2], distNew = new double[2], distOld = new double[2];
        readonly float[] retreatAt = { -1f, -1f }, minRest = { 1e9f, 1e9f };
        readonly int[] rests = new int[2], diggersNear = new int[2], raidersLost = new int[2], maxSquad = new int[2];
        readonly Unit[] lastTarget = new Unit[2];
        readonly bool[] wasOut = new bool[2];
        readonly int[] lastSorties = new int[2];
        readonly HashSet<Unit> everRaider = new HashSet<Unit>();

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) yield break;
            if (GameWorld.Instance == null || !GameWorld.Instance.running)
            {
                MatchSettings.spectate = true;
                MatchSettings.aiMemory = false;
                MatchSettings.fixedSeed = true;
                MatchSettings.seed = 1000;
                boot.StartMatch();
                MatchSettings.fixedSeed = false;
                yield return null;
            }
            world = GameWorld.Instance;
            boot.AI.Personality.raidTaste = 0.9f;
            if (boot.PlayerProxy != null) boot.PlayerProxy.Personality.raidTaste = 0.9f;
            foreach (var cam in FindObjectsByType<Camera>()) cam.cullingMask = 0;
            Application.targetFrameRate = -1;
            Time.timeScale = 8f;
            world.Ticked += OnTick;
            world.Event += OnEvent;
        }

        void OnDestroy()
        {
            if (world != null) { world.Ticked -= OnTick; world.Event -= OnEvent; }
            MatchSettings.spectate = false;   // the trial's, not the next match's
        }

        void OnEvent(GameEvent e)
        {
            if (e.kind != GameEventKind.Death || e.unit == null) return;
            var boot = GameBootstrap.Instance;
            foreach (var c in new[] { boot.PlayerProxy, boot.AI })
            {
                if (c == null) continue;
                int t = c.Team;
                // A raider lost while in the squad (one back with the army is the army's).
                bool inSquad = false;
                foreach (var r in c.Raiders) if (r == e.unit) { inSquad = true; break; }
                if (inSquad && e.unit.team == t) raidersLost[t]++;
                // A Digger of theirs dying within 20 m of a raider of ours on a sortie.
                if (e.type == UnitType.Worker && e.team == 1 - t && wasOut[t])
                    foreach (var r in c.Raiders)
                        if (Unit.Live(r) && (r.pos - new Vector2(e.pos.x, e.pos.z)).magnitude < 20f) { diggersNear[t]++; break; }
            }
        }

        void OnTick(float dt)
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || world.winner >= 0) return;
            foreach (var c in new[] { boot.PlayerProxy, boot.AI }) if (c != null) Sample(c);
        }

        void Sample(Commander c)
        {
            int t = c.Team;
            var raiders = c.Raiders;
            if (raiders.Count == 0) { wasOut[t] = false; return; }
            foreach (var r in raiders) everRaider.Add(r);
            squadTicks[t]++;
            maxSquad[t] = Mathf.Max(maxSquad[t], raiders.Count);
            bool harassPlan = c.Dbg.strategy == Strategy.Harass;
            if (!harassPlan)
            {
                bool bad = raiders.Count > 2;
                foreach (var r in raiders) if (r.Type != UnitType.Skimmer) bad = true;
                if (bad) badSquad[t]++;
            }
            // Out on a sortie (attacking), or pulled back (a plain move).
            bool attacking = false, retreating = false;
            Unit target = null;
            Vector2 hc = Vector2.zero;
            foreach (var r in raiders)
            {
                hc += r.pos;
                if (r.order == Order.Attack || r.order == Order.AttackMove) { attacking = true; if (r.order == Order.Attack && Unit.Live(r.target)) target = r.target; }
                else if (r.order == Order.Move) retreating = true;
            }
            hc /= raiders.Count;
            if (retreating && !attacking && wasOut[t]) retreatAt[t] = world.time;
            // A sortie is one the Commander sent (its own count), not a Skimmer joining with its
            // wave's attack-move still on.
            if (c.Dbg.raidSorties > lastSorties[t])
            {
                sorties[t] += c.Dbg.raidSorties - lastSorties[t];
                lastSorties[t] = c.Dbg.raidSorties;
                if (retreatAt[t] > 0f) { float gap = world.time - retreatAt[t]; minRest[t] = Mathf.Min(minRest[t], gap); rests[t]++; retreatAt[t] = -1f; }
            }
            wasOut[t] = attacking;
            // A new Digger picked: against the one the old rule took (nearest their base's middle).
            if (target != null && target.Type == UnitType.Worker && target != lastTarget[t])
            {
                lastTarget[t] = target;
                Unit oldPick = null;
                float best = 1e30f;
                Vector2 eb = c.Perception.Snap.enemyBase;
                foreach (var e in world.units)
                {
                    if (e == null || e.dying || e.team != 1 - t || e.Type != UnitType.Worker || !world.Visible(t, e.pos)) continue;
                    float d = (e.pos - eb).magnitude;
                    if (d < best) { best = d; oldPick = e; }
                }
                if (oldPick == null) return;
                picks[t]++;
                if (oldPick == target) picksSameAsOld[t]++;
                infNew[t] += c.Influence.Sample(1, target.pos); infOld[t] += c.Influence.Sample(1, oldPick.pos);
                distNew[t] += (target.pos - hc).magnitude; distOld[t] += (oldPick.pos - hc).magnitude;
            }
        }

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"match time {(world != null ? world.time : 0f):0}s{(world != null && world.winner >= 0 ? $", won by team {world.winner}" : "")}");
            var boot = GameBootstrap.Instance;
            foreach (var c in new[] { boot != null ? boot.PlayerProxy : null, boot != null ? boot.AI : null })
            {
                if (c == null) continue;
                int t = c.Team;
                sb.AppendLine($"team {t}: sorties {sorties[t]} (Dbg {c.Dbg.raidSorties}, {c.Dbg.sideRaidSorties} beside another plan); squad at most {maxSquad[t]}, " +
                              $"ticks with more than two or a non-Skimmer outside the Harass plan {badSquad[t]} of {squadTicks[t]}; " +
                              $"rest after being driven off: {(rests[t] > 0 ? $"{rests[t]} times, shortest {minRest[t]:0.0}s" : "never driven off")}; " +
                              $"Digger picks {picks[t]} ({picksSameAsOld[t]} the same as the old rule's), enemy influence at the pick {(picks[t] > 0 ? infNew[t] / picks[t] : 0):0.00} against the old rule's {(picks[t] > 0 ? infOld[t] / picks[t] : 0):0.00}, " +
                              $"distance from the raiders {(picks[t] > 0 ? distNew[t] / picks[t] : 0):0} m against {(picks[t] > 0 ? distOld[t] / picks[t] : 0):0} m; " +
                              $"their Diggers died near the raiders {diggersNear[t]}, raiders lost {raidersLost[t]}");
            }
            return sb.ToString();
        }
    }

    public sealed class PerceptionProbe : MonoBehaviour
    {
        readonly long[] ticks = new long[2], under = new long[2];
        readonly double[] truth = new double[2], held = new double[2], truthV = new double[2], heldV = new double[2], oldHeld = new double[2];
        readonly List<Vector2> oldPos = new List<Vector2>();
        readonly List<UnitType> oldType = new List<UnitType>();
        readonly int[] worstTruth = new int[2], worstHeld = new int[2];
        GameWorld world;

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) yield break;
            if (GameWorld.Instance == null || !GameWorld.Instance.running)
            {
                MatchSettings.spectate = true;
                MatchSettings.aiMemory = false;
                MatchSettings.fixedSeed = true;
                MatchSettings.seed = 1000;
                boot.StartMatch();
                // A static: left on, every later match in this session would play seed 1000.
                MatchSettings.fixedSeed = false;
                yield return null;
            }
            world = GameWorld.Instance;
            Time.timeScale = 8f;
            world.Ticked += OnTick;
        }

        void OnDestroy()
        {
            if (world != null) world.Ticked -= OnTick;
            MatchSettings.spectate = false;   // the trial's, not the next match's
        }

        void OnTick(float dt)
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || world.winner >= 0) return;
            Sample(boot.PlayerProxy);
            Sample(boot.AI);
            if (world.time >= nextEco) { nextEco += 60f; Economy(); }
        }

        // Once a minute: what each side mined in the last minute, what ore is left by its
        // Foundries, how many it has, and how many of its Diggers stand idle.
        float nextEco = 60f;
        readonly int[] minedBefore = new int[2];
        readonly StringBuilder eco = new StringBuilder();

        void Economy()
        {
            eco.Append($"{world.time / 60f:0}m:");
            for (int t = 0; t < 2; t++)
            {
                var F = world.factions[t];
                int foundries = 0, workers = 0, idle = 0, oreBy = 0, far = 0;
                foreach (var u in world.units)
                {
                    if (u == null || u.dying || u.team != t) continue;
                    if (u.Type == UnitType.Foundry && u.Complete) foundries++;
                    if (u.Type == UnitType.Worker)
                    {
                        workers++;
                        if (u.order == Order.Idle) idle++;
                        // Mining from a node over 30 m from the Foundry it takes the ore to.
                        if ((u.order == Order.Harvest || u.order == Order.Return) && Unit.Live(u.harvestNode))
                        {
                            var dp = world.NearestDropoff(u);
                            if (dp != null && (dp.pos - u.harvestNode.pos).magnitude > 30f) far++;
                        }
                    }
                }
                foreach (var n in world.units)
                {
                    if (n == null || n.dying || n.Type != UnitType.Ore || n.oreLeft <= 0) continue;
                    foreach (var f in world.units)
                        if (f != null && !f.dying && f.team == t && f.Type == UnitType.Foundry && f.Complete && (f.pos - n.pos).magnitude < 25f)
                        { oreBy += n.oreLeft; break; }
                }
                eco.Append($"  T{t} +{F.oreMined - minedBefore[t]}/min, {F.ore} banked, {oreBy} left by {foundries} Foundr{(foundries == 1 ? "y" : "ies")}, " +
                           $"{workers} Diggers ({idle} idle, {far} hauling from over 30 m)");
                minedBefore[t] = F.oreMined;
            }
            eco.AppendLine();
        }

        void Sample(Commander c)
        {
            if (c == null) return;
            int t = c.Team;
            int n = 0, m = 0;
            float nv = 0f, mv = 0f;
            oldPos.Clear();
            oldType.Clear();
            foreach (var e in world.units)
            {
                if (e == null || e.dying || e.team != 1 - t || !e.def.IsArmy) continue;
                if (!world.Visible(t, e.pos)) continue;
                n++;
                nv += Defs.ArmyValue(e.Type);
                // The matching Perception had before: each sighting folded into the first entry
                // of its kind within 7 m, which then moved onto it -- so a clump chained into one.
                bool merged = false;
                for (int k = 0; k < oldPos.Count && !merged; k++)
                    if (oldType[k] == e.Type && (oldPos[k] - e.pos).magnitude <= 7f) { oldPos[k] = e.pos; merged = true; }
                if (!merged) { oldPos.Add(e.pos); oldType.Add(e.Type); }
            }
            foreach (var r in c.Perception.Enemies)
            {
                if (!r.stillVisible || !Defs.Get(r.type).IsArmy) continue;
                m++;
                mv += Defs.ArmyValue(r.type);
            }
            if (n == 0) return;
            ticks[t]++;
            truth[t] += n; held[t] += m; oldHeld[t] += oldPos.Count;
            truthV[t] += nv; heldV[t] += mv;
            if (m < n) under[t]++;
            if (n - m > worstTruth[t] - worstHeld[t]) { worstTruth[t] = n; worstHeld[t] = m; }
        }

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"match time {(world != null ? world.time : 0f):0}s{(world != null && world.winner >= 0 ? $", won by team {world.winner}" : "")}");
            for (int t = 0; t < 2; t++)
            {
                if (ticks[t] == 0) { sb.AppendLine($"team {t}: no enemy army in sight yet"); continue; }
                sb.AppendLine($"team {t}: {ticks[t]} ticks with enemy army in sight; in sight {truth[t] / ticks[t]:0.0} units on average, " +
                              $"held as in sight {held[t] / ticks[t]:0.0} ({100 * held[t] / truth[t]:0}% of the units, {100 * heldV[t] / truthV[t]:0}% of the value); " +
                              $"under-counted in {100.0 * under[t] / ticks[t]:0}% of ticks; worst {worstHeld[t]} held of {worstTruth[t]} in sight; " +
                              $"the old first-come matching would hold {oldHeld[t] / ticks[t]:0.0} ({100 * oldHeld[t] / truth[t]:0}%)");
            }
            var boot = GameBootstrap.Instance;
            foreach (var c in new[] { boot != null ? boot.PlayerProxy : null, boot != null ? boot.AI : null })
                if (c != null) sb.AppendLine($"team {c.Team} Commander: {c.Dbg.waves} waves, {c.Retreats} retreats, plan {c.Dbg.strategy}, reads the other as {c.Dbg.believed}");
            sb.Append(eco);
            return sb.ToString();
        }
    }
}
