// SonarGrid.hlsl
// Custom Function node name: SonarGrid
// ─────────────────────────────────────────────────────────────────────────────
// _PulseOrigins and _PulseOriginCount are global uniforms — do NOT add
// PulseOrigin as a node input. Remove it from the Custom Function node if
// it was previously wired in.
//
// Inputs
//   UV             Vector2   — UV node
//   WorldPos       Vector3   — Position node, World space
//   PulseRadius    Float     — _PulseRadius property
//   PulseWidth     Float     — _PulseWidth property
//   GridDensity    Float     — _GridDensity property
//   LineWidth      Float     — _LineWidth property
//   SpaceBaseAlpha Float     — _SpaceBaseAlpha property
//
// Outputs
//   LinesMask      Float     — 1 on grid lines, 0 in spaces
//   PulseMask      Float     — max contribution across all pulse origins
//   Alpha          Float     — lines = 1, spaces boosted by pulse
// ─────────────────────────────────────────────────────────────────────────────

// Global array — set from C# via material.SetVectorArray("_PulseOrigins", ...)
// Include guard prevents double-declaration when both HLSL files are in the same shader
#ifndef SONAR_PULSE_ORIGINS_INCLUDED
#define SONAR_PULSE_ORIGINS_INCLUDED
#define SONAR_MAX_ORIGINS 8
float4 _PulseOrigins[SONAR_MAX_ORIGINS];
float  _PulseOriginCount;
#endif

void SonarGrid_float(
    float2 UV,
    float3 WorldPos,
    float  PulseRadius,
    float  PulseWidth,
    float  GridDensity,
    float  LineWidth,
    float  SpaceBaseAlpha,
    float2 GridOffset,
    out float LinesMask,
    out float PulseMask,
    out float Alpha
)
{
    // ── grid lines ────────────────────────────────────────────────────────────
    float2 scaled      = frac(UV * GridDensity + GridOffset);
    float2 distToEdge  = min(scaled, 1.0 - scaled);
    float  nearestLine = min(distToEdge.x, distToEdge.y);

    float lineAA = fwidth(nearestLine) * 1.5;
    LinesMask = 1.0 - smoothstep(LineWidth - lineAA, LineWidth + lineAA, nearestLine);

    // ── multi-origin feathered disc ───────────────────────────────────────────
    float feather = max(PulseWidth, 0.001);
    float combined = 0.0;

    int count = clamp((int)_PulseOriginCount, 0, SONAR_MAX_ORIGINS);
    for (int i = 0; i < count; i++)
    {
        float dist = distance(WorldPos, _PulseOrigins[i].xyz);
        float mask = 1.0 - smoothstep(PulseRadius - feather, PulseRadius, dist);
        combined = max(combined, mask);   // brightest origin wins
    }
    PulseMask = combined;

    // ── combined alpha ────────────────────────────────────────────────────────
    float spaceAlpha = saturate(SpaceBaseAlpha + PulseMask);
    Alpha = saturate(LinesMask + (1.0 - LinesMask) * spaceAlpha);
}

void SonarGrid_half(
    half2 UV,
    half3 WorldPos,
    half  PulseRadius,
    half  PulseWidth,
    half  GridDensity,
    half  LineWidth,
    half  SpaceBaseAlpha,
    half2 GridOffset,
    out half LinesMask,
    out half PulseMask,
    out half Alpha
)
{
    half2 scaled      = frac(UV * GridDensity + GridOffset);
    half2 distToEdge  = min(scaled, 1.0h - scaled);
    half  nearestLine = min(distToEdge.x, distToEdge.y);

    half lineAA = fwidth(nearestLine) * 1.5h;
    LinesMask = 1.0h - smoothstep(LineWidth - lineAA, LineWidth + lineAA, nearestLine);

    half feather  = max(PulseWidth, 0.001h);
    half combined = 0.0h;

    int count = clamp((int)_PulseOriginCount, 0, SONAR_MAX_ORIGINS);
    for (int i = 0; i < count; i++)
    {
        half dist = distance(WorldPos, (half3)_PulseOrigins[i].xyz);
        half mask = 1.0h - smoothstep(PulseRadius - feather, PulseRadius, dist);
        combined  = max(combined, mask);
    }
    PulseMask = combined;

    half spaceAlpha = saturate(SpaceBaseAlpha + PulseMask);
    Alpha = saturate(LinesMask + (1.0h - LinesMask) * spaceAlpha);
}
