float4 _SoulFishPositions[10];
float _SoulFishCount;

float4 _MapCenter;
float4 _MapSize;

void SoulFishMask_float(float3 WorldPos, float Radius, out float Mask)
{
    Mask = 0.0;

    float2 mapUV = (WorldPos.xz - _MapCenter.xz) / _MapSize.xz + 0.5;

    for (int i = 0; i < 10; i++)
    {
        if (i >= (int)_SoulFishCount)
            break;

        float2 fishUV = (_SoulFishPositions[i].xz - _MapCenter.xz) / _MapSize.xz + 0.5;

        float dist = distance(mapUV, fishUV);

        float contribution = 1.0 - saturate(dist / max(Radius, 0.0001));

        Mask = max(Mask, contribution);
    }
}