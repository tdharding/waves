using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR

/// <summary>
/// Installation outposts: square blocks standing beside a river run, with a wall round the top.
///
/// An outpost is placed at a point along a path and stands on one bank of it. Select a node on a
/// river and press Add Outpost to place one there; clicking an outpost's point selects it, and its
/// numbers are edited in the left panel.
///
/// On the canvas the footprint is drawn against the drawn path. In the scene it is placed against
/// the generated run itself — the run is swept along a smoothed curve, not the straight lines
/// between the nodes, so only the run knows exactly where its outer edge is.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string OutpostsParent = "OUTPOSTS";

    private string _selectedOutpostId;

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Stands a new outpost beside the river at one of its nodes. It goes on the left bank; the
    /// Side setting in its panel moves it across.
    /// </summary>
    private void AddOutpostAt(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        int index = path.nodeIds.IndexOf(nodeId);
        if (index < 0 || path.nodeIds.Count < 2) return;

        Undo.RecordObject(_data, "Add Outpost");
        var outpost = new LevelSelectDesignerData.DesignerOutpost
        {
            outpostId = Guid.NewGuid().ToString(),
            pathId    = path.pathId,
            pathT     = index / (float)(path.nodeIds.Count - 1),
            side      = LevelSelectDesignerData.OutpostSide.Left,
        };
        _data.outposts.Add(outpost);

        // Open its settings, the same as clicking its point.
        _selectedOutpostId   = outpost.outpostId;
        _selectedPathId      = null;
        _selectedNodeId      = null;
        _selectedObstacleId  = null;
        _selectedArenaNodeId = null;
        _selectedShopNodeId  = null;
        _selectedPoolNodeId  = null;
        _selectedEntranceIdx = -1;
        _leftScroll          = Vector2.zero;
        GUI.FocusControl(null);

        MarkDirty();
        Repaint();
    }

    private void DrawOutposts()
    {
        if (Event.current.type != EventType.Repaint) return;

        foreach (var outpost in _data.outposts)
        {
            if (!TryOutpostFootprint(outpost, out var outer, out var floor)) continue;

            bool selected = outpost.outpostId == _selectedOutpostId;
            var  wall     = selected ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.85f, 0.7f, 0.45f, 0.85f);
            var  inside   = selected ? new Color(0.55f, 0.55f, 0.55f, 0.9f) : new Color(0.35f, 0.28f, 0.18f, 0.85f);

            // Solid fills: the whole block in the wall colour, the floor inside it darker, so the
            // wall reads as the band left showing round the edge.
            Handles.color = wall;
            Handles.DrawAAConvexPolygon(outer);
            if (floor != null)
            {
                Handles.color = inside;
                Handles.DrawAAConvexPolygon(floor);
            }

            // The tower, as a solid dot the size of its orb where its base stands.
            if (outpost.tower != null && TryOutpostTowerCanvas(outpost, out Vector2 towerAt))
            {
                Handles.color = wall;
                Handles.DrawSolidDisc(towerAt, Vector3.forward,
                                      Mathf.Max(2f, outpost.tower.orbRadius * _zoom));
            }

            DrawOutpostObservers(outpost, wall);
        }

        // Selection points last, so no footprint or tower covers one.
        foreach (var outpost in _data.outposts)
        {
            if (!TryOutpostPointCanvas(outpost, out Vector2 at)) continue;
            bool selected = outpost.outpostId == _selectedOutpostId;
            Handles.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            Handles.DrawSolidDisc(at, Vector3.forward, OutpostPointRadius + 1.5f);
            Handles.color = selected ? Color.white : new Color(1f, 0.6f, 0.15f, 1f);
            Handles.DrawSolidDisc(at, Vector3.forward, OutpostPointRadius);
        }
    }

    private const float OutpostPointRadius = 5f;

    /// <summary>
    /// A click on an outpost's point, in any mode, selects it and opens its settings at the top of
    /// the left panel. A click anywhere else closes them again.
    /// </summary>
    private bool HandleOutpostPointClick(Event e)
    {
        if (e.type != EventType.MouseDown || e.button != 0) return false;

        string hit = FindOutpostPointAtCanvas(e.mousePosition);
        if (hit == null)
        {
            if (_selectedOutpostId != null)
            {
                _selectedOutpostId = null;
                Repaint();
            }
            return false;
        }

        _selectedOutpostId   = hit;
        _selectedPathId      = null;
        _selectedNodeId      = null;
        _selectedObstacleId  = null;
        _selectedArenaNodeId = null;
        _selectedShopNodeId  = null;
        _selectedEntranceIdx = -1;
        _leftScroll          = Vector2.zero;
        GUI.FocusControl(null);
        Repaint();
        e.Use();
        return true;
    }

    private string FindOutpostPointAtCanvas(Vector2 canvas)
    {
        float reach = (OutpostPointRadius + 3f) * (OutpostPointRadius + 3f);
        for (int i = _data.outposts.Count - 1; i >= 0; i--)
        {
            var outpost = _data.outposts[i];
            if (!TryOutpostPointCanvas(outpost, out Vector2 at)) continue;
            if ((canvas - at).sqrMagnitude <= reach) return outpost.outpostId;
        }
        return null;
    }

    /// <summary>Canvas position of an outpost's selection point: the centre of its floor.</summary>
    private bool TryOutpostPointCanvas(LevelSelectDesignerData.DesignerOutpost outpost, out Vector2 canvas)
    {
        canvas = default;
        if (outpost == null) return false;
        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        if (path == null || path.nodeIds.Count < 2) return false;

        PathFrameAt(path, outpost.pathT, out Vector3 centre, out Vector3 forward);
        Vector3 away = OutpostAway(forward, outpost.side);
        Vector3 edge = centre + away * (_data.ProfileFor(path.riverName).OuterWidth * 0.5f);

        canvas = WorldToCanvas(edge + away * (outpost.depth * 0.5f));
        return true;
    }

    private string FindOutpostAtCanvas(Vector2 canvas)
    {
        // Last drawn is on top, so it is the one a click lands on.
        for (int i = _data.outposts.Count - 1; i >= 0; i--)
        {
            var outpost = _data.outposts[i];
            if (!TryOutpostFootprint(outpost, out var outer, out _)) continue;
            if (InsideConvex(canvas, outer)) return outpost.outpostId;
        }
        return null;
    }

    /// <summary>
    /// Canvas corners of an outpost's footprint, and of the floor inside its wall (null when it
    /// has no wall). Measured off the drawn path, which is close to the run but not on it.
    /// </summary>
    private bool TryOutpostFootprint(LevelSelectDesignerData.DesignerOutpost outpost,
                                     out Vector3[] outer, out Vector3[] floor)
    {
        outer = null;
        floor = null;
        if (outpost == null) return false;

        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        if (path == null || path.nodeIds.Count < 2) return false;

        PathFrameAt(path, outpost.pathT, out Vector3 centre, out Vector3 forward);
        Vector3 away = OutpostAway(forward, outpost.side);
        Vector3 edge = centre + away * (_data.ProfileFor(path.riverName).OuterWidth * 0.5f);

        outer = CanvasRect(edge, forward, away, outpost.width * 0.5f, 0f, outpost.depth);

        float t = Mathf.Min(outpost.wallThickness,
                            Mathf.Min(outpost.width * 0.5f, outpost.depth * 0.5f) - 0.001f);
        if (t > 0.0001f && outpost.wallHeight > 0.0001f)
            floor = CanvasRect(edge, forward, away, outpost.width * 0.5f - t, t, outpost.depth - t);

        return true;
    }

    private bool TryOutpostTowerCanvas(LevelSelectDesignerData.DesignerOutpost outpost, out Vector2 canvas)
    {
        canvas = default;
        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        if (path == null || path.nodeIds.Count < 2) return false;

        PathFrameAt(path, outpost.pathT, out Vector3 centre, out Vector3 forward);
        Vector3 away = OutpostAway(forward, outpost.side);
        Vector3 edge = centre + away * (_data.ProfileFor(path.riverName).OuterWidth * 0.5f);

        canvas = WorldToCanvas(edge + forward * outpost.towerOffset.x
                                    + away * (outpost.depth * 0.5f + outpost.towerOffset.y));
        return true;
    }

    /// <summary>A solid dot where each observer stands, with a short line the way it faces.</summary>
    private void DrawOutpostObservers(LevelSelectDesignerData.DesignerOutpost outpost, Color color)
    {
        if (outpost.observers == null || outpost.observers.Count == 0) return;

        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        if (path == null || path.nodeIds.Count < 2) return;

        PathFrameAt(path, outpost.pathT, out Vector3 centre, out Vector3 forward);
        Vector3 away    = OutpostAway(forward, outpost.side);
        Vector3 edge    = centre + away * (_data.ProfileFor(path.riverName).OuterWidth * 0.5f);
        Vector3 toRiver = -away;

        Handles.color = color;
        foreach (var observer in outpost.observers)
        {
            var o = observer.Clone();
            ClampObserverToFloor(outpost, o);
            Vector3 stand = edge + forward * o.offsetAlong + away * (outpost.depth * 0.5f + o.offsetAway);
            Vector3 face  = Quaternion.AngleAxis(o.facing, Vector3.up) * toRiver;

            Vector2 at  = WorldToCanvas(stand);
            Vector2 dir = WorldToCanvas(stand + face) - at;
            if (dir.sqrMagnitude > 1e-8f) Handles.DrawAAPolyLine(2f, at, at + dir.normalized * 8f);
            Handles.DrawSolidDisc(at, Vector3.forward, 2.5f);
        }
    }

    private Vector3[] CanvasRect(Vector3 edge, Vector3 forward, Vector3 away,
                                 float halfAlong, float awayFrom, float awayTo)
    {
        return new[]
        {
            (Vector3)WorldToCanvas(edge - forward * halfAlong + away * awayFrom),
            (Vector3)WorldToCanvas(edge + forward * halfAlong + away * awayFrom),
            (Vector3)WorldToCanvas(edge + forward * halfAlong + away * awayTo),
            (Vector3)WorldToCanvas(edge - forward * halfAlong + away * awayTo),
        };
    }

    private static bool InsideConvex(Vector2 p, Vector3[] poly)
    {
        float sign = 0f;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
            float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            if (Mathf.Abs(cross) < 1e-6f) continue;
            if (sign == 0f) sign = Mathf.Sign(cross);
            else if (Mathf.Sign(cross) != sign) return false;
        }
        return true;
    }

    /// <summary>A point on the drawn path and the flat direction it runs in there.</summary>
    private void PathFrameAt(LevelSelectDesignerData.DesignerPath path, float t,
                             out Vector3 position, out Vector3 forward)
    {
        position = GetWorldPosOnPathRaw(path, t);
        forward  = Vector3.forward;

        int   segments = path.nodeIds.Count - 1;
        int   seg      = Mathf.Min(Mathf.FloorToInt(Mathf.Clamp(t * segments, 0, segments)), segments - 1);
        Vector3 dir    = _data.NodeWorldPosition(path.nodeIds[seg + 1]) -
                         _data.NodeWorldPosition(path.nodeIds[seg]);
        dir.y = 0f;
        if (dir.sqrMagnitude > 1e-8f) forward = dir.normalized;
    }

    // Right is the run's own right, Cross(up, forward) — the same frame the run is swept on.
    private static Vector3 OutpostAway(Vector3 forward, LevelSelectDesignerData.OutpostSide side)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        return side == LevelSelectDesignerData.OutpostSide.Right ? right : -right;
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private void DrawSelectedOutpostProps()
    {
        var outpost = _data.outposts.Find(o => o.outpostId == _selectedOutpostId);
        if (outpost == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Installation Outpost", EditorStyles.boldLabel);

        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        EditorGUILayout.LabelField($"On {(path != null ? path.segmentId : "(missing path)")}",
                                   EditorStyles.miniLabel);

        DrawOutpostPresetRow(outpost);

        EditorGUI.BeginChangeCheck();
        var side = (LevelSelectDesignerData.OutpostSide)EditorGUILayout.EnumPopup(
            new GUIContent("Side", "Which bank it stands on, looking down the path the way it was drawn."),
            outpost.side);
        float pathT = EditorGUILayout.Slider(
            new GUIContent("Along Path", "Where it stands along the path, start to end."),
            outpost.pathT, 0f, 1f);
        float width = EditorGUILayout.FloatField(
            new GUIContent("Width", "How far it runs along the river."), outpost.width);
        float depth = EditorGUILayout.FloatField(
            new GUIContent("Depth", "How far it reaches away from the river, out from the run's outer edge."),
            outpost.depth);
        float height = EditorGUILayout.FloatField(
            new GUIContent("Height", "How far its floor stands above the rim top of the river run."),
            outpost.height);
        float wallThickness = EditorGUILayout.FloatField(
            new GUIContent("Wall Thickness", "Thickness of the wall around the top."),
            outpost.wallThickness);
        float wallHeight = EditorGUILayout.FloatField(
            new GUIContent("Wall Height", "How far the wall around the top stands above the floor."),
            outpost.wallHeight);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Lollipop Tower", EditorStyles.miniBoldLabel);
        float offsetAlong = EditorGUILayout.FloatField(
            new GUIContent("Offset Along", "Moves the tower along the river from the floor's centre."),
            outpost.towerOffset.x);
        float offsetAway = EditorGUILayout.FloatField(
            new GUIContent("Offset Away", "Moves the tower away from the river from the floor's centre."),
            outpost.towerOffset.y);
        DrawLollipopPresetRow(outpost.towerPreset, outpost.tower,
                              p => outpost.towerPreset = p, t => outpost.tower = t);
        var tower = DrawLollipopTowerFields(outpost.tower);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Observers", EditorStyles.miniBoldLabel);
        if (LoadObserverPrefab() == null)
            EditorGUILayout.HelpBox($"No prefab at {ObserverPrefabPath} — observers won't be spawned.",
                                    MessageType.Warning);
        var observers = DrawOutpostObserverFields(outpost.observers);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Outpost");
            outpost.side          = side;
            outpost.pathT         = pathT;
            outpost.width         = Mathf.Max(0.01f, width);
            outpost.depth         = Mathf.Max(0.01f, depth);
            outpost.height        = Mathf.Max(0f, height);
            outpost.wallThickness = Mathf.Max(0f, wallThickness);
            outpost.wallHeight    = Mathf.Max(0f, wallHeight);
            outpost.towerOffset   = new Vector2(offsetAlong, offsetAway);
            outpost.tower         = tower;
            outpost.observers     = observers;
            foreach (var o in outpost.observers) ClampObserverToFloor(outpost, o);
            MarkDirty();
            RebuildOutpost(outpost);
        }

        EditorGUILayout.Space(2);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("Delete Outpost"))
        {
            Undo.RecordObject(_data, "Delete Outpost");
            _data.outposts.Remove(outpost);
            _selectedOutpostId = null;
            MarkDirty();
        }
        GUI.backgroundColor = Color.white;
    }

    private const string OutpostPresetFolder = "Assets/ScriptsData/DataScripts/InstallationOutpostPresets";

    /// <summary>
    /// The outpost's preset: pick one from the dropdown to put its settings on this outpost, Save
    /// to write this outpost's settings back into it, or Save as New to make one from them.
    /// Settings are copied either way — where the outpost stands is never part of a preset.
    /// </summary>
    private void DrawOutpostPresetRow(LevelSelectDesignerData.DesignerOutpost outpost)
    {
        var picked = LevelSelectRiverPresetLibrary.DrawPicker(
            new GUIContent("Preset", "The outpost presets kept in " + OutpostPresetFolder +
                                     ". Picking one puts its settings on this outpost."),
            outpost.preset, OutpostPresetFolder);

        if (picked != outpost.preset)
        {
            Undo.RecordObject(_data, "Load Outpost Preset");
            outpost.preset = picked;
            if (picked != null)
            {
                picked.ApplyTo(outpost);
                foreach (var o in outpost.observers) ClampObserverToFloor(outpost, o);
            }
            MarkDirty();
            RebuildOutpost(outpost);
            GUI.FocusControl(null);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(outpost.preset == null))
            {
                if (GUILayout.Button(new GUIContent("Save", "Overwrite the preset with this outpost's settings.")) &&
                    EditorUtility.DisplayDialog("Save Outpost Preset",
                        $"Overwrite '{outpost.preset.name}' with this outpost's settings?", "Save", "Cancel"))
                {
                    Undo.RecordObject(outpost.preset, "Save Outpost Preset");
                    outpost.preset.CopyFrom(outpost);
                    EditorUtility.SetDirty(outpost.preset);
                    AssetDatabase.SaveAssetIfDirty(outpost.preset);
                }
            }

            if (GUILayout.Button(new GUIContent("Save as New", "Make a new preset from this outpost's settings.")))
            {
                string assetPath = EditorUtility.SaveFilePanelInProject(
                    "Save Outpost Preset", "NewOutpostPreset", "asset", "Choose save location",
                    LevelSelectRiverPresetLibrary.EnsureFolder(OutpostPresetFolder));
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var preset = CreateInstance<InstallationOutpostPreset>();
                    preset.CopyFrom(outpost);
                    AssetDatabase.CreateAsset(preset, assetPath);
                    AssetDatabase.SaveAssets();

                    Undo.RecordObject(_data, "Save Outpost Preset");
                    outpost.preset = preset;
                    MarkDirty();
                }
                GUIUtility.ExitGUI();
            }
        }
    }

    /// <summary>
    /// How many observers, and where each stands and faces. Hands back an edited copy, so the
    /// caller writes it only inside its own change check. A new observer copies the one before it.
    /// </summary>
    private static List<LevelSelectDesignerData.OutpostObserver> DrawOutpostObserverFields(
        List<LevelSelectDesignerData.OutpostObserver> observers)
    {
        var edited = (observers ?? new List<LevelSelectDesignerData.OutpostObserver>())
                     .Select(o => o.Clone()).ToList();

        int count = Mathf.Max(0, EditorGUILayout.IntField(
            new GUIContent("Count", "How many observers stand on this outpost."), edited.Count));
        while (edited.Count > count) edited.RemoveAt(edited.Count - 1);
        while (edited.Count < count)
            edited.Add(edited.Count > 0 ? edited[edited.Count - 1].Clone()
                                        : new LevelSelectDesignerData.OutpostObserver());

        for (int i = 0; i < edited.Count; i++)
        {
            var o = edited[i];
            EditorGUILayout.LabelField($"Observer {i + 1}", EditorStyles.miniLabel);
            EditorGUI.indentLevel++;
            o.offsetAlong = EditorGUILayout.FloatField(
                new GUIContent("Offset Along", "Moves it along the river from the floor's centre."), o.offsetAlong);
            o.offsetAway = EditorGUILayout.FloatField(
                new GUIContent("Offset Away", "Moves it away from the river from the floor's centre."), o.offsetAway);
            o.facing = EditorGUILayout.Slider(
                new GUIContent("Facing", "Which way it faces. 0 = facing the river, clockwise seen from above."),
                o.facing, 0f, 360f);
            o.height = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                new GUIContent("Height", "How tall it stands, feet to the top marker on its prefab."), o.height));
            EditorGUI.indentLevel--;
        }

        return edited;
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    /// <summary>
    /// Builds every outpost. Runs after the river runs, because an outpost is placed against the
    /// run it stands beside.
    /// </summary>
    private void GenerateOutposts(GameObject outpostsParent)
    {
        foreach (var outpost in _data.outposts)
        {
            if (outpost == null || string.IsNullOrEmpty(outpost.outpostId)) continue;

            var existing = FindObjectsOfType<InstallationOutpostMesh>()
                .FirstOrDefault(r => r.outpostId == outpost.outpostId);

            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject(OutpostMeshName(outpost));
                Undo.RegisterCreatedObjectUndo(go, "Generate Outpost");
                go.transform.SetParent(outpostsParent.transform, false);
                go.AddComponent<InstallationOutpostMesh>();
            }

            BuildOutpost(outpost, go);
        }
    }

    /// <summary>Rebuilds one outpost already standing in the scene, if it is there.</summary>
    private void RebuildOutpost(LevelSelectDesignerData.DesignerOutpost outpost)
    {
        var record = FindObjectsOfType<InstallationOutpostMesh>()
            .FirstOrDefault(r => r.outpostId == outpost.outpostId);
        if (record == null) return;

        BuildOutpost(outpost, record.gameObject);
        AssetDatabase.SaveAssets();
    }

    private void BuildOutpost(LevelSelectDesignerData.DesignerOutpost outpost, GameObject go)
    {
        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        if (path == null || path.nodeIds.Count < 2)
        {
            Debug.LogWarning($"[LevelSelectDesigner] Outpost '{outpost.outpostId}' has no path — skipped.");
            return;
        }

        var profile = _data.ProfileFor(path.riverName);
        OutpostSite(outpost, path, out Vector3 rimTop, out Vector3 forward);

        Vector3 away = OutpostAway(forward, outpost.side);
        string  name = OutpostMeshName(outpost);

        Undo.RecordObject(go.transform, "Place Outpost");
        go.name               = name;
        go.transform.position = rimTop + away * (profile.OuterWidth * 0.5f);
        go.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        go.transform.localScale = Vector3.one;

        // Down to the same underside as the runs, which are built from their rim top too.
        var mesh = SaveGeneratedMesh(name, InstallationOutpostMesh.Build(
            outpost.width, outpost.depth, outpost.height, _data.RunDepth,
            outpost.wallThickness, outpost.wallHeight,
            outpost.tower, TowerOffsetInMesh(outpost, go.transform, forward),
            StoneShadingForBuild));
        if (mesh == null) return;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _data.arenaWallMaterial != null
                                ? _data.arenaWallMaterial : _data.riverMaterial;
        EditorUtility.SetDirty(renderer);

        var record = go.GetComponent<InstallationOutpostMesh>();
        record.outpostId     = outpost.outpostId;
        record.meshAssetName = name;
        EditorUtility.SetDirty(record);

        SpawnObservers(outpost, go, forward, away);
    }

    // ─────────────────────────────────────────────
    // OBSERVERS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Keeps an observer's offsets on the floor inside the walls. Measured the same way the mesh
    /// measures its wall: thickness capped just short of half the width or depth, and no wall at
    /// all when the wall has no height — then the whole top is floor.
    /// </summary>
    private static void ClampObserverToFloor(LevelSelectDesignerData.DesignerOutpost outpost,
                                             LevelSelectDesignerData.OutpostObserver o)
    {
        Vector2 half = FloorHalfSize(outpost);
        o.offsetAlong = Mathf.Clamp(o.offsetAlong, -half.x, half.x);
        o.offsetAway  = Mathf.Clamp(o.offsetAway,  -half.y, half.y);
    }

    /// <summary>Half the floor inside the walls: x along the river, y away from it.</summary>
    private static Vector2 FloorHalfSize(LevelSelectDesignerData.DesignerOutpost outpost)
    {
        float halfW  = outpost.width * 0.5f;
        float halfD  = outpost.depth * 0.5f;
        float t      = Mathf.Clamp(outpost.wallThickness, 0f, Mathf.Min(halfW, halfD) - 0.001f);
        bool  walled = t > 0.0001f && outpost.wallHeight > 0.0001f;
        if (walled) { halfW -= t; halfD -= t; }

        return new Vector2(Mathf.Max(0f, halfW), Mathf.Max(0f, halfD));
    }

    private const string ObserverPrefabPath = "Assets/Prefab/NPCPrefabs/ObserverManNPC.prefab";
    private const string ObserversChild     = "Observers";

    private static GameObject LoadObserverPrefab()
        => AssetDatabase.LoadAssetAtPath<GameObject>(ObserverPrefabPath);

    /// <summary>
    /// Stands the outpost's observers on its floor, offset from its centre, under an "Observers" child of
    /// the block. Instances already there are reused in order; extras are removed.
    ///
    /// Placed off the prefab's PrefabBaselineAlignment: its disc is where the feet meet the floor,
    /// its forward override is the way the figure faces, and its top marker is what Height
    /// measures, so Height is scaled against it.
    /// </summary>
    private void SpawnObservers(LevelSelectDesignerData.DesignerOutpost outpost, GameObject block,
                                Vector3 forward, Vector3 away)
    {
        Transform container = block.transform.Find(ObserversChild);
        var       observers = outpost.observers ?? new List<LevelSelectDesignerData.OutpostObserver>();
        var       prefab    = LoadObserverPrefab();

        if (observers.Count == 0 || prefab == null)
        {
            if (observers.Count > 0)
                Debug.LogWarning($"[LevelSelectDesigner] No observer prefab at {ObserverPrefabPath} — " +
                                 $"outpost '{outpost.outpostId}' has no observers spawned.");
            if (container != null) Undo.DestroyObjectImmediate(container.gameObject);
            return;
        }

        if (container == null)
        {
            var c = new GameObject(ObserversChild);
            Undo.RegisterCreatedObjectUndo(c, "Spawn Observers");
            c.transform.SetParent(block.transform, false);
            container = c.transform;
        }

        // What the prefab's aligner says, in the prefab root's own frame.
        var       aligner  = prefab.GetComponentInChildren<PrefabBaselineAlignment>(true);
        Transform root     = prefab.transform;
        Vector3   contact  = Vector3.zero;
        Vector3   faceWay  = Vector3.forward;
        float     topHeight = 0f;
        if (aligner != null)
        {
            contact   = Quaternion.Inverse(root.rotation) * (aligner.transform.position - root.position);
            faceWay   = Quaternion.Inverse(root.rotation) * aligner.transform.TransformDirection(aligner.LocalForward);
            topHeight = aligner.PrefabTopHeight;
        }
        else
        {
            Debug.LogWarning($"[LevelSelectDesigner] {prefab.name} has no PrefabBaselineAlignment — " +
                             "observers are placed off its root.");
        }
        faceWay.y = 0f;
        if (faceWay.sqrMagnitude < 1e-8f) faceWay = Vector3.forward;
        if (topHeight <= 0f)
            Debug.LogWarning($"[LevelSelectDesigner] {prefab.name} has no top marker — observer Height " +
                             "is ignored and they spawn at the prefab's own size.");

        // The floor's centre, and which way along the block's x runs down the path.
        float   alongSign = Vector3.Dot(block.transform.right, forward) >= 0f ? 1f : -1f;
        Vector3 toRiver   = -away;

        for (int i = 0; i < observers.Count; i++)
        {
            var o = observers[i].Clone();
            ClampObserverToFloor(outpost, o);

            GameObject figure = null;
            if (i < container.childCount)
            {
                var child = container.GetChild(i).gameObject;
                if (PrefabUtility.GetCorrespondingObjectFromSource(child) == prefab) figure = child;
                else Undo.DestroyObjectImmediate(child);
            }
            if (figure == null)
            {
                figure = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(figure, "Spawn Observer");
                figure.transform.SetParent(container, false);
                figure.transform.SetSiblingIndex(i);
            }

            float   scale = topHeight > 0f ? o.height / topHeight : 1f;
            Vector3 stand = block.transform.TransformPoint(new Vector3(
                o.offsetAlong * alongSign, outpost.height, outpost.depth * 0.5f + o.offsetAway));
            Vector3 face  = Quaternion.AngleAxis(o.facing, Vector3.up) * toRiver;
            var     rot  = Quaternion.LookRotation(face, Vector3.up) *
                            Quaternion.Inverse(Quaternion.LookRotation(faceWay, Vector3.up));

            Undo.RecordObject(figure.transform, "Place Observer");
            figure.name                    = $"Observer_{i + 1}";
            figure.transform.localScale    = root.localScale * scale;
            figure.transform.rotation      = rot;
            figure.transform.position      = stand - rot * (contact * scale);

            // The floor it may walk about on, in the block's own space.
            var npc = figure.GetComponent<ObserverManNPCController>();
            if (npc != null)
            {
                Undo.RecordObject(npc, "Place Observer");
                npc.SetFloor(block.transform, new Vector3(0f, outpost.height, outpost.depth * 0.5f),
                             FloorHalfSize(outpost));
                PrefabUtility.RecordPrefabInstancePropertyModifications(npc);
                EditorUtility.SetDirty(npc);
            }
        }

        for (int i = container.childCount - 1; i >= observers.Count; i--)
            Undo.DestroyObjectImmediate(container.GetChild(i).gameObject);
    }

    /// <summary>
    /// Where an outpost's river runs past it: a point on the centre of the run's rim top, and the
    /// flat direction the run is heading there.
    ///
    /// The drawn path only gives the rough spot. The run was swept along a smoothed curve, so the
    /// spot is carried onto the nearest point of that curve — the generated run of the same river
    /// closest to it — and the outpost lands on the run's real edge. With no run generated yet it
    /// falls back to the drawn path and says so.
    /// </summary>
    private void OutpostSite(LevelSelectDesignerData.DesignerOutpost outpost,
                             LevelSelectDesignerData.DesignerPath path,
                             out Vector3 rimTop, out Vector3 forward)
    {
        PathFrameAt(path, outpost.pathT, out rimTop, out forward);
        Vector3 rough = rimTop;

        float best = float.MaxValue;
        foreach (var run in FindObjectsOfType<RiverRunMesh>())
        {
            if (run.knots.Count < 2) continue;
            if (!string.IsNullOrEmpty(path.riverName) && run.riverName != path.riverName) continue;

            var    spline = run.ToSpline();
            float3 local  = run.transform.InverseTransformPoint(rough);
            SplineUtility.GetNearestPoint(spline, local, out float3 nearest, out float t);

            Vector3 world = run.transform.TransformPoint(nearest);
            float   dist  = new Vector2(world.x - rough.x, world.z - rough.z).sqrMagnitude;
            if (dist >= best) continue;

            Vector3 tangent = run.transform.TransformDirection((Vector3)spline.EvaluateTangent(t));
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 1e-8f) continue;

            best    = dist;
            rimTop  = world;
            forward = tangent.normalized;
        }

        if (best == float.MaxValue)
            Debug.LogWarning($"[LevelSelectDesigner] Outpost '{outpost.outpostId}' found no generated " +
                             $"run for river '{path.riverName}' — placed against the drawn path instead.");
    }

    // The mesh's x runs one way on the right bank and the other on the left, so Offset Along is
    // turned into it here — it always means down the path the way it was drawn.
    private static Vector2 TowerOffsetInMesh(LevelSelectDesignerData.DesignerOutpost outpost,
                                             Transform block, Vector3 forward)
    {
        float along = Vector3.Dot(block.right, forward) >= 0f ? 1f : -1f;
        return new Vector2(outpost.towerOffset.x * along, outpost.towerOffset.y);
    }

    // Named off the outpost's id, which survives every regenerate — so the mesh asset is reused.
    private static string OutpostMeshName(LevelSelectDesignerData.DesignerOutpost outpost)
        => $"Outpost_{SanitiseAssetName(outpost.outpostId)}";
}

#endif
