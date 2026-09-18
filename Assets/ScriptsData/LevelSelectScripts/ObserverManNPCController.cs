using UnityEngine;

/// <summary>
/// An observer on an installation outpost: stands a while, then walks to somewhere else on the
/// floor, stops, and stands again.
///
/// The floor is handed over by the Level Select Designer when it spawns the observers — the
/// outpost block, and the rectangle of floor inside its walls in the block's own space. Walking
/// is done in that space, so the figure stays on the floor wherever the block ends up.
///
/// Feet and facing come from the prefab's PrefabBaselineAlignment: the disc is what stays on the
/// floor, the forward override is the way it walks.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ObserverManNPCController : MonoBehaviour
{
    private const string StandingState = "StandingState";
    private const string WalkingState  = "WalkingState";

    [Header("Floor (set by the Level Select Designer)")]
    [Tooltip("The outpost block this observer stands on.")]
    [SerializeField] private Transform outpost;
    [Tooltip("Centre of the floor inside the walls, in the block's own space.")]
    [SerializeField] private Vector3 floorCentre;
    [Tooltip("Half the floor's size inside the walls: x along the block, y away from the river.")]
    [SerializeField] private Vector2 floorHalfSize;

    [Header("Standing")]
    [Tooltip("Shortest time it stands still before walking again, in seconds.")]
    [Min(0f)] public float standTimeMin = 3f;
    [Tooltip("Longest time it stands still before walking again, in seconds.")]
    [Min(0f)] public float standTimeMax = 8f;

    [Header("Walking")]
    [Tooltip("How fast it walks, in world units per second.")]
    [Min(0f)] public float walkSpeed = 0.1f;
    [Tooltip("How fast it turns towards where it's walking, in degrees per second.")]
    [Min(0f)] public float turnSpeed = 270f;

    [Header("Animation")]
    [Tooltip("How long the blend between standing and walking takes, in seconds.")]
    [Min(0f)] public float blendTime = 0.25f;

    private enum Mode { Standing, Walking }

    private Animator                _animator;
    private PrefabBaselineAlignment _aligner;
    private Mode                    _mode;
    private float                   _standTimer;
    private Vector3                 _targetLocal;

    public void SetFloor(Transform block, Vector3 centre, Vector2 halfSize)
    {
        outpost       = block;
        floorCentre   = centre;
        floorHalfSize = halfSize;
    }

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _aligner  = GetComponentInChildren<PrefabBaselineAlignment>(true);
        if (_aligner == null)
            Debug.LogWarning($"[ObserverManNPC] {name} has no PrefabBaselineAlignment — walking off its root.", this);
    }

    private void Start()
    {
        if (outpost == null)
        {
            Debug.LogWarning($"[ObserverManNPC] {name} has no outpost floor — regenerate the outposts " +
                             "in the Level Select Designer to hand it one. Standing still.", this);
            enabled = false;
            return;
        }
        Stand();
    }

    private void Update()
    {
        if (outpost == null) return;

        if (_mode == Mode.Standing)
        {
            _standTimer -= Time.deltaTime;
            if (_standTimer <= 0f) Walk();
            return;
        }

        Vector3 feet      = FeetWorld();
        Vector3 feetLocal = outpost.InverseTransformPoint(feet);
        Vector3 toTarget  = _targetLocal - feetLocal;
        toTarget.y = 0f;

        float step = walkSpeed * Time.deltaTime;
        if (toTarget.magnitude <= step)
        {
            MoveFeetTo(_targetLocal, feet);
            Stand();
            return;
        }

        // Turn towards the target round the feet, so the feet don't slide as it turns.
        Vector3 wantWorld = outpost.TransformDirection(toTarget);
        wantWorld.y = 0f;
        Vector3 faceWorld = FacingWorld();
        if (wantWorld.sqrMagnitude > 1e-10f && faceWorld.sqrMagnitude > 1e-10f)
        {
            float angle = Vector3.SignedAngle(faceWorld, wantWorld, Vector3.up);
            float turn  = Mathf.Clamp(angle, -turnSpeed * Time.deltaTime, turnSpeed * Time.deltaTime);
            transform.RotateAround(feet, Vector3.up, turn);
        }

        MoveFeetTo(feetLocal + toTarget.normalized * step, FeetWorld());
    }

    private void Stand()
    {
        _mode       = Mode.Standing;
        _standTimer = Random.Range(standTimeMin, Mathf.Max(standTimeMin, standTimeMax));
        _animator.CrossFadeInFixedTime(StandingState, blendTime);
    }

    private void Walk()
    {
        _mode        = Mode.Walking;
        _targetLocal = floorCentre + new Vector3(Random.Range(-floorHalfSize.x, floorHalfSize.x), 0f,
                                                 Random.Range(-floorHalfSize.y, floorHalfSize.y));
        _animator.CrossFadeInFixedTime(WalkingState, blendTime);
    }

    // Puts the feet at a point on the floor, at the floor's height, by moving the whole figure.
    private void MoveFeetTo(Vector3 local, Vector3 feetWorldNow)
    {
        local.y = floorCentre.y;
        transform.position += outpost.TransformPoint(local) - feetWorldNow;
    }

    private Vector3 FeetWorld() => _aligner != null ? _aligner.transform.position : transform.position;

    private Vector3 FacingWorld()
    {
        Vector3 f = _aligner != null ? _aligner.transform.TransformDirection(_aligner.LocalForward)
                                     : transform.forward;
        f.y = 0f;
        return f;
    }
}
