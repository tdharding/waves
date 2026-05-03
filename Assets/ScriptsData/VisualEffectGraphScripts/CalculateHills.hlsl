uniform float4 _HillPositions[100];

float CalculateHillHeight(float3 WorldPos, int count, float GlobalHeight)
{
    float totalY = 0.0;
    for (int i = 0; i < count; i++)
    {
        float3 hillCenter = _HillPositions[i].xyz;
        float  radius     = _HillPositions[i].w;

        float2 distVector = WorldPos.xz - hillCenter.xz;
        float  d          = length(distVector);

        float h       = 1.0 - saturate(d / radius);
        float falloff = h * h * (3.0 - 2.0 * h);

        totalY += falloff * hillCenter.y * GlobalHeight;
    }
    return totalY;
}

void CalculateHills_float(float3 WorldPos, float PointCount, float GlobalHeight,
                          out float3 OffsetPos, out float3 Normal)
{
    int count = (int)PointCount;

    float h0 = CalculateHillHeight(WorldPos, count, GlobalHeight);
    OffsetPos   = WorldPos;
    OffsetPos.y += h0;

    // Finite differences for surface normal
    float epsilon = 0.5;
    float hX = CalculateHillHeight(WorldPos + float3(epsilon, 0, 0), count, GlobalHeight);
    float hZ = CalculateHillHeight(WorldPos + float3(0, 0, epsilon), count, GlobalHeight);

    float3 tangentX = float3(epsilon, hX - h0, 0);
    float3 tangentZ = float3(0, hZ - h0, epsilon);
    Normal = normalize(cross(tangentZ, tangentX));
}
