using System;
using UnityEngine;

/// <summary>
/// Shape of a pool — the numbers that are the pool's own, rather than the river's. The
/// cross-section it is revolved from comes from the river meeting it; these three say how far
/// round it goes and how deep it sits.
///
///        |<-------- pool radius -------->|
///        |<-- island -->|                |
///     ___________________                |
///    |    island top     \               |          <- rim top (y = 0)
///                          \___________ /           | floor depth
///
/// Authored as a default for every pool, with an override per pool that wants its own — the
/// same arrangement <see cref="RiverProfile"/> uses for rivers.
/// </summary>
[Serializable]
public class PoolShape
{
    [Tooltip("Radius of the water — the channel edge. The rim goes on outside this by the " +
             "river's own rim width.")]
    [Min(0.05f)] public float poolRadius = 3f;

    [Tooltip("Radius of the plinth in the middle, flush with the rim. 0 leaves an open bowl.")]
    [Min(0f)] public float islandRadius = 1f;

    [Tooltip("How far the floor drops below the rim at its deepest — the middle of a bowl, or " +
             "the centre of the ring channel. 0 takes the river's own depth.")]
    [Min(0f)] public float floorDepth = 0f;

    public PoolShape Clone() => (PoolShape)MemberwiseClone();

    /// <summary>Identifies the shape, so meshes can be reused when nothing changed.</summary>
    public string ShapeKey => $"{poolRadius:F4}|{islandRadius:F4}|{floorDepth:F4}";
}
