// SoulFishGlow.hlsl
// Custom Function node — screen-space glow circles at each soul fish world position
// as it travels through the supernatural hoover.
//
// Chain this after the SuckingVFX node in the shader graph.
// Gate with GlowEnabled=1 on the hoover material, 0 on the air-suction material.
//
// Inputs
//   ScreenPos      float4  — Screen Position node (Default mode)
//   GlowRadius     float   — Circle radius, fraction of screen height (e.g. 0.08)
//   GlowSoftness   float   — Edge feather width, same units as GlowRadius (e.g. 0.04)
//   GlowIntensity  float   — Brightness multiplier (e.g. 3)
//
// Outputs
//   GlowOut        float   — Accumulated glow intensity; multiply by emission colour and add to Emission

#ifndef SOUL_FISH_GLOW_INCLUDED
#define SOUL_FISH_GLOW_INCLUDED
#define HOOVER_MAX_FISH 8
float4 _HooverFishPoints[HOOVER_MAX_FISH];
float  _HooverFishCount;
#endif

void SoulFishGlow_float(
    float4 ScreenPos,
    float  GlowRadius,
    float  GlowSoftness,
    float  GlowIntensity,
    out float GlowOut
)
{
    GlowOut = 0.0;

    // ScreenPos from Default mode: xy/w gives 0..1 viewport UV
    float2 fragUV = ScreenPos.xy / max(ScreenPos.w, 0.0001);
    float  aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);

    int count = clamp((int)_HooverFishCount, 0, HOOVER_MAX_FISH);
    for (int i = 0; i < count; i++)
    {
        float4 clip = mul(UNITY_MATRIX_VP, float4(_HooverFishPoints[i].xyz, 1.0));
        if (clip.w <= 0.001) continue;

        float2 fishUV = clip.xy / clip.w * 0.5 + 0.5;
        #if defined(UNITY_UV_STARTS_AT_TOP)
        if (_ProjectionParams.x < 0.0) fishUV.y = 1.0 - fishUV.y;
        #endif

        float2 diff  = fragUV - fishUV;
        float  dist  = length(float2(diff.x * aspect, diff.y));
        float  inner = max(GlowRadius - GlowSoftness, 0.0);
        float  glow  = 1.0 - smoothstep(inner, max(GlowRadius, inner + 0.0001), dist);

        GlowOut += glow;
    }

    GlowOut = saturate(GlowOut) * GlowIntensity;
}

void SoulFishGlow_half(
    half4 ScreenPos,
    half  GlowRadius,
    half  GlowSoftness,
    half  GlowIntensity,
    out half GlowOut
)
{
    GlowOut = 0.0h;

    half2 fragUV = ScreenPos.xy / max(ScreenPos.w, 0.0001h);
    half  aspect = (half)(_ScreenParams.x / max(_ScreenParams.y, 1.0));

    int count = clamp((int)_HooverFishCount, 0, HOOVER_MAX_FISH);
    for (int i = 0; i < count; i++)
    {
        float4 clip = mul(UNITY_MATRIX_VP, float4(_HooverFishPoints[i].xyz, 1.0));
        if (clip.w <= 0.001) continue;

        half2 fishUV = (half2)(clip.xy / clip.w * 0.5 + 0.5);
        #if defined(UNITY_UV_STARTS_AT_TOP)
        if (_ProjectionParams.x < 0.0) fishUV.y = 1.0h - fishUV.y;
        #endif

        half2 diff  = fragUV - fishUV;
        half  dist  = length(half2(diff.x * aspect, diff.y));
        half  inner = max(GlowRadius - GlowSoftness, 0.0h);
        half  glow  = 1.0h - smoothstep(inner, max(GlowRadius, inner + 0.0001h), dist);

        GlowOut += glow;
    }

    GlowOut = saturate(GlowOut) * GlowIntensity;
}
