#ifndef WHIRLPOOL_POSITIONS_DECLARED
#define WHIRLPOOL_POSITIONS_DECLARED
uniform float4 _WhirlpoolPositions[8];
#endif

void WavesAndWhirlpools_float(
    float3 PositionIn,
    float4 WaveCenter,
    float  WaveStepRate,
    float  Speed,
    float  Frequency,
    float  RippleDepth,
    float  Time,
    float  WhirlpoolCount,
    float  WhirlpoolDepth,
    float  WhirlpoolSwirl,
    float  WhirlpoolTaper,
    out float3 PositionOut)
{
    PositionOut = PositionIn;

    // On a -90X plane, horizontal axes in object space are XY (not XZ)
    // WaveCenter is world space — convert Z to match object XY: objectY = -worldZ
    float2 toCenter = PositionIn.xy - float2(WaveCenter.x, -WaveCenter.z);
    float  dist     = length(toCenter);
    float  stepped  = floor(dist * WaveStepRate) / max(WaveStepRate, 0.0001);
    float  wave     = sin(stepped * Frequency - Time * Speed) * RippleDepth;
    PositionOut.z  -= wave;

    // Whirlpools — also use XY for horizontal distance
    int count = (int)WhirlpoolCount;
    for (int i = 0; i < count; i++)
    {
        float3 center  = _WhirlpoolPositions[i].xyz;
        float  radius  = _WhirlpoolPositions[i].w;

        float2 offset  = PositionIn.xy - center.xy;
        float  wpDist  = length(offset);

        float  h       = 1.0 - saturate(wpDist / radius);
        float  ss      = h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
        float  falloff = pow(ss * ss, max(WhirlpoolTaper, 0.01));

        PositionOut.z -= falloff * WhirlpoolDepth;

        float  angle   = WhirlpoolSwirl * falloff;
        float  cosA    = cos(angle);
        float  sinA    = sin(angle);
        float2 rotated = float2(
            offset.x * cosA - offset.y * sinA,
            offset.x * sinA + offset.y * cosA
        );
        PositionOut.xy += rotated - offset;
    }
}
