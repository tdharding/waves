uniform float4 _HillPositions[100];

void CalculateHills_float(float3 WorldPos, float PointCount, float GlobalHeight, out float3 OffsetPos)
{
    // Start with the original vertex position
    OffsetPos = WorldPos;
    int count = (int)PointCount;

    for (int i = 0; i < count; i++)
    {
        float3 hillCenter = _HillPositions[i].xyz;
        float radius = _HillPositions[i].w;
        
        // 1. Calculate horizontal distance only (ignore Y for the "circle" of the hill)
        float2 distVector = WorldPos.xz - hillCenter.xz;
        float d = length(distVector);
        
        // 2. Create the falloff (0 at edge, 1 at center)
        float h = 1.0 - saturate(d / radius);
        float falloff = h * h * (3.0 - 2.0 * h); // Smoothstep for round tops
        
        // 3. The New Feature: 
        // Use the handle's Y position to determine how high to push the vertex.
        // If the handle is at Y=10, the hill is 10 units high.
        OffsetPos.y += falloff * hillCenter.y * GlobalHeight;
    }
}