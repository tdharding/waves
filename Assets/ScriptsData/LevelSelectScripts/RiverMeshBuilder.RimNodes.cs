using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rim nodes: a round platform standing on a river run's rim — centred on the middle of the rim
/// on one side of the river, its top a set height above the rim and its wall running all the way
/// down to the drop every generated piece ends at.
///
///                 ___                         ______________
///     rim  ______/   \______        rim  ____/   __      _   \____
///          ______|   |______             ____|  |  |    (o)   |____
///                 \_/                        \___\__\________/
///     ~~~~~~~~~~~~~~~~~~~~~~~~~      ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
///        seen from above                 with a width, and a plinth
///
/// A width pushes two circles apart ACROSS the rim — one out past the run's outer edge, one in
/// towards the river — and the platform is those two plus the straight sides joining them. At
/// width zero the two sit on each other and it is the plain cylinder it has always been. An
/// optional plinth is a smaller round platform standing on the top, centred on the river-side
/// circle: the spot anything on top stands on.
///
/// It is a piece of its own, standing in the run rather than cut into it — the run is built
/// exactly as it always was, and buries whatever of the platform lies inside it. So the shape is
/// only ever the platform's own, and nothing about the run's tessellation can get in its way.
/// The plinth stands on the platform's top the same way, and the top under it is built whole.
///
/// The seam shading only draws along edges a piece's own faces share, and the platform shares
/// none with the run. So its wall carries a ring of vertices exactly where it goes into the run —
/// down the channel wall and across the rim — and the part below that ring is a part of its own,
/// which puts a seam along the line. Where the wall comes out past the run's outer wall, the
/// same thing happens down the upright line the two meet along.
///
/// Everything round the outline is measured by ONE running number, 0 to 4: the far cap, the side
/// down the run, the near cap, the side back. It reads as an angle used to on a plain circle —
/// with no width the two sides have no length and drop out, leaving exactly the circle it always
/// was — and it still lines up section for section when the width stretches the shape out, which
/// is what lets the rings of the top zip to each other however long the middle gets.
/// </summary>
public static partial class RiverMeshBuilder
{
    /// <summary>
    /// A rim node on one side of a run: the designer node it stands at, in the run's local space,
    /// which side of the river it is on looking down the run the way it was swept, its radius,
    /// how far its top stands above the rim, how far apart its two circles stand across the rim,
    /// and the plinth standing on it.
    /// </summary>
    public struct RimNode
    {
        public Vector3 at;
        public int     side;           // +1 the run's right, -1 its left
        public float   radius;
        public float   height;
        public float   width;          // circle centre to circle centre; 0 is one plain circle
        public float   plinthRadius;   // 0 for no plinth
        public float   plinthHeight;   // how far the plinth's top stands above the platform's
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
    /// As far as the width goes: the whole width of the run, so the river-side circle can be
    /// pushed right out over the water. Nothing holds it back the way the radius is held back —
    /// out there its wall runs down to the drop like the rest of it, and it stands in the river.
    /// </summary>
    public static float RimNodeMaxWidth(RiverProfile profile)
        => profile == null ? 0f : profile.OuterWidth;

    /// <summary>
    /// Lowest the top may stand above the rim. Level with it, the two surfaces fight over the same
    /// depth and flicker; a little above, and the top also stays clear of the millimetre the seam
    /// shading welds points within. The plinth stands on the top by the same least amount.
    /// </summary>
    public const float RimNodeMinHeight = 0.003f;

    /// <summary>Smallest plinth worth building — under this it is taken as none at all.</summary>
    public const float RimNodePlinthMinRadius = 0.001f;

    // The top is one part with the run; the wall above where it goes into the run another;
    // everything buried below that line a third; and the plinth a fourth — so every one of those
    // lines is a seam, however gently the surfaces meet along it.
    private const int RimTopPart    = 0;
    private const int RimWallPart   = 1;
    private const int RimBuriedPart = 2;
    private const int RimPlinthPart = 3;

