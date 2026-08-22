using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Text;

public class CameraController : MonoBehaviour
{
    public static CameraController Instance;

    [Header("Default Camera")]
    [Tooltip("The camera that follows the boat. Manual orbit drives this one's transform directly.")]
    [SerializeField] private CinemachineCamera boatFollowCam;

    [Header("Orbital Camera")]
    [Tooltip("Optional camera that circles the level centre instead of the boat. Only used by a " +
             "CameraProfile with Use Orbital Cam ticked; disabled otherwise.")]
    [SerializeField] private CinemachineCamera orbitalCam;
    private CinemachineOrbitalFollow orbital;

    [Header("Runtime Targets")]
    [Tooltip("The boat being framed. Assigned at level spawn via SetTargets — usually leave blank.")]
    [SerializeField] private Transform boatTarget;
    [Tooltip("Point the orbital camera circles. Resolved from the LevelCenter-tagged object.")]
    [SerializeField] private Transform orbitalCenter;

    [Header("Follow Offset")]
    [Tooltip("Vertical offset applied to the boat follow target. Positive = above the boat, negative = below. " +
             "This is the offset used at the reference distance below.")]
    [SerializeField] private float followVerticalOffset = 0f;

    [Tooltip("Scales the offset with the camera's distance so the boat keeps the same position on " +
             "SCREEN as you zoom. Without this a fixed world offset pushes the boat further out of " +
             "frame the closer the camera gets.")]
    [SerializeField] private bool scaleFollowOffsetWithZoom = true;

    [Tooltip("Distance at which Follow Vertical Offset is used exactly as entered. Closer than this " +
             "the offset shrinks, further out it grows.")]
    [SerializeField] private float followOffsetReferenceDistance = 9f;

    [Tooltip("How much of that scaling to apply. 1 = the boat holds its screen position exactly, " +
             "0 = the old fixed world offset. Drop below 1 if the correction overshoots.")]
    [Range(0f, 1f)] [SerializeField] private float followOffsetZoomAmount = 1f;

    // The offset subtends atan(offset / distance) on screen, so holding the boat at a constant screen
    // position means scaling the offset with distance. Works for a negative offset too — it shrinks
    // toward zero as the camera closes in, rather than flipping.
    private float CurrentFollowOffset
    {
        get
        {
            if (!scaleFollowOffsetWithZoom || followOffsetReferenceDistance <= 0.001f)
                return followVerticalOffset;

            float scaled = followVerticalOffset * (_currentDistance / followOffsetReferenceDistance);
            return Mathf.Lerp(followVerticalOffset, scaled, followOffsetZoomAmount);
        }
    }

    // Boat follow target position with the vertical offset applied.
    private Vector3 BoatFollowPoint => boatTarget.position + Vector3.up * CurrentFollowOffset;

    private CameraProfile activeProfile;
    private bool orbitalActive = false;

