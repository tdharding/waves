using UnityEngine;

/// <summary>
/// The one place every turret tower's animation is set — add it to any object in the scene.
/// Each <see cref="TurretTowerController"/> reads these every frame, so changes in play mode show
/// on every turret straight away.
///
/// With none in the scene the turrets stand still at their rest pitch, and say so once.
/// </summary>
public class TurretTowerAnimation : MonoBehaviour
{
    [Tooltip("How fast the heads turn left and right, in degrees per second.")]
    [Min(0f)] public float turnSpeed = 20f;

    [Tooltip("How fast the noses tilt up and down, in degrees per second.")]
    [Min(0f)] public float tiltSpeed = 10f;

    [Tooltip("The lowest a nose tilts to, in degrees up from level.")]
    public float minPitch = 5f;

    [Tooltip("The highest a nose tilts to, in degrees up from level.")]
    public float maxPitch = 45f;

    [Tooltip("The shortest pause between one move and the next, in seconds.")]
    [Min(0f)] public float pauseMin = 1f;

    [Tooltip("The longest pause between one move and the next, in seconds.")]
    [Min(0f)] public float pauseMax = 3f;

    static TurretTowerAnimation _active;
    static bool                 _warned;

    /// <summary>The one in the scene, or null when there is none.</summary>
    public static TurretTowerAnimation Active
    {
        get
        {
            if (_active != null) return _active;

            _active = FindFirstObjectByType<TurretTowerAnimation>();
            if (_active == null && !_warned)
            {
                _warned = true;
                Debug.LogWarning("[TurretTowerAnimation] None in the scene — turret towers will stand " +
                                 "still. Add a TurretTowerAnimation to any object to set them moving.");
            }
            return _active;
        }
    }

    void OnEnable()
    {
        _active = this;
        _warned = false;
    }

    void OnDisable()
    {
        if (_active == this) _active = null;
    }
}
