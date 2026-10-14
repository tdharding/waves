using UnityEngine;

/// <summary>
/// Moves a turret tower in play: the head turns to a new heading, pauses, then the nose tilts to
/// a new angle, pauses, and round again — never both at once. Each heading and angle is picked at
/// random. How fast, how far and how long the pauses are is set for every turret at once on the
/// scene's <see cref="TurretTowerAnimation"/>.
///
/// The Level Select Designer builds the turret and fills this in: <see cref="head"/> turns about
/// its local y (it sits on the orb's centre), and <see cref="nose"/>, a child of the head on the
/// same centre, tilts about its local x.
/// </summary>
public class TurretTowerController : MonoBehaviour
{
    [Tooltip("The orb. Turns left and right about its own centre.")]
    public Transform head;

    [Tooltip("The nose, a child of the head. Tilts up and down about the orb's centre.")]
    public Transform nose;

    [Tooltip("Where the nose starts, in degrees up from level — the pitch it was built at.")]
    public float restPitch = 20f;

    [Tooltip("Ticked, the turret turns and tilts, and the angel stays where she is while it does. " +
             "Unticked, it stands still where it is.")]
    public bool turretIsActive = true;

    static readonly System.Collections.Generic.List<TurretTowerController> _all =
        new System.Collections.Generic.List<TurretTowerController>();

    /// <summary>True while any turret in the scene is active — the angel holds her position.</summary>
    public static bool AnyActive
    {
        get
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null && _all[i].turretIsActive) return true;
            return false;
        }
    }

    void OnEnable()  => _all.Add(this);
    void OnDisable() => _all.Remove(this);

    enum Step { WaitToTurn, Turning, WaitToTilt, Tilting }

    Step  _step;
    float _wait;
    float _yaw, _pitch;
    float _targetYaw, _targetPitch;
    bool  _started;

    void Start()
    {
        _yaw   = head != null ? head.localEulerAngles.y : 0f;
        _pitch = restPitch;
        Apply();
        _step = Step.WaitToTurn;
    }

    void Update()
    {
        if (head == null || nose == null || !turretIsActive) return;

        var anim = TurretTowerAnimation.Active;
        if (anim == null) return;

        // The first pause is drawn once the settings are there to draw it from.
        if (!_started)
        {
            _started = true;
            _wait    = Pause(anim);
        }

        float dt = Time.deltaTime;
        switch (_step)
        {
            case Step.WaitToTurn:
                if ((_wait -= dt) > 0f) break;
                _targetYaw = Random.Range(0f, 360f);
                _step      = Step.Turning;
                break;

            case Step.Turning:
                _yaw = Mathf.MoveTowardsAngle(_yaw, _targetYaw, anim.turnSpeed * dt);
                Apply();
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, _targetYaw)) > 0.01f && anim.turnSpeed > 0f) break;
                _step = Step.WaitToTilt;
                _wait = Pause(anim);
                break;

            case Step.WaitToTilt:
                if ((_wait -= dt) > 0f) break;
                _targetPitch = Random.Range(Mathf.Min(anim.minPitch, anim.maxPitch),
                                            Mathf.Max(anim.minPitch, anim.maxPitch));
                _step        = Step.Tilting;
                break;

            case Step.Tilting:
                _pitch = Mathf.MoveTowards(_pitch, _targetPitch, anim.tiltSpeed * dt);
                Apply();
                if (Mathf.Abs(_pitch - _targetPitch) > 0.01f && anim.tiltSpeed > 0f) break;
                _step = Step.WaitToTurn;
                _wait = Pause(anim);
                break;
        }
    }

    static float Pause(TurretTowerAnimation anim)
        => Random.Range(Mathf.Min(anim.pauseMin, anim.pauseMax), Mathf.Max(anim.pauseMin, anim.pauseMax));

    // Up is a negative turn about x: a positive one tips +z down.
    void Apply()
    {
        if (head != null) head.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        if (nose != null) nose.localRotation = Quaternion.Euler(-_pitch, 0f, 0f);
    }
}
