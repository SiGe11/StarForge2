// HeightfieldGenerator.cs — editor-time generator for the Unity Terrain asset.
//
// Runs once from the map builder; the result is baked into a TerrainData that
// is then an ordinary, hand-editable Unity Terrain. Nothing here runs in game.
//
// Faithful port of the original generator: fractal field, renormalised (value
// noise never reaches its own extremes, so skipping this gives a map with no
// low ground and no water), terraced into plateaus, rim mountains, flattened
// base plateaus, and a carved corridor whenever the bases are disconnected --
// so every generated map is playable.
using System;
using System.Collections.Generic;
using UnityEngine;
using static StarForge.SFMath;

namespace StarForge.EditorTools
{
    public sealed class HeightfieldGenerator
    {
        public const int N = 128;              // cells per side
        public const float CELL = 2f;          // world units per cell
        public const float SIZE = N * CELL;    // 256 world units
        public const float HSCALE = 26f;       // peak elevation
        public const float WATER = 2.4f;       // sea level
        public const int VN = N + 1;           // corner samples per side

        // A cell is walkable when no corner pair differs by more than this. At
        // CELL=2 this permits roughly a 39-degree incline: ramps pass, cliffs do not.
        const float MAX_STEP = 1.60f;

        readonly float[] h = new float[VN * VN];
        readonly byte[] pass = new byte[N * N];

        public float CornerHeight(int x, int z)
        {
            x = x < 0 ? 0 : (x > VN - 1 ? VN - 1 : x);
            z = z < 0 ? 0 : (z > VN - 1 ? VN - 1 : z);
            return h[z * VN + x];
        }

        public bool PassableCell(int cx, int cz)
        {
            if (cx < 0 || cz < 0 || cx >= N || cz >= N) return false;
            return pass[cz * N + cx] != 0;
        }

        public bool PassableWorld(float x, float z) =>
            PassableCell((int)Math.Floor(x / CELL), (int)Math.Floor(z / CELL));

        public static bool InBoundsWorld(float x, float z) => x >= 0f && z >= 0f && x < SIZE && z < SIZE;

        public float HeightAt(float x, float z)
        {
            float gx = Clamp(x / CELL, 0f, VN - 1 - 1e-4f);
            float gz = Clamp(z / CELL, 0f, VN - 1 - 1e-4f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            float h00 = CornerHeight(x0, z0), h10 = CornerHeight(x0 + 1, z0);
            float h01 = CornerHeight(x0, z0 + 1), h11 = CornerHeight(x0 + 1, z0 + 1);
            return Lerp(Lerp(h00, h10, fx), Lerp(h01, h11, fx), fz);
        }

        /// <summary>Catmull-Rom surface through the corner samples. Rendering only:
        /// it rounds cliff lips and ramps instead of showing the bilinear facets.</summary>
        public float SmoothHeightAt(float x, float z)
        {
            float gx = Clamp(x / CELL, 0f, VN - 1 - 1e-4f);
            float gz = Clamp(z / CELL, 0f, VN - 1 - 1e-4f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            float r0 = CR(CornerHeight(x0 - 1, z0 - 1), CornerHeight(x0, z0 - 1), CornerHeight(x0 + 1, z0 - 1), CornerHeight(x0 + 2, z0 - 1), fx);
            float r1 = CR(CornerHeight(x0 - 1, z0), CornerHeight(x0, z0), CornerHeight(x0 + 1, z0), CornerHeight(x0 + 2, z0), fx);
            float r2 = CR(CornerHeight(x0 - 1, z0 + 1), CornerHeight(x0, z0 + 1), CornerHeight(x0 + 1, z0 + 1), CornerHeight(x0 + 2, z0 + 1), fx);
            float r3 = CR(CornerHeight(x0 - 1, z0 + 2), CornerHeight(x0, z0 + 2), CornerHeight(x0 + 1, z0 + 2), CornerHeight(x0 + 2, z0 + 2), fx);
            float v = CR(r0, r1, r2, r3, fz);
            // Catmull-Rom overshoots at terrace lips; never dip below the lower
            // of the bilinear neighbours by more than a little.
            return Mathf.Max(v, HeightAt(x, z) - 0.6f);
        }

        static float CR(float p0, float p1, float p2, float p3, float t)
        {
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t +
                           (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
        }

        public Vector3 NormalAt(float x, float z)
        {
            const float e = CELL;
            float hl = HeightAt(x - e, z), hr = HeightAt(x + e, z);
            float hd = HeightAt(x, z - e), hu = HeightAt(x, z + e);
            return new Vector3(hl - hr, 2f * e, hd - hu).normalized;
        }

        void FlattenDisc(Vector2 c, float rInner, float rOuter, float targetH)
        {
            int x0 = Math.Max(0, (int)((c.x - rOuter) / CELL) - 1);
            int x1 = Math.Min(VN - 1, (int)((c.x + rOuter) / CELL) + 1);
            int z0 = Math.Max(0, (int)((c.y - rOuter) / CELL) - 1);
            int z1 = Math.Min(VN - 1, (int)((c.y + rOuter) / CELL) + 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = (new Vector2(x * CELL, z * CELL) - c).magnitude;
                    float w = 1f - Smoothstep(rInner, rOuter, d);
                    if (w <= 0f) continue;
                    h[z * VN + x] = Lerp(h[z * VN + x], targetH, w);
                }
        }

        void ComputePassability()
        {
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    float a = CornerHeight(x, z), b = CornerHeight(x + 1, z);
                    float c = CornerHeight(x, z + 1), d = CornerHeight(x + 1, z + 1);
                    float mn = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d));
                    float mx = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
                    bool ok = (mx - mn) <= MAX_STEP && mn > WATER + 0.15f;
                    pass[z * N + x] = ok ? (byte)1 : (byte)0;
                }
        }

