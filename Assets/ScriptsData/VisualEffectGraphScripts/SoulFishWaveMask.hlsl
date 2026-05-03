// Declare the 10 individual position properties (matches your existing shader property names)
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

float _SoulFishCount;
float _SoulFishRadius;
float _SoulFishStrength;

void SoulFishWaveMask_float(float3 WorldPos, out float Mask)
{
    // Pack individual properties into a local array for clean looping
    float4 positions[10];
    positions[0] = _SoulFishPosition1;
    positions[1] = _SoulFishPosition2;
    positions[2] = _SoulFishPosition3;
    positions[3] = _SoulFishPosition4;
    positions[4] = _SoulFishPosition5;
    positions[5] = _SoulFishPosition6;
    positions[6] = _SoulFishPosition7;
    positions[7] = _SoulFishPosition8;
    positions[8] = _SoulFishPosition9;
    positions[9] = _SoulFishPosition10;

    Mask = 0.0;

    for (int i = 0; i < 10; i++)
    {
        if (i >= (int)_SoulFishCount)
            break;

        // World XZ distance — matches AbsoluteWorldPosition R+B from your shader graph
        float2 delta = WorldPos.xz - positions[i].xz;
        float dist = length(delta);

        // Smooth falloff within radius, scaled by strength
        float contribution = (1.0 - saturate(dist / max(_SoulFishRadius, 0.0001))) * _SoulFishStrength;

        Mask = max(Mask, contribution);
    }
}