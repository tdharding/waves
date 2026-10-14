using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Walls: plain slabs standing out of the water, drawn node by node — the Grid Designer's spline
/// walls brought over, but built as ONE mesh along the nodes rather than a tile every few
/// centimetres. In the river material, with a mesh collider of the same shape.
///
/// Wall mode draws them. Click empty space to start a wall and keep clicking to lay its nodes;
/// Enter, Esc or a double-click finishes it. Drag a node to move it; right-click one, or select
/// it and press Delete, to remove it. Delete with only the wall selected removes the whole wall.
///
/// Each stretch between two nodes is curved or straight, or a gap — set in the wall's node list,
/// as in the Grid Designer. Height is measured from the water surface up to the wall's top: one
/// height for the whole wall, which any node can override. The sides go down to the one Drop.
///
/// Any node can carry a lollipop tower standing on the wall's top there, with its own sizes,
/// preset and perch — built as a child of the wall. Any node can also be a round column, its
/// own radius and height above the wall's top; a tower on that node stands on the column.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string WallsParent     = "WALLS";
    private const string WallTowersChild = "Towers";
    private const string WallChunksChild = "Chunks";

    private string  _selectedWallId;
    private int     _selectedWallNode = -1;
    private bool    _isDrawingWall;
    private bool    _isDraggingWall;
    private Vector2 _wallDragOffset;

    private static float WallNodeRadius => LevelSelectDesignerSettings.WallNodeSize;
    private const float WallHitReach   = 7f;

    private LevelSelectDesignerData.DesignerWall SelectedWall =>
        _selectedWallId == null ? null : _data.walls.Find(w => w.wallId == _selectedWallId);

    // ─────────────────────────────────────────────
    // HEIGHTS AND SHAPE
    // ─────────────────────────────────────────────

    // Where the rivers' rim top is, in the world. Walls are built from it, the same as pipes.
    private float WallRimTopY => _data.canvasWorldY;

    /// <summary>A wall's nodes as the builder takes them: world x and z, y the top up from the rim top.</summary>
    private List<WallMesh.Node> WallNodes(LevelSelectDesignerData.DesignerWall wall)
    {
        float water = -_data.WaterSurfaceDrop;
        var   list  = new List<WallMesh.Node>(wall.nodes.Count);
        for (int i = 0; i < wall.nodes.Count; i++)
        {
            var n = wall.nodes[i];
            list.Add(new WallMesh.Node
            {
                top          = new Vector3(n.positionXZ.x, water + wall.HeightAt(i), n.positionXZ.y),
                curvedToNext = n.curvedToNext,
                gapToNext    = n.gapToNext,
                archway      = n.hasArchway,
                archWidth    = n.archWidth,
                archHeight   = n.archHeight,
                porthole       = n.hasPorthole,
                portholeRadius = n.portholeRadius,
                portholeHeight = n.portholeHeight,
                column       = n.hasColumn,
                columnRadius = n.columnRadius,
                columnHeight = n.columnHeight,
                lipHeight    = wall.LipHeight,
            });
        }
        return list;
    }

    /// <summary>The wall's lip as the builder takes it — null when it has none.</summary>
    private static WallLip WallLipFor(LevelSelectDesignerData.DesignerWall wall)
        => wall.hasLip && wall.lip != null && wall.lip.height > 0f ? wall.lip : null;

    private static Vector3 WallNodeWorldFlat(LevelSelectDesignerData.WallNode node)
        => new Vector3(node.positionXZ.x, 0f, node.positionXZ.y);

    // ─────────────────────────────────────────────
    // CANVAS — DRAWING
    // ─────────────────────────────────────────────

    private void DrawWalls()
    {
        if (Event.current.type != EventType.Repaint || _data.walls == null) return;

        foreach (var wall in _data.walls)
        {
            if (wall == null || wall.nodes.Count == 0) continue;
            bool selected = wall.wallId == _selectedWallId;

            var body = selected ? LevelSelectDesignerSettings.WallSelBodyColour : LevelSelectDesignerSettings.WallBodyColour;

            // The wall as a solid band its own thickness, curved where it will be curved.
            if (wall.nodes.Count >= 2)
            {
                float width = Mathf.Max(3f, wall.thickness * _zoom);
                foreach (var (points, loop) in WallMesh.CentreLines(WallNodes(wall), wall.closed))
                {
                    var canvas = points.Select(p => (Vector3)WorldToCanvas(p)).ToList();
                    if (loop && canvas.Count > 0) canvas.Add(canvas[0]);
                    Handles.color = body;
                    DrawSolidLine(width, canvas.ToArray());
                }
            }

            // Nodes: solid dots, orange where the node has its own height; a tower's base as a
            // wider dark disc under it.
            for (int i = 0; i < wall.nodes.Count; i++)
            {
                var     node = wall.nodes[i];
                Vector2 at   = WorldToCanvas(WallNodeWorldFlat(node));

                // A column as a solid disc its own size, in the wall's colour.
                if (node.hasColumn)
                {
                    Handles.color = body;
                    Handles.DrawSolidDisc(at, Vector3.forward, Mathf.Max(WallNodeRadius + 2f, node.columnRadius * _zoom));
                }

                if (node.hasTower)
                {
                    float baseR = Mathf.Max(TowerBaseRadius(node.towerKind, node.tower, node.turret, node.towerScale),
                                            TowerOrbRadius (node.towerKind, node.tower, node.turret, node.towerScale));
                    Handles.color = LevelSelectDesignerSettings.WallTowerColour;
                    Handles.DrawSolidDisc(at, Vector3.forward, Mathf.Max(WallNodeRadius + 3f, baseR * _zoom));
                }

                Handles.color = LevelSelectDesignerSettings.WallOutlineColour;
                Handles.DrawSolidDisc(at, Vector3.forward, WallNodeRadius + 1.5f);
                Handles.color = selected && i == _selectedWallNode ? LevelSelectDesignerSettings.WallSelColour
                              : node.overrideHeight               ? LevelSelectDesignerSettings.WallOwnHeightColour
                              : body;
                Handles.DrawSolidDisc(at, Vector3.forward, WallNodeRadius);
            }
        }

        // The ghost stretch out to the cursor while a wall is being laid.
        var drawing = _isDrawingWall ? SelectedWall : null;
        if (drawing != null && drawing.nodes.Count > 0 && _canvasRect.Contains(Event.current.mousePosition))
        {
            Handles.color = LevelSelectDesignerSettings.WallGhostColour;
            Handles.DrawDottedLine(WorldToCanvas(WallNodeWorldFlat(drawing.nodes[drawing.nodes.Count - 1])),
                                   Event.current.mousePosition, 5f);
        }
    }

    // ─────────────────────────────────────────────
    // CANVAS — HIT TESTING
    // ─────────────────────────────────────────────

    private bool FindWallNodeAtCanvas(Vector2 canvas, out string wallId, out int node)
    {
        wallId = null; node = -1;
        float reach = WallHitReach * WallHitReach;
        for (int w = _data.walls.Count - 1; w >= 0; w--)
        {
            var wall = _data.walls[w];
            for (int i = wall.nodes.Count - 1; i >= 0; i--)
            {
                if ((canvas - WorldToCanvas(WallNodeWorldFlat(wall.nodes[i]))).sqrMagnitude > reach) continue;
                wallId = wall.wallId; node = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>The wall under the cursor, measured against the line it is really built along.</summary>
    private bool FindWallAtCanvas(Vector2 canvas, out string wallId)
    {
        wallId = null;
        for (int w = _data.walls.Count - 1; w >= 0; w--)
        {
            var wall = _data.walls[w];
            if (wall.nodes.Count < 2) continue;

            float reach = Mathf.Max(WallHitReach, wall.thickness * _zoom * 0.5f + 3f);
            foreach (var (points, loop) in WallMesh.CentreLines(WallNodes(wall), wall.closed))
            {
                int legs = loop ? points.Count : points.Count - 1;
                for (int i = 0; i < legs; i++)
                {
                    Vector2 a = WorldToCanvas(points[i]);
                    Vector2 b = WorldToCanvas(points[(i + 1) % points.Count]);
                    if (DistanceToSegment(canvas, a, b) > reach) continue;
                    wallId = wall.wallId;
                    return true;
                }
            }
        }
        return false;
    }

    // ─────────────────────────────────────────────
    // CANVAS — EDITING
    // ─────────────────────────────────────────────

    private void HandleWallMode(Event e)
    {
        if (e.type == EventType.KeyDown)
        {
            if (_isDrawingWall && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter ||
                                   e.keyCode == KeyCode.Escape))
            {
                FinishDrawingWall();
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                DeleteSelectedWallPart();
                e.Use();
                return;
            }
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (_isDrawingWall)
            {
                if (e.clickCount >= 2) FinishDrawingWall();
                else                   AddWallNode(SelectedWall, e.mousePosition);
                e.Use();
                return;
            }

            if (FindWallNodeAtCanvas(e.mousePosition, out string nodeWall, out int node))
            {
                SelectWall(nodeWall, node);
                _wallDragOffset = e.mousePosition - WorldToCanvas(WallNodeWorldFlat(SelectedWall.nodes[node]));
                _isDraggingWall = true;
                e.Use();
                return;
            }

            if (FindWallAtCanvas(e.mousePosition, out string wallId))
            {
                SelectWall(wallId, -1);
                e.Use();
                return;
            }

            // Empty space: a new wall starts here.
            Undo.RecordObject(_data, "Add Wall");
            var fresh = new LevelSelectDesignerData.DesignerWall { wallId = Guid.NewGuid().ToString() };
            _data.walls.Add(fresh);
            SelectWall(fresh.wallId, -1);
            _isDrawingWall = true;
            AddWallNode(fresh, e.mousePosition);
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 1)
        {
            if (FindWallNodeAtCanvas(e.mousePosition, out string nodeWall, out int node))
            {
                SelectWall(nodeWall, node);
                DeleteSelectedWallPart();
                e.Use();
            }
            return;
        }

        if (e.type == EventType.MouseDrag && _isDraggingWall)
        {
            var wall = SelectedWall;
            if (wall != null && _selectedWallNode >= 0 && _selectedWallNode < wall.nodes.Count)
            {
                Undo.RecordObject(_data, "Move Wall Node");
                wall.nodes[_selectedWallNode].positionXZ = CanvasToWorld2D(e.mousePosition - _wallDragOffset);
                MarkDirty();
            }
            Repaint();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && _isDraggingWall)
        {
            _isDraggingWall = false;
            RebuildWall(SelectedWall);
            e.Use();
        }
    }

    private void SelectWall(string wallId, int node)
    {
        _selectedWallId   = wallId;
        _selectedWallNode = node;
        GUI.FocusControl(null);
        Repaint();
    }

    private void AddWallNode(LevelSelectDesignerData.DesignerWall wall, Vector2 canvas)
    {
        if (wall == null) return;
        Undo.RecordObject(_data, "Add Wall Node");
        wall.nodes.Add(new LevelSelectDesignerData.WallNode { positionXZ = CanvasToWorld2D(canvas) });
        _selectedWallNode = wall.nodes.Count - 1;
        MarkDirty();
        Repaint();
    }

    // A wall needs two nodes to be a wall; one finished with fewer is taken away again.
    private void FinishDrawingWall()
    {
        _isDrawingWall = false;
        var wall = SelectedWall;
        if (wall != null && wall.nodes.Count < 2)
        {
            Undo.RecordObject(_data, "Remove Wall");
            RemoveWall(wall);
            MarkDirty();
        }
        else
        {
            RebuildWall(wall);
        }
        _selectedWallNode = -1;
        Repaint();
    }

    /// <summary>Removes the selected node, else the whole selected wall.</summary>
    private void DeleteSelectedWallPart()
    {
        var wall = SelectedWall;
        if (wall == null) return;

        if (_selectedWallNode >= 0 && _selectedWallNode < wall.nodes.Count)
        {
            Undo.RecordObject(_data, "Delete Wall Node");
            wall.nodes.RemoveAt(_selectedWallNode);
            _selectedWallNode = -1;
            if (wall.nodes.Count < 2 && !_isDrawingWall) RemoveWall(wall);
            else                                         RebuildWall(wall);
            MarkDirty();
        }
        else
        {
            Undo.RecordObject(_data, "Delete Wall");
            RemoveWall(wall);
            MarkDirty();
        }
        Repaint();
    }

    private void RemoveWall(LevelSelectDesignerData.DesignerWall wall)
    {
        _data.walls.Remove(wall);
        var record = FindObjectsOfType<WallMesh>().FirstOrDefault(r => r.wallId == wall.wallId);
        if (record != null) Undo.DestroyObjectImmediate(record.gameObject);
        _selectedWallId   = null;
        _selectedWallNode = -1;
        _isDrawingWall    = false;
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private void DrawSelectedWallProps()
    {
        if (_mode != DesignerMode.Wall && _mode != DesignerMode.Select) return;
        var wall = SelectedWall;
        if (wall == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Wall", EditorStyles.boldLabel);

        DrawWallSettings(wall);

        EditorGUI.BeginChangeCheck();
        bool closed = EditorGUILayout.Toggle(
            new GUIContent("Closed", "Join the last node back to the first."), wall.closed);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Wall");
            wall.closed = closed;
            MarkDirty();
            RebuildWall(wall);
        }

        DrawWallNodeList(wall);
        DrawSelectedWallNodeProps(wall);

        EditorGUILayout.Space(2);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("Delete Wall"))
        {
            Undo.RecordObject(_data, "Delete Wall");
            RemoveWall(wall);
            MarkDirty();
        }
        GUI.backgroundColor = Color.white;
    }

    internal const string WallPresetFolder = "Assets/ScriptsData/DataScripts/WallPresets";

    /// <summary>
    /// The wall's preset and the settings it carries — height, thickness and lip. Pick a preset
    /// from the dropdown to put its settings on this wall, Edit to open it in the preset editor
    /// (every wall using it changes with it), or New to save this wall's settings as one. Linked,
    /// the settings show here but are changed in the preset; with no preset they're edited here.
    /// </summary>
    private void DrawWallSettings(LevelSelectDesignerData.DesignerWall wall)
    {
        var picked = LevelSelectPresetEditorWindow.DrawPresetLink(
            new GUIContent("Wall Preset", "The wall presets kept in " + WallPresetFolder +
                                          ". Picking one puts its height, thickness and lip on this wall; Edit changes them."),
            wall.wallPreset, WallPresetFolder, "Wall",
            () =>
            {
                var made = CreateInstance<WallPreset>();
                made.CopyFrom(WallSettings.Of(wall));
                return made;
            },
            made =>
            {
                if (_data == null) return;
                Undo.RecordObject(_data, "New Wall Preset");
                wall.wallPreset = made;
                MarkDirty();
                Repaint();
            });

        if (picked != wall.wallPreset)
        {
            Undo.RecordObject(_data, "Load Wall Preset");
            wall.wallPreset = picked;
            if (picked != null) picked.Build().ApplyTo(wall);
            MarkDirty();
            GUI.FocusControl(null);
            RebuildWall(wall);
        }

        if (wall.wallPreset != null)
        {
            using (new EditorGUI.DisabledScope(true))
                DrawWallSettingsFields(WallSettings.Of(wall));
            EditorGUILayout.LabelField("Set in the preset — Edit changes every wall using it.",
                                       EditorStyles.wordWrappedMiniLabel);
            return;
        }

        EditorGUI.BeginChangeCheck();
        var edited = DrawWallSettingsFields(WallSettings.Of(wall));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Wall");
            edited.ApplyTo(wall);
            MarkDirty();
            RebuildAfterEdit(wall, () => RebuildWall(wall));
        }
    }

    /// <summary>
    /// A wall's height, thickness and lip fields. Hands back an edited copy, so the caller writes
    /// it only inside its own change check.
    /// </summary>
    internal static WallSettings DrawWallSettingsFields(WallSettings settings)
    {
        var edited = (settings ?? new WallSettings()).Clone();
        edited.height = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Height", "How far the wall's top stands above the water, at every node " +
                                     "that doesn't override it."), edited.height));
        edited.thickness = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Thickness", "The wall's thickness, across."), edited.thickness));
        edited.hasLip = EditorGUILayout.Toggle(
            new GUIContent("Lip", "Stand a square lip on the wall's top all the way along, going round every column."),
            edited.hasLip);
        if (!edited.hasLip) return edited;

        var lip = edited.lip;
        EditorGUI.indentLevel++;
        lip.height = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Height", "How far the lip stands above the wall's top."), lip.height));
        lip.thickness = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Thickness", "The lip's thickness across, centred on the wall. Thicker than the " +
                                        "wall overhangs it both sides."), lip.thickness));
        lip.columnRing = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Column Ring", "How far the lip's ring round a column stands out from the column's " +
                                          "side. 0 leaves columns without one."), lip.columnRing));
        EditorGUI.indentLevel--;
        return edited;
    }

    /// <summary>
    /// Every node in order, with the stretch on to the next one under it — Curved or straight,
    /// and Gap — as in the Grid Designer's spline wall list.
    /// </summary>
    private void DrawWallNodeList(LevelSelectDesignerData.DesignerWall wall)
    {
        int count = wall.nodes.Count;
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField($"Nodes  ({count})", EditorStyles.miniBoldLabel);
        if (count < 2) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Straighten All", "Make every stretch of this wall straight."),
                                 EditorStyles.miniButtonLeft))
                SetAllWallStretchesCurved(wall, false);
            if (GUILayout.Button(new GUIContent("Curve All", "Make every stretch of this wall curved."),
                                 EditorStyles.miniButtonRight))
                SetAllWallStretchesCurved(wall, true);
        }

        int insertAt = -1;
        for (int i = 0; i < count; i++)
        {
            var node = wall.nodes[i];
            bool isSelected = i == _selectedWallNode;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                string label = $"Node {i + 1}" + (node.hasColumn ? "  ▮ column" : "") +
                               (node.hasTower ? "  ● tower" : "") +
                               (node.hasArchway ? "  ∩ arch" : "") +
                               (node.hasPorthole ? "  ◯ porthole" : "") +
                               (node.overrideHeight ? $"  h {node.height:0.##}" : "");
                if (GUILayout.Button(label, EditorStyles.miniButton))
                    SelectWall(wall.wallId, i);
                GUI.backgroundColor = Color.white;

                // Before/After and the stretch controls only on the selected node, to keep the list readable.
                if (!isSelected) continue;

                if (GUILayout.Button(new GUIContent("+ Before", "Add a node between this one and the one before it."),
                                     EditorStyles.miniButtonLeft, GUILayout.Width(58)))
                    insertAt = i;
                if (GUILayout.Button(new GUIContent("+ After", "Add a node between this one and the one after it."),
                                     EditorStyles.miniButtonRight, GUILayout.Width(52)))
                    insertAt = i + 1;
            }

            // The stretch on to the next node — the last node only has one when the wall is closed.
            bool hasNext = i < count - 1 || wall.closed;
            if (!hasNext) continue;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(16);
                EditorGUI.BeginChangeCheck();
                bool curved = GUILayout.Toggle(node.curvedToNext,
                    new GUIContent("Curved", "The stretch to the next node bends through its neighbours. " +
                                             "Off, it runs straight."), EditorStyles.miniButtonLeft);
                bool gap = GUILayout.Toggle(node.gapToNext,
                    new GUIContent("Gap", "Leave out the wall between this node and the next."),
                    EditorStyles.miniButtonRight);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Edit Wall Stretch");
                    node.curvedToNext = curved;
                    node.gapToNext    = gap;
                    MarkDirty();
                    RebuildWall(wall);
                }
            }
        }

        // Carried out once the list has finished drawing rather than changing it half way down.
        if (insertAt >= 0)
        {
            Undo.RecordObject(_data, "Add Wall Node");
            int added = InsertWallNode(wall, insertAt);
            SelectWall(wall.wallId, added);
            MarkDirty();
            RebuildWall(wall);
            Repaint();
        }
    }

    /// <summary>
    /// Adds a node at `at` (0 = off the start, Count = off the end, else between at-1 and at):
    /// halfway between the two nodes either side, or off an open end by half the end stretch's
    /// length. On a closed wall both ends are the stretch from the last node back to the first.
    /// The new node's stretch on is curved or gapped the same as the stretch it split.
    /// Returns where the new node went.
    /// </summary>
    private static int InsertWallNode(LevelSelectDesignerData.DesignerWall wall, int at)
    {
        var nodes = wall.nodes;
        int count = nodes.Count;
        if (wall.closed && (at <= 0 || at >= count)) at = count;

        int prev, next;
        LevelSelectDesignerData.WallNode fresh;
        if ((at > 0 && at < count) || wall.closed)
        {
            prev  = at - 1;
            next  = at % count;
            fresh = new LevelSelectDesignerData.WallNode
            {
                positionXZ   = (nodes[prev].positionXZ + nodes[next].positionXZ) * 0.5f,
                curvedToNext = nodes[prev].curvedToNext,
                gapToNext    = nodes[prev].gapToNext,
            };
        }
        else
        {
            bool atStart = at <= 0;
            prev = next = atStart ? 0 : count - 1;
            Vector2 end   = nodes[prev].positionXZ;
            Vector2 inner = nodes[atStart ? 1 : count - 2].positionXZ;
            fresh = new LevelSelectDesignerData.WallNode
            {
                positionXZ   = end + (end - inner) * 0.5f,
                curvedToNext = nodes[atStart ? 0 : count - 2].curvedToNext,
            };
            at = atStart ? 0 : count;
        }

        // A new node between two with their own heights sits between those heights.
        if (nodes[prev].overrideHeight || nodes[next].overrideHeight)
        {
            fresh.overrideHeight = true;
            fresh.height         = (wall.HeightAt(prev) + wall.HeightAt(next)) * 0.5f;
        }

        nodes.Insert(at, fresh);
        return at;
    }

    private void SetAllWallStretchesCurved(LevelSelectDesignerData.DesignerWall wall, bool curved)
    {
        Undo.RecordObject(_data, curved ? "Curve All Wall Stretches" : "Straighten All Wall Stretches");
        foreach (var node in wall.nodes) node.curvedToNext = curved;
        MarkDirty();
        RebuildWall(wall);
    }

    /// <summary>The selected node's own height, and the lollipop tower standing on it.</summary>
    private void DrawSelectedWallNodeProps(LevelSelectDesignerData.DesignerWall wall)
    {
        if (_selectedWallNode < 0 || _selectedWallNode >= wall.nodes.Count) return;
        var node = wall.nodes[_selectedWallNode];

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Node {_selectedWallNode + 1}", EditorStyles.miniBoldLabel);

        EditorGUI.BeginChangeCheck();
        bool nodeOverride = EditorGUILayout.Toggle(
            new GUIContent("Override Height", "Give this node its own height. Left off, it takes the wall's."),
            node.overrideHeight);
        float nodeHeight;
        using (new EditorGUI.DisabledScope(!nodeOverride))
            nodeHeight = EditorGUILayout.FloatField(
                new GUIContent("Height", "How far the wall's top stands above the water at this node."),
                nodeOverride ? node.height : wall.height);

        bool hasColumn = EditorGUILayout.Toggle(
            new GUIContent("Column", "Make this node a round column, from the wall's base up past its " +
                                     "top. A tower on this node stands on the column's top."),
            node.hasColumn);
        float columnRadius = node.columnRadius, columnHeight = node.columnHeight;
        if (hasColumn)
        {
            EditorGUI.indentLevel++;
            columnRadius = EditorGUILayout.FloatField(
                new GUIContent("Radius", "The column's radius."), node.columnRadius);
            columnHeight = EditorGUILayout.FloatField(
                new GUIContent("Height", "How far the column's top stands above the wall's top here — " +
                                         "above the lip's top, when the wall has a lip."),
                node.columnHeight);
            EditorGUI.indentLevel--;
        }

        bool hasTower = EditorGUILayout.Toggle(
            new GUIContent("Tower", "Stand a tower on top of the wall at this node — a lollipop or a turret."),
            node.hasTower);

        var           towerKind  = node.towerKind;
        LollipopTower tower      = node.tower;
        TurretTower   turret     = node.turret;
        float         towerScale = node.towerScale;
        var           perch      = node.towerPerch;
        if (hasTower)
        {
            EditorGUI.indentLevel++;
            towerKind = DrawTowerKindField(node.towerKind);
            if (towerKind == LevelSelectDesignerData.TowerKind.Turret)
            {
                DrawTurretPresetRow(node.turretPreset, node.turret,
                                    p => node.turretPreset = p, t => node.turret = t);
                turret     = node.turret;   // read back: picking a preset just loaded it
                towerScale = DrawLollipopScaleField(node.towerScale);
            }
            else
            {
                DrawLollipopPresetRow(node.towerPreset, node.tower,
                                      p => node.towerPreset = p, t => node.tower = t);
                tower      = node.tower;   // read back: picking a preset just loaded it
                towerScale = DrawLollipopScaleField(node.towerScale);
            }
            perch = DrawPerchBlock(node.towerPerch);
            EditorGUI.indentLevel--;
        }

        bool hasArchway = EditorGUILayout.Toggle(
            new GUIContent("Archway", "Cut an archway through the wall, centred on this node."),
            node.hasArchway);
        float archWidth = node.archWidth, archHeight = node.archHeight;
        if (hasArchway)
        {
            EditorGUI.indentLevel++;
            archWidth = EditorGUILayout.FloatField(
                new GUIContent("Width", "How wide the archway is, measured along the wall."), node.archWidth);
            archHeight = EditorGUILayout.FloatField(
                new GUIContent("Height", "How tall the archway is, from the wall's base up to the top of its round."),
                node.archHeight);
            EditorGUI.indentLevel--;
        }

        bool hasPorthole = EditorGUILayout.Toggle(
            new GUIContent("Porthole", "Bore a round hole straight across the wall, centred on this node."),
            node.hasPorthole);
        float portholeRadius = node.portholeRadius, portholeHeight = node.portholeHeight;
        if (hasPorthole)
        {
            EditorGUI.indentLevel++;
            portholeRadius = EditorGUILayout.FloatField(
                new GUIContent("Radius", "The porthole's radius."), node.portholeRadius);
            portholeHeight = EditorGUILayout.FloatField(
                new GUIContent("Height", "How high the porthole's centre is, from the wall's base. " +
                                         "Reaching past the base or the top opens the wall there."),
                node.portholeHeight);
            EditorGUI.indentLevel--;
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Wall Node");
            // Switching the override on starts it from the height the node already had.
            if (nodeOverride && !node.overrideHeight) nodeHeight = wall.height;
            node.overrideHeight = nodeOverride;
            if (nodeOverride) node.height = Mathf.Max(0f, nodeHeight);
            node.hasColumn    = hasColumn;
            node.columnRadius = Mathf.Max(0.001f, columnRadius);
            node.columnHeight = Mathf.Max(0f, columnHeight);
            node.hasTower   = hasTower;
            node.towerKind  = towerKind;
            node.tower      = tower;
            node.turret     = turret;
            node.towerScale = towerScale;
            node.towerPerch = perch;
            node.hasArchway = hasArchway;
            node.archWidth  = Mathf.Max(0f, archWidth);
            node.archHeight = Mathf.Max(0f, archHeight);
            node.hasPorthole    = hasPorthole;
            node.portholeRadius = Mathf.Max(0f, portholeRadius);
            node.portholeHeight = Mathf.Max(0f, portholeHeight);
            MarkDirty();
            RebuildAfterTowerEdit(wall, () => RebuildWall(wall));
        }
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    private void GenerateWalls(GameObject wallsParent)
    {
        foreach (var wall in _data.walls)
        {
            if (wall == null || string.IsNullOrEmpty(wall.wallId) || wall.nodes.Count < 2) continue;

            var existing = FindObjectsOfType<WallMesh>().FirstOrDefault(r => r.wallId == wall.wallId);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject(WallMeshName(wall));
                Undo.RegisterCreatedObjectUndo(go, "Generate Wall");
                go.transform.SetParent(wallsParent.transform, false);
                go.AddComponent<WallMesh>();
            }

            BuildWall(wall, go);
        }
    }

    /// <summary>
    /// Rebuilds one wall in the scene. A wall not generated yet is built now, once the WALLS
    /// parent is there — so a wall drawn after the last Generate still shows up as it is drawn.
    /// </summary>
    private void RebuildWall(LevelSelectDesignerData.DesignerWall wall)
    {
        if (wall == null || wall.nodes.Count < 2) return;

        var record = FindObjectsOfType<WallMesh>().FirstOrDefault(r => r.wallId == wall.wallId);
        if (record != null)
        {
            BuildWall(wall, record.gameObject);
        }
        else
        {
            var parent = GameObject.Find(WallsParent);
            if (parent == null) return;
            var go = new GameObject(WallMeshName(wall));
            Undo.RegisterCreatedObjectUndo(go, "Generate Wall");
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<WallMesh>();
            BuildWall(wall, go);
        }
        AssetDatabase.SaveAssets();
    }

    private void BuildWall(LevelSelectDesignerData.DesignerWall wall, GameObject go)
    {
        string name = WallMeshName(wall);

        // Built in world x and z, with y measured from the rim top, so the stone shading places
        // its waterline off the same rim the rivers use.
        Undo.RecordObject(go.transform, "Place Wall");
        go.name                 = name;
        go.transform.position   = new Vector3(0f, WallRimTopY, 0f);
        go.transform.rotation   = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var nodes  = WallNodes(wall);
        var seams  = new WallMesh.SeamReport();
        var meshes = WallMesh.BuildChunks(nodes, wall.closed, wall.thickness, -_data.RunDepth, StoneShadingForBuild,
                                          wall.removedSeams, seams, wall.faceGroups, WallLipFor(wall));
        KeepWallSeams(wall, seams);

        // The wall's own mesh from when it was built as one piece — the chunks carry it now.
        var oneCollider = go.GetComponent<MeshCollider>();
        if (oneCollider != null) Undo.DestroyObjectImmediate(oneCollider);
        var oneRenderer = go.GetComponent<MeshRenderer>();
        if (oneRenderer != null) Undo.DestroyObjectImmediate(oneRenderer);
        var oneFilter = go.GetComponent<MeshFilter>();
        if (oneFilter != null) Undo.DestroyObjectImmediate(oneFilter);

        // One child per chunk, cleared and built afresh each time; the mesh assets are reused by name.
        var old = go.transform.Find(WallChunksChild);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var container = new GameObject(WallChunksChild);
        Undo.RegisterCreatedObjectUndo(container, "Generate Wall");
        container.transform.SetParent(go.transform, false);

        for (int k = 0; k < meshes.Count; k++)
        {
            string chunkName = WallChunkName(name, k);
            var    mesh      = SaveGeneratedMesh(chunkName, meshes[k]);
            if (mesh == null) continue;

            var chunk = NewMeshChild(container, chunkName, mesh);

            // Collides as exactly the shape it is drawn.
            chunk.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // Chunks left over from a wall that had more of them last time.
        for (int k = meshes.Count; ; k++)
        {
            string stale = $"{GeneratedDir}/{WallChunkName(name, k)}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(stale) == null) break;
            AssetDatabase.DeleteAsset(stale);
        }

        var record = go.GetComponent<WallMesh>();
        record.wallId        = wall.wallId;
        record.meshAssetName = name;
        EditorUtility.SetDirty(record);

        RefreshWallTowers(wall, go, nodes);
    }

    /// <summary>
    /// Stands a lollipop tower on the wall's top at every node that carries one, under a
    /// "Towers" child of the wall — cleared and built afresh each time the wall is.
    /// </summary>
    private void RefreshWallTowers(LevelSelectDesignerData.DesignerWall wall, GameObject go,
                                   List<WallMesh.Node> nodes)
    {
        var old = go.transform.Find(WallTowersChild);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        bool Builds(LevelSelectDesignerData.WallNode n) =>
            n.hasTower && (n.towerKind == LevelSelectDesignerData.TowerKind.Turret ? n.turret != null : n.tower != null);

        if (!wall.nodes.Exists(Builds)) return;

        var container = new GameObject(WallTowersChild);
        Undo.RegisterCreatedObjectUndo(container, "Generate Wall Towers");
        container.transform.SetParent(go.transform, false);

        for (int i = 0; i < wall.nodes.Count; i++)
        {
            var node = wall.nodes[i];
            if (!Builds(node)) continue;

            string towerName = $"{WallMeshName(wall)}_Tower{i + 1}";

            if (node.towerKind == LevelSelectDesignerData.TowerKind.Turret)
            {
                var turret = node.turret.Scaled(node.towerScale);
                var built  = PlaceTurret(container.transform, towerName, turret, _data.riverMaterial);
                if (built == null) continue;

                built.transform.localPosition = nodes[i].StandOn;
                built.transform.localRotation = Quaternion.identity;
                built.transform.localScale    = Vector3.one;

                ApplyPerch(built, node.towerPerch, Vector3.up * turret.TopY);
                continue;
            }

            var    placed    = node.tower.Scaled(node.towerScale);
            var    mesh      = SaveGeneratedMesh(towerName, placed.Build(StoneShadingForBuild));
            if (mesh == null) continue;

            // The wall sits unrotated at the rim top, so the node's top is already local — the
            // column's top, where the node has one.
            var tower = NewMeshChild(container, towerName, mesh);
            tower.transform.localPosition = nodes[i].StandOn;
            tower.transform.localRotation = Quaternion.identity;
            tower.transform.localScale    = Vector3.one;

            // Her feet go on the top of the orb.
            ApplyPerch(tower, node.towerPerch, Vector3.up * placed.TopY);
        }
    }

    // Named off the wall's id, which survives every regenerate — so the mesh asset is reused.
    private static string WallMeshName(LevelSelectDesignerData.DesignerWall wall)
        => $"Wall_{SanitiseAssetName(wall.wallId)}";

    private static string WallChunkName(string wallName, int chunk) => $"{wallName}_Chunk{chunk + 1}";
}

#endif
