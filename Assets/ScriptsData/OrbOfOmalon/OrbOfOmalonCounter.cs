using UnityEngine;
using TMPro;
using System.Collections;

public class OrbsOfOmalonCounter : MonoBehaviour
{
    public static OrbsOfOmalonCounter Instance { get; private set; }

    [Header("References")]
    [SerializeField] private TMP_Text counterText;
    [SerializeField] private GameObject displayRoot;

    [Header("Settings")]
    [SerializeField] private float displayDuration = 3f;

    private static int _collectedCount;
    public static int CollectedCount => _collectedCount;

    private Coroutine _hideCoroutine;
    private bool _isForceVisible;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        if (displayRoot != null)
            displayRoot.SetActive(false);
            
        UpdateText();
    }

    public static void ResetCount()
    {
        _collectedCount = 0;
        if (Instance != null) Instance.UpdateText();
    }

    public static void AddOrb()
    {
        _collectedCount++;
        if (Instance != null)
        {
            Instance.UpdateText();
            Instance.ShowBriefly();
        }
        Debug.Log($"[OrbsOfOmalonCounter] Orbs collected: {_collectedCount}");
    }

    public void SetForceVisible(bool visible)
    {
        _isForceVisible = visible;
        UpdateVisibility();
    }

    public void ShowBriefly()
    {
        gameObject.SetActive(true);

        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        _hideCoroutine = StartCoroutine(ShowBrieflyRoutine());
    }

    private IEnumerator ShowBrieflyRoutine()
    {
        if (displayRoot != null)
            displayRoot.SetActive(true);
            
        yield return new WaitForSeconds(displayDuration);
        
        _hideCoroutine = null;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (displayRoot == null) return;
        
        bool shouldBeVisible = _isForceVisible || _hideCoroutine != null;
        displayRoot.SetActive(shouldBeVisible);
    }

    private void UpdateText()
    {
        if (counterText != null)
            counterText.text = _collectedCount.ToString();
    }
}
