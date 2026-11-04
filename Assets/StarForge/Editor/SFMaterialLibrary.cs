// SFMaterialLibrary.cs — builds the materials the Blender material slots map to.
//
// The palette mirrors sf_model.MATERIALS (linear albedo, roughness, metallic,
// team weight, emissive gain). Every slot uses StarForge/Unit (SF_Unit.shader):
// surface detail is the generated hull plating (Tools/make_panel_texture.py) as
// a triplanar x2 detail multiply around its own average, so it adds panelling without
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
            // Dark glass: the colour is all in the glow it shows when loaded.
            { "ore_glow",   new Entry(0.03f, 0.07f, 0.09f, 0.15f, 0f, 0f, 3.0f) },
            { "rock",       new Entry(0.19f, 0.18f, 0.17f, 0.92f, 0f, 0f, 0f) },
            { "shadowed",   new Entry(0.10f, 0.10f, 0.10f, 0.85f, 0.10f, 0f, 0f) },
            // The Biolab's vats (Tools/blender/build_roster.py).
            { "glow_bio",   new Entry(0.45f, 1.00f, 0.35f, 0.20f, 0f, 0f, 2.2f) },
        };

        /// <summary>The slot a model names to draw every face with one material: its colour G
        /// says which palette slot a face is (Tools/blender/export_fbx.py merge_palette; the rigged
        /// soldier's build_soldier_rig.py merge_materials).</summary>
        public const string PaletteSlot = "palette";

        // sf_model.MAT_NAMES, whose indices the model writes. Flora's slots are never drawn with
        // SF_Unit, and rock, crystal and ore glass need their own material: all four stay black here.
        static readonly string[] PaletteOrder =
        {
            "armor", "armor_lit", "armor_dark", "dark", "steel", "rubber", "team", "team_dark", "rust",
            "glow_warm", "glow_dim", "glow_cyan", "glow_amber", "crystal", "ore_glow", "rock",
            "bark", "leaf", "needle", "shadowed", "glow_bio",
        };

        public static bool IsTeamSlot(string slot) =>
            slot == PaletteSlot || Palette.TryGetValue(slot, out var e) && e.team > 0f;

        public static string PathFor(string slot, int team) =>
            IsTeamSlot(slot) ? $"{MaterialDir}/SF_{slot}_T{team}.mat" : $"{MaterialDir}/SF_{slot}.mat";

        /// <summary>Material for a Blender slot name; team slots resolve per team, and
        /// scan_&lt;asset&gt; slots (Tools/blender/build_rocks.py) to that scan's rock material.</summary>
        public static Material ForSlot(string slot, int team)
        {
            if (slot.StartsWith("scan_"))
                return AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/SF_{slot}.mat");
            string p = PathFor(slot == PaletteSlot || Palette.ContainsKey(slot) ? slot : "armor", team);
            return AssetDatabase.LoadAssetAtPath<Material>(p);
        }

        /// <summary>One StarForge/Rock material per scanned rock set in Textures/Rocks
        /// (&lt;asset&gt;_col.jpg and &lt;asset&gt;_nrm.jpg). The sets were shot under different
        /// skies; the tints pull them toward the terrain's cliff rock.</summary>
        static void BuildScanMaterials()
        {
            var shader = Shader.Find("StarForge/Rock");
            var tints = new Dictionary<string, Color>
            {
                { "rock_moss_set_02", new Color(1.08f, 1.06f, 1.0f) },
                { "rock_moss_set_01", new Color(0.95f, 0.97f, 1.0f) },
                { "boulder_01", new Color(0.92f, 0.95f, 1.0f) },
            };
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Tex + "Rocks" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                if (!file.EndsWith("_col")) continue;
                string aid = file.Substring(0, file.Length - 4);
                var m = SFEditorUtil.CreateOrLoadMaterial($"{MaterialDir}/SF_scan_{aid}.mat", shader);
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}Rocks/{aid}_nrm.jpg"));
                m.SetColor("_Tint", tints.TryGetValue(aid, out var t) ? t : Color.white);
                m.SetFloat("_Smoothness", 0.16f);
                m.SetColor("_DustColor", new Color(0.30f, 0.26f, 0.21f));
                m.enableInstancing = false;
                EditorUtility.SetDirty(m);
            }
        }

        [MenuItem("StarForge/Build/1 Materials", priority = 1)]
        public static void Build()
        {
            SFEditorUtil.EnsureFolder(MaterialDir);
            var unitShader = Shader.Find("StarForge/Unit");
            var armor = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "armor.png");
            var armorN = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "armor_n.png");
            // Rock (the ore's bed, the procedural set dressing) takes its grain from
            // the terrain's cliff scan, so it matches the cliffs around it.
            var cliff = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "Terrain/cliff_ch.png");
            var cliffN = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "Terrain/cliff_nrm.jpg");
            var crystal = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "crystal.jpg");

            foreach (var kv in Palette)
            {
                string slot = kv.Key;
                Entry e = kv.Value;
                Texture2D det = armor, detN = armorN;
                float detScale = DetailScale, detNormal = DetailNormalScale, tiling = 1f;
                if (slot == "rock") { det = cliff; detN = cliffN; detScale = 0.9f; detNormal = 1.0f; tiling = 0.6f; }
                else if (slot == "crystal") { det = crystal; detN = null; detScale = 0.5f; }
                else if (!TakesPanelDetail(slot, e)) { det = null; detN = null; }

                int teams = e.team > 0f ? 2 : 1;
                for (int t = 0; t < teams; t++)
                {
                    var m = SFEditorUtil.CreateOrLoadMaterial(PathFor(slot, t), unitShader);
                    SurfaceColours(slot, e, t, out Color albedo, out Color emission);
                    SetupUnit(m, slot, albedo, e.rough, e.metal, emission, det, detN, detScale, detNormal, tiling);
                    // Ore glows from inside (SF_Unit's crystal path, which also
                    // uses the rim colour as its edge light); ore glass follows
                    // the load a Digger carries and is dark on its own.
                    m.SetFloat("_Crystal", slot == "crystal" ? 1f : 0f);
                    m.SetFloat("_OreGlowMask", slot == "ore_glow" ? 1f : 0f);
                    m.SetFloat("_OreGlow", 0f);
                    if (slot == "crystal") m.SetColor("_RimColor", new Color(0.55f, 0.3f, 1.0f) * 1.3f);
                    EditorUtility.SetDirty(m);
                }
            }
            BuildPalette(unitShader, armor, armorN);
            BuildScanMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log($"[StarForge] materials built in {MaterialDir}");
        }

        /// <summary>Each team's palette against the slot materials it stands in for, and its order against
        /// sf_model.py. Target: 0 off, the same order.</summary>
        public static string CheckPalette()
        {
            var text = File.ReadAllText("Tools/blender/sf_model.py");
            int at = text.IndexOf("MATERIALS = {", System.StringComparison.Ordinal);
            int end = text.IndexOf("\n}", at, System.StringComparison.Ordinal);
            var names = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text.Substring(at, end - at), @"^\s*'(\w+)':", System.Text.RegularExpressions.RegexOptions.Multiline))
                names.Add(m.Groups[1].Value);
            string order = string.Join(",", names) == string.Join(",", PaletteOrder) ? "same order" : "ORDER DIFFERS";
            var sb = new System.Text.StringBuilder($"{PaletteOrder.Length} slots, {order} as sf_model.py;");
            for (int t = 0; t < 2; t++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{MaterialDir}/SF_{PaletteSlot}_T{t}.asset");
                int off = 0, slots = 0;
                for (int x = 0; x < PaletteOrder.Length; x++)
                {
                    string slot = PaletteOrder[x];
                    if (!Palette.ContainsKey(slot) || slot == "crystal" || slot == "ore_glow" || slot == "rock") continue;
                    var m = ForSlot(slot, t);
                    slots++;
                    Color r0 = tex.GetPixel(x, 0), r1 = tex.GetPixel(x, 1), r2 = tex.GetPixel(x, 2);
                    Color b = m.GetColor("_BaseColor").linear, e = m.GetColor("_EmissionColor");
                    bool same(float u, float v) => Mathf.Abs(u - v) < 2e-3f;
                    if (!same(r0.r, b.r) || !same(r0.g, b.g) || !same(r0.b, b.b) || !same(r0.a, m.GetFloat("_Smoothness"))) off++;
                    else if (!same(r1.r, e.r) || !same(r1.g, e.g) || !same(r1.b, e.b) || !same(r1.a, m.GetFloat("_Metallic"))) off++;
                    else if (!same(r2.r, m.GetFloat("_WearAmount")) || !same(r2.g, m.GetFloat("_GrimeAmount")) || !same(r2.b, m.GetFloat("_AOStrength"))) off++;
                    else if ((r2.a > 0.5f) != (m.GetTexture("_DetailAlbedoMap") != null)) off++;
                }
                sb.Append($" team {t}: {off} of {slots} slots off;");
            }
            return sb.ToString();
        }

        const float DetailScale = 0.75f, DetailNormalScale = 0.6f;

        static bool TakesPanelDetail(string slot, Entry e) =>
            slot != "rock" && slot != "crystal" && e.emis < 0.5f && slot != "rubber" && slot != "dark";

        static void SurfaceColours(string slot, Entry e, int team, out Color albedo, out Color emission)
        {
            albedo = e.albedo;
            emission = Color.black;
            if (e.team > 0f)
            {
                albedo = Color.Lerp(e.albedo, TeamLinear[team] * (slot == "team_dark" ? 0.45f : 0.8f), 0.88f);
                emission = TeamLinear[team] * (slot == "team_dark" ? 0.05f : 0.16f);
            }
            // ACES rolls bright near-white colours toward white, so the
            // crystal glows a saturated blue rather than its pale albedo,
            // and glow strips sit just past the bloom threshold.
            if (e.emis > 0f)
                // Ore is a spectral teal-green under a violet rim: eerie rather
                // than the plain blue it was, and apart from the team colours.
                emission = slot == "crystal" ? new Color(0.02f, 0.62f, 0.5f) * 3.6f
                         : slot == "ore_glow" ? new Color(0.03f, 0.62f, 0.5f) * 8f
                         : e.albedo * e.emis * 1.1f;
        }

        static void Weathering(string slot, out float wear, out float grime, out float ao)
        {
            bool painted = slot.StartsWith("armor") || slot.StartsWith("team");
            bool glow = slot.StartsWith("glow") || slot == "crystal" || slot == "ore_glow";
            wear = painted ? 0.7f : slot == "steel" ? 0.4f : 0f;
            // Enough grime to ground the machine, not enough to swallow its value
            // range: with the baked occlusion on top, heavier settings turned every
            // hull into a silhouette.
            grime = glow ? 0f : slot == "rock" ? 0.18f : slot == "rubber" || slot == "dark" ? 0.18f : 0.3f;
            ao = glow ? 0.15f : 0.45f;
        }

        /// <summary>One material per team that draws a whole model in one call, a texel per slot:
        /// each slot's own material's numbers, in rows SF_Unit reads when _UsePalette is on (what the
        /// shader would receive from that material). Only panel detail is shared, on or off.</summary>
        static void BuildPalette(Shader unitShader, Texture2D armor, Texture2D armorN)
        {
            for (int t = 0; t < 2; t++)
            {
                string path = $"{MaterialDir}/SF_{PaletteSlot}_T{t}.asset";
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null || tex.width != PaletteOrder.Length)
                {
                    if (tex != null) AssetDatabase.DeleteAsset(path);
                    tex = new Texture2D(PaletteOrder.Length, 3, TextureFormat.RGBAHalf, false, true);
                    AssetDatabase.CreateAsset(tex, path);
                }
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                for (int x = 0; x < PaletteOrder.Length; x++)
                {
                    string slot = PaletteOrder[x];
                    bool usable = Palette.TryGetValue(slot, out Entry e) && slot != "crystal" && slot != "ore_glow" && slot != "rock";
                    if (!usable) { for (int y = 0; y < 3; y++) tex.SetPixel(x, y, Color.clear); continue; }
                    SurfaceColours(slot, e, t, out Color albedo, out Color emission);
                    Weathering(slot, out float wear, out float grime, out float ao);
                    tex.SetPixel(x, 0, new Color(albedo.r, albedo.g, albedo.b, 1f - e.rough));
                    // An [HDR] colour reaches the shader as it was set, unconverted (SetupUnit sets .gamma).
                    Color glow = emission.maxColorComponent > 1e-4f ? emission.gamma : Color.black;
                    tex.SetPixel(x, 1, new Color(glow.r, glow.g, glow.b, e.metal));
                    tex.SetPixel(x, 2, new Color(wear, grime, ao, TakesPanelDetail(slot, e) ? 1f : 0f));
                }
                tex.Apply(false, false);
                EditorUtility.SetDirty(tex);

                var m = SFEditorUtil.CreateOrLoadMaterial(PathFor(PaletteSlot, t), unitShader);
                SetupUnit(m, "armor", Color.white, 0f, 0f, Color.black, armor, armorN, DetailScale, DetailNormalScale, 1f);
                m.SetFloat("_Crystal", 0f);
                m.SetFloat("_OreGlowMask", 0f);
                m.SetFloat("_UsePalette", 1f);
                m.SetTexture("_PaletteMap", tex);
                EditorUtility.SetDirty(m);
            }
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

            Weathering(slot, out float wear, out float grime, out float ao);
            m.SetFloat("_WearAmount", wear);
            m.SetFloat("_GrimeAmount", grime);
            m.SetColor("_GrimeColor", new Color(0.26f, 0.22f, 0.17f));
            m.SetFloat("_AOStrength", ao);

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
