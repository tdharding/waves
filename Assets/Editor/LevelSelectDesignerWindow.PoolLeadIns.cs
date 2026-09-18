using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Pool lead-ins — a node a pool places on one of its compass points (north, east, south, west)
/// for each river that meets it, so the river comes in square on to the rim and at the pool's
/// height.
///
/// A lead-in is locked to its pool. Where it stands is worked out from the pool every time the
/// designer draws, never authored: straight out from the pool's centre along its compass point,
/// past the point the river is cut off at by the designer's Lead-in Distance, at the pool node's
/// height. Moving the pool, changing its height or radius, or changing the distance all carry the
/// lead-ins with them. They cannot be dragged, have their height edited, or be deleted on their
/// own; removing the pool deletes them.
///
///                 N
///                 o
///                 |
///          W o---(P)---o E
///                 |
///                 o
///                 S
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // How far out from its pool's centre a lead-in stands.
    private float PoolLeadInRadius(LevelSelectDesignerData.DesignerPool pool)
    {
        var profile = _data.ProfileFor(_data.PoolRiverName(pool));
        return RiverMeshBuilder.PoolEdgeDistance(profile, _data.PoolShapeFor(pool).poolRadius, MeshEdge)
             + Mathf.Max(0f, _data.poolLeadInDistance);
    }

    /// <summary>
    /// Gives every river meeting the pool a lead-in on a compass point: the one nearest the way
    /// the river already comes in, or the next nearest when another river is closer to it. A
    /// river that already has a lead-in keeps its node and may move to another point. A pool has
    /// four points, so any river past four is left without one — returns how many were.
    /// </summary>
    private int RefreshPoolLeadIns(LevelSelectDesignerData.DesignerPool pool)
    {
        var poolNode = _data.nodes.Find(n => n.id == pool.nodeId);
        if (poolNode == null) return 0;

        pool.leadIns ??= new List<LevelSelectDesignerData.PoolLeadIn>();
        PrunePoolLeadIns(pool);

        Vector3 centre = poolNode.worldPosition;
        var     mouths = _data.PathsAtPool(pool);

        // Which way each river comes in — read past its lead-in when it has one, since the
        // lead-in sits wherever it was last put rather than where the river is heading from.
        var bearings = new List<Vector3>();
        foreach (var (path, neighbourId) in mouths)
        {
            string from = neighbourId;
            if (pool.leadIns.Exists(l => l.nodeId == neighbourId))
                from = NodeBeyond(path, pool.nodeId, neighbourId) ?? neighbourId;

            Vector3 d = _data.NodeWorldPosition(from) - centre;
            d.y = 0f;
            bearings.Add(d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward);
        }

        var points = NearestFreeCompasses(bearings, new HashSet<LevelSelectDesignerData.Compass>());

        float radius = PoolLeadInRadius(pool);
        var   kept   = new List<LevelSelectDesignerData.PoolLeadIn>();
        int   missed = 0;

        for (int m = 0; m < mouths.Count; m++)
        {
            var (path, neighbourId) = mouths[m];
            var existing = pool.leadIns.Find(l => l.nodeId == neighbourId);

            if (points[m] == null)
            {
                missed++;
                continue;
            }
            var compass = points[m].Value;

            if (existing != null)
            {
                existing.compass = compass;
                kept.Add(existing);
                continue;
            }

            var node = AddNode(centre + LevelSelectDesignerData.CompassDirection(compass) * radius,
                               LevelSelectDesignerData.NodeType.Waypoint);
            InsertNodeBetween(path, pool.nodeId, neighbourId, node);
            kept.Add(new LevelSelectDesignerData.PoolLeadIn { nodeId = node.id, compass = compass });
        }

        // A lead-in whose river went without a point this time round is taken out of the river.
        foreach (var leadIn in pool.leadIns)
            if (!kept.Contains(leadIn))
                DeleteNode(leadIn.nodeId);

        pool.leadIns = kept;
        PlacePoolLeadIns(pool);

        if (missed > 0)
            Debug.LogWarning($"[LevelSelectDesigner] Pool {pool.nodeId}: {mouths.Count} rivers meet it " +
                             $"but a pool has four compass points — {missed} left without a lead-in.");

        return missed;
    }

    /// <summary>Deletes every lead-in the pool placed, closing each river back up to the pool.</summary>
    private void RemovePoolLeadIns(LevelSelectDesignerData.DesignerPool pool)
    {
        if (pool?.leadIns == null) return;

        foreach (var leadIn in new List<LevelSelectDesignerData.PoolLeadIn>(pool.leadIns))
            DeleteNode(leadIn.nodeId);

        pool.leadIns.Clear();
    }

    /// <summary>
    /// Keeps every lead-in where its pool puts it. Run as the designer lays out, so whatever
    /// moved the pool — a drag, a height edit, a radius, the lead-in distance, an undo — the
    /// lead-ins follow it.
    /// </summary>
    private void SyncPoolLeadIns()
    {
        if (_data?.pools == null) return;

        bool changed = false;
        foreach (var pool in _data.pools)
        {
            if (pool?.leadIns == null || pool.leadIns.Count == 0) continue;
            changed |= PrunePoolLeadIns(pool);
            changed |= PlacePoolLeadIns(pool);
        }

        if (changed) MarkDirty();
    }

    // Stands each lead-in on its compass point at the pool's height. True when any moved.
    private bool PlacePoolLeadIns(LevelSelectDesignerData.DesignerPool pool)
    {
        var poolNode = _data.nodes.Find(n => n.id == pool.nodeId);
        if (poolNode == null) return false;

        float radius  = PoolLeadInRadius(pool);
        bool  changed = false;

        foreach (var leadIn in pool.leadIns)
        {
            var node = _data.nodes.Find(n => n.id == leadIn.nodeId);
            if (node == null) continue;

            Vector3 want = poolNode.worldPosition
                         + LevelSelectDesignerData.CompassDirection(leadIn.compass) * radius;
            if ((node.worldPosition - want).sqrMagnitude < 1e-10f) continue;

            node.worldPosition = want;
            changed = true;
        }

        return changed;
    }

    // Forgets lead-ins whose node is gone or no longer sits next to the pool on any river —
    // the node itself is left alone. True when any were forgotten.
    private bool PrunePoolLeadIns(LevelSelectDesignerData.DesignerPool pool)
    {
        int before = pool.leadIns.Count;
        pool.leadIns.RemoveAll(l => l == null
                                 || _data.nodes.Find(n => n.id == l.nodeId) == null
                                 || !_data.PathsAtPool(pool).Exists(a => a.neighbourId == l.nodeId));
        return pool.leadIns.Count != before;
    }

    // The node past `neighbourId` on the far side from the pool, or null when the river ends there.
    private static string NodeBeyond(LevelSelectDesignerData.DesignerPath path, string poolId, string neighbourId)
    {
        var ids = path.nodeIds;
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] != poolId) continue;
            if (i + 1 < ids.Count && ids[i + 1] == neighbourId) return i + 2 < ids.Count ? ids[i + 2] : null;
            if (i - 1 >= 0        && ids[i - 1] == neighbourId) return i - 2 >= 0        ? ids[i - 2] : null;
        }
        return null;
    }

    /// <summary>
    /// Puts a node into a river between two neighbouring nodes. Gates, outposts and shops on that
    /// river are placed by a fraction along its nodes, so adding one would slide them all along;
    /// their fractions are re-worked here so each stays on the stretch it was on.
    /// </summary>
    private void InsertNodeBetween(LevelSelectDesignerData.DesignerPath path, string a, string b,
                                   LevelSelectDesignerData.DesignerNode node)
    {
        var ids = path.nodeIds;
        for (int i = 0; i + 1 < ids.Count; i++)
        {
            bool pair = (ids[i] == a && ids[i + 1] == b) || (ids[i] == b && ids[i + 1] == a);
            if (!pair) continue;

            Vector3 p0 = _data.NodeWorldPosition(ids[i]);
            Vector3 p1 = _data.NodeWorldPosition(ids[i + 1]);
            float   d0 = Vector3.Distance(p0, node.worldPosition);
            float   d1 = Vector3.Distance(node.worldPosition, p1);
            float   split    = d0 + d1 > 1e-5f ? Mathf.Clamp(d0 / (d0 + d1), 0.01f, 0.99f) : 0.5f;
            int     segments = ids.Count - 1;
            int     at       = i;

            float Remap(float t)
            {
                float scaled = Mathf.Clamp(t * segments, 0f, segments);
                int   seg    = Mathf.Min(Mathf.FloorToInt(scaled), segments - 1);
                float local  = scaled - seg;

                float s = seg < at     ? seg + local
                        : seg > at     ? seg + 1 + local
                        : local < split ? at + local / split
                                        : at + 1 + (local - split) / (1f - split);
                return s / (segments + 1);
            }

            foreach (var o in _data.obstacles) if (o.pathId == path.pathId) o.pathT = Remap(o.pathT);
            foreach (var o in _data.outposts)  if (o.pathId == path.pathId) o.pathT = Remap(o.pathT);
            foreach (var s in _data.shops)     if (s.pathId == path.pathId) s.pathT = Remap(s.pathT);

            ids.Insert(i + 1, node.id);
            return;
        }
    }
}

#endif
