using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// In-Between nodes — a node the designer places itself, to smooth a river.
///
/// An in-between node stands on the straight line between the nearest ordinary nodes either side
/// of it on its river, height included. Several in a row share that stretch evenly. Nothing moves
/// one by hand: it is re-placed every time the window lays out, so it follows whatever moved its
/// neighbours — a node drag, a River Select drag, an undo.
///
/// Only a plain node in a river's middle can be one. A node that later stops being one (a river
/// split there, another river joined it) keeps its flag but stays where it was left.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    /// <summary>Why the node can't be in-between, or null when it can.</summary>
    private string WhyCannotBeInBetween(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        int index = path.nodeIds.IndexOf(nodeId);
        if (index <= 0 || index >= path.nodeIds.Count - 1)       return "river end";
        if (_data.PoolAt(nodeId) != null)                         return "pool";
        if (IsLockedNode(nodeId))                                 return "lead-in";
        if (_data.arenas.Exists(a => a.nodeId == nodeId) ||
            FindArenaForSecondaryNode(nodeId) != null)            return "arena";
        if (_data.junctions.Exists(j => j.nodeId == nodeId) ||
            _data.paths.Exists(p => p != path && p.nodeIds.Contains(nodeId)))
                                                                  return "another river meets it";
        return null;
    }

    /// <summary>True when the node is in-between and the designer is placing it.</summary>
    private bool IsPlacedInBetween(string nodeId)
    {
        var node = _data?.nodes.Find(n => n.id == nodeId);
        if (node == null || !node.inBetween) return false;
        var path = _data.paths.Find(p => p.nodeIds.Contains(nodeId));
        return path != null && WhyCannotBeInBetween(path, nodeId) == null;
    }

    private readonly Dictionary<string, LevelSelectDesignerData.DesignerNode> _inBetweenNodeLookup = new();

    /// <summary>
    /// Stands every in-between node on the line between the ordinary nodes either side of it.
    /// Run after the lead-ins are synced, since a lead-in can be one of those ends.
    /// </summary>
    private void SyncInBetweenNodes()
    {
        if (_data?.paths == null || _data.nodes == null) return;

        // Runs on every layout, so it costs nothing until a node is flagged.
        var lookup = _inBetweenNodeLookup;
        lookup.Clear();
        foreach (var n in _data.nodes)
            if (n != null && n.inBetween) lookup[n.id] = n;
        if (lookup.Count == 0) return;

        bool Placed(LevelSelectDesignerData.DesignerPath path, string id)
            => lookup.ContainsKey(id) && WhyCannotBeInBetween(path, id) == null;

        bool changed = false;
        foreach (var path in _data.paths)
        {
            var ids = path.nodeIds;
            for (int i = 1; i < ids.Count - 1; i++)
            {
                if (!Placed(path, ids[i])) continue;

                // The run of in-between nodes starting here, and the ordinary node past each end.
                int last = i;
                while (last + 1 < ids.Count - 1 && Placed(path, ids[last + 1])) last++;

                var from = _data.nodes.Find(n => n.id == ids[i - 1]);
                var to   = _data.nodes.Find(n => n.id == ids[last + 1]);
                if (from != null && to != null)
                {
                    int span = last + 2 - i;
                    for (int k = i; k <= last; k++)
                    {
                        var node = lookup[ids[k]];
                        Vector3 want = Vector3.Lerp(from.worldPosition, to.worldPosition, (k - i + 1) / (float)span);
                        if ((node.worldPosition - want).sqrMagnitude < 1e-10f) continue;
                        node.worldPosition = want;
                        changed = true;
                    }
                }
                i = last;
            }
        }

        if (changed) MarkDirty();
    }

    // The In-Between toggle in a node's open row.
    private void DrawNodeInBetween(LevelSelectDesignerData.DesignerPath path,
                                   LevelSelectDesignerData.DesignerNode node)
    {
        string why = WhyCannotBeInBetween(path, node.id);

        EditorGUI.BeginChangeCheck();
        GUI.enabled = why == null || node.inBetween;
        bool on = EditorGUILayout.ToggleLeft(new GUIContent(
            why == null ? "In-Between" : $"In-Between  ({why})",
            "Places this node evenly between the nodes either side of it, height too, to smooth " +
            "the river. It can't be moved by hand while on; it follows its neighbours, and moves " +
            "with its river under River Select."), node.inBetween);
        GUI.enabled = true;
        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Node In-Between");
        node.inBetween = on;
        SyncInBetweenNodes();
        MarkDirty();
        Repaint();
    }
}

#endif