    [Header("Manual Orbit")]
[Tooltip("Player-driven camera: the mouse orbits the boat and the scroll wheel moves it in and out. " +
         "Off means the camera is left to Cinemachine / the level's CameraProfile.")]
[SerializeField] private bool manualOrbitActive = false;
[Tooltip("Base rotation speed in degrees per second of mouse travel, before Mouse Sensitivity is " +
         "applied. Treat this as the fixed baseline and tune the sensitivity slider instead.")]
[SerializeField] private float manualOrbitSpeed = 250f;
[Tooltip("World units the camera moves in or out per notch of the scroll wheel.")]
[SerializeField] private float manualZoomSpeed = 5f;
[Tooltip("Closest the scroll wheel can bring the camera. The low-angle zoom can go closer than this.")]
[SerializeField] private float manualMinDistance = 4f;
[Tooltip("Furthest the scroll wheel can push the camera out.")]
[SerializeField] private float manualMaxDistance = 16f;
[Tooltip("Lowest vertical angle. Negative looks up from below the boat — the low-angle zoom exists " +
         "to stop this dipping under the water surface.")]
[SerializeField] private float manualPitchMin = -60f;
[Tooltip("Highest vertical angle. Positive looks down on the boat.")]
[SerializeField] private float manualPitchMax = 45f;

[Header("Mouse Look")]
[Tooltip("Mouse moves the camera with no button held. The cursor is locked while this is on — " +
         "press the free cursor key to release it and click on things.")]
[SerializeField] private bool mouseLookEnabled = true;
[Tooltip("Toggles the cursor between locked (camera control) and free (clicking).")]
[SerializeField] private KeyCode freeCursorKey = KeyCode.I;
[Tooltip("Overall mouse sensitivity, multiplying Manual Orbit Speed. 1 = the base speed. " +
         "Safe to drag while playing — the change is immediate.")]
[Range(0.05f, 4f)] [SerializeField] private float mouseSensitivity = 1f;
[Tooltip("Extra multiplier on horizontal turning only, on top of Mouse Sensitivity.")]
[Range(0.05f, 4f)] [SerializeField] private float horizontalSensitivity = 1f;
[Tooltip("Extra multiplier on vertical turning only, on top of Mouse Sensitivity. Lower than the " +
         "horizontal value keeps the pitch calm while still allowing quick turns.")]
[Range(0.05f, 4f)] [SerializeField] private float verticalSensitivity = 1f;
[Tooltip("Flips the vertical direction — push the mouse forward to look up instead of down.")]
[SerializeField] private bool invertMouseY = false;

[Header("Low Angle Zoom")]
[Tooltip("Pulls the camera in as the pitch drops, so it never dips below the water surface.")]
[SerializeField] private bool lowAngleZoomEnabled = true;
[Tooltip("Pitch (degrees) below which the camera starts zooming in. Above this the manual zoom distance is used.")]
[SerializeField] private float lowAngleZoomPitchThreshold = 5f;
[Tooltip("Distance used once the pitch has dropped all the way to Manual Pitch Min.")]
[SerializeField] private float lowAngleZoomDistance = 3f;
[Tooltip("How quickly the zoom eases toward its target. Higher = snappier.")]
[SerializeField] private float lowAngleZoomSmoothing = 8f;

[Header("Auto Follow Behind Boat")]
[Tooltip("After the player stops turning, the camera drifts round to sit behind the boat while it " +
         "is moving. Any mouse rotation cancels it and restarts the delay.")]
[SerializeField] private bool autoFollowBehindBoat = true;
[Tooltip("Seconds of no mouse rotation before the camera starts drifting round.")]
[SerializeField] private float autoFollowDelay = 2f;
[Tooltip("Degrees per second the camera turns while catching up. Keep it low — this should be a " +
         "drift the player barely notices, not a snap.")]
[SerializeField] private float autoFollowSpeed = 25f;
[Tooltip("How fast the boat must be moving (world units per second) for the drift to engage. " +
         "A stationary boat never pulls the camera round.")]
[SerializeField] private float autoFollowMinBoatSpeed = 0.5f;
[Tooltip("Transform whose forward is the boat's heading. Leave blank to use BoatMovement's transform — " +
         "the follow target itself is usually a child that never rotates, so its forward is useless here.")]
[SerializeField] private Transform boatHeadingSource;

[Header("Sonar View")]
[Tooltip("While sonar is active the pitch is held at Sonar Pitch and the mouse only turns horizontally.")]
[SerializeField] private bool lockPitchInSonar = true;
[Tooltip("Vertical angle the camera settles at during sonar. Positive = looking down.")]
[SerializeField] private float sonarPitch = 20f;
[Tooltip("How quickly the pitch eases into and out of the sonar angle. Higher = snappier.")]
[SerializeField] private float sonarPitchSmoothing = 6f;

[Header("Zoom Depth Of Field")]
[Tooltip("Drives the Gaussian DoF start distance from the camera's current zoom distance.")]
[SerializeField] private bool zoomDrivesDepthOfField = true;
[Tooltip("Volume holding the Depth Of Field override. Leave blank to use the Cinemachine volume below.")]
[SerializeField] private Volume depthOfFieldVolume;
[Tooltip("Used only if the Volume above is blank — the Cinemachine Volume Settings holding the " +
         "Depth Of Field override. Note this writes to the profile asset, not a runtime copy.")]
[SerializeField] private CinemachineVolumeSettings depthOfFieldCinemachineVolume;
[Tooltip("Gaussian Start at the closest zoom (the low-angle zoom distance).")]
[SerializeField] private float dofStartWhenClosest = 10f;
[Tooltip("Gaussian Start at full zoom out (Manual Max Distance).")]
[SerializeField] private float dofStartWhenFurthest = 20f;

private float _manualYaw = -128.6533f;
private float _manualPitch = 44.56071f;
private float _manualDistance = 9f;

// Distance actually used by the camera — _manualDistance eased toward the low-angle zoom.
private float _currentDistance = 9f;

private bool _cursorFree = false;
private bool _cursorStateApplied = false;
private bool _lastCursorLocked = false;

private DepthOfField _dof;
private bool  _dofResolved = false;
private float _dofOriginalStart;

private bool  _sonarView = false;
private bool  _restoringPitch = false;
private float _pitchBeforeSonar;

private float   _lastRotateInputTime = -999f;
private Vector3 _lastBoatPosition;
private bool    _hasLastBoatPosition = false;
private BoatMovement _boatMovement;

// Pitch is driven by sonar rather than the mouse.
private bool PitchLocked => lockPitchInSonar && _sonarView;

// Cursor is only captured while the mouse is actually driving the camera.
private bool WantCursorLocked =>
    mouseLookEnabled && manualOrbitActive && !_cursorFree && !PauseManager.IsPaused;

// =====================================================
// UNITY
// =====================================================

private void Awake()
{
    Instance = this;
    _currentDistance = _manualDistance;

    if (orbitalCam != null)
    {
        orbital = orbitalCam.GetCinemachineComponent(CinemachineCore.Stage.Body) 
                  as CinemachineOrbitalFollow;

        if (orbital == null)
            Debug.LogWarning("[CameraController] OrbitalCam has no CinemachineOrbitalFollow in Body stage.");

        orbitalCam.gameObject.SetActive(false);
    }
}

private void OnValidate()
{
    // This ensures the mode is correctly initialized if changed in inspector while playing
    if (Application.isPlaying && Instance == this)
    {
        SetManualOrbit(manualOrbitActive);
    }
}

private void Update()
{
    if (Input.GetKeyDown(freeCursorKey) && !PauseManager.IsPaused)
        SetCursorFree(!_cursorFree);

    ApplyCursorState();

    if (!manualOrbitActive || boatTarget == null) return;

    bool paused = PauseManager.IsPaused;

    // Scroll zoom
    float scroll = Input.GetAxis("Mouse ScrollWheel");
    if (!paused && Mathf.Abs(scroll) >= 0.01f)
    {
        _manualDistance = Mathf.Clamp(_manualDistance - scroll * manualZoomSpeed, manualMinDistance, manualMaxDistance);
    }

    // Orbit — mouse movement alone when mouse look is on, otherwise the old Right/Middle Mouse drag
    bool orbitFromMouse = mouseLookEnabled
        ? (!paused && !_cursorFree)
        : (Input.GetMouseButton(1) || Input.GetMouseButton(2));

    if (orbitFromMouse)
    {
        float speed   = manualOrbitSpeed * mouseSensitivity * Time.deltaTime;
        float yawInput = Input.GetAxis("Mouse X");

        _manualYaw += yawInput * speed * horizontalSensitivity;

        // Sonar holds the vertical angle — horizontal turning only
        float pitchInput = 0f;
        if (!PitchLocked)
        {
            pitchInput = Input.GetAxis("Mouse Y") * (invertMouseY ? 1f : -1f);
            if (Mathf.Abs(pitchInput) > 0.0001f) _restoringPitch = false;

            _manualPitch += pitchInput * speed * verticalSensitivity;
            _manualPitch  = Mathf.Clamp(_manualPitch, manualPitchMin, manualPitchMax);
        }

        // Any deliberate turn hands control back to the player and restarts the auto-follow delay
        if (Mathf.Abs(yawInput) > 0.001f || Mathf.Abs(pitchInput) > 0.001f)
            _lastRotateInputTime = Time.time;
    }

    UpdateAutoFollow();
    UpdateSonarPitch();

    // Ease toward the pitch-driven distance so the camera never drops under the water surface
    float target = TargetDistance();
    _currentDistance = lowAngleZoomSmoothing > 0f
        ? Mathf.Lerp(_currentDistance, target, 1f - Mathf.Exp(-lowAngleZoomSmoothing * Time.deltaTime))
        : target;

    ApplyZoomDepthOfField();
}

// =====================================================
// DEPTH OF FIELD
// =====================================================

// Gaussian Start pushes further out as the camera zooms out, so the blur stays behind the boat
// instead of creeping onto it when pulled in close.
private void ApplyZoomDepthOfField()
{
    if (!zoomDrivesDepthOfField) return;

    DepthOfField dof = ResolveDepthOfField();
    if (dof == null) return;

    float closest  = lowAngleZoomEnabled ? Mathf.Min(lowAngleZoomDistance, manualMinDistance) : manualMinDistance;
    float furthest = manualMaxDistance;

    float t = furthest - closest <= 0.001f
        ? 1f
        : Mathf.InverseLerp(closest, furthest, _currentDistance);

    dof.gaussianStart.value = Mathf.Lerp(dofStartWhenClosest, dofStartWhenFurthest, t);
}

private DepthOfField ResolveDepthOfField()
{
    if (_dofResolved) return _dof;
    _dofResolved = true;

    VolumeProfile profile = null;

    if (depthOfFieldVolume != null)
        profile = depthOfFieldVolume.profile;          // runtime copy — safe to write to
    else if (depthOfFieldCinemachineVolume != null)
        profile = depthOfFieldCinemachineVolume.Profile;

    if (profile != null)
        profile.TryGet(out _dof);

    if (_dof == null)
        Debug.LogWarning("[CameraController] Zoom depth of field is on but no Depth Of Field override was found — assign a Volume or Cinemachine Volume Settings.");
    else
        _dofOriginalStart = _dof.gaussianStart.value;

    return _dof;
}

private void OnDisable()
{
    // Restore, so a Cinemachine profile asset isn't left holding a runtime value
    if (_dof != null)
        _dof.gaussianStart.value = _dofOriginalStart;
}

// =====================================================
// AUTO FOLLOW
// =====================================================

// The transform that actually turns. The camera's follow target is typically a child that holds a
// fixed local rotation while its parent steers, so reading forward off boatTarget gives a heading
// that never changes — take it from BoatMovement instead.
private Transform HeadingSource
{
    get
    {
        if (boatHeadingSource != null) return boatHeadingSource;

        BoatMovement movement = ResolveBoatMovement();
        return movement != null ? movement.transform : boatTarget;
    }
}

private BoatMovement ResolveBoatMovement()
{
    if (_boatMovement != null) return _boatMovement;

    if (LevelDataController.Instance != null)
        _boatMovement = LevelDataController.Instance.GetBoatMovement();

    if (_boatMovement == null && boatTarget != null)
        _boatMovement = boatTarget.GetComponentInParent<BoatMovement>();

    if (_boatMovement == null && boatTarget != null)
        _boatMovement = boatTarget.GetComponentInChildren<BoatMovement>();

    return _boatMovement;
}

// Drifts the yaw round to sit behind the boat once the player has stopped turning and the boat is
// actually under way. A stationary boat, or one only bobbing on the waves, never drags the camera.
private void UpdateAutoFollow()
{
    if (boatTarget == null) return;

    BoatMovement movement = ResolveBoatMovement();

    // Prefer the movement script's own speed; fall back to how far the target actually travelled.
    float boatSpeed;
    if (movement != null)
    {
        boatSpeed = Mathf.Abs(movement.CurrentSpeed);
        _hasLastBoatPosition = false;
    }
    else
    {
        Vector3 position = boatTarget.position;
        boatSpeed = 0f;

        if (_hasLastBoatPosition && Time.deltaTime > 0f)
        {
            Vector3 travel = position - _lastBoatPosition;
            travel.y = 0f;                               // heaving on the waves is not travel
            boatSpeed = travel.magnitude / Time.deltaTime;
        }

        _lastBoatPosition    = position;
        _hasLastBoatPosition = true;
    }

    if (!autoFollowBehindBoat) return;
    if (boatSpeed < autoFollowMinBoatSpeed) return;
    if (Time.time - _lastRotateInputTime < autoFollowDelay) return;

    Transform heading = HeadingSource;
    if (heading == null) return;

    // Behind the boat = looking the way the boat faces
    Vector3 forward = heading.forward;
    forward.y = 0f;
    if (forward.sqrMagnitude < 1e-6f) return;

    float targetYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    _manualYaw = Mathf.MoveTowardsAngle(_manualYaw, targetYaw, autoFollowSpeed * Time.deltaTime);
}

// =====================================================
// SONAR VIEW
// =====================================================

// Called by SonarSystemController as sonar starts and ends.
public void SetSonarView(bool active)
{
    if (active == _sonarView) return;

    _sonarView = active;

    if (active)
    {
        _pitchBeforeSonar = _manualPitch;
        _restoringPitch   = false;
    }
    else
    {
        // Ease back to wherever the player had it, unless they move the mouse vertically first
        _restoringPitch = lockPitchInSonar;
    }
}

private void UpdateSonarPitch()
{
    if (PitchLocked)
    {
        float targetPitch = Mathf.Clamp(sonarPitch, manualPitchMin, manualPitchMax);
        _manualPitch = Damp(_manualPitch, targetPitch, sonarPitchSmoothing);
        return;
    }

    if (!_restoringPitch) return;

    _manualPitch = Damp(_manualPitch, _pitchBeforeSonar, sonarPitchSmoothing);

    if (Mathf.Abs(_manualPitch - _pitchBeforeSonar) < 0.05f)
    {
        _manualPitch    = _pitchBeforeSonar;
        _restoringPitch = false;
    }
}

private static float Damp(float from, float to, float smoothing)
{
    if (smoothing <= 0f) return to;
    return Mathf.Lerp(from, to, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
}

// Distance the camera should sit at for the current pitch. Above the threshold this is just the
// manual (scroll) distance; below it the camera pulls in toward lowAngleZoomDistance, reaching it
// at manualPitchMin.
private float TargetDistance()
{
    if (!lowAngleZoomEnabled || _manualPitch >= lowAngleZoomPitchThreshold)
        return _manualDistance;

    float span = lowAngleZoomPitchThreshold - manualPitchMin;
    if (span <= 0.001f) return lowAngleZoomDistance;

    float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(manualPitchMin, lowAngleZoomPitchThreshold, _manualPitch));
    return Mathf.Lerp(lowAngleZoomDistance, _manualDistance, t);
}

// =====================================================
// CURSOR
// =====================================================

public void SetCursorFree(bool free)
{
    _cursorFree = free;
    ApplyCursorState();
}

private void ApplyCursorState()
{
    bool locked = WantCursorLocked;
    if (_cursorStateApplied && locked == _lastCursorLocked) return;

    Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
    Cursor.visible   = !locked;

    _lastCursorLocked   = locked;
    _cursorStateApplied = true;
}

private void LateUpdate()
{
    if (manualOrbitActive && boatTarget != null && boatFollowCam != null)
    {
        Quaternion rotation = Quaternion.Euler(_manualPitch, _manualYaw, 0f);
        boatFollowCam.transform.position = BoatFollowPoint + rotation * (Vector3.back * _currentDistance);
        boatFollowCam.transform.rotation = rotation;
        return;
    }

    if (!orbitalActive || orbital == null || boatTarget == null || orbitalCenter == null)
        return;

        Vector3 direction = boatTarget.position - orbitalCenter.position;
        float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + 180f;

        orbital.HorizontalAxis.Value = Mathf.LerpAngle(
            orbital.HorizontalAxis.Value,
            targetAngle,
            Time.deltaTime * (1f / Mathf.Max(activeProfile.dampingX, 0.001f))
        );
    }

