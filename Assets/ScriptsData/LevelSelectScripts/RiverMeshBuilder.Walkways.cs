using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// River walkways: an extra rim laid along the outside of a river run, between the run's outer
/// wall and whatever stands beyond it — an outpost on that bank stands out past the walkway.
///
///            outpost                      walkway            run
///          |‾‾‾‾‾‾‾‾|  ___________________________  ____________________
///          |        | |___________________________||____________________|
///          |        | |                           ||
///
/// It runs from one designer node to the next, its inner edge on the run's outer edge and its
/// outer edge a set width further out, following the rim's curve ring for ring. Where an outpost
/// stands beside it, its outer edge goes straight along the outpost's front face instead. Its
/// top is a set height above the rim (or below it), riding the rim as the river climbs, and its
/// walls run down to the same drop as the run.
///
/// A piece of its own beside the run, never cut into it. Each stretch is a closed box; where the
/// outer edge changes between following the rim and going straight along an outpost, or where
/// the settings change from one stretch to the next, two boxes butt against each other on the
/// same cross line — the seam shading draws the joint across the top there.
/// </summary>
public static partial class RiverMeshBuilder
{
    /// <summary>
    /// One stretch of walkway on one side of a run: the designer nodes it runs between, in the
    /// run's own local space, which side it is on, how far it reaches out from the run's outer
    /// edge, and how far its top stands above the rim (below it when negative).
    /// </summary>
    public struct Walkway
    {
        public Vector3 from;
        public Vector3 to;
        public int     side;     // +1 the run's right, -1 its left
        public float   width;
        public float   height;

        /// <summary>
        /// River Walkway 2 when 2 — built out past the first walkway rather than against the run.
        /// 0 or 1 is the first walkway.
        /// </summary>
        public int     layer;

        /// <summary>How far out from the run's outer edge its inner edge stands — the first walkway's width beside it.</summary>
        public float   inset;
    }

    /// <summary>
    /// Where the walkway goes straight: an outpost's front face, in the run's own local space —
    /// the middle of the face, the flat way along it, and half its width.
    /// </summary>
    public struct WalkwayFlat
    {
        public Vector3 face;
        public Vector3 along;
        public float   halfWidth;
        public int     side;     // +1 the run's right, -1 its left

        /// <summary>
        /// How wide the River Walkway 2 standing in front of this face is — the first walkway
        /// goes straight along a line this far in from the face, and walkway 2 fills the rest.
        /// 0 where there is none.
        /// </summary>
        public float   beyond;
    }

    /// <summary>
    /// Under this, a walkway's top is taken as flush with the rim and it has no step up to it —
    /// a step any thinner would be welded flat by the seam shading.
    /// </summary>
    public const float WalkwayMinStep = 0.002f;

    /// <summary>Narrowest a walkway is built.</summary>
    public const float WalkwayMinWidth = 0.01f;

    // The top is one part; the walls that show (outside, step up from the rim, ends) another; and
    // what stands against the run's outer wall and the underside a third — so the line the step
    // meets the rim along is a seam.
    private const int WalkTopPart    = 0;
    private const int WalkWallPart   = 1;
    private const int WalkBuriedPart = 2;

    // A cross line of the walkway: a point on the run's centreline and the run's cross axis
    // there, both lerped between the rings either side so the inner edge lies exactly on the
    // run's own outer edge.
    private struct WalkStation
    {
        public float   s;
        public Vector3 c;
        public Vector3 r;
    }

    /// <summary>
    /// Every walkway along one run, as one piece in the run's own local space — placed off the
    /// same centres the run was swept through. <paramref name="shading"/> picks the stone colour
    /// each part takes; null leaves them all to be worked out.
    /// </summary>
    public static Mesh BuildWalkways(
        RiverProfile profile, IList<Vector3> centres, IList<Vector3> forwards,
        IList<Walkway> walkways, IList<WalkwayFlat> flats, float edge,
        RiverRunShadingSettings shading = null, IList<Bridge> bridges = null)
    {
        var b = new MeshBuild();
        bool any = (walkways != null && walkways.Count > 0) || (bridges != null && bridges.Count > 0);
        if (profile == null || centres == null || forwards == null || centres.Count < 2 || !any)
            return b.ToMesh("RiverWalkways");

        var grid = new RunGrid(profile, centres, forwards, Mathf.Max(edge, 0.005f));
        if (grid.Rings < 2) return b.ToMesh("RiverWalkways");

        if (walkways != null)
            foreach (var walkway in walkways)
                BuildWalkway(b, grid, walkway, flats, shading);

        if (bridges != null)
            foreach (var bridge in bridges)
                BuildBridge(b, grid, bridge, shading);

        b.NextKind = 0f;
        b.NextPart = 0;

        // Measured off the run's own rim line, as the run and its rim nodes are.
        b.ShadeSeams(grid.centre, profile.jointGroove);
        return b.ToMesh("RiverWalkways");
    }

