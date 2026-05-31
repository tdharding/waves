// SonarWaveDistort.hlsl
// Custom Function node name: SonarWaveDistort
// ─────────────────────────────────────────────────────────────────────────────
// World-space wave ripple displacement for the sonar grid.
// Whirlpool displacement is handled separately by SonarWhirlpoolDisplace.hlsl.
//
// Inputs
//   VertexWorldPos   Vector3  — Position node, World space
//   VertexWorldNormal Vector3 — Normal node, World space (drives displacement axis)
//   WaveOrigin       Vector3  — _WaveOrigin  (waterTransform.position)
//   MeshScale        Float    — _WaveMeshScale (waterTransform.localScale.x)
//   WaveStepRate     Float    — _WaveStepRate
//   Frequency        Float    — _WaveFrequency
//   Speed            Float    — _WaveSpeed
//   RippleDepth      Float    — _WaveRipple
//   WaveStrength     Float    — _SonarWaveStrength (0–1, scales down ripple only)
//   Time             Float    — Time node
//
// Output
//   Offset           Vector3  — ADD to other offsets before Transform World→Object
//
// Displacement is along the vertex world normal so all three grid orientations
// (Horizontal/Vertical/CrossVertical) receive an outward ripple perpendicular
// to their face. Previously Offset.y was hardcoded, which left CrossVertical
// planes (normal = ±X) with only tangential Y movement and no visible ripple.
// ─────────────────────────────────────────────────────────────────────────────

void SonarWaveDistort_float(
    float3 VertexWorldPos,
    float3 VertexWorldNormal,
    float3 WaveOrigin,
    float  MeshScale,
    float  WaveStepRate,
    float  Frequency,
    float  Speed,
    float  RippleDepth,
    float  WaveStrength,
    float  Time,
    out float3 Offset
)
{
    Offset = float3(0, 0, 0);
    float safeScale = max(MeshScale, 0.0001);

    float2 toOrigin = (VertexWorldPos.xz - WaveOrigin.xz) / safeScale;
    float  dist     = length(toOrigin);
    float  stepped  = floor(dist * WaveStepRate) / max(WaveStepRate, 0.0001);
    float  wave     = sin(stepped * Frequency - Time * Speed) * RippleDepth * safeScale * WaveStrength;
    Offset -= normalize(VertexWorldNormal) * wave;
}

void SonarWaveDistort_half(
    half3 VertexWorldPos,
    half3 VertexWorldNormal,
    half3 WaveOrigin,
    half  MeshScale,
    half  WaveStepRate,
    half  Frequency,
    half  Speed,
    half  RippleDepth,
    half  WaveStrength,
    half  Time,
    out half3 Offset
)
{
    Offset = half3(0, 0, 0);
    half safeScale = max(MeshScale, 0.0001h);

    half2 toOrigin = (VertexWorldPos.xz - WaveOrigin.xz) / safeScale;
    half  dist     = length(toOrigin);
    half  stepped  = floor(dist * WaveStepRate) / max(WaveStepRate, 0.0001h);
    half  wave     = sin(stepped * Frequency - Time * Speed) * RippleDepth * safeScale * WaveStrength;
    Offset -= normalize(VertexWorldNormal) * wave;
}
