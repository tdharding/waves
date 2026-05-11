using UnityEngine;

public class BoatMovement : MonoBehaviour
{
    [Header("Steering (V2)")]
    public SteeringWheelControllerV2 wheelV2;

    [Header("Speed Settings")]
    public float baseMoveSpeed = 5f;
    public float boostedMoveSpeed = 8f;

    [Header("Acceleration")]
    public float acceleration = 3f;
    public float deceleration = 4f;

    [Header("Turning")]
    public float maxTurnSpeed = 80f;
    public float shiftTurnMultiplier = 3f;
    [Header("Shift Handling")]
    public float shiftSpeedMultiplier = 0.5f;
    [Header("Whirl Handling")]
    public float whirlSpeedMultiplier = 0.5f;




    [Header("Turn Acceleration")]
    public float turnAcceleration = 4f;
    public float turnDeceleration = 6f;

    private float currentTurnInput = 0f;


    [Header("Sonar Slow")]
    public float sonarSpeedMultiplier = 0.5f;

    [Header("Ability Wheel Slow")]
    public float abilityWheelSpeedMultiplier = 0.25f;

    [Header("Catapult Slow")]
    public float catapultSpeedMultiplier = 0.45f;

    [Header("Engine Audio")]
    public AudioSource boatEngineAudio;
    public float turnPitchAmount = 0.2f;
    public float speedPitchAmount = 0.25f;
    private float basePitch = 1f;

    [Header("Physics Settings")]
    public float slopeSpeedMultiplier = 2f;
    public float whirlpoolPullForce = 7.33f;
    public float whirlpoolPullRadiusMultiplier = 2.75f;

    [HideInInspector] public bool controlsEnabled = false;

    private Rigidbody rb;
    private float fixedY;
    private float currentSpeed;
    private Transform waterTransform;
    private Material waterMaterial;

    // External control flags
    private bool boosting = false;
    private bool sonarSlow = false;
    private bool abilityWheelSlow = false;
    private bool catapultActiveSlow = false;
    private bool steeringBlocked = false;

    // --------------------------------------------------
    // UNITY
    // --------------------------------------------------

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        waterTransform = LevelDataController.Instance?.GetWaveTransform();
        if (waterTransform != null)
        {
            var rend = waterTransform.GetComponent<Renderer>();
            if (rend != null) waterMaterial = rend.sharedMaterial;
        }
        
        fixedY = waterTransform != null ? waterTransform.position.y : transform.position.y;
        currentSpeed = 0f;

