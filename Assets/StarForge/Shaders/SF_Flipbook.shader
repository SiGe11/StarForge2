// SF_Flipbook — fire and explosions from simulated flipbooks (fx_explosion.png,
// fx_flame.png: JangaFX's EmberGen simulations rendered by
// Tools/blender/render_flipbooks.py).
//
// The old sheets were bright-on-black photographs drawn additively, so a fireball's
// smoke could never darken anything and every explosion read as a flat orange blob.
// These sheets carry four data channels, premultiplied by coverage:
//
//   R  the smoke lit by a sun overhead     B  the smoke lit by the sky alone
//   G  the fire's own light (sqrt)         A  coverage
//
// so the smoke is relit here by the scene's sun and sky, the fire is coloured by its
// heat (dark red at the fringe, orange, yellow-white in the core, pushed into HDR so
// bloom picks the core up) and the two are composited premultiplied: Blend One
// OneMinusSrcAlpha. Frames crossfade through the particle system's animation blend
// stream (FXDirector.FlipbookStreams: TEXCOORD0 = uv of this frame and the next,
// TEXCOORD1.x the blend), so a short-lived particle does not step through its cells.
Shader "StarForge/Flipbook"
{
    Properties
    {
        _MainTex("Sheet (R sun, G fire, B sky, A coverage)", 2D) = "black" {}
        _SmokeColor("Smoke albedo", Color) = (0.38, 0.36, 0.34, 1)
        _SunGain("Sun on smoke", Float) = 1.6
        _SkyGain("Sky on smoke", Float) = 1.2
        _FireGain("Fire brightness (HDR)", Float) = 5
        _FireCool("Fire colour, cool fringe", Color) = (0.85, 0.16, 0.03, 1)
        _FireMid("Fire colour, body", Color) = (1.0, 0.46, 0.10, 1)
        _FireHot("Fire colour, core", Color) = (1.0, 0.86, 0.58, 1)
        _Coverage("Coverage multiplier", Range(0, 2)) = 1
        _SoftFade("Soft fade distance", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Flipbook"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
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
                half4 _SmokeColor;
                half _SunGain;
                half _SkyGain;
                half _FireGain;
                half4 _FireCool;
                half4 _FireMid;
                half4 _FireHot;
                half _Coverage;
                half _SoftFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float4 uv         : TEXCOORD0;   // xy this frame, zw the next
                float  blend      : TEXCOORD1;   // how far between them
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float4 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  blend      : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                o.blend = v.blend;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
                half4 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw);
                half4 t = lerp(a, b, saturate(i.blend));

                Light L = GetMainLight();
                // A low sun lights the smoke less from above; the sky term keeps its form.
                half high = saturate(L.direction.y * 1.4 + 0.2);
                half3 sky = SampleSH(half3(0, 1, 0));
                half3 smoke = (t.r * _SunGain * L.color * high + t.b * _SkyGain * sky) * _SmokeColor.rgb;

                // The fire: squared back to linear, coloured by how hot it is.
                half e = t.g * t.g;
                half3 heat = e < 0.5 ? lerp(_FireCool.rgb, _FireMid.rgb, e * 2.0)
                                     : lerp(_FireMid.rgb, _FireHot.rgb, e * 2.0 - 1.0);
                half3 fire = heat * e * _FireGain;

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half soft = saturate((sceneEye - i.screenPos.w) / max(0.01, _SoftFade));
                half nearCam = saturate((i.screenPos.w - 2.0) / 4.0);
                half f = i.color.a * soft * nearCam;

                half cover = saturate(t.a * _Coverage);
                // Smoke and coverage scale together, so a thinner puff stays premultiplied.
                half3 rgb = (smoke * _Coverage * i.color.rgb + fire * i.color.rgb) * f;
                return half4(rgb, cover * f);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
