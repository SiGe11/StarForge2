// SF_Grass — instanced grass clumps and wildflowers drawn by GroundScatter.
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
//
// Two ways in (GroundScatter): _SF_GRASS_BUFFER reads each clump from one GPU
// buffer filled when the cover is grown (rows of its object-to-world matrix, its
// place in its chunk's thinning order, a flower's colour), so a frame uploads
// nothing; without the keyword the clump comes through Unity's instancing, as
// it always did (-sfgrassold on the player, for the A/B). In the buffer path a
// field thins out with distance: past 60 m a growing share of the clumps sink
// away, last-in-the-order first, and those left spread wider, so the far half
// of the view draws half the blades over the same ground. The colour comes in
// broad patches the terrain's turf shares (SF_Meadow.hlsl).
//
// The sun's shadow is taken per vertex: a blade is two pixels wide, and the
// filtered lookup per pixel cost more than everything else its pixel does.
// (_SF_GrassPixelShadow 1 puts it back per pixel, for the A/B.)
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
        float _SF_Rain;   // how wet a shower has left the ground, 0..1 (FXDirector.Weather)
        float _SF_GrassPixelShadow;   // 1: the sun's shadow per pixel again (A/B only)
        // The terrain's AO texture (G: the variation the meadow's colour drifts follow),
        // bound by GroundScatter when the cover is grown.
        TEXTURE2D(_SF_TerrainAO); SAMPLER(sampler_SF_TerrainAO);
        float4 _SF_TerrainAOParams;   // x: 1 / map size, y: bound
        // The match's wind and the live blast pressure fronts.
        #include "SF_Wind.hlsl"
        #include "SF_Meadow.hlsl"

        #if defined(_SF_GRASS_BUFFER)
        struct SFGrassInst
        {
            float4 r0, r1, r2;   // rows of the clump's object-to-world matrix
            float4 x;            // x place in the chunk's thinning order 0..1, y flower colour (0 none), z w spare
        };
        StructuredBuffer<SFGrassInst> _SF_GrassInst;
        float _SF_GrassBase;     // where this draw's chunk starts in the buffer (a whole number)
        #endif

        struct GrassAttributes
        {
            float4 positionOS : POSITION;
            float2 uv         : TEXCOORD0;   // x: blade phase, y: height along the blade (2: a petal, 3: a flower's eye)
            float2 side       : TEXCOORD1;   // the vertex's offset across its own blade (xz, metres)
        #if defined(_SF_GRASS_BUFFER)
            uint   inst       : SV_InstanceID;
        #else
            UNITY_VERTEX_INPUT_INSTANCE_ID
        #endif
        };

        float SFGrassHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

        // How much of a field is left at a distance (the same curve as
        // GroundScatter.KeepAt, which draws only that share of a chunk's clumps).
        float SFGrassKeep(float dist)
        {
            return 1.0 - saturate(0.5 + _SF_TuneA.z) * smoothstep(60.0, 140.0, dist);
        }

        // World position of a clump's vertex, and what its colour needs: burn (charred),
        // rnd (per clump), far (0 near .. 1 where a blade is a pixel or two), patch (the
        // meadow's dry and lush drifts), flower (the clump's flower colour, 0 for grass).
        float3 GrassVertex(GrassAttributes v, out half burn, out half rnd, out half far, out half patch, out half flower)
        {
            float order = 0.0;
            flower = 0.0;
        #if defined(_SF_GRASS_BUFFER)
            SFGrassInst gi = _SF_GrassInst[(uint)(_SF_GrassBase + 0.5) + v.inst];
            float3 origin = float3(gi.r0.w, gi.r1.w, gi.r2.w);
            order = gi.x.x;
            flower = gi.x.y;
        #else
            float3 origin = TransformObjectToWorld(float3(0, 0, 0));
        #endif
            float dist = distance(origin, _WorldSpaceCameraPos);
            // A blade is narrow, 5-8 cm, so a tuft is grass and not an agave from close
            // to; it widens across its own line as it recedes (x2.6 by 110 m), so it
            // never thins to less than a pixel and shimmers.
            float3 posOS = v.positionOS.xyz;
            posOS.xz += v.side * (1.6 * smoothstep(15.0, 110.0, dist));
        #if defined(_SF_GRASS_BUFFER)
            float4 p4 = float4(posOS, 1.0);
            float3 wp = float3(dot(gi.r0, p4), dot(gi.r1, p4), dot(gi.r2, p4));
        #else
            float3 wp = TransformObjectToWorld(posOS);
        #endif
            rnd = SFGrassHash(origin.xz);
            half vari = 0.5;
            if (_SF_TerrainAOParams.y > 0.5)
                vari = SAMPLE_TEXTURE2D_LOD(_SF_TerrainAO, sampler_SF_TerrainAO, origin.xz * _SF_TerrainAOParams.x, 0).g;
            patch = SFMeadowPatch(vari);

            half4 gm = 0;
            if (_SF_GroundMaskParams.y > 0.5)
                gm = SAMPLE_TEXTURE2D_LOD(_SF_GroundMask, sampler_SF_GroundMask, origin.xz * _SF_GroundMaskParams.x, 0);
            half flatten = saturate(max(max(gm.r, gm.b * 0.8), gm.a * 0.95));
            burn = gm.g;
            // Burnt right down: short black stubble drawn in toward its root. (Laid flat
            // and spread like trodden grass, a burnt field was a scatter of brown flakes.)
            half stubble = 0;
            if (_SF_BurntGrassParams.y > 0.5)
            {
                half gone = SAMPLE_TEXTURE2D_LOD(_SF_BurntGrass, sampler_SF_BurntGrass, origin.xz * _SF_BurntGrassParams.x, 0).r;
                stubble = smoothstep(0.35, 0.95, gone);
                burn = max(burn, smoothstep(0.25, 0.8, gone));
            }

            // How far the clump is into the range where a blade is only a couple of
            // pixels wide: its root-to-tip contrast and back-light fade out there.
            far = smoothstep(30.0, 110.0, dist);
            half fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, dist);
            // Thinning: the clumps late in their chunk's order sink away with distance.
        #if defined(_SF_GRASS_BUFFER)
            float keep = SFGrassKeep(dist);
        #else
            float keep = 1.0;   // Unity's instancing carries no order: every clump stays
        #endif
            fade *= 1.0 - smoothstep(keep, keep + 0.06, order);
            half thinned = 1.0 - keep;
            half squash = fade * (1.0 - flatten * 0.92) * (1.0 - burn * 0.55) * (1.0 - stubble * 0.72);

            float3 rel = wp - origin;
            rel.y *= squash;
            rel.xz *= lerp(1.0, 1.7, saturate((dist - 45.0) / 110.0)) * (1.0 + 0.7 * thinned)
                    * lerp(1.0, 1.35, flatten) * lerp(1.0, 0.5, stubble);
            // A flower is a few pixels of colour: past 42 m it draws in to nothing
            // rather than flicker (its heads would lie on the ground as flat discs).
            if (flower > 0.001) rel *= 1.0 - smoothstep(42.0, 70.0, dist);

            // Wind is one slow gust travelling across the map along the wind's own
            // heading, the same for every blade in a clump, with a second, shorter
            // wave over it so a strong gust ripples the field rather than leaning it.
            // Per-blade flutter looked lively up close, but at RTS distance a blade is
            // two pixels wide, and a bright tip flicking across pixel centres on its
            // own rhythm made the whole field twinkle like blinking lights, even with
            // the camera still.
            float h = min(v.uv.y, 1.0);
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
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _SF_GRASS_BUFFER
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
                centroid half2  farShadow  : TEXCOORD2;   // x far, y the sun's shadow at the vertex
            };

            Varyings Vert(GrassAttributes v)
            {
                Varyings o;
            #if !defined(_SF_GRASS_BUFFER)
                UNITY_SETUP_INSTANCE_ID(v);
            #endif
                half burn, rnd, far, patch, flower;
                float3 wp = GrassVertex(v, burn, rnd, far, patch, flower);
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                // Far away, every blade takes the clump's mid-tone instead of a dark
                // root and a bright tip, so there is no contrast left to shimmer.
                half h = lerp(min(v.uv.y, 1.0), 0.55, far * 0.85);
                half3 c = lerp(_RootColor.rgb, _TipColor.rgb, h) * lerp(1.0 - _Variation, 1.0 + _Variation, rnd);
                // Each blade a little lighter or darker than its neighbours, one in five
                // carrying a paler seed head; the whole clump in its patch's colour.
                c *= lerp(0.86, 1.14, v.uv.x);
                c *= lerp(half3(1, 1, 1), half3(1.45, 1.32, 0.95), smoothstep(0.7, 1.0, h) * step(0.8, v.uv.x) * (1.0 - far));
                c *= SFGrassPatchTint(patch) * (1.0 + _SF_TuneA.y);
                // A flower's petals (uv.y 2) and eye (3): white, yellow or violet by clump.
                if (v.uv.y > 1.5)
                {
                    half3 petal = flower < 0.34 ? half3(0.60, 0.60, 0.54)
                                : flower < 0.67 ? half3(0.68, 0.44, 0.03)
                                                : half3(0.33, 0.14, 0.62);
                    half3 eye = flower < 0.67 ? half3(0.75, 0.45, 0.02) : half3(0.85, 0.65, 0.08);
                    half3 head = v.uv.y > 2.5 ? eye : petal * lerp(0.85, 1.1, v.uv.x);
                    c = lerp(head, c, saturate(far * 1.6));
                    h = 1.0;
                }
                half3 charred = lerp(half3(0.02, 0.018, 0.016), half3(0.075, 0.058, 0.046), h);
                o.albedoH = half4(lerp(c, charred, saturate(burn * 1.3)), h);
                // The sun's shadow here, one filtered tap: a blade carries it root to tip.
                half shadow = 1.0;
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                shadow = GetMainLight(TransformWorldToShadowCoord(wp), wp, half4(1, 1, 1, 1)).shadowAttenuation;
            #endif
                o.farShadow = half2(far, shadow);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 wp = i.positionWS;
                Light L = GetMainLight();
            #if defined(_LIGHT_COOKIES)
                L.color *= SampleMainLightCookie(wp);   // the clouds' shadows
            #endif
                half shadow = saturate(i.farShadow.y);
                if (_SF_GrassPixelShadow > 0.5)
                    shadow = GetMainLight(TransformWorldToShadowCoord(wp), wp, half4(1, 1, 1, 1)).shadowAttenuation;
                half far = saturate(i.farShadow.x);
                half h = saturate(i.albedoH.a);
                half3 albedo = clamp(i.albedoH.rgb, 0.0, 1.0) * (1.0 - 0.2 * _SF_Rain);   // wet blades go darker
                half3 v = GetWorldSpaceNormalizeViewDir(wp);
                half wrap = saturate(L.direction.y * 0.6 + 0.4);
                half back = pow(saturate(dot(-v, L.direction)), 4.0) * _Translucency * h * (1.0 - far);
                half ao = lerp(0.35, 1.0, h);
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 col = albedo * (L.color * shadow * (wrap + back) + ambient) * ao;
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
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _SF_GRASS_BUFFER

            struct DepthVaryings { float4 positionCS : SV_POSITION; };

            DepthVaryings DepthVert(GrassAttributes v)
            {
                DepthVaryings o;
            #if !defined(_SF_GRASS_BUFFER)
                UNITY_SETUP_INSTANCE_ID(v);
            #endif
                half burn, rnd, far, patch, flower;
                o.positionCS = TransformWorldToHClip(GrassVertex(v, burn, rnd, far, patch, flower));
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
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _SF_GRASS_BUFFER
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct DepthVaryings { float4 positionCS : SV_POSITION; };

            DepthVaryings DepthVert(GrassAttributes v)
            {
                DepthVaryings o;
            #if !defined(_SF_GRASS_BUFFER)
                UNITY_SETUP_INSTANCE_ID(v);
            #endif
                half burn, rnd, far, patch, flower;
                o.positionCS = TransformWorldToHClip(GrassVertex(v, burn, rnd, far, patch, flower));
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
