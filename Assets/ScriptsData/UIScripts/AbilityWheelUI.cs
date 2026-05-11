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
    public float whirlRotation = 0f;
    public float catapultRotation = 180f;

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

    public void CycleLeft()
    {
        _pendingTool = _pendingTool == BoatTool.WhirlSucker ? BoatTool.Catapult : BoatTool.WhirlSucker;
        _targetZ = RotationForTool(_pendingTool);
    }

    public void CycleRight()
    {
        _pendingTool = _pendingTool == BoatTool.WhirlSucker ? BoatTool.Catapult : BoatTool.WhirlSucker;
        _targetZ = RotationForTool(_pendingTool);
    }

    private float RotationForTool(BoatTool tool)
    {
        return tool == BoatTool.Catapult ? catapultRotation : whirlRotation;
    }
}
