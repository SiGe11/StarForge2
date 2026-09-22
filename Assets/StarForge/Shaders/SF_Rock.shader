// SF_Rock — the scanned rocks (boulders, crags, slabs; Tools/blender/build_rocks.py).
//
// Unlike the unit shader, which invents its surface from triplanar detail
// around a flat colour, a scan carries its own: colour and normal maps on the
// scan's UVs. Two things tie it into the ground it stands on: dust settling
// on its lower flanks in the terrain's colour, and a tint and roughness shared
// across the scan sets so rocks from different sets do not look like two
// different planets. Opaque and without discard, like every StarForge shader;
// batched by the SRP batcher (the shared passes in SFPasses.hlsl are not
// instancing-aware, so the material must not enable GPU instancing).
Shader "StarForge/Rock"
{
    Properties
    {
        _BaseMap("Colour", 2D) = "grey" {}
        _BumpMap("Normal", 2D) = "bump" {}
        _NormalStrength("Normal strength", Range(0, 2)) = 1
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.18
        _DustColor("Dust colour", Color) = (0.30, 0.26, 0.21, 1)
        _DustHeight("Dust height (m)", Float) = 0.45
        _DustAmount("Dust", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _NormalStrength;
            half4 _Tint;
            half _Smoothness;
            half4 _DustColor;
            float _DustHeight;
            half _DustAmount;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  tangentWS  : TEXCOORD3;
                half4  fogLight   : TEXCOORD4;   // x fog, yzw vertex lighting
                half   heightOS   : TEXCOORD5;   // metres above the model's foot, scaled
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogLight.x = ComputeFogFactor(p.positionCS.z);
                o.fogLight.yzw = VertexLighting(p.positionWS, n.normalWS);
                // World-space height above the object's origin, which the map
                // generator puts at the ground.
                o.heightOS = p.positionWS.y - GetObjectToWorldMatrix()._m13;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _Tint.rgb;
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, i.uv), _NormalStrength);
                half3 nWS0 = normalize(i.normalWS);
                half3 bitangent = i.tangentWS.w * cross(nWS0, i.tangentWS.xyz);
                half3 nWS = normalize(TransformTangentToWorld(nTS, half3x3(i.tangentWS.xyz, bitangent, nWS0)));

                // Dust on the lower flanks, thicker where the surface faces up.
                half dust = (1.0 - smoothstep(0.0, _DustHeight, i.heightOS)) * _DustAmount;
                dust = saturate(dust * (0.7 + 0.5 * saturate(nWS.y)));
                albedo = lerp(albedo, _DustColor.rgb, dust);
                half smooth = _Smoothness * (1.0 - dust * 0.6);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogLight.x);
                inputData.vertexLighting = i.fogLight.yzw;
                inputData.bakedGI = SampleSH(nWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.smoothness = smooth;
                s.occlusion = 1.0;
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
