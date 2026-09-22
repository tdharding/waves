using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the river run geometry from a <see cref="RiverProfile"/>.
///
/// A run is the cross-section swept along the path its spline traces — one unbroken mesh
/// per river, including straight through its junctions. A branch does not get a piece of
/// its own: it is cut into the run it meets as a <see cref="RiverNotch"/>, a channel of the
/// branch's own width and depth carved down through the rim. The branch's own run then
/// simply stops against that wall.
///
/// Meshes are built in the segment's local space. The rim top is y = 0 and the run hangs
/// below the path by <see cref="RiverProfile.depth"/>; the rim is held flat to world up
/// along the whole sweep, so the run never rolls with the curve.
/// </summary>
public static partial class RiverMeshBuilder
{
    // Fewest segments across the half-ellipse channel floor, however coarse the detail is
    // set — a narrow river still has to read as a curve rather than a V.
    private const int MinChannelSegments = 6;

    private const float TwoPi = Mathf.PI * 2f;

    // Stands in the water surface's edge data wherever there is no edge on that side at all —
    // across a junction mouth, out into a pool, or in the middle of an open bowl. Far enough
    // out that nothing drawn along an edge reaches it, and a plain number rather than infinity
    // so it interpolates across a face like any other distance.
    private const float NoEdge = 1000f;

    // Which of the two waters a surface is, left in the last channel of its frame so one shader
    // can draw a river's bank lines and a pool's rings without being told which it is looking at.
    // Water generated before there was a frame to bake carries no channel at all, which reads as
    // 0 — the shader takes that as "no frame" and falls back to what it drew before, rather than
    // reading a river's numbers off a pool.
    private const float RiverKind = 1f;
    private const float PoolKind  = 2f;

    // The first two points of a cross-section are the underside corners; everything
    // after them is top surface, and so can be cut into by a notch.
    private const int TopProfileStart = 2;

    // How sharply the surface has to turn for the fold to count as a SEAM — one of the lines
    // the run is shaded along. The channel floor is a half-ellipse cut into flat facets, and
    // at the coarsest detail the section allows those facets turn about 35 degrees into one
    // another at their steepest; the corners that matter — rim top into channel, rim top into
    // outer wall, wall into underside — all turn 60 degrees or more. Sitting the line between
    // the two keeps the shading on the real corners and off the channel's own faceting.
    private const float SeamAngle = 45f;

    // How level a face has to be to count as part of the flat lip. The rim is built dead flat,
    // so anything at all generous is enough; this is tight so that the near-flat facets at the
    // very bottom of a finely cut channel are not mistaken for it.
    private const float LevelFace = 0.9999f;

    // How much nearer a seam has to be before it beats another one outright. Two seams meeting
    // at a corner are both at nothing from it and the arithmetic that placed them took different
    // routes, so they answer a hair apart; inside this they count as level and the face is free
    // to keep whichever of them it has the better use for.
    private const float SeamTie = 1e-5f;

    // Stands in for "this side of the face is not a seam" in the shading data. Far enough out
    // that no authored extent reaches it, and a plain number rather than infinity so it
    // interpolates across a face like any other distance — the same trick NoEdge plays on the
    // water.
    private const float NoSeam = 1000f;

    // What UV2.x tells the run shader a face is, so the stone can be given a colour of its own
    // for each part of a run: the OUTER faces the piece shows the world — the outer walls, the
    // underside, the open ends — the RIM lip laid flat round the top, and the INNER faces of the
    // channel the water runs down. A mesh built before any of this carries no UV2 at all, which
    // reads as 0, and the shader takes that as "no kind" and leaves the stone the colour the
    // graph handed it.
    //
    // Worked out here rather than in the shader for the same reason the seams are: a steep
    // channel wall and an outer wall are both very nearly vertical, and telling them apart needs
    // to know which side of the piece a face is on, which is only known where it was built.
    private const float FaceOuter = 1f;
    private const float FaceRim   = 2f;
    private const float FaceInner = 3f;

    // Left in UV2.w to say that the kind in UV2.x is a real one — see where it is written.
    private const float FaceMark = 1f;

    // How far a face has to lean off vertical before it counts as looking upward. The channel is
    // the only part of a run that does — the outer walls and the end caps are built exactly
    // vertical, and the underside faces straight down — so this only has to clear the arithmetic
    // rather than settle how steep is steep.
    private const float UpwardFace = 0.001f;

    // How far below the rim top a face may sit and still be the flat lip, over and above the
    // joint groove it is allowed to be dipped by. The groove is the only reason that band is not
    // zero; the whisker on top of it is for a rim riding a climbing sweep, where a corner reads
    // its height off the nearest centre rather than off its own.
    private const float RimWhisker = 0.005f;

    /// <summary>
    /// Where a branch meets a run: the branch's whole section, arriving on this run's
    /// centreline at <see cref="centre"/> and heading away along <see cref="direction"/>.
    /// Both are in the run mesh's local space.
    ///
    /// A mouth is not stamped into the run's surface. The run gives up whole cells over the
    /// stretch the branch covers and a patch carries the branch's section across them, so
    /// the branch's rim runs on into the run's rim and the two outer edges meet at a corner
    /// rather than one of them stopping against a wall.
    /// </summary>
    public struct RiverNotch
    {
        public Vector3 centre;
        public Vector3 direction;
        public float   innerWidth;
        public float   riverDepth;

        /// <summary>The branch's own shape. Its rim is what runs on across the run's rim, so
        /// a mouth needs the whole section and not just the channel it opens.</summary>
        public RiverProfile profile;

        /// <summary>How far out along <see cref="direction"/> the branch's own run stops —
        /// where the mouth patch picks its section up. Falls back to the width the two
        /// sections work out to when it is left at zero.</summary>
        public float collar;
    }

    // ══════════════════════════════════════════════════════════════
    // RUN
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Sweeps the cross-section through <paramref name="centres"/>, each ring turned to face
    /// its entry in <paramref name="forwards"/>. All lists are in the mesh's own local space.
    ///
    /// A branch is not stamped into the surface. The run gives up whole cells over the stretch
    /// each of its <paramref name="notches"/> covers — from the rim it opens through in to the
    /// centreline — and <see cref="BuildRunMouthPatch"/> carries the branch's own section
    /// across them. So the branch's rim runs on into this one and their two outer edges meet
    /// at a corner that is a real edge of the mesh, rather than one stopping against the other.
    ///
    /// <paramref name="edge"/> is the target length of an edge anywhere in the mesh — the same
    /// number a pool is built from, so a run and the pool it meets carry the same density.
    /// An end that runs into a pool, or out of the river it branches off, is left uncapped:
    /// the patch there carries the section on, and a cap would be a wall across the middle.
    /// </summary>
    public static Mesh BuildRun(
        RiverProfile profile, IList<Vector3> centres, IList<Vector3> forwards,
        IList<RiverNotch> notches, float edge, bool capStart = true, bool capEnd = true)
    {
        var b = new MeshBuild();
        if (profile == null || centres == null || forwards == null || centres.Count < 2)
            return b.ToMesh("RiverRun");

        edge = Mathf.Max(edge, 0.005f);

        var loop = BuildCrossSectionLoop(profile, edge);
        var grid = new RunGrid(profile, centres, forwards, edge);
        if (grid.Rings < 2) return b.ToMesh("RiverRun");

        // The section is the same all the way along now that nothing is stamped into it, so
        // every ring is that one outline and the caps are triangulated from it.
        var ring = new Vector3[grid.Rings][];
        for (int j = 0; j < grid.Rings; j++)
        {
            // An end left uncapped is an end a patch carries on from — a joint. The two sides
            // stand on the very same ring, but they are separate meshes with their own normals
            // and their own planar UVs, so flush they read as a crease rather than as a join.
            // Dipping the shared ring's top face, which the patch dips its matching row to
            // match, turns that line into a groove both pieces run down into: the river reads
            // as blocks laid end to end rather than as one surface that failed to be seamless.
            float dip = profile.jointGroove > 0f &&
                        ((j == 0 && !capStart) || (j == grid.Rings - 1 && !capEnd))
                      ? profile.jointGroove
                      : 0f;

            ring[j] = new Vector3[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                // The loop opens on its two underside corners, so everything from 2 on is the
                // top surface — the only part a joint shows in.
                float y = loop[i].y - (i >= 2 ? dip : 0f);
                ring[j][i] = grid.centre[j] + grid.right[j] * loop[i].x + Vector3.up * y;
            }
        }

        var mouths = ResolveRunMouths(profile, notches, grid, edge);

        // Walls — one flat quad per cross-section edge per step along the sweep. A cell a mouth
        // runs through is cut along the mouth's own side lines, so the rings stop exactly on
        // them and the patch picks up from there.
        for (int j = 0; j < grid.Rings - 1; j++)
        for (int i = 0; i < loop.Count; i++)
        {
            int i2 = (i + 1) % loop.Count;
            if (CutByMouth(b, mouths, grid, loop.Count, i, j,
                           ring[j][i], ring[j][i2], ring[j + 1][i2], ring[j + 1][i])) continue;

            b.Quad(ring[j][i], ring[j][i2], ring[j + 1][i2], ring[j + 1][i]);
        }

        // Caps at each end of the run.
        if (capEnd)   CapRun(b, ring[grid.Rings - 1], loop, false);
        if (capStart) CapRun(b, ring[0],              loop, true);

        foreach (var mouth in mouths) BuildRunMouthPatch(b, mouth, grid, edge);

        // The run's rim rides the sweep, so the shading is measured off the centres it was
        // swept through rather than off the world — a river that climbs takes its waterline
        // up with it.
        //
        // The deepest joint groove anywhere in the piece goes with it: a groove dips the rim,
        // and a rim dipped further than the lip is allowed to fall would stop reading as rim.
        // A run carries its branches' grooves as well as its own, because a mouth patch dips
        // its row to whatever the branch arriving asked for.
        float groove = profile.jointGroove;
        foreach (var mouth in mouths)
            if (mouth.branch != null) groove = Mathf.Max(groove, mouth.branch.jointGroove);

        b.ShadeSeams(grid.centre, groove);

        return b.ToMesh("RiverRun");
    }

    private static void CapRun(MeshBuild b, Vector3[] ring, List<Vector2> shape, bool reversed)
    {
        var tris = Triangulate(shape);
        for (int i = 0; i < tris.Count; i += 3)
        {
            int a = tris[i], c = tris[i + 1], d = tris[i + 2];
            if (reversed) b.Tri(ring[d], ring[c], ring[a]);
            else          b.Tri(ring[a], ring[c], ring[d]);
        }
    }


    /// <summary>
    /// Closed cross-section outline in XY (x across, y up), counter-clockwise, starting at
    /// the two underside corners so everything after them is top surface. Rim top is y = 0.
    /// </summary>
    private static List<Vector2> BuildCrossSectionLoop(RiverProfile profile, float edge)
    {
        float halfOuter = profile.OuterWidth * 0.5f;
        float halfInner = profile.innerWidth * 0.5f;

        var loop = new List<Vector2>();
        loop.Add(new Vector2(-halfOuter, -profile.depth));
        loop.Add(new Vector2( halfOuter, -profile.depth));

        foreach (float x in TopColumns(profile, edge))
            loop.Add(new Vector2(x, -ChannelFloor(Mathf.Abs(x), halfInner, profile.riverDepth)));

        return loop;
    }

    /// <summary>
    /// Where the top surface is cut across the width, right rim inwards, over the channel and
    /// out across the left rim. The rims are stepped too, so a mouth cuts cleanly through them.
    ///
    /// The count comes off <paramref name="edge"/> alone, and a pool sizes its mouths by the
    /// same call — so the lines of a river's section have somewhere to run when it meets one.
    /// </summary>
    public static List<float> TopColumns(RiverProfile profile, float edge)
    {
        var xs = new List<float>();
        if (profile == null) return xs;

        float halfOuter = profile.OuterWidth * 0.5f;
        float halfInner = profile.innerWidth * 0.5f;
        edge = Mathf.Max(edge, 0.001f);

        // Always an even number of steps, so the centreline is a line of the section rather
        // than something that falls between two of them. A branch arriving runs its channel
        // all the way in to that line, and has to have vertices there to land on.
        int channelSteps = Mathf.Max(MinChannelSegments,
                                     Mathf.CeilToInt(profile.innerWidth / edge));
        if ((channelSteps & 1) != 0) channelSteps++;

        int rimSteps     = profile.rimWidth <= 0.0001f
                         ? 0
                         : Mathf.Max(1, Mathf.CeilToInt(profile.rimWidth / edge));

        for (int i = 0; i <= rimSteps; i++)
            xs.Add(rimSteps == 0 ? halfOuter
                                 : Mathf.Lerp(halfOuter, halfInner, (float)i / rimSteps));
        for (int i = 1; i < channelSteps; i++)
            xs.Add(halfInner - profile.innerWidth * i / channelSteps);
        for (int i = 0; i <= rimSteps; i++)
            xs.Add(rimSteps == 0 ? -halfOuter
                                 : Mathf.Lerp(-halfInner, -halfOuter, (float)i / rimSteps));

        return xs;
    }

    // Half-ellipse: full depth on the centreline, flush with the rim at the channel edge.
    private static float ChannelFloor(float across, float halfInner, float riverDepth)
    {
        if (halfInner <= 0f || across >= halfInner) return 0f;
        float k = across / halfInner;
        return riverDepth * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
    }

    // ══════════════════════════════════════════════════════════════
    // WATER
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Sweeps the water surface along the same centres the run was swept along: a flat
    /// ribbon the full inner width, held <paramref name="waterLevel"/> below the rim top.
    /// Sunk water buries its own edges in the channel wall, so there is never a seam at the
    /// waterline.
    ///
    /// The centres are the rim top of the run, so a run that climbs or drops carries its
    /// water with it — the surface keeps the same distance from the rim the whole way.
    ///
    /// A branch passes the river it leaves as <paramref name="join"/>, and its water carries
    /// that river's banks on UV3 wherever it lies near it — see <see cref="RiverJoin"/>.
    /// </summary>
    public static Mesh BuildWater(
        RiverProfile profile, float waterLevel,
        IList<Vector3> centres, IList<Vector3> forwards,
        RiverJoin? join = null)
    {
        var b = new MeshBuild();
        if (centres == null || forwards == null || centres.Count < 2) return b.ToMesh("RiverWater");

        int rings = Mathf.Min(centres.Count, forwards.Count);
        if (rings < 2) return b.ToMesh("RiverWater");

        float half = profile.innerWidth * 0.5f;
        float y    = -waterLevel;

        // Where the channel wall comes up through the surface. The ribbon is the full inner
        // width and buries its own edges in that wall, so the line the water is actually seen
        // to end on is this far in from the edge it is built to — and that line, not the mesh
        // edge, is what the surface reads to draw ripples along its banks.
        float shore = WaterHalfWidth(profile, waterLevel);

        var left  = new Vector3[rings];
        var right = new Vector3[rings];
        var along = new float[rings];

        for (int j = 0; j < rings; j++)
        {
            Vector3 fwd = forwards[j];
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
            fwd.Normalize();

            // Flat to world up, exactly as the run's rim is, so the two never disagree.
            Vector3 across = Vector3.Cross(Vector3.up, fwd);
            left[j]  = centres[j] - across * half + Vector3.up * y;
            right[j] = centres[j] + across * half + Vector3.up * y;

            if (j > 0) along[j] = along[j - 1] + Vector3.Distance(centres[j - 1], centres[j]);
        }

        // The river a branch leaves. A ring still lying within the joined river's own width of
        // its near bank carries that river's banks; the first ring past that, and every ring after
        // it, carries none. Decided per ring pair rather than per corner, so no face is left
        // half-flagged and interpolating a blend that fades out across it.
        bool joining = join.HasValue;
        DebugJoinQuads = 0; DebugJoinAlong = 0f; DebugJoinMaxIn = float.NegativeInfinity;
        DebugWaterLength = along[rings - 1];
        for (int j = 0; j < rings - 1; j++)
        {
            if (joining)
            {
                var jn = join.Value;
                joining = jn.Near(left[j]) || jn.Near(right[j]) ||
                          jn.Near(left[j + 1]) || jn.Near(right[j + 1]);
                b.BankAt = joining ? jn.BankAt : null;

                // DEBUG (branch mouth fade at pools): where the flagged stretch reaches, and the
                // most any flagged corner lies INSIDE the joined river's water.
                if (joining)
                {
                    DebugJoinQuads = j + 1;
                    DebugJoinAlong = along[j + 1];
                    DebugJoinMaxIn = Mathf.Max(DebugJoinMaxIn,
                        Mathf.Max(jn.BankAt(left[j + 1]).x, jn.BankAt(right[j + 1]).x));
                }
            }

            // The lines run the whole length of the ribbon — across a junction mouth and over a
            // pool's rim — with nothing easing them off at either end.
            b.Quad(left[j], left[j + 1], right[j + 1], right[j],
                   RunEdgeData(shore, -half, along[j]),
                   RunEdgeData(shore, -half, along[j + 1]),
                   RunEdgeData(shore,  half, along[j + 1]),
                   RunEdgeData(shore,  half, along[j]),
                   RunFlowData(-half, along[j]),
                   RunFlowData(-half, along[j + 1]),
                   RunFlowData( half, along[j + 1]),
                   RunFlowData( half, along[j]));
        }

        b.BankAt = null;

        return b.ToMesh("RiverWater");
    }

    /// <summary>
    /// The river a branch leaves, as its water sees it: a straight line across the junction.
    ///
    /// A branch's water runs back across the mouth to that river's centreline and lies over it,
    /// drawn on top. Where it does, its lines should belong to THAT river's banks — held along
    /// them, gone from its middle — rather than carrying the branch's own banks straight out
    /// across it. So every corner near the junction carries, on UV3, how far it lies from each of
    /// that river's waterlines, which the shader turns into that river's Reach and eases the
    /// branch's own over to.
    ///
    /// The river is taken as straight across the junction: its tangent there. Both distances are
    /// straight lines, so they interpolate exactly across a face.
    /// </summary>
    // DEBUG (branch mouth fade at pools) — read by the designer straight after BuildWater.
    public static int   DebugJoinQuads;
    public static float DebugJoinAlong, DebugJoinMaxIn, DebugWaterLength;

    public struct RiverJoin
    {
        /// <summary>A point on that river's centreline, in the water's own space.</summary>
        public Vector3 point;

        /// <summary>Flat unit direction across that river, facing out toward the branch.</summary>
        public Vector3 across;

        /// <summary>Half the width of that river's water.</summary>
        public float shore;

        /// <summary>How far out past its near waterline, into the branch, the banks are still
        /// carried. That river's outer width: past it the branch is its own water.</summary>
        public float extent;

        private float Out(Vector3 corner)
        {
            Vector3 d = corner - point;
            d.y = 0f;
            return Vector3.Dot(d, across);
        }

        public bool Near(Vector3 corner) => Out(corner) <= shore + extent;

        /// <summary>
        /// UV3 for one corner: .x metres to that river's NEAR waterline (the branch's side),
        /// positive inside its water and negative out in the branch; .y metres to its far
        /// waterline; .z 1, carrying.
        /// </summary>
        public Vector4 BankAt(Vector3 corner)
        {
            float off = Out(corner);
            return new Vector4(shore - off, shore + off, 1f, 0f);
        }
    }

    /// <summary>
    /// The frame one corner of a river's water is drawn in: which way is along it, which way is
    /// across it, and how much of it is really there.
    ///
    /// A river's lines lie along its banks, so along is metres down the channel and across is
    /// metres off the centreline. Because both are measured on the sweep itself rather than in
    /// the world, they follow the river round its bends — which is what carries the lines round
    /// a branch as it curves away from the river it left.
    ///
    /// The lap ramp is always 1: a river's water never laps over another's. Only a pool does.
    /// </summary>
    private static Vector4 RunFlowData(float across, float along)
        => new Vector4(along, across, 1f, RiverKind);

    /// <summary>
    /// What one corner of the water surface knows about the edges it lies between: how far it
    /// is from the waterline on either side, and how far along the channel it sits.
    ///
    /// Both distances are straight lines in <paramref name="across"/>, which is what lets two
    /// vertices a whole channel apart carry them — a distance to the NEARER edge would be a
    /// fold down the middle, and a ribbon two vertices wide has nowhere to put the fold. The
    /// surface takes the smaller of the two and gets the fold for nothing.
    ///
    /// Negative on the far side of a waterline, out where the wall has the water buried. That
    /// water is never seen, so nothing is spent hiding it.
    ///
    /// <c>.w</c> is the length of the lap in metres — always 0, a river never laps.
    /// </summary>
    private static Vector4 RunEdgeData(float shore, float across, float along)
        => new Vector4(shore + across, shore - across, along, 0f);

    // ══════════════════════════════════════════════════════════════
    // BANKS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Half the width of the water: where the channel wall comes up through the surface.
    ///
    /// The channel is a half-ellipse, so the water is always narrower than the inner width —
    /// how much narrower depends on how deep it is held. Water lying deeper than the channel
    /// is cut has no width at all, and water level with the rim spans the whole channel.
    /// </summary>
    public static float WaterHalfWidth(RiverProfile profile, float waterLevel)
    {
        if (profile == null) return 0f;

        float halfInner = profile.innerWidth * 0.5f;
        if (waterLevel <= 0f)                 return halfInner;
        if (waterLevel >= profile.riverDepth) return 0f;

        float k = waterLevel / profile.riverDepth;
        return halfInner * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
    }

