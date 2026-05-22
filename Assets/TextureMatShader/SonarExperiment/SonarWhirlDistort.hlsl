// SonarWhirlDistort.hlsl
// Custom Function node name: SonarWhirlDistort
// ─────────────────────────────────────────────────────────────────────────────
// Vertex-stage radial distort at the whirl mouth position.
// Same pattern as SonarVertexDisplace — 3D radial outward push within a
// feathered sphere. ADD this Offset to the SonarVertexDisplace Offset, then
// Transform World→Object and connect to Vertex Position.
//
// Inputs
//   VertexWorldPos   Vector3  — Position node, World space
//   MouthWorldPos    Vector3  — _MouthWorldPos property (Vector3)
//   WhirlRadius      Float    — _WhirlRadius property
//   PinchStrength    Float    — _PinchStrength property
//
// Output
//   Offset           Vector3  — ADD to SonarVertexDisplace Offset
// ─────────────────────────────────────────────────────────────────────────────

void SonarWhirlDistort_float(
    float3 VertexWorldPos,
    float3 MouthWorldPos,
    float  WhirlRadius,
    float  PinchStrength,
    out float3 Offset
)
{
    float  feather   = max(WhirlRadius * 0.4, 0.001);
    float  innerEdge = max(0.001, WhirlRadius - feather);

    float  dist = distance(VertexWorldPos, MouthWorldPos);
    float  mask = 1.0 - smoothstep(innerEdge, WhirlRadius, dist);

    float3 dir = dist > 0.001 ? normalize(VertexWorldPos - MouthWorldPos)
                              : float3(0, 1, 0);

    Offset = dir * mask * PinchStrength;
}

void SonarWhirlDistort_half(
    half3 VertexWorldPos,
    half3 MouthWorldPos,
    half  WhirlRadius,
    half  PinchStrength,
    out half3 Offset
)
{
    half  feather   = max(WhirlRadius * 0.4h, 0.001h);
    half  innerEdge = max(0.001h, WhirlRadius - feather);

    half  dist = distance(VertexWorldPos, MouthWorldPos);
    half  mask = 1.0h - smoothstep(innerEdge, WhirlRadius, dist);

    half3 dir = dist > 0.001h ? normalize(VertexWorldPos - MouthWorldPos)
                              : half3(0, 1, 0);

    Offset = dir * mask * PinchStrength;
}