        if (boatEngineAudio != null)
        {
            basePitch = boatEngineAudio.pitch;
            boatEngineAudio.Stop();
        }
    }

    void FixedUpdate()
    {
        if (rb == null || !controlsEnabled)
        {
            if (rb != null)
            {
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, deceleration * Time.fixedDeltaTime);
                ApplyVelocity(transform.forward * currentSpeed);
            }
            StopEngineAudio();
            return;
        }

        if (PauseManager.IsPaused)
            return;

        if (waterTransform != null)
            fixedY = waterTransform.position.y;

        HandleSteering();
        HandleMovement();
        ApplyWhirlpoolPull();
    }

    private void ApplyVelocity(Vector3 move)
    {
        rb.linearVelocity = new Vector3(move.x, rb.linearVelocity.y, move.z);
        Vector3 pos = rb.position;
        pos.y = fixedY;
        rb.position = pos;
    }

    // --------------------------------------------------
    // STEERING
    // --------------------------------------------------
    void HandleSteering()
    {
        if (steeringBlocked)
        {
            currentTurnInput = Mathf.MoveTowards(currentTurnInput, 0f, turnDeceleration * Time.fixedDeltaTime);
            return;
        }

        float targetInput = 0f;

        if (Input.GetKey(KeyCode.LeftArrow))
            targetInput = -1f;
        else if (Input.GetKey(KeyCode.RightArrow))
            targetInput = 1f;

        // Accelerate toward target input
        float accelRate = Mathf.Abs(targetInput) > Mathf.Abs(currentTurnInput)
            ? turnAcceleration
            : turnDeceleration;

        currentTurnInput = Mathf.MoveTowards(
            currentTurnInput,
            targetInput,
            accelRate * Time.fixedDeltaTime
        );

        if (Mathf.Approximately(currentTurnInput, 0f))
        {
            UpdateEnginePitch(0f, 1f);
            return;
        }

        float turnMultiplier = 1f;

        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            turnMultiplier = shiftTurnMultiplier;

        float turn =
            currentTurnInput *
            maxTurnSpeed *
            turnMultiplier *
            Time.fixedDeltaTime;

        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, turn, 0f));

        UpdateEnginePitch(currentTurnInput, turnMultiplier);
    }


    // --------------------------------------------------
    // MOVEMENT (ACCELERATED)
    // --------------------------------------------------

    void HandleMovement()
    {
        float targetSpeed = boosting ? boostedMoveSpeed : baseMoveSpeed;
        float accelRate = boosting ? acceleration : deceleration;

        if (sonarSlow)
            targetSpeed *= sonarSpeedMultiplier;

        if (abilityWheelSlow)
            targetSpeed *= abilityWheelSpeedMultiplier;

        if (catapultActiveSlow)
            targetSpeed *= catapultSpeedMultiplier;

        // Shift reduces boat speed
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (shiftHeld)
            targetSpeed *= shiftSpeedMultiplier;

        // Whirl state = Sonar active AND Space held
        bool whirlActive = sonarSlow && Input.GetKey(KeyCode.Space);
        if (whirlActive)
            targetSpeed *= whirlSpeedMultiplier;

        // SLOPE ADJUSTMENT
        float slopeModifier = CalculateSlopeModifier();
        targetSpeed *= slopeModifier;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            accelRate * Time.fixedDeltaTime
        );

        Vector3 move = transform.forward * currentSpeed;
        ApplyVelocity(move);

        if (boatEngineAudio != null && !boatEngineAudio.isPlaying)
            boatEngineAudio.Play();
    }

    float CalculateSlopeModifier()
    {
        if (waterTransform == null || waterMaterial == null) return 1f;

        var p = WaveUtils.ReadParams(waterTransform, waterMaterial);
        Vector3 pos = transform.position;
        
        float hCenter = WaveUtils.SampleWaveSmooth(pos, p);
        float hAhead  = WaveUtils.SampleWaveSmooth(pos + transform.forward * 0.1f, p);
        
        float slope = hAhead - hCenter;
        // slope > 0 means going uphill -> reduce speed
        // slope < 0 means going downhill -> increase speed
        
        return Mathf.Clamp(1f - (slope * slopeSpeedMultiplier), 0.5f, 2f);
    }

    void ApplyWhirlpoolPull()
    {
        if (waterTransform == null) return;
        
        Vector3 pull = WaveUtils.GetWhirlpoolPull(transform.position, waterTransform.localScale.x, whirlpoolPullRadiusMultiplier);
        rb.AddForce(pull * whirlpoolPullForce, ForceMode.Acceleration);
    }
// --------------------------------------------------
// AUDIO
// --------------------------------------------------

    void UpdateEnginePitch(float steeringT, float turnMultiplier)
    {
        if (boatEngineAudio == null)
            return;

        float speed01 = Mathf.InverseLerp(
            0f,
            boostedMoveSpeed,
            currentSpeed
        );

        float pitch = basePitch;
        pitch += Mathf.Abs(steeringT) * turnPitchAmount;
        pitch += (turnMultiplier - 1f) * turnPitchAmount;
        pitch += speed01 * speedPitchAmount;

        boatEngineAudio.pitch = pitch;
    }

    void StopEngineAudio()
    {
        if (boatEngineAudio != null)
        {
            boatEngineAudio.Stop();
            boatEngineAudio.pitch = basePitch;
        }
    }

    // --------------------------------------------------
    // PUBLIC API (EXISTING + NEW)
    // --------------------------------------------------

    public void StopBoatMovement()
    {
        controlsEnabled = false;
        currentSpeed = 0f;
        StopEngineAudio();
    }

    public void SetBoosting(bool value)
    {
        boosting = value;
    }

    public void SetSonarSlow(bool value)        => sonarSlow = value;
    public void SetAbilityWheelSlow(bool value) => abilityWheelSlow = value;
    public void SetCatapultSlow(bool value)     => catapultActiveSlow = value;
    public void SetSteeringBlocked(bool value)  => steeringBlocked = value;

    public float Speed01
    {
        get
        {
            return Mathf.InverseLerp(
                0f,
                boostedMoveSpeed,
                currentSpeed
            );
        }
    }
}
