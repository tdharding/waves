float _FoamBayerDither(float2 screenPx)
{
    int2 p = int2(fmod(screenPx, 4.0));
    float b00 =  0.0; float b01 =  8.0; float b02 =  2.0; float b03 = 10.0;
    float b10 = 12.0; float b11 =  4.0; float b12 = 14.0; float b13 =  6.0;
    float b20 =  3.0; float b21 = 11.0; float b22 =  1.0; float b23 =  9.0;
    float b30 = 15.0; float b31 =  7.0; float b32 = 13.0; float b33 =  5.0;

    float row0 = p.x == 0 ? b00 : (p.x == 1 ? b01 : (p.x == 2 ? b02 : b03));
    float row1 = p.x == 0 ? b10 : (p.x == 1 ? b11 : (p.x == 2 ? b12 : b13));
    float row2 = p.x == 0 ? b20 : (p.x == 1 ? b21 : (p.x == 2 ? b22 : b23));
    float row3 = p.x == 0 ? b30 : (p.x == 1 ? b31 : (p.x == 2 ? b32 : b33));

    float val  = p.y == 0 ? row0 : (p.y == 1 ? row1 : (p.y == 2 ? row2 : row3));
    return val / 16.0;
}

void FoamDepth_float(
    float  SceneDepthEye,    // Scene Depth node (Eye mode)
    float  FragDepthEye,     // Screen Position (Raw) → Split → A
    float2 ScreenPosPixels,  // Screen Position XY × screen resolution
    out float DepthGap,      // SceneDepth - FragDepth, normalise downstream with DistanceDepth
    out float Dither)        // raw 0-1 Bayer value, use as Smoothstep Edge1
{
    DepthGap = max(SceneDepthEye - FragDepthEye, 0.0);
    Dither   = _FoamBayerDither(ScreenPosPixels);
}
