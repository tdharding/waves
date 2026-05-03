using UnityEngine;

/// <summary>
/// Global tier floor configuration. Single source of truth for Y offsets.
/// Referenced by LevelSpawner, GridDesigner, and any editor that needs floor labels.
/// High to low: F2, F1, G, B-1, B-2, B-3 ...
/// </summary>
[CreateAssetMenu(fileName = "TierConfig", menuName = "Levels/Tier Config")]
public class TierConfig : ScriptableObject
{
    [Tooltip("Y offsets per floor slot, ordered high to low (e.g. F2=+4 down to B-3=-6).")]
    public float[] offsets = { 4f, 2f, 0f, -2f, -4f, -6f };
}
