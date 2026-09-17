// SF_WaterRipple — rings and foam lying on the water, drawn instanced by
// FXDirector: the wake a unit leaves wading or skimming (a ring dropped every
// metre or so; overlapping rings widen behind it into a V), the splash where it
// enters or leaves the water, and where shells land in it.
//
// Flat quads at the water's surface, lit by the sun and the sky, alpha-blended
// (premultiplied) after the water. _Params: x kind (0 ring, 1 foam patch),
// y age 0..1, z seed. _Color: rgb tint, a strength.
Shader "StarForge/WaterRipple"
{
    Properties { }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-90" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Ripple"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Params)
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
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float RHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float RNoise(float2 x)
            {
                float2 i = floor(x);
                float2 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(RHash(i), RHash(i + float2(1, 0)), f.x), lerp(RHash(i + float2(0, 1)), RHash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float4 p = UNITY_ACCESS_INSTANCED_PROP(Props, _Params);
                float2 d = (i.uv - 0.5) * 2.0;
                float r = length(d);
                if (r > 1.0) return 0;
                half age = saturate(p.y);
                half k = 1.0 - age;
                half a;
                // Broken up so no ring or patch is a clean geometric shape.
                half ang = atan2(d.y, d.x);
                half breakup = RNoise(float2(ang * 2.5 + p.z * 17.0, r * 4.0 - age * 1.5 + p.z * 5.0));
                if (p.x < 0.5)
                {
                    // A ring spreading and thinning as it fades.
                    half rr = lerp(0.2, 0.95, 1.0 - k * k);
                    half width = lerp(0.2, 0.06, age);
                    half ring = smoothstep(rr - width, rr, r) * (1.0 - smoothstep(rr, rr + width * 0.6, r));
                    a = ring * smoothstep(0.25, 0.65, breakup) * k * k;
                }
                else
                {
                    // Foam: a lacy patch that opens up into holes and dissolves.
                    half lace = RNoise(i.positionWS.xz * 2.3 + p.z * 31.0) * 0.6 + RNoise(i.positionWS.xz * 5.1 - p.z * 13.0) * 0.4;
                    half threshold = lerp(0.3, 0.85, age);
                    half foam = smoothstep(threshold, threshold + 0.15, lace + (1.0 - r) * 0.35);
                    a = foam * (1.0 - smoothstep(0.55, 1.0, r)) * k;
                }
                a = saturate(a * c.a);
                Light L = GetMainLight();
                half3 lit = c.rgb * (L.color * 0.55 * saturate(L.direction.y + 0.2) + SampleSH(half3(0, 1, 0)));
                return half4(lit * a, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
