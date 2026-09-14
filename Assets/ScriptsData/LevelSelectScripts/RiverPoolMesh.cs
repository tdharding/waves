using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on a generated pool and records everything needed to rebuild it: its two radii, the
/// river whose section it is revolved from, and where the rivers meeting it cut through its rim.
///
/// The same job <see cref="RiverRunMesh"/> does for a run — the Level Select Designer rebuilds
/// pools from this whenever a river shape changes, without a full regenerate.
/// </summary>
public class RiverPoolMesh : MonoBehaviour
{
    [Tooltip("River whose Run Shape this pool is revolved from.")]
    public string riverName;

    [Tooltip("Name of the generated mesh asset this pool writes to.")]
    public string meshAssetName;

    [Tooltip("Name of the generated water mesh asset, when this river is water filled.")]
    public string waterMeshAssetName;

    [Tooltip("Radius of the water — the channel edge. The rim goes on outside this.")]
    public float poolRadius = 3f;

    [Tooltip("Radius of the plinth in the middle. 0 leaves an open bowl.")]
    public float islandRadius = 1f;

    [Tooltip("How far the floor drops below the rim at its deepest. 0 takes the river's depth.")]
    public float floorDepth = 0f;

    [Tooltip("The rivers arriving, and where they cut through the rim — in this object's local space.")]
    public List<RiverRunMesh.Mouth> mouths = new List<RiverRunMesh.Mouth>();

    /// <summary>The ring a boat travels round this pool on — its deepest ring.</summary>
    public float ChannelRadius => RiverMeshBuilder.PoolChannelRadius(poolRadius, islandRadius);
}