    private static void BuildWalkway(MeshBuild b, RunGrid grid, Walkway walkway,
                                     IList<WalkwayFlat> flats, RiverRunShadingSettings shading)
    {
        int side = walkway.side >= 0 ? 1 : -1;

        float s0 = ArcOnCentreline(grid, walkway.from);
        float s1 = ArcOnCentreline(grid, walkway.to);
        if (s0 > s1) (s0, s1) = (s1, s0);
        if (s1 - s0 < 1e-4f) return;

        float halfOuter = grid.profile.OuterWidth * 0.5f;
        float width     = Mathf.Max(walkway.width, WalkwayMinWidth);
        float height    = Mathf.Max(walkway.height, -(grid.profile.depth - 0.01f));
        bool  second    = walkway.layer == 2;
        float inset     = second ? Mathf.Max(0f, walkway.inset) : 0f;

        // The outposts on this side, and the places along the stretch where one starts or ends.
        var mine = new List<WalkwayFlat>();
        if (flats != null)
            foreach (var flat in flats)
                if ((flat.side >= 0 ? 1 : -1) == side && flat.halfWidth > 0f) mine.Add(flat);

        var cuts = new List<float> { s0, s1 };
        foreach (var flat in mine)
        {
            FlatEnds(grid, side, flat, s0, s1, -flat.halfWidth, cuts);
            FlatEnds(grid, side, flat,  s0, s1,  flat.halfWidth, cuts);
        }
        cuts.Sort();
        for (int i = cuts.Count - 1; i > 0; i--)
            if (cuts[i] - cuts[i - 1] < 1e-4f) cuts.RemoveAt(i);

        // A box between each pair of cuts, carrying every ring of the run in between.
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            float a = cuts[i], z = cuts[i + 1];

            var stations = new List<WalkStation> { StationAt(grid, a) };
            for (int j = 0; j < grid.Rings; j++)
                if (grid.arc[j] > a + 1e-4f && grid.arc[j] < z - 1e-4f)
                    stations.Add(StationAt(grid, grid.arc[j]));
            stations.Add(StationAt(grid, z));

            // Straight along an outpost when the middle of the box is in front of one.
            int flatIndex = FlatAt(StationAt(grid, (a + z) * 0.5f), side, mine);

            // Beside an outpost the first walkway goes straight along a line in from its face by
            // the width of any walkway 2 there, and walkway 2 runs from that line to the face.
            var inner = new float[stations.Count];
            var outer = new float[stations.Count];
            for (int k = 0; k < stations.Count; k++)
            {
                inner[k] = halfOuter + inset;
                outer[k] = inner[k] + width;
                if (flatIndex < 0) continue;

                var flat = mine[flatIndex];
                if (second)
                {
                    if (FlatCrossing(stations[k], side, flat, out float tIn, out _, flat.beyond))
                        inner[k] = Mathf.Max(tIn, halfOuter);
                    if (FlatCrossing(stations[k], side, flat, out float tOut, out _))
                        outer[k] = tOut;
                }
                else if (FlatCrossing(stations[k], side, flat, out float t, out _, flat.beyond))
                {
                    outer[k] = t;
                }
                outer[k] = Mathf.Max(outer[k], inner[k] + WalkwayMinWidth);
            }

            BuildWalkwayBox(b, stations, inner, outer, side, height, grid.profile.depth, shading);
        }
    }

    /// <summary>
    /// One closed box: the top, the outer wall, the step up from the rim when the top stands
    /// above it, the face against the run's outer wall, the underside and the two ends.
    /// </summary>
    private static void BuildWalkwayBox(MeshBuild b, List<WalkStation> st, float[] innerT,
                                        float[] outerT, int side, float height, float depth,
                                        RiverRunShadingSettings shading)
    {
        int n = st.Count;
        if (n < 2) return;

        bool step = height > WalkwayMinStep;
        bool flush = Mathf.Abs(height) <= WalkwayMinStep;
        float lift = flush ? 0f : height;

        float Kind(System.Func<RiverRunShadingSettings, StoneFaceKind> pick) =>
            shading != null ? (float)pick(shading) : 0f;

        var iT = new Vector3[n]; var iR = new Vector3[n]; var iB = new Vector3[n];
        var oT = new Vector3[n]; var oB = new Vector3[n];
        var outward = new Vector3[n];

        for (int k = 0; k < n; k++)
        {
            Vector3 d    = st[k].r * side;
            float   rimY = st[k].c.y;
            Vector3 inner = st[k].c + d * innerT[k];
            Vector3 outer = st[k].c + d * outerT[k];

            iT[k] = new Vector3(inner.x, rimY + lift,  inner.z);
            iR[k] = new Vector3(inner.x, rimY,         inner.z);
            iB[k] = new Vector3(inner.x, rimY - depth, inner.z);
            oT[k] = new Vector3(outer.x, rimY + lift,  outer.z);
            oB[k] = new Vector3(outer.x, rimY - depth, outer.z);

            d.y = 0f;
            outward[k] = d.sqrMagnitude < 1e-8f ? Vector3.right * side : d.normalized;
        }

        for (int k = 0; k + 1 < n; k++)
        {
            int     k2  = k + 1;
            Vector3 out3 = (outward[k] + outward[k2]).normalized;

            b.NextPart = WalkTopPart;
            b.NextKind = Kind(s => s.walkwayTop);
            FacingQuad(b, iT[k], oT[k], oT[k2], iT[k2], Vector3.up);

            b.NextPart = WalkWallPart;
            b.NextKind = Kind(s => s.walkwayOuterWall);
            FacingQuad(b, oB[k], oB[k2], oT[k2], oT[k], out3);

            if (step)
            {
                b.NextKind = Kind(s => s.walkwayStep);
                FacingQuad(b, iR[k], iR[k2], iT[k2], iT[k], -out3);
            }

            b.NextPart = WalkBuriedPart;
            b.NextKind = 0f;
            FacingQuad(b, iB[k], iB[k2], step ? iR[k2] : iT[k2], step ? iR[k] : iT[k], -out3);
            FacingQuad(b, iB[k], iB[k2], oB[k2], oB[k], Vector3.down);
        }

        // The two ends, square across the run. The step's foot is carried as a corner so the end
        // shares its edges with both the step and the face below it.
        b.NextPart = WalkWallPart;
        b.NextKind = Kind(s => s.walkwayEnds);
        foreach (int k in new[] { 0, n - 1 })
        {
            // Down the run, whichever side this is on — outward times side is the run's right.
            Vector3 fwd = Vector3.Cross(outward[k] * side, Vector3.up);
            if (k == 0) fwd = -fwd;

            var poly = new List<Vector3> { iT[k], oT[k], oB[k], iB[k] };
            if (step) poly.Add(iR[k]);
            EmitFacingConvex(b, poly, fwd);
        }

        b.NextPart = 0;
        b.NextKind = 0f;
    }

    // ══════════════════════════════════════════════════════════════
    // WHERE THINGS ARE
    // ══════════════════════════════════════════════════════════════

    /// <summary>How far along the run's centreline the point nearest <paramref name="at"/> is, seen from above.</summary>
    private static float ArcOnCentreline(RunGrid grid, Vector3 at)
    {
        float best = float.MaxValue, arc = 0f;

        for (int j = 0; j < grid.Rings - 1; j++)
        {
            Vector3 a = grid.centre[j];
            Vector3 d = grid.centre[j + 1] - a;
            float len2 = d.x * d.x + d.z * d.z;
            float t    = len2 < 1e-12f
                       ? 0f
                       : Mathf.Clamp01(((at.x - a.x) * d.x + (at.z - a.z) * d.z) / len2);

            float px = a.x + d.x * t - at.x, pz = a.z + d.z * t - at.z;
            float dd = px * px + pz * pz;
            if (dd >= best) continue;

            best = dd;
            arc  = Mathf.Lerp(grid.arc[j], grid.arc[j + 1], t);
        }
        return arc;
    }

    /// <summary>
    /// The cross line <paramref name="s"/> along the run. The cross axis is lerped and left
    /// unnormalised, so a point a set distance out along it is exactly on the straight edge the
    /// run's mesh draws between its two rings.
    /// </summary>
    private static WalkStation StationAt(RunGrid grid, float s)
    {
        int last = grid.Rings - 1;
        if (s <= grid.arc[0])    return new WalkStation { s = s, c = grid.centre[0],    r = grid.right[0] };
        if (s >= grid.arc[last]) return new WalkStation { s = s, c = grid.centre[last], r = grid.right[last] };

        int j = 0;
        while (j < last - 1 && grid.arc[j + 1] < s) j++;

        float span = grid.arc[j + 1] - grid.arc[j];
        float t    = span < 1e-9f ? 0f : (s - grid.arc[j]) / span;
        return new WalkStation
        {
            s = s,
            c = Vector3.Lerp(grid.centre[j], grid.centre[j + 1], t),
            r = Vector3.Lerp(grid.right[j],  grid.right[j + 1],  t),
        };
    }

    /// <summary>
    /// Where a cross line meets an outpost's front face: how far out along the line, and how far
    /// along the face from its middle. False when the line runs nearly along the face.
    /// </summary>
    private static bool FlatCrossing(WalkStation st, int side, WalkwayFlat flat,
                                     out float t, out float along, float inward = 0f)
    {
        t = 0f; along = 0f;

        Vector3 d = st.r * side; d.y = 0f;
        Vector3 a = flat.along;  a.y = 0f;
        if (a.sqrMagnitude < 1e-8f) return false;
        a.Normalize();

        // The face's normal, the way away from the river.
        Vector3 n = Vector3.Cross(Vector3.up, a);
        if (Vector3.Dot(n, d) < 0f) n = -n;

        float dn = Vector3.Dot(d, n);
        if (dn < 0.2f * d.magnitude) return false;

        // A line parallel to the face, in towards the river by this much.
        Vector3 face   = flat.face - n * inward;
        Vector3 toFace = face - st.c; toFace.y = 0f;
        t = Vector3.Dot(toFace, n) / dn;

        Vector3 hit = st.c + d * t - face; hit.y = 0f;
        along = Vector3.Dot(hit, a);
        return true;
    }

    // The first outpost this cross line runs into the front face of, or -1.
    private static int FlatAt(WalkStation st, int side, List<WalkwayFlat> flats)
    {
        for (int i = 0; i < flats.Count; i++)
            if (FlatCrossing(st, side, flats[i], out _, out float along) &&
                Mathf.Abs(along) <= flats[i].halfWidth)
                return i;
        return -1;
    }

    /// <summary>
    /// Adds to <paramref name="cuts"/> every place between <paramref name="s0"/> and
    /// <paramref name="s1"/> where the cross line passes an end of the outpost's face — the
    /// corner at <paramref name="end"/> along it — found against the rings, then closed in on.
    /// </summary>
    private static void FlatEnds(RunGrid grid, int side, WalkwayFlat flat, float s0, float s1,
                                 float end, List<float> cuts)
    {
        bool Off(float s, out float v)
        {
            bool ok = FlatCrossing(StationAt(grid, s), side, flat, out _, out float along);
            v = along - end;
            return ok;
        }

        var samples = new List<float> { s0 };
        for (int j = 0; j < grid.Rings; j++)
            if (grid.arc[j] > s0 && grid.arc[j] < s1) samples.Add(grid.arc[j]);
        samples.Add(s1);

        for (int i = 0; i + 1 < samples.Count; i++)
        {
            float lo = samples[i], hi = samples[i + 1];
            if (!Off(lo, out float vLo) || !Off(hi, out float vHi)) continue;
            if (vLo == 0f) { cuts.Add(lo); continue; }
            if (Mathf.Sign(vLo) == Mathf.Sign(vHi)) continue;

            for (int it = 0; it < 40; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (!Off(mid, out float vMid)) break;
                if (Mathf.Sign(vMid) == Mathf.Sign(vLo)) { lo = mid; vLo = vMid; }
                else                                       hi = mid;
            }
            cuts.Add((lo + hi) * 0.5f);
        }
    }
}
