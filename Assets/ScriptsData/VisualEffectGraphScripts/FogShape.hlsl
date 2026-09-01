// Turns density into a shape: the body mask and the lip that rims it.
//
// THE THRESHOLD IS WHAT MAKES THE OUTLINE. It does not fade the fog — it eats limbs from the tips
// inward as it rises, because a limb tip is the thinnest, least dense part of a blob. Test runs
// put the working range at 0.20-0.30 against the reference sketches, which have blunt rounded
// limb tips. By 0.70 a blob is a lumpy sausage with no fingers left at all.
//
// Threshold is measured against a FIXED reference, not against the blob's own peak density.
// Measured relatively it behaves differently on a fat blob and a thin one, and limbs disappear
// inconsistently across the field for no reason the shapes give.
//
// Undulation is sampled in BLOB SPACE, offset by blob id, not in world space. Sampled in world
// space the wobble stays pinned to the water and appears to crawl across a mass as it drifts
// past, instead of travelling with it.

#ifndef FOG_SHAPE_INCLUDED
#define FOG_SHAPE_INCLUDED

// Density a chain of full-size overlapping dots reaches once blurred. Threshold is a fraction of
// this, so "0.26" means the same thing everywhere in the field.
#define FOG_SOLID_LIMB 2.44

float FogShape_Hash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

// Value noise. Cheap, and smooth enough that the outline wanders rather than jitters.
float FogShape_Noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = FogShape_Hash(i);
    float b = FogShape_Hash(i + float2(1, 0));
    float c = FogShape_Hash(i + float2(0, 1));
    float d = FogShape_Hash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y) - 0.5;
}

void FogShape_float(
    float  Density,
    float  BlobId,
    float3 WorldPos,
    float  Threshold,        // 0.20-0.30
    float  EdgeSoftness,     // hard ink outline vs soft feathered fade
    float  UndulationAmount,
    float  UndulationScale,
    float  LipWidth,
    out float Body,
    out float Lip,
    out float Fill)
{
    float d = Density / FOG_SOLID_LIMB;

    // Blob id shifts the noise so each mass carries its own wobble that travels with it. Two
    // octaves: a long swell and a finer ripple, which is what stops the edge reading as a wave.
    float2 p = WorldPos.xz * UndulationScale + BlobId * 137.0;
    float n = FogShape_Noise(p) * 0.9 + FogShape_Noise(p * 2.3 + 19.7) * 0.45;
    d += n * UndulationAmount;

    float soft = max(EdgeSoftness, 1e-4);
    Body = smoothstep(Threshold, Threshold + soft, d);

    // A band just inside the outline. Near the edge the surface is tapering hardest, so this is
    // also where the fake normal tips furthest over and catches a street light strongest.
    float w = max(LipWidth, 1e-4);
    Lip = smoothstep(Threshold, Threshold + w, d)
        - smoothstep(Threshold + w, Threshold + w * 3.0, d);

    // How far inside the body a pixel sits, for thinning the interior toward the edges.
    Fill = saturate((d - Threshold) / max(1.0 - Threshold, 1e-4));
}

void FogShape_half(
    half Density, half BlobId, half3 WorldPos,
    half Threshold, half EdgeSoftness, half UndulationAmount, half UndulationScale, half LipWidth,
    out half Body, out half Lip, out half Fill)
{
    float b, l, f;
    FogShape_float(Density, BlobId, WorldPos, Threshold, EdgeSoftness,
                   UndulationAmount, UndulationScale, LipWidth, b, l, f);
    Body = (half)b; Lip = (half)l; Fill = (half)f;
}

#endif
