using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// River width overrides — a node where the river's channel is wider or narrower than its Run
/// Shape's Inner Width (see <see cref="LevelSelectDesignerData.DesignerRiverWidth"/>). Ticked in the
/// node's panel. The rims keep their own width either side, so the outer edge moves with it.
///
/// The width eases from one node to the next — each node without an override counts as the
/// river's usual width — and is level at every node. Only the same nodes a rim node can stand on
/// take one, so a junction's mouth, a pool and a river's ends always meet the usual width.
///
/// A run carries the width at every node of its path on a copy of its profile
/// (<see cref="RiverProfile.widthKeys"/>), and everything built from that profile follows it:
/// the run, its water and banks, rim nodes, walkways, bridges and archways. The canvas, the
/// landscape's cut and outposts read the same widths here.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // WHERE ONE CAN GO, AND HOW WIDE THE RIVER IS
    // ─────────────────────────────────────────────

    /// <summary>The same nodes a rim node can stand on — plain nodes partway along one river.</summary>
    private bool CanHoldRiverWidth(string nodeId, out string why)
    {
        bool ok = CanHoldRimNode(nodeId, out why);
        if (!ok && why != null) why = why.Replace("Rim nodes", "Width overrides");
        return ok;
    }

    /// <summary>The channel's width at a node: its override when it has one on, else the river's own.</summary>
    private float InnerWidthAtNode(RiverProfile profile, string nodeId)
    {
        var over = _data.RiverWidthAt(nodeId);
        return over != null && over.enabled && CanHoldRiverWidth(nodeId, out _)
             ? Mathf.Max(0.001f, over.innerWidth)
             : profile.innerWidth;
    }

    /// <summary>Whether any node of these carries a width override that is on.</summary>
    private bool AnyRiverWidth(IList<string> nodeIds)
    {
        if (_data.riverWidths == null || _data.riverWidths.Count == 0 || nodeIds == null) return false;
        foreach (string id in nodeIds)
        {
            var over = _data.RiverWidthAt(id);
            if (over != null && over.enabled) return true;
        }
        return false;
    }

    /// <summary>
    /// Stamps the width at every node onto a run's own copy of its profile, the nodes placed in
    /// that run's space. Left alone when no node overrides it, so the run builds as it always did.
    /// </summary>
    private void StampRunWidths(RiverProfile profile, IList<string> nodeIds, IList<Vector3> positions)
    {
        profile.widthKeys = null;
        if (nodeIds == null || positions == null || !AnyRiverWidth(nodeIds)) return;

        int n = Mathf.Min(nodeIds.Count, positions.Count);
        var keys = new RiverProfile.WidthKey[n];
        for (int i = 0; i < n; i++)
            keys[i] = new RiverProfile.WidthKey
            {
                at         = positions[i],
                innerWidth = InnerWidthAtNode(profile, nodeIds[i]),
            };
        profile.widthKeys = keys;
    }

    // A run's copy from what it recorded of its path's nodes.
    private void StampRunWidths(RiverProfile profile, RiverRunMesh record)
    {
        if (record?.pathNodes == null) { profile.widthKeys = null; return; }
        StampRunWidths(profile, record.pathNodes.Select(p => p.nodeId).ToList(),
                       record.pathNodes.Select(p => p.position).ToList());
    }

    /// <summary>
    /// The channel's width a share of the way along a path — <paramref name="pathT"/> 0 at its
    /// first node, 1 at its last, the nodes evenly spaced in between — eased between the nodes
    /// either side as the run is.
    /// </summary>
    private float InnerWidthAlongPath(LevelSelectDesignerData.DesignerPath path, RiverProfile profile,
                                      float pathT)
    {
        if (path == null || path.nodeIds.Count < 2 || !AnyRiverWidth(path.nodeIds))
            return profile.innerWidth;

        float f = Mathf.Clamp01(pathT) * (path.nodeIds.Count - 1);
        int   i = Mathf.Min(Mathf.FloorToInt(f), path.nodeIds.Count - 2);
        return RiverMeshBuilder.EaseWidth(InnerWidthAtNode(profile, path.nodeIds[i]),
                                          InnerWidthAtNode(profile, path.nodeIds[i + 1]), f - i);
    }

    /// <summary>Half the run's outer width at a node of its path.</summary>
    private float HalfOuterAtNode(RiverProfile profile, string nodeId)
        => InnerWidthAtNode(profile, nodeId) * 0.5f + profile.rimWidth;

    /// <summary>The river's Run Shape at the channel's width at one node.</summary>
    private RiverProfile ProfileAtNode(RiverProfile profile, string nodeId)
    {
        float width = InnerWidthAtNode(profile, nodeId);
        return Mathf.Approximately(width, profile.innerWidth) ? profile : profile.WithInnerWidth(width);
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>The River Width tick for the selected node, and the width it overrides to.</summary>
    private void DrawSelectedRiverWidthProps()
    {
        if (string.IsNullOrEmpty(_selectedNodeId)) return;

        var path = _data.paths.Find(p => p.nodeIds.Contains(_selectedNodeId));
        if (path == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("River Width", EditorStyles.boldLabel);

        if (!CanHoldRiverWidth(_selectedNodeId, out string why))
        {
            EditorGUILayout.LabelField(why, EditorStyles.miniLabel);
            return;
        }

        var profile = _data.ProfileFor(path.riverName);
        var over    = _data.RiverWidthAt(_selectedNodeId);

        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.Toggle(
            new GUIContent("Width Override",
                "The river's channel is a width of its own at this node. It eases to it from the " +
                "nodes either side, and its rims keep their width, so the outer edge moves with it."),
            over != null && over.enabled);

        float width = over?.innerWidth ?? profile.innerWidth;
        if (on)
        {
            width = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                new GUIContent("Inner Width",
                    "Width of the channel opening across the top of the run at this node. The " +
                    $"river's own is {profile.innerWidth:0.###}."),
                width));
            EditorGUILayout.LabelField(
                $"Outer width here {width + profile.rimWidth * 2f:0.###}  (inner + 2 x rim)",
                EditorStyles.miniLabel);
        }

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Edit River Width");
        _data.riverWidths ??= new List<LevelSelectDesignerData.DesignerRiverWidth>();
        if (over == null)
        {
            over = new LevelSelectDesignerData.DesignerRiverWidth { nodeId = _selectedNodeId };
            _data.riverWidths.Add(over);
        }
        over.enabled    = on;
        over.innerWidth = width;

        MarkDirty();
        string riverName = path.riverName;
        string pathId    = path.pathId;
        RebuildAfterTowerEdit("RiverWidth:" + pathId, () =>
        {
            RebuildRunMeshes(riverName);
            RebuildOutpostsOnPath(pathId);
        });
        Repaint();
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// The drawn path's points with each one's half channel width — the segments either side of
    /// a width override cut into short pieces, so the canvas shows the same eased swell the run
    /// is built with rather than a straight taper.
    /// </summary>
    private void PathPointsWithWidths(LevelSelectDesignerData.DesignerPath path, RiverProfile profile,
                                      List<Vector3> points, out List<Vector3> fine, out List<float> halfInner)
    {
        fine      = new List<Vector3>(points.Count);
        halfInner = new List<float>(points.Count);

        int n = Mathf.Min(points.Count, path.nodeIds.Count);
        if (!AnyRiverWidth(path.nodeIds))
        {
            fine.AddRange(points);
            for (int i = 0; i < points.Count; i++) halfInner.Add(profile.innerWidth * 0.5f);
            return;
        }

        const int Pieces = 8;
        for (int i = 0; i < n; i++)
        {
            float w = InnerWidthAtNode(profile, path.nodeIds[i]);
            fine.Add(points[i]);
            halfInner.Add(w * 0.5f);
            if (i == n - 1) break;

            float next = InnerWidthAtNode(profile, path.nodeIds[i + 1]);
            if (Mathf.Approximately(w, next)) continue;

            for (int k = 1; k < Pieces; k++)
            {
                float t = (float)k / Pieces;
                fine.Add(Vector3.Lerp(points[i], points[i + 1], t));
                halfInner.Add(RiverMeshBuilder.EaseWidth(w, next, t) * 0.5f);
            }
        }
    }

    // ─────────────────────────────────────────────
    // REBUILDING
    // ─────────────────────────────────────────────

    /// <summary>Rebuilds the outposts standing on a path — they move out with the run's edge.</summary>
    private void RebuildOutpostsOnPath(string pathId)
    {
        if (_data.outposts == null) return;

        var blocks = FindObjectsOfType<InstallationOutpostMesh>();
        foreach (var outpost in _data.outposts)
        {
            if (outpost == null || outpost.pathId != pathId) continue;
            var block = blocks.FirstOrDefault(r => r.outpostId == outpost.outpostId);
            if (block != null) BuildOutpostBlocks(outpost, block.transform.parent);
        }
    }
}

#endif
