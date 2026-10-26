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

    // ── The run's style ──
    // Not part of the river's shape: they belong to the PATH (its Run Type), and the Level Select
    // Designer stamps them onto a copy of this profile for whatever it builds from that path.
    // A Structural River Run leaves both at nothing, which is the flat-rimmed run as it always was.

    /// <summary>How rounded the rim is: 0 a flat rim with square corners, 1 fully rounded.
    /// With the outer walls on both corners round; with them off only the channel side does,
    /// and the outer edge stays level to meet the ground.</summary>
    [NonSerialized] public float rimRounding;

    /// <summary>The run is built with no outer walls, so the landscape can run onto its rim.</summary>
    [NonSerialized] public bool wallsOff;

    /// <summary>
    /// Outer Flare, in world units: how far the outer corner is stretched out past the rim's
    /// outer edge — and how far it drops by the same — before the outer wall goes on down
    /// vertically. 0 is the plain wall. Only with the outer walls on.
    /// </summary>
    [NonSerialized] public float outerFlare;

    /// <summary>
    /// The river's width at each node of the path one run was swept along, in that run's own
    /// local space, path order — the node's width override, or this profile's inner width. Null
    /// leaves the run its one inner width the whole way. Stamped by the Level Select Designer on
    /// a copy of this profile for the run it builds from it, never on the shared one: the points
    /// mean nothing in any other run's space.
    /// </summary>
    [NonSerialized] public WidthKey[] widthKeys;

    /// <summary>The channel's width at one node of a run — see <see cref="widthKeys"/>.</summary>
    public struct WidthKey
    {
        public Vector3 at;
        public float   innerWidth;
    }

    /// <summary>
    /// The portholes bored across the run one profile copy is built for, in that run's own local
    /// space — stamped by the Level Select Designer the same way as <see cref="widthKeys"/>. Null
    /// for none.
    /// </summary>
    [NonSerialized] public PortholeKey[] portholes;

    /// <summary>
    /// A round hole bored straight across a run under its channel, at one node: through both
    /// outer walls and any walkway beside them.
    /// </summary>
    public struct PortholeKey
    {
        public Vector3 at;           // the node
        public float   radius;
        public float   height;       // the run's underside up to the hole's centre
        public float   offsetAlong;  // along the river from the node, the way the path was drawn
        public float   reach;        // how far out from the centreline the bore goes, either side
    }

    /// <summary>Full outer width — derived, never authored.</summary>
    public float OuterWidth => innerWidth + rimWidth * 2f;

    public RiverProfile Clone() => (RiverProfile)MemberwiseClone();

    /// <summary>A copy at one width all the way along, for building what stands at one spot of a run.</summary>
    public RiverProfile WithInnerWidth(float width)
    {
        var copy = Clone();
        copy.innerWidth = Mathf.Max(0.001f, width);
        copy.widthKeys  = null;
        copy.portholes  = null;
        return copy;
    }

    /// <summary>Identifies the shape, so meshes can be reused when nothing changed.</summary>
    public string ShapeKey =>
        $"{innerWidth:F4}|{rimWidth:F4}|{riverDepth:F4}|{depth:F4}|{jointGroove:F4}" +
        $"|{rimRounding:F4}|{(wallsOff ? 1 : 0)}" +
        (outerFlare > 0f ? $"|{outerFlare:F4}" : "");
}
