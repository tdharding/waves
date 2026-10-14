using UnityEngine;

/// <summary>
/// A souls pool tower's sizes — base tiers, column and bowl — saved so they can be put on another
/// pool's tower from the dropdown in the Level Select Designer. The placement scale is not part
/// of it; that stays with each pool.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Souls Pool Tower Preset")]
public class SoulsPoolTowerPreset : ScriptableObject
{
    public SoulsPoolTower tower = new SoulsPoolTower();

    /// <summary>Takes the tower's sizes, as a copy — nothing is shared with it.</summary>
    public void CopyFrom(SoulsPoolTower source) => tower = (source ?? new SoulsPoolTower()).Clone();

    /// <summary>The preset's sizes as a fresh tower — nothing is shared with it.</summary>
    public SoulsPoolTower Build() => (tower ?? new SoulsPoolTower()).Clone();
}
