// Declare individual position properties
float4 _SoulFishPosition1;
float4 _SoulFishPosition2;
float4 _SoulFishPosition3;
float4 _SoulFishPosition4;
float4 _SoulFishPosition5;
float4 _SoulFishPosition6;
float4 _SoulFishPosition7;
float4 _SoulFishPosition8;
float4 _SoulFishPosition9;
float4 _SoulFishPosition10;
float4 _SoulFishPosition11;
float4 _SoulFishPosition12;
float4 _SoulFishPosition13;
float4 _SoulFishPosition14;
float4 _SoulFishPosition15;
float4 _SoulFishPosition16;
float4 _SoulFishPosition17;
float4 _SoulFishPosition18;
float4 _SoulFishPosition19;
float4 _SoulFishPosition20;

float _SoulFishCount;
float _SoulFishRadius;
float _SoulFishStrength;

float distToSegment_SoulFish(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

void SoulFishWaveMask_float(
    float3 WorldPos,
    float  SoulFishMaskStrength,
    out float Mask)
{
    float4 positions[20];
    positions[0]  = _SoulFishPosition1;
    positions[1]  = _SoulFishPosition2;
    positions[2]  = _SoulFishPosition3;
    positions[3]  = _SoulFishPosition4;
    positions[4]  = _SoulFishPosition5;
    positions[5]  = _SoulFishPosition6;
    positions[6]  = _SoulFishPosition7;
    positions[7]  = _SoulFishPosition8;
    positions[8]  = _SoulFishPosition9;
    positions[9]  = _SoulFishPosition10;
    positions[10] = _SoulFishPosition11;
    positions[11] = _SoulFishPosition12;
    positions[12] = _SoulFishPosition13;
    positions[13] = _SoulFishPosition14;
    positions[14] = _SoulFishPosition15;
    positions[15] = _SoulFishPosition16;
    positions[16] = _SoulFishPosition17;
    positions[17] = _SoulFishPosition18;
    positions[18] = _SoulFishPosition19;
    positions[19] = _SoulFishPosition20;

    float minDist = 99999.0;
    int count = (int)_SoulFishCount;

    for (int i = 0; i < 20; i++)
    {
        if (i >= count) break;

        float2 p = positions[i].xz;
        minDist = min(minDist, length(WorldPos.xz - p));

        if (i < 19 && i < count - 1 && positions[i].w > 1.5)
            minDist = min(minDist, distToSegment_SoulFish(WorldPos.xz, p, positions[i+1].xz));
    }

    // Distance-based zone falloff
    float distMask = (1.0 - saturate(minDist / max(_SoulFishRadius, 0.0001))) * _SoulFishStrength;

    Mask = distMask * SoulFishMaskStrength;
}
