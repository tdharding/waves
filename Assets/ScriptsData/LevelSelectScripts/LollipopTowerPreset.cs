using UnityEngine;

/// <summary>
/// A lollipop tower's sizes — stem, orb, base, ramp and second base — saved so they can be put on
/// another tower from the dropdown in the Level Select Designer, on an outpost or a pool island.
/// Where the tower stands on its outpost is not part of it; that stays with each outpost.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Lollipop Tower Preset")]
public class LollipopTowerPreset : ScriptableObject
{
    public LollipopTower tower = new LollipopTower();

    /// <summary>Takes the tower's sizes, as a copy — nothing is shared with it.</summary>
    public void CopyFrom(LollipopTower source) => tower = (source ?? new LollipopTower()).Clone();

    /// <summary>The preset's sizes as a fresh tower — nothing is shared with it.</summary>
    public LollipopTower Build() => (tower ?? new LollipopTower()).Clone();
}
