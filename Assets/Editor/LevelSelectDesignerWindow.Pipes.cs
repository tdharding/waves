using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Pipes: hollow pipes standing over the map on support stems, with a ring round the pipe at
/// every support. Only for looks — nothing travels along them, but they carry capsule
/// colliders so the camera and the glows can tell when a pipe is in front of something.
///
/// Pipe mode draws them. Click empty space to start a pipe and keep clicking to lay its nodes;
/// Enter, Esc or a double-click finishes it. Shift-click on a pipe puts a support there. Drag a
/// node or a support to move it; right-click one, or select it and press Delete, to remove it.
/// Delete with only the pipe selected removes the whole pipe.
///
/// Height is measured from the water surface up to the pipe's centre line: one height for the
/// whole pipe, which any node can override. The stems go down to the one Drop, with everything
/// else that is generated.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string PipesParent = "PIPES";

    private string _selectedPipeId;
    private int    _selectedPipeNode    = -1;
    private int    _selectedPipeSupport = -1;
    private bool   _isDrawingPipe;
    private bool   _isDraggingPipe;
    private Vector2 _pipeDragOffset;

    private const float PipeNodeRadius  = 4.5f;
    private const float PipeHitReach    = 7f;

    private LevelSelectDesignerData.DesignerPipe SelectedPipe =>
        _selectedPipeId == null ? null : _data.pipes.Find(p => p.pipeId == _selectedPipeId);

    // ─────────────────────────────────────────────
    // HEIGHTS AND SHAPE
    // ─────────────────────────────────────────────

    // Where the rivers' rim top is, in the world. Pipes are built from it, the same as runs.
    private float PipeRimTopY => _data.canvasWorldY;

    /// <summary>A pipe's nodes in its mesh's frame: world x and z, y up from the rim top.</summary>
    private List<Vector3> PipeNodePoints(LevelSelectDesignerData.DesignerPipe pipe)
    {
        float water = -_data.WaterSurfaceDrop;
        var   pts   = new List<Vector3>(pipe.nodes.Count);
        for (int i = 0; i < pipe.nodes.Count; i++)
        {
            var n = pipe.nodes[i];
            pts.Add(new Vector3(n.positionXZ.x, water + pipe.HeightAt(i), n.positionXZ.y));
        }
        return pts;
    }

    private static Vector3 PipeNodeWorldFlat(LevelSelectDesignerData.PipeNode node)
        => new Vector3(node.positionXZ.x, 0f, node.positionXZ.y);

    // ─────────────────────────────────────────────
    // CANVAS — DRAWING
    // ─────────────────────────────────────────────

    private void DrawPipes()
    {
        if (Event.current.type != EventType.Repaint) return;

        foreach (var pipe in _data.pipes)
        {
            if (pipe == null || pipe.nodes.Count == 0) continue;
            bool selected = pipe.pipeId == _selectedPipeId;

            var body = selected ? new Color(0.92f, 0.92f, 0.92f, 0.95f) : new Color(0.55f, 0.72f, 0.78f, 0.9f);
            var bore = selected ? new Color(0.45f, 0.45f, 0.45f, 0.95f) : new Color(0.22f, 0.3f, 0.33f, 0.9f);

            // The pipe as a solid band its own thickness, rounded where it will be rounded, with
            // a darker line down the middle for the hollow.
            if (pipe.nodes.Count >= 2)
            {
                var line   = PipeMesh.CentreLine(PipeNodePoints(pipe), pipe.pipeThickness);
                var canvas = line.Select(p => (Vector3)WorldToCanvas(p)).ToArray();
                float width = Mathf.Max(3f, pipe.pipeThickness * _zoom);

                Handles.color = body;
                Handles.DrawAAPolyLine(width, canvas);
                float hollow = Mathf.Max(0f, pipe.pipeThickness - pipe.wallThickness * 2f) * _zoom;
                if (hollow > 1f)
                {
                    Handles.color = bore;
                    Handles.DrawAAPolyLine(Mathf.Max(1f, hollow), canvas);
                }

                DrawPipeSupports(pipe, line, selected);
            }

            // Nodes: solid dots, orange where the node has its own height.
            for (int i = 0; i < pipe.nodes.Count; i++)
            {
                Vector2 at = WorldToCanvas(PipeNodeWorldFlat(pipe.nodes[i]));
                Handles.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
                Handles.DrawSolidDisc(at, Vector3.forward, PipeNodeRadius + 1.5f);
                Handles.color = selected && i == _selectedPipeNode ? Color.yellow
                              : pipe.nodes[i].overrideHeight       ? new Color(1f, 0.6f, 0.15f)
                              : body;
                Handles.DrawSolidDisc(at, Vector3.forward, PipeNodeRadius);
            }
        }

        // The ghost leg out to the cursor while a pipe is being laid.
        var drawing = _isDrawingPipe ? SelectedPipe : null;
        if (drawing != null && drawing.nodes.Count > 0 && _canvasRect.Contains(Event.current.mousePosition))
        {
            Handles.color = new Color(1f, 1f, 1f, 0.3f);
            Handles.DrawDottedLine(WorldToCanvas(PipeNodeWorldFlat(drawing.nodes[drawing.nodes.Count - 1])),
                                   Event.current.mousePosition, 5f);
        }
    }

    // Each support as a short bar laid across the pipe, as wide as the ring and as thick as the stem.
    private void DrawPipeSupports(LevelSelectDesignerData.DesignerPipe pipe, List<Vector3> line, bool selected)
    {
        var nodes = PipeNodePoints(pipe);
        for (int i = 0; i < pipe.supports.Count; i++)
        {
            if (!TryPipeSupportCanvas(pipe, nodes, line, i, out Vector2 at, out Vector2 across)) continue;

            float half  = Mathf.Max(8f, (pipe.pipeThickness * 0.5f + pipe.ringOverhang) * _zoom + 3f);
            float thick = Mathf.Max(2.5f, pipe.supportThickness * _zoom);

            Handles.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            Handles.DrawAAPolyLine(thick + 2f, at - across * (half + 1f), at + across * (half + 1f));
            Handles.color = selected && i == _selectedPipeSupport ? Color.yellow : new Color(1f, 0.85f, 0.4f);
            Handles.DrawAAPolyLine(thick, at - across * half, at + across * half);
        }
    }

    /// <summary>Canvas position of a support, and the canvas direction straight across the pipe there.</summary>
    private bool TryPipeSupportCanvas(LevelSelectDesignerData.DesignerPipe pipe, List<Vector3> nodes,
                                      List<Vector3> line, int index, out Vector2 at, out Vector2 across)
    {
        at = across = default;
        if (pipe.nodes.Count < 2 || index < 0 || index >= pipe.supports.Count) return false;

        PipeMesh.SupportOnLine(nodes, line, ToMeshSupport(pipe.supports[index]), out Vector3 p, out Vector3 t);
        at = WorldToCanvas(p);
        Vector2 dir = WorldToCanvas(p + t) - at;
        if (dir.sqrMagnitude < 1e-8f) dir = Vector2.right;
        dir.Normalize();
        across = new Vector2(-dir.y, dir.x);
        return true;
    }

    private static PipeMesh.Support ToMeshSupport(LevelSelectDesignerData.PipeSupport s)
        => new PipeMesh.Support { leg = s.leg, along = s.along };

    // ─────────────────────────────────────────────
    // CANVAS — HIT TESTING
    // ─────────────────────────────────────────────

    private bool FindPipeNodeAtCanvas(Vector2 canvas, out string pipeId, out int node)
    {
        pipeId = null; node = -1;
        float reach = PipeHitReach * PipeHitReach;
        for (int p = _data.pipes.Count - 1; p >= 0; p--)
        {
            var pipe = _data.pipes[p];
            for (int i = pipe.nodes.Count - 1; i >= 0; i--)
            {
                if ((canvas - WorldToCanvas(PipeNodeWorldFlat(pipe.nodes[i]))).sqrMagnitude > reach) continue;
                pipeId = pipe.pipeId; node = i;
                return true;
            }
        }
        return false;
    }

    private bool FindPipeSupportAtCanvas(Vector2 canvas, out string pipeId, out int support)
    {
        pipeId = null; support = -1;
        for (int p = _data.pipes.Count - 1; p >= 0; p--)
        {
            var pipe = _data.pipes[p];
            if (pipe.nodes.Count < 2 || pipe.supports.Count == 0) continue;

            var nodes = PipeNodePoints(pipe);
            var line  = PipeMesh.CentreLine(nodes, pipe.pipeThickness);
            float half = Mathf.Max(8f, (pipe.pipeThickness * 0.5f + pipe.ringOverhang) * _zoom + 3f);

            for (int i = pipe.supports.Count - 1; i >= 0; i--)
            {
                if (!TryPipeSupportCanvas(pipe, nodes, line, i, out Vector2 at, out Vector2 across)) continue;
                if (DistanceToSegment(canvas, at - across * half, at + across * half) > PipeHitReach * 0.6f) continue;
                pipeId = pipe.pipeId; support = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>The pipe under the cursor, and the leg and fraction along it nearest the cursor.</summary>
    private bool FindPipeAtCanvas(Vector2 canvas, out string pipeId, out int leg, out float along)
    {
        pipeId = null; leg = -1; along = 0f;
        for (int p = _data.pipes.Count - 1; p >= 0; p--)
        {
            var pipe = _data.pipes[p];
            if (pipe.nodes.Count < 2) continue;

            NearestPipeLeg(pipe, canvas, out int l, out float t, out float dist);
            if (dist > Mathf.Max(PipeHitReach, pipe.pipeThickness * _zoom * 0.5f + 3f)) continue;
            pipeId = pipe.pipeId; leg = l; along = t;
            return true;
        }
        return false;
    }

    // Nearest point on the pipe's straight legs, in canvas space.
    private void NearestPipeLeg(LevelSelectDesignerData.DesignerPipe pipe, Vector2 canvas,
                                out int leg, out float along, out float dist)
    {
        leg = 0; along = 0f; dist = float.MaxValue;
        for (int i = 0; i < pipe.nodes.Count - 1; i++)
        {
            Vector2 a = WorldToCanvas(PipeNodeWorldFlat(pipe.nodes[i]));
            Vector2 b = WorldToCanvas(PipeNodeWorldFlat(pipe.nodes[i + 1]));
            Vector2 ab = b - a;
            float   t  = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(canvas - a, ab) / ab.sqrMagnitude) : 0f;
            float   d  = (canvas - (a + ab * t)).magnitude;
            if (d >= dist) continue;
            dist = d; leg = i; along = t;
        }
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float   t  = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return (p - (a + ab * t)).magnitude;
    }

    // ─────────────────────────────────────────────
    // CANVAS — EDITING
    // ─────────────────────────────────────────────

    private void HandlePipeMode(Event e)
    {
        if (e.type == EventType.KeyDown)
        {
            if (_isDrawingPipe && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter ||
                                   e.keyCode == KeyCode.Escape))
            {
                FinishDrawingPipe();
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                DeleteSelectedPipePart();
                e.Use();
                return;
            }
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (_isDrawingPipe)
            {
                if (e.clickCount >= 2) FinishDrawingPipe();
                else                   AddPipeNode(SelectedPipe, e.mousePosition);
                e.Use();
                return;
            }

            if (FindPipeNodeAtCanvas(e.mousePosition, out string nodePipe, out int node))
            {
                SelectPipe(nodePipe, node, -1);
                var pipe = SelectedPipe;
                _pipeDragOffset = e.mousePosition - WorldToCanvas(PipeNodeWorldFlat(pipe.nodes[node]));
                _isDraggingPipe = true;
                e.Use();
                return;
            }

            if (FindPipeSupportAtCanvas(e.mousePosition, out string supportPipe, out int support))
            {
                SelectPipe(supportPipe, -1, support);
                _isDraggingPipe = true;
                e.Use();
                return;
            }

            if (FindPipeAtCanvas(e.mousePosition, out string pipeId, out int leg, out float along))
            {
                if (e.shift)
                {
                    var pipe = _data.pipes.Find(p => p.pipeId == pipeId);
                    Undo.RecordObject(_data, "Add Pipe Support");
                    pipe.supports.Add(new LevelSelectDesignerData.PipeSupport { leg = leg, along = along });
                    SelectPipe(pipeId, -1, pipe.supports.Count - 1);
                    _isDraggingPipe = true;
                    MarkDirty();
                    RebuildPipe(pipe);
                }
                else
                {
                    SelectPipe(pipeId, -1, -1);
                }
                e.Use();
                return;
            }

            // Empty space: a new pipe starts here.
            Undo.RecordObject(_data, "Add Pipe");
            var fresh = new LevelSelectDesignerData.DesignerPipe { pipeId = Guid.NewGuid().ToString() };
            _data.pipes.Add(fresh);
            SelectPipe(fresh.pipeId, -1, -1);
            _isDrawingPipe = true;
            AddPipeNode(fresh, e.mousePosition);
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 1)
        {
            if (FindPipeNodeAtCanvas(e.mousePosition, out string nodePipe, out int node))
            {
                SelectPipe(nodePipe, node, -1);
                DeleteSelectedPipePart();
                e.Use();
            }
            else if (FindPipeSupportAtCanvas(e.mousePosition, out string supportPipe, out int support))
            {
                SelectPipe(supportPipe, -1, support);
                DeleteSelectedPipePart();
                e.Use();
            }
            return;
        }

        if (e.type == EventType.MouseDrag && _isDraggingPipe)
        {
            var pipe = SelectedPipe;
            if (pipe != null && _selectedPipeNode >= 0 && _selectedPipeNode < pipe.nodes.Count)
            {
                Undo.RecordObject(_data, "Move Pipe Node");
                Vector2 xz = CanvasToWorld2D(e.mousePosition - _pipeDragOffset);
                pipe.nodes[_selectedPipeNode].positionXZ = xz;
                MarkDirty();
            }
            else if (pipe != null && _selectedPipeSupport >= 0 && _selectedPipeSupport < pipe.supports.Count)
            {
                Undo.RecordObject(_data, "Move Pipe Support");
                NearestPipeLeg(pipe, e.mousePosition, out int leg, out float along, out _);
                pipe.supports[_selectedPipeSupport].leg   = leg;
                pipe.supports[_selectedPipeSupport].along = along;
                MarkDirty();
            }
            Repaint();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && _isDraggingPipe)
        {
            _isDraggingPipe = false;
            RebuildPipe(SelectedPipe);
            e.Use();
        }
    }

    private void SelectPipe(string pipeId, int node, int support)
    {
        _selectedPipeId      = pipeId;
        _selectedPipeNode    = node;
        _selectedPipeSupport = support;
        GUI.FocusControl(null);
        Repaint();
    }

    private void AddPipeNode(LevelSelectDesignerData.DesignerPipe pipe, Vector2 canvas)
    {
        if (pipe == null) return;
        Undo.RecordObject(_data, "Add Pipe Node");
        pipe.nodes.Add(new LevelSelectDesignerData.PipeNode { positionXZ = CanvasToWorld2D(canvas) });
        _selectedPipeNode = pipe.nodes.Count - 1;
        MarkDirty();
        Repaint();
    }

    // A pipe needs two nodes to be a pipe; one finished with fewer is taken away again.
    private void FinishDrawingPipe()
    {
        _isDrawingPipe = false;
        var pipe = SelectedPipe;
        if (pipe != null && pipe.nodes.Count < 2)
        {
            Undo.RecordObject(_data, "Remove Pipe");
            _data.pipes.Remove(pipe);
            _selectedPipeId = null;
            MarkDirty();
        }
        else
        {
            RebuildPipe(pipe);
        }
        _selectedPipeNode = -1;
        Repaint();
    }

    /// <summary>Removes the selected support, else the selected node, else the whole selected pipe.</summary>
    private void DeleteSelectedPipePart()
    {
        var pipe = SelectedPipe;
        if (pipe == null) return;

        if (_selectedPipeSupport >= 0 && _selectedPipeSupport < pipe.supports.Count)
        {
            Undo.RecordObject(_data, "Delete Pipe Support");
            pipe.supports.RemoveAt(_selectedPipeSupport);
            _selectedPipeSupport = -1;
            MarkDirty();
            RebuildPipe(pipe);
        }
        else if (_selectedPipeNode >= 0 && _selectedPipeNode < pipe.nodes.Count)
        {
            Undo.RecordObject(_data, "Delete Pipe Node");
            RemovePipeNode(pipe, _selectedPipeNode);
            _selectedPipeNode = -1;
            if (pipe.nodes.Count < 2 && !_isDrawingPipe) RemovePipe(pipe);
            else                                         RebuildPipe(pipe);
            MarkDirty();
        }
        else
        {
            Undo.RecordObject(_data, "Delete Pipe");
            RemovePipe(pipe);
            MarkDirty();
        }
        Repaint();
    }

    // Takes a node out and puts every support back on the nearest point of the legs left, so a
    // support stays where it stood on the map rather than jumping with the leg numbering.
    private static void RemovePipeNode(LevelSelectDesignerData.DesignerPipe pipe, int index)
    {
        var standing = pipe.supports.Select(s => SupportFlat(pipe, s)).ToList();
        pipe.nodes.RemoveAt(index);
        if (pipe.nodes.Count < 2) { pipe.supports.Clear(); return; }

        for (int i = 0; i < pipe.supports.Count; i++)
        {
            Vector2 p = standing[i];
            float best = float.MaxValue;
            for (int leg = 0; leg < pipe.nodes.Count - 1; leg++)
            {
                Vector2 a = pipe.nodes[leg].positionXZ, ab = pipe.nodes[leg + 1].positionXZ - a;
                float   t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                float   d = (p - (a + ab * t)).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                pipe.supports[i].leg   = leg;
                pipe.supports[i].along = t;
            }
        }
    }

    private static Vector2 SupportFlat(LevelSelectDesignerData.DesignerPipe pipe, LevelSelectDesignerData.PipeSupport s)
    {
        int leg = Mathf.Clamp(s.leg, 0, Mathf.Max(0, pipe.nodes.Count - 2));
        if (pipe.nodes.Count < 2) return pipe.nodes.Count == 1 ? pipe.nodes[0].positionXZ : Vector2.zero;
        return Vector2.Lerp(pipe.nodes[leg].positionXZ, pipe.nodes[leg + 1].positionXZ, s.along);
    }

    private void RemovePipe(LevelSelectDesignerData.DesignerPipe pipe)
    {
        _data.pipes.Remove(pipe);
        var record = FindObjectsOfType<PipeMesh>().FirstOrDefault(r => r.pipeId == pipe.pipeId);
        if (record != null) Undo.DestroyObjectImmediate(record.gameObject);
        _selectedPipeId      = null;
        _selectedPipeNode    = -1;
        _selectedPipeSupport = -1;
        _isDrawingPipe       = false;
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private void DrawSelectedPipeProps()
    {
        if (_mode != DesignerMode.Pipe) return;
        var pipe = SelectedPipe;
        if (pipe == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Pipe", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"{pipe.nodes.Count} nodes, {pipe.supports.Count} supports",
                                   EditorStyles.miniLabel);

        EditorGUI.BeginChangeCheck();
        float height = EditorGUILayout.FloatField(
            new GUIContent("Height", "How far the pipe's centre line stands above the water, at every " +
                                     "node that doesn't override it."), pipe.height);
        float pipeThickness = EditorGUILayout.FloatField(
            new GUIContent("Pipe Thickness", "The pipe's thickness, across the outside. The bends are " +
                                             "rounded to match it."), pipe.pipeThickness);
        float wallThickness = EditorGUILayout.FloatField(
            new GUIContent("Wall Thickness", "Thickness of the pipe's wall — the hollow is what is left " +
                                             "inside it."), pipe.wallThickness);
        float supportThickness = EditorGUILayout.FloatField(
            new GUIContent("Support Thickness", "Thickness of each support stem. Each ring is as long as this."),
            pipe.supportThickness);
        float ringOverhang = EditorGUILayout.FloatField(
            new GUIContent("Ring Overhang", "How far each ring stands out beyond the pipe."), pipe.ringOverhang);

        // The selected node's own height.
        bool  nodeOverride = false;
        float nodeHeight   = 0f;
        bool  hasNode      = _selectedPipeNode >= 0 && _selectedPipeNode < pipe.nodes.Count;
        if (hasNode)
        {
            var node = pipe.nodes[_selectedPipeNode];
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"Node {_selectedPipeNode + 1}", EditorStyles.miniBoldLabel);
            nodeOverride = EditorGUILayout.Toggle(
                new GUIContent("Override Height", "Give this node its own height. Left off, it takes the pipe's."),
                node.overrideHeight);
            using (new EditorGUI.DisabledScope(!nodeOverride))
                nodeHeight = EditorGUILayout.FloatField(
                    new GUIContent("Height", "How far the pipe's centre line stands above the water at this node."),
                    nodeOverride ? node.height : pipe.height);
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Pipe");
            pipe.height           = Mathf.Max(0f, height);
            pipe.pipeThickness    = Mathf.Max(0.01f, pipeThickness);
            pipe.wallThickness    = Mathf.Clamp(wallThickness, 0f, pipe.pipeThickness * 0.5f);
            pipe.supportThickness = Mathf.Max(0.005f, supportThickness);
            pipe.ringOverhang     = Mathf.Max(0f, ringOverhang);
            if (hasNode)
            {
                var node = pipe.nodes[_selectedPipeNode];
                // Switching the override on starts it from the height the node already had.
                if (nodeOverride && !node.overrideHeight) nodeHeight = pipe.height;
                node.overrideHeight = nodeOverride;
                if (nodeOverride) node.height = Mathf.Max(0f, nodeHeight);
            }
            MarkDirty();
            RebuildPipe(pipe);
        }

        EditorGUILayout.Space(2);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("Delete Pipe"))
        {
            Undo.RecordObject(_data, "Delete Pipe");
            RemovePipe(pipe);
            MarkDirty();
        }
        GUI.backgroundColor = Color.white;
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    private void GeneratePipes(GameObject pipesParent)
    {
        foreach (var pipe in _data.pipes)
        {
            if (pipe == null || string.IsNullOrEmpty(pipe.pipeId) || pipe.nodes.Count < 2) continue;

            var existing = FindObjectsOfType<PipeMesh>().FirstOrDefault(r => r.pipeId == pipe.pipeId);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject(PipeMeshName(pipe));
                Undo.RegisterCreatedObjectUndo(go, "Generate Pipe");
                go.transform.SetParent(pipesParent.transform, false);
                go.AddComponent<PipeMesh>();
            }

            BuildPipe(pipe, go);
        }
    }

    /// <summary>
    /// Rebuilds one pipe in the scene. A pipe not generated yet is built now, once the PIPES
    /// parent is there — so a pipe drawn after the last Generate still shows up as it is drawn.
    /// </summary>
    private void RebuildPipe(LevelSelectDesignerData.DesignerPipe pipe)
    {
        if (pipe == null || pipe.nodes.Count < 2) return;

        var record = FindObjectsOfType<PipeMesh>().FirstOrDefault(r => r.pipeId == pipe.pipeId);
        if (record != null)
        {
            BuildPipe(pipe, record.gameObject);
        }
        else
        {
            var parent = GameObject.Find(PipesParent);
            if (parent == null) return;
            var go = new GameObject(PipeMeshName(pipe));
            Undo.RegisterCreatedObjectUndo(go, "Generate Pipe");
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<PipeMesh>();
            BuildPipe(pipe, go);
        }
        AssetDatabase.SaveAssets();
    }

    private void BuildPipe(LevelSelectDesignerData.DesignerPipe pipe, GameObject go)
    {
        string name = PipeMeshName(pipe);

        // Built in world x and z, with y measured from the rim top, so the stone shading places
        // its waterline off the same rim the rivers use.
        Undo.RecordObject(go.transform, "Place Pipe");
        go.name                 = name;
        go.transform.position   = new Vector3(0f, PipeRimTopY, 0f);
        go.transform.rotation   = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var mesh = SaveGeneratedMesh(name, PipeMesh.Build(
            PipeNodePoints(pipe), pipe.supports.Select(ToMeshSupport).ToList(),
            pipe.pipeThickness, pipe.wallThickness, pipe.supportThickness, pipe.ringOverhang,
            -_data.RunDepth));
        if (mesh == null) return;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _data.riverMaterial;
        EditorUtility.SetDirty(renderer);

        var record = go.GetComponent<PipeMesh>();
        record.pipeId        = pipe.pipeId;
        record.meshAssetName = name;
        EditorUtility.SetDirty(record);

        EnsurePipeColliders(pipe, go);
    }

    private const string PipeColliderName = "PipeCollider";

    /// <summary>
    /// Gives a pipe something to be hit by: a capsule down each leg and each support stem,
    /// rather than a collider shaped like the mesh. What wants them is the camera and the
    /// glows, which cast rays to find out what is standing in front of what, so a shape
    /// roughly the size of the pipe is all they ask for and a cheap one is worth having.
    ///
    /// Each capsule is a child of its own, because a capsule can only run along one of its
    /// object's axes and a pipe's legs run every which way. They are laid out again from
    /// scratch on every rebuild, since a pipe redrawn has a different number of them.
    /// </summary>
    private void EnsurePipeColliders(LevelSelectDesignerData.DesignerPipe pipe, GameObject go)
    {
        for (int i = go.transform.childCount - 1; i >= 0; i--)
        {
            var child = go.transform.GetChild(i);
            if (child.name.StartsWith(PipeColliderName))
                Undo.DestroyObjectImmediate(child.gameObject);
        }

        var rods = PipeMesh.CollisionRods(
            PipeNodePoints(pipe), pipe.supports.Select(ToMeshSupport).ToList(),
            pipe.pipeThickness, pipe.supportThickness, -_data.RunDepth);

        for (int i = 0; i < rods.Count; i++)
        {
            var     rod    = rods[i];
            Vector3 along  = rod.to - rod.from;
            float   length = along.magnitude;
            if (length < 1e-4f) continue;

            var child = new GameObject($"{PipeColliderName}_{i}");
            Undo.RegisterCreatedObjectUndo(child, "Generate Pipe");
            child.transform.SetParent(go.transform, false);
            child.layer = go.layer;

            // The pipe sits unrotated at the rim top, so a rod's points are already the
            // child's local ones. The capsule runs up its own y, turned onto the rod.
            child.transform.localPosition = (rod.from + rod.to) * 0.5f;
            child.transform.localRotation = Quaternion.FromToRotation(Vector3.up, along / length);
            child.transform.localScale    = Vector3.one;

            var col       = child.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.radius    = rod.radius;

            // Height counts the two rounded ends in, so the straight part is the rod itself
            // and the ends round off past it the way the pipe does at a bend.
            col.height    = length + rod.radius * 2f;
            EditorUtility.SetDirty(col);
        }
    }

    // Named off the pipe's id, which survives every regenerate — so the mesh asset is reused.
    private static string PipeMeshName(LevelSelectDesignerData.DesignerPipe pipe)
        => $"Pipe_{SanitiseAssetName(pipe.pipeId)}";
}

#endif
