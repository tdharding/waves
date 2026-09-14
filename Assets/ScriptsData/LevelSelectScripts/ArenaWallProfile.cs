using System;
using UnityEngine;

/// <summary>
/// Cross-section of a level select arena wall, authored per arena in the Level Select Designer.
///
/// The same four numbers the in-level arena walls are built from, so an arena reads the same
/// on the map as it does once you are inside it. <see cref="ProceduralArenaWallMesh"/> builds
/// both.
///
///                    | thickness |
///                    |___________|   <- wall top (y = +height)
///                    |           |
///     - - - - - - - -|- - - - - -|- - -   <- waterline (y = 0, mesh origin)
///                    |           |
///     |<-- radius -->|           |        | drop
///     centre         |___________|        v
///
/// <see cref="radius"/> is the arena boundary — the wall's INNER face sits on it and the
/// thickness is added outward, so the closest approach to the centre is always the radius the
/// doors and the river are placed against.
/// </summary>
[Serializable]
public class ArenaWallProfile
{
    [Tooltip("Arena boundary. The wall's inner face sits exactly on this and the thickness " +
             "goes on outside it, so the play area is never eaten into. 0 takes the radius the " +
             "arena already carries.")]
    [Min(0f)] public float radius = 0f;

    [Tooltip("How thick the wall is, laid off outward from the radius.")]
    [Min(0.01f)] public float thickness = 1f;

    [Tooltip("How far the wall stands above the water surface.")]
    [Min(0f)] public float height = 4f;

    [Tooltip("How far the wall carries on below the water surface, so it reads as bottomless. " +
             "Not authored here — it is stamped from the one Drop under Procedural Generation, " +
             "so every piece ends at the same height.")]
    [Min(0f)] public float drop = 8f;

    /// <summary>Outer face of the wall — derived, never authored.</summary>
    public float OuterRadius => radius + thickness;

    public ArenaWallProfile Clone() => (ArenaWallProfile)MemberwiseClone();

    /// <summary>Identifies the shape, so meshes can be reused when nothing changed.</summary>
    public string ShapeKey => $"{radius:F4}|{thickness:F4}|{height:F4}|{drop:F4}";
}
