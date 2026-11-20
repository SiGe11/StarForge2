// GroundScatter.cs — grass, pebbles and rocks over the terrain, drawn GPU-instanced.
//
// Grown at load from the terrain's own splat weights, so repainting the lichen
// layer with the terrain tools moves the grass with it: lush clumps on lichen,
// sparse dry tufts on gravel, nothing on cliffs, shores or under boulders;
// pebbles on gravel and ash, and rocks gathered at the feet of cliffs. The field
// is cut into 32 m chunks, and each chunk inside the view and draw distance is
// one instanced draw per mesh. Meshes are generated here, so nothing needs
// authoring; GroundMask decides where the grass is flattened or burned.
//
// The grass and the wildflowers are drawn from one GPU buffer filled when the
// cover is grown (SF_Grass, _SF_GRASS_BUFFER): a frame uploads nothing, where
// Unity's instancing copied every visible clump's matrix and its inverse each
// frame. Each chunk's clumps are in a shuffled order, and a chunk draws only
// the first share of them that its distance keeps (KeepAt, the curve SF_Grass
// sinks the rest by), so the far half of the view draws about half the blades,
// spread wider over the same ground. -sfgrassold on the player draws the old
// way, every clump through Unity's instancing, for an fps A/B; -sfnoflowers
// leaves the flowers out.
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
        [Tooltip("Draw the grass through Unity's instancing, every clump, as before the GPU buffer (the A/B).")]
        public bool legacyDraw;
        public bool flowers = true;

        const float ChunkSize = 32f;

        sealed class Chunk
        {
            public Bounds bounds;
            public readonly List<Matrix4x4> lush = new List<Matrix4x4>();
            public readonly List<Matrix4x4> dry = new List<Matrix4x4>();
            public readonly List<Matrix4x4> pebbles = new List<Matrix4x4>();
            public readonly List<Matrix4x4> rocks = new List<Matrix4x4>();
            public readonly List<Matrix4x4> flowers = new List<Matrix4x4>();
            public readonly List<float> flowerHue = new List<float>();
            // Where each list starts in the GPU buffer (list order is buffer order).
            public int lushStart, dryStart, flowerStart;
        }

        /// <summary>One clump as SF_Grass reads it: the rows of its object-to-world matrix,
        /// then x its place in its chunk's thinning order (0..1), y a flower's colour.</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Inst { public Vector4 r0, r1, r2, x; }
        const int InstStride = 64;

        Inst[] inst;
        GraphicsBuffer instBuffer;
        Material lushBuffered, dryBuffered;
        MaterialPropertyBlock block;
        static readonly int InstId = Shader.PropertyToID("_SF_GrassInst");
        static readonly int BaseId = Shader.PropertyToID("_SF_GrassBase");
        static readonly int TuneAId = Shader.PropertyToID("_SF_TuneA");
        static readonly int TerrainAOId = Shader.PropertyToID("_SF_TerrainAO");
        static readonly int TerrainAOParamsId = Shader.PropertyToID("_SF_TerrainAOParams");

        /// <summary>Flower clumps grown (not counted in <see cref="InstanceCount"/>).</summary>
        public int FlowerCount { get; private set; }
        /// <summary>Grass and flower clumps handed to the GPU last frame, after thinning.</summary>
        public int DrawnLastFrame { get; private set; }

        /// <summary>The share of a field's clumps left at a distance: SF_Grass's SFGrassKeep,
        /// which sinks the rest. The two must stay the same curve.</summary>
        public static float KeepAt(float dist, float tune = 0f) =>
            1f - Mathf.Clamp01(0.5f + tune) * Smoothstep(60f, 140f, dist);

        Chunk[] chunks;
        int side;
        Mesh grassMesh, flowerMesh, pebbleMesh, rockMesh;
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
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-sfgrassold") >= 0) legacyDraw = true;
            if (System.Array.IndexOf(args, "-sfnoflowers") >= 0) flowers = false;
            grassMesh = BuildGrassMesh();
            flowerMesh = BuildFlowerMesh();
            block = new MaterialPropertyBlock();
            if (grassMaterial != null) { lushBuffered = new Material(grassMaterial); lushBuffered.EnableKeyword("_SF_GRASS_BUFFER"); }
            if (dryGrassMaterial != null) { dryBuffered = new Material(dryGrassMaterial); dryBuffered.EnableKeyword("_SF_GRASS_BUFFER"); }
            pebbleMesh = BuildRockMesh(0, 0.55f, 101);
            rockMesh = BuildRockMesh(1, 0.62f, 202);
            Grow();
            ground = GameWorld.Instance != null ? GameWorld.Instance.GroundShape : null;
            if (ground != null) ground.Deformed += Resettle;
        }

        /// <summary>Grow the cover again at the current <see cref="density"/>: a preset picked
        /// on the title screen, after Start had grown it at the old one (QualityController).</summary>
        public void Regrow()
        {
            if (grassMesh != null && map != null) Grow();
        }

        GroundDeformer ground;

        /// <summary>A crater opened: everything growing or lying in it moves with the ground.</summary>
        void Resettle(Vector2 c, float radius)
        {
            if (chunks == null) return;
            int x0 = ClampI((int)((c.x - radius) / ChunkSize), 0, side - 1), x1 = ClampI((int)((c.x + radius) / ChunkSize), 0, side - 1);
            int z0 = ClampI((int)((c.y - radius) / ChunkSize), 0, side - 1), z1 = ClampI((int)((c.y + radius) / ChunkSize), 0, side - 1);
            float r2 = radius * radius;
            int lo = int.MaxValue, hi = -1;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var chunk = chunks[z * side + x];
                    Settle(chunk.lush, chunk.lushStart, c, r2, ref lo, ref hi);
                    Settle(chunk.dry, chunk.dryStart, c, r2, ref lo, ref hi);
                    Settle(chunk.flowers, chunk.flowerStart, c, r2, ref lo, ref hi);
                    Settle(chunk.pebbles, -1, c, r2, ref lo, ref hi);
                    Settle(chunk.rocks, -1, c, r2, ref lo, ref hi);
                }
            // The clumps that moved, in the GPU's copy too.
            if (instBuffer != null && hi >= lo) instBuffer.SetData(inst, lo, lo, hi - lo + 1);
        }

        void Settle(List<Matrix4x4> list, int start, Vector2 c, float r2, ref int lo, ref int hi)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                float dx = m.m03 - c.x, dz = m.m23 - c.y;
                if (dx * dx + dz * dz > r2) continue;
                m.m13 += ground.LastChange(new Vector2(m.m03, m.m23));
                list[i] = m;
                if (start < 0 || inst == null) continue;
                inst[start + i].r1.w = m.m13;
                if (start + i < lo) lo = start + i;
                if (start + i > hi) hi = start + i;
            }
        }

        void OnDestroy()
        {
            if (ground != null) ground.Deformed -= Resettle;
            instBuffer?.Release();
            instBuffer = null;
            if (lushBuffered != null) Destroy(lushBuffered);
            if (dryBuffered != null) Destroy(dryBuffered);
            if (grassMesh != null) Destroy(grassMesh);
            if (flowerMesh != null) Destroy(flowerMesh);
            if (pebbleMesh != null) Destroy(pebbleMesh);
            if (rockMesh != null) Destroy(rockMesh);
        }

        // ------------------------------------------------------------ placement
        void Grow()
        {
            var td = map.terrain.terrainData;
            float baseY = map.terrain.transform.position.y;
            float size = td.size.x;
            // The grass takes its colour drifts from the terrain's own variation channel
            // (SF_Meadow.hlsl), so the blades and the turf under them agree.
            var ground = map.terrain.materialTemplate;
            var ao = ground != null && ground.HasProperty("_AOTex") ? ground.GetTexture("_AOTex") : null;
            Shader.SetGlobalTexture(TerrainAOId, ao != null ? ao : Texture2D.grayTexture);
            Shader.SetGlobalVector(TerrainAOParamsId, new Vector4(1f / size, ao != null ? 1f : 0f, 0f, 0f));
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

            // Wildflowers: drifts of white, yellow or violet in the lush meadow, a drift
            // mostly of one colour. Their own stream, so the grass, the pebbles and the
            // rocks above stand exactly where they did before there were flowers.
            int flowerCount = 0;
            if (flowers)
            {
                var frng = new Rng(seed * 7919u + 13u);
                cell = 2.4f / Mathf.Sqrt(density);
                for (float z = cell * 0.5f; z < size; z += cell)
                    for (float x = cell * 0.5f; x < size; x += cell)
                    {
                        float px = x + frng.Range(-0.5f, 0.5f) * cell, pz = z + frng.Range(-0.5f, 0.5f) * cell;
                        float roll = frng.F01(), stray = frng.F01(), pick = frng.F01(), turn = frng.Range(0f, 360f), s = frng.Range(0.85f, 1.3f);
                        if (px < 1f || pz < 1f || px > size - 1f || pz > size - 1f) continue;
                        float drift = Noise.Fbm(px * 0.045f + 31.7f, pz * 0.045f - 12.3f, 3);
                        if (roll >= Smoothstep(0.52f, 0.68f, drift) * 0.85f) continue;
                        MapGenerator.GrassChance(new Vector4(W(0, px, pz), W(1, px, pz), W(2, px, pz), W(3, px, pz)), px, pz,
                                                 out float lushChance, out _);
                        if (lushChance < 0.7f) continue;
                        float u = px / size, v = pz / size;
                        float h = td.GetInterpolatedHeight(u, v) + baseY;
                        if (h < water + 0.6f) continue;
                        Vector3 n = td.GetInterpolatedNormal(u, v);
                        if (n.y < MapGenerator.GrassMinUp || Blocked(px, pz)) continue;
                        float kind = Noise.Value(px * 0.021f + 5.1f, pz * 0.021f + 8.7f);
                        float hue = kind < 0.42f ? 0.17f : kind < 0.68f ? 0.5f : 0.84f;
                        if (stray < 0.15f) hue = pick < 0.34f ? 0.17f : pick < 0.67f ? 0.5f : 0.84f;
                        var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, n, 0.6f)) * Quaternion.Euler(0f, turn, 0f);
                        var c = ChunkAt(px, pz);
                        c.flowers.Add(Matrix4x4.TRS(new Vector3(px, h - 0.03f, pz), rot, new Vector3(s, s, s)));
                        c.flowerHue.Add(hue);
                        flowerCount++;
                    }
            }
            FlowerCount = flowerCount;

            // Each chunk's clumps in a shuffled order: a chunk far off draws only the
            // first part of each list, and that part must be spread evenly over it.
            var order = new Rng(seed * 104729u + 5u);
            foreach (var c in chunks)
            {
                Shuffle(c.lush, null, order);
                Shuffle(c.dry, null, order);
                Shuffle(c.flowers, c.flowerHue, order);
            }
            Upload();
        }

        static void Shuffle(List<Matrix4x4> list, List<float> with, Rng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.IRange(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
                if (with != null) (with[i], with[j]) = (with[j], with[i]);
            }
        }

        /// <summary>The grass and the flowers into one GPU buffer, chunk after chunk.</summary>
        void Upload()
        {
            int n = 0;
            foreach (var c in chunks) n += c.lush.Count + c.dry.Count + c.flowers.Count;
            instBuffer?.Release();
            instBuffer = null;
            inst = new Inst[n];
            int at = 0;
            foreach (var c in chunks)
            {
                c.lushStart = at; Fill(c.lush, null, ref at);
                c.dryStart = at; Fill(c.dry, null, ref at);
                c.flowerStart = at; Fill(c.flowers, c.flowerHue, ref at);
            }
            if (n == 0) return;
            instBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, InstStride);
            instBuffer.SetData(inst);
            block.SetBuffer(InstId, instBuffer);
        }

        void Fill(List<Matrix4x4> list, List<float> hue, ref int at)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                inst[at++] = new Inst
                {
                    r0 = m.GetRow(0), r1 = m.GetRow(1), r2 = m.GetRow(2),
                    x = new Vector4((i + 0.5f) / list.Count, hue != null ? hue[i] : 0f, 0f, 0f)
                };
            }
        }

        // ------------------------------------------------------------ drawing
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || chunks == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position;
            float maxSq = drawDistance * drawDistance;
            bool buffered = !legacyDraw && instBuffer != null && lushBuffered != null && dryBuffered != null;
            float tune = Shader.GetGlobalVector(TuneAId).z;
            int drawn = 0;
            foreach (var c in chunks)
            {
                float sq = c.bounds.SqrDistance(cp);
                if (sq > maxSq || !GeometryUtility.TestPlanesAABB(planes, c.bounds)) continue;
                if (buffered)
                {
                    // The share its nearest point keeps, and the band SF_Grass sinks the rest over.
                    float near = Mathf.Sqrt(sq);
                    float keep = Mathf.Min(1f, KeepAt(near, tune) + 0.06f);
                    drawn += DrawBuffered(grassMesh, lushBuffered, c.lushStart, c.lush.Count, keep, c.bounds);
                    drawn += DrawBuffered(grassMesh, dryBuffered, c.dryStart, c.dry.Count, keep, c.bounds);
                    if (near < 70f) drawn += DrawBuffered(flowerMesh, lushBuffered, c.flowerStart, c.flowers.Count, keep, c.bounds);
                }
                else
                {
                    Draw(grassMesh, grassMaterial, c.lush, c.bounds, false);
                    Draw(grassMesh, dryGrassMaterial, c.dry, c.bounds, false);
                    drawn += c.lush.Count + c.dry.Count;
                }
                Draw(pebbleMesh, pebbleMaterial, c.pebbles, c.bounds, false);
                Draw(rockMesh, pebbleMaterial, c.rocks, c.bounds, true);
            }
            DrawnLastFrame = drawn;
        }

        int DrawBuffered(Mesh mesh, Material mat, int start, int count, float keep, Bounds bounds)
        {
            int n = Mathf.Min(count, Mathf.CeilToInt(count * keep));
            if (n <= 0) return 0;
            // The block is copied by the call, so one serves every draw.
            block.SetFloat(BaseId, start);
            var rp = new RenderParams(mat)
            {
                worldBounds = bounds,
                matProps = block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true
            };
            Graphics.RenderMeshPrimitives(rp, mesh, 0, n);
            return n;
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
        /// <summary>A clump of sixteen blades: eight bent ones, each a five-vertex strip,
        /// fanned open over the clump's ground, and eight shorter single-triangle ones
        /// between them. uv.x is a per-blade random value (shade, seed heads), uv.y the
        /// height along the blade, and uv2 the vertex's offset across its blade, which
        /// SF_Grass scales up with distance: the blades are narrow (5-8 cm) so a tuft
        /// reads as grass close to, and widen as they recede so none goes sub-pixel.
        /// (It was nine blades 9-15 cm wide standing close: a tuft read as an agave.)</summary>
        static Mesh BuildGrassMesh()
        {
            var rnd = new System.Random(11);
            float R() => (float)rnd.NextDouble();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var sides = new List<Vector2>();
            var tris = new List<int>();
            for (int b = 0; b < 16; b++)
            {
                bool spike = b >= 8;
                float ang = R() * Mathf.PI * 2f, rad = Mathf.Sqrt(R()) * 0.42f;
                var root = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                float h = spike ? 0.16f + R() * 0.26f : 0.34f + R() * 0.38f;
                float w = spike ? 0.028f + R() * 0.012f : 0.026f + R() * 0.016f;
                float face = R() * Mathf.PI * 2f;
                var sideV = new Vector3(Mathf.Cos(face), 0f, Mathf.Sin(face)) * w;
                var side2 = new Vector2(sideV.x, sideV.z);
                float leanAng = ang + (R() - 0.5f) * 1.6f;
                var lean = new Vector3(Mathf.Cos(leanAng), 0f, Mathf.Sin(leanAng)) * (spike ? 0.05f + R() * 0.14f : 0.08f + R() * 0.24f);
                float phase = R();
                int i0 = verts.Count;
                if (spike)
                {
                    verts.Add(root - sideV); verts.Add(root + sideV);
                    verts.Add(root + lean + Vector3.up * h);
                    uvs.Add(new Vector2(phase, 0f)); uvs.Add(new Vector2(phase, 0f));
                    uvs.Add(new Vector2(phase, h / 0.72f));
                    sides.Add(-side2); sides.Add(side2); sides.Add(Vector2.zero);
                    tris.AddRange(new[] { i0, i0 + 2, i0 + 1 });
                    continue;
                }
                var mid = root + lean * 0.4f + Vector3.up * h * 0.55f;
                verts.Add(root - sideV); verts.Add(root + sideV);
                verts.Add(mid - sideV * 0.7f); verts.Add(mid + sideV * 0.7f);
                verts.Add(root + lean + Vector3.up * h);
                uvs.Add(new Vector2(phase, 0f)); uvs.Add(new Vector2(phase, 0f));
                uvs.Add(new Vector2(phase, 0.55f)); uvs.Add(new Vector2(phase, 0.55f));
                uvs.Add(new Vector2(phase, 1f));
                sides.Add(-side2); sides.Add(side2); sides.Add(-side2 * 0.7f); sides.Add(side2 * 0.7f); sides.Add(Vector2.zero);
                tris.AddRange(new[] { i0, i0 + 2, i0 + 1, i0 + 1, i0 + 2, i0 + 3, i0 + 2, i0 + 4, i0 + 3 });
            }
            return Finish("SF_GrassClump", verts, uvs, sides, tris);
        }

        /// <summary>Six small flowers on thin stems over a foot of short leaves. A head is a
        /// small six-sided disc tilted a little off level, its rim uv.y 2 (petal) and its
        /// middle 3 (the eye): SF_Grass colours it by the clump's flower value. Opaque
        /// geometry like the blades, no cut-out.</summary>
        static Mesh BuildFlowerMesh()
        {
            var rnd = new System.Random(29);
            float R() => (float)rnd.NextDouble();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var sides = new List<Vector2>();
            var tris = new List<int>();
            for (int f = 0; f < 6; f++)
            {
                float ang = R() * Mathf.PI * 2f, rad = 0.05f + R() * 0.3f;
                var root = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                float h = 0.24f + R() * 0.22f, phase = R();
                var top = root + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (0.03f + R() * 0.08f) + Vector3.up * h;
                float face = R() * Mathf.PI * 2f;
                var sideV = new Vector3(Mathf.Cos(face), 0f, Mathf.Sin(face)) * 0.014f;
                var side2 = new Vector2(sideV.x, sideV.z);
                int i0 = verts.Count;
                verts.Add(root - sideV); verts.Add(root + sideV); verts.Add(top);
                uvs.Add(new Vector2(phase, 0f)); uvs.Add(new Vector2(phase, 0f)); uvs.Add(new Vector2(phase, 0.85f));
                sides.Add(-side2); sides.Add(side2); sides.Add(Vector2.zero);
                tris.AddRange(new[] { i0, i0 + 2, i0 + 1 });
                // The head, tilted up to ~25 degrees so heads do not all lie in one plane.
                var tilt = Quaternion.Euler((R() - 0.5f) * 50f, 0f, (R() - 0.5f) * 50f);
                float hr = 0.04f + R() * 0.022f;
                int c0 = verts.Count;
                verts.Add(top + Vector3.up * 0.012f);
                uvs.Add(new Vector2(phase, 3f));
                sides.Add(Vector2.zero);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f;
                    verts.Add(top + tilt * new Vector3(Mathf.Cos(a) * hr, 0f, Mathf.Sin(a) * hr));
                    uvs.Add(new Vector2(phase, 2f));
                    sides.Add(Vector2.zero);
                }
                for (int k = 0; k < 6; k++) tris.AddRange(new[] { c0, c0 + 1 + (k + 1) % 6, c0 + 1 + k });
            }
            for (int b = 0; b < 4; b++)
            {
                float ang = R() * Mathf.PI * 2f, rad = R() * 0.2f;
                var root = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                float h = 0.14f + R() * 0.14f, face = R() * Mathf.PI * 2f, phase = R() * 0.7f;
                var sideV = new Vector3(Mathf.Cos(face), 0f, Mathf.Sin(face)) * 0.035f;
                var side2 = new Vector2(sideV.x, sideV.z);
                int i0 = verts.Count;
                verts.Add(root - sideV); verts.Add(root + sideV);
                verts.Add(root + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.12f + Vector3.up * h);
                uvs.Add(new Vector2(phase, 0f)); uvs.Add(new Vector2(phase, 0f)); uvs.Add(new Vector2(phase, h / 0.6f));
                sides.Add(-side2); sides.Add(side2); sides.Add(Vector2.zero);
                tris.AddRange(new[] { i0, i0 + 2, i0 + 1 });
            }
            return Finish("SF_FlowerClump", verts, uvs, sides, tris);
        }

        static Mesh Finish(string name, List<Vector3> verts, List<Vector2> uvs, List<Vector2> sides, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sides);
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
