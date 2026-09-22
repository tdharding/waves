// How the level select's landscape hills are shaded.
//
// The same idea as the river runs' stone, kept entirely separate from it: three stone VARIANTS
// (A, B, C), each a colour with its own grain, and three PARTS of the landscape that each wear one
// of them — Holes, NoiseUp and NoiseDown.
//
// A run's parts are baked into its mesh as it is built. The hills are not built — they are raised
// in the vertex shader by CalculateHills — so which part a pixel belongs to is worked out here,
// from two things only:
//   * how deep it sits below the tile base (Holes below Hole Depth), and
//   * which way the rocky noise leans (NoiseUp on the ups, NoiseDown on the downs).
// Holes win; everything else is one side of the noise or the other. Ground level carries no lean
// either way, so it lands wherever Noise Softness puts the middle — on the NoiseDown side when
// the split is hard, halfway between the two when it is not.
//
// Every setting is a bare $Global pushed each frame by LandscapeShadingSettings — there is no
// property block behind any of them, so nothing here has a material value to fall back on.
// Authored in Tools > Waves > Level Select Landscape Tuner.

#ifndef LANDSCAPE_SHADING_INCLUDED
#define LANDSCAPE_SHADING_INCLUDED

// The three variants' colours. Alpha is ignored.
float4 _LandscapeColourA;
float4 _LandscapeColourB;
float4 _LandscapeColourC;

// x A, y B, z C. Grain size is metres per noise cell, 0 turns it off.
float4 _LandscapeGrainSizes;
float4 _LandscapeGrainStrengths;

// Which variant each part wears: 0 A, 1 B, 2 C. x Holes, y NoiseUp, z NoiseDown.
float4 _LandscapePartVariants;

// Where Holes split off. Hole Depth is metres below the tile base, and Softness is how wide the
// blend is as a fraction of it.
float  _LandscapeHoleDepth;
float  _LandscapeSoftness;

// How wide the blend between NoiseUp and NoiseDown is, as a fraction of the noise's own lean.
// 0 is a hard line down the middle of the noise, 1 blends across the whole of it.
float  _LandscapeNoiseSoftness;

// The tile surface's world height — the level Holes are measured down from. Pushed from the
// designer's landscape World Y + Height Offset, not tuned.
float  _LandscapeBaseY;

// The light. The position is the world's, shared with the river runs; the strength is the
// landscape's own.
float4 _LevelSelectLightPosition;
float  _LandscapeLightStrength;

// Which way the noise leans in the cell at p — arithmetic rather than a sine, so every machine
// agrees where the grain is.
float2 LandscapeNoiseDir(float2 p)
{
    p = fmod(p, 289.0);
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

// Gradient noise about zero, the same shape as the Gradient Noise node.
float LandscapeGradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);

    float d00 = dot(LandscapeNoiseDir(ip),                    fp);
    float d01 = dot(LandscapeNoiseDir(ip + float2(0.0, 1.0)), fp - float2(0.0, 1.0));
    float d10 = dot(LandscapeNoiseDir(ip + float2(1.0, 0.0)), fp - float2(1.0, 0.0));
    float d11 = dot(LandscapeNoiseDir(ip + float2(1.0, 1.0)), fp - float2(1.0, 1.0));

    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y);
}

// What one variant's grain multiplies its colour by — about 1, so graining never darkens overall.
//
// Laid on from three sides and blended by which way the surface faces. Laid only from above, the
// grain would smear into long streaks down a cliff, which is exactly where it is most visible.
float LandscapeGrain(float3 worldPos, float3 n, float size, float strength)
{
    if (size <= 0.0 || strength <= 0.0) return 1.0;

    float3 w = abs(n);
    w = w * w * w * w;
    w /= max(w.x + w.y + w.z, 1e-5);

    float noise = LandscapeGradientNoise(worldPos.zy / size) * w.x
                + LandscapeGradientNoise(worldPos.xz / size) * w.y
                + LandscapeGradientNoise(worldPos.xy / size) * w.z;

    return max(0.0, 1.0 + noise * 2.0 * strength);
}

// 0 below the threshold, 1 above it, blended across Softness × threshold either side.
float LandscapeSplit(float value, float threshold)
{
    float band = abs(threshold) * max(_LandscapeSoftness, 0.0);
    if (band <= 1e-5) return value > threshold ? 1.0 : 0.0;
    return smoothstep(threshold - band, threshold + band, value);
}

