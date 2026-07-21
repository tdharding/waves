// WaterSurfaceHeight.hlsl
// Custom Function node name: WaterSurfaceHeight
// ─────────────────────────────────────────────────────────────────────────────
// Reconstructs the world-space water surface height at any world position, using
// globals broadcast by WaveMaterialController.PublishSurfaceGlobals(). Lets solid
// objects in the water (walls, rocks) compute a per-pixel underwater "waterline"
// mask that stays glued to the same wavy surface the boat floats on.
//
// This mirrors WaveUtils.SampleWave / SonarWaveDistort EXACTLY (stepped XZ distance,
// same sine, _WavePhase driven), minus whirlpools — they don't affect this effect.
//
// Inputs
//   WorldPos   Vector3  — the pixel/vertex Position node, World space
//   Feather    Float    — soft band (world units) below the line over which the
//                         mask ramps 0→1. Use a tiny value (~0.01) for a hard cut.
//
// Outputs
//   SurfaceY   Float    — world-space Y of the water surface at WorldPos.xz
//   BelowMask  Float    — 0 above the surface, 1 fully submerged (feathered near the line)
//
// Wire BelowMask into e.g. Lerp(BaseColor, 1 - BaseColor, BelowMask) for the invert,
// or drive a sonar tint. SurfaceY is exposed for custom band/foam logic if wanted.
// ─────────────────────────────────────────────────────────────────────────────

#ifndef WATER_SURFACE_GLOBALS_DECLARED
#define WATER_SURFACE_GLOBALS_DECLARED
float4 _WaterSurfaceOrigin;   // xyz = world origin; y = flat base surface height
float  _WaterSurfaceScale;    // water plane localScale.x
float  _WaterSurfaceFreq;     // _Frequency
float  _WaterSurfaceRipple;   // _RippleDepth
float  _WaterSurfaceStepRate; // _WaveStepRate
#endif

#ifndef WAVE_PHASE_DECLARED
#define WAVE_PHASE_DECLARED
float _WavePhase;             // shared accumulated phase (WaveMaterialController / tuner)
#endif

void WaterSurfaceHeight_float(
    float3 WorldPos,
    float  Feather,
    out float SurfaceY,
    out float BelowMask)
{
    float safeScale = max(_WaterSurfaceScale, 1e-4);

    float2 toOrigin = (WorldPos.xz - _WaterSurfaceOrigin.xz) / safeScale;
    float  dist     = length(toOrigin);
    float  stepped  = floor(dist * _WaterSurfaceStepRate) / max(_WaterSurfaceStepRate, 1e-4);
    float  wave     = sin(stepped * _WaterSurfaceFreq - _WavePhase) * _WaterSurfaceRipple * safeScale;

    SurfaceY  = _WaterSurfaceOrigin.y - wave;
    BelowMask = saturate((SurfaceY - WorldPos.y) / max(Feather, 1e-4));
}

void WaterSurfaceHeight_half(
    half3 WorldPos,
    half  Feather,
    out half SurfaceY,
    out half BelowMask)
{
    half safeScale = max((half)_WaterSurfaceScale, 1e-4h);

    half2 toOrigin = (WorldPos.xz - (half2)_WaterSurfaceOrigin.xz) / safeScale;
    half  dist     = length(toOrigin);
    half  stepped  = floor(dist * (half)_WaterSurfaceStepRate) / max((half)_WaterSurfaceStepRate, 1e-4h);
    half  wave     = sin(stepped * (half)_WaterSurfaceFreq - (half)_WavePhase) * (half)_WaterSurfaceRipple * safeScale;

    SurfaceY  = (half)_WaterSurfaceOrigin.y - wave;
    BelowMask = saturate((SurfaceY - WorldPos.y) / max(Feather, 1e-4h));
}
