using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// How an installation outpost is built — its block, wall and lollipop tower — saved
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

    public LevelSelectDesignerData.TowerKind towerKind = LevelSelectDesignerData.TowerKind.Lollipop;
    public TurretTower                       turret    = new TurretTower();

    public bool                  columnDownstream  = false;
    public bool                  towerOnDownstream = true;
    public bool                  columnUpstream    = false;
    public bool                  towerOnUpstream   = true;
    [Min(0f)]     public float   columnHeight      = 0.2f;
    [Min(0.001f)] public float   columnRadius      = 0.1f;

    [Tooltip("The lollipop tower preset this outpost's tower is built from. Editing that preset " +
             "changes this one's tower with it.")]
    public LollipopTowerPreset towerPreset;

    [Tooltip("The turret preset this outpost's turret is built from. Editing that preset " +
             "changes this one's turret with it.")]
    public TurretTowerPreset turretPreset;

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
        towerKind     = outpost.towerKind;
        turret        = outpost.turret?.Clone();
        columnDownstream  = outpost.columnDownstream;
        towerOnDownstream = outpost.towerOnDownstream;
        columnUpstream    = outpost.columnUpstream;
        towerOnUpstream   = outpost.towerOnUpstream;
        columnHeight  = outpost.columnHeight;
        columnRadius  = outpost.columnRadius;
        towerPreset   = outpost.towerPreset;
        turretPreset  = outpost.turretPreset;
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
        outpost.towerKind     = towerKind;
        outpost.turret        = (turret ?? new TurretTower()).Clone();
        outpost.columnDownstream  = columnDownstream;
        outpost.towerOnDownstream = towerOnDownstream;
        outpost.columnUpstream    = columnUpstream;
        outpost.towerOnUpstream   = towerOnUpstream;
        outpost.columnHeight  = columnHeight;
        outpost.columnRadius  = columnRadius;
        outpost.towerPreset   = towerPreset;
        outpost.turretPreset  = turretPreset;
    }
}
