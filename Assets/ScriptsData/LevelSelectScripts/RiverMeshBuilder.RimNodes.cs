using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rim nodes: a round platform standing on a river run's rim — a cylinder, centred on the middle
/// of the rim on one side of the river, its top a set height above the rim and its wall running
/// all the way down to the drop every generated piece ends at.
///
///                 ___
///     rim  ______/   \______        seen from above: the disc bulges out past the run's outer
///          ______|   |______        edge on one side and into the channel on the other
///                 \_/
///     ~~~~~~~~~~~~~~~~~~~~~~~~~
///
/// It is a piece of its own, standing in the run rather than cut into it — the run is built
/// exactly as it always was, and buries whatever of the cylinder lies inside it. So the shape is
/// only ever the cylinder's own, and nothing about the run's tessellation can get in its way.
///
/// The seam shading only draws along edges a piece's own faces share, and the cylinder shares
/// none with the run. So its wall carries a ring of vertices exactly where it goes into the run —
/// down the channel wall and across the rim — and the part below that ring is a part of its own,
/// which puts a seam along the line. Where the wall comes out past the run's outer wall, the
/// same thing happens down the upright line the two meet along.
/// </summary>
public static partial class RiverMeshBuilder
{
    /// <summary>
    /// A rim node on one side of a run: the designer node it stands at, in the run's local space,
    /// which side of the river it is on looking down the run the way it was swept, its radius, and
    /// how far its top stands above the rim.
    /// </summary>
    public struct RimNode
    {
        public Vector3 at;
        public int     side;     // +1 the run's right, -1 its left
        public float   radius;
        public float   height;
    }

    /// <summary>Smallest radius: past half the rim, so it reaches over both of the rim's edges.</summary>
    public static float RimNodeMinRadius(RiverProfile profile)
        => profile == null ? 0f : profile.rimWidth * 0.5f + 0.005f;

    /// <summary>Largest radius: short of the centreline, so the two of a two-sided rim node never
    /// touch and neither reaches the rim across the river.</summary>
    public static float RimNodeMaxRadius(RiverProfile profile)
        => profile == null ? 0f
         : Mathf.Max(RimNodeMinRadius(profile), 0.48f * (profile.innerWidth + profile.rimWidth));

    /// <summary>
    /// Lowest the top may stand above the rim. Level with it, the two surfaces fight over the same
    /// depth and flicker; a little above, and the top also stays clear of the millimetre the seam
    /// shading welds points within.
    /// </summary>
    public const float RimNodeMinHeight = 0.003f;

    // The top is one part with the run; the wall above where it goes into the run another; and
    // everything buried below that line a third — so the line is a seam, however gently the wall
    // meets the rim or the channel there.
    private const int RimTopPart    = 0;
    private const int RimWallPart   = 1;
    private const int RimBuriedPart = 2;

    private struct RimDisc
    {
        public Vector3 centre;
        public float   radius;
        public float   height;
        public int     side;
        public int     index;    // which of the rims handed in it came from
    }

    // ══════════════════════════════════════════════════════════════
    // BUILDING
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Every rim node along one run, as one piece in the run's own local space — placed off the
    /// same centres the run was swept through, so each stands where the run really is.
    /// </summary>
    public static Mesh BuildRimNodes(
        RiverProfile profile, IList<Vector3> centres, IList<Vector3> forwards,
        IList<RimNode> rims, float edge)
    {
        var b = new MeshBuild();
        if (profile == null || centres == null || forwards == null || centres.Count < 2 ||
            rims == null || rims.Count == 0)
            return b.ToMesh("RiverRimNodes");

        edge = Mathf.Max(edge, 0.005f);
        var grid = new RunGrid(profile, centres, forwards, edge);
        if (grid.Rings < 2) return b.ToMesh("RiverRimNodes");

        foreach (var disc in ResolveRimDiscs(profile, rims, grid, false))
            BuildCylinder(b, profile, grid, disc, edge);

        // Measured off the run's own rim line, so the top reads as rim and the walls as what
        // they stand beside.
        b.ShadeSeams(grid.centre, profile.jointGroove);
        return b.ToMesh("RiverRimNodes");
    }

    /// <summary>The middle of a rim node's top, and the flat way from there into the river.</summary>
    public struct RimNodeTop
    {
        public int     index;    // which of the rims handed in it is
        public Vector3 top;
        public Vector3 toRiver;
    }

