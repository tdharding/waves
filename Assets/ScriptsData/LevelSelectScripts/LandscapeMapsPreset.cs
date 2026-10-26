using UnityEngine;

/// <summary>
/// One world's painted landscape: the base height map and the rocky noise map together. Painted in
/// the Level Select Designer (Landscape ▾ > Landscape Maps), which edits a working copy and only
/// writes here on Save. Kept in the river presets folder with the other world presets.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Level Select Landscape Maps Preset")]
public class LandscapeMapsPreset : ScriptableObject
{
    public LandscapeMaps maps = new LandscapeMaps();
}
