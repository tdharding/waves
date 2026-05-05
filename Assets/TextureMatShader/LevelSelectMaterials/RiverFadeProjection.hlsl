void RiverFadeProjection_float(
    float3 WorldPos,
    float3 FadeStart,
    float3 FadeEnd,
    out float Out)
{
    float range = FadeEnd.x - FadeStart.x;
    Out = abs(range) > 0.0001 ? saturate((WorldPos.x - FadeStart.x) / range) : 0.0;
}
