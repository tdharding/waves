// Grain and transparency — the two things that stop cheap 3D fog reading as rubber.
//
// The test runs came out looking like moulded plastic: too solid, too glossy, too even. Squash
// cures most of it in the preset, and these two cure the rest at the shader end. Thin limb tips
// go see-through so they melt into the water instead of ending as cut-out shapes, and grain
// breaks the smooth sheen the sphere shading otherwise gives.
//
// Grain is sampled in BLOB SPACE, offset by blob id, for the same reason undulation is: sampled
// in world space it stays pinned to the water and crawls across a mass as it drifts.

#ifndef FOG_GRAIN_INCLUDED
#define FOG_GRAIN_INCLUDED

// Must match FOG_BLOB_SLOTS in FogFieldManager.cs — the id written into the grid is an index into
// this array, so the two sizes are a matched pair.
#define FOG_BLOB_SLOTS 64

float4 _FogBlobCentres[FOG_BLOB_SLOTS];   // xy = world XZ of each live mass

float FogGrain_Hash(float2 p)
{
    return frac(sin(dot(p, float2(269.5, 183.3))) * 43758.5453);
}

float FogGrain_Noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = FogGrain_Hash(i);
    float b = FogGrain_Hash(i + float2(1, 0));
    float c = FogGrain_Hash(i + float2(0, 1));
    float d = FogGrain_Hash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

void FogGrain_float(
    float3 WorldPos,
    float  BlobId,
    float  Fill,                  // how far inside the body, from FogShape
    float  GrainAmount,
    float  GrainScale,
    float  TransparencyFalloff,   // how hard thin fog thins out
    float  Time,                  // slow crawl; 0 holds it still
    out float Grain,
    out float Alpha)
{
    // Sample in the mass's own space so the grain travels with it. Rounded to the nearest slot:
    // where two masses overlap the id is a density-weighted blend, so this picks whichever owns
    // more of the pixel, and the seam sits inside a region that is already a mixture of both.
    int slot = clamp((int)round(BlobId * (float)FOG_BLOB_SLOTS), 0, FOG_BLOB_SLOTS - 1);
    float2 centre = _FogBlobCentres[slot].xy;

    float2 p = (WorldPos.xz - centre) * GrainScale + BlobId * 311.0 + Time * 0.03;

    // Two octaves so it reads as texture rather than as television static.
    float n = FogGrain_Noise(p) * 0.65 + FogGrain_Noise(p * 2.7 + 5.1) * 0.35;

    // Floored at black. Past an amount of 1 the swing reaches below zero, and a negative
    // multiplier does not darken the fog — it inverts its colour, which reads as bright wrong-
    // coloured speckle rather than as heavier grain.
    Grain = max(1.0 + (n - 0.5) * 2.0 * GrainAmount, 0.0);

    // How wide the see-through band along the edge is, NOT a curve over the whole body.
    //
    // This was pow(Fill, falloff), which spreads the fade across the entire mass — raising it made
    // the whole blob thinner rather than tightening its edge, which is backwards from what the
    // dial is for. As a width, the body reaches full opacity immediately and only the outer
    // fraction softens, so small values hug the outline and large ones bleed inward.
    Alpha = smoothstep(0.0, max(TransparencyFalloff, 1e-3), saturate(Fill));
}

void FogGrain_half(
    half3 WorldPos, half BlobId, half Fill,
    half GrainAmount, half GrainScale, half TransparencyFalloff, half Time,
    out half Grain, out half Alpha)
{
    float g, a;
    FogGrain_float(WorldPos, BlobId, Fill, GrainAmount, GrainScale, TransparencyFalloff, Time, g, a);
    Grain = (half)g; Alpha = (half)a;
}

#endif
