#ifndef GLOW_FX_MASK_INCLUDED
#define GLOW_FX_MASK_INCLUDED

// Set each frame by BrightnessGlowController via Shader.SetGlobalVectorArray / SetGlobalFloat
// xy = screen UV (0-1), z = radius (UV space), w = softness
float4 _GlowPoints[8];
float  _GlowPointCount;

void GlowFXMask_float(float2 UV, float2 ScreenSize, out float Mask)
{
    Mask = 0.0;
    float aspect = ScreenSize.x / ScreenSize.y;
    int count = (int)_GlowPointCount;

    for (int i = 0; i < count; i++)
    {
        float2 center   = _GlowPoints[i].xy;
        float  radius   = _GlowPoints[i].z;
        float  softness = _GlowPoints[i].w;

        float2 d = UV - center;
        d.x *= aspect;
        float dist = length(d);

        float m = 1.0 - smoothstep(radius - softness, radius, dist);
        Mask = max(Mask, m);
    }
}

void GlowFXMask_half(half2 UV, half2 ScreenSize, out half Mask)
{
    float fMask;
    GlowFXMask_float((float2)UV, (float2)ScreenSize, fMask);
    Mask = (half)fMask;
}

#endif
