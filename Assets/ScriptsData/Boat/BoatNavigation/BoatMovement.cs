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

    [Header("Engine Audio")]
    public AudioSource boatEngineAudio;
    public float turnPitchAmount = 0.2f;
    public float speedPitchAmount = 0.25f;
    private float basePitch = 1f;

    [HideInInspector] public bool controlsEnabled = false;

    private CharacterController controller;
    private float fixedY;
    private float currentSpeed;
    private Transform waterTransform;

    // External control flags
    private bool boosting = false;
    private bool sonarSlow = false;

    // --------------------------------------------------
    // UNITY
    // --------------------------------------------------

    void Start()
    {
        controller = GetComponent<CharacterController>();
        waterTransform = LevelDataController.Instance?.GetWaveTransform();
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
    if (controller == null || !controller.enabled)
        return;

    if (PauseManager.IsPaused)
        return;

    if (waterTransform != null)
        fixedY = waterTransform.position.y;

    if (!controlsEnabled)
    {
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            0f,
            deceleration * Time.fixedDeltaTime
        );

        Vector3 move = transform.forward * currentSpeed;
        controller.Move(move * Time.fixedDeltaTime);

        Vector3 pos = transform.position;
        pos.y = fixedY;
        transform.position = pos;

        StopEngineAudio();
        return;
    }

    HandleSteering();
    HandleMovement();

    // --- DEBUG ---
   
}
    // --------------------------------------------------
    // STEERING
    // --------------------------------------------------
void HandleSteering()
{
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

    transform.Rotate(0f, turn, 0f);

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

        // Shift reduces boat speed
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (shiftHeld)
            targetSpeed *= shiftSpeedMultiplier;

        // Whirl state = Sonar active AND Space held
        bool whirlActive = sonarSlow && Input.GetKey(KeyCode.Space);
        if (whirlActive)
            targetSpeed *= whirlSpeedMultiplier;


        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            accelRate * Time.fixedDeltaTime
        );

        Vector3 move = transform.forward * currentSpeed;
        controller.Move(move * Time.fixedDeltaTime);

        // Lock Y
        Vector3 pos = transform.position;
        pos.y = fixedY;
        transform.position = pos;

        if (boatEngineAudio != null && !boatEngineAudio.isPlaying)
            boatEngineAudio.Play();
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

    public void SetSonarSlow(bool value)
    {
        sonarSlow = value;
    }

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
