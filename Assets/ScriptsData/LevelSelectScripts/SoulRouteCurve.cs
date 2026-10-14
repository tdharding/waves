using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Turns a soul route's node ids into the line down the MIDDLE of the rivers it travels — the line
/// the soul zone is drawn along and the fish swim.
///
/// A route is authored as nodes, and a straight line node to node cuts every bend. The river the
/// player sees is the curve each generated run was swept along, kept on its
/// <see cref="RiverRunMesh"/>, so between two nodes the route follows that curve instead. Where
/// the route crosses a pool it comes in through one mouth, curves round the pool's channel ring
/// (the ring midway between island and rim) and leaves by the other mouth; an open pool with no
/// island is crossed on one smooth curve from mouth to mouth. A route that begins or ends in a pool
/// starts or stops on that ring, in line with its mouth — or in the middle of an open pool.
///
/// Where no generated run holds a stretch (never generated, or generated before runs recorded
/// their nodes) that stretch falls back to the straight node-to-node line, and says so.
/// </summary>
public static class SoulRouteCurve
{
    // World units between samples of the line. Fine enough that a pool ring or a tight bend
    // reads as a curve; the zone thins this out again when it packs the line for the shader.
    private const float Step = 0.1f;

    /// <summary>One generated run's curve, sampled into world space, and where each of its nodes falls on it.</summary>
    public class RunLine
    {
        public RiverRunMesh              run;
        public List<Vector3>             points    = new List<Vector3>();
        public Dictionary<string, int>   nodeIndex = new Dictionary<string, int>();
    }

