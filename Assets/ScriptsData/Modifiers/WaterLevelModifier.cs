using System.Collections;
using UnityEngine;

/// <summary>
/// Place in a gameplay level scene.
/// Slot A raises the wave plane Y; Slot B lowers it.
/// Offsets stack — both slots filled = raise + lower combined.
/// The transition is animated over transitionDuration seconds.
/// </summary>
public class WaterLevelModifier : MonoBehaviour
{
    [Header("Slots")]
    [SerializeField] private SoulSlot raiseSlot;
    [SerializeField] private SoulSlot lowerSlot;

    [Header("Settings")]
    [SerializeField] private float transitionDuration = 3f;
    [SerializeField] private bool debugLog = false;

    // ── Tier Awareness ──
    [Header("Tier Info (set at runtime by LevelSpawner)")]
    [SerializeField] private string spawnedTierName;
    [SerializeField] private string spawnedFloorLabel;
    [SerializeField] private int    spawnedTierSlot;
    [SerializeField] private float  spawnedTierY;
    [SerializeField] private float  raiseAmount;
    [SerializeField] private float  lowerAmount;

    // Called by LevelSpawner when this modifier is placed on a tier.
    // slotIndex = index into tierYOffsets. offsets must be sorted high→low (e.g. +2, 0, -2, -4).
    public void Init(int slotIndex, float[] offsets, string tierName = "", float baselineWaterY = 0f)
    {
        if (offsets == null || offsets.Length == 0) return;
        slotIndex = Mathf.Clamp(slotIndex, 0, offsets.Length - 1);

        spawnedTierName  = tierName;
        spawnedTierSlot  = slotIndex;
        spawnedTierY     = baselineWaterY + offsets[slotIndex];
        spawnedFloorLabel = FloorLabel(slotIndex, offsets);

        bool hasAbove = slotIndex > 0;
        bool hasBelow = slotIndex < offsets.Length - 1;

        // Distance to adjacent tier above / below
        raiseAmount = hasAbove ? offsets[slotIndex - 1] - offsets[slotIndex] : 0f;
        lowerAmount = hasBelow ? offsets[slotIndex] - offsets[slotIndex + 1] : 0f;

        // Disable slots that have no valid direction
        if (raiseSlot != null) raiseSlot.gameObject.SetActive(hasAbove);
        if (lowerSlot != null) lowerSlot.gameObject.SetActive(hasBelow);

        if (debugLog) Debug.Log($"[WaterLevelModifier] Init — tier='{spawnedTierName}' floor={spawnedFloorLabel} slot={spawnedTierSlot} y={spawnedTierY}, raise={raiseAmount}, lower={lowerAmount}, hasAbove={hasAbove}, hasBelow={hasBelow}");
    }

    private Transform sonarGridParent;

    private static readonly int ArenaMaskID = Shader.PropertyToID("_ArenaMask");

    private Transform wavePlane;
    private Material  waveMaterial;
    private float     baselineY;
    private float     raiseOffset;
    private float     lowerOffset;
    private Coroutine tweenRoutine;

    private void Start()
    {
        wavePlane       = LevelDataController.Instance?.GetWaveTransform();
        sonarGridParent = LevelDataController.Instance?.GetSonarGridParent();

        if (wavePlane != null)
        {
            // Use the tier's authored Y as baseline, not the wave plane's current position.
            // The wave plane starts at 0 for all tiers, so capturing position.y here
            // would give every modifier a baseline of 0 regardless of which floor they're on.
            baselineY    = spawnedTierY;
            var renderer = wavePlane.GetComponent<Renderer>();
            if (renderer != null)
                waveMaterial = renderer.sharedMaterial;
        }

        if (raiseSlot != null)
        {
            raiseSlot.onFilled.AddListener(_ => SetRaise(raiseAmount));
            raiseSlot.onEmptied.AddListener(_ => SetRaise(0f));
        }

        if (lowerSlot != null)
        {
            lowerSlot.onFilled.AddListener(_ => SetLower(lowerAmount));
            lowerSlot.onEmptied.AddListener(_ => SetLower(0f));
        }
    }

