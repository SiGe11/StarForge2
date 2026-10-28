// SF_GroundDecal — instanced box-volume decals that paint onto whatever the
// depth buffer holds: selection rings, placement footprints, order markers,
// range rings, scorch marks, vehicle tracks and the light pool under ore. One
// draw per material for all of them.
//
// Each instance is a unit cube; the fragment reconstructs the world position
// under the pixel, moves it into the cube's space and discards anything
// outside, so the mark conforms to slopes and cliff lips. Surfaces that are
// not roughly upward-facing (unit bodies, cliff faces) fade out.
Shader "StarForge/GroundDecal"
{
    Properties
    {
        _MainTex("Sheet (scorch)", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-200" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "GroundDecal"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Params)   // x kind, y progress/time, z rotation, w extra
                UNITY_DEFINE_INSTANCED_PROP(float4, _UVRect)   // sheet cell: xy offset, zw size
            UNITY_INSTANCING_BUFFER_END(Props)
            float _SF_Rain;   // how wet a shower has left the ground (FXDirector.Weather)

            // An integer hash of a cell's coordinates: no period over the map (a float hash
            // with two-decimal multipliers repeated every 50 and 100 cells).
            float Hash21(float2 q)
            {
                uint2 u = (uint2)(int2)q;
                uint h = (u.x * 1597334677u) ^ (u.y * 3812015801u);
                h = (h ^ (h >> 16)) * 2246822519u;
                h ^= h >> 13;
                return h * (1.0 / 4294967296.0);
            }

            float VNoise(float2 q)
            {
                float2 i = floor(q), f = frac(q);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + 1.0), f.x), f.y);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float4 p = UNITY_ACCESS_INSTANCED_PROP(Props, _Params);

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float raw = SampleSceneDepth(suv);
            #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
            #endif
                float3 wp = ComputeWorldSpacePosition(suv, raw, UNITY_MATRIX_I_VP);
                float3 op = TransformWorldToObject(wp);
                clip(0.5 - abs(op));

                float3 n = normalize(cross(ddy(wp), ddx(wp)));
                half upness = smoothstep(0.45, 0.75, abs(n.y));
                // Fade toward the top and bottom of the volume so marks do not
                // climb the legs of whatever is standing on them.
                half vfade = 1.0 - smoothstep(0.25, 0.5, abs(op.y));

                float2 d = op.xz * 2.0;
                float r = length(d);
                float kind = p.x;
                half a = 0.0;

                if (kind < 0.5)
                {
                    // Selection ring with a faint fill and slow chevron shimmer.
                    half ring = smoothstep(0.78, 0.84, r) * (1.0 - smoothstep(0.92, 1.0, r));
                    half fill = (1.0 - smoothstep(0.0, 0.84, r)) * 0.07;
                    half ang = atan2(d.y, d.x);
                    half ticks = step(0.5, frac(ang / 6.2831853 * 24.0 + _Time.y * 0.15));
                    half outer = smoothstep(1.0, 1.02, r + 0.06) * (1.0 - smoothstep(1.02, 1.08, r + 0.06)) * ticks * 0.0;
                    a = ring + fill + outer;
                }
                else if (kind < 1.5)
                {
                    // Scorch mark from a 2x2 sheet, rotated.
                    float c = cos(p.z), s = sin(p.z);
                    float2 ruv = float2(d.x * c - d.y * s, d.x * s + d.y * c) * 0.5 + 0.5;
                    float4 rect = UNITY_ACCESS_INSTANCED_PROP(Props, _UVRect);
                    half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, rect.xy + saturate(ruv) * rect.zw).r;
                    a = saturate((mask - 0.04) * 1.3) * (1.0 - smoothstep(0.85, 1.0, r));
                    return half4(color.rgb, a * color.a * upness * vfade);
                }
                else if (kind < 2.5)
                {
                    // Placement footprint: grid-lined disc with a hard rim.
                    half rim = smoothstep(0.90, 0.95, r) * (1.0 - smoothstep(0.98, 1.0, r));
                    float2 g = abs(frac(wp.xz * 0.5) - 0.5);
                    half grid = (1.0 - smoothstep(0.0, 0.05, min(g.x, g.y))) * 0.35;
                    half disc = 1.0 - smoothstep(0.95, 1.0, r);
                    a = rim + (0.12 + grid) * disc;
                }
                else if (kind < 3.5)
                {
                    // Order marker: a ring collapsing inward, fading out.
                    half t = saturate(p.y);
                    half rr = lerp(1.0, 0.25, t);
                    a = (smoothstep(rr - 0.14, rr - 0.04, r) * (1.0 - smoothstep(rr, rr + 0.06, r))) * (1.0 - t);
                }
                else if (kind < 4.5)
                {
                    // Range ring: thin and dashed.
                    half ang = atan2(d.y, d.x);
                    half dash = step(0.45, frac(ang / 6.2831853 * 48.0));
                    a = smoothstep(0.965, 0.98, r) * (1.0 - smoothstep(0.99, 1.0, r)) * dash * 0.8;
                }
                // Kind 5 was the explosion's shockwave ring: a hot circle racing out
                // over the ground. A blast's pressure now shows only in the growth it
                // lays over, so nothing draws it -- do not hand 5 to something else.
                else if (kind < 6.5)
                {
                    // Track segment, alpha-blended: two treads either side of the
                    // centre line (z band centre, w half-width, both as fractions of
                    // the half-width across), pressed into the ground: a compacted
                    // groove (_Color, the ground's own soil darkened) with cleats across
                    // it and a soft darker fringe where the edge pressed in. It only
                    // darkens: a paler heaped-soil line each side read as lane markings
                    // and glowed in shade (the decal is unlit). Edges and depth are
                    // broken up by world-space noise, so consecutive segments line up
                    // and no two look painted on; the ends are soft so overlapping
                    // segments blend. Cleats finer than a couple of pixels fade to
                    // their average instead of aliasing into a moire.
                    float4 uvr = UNITY_ACCESS_INSTANCED_PROP(Props, _UVRect);   // x cleat pitch
                    float3 fwd = normalize(float3(UNITY_MATRIX_M._m02, 0.0, UNITY_MATRIX_M._m22));
                    float alongM = dot(wp.xz, fwd.xz);
                    float along = alongM * uvr.x;
                    half across = abs(d.x);
                    half n1 = VNoise(wp.xz * 2.3);
                    half n2 = VNoise(wp.xz * 7.1 + 13.7);
                    half t = abs(across - p.z) / max(p.w, 1e-3) * (1.0 + (n1 - 0.5) * 0.35);
                    half groove = 1.0 - smoothstep(0.72, 1.0, t);
                    half fringe = (1.0 - smoothstep(0.95, 1.4, t)) * 0.3 * (0.5 + 0.5 * n2);
                    half cleatSharp = 0.3 + 0.7 * smoothstep(0.12, 0.28, abs(frac(along) - 0.5));
                    half cleat = lerp(0.72, cleatSharp, saturate(1.5 - fwidth(along) * 3.0));
                    half ends = 1.0 - smoothstep(0.87, 1.0, abs(d.y));
                    half wet = 1.0 + 0.35 * _SF_Rain;   // a wet groove holds water and goes darker
                    half aGroove = max(groove * cleat * (0.55 + 0.45 * n1), fringe) * wet;
                    // Faded inside the box's long sides too, so nothing that reaches them
                    // (a Mech pad's fringe did) ends in a straight edge.
                    a = aGroove * ends * (1.0 - smoothstep(0.86, 1.0, across));
                    return half4(color.rgb, saturate(a * color.a) * upness * vfade);
                }
                else
                {
                    // Light pool under an ore seam: a soft radial falloff, additive.
                    half fall = saturate(1.0 - r);
                    a = fall * fall * (0.55 + 0.45 * fall);
                }

                a *= upness * vfade;
                return half4(color.rgb * a * color.a, a * color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