    /// <summary>Samples every generated run's curve once. Runs with no recorded curve are skipped.</summary>
    public static List<RunLine> SampleRuns(IEnumerable<RiverRunMesh> runs)
    {
        var lines = new List<RunLine>();

        foreach (var run in runs)
        {
            if (run == null || run.knots == null || run.knots.Count < 2) continue;
            if (run.pathNodes == null || run.pathNodes.Count == 0) continue;

            var spline = run.ToSpline();
            float length = spline.GetLength();
            int   n      = Mathf.Max(8, Mathf.CeilToInt(length / Step));

            var line = new RunLine { run = run };
            for (int i = 0; i <= n; i++)
            {
                float3 p = SplineUtility.EvaluatePosition(spline, (float)i / n);
                line.points.Add(run.transform.TransformPoint(p));
            }

            // Recorded in the segment's space, and the run sits on its segment at identity.
            foreach (var node in run.pathNodes)
            {
                if (node == null || string.IsNullOrEmpty(node.nodeId)) continue;
                line.nodeIndex[node.nodeId] = Nearest(line.points, run.transform.TransformPoint(node.position));
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// The line down the middle of the river along <paramref name="ids"/>, in travel order.
    /// <paramref name="nodeVertex"/> comes back with the index in the line where each id stands —
    /// for a pool crossed on the way, the middle of its curve round the pool.
    /// </summary>
    public static List<Vector3> Build(
        LevelSelectDesignerData data, List<string> ids, List<RunLine> runs,
        IList<RiverPoolMesh> pools, out List<int> nodeVertex, List<string> problems)
    {
        var curve = new List<Vector3>();
        nodeVertex = new List<int>();
        if (ids == null || ids.Count == 0) return curve;

        for (int k = 0; k < ids.Count; k++) nodeVertex.Add(0);

        if (ids.Count == 1)
        {
            curve.Add(data.NodeWorldPosition(ids[0]));
            return curve;
        }

        for (int k = 0; k < ids.Count - 1; k++)
        {
            var stretch = Stretch(data, ids[k], ids[k + 1], runs, problems);

            var pool = data.PoolAt(ids[k]);
            if (pool != null)
            {
                PoolAt(data, pool, pools, out Vector3 centre, out float ring);
                Vector3 p1 = stretch[0];
                Vector3 d1 = stretch.Count >= 2 ? Flat(stretch[1] - stretch[0]) : Flat(p1 - centre);

                if (k == 0)
                {
                    // Setting off from a pool: on its ring in line with the mouth, or its middle.
                    PoolEnd(curve, centre, ring, p1, -d1, leaving: true);
                    nodeVertex[k] = 0;
                }
                else
                {
                    Vector3 p0 = curve[curve.Count - 1];
                    Vector3 d0 = curve.Count >= 2 ? Flat(p0 - curve[curve.Count - 2]) : Flat(centre - p0);
                    nodeVertex[k] = PoolCrossing(curve, centre, ring, p0, d0, p1, d1);
                }
            }
            else
            {
                nodeVertex[k] = Mathf.Max(0, curve.Count - 1);
            }

            Append(curve, stretch);
        }

        int last     = ids.Count - 1;
        var lastPool = data.PoolAt(ids[last]);
        if (lastPool != null && curve.Count > 0)
        {
            PoolAt(data, lastPool, pools, out Vector3 centre, out float ring);
            Vector3 p0 = curve[curve.Count - 1];
            Vector3 d0 = curve.Count >= 2 ? Flat(p0 - curve[curve.Count - 2]) : Flat(centre - p0);
            PoolEnd(curve, centre, ring, p0, d0, leaving: false);
        }
        nodeVertex[last] = curve.Count - 1;

        return curve;
    }

    /// <summary>
    /// Evenly spaced points along <paramref name="line"/>, <paramref name="spacing"/> apart, with
    /// the arc length to each. The last point is always the line's own end.
    /// </summary>
    public static void Resample(List<Vector3> line, List<float> cumArc, float spacing,
                                List<Vector3> outPoints, List<float> outArc)
    {
        outPoints.Clear();
        outArc.Clear();
        if (line.Count == 0) return;

        float total = cumArc[cumArc.Count - 1];
        int   n     = Mathf.Max(1, Mathf.CeilToInt(total / Mathf.Max(spacing, 0.0001f)));

        for (int i = 0; i <= n; i++)
        {
            float s = total * i / n;
            outPoints.Add(PointAt(line, cumArc, s));
            outArc.Add(s);
        }
    }

    /// <summary>The point <paramref name="s"/> world units along <paramref name="line"/>.</summary>
    public static Vector3 PointAt(List<Vector3> line, List<float> cumArc, float s)
    {
        if (line.Count == 1 || s <= 0f) return line[0];
        if (s >= cumArc[cumArc.Count - 1]) return line[line.Count - 1];

        int lo = 0, hi = cumArc.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (cumArc[mid] <= s) lo = mid; else hi = mid;
        }

        float span = cumArc[hi] - cumArc[lo];
        return Vector3.Lerp(line[lo], line[hi], span > 1e-6f ? (s - cumArc[lo]) / span : 0f);
    }

    // ─────────────────────────────────────────────
    // RIVER STRETCHES
    // ─────────────────────────────────────────────

    /// <summary>The run's own curve from node <paramref name="a"/> to node <paramref name="b"/>.</summary>
    private static List<Vector3> Stretch(LevelSelectDesignerData data, string a, string b,
                                         List<RunLine> runs, List<string> problems)
    {
        foreach (var line in runs)
        {
            if (!line.nodeIndex.TryGetValue(a, out int ia)) continue;
            if (!line.nodeIndex.TryGetValue(b, out int ib)) continue;

            var stretch = new List<Vector3>();
            int step = ib >= ia ? 1 : -1;
            for (int i = ia; ; i += step)
            {
                stretch.Add(line.points[i]);
                if (i == ib) break;
            }
            return stretch;
        }

        problems?.Add($"no generated run holds the river from '{a}' to '{b}' — that stretch is drawn " +
                      "straight node to node. Generate in the Level Select Designer.");
        return new List<Vector3> { data.NodeWorldPosition(a), data.NodeWorldPosition(b) };
    }

    private static void Append(List<Vector3> curve, List<Vector3> points)
    {
        foreach (var p in points)
        {
            if (curve.Count > 0 && FlatDistance(curve[curve.Count - 1], p) < Step * 0.25f) continue;
            curve.Add(p);
        }
    }

    // ─────────────────────────────────────────────
    // POOLS
    // ─────────────────────────────────────────────

    /// <summary>
    /// The pool's centre and channel ring as generated — off its <see cref="RiverPoolMesh"/> when
    /// one stands on the node, off the designer's pool shape otherwise.
    /// </summary>
    private static void PoolAt(LevelSelectDesignerData data, LevelSelectDesignerData.DesignerPool pool,
                               IList<RiverPoolMesh> pools, out Vector3 centre, out float ring)
    {
        centre = data.NodeWorldPosition(pool.nodeId);
        var shape = data.PoolShapeFor(pool);
        ring = RiverMeshBuilder.PoolChannelRadius(shape.poolRadius, shape.islandRadius);

        RiverPoolMesh best = null;
        float bestDist = shape.poolRadius;
        if (pools != null)
        {
            foreach (var p in pools)
            {
                if (p == null) continue;
                float d = FlatDistance(p.transform.position, centre);
                if (d < bestDist) { bestDist = d; best = p; }
            }
        }

        if (best != null)
        {
            centre = best.transform.position;
            ring   = best.ChannelRadius;
        }
    }

    /// <summary>
    /// In through one mouth and out through the other. Round the channel ring when the pool has an
    /// island, the shorter way round (anticlockwise when both ways are equal); one smooth curve
    /// across when it is an open bowl. Returns the index of the middle of the crossing.
    /// </summary>
    private static int PoolCrossing(List<Vector3> curve, Vector3 c, float ring,
                                    Vector3 p0, Vector3 d0, Vector3 p1, Vector3 d1)
    {
        if (ring <= 0.001f)
        {
            int start = curve.Count;
            Hermite(curve, p0, d0, p1, d1);
            return Mathf.Clamp((start + curve.Count - 1) / 2, 0, curve.Count - 1);
        }

        float a0    = Mathf.Atan2(p0.z - c.z, p0.x - c.x);
        float a1    = Mathf.Atan2(p1.z - c.z, p1.x - c.x);
        float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        float dir   = delta >= 0f ? 1f : -1f;
        float span  = Mathf.Abs(delta);

        // How far round the ring the curve in (and out) takes to join it: about as far as the
        // mouth stands off the ring, so it bends on rather than turning a corner — never more
        // than a third of the way, so there is always some ring left in the middle.
        float gapIn  = Mathf.Max(FlatDistance(p0, c) - ring, 0.01f);
        float gapOut = Mathf.Max(FlatDistance(p1, c) - ring, 0.01f);
        float leadIn  = Mathf.Min(gapIn  / ring, span / 3f);
        float leadOut = Mathf.Min(gapOut / ring, span / 3f);

        // Leave off a0 + delta, never a1 itself: a1 comes off Atan2 in -π..π, so when the way
        // round crosses the back of that range (a pool crossed straight over, mouths at ±90° but a
        // hair off) the arc below would lerp the LONG way, against both bends — the V at each mouth.
        float y = p0.y;
        float joinA  = a0 + dir * leadIn;
        float leaveA = a0 + delta - dir * leadOut;

        Hermite(curve, p0, d0, Ring(c, ring, joinA, y), RingTangent(joinA, dir));

        float arc   = Mathf.Abs(leaveA - joinA) * ring;
        int   steps = Mathf.Max(1, Mathf.CeilToInt(arc / Step));
        int   mid   = curve.Count - 1 + steps / 2;
        for (int i = 1; i <= steps; i++)
            curve.Add(Ring(c, ring, Mathf.Lerp(joinA, leaveA, (float)i / steps), y));

        Hermite(curve, curve[curve.Count - 1], RingTangent(leaveA, dir), p1, d1);

        return Mathf.Clamp(mid, 0, curve.Count - 1);
    }

    /// <summary>
    /// Setting off from, or stopping in, a pool: from its ring straight out along the mouth's
    /// line (or its middle, for an open bowl), and the same in reverse. <paramref name="mouth"/> is
    /// the river end at the rim and <paramref name="along"/> the way the river runs there, INTO
    /// the pool.
    /// </summary>
    private static void PoolEnd(List<Vector3> curve, Vector3 c, float ring,
                                Vector3 mouth, Vector3 along, bool leaving)
    {
        Vector3 outward = Flat(mouth - c);
        if (outward == Vector3.zero) outward = -along;

        Vector3 inside = ring > 0.001f ? c + outward * ring : c;
        inside.y = mouth.y;

        if (leaving)
        {
            curve.Add(inside);
            Hermite(curve, inside, outward, mouth, -along);
        }
        else
        {
            Hermite(curve, mouth, along, inside, -outward);
        }
    }

    private static Vector3 Ring(Vector3 c, float r, float a, float y)
        => new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r);

    private static Vector3 RingTangent(float a, float dir)
        => new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * dir;

    /// <summary>
    /// A curve from <paramref name="p0"/> heading <paramref name="d0"/> to <paramref name="p1"/>
    /// arriving heading <paramref name="d1"/>. The start is taken as already on the line.
    /// </summary>
    private static void Hermite(List<Vector3> curve, Vector3 p0, Vector3 d0, Vector3 p1, Vector3 d1)
    {
        float   len = Vector3.Distance(p0, p1);
        Vector3 m0  = Flat(d0) * len;
        Vector3 m1  = Flat(d1) * len;
        int     n   = Mathf.Max(4, Mathf.CeilToInt(len * 1.3f / Step));

        for (int i = 1; i <= n; i++)
        {
            float t  = (float)i / n;
            float t2 = t * t, t3 = t2 * t;
            curve.Add((2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m0 +
                      (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * m1);
        }
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-10f ? v.normalized : Vector3.zero;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static int Nearest(List<Vector3> points, Vector3 at)
    {
        int   best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            float d = FlatDistance(points[i], at);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}
