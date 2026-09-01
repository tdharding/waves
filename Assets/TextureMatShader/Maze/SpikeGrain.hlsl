// SpikeGrain.hlsl
// Custom Function node name: SpikeGrain
// ─────────────────────────────────────────────────────────────────────────────
// Generated stone grain for the procedural spikes. Deliberately the plainest useful version —
// one noise field, two knobs — meant to be built on rather than used as-is.
//
// The one decision already made for you is WHERE it samples: 3D noise at the object-space
// position, not across the UVs. Two reasons, both specific to these rocks:
//
//   • No seam. A rock is a closed loop, so grain laid across the U coordinate has to meet itself
//     round the back, and it won't unless the circumference lands on a whole number of noise
//     periods. Sampling position sidesteps the join.
//   • No stretch. ProceduralSpikeMesh bakes the rock's real size into its geometry, so object
//     space IS metres — grain stays the same physical size on a boulder and a needle, and does
//     not crowd together as the rock tapers.
//
// Also worth knowing before you extend it: object-space Y is signed height from the WATERLINE,
// since the generator puts the waterline at y = 0. A fade at the water needs no extra data.
//
// The mesh carries more, if you want the grain to know about the form:
//   UV1  — x arc length up the surface, y circumference here, z height 0..1, w radius here.
//   UV2  — x how near a carved groove (0 in one, 1 midway; NEGATIVE on the end caps),
//          y how much of the full depth was actually cut here.
//
// Inputs
//   PositionOS Vector3 — Position node, Space = Object. Wired inside the subgraph.
//   Scale      Float   — grain features per metre. ~8 coarse, ~40 fine.
//   Strength   Float   — how far it pushes the colour either side of flat.
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
    float  Scale,
    float  Strength,
    out float Grain,
    out float Offset)
{
    Grain  = SpikeGrain_Fbm(PositionOS * max(Scale, 0.01));

    // Centred on zero so adding it darkens and lightens equally, leaving the rock's average
    // colour where it was.
    Offset = (Grain - 0.5) * 2.0 * Strength;
}

void SpikeGrain_half(
    half3 PositionOS,
    half  Scale,
    half  Strength,
    out half Grain,
    out half Offset)
{
    float g, o;
    SpikeGrain_float((float3)PositionOS, (float)Scale, (float)Strength, g, o);
    Grain  = (half)g;
    Offset = (half)o;
}

#endif
