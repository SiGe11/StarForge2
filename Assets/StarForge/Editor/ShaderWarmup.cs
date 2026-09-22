// ShaderWarmup.cs — records the shader variants a real match uses and preloads
// them in the player.
//
// The first frame that shows an explosion, a hologram or a burning wreck is
// also the first time Metal is asked to compile that pipeline state, and the
// 90-second benchmark caught exactly that as a 1.4 s stall. Unity keeps a list
// of every variant it has rendered this editor session, so: play a match in the
// editor, then run this. It saves that list and registers it in Graphics
// Settings' preloaded shaders, which the built player warms up while loading.
//
// Unity's list only covers what rendered since the last script reload, so a
// session with a few recompiles in it loses variants that were recorded before
// (the fire and water-ripple shaders went missing that way). A new recording is
// therefore merged into the saved one rather than replacing it.
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace StarForge.EditorTools
{
    public static class ShaderWarmup
    {
        public const string CollectionPath = RenderSetup.SettingsDir + "/SF_ShaderVariants.shadervariants";

        [MenuItem("StarForge/Build/5 Record Shader Variants (after playing)", priority = 5)]
        public static void Record()
        {
            SFEditorUtil.EnsureFolder(RenderSetup.SettingsDir);

            // The recorder is internal in Unity 6 (it is what the "Save to asset"
            // button in Graphics Settings calls), so reach it by reflection rather
            // than asking whoever rebuilds this to click through a settings page.
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var save = typeof(ShaderUtil).GetMethod("SaveCurrentShaderVariantCollection", flags);
            var shaderCount = typeof(ShaderUtil).GetMethod("GetCurrentShaderVariantCollectionShaderCount", flags);
            var variantCount = typeof(ShaderUtil).GetMethod("GetCurrentShaderVariantCollectionVariantCount", flags);
            if (save == null)
            {
                Debug.LogWarning("[StarForge] this Unity version has no shader variant recorder; skipping preload");
                return;
            }
            int shaders = shaderCount != null ? (int)shaderCount.Invoke(null, null) : -1;
            int variants = variantCount != null ? (int)variantCount.Invoke(null, null) : -1;
            var previous = ReadVariants(AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath));
            if (shaders == 0)
                // Unity's list comes back empty in some editor states (it did even
                // mid-match); keep the saved collection and add the authored variants.
                Debug.LogWarning("[StarForge] Unity recorded no variants this session; keeping the saved collection");
            else
            {
                save.Invoke(null, new object[] { CollectionPath });
                AssetDatabase.ImportAsset(CollectionPath, ImportAssetOptions.ForceUpdate);
            }
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath);
            if (svc == null) { Debug.LogWarning("[StarForge] variant collection did not import"); return; }
            AddAuthored(svc);
            int kept = 0;
            foreach (var (shader, pass, keywords) in previous)
            {
                // A variant whose keywords a shader no longer has is dropped.
                try { if (svc.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, keywords))) kept++; }
                catch (System.ArgumentException) { }
            }
            EditorUtility.SetDirty(svc);
            shaders = svc.shaderCount;
            variants = svc.variantCount;
            if (kept > 0) Debug.Log($"[StarForge] kept {kept} variants from the previous recording");

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets != null && assets.Length > 0)
            {
                var so = new SerializedObject(assets[0]);
                var list = so.FindProperty("m_PreloadedShaders");
                bool already = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == svc) already = true;
                if (!already)
                {
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = svc;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[StarForge] preloading {shaders} shaders / {variants} variants from {CollectionPath}");
        }

        /// <summary>Variants of the shaders the game draws only in particular moments (a tree
        /// burning, a splash, smoke), with the keyword sets the recorded shaders of the
        /// same kind use: High (soft shadows, cookies) and the plain one.</summary>
        static void AddAuthored(ShaderVariantCollection svc)
        {
            const UnityEngine.Rendering.PassType Srp = UnityEngine.Rendering.PassType.ScriptableRenderPipeline;
            const UnityEngine.Rendering.PassType Shadow = UnityEngine.Rendering.PassType.ShadowCaster;
            var table = new (string shader, UnityEngine.Rendering.PassType pass, string keywords)[]
            {
                ("StarForge/Tree", Srp, "INSTANCING_ON _CLUSTER_LIGHT_LOOP _LIGHT_COOKIES _MAIN_LIGHT_SHADOWS_CASCADE _SHADOWS_SOFT"),
                ("StarForge/Tree", Srp, "INSTANCING_ON _CLUSTER_LIGHT_LOOP _MAIN_LIGHT_SHADOWS_CASCADE"),
                ("StarForge/Tree", Srp, "INSTANCING_ON"),
                ("StarForge/Tree", Shadow, "INSTANCING_ON"),
                ("StarForge/Smoke", Srp, ""),
                ("StarForge/WaterRipple", Srp, "INSTANCING_ON"),
                ("StarForge/Particle", Srp, ""),
                ("StarForge/Rock", Srp, "_CLUSTER_LIGHT_LOOP _LIGHT_COOKIES _MAIN_LIGHT_SHADOWS_CASCADE _SHADOWS_SOFT"),
                ("StarForge/Rock", Srp, "_CLUSTER_LIGHT_LOOP _LIGHT_COOKIES _MAIN_LIGHT_SHADOWS_CASCADE _SCREEN_SPACE_OCCLUSION _SHADOWS_SOFT"),
                ("StarForge/Rock", Srp, "_CLUSTER_LIGHT_LOOP _MAIN_LIGHT_SHADOWS_CASCADE"),
                ("StarForge/Rock", Shadow, ""),
            };
            int added = 0;
            foreach (var (name, pass, keywords) in table)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                try
                {
                    var kw = keywords.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (svc.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, kw))) added++;
                }
                catch (System.ArgumentException e) { Debug.LogWarning($"[StarForge] variant {name} [{keywords}]: {e.Message}"); }
            }
            EditorUtility.SetDirty(svc);
            if (added > 0) Debug.Log($"[StarForge] added {added} authored variants");
        }

        static System.Collections.Generic.List<(Shader, UnityEngine.Rendering.PassType, string[])> ReadVariants(ShaderVariantCollection svc)
        {
            var list = new System.Collections.Generic.List<(Shader, UnityEngine.Rendering.PassType, string[])>();
            if (svc == null) return list;
            var so = new SerializedObject(svc);
            var shaders = so.FindProperty("m_Shaders");
            for (int i = 0; i < shaders.arraySize; i++)
            {
                var entry = shaders.GetArrayElementAtIndex(i);
                var shader = entry.FindPropertyRelative("first").objectReferenceValue as Shader;
                var vars = entry.FindPropertyRelative("second.variants");
                if (shader == null || vars == null) continue;
                for (int v = 0; v < vars.arraySize; v++)
                {
                    var variant = vars.GetArrayElementAtIndex(v);
                    string kw = variant.FindPropertyRelative("keywords").stringValue ?? "";
                    var keywords = kw.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    list.Add((shader, (UnityEngine.Rendering.PassType)variant.FindPropertyRelative("passType").intValue, keywords));
                }
            }
            return list;
        }
    }
}
