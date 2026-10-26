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

    private static float PipeNodeRadius => LevelSelectDesignerSettings.PipeNodeSize;
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

    /// <summary>
    /// What the pipe's centre line runs through: its nodes, with a lead-in either side of every
    /// hub that has Lead-ins ticked, so the pipe runs straight through the hub.
    /// </summary>
    private List<Vector3> PipeLinePoints(LevelSelectDesignerData.DesignerPipe pipe)
    {
        var nodes     = PipeNodePoints(pipe);
        var leadIns   = new List<int>();
        var distances = new List<float>();
        for (int i = 0; i < pipe.nodes.Count; i++)
        {
            var n = pipe.nodes[i];
            if (!n.hub || !n.hubLeadIns) continue;
            leadIns.Add(i);
            distances.Add(n.hubLeadInDistance);
        }
        return leadIns.Count == 0 ? nodes : PipeMesh.WithLeadIns(nodes, leadIns, distances);
    }

    // ─────────────────────────────────────────────
    // HUBS
    // ─────────────────────────────────────────────

    /// <summary>The pipe and node index of the hub <paramref name="hubId"/>, if it is still a hub.</summary>
    private bool FindPipeHub(string hubId, out LevelSelectDesignerData.DesignerPipe pipe, out int node)
    {
        pipe = null; node = -1;
        if (string.IsNullOrEmpty(hubId) || _data == null) return false;
        foreach (var p in _data.pipes)
        {
            if (p?.nodes == null) continue;
            int i = p.nodes.FindIndex(n => n != null && n.hub && n.hubId == hubId);
            if (i < 0) continue;
            pipe = p; node = i;
            return true;
        }
        return false;
    }

    /// <summary>The pool whose soul juice tank takes its intake from <paramref name="hubId"/>, or null.</summary>
    private LevelSelectDesignerData.DesignerPool TankLinkedTo(string hubId)
        => string.IsNullOrEmpty(hubId) ? null
         : _data.pools.Find(p => p != null && p.tankHubId == hubId && p.hasTower &&
                                 p.towerKind == LevelSelectDesignerData.TowerKind.SoulJuiceTank);

    /// <summary>
    /// A hub in the pipe's own frame (world x and z, y up from the rim top). Its ring stands out
    /// level and square to the pipe, on the side of the tank linked to it — or on the right-hand
    /// side looking along the pipe when no tank is linked.
    /// </summary>
    private PipeMesh.Hub PipeHubLocal(LevelSelectDesignerData.DesignerPipe pipe, int node)
    {
        var nodes = PipeNodePoints(pipe);
        var n     = pipe.nodes[node];
        var hub   = new PipeMesh.Hub
        {
            centre          = nodes[node],
            axis            = PipeMesh.AxisAt(nodes, node),
            sleeveRadius    = n.hubSleeveRadius,
            sleeveThickness = n.hubSleeveThickness,
            ringRadius      = n.hubRingRadius,
            ringThickness   = n.hubRingThickness,
        };

        // Always level, square to the pipe — so the intake comes up and turns one right angle into
        // it, the way the tap turns down. It stands out on the linked tank's side; with none, on
        // the right-hand side looking along the pipe.
        Vector3 dir = Vector3.Cross(hub.axis, Vector3.up);
        var pool = TankLinkedTo(n.hubId);
        if (pool != null && TankTopHubWorld(pool, out Vector3 top))
        {
            Vector3 toward = top - (hub.centre + Vector3.up * PipeRimTopY);
            toward.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) dir = toward;           // a pipe running straight up
            else if (Vector3.Dot(dir, toward) < 0f) dir = -dir;
        }
        if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;
        if (n.hubFlip) dir = -dir;
        dir.y = 0f;
        hub.ringDir = dir.normalized;
        return hub;
    }

    /// <summary>A hub as <see cref="PipeHubLocal"/> gives it, moved into the world.</summary>
    private bool TryPipeHubWorld(LevelSelectDesignerData.DesignerPipe pipe, int node, out PipeMesh.Hub hub)
    {
        hub = default;
        if (pipe == null || node < 0 || node >= pipe.nodes.Count) return false;
        hub = PipeHubLocal(pipe, node);
        hub.centre += Vector3.up * PipeRimTopY;
        return true;
    }

    /// <summary>
    /// Where the middle of a soul juice tank's top hub face is in the world: off the tank as
    /// generated, or off its pool's node when it isn't built yet.
    /// </summary>
    private bool TankTopHubWorld(LevelSelectDesignerData.DesignerPool pool, out Vector3 top)
    {
        top = default;
        if (pool?.soulJuiceTank == null) return false;
        var placed = pool.soulJuiceTank.Scaled(pool.towerScale);

        var tank = FindGeneratedPoolTower(pool);
        if (tank != null) { top = tank.TransformPoint(placed.TopHubFace); return true; }

        var node = _data.nodes.Find(n => n.id == pool.nodeId);
        if (node == null) return false;
        top = node.worldPosition + placed.TopHubFace;
        return true;
    }

    /// <summary>
    /// The Intake Hub dropdown for a soul juice tank: None, then every pipe hub within the Hub
    /// Reach set under Pipes in Level Select Settings, nearest first. A hub already feeding
    /// another tank isn't offered. Hands back the hub id picked.
    /// </summary>
    private string DrawTankIntakeHubField(LevelSelectDesignerData.DesignerPool pool)
    {
        var node = _data.nodes.Find(n => n.id == pool.nodeId);
        Vector2 at = node != null ? new Vector2(node.worldPosition.x, node.worldPosition.z) : Vector2.zero;
        float reach = LevelSelectDesignerSettings.PipeHubReach;

        var near = new List<(string id, string label, float dist)>();
        for (int p = 0; p < _data.pipes.Count; p++)
        {
            var pipe = _data.pipes[p];
            if (pipe?.nodes == null) continue;
            for (int i = 0; i < pipe.nodes.Count; i++)
            {
                var n = pipe.nodes[i];
                if (n == null || !n.hub || string.IsNullOrEmpty(n.hubId)) continue;

                var other = TankLinkedTo(n.hubId);
                if (other != null && other != pool) continue;

                float dist = Vector2.Distance(at, n.positionXZ);
                if (dist > reach && n.hubId != pool.tankHubId) continue;
                near.Add((n.hubId, $"Pipe {p + 1} · Node {i + 1}  ({dist:F2} m)", dist));
            }
        }
        near.Sort((a, b) => a.dist.CompareTo(b.dist));

        var labels = new List<GUIContent> { new GUIContent("None") };
        labels.AddRange(near.Select(h => new GUIContent(h.label)));
        int index = Mathf.Max(0, near.FindIndex(h => h.id == pool.tankHubId) + 1);


        index = EditorGUILayout.Popup(
            new GUIContent("Intake Hub", "The pipe hub the intake runs up to — hubs within Hub Reach (Level " +
                                         "Select Settings > Pipes), nearest first. A hub feeds one tank only."),
            index, labels.ToArray());
        if (near.Count == 0)
            EditorGUILayout.LabelField($"No free pipe hub within {reach:F1} m.", EditorStyles.miniLabel);

        return index <= 0 ? null : near[index - 1].id;
    }

    // ─────────────────────────────────────────────
    // CANVAS — DRAWING
    // ─────────────────────────────────────────────

    // The pipe itself (band, bore, supports, nodes) at the Pipes settings' Opacity, on top of any mode fade.
    private static Color PipeFaded(Color c) { c = Faded(c); c.a *= LevelSelectDesignerSettings.PipeOpacity; return c; }

    private void DrawPipes()
    {
        if (Event.current.type != EventType.Repaint) return;

        foreach (var pipe in _data.pipes)
        {
            if (pipe == null || pipe.nodes.Count == 0) continue;
            bool selected = pipe.pipeId == _selectedPipeId || IsPipePicked(pipe.pipeId);

            var body = selected ? LevelSelectDesignerSettings.PipeSelBodyColour : LevelSelectDesignerSettings.PipeBodyColour;
            var bore = selected ? LevelSelectDesignerSettings.PipeSelBoreColour : LevelSelectDesignerSettings.PipeBoreColour;

            // The pipe as a solid band its own thickness, rounded where it will be rounded, with
            // a darker line down the middle for the hollow.
            if (pipe.nodes.Count >= 2)
            {
                var line   = PipeMesh.CentreLine(PipeLinePoints(pipe), pipe.pipeThickness);
                var canvas = line.Select(p => (Vector3)WorldToCanvas(p)).ToArray();
                float width = Mathf.Max(3f, pipe.pipeThickness * _zoom);

                Handles.color = PipeFaded(body);
                DrawSolidLine(width, canvas);
                float hollow = Mathf.Max(0f, pipe.pipeThickness - pipe.wallThickness * 2f) * _zoom;
                if (hollow > 1f)
                {
                    Handles.color = PipeFaded(bore);
                    DrawSolidLine(Mathf.Max(1f, hollow), canvas);
                }

                DrawPipeSupports(pipe, line, selected);
                DrawPipeHubs(pipe);
            }

            // Nodes: solid dots, orange where the node has its own height.
            for (int i = 0; i < pipe.nodes.Count; i++)
            {
                Vector2 at = WorldToCanvas(PipeNodeWorldFlat(pipe.nodes[i]));
                Handles.color = PipeFaded(LevelSelectDesignerSettings.PipeOutlineColour);
                Handles.DrawSolidDisc(at, Vector3.forward, PipeNodeRadius + 1.5f);
                Handles.color = PipeFaded(selected && i == _selectedPipeNode ? LevelSelectDesignerSettings.PipeSelColour
                              : pipe.nodes[i].overrideHeight       ? LevelSelectDesignerSettings.PipeOwnHeightColour
                              : body);
                Handles.DrawSolidDisc(at, Vector3.forward, PipeNodeRadius);
            }
        }

        // The ghost leg out to the cursor while a pipe is being laid.
        var drawing = _isDrawingPipe ? SelectedPipe : null;
        if (drawing != null && drawing.nodes.Count > 0 && _canvasRect.Contains(Event.current.mousePosition))
        {
            Handles.color = Faded(LevelSelectDesignerSettings.PipeGhostColour);
            Handles.DrawDottedLine(WorldToCanvas(PipeNodeWorldFlat(drawing.nodes[drawing.nodes.Count - 1])),
                                   Event.current.mousePosition, 5f);
        }
    }

    // Each hub as its sleeve seen from above — a bar across the pipe as wide as the sleeve and as
    // long as it is thick — and each lead-in as a small dot on the pipe.
    private void DrawPipeHubs(LevelSelectDesignerData.DesignerPipe pipe)
    {
        var nodes = PipeNodePoints(pipe);
        for (int i = 0; i < pipe.nodes.Count; i++)
        {
            var n = pipe.nodes[i];
            if (!n.hub) continue;

            Vector3 axis   = PipeMesh.AxisAt(nodes, i);
            Vector2 at     = WorldToCanvas(PipeNodeWorldFlat(n));
            Vector2 along  = WorldToCanvas(PipeNodeWorldFlat(n) + axis) - at;
            if (along.sqrMagnitude < 1e-8f) along = Vector2.right;
            along.Normalize();
            Vector2 across = new Vector2(-along.y, along.x);

            float half  = Mathf.Max(PipeNodeRadius + 3f, n.hubSleeveRadius * _zoom);
            float thick = Mathf.Max(3f, n.hubSleeveThickness * _zoom);
            Handles.color = PipeFaded(LevelSelectDesignerSettings.PipeOutlineColour);
            DrawSolidLine(thick + 2f, at - across * (half + 1f), at + across * (half + 1f));
            Handles.color = PipeFaded(LevelSelectDesignerSettings.PipeSupportColour);
            DrawSolidLine(thick, at - across * half, at + across * half);

            if (!n.hubLeadIns) continue;
            float d = Mathf.Max(0f, n.hubLeadInDistance);
            Vector3 flat = PipeNodeWorldFlat(n), flatAxis = new Vector3(axis.x, 0f, axis.z);
            for (int side = -1; side <= 1; side += 2)
            {
                if ((side < 0 && i == 0) || (side > 0 && i == pipe.nodes.Count - 1)) continue;
                Vector2 lead = WorldToCanvas(flat + flatAxis * (d * side));
                Handles.color = PipeFaded(LevelSelectDesignerSettings.PipeOutlineColour);
                Handles.DrawSolidDisc(lead, Vector3.forward, PipeNodeRadius * 0.6f + 1.5f);
                Handles.color = PipeFaded(LevelSelectDesignerSettings.PipeSupportColour);
                Handles.DrawSolidDisc(lead, Vector3.forward, PipeNodeRadius * 0.6f);
            }
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

            Handles.color = PipeFaded(LevelSelectDesignerSettings.PipeOutlineColour);
            DrawSolidLine(thick + 2f, at - across * (half + 1f), at + across * (half + 1f));
            Handles.color = PipeFaded(selected && i == _selectedPipeSupport ? LevelSelectDesignerSettings.PipeSelColour
                                                                   : LevelSelectDesignerSettings.PipeSupportColour);
            DrawSolidLine(thick, at - across * half, at + across * half);
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

    /// <summary>
    /// Adds a node at `at` (0 = off the start, Count = off the end, else between at-1 and at):
    /// halfway between the two nodes either side, or off an end by half the end leg's length.
    /// Supports keep where they stand — a leg split in half hands its supports to whichever half
    /// they are on, and the legs after it are renumbered.
    /// </summary>
    private static void InsertPipeNode(LevelSelectDesignerData.DesignerPipe pipe, int at)
    {
        var nodes = pipe.nodes;
        int count = nodes.Count;
        LevelSelectDesignerData.PipeNode fresh;

        if (at > 0 && at < count)
        {
            int prev = at - 1;
            fresh = new LevelSelectDesignerData.PipeNode
                { positionXZ = (nodes[prev].positionXZ + nodes[at].positionXZ) * 0.5f };

            // A new node between two with their own heights sits between those heights.
            if (nodes[prev].overrideHeight || nodes[at].overrideHeight)
            {
                fresh.overrideHeight = true;
                fresh.height         = (pipe.HeightAt(prev) + pipe.HeightAt(at)) * 0.5f;
            }

            foreach (var s in pipe.supports)
            {
                if (s.leg > prev) s.leg++;
                else if (s.leg == prev)
                {
                    if (s.along < 0.5f) s.along *= 2f;
                    else { s.leg++; s.along = s.along * 2f - 1f; }
                }
            }
        }
        else
        {
            bool    atStart = at <= 0;
            var     endNode = nodes[atStart ? 0 : count - 1];
            Vector2 inner   = nodes[atStart ? 1 : count - 2].positionXZ;
            fresh = new LevelSelectDesignerData.PipeNode
            {
                positionXZ     = endNode.positionXZ + (endNode.positionXZ - inner) * 0.5f,
                overrideHeight = endNode.overrideHeight,
                height         = endNode.height,
            };
            if (atStart) foreach (var s in pipe.supports) s.leg++;
            at = atStart ? 0 : count;
        }

        nodes.Insert(at, fresh);
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

        // A tank whose hub went with the pipe loses its intake.
        RebuildTankIntakes();
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private void DrawSelectedPipeProps()
    {
        if (_mode != DesignerMode.Pipe && _mode != DesignerMode.Select) return;
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
        bool  hub = false, hubLeadIns = false, hubFlip = false;
        float hubLeadInDistance = 0f, sleeveRadius = 0f, sleeveThickness = 0f, ringRadius = 0f, ringThickness = 0f;
        if (hasNode)
        {
            var n = pipe.nodes[_selectedPipeNode];
            hubLeadIns        = n.hubLeadIns;
            hubFlip           = n.hubFlip;
            hubLeadInDistance = n.hubLeadInDistance;
            sleeveRadius      = n.hubSleeveRadius;
            sleeveThickness   = n.hubSleeveThickness;
            ringRadius        = n.hubRingRadius;
            ringThickness     = n.hubRingThickness;
        }
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

            hub = EditorGUILayout.Toggle(
                new GUIContent("Hub", "Make this node a hub: a sleeve round the pipe with a ring standing out of it " +
                                      "at a right angle, which a soul juice tank's intake can be linked into."),
                node.hub);
            if (hub)
            {
                EditorGUI.indentLevel++;
                hubLeadIns = EditorGUILayout.Toggle(
                    new GUIContent("Lead-ins", "Put a lead-in either side of the hub, so the pipe runs straight " +
                                               "through it and bends at the lead-ins instead."),
                    node.hubLeadIns);
                using (new EditorGUI.DisabledScope(!hubLeadIns))
                    hubLeadInDistance = EditorGUILayout.FloatField(
                        new GUIContent("Lead-in Distance", "How far each lead-in is from the hub, along the pipe."),
                        node.hubLeadInDistance);
                sleeveRadius = EditorGUILayout.FloatField(
                    new GUIContent("Sleeve Radius", "Radius of the sleeve round the pipe, out to its outside."),
                    node.hubSleeveRadius);
                sleeveThickness = EditorGUILayout.FloatField(
                    new GUIContent("Sleeve Thickness", "How long the sleeve is along the pipe."),
                    node.hubSleeveThickness);
                ringRadius = EditorGUILayout.FloatField(
                    new GUIContent("Ring Radius", "Radius of the ring standing out of the sleeve, the intake goes into."),
                    node.hubRingRadius);
                ringThickness = EditorGUILayout.FloatField(
                    new GUIContent("Ring Thickness", "How far the ring stands out past the sleeve. It stands out " +
                                                     "level, on the linked tank's side (the right-hand side " +
                                                     "looking along the pipe with none)."),
                    node.hubRingThickness);
                hubFlip = EditorGUILayout.Toggle(
                    new GUIContent("Flip Side", "Put the ring on the other side of the pipe."), node.hubFlip);

                var linked = TankLinkedTo(node.hubId);
                EditorGUILayout.LabelField(linked != null ? $"Feeds the soul juice tank on {linked.nodeId}."
                                                          : "No soul juice tank linked — pick it from a tank's Intake Hub.",
                                           EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }
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

                node.hub = hub;
                if (hub && string.IsNullOrEmpty(node.hubId)) node.hubId = Guid.NewGuid().ToString();
                node.hubLeadIns         = hubLeadIns;
                node.hubLeadInDistance  = Mathf.Max(0f, hubLeadInDistance);
                node.hubSleeveRadius    = Mathf.Max(0.001f, sleeveRadius);
                node.hubSleeveThickness = Mathf.Max(0.001f, sleeveThickness);
                node.hubRingRadius      = Mathf.Max(0.001f, ringRadius);
                node.hubRingThickness   = Mathf.Max(0f, ringThickness);
                node.hubFlip            = hubFlip;
            }
            MarkDirty();
            RebuildAfterEdit(pipe, () => RebuildPipe(pipe));
        }

        if (OcclusionToggle(ref pipe.occlusion, ref pipe.occlusionStyle)) RebuildPipe(pipe);

        if (hasNode)
        {
            int insertAt = -1;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("+ Before", "Add a node between this one and the one before it."),
                                     EditorStyles.miniButtonLeft))
                    insertAt = _selectedPipeNode;
                if (GUILayout.Button(new GUIContent("+ After", "Add a node between this one and the one after it."),
                                     EditorStyles.miniButtonRight))
                    insertAt = _selectedPipeNode + 1;
            }
            if (insertAt >= 0 && pipe.nodes.Count >= 2)
            {
                Undo.RecordObject(_data, "Add Pipe Node");
                InsertPipeNode(pipe, insertAt);
                SelectPipe(pipe.pipeId, insertAt, -1);
                MarkDirty();
                RebuildPipe(pipe);
            }
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

        RebuildTankIntakes();
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

        // A tank's intake reaches this pipe's hub, so it is laid again to meet it.
        RebuildTankIntakes();
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Rebuilds one pipe already in the scene, and nothing else — for when a tank linked to one
    /// of its hubs moves, and the hub's ring only needs turning to face it.
    /// </summary>
    private void BuildPipeIfGenerated(LevelSelectDesignerData.DesignerPipe pipe)
    {
        if (pipe == null || pipe.nodes.Count < 2) return;
        var record = FindObjectsOfType<PipeMesh>().FirstOrDefault(r => r.pipeId == pipe.pipeId);
        if (record != null) BuildPipe(pipe, record.gameObject);
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

        var hubs = new List<PipeMesh.Hub>();
        for (int i = 0; i < pipe.nodes.Count; i++)
            if (pipe.nodes[i].hub) hubs.Add(PipeHubLocal(pipe, i));

        var mesh = SaveGeneratedMesh(name, PipeMesh.Build(
            PipeNodePoints(pipe), pipe.supports.Select(ToMeshSupport).ToList(),
            pipe.pipeThickness, pipe.wallThickness, pipe.supportThickness, pipe.ringOverhang,
            -_data.RunDepth, StoneShadingForBuild, PipeLinePoints(pipe), hubs));
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

        // Its rods are found by the camera already — the toggle only says whether it fades.
        ApplyOcclusion(go, pipe.occlusion, pipe.occlusionStyle, addColliders: false);
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
