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

    int freqID, speedID, stepID, depthID;

    void Start()
    {
        MeshRenderer rend = waterTransform.GetComponent<MeshRenderer>();
        waterMaterial = rend.sharedMaterial;

        freqID  = Shader.PropertyToID("_Frequency");
        speedID = Shader.PropertyToID("_Speed");
        stepID  = Shader.PropertyToID("_WaveStepRate");
        depthID = Shader.PropertyToID("_RippleDepth");

        if (boatVisual != null)
            baseRotation = boatVisual.localRotation;
    }

    void Update()
    {
        Vector3 boatPos = transform.position;
        Vector3 waveCenter = waterTransform.position;

        // --- Read shader values ---
        float frequency = waterMaterial.GetFloat(freqID);
        float speed     = waterMaterial.GetFloat(speedID);
        float stepRate  = waterMaterial.GetFloat(stepID);
        float ripple    = waterMaterial.GetFloat(depthID);

        // --- Stepped phase from shader logic ---
        float steppedTime = Mathf.Floor(Time.time * stepRate) / stepRate;
        float phase = -(steppedTime * speed);

        // --- Account for mesh scale (shader uses object space) ---
        float meshScale = waterTransform.localScale.x;

        // --- Compute world-space radial distance ---
        float worldDist = Vector3.Distance(boatPos, waveCenter);
        float scaledDist = worldDist / meshScale;  // convert to object-space distance

        // --- Compute sine wave ---
        float sine = Mathf.Sin(phase + scaledDist * frequency);

        // --- Final amplitude (object space → world space) ---
        float amplitude = ripple * meshScale;

        // --- Final height ---
        float height = sine * amplitude + extraYOffset;

        // Apply height
        boatPos.y = height;
        transform.position = boatPos;

        if (boatVisual != null)
            ApplyTilt(boatPos, waveCenter, phase, frequency, amplitude, meshScale);
    }
void ApplyTilt(Vector3 boatPos, Vector3 waveCenter, float phase, float frequency, float amplitude, float meshScale)
{
    float Dist(Vector3 p)
    {
        return Vector3.Distance(p, waveCenter) / meshScale;
    }

    // --- Use radial direction instead of boat.forward ---
    Vector3 radialDir = (boatPos - waveCenter).normalized;
    Vector3 tangentDir = Vector3.Cross(Vector3.up, radialDir).normalized;

    // --- PITCH (radial direction) ---
    Vector3 frontPoint = boatPos + radialDir * tiltSampleOffset;
    Vector3 backPoint  = boatPos - radialDir * tiltSampleOffset;

    float frontH = Mathf.Sin(phase + Dist(frontPoint) * frequency) * amplitude;
    float backH  = Mathf.Sin(phase + Dist(backPoint) * frequency) * amplitude;

    float pitchAmount = (backH - frontH);
    float pitchAngle = Mathf.Clamp(pitchAmount * tiltMultiplier, maxTiltDown, maxTiltUp);

    // --- ROLL (side-to-side, perpendicular to radial) ---
    Vector3 rightPoint = boatPos + tangentDir * tiltSampleOffset;
    Vector3 leftPoint  = boatPos - tangentDir * tiltSampleOffset;

    float rightH = Mathf.Sin(phase + Dist(rightPoint) * frequency) * amplitude;
    float leftH  = Mathf.Sin(phase + Dist(leftPoint) * frequency) * amplitude;

    float rollAmount = (leftH - rightH);
    float rollAngle = Mathf.Clamp(rollAmount * rollMultiplier, -maxRollAngle, maxRollAngle);

    // --- Final rotation ---
    Quaternion tilt = Quaternion.Euler(pitchAngle, 0f, rollAngle);
    boatVisual.localRotation = baseRotation * tilt;
}

}
