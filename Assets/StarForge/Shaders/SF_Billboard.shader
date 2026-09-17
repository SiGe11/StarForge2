// SF_Billboard — instanced quads for health bars (screen-aligned, drawn over
// the scene), projectile streaks (stretched along the velocity, facing the
// camera) and muzzle flares (the same, shaped as a blast bulging out of the
// muzzle and tapering to a point). The instance matrix carries position and
// size; _Params.x picks the kind.
Shader "StarForge/Billboard"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Billboard"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Params)   // x kind (0 bar, 1 streak), y fill, z segments, w secondary fill
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float kind = UNITY_ACCESS_INSTANCED_PROP(Props, _Params).x;
                float4x4 m = GetObjectToWorldMatrix();
                float3 center = float3(m._m03, m._m13, m._m23);
                float3 ax = float3(m._m00, m._m10, m._m20);
                float3 ay = float3(m._m01, m._m11, m._m21);
                float3 wp;
                if (kind < 0.5)
                {
                    float3 right = UNITY_MATRIX_V[0].xyz;
                    float3 up = UNITY_MATRIX_V[1].xyz;
                    wp = center + right * v.positionOS.x * length(ax) + up * v.positionOS.y * length(ay);
                }
                else
                {
                    float len = length(ax);
                    float3 dir = ax / max(len, 1e-4);
                    float3 toCam = normalize(_WorldSpaceCameraPos - center);
                    float3 side = normalize(cross(dir, toCam) + 1e-5);
                    wp = center + dir * v.positionOS.x * len + side * v.positionOS.y * length(ay);
                }
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float4 p = UNITY_ACCESS_INSTANCED_PROP(Props, _Params);
                float2 uv = i.uv;

                if (p.x < 0.5)
                {
                    half inFill = step(uv.x, p.y);
                    half inSecondary = step(uv.x, p.w) * step(uv.y, 0.3);
                    half seg = p.z > 0.0 ? step(0.9, frac(uv.x * p.z)) : 0.0;
                    half border = saturate(step(uv.y, 0.14) + step(0.86, uv.y) + step(uv.x, 0.015) + step(0.985, uv.x));
                    half3 col = lerp(half3(0.03, 0.03, 0.04), c.rgb * lerp(0.7, 1.15, uv.y), inFill);
                    col = lerp(col, half3(0.35, 0.75, 1.0), inSecondary);
                    col *= 1.0 - seg * 0.55;
                    col = lerp(col, half3(0.0, 0.0, 0.0), border);
                    return half4(col, c.a);
                }

                if (p.x > 1.5)
                {
                    // Flare: widest a third of the way out, a hot core along the
                    // axis, soft where it leaves the muzzle, pointed at the tip.
                    half x = uv.x;
                    half profile = sin(3.14159 * pow(max(x, 1e-3), 0.6));
                    half r = abs(uv.y * 2.0 - 1.0) / max(profile, 0.02);
                    half body = saturate(1.0 - r);
                    half core = saturate(1.0 - r * 2.2);
                    half a = (body * body * 0.7 + core * core) * smoothstep(0.0, 0.06, x) * (1.0 - x * 0.55);
                    return half4(c.rgb * a, a);
                }

                half across = 1.0 - abs(uv.y * 2.0 - 1.0);
                across *= across;
                half along = uv.x * uv.x * (1.0 - smoothstep(0.92, 1.0, uv.x) * 0.5);
                return half4(c.rgb * across * along, across * along);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
