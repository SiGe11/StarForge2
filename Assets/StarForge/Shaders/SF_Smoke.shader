// SF_Smoke — smoke, dust and spray particles as lit, billowing clouds.
//
// The photographed smoke sheet drew every puff as the same flat grey sprite. A
// puff here is one of four cauliflower clusters in smoke_puffs.png (made by
// Tools/make_smoke_puffs.py: surface normal in RG, fold occlusion in B,
// coverage in A), picked per particle by its random seed and lit by the sun
// and the sky: the billows facing the sun are bright, the folds between them
// and the underside fall into shade, and thin edges glow when the camera looks
// toward the sun. The normal is carried into world space through the quad's
// own screen-space derivatives, so it follows each particle's rotation. As a
// puff ages its thin parts are eaten away first, so it breaks into wisps
// instead of fading out as a disc.
//
// Needs the particle system's custom vertex streams (FXDirector.SmokeStreams):
// TEXCOORD0 = uv.xy, stable random, age 0..1. Soft-faded against the depth
// texture where it meets the ground, and near the camera. _Billow 0 draws a
// plain soft ball (water drops).
Shader "StarForge/Smoke"
{
    Properties
    {
        _MainTex("Puff atlas (2x2: normal xy, occlusion, coverage)", 2D) = "white" {}
        _Billow("Billowing", Range(0, 1)) = 1
        _Density("Density", Range(0.2, 3)) = 1.0
        _SoftFade("Soft fade distance", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Smoke"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Billow;
                half _Density;
                half _SoftFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float4 uv         : TEXCOORD0;   // xy quad, z stable random, w age
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float4 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                // World-simulated particles arrive in world space (identity object matrix).
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv.xy;
                half seed = i.uv.z;
                half age = saturate(i.uv.w);
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);

                half3 nTS;
                half ao, coverage;
                if (_Billow > 0.5)
                {
                    float cell = floor(frac(seed * 7.13) * 4.0);
                    float2 atlasUV = uv * 0.5 + float2(fmod(cell, 2.0), floor(cell / 2.0)) * 0.5;
                    half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, atlasUV);
                    nTS.xy = t.rg * 2.0 - 1.0;
                    nTS.z = sqrt(saturate(1.0 - dot(nTS.xy, nTS.xy)));
                    ao = t.b;
                    // Thin parts go first as the puff ages; the edge stays soft. The
                    // atlas's coverage stops short of opaque on purpose -- do not
                    // rescale it back up to 1 here, or one puff draws as a solid
                    // sprite again and the whole point of it is lost.
                    coverage = smoothstep(0.0, 1.0, saturate(t.a - age * 0.5));
                }
                else
                {
                    float2 c = uv * 2.0 - 1.0;
                    half r2 = dot(c, c);
                    nTS = half3(c * 0.8, sqrt(saturate(1.0 - r2 * 0.64)));
                    ao = 1.0;
                    coverage = saturate(1.0 - r2) * saturate(1.0 - r2);
                }

                // Tangent frame of the (rotated) quad from its screen-space derivatives.
                float3 dp1 = ddx(i.positionWS), dp2 = ddy(i.positionWS);
                float2 duv1 = ddx(uv), duv2 = ddy(uv);
                float3 dp2perp = cross(dp2, V), dp1perp = cross(V, dp1);
                float3 T = dp2perp * duv1.x + dp1perp * duv2.x;
                float3 B = dp2perp * duv1.y + dp1perp * duv2.y;
                float invmax = rsqrt(max(max(dot(T, T), dot(B, B)), 1e-12));
                half3 n = normalize(T * invmax * nTS.x + B * invmax * nTS.y + V * nTS.z);

                Light L = GetMainLight();
                half ndl = saturate(dot(n, L.direction));
                // Light from above as well as from the sun: with the sun behind the camera
                // every billow faces it and the cloud goes flat, but tops still catch the
                // sky and undersides and folds stay dark, which is what reads as volume.
                half up = saturate(n.y * 0.5 + 0.5);
                half3 sky = SampleSH(half3(0, 1, 0));
                // Thin edges light up when the sun is behind them.
                half back = pow(saturate(dot(-V, L.direction)), 4.0) * (1.0 - coverage) * 1.5;
                // The sky term carries most of the form: smoke over a landscape is
                // grey with bright tops, not a black mass. It used to be weak enough
                // that a dark particle colour came out nearly black whatever the sun
                // was doing.
                half3 lit = i.color.rgb * ((sky * (0.15 + 1.1 * up) + L.color * (0.08 + 0.8 * ndl) * (0.35 + 0.65 * up * up))
                                           * lerp(0.20, 1.0, ao) + L.color * back);

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half soft = saturate((sceneEye - i.screenPos.w) / max(0.01, _SoftFade));
                half nearCam = saturate((i.screenPos.w - 2.0) / 4.0);

                half a = saturate(coverage * _Density) * i.color.a * soft * nearCam;
                return half4(lit, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
