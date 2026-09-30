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