        bool Connected(Vector2 a, Vector2 b)
        {
            int ax = (int)(a.x / CELL), az = (int)(a.y / CELL);
            int bx = (int)(b.x / CELL), bz = (int)(b.y / CELL);
            if (!PassableCell(ax, az) || !PassableCell(bx, bz)) return false;
            var seen = new bool[N * N];
            var stack = new Stack<int>();
            stack.Push(az * N + ax);
            seen[az * N + ax] = true;
            int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                int cx = cur % N, cz = cur / N;
                if (cx == bx && cz == bz) return true;
                for (int i = 0; i < 4; i++)
                {
                    int nx = cx + dx[i], nz = cz + dz[i];
                    if (!PassableCell(nx, nz)) continue;
                    int ni = nz * N + nx;
                    if (seen[ni]) continue;
                    seen[ni] = true;
                    stack.Push(ni);
                }
            }
            return false;
        }

        // Routes a least-resistance line between the bases (crossing cliffs at high
        // cost) then reshapes the height field along it into a walkable ramp.
        void CarveCorridor(Vector2 a, Vector2 b)
        {
            int ax = (int)Clamp(a.x / CELL, 0, N - 1), az = (int)Clamp(a.y / CELL, 0, N - 1);
            int bx = (int)Clamp(b.x / CELL, 0, N - 1), bz = (int)Clamp(b.y / CELL, 0, N - 1);

            var dist = new float[N * N];
            var prev = new int[N * N];
            for (int i = 0; i < dist.Length; i++) { dist[i] = 1e30f; prev[i] = -1; }
            var pq = new MinHeap();
            dist[az * N + ax] = 0f;
            pq.Push(0f, az * N + ax);
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

            while (pq.Count > 0)
            {
                pq.Pop(out float d, out int cur);
                if (d > dist[cur] + 1e-4f) continue;
                if (cur == bz * N + bx) break;
                int cx = cur % N, cz = cur / N;
                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + dx[i], nz = cz + dz[i];
                    if (nx < 0 || nz < 0 || nx >= N || nz >= N) continue;
                    float cost = (i < 4) ? 1f : 1.4142f;
                    if (pass[nz * N + nx] == 0)
                    {
                        // Water is far more expensive than rock, so corridors prefer land.
                        float hh = 0.25f * (CornerHeight(nx, nz) + CornerHeight(nx + 1, nz) +
                                            CornerHeight(nx, nz + 1) + CornerHeight(nx + 1, nz + 1));
                        cost += (hh <= WATER + 0.15f) ? 600f : 34f;
                    }
                    int ni = nz * N + nx;
                    if (dist[cur] + cost < dist[ni])
                    {
                        dist[ni] = dist[cur] + cost;
                        prev[ni] = cur;
                        pq.Push(dist[ni], ni);
                    }
                }
            }

            var path = new List<int>();
            for (int cur = bz * N + bx; cur != -1; cur = prev[cur])
            {
                path.Add(cur);
                if (cur == az * N + ax) break;
            }
            if (path.Count < 3) return;
            path.Reverse();

