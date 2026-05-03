void WavePeaksTroughs_float(
    float3 PositionIn,
    float4 WaveCenter,
    float  WaveStepRate,
    float  Speed,
    float  Frequency,
    float  Time,
    float  SoulFishMask,
    float  WhirlpoolMask,
    float  TroughBrightness,
    float  PeakBrightness,
    out float Brightness)
{
    // Same horizontal distance formula as vertex displacement (XY = horizontal on -90X plane)
    float2 toCenter = PositionIn.xy - float2(WaveCenter.x, -WaveCenter.z);
    float  dist     = length(toCenter);
    float  stepped  = floor(dist * WaveStepRate) / max(WaveStepRate, 0.0001);

    // Re-derive wave height — same formula as vertex stage, no RippleDepth multiply needed
    float  height   = sin(stepped * Frequency - Time * Speed); // -1 to +1
    float  height01 = height * 0.5 + 0.5;                      // 0 = trough, 1 = peak

    // Whirlpool pulls surface into a deep trough
    height01 = saturate(height01 - WhirlpoolMask);

    // Single-value brightness per band (linear, no contrast)
    float troughBright = lerp(1.0, TroughBrightness, 1.0 - height01);
    float peakBright   = lerp(1.0, PeakBrightness,   height01);

    // Multiply together — peaks and troughs are independent, mid-wave stays neutral
    Brightness = troughBright * peakBright;

    // SoulFish brightens the output directly as a multiplicative boost
    Brightness *= (1.0 + SoulFishMask);
}
