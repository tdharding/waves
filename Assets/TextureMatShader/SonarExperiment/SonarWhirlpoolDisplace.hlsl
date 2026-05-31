// SonarWhirlpoolDisplace.hlsl
// Custom Function node name: SonarWhirlpoolDisplace
// ─────────────────────────────────────────────────────────────────────────────
// Vertical funnel displacement at whirlpool positions for the sonar grid.
// Data fed by WhirlpoolManager.cs via Shader.SetGlobalFloat / SetGlobalVectorArray.
//
// Inputs
//   VertexWorldPos   Vector3  — Position node, World space
//
// Output
//   Offset           Vector3  — ADD to other offsets before Transform World→Object
// ─────────────────────────────────────────────────────────────────────────────

#ifndef SONAR_WHIRLPOOL_DISPLACE_DECLARED
#define SONAR_WHIRLPOOL_DISPLACE_DECLARED
#define SONAR_MAX_WHIRLPOOLS 8
float4 _SonarWhirlpoolPositions[SONAR_MAX_WHIRLPOOLS];
float  _SonarWhirlpoolCount;
float  _SonarWhirlpoolDepth;
float  _SonarWhirlpoolTaper;
#endif

void SonarWhirlpoolDisplace_float(
    float3 VertexWorldPos,
    out float3 Offset
)
{
    Offset = float3(0, 0, 0);

    int count = clamp((int)_SonarWhirlpoolCount, 0, SONAR_MAX_WHIRLPOOLS);
    for (int i = 0; i < count; i++)
    {
        float3 center = _SonarWhirlpoolPositions[i].xyz;
        float  radius = max(_SonarWhirlpoolPositions[i].w, 0.001);

        float2 toCenter = VertexWorldPos.xz - center.xz;
        float  wpDist   = length(toCenter);

        float  h       = 1.0 - saturate(wpDist / radius);
        float  ss      = h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
        float  falloff = pow(ss * ss, max(_SonarWhirlpoolTaper, 0.01));

        Offset.y -= falloff * _SonarWhirlpoolDepth;
    }
}

void SonarWhirlpoolDisplace_half(
    half3 VertexWorldPos,
    out half3 Offset
)
{
    Offset = half3(0, 0, 0);

    int count = clamp((int)_SonarWhirlpoolCount, 0, SONAR_MAX_WHIRLPOOLS);
    for (int i = 0; i < count; i++)
    {
        half3 center = (half3)_SonarWhirlpoolPositions[i].xyz;
        half  radius = max((half)_SonarWhirlpoolPositions[i].w, 0.001h);

        half2 toCenter = VertexWorldPos.xz - center.xz;
        half  wpDist   = length(toCenter);

        half  h       = 1.0h - saturate(wpDist / radius);
        half  ss      = h * h * h * (h * (h * 6.0h - 15.0h) + 10.0h);
        half  falloff = pow(ss * ss, max((half)_SonarWhirlpoolTaper, 0.01h));

        Offset.y -= falloff * (half)_SonarWhirlpoolDepth;
    }
}
