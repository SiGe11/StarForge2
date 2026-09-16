// ShaderWarmup.cs — records the shader variants a real match uses and preloads
// them in the player.
//
// The first frame that shows an explosion, a hologram or a burning wreck is
// also the first time Metal is asked to compile that pipeline state, and the
// 90-second benchmark caught exactly that as a 1.4 s stall. Unity keeps a list
// of every variant it has rendered this editor session, so: play a match in the
// editor, then run this. It saves that list and registers it in Graphics
// Settings' preloaded shaders, which the built player warms up while loading.
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
            if (shaders == 0)
            {
                Debug.LogWarning("[StarForge] nothing recorded yet — play a match in the editor first, then run this");
                return;
            }
            save.Invoke(null, new object[] { CollectionPath });
            AssetDatabase.ImportAsset(CollectionPath, ImportAssetOptions.ForceUpdate);
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath);
            if (svc == null) { Debug.LogWarning("[StarForge] variant collection did not import"); return; }

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
    }
}
