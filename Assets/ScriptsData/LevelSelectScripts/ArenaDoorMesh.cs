using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the door standing in an arena's archway: the sheet that fills the arch's opening, the
/// keyhole frame standing proud on it, the panel filling the keyhole inside that frame, and the
/// rim round the soul opening at its foot.
///
/// Built in the archway's own local space, the same frame <see cref="ArenaArchwayMesh"/> uses:
///   x  across the river, 0 on its centreline
///   y  up from the rim top, which is 0 — so the water, and the door's foot, are below it
///   z  along the river, 0 at the arch's front face on the arena side and running out over the
///      water, so the frame stands proud toward the boat coming in
///
/// The sheet is solid everywhere but the soul opening, which is cut clean through: the columns
/// either side of it run floor to crown, and the ones across it stop under its foot and start
/// again over its crown. That hole is the only way to see through the door.
///
/// The sheet's curved top is the very ellipse the arch's inner edge is swept along, walked at
/// the same stations, so the two meet exactly rather than nearly.
/// </summary>
public static class ArenaDoorMesh
{
    // How far round the bulb, from straight down, the neck curve meets it. Not a setting: it is
    // what makes a keyhole read as a keyhole, and nothing good comes of moving it.
    private const float BulbAttachDegrees = 60f;

    /// <summary>Which part of the door a triangle belongs to — see the parts list on Build.</summary>
    public enum Part { Sheet, Frame, Panel, FlapRim, Disc, Edges }

    /// <summary>
    /// Builds the whole door as one mesh.
    ///
    /// <paramref name="arch"/> and <paramref name="door"/> must both already be resolved — see
    /// <see cref="ArenaArchwayProfile.Resolve"/> and <see cref="ArenaDoorProfile.Resolve"/>.
    /// <paramref name="floor"/> is how far the channel floor sits below the rim top, so the sheet
    /// reaches down to it; <paramref name="waterY"/> is where the water surface sits in this
    /// frame, which is what the door stands on. <paramref name="along"/> is how far into the arch
    /// the sheet stands, and <paramref name="edge"/> the target edge length everything is walked
    /// at — the same number the runs and the arch are built from.
    /// </summary>
    public static Mesh Build(ArenaArchwayProfile arch, ArenaDoorProfile door,
                             float floor, float waterY, float along, float edge,
                             List<Part> parts = null)
    {
        var b = new DoorBuild { Parts = parts };
        if (arch == null || door == null) return b.ToMesh("ArenaDoor");

        edge = Mathf.Max(edge, 0.005f);

        float half   = Mathf.Max(0.005f, arch.openingWidth * 0.5f);
        float leg    = Mathf.Max(0f,     arch.legHeight);
        float crown  = Mathf.Max(0.005f, arch.archHeight);
        float bottom = -Mathf.Max(0f, floor);

        // The door stands on the water, but never below the sheet it is drawn on.
        float foot = Mathf.Max(bottom, waterY);

        Sheet(b, door, half, leg, crown, bottom, foot, along, edge);
        Frame(b, door, foot, along, edge);
        BuildPanel(b, door, foot, along, edge);
        FlapRim(b, door, foot, along, edge);
        Disc(b, door, foot, along, edge);

        return b.ToMesh("ArenaDoor");
    }

    // ─────────────────────────────────────────────────────────────
    // THE SHEET
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The sheet filling the arch's opening, with the soul opening cut out of it. Walked as
    /// columns across the opening rather than fanned from the middle, because a fan cannot hold
    /// a hole — every triangle of one touches its hub.
    ///
    /// Faces both ways: it is seen from the river on the way in and from the arena on the way out.
    /// </summary>
    private static void Sheet(DoorBuild b, ArenaDoorProfile door, float half, float leg, float crown,
                              float bottom, float foot, float along, float edge)
    {
        float fHalf = door.flapWidth * 0.5f;
        float top   = leg + crown;

        b.Part = Part.Sheet;

        foreach (var span in Columns(door, half, leg, crown, edge))
        {
            float x0 = span.Item1, x1 = span.Item2;
            float xm = (x0 + x1) * 0.5f;

            if (Mathf.Abs(xm) >= fHalf)
            {
                Panel(x0, bottom, ArchTop(x0, half, leg, crown),
                      x1, bottom, ArchTop(x1, half, leg, crown));
                continue;
            }

            // Across the opening: under its foot — which sags, so the hole reaches below the
            // water in the middle — and over its crown.
            Panel(x0, bottom, Mathf.Max(bottom, foot + FlapBottom(x0, door)),
                  x1, bottom, Mathf.Max(bottom, foot + FlapBottom(x1, door)));

            Panel(x0, foot + FlapTop(x0, door), ArchTop(x0, half, leg, crown),
                  x1, foot + FlapTop(x1, door), ArchTop(x1, half, leg, crown));
        }

        // One quad of the sheet, both ways round, with UV0 running 0 to 1 across the opening and
        // from the floor to the crown.
        void Panel(float xa, float ya0, float ya1, float xb, float yb0, float yb1)
        {
            if (ya1 - ya0 < 0.0001f && yb1 - yb0 < 0.0001f) return;

            Vector3 a = P(xa, ya0), d = P(xa, ya1);
            Vector3 c = P(xb, yb0), e = P(xb, yb1);

            b.Quad(a, c, e, d, UV(a), UV(c), UV(e), UV(d), Vector3.forward);
            b.Quad(a, d, e, c, UV(a), UV(d), UV(e), UV(c), Vector3.back);
        }

        Vector3 P(float x, float y) => new Vector3(x, y, along);

        Vector2 UV(Vector3 p) => new Vector2(Mathf.InverseLerp(-half, half, p.x),
                                             Mathf.InverseLerp(bottom, top, p.y));
    }

