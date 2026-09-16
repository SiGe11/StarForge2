// RenderSetup.cs — URP configuration tuned for the MacBook Neo (A18 Pro, 5-core
// GPU, 8 GB unified memory, 2408x1506 panel).
//
// Choices, and why:
//  * Forward+ -- explosions and muzzle flashes spawn short-lived point lights;
//    clustered lighting keeps many of them cheap.
//  * 2x MSAA -- on a tile-based GPU the MSAA attachment stays in tile memory;
//    the original measured 2x as no slower than 1x on this chip.
//  * B10G11R11 HDR -- half the bytes of RGBA16F for the bloom chain; nothing
//    reads destination alpha.
//  * Two shadow cascades at 2048 over 150 m -- the RTS camera never sees
//    farther than that at useful detail.
//  * FSR upscaling -- AdaptiveResolution lowers render scale under sustained
//    thermal load and FSR keeps the result sharp on the Retina panel.
//  * Depth texture on, opaque texture off -- fog of war and water need depth;
//    nothing needs a colour copy.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarForge.EditorTools
{
    public static class RenderSetup
    {
        public const string SettingsDir = "Assets/StarForge/Settings";
        public const string PipelinePath = SettingsDir + "/SF_URP_Neo.asset";
        public const string RendererPath = SettingsDir + "/SF_Renderer_Neo.asset";

        [MenuItem("StarForge/Build/0 Render Pipeline (MacBook Neo)", priority = 0)]
        public static void Build()
        {
            SFEditorUtil.EnsureFolder(SettingsDir);
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath) == null)
                AssetDatabase.CopyAsset("Assets/Settings/PC_RPAsset.asset", PipelinePath);
            if (AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath) == null)
                AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset", RendererPath);

            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);

            var so = new SerializedObject(rp);
            var list = so.FindProperty("m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = rd;
            so.FindProperty("m_DefaultRendererIndex").intValue = 0;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
            var softQ = so.FindProperty("m_SoftShadowQuality");
            if (softQ != null) softQ.intValue = 2; // medium
            so.ApplyModifiedPropertiesWithoutUndo();

            rp.supportsHDR = true;
            rp.hdrColorBufferPrecision = HDRColorBufferPrecision._32Bits;
            rp.msaaSampleCount = 2;
            rp.renderScale = 1f;
            // 6000.3 adds a named-upscaler field, but it is compiled out unless
            // ENABLE_UPSCALER_FRAMEWORK is defined (it is not here), and then
            // upscalerName reads back empty while the enum below is what the
            // pipeline actually uses. Checked before trusting either.
#pragma warning disable 618
            rp.upscalingFilter = UpscalingFilterSelection.FSR;
#pragma warning restore 618
            rp.supportsCameraDepthTexture = true;
            rp.supportsCameraOpaqueTexture = false;
            rp.shadowDistance = 150f;
            rp.shadowCascadeCount = 2;
            rp.cascade2Split = 0.28f;
            rp.useSRPBatcher = true;
            EditorUtility.SetDirty(rp);

            rd.renderingMode = RenderingMode.ForwardPlus;
            rd.copyDepthMode = CopyDepthMode.AfterOpaques;

            var fowMat = SFEditorUtil.CreateOrLoadMaterial(SFMaterialLibrary.MaterialDir + "/SF_FogOfWar.mat",
                                                           Shader.Find("StarForge/FogOfWar"));
            var fow = EnsureFeature<FullScreenPassRendererFeature>(rd, "FogOfWar");
            fow.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            fow.fetchColorBuffer = true;
            fow.requirements = ScriptableRenderPassInput.Depth;
            fow.passMaterial = fowMat;
            fow.passIndex = 0;
            EditorUtility.SetDirty(fow);

            // Selection rings, scorch marks and footprints are drawn by one
            // instanced depth-decal shader (SF_GroundDecal), so URP's decal
            // feature and its extra passes are not needed.

            // Screen-space ambient occlusion: contact shading where units meet the
            // ground and in every crevice. Normals are reconstructed from the depth
            // texture the fog pass already needs (no extra depth-normals pass over
            // the terrain), it runs at half resolution, and it is applied after
            // opaques as a single multiply, which suits a tile-based GPU. Radius is
            // in metres, sized for the RTS camera distance.
            var ssao = EnsureFeature<ScreenSpaceAmbientOcclusion>(rd, "SSAO");
            var ssaoSo = new SerializedObject(ssao);
            var st = ssaoSo.FindProperty("m_Settings");
            st.FindPropertyRelative("AOMethod").enumValueIndex = 0;           // blue noise
            st.FindPropertyRelative("Downsample").boolValue = true;
            st.FindPropertyRelative("AfterOpaque").boolValue = true;
            st.FindPropertyRelative("Source").enumValueIndex = 0;             // depth
            st.FindPropertyRelative("NormalSamples").enumValueIndex = 1;      // medium
            st.FindPropertyRelative("Intensity").floatValue = 1.6f;
            st.FindPropertyRelative("DirectLightingStrength").floatValue = 0.35f;
            st.FindPropertyRelative("Radius").floatValue = 0.9f;
            st.FindPropertyRelative("Samples").enumValueIndex = 1;            // 8 samples
            st.FindPropertyRelative("BlurQuality").enumValueIndex = 1;        // gaussian
            st.FindPropertyRelative("Falloff").floatValue = 260f;
            ssaoSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ssao);
            EditorUtility.SetDirty(rd);

            GraphicsSettings.defaultRenderPipeline = rp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = rp;
                QualitySettings.vSyncCount = 1;
            }
            QualitySettings.SetQualityLevel(current, false);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.productName = "StarForge";
            PlayerSettings.companyName = "StarForge";

            AssetDatabase.SaveAssets();
            Debug.Log("[StarForge] URP configured for MacBook Neo: " + PipelinePath);
        }

        static T EnsureFeature<T>(UniversalRendererData rd, string name) where T : ScriptableRendererFeature
        {
            foreach (var f in rd.rendererFeatures)
                if (f is T existing) return existing;

            var feat = ScriptableObject.CreateInstance<T>();
            feat.name = name;
            AssetDatabase.AddObjectToAsset(feat, rd);
            rd.rendererFeatures.Add(feat);
            AssetDatabase.SaveAssets();

            var so = new SerializedObject(rd);
            var map = so.FindProperty("m_RendererFeatureMap");
            if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out string _, out long id))
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return feat;
        }
    }
}
