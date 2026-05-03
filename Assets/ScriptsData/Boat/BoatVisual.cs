using UnityEngine;

public class BoatVisual : MonoBehaviour
{
    [Header("Assign the water plane (wave mesh)")]
    public Transform waterTransform;

    [Header("Wave Settings")]
    public float extraYOffset = 0f;
    public float tiltSampleOffset = 0.1f;
    public float tiltMultiplier = 100f;
    public float rollMultiplier = 100f;
    public float maxTiltUp = 53f;
    public float maxTiltDown = -22f;
    public float maxRollAngle = 60f;

    [Header("Smooth Direction Flip Settings")]
    public bool flipTiltWhenMovingTowards = true;
    public float directionSmoothSpeed = 8f;
    public float deadzone = 0.15f;

    private float directionBlend = 0f;
    private Quaternion baseRotation;

    private Material mat;

    Transform root;

    void Start()
    {
        root = transform.parent; // BoatRoot

        mat = waterTransform.GetComponent<MeshRenderer>().sharedMaterial;

        baseRotation = transform.localRotation;
    }

    void LateUpdate()
    {
        ApplyWaveHeightAndTilt();
    }

    void ApplyWaveHeightAndTilt()
    {
        Vector3 boatWorldPos = root.position;
        var p = WaveUtils.ReadParams(waterTransform, mat);

        // WAVE HEIGHT
        float height = WaveUtils.SampleWaveSmooth(boatWorldPos, p) - WaveUtils.SampleWhirlpoolDepth(boatWorldPos, p) + extraYOffset;
        Vector3 localPos = transform.localPosition;
        localPos.y = height;
        transform.localPosition = localPos;

        // SMOOTH DIRECTION DETECTOR
        Vector3 radialDir = new Vector3(boatWorldPos.x - p.origin.x, 0f, boatWorldPos.z - p.origin.z).normalized;
        if (radialDir == Vector3.zero) radialDir = root.forward;
        Vector3 moveDir = root.forward;

        float rawDot = Vector3.Dot(moveDir, radialDir);
        float targetDir;

        if (Mathf.Abs(rawDot) < deadzone)
            targetDir = 0f;
        else
        {
            float sign = Mathf.Sign(rawDot);
            float t = (Mathf.Abs(rawDot) - deadzone) / (1f - deadzone);
            targetDir = Mathf.Clamp01(t) * sign;
        }

        directionBlend = Mathf.Lerp(directionBlend, targetDir, Time.deltaTime * directionSmoothSpeed);
        if (Mathf.Abs(directionBlend) < 0.0001f) directionBlend = 0f;

        // PITCH SAMPLING
        Vector3 front = boatWorldPos + radialDir * tiltSampleOffset;
        Vector3 back  = boatWorldPos - radialDir * tiltSampleOffset;
        float frontH = WaveUtils.SampleWaveSmooth(front, p);
        float backH  = WaveUtils.SampleWaveSmooth(back,  p);

        float pitchAmount = (backH - frontH);

        if (flipTiltWhenMovingTowards)
        {
            float flipAmount = Mathf.InverseLerp(0f, -1f, directionBlend);
            float flip = Mathf.Lerp(1f, -1f, flipAmount);
            pitchAmount *= flip;
        }

        float pitchAngle = Mathf.Clamp(pitchAmount * tiltMultiplier, maxTiltDown, maxTiltUp);

        // ROLL SAMPLING
        Vector3 tangentDir = Vector3.Cross(Vector3.up, radialDir).normalized;

        Vector3 right = boatWorldPos + tangentDir * tiltSampleOffset;
        Vector3 left  = boatWorldPos - tangentDir * tiltSampleOffset;
        float rightH = WaveUtils.SampleWaveSmooth(right, p);
        float leftH  = WaveUtils.SampleWaveSmooth(left,  p);

        float rollAmount = (leftH - rightH);
        float rollAngle = Mathf.Clamp(rollAmount * rollMultiplier, -maxRollAngle, maxRollAngle);

        // APPLY FINAL LOCAL ROTATION
        Quaternion tilt = Quaternion.Euler(pitchAngle, 0, rollAngle);
        transform.localRotation = baseRotation * tilt;
    }
}
