void WaveTwirlNormal_float(
    float2 UV,
    float3 PositionIn,
    float4 WaveCenter,
    float  WaveStepRate,
    float  Speed,
    float  Frequency,
    float  Time,
    float  TwirlBaseStrength,
    float  TwirlSlopeBoost,
    float  TwirlScale,
    float  WhirlpoolMask,
    float  WhirlpoolTwirlStrength,
    float  WhirlpoolAreaMask,
    float  WhirlpoolAreaTwirlStrength,
    float  SoulFishMask,
    float  SoulFishTwirlStrength,
    out float2 TwistedUV)
{
    // Horizontal distance from wave centre (XY = horizontal on -90X plane)
    float2 toCenter = PositionIn.xy - float2(WaveCenter.x, -WaveCenter.z);
    float  dist     = length(toCenter);
    float  stepped  = floor(dist * WaveStepRate) / max(WaveStepRate, 0.0001);

    // Wave gradient — cos of the same argument gives the slope of the sine wave.
    // Magnitude near 1 = steep incline, near 0 = flat peak or trough.
    float  gradient = cos(stepped * Frequency - Time * Speed);
    float  slope    = abs(gradient);

    // Rotation angle: base twirl + depth/whirlpool disturbance
    float  angle    = TwirlBaseStrength + WhirlpoolMask * WhirlpoolTwirlStrength + WhirlpoolAreaMask * WhirlpoolAreaTwirlStrength + SoulFishMask * SoulFishTwirlStrength;

    // Twirl pivot is the wave centre projected into UV space (0.5, 0.5 if centred)
    float2 pivot    = float2(0.5, 0.5);
    float2 delta    = UV - pivot;

    // Slope boosts UV scale — denser detail on inclines, no effect on rotation speed
    float2 scaledDelta = delta * max(TwirlScale + slope * TwirlSlopeBoost, 0.001);

    // Rotation by angle scaled by distance from pivot — tighter near centre, looser at rim
    float  r        = length(scaledDelta);
    float  theta    = atan2(scaledDelta.y, scaledDelta.x) + angle * r;
    float2 rotated  = float2(cos(theta), sin(theta)) * r;

    TwistedUV = pivot + rotated;
}
