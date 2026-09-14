using System;
using UnityEngine;

/// <summary>
/// Where an entrance prefab stands inside the archway at its door.
///
/// The three numbers are measured in the archway's own frame, the same one the arch mesh is
/// built in — so they read the same however the arena is turned and whichever door they are for:
///
///        _______
///      /  ___    \
///     |  /   \    |          up      ^  from the rim top the arch stands on
///     | |  +  |   |          across  <-> from the channel centreline
///     |_|     |___|          along   into the page, from the arch's front face
///   ___|_|_____|___          back through the wall the way you travel
///
/// All three are 0 by default, which puts the prefab at the front face of the arch, on the
/// centreline, standing on the rim top. Nothing is inherited or settled later — an offset
/// authored here is exactly the offset applied.
/// </summary>
[Serializable]
public class ArenaEntranceAlignment
{
    [Tooltip("Sideways from the channel centreline. Positive is to the right as you travel out " +
             "through the arch.")]
    public float across;

    [Tooltip("Up from the rim top the arch stands on.")]
    public float up;

    [Tooltip("Along the river, from the arch's front face. Positive runs back through the wall " +
             "the way you travel out; negative brings the prefab in toward the arena.")]
    public float along;

    public ArenaEntranceAlignment Clone() => (ArenaEntranceAlignment)MemberwiseClone();

    /// <summary>The offset as it stands in the archway's own axes.</summary>
    public Vector3 Offset => new Vector3(across, up, along);
}
