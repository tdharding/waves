#ifndef WHIRLPOOL_POSITIONS_DECLARED
#define WHIRLPOOL_POSITIONS_DECLARED
uniform float4 _WhirlpoolPositions[8];
#endif

void WhirlpoolDisplacement_float(
    float3 PositionIn,
    float  PointCount,
    float  GlobalDepth,
    float  GlobalSwirl,
    out float3 PositionOut)
{
    PositionOut = PositionIn;
    int count = (int)PointCount;

    for (int i = 0; i < count; i++)
    {
        float3 center = _WhirlpoolPositions[i].xyz;
        float  radius = _WhirlpoolPositions[i].w;

        float2 offset = PositionIn.xz - center.xz;
        float  dist   = length(offset);

        float h       = 1.0 - saturate(dist / radius);
        float ss      = h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
        float falloff = ss * ss;

        // Downward funnel: on a -90X plane, world -Y = object -Z
        PositionOut.z -= falloff * GlobalDepth;

        // Swirl
        float  angle   = GlobalSwirl * falloff;
        float  cosA    = cos(angle);
        float  sinA    = sin(angle);
        float2 rotated = float2(
            offset.x * cosA - offset.y * sinA,
            offset.x * sinA + offset.y * cosA
        );
        PositionOut.xz += rotated - offset;
    }
}
