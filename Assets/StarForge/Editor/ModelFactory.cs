// ModelFactory.cs — instantiates Blender-exported models with URP materials.
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace StarForge.EditorTools
{
    public static class ModelFactory
    {
        public struct ModelMeta { public float radius, height; public int triangles; }

        static Dictionary<string, ModelMeta> meta;

        public static GameObject LoadSource(string model) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{SFAssetPostprocessor.ModelDir}SF_{model}.fbx");

        /// <summary>A fresh, unlinked copy of a model with every Blender material slot
        /// remapped to its URP material (team slots resolved for <paramref name="team"/>).</summary>
        public static GameObject Create(string model, int team, Transform parent = null)
        {
            var src = LoadSource(model);
            if (src == null) throw new FileNotFoundException($"model SF_{model}.fbx has not been imported");
            var go = Object.Instantiate(src, parent, false);
            go.name = model;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string slot = mats[i] != null ? mats[i].name : "armor";
                    int dot = slot.IndexOf('.');
                    if (dot > 0) slot = slot.Substring(0, dot);
                    mats[i] = SFMaterialLibrary.ForSlot(slot, team);
                }
                r.sharedMaterials = mats;
            }
            return go;
        }

        public static ModelMeta Meta(string model)
        {
            if (meta == null)
            {
                meta = new Dictionary<string, ModelMeta>();
                string path = Path.Combine(SFAssetPostprocessor.ModelDir, "models.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var rx = new Regex("\"([A-Z_]+)\":\\s*\\{\\s*\"radius\":\\s*([0-9.]+),\\s*\"height\":\\s*([0-9.]+),\\s*\"triangles\":\\s*([0-9]+)");
                    foreach (Match m in rx.Matches(json))
                        meta[m.Groups[1].Value] = new ModelMeta
                        {
                            radius = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                            height = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                            triangles = int.Parse(m.Groups[4].Value),
                        };
                }
            }
            return meta.TryGetValue(model, out var mm) ? mm : new ModelMeta { radius = 1f, height = 2f };
        }
    }
}
