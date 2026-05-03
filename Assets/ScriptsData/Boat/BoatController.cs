using UnityEngine;

public class BoatController : MonoBehaviour
{
    [Header("Assign the water plane (wave mesh)")]
    public Transform waterTransform;

    [Header("Boat Visual (for pitch + roll)")]
    public Transform boatVisual;

    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float turnSpeed = 2f;

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
    int freqID, speedID, depthID;

    private Rigidbody rb;

    void Start()
    {
        // CACHE RIGIDBODY
        rb = GetComponent<Rigidbody>();

        // IMPORTANT PHYSICS SETTINGS (required for stable tilt)
        rb.freezeRotation = true;

        mat = waterTransform.GetComponent<MeshRenderer>().sharedMaterial;

        freqID  = Shader.PropertyToID("_Frequency");
        speedID = Shader.PropertyToID("_Speed");
        depthID = Shader.PropertyToID("_RippleDepth");

        baseRotation = boatVisual.localRotation;
    }

    void Update()
    {
        HandleMovement();
        ApplyWaveHeightAndTilt();
    }

    // ---------------------------------------------------------
    // 1) FOLLOW MOUSE → TURN TOWARD MOUSE → MOVE FORWARD (PHYSICS)
    // ---------------------------------------------------------
    void HandleMovement()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane plane = new Plane(Vector3.up, 0f);

        if (plane.Raycast(ray, out float enter))
        {
            Vector3 target = ray.GetPoint(enter);
            Vector3 dir = target - transform.position;
            dir.y = 0;

            if (dir.sqrMagnitude > 0.001f)
            {
                // Turn toward desired direction
                Vector3 desiredDir = dir.normalized;

                Quaternion targetRot = Quaternion.LookRotation(desiredDir);
                transform.rotation = Quaternion.Lerp(
                    transform.rotation,
                    targetRot,
                    Time.deltaTime * turnSpeed
                );

                // MOVE ONLY FORWARD IN THE DIRECTION THE BOAT IS FACING
                Vector3 move = transform.forward * moveSpeed * Time.deltaTime;

                // PHYSICS-BASED MOVEMENT (collisions now work)
                rb.MovePosition(rb.position + move);
            }
        }
    }

    // ---------------------------------------------------------
    // 2) WAVE HEIGHT + SMOOTH PITCH/ROLL
    // ---------------------------------------------------------
    void ApplyWaveHeightAndTilt()
    {
        Vector3 pos = transform.position;
        Vector3 waveCenter = waterTransform.position;

        float frequency = mat.GetFloat(freqID);
        float waveSpeed = mat.GetFloat(speedID);
        float ripple = mat.GetFloat(depthID);

        float phase = -(Time.time * waveSpeed);
        float meshScale = waterTransform.localScale.x;

        float Dist(Vector3 p)
        {
            return Vector3.Distance(p, waveCenter) / meshScale;
        }

        //----------------------------------------------------------
        // WAVE HEIGHT
        //----------------------------------------------------------
        float sine = Mathf.Sin(phase + Dist(pos) * frequency);
        float amplitude = ripple * meshScale;
        float height = sine * amplitude + extraYOffset;

        // Keep the boat positioned vertically without breaking physics
        rb.position = new Vector3(rb.position.x, height, rb.position.z);

        //----------------------------------------------------------
        // SMOOTH DIRECTION DETECTOR + DEADZONE
        //----------------------------------------------------------
        Vector3 radialDir = (pos - waveCenter).normalized;
        Vector3 moveDir   = transform.forward;

        float rawDot = Vector3.Dot(moveDir, radialDir);
        float targetDir;

        if (Mathf.Abs(rawDot) < deadzone)
        {
            targetDir = 0f;
        }
        else
        {
            float sign = Mathf.Sign(rawDot);
            float t = (Mathf.Abs(rawDot) - deadzone) / (1f - deadzone);
            targetDir = Mathf.Clamp01(t) * sign;
        }

        directionBlend = Mathf.Lerp(
            directionBlend,
            targetDir,
            Time.deltaTime * directionSmoothSpeed
        );

        if (Mathf.Abs(directionBlend) < 0.0001f)
            directionBlend = 0f;

        //----------------------------------------------------------
        // PITCH (radial sampling)
        //----------------------------------------------------------
        Vector3 front = pos + radialDir * tiltSampleOffset;
        Vector3 back  = pos - radialDir * tiltSampleOffset;

        float frontH = Mathf.Sin(phase + Dist(front) * frequency) * amplitude;
        float backH  = Mathf.Sin(phase + Dist(back)  * frequency) * amplitude;

        float pitchAmount = (backH - frontH);

        // Smooth tilt flip when moving TOWARDS center
        if (flipTiltWhenMovingTowards)
        {
            float flipAmount = Mathf.InverseLerp(0f, -1f, directionBlend);
            float flip = Mathf.Lerp(1f, -1f, flipAmount);
            pitchAmount *= flip;
        }

        float pitchAngle = Mathf.Clamp(pitchAmount * tiltMultiplier, maxTiltDown, maxTiltUp);

        //----------------------------------------------------------
        // ROLL (side-to-side sampling)
        //----------------------------------------------------------
        Vector3 tangentDir = Vector3.Cross(Vector3.up, radialDir).normalized;

        Vector3 right = pos + tangentDir * tiltSampleOffset;
        Vector3 left  = pos - tangentDir * tiltSampleOffset;

        float rightH = Mathf.Sin(phase + Dist(right) * frequency) * amplitude;
        float leftH  = Mathf.Sin(phase + Dist(left)  * frequency) * amplitude;

        float rollAmount = (leftH - rightH);
        float rollAngle = Mathf.Clamp(rollAmount * rollMultiplier, -maxRollAngle, maxRollAngle);

        //----------------------------------------------------------
        // APPLY FINAL ROTATION TO VISUAL
        //----------------------------------------------------------
        Quaternion tilt = Quaternion.Euler(pitchAngle, 0, rollAngle);
        boatVisual.localRotation = baseRotation * tilt;
    }
}
