using System;
using UnityEngine;

/// <summary>
/// Cross-section of a river run, authored per river in the Level Select Designer.
/// The block and its junctions are both built from these numbers, so a junction
/// always mates with the blocks that run into it.
///
///        |<------------ outer width ------------>|
///        | rim |<----- inner width ----->| rim |
///     ___|_____|                         |_____|___   <- rim top (y = 0, mesh origin)
///                \                     /                 |
///                  \_______________ /                    | river depth
///     |                                             |
///     |                                             |    | depth
///     |_____________________________________________|    v
/// </summary>
[Serializable]
public class RiverProfile
{
    [Tooltip("Name of the river these numbers apply to — matches the River Name on a path. Leave blank for the default profile.")]
    public string riverName;

    [Tooltip("Width of the channel opening across the top of the run.")]
    [Min(0.001f)] public float innerWidth = 0.42f;

    [Tooltip("Solid lip either side of the channel. Outer width = inner width + 2 x rim width.")]
    [Min(0f)] public float rimWidth = 0.06f;

    [Tooltip("How far the channel floor sits below the rim top.")]
    [Min(0.001f)] public float riverDepth = 0.16f;

    [Tooltip("Total height of the run, rim top down to the underside. Not authored here — it is " +
             "stamped from the one Drop under Procedural Generation, so every piece ends at the " +
             "same height.")]
    [Min(0.001f)] public float depth = 1f;

    [Tooltip("How far the top surface dips where one generated piece butts onto the next — a " +
             "run against the patch that carries it into a pool or out of a junction. The two " +
             "meet exactly either way, but coplanar surfaces from separate meshes still show a " +
             "hard crease; dipping the shared ring turns that line into a deliberate joint, so " +
             "the river reads as marble blocks laid end to end. 0 leaves the joints flush, as " +
             "they were. Try around 0.03 at this world scale.")]
    [Min(0f)] public float jointGroove = 0f;

    /// <summary>Full outer width — derived, never authored.</summary>
    public float OuterWidth => innerWidth + rimWidth * 2f;

    public RiverProfile Clone() => (RiverProfile)MemberwiseClone();

    /// <summary>Identifies the shape, so meshes can be reused when nothing changed.</summary>
    public string ShapeKey =>
        $"{innerWidth:F4}|{rimWidth:F4}|{riverDepth:F4}|{depth:F4}|{jointGroove:F4}";
}
