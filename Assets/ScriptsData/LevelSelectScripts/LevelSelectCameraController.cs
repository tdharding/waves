using UnityEngine;
using Unity.Cinemachine;

public class LevelSelectCameraController : MonoBehaviour
{
    [Header("Camera")]
    public CinemachineCamera cam;

    [Header("Scroll Zoom")]
    public float zoomSpeed       = 10f;
    public float minFOV          = 5f;
    public float maxFOV          = 60f;
    public float defaultFOV      = 30f;
    public float minDistance     = 5f;
    public float maxDistance     = 12f;
    public float defaultDistance = 5.16f;

    [Header("Orbit (Middle Mouse)")]
    public float orbitSpeed = 150f;
    public float pitchMin   = -60f;
    public float pitchMax   = 45f;

    public bool IsControlEnabled { get; set; } = true;

    private float                        _currentFOV;
    private float                        _yaw;
    private float                        _pitch;
    private Transform                    _orbitPivot;
    private Transform                    _boatTarget;
    private CinemachinePositionComposer  _positionComposer;

    private void Start()
    {
        if (cam != null)
            cam.TryGetComponent(out _positionComposer);

        _currentFOV = defaultFOV;
        ApplyZoom();
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

        if (_boatTarget != null)
        {
            _orbitPivot.position = _boatTarget.position;
            _yaw   = 0f;
            _pitch = 0f;
        }

        if (cam != null)
            cam.Follow = _orbitPivot;
    }

    private void Update()
    {
        if (cam == null || !IsControlEnabled) return;

        // Scroll zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) >= 0.01f)
        {
            _currentFOV = Mathf.Clamp(_currentFOV - scroll * zoomSpeed, minFOV, maxFOV);
            ApplyZoom();
        }

        // Middle mouse or right-click orbit
        if ((Input.GetMouseButton(2) || Input.GetMouseButton(1)) && _boatTarget != null)
        {
            _yaw   += Input.GetAxis("Mouse X") * orbitSpeed * Time.deltaTime;
            _pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * Time.deltaTime;
            _pitch  = Mathf.Clamp(_pitch, pitchMin, pitchMax);
        }

        // Pivot tracks boat position, orbit rotation applied separately
        if (_orbitPivot != null && _boatTarget != null)
        {
            _orbitPivot.position = _boatTarget.position;
            _orbitPivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }

    private void ApplyZoom()
    {
        var lens = cam.Lens;
        lens.FieldOfView = _currentFOV;
        cam.Lens = lens;

        if (_positionComposer != null)
        {
            float t = Mathf.InverseLerp(minFOV, maxFOV, _currentFOV);
            _positionComposer.CameraDistance = Mathf.Lerp(minDistance, maxDistance, t);
        }
    }
}
