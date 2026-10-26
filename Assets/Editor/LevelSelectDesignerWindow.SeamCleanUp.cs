using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// What the Seam Clean Up Tool reaches into the designer for. The removed seams live on each
/// wall in the designer's data, so they are edited on the working copy here and saved with the
/// designer's own Save; a change rebuilds the wall straight away, the same as any wall edit.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // The seam lines each wall came out with the last time it was built, for the tool to draw
    // and pick from. Not saved — a wall not built since the editor started is built for it once.
    private readonly Dictionary<string, WallMesh.SeamReport> _wallSeams = new Dictionary<string, WallMesh.SeamReport>();

    // What each wall's removed spots looked like when its report was made — an undo that puts
    // them back some other way leaves this behind, which is how the tool knows to rebuild.
    private readonly Dictionary<string, string> _wallSeamsMadeFrom = new Dictionary<string, string>();

    /// <summary>The open designer, or null.</summary>
    internal static LevelSelectDesignerWindow FindOpen() =>
        Resources.FindObjectsOfTypeAll<LevelSelectDesignerWindow>().FirstOrDefault();

    internal bool HasData => _data != null;

    /// <summary>Has edits the designer hasn't saved to its asset yet.</summary>
    internal bool HasUnsaved => hasUnsavedChanges;

    /// <summary>Every wall, as the tool lists them — named as its GameObject is in the scene.</summary>
    internal List<(string id, string label)> SeamWalls()
    {
        var list = new List<(string, string)>();
        if (_data == null) return list;
        foreach (var wall in _data.walls)
        {
            if (wall == null || string.IsNullOrEmpty(wall.wallId) || wall.nodes.Count < 2) continue;
            list.Add((wall.wallId, WallMeshName(wall)));
        }
        return list;
    }

    /// <summary>Where a wall's own frame sits in the world — its seam lines are in that frame.</summary>
    internal Vector3 WallFrameOrigin => new Vector3(0f, WallRimTopY, 0f);

    internal IReadOnlyList<WallSeamSpot> RemovedWallSeams(string wallId) =>
        FindWall(wallId)?.removedSeams ?? (IReadOnlyList<WallSeamSpot>)new List<WallSeamSpot>();

    /// <summary>The seam lines a wall came out with — built now, unbuilt, if it hasn't been since the editor started.</summary>
    internal WallMesh.SeamReport WallSeams(string wallId)
    {
        var wall = FindWall(wallId);
        if (wall == null) return null;
        if (_wallSeams.TryGetValue(wallId, out var report)) return report;

        wall.SyncSpotNodes();
        report = new WallMesh.SeamReport();
        var meshes = WallMesh.BuildChunks(WallNodes(wall), wall.closed, wall.thickness, -_data.RunDepth,
                                          StoneShadingForBuild, wall.removedSeams, report, wall.faceGroups,
                                          WallLipFor(wall));
        foreach (var mesh in meshes) DestroyImmediate(mesh);
        KeepWallSeams(wall, report);
        return report;
    }

    /// <summary>Removes the seam line running through <paramref name="point"/> (wall frame) and rebuilds the wall.</summary>
    internal void RemoveWallSeam(string wallId, Vector3 point, Vector3 dir)
    {
        var wall = FindWall(wallId);
        if (wall == null) return;

        var spot = WallSpotAt(wall, point, dir);
        if (spot == null) return;

        Undo.RecordObject(_data, "Remove Seam");
        wall.removedSeams.Add(spot);
        MarkDirty();
        RebuildWall(wall);
        Repaint();
    }

    /// <summary>Puts back the seams these removed spots took away, and rebuilds the wall.</summary>
    internal void RestoreWallSeams(string wallId, IEnumerable<int> spots)
    {
        var wall = FindWall(wallId);
        if (wall == null) return;

        var gone = spots.Where(i => i >= 0 && i < wall.removedSeams.Count).Distinct().OrderByDescending(i => i).ToList();
        if (gone.Count == 0) return;

        Undo.RecordObject(_data, "Restore Seam");
        foreach (int i in gone) wall.removedSeams.RemoveAt(i);
        MarkDirty();
        RebuildWall(wall);
        Repaint();
    }

    /// <summary>Rebuilds a wall whose removed seams changed under its last build — after an undo.</summary>
    internal void SyncWallSeams(string wallId)
    {
        var wall = FindWall(wallId);
        if (wall == null) return;
        if (_wallSeamsMadeFrom.TryGetValue(wallId, out string made) && made == WallKey(wall)) return;
        RebuildWall(wall);
    }

    private void KeepWallSeams(LevelSelectDesignerData.DesignerWall wall, WallMesh.SeamReport report)
    {
        _wallSeams[wall.wallId]        = report;
        _wallSeamsMadeFrom[wall.wallId] = WallKey(wall);
    }

    // Everything the tool edits on a wall, as one string — what an undo is checked against.
    private static string WallKey(LevelSelectDesignerData.DesignerWall wall) =>
        SpotsKey(wall.removedSeams) + "#" +
        string.Join("#", (wall.faceGroups ?? new List<WallFaceGroup>()).Select(g => g == null ? "-" : SpotsKey(g.surfaces)));

    // ─────────────────────────────────────────────
    // FACE GROUPS
    // ─────────────────────────────────────────────

    internal IReadOnlyList<WallFaceGroup> WallFaceGroups(string wallId) =>
        FindWall(wallId)?.faceGroups ?? (IReadOnlyList<WallFaceGroup>)new List<WallFaceGroup>();

    /// <summary>Starts a new, empty face group on a wall; hands back its index.</summary>
    internal int AddWallFaceGroup(string wallId)
    {
        var wall = FindWall(wallId);
        if (wall == null) return -1;

        Undo.RecordObject(_data, "New Face Group");
        wall.faceGroups.Add(new WallFaceGroup());
        MarkDirty();
        Repaint();
        return wall.faceGroups.Count - 1;
    }

    /// <summary>
    /// Adds the surface the face at <paramref name="point"/> (wall frame, looking along
    /// <paramref name="normal"/>) is on to a face group, starting one if there is none, and
    /// rebuilds the wall.
    /// </summary>
    internal void AddWallFaceToGroup(string wallId, int group, Vector3 point, Vector3 normal)
    {
        var wall = FindWall(wallId);
        if (wall == null) return;

        var spot = WallSpotAt(wall, point, normal);
        if (spot == null) return;

        Undo.RecordObject(_data, "Group Face");
        if (group < 0 || group >= wall.faceGroups.Count)
        {
            wall.faceGroups.Add(new WallFaceGroup());
            group = wall.faceGroups.Count - 1;
        }
        wall.faceGroups[group].surfaces.Add(spot);
        MarkDirty();
        RebuildWall(wall);
        Repaint();
    }

    /// <summary>Takes surfaces out of a face group by their spots, and rebuilds the wall.</summary>
    internal void RemoveWallGroupFaces(string wallId, int group, IEnumerable<int> spots)
    {
        var wall = FindWall(wallId);
        if (wall == null || group < 0 || group >= wall.faceGroups.Count) return;

        var list = wall.faceGroups[group].surfaces;
        var gone = spots.Where(i => i >= 0 && i < list.Count).Distinct().OrderByDescending(i => i).ToList();
        if (gone.Count == 0) return;

        Undo.RecordObject(_data, "Ungroup Face");
        foreach (int i in gone) list.RemoveAt(i);
        MarkDirty();
        RebuildWall(wall);
        Repaint();
    }

    /// <summary>Deletes a whole face group, and rebuilds the wall.</summary>
    internal void DeleteWallFaceGroup(string wallId, int group)
    {
        var wall = FindWall(wallId);
        if (wall == null || group < 0 || group >= wall.faceGroups.Count) return;

        Undo.RecordObject(_data, "Delete Face Group");
        wall.faceGroups.RemoveAt(group);
        MarkDirty();
        RebuildWall(wall);
        Repaint();
    }

    /// <summary>The spot <paramref name="point"/> (wall frame) is on this wall, tied to its node by the node's id.</summary>
    private WallSeamSpot WallSpotAt(LevelSelectDesignerData.DesignerWall wall, Vector3 point, Vector3 dir)
    {
        wall.SyncSpotNodes();
        var spot = WallMesh.SpotAt(WallNodes(wall), wall.closed, -_data.RunDepth, point, dir);
        if (spot != null && spot.node >= 0 && spot.node < wall.nodes.Count) spot.nodeId = wall.nodes[spot.node].nodeId;
        return spot;
    }

    private static string SpotsKey(List<WallSeamSpot> spots) =>
        spots == null ? "" : string.Join("|", spots.Select(s => s == null ? "-" :
            $"{s.node},{s.along:R},{s.across:R},{s.height:R},{s.fromTop},{s.direction.x:R},{s.direction.y:R},{s.direction.z:R},{s.measuredBoth},{s.otherHeight:R}"));

    private LevelSelectDesignerData.DesignerWall FindWall(string wallId) =>
        _data == null || string.IsNullOrEmpty(wallId) ? null : _data.walls.Find(w => w != null && w.wallId == wallId);
}

#endif
