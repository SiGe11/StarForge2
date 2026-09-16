// SFPasses.hlsl — shadow, depth and depth-normal passes shared by the
// StarForge shaders that only need position + normal.
#ifndef SF_PASSES_INCLUDED
#define SF_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

struct SFSimpleAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
};

struct SFShadowVaryings
{
    float4 positionCS : SV_POSITION;
};

float3 _LightDirection;
float3 _LightPosition;

SFShadowVaryings SFShadowVert(SFSimpleAttributes v)
{
    SFShadowVaryings o;
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    o.positionCS = ApplyShadowClamping(positionCS);
    return o;
}

half4 SFShadowFrag(SFShadowVaryings i) : SV_Target
{
    return 0;
}

struct SFDepthVaryings
{
    float4 positionCS : SV_POSITION;
    half3  normalWS   : TEXCOORD0;
};

SFDepthVaryings SFDepthVert(SFSimpleAttributes v)
{
    SFDepthVaryings o;
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    return o;
}

half SFDepthFrag(SFDepthVaryings i) : SV_Target
{
    return i.positionCS.z;
}

half4 SFDepthNormalsFrag(SFDepthVaryings i) : SV_Target
{
#if defined(_GBUFFER_NORMALS_OCT)
    float3 normalWS = normalize(i.normalWS);
    float2 oct = PackNormalOctQuadEncode(normalWS);
    float2 remapped = saturate(oct * 0.5 + 0.5);
    return half4(PackFloat2To888(remapped), 0.0);
#else
    return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
#endif
}

#endif
