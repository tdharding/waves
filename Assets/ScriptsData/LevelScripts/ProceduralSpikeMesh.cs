using System.Collections.Generic;
using UnityEngine;

// The silhouette of a spike rock, in world units, measured from the waterline.
// One place where an authored SpikeShapeConfig becomes a shape, so the generated mesh, the
// creepy guy's climbing rings, the Spike Studio preview, the Grid Designer drawing and the
// Map UI icon all read the exact same curve.
public struct SpikeProfile
{
    public float radiusBelowSurface, radiusWaterline, radiusMid, radiusTop;
    public float bottomY;      // negative — the base, deep below the surface
    public float midY;         // above the waterline
    public float topY;         // the tip
    public float topRoundness; // 0 = straight up to a flat top, 1 = the upper span domes over

    /// <summary>
    /// World-space profile for a shape preset, optionally scaled up or down as a whole.
    /// A null config falls back to the defaults, so a spike placed before any preset exists
    /// still draws and still builds instead of vanishing.
    /// </summary>
    public static SpikeProfile From(SpikeShapeConfig c, float scale = 1f)
    {
        if (c == null) c = new SpikeShapeConfig();
        float s = scale > 0.0001f ? scale : 1f;

        return new SpikeProfile
        {
            radiusBelowSurface = c.radiusBelowSurface * s,
            radiusWaterline    = c.radiusWaterline    * s,
            radiusMid          = c.radiusMid          * s,
            radiusTop          = c.radiusTop          * s,
            bottomY            = -Mathf.Max(0.01f, c.depthBelowWater  * s),
            midY               = Mathf.Max(0.01f, c.heightAboveWater * s) * Mathf.Clamp01(c.midHeightFraction),
            topY               = Mathf.Max(0.02f, c.heightAboveWater  * s),
            topRoundness       = Mathf.Clamp01(c.topRoundness),
        };
    }

    public static SpikeProfile From(SpikeShapePreset p, float scale = 1f) =>
        From(p != null ? p.config : null, scale);

    public float Height => topY - bottomY;

    /// <summary>
    /// True when the top comes together rather than ending on a plateau — either because the
    /// top width is already nothing, or because roundness has domed it over. Either way the
    /// mesh needs no flat cap up there.
    /// </summary>
    public bool TipIsClosed => radiusTop <= 1e-3f || CapHeight > 1e-5f;

    /// <summary>
    /// Radius of the rock at a height above the waterline. The four authored radii are
    /// control points of a smooth curve, so the mid radius can bulge the spike into a
    /// belly or pinch it into a waist rather than just tapering straight through.
    /// </summary>
    public float RadiusAt(float y)
    {
        // Which of the three spans (base→waterline, waterline→mid, mid→tip) the height falls in.
        if (y <= bottomY) return Mathf.Max(0f, radiusBelowSurface);

        // At the very top the curved cap has already closed the rock to a point. Answering
        // radiusTop here regardless — as this did — flared the last ring straight back out to
        // the full top width and left the rock ending in a trumpet.
        if (y >= topY) return CapHeight > 1e-5f ? 0f : Mathf.Max(0f, radiusTop);

        int   span;
        float t;
        if (y < 0f)         { span = 0; t = Mathf.InverseLerp(bottomY, 0f,   y); }
        else if (y < midY)  { span = 1; t = Mathf.InverseLerp(0f,      midY, y); }
        else                { span = 2; t = Mathf.InverseLerp(midY,    topY, y); }

        float r = CatmullRom(span, t);

        // The curved cap. Without it the rock ends on a flat plateau the width of radiusTop —
        // right for a perch, wrong for a weathered rock. This blends that top width in: the
        // last stretch follows a quarter-ellipse that starts at the top width and closes at
        // the apex, so the flat disc becomes a curved cap of the same width.
        //
        // The cap is as tall as half the plateau is wide — a hemisphere at full strength — and
        // it eats into the rock's height rather than adding to it, so heightAboveWater keeps
        // meaning the same thing. Blended in with a smoothstep (zero slope at both ends) so the
        // cap grows out of the taper below with no crease.
        float capH = CapHeight;
        if (capH > 1e-5f && y > topY - capH)
        {
            float k   = Mathf.InverseLerp(topY - capH, topY, y);
            float cap = radiusTop * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
            r = Mathf.Lerp(r, cap, Mathf.SmoothStep(0f, 1f, k));
        }

        return Mathf.Max(0f, r);
    }

