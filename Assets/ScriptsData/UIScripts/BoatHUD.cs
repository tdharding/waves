using UnityEngine;

/// <summary>
/// Positions this RectTransform at the boat's screen position each frame.
/// Place as a child of a Screen Space — Overlay canvas, anchored to centre (0.5, 0.5).
/// Child panels (PortalConfirmUI, junction prompts, etc.) are shown/hidden independently.
/// </summary>
public class BoatHUD : MonoBehaviour
{
    [SerializeField] private Transform boatTransform;
    [SerializeField] private Vector2   screenOffset = new Vector2(0f, 80f); // pixels above boat

    private RectTransform _rect;
    private Canvas        _canvas;

    private void Awake()
    {
        _rect   = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
    }

    private void LateUpdate()
    {
        if (boatTransform == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(boatTransform.position);

        // Behind the camera — hide everything
        if (screenPos.z < 0f)
        {
            _rect.anchoredPosition = new Vector2(-9999f, -9999f);
            return;
        }

        // Convert screen position to canvas local position
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.transform as RectTransform,
            screenPos,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main,
            out Vector2 localPoint);

        _rect.anchoredPosition = localPoint + screenOffset;
    }
}
