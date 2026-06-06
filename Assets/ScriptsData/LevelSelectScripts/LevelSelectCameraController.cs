using UnityEngine;
using Unity.Cinemachine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LevelSelectCameraController : MonoBehaviour
{
    [Header("Camera")]
    public CinemachineCamera cam;

    [Header("Follow Distance")]
    public float followDistance = 8f;
    public float minDistance    = 4f;
    public float maxDistance    = 16f;

    [Header("Scroll Zoom")]
    public float zoomSpeed = 5f;

    [Header("Orbit (Middle Mouse)")]
    public float orbitSpeed = 250f;
    public float pitchMin   = -60f;
    public float pitchMax   = 45f;

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

    public bool IsControlEnabled { get; set; } = true;

    private float             _currentDistance;
    private float             _yaw;
    private float             _pitch;
    private Transform         _orbitPivot;
    private Transform         _boatTarget;

    private Quaternion _introRelativeRotation;
    private bool       _isTransitioning;

    private void Awake()
    {
        // No longer need to fetch CinemachineFollow
    }

    private void Start()
    {
        _currentDistance = followDistance;
    }

    // Called by LevelSelectDataController at runtime
    public void SetFollowTarget(Transform target)
    {
        _boatTarget = target;

        if (_orbitPivot == null)
        {
            _orbitPivot = new GameObject("CameraOrbitPivot").transform;
            _orbitPivot.SetParent(transform);
        }

        if (_boatTarget != null && cam != null)
        {
            _orbitPivot.position = _boatTarget.position;

            if (useManualStartingState)
            {
                _yaw = manualStartYaw;
                _pitch = manualStartPitch;
                _currentDistance = manualStartDistance;
            }
            else
            {
                // Derive starting angle from the camera's current world position
                Vector3 offset = cam.transform.position - _boatTarget.position;
                Vector3 dir = offset.normalized;
                if (dir == Vector3.zero) dir = Vector3.back;

                _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                _pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
                _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
                _currentDistance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);
            }

            _orbitPivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // Capture initial relative rotation for the intro follow
            _introRelativeRotation = Quaternion.Inverse(_boatTarget.rotation) * _orbitPivot.rotation;
            
            // Initial positioning
            ApplyCameraState();
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

        // Apply state for manual control (Orbit/Zoom)
        ApplyCameraState();
    }

    private void Update()
    {
        if (cam == null || _orbitPivot == null || _boatTarget == null || _isTransitioning || !IsControlEnabled) return;

        // Scroll zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) >= 0.01f)
        {
            _currentDistance = Mathf.Clamp(_currentDistance - scroll * zoomSpeed, minDistance, maxDistance);
        }

        // Orbit (Right or Middle Mouse)
        if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
        {
            _yaw   += Input.GetAxis("Mouse X") * orbitSpeed * Time.deltaTime;
            _pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * Time.deltaTime;
            _pitch  = Mathf.Clamp(_pitch, pitchMin, pitchMax);
        }
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

    private void InternalEditorPreview(float p, float y, float d, string undoName)
    {
        if (cam == null) return;
        Transform target = previewTarget;
        if (target == null)
        {
            var boat = Object.FindAnyObjectByType<LevelSelectBoatControl>();
            if (boat != null) target = boat.BoatTransform;
        }
        
        // Safety: Don't use the camera as its own target
        if (target == null || target == cam.transform) 
        {
            Debug.LogWarning("[CameraController] No valid preview target found. Assign the Boat to 'Preview Target'.");
            return;
        }

        Undo.RecordObject(cam.transform, undoName);
        
        // Force distance to be at least small positive to avoid camera flipping
        d = Mathf.Max(d, 0.1f);

        Quaternion rotation = Quaternion.Euler(p, y, 0f);
        Vector3 offset = rotation * (Vector3.back * d);

        // Explicit world position calculation
        cam.transform.position = target.position + offset;
        cam.transform.rotation = rotation;
    }
#endif
}
