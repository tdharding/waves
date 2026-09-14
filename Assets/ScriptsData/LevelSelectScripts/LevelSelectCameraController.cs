using UnityEngine;
using Unity.Cinemachine;
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
    [Tooltip("The resting zoom. 1 is the shot exactly as the distance and height describe it; " +
             "the scroll wheel moves out from here, held in by the distance limits below.")]
    public float defaultZoom = 1f;
    [Tooltip("How long the camera takes to swing back in behind the boat. 0 pins it rigidly to " +
             "the stern; a little lets it lag through a turn and catch up after it.")]
    public float followCatchUpTime = 0.35f;
    [Tooltip("The lens the camera rests at — its vertical field of view, in degrees. Pushed to " +
             "the Cinemachine camera when the boat is wired. 0 leaves the lens as authored.")]
    public float defaultVerticalFOV = 16.2f;

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

    [Header("Transition Target")]
    public float transitionYaw      = 0f;
    public float transitionPitch    = 20f;
    public float transitionDistance = 10f;
    public float transitionDuration = 2f;

    [Header("Starting State")]
    public bool useManualStartingState = false;
    public float manualStartYaw      = 0f;
    public float manualStartPitch    = 20f;
    public float manualStartDistance = 8f;

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

    private bool           _isOrbiting;
    private CursorLockMode _cursorLockBeforeOrbit;
    private bool           _cursorVisibleBeforeOrbit;

    private Quaternion _introRelativeRotation;
    private bool       _isTransitioning;

    /// <summary>The resting shot as an angle and a length down the view ray, so the one piece of
    /// orbit maths carries both the follow and the turning about the boat.</summary>
    private float RestPitch    => Mathf.Atan2(followHeight, Mathf.Max(followDistance, 0.001f)) * Mathf.Rad2Deg;
    private float RestDistance => Mathf.Max(new Vector2(followDistance, followHeight).magnitude, 0.001f);

    /// <summary>How far out the camera wants to be: the resting shot taken at the current zoom,
    /// held inside the distance the map allows.</summary>
    private float TargetDistance => Mathf.Clamp(RestDistance * _zoom, minDistance, maxDistance);

    /// <summary>The boat is held still with Q, and turning about it is offered.</summary>
    private bool IsAnchored => boatControl != null && boatControl.HeldByPlayer;

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

            _orbitPivot.position = _boatTarget.position;

            if (useManualStartingState)
            {
                _yaw             = manualStartYaw;
                _pitch           = manualStartPitch;
                _currentDistance = manualStartDistance;
                Debug.Log($"[CameraController] SetFollowTarget: MANUAL start — yaw={_yaw}, pitch={_pitch}, dist={_currentDistance}");
            }
            else
            {
                Vector3 offset = cam.transform.position - _boatTarget.position;
                Vector3 dir    = offset.normalized;
                if (dir == Vector3.zero) dir = Vector3.back;

                _yaw             = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                _pitch           = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
                _pitch           = Mathf.Clamp(_pitch, pitchMin, pitchMax);
                _currentDistance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);
                Debug.Log($"[CameraController] SetFollowTarget: DERIVED start — camPos={cam.transform.position}, boatPos={_boatTarget.position}, offset={offset}, yaw={_yaw:F2}, pitch={_pitch:F2}, dist={_currentDistance:F2}");
            }

            _yawVelocity = _pitchVelocity = _distanceVelocity = 0f;

            _orbitPivot.rotation       = Quaternion.Euler(_pitch, _yaw, 0f);
            _introRelativeRotation     = Quaternion.Inverse(_boatTarget.rotation) * _orbitPivot.rotation;

            ApplyCameraState();
            Debug.Log($"[CameraController] SetFollowTarget: final cam position={cam.transform.position}, rotation={cam.transform.eulerAngles}");
        }
        else
        {
            if (_boatTarget == null) Debug.LogWarning("[CameraController] SetFollowTarget: target is NULL — camera will not initialise.");
            if (cam == null)         Debug.LogWarning("[CameraController] SetFollowTarget: 'cam' field is NULL — assign the CinemachineCamera.");
        }
    }

    // Called by LevelSelectOpeningSequence when the intro trigger fires
    public void TransitionToFollow()
    {
        if (_isTransitioning) return;
        StartCoroutine(DoTransition());
    }

    private System.Collections.IEnumerator DoTransition()
    {
        _isTransitioning = true;
        IsControlEnabled = false;
        ReleaseCursor();

        // Current world angles are derived from current pivot rotation
        Vector3 currentEuler = _orbitPivot.rotation.eulerAngles;
        float startYaw   = currentEuler.y;
        float startPitch = currentEuler.x;
        if (startPitch > 180) startPitch -= 360;
        if (startYaw > 180) startYaw -= 360;

        float startDist  = _currentDistance;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsed / transitionDuration);

            _yaw   = Mathf.LerpAngle(startYaw, transitionYaw, t);
            _pitch = Mathf.Lerp(startPitch, transitionPitch, t);
            _currentDistance = Mathf.Lerp(startDist, transitionDistance, t);

            ApplyCameraState();
            yield return null;
        }

        _yaw   = transitionYaw;
        _pitch = transitionPitch;
        _currentDistance = transitionDistance;
        ApplyCameraState();

        // The follow eases on from where the transition left off rather than snapping to it.
        _yawVelocity = _pitchVelocity = _distanceVelocity = 0f;

        _isTransitioning = false;
        IsControlEnabled = true;
    }

    private void ApplyCameraState()
    {
        if (cam == null || _orbitPivot == null) return;

        // Update pivot rotation based on internal yaw/pitch
        _orbitPivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        // Position camera relative to pivot manually
        cam.transform.position = _orbitPivot.position + _orbitPivot.rotation * (Vector3.back * _currentDistance);
        cam.transform.rotation = _orbitPivot.rotation;
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
    /// The resting shot: dead behind the boat, at the authored height and zoom. The angles are
    /// eased rather than set, so a turn leaves the camera trailing the stern for a moment and it
    /// comes round after — the catching up that gives a turning its weight.
    /// </summary>
    private void EaseTowardsFollowPose()
    {
        float targetYaw   = _boatTarget.eulerAngles.y;
        float targetPitch = Mathf.Clamp(RestPitch, pitchMin, pitchMax);

        if (followCatchUpTime <= 0f)
        {
            _yaw             = targetYaw;
            _pitch           = targetPitch;
            _currentDistance = TargetDistance;
            return;
        }

        _yaw             = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, followCatchUpTime);
        _pitch           = Mathf.SmoothDampAngle(_pitch, targetPitch, ref _pitchVelocity, followCatchUpTime);
        _currentDistance = Mathf.SmoothDamp(_currentDistance, TargetDistance, ref _distanceVelocity, followCatchUpTime);
    }

    private void LateUpdate()
    {
        if (cam == null || _orbitPivot == null || _boatTarget == null) return;

        // Always keep pivot on the boat
        _orbitPivot.position = _boatTarget.position;

        if (_isTransitioning) return;

        if (!IsControlEnabled)
        {
            // During intro, follow the boat's rotation exactly
            _orbitPivot.rotation = _boatTarget.rotation * _introRelativeRotation;

            // Sync internal yaw/pitch so we don't jump when controls enable
            Vector3 euler = _orbitPivot.rotation.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
            if (_pitch > 180) _pitch -= 360;

            // Position camera based on boat rotation follow
            cam.transform.position = _orbitPivot.position + _orbitPivot.rotation * (Vector3.back * _currentDistance);
            cam.transform.rotation = _orbitPivot.rotation;
            return;
        }

        // Under way the camera keeps its place behind the boat; anchored, the mouse has the
        // angles and only the distance is still eased.
        if (!IsAnchored)
        {
            EaseTowardsFollowPose();
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

        ApplyCameraState();
    }

    private void Update()
    {
        if (cam == null || _orbitPivot == null || _boatTarget == null || _isTransitioning || !IsControlEnabled)
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
    public void EditorPreview()
    {
        InternalEditorPreview(manualStartPitch, manualStartYaw, manualStartDistance, "Preview Camera Starting Position");
    }

    public void EditorPreviewTransition()
    {
        InternalEditorPreview(transitionPitch, transitionYaw, transitionDistance, "Preview Camera Transition Position");
    }

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

        Undo.RecordObject(cam.transform, undoName);

        d = Mathf.Max(d, 0.1f);
        Quaternion rotation = Quaternion.Euler(p, y, 0f);
        cam.transform.position = pivot + rotation * (Vector3.back * d);
        cam.transform.rotation = rotation;
    }
#endif
}
