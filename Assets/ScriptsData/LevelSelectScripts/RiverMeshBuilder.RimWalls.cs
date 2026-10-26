using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rim walls: a wall standing straight up off a river's rim, or right round a pool's.
///
///                 wall
///                |‾‾‾‾|
///                |    |
///        ________|____|          run
///       |  rim   |          ____________
///       |________|         /  channel
///
/// On a run it goes from one designer node to the next, its foot on the rim — the whole rim by
/// default, or a thinner band in the middle of it — and its top a set height above the rim,
/// riding the rim as the river climbs. Where the run ends at a pool, a wall reaching that end
/// carries straight on across the collar onto the pool's rim, up to the pool's own wall.
///
/// On a pool it goes all the way round, open where each river comes in: the opening is cut
/// along the arriving river's own lines, where its wall would stand, so a river with a rim wall
/// of its own runs into the gap and closes it.
///
/// A piece of its own standing on the rim, never cut into it. Each stretch is a closed box.
/// </summary>
public static partial class RiverMeshBuilder
{
    /// <summary>
    /// One stretch of rim wall on one side of a run: the designer nodes it runs between, in the
    /// run's own local space, which side it is on, how high it stands above the rim, and how
    /// much of the rim's width it takes.
    /// </summary>
    public struct RimWall
    {
        public Vector3 from;
        public Vector3 to;
        public int     side;        // +1 the run's right, -1 its left
        public float   height;
        public float   thickness;   // share of the rim's width, 1 the whole rim
    }

    /// <summary>
    /// A pool one end of a run meets, in the run's own local space — so a rim wall reaching that
    /// end carries on across the collar onto the pool's rim.
    /// </summary>
    public struct RimWallPool
    {
        public bool    present;

        /// <summary>The pool's centre, at its rim height.</summary>
        public Vector3 centre;

        public float   poolRadius;
        public float   rimWidth;

        /// <summary>The pool's own rim wall's thickness — 0 when the pool has none.</summary>
        public float   wallThickness;
    }

    /// <summary>Lowest a rim wall is built.</summary>
    public const float RimWallMinHeight = 0.01f;

    /// <summary>Thinnest a rim wall is built, as a share of the rim's width.</summary>
    public const float RimWallMinThickness = 0.05f;

    // Where a river's wall carries on into a pool that has a wall of its own, it stops this far
    // short of that wall's face — inside it, so the two faces never lie on top of each other.
    private const float RimWallTuck = 0.002f;

    // The top is one part; the faces that show another; the foot standing on the rim a third —
    // so the line the wall meets the rim along is a seam.
    private const int RimWallTopPart    = 0;
    private const int RimWallFacePart   = 1;
    private const int RimWallBuriedPart = 2;

    // A cross line of a wall: its foot on the river's side and on the far side, each on the rim's
    // own surface, and the height of its top.
    private struct RimWallStation
    {
        public Vector3 inner;
        public Vector3 outer;
        public float   top;
    }

    // How far below the rim top the rim's surface is, this far out from the channel edge — 0
    // across a flat rim, falling into a rounded one's corners.
    private static float RimFall(RiverProfile profile, float fromInner) =>
        SectionCut(profile, profile.innerWidth * 0.5f + Mathf.Clamp(fromInner, 0f, profile.rimWidth));

    // ══════════════════════════════════════════════════════════════
    // ON A RUN
    // ══════════════════════════════════════════════════════════════

