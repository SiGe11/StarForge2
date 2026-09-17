// SceneAssembler.cs — wires the game systems into the Battlefield scene and
// offers a one-click "Build All" that runs every generator in order.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using StarForge.Game;
using StarForge.UI;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class SceneAssembler
    {
        const string FxDir = "Assets/StarForge/Art/Materials/FX";
        const string UIDir = "Assets/StarForge/UI";

        [MenuItem("StarForge/Build All", priority = -100)]
        public static void BuildAll()
        {
            RenderSetup.Build();
            SFMaterialLibrary.Build();
            PrefabBuilder.Build();
            MapBuilder.Build(MapBuilder.DefaultSeed);
            Assemble();
        }

        [MenuItem("StarForge/Build/4 Assemble Game Scene", priority = 4)]
        public static void Assemble()
        {
            var scene = EditorSceneManager.OpenScene(MapBuilder.ScenePath, OpenSceneMode.Single);
            var old = GameObject.Find("Game");
            if (old != null) Object.DestroyImmediate(old);

            var map = Object.FindAnyObjectByType<MapInfo>();
            var game = new GameObject("Game");

            var worldGo = new GameObject("World");
            worldGo.transform.SetParent(game.transform, false);
            var world = worldGo.AddComponent<GameWorld>();
            var wso = new SerializedObject(world);
            wso.FindProperty("map").objectReferenceValue = map;
            wso.ApplyModifiedPropertiesWithoutUndo();

            var bootGo = new GameObject("Bootstrap");
            bootGo.transform.SetParent(game.transform, false);
            var boot = bootGo.AddComponent<GameBootstrap>();
            boot.world = world;
            bootGo.AddComponent<AIEvalRunner>();   // idle unless StarForge > Evaluate AI started it
            bootGo.AddComponent<BenchmarkRunner>(); // idle unless the player is launched with -sfbench
            bootGo.AddComponent<DisplayModes>();    // full screen / window switches keep the display's shape

            var rigGo = GameObject.Find("CameraRig");
            var cam = rigGo != null ? rigGo.GetComponentInChildren<Camera>() : Camera.main;
            var rtsCam = cam.GetComponent<RTSCamera>() ?? cam.gameObject.AddComponent<RTSCamera>();
            rtsCam.cam = cam;
            rtsCam.map = map;
            cam.allowDynamicResolution = false;

            var playerGo = new GameObject("Player");
            playerGo.transform.SetParent(game.transform, false);
            var player = playerGo.AddComponent<PlayerController>();
            player.world = world;
            player.rig = rtsCam;
            player.team = 0;

            var fowGo = new GameObject("FogOfWar");
            fowGo.transform.SetParent(game.transform, false);
            var fow = fowGo.AddComponent<FogOfWarRenderer>();
            fow.world = world;

            var fxGo = new GameObject("FX");
            fxGo.transform.SetParent(game.transform, false);
            var fx = fxGo.AddComponent<FXDirector>();
            fx.world = world;
            fx.player = player;
            fx.rig = rtsCam;
            BuildFxMaterials(fx);

            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(game.transform, false);
            groundGo.AddComponent<GroundMask>().world = world;
            var scatter = groundGo.AddComponent<GroundScatter>();
            scatter.map = map;
            BuildScatterMaterials(scatter);

            var vegetation = map != null ? map.GetComponentInChildren<Vegetation>() : null;
            if (vegetation != null)
            {
                var trees = groundGo.AddComponent<VegetationRenderer>();
                trees.vegetation = vegetation;
                trees.world = world;
                trees.player = player;
                BuildTreeMaterials(trees);
            }

            game.AddComponent<AdaptiveResolution>();
            game.AddComponent<QualityController>();
            game.AddComponent<AssetWarmup>();

            var audio = fxGo.AddComponent<AudioDirector>();
            audio.world = world;
            audio.player = player;
            audio.rig = rtsCam;

            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(game.transform, false);
            var doc = hudGo.AddComponent<UIDocument>();
            doc.panelSettings = PanelSettingsAsset();
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UIDir + "/StarForgeHUD.uxml");
            var hud = hudGo.AddComponent<HUDController>();
            hud.world = world;
            hud.player = player;
            hud.rig = rtsCam;
            hud.bootstrap = boot;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[StarForge] game scene assembled: " + MapBuilder.ScenePath);
        }

        static PanelSettings PanelSettingsAsset()
        {
            string path = UIDir + "/SF_PanelSettings.asset";
            var ps = SFEditorUtil.CreateOrLoadAsset<PanelSettings>(path);
            // Reference 1920x1080, matched halfway between width and height, so the
            // HUD grows ~1.3x on the MacBook Neo's 2408x1506 panel and stays proportional.
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.sortingOrder = 10;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return ps;
        }

        static void BuildScatterMaterials(GroundScatter scatter)
        {
            var grass = Shader.Find("StarForge/Grass");
            Material Grass(string name, Color root, Color tip, float variation, float translucency)
            {
                var m = SFEditorUtil.CreateOrLoadMaterial($"{MapBuilder.MapDir}/{name}.mat", grass);
                m.SetColor("_RootColor", root);
                m.SetColor("_TipColor", tip);
                m.SetFloat("_Variation", variation);
                m.SetFloat("_Translucency", translucency);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                return m;
            }
            scatter.grassMaterial = Grass("SF_Grass", new Color(0.10f, 0.15f, 0.09f), new Color(0.36f, 0.45f, 0.25f), 0.28f, 0.5f);
            scatter.dryGrassMaterial = Grass("SF_GrassDry", new Color(0.22f, 0.18f, 0.12f), new Color(0.52f, 0.46f, 0.32f), 0.22f, 0.5f);

            var pebble = SFEditorUtil.CreateOrLoadMaterial(MapBuilder.MapDir + "/SF_Pebble.mat", Shader.Find("Universal Render Pipeline/Lit"));
            pebble.SetColor("_BaseColor", new Color(0.36f, 0.33f, 0.30f));
            pebble.SetFloat("_Smoothness", 0.12f);
            pebble.SetFloat("_Metallic", 0f);
            pebble.enableInstancing = true;
            EditorUtility.SetDirty(pebble);
            scatter.pebbleMaterial = pebble;
            AssetDatabase.SaveAssets();
        }

        static void BuildTreeMaterials(VegetationRenderer trees)
        {
            var shader = Shader.Find("StarForge/Tree");
            Material Tree(string name, bool foliage)
            {
                var m = SFEditorUtil.CreateOrLoadMaterial($"{MapBuilder.MapDir}/{name}.mat", shader);
                m.SetFloat("_Foliage", foliage ? 1f : 0f);
                m.SetFloat("_WindStrength", foliage ? 0.12f : 0.06f);
                m.SetFloat("_Translucency", 0.7f);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                return m;
            }
            trees.barkMaterial = Tree("SF_TreeBark", false);
            trees.foliageMaterial = Tree("SF_TreeFoliage", true);
            AssetDatabase.SaveAssets();
        }

        static void BuildFxMaterials(FXDirector fx)
        {
            SFEditorUtil.EnsureFolder(FxDir);
            string tex = SFAssetPostprocessor.TextureDir;
            var particle = Shader.Find("StarForge/Particle");
            var decal = Shader.Find("StarForge/GroundDecal");
            var billboard = Shader.Find("StarForge/Billboard");

            Material Particle(string name, string sheet, bool alpha, float floor, float gain)
            {
                var m = SFEditorUtil.CreateOrLoadMaterial($"{FxDir}/{name}.mat", particle);
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tex + sheet));
                m.SetFloat("_AlphaMode", alpha ? 1f : 0f);
                m.SetFloat("_Floor", floor);
                m.SetFloat("_Gain", gain);
                m.SetFloat("_SheetTiles", 1f);
                m.SetFloat("_SrcBlend", alpha ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", alpha ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
                m.renderQueue = 3000;
                EditorUtility.SetDirty(m);
                return m;
            }

            Material Instanced(string name, Shader s, UnityEngine.Rendering.BlendMode src, UnityEngine.Rendering.BlendMode dst, int queue)
            {
                var m = SFEditorUtil.CreateOrLoadMaterial($"{FxDir}/{name}.mat", s);
                m.enableInstancing = true;
                m.SetFloat("_SrcBlend", (float)src);
                m.SetFloat("_DstBlend", (float)dst);
                m.renderQueue = queue;
                EditorUtility.SetDirty(m);
                return m;
            }

            fx.fireMaterial = Particle("FX_Fire", "explosion.jpg", false, 0.03f, 1.5f);
            var smokeShader = Shader.Find("StarForge/Smoke");
            var puffs = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "smoke_puffs.png");
            Material Smoke(string name, float billow, float density)
            {
                var m = SFEditorUtil.CreateOrLoadMaterial($"{FxDir}/{name}.mat", smokeShader);
                m.SetTexture("_MainTex", puffs);
                m.SetFloat("_Billow", billow);
                m.SetFloat("_Density", density);
                m.renderQueue = 3000;
                EditorUtility.SetDirty(m);
                return m;
            }
            fx.smokeMaterial = Smoke("FX_Smoke", 1f, 1.2f);
            fx.dropletMaterial = Smoke("FX_Droplet", 0f, 2.2f);
            fx.glowMaterial = Particle("FX_Glow", "particles.jpg", false, 0.02f, 2.2f);
            // Debris trails stretch one soft glow cell of the sheet along their length.
            fx.trailMaterial = Particle("FX_Trail", "particles.jpg", false, 0.02f, 2.4f);
            fx.trailMaterial.SetVector("_SheetRect", new Vector4(0f, 0.5f, 0.25f, 0.25f));
            fx.debrisMaterial = SFEditorUtil.CreateOrLoadMaterial($"{FxDir}/FX_Debris.mat",
                Shader.Find("Universal Render Pipeline/Particles/Simple Lit"));
            fx.debrisMaterial.SetColor("_BaseColor", new Color(0.42f, 0.40f, 0.37f));
            fx.debrisMaterial.enableInstancing = true;
            EditorUtility.SetDirty(fx.debrisMaterial);

            fx.ringMaterial = Instanced("FX_GroundRings", decal, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.One, 2800);
            fx.scorchMaterial = Instanced("FX_Scorch", decal, UnityEngine.Rendering.BlendMode.SrcAlpha, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, 2790);
            fx.scorchMaterial.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "scorch.jpg"));
            fx.barMaterial = Instanced("FX_HealthBars", billboard, UnityEngine.Rendering.BlendMode.SrcAlpha, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, 3100);
            fx.barMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            fx.streakMaterial = Instanced("FX_Streaks", billboard, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.One, 3050);
            fx.streakMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            // Wakes and splashes lie on the water, so they draw just after it.
            fx.rippleMaterial = Instanced("FX_WaterRipple", Shader.Find("StarForge/WaterRipple"),
                UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, 2910);
            AssetDatabase.SaveAssets();
        }
    }
}
