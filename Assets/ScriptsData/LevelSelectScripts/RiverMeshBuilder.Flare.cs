using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Outer Flare: a natural run's outer corner stretched out past the rim's outer edge and down
/// by the same, before the outer wall goes on down vertically (see <see cref="FlarePoints"/>).
///
/// The flare and the wall under it are the piece's SKIRT. Where two pieces meet, each keeps its
/// own skirt and the two simply run into each other: every piece is a closed solid, so whichever
/// skirt stands higher is the one drawn, and the line where they cross is the mitre between them.
/// What has to be cut is only what would stand up inside the other piece's channel:
///
/// - A run gives up its skirt across a junction mouth, cut straight down the branch's own two
///   side lines (<see cref="BuildRunSkirtFace"/>).
/// - The collar a mouth patch carries across, between the arriving run's end and the run or pool
///   it meets, carries the arriving run's skirt down both its sides (<see cref="BuildCollarSkirt"/>),
///   stopped at the edge of what it meets and closed there.
/// - A pool's skirt stops at each mouth's sides, along the radius, and is closed there
///   (<see cref="BuildPoolSkirt"/>, <see cref="BuildPoolSkirtEnd"/>).
/// </summary>
public static partial class RiverMeshBuilder
{
    // Closer than this to a cutting line, a point counts as on it.
    private const float SkirtCutWeld = 1e-6f;

    // ══════════════════════════════════════════════════════════════
    // RUN — the skirt across a junction mouth
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// One face of a run's skirt — a face of its outer flare, or of the outer wall under it —
    /// with whatever lies inside a branch's mouth cut away. A mouth takes the skirt between the
    /// branch's two side lines on its own side of the run, straight down; the branch's collar and
    /// its own run stand there instead.
    /// </summary>
    private static void BuildRunSkirtFace(
        MeshBuild b, List<RunMouth> mouths, RunGrid grid,
        Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3)
    {
        var face   = new List<Vector3> { c0, c1, c2, c3 };
        var pieces = new List<List<Vector3>> { face };

        if (mouths != null)
            foreach (var mouth in mouths)
            {
                var kept = new List<List<Vector3>>();
                foreach (var piece in pieces) KeepOutsideMouth(piece, mouth, grid, kept);
                pieces = kept;
            }

        if (pieces.Count == 1 && pieces[0] == face)
        {
            b.Quad(c0, c1, c2, c3);
            return;
        }

        foreach (var piece in pieces) EmitConvex(b, piece);
    }

    // The part of a convex face outside one mouth's slot — the slot being the strip between the
    // branch's two side lines, from the run's centreline out past where anything of this run
    // could stand. Kept as up to four convex pieces: beyond either side line, or within the strip
    // but behind the centreline or past its far end.
    private static void KeepOutsideMouth(
        List<Vector3> poly, RunMouth mouth, RunGrid grid, List<List<Vector3>> kept)
    {
        float hb   = mouth.halfOuter;
        float tMax = mouth.collar
                   + 4f * (grid.profile.OuterWidth + FlareReach(grid.profile) + mouth.halfOuter);

        bool allIn  = true;
        bool allPos = true, allNeg = true, allBehind = true, allPast = true;
        foreach (var p in poly)
        {
            float u = FlatDot(p - mouth.centre, mouth.right);
            float t = FlatDot(p - mouth.centre, mouth.dir);

            allPos    &= u >= hb;
            allNeg    &= u <= -hb;
            allBehind &= t <= 0f;
            allPast   &= t >= tMax;
            allIn     &= u > -hb && u < hb && t > 0f && t < tMax;
        }

        if (allPos || allNeg || allBehind || allPast) { kept.Add(poly); return; }
        if (allIn) return;

        AddPiece(kept, ClipFlat(poly, mouth.centre,  mouth.right, hb));
        AddPiece(kept, ClipFlat(poly, mouth.centre, -mouth.right, hb));

        var strip = ClipFlat(ClipFlat(poly, mouth.centre, mouth.right, -hb), mouth.centre, -mouth.right, -hb);
        AddPiece(kept, ClipFlat(strip, mouth.centre, -mouth.dir, 0f));
        AddPiece(kept, ClipFlat(strip, mouth.centre,  mouth.dir, tMax));
    }

