// SF_Tree — trees and bushes, drawn GPU-instanced by VegetationRenderer.
//
// One material per surface (bark, foliage), one draw per kind. Per instance,
// _Params carries the tree's state: x how charred it is, y how hot it burns
// (glowing embers), z how much foliage has burned away, w how freely it sways
// (a felled tree is still). The models (Tools/blender/build_flora.py) carry the
// per-vertex data: colour R baked occlusion, G a random value per clump, B how
// far from the trunk's foot, which scales the wind.
//
// Foliage normals are bent toward the crown's outward direction (_CanopyCenter,
// _CanopyRadii, per kind), so a crown lights as one soft mass rather than as a
// pile of lumps, with a leafy noise in the albedo and light through the leaves
// when the camera looks toward the sun. Opaque geometry and no discard: see
// CLAUDE.md. Burned-away foliage collapses to a point, which draws nothing.
Shader "StarForge/Tree"
{
    Properties
    {
        [Toggle] _Foliage("Foliage (else bark)", Float) = 0
        _BarkColor("Bark", Color) = (0.16, 0.12, 0.09, 1)
        _LeafColor("Leaf", Color) = (0.13, 0.22, 0.07, 1)
        _LeafColor2("Leaf variation", Color) = (0.24, 0.29, 0.08, 1)
        _CanopyCenter("Crown centre (xyz), normal bend (w)", Vector) = (0, 5, 0, 0.6)
        _CanopyRadii("Crown radii", Vector) = (2.5, 2, 2.5, 0)
        _WindStrength("Wind strength", Float) = 0.12
        _Translucency("Back-light translucency", Range(0, 2)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half _Foliage;
            half4 _BarkColor;
            half4 _LeafColor;
            half4 _LeafColor2;
            float4 _CanopyCenter;
            float4 _CanopyRadii;
            float _WindStrength;
            half _Translucency;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(TreeProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Params)   // x char, y embers, z foliage lost, w sway
        UNITY_INSTANCING_BUFFER_END(TreeProps)

        struct TreeAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float4 TreeParams()
        {
        #if defined(UNITY_INSTANCING_ENABLED)
            return UNITY_ACCESS_INSTANCED_PROP(TreeProps, _Params);
        #else
            return float4(0, 0, 0, 1);
        #endif
        }

        // Object-space position after foliage loss, and the world position after wind.
        float3 TreeVertex(TreeAttributes v, float4 prm, out float3 posOS, out float3 normalOS)
        {
            posOS = v.positionOS.xyz;
            normalOS = v.normalOS;
            if (_Foliage > 0.5)
            {
                float3 c = _CanopyCenter.xyz;
                float3 rel = posOS - c;
                // Burning foliage shrinks toward the branches and is gone at 1.
                float keep = saturate(1.0 - prm.z);
                keep *= keep;
                posOS = c + rel * keep;
                float3 outward = normalize(rel / max(_CanopyRadii.xyz, 0.1) + 1e-5);
                normalOS = normalize(lerp(normalOS, outward, _CanopyCenter.w));
            }
            float3 wp = TransformObjectToWorld(posOS);
            float3 origin = TransformObjectToWorld(float3(0, 0, 0));
            float t = _Time.y;
            float phase = dot(origin.xz, float2(0.071, 0.047));
            // A slow gust travelling across the map and a quicker flutter per clump;
            // both scale with the vertex's distance from the foot of the trunk.
            float gust = sin(t * 0.8 - phase * 0.6) * 0.65 + 0.35;
            float flutter = sin(t * 2.1 + phase * 3.0 + v.color.g * 6.2831) * 0.25;
            float sway = (gust + flutter) * _WindStrength * v.color.b * prm.w;
            wp.xz += float2(0.82, 0.57) * sway * 1.6;
            wp.y -= abs(sway) * 0.3 * v.color.b;
            return wp;
        }

        float TreeHash3(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float TreeNoise3(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(TreeHash3(i), TreeHash3(i + float3(1, 0, 0)), f.x),
                             lerp(TreeHash3(i + float3(0, 1, 0)), TreeHash3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(TreeHash3(i + float3(0, 0, 1)), TreeHash3(i + float3(1, 0, 1)), f.x),
                             lerp(TreeHash3(i + float3(0, 1, 1)), TreeHash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half4  color      : TEXCOORD3;
                half4  prm        : TEXCOORD4;
                half   fog        : TEXCOORD5;
                half   rnd        : TEXCOORD6;
            };

            Varyings Vert(TreeAttributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float4 prm = TreeParams();
                float3 posOS, nOS;
                float3 wp = TreeVertex(v, prm, posOS, nOS);
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.normalWS = TransformObjectToWorldNormal(nOS);
                o.positionOS = posOS;
                o.color = v.color;
                o.prm = prm;
                o.fog = ComputeFogFactor(o.positionCS.z);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.rnd = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 p = i.positionOS;
                half ao = lerp(0.35, 1.0, i.color.r);
                half charAmt = saturate(i.prm.x);
                half embers = saturate(i.prm.y);
                half3 albedo;
                half smooth;
                half leafMask = 0;

                if (_Foliage > 0.5)
                {
                    // Leafy mottling: clusters of lit leaves with darker gaps between.
                    half n1 = TreeNoise3(p * 1.7 + i.rnd * 13.0);
                    half n2 = TreeNoise3(p * 3.1 - i.rnd * 7.0);
                    half leafy = smoothstep(0.2, 0.8, n1 * 0.65 + n2 * 0.35);
                    half3 c = lerp(_LeafColor.rgb, _LeafColor2.rgb, saturate(i.color.g * 0.7 + i.rnd * 0.5));
                    // The underside and the inside of the crown are in their own shade,
                    // cooler and bluer; the sunlit top is warmer and yellower.
                    half height = saturate((p.y - (_CanopyCenter.y - _CanopyRadii.y)) / max(2.0 * _CanopyRadii.y, 0.1));
                    c = lerp(c * half3(0.8, 0.92, 1.08), c * half3(1.15, 1.08, 0.78), height);
                    albedo = c * (0.5 + 0.85 * leafy);
                    ao *= lerp(0.55, 1.0, height) * lerp(0.7, 1.0, smoothstep(0.3, 0.6, leafy));
                    smooth = 0.18;
                    leafMask = 1;
                }
                else
                {
                    // Bark: vertical streaks and a little knot noise.
                    half streak = TreeNoise3(float3(p.x * 9.0, p.y * 0.8, p.z * 9.0));
                    half knot = TreeNoise3(p * 2.3);
                    albedo = _BarkColor.rgb * (0.7 + 0.45 * streak) * (0.85 + 0.3 * knot);
                    smooth = 0.08;
                }

                // Charring creeps in in patches (foliage browns and blackens faster
                // than bark); while it burns, embers glow in thin cracks of the char.
                half cn = TreeNoise3(p * 2.7 + 3.1);
                half charIn = saturate(charAmt * (leafMask > 0.5 ? 2.2 : 1.15));
                half charred = smoothstep(1.0 - charIn, 1.0 - charIn + 0.2, cn * 0.6 + 0.4 * (1.0 - i.color.b));
                charred = max(charred, charIn * charIn);
                half3 scorched = leafMask > 0.5 ? half3(0.05, 0.035, 0.02) : half3(0.025, 0.022, 0.02);
                albedo = lerp(albedo, scorched, charred * 0.92);
                half crack = saturate(1.0 - abs(TreeNoise3(p * 5.3) - 0.5) * 16.0);
                half flicker = 0.7 + 0.3 * sin(_Time.y * 9.0 + p.y * 3.0 + i.rnd * 20.0);
                half3 emission = half3(1.0, 0.33, 0.07) * embers * flicker * crack * charred * 1.6;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = v;
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fog;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.smoothness = smooth;
                s.occlusion = ao;
                s.emission = emission;
                s.normalTS = half3(0, 0, 1);
                s.alpha = 1.0;

                half4 col = UniversalFragmentPBR(inputData, s);

                // Sunlight through the leaves, seen looking toward the sun.
                if (leafMask > 0.5)
                {
                    Light L = GetMainLight(inputData.shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                    half back = pow(saturate(dot(-v, L.direction)), 3.0) * _Translucency;
                    half wrap = saturate(dot(n, L.direction) * 0.5 + 0.5) * 0.08;
                    col.rgb += albedo * L.color * L.shadowAttenuation * (back + wrap) * (1.0 - charred);
                }
                col.rgb = MixFog(col.rgb, i.fog);
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
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 ShadowVert(TreeAttributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posOS, nOS;
                float3 wp = TreeVertex(v, TreeParams(), posOS, nOS);
                float3 nWS = TransformObjectToWorldNormal(nOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - wp);
            #else
                float3 lightDir = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(wp, nWS, lightDir)));
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthVaryings { float4 positionCS : SV_POSITION; };

            DepthVaryings DepthVert(TreeAttributes v)
            {
                DepthVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posOS, nOS;
                o.positionCS = TransformWorldToHClip(TreeVertex(v, TreeParams(), posOS, nOS));
                return o;
            }

            half DepthFrag(DepthVaryings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct NormalsVaryings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            NormalsVaryings NormalsVert(TreeAttributes v)
            {
                NormalsVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posOS, nOS;
                o.positionCS = TransformWorldToHClip(TreeVertex(v, TreeParams(), posOS, nOS));
                o.normalWS = TransformObjectToWorldNormal(nOS);
                return o;
            }

            half4 NormalsFrag(NormalsVaryings i) : SV_Target
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normalize(i.normalWS));
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
            #else
                return half4(normalize(i.normalWS), 0.0);
            #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