    // =====================================================
    // PUBLIC API
    // =====================================================

    // The camera transform currently framing the boat — manual orbit and the default follow both
    // drive boatFollowCam; only a profile with an orbital cam swaps it. Used by things that need the
    // eye position (e.g. CameraOccluderFader's camera-to-boat ray).
    public Transform ActiveCameraTransform
    {
        get
        {
            if (orbitalActive && !manualOrbitActive && orbitalCam != null)
                return orbitalCam.transform;

            return boatFollowCam != null ? boatFollowCam.transform : null;
        }
    }

    /// <summary>The boat the camera is framing.</summary>
    public Transform BoatTarget => boatTarget;

    public void SetManualOrbit(bool active)
    {
        manualOrbitActive = active;
        if (active && boatFollowCam != null)
        {
            boatFollowCam.gameObject.SetActive(true);
        }
        
        Debug.Log($"[CameraController] Manual Orbit: {active}");
    }

#if UNITY_EDITOR
    [ContextMenu("Editor Preview Manual Orbit")]
    public void EditorPreviewManualOrbit()
    {
        if (boatTarget == null)
        {
            var boat = Object.FindAnyObjectByType<BoatMovement>();
            if (boat != null) boatTarget = boat.transform;
        }

        if (boatTarget == null)
        {
            Debug.LogWarning("[CameraController] No boatTarget found for preview.");
            return;
        }

        if (boatFollowCam == null)
        {
            Debug.LogWarning("[CameraController] No boatFollowCam found for preview.");
            return;
        }

        UnityEditor.Undo.RecordObject(boatFollowCam.transform, "Preview Manual Orbit");

        _currentDistance = TargetDistance();

        Quaternion rotation = Quaternion.Euler(_manualPitch, _manualYaw, 0f);
        boatFollowCam.transform.position = BoatFollowPoint + rotation * (Vector3.back * _currentDistance);
        boatFollowCam.transform.rotation = rotation;
    }
#endif

