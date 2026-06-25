using UnityEngine;
using Unity.Cinemachine;
using System.Text;

public class CameraController : MonoBehaviour
{
    public static CameraController Instance;

    [Header("Default Camera")]
    [SerializeField] private CinemachineCamera boatFollowCam;

    [Header("Orbital Camera")]
    [SerializeField] private CinemachineCamera orbitalCam;
    private CinemachineOrbitalFollow orbital;

    [Header("Runtime Targets")]
    [SerializeField] private Transform boatTarget;
    [SerializeField] private Transform orbitalCenter;

    private CameraProfile activeProfile;
    private bool orbitalActive = false;

    [Header("Manual Orbit")]
[SerializeField] private bool manualOrbitActive = false;
[SerializeField] private float manualOrbitSpeed = 250f;
[SerializeField] private float manualZoomSpeed = 5f;
[SerializeField] private float manualMinDistance = 4f;
[SerializeField] private float manualMaxDistance = 16f;
[SerializeField] private float manualPitchMin = -60f;
[SerializeField] private float manualPitchMax = 45f;

private float _manualYaw = -128.6533f;
private float _manualPitch = 44.56071f;
private float _manualDistance = 9f;

// =====================================================
// UNITY
// =====================================================

private void Awake()
{
    Instance = this;

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
    if (!manualOrbitActive || boatTarget == null) return;

    // Scroll zoom
    float scroll = Input.GetAxis("Mouse ScrollWheel");
    if (Mathf.Abs(scroll) >= 0.01f)
    {
        _manualDistance = Mathf.Clamp(_manualDistance - scroll * manualZoomSpeed, manualMinDistance, manualMaxDistance);
    }

    // Orbit (Right or Middle Mouse)
    if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
    {
        _manualYaw   += Input.GetAxis("Mouse X") * manualOrbitSpeed * Time.deltaTime;
        _manualPitch -= Input.GetAxis("Mouse Y") * manualOrbitSpeed * Time.deltaTime;
        _manualPitch  = Mathf.Clamp(_manualPitch, manualPitchMin, manualPitchMax);
    }
}

private void LateUpdate()
{
    if (manualOrbitActive && boatTarget != null && boatFollowCam != null)
    {
        Quaternion rotation = Quaternion.Euler(_manualPitch, _manualYaw, 0f);
        boatFollowCam.transform.position = boatTarget.position + rotation * (Vector3.back * _manualDistance);
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
        
        Quaternion rotation = Quaternion.Euler(_manualPitch, _manualYaw, 0f);
        boatFollowCam.transform.position = boatTarget.position + rotation * (Vector3.back * _manualDistance);
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