    // ══════════════════════════════════════════════════════════════
    // THE COLLAR — the arriving run's skirt carried across
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// The skirt down one side of a mouth's collar: the arriving run's own flare and wall, swept
    /// straight along the collar's side from the run's open end (<paramref name="topRun"/>) to
    /// where the side meets the edge of the run or pool it opens into (<paramref name="topEdge"/>).
    /// At the run's end it is exactly the run's own last section, so the two meet ring for ring.
    ///
    /// Where the collar meets its host at a slant, the far end of this skirt would reach back in
    /// over the host's rim and stand up inside its channel. So it is stopped on the host's edge —
    /// the vertical plane through <paramref name="topEdge"/> facing <paramref name="hostOutward"/>
    /// — and closed there.
    /// </summary>
    private static void BuildCollarSkirt(
        MeshBuild b, Vector3 topRun, Vector3 topEdge, Vector3 outward, RiverProfile profile,
        float edge, float floorY, Vector3 hostOutward)
    {
        var flare = FlarePoints(CornersOf(profile), edge);
        if (flare.Count == 0) return;

        outward.y = 0f;
        outward.Normalize();
        hostOutward.y = 0f;
        if (hostOutward.sqrMagnitude < 1e-12f) hostOutward = outward;
        hostOutward.Normalize();

        // The run's edge stands at the rim's own fall there; the flare's points are measured
        // down from the rim top, so each is carried down from the edge by the difference.
        float edgeFall = SectionCut(profile, profile.OuterWidth * 0.5f);

        var a = SkirtSection(topRun,  outward, flare, edgeFall, floorY);
        var c = SkirtSection(topEdge, outward, flare, edgeFall, floorY);
        int n = flare.Count;

        for (int i = 0; i < a.Length - 1; i++)
        {
            // Flare faces look out and up, the wall out, the last face is the underside.
            Vector3 facing = i < n ? outward + Vector3.up : i == n ? outward : Vector3.down;

            var poly = ClipFlat(new List<Vector3> { a[i], a[i + 1], c[i + 1], c[i] },
                                topEdge, hostOutward, 0f);
            if (poly.Count >= 3) EmitFacing(b, poly, facing);
        }

        // The face it is stopped on, where the cut went through it: from the top down the flare,
        // down the wall and back under it, all in that one plane.
        var cap = new List<Vector3>();
        for (int i = 0; i < a.Length; i++)
        {
            float da = FlatDot(a[i] - topEdge, hostOutward);
            float dc = FlatDot(c[i] - topEdge, hostOutward);
            if (da < -SkirtCutWeld || dc > SkirtCutWeld) continue;
            if (da - dc < SkirtCutWeld) continue;
            cap.Add(Vector3.Lerp(a[i], c[i], da / (da - dc)));
        }
        if (cap.Count >= 3) EmitFacing(b, cap, -hostOutward);
    }

    // One cross-section of a skirt hung off a top point: the top, the flare's points from the
    // edge out to its foot, the wall's foot on the underside, and the underside under the top.
    private static Vector3[] SkirtSection(
        Vector3 top, Vector3 outward, List<Vector2> flare, float edgeFall, float floorY)
    {
        int n = flare.Count;
        var s = new Vector3[n + 3];
        s[0] = top;
        for (int k = 0; k < n; k++)
        {
            var f = flare[n - 1 - k];
            s[k + 1] = top + outward * f.x + Vector3.down * (f.y - edgeFall);
        }
        s[n + 1] = new Vector3(s[n].x, floorY, s[n].z);
        s[n + 2] = new Vector3(top.x,  floorY, top.z);
        return s;
    }

    // ══════════════════════════════════════════════════════════════
    // POOL
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// The pool's skirt between two points of its rim's outer edge, each carried straight out
    /// along its own radius: the flare's faces, then the wall from its foot to the underside.
    /// </summary>
    private static void BuildPoolSkirt(
        MeshBuild b, Vector3 topA, float angleA, Vector3 topB, float angleB,
        List<Vector2> flare, float outer, float floorY)
    {
        var a = PoolSkirtSection(topA, angleA, flare, outer, floorY);
        var c = PoolSkirtSection(topB, angleB, flare, outer, floorY);
        int n = flare.Count;

        Vector3 radial = PoolDirection((angleA + angleA + DeltaRad(angleA, angleB)) * 0.5f);

        b.NextLabel = "Outer Flare";
        for (int i = 0; i < n; i++)
            FacingQuad(b, a[i], a[i + 1], c[i + 1], c[i], radial + Vector3.up);

        b.NextLabel = "Outer Wall";
        FacingQuad(b, a[n], a[n + 1], c[n + 1], c[n], radial);
    }

