using UnityEngine;

/// <summary>
/// How one world's landscape hills look — the three stone variants and which part of the
/// landscape wears each. Its own preset, apart from the river's two, because it is its own
/// material authored in its own tuner (Tools > Waves > Level Select Landscape Tuner). Kept in the
/// same presets folder, held by the Level Select Designer and pushed by
/// <see cref="LevelSelectDesignerData.ApplyAesthetics"/>.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Level Select Landscape Preset")]
public class LevelSelectLandscapePreset : ScriptableObject
{
    public LandscapeShadingSettings landscapeShading = new LandscapeShadingSettings();
}
