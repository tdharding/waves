#ifndef GLOW_FX_MASK_INCLUDED
#define GLOW_FX_MASK_INCLUDED

// Must equal BrightnessGlowController.MAX_POINTS. Unused slots cost nothing per pixel — the loop
// runs to _GlowPointCount, not to this — so this is only a register/uniform budget.
#define GLOW_FX_MAX 16

// Set each frame by BrightnessGlowController via Shader.SetGlobalVectorArray / SetGlobalFloat
// xy = screen UV (0-1), z = radius (UV space), w = softness
float4 _GlowPoints[GLOW_FX_MAX];
float  _GlowPointCount;

// Per-point extras, same indexing as _GlowPoints. x = that point's own opacity (0-1), yzw reserved.
// Pushed in the same call as _GlowPoints, so the two arrays can never disagree on a frame.
float4 _GlowPointParams[GLOW_FX_MAX];

// Master fade for the whole mask, 0 = fully off. Pushed every frame alongside the points, so with
// no controller in the scene it reads 0 and the effect is silent (as it already was with count 0).
float  _GlowOpacity;

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
        m *= _GlowPointParams[i].x;   // this point's own opacity

        // max, not add: overlapping points stay at the brightness of the strongest one rather than
        // stacking into a blown-out blob where two lamps are close together on screen.
        Mask = max(Mask, m);
    }

    Mask *= _GlowOpacity;
}

void GlowFXMask_half(half2 UV, half2 ScreenSize, out half Mask)
{
    float fMask;
    GlowFXMask_float((float2)UV, (float2)ScreenSize, fMask);
    Mask = (half)fMask;
}

#endif
