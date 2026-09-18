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

    [Header("Follow Pose")]
[Tooltip("How far behind the boat the camera sits, along the ground.")]
[SerializeField] private float followDistance = 7.01f;
[Tooltip("How far above the boat it sits. Together with the distance this is the whole resting " +
         "shot — the camera looks straight at the boat from there.")]
[SerializeField] private float followHeight = 3f;
[Tooltip("The resting zoom. 1 is the shot exactly as the distance and height describe it; the scroll " +
         "wheel moves out from here, held in by Manual Min/Max Distance.")]
[SerializeField] private float defaultZoom = 1.74f;
[Tooltip("How long the camera takes to swing back in behind the boat. 0 pins it rigidly to the stern; " +
         "a little lets it lag through a turn and catch up after it.")]
[SerializeField] private float followCatchUpTime = 1f;
[Tooltip("Transform whose forward is the boat's heading. Leave blank to use BoatMovement's transform — " +
         "the follow target itself is usually a child that never rotates, so its forward is useless here.")]
[SerializeField] private Transform boatHeadingSource;

    [Header("Manual Orbit")]
[Tooltip("Player-driven camera: follows behind the boat under way, and the mouse turns it about the " +
         "boat while it is stopped with the anchor key. The scroll wheel moves it in and out. " +
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
[Tooltip("Lowest vertical angle while turning about the anchored boat. Negative looks up from below " +
         "the boat — the low-angle zoom exists to stop this dipping under the water surface.")]
[SerializeField] private float manualPitchMin = -60f;
[Tooltip("Highest vertical angle. Positive looks down on the boat.")]
[SerializeField] private float manualPitchMax = 45f;

[Header("Mouse Look")]
[Tooltip("While the boat is anchored, the mouse turns the camera with no button held and the cursor " +
         "is locked — press the free cursor key to release it and click on things. Off = hold Right " +
         "or Middle Mouse to turn. Under way the cursor is always free.")]
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
[Tooltip("Pulls the camera in as the pitch drops, so it never dips below the water surface. Only while " +
         "turning about the anchored boat — under way the follow pose is the shot.")]
[SerializeField] private bool lowAngleZoomEnabled = true;
[Tooltip("Pitch (degrees) below which the camera starts zooming in. Above this the manual zoom distance is used.")]
[SerializeField] private float lowAngleZoomPitchThreshold = 5f;
[Tooltip("Distance used once the pitch has dropped all the way to Manual Pitch Min.")]
[SerializeField] private float lowAngleZoomDistance = 3f;
[Tooltip("How quickly the zoom eases toward its target. Higher = snappier.")]
[SerializeField] private float lowAngleZoomSmoothing = 8f;

[Header("Height Floor")]
[Tooltip("Hard backstop so the camera can never drop below the boat, whatever combination of pitch " +
         "and zoom distance it ends up at. The low-angle zoom softens the approach; this guarantees it.")]
[SerializeField] private bool limitCameraHeight = true;
[Tooltip("Minimum height the camera must stay above the follow point, in world units. The pitch is " +
         "raised as far as needed to hold this — and the closer the camera is, the steeper that has to be.")]
[SerializeField] private float minHeightAboveBoat = 0.5f;

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
// The scroll wheel's zoom on the resting shot. Kept as a zoom rather than a distance, so the shot
// returns to it without forgetting the height it was paired with.
private float _zoom = 1f;

// Distance actually used by the camera — the scroll distance, eased toward the low-angle zoom while
// anchored.
private float _currentDistance = 9f;

private float _yawVelocity;
private float _pitchVelocity;
private float _distanceVelocity;

private bool _cursorFree = false;
private bool _cursorStateApplied = false;
private bool _lastCursorLocked = false;

private DepthOfField _dof;
private bool  _dofResolved = false;
private float _dofOriginalStart;

private bool  _sonarView = false;
private bool  _restoringPitch = false;
private float _pitchBeforeSonar;

private BoatMovement _boatMovement;

// Pitch is driven by sonar rather than the mouse.
private bool PitchLocked => lockPitchInSonar && _sonarView;

// The resting shot as an angle and a length down the view ray, so the one piece of orbit maths
// carries both the follow and the turning about the boat.
private float RestPitch    => Mathf.Atan2(followHeight, Mathf.Max(followDistance, 0.001f)) * Mathf.Rad2Deg;
private float RestDistance => Mathf.Max(new Vector2(followDistance, followHeight).magnitude, 0.001f);

// How far out the camera wants to be: the resting shot at the current zoom, held inside the limits.
private float ScrollDistance => Mathf.Clamp(RestDistance * _zoom, manualMinDistance, manualMaxDistance);

// The boat is held still with the anchor key, and turning about it is offered. Not while a
// conversation has taken the anchor — the camera is on someone else then.
private bool IsAnchored => BoatAnchor.Instance != null && BoatAnchor.Instance.HeldByPlayer;

// Cursor is only captured while the mouse is actually driving the camera.
private bool WantCursorLocked =>
    mouseLookEnabled && manualOrbitActive && IsAnchored && !_cursorFree && !PauseManager.IsPaused;

// =====================================================
// UNITY
// =====================================================

private void Awake()
{
    Instance = this;
    _zoom            = Mathf.Max(defaultZoom, 0.01f);
    _currentDistance = ScrollDistance;

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
        float wanted = Mathf.Clamp(ScrollDistance - scroll * manualZoomSpeed, manualMinDistance, manualMaxDistance);
        _zoom = wanted / RestDistance;
    }

    // Turning about the boat is offered only while it is anchored. Under way the camera keeps its
    // place behind the stern (LateUpdate).
    if (!IsAnchored) return;

    // Orbit — mouse movement alone when mouse look is on, otherwise the old Right/Middle Mouse drag
    bool orbitFromMouse = mouseLookEnabled
        ? (!paused && !_cursorFree)
        : (!paused && (Input.GetMouseButton(1) || Input.GetMouseButton(2)));

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

        // Let the follow's eases go, so the swing back in behind the boat starts from rest.
        _yawVelocity = _pitchVelocity = 0f;
    }
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
// FOLLOW
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

// The resting shot: dead behind the boat, at the authored height and zoom. The angles are eased
// rather than set, so a turn leaves the camera trailing the stern for a moment and it comes round
// after — the catching up that gives a turning its weight. Sonar's pitch stands in for the resting
// one while it runs.
private void EaseTowardsFollowPose()
{
    float targetYaw = _manualYaw;

    Transform heading = HeadingSource;
    if (heading != null)
    {
        // Behind the boat = looking the way the boat faces
        Vector3 forward = heading.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 1e-6f)
            targetYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    float targetPitch = Mathf.Clamp(PitchLocked ? sonarPitch : RestPitch, manualPitchMin, manualPitchMax);

    if (followCatchUpTime <= 0f)
    {
        _manualYaw       = targetYaw;
        _manualPitch     = targetPitch;
        _currentDistance = ScrollDistance;
        return;
    }

    _manualYaw       = Mathf.SmoothDampAngle(_manualYaw, targetYaw, ref _yawVelocity, followCatchUpTime);
    _manualPitch     = Mathf.SmoothDampAngle(_manualPitch, targetPitch, ref _pitchVelocity, followCatchUpTime);
    _currentDistance = Mathf.SmoothDamp(_currentDistance, ScrollDistance, ref _distanceVelocity, followCatchUpTime);
}

// Hard floor on how low the camera can sit. The rig puts the camera at a height of
// distance × sin(pitch) above the follow point, so holding a minimum height means a minimum pitch of
// asin(minHeight / distance) — which automatically steepens as the zoom pulls the camera in.
private void ClampCameraHeight()
{
    if (!limitCameraHeight || minHeightAboveBoat <= 0f) return;

    float distance = Mathf.Max(_currentDistance, 0.001f);

    // Closer than the minimum height, no pitch can satisfy it — go fully overhead.
    float ratio     = Mathf.Clamp(minHeightAboveBoat / distance, -1f, 1f);
    float floorPitch = Mathf.Asin(ratio) * Mathf.Rad2Deg;

    if (_manualPitch < floorPitch)
        _manualPitch = Mathf.Min(floorPitch, manualPitchMax);
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
        return ScrollDistance;

    float span = lowAngleZoomPitchThreshold - manualPitchMin;
    if (span <= 0.001f) return lowAngleZoomDistance;

    float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(manualPitchMin, lowAngleZoomPitchThreshold, _manualPitch));
    return Mathf.Lerp(lowAngleZoomDistance, ScrollDistance, t);
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
        // Eased here rather than in Update, so the follow reads where the boat got to this frame.
        if (IsAnchored)
        {
            // The mouse has the angles (Update); sonar may hold the pitch, and the distance pulls in
            // as the pitch drops so the camera never goes under the water surface.
            UpdateSonarPitch();

            float target = TargetDistance();
            _currentDistance  = lowAngleZoomSmoothing > 0f
                ? Mathf.Lerp(_currentDistance, target, 1f - Mathf.Exp(-lowAngleZoomSmoothing * Time.deltaTime))
                : target;
            _distanceVelocity = 0f;
        }
        else
        {
            // Under way the camera keeps its place behind the boat.
            _restoringPitch = false;
            EaseTowardsFollowPose();
        }

        // Last word on pitch — depends on the distance just settled above
        ClampCameraHeight();

        ApplyZoomDepthOfField();

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

        _zoom            = Mathf.Max(defaultZoom, 0.01f);
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
    _boatMovement = null;

    if (boatTarget != null && boatFollowCam != null && (manualOrbitActive || initializing))
    {
        // Derive starting angle from the camera's current world position relative to target
        Vector3 offset = boatFollowCam.transform.position - boatTarget.position;
        Vector3 dir = offset.normalized;
        if (dir == Vector3.zero) dir = Vector3.back;

        _manualYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        _manualPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        _manualPitch = Mathf.Clamp(_manualPitch, manualPitchMin, manualPitchMax);
        // Starts from where the camera was placed and eases in behind the boat from there.
        _zoom            = Mathf.Max(defaultZoom, 0.01f);
        _currentDistance = Mathf.Clamp(offset.magnitude, manualMinDistance, manualMaxDistance);
        _yawVelocity = _pitchVelocity = _distanceVelocity = 0f;
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
        sb.AppendLine($"Zoom: {_zoom}  (Scroll Distance: {ScrollDistance})");
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