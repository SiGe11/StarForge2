// MapBuilder.cs — the editor side of the battlefield: the assets a map is built
// from, and the map the Battlefield scene is saved with.
//
// The map itself is made by MapGenerator, the same code that builds a new map
// at the start of every match (MapRuntime). This step prepares everything that
// is an asset rather than generated -- terrain and water materials, terrain
// layers, scenery prefabs, the plant kinds -- into a MapKit, builds the scene
// (lighting, post-processing, camera, the Map with its generator), runs the
// generator once with the default seed and saves what it made, so the scene
// has a map to look at and edit in the editor. Re-running it regenerates the
// scene from scratch.
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
        public const string KitPath = MapDir + "/SF_MapKit.asset";
        public const uint DefaultSeed = MapGenerator.DefaultSeed;
        /// <summary>Deeper than this, water stops units; shallower, they wade in.</summary>
        public const float WadeDepth = MapGenerator.WadeDepth;

        const float Size = MapGenerator.Size;
        static readonly Vector2 BaseA = MapGenerator.BaseA;

        [MenuItem("StarForge/Build/3 Map Scene (regenerates)", priority = 3)]
        public static void BuildMenu() => Build(DefaultSeed);

        public static void Build(uint seed)
        {
            SFEditorUtil.EnsureFolder(MapDir);
            SFEditorUtil.EnsureFolder("Assets/StarForge/Scenes");
            ConfigureAgent();
            var kit = BuildKit();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var mapRoot = new GameObject("Map");
            var info = mapRoot.AddComponent<MapInfo>();
            var surface = mapRoot.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
            surface.agentTypeID = 0;
            surface.layerMask = ~0;
            var runtime = mapRoot.AddComponent<MapRuntime>();
            runtime.kit = kit;

            // The saved scene keeps its prefab links.
            var place = MapGenerator.Instantiate;
            MapGenerator.Instantiate = (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            MapGenerator.Result r;
            try { r = MapGenerator.Generate(info, kit, seed, bakeNavMesh: false, sharedMaterials: true); }
            finally { MapGenerator.Instantiate = place; }
            Debug.Log($"[StarForge] shore corners slumped into beaches: {r.gen.BeachCorners}");
            Debug.Log($"[StarForge] map from seed {seed}: {r.ore} ore, {r.boulders} boulders, {r.scenery} scenery, " +
                      $"{r.plants} plants ({r.blockingPlants} trees block ground units, {r.grovesDropped} groves dropped to keep paths open)");
            SaveGenerated(r, info);
            foreach (Transform t in info.sceneryRoot)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);

            BuildLighting();
            BuildPostProcessing();
            BuildCamera(info.terrain);
            BakeNavMesh(surface);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            BakeEnvironment();
            EditorSceneManager.SaveScene(scene, ScenePath);
            PaintSplat(info.terrain, r.splat);
            Debug.Log($"[StarForge] map built from seed {seed} -> {ScenePath}");
        }

        /// <summary>What the generator made in memory, saved as assets the scene can reference.</summary>
        static void SaveGenerated(MapGenerator.Result r, MapInfo info)
        {
            void Save(Object asset, string path)
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(asset, path);
            }
            // The splat texture is a sub-asset, so the TerrainData asset exists before it is painted (PaintSplat).
            Save(r.terrainData, MapDir + "/Battlefield_TerrainData.asset");
            Save(r.ao, MapDir + "/Battlefield_TerrainAO.asset");
            r.terrainMaterial.SetTexture("_AOTex", r.ao);
            EditorUtility.SetDirty(r.terrainMaterial);
            Save(r.water, MapDir + "/Battlefield_WaterMesh.asset");
            Save(r.backdrop, MapDir + "/Backdrop.asset");
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------ kit
        static MapKit BuildKit()
        {
            var kit = SFEditorUtil.CreateOrLoadAsset<MapKit>(KitPath);
            kit.terrainMaterial = BuildTerrainMaterial(out var layers);
            kit.terrainLayers = layers;
            kit.backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(MapDir + "/SF_Backdrop.mat");
            kit.waterMaterial = BuildWaterMaterial();
            kit.orePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Ore.prefab");
            kit.boulderPrefabs = BoulderVariants();
            // Scanned crags and slabs (Tools/blender/build_rocks.py) where the
            // procedural spire and shelf stood; the same counts and footprints.
            kit.scenery = new[]
            {
                Scenery("SCAN_CRAG_A", 4, 3.0f, 0.85f, 1.25f),
                Scenery("SCAN_CRAG_B", 3, 3.8f, 0.8f, 1.2f),
                Scenery("RUIN_PYLON", 4, 3.0f, 0.9f, 1.2f),
                Scenery("WRECK", 2, 5.9f, 0.9f, 1.1f),
                Scenery("SCAN_SHELF_A", 9, 5.0f, 0.7f, 1.2f),
            };
            kit.plantKinds = LoadPlantKinds();
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            return kit;
        }

        /// <summary>The Boulder prefab (PrefabBuilder) once per scanned rock: the same
        /// crushable rock and Rubble footprint, with a different scan as its body.</summary>
        static GameObject[] BoulderVariants()
        {
            var baseline = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Boulder.prefab");
            var list = new List<GameObject> { baseline };
            foreach (string v in new[] { "B", "C", "D", "E", "F" })
            {
                string path = $"{PrefabDir}/Boulder_{v}.prefab";
                var go = (GameObject)PrefabUtility.InstantiatePrefab(baseline);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var body = go.transform.Find("Body");
                for (int i = body.childCount - 1; i >= 0; i--) Object.DestroyImmediate(body.GetChild(i).gameObject);
                ModelFactory.Create("SCAN_BOULDER_" + v, 0, body);
                list.Add(PrefabUtility.SaveAsPrefabAsset(go, path));
                Object.DestroyImmediate(go);
            }
            return list.ToArray();
        }

        /// <summary>Set dressing from Tools/blender/build_env.py as a prefab with its URP
        /// materials, so a match can place it.</summary>
        static SceneryPiece Scenery(string model, int count, float radius, float minScale, float maxScale)
        {
            string path = $"{PrefabDir}/Scenery_{model}.prefab";
            var go = ModelFactory.Create(model, 0);
            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return new SceneryPiece { name = model, prefab = prefab, count = count, radius = radius, minScale = minScale, maxScale = maxScale };
        }

        static Material BuildWaterMaterial()
        {
            var mat = SFEditorUtil.CreateOrLoadMaterial(MapDir + "/SF_Water.mat", Shader.Find("StarForge/Water"));
            mat.SetTexture("_NormalTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SFAssetPostprocessor.TextureDir + "water_n.jpg"));
            // The bed seen through the water is tinted and absorbed (red first), and
            // the water's own scattered light fills in: clear green shallows, dark
            // blue-green deeps.
            mat.SetColor("_ShallowColor", new Color(0.86f, 0.98f, 0.93f));
            mat.SetColor("_DeepColor", new Color(0.035f, 0.16f, 0.19f));
            mat.SetVector("_Absorption", new Vector4(0.6f, 0.2f, 0.16f, 0f));
            mat.SetFloat("_Refraction", 0.035f);
            mat.SetFloat("_NormalStrength", 0.6f);
            mat.SetFloat("_SwellStrength", 0.35f);
            mat.SetFloat("_DepthRange", 2.2f);
            mat.SetFloat("_ShoreWaves", 0.6f);
            mat.SetFloat("_CausticStrength", 0.35f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------ terrain
        static Material BuildTerrainMaterial(out TerrainLayer[] layers)
        {
            // Ground scans from Poly Haven (CC0), packed by Tools/blender/pack_textures.py:
            // colour + height, and normals. Splat order stays lichen, gravel, cliff,
            // ash (MapGenerator.SplatAt): meadow earth under the grass, the stony
            // dirt most of the map stands on, rock for the terrace cliffs and pale
            // sand on shores and dry ridges.
            string tex = SFAssetPostprocessor.TextureDir + "Terrain/";
            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(tex + n) ??
                                     throw new FileNotFoundException(tex + n + " (run Tools/blender/pack_textures.py)");
            var meadow = T("meadow_ch.png"); var meadowN = T("meadow_nrm.jpg");
            var dirt = T("dirt_ch.png"); var dirtN = T("dirt_nrm.jpg");
            var cliff = T("cliff_ch.png"); var cliffN = T("cliff_nrm.jpg");
            var sand = T("sand_ch.png"); var sandN = T("sand_nrm.jpg");
            var macro = AssetDatabase.LoadAssetAtPath<Texture2D>(SFAssetPostprocessor.TextureDir + "terrain-macro.jpg");

            // Tile sizes: the dirt scan is 3.15 m across in reality, but a tile that
            // small repeats visibly across a 256 m map; the meadow and sand scans
            // are 15 m aerial shots, the cliff 50 m, shrunk so their stones stay
            // in scale with the units.
            var tile = new Vector4(6f, 5f, 16f, 7f);
            layers = new[]
            {
                Layer("Lichen", meadow, meadowN, tile.x),
                Layer("Gravel", dirt, dirtN, tile.y),
                Layer("Cliff", cliff, cliffN, tile.z),
                Layer("Ash", sand, sandN, tile.w),
            };

            var mat = SFEditorUtil.CreateOrLoadMaterial(MapDir + "/SF_Terrain.mat", Shader.Find("StarForge/Terrain"));
            // The scans carry their own, measured colour: the tints only nudge the
            // layers apart (a cooler rock, a greener meadow).
            mat.SetColor("_Tint0", new Color(0.92f, 1.0f, 0.86f).gamma);
            mat.SetColor("_Tint1", new Color(0.93f, 0.95f, 1.0f).gamma);
            mat.SetColor("_Tint2", new Color(0.86f, 0.9f, 1.0f).gamma);
            mat.SetColor("_Tint3", new Color(0.95f, 0.96f, 1.0f).gamma);
            mat.SetVector("_Smooth", new Vector4(0.10f, 0.14f, 0.20f, 0.12f));
            mat.SetVector("_NScale", new Vector4(1.0f, 1.0f, 1.2f, 0.8f));
            mat.SetVector("_HeightScale", new Vector4(0.8f, 1.0f, 1.0f, 0.7f));
            mat.SetFloat("_BlendSharpness", 0.2f);
            mat.SetFloat("_Cavity", 0.35f);
            mat.SetFloat("_MacroStrength", 0.22f);
            mat.SetVector("_Tile", tile);
            mat.SetTexture("_MacroTex", macro);
            mat.SetFloat("_WaterLevel", HeightfieldGenerator.WATER);
            EditorUtility.SetDirty(mat);

            // The backdrop needs the layer textures bound by hand (no TerrainData).
            var back = SFEditorUtil.CreateOrLoadMaterial(MapDir + "/SF_Backdrop.mat", mat.shader);
            back.CopyPropertiesFromMaterial(mat);
            back.SetTexture("_Splat0", meadow); back.SetTexture("_Normal0", meadowN);
            back.SetTexture("_Splat1", dirt); back.SetTexture("_Normal1", dirtN);
            back.SetTexture("_Splat2", cliff); back.SetTexture("_Normal2", cliffN);
            back.SetTexture("_Splat3", sand); back.SetTexture("_Normal3", sandN);
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

        /// <summary>Writes the splat weights straight into the splat texture inside the
        /// TerrainData asset, as the last step of the build. TerrainData.SetAlphamaps,
        /// or painting the texture any earlier, looked right until something later in
        /// the build reloaded the asset from disk with a blank splat texture: every
        /// map built that way rendered, and grew grass, as pure moss. (A match's map
        /// is never saved, so there SetAlphamaps is enough.)</summary>
        static void PaintSplat(Terrain terrain, float[,,] splat)
        {
            var td = terrain.terrainData;
            var tex = td.alphamapTextures[0];
            int res = tex.width;
            var px = new Color32[res * res];
            var share = new double[4];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float l = splat[z, x, 0], c = splat[z, x, 2], a = splat[z, x, 3];
                    // Rounding each weight alone can leave the sum a count or two off;
                    // gravel takes the remainder so the layers add to one.
                    byte lb = (byte)Mathf.RoundToInt(l * 255f), cb = (byte)Mathf.RoundToInt(c * 255f), ab = (byte)Mathf.RoundToInt(a * 255f);
                    px[z * res + x] = new Color32(lb, (byte)Mathf.Clamp(255 - lb - cb - ab, 0, 255), cb, ab);
                    for (int k = 0; k < 4; k++) share[k] += splat[z, x, k];
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            EditorUtility.SetDirty(tex);
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            var back = td.GetAlphamaps(res / 2, res / 2, 1, 1);
            double cells = res * res;
            Debug.Log($"[StarForge] terrain layers: lichen {share[0] / cells:P0}, gravel {share[1] / cells:P0}, cliff {share[2] / cells:P0}, ash {share[3] / cells:P0} " +
                      $"(centre reads back {back[0, 0, 0]:0.00}/{back[0, 0, 1]:0.00}/{back[0, 0, 2]:0.00}/{back[0, 0, 3]:0.00})");
        }

        // ------------------------------------------------------------ vegetation
        /// <summary>The plant kinds, in Vegetation.kinds order (MapGenerator's kind
        /// constants index it): model, trunk radius, how readily it burns, colours and
        /// the leaf surface.</summary>
        static readonly PlantKind[] PlantKinds =
        {
            new PlantKind { name = "TREE_PINE", burns = 1.25f, trunkRadius = 0.30f, blockRadius = 1.6f, bark = new Color(0.30f, 0.22f, 0.16f),
                            leaf = new Color(0.12f, 0.25f, 0.14f), leaf2 = new Color(0.20f, 0.31f, 0.15f), needles = true, leafTiling = 0.6f,
                            cardTint = new Color(0.55f, 0.66f, 0.58f) },
            new PlantKind { name = "TREE_BROAD", burns = 0.75f, trunkRadius = 0.38f, blockRadius = 1.5f, bark = new Color(0.30f, 0.24f, 0.18f),
                            leaf = new Color(0.14f, 0.27f, 0.08f), leaf2 = new Color(0.28f, 0.35f, 0.10f), leafTiling = 0.42f,
                            cardTint = new Color(0.42f, 0.5f, 0.34f) },
            new PlantKind { name = "TREE_TALL", burns = 0.8f, trunkRadius = 0.26f, blockRadius = 1.1f, bark = new Color(0.50f, 0.48f, 0.43f),
                            leaf = new Color(0.16f, 0.29f, 0.09f), leaf2 = new Color(0.29f, 0.35f, 0.10f), leafTiling = 0.5f,
                            cardTint = new Color(0.48f, 0.56f, 0.42f) },
            new PlantKind { name = "TREE_DEAD", burns = 1.6f, trunkRadius = 0.34f, blockRadius = 1.0f, bark = new Color(0.30f, 0.26f, 0.22f), leaf = Color.black, leaf2 = Color.black },
            new PlantKind { name = "BUSH", bush = true, burns = 1.1f, trunkRadius = 0.55f, bark = new Color(0.30f, 0.24f, 0.18f),
                            leaf = new Color(0.13f, 0.25f, 0.08f), leaf2 = new Color(0.25f, 0.32f, 0.10f), leafTiling = 0.75f,
                            cardTint = new Color(0.46f, 0.54f, 0.4f) },
            new PlantKind { name = "TREE_BIRCH", burns = 0.9f, trunkRadius = 0.22f, blockRadius = 1.1f, bark = new Color(0.80f, 0.78f, 0.73f), birchBark = true,
                            leaf = new Color(0.22f, 0.34f, 0.08f), leaf2 = new Color(0.38f, 0.44f, 0.12f), leafTiling = 0.6f,
                            cardTint = new Color(0.56f, 0.62f, 0.44f) },
            new PlantKind { name = "FERN", bush = true, burns = 1.2f, trunkRadius = 0.3f, bark = new Color(0.25f, 0.2f, 0.15f),
                            leaf = new Color(0.14f, 0.28f, 0.07f), leaf2 = new Color(0.26f, 0.38f, 0.09f), leafTiling = 1.2f,
                            drawDistance = 95f, castShadows = false, cardTint = new Color(0.5f, 0.6f, 0.46f) },
            new PlantKind { name = "REEDS", bush = true, burns = 1.5f, trunkRadius = 0.3f, bark = new Color(0.30f, 0.19f, 0.10f),
                            leaf = new Color(0.20f, 0.29f, 0.10f), leaf2 = new Color(0.36f, 0.38f, 0.15f), leafTiling = 1.0f,
                            drawDistance = 110f, castShadows = false },
            new PlantKind { name = "BUSH_FLOWER", bush = true, burns = 1.0f, trunkRadius = 0.55f, bark = new Color(0.30f, 0.24f, 0.18f),
                            leaf = new Color(0.14f, 0.25f, 0.08f), leaf2 = new Color(0.25f, 0.32f, 0.10f), leafTiling = 0.8f,
                            bloom = new Color(0.95f, 0.78f, 0.88f, 0.34f), drawDistance = 140f, cardTint = new Color(0.5f, 0.56f, 0.42f) },
        };

        static PlantKind[] LoadPlantKinds()
        {
            string json = File.ReadAllText(Path.Combine(SFAssetPostprocessor.ModelDir, "models.json"));
            var kinds = new PlantKind[PlantKinds.Length];
            for (int k = 0; k < kinds.Length; k++)
            {
                var d = PlantKinds[k];
                var kind = kinds[k] = new PlantKind
                {
                    name = d.name, bush = d.bush, trunkRadius = d.trunkRadius, blockRadius = d.blockRadius, bark = d.bark, leaf = d.leaf, leaf2 = d.leaf2,
                    needles = d.needles, bloom = d.bloom, birchBark = d.birchBark, leafTiling = d.leafTiling, burns = d.burns,
                    drawDistance = d.drawDistance, castShadows = d.castShadows, cardTint = d.cardTint,
                };
                // Leaf-spray cards (Tools/blender/make_leaf_cards.py): which spray each kind wears.
                string spray = d.name switch
                {
                    "TREE_PINE" => "conifer", "FERN" => "conifer",
                    "TREE_BROAD" => "oak",
                    "TREE_BIRCH" => "birch", "BUSH_FLOWER" => "birch",
                    "TREE_TALL" => "beech", "BUSH" => "beech",
                    _ => null,
                };
                if (spray != null)
                {
                    kind.cardTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SFAssetPostprocessor.TextureDir}Leaves/{spray}_col.png");
                    kind.cardNormal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SFAssetPostprocessor.TextureDir}Leaves/{spray}_nrm.png");
                    if (kind.cardTex == null) Debug.LogWarning($"[StarForge] leaf cards {spray} missing: run Tools/blender/make_leaf_cards.py");
                }
                string path = $"{SFAssetPostprocessor.ModelDir}SF_{d.name}.fbx";
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (a is Mesh m) { kind.mesh = m; break; }
                if (kind.mesh == null) throw new FileNotFoundException($"plant model {path} has not been imported");
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath($"{SFAssetPostprocessor.ModelDir}SF_{d.name}_LOD1.fbx"))
                    if (a is Mesh m) { kind.lodMesh = m; break; }

                // This model's entry in models.json: its slots (submesh order) and crown.
                int at = json.IndexOf($"\"{d.name}\":", System.StringComparison.Ordinal);
                int end = json.IndexOf("\"chunks\"", at, System.StringComparison.Ordinal);
                int next = json.IndexOf("\n  \"", end, System.StringComparison.Ordinal);
                string entry = json.Substring(at, (next < 0 ? json.Length : next) - at);
                var slots = System.Text.RegularExpressions.Regex.Match(entry, "\"materials\":\\s*\\[([^\\]]*)\\]").Groups[1].Value;
                var names = new List<string>();
                foreach (var part in slots.Split(',')) { string n = part.Trim().Trim('"'); if (n.Length > 0) names.Add(n); }
                kind.barkSubmesh = names.IndexOf("bark");
                kind.foliageSubmesh = Mathf.Max(names.IndexOf("leaf"), names.IndexOf("needle"));
                kind.height = ModelFactory.Meta(d.name).height;
                var fol = System.Text.RegularExpressions.Regex.Match(entry,
                    "\"center\":\\s*\\[([^\\]]*)\\],\\s*\"radii\":\\s*\\[([^\\]]*)\\]");
                if (fol.Success)
                {
                    kind.crownCenter = ParseVec(fol.Groups[1].Value);
                    kind.crownRadii = ParseVec(fol.Groups[2].Value);
                }
                else kind.crownRadii = Vector3.zero;
            }
            return kinds;
        }

        static Vector3 ParseVec(string csv)
        {
            var p = csv.Split(',');
            float F(int i) => float.Parse(p[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            return new Vector3(F(0), F(1), F(2));
        }

        // ------------------------------------------------------------ lighting & post
        static void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.92f, 0.82f);
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
            // A single overbright pixel (a glint, a shading spike) should not bloom
            // into a light of its own.
            bloom.clamp.Override(24f);
            var grain = Get<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.16f);
            grain.response.Override(0.85f);
            var ca = Get<ColorAdjustments>();
            // Nearly neutral: the ground scans carry their own colour, and the old
            // warm grade (saturation +14, white balance +5 on an amber sun) turned
            // every brown into orange.
            ca.postExposure.Override(0.6f);
            ca.contrast.Override(14f);
            ca.saturation.Override(3f);
            var vig = Get<Vignette>();
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.45f);
            Get<WhiteBalance>().temperature.Override(0f);
            var smh = Get<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(0.90f, 0.98f, 1.14f, 0f));
            smh.highlights.Override(new Vector4(1.02f, 1.0f, 0.97f, 0f));
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
        /// <summary>NavMesh area under boulders and trunks; see Boulder.cs and GameWorld.GroundAreas.</summary>
        public const int RubbleArea = MapGenerator.RubbleArea;

        static void ConfigureAgent()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var areas = so.FindProperty("areas");
            if (areas != null && areas.arraySize > RubbleArea)
            {
                areas.GetArrayElementAtIndex(RubbleArea).FindPropertyRelative("name").stringValue = "Rubble";
                areas.GetArrayElementAtIndex(RubbleArea).FindPropertyRelative("cost").floatValue = 1f;
            }
            var settings = so.FindProperty("m_Settings");
            if (settings == null || settings.arraySize == 0) return;
            var a = settings.GetArrayElementAtIndex(0);
            a.FindPropertyRelative("agentRadius").floatValue = 0.6f;
            a.FindPropertyRelative("agentHeight").floatValue = 2.0f;
            a.FindPropertyRelative("agentSlope").floatValue = 40f;
            a.FindPropertyRelative("agentClimb").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BakeNavMesh(NavMeshSurface surface)
        {
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
