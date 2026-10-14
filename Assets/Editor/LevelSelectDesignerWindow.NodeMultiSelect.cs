using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Several river nodes picked at once in Select mode.
///
/// Shift+click a node to add it, Shift+click a picked node to take it away. Dragging any picked
/// node moves them all by one offset. Each node moves as it would on its own: lead-ins, compass
/// entrances and in-between nodes stay where their owner puts them, and an arena's other doors
/// stay on its ring. The node clicked last is the one the left panel shows (_selectedNodeId).
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private readonly List<string> _multiNodeIds = new();

    private bool    _isDraggingNodeGroup;
    private bool    _nodeGroupDragRecorded;
    private Vector3 _nodeGroupDragStartWorld;
    private readonly Dictionary<string, Vector3> _nodeGroupDragStartPositions = new();

    /// The group only counts while the node the left panel shows is still one of it — anything
    /// that picks something else clears _selectedNodeId, and so drops the group with it.
    private bool NodeGroupActive
        => _mode == DesignerMode.Select && _multiNodeIds.Count > 1 &&
           _selectedNodeId != null && _multiNodeIds.Contains(_selectedNodeId);

    private bool IsNodeMultiSelected(string nodeId)
        => NodeGroupActive && _multiNodeIds.Contains(nodeId);

    /// Shift+click on a node. Returns the node the left panel should now show (null when the
    /// last picked node was taken away).
    private string ToggleNodeInGroup(string nodeId)
    {
        // Starting fresh: keep the node already picked alone, if there is one.
        if (!NodeGroupActive)
        {
            _multiNodeIds.Clear();
            if (_selectedNodeId != null && _selectedNodeId != nodeId) _multiNodeIds.Add(_selectedNodeId);
        }

        if (_multiNodeIds.Remove(nodeId))
            return _multiNodeIds.Count > 0 ? _multiNodeIds[_multiNodeIds.Count - 1] : null;

        _multiNodeIds.Add(nodeId);
        return nodeId;
    }

    /// A plain click: a node already in the group keeps the group (so it can be dragged);
    /// any other node starts again on its own.
    private void PickNodeAlone(string nodeId)
    {
        if (IsNodeMultiSelected(nodeId)) return;
        _multiNodeIds.Clear();
        _multiNodeIds.Add(nodeId);
    }

    /// Notes where every picked node starts. Returns false when there is no group to drag.
    private bool BeginNodeGroupDrag(Vector2 mouse)
    {
        _nodeGroupDragStartPositions.Clear();
        _isDraggingNodeGroup = false;
        if (!NodeGroupActive) return false;

        foreach (string id in _multiNodeIds)
        {
            if (IsLockedNode(id) || IsPlacedInBetween(id) || FindArenaForSecondaryNode(id) != null) continue;
            var node = _data.nodes.Find(n => n.id == id);
            if (node != null) _nodeGroupDragStartPositions[id] = node.worldPosition;
        }

        _nodeGroupDragStartWorld = CanvasToWorldPos(mouse);
        _nodeGroupDragRecorded   = false;
        _isDraggingNodeGroup     = true;
        return true;
    }

    private void DragNodeGroup(Vector2 mouse)
    {
        if (_nodeGroupDragStartPositions.Count == 0) return;
        if (!_nodeGroupDragRecorded)
        {
            Undo.RecordObject(_data, "Move Nodes");
            _nodeGroupDragRecorded = true;
        }

        Vector3 delta = CanvasToWorldPos(mouse) - _nodeGroupDragStartWorld;
        delta.y = 0f;
        foreach (var node in _data.nodes)
            if (_nodeGroupDragStartPositions.TryGetValue(node.id, out Vector3 start))
                node.worldPosition = start + delta;
        SyncInBetweenNodes();

        MarkDirty();
        Repaint();
    }

    private void EndNodeGroupDrag()
    {
        _isDraggingNodeGroup = false;
        _nodeGroupDragStartPositions.Clear();
    }
}

#endif