    /// <summary>
    /// Closes the pool's skirt where it stops at a mouth's side — the flare and wall as seen
    /// cut along the radius, facing round into the mouth.
    /// </summary>
    private static void BuildPoolSkirtEnd(
        MeshBuild b, Vector3 top, float angle, float towardsAngle,
        List<Vector2> flare, float outer, float floorY)
    {
        var s    = PoolSkirtSection(top, angle, flare, outer, floorY);
        var poly = new List<Vector3>(s) { new Vector3(top.x, floorY, top.z) };

        Vector3 round = new Vector3(Mathf.Cos(angle), 0f, -Mathf.Sin(angle));
        if (DeltaRad(angle, towardsAngle) < 0f) round = -round;

        b.NextLabel = "Outer Flare";
        EmitFacing(b, poly, round);
    }

    // A pool skirt's cross-section along one radius: the rim's edge point, the flare's points out
    // to its foot, and the wall's foot on the underside.
    private static Vector3[] PoolSkirtSection(
        Vector3 top, float angle, List<Vector2> flare, float outer, float floorY)
    {
        int     n   = flare.Count;
        Vector3 dir = PoolDirection(angle);
        var     s   = new Vector3[n + 2];
        s[0] = top;
        for (int k = 0; k < n; k++)
        {
            var f = flare[n - 1 - k];
            s[k + 1] = dir * (outer + f.x) + Vector3.up * -f.y;
        }
        s[n + 1] = new Vector3(s[n].x, floorY, s[n].z);
        return s;
    }

    // ══════════════════════════════════════════════════════════════
    // CUTTING
    // ══════════════════════════════════════════════════════════════

    private static float FlatDot(Vector3 v, Vector3 d) => v.x * d.x + v.z * d.z;

    /// <summary>
    /// The part of a convex outline where (p - origin)·normal, seen from above, is at least
    /// <paramref name="offset"/> — the outline cut by a vertical plane, in the order it was wound.
    /// A crossing is worked out from its two ends in a fixed order, so the face either side of a
    /// shared edge gets the very same point.
    /// </summary>
    private static List<Vector3> ClipFlat(List<Vector3> poly, Vector3 origin, Vector3 normal, float offset)
    {
        var kept = new List<Vector3>(poly.Count + 2);
        int n    = poly.Count;
        if (n == 0) return kept;

        for (int k = 0; k < n; k++)
        {
            Vector3 p = poly[k], q = poly[(k + 1) % n];
            float dp = FlatDot(p - origin, normal) - offset;
            float dq = FlatDot(q - origin, normal) - offset;

            if (dp >= 0f) kept.Add(p);
            if ((dp >= 0f) == (dq >= 0f)) continue;

            // Always from the same end, whichever way round the face meets the edge.
            bool    pFirst = p.x < q.x || (p.x == q.x && (p.z < q.z || (p.z == q.z && p.y <= q.y)));
            Vector3 from   = pFirst ? p : q, to = pFirst ? q : p;
            float   dFrom  = pFirst ? dp : dq, dTo = pFirst ? dq : dp;
            kept.Add(Vector3.Lerp(from, to, dFrom / (dFrom - dTo)));
        }

        // A plane through a corner lands its crossing on top of it.
        for (int k = kept.Count - 1; k >= 0 && kept.Count > 0; k--)
        {
            int prev = (k + kept.Count - 1) % kept.Count;
            if (prev != k && (kept[k] - kept[prev]).sqrMagnitude < CutWeld * CutWeld)
                kept.RemoveAt(k);
        }
        return kept;
    }

    // Keeps a cut piece only if it still has an area to it.
    private static void AddPiece(List<List<Vector3>> pieces, List<Vector3> poly)
    {
        if (poly.Count < 3) return;

        Vector3 n = Vector3.zero;
        for (int k = 1; k < poly.Count - 1; k++)
            n += Vector3.Cross(poly[k] - poly[0], poly[k + 1] - poly[0]);
        if (n.sqrMagnitude < 1e-16f) return;

        pieces.Add(poly);
    }

    // A convex outline filled facing the given way, whichever way round it was listed.
    private static void EmitFacing(MeshBuild b, List<Vector3> poly, Vector3 facing)
    {
        if (poly.Count < 3) return;

        Vector3 n = Vector3.zero;
        for (int k = 1; k < poly.Count - 1; k++)
            n += Vector3.Cross(poly[k] - poly[0], poly[k + 1] - poly[0]);

        if (Vector3.Dot(n, facing) < 0f) poly.Reverse();
        EmitConvex(b, poly);
    }
}
