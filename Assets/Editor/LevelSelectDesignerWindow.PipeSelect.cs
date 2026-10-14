using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Pipe Select — the third tool under the Select button's arrow.
///
/// Click a pipe to pick it whole, Shift+click to add or take away more, and drag any picked pipe
/// to move every picked pipe together. A pipe moves by all of its nodes; its supports sit on its
/// legs, so they go with it.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private readonly List<string> _pipeSelectIds = new();

    private bool    _isDraggingPipes;
    private bool    _pipeDragRecorded;
    private Vector2 _pipeDragStartXZ;
    private readonly Dictionary<string, Vector2[]> _pipeDragStartPositions = new();

    private bool IsPipePicked(string pipeId)
        => _mode == DesignerMode.PipeSelect && _pipeSelectIds.Contains(pipeId);

    private void ClearPipeSelect()
    {
        _pipeSelectIds.Clear();
        _isDraggingPipes = false;
        _pipeDragStartPositions.Clear();
    }

    private void HandlePipeSelectMode(Event e)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            string pipeId = FindPipeAtCanvas(e.mousePosition, out string hit, out _, out _) ? hit : null;
            if (pipeId == null && FindPipeNodeAtCanvas(e.mousePosition, out string nodePipe, out _))
                pipeId = nodePipe;

            if (e.shift)
            {
                if (pipeId == null) { e.Use(); return; }
                if (_pipeSelectIds.Remove(pipeId))
                {
                    Repaint();
                    e.Use();
                    return;
                }
                _pipeSelectIds.Add(pipeId);
            }
            else if (pipeId == null)
            {
                _pipeSelectIds.Clear();
                Repaint();
                e.Use();
                return;
            }
            else if (!_pipeSelectIds.Contains(pipeId))
            {
                _pipeSelectIds.Clear();
                _pipeSelectIds.Add(pipeId);
            }

            ClearRiverSelectionForWallOrPipe();
            ClearWallAndPipeSelection();

            BeginPipeDrag(e.mousePosition);
            Repaint();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && _isDraggingPipes)
        {
            if (!_pipeDragRecorded)
            {
                Undo.RecordObject(_data, _pipeSelectIds.Count > 1 ? "Move Pipes" : "Move Pipe");
                _pipeDragRecorded = true;
            }

            Vector2 delta = CanvasToWorld2D(e.mousePosition) - _pipeDragStartXZ;
            foreach (var pipe in _data.pipes)
            {
                if (!_pipeDragStartPositions.TryGetValue(pipe.pipeId, out Vector2[] start)) continue;
                for (int i = 0; i < pipe.nodes.Count && i < start.Length; i++)
                    pipe.nodes[i].positionXZ = start[i] + delta;
            }

            MarkDirty();
            Repaint();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && _isDraggingPipes)
        {
            _isDraggingPipes = false;
            if (_pipeDragRecorded)
                foreach (var pipe in _data.pipes)
                    if (_pipeDragStartPositions.ContainsKey(pipe.pipeId)) RebuildPipe(pipe);
            _pipeDragStartPositions.Clear();
            e.Use();
        }
    }

    /// Notes where every node of the picked pipes starts, so the drag moves them by one offset.
    private void BeginPipeDrag(Vector2 mouse)
    {
        _pipeDragStartPositions.Clear();
        foreach (string pipeId in _pipeSelectIds)
        {
            var pipe = _data.pipes.Find(p => p.pipeId == pipeId);
            if (pipe == null) continue;
            var start = new Vector2[pipe.nodes.Count];
            for (int i = 0; i < start.Length; i++) start[i] = pipe.nodes[i].positionXZ;
            _pipeDragStartPositions[pipeId] = start;
        }

        _pipeDragStartXZ  = CanvasToWorld2D(mouse);
        _pipeDragRecorded = false;
        _isDraggingPipes  = _pipeDragStartPositions.Count > 0;
    }

    /// The left panel while Pipe Select is on — just the picked pipes, nothing else.
    private void DrawPipeSelectList()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"Selected Pipes ({_pipeSelectIds.Count})", EditorStyles.boldLabel);

        if (_pipeSelectIds.Count == 0)
        {
            EditorGUILayout.LabelField("None — click a pipe on the canvas.", EditorStyles.miniLabel);
            return;
        }

        foreach (string pipeId in _pipeSelectIds)
        {
            int index = _data.pipes.FindIndex(p => p.pipeId == pipeId);
            if (index < 0) continue;
            var pipe = _data.pipes[index];
            EditorGUILayout.LabelField($"Pipe {index + 1}  —  {pipe.nodes.Count} nodes, {pipe.supports.Count} supports");
        }
    }
}

#endif
