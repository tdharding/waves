using UnityEngine;

/// <summary>
/// The boat on the level select map — driven, not carried.
///
/// It steers and drives the way the boat in a level does: the arrow keys turn it, it makes
/// its own way forward, Space opens the throttle and Shift eases off it for a tighter turn.
/// Where it can go is decided by the banks the Level Select Designer lays either side of the
/// water, not by a curve it is pinned to, so a turning is taken rather than asked for.
///
/// Height is the one thing it does not choose: every step it looks down for the water under
/// it and sits on that surface, which is how a river that climbs carries it up with it.
///
/// The rails this replaced are kept, in full, under Archive/LevelSelectOnRails.
/// </summary>
public class LevelSelectBoatControl : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The boat itself. Left empty, LevelSelectDataController wires it at load.")]
    [SerializeField] private Transform _boatTransform;
    [SerializeField] private Rigidbody _boatBody;

    [Header("Visual")]
    [Tooltip("The hull. Held upright — the rails used to spin it round to travel a river " +
             "backwards, and nothing turns it about its own axis any more.")]
    [SerializeField] private Transform _meshTransform;

    [Header("Speed")]
    public float baseMoveSpeed    = 3f;
    public float boostedMoveSpeed = 5f;
    public float acceleration     = 3f;
    public float deceleration     = 4f;

    [Header("Turning")]
    public float maxTurnSpeed     = 80f;
    public float turnAcceleration = 4f;
    public float turnDeceleration = 6f;

    [Header("Shift Handling")]
    [Tooltip("How much of its speed the boat keeps while Shift is held.")]
    public float shiftSpeedMultiplier = 0.5f;
    [Tooltip("How much harder it turns while Shift is held.")]
    public float shiftTurnMultiplier  = 3f;

    [Header("Anchoring")]
    [Tooltip("Press to anchor the boat where it is and hand the mouse to the camera; press " +
             "again to let go and get under way.")]
    public KeyCode anchorKey = KeyCode.Q;

    [Header("Water")]
    [Tooltip("How far above the boat the search for the water surface starts.")]
    public float surfaceProbeUp   = 4f;
    [Tooltip("How far below that it reaches. Wants to cover the deepest drop between two " +
             "rivers the boat can cross.")]
    public float surfaceProbeDown = 12f;
    [Tooltip("How far the boat rides above the surface it finds.")]
    public float floatOffset      = 0f;

    [Header("Debug")]
    [SerializeField] private bool debugMovement = false;

    // ── State ──────────────────────────────────────────────────────
    private float _currentSpeed;
    private float _currentTurnInput;
    private float _surfaceY;
    private bool  _hasSurface;

    private bool _anchored;

    private int  _waterMask;
    private bool _waterMaskFound;

    /// <summary>The Water layer, worked out the first time it is wanted — the data controller
    /// wires the boat from its own Awake, which runs before this script's.</summary>
    private int WaterMask
    {
        get
        {
            if (!_waterMaskFound)
            {
                _waterMask      = LayerMask.GetMask("Water");
                _waterMaskFound = true;
            }
            return _waterMask;
        }
    }

    // ── Public API ─────────────────────────────────────────────────

    /// <summary>Stops the boat dead — used while a menu or the intro has the floor.</summary>
    public bool ControlsFrozen { get; set; }

    /// <summary>The intro drives the boat down the river with the player's hands off it.</summary>
    public bool IntroMode { get; set; }

    /// <summary>
    /// Whether the boat is anchored to look around. A mode rather than a key held down: the
    /// camera takes the mouse outright while this is on, and a mouse being used to look with
    /// is not one that can also be holding a key. The camera reads this to offer turning
    /// about the boat rather than following along behind it.
    /// </summary>
    public bool HeldByPlayer => _anchored && !IntroMode && !ControlsFrozen;

    public Transform BoatTransform => _boatTransform;
    public Transform MeshTransform => _meshTransform;

    public float CurrentSpeed => _currentSpeed;

    /// <summary>
    /// CurrentSpeed as 0..1 against the boat's own top speed. The mirror of
    /// BoatMovement.Speed01, normalised the same way, so an effect written against the arena
    /// boat reads the same number here without knowing which boat it is following.
    /// </summary>
    public float Speed01 =>
        boostedMoveSpeed > 0f ? Mathf.InverseLerp(0f, boostedMoveSpeed, _currentSpeed) : 0f;

    /// <summary>Where the boat is and which way it faces — what gets written down when the
    /// player leaves the map.</summary>
    public Vector3 Position => _boatTransform != null ? _boatTransform.position : transform.position;
    public float   Heading  => _boatTransform != null ? _boatTransform.eulerAngles.y : 0f;

    // ── Lifecycle ──────────────────────────────────────────────────

    private void Awake()
    {
        if (WaterMask == 0)
            Debug.LogWarning("[LevelSelectBoatControl] No 'Water' layer — the boat has no " +
                             "surface to sit on and will hold whatever height it starts at.", this);
    }

    private void Start()
    {
        if (_boatTransform == null)
            Debug.LogError("[LevelSelectBoatControl] No boat wired — nothing to drive.", this);

        PrepareBody();
        SnapToSurface();
    }

    /// <summary>
    /// Sets the boat's body up to be driven. These are not choices — a boat that is kinematic
    /// is pushed through walls rather than stopped by them, and one that falls is gone the
    /// moment it leaves the water — so they are settled here rather than left to be
    /// remembered on the prefab.
    /// </summary>
    private void PrepareBody()
    {
        if (_boatBody == null) return;

        _boatBody.isKinematic            = false;
        _boatBody.useGravity             = false;
        _boatBody.interpolation          = RigidbodyInterpolation.Interpolate;
        _boatBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Every bit of the boat's heading comes from the wheel. Freeing rotation about up as
        // well as across leaves a sphere collider free to pick up spin off the first bank it
        // brushes — and since the drive follows wherever the boat is pointing, that spin turns
        // straight into driving in a circle.
        _boatBody.constraints = RigidbodyConstraints.FreezeRotation;

        // The water surfaces carry colliders so the boat can find what height the water is.
        // They are there to be looked at, never to be hit — they already exclude every layer
        // themselves, and this says the same thing from the boat's side, because a boat that
        // bumps into its own water surface would fight the very height it is being held at.
        if (WaterMask != 0)
            _boatBody.excludeLayers = _boatBody.excludeLayers | WaterMask;
    }

    private void Update()
    {
        // Polled here rather than in the step: a press lasts one frame, and a fixed step
        // either misses it or sees it twice. A paused game still runs this, so the key is
        // only read while the map is actually being played.
        if (!PauseManager.IsPaused && Input.GetKeyDown(anchorKey)) _anchored = !_anchored;

        // The intro and the menus take the boat away entirely, so an anchor cannot be left
        // set behind them and found still on when they hand it back.
        if (IntroMode || ControlsFrozen) _anchored = false;
    }

    private void FixedUpdate()
    {
        if (_boatBody == null) return;

        if (PauseManager.IsPaused)
        {
            Coast();
            return;
        }

        // Anchored, or handed over to the intro or a menu: the boat stops where it is but
        // stays on the water, so the camera has something steady to turn about.
        if (ControlsFrozen || HeldByPlayer)
        {
            _boatBody.angularVelocity = Vector3.zero;
            Coast();
            HoldOnSurface();
            return;
        }

        HandleSteering();
        HandleDrive();
        HoldOnSurface();
    }

    // ── Steering ───────────────────────────────────────────────────

    private void HandleSteering()
    {
        float targetInput = 0f;

        if (!IntroMode)
        {
            if      (Input.GetKey(KeyCode.LeftArrow))  targetInput = -1f;
            else if (Input.GetKey(KeyCode.RightArrow)) targetInput =  1f;
        }

        // Wind up into a turn and unwind out of it, so the wheel has weight.
        float rate = Mathf.Abs(targetInput) > Mathf.Abs(_currentTurnInput)
                   ? turnAcceleration
                   : turnDeceleration;

        _currentTurnInput = Mathf.MoveTowards(_currentTurnInput, targetInput,
                                              rate * Time.fixedDeltaTime);

        // Frozen rotation is the solver's answer; this is the same answer for anything a
        // contact managed to impart before the constraint took hold.
        _boatBody.angularVelocity = Vector3.zero;

        if (Mathf.Approximately(_currentTurnInput, 0f)) return;

        float turn = _currentTurnInput * maxTurnSpeed * TurnMultiplier() * Time.fixedDeltaTime;

        // Set outright rather than moved to: MoveRotation is a request the solver can refuse
        // while rotation is constrained, and the wheel is not a request.
        _boatBody.rotation = _boatBody.rotation * Quaternion.Euler(0f, turn, 0f);
    }

    private float TurnMultiplier() => ShiftHeld ? shiftTurnMultiplier : 1f;

    private bool ShiftHeld =>
        !IntroMode && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

    private bool BoostHeld => !IntroMode && Input.GetKey(KeyCode.Space);

    // ── Drive ──────────────────────────────────────────────────────

    private void HandleDrive()
    {
        float targetSpeed = BoostHeld ? boostedMoveSpeed : baseMoveSpeed;
        if (ShiftHeld) targetSpeed *= shiftSpeedMultiplier;

        float rate = targetSpeed > _currentSpeed ? acceleration : deceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, rate * Time.fixedDeltaTime);

        ApplyVelocity(_boatTransform.forward * _currentSpeed);

        if (debugMovement)
            Debug.Log($"[LevelSelectBoatControl] speed={_currentSpeed:F2} " +
                      $"turn={_currentTurnInput:F2} surfaceY={_surfaceY:F3} found={_hasSurface}");
    }

    private void Coast()
    {
        _currentSpeed     = Mathf.MoveTowards(_currentSpeed, 0f, deceleration * Time.fixedDeltaTime);
        _currentTurnInput = Mathf.MoveTowards(_currentTurnInput, 0f, turnDeceleration * Time.fixedDeltaTime);
        ApplyVelocity(_boatTransform != null ? _boatTransform.forward * _currentSpeed : Vector3.zero);
    }

    private void ApplyVelocity(Vector3 move)
    {
        // Flat: the surface decides the height, so the body is never asked to climb.
        _boatBody.linearVelocity = new Vector3(move.x, 0f, move.z);
    }

    // ── The water ──────────────────────────────────────────────────

    /// <summary>
    /// Sits the boat on the water under it. A step that finds none — over a mouth the water
    /// does not reach, or between two rivers — holds the last height found rather than
    /// dropping the boat into the world.
    /// </summary>
    private void HoldOnSurface()
    {
        if (TrySampleSurface(_boatBody.position, out float y))
        {
            _surfaceY   = y;
            _hasSurface = true;
        }

        if (!_hasSurface) return;

        Vector3 pos = _boatBody.position;
        pos.y = _surfaceY;
        _boatBody.position = pos;
    }

    /// <summary>Puts the boat on the surface at once, without waiting for a step.</summary>
    private void SnapToSurface()
    {
        if (_boatTransform == null) return;

        if (!TrySampleSurface(_boatTransform.position, out float y)) return;

        _surfaceY   = y;
        _hasSurface = true;

        Vector3 pos = _boatTransform.position;
        pos.y = y;
        _boatTransform.position = pos;
    }

    /// <summary>
    /// The height of the water at a place: looked for from above, so a boat that has slipped
    /// a little under the surface still finds it rather than searching the sky.
    /// </summary>
    private bool TrySampleSurface(Vector3 at, out float y)
    {
        y = 0f;
        if (WaterMask == 0) return false;

        Vector3 from  = at + Vector3.up * surfaceProbeUp;
        float   reach = surfaceProbeUp + surfaceProbeDown;

        if (!Physics.Raycast(from, Vector3.down, out var hit, reach, WaterMask,
                             QueryTriggerInteraction.Collide))
            return false;

        y = hit.point.y + floatOffset;
        return true;
    }

    // ── Placement ──────────────────────────────────────────────────

    /// <summary>
    /// Stands the boat somewhere, facing a way, and settles it on the water there. Everything
    /// that puts the boat down — a save, a door letting out onto a river, the intro — comes
    /// through here.
    /// </summary>
    public void PlaceAt(Vector3 position, Quaternion rotation)
    {
        if (_boatTransform == null) return;

        _boatTransform.SetPositionAndRotation(position, rotation);

        if (_boatBody != null)
        {
            _boatBody.position        = position;
            _boatBody.rotation        = rotation;
            _boatBody.linearVelocity  = Vector3.zero;
            _boatBody.angularVelocity = Vector3.zero;
        }

        _currentSpeed     = 0f;
        _currentTurnInput = 0f;
        _hasSurface       = false;

        SnapToSurface();
    }

    /// <summary>Stands the boat where a river and a distance along it name — how a door that
    /// lets out onto a river places it.</summary>
    public bool PlaceOnSegment(string segmentID, float progress)
    {
        if (!LevelSelectBoatPlacement.TryResolve(segmentID, progress,
                                                 out var pos, out var rot))
            return false;

        PlaceAt(pos, rot);
        return true;
    }

    // ── Wiring ─────────────────────────────────────────────────────

    /// <summary>Handed the boat by LevelSelectDataController once the scene is up.</summary>
    public void WireBoatReferences(Transform boatTransform, Rigidbody boatBody,
                                   Transform meshTransform = null)
    {
        _boatTransform = boatTransform;
        _boatBody      = boatBody;
        if (meshTransform != null) _meshTransform = meshTransform;

        // The rails spun the hull 180 degrees to travel a river backwards, and a scene saved
        // mid-journey still carries that. There is no reverse any more, so it comes back up
        // the right way round rather than starting the map facing its own stern.
        if (_meshTransform != null) _meshTransform.localRotation = Quaternion.identity;

        PrepareBody();
    }

    // ── Leaving ────────────────────────────────────────────────────

    private void OnDestroy()
    {
        if (_boatTransform == null) return;
        GameProgressData.SaveBoatPose(Position, Heading);
    }
}
