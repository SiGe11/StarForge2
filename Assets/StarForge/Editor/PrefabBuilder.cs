// PrefabBuilder.cs — unit definitions (ScriptableObjects), per-team prefabs and
// command-card icons, all generated from the Blender models.
//
// Definitions are only filled in when first created: once an asset exists its
// numbers belong to whoever tunes them in the Inspector, and rebuilding keeps
// them. Prefabs and icons are always regenerated.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class PrefabBuilder
    {
        public const string PrefabDir = MapBuilder.PrefabDir;
        public const string DefsDir = "Assets/StarForge/Data/Units";
        public const string IconDir = "Assets/StarForge/Art/Icons";
        public const string CatalogPath = "Assets/StarForge/Resources/UnitCatalog.asset";

        [MenuItem("StarForge/Build/2 Unit Definitions, Prefabs & Icons", priority = 2)]
        public static void Build()
        {
            SFEditorUtil.EnsureFolder(PrefabDir);
            SFEditorUtil.EnsureFolder(DefsDir);
            SFEditorUtil.EnsureFolder(IconDir);
            SFEditorUtil.EnsureFolder("Assets/StarForge/Resources");

            var defs = new List<UnitDef>
            {
                //   type               name        bld    neu    rad    hp   spd   turn  rng  dmg  cd     spl   sight bt  cost sup give key
                Def(UnitType.Worker,   "Digger",   false, false, 0.70f,  60, 6.6f, 7.0f, 2f,  5,  1.00f, 0f,   22, 12,  50, 1, 0, 'D',
                    "Harvests ore and constructs structures."),
                Def(UnitType.Trooper,  "Trooper",  false, false, 0.58f,  55, 6.0f, 9.0f, 16f, 7,  0.70f, 0f,   30, 14,  50, 1, 0, 'T',
                    "Cheap ranged infantry. Strong in numbers."),
                Def(UnitType.Mauler,   "Mauler",   false, false, 1.50f, 180, 4.1f, 2.6f, 24f, 32, 2.20f, 4.5f, 34, 30, 150, 3, 0, 'M',
                    "Heavy tank. Long-range splash artillery that outranges Sentinels."),
                Def(UnitType.Skimmer,  "Skimmer",  false, false, 0.85f,  75, 11f,  8.0f, 13f, 6,  0.50f, 0f,   38, 16,  75, 2, 0, 'K',
                    "Fast hover raider with wide sensors. Double damage against Diggers."),
                Def(UnitType.Foundry,  "Foundry",  true,  false, 5.0f, 1500, 0, 0, 0, 0, 0, 0, 32, 55, 400, 0, 10, 'F',
                    "Command hub. Trains Diggers, accepts ore, +10 supply."),
                Def(UnitType.Garrison, "Garrison", true,  false, 4.3f, 1000, 0, 0, 0, 0, 0, 0, 26, 30, 150, 0, 0, 'G',
                    "Trains Troopers and Skimmers."),
                Def(UnitType.Workshop, "Workshop", true,  false, 5.0f, 1250, 0, 0, 0, 0, 0, 0, 26, 40, 200, 0, 0, 'W',
                    "Heavy fabricator. Trains Maulers. Requires a Garrison."),
                Def(UnitType.Bunkhouse,"Bunkhouse",true,  false, 2.4f,  400, 0, 0, 0, 0, 0, 0, 20, 20, 100, 0, 8, 'B',
                    "Crew quarters. +8 supply."),
                Def(UnitType.Sentinel, "Sentinel", true,  false, 1.7f,  500, 0, 3.5f, 20f, 13, 0.85f, 0f, 28, 25, 125, 0, 0, 'N',
                    "Defensive gun turret. Requires a Garrison."),
                Def(UnitType.Ore,      "Ore Seam", false, true,  1.6f, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ' ', "Crystal ore. Diggers carry 8 per trip."),
                Def(UnitType.Boulder,  "Boulder",  false, true,  1.1f, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ' ', ""),
            };
            Find(defs, UnitType.Worker).producer = UnitType.Foundry;
            Find(defs, UnitType.Trooper).producer = UnitType.Garrison;
            Find(defs, UnitType.Skimmer).producer = UnitType.Garrison;
            Find(defs, UnitType.Mauler).producer = UnitType.Workshop;
            Find(defs, UnitType.Workshop).requires = UnitType.Garrison;
            Find(defs, UnitType.Sentinel).requires = UnitType.Garrison;
            Find(defs, UnitType.Skimmer).bonusVsWorkers = 2f;

            foreach (var d in defs)
            {
                BuildPrefabs(d);
                EditorUtility.SetDirty(d);
            }

            var catalog = SFEditorUtil.CreateOrLoadAsset<UnitCatalog>(CatalogPath);
            catalog.defs = defs.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Defs.Bind(catalog);

            RenderIcons(defs);
            AssetDatabase.SaveAssets();
            Debug.Log($"[StarForge] {defs.Count} unit definitions, prefabs and icons built");
        }

        static UnitDef Find(List<UnitDef> l, UnitType t) => l.Find(d => d.type == t);

        static UnitDef Def(UnitType t, string name, bool bld, bool neu, float rad, float hp, float spd, float turn,
                           float rng, float dmg, float cd, float spl, float sight, float bt, int cost, int sup, int give,
                           char key, string blurb)
        {
            string path = $"{DefsDir}/{t}.asset";
            var d = AssetDatabase.LoadAssetAtPath<UnitDef>(path);
            if (d != null) return d;
            d = ScriptableObject.CreateInstance<UnitDef>();
            d.type = t; d.displayName = name; d.building = bld; d.neutral = neu; d.radius = rad; d.hp = hp;
            d.speed = spd; d.turnRate = turn; d.range = rng; d.damage = dmg; d.cooldown = cd; d.splash = spl;
            d.sight = sight; d.buildTime = bt; d.cost = cost; d.supplyCost = sup; d.supplyGive = give;
            d.hotkey = key; d.blurb = blurb;
            AssetDatabase.CreateAsset(d, path);
            return d;
        }

        // ------------------------------------------------------------ prefabs
        static string ModelFor(UnitType t)
        {
            switch (t)
            {
                case UnitType.Worker: return "WORKER";
                case UnitType.Trooper: return "TROOPER";
                case UnitType.Mauler: return "MAULER_HULL";
                case UnitType.Skimmer: return "SKIMMER";
                case UnitType.Foundry: return "FOUNDRY";
                case UnitType.Garrison: return "GARRISON";
                case UnitType.Workshop: return "WORKSHOP";
                case UnitType.Bunkhouse: return "BUNKHOUSE";
                case UnitType.Sentinel: return "SENTINEL_BASE";
                case UnitType.Ore: return "ORE";
                default: return "BOULDER";
            }
        }

        static void BuildPrefabs(UnitDef d)
        {
            var meta = ModelFactory.Meta(ModelFor(d.type));
            d.visualRadius = Mathf.Min(meta.radius, d.radius * 1.6f);
            d.visualHeight = meta.height;
            if (d.type == UnitType.Mauler) d.visualHeight = 1.40f + ModelFactory.Meta("MAULER_TURRET").height;
            if (d.type == UnitType.Sentinel) d.visualHeight = 1.40f + ModelFactory.Meta("SENTINEL_HEAD").height;
            if (d.type == UnitType.Skimmer) d.visualHeight = meta.height + 0.35f;

            int teams = d.neutral ? 1 : 2;
            if (d.prefabs == null || d.prefabs.Length != 2) d.prefabs = new GameObject[2];
            for (int team = 0; team < teams; team++)
            {
                string file = d.neutral ? d.type.ToString() : $"{d.type}_P{team}";
                var root = new GameObject(d.displayName);
                var body = new GameObject("Body").transform;
                body.SetParent(root.transform, false);
                ModelFactory.Create(ModelFor(d.type), team, body);

                Transform turret = null;
                if (d.type == UnitType.Mauler || d.type == UnitType.Sentinel)
                {
                    turret = new GameObject("Turret").transform;
                    turret.SetParent(body, false);
                    turret.localPosition = new Vector3(0f, 1.40f, 0f);
                    ModelFactory.Create(d.type == UnitType.Mauler ? "MAULER_TURRET" : "SENTINEL_HEAD", team, turret);
                }

                if (d.type != UnitType.Boulder)
                {
                    var unit = root.AddComponent<Unit>();
                    unit.def = d;
                    unit.team = d.neutral ? 2 : team;
                    var view = root.AddComponent<UnitView>();
                    view.body = body;
                    view.turret = turret;
                    view.hoverHeight = d.type == UnitType.Skimmer ? 0.35f : 0f;
                    if (d.building) view.debris = BuildDebrisPrefab(d, team);
                }

                if (d.IsMobile)
                {
                    var agent = root.AddComponent<NavMeshAgent>();
                    agent.radius = d.radius;
                    agent.height = 2f;
                    agent.speed = d.speed;
                    agent.angularSpeed = 0f;
                    agent.updateRotation = false;
                }
                else if (d.building || d.type == UnitType.Ore)
                {
                    var obs = root.AddComponent<NavMeshObstacle>();
                    obs.shape = NavMeshObstacleShape.Capsule;
                    obs.radius = d.radius * 0.92f;
                    obs.height = 4f;
                    obs.center = new Vector3(0f, 2f, 0f);
                    obs.carving = true;
                }

                string path = $"{PrefabDir}/{file}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                d.prefabs[team] = prefab;
            }
            if (teams == 1) d.prefabs[1] = d.prefabs[0];
        }

        /// <summary>The structure's pre-cut chunks (SF_*_CHUNKS.fbx) as a prefab of
        /// rigid bodies, thrown apart by DebrisBurst when the structure dies.</summary>
        static GameObject BuildDebrisPrefab(UnitDef d, int team)
        {
            string model = ModelFor(d.type) + "_CHUNKS";
            if (ModelFactory.LoadSource(model) == null) return null;
            var root = ModelFactory.Create(model, team);
            root.name = d.displayName + " Debris";
            foreach (Transform chunk in root.transform)
            {
                var r = chunk.GetComponent<MeshRenderer>();
                if (r == null) continue;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                chunk.gameObject.AddComponent<BoxCollider>();   // fits the mesh bounds
                var rb = chunk.gameObject.AddComponent<Rigidbody>();
                Vector3 size = r.bounds.size;
                rb.mass = Mathf.Max(1f, size.x * size.y * size.z * 400f);
                rb.linearDamping = 0.2f;
                rb.angularDamping = 0.6f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            root.AddComponent<DebrisBurst>();
            string path = $"{PrefabDir}/Debris_{d.type}_P{team}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------ icons
        /// <summary>Renders a three-quarter portrait of each player-team model into a PNG,
        /// used by the command card and selection panel.</summary>
        static void RenderIcons(List<UnitDef> defs)
        {
            const int Res = 256;
            var stage = new GameObject("__IconStage");
            stage.transform.position = new Vector3(5000f, 0f, 5000f);
            var camGo = new GameObject("IconCam");
            camGo.transform.SetParent(stage.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.enabled = false;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;
            var lightGo = new GameObject("IconKey");
            lightGo.transform.SetParent(stage.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.4f;
            light.color = new Color(1f, 0.92f, 0.82f);
            lightGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            var fillGo = new GameObject("IconFill");
            fillGo.transform.SetParent(stage.transform, false);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.9f;
            fill.color = new Color(0.55f, 0.7f, 1f);
            fillGo.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

            var rt = new RenderTexture(Res, Res, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            try
            {
                foreach (var d in defs)
                {
                    if (d.type == UnitType.Boulder) continue;
                    var prefab = d.Prefab(0);
                    if (prefab == null) continue;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.transform.SetParent(stage.transform, false);
                    inst.transform.localRotation = Quaternion.Euler(0f, 205f, 0f);
                    foreach (var c in inst.GetComponentsInChildren<MonoBehaviour>()) c.enabled = false;

                    var b = new Bounds(inst.transform.position, Vector3.zero);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                    float size = b.extents.magnitude;
                    float dist = size / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                    camGo.transform.rotation = Quaternion.Euler(24f, 0f, 0f);
                    camGo.transform.position = b.center - camGo.transform.forward * dist;

                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    string path = $"{IconDir}/sf_ui_icon_{d.type}.png";
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    Object.DestroyImmediate(inst);
                    AssetDatabase.ImportAsset(path);
                    var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                    ti.textureType = TextureImporterType.Default;
                    ti.alphaIsTransparency = true;
                    ti.mipmapEnabled = false;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.SaveAndReimport();
                    d.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    EditorUtility.SetDirty(d);
                }
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(stage);
            }
        }
    }
}
