using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Arena lead-ins — the pool lead-in pattern carried over to arena entrances.
///
/// Refresh Lead-ins puts every entrance of an arena on a compass point: the primary entrance on
/// the one nearest the way its river comes in, the others on the free points nearest where they
/// sat. From then on the entrances are locked there, and every entrance a river arrives at gets
/// a lead-in node straight out from it by the designer's Lead-in Distance, at the arena's
/// height. Neither an entrance nor its lead-in can be moved on its own — moving the arena node
/// carries them all.
///
///                  N
///                  o  lead-in
///                  |
///           W o---( )---o E        the arena centre sits past the primary entrance,
///                  |               along the way its river arrives
///                  o
///                  S  primary
///
/// The primary entrance's lead-in is the node its river arrives from, so the arrival direction
/// every other part of the designer reads off the last two nodes of that river IS the compass
/// point — nothing else has to be told about it.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private bool IsLeadInNode(string nodeId)
        => _data.PoolOfLeadIn(nodeId) != null || _data.ArenaOfLeadIn(nodeId) != null;

    // Lead-ins, and entrances of an arena whose entrances are on compass points.
    private bool IsLockedNode(string nodeId)
        => IsLeadInNode(nodeId) || FindArenaForSecondaryNode(nodeId)?.compassEntrances == true;

    /// <summary>
    /// Puts the arena's entrances on compass points and gives each one a river arrives at a
    /// lead-in. Returns false, changing nothing, when the arena has more than four entrances.
    /// </summary>
    private bool RefreshArenaLeadIns(LevelSelectDesignerData.DesignerArena arena)
    {
        var primary = _data.nodes.Find(n => n.id == arena.nodeId);
        if (primary == null) return false;

        if (arena.secondaryEntrances.Count > 3)
        {
            Debug.LogWarning($"[LevelSelectDesigner] Arena {arena.nodeId} has " +
                             $"{arena.secondaryEntrances.Count + 1} entrances — only four fit on " +
                             "compass points. Lead-ins not refreshed.");
            return false;
        }

        arena.leadIns ??= new List<LevelSelectDesignerData.ArenaLeadIn>();
        PruneArenaLeadIns(arena);

        Vector3 centre = GetTrueArenaCenter(arena);

        // The primary entrance faces back the way its river comes in — read past its lead-in
        // when it has one. With no river it keeps the side it already faces.
        Vector3 side    = primary.worldPosition - centre;
        var     arrival = ArenaArrivalRiver(arena);
        if (arrival != null)
        {
            string prevId = arrival.nodeIds[arrival.nodeIds.Count - 2];
            string from   = arena.leadIns.Exists(l => l.nodeId == prevId)
                          ? NodeBeyond(arrival, arena.nodeId, prevId) ?? prevId
                          : prevId;
            side = _data.NodeWorldPosition(from) - primary.worldPosition;
        }

        var taken = new HashSet<LevelSelectDesignerData.Compass>();
        arena.compass = NearestFreeCompasses(new List<Vector3> { side }, taken)[0].Value;

        var bearings = arena.secondaryEntrances
            .Select(e => _data.NodeWorldPosition(e.nodeId) - centre)
            .ToList();
        var points = NearestFreeCompasses(bearings, taken);
        for (int i = 0; i < arena.secondaryEntrances.Count; i++)
            arena.secondaryEntrances[i].compass = points[i].Value;

        arena.compassEntrances = true;
        PlaceArenaLeadIns(arena);

        // One lead-in per entrance with a river. The primary's has to go on the river that
        // arrives at the arena, since that is the river its direction is read from.
        var kept      = new List<LevelSelectDesignerData.ArenaLeadIn>();
        var entrances = new List<(string nodeId, LevelSelectDesignerData.Compass compass)>
            { (arena.nodeId, arena.compass) };
        entrances.AddRange(arena.secondaryEntrances.Select(e => (e.nodeId, e.compass)));

        foreach (var (entranceId, compass) in entrances)
        {
            var existing = arena.leadIns.Find(l => l.entranceNodeId == entranceId);
            if (existing != null) { kept.Add(existing); continue; }

            LevelSelectDesignerData.DesignerPath path;
            string neighbourId;
            if (entranceId == arena.nodeId)
            {
                path = arrival;
                if (path == null) continue;
                neighbourId = path.nodeIds[path.nodeIds.Count - 2];
            }
            else
            {
                var rivers = RiversAtNode(entranceId);
                if (rivers.Count == 0) continue;
                (path, neighbourId) = rivers[0];
                if (rivers.Count > 1)
                    Debug.LogWarning($"[LevelSelectDesigner] Arena {arena.nodeId}: {rivers.Count} rivers " +
                                     $"meet one entrance — only the first gets a lead-in.");
            }

            Vector3 entrance = _data.NodeWorldPosition(entranceId);
            var node = AddNode(entrance + LevelSelectDesignerData.CompassDirection(compass) *
                                          Mathf.Max(0f, _data.arenaLeadInDistance),
                               LevelSelectDesignerData.NodeType.Waypoint);
            InsertNodeBetween(path, entranceId, neighbourId, node);
            kept.Add(new LevelSelectDesignerData.ArenaLeadIn { nodeId = node.id, entranceNodeId = entranceId });
        }

        foreach (var leadIn in arena.leadIns)
            if (!kept.Contains(leadIn))
                DeleteNode(leadIn.nodeId);

        arena.leadIns = kept;
        PlaceArenaLeadIns(arena);
        return true;
    }

    /// <summary>Deletes every lead-in the arena placed — all of them, or one entrance's.</summary>
    private void RemoveArenaLeadIns(LevelSelectDesignerData.DesignerArena arena, string entranceNodeId = null)
    {
        if (arena?.leadIns == null) return;

        foreach (var leadIn in arena.leadIns.ToList())
        {
            if (entranceNodeId != null && leadIn.entranceNodeId != entranceNodeId) continue;
            arena.leadIns.Remove(leadIn);
            DeleteNode(leadIn.nodeId);
        }
    }

    /// <summary>Keeps compass entrances and lead-ins where their arena puts them. Run on layout.</summary>
    private void SyncArenaLeadIns()
    {
        if (_data?.arenas == null) return;

        bool changed = false;
        foreach (var arena in _data.arenas)
        {
            if (arena == null || !arena.compassEntrances) continue;
            if (arena.leadIns != null) changed |= PruneArenaLeadIns(arena);
            changed |= PlaceArenaLeadIns(arena);
        }

        if (changed) MarkDirty();
    }

    // Stands each entrance on its compass point and each lead-in straight out from its
    // entrance, all at the arena node's height. True when anything moved.
    private bool PlaceArenaLeadIns(LevelSelectDesignerData.DesignerArena arena)
    {
        var primary = _data.nodes.Find(n => n.id == arena.nodeId);
        if (primary == null) return false;

        bool changed = false;

        // The centre sits past the primary entrance along the way its river arrives — which,
        // with the lead-in in place, is straight in from the primary's compass point.
        Vector3 centre = primary.worldPosition
                       - LevelSelectDesignerData.CompassDirection(arena.compass) * _data.arenaHeadOffset;
        float   radius = GetArenaRadius(arena);

        foreach (var entrance in arena.secondaryEntrances)
        {
            var node = _data.nodes.Find(n => n.id == entrance.nodeId);
            if (node == null) continue;
            changed |= MoveNode(node, centre + LevelSelectDesignerData.CompassDirection(entrance.compass) * radius);
        }

        if (arena.leadIns == null) return changed;

        float distance = Mathf.Max(0f, _data.arenaLeadInDistance);
        foreach (var leadIn in arena.leadIns)
        {
            var node = _data.nodes.Find(n => n.id == leadIn.nodeId);
            if (node == null) continue;

            LevelSelectDesignerData.Compass compass;
            if (leadIn.entranceNodeId == arena.nodeId) compass = arena.compass;
            else
            {
                var entrance = arena.secondaryEntrances.Find(e => e.nodeId == leadIn.entranceNodeId);
                if (entrance == null) continue;
                compass = entrance.compass;
            }

            changed |= MoveNode(node, _data.NodeWorldPosition(leadIn.entranceNodeId)
                                    + LevelSelectDesignerData.CompassDirection(compass) * distance);
        }

        return changed;
    }

    private static bool MoveNode(LevelSelectDesignerData.DesignerNode node, Vector3 want)
    {
        if ((node.worldPosition - want).sqrMagnitude < 1e-10f) return false;
        node.worldPosition = want;
        return true;
    }

    // Forgets lead-ins whose node or entrance is gone, or that no longer sit next to their
    // entrance on any river. True when any were forgotten.
    private bool PruneArenaLeadIns(LevelSelectDesignerData.DesignerArena arena)
    {
        int before = arena.leadIns.Count;
        arena.leadIns.RemoveAll(l => l == null
            || _data.nodes.Find(n => n.id == l.nodeId) == null
            || (l.entranceNodeId != arena.nodeId && !arena.secondaryEntrances.Exists(e => e.nodeId == l.entranceNodeId))
            || !RiversAtNode(l.entranceNodeId).Exists(r => r.neighbourId == l.nodeId));
        return arena.leadIns.Count != before;
    }

    // The river that arrives at the arena — the one its direction is read from.
    private LevelSelectDesignerData.DesignerPath ArenaArrivalRiver(LevelSelectDesignerData.DesignerArena arena)
        => _data.paths.FirstOrDefault(p => p.leadsToArena &&
                                           p.nodeIds.Count >= 2 &&
                                           p.nodeIds[p.nodeIds.Count - 1] == arena.nodeId);

    // Every river touching a node, paired with the node next to it — twice for a river drawn
    // through it.
    private List<(LevelSelectDesignerData.DesignerPath path, string neighbourId)> RiversAtNode(string nodeId)
    {
        var rivers = new List<(LevelSelectDesignerData.DesignerPath, string)>();
        foreach (var path in _data.paths)
        {
            int n = path.nodeIds.Count;
            for (int i = 0; i < n; i++)
            {
                if (path.nodeIds[i] != nodeId) continue;
                if (i > 0)     rivers.Add((path, path.nodeIds[i - 1]));
                if (i < n - 1) rivers.Add((path, path.nodeIds[i + 1]));
            }
        }
        return rivers;
    }

    /// <summary>
    /// Gives each bearing the free compass point nearest it, closest pairs first, each point used
    /// once. Points given out are added to <paramref name="taken"/>. A bearing left over when the
    /// points run out comes back null.
    /// </summary>
    private static List<LevelSelectDesignerData.Compass?> NearestFreeCompasses(
        List<Vector3> bearings, HashSet<LevelSelectDesignerData.Compass> taken)
    {
        var pairs = new List<(int index, LevelSelectDesignerData.Compass compass, float angle)>();
        for (int i = 0; i < bearings.Count; i++)
        {
            Vector3 b = bearings[i];
            b.y = 0f;
            if (b.sqrMagnitude < 1e-8f) b = Vector3.forward;

            foreach (LevelSelectDesignerData.Compass c in System.Enum.GetValues(typeof(LevelSelectDesignerData.Compass)))
                pairs.Add((i, c, Vector3.Angle(b, LevelSelectDesignerData.CompassDirection(c))));
        }
        pairs.Sort((a, b) => a.angle.CompareTo(b.angle));

        var result = new List<LevelSelectDesignerData.Compass?>(new LevelSelectDesignerData.Compass?[bearings.Count]);
        foreach (var (i, c, _) in pairs)
        {
            if (result[i] != null || taken.Contains(c)) continue;
            result[i] = c;
            taken.Add(c);
        }
        return result;
    }
}

#endif
