using UnityEngine;

/// <summary>
/// What actually happens when the player presses the interact key at a point on the map — the
/// poster that comes up, the characters that start talking, the map that opens.
///
/// One of these sits beside a <see cref="LevelSelectInteractPoint"/> on the same object. The
/// point handles being noticed and prompted; this handles what is shown, so a new kind of
/// interaction is a new script here and nothing else changes.
///
/// The run of it: <see cref="Begin"/> once, <see cref="Step"/> every frame while it is up, and
/// <see cref="End"/> once when it closes — however it closes, including the boat being taken
/// away by a menu or the scene being left.
/// </summary>
public abstract class LevelSelectInteractAction : MonoBehaviour
{
    /// <summary>
    /// Whether this is worth offering at all right now. A poster is always there to be read, so
    /// the default is yes; something that comes and goes — the angel, who is only on her perch
    /// some of the time — says no while it is away, and the boat is never prompted for it.
    /// </summary>
    public virtual bool IsAvailable => true;

    /// <summary>
    /// Whether the map stops its clock while this is up. A poster is a still picture, so the
    /// default is yes; anything that plays out over time on screen — a conversation fading its
    /// lines in — says no, or it would be frozen before it had shown anything.
    /// </summary>
    public virtual bool PausesTime => true;

    /// <summary>
    /// Where the camera should glance when passing, if this knows better than the point does —
    /// the angel's perch sits high on a tower the point only marks the foot of. A point's own
    /// look-at, when one is set, still wins. The default names nothing.
    /// </summary>
    public virtual bool TryGetLookAt(out Vector3 worldPosition)
    {
        worldPosition = default;
        return false;
    }

    /// <summary>
    /// Opens it. False means there was nothing to show — a poster with no art, a character
    /// with nothing to say — and the interaction does not start at all, so the boat is never
    /// anchored for an empty screen.
    /// </summary>
    public abstract bool Begin();

    /// <summary>
    /// One frame of it. <paramref name="advancePressed"/> is the interact key pressed again
    /// while it is up: a poster takes that as "done", a conversation as "next line". Return
    /// false to close.
    /// </summary>
    public abstract bool Step(bool advancePressed);

    /// <summary>Closes it and puts back anything it borrowed.</summary>
    public abstract void End();
}
