void HandRippleEffect_float(
    float3 WorldPos,
    float3 EffectPoint,
    float  Radius,
    float  Frequency,
    float  Speed,
    float  Time,
    float3 BaseColor,
    out float3 Out)
{
    float dist      = length(WorldPos - EffectPoint);
    float falloff   = 1.0 - saturate(dist / Radius);
    float ripple    = sin(dist * Frequency - Time * Speed) * 0.5 + 0.5;
    float effect    = ripple * falloff;
    Out             = BaseColor * (1.0 + effect);
}
