using UnityEngine;

/// <summary>
/// The world's white fade — see <see cref="WhiteFadeSettings"/>. Its own preset rather than a
/// part of the structure or landscape preset because it belongs to neither: the runs, the hills
/// and the spikes all wear it, and it is tuned from both of their tuners. Kept in the same
/// presets folder, held by the Level Select Designer and pushed by
/// <see cref="LevelSelectDesignerData.ApplyAesthetics"/>.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Level Select White Fade Preset")]
public class LevelSelectWhiteFadePreset : ScriptableObject
{
    public WhiteFadeSettings whiteFade = new WhiteFadeSettings();
}
