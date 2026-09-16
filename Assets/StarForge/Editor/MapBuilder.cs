// MapBuilder.cs — generates the Battlefield scene as ordinary, editable Unity content.
//
// The original game generated a new heightfield every match. Here that
// generator runs once, and everything it produces becomes normal assets: a
// TerrainData you can sculpt and paint, ore fields and boulders you can drag
// around, start locations, water, lighting, post-processing and a baked
// NavMesh. Re-running this regenerates the scene from scratch, so hand edits
// belong in the saved scene, not here.
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StarForge.View;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.EditorTools
{
    public static class MapBuilder
    {
        public const string MapDir = "Assets/StarForge/Map";
        public const string ScenePath = "Assets/StarForge/Scenes/Battlefield.unity";
        public const string PrefabDir = "Assets/StarForge/Prefabs";
        public const uint DefaultSeed = 1000;

        const float Size = HeightfieldGenerator.SIZE;
        const float TerrainHeight = 40f;
        const int HeightRes = 513, SplatRes = 512, AORes = 256;

        static readonly Vector2 BaseA = new Vector2(0.22f * Size, 0.26f * Size);
        static readonly Vector2 BaseB = new Vector2(0.78f * Size, 0.74f * Size);

        [MenuItem("StarForge/Build/3 Map Scene (regenerates)", priority = 3)]
        public static void BuildMenu() => Build(DefaultSeed);

        public static void Build(uint seed)
        {
            SFEditorUtil.EnsureFolder(MapDir);
            SFEditorUtil.EnsureFolder("Assets/StarForge/Scenes");

            var gen = new HeightfieldGenerator();
            gen.Generate(seed, BaseA, BaseB);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var mapRoot = new GameObject("Map");

            var terrainMat = BuildTerrainMaterial(out var layers);
            var terrain = BuildTerrain(gen, seed, mapRoot.transform, terrainMat, layers);
            BuildWater(gen, mapRoot.transform);
            BuildBackdrop(gen, terrainMat);

            var info = mapRoot.AddComponent<MapInfo>();
            info.terrain = terrain;
            info.mapSize = Size;
            info.waterLevel = HeightfieldGenerator.WATER;
            info.seed = seed;
            PlaceMapObjects(gen, seed, mapRoot.transform, info);

            BuildLighting();
            BuildPostProcessing();
            BuildCamera(terrain);
            BakeNavMesh(mapRoot);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            BakeEnvironment();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[StarForge] map built from seed {seed} -> {ScenePath}");
        }

        // ------------------------------------------------------------ terrain
        static Material BuildTerrainMaterial(out TerrainLayer[] layers)
        {
            string tex = SFAssetPostprocessor.TextureDir;
            var ground = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "ground.png");
            var groundN = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "ground_n.png");
            var cliff = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "cliff.png");
            var cliffN = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "cliff_n.png");
            var macro = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "terrain-macro.jpg");

            layers = new[]
            {
                Layer("Lichen", ground, groundN, 7f),
                Layer("Gravel", ground, groundN, 5f),
                Layer("Cliff", cliff, cliffN, 14f),
                Layer("Ash", ground, groundN, 4f),
            };

            var mat = SFEditorUtil.CreateOrLoadMaterial(MapDir + "/SF_Terrain.mat", Shader.Find("StarForge/Terrain"));
            // Tints are linear multipliers on the photographs. The gravel shot is a
            // neutral brown, so a mild green tint only turned the whole map olive;
            // the palette needs separation instead: teal lichen, warm rust gravel,
            // pale volcanic ash, so plateaus, paths and shores read apart.
            mat.SetColor("_Tint0", new Color(0.52f, 0.66f, 0.46f).gamma);
            mat.SetColor("_Tint1", new Color(1.20f, 0.92f, 0.74f).gamma);
            mat.SetColor("_Tint2", new Color(1.05f, 0.92f, 0.84f).gamma);
            mat.SetColor("_Tint3", new Color(1.55f, 1.42f, 1.28f).gamma);
            mat.SetFloat("_MacroStrength", 0.22f);
            mat.SetVector("_Tile", new Vector4(7f, 5f, 14f, 4f));
            mat.SetTexture("_MacroTex", macro);
            mat.SetFloat("_WaterLevel", HeightfieldGenerator.WATER);
            EditorUtility.SetDirty(mat);

            // The backdrop needs the layer textures bound by hand (no TerrainData).
            var back = SFEditorUtil.CreateOrLoadMaterial(MapDir + "/SF_Backdrop.mat", mat.shader);
            back.CopyPropertiesFromMaterial(mat);
            back.SetTexture("_Splat0", ground); back.SetTexture("_Normal0", groundN);
            back.SetTexture("_Splat1", ground); back.SetTexture("_Normal1", groundN);
            back.SetTexture("_Splat2", cliff); back.SetTexture("_Normal2", cliffN);
            back.SetTexture("_Splat3", ground); back.SetTexture("_Normal3", groundN);
            back.SetFloat("_AutoSplat", 1f);
            back.EnableKeyword("_SF_AUTOSPLAT");
            EditorUtility.SetDirty(back);
            return mat;
        }

        static TerrainLayer Layer(string name, Texture2D diffuse, Texture2D normal, float tile)
        {
            string path = $"{MapDir}/Layer_{name}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (l == null)
            {
                l = new TerrainLayer();
                AssetDatabase.CreateAsset(l, path);
            }
            l.diffuseTexture = diffuse;
            l.normalMapTexture = normal;
            l.tileSize = new Vector2(tile, tile);
            l.normalScale = 1f;
            EditorUtility.SetDirty(l);
            return l;
        }

        static Terrain BuildTerrain(HeightfieldGenerator gen, uint seed, Transform parent, Material mat, TerrainLayer[] layers)
        {
            string tdPath = MapDir + "/Battlefield_TerrainData.asset";
            AssetDatabase.DeleteAsset(tdPath);
            var td = new TerrainData { heightmapResolution = HeightRes };
            td.size = new Vector3(Size, TerrainHeight, Size);

            // Heights: the Catmull-Rom surface through the generator's corners,
            // plus a little fine noise away from the base plateaus so the
            // terraces stop looking machined.
            var heights = new float[HeightRes, HeightRes];
            float step = Size / (HeightRes - 1);
            for (int z = 0; z < HeightRes; z++)
                for (int x = 0; x < HeightRes; x++)
                {
                    float wx = x * step, wz = z * step;
                    float h = gen.SmoothHeightAt(wx, wz);
                    float dBase = Mathf.Min((new Vector2(wx, wz) - BaseA).magnitude, (new Vector2(wx, wz) - BaseB).magnitude);
                    float rough = Smoothstep(20f, 34f, dBase);
                    h += (Noise.Fbm(wx * 0.21f + 13.1f, wz * 0.21f - 7.7f, 3) - 0.5f) * 0.45f * rough;
                    heights[z, x] = Mathf.Clamp01(h / TerrainHeight);
                }
            td.SetHeights(0, 0, heights);

            td.alphamapResolution = SplatRes;
            td.baseMapResolution = 512;
            td.terrainLayers = layers;
            var alpha = new float[SplatRes, SplatRes, 4];
            float sstep = Size / SplatRes;
            for (int z = 0; z < SplatRes; z++)
                for (int x = 0; x < SplatRes; x++)
                {
                    float wx = (x + 0.5f) * sstep, wz = (z + 0.5f) * sstep;
                    Vector3 n = td.GetInterpolatedNormal((x + 0.5f) / SplatRes, (z + 0.5f) / SplatRes);
                    float h = td.GetInterpolatedHeight((x + 0.5f) / SplatRes, (z + 0.5f) / SplatRes);
                    // Rock wherever the terraces turn steep.
                    float cliffW = Smoothstep(0.90f, 0.66f, n.y);
                    float shore = 1f - Smoothstep(HeightfieldGenerator.WATER + 0.4f, HeightfieldGenerator.WATER + 1.8f, h);
                    // Moss grows in broad patches with ragged edges. GroundScatter grows
                    // its grass on this layer, so patches become meadows and the ground
                    // between them stays bare gravel -- the variety a single green
                    // blanket lacked.
                    float patch = Noise.Fbm(wx * 0.024f + 5.3f, wz * 0.024f + 1.9f, 4)
                                + (Noise.Fbm(wx * 0.11f - 3.1f, wz * 0.11f + 8.2f, 3) - 0.5f) * 0.22f;
                    float lichen = Smoothstep(0.47f, 0.60f, patch);
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
                    alpha[z, x, 0] = wl / sum;
                    alpha[z, x, 1] = wg / sum;
                    alpha[z, x, 2] = cliffW / sum;
                    alpha[z, x, 3] = wa / sum;
                }
            td.SetAlphamaps(0, 0, alpha);
            AssetDatabase.CreateAsset(td, tdPath);

            mat.SetTexture("_AOTex", BuildAOTexture(gen));
            EditorUtility.SetDirty(mat);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Terrain>();
            t.materialTemplate = mat;
            t.drawInstanced = false;
            t.heightmapPixelError = 4f;
            t.basemapDistance = 4000f;
            t.drawTreesAndFoliage = false;
            t.shadowCastingMode = ShadowCastingMode.On;
            t.allowAutoConnect = false;
            return t;
        }

        // Relief occlusion, as the original baked into its terrain vertices: pits
        // and cliff bases darken, exposed ridges stay bright.
        static Texture2D BuildAOTexture(HeightfieldGenerator gen)
        {
            string path = MapDir + "/Battlefield_TerrainAO.asset";
            AssetDatabase.DeleteAsset(path);
            var tex = new Texture2D(AORes, AORes, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Battlefield_TerrainAO"
            };
            var px = new Color32[AORes * AORes];
            float cell = Size / AORes;
            int[] dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dz8 = { 0, 0, 1, -1, 1, -1, 1, -1 };
            for (int z = 0; z < AORes; z++)
                for (int x = 0; x < AORes; x++)
                {
                    float wx = (x + 0.5f) * cell, wz = (z + 0.5f) * cell;
                    float hc = gen.HeightAt(wx, wz);
                    float occ = 0f; int cnt = 0;
                    for (int r = 1; r <= 4; r++)
                        for (int i = 0; i < 8; i++)
                        {
                            float hh = gen.HeightAt(wx + dx8[i] * r * 2f, wz + dz8[i] * r * 2f);
                            occ += Saturate((hh - hc) / (r * 2f * 1.15f));
                            cnt++;
                        }
                    float ao = 1f - Saturate(occ / cnt * 1.75f);
                    float variation = Noise.Fbm(wx * 0.055f + 91f, wz * 0.055f - 44f, 4);
                    px[z * AORes + x] = new Color32((byte)(ao * 255), (byte)(Saturate(variation) * 255), 0, 255);
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        // ------------------------------------------------------------ water
        static void BuildWater(HeightfieldGenerator gen, Transform parent)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            const int N = HeightfieldGenerator.N;
            const float C = HeightfieldGenerator.CELL, W = HeightfieldGenerator.WATER;
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    float lo = Mathf.Min(Mathf.Min(gen.CornerHeight(x, z), gen.CornerHeight(x + 1, z)),
                                         Mathf.Min(gen.CornerHeight(x, z + 1), gen.CornerHeight(x + 1, z + 1)));
                    if (lo >= W + 0.35f) continue;
                    int b = verts.Count;
                    verts.Add(new Vector3(x * C, W, z * C));
                    verts.Add(new Vector3((x + 1) * C, W, z * C));
                    verts.Add(new Vector3((x + 1) * C, W, (z + 1) * C));
                    verts.Add(new Vector3(x * C, W, (z + 1) * C));
                    tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
            if (verts.Count == 0) return;

            string meshPath = MapDir + "/Battlefield_WaterMesh.asset";
            AssetDatabase.DeleteAsset(meshPath);
            var mesh = new Mesh { name = "Water", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, meshPath);

            var mat = SFEditorUtil.CreateOrLoadMaterial(MapDir + "/SF_Water.mat", Shader.Find("StarForge/Water"));
            mat.SetTexture("_NormalTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SFAssetPostprocessor.TextureDir + "water_n.jpg"));
            EditorUtility.SetDirty(mat);

            var go = new GameObject("Water");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            // Water is where the NavMesh stops: marking this surface not-walkable
            // leaves too little clearance for the lake bed beneath it.
            var mod = go.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = 1;
        }

        // ------------------------------------------------------------ backdrop
        // Mountains beyond the rim, so a zoomed-out camera at the map edge sees
        // a landscape fading into haze instead of the edge of the world.
        static void BuildBackdrop(HeightfieldGenerator gen, Material terrainMat)
        {
            const int R = 96;
            const float ext = 256f;
            float min = -ext, span = Size + 2f * ext, stepSz = span / R;
            var verts = new Vector3[(R + 1) * (R + 1)];
            var uvs = new Vector2[verts.Length];
            for (int z = 0; z <= R; z++)
                for (int x = 0; x <= R; x++)
                {
                    float wx = min + x * stepSz, wz = min + z * stepSz;
                    var c = new Vector2(Mathf.Clamp(wx, 0f, Size), Mathf.Clamp(wz, 0f, Size));
                    float d = (new Vector2(wx, wz) - c).magnitude;
                    float h;
                    bool inside = wx > 0.01f && wz > 0.01f && wx < Size - 0.01f && wz < Size - 0.01f;
                    if (inside) h = -8f;
                    else
                    {
                        float rim = gen.HeightAt(c.x, c.y);
                        float ridge = Noise.Ridge(wx * 0.010f + 7.1f, wz * 0.010f - 3.3f, 5);
                        h = rim - 0.4f + Smoothstep(0f, 110f, d) * (ridge * 85f - 8f)
                            + (Noise.Fbm(wx * 0.05f, wz * 0.05f, 3) - 0.5f) * 10f * Smoothstep(0f, 40f, d);
                    }
                    verts[z * (R + 1) + x] = new Vector3(wx, h, wz);
                    uvs[z * (R + 1) + x] = new Vector2(wx / Size, wz / Size);
                }
            var tris = new int[R * R * 6];
            int k = 0;
            for (int z = 0; z < R; z++)
                for (int x = 0; x < R; x++)
                {
                    int i0 = z * (R + 1) + x, i1 = i0 + 1, i2 = i0 + R + 1, i3 = i2 + 1;
                    tris[k++] = i0; tris[k++] = i2; tris[k++] = i3;
                    tris[k++] = i0; tris[k++] = i3; tris[k++] = i1;
                }
            string meshPath = MapDir + "/Backdrop.asset";
            AssetDatabase.DeleteAsset(meshPath);
            var mesh = new Mesh { name = "Backdrop" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject("Backdrop");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MapDir + "/SF_Backdrop.mat");
            mr.shadowCastingMode = ShadowCastingMode.Off;
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

        static void PlaceMapObjects(HeightfieldGenerator gen, uint seed, Transform root, MapInfo info)
        {
            var rng = new Rng(seed ^ 0x51F0u);
            var placer = new Placer(gen);
            var oreRoot = new GameObject("Ore").transform; oreRoot.SetParent(root, false);
            var decoRoot = new GameObject("Boulders").transform; decoRoot.SetParent(root, false);

            Vector3 Ground(Vector2 p) => new Vector3(p.x, info.terrain.SampleHeight(new Vector3(p.x, 0, p.y)), p.y);

            var bases = new[] { BaseA, BaseB };
            for (int t = 0; t < 2; t++)
            {
                var start = new GameObject($"Start_{t}").transform;
                start.SetParent(root, false);
                start.position = Ground(bases[t]);
                info.startLocations[t] = start;
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
                    SpawnOre(p, oreRoot, rng, Ground);
                    placer.Take(p, 2.2f);
                }
            }

            // Expansions, mirrored through the map centre so both players get
            // the same distances -- the original placed these independently.
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
                        SpawnOre(o, oreRoot, rng, Ground);
                        placer.Take(o, 2.2f);
                    }
                    placer.Take(c, 4f);
                }
                placedPairs++;
            }

            for (int i = 0; i < 55; i++)
                for (int tries = 0; tries < 30; tries++)
                {
                    var p = new Vector2(rng.Range(10f, Size - 10f), rng.Range(10f, Size - 10f));
                    if ((p - BaseA).magnitude < 30f || (p - BaseB).magnitude < 30f) continue;
                    if (!placer.Clear(p, 2.5f)) continue;
                    var b = Spawn("Boulder", decoRoot);
                    b.transform.position = Ground(p) + Vector3.down * 0.15f;
                    b.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                    b.transform.localScale = Vector3.one * rng.Range(0.8f, 1.6f);
                    GameObjectUtility.SetStaticEditorFlags(b, StaticEditorFlags.BatchingStatic);
                    placer.Take(p, 3f);
                    break;
                }

            PlaceScenery(gen, seed, root, info);
        }

        /// <summary>Set dressing from Tools/blender/build_env.py -- rock spires and
        /// shelves, a crashed dropship, ruined pylons -- only where most of the
        /// footprint is ground no unit can use (cliffs, the rim), and left out of the
        /// NavMesh bake, so the map looks richer without a single path changing.</summary>
        static void PlaceScenery(HeightfieldGenerator gen, uint seed, Transform root, MapInfo info)
        {
            var rng = new Rng(seed ^ 0xC3A7u);
            var parent = new GameObject("Scenery").transform;
            parent.SetParent(root, false);
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

            void Scatter(string kind, int count, float radius, float minScale, float maxScale)
            {
                int done = 0;
                for (int tries = 0; tries < 900 && done < count; tries++)
                {
                    float scale = rng.Range(minScale, maxScale);
                    var p = new Vector2(rng.Range(6f, Size - 6f), rng.Range(6f, Size - 6f));
                    if (!Fits(p, radius * scale)) continue;
                    var go = ModelFactory.Create(kind, 0, parent);
                    float low = float.MaxValue;
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * TAU / 6f;
                        low = Mathf.Min(low, info.terrain.SampleHeight(new Vector3(p.x + Mathf.Cos(a) * radius * scale * 0.5f, 0f, p.y + Mathf.Sin(a) * radius * scale * 0.5f)));
                    }
                    go.transform.position = new Vector3(p.x, low - 0.3f * scale, p.y);
                    go.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                    go.transform.localScale = Vector3.one * scale;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                    go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
                    placed.Add((p, radius * scale));
                    done++;
                }
            }

            Scatter("ROCK_SPIRE", 7, 3.4f, 0.8f, 1.35f);
            Scatter("RUIN_PYLON", 4, 3.0f, 0.9f, 1.2f);
            Scatter("WRECK", 2, 5.9f, 0.9f, 1.1f);
            Scatter("ROCK_SHELF", 9, 5.0f, 0.7f, 1.2f);
        }

        static void SpawnOre(Vector2 p, Transform parent, Rng rng, System.Func<Vector2, Vector3> ground)
        {
            var o = Spawn("Ore", parent);
            o.transform.position = ground(p);
            o.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
            // Ore depletes at runtime, so it cuts its own hole in the NavMesh
            // instead of being baked into it.
            var mod = o.GetComponent<NavMeshModifier>() ?? o.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;
        }

        static GameObject Spawn(string kind, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{kind}.prefab");
            GameObject go;
            if (prefab != null) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            else go = ModelFactory.Create(kind.ToUpperInvariant(), 0, parent);
            go.name = kind;
            return go;
        }

        // ------------------------------------------------------------ lighting & post
        static void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.86f, 0.70f);
            // Brighter than before the cloud cookie: drifting cloud shadow takes
            // roughly a fifth off the average sunlight.
            sun.intensity = 2.95f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.shadowNormalBias = 0.35f;
            sun.shadowBias = 0.05f;
            // Warm and roughly perpendicular to the default camera heading, so units
            // and cliffs throw readable shadows. At 34 degrees (the original's sun)
            // terrace cliffs shadowed whole bases on this terrain; ~46 keeps the
            // shadows long without burying the plateau below each cliff.
            sunGo.transform.rotation = Quaternion.LookRotation(-new Vector3(0.70f, 0.78f, -0.30f).normalized);
            RenderSettings.sun = sun;

            // Cloud shadows: a remapped cloud photograph as the sun's cookie, tiled
            // every 240 m and drifted by Atmosphere.
            sun.cookie = BuildCloudCookie();
            if (!sunGo.TryGetComponent(out UniversalAdditionalLightData sunData))
                sunData = sunGo.AddComponent<UniversalAdditionalLightData>();
            sunData.lightCookieSize = new Vector2(240f, 240f);

            var sky = SFEditorUtil.CreateOrLoadMaterial(MapDir + "/SF_Sky.mat", Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.9f);
            sky.SetColor("_SkyTint", new Color(0.46f, 0.52f, 0.66f));
            sky.SetColor("_GroundColor", new Color(0.30f, 0.27f, 0.27f));
            sky.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Cool sky fill against the warm key: shadows go blue instead of black.
            // Lifted a little now that SSAO darkens contact and crevices on top.
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.82f, 1.0f);
            RenderSettings.ambientEquatorColor = new Color(0.60f, 0.58f, 0.60f);
            RenderSettings.ambientGroundColor = new Color(0.27f, 0.24f, 0.21f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.85f;

            // Distance fog comes from Atmosphere's height fog in the full-screen
            // pass; the built-in linear fog would apply it twice.
            RenderSettings.fog = false;
            var atmo = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            atmo.sun = sun;
            atmo.baseHeight = HeightfieldGenerator.WATER;
        }

        /// <summary>The cloud photograph remapped to a light multiplier: clear sky 1,
        /// thick cloud 0.38, soft in between.</summary>
        static Texture2D BuildCloudCookie()
        {
            string path = MapDir + "/SF_CloudCookie.png";
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes(SFAssetPostprocessor.TextureDir + "clouds.jpg"));
            const int N = 512;
            var px = new Color32[N * N];
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    float cloud = Smoothstep(0.18f, 0.8f, src.GetPixelBilinear((x + 0.5f) / N, (z + 0.5f) / N).grayscale);
                    byte b = (byte)((1f - 0.62f * cloud) * 255f);
                    px[z * N + x] = new Color32(b, b, b, 255);
                }
            Object.DestroyImmediate(src);
            WriteTexture(path, N, N, px, srgb: false, repeat: true);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Lens dirt for bloom: faint bokeh specks, denser toward the edges,
        /// and a few broad smudges. Only visible when something bright blooms.</summary>
        static Texture2D BuildLensDirt()
        {
            string path = MapDir + "/SF_LensDirt.png";
            const int W = 1024, H = 640;
            var acc = new float[W * H];
            var rng = new Rng(77);
            for (int i = 0; i < 150; i++)
            {
                float cx = rng.Range(0f, W), cy = rng.Range(0f, H);
                float edge = Mathf.Max(Mathf.Abs(cx / W - 0.5f), Mathf.Abs(cy / H - 0.5f)) * 2f;
                if (rng.F01() > 0.35f + edge * 0.65f) continue;
                float r = rng.Range(5f, 46f), inten = rng.Range(0.05f, 0.24f);
                for (int y = Mathf.Max(0, (int)(cy - r)); y < Mathf.Min(H, (int)(cy + r) + 1); y++)
                    for (int x = Mathf.Max(0, (int)(cx - r)); x < Mathf.Min(W, (int)(cx + r) + 1); x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                        if (d >= 1f) continue;
                        acc[y * W + x] += inten * (1f - Smoothstep(0.8f, 1f, d)) * (0.65f + 0.35f * d);
                    }
            }
            for (int i = 0; i < 10; i++)
            {
                float cx = rng.Range(0f, W), cy = rng.Range(0f, H), ang = rng.Range(0f, Mathf.PI);
                float ra = rng.Range(90f, 260f), rb = rng.Range(20f, 60f), inten = rng.Range(0.03f, 0.08f);
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                int ext = (int)ra;
                for (int y = Mathf.Max(0, (int)cy - ext); y < Mathf.Min(H, (int)cy + ext); y++)
                    for (int x = Mathf.Max(0, (int)cx - ext); x < Mathf.Min(W, (int)cx + ext); x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float u = (dx * ca + dy * sa) / ra, v = (-dx * sa + dy * ca) / rb;
                        acc[y * W + x] += inten * Mathf.Exp(-(u * u + v * v) * 2.5f);
                    }
            }
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++)
            {
                float a = Saturate(acc[i]);
                px[i] = new Color32((byte)(a * 255f), (byte)(a * 240f), (byte)(a * 222f), 255);
            }
            WriteTexture(path, W, H, px, srgb: true, repeat: false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void WriteTexture(string path, int w, int h, Color32[] px, bool srgb, bool repeat)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = srgb;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }

        static void BuildPostProcessing()
        {
            string path = MapDir + "/SF_PostProfile.asset";
            var profile = SFEditorUtil.CreateOrLoadAsset<VolumeProfile>(path);

            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet(out T c))
                {
                    c = profile.Add<T>(true);
                    c.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
                return c;
            }

            Get<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.85f);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(1.0f, 0.95f, 0.9f));
            bloom.dirtTexture.Override(BuildLensDirt());
            bloom.dirtIntensity.Override(2.2f);
            var grain = Get<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.16f);
            grain.response.Override(0.85f);
            var ca = Get<ColorAdjustments>();
            ca.postExposure.Override(0.65f);
            ca.contrast.Override(12f);
            ca.saturation.Override(14f);
            var vig = Get<Vignette>();
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.45f);
            Get<WhiteBalance>().temperature.Override(5f);
            var smh = Get<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(0.90f, 0.98f, 1.14f, 0f));
            smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.95f, 0f));
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Global Volume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        static void BuildCamera(Terrain terrain)
        {
            var rig = new GameObject("CameraRig");
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 900f;
            cam.fieldOfView = 40f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            camGo.AddComponent<AudioListener>();

            Vector3 focus = new Vector3(BaseA.x, terrain.SampleHeight(new Vector3(BaseA.x, 0, BaseA.y)), BaseA.y);
            var rot = Quaternion.Euler(48f, 45f, 0f);
            camGo.transform.SetPositionAndRotation(focus - rot * Vector3.forward * 80f, rot);
        }

        // ------------------------------------------------------------ navigation
        static void ConfigureAgent()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var settings = so.FindProperty("m_Settings");
            if (settings == null || settings.arraySize == 0) return;
            var a = settings.GetArrayElementAtIndex(0);
            a.FindPropertyRelative("agentRadius").floatValue = 0.6f;
            a.FindPropertyRelative("agentHeight").floatValue = 2.0f;
            a.FindPropertyRelative("agentSlope").floatValue = 40f;
            a.FindPropertyRelative("agentClimb").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BakeNavMesh(GameObject mapRoot)
        {
            ConfigureAgent();
            var surface = mapRoot.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
            surface.agentTypeID = 0;
            surface.layerMask = ~0;
            surface.BuildNavMesh();
            string path = MapDir + "/Battlefield_NavMesh.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
        }

        static void BakeEnvironment()
        {
            string path = MapDir + "/SF_Lighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (ls == null)
            {
                ls = new LightingSettings { name = "SF_Lighting" };
                AssetDatabase.CreateAsset(ls, path);
            }
            ls.bakedGI = false;
            ls.realtimeGI = false;
            Lightmapping.lightingSettings = ls;
            Lightmapping.Bake();
        }

        static void AddSceneToBuild(string path)
        {
            var list = new List<EditorBuildSettingsScene>();
            list.Add(new EditorBuildSettingsScene(path, true));
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != path && s.path != "Assets/Scenes/SampleScene.unity") list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