    /// <summary>
    /// The run's banks, as the boat meets them: a wall standing on each waterline, open
    /// wherever a branch arrives so the boat can turn into it.
    ///
    /// This is collision only — nothing draws it. It is a plain wall rather than a strip of
    /// the channel's own curve because a curved wall, under a boat held at a fixed height, is
    /// something to be climbed, and a straight one is something to be stopped by. It reaches
    /// from the deepest the channel is cut up to the rim top, so the boat can neither slip
    /// under it nor ride over it, and every face is turned to look at the water — one side
    /// only, because a wall laid down both ways round pushes a boat that touches it out and
    /// back in at once, and shakes it to pieces between them.
    ///
    /// Built along the same centres, from the same grid and the same mouths as
    /// <see cref="BuildRun"/>, so its openings stand exactly where the run's do — and closed
    /// off at whichever ends that run is capped at, so a river that stops somewhere is one the
    /// boat is stopped at too.
    /// </summary>
    public static Mesh BuildRunBanks(
        RiverProfile profile, float waterLevel,
        IList<Vector3> centres, IList<Vector3> forwards,
        IList<RiverNotch> notches, float edge,
        bool capStart = true, bool capEnd = true, IList<RimNode> rims = null)
    {
        var b = new MeshBuild();
        if (profile == null || centres == null || forwards == null || centres.Count < 2)
            return b.ToMesh("RiverBanks");

        float half = WaterHalfWidth(profile, waterLevel);
        if (half <= 0.0001f) return b.ToMesh("RiverBanks");

        edge = Mathf.Max(edge, 0.005f);

        var grid = new RunGrid(profile, centres, forwards, edge);
        if (grid.Rings < 2) return b.ToMesh("RiverBanks");

        var mouths = ResolveRunMouths(profile, notches, grid, edge);

        // Resolved exactly as the rim nodes' own piece resolves them, so one that was left out
        // has no bank round it either. Quiet, because the piece has already said why.
        var rimShapes = ResolveRimShapes(profile, rims, grid, true);

        float topY = 0f;                      // the rim top
        float botY = -profile.riverDepth;     // the deepest the channel is cut

        for (int side = 0; side < 2; side++)
        {
            float x   = side == 0 ? half : -half;
            int   col = NearestColumn(grid, x);

            // Where along this bank each mouth opens it, as ring index plus the fraction of the
            // way on to the next. A mouth that can carry its branch's banks in to this one opens
            // exactly between where they meet it; one that cannot falls back to the whole rings
            // the run gave up, as before.
            var gaps = new List<Vector2>();
            foreach (var mouth in mouths)
            {
                if ((mouth.step > 0) != (side == 0)) continue;

                if (BranchBanks(b, grid, mouth, x, waterLevel, botY, topY, out Vector2 gap))
                {
                    gaps.Add(gap);
                }
                else
                {
                    RunMouthRings(mouth, grid, col, col, out int ringLo, out int ringHi);
                    gaps.Add(new Vector2(ringLo, ringHi));
                }
            }

            foreach (var shape in rimShapes)
                if ((shape.side > 0) == (side == 0))
                    RimBank(b, grid, shape, x, botY, topY, edge, gaps);

            for (int j = 0; j < grid.Rings - 1; j++)
            {
                Vector3 a = grid.centre[j]     + grid.right[j]     * x;
                Vector3 c = grid.centre[j + 1] + grid.right[j + 1] * x;

                // Looking back across the channel at the water it holds in.
                Vector3 inward = grid.right[j] * (side == 0 ? -1f : 1f);

                foreach (var piece in OutsideGaps(j, j + 1, gaps))
                    Wall(b, Vector3.Lerp(a, c, piece.x - j), Vector3.Lerp(a, c, piece.y - j),
                         botY, topY, inward);
            }
        }

        // The ends. A run left open at one end carries on into a pool or out of the river it
        // branches from, and a wall there would be a wall across open water.
        if (capStart) Cap(b, grid, 0,              1, half, botY, topY);
        if (capEnd)   Cap(b, grid, grid.Rings - 1, grid.Rings - 2, half, botY, topY);

        return b.ToMesh("RiverBanks");
    }

    /// <summary>
    /// The banks across a branch's mouth: the branch's own two waterlines, carried dead straight
    /// from where its run stops in to where each meets this run's waterline. Without them the
    /// joining piece is open water at both sides, and the boat drives straight out of it.
    ///
    /// Hands back the stretch of this run's bank between those two meeting points — the opening
    /// the branch really needs, rather than the whole outer width of it.
    /// </summary>
    private static bool BranchBanks(
        MeshBuild b, RunGrid grid, RunMouth mouth, float x, float waterLevel,
        float botY, float topY, out Vector2 gap)
    {
        gap = default;

        float half = WaterHalfWidth(mouth.branch, waterLevel);
        if (half <= 0.0001f) return false;

        // Far enough in to reach the centreline from the widest the branch can lean.
        float reach = mouth.collar + mouth.halfOuter / Mathf.Sin(MinFromAxis);

        if (!BankMeets(grid, x, mouth.centre + mouth.dir * mouth.collar + mouth.right * half,
                       -mouth.dir, reach, out float s0, out Vector3 h0)) return false;
        if (!BankMeets(grid, x, mouth.centre + mouth.dir * mouth.collar - mouth.right * half,
                       -mouth.dir, reach, out float sM, out Vector3 hM)) return false;

        // As deep as whichever of the two channels is cut deeper, so neither can be slipped under.
        float bot = Mathf.Min(botY, -mouth.branch.riverDepth);

        Wall(b, mouth.centre + mouth.dir * mouth.collar + mouth.right * half, h0,
             bot, topY, -mouth.right);
        Wall(b, mouth.centre + mouth.dir * mouth.collar - mouth.right * half, hM,
             bot, topY,  mouth.right);

        gap = new Vector2(Mathf.Min(s0, sM), Mathf.Max(s0, sM));
        return true;
    }

    /// <summary>
    /// Where a line walked in from <paramref name="from"/> first meets one of the run's waterlines
    /// — as ring index plus fraction along the run, and as the point itself.
    /// </summary>
    private static bool BankMeets(
        RunGrid grid, float x, Vector3 from, Vector3 dir, float reach,
        out float s, out Vector3 hit)
    {
        s   = 0f;
        hit = from;

        float best = float.MaxValue;
        for (int j = 0; j < grid.Rings - 1; j++)
        {
            Vector3 a = grid.centre[j]     + grid.right[j]     * x;
            Vector3 c = grid.centre[j + 1] + grid.right[j + 1] * x;

            // Flat: ray from + dir*t against segment a + (c-a)*u.
            float ex = c.x - a.x, ez = c.z - a.z;
            float den = dir.x * ez - dir.z * ex;
            if (Mathf.Abs(den) < 1e-8f) continue;

            float px = a.x - from.x, pz = a.z - from.z;
            float t  = (px * ez - pz * ex) / den;
            float u  = (px * dir.z - pz * dir.x) / den;

            if (t < 0f || t > reach || u < 0f || u > 1f || t >= best) continue;

            best = t;
            s    = j + u;
            hit  = Vector3.Lerp(a, c, u);
        }
        return best < float.MaxValue;
    }

    /// <summary>The parts of the stretch <paramref name="lo"/>–<paramref name="hi"/> that no gap
    /// covers.</summary>
    private static List<Vector2> OutsideGaps(float lo, float hi, List<Vector2> gaps)
    {
        var pieces = new List<Vector2> { new Vector2(lo, hi) };
        if (gaps == null) return pieces;

        foreach (var g in gaps)
        {
            var next = new List<Vector2>(pieces.Count + 1);
            foreach (var p in pieces)
            {
                if (g.y <= p.x || g.x >= p.y) { next.Add(p); continue; }
                if (g.x > p.x + 1e-5f) next.Add(new Vector2(p.x, g.x));
                if (g.y < p.y - 1e-5f) next.Add(new Vector2(g.y, p.y));
            }
            pieces = next;
        }
        return pieces;
    }

    /// <summary>
    /// The pool's banks: a wall round the waterline of the bowl, open where each river
    /// arrives, and a second wall round the island when one stands above the water.
    /// </summary>
    public static Mesh BuildPoolBanks(
        RiverProfile profile, float waterLevel,
        float poolRadius, float islandRadius, float floorDepth,
        IList<PoolMouth> mouths, float edge)
    {
        var b = new MeshBuild();
        if (profile == null) return b.ToMesh("RiverPoolBanks");

        poolRadius   = Mathf.Max(poolRadius, 0.01f);
        islandRadius = Mathf.Clamp(islandRadius, 0f, poolRadius - 0.01f);
        if (floorDepth <= 0f) floorDepth = profile.riverDepth;
        edge = Mathf.Max(edge, 0.005f);

        // Where the bowl comes up through the surface, coming in from the rim and going out
        // from the island. Walked rather than solved, because the island end of a roundabout
        // is the same half-ellipse read backwards.
        float outerR = WaterlineRadius(poolRadius, islandRadius, poolRadius + profile.rimWidth,
                                       floorDepth, waterLevel, true);
        float innerR = islandRadius > 0f
                     ? WaterlineRadius(poolRadius, islandRadius, poolRadius + profile.rimWidth,
                                       floorDepth, waterLevel, false)
                     : 0f;

        float outer = poolRadius + profile.rimWidth;
        var   spans = ResolveMouths(mouths, poolRadius, islandRadius, outer, edge);

        float topY = 0f;
        float botY = -floorDepth;

        int columns = Mathf.Clamp(Mathf.RoundToInt(TwoPi * poolRadius / edge), 12, 1024);

        // Each river carries its own two banks in across the rim to the bowl's waterline, and
        // the bowl is opened exactly between where they land. A river whose banks cannot reach
        // it falls back to the slice the mouth took out, as before.
        var gaps     = new List<Vector2>();   // start angle, then how far round
        var fallback = new List<MouthSpan>();
        foreach (var s in spans)
        {
            if (outerR > 0.0001f && PoolMouthBanks(b, s, outerR, waterLevel, botY, topY, out Vector2 gap))
                gaps.Add(gap);
            else
                fallback.Add(s);
        }

        for (int j = 0; j < columns; j++)
        {
            float a1  = TwoPi * j            / columns;
            float a2  = TwoPi * (j + 1)      / columns;
            float mid = TwoPi * (j + 0.5f)   / columns;

            // A mouth is a gap in the outer wall only — a river arrives across the rim, and
            // the island in the middle is never cut into.
            // The bowl's wall looks in at the water; the island's looks out at it.
            if (outerR > 0.0001f && !InAnyMouth(fallback, outerR, outer, mid))
            {
                // Each gap laid against this column twice, a turn apart, so one that wraps
                // past zero still cuts the column it spills into.
                var columnGaps = new List<Vector2>(gaps.Count * 2);
                foreach (var g in gaps)
                {
                    float from = a1 + Mathf.Repeat(g.x - a1, TwoPi);
                    columnGaps.Add(new Vector2(from,         from + g.y));
                    columnGaps.Add(new Vector2(from - TwoPi, from - TwoPi + g.y));
                }

                foreach (var piece in OutsideGaps(a1, a2, columnGaps))
                    Wall(b, PoolDirection(piece.x) * outerR, PoolDirection(piece.y) * outerR,
                         botY, topY, -PoolDirection(mid));
            }

            if (innerR > 0.0001f)
                Wall(b, PoolDirection(a1) * innerR, PoolDirection(a2) * innerR,
                     botY, topY, PoolDirection(mid));
        }

        return b.ToMesh("RiverPoolBanks");
    }

    /// <summary>
    /// A river's two banks carried in from where its run stops, across the rim, to where each
    /// meets the bowl's waterline — the pool's side of <see cref="BranchBanks"/>. Hands back
    /// the stretch of the bowl's wall between them as a start angle and how far round it runs.
    /// </summary>
    private static bool PoolMouthBanks(
        MeshBuild b, MouthSpan s, float waterlineR, float waterLevel,
        float botY, float topY, out Vector2 gap)
    {
        gap = default;

        float half = WaterHalfWidth(s.mouth.profile, waterLevel);
        if (half <= 0.0001f) return false;

        Vector3 start0 = s.mouth.centre + s.right * half;
        Vector3 startM = s.mouth.centre - s.right * half;

        if (!CircleMeets(start0, -s.dir, waterlineR, out Vector3 h0)) return false;
        if (!CircleMeets(startM, -s.dir, waterlineR, out Vector3 hM)) return false;

        float bot = Mathf.Min(botY, -s.mouth.profile.riverDepth);

        Wall(b, start0, h0, bot, topY, -s.right);
        Wall(b, startM, hM, bot, topY,  s.right);

        float a0    = Mathf.Atan2(h0.x, h0.z);
        float aM    = Mathf.Atan2(hM.x, hM.z);
        float delta = DeltaRad(a0, aM);

        gap = delta >= 0f ? new Vector2(a0, delta) : new Vector2(aM, -delta);
        return true;
    }

    /// <summary>Where a flat line walked in from <paramref name="from"/> first crosses the
    /// circle of that radius about the pool's centre.</summary>
    private static bool CircleMeets(Vector3 from, Vector3 dir, float radius, out Vector3 hit)
    {
        hit = from;

        float fd   = from.x * dir.x + from.z * dir.z;
        float ff   = from.x * from.x + from.z * from.z;
        float disc = fd * fd - (ff - radius * radius);
        if (disc < 0f) return false;

        float t = -fd - Mathf.Sqrt(disc);
        if (t < 0f) return false;

        hit = new Vector3(from.x + dir.x * t, 0f, from.z + dir.z * t);
        return true;
    }

    /// <summary>
    /// How far out the pool's floor crosses the water surface. Walked from one end of the
    /// bowl to the other in even steps, taking the first crossing found from whichever end
    /// is asked for.
    /// </summary>
    private static float WaterlineRadius(
        float poolRadius, float islandRadius, float rimRadius, float floorDepth,
        float waterLevel, bool fromRim)
    {
        const int steps = 256;

        float from = fromRim ? rimRadius : islandRadius;
        float to   = fromRim ? islandRadius : rimRadius;

        float prevR = from;
        float prevD = PoolFloor(prevR, islandRadius, poolRadius, floorDepth);

        for (int i = 1; i <= steps; i++)
        {
            float r = Mathf.Lerp(from, to, (float)i / steps);
            float d = PoolFloor(r, islandRadius, poolRadius, floorDepth);

            // Crossing from shallower than the water to deeper than it.
            if (prevD < waterLevel && d >= waterLevel)
            {
                float span = d - prevD;
                float t    = span < 1e-6f ? 0f : (waterLevel - prevD) / span;
                return Mathf.Lerp(prevR, r, t);
            }

            prevR = r;
            prevD = d;
        }

        return 0f;
    }

    /// <summary>A wall right across the channel at one end of a run, facing back down it.</summary>
    private static void Cap(MeshBuild b, RunGrid grid, int ring, int towards, float half,
                            float botY, float topY)
    {
        Vector3 left  = grid.centre[ring] - grid.right[ring] * half;
        Vector3 right = grid.centre[ring] + grid.right[ring] * half;

        Vector3 inward = grid.centre[Mathf.Clamp(towards, 0, grid.Rings - 1)] - grid.centre[ring];
        inward.y = 0f;

        Wall(b, left, right, botY, topY, inward);
    }

    /// <summary>
    /// One panel of a bank: the segment a-c stood up between two heights, wound so that it
    /// looks the way <paramref name="inward"/> points — at the water it is there to hold.
    /// </summary>
    private static void Wall(MeshBuild b, Vector3 a, Vector3 c, float botY, float topY,
                             Vector3 inward)
    {
        Vector3 a0 = new Vector3(a.x, a.y + botY, a.z);
        Vector3 a1 = new Vector3(a.x, a.y + topY, a.z);
        Vector3 c0 = new Vector3(c.x, c.y + botY, c.z);
        Vector3 c1 = new Vector3(c.x, c.y + topY, c.z);

        // Which way this winding ends up looking, measured rather than worked out, so it is
        // right whichever way round the run was swept.
        Vector3 facing = Vector3.Cross(a1 - a0, c1 - a1);

        if (Vector3.Dot(facing, inward) >= 0f) b.Quad(a0, a1, c1, c0);
        else                                   b.Quad(c0, c1, a1, a0);
    }

    /// <summary>The column line of the run's section lying nearest a given place across it —
    /// how a bank asks the mouths whether it is open where it stands.</summary>
    private static int NearestColumn(RunGrid grid, float x)
    {
        int   best     = 0;
        float bestDist = float.MaxValue;

        for (int c = 0; c <= grid.Columns; c++)
        {
            float d = Mathf.Abs(grid.xs[c] - x);
            if (d < bestDist) { bestDist = d; best = c; }
        }
        return best;
    }


    // ══════════════════════════════════════════════════════════════
    // WHERE A BRANCH MEETS A RUN
    // ══════════════════════════════════════════════════════════════

    // A branch meeting the river almost head-on has no mouth to speak of — the two
    // channels never separate — so the heading is held off the axis.
    private const float MinFromAxis = 20f * Mathf.Deg2Rad;

    /// <summary>
    /// How far out along its own heading a branch's run has to stop for the whole of its last
    /// ring to stand clear of the river it leaves — half its own outer width sheared across,
    /// not just its centreline. Stopping on the centreline alone is what left one rim corner
    /// hanging past the river with nothing under it and buried the other one in the rim.
    ///
    /// One edge further out again, so the last ring clears the wall rather than sitting on it
    /// and the patch has a collar to carry across.
    /// </summary>
    public static float RunCollar(RiverProfile main, RiverProfile branch, float branchAngleRad,
                                  float edge)
    {
        if (branch == null) branch = main;

        float angle = ClampBranchAngle(branchAngleRad);
        return CollarFor(main, branch, Mathf.Abs(Mathf.Sin(angle)), Mathf.Abs(Mathf.Cos(angle)),
                         edge);
    }

    private static float CollarFor(RiverProfile main, RiverProfile branch,
                                   float sin, float cos, float edge)
    {
        sin = Mathf.Max(sin, Mathf.Sin(MinFromAxis));
        return (main.OuterWidth * 0.5f + branch.OuterWidth * 0.5f * cos) / sin
             + Mathf.Max(edge, 0.005f);
    }

    /// <summary>
    /// How much of the main river the branch mouth takes up, measured along the river.
    /// Used as the half-gap when the spline is split for the boat's routing.
    /// </summary>
    public static float JunctionSpan(RiverProfile main, RiverProfile branch, float branchAngleRad)
    {
        if (branch == null) branch = main;

        float angle = ClampBranchAngle(branchAngleRad);
        float sin   = Mathf.Max(Mathf.Sin(Mathf.Abs(angle)), 0.001f);
        float cos   = Mathf.Abs(Mathf.Cos(angle));

        // The mouth is the branch's whole section sheared across the width of the main river.
        return (main.OuterWidth * 0.5f * cos + branch.OuterWidth * 0.5f) / sin + main.rimWidth;
    }

    private static float ClampBranchAngle(float angleRad)
    {
        float a    = Mathf.Repeat(angleRad + Mathf.PI, Mathf.PI * 2f) - Mathf.PI; // -pi..pi
        float sign = a < 0f ? -1f : 1f;
        float mag  = Mathf.Clamp(Mathf.Abs(a), MinFromAxis, Mathf.PI - MinFromAxis);
        return sign * mag;
    }

    /// <summary>
    /// The run as the mouth code reads it: a ring of cross-sections, the same columns across
    /// every one of them, and how far along the run each ring sits — so a mouth can say where
    /// along the run one of its own lines crosses one of the run's columns.
    /// </summary>
    private class RunGrid
    {
        public readonly RiverProfile profile;
        public readonly Vector3[]    centre;
        public readonly Vector3[]    right;
        public readonly float[]      arc;
        public readonly List<float>  xs;
        public readonly float[]      colY;

        public int Rings   => centre.Length;
        public int Columns => xs.Count - 1;

        public RunGrid(RiverProfile profile, IList<Vector3> centres, IList<Vector3> forwards,
                       float edge)
        {
            this.profile = profile;

            int n  = Mathf.Min(centres.Count, forwards.Count);
            centre = new Vector3[n];
            right  = new Vector3[n];
            arc    = new float[n];
            xs     = TopColumns(profile, edge);
            colY   = new float[xs.Count];

            float halfInner = profile.innerWidth * 0.5f;
            for (int c = 0; c < xs.Count; c++)
                colY[c] = -ChannelFloor(Mathf.Abs(xs[c]), halfInner, profile.riverDepth);

            for (int j = 0; j < n; j++)
            {
                Vector3 fwd = forwards[j];
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
                fwd.Normalize();

                centre[j] = centres[j];
                right[j]  = Vector3.Cross(Vector3.up, fwd);
                arc[j]    = j == 0 ? 0f
                                   : arc[j - 1] + Vector3.Distance(centres[j - 1], centres[j]);
            }
        }

        /// <summary>A vertex of the run's top surface — the same point the sweep laid down.</summary>
        public Vector3 At(int j, int col)
        {
            col = Mathf.Clamp(col, 0, Columns);
            return centre[j] + right[j] * xs[col] + Vector3.up * colY[col];
        }

        /// <summary>
        /// How far along the run one of its column lines crosses a line running across it —
        /// solved against the rings themselves, so the answer is a place the run really has.
        /// A line that never crosses is taken at whichever end it runs off.
        /// </summary>
        public float Cross(int col, Vector3 origin, Vector3 normal)
        {
            float prev = Offset(0, col, origin, normal);
            if (prev == 0f) return arc[0];

            for (int j = 1; j < Rings; j++)
            {
                float here = Offset(j, col, origin, normal);
                if (here == 0f) return arc[j];
                if ((prev < 0f) != (here < 0f))
                    return Mathf.Lerp(arc[j - 1], arc[j], prev / (prev - here));
                prev = here;
            }
            return prev < 0f ? arc[Rings - 1] : arc[0];
        }

        private float Offset(int j, int col, Vector3 origin, Vector3 normal)
        {
            Vector3 rel = At(j, col) - origin;
            return rel.x * normal.x + rel.z * normal.z;
        }

        /// <summary>The point that far along the run, on one of its column lines.</summary>
        public Vector3 PointAt(float s, int col)
        {
            int   j    = RingAt(s);
            int   k    = Mathf.Min(j + 1, Rings - 1);
            float span = arc[k] - arc[j];
            float t    = span < 1e-6f ? 0f : Mathf.Clamp01((s - arc[j]) / span);
            return Vector3.Lerp(At(j, col), At(k, col), t);
        }

