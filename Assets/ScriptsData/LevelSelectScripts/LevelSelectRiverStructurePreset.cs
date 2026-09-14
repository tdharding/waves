using UnityEngine;

/// <summary>
/// How the STRUCTURES of one world's level select rivers look — the generated stone itself: the
/// colour of each part of a run, its grain, the dark gathered along every seam, and the white
/// rising off the waterline up the inside of the channel.
///
/// Separate from <see cref="LevelSelectRiverWaterPreset"/> on purpose. The stone and the water
/// are two materials on two pieces of geometry, authored in two tuners against two quite
/// different sets of questions, and holding them on one asset only gave them somewhere to drift:
/// two copies of a combined preset had already ended up in two folders, one carrying tuned water
/// and default stone and the other the reverse, with the world reading whichever it was pointed
/// at and the editor's live tuner hiding the difference until a build.
///
/// The numbers live on an asset rather than on a material because the shaders read them as bare
/// $Globals: there is no property block behind them and nothing on disk remembers them, so a
/// preset plus a per-frame push is what gives them somewhere to live. Authored in
/// Tools > Waves > Level Select Run Shading Tuner, held by the Level Select Designer, and pushed
/// by <see cref="LevelSelectDesignerData.ApplyAesthetics"/>.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Level Select River Structure Preset")]
public class LevelSelectRiverStructurePreset : ScriptableObject
{
    [Tooltip("How the stone itself is shaded — the colour of each part of a run, dark along its " +
             "seams, and white rising off the waterline up the inside of the channel.")]
    public RiverRunShadingSettings runShading = new RiverRunShadingSettings();
}
