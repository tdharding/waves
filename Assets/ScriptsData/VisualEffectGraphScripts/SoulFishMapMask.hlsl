float4 _SoulFishPositions[10];
float _SoulFishCount;

float4 _MapCenter;
float4 _MapSize;

float distToSegment_SoulMap(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

void SoulFishMask_float(float3 WorldPos, float Radius, out float Mask)
{
    float2 mapUV = (WorldPos.xz - _MapCenter.xz) / _MapSize.xz + 0.5;
    float minDist = 99999.0;
    int count = (int)_SoulFishCount;

    for (int i = 0; i < 10; i++)
    {
        if (i >= count)
            break;

        float2 fishUV = (_SoulFishPositions[i].xz - _MapCenter.xz) / _MapSize.xz + 0.5;
        
        // Distance to node point
        minDist = min(minDist, distance(mapUV, fishUV));

        // Distance to segment if connected (w > 1.5)
        if (i < 9 && i < count - 1 && _SoulFishPositions[i].w > 1.5)
        {
            float2 nextFishUV = (_SoulFishPositions[i+1].xz - _MapCenter.xz) / _MapSize.xz + 0.5;
            minDist = min(minDist, distToSegment_SoulMap(mapUV, fishUV, nextFishUV));
        }
    }

    Mask = 1.0 - saturate(minDist / max(Radius, 0.0001));
}