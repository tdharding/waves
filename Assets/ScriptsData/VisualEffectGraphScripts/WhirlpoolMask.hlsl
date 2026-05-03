#ifndef WHIRLPOOL_POSITIONS_DECLARED
#define WHIRLPOOL_POSITIONS_DECLARED
uniform float4 _WhirlpoolPositions[8];
#endif

void WhirlpoolMask_float(
    float3 PositionIn,
    float  WhirlpoolCount,
    out float Mask)         // 0-1, strongest at whirlpool centre
{
    Mask = 0.0;
    int count = (int)WhirlpoolCount;

    for (int i = 0; i < count; i++)
    {
        float2 offset = PositionIn.xy - _WhirlpoolPositions[i].xy;
        float  radius = _WhirlpoolPositions[i].w;
        float  dist   = length(offset);

        float  h      = 1.0 - saturate(dist / max(radius, 0.0001));
        float  ss     = h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
        float  falloff= ss * ss;

        Mask = max(Mask, falloff);
    }
}
