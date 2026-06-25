#ifndef WAVE_PHASE_DECLARED
#define WAVE_PHASE_DECLARED
float _WavePhase;
#endif

void BoatWakeDisplacement_float(
    float3 PositionIn,
    float4 BoatCenter,
    float  WakeRadius,
    float  RingFrequency,
    float  RingScrollSpeed,
    float  RingFalloff,
    float  RingAmplitude,
    float  IdleAmplitude,
    float  BoatSpeed01,
    float  Time,
    out float3 PositionOut,
    out float  WakeBrightness)
{
    PositionOut = PositionIn;
    WakeBrightness = 0.0;

    // Same convention as WavesAndWhirlpools — object XY is horizontal on -90X mesh
    float2 toBoat = PositionIn.xy - float2(BoatCenter.x, -BoatCenter.z);
    float  dist   = length(toBoat);

    // Radial mask — smooth fade to zero at WakeRadius
    float  mask   = 1.0 - smoothstep(WakeRadius * 0.5, WakeRadius, dist);

    // Scrolling rings — stronger when moving
    float  rings  = sin(dist * RingFrequency - Time * RingScrollSpeed)
                  * exp(-dist * RingFalloff)
                  * RingAmplitude
                  * BoatSpeed01;

    // Idle pulse — always present at low amplitude
    float  idle   = sin(dist * RingFrequency * 0.5 - Time * RingScrollSpeed * 0.3)
                  * exp(-dist * RingFalloff * 0.5)
                  * IdleAmplitude;

    float  wakeDisplace = (rings + idle) * mask;
    PositionOut.z -= wakeDisplace;

    WakeBrightness = wakeDisplace * mask;
}
