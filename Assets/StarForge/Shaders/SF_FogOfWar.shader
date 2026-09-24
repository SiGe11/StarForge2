// SF_FogOfWar — full-screen atmosphere and fog of war, run by a URP
// FullScreenPassRendererFeature.
//
// Reconstructs each pixel's world position from the camera depth texture, then:
//
//  1. Atmosphere (Atmosphere.cs): exponential height fog integrated along the
//     view ray, so mist pools in low ground and over water while plateaus stay
//     clear, plus distance haze and sun in-scattering. Done here rather than per
//     shader, because this pass already has the world position of everything.
//  3. Fog of war: samples the player's visibility texture (r = currently seen,
//     g = ever seen), which FogOfWarRenderer uploads from the same per-team grid
//     the AI is fogged by. Explored-but-unwatched ground stays legible as a dim,
//     cooled memory; ground never seen goes near-black. Applied after the
//     atmosphere so unexplored ground stays dark instead of hazing to grey.
Shader "StarForge/FogOfWar"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "FogOfWar"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_SF_FogTex); SAMPLER(sampler_SF_FogTex);
            float4 _SF_FogParams;    // x: 1 / map size, y: strength, z: time

            float4 _SF_AtmoParams;   // x: density, y: height falloff, z: base height, w: enabled
            float4 _SF_AtmoHaze;     // x: start distance, y: density
            float4 _SF_AtmoColor;    // linear fog colour
            float4 _SF_AtmoSun;      // rgb: linear sun scatter colour, a: strength
            float4 _SF_AtmoSunDir;   // xyz: direction toward the sun


            half3 ApplyAtmosphere(half3 col, float3 wp)
            {
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = wp - cam;
                float dist = length(ray);
                float3 dir = ray / max(dist, 1e-4);

                // Density a*exp(-b*(h - base)), integrated in closed form between the
                // camera's height and the pixel's. Heights are clamped a little below
                // the base so lake beds do not blow up the exponential.
                float a = _SF_AtmoParams.x, b = _SF_AtmoParams.y;
                float h0 = max(cam.y - _SF_AtmoParams.z, -2.0);
                float h1 = max(wp.y - _SF_AtmoParams.z, -2.0);
                float dh = h1 - h0;
                float e0 = exp(-b * h0), e1 = exp(-b * h1);
                float optical = abs(dh) > 0.01 ? a * dist * (e0 - e1) / (b * dh) : a * dist * e0;
                optical += max(0.0, dist - _SF_AtmoHaze.x) * _SF_AtmoHaze.y;
                half transmittance = exp(-optical);

                half mu = saturate(dot(dir, _SF_AtmoSunDir.xyz));
                half3 inscatter = _SF_AtmoColor.rgb + _SF_AtmoSun.rgb * (pow(mu, 8.0) * _SF_AtmoSun.a);
                return col * transmittance + inscatter * (1.0 - transmittance);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                bool fow = _SF_FogParams.y > 0.0;
                bool atmo = _SF_AtmoParams.w > 0.5;
                if (!fow && !atmo) return col;

                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                if (raw <= 1e-7) return col;             // sky
                float depth = raw;
            #else
                if (raw >= 1.0 - 1e-7) return col;
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
            #endif
                float3 wp = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                half3 c = col.rgb;
                if (atmo) c = ApplyAtmosphere(c, wp);
                if (!fow) return half4(c, col.a);

                // Beyond the map the texture only has its border texels, and
                // stretched straight out they draw streaks. Out there the fog follows
                // the ground along the nearest border instead, averaged over a
                // stretch of it that widens with distance, so it fans out in soft
                // sweeps: dark past unexplored edges, dimmed past explored ones.
                float2 m01 = wp.xz * _SF_FogParams.x;
                float2 edge01 = saturate(m01);
                float2 past = m01 - edge01;
                half2 fog;
                if (dot(past, past) <= 0.0)
                    fog = SAMPLE_TEXTURE2D_LOD(_SF_FogTex, sampler_SF_FogTex, m01, 0).rg;
                else
                {
                    float2 tangent = abs(past.x) > abs(past.y) ? float2(0.0, 1.0) : float2(1.0, 0.0);
                    float2 spread = tangent * length(past) * 0.45;
                    fog = (SAMPLE_TEXTURE2D_LOD(_SF_FogTex, sampler_SF_FogTex, edge01 - spread * 2.0, 0).rg
                         + SAMPLE_TEXTURE2D_LOD(_SF_FogTex, sampler_SF_FogTex, edge01 - spread, 0).rg
                         + SAMPLE_TEXTURE2D_LOD(_SF_FogTex, sampler_SF_FogTex, edge01, 0).rg
                         + SAMPLE_TEXTURE2D_LOD(_SF_FogTex, sampler_SF_FogTex, edge01 + spread, 0).rg
                         + SAMPLE_TEXTURE2D_LOD(_SF_FogTex, sampler_SF_FogTex, edge01 + spread * 2.0, 0).rg) * 0.2;
                }

                // A slow shimmer along the edge of vision, so the boundary reads
                // as a sensor horizon rather than a dimmer switch.
                half edge = fog.r * (1.0 - fog.r) * 4.0;
                half shimmer = 0.5 + 0.5 * sin(_SF_FogParams.z * 1.7 + wp.x * 0.45 + wp.z * 0.31);

                half lum = dot(c, half3(0.299, 0.587, 0.114));
                half3 memory = lerp(c, lum.xxx * half3(0.80, 0.88, 1.06), 0.65) * 0.40;
                half3 unknown = c * 0.05 + half3(0.004, 0.006, 0.012);
                half3 fogged = lerp(unknown, memory, fog.g);
                half3 outc = lerp(fogged, c, fog.r);
                outc += half3(0.05, 0.12, 0.2) * edge * shimmer * 0.35 * lum;

                return half4(lerp(c, outc, _SF_FogParams.y), col.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
