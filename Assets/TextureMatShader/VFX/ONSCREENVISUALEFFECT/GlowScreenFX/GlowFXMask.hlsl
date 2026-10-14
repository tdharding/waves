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

float  _GlowOpacity;

// Angel glow. The silhouette of everything on the Glow rendering layer, drawn white by
// GlowFXRendererFeature; the three knobs come from BrightnessGlowController.
TEXTURE2D(_AngelGlowMask);
SAMPLER(sampler_AngelGlowMask);
float _AngelGlowSize;       // how far the glow reaches, as a fraction of screen height
float _AngelGlowStrength;   // how bright and full it is before it fades
float _AngelGlowOpacity;    // master fade; 0 = off, and the feature skips the silhouette too

#define ANGEL_GLOW_TAPS 32

// Blurs the silhouette and keeps only what spills OUTSIDE it, so the light spreads off the edges
// without washing over the angel. The taps are a spiral turned per pixel, which trades the
// stepping a fixed pattern would leave for a fine grain.
float GlowFX_AngelGlow(float2 uv, float2 screenSize)
{
    float2 radius = float2(_AngelGlowSize * screenSize.y / screenSize.x, _AngelGlowSize);
    float2 px     = uv * screenSize;
    float  turn   = 6.2831853 * frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715))));

    float sum = 0.0, weights = 0.0;
    [loop] for (int i = 0; i < ANGEL_GLOW_TAPS; i++)
    {
        float  r = sqrt((i + 0.5) / ANGEL_GLOW_TAPS);
        float  a = i * 2.39996323 + turn;
        float  w = 1.0 - r;   // nearer taps count for more, so the glow fades out softly
        float2 o = float2(cos(a), sin(a)) * r * radius;
        sum     += SAMPLE_TEXTURE2D_LOD(_AngelGlowMask, sampler_AngelGlowMask, uv + o, 0).r * w;
        weights += w;
    }

    float inside = SAMPLE_TEXTURE2D_LOD(_AngelGlowMask, sampler_AngelGlowMask, uv, 0).r;
    return saturate(sum / weights * _AngelGlowStrength) * (1.0 - inside) * _AngelGlowOpacity;
}

// Round point: plain radial falloff, in aspect-corrected UV so it stays circular on screen.
float GlowFX_Disc(float2 p, float2 centre, float radius, float softness)
{
    float dist = length(p - centre);
    return 1.0 - smoothstep(radius - softness, radius, dist);
}

void GlowFXMask_float(float2 UV, float2 ScreenSize, out float Mask)
{
    Mask = 0.0;
    float aspect = ScreenSize.x / ScreenSize.y;
    float2 p = float2(UV.x * aspect, UV.y);
    int count = (int)_GlowPointCount;

    for (int i = 0; i < count; i++)
    {
        float4 pt = _GlowPoints[i];
        float2 c  = float2(pt.x * aspect, pt.y);

        float m = GlowFX_Disc(p, c, pt.z, pt.w);

        m *= _GlowPointParams[i].x;   // this point's own opacity

        // max, not add: overlapping points stay at the brightness of the strongest one rather than
        // stacking into a blown-out blob where two lamps are close together on screen.
        Mask = max(Mask, m);
    }

    Mask *= _GlowOpacity;

    // Rides the same brightness lift as the points; max for the same reason as above.
    if (_AngelGlowOpacity > 0.0)
        Mask = max(Mask, GlowFX_AngelGlow(UV, ScreenSize));
}

void GlowFXMask_half(half2 UV, half2 ScreenSize, out half Mask)
{
    float fMask;
    GlowFXMask_float((float2)UV, (float2)ScreenSize, fMask);
    Mask = (half)fMask;
}

#endif