    /// <summary>
    /// The middle of the top of every rim node <see cref="BuildRimNodes"/> builds from the same
    /// run and rims, in the run's own local space — for what stands on them. Ones left out of
    /// the build are left out here too.
    /// </summary>
    public static List<RimNodeTop> RimNodeTops(
        RiverProfile profile, IList<Vector3> centres, IList<Vector3> forwards,
        IList<RimNode> rims, float edge)
    {
        var tops = new List<RimNodeTop>();
        if (profile == null || centres == null || forwards == null || centres.Count < 2 ||
            rims == null || rims.Count == 0)
            return tops;

        var grid = new RunGrid(profile, centres, forwards, Mathf.Max(edge, 0.005f));
        if (grid.Rings < 2) return tops;

        foreach (var disc in ResolveRimDiscs(profile, rims, grid, true))
        {
            OnCentreline(grid, disc.centre, out Vector3 c, out Vector3 r);
            tops.Add(new RimNodeTop
            {
                index   = disc.index,
                top     = new Vector3(disc.centre.x, c.y + disc.height, disc.centre.z),
                toRiver = -r * disc.side,
            });
        }
        return tops;
    }

    /// <summary>
    /// Places each rim node on the run: on the centreline beside its designer node, then out to
    /// the middle of the rim on its side. One whose circle reaches either end of the run is left
    /// out — it would stand out past the end into nothing.
    /// </summary>
    private static List<RimDisc> ResolveRimDiscs(
        RiverProfile profile, IList<RimNode> rims, RunGrid grid, bool quiet)
    {
        var discs = new List<RimDisc>();
        if (profile == null || rims == null || rims.Count == 0 || grid.Rings < 2) return discs;

        float across = profile.innerWidth * 0.5f + profile.rimWidth * 0.5f;
        float minR   = RimNodeMinRadius(profile);
        float maxR   = RimNodeMaxRadius(profile);

        for (int i = 0; i < rims.Count; i++)
        {
            var rim = rims[i];
            if (rim.side == 0) continue;
            int side = rim.side > 0 ? 1 : -1;

            OnCentreline(grid, rim.at, out Vector3 c, out Vector3 r);
            var disc = new RimDisc
            {
                centre = c + r * (side * across),
                radius = Mathf.Clamp(rim.radius, minR, maxR),
                height = Mathf.Max(rim.height, RimNodeMinHeight),
                side   = side,
                index  = i,
            };

            if (ReachesRunEnd(grid, disc))
            {
                if (!quiet) Debug.LogWarning(
                    $"[RiverMeshBuilder] Rim node at {rim.at} reaches the end of its run, so it " +
                    "was left out. Make it smaller, or move it further from the end.");
                continue;
            }

            discs.Add(disc);
        }
        return discs;
    }

    // Whether the disc reaches across the first or last ring of the run.
    private static bool ReachesRunEnd(RunGrid grid, RimDisc disc)
    {
        float halfOuter = grid.profile.OuterWidth * 0.5f;
        foreach (int j in new[] { 0, grid.Rings - 1 })
        {
            Vector3 a = grid.centre[j] - grid.right[j] * halfOuter;
            Vector3 b = grid.centre[j] + grid.right[j] * halfOuter;
            if (FlatSegmentDistance(disc.centre, a, b) < disc.radius) return true;
        }
        return false;
    }

