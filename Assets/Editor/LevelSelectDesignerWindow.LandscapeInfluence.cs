using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Landscape Influence — a path's river run shaping the landscape round it.
///
/// With Landscape to Outer Rim on, the ground is drawn up to just under the run's rim top along
/// its outer edge and eases back to the hills over the path's Influence Distance. The hills are
/// raised in the shader, so this is done there too (CalculateHills.hlsl): every run and pool is
/// traced here as a line of points along its centreline, carrying its half outer width, and
/// handed to <see cref="LandscapeTool"/>.
///
/// The tile grid is far coarser than a rim is wide, so vertices alone can never meet the edge.
/// Instead the ground is held flat at the rim height right across the run, and the RiverEdgeCut
/// subgraph cuts it away inside every run and pool, to the pixel. That is why Landscape Natural
/// runs whose path has no influence are traced too — cut only, never lifted — so raised ground
/// running up to a junction or a pool never covers that river's channel. Structural runs and
/// pools are left out unless they lift the ground themselves. Nothing is traced at all while no
/// path has Landscape Influence on, so the landscape is exactly as it was until one does.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // How far a traced line may stray from the run it follows. Doubled until everything fits in
    // the shader's points, with a note in the console when it has to be.
    private const float RiverEdgeTolerance = 0.02f;

    // The river's Landscape Influence dropdown; closed each time the window opens.
    [System.NonSerialized] private bool _foldPathLandscapeInfluence;

    private class RiverEdgeLine
    {
        public List<Vector3> points;
        public float         half;
        public List<float>   halves;  // one per point where a width override changes it; else null
        public float         reach;   // negative: cut only
        public float         strength = 1f;
        public float         shelf    = -1f;   // walls-off run: held level this far past the edge
        public float         cutGap   = -LandscapeHillHeight.RiverEdgeTuck;
        public float         smooth;
    }

    /// <summary>
    /// The path's Landscape Influence settings, under its other properties. Returns true when one
    /// changed, so the landscape is re-traced.
    /// </summary>
    private bool DrawPathLandscapeInfluence(LevelSelectDesignerData.DesignerPath path)
    {
        EditorGUILayout.Space(4);
        _foldPathLandscapeInfluence = EditorGUILayout.Foldout(
            _foldPathLandscapeInfluence, "Landscape Influence Settings", true);
        if (!_foldPathLandscapeInfluence) return false;

        EditorGUI.BeginChangeCheck();
        EditorGUI.indentLevel++;

        path.landscapeInfluence = EditorGUILayout.Toggle(
            new GUIContent("Landscape Influence",
                "Lets this path's river run shape the landscape around it."),
            path.landscapeInfluence);

        path.cutoutGap = EditorGUILayout.FloatField(
            new GUIContent("Cutout Gap",
                "How far out past the run's outer edge the landscape is cut away, leaving a gap " +
                "round it. Negative tucks the landscape in under the rim instead (the default, " +
                $"{-LandscapeHillHeight.RiverEdgeTuck}, hides the seam)."),
            path.cutoutGap);

        if (path.landscapeInfluence)
        {
            EditorGUI.indentLevel++;
            path.landscapeToOuterRim = EditorGUILayout.Toggle(
                new GUIContent("Landscape to Outer Rim",
                    "Brings the landscape up to the rim top along the run's outer edge."),
                path.landscapeToOuterRim);

            using (new EditorGUI.DisabledScope(!path.landscapeToOuterRim))
            {
                path.influenceDistance = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Influence Distance",
                        "How far out past the run's outer edge the landscape is shaped, before " +
                        "it eases back to its own hills."),
                    path.influenceDistance));

                path.influenceStrength = EditorGUILayout.Slider(
                    new GUIContent("Influence Strength",
                        "How far the landscape is drawn up toward the rim top: 1 meets it, 0.5 " +
                        "goes halfway, 0 leaves the hills as they are."),
                    path.influenceStrength, 0f, 1f);

                path.influenceVerticalOffset = EditorGUILayout.FloatField(
                    new GUIContent("Vertical Offset",
                        "Moves the influenced landscape up (positive) or down (negative) from the " +
                        "height it is drawn to, in metres."),
                    path.influenceVerticalOffset);

                path.influenceSmoothFactor = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Smooth Factor",
                        "Calms the hills' rocky noise further out round the influenced area: it " +
                        "is eased away over the Influence Distance times (1 + this). 0 calms it " +
                        "only as far as the landscape is lifted."),
                    path.influenceSmoothFactor));

                path.influenceCancelledNodes ??= new List<string>();
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Cancel Influence At", EditorStyles.miniBoldLabel);
                for (int i = 0; i < path.nodeIds.Count; i++)
                {
                    string id        = path.nodeIds[i];
                    bool   cancelled = path.influenceCancelledNodes.Contains(id);
                    bool   now       = EditorGUILayout.Toggle(
                        new GUIContent($"Node {i + 1}",
                            "Cancels the influence on the stretch of run nearest this node — " +
                            "cut only, never lifted."),
                        cancelled);

                    if (now == cancelled) continue;
                    if (now) path.influenceCancelledNodes.Add(id);
                    else     path.influenceCancelledNodes.Remove(id);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.indentLevel--;

        return EditorGUI.EndChangeCheck();
    }

    /// <summary>
    /// Traces every generated run and pool into the landscape, as they stand now. Called after a
    /// Generate, a run or pool rebuild, and any change to a path's Landscape Influence.
    /// </summary>
    private void RefreshLandscapeInfluence(bool placeSpikes = true)
    {
        if (_data == null) return;

        var tool = FindLandscapeTool();
        if (tool == null) return;

        var lines = new List<RiverEdgeLine>();
        bool any  = _data.paths.Exists(p => p != null && p.landscapeInfluence && p.landscapeToOuterRim);
        if (any)
        {
            TraceRuns(lines);
            TracePools(lines);
        }

        PackRiverEdges(lines, out var points, out var reach, out var strength, out var shelf,
                       out var cutGap, out var smooth);

        tool.SetRiverEdges(points, reach, strength, shelf, cutGap, smooth);
        EditorUtility.SetDirty(tool);
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();

        // Spikes and structures stand on the ground, so they rise with it.
        if (placeSpikes)
        {
            PlaceAllSpikes();
            PlaceAllNomKabarTrees();
            PlaceAllStructures();
        }

        if (any)
            Debug.Log($"[LevelSelectDesigner] Landscape influence: {lines.Count(l => l.reach >= 0f)} " +
                      $"lifted, {lines.Count} traced, {points.Count}/{LandscapeTool.MaxRiverEdgePoints} points.");
    }

    private LandscapeTool FindLandscapeTool()
    {
        var container = FindHillContainer();
        var tool      = container != null ? container.parent.GetComponent<LandscapeTool>() : null;
        return tool != null ? tool : FindObjectOfType<LandscapeTool>();
    }

    // Each run along the same curve it is swept on, carried into its arena as the mesh is.
    private void TraceRuns(List<RiverEdgeLine> lines)
    {
        float landscapeShelf = LandscapeShelf(FindLandscapeTool());

        foreach (var record in FindObjectsOfType<RiverRunMesh>())
        {
            if (record.knots.Count < 2) continue;

            // A Structural River Run isn't cut out of the landscape unless it lifts the ground.
            var path = PathForRun(record);
            bool lifts = path != null && path.landscapeInfluence && path.landscapeToOuterRim;
            if (!lifts && !IsNaturalRun(path)) continue;

            var profile = RunProfileFor(record);
            if (!SampleRun(record.ToSpline(), MeshEdge, profile, ToNotches(record.mouths),
                           out var centres, out var forwards)) continue;

            FitRunToArena(centres, forwards,
                          ArenaReachAt(record.arenaAtStart), ArenaReachAt(record.arenaAtEnd), MeshEdge);

            var world = centres.Select(c => record.transform.TransformPoint(c)).ToList();

            // Half the outer width at each point, where a width override changes it along the run.
            var widths = RiverMeshBuilder.InnerWidthsAlong(profile, centres);
            var halves = widths?.Select(w => w * 0.5f + profile.rimWidth).ToList();

            // A branch's run stops a collar clear of the river it leaves, and the mouth between
            // belongs to that river's mesh. Carry the line back to that river's centreline so
            // the ground is cut over the mouth too.
            if (!string.IsNullOrEmpty(record.joinRiver))
            {
                Vector3 join = record.transform.TransformPoint(record.joinPoint);
                join.y = world[0].y;
                world.Insert(0, join);
                halves?.Insert(0, halves[0]);
            }

            float half  = profile.OuterWidth * 0.5f;
            float reach = lifts ? Mathf.Max(0f, path.influenceDistance) : -1f;
            float strength = lifts ? Mathf.Clamp01(path.influenceStrength) : 1f;
            float smooth   = lifts ? Mathf.Max(0f, path.influenceSmoothFactor) : 0f;

            // A point's height is only what the ground is lifted to, so the Vertical Offset moves
            // the lifted ground by moving the points; the cut ignores height.
            if (lifts && path.influenceVerticalOffset != 0f)
                for (int i = 0; i < world.Count; i++) world[i] += Vector3.up * path.influenceVerticalOffset;

            // A run with no outer walls has the ground run straight onto its rim.
            float shelf = RunWallsOff(path) ? landscapeShelf : -1f;
            float cutGap = path != null ? path.cutoutGap : -LandscapeHillHeight.RiverEdgeTuck;

            if (!lifts || path.influenceCancelledNodes == null || path.influenceCancelledNodes.Count == 0)
            {
                lines.Add(new RiverEdgeLine { points = world, half = half, halves = halves, reach = reach, strength = strength, shelf = shelf, cutGap = cutGap, smooth = smooth });
                continue;
            }

            // Each point belongs to the path node it lies nearest; where that node's influence is
            // cancelled the run is cut only. The line is split where that changes, the two halves
            // sharing the point so the cut runs on unbroken; the lifted half eases off round its end.
            var nodes = path.nodeIds
                .Select(id => _data.nodes.Find(n => n.id == id))
                .Where(n => n != null)
                .ToList();

            bool Cancelled(Vector3 p)
            {
                LevelSelectDesignerData.DesignerNode nearest = null;
                float best = float.MaxValue;
                foreach (var n in nodes)
                {
                    float dx = n.worldPosition.x - p.x, dz = n.worldPosition.z - p.z;
                    float d  = dx * dx + dz * dz;
                    if (d < best) { best = d; nearest = n; }
                }
                return nearest != null && path.influenceCancelledNodes.Contains(nearest.id);
            }

            int  start = 0;
            bool off   = Cancelled(world[0]);
            for (int i = 1; i <= world.Count; i++)
            {
                bool here = i < world.Count ? Cancelled(world[i]) : !off;
                if (here == off) continue;

                int end = Mathf.Min(i, world.Count - 1);
                lines.Add(new RiverEdgeLine
                {
                    points = world.GetRange(start, end - start + 1),
                    half   = half,
                    halves = halves?.GetRange(start, end - start + 1),
                    reach  = off ? -1f : reach,
                    strength = strength,
                    shelf    = shelf,
                    cutGap   = cutGap,
                    smooth   = smooth,
                });
                start = end;
                off   = here;
            }
        }
    }

    // A pool is a disc: its centre and outer radius. A pool has no path of its own, so a
    // Landscape Natural River Run pool lifts the ground by its river's path, as that river's
    // runs do; any other natural pool is cut only, and a structural one isn't cut at all.
    private void TracePools(List<RiverEdgeLine> lines)
    {
        float landscapeShelf = LandscapeShelf(FindLandscapeTool());

        foreach (var record in FindObjectsOfType<RiverPoolMesh>())
        {
            var  pool    = PoolForRecord(record);
            var  path    = pool != null ? PoolPath(pool) : PathForRiver(record.riverName);
            if (!IsNaturalRun(path)) continue;

            var  profile = RunProfileFor(path, record.riverName);
            bool lifts   = path.landscapeInfluence && path.landscapeToOuterRim;

            lines.Add(new RiverEdgeLine
            {
                points   = new List<Vector3>
                {
                    record.transform.position + Vector3.up * (lifts ? path.influenceVerticalOffset : 0f),
                },
                half     = record.poolRadius + profile.rimWidth,
                reach    = lifts ? Mathf.Max(0f, path.influenceDistance) : -1f,
                strength = lifts ? Mathf.Clamp01(path.influenceStrength) : 1f,
                shelf    = RunWallsOff(path) ? landscapeShelf : -1f,
                cutGap   = path != null ? path.cutoutGap : -LandscapeHillHeight.RiverEdgeTuck,
                smooth   = lifts ? Mathf.Max(0f, path.influenceSmoothFactor) : 0f,
            });
        }
    }

    /// <summary>
    /// The designer path a run was built from. A path cut into legs at its pools gives each leg
    /// its own id with a '#pool' suffix; runs generated before the id was recorded are matched
    /// by their first two nodes instead.
    /// </summary>
    private LevelSelectDesignerData.DesignerPath PathForRun(RiverRunMesh record)
    {
        if (!string.IsNullOrEmpty(record.pathId))
        {
            string id  = record.pathId;
            int    cut = id.IndexOf("#pool");
            if (cut >= 0) id = id.Substring(0, cut);

            var byId = _data.paths.Find(p => p != null && p.pathId == id);
            if (byId != null) return byId;
        }

        if (record.pathNodes == null || record.pathNodes.Count < 2) return null;
        string a = record.pathNodes[0].nodeId, b = record.pathNodes[1].nodeId;

        return _data.paths.Find(p =>
        {
            if (p == null) return false;
            int i = p.nodeIds.IndexOf(a);
            return i >= 0 && i + 1 < p.nodeIds.Count && p.nodeIds[i + 1] == b;
        });
    }

    // Into the shader's layout (see LandscapeTool.riverEdgePoints), thinned until it fits.
    private static void PackRiverEdges(List<RiverEdgeLine> lines,
                                       out List<Vector4> points, out List<float> reach,
                                       out List<float> strength, out List<float> shelf,
                                       out List<float> cutGap, out List<float> smooth)
    {
        points   = new List<Vector4>();
        reach    = new List<float>();
        strength = new List<float>();
        shelf    = new List<float>();
        cutGap   = new List<float>();
        smooth   = new List<float>();

        float tolerance = RiverEdgeTolerance;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            points.Clear();
            reach.Clear();
            strength.Clear();
            shelf.Clear();
            cutGap.Clear();
            smooth.Clear();

            foreach (var line in lines)
            {
                var keep = line.points.Count > 2
                         ? ThinIndices(line.points, line.halves, tolerance)
                         : Enumerable.Range(0, line.points.Count).ToList();
                for (int i = 0; i < keep.Count; i++)
                {
                    Vector3 p    = line.points[keep[i]];
                    float   half = line.halves != null ? line.halves[keep[i]] : line.half;
                    float   w    = i == keep.Count - 1 ? -half : half;
                    points.Add(new Vector4(p.x, p.y, p.z, w));
                    reach.Add(line.reach);
                    strength.Add(line.strength);
                    shelf.Add(line.shelf);
                    cutGap.Add(line.cutGap);
                    smooth.Add(line.smooth);
                }
            }

            if (points.Count <= LandscapeTool.MaxRiverEdgePoints) break;
            tolerance *= 2f;
        }

        if (tolerance > RiverEdgeTolerance)
            Debug.Log($"[LevelSelectDesigner] Landscape influence: river edges traced to within " +
                      $"{tolerance:F2} to fit the shader's {LandscapeTool.MaxRiverEdgePoints} points.");

        if (points.Count > LandscapeTool.MaxRiverEdgePoints)
        {
            Debug.LogWarning($"[LevelSelectDesigner] Landscape influence: {points.Count} river edge " +
                             $"points, only the first {LandscapeTool.MaxRiverEdgePoints} are used.");
            points.RemoveRange(LandscapeTool.MaxRiverEdgePoints, points.Count - LandscapeTool.MaxRiverEdgePoints);
            reach.RemoveRange(LandscapeTool.MaxRiverEdgePoints, reach.Count - LandscapeTool.MaxRiverEdgePoints);
            strength.RemoveRange(LandscapeTool.MaxRiverEdgePoints, strength.Count - LandscapeTool.MaxRiverEdgePoints);
            shelf.RemoveRange(LandscapeTool.MaxRiverEdgePoints, shelf.Count - LandscapeTool.MaxRiverEdgePoints);
            cutGap.RemoveRange(LandscapeTool.MaxRiverEdgePoints, cutGap.Count - LandscapeTool.MaxRiverEdgePoints);
            smooth.RemoveRange(LandscapeTool.MaxRiverEdgePoints, smooth.Count - LandscapeTool.MaxRiverEdgePoints);
            var last = points[points.Count - 1];
            points[points.Count - 1] = new Vector4(last.x, last.y, last.z, -Mathf.Abs(last.w));
        }
    }

    // Keeps only the points needed to stay within tolerance of the line — straight stretches
    // collapse to their ends, bends keep their points. Ends always kept. With a half width at each
    // point, one whose width the kept ends either side would not give it is kept too.
    private static List<int> ThinIndices(List<Vector3> line, List<float> halves, float tolerance)
    {
        var keep = new bool[line.Count];
        keep[0] = keep[line.Count - 1] = true;

        var stack = new Stack<(int, int)>();
        stack.Push((0, line.Count - 1));
        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            float worst = 0f;
            int   at    = -1;
            for (int i = from + 1; i < to; i++)
            {
                float d = DistanceToSegment(line[i], line[from], line[to]);
                if (halves != null)
                {
                    // How far the width read off the ends either side misses this point's own.
                    Vector3 ab = line[to] - line[from]; ab.y = 0f;
                    Vector3 ap = line[i] - line[from];  ap.y = 0f;
                    float   t  = ab.sqrMagnitude < 1e-12f ? 0f
                               : Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude);
                    d = Mathf.Max(d, Mathf.Abs(halves[i] - Mathf.Lerp(halves[from], halves[to], t)));
                }
                if (d > worst) { worst = d; at = i; }
            }
            if (at < 0 || worst <= tolerance) continue;

            keep[at] = true;
            stack.Push((from, at));
            stack.Push((at, to));
        }

        var thin = new List<int>();
        for (int i = 0; i < line.Count; i++) if (keep[i]) thin.Add(i);
        return thin;
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab   = b - a;
        float   len2 = ab.sqrMagnitude;
        float   t    = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }
}

#endif
