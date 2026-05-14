// SonarVertexDisplace.hlsl
// Custom Function node name: SonarVertexDisplace
// ─────────────────────────────────────────────────────────────────────────────
// Plug into the VERTEX block of the Master Stack.
// Remove PulseOrigin as a node input — origins come from the global array.
//
// Inputs
//   VertexWorldPos   Vector3  — Position node, World space
//   PulseRadius      Float    — _PulseRadius property
//   PulseWidth       Float    — _PulseWidth property
//   RadiusOffset     Float    — _DisplaceRadiusOffset property
//   Strength         Float    — _DisplaceStrength property
//
// Output
//   Offset           Vector3  — ADD to Position (World), Transform World→Object,
//                               connect to Vertex Position
// ─────────────────────────────────────────────────────────────────────────────

#ifndef SONAR_PULSE_ORIGINS_INCLUDED
#define SONAR_PULSE_ORIGINS_INCLUDED
#define SONAR_MAX_ORIGINS 8
float4 _PulseOrigins[SONAR_MAX_ORIGINS];
float  _PulseOriginCount;
#endif

void SonarVertexDisplace_float(
    float3 VertexWorldPos,
    float  PulseRadius,
    float  PulseWidth,
    float  RadiusOffset,
    float  Strength,
    out float3 Offset
)
{
    float effectRadius = PulseRadius + RadiusOffset;
    float feather      = max(PulseWidth, 0.001);
    float innerEdge    = max(0.001, effectRadius - feather);

    Offset = float3(0, 0, 0);

    int count = clamp((int)_PulseOriginCount, 0, SONAR_MAX_ORIGINS);
    for (int i = 0; i < count; i++)
    {
        float3 origin = _PulseOrigins[i].xyz;
        float  dist   = distance(VertexWorldPos, origin);
        float  mask   = 1.0 - smoothstep(innerEdge, effectRadius, dist);

        float3 dir = dist > 0.001 ? normalize(VertexWorldPos - origin)
                                  : float3(0, 1, 0);

        // Accumulate — each origin adds its own displacement bump
        Offset += dir * mask * Strength;
    }
}

void SonarVertexDisplace_half(
    half3 VertexWorldPos,
    half  PulseRadius,
    half  PulseWidth,
    half  RadiusOffset,
    half  Strength,
    out half3 Offset
)
{
    half effectRadius = PulseRadius + RadiusOffset;
    half feather      = max(PulseWidth, 0.001h);
    half innerEdge    = max(0.001h, effectRadius - feather);

    Offset = half3(0, 0, 0);

    int count = clamp((int)_PulseOriginCount, 0, SONAR_MAX_ORIGINS);
    for (int i = 0; i < count; i++)
    {
        half3 origin = (half3)_PulseOrigins[i].xyz;
        half  dist   = distance(VertexWorldPos, origin);
        half  mask   = 1.0h - smoothstep(innerEdge, effectRadius, dist);

        half3 dir = dist > 0.001h ? normalize(VertexWorldPos - origin)
                                  : half3(0, 1, 0);

        Offset += dir * mask * Strength;
    }
}
