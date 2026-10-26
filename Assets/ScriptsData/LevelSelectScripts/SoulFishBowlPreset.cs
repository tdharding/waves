using UnityEngine;

/// <summary>
/// A soul fish bowl's sizes — base tiers, column and bowl — saved so they can be put on another
/// pool's tower from the dropdown in the Level Select Designer. The placement scale is not part
/// of it; that stays with each pool.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Soul Fish Bowl Preset")]
public class SoulFishBowlPreset : ScriptableObject
{
    public SoulFishBowl tower = new SoulFishBowl();

    /// <summary>Takes the tower's sizes, as a copy — nothing is shared with it.</summary>
    public void CopyFrom(SoulFishBowl source) => tower = (source ?? new SoulFishBowl()).Clone();

    /// <summary>The preset's sizes as a fresh tower — nothing is shared with it.</summary>
    public SoulFishBowl Build() => (tower ?? new SoulFishBowl()).Clone();
}
