using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// River portholes: a round hole bored straight across a run under its channel, square to the
/// river at a node (moved along it by an offset), through both of the run's outer walls and
/// through every walkway standing beside them.
///
///          walkway          run          walkway
///        ___________ ________________ ___________
///       |           |____        ____|           |
///       |           |    \______/    |           |
///       |    ___________________________________ |
///       |   (___________________________________)|   the bore
///       |___________|________________|___________|
///
/// A wall the bore passes through keeps its own upright lines — the run's rings, a walkway's
/// stations — and the hole is cut out between them. Every line inside the hole carries the two
/// points where the circle crosses it, and where the hole's side falls between two lines a point
/// at the bore's centre height is put there. Each point lies exactly on the bore's circle, so the
/// tube joining one wall's outline to the next is round whichever way the walls meet it — on a
/// bend the two walls of the run cut it at different places, and the tube is zipped round from
/// one outline to the other by angle.
///
/// The designer places the run's rings closely through every porthole
/// (<see cref="AddPortholeStops"/>), so every outline is round rather than a few chords.
/// </summary>
public static partial class RiverMeshBuilder
{
    /// <summary>How many rings the designer places across a porthole's width, at the least.</summary>
    public const int RiverPortholeSteps = 16;

    // The face part a walkway's porthole tube is, so it meets the walls it bores through at a seam.
    private const int WalkPortholePart = 3;

    /// <summary>A porthole placed on one run's grid.</summary>
    private struct RunPorthole
    {
        public Vector3 o;        // where the bore crosses the centreline
        public Vector3 f;        // flat, along the river there
        public Vector3 r;        // flat, across it — the way the bore runs
        public float   cy;       // height of the bore's centre
        public float   radius;

        // How far along the run the stretch the hole can touch reaches — two rings just clear of
        // it either side, so every wall face it cuts is between them.
        public float   sLo, sHi;
        public int     ringLo, ringHi;
    }

    /// <summary>
    /// The radius and centre height a porthole is built at — kept to the solid stone between the
    /// run's underside and the bottom of its channel, with a little left above and below. False
    /// when there is no room for one at all.
    /// </summary>
    public static bool PortholeFit(RiverProfile profile, float radius, float height,
                                   out float fitRadius, out float fitHeight)
    {
        // An outer flare leaves only the vertical wall under its foot to bore through.
        float room   = Mathf.Min(profile.depth - profile.riverDepth, profile.depth - FlareFootFall(profile));
        float margin = room * 0.02f;

        fitRadius = Mathf.Min(Mathf.Max(0f, radius), room * 0.5f - margin);
        fitHeight = Mathf.Clamp(height, fitRadius + margin, room - fitRadius - margin);
        return fitRadius > 1e-4f;
    }

