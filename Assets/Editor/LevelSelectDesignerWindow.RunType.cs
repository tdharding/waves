using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Run Type — what a path's river run is built as (see <see cref="LevelSelectDesignerData.RunType"/>).
///
/// A Structural River Run is the stone run as it always was. A Landscape Natural River Run is the
/// same section in the landscape's own material, with two settings of its own: Rim Rounding, and
/// Outer Walls, which can come off where Landscape Influence brings the ground up to the rim to
/// stand in for them.
///
/// The style belongs to the path, not to the river's Run Shape, so it is stamped onto a COPY of
/// the river's profile (<see cref="RunProfileFor(LevelSelectDesignerData.DesignerPath, string)"/>)
/// for everything built from that run: the run, its water, its rim nodes, and the mouth patches
/// it is carried into a junction or a pool by. A pool is built as the river it takes its shape
/// from (<see cref="LevelSelectDesignerData.PoolRiverName"/>): rounded rim and island edge,
/// walls off, landscape material, and the ground lifted round it by that river's path.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private static bool IsNaturalRun(LevelSelectDesignerData.DesignerPath path)
        => path != null && path.runType == LevelSelectDesignerData.RunType.LandscapeNaturalRiverRun;

    // The walls only come off where the ground is brought up to the rim to stand in for them.
    private static bool RunWallsOff(LevelSelectDesignerData.DesignerPath path)
        => IsNaturalRun(path) && !path.outerWalls
        && path.landscapeInfluence && path.landscapeToOuterRim;

    // Two runs meeting end to end at a river split carry on into each other only when they are
    // built alike.
    private static string RunStyleKey(LevelSelectDesignerData.DesignerPath path)
        => IsNaturalRun(path)
         ? $"Natural|{Mathf.Clamp01(path.rimRounding):F4}|{RunWallsOff(path)}|{RunOuterFlare(path):F4}"
         : "Structural";

    // The flare is the outer corner stretched out and down, so it only exists with the walls on.
    private static float RunOuterFlare(LevelSelectDesignerData.DesignerPath path)
        => IsNaturalRun(path) && !RunWallsOff(path) ? Mathf.Max(0f, path.outerFlare) : 0f;

    /// <summary>The river's Run Shape, with this path's Run Type stamped on a copy of it.</summary>
    private RiverProfile RunProfileFor(LevelSelectDesignerData.DesignerPath path, string riverName)
    {
        var profile = _data.ProfileFor(riverName).Clone();
        profile.rimRounding = IsNaturalRun(path) ? Mathf.Clamp01(path.rimRounding) : 0f;
        profile.wallsOff    = RunWallsOff(path);
        profile.outerFlare  = RunOuterFlare(path);
        return profile;
    }

    // A run's own copy also carries the width at each node of its path, in its own space.
    private RiverProfile RunProfileFor(RiverRunMesh record)
    {
        var path    = PathForRunOrRiver(record);
        var profile = RunProfileFor(path, record.riverName);
        StampRunWidths(profile, record);
        StampRunPortholes(profile, path, record);
        return profile;
    }

    private LevelSelectDesignerData.DesignerPath PathForRunOrRiver(RiverRunMesh record)
        => PathForRun(record) ?? PathForRiver(record.riverName);

    // A mouth or a join only knows the river by name; its path is the one drawn with that name.
    private RiverProfile RunProfileForRiver(string riverName)
        => RunProfileFor(PathForRiver(riverName), riverName);

    private LevelSelectDesignerData.DesignerPath PathForRiver(string riverName)
    {
        string name = riverName ?? "";
        return _data.paths.Find(p => p != null && (p.riverName ?? "") == name);
    }

    // A pool, and every river arriving at it, reads the path that actually reaches it — not the
    // first path drawn under that river's name. A river split into paths at a river split can be
    // built differently on each side, and only the one that arrives decides the pool.
    private LevelSelectDesignerData.DesignerPath PoolPath(LevelSelectDesignerData.DesignerPool pool)
    {
        var arrivals = _data.PathsAtPool(pool);
        return arrivals.Count > 0 ? arrivals[0].path : null;
    }

    private RiverProfile PoolProfileFor(LevelSelectDesignerData.DesignerPool pool)
        => RunProfileFor(PoolPath(pool), _data.PoolRiverName(pool));

    private RiverProfile ArrivingProfileFor(LevelSelectDesignerData.DesignerPool pool, string riverName)
    {
        string name = riverName ?? "";
        var    path = pool != null
                    ? _data.PathsAtPool(pool).Find(a => (a.path.riverName ?? "") == name).path
                    : null;
        return RunProfileFor(path ?? PathForRiver(riverName), riverName);
    }

    // The designer pool a generated pool was built from — its mesh is named off the pool's node.
    private LevelSelectDesignerData.DesignerPool PoolForRecord(RiverPoolMesh record)
        => record == null ? null : _data.pools.Find(p => p != null && PoolMeshName(p) == record.meshAssetName);

    /// <summary>The Run Type, under the path's river name. Its natural-run settings are drawn
    /// in the Run Shape section. Returns true when it changed, so the runs rebuild.</summary>
    private bool DrawPathRunType(LevelSelectDesignerData.DesignerPath path)
    {
        var type = (LevelSelectDesignerData.RunType)EditorGUILayout.EnumPopup(
            new GUIContent("Run Type",
                "What this path's river run is built as: the stone Structural River Run, or a " +
                "Landscape Natural River Run in the landscape's material."),
            path.runType);

        if (type == path.runType) return false;

        Undo.RecordObject(_data, "Edit Run Type");
        path.runType = type;
        EditorUtility.SetDirty(_data);
        return true;
    }

    /// <summary>A natural run's own two settings, drawn under Run Shape. They still belong to
    /// the path, not the river's shared shape. Returns true when either changed.</summary>
    private bool DrawNaturalRimSettings(LevelSelectDesignerData.DesignerPath path)
    {
        if (!IsNaturalRun(path)) return false;

        float rounding = path.rimRounding;
        bool  walls    = path.outerWalls;

        rounding = EditorGUILayout.Slider(
            new GUIContent("Rim Rounding",
                "0 keeps the rim flat with square corners, 1 rounds it fully. With the outer " +
                "walls off only the channel side rounds, so the outer edge stays level with " +
                "the ground."),
            rounding, 0f, 1f);

        // Only offered while the ground is brought up to the rim to take the walls' place.
        if (path.landscapeInfluence && path.landscapeToOuterRim)
            walls = EditorGUILayout.Toggle(
                new GUIContent("Outer Walls",
                    "Off leaves the run without outer walls, so the landscape runs straight " +
                    "onto its rim."),
                walls);

        // The outer corner stretched out and down past the rim — only while there is a wall.
        float flare = path.outerFlare;
        if (!RunWallsOff(path))
            flare = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Outer Flare",
                    "How far, in world units, the outer corner is stretched out past the rim's " +
                    "outer edge — and how far it drops by the same — before the outer wall goes " +
                    "on down vertically. 0 is the plain vertical wall. A junction's branch starts " +
                    "further out to clear it, which takes a Generate."),
                flare));

        if (rounding == path.rimRounding && walls == path.outerWalls && flare == path.outerFlare)
            return false;

        Undo.RecordObject(_data, "Edit Rim Rounding");
        path.rimRounding = rounding;
        path.outerWalls  = walls;
        path.outerFlare  = flare;
        EditorUtility.SetDirty(_data);
        return true;
    }

    /// <summary>
    /// Rebuilds what a change to a path's Run Type reaches: its own runs, the runs and pools it
    /// is carried into, and the landscape round them (the pool rebuild re-traces it). A path at
    /// a river split rebuilds every run, since whether the two ends there close off depends on
    /// both of them.
    /// </summary>
    private void ApplyRunType(LevelSelectDesignerData.DesignerPath path)
    {
        if (path == null) return;

        bool atJoin = path.nodeIds.Count > 0 &&
                      (_data.RiverJoinAt(path.nodeIds[0]) != null ||
                       _data.RiverJoinAt(path.nodeIds[path.nodeIds.Count - 1]) != null);

        RebuildRunMeshes(atJoin ? null : path.riverName);
    }

    /// <summary>
    /// Whether the run ending at a river split on <paramref name="nodeId"/> is built differently
    /// from the run it meets there. If it is, neither carries on into the other: each closes on
    /// its own flat end, and only the water crosses.
    /// </summary>
    private bool RunStyleBreaksAt(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        if (path == null || _data.RiverJoinAt(nodeId) == null) return false;

        string own  = SourcePathId(path);
        string mine = RunStyleKey(path);

        foreach (var other in _data.paths)
        {
            if (other == null || other.pathId == own || other.nodeIds.Count == 0) continue;
            if (other.nodeIds[0] != nodeId && other.nodeIds[other.nodeIds.Count - 1] != nodeId)
                continue;
            if (RunStyleKey(other) != mine) return true;
        }
        return false;
    }

    private Material RunMaterialFor(LevelSelectDesignerData.DesignerPath path)
    {
        if (!IsNaturalRun(path)) return _data.riverMaterial;

        var landscape = LandscapeMaterial();
        if (landscape != null) return landscape;

        Debug.LogWarning("[LevelSelectDesigner] Landscape Natural River Run: no landscape " +
                         "material found (LandscapeTool's, or 'LevelSelectLandscape'), so the " +
                         "run keeps the Run Material.");
        return _data.riverMaterial;
    }

    // The one the landscape tiles wear, found the way Generate Landscape Tiles finds it.
    private Material LandscapeMaterial()
    {
        var tool = FindLandscapeTool();
        if (tool != null && tool.targetMaterial != null) return tool.targetMaterial;

        var guids = AssetDatabase.FindAssets("LevelSelectLandscape t:Material");
        return guids.Length > 0
            ? AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]))
            : null;
    }

    /// <summary>
    /// Writes the landscape's rocky noise lean into a natural run's UV1, exactly as a landscape
    /// tile carries it (x the lean without its grain, y how much noise there is), read off the
    /// same hills at each vertex. The landscape material reads UV1 as that and nothing else, so
    /// the run's ups and downs carry on from the ground beside it. A structural run keeps its
    /// seam lines there instead.
    ///
    /// Read when the run is built, so a hill moved afterwards reaches the run on its next rebuild.
    /// </summary>
    private void ApplyLandscapeNoise(Mesh mesh, Matrix4x4 toWorld)
    {
        if (mesh == null) return;

        var tool   = FindLandscapeTool();
        var ground = LandscapeHillHeight.BuildSurface(
            ActiveHillPoints,
            tool != null ? tool.heightMultiplier : 1f,
            tool != null ? tool.noiseScale       : _data.landscapeNoiseScale,
            null, null, null, null, null, null,
            tool != null ? tool.maps             : null,
            tool != null ? tool.mapArea          : default);

        var verts = mesh.vertices;
        var uv1   = new List<Vector2>(verts.Length);
        foreach (var v in verts)
        {
            Vector3 w     = toWorld.MultiplyPoint3x4(v);
            Vector3 noise = ground.Noise(new Vector2(w.x, w.z));
            uv1.Add(new Vector2(noise.y, noise.z));
        }
        mesh.SetUVs(1, uv1);
    }

    /// <summary>
    /// How far past a walls-off run's outer edge the ground is held level with its rim: one tile
    /// cell corner to corner, plus a little. The tile is planar between its vertices, so the
    /// triangle the edge falls in must have every corner fully lifted, or the ground sags below
    /// the rim right at the edge, with no wall there to hide it.
    /// </summary>
    private static float LandscapeShelf(LandscapeTool tool)
    {
        float cell = 0f;
        if (tool != null)
            foreach (var tile in tool.GetComponentsInChildren<LandscapeTileMesh>())
            {
                Vector3 step = tile.transform.TransformVector(new Vector3(
                    tile.width / Mathf.Max(1, tile.subdivisionsX), 0f,
                    tile.depth / Mathf.Max(1, tile.subdivisionsZ)));
                cell = Mathf.Max(cell, step.magnitude);
            }
        return cell * 1.1f;
    }
}

#endif
