using UnityEngine;

/// <summary>
/// A wall's lip — a square band standing on the wall's top all the way along, centred on it, and
/// as thick across as it likes: thicker than the wall below overhangs it both sides. It goes round
/// every column as a ring of its own thickness. Anything standing on a wall node stands on the
/// lip's top.
/// </summary>
[System.Serializable]
public class WallLip
{
    [Tooltip("How far the lip stands above the wall's top.")]
    [Min(0f)] public float height = 0.03f;

    [Tooltip("The lip's thickness across, centred on the wall. Thicker than the wall overhangs it both sides.")]
    [Min(0.001f)] public float thickness = 0.12f;

    [Tooltip("How far the lip's ring round a column stands out from the column's side. 0 leaves columns without one.")]
    [Min(0f)] public float columnRing = 0.02f;

    public WallLip Clone() => (WallLip)MemberwiseClone();
}