    // ══════════════════════════════════════════════════════════════
    // RINGS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Puts rings closely across every porthole on a run, so the circle cut from its walls is
    /// round: the even spacing is lifted out from under the stretch the bore touches and at least
    /// <see cref="RiverPortholeSteps"/> rings set across it instead. <paramref name="walk"/> is the
    /// run's curve sampled densely, <paramref name="dist"/> how far along it each sample is, and
    /// <paramref name="stops"/> the distances along it the rings are placed at.
    /// </summary>
    public static void AddPortholeStops(RiverProfile profile, IList<Vector3> walk, IList<float> dist,
                                        List<float> stops)
    {
        if (profile?.portholes == null || profile.wallsOff || walk == null || walk.Count < 3) return;

        int   n      = walk.Count;
        float length = dist[n - 1];

        Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        foreach (var key in profile.portholes)
        {
            if (!PortholeFit(profile, key.radius, key.height, out float radius, out _)) continue;

            float s0 = Mathf.Clamp(NearestAlongWalk(walk, dist, key.at) + key.offsetAlong, 0f, length);

            int i0 = 0;
            while (i0 < n - 2 && dist[i0 + 1] < s0) i0++;
            float span = dist[i0 + 1] - dist[i0];
            float t    = span < 1e-9f ? 0f : Mathf.Clamp01((s0 - dist[i0]) / span);

            Vector3 o     = Vector3.Lerp(walk[i0], walk[i0 + 1], t);
            Vector3 along = Flat(walk[i0 + 1] - walk[i0]);
            if (along.sqrMagnitude < 1e-12f) continue;
            along.Normalize();

            float reach = Mathf.Max(key.reach, profile.OuterWidth * 0.5f);

            // Whether the cross line at a sample, as far out as the bore reaches, crosses its width.
            bool Touches(int i)
            {
                Vector3 fwd = Flat(walk[Mathf.Min(i + 1, n - 1)] - walk[Mathf.Max(i - 1, 0)]);
                if (fwd.sqrMagnitude < 1e-12f) return true;
                Vector3 right  = Vector3.Cross(Vector3.up, fwd.normalized);
                float   centre = Vector3.Dot(Flat(walk[i] - o), along);
                float   spread = reach * Mathf.Abs(Vector3.Dot(right, along));
                return centre - spread < radius && centre + spread > -radius;
            }

            int lo = i0;
            while (lo > 0 && Touches(lo)) lo--;
            int hi = i0 + 1;
            while (hi < n - 1 && Touches(hi)) hi++;

            float sLo = dist[lo], sHi = dist[hi];
            if (sHi - sLo < 1e-5f) continue;

            int count = Mathf.Max(RiverPortholeSteps,
                                  Mathf.CeilToInt(RiverPortholeSteps * (sHi - sLo) / (2f * radius)));

            // The even rings just outside go too, so none is left a sliver away from the first or
            // last of these — on a bend, two cross lines that close pinch where a walkway is.
            float gap = 0.5f * (sHi - sLo) / count;
            stops.RemoveAll(x => x > sLo - gap && x < sHi + gap && x > 1e-4f && x < length - 1e-4f);
            for (int c = 0; c <= count; c++) stops.Add(Mathf.Lerp(sLo, sHi, (float)c / count));
        }
    }

    // How far along a sampled curve the point nearest another is, seen from above.
    private static float NearestAlongWalk(IList<Vector3> walk, IList<float> dist, Vector3 at)
    {
        float best = float.MaxValue, along = 0f;
        for (int i = 0; i + 1 < walk.Count; i++)
        {
            Vector3 a = walk[i];
            Vector3 d = walk[i + 1] - a;
            float len2 = d.x * d.x + d.z * d.z;
            float t    = len2 < 1e-12f
                       ? 0f
                       : Mathf.Clamp01(((at.x - a.x) * d.x + (at.z - a.z) * d.z) / len2);

            float px = a.x + d.x * t - at.x, pz = a.z + d.z * t - at.z;
            float dd = px * px + pz * pz;
            if (dd >= best) continue;

            best  = dd;
            along = Mathf.Lerp(dist[i], dist[i + 1], t);
        }
        return along;
    }

