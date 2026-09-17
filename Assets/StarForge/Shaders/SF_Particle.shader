// SF_Particle — flipbook particles for the original sprite sheets.
//
// The sheets are bright-on-black photographs with no real alpha, so coverage
// comes from luminance with a black-level floor subtracted: the smoke sheet
// averages ~27/255 between plumes, and used raw that haze draws every quad as
// a grey rectangle (the trap the original documented). Soft-particle fading
// against the depth texture keeps fireballs from slicing along the ground.
// The smoke sheet also has bright two-pixel grid lines between its cells, which
// drew a square outline round every puff; the border of each quad's texture
// footprint is faded out. (Faded per sheet cell, _SheetTiles 4, it also cut
// the smoke's opacity to a faint haze, so it stays at 1.)
Shader "StarForge/Particle"
{
    Properties
    {
        _MainTex("Sheet", 2D) = "white" {}
        _Floor("Black level floor", Range(0, 0.4)) = 0.03
        _Gain("Gain (additive)", Float) = 1.6
        [Toggle] _AlphaMode("Alpha blended (smoke)", Float) = 0
        _SoftFade("Soft fade distance", Float) = 1.2
        _SheetRect("Sheet cell (xy offset, zw size)", Vector) = (0, 0, 1, 1)
        _SheetTiles("Border fade repeats", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Particle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Floor;
                half _Gain;
                half _AlphaMode;
                half _SoftFade;
                float4 _SheetRect;
                float _SheetTiles;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                // Trails map 0..1 along their length, so they pick one cell of the sheet.
                o.uv = v.uv * _SheetRect.zw + _SheetRect.xy;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half lum = max(t.r, max(t.g, t.b));
                half mask = saturate((lum - _Floor) / (1.0 - _Floor));
                float2 cell = frac(i.uv * _SheetTiles);
                float2 border = smoothstep(0.0, 0.07, cell) * smoothstep(0.0, 0.07, 1.0 - cell);
                mask *= border.x * border.y;

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half fade = saturate((sceneEye - i.screenPos.w) / max(0.01, _SoftFade));

                if (_AlphaMode > 0.5)
                {
                    half a = mask * i.color.a * fade;
                    return half4(i.color.rgb * lerp(0.55, 1.15, lum), a);
                }
                half3 c = t.rgb * i.color.rgb * _Gain * mask * i.color.a * fade;
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
