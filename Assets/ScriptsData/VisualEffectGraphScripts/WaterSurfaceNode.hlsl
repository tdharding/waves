// WaterSurfaceNode.hlsl
// Custom Function node name: WaterSurface
// ─────────────────────────────────────────────────────────────────────────────
// The vertex-stage entry point for the water. Replaces WavesAndWhirlpools.
//
// Feed it the object-space Position of the water plane. It returns the displaced
// position plus everything the fragment stage used to recompute for itself:
// the gradient (real normals), the crest signal (peak/trough brightness) and the
// normalized slope (twirl). Nothing downstream needs to know the wave formula.
//
// The boat wake is NOT folded in here — BoatWakeDisplacement stays its own node
// in the chain, since that effect is being rethought separately.
//
// Inputs
//   PositionIn       Vector3  Position node, Object space
//   WhirlpoolCount   Float    _WhirlpoolCount
//   WhirlpoolDepth   Float    _WhirlpoolDepth
//   WhirlpoolSwirl   Float    _WhirlpoolSwirl
//   WhirlpoolTaper   Float    _WhirlpoolTaper
//
// Outputs
//   PositionOut      Vector3  displaced object-space position -> Vertex Position
//   Gradient         Vector2  dh/dx, dh/dy -> WaterNormal
//   Height           Float    world-up offset from waves alone (no whirlpools)
//   Crest            Float    -1..1 wave signal -> WavePeaksTroughs
//   Slope            Float    0..1 steepness -> WaveTwirlNormal
// ─────────────────────────────────────────────────────────────────────────────

#ifndef WATER_SURFACE_NODE_INCLUDED
#define WATER_SURFACE_NODE_INCLUDED

#include "WaterSurface.hlsl"

void WaterSurface_float(
    float3 PositionIn,
    float  WhirlpoolCount,
    float  WhirlpoolDepth,
    float  WhirlpoolSwirl,
    float  WhirlpoolTaper,
    out float3 PositionOut,
    out float2 Gradient,
    out float  Height,
    out float  Crest,
    out float  Slope)
{
    WaterSample s = SampleWater(
        PositionIn.xy,
        WhirlpoolCount, WhirlpoolDepth, WhirlpoolSwirl, WhirlpoolTaper);

    PositionOut = PositionIn + s.Displacement;
    Gradient    = s.Gradient;
    Height      = s.Height;
    Crest       = s.Crest;
    Slope       = s.Slope;
}

void WaterSurface_half(
    half3 PositionIn,
    half  WhirlpoolCount,
    half  WhirlpoolDepth,
    half  WhirlpoolSwirl,
    half  WhirlpoolTaper,
    out half3 PositionOut,
    out half2 Gradient,
    out half  Height,
    out half  Crest,
    out half  Slope)
{
    float3 p; float2 g; float h; float c; float sl;
    WaterSurface_float(
        (float3)PositionIn,
        (float)WhirlpoolCount, (float)WhirlpoolDepth,
        (float)WhirlpoolSwirl, (float)WhirlpoolTaper,
        p, g, h, c, sl);

    PositionOut = (half3)p;
    Gradient    = (half2)g;
    Height      = (half)h;
    Crest       = (half)c;
    Slope       = (half)sl;
}

#endif // WATER_SURFACE_NODE_INCLUDED
