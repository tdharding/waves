using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LevelSelectCameraController : MonoBehaviour
{
    [Header("Camera")]
    public CinemachineCamera cam;

    [Header("Follow Pose")]
    [Tooltip("How far behind the boat the camera sits, along the ground.")]
    public float followDistance = 8f;
    [Tooltip("How far above the boat it sits. Together with the distance this is the whole " +
             "resting shot — the camera looks straight at the boat from there.")]
    public float followHeight = 3f;
    [Tooltip("Raises (or lowers) the point on the boat the camera follows and looks at, in " +
             "world units. The distance and height are measured from this point, so the whole " +
             "shot lifts with it and the boat sits lower in the frame.")]
    public float followTargetVerticalOffset = 0f;
    [Tooltip("The resting zoom. 1 is the shot exactly as the distance and height describe it; " +
             "the scroll wheel moves out from here, held in by the distance limits below.")]
    public float defaultZoom = 1f;
    [Tooltip("How long the camera takes to swing back in behind the boat. 0 pins it rigidly to " +
             "the stern; a little lets it lag through a turn and catch up after it.")]
    public float followCatchUpTime = 0.35f;
    [Tooltip("The lens the camera rests at — its vertical field of view, in degrees. Pushed to " +
             "the Cinemachine camera when the boat is wired. 0 leaves the lens as authored.")]
    public float defaultVerticalFOV = 16.2f;

    [Header("Downhill Rise")]
    [Tooltip("How much higher the camera sits, in world units, once the boat is on a drop as " +
             "steep as the slope below. Added to the follow height, so the camera looks down " +
             "the drop from above rather than at the boat's back. 0 turns it off.")]
    [Min(0f)] public float downhillRise = 2f;
    [Tooltip("The downhill slope, in degrees, at which the whole rise is in. Gentler drops " +
             "lift the camera a share of it; climbs leave it where it rests.")]
    [Min(0.01f)] public float downhillRiseFullSlope = 20f;
    [Tooltip("How long the camera takes to rise and settle again. Its own time rather than the " +
             "follow's, so a short dip need not bob the shot.")]
    [Min(0f)] public float downhillRiseTime = 0.5f;

    [Header("Follow Distance")]
    public float minDistance    = 4f;
    public float maxDistance    = 16f;

    [Header("Scroll Zoom")]
    public float zoomSpeed = 5f;

    [Header("Orbit (While Anchored)")]
    [Tooltip("The boat. Turning about it is only offered while it is held still with Q. " +
             "Left empty, it is found in the scene.")]
    public LevelSelectBoatControl boatControl;
    public float orbitSpeed = 250f;
    public float pitchMin   = -60f;
    public float pitchMax   = 45f;
    [Tooltip("Hides the pointer and holds it in the middle of the screen while turning, so the " +
             "mouse can be moved on and on one way without meeting the edge of the screen.")]
    public bool lockCursorWhileOrbiting = true;

    [Header("Low Angle Zoom")]
    [Tooltip("Pulls the camera in as the pitch drops, so it looks up from the boat rather than " +
             "dipping below the water surface. Only while turning about the anchored boat — " +
             "under way the follow pose is the shot.")]
    public bool lowAngleZoomEnabled = true;
    [Tooltip("Level the camera starts zooming in at, in world units from the boat's own position — " +
             "measured like the Height Floor. Taken where the camera would sit at the scroll zoom; " +
             "above it the scroll zoom distance is used, and the zoom is all the way in by the floor.")]
    public float lowAngleZoomStartHeight = 1f;
    [Tooltip("Distance used once the camera has come down to the Height Floor.")]
    public float lowAngleZoomDistance = 3f;
    [Tooltip("Follow Target Vertical Offset used once the camera has come down to the Height Floor. " +
             "The point the camera follows and looks at moves from the normal offset to this one as " +
             "the zoom goes in, and back as it comes out.")]
    public float lowAngleZoomFollowOffset = 0f;
    [Tooltip("How quickly the zoom eases toward its target. Higher = snappier.")]
    public float lowAngleZoomSmoothing = 8f;

    [Header("Height Floor")]
    [Tooltip("Hard backstop so the camera can never drop below the boat while turning about it, " +
             "whatever combination of pitch and zoom distance it ends up at. The low-angle zoom " +
             "softens the approach; this guarantees it.")]
    public bool limitCameraHeight = true;
    [Tooltip("The lowest the camera may go: a level in world units measured from the boat's own " +
             "position (not the lifted follow point) — negative sits below the boat. The pitch is " +
             "raised as far as needed to hold it.")]
    [FormerlySerializedAs("minHeightAboveBoat")]
    public float heightFloor = 0.5f;

    [Header("Interact Lean")]
    [Tooltip("Whether interact points pull the camera's look at all. Off, the glance eases home " +
             "and stays there; the settings below are kept for when it comes back on.")]
    public bool interactLeanEnabled = true;
    [Tooltip("How far the camera turns its head towards an interact point it is passing, as a " +
             "share of the angle to it. 0 looks straight down the boat's heading as before; " +
             "1 would face the point outright. A quarter reads as a glance.")]
    [Range(0f, 1f)] public float interactLean = 0.25f;
    [Tooltip("The most the glance is ever allowed to be worth, in degrees, however far off to " +
             "the side the point sits. This is what keeps it a lean rather than a look.")]
    [Min(0f)] public float maxInteractLeanAngle = 12f;
    [Tooltip("How far the camera keeps its place behind the boat while it glances. 0 swings the " +
             "whole shot round the boat towards the point; 1 stays dead behind the stern and only " +
             "turns its head to look.")]
    [Range(0f, 1f)] public float interactLeanStayBehind = 0f;
    [Tooltip("How long the glance takes to come on and to go again. Its own time rather than " +
             "the follow's, so the camera can swing back in behind the boat briskly and still " +
             "notice things slowly. 0 turns the head at once.")]
    [Min(0f)] public float interactLeanTime = 0.6f;
    [Tooltip("How far the camera draws in while it is glancing, as a share of the distance it " +
             "sits at. 0 keeps its distance; a little leans in with the look.")]
    [Range(0f, 0.9f)] public float interactLeanZoom = 0.12f;
    [Tooltip("How far the camera tips its head up or down towards the point's look-at, as a " +
             "share of the angle to it. The camera tilts where it stands rather than orbiting, " +
             "so looking up at something high never drops it towards the water. 0 keeps the " +
             "resting pitch.")]
    [Range(0f, 1f)] public float interactLeanVertical = 0.25f;
    [Tooltip("The most the up-or-down glance is ever allowed to be worth, in degrees.")]
    [Min(0f)] public float maxInteractLeanPitch = 12f;

    [Header("Editor Preview")]
    [SerializeField] private Transform previewTarget;
    [SerializeField] private Vector3   previewOrigin;   // world position the boat starts at — set by spawner

    public bool IsControlEnabled { get; set; } = true;

    private float             _currentDistance;
    private float             _yaw;
    private float             _pitch;
    private float             _zoom = 1f;
    private Transform         _orbitPivot;
    private Transform         _boatTarget;

    private float _yawVelocity;
    private float _pitchVelocity;
    private float _distanceVelocity;

    private float _lowAngleZoomIn;     // how far the low-angle zoom is in, 0 to 1 at the floor, eased

    private float _leanYaw;           // the glance, in degrees off the shot the follow holds
    private float _leanWeight;         // how far into a point's reach the boat stands, eased
    private float _leanYawVelocity;
    private float _leanWeightVelocity;
    private float _leanPitch;          // the head tipped up (negative) or down, in degrees
    private float _leanPitchVelocity;

    private float _rise;               // the downhill rise in, in world units, eased
    private float _riseVelocity;

    private bool           _isOrbiting;
    private CursorLockMode _cursorLockBeforeOrbit;
    private bool           _cursorVisibleBeforeOrbit;

    /// <summary>The resting shot as an angle and a length down the view ray, so the one piece of
    /// orbit maths carries both the follow and the turning about the boat.</summary>
    private float RestPitch    => Mathf.Atan2(followHeight, Mathf.Max(followDistance, 0.001f)) * Mathf.Rad2Deg;
    private float RestDistance => Mathf.Max(new Vector2(followDistance, followHeight).magnitude, 0.001f);

    /// <summary>How far out the camera wants to be: the resting shot taken at the current zoom,
    /// held inside the distance the map allows.</summary>
    private float TargetDistance => Mathf.Clamp(RestDistance * _zoom, minDistance, maxDistance);

    /// <summary>The shot under way with the downhill rise in: the same distance behind the boat,
    /// the height lifted, so the camera climbs and looks down more steeply rather than backing
    /// off. Taken at the current zoom, like the resting shot.</summary>
    private float FollowPitch => Mathf.Atan2(followHeight + _rise, Mathf.Max(followDistance, 0.001f)) * Mathf.Rad2Deg;
    private float FollowDistance =>
        Mathf.Clamp(Mathf.Max(new Vector2(followDistance, followHeight + _rise).magnitude, 0.001f) * _zoom,
                    minDistance, maxDistance);

    /// <summary>The boat is held still with Q, and turning about it is offered. A display point
    /// holds the view as well as the boat, and while one is up this goes false again, so the
    /// mouse is left alone.</summary>
    /// <summary>The point the camera follows and looks at: the boat, lifted by the offset.</summary>
    private Vector3 FollowPoint(Vector3 boatPosition) => boatPosition + Vector3.up * CurrentFollowOffset;

    /// <summary>The follow offset with the low-angle zoom's share of its own offset in.</summary>
    private float CurrentFollowOffset =>
        Mathf.Lerp(followTargetVerticalOffset, lowAngleZoomFollowOffset, _lowAngleZoomIn);

    private bool IsAnchored =>boatControl != null && boatControl.CanLookAround;

    /// <summary>A display point has the boat and the view both: the angles stay exactly where
    /// they were, so the camera neither answers the mouse nor swings back in behind the boat
    /// while what was opened is on screen.</summary>
    private bool IsViewHeld => boatControl != null && boatControl.LookLocked;

    private void Awake()
    {
        // No longer need to fetch CinemachineFollow
    }

    private void Start()
    {
        _zoom            = Mathf.Max(defaultZoom, 0.01f);
        _currentDistance = TargetDistance;

        ApplyDefaultLens();

        if (boatControl == null)
            boatControl = Object.FindFirstObjectByType<LevelSelectBoatControl>();

        // Self-initialise if the data controller hasn't called SetFollowTarget yet
        if (_boatTarget == null && previewTarget != null)
        {
            Debug.Log($"[CameraController] Start: _boatTarget null, self-initialising from previewTarget '{previewTarget.name}'");
            SetFollowTarget(previewTarget);
        }
        else if (_boatTarget == null)
        {
            Debug.LogWarning("[CameraController] Start: _boatTarget null and no previewTarget — camera has no follow target.");
        }
    }

    // Called by LevelSelectDataController at runtime
    public void SetFollowTarget(Transform target)
    {
        _boatTarget = target;
        Debug.Log($"[CameraController] SetFollowTarget: target='{(target != null ? target.name : "NULL")}', cam='{(cam != null ? cam.name : "NULL")}'");

        if (boatControl == null)
            boatControl = Object.FindFirstObjectByType<LevelSelectBoatControl>();

        if (boatControl == null)
            Debug.LogWarning("[CameraController] SetFollowTarget: no LevelSelectBoatControl found — " +
                             "the camera will hold its place behind the boat and never turn about it.");

        if (_orbitPivot == null)
        {
            _orbitPivot = new GameObject("CameraOrbitPivot").transform;
            _orbitPivot.SetParent(transform);
        }

        if (_boatTarget != null && cam != null)
        {
            ApplyDefaultLens();

            _orbitPivot.position = FollowPoint(_boatTarget.position);

            // The camera starts in its resting shot behind the boat. Any opening shot is a
            // camera sequence's, which hands back to this.
            SnapToFollowPose();
            ApplyCameraState();
            Debug.Log($"[CameraController] SetFollowTarget: final cam position={cam.transform.position}, rotation={cam.transform.eulerAngles}");
        }
        else
        {
            if (_boatTarget == null) Debug.LogWarning("[CameraController] SetFollowTarget: target is NULL — camera will not initialise.");
            if (cam == null)         Debug.LogWarning("[CameraController] SetFollowTarget: 'cam' field is NULL — assign the CinemachineCamera.");
        }
    }

    /// <summary>Called by CameraSequence when it hands back. The camera has been held in its resting
    /// shot behind the boat all along, so the brain's blend lands on a still, settled shot and
    /// control simply comes back.</summary>
    public void TransitionToFollow()
    {
        if (_boatTarget != null && _orbitPivot != null)
        {
            _orbitPivot.position = FollowPoint(_boatTarget.position);
            SnapToFollowPose();
            ApplyCameraState();
        }
        IsControlEnabled = true;
    }

    /// <summary>Puts the camera straight into the resting shot behind the boat — no easing, no
    /// glance — with the eases let go so the follow carries on from rest.</summary>
    private void SnapToFollowPose()
    {
        _yaw             = _boatTarget.eulerAngles.y;
        _pitch           = Mathf.Clamp(FollowPitch, pitchMin, pitchMax);
        _currentDistance = FollowDistance;
        _lowAngleZoomIn  = 0f;
        _leanYaw = _leanPitch = _leanWeight = 0f;
        _yawVelocity = _pitchVelocity = _distanceVelocity = 0f;
        _leanYawVelocity = _leanPitchVelocity = _leanWeightVelocity = 0f;
    }

    private void ApplyCameraState()
    {
        if (cam == null || _orbitPivot == null) return;

        // Update pivot rotation based on internal yaw/pitch, with the glance laid over it
        _orbitPivot.rotation = Quaternion.Euler(_pitch, _yaw + _leanYaw, 0f);

        // Position camera relative to pivot manually, drawn in by however much of the glance is
        // in. The zoom is read here rather than eased, so moving the slider is felt at once.
        float distance = _currentDistance * (1f - interactLeanZoom * _leanWeight);

        // The share of the glance kept behind the stern moves only the head, not where the
        // camera stands, so at 1 it stays dead behind the boat and turns to look.
        Quaternion standing = Quaternion.Euler(_pitch, _yaw + _leanYaw * (1f - interactLeanStayBehind), 0f);

        cam.transform.position = _orbitPivot.position + standing * (Vector3.back * distance);
        // The up-or-down glance tips the camera's own head, not the orbit, so it stays put.
        cam.transform.rotation = _orbitPivot.rotation * Quaternion.Euler(_leanPitch, 0f, 0f);
    }

    /// <summary>Sets the lens the map is looked at through. Part of the resting shot rather than
    /// something watched every frame, so anything that takes the lens away later — a shop camera,
    /// a cutscene — keeps it.</summary>
    private void ApplyDefaultLens()
    {
        if (cam == null || defaultVerticalFOV <= 0f) return;

        cam.Lens.FieldOfView = Mathf.Clamp(defaultVerticalFOV, 1f, 179f);
    }

    /// <summary>
    /// The glance: the camera notices an interact point it is passing and turns its head a
    /// little towards it, drawing in as it does. It is a share of the angle to the point and
    /// held to a few degrees, so something dead ahead moves the camera not at all and something
    /// abeam moves it a little - the head turning while the walking carries on.
    ///
    /// Only the nearest point in reach counts, and reach is the point's own radius, so the
    /// glance and the prompt come up together. It fades in across the outer part of the radius
    /// rather than arriving at its edge, and is eased on its own time: the camera may swing
    /// back in behind the boat at quite another speed from the one it notices things at.
    /// </summary>
    private void EaseInteractLean(bool offered)
    {
        float targetYaw    = 0f;
        float targetPitch  = 0f;
        float targetWeight = 0f;

        if (offered) LeanTarget(out targetYaw, out targetPitch, out targetWeight);

        if (interactLeanTime <= 0f)
        {
            _leanYaw            = targetYaw;
            _leanPitch          = targetPitch;
            _leanWeight         = targetWeight;
            _leanYawVelocity    = 0f;
            _leanPitchVelocity  = 0f;
            _leanWeightVelocity = 0f;
            return;
        }

        _leanYaw    = Mathf.SmoothDampAngle(_leanYaw, targetYaw, ref _leanYawVelocity,
                                            interactLeanTime);
        _leanPitch  = Mathf.SmoothDampAngle(_leanPitch, targetPitch, ref _leanPitchVelocity,
                                            interactLeanTime);
        _leanWeight = Mathf.SmoothDamp(_leanWeight, targetWeight, ref _leanWeightVelocity,
                                       interactLeanTime);
    }

    /// <summary>Where the glance wants to be this frame: how far off the boat's heading the
    /// nearest point in reach lies, above or below the camera's line of sight its look-at sits,
    /// and how far into its reach the boat stands.</summary>
    private void LeanTarget(out float leanYaw, out float leanPitch, out float weight)
    {
        leanYaw   = 0f;
        leanPitch = 0f;
        weight    = 0f;

        if (interactLean <= 0f && interactLeanZoom <= 0f && interactLeanVertical <= 0f) return;

        Vector3 boat = _boatTarget.position;

        LevelSelectInteractPoint nearest = null;
        float nearestDistance = float.MaxValue;

        var points = LevelSelectInteractPoint.All;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point == null || !point.InReach(boat)) continue;

            float distance = point.FlatDistanceTo(boat);
            if (distance >= nearestDistance) continue;

            nearest         = point;
            nearestDistance = distance;
        }

        if (nearest == null) return;

        // Nothing at the very edge of reach, all of it once well inside.
        float radius  = Mathf.Max(nearest.radius, 0.001f);
        float inwards = Mathf.Clamp01((radius - nearestDistance) / (radius * LeanFadeShare));
        weight        = Mathf.SmoothStep(0f, 1f, inwards);

        Vector3 lookAt = nearest.LookAtPosition;

        // Up or down: measured from where the camera stands, against the pitch it holds, so a
        // look-at level with its line of sight asks for nothing. Pitch is positive looking down.
        Vector3 fromCamera = lookAt - cam.transform.position;
        float   across     = new Vector2(fromCamera.x, fromCamera.z).magnitude;
        if (across > 0.0001f || Mathf.Abs(fromCamera.y) > 0.0001f)
        {
            float lookPitch = -Mathf.Atan2(fromCamera.y, across) * Mathf.Rad2Deg;
            float rise      = Mathf.DeltaAngle(_pitch, lookPitch);
            leanPitch = Mathf.Clamp(rise * interactLeanVertical * weight,
                                    -maxInteractLeanPitch, maxInteractLeanPitch);
        }

        Vector3 towards = lookAt - boat;
        towards.y = 0f;
        if (towards.sqrMagnitude < 0.0001f) return;

        // How far round from the stern-on shot the point sits, signed, so the glance goes the
        // way the point is.
        float pointYaw = Mathf.Atan2(towards.x, towards.z) * Mathf.Rad2Deg;
        float offset   = Mathf.DeltaAngle(_boatTarget.eulerAngles.y, pointYaw);

        leanYaw = Mathf.Clamp(offset * interactLean * weight,
                              -maxInteractLeanAngle, maxInteractLeanAngle);
    }

    /// <summary>The outer share of a point's reach the glance fades in across.</summary>
    private const float LeanFadeShare = 0.45f;

    /// <summary>
    /// The resting shot: dead behind the boat, at the authored height and zoom. The angles are
    /// eased rather than set, so a turn leaves the camera trailing the stern for a moment and it
    /// comes round after — the catching up that gives a turning its weight.
    /// </summary>
    private void EaseTowardsFollowPose()
    {
        float targetYaw      = _boatTarget.eulerAngles.y;
        float targetPitch    = Mathf.Clamp(FollowPitch, pitchMin, pitchMax);
        float targetDistance = FollowDistance;

        if (followCatchUpTime <= 0f)
        {
            _yaw             = targetYaw;
            _pitch           = targetPitch;
            _currentDistance = targetDistance;
            return;
        }

        _yaw             = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, followCatchUpTime);
        _pitch           = Mathf.SmoothDampAngle(_pitch, targetPitch, ref _pitchVelocity, followCatchUpTime);
        _currentDistance = Mathf.SmoothDamp(_currentDistance, targetDistance, ref _distanceVelocity, followCatchUpTime);
    }

    /// <summary>
    /// Lifts the camera while the boat runs downhill, by a share of the rise that grows with
    /// how steep the drop is. Climbs ask for nothing. Only under way: anchored, the mouse has
    /// the shot, and the rise eases away so it is not waiting when the boat sets off again.
    /// </summary>
    private void EaseDownhillRise(bool underWay)
    {
        float target = 0f;
        if (underWay && boatControl != null && downhillRise > 0f)
        {
            float steepness = Mathf.Clamp01(boatControl.SlopeAngle / downhillRiseFullSlope);
            target = downhillRise * steepness;
        }

        if (downhillRiseTime <= 0f)
        {
            _rise         = target;
            _riseVelocity = 0f;
            return;
        }

        _rise = Mathf.SmoothDamp(_rise, target, ref _riseVelocity, downhillRiseTime);
    }

    private void LateUpdate()
    {
        if (cam == null || _orbitPivot == null || _boatTarget == null) return;

        // Always keep pivot on the boat
        _orbitPivot.position = FollowPoint(_boatTarget.position);

        if (!IsControlEnabled)
        {
            // Another camera has the view (a camera sequence, the shop): wait in the resting shot
            // behind the boat, so whatever hands back blends into a settled shot.
            SnapToFollowPose();
            ApplyCameraState();
            return;
        }

        // The glance belongs to being under way: the mouse has the angles while anchored, and a
        // display point holds them. In each case it eases home rather than sticking where it was.
        EaseInteractLean(interactLeanEnabled && !IsAnchored && !IsViewHeld);

        // Under way the camera keeps its place behind the boat; anchored, the mouse has the
        // angles and only the distance is still eased. Held at a display point, the angles are
        // nobody's and simply stay put.
        EaseDownhillRise(!IsAnchored && !IsViewHeld);

        float zoomEase = lowAngleZoomSmoothing > 0f
            ? 1f - Mathf.Exp(-lowAngleZoomSmoothing * Time.deltaTime)
            : 1f;

        if (!IsAnchored && !IsViewHeld)
        {
            // Under way the low-angle zoom lets go, taking its follow offset with it.
            _lowAngleZoomIn = Mathf.Lerp(_lowAngleZoomIn, 0f, zoomEase);
            EaseTowardsFollowPose();
        }
        else
        {
            if (lowAngleZoomEnabled)
            {
                // The distance pulls in as the pitch drops, so the camera looks up from the boat
                // rather than going under the water surface. The follow offset moves with it.
                float zoomIn = LowAngleZoomIn();
                float target = Mathf.Lerp(TargetDistance, lowAngleZoomDistance, zoomIn);
                _currentDistance  = Mathf.Lerp(_currentDistance, target, zoomEase);
                _lowAngleZoomIn   = Mathf.Lerp(_lowAngleZoomIn, zoomIn, zoomEase);
                _distanceVelocity = 0f;
            }
            else if (followCatchUpTime > 0f)
            {
                _currentDistance = Mathf.SmoothDamp(_currentDistance, TargetDistance,
                                                    ref _distanceVelocity, followCatchUpTime);
            }
            else
            {
                _currentDistance = TargetDistance;
            }

            if (!lowAngleZoomEnabled)
                _lowAngleZoomIn = Mathf.Lerp(_lowAngleZoomIn, 0f, zoomEase);

            // Last word on pitch — depends on the distance and offset just settled above
            ClampCameraHeight();
        }

        // The follow offset may have moved with the low-angle zoom this frame.
        _orbitPivot.position = FollowPoint(_boatTarget.position);

        ApplyCameraState();
    }

    // How far in the low-angle zoom should be for the current pitch while turning about the boat:
    // 0 at the scroll zoom, 1 all the way in. Measured as a level from the boat, like the floor:
    // where the camera would sit in the normal shot at the scroll zoom. Above the start height it is
    // 0; below it the zoom goes in, reaching 1 at the height floor.
    private float LowAngleZoomIn()
    {
        float height = followTargetVerticalOffset + TargetDistance * Mathf.Sin(_pitch * Mathf.Deg2Rad);

        if (height >= lowAngleZoomStartHeight)
            return 0f;

        float span = lowAngleZoomStartHeight - heightFloor;
        if (span <= 0.001f) return 1f;

        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(heightFloor, lowAngleZoomStartHeight, height));
    }

    // Hard floor on how low the camera can sit. The orbit puts the camera at a height of
    // distance × sin(pitch) above the follow point, which sits CurrentFollowOffset above the
    // boat — so holding the floor means a minimum pitch of asin((floor − offset) / distance), which
    // automatically steepens as the zoom pulls the camera in. A floor below the boat is allowed.
    private void ClampCameraHeight()
    {
        if (!limitCameraHeight) return;

        float distance = Mathf.Max(_currentDistance, 0.001f);

        // Closer than the floor allows, no pitch can satisfy it — go fully overhead. Far enough
        // below that no pitch reaches it, the floor asks for nothing.
        float ratio      = Mathf.Clamp((heightFloor - CurrentFollowOffset) / distance, -1f, 1f);
        float floorPitch = Mathf.Asin(ratio) * Mathf.Rad2Deg;

        if (_pitch < floorPitch)
            _pitch = Mathf.Min(floorPitch, pitchMax);
    }

    private void Update()
    {
        if (cam == null || _orbitPivot == null || _boatTarget == null || !IsControlEnabled)
        {
            ReleaseCursor();
            return;
        }

        // Scroll zoom. The zoom is what is kept, so the resting shot returns to it rather than to
        // a distance that has forgotten the height it was paired with.
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) >= 0.01f)
        {
            float wanted = Mathf.Clamp(RestDistance * _zoom - scroll * zoomSpeed, minDistance, maxDistance);
            _zoom = wanted / RestDistance;
        }

        // Turning about the boat, offered only while it is anchored, and taken from where the
        // mouse is moved rather than from a button held down.
        if (IsAnchored)
        {
            CaptureCursor();

            _yaw   += Input.GetAxis("Mouse X") * orbitSpeed * Time.deltaTime;
            _pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * Time.deltaTime;
            _pitch  = Mathf.Clamp(_pitch, pitchMin, pitchMax);

            // Let the eases go, so the swing back in behind the boat starts from rest.
            _yawVelocity = _pitchVelocity = 0f;
        }
        else
        {
            ReleaseCursor();
        }
    }

    /// <summary>Holds the pointer in the middle of the screen and hides it, remembering how it was
    /// so the map's own cursor comes back untouched.</summary>
    private void CaptureCursor()
    {
        if (!lockCursorWhileOrbiting || _isOrbiting) return;

        _cursorLockBeforeOrbit    = Cursor.lockState;
        _cursorVisibleBeforeOrbit = Cursor.visible;
        _isOrbiting               = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void ReleaseCursor()
    {
        if (!_isOrbiting) return;

        Cursor.lockState = _cursorLockBeforeOrbit;
        Cursor.visible   = _cursorVisibleBeforeOrbit;
        _isOrbiting      = false;
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }

#if UNITY_EDITOR
    /// <summary>The resting shot the camera holds under way, shown from where the boat starts.</summary>
    public void EditorPreviewFollow()
    {
        float yaw = previewTarget != null ? previewTarget.eulerAngles.y : 0f;
        InternalEditorPreview(RestPitch, yaw, RestDistance * Mathf.Max(defaultZoom, 0.01f),
                              "Preview Camera Follow Position");

        // The lens is half of what the shot looks like, so the preview wears it too.
        if (cam != null && defaultVerticalFOV > 0f)
        {
            Undo.RecordObject(cam, "Preview Camera Follow Lens");
            ApplyDefaultLens();
        }
    }

    private void InternalEditorPreview(float p, float y, float d, string undoName)
    {
        if (cam == null) return;

        // Use the stored preview origin (boat's game-start world position) if set by spawner.
        // Fall back to the previewTarget transform, then search the scene.
        Vector3 pivot;
        if (previewOrigin != Vector3.zero)
        {
            pivot = previewOrigin;
        }
        else
        {
            Transform target = previewTarget;
            if (target == null)
            {
                var boat = Object.FindAnyObjectByType<LevelSelectBoatControl>();
                if (boat != null) target = boat.BoatTransform;
            }
            if (target == null || target == cam.transform)
            {
                Debug.LogWarning("[CameraController] No valid preview target found. Set 'Preview Origin' or assign the Boat to 'Preview Target'.");
                return;
            }
            pivot = target.position;
        }

        pivot = FollowPoint(pivot);

        Undo.RecordObject(cam.transform, undoName);

        d = Mathf.Max(d, 0.1f);
        Quaternion rotation = Quaternion.Euler(p, y, 0f);
        cam.transform.position = pivot + rotation * (Vector3.back * d);
        cam.transform.rotation = rotation;
    }
#endif
}
