using UnityEngine;

public class AbilityWheelUI : MonoBehaviour
{
    [Header("Refs")]
    public BoatToolManager toolManager;
    public BoatMovement boatMovement;
    public GameObject visualRoot;

    [Header("Wheel")]
    public RectTransform wheelRect;
    public float spinSpeed = 12f;

    [Header("Notch Rotations")]
    public float whirlRotation    = 0f;
    public float catapultRotation = 180f;
    public float lureRotation     = 90f;

    public bool IsOpen { get; private set; }

    private BoatTool _pendingTool;
    private float _targetZ;

    private void Awake()
    {
        if (visualRoot != null) visualRoot.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen || wheelRect == null) return;

        float currentZ = wheelRect.localEulerAngles.z;
        float newZ = Mathf.LerpAngle(currentZ, _targetZ, spinSpeed * Time.unscaledDeltaTime);
        wheelRect.localEulerAngles = new Vector3(0f, 0f, newZ);
    }

    public void Show()
    {
        if (IsOpen) return;
        IsOpen = true;
        _pendingTool = toolManager.CurrentTool;
        _targetZ = RotationForTool(_pendingTool);
        if (visualRoot != null) visualRoot.SetActive(true);
        boatMovement.SetAbilityWheelSlow(true);
        boatMovement.SetSteeringBlocked(true);
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (visualRoot != null) visualRoot.SetActive(false);
        boatMovement.SetAbilityWheelSlow(false);
        boatMovement.SetSteeringBlocked(false);
        toolManager.SetTool(_pendingTool);
    }

    // Tool order: WhirlSucker(0°) → Lure(90°) → Catapult(180°)
    static readonly BoatTool[] ToolOrder = { BoatTool.WhirlSucker, BoatTool.Lure, BoatTool.Catapult };

    public void CycleLeft()
    {
        int idx = System.Array.IndexOf(ToolOrder, _pendingTool);
        _pendingTool = ToolOrder[(idx - 1 + ToolOrder.Length) % ToolOrder.Length];
        _targetZ = RotationForTool(_pendingTool);
    }

    public void CycleRight()
    {
        int idx = System.Array.IndexOf(ToolOrder, _pendingTool);
        _pendingTool = ToolOrder[(idx + 1) % ToolOrder.Length];
        _targetZ = RotationForTool(_pendingTool);
    }

    private float RotationForTool(BoatTool tool)
    {
        switch (tool)
        {
            case BoatTool.Catapult: return catapultRotation;
            case BoatTool.Lure:     return lureRotation;
            default:                return whirlRotation;
        }
    }
}
