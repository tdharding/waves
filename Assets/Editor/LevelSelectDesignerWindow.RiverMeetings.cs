using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Where two rivers share a node, and what that meeting is: a junction (one river runs on
/// through, the other leaves from it as a branch) or a river join (one ends there, the other
/// starts there, end to end). Generate guesses a junction at any shared node nobody has named,
/// and that guess breaks when neither river runs through — so these let a meeting be named
/// from the node's own menu, and a river drawn from another river's node is named as it's drawn.
///
/// Carrying a river on past its end, where another river starts, is how an end-to-end meeting
/// becomes a junction: the river runs through, and the one starting there is its branch.
///
/// A branch has to START at its junction — Generate reads the way it leaves off the node after
/// the junction, and trims only a branch's start. A river that ends on another river's middle
/// is left as it is.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private enum RiverAtNode { None, Starts, Ends, Through }

    private static RiverAtNode HowRiverMeets(LevelSelectDesignerData.DesignerPath p, string nodeId)
    {
        int i = p.nodeIds.IndexOf(nodeId);
        if (i < 0)                    return RiverAtNode.None;
        if (i == 0)                   return RiverAtNode.Starts;
        if (i == p.nodeIds.Count - 1) return RiverAtNode.Ends;
        return RiverAtNode.Through;
    }

    // Every river with this node in it.
    private List<LevelSelectDesignerData.DesignerPath> RiversMeetingAt(string nodeId)
        => _data.paths.Where(p => p != null && p.nodeIds.Count >= 2 && p.nodeIds.Contains(nodeId))
                      .ToList();

    private bool AnotherRiverMeets(LevelSelectDesignerData.DesignerPath path, string nodeId)
        => _data.paths.Exists(p => p != null && p != path && p.nodeIds.Contains(nodeId));

    // ─────────────────────────────────────────────
    // JUNCTION
    // ─────────────────────────────────────────────

    /// <summary>Why the node can't be named a junction, or null when it can.</summary>
    private string WhyCannotMakeJunctionAt(string nodeId)
    {
        var rivers = RiversMeetingAt(nodeId);
        if (rivers.Count < 2)                              return "no other river meets it";
        if (rivers.Count > 2)                              return "three rivers meet";
        if (_data.junctions.Exists(j => j.nodeId == nodeId)) return "already a junction";
        if (_data.PoolAt(nodeId) != null)                  return "pool";

        var a = HowRiverMeets(rivers[0], nodeId);
        var b = HowRiverMeets(rivers[1], nodeId);
        if (a == RiverAtNode.Through && b == RiverAtNode.Through) return "the rivers cross";
        if (a != RiverAtNode.Through && b != RiverAtNode.Through) return "no river runs through";

        var branch = a == RiverAtNode.Through ? b : a;
        if (branch == RiverAtNode.Ends)                    return "the branch ends here";
        return null;
    }

    /// <summary>
    /// Names the node a junction: the river running through is the river, the one starting
    /// there its branch. Check <see cref="WhyCannotMakeJunctionAt"/> first.
    /// </summary>
    private void MakeJunctionAt(string nodeId)
    {
        var rivers = RiversMeetingAt(nodeId);
        var river  = rivers.First(p => HowRiverMeets(p, nodeId) == RiverAtNode.Through);
        var branch = rivers.First(p => p != river);

        var junc = new LevelSelectDesignerData.DesignerJunction
        {
            junctionId   = System.Guid.NewGuid().ToString(),
            nodeId       = nodeId,
            pathIds      = new List<string> { river.pathId, branch.pathId },
            riverPathId  = river.pathId,
            branchPathId = branch.pathId,
        };
        _data.junctions.Add(junc);
        RefreshJunctionLeadIns(junc);

        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node != null && node.type == LevelSelectDesignerData.NodeType.Waypoint)
            node.type = LevelSelectDesignerData.NodeType.JunctionSplit;

        Debug.Log($"[LevelSelectDesigner] Junction: '{branch.segmentId}' branches off " +
                  $"'{river.segmentId}'. Generate to rebuild the runs.");
    }

    // ─────────────────────────────────────────────
    // RIVER JOIN
    // ─────────────────────────────────────────────

    /// <summary>Why the node can't be named a river join, or null when it can.</summary>
    private string WhyCannotJoinRiversAt(string nodeId)
    {
        var rivers = RiversMeetingAt(nodeId);
        if (rivers.Count < 2)                                return "no other river meets it";
        if (rivers.Count > 2)                                return "three rivers meet";
        if (_data.RiverJoinAt(nodeId) != null)               return "already joined";
        if (_data.junctions.Exists(j => j.nodeId == nodeId)) return "it's a junction";
        if (_data.PoolAt(nodeId) != null)                    return "pool";
        if (_data.arenas.Exists(a => a.nodeId == nodeId))    return "arena";
        if (IsLockedNode(nodeId))                            return "lead-in";

        var a = HowRiverMeets(rivers[0], nodeId);
        var b = HowRiverMeets(rivers[1], nodeId);
        if (a == RiverAtNode.Through || b == RiverAtNode.Through) return "a river runs through";
        if (a == b) return a == RiverAtNode.Ends ? "both rivers end here" : "both rivers start here";
        return null;
    }

    /// <summary>Names the node a river join. Check <see cref="WhyCannotJoinRiversAt"/> first.</summary>
    private void JoinRiversAt(string nodeId)
    {
        if (_data.riverJoins == null) _data.riverJoins = new List<LevelSelectDesignerData.DesignerRiverJoin>();
        _data.riverJoins.Add(new LevelSelectDesignerData.DesignerRiverJoin { nodeId = nodeId });

        _data.RiverJoinPaths(nodeId, out var ending, out var starting);
        Debug.Log($"[LevelSelectDesigner] River join: '{ending?.segmentId}' runs on into " +
                  $"'{starting?.segmentId}'. Generate to rebuild the runs.");
    }

    // ─────────────────────────────────────────────
    // CARRYING ON PAST AN END
    // ─────────────────────────────────────────────

    /// <summary>
    /// Why a node can't be added off this end of the river, or null when it can. A free end
    /// always can; an end another river starts at can too, and the river then runs on through
    /// it with that one as its branch.
    /// </summary>
    private string WhyCannotCarryOnFrom(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null)                                      return "missing node";
        if (IsLockedNode(nodeId))                              return "lead-in";
        if (node.type == LevelSelectDesignerData.NodeType.ArenaEnd ||
            _data.arenas.Exists(a => a.nodeId == nodeId))     return "arena";
        if (node.type == LevelSelectDesignerData.NodeType.ShopEnd) return "shop";
        if (_data.PoolAt(nodeId) != null)                      return "pool";

        var others = RiversMeetingAt(nodeId).Where(p => p != path).ToList();
        if (others.Count == 0)
            return _data.junctions.Exists(j => j.nodeId == nodeId) ? "junction" : null;
        if (others.Count > 1)                                  return "three rivers meet";

        switch (HowRiverMeets(others[0], nodeId))
        {
            case RiverAtNode.Through: return "another river runs through";
            case RiverAtNode.Ends:    return "another river ends here";
        }
        return null;
    }

    /// <summary>
    /// After a node has gone in off a river's end: if another river started at that end, the
    /// river now runs through it — a river join there no longer holds, and it is named a
    /// junction instead.
    /// </summary>
    private void NameJunctionCarriedThrough(string oldEndNodeId)
    {
        if (_data.RiverJoinAt(oldEndNodeId) != null)
        {
            _data.riverJoins.RemoveAll(j => j == null || j.nodeId == oldEndNodeId);
            Debug.Log("[LevelSelectDesigner] River join removed — the river now runs on through.");
        }
        if (WhyCannotMakeJunctionAt(oldEndNodeId) == null) MakeJunctionAt(oldEndNodeId);
    }

    /// <summary>
    /// A river just drawn, or drawn on from: name what it meets at the nodes the drawing
    /// started and finished on — a junction where one runs through, a join where one ends and
    /// the other starts. Anything else is left for Generate to guess, as before.
    /// </summary>
    private void NameMeetingsAtDrawnEnds(string firstNodeId, string lastNodeId)
    {
        foreach (var nodeId in new[] { firstNodeId, lastNodeId }.Distinct())
        {
            if      (WhyCannotMakeJunctionAt(nodeId) == null) MakeJunctionAt(nodeId);
            else if (WhyCannotJoinRiversAt(nodeId)   == null) JoinRiversAt(nodeId);
        }
    }
}

#endif
