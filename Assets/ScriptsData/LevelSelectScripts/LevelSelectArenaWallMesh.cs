using UnityEngine;

/// <summary>
/// Sits on a generated level select arena wall and records everything needed to rebuild it:
/// the shape it was built from, and the name of the mesh asset it writes to.
///
/// The same job <see cref="RiverPoolMesh"/> does for a pool — the Level Select Designer
/// rebuilds arena walls from this whenever a shape changes, without a full regenerate.
/// </summary>
public class LevelSelectArenaWallMesh : MonoBehaviour
{
    [Tooltip("Arena node this wall was built for.")]
    public string nodeId;

    [Tooltip("Name of the generated mesh asset this wall writes to.")]
    public string meshAssetName;

    [Tooltip("The shape this wall was built from.")]
    public ArenaWallProfile profile = new ArenaWallProfile();
}
