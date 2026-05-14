using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class AbilityWheelUI : MonoBehaviour
{
    [Header("Refs")]
    public BoatToolManager toolManager;
    public BoatMovement boatMovement;
    public GameObject visualRoot;

    [Header("Wheel Configuration")]
    public GameObject nobPrefab;
    public GameObject edgeWheelVisPrefab;
    public RectTransform edgeWheelVisContainer;
    public RectTransform pointerRect;
    public float spinSpeed = 15f;

    public bool IsOpen { get; private set; }

    private List<BoatTool> _availableTools = new List<BoatTool>();
    private List<GameObject> _spawnedNobs = new List<GameObject>();
    private GameObject _spawnedEdgeVis;
    private Dictionary<BoatTool, float> _toolAngles = new Dictionary<BoatTool, float>();
    
    private BoatTool _pendingTool;
    private float _targetZ;

    private void Awake()
    {
        if (visualRoot != null) visualRoot.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen || pointerRect == null) return;

        float currentZ = pointerRect.localEulerAngles.z;
        float newZ = Mathf.LerpAngle(currentZ, _targetZ, spinSpeed * Time.unscaledDeltaTime);
        pointerRect.localEulerAngles = new Vector3(0f, 0f, newZ);
    }

    public void Show()
    {
        if (IsOpen) return;
        
        RefreshWheel();
        
        IsOpen = true;
        _pendingTool = toolManager.CurrentTool;
        
        // Ensure pending tool is actually in the wheel, else default to first available
        if (!_availableTools.Contains(_pendingTool))
        {
            if (_availableTools.Count > 0) _pendingTool = _availableTools[0];
        }

        if (_toolAngles.ContainsKey(_pendingTool))
            _targetZ = _toolAngles[_pendingTool];

        if (visualRoot != null) visualRoot.SetActive(true);
        boatMovement.SetAbilityWheelSlow(true);
        boatMovement.SetSteeringBlocked(true);
    }

    private void RefreshWheel()
    {
        // Clear old nobs
        foreach (var nob in _spawnedNobs)
            if (nob != null) Destroy(nob);
        _spawnedNobs.Clear();

        // Clear old circle
        if (_spawnedEdgeVis != null) Destroy(_spawnedEdgeVis);
        _spawnedEdgeVis = null;

        _availableTools.Clear();
        _toolAngles.Clear();

        // Get unlocked tools
        System.Array allTools = System.Enum.GetValues(typeof(BoatTool));
        foreach (BoatTool t in allTools)
        {
            if (toolManager.IsToolUnlocked(t))
                _availableTools.Add(t);
        }

        int count = _availableTools.Count;
        if (count == 0) return;

        // Spawn one circle vis; all nobs parent into it
        Transform nobParent = edgeWheelVisContainer;
        if (edgeWheelVisPrefab != null && edgeWheelVisContainer != null)
        {
            _spawnedEdgeVis = Instantiate(edgeWheelVisPrefab, edgeWheelVisContainer);
            nobParent = _spawnedEdgeVis.transform;
        }

        // Distribute nobs evenly around the circle
        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            BoatTool tool = _availableTools[i];

            // Pointer visual is UP (pos Y), Nob visual is DOWN (neg Y).
            // So if pointer rotation is 0 (UP), nob rotation should be 180 (DOWN flipped to UP).
            float targetPointerAngle = i * angleStep;
            float nobRotation = targetPointerAngle + 180f;

            GameObject nob = Instantiate(nobPrefab, nobParent);
            nob.transform.localEulerAngles = new Vector3(0f, 0f, nobRotation);
            _spawnedNobs.Add(nob);

            TextMeshProUGUI text = nob.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
                text.text = GetToolDisplayName(tool);

            _toolAngles[tool] = targetPointerAngle;
        }

        // Ensure pointer renders on top of spawned elements
        if (pointerRect != null)
            pointerRect.SetAsLastSibling();
    }

    private string GetToolDisplayName(BoatTool tool)
    {
        switch (tool)
        {
            case BoatTool.WhirlSucker: return "Whirl";
            case BoatTool.Catapult: return "Catapult";
            case BoatTool.Lure: return "Lure";
            default: return tool.ToString();
        }
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
        if (_availableTools.Count <= 1) return;
        int idx = _availableTools.IndexOf(_pendingTool);
        if (idx == -1) idx = 0;
        _pendingTool = _availableTools[(idx + 1) % _availableTools.Count];
        _targetZ = _toolAngles[_pendingTool];
    }

    public void CycleRight()
    {
        if (_availableTools.Count <= 1) return;
        int idx = _availableTools.IndexOf(_pendingTool);
        if (idx == -1) idx = 0;
        _pendingTool = _availableTools[(idx - 1 + _availableTools.Count) % _availableTools.Count];
        _targetZ = _toolAngles[_pendingTool];
    }
}

