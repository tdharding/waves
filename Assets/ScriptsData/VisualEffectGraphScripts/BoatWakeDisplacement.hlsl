#ifndef WAVE_PHASE_DECLARED
#define WAVE_PHASE_DECLARED
float _WavePhase;
#endif

// RingFrequency, RingScrollSpeed and Time are kept on the signature so the Shader Graph
// custom function node stays wired, but the wake no longer ripples — they are unused.
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

    // Permanent displacement pocket centred on the boat.
    // 1 directly under the hull, easing to 0 at WakeRadius.
    float  t       = saturate(dist / max(WakeRadius, 1e-4));
    float  profile = 1.0 - smoothstep(0.0, 1.0, t);

    // RingFalloff shapes the profile: >1 pulls the pocket tight around the hull,
    // <1 spreads it out towards the edge.
    profile = pow(profile, max(RingFalloff, 0.01));

    // IdleAmplitude is the always-on depth; RingAmplitude adds depth with speed.
    float  amplitude = IdleAmplitude + RingAmplitude * BoatSpeed01;

    float  wakeDisplace = profile * amplitude;
    PositionOut.z -= wakeDisplace;

    WakeBrightness = wakeDisplace;
}
