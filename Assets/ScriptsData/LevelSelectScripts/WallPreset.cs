using UnityEngine;

/// <summary>
/// A wall's settings as one — its height, thickness and lip — the part a wall preset saves.
/// </summary>
[System.Serializable]
public class WallSettings
{
    [Tooltip("How far the wall's top stands above the water, at every node that doesn't override it.")]
    [Min(0f)] public float height = 0.3f;

    [Tooltip("The wall's thickness, across.")]
    [Min(0.001f)] public float thickness = 0.09f;

    [Tooltip("Stand a square lip on the wall's top all the way along.")]
    public bool hasLip;

    [Tooltip("The lip's sizes.")]
    public WallLip lip = new WallLip();

    public WallSettings Clone()
    {
        var copy = (WallSettings)MemberwiseClone();
        copy.lip = (lip ?? new WallLip()).Clone();
        return copy;
    }

    /// <summary>The wall's own settings, as a copy — nothing is shared with it.</summary>
    public static WallSettings Of(LevelSelectDesignerData.DesignerWall wall) => new WallSettings
    {
        height    = wall.height,
        thickness = wall.thickness,
        hasLip    = wall.hasLip,
        lip       = (wall.lip ?? new WallLip()).Clone(),
    };

    /// <summary>Puts these settings on the wall, as a copy — nothing is shared with it.</summary>
    public void ApplyTo(LevelSelectDesignerData.DesignerWall wall)
    {
        wall.height    = height;
        wall.thickness = thickness;
        wall.hasLip    = hasLip;
        wall.lip       = (lip ?? new WallLip()).Clone();
    }
}

/// <summary>
/// A wall's height, thickness and lip, saved so they can be put on another wall from the dropdown
/// in the Level Select Designer. Walls linked to it change with it.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Wall Preset")]
public class WallPreset : ScriptableObject
{
    public WallSettings wall = new WallSettings();

    /// <summary>Takes the settings, as a copy — nothing is shared with them.</summary>
    public void CopyFrom(WallSettings source) => wall = (source ?? new WallSettings()).Clone();

    /// <summary>The preset's settings as a fresh copy — nothing is shared with it.</summary>
    public WallSettings Build() => (wall ?? new WallSettings()).Clone();
}
