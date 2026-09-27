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
/// subgraph cuts it away inside every run and pool, to the pixel. That is why runs whose path
/// has no influence are traced too — cut only, never lifted — so raised ground running up to a
/// junction or a pool never covers that river's channel. Nothing is traced at all while no
/// path has Landscape Influence on, so the landscape is exactly as it was until one does.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // How far a traced line may stray from the run it follows. Doubled until everything fits in
    // the shader's points, with a note in the console when it has to be.
    private const float RiverEdgeTolerance = 0.02f;

    private class RiverEdgeLine
    {
        public List<Vector3> points;
        public float         half;
        public float         reach;   // negative: cut only
    }

    /// <summary>
    /// The path's Landscape Influence settings, under its other properties. Returns true when one
    /// changed, so the landscape is re-traced.
    /// </summary>
    private bool DrawPathLandscapeInfluence(LevelSelectDesignerData.DesignerPath path)
    {
        EditorGUILayout.Space(4);
        EditorGUI.BeginChangeCheck();

        path.landscapeInfluence = EditorGUILayout.Toggle(
            new GUIContent("Landscape Influence",
                "Lets this path's river run shape the landscape around it."),
            path.landscapeInfluence);

        if (path.landscapeInfluence)
        {
            EditorGUI.indentLevel++;
            path.landscapeToOuterRim = EditorGUILayout.Toggle(
                new GUIContent("Landscape to Outer Rim",
                    "Brings the landscape up to the rim top along the run's outer edge."),
                path.landscapeToOuterRim);

            using (new EditorGUI.DisabledScope(!path.landscapeToOuterRim))
                path.influenceDistance = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Influence Distance",
                        "How far out past the run's outer edge the landscape is shaped, before " +
                        "it eases back to its own hills."),
                    path.influenceDistance));
            EditorGUI.indentLevel--;
        }

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

        PackRiverEdges(lines, out var points, out var reach);

        tool.SetRiverEdges(points, reach);
        EditorUtility.SetDirty(tool);
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();

        // Spikes stand on the ground, so they rise with it.
        if (placeSpikes) PlaceAllSpikes();

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
        foreach (var record in FindObjectsOfType<RiverRunMesh>())
        {
            if (record.knots.Count < 2) continue;

            var profile = _data.ProfileFor(record.riverName);
            if (!SampleRun(record.ToSpline(), MeshEdge, profile, ToNotches(record.mouths),
                           out var centres, out var forwards)) continue;

            FitRunToArena(centres, forwards,
                          ArenaReachAt(record.arenaAtStart), ArenaReachAt(record.arenaAtEnd), MeshEdge);

            var world = centres.Select(c => record.transform.TransformPoint(c)).ToList();

            // A branch's run stops a collar clear of the river it leaves, and the mouth between
            // belongs to that river's mesh. Carry the line back to that river's centreline so
            // the ground is cut over the mouth too.
            if (!string.IsNullOrEmpty(record.joinRiver))
            {
                Vector3 join = record.transform.TransformPoint(record.joinPoint);
                join.y = world[0].y;
                world.Insert(0, join);
            }

            var path = PathForRun(record);
            bool lifts = path != null && path.landscapeInfluence && path.landscapeToOuterRim;

            lines.Add(new RiverEdgeLine
            {
                points = world,
                half   = profile.OuterWidth * 0.5f,
                reach  = lifts ? Mathf.Max(0f, path.influenceDistance) : -1f,
            });
        }
    }

    // A pool is a disc: its centre and outer radius. Cut only — a pool has no path of its own.
    private void TracePools(List<RiverEdgeLine> lines)
    {
        foreach (var record in FindObjectsOfType<RiverPoolMesh>())
        {
            var profile = _data.ProfileFor(record.riverName);
            lines.Add(new RiverEdgeLine
            {
                points = new List<Vector3> { record.transform.position },
                half   = record.poolRadius + profile.rimWidth,
                reach  = -1f,
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
                                       out List<Vector4> points, out List<float> reach)
    {
        points = new List<Vector4>();
        reach  = new List<float>();

        float tolerance = RiverEdgeTolerance;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            points.Clear();
            reach.Clear();

            foreach (var line in lines)
            {
                var thin = line.points.Count > 2 ? Thin(line.points, tolerance) : line.points;
                for (int i = 0; i < thin.Count; i++)
                {
                    Vector3 p = thin[i];
                    float   w = i == thin.Count - 1 ? -line.half : line.half;
                    points.Add(new Vector4(p.x, p.y, p.z, w));
                    reach.Add(line.reach);
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
            var last = points[points.Count - 1];
            points[points.Count - 1] = new Vector4(last.x, last.y, last.z, -Mathf.Abs(last.w));
        }
    }

    // Keeps only the points needed to stay within tolerance of the line — straight stretches
    // collapse to their ends, bends keep their points. Ends always kept.
    private static List<Vector3> Thin(List<Vector3> line, float tolerance)
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
                if (d > worst) { worst = d; at = i; }
            }
            if (at < 0 || worst <= tolerance) continue;

            keep[at] = true;
            stack.Push((from, at));
            stack.Push((at, to));
        }

        var thin = new List<Vector3>();
        for (int i = 0; i < line.Count; i++) if (keep[i]) thin.Add(line[i]);
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
