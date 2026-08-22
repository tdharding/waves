using UnityEngine;

// A reusable recipe for the shape of a spike rock. Holds only the SHAPE — no position, no
// level — so one preset can stand a whole field of rocks up, and editing it restyles all of
// them at once. Authored in the Spike Studio window, saved as an asset into a Resources
// folder, then picked per-spike in the Grid Designer.
//
// Sizes are WORLD UNITS (metres), not fractions of the arena: you design these looking at a
// real rock in a scene, so the numbers should be the ones you see. A placement can scale the
// whole thing up or down from there.
//
// Same shape as SteppedBuildingConfig — a plain serializable config plus a thin ScriptableObject
// wrapper — so the studio, the live-rebuild watcher and the spawner all work the same way.
[System.Serializable]
public class SpikeShapeConfig
{
    [Header("Widths (radius in metres)")]
    [Tooltip("Radius at the very bottom, deep below the surface. Usually the widest — it's the rock's footing.")]
    public float radiusBelowSurface = 0.6f;

    [Tooltip("Radius where the rock meets the water. This is the ring the boat sees.")]
    public float radiusWaterline = 0.5f;

    [Tooltip("Radius partway up. Wider than its neighbours bulges the rock into a belly; narrower pinches it into a waist.")]
    public float radiusMid = 0.3f;

    [Tooltip("Radius at the very top. Near zero comes to a point; larger gives a flat perch.")]
    public float radiusTop = 0.05f;

    [Header("Heights (metres from the waterline)")]
    [Tooltip("How far the tip stands above the waterline.")]
    public float heightAboveWater = 3f;

    [Tooltip("How far the base drops below the waterline so the rock appears bottomless.")]
    public float depthBelowWater = 5f;

    [Tooltip("Where the mid radius sits between the waterline (0) and the tip (1).")]
    [Range(0.05f, 0.95f)] public float midHeightFraction = 0.5f;

    [Header("Top")]
    [Tooltip("Curved cap — blends the top width in so the rock doesn't end on a flat plateau. " +
             "0 = a flat top the width of Top (a perch); 1 = that width fully capped with a curve. " +
             "The wider the top, the more cap there is — a rock that already comes to a point has " +
             "no plateau to blend.")]
    [Range(0f, 1f)] public float topRoundness = 0f;

    [Header("Spiral")]
    [Tooltip("Turns the rock's surface twists through, base to tip. This rotates each ring a little " +
             "further than the one below, so the mesh's own vertical edges wind up the rock as " +
             "helices — which is what the carved ridges then follow. 0 = a plain untwisted lathe.")]
    public float twistTurns = 1.5f;

    [Tooltip("Cut ridges into the generated mesh along its twisted edges, rather than only shading " +
             "them. Because the groove IS an edge loop it stays razor sharp at any density, and the " +
             "generator works out the normals, so the rock's own lighting picks it out unaided.")]
    public bool carveSpiralRidge = false;

    [Tooltip("How far apart the spiral lines sit going up the rock, in metres at this preset's own " +
             "size. Bigger = fewer, wider-spaced wraps. The generator works out how many edges to " +
             "cut from this and the twist, then snaps it to a count that divides evenly into Faces " +
             "around so every groove lands squarely on an edge.")]
    public float ridgeSpacing = 0.35f;

    [Tooltip("How deep each groove cuts into the rock, in metres at this preset's own size.")]
    public float ridgeDepth = 0.02f;

    [Tooltip("How far the cut bleeds into the edges either side. 0 pinches a sharp crease on the " +
             "edge itself; 1 spreads it right across to the next ridge, which reads as a rounded " +
             "flute rather than a cut.")]
    [Range(0f, 1f)] public float ridgeSoftness = 0.5f;

    [Header("Mesh")]
    [Tooltip("Faces around the rock. Low reads as chiselled; high reads as a smooth column.")]
    [Range(3, 64)] public int sidesAround = 16;

    [Tooltip("Rings generated between each pair of widths. Higher rounds the curve out; 1 gives straight tapers.")]
    [Range(1, 16)] public int heightSubdivisions = 6;

    public SpikeShapeConfig Copy() => new SpikeShapeConfig
    {
        radiusBelowSurface = radiusBelowSurface,
        radiusWaterline    = radiusWaterline,
        radiusMid          = radiusMid,
        radiusTop          = radiusTop,
        heightAboveWater   = heightAboveWater,
        depthBelowWater    = depthBelowWater,
        midHeightFraction  = midHeightFraction,
        topRoundness       = topRoundness,
        carveSpiralRidge   = carveSpiralRidge,
        ridgeDepth         = ridgeDepth,
        twistTurns         = twistTurns,
        ridgeSpacing       = ridgeSpacing,
        ridgeSoftness      = ridgeSoftness,
        sidesAround        = sidesAround,
        heightSubdivisions = heightSubdivisions,
    };

    // Field-by-field compare so a live watcher can tell when the preset was edited.
    public bool ValueEquals(SpikeShapeConfig o) =>
        o != null &&
        radiusBelowSurface == o.radiusBelowSurface && radiusWaterline == o.radiusWaterline &&
        radiusMid == o.radiusMid && radiusTop == o.radiusTop &&
        heightAboveWater == o.heightAboveWater && depthBelowWater == o.depthBelowWater &&
        midHeightFraction == o.midHeightFraction && topRoundness == o.topRoundness &&
        carveSpiralRidge == o.carveSpiralRidge && ridgeDepth == o.ridgeDepth &&
        twistTurns == o.twistTurns && ridgeSpacing == o.ridgeSpacing &&
        ridgeSoftness == o.ridgeSoftness &&
        sidesAround == o.sidesAround && heightSubdivisions == o.heightSubdivisions;

    /// <summary>Widest radius anywhere on the rock — what a map or designer sizes its drawing to.</summary>
    public float WidestRadius =>
        Mathf.Max(Mathf.Max(radiusBelowSurface, radiusWaterline), Mathf.Max(radiusMid, radiusTop));
}

// Folder under Resources that the Spike Studio saves into and the Grid Designer lists from.
// Matches Resources/Buildings for stepped rooftops and Resources/Levels for levels.
[CreateAssetMenu(fileName = "SpikeShapePreset", menuName = "Waves/Spike Shape Preset")]
public class SpikeShapePreset : ScriptableObject
{
    public const string ResourcesFolder = "Spikes";
    public const string AssetFolder     = "Assets/Resources/" + ResourcesFolder;

    public SpikeShapeConfig config = new SpikeShapeConfig();
}
