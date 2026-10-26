using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// River archways: a pillar on the rim either side of a river run, joined over the water by a
/// round arch, at a designer node. Seen looking down the river:
///
///         ______________________________ top
///        |        __________           |
///        |     /              \        |
///        |   /    (opening)     \      |
///        |  |                    |     |  jambs, when the arch is taller than it is wide
///   _____|__|      the run       |_____|_____
///        |rim                     rim  |
///
/// The opening spans the channel's inner width. Its top is a half ellipse as wide as the
/// channel: a round arch when the opening is at least half the channel's width high — with
/// upright jambs below it, from the rim, for the rest — and a flatter arch when lower. Each
/// pillar stands on the rim from the channel's edge outward; any part beyond the run's outer
/// edge reaches down to the same drop as the run. The two top outer corners can be curved, a
/// quarter circle each from the side up to the top.
///
/// One closed piece per archway: the cross-section above, swept the archway's thickness along
/// the run through every ring it covers — bending, climbing and widening with it, so it sits
/// on the rims all the way through. Built into the same piece as the walkways, in the river
/// material.
/// </summary>
public static partial class RiverMeshBuilder
{
    /// <summary>
    /// One archway across a run: the designer node it stands at, in the run's own local space,
    /// and its sizes.
    /// </summary>
    public struct Archway
    {
        public Vector3 at;
        public float   archHeight;
        public float   thickness;
        public float   pillarWidth;
        public float   archDepth;

        /// <summary>How far along the run from its node it stands — down the run when positive.</summary>
        public float   offsetAlong;

        /// <summary>How far round the two top outer corners are curved — 0 leaves them square.</summary>
        public float   cornerRadius;
    }

    // Straight pieces round each half of the arch, springing to crown — kept low.
    private const int ArchwaySegmentsPerSide = 5;

    // Straight pieces round each curved top corner.
    private const int ArchwayCornerSegments = 4;

