using UnityEngine;

public class BoatVisual : MonoBehaviour
{
    [Header("Assign the water plane (wave mesh)")]
    public Transform waterTransform;

    [Header("Airborne (Boost tool)")]
    [Tooltip("If assigned, wave-snapping is suspended while this is airborne.")]
    public BoatMovement boatMovement;

    [Header("Wave Settings")]
    public float extraYOffset = 0f;
    public float sampleOffset = 0.2f;
    public float rotationSmoothSpeed = 10f;

    private Material mat;
    private Transform root;
    private Quaternion baseRotation;

    void Start()
    {
        root = transform.parent; // BoatRoot
        if (waterTransform != null)
        {
            var renderer = waterTransform.GetComponent<MeshRenderer>();
            if (renderer != null) mat = renderer.sharedMaterial;
        }

        baseRotation = transform.localRotation;
    }

    void LateUpdate()
    {
        if (waterTransform == null || mat == null) return;
        if (boatMovement != null && boatMovement.IsAirborne) return;
        ApplyWaveAlignment();
    }

    void ApplyWaveAlignment()
    {
        Vector3 boatWorldPos = root.position;
        var p = WaveUtils.ReadParams(waterTransform, mat);

        // WAVE HEIGHT
        float height = WaveUtils.SampleHeightSmooth(boatWorldPos, p) + extraYOffset;
        Vector3 localPos = transform.localPosition;
        localPos.y = height;
        transform.localPosition = localPos;

        // WAVE NORMAL ALIGNMENT
        Vector3 normal = WaveUtils.GetNormal(boatWorldPos, p, sampleOffset);
        
        // We want the boat's "up" to be the wave normal.
        // We preserve the root's yaw.
        Quaternion tiltAlignment = Quaternion.FromToRotation(Vector3.up, normal);
        Quaternion targetRotation = tiltAlignment * root.rotation * baseRotation;

        if (rotationSmoothSpeed > 0)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSmoothSpeed);
        }
        else
        {
            transform.rotation = targetRotation;
        }
    }
}
