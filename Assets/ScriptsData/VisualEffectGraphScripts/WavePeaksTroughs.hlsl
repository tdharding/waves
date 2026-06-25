// Global phase provided by WaveMaterialController to prevent teleporting waves
#ifndef WAVE_PHASE_DECLARED
#define WAVE_PHASE_DECLARED
float _WavePhase;
#endif

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
    // Using accumulated _WavePhase global instead of Time * Speed to prevent "skipping" 
    float  height   = sin(stepped * Frequency - _WavePhase); // -1 to +1
    float  height01 = height * 0.5 + 0.5;                      // 0 = trough, 1 = peak

    // Whirlpool pulls surface into a deep trough
    height01 = saturate(height01 - WhirlpoolMask);

    // Single-value brightness per band (linear, no contrast)
    float troughBright = lerp(1.0, TroughBrightness, 1.0 - height01);
    float peakBright   = lerp(1.0, PeakBrightness,   height01);

    // Multiply together — peaks and troughs are independent, mid-wave stays neutral
    Brightness = troughBright * peakBright;

    // SoulFishMask arrives pre-scaled by _SoulFishMaskStrength from SoulFishWaveMask
    Brightness *= (1.0 + SoulFishMask);
}
