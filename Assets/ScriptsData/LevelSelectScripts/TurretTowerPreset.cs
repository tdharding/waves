using UnityEngine;

/// <summary>
/// A turret tower's sizes — body, nose and rest pitch — saved so they can be put on another
/// turret from the dropdown in the Level Select Designer. Where the turret stands, and its
/// placement scale, are not part of it; nor is how it moves, which is set for every turret on the
/// scene's TurretTowerAnimation.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Turret Tower Preset")]
public class TurretTowerPreset : ScriptableObject
{
    public TurretTower turret = new TurretTower();

    /// <summary>Takes the turret's settings, as a copy — nothing is shared with it.</summary>
    public void CopyFrom(TurretTower source) => turret = (source ?? new TurretTower()).Clone();

    /// <summary>The preset's settings as a fresh turret — nothing is shared with it.</summary>
    public TurretTower Build() => (turret ?? new TurretTower()).Clone();
}
