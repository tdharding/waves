#ifndef WHIRLPOOL_POSITIONS_DECLARED
#define WHIRLPOOL_POSITIONS_DECLARED
uniform float4 _WhirlpoolPositions[8];
#endif

// DarknessMask: 0=no darkening, 1=fully dark
void WhirlpoolAreaDarkness_float(
    float3 PositionIn,
    float  WhirlpoolCount,
    float  RadiusMult,      // >1 = dark area larger than whirlpool geometry radius
    float  DarkStrength,    // 0-1 peak darkness at centre
    float  FalloffPower,    // 1=smooth, >1=sharper edge, <1=softer spread
    out float DarknessMask)
{
    DarknessMask = 0.0;
    int count = (int)WhirlpoolCount;

    for (int i = 0; i < count; i++)
    {
        float2 offset = PositionIn.xy - _WhirlpoolPositions[i].xy;
        float  radius = _WhirlpoolPositions[i].w * max(RadiusMult, 0.001);
        float  dist   = length(offset);

        float h      = 1.0 - saturate(dist / max(radius, 0.001));
        float ss     = h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
        float falloff = pow(ss, max(FalloffPower, 0.01));

        DarknessMask = max(DarknessMask, falloff * DarkStrength);
    }
}
