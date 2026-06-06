using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class LevelSelectMapUI : MonoBehaviour
{
    [Header("Data & Control")]
    [SerializeField] private LevelSelectDesignerData designerData;
    [SerializeField] private LevelSelectBoatControl boatControl;

    [Header("UI References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform mapContainer;
    [SerializeField] private RectTransform boatDot;
    [SerializeField] private Image background;

    [Header("Settings")]
    [SerializeField] private float padding = 40f;
    [SerializeField] private float lineWidth = 4f;
    [SerializeField] private Color pathColor = Color.white;
    [SerializeField] private Color boatColor = Color.red;
    [SerializeField] private float fadeSpeed = 5f;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    private Vector2 _minBounds;
    private Vector2 _maxBounds;
    private Vector2 _lastSize;
    private bool _isOpen;
    private float _currentAlpha;

    private void Awake()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        _isOpen = false;
        _currentAlpha = 0f;
        
        if (mapContainer != null)
            _lastSize = mapContainer.rect.size;

        if (boatDot != null)
        {
            var dotImage = boatDot.GetComponent<Image>();
            if (dotImage != null) dotImage.color = boatColor;
        }
    }

    private void Start()
    {
        if (designerData != null)
        {
            CalculateBounds();
            GenerateMap();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            _isOpen = !_isOpen;
        }

        UpdateMapFade();

        if (_isOpen)
        {
            if (mapContainer != null && mapContainer.rect.size != _lastSize)
            {
                _lastSize = mapContainer.rect.size;
                GenerateMap();
            }

            if (boatControl != null)
            {
                UpdateBoatDot();
            }
        }
    }

    private void UpdateMapFade()
    {
        if (canvasGroup == null) return;

        float targetAlpha = _isOpen ? 1f : 0f;
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);
        canvasGroup.alpha = _currentAlpha;
        canvasGroup.interactable = _isOpen && _currentAlpha > 0.9f;
        canvasGroup.blocksRaycasts = _isOpen && _currentAlpha > 0.5f;
    }

    private void ToggleMap()
    {
        _isOpen = !_isOpen;
    }

    private void CalculateBounds()
    {
        if (designerData.nodes == null || designerData.nodes.Count == 0) return;

        _minBounds = new Vector2(float.MaxValue, float.MaxValue);
        _maxBounds = new Vector2(float.MinValue, float.MinValue);

        foreach (var node in designerData.nodes)
        {
            _minBounds.x = Mathf.Min(_minBounds.x, node.worldPosition.x);
            _minBounds.y = Mathf.Min(_minBounds.y, node.worldPosition.z);
            _maxBounds.x = Mathf.Max(_maxBounds.x, node.worldPosition.x);
            _maxBounds.y = Mathf.Max(_maxBounds.y, node.worldPosition.z);
        }
    }

    private void GenerateMap()
    {
        if (mapContainer == null || designerData.paths == null) return;

        // Clear existing lines (except boat dot and background)
        foreach (Transform child in mapContainer)
        {
            if (child != boatDot && (background == null || child != background.transform))
            {
                Destroy(child.gameObject);
            }
        }

        Dictionary<string, Vector3> nodePositions = new Dictionary<string, Vector3>();
        foreach (var node in designerData.nodes)
        {
            nodePositions[node.id] = node.worldPosition;
        }

        foreach (var path in designerData.paths)
        {
            // SegmentType: MainRiver, PrimaryBranch
            if (path.segmentType == LevelSelectDesignerData.SegmentType.MainRiver ||
                path.segmentType == LevelSelectDesignerData.SegmentType.PrimaryBranch)
            {
                for (int i = 0; i < path.nodeIds.Count - 1; i++)
                {
                    if (nodePositions.TryGetValue(path.nodeIds[i], out Vector3 startWorld) &&
                        nodePositions.TryGetValue(path.nodeIds[i + 1], out Vector3 endWorld))
                    {
                        DrawLine(WorldToUISpace(startWorld), WorldToUISpace(endWorld));
                    }
                }
            }
        }
        
        if (boatDot != null) boatDot.SetAsLastSibling();
    }

    private void DrawLine(Vector2 start, Vector2 end)
    {
        GameObject lineObj = new GameObject("MapLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        lineObj.transform.SetParent(mapContainer, false);

        Image img = lineObj.GetComponent<Image>();
        img.color = pathColor;
        img.raycastTarget = false;

        RectTransform rect = lineObj.GetComponent<RectTransform>();
        Vector2 dir = end - start;
        float distance = dir.magnitude;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        rect.sizeDelta = new Vector2(distance, lineWidth);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = start;
        rect.localRotation = Quaternion.Euler(0, 0, angle);
    }

    private void UpdateBoatDot()
    {
        if (boatDot == null || boatControl == null || boatControl.BoatTransform == null) return;
        boatDot.anchoredPosition = WorldToUISpace(boatControl.BoatTransform.position);
    }

    private Vector2 WorldToUISpace(Vector3 worldPos)
    {
        Vector2 containerSize = mapContainer.rect.size;
        float usableWidth = containerSize.x - padding * 2;
        float usableHeight = containerSize.y - padding * 2;

        float worldWidth = _maxBounds.x - _minBounds.x;
        float worldHeight = _maxBounds.y - _minBounds.y;

        if (worldWidth <= 0) worldWidth = 1f;
        if (worldHeight <= 0) worldHeight = 1f;

        float scale = Mathf.Min(usableWidth / worldWidth, usableHeight / worldHeight);

        float x = (worldPos.x - _minBounds.x) * scale;
        float y = (worldPos.z - _minBounds.y) * scale;

        float mapContentWidth = worldWidth * scale;
        float mapContentHeight = worldHeight * scale;

        float offsetX = (containerSize.x - mapContentWidth) / 2f;
        float offsetY = (containerSize.y - mapContentHeight) / 2f;

        return new Vector2(x + offsetX - containerSize.x / 2f, y + offsetY - containerSize.y / 2f);
    }
}