    /// <summary>
    /// One cylinder. Round its wall, at every angle, three heights: the top, where the wall goes
    /// into the run (the rim top, or the channel's own depth under it), and the drop. Out past the
    /// run's outer wall there is nothing to go into, and the wall runs clear down to the drop.
    /// The angles are an even spread plus exactly where the circle crosses the channel's edge and
    /// the run's outer edge — the first is a corner in the line the wall goes into the run along,
    /// the second is where that line turns and runs straight down the run's outer wall.
    /// </summary>
    private static void BuildCylinder(MeshBuild b, RiverProfile profile, RunGrid grid,
                                      RimDisc disc, float edge)
    {
        float halfInner = profile.innerWidth * 0.5f;
        float halfOuter = profile.OuterWidth * 0.5f;

        var angles = CylinderAngles(grid, disc, edge, halfInner, halfOuter, out var outerBreaks);
        int n      = angles.Count;

        var top    = new Vector3[n];
        var meet   = new Vector3[n];
        var bottom = new Vector3[n];

        for (int k = 0; k < n; k++)
        {
            Vector3 q = OnDisc(disc, disc.radius, angles[k]);
            SideOfRun(grid, q, out float y, out float across);

            float a = Mathf.Min(Mathf.Abs(across), halfOuter);
            top[k]    = new Vector3(q.x, y + disc.height, q.z);
            meet[k]   = new Vector3(q.x, y - ChannelFloor(a, halfInner, profile.riverDepth), q.z);
            bottom[k] = new Vector3(q.x, y - profile.depth, q.z);
        }

        // The wall, stretch by stretch round the circle.
        for (int k = 0; k < n; k++)
        {
            int     k2      = (k + 1) % n;
            Vector3 mid     = OnDisc(disc, disc.radius, MidAngle(angles[k], angles[k2]));
            Vector3 outward = mid - disc.centre;
            outward.y = 0f;

            SideOfRun(grid, mid, out _, out float midAcross);
            float midA = Mathf.Abs(midAcross);

            if (midA < halfOuter)
            {
                // Over the run: the wall above where it goes in, which shows, and the part below,
                // which the run buries. Down into the channel the showing part is the inside of
                // the river; above the rim it is the step up to the top.
                b.NextPart = RimWallPart;
                b.NextKind = midA < halfInner ? FaceInner : 0f;
                FacingQuad(b, meet[k], meet[k2], top[k2], top[k], outward);

                b.NextPart = RimBuriedPart;
                b.NextKind = 0f;
                FacingQuad(b, bottom[k], bottom[k2], meet[k2], meet[k], outward);
            }
            else
            {
                // Out past the run: all of it shows, top to drop. At an end where the circle
                // crosses the run's outer edge, the wall carries the point it went into the run
                // at, so the line it meets the run's outer wall along is an edge of the piece.
                b.NextPart = RimWallPart;
                b.NextKind = 0f;

                var poly = new List<Vector3> { top[k], top[k2] };
                if (outerBreaks.Contains(k2)) poly.Add(meet[k2]);
                poly.Add(bottom[k2]);
                poly.Add(bottom[k]);
                if (outerBreaks.Contains(k)) poly.Add(meet[k]);
                EmitFacingConvex(b, poly, outward);
            }
        }

        // The top, and the bottom it stands on at the drop.
        b.NextKind = 0f;
        b.NextPart = RimTopPart;
        FillDisc(b, grid, disc, angles, top, edge, disc.height, Vector3.up);

        b.NextPart = RimBuriedPart;
        FillDisc(b, grid, disc, angles, bottom, edge, -profile.depth, Vector3.down);

        b.NextPart = 0;
    }

    /// <summary>
    /// The angles round a cylinder: an even spread, plus every angle at which the circle crosses
    /// the channel's edge or the run's outer edge, with any of the spread too close to one of
    /// those dropped. <paramref name="outerBreaks"/> holds the indices of the outer-edge crossings.
    /// </summary>
    private static List<float> CylinderAngles(RunGrid grid, RimDisc disc, float edge,
                                              float halfInner, float halfOuter,
                                              out HashSet<int> outerBreaks)
    {
        const int Probe = 720;

        var acrossAt = new float[Probe];
        for (int k = 0; k < Probe; k++)
        {
            SideOfRun(grid, OnDisc(disc, disc.radius, TwoPi * k / Probe), out _, out float a);
            acrossAt[k] = Mathf.Abs(a);
        }

        var breaks = new List<(float angle, bool outer)>();
        foreach (var (line, outer) in new[] { (halfInner, false), (halfOuter, true) })
        {
            for (int k = 0; k < Probe; k++)
            {
                bool in1 = acrossAt[k] < line, in2 = acrossAt[(k + 1) % Probe] < line;
                if (in1 == in2) continue;

                float lo = TwoPi * k / Probe, hi = lo + TwoPi / Probe;
                for (int it = 0; it < 30; it++)
                {
                    float m = 0.5f * (lo + hi);
                    SideOfRun(grid, OnDisc(disc, disc.radius, m), out _, out float a);
                    if ((Mathf.Abs(a) < line) == in1) lo = m; else hi = m;
                }
                breaks.Add((Mathf.Repeat(0.5f * (lo + hi), TwoPi), outer));
            }
        }

        int   spread = Mathf.Max(32, Mathf.CeilToInt(TwoPi * disc.radius / (edge * 0.5f)));
        float step   = TwoPi / spread;

        var all = new List<(float angle, bool outerBreak)>(breaks);
        for (int u = 0; u < spread; u++)
        {
            float a = u * step;
            if (!breaks.Exists(brk => AngleGap(a, brk.angle) < step * 0.3f)) all.Add((a, false));
        }
        all.Sort((x, y) => x.angle.CompareTo(y.angle));

        var angles = new List<float>(all.Count);
        outerBreaks = new HashSet<int>();
        for (int k = 0; k < all.Count; k++)
        {
            if (all[k].outerBreak) outerBreaks.Add(k);
            angles.Add(all[k].angle);
        }
        return angles;
    }