    public void ApplyProfile(CameraProfile profile)
{
        if (profile == null) return;

        activeProfile = profile;

        if (!profile.useOrbitalCam)
        {
            DisableOrbital();
            return;
        }

        // Resolve center from scene
        GameObject center = GameObject.FindGameObjectWithTag("LevelCenter");
        if (center == null)
        {
            Debug.LogWarning("[CameraController] No GameObject tagged LevelCenter found.");
            return;
        }

        orbitalCenter = center.transform;

        if (orbitalCam == null)
        {
            Debug.LogError("[CameraController] OrbitalCam reference is not assigned.");
            return;
        }

        // Apply profile settings
        if (orbital != null)
        {
            orbital.Radius = profile.orbitRadius;
            orbital.TargetOffset = new Vector3(0f, profile.height, 0f);
        }

        var lens = orbitalCam.Lens;
        lens.FieldOfView = profile.fieldOfView;
        lens.Dutch = profile.dutch;
        orbitalCam.Lens = lens;

        orbitalCam.Follow = orbitalCenter;
        orbitalCam.LookAt = orbitalCenter;

        orbitalCam.gameObject.SetActive(true);
        orbitalActive = true;

        Debug.Log("[CameraController] OrbitalCam activated.");
    }

public void SetTargets(Transform newCenter, Transform newBoat)
{
    bool initializing = boatTarget == null;
    orbitalCenter = newCenter;
    boatTarget = newBoat;

    // New boat — re-resolve the heading/speed source next frame
    _boatMovement        = null;
    _hasLastBoatPosition = false;

    if (boatTarget != null && boatFollowCam != null && (manualOrbitActive || initializing))
    {
        // Derive starting angle from the camera's current world position relative to target
        Vector3 offset = boatFollowCam.transform.position - boatTarget.position;
        Vector3 dir = offset.normalized;
        if (dir == Vector3.zero) dir = Vector3.back;

        _manualYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        _manualPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        _manualPitch = Mathf.Clamp(_manualPitch, manualPitchMin, manualPitchMax);
        _manualDistance = Mathf.Clamp(offset.magnitude, manualMinDistance, manualMaxDistance);
        _currentDistance = TargetDistance();
    }

    // Assign the correct camera to BoatCameraZoom
    if (newBoat != null)
    {
        var zoomController = newBoat.GetComponentInChildren<BoatCameraZoom>();
        if (zoomController != null)
        {
            // Orbital active = use orbitalCam, otherwise use boatFollowCam
            zoomController.AssignCamera(orbitalActive ? orbitalCam : boatFollowCam);
        }
    }
}