    /// <summary>
    /// Where the columns of the sheet are cut. Off the arch's own crown stations, so the sheet's
    /// top edge lands on the arch's inner edge station for station; plus the soul opening's, so
    /// its curve comes out as clean as the arch's; plus its two sides exactly, so the hole has
    /// straight edges rather than a stepped approximation of them.
    /// </summary>
    private static List<System.Tuple<float, float>> Columns(ArenaDoorProfile door, float half,
                                                            float leg, float crown, float edge)
    {
        float fHalf = door.flapWidth * 0.5f;
        var   xs    = new List<float> { -half, half, -fHalf, fHalf };

        int archSeg = ArenaArchwayMesh.CrownSegments(half, crown, edge);
        for (int i = 1; i < archSeg; i++)
            xs.Add(Mathf.Cos(Mathf.PI * (1f - (float)i / archSeg)) * half);

        int flapSeg = ArenaArchwayMesh.CrownSegments(fHalf, door.flapCrownHeight, edge);
        for (int i = 1; i < flapSeg; i++)
            xs.Add(Mathf.Cos(Mathf.PI * (1f - (float)i / flapSeg)) * fHalf);

        // The legs are straight up the sides, so the crown's stations say nothing about how
        // finely the rest of the sheet is broken up. A tall arch still wants columns of its own.
        int legSteps = leg <= 0.0001f ? 0 : Mathf.CeilToInt(leg / edge);
        for (int i = 1; i < legSteps; i++)
            xs.Add(Mathf.Lerp(-half, half, (float)i / legSteps));

        xs.Sort();

        var spans = new List<System.Tuple<float, float>>();
        for (int i = 0; i + 1 < xs.Count; i++)
            if (xs[i + 1] - xs[i] > 0.0001f)
                spans.Add(System.Tuple.Create(xs[i], xs[i + 1]));
        return spans;
    }

    /// <summary>The underside of the arch at one x — the ellipse its crown is swept along.</summary>
    private static float ArchTop(float x, float half, float leg, float crown)
    {
        float t = Mathf.Clamp(x / half, -1f, 1f);
        return leg + crown * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
    }

    /// <summary>
    /// The bottom of the soul opening at one x, measured from the door's foot — 0 where it
    /// stands flat on the water, and hanging below it where it has been given a sag.
    /// </summary>
    private static float FlapBottom(float x, ArenaDoorProfile door)
    {
        if (door.flapCurve <= 0.0001f) return 0f;

        float fHalf = Mathf.Max(0.001f, door.flapWidth * 0.5f);
        float t     = Mathf.Clamp(x / fHalf, -1f, 1f);
        return -door.flapCurve * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
    }

    /// <summary>The top of the soul opening at one x, measured up from the door's foot.</summary>
    private static float FlapTop(float x, ArenaDoorProfile door)
    {
        float fHalf = Mathf.Max(0.001f, door.flapWidth * 0.5f);
        float t     = Mathf.Clamp(x / fHalf, -1f, 1f);
        return door.flapLegHeight + door.flapCrownHeight * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
    }

    // ─────────────────────────────────────────────────────────────
    // THE BANDS
    // ─────────────────────────────────────────────────────────────

    /// <summary>The frame running round the keyhole, standing proud of the sheet.</summary>
    private static void Frame(DoorBuild b, ArenaDoorProfile door, float foot, float along, float edge)
    {
        KeyholeOutlines(door, edge, out var inner, out var outer, out bool closed);
        Band(b, inner, outer, foot, along, door.frameDepth, Part.Frame, closed);
    }

