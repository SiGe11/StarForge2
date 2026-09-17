// SF_Terrain — material template for the Unity Terrain.
//
// Why not TerrainLit: the map is terraced, so a third of what the camera sees
// is cliff face, and a planar XZ projection smears the cliff texture into
// vertical streaks. This keeps Unity's splatmap workflow (paint layers with
// the terrain tools as usual) but samples the cliff layer biplanar with world
// height as V, so its strata run along the terraces, and height-blends the
// layers on their own luminance so transitions read as gravel pushing through
// lichen rather than a soft cross-fade. Baked relief AO and a macro mottling
// texture break up the tiling at RTS distance.
//
// _SF_AUTOSPLAT takes the weights, relief occlusion and mottling from the mesh
// (vertex colour and uv2) instead of a splatmap and textures; the backdrop
// beyond the rim uses it, computed by the map builder with the terrain's rules.
Shader "StarForge/Terrain"
{
    Properties
    {
        [HideInInspector] _Control("Control", 2D) = "red" {}
        [HideInInspector] _Splat0("Layer 0", 2D) = "grey" {}
        [HideInInspector] _Splat1("Layer 1", 2D) = "grey" {}
        [HideInInspector] _Splat2("Layer 2", 2D) = "grey" {}
        [HideInInspector] _Splat3("Layer 3", 2D) = "grey" {}
        [HideInInspector] _Normal0("Normal 0", 2D) = "bump" {}
        [HideInInspector] _Normal1("Normal 1", 2D) = "bump" {}
        [HideInInspector] _Normal2("Normal 2", 2D) = "bump" {}
        [HideInInspector] _Normal3("Normal 3", 2D) = "bump" {}

        _Tint0("Layer 0 tint (lichen)", Color) = (1,1,1,1)
        _Tint1("Layer 1 tint (gravel)", Color) = (1,1,1,1)
        _Tint2("Layer 2 tint (cliff)", Color) = (1,1,1,1)
        _Tint3("Layer 3 tint (ash)", Color) = (1,1,1,1)
        _Tile("Layer tile sizes (m)", Vector) = (7, 5, 14, 4)
        _Smooth("Layer smoothness", Vector) = (0.12, 0.18, 0.22, 0.10)
        _NScale("Layer normal strength", Vector) = (0.8, 1.0, 1.2, 0.6)

        _AOTex("Baked AO (R) / variation (G)", 2D) = "white" {}
        _MacroTex("Macro mottling", 2D) = "grey" {}
        _MacroScale("Macro scale", Float) = 0.011
        _MacroStrength("Macro strength", Range(0, 1)) = 0.4
        _CliffStretch("Cliff strata stretch", Float) = 0.55
        _BlendSharpness("Height blend sharpness", Range(0.02, 1)) = 0.22
        _WaterLevel("Water level", Float) = 2.4
        [Toggle(_SF_AUTOSPLAT)] _AutoSplat("Procedural splat (backdrop)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-100" "TerrainCompatible" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _SF_AUTOSPLAT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Control);  SAMPLER(sampler_Control);
            TEXTURE2D(_Splat0);   SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1);
            TEXTURE2D(_Splat2);
            TEXTURE2D(_Splat3);
            TEXTURE2D(_Normal0);
            TEXTURE2D(_Normal1);
            TEXTURE2D(_Normal2);
            TEXTURE2D(_Normal3);
            TEXTURE2D(_AOTex);    SAMPLER(sampler_AOTex);
            TEXTURE2D(_MacroTex); SAMPLER(sampler_MacroTex);
            TEXTURE2D(_SF_GroundMask); SAMPLER(sampler_SF_GroundMask);
            float4 _SF_GroundMaskParams;   // x: 1 / map size, y: enabled (GroundMask.cs)

            float4 _Control_TexelSize;
            half4 _Tint0, _Tint1, _Tint2, _Tint3;
            float4 _Tile;
            half4 _Smooth, _NScale;
            float _MacroScale, _CliffStretch;
            half _MacroStrength, _BlendSharpness;
            float _WaterLevel;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            #if defined(_SF_AUTOSPLAT)
                // Full precision to match the mesh's float vertex colours; declared
                // half, Metal rejects the attribute type.
                float4 color      : COLOR;       // splat weights
                float2 uv2        : TEXCOORD1;   // x relief AO, y mottling
            #endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  fogLight   : TEXCOORD3;   // x fog, yzw vertex lighting
            #if defined(_SF_AUTOSPLAT)
                half4  splat      : TEXCOORD4;
                half2  aoVar      : TEXCOORD5;
            #endif
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fogLight.x = ComputeFogFactor(p.positionCS.z);
                o.fogLight.yzw = VertexLighting(p.positionWS, o.normalWS);
            #if defined(_SF_AUTOSPLAT)
                o.splat = v.color;
                o.aoVar = v.uv2;
            #endif
                return o;
            }

            half Lum(half3 c) { return dot(c, half3(0.299, 0.587, 0.114)); }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 wp = i.positionWS;
                half3 n = normalize(i.normalWS);

                half4 w;
            #if defined(_SF_AUTOSPLAT)
                w = i.splat / max(1e-3, i.splat.r + i.splat.g + i.splat.b + i.splat.a);
            #else
                float2 cuv = (i.uv * (_Control_TexelSize.zw - 1.0) + 0.5) * _Control_TexelSize.xy;
                w = SAMPLE_TEXTURE2D(_Control, sampler_Control, cuv);
            #endif

                // Planar layers, world XZ so the backdrop tiles identically.
                float2 uv0 = wp.xz / _Tile.x;
                float2 uv1 = wp.xz / _Tile.y;
                float2 uv3 = wp.xz / _Tile.w;
                half4 c0 = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, uv0);
                half4 c1 = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, uv1);
                half4 c3 = SAMPLE_TEXTURE2D(_Splat3, sampler_Splat0, uv3);

                // Cliff layer: biplanar sides with height as V, plus a top plane.
                half3 an = abs(n);
                half3 tw = an * an; tw *= tw;
                tw /= max(1e-4, tw.x + tw.y + tw.z);
                float cs = 1.0 / _Tile.z;
                float2 uvX = float2(wp.z, wp.y * _CliffStretch) * cs;
                float2 uvY = wp.xz * cs;
                float2 uvZ = float2(wp.x, wp.y * _CliffStretch) * cs;
                half4 c2 = SAMPLE_TEXTURE2D(_Splat2, sampler_Splat0, uvX) * tw.x
                         + SAMPLE_TEXTURE2D(_Splat2, sampler_Splat0, uvY) * tw.y
                         + SAMPLE_TEXTURE2D(_Splat2, sampler_Splat0, uvZ) * tw.z;

                // Height blend on each layer's own luminance.
                half4 hgt = half4(Lum(c0.rgb), Lum(c1.rgb), Lum(c2.rgb), Lum(c3.rgb));
                half4 wh = w + hgt;
                half top = max(max(wh.r, wh.g), max(wh.b, wh.a)) - _BlendSharpness;
                half4 b = max(wh - top, 0.0) * step(0.001, w);
                b /= max(1e-4, b.r + b.g + b.b + b.a);

                half3 albedo = c0.rgb * _Tint0.rgb * b.r + c1.rgb * _Tint1.rgb * b.g
                             + c2.rgb * _Tint2.rgb * b.b + c3.rgb * _Tint3.rgb * b.a;
                half smooth = dot(b, _Smooth);

                // Normals: planar layers through a world-aligned TBN...
                half3 T = normalize(cross(n, half3(0, 0, 1)));
                half3 B = cross(T, n);
                half3 nt0 = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal0, sampler_Splat0, uv0), _NScale.x);
                half3 nt1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal1, sampler_Splat0, uv1), _NScale.y);
                half3 nt3 = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal3, sampler_Splat0, uv3), _NScale.w);
                half3 ntPlanar = nt0 * b.r + nt1 * b.g + nt3 * b.a + half3(0, 0, 1) * b.b;
                half3 nPlanar = TransformTangentToWorld(ntPlanar, half3x3(T, B, n));
                // ...and the cliff with a whiteout-blended triplanar normal.
                half3 tnX = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal2, sampler_Splat0, uvX), _NScale.z);
                half3 tnY = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal2, sampler_Splat0, uvY), _NScale.z);
                half3 tnZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal2, sampler_Splat0, uvZ), _NScale.z);
                tnX = half3(tnX.xy + n.zy, abs(tnX.z) * n.x);
                tnY = half3(tnY.xy + n.xz, abs(tnY.z) * n.y);
                tnZ = half3(tnZ.xy + n.xy, abs(tnZ.z) * n.z);
                half3 nCliff = normalize(tnX.zyx * tw.x + tnY.xzy * tw.y + tnZ.xyz * tw.z);
                half3 nWS = normalize(lerp(nPlanar, nCliff, b.b));

            #if defined(_SF_AUTOSPLAT)
                half ao = lerp(0.5, 1.0, i.aoVar.x);
                half variation = i.aoVar.y;
            #else
                half4 aov = SAMPLE_TEXTURE2D(_AOTex, sampler_AOTex, i.uv);
                // SSAO handles crevices at screen scale; the baked relief AO only
                // carries the broad occlusion, and at full strength it turned pits black.
                half ao = lerp(0.5, 1.0, aov.r);
                half variation = aov.g;
            #endif
                half macro = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp.xz * _MacroScale).r;
                albedo *= lerp(1.0, macro * 2.0, _MacroStrength) * lerp(0.86, 1.12, variation);

                // Wet, darker, glossier ground just above the waterline.
                half wet = 1.0 - smoothstep(_WaterLevel, _WaterLevel + 1.2, wp.y);
                albedo *= lerp(1.0, 0.55, wet);
                smooth = lerp(smooth, 0.72, wet);

            #if !defined(_SF_AUTOSPLAT)
                // Battle damage from GroundMask: charred earth (G) and churned tracks (B).
                if (_SF_GroundMaskParams.y > 0.5)
                {
                    half4 gm = SAMPLE_TEXTURE2D(_SF_GroundMask, sampler_SF_GroundMask, wp.xz * _SF_GroundMaskParams.x);
                    albedo *= lerp(1.0, 0.45, gm.g) * lerp(1.0, 0.82, gm.b);
                    smooth *= 1.0 - gm.g * 0.6;
                    // Craters (A): a scorched, blasted centre inside a ring of lighter,
                    // freshly thrown earth, both with ragged edges from the gravel
                    // photograph so no crater is a clean circle.
                    half crater = gm.a;
                    if (crater > 0.004)
                    {
                        float2 cp = wp.xz;
                        half n = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, cp * 0.19).r * 0.6
                               + SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, cp * 0.83).r * 0.4;
                        half core = smoothstep(0.5, 0.85, crater + (n - 0.5) * 0.45);
                        half thrown = smoothstep(0.06, 0.32, crater + (n - 0.5) * 0.4) * (1.0 - core);
                        half3 soil = half3(0.21, 0.165, 0.12) * (0.75 + 0.5 * n);
                        half3 burnt = half3(0.045, 0.04, 0.035) * (0.6 + 0.9 * n);
                        albedo = lerp(albedo, soil, thrown * 0.65);
                        albedo = lerp(albedo, burnt, core * 0.88);
                        smooth *= 1.0 - max(core, thrown) * 0.7;
                        ao *= lerp(1.0, 0.85, core);
                    }
                }
            #endif

                InputData inputData = (InputData)0;
                inputData.positionWS = wp;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(wp);
                inputData.shadowCoord = TransformWorldToShadowCoord(wp);
                inputData.fogCoord = InitializeInputDataFog(float4(wp, 1.0), i.fogLight.x);
                inputData.vertexLighting = i.fogLight.yzw;
                inputData.bakedGI = SampleSH(nWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.smoothness = smooth;
                s.occlusion = ao;
                s.normalTS = half3(0, 0, 1);
                s.alpha = 1.0;

                half4 col = UniversalFragmentPBR(inputData, s);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                return half4(col.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SFShadowVert
            #pragma fragment SFShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "SFPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SFDepthVert
            #pragma fragment SFDepthFrag
            #include "SFPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SFDepthVert
            #pragma fragment SFDepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "SFPasses.hlsl"
            ENDHLSL
        }
    }
    Fallback Off
}