    public void DisableOrbital()
    {
        orbitalActive = false;

        if (orbitalCam != null)
            orbitalCam.gameObject.SetActive(false);

        Debug.Log("[CameraController] OrbitalCam disabled.");
    }

    [ContextMenu("Log Camera Snapshot")]
    public void LogCameraSnapshot()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b>[Camera Snapshot]</b> Copy these values for your defaults:");
        sb.AppendLine($"Manual Yaw: {_manualYaw}");
        sb.AppendLine($"Manual Pitch: {_manualPitch}");
        sb.AppendLine($"Manual Distance: {_manualDistance}");
        sb.AppendLine($"Current Distance (after low-angle zoom): {_currentDistance}");
        sb.AppendLine($"Current Follow Offset (after zoom scaling): {CurrentFollowOffset}");

        if (boatFollowCam != null)
        {
            sb.AppendLine($"World Position: {boatFollowCam.transform.position}");
            sb.AppendLine($"World Rotation: {boatFollowCam.transform.eulerAngles}");
            sb.AppendLine($"Current FOV: {boatFollowCam.Lens.FieldOfView}");
        }

        var zoom = Object.FindAnyObjectByType<BoatCameraZoom>();
        if (zoom != null && boatFollowCam != null)
        {
            sb.AppendLine($"BoatCameraZoom Base FOV: {boatFollowCam.Lens.FieldOfView}");
        }

        Debug.Log(sb.ToString());
    }
}