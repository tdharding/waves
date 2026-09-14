using UnityEngine;

/// <summary>
/// Sits on a generated entrance archway and records what it was built from: the arena and
/// entrance it belongs to, and the shape it came out of.
///
/// The same job <see cref="LevelSelectArenaWallMesh"/> does for a wall — the Level Select
/// Designer rebuilds archways from this when a shape changes, without a full regenerate.
/// </summary>
public class LevelSelectArenaArchwayMesh : MonoBehaviour
{
    [Tooltip("Arena node this archway belongs to.")]
    public string nodeId;

    [Tooltip("Which of the arena's entrances this archway stands at.")]
    public int entranceIndex;

    [Tooltip("Name of the generated mesh asset this archway writes to.")]
    public string meshAssetName;

    [Tooltip("The shape this archway was built from, with every inherited number already settled.")]
    public ArenaArchwayProfile profile = new ArenaArchwayProfile();
}
