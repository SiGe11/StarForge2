// InfluenceMap.cs — spatial influence fields: friendly strength, believed enemy
// strength, threat and information age on a 64x64 grid.
//
// The field is the one AI workload that is genuinely wide parallel arithmetic
// (every cell accumulates a falloff term from every unit), so it has a GPU
// compute backend (InfluenceComputeBackend). Following hardware.md, the CPU
// path stays as the fallback and the inspector shows the measured timings.
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.AI
{
    [StructLayout(LayoutKind.Sequential)]
    public struct InfluenceUnit   // 32 bytes, matches the compute buffer layout
    {
        public float x, z, strength, range;
        public float team, pad0, pad1, pad2;
    }

    public interface IInfluenceBackend
    {
        string Name { get; }
        /// <summary>Fill channels 0-2 of <paramref name="field"/>. May return a result that
        /// is a frame or two old (asynchronous readback); false means "use the CPU".</summary>
        bool Compute(InfluenceUnit[] units, int count, int gridN, float cellSize, float[] field);
        float LastMs { get; }
    }

    public sealed class InfluenceMap
    {
        public const int N = 64;
        public const int CHANNELS = 4;   // 0 friendly, 1 enemy, 2 threat, 3 unseen

        IInfluenceBackend backend;
        InfluenceUnit[] units = new InfluenceUnit[512];
        int count;
        readonly float[] field = new float[N * N * CHANNELS];
        float cell = 4f;
        readonly Stopwatch sw = new Stopwatch();

        public float CpuMs { get; private set; }
        public string BackendName => backend != null ? backend.Name : "cpu";
        public float BackendMs => backend != null ? backend.LastMs : CpuMs;
        public float[] Data => field;
        public float Cell => cell;

        public void SetBackend(IInfluenceBackend b) => backend = b;

        void Add(Vector2 p, float strength, float range, float team)
        {
            if (count == units.Length) System.Array.Resize(ref units, count * 2);
            units[count++] = new InfluenceUnit { x = p.x, z = p.y, strength = strength, range = range, team = team };
        }

        public void Build(Perception p, GameWorld w, int team)
        {
            cell = w.MapSize / N;
            count = 0;
            foreach (var e in w.units)
            {
                if (e == null || e.dying || e.team != team || !e.Complete) continue;
                var D = e.def;
                if (D.range <= 0f && D.type != UnitType.Worker) continue;
                Add(e.pos, Defs.InfluenceStrength(D.type), Mathf.Max(12f, D.range * 1.4f), 1f);
            }
            foreach (var r in p.Enemies)
            {
                var D = Defs.Get(r.type);
                if (D.building && r.type != UnitType.Sentinel) continue;
                // Stale sightings contribute less; turrets do not wander off.
                float conf = D.building ? 1f : Mathf.Exp(-(w.time - r.lastSeen) / 25f);
                if (conf < 0.05f) continue;
                Add(r.pos, Defs.InfluenceStrength(r.type) * conf, Mathf.Max(12f, D.range * 1.4f), -1f);
            }

            bool done = false;
            if (backend != null && count > 0) done = backend.Compute(units, count, N, cell, field);
            if (!done)
            {
                sw.Restart();
                ComputeCPU();
                CpuMs = (float)sw.Elapsed.TotalMilliseconds;
            }

            // Channel 3 is information age, which only the game can answer.
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    var c = new Vector2((x + 0.5f) * cell, (z + 0.5f) * cell);
                    float st = w.Staleness(team, c);
                    field[(z * N + x) * CHANNELS + 3] = st > 1e8f ? 1f : Saturate(st / 90f);
                }
        }

        public void ComputeCPU() => ComputeCPU(units, count, cell, field);

        public static void ComputeCPU(InfluenceUnit[] u, int n, float cell, float[] field)
        {
            System.Array.Clear(field, 0, field.Length);
            for (int i = 0; i < n; i++)
            {
                var a = u[i];
                float reach = a.range * 2f;
                int x0 = Mathf.Max(0, (int)((a.x - reach) / cell));
                int x1 = Mathf.Min(N - 1, (int)((a.x + reach) / cell));
                int z0 = Mathf.Max(0, (int)((a.z - reach) / cell));
                int z1 = Mathf.Min(N - 1, (int)((a.z + reach) / cell));
                int ch = a.team > 0f ? 0 : 1;
                float inv = 1f / (a.range * a.range);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x + 0.5f) * cell - a.x;
                        float dz = (z + 0.5f) * cell - a.z;
                        float d2 = dx * dx + dz * dz;
                        if (d2 > reach * reach) continue;
                        float f = a.strength / (1f + d2 * inv);
                        int idx = (z * N + x) * CHANNELS;
                        field[idx + ch] += f;
                        if (ch == 1) field[idx + 2] += f;   // enemy influence is threat
                    }
            }
        }

        public float Sample(int channel, Vector2 w)
        {
            int x = (int)Clamp(w.x / cell, 0, N - 1);
            int z = (int)Clamp(w.y / cell, 0, N - 1);
            return field[(z * N + x) * CHANNELS + channel];
        }

        /// <summary>Best position to fight from near a point: high friendly, low threat.</summary>
        public Vector2 SafestNear(Vector2 around, float radius)
        {
            Vector2 best = around;
            float bestScore = -1e30f;
            int r = (int)(radius / cell) + 1;
            int cx = (int)Clamp(around.x / cell, 0, N - 1);
            int cz = (int)Clamp(around.y / cell, 0, N - 1);
            for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(N - 1, cz + r); z++)
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(N - 1, cx + r); x++)
                {
                    int i = (z * N + x) * CHANNELS;
                    float score = field[i] - field[i + 2] * 1.6f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new Vector2((x + 0.5f) * cell, (z + 0.5f) * cell);
                    }
                }
            return best;
        }
    }
}
