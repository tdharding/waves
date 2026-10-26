using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Music boxes — a trigger box across the river at every node with Audio on (see DrawNodeAudio).
/// The boat sailing into one crossfades into that node's music (<see cref="LevelSelectMusicTrigger"/>).
///
/// Nothing about a box is authored. It is sized off the river at its node, and rebuilt on
/// Generate and whenever a node's Audio is switched on or off:
/// - Across and along: the channel plus half a rim either side, so it reaches out onto the rim
///   and the boat cannot slip past at the bank. On a pool, the whole pool the same way.
/// - Up and down: from a little above the rim top to a little below the water surface — halfway
///   on down to the channel floor. The hull rides in that band; it is the band the banks stop it in.
///
/// Facing down the river the node sits partway along — at a junction, the river carrying on
/// through it rather than the branch leaving it.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string MusicTriggersParent = "MUSICTRIGGERS";

    /// <summary>Clears the music boxes and builds one for each node with Audio on.</summary>
    private void RebuildMusicTriggers()
    {
        if (_data == null) return;

        var parent = FindParent(MusicTriggersParent);
        if (parent != null)
            foreach (var child in parent.transform.Cast<Transform>().ToList())
                Undo.DestroyObjectImmediate(child.gameObject);

        var nodes = _data.nodes.Where(n => n != null && n.musicTrigger).ToList();
        if (nodes.Count == 0) return;

        if (parent == null) parent = FindOrCreateParent(MusicTriggersParent);

        // Out of the way of mouse picks — the souls are dragged by raycast, and the box sits
        // over the water they are dragged across.
        int layer = LayerMask.NameToLayer("Ignore Raycast");

        int built = 0;
        foreach (var node in nodes)
        {
            if (!TryMusicTriggerBox(node.id, out Vector3 centre, out Quaternion facing, out Vector3 size))
            {
                Debug.LogWarning($"[LevelSelectDesigner] Node {node.id} has Audio on but is not on a " +
                                 "river — no music box built for it.");
                continue;
            }

            var go = new GameObject($"MusicTrigger_{SanitiseAssetName(node.id)}");
            Undo.RegisterCreatedObjectUndo(go, "Music Trigger");
            go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(centre, facing);
            if (layer >= 0) go.layer = layer;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size      = size;

            go.AddComponent<LevelSelectMusicTrigger>().nodeId = node.id;
            built++;
        }

        Debug.Log($"[LevelSelectDesigner] Built {built} music box(es) under {MusicTriggersParent}.");
    }

    /// <summary>
    /// Where the music box at a node stands, which way it faces and its size (x across the
    /// river, y up, z along it). False when the node is on no river.
    /// </summary>
    private bool TryMusicTriggerBox(string nodeId, out Vector3 centre, out Quaternion facing, out Vector3 size)
    {
        centre = default;
        facing = Quaternion.identity;
        size   = default;

        var path = _data.paths.FirstOrDefault(p =>
                   {
                       int i = p.nodeIds.IndexOf(nodeId);
                       return i > 0 && i < p.nodeIds.Count - 1;
                   })
                ?? _data.paths.FirstOrDefault(p => p.nodeIds.Count >= 2 && p.nodeIds.Contains(nodeId));
        if (path == null) return false;

        int     at   = path.nodeIds.IndexOf(nodeId);
        Vector3 here = WorldPosOfNode(nodeId);
        Vector3 down = WorldPosOfNode(path.nodeIds[Mathf.Min(at + 1, path.nodeIds.Count - 1)])
                     - WorldPosOfNode(path.nodeIds[Mathf.Max(at - 1, 0)]);
        down.y = 0f;
        if (down.sqrMagnitude < 1e-8f) down = Vector3.forward;

        var pool    = _data.PoolAt(nodeId);
        var profile = _data.ProfileFor(pool != null ? _data.PoolRiverName(pool) : path.riverName);

        float across = pool != null
            ? (_data.PoolShapeFor(pool).poolRadius + profile.rimWidth * 0.5f) * 2f
            : InnerWidthAtNode(profile, nodeId) + profile.rimWidth;

        // The node is on the rim top; the water lies WaterSurfaceDrop below it.
        float drop   = _data.WaterSurfaceDrop;
        float margin = Mathf.Max(profile.riverDepth - drop, 0.02f) * 0.5f;
        float bottom = here.y - drop - margin;
        float top    = here.y + margin;

        centre = new Vector3(here.x, (top + bottom) * 0.5f, here.z);
        facing = Quaternion.LookRotation(down.normalized, Vector3.up);
        size   = new Vector3(across, top - bottom, across);
        return true;
    }
}

#endif
