using UnityEngine;

/// <summary>
/// A soul juice tank's sizes — base tiers, column and bowl — saved so they can be put on another
/// pool's tower from the dropdown in the Level Select Designer. The placement scale is not part
/// of it; that stays with each pool.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Soul Juice Tank Preset")]
public class SoulJuiceTankPreset : ScriptableObject
{
    public SoulJuiceTank tower = new SoulJuiceTank();

    /// <summary>Takes the tower's sizes, as a copy — nothing is shared with it.</summary>
    public void CopyFrom(SoulJuiceTank source) => tower = (source ?? new SoulJuiceTank()).Clone();

    /// <summary>The preset's sizes as a fresh tower — nothing is shared with it.</summary>
    public SoulJuiceTank Build() => (tower ?? new SoulJuiceTank()).Clone();
}
