// IroncladProbe.cs -- the player-side check of the Ironclad's walk (View/IroncladLegs); nothing in a
// match reads it.
//
//     StarForge -sfbench 150 -sfplay -sfholdscale 0.75 -sfironclads 12 -sflook x,z -logFile <abs>
//         [-sfnoironcladik] [-sfironcladcost]
//
// -sfironclads N puts N Ironclads of team 0 on the -sflook spot (else 45 m from team 0's start toward
// the map's middle; its log line gives the spot) once the match is up, silences both Commanders (a
// quiet map: the pair compares the walk, not a fight) and keeps each wandering within 6 m of the block: walking,
// turning, stopping. -sfnoironcladik is the other arm (no walker rigged, the legs stand still).
// -sfironcladcost times the walker itself (IroncladLegs.Profile; keep it out of an fps pair, its
// Stopwatch runs only in that arm). "[IroncladProbe]" lines in the log: the spot, then at quit how
// many stood, how many walkers were rigged, orders given, and the walker's time if timed.
using System.Collections.Generic;
using System.Globalization;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;
using UnityEngine;

namespace StarForge.Game
{
    public sealed class IroncladProbe : MonoBehaviour
    {
        int count;
        float spread, wander;   // spawn and wander radii, wider for a bigger block
        bool timed, lookSet;
        Vector2 look, spot;
        bool spawned;
        int orders, frames, rigged = -1;
        readonly List<Unit> clads = new List<Unit>();
        readonly List<float> due = new List<float>();
        readonly List<Unit> one = new List<Unit>(1);
        readonly System.Random rng = new System.Random(1000);

        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return;
            var args = System.Environment.GetCommandLineArgs();
            int n = -1;
            bool cost = false, lset = false;
            Vector2 l = default;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-sfironclads" && i + 1 < args.Length && int.TryParse(args[i + 1], out int k)) n = k;
                else if (args[i] == "-sfironcladcost") cost = true;
                else if (args[i] == "-sflook" && i + 1 < args.Length)
                {
                    var p = args[i + 1].Split(',');
                    if (p.Length >= 2 &&
                        float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    { lset = true; l = new Vector2(x, z); }
                }
            }
            if (n <= 0) return;
            var go = new GameObject(nameof(IroncladProbe));
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<IroncladProbe>();
            probe.count = n;
            probe.timed = cost;
            probe.lookSet = lset;
            probe.look = l;
            Application.quitting += probe.Report;
        }

        void Update()
        {
            var w = GameWorld.Instance;
            if (w == null || w.Map == null) return;
            if (!spawned)
            {
                // After BenchmarkRunner's start, which clears Commander.Suspended.
                if (w.time < 1f) return;
                Spawn(w);
                return;
            }
            frames++;
            if (rigged < 0 && frames == 60) rigged = Rigged();
            for (int i = 0; i < clads.Count; i++)
            {
                var u = clads[i];
                if (u == null || u.dying || w.time < due[i]) continue;
                one.Clear();
                one.Add(u);
                // One leg in five a halt: stop-start and standing turns, as in a fight.
                if (rng.NextDouble() < 0.2) { w.CmdStop(one); due[i] = w.time + Range(2f, 5f); }
                else { w.CmdMove(one, Near(w, spot, wander), false); due[i] = w.time + Range(5f, 11f); }
                orders++;
            }
        }

        void Spawn(GameWorld w)
        {
            spawned = true;
            AI.Commander.Suspended = true;
            var map = w.Map;
            if (lookSet) spot = look;
            else
            {
                Vector2 start = map.StartPos(0), mid = new Vector2(w.MapSize, w.MapSize) * 0.5f;
                spot = start + (mid - start).normalized * 45f;
            }
            spot = Near(w, spot, 0f);
            spread = Mathf.Max(8f, Mathf.Sqrt(count) * 2.5f);
            wander = spread + 6f;
            for (int i = 0; i < count; i++)
            {
                var u = w.Spawn(UnitType.Ironclad, 0, Near(w, spot, spread), true, Range(-Mathf.PI, Mathf.PI));
                clads.Add(u);
                due.Add(w.time + Range(0.5f, 3f));
            }
            IroncladLegs.Profile = timed;
            IroncladLegs.CostMs = 0.0;
            IroncladLegs.CostCalls = 0;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[IroncladProbe] {0} Ironclads of team 0 at {1:0.0},{2:0.0} (-sflook {1:0.0},{2:0.0}), Commanders silenced, walker {3}",
                count, spot.x, spot.y, timed ? "timed" : "not timed"));
        }

        /// <summary>A walkable point within r of c, dry and on the map (c itself if none is found).</summary>
        Vector2 Near(GameWorld w, Vector2 c, float r)
        {
            var map = w.Map;
            for (int tries = 0; tries < 40; tries++)
            {
                float a = Range(0f, 2f * Mathf.PI), d = r * Mathf.Sqrt((float)rng.NextDouble()) + tries * 0.5f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (map.InBounds(p, 8f) && map.WaterDepth(p) < -0.3f && w.Walkable(p, 0.5f)) return p;
            }
            return c;
        }

        int Rigged()
        {
            int n = 0;
            foreach (var u in clads)
            {
                if (u == null) continue;
                var v = u.GetComponent<UnitView>();
                if (v != null && v.Walk != null) n++;
            }
            return n;
        }

        void Report()
        {
            int alive = 0;
            foreach (var u in clads) if (u != null && !u.dying) alive++;
            if (rigged < 0) rigged = Rigged();
            string cost = !timed ? "walker not timed"
                : IroncladLegs.CostCalls == 0 ? "walker timed: no calls"
                : string.Format(CultureInfo.InvariantCulture, "walker {0:0.000} ms a frame, {1:0.0} us an Ironclad ({2} poses in {3} frames)",
                    IroncladLegs.CostMs / Mathf.Max(1, frames), IroncladLegs.CostMs * 1000.0 / IroncladLegs.CostCalls,
                    IroncladLegs.CostCalls, frames);
            Debug.Log($"[IroncladProbe] at quit: {alive} of {count} standing, walkers rigged {rigged}, {orders} orders in {frames} frames, {cost}");
        }
    }
}
