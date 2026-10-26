using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// River portholes — a round hole bored straight across a river's run under its channel, square
/// to the river at a node, through both outer walls and any walkways beside them (see
/// <see cref="LevelSelectDesignerData.DesignerRiverPorthole"/>). Ticked in the node's panel, with
/// its radius, its height above the run's underside and an offset along the river.
///
/// A run carries the portholes of its path's nodes on a copy of its profile
/// (<see cref="RiverProfile.portholes"/>), as it carries their widths, so the run and the
/// walkway piece beside it are both built with them, and the run's rings are set closely
/// across each one.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // WHERE ONE CAN GO
    // ─────────────────────────────────────────────

    /// <summary>The same nodes a rim node can stand on, on a river whose run has its outer walls.</summary>
    private bool CanHoldRiverPorthole(string nodeId, out string why)
    {
        bool ok = CanHoldRimNode(nodeId, out why);
        if (!ok)
        {
            if (why != null) why = why.Replace("Rim nodes", "Portholes");
            return false;
        }

        var path = _data.paths.Find(p => p.nodeIds.Contains(nodeId));
        if (path != null && RunWallsOff(path))
        {
            why = "Portholes need the run's outer walls — this river's Run Type has them off.";
            return false;
        }
        return true;
    }

    // The porthole on a node when it is on and can stand there, else null.
    private LevelSelectDesignerData.DesignerRiverPorthole RiverPortholeOn(string nodeId)
    {
        var hole = _data.RiverPortholeAt(nodeId);
        return hole != null && hole.enabled && CanHoldRiverPorthole(nodeId, out _) ? hole : null;
    }

    /// <summary>
    /// How far out from the river's centreline the bore goes at a node of a path, either side:
    /// the run's outer edge, and out across the walkways beside it on the wider side.
    /// </summary>
    private float PortholeReachAt(RiverProfile profile, IList<string> nodeIds, int index)
    {
        float right = 0f, left = 0f;
        foreach (int layer in new[] { 1, 2 })
            foreach (var s in WalkwayStretches(nodeIds, layer))
            {
                if (index < s.from || index > s.to) continue;
                float width = Mathf.Max(s.settings.width, RiverMeshBuilder.WalkwayMinWidth);
                if (s.side > 0) right += width; else left += width;
            }
        return HalfOuterAtNode(profile, nodeIds[index]) + Mathf.Max(right, left);
    }

    // ─────────────────────────────────────────────
    // STAMPING
    // ─────────────────────────────────────────────

    /// <summary>
    /// Stamps the portholes of these nodes onto a run's own copy of its profile, the nodes placed
    /// in that run's space. Left null when none of them has one.
    /// </summary>
    private void StampRunPortholes(RiverProfile profile, LevelSelectDesignerData.DesignerPath path,
                                   IList<string> nodeIds, IList<Vector3> positions)
    {
        profile.portholes = null;
        if (nodeIds == null || positions == null || _data.riverPortholes == null ||
            _data.riverPortholes.Count == 0 || (path != null && RunWallsOff(path)))
            return;

        var keys = new List<RiverProfile.PortholeKey>();
        int n = Mathf.Min(nodeIds.Count, positions.Count);
        for (int i = 0; i < n; i++)
        {
            var hole = RiverPortholeOn(nodeIds[i]);
            if (hole == null) continue;

            keys.Add(new RiverProfile.PortholeKey
            {
                at          = positions[i],
                radius      = hole.radius,
                height      = hole.height,
                offsetAlong = hole.offsetAlong,
                // Generous: it only sets how far either side of the hole the rings are set closely.
                reach       = PortholeReachAt(profile, nodeIds, i) * 1.25f,
            });
        }
        if (keys.Count > 0) profile.portholes = keys.ToArray();
    }

    // A run's copy from what it recorded of its path's nodes.
    private void StampRunPortholes(RiverProfile profile, LevelSelectDesignerData.DesignerPath path,
                                   RiverRunMesh record)
    {
        if (record?.pathNodes == null) { profile.portholes = null; return; }
        StampRunPortholes(profile, path, record.pathNodes.Select(p => p.nodeId).ToList(),
                          record.pathNodes.Select(p => p.position).ToList());
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>The Porthole tick for the selected node, and its radius, height and offset.</summary>
    private void DrawSelectedRiverPortholeProps()
    {
        if (string.IsNullOrEmpty(_selectedNodeId)) return;

        var path = _data.paths.Find(p => p.nodeIds.Contains(_selectedNodeId));
        if (path == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Porthole", EditorStyles.boldLabel);

        if (!CanHoldRiverPorthole(_selectedNodeId, out string why))
        {
            EditorGUILayout.LabelField(why, EditorStyles.miniLabel);
            return;
        }

        var profile = _data.ProfileFor(path.riverName);
        var hole    = _data.RiverPortholeAt(_selectedNodeId);

        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.Toggle(
            new GUIContent("Porthole",
                "Bore a round hole straight across the river's run under its channel, square to " +
                "the river at this node — through both outer walls and any walkways beside them."),
            hole != null && hole.enabled);

        // A new one starts a diameter under the channel floor.
        float radius = hole?.radius ?? 1f;
        float height = hole?.height ?? profile.depth - profile.riverDepth - radius * 2f;
        float offset = hole?.offsetAlong ?? 0f;

        if (on)
        {
            EditorGUI.indentLevel++;
            radius = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Radius", "The porthole's radius."), radius));
            height = EditorGUILayout.FloatField(
                new GUIContent("Height", "How high the porthole's centre is above the run's underside."),
                height);
            offset = EditorGUILayout.FloatField(
                new GUIContent("Offset Along", "Moves the porthole along the river from its node — " +
                                               "the way the path was drawn when positive, back when " +
                                               "negative."),
                offset);

            if (!RiverMeshBuilder.PortholeFit(ProfileAtNode(profile, _selectedNodeId), radius, height,
                                              out float fitRadius, out float fitHeight))
                EditorGUILayout.LabelField("No room for a porthole under this river's channel.",
                                           EditorStyles.miniLabel);
            else if (!Mathf.Approximately(fitRadius, radius) || !Mathf.Approximately(fitHeight, height))
                EditorGUILayout.LabelField(
                    $"Built at radius {fitRadius:0.###}, height {fitHeight:0.###} — what fits " +
                    $"under the channel (underside to channel floor is {profile.depth - profile.riverDepth:0.###}).",
                    EditorStyles.wordWrappedMiniLabel);

            if (_data.RimNodeAt(_selectedNodeId) != null)
                EditorGUILayout.LabelField("The rim node on this node stands in the porthole's way.",
                                           EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
        }

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Edit Porthole");
        _data.riverPortholes ??= new List<LevelSelectDesignerData.DesignerRiverPorthole>();
        if (hole == null)
        {
            hole = new LevelSelectDesignerData.DesignerRiverPorthole { nodeId = _selectedNodeId };
            _data.riverPortholes.Add(hole);
        }
        hole.enabled     = on;
        hole.radius      = radius;
        hole.height      = height;
        hole.offsetAlong = offset;

        MarkDirty();
        string riverName = path.riverName;
        RebuildAfterTowerEdit("Porthole:" + path.pathId, () => RebuildRunMeshes(riverName));
        Repaint();
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Each porthole from above: a dark band straight across the river and its walkways at its
    /// node, as wide as the hole. Drawn against the drawn path, which is close to the run but not
    /// on it.
    /// </summary>
    private void DrawRiverPortholes()
    {
        if (Event.current.type != EventType.Repaint || _data.riverPortholes == null ||
            _data.riverPortholes.Count == 0)
            return;

        foreach (var entry in _data.riverPortholes)
        {
            if (entry == null || RiverPortholeOn(entry.nodeId) == null) continue;

            var path = _data.paths.Find(p => p.nodeIds.Contains(entry.nodeId));
            if (path == null || path.nodeIds.Count < 2) continue;

            int index = path.nodeIds.IndexOf(entry.nodeId);
            var profile = _data.ProfileFor(path.riverName);
            if (!RiverMeshBuilder.PortholeFit(ProfileAtNode(profile, entry.nodeId), entry.radius,
                                              entry.height, out float radius, out _))
                continue;

            Vector3 right = DrawnRightAt(path, index);
            Vector3 along = Vector3.Cross(right, Vector3.up);
            Vector3 at    = _data.NodeWorldPosition(entry.nodeId) + along * entry.offsetAlong;
            float   reach = PortholeReachAt(profile, path.nodeIds, index);

            bool selected = entry.nodeId == _selectedNodeId;
            Handles.color = Faded(selected ? new Color(0.2f, 0.2f, 0.2f, 0.9f)
                                           : new Color(0.3f, 0.3f, 0.3f, 0.7f));
            Handles.DrawAAConvexPolygon(
                (Vector3)WorldToCanvas(at - right * reach - along * radius),
                (Vector3)WorldToCanvas(at + right * reach - along * radius),
                (Vector3)WorldToCanvas(at + right * reach + along * radius),
                (Vector3)WorldToCanvas(at - right * reach + along * radius));
        }
    }
}

#endif
