// SpikeGrain.hlsl
// Custom Function node name: SpikeGrain
// ─────────────────────────────────────────────────────────────────────────────
// Generated stone grain for the procedural spikes. Two grain sizes scattered across the rock by
// a mask, so a spike reads as two kinds of stone meeting rather than one even tooth.
//
// WHERE IT SAMPLES — 3D noise at the object-space position, not across the UVs. Two reasons
// specific to these rocks:
//
//   • No seam. A rock is a closed loop, so grain laid across the U coordinate has to meet itself
//     round the back, and it won't unless the circumference lands on a whole number of noise
//     periods. Sampling position sidesteps the join.
//   • No stretch. ProceduralSpikeMesh bakes the rock's real size into its geometry, so object
//     space IS metres — grain stays the same physical size on a boulder and a needle, and does
//     not crowd together as the rock tapers.
//
// The catch object space brings is that it is IDENTICAL on two clones of the same preset, so
// without a per-instance nudge every rock would carry the same grain in the same places. That is
// handled below, from the instance's own world position, and needs no control.
//
// Room to build on, when you want it. Object-space Y is signed height from the WATERLINE, since
// the generator puts the waterline at y = 0 — a fade at the water needs no extra data. And the
// mesh carries more in its UVs: UV1 has arc length, circumference, height and radius; UV2.x is
// how near a carved groove you are (0 in one, 1 midway, NEGATIVE on the end caps).
//
// Inputs
//   PositionOS Vector3 — Position node, Space = Object. Wired inside the subgraph.
//   Strength   Float   — how far the grain pushes the colour either side of flat. Shared, so the
//                        two stones read as one surface treatment.
//   ScaleA     Float   — features per metre for the first stone. ~8 coarse, ~40 fine.
//   ScaleB     Float   — the same for the second.
//   PatchSize  Float   — how big the patches of each stone are, in metres. Small mottles, large
//                        gives a few broad regions across the rock.
//   Balance    Float   — how much of the rock is stone B. 0 all A, 1 all B, 0.5 an even split.
//   Blend      Float   — how the two meet. 0 a crisp boundary, 1 a long soft fade.
// Outputs
//   Grain      Float   — the raw 0..1 field, for driving anything else.
//   Offset     Float   — signed, already scaled by Strength. ADD into Base Color, where the
//                        sampled texture goes today.
// ─────────────────────────────────────────────────────────────────────────────

#ifndef SPIKE_GRAIN_INCLUDED
#define SPIKE_GRAIN_INCLUDED

float SpikeGrain_Hash(float3 p)
{
    p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Value noise: the eight lattice corners around a point, smoothly blended so the lattice
// itself never shows as creases.
float SpikeGrain_Noise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = SpikeGrain_Hash(i + float3(0, 0, 0));
    float n100 = SpikeGrain_Hash(i + float3(1, 0, 0));
    float n010 = SpikeGrain_Hash(i + float3(0, 1, 0));
    float n110 = SpikeGrain_Hash(i + float3(1, 1, 0));
    float n001 = SpikeGrain_Hash(i + float3(0, 0, 1));
    float n101 = SpikeGrain_Hash(i + float3(1, 0, 1));
    float n011 = SpikeGrain_Hash(i + float3(0, 1, 1));
    float n111 = SpikeGrain_Hash(i + float3(1, 1, 1));

    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

// Three octaves, each half the size and half the weight. Enough tooth for stone without the
// cost of a full fbm, and there are a lot of these on screen at once.
float SpikeGrain_Fbm(float3 p)
{
    float sum = SpikeGrain_Noise(p);
    sum += SpikeGrain_Noise(p * 2.03) * 0.5;
    sum += SpikeGrain_Noise(p * 4.07) * 0.25;
    return sum / 1.75;
}

void SpikeGrain_float(
    float3 PositionOS,
    float  Strength,
    float  ScaleA,
    float  ScaleB,
    float  PatchSize,
    float  Balance,
    float  Blend,
    out float Grain,
    out float Offset)
{
    // Every rock is generated around its own origin, so object space is IDENTICAL on two clones
    // of the same preset — without this they would carry the same grain in the same places, and
    // the repetition shows the moment two of them stand near each other. Nudging the sample point
    // by the instance's own world position hands each rock a different slice of the noise field,
    // while still sampling in object space so the grain stays put on the rock rather than
    // swimming through it as the level moves.
    float3 seed = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float3 p    = PositionOS + frac(seed * 0.137) * 64.0;

    // Which stone is this? A far coarser noise than either grain, thresholded rather than simply
    // blended, so the two sizes meet along a boundary instead of dissolving through each other —
    // that is the difference between "two stones" and "one uneven stone". Blend reopens the
    // crossing when a softer meeting is wanted.
    float mask = SpikeGrain_Noise(p / max(PatchSize, 0.01));
    float edge = lerp(0.002, 0.5, saturate(Blend));
    float thr  = 1.0 - saturate(Balance);
    float t    = smoothstep(thr - edge, thr + edge, mask);   // 0 = stone A, 1 = stone B

    float a = SpikeGrain_Fbm(p * max(ScaleA, 0.01));
    float b = SpikeGrain_Fbm(p * max(ScaleB, 0.01));

    Grain = lerp(a, b, t);

    // Centred on zero so adding it darkens and lightens equally, leaving the rock's average
    // colour where it was.
    Offset = (Grain - 0.5) * 2.0 * Strength;
}

void SpikeGrain_half(
    half3 PositionOS,
    half  Strength,
    half  ScaleA,
    half  ScaleB,
    half  PatchSize,
    half  Balance,
    half  Blend,
    out half Grain,
    out half Offset)
{
    float g, o;
    SpikeGrain_float((float3)PositionOS, (float)Strength,
                     (float)ScaleA, (float)ScaleB,
                     (float)PatchSize, (float)Balance, (float)Blend, g, o);
    Grain  = (half)g;
    Offset = (half)o;
}

#endif