    private void SetRaise(float value)
    {
        raiseOffset = value;
        if (debugLog) Debug.Log($"[WaterLevelModifier] SetRaise — value={value}, baselineY={baselineY}, raiseOffset={raiseOffset}, lowerOffset={lowerOffset}, targetY={baselineY + raiseOffset + lowerOffset}");
        TweenToTarget();
    }

    private void SetLower(float value)
    {
        lowerOffset = -value;
        if (debugLog) Debug.Log($"[WaterLevelModifier] SetLower — value={value}, baselineY={baselineY}, raiseOffset={raiseOffset}, lowerOffset={lowerOffset}, targetY={baselineY + raiseOffset + lowerOffset}");
        TweenToTarget();
    }

    private void TweenToTarget()
    {
        if (wavePlane == null)
        {
            Debug.LogWarning("[WaterLevelModifier] TweenToTarget called but wavePlane is null!");
            return;
        }

        float targetY = baselineY + raiseOffset + lowerOffset;

        if (tweenRoutine != null)
            StopCoroutine(tweenRoutine);

        float fromGridY = sonarGridParent != null ? sonarGridParent.position.y : 0f;
        float toGridY   = fromGridY + (targetY - wavePlane.position.y);
        tweenRoutine = StartCoroutine(TweenY(wavePlane.position.y, targetY, fromGridY, toGridY));
    }

    private IEnumerator TweenY(float fromY, float toY, float fromGridY, float toGridY)
    {
        float elapsed = 0f;

        // Anchor mask values to the actual wave Y positions so they stay in sync
        // regardless of which tier's baseline initiated the tween.
        Vector4 maskBase = waveMaterial != null
            ? waveMaterial.GetVector(ArenaMaskID)
            : Vector4.zero;

        Vector4 maskFrom = maskBase;
        maskFrom.y = fromY;

        Vector4 maskTo = maskBase;
        maskTo.y = toY;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);

            Vector3 pos = wavePlane.position;
            pos.y = Mathf.Lerp(fromY, toY, t);
            wavePlane.position = pos;

            if (waveMaterial != null)
                waveMaterial.SetVector(ArenaMaskID, Vector4.Lerp(maskFrom, maskTo, t));

            if (sonarGridParent != null)
            {
                Vector3 gp = sonarGridParent.position;
                gp.y = Mathf.Lerp(fromGridY, toGridY, t);
                sonarGridParent.position = gp;
            }

            yield return null;
        }

        Vector3 final = wavePlane.position;
        final.y = toY;
        wavePlane.position = final;

        if (waveMaterial != null)
            waveMaterial.SetVector(ArenaMaskID, maskTo);

        if (sonarGridParent != null)
        {
            Vector3 gFinal = sonarGridParent.position;
            gFinal.y = toGridY;
            sonarGridParent.position = gFinal;
        }

        tweenRoutine = null;
    }

    // ── Floor Label ──
    // Order-independent: sorts offsets by value to find ground and count floors.
    // G = ground, F1/F2 = floors above (+Y), B-1/B-2 = basements below (-Y).
    public static string FloorLabel(int slotIndex, float[] offsets)
    {
        if (offsets == null || slotIndex < 0 || slotIndex >= offsets.Length) return "?";

        float y = offsets[slotIndex];

        // Sort unique values low→high to get a stable floor sequence
        var sorted = new System.Collections.Generic.List<float>(offsets);
        sorted.Sort();

        // Ground = value closest to 0 in sorted list
        int groundIdx = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < sorted.Count; i++)
        {
            float d = Mathf.Abs(sorted[i]);
            if (d < minDist) { minDist = d; groundIdx = i; }
        }

        int yIdx  = sorted.IndexOf(y);
        int delta = yIdx - groundIdx;

        if (delta == 0) return "G";
        if (delta >  0) return $"F{delta}";
        return $"B{delta}"; // e.g. B-1, B-2
    }
}
