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
    [SerializeField] private Vector4 debugMaterialWaveCenter;

    private WaveMaterialController waveController;
    private Transform wavePlane;

    private static readonly List<LevelWaveModifierControllerTypeA> allModifiers = new();

    private bool hasBaseline;
    private float baselineSpeed;
    private float baselineFrequency;
    private float baselineRippleDepth;
    private Vector4 baselineWaveCenter;

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
            CaptureBaseline();
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

    private void CaptureBaseline()
    {
        if (!waveController || !waveController.waveMaterial) return;
        baselineSpeed       = waveController.waveMaterial.GetFloat("_Speed");
        baselineFrequency   = waveController.waveMaterial.GetFloat("_Frequency");
        baselineRippleDepth = waveController.waveMaterial.GetFloat("_RippleDepth");
        baselineWaveCenter  = waveController.waveMaterial.GetVector("_WaveCenter");
        hasBaseline = true;
    }

    private void ApplyWaveCenter()
    {
        if (!waveController || !waveController.waveMaterial) return;

        Vector3 worldPos = transform.position;
        debugWorldPosition = worldPos;

        Vector4 wc;
        if (wavePlane != null)
        {
            // PositionIn in the HLSL is the wave plane's object space.
            // On a -90° X plane: objectX = worldX/S, objectY = -worldZ/S.
            // Shader formula: toCenter = PositionIn.xy - float2(WaveCenter.x, -WaveCenter.z)
            // → WaveCenter.x = local.x, WaveCenter.z = -local.y
            Vector3 local = wavePlane.InverseTransformPoint(worldPos);
            wc = new Vector4(local.x, 0f, -local.y, 0f);
        }
        else
        {
            // Fallback with no scale correction
            wc = new Vector4(worldPos.x, worldPos.y, worldPos.z, 0f);
        }

        waveController.waveMaterial.SetVector("_WaveCenter", wc);
        debugWaveCenterSent     = wc;
        debugMaterialWaveCenter = wc;
    }

    private void ApplyBoosts()
    {
        if (!waveController || !waveController.waveMaterial) return;

        float speed     = baselineSpeed;
        float frequency = baselineFrequency;
        float ripple    = baselineRippleDepth;

        foreach (var entry in slots)
        {
            if (entry.slot != null && entry.slot.IsFilled)
            {
                speed     += entry.speedBoost;
                frequency += entry.frequencyBoost;
                ripple    += entry.rippleDepthBoost;
            }
        }

        waveController.waveMaterial.SetFloat("_Speed",       speed);
        waveController.waveMaterial.SetFloat("_Frequency",   frequency);
        waveController.waveMaterial.SetFloat("_RippleDepth", ripple);
    }

    private void RestoreBaseline()
    {
        if (!hasBaseline || !waveController || !waveController.waveMaterial) return;
        waveController.waveMaterial.SetFloat("_Speed",       baselineSpeed);
        waveController.waveMaterial.SetFloat("_Frequency",   baselineFrequency);
        waveController.waveMaterial.SetFloat("_RippleDepth", baselineRippleDepth);
        waveController.waveMaterial.SetVector("_WaveCenter", baselineWaveCenter);
        hasBaseline = false;

        debugMaterialWaveCenter = baselineWaveCenter;
    }

    private void LockOthers()
    {
        foreach (var mod in allModifiers)
            if (mod != this) mod.SetLocked(true);
    }

    private static void UnlockAll()
    {
        foreach (var mod in allModifiers)
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

#if UNITY_EDITOR
        string state = hasBaseline ? "ACTIVE" : "idle";
        string label = $"WaveModifier [{state}]\n" +
                       $"World pos: ({pos.x:F3}, {pos.y:F3}, {pos.z:F3})\n" +
                       $"WaveCenter (obj spc): ({debugWaveCenterSent.x:F3}, {debugWaveCenterSent.y:F3}, {debugWaveCenterSent.z:F3})\n" +
                       $"Material WC: ({debugMaterialWaveCenter.x:F3}, {debugMaterialWaveCenter.y:F3}, {debugMaterialWaveCenter.z:F3})";
        UnityEditor.Handles.Label(pos + Vector3.up * 1.0f, label);
#endif
    }
}
