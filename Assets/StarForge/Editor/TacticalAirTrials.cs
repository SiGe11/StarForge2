// TacticalAirTrials.cs -- the checks for the new combat's dogfights (phase 7): Swift against Swift, how long a duel
// lasts and over how many passes, hits per shot at 1x and 8x, and how near the ground a plane comes.
// Called from a fresh Play at the menu, like TacticalTrials.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class TacticalAir
    {
        static string Stage<T>(string name) where T : BugTrial
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<T>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject(name + "Trial").AddComponent<T>();
            return $"staging; call {name}Report in a few minutes";
        }

        static string Report<T>() where T : BugTrial
        {
            var r = Object.FindAnyObjectByType<T>();
            return r == null ? "no trial running" : r.report;
        }

        /// <summary>Two Swifts sent at each other from 60 m, three duels at 1x and three at 8x (120 s cap each). Target
        /// (phase 7): duels of 20-60 s over several passes, hits per shot the same at 1x and 8x, never below 4 m over the
        /// ground.</summary>
        public static string Dogfight() => Stage<DogfightTrial>("Dogfight");
        public static string DogfightReport() => Report<DogfightTrial>();
        public static string TailChase() => Stage<TailChaseTrial>("TailChase");
        public static string TailChaseReport() => Report<TailChaseTrial>();
    }

    /// <summary>A Swift flying straight on with an enemy Swift 15 m behind it, chasing: does it break (PlaneBreaks,
    /// ChaserBehind)? Five runs at the trials' 3x (HoldStatics). Target: a break in every run, within 5 s. The count is the
    /// world's (the hunter's breaks too); the first is the prey's by the staging.</summary>
    public sealed class TailChaseTrial : TacticalTrial
    {
        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new System.Text.StringBuilder();
            HoldStatics();
            var rng = new System.Random(5151);
            int runs = 0, broke = 0;
            for (int k = 0; k < 5; k++)
            {
                if (!FindRange(rng, 60f, out var a, out var d)) continue;
                runs++;
                var prey = Put(UnitType.Swift, 0, a);
                var hunter = Put(UnitType.Swift, 1, a - d * 15f);
                prey.yaw = hunter.yaw = Mathf.Atan2(d.x, d.y);
                world.CmdMove(new List<Unit> { prey }, world.ClampToMap(a + d * 400f), false);
                world.CmdAttack(new List<Unit> { hunter }, prey);
                int b0 = world.PlaneBreaks;
                float t0 = world.time, first = -1f;
                while (world.time - t0 < 12f && Unit.Live(prey) && Unit.Live(hunter))
                {
                    if (first < 0f && world.PlaneBreaks > b0) first = world.time - t0;
                    yield return null;
                }
                int n = world.PlaneBreaks - b0;
                if (first >= 0f && first <= 5f) broke++;
                sb.AppendLine($"  run {k}: breaks {n}, first after {(first < 0f ? "never" : first.ToString("0.0") + " s")}, the prey {(Unit.Live(prey) ? $"alive at {prey.hp:0}" : "down")}");
                ClearSpawned();
                yield return null;
            }
            report = $"{runs} tail chases: broke within 5 s in {broke} (target all)\n" + sb;
            ReleaseStatics();
        }
    }

    public sealed class DogfightTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        Unit a0, a1;
        int shots;

        void OnFire(GameEvent e)
        {
            if (e.kind == GameEventKind.Fire && (e.unit == a0 || e.unit == a1) && e.unit != null) shots++;
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            ListenWhileHeld(OnFire);
            float[] scales = { 1f, 8f };
            var rng = new System.Random(4242);
            foreach (float scale in scales)
            {
                float durSum = 0f, minClear = float.MaxValue;
                int cs0 = world.CannonShotsAtAir, ch0 = world.CannonHitsAtAir, mf0 = world.AirMissilesFired, fl0 = world.FlaresPopped, br0 = world.PlaneBreaks;
                int duels = 0, passSum = 0, hitSum = 0, shotSum = 0, decided = 0;
                for (int k = 0; k < 3; k++)
                {
                    if (!FindRange(rng, 60f, out var a, out var d)) continue;
                    duels++;
                    a0 = Put(UnitType.Swift, 0, a);
                    a1 = Put(UnitType.Swift, 1, a + d * 60f);
                    world.CmdAttack(new List<Unit> { a0 }, a1);
                    world.CmdAttack(new List<Unit> { a1 }, a0);
                    Time.timeScale = scale;
                    shots = 0;
                    float hp0 = a0.hp, hp1 = a1.hp, t0 = world.time, lastD = (a0.pos - a1.pos).magnitude;
                    int hits = 0, passes = 0;
                    bool closing = true;
                    while (world.time - t0 < 120f && Unit.Live(a0) && Unit.Live(a1))
                    {
                        if (a0.hp < hp0) { hits++; hp0 = a0.hp; }
                        if (a1.hp < hp1) { hits++; hp1 = a1.hp; }
                        float dd = (a0.pos - a1.pos).magnitude;
                        if (closing && dd > lastD + 0.05f) { if (lastD < 25f) passes++; closing = false; }
                        else if (!closing && dd < lastD - 0.05f) closing = true;
                        lastD = dd;
                        minClear = Mathf.Min(minClear, a0.def.altitude - a0.dip, a1.def.altitude - a1.dip);
                        yield return null;
                    }
                    float dur = world.time - t0;
                    bool done = !Unit.Live(a0) || !Unit.Live(a1);
                    if (done) decided++;
                    durSum += dur;
                    passSum += passes;
                    hitSum += hits;
                    shotSum += shots;
                    sb.AppendLine($"  {scale:0}x duel {k}: {(done ? "decided" : "no kill")} after {dur:0} s, {passes} passes, {hits} hits in {shots} shots");
                    ClearSpawned();
                    yield return null;
                }
                int cs = world.CannonShotsAtAir - cs0, ch = world.CannonHitsAtAir - ch0;
                sb.Insert(0, $"{scale:0}x: {duels} duels, {decided} decided, mean {durSum / Mathf.Max(1, duels):0} s, {passSum / Mathf.Max(1f, duels):0.0} passes, cannon hits per shot {(cs > 0 ? (float)ch / cs : 0f):0.00} ({ch} of {cs}), missiles {world.AirMissilesFired - mf0}, flares {world.FlaresPopped - fl0}, breaks {world.PlaneBreaks - br0}, lowest {minClear:0.0} m over the ground\n");
            }
            report = "Dogfight (target: 20-60 s, several passes, hits per shot alike at 1x and 8x, never under 4 m)\n" + sb;
            ReleaseStatics();
        }
    }
}
