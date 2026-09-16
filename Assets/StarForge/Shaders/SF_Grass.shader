// SF_Grass — instanced grass clumps drawn by GroundScatter.
//
// Blades are opaque geometry rather than alpha-tested cards: discard switches
// off the hidden-surface removal that keeps overdraw cheap on Apple's
// tile-based GPUs. Wind is a slow travelling gust plus per-blade flutter. The
// ground mask (GroundMask.cs) flattens grass under structures and along unit
// tracks and chars it where explosions landed. Clumps sink into the ground
// with distance instead of popping, and spread a little as they recede so
// blades never go sub-pixel and shimmer. Lit with a wrapped, up-facing normal
// so grass shades like the ground it grows from, plus back-light when the
// camera looks toward the sun.
Shader "StarForge/Grass"
{
    Properties
    {
        _RootColor("Root", Color) = (0.10, 0.15, 0.09, 1)
        _TipColor("Tip", Color) = (0.42, 0.52, 0.28, 1)
        _Variation("Colour variation", Range(0, 1)) = 0.3
        _WindStrength("Wind strength", Float) = 0.22
        _WindSpeed("Wind speed", Float) = 1.6
        _Translucency("Back-light translucency", Range(0, 2)) = 0.8
        _FadeStart("Fade start (m)", Float) = 120
        _FadeEnd("Fade end (m)", Float) = 175
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _RootColor;
            half4 _TipColor;
            half _Variation;
            float _WindStrength;
            float _WindSpeed;
            half _Translucency;
            float _FadeStart;
            float _FadeEnd;
        CBUFFER_END

        TEXTURE2D(_SF_GroundMask); SAMPLER(sampler_SF_GroundMask);
        float4 _SF_GroundMaskParams;   // x: 1 / map size, y: enabled

        struct GrassAttributes
        {
            float4 positionOS : POSITION;
            float2 uv         : TEXCOORD0;   // x: blade phase, y: height along the blade
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float SFGrassHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

        float3 GrassVertex(GrassAttributes v, out half burn, out half rnd)
        {
            float3 origin = TransformObjectToWorld(float3(0, 0, 0));
            float3 wp = TransformObjectToWorld(v.positionOS.xyz);
            rnd = SFGrassHash(origin.xz);

            half4 gm = 0;
            if (_SF_GroundMaskParams.y > 0.5)
                gm = SAMPLE_TEXTURE2D_LOD(_SF_GroundMask, sampler_SF_GroundMask, origin.xz * _SF_GroundMaskParams.x, 0);
            half flatten = saturate(max(gm.r, gm.b * 0.8));
            burn = gm.g;

            float dist = distance(origin, _WorldSpaceCameraPos);
            half fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, dist);
            half squash = fade * (1.0 - flatten * 0.92) * (1.0 - burn * 0.55);

            float3 rel = wp - origin;
            rel.y *= squash;
            rel.xz *= lerp(1.0, 1.7, saturate((dist - 45.0) / 110.0)) * lerp(1.0, 1.35, flatten);

            float h = v.uv.y;
            float t = _Time.y * _WindSpeed;
            float travel = dot(origin.xz, float2(0.071, 0.047));
            float gust = sin(t * 0.43 - travel * 0.6) * 0.5 + 0.5;
            float flutter = sin(t * 2.3 + v.uv.x * 6.2831 + travel * 3.0);
            float sway = (gust * 0.9 + flutter * 0.25) * _WindStrength * h * h * squash;
            rel.xz += float2(0.82, 0.57) * sway;
            rel.y -= abs(sway) * 0.25 * h;
            return origin + rel;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4  albedoH    : TEXCOORD1;   // rgb albedo, a height along the blade
            };

            Varyings Vert(GrassAttributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                half burn, rnd;
                float3 wp = GrassVertex(v, burn, rnd);
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                half h = v.uv.y;
                half3 c = lerp(_RootColor.rgb, _TipColor.rgb, h) * lerp(1.0 - _Variation, 1.0 + _Variation, rnd);
                half3 charred = lerp(half3(0.03, 0.026, 0.022), half3(0.14, 0.10, 0.06), h);
                o.albedoH = half4(lerp(c, charred, saturate(burn * 1.3)), h);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 wp = i.positionWS;
                Light L = GetMainLight(TransformWorldToShadowCoord(wp), wp, half4(1, 1, 1, 1));
                half h = i.albedoH.a;
                half3 v = GetWorldSpaceNormalizeViewDir(wp);
                half wrap = saturate(L.direction.y * 0.6 + 0.4);
                half back = pow(saturate(dot(-v, L.direction)), 4.0) * _Translucency * h;
                half ao = lerp(0.35, 1.0, h);
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 col = i.albedoH.rgb * (L.color * L.shadowAttenuation * (wrap + back) + ambient) * ao;
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthVaryings { float4 positionCS : SV_POSITION; };

            DepthVaryings DepthVert(GrassAttributes v)
            {
                DepthVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                half burn, rnd;
                o.positionCS = TransformWorldToHClip(GrassVertex(v, burn, rnd));
                return o;
            }

            half DepthFrag(DepthVaryings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct DepthVaryings { float4 positionCS : SV_POSITION; };

            DepthVaryings DepthVert(GrassAttributes v)
            {
                DepthVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                half burn, rnd;
                o.positionCS = TransformWorldToHClip(GrassVertex(v, burn, rnd));
                return o;
            }

            half4 NormalsFrag(DepthVaryings i) : SV_Target
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(float3(0, 1, 0));
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
            #else
                return half4(0, 1, 0, 0);
            #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
