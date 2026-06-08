using System.Collections.Generic;
using UnityEngine;

public class LevelWaveModifierControllerTypeA : MonoBehaviour
{
    [System.Serializable]
    public struct SlotEntry
    {
        public SoulSlot slot;
        public float speedBoost;
        public float frequencyBoost;
        public float rippleDepthBoost;
    }

    [Header("Slots")]
    [SerializeField] private SlotEntry[] slots;

    [Header("Debug Info")]
    [SerializeField] private bool showDebug = true;
    [SerializeField] private Vector3 debugWorldPosition;
    [SerializeField] private Vector4 debugWaveCenterSent;

    private WaveMaterialController waveController;
    private Transform wavePlane;

    private static readonly List<LevelWaveModifierControllerTypeA> allModifiers = new();

    private bool hasBaseline;

    public void Init(WaveMaterialController controller)
    {
        waveController = controller;
        debugWorldPosition = transform.position;
    }

    private void Awake()
    {
        allModifiers.Add(this);
        if (waveController == null)
            waveController = FindObjectOfType<WaveMaterialController>();
        debugWorldPosition = transform.position;
    }

    private void Start()
    {
        wavePlane = LevelDataController.Instance?.GetWaveTransform();

        foreach (var entry in slots)
        {
            if (entry.slot == null) continue;
            entry.slot.SetAllowRemoval(true);
            entry.slot.onFilled.AddListener(_ => OnAnySlotFilled());
            entry.slot.onEmptied.AddListener(_ => OnAnySlotEmptied());
        }
    }

    private void OnDestroy()
    {
        allModifiers.Remove(this);
    }

    private void OnAnySlotFilled()
    {
        int filled = FilledCount();

        if (filled == 1)
        {
            hasBaseline = true;
            ApplyWaveCenter();
            LockOthers();
        }

        ApplyBoosts();
    }

    private void OnAnySlotEmptied()
    {
        int filled = FilledCount();

        if (filled == 0)
        {
            RestoreBaseline();
            UnlockAll();
        }
        else
        {
            ApplyBoosts();
        }
    }

    public void SetLocked(bool locked)
    {
        foreach (var entry in slots)
            entry.slot?.SetInteractable(!locked);
    }

    private void ApplyWaveCenter()
    {
        if (!waveController) return;

        Vector3 worldPos = transform.position;
        debugWorldPosition = worldPos;

        Vector4 wc;
        if (wavePlane != null)
        {
            Vector3 local = wavePlane.InverseTransformPoint(worldPos);
            wc = new Vector4(local.x, 0f, -local.y, 0f);
        }
        else
        {
            wc = new Vector4(worldPos.x, worldPos.y, worldPos.z, 0f);
        }

        currentCenter = wc;
    }

    private Vector4 currentCenter;

    private void ApplyBoosts()
    {
        if (!waveController || !hasBaseline) return;

        float totalSpeed = 0;
        float totalFreq = 0;
        float totalRipple = 0;

        foreach (var entry in slots)
        {
            if (entry.slot != null && entry.slot.IsFilled)
            {
                totalSpeed += entry.speedBoost;
                totalFreq += entry.frequencyBoost;
                totalRipple += entry.rippleDepthBoost;
            }
        }

        waveController.SetModifierBoost(true, currentCenter, totalFreq, totalSpeed, totalRipple);
    }

    private void RestoreBaseline()
    {
        if (!hasBaseline || !waveController) return;
        waveController.SetModifierBoost(false, Vector4.zero, 0, 0, 0);
        hasBaseline = false;
    }

    private void LockOthers()
    {
        foreach (var mod in allModifiers)
            if (mod != this) mod.SetLocked(true);
        
        var typeBMods = FindObjectsByType<LevelWaveModifierControllerTypeB>(FindObjectsSortMode.None);
        foreach (var mod in typeBMods)
            mod.SetLocked(true);
    }

    private static void UnlockAll()
    {
        foreach (var mod in allModifiers)
            mod.SetLocked(false);

        var typeBMods = FindObjectsByType<LevelWaveModifierControllerTypeB>(FindObjectsSortMode.None);
        foreach (var mod in typeBMods)
            mod.SetLocked(false);
    }

    private int FilledCount()
    {
        int count = 0;
        foreach (var entry in slots)
            if (entry.slot != null && entry.slot.IsFilled) count++;
        return count;
    }

    private void OnDrawGizmos()
    {
        if (!showDebug) return;

        Vector3 pos = transform.position;

         Gizmos.color = hasBaseline
            ? new Color(0f, 1f, 0.8f, 0.9f)
            : new Color(0.4f, 0.6f, 1f, 0.6f);

        Gizmos.DrawWireSphere(pos, 0.6f);

        float arm = 1.5f;
        Gizmos.DrawLine(pos - Vector3.right   * arm, pos + Vector3.right   * arm);
        Gizmos.DrawLine(pos - Vector3.forward * arm, pos + Vector3.forward * arm);
        Gizmos.DrawLine(pos - Vector3.up * 0.5f,    pos + Vector3.up * 0.5f);
    }
}