    /// <summary>
    /// One platform, placed on the run: the middle of the rim it is centred on, the way its width
    /// runs (out across the rim, away from the river), and its sizes. Its outline is every point
    /// <see cref="radius"/> from the straight line between <see cref="Near"/> and
    /// <see cref="Far"/> — a plain circle when the width is zero.
    /// </summary>
    private struct RimShape
    {
        public Vector3 centre;
        public Vector3 axis;       // flat unit, across the rim, away from the river
        public Vector3 along;      // flat unit, down the run
        public float   halfWidth;
        public float   radius;
        public float   height;
        public float   plinthRadius;
        public float   plinthHeight;
        public int     side;
        public int     index;      // which of the rims handed in it came from

        /// <summary>The circle out past the run's outer edge.</summary>
        public Vector3 Far => centre + axis * halfWidth;

        /// <summary>The circle in towards the river — where the plinth and what is on it stand.</summary>
        public Vector3 Near => centre - axis * halfWidth;

        public bool HasPlinth => plinthRadius > RimNodePlinthMinRadius &&
                                 plinthHeight >= RimNodeMinHeight;

        /// <summary>The same shape at another radius, its two circles left where they are.</summary>
        public RimShape At(float r) { var s = this; s.radius = r; return s; }

        /// <summary>The plinth's plain circle, as a shape of its own.</summary>
        public RimShape PlinthShape()
        {
            var s = this;
            s.centre    = Near;
            s.halfWidth = 0f;
            s.radius    = plinthRadius;
            return s;
        }
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

        foreach (var shape in ResolveRimShapes(profile, rims, grid, false))
        {
            BuildPlatform(b, profile, grid, shape, edge);
            BuildPlinth(b, profile, grid, shape, edge);
        }

        // Measured off the run's own rim line, so the top reads as rim and the walls as what
        // they stand beside.
        b.ShadeSeams(grid.centre, profile.jointGroove);
        return b.ToMesh("RiverRimNodes");
    }

    /// <summary>The spot on a rim node anything stands on, and the flat way from there into the river.</summary>
    public struct RimNodeTop
    {
        public int     index;    // which of the rims handed in it is
        public Vector3 top;
        public Vector3 toRiver;
    }

    /// <summary>
    /// The standing spot of every rim node <see cref="BuildRimNodes"/> builds from the same run
    /// and rims, in the run's own local space — the middle of the river-side circle, on top of the
    /// plinth when there is one, which at width zero is the middle of the top as it always was.
    /// Ones left out of the build are left out here too.
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

        foreach (var shape in ResolveRimShapes(profile, rims, grid, true))
        {
            Vector3 at = shape.Near;
            OnCentreline(grid, at, out Vector3 c, out _);
            float lift = shape.height + (shape.HasPlinth ? shape.plinthHeight : 0f);

            tops.Add(new RimNodeTop
            {
                index   = shape.index,
                top     = new Vector3(at.x, c.y + lift, at.z),
                toRiver = -shape.axis,
            });
        }
        return tops;
    }