    /// <summary>
    /// A disc's top or bottom: rings stepping in from the outline, each zipped to the next, down
    /// to eight points round the middle, which are grid filled — so there is no fan meeting at
    /// one point, and no near-flat cell where a grid laid across a many-sided circle bends.
    /// Heights follow the rim, <paramref name="lift"/> above it.
    /// </summary>
    private static void FillDisc(MeshBuild b, RunGrid grid, RimDisc disc, List<float> angles,
                                 Vector3[] outline, float edge, float lift, Vector3 facing)
    {
        var outer = new List<(float angle, Vector3 p)>(angles.Count);
        for (int k = 0; k < angles.Count; k++) outer.Add((angles[k], outline[k]));

        float step  = Mathf.Max(edge, 0.005f);
        float inner = Mathf.Min(disc.radius * 0.5f, 8f * step / TwoPi);
        int   rings = Mathf.Max(1, Mathf.RoundToInt((disc.radius - inner) / step));

        for (int m = 1; m <= rings; m++)
        {
            float r     = disc.radius - (disc.radius - inner) * m / rings;
            int   count = m == rings ? 8 : Mathf.Max(8, Mathf.CeilToInt(TwoPi * r / step));

            var next = new List<(float angle, Vector3 p)>(count);
            for (int k = 0; k < count; k++)
            {
                float   a = TwoPi * k / count;
                Vector3 q = OnDisc(disc, r, a);
                SideOfRun(grid, q, out float y, out _);
                q.y = y + lift;
                next.Add((a, q));
            }

            ZipRims(b, outer, next, facing);
            outer = next;
        }

        var middle = new List<Vector3>(outer.Count);
        foreach (var o in outer) middle.Add(o.p);

        var cells = GridFill(middle);
        if (cells == null) return;

        for (int i = 0; i < cells.Length - 1; i++)
        for (int k = 0; k < cells[0].Length - 1; k++)
            FacingQuad(b, cells[i][k], cells[i + 1][k], cells[i + 1][k + 1], cells[i][k + 1],
                       facing);
    }