// How much of a pixel one part hands to each of the three variants — all of its weight to the one
// it wears, nothing to the other two. Parts are added up this way before any colour is worked out,
// so a variant's grain costs the same whether one part wears it or all three do.
float3 LandscapeShare(float variant, float weight)
{
    int v = (int)(variant + 0.5);
    return float3(v == 0 ? weight : 0.0, v == 1 ? weight : 0.0, v == 2 ? weight : 0.0);
}

// The grained colour of variant 0 (A), 1 (B) or 2 (C).
float3 LandscapeVariant(float variant, float3 worldPos, float3 n)
{
    int v = (int)(variant + 0.5);

    float4 colour   = v == 1 ? _LandscapeColourB : v == 2 ? _LandscapeColourC : _LandscapeColourA;
    float  size     = v == 1 ? _LandscapeGrainSizes.y     : v == 2 ? _LandscapeGrainSizes.z     : _LandscapeGrainSizes.x;
    float  strength = v == 1 ? _LandscapeGrainStrengths.y : v == 2 ? _LandscapeGrainStrengths.z : _LandscapeGrainStrengths.x;

    return colour.rgb * LandscapeGrain(worldPos, n, size, strength);
}

// Which side of the rocky noise this pixel is on: 1 fully up, 0 fully down, blended across
// Noise Softness either side of the middle. At 0 softness it is the bare sign of the lean.
float LandscapeNoiseUp(float lean)
{
    float band = max(_LandscapeNoiseSoftness, 0.0);
    if (band <= 1e-5) return lean > 0.0 ? 1.0 : 0.0;
    return smoothstep(-band, band, lean);
}

// Normal : the smooth hill normal, from CalculateHills' Normal output. The mesh's own normal is
//          flat — the hills are raised in the shader — so it has to come in from there. Nothing
//          is judged by steepness any more, but the grain is still laid along it.
// Noise  : CalculateHills' Noise output — x which way the rocky noise leans here (-1 down to
//          +1 up). The y, how much noise there is here at all, is left on the wire but unused:
//          with no Ground or Tops behind it there is nothing for unnoisy ground to fall back to.
// Colour : the landscape, each part in its variant, grained and lit.
void LandscapeShading_float(float3 Normal, float3 WorldPos, float2 Noise, out float3 Colour)
{
    float3 n = normalize(Normal);

    // Which part this pixel is.
    float height = WorldPos.y - _LandscapeBaseY;

    float hole = LandscapeSplit(-height, _LandscapeHoleDepth);
    float up   = LandscapeNoiseUp(Noise.x);

    // Holes take the pixel first; all the rest is one side of the noise or the other.
    float wHole = hole;
    float wUp   = (1.0 - hole) * up;
    float wDown = (1.0 - hole) * (1.0 - up);

    float3 share = LandscapeShare(_LandscapePartVariants.x, wHole)
                 + LandscapeShare(_LandscapePartVariants.y, wUp)
                 + LandscapeShare(_LandscapePartVariants.z, wDown);

    float3 stone = LandscapeVariant(0.0, WorldPos, n) * share.x
                 + LandscapeVariant(1.0, WorldPos, n) * share.y
                 + LandscapeVariant(2.0, WorldPos, n) * share.z;

    // Half lambert toward the world's light. Strength lerps out of it, so 0 is flat and unlit;
    // past 1 the lit result is multiplied, up to 10x brighter.
    float3 toLight = _LevelSelectLightPosition.xyz - WorldPos;
    toLight = dot(toLight, toLight) > 1e-8 ? normalize(toLight) : float3(0.0, 1.0, 0.0);

    float ndl   = dot(n, toLight) * 0.5 + 0.5;
    float light = lerp(1.0, saturate(ndl), saturate(_LandscapeLightStrength))
                * max(_LandscapeLightStrength, 1.0);

    Colour = stone * light;
}

void LandscapeShading_half(half3 Normal, half3 WorldPos, half2 Noise, out half3 Colour)
{
    float3 colour;
    LandscapeShading_float(Normal, WorldPos, Noise, colour);
    Colour = colour;
}

#endif
