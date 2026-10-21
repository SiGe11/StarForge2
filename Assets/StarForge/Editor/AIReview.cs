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