    // ══════════════════════════════════════════════════════════════
    // PLACING
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// The portholes stamped on a run's profile, placed on its grid. Left out: one with no room,
    /// one running off either end of the run, one near a junction's mouth, and one overlapping a
    /// porthole already placed. A run with its outer walls off has none.
    /// </summary>
    private static List<RunPorthole> ResolvePortholes(RiverProfile profile, RunGrid grid,
                                                      List<RunMouth> mouths)
    {
        var holes = new List<RunPorthole>();
        if (profile?.portholes == null || profile.wallsOff || grid.Rings < 3) return holes;

        float last = grid.arc[grid.Rings - 1];

        foreach (var key in profile.portholes)
        {
            if (!PortholeFit(grid.profile, key.radius, key.height, out float radius, out float height))
                continue;

            float s  = Mathf.Clamp(ArcOnCentreline(grid, key.at) + key.offsetAlong, grid.arc[0], last);
            var   st = StationAt(grid, s);

            Vector3 across = st.r;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-12f) continue;
            across.Normalize();

            var hole = new RunPorthole
            {
                o      = st.c,
                f      = Vector3.Cross(across, Vector3.up),
                r      = across,
                cy     = st.c.y - grid.profile.depth + height,
                radius = radius,
            };

            float reach = Mathf.Max(key.reach, st.halfOuter);
            int   at    = grid.RingAt(s);

            int lo = at;
            while (lo > 0 && RingTouches(grid, hole, lo, reach)) lo--;
            int hi = at + 1;
            while (hi < grid.Rings - 1 && RingTouches(grid, hole, hi, reach)) hi++;

            // It runs off an end of the run.
            if (RingTouches(grid, hole, lo, reach) || RingTouches(grid, hole, hi, reach)) continue;

            hole.ringLo = lo;
            hole.ringHi = hi;
            hole.sLo    = grid.arc[lo];
            hole.sHi    = grid.arc[hi];

            // On a river climbing steeply enough the hole would cut the underside or the channel
            // somewhere along its width; it is left out there rather than built through them.
            bool clear = true;
            for (int j = lo; j <= hi && clear; j++)
            {
                float rim = grid.centre[j].y;
                clear = hole.cy - hole.radius > rim - grid.profile.depth + 1e-4f &&
                        hole.cy + hole.radius < rim - grid.profile.riverDepth - 1e-4f;
            }

            if (mouths != null)
                foreach (var mouth in mouths)
                {
                    float pad  = mouth.halfOuter + grid.profile.OuterWidth + mouth.collar;
                    float mLo  = Mathf.Min(mouth.fan[0], mouth.fan[mouth.columns]) - pad;
                    float mHi  = Mathf.Max(mouth.fan[0], mouth.fan[mouth.columns]) + pad;
                    clear &= hole.sHi < mLo || hole.sLo > mHi;
                }
            foreach (var other in holes)
                clear &= hole.ringLo >= other.ringHi || other.ringLo >= hole.ringHi;
            if (!clear) continue;

            holes.Add(hole);
        }
        return holes;
    }

    // Whether ring j's cross line, out as far as the bore reaches either side, crosses its width
    // — or comes near enough its side to count as on it, so the rings either end stand clear.
    private static bool RingTouches(RunGrid grid, RunPorthole hole, int j, float reach)
    {
        float centre = Bore(hole, grid.centre[j]);
        float spread = reach * Mathf.Abs(grid.right[j].x * hole.f.x + grid.right[j].z * hole.f.z);
        float wide   = hole.radius * (1f + 4f * HoleEdgeShare);
        return centre - spread < wide && centre + spread > -wide;
    }

    // ══════════════════════════════════════════════════════════════
    // THE CIRCLE ON A WALL
    // ══════════════════════════════════════════════════════════════

    // How far along the river from the bore's centre line a point is.
    private static float Bore(RunPorthole hole, Vector3 p)
        => (p.x - hole.o.x) * hole.f.x + (p.z - hole.o.z) * hole.f.z;

    // Where a wall's upright line stands against the hole: clear of it, on its very side (so
    // the hole's side IS a point of that line), or through it. Settled the same for both faces
    // either side of the line, which is what keeps them meeting on the same points.
    private enum HoleSide { Outside, Edge, Inside }

    // Closer to the hole's side than this, as a share of its radius, a line counts as on it.
    private const float HoleEdgeShare = 1e-3f;

    private static HoleSide SideOf(RunPorthole hole, float a)
    {
        float eps = hole.radius * HoleEdgeShare;
        float d   = Mathf.Abs(a) - hole.radius;
        return d < -eps ? HoleSide.Inside : d <= eps ? HoleSide.Edge : HoleSide.Outside;
    }

    private static float HalfChord(RunPorthole hole, float a)
        => Mathf.Sqrt(Mathf.Max(0f, hole.radius * hole.radius - a * a));

    // Where the hole crosses the upright line standing on a wall's foot, under and over its
    // centre — one point, at the centre height, when the line is on the hole's side.
    private static Vector3 HoleLow(RunPorthole hole, Vector3 foot, float a, HoleSide side)
        => new Vector3(foot.x, hole.cy - (side == HoleSide.Inside ? HalfChord(hole, a) : 0f), foot.z);

    private static Vector3 HoleHigh(RunPorthole hole, Vector3 foot, float a, HoleSide side)
        => new Vector3(foot.x, hole.cy + (side == HoleSide.Inside ? HalfChord(hole, a) : 0f), foot.z);

    /// <summary>
    /// The hole across one face of a wall, between the lines standing on <paramref name="foot0"/>
    /// and <paramref name="foot1"/>: its outline under the centre and over it, each from left to
    /// right — on the left line when <paramref name="onL"/>, then wherever a side of the hole
    /// falls between the lines, then on the right line when <paramref name="onR"/>. Both empty
    /// when none of the hole is there. <paramref name="tips"/> counts the sides falling between.
    /// </summary>
    private static void StripChains(RunPorthole hole, Vector3 foot0, Vector3 foot1,
                                    List<Vector3> low, List<Vector3> high,
                                    out bool onL, out bool onR, out int tips)
    {
        low.Clear(); high.Clear();
        tips = 0;

        float a0 = Bore(hole, foot0), a1 = Bore(hole, foot1);
        HoleSide s0 = SideOf(hole, a0), s1 = SideOf(hole, a1);
        onL = s0 != HoleSide.Outside;
        onR = s1 != HoleSide.Outside;

        if (onL) { low.Add(HoleLow(hole, foot0, a0, s0)); high.Add(HoleHigh(hole, foot0, a0, s0)); }

        // A side of the hole falling between the lines, where neither line is on it.
        float eps = hole.radius * HoleEdgeShare;
        float tA = -1f, tB = -1f;
        foreach (float e in new[] { -hole.radius, hole.radius })
        {
            float d0 = a0 - e, d1 = a1 - e;
            if (Mathf.Abs(d0) <= eps || Mathf.Abs(d1) <= eps || (d0 < 0f) == (d1 < 0f)) continue;
            float t = d0 / (d0 - d1);
            if (tA < 0f) tA = t; else tB = t;
        }
        if (tB >= 0f && tB < tA) (tA, tB) = (tB, tA);
        foreach (float t in new[] { tA, tB })
        {
            if (t < 0f) continue;
            Vector3 p = Vector3.Lerp(foot0, foot1, t);
            p.y = hole.cy;
            low.Add(p); high.Add(p);
            tips++;
        }

        if (onR) { low.Add(HoleLow(hole, foot1, a1, s1)); high.Add(HoleHigh(hole, foot1, a1, s1)); }
    }

    /// <summary>
    /// The hole's outline on one upright wall face — the wall standing on <paramref name="foot"/>
    /// at each of its lines, <paramref name="s"/> along the run — in order round it, under the
    /// centre one way and back over it. Null when the hole does not lie wholly on the face: the
    /// face starts or ends on it or inside it, or never reaches across it.
    /// </summary>
    private static List<Vector3> WallOutline(RunPorthole hole, IList<float> s, IList<Vector3> foot)
    {
        int first = -1, last = -1;
        for (int k = 0; k < foot.Count; k++)
        {
            if (s[k] < hole.sLo - 1e-5f || s[k] > hole.sHi + 1e-5f) continue;
            if (first < 0) first = k;
            last = k;
        }
        if (first < 0 || last <= first) return null;

        float aFirst = Bore(hole, foot[first]), aLast = Bore(hole, foot[last]);
        if (SideOf(hole, aFirst) != HoleSide.Outside || SideOf(hole, aLast) != HoleSide.Outside ||
            (aFirst < 0f) == (aLast < 0f))
            return null;

        var low  = new List<Vector3>();
        var high = new List<Vector3>();
        var lc   = new List<Vector3>(4);
        var hc   = new List<Vector3>(4);
        int ends = 0;

        for (int k = first; k < last; k++)
        {
            StripChains(hole, foot[k], foot[k + 1], lc, hc, out bool onL, out bool onR, out int tips);
            ends += tips;
            if (onR && SideOf(hole, Bore(hole, foot[k + 1])) == HoleSide.Edge) ends++;

            // The left line's points were the last strip's right line's.
            for (int i = onL ? 1 : 0; i < lc.Count; i++) { low.Add(lc[i]); high.Add(hc[i]); }
        }

        // In and out once; a wall that wanders back across the bore is left whole.
        if (ends != 2 || low.Count < 2) return null;

        var outline = new List<Vector3>(low);
        for (int k = high.Count - 2; k >= 1; k--) outline.Add(high[k]);
        return outline;
    }

    /// <summary>
    /// One upright wall face, from each of its lines to the next — standing from
    /// <paramref name="foot"/> to <paramref name="head"/>, <paramref name="s"/> along the run,
    /// each piece turned to face <paramref name="facing"/> — with every hole in
    /// <paramref name="holes"/> that reaches it cut out. Only pass holes whose
    /// <see cref="WallOutline"/> on this face closes.
    /// </summary>
    private static void BuildHoledWall(MeshBuild b, List<RunPorthole> holes, IList<float> s,
                                       IList<Vector3> foot, IList<Vector3> head,
                                       System.Func<int, Vector3> facing)
    {
        var low  = new List<Vector3>(4);
        var high = new List<Vector3>(4);

        for (int k = 0; k + 1 < foot.Count; k++)
        {
            Vector3 out3 = facing(k);

            bool found = false, onL = false, onR = false;
            for (int h = 0; holes != null && h < holes.Count && !found; h++)
            {
                var hole = holes[h];
                if (s[k] < hole.sLo - 1e-5f || s[k + 1] > hole.sHi + 1e-5f) continue;
                StripChains(hole, foot[k], foot[k + 1], low, high, out onL, out onR, out _);
                found = low.Count > 0;
            }

            if (!found)
            {
                FacingQuad(b, foot[k], foot[k + 1], head[k + 1], head[k], out3);
                continue;
            }

            Vector3 lowL = low[0],  lowR  = low[low.Count - 1];
            Vector3 highL = high[0], highR = high[high.Count - 1];

            // Under the hole, over it, and beside it wherever it starts or ends between the lines.
            FacingQuad(b, foot[k], foot[k + 1], lowR, lowL, out3);
            FacingQuad(b, highL, highR, head[k + 1], head[k], out3);
            if (!onL) FacingTri(b, foot[k],     lowL,        head[k], out3);
            if (!onR) FacingTri(b, foot[k + 1], head[k + 1], lowR,    out3);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // THE TUBE
    // ══════════════════════════════════════════════════════════════

    // How far round the bore a point of an outline is: 0 at the hole's side reached along the
    // river, over the top to half way round, and back under — 0 to 2 pi.
    private static float BoreAngle(RunPorthole hole, Vector3 p)
    {
        float th = Mathf.Atan2(p.y - hole.cy, Bore(hole, p));
        return th < 0f ? th + 2f * Mathf.PI : th;
    }

    /// <summary>
    /// The tube between the hole's outlines on two walls, facing in towards the bore. Each
    /// outline is taken in order round the bore, and the two are zipped together by angle — so
    /// every edge of either outline is the edge of one face, and the tube closes onto the walls.
    /// </summary>
    private static void BuildPortholeTube(MeshBuild b, RunPorthole hole,
                                          List<Vector3> outlineA, List<Vector3> outlineB)
    {
        if (outlineA == null || outlineB == null || outlineA.Count < 3 || outlineB.Count < 3) return;

        List<Vector3> ByAngle(List<Vector3> outline, out List<float> angles)
        {
            var sorted = new List<Vector3>(outline);
            sorted.Sort((p, q) => BoreAngle(hole, p).CompareTo(BoreAngle(hole, q)));
            angles = sorted.ConvertAll(p => BoreAngle(hole, p));
            return sorted;
        }

        var A = ByAngle(outlineA, out var ta);
        var B = ByAngle(outlineB, out var tb);
        int na = A.Count, nb = B.Count;

        float Next(List<float> t, int i, int n) => i + 1 >= n ? t[i + 1 - n] + 2f * Mathf.PI : t[i + 1];

        Vector3 axis = new Vector3(hole.o.x, hole.cy, hole.o.z);
        void Face(Vector3 p, Vector3 q, Vector3 r)
        {
            Vector3 mid = (p + q + r) / 3f;
            Vector3 on  = axis + hole.r * Vector3.Dot(mid - axis, hole.r);
            FacingTri(b, p, q, r, on - mid);
        }

        int ia = 0, ib = 0;
        while (ia < na || ib < nb)
        {
            bool stepA = ib >= nb || (ia < na && Next(ta, ia, na) <= Next(tb, ib, nb));
            Vector3 pa = A[ia % na], pb = B[ib % nb];

            if (stepA) { Face(pa, A[(ia + 1) % na], pb); ia++; }
            else       { Face(pa, B[(ib + 1) % nb], pb); ib++; }
        }
    }

    // The stone the inside of a porthole takes: its tuner part, or on Auto the River Runs' outer wall.
    private static float PortholeKind(RiverRunShadingSettings shading)
        => shading == null || shading.riverPortholes == StoneFaceKind.Auto
         ? RunPart(FaceOuter)
         : (float)shading.riverPortholes;

    // ══════════════════════════════════════════════════════════════
    // THE RUN
    // ══════════════════════════════════════════════════════════════

    // The foot and head of one of the run's outer walls at every ring of a porthole's stretch.
    private static void RunWallLines(RunPorthole hole, Vector3[][] ring, int footIndex, int headIndex,
                                     RunGrid grid, out float[] s, out Vector3[] foot, out Vector3[] head)
    {
        int n = hole.ringHi - hole.ringLo + 1;
        s    = new float[n];
        foot = new Vector3[n];
        head = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            int j = hole.ringLo + k;
            s[k]    = grid.arc[j];
            foot[k] = ring[j][footIndex];
            head[k] = ring[j][headIndex];
        }
    }

    /// <summary>
    /// The portholes a run can carry — each with its outline closing on both outer walls. The
    /// right wall stands from loop point 1 up to 2, the left from 0 up to the last.
    /// </summary>
    private static List<RunPorthole> RunPortholes(RiverProfile profile, RunGrid grid,
                                                  List<RunMouth> mouths, Vector3[][] ring, int loopCount)
    {
        var holes = ResolvePortholes(profile, grid, mouths);
        holes.RemoveAll(hole =>
        {
            RunWallLines(hole, ring, 1, 2, grid, out var s, out var footR, out _);
            RunWallLines(hole, ring, 0, loopCount - 1, grid, out _, out var footL, out _);
            return WallOutline(hole, s, footR) == null || WallOutline(hole, s, footL) == null;
        });
        return holes;
    }

    // Whether a strip of the run's outer walls, from ring j to the next, is built by a porthole.
    private static bool InPortholeStretch(List<RunPorthole> holes, int j)
    {
        foreach (var hole in holes)
            if (j >= hole.ringLo && j < hole.ringHi) return true;
        return false;
    }

    /// <summary>Both of the run's outer walls across a porthole's stretch, with the hole cut out, and the tube between them.</summary>
    private static void BuildRunPorthole(MeshBuild b, RunPorthole hole, Vector3[][] ring, int loopCount,
                                         RunGrid grid, RiverRunShadingSettings shading)
    {
        var one = new List<RunPorthole> { hole };

        RunWallLines(hole, ring, 1, 2, grid, out var s, out var footR, out var headR);
        RunWallLines(hole, ring, 0, loopCount - 1, grid, out _, out var footL, out var headL);

        Vector3 Out(int k)
        {
            int j = hole.ringLo + k;
            Vector3 d = grid.right[j] + grid.right[j + 1];
            d.y = 0f;
            return d.sqrMagnitude < 1e-12f ? grid.right[j] : d.normalized;
        }

        b.NextLabel = "Outer Wall";
        BuildHoledWall(b, one, s, footR, headR, k => Out(k));
        BuildHoledWall(b, one, s, footL, headL, k => -Out(k));

        var keepKind = b.NextKind;
        var keepAs   = b.NextDesignation;
        b.NextLabel       = "Porthole";
        b.NextKind        = PortholeKind(shading);
        b.NextDesignation = StoneDesignation.RiverPortholes;
        BuildPortholeTube(b, hole, WallOutline(hole, s, footR), WallOutline(hole, s, footL));
        b.NextKind        = keepKind;
        b.NextDesignation = keepAs;
        b.NextLabel       = null;
    }
}
