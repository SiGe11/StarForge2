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
// pile of lumps, and light through the leaves when the camera looks toward the
// sun. A crown is leaf cards over a solid inner canopy (build_flora.py): cards
// (vertex alpha 1) show a photographed leaf spray (_CardTex, per kind; made by
// Tools/blender/make_leaf_cards.py from ambientCG's leaf atlases) and are cut
// out on its alpha, drawn from both sides; the canopy inside (vertex alpha 0)
// is the generated leaf pile (Tools/make_leaf_textures.py), triplanar and a
// shade darker, so the sky never shows through a crown.
// Per kind (property block): card texture, leaf style, blossom, bark style.
// The cards are the one place StarForge alpha-tests: nothing else reads as
// leaves at this range, and only foliage pays for it (the bark material culls
// back faces and never clips). Burned-away foliage collapses to a point.
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
        _LeafTex("Leaf pile (normal xy, occlusion, shade)", 2D) = "grey" {}
        _NeedleTex("Needle tufts (normal xy, occlusion, shade)", 2D) = "grey" {}
        _LeafTiling("Leaf texture repeats per metre", Float) = 0.9
        _LeafNormal("Leaf normal strength", Range(0, 2)) = 0.9
        _LeafStyle("0 broad leaves, 1 needles", Float) = 0
        _Bloom("Blossom colour (rgb), share (a)", Color) = (1, 1, 1, 0)
        _BarkStyle("0 furrowed, 1 birch", Float) = 0
        _CardTex("Leaf spray cards (colour, coverage)", 2D) = "white" {}
        _CardNormal("Leaf spray normals", 2D) = "bump" {}
        _CardTint("Card colour multiplier", Color) = (1, 1, 1, 1)
        _Cutoff("Card coverage cut-off", Range(0, 1)) = 0.45
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
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
            float _LeafTiling;
            half _LeafNormal;
            half _LeafStyle;
            half4 _Bloom;
            half _BarkStyle;
            half4 _CardTint;
            half _Cutoff;
        CBUFFER_END

        TEXTURE2D(_CardTex); SAMPLER(sampler_CardTex);
        TEXTURE2D(_CardNormal);

        // Card coverage; everything that is not a card (vertex alpha 0) is solid.
        half CardAlpha(float2 uv, half isCard)
        {
            if (isCard < 0.5) return 1.0;
            return SAMPLE_TEXTURE2D(_CardTex, sampler_CardTex, uv).a;
        }

        UNITY_INSTANCING_BUFFER_START(TreeProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Params)   // x char, y embers, z foliage lost, w sway
        UNITY_INSTANCING_BUFFER_END(TreeProps)

        struct TreeAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
            float2 uv         : TEXCOORD0;
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

        // The match's wind and the live blast pressure fronts.
        #include "SF_Wind.hlsl"

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
            // Atmosphere uploads the match's wind; without it (the editor's scene view)
            // fall back to a steady breeze so nothing stands frozen.
            float4 w = _SF_Wind.z > 0.001 ? _SF_Wind : float4(0.82, 0.57, 0.5, _Time.y);
            float2 dir = w.xy;
            float blowing = w.z;
            float t = w.w;
            // The gust travels along the wind, so a wave of sway crosses a wood the
            // way it does a field; the whole tree leans with it, the twigs flutter
            // faster, and a leaf card shivers on its own. All of it scales with the
            // vertex's distance from the foot of the trunk (vertex colour B).
            float phase = dot(origin.xz, dir * 0.075);
            float gust = sin(t * 0.8 - phase) * 0.65 + 0.35;
            float flutter = sin(t * 2.1 + phase * 3.0 + v.color.g * 6.2831) * 0.25;
            float sway = (gust + flutter) * _WindStrength * v.color.b * prm.w * blowing;
            float3 side = float3(-dir.y, 0, dir.x);
            // A leaf card (vertex alpha 1) also shivers across the wind.
            float shiver = v.color.a * blowing * _WindStrength * v.color.b
                         * sin(t * 4.6 + v.color.g * 25.13 + phase * 2.0) * 0.5;
            wp.xz += dir * sway * 1.6 + side.xz * shiver;
            wp.y -= abs(sway) * 0.3 * v.color.b;
            // A blast's pressure front: the whole plant is thrown away from it and
            // pressed down, hardest at the crown, and springs back as it passes.
            // A felled tree is stiff (prm.w), the same as it is to the wind.
            float2 blast = SFGust(origin.xz) * prm.w;
            if (dot(blast, blast) > 1e-8)
            {
                // Bent about the foot of the trunk, not sheared: the crown swings
                // over and down, it does not slide sideways and grow taller.
                float3 rel = wp - origin;
                float L = length(rel);
                rel.xz += blast * v.color.b * 1.5;
                rel.y -= length(blast) * v.color.b * 0.45;
                wp = origin + rel * (L / max(length(rel), 1e-4));
            }
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
            Cull [_Cull]

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
                half3  normalOS   : TEXCOORD7;   // the model's own normal, for the triplanar weights
                float2 uv         : TEXCOORD8;
                UNITY_VERTEX_INPUT_INSTANCE_ID       // the leaf normals go to world space per instance
            };

            TEXTURE2D(_LeafTex); SAMPLER(sampler_LeafTex);
            TEXTURE2D(_NeedleTex);

            // The leaf pile, triplanar in object space: normal offset (world), occlusion, shade.
            half4 LeafPile(float3 p, half3 nOS, out half3 offsetWS)
            {
                half3 w = abs(nOS);
                w = w * w * w;
                w /= max(1e-4, w.x + w.y + w.z);
                float3 tp = p * _LeafTiling;
                half4 tX, tY, tZ;
                if (_LeafStyle > 0.5)
                {
                    tX = SAMPLE_TEXTURE2D(_NeedleTex, sampler_LeafTex, tp.zy);
                    tY = SAMPLE_TEXTURE2D(_NeedleTex, sampler_LeafTex, tp.xz);
                    tZ = SAMPLE_TEXTURE2D(_NeedleTex, sampler_LeafTex, tp.xy);
                }
                else
                {
                    tX = SAMPLE_TEXTURE2D(_LeafTex, sampler_LeafTex, tp.zy);
                    tY = SAMPLE_TEXTURE2D(_LeafTex, sampler_LeafTex, tp.xz);
                    tZ = SAMPLE_TEXTURE2D(_LeafTex, sampler_LeafTex, tp.xy);
                }
                // Each projection's slope, on the two axes of its own plane.
                half2 sX = tX.rg * 2.0 - 1.0, sY = tY.rg * 2.0 - 1.0, sZ = tZ.rg * 2.0 - 1.0;
                half3 offOS = half3(0.0, sX.y, sX.x) * w.x + half3(sY.x, 0.0, sY.y) * w.y + half3(sZ.x, sZ.y, 0.0) * w.z;
                offsetWS = TransformObjectToWorldDir(offOS, false);
                return tX * w.x + tY * w.y + tZ * w.z;
            }

            Varyings Vert(TreeAttributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float4 prm = TreeParams();
                float3 posOS, nOS;
                float3 wp = TreeVertex(v, prm, posOS, nOS);
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.normalWS = TransformObjectToWorldNormal(nOS);
                o.positionOS = posOS;
                o.normalOS = v.normalOS;
                o.uv = v.uv;
                o.color = v.color;
                o.prm = prm;
                o.fog = ComputeFogFactor(o.positionCS.z);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.rnd = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 p = i.positionOS;
                half ao = lerp(0.35, 1.0, i.color.r);
                half charAmt = saturate(i.prm.x);
                half embers = saturate(i.prm.y);
                half3 albedo;
                half smooth;
                half leafMask = 0;

                if (_Foliage > 0.5 && i.color.a > 0.5)
                {
                    // A leaf card: the photographed spray, cut out on its coverage.
                    half4 tex = SAMPLE_TEXTURE2D(_CardTex, sampler_CardTex, i.uv);
                    clip(tex.a - _Cutoff);
                    // The spray's own normals, in a frame from the screen-space
                    // derivatives of position and UV (the cards carry no tangents),
                    // tilted gently off the crown normal the card was given.
                    half3 nt = UnpackNormal(SAMPLE_TEXTURE2D(_CardNormal, sampler_CardTex, i.uv));
                    float3 dp1 = ddx(i.positionWS), dp2 = ddy(i.positionWS);
                    float2 du1 = ddx(i.uv), du2 = ddy(i.uv);
                    float3 dp2perp = cross(dp2, n), dp1perp = cross(n, dp1);
                    float3 T = dp2perp * du1.x + dp1perp * du2.x;
                    float3 B = dp2perp * du1.y + dp1perp * du2.y;
                    float inv = rsqrt(max(1e-12, max(dot(T, T), dot(B, B))));
                    n = normalize(n + (T * inv * nt.x + B * inv * nt.y) * 0.45);
                    half patch = TreeNoise3(p * 0.9 + i.rnd * 13.0);
                    half height = saturate((p.y - (_CanopyCenter.y - _CanopyRadii.y)) / max(2.0 * _CanopyRadii.y, 0.1));
                    half3 c = tex.rgb * _CardTint.rgb;
                    // Per card and per tree a little lighter or darker, warmer or cooler.
                    c *= lerp(0.82, 1.12, saturate(i.color.g * 0.7 + i.rnd * 0.3)) * lerp(0.9, 1.08, patch);
                    c = lerp(c * half3(0.85, 0.94, 1.05), c * half3(1.08, 1.04, 0.9), height);
                    // Blossom: whole cards, a share of them picked by their random value.
                    half bloom = step(1.0 - _Bloom.a, frac(i.color.g * 7.31)) * step(0.35, dot(tex.rgb, half3(0.33, 0.33, 0.33)));
                    albedo = lerp(c, _Bloom.rgb * (0.7 + 0.3 * tex.g), bloom * 0.85);
                    ao *= lerp(0.65, 1.0, height);
                    smooth = 0.25;
                    leafMask = 1;
                }
                else if (_Foliage > 0.5)
                {
                    half3 leafOffset;
                    half4 pile = LeafPile(p, normalize(i.normalOS), leafOffset);
                    n = normalize(n + leafOffset * _LeafNormal);
                    // Broad patches of lighter and darker leaves across the crown.
                    half patch = TreeNoise3(p * 0.9 + i.rnd * 13.0);
                    half3 c = lerp(_LeafColor.rgb, _LeafColor2.rgb, saturate(i.color.g * 0.6 + i.rnd * 0.4 + (patch - 0.5) * 0.5));
                    // The underside and the inside of the crown are in their own shade,
                    // cooler and bluer; the sunlit top is warmer and yellower.
                    half height = saturate((p.y - (_CanopyCenter.y - _CanopyRadii.y)) / max(2.0 * _CanopyRadii.y, 0.1));
                    c = lerp(c * half3(0.8, 0.92, 1.08), c * half3(1.15, 1.08, 0.8), height);
                    // The inner canopy behind the cards: in the crown's own shade.
                    albedo = c * lerp(0.6, 1.3, pile.a) * 0.62;
                    // Blossom: a share of the leaves on top, picked by their own shade
                    // and patchy across the crown.
                    half bloom = step(1.0 - _Bloom.a, pile.a * 0.7 + patch * 0.3) * smoothstep(0.55, 0.8, pile.b);
                    albedo = lerp(albedo, _Bloom.rgb, bloom);
                    ao *= lerp(0.6, 1.0, height) * lerp(0.5, 1.0, pile.b);
                    smooth = lerp(0.22, 0.3, bloom);
                    leafMask = 1;
                }
                else if (_BarkStyle > 0.5)
                {
                    // Birch: pale, chalky bark with dark lenticel dashes round the
                    // trunk and a darker, rougher foot.
                    half dash = TreeNoise3(float3(p.x * 14.0, p.y * 7.0, p.z * 14.0));
                    half marks = smoothstep(0.72, 0.82, dash) * step(0.35, TreeNoise3(p * 3.0));
                    half foot = 1.0 - smoothstep(0.2, 1.4, p.y);
                    albedo = _BarkColor.rgb * (0.92 + 0.12 * TreeNoise3(p * 5.0));
                    albedo = lerp(albedo, half3(0.05, 0.045, 0.04), max(marks * 0.85, foot * 0.7));
                    smooth = 0.14;
                }
                else
                {
                    // Bark: vertical furrows and a little knot noise.
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
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half card : TEXCOORD1; };

            ShadowVaryings ShadowVert(TreeAttributes v)
            {
                ShadowVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posOS, nOS;
                float3 wp = TreeVertex(v, TreeParams(), posOS, nOS);
                float3 nWS = TransformObjectToWorldNormal(nOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - wp);
            #else
                float3 lightDir = _LightDirection;
            #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(wp, nWS, lightDir)));
                o.uv = v.uv;
                o.card = _Foliage > 0.5 ? v.color.a : 0.0;
                return o;
            }

            half4 ShadowFrag(ShadowVaryings i) : SV_Target
            {
                clip(CardAlpha(i.uv, i.card) - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half card : TEXCOORD1; };

            DepthVaryings DepthVert(TreeAttributes v)
            {
                DepthVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posOS, nOS;
                o.positionCS = TransformWorldToHClip(TreeVertex(v, TreeParams(), posOS, nOS));
                o.uv = v.uv;
                o.card = _Foliage > 0.5 ? v.color.a : 0.0;
                return o;
            }

            half DepthFrag(DepthVaryings i) : SV_Target
            {
                clip(CardAlpha(i.uv, i.card) - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct NormalsVaryings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; half card : TEXCOORD2; };

            NormalsVaryings NormalsVert(TreeAttributes v)
            {
                NormalsVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posOS, nOS;
                o.positionCS = TransformWorldToHClip(TreeVertex(v, TreeParams(), posOS, nOS));
                o.normalWS = TransformObjectToWorldNormal(nOS);
                o.uv = v.uv;
                o.card = _Foliage > 0.5 ? v.color.a : 0.0;
                return o;
            }

            half4 NormalsFrag(NormalsVaryings i) : SV_Target
            {
                clip(CardAlpha(i.uv, i.card) - _Cutoff);
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
