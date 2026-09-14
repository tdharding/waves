using UnityEngine;

/// <summary>
/// How the WATER of one world's level select rivers looks — the lines drawn on the surface, along
/// a river's banks and in rings out of a pool's middle.
///
/// The water and the stone it runs through are two different materials on two different pieces of
/// geometry, tuned against each other but never together, so they are two different presets: a
/// water look can be carried onto a world whose stone is already settled without dragging the
/// stone along with it. See <see cref="LevelSelectRiverStructurePreset"/> for the stone.
///
/// The numbers live on an asset rather than on a material because the shaders read them as bare
/// $Globals: there is no property block behind them and nothing on disk remembers them, so a
/// preset plus a per-frame push is what gives them somewhere to live. Authored in
/// Tools > Waves > Level Select River Tuner, held by the Level Select Designer, and pushed by
/// <see cref="LevelSelectDesignerData.ApplyAesthetics"/>.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Level Select River Water Preset")]
public class LevelSelectRiverWaterPreset : ScriptableObject
{
    [Tooltip("The lines drawn on the water — along a river's banks, and in rings out of a " +
             "pool's middle.")]
    public RiverEdgeRippleSettings edgeRipples = new RiverEdgeRippleSettings();
}
