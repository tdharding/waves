using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BoatAnchorController : MonoBehaviour
{
    [Header("Refs")]
    public BoatMovement boat;
    public RectTransform anchorUI;

    [Header("Wheel Controller")]
    public SteeringWheelControllerV2 steeringWheelController;

    [Header("UI Overlay Camera Controller")]
    public UIOverlayCameraController uiOverlayController;

    [Header("Zoom Controller")]
    public BoatCameraZoom zoomController;

    [Header("Cursor")]
    public VirtualCursor virtualCursor;
    public RectTransform cursorImage;

    [Header("Anchor UI Movement")]
    public float upY = 0f;
    public float downY = -650f;
    public float uiSpeed = 500f;

    [Header("Audio Sources")]
    public AudioSource dropSFX;
    public AudioSource impactSFX;
    public AudioSource hoistSFX;

    [Header("Maze Walls Fade")]
    public Material mazeWallsMaterial;
    [Range(0f, 1f)] public float wallsTargetAlpha = 0.8f;

    // ========================================================
    // Anchor UI Text
    // ========================================================
    [Header("Anchor UI Text")]
    public TMP_Text anchorStateText;
    public string anchorUpText = "Anchor Up";
    public string anchorDownText = "Anchor Down";
    // ========================================================

    public bool IsAnchorDown => anchorDown;

    private bool anchorDown = false;
    private bool isAnimatingAnchorUI = false;

    // --------------------------------------------------------
    void Start()
    {
        cursorImage?.gameObject.SetActive(false);

        if (anchorStateText != null)
            anchorStateText.text = anchorUpText;
    }

    // --------------------------------------------------------
    public void InitializeAnchorState()
    {
        anchorDown = false;
        isAnimatingAnchorUI = false;

        if (anchorUI != null)
        {
            Vector2 p = anchorUI.anchoredPosition;
            p.y = upY;
            anchorUI.anchoredPosition = p;
        }

        steeringWheelController.EnableWheel();
        uiOverlayController?.ShowUI();

        cursorImage?.gameObject.SetActive(false);

        zoomController?.ApplyAnchorZoom(false);

        virtualCursor.SetNormalSensitivity();

        if (anchorStateText != null)
            anchorStateText.text = anchorUpText;
    }

    // --------------------------------------------------------
    void Update()
    {
        if (PauseManager.IsPaused)
            return;

        if (Input.GetKeyDown(KeyCode.A))
            ToggleAnchor();

        if (isAnimatingAnchorUI)
            AnimateAnchorUI();

        if (anchorDown && cursorImage != null)
            cursorImage.position = virtualCursor.Position;
    }

    // --------------------------------------------------------
    void ToggleAnchor()
    {
        if (isAnimatingAnchorUI)
            return;

        anchorDown = !anchorDown;

        if (anchorStateText != null)
            anchorStateText.text = anchorDown ? anchorDownText : anchorUpText;

        if (anchorDown) dropSFX?.Play();
        else hoistSFX?.Play();

        if (!anchorDown)
            virtualCursor.ResetPosition();

        ApplyAnchorState();
    }

    // --------------------------------------------------------
    void ApplyAnchorState()
    {
        if (anchorDown)
        {
            steeringWheelController.DisableWheel();
            uiOverlayController?.HideUI();
        }
        else
        {
            steeringWheelController.EnableWheel();
            uiOverlayController?.ShowUI();
        }

        cursorImage?.gameObject.SetActive(anchorDown);

        boat.controlsEnabled = !anchorDown;
        if (anchorDown)
            boat.StopBoatMovement();

        isAnimatingAnchorUI = true;

        zoomController?.ApplyAnchorZoom(anchorDown);
    }

    // --------------------------------------------------------
    void AnimateAnchorUI()
    {
        float targetY = anchorDown ? downY : upY;

        // Maze walls fade synced with zoom
        if (mazeWallsMaterial != null && zoomController != null)
        {
            float t = zoomController.ZoomT;
            float target = anchorDown ? wallsTargetAlpha : 1f;

            Color c = mazeWallsMaterial.color;
            c.a = Mathf.Lerp(1f, target, t);
            mazeWallsMaterial.color = c;
        }

        Vector2 pos = anchorUI.anchoredPosition;
        pos.y = Mathf.MoveTowards(pos.y, targetY, uiSpeed * Time.deltaTime);
        anchorUI.anchoredPosition = pos;

        if (Mathf.Abs(pos.y - targetY) < 0.1f)
        {
            isAnimatingAnchorUI = false;

            dropSFX?.Stop();
            hoistSFX?.Stop();

            if (anchorDown)
            {
                impactSFX?.Play();
                virtualCursor.SetAnchorSensitivity();
            }
            else
            {
                virtualCursor.SetNormalSensitivity();
            }
        }
    }
}
