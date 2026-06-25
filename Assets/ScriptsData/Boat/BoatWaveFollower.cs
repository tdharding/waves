using UnityEngine;

public class BoatWaveFollower : MonoBehaviour
{
    [Header("Assign the water plane (wave mesh)")]
    public Transform waterTransform;

    [Header("Wave Height Offset")]
    public float extraYOffset = 0f;

    [Header("Boat Visual for Pitch + Roll")]
    public Transform boatVisual;

    [Header("Tilt Settings")]
    public float tiltSampleOffset = 0.5f;
    public float tiltMultiplier = 60f;
    public float rollMultiplier = 45f;

    [Header("Tilt Limits (relative to base rotation)")]
    public float maxTiltUp = 53f;
    public float maxTiltDown = -52f;
    public float maxRollAngle = 25f;

    private Quaternion baseRotation;
    private Material waterMaterial;

    int freqID, speedID, stepID, depthID, waveCenterID;

    void Start()
    {
        MeshRenderer rend = waterTransform.GetComponent<MeshRenderer>();
        waterMaterial = rend.sharedMaterial;

        freqID       = Shader.PropertyToID("_Frequency");
        speedID      = Shader.PropertyToID("_Speed");
        stepID       = Shader.PropertyToID("_WaveStepRate");
        depthID      = Shader.PropertyToID("_RippleDepth");
        waveCenterID = Shader.PropertyToID("_WaveCenter");

        if (boatVisual != null)
            baseRotation = boatVisual.localRotation;
    }

    void Update()
    {
        if (waterTransform == null || waterMaterial == null) return;

        WaveUtils.WaveParams p = WaveUtils.ReadParams(waterTransform, waterMaterial);
        Vector3 boatPos = transform.position;

        // Use Smooth version for height to prevent vibrating boat
        float height = WaveUtils.SampleHeightSmooth(boatPos, p) + extraYOffset;

        // Apply height
        boatPos.y = height;
        transform.position = boatPos;

        if (boatVisual != null)
            ApplyTilt(boatPos, p);
    }

    void ApplyTilt(Vector3 boatPos, WaveUtils.WaveParams p)
    {
        // --- Use radial direction relative to wave center ---
        Vector3 radialDir = (boatPos - p.origin).normalized;
        Vector3 tangentDir = Vector3.Cross(Vector3.up, radialDir).normalized;

        // --- PITCH (radial direction) ---
        Vector3 frontPoint = boatPos + radialDir * tiltSampleOffset;
        Vector3 backPoint  = boatPos - radialDir * tiltSampleOffset;

        float frontH = WaveUtils.SampleHeightSmooth(frontPoint, p);
        float backH  = WaveUtils.SampleHeightSmooth(backPoint, p);

        float pitchAmount = (backH - frontH);
        float pitchAngle = Mathf.Clamp(pitchAmount * tiltMultiplier, maxTiltDown, maxTiltUp);

        // --- ROLL (side-to-side, perpendicular to radial) ---
        Vector3 rightPoint = boatPos + tangentDir * tiltSampleOffset;
        Vector3 leftPoint  = boatPos - tangentDir * tiltSampleOffset;

        float rightH = WaveUtils.SampleHeightSmooth(rightPoint, p);
        float leftH  = WaveUtils.SampleHeightSmooth(leftPoint, p);

        float rollAmount = (leftH - rightH);
        float rollAngle = Mathf.Clamp(rollAmount * rollMultiplier, -maxRollAngle, maxRollAngle);

        // --- Final rotation ---
        Quaternion tilt = Quaternion.Euler(pitchAngle, 0f, rollAngle);
        boatVisual.localRotation = baseRotation * tilt;
    }

}