    /// <summary>
    /// The keyhole's two outlines — the silhouette itself, and the same shape drawn in by the
    /// frame width. Handed out rather than built inside <see cref="Frame"/> because the panel
    /// inside the frame fills exactly the inner one, and the two have to be walked at the same
    /// stations or the panel's edge and the frame's wall nearly meet instead of exactly meeting.
    ///
    /// <paramref name="closed"/> says whether the outlines come back round underneath — which
    /// they do once the door has a sag.
    /// </summary>
    private static void KeyholeOutlines(ArenaDoorProfile door, float edge,
                                        out List<Vector2> inner, out List<Vector2> outer,
                                        out bool closed)
    {
        float f = door.frameWidth;

        // Counted once off the outer silhouette, so the two outlines come back the same length
        // and station i on one means station i on the other.
        int flare = Mathf.Max(1, Mathf.CeilToInt(
            Vector2.Distance(new Vector2(door.baseWidth * 0.5f, 0f),
                             new Vector2(door.waistWidth * 0.5f, door.waistHeight)) / edge));
        int neck  = Mathf.Max(3, Mathf.CeilToInt(door.bulbRadius * 1.5f / edge));
        int bulb  = Mathf.Max(4, Mathf.CeilToInt(
            door.bulbRadius * (Mathf.PI - BulbAttachDegrees * Mathf.Deg2Rad) / edge));

        // The sag closes the silhouette underneath, so the band runs right round with no cut
        // ends. Off the outer curve, like every other count here, so both outlines agree.
        closed   = door.baseCurve > 0.0001f;
        int  arc = closed
                    ? ArenaArchwayMesh.CrownSegments(door.baseWidth * 0.5f, door.baseCurve, edge)
                    : 0;

        outer = Keyhole(door.baseWidth * 0.5f, door.waistWidth * 0.5f, door.waistHeight,
                        door.bulbRadius, door.height, door.baseCurve,
                        flare, neck, bulb, arc);
        // The shape itself is built at exactly what was authored, but the frame's INNER outline
        // is the outer one drawn in by the frame width, and a frame wider than what it runs round
        // would turn that inside out — a negative radius mirrors the curve and the band comes out
        // crossing itself. Floored here, at the one place it matters, rather than by clamping the
        // number the author typed.
        inner = Keyhole(Mathf.Max(0.001f, door.baseWidth * 0.5f - f),
                        Mathf.Max(0.001f, door.waistWidth * 0.5f - f), door.waistHeight,
                        Mathf.Max(0.001f, door.bulbRadius - f),
                        Mathf.Max(0.002f, door.height - f),
                        Mathf.Max(0.001f, door.baseCurve - f),
                        flare, neck, bulb, arc);
    }

    /// <summary>The rim running round the soul opening, standing proud by the same depth.</summary>
    private static void FlapRim(DoorBuild b, ArenaDoorProfile door, float foot, float along, float edge)
    {
        FlapRimOutlines(door, edge, out var inner, out var outer, out bool closed);
        Band(b, inner, outer, foot, along, door.frameDepth, Part.FlapRim, closed);
    }

    /// <summary>
    /// The soul opening's two outlines — the hole itself, and the rim grown outward from it.
    /// Handed out for the same reason the keyhole's are: the panel's hole is the outer one, so
    /// the panel stops exactly where the rim stands rather than a station either side of it.
    /// </summary>
    private static void FlapRimOutlines(ArenaDoorProfile door, float edge,
                                        out List<Vector2> inner, out List<Vector2> outer,
                                        out bool closed)
    {
        float fHalf = door.flapWidth * 0.5f;
        float r     = door.flapRimWidth;

        int legSteps = door.flapLegHeight <= 0.0001f
                     ? 0 : Mathf.Max(1, Mathf.CeilToInt(door.flapLegHeight / edge));
        int crownSeg = ArenaArchwayMesh.CrownSegments(fHalf + r, door.flapCrownHeight + r, edge);

        // The opening's own sag, closing it underneath the way the door's base curve closes the
        // keyhole. Counted off the outer curve, so both outlines come back the same length.
        closed   = door.flapCurve > 0.0001f;
        int  arc = closed
                    ? ArenaArchwayMesh.CrownSegments(fHalf + r, door.flapCurve + r, edge)
                    : 0;

        // The rim grows outward from the opening rather than shrinking inward into it, so every
        // dimension gains the rim width — including the sag, which keeps the band exactly r wide
        // all the way under the opening rather than pinching at the bottom.
        inner = FlapOutline(fHalf,     door.flapLegHeight, door.flapCrownHeight,
                            door.flapCurve,     legSteps, crownSeg, arc);
        outer = FlapOutline(fHalf + r, door.flapLegHeight, door.flapCrownHeight + r,
                            door.flapCurve + r, legSteps, crownSeg, arc);
    }

