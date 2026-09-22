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

        public static GroundDeformer Instance { get; private set; }

        public void Init(MapInfo map)
        {
            Instance = this;
            terrain = map.terrain;
            waterLevel = map.waterLevel;
            mapSize = map.mapSize;
            var src = terrain.terrainData;
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
