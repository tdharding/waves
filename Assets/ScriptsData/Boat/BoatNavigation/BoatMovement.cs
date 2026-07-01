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
    private float boostSpeedOverride = -1f;
    private float boostAccelOverride = -1f;
    private float boostDecelOverride = -1f;
    private bool sonarSlow = false;
    private bool abilityWheelSlow = false;
    private bool catapultActiveSlow = false;
    private bool steeringBlocked = false;

    // Airborne state (set by BoostController via LaunchBoat)
    private bool isAirborne = false;
    private float airGravityMultiplier;
    private float airControlFactor;
    private float airTimer;
    private float maxAirTime;

    public bool IsAirborne => isAirborne;
    public float CurrentSpeed => currentSpeed;

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

        if (isAirborne)
            HandleAirborne();
        else
        {
            HandleMovement();
            ApplyWhirlpoolPull();
        }

        DebugPostLand();
    }

    private void ApplyVelocity(Vector3 move)
    {
        rb.linearVelocity = new Vector3(move.x, rb.linearVelocity.y, move.z);
        if (!isAirborne)
        {
            Vector3 pos = rb.position;
            pos.y = fixedY;
            rb.position = pos;
        }
    }

    // --------------------------------------------------
    // AIRBORNE (Boost tool launch)
    // --------------------------------------------------

    void HandleAirborne()
    {
        airTimer += Time.fixedDeltaTime;

        // rb.useGravity is off (the boat is normally Y-pinned every frame), so apply the
        // full intended gravity ourselves rather than assuming engine gravity is already active.
        rb.AddForce(Physics.gravity * airGravityMultiplier, ForceMode.Acceleration);

        // Light forward air control so the boat doesn't feel completely ballistic.
        Vector3 vel = rb.linearVelocity;
        float thrust = (boosting ? boostedMoveSpeed : baseMoveSpeed) * airControlFactor;
        Vector3 nudge = transform.forward * thrust * Time.fixedDeltaTime;
        rb.linearVelocity = new Vector3(vel.x + nudge.x, vel.y, vel.z + nudge.z);

        if (airTimer >= maxAirTime || rb.position.y <= fixedY + SampleWaveHeightAt(rb.position))
        {
            bool timedOut = airTimer >= maxAirTime;
            Debug.Log($"[BoatMovement] Landed ({(timedOut ? "max air time reached" : "reached water")}), airTime={airTimer:F2}s");
            Land();
        }
    }

    float SampleWaveHeightAt(Vector3 worldPos)
    {
        if (waterTransform == null || waterMaterial == null) return 0f;
        var p = WaveUtils.ReadParams(waterTransform, waterMaterial);
        return WaveUtils.SampleHeightSmooth(worldPos, p);
    }

    void Land()
    {
        isAirborne = false;
        Vector3 vel = rb.linearVelocity;
        float waveHeight = SampleWaveHeightAt(rb.position);
        Debug.Log($"[BoatMovement] Land() — rb.position.y={rb.position.y:F3}, fixedY={fixedY:F3}, waveHeight={waveHeight:F3}, " +
                  $"landingTarget={fixedY + waveHeight:F3}, vel.y={vel.y:F3}");
        vel.y = 0f;
        rb.linearVelocity = vel;
        _postLandFrames = 10;
    }

    private int _postLandFrames = 0;
    private void DebugPostLand()
    {
        if (_postLandFrames <= 0) return;
        _postLandFrames--;
        float waveHeight = SampleWaveHeightAt(rb.position);
        Debug.Log($"[BoatMovement] PostLand frame {10 - _postLandFrames} — rb.y={rb.position.y:F3}, " +
                  $"fixedY={fixedY:F3}, waveH={waveHeight:F3}, pinnedTarget={fixedY:F3}, vel.y={rb.linearVelocity.y:F3}");
    }

    // Called by BoostController to launch the boat off a wave.
    public void LaunchBoat(Vector3 launchVelocity, float gravityMultiplier, float airControl, float maxAirborneTime)
    {
        isAirborne = true;
        airGravityMultiplier = gravityMultiplier;
        airControlFactor = airControl;
        maxAirTime = maxAirborneTime;
        airTimer = 0f;
        rb.linearVelocity = launchVelocity;
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
        float targetSpeed = boosting ? (boostSpeedOverride > 0f ? boostSpeedOverride : boostedMoveSpeed) : baseMoveSpeed;
        float accelRate = boosting
            ? (boostAccelOverride > 0f ? boostAccelOverride : acceleration)
            : (boostDecelOverride > 0f ? boostDecelOverride : deceleration);

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
        boostSpeedOverride = -1f;
        boostAccelOverride = -1f;
        boostDecelOverride = -1f;
    }

    // Used by BoostController so the Boost tool's speed/acceleration/deceleration are tunable
    // independently of the defaults shared by other tools.
    public void SetBoosting(bool value, float speedOverride, float accelOverride = -1f, float decelOverride = -1f)
    {
        boosting = value;
        boostSpeedOverride = speedOverride;
        boostAccelOverride = accelOverride;
        boostDecelOverride = decelOverride;
    }

    public void BleedSpeedOnCollision(float retain) => currentSpeed *= retain;

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
