// How the level select's landscape hills are shaded.
//
// The same idea as the river runs' stone, kept entirely separate from it: three stone VARIANTS
// (A, B, C), each a colour with its own grain, and four PARTS of the landscape that each wear one
// of them — Ground, Tops, Cliffs and Holes.
//
// A run's parts are baked into its mesh as it is built. The hills are not built — they are raised
// in the vertex shader by CalculateHills — so which part a pixel belongs to is worked out here,
// from two things only:
//   * how steep it is, off the smooth hill normal (Cliffs past the Cliff Angle), and
//   * how high it sits off the tile base (Tops above Top Height, Holes below Hole Depth).
// Holes win over everything, Cliffs win over Tops and Ground, and whatever is left is Ground.
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

// Which variant each part wears: 0 A, 1 B, 2 C. x Ground, y Tops, z Cliffs, w Holes.
float4 _LandscapePartVariants;

// Where the parts split. Cliff Angle is degrees off flat, Top Height and Hole Depth are metres off
// the tile base, and Softness is how wide each blend is as a fraction of its own threshold.
float  _LandscapeCliffAngle;
float  _LandscapeTopHeight;
float  _LandscapeHoleDepth;
float  _LandscapeSoftness;

// The tile surface's world height — the level Tops and Holes are measured from. Pushed from the
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

// The grained colour of variant 0 (A), 1 (B) or 2 (C).
float3 LandscapeVariant(float variant, float3 worldPos, float3 n)
{
    int v = (int)(variant + 0.5);

    float4 colour   = v == 1 ? _LandscapeColourB : v == 2 ? _LandscapeColourC : _LandscapeColourA;
    float  size     = v == 1 ? _LandscapeGrainSizes.y     : v == 2 ? _LandscapeGrainSizes.z     : _LandscapeGrainSizes.x;
    float  strength = v == 1 ? _LandscapeGrainStrengths.y : v == 2 ? _LandscapeGrainStrengths.z : _LandscapeGrainStrengths.x;

    return colour.rgb * LandscapeGrain(worldPos, n, size, strength);
}

// Normal : the smooth hill normal, from CalculateHills' Normal output. The mesh's own normal is
//          flat — the hills are raised in the shader — so it has to come in from there.
// Colour : the landscape, each part in its variant, grained and lit.
void LandscapeShading_float(float3 Normal, float3 WorldPos, out float3 Colour)
{
    float3 n = normalize(Normal);

    // Which part this pixel is.
    float angle  = degrees(acos(saturate(n.y)));
    float height = WorldPos.y - _LandscapeBaseY;

    float hole  = LandscapeSplit(-height, _LandscapeHoleDepth);
    float cliff = LandscapeSplit(angle,   _LandscapeCliffAngle);
    float top   = LandscapeSplit(height,  _LandscapeTopHeight);

    float wHole   = hole;
    float wCliff  = (1.0 - hole) * cliff;
    float wTop    = (1.0 - hole) * (1.0 - cliff) * top;
    float wGround = (1.0 - hole) * (1.0 - cliff) * (1.0 - top);

    float3 stone = LandscapeVariant(_LandscapePartVariants.x, WorldPos, n) * wGround
                 + LandscapeVariant(_LandscapePartVariants.y, WorldPos, n) * wTop
                 + LandscapeVariant(_LandscapePartVariants.z, WorldPos, n) * wCliff
                 + LandscapeVariant(_LandscapePartVariants.w, WorldPos, n) * wHole;

    // Half lambert toward the world's light. Strength lerps out of it, so 0 is flat and unlit;
    // past 1 the lit result is multiplied, up to 10x brighter.
    float3 toLight = _LevelSelectLightPosition.xyz - WorldPos;
    toLight = dot(toLight, toLight) > 1e-8 ? normalize(toLight) : float3(0.0, 1.0, 0.0);

    float ndl   = dot(n, toLight) * 0.5 + 0.5;
    float light = lerp(1.0, saturate(ndl), saturate(_LandscapeLightStrength))
                * max(_LandscapeLightStrength, 1.0);

    Colour = stone * light;
}

void LandscapeShading_half(half3 Normal, half3 WorldPos, out half3 Colour)
{
    float3 colour;
    LandscapeShading_float(Normal, WorldPos, colour);
    Colour = colour;
}

#endif
