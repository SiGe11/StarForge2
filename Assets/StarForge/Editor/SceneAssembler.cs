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

            // Wildlife: Quaternius's animated animals (Tools/pack_fauna.py).
            var fauna = groundGo.AddComponent<Fauna>();
            fauna.species = BuildFauna();

            game.AddComponent<AdaptiveResolution>();
            game.AddComponent<QualityController>();
            game.AddComponent<AssetWarmup>();

            var audio = fxGo.AddComponent<AudioDirector>();
            audio.world = world;
            audio.player = player;
            audio.rig = rtsCam;
            audio.bank = BuildSoundBank();

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
            // The dirt scan's own brown, a shade darker: darker still, pebbles read
            // from above as holes in the ground.
            pebble.SetColor("_BaseColor", new Color(0.47f, 0.42f, 0.36f));
            pebble.SetFloat("_Smoothness", 0.12f);
            pebble.SetFloat("_Metallic", 0f);
            pebble.enableInstancing = true;
            EditorUtility.SetDirty(pebble);
            scatter.pebbleMaterial = pebble;
            AssetDatabase.SaveAssets();
        }

        /// <summary>Prefabs for the animals: each model turned to face +Z, scaled to its
        /// size, set on the ground, and its parts coloured (the models' own materials
        /// are plain grey), with the species' behaviour settings for Fauna.</summary>
        static FaunaSpecies[] BuildFauna()
        {
            string dir = SFAssetPostprocessor.FaunaDir;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Material Mat(string name, Color c, float smooth)
            {
                var m = SFEditorUtil.CreateOrLoadMaterial($"{MapBuilder.MapDir}/Fauna_{name}.mat", lit);
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", 0f);
                EditorUtility.SetDirty(m);
                return m;
            }
            (GameObject prefab, AnimationClip[] clips) Build(string model, float length, bool span, Vector3 facing, Material[] partsBySize, float lift = 0f)
            {
                string fbx = dir + model + ".fbx";
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (src == null) { Debug.LogWarning($"[StarForge] {fbx} missing: run python3 Tools/pack_fauna.py"); return (null, null); }
                var root = new GameObject("Fauna_" + model);
                var body = (GameObject)Object.Instantiate(src, root.transform);
                body.name = model;
                // Colour the parts: the largest submesh gets the first colour, and so on.
                foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = r.sharedMesh;
                    var order = new System.Collections.Generic.List<int>();
                    for (int i = 0; i < mesh.subMeshCount; i++) order.Add(i);
                    order.Sort((x, y) => mesh.GetSubMesh(y).indexCount.CompareTo(mesh.GetSubMesh(x).indexCount));
                    var mats = new Material[mesh.subMeshCount];
                    for (int k = 0; k < order.Count; k++) mats[order[k]] = partsBySize[Mathf.Min(k, partsBySize.Length - 1)];
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.updateWhenOffscreen = false;
                }
                // The skeleton's extent in world space, from the bones themselves:
                // the imported renderer bounds were metres across while the mesh, in
                // these files' centimetre units, was a few centimetres long, and a
                // freshly imported skinned mesh bakes in its bind pose, not its bones'.
                Bounds Posed()
                {
                    var smrs = body.GetComponentsInChildren<SkinnedMeshRenderer>();
                    var b = new Bounds();
                    bool first = true;
                    foreach (var smr in smrs)
                        foreach (var bone in smr.bones)
                        {
                            if (bone == null) continue;
                            if (first) { b = new Bounds(bone.position, Vector3.zero); first = false; } else b.Encapsulate(bone.position);
                        }
                    return b;
                }
                // Face +Z. The rigs' bones are unnamed (Bone.001 ...), so each model's
                // facing is given: the side its head chain runs to in the file.
                body.transform.rotation = Quaternion.FromToRotation(facing, Vector3.forward) * body.transform.rotation;
                // The files carry cameras and lamps; only their empty transforms import.
                foreach (var t in body.GetComponentsInChildren<Transform>())
                    if (t != null && t != body.transform && (t.name.StartsWith("Camera") || t.name.StartsWith("Lamp") || t.name.StartsWith("Sun")))
                        Object.DestroyImmediate(t.gameObject);
                var bounds = Posed();
                // Size: body length (or wingspan) in metres, feet on the ground.
                bounds = Posed();
                float have = (span ? bounds.size.x : bounds.size.z) / 0.85f;   // bones stop short of the skin
                body.transform.localScale = body.transform.localScale * (length / Mathf.Max(1e-5f, have));
                bounds = Posed();
                body.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) - root.transform.position;
                // The feet can reach below the lowest bone (the songbird's hang 7 cm under its leg bone).
                body.transform.position += Vector3.up * lift * length;
                bounds = Posed();
                // Culling and shadow bounds from the real mesh, with room for the cycles.
                foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var inv = smr.transform.worldToLocalMatrix;
                    var lb = new Bounds(inv.MultiplyPoint3x4(bounds.center), Vector3.zero);
                    for (int c = 0; c < 8; c++)
                        lb.Encapsulate(inv.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents * 1.6f,
                            new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                    smr.localBounds = lb;
                }
                var anim = body.GetComponent<Animation>() ?? body.AddComponent<Animation>();
                var clips = new System.Collections.Generic.List<AnimationClip>();
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbx))
                    if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                    {
                        c.legacy = true;
                        c.wrapMode = WrapMode.Loop;
                        clips.Add(c);
                        if (anim.GetClip(c.name) == null) anim.AddClip(c, c.name);
                    }
                anim.playAutomatically = false;
                anim.cullingType = AnimationCullingType.BasedOnRenderers;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{MapBuilder.PrefabDir}/Fauna_{model}.prefab");
                Object.DestroyImmediate(root);
                return (prefab, clips.ToArray());
            }
            AnimationClip Clip(AnimationClip[] clips, string part)
            {
                if (clips == null) return null;
                foreach (var c in clips) if (c.name.ToLowerInvariant().Contains(part.ToLowerInvariant())) return c;
                return clips.Length > 0 ? clips[0] : null;
            }

            var fur = Mat("WolfFur", new Color(0.30f, 0.29f, 0.27f), 0.12f);
            var foxRed = Mat("FoxRed", new Color(0.60f, 0.25f, 0.08f), 0.12f);
            var foxWhite = Mat("FoxWhite", new Color(0.82f, 0.78f, 0.70f), 0.12f);
            var eagleBrown = Mat("EagleBrown", new Color(0.20f, 0.13f, 0.07f), 0.15f);
            var eagleWhite = Mat("EagleWhite", new Color(0.88f, 0.87f, 0.84f), 0.15f);
            var eagleYellow = Mat("EagleYellow", new Color(0.85f, 0.62f, 0.10f), 0.3f);
            // The songbird is coloured by a palette texture (Tools/blender/build_songbird.py).
            var songbird = Mat("Songbird", Color.white, 0.22f);
            songbird.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "Songbird_palette.png"));
            EditorUtility.SetDirty(songbird);

            var wolf = Build("Wolf", 1.7f, false, Vector3.right, new[] { fur });
            var fox = Build("Fox", 1.1f, false, Vector3.forward, new[] { foxRed, foxWhite });
            var eagle = Build("Eagle", 2.6f, true, Vector3.forward, new[] { eagleBrown, eagleWhite, eagleBrown, eagleYellow });
            // Bigger than life (a crow, not a finch): a real songbird is a few pixels
            // across at this camera and disappears into the grass.
            var bird = Build("Songbird", 0.9f, false, Vector3.back, new[] { songbird }, 0.16f);
            AssetDatabase.SaveAssets();
            return new[]
            {
                new FaunaSpecies { name = "Wolf", prefab = wolf.prefab, groups = 2, minPer = 3, maxPer = 5, speed = 1.3f, runSpeed = 7.5f,
                                   move = Clip(wolf.clips, "walk"), moveClipSpeed = 1.3f, idle = Clip(wolf.clips, "idle"), spacing = 2.6f, wariness = 22f },
                new FaunaSpecies { name = "Fox", prefab = fox.prefab, groups = 3, minPer = 1, maxPer = 1, speed = 1.1f, runSpeed = 6.5f,
                                   move = Clip(fox.clips, "armature"), moveClipSpeed = 1.6f, spacing = 0f, wariness = 18f },
                new FaunaSpecies { name = "Eagle", prefab = eagle.prefab, flies = true, groups = 2, minPer = 1, maxPer = 1, speed = 7f, runSpeed = 14f,
                                   move = Clip(eagle.clips, "fly"), moveClipSpeed = 7f, altitude = 34f, circle = 30f, wariness = 20f },
                // Feeding flocks on open ground, flying low from patch to patch.
                new FaunaSpecies { name = "Songbird", prefab = bird.prefab, flies = true, perches = true, groups = 3, minPer = 6, maxPer = 10,
                                   speed = 7.5f, runSpeed = 11f, move = Clip(bird.clips, "fly"), fold = Clip(bird.clips, "fold"),
                                   idle = Clip(bird.clips, "perch"), peck = Clip(bird.clips, "peck"), flapRate = 2.0f, hop = 0.5f,
                                   altitude = 10f, circle = 4.5f, spacing = 0.7f, wariness = 16f },
            };
        }

        /// <summary>The recordings and synthesised loops Tools/make_audio.py put in
        /// Audio/, by name: a name with takes (rifle_0, rifle_1 ...) becomes an array.</summary>
        static SoundBank BuildSoundBank()
        {
            const string Dir = "Assets/StarForge/Audio/";
            AudioClip One(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + path);
            AudioClip[] Takes(string folder, string stem, string ext)
            {
                var list = new System.Collections.Generic.List<AudioClip>();
                for (int i = 0; i < 16; i++)
                {
                    var c = One($"{folder}/{stem}_{i}.{ext}");
                    if (c == null) break;
                    list.Add(c);
                }
                return list.ToArray();
            }
            var b = new SoundBank
            {
                // The guns and the wood are built by make_audio.py (.wav); the rest are Kenney's (.ogg).
                rifle = Takes("Sfx", "rifle", "wav"), cannon = Takes("Sfx", "cannon", "wav"),
                pulse = Takes("Sfx", "pulse", "wav"), bolt = Takes("Sfx", "bolt", "wav"),
                treeFall = Takes("Sfx", "treefall", "wav"), treeCrash = Takes("Sfx", "treecrash", "wav"),
                crush = Takes("Sfx", "crush", "wav"),
                boom = Takes("Sfx", "boom", "ogg"), bigBoom = Takes("Sfx", "bigboom", "ogg"), rumble = Takes("Sfx", "rumble", "ogg"),
                thud = Takes("Sfx", "thud", "ogg"), hitMetal = Takes("Sfx", "hitmetal", "ogg"), crystal = Takes("Sfx", "crystal", "ogg"),
                rock = Takes("Sfx", "rock", "ogg"), mine = Takes("Sfx", "mine", "ogg"),
                stomp = Takes("Sfx", "stomp", "ogg"), shield = Takes("Sfx", "shield", "ogg"), build = Takes("Sfx", "build", "ogg"),
                engineHeavy = One("Sfx/engine_heavy.ogg"), engineHover = One("Sfx/engine_hover.ogg"),
                uiSelect = One("Sfx/ui_select.ogg"), uiClick = One("Sfx/ui_click.ogg"), uiMove = One("Sfx/ui_move.ogg"),
                uiAttack = One("Sfx/ui_attack.ogg"), uiError = One("Sfx/ui_error.ogg"), uiDone = One("Sfx/ui_done.ogg"),
                uiNotice = One("Sfx/ui_notice.ogg"), uiAlert = One("Sfx/ui_alert.ogg"), uiPromote = One("Sfx/ui_promote.ogg"),
                uiPlace = One("Sfx/ui_place.ogg"),
                wind = One("Ambience/wind.wav"), water = One("Ambience/water.wav"), fire = One("Ambience/fire.wav"),
                birds = Takes("Ambience", "bird", "wav"),
            };
            // Music: music_<mood>_<n>.ogg|mp3, with the loudness make_audio.py measured.
            string json = System.IO.File.Exists(Dir + "Music/music.json") ? System.IO.File.ReadAllText(Dir + "Music/music.json") : "";
            (AudioClip[] clips, float[] rms) Mood(string mood)
            {
                var clips = new System.Collections.Generic.List<AudioClip>();
                var rms = new System.Collections.Generic.List<float>();
                for (int i = 0; i < 16; i++)
                {
                    string stem = $"music_{mood}_{i}";
                    var c = One($"Music/{stem}.ogg") ?? One($"Music/{stem}.mp3");
                    if (c == null) break;
                    clips.Add(c);
                    var m = System.Text.RegularExpressions.Regex.Match(json, $"\"{stem}\":\\s*\\{{\\s*\"rms\":\\s*([0-9.]+)");
                    rms.Add(m.Success ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0.1f);
                }
                return (clips.ToArray(), rms.ToArray());
            }
            (b.musicCalm, b.rmsCalm) = Mood("calm");
            (b.musicTension, b.rmsTension) = Mood("tension");
            (b.musicCombat, b.rmsCombat) = Mood("combat");
            if (b.rifle.Length == 0 || b.musicCalm.Length == 0)
                Debug.LogWarning("[StarForge] Audio/ is incomplete; run python3 Tools/make_audio.py. Missing sounds fall back to synthesis.");
            return b;
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
                // The leaf piles every crown is surfaced with (Tools/make_leaf_textures.py).
                m.SetTexture("_LeafTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SFAssetPostprocessor.TextureDir + "foliage_broad.png"));
                m.SetTexture("_NeedleTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SFAssetPostprocessor.TextureDir + "foliage_needle.png"));
                m.SetFloat("_LeafNormal", 0.9f);
                // Leaf cards are seen from both sides; bark culls as usual.
                m.SetFloat("_Cull", foliage ? 0f : 2f);
                m.SetFloat("_Cutoff", 0.45f);
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
            fx.flameMaterial = Particle("FX_Flame", "flames.png", false, 0.02f, 1.9f);
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
