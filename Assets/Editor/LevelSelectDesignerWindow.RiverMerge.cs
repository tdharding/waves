using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Merge Rivers — two rivers that meet end to end become one.
///
/// Picked with River Select (Shift+click two rivers) or Select mode (Shift+click a node on each
/// river); a "Merge Rivers" box then shows at the top of the left panel. The river picked first
/// is kept — its name, colour and settings — and the other is folded into it, turned round when
/// both END (or both START) at the node they share. The node they met at becomes an ordinary
/// node in the river's middle, so Generate no longer caps either run there.
///
/// Which one turns round: a river whose direction is fixed — the main river (its head is where
/// the boat starts) or a branch leaving a junction — never does. Gates, outposts and shops keep
/// their places (fractions re-worked over the merged river, an outpost on a turned river swaps
/// bank), and junctions, archways and an arena carry over to the kept river.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private struct RiverMergePlan
    {
        public LevelSelectDesignerData.DesignerPath kept, other;
        public string meetNodeId;
        public bool   keptFirst;   // the kept river's stretch comes first in the merged river
        public bool   flipKept, flipOther;
    }

    // ─────────────────────────────────────────────
    // WHICH TWO RIVERS
    // ─────────────────────────────────────────────

    /// <summary>
    /// The rivers the picked items are on, in the order they were picked, plus whether the left
    /// panel should show the Merge box at all. River Select gives rivers whole; a node group
    /// gives each node's river — a node two rivers share can't say which, so it doesn't count.
    /// </summary>
    private bool RiversPickedForMerge(out List<LevelSelectDesignerData.DesignerPath> rivers, out string why)
    {
        rivers = new List<LevelSelectDesignerData.DesignerPath>();
        why    = null;

        if (_mode == DesignerMode.RiverSelect)
        {
            foreach (string id in _riverSelectIds)
            {
                var p = _data.paths.Find(x => x.pathId == id);
                if (p != null) rivers.Add(p);
            }
            if (rivers.Count < 2) return false;
            if (rivers.Count > 2) why = "pick just two";
            return true;
        }

        if (!NodeGroupActive) return false;
        if (JoinPicked) return false;   // two ends picked — the Join box is the one that shows

        // Only worth showing once the picked nodes are on more than one river between them.
        var touched = new HashSet<LevelSelectDesignerData.DesignerPath>();
        foreach (string nodeId in _multiNodeIds)
            foreach (var p in _data.paths)
                if (p != null && p.nodeIds.Contains(nodeId)) touched.Add(p);
        if (touched.Count < 2) return false;

        foreach (string nodeId in _multiNodeIds)
        {
            var on = _data.paths.Where(p => p != null && p.nodeIds.Contains(nodeId)).ToList();
            if (on.Count == 1 && !rivers.Contains(on[0])) rivers.Add(on[0]);
        }
        if (rivers.Count > 2)  why = "the nodes are on more than two rivers";
        else if (rivers.Count < 2)
            why = "pick a node on each river that no other river shares — or use River Select";
        return true;
    }

    // ─────────────────────────────────────────────
    // CAN THEY MERGE
    // ─────────────────────────────────────────────

    /// <summary>
    /// How the two rivers go together, or why they can't. <paramref name="kept"/> is the one
    /// whose name and settings the merged river keeps.
    /// </summary>
    private string PlanRiverMerge(LevelSelectDesignerData.DesignerPath kept,
                                  LevelSelectDesignerData.DesignerPath other,
                                  out RiverMergePlan plan)
    {
        plan = default;
        if (kept == null || other == null || kept == other) return "pick two rivers";
        if (kept.nodeIds.Count < 2 || other.nodeIds.Count < 2) return "a river has only one node";

        string k0 = kept.nodeIds[0],  kN = kept.nodeIds[kept.nodeIds.Count - 1];
        string o0 = other.nodeIds[0], oN = other.nodeIds[other.nodeIds.Count - 1];

        var shared = kept.nodeIds.Intersect(other.nodeIds).ToList();
        if (shared.Count == 0) return "they don't touch";

        var ends = shared.Where(id => (id == k0 || id == kN) && (id == o0 || id == oN)).ToList();
        if (ends.Count == 0)   return "a river runs through where they meet";
        if (ends.Count > 1)    return "they'd make a loop";
        if (shared.Count > 1)  return "they cross somewhere else too";

        string meet = ends[0];
        if (RiversMeetingAt(meet).Count > 2)                    return "three rivers meet";
        if (_data.PoolAt(meet) != null)                         return "they meet at a pool";
        if (_data.arenas.Exists(a => a.nodeId == meet) ||
            FindArenaForSecondaryNode(meet) != null)            return "they meet at an arena";
        if (kept.leadsToArena && other.leadsToArena)            return "both reach arenas";
        if (kept.segmentType  == LevelSelectDesignerData.SegmentType.MainRiver &&
            other.segmentType == LevelSelectDesignerData.SegmentType.MainRiver)
                                                                return "both are main rivers";

        plan.kept       = kept;
        plan.other      = other;
        plan.meetNodeId = meet;
        return OrientRivers(ref plan, meet == kN, meet == o0);
    }

    /// <summary>
    /// Which river comes first and which (if either) turns round, given which end of each the
    /// two meet at — a shared node for Merge, the two picked ends for Join.
    /// </summary>
    private string OrientRivers(ref RiverMergePlan plan, bool keptEndsHere, bool otherStartsHere)
    {
        var kept  = plan.kept;
        var other = plan.other;

        // One ends where the other starts: straight on, nothing turns round.
        if (keptEndsHere == otherStartsHere)
        {
            plan.keptFirst = keptEndsHere;
            return null;
        }

        // Both end here, or both start here — one has to turn round.
        bool keptFixed  = DirectionFixed(kept);
        bool otherFixed = DirectionFixed(other);
        if (keptFixed && otherFixed) return "neither can turn round (main river / junction branch)";

        if (!otherFixed) { plan.flipOther = true; plan.keptFirst = keptEndsHere; }
        else             { plan.flipKept  = true; plan.keptFirst = !keptEndsHere; }
        return null;
    }

    /// A river that can't be turned round: the main river runs from where the boat starts, and a
    /// branch has to START at its junction.
    private bool DirectionFixed(LevelSelectDesignerData.DesignerPath p)
    {
        if (p.segmentType == LevelSelectDesignerData.SegmentType.MainRiver) return true;
        string start = p.nodeIds[0];
        return _data.junctions.Exists(j => j != null && j.nodeId == start && j.branchPathId == p.pathId);
    }

    // ─────────────────────────────────────────────
    // MERGE
    // ─────────────────────────────────────────────

    private void MergeRivers(RiverMergePlan plan)
    {
        var kept  = plan.kept;
        var other = plan.other;
        string meet = plan.meetNodeId;

        var keptIds  = new List<string>(kept.nodeIds);
        var otherIds = new List<string>(other.nodeIds);
        if (plan.flipKept)  keptIds.Reverse();
        if (plan.flipOther) otherIds.Reverse();

        // Merge shares the meeting node, so the second river's copy of it is dropped. Join has
        // no meeting node — its two ends are linked by one new straight stretch.
        int gap  = meet == null ? 1 : 0;
        int skip = 1 - gap;
        var merged = plan.keptFirst
                   ? keptIds.Concat(otherIds.Skip(skip)).ToList()
                   : otherIds.Concat(keptIds.Skip(skip)).ToList();

        // Where each river's arena stood, before the node list changes under it.
        string keptArenaNode  = kept.leadsToArena  ? (kept.arenaIsAtEnd  ? kept.nodeIds[kept.nodeIds.Count - 1]   : kept.nodeIds[0])  : null;
        string otherArenaNode = other.leadsToArena ? (other.arenaIsAtEnd ? other.nodeIds[other.nodeIds.Count - 1] : other.nodeIds[0]) : null;

        // The side flags say where a river LEAVES another one — they belong to whichever start
        // the merged river now has, and to nothing if that start was a meeting end.
        string newStart = merged[0];
        if (newStart == other.nodeIds[0])
        {
            kept.isLeftPath             = other.isLeftPath;
            kept.isRightPath            = other.isRightPath;
            kept.tJunctionBidirectional = other.tJunctionBidirectional;
        }
        else if (newStart != kept.nodeIds[0])
        {
            kept.isLeftPath = kept.isRightPath = kept.tJunctionBidirectional = false;
        }

        // Fractions along a river are per stretch: each river's stretches land at its place in
        // the merged one, read the other way if it turned round.
        int keptSegs  = kept.nodeIds.Count  - 1;
        int otherSegs = other.nodeIds.Count - 1;
        int total     = keptSegs + otherSegs + gap;
        float Place(float t, bool flip, int segs, bool first)
        {
            t = Mathf.Clamp01(t);
            if (flip) t = 1f - t;
            float s = t * segs + (first ? 0 : total - segs);
            return s / total;
        }
        bool Remap(ref string pid, ref float t)
        {
            if (pid == kept.pathId)  { t = Place(t, plan.flipKept,  keptSegs,  plan.keptFirst);  return plan.flipKept; }
            if (pid == other.pathId) { pid = kept.pathId; t = Place(t, plan.flipOther, otherSegs, !plan.keptFirst); return plan.flipOther; }
            return false;
        }
        foreach (var o in _data.obstacles) Remap(ref o.pathId, ref o.pathT);
        foreach (var s in _data.shops)     Remap(ref s.pathId, ref s.pathT);
        foreach (var o in _data.outposts)
        {
            // An outpost's bank is read looking down the river the way it was drawn.
            if (!Remap(ref o.pathId, ref o.pathT)) continue;
            if      (o.side == LevelSelectDesignerData.OutpostSide.Left)  o.side = LevelSelectDesignerData.OutpostSide.Right;
            else if (o.side == LevelSelectDesignerData.OutpostSide.Right) o.side = LevelSelectDesignerData.OutpostSide.Left;
        }

        kept.nodeIds = merged;

        // One main river only, and it stays the main river whichever was picked first.
        if (other.segmentType == LevelSelectDesignerData.SegmentType.MainRiver)
            kept.segmentType = LevelSelectDesignerData.SegmentType.MainRiver;

        string arenaNode = keptArenaNode ?? otherArenaNode;
        if (arenaNode != null)
        {
            if (keptArenaNode == null) kept.arenaGridDataGuid = other.arenaGridDataGuid;
            kept.leadsToArena = true;
            kept.arenaIsAtEnd = merged[merged.Count - 1] == arenaNode;
        }

        if (other.influenceCancelledNodes != null)
        {
            if (kept.influenceCancelledNodes == null) kept.influenceCancelledNodes = new List<string>();
            foreach (string id in other.influenceCancelledNodes)
                if (!kept.influenceCancelledNodes.Contains(id)) kept.influenceCancelledNodes.Add(id);
        }

        // The meeting node is now just a node in the river's middle — nothing meets there.
        if (meet != null)
        {
            _data.junctions.RemoveAll(j => j == null || j.nodeId == meet);
            _data.riverJoins?.RemoveAll(j => j == null || j.nodeId == meet);
            var meetNode = _data.nodes.Find(n => n.id == meet);
            if (meetNode != null && meetNode.type == LevelSelectDesignerData.NodeType.JunctionSplit)
                meetNode.type = LevelSelectDesignerData.NodeType.Waypoint;
        }

        foreach (var j in _data.junctions)
        {
            if (j.riverPathId  == other.pathId) j.riverPathId  = kept.pathId;
            if (j.branchPathId == other.pathId) j.branchPathId = kept.pathId;
            for (int k = 0; k < j.pathIds.Count; k++)
                if (j.pathIds[k] == other.pathId) j.pathIds[k] = kept.pathId;
            j.pathIds = j.pathIds.Distinct().ToList();
        }

        if (_data.archways != null)
        {
            // An archway is keyed by node + river; where both rivers had one on a node, the kept one stays.
            var keptArches = new HashSet<string>(_data.archways.Where(a => a != null && a.pathId == kept.pathId)
                                                               .Select(a => a.nodeId));
            _data.archways.RemoveAll(a => a != null && a.pathId == other.pathId && keptArches.Contains(a.nodeId));
            foreach (var a in _data.archways)
                if (a != null && a.pathId == other.pathId) a.pathId = kept.pathId;
        }

        _data.paths.Remove(other);

        Debug.Log($"[LevelSelectDesigner] {(meet == null ? "Joined" : "Merged")} '{other.segmentId}' into '{kept.segmentId}'" +
                  (plan.flipOther ? $" ('{other.segmentId}' turned round)" : "") +
                  (plan.flipKept  ? $" ('{kept.segmentId}' turned round)"  : "") +
                  $" — {merged.Count} nodes. Generate to rebuild the runs.");
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>
    /// The box at the top of the left panel when two rivers are picked: what would be kept, and
    /// the Merge button — greyed with the reason when they can't go together.
    /// </summary>
    private void DrawMergeRiversPanel()
    {
        if (!RiversPickedForMerge(out var rivers, out string why)) return;

        RiverMergePlan plan = default;
        if (why == null) why = PlanRiverMerge(rivers[0], rivers[1], out plan);

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(
            rivers.Count >= 2 ? $"{rivers.Count} rivers selected" : "Several rivers selected",
            EditorStyles.boldLabel);

        if (why == null)
            EditorGUILayout.LabelField(
                $"Keeps '{PathDisplayName(plan.kept)}', folds in '{PathDisplayName(plan.other)}'" +
                (plan.flipOther ? " (turned round)" : plan.flipKept ? $" ('{plan.kept.segmentId}' turned round)" : ""),
                EditorStyles.wordWrappedMiniLabel);

        using (new EditorGUI.DisabledScope(why != null))
        {
            var label = new GUIContent(why == null ? "Merge Rivers" : $"Merge Rivers ({why})",
                "Make the two rivers one, where they meet end to end. The river picked first keeps " +
                "its name and settings; the other is folded into it. Generate to see it.");
            if (GUILayout.Button(label))
            {
                Undo.RecordObject(_data, "Merge Rivers");
                MergeRivers(plan);
                if (_mode == DesignerMode.RiverSelect)
                {
                    _riverSelectIds.Remove(plan.other.pathId);
                    if (!_riverSelectIds.Contains(plan.kept.pathId)) _riverSelectIds.Add(plan.kept.pathId);
                }
                if (_selectedPathId == plan.other.pathId) _selectedPathId = plan.kept.pathId;
                MarkDirty();
                Repaint();
                GUIUtility.ExitGUI();
            }
        }
        EditorGUILayout.EndVertical();
    }
}

#endif
