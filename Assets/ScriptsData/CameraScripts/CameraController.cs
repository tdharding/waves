using UnityEngine;
using Unity.Cinemachine;

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

    private void LateUpdate()
    {
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
    orbitalCenter = newCenter;
    boatTarget = newBoat;

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
}