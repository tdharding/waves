using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class LevelSelectObstacleManager : MonoBehaviour
{
    [Header("Identification")]
    public string obstacleID;

    [Header("Next Obstacle Target")]
    [SerializeField] private Transform nextObstacleTransform;

    [Header("Slots")]
    [SerializeField] private List<SoulSlot> slots = new List<SoulSlot>();

    [Header("Obstacle Components")]
    [SerializeField] private LevelSelectPathObstacleObject obstacleMeshObject;
    [SerializeField] private ObstacleSoulVideoPlayer soulVideoPlayer;

    [Header("Obstacle Gate Material")]
    [SerializeField] private Renderer gateRenderer;
    [SerializeField] private float tweenTime = 1f;

    private Coroutine gateMaterialRoutine;
    private static readonly int AlphaID = Shader.PropertyToID("_Alpha");

    private int filledSlots;
    private bool unlocked;

    private void Start()
    {
        foreach (SoulSlot slot in slots)
        {
            if (slot == null) continue;
            slot.SetAllowRemoval(false);
            slot.onFilled.AddListener(OnSlotFilled);
            slot.onEmptied.AddListener(OnSlotEmptied);
        }

        if (GameProgressData.IsUnlocked(obstacleID))
        {
            ApplyUnlockedState(true);
            return;
        }

        RefreshSlotState();
        CheckUnlock();
    }

    private void OnSlotFilled(int soulIdentity)
    {
        filledSlots++;
        soulVideoPlayer?.RefreshState();
        CheckUnlock();
    }

    private void OnSlotEmptied(int soulIdentity)
    {
        filledSlots = Mathf.Max(0, filledSlots - 1);
        soulVideoPlayer?.RefreshState();
    }

    private void CheckUnlock()
    {
        if (unlocked) return;
        if (filledSlots < slots.Count) return;

        GameProgressData.UnlockObstacle(obstacleID);

        if (nextObstacleTransform != null)
            LevelSelectSplineManager.Instance?.AdvanceSplineToPosition(nextObstacleTransform.position);

        ApplyUnlockedState(false);
    }

    private void ApplyUnlockedState(bool immediate)
    {
        unlocked = true;
        DisableSlots();

        if (gateMaterialRoutine != null) StopCoroutine(gateMaterialRoutine);

        if (immediate)
            gateRenderer.material.SetFloat(AlphaID, 0f);
        else
            gateMaterialRoutine = StartCoroutine(FadeGateMaterial(1f, 0f, tweenTime));

        if (obstacleMeshObject != null)
            Destroy(obstacleMeshObject.gameObject);

        soulVideoPlayer?.RefreshState();
    }

    private void DisableSlots()
    {
        foreach (SoulSlot slot in slots)
        {
            if (slot != null) slot.SetInteractable(false);
        }
    }

    private IEnumerator FadeGateMaterial(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            gateRenderer.material.SetFloat(AlphaID, Mathf.Lerp(from, to, elapsed / duration));
            yield return null;
        }
        gateRenderer.material.SetFloat(AlphaID, to);
    }

    private void RefreshSlotState()
    {
        filledSlots = 0;
        foreach (SoulSlot slot in slots)
        {
            if (slot != null && slot.IsFilled)
                filledSlots++;
        }
    }
}
