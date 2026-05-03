using UnityEngine;

public class SteeringWheelControllerV2 : MonoBehaviour
{
    [Header("References")]
    public Transform ballParent;   // Rotates on Z (arc)
    public Transform ballVisual;   // Rolls on X

    [Header("Z Rotation Values")]
    public float leftZ = 0f;
    public float centerZ = 155f;
    public float rightZ = 309.5f;

    [Header("Tuning")]
    public float rotateSpeed = 140f;          // Base degrees per second
    public float ballSpinMultiplier = 2f;

    [Header("Shift Turn Modifier")]
    public float shiftTurnMultiplier = 1.75f; // How much sharper turning is with Shift

    [Header("State")]
    public bool wheelActive = false;

    // Authoritative steering value
    private float currentZ;

    // Cache authored rotation so X/Y never drift
    private float baseX;
    private float baseY;

    // --------------------------------------------------
    // UNITY
    // --------------------------------------------------

    void Awake()
    {
        Vector3 euler = ballParent.localEulerAngles;
        baseX = euler.x;
        baseY = euler.y;
    }

    void Start()
    {
        currentZ = centerZ;
        ApplyRotation();
    }

    void Update()
    {
        if (!wheelActive)
            return;

        if (Input.GetKey(KeyCode.LeftArrow))
            TurnLeft();

        if (Input.GetKey(KeyCode.RightArrow))
            TurnRight();
    }

    // --------------------------------------------------
    // TURN LOGIC
    // --------------------------------------------------

    void TurnLeft()
    {
        if (currentZ <= leftZ)
            return;

        float speed = GetEffectiveRotateSpeed();
        float spin = GetEffectiveBallSpinMultiplier();

        float delta = speed * Time.deltaTime;

        currentZ -= delta;
        currentZ = Mathf.Max(currentZ, leftZ);

        ApplyRotation();

        ballVisual.Rotate(
            -delta * spin,
            0f,
            0f,
            Space.Self
        );
    }

    void TurnRight()
    {
        if (currentZ >= rightZ)
            return;

        float speed = GetEffectiveRotateSpeed();
        float spin = GetEffectiveBallSpinMultiplier();

        float delta = speed * Time.deltaTime;

        currentZ += delta;
        currentZ = Mathf.Min(currentZ, rightZ);

        ApplyRotation();

        ballVisual.Rotate(
            +delta * spin,
            0f,
            0f,
            Space.Self
        );
    }

    // --------------------------------------------------
    // MODIFIERS
    // --------------------------------------------------

    float GetEffectiveRotateSpeed()
    {
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            return rotateSpeed * shiftTurnMultiplier;

        return rotateSpeed;
    }

    float GetEffectiveBallSpinMultiplier()
    {
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            return ballSpinMultiplier * shiftTurnMultiplier;

        return ballSpinMultiplier;
    }

    // --------------------------------------------------
    // APPLY
    // --------------------------------------------------

    void ApplyRotation()
    {
        ballParent.localEulerAngles = new Vector3(
            baseX,
            baseY,
            currentZ
        );
    }

    // --------------------------------------------------
    // PUBLIC API (BoatMovement)
    // --------------------------------------------------

    /// <summary>
    /// -1 = full left, 0 = center, +1 = full right
    /// </summary>
    public float SteeringNormalized
    {
        get
        {
            float t01 = Mathf.InverseLerp(leftZ, rightZ, currentZ);
            return Mathf.Lerp(-1f, 1f, t01);
        }
    }

    public void EnableWheel()
    {
        wheelActive = true;
    }

    public void DisableWheel()
    {
        wheelActive = false;
    }
}
