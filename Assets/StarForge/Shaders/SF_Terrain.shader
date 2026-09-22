// SF_Terrain — material template for the Unity Terrain.
//
// Why not TerrainLit: the map is terraced, so a third of what the camera sees
// is cliff face, and a planar XZ projection smears the cliff texture into
// vertical streaks. This keeps Unity's splatmap workflow (paint layers with
// the terrain tools as usual) but samples the cliff layer biplanar with world
// height as V, so its strata run along the terraces, and height-blends the
// layers on the scanned height each layer carries in its alpha
// (Tools/blender/pack_textures.py), so stones stand out of the sand and moss
// fills the hollows between them rather than the two cross-fading. The same
// height darkens the hollows, which the scans' colour maps leave unshaded.
// Baked relief AO and a macro mottling texture break up the tiling at RTS
// distance.
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

        _Tint0("Layer 0 tint (meadow)", Color) = (1,1,1,1)
        _Tint1("Layer 1 tint (dirt)", Color) = (1,1,1,1)
        _Tint2("Layer 2 tint (cliff)", Color) = (1,1,1,1)
        _Tint3("Layer 3 tint (sand)", Color) = (1,1,1,1)
        _Tile("Layer tile sizes (m)", Vector) = (7, 5, 14, 4)
        _Smooth("Layer smoothness", Vector) = (0.12, 0.18, 0.22, 0.10)
        _NScale("Layer normal strength", Vector) = (0.8, 1.0, 1.2, 0.6)
        _HeightScale("Layer height for blending", Vector) = (1, 1, 1, 1)
        _Cavity("Hollow darkening from height", Range(0, 1)) = 0.35

        _AOTex("Baked AO (R) / variation (G) / moisture (B)", 2D) = "white" {}
        _WetTint("Tint of moist hollows", Color) = (0.86, 0.95, 0.84, 1)
        _DryTint("Tint of dry ridges", Color) = (1.07, 1.03, 0.95, 1)
        _MacroNormal("Large-scale relief from the cliff scan", Range(0, 1)) = 0.35
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
            TEXTURE2D(_SF_BurntGrass); SAMPLER(sampler_SF_BurntGrass);
            float4 _SF_BurntGrassParams;   // where a grass fire has been; it does not heal

            float4 _Control_TexelSize;
            half4 _Tint0, _Tint1, _Tint2, _Tint3;
            float4 _Tile;
            half4 _Smooth, _NScale, _HeightScale;
            half _Cavity;
            half4 _WetTint, _DryTint;
            half _MacroNormal;
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

            #if defined(_SF_AUTOSPLAT)
                half ao = lerp(0.5, 1.0, i.aoVar.x);
                half variation = i.aoVar.y;
                half moisture = 0.5;
            #else
                half4 aov = SAMPLE_TEXTURE2D(_AOTex, sampler_AOTex, i.uv);
                // SSAO handles crevices at screen scale; the baked relief AO only
                // carries the broad occlusion, and at full strength it turned pits black.
                half ao = lerp(0.5, 1.0, aov.r);
                half variation = aov.g;
                half moisture = aov.b;
            #endif

                // Planar layers, world XZ so the backdrop tiles identically. Each is
                // sampled at two scales, the second rotated and 2.7x larger, which
                // swap over in broad patches (the variation noise): a single scale
                // repeating every few metres read as a grid of blotches from above.
                // Only what shows is sampled -- the layers present here, the scale in
                // use -- with gradients taken outside the branches, so mip selection
                // stays seamless: most pixels need one or two of the four layers, and
                // sampling all of them twice cost more than the rest of the frame's
                // new art together.
                float2 gx = ddx(wp.xz), gy = ddy(wp.xz);
                float2 wr = float2(wp.x * 0.8 - wp.z * 0.6, wp.x * 0.6 + wp.z * 0.8) * (1.0 / 2.7) + 0.37;
                float2 gxr = float2(gx.x * 0.8 - gx.y * 0.6, gx.x * 0.6 + gx.y * 0.8) * (1.0 / 2.7);
                float2 gyr = float2(gy.x * 0.8 - gy.y * 0.6, gy.x * 0.6 + gy.y * 0.8) * (1.0 / 2.7);
                half swap = smoothstep(0.32, 0.68, variation);
                const half on = 0.001;

                #define SF_PLANAR(TEX, TILE, OUT) \
                    { half4 a0 = 0, a1 = 0; float k = 1.0 / (TILE); \
                      if (swap < 1.0 - on) a0 = SAMPLE_TEXTURE2D_GRAD(TEX, sampler_Splat0, wp.xz * k, gx * k, gy * k); \
                      if (swap > on)       a1 = SAMPLE_TEXTURE2D_GRAD(TEX, sampler_Splat0, wr * k, gxr * k, gyr * k); \
                      OUT = lerp(a0, a1, swap); }

                half4 c0 = 0, c1 = 0, c2 = 0, c3 = 0;
                if (w.r > on) SF_PLANAR(_Splat0, _Tile.x, c0)
                if (w.g > on) SF_PLANAR(_Splat1, _Tile.y, c1)
                if (w.a > on) SF_PLANAR(_Splat3, _Tile.w, c3)

                // Cliff layer: biplanar sides with height as V, plus a top plane.
                half3 an = abs(n);
                half3 tw = an * an; tw *= tw;
                tw /= max(1e-4, tw.x + tw.y + tw.z);
                float cs = 1.0 / _Tile.z;
                float2 uvX = float2(wp.z, wp.y * _CliffStretch) * cs;
                float2 uvY = wp.xz * cs;
                float2 uvZ = float2(wp.x, wp.y * _CliffStretch) * cs;
                float2 gxX = ddx(uvX), gyX = ddy(uvX), gxZ = ddx(uvZ), gyZ = ddy(uvZ);
                if (w.b > on)
                {
                    if (tw.x > on) c2 += SAMPLE_TEXTURE2D_GRAD(_Splat2, sampler_Splat0, uvX, gxX, gyX) * tw.x;
                    if (tw.y > on) c2 += SAMPLE_TEXTURE2D_GRAD(_Splat2, sampler_Splat0, uvY, gx * cs, gy * cs) * tw.y;
                    if (tw.z > on) c2 += SAMPLE_TEXTURE2D_GRAD(_Splat2, sampler_Splat0, uvZ, gxZ, gyZ) * tw.z;
                }

                // Height blend on the scanned heights (alpha).
                half4 hgt = half4(c0.a, c1.a, c2.a, c3.a) * _HeightScale;
                half4 wh = w + hgt;
                half top = max(max(wh.r, wh.g), max(wh.b, wh.a)) - _BlendSharpness;
                half4 b = max(wh - top, 0.0) * step(on, w);
                b /= max(1e-4, b.r + b.g + b.b + b.a);

                half3 albedo = c0.rgb * _Tint0.rgb * b.r + c1.rgb * _Tint1.rgb * b.g
                             + c2.rgb * _Tint2.rgb * b.b + c3.rgb * _Tint3.rgb * b.a;
                half smooth = dot(b, _Smooth);
                half surface = dot(b, half4(c0.a, c1.a, c2.a, c3.a));

                // Normals: planar layers through a world-aligned TBN, at the same
                // scales as their colour, for the layers that won the height blend...
                half3 T = normalize(cross(n, half3(0, 0, 1)));
                half3 B = cross(T, n);
                half4 p0 = half4(0.5, 0.5, 1, 1), p1 = p0, p3 = p0;
                if (b.r > on) SF_PLANAR(_Normal0, _Tile.x, p0)
                if (b.g > on) SF_PLANAR(_Normal1, _Tile.y, p1)
                if (b.a > on) SF_PLANAR(_Normal3, _Tile.w, p3)
                half3 ntPlanar = UnpackNormalScale(p0, _NScale.x) * b.r + UnpackNormalScale(p1, _NScale.y) * b.g
                               + UnpackNormalScale(p3, _NScale.w) * b.a + half3(0, 0, 1) * b.b;
                // Folds and ribs tens of metres across, so broad slopes are not
                // smooth as dunes: the cliff scan's normals at 1/40 scale, faint.
                const float mk = 2.7 / 38.0;
                half3 ntMacro = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_Normal2, sampler_Splat0, wr * mk, gxr * mk, gyr * mk), _MacroNormal);
                ntPlanar = half3(ntPlanar.xy + ntMacro.xy * (1.0 - b.b), ntPlanar.z);
                half3 nPlanar = TransformTangentToWorld(ntPlanar, half3x3(T, B, n));
                // ...and the cliff with a whiteout-blended triplanar normal.
                half3 nWS = nPlanar;
                if (b.b > on)
                {
                    half4 nx = half4(0.5, 0.5, 1, 1), ny = nx, nz = nx;
                    if (tw.x > on) nx = SAMPLE_TEXTURE2D_GRAD(_Normal2, sampler_Splat0, uvX, gxX, gyX);
                    if (tw.y > on) ny = SAMPLE_TEXTURE2D_GRAD(_Normal2, sampler_Splat0, uvY, gx * cs, gy * cs);
                    if (tw.z > on) nz = SAMPLE_TEXTURE2D_GRAD(_Normal2, sampler_Splat0, uvZ, gxZ, gyZ);
                    half3 tnX = UnpackNormalScale(nx, _NScale.z);
                    half3 tnY = UnpackNormalScale(ny, _NScale.z);
                    half3 tnZ = UnpackNormalScale(nz, _NScale.z);
                    tnX = half3(tnX.xy + n.zy, abs(tnX.z) * n.x);
                    tnY = half3(tnY.xy + n.xz, abs(tnY.z) * n.y);
                    tnZ = half3(tnZ.xy + n.xy, abs(tnZ.z) * n.z);
                    half3 nCliff = normalize(tnX.zyx * tw.x + tnY.xzy * tw.y + tnZ.xyz * tw.z);
                    nWS = lerp(nPlanar, nCliff, b.b);
                }
                nWS = normalize(nWS);
                #undef SF_PLANAR

                ao *= lerp(1.0 - _Cavity, 1.0, surface);
                // Moist hollows and shores darker and greener, exposed ridges paler;
                // rock keeps its own colour.
                albedo *= lerp(half3(1, 1, 1), lerp(_DryTint.rgb, _WetTint.rgb, moisture), 1.0 - b.b);
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
                    if (_SF_BurntGrassParams.y > 0.5)
                    {
                        // Burnt ground keeps its ash long after the scorch of a shell fades.
                        half gone = SAMPLE_TEXTURE2D(_SF_BurntGrass, sampler_SF_BurntGrass, wp.xz * _SF_BurntGrassParams.x).r;
                        half ash = smoothstep(0.3, 0.9, gone);
                        albedo = lerp(albedo, albedo * half3(0.30, 0.28, 0.27), ash);
                        smooth *= 1.0 - ash * 0.45;
                    }
                    // Craters (A): a scorched, blasted centre inside a ring of lighter,
                    // freshly thrown earth, both with ragged edges from the dirt
                    // scan's height so no crater is a clean circle.
                    half crater = gm.a;
                    if (crater > 0.004)
                    {
                        float2 cp = wp.xz;
                        half n = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, cp * 0.19).a * 0.6
                               + SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, cp * 0.83).a * 0.4;
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
