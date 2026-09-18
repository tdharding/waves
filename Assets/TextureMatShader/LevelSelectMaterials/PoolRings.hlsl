// The rings drawn on a pool's water, and nothing else.
//
// A POOL is a bowl with a middle, so its lines are rings coming out of that middle. A RIVER is a
// channel with two banks, and its lines lie along those banks — a different shape of water and a
// different drawing, kept in RiverEdgeRipples.hlsl.
//
// A ring is drawn from the DISTANCE OUT FROM THE MIDDLE, measured in world space from the pool
// object's origin, plus a distortion that is a noise field fixed in the world — the rock rings'
// construction. No heading goes anywhere near it: a heading has no width at the middle of the
// bowl, and anything measured in headings pinches into a whorl there. Nothing baked into the
// mesh reaches it either, so the way the water is cut into faces cannot show.
//
// The shared build of a line is kept exactly as the river's, so the two waters still read as one
// scene:
//   * a line is a WINDOW cut out of its cycle, not a sine, so widening one lights more of the
//     cycle without also brightening it;
//   * the phase is SUBTRACTED, which is what carries the rings outward rather than pulsing them;
//   * the output is white by construction and floored at zero, so whatever it feeds, an Add can
//     only ever brighten, and Strength at 0 gives the water back exactly as it was.
//
// Everything comes in as a number rather than read off a global, so there is one place the pool's
// numbers are read — the globals at the top of RiverEdgeRipples.hlsl.

#ifndef POOL_RINGS_INCLUDED
#define POOL_RINGS_INCLUDED

// Radius   : metres out from the pool's middle, in world space.
// Distort  : how far the rings are pushed at this pixel, in ring-widths, already scaled.
// Time     : seconds, for carrying the rings outward.
//
// Ripples  : >= 0, already scaled by Strength. 0 anywhere no ring falls, so it stays neutral
//            into an Add. Not capped at 1 — the overbright headroom is for bloom to find.
// Mask     : 1 here; Pool Reach is applied by the caller in RiverEdgeRipples.hlsl.
void PoolRings(
    float Radius,
    float Distort,
    float Time,
    float Spacing,
    float Strength,
    float Speed,
    float Width,
    float Softness,
    out float Ripples,
    out float Mask)
{
    Ripples = 0.0;
    Mask    = 1.0;

    if (Strength <= 0.0) return;

    float spacing = max(Spacing, 0.0001);

    // Counted in whole rings, so the window below is a plain fraction of a cycle. SUBTRACTING the
    // phase is what carries the rings: a crest sits where the cycle comes round, and that place is
    // Speed metres further out every second. Negative Speed draws them back in.
    float cyc     = Radius / spacing + Distort - Time * (Speed / spacing);
    float centred = abs(frac(cyc) - 0.5) * 2.0;   // 0 at a ring's centre, 1 at the cycle edge

    // The window. Held just under 1 so neighbouring rings always keep a seam between them rather
    // than merging into a wash. Softness slides the inner edge of the falloff in toward the
    // centre. The epsilon keeps the two apart, since smoothstep divides by the gap.
    float w     = clamp(Width, 0.01, 0.98);
    float soft  = saturate(Softness);
    float inner = w * (1.0 - soft);
    float outer = max(w, inner + 1e-4);
    float pulse = 1.0 - smoothstep(inner, outer, centred);

    Ripples = max(pulse * Strength, 0.0);
}

#endif
