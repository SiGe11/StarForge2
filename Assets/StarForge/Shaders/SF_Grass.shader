// SF_Grass — instanced grass clumps drawn by GroundScatter.
//
// Blades are opaque geometry rather than alpha-tested cards: discard switches
// off the hidden-surface removal that keeps overdraw cheap on Apple's
// tile-based GPUs. Wind is one slow travelling gust shared by a whole clump. The
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
        // Where a grass fire has been (Vegetation's grid, uploaded by GroundMask):
        // 0 unburnt, ~0.55 alight, 1 burnt out. It does not heal.
        TEXTURE2D(_SF_BurntGrass); SAMPLER(sampler_SF_BurntGrass);
        float4 _SF_BurntGrassParams;
        // The match's wind and the live blast pressure fronts.
        #include "SF_Wind.hlsl"

        struct GrassAttributes
        {
            float4 positionOS : POSITION;
            float2 uv         : TEXCOORD0;   // x: blade phase, y: height along the blade
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float SFGrassHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

        float3 GrassVertex(GrassAttributes v, out half burn, out half rnd, out half far)
        {
            float3 origin = TransformObjectToWorld(float3(0, 0, 0));
            float3 wp = TransformObjectToWorld(v.positionOS.xyz);
            rnd = SFGrassHash(origin.xz);

            half4 gm = 0;
            if (_SF_GroundMaskParams.y > 0.5)
                gm = SAMPLE_TEXTURE2D_LOD(_SF_GroundMask, sampler_SF_GroundMask, origin.xz * _SF_GroundMaskParams.x, 0);
            half flatten = saturate(max(max(gm.r, gm.b * 0.8), gm.a * 0.95));
            burn = gm.g;
            if (_SF_BurntGrassParams.y > 0.5)
            {
                half gone = SAMPLE_TEXTURE2D_LOD(_SF_BurntGrass, sampler_SF_BurntGrass, origin.xz * _SF_BurntGrassParams.x, 0).r;
                // Burnt right down to stubble, and charred black.
                flatten = max(flatten, smoothstep(0.35, 0.95, gone) * 0.82);
                burn = max(burn, smoothstep(0.25, 0.8, gone));
            }

            float dist = distance(origin, _WorldSpaceCameraPos);
            // How far the clump is into the range where a blade is only a couple of
            // pixels wide: its root-to-tip contrast and back-light fade out there.
            far = smoothstep(30.0, 110.0, dist);
            half fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, dist);
            half squash = fade * (1.0 - flatten * 0.92) * (1.0 - burn * 0.55);

            float3 rel = wp - origin;
            rel.y *= squash;
            rel.xz *= lerp(1.0, 1.7, saturate((dist - 45.0) / 110.0)) * lerp(1.0, 1.35, flatten);

            // Wind is one slow gust travelling across the map along the wind's own
            // heading, the same for every blade in a clump, with a second, shorter
            // wave over it so a strong gust ripples the field rather than leaning it.
            // Per-blade flutter looked lively up close, but at RTS distance a blade is
            // two pixels wide, and a bright tip flicking across pixel centres on its
            // own rhythm made the whole field twinkle like blinking lights, even with
            // the camera still.
            float h = v.uv.y;
            float4 w = _SF_Wind.z > 0.001 ? _SF_Wind : float4(0.82, 0.57, 0.5, _Time.y);
            float2 dir = w.xy;
            float blowing = w.z;
            float t = w.w * _WindSpeed;
            float travel = dot(origin.xz, dir * 0.09);
            float gust = sin(t * 0.35 - travel) * 0.5 + 0.5;
            float ripple = sin(t * 1.15 - travel * 2.7) * 0.25 * blowing;
            float sway = (gust + ripple) * 0.9 * _WindStrength * blowing * h * h * squash * lerp(1.0, 0.08, far);
            rel.xz += dir * sway;
            rel.y -= abs(sway) * 0.25 * h;
            // A blast's pressure front lays the field over away from it and lets it
            // stand up again behind: the blades bend from the root, so the push goes
            // with the square of the height, and what bends over also goes down.
            float2 blast = SFGust(origin.xz) * (h * h * squash);
            if (dot(blast, blast) > 1e-8)
            {
                // Bend, do not stretch: the vertex keeps its distance from the root,
                // so a hard push lays the blade over instead of drawing it out long.
                float L = length(rel);
                rel.xz += blast * 1.1;
                rel.y = max(rel.y - length(blast) * 0.7, L * 0.08);
                rel *= L / max(length(rel), 1e-4);
            }
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

            // Centroid sampling. With MSAA a pixel is shaded when any of its
            // samples touches a blade, but the attributes are evaluated at the
            // pixel centre, which can lie well outside a blade seen almost edge-on.
            // Across such a sliver the height and colour extrapolate to tens of
            // times their range, and one shaded pixel blew up into an HDR spark
            // that bloom turned into a flashing white light (High and Balanced
            // only: Battery has no MSAA). Centroid keeps the evaluation inside
            // the triangle; the clamps in Frag catch what is left.
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                centroid float3 positionWS : TEXCOORD0;
                centroid half4  albedoH    : TEXCOORD1;   // rgb albedo, a height along the blade
                half   far        : TEXCOORD2;
            };

            Varyings Vert(GrassAttributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                half burn, rnd, far;
                float3 wp = GrassVertex(v, burn, rnd, far);
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.far = far;
                // Far away, every blade takes the clump's mid-tone instead of a dark
                // root and a bright tip, so there is no contrast left to shimmer.
                half h = lerp(v.uv.y, 0.55, far * 0.85);
                half3 c = lerp(_RootColor.rgb, _TipColor.rgb, h) * lerp(1.0 - _Variation, 1.0 + _Variation, rnd);
                half3 charred = lerp(half3(0.03, 0.026, 0.022), half3(0.14, 0.10, 0.06), h);
                o.albedoH = half4(lerp(c, charred, saturate(burn * 1.3)), h);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 wp = i.positionWS;
                Light L = GetMainLight(TransformWorldToShadowCoord(wp), wp, half4(1, 1, 1, 1));
                half h = saturate(i.albedoH.a);
                half3 albedo = clamp(i.albedoH.rgb, 0.0, 1.0);
                half3 v = GetWorldSpaceNormalizeViewDir(wp);
                half wrap = saturate(L.direction.y * 0.6 + 0.4);
                half back = pow(saturate(dot(-v, L.direction)), 4.0) * _Translucency * h * (1.0 - i.far);
                half ao = lerp(0.35, 1.0, h);
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 col = albedo * (L.color * L.shadowAttenuation * (wrap + back) + ambient) * ao;
                return half4(min(col, 4.0), 1.0);
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
                half burn, rnd, far;
                o.positionCS = TransformWorldToHClip(GrassVertex(v, burn, rnd, far));
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
                half burn, rnd, far;
                o.positionCS = TransformWorldToHClip(GrassVertex(v, burn, rnd, far));
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