    private static void BuildRimWall(MeshBuild b, RunGrid grid, RimWall wall,
                                     RimWallPool poolAtStart, RimWallPool poolAtEnd,
                                     RiverRunShadingSettings shading)
    {
        var   profile = grid.profile;
        float rim     = profile.rimWidth;
        if (rim <= 0.0001f) return;

        int side = wall.side >= 0 ? 1 : -1;

        float s0 = ArcOnCentreline(grid, wall.from);
        float s1 = ArcOnCentreline(grid, wall.to);
        if (s0 > s1) (s0, s1) = (s1, s0);
        if (s1 - s0 < 1e-4f) return;

        float thickness = Mathf.Clamp(wall.thickness, RimWallMinThickness, 1f);
        float height    = Mathf.Max(wall.height, RimWallMinHeight);
        float inset     = rim * (1f - thickness) * 0.5f;
        float fallIn    = RimFall(profile, inset);
        float fallOut   = RimFall(profile, rim - inset);

        var stations = new List<RimWallStation>();
        void Add(float s)
        {
            var     w     = StationAt(grid, s);
            Vector3 d     = w.r * side;
            float   rimY  = w.c.y;
            Vector3 inner = w.c + d * (w.halfOuter - rim + inset);
            Vector3 outer = w.c + d * (w.halfOuter - inset);
            inner.y = rimY - fallIn;
            outer.y = rimY - fallOut;
            stations.Add(new RimWallStation { inner = inner, outer = outer, top = rimY + height });
        }

        Add(s0);
        for (int j = 0; j < grid.Rings; j++)
            if (grid.arc[j] > s0 + 1e-4f && grid.arc[j] < s1 - 1e-4f) Add(grid.arc[j]);
        Add(s1);

        // Carried on into a pool the run stops short of, when the wall reaches that end.
        int last = grid.Rings - 1;
        if (poolAtStart.present && s0 <= grid.arc[0] + 1e-4f &&
            IntoPool(grid, 0, -1, side, inset, height, fallIn, fallOut, thickness, poolAtStart, out var into))
            stations.Insert(0, into);
        if (poolAtEnd.present && s1 >= grid.arc[last] - 1e-4f &&
            IntoPool(grid, last, 1, side, inset, height, fallIn, fallOut, thickness, poolAtEnd, out into))
            stations.Add(into);

        b.NextLabel = "Rim Wall";
        BuildRimWallBox(b, stations, false, shading);
        b.NextLabel = null;
    }

    /// <summary>
    /// The wall's cross line where it reaches a pool from ring <paramref name="ring"/> — straight
    /// on from the run's end (<paramref name="way"/> +1 off its last ring, -1 back off its first),
    /// each edge of the wall stopping where it meets the circle the pool's wall stands inside: its
    /// foot band on the pool's rim, or tucked just inside the pool's own wall where it has one.
    /// </summary>
    private static bool IntoPool(RunGrid grid, int ring, int way, int side, float inset, float height,
                                 float fallIn, float fallOut, float thickness, RimWallPool pool,
                                 out RimWallStation station)
    {
        station = default;

        Vector3 c   = grid.centre[ring];
        Vector3 d   = grid.right[ring] * side;
        Vector3 fwd = Vector3.Cross(grid.right[ring], Vector3.up) * way;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-8f) return false;
        fwd.Normalize();

        float half = grid.HalfInnerAt(ring);
        float rim  = grid.profile.rimWidth;

        // The band the pool's own wall stands in, or this wall's where the pool has none.
        float poolShare = pool.wallThickness > 0f ? Mathf.Clamp(pool.wallThickness, RimWallMinThickness, 1f)
                                                  : thickness;
        float reach = pool.poolRadius + pool.rimWidth * (1f - poolShare) * 0.5f
                    + (pool.wallThickness > 0f ? RimWallTuck : 0f);

        bool Hit(float across, out Vector3 at)
        {
            at = c + d * across;
            float ax = at.x - pool.centre.x, az = at.z - pool.centre.z;
            float along = ax * fwd.x + az * fwd.z;
            float far   = ax * ax + az * az - reach * reach;
            float disc  = along * along - far;
            if (far <= 0f || along >= 0f || disc < 0f) return false;   // already inside, or heading away

            float t = -along - Mathf.Sqrt(disc);
            at += fwd * t;
            return true;
        }

        if (!Hit(half + inset, out Vector3 inner) || !Hit(half + rim - inset, out Vector3 outer))
            return false;