    /// <summary>
    /// Places each rim node on the run: on the centreline beside its designer node, then out to
    /// the middle of the rim on its side, with its width running on across the rim from there.
    /// One whose shape reaches either end of the run is left out — it would stand out past the
    /// end into nothing.
    /// </summary>
    private static List<RimShape> ResolveRimShapes(
        RiverProfile profile, IList<RimNode> rims, RunGrid grid, bool quiet)
    {
        var shapes = new List<RimShape>();
        if (profile == null || rims == null || rims.Count == 0 || grid.Rings < 2) return shapes;

        float across = profile.innerWidth * 0.5f + profile.rimWidth * 0.5f;
        float minR   = RimNodeMinRadius(profile);
        float maxR   = RimNodeMaxRadius(profile);

        for (int i = 0; i < rims.Count; i++)
        {
            var rim = rims[i];
            if (rim.side == 0) continue;
            int side = rim.side > 0 ? 1 : -1;

            OnCentreline(grid, rim.at, out Vector3 c, out Vector3 r);
            Vector3 axis = r * side;

            float radius = Mathf.Clamp(rim.radius, minR, maxR);
            float plinth = Mathf.Min(Mathf.Max(rim.plinthRadius, 0f), radius);

            var shape = new RimShape
            {
                centre       = c + axis * across,
                axis         = axis,
                along        = Vector3.Cross(Vector3.up, axis),
                halfWidth    = Mathf.Max(rim.width, 0f) * 0.5f,
                radius       = radius,
                height       = Mathf.Max(rim.height, RimNodeMinHeight),
                plinthRadius = plinth <= RimNodePlinthMinRadius ? 0f : plinth,
                plinthHeight = Mathf.Max(rim.plinthHeight, RimNodeMinHeight),
                side         = side,
                index        = i,
            };

            if (ReachesRunEnd(grid, shape))
            {
                if (!quiet) Debug.LogWarning(
                    $"[RiverMeshBuilder] Rim node at {rim.at} reaches the end of its run, so it " +
                    "was left out. Make it smaller or narrower, or move it further from the end.");
                continue;
            }

            shapes.Add(shape);
        }
        return shapes;
    }

    // Whether the shape reaches across the first or last ring of the run.
    private static bool ReachesRunEnd(RunGrid grid, RimShape shape)
    {
        float halfOuter = grid.profile.OuterWidth * 0.5f;
        foreach (int j in new[] { 0, grid.Rings - 1 })
        {
            Vector3 a = grid.centre[j] - grid.right[j] * halfOuter;
            Vector3 b = grid.centre[j] + grid.right[j] * halfOuter;
            if (FlatSegmentsDistance(shape.Near, shape.Far, a, b) < shape.radius) return true;
        }
        return false;
    }