    // ─────────────────────────────────────────────────────────────
    // THE PANEL
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The panel filling the keyhole inside the frame — the leaf of the door itself, standing
    /// proud of the sheet, with the soul opening left out of it so the way through stays open.
    ///
    /// Its edge is the frame's inner outline and its hole is the flap rim's outer one, so it
    /// stops exactly against the walls those two bands already stand on. It builds no walls of
    /// its own for that reason: standing shallower than the rims, as it is meant to, it meets
    /// them partway up and closes the recess without a second surface in the same place.
    ///
    /// A depth of 0 builds nothing, and the keyhole is left showing the plain sheet behind the
    /// frame — which is what the door was before it had a panel.
    /// </summary>
    private static void BuildPanel(DoorBuild b, ArenaDoorProfile door, float foot, float along,
                                   float edge)
    {
        if (door.panelDepth <= 0.0001f) return;

        KeyholeOutlines(door, edge, out var keyhole, out _, out _);
        FlapRimOutlines(door, edge, out _, out var flap, out _);

        FillWithHole(b, keyhole, flap, foot, along + door.panelDepth, Part.Panel);
    }

    /// <summary>
    /// Fills a flat outline that has a hole in it, at one z, facing out toward the river.
    ///
    /// The hole is let in by a bridge — a cut run from the hole out to the edge and back, which
    /// turns the two loops into one that can be walked straight through. It is the standard way
    /// round the thing the sheet works round differently: a fan cannot hold a hole, and neither
    /// can an ear clipper handed two separate loops.
    ///
    /// Both outlines are taken as closed, whether or not they come back round underneath: an
    /// open one is closed by the straight run between its two ends, which is the door cut off
    /// flat at the water — exactly what it looks like with no sag.
    /// </summary>
    private static void FillWithHole(DoorBuild b, List<Vector2> outline, List<Vector2> hole,
                                     float foot, float z, Part part)
    {
        if (outline == null || outline.Count < 3) return;

        // The outline is walked anticlockwise and the hole clockwise, so that once the bridge
        // has joined them the hole reads as a bite taken out rather than a second island.
        var loop = Wound(outline, true);
        if (hole != null && hole.Count >= 3)
        {
            loop = Bridge(loop, Wound(hole, false));
            if (loop == null) return;
        }

        b.Part = part;
        var tris = EarClip(loop);
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            Vector2 a = loop[tris[i]], c = loop[tris[i + 1]], d = loop[tris[i + 2]];
            b.Tri(At(a, foot, z), At(c, foot, z), At(d, foot, z), a, c, d, Vector3.forward);
        }
    }

    /// <summary>The outline walked the way round asked for, as a copy.</summary>
    private static List<Vector2> Wound(List<Vector2> loop, bool anticlockwise)
    {
        var copy = new List<Vector2>(loop);
        if (SignedArea(copy) > 0f != anticlockwise) copy.Reverse();
        return copy;
    }

    /// <summary>
    /// Joins a hole into the outline round it, by cutting from the hole's rightmost point out to
    /// the nearest station on the outline it can see. The cut is walked out and back, so the one
    /// loop that comes out has no width there and the fill closes over it.
    ///
    /// Null when the hole cannot be reached at all — it lies outside the outline, or crosses it —
    /// in which case the panel is left unbuilt rather than built inside out.
    /// </summary>
    private static List<Vector2> Bridge(List<Vector2> outline, List<Vector2> hole)
    {
        int from = 0;
        for (int i = 1; i < hole.Count; i++)
            if (hole[i].x > hole[from].x) from = i;

        int   to   = -1;
        float best = float.MaxValue;
        for (int i = 0; i < outline.Count; i++)
        {
            float d = (outline[i] - hole[from]).sqrMagnitude;
            if (d >= best) continue;
            if (Crosses(hole[from], outline[i], outline, i)) continue;
            if (Crosses(hole[from], outline[i], hole, from)) continue;
            best = d;
            to   = i;
        }
        if (to < 0) return null;

        // Out along the outline to the cut, round the hole and back to where the cut started,
        // then back out to the outline and on round it.
        var loop = new List<Vector2>();
        for (int i = 0; i <= to; i++)            loop.Add(outline[i]);
        for (int i = 0; i <= hole.Count; i++)    loop.Add(hole[(from + i) % hole.Count]);
        for (int i = to; i < outline.Count; i++) loop.Add(outline[i]);
        return loop;
    }

    /// <summary>
    /// Whether the cut a-b crosses any edge of a loop, ignoring the two edges that meet it at
    /// the station it is allowed to touch.
    /// </summary>
    private static bool Crosses(Vector2 a, Vector2 b, List<Vector2> loop, int skip)
    {
        for (int i = 0; i < loop.Count; i++)
        {
            int j = (i + 1) % loop.Count;
            if (i == skip || j == skip) continue;
            if (SegmentsCross(a, b, loop[i], loop[j])) return true;
        }
        return false;
    }

    /// <summary>Whether two segments properly cross — touching at an end does not count.</summary>
    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d0 = Cross(b - a, c - a), d1 = Cross(b - a, d - a);
        float d2 = Cross(d - c, a - c), d3 = Cross(d - c, b - c);
        return (d0 > 0f) != (d1 > 0f) && (d2 > 0f) != (d3 > 0f);
    }

    /// <summary>
    /// Ear clipping for the panel — the outline is simple but far from convex, and once a hole
    /// has been bridged into it, it touches itself along the cut.
    ///
    /// The loop must already be walked anticlockwise. A station landing exactly on a corner of
    /// the ear being tested is ignored rather than counted as inside it, because the bridge puts
    /// two stations in the same place on purpose, and a test that counted them would find no ear
    /// there and leave the panel with a wedge missing.
    /// </summary>
    private static List<int> EarClip(List<Vector2> loop)
    {
        var tris = new List<int>();
        int n = loop.Count;
        if (n < 3) return tris;

        var index = new List<int>(n);
        for (int i = 0; i < n; i++) index.Add(i);

        int guard = n * n;
        while (index.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < index.Count; i++)
            {
                int ia = index[(i + index.Count - 1) % index.Count];
                int ib = index[i];
                int ic = index[(i + 1) % index.Count];

                Vector2 a = loop[ia], c = loop[ib], d = loop[ic];
                if (Cross(c - a, d - c) <= 0f) continue;              // reflex corner

                bool clean = true;
                foreach (int other in index)
                {
                    if (other == ia || other == ib || other == ic) continue;
                    Vector2 p = loop[other];
                    if (p == a || p == c || p == d) continue;         // the bridge, doubled back
                    if (Inside(p, a, c, d)) { clean = false; break; }
                }
                if (!clean) continue;

                tris.Add(ia); tris.Add(ib); tris.Add(ic);
                index.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break;                                      // degenerate outline
        }
        if (index.Count == 3) { tris.Add(index[0]); tris.Add(index[1]); tris.Add(index[2]); }

        return tris;
    }

    private static float SignedArea(List<Vector2> loop)
    {
        float area = 0f;
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
            area += a.x * b.y - b.x * a.y;
        }
        return area * 0.5f;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    /// <summary>
    /// Inside the triangle, edges counted as in. A station sitting exactly on an edge of the ear
    /// has to block it: the outlines run in long straight stretches, so stations land on the ear
    /// edges constantly, and letting them through clips ears the polygon folds back over — which
    /// shows up as a panel whose triangles overlap and whose area comes out wrong.
    /// </summary>
    private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
        Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f;

    /// <summary>
    /// The round disc standing on the bulb, which the arena's numeral is drawn on. Built the way
    /// the rims are — a face out toward the river with a wall round it holding it off the sheet —
    /// but fanned from the middle rather than swept between two outlines, because it is solid.
    ///
    /// A radius of 0 builds nothing, and the bulb is left plain.
    /// </summary>
    private static void Disc(DoorBuild b, ArenaDoorProfile door, float foot, float along, float edge)
    {
        if (door.discRadius <= 0.0001f) return;

        float r     = door.discRadius;
        float depth = Mathf.Max(0.001f, door.discDepth);

        // Stood off the panel rather than the sheet, because the panel is the bulb's surface
        // where there is one — otherwise a panel deeper than the disc would bury it.
        float near  = along + Mathf.Max(0f, door.panelDepth), far = near + depth;

        Vector3 c = DiscCentre(door, foot, far);
        int     n = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / edge));

        for (int i = 0; i < n; i++)
        {
            Vector2 a = Round(i, n, r), d = Round(i + 1, n, r);

            // The face you look at. Fanned from the middle, with UV0 running 0 to 1 across the
            // disc both ways, so anything drawn on it lands square.
            b.Part = Part.Disc;
            b.Tri(c, c + new Vector3(a.x, a.y, 0f), c + new Vector3(d.x, d.y, 0f),
                  new Vector2(0.5f, 0.5f), UV(a), UV(d), Vector3.forward);

            // The wall round it, holding the face off the sheet. Broken into rings from the
            // sheet outward when it has been asked for more than one — the same wall, cut into
            // bands rather than taken in a single step.
            float u0 = 2f * Mathf.PI * r * i / n, u1 = 2f * Mathf.PI * r * (i + 1) / n;

            Vector3 outward = new Vector3(a.x + d.x, a.y + d.y, 0f).normalized;
            int     rings   = Mathf.Max(1, door.discSideSteps);

            b.Part = Part.Edges;
            for (int k = 0; k < rings; k++)
            {
                float v0 = depth * k / rings, v1 = depth * (k + 1) / rings;
                float z0 = near + v0,         z1 = near + v1;

                b.Quad(new Vector3(c.x + a.x, c.y + a.y, z0), new Vector3(c.x + a.x, c.y + a.y, z1),
                       new Vector3(c.x + d.x, c.y + d.y, z1), new Vector3(c.x + d.x, c.y + d.y, z0),
                       new Vector2(u0, v0), new Vector2(u0, v1),
                       new Vector2(u1, v1), new Vector2(u1, v0), outward);
            }
        }

        Vector2 Round(int i, int steps, float radius)
        {
            float t = 2f * Mathf.PI * i / steps;
            return new Vector2(Mathf.Sin(t) * radius, Mathf.Cos(t) * radius);
        }

        Vector2 UV(Vector2 p) => new Vector2(0.5f + p.x / (2f * r), 0.5f + p.y / (2f * r));
    }

    /// <summary>
    /// The middle of the bulb, out at whatever z is asked for — where the disc's face is centred,
    /// and where the numeral is centred whether the disc is there or not.
    /// </summary>
    private static Vector3 DiscCentre(ArenaDoorProfile door, float foot, float z) =>
        new Vector3(0f, foot + door.height - door.bulbRadius + door.discRise, z);

    /// <summary>
    /// Where the arena's numeral is drawn, in the archway's own frame — the middle of whatever it
    /// is drawn on, floated off it by the lift, and how wide the drawing is across there.
    ///
    /// With a disc, that is the disc's face, and the drawing spans the disc. With no disc it is
    /// the panel the bulb is made of, in the same place on the bulb, and the drawing spans the
    /// bulb instead — so the number can be cut straight into the door with nothing behind it.
    ///
    /// Handed out so the designer can stand the numeral in front without knowing how the door is
    /// put together. Both profiles must already be resolved, and the other numbers are the ones
    /// <see cref="Build"/> was given. False when the drawing has been sized away to nothing.
    /// </summary>
    public static bool TryNumeralFace(ArenaDoorProfile door, float floor, float waterY, float along,
                                      out Vector3 centre, out float width)
    {
        centre = Vector3.zero;
        width  = 0f;
        if (door == null || door.numeralSize <= 0.0001f) return false;

        bool  disc = door.discRadius > 0.0001f;
        float foot = Mathf.Max(-Mathf.Max(0f, floor), waterY);

        // Off the disc's face where there is one, and off the panel's where there is not — the
        // same surface the disc would have been standing on.
        float face = along + Mathf.Max(0f, door.panelDepth) + door.numeralLift
                   + (disc ? Mathf.Max(0.001f, door.discDepth) : 0f);

        centre    = DiscCentre(door, foot, face);
        centre.y += door.numeralRise;
        width     = (disc ? door.discRadius : door.bulbRadius) * 2f * door.numeralSize;
        return true;
    }

    /// <summary>
    /// Sweeps a band between two outlines, from the sheet out toward the river.
    ///
    /// <paramref name="closed"/> says whether the outlines come back round on themselves. The
    /// soul opening's rim never does — it stands on the water and stops there — and neither does
    /// the frame until the door is given a sag to close it underneath. An open band gets a cap
    /// at each cut end; a closed one wraps instead, and has none.
    ///
    /// The face against the sheet is not built: it is flush with it and would only z-fight.
    /// </summary>
    private static void Band(DoorBuild b, List<Vector2> inner, List<Vector2> outer,
                             float foot, float along, float depth, Part face, bool closed = false)
    {
        if (inner == null || outer == null) return;
        if (inner.Count < 2 || inner.Count != outer.Count) return;

        depth = Mathf.Max(0.001f, depth);

        int   n    = inner.Count;
        float near = along, far = along + depth;

        // Arc length along the band, so a texture keeps world scale round the curve. One entry
        // longer than the outline, so the segment that wraps a closed band has a u to end at.
        var run = new float[n + 1];
        for (int i = 1; i <= n; i++)
            run[i] = run[i - 1] + Vector2.Distance(inner[i - 1], inner[i % n]);

        for (int i = 0; i < (closed ? n : n - 1); i++)
        {
            int j = (i + 1) % n;

            Vector3 i0F = At(inner[i], foot, far),  i1F = At(inner[j], foot, far);
            Vector3 o0F = At(outer[i], foot, far),  o1F = At(outer[j], foot, far);
            Vector3 i0N = At(inner[i], foot, near), i1N = At(inner[j], foot, near);
            Vector3 o0N = At(outer[i], foot, near), o1N = At(outer[j], foot, near);

            float u0 = run[i], u1 = run[i + 1];
            float w  = Vector2.Distance(inner[i], outer[i]);

            // The face you look at, out toward the river.
            b.Part = face;
            b.Quad(i0F, o0F, o1F, i1F,
                   new Vector2(u0, 0f), new Vector2(u0, w),
                   new Vector2(u1, w),  new Vector2(u1, 0f), Vector3.forward);

            // The two walls of the band, each facing away from the other.
            Vector2 across  = ((outer[i] + outer[j]) - (inner[i] + inner[j])) * 0.5f;
            Vector3 outward = new Vector3(across.x, across.y, 0f).normalized;

            b.Part = Part.Edges;
            b.Quad(i0N, i0F, i1F, i1N,
                   new Vector2(u0, 0f), new Vector2(u0, depth),
                   new Vector2(u1, depth), new Vector2(u1, 0f), -outward);
            b.Quad(o0N, o0F, o1F, o1N,
                   new Vector2(u0, 0f), new Vector2(u0, depth),
                   new Vector2(u1, depth), new Vector2(u1, 0f), outward);
        }

        // The two cut ends, where an open band meets the water. A closed one has none.
        if (!closed)
        {
            b.Part = Part.Edges;
            End(0, 1);
            End(n - 1, n - 2);
        }

        void End(int at, int towards)
        {
            Vector2 away2 = (inner[at] - inner[towards]).normalized;
            Vector3 away  = new Vector3(away2.x, away2.y, 0f);
            float   w     = Vector2.Distance(inner[at], outer[at]);

            b.Quad(At(inner[at], foot, near), At(inner[at], foot, far),
                   At(outer[at], foot, far),  At(outer[at], foot, near),
                   new Vector2(0f, 0f), new Vector2(0f, depth),
                   new Vector2(w, depth), new Vector2(w, 0f), away);
        }
    }

    private static Vector3 At(Vector2 p, float foot, float z) => new Vector3(p.x, foot + p.y, z);

    // ─────────────────────────────────────────────────────────────
    // THE OUTLINES
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The keyhole silhouette, walked from the left foot up over the bulb and down to the right
    /// foot, and then — when it has a sag — back underneath to where it started.
    ///
    /// The sag is a half-ellipse hanging between the two base corners, which stay on the water.
    /// It leaves each corner running straight down, so it meets the flare there at the door's
    /// widest point the way the drawing does, rather than rounding the corner away. With no sag
    /// the outline stops at the two corners and the door is cut off flat at the water.
    ///
    /// y is measured up from the door's foot. The right half is built and mirrored, so the two
    /// sides always match; it is the top and bottom that differ, which is the whole point of the
    /// shape — a bulb over a flare, not a bulb over a bulb.
    ///
    /// The station counts are passed in rather than worked out here, so the frame's two outlines
    /// come back the same length and can be paired station for station.
    /// </summary>
    private static List<Vector2> Keyhole(float baseHalf, float waistHalf, float waistHeight,
                                         float bulbRadius, float height, float baseCurve,
                                         int flareSteps, int neckSteps, int bulbSteps, int arcSteps)
    {
        var rh = new List<Vector2>();

        float cy    = height - bulbRadius;
        float alpha = BulbAttachDegrees * Mathf.Deg2Rad;

        // Where the neck meets the bulb, and the way the bulb runs at that point.
        var attach = new Vector2(bulbRadius * Mathf.Sin(alpha), cy - bulbRadius * Mathf.Cos(alpha));
        var onBulb = new Vector2(Mathf.Cos(alpha), Mathf.Sin(alpha));

        var atFoot  = new Vector2(baseHalf,  0f);
        var atWaist = new Vector2(waistHalf, waistHeight);

        // The flare: straight out from the waist down to the foot.
        for (int i = 0; i <= flareSteps; i++)
            rh.Add(Vector2.Lerp(atFoot, atWaist, (float)i / flareSteps));

        // The neck: a curve leaving the waist straight up and arriving on the bulb running the
        // way the bulb runs, so the two read as one line rather than a join.
        float reach = Vector2.Distance(atWaist, attach) * 0.4f;
        var   c1    = atWaist + Vector2.up * reach;
        var   c2    = attach  - onBulb * reach;
        for (int i = 1; i <= neckSteps; i++)
            rh.Add(Bezier(atWaist, c1, c2, attach, (float)i / neckSteps));

        // The bulb: round from the attach point to the apex.
        for (int i = 1; i <= bulbSteps; i++)
        {
            float phi = Mathf.Lerp(alpha, Mathf.PI, (float)i / bulbSteps);
            rh.Add(new Vector2(bulbRadius * Mathf.Sin(phi), cy - bulbRadius * Mathf.Cos(phi)));
        }

        var pts = new List<Vector2>();
        for (int i = 0; i < rh.Count; i++)      pts.Add(new Vector2(-rh[i].x, rh[i].y));
        for (int i = rh.Count - 2; i >= 0; i--) pts.Add(rh[i]);

        // Back under the door, right corner to left, closing the loop. The two corners are
        // already in the list, so only what hangs between them is added.
        for (int i = 1; i < arcSteps; i++)
        {
            float a = Mathf.PI * i / arcSteps;
            pts.Add(new Vector2(Mathf.Cos(a) * baseHalf, -Mathf.Sin(a) * baseCurve));
        }

        return pts;
    }

    private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float s = 1f - t;
        return s * s * s * a + 3f * s * s * t * b + 3f * s * t * t * c + t * t * t * d;
    }

    /// <summary>
    /// The soul opening's outline — its own little arch, legs and crown, walked left foot to
    /// right foot, and then back underneath when it has a sag. y is measured from the door's
    /// foot, so a sag hangs below the water and an opening without one stands flat on it.
    /// </summary>
    private static List<Vector2> FlapOutline(float half, float leg, float crown, float sag,
                                             int legSteps, int crownSeg, int arcSteps)
    {
        var pts = new List<Vector2>();

        for (int i = 0; i <= legSteps; i++)
            pts.Add(new Vector2(-half, leg * i / Mathf.Max(1, legSteps)));

        for (int i = 1; i < crownSeg; i++)
        {
            float a = Mathf.PI * (1f - (float)i / crownSeg);
            pts.Add(new Vector2(Mathf.Cos(a) * half, leg + Mathf.Sin(a) * crown));
        }

        for (int i = legSteps; i >= 0; i--)
            pts.Add(new Vector2(half, leg * i / Mathf.Max(1, legSteps)));

        // Back under the opening, right foot to left, closing the loop. Both feet are already
        // in the list, so only what hangs between them is added.
        for (int i = 1; i < arcSteps; i++)
        {
            float a = Mathf.PI * i / arcSteps;
            pts.Add(new Vector2(Mathf.Cos(a) * half, -Mathf.Sin(a) * sag));
        }

        return pts;
    }

    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every face gets its own vertices, so the door stays hard-edged and faceted like the arch
    /// it stands in.
    ///
    /// Quads are handed the way they should face rather than being wound by hand. The door is
    /// built from outlines walked in whichever order reads best, not from one consistent circuit,
    /// so which winding points outward changes from piece to piece — saying it outright is the
    /// only way to be sure none of them comes out inside out.
    /// </summary>
    private class DoorBuild
    {
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _norms = new List<Vector3>();
        private readonly List<Vector2> _uvs   = new List<Vector2>();
        private readonly List<int>     _tris  = new List<int>();

        public List<Part> Parts;
        public Part       Part;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 facing)
        {
            Tri(a, b, c, ua, ub, uc, facing);
            Tri(a, c, d, ua, uc, ud, facing);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c,
                         Vector2 ua, Vector2 ub, Vector2 uc, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return;      // collapsed, e.g. a zero-height flare
            n.Normalize();

            // Wound the other way when it came in facing the wrong side.
            if (Vector3.Dot(n, facing) < 0f)
            {
                Vector3 sv = b; b = c; c = sv;
                Vector2 su = ub; ub = uc; uc = su;
                n = -n;
            }

            int i0 = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            _norms.Add(n); _norms.Add(n); _norms.Add(n);
            _uvs.Add(ua);  _uvs.Add(ub);  _uvs.Add(uc);
            _tris.Add(i0); _tris.Add(i0 + 1); _tris.Add(i0 + 2);
            Parts?.Add(Part);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_norms);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
