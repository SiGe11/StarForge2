// MapGenerator.cs — builds a whole battlefield from a seed, into the scene's Map.
//
// Every match starts on a new map (MapRuntime calls this before anything else
// in the scene wakes up), and the editor's map builder runs the same code once
// for the map the scene is saved with. Everything here is generated, not
// authored: the heightfield and its terrain, the splat weights (moss in broad
// patches, bare gravel between them, rock on the steep faces and high ground,
// sand along the water), relief occlusion, the water surface and the deep
// water units cannot wade, the land beyond the rim, the start plateaus, ore
// fields and mirrored expansions, boulders, scenery, groves and bushes, and the
// NavMesh over all of it.
//
// The per-texel work (heights, splat, occlusion, the surround) runs on worker
// threads from the generator's own arrays; the Unity calls stay on the main
// thread. A new map takes a second or two on the MacBook Neo, most of it the
// NavMesh bake.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering;
using static StarForge.SFMath;

namespace StarForge.World
{
    public static class MapGenerator
    {
        public const uint DefaultSeed = 1000;
        public const float Size = HeightfieldGenerator.SIZE;
        public const float TerrainHeight = 40f;
        public const int HeightRes = 513, SplatRes = 512, AORes = 256;
        /// <summary>Deeper than this, water stops units; shallower, they wade in.</summary>
        public const float WadeDepth = 1.0f;
        /// <summary>NavMesh area under boulders and tree trunks (named "Rubble" in the project's areas).</summary>
        public const int RubbleArea = 3;

        public static readonly Vector2 BaseA = new Vector2(0.22f * Size, 0.26f * Size);
        public static readonly Vector2 BaseB = new Vector2(0.78f * Size, 0.74f * Size);

        /// <summary>How prefabs are placed; the editor swaps in PrefabUtility so the saved
        /// scene keeps its prefab links.</summary>
        public static Func<GameObject, Transform, GameObject> Instantiate = (prefab, parent) => UnityEngine.Object.Instantiate(prefab, parent);

        public sealed class Result
        {
            public uint seed;
            public HeightfieldGenerator gen;
            public TerrainData terrainData;
            public float[,,] splat;
            public Texture2D ao;
            public Material terrainMaterial;
            public Mesh water, backdrop;
            public int plants, blockingPlants, ore, boulders, scenery;
            public long heightsMs, splatMs, objectsMs, navMs;
        }

        /// <summary>Builds the map from <paramref name="seed"/> into <paramref name="info"/>'s
        /// hierarchy, replacing what was there. <paramref name="sharedMaterials"/> writes the
        /// occlusion texture into the kit's terrain material itself (the editor, which saves
        /// it) rather than into a copy (a match).</summary>
        public static Result Generate(MapInfo info, MapKit kit, uint seed, bool bakeNavMesh, bool sharedMaterials)
        {
            var r = new Result { seed = seed };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var gen = r.gen = new HeightfieldGenerator();
            gen.Generate(seed, BaseA, BaseB);

            info.mapSize = Size;
            info.waterLevel = HeightfieldGenerator.WATER;
            info.seed = seed;

            var heights = BuildHeights(gen);
            r.terrainData = BuildTerrainData(heights, kit);
            r.heightsMs = clock.ElapsedMilliseconds;

            r.splat = BuildSplat(heights);
            r.terrainData.SetAlphamaps(0, 0, r.splat);
            r.ao = BuildAOTexture(gen);
            r.terrainMaterial = sharedMaterials ? kit.terrainMaterial : new Material(kit.terrainMaterial) { name = kit.terrainMaterial.name + " (match)" };
            r.terrainMaterial.SetTexture("_AOTex", r.ao);
            SetupTerrain(info, r.terrainData, r.terrainMaterial);
            r.splatMs = clock.ElapsedMilliseconds - r.heightsMs;

            long t0 = clock.ElapsedMilliseconds;
            r.water = BuildWater(info, heights, kit);
            BuildDeepWater(info, heights);
            r.backdrop = BuildBackdrop(info, gen, heights, kit);
            PlaceMapObjects(info, kit, gen, seed, r);
            r.objectsMs = clock.ElapsedMilliseconds - t0;

            if (bakeNavMesh)
            {
                t0 = clock.ElapsedMilliseconds;
                var surface = info.GetComponent<NavMeshSurface>();
                if (surface != null)
                {
                    surface.RemoveData();
                    surface.navMeshData = null;
                    surface.BuildNavMesh();
                }
                r.navMs = clock.ElapsedMilliseconds - t0;
            }
            return r;
        }

        // ------------------------------------------------------------ terrain
        /// <summary>Heights in metres, [z, x], one sample per heightmap texel: the
        /// Catmull-Rom surface through the generator's corners, plus a little fine
        /// noise away from the base plateaus so the terraces stop looking machined.</summary>
        static float[,] BuildHeights(HeightfieldGenerator gen)
        {
            var h = new float[HeightRes, HeightRes];
            float step = Size / (HeightRes - 1);
            Parallel.For(0, HeightRes, z =>
            {
                for (int x = 0; x < HeightRes; x++)
                {
                    float wx = x * step, wz = z * step;
                    float v = gen.SmoothHeightAt(wx, wz);
                    float dBase = Mathf.Min((new Vector2(wx, wz) - BaseA).magnitude, (new Vector2(wx, wz) - BaseB).magnitude);
                    float rough = Smoothstep(20f, 34f, dBase);
                    v += (Noise.Fbm(wx * 0.21f + 13.1f, wz * 0.21f - 7.7f, 3) - 0.5f) * 0.45f * rough;
                    h[z, x] = Mathf.Clamp(v, 0f, TerrainHeight);
                }
            });
            return h;
        }

