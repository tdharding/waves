using System;
using UnityEngine;

/// <summary>
/// Shape of the archway standing over a river where it arrives at an arena.
///
/// The arch is the river's own cross-section stood upright and carried over: its two legs are
/// the run's rims, and the opening between them is the channel. So an arch always lands square
/// on the run it straddles, whatever that river's section happens to be.
///
///          _______
///        /  ___    \        <- arch height (the curved part, above the springing)
///       |  /   \    |
///       | |     |   |       <- leg height (the straight part)
///       |_|     |___|
///       | |     |   |
///     __|_|_____|___|__     <- rim top (y = 0, the base the arch stands on)
///        ^     ^
///     thickness  opening
///     (= rim)    (= channel)
///
/// <see cref="openingWidth"/> and <see cref="thickness"/> are 0 by default, meaning "take them
/// from the river arriving" — the same 0-means-inherit convention <see cref="PoolShape.floorDepth"/>
/// uses. Setting either breaks the arch away from the run, which is worth doing deliberately
/// and not by accident.
/// </summary>
[Serializable]
public class ArenaArchwayProfile
{
    [Tooltip("Width of the opening between the legs. 0 takes the arriving river's channel width, " +
             "so the arch frames the water exactly.")]
    [Min(0f)] public float openingWidth = 0f;

    [Tooltip("How thick the arch band is. 0 takes the arriving river's rim width, so the legs " +
             "stand exactly on its rims.")]
    [Min(0f)] public float thickness = 0f;

    [Tooltip("Height of the straight part of the legs, measured up from the rim top.")]
    [Min(0f)] public float legHeight = 0.35f;

    [Tooltip("Height of the curved part above the legs. Equal to half the opening gives a plain " +
             "semicircle; more than that gives the taller, rounder arch.")]
    [Min(0.001f)] public float archHeight = 0.4f;

    [Tooltip("How far the archway runs along the river — the depth of the tunnel. 0 takes the " +
             "arena wall's thickness, so the arch is exactly the way through the wall.")]
    [Min(0f)] public float depth = 0f;

    public ArenaArchwayProfile Clone() => (ArenaArchwayProfile)MemberwiseClone();

    /// <summary>Identifies the shape, so meshes can be reused when nothing changed.</summary>
    public string ShapeKey =>
        $"{openingWidth:F4}|{thickness:F4}|{legHeight:F4}|{archHeight:F4}|{depth:F4}";

    /// <summary>
    /// The shape with every inherited number settled against the river it straddles and the
    /// wall it stands in — what the mesh is actually built from.
    /// </summary>
    public ArenaArchwayProfile Resolve(RiverProfile river, float wallThickness)
    {
        var settled = Clone();

        if (settled.openingWidth <= 0.0001f)
            settled.openingWidth = river != null ? river.innerWidth : 0.42f;

        if (settled.thickness <= 0.0001f)
            settled.thickness = river != null && river.rimWidth > 0.0001f ? river.rimWidth : 0.06f;

        if (settled.depth <= 0.0001f)
            settled.depth = Mathf.Max(0.01f, wallThickness);

        return settled;
    }
}
