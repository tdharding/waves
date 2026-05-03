using UnityEngine;

public class SonarCameraFollow : MonoBehaviour
{
    [Header("Relative Camera Offset")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(2.402f, -44.69f, -6.771f);

    [Header("Axis Locks")]
    [SerializeField] private bool lockY = true;

    private Transform target;
    private bool trackingActive = false;

    /// <summary>
    /// Begin tracking a specific target transform (the SoulBoat).
    /// Called from LevelDataController after SpawnSoulBoat() returns.
    /// </summary>
    public void BeginTracking(Transform soulBoat)
    {
        if (soulBoat == null)
        {
            Debug.LogError("SonarCameraFollow: BeginTracking called with null target.");
            return;
        }

        target = soulBoat;
        trackingActive = true;
        enabled = true;
    }

    void LateUpdate()
    {
        if (!trackingActive || !target)
            return;

        Vector3 desired = target.position + cameraOffset;

        if (lockY)
        {
            desired.y = transform.position.y;
        }

        transform.position = desired;
    }
}
