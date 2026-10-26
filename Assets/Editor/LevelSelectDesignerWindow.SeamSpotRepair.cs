using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// ONE-OFF — delete this file once it has been run and saved. Seam Clean Up spots made before wall
/// nodes had ids remembered their node by its place in the list, so nodes added before them since
/// left them on the wrong node, Not Found. This tries every Not Found spot a few places either
/// way, the shift most spots agree on first, and puts each back on the node where it lands on a
/// seam (or face) again — then ties it to that node's id. Spots already found are left alone.
/// Everything it does, and how far off anything still lost is, goes to the Console / Editor.log.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // How many places either way a spot's node is looked for.
    const int RepairReach = 6;

    [MenuItem("Tools/Level Design/Seam Clean Up - Repair Shifted Spots (one-off)", false, 105)]
    static void RepairShiftedSeamSpotsMenu()
    {
        var designer = FindOpen();
        if (designer == null || !designer.HasData)
        {
            Debug.LogWarning("[SeamSpotRepair] Open the Level Select Designer first.");
            return;
        }
        designer.RepairShiftedSeamSpots();
    }

    private void RepairShiftedSeamSpots()
    {
        Undo.RecordObject(_data, "Repair Seam Spots");
        bool any = false;
        foreach (var wall in _data.walls)
            if (wall != null && wall.nodes.Count >= 2 && RepairWallSpots(wall)) any = true;

        if (any) MarkDirty();
        Debug.Log(any ? "[SeamSpotRepair] Done — Save the designer to keep it." : "[SeamSpotRepair] Done — nothing moved.");
        Repaint();
    }

    private bool RepairWallSpots(LevelSelectDesignerData.DesignerWall wall)
    {
        wall.SyncSpotNodes();
        var seams = wall.removedSeams ?? new List<WallSeamSpot>();
        var faces = new List<WallSeamSpot>();
        foreach (var group in wall.faceGroups ?? new List<WallFaceGroup>())
            if (group?.surfaces != null) faces.AddRange(group.surfaces);
        if (seams.Count == 0 && faces.Count == 0) return false;

        int nodeCount = wall.nodes.Count;
        var shifts    = Enumerable.Range(-RepairReach, RepairReach * 2 + 1).ToList();
        int zero      = shifts.IndexOf(0);

        // Every spot at every shift, in one build: shift k's seam spots one after another, its
        // face spots as one group.
        var testSeams  = new List<WallSeamSpot>();
        var testGroups = new List<WallFaceGroup>();
        foreach (int shift in shifts)
        {
            foreach (var s in seams) testSeams.Add(Shifted(s, shift, nodeCount));
            var group = new WallFaceGroup();
            foreach (var s in faces) group.surfaces.Add(Shifted(s, shift, nodeCount));
            testGroups.Add(group);
        }

        var nodes  = WallNodes(wall);
        var report = new WallMesh.SeamReport();
        foreach (var mesh in WallMesh.BuildChunks(nodes, wall.closed, wall.thickness, -_data.RunDepth, StoneShadingForBuild,
                                                  testSeams, report, testGroups, WallLipFor(wall)))
            DestroyImmediate(mesh);

        bool SeamFound(int k, int i) => report.found[k * seams.Count + i];
        bool FaceFound(int k, int j) => report.groupFound[k][j];

        // The shifts most spots land on, first; nearer shifts first between equals.
        var score = new int[shifts.Count];
        for (int k = 0; k < shifts.Count; k++)
        {
            for (int i = 0; i < seams.Count; i++) if (SeamFound(k, i)) score[k]++;
            for (int j = 0; j < faces.Count; j++) if (FaceFound(k, j)) score[k]++;
        }
        var order = Enumerable.Range(0, shifts.Count)
                              .OrderByDescending(k => score[k]).ThenBy(k => Mathf.Abs(shifts[k])).ToList();

        var log = new StringBuilder();
        log.AppendLine($"[SeamSpotRepair] {WallMeshName(wall)} — {seams.Count} removed seams, {faces.Count} face group spots");
        log.AppendLine("  spots landing per shift: " +
                       string.Join(", ", Enumerable.Range(0, shifts.Count).Where(k => score[k] > 0)
                                                   .Select(k => $"{shifts[k]:+0;-0;0} → {score[k]}")));

        bool changed = false;
        void Place(WallSeamSpot spot, string what, System.Func<int, bool> found)
        {
            if (spot.node < 0) { log.AppendLine($"  {what}: its node is gone — left alone"); return; }
            if (found(zero)) { log.AppendLine($"  {what}: found where it is (Node {spot.node + 1})"); return; }
            foreach (int k in order)
            {
                if (!found(k)) continue;
                int from = spot.node;
                spot.node   = from + shifts[k];
                spot.nodeId = wall.nodes[spot.node].nodeId;
                changed     = true;
                log.AppendLine($"  {what}: Node {from + 1} → Node {spot.node + 1}");
                return;
            }
            log.AppendLine($"  {what}: STILL NOT FOUND (Node {spot.node + 1}) — {NearestLineNote(nodes, wall, spot, report)}");
        }

        for (int i = 0; i < seams.Count; i++)
            if (seams[i] != null) Place(seams[i], $"seam {i + 1}", k => SeamFound(k, i));
        for (int j = 0; j < faces.Count; j++)
            if (faces[j] != null) Place(faces[j], $"face spot {j + 1}", k => FaceFound(k, j));

        Debug.Log(log.ToString());
        if (changed) RebuildWall(wall);
        return changed;
    }

    // A copy of the spot on the node <paramref name="shift"/> places on — null off either end.
    private static WallSeamSpot Shifted(WallSeamSpot s, int shift, int nodeCount)
    {
        if (s == null || s.node + shift < 0 || s.node + shift >= nodeCount) return null;
        return new WallSeamSpot
        {
            node = s.node + shift, along = s.along, across = s.across, height = s.height, fromTop = s.fromTop,
            direction = s.direction, measuredBoth = s.measuredBoth, otherHeight = s.otherHeight, nodeId = s.nodeId,
        };
    }

    // How far a lost seam spot lies from the nearest line, and from the nearest running its way.
    private string NearestLineNote(List<WallMesh.Node> nodes, LevelSelectDesignerData.DesignerWall wall,
                                   WallSeamSpot spot, WallMesh.SeamReport report)
    {
        if (!WallMesh.Resolve(nodes, wall.closed, -_data.RunDepth, spot, out Vector3[] points, out Vector3 dir))
            return "its node is gone";

        float any = float.MaxValue, sameWay = float.MaxValue;
        foreach (Vector3 p in points)
            foreach (var line in report.lines)
            {
                float d = WallMesh.DistanceToLine(line.points, p, out Vector3 runs);
                any = Mathf.Min(any, d);
                if (Mathf.Abs(Vector3.Dot(runs, dir)) >= 0.7f) sameWay = Mathf.Min(sameWay, d);   // WallMesh.SpotRunsSameWay
            }
        return $"nearest line {any:0.####} m, nearest running its way {sameWay:0.####} m (reach is 0.005 m)";
    }
}

#endif
