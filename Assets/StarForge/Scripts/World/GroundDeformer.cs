// GroundDeformer.cs — explosions dent the terrain.
//
// The match works on a copy of the TerrainData, made before anything reads a
// height, so craters never reach the asset in the project (the same reason
// GameWorld copies the baked NavMesh). A crater is a shallow, smooth bowl
// pressed into the ground where a blast lands. They do not stack: the ground
// only goes down to the deepest single bowl covering it, so a spot shelled all
// match is a wide dip, never a pit (deep pits turned their walls into cliff
// rock, and read as holes rather than as blasted earth). Heights change at once
// for rendering and for every height query; the terrain's level of detail and
// its collider catch up a few times a second.
//
// Units ride the NavMesh, which craters do not change, so UnitView lowers a
// unit's model by Drop(). Everything else that sits on the ground (grass and
// pebbles, plants, boulders) listens to Deformed and moves by LastChange();
// GroundMask marks the churned, blackened earth.
using System;
using UnityEngine;

namespace StarForge.World
{
    public sealed class GroundDeformer : MonoBehaviour
    {
        Terrain terrain;
        TerrainData data;
        int res;
        float step, heightScale, waterLevel, baseY, mapSize;
        float[,] original, current;
        // The heights before the last crater, over its rectangle, for LastChange.
        float[,] before;
        int bx0, bz0, bw, bh;
        bool lodDirty;
        float nextLod;

        /// <summary>After a crater opens: centre and radius of what changed.</summary>
        public event Action<Vector2, float> Deformed;
        /// <summary>While Deformed is raised for a ditch (Trench): its line and half width, so the ground mask
        /// lays dug soil along it rather than a crater's round of thrown earth.</summary>
        public bool Digging { get; private set; }
        public Vector2 DitchA, DitchB;
        public float DitchHalf;

        public static GroundDeformer Instance { get; private set; }

        public void Init(MapInfo map)
        {
            Instance = this;
            terrain = map.terrain;
            waterLevel = map.waterLevel;
            mapSize = map.mapSize;
            var src = terrain.terrainData;
            // Again on a map generated in place since (the map sweep): the copy before goes.
            if (data != null && data != src) Destroy(data);
            data = Instantiate(src);
            data.name = src.name + " (match)";
            terrain.terrainData = data;
            var col = terrain.GetComponent<TerrainCollider>();
            if (col != null) col.terrainData = data;
            res = data.heightmapResolution;
            step = data.size.x / (res - 1);
            heightScale = data.size.y;
            baseY = terrain.transform.position.y;
            original = data.GetHeights(0, 0, res, res);
            current = (float[,])original.Clone();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (data != null) Destroy(data);
        }

        void Update()
        {
            if (lodDirty && Time.unscaledTime >= nextLod)
            {
                nextLod = Time.unscaledTime + 0.4f;
                lodDirty = false;
                terrain.terrainData.SyncHeightmap();   // finishes the SetHeightsDelayLOD edits (LOD and collider)
            }
        }

        float Bilinear(float[,] h, Vector2 p)
        {
            float fx = Mathf.Clamp(p.x / step, 0f, res - 1.001f), fz = Mathf.Clamp(p.y / step, 0f, res - 1.001f);
            int x = (int)fx, z = (int)fz;
            float tx = fx - x, tz = fz - z;
            return Mathf.Lerp(Mathf.Lerp(h[z, x], h[z, x + 1], tx), Mathf.Lerp(h[z + 1, x], h[z + 1, x + 1], tx), tz);
        }

        /// <summary>How far the ground here now lies below where the map put it, metres.</summary>
        public float Drop(Vector2 p) => data == null ? 0f : (Bilinear(original, p) - Bilinear(current, p)) * heightScale;

        /// <summary>How much the last crater moved the ground here (negative is down), metres.</summary>
        public float LastChange(Vector2 p)
        {
            if (before == null) return 0f;
            float fx = p.x / step - bx0, fz = p.y / step - bz0;
            if (fx < 0f || fz < 0f || fx > bw - 1.001f || fz > bh - 1.001f) return 0f;
            int x = (int)fx, z = (int)fz;
            float tx = fx - x, tz = fz - z;
            float was = Mathf.Lerp(Mathf.Lerp(before[z, x], before[z, x + 1], tx), Mathf.Lerp(before[z + 1, x], before[z + 1, x + 1], tx), tz);
            float now = Bilinear(current, p);
            return (now - was) * heightScale;
        }

