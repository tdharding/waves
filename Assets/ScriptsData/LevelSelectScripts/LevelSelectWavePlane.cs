using UnityEngine;

/// <summary>
/// Attach to a wave plane in the level select scene alongside its arena.
/// On Start it reads the linked GridData's effective WavePreset
/// (runtimeWavePresetOverride if set, otherwise gameplayWavePreset) and
/// pushes all WaveState parameters to this plane's material instance.
///
/// Call Refresh() any time the grid data's preset changes at runtime
/// (ExternalWaveModifier does this automatically when a soul is slotted).
/// </summary>
public class LevelSelectWavePlane : MonoBehaviour
{
    // Set by LevelSelectArenaController — do not assign in inspector.
    private GridData _gridData;

    [Header("Renderer")]
    [Tooltip("Leave empty to use the Renderer on this GameObject.")]
    [SerializeField] private Renderer wavePlaneRenderer;

    // Per-instance material created in Refresh so Awake order doesn't matter.
    private Material _materialInstance;

    public GridData GridData => _gridData;

    public void SetGridData(GridData data) => _gridData = data;

    // ─────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────

    private void Start()
    {
        Refresh();
    }

    // ─────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────

    /// <summary>
    /// Re-reads the grid data's effective preset and applies it to the material.
    /// Safe to call before Start (e.g. from ExternalWaveModifier.Awake restore path).
    /// </summary>
    public void Refresh()
    {
        if (_gridData == null) return;

        EnsureMaterialInstance();
        if (_materialInstance == null) return;

        WavePreset preset = _gridData.runtimeWavePresetOverride != null
            ? _gridData.runtimeWavePresetOverride
            : _gridData.gameplayWavePreset;

        if (preset == null) return;

        ApplyState(preset.state);
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    private void EnsureMaterialInstance()
    {
        if (_materialInstance != null) return;

        if (wavePlaneRenderer == null)
            wavePlaneRenderer = GetComponent<Renderer>();

        if (wavePlaneRenderer != null)
            _materialInstance = wavePlaneRenderer.material; // creates per-instance copy
    }

    private void ApplyState(WaveMaterialController.WaveState state)
    {
        _materialInstance.SetFloat("_Frequency",                  state.Frequency);
        _materialInstance.SetFloat("_Speed",                      state.Speed);
        _materialInstance.SetFloat("_RippleDepth",                state.RippleDepth);
        _materialInstance.SetFloat("_Smoothness",                 state.Smoothness);
        _materialInstance.SetFloat("_Transparency",               state.Transparency);
        _materialInstance.SetFloat("_Strength",                   state.Strength);
        _materialInstance.SetFloat("_TroughRingWave1Brightness",  state.TroughRingWave1Brightness);
        _materialInstance.SetFloat("_PeakRingWave2Brightness",    state.PeakRingWave2Brightness);
    }
}
