// ModelSheet.cs -- fixed portraits of every model SF_Unit draws, on both teams, for comparing a
// re-export with the one before it (Tools/diff_shots.py). Nothing moves between two sheets, unlike
// the gallery, and every model is in it. Plain from two sides; then hurt and flashing, and half
// built, through the property block UnitView writes. Ore rock and boulders are the control.
// Editor only, edit mode: Tools/editor.sh call StarForge.EditorTools.ModelSheet.SheetA (B, C).
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StarForge.EditorTools
{
    public static class ModelSheet
    {
        const int Res = 384;
        const string Root = "Builds/merge/sheet";
        static readonly int DamageId = Shader.PropertyToID("_Damage");
        static readonly int FlashId = Shader.PropertyToID("_FlashColor");
        static readonly int BuildLevelId = Shader.PropertyToID("_BuildLevel");
        static readonly int TeamGlowId = Shader.PropertyToID("_TeamGlow");

        public static string SheetA() => Shoot("a");
        public static string SheetB() => Shoot("b");
        public static string SheetC() => Shoot("c");

        static bool Wanted(string model)
        {
            if (model.EndsWith("_LOD1") || model.EndsWith("_RIG") || model.StartsWith("SCAN_")) return false;
            return !model.StartsWith("TREE_") && !model.StartsWith("BUSH") && model != "FERN" && model != "REEDS";
        }

        static string Shoot(string label)
        {
            if (EditorApplication.isPlaying) return "edit mode only";
            string dir = $"{Root}/{label}";
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            double start = EditorApplication.timeSinceStartup;

            var stage = new GameObject("__ModelSheet") { hideFlags = HideFlags.DontSave };
            stage.transform.position = new Vector3(5000f, 0f, 5000f);
            var camGo = new GameObject("Cam");
            camGo.transform.SetParent(stage.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.enabled = false;
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            // Only these two, and dim: clipped to white, a surface's colour cannot be compared.
            var sceneLights = new List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.enabled) { l.enabled = false; sceneLights.Add(l); }
            Light(stage, "Key", 1.0f, new Color(1f, 0.92f, 0.82f), Quaternion.Euler(40f, -35f, 0f));
            Light(stage, "Fill", 0.35f, new Color(0.55f, 0.7f, 1f), Quaternion.Euler(20f, 150f, 0f));
            var rt = new RenderTexture(Res, Res, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
            // Crystal breathes with the clock, and the merge leaves it as it was.
            var crystal = SFMaterialLibrary.ForSlot("crystal", 0);
            var still = SFMaterialLibrary.ForSlot("dark", 0);
            var mpb = new MaterialPropertyBlock();

            var models = new List<string>();
            foreach (var file in Directory.GetFiles(SFAssetPostprocessor.ModelDir, "SF_*.fbx"))
            {
                string model = Path.GetFileNameWithoutExtension(file).Substring(3);
                if (Wanted(model)) models.Add(model);
            }
            models.Sort(System.StringComparer.Ordinal);

            int shots = 0, draws = 0, renderers = 0;
            try
            {
                foreach (string model in models)
                    for (int team = 0; team < 2; team++)
                    {
                        var go = ModelFactory.Create(model, team, stage.transform);
                        var rs = go.GetComponentsInChildren<Renderer>();
                        foreach (var r in rs)
                        {
                            var mats = r.sharedMaterials;
                            for (int i = 0; i < mats.Length; i++) if (mats[i] == crystal) mats[i] = still;
                            r.sharedMaterials = mats;
                            if (team == 0) { renderers++; draws += mats.Length; }
                        }
                        Color glow = (team == 0 ? new Color(0.15f, 0.55f, 1f) : new Color(1f, 0.25f, 0.12f)) * 1.5f;
                        void Take(string state, float yaw)
                        {
                            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                            var b = new Bounds(go.transform.position, Vector3.zero);
                            foreach (var r in rs) b.Encapsulate(r.bounds);
                            float dist = b.extents.magnitude / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                            camGo.transform.rotation = Quaternion.Euler(24f, 0f, 0f);
                            camGo.transform.position = b.center - camGo.transform.forward * dist;
                            cam.Render();
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            tex.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
                            RenderTexture.active = prev;
                            File.WriteAllBytes($"{dir}/{model}_T{team}_{state}.png", tex.EncodeToPNG());
                            shots++;
                        }
                        Take("front", 205f);
                        Take("back", 25f);
                        foreach (var r in rs)
                        {
                            mpb.Clear();
                            mpb.SetFloat(DamageId, 0.4f);
                            mpb.SetColor(FlashId, new Color(1f, 0.55f, 0.3f) * 0.3f);
                            r.SetPropertyBlock(mpb);
                        }
                        Take("hurt", 205f);
                        var whole = new Bounds(go.transform.position, Vector3.zero);
                        foreach (var r in rs) whole.Encapsulate(r.bounds);
                        foreach (var r in rs)
                        {
                            mpb.Clear();
                            mpb.SetFloat(BuildLevelId, r.transform.InverseTransformPoint(whole.center).y);
                            mpb.SetColor(TeamGlowId, glow);
                            r.SetPropertyBlock(mpb);
                        }
                        Take("building", 205f);
                        Object.DestroyImmediate(go);
                    }
            }
            finally
            {
                foreach (var l in sceneLights) if (l != null) l.enabled = true;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(stage);
            }
            return $"{shots} shots of {models.Count} models in {dir}: {renderers} renderers, {draws} draws a pass in all, {EditorApplication.timeSinceStartup - start:0.0} s";
        }

        static void Light(GameObject stage, string name, float intensity, Color colour, Quaternion turn)
        {
            var go = new GameObject(name);
            go.transform.SetParent(stage.transform, false);
            go.transform.rotation = turn;
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = colour;
        }
    }
}
