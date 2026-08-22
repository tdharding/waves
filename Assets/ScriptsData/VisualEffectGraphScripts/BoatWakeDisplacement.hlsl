#ifndef WAVE_PHASE_DECLARED
#define WAVE_PHASE_DECLARED
float _WavePhase;
#endif

// Pushes the water down in a small pocket centred on the boat so the surface never
// covers the hull. This is clearance only — not a wake effect.
//
//   WakeRadius : size of the pocket
//   WakeDrop   : how far the water drops directly under the hull
void BoatWakeDisplacement_float(
    float3 PositionIn,
    float4 BoatCenter,
    float  WakeRadius,
    float  WakeDrop,
    out float3 PositionOut,
    out float  WakeBrightness)
{
    PositionOut = PositionIn;

    // Same convention as WavesAndWhirlpools — object XY is horizontal on -90X mesh
    float2 toBoat = PositionIn.xy - float2(BoatCenter.x, -BoatCenter.z);
    float  dist   = length(toBoat);

    // 1 directly under the hull, easing to 0 at WakeRadius
    float  t       = saturate(dist / max(WakeRadius, 1e-4));
    float  profile = 1.0 - smoothstep(0.0, 1.0, t);

    float  wakeDisplace = profile * WakeDrop;
    PositionOut.z -= wakeDisplace;

    WakeBrightness = wakeDisplace;
}

void BoatWakeDisplacement_half(
    half3 PositionIn,
    half4 BoatCenter,
    half  WakeRadius,
    half  WakeDrop,
    out half3 PositionOut,
    out half  WakeBrightness)
{
    PositionOut = PositionIn;

    half2 toBoat = PositionIn.xy - half2(BoatCenter.x, -BoatCenter.z);
    half  dist   = length(toBoat);

    half  t       = saturate(dist / max(WakeRadius, (half)1e-3));
    half  profile = (half)1.0 - smoothstep((half)0.0, (half)1.0, t);

    half  wakeDisplace = profile * WakeDrop;
    PositionOut.z -= wakeDisplace;

    WakeBrightness = wakeDisplace;
}