    private static void BuildArchway(MeshBuild b, RunGrid grid, Archway arch,
                                     RiverRunShadingSettings shading)
    {
        float hw = Mathf.Max(arch.thickness, 0.005f) * 0.5f;

        // Moved along the run's own centreline and kept wholly on the run — at a river's end it
        // stands just inside where the run stops.
        float first = grid.arc[0], last = grid.arc[grid.Rings - 1];
        float s     = ArcOnCentreline(grid, arch.at) + arch.offsetAlong;
        s = last - first > 2f * hw ? Mathf.Clamp(s, first + hw, last - hw) : (first + last) * 0.5f;
        // Swept along the run itself, ring by ring, so the pillars stay on the rims however the
        // river bends, climbs or widens through the archway. The section is drawn at the
        // archway's middle, measured from the rim top there, and carried to each station.
        var stations = StationsBetween(grid, s - hw, s + hw);
        if (stations.Count < 2) return;
        int ends = stations.Count - 1;

        float halfInner = grid.HalfInnerAtArc(s);
        float halfOuter = halfInner + grid.profile.rimWidth;
        float outerX    = halfInner + Mathf.Max(arch.pillarWidth, 0.005f);

        const float rimY = 0f;
        float height = Mathf.Max(arch.archHeight, 0.01f);

        // A round arch, with jambs below it, when the opening is taller than half the channel;
        // otherwise a flatter arch springing straight off the rim.
        bool  jamb    = height > halfInner + 0.002f;
        float rise    = jamb ? halfInner : height;
        float springY = jamb ? rimY + height - rise : rimY;   // exactly the rim when no jamb
        float topY    = rimY + height + Mathf.Max(arch.archDepth, 0.005f);
        float baseY   = rimY - grid.profile.depth;
        bool  pastRim = outerX > halfOuter + 0.002f;

        // The top outer corner, square or curved: a quarter circle from the side up to the top,
        // kept above the springing and within the archway's half-width.
        float above   = topY - springY;
        float radius  = Mathf.Clamp(arch.cornerRadius, 0f, Mathf.Min(outerX, above));
        var   centre  = new Vector2(outerX - radius, above - radius);   // from the arch's centre
        int   cornerN = radius > 1e-5f ? ArchwayCornerSegments : 0;
        var   cornerPts = new Vector2[cornerN + 1];
        for (int j = 0; j <= cornerN; j++)
        {
            float a = cornerN == 0 ? 0f : Mathf.PI * 0.5f * j / cornerN;
            cornerPts[j] = centre + radius * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        cornerPts[0].x       = outerX;   // exactly on the side and the top, so their faces stay theirs
        cornerPts[cornerN].y = above;

        // Where the ray out from the arch's centre along d meets the side, the corner or the top.
        Vector2 Outward(Vector2 d)
        {
            if (d.y <= 0f) return new Vector2(outerX, 0f);
            if (d.x > 0f && d.y * outerX / d.x <= above - radius)
                return new Vector2(outerX, d.y * outerX / d.x);
            if (d.x * above / d.y <= outerX - radius)
                return new Vector2(d.x * above / d.y, above);

            float dd = Vector2.Dot(d, d), dc = Vector2.Dot(d, centre);
            float t  = (dc + Mathf.Sqrt(Mathf.Max(0f, dc * dc - dd * (Vector2.Dot(centre, centre) -
                                                                      radius * radius)))) / dd;
            return d * t;
        }

        // The arch's underside, springing to crown, where the ray out through each of its points
        // meets the archway's outline, and the corner's points lying between one and the next.
        int n = ArchwaySegmentsPerSide;
        var inner = new Vector2[n + 1];
        var outer = new Vector2[n + 1];
        var angle = new float[n + 1];
        for (int k = 0; k <= n; k++)
        {
            float   a = Mathf.PI * 0.5f * k / n;
            Vector2 d = new Vector2(k == n ? 0f : halfInner * Mathf.Cos(a), rise * Mathf.Sin(a));
            inner[k] = new Vector2(d.x, springY + d.y);

            Vector2 o = Outward(d);
            outer[k] = new Vector2(o.x, springY + o.y);
            angle[k] = Mathf.Atan2(o.y, o.x);
        }
        // A corner point is left out when a ray meets the corner close beside it: the two would
        // all but overlap, and the end faces' pieces could fold.
        float near = cornerN == 0 ? 1e-4f
                                  : Mathf.Max(1e-4f, 0.3f * radius * Mathf.PI * 0.5f / cornerN);
        var between = new List<Vector2>[n];
        for (int k = 0; k < n; k++)
        {
            between[k] = new List<Vector2>();
            foreach (var c in cornerPts)
            {
                var   p  = new Vector2(c.x, springY + c.y);
                float ca = Mathf.Atan2(c.y, c.x);
                if (ca > angle[k] && ca < angle[k + 1] &&
                    (p - outer[k]).magnitude > near && (p - outer[k + 1]).magnitude > near)
                    between[k].Add(p);
            }
        }

        // ── The cross-section, anticlockwise looking down the run (x across, y up) ──
        var right = new List<Vector2>();
        void Add(List<Vector2> list, Vector2 p)
        {
            if (list.Count == 0 || (list[list.Count - 1] - p).sqrMagnitude > 1e-12f) list.Add(p);
        }

        // Right pillar: its foot, out and up its side, then along the top to the middle.
        Add(right, new Vector2(halfInner, rimY));
        if (pastRim)
        {
            Add(right, new Vector2(halfOuter, rimY));
            Add(right, new Vector2(halfOuter, baseY));
            Add(right, new Vector2(outerX,    baseY));
        }
        Add(right, new Vector2(outerX, rimY));
        for (int k = 0; k <= n; k++)
        {
            Add(right, outer[k]);
            if (k < n) foreach (var c in between[k]) Add(right, c);
        }

        var loop = new List<Vector2>(right);
        for (int i = right.Count - 2; i >= 0; i--) Add(loop, new Vector2(-right[i].x, right[i].y));

        // The faces from here round to the start are the opening: the jambs and under the arch.
        int openStart = loop.Count - 1;

        // Up the left jamb and under the arch to the crown, then down to the right jamb's foot.
        for (int k = 0; k <= n; k++) Add(loop, new Vector2(-inner[k].x, inner[k].y));
        for (int k = n - 1; k >= 0; k--) Add(loop, inner[k]);
        if ((loop[loop.Count - 1] - loop[0]).sqrMagnitude < 1e-12f) loop.RemoveAt(loop.Count - 1);

        // A point of the section at station k: on that station's own rim line, the way the run's
        // mesh lays its rings, and moved with the channel's width there.
        Vector3 At(Vector2 p, int k)
        {
            var t = stations[k];
            return t.c + t.r * AcrossAt(p.x, halfInner, t.halfInner) + Vector3.up * p.y;
        }

        // Across, and down the run, between two stations or at one.
        Vector3 Across(int k0, int k1)
        {
            Vector3 a = stations[k0].r + stations[k1].r; a.y = 0f;
            return a.sqrMagnitude < 1e-12f ? Vector3.right : a.normalized;
        }
        Vector3 Along(int k) => Vector3.Cross(Across(k, k), Vector3.up);

        float Kind(System.Func<RiverRunShadingSettings, StoneFaceKind> pick) =>
            shading != null && pick != null ? (float)pick(shading) : 0f;

        // The faces round the section, each edge swept from the upstream side to the downstream:
        // the top, the walls that show (outer sides and the opening), and what sits on the rim or
        // stands down past the run's outer edge.
        const float eps = 1e-5f;
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 p = loop[i], q = loop[(i + 1) % loop.Count];
            Vector2 d = q - p;
            if (d.sqrMagnitude < 1e-10f) continue;

            bool top    = Mathf.Abs(p.y - topY) < eps && Mathf.Abs(q.y - topY) < eps;
            bool side   = Mathf.Abs(Mathf.Abs(p.x) - outerX) < eps && Mathf.Abs(Mathf.Abs(q.x) - outerX) < eps;
            bool buried = !side && p.y <= rimY + eps && q.y <= rimY + eps;
            bool opening = i >= openStart;

            b.NextPart = top ? WalkTopPart : buried ? WalkBuriedPart : WalkWallPart;
            b.NextKind = top     ? Kind(x => x.riverArchTop)
                       : buried  ? 0f
                       : opening ? Kind(x => x.riverArchOpening)
                                 : Kind(x => x.riverArchOutside);
            b.NextLabel = buried ? null : (p.y + q.y) * 0.5f < springY - eps ? "Pillar" : "Arch";

            for (int k = 0; k < ends; k++)
            {
                Vector3 outward = Across(k, k + 1) * d.y - Vector3.up * d.x;
                FacingQuad(b, At(p, k), At(q, k), At(q, k + 1), At(p, k + 1), outward);
            }
        }

        // The two faces up and down the river, in convex pieces sharing every corner the faces
        // round the section have: the stone between each piece of the arch and the side or top
        // beyond it, the jamb below the springing, and the part past the run's outer edge.
        var pieces = new List<List<Vector2>>();
        for (int k = 0; k < n; k++)
        {
            var piece = new List<Vector2> { inner[k] };
            if (k == 0 && !jamb && pastRim) piece.Add(new Vector2(halfOuter, rimY));
            piece.Add(outer[k]);
            piece.AddRange(between[k]);
            piece.Add(outer[k + 1]);
            piece.Add(inner[k + 1]);
            pieces.Add(piece);
        }
        if (jamb)
        {
            var piece = new List<Vector2> { new Vector2(halfInner, rimY) };
            if (pastRim) piece.Add(new Vector2(halfOuter, rimY));
            piece.Add(new Vector2(outerX, rimY));
            piece.Add(new Vector2(outerX, springY));
            piece.Add(new Vector2(halfInner, springY));
            pieces.Add(piece);
        }
        if (pastRim)
            pieces.Add(new List<Vector2>
            {
                new Vector2(halfOuter, baseY), new Vector2(outerX, baseY),
                new Vector2(outerX, rimY),     new Vector2(halfOuter, rimY),
            });

        b.NextPart = WalkWallPart;
        b.NextKind = Kind(x => x.riverArchFaces);
        foreach (var piece in pieces)
        {
            b.NextLabel = pieces.IndexOf(piece) < n ? "Arch" : "Pillar";
            foreach (float mirror in new[] { 1f, -1f })
                foreach (int k in new[] { 0, ends })
                {
                    var poly = new List<Vector3>(piece.Count);
                    foreach (var p in piece) poly.Add(At(new Vector2(p.x * mirror, p.y), k));
                    EmitFacingConvex(b, poly, Along(k) * (k == 0 ? -1f : 1f));
                }
        }

        b.NextPart = 0;
        b.NextKind = 0f;
        b.NextLabel = null;
    }