        /// <summary>Bilinear height in metres from the heights array.</summary>
        static float Sample(float[,] h, float wx, float wz)
        {
            float step = Size / (HeightRes - 1);
            float fx = Mathf.Clamp(wx / step, 0f, HeightRes - 1.001f), fz = Mathf.Clamp(wz / step, 0f, HeightRes - 1.001f);
            int x = (int)fx, z = (int)fz;
            float tx = fx - x, tz = fz - z;
            return Mathf.Lerp(Mathf.Lerp(h[z, x], h[z, x + 1], tx), Mathf.Lerp(h[z + 1, x], h[z + 1, x + 1], tx), tz);
        }

        static Vector3 SampleNormal(float[,] h, float wx, float wz)
        {
            const float e = 0.5f;
            float hl = Sample(h, wx - e, wz), hr = Sample(h, wx + e, wz);
            float hd = Sample(h, wx, wz - e), hu = Sample(h, wx, wz + e);
            return new Vector3(hl - hr, 2f * e, hd - hu).normalized;
        }

        static TerrainData BuildTerrainData(float[,] heights, MapKit kit)
        {
            var td = new TerrainData { heightmapResolution = HeightRes };
            td.size = new Vector3(Size, TerrainHeight, Size);
            var norm = new float[HeightRes, HeightRes];
            for (int z = 0; z < HeightRes; z++)
                for (int x = 0; x < HeightRes; x++)
                    norm[z, x] = heights[z, x] / TerrainHeight;
            td.SetHeights(0, 0, norm);
            td.alphamapResolution = SplatRes;
            td.baseMapResolution = 512;
            td.terrainLayers = kit.terrainLayers;
            return td;
        }

        static void SetupTerrain(MapInfo info, TerrainData td, Material mat)
        {
            Terrain t = info.terrain;
            if (t == null)
            {
                var go = Terrain.CreateTerrainGameObject(td);
                go.name = "Terrain";
                go.transform.SetParent(info.transform, false);
                t = info.terrain = go.GetComponent<Terrain>();
            }
            t.terrainData = td;
            var col = t.GetComponent<TerrainCollider>();
            if (col != null) col.terrainData = td;
            t.materialTemplate = mat;
            t.drawInstanced = false;
            t.heightmapPixelError = 4f;
            t.basemapDistance = 4000f;
            t.drawTreesAndFoliage = false;
            t.shadowCastingMode = ShadowCastingMode.On;
            t.allowAutoConnect = false;
        }

        static float[,,] BuildSplat(float[,] heights)
        {
            var splat = new float[SplatRes, SplatRes, 4];
            float step = Size / SplatRes;
            Parallel.For(0, SplatRes, z =>
            {
                for (int x = 0; x < SplatRes; x++)
                {
                    float wx = (x + 0.5f) * step, wz = (z + 0.5f) * step;
                    Vector4 w = SplatAt(wx, wz, Sample(heights, wx, wz), SampleNormal(heights, wx, wz));
                    for (int k = 0; k < 4; k++) splat[z, x, k] = w[k];
                }
            });
            return splat;
        }

        /// <summary>Splat weights (lichen, gravel, cliff, ash) at a point. The terrain's
        /// splatmap and the backdrop beyond the rim both use it, so the ground keeps
        /// its colours across the edge of the map.</summary>
        public static Vector4 SplatAt(float wx, float wz, float h, Vector3 n)
        {
            // Rock wherever the terraces turn steep, and on high ground: the
            // mountains beyond the rim are bare stone rather than gravel.
            float cliffW = Mathf.Max(Smoothstep(0.90f, 0.66f, n.y), Smoothstep(32f, 52f, h) * 0.7f);
            // Sand along the waterline; a gentle beach carries it further up.
            float shoreTop = Mathf.Lerp(HeightfieldGenerator.WATER + 1.8f, HeightfieldGenerator.WATER + 3.2f, Smoothstep(0.9f, 0.97f, n.y));
            float shore = 1f - Smoothstep(HeightfieldGenerator.WATER + 0.4f, shoreTop, h);
            // Moss grows in broad patches with ragged edges. GroundScatter grows
            // its grass on this layer, so patches become meadows and the ground
            // between them stays bare gravel.
            float patch = Noise.Fbm(wx * 0.024f + 5.3f, wz * 0.024f + 1.9f, 4)
                        + (Noise.Fbm(wx * 0.11f - 3.1f, wz * 0.11f + 8.2f, 3) - 0.5f) * 0.22f;
            float lichen = Smoothstep(0.41f, 0.54f, patch);
            float dBase = Mathf.Min((new Vector2(wx, wz) - BaseA).magnitude, (new Vector2(wx, wz) - BaseB).magnitude);
            lichen *= Smoothstep(14f, 30f, dBase);                  // trampled base pads
            lichen *= 1f - Smoothstep(24f, 34f, h);                  // bare rim peaks
            // Scree: slopes just short of cliff gather loose gravel.
            float scree = Smoothstep(0.985f, 0.90f, n.y);
            lichen *= 1f - scree * 0.85f;
            float dry = Noise.Fbm(wx * 0.017f - 41f, wz * 0.017f + 17f, 3);
            float ash = shore + Smoothstep(0.58f, 0.70f, dry) * 0.55f;
            float rest = 1f - cliffW;
            float wl = lichen * (1f - Saturate(ash)) * rest;
            float wa = Saturate(ash) * rest;
            float wg = Mathf.Max(0f, rest - wl - wa);
            float sum = wl + wg + cliffW + wa;
            return new Vector4(wl / sum, wg / sum, cliffW / sum, wa / sum);
        }

