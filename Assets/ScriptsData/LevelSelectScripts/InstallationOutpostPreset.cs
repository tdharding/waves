using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// How an installation outpost is built — its block, wall, lollipop tower and observers — saved
/// so it can be put on another outpost from the dropdown in the Level Select Designer. Where an
/// outpost stands (its path, how far along, which bank) is not part of it; that stays with each
/// outpost.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Installation Outpost Preset")]
public class InstallationOutpostPreset : ScriptableObject
{
    [Min(0.01f)] public float width         = 1.2f;
    [Min(0.01f)] public float depth         = 0.8f;
    [Min(0f)]    public float height        = 0.6f;
    [Min(0f)]    public float wallThickness = 0.05f;
    [Min(0f)]    public float wallHeight    = 0.1f;

    public LollipopTower tower       = new LollipopTower();
    public Vector2       towerOffset = Vector2.zero;

    public List<LevelSelectDesignerData.OutpostObserver> observers = new();

    /// <summary>Takes every setting from the outpost, as copies — nothing is shared with it.</summary>
    public void CopyFrom(LevelSelectDesignerData.DesignerOutpost outpost)
    {
        width         = outpost.width;
        depth         = outpost.depth;
        height        = outpost.height;
        wallThickness = outpost.wallThickness;
        wallHeight    = outpost.wallHeight;
        tower         = outpost.tower?.Clone();
        towerOffset   = outpost.towerOffset;
        observers     = (outpost.observers ?? new List<LevelSelectDesignerData.OutpostObserver>())
                        .Select(o => o.Clone()).ToList();
    }

    /// <summary>Puts every setting on the outpost, as copies — nothing is shared with it.</summary>
    public void ApplyTo(LevelSelectDesignerData.DesignerOutpost outpost)
    {
        outpost.width         = width;
        outpost.depth         = depth;
        outpost.height        = height;
        outpost.wallThickness = wallThickness;
        outpost.wallHeight    = wallHeight;
        outpost.tower         = tower?.Clone();
        outpost.towerOffset   = towerOffset;
        outpost.observers     = (observers ?? new List<LevelSelectDesignerData.OutpostObserver>())
                                .Select(o => o.Clone()).ToList();
    }
}