    /// <summary>
    /// One place along a run, as its mesh stands there: the rim-top centre, the cross axis (left
    /// unnormalised between rings, so a point a set distance out along it is on the run's own
    /// edge) and half the channel's width.
    /// </summary>
    public struct RunStation
    {
        public float   s;
        public Vector3 c;
        public Vector3 r;
        public float   halfInner;
    }

    /// <summary>
    /// The stations from <paramref name="s0"/> to <paramref name="s1"/> along a run (either way
    /// round): both ends and every ring between them, so a piece swept through them follows
    /// the run's rims exactly. Out past the run's ends it stops at the end ring.
    /// </summary>
    public static List<RunStation> RunStations(RiverProfile profile, IList<Vector3> centres,
                                               IList<Vector3> forwards, float edge,
                                               float s0, float s1)
    {
        if (profile == null || centres == null || forwards == null || centres.Count < 2)
            return new List<RunStation>();

        var grid = new RunGrid(profile, centres, forwards, Mathf.Max(edge, 0.005f));
        return grid.Rings < 2 ? new List<RunStation>() : StationsBetween(grid, s0, s1);
    }

    private static List<RunStation> StationsBetween(RunGrid grid, float s0, float s1)
    {
        const float gap = 1e-5f;
        float lo = grid.arc[0], hi = grid.arc[grid.Rings - 1];
        s0 = Mathf.Clamp(s0, lo, hi);
        s1 = Mathf.Clamp(s1, lo, hi);

        var list = new List<RunStation>();
        void Add(float s)
        {
            if (list.Count > 0 && Mathf.Abs(list[list.Count - 1].s - s) < gap) return;
            var st = StationAt(grid, s);
            list.Add(new RunStation { s = s, c = st.c, r = st.r, halfInner = grid.HalfInnerAtArc(s) });
        }

        Add(s0);
        if (s1 >= s0)
        {
            for (int j = 0; j < grid.Rings; j++)
                if (grid.arc[j] > s0 + gap && grid.arc[j] < s1 - gap) Add(grid.arc[j]);
        }
        else
        {
            for (int j = grid.Rings - 1; j >= 0; j--)
                if (grid.arc[j] < s0 - gap && grid.arc[j] > s1 + gap) Add(grid.arc[j]);
        }
        Add(s1);
        return list;
    }
}
