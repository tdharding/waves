using UnityEngine;

/// <summary>
/// Place in the level select scene near a level entrance.
/// Wire a SoulSlot (on a child collider) to the slot field.
/// When a soul is inserted, its home level's wave preset is applied as
/// a runtime override on the target GridData.
/// The soul can be clicked back out (allowRemoval = true).
/// </summary>
public class ExternalWaveModifier : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The GridData whose wave climate this modifier affects.")]
    [SerializeField] private GridData targetGridData;

    [Header("Slot")]
    [SerializeField] private SoulSlot slot;

    // ─────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────

    private void Awake()
    {
        if (slot != null)
        {
            slot.SetAllowRemoval(true);
            slot.onFilled.AddListener(OnSlotFilled);
            slot.onEmptied.AddListener(OnSlotEmptied);
        }

        RestoreFromSave();
    }

    private void RestoreFromSave()
    {
        int savedID = GameProgressData.GetExternalModifierSoulID();
        if (savedID < 0) return;

        SoulData soul = SoulRegistry.Get(savedID);
        if (soul == null) return;

        // TryInsertSoul fires onFilled → ApplyPreset automatically
        slot?.TryInsertSoul(savedID);

        Debug.Log($"[ExternalWaveModifier] Restored — soul {savedID} " +
                  $"preset '{soul.associatedWavePreset?.name}' → '{targetGridData?.levelID}'.");
    }

    // ─────────────────────────────────────────────
    // SLOT CALLBACKS
    // ─────────────────────────────────────────────

    private void OnSlotFilled(int soulIdentity)
    {
        SoulData soul = SoulRegistry.Get(soulIdentity);

        if (soul == null)
        {
            Debug.LogWarning($"[ExternalWaveModifier] Soul {soulIdentity} not found in SoulRegistry.");
            return;
        }

        if (soul.associatedWavePreset == null)
        {
            Debug.LogWarning($"[ExternalWaveModifier] Soul {soulIdentity} has no associatedWavePreset — " +
                             $"run Randomise Soul Wave Presets in the Grid Designer.");
            return;
        }

        ApplyPreset(soul);
        GameProgressData.RemoveSoulFromBoat(soulIdentity);
        GameProgressData.SaveExternalModifierSoulID(soulIdentity);

        Debug.Log($"[ExternalWaveModifier] Soul {soulIdentity} inserted — " +
                  $"preset '{soul.associatedWavePreset.name}' applied to '{targetGridData?.levelID}'.");
    }

    private void OnSlotEmptied(int soulIdentity)
    {
        if (targetGridData != null)
            targetGridData.runtimeWavePresetOverride = null;

        RefreshLinkedWavePlane();

        GameProgressData.AddSoulToBoat(soulIdentity);
        GameProgressData.SaveExternalModifierSoulID(-1);
        SoulsOnBoatDisplayManager.Instance?.ReturnSoulToDisplay(soulIdentity);

        Debug.Log($"[ExternalWaveModifier] Soul {soulIdentity} removed — override cleared on '{targetGridData?.levelID}'.");
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    private void ApplyPreset(SoulData soul)
    {
        if (targetGridData != null)
            targetGridData.runtimeWavePresetOverride = soul.associatedWavePreset;

        RefreshLinkedWavePlane();
    }

    // ─────────────────────────────────────────────
    // LEVEL SELECT WAVE PLANE SYNC
    // ─────────────────────────────────────────────

    /// <summary>
    /// Finds any LevelSelectWavePlane in the scene that is linked to the same
    /// GridData and tells it to re-read the effective preset.
    /// </summary>
    private void RefreshLinkedWavePlane()
    {
        if (targetGridData == null) return;

        var planes = Object.FindObjectsOfType<LevelSelectWavePlane>();
        foreach (var plane in planes)
        {
            if (plane.GridData == targetGridData)
                plane.Refresh();
        }
    }
}
