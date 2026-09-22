using UnityEngine;

/// <summary>
/// A door shape — the keyhole and the soul opening at its foot — saved so it can be put on
/// another arena from the dropdown in the Level Select Designer. Which arena a door stands in,
/// and how far into the arch it sits, are not part of it; those stay with each arena.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Arena Door Preset")]
public class ArenaDoorPreset : ScriptableObject
{
    public ArenaDoorProfile door = new ArenaDoorProfile();

    /// <summary>Takes the shape from the arena, as a copy — nothing is shared with it.</summary>
    public void CopyFrom(ArenaDoorProfile profile)
    {
        door = profile != null ? profile.Clone() : new ArenaDoorProfile();
    }

    /// <summary>Puts the shape on the arena, as a copy — nothing is shared with it.</summary>
    public ArenaDoorProfile ToProfile()
    {
        return door != null ? door.Clone() : new ArenaDoorProfile();
    }
}
