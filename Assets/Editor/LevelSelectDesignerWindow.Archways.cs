using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// River archways — a pillar on the rim either side of the river, joined over the water by a
/// round arch (see <see cref="LevelSelectDesignerData.DesignerArchway"/>). Added with Add Archway
/// on a node; its arch height, thickness, pillar width, arch depth, offset along the river and
/// curved corners are set in the node's panel. It can stand on the nodes a rim node can, and on either end of a river.
///
/// Built by <see cref="RiverMeshBuilder.BuildWalkways"/> into the same piece as the walkways
/// beside each run, in the river material, so it is rebuilt whenever they are.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // WHERE ONE CAN GO
    // ─────────────────────────────────────────────

    /// <summary>
    /// Whether a river can carry an archway at a node: either end of it, or anywhere a rim node
    /// can go.
    /// </summary>
    private bool CanHoldArchway(LevelSelectDesignerData.DesignerPath path, string nodeId,
                                out string why)
    {
        why = null;
        if (path == null || path.nodeIds.Count < 2) { why = "Archways need a river."; return false; }

        int at = path.nodeIds.IndexOf(nodeId);
        if (at < 0) { why = "Node not found on this river."; return false; }
        if (at == 0 || at == path.nodeIds.Count - 1) return true;

        bool ok = CanHoldRimNode(nodeId, out why);
        if (!ok && why != null) why = why.Replace("Rim nodes", "Archways");
        return ok;
    }

    // ─────────────────────────────────────────────
    // ADDING
    // ─────────────────────────────────────────────

    // Add Archway / Remove Archway is in a node's Add ▾ menu (NodeAddMenu.cs).
    private void ToggleArchwayAt(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        Undo.RecordObject(_data, "Toggle Archway");
        _data.archways ??= new List<LevelSelectDesignerData.DesignerArchway>();

        var archway = _data.ArchwayAt(nodeId, path.pathId);
        if (archway != null) _data.archways.Remove(archway);
        else _data.archways.Add(new LevelSelectDesignerData.DesignerArchway
                                { nodeId = nodeId, pathId = path.pathId });

        MarkDirty();
        string pathId = path.pathId;
        RebuildAfterTowerEdit("Archway:" + pathId, () => RebuildWalkwaysOnPath(pathId, false));
        Repaint();
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>
    /// The selected node's archways — one per river through it, which is only ever more than one
    /// at an end shared by several rivers.
    /// </summary>
    private void DrawSelectedArchwayProps()
    {
        if (string.IsNullOrEmpty(_selectedNodeId) || _data.archways == null) return;

        foreach (var archway in _data.archways.Where(a => a != null && a.nodeId == _selectedNodeId)
                                              .ToList())
        {
            var path = _data.paths.Find(p => p.pathId == archway.pathId);
            if (path == null) continue;

            EditorGUILayout.Space(6);
            bool shared = _data.paths.Count(p => p.nodeIds.Contains(_selectedNodeId)) > 1;
            EditorGUILayout.LabelField(shared ? $"Archway — {path.segmentId}" : "Archway",
                                       EditorStyles.boldLabel);

            if (!CanHoldArchway(path, _selectedNodeId, out string why))
            {
                EditorGUILayout.LabelField(why, EditorStyles.miniLabel);
                continue;
            }

            EditorGUI.BeginChangeCheck();
            float archHeight = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                new GUIContent("Arch Height", "How high the top of the opening is above the rim."),
                archway.archHeight));
            float thickness = Mathf.Max(0.005f, EditorGUILayout.FloatField(
                new GUIContent("Thickness", "How deep the archway is, along the river."),
                archway.thickness));
            float pillarWidth = Mathf.Max(0.005f, EditorGUILayout.FloatField(
                new GUIContent("Pillar Width", "How far each pillar reaches out from the " +
                                               "channel's edge, over the rim."),
                archway.pillarWidth));
            float archDepth = Mathf.Max(0.005f, EditorGUILayout.FloatField(
                new GUIContent("Arch Depth", "How much stone stands above the top of the opening."),
                archway.archDepth));
            float offsetAlong = EditorGUILayout.FloatField(
                new GUIContent("Offset Along", "Moves the archway along the river from its node — " +
                                               "the way the path was drawn when positive, back when " +
                                               "negative. It stays on the river."),
                archway.offsetAlong);
            bool curvedCorners = EditorGUILayout.Toggle(
                new GUIContent("Curved Corners", "Rounds off the archway's two top outer corners."),
                archway.curvedCorners);
            float cornerRadius = archway.cornerRadius;
            if (curvedCorners)
            {
                EditorGUI.indentLevel++;
                cornerRadius = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Corner Radius", "How far round each curved corner reaches — " +
                                                    "kept within the stone beside and above the " +
                                                    "opening."),
                    archway.cornerRadius));
                EditorGUI.indentLevel--;
            }
            bool occlusion = archway.occlusion;
            var  occlusionStyle = archway.occlusionStyle;
            OcclusionField(ref occlusion, ref occlusionStyle);
            if (!EditorGUI.EndChangeCheck()) continue;

            Undo.RecordObject(_data, "Edit Archway");
            archway.archHeight  = archHeight;
            archway.thickness   = thickness;
            archway.pillarWidth = pillarWidth;
            archway.archDepth   = archDepth;
            archway.offsetAlong   = offsetAlong;
            archway.curvedCorners = curvedCorners;
            archway.cornerRadius  = cornerRadius;
            archway.occlusion     = occlusion;
            archway.occlusionStyle = occlusionStyle;

            MarkDirty();
            string pathId = path.pathId;
            RebuildAfterTowerEdit("Archway:" + pathId, () => RebuildWalkwaysOnPath(pathId, false));
            Repaint();
        }
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Each archway from above: the span over the drawn path at its node, and a pillar either
    /// side of it. Drawn against the drawn path, which is close to the run but not on it.
    /// </summary>
    private void DrawArchways()
    {
        if (Event.current.type != EventType.Repaint || _data.archways == null ||
            _data.archways.Count == 0)
            return;

        foreach (var archway in _data.archways)
        {
            if (archway == null) continue;
            var path = _data.paths.Find(p => p.pathId == archway.pathId);
            if (path == null || path.nodeIds.Count < 2) continue;

            int index = path.nodeIds.IndexOf(archway.nodeId);
            if (index < 0) continue;

            var     profile = _data.ProfileFor(path.riverName);
            Vector3 right   = DrawnRightAt(path, index);
            Vector3 along   = Vector3.Cross(right, Vector3.up);
            Vector3 at      = _data.NodeWorldPosition(archway.nodeId) + along * archway.offsetAlong;

            float hw    = Mathf.Max(archway.thickness, 0.005f) * 0.5f;
            float inner = InnerWidthAtNode(profile, archway.nodeId) * 0.5f;
            float reach = inner + Mathf.Max(archway.pillarWidth, 0.005f);

            void Band(float from, float to)
            {
                Handles.DrawAAConvexPolygon(
                    (Vector3)WorldToCanvas(at + right * from - along * hw),
                    (Vector3)WorldToCanvas(at + right * to   - along * hw),
                    (Vector3)WorldToCanvas(at + right * to   + along * hw),
                    (Vector3)WorldToCanvas(at + right * from + along * hw));
            }

            bool selected = archway.nodeId == _selectedNodeId;

            // The span over the water lighter than the pillars, so the opening reads at a glance.
            Handles.color = Faded(selected ? new Color(0.98f, 0.98f, 0.95f, 0.75f)
                                     : new Color(0.85f, 0.85f, 0.82f, 0.6f));
            Band(-inner, inner);

            Handles.color = Faded(selected ? new Color(0.8f, 0.8f, 0.77f, 0.9f)
                                     : new Color(0.62f, 0.62f, 0.6f, 0.85f));
            Band(-reach, -inner);
            Band(inner, reach);
        }
    }

    // ─────────────────────────────────────────────
    // BUILDING
    // ─────────────────────────────────────────────

    /// <summary>
    /// The archways across a run, as the builder takes them — in the run's own space. With
    /// <paramref name="occluded"/>, one entry per archway: a name for each ticked for Occlusion,
    /// null for the rest.
    /// </summary>
    private List<RiverMeshBuilder.Archway> ArchwaysFor(RiverRunMesh record, List<string> occluded = null,
        List<OcclusionStyle> occludedStyles = null)
    {
        var archways = new List<RiverMeshBuilder.Archway>();
        if (record?.pathNodes == null || record.pathNodes.Count < 2 || _data.archways == null ||
            _data.archways.Count == 0)
            return archways;

        var path = _data.paths.Find(p => p.pathId == record.pathId);
        if (path == null) return archways;

        foreach (var node in record.pathNodes)
        {
            var archway = _data.ArchwayAt(node.nodeId, record.pathId);
            if (archway == null || !CanHoldArchway(path, node.nodeId, out _)) continue;

            archways.Add(new RiverMeshBuilder.Archway
            {
                at          = node.position,
                archHeight  = archway.archHeight,
                thickness   = archway.thickness,
                pillarWidth = archway.pillarWidth,
                archDepth   = archway.archDepth,
                offsetAlong = archway.offsetAlong,
                cornerRadius = archway.curvedCorners ? archway.cornerRadius : 0f,
            });
            occluded?.Add(archway.occlusion ? $"Archway_{SanitiseAssetName(node.nodeId)}" : null);
            occludedStyles?.Add(archway.occlusionStyle);
        }
        return archways;
    }
}

#endif