        inner.y = pool.centre.y - fallIn;
        outer.y = pool.centre.y - fallOut;
        station = new RimWallStation { inner = inner, outer = outer, top = pool.centre.y + height };
        return true;
    }

    // ══════════════════════════════════════════════════════════════
    // ON A POOL
    // ══════════════════════════════════════════════════════════════

    // Where a river comes in through a pool's wall: the gap's two ends round the circle, at the
    // wall's river-side face and at its far face. Angles are measured anticlockwise seen from
    // above, from +x towards +z.
    private struct RimWallGap
    {
        public float innerFrom, innerTo;
        public float outerFrom, outerTo;
    }

    /// <summary>
    /// The wall on a pool's rim, all the way round and open where each river comes in, in the
    /// pool's own local space (rim top at y = 0). <paramref name="thickness"/> is the share of
    /// the rim's width it takes, in the middle of the rim; each opening is cut along the same
    /// share of the arriving river's rim, where that river's own wall would stand.
    /// </summary>
    /// <param name="doorways">One per mouth, in the same order: true where that mouth is closed
    /// by a doorway — the wall carried across it with <paramref name="door"/>'s keyhole cut
    /// through. Null for none.</param>
    public static Mesh BuildPoolRimWall(RiverProfile profile, float poolRadius, IList<PoolMouth> mouths,
                                        float height, float thickness, float edge,
                                        RiverRunShadingSettings shading = null,
                                        IList<bool> doorways = null, PoolDoorway door = default)
    {
        var b = new MeshBuild { NextDesignation = StoneDesignation.RimWalls };
        if (profile == null || profile.rimWidth <= 0.0001f) return b.ToMesh("PoolRimWall");

        edge       = Mathf.Max(edge, 0.005f);
        poolRadius = Mathf.Max(poolRadius, 0.01f);
        thickness  = Mathf.Clamp(thickness, RimWallMinThickness, 1f);
        height     = Mathf.Max(height, RimWallMinHeight);

        float rim     = profile.rimWidth;
        float inset   = rim * (1f - thickness) * 0.5f;
        float rIn     = poolRadius + inset;
        float rOut    = poolRadius + rim - inset;
        float fallIn  = RimFall(profile, inset);
        float fallOut = RimFall(profile, rim - inset);

        var gaps  = new List<RimWallGap>();
        var doors = new List<PoolMouth>();
        if (mouths != null)
            for (int m = 0; m < mouths.Count; m++)
            {
                if (!PoolWallGap(mouths[m], thickness, rIn, rOut, out var gap)) continue;
                gaps.Add(gap);
                if (doorways != null && m < doorways.Count && doorways[m]) doors.Add(mouths[m]);
            }
        gaps.Sort((p, q) => p.innerFrom.CompareTo(q.innerFrom));

        RimWallStation At(float innerAngle, float outerAngle) => new RimWallStation
        {
            inner = new Vector3(Mathf.Cos(innerAngle) * rIn,  -fallIn,  Mathf.Sin(innerAngle) * rIn),
            outer = new Vector3(Mathf.Cos(outerAngle) * rOut, -fallOut, Mathf.Sin(outerAngle) * rOut),
            top   = height,
        };

        b.NextLabel = "Rim Wall";
        if (gaps.Count == 0)
        {
            int n = Mathf.Max(12, Mathf.CeilToInt(TwoPi * rOut / edge));
            var ring = new List<RimWallStation>(n);
            for (int k = 0; k < n; k++)
            {
                float a = TwoPi * k / n;
                ring.Add(At(a, a));
            }
            BuildRimWallBox(b, ring, true, shading);
        }
        else
        {
            // A stretch of wall from the end of each gap to the start of the next, round the circle.
            for (int g = 0; g < gaps.Count; g++)
            {
                var from = gaps[g];
                var to   = gaps[(g + 1) % gaps.Count];
                float wrap = g + 1 < gaps.Count ? 0f : TwoPi;

                // With one gap, from and to are the same one: the wall runs right round from its
                // far end back to its near end.
                float innerA = from.innerTo, innerZ = to.innerFrom + wrap;
                float outerA = from.outerTo, outerZ = to.outerFrom + wrap;
                if (innerZ - innerA < 1e-4f || outerZ - outerA < 1e-4f) continue;   // two gaps meet

                float longest = Mathf.Max((innerZ - innerA) * rIn, (outerZ - outerA) * rOut);
                int   steps   = Mathf.Max(1, Mathf.CeilToInt(longest / edge));

                var stretch = new List<RimWallStation>(steps + 1);
                for (int k = 0; k <= steps; k++)
                {
                    float t = (float)k / steps;
                    stretch.Add(At(Mathf.Lerp(innerA, innerZ, t), Mathf.Lerp(outerA, outerZ, t)));
                }
                BuildRimWallBox(b, stretch, false, shading);
            }
        }

        foreach (var mouth in doors)
            BuildPoolDoorway(b, mouth, thickness, rIn, rOut, fallIn, fallOut, height, door, edge, shading);

        b.NextLabel       = null;
        b.NextKind        = 0f;
        b.NextPart        = 0;
        b.NextDesignation = StoneDesignation.None;

        // Built with the rim top flat at y = 0, as the pool is.
        b.ShadeSeams(null, 0f);
        return b.ToMesh("PoolRimWall");
    }

    /// <summary>
    /// Where one river comes in through the pool's wall: between the two lines its own wall
    /// would stand along — the arriving river's rim, the same share of it the pool's wall takes —
    /// out to the far side of that band. False when the river is too wide to cut the circle.
    /// </summary>
    private static bool PoolWallGap(PoolMouth mouth, float thickness, float rIn, float rOut,
                                    out RimWallGap gap)
    {
        gap = default;

        Vector3 dir = mouth.direction;
        dir.y = 0f;
        if (mouth.profile == null || dir.sqrMagnitude < 1e-8f) return false;
        dir.Normalize();

        var   p     = mouth.profile;
        float reach = p.innerWidth * 0.5f + p.rimWidth - p.rimWidth * (1f - thickness) * 0.5f;

        // Across the mouth, its lines are where the run's cross axis reaches ±reach.
        Vector3 right  = Vector3.Cross(Vector3.up, dir);
        float   offset = mouth.centre.x * right.x + mouth.centre.z * right.z;

        bool Angle(float across, float radius, out float angle)
        {
            angle = 0f;
            float k = across + offset;
            if (Mathf.Abs(k) >= radius) return false;

            Vector3 at = right * k + dir * Mathf.Sqrt(radius * radius - k * k);
            angle = Mathf.Atan2(at.z, at.x);
            return true;
        }

        // Anticlockwise the right-hand line comes first: right is clockwise of the way out.
        if (!Angle( reach, rIn,  out float inFrom)  || !Angle(-reach, rIn,  out float inTo) ||
            !Angle( reach, rOut, out float outFrom) || !Angle(-reach, rOut, out float outTo))
            return false;

        inFrom = Mathf.Repeat(inFrom, TwoPi);
        while (inTo   < inFrom) inTo   += TwoPi;
        while (outFrom < inFrom - Mathf.PI) outFrom += TwoPi;
        while (outFrom > inFrom + Mathf.PI) outFrom -= TwoPi;
        while (outTo  < outFrom) outTo += TwoPi;

        gap = new RimWallGap { innerFrom = inFrom, innerTo = inTo, outerFrom = outFrom, outerTo = outTo };
        return true;
    }

    // ══════════════════════════════════════════════════════════════
    // DOORWAYS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// The keyhole cut through the wall where a doorway closes a pool's mouth — the arena door's
    /// silhouette, its foot as wide as the arriving river's channel. Heights are up from the rim top.
    /// </summary>
    public struct PoolDoorway
    {
        public float headRadius;
        public float bodyHeight;
        public float neckWidth;
        public float height;
        public float shoulderWidth;       // 0, or no wider than the neck: no shoulders
        public float shoulderRoundness;   // 0 to 1
    }

    // The inside of the keyhole — its own part, so it meets the wall's faces at a seam.
    private const int RimWallDoorPart = 3;

    // One edge bounding the wall's solid in a strip: the foot, the top, or a keyhole edge.
    private enum DoorwayBound { Foot, Top, Keyhole }

    private struct DoorwayLine
    {
        public DoorwayBound bound;
        public float        ya, yb;   // its height at the strip's two sides
        public float Mid => (ya + yb) * 0.5f;
    }

    /// <summary>
    /// The wall carried across a pool's mouth with a keyhole cut through it. Seen along the
    /// river it is the gap the wall leaves — the arriving river's wall lines either side, the
    /// foot to the wall's top — less the keyhole standing on the channel. Its two faces lie on
    /// the wall's own two circles, so it reads as the wall closing over the mouth, and its sides
    /// land exactly on the ends of the wall either side.
    ///
    /// Built as upright strips across the mouth: at every station where anything changes — a
    /// keyhole corner, a crossing of the wall's top — and at least every <paramref name="edge"/>,
    /// so the faces bend round the circle the way the wall's own do. Within a strip no edge
    /// starts or stops, so the solid there is a stack of plain four-sided pieces.
    /// </summary>
    private static void BuildPoolDoorway(MeshBuild b, PoolMouth mouth, float thickness, float rIn, float rOut,
                                         float fallIn, float fallOut, float height, PoolDoorway door,
                                         float edge, RiverRunShadingSettings shading)
    {
        Vector3 dir = mouth.direction;
        dir.y = 0f;
        if (mouth.profile == null || dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();

        var   p      = mouth.profile;
        float reach  = p.innerWidth * 0.5f + p.rimWidth - p.rimWidth * (1f - thickness) * 0.5f;
        float half   = p.innerWidth * 0.5f;
        Vector3 right  = Vector3.Cross(Vector3.up, dir);
        float   offset = mouth.centre.x * right.x + mouth.centre.z * right.z;
        if (Mathf.Abs(offset) + reach >= rIn || half <= 0.0001f) return;

        // The keyhole, closed along the rim top under it.
        var hole = ArenaDoorMesh.KeyholeOutline(half,
                                                Mathf.Max(0.001f, door.neckWidth * 0.5f),
                                                Mathf.Max(0f,     door.bodyHeight),
                                                Mathf.Max(0.001f, door.headRadius),
                                                Mathf.Max(0.001f, door.height), edge,
                                                Mathf.Max(0f, door.shoulderWidth * 0.5f),
                                                door.shoulderRoundness);
        if (hole.Count < 3) return;

        // Corners and crossings closer than a millimetre are one: anything finer builds faces
        // too small for MeshBuild to keep, and leaves a hole where they should be.
        const float Merge = 0.001f;

        // Corners closer together than twice that are one — where a flat shoulder runs into the
        // neck, say. Twice, because two corners a station apart are then far enough apart up it
        // that the faces between them keep. The two feet stay exactly where they are.
        const float Apart = 2f * Merge;
        {
            Vector2 lastFoot = hole[hole.Count - 1];
            var kept = new List<Vector2>(hole.Count) { hole[0] };
            for (int i = 1; i < hole.Count - 1; i++)
                if ((hole[i] - kept[kept.Count - 1]).sqrMagnitude >= Apart * Apart) kept.Add(hole[i]);
            if (kept.Count > 1 && (lastFoot - kept[kept.Count - 1]).sqrMagnitude < Apart * Apart)
                kept.RemoveAt(kept.Count - 1);
            kept.Add(lastFoot);
            hole = kept;
            if (hole.Count < 3) return;
        }

        // A corner that close to the wall's top, or to the rim top it stands on, is on it. Where
        // the outline crosses the top becomes a corner of its own, exactly at the top, so within
        // a strip no edge crosses it.
        for (int i = 0; i < hole.Count; i++)
        {
            if (Mathf.Abs(hole[i].y - height) < Apart) hole[i] = new Vector2(hole[i].x, height);
            else if (hole[i].y < Apart)              hole[i] = new Vector2(hole[i].x, 0f);
        }
        var cut = new List<Vector2>(hole.Count + 4);
        for (int i = 0; i < hole.Count; i++)
        {
            Vector2 a = hole[i], c = hole[(i + 1) % hole.Count];
            cut.Add(a);
            if ((a.y - height) * (c.y - height) < 0f)
                cut.Add(new Vector2(Mathf.Lerp(a.x, c.x, (height - a.y) / (c.y - a.y)), height));
        }
        hole = cut;
        int nh = hole.Count;

        // Every station across the mouth: where anything changes, and then enough more between
        // them for the faces to bend round the circle — kept clear of the others, so no strip
        // comes out a sliver.
        var xs = new List<float> { -reach, reach };
        foreach (var a in hole)
            if (a.x > -reach && a.x < reach) xs.Add(a.x);
        xs.Sort();
        var stations = new List<float>();
        foreach (float x in xs)
            if (stations.Count == 0 || x - stations[stations.Count - 1] > Merge) stations.Add(x);
        if (stations.Count < 2) return;
        if (reach - stations[stations.Count - 1] > 0f) stations[stations.Count - 1] = reach;   // the end stays put

        // A keyhole corner merged into a station sits on it exactly.
        for (int i = 0; i < nh; i++)
        {
            float best = float.MaxValue, at = hole[i].x;
            foreach (float x in stations)
                if (Mathf.Abs(hole[i].x - x) < best) { best = Mathf.Abs(hole[i].x - x); at = x; }
            if (best <= Merge * 1.0001f) hole[i] = new Vector2(at, hole[i].y);
        }

        var fill = new List<float>();
        for (int i = 0; i + 1 < stations.Count; i++)
        {
            float x0 = stations[i], x1 = stations[i + 1];
            int   n  = Mathf.CeilToInt((x1 - x0) / edge - 0.25f);
            for (int k = 1; k < n; k++) fill.Add(Mathf.Lerp(x0, x1, (float)k / n));
        }
        stations.AddRange(fill);
        stations.Sort();

        // Exactly the corner's own height at its own station, so neighbouring strips share it.
        float EdgeY(Vector2 a, Vector2 c, float x)
        {
            if (x == a.x) return a.y;
            if (x == c.x) return c.y;
            return Mathf.Lerp(a.y, c.y, (x - a.x) / (c.x - a.x));
        }

        // On the wall's river-side circle (front) or its far one; the foot sits on the rim's surface.
        Vector3 At(float x, float y, bool front)
        {
            float r  = front ? rIn : rOut;
            float k  = x + offset;
            Vector3 at = right * k + dir * Mathf.Sqrt(Mathf.Max(0f, r * r - k * k));
            at.y = y <= 1e-6f ? -(front ? fallIn : fallOut) : y;
            return at;
        }

        float Kind(System.Func<RiverRunShadingSettings, StoneFaceKind> pick) =>
            shading != null ? (float)pick(shading) : 0f;

        void Style(DoorwayBound bound)
        {
            b.NextDesignation = bound == DoorwayBound.Keyhole ? StoneDesignation.PoolDoorways
                                                              : StoneDesignation.RimWalls;
            b.NextLabel = bound == DoorwayBound.Foot    ? null
                        : bound == DoorwayBound.Keyhole ? "Doorway" : "Rim Wall";
            b.NextPart  = bound == DoorwayBound.Foot    ? RimWallBuriedPart
                        : bound == DoorwayBound.Keyhole ? RimWallDoorPart : RimWallTopPart;
            b.NextKind  = bound == DoorwayBound.Foot    ? 0f
                        : bound == DoorwayBound.Keyhole ? Kind(s => s.poolDoorway)
                                                        : Kind(s => s.rimWallTop);
        }

        // The solid in each strip, as stacked pieces between a lower and an upper line.
        int strips = stations.Count - 1;
        var solid  = new List<(DoorwayLine lo, DoorwayLine hi)>[strips];
        for (int s = 0; s < strips; s++)
        {
            float xa = stations[s], xb = stations[s + 1];

            var crossing = new List<DoorwayLine>();
            for (int i = 0; i < nh; i++)
            {
                Vector2 a = hole[i], c = hole[(i + 1) % nh];
                if (Mathf.Abs(c.x - a.x) < 1e-9f) continue;
                if (Mathf.Min(a.x, c.x) > xa + 1e-7f || Mathf.Max(a.x, c.x) < xb - 1e-7f) continue;
                crossing.Add(new DoorwayLine { bound = DoorwayBound.Keyhole,
                                               ya = EdgeY(a, c, xa), yb = EdgeY(a, c, xb) });
            }
            crossing.Sort((u, v) => u.Mid.CompareTo(v.Mid));

            var pieces = new List<(DoorwayLine lo, DoorwayLine hi)>
            {
                (new DoorwayLine { bound = DoorwayBound.Foot, ya = 0f,     yb = 0f },
                 new DoorwayLine { bound = DoorwayBound.Top,  ya = height, yb = height }),
            };

            // Inside the keyhole between each pair of its edges, taken out of the wall.
            for (int i = 0; i + 1 < crossing.Count; i += 2)
            {
                var hl = crossing[i];
                var hh = crossing[i + 1];
                var kept = new List<(DoorwayLine lo, DoorwayLine hi)>();
                foreach (var (lo, hi) in pieces)
                {
                    if (hh.Mid <= lo.Mid || hl.Mid >= hi.Mid) { kept.Add((lo, hi)); continue; }
                    if (hl.Mid > lo.Mid) kept.Add((lo, hl));
                    if (hh.Mid < hi.Mid) kept.Add((hh, hi));
                }
                pieces = kept;
            }

            // A wedge thinner than this at both sides — where the head just crosses the wall's
            // top — is left out: MeshBuild would drop its faces anyway, and the upright faces at
            // the stations close over it, being worked out from what is kept.
            pieces.RemoveAll(q => q.hi.ya - q.lo.ya < 2f * Merge && q.hi.yb - q.lo.yb < 2f * Merge);
            solid[s] = pieces;
        }

        // Where the strip beside has a corner partway up this one's side, this one's face takes
        // that corner too, so the two meet edge to edge rather than at a T.
        List<float> Between(List<(DoorwayLine lo, DoorwayLine hi)> beside, bool theirEnd, float y0, float y1)
        {
            var ys = new List<float>();
            if (beside == null) return ys;
            foreach (var (lo, hi) in beside)
                foreach (float y in new[] { theirEnd ? lo.yb : lo.ya, theirEnd ? hi.yb : hi.ya })
                    if (y > y0 + 1e-6f && y < y1 - 1e-6f && !ys.Contains(y)) ys.Add(y);
            ys.Sort();
            return ys;
        }

        for (int s = 0; s < strips; s++)
        {
            float xa = stations[s], xb = stations[s + 1];

            Vector3 radial = At((xa + xb) * 0.5f, 1f, true);
            radial.y = 0f;
            radial.Normalize();

            foreach (var (lo, hi) in solid[s])
            {
                // The two faces, on the wall's two circles, with every corner either side has.
                var up   = Between(s + 1 < strips ? solid[s + 1] : null, false, lo.yb, hi.yb);
                var down = Between(s > 0 ? solid[s - 1] : null, true, lo.ya, hi.ya);

                foreach (bool front in new[] { true, false })
                {
                    var leftSide  = new List<float> { lo.ya };
                    leftSide.AddRange(down);
                    leftSide.Add(hi.ya);
                    var rightSide = new List<float> { lo.yb };
                    rightSide.AddRange(up);
                    rightSide.Add(hi.yb);

                    b.NextDesignation = StoneDesignation.RimWalls;
                    b.NextLabel = "Rim Wall";
                    b.NextPart  = RimWallFacePart;
                    b.NextKind  = front ? Kind(s2 => s2.rimWallRiverFace) : Kind(s2 => s2.rimWallOuterFace);
                    DoorwayStripFace(b, leftSide.ConvertAll(y => At(xa, y, front)),
                                        rightSide.ConvertAll(y => At(xb, y, front)),
                                        front ? -radial : radial);
                }

                // Under it and over it, through the wall's thickness.
                float dx = xb - xa;
                Style(lo.bound);
                DoorwayFace(b, At(xa, lo.ya, true), At(xb, lo.yb, true),
                               At(xb, lo.yb, false), At(xa, lo.ya, false),
                               right * (lo.yb - lo.ya) - Vector3.up * dx);
                Style(hi.bound);
                DoorwayFace(b, At(xa, hi.ya, true), At(xb, hi.yb, true),
                               At(xb, hi.yb, false), At(xa, hi.ya, false),
                               -right * (hi.yb - hi.ya) + Vector3.up * dx);
            }
        }

        // Upright faces at the stations, wherever the solid on one side does not carry on to the
        // other — the two ends, landing on the wall's own ends, and any upright keyhole edge.
        for (int i = 0; i < stations.Count; i++)
        {
            float x = stations[i];
            var left  = i > 0      ? solid[i - 1] : null;
            var rightS = i < strips ? solid[i]     : null;

            var ys = new List<float>();
            if (left  != null) foreach (var (lo, hi) in left)   { ys.Add(lo.yb); ys.Add(hi.yb); }
            if (rightS != null) foreach (var (lo, hi) in rightS) { ys.Add(lo.ya); ys.Add(hi.ya); }
            ys.Sort();

            bool In(List<(DoorwayLine lo, DoorwayLine hi)> pieces, float y, bool atEnd)
            {
                if (pieces == null) return false;
                foreach (var (lo, hi) in pieces)
                {
                    float y0 = atEnd ? lo.yb : lo.ya, y1 = atEnd ? hi.yb : hi.ya;
                    if (y > y0 && y < y1) return true;
                }
                return false;
            }

            bool end = i == 0 || i == strips;
            for (int k = 0; k + 1 < ys.Count; k++)
            {
                float u = ys[k], v = ys[k + 1];
                if (v - u < 1e-6f) continue;
                float m = (u + v) * 0.5f;
                bool inL = In(left, m, true), inR = In(rightS, m, false);
                if (inL == inR) continue;

                if (end)
                {
                    b.NextDesignation = StoneDesignation.RimWalls;
                    b.NextLabel = "Rim Wall";
                    b.NextPart  = RimWallFacePart;
                    b.NextKind  = Kind(s => s.rimWallEnds);
                }
                else Style(DoorwayBound.Keyhole);

                DoorwayFace(b, At(x, u, true), At(x, v, true), At(x, v, false), At(x, u, false),
                            inL ? right : -right);
            }
        }

        b.NextDesignation = StoneDesignation.RimWalls;
        b.NextLabel = "Rim Wall";
        b.NextPart  = 0;
        b.NextKind  = 0f;
    }

    // A four-cornered face of a doorway, any two corners of which may meet — the strips narrow to
    // a point where a keyhole edge touches another line.
    private static void DoorwayFace(MeshBuild b, Vector3 a, Vector3 c, Vector3 d, Vector3 e, Vector3 facing)
        => DoorwayFace(b, new List<Vector3> { a, c, d, e }, facing);

    // The same for a face with corners partway along its sides, which lie on those sides' lines.
    private static void DoorwayFace(MeshBuild b, List<Vector3> corners, Vector3 facing)
    {
        var poly = new List<Vector3>(corners.Count);
        foreach (var q in corners)
            if (poly.Count == 0 || (poly[poly.Count - 1] - q).sqrMagnitude > 1e-12f) poly.Add(q);
        if (poly.Count > 1 && (poly[0] - poly[poly.Count - 1]).sqrMagnitude <= 1e-12f)
            poly.RemoveAt(poly.Count - 1);
        if (poly.Count >= 3) EmitFacingConvex(b, poly, facing);
    }

    // A strip's face between its two upright sides, each walked foot to top with every corner
    // on it — zipped up from the bottom, always stepping the side whose next corner is lower, so
    // corners partway up either side are met edge to edge and no triangle lies flat along a side.
    private static void DoorwayStripFace(MeshBuild b, List<Vector3> left, List<Vector3> right, Vector3 facing)
    {
        void Tri(Vector3 p, Vector3 q, Vector3 r)
        {
            if (Vector3.Cross(q - p, r - p).sqrMagnitude > 1e-14f) FacingTri(b, p, q, r, facing);
        }

        int i = 0, j = 0;
        while (i < left.Count - 1 || j < right.Count - 1)
        {
            bool stepLeft = j >= right.Count - 1 ||
                            (i < left.Count - 1 && left[i + 1].y <= right[j + 1].y);
            if (stepLeft) { Tri(left[i], left[i + 1], right[j]);  i++; }
            else          { Tri(left[i], right[j + 1], right[j]); j++; }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // THE BOX
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// One closed wall along its cross lines: the top, the face on the river's side, the face
    /// away from it, the foot standing on the rim and — unless it goes right round in a
    /// <paramref name="loop"/> — its two ends.
    /// </summary>
    private static void BuildRimWallBox(MeshBuild b, List<RimWallStation> st, bool loop,
                                        RiverRunShadingSettings shading)
    {
        int n = st.Count;
        if (n < 2) return;

        string label = b.NextLabel;   // the foot is named nothing, so the name sits on what shows

        float Kind(System.Func<RiverRunShadingSettings, StoneFaceKind> pick) =>
            shading != null ? (float)pick(shading) : 0f;

        var iT = new Vector3[n]; var oT = new Vector3[n];
        var iF = new Vector3[n]; var oF = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            iF[k] = st[k].inner;
            oF[k] = st[k].outer;
            iT[k] = new Vector3(iF[k].x, st[k].top, iF[k].z);
            oT[k] = new Vector3(oF[k].x, st[k].top, oF[k].z);
        }

        int spans = loop ? n : n - 1;
        for (int k = 0; k < spans; k++)
        {
            int k2 = (k + 1) % n;

            Vector3 out3 = (oF[k] + oF[k2]) - (iF[k] + iF[k2]);
            out3.y = 0f;
            out3 = out3.sqrMagnitude < 1e-10f ? Vector3.right : out3.normalized;

            b.NextLabel = label;
            b.NextPart  = RimWallTopPart;
            b.NextKind  = Kind(s => s.rimWallTop);
            FacingQuad(b, iT[k], oT[k], oT[k2], iT[k2], Vector3.up);

            b.NextPart = RimWallFacePart;
            b.NextKind = Kind(s => s.rimWallRiverFace);
            FacingQuad(b, iF[k], iF[k2], iT[k2], iT[k], -out3);

            b.NextKind = Kind(s => s.rimWallOuterFace);
            FacingQuad(b, oF[k], oF[k2], oT[k2], oT[k], out3);

            b.NextPart  = RimWallBuriedPart;
            b.NextKind  = 0f;
            b.NextLabel = null;
            FacingQuad(b, iF[k], oF[k], oF[k2], iF[k2], Vector3.down);
        }

        if (!loop)
        {
            b.NextLabel = label;
            b.NextPart  = RimWallFacePart;
            b.NextKind  = Kind(s => s.rimWallEnds);
            foreach (int k in new[] { 0, n - 1 })
            {
                int     next  = k == 0 ? 1 : n - 2;
                Vector3 along = (iF[k] + oF[k]) - (iF[next] + oF[next]);
                along.y = 0f;
                EmitFacingConvex(b, new List<Vector3> { iT[k], oT[k], oF[k], iF[k] }, along);
            }
        }

        b.NextLabel = label;
        b.NextPart  = 0;
        b.NextKind  = 0f;
    }
}
