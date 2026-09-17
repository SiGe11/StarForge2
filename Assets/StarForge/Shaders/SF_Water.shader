// SF_Water — lake surface.
//
// What it is made of, and why:
//  * No repeating pattern. Three ripple layers of the one normal texture run at
//    scales and angles with no common period, through UVs bent by a slow,
//    large-scale noise, and the mix between them drifts from patch to patch,
//    so no two stretches of water look alike. Under them, three long swells
//    cross the lake in different directions, analytic so they never tile.
//  * Depth from the camera depth texture, as both the vertical depth over the
//    lake bed (shore effects) and the path length through the water.
//  * The bed seen through the water, from the camera's opaque colour copy, bent
//    by the ripples (never picking up something standing in front of the water)
//    and absorbed channel by channel with the path length: red goes first, so
//    the shallows are clear and green-tinted and the deeps turn dark blue-green,
//    lit by light scattered inside the water rather than painted over it.
//  * Waves rolling onto the shore: foam crests that form on depth contours and
//    travel toward the waterline, broken up by noise, plus lacy foam that laps
//    at the waterline itself.
//  * Caustics dancing on the bed in the shallows.
//  * Sun glint and a Fresnel sky reflection, both calmed with distance: at RTS
//    range a ripple is sub-pixel and a sharp glint on it aliases into speckle.
//
// Premultiplied alpha: opaque over the water, fading to nothing at the
// waterline so the shore has no seam.
Shader "StarForge/Water"
{
    Properties
    {
        _ShallowColor("Tint of the bed through the water", Color) = (0.80, 0.97, 0.92, 1)
        _DeepColor("Colour of light scattered in the water", Color) = (0.03, 0.14, 0.16, 1)
        _Absorption("Absorption per metre (rgb)", Vector) = (0.55, 0.16, 0.13, 0)
        _Refraction("Refraction", Range(0, 0.1)) = 0.03
        _NormalTex("Ripple normal", 2D) = "bump" {}
        _NormalStrength("Ripple strength", Range(0, 2)) = 0.6
        _RippleScale("Ripple scale", Float) = 0.05
        _FlowSpeed("Flow speed", Float) = 0.6
        _SwellStrength("Swell strength", Range(0, 2)) = 0.35
        _DepthRange("Absorption depth", Float) = 2.2
        _FoamColor("Foam", Color) = (0.85, 0.9, 0.92, 1)
        _FoamWidth("Foam width", Float) = 0.6
        _ShoreWaves("Shore waves", Range(0, 1)) = 0.6
        _CausticStrength("Caustics", Range(0, 2)) = 0.5
        _Smoothness("Smoothness", Range(0, 1)) = 0.93
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _FoamColor;
                half4 _Absorption;
                half _Refraction;
                float4 _NormalTex_ST;
                half _NormalStrength;
                float _RippleScale;
                float _FlowSpeed;
                half _SwellStrength;
                half _DepthRange;
                half _FoamWidth;
                half _ShoreWaves;
                half _CausticStrength;
                half _Smoothness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                half   fog        : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth value noise, 0..1.
            float ValueNoise(float2 x)
            {
                float2 i = floor(x);
                float2 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float2 Rotate(float2 p, float a)
            {
                float c = cos(a), s = sin(a);
                return float2(p.x * c - p.y * s, p.x * s + p.y * c);
            }

            // Slope of one long travelling swell (the gradient of sin along dir).
            float2 Swell(float2 xz, float2 dir, float wavelength, float speed, float t)
            {
                float k = 6.2831853 / wavelength;
                return dir * (cos(dot(xz, dir) * k - t * speed) * k);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 wp = i.positionWS;
                float time = _Time.y;
                float t = time * _FlowSpeed;
                half camDist = distance(wp, _WorldSpaceCameraPos);
                // 1 up close, 0 at long RTS range.
                half nearFade = saturate(1.0 - camDist / 200.0);
                half closeFade = 1.0 - smoothstep(60.0, 140.0, camDist);

                // Warp the ripple domain with a slow, large noise field.
                float2 warp = float2(ValueNoise(wp.xz * 0.021 + float2(0.0, t * 0.013)),
                                     ValueNoise(wp.xz * 0.021 + float2(17.3, -t * 0.011))) - 0.5;
                float2 base = wp.xz + warp * 9.0;

                float2 uvA = Rotate(base, 0.0) * _RippleScale + float2(t * 0.021, t * 0.013);
                float2 uvB = Rotate(base, 2.1) * _RippleScale * 1.71 + float2(-t * 0.017, t * 0.024);
                float2 uvC = Rotate(base, 4.0) * _RippleScale * 0.43 + float2(t * 0.008, -t * 0.011);
                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvA));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvB));
                half3 nC = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvC));
                // Some patches of the lake are choppier than others, and they drift.
                half chop = ValueNoise(wp.xz * 0.012 + float2(-t * 0.004, t * 0.003));
                half2 ripple = nA.xy * lerp(0.55, 1.15, chop) + nB.xy * lerp(1.0, 0.45, chop) + nC.xy * 0.7;

                float2 swell = Swell(wp.xz, float2(0.83, 0.56), 13.0, 0.9, time)
                             + Swell(wp.xz, float2(-0.34, 0.94), 8.5, 1.15, time) * 0.7
                             + Swell(wp.xz, float2(0.97, -0.26), 5.5, 1.5, time) * 0.45;
                swell *= 0.08;

                // Depth over the bed and the path length through the water.
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float raw = SampleSceneDepth(suv);
                float sceneEye = LinearEyeDepth(raw, _ZBufferParams);
            #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
            #endif
                float3 bed = ComputeWorldSpacePosition(suv, raw, UNITY_MATRIX_I_VP);
                half depth = max(0.0, wp.y - bed.y);
                half shallow = 1.0 - saturate(depth / 1.2);

                // Waves flatten out as they run up a shallow shore.
                half2 slope = ripple * _NormalStrength * lerp(0.2, 1.0, nearFade * nearFade) * lerp(1.0, 0.5, shallow)
                            + swell * _SwellStrength * (1.0 - shallow * 0.6);
                half3 n = normalize(half3(slope.x, 1.0, slope.y));
                half3 v = GetWorldSpaceNormalizeViewDir(wp);

                Light L = GetMainLight(TransformWorldToShadowCoord(wp), wp, half4(1, 1, 1, 1));
                half shadow = L.shadowAttenuation;
                half3 ambient = SampleSH(half3(0, 1, 0));

                // The bed, bent by the surface. The bend grows with depth (none at the
                // waterline, where it would tear the shore) and is refused where the
                // bent sample lands on something in front of the water, such as a
                // unit wading in it.
                float2 ruv = suv + slope * _Refraction * saturate(depth * 1.2);
                float rawR = SampleSceneDepth(ruv);
                float eyeR = LinearEyeDepth(rawR, _ZBufferParams);
                if (eyeR < i.screenPos.w) { ruv = suv; eyeR = sceneEye; }
                half path = max(0.0, eyeR - i.screenPos.w);
                half3 bedColor = SampleSceneColor(ruv);

                // Caustics on the bed in the shallows, from two slow layers of the
                // ripple texture crossing (bright where their slopes agree). Up close
                // only: far away the pattern is finer than a pixel.
                if (_CausticStrength > 0.001)
                {
                    float2 cuvA = bed.xz * 0.17 + float2(time * 0.021, time * 0.012);
                    float2 cuvB = Rotate(bed.xz, 1.3) * 0.21 - float2(time * 0.017, -time * 0.014);
                    half cA = UnpackNormal(SAMPLE_TEXTURE2D_BIAS(_NormalTex, sampler_NormalTex, cuvA, 1.0)).x;
                    half cB = UnpackNormal(SAMPLE_TEXTURE2D_BIAS(_NormalTex, sampler_NormalTex, cuvB, 1.0)).x;
                    half caustic = saturate(1.0 - abs(cA - cB) * 2.0);
                    caustic *= caustic * caustic;
                    bedColor += caustic * _CausticStrength * 0.6 * L.color * shadow * shallow * closeFade;
                }

                // Light through water: the bed absorbed channel by channel along the
                // path, and the water's own scattered light filling in what is lost.
                half3 transmit = exp(-path * _Absorption.rgb);
                half3 scattered = _DeepColor.rgb * (L.color * lerp(0.35, 1.0, shadow) * saturate(L.direction.y) * 0.9 + ambient * 0.9);
                half3 under = bedColor * transmit * _ShallowColor.rgb + scattered * (1.0 - transmit.g);

                half fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, v)), 5.0);
                half3 refl = GlossyEnvironmentReflection(reflect(-v, n), 1.0 - _Smoothness, 1.0);
                half3 h = normalize(L.direction + v);
                half gloss = lerp(40.0, 260.0, _Smoothness) * lerp(0.35, 1.0, nearFade);
                half spec = pow(saturate(dot(n, h)), gloss) * 1.6 * shadow * lerp(0.08, 1.0, closeFade * closeFade);
                half3 col = under * (1.0 - fresnel) + refl * fresnel + spec * L.color;

                // Foam: crests on depth contours rolling in to the shore, and lace
                // lapping at the waterline, opening and closing with the swell.
                half breakup = ValueNoise(wp.xz * 0.11 + warp * 3.0 + float2(time * 0.05, 0.0));
                float roll = depth * 1.8 - time * 0.16 + (warp.x + warp.y) * 1.2;
                half crest = pow(saturate(1.0 - frac(roll)), 12.0);
                half waves = crest * smoothstep(0.35, 0.7, breakup) * pow(shallow, 1.5) * _ShoreWaves
                           * lerp(0.35, 1.0, closeFade);
                half lace = ValueNoise(wp.xz * 1.9 + float2(time * 0.13, -time * 0.09)) * 0.6
                          + ValueNoise(wp.xz * 4.3 - float2(time * 0.07, time * 0.11)) * 0.4;
                half lap = 0.5 + 0.5 * sin(time * 0.8 + breakup * 6.0);
                half reach = _FoamWidth * lerp(0.12, 0.3, lap);
                half edge = smoothstep(0.0, 0.25, saturate(1.0 - depth / max(reach, 0.01)) - (1.0 - lace) * 0.6);
                half foam = saturate(max(waves, edge * lerp(0.55, 1.0, closeFade)));
                half3 foamCol = _FoamColor.rgb * (L.color * 0.6 * lerp(0.4, 1.0, shadow) + ambient);
                col = lerp(col, foamCol, foam);

                // Fade out right at the waterline, so the intersection has no seam.
                half contact = smoothstep(0.0, 0.05, depth);
                col = MixFog(col, i.fog) * contact;
                return half4(col, contact);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
