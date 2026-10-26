using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on a generated pool and records everything needed to rebuild it: its two radii, the
/// river whose section it is revolved from, and where the rivers meeting it cut through its rim.
///
/// The same job <see cref="RiverRunMesh"/> does for a run — the Level Select Designer rebuilds
/// pools from this whenever a river shape changes, without a full regenerate.
/// </summary>
[ExecuteAlways]
public class RiverPoolMesh : MonoBehaviour
{
    [Tooltip("River whose Run Shape this pool is revolved from.")]
    public string riverName;

    [Tooltip("Name of the generated mesh asset this pool writes to.")]
    public string meshAssetName;

    [Tooltip("Name of the generated water mesh asset, when this river is water filled.")]
    public string waterMeshAssetName;

    [Tooltip("Whether this pool's water wears its own Water Colour rather than the Level Select " +
             "River Tuner's. Stamped by the designer from the Water Colour Override of the path " +
             "the pool takes its shape from.")]
    public bool waterColourOverride;

    [Tooltip("This pool's water colour, while Water Colour Override is on.")]
    public Color waterColour = new Color(0.836478f, 0.836478f, 0.836478f, 1f);

    [Tooltip("Radius of the water — the channel edge. The rim goes on outside this.")]
    public float poolRadius = 3f;

    [Tooltip("Radius of the plinth in the middle. 0 leaves an open bowl.")]
    public float islandRadius = 1f;

    [Tooltip("How far the floor drops below the rim at its deepest. 0 takes the river's depth.")]
    public float floorDepth = 0f;

    [Tooltip("Whether this pool was built on a Landscape Natural River Run. Stamped by the designer " +
             "on generate and on every rebuild; read by the pool's fog circle, so the FogMap's " +
             "Natural Runs Repel leaves natural pools out along with natural runs.")]
    public bool naturalRun;

    [Tooltip("The rivers arriving, and where they cut through the rim — in this object's local space.")]
    public List<RiverRunMesh.Mouth> mouths = new List<RiverRunMesh.Mouth>();

    /// <summary>The ring a boat travels round this pool on — its deepest ring.</summary>
    public float ChannelRadius => RiverMeshBuilder.PoolChannelRadius(poolRadius, islandRadius);

    // A property block is not saved with the scene, so it goes back on every time the pool loads.
    private void OnEnable() => ApplyWaterColour();

    // Undo puts the stamped colour back without the designer hearing of it.
    private void OnValidate() => ApplyWaterColour();

    /// <summary>Puts this pool's Water Colour on its water, or takes it off again.</summary>
    public void ApplyWaterColour()
        => RiverRunMesh.ApplyWaterColour(transform, waterMeshAssetName, waterColourOverride, waterColour);
}
