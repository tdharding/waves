using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Junction lead-ins — the pool lead-in pattern carried over to junctions, to smooth the kinks
/// where a branch leaves a river.
///
/// A junction puts a lead-in on every river next to it, all at the junction node's height. The
/// river running through gets one either side, on one straight line across the junction, lined
/// up with the way the river runs between the nodes past them. The branch gets one straight out
/// along the way it leaves. Each stands the designer's Lead-in Distance past the junction's
/// mouth: along the river from the edge of the opening, along the branch from where it is cut
/// off.
///
///                    o  branch lead-in
///                    |
///          ──o──────(J)──────o──     river lead-ins either side, same height as J
///
/// Like a pool's, a junction lead-in is never authored: it is re-placed every time the designer
/// lays out, so moving the junction, its height, the nodes past the lead-ins or the distance all
/// carry it. It cannot be dragged, have its height edited, or be deleted on its own; removing the
/// junction deletes them.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    /// <summary>
    /// Gives every river next to the junction a lead-in, keeping any it already has. Called when
    /// a junction is made, and by Refresh Lead-ins on one made before there were lead-ins.
    /// </summary>
    private void RefreshJunctionLeadIns(LevelSelectDesignerData.DesignerJunction junc)
    {
        if (junc == null || _data.nodes.Find(n => n.id == junc.nodeId) == null) return;

        junc.leadIns ??= new List<LevelSelectDesignerData.JunctionLeadIn>();
        PruneJunctionLeadIns(junc);

        foreach (var (path, neighbourId, spot) in JunctionLeadInSpots(junc))
        {
            if (junc.leadIns.Exists(l => l.nodeId == neighbourId)) continue;

            // A lead-in standing past the next node out would turn the river back on itself.
            Vector3 far  = _data.NodeWorldPosition(JunctionFarNode(path, junc.nodeId, neighbourId));
            Vector3 here = _data.NodeWorldPosition(junc.nodeId);
            if (FlatDistance(here, spot) >= FlatDistance(here, far))
                Debug.LogWarning($"[LevelSelectDesigner] Junction {junc.nodeId}: the lead-in on " +
                                 $"'{path.segmentId}' stands past the next node — move that node " +
                                 "further out, or lower the junction Lead-in Distance.");

            var node = AddNode(spot, LevelSelectDesignerData.NodeType.Waypoint);
            InsertNodeBetween(path, junc.nodeId, neighbourId, node);
            junc.leadIns.Add(new LevelSelectDesignerData.JunctionLeadIn { nodeId = node.id });
        }

        PlaceJunctionLeadIns(junc);
    }

    /// <summary>Deletes every lead-in the junction placed, closing each river back up to it.</summary>
    private void RemoveJunctionLeadIns(LevelSelectDesignerData.DesignerJunction junc)
    {
        if (junc?.leadIns == null) return;

        foreach (var leadIn in junc.leadIns.ToList())
            DeleteNode(leadIn.nodeId);

        junc.leadIns.Clear();
    }

    /// <summary>Keeps every lead-in where its junction puts it. Run as the designer lays out.</summary>
    private void SyncJunctionLeadIns()
    {
        if (_data?.junctions == null) return;

        bool changed = false;
        foreach (var junc in _data.junctions)
        {
            if (junc?.leadIns == null || junc.leadIns.Count == 0) continue;
            changed |= PruneJunctionLeadIns(junc);
            changed |= PlaceJunctionLeadIns(junc);
        }

        if (changed) MarkDirty();
    }

    // Stands each lead-in on its spot. True when any moved.
    private bool PlaceJunctionLeadIns(LevelSelectDesignerData.DesignerJunction junc)
    {
        bool changed = false;
        foreach (var (_, neighbourId, spot) in JunctionLeadInSpots(junc))
        {
            if (!junc.leadIns.Exists(l => l.nodeId == neighbourId)) continue;
            var node = _data.nodes.Find(n => n.id == neighbourId);
            if (node != null) changed |= MoveNode(node, spot);
        }
        return changed;
    }

    // Forgets lead-ins whose node is gone or no longer sits next to the junction on any river —
    // the node itself is left alone. True when any were forgotten.
    private bool PruneJunctionLeadIns(LevelSelectDesignerData.DesignerJunction junc)
    {
        int before = junc.leadIns.Count;
        var rivers = RiversAtNode(junc.nodeId);
        junc.leadIns.RemoveAll(l => l == null
                                 || _data.nodes.Find(n => n.id == l.nodeId) == null
                                 || !rivers.Exists(r => r.neighbourId == l.nodeId));
        return junc.leadIns.Count != before;
    }

    /// <summary>
    /// Where a lead-in belongs on each river next to the junction, whether it has one there yet
    /// or not. Every spot is at the junction node's height.
    /// </summary>
    private List<(LevelSelectDesignerData.DesignerPath path, string neighbourId, Vector3 spot)>
        JunctionLeadInSpots(LevelSelectDesignerData.DesignerJunction junc)
    {
        var spots  = new List<(LevelSelectDesignerData.DesignerPath, string, Vector3)>();
        var jNode  = _data.nodes.Find(n => n.id == junc.nodeId);
        if (jNode == null) return spots;

        Vector3 centre = jNode.worldPosition;
        var     rivers = RiversAtNode(junc.nodeId);

        // The way out from the junction toward this neighbour. On a river running through, it is
        // the line between the nodes past the two neighbours, so both lead-ins sit on one
        // straight line across the junction; on a branch, straight out to the node past it.
        Vector3 Heading(LevelSelectDesignerData.DesignerPath path, string neighbourId)
        {
            Vector3 far   = _data.NodeWorldPosition(JunctionFarNode(path, junc.nodeId, neighbourId));
            string  other = NeighbourAcross(path, junc.nodeId, neighbourId);

            Vector3 d = other != null
                ? far - _data.NodeWorldPosition(JunctionFarNode(path, junc.nodeId, other))
                : far - centre;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-8f) { d = far - centre; d.y = 0f; }
            return d.sqrMagnitude < 1e-8f ? Vector3.zero : d.normalized;
        }

        // How far out — past the mouth the branch cuts into the river, measured the way
        // Generate cuts it.
        float extra       = Mathf.Max(0f, _data.junctionLeadInDistance);
        float alongRiver  = extra;
        float alongBranch = extra;

        var main   = rivers.FirstOrDefault(r => HowRiverMeets(r.path, junc.nodeId) == RiverAtNode.Through);
        var branch = rivers.FirstOrDefault(r => HowRiverMeets(r.path, junc.nodeId) != RiverAtNode.Through);
        if (main.path != null && branch.path != null)
        {
            Vector3 axis = Heading(main.path,   main.neighbourId);
            Vector3 away = Heading(branch.path, branch.neighbourId);
            if (axis != Vector3.zero && away != Vector3.zero)
            {
                var   mainProfile   = _data.ProfileFor(main.path.riverName);
                var   branchProfile = _data.ProfileFor(branch.path.riverName);
                float angle         = Vector3.Angle(axis, away) * Mathf.Deg2Rad;
                alongRiver  += RiverMeshBuilder.JunctionSpan(mainProfile, branchProfile, angle);
                alongBranch += RiverMeshBuilder.RunCollar(mainProfile, branchProfile, angle, MeshEdge);
            }
        }

        foreach (var (path, neighbourId) in rivers)
        {
            Vector3 heading = Heading(path, neighbourId);
            if (heading == Vector3.zero) continue;

            bool through = HowRiverMeets(path, junc.nodeId) == RiverAtNode.Through;
            spots.Add((path, neighbourId, centre + heading * (through ? alongRiver : alongBranch)));
        }
        return spots;
    }

    /// <summary>
    /// The first node out from the junction along the river, starting at <paramref name="neighbourId"/>,
    /// that the designer doesn't place itself. Junction lead-ins and in-between nodes are placed
    /// off their neighbours, so reading one of them would chase its own tail. The river's end
    /// when there is nothing else.
    /// </summary>
    private string JunctionFarNode(LevelSelectDesignerData.DesignerPath path, string junctionId, string neighbourId)
    {
        var ids = path.nodeIds;
        int j   = ids.IndexOf(junctionId);
        if (j < 0) return neighbourId;

        int step = j + 1 < ids.Count && ids[j + 1] == neighbourId ? 1 : -1;
        int i    = j + step;
        if (i < 0 || i >= ids.Count) return neighbourId;

        while (i + step >= 0 && i + step < ids.Count &&
               (_data.JunctionOfLeadIn(ids[i]) != null || IsPlacedInBetween(ids[i])))
            i += step;
        return ids[i];
    }

    // The node next to the junction on the other side from `neighbourId`, or null when the river
    // starts or ends at the junction.
    private static string NeighbourAcross(LevelSelectDesignerData.DesignerPath path, string junctionId, string neighbourId)
    {
        var    ids    = path.nodeIds;
        int    j      = ids.IndexOf(junctionId);
        string before = j > 0             ? ids[j - 1] : null;
        string after  = j + 1 < ids.Count ? ids[j + 1] : null;
        return neighbourId == before ? after : before;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>
    /// Every lead-in placed by whatever stands on this node — a pool, a junction, an arena (all
    /// its entrances' lead-ins), or one arena entrance (its own). Drawn selected with the node.
    /// </summary>
    private void CollectLeadInsOf(string nodeId, HashSet<string> into)
    {
        into.Clear();
        if (_data == null || string.IsNullOrEmpty(nodeId)) return;

        var pool = _data.PoolAt(nodeId);
        if (pool?.leadIns != null)
            foreach (var l in pool.leadIns) into.Add(l.nodeId);

        var junc = _data.junctions.Find(j => j != null && j.nodeId == nodeId);
        if (junc?.leadIns != null)
            foreach (var l in junc.leadIns) into.Add(l.nodeId);

        foreach (var arena in _data.arenas)
        {
            if (arena?.leadIns == null) continue;
            bool whole = arena.nodeId == nodeId;
            foreach (var l in arena.leadIns)
                if (whole || l.entranceNodeId == nodeId) into.Add(l.nodeId);
        }
    }
}

#endif
