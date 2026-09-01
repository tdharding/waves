using UnityEngine;

/// <summary>
/// Anything that pushes fog away. Rocks, blocks, spline walls, the boat and lit street lights
/// are all the same thing wearing different numbers — a tight firm rock that fog wraps close
/// around, a small soft moving boat that fog eases aside for, a wide strong street light that
/// holds a clear space.
///
/// Registered with <see cref="FogFieldManager"/>. Nothing here knows what a rock is.
///
/// Repelling is the ONLY influence anything has on where fog is. Placement belongs to the arena
/// map and nothing else — a lamp pushes fog out of its pool, it never gathers fog to itself.
///
/// Because clearing is physical rather than scored, this interface is also the whole of the
/// "clear the fog" mechanic: light enough street lights along a stretch and the fog has nowhere
/// left to sit. Switch one off, its repeller goes with it, and fog creeps back on its own.
/// </summary>
public interface IFogRepeller
{
    /// <summary>World position at the waterline. Only .xz is read — the fog is a flat field.</summary>
    Vector3 RepelCentre { get; }

    /// <summary>The obstacle's own radius at the waterline.</summary>
    float RepelRadius { get; }

    /// <summary>
    /// Clear water kept beyond the radius. Fixed rather than scaled with size, so every rock
    /// gets a ring of the same width instead of big rocks getting enormous moats.
    ///
    /// Worth knowing: the clear radius does not simply add a gap. The spine is stretched around a bigger
    /// circle, so the same fog covers more ground and the mass thins. Raise body thickness with it.
    /// </summary>
    float RepelClearRadius { get; }

    /// <summary>
    /// How hard fog is pushed out. 1 pins the skeleton exactly on the clear radius; lower lets
    /// it press in, which suits a moving repeller like the boat where fog should lag and recover.
    /// </summary>
    float RepelStrength { get; }

    bool RepelActive { get; }
}