    /// <summary>
    /// One platform. Round its outline, at every point of the walk, three heights: the top, where
    /// the wall goes into the run (the rim top, or the channel's own depth under it), and the
    /// drop. Out past the run's outer wall there is nothing to go into, and the wall runs clear
    /// down to the drop. The walk is an even spread plus exactly where the outline crosses the
    /// channel's edge and the run's outer edge — the first is a corner in the line the wall goes
    /// into the run along, the second is where that line turns and runs straight down the run's
    /// outer wall.
    /// </summary>
    private static void BuildPlatform(MeshBuild b, RiverProfile profile, RunGrid grid,
                                      RimShape shape, float edge)
    {
        float halfInner = profile.innerWidth * 0.5f;
        float halfOuter = profile.OuterWidth * 0.5f;

        var walk = PlatformOutline(grid, shape, edge, halfInner, halfOuter, out var outerBreaks);
        int n    = walk.Count;
        if (n < 3) return;

        var top    = new Vector3[n];
        var meet   = new Vector3[n];
        var bottom = new Vector3[n];

        for (int k = 0; k < n; k++)
        {
            Vector3 q = OnOutline(shape, shape.radius, walk[k]);
            SideOfRun(grid, q, out float y, out float across);

            float a = Mathf.Min(Mathf.Abs(across), halfOuter);
            top[k]    = new Vector3(q.x, y + shape.height, q.z);
            meet[k]   = new Vector3(q.x, y - ChannelFloor(a, halfInner, profile.riverDepth), q.z);
            bottom[k] = new Vector3(q.x, y - profile.depth, q.z);
        }

        // The wall, stretch by stretch round the outline.
        for (int k = 0; k < n; k++)
        {
            int     k2      = (k + 1) % n;
            Vector3 mid     = OnOutline(shape, shape.radius, MidQ(walk[k], walk[k2]));
            Vector3 outward = OutwardAt(shape, mid);

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
                // Out past the run: all of it shows, top to drop. At an end where the outline
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
        FillFlat(b, grid, shape, walk, top, edge, shape.height, Vector3.up);

        b.NextPart = RimBuriedPart;
        FillFlat(b, grid, shape, walk, bottom, edge, -profile.depth, Vector3.down);

        b.NextPart = 0;
    }

    /// <summary>
    /// The plinth: a smaller round platform standing on the platform's top, centred on the
    /// river-side circle. It stands on the top the way the platform stands in the run — the top
    /// underneath is built whole and buries its foot — so it is only ever its own wall and top.
    /// </summary>
    private static void BuildPlinth(MeshBuild b, RiverProfile profile, RunGrid grid,
                                    RimShape shape, float edge)
    {
        if (!shape.HasPlinth) return;

        float halfInner = profile.innerWidth * 0.5f;
        var   plinth    = shape.PlinthShape();
        var   walk      = SampleOutline(plinth, plinth.radius, Mathf.Max(edge, 0.005f) * 0.5f, 16);
        int   n         = walk.Count;
        if (n < 3) return;

        var foot = new Vector3[n];
        var top  = new Vector3[n];

        for (int k = 0; k < n; k++)
        {
            Vector3 q = OnOutline(plinth, plinth.radius, walk[k]);
            SideOfRun(grid, q, out float y, out _);
            foot[k] = new Vector3(q.x, y + shape.height, q.z);
            top[k]  = new Vector3(q.x, y + shape.height + shape.plinthHeight, q.z);
        }

        b.NextPart = RimPlinthPart;
        for (int k = 0; k < n; k++)
        {
            int     k2      = (k + 1) % n;
            Vector3 mid     = OnOutline(plinth, plinth.radius, MidQ(walk[k], walk[k2]));
            Vector3 outward = OutwardAt(plinth, mid);

            SideOfRun(grid, mid, out _, out float midAcross);
            b.NextKind = Mathf.Abs(midAcross) < halfInner ? FaceInner : 0f;
            FacingQuad(b, foot[k], foot[k2], top[k2], top[k], outward);
        }

        b.NextKind = 0f;
        FillFlat(b, grid, plinth, walk, top, edge, shape.height + shape.plinthHeight, Vector3.up);
        b.NextPart = 0;
    }

    /// <summary>
    /// The walk round a platform's outline: an even spread, plus every place the outline crosses
    /// the channel's edge or the run's outer edge, with any of the spread too close to one of
    /// those dropped. <paramref name="outerBreaks"/> holds the indices of the outer-edge crossings.
    /// </summary>
    private static List<float> PlatformOutline(RunGrid grid, RimShape shape, float edge,
                                               float halfInner, float halfOuter,
                                               out HashSet<int> outerBreaks)
    {
        const int Probe = 720;

        var probe    = SampleEven(shape, shape.radius, Probe);
        var acrossAt = new float[probe.Count];
        for (int k = 0; k < probe.Count; k++)
        {
            SideOfRun(grid, OnOutline(shape, shape.radius, probe[k]), out _, out float a);
            acrossAt[k] = Mathf.Abs(a);
        }

        var breaks = new List<(float q, bool outer)>();
        foreach (var (line, outer) in new[] { (halfInner, false), (halfOuter, true) })
        {
            for (int k = 0; k < probe.Count; k++)
            {
                int  k2  = (k + 1) % probe.Count;
                bool in1 = acrossAt[k] < line, in2 = acrossAt[k2] < line;
                if (in1 == in2) continue;

                float lo = probe[k];
                float hi = k2 == 0 ? probe[0] + 4f : probe[k2];
                for (int it = 0; it < 30; it++)
                {
                    float m = 0.5f * (lo + hi);
                    SideOfRun(grid, OnOutline(shape, shape.radius, m), out _, out float a);
                    if ((Mathf.Abs(a) < line) == in1) lo = m; else hi = m;
                }
                breaks.Add((Mathf.Repeat(0.5f * (lo + hi), 4f), outer));
            }
        }

        var   spread  = SampleOutline(shape, shape.radius, Mathf.Max(edge, 0.005f) * 0.5f, 16);
        float spacing = Perimeter(shape, shape.radius) / Mathf.Max(1, spread.Count);

        var all = new List<(float q, bool outerBreak)>();
        foreach (var brk in breaks) all.Add((brk.q, brk.outer));
        foreach (float q in spread)
            if (!breaks.Exists(brk => ArcGap(shape, shape.radius, q, brk.q) < spacing * 0.3f))
                all.Add((q, false));
        all.Sort((x, y) => x.q.CompareTo(y.q));

        var walk = new List<float>(all.Count);
        outerBreaks = new HashSet<int>();
        for (int k = 0; k < all.Count; k++)
        {
            if (all[k].outerBreak) outerBreaks.Add(k);
            walk.Add(all[k].q);
        }
        return walk;
    }

    /// <summary>
    /// A platform's top or bottom: rings stepping in from the outline, each zipped to the next,
    /// down to a small ring in the middle, which is grid filled — so there is no fan meeting at
    /// one point, and no near-flat cell where a grid laid across a many-sided outline bends. Each
    /// ring is the same shape at a smaller radius, so a widened platform closes down to a short
    /// bar in the middle rather than to a point. Heights follow the rim, <paramref name="lift"/>
    /// above it.
    /// </summary>
    private static void FillFlat(MeshBuild b, RunGrid grid, RimShape shape, List<float> walk,
                                 Vector3[] outline, float edge, float lift, Vector3 facing)
    {
        var outer = new List<(float q, Vector3 p)>(walk.Count);
        for (int k = 0; k < walk.Count; k++) outer.Add((walk[k], outline[k]));

        float step  = Mathf.Max(edge, 0.005f);
        float inner = Mathf.Min(shape.radius * 0.5f, 8f * step / TwoPi);
        int   rings = Mathf.Max(1, Mathf.RoundToInt((shape.radius - inner) / step));

        for (int m = 1; m <= rings; m++)
        {
            float r    = shape.radius - (shape.radius - inner) * m / rings;
            var   ring = shape.At(r);

            List<float> qs;
            if (m == rings)
            {
                // The middle, which is grid filled: an even count of points, and at least eight.
                int count = Mathf.Max(8, Mathf.CeilToInt(Perimeter(ring, r) / step));
                if ((count & 1) != 0) count++;
                qs = SampleEven(ring, r, count);
            }
            else
            {
                qs = SampleOutline(ring, r, step, 4);
            }

            var next = new List<(float q, Vector3 p)>(qs.Count);
            foreach (float q in qs)
            {
                Vector3 p = OnOutline(ring, r, q);
                SideOfRun(grid, p, out float y, out _);
                p.y = y + lift;
                next.Add((q, p));
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
    /// Stitches two closed rings round the same shape, each in order of the walk round it, with
    /// however many points each has — always stepping on whichever ring's next point comes round
    /// first.
    /// </summary>
    private static void ZipRims(MeshBuild b, List<(float q, Vector3 p)> outer,
                                List<(float q, Vector3 p)> inner, Vector3 facing)
    {
        int no = outer.Count, ni = inner.Count;
        if (no == 0 || ni == 0) return;

        float At(List<(float q, Vector3 p)> ring, int k)
            => ring[k % ring.Count].q + 4f * (k / ring.Count);

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
    // ROUND THE OUTLINE
    // ══════════════════════════════════════════════════════════════
    //
    //   0 to 1   the far cap, from one side of the run round to the other
    //   1 to 2   the straight side, far circle to near circle
    //   2 to 3   the near cap, back again
    //   3 to 4   the straight side home

    private static float SectionLength(RimShape shape, float radius, int section)
        => (section & 1) == 0 ? Mathf.PI * radius : 2f * shape.halfWidth;

    private static float Perimeter(RimShape shape, float radius)
        => 2f * (Mathf.PI * radius + 2f * shape.halfWidth);

    /// <summary>Where on the outline a point of the walk lands, flat — y is left at zero.</summary>
    private static Vector3 OnOutline(RimShape shape, float radius, float q)
    {
        q = Mathf.Repeat(q, 4f);

        Vector3 p;
        if (q < 1f)
        {
            float t = -Mathf.PI * 0.5f + q * Mathf.PI;
            p = shape.Far + (shape.axis * Mathf.Cos(t) + shape.along * Mathf.Sin(t)) * radius;
        }
        else if (q < 2f)
        {
            p = Vector3.Lerp(shape.Far, shape.Near, q - 1f) + shape.along * radius;
        }
        else if (q < 3f)
        {
            float t = Mathf.PI * 0.5f + (q - 2f) * Mathf.PI;
            p = shape.Near + (shape.axis * Mathf.Cos(t) + shape.along * Mathf.Sin(t)) * radius;
        }
        else
        {
            p = Vector3.Lerp(shape.Near, shape.Far, q - 3f) - shape.along * radius;
        }

        p.y = 0f;
        return p;
    }

    /// <summary>Which point of the walk a point on the outline is at.</summary>
    private static float QOf(RimShape shape, Vector3 p)
    {
        Vector3 d = p - shape.centre;
        float   u = d.x * shape.axis.x  + d.z * shape.axis.z;
        float   v = d.x * shape.along.x + d.z * shape.along.z;

        if (shape.halfWidth > 1e-6f && Mathf.Abs(u) <= shape.halfWidth)
        {
            float along = (shape.halfWidth - u) / (2f * shape.halfWidth);
            return v >= 0f ? 1f + along : Mathf.Repeat(4f - along, 4f);
        }

        if (u >= 0f)
        {
            float t = Mathf.Atan2(v, u - shape.halfWidth);          // -PI/2 .. PI/2
            return Mathf.Repeat((t + Mathf.PI * 0.5f) / Mathf.PI, 4f);
        }

        float s = Mathf.Atan2(v, u + shape.halfWidth);              // PI/2 .. 3PI/2
        if (s < 0f) s += TwoPi;
        return Mathf.Repeat(2f + (s - Mathf.PI * 0.5f) / Mathf.PI, 4f);
    }

    /// <summary>The flat way out of the shape at a point on or near its outline.</summary>
    private static Vector3 OutwardAt(RimShape shape, Vector3 p)
    {
        Vector3 d = p - shape.centre;
        float   u = Mathf.Clamp(d.x * shape.axis.x + d.z * shape.axis.z,
                                -shape.halfWidth, shape.halfWidth);

        Vector3 away = p - (shape.centre + shape.axis * u);
        away.y = 0f;
        return away;
    }

    /// <summary>How far round the outline a point of the walk is, from the start of it.</summary>
    private static float ArcAt(RimShape shape, float radius, float q)
    {
        q = Mathf.Repeat(q, 4f);
        int   whole = Mathf.Min(3, Mathf.FloorToInt(q));
        float arc   = 0f;
        for (int i = 0; i < whole; i++) arc += SectionLength(shape, radius, i);
        return arc + SectionLength(shape, radius, whole) * (q - whole);
    }

    /// <summary>The point of the walk a given way round the outline.</summary>
    private static float QAtArc(RimShape shape, float radius, float arc)
    {
        float perimeter = Perimeter(shape, radius);
        if (perimeter <= 1e-6f) return 0f;

        arc = Mathf.Repeat(arc, perimeter);
        for (int i = 0; i < 4; i++)
        {
            float len = SectionLength(shape, radius, i);
            if (len > 1e-9f && arc <= len) return i + arc / len;
            arc -= len;
        }
        return 0f;
    }

    /// <summary>The shorter of the two ways round between two points of the walk, as a length.</summary>
    private static float ArcGap(RimShape shape, float radius, float q1, float q2)
    {
        float perimeter = Perimeter(shape, radius);
        if (perimeter <= 1e-6f) return 0f;

        float d = Mathf.Repeat(ArcAt(shape, radius, q1) - ArcAt(shape, radius, q2), perimeter);
        return Mathf.Min(d, perimeter - d);
    }

    /// <summary>How long the outline is between one point of the walk and another, the way given.</summary>
    private static float ArcAlong(RimShape shape, float radius, float from, float span)
    {
        const int Steps = 32;

        float   len  = 0f;
        Vector3 prev = OnOutline(shape, radius, from);
        for (int k = 1; k <= Steps; k++)
        {
            Vector3 p = OnOutline(shape, radius, from + span * k / Steps);
            len += FlatDistance(prev, p);
            prev = p;
        }
        return len;
    }

    /// <summary>
    /// A walk round the outline, section by section, no further apart than
    /// <paramref name="step"/> — and never fewer than <paramref name="minPerCap"/> steps round
    /// each cap, so a small circle is still round. Each section holds its own start and stops
    /// short of the next one's, so nothing is ever landed on twice.
    /// </summary>
    private static List<float> SampleOutline(RimShape shape, float radius, float step,
                                             int minPerCap)
    {
        var qs = new List<float>();
        step = Mathf.Max(step, 1e-4f);

        for (int i = 0; i < 4; i++)
        {
            float len = SectionLength(shape, radius, i);

            // A straight side with no length in it is left out altogether: with no width the two
            // circles are one circle, and both ends of the side are already on the caps.
            if ((i & 1) != 0 && len <= 1e-9f) continue;

            int count = Mathf.Max((i & 1) == 0 ? minPerCap : 1, Mathf.CeilToInt(len / step));
            for (int k = 0; k < count; k++) qs.Add(i + (float)k / count);
        }
        return qs;
    }

    /// <summary>A walk round the outline in evenly spaced steps, however long its sides are.</summary>
    private static List<float> SampleEven(RimShape shape, float radius, int count)
    {
        var   qs        = new List<float>(count);
        float perimeter = Perimeter(shape, radius);
        for (int k = 0; k < count; k++) qs.Add(QAtArc(shape, radius, perimeter * k / count));
        return qs;
    }

    // ══════════════════════════════════════════════════════════════
    // WHERE THINGS ARE
    // ══════════════════════════════════════════════════════════════

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

    /// <summary>How far apart two flat lines lie — nothing at all when they cross.</summary>
    private static float FlatSegmentsDistance(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
    {
        float ax = a2.x - a1.x, az = a2.z - a1.z;
        float bx = b2.x - b1.x, bz = b2.z - b1.z;
        float denom = ax * bz - az * bx;

        if (Mathf.Abs(denom) > 1e-12f)
        {
            float cx = b1.x - a1.x, cz = b1.z - a1.z;
            float t  = (cx * bz - cz * bx) / denom;
            float u  = (cx * az - cz * ax) / denom;
            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
        }

        return Mathf.Min(
            Mathf.Min(FlatSegmentDistance(a1, b1, b2), FlatSegmentDistance(a2, b1, b2)),
            Mathf.Min(FlatSegmentDistance(b1, a1, a2), FlatSegmentDistance(b2, a1, a2)));
    }

    /// <summary>The point of the walk halfway between two, the short way round.</summary>
    private static float MidQ(float q1, float q2)
    {
        float d = Mathf.Repeat(q2 - q1, 4f);
        return Mathf.Repeat(q1 + d * 0.5f, 4f);
    }

    /// <summary>
    /// Where a platform's outline crosses the segment a-b, seen from above, as fractions of the
    /// way along. The outline is everything <paramref name="radius"/> from the straight line
    /// between the two circles, so the crossings are the segment's against each circle and
    /// against each straight side — and a straight line crosses a shape that never bends back on
    /// itself at most twice.
    /// </summary>
    private static int OutlineRoots(Vector3 a, Vector3 b, RimShape shape, float radius,
                                    out float t0, out float t1)
    {
        t0 = t1 = 0f;
        var hits = new List<float>(2);

        void Keep(float t)
        {
            if (t <= 0f || t >= 1f) return;
            foreach (float had in hits) if (Mathf.Abs(had - t) < 1e-6f) return;
            hits.Add(t);
        }

        // Against each circle, kept only where it is really on the outline rather than inside the
        // straight middle: out past the far circle's own end of the line, or past the near one's.
        foreach (var (centre, beyond) in new[] { (shape.Far, 1f), (shape.Near, -1f) })
        {
            int count = CircleRoots(a, b, centre, radius, out float c0, out float c1);
            for (int k = 0; k < count; k++)
            {
                float   t = k == 0 ? c0 : c1;
                Vector3 p = Vector3.Lerp(a, b, t);
                Vector3 d = p - shape.centre;
                float   u = d.x * shape.axis.x + d.z * shape.axis.z;
                if (u * beyond >= shape.halfWidth - 1e-6f) Keep(t);
            }
        }

        // Against each straight side, kept only between the two circles.
        if (shape.halfWidth > 1e-6f)
        {
            float va = (a.x - shape.centre.x) * shape.along.x + (a.z - shape.centre.z) * shape.along.z;
            float vb = (b.x - shape.centre.x) * shape.along.x + (b.z - shape.centre.z) * shape.along.z;
            float ua = (a.x - shape.centre.x) * shape.axis.x  + (a.z - shape.centre.z) * shape.axis.z;
            float ub = (b.x - shape.centre.x) * shape.axis.x  + (b.z - shape.centre.z) * shape.axis.z;

            float dv = vb - va;
            if (Mathf.Abs(dv) > 1e-12f)
            {
                foreach (float line in new[] { radius, -radius })
                {
                    float t = (line - va) / dv;
                    if (t <= 0f || t >= 1f) continue;
                    if (Mathf.Abs(Mathf.Lerp(ua, ub, t)) <= shape.halfWidth + 1e-6f) Keep(t);
                }
            }
        }

        hits.Sort();
        if (hits.Count > 0) t0 = hits[0];
        if (hits.Count > 1) t1 = hits[1];
        return Mathf.Min(hits.Count, 2);
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
    /// is opened between the two places the outline crosses the waterline, and a wall stands round
    /// the platform between them, facing the water. A rim node too small to reach the waterline
    /// leaves the bank as it was.
    /// </summary>
    private static void RimBank(MeshBuild b, RunGrid grid, RimShape shape, float x, float botY,
                                float topY, float edge, List<Vector2> gaps)
    {
        var   hits  = new List<(float s, Vector3 p)>();
        float reach = shape.radius + shape.halfWidth + grid.profile.OuterWidth + edge;

        for (int j = 0; j < grid.Rings - 1; j++)
        {
            if (FlatDistance(grid.centre[j], shape.centre) >
                reach + Vector3.Distance(grid.centre[j], grid.centre[j + 1])) continue;

            Vector3 a = grid.centre[j]     + grid.right[j]     * x;
            Vector3 c = grid.centre[j + 1] + grid.right[j + 1] * x;

            int count = OutlineRoots(a, c, shape, shape.radius, out float t0, out float t1);
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
        // the river than the platform's own middle is.
        float q0  = QOf(shape, first.p);
        float q1  = QOf(shape, last.p);
        float ccw = Mathf.Repeat(q1 - q0, 4f);

        float Across(float q)
        {
            SideOfRun(grid, OnOutline(shape, shape.radius, q), out _, out float a);
            return a * shape.side;
        }

        bool  goCcw = Across(q0 + ccw * 0.5f) < Across(q0 - (4f - ccw) * 0.5f);
        float span  = goCcw ? ccw : -(4f - ccw);
        int   steps = Mathf.Max(4, Mathf.CeilToInt(
                          ArcAlong(shape, shape.radius, q0, span) / (edge * 0.5f)));

        var pts = new List<Vector3> { first.p };
        for (int s = 1; s < steps; s++)
        {
            Vector3 q = OnOutline(shape, shape.radius, q0 + span * s / steps);
            SideOfRun(grid, q, out float y, out _);
            q.y = y;
            pts.Add(q);
        }
        pts.Add(last.p);

        for (int s = 0; s < pts.Count - 1; s++)
            Wall(b, pts[s], pts[s + 1], botY, topY,
                 OutwardAt(shape, (pts[s] + pts[s + 1]) * 0.5f));
    }
}
