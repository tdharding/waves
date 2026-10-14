using UnityEngine;

/// <summary>
/// A wall lip's sizes — height and thickness — saved so they can be put on another wall from the
/// dropdown in the Level Select Designer.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Wall Lip Preset")]
public class WallLipPreset : ScriptableObject
{
    public WallLip lip = new WallLip();

    /// <summary>Takes the lip's sizes, as a copy — nothing is shared with it.</summary>
    public void CopyFrom(WallLip source) => lip = (source ?? new WallLip()).Clone();

    /// <summary>The preset's sizes as a fresh lip — nothing is shared with it.</summary>
    public WallLip Build() => (lip ?? new WallLip()).Clone();
}
