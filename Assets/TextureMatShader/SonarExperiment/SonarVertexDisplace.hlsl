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
float4 _LureOrigins[SONAR_MAX_ORIGINS];
float  _LureOriginCount;
#endif

void SonarVertexDisplace_float(
    float3 VertexWorldPos,
    float  PulseRadius,
    float  PulseWidth,
    float  RadiusOffset,
    float  Strength,
    float  LureRadius,
    float  LureStrength,
    out float3 Offset
)
{
    float effectRadius = PulseRadius + RadiusOffset;
    float feather      = max(PulseWidth, 0.001);
    float innerEdge    = max(0.001, effectRadius - feather);

    Offset = float3(0, 0, 0);

    // Fish pulse — ring displacement
    int count = clamp((int)_PulseOriginCount, 0, SONAR_MAX_ORIGINS);
    for (int i = 0; i < count; i++)
    {
        float3 origin = _PulseOrigins[i].xyz;
        float  dist   = distance(VertexWorldPos, origin);
        float  mask   = 1.0 - smoothstep(innerEdge, effectRadius, dist);

        float3 dir = dist > 0.001 ? normalize(VertexWorldPos - origin)
                                  : float3(0, 1, 0);
        Offset += dir * mask * Strength;
    }

    // Lure — full disc displacement
    int lureCount = clamp((int)_LureOriginCount, 0, SONAR_MAX_ORIGINS);
    for (int j = 0; j < lureCount; j++)
    {
        float3 lureOrigin = _LureOrigins[j].xyz;
        float  lureDist   = distance(VertexWorldPos, lureOrigin);
        float  lureMask   = 1.0 - smoothstep(0.0, max(LureRadius, 0.001), lureDist);

        float3 lureDir = lureDist > 0.001 ? normalize(VertexWorldPos - lureOrigin)
                                          : float3(0, 1, 0);
        Offset += lureDir * lureMask * LureStrength;
    }
}

void SonarVertexDisplace_half(
    half3 VertexWorldPos,
    half  PulseRadius,
    half  PulseWidth,
    half  RadiusOffset,
    half  Strength,
    half  LureRadius,
    half  LureStrength,
    out half3 Offset
)
{
    half effectRadius = PulseRadius + RadiusOffset;
    half feather      = max(PulseWidth, 0.001h);
    half innerEdge    = max(0.001h, effectRadius - feather);

    Offset = half3(0, 0, 0);

    // Fish pulse — ring displacement
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

    // Lure — full disc displacement
    int lureCount = clamp((int)_LureOriginCount, 0, SONAR_MAX_ORIGINS);
    for (int j = 0; j < lureCount; j++)
    {
        half3 lureOrigin = (half3)_LureOrigins[j].xyz;
        half  lureDist   = distance(VertexWorldPos, lureOrigin);
        half  lureMask   = 1.0h - smoothstep(0.0h, max(LureRadius, 0.001h), lureDist);

        half3 lureDir = lureDist > 0.001h ? normalize(VertexWorldPos - lureOrigin)
                                          : half3(0, 1, 0);
        Offset += lureDir * lureMask * LureStrength;
    }
}
