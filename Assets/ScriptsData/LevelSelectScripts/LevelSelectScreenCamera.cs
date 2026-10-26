using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// A rim node screen's camera: where the view cuts to while that screen's picture is up, and
/// back from the moment it is closed.
///
/// Built by the Level Select Designer (the screen's Screen Camera toggle), standing square in front
/// of the screen; its place and lens are then set by hand. It is pinned to its screen — the
/// designer moves and turns it with the screen whenever the screen is rebuilt somewhere else.
///
/// The Cinemachine camera is kept switched off so the brain never takes it live: the follow camera
/// (<see cref="LevelSelectCameraController"/>) stands where this one does while it is
/// <see cref="Live"/>.
/// </summary>
public class LevelSelectScreenCamera : MonoBehaviour
{
    [Tooltip("The designer node whose rim node carries the screen. Written by the designer.")]
    public string nodeId;

    [Tooltip("Which platform's screen: +1 the right, -1 the left. Written by the designer.")]
    public int side = 1;

    [Header("Pinned to (written by the designer)")]
    [Tooltip("The middle of the screen, in the world, when this camera was last moved with it.")]
    public Vector3 screenCentre;
    [Tooltip("The way the screen faced then, flat, out into the river.")]
    public Vector3 screenFacing = Vector3.forward;

    private CinemachineCamera _cam;

    /// <summary>The screen camera the view is on right now, if any.</summary>
    public static LevelSelectScreenCamera Live { get; private set; }

    /// <summary>What the depth of field focuses on while this camera has the view — the screen it looks at.</summary>
    public Transform FocusTarget { get; private set; }

    private CinemachineCamera Cam
    {
        get
        {
            if (_cam == null) _cam = GetComponent<CinemachineCamera>();
            return _cam;
        }
    }

    /// <summary>This camera's vertical field of view — its Cinemachine lens.</summary>
    public float FieldOfView => Cam != null ? Cam.Lens.FieldOfView : 60f;

    /// <summary>Its near clip — set closer than the follow camera's, a screen being small and near.</summary>
    public float NearClipPlane => Cam != null ? Cam.Lens.NearClipPlane : 0.01f;

    private void Awake()
    {
        // The follow camera stands where this one does; the brain must never take it live.
        if (Cam != null) Cam.enabled = false;
    }

    private void OnDisable()
    {
        if (Live == this) Live = null;
    }

    /// <summary>Puts the view on this camera, focused on <paramref name="screen"/>.</summary>
    public void Look(Transform screen)
    {
        FocusTarget = screen;
        Live        = this;
    }

    /// <summary>Takes the view off it, if it is still on it.</summary>
    public void Release()
    {
        if (Live == this) Live = null;
    }
}