    /// <summary>
    /// How much of the rock's height the curved cap occupies. Zero when the cap is off, rising
    /// to a full hemisphere over the top width — capping a plateau of diameter D costs D/2 of
    /// height, which is simply what a curved cap of that width is.
    /// </summary>
    public float CapHeight => Mathf.Min(radiusTop * topRoundness, (topY - midY) * 0.9f);

    // Catmull-Rom through the four radii. The ends are duplicated so the curve actually
    // passes through the base and tip radii instead of drifting off them.
    float CatmullRom(int span, float t)
    {
        float r0 = radiusBelowSurface, r1 = radiusWaterline, r2 = radiusMid, r3 = radiusTop;

        float pA, pB, pC, pD;
        switch (span)
        {
            case 0:  pA = r0; pB = r0; pC = r1; pD = r2; break;  // duplicated start
            case 1:  pA = r0; pB = r1; pC = r2; pD = r3; break;
            default: pA = r1; pB = r2; pC = r3; pD = r3; break;  // duplicated end
        }

        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * pB)
                     + (-pA + pC) * t
                     + (2f * pA - 5f * pB + 4f * pC - pD) * t2
                     + (-pA + 3f * pB - 3f * pC + pD) * t3);
    }
}

// The spiral groove, cut into the generated mesh rather than faked in the shader.
//
// This is the SAME groove SpikeSpiral.hlsl draws — same phase, same width-in-turns conversion —
// so a rock carved here and shaded there has its dark line sitting in its own indentation,
// provided the pitch and width reaching the material match the ones used to cut it.
//
// Carving in the generator rather than displacing in a vertex shader is what makes this work at
// all: the generator can put rings where the groove needs them and work out the normals from the
// real surface, where a vertex shader can only nudge whatever vertices happen to be there and
// leaves the normals pointing at the old shape.
public struct SpikeRidge
{
    public bool  enabled;
    public int   count;      // how many grooves wind up the rock
    public int   sides;      // faces around, so grooves can be pinned to whole edges
    public float depth;      // how far each one cuts in
    public float softness;   // 0 = a crease on the edge itself, 1 = a broad flute

    public static SpikeRidge From(SpikeShapeConfig c, float scale = 1f)
    {
        if (c == null || !c.carveSpiralRidge) return default;
        float s = scale > 0.0001f ? scale : 1f;
        return new SpikeRidge
        {
            enabled  = true,
            count    = CountFor(c),
            sides    = Mathf.Clamp(c.sidesAround, 3, 64),
            // Depth scales with the rock, so one preset carves the same rock at any size.
            depth    = Mathf.Max(0f, c.ridgeDepth * s),
            softness = Mathf.Clamp01(c.ridgeSoftness),
        };
    }

    /// <summary>Column index of groove number k, always a whole edge.</summary>
    public int GrooveColumn(int k) =>
        Mathf.RoundToInt(k * sides / (float)Mathf.Max(1, count)) % Mathf.Max(1, sides);

    /// <summary>
    /// How many edges to cut, so the spiral lines end up `ridgeSpacing` apart going up the rock.
    ///
    /// A single cut edge wraps the rock `twist` times over its height, so it shows as `twist`
    /// lines stacked up the side. Cutting N edges gives N times that, so the gap between lines
    /// is height / (twist × N) — invert it for N. Scale cancels out, since spacing and height
    /// scale together.
    ///
    /// Snapped to a divisor of Faces around, because a groove only stays razor sharp while it
    /// sits exactly on an edge; a count that doesn't divide would drift between them.
    /// </summary>
    public static int CountFor(SpikeShapeConfig c)
    {
        int   sides = Mathf.Clamp(c.sidesAround, 3, 64);
        float turns = Mathf.Abs(c.twistTurns);

        // Never more than every other edge. A groove needs an uncut edge beside it to be a
        // groove — cut them all and the rock just comes out thinner, with no relief at all.
        int most = Mathf.Max(1, sides / 2);

        if (turns < 0.001f || c.ridgeSpacing < 1e-4f) return most;
        return Mathf.Clamp(Mathf.RoundToInt(c.heightAboveWater / (c.ridgeSpacing * turns)), 1, most);
    }

