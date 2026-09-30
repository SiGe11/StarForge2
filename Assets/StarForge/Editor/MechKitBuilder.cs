// MechKitBuilder.cs — turns the Blender Mech kit (SF_MECH_*.fbx and mechs.json,
// Tools/blender/build_mechs.py) into what the game assembles Mechs from: a prefab
// per part and team, and the MechKit asset in Resources listing them with the
// points each part carries (the waist, the frame's mounts, each weapon's muzzle,
// the leg joints at rest). Run as part of Build step 2.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class MechKitBuilder
    {
        public const string KitPath = "Assets/StarForge/Resources/MechKit.asset";
        public const string PartDir = MapBuilder.PrefabDir + "/MechParts";

        public static readonly string[] Parts =
        {
            "MECH_LEGS_BIPED", "MECH_LEGS_QUAD", "MECH_TRACKS", "MECH_HOVER",
            "MECH_FRAME_LIGHT", "MECH_FRAME_MEDIUM", "MECH_FRAME_HEAVY",
            "MECH_W_AUTOCANNON", "MECH_W_GATLING", "MECH_W_MISSILES", "MECH_W_MORTAR",
            "MECH_W_LASER", "MECH_W_FLAMER", "MECH_W_RAILGUN", "MECH_W_FLAMETOWER",
        };

        static Dictionary<string, object> meta;

        public static Dictionary<string, object> Meta()
        {
            string path = Path.Combine(SFAssetPostprocessor.ModelDir, "mechs.json");
            meta = File.Exists(path) ? (Dictionary<string, object>)SFJson.Parse(File.ReadAllText(path)) : new Dictionary<string, object>();
            return meta;
        }

        /// <summary>A named point on a model from mechs.json, if it is there.</summary>
        public static bool TryPoint(string model, string name, out Vector3 at)
        {
            at = default;
            if (meta == null) Meta();
            if (!meta.TryGetValue(model, out var m) || !(m is Dictionary<string, object> md)) return false;
            if (!md.TryGetValue("points", out var pts) || !(pts is Dictionary<string, object> pd)) return false;
            if (!pd.TryGetValue(name, out var p)) return false;
            at = SFJson.Vec(p);
            return true;
        }

        public static MechKit Build()
        {
            Meta();
            SFEditorUtil.EnsureFolder(PartDir);
            SFEditorUtil.EnsureFolder("Assets/StarForge/Resources");
            var kit = SFEditorUtil.CreateOrLoadAsset<MechKit>(KitPath);
            var list = new List<MechKit.Part>();
            foreach (var model in Parts)
            {
                if (ModelFactory.LoadSource(model) == null) { Debug.LogWarning($"[StarForge] Mech kit: SF_{model}.fbx missing"); continue; }
                var part = new MechKit.Part { model = model, prefabs = new GameObject[2] };
                for (int team = 0; team < 2; team++)
                {
                    var go = ModelFactory.Create(model, team);
                    go.name = model;
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    part.prefabs[team] = PrefabUtility.SaveAsPrefabAsset(go, $"{PartDir}/{model}_P{team}.prefab");
                    Object.DestroyImmediate(go);
                }
                if (meta.TryGetValue(model, out var m) && m is Dictionary<string, object> md)
                {
                    if (md.TryGetValue("height", out var h)) part.height = (float)(double)h;
                    if (md.TryGetValue("radius", out var rr)) part.radius = (float)(double)rr;
                    if (md.TryGetValue("points", out var pts) && pts is Dictionary<string, object> pd)
                    {
                        var names = new List<string>();
                        var at = new List<Vector3>();
                        foreach (var kv in pd) { names.Add(kv.Key); at.Add(SFJson.Vec(kv.Value)); }
                        part.pointNames = names.ToArray();
                        part.points = at.ToArray();
                    }
                }
                list.Add(part);
            }
            kit.parts = list.ToArray();
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log($"[StarForge] Mech kit: {list.Count} parts");
            return kit;
        }
    }
}