        // Relief occlusion, as the original baked into its terrain vertices: pits
        // and cliff bases darken, exposed ridges stay bright.
        static Texture2D BuildAOTexture(HeightfieldGenerator gen)
        {
            var px = new Color32[AORes * AORes];
            float cell = Size / AORes;
            Parallel.For(0, AORes, z =>
            {
                for (int x = 0; x < AORes; x++)
                {
                    float wx = (x + 0.5f) * cell, wz = (z + 0.5f) * cell;
                    px[z * AORes + x] = new Color32((byte)(ReliefAO(gen, wx, wz) * 255), (byte)(Variation(wx, wz) * 255), 0, 255);
                }
            });
            var tex = new Texture2D(AORes, AORes, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Battlefield_TerrainAO"
            };
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        static readonly int[] Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dz8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

        static float ReliefAO(HeightfieldGenerator gen, float wx, float wz)
        {
            float hc = gen.HeightAt(wx, wz);
            float occ = 0f; int cnt = 0;
            for (int r = 1; r <= 4; r++)
                for (int i = 0; i < 8; i++)
                {
                    float hh = gen.HeightAt(wx + Dx8[i] * r * 2f, wz + Dz8[i] * r * 2f);
                    occ += Saturate((hh - hc) / (r * 2f * 1.15f));
                    cnt++;
                }
            return 1f - Saturate(occ / cnt * 1.75f);
        }

        /// <summary>Broad albedo mottling (the AO texture's G), continued past the rim.</summary>
        static float Variation(float wx, float wz) => Saturate(Noise.Fbm(wx * 0.055f + 91f, wz * 0.055f - 44f, 4));

        // ------------------------------------------------------------ water
        static Mesh BuildWater(MapInfo info, float[,] heights, MapKit kit)
        {
            // The surface covers every 1 m cell where the terrain itself dips below
            // the water line (and a little way up the shore, for the foam line and the
            // fade at the waterline), one quad per run of cells along a row.
            var verts = new List<Vector3>();
            var tris = new List<int>();
            const float W = HeightfieldGenerator.WATER;
            int R = (int)Size;
            var low = new float[(R + 1) * (R + 1)];
            for (int z = 0; z <= R; z++)
                for (int x = 0; x <= R; x++)
                    low[z * (R + 1) + x] = Sample(heights, x, z);
            bool Wet(int x, int z) =>
                Mathf.Min(Mathf.Min(low[z * (R + 1) + x], low[z * (R + 1) + x + 1]),
                          Mathf.Min(low[(z + 1) * (R + 1) + x], low[(z + 1) * (R + 1) + x + 1])) < W + 0.35f;
            for (int z = 0; z < R; z++)
                for (int x = 0; x < R;)
                {
                    if (!Wet(x, z)) { x++; continue; }
                    int x0 = x;
                    while (x < R && Wet(x, z)) x++;
                    int b = verts.Count;
                    verts.Add(new Vector3(x0, W, z));
                    verts.Add(new Vector3(x, W, z));
                    verts.Add(new Vector3(x, W, z + 1));
                    verts.Add(new Vector3(x0, W, z + 1));
                    tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }

            var mesh = new Mesh { name = "Water", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (info.water == null)
            {
                var go = new GameObject("Water");
                go.transform.SetParent(info.transform, false);
                info.water = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                // The surface is not ground: the bake sees the lake bed under it, so
                // units can wade into the shallows.
                go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            }
            info.water.sharedMesh = mesh;
            info.water.GetComponent<MeshRenderer>().sharedMaterial = kit.waterMaterial;
            info.water.gameObject.SetActive(verts.Count > 0);
            return mesh;
        }

        /// <summary>Where the water is deeper than WadeDepth, the lake bed is marked
        /// Not Walkable with box volumes: runs of 1 m cells merged row by row, then
        /// stacked while the next row has the same run.</summary>
        static void BuildDeepWater(MapInfo info, float[,] heights)
        {
            const int R = (int)Size;
            float W = HeightfieldGenerator.WATER;
            var deep = new bool[R * R];
            Parallel.For(0, R, z =>
            {
                for (int x = 0; x < R; x++)
                {
                    // Deep only if every sample in the cell is deep, so the edge of
                    // the walkable shallows follows the shallow side.
                    float hi = float.MinValue;
                    for (int k = 0; k < 5; k++)
                    {
                        float sx = x + (k == 4 ? 0.5f : (k & 1)), sz = z + (k == 4 ? 0.5f : (k >> 1));
                        hi = Mathf.Max(hi, Sample(heights, sx, sz));
                    }
                    deep[z * R + x] = hi < W - WadeDepth;
                }
            });

            if (info.deepWater == null)
            {
                info.deepWater = new GameObject("DeepWater");
                info.deepWater.transform.SetParent(info.transform, false);
            }
            foreach (var old in info.deepWater.GetComponents<NavMeshModifierVolume>()) Remove(old);
            var go = info.deepWater;
            var open = new Dictionary<(int x0, int x1), (int z0, NavMeshModifierVolume v)>();
            var seen = new HashSet<(int, int)>();
            for (int z = 0; z <= R; z++)
            {
                seen.Clear();
                for (int x = 0; z < R && x < R;)
                {
                    if (!deep[z * R + x]) { x++; continue; }
                    int x0 = x;
                    while (x < R && deep[z * R + x]) x++;
                    seen.Add((x0, x));
                }
                var closing = new List<(int, int)>();
                foreach (var kv in open) if (!seen.Contains(kv.Key)) closing.Add(kv.Key);
                foreach (var key in closing)
                {
                    var (z0, v) = open[key];
                    v.center = new Vector3((key.Item1 + key.Item2) * 0.5f, W - 3f, (z0 + z) * 0.5f);
                    v.size = new Vector3(key.Item2 - key.Item1, 6.4f, z - z0);
                    open.Remove(key);
                }
                foreach (var key in seen)
                {
                    if (open.ContainsKey(key)) continue;
                    var v = go.AddComponent<NavMeshModifierVolume>();
                    v.area = 1;   // Not Walkable
                    open[key] = (z, v);
                }
            }
        }

        // ------------------------------------------------------------ backdrop
        // The land beyond the rim, so a camera at the map edge sees mountains
        // fading into haze instead of the end of the world. A ring of rows round
        // the map, one vertex per metre along the edge: the first row tucks under
        // the terrain's edge (so its level-of-detail cracks never open onto sky),
        // the next starts exactly at the terrain's edge height, and further out the
        // ground keeps climbing into ridges. It is textured by the terrain's own
        // splat rules and relief shading, so nothing changes colour at the border.
        static readonly float[] BackdropRows =
            { -2f, 0f, 1f, 2.5f, 4.5f, 7f, 10f, 14f, 19f, 25f, 33f, 43f, 56f, 72f, 92f, 118f, 150f, 190f, 240f, 300f };

        static Mesh BuildBackdrop(MapInfo info, HeightfieldGenerator gen, float[,] heights, MapKit kit)
        {
            const int Side = 256, Corner = 16;
            int perRow = 4 * (Side + Corner), rows = BackdropRows.Length;

            // Samples round the square: each side from its first corner, then a
            // fan of outward normals turning round the next corner.
            Vector2[] start = { new Vector2(0f, 0f), new Vector2(Size, 0f), new Vector2(Size, Size), new Vector2(0f, Size) };
            Vector2[] along = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            Vector2[] outward = { Vector2.down, Vector2.right, Vector2.up, Vector2.left };
            var edgePoint = new Vector2[perRow];
            var normal = new Vector2[perRow];
            int k = 0;
            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i < Side; i++, k++)
                {
                    edgePoint[k] = start[side] + along[side] * (i * Size / Side);
                    normal[k] = outward[side];
                }
                for (int j = 0; j < Corner; j++, k++)
                {
                    float a = (j + 0.5f) / Corner * Mathf.PI * 0.5f;
                    edgePoint[k] = start[(side + 1) % 4];
                    normal[k] = (outward[side] * Mathf.Cos(a) + outward[(side + 1) % 4] * Mathf.Sin(a)).normalized;
                }
            }

            float EdgeHeight(Vector2 p) => Sample(heights, Mathf.Clamp(p.x, 0f, Size), Mathf.Clamp(p.y, 0f, Size));
            var edgeH = new float[perRow];
            var prefix = new double[perRow * 3 + 1];
            for (int i = 0; i < perRow; i++) edgeH[i] = EdgeHeight(edgePoint[i]);
            for (int i = 0; i < perRow * 3; i++) prefix[i + 1] = prefix[i] + edgeH[i % perRow];
            // The rim's small bumps would run outward as ribs; further out the edge
            // profile they grow from is smoothed along the rim.
            float Smoothed(int i, float d)
            {
                int half = Mathf.Clamp(Mathf.RoundToInt(d * 0.6f), 0, 80);
                if (half == 0) return edgeH[i];
                int a = i + perRow - half, b = i + perRow + half + 1;
                return (float)((prefix[b] - prefix[a]) / (b - a));
            }

            var verts = new Vector3[rows * perRow];
            var uvs = new Vector2[verts.Length];
            var uv2 = new Vector2[verts.Length];
            Parallel.For(0, rows, r =>
            {
                float d = BackdropRows[r];
                for (int i = 0; i < perRow; i++)
                {
                    Vector2 p = edgePoint[i] + normal[i] * d;
                    float h;
                    if (d < 0f) h = EdgeHeight(p) - 0.35f;
                    else if (d == 0f) h = edgeH[i] - 0.05f;
                    else
                    {
                        float ridge = Noise.Ridge(p.x * 0.010f + 7.1f, p.y * 0.010f - 3.3f, 5);
                        h = Smoothed(i, d) + d * 0.12f
                            + Smoothstep(8f, 170f, d) * (ridge * 80f - 6f)
                            + (Noise.Fbm(p.x * 0.05f, p.y * 0.05f, 3) - 0.5f) * 10f * Smoothstep(2f, 45f, d);
                    }
                    int v = r * perRow + i;
                    verts[v] = new Vector3(p.x, h, p.y);
                    uvs[v] = new Vector2(p.x / Size, p.y / Size);
                    Vector2 inside = new Vector2(Mathf.Clamp(p.x, 0f, Size), Mathf.Clamp(p.y, 0f, Size));
                    float ao = Mathf.Lerp(ReliefAO(gen, inside.x, inside.y), 1f, Smoothstep(0f, 20f, d));
                    uv2[v] = new Vector2(ao, Variation(p.x, p.y));
                }
            });

            var tris = new int[(rows - 1) * perRow * 6];
            int t = 0;
            for (int r = 0; r < rows - 1; r++)
                for (int i = 0; i < perRow; i++)
                {
                    int i1 = (i + 1) % perRow;
                    int a = r * perRow + i, b = r * perRow + i1, c = (r + 1) * perRow + i, e = (r + 1) * perRow + i1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = e;
                }

            var mesh = new Mesh { name = "Backdrop" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.uv2 = uv2;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            // Wound so the ground faces up.
            var ns = mesh.normals;
            float up = 0f;
            foreach (var n in ns) up += n.y;
            if (up < 0f)
            {
                for (int i = 0; i < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                mesh.triangles = tris;
                mesh.RecalculateNormals();
                ns = mesh.normals;
            }
            var colors = new Color[verts.Length];
            Parallel.For(0, verts.Length, v =>
            {
                var w = SplatAt(verts[v].x, verts[v].z, verts[v].y, ns[v]);
                colors[v] = new Color(w.x, w.y, w.z, w.w);
            });
            mesh.colors = colors;
            mesh.RecalculateBounds();

            // Outside the Map root on purpose: the NavMesh bake collects the Map's
            // children, and must not grow walkable ground beyond the rim.
            if (info.backdrop == null)
            {
                var go = new GameObject("Backdrop");
                info.backdrop = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            info.backdrop.sharedMesh = mesh;
            info.backdrop.GetComponent<MeshRenderer>().sharedMaterial = kit.backdropMaterial;
            return mesh;
        }

        // ------------------------------------------------------------ map objects
        sealed class Placer
        {
            readonly HeightfieldGenerator gen;
            readonly List<(Vector2 p, float r)> taken = new List<(Vector2, float)>();
            public Placer(HeightfieldGenerator g) { gen = g; }
            public void Take(Vector2 p, float r) => taken.Add((p, r));

            public bool Walkable(Vector2 p)
            {
                if (!gen.PassableCell((int)(p.x / HeightfieldGenerator.CELL), (int)(p.y / HeightfieldGenerator.CELL))) return false;
                foreach (var t in taken) if ((t.p - p).magnitude < t.r) return false;
                return true;
            }

            public bool Clear(Vector2 p, float r)
            {
                for (int i = 0; i < 8; i++)
                {
                    float a = i * TAU / 8f;
                    if (!Walkable(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r)) return false;
                }
                return Walkable(p);
            }

            public Vector2 NearestWalkable(Vector2 p)
            {
                if (Walkable(p)) return p;
                for (float r = 1f; r < 30f; r += 1f)
                    for (int i = 0; i < 16; i++)
                    {
                        float a = i * TAU / 16f;
                        var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        if (Walkable(q)) return q;
                    }
                return p;
            }
        }

        /// <summary>Removes a component or object built by an earlier map. Destroy waits for
        /// the end of the frame, and the rest of the scene wakes up (and finds things) in
        /// this frame, so objects are taken out of the hierarchy and switched off first.</summary>
        static void Remove(UnityEngine.Object o)
        {
            if (o == null) return;
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(o); return; }
            if (o is Behaviour b) b.enabled = false;
            if (o is GameObject go)
            {
                go.SetActive(false);
                go.transform.SetParent(null, false);
            }
            UnityEngine.Object.Destroy(o);
        }

        // Unity's missing-component placeholder is not null to ??, so test explicitly.
        static T Get<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static Transform Container(MapInfo info, ref Transform field, string name)
        {
            if (field == null)
            {
                field = new GameObject(name).transform;
                field.SetParent(info.transform, false);
            }
            var list = new List<GameObject>();
            foreach (Transform c in field) list.Add(c.gameObject);
            foreach (var go in list) Remove(go);
            return field;
        }

        static void PlaceMapObjects(MapInfo info, MapKit kit, HeightfieldGenerator gen, uint seed, Result result)
        {
            var rng = new Rng(seed ^ 0x51F0u);
            var placer = new Placer(gen);
            var oreRoot = Container(info, ref info.oreRoot, "Ore");
            var boulderRoot = Container(info, ref info.boulderRoot, "Boulders");

            Vector3 Ground(Vector2 p) => new Vector3(p.x, info.terrain.SampleHeight(new Vector3(p.x, 0, p.y)), p.y);

            var bases = new[] { BaseA, BaseB };
            for (int t = 0; t < 2; t++)
            {
                if (info.startLocations[t] == null)
                {
                    info.startLocations[t] = new GameObject($"Start_{t}").transform;
                    info.startLocations[t].SetParent(info.transform, false);
                }
                info.startLocations[t].position = Ground(bases[t]);
                placer.Take(bases[t], 7.5f);
            }

            // Each base's ore arc faces away from the map centre, as in the original.
            for (int t = 0; t < 2; t++)
            {
                Vector2 toCentre = Norm(new Vector2(Size * 0.5f, Size * 0.5f) - bases[t]);
                float baseAng = Mathf.Atan2(-toCentre.y, -toCentre.x);
                for (int i = 0; i < 8; i++)
                {
                    float a = baseAng + (i - 3.5f) * 0.20f;
                    Vector2 p = placer.NearestWalkable(bases[t] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 13.5f);
                    SpawnOre(kit, p, oreRoot, rng, Ground);
                    placer.Take(p, 2.2f);
                }
            }

            // Expansions, mirrored through the map centre so both players get
            // the same distances -- the original placed these independently.
            var expansions = new List<Vector2>();
            int placedPairs = 0;
            for (int tries = 0; tries < 400 && placedPairs < 2; tries++)
            {
                var p = new Vector2(rng.Range(35f, Size - 35f), rng.Range(35f, Size - 35f));
                var q = new Vector2(Size, Size) - p;
                if ((p - q).magnitude < 50f) continue;
                if ((p - BaseA).magnitude < 60f || (p - BaseB).magnitude < 60f) continue;
                if (!placer.Clear(p, 7f) || !placer.Clear(q, 7f)) continue;
                foreach (var c in new[] { p, q })
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float a = TAU * i / 6f;
                        var o = placer.NearestWalkable(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5f);
                        SpawnOre(kit, o, oreRoot, rng, Ground);
                        placer.Take(o, 2.2f);
                    }
                    placer.Take(c, 4f);
                    expansions.Add(c);
                }
                placedPairs++;
            }

            for (int i = 0; i < 55; i++)
                for (int tries = 0; tries < 30; tries++)
                {
                    var p = new Vector2(rng.Range(10f, Size - 10f), rng.Range(10f, Size - 10f));
                    if ((p - BaseA).magnitude < 30f || (p - BaseB).magnitude < 30f) continue;
                    if (!placer.Clear(p, 2.5f)) continue;
                    var b = Instantiate(kit.boulderPrefab, boulderRoot);
                    b.name = "Boulder";
                    b.transform.position = Ground(p) + Vector3.down * 0.15f;
                    b.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                    b.transform.localScale = Vector3.one * rng.Range(0.8f, 1.6f);
                    // Maulers crush boulders, and the rock is not baked as an obstacle:
                    // the ground under it is Rubble, which only a Mauler's path may cross
                    // (Boulder.cs).
                    Get<NavMeshModifier>(b).ignoreFromBuild = true;
                    var rubble = Get<NavMeshModifierVolume>(b);
                    rubble.area = RubbleArea;
                    rubble.center = new Vector3(0f, 1f, 0f);
                    rubble.size = new Vector3(2.3f, 4f, 2.3f);
                    placer.Take(p, 3f);
                    result.boulders++;
                    break;
                }

            var scenery = PlaceScenery(info, kit, gen, seed, result);
            var ore = new List<Vector2>();
            foreach (Transform o in oreRoot) ore.Add(new Vector2(o.position.x, o.position.z));
            result.ore = ore.Count;
            PlaceVegetation(info, kit, gen, seed, placer, ore, expansions, scenery, result);
        }

