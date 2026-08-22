// SpikeSpiral.hlsl
// Custom Function node name: SpikeSpiralShade
// ─────────────────────────────────────────────────────────────────────────────
// Darkens the spiral grooves that ProceduralSpikeMesh has already carved into a spike rock.
//
// The spiral is GEOMETRY. The mesh twists, and the grooves are cut along its own edges, so this
// has nothing to work out — the generator bakes where the grooves are straight into the mesh and
// this reads it. That is the whole point: no ridge count, no faces-around, no pitch, nothing to
// push onto the material and nothing to keep in step. Change the shape and the shading follows,
// because it is reporting what was actually carved rather than recomputing it from parameters.
//
// UV2 (Shader Graph channel UV2), written by ProceduralSpikeMesh:
//   x = how near a groove this point is — 0 sitting in one, rising to 1 midway to the next.
//       NEGATIVE on the end caps, which is the not-a-side-wall early-out.
//   y = how much of the full depth was actually cut here. The carve tapers as the rock narrows
//       toward its tip, and this carries that, so the shading fades out with it instead of
//       staying at full strength on a groove that has run out of rock to cut.
//
// Inputs (Custom Function node)
//   UV2        Vector4 — UV node, Channel UV2.
//   Softness   Float   — how wide the dark band is. 0 hugs the groove itself; 1 spreads most of
//                        the way to the next one. Independent of the carve's own softness, so
//                        the shading can be wider than the cut — usually what you want, since
//                        shadow pools past the fold rather than stopping at its edge.
//   Darkness   Float   — how dark the groove goes. 1 = no change, 0 = black.
//   Resolution Float   — texels across, as if the shading were painted into a sheet of that size
//                        and wrapped on. 256 gives a chunky sampled look; 0 = smooth.
// Outputs
//   Shade      Float   — MULTIPLY into Base Color, at the END of the lighting chain.
//   Groove     Float   — the raw 0..1 mask, if you want it driving anything else.
//
// No normal output: the carved mesh already has correct normals, so the rock's own lighting picks
// the folds out on its own. This only deepens them.
// ─────────────────────────────────────────────────────────────────────────────

#ifndef SPIKE_SPIRAL_INCLUDED
#define SPIKE_SPIRAL_INCLUDED

void SpikeSpiralShade_float(
    float4 UV2,
    float  Softness,
    float  Darkness,
    float  Resolution,
    out float Shade,
    out float Groove)
{
    Shade  = 1.0;
    Groove = 0.0;

    // Negative marks an end cap — not a side wall, so nothing to shade.
    if (UV2.x < 0.0) return;

    float nearness = saturate(UV2.x);
    float carved   = saturate(UV2.y);

    // Quantise the lookup so the shading reads as a wrapped low-resolution sheet rather than
    // something computed per-pixel. Snapping the COORDINATE rather than the result is what gives
    // real texel stair-steps along the band edge instead of a smooth edge with a stepped value.
    if (Resolution >= 1.0)
        nearness = (floor(nearness * Resolution) + 0.5) / Resolution;

    // Softness widens the band out from the groove. At 0 only the very bottom of the fold takes
    // it; at 1 it reaches most of the way to the next groove.
    float reach = lerp(0.08, 1.0, saturate(Softness));

    // Scaled by how much was actually cut, so where the carve tapered off near the tip the
    // shading tapers with it and no dark band is left painted on rock that was never grooved.
    Groove = (1.0 - smoothstep(0.0, reach, nearness)) * carved;
    Shade  = lerp(1.0, saturate(Darkness), Groove);
}

void SpikeSpiralShade_half(
    half4 UV2,
    half  Softness,
    half  Darkness,
    half  Resolution,
    out half Shade,
    out half Groove)
{
    float s, g;
    SpikeSpiralShade_float((float4)UV2, (float)Softness, (float)Darkness, (float)Resolution, s, g);
    Shade  = (half)s;
    Groove = (half)g;
}

#endif
