// GroundScatter.cs — grass, pebbles and rocks over the terrain, drawn GPU-instanced.
//
// Grown at load from the terrain's own splat weights, so repainting the lichen
// layer with the terrain tools moves the grass with it: lush clumps on lichen,
// sparse dry tufts on gravel, nothing on cliffs, shores or under boulders;
// pebbles on gravel and ash, and rocks gathered at the feet of cliffs. The field
// is cut into 32 m chunks, and each chunk inside the view and draw distance is
// one instanced draw per mesh. Meshes are generated here, so nothing needs
// authoring; GroundMask decides where the grass is flattened or burned.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.View
{
    public sealed class GroundScatter : MonoBehaviour
    {
        public MapInfo map;
        public Material grassMaterial;
        public Material dryGrassMaterial;
        public Material pebbleMaterial;
        [Range(0.25f, 2f)] public float density = 1f;
        public float drawDistance = 185f;
        public uint seed = 7;

        const float ChunkSize = 32f;

        sealed class Chunk
        {
            public Bounds bounds;
            public readonly List<Matrix4x4> lush = new List<Matrix4x4>();
            public readonly List<Matrix4x4> dry = new List<Matrix4x4>();
            public readonly List<Matrix4x4> pebbles = new List<Matrix4x4>();
            public readonly List<Matrix4x4> rocks = new List<Matrix4x4>();
        }

        Chunk[] chunks;
        int side;
        Mesh grassMesh, pebbleMesh, rockMesh;
        readonly Plane[] planes = new Plane[6];

        public int InstanceCount { get; private set; }

        /// <summary>How many tufts of grass (lush and dry) stand inside a rectangle on the
        /// ground. For checking the grass fire against what is actually drawn.</summary>
        public int TuftsIn(Vector2 min, Vector2 max)
        {
            if (chunks == null) return 0;
            int n = 0;
            int x0 = ClampI((int)(min.x / ChunkSize), 0, side - 1), x1 = ClampI((int)(max.x / ChunkSize), 0, side - 1);
            int z0 = ClampI((int)(min.y / ChunkSize), 0, side - 1), z1 = ClampI((int)(max.y / ChunkSize), 0, side - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var chunk = chunks[z * side + x];
                    foreach (var list in new[] { chunk.lush, chunk.dry })
                        foreach (var m in list)
                            if (m.m03 >= min.x && m.m03 < max.x && m.m23 >= min.y && m.m23 < max.y) n++;
                }
            return n;
        }

        void Start()
        {
            if (map == null) map = MapInfo.Instance != null ? MapInfo.Instance : FindAnyObjectByType<MapInfo>();
            if (map == null || map.terrain == null) { enabled = false; return; }
            grassMesh = BuildGrassMesh();
            pebbleMesh = BuildRockMesh(0, 0.55f, 101);
            rockMesh = BuildRockMesh(1, 0.62f, 202);
            Grow();
            ground = GameWorld.Instance != null ? GameWorld.Instance.GroundShape : null;
            if (ground != null) ground.Deformed += Resettle;
        }

        GroundDeformer ground;

        /// <summary>A crater opened: everything growing or lying in it moves with the ground.</summary>
        void Resettle(Vector2 c, float radius)
        {
            if (chunks == null) return;
            int x0 = ClampI((int)((c.x - radius) / ChunkSize), 0, side - 1), x1 = ClampI((int)((c.x + radius) / ChunkSize), 0, side - 1);
            int z0 = ClampI((int)((c.y - radius) / ChunkSize), 0, side - 1), z1 = ClampI((int)((c.y + radius) / ChunkSize), 0, side - 1);
            float r2 = radius * radius;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var chunk = chunks[z * side + x];
                    foreach (var list in new[] { chunk.lush, chunk.dry, chunk.pebbles, chunk.rocks })
                        for (int i = 0; i < list.Count; i++)
                        {
                            var m = list[i];
                            float dx = m.m03 - c.x, dz = m.m23 - c.y;
                            if (dx * dx + dz * dz > r2) continue;
                            m.m13 += ground.LastChange(new Vector2(m.m03, m.m23));
                            list[i] = m;
                        }
                }
        }

        void OnDestroy()
        {
            if (ground != null) ground.Deformed -= Resettle;
            if (grassMesh != null) Destroy(grassMesh);
            if (pebbleMesh != null) Destroy(pebbleMesh);
            if (rockMesh != null) Destroy(rockMesh);
        }

        // ------------------------------------------------------------ placement
        void Grow()
        {
            var td = map.terrain.terrainData;
            float baseY = map.terrain.transform.position.y;
            float size = td.size.x;
            side = Mathf.CeilToInt(size / ChunkSize);
            chunks = new Chunk[side * side];
            for (int cz = 0; cz < side; cz++)
                for (int cx = 0; cx < side; cx++)
                    chunks[cz * side + cx] = new Chunk
                    {
                        bounds = new Bounds(new Vector3((cx + 0.5f) * ChunkSize, baseY + td.size.y * 0.5f, (cz + 0.5f) * ChunkSize),
                                            new Vector3(ChunkSize + 4f, td.size.y + 8f, ChunkSize + 4f))
                    };

            int aw = td.alphamapWidth, ah = td.alphamapHeight, layers = td.alphamapLayers;
            float[,,] alpha = td.GetAlphamaps(0, 0, aw, ah);
            float W(int layer, float x, float z)
            {
                if (layer >= layers) return 0f;
                int ax = Mathf.Clamp((int)(x / size * aw), 0, aw - 1);
                int az = Mathf.Clamp((int)(z / size * ah), 0, ah - 1);
                return alpha[az, ax, layer];
            }

            // Boulders placed on the map keep their ground clear.
            var blockers = new List<Vector3>();
            var boulders = map.transform.Find("Boulders");
            if (boulders != null)
                foreach (Transform b in boulders)
                    blockers.Add(new Vector3(b.position.x, 1.5f * b.lossyScale.x, b.position.z));
            bool Blocked(float x, float z)
            {
                foreach (var b in blockers)
                    if ((b.x - x) * (b.x - x) + (b.z - z) * (b.z - z) < b.y * b.y) return true;
                return false;
            }

            Chunk ChunkAt(float x, float z) =>
                chunks[ClampI((int)(z / ChunkSize), 0, side - 1) * side + ClampI((int)(x / ChunkSize), 0, side - 1)];

            var rng = new Rng(seed);
            float water = map.waterLevel;
            int count = 0;

            float cell = 1.15f / Mathf.Sqrt(density);
            for (float z = cell * 0.5f; z < size; z += cell)
                for (float x = cell * 0.5f; x < size; x += cell)
                {
                    float px = x + rng.Range(-0.45f, 0.45f) * cell, pz = z + rng.Range(-0.45f, 0.45f) * cell;
                    float roll = rng.F01();
                    if (px < 1f || pz < 1f || px > size - 1f || pz > size - 1f) continue;
                    float lichen = W(0, px, pz);
                    // The same rule the grass fire burns by (MapGenerator.GrassChance).
                    MapGenerator.GrassChance(new Vector4(lichen, W(1, px, pz), W(2, px, pz), W(3, px, pz)), px, pz,
                                             out float lushChance, out float dryChance);
                    if (roll >= lushChance + dryChance) continue;
                    float u = px / size, v = pz / size;
                    float h = td.GetInterpolatedHeight(u, v) + baseY;
                    if (h < water + 0.4f) continue;
                    Vector3 n = td.GetInterpolatedNormal(u, v);
                    if (n.y < MapGenerator.GrassMinUp || Blocked(px, pz)) continue;

                    bool lush = roll < lushChance;
                    float s = rng.Range(0.75f, 1.25f) * (lush ? Mathf.Lerp(0.7f, 1.05f, lichen) : rng.Range(0.6f, 0.85f));
                    var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, n, 0.6f)) *
                              Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                    var m = Matrix4x4.TRS(new Vector3(px, h - 0.04f, pz), rot, new Vector3(s, s * rng.Range(0.8f, 1.2f), s));
                    var c = ChunkAt(px, pz);
                    (lush ? c.lush : c.dry).Add(m);
                    count++;
                }

            cell = 2.0f / Mathf.Sqrt(density);
            for (float z = cell * 0.5f; z < size; z += cell)
                for (float x = cell * 0.5f; x < size; x += cell)
                {
                    float px = x + rng.Range(-0.5f, 0.5f) * cell, pz = z + rng.Range(-0.5f, 0.5f) * cell;
                    float roll = rng.F01(), bigRoll = rng.F01();
                    if (px < 1f || pz < 1f || px > size - 1f || pz > size - 1f) continue;
                    float lichen = W(0, px, pz), gravel = W(1, px, pz), cliff = W(2, px, pz), ash = W(3, px, pz);
                    float cliffFoot = Smoothstep(0.05f, 0.3f, cliff) * (1f - Smoothstep(0.5f, 0.85f, cliff));
                    float chance = gravel * 0.22f + ash * 0.18f + cliffFoot * 0.55f + lichen * 0.03f;
                    if (roll >= chance) continue;
                    float u = px / size, v = pz / size;
                    float h = td.GetInterpolatedHeight(u, v) + baseY;
                    if (h < water - 0.5f) continue;
                    Vector3 n = td.GetInterpolatedNormal(u, v);
                    if (n.y < 0.6f || Blocked(px, pz)) continue;

                    bool big = bigRoll < cliffFoot * 0.35f + 0.04f;
                    float s = big ? rng.Range(0.45f, 0.95f) : rng.Range(0.10f, 0.32f);
                    var rot = Quaternion.FromToRotation(Vector3.up, n) *
                              Quaternion.Euler(rng.Range(-20f, 20f), rng.Range(0f, 360f), rng.Range(-20f, 20f));
                    var scale = new Vector3(s * rng.Range(0.8f, 1.3f), s * rng.Range(0.55f, 0.95f), s * rng.Range(0.8f, 1.3f));
                    var m = Matrix4x4.TRS(new Vector3(px, h - s * 0.22f, pz), rot, scale);
                    var c = ChunkAt(px, pz);
                    (big ? c.rocks : c.pebbles).Add(m);
                    count++;
                }
            InstanceCount = count;
        }

        // ------------------------------------------------------------ drawing
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || chunks == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position;
            float maxSq = drawDistance * drawDistance;
            foreach (var c in chunks)
            {
                if (c.bounds.SqrDistance(cp) > maxSq || !GeometryUtility.TestPlanesAABB(planes, c.bounds)) continue;
                Draw(grassMesh, grassMaterial, c.lush, c.bounds, false);
                Draw(grassMesh, dryGrassMaterial, c.dry, c.bounds, false);
                Draw(pebbleMesh, pebbleMaterial, c.pebbles, c.bounds, false);
                Draw(rockMesh, pebbleMaterial, c.rocks, c.bounds, true);
            }
        }

        static void Draw(Mesh mesh, Material mat, List<Matrix4x4> list, Bounds bounds, bool shadows)
        {
            if (list.Count == 0 || mat == null) return;
            var rp = new RenderParams(mat)
            {
                worldBounds = bounds,
                shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true
            };
            Graphics.RenderMeshInstanced(rp, mesh, 0, list);
        }

        // ------------------------------------------------------------ meshes
        /// <summary>A clump of nine bent blades, each a five-vertex strip; uv.x is a
        /// per-blade wind phase and uv.y the height along the blade.</summary>
        static Mesh BuildGrassMesh()
        {
            var rnd = new System.Random(11);
            float R() => (float)rnd.NextDouble();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int b = 0; b < 9; b++)
            {
                float ang = R() * Mathf.PI * 2f, rad = Mathf.Sqrt(R()) * 0.24f;
                var root = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                float h = 0.38f + R() * 0.34f, w = 0.045f + R() * 0.03f;
                float face = R() * Mathf.PI * 2f;
                var sideV = new Vector3(Mathf.Cos(face), 0f, Mathf.Sin(face)) * w;
                var lean = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (0.08f + R() * 0.2f);
                float phase = R();
                int i0 = verts.Count;
                var mid = root + lean * 0.4f + Vector3.up * h * 0.55f;
                verts.Add(root - sideV); verts.Add(root + sideV);
                verts.Add(mid - sideV * 0.65f); verts.Add(mid + sideV * 0.65f);
                verts.Add(root + lean + Vector3.up * h);
                uvs.Add(new Vector2(phase, 0f)); uvs.Add(new Vector2(phase, 0f));
                uvs.Add(new Vector2(phase, 0.55f)); uvs.Add(new Vector2(phase, 0.55f));
                uvs.Add(new Vector2(phase, 1f));
                tris.AddRange(new[] { i0, i0 + 2, i0 + 1, i0 + 1, i0 + 2, i0 + 3, i0 + 2, i0 + 4, i0 + 3 });
            }
            var mesh = new Mesh { name = "SF_GrassClump" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A faceted rock: an icosphere with jittered radii, squashed and
        /// flat shaded. subdivisions 0 = 20 faces, 1 = 80.</summary>
        static Mesh BuildRockMesh(int subdivisions, float squash, int seed)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            var f = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var mids = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mids.TryGetValue(key, out int idx)) return idx;
                    v.Add((v[a] + v[b]) * 0.5f);
                    mids[key] = v.Count - 1;
                    return v.Count - 1;
                }
                var nf = new List<int>(f.Count * 4);
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }

            var rnd = new System.Random(seed);
            for (int i = 0; i < v.Count; i++)
            {
                var p = v[i].normalized * (0.78f + 0.34f * (float)rnd.NextDouble());
                p.y = Mathf.Max(p.y * squash, -0.3f);
                v[i] = p * 0.5f;
            }

            var verts = new List<Vector3>(f.Count);
            var tris = new List<int>(f.Count);
            for (int i = 0; i < f.Count; i += 3)
            {
                Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) (b, c) = (c, b);
                int k = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
            }
            var mesh = new Mesh { name = subdivisions == 0 ? "SF_Pebble" : "SF_Rock" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