        /// <summary>Set dressing -- rock spires and shelves, a crashed dropship, ruined
        /// pylons -- only where most of the footprint is ground no unit can use (cliffs,
        /// the rim), and left out of the NavMesh bake, so the map looks richer without a
        /// single path changing.</summary>
        static List<(Vector2 p, float r)> PlaceScenery(MapInfo info, MapKit kit, HeightfieldGenerator gen, uint seed, Result result)
        {
            var rng = new Rng(seed ^ 0xC3A7u);
            var parent = Container(info, ref info.sceneryRoot, "Scenery");
            var placed = new List<(Vector2 p, float r)>();
            const float C = HeightfieldGenerator.CELL;

            bool Fits(Vector2 p, float r)
            {
                if (p.x < 4f || p.y < 4f || p.x > Size - 4f || p.y > Size - 4f) return false;
                if ((p - BaseA).magnitude < 34f || (p - BaseB).magnitude < 34f) return false;
                if (gen.HeightAt(p.x, p.y) < HeightfieldGenerator.WATER + 2f) return false;   // not in the shallows
                foreach (var q in placed) if ((q.p - p).magnitude < q.r + r + 6f) return false;
                int blocked = 0, total = 0;
                for (int ring = 0; ring <= 2; ring++)
                {
                    int n = ring == 0 ? 1 : 8;
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * TAU / n, d = r * ring * 0.45f;
                        var s = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                        total++;
                        if (!gen.PassableCell((int)(s.x / C), (int)(s.y / C)) && gen.HeightAt(s.x, s.y) > HeightfieldGenerator.WATER + 1f)
                            blocked++;
                    }
                }
                return blocked >= total * 0.8f;
            }

