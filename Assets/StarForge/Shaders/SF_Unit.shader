// SF_Unit — the lit shader for every unit and structure.
//
// The models are flat-coloured plates; what makes them read as worn machines
// is layered here, from the mesh itself and the ambient occlusion the Blender
// exporter bakes into vertex colour R:
//
//  * panel detail: the original armour photograph, triplanar in object space so
//    it stays glued to a turning unit, as an x2 detail multiply and normal;
//  * edge wear: paint chipped back to bare metal along the bevels, found by
//    screen-space curvature (normals turn fast across a bevel and not at all
//    across a plate; vertex colours cannot tell the two apart, because a
//    plate's corners are the bevel's vertices) and broken up by the detail
//    texture so it reads as chips rather than a stroke;
//  * grime: dust climbing up from the ground and dirt settling in cavities;
//  * rim light: a cool sky rim that keeps silhouettes readable over the ground.
//
// UnitView drives per-renderer state through a property block: _Damage chars
// the surface and, when heavy, opens glowing seams; _BuildLevel shows the
// unbuilt part of a structure above that line as a team-coloured hologram with
// a bright weld line; _FlashColor is the hit flash; _Burn scorches a dying
// unit or glowing debris. None of it uses discard, which would switch
// off early depth testing on Apple GPUs for every unit using the shader.
Shader "StarForge/Unit"
{
    Properties
    {
        _BaseColor("Base colour", Color) = (0.5, 0.5, 0.5, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.45
        _Metallic("Metallic", Range(0, 1)) = 0.3
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 1)

        _DetailAlbedoMap("Detail albedo (x2)", 2D) = "linearGrey" {}
        _DetailAlbedoMapScale("Detail albedo strength", Range(0, 2)) = 0.75
        _DetailNormalMap("Detail normal", 2D) = "bump" {}
        _DetailNormalMapScale("Detail normal strength", Range(0, 2)) = 0.6
        _DetailTiling("Detail tiling (repeats per metre)", Float) = 0.5

        _WearColor("Edge wear colour", Color) = (0.62, 0.60, 0.56, 1)
        _WearAmount("Edge wear", Range(0, 1)) = 0.6
        _WearCurvature("Edge wear curvature scale", Float) = 0.05
        _GrimeColor("Grime colour", Color) = (0.16, 0.13, 0.10, 1)
        _GrimeAmount("Grime", Range(0, 1)) = 0.5
        _GrimeHeight("Grime height (m)", Float) = 0.9
        _AOStrength("Baked AO strength", Range(0, 1)) = 0.85
        _RimColor("Rim light", Color) = (0.30, 0.40, 0.55, 1)

        [HideInInspector] _Damage("Damage", Range(0, 1)) = 0
        [HideInInspector] _BuildLevel("Construction line, object-space metres", Float) = 100000
        [HideInInspector] _FlashColor("Hit flash", Color) = (0, 0, 0, 0)
        [HideInInspector] _Burn("Burn", Range(0, 1)) = 0
        [HideInInspector] _TeamGlow("Team glow", Color) = (0.1, 0.5, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Smoothness;
            half _Metallic;
            half4 _EmissionColor;
            float4 _DetailAlbedoMap_ST;
            half _DetailAlbedoMapScale;
            half _DetailNormalMapScale;
            float _DetailTiling;
            half4 _WearColor;
            half _WearAmount;
            float _WearCurvature;
            half4 _GrimeColor;
            half _GrimeAmount;
            float _GrimeHeight;
            half _AOStrength;
            half4 _RimColor;
            half _Damage;
            float _BuildLevel;
            half4 _FlashColor;
            half _Burn;
            half4 _TeamGlow;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DetailAlbedoMap); SAMPLER(sampler_DetailAlbedoMap);
            TEXTURE2D(_DetailNormalMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half3  normalOS   : TEXCOORD3;
                half4  color      : TEXCOORD4;
                half4  fogLight   : TEXCOORD5;   // x fog, yzw vertex lighting
            };

            float SFHash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float SFNoise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(SFHash3(i), SFHash3(i + float3(1, 0, 0)), f.x),
                                 lerp(SFHash3(i + float3(0, 1, 0)), SFHash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(SFHash3(i + float3(0, 0, 1)), SFHash3(i + float3(1, 0, 1)), f.x),
                                 lerp(SFHash3(i + float3(0, 1, 1)), SFHash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.color = v.color;
                o.fogLight.x = ComputeFogFactor(p.positionCS.z);
                o.fogLight.yzw = VertexLighting(p.positionWS, o.normalWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 nOS = normalize(i.normalOS);
                float3 tp = i.positionOS * _DetailTiling;
                half3 w = abs(nOS);
                w *= w; w *= w;
                w /= max(1e-4, w.x + w.y + w.z);

                // Triplanar panel detail in object space.
                half3 detail = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, tp.zy).rgb * w.x
                             + SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, tp.xz).rgb * w.y
                             + SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, tp.xy).rgb * w.z;
                half detailLum = dot(detail, half3(0.299, 0.587, 0.114));
                // Normalise the detail multiply by the photograph's own average (its
                // smallest mip), so it adds grain and panelling around the calibrated
                // albedo instead of darkening it: the armour plate photograph averages
                // far below mid-grey, and multiplying by it raw sank every hull.
                half3 detailAvg = SAMPLE_TEXTURE2D_LOD(_DetailAlbedoMap, sampler_DetailAlbedoMap, float2(0.5, 0.5), 12).rgb;
                half3 albedo = _BaseColor.rgb * LerpWhiteTo(detail / max(detailAvg, 0.02), _DetailAlbedoMapScale);

                half3 tnX = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailAlbedoMap, tp.zy), _DetailNormalMapScale);
                half3 tnY = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailAlbedoMap, tp.xz), _DetailNormalMapScale);
                half3 tnZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailAlbedoMap, tp.xy), _DetailNormalMapScale);
                tnX = half3(tnX.xy + nOS.zy, abs(tnX.z) * nOS.x);
                tnY = half3(tnY.xy + nOS.xz, abs(tnY.z) * nOS.y);
                tnZ = half3(tnZ.xy + nOS.xy, abs(tnZ.z) * nOS.z);
                half3 n = TransformObjectToWorldNormal(normalize(tnX.zyx * w.x + tnY.xzy * w.y + tnZ.xyz * w.z));

                half ao = lerp(1.0, i.color.r, _AOStrength);
                half metallic = _Metallic;
                half smooth = _Smoothness;

                // Edge wear on the bevels, kept out of cavities, chipped by the detail texture.
                half curvature = length(fwidth(i.normalWS)) / max(length(fwidth(i.positionWS)), 1e-4);
                half edge = saturate(curvature * _WearCurvature);
                half wear = saturate((edge - 0.35 + (detailLum - 0.45) * 1.4) * 2.5) * _WearAmount
                          * smoothstep(0.55, 0.85, i.color.r);
                albedo = lerp(albedo, _WearColor.rgb * (0.7 + detailLum * 0.6), wear);
                metallic = lerp(metallic, 0.85, wear);
                smooth = lerp(smooth, 0.55, wear);

                // Dust climbing from the ground, dirt in the cavities.
                half ground = 1.0 - smoothstep(0.0, _GrimeHeight, i.positionOS.y);
                half cavity = saturate((1.0 - i.color.r) * 1.5);
                half grime = saturate((ground * 0.8 + cavity * 0.6) * (0.6 + (0.5 - detailLum) * 1.2)) * _GrimeAmount * (1.0 - wear);
                albedo = lerp(albedo, _GrimeColor.rgb, grime);
                smooth *= 1.0 - grime * 0.6;

                half3 emission = _EmissionColor.rgb + _FlashColor.rgb;

                if (_Damage > 0.001 || _Burn > 0.001)
                {
                    half nse = SFNoise3(i.positionOS * 1.9) * 0.65 + detailLum * 0.35;
                    half d = saturate(max(_Damage, _Burn));
                    half charred = smoothstep(1.0 - d, 1.0 - d + 0.12, nse);
                    albedo = lerp(albedo, half3(0.02, 0.018, 0.016), charred * 0.85);
                    smooth *= 1.0 - charred * 0.8;
                    metallic *= 1.0 - charred * 0.5;
                    // Heavy damage opens glowing seams; burning units glow all over.
                    half seam = saturate(1.0 - abs(nse - (1.0 - d * 0.8)) * 18.0) * smoothstep(0.45, 0.8, d);
                    half flicker = 2.0 + sin(_Time.y * 7.0 + i.positionOS.x * 3.0);
                    emission += half3(1.0, 0.35, 0.08) * (seam * flicker + charred * _Burn * _Burn * 2.5);
                }

                if (_BuildLevel < 9999.0)
                {
                    float level = _BuildLevel;
                    half y = i.positionOS.y;
                    half above = smoothstep(level - 0.05, level + 0.05, y);
                    half scan = 0.5 + 0.5 * sin(y * 40.0 - _Time.y * 6.0);
                    half weld = saturate(1.0 - abs(y - level) * 6.0);
                    albedo *= lerp(1.0, 0.2, above);
                    smooth = lerp(smooth, 0.9, above);
                    emission += _TeamGlow.rgb * (above * (0.2 + 0.6 * scan * scan) + weld * 3.0);
                }

                half3 viewWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half rim = pow(1.0 - saturate(dot(n, viewWS)), 4.0);
                emission += _RimColor.rgb * rim * 0.35;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogLight.x);
                inputData.vertexLighting = i.fogLight.yzw;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = metallic;
                s.smoothness = smooth;
                s.occlusion = ao;
                s.emission = emission;
                s.normalTS = half3(0, 0, 1);
                s.alpha = 1.0;

                half4 col = UniversalFragmentPBR(inputData, s);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                return half4(col.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SFShadowVert
            #pragma fragment SFShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "SFPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SFDepthVert
            #pragma fragment SFDepthFrag
            #include "SFPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SFDepthVert
            #pragma fragment SFDepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "SFPasses.hlsl"
            ENDHLSL
        }
    }
    Fallback Off
}
