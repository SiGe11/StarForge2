// SightMap.cs -- what hills, rocks, walls and trees hide (GameWorld.Tactical).
//
// Sight and shots run along lines many times a second, so they read grids kept here rather than
// the engine's terrain: the ground at 1 m corners, what stands on it (boulders and finished or
// unfinished structures, stamped as extra height), and how thick the foliage is in each 4 m cell.
// What stands on the ground and the foliage are redone every couple of seconds: nothing that
// changes them (a tree burning down, a Garrison going up) has to tell this about it.
//
// Craters and ditches lower the ground under them (Regrid, from GroundDeformer's Deformed): a man lying
// in one has its rim between him and a round. Low walls and ruins (the new combat's CoverWall) and the
// trunks of fallen trees are stamped as standing height for sight and tried exactly, as rectangles, for
// rounds: a 1.2 m wall stops a round at a crouching man and lets one over it.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarForge.World
{
    public sealed class SightMap
    {
        const int N = 256;
        const int LeafN = 64, LeafShift = 2;     // a leaf cell is 4 ground cells, the fog grid's cell
        const float RefreshEvery = 2f;
        // Metres of it a line of sight gets through: a tree a cell makes 16 m of wood opaque.
        const float TreeLeaf = 1f / 16f, BushLeaf = 1f / 24f, SnagLeaf = 1f / 48f;
        // A smoke cloud is foliage nobody can see through for long, as tall as a man on a roof.
        const float SmokeLeaf = 0.3f, SmokeTop = 5f;

        readonly float cell, inv;
        readonly float[] ground = new float[(N + 1) * (N + 1)];
        readonly float[] standing = new float[N * N];
        readonly float[] leaves = new float[LeafN * LeafN];
        readonly float[] leafTop = new float[LeafN * LeafN];
        readonly List<int>[] trunks = new List<int>[LeafN * LeafN];
        readonly List<int>[] plantsIn = new List<int>[LeafN * LeafN];
        float nextRefresh;

        /// <summary>Every structure on the map, as of the last Refresh.</summary>
        public readonly List<Unit> Structures = new List<Unit>(64);
        Plant[] plantsSeen;

        /// <summary>The kept twin house (GameWorld.Sanctuary): a line of fire stops where it enters its grounds.</summary>
        public Sanctuary sanct;

        public SightMap(MapInfo map)
        {
            cell = map.mapSize / N;
            inv = 1f / cell;
            for (int z = 0; z <= N; z++)
                for (int x = 0; x <= N; x++)
                    ground[z * (N + 1) + x] = map.HeightAt(new Vector2(x * cell, z * cell));
        }

        void IndexPlants(Vegetation v)
        {
            plantsSeen = v.plants;
            Array.Clear(trunks, 0, trunks.Length);
            Array.Clear(plantsIn, 0, plantsIn.Length);
            float leafCell = cell * (1 << LeafShift);
            for (int i = 0; i < v.plants.Length; i++)
            {
                var k = v.KindOf(i);
                var p = v.plants[i].pos;
                int cx = Mathf.Clamp((int)(p.x / leafCell), 0, LeafN - 1), cz = Mathf.Clamp((int)(p.z / leafCell), 0, LeafN - 1);
                (plantsIn[cz * LeafN + cx] ??= new List<int>(4)).Add(i);
                if (k.bush) continue;
                // A bullet stops on a trunk: in every cell its round touches, so a line through one
                // cell finds a trunk standing in the next.
                float r = v.TrunkRadius(i);
                int x0 = Mathf.Clamp((int)((p.x - r) / leafCell), 0, LeafN - 1), x1 = Mathf.Clamp((int)((p.x + r) / leafCell), 0, LeafN - 1);
                int z0 = Mathf.Clamp((int)((p.z - r) / leafCell), 0, LeafN - 1), z1 = Mathf.Clamp((int)((p.z + r) / leafCell), 0, LeafN - 1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        (trunks[z * LeafN + x] ??= new List<int>(4)).Add(i);
            }
        }

        /// <summary>Bring what stands on the ground and the foliage up to date, at most every couple of seconds.</summary>
        /// <summary>Burnt-out tanks: they stop a round and a look as a rock does (GameWorld.Hulks).</summary>
        List<Hulk> hulks;

        public void StampHulk(Hulk h) => Stamp(h.at, h.radius, h.height);

        List<CoverWall> walls;
        readonly List<CoverWall>[] wallsIn = new List<CoverWall>[LeafN * LeafN];
        int wallMark;
        /// <summary>Every run of cover as of the last Refresh: the match's walls and the fallen trunks.</summary>
        public readonly List<CoverWall> Runs = new List<CoverWall>(192);
        readonly List<CoverWall> logs = new List<CoverWall>(64), logPool = new List<CoverWall>(64);

        /// <summary>The match's walls (they are listed with the fallen trunks in every leaf cell they touch).</summary>
        public void SetWalls(List<CoverWall> list)
        {
            walls = list;
            Reindex();
        }

        void Reindex()
        {
            // Every 2 s with the logs: the lists are kept, not made again.
            for (int i = 0; i < wallsIn.Length; i++) wallsIn[i]?.Clear();
            Runs.Clear();
            if (walls != null) Runs.AddRange(walls);
            Runs.AddRange(logs);
            float leafCell = cell * (1 << LeafShift);
            foreach (var w in Runs)
            {
                float r = w.half + 0.1f;
                int x0 = Mathf.Clamp((int)((Mathf.Min(w.a.x, w.b.x) - r) / leafCell), 0, LeafN - 1), x1 = Mathf.Clamp((int)((Mathf.Max(w.a.x, w.b.x) + r) / leafCell), 0, LeafN - 1);
                int z0 = Mathf.Clamp((int)((Mathf.Min(w.a.y, w.b.y) - r) / leafCell), 0, LeafN - 1), z1 = Mathf.Clamp((int)((Mathf.Max(w.a.y, w.b.y) + r) / leafCell), 0, LeafN - 1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        (wallsIn[z * LeafN + x] ??= new List<CoverWall>(4)).Add(w);
            }
        }

        /// <summary>A tree lying where it fell: its trunk from the stump out to where it rises clear of a
        /// crouching man is a low run of cover, as thick as the trunk. Pressed into the ground by tracks, sunk or
        /// burnt away, it is not.</summary>
        void RefreshLogs(Vegetation v)
        {
            foreach (var l in logs) logPool.Add(l);
            logs.Clear();
            for (int i = 0; i < v.plants.Length && i < v.live.Length; i++)
            {
                var s = v.live[i];
                if (s.state != PlantState.Down || s.crush > 0.5f || s.sink > 0.2f) continue;
                var k = v.KindOf(i);
                if (k.bush) continue;
                float r = v.TrunkRadius(i);
                if (r < 0.16f || s.fallDir.sqrMagnitude < 0.5f) continue;
                float len = k.height * v.plants[i].scale;
                // It rests on its boughs at `rest` from upright: the trunk rises 1 m for every tan(rest) along.
                float along = Mathf.Min(len * Mathf.Sin(s.rest) * 0.9f, Mathf.Tan(Mathf.Clamp(s.rest, 0.2f, 1.55f)) * 0.75f);
                if (along < 1.2f) continue;
                var p = v.plants[i].pos;
                var foot = new Vector2(p.x, p.z);
                CoverWall w;
                if (logPool.Count > 0) { w = logPool[logPool.Count - 1]; logPool.RemoveAt(logPool.Count - 1); }
                else w = new CoverWall();
                w.a = foot + s.fallDir * (r + 0.2f);
                w.b = foot + s.fallDir * along;
                w.half = r;
                w.height = 0.55f + r;
                w.top = GroundAt(foot.x, foot.y) + w.height;
                w.smashed = false;
                w.piece = null;
                w.cluster = -1;
                logs.Add(w);
            }
        }

        /// <summary>The ground under a crater or a ditch, again from the terrain.</summary>
        public void Regrid(MapInfo map, Vector2 c, float r)
        {
            int x0 = Mathf.Max(0, (int)((c.x - r) * inv)), x1 = Mathf.Min(N, (int)((c.x + r) * inv) + 1);
            int z0 = Mathf.Max(0, (int)((c.y - r) * inv)), z1 = Mathf.Min(N, (int)((c.y + r) * inv) + 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    ground[z * (N + 1) + x] = map.HeightAt(new Vector2(x * cell, z * cell));
        }

        /// <summary>A wall into the standing heights: every 1 m cell its middle line crosses, at its top.</summary>
        void StampWall(CoverWall w)
        {
            Vector2 ab = w.b - w.a;
            float len = ab.magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(len * inv * 2f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = w.a + ab * (s / (float)steps);
                int ix = (int)(p.x * inv), iz = (int)(p.y * inv);
                if (ix < 0 || iz < 0 || ix >= N || iz >= N) continue;
                float h = w.top - GroundAt((ix + 0.5f) * cell, (iz + 0.5f) * cell);
                int i = iz * N + ix;
                if (h > standing[i]) standing[i] = h;
            }
        }

        public void Refresh(GameWorld w, bool force = false)
        {
            hulks = w.Hulks;
            if (!force && w.time < nextRefresh) return;
            nextRefresh = w.time + RefreshEvery;

            Array.Clear(leaves, 0, leaves.Length);
            Array.Clear(leafTop, 0, leafTop.Length);
            var v = w.Plants;
            if (v != null)
            {
                if (!ReferenceEquals(plantsSeen, v.plants)) IndexPlants(v);
                for (int c = 0; c < plantsIn.Length; c++)
                {
                    var list = plantsIn[c];
                    if (list == null) continue;
                    for (int j = 0; j < list.Count; j++)
                    {
                        int i = list[j];
                        if (v.live[i].state != PlantState.Standing) continue;
                        var k = v.KindOf(i);
                        float s = v.plants[i].scale;
                        leaves[c] += k.bush ? BushLeaf : k.HasCrown ? TreeLeaf : SnagLeaf;
                        leafTop[c] = Mathf.Max(leafTop[c], k.bush ? 2f * s : k.height * s);
                    }
                }
            }

            foreach (var c in w.SmokeClouds) AddSmoke(c.at, StarForge.Sim.TacticalRules.SmokeRadius);

            Array.Clear(standing, 0, standing.Length);
            Structures.Clear();
            foreach (var b in w.boulders)
                if (b != null && !b.smashed)
                    Stamp(b.Pos, b.Radius, b.Radius * 1.1f);
            foreach (var h in w.Hulks) StampHulk(h);
            if (v != null) RefreshLogs(v);
            Reindex();
            foreach (var cw in Runs)
                if (!cw.smashed) StampWall(cw);
            foreach (var u in w.units)
                if (u != null && !u.dying && u.def.building)
                {
                    Structures.Add(u);
                    Stamp(u.pos, u.def.radius * 0.9f, u.def.visualHeight * Mathf.Max(0.25f, u.buildProgress));
                }
        }

        /// <summary>A smoke cloud into the leaf cells: every line of sight, for a unit or for the fog, then
        /// goes through it. The cells forget it at the refresh after it ends.</summary>
        public void AddSmoke(Vector2 at, float radius)
        {
            float size = cell * (1 << LeafShift);
            int x0 = Mathf.Max(0, (int)((at.x - radius) / size)), x1 = Mathf.Min(LeafN - 1, (int)((at.x + radius) / size));
            int z0 = Mathf.Max(0, (int)((at.y - radius) / size)), z1 = Mathf.Min(LeafN - 1, (int)((at.y + radius) / size));
            float r2 = radius * radius;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f) * size - at.x, dz = (z + 0.5f) * size - at.y;
                    bool own = at.x >= x * size && at.x < (x + 1) * size && at.y >= z * size && at.y < (z + 1) * size;
                    if (!own && dx * dx + dz * dz > r2) continue;
                    int c = z * LeafN + x;
                    leaves[c] += SmokeLeaf;
                    leafTop[c] = Mathf.Max(leafTop[c], SmokeTop);
                }
        }

        void Stamp(Vector2 p, float radius, float height)
        {
            int x0 = Mathf.Max(0, (int)((p.x - radius) * inv)), x1 = Mathf.Min(N - 1, (int)((p.x + radius) * inv));
            int z0 = Mathf.Max(0, (int)((p.y - radius) * inv)), z1 = Mathf.Min(N - 1, (int)((p.y + radius) * inv));
            float r2 = radius * radius;
            for (int z = z0; z <= z1; z++)
            {
                float dz = (z + 0.5f) * cell - p.y;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f) * cell - p.x;
                    if (dx * dx + dz * dz > r2) continue;
                    int i = z * N + x;
                    if (height > standing[i]) standing[i] = height;
                }
            }
        }

        /// <summary>The ground here, between the 1 m corners.</summary>
        public float GroundAt(float x, float z)
        {
            float fx = x * inv, fz = z * inv;
            int ix = Mathf.Clamp((int)fx, 0, N - 1), iz = Mathf.Clamp((int)fz, 0, N - 1);
            fx -= ix; fz -= iz;
            int a = iz * (N + 1) + ix;
            float h0 = ground[a] + (ground[a + 1] - ground[a]) * fx;
            float h1 = ground[a + N + 1] + (ground[a + N + 2] - ground[a + N + 1]) * fx;
            return h0 + (h1 - h0) * fz;
        }

        /// <summary>How thick the foliage is where something stands, 0 in the open to 1 in deep wood.</summary>
        public float WoodsAt(Vector2 p)
        {
            int cx = (int)(p.x * inv) >> LeafShift, cz = (int)(p.y * inv) >> LeafShift;
            if (cx < 0 || cz < 0 || cx >= LeafN || cz >= LeafN) return 0f;
            return Mathf.Clamp01(leaves[cz * LeafN + cx] / (2f * TreeLeaf));
        }

        /// <summary>Can an eye at <paramref name="a"/> see <paramref name="b"/>? The ground, what stands
        /// on it and the foliage along the line, walked a metre at a time. A structure looks out of
        /// itself: <paramref name="ownRadius"/> of the line round the eye ignores what stands there.</summary>
        public bool Clear(Vector3 a, Vector3 b, float ownRadius = 0f)
        {
            float dx = b.x - a.x, dz = b.z - a.z, dy = b.y - a.y;
            float flat = Mathf.Sqrt(dx * dx + dz * dz);
            int steps = (int)(flat * inv);
            if (steps < 2) return true;
            float step = 1f / steps, leaf = 0f, own = flat > 1e-3f ? ownRadius / flat : 0f;
            for (int s = 1; s < steps; s++)
            {
                float t = s * step, x = a.x + dx * t, z = a.z + dz * t, y = a.y + dy * t;
                int ix = (int)(x * inv), iz = (int)(z * inv);
                if (ix < 0 || iz < 0 || ix >= N || iz >= N) return false;
                float g = GroundAt(x, z);
                if (y <= g + (t < own ? 0f : standing[iz * N + ix])) return false;
                int c = (iz >> LeafShift) * LeafN + (ix >> LeafShift);
                if (leaves[c] > 0f && y - g < leafTop[c])
                {
                    leaf += leaves[c] * cell;
                    if (leaf >= 1f) return false;
                }
            }
            return true;
        }

        /// <summary>Could a round get from <paramref name="a"/> to <paramref name="b"/> past the ground
        /// and the boulders? (Not trunks, which only stop what strikes them, nor foliage, nor units.)</summary>
        public bool FireClear(Vector3 a, Vector3 b, List<Boulder> boulders) => FirstStop(a, b, null, boulders) >= 1f;

        /// <summary>How far along the line a round gets, 0..1: 1 if the ground, a boulder or a trunk do
        /// not stop it first. The ground half a metre at a time, the rest exactly.</summary>
        public float FirstStop(Vector3 a, Vector3 b, Vegetation plants, List<Boulder> boulders) => FirstStop(a, b, plants, boulders, new Vector2(b.x, b.z));

        /// <summary>The same for a round fired on past its mark (DecideShot): <paramref name="inside"/> is where the
        /// man it is fired at stands. A wall he stands in lets it reach him and stops it where it comes out beyond.</summary>
        public float FirstStop(Vector3 a, Vector3 b, Vegetation plants, List<Boulder> boulders, Vector2 inside)
        {
            float dx = b.x - a.x, dz = b.z - a.z, dy = b.y - a.y;
            float flat2 = dx * dx + dz * dz;
            float flat = Mathf.Sqrt(flat2);
            if (flat < 1e-3f) return 1f;
            float stop = 1f;
            if (boulders != null)
                foreach (var r in boulders)
                {
                    if (r == null || r.smashed) continue;
                    Vector2 c = r.Pos;
                    float rad = r.Radius;
                    float t = RayCircle(a.x, a.z, dx, dz, flat2, c.x, c.y, rad);
                    if (t < stop && a.y + dy * t < GroundAt(c.x, c.y) + rad * 1.1f) stop = t;
                }
            if (hulks != null)
                foreach (var h in hulks)
                {
                    float t = RayCircle(a.x, a.z, dx, dz, flat2, h.at.x, h.at.y, h.radius);
                    if (t < stop && a.y + dy * t < GroundAt(h.at.x, h.at.y) + h.height) stop = t;
                }
            if (sanct != null)
            {
                // Stopped where it meets the grounds below the walls' top: a line to or from an aircraft passes over.
                float e = sanct.Enter(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                if (e < stop && a.y + dy * e < GroundAt(a.x + dx * e, a.z + dz * e) + Sanctuary.WallTop + 0.5f) stop = e;
            }
            int steps = Mathf.Max(2, Mathf.CeilToInt(flat * 2f * inv));
            float step = 1f / steps;
            int lastCell = -1;
            bool runs = Runs.Count > 0;
            if (runs) wallMark++;
            // The cell the line starts in has its walls too (the first step may be in the next).
            if (runs) WallsStop(a, dx, dz, dy, inside, (Mathf.Clamp((int)(a.z * inv), 0, N - 1) >> LeafShift) * LeafN + (Mathf.Clamp((int)(a.x * inv), 0, N - 1) >> LeafShift), ref stop);
            for (int s = 1; s <= steps; s++)
            {
                float t = s * step;
                if (t > stop) break;
                float x = a.x + dx * t, z = a.z + dz * t, y = a.y + dy * t;
                int ix = (int)(x * inv), iz = (int)(z * inv);
                if (ix < 0 || iz < 0 || ix >= N || iz >= N) { stop = Mathf.Min(stop, t); break; }
                if (y <= GroundAt(x, z)) { stop = Mathf.Min(stop, t); break; }
                int c = (iz >> LeafShift) * LeafN + (ix >> LeafShift);
                if (c == lastCell) continue;
                lastCell = c;
                if (runs) WallsStop(a, dx, dz, dy, inside, c, ref stop);
                if (plants == null) continue;
                var list = trunks[c];
                if (list == null) continue;
                for (int j = 0; j < list.Count; j++)
                {
                    int i = list[j];
                    if (plants.live[i].state != PlantState.Standing) continue;
                    var p = plants.plants[i].pos;
                    float hit = RayCircle(a.x, a.z, dx, dz, flat2, p.x, p.z, plants.TrunkRadius(i));
                    if (hit < stop && a.y + dy * hit < p.y + plants.KindOf(i).height * plants.plants[i].scale) stop = hit;
                }
            }
            return stop;
        }

        /// <summary>The walls listed in leaf cell <paramref name="c"/>, each tried once a line: where the line
        /// is inside one below its top, it stops where it went in. Not before <paramref name="inside"/> in a wall
        /// that holds it (the line's end, or the man a round is fired at): a man standing in one (on its band,
        /// vaulting or stopped there) is in nothing's shelter, and with it no round ever came at him while he
        /// fired out. Past him such a round stops where it comes out of the wall.</summary>
        void WallsStop(Vector3 a, float dx, float dz, float dy, Vector2 inside, int c, ref float stop)
        {
            var list = wallsIn[c];
            if (list == null) return;
            for (int j = 0; j < list.Count; j++)
            {
                var w = list[j];
                if (w.mark == wallMark || w.smashed) continue;
                w.mark = wallMark;
                if (!w.Ray(a.x, a.z, dx, dz, out float enter, out float leave)) continue;
                if (w.Distance(inside) <= w.half)
                {
                    if (leave < 1f && leave < stop && a.y + dy * leave < w.top) stop = leave;
                    continue;
                }
                if (enter >= stop) continue;
                if (Mathf.Min(a.y + dy * enter, a.y + dy * leave) < w.top) stop = enter;
            }
        }

        /// <summary>The low wall whose run is under <paramref name="p"/> (within its half thickness and
        /// <paramref name="pad"/>), or null: a soldier crossing it is vaulting it.</summary>
        public CoverWall WallUnder(Vector2 p, float pad)
        {
            int ix = (int)(p.x * inv), iz = (int)(p.y * inv);
            if (ix < 0 || iz < 0 || ix >= N || iz >= N) return null;
            var list = wallsIn[(iz >> LeafShift) * LeafN + (ix >> LeafShift)];
            if (list == null) return null;
            foreach (var w in list)
                if (!w.smashed && w.height <= MapGenerator.VaultHigh && w.Distance(p) < w.half + pad) return w;
            return null;
        }

        /// <summary>Trunks a man could shelter behind within reach, into <paramref name="into"/> as (x, z,
        /// radius), up to <paramref name="most"/> in all. Thin ones hide no one.</summary>
        public void TrunksNear(Vector2 at, float reach, Vegetation v, List<Vector3> into, int most)
        {
            float leafCell = cell * (1 << LeafShift);
            int x0 = Mathf.Max(0, (int)((at.x - reach) / leafCell)), x1 = Mathf.Min(LeafN - 1, (int)((at.x + reach) / leafCell));
            int z0 = Mathf.Max(0, (int)((at.y - reach) / leafCell)), z1 = Mathf.Min(LeafN - 1, (int)((at.y + reach) / leafCell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var list = trunks[z * LeafN + x];
                    if (list == null) continue;
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (into.Count >= most) return;
                        int i = list[j];
                        if (v.live[i].state != PlantState.Standing) continue;
                        float r = v.TrunkRadius(i);
                        if (r < 0.2f) continue;
                        var p = v.plants[i].pos;
                        if ((new Vector2(p.x, p.z) - at).sqrMagnitude <= reach * reach) into.Add(new Vector3(p.x, p.z, r));
                    }
                }
        }

        /// <summary>Where along a..a+d a line first enters a circle, 0..1, or 2 if it does not.</summary>
        public static float RayCircle(float ax, float az, float dx, float dz, float d2, float cx, float cz, float r)
        {
            float fx = ax - cx, fz = az - cz;
            float b = fx * dx + fz * dz;
            float c = fx * fx + fz * fz - r * r;
            if (c <= 0f) return 0f;
            if (b >= 0f) return 2f;
            float disc = b * b - d2 * c;
            if (disc < 0f) return 2f;
            float t = (-b - Mathf.Sqrt(disc)) / d2;
            return t <= 1f ? t : 2f;
        }

        // ------------------------------------------------------------ what a unit can see of the ground
        readonly int[] sweepMark = new int[GameWorld.VIS * GameWorld.VIS];
        int sweepId;

        /// <summary>The fog cells an eye sees, into <paramref name="cells"/>; how many. Rays out from it,
        /// each keeping the steepest rise so far: a cell shows if something a man's height on it would
        /// clear that rise, until the foliage along the ray closes it.</summary>
        /// <summary>Every leaf cell within this reach, nothing in the way (a Mech's sensors).</summary>
        public int SweepOpen(Vector2 from, float sight, ref ushort[] cells)
        {
            float leaf = cell * (1 << LeafShift);
            int r = Mathf.CeilToInt(sight / leaf) + 1;
            int most = (2 * r + 1) * (2 * r + 1);
            if (cells == null || cells.Length < most) cells = new ushort[most];
            int count = 0;
            int cx = (int)(from.x * inv) >> LeafShift, cz = (int)(from.y * inv) >> LeafShift;
            float reach = sight + leaf * 0.5f, reach2 = reach * reach;
            for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || z < 0 || x >= LeafN || z >= LeafN) continue;
                    float wx = (x + 0.5f) * leaf - from.x, wz = (z + 0.5f) * leaf - from.y;
                    if (wx * wx + wz * wz > reach2) continue;
                    cells[count++] = (ushort)(z * LeafN + x);
                }
            return count;
        }

        public int Sweep(Vector2 from, float eye, float sight, float viewOverGround, float ownRadius, ref ushort[] cells)
        {
            int r = (int)(sight * inv / (1 << LeafShift)) + 2;
            int most = (2 * r + 1) * (2 * r + 1);
            if (cells == null || cells.Length < most) cells = new ushort[most];
            int count = 0;
            sweepId++;
            float step = cell * 2f;
            int steps = Mathf.CeilToInt(sight / step);
            int rays = Mathf.Max(24, Mathf.CeilToInt(2f * Mathf.PI * sight / (cell * (1 << LeafShift) * 0.7f)));
            int leafCx = (int)(from.x * inv) >> LeafShift, leafCz = (int)(from.y * inv) >> LeafShift;
            // Where it stands and round about always shows.
            for (int z = leafCz - 1; z <= leafCz + 1; z++)
                for (int x = leafCx - 1; x <= leafCx + 1; x++)
                {
                    if (x < 0 || z < 0 || x >= LeafN || z >= LeafN) continue;
                    int c = z * LeafN + x;
                    if (sweepMark[c] == sweepId) continue;
                    sweepMark[c] = sweepId;
                    cells[count++] = (ushort)c;
                }
            for (int a = 0; a < rays; a++)
            {
                float ang = a * (2f * Mathf.PI / rays);
                float dx = Mathf.Cos(ang), dz = Mathf.Sin(ang);
                float rise = float.NegativeInfinity, leaf = 0f;
                for (int s = 1; s <= steps; s++)
                {
                    float d = s * step;
                    float x = from.x + dx * d, z = from.y + dz * d;
                    int ix = (int)(x * inv), iz = (int)(z * inv);
                    if (ix < 0 || iz < 0 || ix >= N || iz >= N) break;
                    float g = ground[iz * (N + 1) + ix];
                    float top = d < ownRadius ? g : g + standing[iz * N + ix];
                    int c = (iz >> LeafShift) * LeafN + (ix >> LeafShift);
                    if ((g + viewOverGround - eye) / d >= rise && sweepMark[c] != sweepId)
                    {
                        sweepMark[c] = sweepId;
                        cells[count++] = (ushort)c;
                    }
                    float slope = (top - eye) / d;
                    if (slope > rise) rise = slope;
                    if (leaves[c] > 0f && eye - g < leafTop[c])
                    {
                        leaf += leaves[c] * step;
                        if (leaf >= 1f) break;
                    }
                }
            }
            return count;
        }
    }
}
