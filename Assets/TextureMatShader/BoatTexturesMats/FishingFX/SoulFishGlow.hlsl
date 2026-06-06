// SoulFishGlow.hlsl
// Custom Function node name: SoulFishGlow
// ─────────────────────────────────────────────────────────────────────────────
// Globals provided by FishingController.cs via Shader.SetGlobalVectorArray
//
// Inputs
//   ViewDir      Vector3  — View Direction node (World space)
//   GlowRadius   Float    — Glow cone half-angle as (1 - cos), e.g. 0.02
//   GlowSoftness Float    — Feather width within cone, e.g. 0.005
//   GlowIntensity Float   — Brightness multiplier
//
// Output
//   GlowOut      Float    — 0..1 mask, use as Base Color / multiply by colour
// ─────────────────────────────────────────────────────────────────────────────

#ifndef SOUL_FISH_GLOW_INCLUDED
#define SOUL_FISH_GLOW_INCLUDED

#define HOOVER_MAX_FISH 8
float4 _HooverFishPoints[HOOVER_MAX_FISH];
float  _HooverFishCount;

void SoulFishGlow_float(
    float3 ViewDir,
    float  GlowRadius,    // World-space radius of glow circle around fish
    float  GlowSoftness,  // World-space feather width beyond GlowRadius
    float  GlowIntensity,
    out float GlowOut
)
{
    GlowOut = 0.0;

    // ViewDir is fragment→camera; negate for camera→fragment direction
    float3 camToFrag = normalize(-ViewDir);

    int count = clamp((int)_HooverFishCount, 0, HOOVER_MAX_FISH);
    for (int i = 0; i < count; i++)
    {
        float3 toFish   = _HooverFishPoints[i].xyz - _WorldSpaceCameraPos.xyz;
        float  fishDist = length(toFish);
        float3 camToFish = toFish / max(fishDist, 0.0001);

        float cosAngle = dot(camToFish, camToFrag);
        if (cosAngle <= 0.0) continue;

        // Convert world-space radius to angular cosine threshold at this fish's distance.
        // cos(atan(r/d)) ≈ d/sqrt(d²+r²), exact for any angle.
        float cosInner = fishDist / sqrt(fishDist * fishDist + GlowRadius * GlowRadius);
        float outerR   = GlowRadius + max(GlowSoftness, 0.0001);
        float cosOuter = fishDist / sqrt(fishDist * fishDist + outerR * outerR);

        // cosAngle → 1.0 = fragment aligned with fish; cosInner > cosOuter
        float mask = smoothstep(cosOuter, cosInner, cosAngle);
        GlowOut = max(GlowOut, mask);
    }

    GlowOut *= GlowIntensity;
}

void SoulFishGlow_half(
    half3 ViewDir,
    half  GlowRadius,
    half  GlowSoftness,
    half  GlowIntensity,
    out half GlowOut
)
{
    float res;
    SoulFishGlow_float((float3)ViewDir, (float)GlowRadius, (float)GlowSoftness, (float)GlowIntensity, res);
    GlowOut = (half)res;
}

#endif