    /// <summary>
    /// Stitches two closed rings round the same centre, each in order of angle, with however many
    /// points each has — always stepping on whichever ring's next point comes round first.
    /// </summary>
    private static void ZipRims(MeshBuild b, List<(float angle, Vector3 p)> outer,
                                List<(float angle, Vector3 p)> inner, Vector3 facing)
    {
        int no = outer.Count, ni = inner.Count;
        if (no == 0 || ni == 0) return;

        float At(List<(float angle, Vector3 p)> ring, int k)
            => ring[k % ring.Count].angle + TwoPi * (k / ring.Count);

        int i = 0, j = 0;
        while (i < no || j < ni)
        {
            bool stepOuter = j >= ni || (i < no && At(outer, i + 1) <= At(inner, j + 1));

            Vector3 o0 = outer[i % no].p, n0 = inner[j % ni].p;
            if (stepOuter) { FacingTri(b, o0, outer[(i + 1) % no].p, n0, facing); i++; }
            else           { FacingTri(b, o0, inner[(j + 1) % ni].p, n0, facing); j++; }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // WHERE THINGS ARE
    // ══════════════════════════════════════════════════════════════

    private static Vector3 OnDisc(RimDisc disc, float radius, float angle)
        => new Vector3(disc.centre.x + Mathf.Cos(angle) * radius, 0f,
                       disc.centre.z + Mathf.Sin(angle) * radius);

    /// <summary>
    /// Where a point lies against the run: the rim top's height beside it, and how far across the
    /// run it is from the centreline — positive to the run's right.
    /// </summary>
    private static void SideOfRun(RunGrid grid, Vector3 p, out float rimY, out float across)
    {
        OnCentreline(grid, p, out Vector3 c, out Vector3 r);
        rimY   = c.y;
        across = (p.x - c.x) * r.x + (p.z - c.z) * r.z;
    }

    /// <summary>
    /// The point on the run's centreline nearest <paramref name="at"/>, seen from above, and the
    /// run's flat cross axis there.
    /// </summary>
    private static void OnCentreline(RunGrid grid, Vector3 at, out Vector3 centre, out Vector3 right)
    {
        centre = grid.centre[0];
        right  = grid.right[0];
        float best = float.MaxValue;

        for (int j = 0; j < grid.Rings - 1; j++)
        {
            Vector3 a = grid.centre[j];
            Vector3 d = grid.centre[j + 1] - a;
            float len2 = d.x * d.x + d.z * d.z;
            float t    = len2 < 1e-12f
                       ? 0f
                       : Mathf.Clamp01(((at.x - a.x) * d.x + (at.z - a.z) * d.z) / len2);

            Vector3 p  = a + d * t;
            float   dx = p.x - at.x, dz = p.z - at.z;
            float   dd = dx * dx + dz * dz;
            if (dd >= best) continue;

            best   = dd;
            centre = p;
            right  = Vector3.Lerp(grid.right[j], grid.right[j + 1], t);
        }

        right.y = 0f;
        right   = right.sqrMagnitude < 1e-8f ? grid.right[0] : right.normalized;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float FlatSegmentDistance(Vector3 q, Vector3 a, Vector3 b)
    {
        float dx = b.x - a.x, dz = b.z - a.z;
        float len2 = dx * dx + dz * dz;
        float t = len2 < 1e-12f
                ? 0f
                : Mathf.Clamp01(((q.x - a.x) * dx + (q.z - a.z) * dz) / len2);
        return FlatDistance(q, new Vector3(a.x + dx * t, 0f, a.z + dz * t));
    }

    private static float AngleOf(Vector3 centre, Vector3 p)
        => Mathf.Repeat(Mathf.Atan2(p.z - centre.z, p.x - centre.x), TwoPi);

    private static float AngleGap(float a, float b)
    {
        float d = Mathf.Repeat(a - b, TwoPi);
        return Mathf.Min(d, TwoPi - d);
    }

    /// <summary>
    /// Where a circle crosses the segment a-b, seen from above, as fractions of the way along.
    /// </summary>
    private static int CircleRoots(Vector3 a, Vector3 b, Vector3 centre, float radius,
                                   out float t0, out float t1)
    {
        t0 = t1 = 0f;

        double dx = b.x - a.x, dz = b.z - a.z;
        double fx = a.x - centre.x, fz = a.z - centre.z;
        double A = dx * dx + dz * dz;
        double B = 2.0 * (fx * dx + fz * dz);
        double C = fx * fx + fz * fz - (double)radius * radius;
        if (A < 1e-14) return 0;

        double disc = B * B - 4.0 * A * C;
        if (disc <= 0.0) return 0;

        double sq = System.Math.Sqrt(disc);
        double r0 = (-B - sq) / (2.0 * A);
        double r1 = (-B + sq) / (2.0 * A);

        int count = 0;
        if (r0 > 0.0 && r0 < 1.0) { t0 = (float)r0; count++; }
        if (r1 > 0.0 && r1 < 1.0)
        {
            if (count == 0) t0 = (float)r1; else t1 = (float)r1;
            count++;
        }
        return count;
    }

    // ══════════════════════════════════════════════════════════════
    // FACES
    // ══════════════════════════════════════════════════════════════

    private static void FacingTri(MeshBuild b, Vector3 a, Vector3 c, Vector3 d, Vector3 facing)
    {
        if (Vector3.Dot(Vector3.Cross(c - a, d - c), facing) >= 0f) b.Tri(a, c, d);
        else                                                        b.Tri(d, c, a);
    }

    // A quad cut along whichever diagonal leaves the fatter pair of triangles.
    private static void FacingQuad(MeshBuild b, Vector3 a, Vector3 c, Vector3 d, Vector3 e,
                                   Vector3 facing)
    {
        float Area(Vector3 p, Vector3 q, Vector3 r) => Vector3.Cross(q - p, r - q).magnitude;

        if (Mathf.Min(Area(a, c, d), Area(a, d, e)) >= Mathf.Min(Area(a, c, e), Area(c, d, e)))
        {
            FacingTri(b, a, c, d, facing);
            FacingTri(b, a, d, e, facing);
        }
        else
        {
            FacingTri(b, a, c, e, facing);
            FacingTri(b, c, d, e, facing);
        }
    }

    // A flat convex outline, fanned from whichever corner leaves no triangle flat, every
    // triangle turned to face the given way.
    private static void EmitFacingConvex(MeshBuild b, List<Vector3> poly, Vector3 facing)
    {
        int n = poly.Count;
        if (n < 3) return;

        for (int apex = 0; apex < n; apex++)
        {
            bool clean = true;
            for (int k = 1; k < n - 1 && clean; k++)
            {
                Vector3 e1 = poly[(apex + k) % n]     - poly[apex];
                Vector3 e2 = poly[(apex + k + 1) % n] - poly[apex];
                clean = Vector3.Cross(e1, e2).sqrMagnitude
                      > 1e-6f * e1.sqrMagnitude * e2.sqrMagnitude;
            }
            if (!clean) continue;

            for (int k = 1; k < n - 1; k++)
                FacingTri(b, poly[apex], poly[(apex + k) % n], poly[(apex + k + 1) % n], facing);
            return;
        }

        for (int k = 1; k < n - 1; k++) FacingTri(b, poly[0], poly[k], poly[k + 1], facing);
    }

    // ══════════════════════════════════════════════════════════════
    // BANKS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Where a rim node reaches out over the water, the bank follows it round: the straight bank
    /// is opened between the two places the circle crosses the waterline, and a wall stands round
    /// the circle between them, facing the water. A rim node too small to reach the waterline
    /// leaves the bank as it was.
    /// </summary>
    private static void RimBank(MeshBuild b, RunGrid grid, RimDisc disc, float x, float botY,
                                float topY, float edge, List<Vector2> gaps)
    {
        var   hits  = new List<(float s, Vector3 p)>();
        float reach = disc.radius + grid.profile.OuterWidth + edge;

        for (int j = 0; j < grid.Rings - 1; j++)
        {
            if (FlatDistance(grid.centre[j], disc.centre) >
                reach + Vector3.Distance(grid.centre[j], grid.centre[j + 1])) continue;

            Vector3 a = grid.centre[j]     + grid.right[j]     * x;
            Vector3 c = grid.centre[j + 1] + grid.right[j + 1] * x;

            int count = CircleRoots(a, c, disc.centre, disc.radius, out float t0, out float t1);
            for (int k = 0; k < count; k++)
            {
                float t = k == 0 ? t0 : t1;
                hits.Add((j + t, Vector3.Lerp(a, c, t)));
            }
        }
        if (hits.Count < 2) return;

        hits.Sort((p, q) => p.s.CompareTo(q.s));
        var first = hits[0];
        var last  = hits[hits.Count - 1];
        gaps.Add(new Vector2(first.s, last.s));

        // Of the two ways round between them, the one out over the water — nearer the middle of
        // the river than the disc's own centre is.
        float a0  = AngleOf(disc.centre, first.p);
        float a1  = AngleOf(disc.centre, last.p);
        float ccw = Mathf.Repeat(a1 - a0, TwoPi);

        float Across(float angle)
        {
            SideOfRun(grid, OnDisc(disc, disc.radius, angle), out _, out float a);
            return a * disc.side;
        }

        bool  goCcw = Across(a0 + ccw * 0.5f) < Across(a0 - (TwoPi - ccw) * 0.5f);
        float span  = goCcw ? ccw : -(TwoPi - ccw);
        int   steps = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(span) * disc.radius / (edge * 0.5f)));

        var pts = new List<Vector3> { first.p };
        for (int s = 1; s < steps; s++)
        {
            Vector3 q = OnDisc(disc, disc.radius, a0 + span * s / steps);
            SideOfRun(grid, q, out float y, out _);
            q.y = y;
            pts.Add(q);
        }
        pts.Add(last.p);

        for (int s = 0; s < pts.Count - 1; s++)
        {
            Vector3 outward = (pts[s] + pts[s + 1]) * 0.5f - disc.centre;
            outward.y = 0f;
            Wall(b, pts[s], pts[s + 1], botY, topY, outward);
        }
    }
}
