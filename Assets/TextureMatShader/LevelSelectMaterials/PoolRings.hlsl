// The rings drawn on a pool's water, and nothing else.
//
// A POOL is a bowl with a middle, so its lines are rings coming out of that middle. A RIVER is a
// channel with two banks, and its lines lie along those banks — a different shape of water and a
// different drawing, kept in RiverEdgeRipples.hlsl. The two used to share one piece of maths and
// the rings paid for it, so they are apart now: nothing a river's lines do can reach a ring.
//
// A RING IS A CIRCLE. That is the whole of it, and it is the reason this file exists.
//
// What a ring is drawn from is the DISTANCE OUT FROM THE MIDDLE and nothing else. No heading goes
// anywhere near it. The moment a heading is added to that distance — a swing round the ring, a
// wander that knows which way it is facing — the drawing stops being circles: a heading has no
// width at the middle of the bowl, every direction meets at that one point, and whatever the
// heading is worth is being asked to fit into no room at all. It comes out as a pinch, a whorl
// of arms winding out of the middle, and no amount of softening the numbers takes it away,
// because the fault is in asking the question at the middle rather than in the answer. So it is
// never asked here. The rings shrink to a point at the middle the way rings on real water do.
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
// numbers are read — the globals at the top of RiverEdgeRipples.hlsl — and this cannot drift out
// of step with the tuner that writes them.

#ifndef POOL_RINGS_INCLUDED
#define POOL_RINGS_INCLUDED

// Radius   : metres out from the pool's middle, taken as the length of the offset the mesh bakes
//            in. A LENGTH, never an angle: an offset interpolates exactly across a face because
//            the water really is flat, so the rings come out true at any tessellation.
// Edge     : metres to the pool's own wall or island, already folded. Open water (a mouth, and the
//            strips running out into the rivers) carries a number far past PoolFade.
// Time     : seconds, for carrying the rings outward.
//
// The lap weight does not come in here: it is the alpha, and the alpha alone blends a pool into
// its rivers. The rings themselves run the full extent of the pool's mesh.
//
// Ripples  : >= 0, already scaled by Strength. 0 anywhere no ring falls, so it stays neutral
//            into an Add. Not capped at 1 — the overbright headroom is for bloom to find.
// Mask     : 1 where the rings are at full, 0 where they are gone at the water's edge.
void PoolRings(
    float Radius,
    float Edge,
    float Time,
    float Spacing,
    float Strength,
    float Speed,
    float Width,
    float Softness,
    float PoolFade,
    out float Ripples,
    out float Mask)
{
    Ripples = 0.0;

    // The rings fill the whole bowl rather than hugging its wall, so the only place they ease off
    // is the last stretch before the water meets that wall or its island — otherwise they would
    // end on a hard line where the surface stops.
    Mask = saturate(Edge / max(PoolFade, 0.0001));

    if (Strength <= 0.0 || Mask <= 0.0) return;

    float spacing = max(Spacing, 0.0001);

    // Counted in whole rings rather than radians, so the window below is a plain fraction of a
    // cycle. SUBTRACTING the phase is what carries the rings: a crest sits where the cycle comes
    // round, and that place is Speed metres further out every second. Negative Speed draws them
    // back in towards the middle.
    float cyc     = Radius / spacing - Time * (Speed / spacing);
    float centred = abs(frac(cyc) - 0.5) * 2.0;   // 0 at a ring's centre, 1 at the cycle edge

    // The window. Held just under 1 so neighbouring rings always keep a seam between them rather
    // than merging into a wash. Softness slides the inner edge of the falloff in toward the
    // centre: at 0 the ring has a hard rim, at 1 it falls away from its middle. The epsilon keeps
    // the two apart, since smoothstep divides by the gap.
    float w     = clamp(Width, 0.01, 0.98);
    float soft  = saturate(Softness);
    float inner = w * (1.0 - soft);
    float outer = max(w, inner + 1e-4);
    float pulse = 1.0 - smoothstep(inner, outer, centred);

    Ripples = max(pulse * Mask * Strength, 0.0);
}

#endif