            foreach (var piece in kit.scenery)
            {
                if (piece.prefab == null) continue;
                int done = 0;
                for (int tries = 0; tries < 900 && done < piece.count; tries++)
                {
                    float scale = rng.Range(piece.minScale, piece.maxScale);
                    var p = new Vector2(rng.Range(6f, Size - 6f), rng.Range(6f, Size - 6f));
                    if (!Fits(p, piece.radius * scale)) continue;
                    var go = Instantiate(piece.prefab, parent);
                    go.name = piece.name;
                    float low = float.MaxValue;
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * TAU / 6f;
                        low = Mathf.Min(low, info.terrain.SampleHeight(new Vector3(p.x + Mathf.Cos(a) * piece.radius * scale * 0.5f, 0f,
                                                                                   p.y + Mathf.Sin(a) * piece.radius * scale * 0.5f)));
                    }
                    go.transform.position = new Vector3(p.x, low - 0.3f * scale, p.y);
                    go.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                    go.transform.localScale = Vector3.one * scale;
                    Get<NavMeshModifier>(go).ignoreFromBuild = true;
                    placed.Add((p, piece.radius * scale));
                    done++;
                    result.scenery++;
                }
            }
            return placed;
        }

        // ------------------------------------------------------------ vegetation
        const byte Pine = 0, Broad = 1, Tall = 2, Dead = 3, Bush = 4;

        /// <summary>Groves in the meadows, a few dead trees on the bare ground, conifers on
        /// the heights no unit reaches, and bushes everywhere green. Trees on ground units
        /// use mark the ground under their trunks Rubble (Vegetation.cs), and stay out of
        /// the bases, the ore fields, the expansions and narrow passages, so a grove is
        /// something to go round or crash through, never a wall across the map.</summary>
        static void PlaceVegetation(MapInfo info, MapKit kit, HeightfieldGenerator gen, uint seed, Placer placer,
                                    List<Vector2> ore, List<Vector2> expansions, List<(Vector2 p, float r)> scenery, Result result)
        {
            var rng = new Rng(seed ^ 0x7EE5u);
            var td = info.terrain.terrainData;
            if (info.vegetation == null)
            {
                var vgo = new GameObject("Vegetation");
                vgo.SetActive(false);            // wake it once it has its plants
                vgo.transform.SetParent(info.transform, false);
                info.vegetation = vgo.AddComponent<Vegetation>();
            }
            var veg = info.vegetation;
            var go = veg.gameObject;
            foreach (var old in go.GetComponents<NavMeshModifierVolume>()) Remove(old);
            veg.kinds = kit.plantKinds;
            var plants = new List<Plant>();
            var volumes = new List<NavMeshModifierVolume>();
            const float C = HeightfieldGenerator.CELL;

            float H(Vector2 p) => info.terrain.SampleHeight(new Vector3(p.x, 0f, p.y));
            Vector3 Nrm(Vector2 p) => td.GetInterpolatedNormal(p.x / Size, p.y / Size);
            Vector4 W(Vector2 p) => SplatAt(p.x, p.y, H(p), Nrm(p));
            float DBase(Vector2 p) => Mathf.Min((p - BaseA).magnitude, (p - BaseB).magnitude);
            float Nearest(List<Vector2> list, Vector2 p) { float m = 1e9f; foreach (var q in list) m = Mathf.Min(m, (q - p).magnitude); return m; }
            bool ClearOfScenery(Vector2 p, float pad) { foreach (var q in scenery) if ((q.p - p).magnitude < q.r * 0.8f + pad) return false; return true; }
            bool Spaced(Vector2 p, float gapTrees, float gapBushes)
            {
                foreach (var q in plants)
                {
                    float gap = veg.kinds[q.kind].bush ? gapBushes : gapTrees;
                    if ((new Vector2(q.pos.x, q.pos.z) - p).sqrMagnitude < gap * gap) return false;
                }
                return true;
            }
            bool Unreachable(Vector2 p, float r)
            {
                if (gen.PassableCell((int)(p.x / C), (int)(p.y / C))) return false;
                int open = 0;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * TAU / 8f;
                    var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (gen.PassableCell((int)(q.x / C), (int)(q.y / C))) open++;
                }
                return open <= 2;
            }
            var highTrees = new List<Vector2>();

            void Add(Vector2 p, byte kind, bool blocks)
            {
                var k = veg.kinds[kind];
                float scale = kind == Bush ? rng.Range(0.7f, 1.25f) : rng.Range(0.78f, 1.22f);
                var plant = new Plant
                {
                    pos = new Vector3(p.x, H(p) - 0.05f, p.y),
                    yaw = rng.Range(0f, 360f),
                    scale = scale,
                    stretch = rng.Range(0.9f, 1.15f),
                    kind = kind,
                    volume = -1
                };
                if (blocks)
                {
                    var v = go.AddComponent<NavMeshModifierVolume>();
                    v.area = RubbleArea;
                    float w = k.trunkRadius * scale * 2f + 0.5f;
                    v.center = new Vector3(p.x, plant.pos.y + 1.5f, p.y) - go.transform.position;
                    v.size = new Vector3(w, 5f, w);
                    plant.volume = (short)volumes.Count;
                    volumes.Add(v);
                }
                plants.Add(plant);
            }

            bool GroveGround(Vector2 p, float lichenMin)
            {
                if (p.x < 8f || p.y < 8f || p.x > Size - 8f || p.y > Size - 8f) return false;
                if (DBase(p) < 38f || Nearest(ore, p) < 11f || Nearest(expansions, p) < 15f) return false;
                if (H(p) < HeightfieldGenerator.WATER + 1.2f || Nrm(p).y < 0.9f) return false;
                return W(p).x >= lichenMin && ClearOfScenery(p, 3f);
            }

            // Groves on open meadow: every trunk needs walkable ground all round it,
            // which keeps them off ramps and out of narrow passes.
            var groves = new List<Vector2>();
            for (int tries = 0; tries < 4000 && groves.Count < 17; tries++)
            {
                var c = new Vector2(rng.Range(20f, Size - 20f), rng.Range(20f, Size - 20f));
                if (!GroveGround(c, 0.5f) || !placer.Clear(c, 7f)) continue;
                if (Nearest(groves, c) < 30f) continue;
                groves.Add(c);
                bool conifer = rng.F01() < 0.45f;
                float radius = rng.Range(6f, 12f);
                int want = Mathf.RoundToInt(radius * rng.Range(0.9f, 1.4f));
                int placedHere = 0;
                for (int t = 0; t < want * 12 && placedHere < want; t++)
                {
                    float a = rng.Range(0f, TAU), r = radius * Mathf.Sqrt(rng.F01());
                    var q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!GroveGround(q, 0.2f) || !placer.Clear(q, 3.2f) || !Spaced(q, 3.0f, 1.6f)) continue;
                    float roll = rng.F01();
                    byte kind = conifer ? (roll < 0.72f ? Pine : roll < 0.88f ? Tall : Broad)
                                        : (roll < 0.45f ? Broad : roll < 0.78f ? Tall : Pine);
                    Add(q, kind, true);
                    placedHere++;
                }
            }

            // A few trees standing alone in the meadows, and dead snags on bare ground.
            int lone = 0, dead = 0;
            for (int tries = 0; tries < 3000 && (lone < 22 || dead < 16); tries++)
            {
                var p = new Vector2(rng.Range(12f, Size - 12f), rng.Range(12f, Size - 12f));
                if (!placer.Clear(p, 3.5f) || !Spaced(p, 12f, 2f)) continue;
                if (DBase(p) < 36f || Nearest(ore, p) < 11f || Nearest(expansions, p) < 15f || !ClearOfScenery(p, 3f)) continue;
                if (H(p) < HeightfieldGenerator.WATER + 1.2f || Nrm(p).y < 0.9f) continue;
                var w = W(p);
                if (w.x > 0.5f && lone < 22) { Add(p, rng.F01() < 0.5f ? Broad : Tall, true); lone++; }
                else if (w.y + w.w > 0.7f && dead < 16) { Add(p, Dead, true); dead++; }
            }

            // Conifers and snags on the heights: ground no unit can reach, so they
            // block nothing and simply make the high country wooded.
            int high = 0;
            for (int tries = 0; tries < 5000 && high < 150; tries++)
            {
                var p = new Vector2(rng.Range(5f, Size - 5f), rng.Range(5f, Size - 5f));
                if (!Unreachable(p, 2.5f) || Nrm(p).y < 0.82f || H(p) < HeightfieldGenerator.WATER + 1.5f) continue;
                if (!ClearOfScenery(p, 2f) || !Spaced(p, 3.4f, 1.6f)) continue;
                // Clustered, not sprinkled: most tries only land near a tree already there.
                if (high > 8 && rng.F01() < 0.8f && Nearest(highTrees, p) > 7f) continue;
                Add(p, rng.F01() < 0.85f ? Pine : Dead, false);
                highTrees.Add(p);
                high++;
            }

            // Bushes in the green, thickest round the groves. They block nothing:
            // units push through them, and a Mauler flattens them.
            int bushes = 0;
            for (int tries = 0; tries < 9000 && bushes < 320; tries++)
            {
                var p = new Vector2(rng.Range(6f, Size - 6f), rng.Range(6f, Size - 6f));
                if (DBase(p) < 22f || Nearest(ore, p) < 5f || !ClearOfScenery(p, 1.5f)) continue;
                if (H(p) < HeightfieldGenerator.WATER + 0.9f || Nrm(p).y < 0.85f) continue;
                float nearGrove = Nearest(groves, p);
                if (W(p).x < (nearGrove < 16f ? 0.15f : 0.55f)) continue;
                if (nearGrove > 16f && rng.F01() < 0.55f) continue;
                if (!Spaced(p, 2.4f, 2.6f)) continue;
                Add(p, Bush, false);
                bushes++;
            }

            veg.plants = plants.ToArray();
            veg.volumes = volumes.ToArray();
            result.plants = plants.Count;
            result.blockingPlants = volumes.Count;
            if (!go.activeSelf) go.SetActive(true);
        }

        static void SpawnOre(MapKit kit, Vector2 p, Transform parent, Rng rng, Func<Vector2, Vector3> ground)
        {
            var o = Instantiate(kit.orePrefab, parent);
            o.name = "Ore";
            o.transform.position = ground(p);
            o.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
            // Ore depletes at runtime, so it cuts its own hole in the NavMesh
            // instead of being baked into it.
            Get<NavMeshModifier>(o).ignoreFromBuild = true;
        }
    }
}
