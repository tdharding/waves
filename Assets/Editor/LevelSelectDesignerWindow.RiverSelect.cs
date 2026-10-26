using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// River Select — the second tool under the Select button's arrow.
///
/// Click a river to pick it whole, Shift+click to add or take away more, and drag any picked river
/// to move every picked river together. A river moves by all of its nodes, so whatever hangs off
/// them (pools, arenas, outposts, obstacles, boat exits) goes with it. An arena's other doors go
/// with its arena. A node shared with a river that is not picked still moves, so that river
/// stretches to meet it rather than coming apart.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // Which Select tool the Select button gives — the last one picked from its arrow.
    private DesignerMode _selectVariant = DesignerMode.Select;

    private readonly List<string> _riverSelectIds = new();

    private bool    _isDraggingRivers;
    private bool    _riverDragRecorded;
    private Vector3 _riverDragStartWorld;
    private readonly Dictionary<string, Vector3> _riverDragStartPositions = new();

    private bool IsRiverSelected(string pathId)
        => _mode == DesignerMode.RiverSelect && _riverSelectIds.Contains(pathId);

    private void ClearRiverSelect()
    {
        _riverSelectIds.Clear();
        _isDraggingRivers = false;
        _riverDragStartPositions.Clear();
    }

    private void HandleRiverSelectMode(Event e)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            string pathId = FindPathAtCanvas(e.mousePosition);

            if (e.shift)
            {
                if (pathId == null) { e.Use(); return; }
                if (_riverSelectIds.Remove(pathId))
                {
                    if (_selectedPathId == pathId)
                        _selectedPathId = _riverSelectIds.Count > 0 ? _riverSelectIds[_riverSelectIds.Count - 1] : null;
                    Repaint();
                    e.Use();
                    return;
                }
                _riverSelectIds.Add(pathId);
            }
            else if (pathId == null)
            {
                _riverSelectIds.Clear();
                _selectedPathId = null;
                Repaint();
                e.Use();
                return;
            }
            else if (!_riverSelectIds.Contains(pathId))
            {
                _riverSelectIds.Clear();
                _riverSelectIds.Add(pathId);
            }

            // The river clicked last is the one the left panel shows.
            _selectedPathId      = pathId;
            _selectedNodeId      = null;
            _selectedObstacleId  = null;
            _selectedArenaNodeId = null;
            _selectedShopNodeId  = null;
            _selectedPoolNodeId  = null;
            _selectedEntranceIdx = -1;
            ClearWallAndPipeSelection();

            BeginRiverDrag(e.mousePosition);
            Repaint();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && _isDraggingRivers)
        {
            if (!_riverDragRecorded)
            {
                Undo.RecordObject(_data, _riverSelectIds.Count > 1 ? "Move Rivers" : "Move River");
                _riverDragRecorded = true;
            }

            Vector3 delta = CanvasToWorldPos(e.mousePosition) - _riverDragStartWorld;
            delta.y = 0f;
            foreach (var node in _data.nodes)
                if (_riverDragStartPositions.TryGetValue(node.id, out Vector3 start))
                    node.worldPosition = start + delta;
            // In-between nodes on a river shared with an unpicked one land between its nodes.
            SyncInBetweenNodes();

            MarkDirty();
            Repaint();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && _isDraggingRivers)
        {
            _isDraggingRivers = false;
            _riverDragStartPositions.Clear();
            e.Use();
        }
    }

    /// Notes where every node of the picked rivers starts, so the drag moves them by one offset.
    private void BeginRiverDrag(Vector2 mouse)
    {
        _riverDragStartPositions.Clear();
        foreach (string pathId in _riverSelectIds)
        {
            var path = _data.paths.Find(p => p.pathId == pathId);
            if (path == null) continue;
            foreach (string nodeId in path.nodeIds)
                AddRiverDragNode(nodeId);
        }

        // An arena's other doors stand round it, so they go wherever its own node goes.
        foreach (var arena in _data.arenas)
        {
            if (!_riverDragStartPositions.ContainsKey(arena.nodeId)) continue;
            foreach (var entrance in arena.secondaryEntrances)
                AddRiverDragNode(entrance.nodeId);
        }

        _riverDragStartWorld = CanvasToWorldPos(mouse);
        _riverDragRecorded   = false;
        _isDraggingRivers    = _riverDragStartPositions.Count > 0;
    }

    /// The left panel while River Select is on — just the picked rivers, nothing else.
    private void DrawRiverSelectList()
    {
        DrawMergeRiversPanel();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"Selected Rivers ({_riverSelectIds.Count})", EditorStyles.boldLabel);

        if (_riverSelectIds.Count == 0)
        {
            EditorGUILayout.LabelField("None — click a river on the canvas.", EditorStyles.miniLabel);
            return;
        }

        foreach (string pathId in _riverSelectIds)
        {
            var path = _data.paths.Find(p => p.pathId == pathId);
            if (path == null) continue;

            EditorGUILayout.BeginHorizontal();
            Rect swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(16f));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(swatch.x, swatch.y + 3f, 10f, 10f), path.editorColor);
            EditorGUILayout.LabelField(PathDisplayName(path));
            EditorGUILayout.EndHorizontal();
        }

        DrawRiverHeightOffset();
    }

    // Each picked node's height when these rivers were picked, so Raise / Lower reads as one
    // offset from there. Retaken whenever the picked rivers change.
    private readonly Dictionary<string, float> _riverHeightStart = new();
    private string _riverHeightPicked;

    /// <summary>
    /// Raise / Lower — moves every node of the picked rivers up or down by one amount, so each
    /// keeps its slopes. A node another, unpicked river also runs through stays where it is.
    /// Lead-ins and in-between nodes aren't moved here; they follow the nodes they hang off.
    /// Like a node's own height, it reaches the scene on the next Generate.
    /// </summary>
    private void DrawRiverHeightOffset()
    {
        string picked = string.Join("|", _riverSelectIds);
        if (picked != _riverHeightPicked)
        {
            _riverHeightPicked = picked;
            _riverHeightStart.Clear();
            foreach (string pathId in _riverSelectIds)
            {
                var path = _data.paths.Find(p => p.pathId == pathId);
                if (path == null) continue;
                foreach (string nodeId in path.nodeIds)
                {
                    if (_riverHeightStart.ContainsKey(nodeId)) continue;
                    if (IsLockedNode(nodeId) || IsPlacedInBetween(nodeId)) continue;
                    if (_data.paths.Exists(p => !_riverSelectIds.Contains(p.pathId) && p.nodeIds.Contains(nodeId)))
                        continue;
                    var node = _data.nodes.Find(n => n.id == nodeId);
                    if (node != null) _riverHeightStart[nodeId] = node.worldPosition.y;
                }
            }
        }

        // The offset shown is how far the first picked node now stands from where it started.
        float offset = 0f;
        foreach (var start in _riverHeightStart)
        {
            var node = _data.nodes.Find(n => n.id == start.Key);
            if (node == null) continue;
            offset = node.worldPosition.y - start.Value;
            break;
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        GUI.enabled = _riverHeightStart.Count > 0;
        float newOffset = EditorGUILayout.FloatField(
            new GUIContent("↔ Raise / Lower",
                "Drag left to lower every node of the picked rivers, right to raise them, all by the " +
                "same amount — measured from where they stood when picked. Nodes shared with a river " +
                "that isn't picked stay put. Press ⟳ to see it in the scene."),
            offset);
        bool changed = EditorGUI.EndChangeCheck();
        if (GUILayout.Button(new GUIContent("⟳", "Regenerate the scene so it shows the river heights as they are now."),
                             EditorStyles.miniButton, GUILayout.Width(22)))
            QueueLiveRegenerate();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        if (!changed) return;

        Undo.RecordObject(_data, _riverSelectIds.Count > 1 ? "Raise / Lower Rivers" : "Raise / Lower River");
        foreach (var start in _riverHeightStart)
        {
            var node = _data.nodes.Find(n => n.id == start.Key);
            if (node == null) continue;
            node.worldPosition = new Vector3(node.worldPosition.x, start.Value + newOffset, node.worldPosition.z);
        }
        SyncInBetweenNodes();

        MarkDirty();
        Repaint();
    }

    private void AddRiverDragNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || _riverDragStartPositions.ContainsKey(nodeId)) return;
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node != null) _riverDragStartPositions[nodeId] = node.worldPosition;
    }
}

#endif
