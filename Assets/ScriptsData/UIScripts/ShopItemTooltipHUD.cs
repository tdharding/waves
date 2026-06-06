using UnityEngine;
using TMPro;

/// <summary>
/// Positions a tooltip above the hovered shop item in screen space.
/// Working similarly to BoatHUD but for shop items.
/// </summary>
public class ShopItemTooltipHUD : MonoBehaviour
{
    public static ShopItemTooltipHUD Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text   priceText;
    [SerializeField] private Vector2    screenOffset = new Vector2(0f, 100f);

    private RectTransform _rect;
    private Canvas        _canvas;
    private Transform     _targetTransform;
    private bool          _isShowing;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _rect   = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        
        if (panel != null) panel.SetActive(false);
    }

    public void Show(Transform target, int price)
    {
        _targetTransform = target;
        if (priceText != null) priceText.text = price.ToString() + " Orbs";
        if (panel != null) panel.SetActive(true);
        _isShowing = true;
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        _isShowing = false;
        _targetTransform = null;
    }

    private void LateUpdate()
    {
        if (!_isShowing || _targetTransform == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(_targetTransform.position);

        // Behind the camera — hide
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