        /// <summary>Digs a ditch along a..b: a flat bottom <paramref name="flatHalf"/> each side of the line,
        /// <paramref name="depth"/> deep, rising over <paramref name="slope"/> metres each side on a cosine (the
        /// steepest part 1.57x the mean: 0.9 m over 2 m is 35 degrees, under the NavMesh's 40), round at the ends.
        /// It becomes the map's own ground (Drop reads nothing there): dug when a match begins, before the
        /// NavMesh is rebuilt over it. It digs the copy Init made: a map generated in place since must Init again.</summary>
        public bool Trench(Vector2 a, Vector2 b, float flatHalf, float slope, float depth, bool tell = true)
        {
            if (data == null) return false;
            float outer = flatHalf + slope;
            float minX = Mathf.Min(a.x, b.x) - outer, maxX = Mathf.Max(a.x, b.x) + outer;
            float minZ = Mathf.Min(a.y, b.y) - outer, maxZ = Mathf.Max(a.y, b.y) + outer;
            if (minX < 4f || minZ < 4f || maxX > mapSize - 4f || maxZ > mapSize - 4f) return false;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX / step)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt(maxX / step));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(minZ / step)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt(maxZ / step));
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            var patch = data.GetHeights(x0, z0, w, h);
            before = (float[,])patch.Clone();
            bx0 = x0; bz0 = z0; bw = w; bh = h;
            float dryFloor = (waterLevel + 0.05f - baseY) / heightScale;
            Vector2 ab = b - a;
            float ab2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
            bool kept = original != null && original.GetLength(0) == res;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2((x0 + x) * step, (z0 + z) * step);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab2);
                    float d = (p - (a + ab * t)).magnitude;
                    if (d >= outer) continue;
                    float k = d <= flatHalf ? 1f : 0.5f * (1f + Mathf.Cos(Mathf.PI * (d - flatHalf) / slope));
                    float o = patch[z, x];
                    float nh = o - k * depth / heightScale;
                    if (o >= dryFloor) nh = Mathf.Max(nh, Mathf.Min(o, dryFloor));
                    patch[z, x] = nh;
                    if (kept)
                    {
                        original[z0 + z, x0 + x] = nh;
                        current[z0 + z, x0 + x] = Mathf.Min(current[z0 + z, x0 + x], nh);
                    }
                }
            data.SetHeights(x0, z0, patch);
            if (tell)
            {
                Digging = true;
                DitchA = a; DitchB = b; DitchHalf = outer;
                try { Deformed?.Invoke((a + b) * 0.5f, ab.magnitude * 0.5f + outer); }
                finally { Digging = false; }
            }
            return true;
        }

        /// <summary>Presses a bowl of <paramref name="radius"/> metres, <paramref name="depth"/> deep at its
        /// centre, into the ground. Returns false if it fell off the map.</summary>
        public bool Crater(Vector3 at, float radius, float depth)
        {
            if (data == null || radius < 0.3f || depth < 0.02f) return false;
            // Keep clear of the rim, where the backdrop meets the terrain's edge.
            float outer = radius;
            if (at.x < outer + 4f || at.z < outer + 4f || at.x > mapSize - outer - 4f || at.z > mapSize - outer - 4f) return false;

            int x0 = Mathf.Max(0, Mathf.FloorToInt((at.x - outer) / step)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((at.x + outer) / step));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((at.z - outer) / step)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((at.z + outer) / step));
            bw = x1 - x0 + 1; bh = z1 - z0 + 1; bx0 = x0; bz0 = z0;
            before = new float[bh, bw];
            var patch = new float[bh, bw];
            float dryFloor = (waterLevel + 0.05f - baseY) / heightScale;
            for (int z = 0; z < bh; z++)
                for (int x = 0; x < bw; x++)
                {
                    int gx = x0 + x, gz = z0 + z;
                    float h = current[gz, gx];
                    before[z, x] = h;
                    float dx = gx * step - at.x, dz = gz * step - at.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz) / radius;
                    float bowl = d < 1f ? (1f - d * d) * (1f - d * d) : 0f;
                    float o = original[gz, gx];
                    // Measured from the original ground, and only ever lowering it:
                    // overlapping blasts widen a dip instead of digging it deeper.
                    float nh = Mathf.Min(h, o - bowl * depth / heightScale);
                    // Dry land does not sink below the lake's surface: a hole there
                    // would be a pit with no water in it.
                    if (o >= dryFloor) nh = Mathf.Max(nh, Mathf.Min(h, dryFloor));
                    patch[z, x] = nh;
                    current[gz, gx] = nh;
                }
            data.SetHeightsDelayLOD(x0, z0, patch);
            lodDirty = true;
            Deformed?.Invoke(new Vector2(at.x, at.z), outer);
            return true;
        }
    }
}