    /// <summary>The spacing actually achieved after snapping — what the studio reports back.</summary>
    public static float ActualSpacing(SpikeShapeConfig c)
    {
        float turns = Mathf.Abs(c.twistTurns);
        int   n     = CountFor(c);
        return turns < 0.001f || n < 1 ? 0f : c.heightAboveWater / (turns * n);
    }

    /// <summary>
    /// How far into the rock the groove cuts at this point: 0 out on the flat, rising to `depth`
    /// at the bottom of the channel. `u` is 0..1 once around, `arc` is distance up the surface
    /// and `circumference` the rock's girth here — the last of which is what keeps the groove a
    /// constant width in metres as the rock narrows toward its tip.
    /// </summary>
    // Most of the rock's thickness a groove is ever allowed to take. Without this the cut keeps
    // its full depth as the rock narrows, and near the tip it goes straight through the axis —
    // a negative radius, which flips the ring to the far side and turns the mesh inside out.
    const float MaxCutFraction = 0.6f;

    /// <summary>
    /// How far in the rock is cut at column position `u` (0..1 once around). The grooves sit on
    /// the mesh's own edge columns, so no matter how coarse the rock is the cut lands exactly on
    /// geometry — there is nothing to alias against. `localRadius` is the rock's thickness here,
    /// which caps the cut so a narrowing tip can't be carved through.
    /// </summary>
    /// <summary>
    /// How near this point is to a groove, 0 on one and 1 midway between two. Baked into the
    /// mesh so the material never has to re-derive where the grooves are: it reads what was
    /// actually carved, and cannot drift out of step with it.
    /// </summary>
    public float GrooveNearness(float u)
    {
        if (!enabled || count < 1) return 1f;

        float col  = Frac(u) * sides;
        float best = sides;
        for (int k = 0; k < count; k++)
        {
            float d = Mathf.Abs(col - GrooveColumn(k));
            best = Mathf.Min(best, Mathf.Min(d, sides - d));
        }
        return Mathf.Clamp01(best / Mathf.Max(0.5f * sides / count, 1e-4f));
    }

    public float DepthAt(float u, float localRadius)
    {
        if (!enabled || depth <= 0f || count < 1) return 0f;

        // Distance to the nearest groove, measured in EDGES. Each groove is pinned to a whole
        // column, found by name rather than by an even division — so the count no longer has to
        // divide into Faces around. With 7 faces that used to leave only 1 groove or 7 (every
        // column, which is no groove at all, just a thinner rock); now anything up to half works.
        float col  = Frac(u) * sides;
        float best = float.MaxValue;
        for (int k = 0; k < count; k++)
        {
            float d = Mathf.Abs(col - GrooveColumn(k));
            best = Mathf.Min(best, Mathf.Min(d, sides - d));   // wrap round the seam
        }

        // Reach measured in EDGES, and it has to scale with how far apart the grooves are.
        //
        // At 0 it spans half an edge: only the groove's own column moves and the rock between
        // grooves is left exactly alone — a hard crease. At 1 it reaches most of the way to the
        // neighbouring groove, drawing a broad soft fold across many columns, which is what a
        // draped-looking spiral needs.
        //
        // Capping it below one edge (as this did) meant the reach could never touch the
        // neighbouring column at all, so 0 and 1 produced an identical mesh and the control
        // appeared dead.
        float spacing = sides / (float)count;                 // edges from one groove to the next
        float half    = Mathf.Lerp(0.5f, Mathf.Max(1.5f, spacing * 0.9f), softness);

        float cut = depth * (1f - Mathf.SmoothStep(0f, half, best));

        return Mathf.Min(cut, Mathf.Max(0f, localRadius) * MaxCutFraction);
    }

