using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Join — two loose ends picked in Select mode (Shift+click), linked by one straight stretch.
///
/// Two river ends: the rivers become one, the same way Merge Rivers makes them one — the river
/// picked first is kept, and the other turns round when it has to (never the main river or a
/// junction branch). A river end and a pool: the river is carried straight on into the pool and
/// finishes there, and the pool's Refresh Lead-ins is run so it arrives on a compass point.
///
/// A loose end is a river's first or last node that no other river meets. Joining onto the
/// middle of a river is a junction — Make Junction Here does that.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    /// Two nodes picked, each a river's end or a pool — the Join box shows (greyed if it can't).
    private bool JoinPicked
        => NodeGroupActive && _multiNodeIds.Count == 2 && _multiNodeIds.All(IsEndOrPool);

    private bool IsEndOrPool(string nodeId)
        => _data.PoolAt(nodeId) != null ||
           _data.paths.Exists(p => p != null && p.nodeIds.Count >= 2 &&
                                   (p.nodeIds[0] == nodeId || p.nodeIds[p.nodeIds.Count - 1] == nodeId));

    /// <summary>The one river a loose end belongs to, or why the node isn't a loose end.</summary>
    private string LooseRiverEnd(string nodeId, out LevelSelectDesignerData.DesignerPath river)
    {
        river = null;
        var on = RiversMeetingAt(nodeId);
        if (on.Count == 0) return "not on a river";
        if (on.Count > 1)  return "another river already meets it — try Merge";

        var p = on[0];
        if (p.nodeIds[0] != nodeId && p.nodeIds[p.nodeIds.Count - 1] != nodeId) return "not a river end";
        if (_data.junctions.Exists(j => j != null && j.nodeId == nodeId))     return "junction";
        if (_data.arenas.Exists(a => a.nodeId == nodeId) ||
            FindArenaForSecondaryNode(nodeId) != null)                        return "arena";
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node != null && node.type == LevelSelectDesignerData.NodeType.ShopEnd) return "shop";
        if (IsLockedNode(nodeId))                                             return "lead-in";

        river = p;
        return null;
    }

    private struct JoinPlan
    {
        public RiverMergePlan rivers;      // two river ends
        public LevelSelectDesignerData.DesignerPath poolRiver;   // a river end and a pool
        public string endNodeId, poolNodeId;
    }

    /// <summary>How the two picked nodes join, or why they can't. The first is the one kept.</summary>
    private string PlanJoin(string firstId, string secondId, out JoinPlan plan)
    {
        plan = default;
        bool firstPool  = _data.PoolAt(firstId)  != null;
        bool secondPool = _data.PoolAt(secondId) != null;
        if (firstPool && secondPool) return "both are pools";

        if (firstPool || secondPool)
        {
            string endId  = firstPool ? secondId : firstId;
            string poolId = firstPool ? firstId  : secondId;
            string why    = LooseRiverEnd(endId, out var river);
            if (why != null) return why;
            if (river.nodeIds.Contains(poolId)) return "the river already reaches that pool";

            plan.poolRiver  = river;
            plan.endNodeId  = endId;
            plan.poolNodeId = poolId;
            return null;
        }

        string whyA = LooseRiverEnd(firstId,  out var a);
        if (whyA != null) return whyA;
        string whyB = LooseRiverEnd(secondId, out var b);
        if (whyB != null) return whyB;
        if (a == b)                                    return "same river — would loop";
        if (a.nodeIds.Intersect(b.nodeIds).Any())      return "they already meet elsewhere";
        if (a.leadsToArena && b.leadsToArena)          return "both reach arenas";
        if (a.segmentType == LevelSelectDesignerData.SegmentType.MainRiver &&
            b.segmentType == LevelSelectDesignerData.SegmentType.MainRiver)
                                                       return "both are main rivers";

        plan.rivers.kept  = a;
        plan.rivers.other = b;
        plan.rivers.meetNodeId = null;   // no shared node — a new stretch links the two ends
        return OrientRivers(ref plan.rivers,
                            firstId  == a.nodeIds[a.nodeIds.Count - 1],
                            secondId == b.nodeIds[0]);
    }

    /// <summary>Carries the river straight on into the pool, then puts it on a compass point.</summary>
    private void JoinRiverToPool(JoinPlan plan)
    {
        var  river   = plan.poolRiver;
        int  segs    = river.nodeIds.Count - 1;
        bool atStart = river.nodeIds[0] == plan.endNodeId;

        // Fractions along the river are per stretch; the new stretch goes on the end it joins at.
        void Remap(ref string pid, ref float t)
        {
            if (pid != river.pathId) return;
            float s = Mathf.Clamp01(t) * segs + (atStart ? 1 : 0);
            t = s / (segs + 1);
        }
        foreach (var o in _data.obstacles) Remap(ref o.pathId, ref o.pathT);
        foreach (var o in _data.outposts)  Remap(ref o.pathId, ref o.pathT);
        foreach (var s in _data.shops)     Remap(ref s.pathId, ref s.pathT);

        if (atStart) river.nodeIds.Insert(0, plan.poolNodeId);
        else         river.nodeIds.Add(plan.poolNodeId);

        var pool   = _data.PoolAt(plan.poolNodeId);
        int missed = RefreshPoolLeadIns(pool);
        _consoleStatusMsg = missed > 0
            ? $"Joined '{river.segmentId}' to the pool — {missed} river(s) left without a lead-in (four compass points)."
            : $"Joined '{river.segmentId}' to the pool.";

        Debug.Log($"[LevelSelectDesigner] Joined '{river.segmentId}' to pool {plan.poolNodeId} " +
                  $"({(atStart ? "starts" : "ends")} there). Generate to rebuild the runs.");
    }

    /// <summary>
    /// The box at the top of the left panel when two loose ends (or an end and a pool) are
    /// picked: what the join will do, and the Join button — greyed with the reason when it can't.
    /// </summary>
    private void DrawJoinPanel()
    {
        if (!JoinPicked) return;

        string firstId  = _multiNodeIds[0];
        string secondId = _multiNodeIds[1];
        string why      = PlanJoin(firstId, secondId, out var plan);

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("2 ends selected", EditorStyles.boldLabel);

        if (why == null)
        {
            string what;
            if (plan.poolRiver != null)
                what = $"Carries '{PathDisplayName(plan.poolRiver)}' on into the pool, and refreshes its lead-ins.";
            else
                what = $"Links the ends with a straight stretch: keeps '{PathDisplayName(plan.rivers.kept)}', " +
                       $"folds in '{PathDisplayName(plan.rivers.other)}'" +
                       (plan.rivers.flipOther ? " (turned round)" :
                        plan.rivers.flipKept  ? $" ('{plan.rivers.kept.segmentId}' turned round)" : "") + ".";
            EditorGUILayout.LabelField(what, EditorStyles.wordWrappedMiniLabel);
        }

        using (new EditorGUI.DisabledScope(why != null))
        {
            var label = new GUIContent(why == null ? "Join" : $"Join ({why})",
                "Link the two picked ends with one straight stretch — two river ends become one " +
                "river (the first picked is kept), a river end and a pool carries the river into " +
                "the pool. Generate to see it.");
            if (GUILayout.Button(label))
            {
                Undo.RecordObject(_data, "Join");
                if (plan.poolRiver != null)
                {
                    JoinRiverToPool(plan);
                    _selectedPathId = plan.poolRiver.pathId;
                }
                else
                {
                    MergeRivers(plan.rivers);
                    _selectedPathId = plan.rivers.kept.pathId;
                }
                MarkDirty();
                Repaint();
                GUIUtility.ExitGUI();
            }
        }
        EditorGUILayout.EndVertical();
    }
}

#endif
