// SFMaterialLibrary.cs — builds the materials the Blender material slots map to.
//
// The palette mirrors sf_model.MATERIALS (linear albedo, roughness, metallic,
// team weight, emissive gain). Every slot uses StarForge/Unit (SF_Unit.shader):
// surface detail comes from the original photographs as a triplanar x2 detail
// multiply around mid-grey, so the texture adds grain and panelling without
// shifting the calibrated albedo -- the same "textures are detail, not
// replacement" rule the original renderer followed -- and weathering (edge
// wear, grime, baked occlusion) is set per kind of surface: painted armour
// chips and gathers dust, steel wears a little, glow strips and crystal stay clean.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StarForge.EditorTools
{
    public static class SFMaterialLibrary
    {
        public const string MaterialDir = "Assets/StarForge/Art/Materials";
        const string Tex = SFAssetPostprocessor.TextureDir;

        // Linear-space team colours: player azure, AI crimson.
        public static readonly Color[] TeamLinear =
        {
            new Color(0.03f, 0.28f, 1.00f),
            new Color(0.95f, 0.07f, 0.03f),
        };

        struct Entry
        {
            public Color albedo; public float rough, metal, team, emis;
            public Entry(float r, float g, float b, float rough, float metal, float team, float emis)
            { albedo = new Color(r, g, b); this.rough = rough; this.metal = metal; this.team = team; this.emis = emis; }
        }

        static readonly Dictionary<string, Entry> Palette = new Dictionary<string, Entry>
        {
            { "armor",      new Entry(0.38f, 0.375f, 0.35f, 0.55f, 0.30f, 0f, 0f) },
            { "armor_lit",  new Entry(0.46f, 0.45f, 0.42f, 0.48f, 0.32f, 0f, 0f) },
            { "armor_dark", new Entry(0.17f, 0.17f, 0.165f, 0.62f, 0.25f, 0f, 0f) },
            { "dark",       new Entry(0.055f, 0.058f, 0.065f, 0.88f, 0.05f, 0f, 0f) },
            { "steel",      new Entry(0.26f, 0.27f, 0.29f, 0.38f, 0.85f, 0f, 0f) },
            { "rubber",     new Entry(0.035f, 0.035f, 0.038f, 0.95f, 0f, 0f, 0f) },
            { "team",       new Entry(0.42f, 0.41f, 0.38f, 0.50f, 0.20f, 1f, 0f) },
            { "team_dark",  new Entry(0.20f, 0.20f, 0.19f, 0.58f, 0.25f, 1f, 0f) },
            { "rust",       new Entry(0.20f, 0.135f, 0.085f, 0.80f, 0.10f, 0f, 0f) },
            { "glow_warm",  new Entry(1.00f, 0.82f, 0.45f, 0.25f, 0f, 0f, 2.6f) },
            { "glow_dim",   new Entry(1.00f, 0.80f, 0.42f, 0.30f, 0f, 0f, 1.4f) },
            { "glow_cyan",  new Entry(0.30f, 0.82f, 1.00f, 0.18f, 0f, 0f, 2.4f) },
            { "glow_amber", new Entry(0.92f, 0.38f, 0.14f, 0.30f, 0f, 0f, 2.0f) },
            { "crystal",    new Entry(0.32f, 0.78f, 0.95f, 0.16f, 0.15f, 0f, 0.55f) },
            { "rock",       new Entry(0.19f, 0.18f, 0.17f, 0.92f, 0f, 0f, 0f) },
            { "shadowed",   new Entry(0.10f, 0.10f, 0.10f, 0.85f, 0.10f, 0f, 0f) },
        };

        public static bool IsTeamSlot(string slot) => Palette.TryGetValue(slot, out var e) && e.team > 0f;

        public static string PathFor(string slot, int team) =>
            IsTeamSlot(slot) ? $"{MaterialDir}/SF_{slot}_T{team}.mat" : $"{MaterialDir}/SF_{slot}.mat";

        /// <summary>Material for a Blender slot name; team slots resolve per team.</summary>
        public static Material ForSlot(string slot, int team)
        {
            string p = PathFor(Palette.ContainsKey(slot) ? slot : "armor", team);
            return AssetDatabase.LoadAssetAtPath<Material>(p);
        }

        [MenuItem("StarForge/Build/1 Materials", priority = 1)]
        public static void Build()
        {
            SFEditorUtil.EnsureFolder(MaterialDir);
            var unitShader = Shader.Find("StarForge/Unit");
            var armor = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "armor.png");
            var armorN = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "armor_n.png");
            var cliff = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "cliff.png");
            var cliffN = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "cliff_n.png");
            var crystal = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "crystal.jpg");

            foreach (var kv in Palette)
            {
                string slot = kv.Key;
                Entry e = kv.Value;
                Texture2D det = armor, detN = armorN;
                float detScale = 0.75f, detNormal = 0.6f, tiling = 1f;
                if (slot == "rock") { det = cliff; detN = cliffN; detScale = 0.9f; detNormal = 1.0f; tiling = 0.6f; }
                else if (slot == "crystal") { det = crystal; detN = null; detScale = 0.5f; }
                else if (e.emis >= 0.5f || slot == "rubber" || slot == "dark") { det = null; detN = null; }

                int teams = e.team > 0f ? 2 : 1;
                for (int t = 0; t < teams; t++)
                {
                    var m = SFEditorUtil.CreateOrLoadMaterial(PathFor(slot, t), unitShader);
                    Color albedo = e.albedo;
                    Color emission = Color.black;
                    if (e.team > 0f)
                    {
                        albedo = Color.Lerp(e.albedo, TeamLinear[t] * (slot == "team_dark" ? 0.45f : 0.8f), 0.88f);
                        emission = TeamLinear[t] * (slot == "team_dark" ? 0.05f : 0.16f);
                    }
                    // ACES rolls bright near-white colours toward white, so the
                    // crystal glows a saturated blue rather than its pale albedo,
                    // and glow strips sit just past the bloom threshold.
                    if (e.emis > 0f)
                        emission = slot == "crystal" ? new Color(0.06f, 0.50f, 1.0f) * 1.1f : e.albedo * e.emis * 1.1f;
                    SetupUnit(m, slot, albedo, e.rough, e.metal, emission, det, detN, detScale, detNormal, tiling);
                    EditorUtility.SetDirty(m);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[StarForge] materials built in {MaterialDir}");
        }

        static void SetupUnit(Material m, string slot, Color linearAlbedo, float rough, float metal, Color linearEmission,
                              Texture2D detail, Texture2D detailNormal, float detailScale, float detailNormalScale,
                              float tiling)
        {
            // Material colours are authored in gamma space; convert so the
            // shader receives the calibrated linear values.
            m.SetColor("_BaseColor", linearAlbedo.gamma);
            m.SetFloat("_Smoothness", 1f - rough);
            m.SetFloat("_Metallic", metal);

            m.SetTexture("_DetailAlbedoMap", detail);
            m.SetFloat("_DetailAlbedoMapScale", detail != null ? detailScale : 0f);
            m.SetTexture("_DetailNormalMap", detailNormal);
            m.SetFloat("_DetailNormalMapScale", detailNormal != null ? detailNormalScale : 0f);
            // One repeat every two metres, as the box-projected UVs the URP detail
            // maps used before.
            m.SetFloat("_DetailTiling", 0.5f * tiling);

            bool painted = slot.StartsWith("armor") || slot.StartsWith("team");
            bool glow = slot.StartsWith("glow") || slot == "crystal";
            m.SetFloat("_WearAmount", painted ? 0.7f : slot == "steel" ? 0.4f : 0f);
            // Enough grime to ground the machine, not enough to swallow its value
            // range: with the baked occlusion on top, heavier settings turned every
            // hull into a silhouette.
            m.SetFloat("_GrimeAmount", glow ? 0f : slot == "rock" ? 0.18f : slot == "rubber" || slot == "dark" ? 0.18f : 0.3f);
            m.SetColor("_GrimeColor", new Color(0.26f, 0.22f, 0.17f));
            m.SetFloat("_AOStrength", glow ? 0.15f : 0.45f);

            bool emissive = linearEmission.maxColorComponent > 1e-4f;
            m.SetColor("_EmissionColor", emissive ? linearEmission.gamma : Color.black);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                                                 : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
    }

    public static class SFEditorUtil
    {
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void EnsureParent(string assetPath) =>
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));

        public static Material CreateOrLoadMaterial(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                EnsureParent(path);
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        public static T CreateOrLoadAsset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null)
            {
                EnsureParent(path);
                a = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(a, path);
            }
            return a;
        }
    }
}