        /// <summary>The last ring at or before that point along the run.</summary>
        public int RingAt(float s)
        {
            int lo = 0, hi = Mathf.Max(0, Rings - 2);
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (arc[mid] <= s) lo = mid; else hi = mid - 1;
            }
            return Mathf.Clamp(lo, 0, Mathf.Max(0, Rings - 2));
        }
    }

    /// <summary>
    /// A branch resolved against the run it opens into: which of the run's columns it crosses,
    /// where along the run each line of its section has to land, and how far out its own run
    /// stops.
    /// </summary>
    private struct RunMouth
    {
        public RiverProfile main;
        public RiverProfile branch;

        /// <summary>Run-local: where the branch leaves the run's centreline, the way it heads
        /// off, and its own cross axis. All flat, the last two unit.</summary>
        public Vector3 centre, dir, right;

        public float halfOuter;   // half the branch's outer width
        public int   columns;     // one fewer than the lines of the branch's section

        /// <summary>The run's columns the mouth runs between: the rim edge it opens through,
        /// the channel edge where its sides stop running straight, and the centreline it ends
        /// on. <see cref="step"/> is +1 or -1 — which way it reads across the section.</summary>
        public int edgeCol, channelCol, centreCol, step;

        /// <summary>How far out along <see cref="dir"/> the branch's own run stops.</summary>
        public float collar;

        /// <summary>Where along the run each line of the branch's section lands once it
        /// reaches the centreline — one per column, on rings the run really has.</summary>
        public float[] fan;
    }

    /// <summary>
    /// Works out, for each branch, how far across the run its mouth reaches and where every
    /// line of its section has to come down.
    ///
    /// The mouth ends on the run's centreline, and its lines land there on the run's own
    /// vertices — one ring per line of the branch's section. That is the one place the two
    /// have to agree exactly, and the run is sampled to hold those rings for it.
    /// </summary>
    private static List<RunMouth> ResolveRunMouths(
        RiverProfile profile, IList<RiverNotch> notches, RunGrid grid, float edge)
    {
        var mouths = new List<RunMouth>();
        if (notches == null) return mouths;

        int m = grid.Columns;

        foreach (var n in notches)
        {
            var branch = n.profile;
            if (branch == null) continue;

            Vector3 dir = n.direction;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) continue;
            dir.Normalize();

            var us   = TopColumns(branch, edge);
            int cols = us.Count - 1;

            // A run with fewer rings than the mouth has lines has nowhere to put it. Leave it
            // whole rather than take a slice out that nothing fills.
            if (cols < 2 || grid.Rings - 1 < cols) continue;

            // Which rim the branch leaves through. The run's columns read from +x to -x, so a
            // mouth on the +x side reads from column 0 inwards, and one on the -x side from
            // the far end inwards.
            int     nearRing  = grid.RingAt(NearestArc(grid, n.centre));
            Vector3 rightHere = grid.right[nearRing];
            float   side      = dir.x * rightHere.x + dir.z * rightHere.z;
            float   sin       = Mathf.Abs(side);
            if (sin < 1e-6f) continue;

            var mouth = new RunMouth
            {
                main      = profile,
                branch    = branch,
                centre    = n.centre,
                dir       = dir,
                right     = Vector3.Cross(Vector3.up, dir),
                halfOuter = branch.OuterWidth * 0.5f,
                columns   = cols,
                centreCol = m / 2,
                edgeCol   = side > 0f ? 0 : m,
                step      = side > 0f ? 1 : -1,
                collar    = n.collar > 0.0001f
                          ? n.collar
                          : CollarFor(profile, branch, sin,
                                      Mathf.Sqrt(Mathf.Max(0f, 1f - sin * sin)), edge),
            };

            mouth.channelCol = ChannelColumn(grid, mouth);

            // Where each line of the branch's section crosses the centreline, then pulled onto
            // the run's own rings — the patch's innermost row is those vertices, so the two
            // halves of the section meet along the centreline without a seam.
            mouth.fan = new float[cols + 1];
            for (int j = 0; j <= cols; j++)
                mouth.fan[j] = grid.Cross(mouth.centreCol, mouth.centre + mouth.right * us[j],
                                          mouth.right);

            bool forward = mouth.fan[cols] >= mouth.fan[0];
            int  lo      = Mathf.Clamp(
                grid.RingAt(Mathf.Min(mouth.fan[0], mouth.fan[cols])),
                0, grid.Rings - 1 - cols);

            for (int j = 0; j <= cols; j++)
                mouth.fan[j] = grid.arc[lo + (forward ? j : cols - j)];

            mouths.Add(mouth);
        }
        return mouths;
    }

    // How far along the run a point sits, judged off the nearest ring centre.
    private static float NearestArc(RunGrid grid, Vector3 at)
    {
        int   best = 0;
        float near = float.MaxValue;

        for (int j = 0; j < grid.Rings; j++)
        {
            Vector3 d = grid.centre[j] - at;
            d.y = 0f;

            float sq = d.sqrMagnitude;
            if (sq < near) { near = sq; best = j; }
        }
        return grid.arc[best];
    }

    // The column at the channel edge on a mouth's side — as far in as its sides stay the
    // branch's own straight lines, because past it the rim has ended and there is no corner
    // left to carry.
    private static int ChannelColumn(RunGrid grid, RunMouth mouth)
    {
        float halfInner = grid.profile.innerWidth * 0.5f;

        int col = mouth.edgeCol;
        while (col != mouth.centreCol && Mathf.Abs(grid.xs[col]) > halfInner + 0.0001f)
            col += mouth.step;
        return col;
    }

    // Whether one of the run's columns is inside the stretch of the section a mouth covers —
    // from the rim it opens through in to the centreline, and no further.
    private static bool InMouthColumns(RunMouth mouth, int col)
        => mouth.step > 0 ? col >= mouth.edgeCol && col <= mouth.centreCol
                          : col <= mouth.edgeCol && col >= mouth.centreCol;

    /// <summary>
    /// Where one line of a branch's section runs as it crosses one of the run's columns: the
    /// straight line the branch carries in, eased onto the run's own vertices once it is past
    /// the channel edge and there is no rim corner left to hold.
    /// </summary>
    private static float RunColumnArc(RunMouth mouth, RunGrid grid, int col, float u, float fan)
    {
        float w = RunFanWeight(mouth, col);
        if (w >= 1f) return fan;

        float straight = grid.Cross(col, mouth.centre + mouth.right * u, mouth.right);
        return w <= 0f ? straight : Mathf.Lerp(straight, fan, w);
    }

    // How far across the run a column is: 0 out at the rim, where the mouth is still the
    // branch's own straight slot, to 1 on the centreline, where it is the run's own vertices.
    private static float RunFanWeight(RunMouth mouth, int col)
    {
        int from = mouth.channelCol;
        int to   = mouth.centreCol;
        if (from == to) return 1f;

        float k = (float)(col - from) / (to - from);
        if (k <= 0f) return 0f;
        if (k >= 1f) return 1f;
        return k * k * (3f - 2f * k);   // eased, so the sides leave straight without a corner
    }

    // Where a mouth's two sides cross one of the run's columns, measured along the run.
    private static void RunMouthSides(
        RunMouth mouth, RunGrid grid, int col, out float side0, out float sideM)
    {
        side0 = RunColumnArc(mouth, grid, col,  mouth.halfOuter, mouth.fan[0]);
        sideM = RunColumnArc(mouth, grid, col, -mouth.halfOuter, mouth.fan[mouth.columns]);
    }

    /// <summary>
    /// The whole rings a mouth takes out across a band of the run's columns. Its sides are the
    /// branch's own straight lines out at the rim and the run's own vertices in at the
    /// centreline, so they are not a constant distance along the run and neither column alone
    /// is always the wider of the two: the band is taken across everywhere all four sides
    /// reach, rounded outwards. So the slice the run gives up always holds the patch that
    /// fills it.
    /// </summary>
    private static void RunMouthRings(
        RunMouth mouth, RunGrid grid, int colA, int colB, out int ringLo, out int ringHi)
    {
        RunMouthSides(mouth, grid, colA, out float a0, out float aM);
        RunMouthSides(mouth, grid, colB, out float b0, out float bM);

        float lo = Mathf.Min(Mathf.Min(a0, aM), Mathf.Min(b0, bM));
        float hi = Mathf.Max(Mathf.Max(a0, aM), Mathf.Max(b0, bM));

        ringLo = grid.RingAt(lo);
        ringHi = Mathf.Min(grid.RingAt(hi) + 1, grid.Rings - 1);
    }

    // Closer than this, two points of a cut face are the same point.
    private const float CutWeld = 1e-5f;

    /// <summary>
    /// One corner of a face of the sweep: where it is, which ring and column of the section it
    /// stands on, and whether it is down on the underside rather than up on the top surface.
    /// </summary>
    private struct FaceCorner
    {
        public Vector3 p;
        public int     ring, col;
        public bool    under;
    }

    // Which column of the section a point of the cross-section loop stands on. The loop opens
    // on its two underside corners, -x then +x, and each sits under the outermost column.
    private static int LoopColumn(int index, int columns)
        => index == 0 ? columns : index == 1 ? 0 : index - 2;

    /// <summary>
    /// Builds one face of the sweep that a mouth runs through, cut along the mouth's two side
    /// lines — the part of the face beyond each side is kept, the part between them is left
    /// for the patch. Returns false when no mouth reaches the face, so it is built whole.
    ///
    /// Within one band of columns a side is a straight line from where it crosses one column
    /// to where it crosses the next, and the patch's own edge is that same line. Each ring
    /// the side crosses gets a vertex on it (<see cref="RingCrossing"/>), and the patch puts
    /// a vertex there too, so the rim reads as rings meeting the side line — no fans, no
    /// slivers, and nothing either piece has that the other lacks.
    /// </summary>
    private static bool CutByMouth(
        MeshBuild b, List<RunMouth> mouths, RunGrid grid, int loopCount, int i, int j,
        Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3)
    {
        // The underside is never cut into — a mouth only takes the top surface and the outer
        // wall below the rim it opens through.
        if (mouths == null || mouths.Count == 0 || i == 0) return false;

        int m    = grid.Columns;
        int i2   = (i + 1) % loopCount;
        int colA = LoopColumn(i, m), colB = LoopColumn(i2, m);

        foreach (var mouth in mouths)
        {
            if (!InMouthColumns(mouth, colA) || !InMouthColumns(mouth, colB)) continue;

            RunMouthRings(mouth, grid, colA, colB, out int ringLo, out int ringHi);
            if (j < ringLo || j >= ringHi) continue;

            // The same order the whole quad is wound in.
            var face = new[]
            {
                new FaceCorner { p = c0, ring = j,     col = colA, under = i  < 2 },
                new FaceCorner { p = c1, ring = j,     col = colB, under = i2 < 2 },
                new FaceCorner { p = c2, ring = j + 1, col = colB, under = i2 < 2 },
                new FaceCorner { p = c3, ring = j + 1, col = colA, under = i  < 2 },
            };

            RunMouthSides(mouth, grid, colA, out float a0, out float aM);
            RunMouthSides(mouth, grid, colB, out float b0, out float bM);
            bool zeroIsHigh = a0 + b0 >= aM + bM;

            EmitConvex(b, KeepBeyondSide(grid, face, colA, a0, colB, b0, zeroIsHigh ? 1f : -1f));
            EmitConvex(b, KeepBeyondSide(grid, face, colA, aM, colB, bM, zeroIsHigh ? -1f : 1f));
            return true;
        }
        return false;
    }

    /// <summary>
    /// The part of a face lying beyond one of a mouth's sides — further along the run than the
    /// side when <paramref name="sign"/> is +1, less far when it is -1. The side crosses the
    /// face's column <paramref name="colA"/> at <paramref name="sideA"/> along the run and
    /// <paramref name="colB"/> at <paramref name="sideB"/>, and runs straight between the two.
    /// </summary>
    private static List<Vector3> KeepBeyondSide(
        RunGrid grid, FaceCorner[] face, int colA, float sideA, int colB, float sideB, float sign)
    {
        var kept = new List<Vector3>(6);

        for (int k = 0; k < face.Length; k++)
        {
            FaceCorner here = face[k], next = face[(k + 1) % face.Length];

            float sHere = here.col == colA ? sideA : sideB;
            float sNext = next.col == colA ? sideA : sideB;
            bool  inHere = sign * (grid.arc[here.ring] - sHere) >= 0f;
            bool  inNext = sign * (grid.arc[next.ring] - sNext) >= 0f;

            if (inHere) kept.Add(here.p);
            if (inHere == inNext) continue;

            if (here.col == next.col)
            {
                // Along a column: the side crosses it at exactly the point the patch has there.
                Vector3 p = grid.PointAt(sHere, here.col);
                if (here.under)
                    p += Vector3.up * (-grid.profile.depth - grid.colY[here.col]);
                kept.Add(p);
            }
            else
            {
                // Across a ring: where the side's straight line crosses it.
                int lo = Mathf.Min(colA, colB), hi = Mathf.Max(colA, colB);
                kept.Add(RingCrossing(grid, here.ring, lo, lo == colA ? sideA : sideB,
                                                       hi, hi == colA ? sideA : sideB));
            }
        }

        // A side passing exactly through a corner lands a crossing on top of it.
        for (int k = kept.Count - 1; k >= 0 && kept.Count > 0; k--)
        {
            int prev = (k + kept.Count - 1) % kept.Count;
            if (prev != k && (kept[k] - kept[prev]).sqrMagnitude < CutWeld * CutWeld)
                kept.RemoveAt(k);
        }
        return kept;
    }

    /// <summary>
    /// Where a mouth's side crosses one ring, between two neighbouring columns. The point is
    /// put ON the side's straight line, so it lies exactly on the patch's edge. Always asked
    /// with the lower column first, so the run and the patch get the very same numbers.
    /// </summary>
    private static Vector3 RingCrossing(
        RunGrid grid, int ring, int colLo, float sideLo, int colHi, float sideHi)
    {
        Vector3 p0 = grid.PointAt(sideLo, colLo);
        Vector3 p1 = grid.PointAt(sideHi, colHi);
        Vector3 a  = grid.At(ring, colLo);
        Vector3 c  = grid.At(ring, colHi);

        float dx = p1.x - p0.x, dz = p1.z - p0.z;
        float ex = c.x - a.x,   ez = c.z - a.z;
        float den = dx * ez - dz * ex;

        float t = Mathf.Abs(den) < 1e-12f
                ? 0.5f
                : ((a.x - p0.x) * ez - (a.z - p0.z) * ex) / den;

        return Vector3.Lerp(p0, p1, Mathf.Clamp01(t));
    }

    /// <summary>
    /// Every ring a mouth's side crosses between two neighbouring columns, in order from
    /// <paramref name="colFrom"/> to <paramref name="colTo"/> — the vertices the patch's edge
    /// needs so it matches the cut faces of the run beside it.
    /// </summary>
    private static List<Vector3> SideCrossings(
        RunGrid grid, int colFrom, float sideFrom, int colTo, float sideTo)
    {
        var pts = new List<Vector3>();

        int   lo    = Mathf.Min(colFrom, colTo), hi = Mathf.Max(colFrom, colTo);
        float sLo   = lo == colFrom ? sideFrom : sideTo;
        float sHi   = hi == colFrom ? sideFrom : sideTo;
        float aMin  = Mathf.Min(sideFrom, sideTo), aMax = Mathf.Max(sideFrom, sideTo);

        Vector3 from = grid.PointAt(sideFrom, colFrom);
        Vector3 to   = grid.PointAt(sideTo,   colTo);

        for (int j = 0; j < grid.Rings; j++)
        {
            float s = grid.arc[j];
            if (s <= aMin || s >= aMax) continue;

            Vector3 p = RingCrossing(grid, j, lo, sLo, hi, sHi);
            if ((p - from).sqrMagnitude < CutWeld * CutWeld ||
                (p - to).sqrMagnitude   < CutWeld * CutWeld) continue;
            pts.Add(p);
        }

        if (sideFrom > sideTo) pts.Reverse();
        return pts;
    }

    /// <summary>
    /// Fills a convex outline in the order it is wound. Fanned from whichever corner leaves no
    /// triangle flat — a corner with points in a straight line either side of it would, and a
    /// flat triangle is dropped and leaves its neighbours meeting at a vertex it no longer has.
    /// </summary>
    private static void EmitConvex(MeshBuild b, List<Vector3> poly)
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
                float   s  = Vector3.Cross(e1, e2).sqrMagnitude;
                clean = s > 1e-6f * e1.sqrMagnitude * e2.sqrMagnitude;
            }
            if (!clean) continue;

            for (int k = 1; k < n - 1; k++)
                b.Tri(poly[apex], poly[(apex + k) % n], poly[(apex + k + 1) % n]);
            return;
        }

        // No clean corner — fan from the middle instead, which a convex outline always allows.
        Vector3 mid = Vector3.zero;
        foreach (var p in poly) mid += p;
        mid /= n;
        for (int k = 0; k < n; k++) b.Tri(mid, poly[k], poly[(k + 1) % n]);
    }

    /// <summary>
    /// The piece that carries a branch's section across the run it leaves. One grid: its
    /// outermost row is the branch's own last ring, standing a collar clear of the run, and
    /// its innermost row lies along the run's centreline on the run's own vertices.
    ///
    /// Out across the collar and the rim, every line of the section is the branch's own
    /// straight line carried on — so the rim keeps its width and the branch's outer edge runs
    /// on to meet the run's outer edge at a corner. Only past the channel edge, down where
    /// there is no corner left to hold, does the section ease round onto the centreline.
    ///
    /// The run is cut along the patch's two outermost lines (<see cref="CutByMouth"/>), so the
    /// patch's sides are the edge of the run beside it. Wherever a ring of the run meets one
    /// of those sides, the patch carries a vertex there too.
    /// </summary>
    private static void BuildRunMouthPatch(MeshBuild b, RunMouth mouth, RunGrid grid, float edge)
    {
        var us = TopColumns(mouth.branch, edge);
        int m  = us.Count - 1;
        if (m != mouth.columns || m < 2) return;

        float halfInnerA = mouth.main.innerWidth * 0.5f;
        float halfInnerB = mouth.branch.innerWidth * 0.5f;
        float floorY     = -mouth.main.depth;

        // Rows outermost first: the branch's own last ring, then one on every column of the
        // run the mouth crosses, in as far as the centreline. A row's column is -1 for the
        // branch's own ring, which is not on the run at all.
        var rows   = new List<Vector3[]>();
        var rowCol = new List<int>();

        var row0 = new Vector3[m + 1];
        for (int j = 0; j <= m; j++)
        {
            // Dipped by the joint groove, exactly as the branch dips its own first ring.
            float cut = ChannelFloor(Mathf.Abs(us[j]), halfInnerB, mouth.branch.riverDepth);
            row0[j] = mouth.centre + mouth.dir * mouth.collar + mouth.right * us[j]
                    + Vector3.up * -(cut + mouth.branch.jointGroove);
        }
        rows.Add(row0);
        rowCol.Add(-1);

        int steps = Mathf.Abs(mouth.centreCol - mouth.edgeCol);
        for (int k = 0; k <= steps; k++)
        {
            int col = mouth.edgeCol + mouth.step * k;
            var pts = new Vector3[m + 1];

            // The branch's channel gives way exactly as the run's own takes over, so it holds
            // its depth the whole way across the rim and only lifts once there is a channel
            // under it. Stepping it off the row instead makes the water climb a hump crossing
            // the rim. A run with no channel of its own has nothing to hand over to, so there
            // it is stepped off the rows after all.
            float mainCut = ChannelFloor(Mathf.Abs(grid.xs[col]), halfInnerA,
                                         mouth.main.riverDepth);
            float v = mouth.main.riverDepth > 0.0001f
                    ? Mathf.Clamp01(mainCut / mouth.main.riverDepth)
                    : (float)(k + 1) / (steps + 1);

            for (int j = 0; j <= m; j++)
            {
                // The run's own floor, with the branch's channel fading out of it. Out at the
                // branch's last ring that is the branch's section untouched; on the centreline
                // the run's own floor untouched; and down both sides the branch's term is
                // already zero — so the patch meets the run exactly, on every edge.
                //
                // Measured down from the run's own point, which already stands at mainCut below
                // the rim, so a side lands on exactly the point the run was cut at.
                float cut = ChannelFloor(Mathf.Abs(us[j]), halfInnerB, mouth.branch.riverDepth);

                Vector3 p = grid.PointAt(RunColumnArc(mouth, grid, col, us[j], mouth.fan[j]), col);
                pts[j] = new Vector3(p.x, p.y - (1f - v) * cut, p.z);
            }
            rows.Add(pts);
            rowCol.Add(col);
        }

        if (rows.Count < 2) return;

        Vector3 n0   = Vector3.Cross(rows[0][1] - rows[0][0], rows[1][1] - rows[0][1]);
        bool    flip = n0.y < 0f;

        // Row by row. Out on the run, the two outermost strips carry a vertex wherever a ring of
        // the run crosses their side, because the run was cut along that side at those rings.
        for (int k = 0; k < rows.Count - 1; k++)
        {
            int colOut = rowCol[k], colIn = rowCol[k + 1];
            bool onRun = colOut >= 0;

            float o0 = 0f, oM = 0f, i0 = 0f, iM = 0f;
            if (onRun)
            {
                RunMouthSides(mouth, grid, colOut, out o0, out oM);
                RunMouthSides(mouth, grid, colIn,  out i0, out iM);
            }

            for (int j = 0; j < m; j++)
            {
                Vector3 p = rows[k][j],         q = rows[k][j + 1];
                Vector3 s = rows[k + 1][j + 1], t = rows[k + 1][j];

                if (!onRun || (j != 0 && j != m - 1))
                {
                    if (flip) b.Quad(q, p, t, s);
                    else      b.Quad(p, q, s, t);
                    continue;
                }

                var poly = new List<Vector3> { p, q };
                if (j == m - 1) poly.AddRange(SideCrossings(grid, colOut, oM, colIn, iM));
                poly.Add(s);
                poly.Add(t);
                if (j == 0)     poly.AddRange(SideCrossings(grid, colIn, i0, colOut, o0));

                if (flip) poly.Reverse();
                EmitConvex(b, poly);
            }

            // Only the collar hangs clear of the run, so only the collar has an underside.
            if (k > 0) continue;

            for (int j = 0; j < m; j++)
            {
                Vector3 p = Underside(rows[k][j],         floorY);
                Vector3 q = Underside(rows[k][j + 1],     floorY);
                Vector3 s = Underside(rows[k + 1][j + 1], floorY);
                Vector3 t = Underside(rows[k + 1][j],     floorY);
                if (flip) b.Quad(p, q, s, t);
                else      b.Quad(t, s, q, p);
            }
        }

        // The collar between the branch's open end and the run's rim stands clear of both, so
        // it carries its own sides down — and they are the branch's own sides, dead straight,
        // from the end of its run to where they meet the run's outer corner.
        MouthWall(b, rows[0][0], rows[1][0], floorY, rows[0][0] - mouth.centre);
        MouthWall(b, rows[0][m], rows[1][m], floorY, rows[0][m] - mouth.centre);
    }

    // ══════════════════════════════════════════════════════════════
    // POOL
    // ══════════════════════════════════════════════════════════════

    /// <summary>A river arriving at a pool: where its run stopped, and the section it carries.</summary>
    public struct PoolMouth
    {
        /// <summary>Pool-local. The centre of the run's last ring.</summary>
        public Vector3 centre;

        /// <summary>Pool-local, unit, pointing back out along the run the way it came.</summary>
        public Vector3 direction;

        /// <summary>The arriving river's own shape — the section the patch carries round.</summary>
        public RiverProfile profile;
    }

    // A mouth resolved against the pool it opens into: where its sides run, how many columns
    // wide it is, and how far in it reaches.
    private struct MouthSpan
    {
        public PoolMouth mouth;

        /// <summary>The two column lines the mouth was cut between, down at the ring it
        /// reaches in to. Out at the rim its sides are the run's own straight lines instead.</summary>
        public float     angleLo;
        public float     angleHi;

        public int       columns;
        public float     innerRadius;

        /// <summary>At and outside this the mouth is the run's own slot, carried in dead
        /// straight — the pool's rim, and the collar standing off it. Inside it the sides ease
        /// round into the pool's own fan, down where the bowl is smooth and there is no crease
        /// left to keep straight.</summary>
        public float     straightRadius;

        /// <summary>The way the run heads back out, and the way its section runs across. Both
        /// flat and unit, in the pool's local space.</summary>
        public Vector3   dir;
        public Vector3   right;

        /// <summary>Half the arriving river's outer width — where its outermost line runs.</summary>
        public float     halfOuter;

        /// <summary>The heading the run arrives on, and one column of the rings it crosses.</summary>
        public float     midAngle;
        public float     step;

        /// <summary>Which side of the mouth column zero of the arriving section runs down.</summary>
        public bool      startAtLo;

        public float FanStart => startAtLo ? angleLo : angleHi;
        public float FanEnd   => startAtLo ? angleHi : angleLo;
    }

    /// <summary>
    /// How many columns each of a pool's rings carries.
    ///
    /// One column is exactly as wide as one column of the arriving river's section, measured
    /// out at the rim edge. That is the whole reason a mouth comes out the width of the river
    /// that made it: it is cut as many columns wide as that river's section has, so the two
    /// only agree if a column and a section step are the same size.
    ///
    /// The count is held all the way down to <see cref="holdRadius"/> — the furthest a mouth
    /// reaches in — so a mouth's sides stay on a column line at every ring they cross. Below
    /// that it halves, keeping every ring's columns a subset of the ring outside it, which is
    /// what stops the middle of a bowl turning into slivers.
    /// </summary>
    private struct ColumnLadder
    {
        public int   baseColumns;
        public float holdRadius;
        public float columnWidth;

        public int At(float radius)
        {
            if (radius >= holdRadius - 0.0001f) return baseColumns;

            int n = baseColumns;
            while (n >= 16 && (n & 1) == 0 && TwoPi * radius / (n / 2) <= columnWidth) n /= 2;

            // Halving an even count can land on an odd one — 386 goes to 193 — and an odd ring
            // stops the halving dead at the very first step, so a ring that should have come
            // right down keeps nearly every column it started with. It also cannot be filled
            // with a grid, which needs the same number of steps down each pair of opposite
            // sides. Stepping back to the even count above costs one halving and fixes both.
            if ((n & 1) != 0) n *= 2;

            return n;
        }
    }

    /// <summary>
    /// A pool is the run's own cross-section bent into a circle — the same rim, the same
    /// half-ellipse channel floor, revolved about the centre. One number separates the shapes:
    /// an <paramref name="islandRadius"/> of 0 gives an open bowl, anything larger leaves a
    /// plinth in the middle flush with the rim, and a pool that several rivers cut into is a
    /// roundabout.
    ///
    /// A river arriving does not have its shape stamped onto the pool's surface. It takes a
    /// slice of the pool out — whole cells, between two of the pool's own column lines — and
    /// <see cref="BuildMouthPatch"/> fills that slice with a grid carrying the river's section
    /// round into the bowl. So a mouth's edges are edges of the mesh, rather than a shape
    /// approximated by wherever the vertices happened to fall.
    ///
    /// <paramref name="floorDepth"/> is how far the floor drops at its deepest — the middle of a
    /// bowl, or the centre of the ring channel. Passing 0 takes the river's own channel depth.
    /// <paramref name="edge"/> is the target length of an edge anywhere in the mesh; every count
    /// here comes off it, so a pool carries the same density of detail as the rivers meeting it.
    ///
    /// Built in the pool's local space with the rim top at y = 0, hanging below it by
    /// <see cref="RiverProfile.depth"/> — the same convention a run uses, so the two mate.
    /// </summary>
    public static Mesh BuildPool(
        RiverProfile profile, float poolRadius, float islandRadius, float floorDepth,
        IList<PoolMouth> mouths, float edge)
    {
        var b = new MeshBuild();
        if (profile == null) return b.ToMesh("RiverPool");

        poolRadius   = Mathf.Max(poolRadius, 0.01f);
        islandRadius = Mathf.Clamp(islandRadius, 0f, poolRadius - 0.01f);
        if (floorDepth <= 0f) floorDepth = profile.riverDepth;
        edge = Mathf.Max(edge, 0.005f);

        float outer = poolRadius + profile.rimWidth;

        var spans  = ResolveMouths(mouths, poolRadius, islandRadius, outer, edge);
        var ladder = BuildLadder(spans, outer, edge);

        // Where the rings stop and the grid takes over. Inside this there is nothing but smooth
        // bowl: no mouth reaches in this far, no crease runs round here, and the rings serve no
        // purpose but to meet each other at the very middle. A plinth stops them at its own edge
        // instead, because the flat top of a plinth is a crease and a ring is what holds it.
        float gridRadius = islandRadius > 0f
                         ? islandRadius
                         : Mathf.Min(ladder.holdRadius, poolRadius);

        var radii  = BuildPoolRadii(profile, poolRadius, islandRadius, spans, edge, gridRadius);
        if (radii.Count < 2) return b.ToMesh("RiverPool");

        // Now the rings are known, each mouth is pulled onto one of them and squared up to its
        // columns — so its inner row of vertices IS that ring's, and its sides ARE column lines.
        for (int i = 0; i < spans.Count; i++) spans[i] = SnapMouth(spans[i], radii, ladder);

        // A river far too wide for the pool it meets would take most of the circle out and
        // leave nothing to arrive at. Leave that pool whole rather than hollow it out.
        spans.RemoveAll(s => WidestMouth(s, outer) > TwoPi * 0.75f);

        var ring = new Vector3[radii.Count][];
        for (int i = 0; i < radii.Count; i++)
        {
            int   n = ladder.At(radii[i]);
            float y = -PoolFloor(radii[i], islandRadius, poolRadius, floorDepth);

            ring[i] = new Vector3[n];
            for (int j = 0; j < n; j++)
                ring[i][j] = PoolDirection(TwoPi * j / n) * radii[i] + Vector3.up * y;
        }

        // Top surface — island, channel and rim in one sheet, with the cells a mouth takes
        // left out of it entirely.
        for (int i = 0; i < radii.Count - 1; i++)
            StitchRings(b, ring[i], ring[i + 1], radii[i], radii[i + 1], spans);

        // The middle, filled with a grid off the innermost ring. It used to be closed by fanning
        // that ring across itself, which kept the faces off the very centre but still ran every
        // one of them back to a single corner of it — hundreds of slivers meeting at a point
        // sitting off to one side of the calmest part of the pool. The grid has no such corner:
        // its cells are square-ish right across the middle, and the ring it is filled to is left
        // exactly where it was, so the rings outside still meet it vertex for vertex.
        PoolMiddle(b, ring[0], spans, islandRadius, poolRadius, floorDepth);

        // Outer wall, and the underside it stands on. The wall breaks across a mouth — the
        // patch carries its own sides down through that gap.
        int   last   = radii.Count - 1;
        int   na     = ring[last].Length;
        float floorY = -profile.depth;
        float below  = radii[Mathf.Max(0, last - 1)];

        Vector3 hub = new Vector3(0f, floorY, 0f);

        for (int j = 0; j < na; j++)
        {
            int j2 = (j + 1) % na;
            Vector3 top  = ring[last][j], top2 = ring[last][j2];
            Vector3 bot  = new Vector3(top.x,  floorY, top.z);
            Vector3 bot2 = new Vector3(top2.x, floorY, top2.z);

            if (!InAnyMouth(spans, below, outer, TwoPi * (j + 0.5f) / na))
                b.Quad(top, bot, bot2, top2);

            // The flat the wall stands on stays a fan. It is the one hub left in the piece and
            // it is deliberate: nobody ever looks at the bottom of a pool, and a grid laid off a
            // ring this wide would cost a hundred times the faces this does.
            b.Tri(hub, bot2, bot);
        }

        foreach (var span in spans)
            BuildMouthPatch(b, span, poolRadius, islandRadius, floorDepth,
                            outer, radii, profile.depth, edge);

        // A pool is built with its rim top flat at y = 0, so there is no line to measure off.
        // Its mouths still dip to the groove of whichever river arrives, though, so the deepest
        // of those goes with it — see the same call in BuildRun.
        float groove = 0f;
        foreach (var span in spans)
            if (span.mouth.profile != null)
                groove = Mathf.Max(groove, span.mouth.profile.jointGroove);

        b.ShadeSeams(null, groove);

        return b.ToMesh("RiverPool");
    }


    /// <summary>
    /// Fills the middle of the bowl with a grid laid inside its innermost ring.
    ///
    /// The ring itself is handed to <see cref="GridFill"/> untouched and comes back untouched,
    /// so the ring outside it still meets it vertex for vertex and there is nothing to stitch.
    /// Only the height has to be put back: the fill works in a straight line between the points
    /// it was given, and the bowl is a curve, so a point invented inside the ring is dropped onto
    /// the pool's own floor at whatever radius it landed at. On the ring that answer is the
    /// height the ring already had, exactly, which is why the border does not move.
    /// </summary>
    private static void PoolMiddle(
        MeshBuild b, Vector3[] ring, List<MouthSpan> spans,
        float islandRadius, float poolRadius, float floorDepth)
    {
        // Turned so the grid's corners fall between the rivers, exactly as the water's is. The
        // rings outside carry the mouths in, so the lines the grid runs back from them want to
        // be the straight middle of a side rather than the shear of a corner.
        int n = ring.Length;

        var ringAngles = new List<float>(n);
        for (int j = 0; j < n; j++) ringAngles.Add(TwoPi * j / n);

        var keepClear = new List<float>();
        if (spans != null)
            foreach (var span in spans)
            {
                keepClear.Add(Mathf.Repeat(span.midAngle, TwoPi));
                keepClear.Add(Mathf.Repeat(span.angleLo,  TwoPi));
                keepClear.Add(Mathf.Repeat(span.angleHi,  TwoPi));
            }

        var grid = GridFill(ring, GridTurn(ringAngles, keepClear));
        if (grid == null)
        {
            // Too few points to make a grid of — close it the old way rather than leave a hole.
            for (int j = 1; j + 1 < ring.Length; j++) b.Tri(ring[0], ring[j], ring[j + 1]);
            return;
        }

        int p = grid.Length - 1;
        int q = grid[0].Length - 1;

        // The border is already at the height its ring put it at, so only the inside is dropped
        // onto the floor — which keeps the two bit for bit the same rather than merely equal.
        for (int i = 1; i < p; i++)
            for (int j = 1; j < q; j++)
            {
                Vector3 v = grid[i][j];
                float   r = new Vector2(v.x, v.z).magnitude;

                grid[i][j] = new Vector3(
                    v.x, -PoolFloor(r, islandRadius, poolRadius, floorDepth), v.z);
            }

        EmitGrid(b, grid);
    }

    /// <summary>
    /// Turns a finished grid into faces, wound to face up whichever way round its outline was
    /// handed in.
    /// </summary>
    private static void EmitGrid(MeshBuild b, Vector3[][] grid)
    {
        int p = grid.Length - 1;
        int q = grid[0].Length - 1;

        bool up = Vector3.Cross(grid[1][0] - grid[0][0], grid[1][1] - grid[1][0]).y >= 0f;

        for (int i = 0; i < p; i++)
            for (int j = 0; j < q; j++)
            {
                Vector3 a = grid[i][j],         c = grid[i + 1][j];
                Vector3 d = grid[i + 1][j + 1], e = grid[i][j + 1];

                if (up) b.Quad(a, c, d, e);
                else    b.Quad(a, e, d, c);
            }
    }

    /// <summary>
    /// The piece that carries a river's section round into the pool. One grid: its first row
    /// is the run's own last ring, its last row lies on the ring the mouth reaches in to, and
    /// it has exactly as many columns as the run's top surface — so every line of the section
    /// runs on into the bowl instead of stopping against it.
    ///
    /// Out across the collar and the pool's rim, every one of those lines is the run's own
    /// straight line carried on — so the rim keeps its width and its two corners arrive
    /// parallel, the outer one running to where it meets the rim's outer corner and the inner
    /// one on to where the bowl starts. Only past the rim, down on the smooth of the bowl
    /// where there is no corner left to hold, does the section ease round into the pool's own
    /// fan and land on the ring it reaches in to.
    ///
    /// The pool gives up whole cells, which stop a little wide of where the straight sides
    /// come in, so every row carries a shoulder either side out to that cell line. A shoulder
    /// sits on the pool's own surface at the pool's own vertices, so it reads as rim rather
    /// than as part of the river, and the two never leave a gap between them.
    /// </summary>
    private static void BuildMouthPatch(
        MeshBuild b, MouthSpan span, float poolRadius, float islandRadius, float floorDepth,
        float outer, List<float> radii, float depth, float edge)
    {
        var branch = span.mouth.profile;
        if (branch == null || span.dir.sqrMagnitude < 1e-8f) return;

        var xs = TopColumns(branch, edge);
        int m  = xs.Count - 1;
        if (m != span.columns || m < 2) return;

        float halfInner = branch.innerWidth * 0.5f;
        float reach     = Mathf.Max(0.0001f, outer - span.innerRadius);
        float poolIn    = PoolFloor(span.innerRadius, islandRadius, poolRadius, floorDepth);

        // Rows outermost first: the run's own last ring, then every pool ring the mouth
        // crosses. A row's radius is -1 for the run's own ring, which is not on a circle.
        var rows = new List<Vector3[]>();
        var rowR = new List<float>();

        var row0 = new Vector3[m + 1];
        for (int j = 0; j <= m; j++)
        {
            // Dipped by the joint groove, exactly as the run dips its own last ring, so the
            // two still stand on one ring and the seam between them is a groove by design.
            float cut = ChannelFloor(Mathf.Abs(xs[j]), halfInner, branch.riverDepth);
            row0[j] = span.mouth.centre + span.right * xs[j]
                    + Vector3.up * -(cut + branch.jointGroove);
        }
        rows.Add(row0);
        rowR.Add(-1f);

        for (int i = radii.Count - 1; i >= 0; i--)
        {
            float r = radii[i];
            if (r > outer + 0.0001f) continue;
            if (r < span.innerRadius - 0.0001f) break;

            // The river's channel gives way exactly as the pool's own takes over, so it holds
            // its depth the whole way across the rim and only lifts once there is a bowl under
            // it. Stepping it off the radius instead leaves the river already half faded where
            // the pool has yet to cut anything at all, and the floor humps up into a bar of
            // stone right on the channel edge — standing proud of the water, cutting the pool
            // off from the river it just arrived from. The junction mouth learned this first.
            // Measured against the bowl at the ring the mouth reaches in to, so the handover
            // is complete exactly there and the patch still lands on the pool's own floor
            // without a step — which is the one thing the radius ramp did get right.
            float pool = PoolFloor(r, islandRadius, poolRadius, floorDepth);
            float v    = poolIn > 0.0001f
                       ? Mathf.Clamp01(pool / poolIn)
                       : Mathf.Clamp01((outer - r) / reach);

            var pts = new Vector3[m + 1];

            for (int j = 0; j <= m; j++)
            {
                // The pool's own floor, with the river's channel fading out of it. At the rim
                // that is the river's section untouched; on the ring the mouth reaches in to,
                // the pool's floor untouched; and down both sides the channel term is already
                // zero — so the patch meets the pool exactly, on every edge.
                float cut = ChannelFloor(Mathf.Abs(xs[j]), halfInner, branch.riverDepth);
                float y   = -(pool + (1f - v) * cut);
                float fan = Mathf.Lerp(span.FanStart, span.FanEnd, (float)j / m);

                pts[j] = PoolDirection(MouthColumnAngle(span, xs[j], fan, r)) * r
                       + Vector3.up * y;
            }
            rows.Add(pts);
            rowR.Add(r);
        }

        if (rows.Count < 2) return;

        Vector3 n0     = Vector3.Cross(rows[0][1] - rows[0][0], rows[1][1] - rows[0][1]);
        bool    flip   = n0.y < 0f;
        float   floorY = -depth;

        // Band by band, each with its own shoulders out to the cells the pool gave up across
        // that band — judged the same way the pool judged them, so the two always meet.
        for (int k = 0; k < rows.Count - 1; k++)
        {
            float rOut = rowR[k], rIn = rowR[k + 1];

            // The collar keeps the run's own sides and no shoulder at all — it is the section
            // swept straight on to the rim, and the ring inside it picks the shoulder up.
            bool collar = rOut < 0f && rows.Count > 2;

            for (int j = 0; j < m; j++)
            {
                Vector3 p = rows[k][j],         q = rows[k][j + 1];
                Vector3 s = rows[k + 1][j + 1], t = rows[k + 1][j];
                if (flip) b.Quad(q, p, t, s);
                else      b.Quad(p, q, s, t);
            }

            if (!collar)
            {
                MouthCells(span, rOut < 0f ? rIn : rOut, rIn, out float cell0, out float cellM);
                MouthSides(span, Mathf.Max(rOut, span.innerRadius), out float o0, out float oM);
                MouthSides(span, Mathf.Max(rIn,  span.innerRadius), out float i0, out float iM);

                MouthShoulder(b, span, rOut, rIn, o0, i0, cell0,
                              rows[k][0], rows[k + 1][0],
                              islandRadius, poolRadius, floorDepth, !flip);
                MouthShoulder(b, span, rOut, rIn, oM, iM, cellM,
                              rows[k][m], rows[k + 1][m],
                              islandRadius, poolRadius, floorDepth, flip);
            }

            // Only the collar hangs clear of the pool, so only the collar has an underside.
            if (k > 0) continue;

            for (int j = 0; j < m; j++)
            {
                Vector3 p = Underside(rows[k][j],         floorY);
                Vector3 q = Underside(rows[k][j + 1],     floorY);
                Vector3 s = Underside(rows[k + 1][j + 1], floorY);
                Vector3 t = Underside(rows[k + 1][j],     floorY);
                if (flip) b.Quad(p, q, s, t);
                else      b.Quad(t, s, q, p);
            }
        }

        // The collar between the run's open end and the pool's rim stands clear of both, so it
        // carries its own sides down — and they are the run's own sides, dead straight, from
        // the end of the run to the point where they meet the rim's outer corner.
        MouthWall(b, rows[0][0], rows[1][0], floorY, rows[0][0] - span.mouth.centre);
        MouthWall(b, rows[0][m], rows[1][m], floorY, rows[0][m] - span.mouth.centre);

        // The pool broke its outer wall at whole cells, a shoulder wide of where those sides
        // come in. Carry the wall on round the rim across each shoulder, so it is never open.
        if (rows.Count < 3) return;

        MouthCells(span, rowR[1], rowR[2], out float w0, out float wM);
        MouthSides(span, rowR[1], out float s0, out float sM);

        ShoulderWall(b, span, rowR[1], s0, w0, floorY, islandRadius, poolRadius, floorDepth);
        ShoulderWall(b, span, rowR[1], sM, wM, floorY, islandRadius, poolRadius, floorDepth);
    }

    /// <summary>
    /// The flat step from one of a mouth's sides out to the cell line the pool gave up at, on
    /// the pool's own surface. It carries a vertex at every cell line it crosses, on both of
    /// its rings — the pool's own cells stop at those lines, and a shoulder that ran straight
    /// past them would leave the ring seamed even though nothing is missing.
    ///
    /// The two rings cross a different number of lines, so the strip is stitched rather than
    /// squared off: whichever side has the nearer line next takes the triangle.
    /// </summary>
    private static void MouthShoulder(
        MeshBuild b, MouthSpan span, float rOut, float rIn,
        float sideOut, float sideIn, float boundary,
        Vector3 sideOutPt, Vector3 sideInPt,
        float islandRadius, float poolRadius, float floorDepth, bool flip)
    {
        var outA = ShoulderAngles(sideOut, boundary, span.step);
        var inA  = ShoulderAngles(sideIn,  boundary, span.step);

        // The run's own ring is not on a circle, so every point of its side is the one point.
        Vector3 OutPt(int i) => rOut < 0f
                              ? sideOutPt
                              : ShoulderPoint(rOut, outA[i], islandRadius, poolRadius, floorDepth);
        Vector3 InPt(int j)  => j == 0
                              ? sideInPt
                              : ShoulderPoint(rIn, inA[j], islandRadius, poolRadius, floorDepth);

        // With no circle to cross there are no lines to land on, so the strip fans off that
        // one point instead.
        if (rOut < 0f) outA = new List<float> { sideOut, sideOut };

        float sign = boundary >= sideIn ? 1f : -1f;
        int   i = 0, j = 0;

        while (i < outA.Count - 1 || j < inA.Count - 1)
        {
            bool takeOut = j >= inA.Count - 1
                        || (i < outA.Count - 1 && sign * outA[i + 1] <= sign * inA[j + 1]);

            if (takeOut)
            {
                if (flip) b.Tri(OutPt(i + 1), OutPt(i), InPt(j));
                else      b.Tri(OutPt(i), OutPt(i + 1), InPt(j));
                i++;
            }
            else
            {
                if (flip) b.Tri(OutPt(i), InPt(j), InPt(j + 1));
                else      b.Tri(OutPt(i), InPt(j + 1), InPt(j));
                j++;
            }
        }
    }

    // The wall standing down from a shoulder's own arc, broken at the same cell lines.
    private static void ShoulderWall(
        MeshBuild b, MouthSpan span, float r, float side, float boundary, float floorY,
        float islandRadius, float poolRadius, float floorDepth)
    {
        var angles = ShoulderAngles(side, boundary, span.step);
        for (int i = 0; i < angles.Count - 1; i++)
        {
            Vector3 p = ShoulderPoint(r, angles[i],     islandRadius, poolRadius, floorDepth);
            Vector3 q = ShoulderPoint(r, angles[i + 1], islandRadius, poolRadius, floorDepth);
            MouthWall(b, p, q, floorY, p);
        }
    }

    // A shoulder's own line, from a mouth's side out to the cell the pool gave up at, with
    // every cell line in between.
    private static List<float> ShoulderAngles(float side, float boundary, float step)
    {
        var angles = new List<float> { side };
        step = Mathf.Max(step, 1e-5f);

        float eps = step * 0.001f;
        if (boundary > side)
            for (float k = Mathf.Ceil(side / step); k * step < boundary - eps; k += 1f)
            {
                if (k * step > side + eps) angles.Add(k * step);
            }
        else
            for (float k = Mathf.Floor(side / step); k * step > boundary + eps; k -= 1f)
            {
                if (k * step < side - eps) angles.Add(k * step);
            }

        angles.Add(boundary);
        return angles;
    }

    private static Vector3 ShoulderPoint(
        float r, float angle, float islandRadius, float poolRadius, float floorDepth)
        => PoolDirection(angle) * r
         + Vector3.up * -PoolFloor(r, islandRadius, poolRadius, floorDepth);

    /// <summary>
    /// Where one line of the arriving section runs at a distance from the pool centre: the
    /// straight line the run carries in, eased into the pool's own fan once it is past the rim.
    /// </summary>
    private static float MouthColumnAngle(MouthSpan s, float x, float fanAngle, float r)
    {
        float straight = StraightAngle(s.mouth.centre, s.right, s.dir, x, r);
        float w        = FanWeight(s, r);
        return w <= 0f ? straight : straight + DeltaRad(straight, fanAngle) * w;
    }

    // Where the line the section carries in — offset x across the run — crosses the circle at
    // r. A line running too far out to reach that circle is taken at its closest approach
    // instead, so the answer never jumps as a mouth reaches past the middle of a small pool.
    private static float StraightAngle(
        Vector3 centre, Vector3 right, Vector3 dir, float x, float r)
    {
        Vector3 a = centre + right * x;
        Vector3 d = -dir;                                    // inward, the way the run runs

        float ad   = a.x * d.x + a.z * d.z;
        float disc = ad * ad - (a.x * a.x + a.z * a.z) + r * r;
        float t    = disc > 0f ? -ad - Mathf.Sqrt(disc) : -ad;

        Vector3 p = a + d * t;
        return Mathf.Atan2(p.x, p.z);
    }

    // How far a ring is into the bowl: 0 out at the rim, where the mouth is still the run's own
    // straight slot, to 1 on the ring it reaches in to, where it is the pool's own fan.
    private static float FanWeight(MouthSpan s, float r)
    {
        if (s.straightRadius <= s.innerRadius) return 1f;   // no room for a straight slot
        if (r >= s.straightRadius)             return 0f;
        if (r <= s.innerRadius)                return 1f;

        float k = (s.straightRadius - r) / Mathf.Max(0.0001f, s.straightRadius - s.innerRadius);
        return k * k * (3f - 2f * k);        // eased, so the sides leave straight without a corner
    }

    /// <summary>
    /// The whole cells a mouth takes out across a band of the pool. A mouth's sides are the
    /// run's own straight lines out at the rim and the pool's fan down in the bowl, so they
    /// are not a constant angle and neither ring alone is always the wider of the two: the
    /// band is taken across everywhere all four sides reach, rounded outwards. So the slice
    /// the pool gives up always holds the patch that fills it, however the run came in.
    /// </summary>
    private static void MouthCells(
        MouthSpan s, float rA, float rB, out float cell0, out float cellM)
    {
        MouthSides(s, Mathf.Max(rA, s.innerRadius), out float a0, out float aM);
        MouthSides(s, Mathf.Max(rB, s.innerRadius), out float b0, out float bM);

        float lo = Mathf.Min(Mathf.Min(a0, aM), Mathf.Min(b0, bM));
        float hi = Mathf.Max(Mathf.Max(a0, aM), Mathf.Max(b0, bM));

        float step  = Mathf.Max(s.step, 1e-5f);
        float loEnd = Mathf.Floor(lo / step) * step;
        float hiEnd = Mathf.Ceil (hi / step) * step;

        bool zeroIsHigh = a0 + b0 >= aM + bM;
        cell0 = zeroIsHigh ? hiEnd : loEnd;
        cellM = zeroIsHigh ? loEnd : hiEnd;
    }

    // Where a mouth's two sides run on the ring at r — unwrapped about the heading the run
    // arrives on, so the two stay either side of it however the mouth sits against the pool.
    private static void MouthSides(MouthSpan s, float r, out float side0, out float sideM)
    {
        side0 = s.midAngle
              + DeltaRad(s.midAngle, MouthColumnAngle(s,  s.halfOuter, s.FanStart, r));
        sideM = s.midAngle
              + DeltaRad(s.midAngle, MouthColumnAngle(s, -s.halfOuter, s.FanEnd,   r));
    }

    // The most of the pool a mouth ever takes out, over the rings it crosses.
    private static float WidestMouth(MouthSpan s, float outer)
    {
        float widest = 0f;
        for (int i = 0; i <= 8; i++)
        {
            float r = Mathf.Lerp(outer, s.innerRadius, i / 8f);
            MouthCells(s, r, r, out float a, out float c);
            widest = Mathf.Max(widest, Mathf.Abs(a - c));
        }
        return widest;
    }

    private static float DeltaRad(float from, float to)
        => Mathf.DeltaAngle(from * Mathf.Rad2Deg, to * Mathf.Rad2Deg) * Mathf.Deg2Rad;

    private static Vector3 Underside(Vector3 p, float floorY) => new Vector3(p.x, floorY, p.z);

    // One quad standing down from an edge to the underside, turned to face outward.
    private static void MouthWall(
        MeshBuild b, Vector3 top, Vector3 top2, float floorY, Vector3 outward)
    {
        Vector3 bot  = Underside(top,  floorY);
        Vector3 bot2 = Underside(top2, floorY);

        Vector3 n = Vector3.Cross(bot - top, bot2 - bot);
        if (Vector3.Dot(n, outward) >= 0f) b.Quad(top, bot, bot2, top2);
        else                               b.Quad(top, top2, bot2, bot);
    }

    /// <summary>
    /// Works out how wide each arriving river's mouth is and how far into the pool it reaches.
    ///
    /// A mouth is as many columns wide as the arriving run's top surface has, and both counts
    /// come off the same target edge length — so the mouth comes out the width of the river
    /// that made it, and every line of that river's section has a column to run into.
    /// </summary>
    private static List<MouthSpan> ResolveMouths(
        IList<PoolMouth> mouths, float poolRadius, float islandRadius, float outer, float edge)
    {
        var spans = new List<MouthSpan>();
        if (mouths == null) return spans;

        float channelR = PoolChannelRadius(poolRadius, islandRadius);

        foreach (var mouth in mouths)
        {
            if (mouth.profile == null) continue;

            int columns = TopColumns(mouth.profile, edge).Count - 1;
            if (columns < 2) continue;

            Vector3 dir = mouth.direction;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) continue;
            dir.Normalize();

            // In as far as the pool's deepest ring, where the mouth's floor and the pool's are
            // already the same depth — but never so far in that the ring there is too short to
            // hold the mouth.
            float innerRadius = Mathf.Max(channelR, columns * edge / Mathf.PI);
            innerRadius = Mathf.Min(innerRadius, poolRadius * 0.9f);

            spans.Add(new MouthSpan
            {
                mouth       = mouth,
                columns     = columns,
                innerRadius = innerRadius,

                // The mouth is the run's own slot out across the rim, and only bends into the
                // pool's fan once it is inside the channel edge. Held to the pool's own rim,
                // so a rim that is stepped finer or coarser makes no difference to where the
                // straight ends — that is the crease it has to carry to.
                straightRadius = StraightReach(mouth.centre, dir,
                                               mouth.profile.OuterWidth * 0.5f,
                                               poolRadius, outer),

                dir       = dir,
                right     = Vector3.Cross(Vector3.up, dir),
                halfOuter = mouth.profile.OuterWidth * 0.5f,
                midAngle  = Mathf.Atan2(mouth.centre.x, mouth.centre.z),
            });
        }
        return spans;
    }

    /// <summary>
    /// How far in a mouth can keep the run's own straight sides: to the channel edge, where the
    /// rim ends and there is no corner left to carry — but never past the ring its outermost
    /// line so much as touches, since a straight line cannot reach a circle smaller than its
    /// own standoff. A river too wide for the pool to hold a straight slot at all gets 0, and
    /// falls back to being turned round the pool's own fan the whole way, as it always was.
    /// </summary>
    private static float StraightReach(
        Vector3 centre, Vector3 dir, float halfOuter, float poolRadius, float outer)
    {
        float standoff = Mathf.Max(SideStandoff(centre, dir,  halfOuter),
                                   SideStandoff(centre, dir, -halfOuter));
        float reach = Mathf.Max(poolRadius, standoff);
        return reach > outer ? 0f : reach;
    }

    // How close to the pool centre one of a mouth's outer lines ever runs.
    private static float SideStandoff(Vector3 centre, Vector3 dir, float x)
    {
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        Vector3 a     = centre + right * x;
        return Mathf.Abs(a.x * dir.z - a.z * dir.x);
    }

    // Pulls a mouth onto a ring the pool actually has and squares its two sides up to whole
    // columns of that ring. From that ring outwards the count never changes, so the sides set
    // here stay on a column line at every ring further out that they cross.
    private static MouthSpan SnapMouth(MouthSpan span, List<float> radii, ColumnLadder ladder)
    {
        float nearest = radii[0];
        foreach (float r in radii)
            if (Mathf.Abs(r - span.innerRadius) < Mathf.Abs(nearest - span.innerRadius))
                nearest = r;

        float step = TwoPi / ladder.At(nearest);
        float mid  = span.midAngle;
        int   lo   = Mathf.RoundToInt((mid - span.columns * step * 0.5f) / step);

        span.innerRadius = nearest;
        span.step        = step;
        span.angleLo     = lo * step;
        span.angleHi     = (lo + span.columns) * step;

        // Which side of the mouth column zero of the arriving section runs down, so the
        // section's own straight lines and the fan they ease into agree on their order.
        Vector3 first = span.mouth.centre + span.right * span.halfOuter;
        float   a0    = Mathf.Atan2(first.x, first.z) * Mathf.Rad2Deg;

        span.startAtLo = Mathf.Abs(Mathf.DeltaAngle(a0, span.angleLo * Mathf.Rad2Deg))
                       < Mathf.Abs(Mathf.DeltaAngle(a0, span.angleHi * Mathf.Rad2Deg));
        return span;
    }

    /// <summary>
    /// Sizes the pool's columns off the rivers meeting it: one column as wide as one step of
    /// the arriving section, measured at the rim edge — so a mouth cut as many columns wide as
    /// that section has comes out the width of the river, right where the two meet. A pool
    /// nothing runs into has nothing to match, so it falls back to the target edge length.
    ///
    /// The count is then held in as far as the deepest mouth reaches. That makes a mouth a
    /// constant angle rather than a constant width, so it does narrow going down into the bowl
    /// — the price of its sides being column lines, and the reason it is anchored out at the
    /// rim rather than in at the bottom, where the same effect would flare it instead.
    /// </summary>
    private static ColumnLadder BuildLadder(List<MouthSpan> spans, float outer, float edge)
    {
        float width = edge;
        float hold  = outer;
        bool  found = false;

        foreach (var s in spans)
        {
            if (s.mouth.profile == null || s.columns < 2) continue;

            if (!found) width = s.mouth.profile.OuterWidth / s.columns;
            hold  = Mathf.Min(hold, s.innerRadius);
            found = true;
        }

        width = Mathf.Max(width, 0.001f);

        int n = Mathf.Clamp(Mathf.RoundToInt(TwoPi * Mathf.Max(outer, 0.001f) / width), 8, 2048);
        if ((n & 1) != 0) n++;

        return new ColumnLadder
        {
            baseColumns = n,
            holdRadius  = Mathf.Max(hold, 0.001f),
            columnWidth = width,
        };
    }

    // Stitches a ring to the one outside it. The outer ring carries the same columns or twice
    // as many, so a mouth's sides stay on a column line the whole way through.
    private static void StitchRings(
        MeshBuild b, Vector3[] inner, Vector3[] outer,
        float innerRadius, float outerRadius, List<MouthSpan> spans)
    {
        int ni = inner.Length, no = outer.Length;
        if (ni < 3 || no < ni) return;

        int ratio = Mathf.Max(1, no / ni);

        for (int i = 0; i < ni; i++)
        {
            if (InAnyMouth(spans, innerRadius, outerRadius, TwoPi * (i + 0.5f) / ni)) continue;

            Vector3 a = inner[i], c = inner[(i + 1) % ni];
            for (int k = 0; k < ratio; k++)
                b.Tri(a, outer[(i * ratio + k) % no], outer[(i * ratio + k + 1) % no]);
            b.Tri(a, outer[(i * ratio + ratio) % no], c);
        }
    }

    // Whether the band of cells between two rings has been taken out by a mouth. Both rings
    // are asked: a mouth's sides are the run's own straight lines out at the rim, so the slice
    // it takes widens going in, and neither ring alone is always the wider of the two.
    private static bool InAnyMouth(List<MouthSpan> spans, float rInner, float rOuter, float angle)
    {
        if (spans == null) return false;

        foreach (var s in spans)
        {
            // Both rings have to be inside the mouth's reach: a band straddling the ring it
            // reaches in to belongs to the pool, and the patch does not come out that far.
            if (Mathf.Min(rInner, rOuter) < s.innerRadius - 0.0001f) continue;

            MouthCells(s, rInner, rOuter, out float cell0, out float cellM);
            float lo = Mathf.Min(cell0, cellM);
            float hi = Mathf.Max(cell0, cellM);

            if (Mathf.Repeat(angle - lo, TwoPi) <= hi - lo) return true;
        }
        return false;
    }

    /// <summary>
    /// The pool's water: a flat sheet held <paramref name="waterLevel"/> below the rim top,
    /// spanning the bowl exactly as a run's ribbon spans its inner width — so its edges bury
    /// themselves in the channel wall and there is never a seam at the waterline.
    ///
    /// An open bowl is filled with a GRID rather than fanned out of its middle. A fan gives the
    /// middle of the pool a hub with every face in the sheet meeting at it: hundreds of slivers
    /// where the water is calmest, and every field read across it — how far the shore is, how the
    /// rings lie — pinched down to a point. The grid has no such place, and its outline is the
    /// circle itself, vertex for vertex: the boundary is handed to <see cref="GridFill"/> and
    /// comes back untouched, so nothing that meets the pool's edge has to be moved to suit what
    /// fills it.
    ///
    /// The grid is TURNED so that its four sides face the rivers rather than its four corners —
    /// see <see cref="GridStart"/>. A grid laid inside a circle has to bend somewhere, and it
    /// bends at its corners; a river arriving on a corner meets lines running across it at an
    /// angle, and a river arriving on a side meets lines running straight into it.
    ///
    /// Where a river arrives, the sheet reaches out PAST the circle rather than being cut from
    /// it. A run's water ends on a straight line right across its channel, and a circle only
    /// touches that line at the one point the river came in on — so a round pool would leave a
    /// crescent of daylight either side of the centreline, widest out at the river's own edges.
    /// The river takes a run of the circle's own points and carries them STRAIGHT DOWN ITS OWN
    /// AXIS to that line, which gives a strip with parallel sides and even columns, carrying the
    /// grid's lines on into the river. Carried out from the pool's middle instead — which is what
    /// this did first — the same strip splays as it goes and meets the river's square end at an
    /// angle, which is what made a mess of the joint.
    ///
    /// Keeping those strips OUTSIDE the grid rather than folding them into its outline is what
    /// makes the fill safe: an outline that detours out into three rivers is no longer convex,
    /// and a grid laid across a shape like that folds over on itself.
    ///
    /// A pool with a plinth in the middle holds a ring of water rather than a disc, which has no
    /// middle to be fanned out of — that one keeps its rings, stepped in at <paramref
    /// name="edge"/> the same way, so its cells come out the size of the grid's.
    /// </summary>
    public static Mesh BuildPoolWater(
        RiverProfile profile, float waterLevel,
        float poolRadius, float islandRadius, float edge,
        IList<PoolMouth> mouths = null, float floorDepth = 0f, float overlap = 0f)
    {
        var b = new MeshBuild();
        if (profile == null) return b.ToMesh("RiverPoolWater");

        poolRadius   = Mathf.Max(poolRadius, 0.01f);
        islandRadius = Mathf.Clamp(islandRadius, 0f, poolRadius - 0.01f);
        edge         = Mathf.Max(edge, 0.005f);
        overlap      = Mathf.Max(overlap, 0f);
        if (floorDepth <= 0f) floorDepth = profile.riverDepth;

        // Every river that meets this pool, each carrying the two lines its strip runs between:
        // where the river's own water really ends, and how far past that the pool's water is
        // drawn over it before fading out.
        var flats = WaterFlats(profile, poolRadius, mouths, edge, overlap);

        var angles = PoolWaterAngles(poolRadius, flats, edge);
        if (angles.Count < 3) return b.ToMesh("RiverPoolWater");

        float y = -waterLevel;

        bool island = islandRadius > 0f;

        // Where the bowl comes up through the water, out at the wall and in at the island — the
        // run's half-ellipse turned round a circle, read off the ring the pool is deepest along.
        float channelHalf = island ? (poolRadius - islandRadius) * 0.5f : poolRadius;
        float mid         = island ? islandRadius + channelHalf : 0f;
        float sunk        = floorDepth > 0.0001f ? Mathf.Clamp01(waterLevel / floorDepth) : 1f;
        float halfWater   = channelHalf * Mathf.Sqrt(Mathf.Max(0f, 1f - sunk * sunk));

        float outerShore = mid + halfWater;
        float innerShore = island ? mid - halfWater : -1f;

        // A pool's rings are drawn in world space from the pool's own centre. What its surface
        // carries is the SAME on every vertex — the two waterline radii, for Pool Reach, the
        // circle its strips set off from, for Pool Mouth Fade, and the lap's length, for the fade
        // distance — so nothing can change along a face edge.
        System.Func<Vector3, bool, Vector4> edges = (flat, open)
            => new Vector4(outerShore, innerShore, poolRadius, overlap);

        int n = angles.Count;

        // The pool's own circle: the outline the sheet inside is filled to, and the line every
        // strip sets off from. Held flat, with the water's own height put on at the last moment,
        // because everything read off a corner is read off where it lies rather than how deep.
        var rim = new Vector3[n];
        for (int j = 0; j < n; j++) rim[j] = PoolDirection(angles[j]) * poolRadius;

        // Which river, if any, owns the cell between one heading and the next. A cell belongs to
        // whichever river's water crosses it, and the two headings a cell lies between were put
        // on the list by those very crossings — so a cell is never half in and half out, and a
        // strip starts and stops on a clean radial edge.
        var owner = WaterOwners(angles, flats, poolRadius);

        // ── The sheet inside the circle ──────────────────────────────────────
        // It carries UV3 too, so Pool Mouth Reach Overlap can start a river's reach inside the circle:
        // each corner takes the river it faces. See SheetBank.
        b.BankAt = corner => SheetBank(corner, flats, waterLevel, edge);
        if (island) PoolWaterRings(b, angles, poolRadius, islandRadius, edge, y, edges);
        else        PoolWaterGrid (b, rim, GridTurn(angles, RiverHeadings(flats, poolRadius)),
                                  y, edges);

        // ── The strips running out into the rivers ───────────────────────────
        for (int j = 0; j < n; j++)
        {
            if (owner[j] < 0) continue;

            int  k = (j + 1) % n;
            var  f = flats[owner[j]];

            Vector3 o1 = CarryDownRiver(rim[j], f, f.distance);
            Vector3 o2 = CarryDownRiver(rim[k], f, f.distance);

            Vector4 eo1 = edges(o1, true);
            Vector4 eo2 = edges(o2, true);

            Vector4 fo1 = PoolFlowData(o1, 1f);
            Vector4 fo2 = PoolFlowData(o2, 1f);

            // UV3: the river this strip runs down — metres to its waterline either side, read off
            // the corner's offset across it, against the same shore the river's own water measures
            // from. Straight lines across a flat face, so it interpolates exactly. Only the strip
            // and its lap carry it; the sheet inside the circle has none.
            float riverShore = WaterHalfWidth(f.section, waterLevel);
            float centreline = Vector3.Dot(f.end, f.across);
            b.BankAt = corner =>
            {
                float off = Vector3.Dot(corner, f.across) - centreline;
                return new Vector4(riverShore + off, riverShore - off, 1f, 0f);
            };

            // The two rim corners a strip sets off from are open water as well: there is no wall
            // across a mouth for the ripples to end on, so they carry on out into the river.
            b.Quad(rim[j] + Vector3.up * y, o1 + Vector3.up * y,
                   o2     + Vector3.up * y, rim[k] + Vector3.up * y,
                   edges(rim[j], true), eo1, eo2, edges(rim[k], true),
                   PoolFlowData(rim[j], 1f), fo1, fo2, PoolFlowData(rim[k], 1f));

            // The lap: the same water carried on up the river, over the river's own, running out
            // to nothing at the far lip. With no overlap authored the two lines are one line and
            // this face is collapsed, which the builder drops.
            if (f.lipDistance <= f.distance + 0.0001f) continue;

            Vector3 l1 = CarryDownRiver(rim[j], f, f.lipDistance);
            Vector3 l2 = CarryDownRiver(rim[k], f, f.lipDistance);

            b.Quad(o1 + Vector3.up * y, l1 + Vector3.up * y,
                   l2 + Vector3.up * y, o2 + Vector3.up * y,
                   eo1, edges(l1, true), edges(l2, true), eo2,
                   fo1, PoolFlowData(l1, 0f), PoolFlowData(l2, 0f), fo2);
        }

        b.BankAt = null;

        return b.ToMesh("RiverPoolWater");
    }

    /// <summary>
    /// UV3 for a corner of the pool's own sheet: the river whose heading it lies nearest, and
    /// its metres to that river's waterline either side, exactly as a strip carries them — so
    /// the two agree along the circle they meet on. .z is 1 only between that river's banks
    /// (give or take <paramref name="margin"/>), 0 anywhere else, and the shader weights the
    /// river's reach by it. That keeps a face lying between two rivers — whose corners read two
    /// different rivers, and whose distances mean nothing interpolated — out of it entirely.
    /// Past a river's banks its reach is full anyway, so where .z steps off nothing shows.
    /// </summary>
    private static Vector4 SheetBank(Vector3 corner, List<WaterFlat> flats, float waterLevel, float margin)
    {
        corner.y = 0f;
        if (flats.Count == 0 || corner.sqrMagnitude < 1e-8f) return Vector4.zero;

        Vector3 heading = corner.normalized;
        int     best    = -1;
        float   nearest = 0f;
        for (int i = 0; i < flats.Count; i++)
        {
            float d = Vector3.Dot(heading, flats[i].dir);
            if (d > nearest) { nearest = d; best = i; }
        }
        if (best < 0) return Vector4.zero;

        var   f     = flats[best];
        float shore = WaterHalfWidth(f.section, waterLevel);
        float off   = Vector3.Dot(corner, f.across) - Vector3.Dot(f.end, f.across);
        float held  = Mathf.Abs(off) <= shore + margin ? 1f : 0f;
        return new Vector4(shore + off, shore - off, held, 0f);
    }

    /// <summary>
    /// Carries a point on the pool's circle straight down a river's axis until it reaches the
    /// line at <paramref name="to"/>. Down the AXIS, so the point keeps the offset it had across
    /// the river and the strip comes out with parallel sides.
    ///
    /// A line already behind the point leaves it where it is, which collapses that face — a
    /// river whose water ends inside the pool has no strip to draw, and the builder drops a face
    /// with no width rather than turning it inside out.
    /// </summary>
    private static Vector3 CarryDownRiver(Vector3 v, WaterFlat f, float to)
        => v + f.dir * Mathf.Max(0f, to - Vector3.Dot(v, f.dir));

    /// <summary>
    /// Where one river breaks the pool's circle: the two headings at which its own water edges —
    /// the lines running down it half its inner width either side of its centreline — cross that
    /// circle. Between them the pool's water leaves the circle and runs on down the river.
    ///
    /// Returned lo first, going forward to hi the way that passes through the river itself. A
    /// river wider than the pool it arrives at never crosses, and gets no strip.
    /// </summary>
    private static bool FlatSpan(WaterFlat f, float poolRadius, out float lo, out float hi)
    {
        lo = hi = 0f;

        float centre = Vector3.Dot(f.end, f.across);       // how far off the middle it arrives
        float left   = centre - f.halfWidth;
        float right  = centre + f.halfWidth;

        if (Mathf.Abs(left) >= poolRadius || Mathf.Abs(right) >= poolRadius) return false;

        Vector3 a = f.across * right
                  + f.dir * Mathf.Sqrt(poolRadius * poolRadius - right * right);
        Vector3 c = f.across * left
                  + f.dir * Mathf.Sqrt(poolRadius * poolRadius - left * left);

        lo = Mathf.Repeat(Mathf.Atan2(c.x, c.z), TwoPi);
        hi = Mathf.Repeat(Mathf.Atan2(a.x, a.z), TwoPi);
        return true;
    }

    /// <summary>
    /// Which river owns each cell of the pool's edge, or -1 for a cell that sees nothing but the
    /// circle. Owned once: two rivers overlapping would otherwise each draw the cell they share,
    /// and two faces in the same place read as one face drawn twice.
    /// </summary>
    private static int[] WaterOwners(List<float> angles, List<WaterFlat> flats, float poolRadius)
    {
        int n = angles.Count;

        var owner = new int[n];
        for (int j = 0; j < n; j++) owner[j] = -1;

        for (int i = 0; i < flats.Count; i++)
        {
            if (!FlatSpan(flats[i], poolRadius, out float lo, out float hi)) continue;

            float span = Mathf.Repeat(hi - lo, TwoPi);

            for (int j = 0; j < n; j++)
            {
                if (owner[j] >= 0) continue;

                float m = MidAngle(angles[j], angles[(j + 1) % n]);
                if (Mathf.Repeat(m - lo, TwoPi) < span) owner[j] = i;
            }
        }

        return owner;
    }

    /// <summary>The heading halfway between two, taken the short way round the back.</summary>
    private static float MidAngle(float a1, float a2)
        => Mathf.Repeat(a1 + Mathf.Repeat(a2 - a1, TwoPi) * 0.5f, TwoPi);

    /// <summary>
    /// Fills the open bowl with the grid, wound so that it faces up whichever way round its
    /// outline was handed in. Too few points to make a grid of leaves the pool empty rather than
    /// fanned — a circle stepped at the target edge length never has fewer than twelve.
    /// </summary>
    private static void PoolWaterGrid(
        MeshBuild b, Vector3[] rim, int turn, float y,
        System.Func<Vector3, bool, Vector4> edges)
    {
        var grid = GridFill(rim, turn);
        if (grid == null) return;

        int p = grid.Length - 1;
        int q = grid[0].Length - 1;

        bool flip = Vector3.Cross(grid[1][0] - grid[0][0], grid[1][1] - grid[1][0]).y < 0f;

        for (int i = 0; i < p; i++)
            for (int j = 0; j < q; j++)
            {
                Vector3 a = grid[i][j],         c = grid[i + 1][j];
                Vector3 d = grid[i + 1][j + 1], e = grid[i][j + 1];

                // Not forced open: the edge data decides from the corner's heading whether a
                // river opens the wall that way.
                Vector4 ea = edges(a, false), ec = edges(c, false);
                Vector4 ed = edges(d, false), ee = edges(e, false);

                Vector3 ya = a + Vector3.up * y, yc = c + Vector3.up * y;
                Vector3 yd = d + Vector3.up * y, ye = e + Vector3.up * y;

                if (flip)
                    b.Quad(ya, ye, yd, yc, ea, ee, ed, ec,
                           PoolFlowData(a, 1f), PoolFlowData(e, 1f),
                           PoolFlowData(d, 1f), PoolFlowData(c, 1f));
                else
                    b.Quad(ya, yc, yd, ye, ea, ec, ed, ee,
                           PoolFlowData(a, 1f), PoolFlowData(c, 1f),
                           PoolFlowData(d, 1f), PoolFlowData(e, 1f));
            }
    }

    /// <summary>
    /// Fills the ring of water round a plinth. There is no middle here to be fanned out of, so
    /// this stays rings — but stepped in at the target edge length rather than crossed in one
    /// jump, so a cell comes out as square as the grid's and everything read across it is read
    /// at the same scale.
    /// </summary>
    private static void PoolWaterRings(
        MeshBuild b, List<float> angles, float poolRadius, float islandRadius, float edge,
        float y, System.Func<Vector3, bool, Vector4> edges)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt((poolRadius - islandRadius) / edge));
        int n     = angles.Count;

        for (int i = 0; i < steps; i++)
        {
            float rOut = Mathf.Lerp(poolRadius, islandRadius, (float)i       / steps);
            float rIn  = Mathf.Lerp(poolRadius, islandRadius, (float)(i + 1) / steps);

            for (int j = 0; j < n; j++)
            {
                Vector3 d1 = PoolDirection(angles[j]);
                Vector3 d2 = PoolDirection(angles[(j + 1) % n]);

                Vector3 a = d1 * rIn,  c = d1 * rOut;
                Vector3 d = d2 * rOut, e = d2 * rIn;

                b.Quad(a + Vector3.up * y, c + Vector3.up * y,
                       d + Vector3.up * y, e + Vector3.up * y,
                       edges(a, false), edges(c, false), edges(d, false), edges(e, false),
                       PoolFlowData(a, 1f), PoolFlowData(c, 1f),
                       PoolFlowData(d, 1f), PoolFlowData(e, 1f));
            }
        }
    }

    /// <summary>
    /// Lays a grid of quads inside a closed outline, KEEPING every point of that outline exactly
    /// where it was — the outline is the grid's own border, so whatever met the shape before
    /// still meets it, vertex for vertex, and there is nothing to stitch.
    ///
    /// The outline is cut into four sides of p, q, p and q steps, and every point inside is read
    /// off all four of them at once: how far along its row says where the two side-to-side
    /// borders put it, how far down its column says where the two top-to-bottom ones do, and the
    /// four corners are taken back off so they are not counted twice. On a border the two terms
    /// that do not belong cancel exactly against the corners, which is why a border point comes
    /// back untouched rather than nearly untouched.
    ///
    /// It needs an EVEN number of points, since opposite sides must hold the same count — the
    /// caller sees to that. Convexity is the other thing it needs and cannot check: a shape that
    /// bends back into itself gets a grid that folds over. The pool hands it the circle alone for
    /// exactly that reason, and stands its mouths off the outside.
    /// </summary>
    private static Vector3[][] GridFill(IList<Vector3> outline, int turn = 0)
    {
        int n = outline.Count;
        if (n < 8 || (n & 1) != 0) return null;

        GridSides(n, out int p, out int q);
        if (q < 1) return null;

        // Where the first side starts. Every point of the outline is still used, and used once —
        // turning the grid only moves which four of them its corners land on.
        System.Func<int, Vector3> at = i => outline[((i + turn) % n + n) % n];

        Vector3 c0 = at(0);
        Vector3 c1 = at(p);
        Vector3 c2 = at(p + q);
        Vector3 c3 = at(2 * p + q);

        var grid = new Vector3[p + 1][];

        for (int i = 0; i <= p; i++)
        {
            grid[i] = new Vector3[q + 1];

            float   u    = (float)i / p;
            Vector3 near = at(i);                               // c0 -> c1 along one side
            Vector3 far  = at(2 * p + q - i);                   // c3 -> c2 along the other

            for (int j = 0; j <= q; j++)
            {
                float   v     = (float)j / q;
                Vector3 left  = at(n - j);                      // c0 -> c3 down one side
                Vector3 right = at(p + j);                      // c1 -> c2 down the other

                grid[i][j] = (1f - v) * near + v * far
                           + (1f - u) * left + u * right
                           - ((1f - u) * (1f - v) * c0 + u * (1f - v) * c1
                            + (1f - u) *        v  * c3 + u *        v  * c2);
            }
        }

        // The border, put back exactly as it came in. The sum above cancels down to the border
        // point on a border, but it cancels in ALGEBRA — in floats it lands a few millionths
        // off, and a vertex a few millionths off its neighbour is a vertex that can round into
        // the next bucket when the finished mesh is asked which of its faces touch. That reads
        // as an open edge, and an open edge gets a seam drawn down it. Copied rather than
        // computed, there is nothing to round.
        for (int i = 0; i <= p; i++)
        {
            grid[i][0] = at(i);
            grid[i][q] = at(2 * p + q - i);
        }
        for (int j = 0; j <= q; j++)
        {
            grid[0][j] = at(n - j);
            grid[p][j] = at(p + j);
        }

        return grid;
    }

    /// <summary>
    /// The frame one corner of a pool's water is drawn in. A pool is a bowl with a middle rather
    /// than a channel with two banks, so its lines are rings coming out of that middle — and what
    /// is carried for them is the OFFSET from that middle, flat, in the pool's own space. The
    /// shader takes the radius as its length, per pixel, and takes nothing else off it — the
    /// heading is worked out only for the debug view, because a drawing measured in headings
    /// pinches into a whorl at the middle where every direction meets.
    ///
    /// It used to carry the heading and the radius themselves, and that is what drew the
    /// starburst. A corner's numbers are INTERPOLATED across the face between them, and an angle
    /// does not survive being interpolated: an open bowl is a fan of triangles meeting at one
    /// point, that point has no heading of its own, and every triangle in the fan was handed a
    /// different invented one — so the wander read a different number at the middle of each and
    /// stepped across every edge of the fan. A position interpolates exactly, because the surface
    /// really is flat, so the rings come out as true circles at any tessellation.
    /// </summary>
    private static Vector4 PoolFlowData(Vector3 offset, float fade)
        => new Vector4(offset.x, offset.z, Mathf.Clamp01(fade), PoolKind);

    /// <summary>
    /// The straight line each arriving river's water ends on, in the pool's own space: the way
    /// the run heads back out, how far out along it that line lies, and how far across it the
    /// water really runs.
    ///
    /// Worked out from the very numbers the run's water was carried on by — its last ring,
    /// carried in by <see cref="PoolWaterReach"/>, spanning the arriving river's inner width —
    /// so the line this ends on and the line that ends on are one line.
    /// </summary>
    private static List<WaterFlat> WaterFlats(
        RiverProfile profile, float poolRadius, IList<PoolMouth> mouths, float edge,
        float overlap = 0f)
    {
        var flats = new List<WaterFlat>();
        if (mouths == null) return flats;

        // The reach is measured back INWARD from where the arriving run stopped. The overlap then
        // carries the pool's water on up the river from there, over the river's own — so it is
        // the same line moved further out, and both are held on one record rather than the whole
        // list being worked out twice. Two lists could not be told apart river by river anyway:
        // the guard below can drop a river from one and keep it in the other.
        float reach = Mathf.Max(0f, PoolWaterReach(profile, poolRadius, edge));

        foreach (var m in mouths)
        {
            Vector3 dir = m.direction;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) continue;
            dir.Normalize();

            Vector3 centre = m.centre;
            centre.y = 0f;

            Vector3 end = centre - dir * reach;
            float   d   = Vector3.Dot(end, dir);

            // A river reaching in past the pool's own middle has no line left to hold — leave
            // that one round rather than turning the water inside out.
            if (d <= 0.01f) continue;

            var section = m.profile != null ? m.profile : profile;

            flats.Add(new WaterFlat
            {
                dir         = dir,
                across      = Vector3.Cross(Vector3.up, dir).normalized,
                end         = end,
                distance    = d,
                lipDistance = d + Mathf.Max(overlap, 0f),
                halfWidth   = section.innerWidth * 0.5f,
                section     = section,
            });
        }

        return flats;
    }

    /// <summary>
    /// The headings the water's edge is built on: the pool's own even columns, with the two
    /// headings every river breaks that circle at forced in among them.
    ///
    /// Those crossings are where a river's strip starts and stops, so a cell falls wholly inside
    /// one or wholly outside it and the step between them is a clean radial edge by construction.
    /// The build used to carry a heading a hair either side of each of them, because the strip
    /// was then part of the pool's own fan and the step had to be faked by a pair of headings
    /// 0.002 apart. It is a grid now, and a pair that close is two cell lines running right
    /// across it a few millimetres apart — which is the one thing that folds a grid over on
    /// itself. The nudges went with the fan that needed them.
    /// </summary>
    private static List<float> PoolWaterAngles(
        float poolRadius, List<WaterFlat> flats, float edge)
    {
        int columns = Mathf.Clamp(Mathf.RoundToInt(TwoPi * poolRadius / edge), 12, 1024);

        var angles = new List<float>(columns + (flats != null ? flats.Count * 2 : 0));
        for (int j = 0; j < columns; j++) angles.Add(TwoPi * j / columns);

        var crossings = new List<float>();
        if (flats != null)
            foreach (var f in flats)
                if (FlatSpan(f, poolRadius, out float lo, out float hi))
                {
                    crossings.Add(lo);
                    crossings.Add(hi);
                }

        // A crossing landing all but on a column of its own would leave a cell line a whisker
        // from its neighbour, running the width of the pool. The crossing is the one that has to
        // be where it is — a column is only there to keep the steps even — so the column gives
        // way.
        float merge = TwoPi / columns * 0.25f;
        foreach (float c in crossings)
            angles.RemoveAll(a => Mathf.Abs(DeltaRad(a, c)) < merge);
        angles.AddRange(crossings);

        angles.Sort();
        angles = Dedupe(angles, merge);

        // The last heading is as close to the first as any other neighbouring pair, once the
        // way round the back is counted.
        if (angles.Count > 2 &&
            TwoPi - angles[angles.Count - 1] + angles[0] <= merge)
            angles.RemoveAt(angles.Count - 1);

        // Opposite sides of a grid hold the same number of steps, so the outline it is filled to
        // has to hold an even number of headings. The widest gap is the one that can least afford
        // to be split, so it is the one that is.
        if ((angles.Count & 1) != 0 && angles.Count >= 3)
        {
            int   at   = 0;
            float span = -1f;

            for (int j = 0; j < angles.Count; j++)
            {
                float gap = Mathf.Repeat(angles[(j + 1) % angles.Count] - angles[j], TwoPi);
                if (gap > span) { span = gap; at = j; }
            }

            angles.Insert(at + 1, Mathf.Repeat(angles[at] + span * 0.5f, TwoPi));
            angles.Sort();
        }

        return angles;
    }

    /// <summary>
    /// How many steps go down each pair of a grid's opposite sides. Opposite sides must carry the
    /// same count, so a closed outline of n points is cut into p, q, p and q — which is the whole
    /// reason n has to be even.
    /// </summary>
    private static void GridSides(int n, out int p, out int q)
    {
        int half = n / 2;
        p = Mathf.Max(1, Mathf.RoundToInt(half * 0.5f));
        q = half - p;
    }

    /// <summary>
    /// Which point of an outline a grid should start its first side at, so that its four corners
    /// fall as far as possible from the headings handed in.
    ///
    /// A grid laid inside a circle has to bend somewhere, and where it bends is its corners: the
    /// cells there are sheared and the lines running through them arrive at an angle to
    /// everything else. So the corners are put where nothing is happening, and whatever meets the
    /// circle gets the straight middle of a side instead — which is what lets a river's strip
    /// carry the grid's own lines on into it rather than cutting across them.
    ///
    /// Judged on the WORST corner rather than the average one. A turn that sits three rivers
    /// beautifully and puts a corner straight through the fourth is the one arrangement that has
    /// to be ruled out, and an average happily hides it.
    /// </summary>
    private static int GridTurn(List<float> outlineAngles, IList<float> keepClear)
    {
        int n = outlineAngles.Count;
        if (n < 8 || (n & 1) != 0 || keepClear == null || keepClear.Count == 0) return 0;

        GridSides(n, out int p, out int q);

        var atCorner = new int[] { 0, p, p + q, 2 * p + q };

        int   best      = 0;
        float bestWorst = -1f;

        for (int s = 0; s < n; s++)
        {
            float worst = Mathf.PI;

            foreach (int c in atCorner)
            {
                float corner = outlineAngles[(s + c) % n];

                foreach (float k in keepClear)
                    worst = Mathf.Min(worst, Mathf.Abs(DeltaRad(corner, k)));
            }

            if (worst > bestWorst) { bestWorst = worst; best = s; }
        }

        return best;
    }

    /// <summary>
    /// The headings a pool's grid has to keep its corners away from: the middle of every river
    /// that meets it and both of that river's edges, so a wide one is kept clear by its whole
    /// width rather than by its centreline alone.
    /// </summary>
    private static List<float> RiverHeadings(List<WaterFlat> flats, float poolRadius)
    {
        var headings = new List<float>();
        if (flats == null) return headings;

        foreach (var f in flats)
        {
            headings.Add(Mathf.Repeat(Mathf.Atan2(f.dir.x, f.dir.z), TwoPi));

            if (!FlatSpan(f, poolRadius, out float lo, out float hi)) continue;

            headings.Add(lo);
            headings.Add(hi);
        }

        return headings;
    }

    // A mouth's water edge: the straight line the arriving run's water surface ends on, held as
    // the plane it lies in and how far across that plane the water actually runs.
    private struct WaterFlat
    {
        /// <summary>Pool-local, flat and unit, pointing back out along the run.</summary>
        public Vector3 dir;

        /// <summary>Pool-local, flat and unit, running along the line itself.</summary>
        public Vector3 across;

        /// <summary>Pool-local, the middle of the run's last row of water.</summary>
        public Vector3 end;

        /// <summary>How far out along <see cref="dir"/> the line lies.</summary>
        public float   distance;

        /// <summary>The same line carried on up the river by the overlap — how far the pool's
        /// water is drawn OVER the river's own before it fades out. Equal to
        /// <see cref="distance"/> when nothing is lapped.</summary>
        public float   lipDistance;

        /// <summary>Half the arriving river's inner width — how far its water reaches either
        /// side of its centreline, which is exactly what its ribbon spans.</summary>
        public float   halfWidth;

        /// <summary>The arriving river's own cross-section — what its waterline is read off.</summary>
        public RiverProfile section;
    }

    /// <summary>
    /// The ring the pool is deepest along — the ring a boat travels round it on, and the ring
    /// a river's mouth reaches in to. An open bowl is deepest at its centre.
    /// </summary>
    public static float PoolChannelRadius(float poolRadius, float islandRadius)
        => islandRadius > 0f ? (poolRadius + islandRadius) * 0.5f : 0f;

    /// <summary>
    /// How far from the pool centre a river's own run has to stop: clear of the pool's outer
    /// wall by a collar, which the mouth patch turns the section round in — the pool's answer
    /// to <see cref="RunCollar"/>.
    /// </summary>
    public static float PoolEdgeDistance(RiverProfile profile, float poolRadius, float edge)
        => poolRadius + (profile != null ? profile.rimWidth : 0f)
         + PoolCollar(profile, poolRadius, edge);

    /// <summary>
    /// The gap left between a run's last ring and the pool's rim. That ring is a straight line
    /// across a curved wall, so it has to stand off far enough that all of it clears — and far
    /// enough that the collar is a band of real width rather than a crease.
    /// </summary>
    public static float PoolCollar(RiverProfile profile, float poolRadius, float edge)
    {
        edge = Mathf.Max(0.01f, edge);
        if (profile == null) return edge;

        float outer = poolRadius + profile.rimWidth;
        float half  = Mathf.Min(profile.OuterWidth * 0.5f, outer * 0.99f);
        float bulge = outer - Mathf.Sqrt(Mathf.Max(0f, outer * outer - half * half));

        return Mathf.Max(edge, bulge * 1.25f);
    }

    /// <summary>
    /// How far a river's water carries on past the end of its run to reach the water lying in
    /// the pool: across the collar, then across the rim.
    /// </summary>
    public static float PoolWaterReach(RiverProfile profile, float poolRadius, float edge)
        => PoolCollar(profile, poolRadius, edge) + (profile != null ? profile.rimWidth : 0f);

    private static Vector3 PoolDirection(float angle)
        => new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

    /// <summary>
    /// The pool's own top surface at a radius: the island's plinth, then the channel floor cast
    /// as the same half-ellipse a run uses, then flat out across the rim.
    /// </summary>
    private static float PoolFloor(float r, float islandRadius, float poolRadius, float floorDepth)
    {
        if (r >= poolRadius)    return 0f;                                      // rim
        if (islandRadius <= 0f) return ChannelFloor(r, poolRadius, floorDepth); // open bowl
        if (r <= islandRadius)  return 0f;                                      // island top

        float half = (poolRadius - islandRadius) * 0.5f;
        return ChannelFloor(Mathf.Abs(r - (islandRadius + half)), half, floorDepth);
    }

    // Radial samples out to the far edge of the rim, stepped at the target edge length, with
    // the island edge, the channel edge, the outer edge and the ring each mouth reaches in to
    // landing exactly on a sample — so those creases stay sharp and the mouths meet them.
    private static List<float> BuildPoolRadii(
        RiverProfile profile, float poolRadius, float islandRadius,
        List<MouthSpan> spans, float edge, float innermost)
    {
        float outer = poolRadius + profile.rimWidth;

        var key = new List<float>
        {
            innermost, islandRadius, PoolChannelRadius(poolRadius, islandRadius),
            poolRadius, outer
        };
        if (spans != null) foreach (var s in spans) key.Add(s.innerRadius);

        // Nothing inside the innermost ring: the grid fills that, and a ring in there would be
        // a ring the grid has to be stitched to rather than simply laid inside.
        key.RemoveAll(v => v < innermost - 0.0001f || v > outer);
        key.Sort();
        key = Dedupe(key, edge * 0.05f);

        var rs = new List<float>();
        for (int i = 0; i < key.Count - 1; i++)
        {
            float gap = key[i + 1] - key[i];
            int   n   = Mathf.Max(1, Mathf.CeilToInt(gap / edge));
            for (int j = 0; j < n; j++) rs.Add(key[i] + gap * j / n);
        }
        rs.Add(outer);

        // A river with no rim at all leaves the grid's own ring and the outer edge in the same
        // place, and one radius is not a band. Kept as two so the wall still has a ring to stand
        // on — the band between them has no width, and the builder drops a face with no width.
        if (rs.Count < 2) rs.Insert(0, innermost);

        return rs;
    }

    private static List<float> Dedupe(List<float> sorted, float epsilon)
    {
        var kept = new List<float>(sorted.Count);
        foreach (float v in sorted)
            if (kept.Count == 0 || v - kept[kept.Count - 1] > epsilon)
                kept.Add(v);
        return kept;
    }


    // ══════════════════════════════════════════════════════════════
    // STONE SHADING FOR OTHER PIECES
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Gives a finished mesh built somewhere else — an outpost, an arena wall, an archway, a
    /// tower — the same stone shading a run carries: seams on UV1, face kinds on UV2, and normals
    /// smoothed along everything that is not a seam. Every triangle is carried over as it was
    /// wound; the source's own normals and UVs are replaced, UV0 by the same planar projection a
    /// run uses.
    ///
    /// <paramref name="rimTopY"/> is the height of the rim top in the mesh's own space — what the
    /// waterline shading is placed off, so it has to be the river's rim, not the piece's top.
    /// <paramref name="lipY"/> is where the piece's own flat lip is when that is not the rim top;
    /// null reads the face kinds off the rim top as a run does.
    /// <paramref name="faceKinds"/>, when given, is one entry per source triangle: a
    /// <see cref="StoneFaceKind"/> that triangle takes whatever it looks like, or 0 to have it
    /// worked out as above — how a tower's parts are told which colour they are.
    /// <paramref name="faceParts"/>, when given, is one entry per source triangle: which part of
    /// the piece it belongs to. Two faces of different parts always meet at a seam, however gently
    /// they turn — how a tower's ramp gets its line where it meets the tier below and above.
    /// </summary>
    public static Mesh ShadeAsStone(Mesh source, float rimTopY = 0f, float? lipY = null,
                                    IList<float> faceKinds = null, IList<int> faceParts = null)
    {
        var b = new MeshBuild();
        if (source == null) return b.ToMesh("Stone");

        var verts = source.vertices;
        var tris  = source.triangles;
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            int face = i / 3;
            b.NextKind = faceKinds != null && face < faceKinds.Count ? faceKinds[face] : 0f;
            b.NextPart = faceParts != null && face < faceParts.Count ? faceParts[face] : 0;
            b.Tri(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]]);
        }
        b.NextKind = 0f;
        b.NextPart = 0;

        b.ShadeSeams(rimTopY, lipY);
        var mesh = b.ToMesh(source.name);
        Object.DestroyImmediate(source);
        return mesh;
    }

    // ══════════════════════════════════════════════════════════════
    // MESH PLUMBING
    // ══════════════════════════════════════════════════════════════

    // Every face gets its own vertices, so the run stays hard-edged and faceted.
    private class MeshBuild
    {
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _norms = new List<Vector3>();
        private readonly List<Vector2> _uvs   = new List<Vector2>();
        private readonly List<Vector4> _edges = new List<Vector4>();
        private readonly List<Vector4> _flows = new List<Vector4>();
        private readonly List<Vector4> _banks = new List<Vector4>();
        private readonly List<int>     _tris  = new List<int>();
        private bool                   _hasEdges;
        private bool                   _hasFlows;
        private bool                   _hasBanks;

        // What UV3 carries at a corner, read off where the corner lies, while this is set — a
        // pool's strips hand it the river they run down. Everything else gets zeroes.
        public System.Func<Vector3, Vector4> BankAt;

        // Set when the piece is stone the run shader draws its seam and waterline shading on.
        // Water never asks for it — it has its own use for UV1 — so the two never collide.
        private bool             _shadeSeams;
        private IList<Vector3>   _rimLine;

        // How far under the rim top a face may sit and still be the flat lip. Whatever joint
        // groove the piece is dipped by, plus the whisker in RimWhisker.
        private float            _rimBand = RimWhisker;

        // Where the rim top sits when there is no rim line to read it off — 0 for a run or a
        // pool, which are built from their rim top; somewhere else for a piece built from
        // another origin, such as an arena wall standing on the water surface.
        private float            _rimTopY;

        // The height of the flat lip for a piece whose lip is NOT its rim top — an outpost's lip
        // is the top of its wall, standing well above the river it is beside. Null leaves the
        // face kinds read off the rim top as a run's are.
        private float?           _lipY;

        // A face kind a caller has fixed, one per face actually added — 0 where it is left to
        // FaceKinds to work out. NextKind is what the next face added carries.
        private readonly List<float> _fixedKinds = new List<float>();
        public float                 NextKind;

        // Which part of the piece each face actually added belongs to — faces of different parts
        // always meet at a seam. NextPart is what the next face added carries; 0 for everything
        // that never says, so a piece that never sets it is one part and nothing changes.
        private readonly List<int>   _parts = new List<int>();
        public int                   NextPart;

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
            => Tri(a, b, c,
                   Vector4.zero, Vector4.zero, Vector4.zero,
                   Vector4.zero, Vector4.zero, Vector4.zero, false, false);

        /// <summary>
        /// A face that carries edge data with it — how far each of its corners lies from the
        /// water's edge. Only the water surfaces build with this; everything else goes through
        /// the plain overload and gets zeroes it never reads.
        /// </summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector4 ea, Vector4 eb, Vector4 ec)
            => Tri(a, b, c, ea, eb, ec,
                   Vector4.zero, Vector4.zero, Vector4.zero, true, false);

        /// <summary>
        /// A face carrying both what its corners know about their edges and the frame the water
        /// is drawn in — which way is along, which way is across, how much of an overlap it lies
        /// under, and whether it is a river or a pool.
        /// </summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c,
                        Vector4 ea, Vector4 eb, Vector4 ec,
                        Vector4 fa, Vector4 fb, Vector4 fc)
            => Tri(a, b, c, ea, eb, ec, fa, fb, fc, true, true);

        private void Tri(Vector3 a, Vector3 b, Vector3 c,
                         Vector4 ea, Vector4 eb, Vector4 ec,
                         Vector4 fa, Vector4 fb, Vector4 fc,
                         bool carriesEdges, bool carriesFlows)
        {
            Vector3 n = Vector3.Cross(b - a, c - b);
            if (n.sqrMagnitude < 1e-12f) return;          // collapsed, e.g. a zero-width rim
            n.Normalize();

            int i0 = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            _norms.Add(n); _norms.Add(n); _norms.Add(n);
            _uvs.Add(PlanarUV(a, n)); _uvs.Add(PlanarUV(b, n)); _uvs.Add(PlanarUV(c, n));
            _edges.Add(ea); _edges.Add(eb); _edges.Add(ec);
            _flows.Add(fa); _flows.Add(fb); _flows.Add(fc);
            if (BankAt != null)
            {
                _banks.Add(BankAt(a)); _banks.Add(BankAt(b)); _banks.Add(BankAt(c));
                _hasBanks = true;
            }
            else
            {
                _banks.Add(Vector4.zero); _banks.Add(Vector4.zero); _banks.Add(Vector4.zero);
            }
            _tris.Add(i0); _tris.Add(i0 + 1); _tris.Add(i0 + 2);
            _fixedKinds.Add(NextKind);
            _parts.Add(NextPart);

            _hasEdges |= carriesEdges;
            _hasFlows |= carriesFlows;
        }

        /// <summary>
        /// Asks for the shading data the run shader reads off UV1 — how far every corner of
        /// every face lies from the seams around it, and how far it sits below the rim top.
        /// Worked out from the finished mesh in <see cref="ToMesh"/> rather than handed in
        /// face by face, because a fold is only a seam once you can see BOTH faces that make
        /// it, and no one adding a triangle knows what will land against it later.
        ///
        /// It is also what asks for the face kinds on UV2 and for the normals to be smoothed
        /// along everything that is not a seam. All three read the same folds, so they are asked
        /// for together: a corner the shading draws a line along is exactly a corner the
        /// smoothing has to leave hard.
        ///
        /// <paramref name="rimLine"/> is the line the rim top runs along — a run's sweep of
        /// centres, so a river that climbs carries its rim height with it. A pool is built
        /// with its rim top flat at y = 0, so it passes null and everything measures off that.
        ///
        /// <paramref name="jointGroove"/> is the deepest the top surface is dipped anywhere in
        /// the piece, which is how far the flat lip is allowed to fall and still be the lip.
        /// </summary>
        public void ShadeSeams(IList<Vector3> rimLine, float jointGroove)
        {
            _shadeSeams = true;
            _rimLine    = rimLine;
            _rimBand    = Mathf.Max(0f, jointGroove) + RimWhisker;
        }

        /// <summary>
        /// The same shading for a piece with a flat rim top at <paramref name="rimTopY"/>, and
        /// optionally a lip of its own at <paramref name="lipY"/> — see <see cref="_lipY"/>.
        /// </summary>
        public void ShadeSeams(float rimTopY, float? lipY)
        {
            ShadeSeams(null, 0f);
            _rimTopY = rimTopY;
            _lipY    = lipY;
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         Vector4 ea, Vector4 eb, Vector4 ec, Vector4 ed)
        {
            Tri(a, b, c, ea, eb, ec);
            Tri(a, c, d, ea, ec, ed);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         Vector4 ea, Vector4 eb, Vector4 ec, Vector4 ed,
                         Vector4 fa, Vector4 fb, Vector4 fc, Vector4 fd)
        {
            Tri(a, b, c, ea, eb, ec, fa, fb, fc);
            Tri(a, c, d, ea, ec, ed, fa, fc, fd);
        }

        // Projected off whichever axis the face points along most — keeps the marble
        // texture roughly the same scale on the rim, the walls and the channel.
        private static Vector2 PlanarUV(Vector3 p, Vector3 n)
        {
            Vector3 a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x, p.z);
            if (a.x >= a.z)               return new Vector2(p.z, p.y);
            return new Vector2(p.x, p.y);
        }

        // ══════════════════════════════════════════════════════════
        // SEAM SHADING
        // ══════════════════════════════════════════════════════════
        //
        // What the run shader draws its dark lines along. A SEAM is a hard corner of the
        // finished piece: the rim top folding down into the channel, the rim top folding over
        // into the outer wall, the wall standing on the underside, and the open ends where one
        // generated piece butts onto the next. The gentle facets the channel floor is cut into
        // are NOT seams — they are one curved surface described in flats, and a line drawn down
        // each of them would read as a mistake.
        //
        // Every corner carries how far it lies from the THREE nearest seams around it, and the
        // shader takes the smallest of the three. Three rather than one is what lets a single
        // face be shaded from both of the seams it lies between: the rim top is one strip with a
        // seam down each side of it, and the outer wall is one quad from the rim top all the way
        // down to the underside. A single distance per corner could only ramp from one end of
        // either to the other; three of them meet in the middle instead, which is what puts a
        // line on both edges of the rim and both ends of the wall with the middle left alone.
        //
        // WHICH three, though, is settled per CORNER and not per face: each of a face's three
        // corners brings the seam nearest to it, and those are the three the face carries. That
        // is what stops the shading breaking. A corner's nearest seam is the nearest thing there
        // is to it, so no other slot can come out smaller — and every face meeting at that
        // corner therefore draws the same number on it, whatever else it happens to be carrying.
        // Deciding it per face instead is what used to break: the ring of faces around one face
        // is not the ring around the face beside it, so the two answered about the corner they
        // shared from different lists and drew a hard edge down the middle of a smooth surface.
        //
        // Where two corners bring the same seam there is a slot going spare, and it is filled
        // with the next seam nearest the face — a seam can cross the middle of a face without
        // being the closest thing to any of its corners, and it still has to draw on it.
        //
        // Distances are to the STRETCH of a seam rather than to the endless line it lies on. A
        // line carries on past both ends of its seam and inks whatever stands beyond it, which
        // on a pool — where every seam is a chord — means cutting a band back across the bowl.
        //
        // A slot with no seam to fill it carries NoSeam, so it never wins that smallest-of-three
        // and the shading simply runs out where the faces around a seam do.
        //
        // The fourth number is how far the corner sits OFF the rim top — zero on the rim, and
        // negative everywhere under it. That is what the waterline shading is placed off: the
        // water lies a fixed distance beneath the rim, so a river that climbs carries its water
        // up with it and a height measured off the world would drift away from it.
        /// <summary>
        /// What the finished piece knows about itself: which corners are really the same point,
        /// which of its folds are seams, which faces stand around each point, and how far off
        /// the rim top each point sits.
        ///
        /// Worked out once and handed to all three of the passes that read it — the seam
        /// shading, the face kinds and the smoothing. They have to agree: a fold the shading
        /// draws a line along is exactly a fold the smoothing has to leave hard, and two passes
        /// answering that separately would eventually disagree about one of them.
        /// </summary>
        private class Topology
        {
            public readonly int                        Faces;
            public readonly int[]                      Id;      // corner -> the point it really is
            public readonly List<Vector3>              Point;
            public readonly float[]                    OffRim;  // per point, off the rim top
            public readonly Dictionary<long, SideFold> Folds;
            public readonly Dictionary<int, List<int>> Around;  // point -> the faces meeting there

            public Topology(MeshBuild b)
            {
                Faces = b._tris.Count / 3;

                // Faces are built with their own copies of every corner, so the same point in
                // space arrives many times over. Round them onto a millimetre grid to find the
                // ones that are really the same point — a millimetre is far below anything the
                // section has in it, and coarse enough that two faces meeting exactly still
                // land together after the arithmetic that placed them took different routes.
                var welded = new Dictionary<Vector3Int, int>(b._verts.Count);
                Id    = new int[b._verts.Count];
                Point = new List<Vector3>();
                for (int i = 0; i < b._verts.Count; i++)
                {
                    Vector3 p = b._verts[i];
                    var     k = new Vector3Int(Mathf.RoundToInt(p.x * 1000f),
                                               Mathf.RoundToInt(p.y * 1000f),
                                               Mathf.RoundToInt(p.z * 1000f));
                    if (!welded.TryGetValue(k, out int w))
                    {
                        w = Point.Count;
                        welded.Add(k, w);
                        Point.Add(p);
                    }
                    Id[i] = w;
                }

                // Which sides are seams. There are three ways to be one, and a side is a seam if
                // it is any of them.
                //
                // ONE FACE ONLY — an open end of the piece, where a run butts onto the patch
                // that carries it on. Always a seam; it is a real edge of the mesh.
                //
                // A HARD TURN — the two faces fold away from each other by more than SeamAngle.
                // That is the rim over the outer wall, the wall onto the underside, and, on a
                // river and on the ring channel of a roundabout, the rim down into the channel.
                //
                // THE END OF THE FLAT LIP — one face is level and the other is not. The rim is a
                // flat lip laid round every piece, and where that lip ends is a corner however
                // gently the ground beyond it falls away. It has to be said separately because an
                // open bowl is the run's channel spread over the WHOLE pool rather than over a
                // channel's width, so it leaves the rim at a shallow angle a turn test cannot
                // tell from the bowl's own faceting — and that edge is exactly the line a pool is
                // drawn along.
                //
                // Level means level WITH THE WORLD, so it is only asked of a piece whose lip
                // really is flat in the world — a pool, a tower, an outpost. A run's lip rides
                // its sweep: where the river climbs, the lip climbs with it and tilts off flat
                // by however steeply it is rising. The rise is never even, so along any run the
                // tilt wanders back and forth across the threshold, and every crossing put a
                // line straight across the rim in the middle of a stretch with no corner in it.
                // A run has no need of the test either — its channel is cut to a width, so it
                // leaves the lip at a real corner the turn test sees.
                //
                // A CHANGE OF PART — the two faces were built as different parts of the piece (a
                // tower's base, ramp and second base). A steep ramp turns off the tier below by
                // less than SeamAngle, but the join is still a corner the piece was built with.
                float cosLimit = Mathf.Cos(SeamAngle * Mathf.Deg2Rad);
                bool  flatLip  = b._rimLine == null || b._rimLine.Count == 0;
                Folds          = new Dictionary<long, SideFold>(b._tris.Count);

                for (int t = 0; t < Faces; t++)
                {
                    Vector3 n     = b._norms[b._tris[t * 3]];
                    bool    level = flatLip && n.y > LevelFace;
                    for (int e = 0; e < 3; e++)
                    {
                        long key = SideKey(Id[b._tris[t * 3 + e]],
                                           Id[b._tris[t * 3 + (e + 1) % 3]]);
                        if (Folds.TryGetValue(key, out SideFold fold))
                        {
                            fold.count++;
                            if (Vector3.Dot(fold.normal, n) < cosLimit) fold.sharp = true;
                            if (fold.level != level)                    fold.sharp = true;
                            if (b._parts[fold.first] != b._parts[t])    fold.sharp = true;
                            fold.second = t;
                            Folds[key]  = fold;
                        }
                        else
                        {
                            Folds.Add(key, new SideFold
                            {
                                count = 1, normal = n, level = level, sharp = false,
                                first = t, second = -1,
                            });
                        }
                    }
                }

                // Which faces meet at each point, so a face can be handed the seams of everything
                // standing around it and not just the ones it happens to own.
                Around = new Dictionary<int, List<int>>(Point.Count);
                for (int t = 0; t < Faces; t++)
                for (int e = 0; e < 3; e++)
                {
                    int w = Id[b._tris[t * 3 + e]];
                    if (!Around.TryGetValue(w, out List<int> list))
                    {
                        list = new List<int>(6);
                        Around.Add(w, list);
                    }
                    if (list.Count == 0 || list[list.Count - 1] != t) list.Add(t);
                }

                // How far off the rim top each point sits, worked out once per point rather than
                // once per copy of it.
                OffRim = new float[Point.Count];
                for (int i = 0; i < Point.Count; i++)
                    OffRim[i] = Point[i].y - b.RimTopAt(Point[i]);
            }
        }

        /// <summary>
        /// Every seam in the piece, gathered once. A face used to be handed only the seams of the
        /// faces standing around it, which is what left the shading breaking: the ring of faces
        /// around one face is not the ring around the face beside it, so two faces meeting at a
        /// corner were answering about that corner from different lists and drawing two
        /// different answers on the one point.
        /// </summary>
        /// <param name="seamOf">Which seam runs along a side, for the sides that are seams.
        /// A face looks its own three sides up in here, so that a fold is always shaded from
        /// the seam lying along it — see <see cref="SeamShading"/>.</param>
        private static List<SeamEdge> Seams(Topology mesh, out Dictionary<long, int> seamOf)
        {
            var seam = new List<SeamEdge>(mesh.Folds.Count);
            seamOf   = new Dictionary<long, int>(mesh.Folds.Count);
            foreach (var pair in mesh.Folds)
            {
                SideFold fold = pair.Value;
                if (fold.count > 1 && !fold.sharp) continue;
                seamOf.Add(pair.Key, seam.Count);
                seam.Add(new SeamEdge
                {
                    on1 = mesh.Point[(int)(pair.Key >> 32)],
                    on2 = mesh.Point[(int)(pair.Key & 0xFFFFFFFFL)],
                });
            }
            return seam;
        }

        /// <summary>
        /// The seam nearest each POINT of the piece, and how far off it is, worked out for the
        /// point itself rather than for any of the faces meeting there. That is what makes the
        /// shading join up: whichever face asks about a corner, the nearest seam to it is the
        /// same seam, so the smallest of the three comes out the same on both sides of every fold.
        /// </summary>
        private static void NearestSeams(Topology mesh, List<SeamEdge> seam,
                                         out int[] closest, out float[] distance)
        {
            closest  = new int[mesh.Point.Count];
            distance = new float[mesh.Point.Count];
            for (int i = 0; i < mesh.Point.Count; i++)
            {
                float best = seam.Count == 0 ? 0f : float.MaxValue;
                for (int k = 0; k < seam.Count; k++)
                {
                    float d = SeamDistance(seam[k], mesh.Point[i]);
                    if (d < best) { best = d; closest[i] = k; }
                }
                distance[i] = best;
            }
        }

        private List<Vector4> SeamShading(Topology mesh, List<SeamEdge> seam,
                                          Dictionary<long, int> seamOf,
                                          int[] closest, float[] distance)
        {
            int faces   = mesh.Faces;
            var shading = new List<Vector4>(_verts.Count);
            for (int i = 0; i < _verts.Count; i++) shading.Add(Vector4.zero);
            if (faces == 0 || seam.Count == 0) return shading;

            int[]   id     = mesh.Id;
            float[] offRim = mesh.OffRim;

            var keep = new int[3];
            var own  = new int[3];

            for (int t = 0; t < faces; t++)
            {
                int     ia = _tris[t * 3], ib = _tris[t * 3 + 1], ic = _tris[t * 3 + 2];
                Vector3 a  = _verts[ia], b = _verts[ib], c = _verts[ic];

                // Which of the face's own three SIDES are seams. A fold has to be shaded from
                // the seam lying along it, and that is not something the corners can be trusted
                // to bring on their own — see below.
                int owned = 0;
                for (int e = 0; e < 3; e++)
                {
                    long side = SideKey(id[_tris[t * 3 + e]],
                                        id[_tris[t * 3 + (e + 1) % 3]]);
                    if (seamOf.TryGetValue(side, out int s)) own[owned++] = s;
                }

                // A face carries the seam nearest each of its own three corners. Three corners,
                // three slots — and because a corner's own nearest seam is always among them,
                // no other slot can ever undercut it, which is exactly why the two faces either
                // side of a fold cannot disagree about the corners they share.
                //
                // Where a corner stands on a side of its own face that is a seam, THAT is the
                // seam it carries. A seam is a stretch between two points, so a line of them
                // runs end to end and a corner along it lies on two at once — nothing at all
                // from either, and nothing to choose between them. Left to pick, the two
                // corners of a side would as readily take the seam butting onto each end as the
                // one running along between them, and a side shaded from its two neighbours
                // instead of from itself is measured out from its ends rather than along its
                // length: the dark runs in from both corners and gives out half a side's length
                // in, leaving the line broken in the middle of a fold that is dead straight.
                // Swapping in the side's own seam costs the corner nothing — it is at the same
                // nothing from it — and hands the face the one seam it certainly has to draw.
                int n = 0;
                for (int e = 0; e < 3; e++)
                {
                    int   point = id[_tris[t * 3 + e]];
                    int   k     = closest[point];
                    float near  = distance[point] + SeamTie;
                    for (int i = 0; i < owned; i++)
                        if (SeamDistance(seam[own[i]], _verts[_tris[t * 3 + e]]) <= near)
                        {
                            k = own[i];
                            break;
                        }

                    bool held = false;
                    for (int i = 0; i < n && !held; i++) held = keep[i] == k;
                    if (!held) keep[n++] = k;
                }

                // Any side of the face still without a slot takes one before anything further
                // off does. A sliver with a seam down all three of its sides has corners at
                // nothing from more seams than it has slots for, and the ones it has to draw
                // are its own.
                for (int i = 0; i < owned && n < 3; i++)
                {
                    bool held = false;
                    for (int j = 0; j < n && !held; j++) held = keep[j] == own[i];
                    if (!held) keep[n++] = own[i];
                }

                // Corners sharing a nearest seam leave slots going spare. Fill them with the
                // next seams nearest the face, so a seam crossing the middle of a face still
                // draws on it even though no corner of it was the closest thing around.
                if (n < 3)
                {
                    // The two nearest of what is left, found in one pass over the seams rather
                    // than one pass per slot — this runs for nearly every face of every piece.
                    int   brought = n;
                    int   first = -1, second = -1;
                    float near1 = float.MaxValue, near2 = float.MaxValue;

                    for (int k = 0; k < seam.Count; k++)
                    {
                        bool held = false;
                        for (int i = 0; i < brought && !held; i++) held = keep[i] == k;
                        if (held) continue;

                        float d = Mathf.Min(SeamDistance(seam[k], a),
                                  Mathf.Min(SeamDistance(seam[k], b), SeamDistance(seam[k], c)));

                        if      (d < near1) { near2 = near1; second = first;
                                              near1 = d;     first  = k; }
                        else if (d < near2) { near2 = d;     second = k; }
                    }

                    if (n < 3 && first  >= 0) keep[n++] = first;
                    if (n < 3 && second >= 0) keep[n++] = second;
                }

                shading[ia] = CornerShading(seam, keep, n, a, offRim[id[ia]]);
                shading[ib] = CornerShading(seam, keep, n, b, offRim[id[ib]]);
                shading[ic] = CornerShading(seam, keep, n, c, offRim[id[ic]]);
            }

            return shading;
        }

        /// <summary>How far one corner stands from each of the seams its face carries, with any
        /// slot left over holding NoSeam so it never wins the smallest of the three.</summary>
        private static Vector4 CornerShading(
            List<SeamEdge> seam, int[] keep, int n, Vector3 p, float offRim)
        {
            return new Vector4(
                n > 0 ? SeamDistance(seam[keep[0]], p) : NoSeam,
                n > 1 ? SeamDistance(seam[keep[1]], p) : NoSeam,
                n > 2 ? SeamDistance(seam[keep[2]], p) : NoSeam,
                offRim);
        }

        // ══════════════════════════════════════════════════════════
        // FACE KINDS
        // ══════════════════════════════════════════════════════════
        //
        // Which part of the run every face belongs to, for the shader to pick the stone's colour
        // off. Three answers: the OUTER faces the piece shows the world, the RIM lip laid flat
        // round the top, and the INNER faces of the channel the water runs down.
        //
        // Two questions settle it. Does the face look upward at all? Only the channel does — the
        // outer walls and the end caps stand exactly vertical, the underside faces straight down
        // — so anything that does not is outer. And of the ones that do, is the WHOLE face still
        // up at the rim top? Then it is the lip. The whole of it, because the topmost facet of
        // the channel starts at the rim and drops away from it, and a face with one corner on the
        // rim and the rest below is the wall under the lip rather than the lip itself.
        //
        // UV2.y carries the width of the surface the face belongs to — see SurfaceWidths.
        private List<Vector4> FaceKinds(Topology mesh, float[] widths)
        {
            var kinds = new List<Vector4>(_verts.Count);
            for (int i = 0; i < _verts.Count; i++) kinds.Add(Vector4.zero);

            for (int t = 0; t < mesh.Faces; t++)
            {
                Vector3 n = _norms[_tris[t * 3]];

                float lowest = float.MaxValue;
                for (int e = 0; e < 3; e++)
                    lowest = Mathf.Min(lowest, mesh.OffRim[mesh.Id[_tris[t * 3 + e]]]);

                float kind = n.y <= UpwardFace     ? FaceOuter
                           : lowest > -_rimBand    ? FaceRim
                                                   : FaceInner;

                // A piece with a lip of its own reads the lip off that height instead: level with
                // it is the lip, below it is the inside, and anything standing up ABOVE it — a
                // tower on an outpost — is part of what the piece shows the world.
                if (_lipY.HasValue && n.y > UpwardFace)
                {
                    float low = float.MaxValue, high = float.MinValue;
                    for (int e = 0; e < 3; e++)
                    {
                        float y = mesh.Point[mesh.Id[_tris[t * 3 + e]]].y - _lipY.Value;
                        low  = Mathf.Min(low, y);
                        high = Mathf.Max(high, y);
                    }

                    kind = high > _rimBand ? FaceOuter
                         : low > -_rimBand ? FaceRim
                                           : FaceInner;
                }

                // A kind the caller fixed for this face wins over anything worked out above.
                if (_fixedKinds[t] > 0f) kind = _fixedKinds[t];

                // FaceMark in w says the kind in x is a real one. A channel the mesh does not
                // have does not arrive at the shader as zero — it comes through carrying
                // whatever was left in the stream, which on these pieces is the seam data, and
                // metres-to-the-nearest-seam rounds to 1, 2 and 3 as readily as a kind does. The
                // mark is what a run that has not been rebuilt cannot accidentally produce.
                var carried = new Vector4(kind, widths[t], 0f, FaceMark);
                for (int e = 0; e < 3; e++) kinds[_tris[t * 3 + e]] = carried;
            }

            return kinds;
        }

        // ══════════════════════════════════════════════════════════
        // SMOOTHING
        // ══════════════════════════════════════════════════════════
        //
        // Every face is built with its own copies of its corners, so the piece comes out of the
        // sweep completely hard-edged. That is right at the seams and wrong everywhere else: the
        // channel is one curved surface cut into flats, and the outer wall is a straight wall
        // that facets its way round every bend the river takes. Left hard, both of them read as
        // a run of separate panels rather than as stone.
        //
        // So the normals are averaged across every fold that is NOT a seam, and left alone across
        // every fold that is. Which is why this reads the same folds the shading does: the lines
        // the run is drawn along and the corners it is allowed to be hard at have to be the same
        // list, or a seam would be smoothed away from under its own line.
        //
        // The rim is untouched by all of this. It is dead flat, and the lip meeting anything at
        // all is one of the three ways of being a seam, so nothing it touches ever averages into
        // it — the lip keeps its own crisp corners while the wall and the channel go smooth.
        /// <summary>
        /// Which faces are one surface: everything reachable through folds that are not seams.
        /// One entry per face, the same number for every face of the same surface.
        /// </summary>
        private static int[] Surfaces(Topology mesh)
        {
            var group = new int[mesh.Faces];
            for (int i = 0; i < mesh.Faces; i++) group[i] = i;

            int Root(int face)
            {
                while (group[face] != face)
                {
                    group[face] = group[group[face]];
                    face        = group[face];
                }
                return face;
            }

            foreach (SideFold fold in mesh.Folds.Values)
            {
                // Two faces exactly, folding gently. One face is an open edge and three is a mesh
                // that has folded back on itself — neither is a surface running on.
                if (fold.count != 2 || fold.sharp || fold.second < 0) continue;

                int a = Root(fold.first), b = Root(fold.second);
                if (a != b) group[a] = b;
            }

            var surface = new int[mesh.Faces];
            for (int i = 0; i < mesh.Faces; i++) surface[i] = Root(i);
            return surface;
        }

        // ══════════════════════════════════════════════════════════
        // SURFACE WIDTHS
        // ══════════════════════════════════════════════════════════
        //
        // What the seam extent is a percentage of. The width of a surface is how far across it
        // is between the seams bounding it — a rim strip's width, a wall's height, a channel's
        // width bank to bank, a tower stem's height — so one percentage shades a wide river and
        // a thin one, a tall wall and a short one, each in proportion to itself.
        //
        // Measured as twice the deepest any part of the surface lies from its nearest seam: the
        // middle of a strip is half its width from either edge. Sampled at every corner, and at
        // the middle of each face's longest side — a wall or a rim built as one quad from edge to
        // edge has every corner ON a seam, and the diagonal it is split along crosses its middle.
        private float[] SurfaceWidths(Topology mesh, int[] surface, List<SeamEdge> seam,
                                      float[] pointToSeam)
        {
            var widths = new float[mesh.Faces];
            if (seam.Count == 0) return widths;

            var deepest = new Dictionary<int, float>();
            for (int t = 0; t < mesh.Faces; t++)
            {
                int     ia = _tris[t * 3], ib = _tris[t * 3 + 1], ic = _tris[t * 3 + 2];
                Vector3 a  = _verts[ia],   b  = _verts[ib],       c  = _verts[ic];

                float far = Mathf.Max(pointToSeam[mesh.Id[ia]],
                            Mathf.Max(pointToSeam[mesh.Id[ib]], pointToSeam[mesh.Id[ic]]));

                float ab = (b - a).sqrMagnitude, bc = (c - b).sqrMagnitude, ca = (a - c).sqrMagnitude;
                Vector3 mid = ab >= bc && ab >= ca ? (a + b) * 0.5f
                            : bc >= ca             ? (b + c) * 0.5f
                                                   : (c + a) * 0.5f;

                float best = float.MaxValue;
                for (int k = 0; k < seam.Count; k++)
                    best = Mathf.Min(best, SeamDistance(seam[k], mid));
                far = Mathf.Max(far, best);

                deepest.TryGetValue(surface[t], out float held);
                if (far > held) deepest[surface[t]] = far;
            }

            for (int t = 0; t < mesh.Faces; t++)
                widths[t] = deepest.TryGetValue(surface[t], out float d) ? d * 2f : 0f;
            return widths;
        }

        private List<Vector3> SmoothedNormals(Topology mesh, int[] surfaceOf)
        {
            int Surface(int face) => surfaceOf[face];

            // One normal per point per surface, so a point standing on a seam still holds a
            // separate normal for each side of it. Weighted by the angle the face turns through
            // at that corner, so a quad split into two triangles does not count double along the
            // diagonal it was split on.
            var gathered = new Dictionary<long, Vector3>(_verts.Count);
            for (int t = 0; t < mesh.Faces; t++)
            {
                int     surface = Surface(t);
                Vector3 n       = _norms[_tris[t * 3]];

                for (int e = 0; e < 3; e++)
                {
                    int     i = _tris[t * 3 + e];
                    Vector3 p = _verts[i];
                    Vector3 u = _verts[_tris[t * 3 + (e + 1) % 3]] - p;
                    Vector3 v = _verts[_tris[t * 3 + (e + 2) % 3]] - p;

                    long key = ((long)mesh.Id[i] << 32) | (uint)surface;
                    gathered.TryGetValue(key, out Vector3 sum);
                    gathered[key] = sum + n * Vector3.Angle(u, v);
                }
            }

            var smoothed = new List<Vector3>(_norms);
            for (int t = 0; t < mesh.Faces; t++)
            {
                int surface = Surface(t);
                for (int e = 0; e < 3; e++)
                {
                    int     i   = _tris[t * 3 + e];
                    Vector3 sum = gathered[((long)mesh.Id[i] << 32) | (uint)surface];

                    // Faces meeting back to back cancel each other out. Nothing here builds that,
                    // but a normal of no length would be a hole in the lighting rather than a
                    // wrong answer, so the face's own is kept instead.
                    smoothed[i] = sum.sqrMagnitude > 1e-12f ? sum.normalized : _norms[i];
                }
            }

            return smoothed;
        }

        private struct SideFold
        {
            public int     count;
            public Vector3 normal;
            public bool    level;   // the first face of the fold lies flat — part of the rim lip
            public bool    sharp;

            /// <summary>The two faces the fold is between — the second is -1 while only one has
            /// been seen. What the smoothing walks along to find the faces that are one
            /// surface.</summary>
            public int     first, second;
        }

        /// <summary>One seam of the finished piece — the stretch of it between two points.</summary>
        private struct SeamEdge
        {
            public Vector3 on1, on2;
        }

        private static long SideKey(int a, int b)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        /// <summary>
        /// How far <paramref name="p"/> stands from the seam — from the STRETCH of it that is
        /// really there, not from the endless line it lies on. Measuring off the line let a
        /// seam draw on faces it ran nowhere near, because the line carried on out past both
        /// ends of the seam and swept across whatever stood beyond it. That showed up most on
        /// a pool, where every seam is a chord and its line cuts back across the bowl.
        /// </summary>
        private static float SeamDistance(SeamEdge seam, Vector3 p)
        {
            Vector3 along = seam.on2 - seam.on1;
            float   len2  = along.sqrMagnitude;
            if (len2 < 1e-12f) return Vector3.Distance(seam.on1, p);

            float t = Mathf.Clamp01(Vector3.Dot(p - seam.on1, along) / len2);
            return Vector3.Distance(seam.on1 + along * t, p);
        }

        /// <summary>
        /// The height of the rim top over this point. A run's rim rides its sweep, so the
        /// answer is the height of the nearest centre it was swept through, looked at from
        /// above; a pool holds its rim flat at zero and has no line to read.
        /// </summary>
        private float RimTopAt(Vector3 p)
        {
            if (_rimLine == null || _rimLine.Count == 0) return _rimTopY;

            float best = float.MaxValue, y = 0f;
            for (int i = 0; i < _rimLine.Count; i++)
            {
                Vector3 c  = _rimLine[i];
                float   dx = c.x - p.x, dz = c.z - p.z;
                float   dd = dx * dx + dz * dz;
                if (dd < best) { best = dd; y = c.y; }
            }
            return y;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_verts);

            // Stone works out three things from the one reading of its folds: where its seams
            // run, which part of the run each face is, and which of its corners are allowed to
            // stay hard. Water asks for none of it — it has its own use for both spare channels.
            var stone = _shadeSeams && !_hasEdges && _tris.Count > 0 ? new Topology(this) : null;

            List<SeamEdge>        seams    = null;
            Dictionary<long, int> seamOf   = null;
            int[]                 surfaces = null, closest = null;
            float[]               toSeam   = null;
            if (stone != null)
            {
                seams    = Seams(stone, out seamOf);
                surfaces = Surfaces(stone);
                NearestSeams(stone, seams, out closest, out toSeam);
            }

            mesh.SetNormals(stone != null ? SmoothedNormals(stone, surfaces) : _norms);
            mesh.SetUVs(0, _uvs);
            if      (_hasEdges)   mesh.SetUVs(1, _edges);
            else if (stone != null)
                mesh.SetUVs(1, SeamShading(stone, seams, seamOf, closest, toSeam));
            if      (_hasFlows)   mesh.SetUVs(2, _flows);
            else if (stone != null)
                mesh.SetUVs(2, FaceKinds(stone, SurfaceWidths(stone, surfaces, seams, toSeam)));
            if (_hasBanks) mesh.SetUVs(3, _banks);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Ear clipping for the end caps — the outline is simple, but not convex.</summary>
    private static List<int> Triangulate(List<Vector2> loop)
    {
        var tris = new List<int>();
        int n = loop.Count;
        if (n < 3) return tris;

        bool ccw   = SignedArea(loop) > 0f;
        var  index = new List<int>(n);
        for (int i = 0; i < n; i++) index.Add(ccw ? i : n - 1 - i);

        int guard = n * n;
        while (index.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < index.Count; i++)
            {
                int ia = index[(i + index.Count - 1) % index.Count];
                int ib = index[i];
                int ic = index[(i + 1) % index.Count];

                Vector2 a = loop[ia], b = loop[ib], c = loop[ic];
                if (Cross(b - a, c - b) <= 0f) continue;              // reflex corner

                bool clean = true;
                foreach (int other in index)
                {
                    if (other == ia || other == ib || other == ic) continue;
                    if (InTriangle(loop[other], a, b, c)) { clean = false; break; }
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

        // Ear clipping ran counter-clockwise; hand the winding back the way it came in.
        if (!ccw)
        {
            for (int i = 0; i < tris.Count; i += 3)
            {
                int swap    = tris[i];
                tris[i]     = tris[i + 2];
                tris[i + 2] = swap;
            }
        }
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

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d0 = Cross(b - a, p - a);
        float d1 = Cross(c - b, p - b);
        float d2 = Cross(a - c, p - c);
        return d0 >= 0f && d1 >= 0f && d2 >= 0f;
    }
}
