using UnityEngine;

/// <summary>
/// Says how <see cref="CameraOccluderFader"/> treats a placed piece of Level Select geometry —
/// written by the designer's Occlusion dropdown on the piece's own object.
///
/// On, the piece fades whenever it stands between the camera and the boat, even when it is
/// on the river the boat is sailing (rim nodes, walkways, bridges and archways live under their
/// river's run, which otherwise never fades). Pieces with no collider of their own are given one
/// that only the camera's ray finds: on the Ignore Raycast layer, so default raycasts pass it by,
/// and colliding with no layer, so nothing bumps into it.
///
/// Off, a piece that does have colliders of its own (walls, pipes) is passed over by the
/// fader rather than faded.
///
/// <see cref="style"/> says how it fades: the whole piece, or only a soft circle round the boat
/// on screen — for big walls and set pieces.
/// </summary>
[DisallowMultipleComponent]
public class OcclusionFade : MonoBehaviour
{
    /// <summary>The child that carries a camera-only collider, under each of the piece's meshes.</summary>
    public const string ColliderChild = "OcclusionCollider";

    [Tooltip("Whether this piece fades when it blocks the camera's view of the boat.")]
    public bool fades = true;

    [Tooltip("Whole Object fades all of it. Screen Circle fades only a soft circle round the boat " +
             "on screen — sized by Circle Radius and Circle Feather on the CameraOccluderFader.")]
    public OcclusionStyle style = OcclusionStyle.WholeObject;
}

/// <summary>How a piece with Occlusion on fades when it blocks the camera's view of the boat.</summary>
public enum OcclusionStyle
{
    /// <summary>The whole piece fades.</summary>
    WholeObject,
    /// <summary>Only a soft circle round the boat on screen fades; the rest of the piece stays solid.</summary>
    ScreenCircle,
}