            int M = path.Count;
            var prof = new float[M];
            for (int i = 0; i < M; i++)
            {
                int cx = path[i] % N, cz = path[i] / N;
                prof[i] = HeightAt((cx + 0.5f) * CELL, (cz + 0.5f) * CELL);
            }
            var tmp = new float[M];
            for (int p = 0; p < 40; p++)
            {
                for (int i = 0; i < M; i++)
                {
                    float s = prof[i] * 2f, w = 2f;
                    if (i > 0) { s += prof[i - 1]; w += 1f; }
                    if (i < M - 1) { s += prof[i + 1]; w += 1f; }
                    tmp[i] = s / w;
                }
                tmp[0] = prof[0]; tmp[M - 1] = prof[M - 1];
                var sw = prof; prof = tmp; tmp = sw;
            }

            float R = 3.2f * CELL;
            for (int i = 0; i < M; i++)
            {
                int cx = path[i] % N, cz = path[i] / N;
                var c = new Vector2((cx + 0.5f) * CELL, (cz + 0.5f) * CELL);
                int gx0 = Math.Max(0, (int)((c.x - R) / CELL)), gx1 = Math.Min(VN - 1, (int)((c.x + R) / CELL) + 1);
                int gz0 = Math.Max(0, (int)((c.y - R) / CELL)), gz1 = Math.Min(VN - 1, (int)((c.y + R) / CELL) + 1);
                for (int z = gz0; z <= gz1; z++)
                    for (int x = gx0; x <= gx1; x++)
                    {
                        float d = (new Vector2(x * CELL, z * CELL) - c).magnitude;
                        float w = 1f - Smoothstep(R * 0.35f, R, d);
                        if (w <= 0f) continue;
                        h[z * VN + x] = Lerp(h[z * VN + x], prof[i], w * 0.85f);
                    }
            }
        }

        public void Generate(uint seed, Vector2 baseA, Vector2 baseB)
        {
            Array.Clear(h, 0, h.Length);
            var rng = new Rng(seed);
            float ox = rng.Range(-800f, 800f);
            float oz = rng.Range(-800f, 800f);
            const float L = 3f; // terrace levels

            var raw = new float[VN * VN];
            float rmn = 1e30f, rmx = -1e30f;
            for (int z = 0; z < VN; z++)
                for (int x = 0; x < VN; x++)
                {
                    float nx = x * CELL * 0.0120f + ox, nz = z * CELL * 0.0120f + oz;
                    float r = Noise.Fbm(nx, nz, 5) * 0.74f + Noise.Ridge(nx * 0.68f + 31.7f, nz * 0.68f - 17.3f, 4) * 0.26f;
                    raw[z * VN + x] = r;
                    rmn = Mathf.Min(rmn, r); rmx = Mathf.Max(rmx, r);
                }
            float inv = 1f / Mathf.Max(1e-6f, rmx - rmn);

            for (int z = 0; z < VN; z++)
                for (int x = 0; x < VN; x++)
                {
                    float r = Saturate((raw[z * VN + x] - rmn) * inv);
                    r = Mathf.Pow(r, 1.20f);

                    float hs = r * L;
                    float fl = Mathf.Floor(hs);
                    float k = Smoothstep(0.44f, 0.56f, hs - fl);
                    float terr = (fl + k) / L;
                    float hv = Lerp(terr, r, 0.13f);
                    hv = 0.02f + hv * 0.98f;

                    float ex = Mathf.Min(x, VN - 1 - x) / (float)VN;
                    float ez = Mathf.Min(z, VN - 1 - z) / (float)VN;
                    float rim = 1f - Smoothstep(0.025f, 0.15f, Mathf.Min(ex, ez));
                    float rimH = 1.00f + 0.44f * Noise.Ridge(x * 0.085f + 55.3f, z * 0.085f - 23.7f, 3);
                    hv = Lerp(hv, rimH, rim * rim);

                    h[z * VN + x] = hv * HSCALE;
                }

            float PlateauFor(Vector2 p)
            {
                float y = HeightAt(p.x, p.y);
                float q = Mathf.Round(y / HSCALE * L) / L * HSCALE;
                return Mathf.Max(q, WATER + 3f);
            }
            FlattenDisc(baseA, 17f, 33f, PlateauFor(baseA));
            FlattenDisc(baseB, 17f, 33f, PlateauFor(baseB));

            ComputePassability();
            for (int attempt = 0; attempt < 8 && !Connected(baseA, baseB); attempt++)
            {
                CarveCorridor(baseA, baseB);
                ComputePassability();
            }
        }
    }
}
