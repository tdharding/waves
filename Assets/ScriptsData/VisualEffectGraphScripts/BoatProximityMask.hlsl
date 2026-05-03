float4 _BoatPosition;
float4 _BoatScreenCenter; // boat viewport UV (x,y in 0-1 range), set by BoatToWaterMaterial

// Matches Unity's built-in Dither node (4x4 Bayer matrix, screen-space)
float BoatDither(float4 screenPos)
{
    float2 uv = screenPos.xy / screenPos.w;
    float thresholds[16] =
    {
         1.0/17.0,  9.0/17.0,  3.0/17.0, 11.0/17.0,
        13.0/17.0,  5.0/17.0, 15.0/17.0,  7.0/17.0,
         4.0/17.0, 12.0/17.0,  2.0/17.0, 10.0/17.0,
        16.0/17.0,  8.0/17.0, 14.0/17.0,  6.0/17.0
    };
    uint index = (uint(uv.x * _ScreenParams.x) % 4) * 4 + uint(uv.y * _ScreenParams.y) % 4;
    return thresholds[index];
}

float _ArenaBoatMaskStrength;

void BoatProximityMask_float(float3 WorldPos, float Radius, float OuterRadius, float4 ScreenPos, out float Mask)
{
    // Screen-space distance from this fragment to the boat's screen position
    float2 fragUV     = ScreenPos.xy / ScreenPos.w;
    float2 boatUV     = _BoatScreenCenter.xy;
    float  aspect     = _ScreenParams.x / _ScreenParams.y;
    float2 delta      = (fragUV - boatUV) * float2(aspect, 1.0);
    float  dist       = length(delta);

    float outer = max(OuterRadius, Radius + 0.0001);

    // Dither offsets the inner edge for a pixel-dissolve rather than a hard cut
    float dither      = BoatDither(ScreenPos) * 0.44;
    float ditherRange = (outer - Radius) * 0.15;
    float innerEdge   = Radius + dither * ditherRange;

    // Gradient: 0 inside inner edge → 1 at OuterRadius (invert in Shader Graph with One Minus)
    float boatMask = smoothstep(innerEdge, outer, dist);

    Mask = lerp(1.0, boatMask, _ArenaBoatMaskStrength);
}