    static float Frac(float v) => v - Mathf.Floor(v);
}

// Builds the spike mesh by sweeping the SpikeProfile around a vertical axis.
//
// The mesh is built around the object origin so that the waterline sits at y = 0 — the same
// convention as ProceduralBoxMesh, so LevelSpawner's PrefabBaselineAlignment offset drops
// the spike onto the water plane without any per-spike fixup.
//
// UVs are laid out for a spiral material:
//   • uv0 — U runs 0..1 once around the spike, V runs 0..1 bottom to tip. A helix is then
//     just frac(U * turns + V * pitch): no seam maths, and the seam column is duplicated so
//     U reaches a clean 1 instead of wrapping back to 0 across the last face.
//   • uv1 — (distance up the surface, circumference here, height 0..1, radius here), all in
//     world units. Lets a spiral hold a constant band width as the spike tapers, instead of
//     the lines crowding together toward the tip. Same idea as the window-tiling UV2 on blocks.
//     The caps leave it zeroed so a shader can skip them on w <= 0.
public static class ProceduralSpikeMesh
{
    /// <summary>
    /// Sweeps the profile into a closed mesh. sidesAround = faces around the spike;
    /// heightSubdivisions = extra rings generated within each of the three spans. Pass a
    /// SpikeRidge to cut the spiral in as real geometry — it raises the density itself to
    /// whatever the groove needs, taking the two counts as a floor.
    /// </summary>
    /// <summary>
    /// The heights the mesh puts its rings at. Shared so anything drawing the rock — the Spike
    /// Studio's gizmo, for one — walks the same rings the geometry uses and cannot drift from it.
    /// </summary>
    public static List<float> RingHeights(SpikeProfile p, int heightSubdivisions, float twistTurns)
    {
        int sub = Mathf.Clamp(heightSubdivisions, 1, 16);

        // Rings above and below the waterline are counted separately: a rock is typically a
        // couple of metres proud and many metres deep, so any extra rings are worth spending
        // where they show rather than on the submerged stub nobody ever sees.
        int subUnder = sub;
        int subOver  = sub;

        // A twisted rock needs enough rings that each one only rotates a little; too few and the
        // helix reads as a stack of rotated hoops. Roughly a ring every 15 degrees of turn.
        // Nothing like the density the old sampled carve wanted — the ridges are edges now, so
        // they stay sharp however coarse the rock is.
        if (Mathf.Abs(twistTurns) > 0.001f)
            subOver = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(twistTurns) * 24f / 3f), sub, 64);

        // The authored spans, each cut into steps, sharing endpoints. A rounded top is its own
        // span with its own (denser) rings — the cap turns through a right angle in a short
        // stretch, and at the body's step count it would read as a bevel.
        float capH = p.CapHeight;

        var heights = new List<float>(subUnder + 3 * subOver + 8);
        AddSpan(heights, p.bottomY, 0f,     subUnder, first: true);
        AddSpan(heights, 0f,        p.midY, subOver,  first: false);

        if (capH > 1e-5f)
        {
            int capSteps = Mathf.Max(5, subOver);
            AddSpan(heights, p.midY,        p.topY - capH, subOver,  first: false);
            AddSpan(heights, p.topY - capH, p.topY,        capSteps, first: false);
        }
        else
        {
            AddSpan(heights, p.midY, p.topY, subOver, first: false);
        }
        return heights;
    }

    /// <summary>
    /// How far each ring is turned. Rotating progressively with height is what carries the mesh's
    /// own vertical edges round the rock as helices — and the carved ridges wind with them.
    ///
    /// Measured over the height ABOVE WATER, not the full span. A rock is typically a metre or so
    /// proud and five metres deep, so spreading the turns over the whole span spent most of them
    /// on the submerged stub and left the visible rock barely twisted — a nearly vertical line
    /// where a spiral was asked for. Zero at the waterline, full turns at the tip, and it carries
    /// on below at the same rate so the surface stays continuous.
    /// </summary>
    public static float TwistAtHeight(SpikeProfile p, float y, float twistTurns) =>
        twistTurns * 2f * Mathf.PI * (Mathf.Max(0f, y) / Mathf.Max(1e-4f, p.topY));

    /// <summary>
    /// The path each carved groove takes over the rock, in object space, one polyline per groove.
    /// Read off the same rings, twist and carve depth the mesh is built from, so a gizmo drawn
    /// from this sits exactly in the geometry rather than approximating it. Empty when nothing
    /// is being carved.
    /// </summary>
    public static List<Vector3[]> RidgeLines(SpikeProfile p, int sidesAround, int heightSubdivisions,
                                             SpikeRidge ridge, float twistTurns)
    {
        var lines = new List<Vector3[]>();
        if (!ridge.enabled || ridge.depth <= 0f) return lines;

        int sides   = Mathf.Clamp(sidesAround, 3, 64);
        var heights = RingHeights(p, heightSubdivisions, twistTurns);

        // Asks the ridge itself which columns it cuts, rather than stepping evenly. An even step
        // only agrees with the real grooves when the count divides into the faces — with 7 faces
        // and 2 grooves it drew columns 0/3/6 while the carve was on 0/4, so the lines sat where
        // the rock was NOT cut and the indentation looked inverted.
        for (int k = 0; k < ridge.count; k++)
        {
            float u   = ridge.GrooveColumn(k) / (float)sides;
            var   pts = new Vector3[heights.Count];
            for (int r = 0; r < heights.Count; r++)
            {
                float y     = heights[r];
                float baseR = p.RadiusAt(y);
                float rad   = Mathf.Max(0f, baseR - ridge.DepthAt(u, baseR));
                float a     = u * Mathf.PI * 2f + TwistAtHeight(p, y, twistTurns);
                pts[r] = new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad);
            }
            lines.Add(pts);
        }
        return lines;
    }

    public static Mesh Build(SpikeProfile p, int sidesAround, int heightSubdivisions,
                             SpikeRidge ridge = default, float twistTurns = 0f)
    {
        int sides = Mathf.Clamp(sidesAround, 3, 64);

        var heights = RingHeights(p, heightSubdivisions, twistTurns);

        int rings   = heights.Count;
        int perRing = sides + 1;                       // +1 duplicated seam column so U ends at 1

        var radii = new float[rings];
        for (int r = 0; r < rings; r++) radii[r] = p.RadiusAt(heights[r]);

        // Distance travelled up the surface, so a spiral can be spaced in world units.
        var arc = new float[rings];
        for (int r = 1; r < rings; r++)
        {
            float dy = heights[r] - heights[r - 1];
            float dr = radii[r]   - radii[r - 1];
            arc[r] = arc[r - 1] + Mathf.Sqrt(dy * dy + dr * dr);
        }

        float span = Mathf.Max(1e-4f, p.Height);

        var verts = new List<Vector3>(rings * perRing + sides * 2 + 2);
        var norms = new List<Vector3>(verts.Capacity);
        var uvs   = new List<Vector2>(verts.Capacity);
        var uv2s  = new List<Vector4>(verts.Capacity);
        var uv3s  = new List<Vector2>(verts.Capacity);   // baked groove info, see below
        var tris  = new List<int>(rings * sides * 6 + sides * 6);

        // The carved surface as one function of (angle, height): the profile radius with the
        // spiral groove cut out of it. Both the positions and the normals come from this, so
        // they can't disagree about where the channel is.
        //
        // The groove is spaced against the UNCARVED circumference — feeding the carved radius
        // back in would make the channel change width as it cuts, and wander.
        // How far each ring is rotated. Turning progressively with height is what carries the
        // mesh's own vertical edges round the rock as helices — and the ridges, cut along those
        // same edges, wind with them.
        float TwistAt(int r) => TwistAtHeight(p, heights[r], twistTurns);

        // The carved surface as one function of (column, ring). Both positions and normals come
        // from this, so they cannot disagree about where the grooves are.
        float SurfaceRadius(float u, int r)
        {
            float baseR = radii[r];
            if (!ridge.enabled) return baseR;

            // Never past the axis. DepthAt already tapers the cut as the rock thins, but at a
            // tip of exactly zero radius rounding can still leave a hair on the wrong side —
            // and a negative radius flips the ring to the far side, turning the mesh inside out.
            return Mathf.Max(0f, baseR - ridge.DepthAt(u, baseR));
        }

        // ── Side wall ──
        for (int r = 0; r < rings; r++)
        {
            float y = heights[r];
            float v = (y - p.bottomY) / span;

            int   rPrev = Mathf.Max(0, r - 1);
            int   rNext = Mathf.Min(rings - 1, r + 1);
            float dY    = heights[rNext] - heights[rPrev];

            float twist = TwistAt(r);

            // Rate the twist turns per unit height — the extra term the normal needs, since a
            // twisted surface leans sideways as it rises even where the profile doesn't taper.
            float dTdY = Mathf.Approximately(dY, 0f) ? 0f : (TwistAt(rNext) - TwistAt(rPrev)) / dY;

            for (int i = 0; i < perRing; i++)
            {
                float u   = i / (float)sides;
                float a   = u * Mathf.PI * 2f + twist;
                float sin = Mathf.Sin(a), cos = Mathf.Cos(a);

                float radius = SurfaceRadius(u, r);
                verts.Add(new Vector3(sin * radius, y, cos * radius));

                // Normal from the two partial derivatives of the twisted, carved surface, in
                // (column, height). Untwisted and uncarved this reduces to the plain
                // (sin, -slope, cos); the carve adds the term round the rock that makes a
                // groove's walls catch the light, and the twist adds the sideways lean.
                float dRdY = Mathf.Approximately(dY, 0f)
                           ? 0f
                           : (SurfaceRadius(u, rNext) - SurfaceRadius(u, rPrev)) / dY;

                float dRdU = 0f;
                if (ridge.enabled)
                {
                    const float e = 1e-3f;
                    dRdU = (SurfaceRadius(u + e, r) - SurfaceRadius(u - e, r)) / (2f * e);
                }

                // P(u,y) = (R sin a, y, R cos a) with a = 2πu + twist(y), R = R(u,y).
                float   dAdU = 2f * Mathf.PI;
                Vector3 dPdU = new Vector3(dRdU * sin + radius * cos * dAdU, 0f,
                                           dRdU * cos - radius * sin * dAdU);
                Vector3 dPdY = new Vector3(dRdY * sin + radius * cos * dTdY, 1f,
                                           dRdY * cos - radius * sin * dTdY);
                Vector3 n    = Vector3.Cross(dPdU, dPdY);

                norms.Add(n.sqrMagnitude > 1e-12f
                          ? n.normalized
                          : new Vector3(sin, -dRdY, cos).normalized);

                // UVs stay on the UNTWISTED column parameter, so a shader stripe at constant U
                // rides the same helix the geometry does — the twist does the spiralling, and
                // the shader only has to draw straight bands.
                uvs.Add(new Vector2(u, v));
                uv2s.Add(new Vector4(arc[r], 2f * Mathf.PI * radii[r], v, radii[r]));

                // Where the grooves are, baked in — x = 0 on a groove rising to 1 midway to the
                // next, y = how much of the full depth was actually cut here (the taper near the
                // tip shows up in it). A material shading the folds reads these instead of being
                // told the ridge count and faces around and recomputing the same thing, so it
                // cannot fall out of step with the geometry.
                float nearness = ridge.enabled ? ridge.GrooveNearness(u) : 1f;
                float cutFrac  = ridge.enabled && ridge.depth > 0f
                               ? Mathf.Clamp01(ridge.DepthAt(u, radii[r]) / ridge.depth) : 0f;
                uv3s.Add(new Vector2(nearness, cutFrac));
            }
        }

        // Wound so the outside faces out. The ring runs from +Z toward +X as i increases, which
        // is clockwise seen from above, so the quad has to be issued a→c→d / a→b→c to put the
        // front face on the outside — the same convention ProceduralBoxMesh uses, where
        // cross(v1-v0, v2-v0) lands on the face's declared normal. Reversed, the rock renders
        // as its own backfaces: a black shape you can see the inside of.
        for (int r = 0; r < rings - 1; r++)
        {
            int row = r * perRing, next = (r + 1) * perRing;
            for (int i = 0; i < sides; i++)
            {
                int a = row + i, b = row + i + 1, c = next + i + 1, d = next + i;
                tris.Add(a); tris.Add(c); tris.Add(d);
                tris.Add(a); tris.Add(b); tris.Add(c);
            }
        }

        // ── Caps ──
        // The base is closed so the MeshCollider is solid; the tip only earns a cap when it's
        // flat enough to see. Both leave uv1 zeroed so a spiral shader can skip them.
        AddCap(verts, norms, uvs, uv2s, uv3s, tris, sides, p.bottomY, radii[0], Vector3.down);
        if (!p.TipIsClosed && radii[rings - 1] > 1e-3f)
            AddCap(verts, norms, uvs, uv2s, uv3s, tris, sides, p.topY, radii[rings - 1], Vector3.up);

        var mesh = new Mesh();
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv2s);
        mesh.SetUVs(2, uv3s);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Rough distance up the rock's surface from `fromY`, used only to decide how many rings a
    // carved groove needs. Sampled coarsely on purpose — it feeds a ceiling, not the geometry.
    static float ApproxSurfaceLength(SpikeProfile p, float fromY)
    {
        const int Samples = 64;
        float total = 0f, prevY = fromY, prevR = p.RadiusAt(fromY);
        for (int i = 1; i <= Samples; i++)
        {
            float y = Mathf.Lerp(fromY, p.topY, i / (float)Samples);
            float r = p.RadiusAt(y);
            total += Mathf.Sqrt((y - prevY) * (y - prevY) + (r - prevR) * (r - prevR));
            prevY = y; prevR = r;
        }
        return Mathf.Max(total, 1e-3f);
    }

    // Ring heights for one span. `first` also emits the starting height; later spans skip it
    // so neighbouring spans share a ring instead of stacking two on top of each other.
    static void AddSpan(List<float> into, float from, float to, int steps, bool first)
    {
        if (first) into.Add(from);
        for (int i = 1; i <= steps; i++) into.Add(Mathf.Lerp(from, to, i / (float)steps));
    }

    // Flat disc closing one end, as its own fan so the cap keeps a hard edge against the wall.
    static void AddCap(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
                       List<Vector4> uv2s, List<Vector2> uv3s, List<int> tris,
                       int sides, float y, float radius, Vector3 normal)
    {
        // -1 in the groove channel marks "not a side wall", so a material can drop the caps
        // without needing any other input to tell it apart.
        var capMark = new Vector2(-1f, 0f);

        int centre = verts.Count;
        verts.Add(new Vector3(0f, y, 0f));
        norms.Add(normal);
        uvs.Add(new Vector2(0.5f, 0.5f));
        uv2s.Add(Vector4.zero);
        uv3s.Add(capMark);

        for (int i = 0; i < sides; i++)
        {
            float a = (i / (float)sides) * Mathf.PI * 2f;
            float sin = Mathf.Sin(a), cos = Mathf.Cos(a);
            verts.Add(new Vector3(sin * radius, y, cos * radius));
            norms.Add(normal);
            uvs.Add(new Vector2(0.5f + 0.5f * sin, 0.5f + 0.5f * cos));
            uv2s.Add(Vector4.zero);
            uv3s.Add(capMark);
        }

        bool upward = normal.y > 0f;
        for (int i = 0; i < sides; i++)
        {
            int a = centre + 1 + i;
            int b = centre + 1 + ((i + 1) % sides);
            if (upward) { tris.Add(centre); tris.Add(a); tris.Add(b); }
            else        { tris.Add(centre); tris.Add(b); tris.Add(a); }
        }
    }
}
