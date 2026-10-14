using UnityEngine;

/// <summary>
/// A bridge's sizes as one — its deck and ramps — the part a bridge preset saves. Where the
/// bridge sits (Offset Along) is not part of it; that stays with each bridge.
/// </summary>
[System.Serializable]
public class BridgeSettings
{
    [Tooltip("How far the deck's top stands above the rim.")]
    [Min(0.01f)] public float deckHeight = 0.15f;

    [Tooltip("How wide the deck and its ramps are, along the river.")]
    [Min(0.01f)] public float deckWidth = 0.12f;

    [Tooltip("How thick the deck is, from its top down to its underside over the water.")]
    [Min(0.005f)] public float deckThickness = 0.02f;

    [Tooltip("How far each ramp reaches out from the channel's edge, away from the river, " +
             "to where it meets the rim's height.")]
    [Min(0.01f)] public float rampLength = 0.4f;

    public BridgeSettings Clone() => (BridgeSettings)MemberwiseClone();

    /// <summary>The bridge's own sizes, as a copy — nothing is shared with it.</summary>
    public static BridgeSettings Of(LevelSelectDesignerData.DesignerBridge bridge) => new BridgeSettings
    {
        deckHeight    = bridge.deckHeight,
        deckWidth     = bridge.deckWidth,
        deckThickness = bridge.deckThickness,
        rampLength    = bridge.rampLength,
    };

    /// <summary>Puts these sizes on the bridge.</summary>
    public void ApplyTo(LevelSelectDesignerData.DesignerBridge bridge)
    {
        bridge.deckHeight    = deckHeight;
        bridge.deckWidth     = deckWidth;
        bridge.deckThickness = deckThickness;
        bridge.rampLength    = rampLength;
    }
}

/// <summary>
/// A bridge's deck and ramp sizes, saved so they can be put on another bridge from the dropdown
/// in the Level Select Designer. Bridges linked to it change with it.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Bridge Preset")]
public class BridgePreset : ScriptableObject
{
    public BridgeSettings bridge = new BridgeSettings();

    /// <summary>Takes the settings, as a copy — nothing is shared with them.</summary>
    public void CopyFrom(BridgeSettings source) => bridge = (source ?? new BridgeSettings()).Clone();

    /// <summary>The preset's settings as a fresh copy — nothing is shared with it.</summary>
    public BridgeSettings Build() => (bridge ?? new BridgeSettings()).Clone();
}
