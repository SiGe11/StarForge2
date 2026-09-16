// SF_Water — lake surface: depth-based absorption from the camera depth
// texture, two scrolling ripple normals, sun glint, sky reflection by
// Fresnel, and an animated foam band along the shore.
Shader "StarForge/Water"
{
    Properties
    {
        _ShallowColor("Shallow", Color) = (0.16, 0.42, 0.42, 1)
        _DeepColor("Deep", Color) = (0.02, 0.08, 0.14, 1)
        _NormalTex("Ripple normal", 2D) = "bump" {}
        _NormalStrength("Ripple strength", Range(0, 2)) = 0.7
        _RippleScale("Ripple scale", Float) = 0.05
        _FlowSpeed("Flow speed", Float) = 0.6
        _DepthRange("Absorption depth", Float) = 2.6
        _FoamColor("Foam", Color) = (0.85, 0.9, 0.92, 1)
        _FoamWidth("Foam width", Float) = 0.6
        _Smoothness("Smoothness", Range(0, 1)) = 0.93
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
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

            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _FoamColor;
                float4 _NormalTex_ST;
                half _NormalStrength;
                float _RippleScale;
                float _FlowSpeed;
                half _DepthRange;
                half _FoamWidth;
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

            half4 Frag(Varyings i) : SV_Target
            {
                float3 wp = i.positionWS;
                float t = _Time.y * _FlowSpeed;
                float2 uvA = wp.xz * _RippleScale + float2(t * 0.021, t * 0.013);
                float2 uvB = wp.xz * _RippleScale * 1.7 + float2(-t * 0.017, t * 0.024);
                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvA));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvB));
                // Ripples calm with distance: at RTS range the ripple texture is
                // sub-pixel, and a sharp glint on it aliases into white speckles.
                half distFade = saturate(1.0 - distance(wp, _WorldSpaceCameraPos) / 200.0);
                half2 slope = (nA.xy + nB.xy) * _NormalStrength * lerp(0.3, 1.0, distFade);
                half3 n = normalize(half3(slope.x, 1.0, slope.y));
                half3 v = GetWorldSpaceNormalizeViewDir(wp);

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float depthAlongRay = max(0.0, sceneEye - i.screenPos.w);
                half vdepth = depthAlongRay * saturate(v.y + 0.05);
                half absorb = saturate(vdepth / _DepthRange);
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, absorb);

                Light L = GetMainLight(TransformWorldToShadowCoord(wp), wp, half4(1, 1, 1, 1));
                half shadow = L.shadowAttenuation;
                half3 ambient = SampleSH(n);
                half3 diffuse = water * (L.color * saturate(dot(n, L.direction)) * lerp(0.35, 1.0, shadow) * 0.6 + ambient);
                half3 h = normalize(L.direction + v);
                half spec = pow(saturate(dot(n, h)), lerp(40.0, 240.0, _Smoothness)) * 1.4 * shadow;
                half fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, v)), 5.0);
                half3 refl = GlossyEnvironmentReflection(reflect(-v, n), 1.0 - _Smoothness, 1.0);
                half3 col = lerp(diffuse, refl, fresnel * 0.85) + spec * L.color;

                half foamBand = 1.0 - saturate(vdepth / _FoamWidth);
                half foamNoise = saturate(0.5 + (nA.x + nB.y) * 2.0);
                half foam = saturate(foamBand * 1.4 - (1.0 - foamNoise) * 0.9 + 0.12 * sin(_Time.y * 1.5 + wp.x * 0.3 + wp.z * 0.2));
                col = lerp(col, _FoamColor.rgb * (L.color * 0.6 * shadow + ambient), foam);

                half alpha = saturate(lerp(0.35, 0.95, absorb) + fresnel * 0.4 + foam);
                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
