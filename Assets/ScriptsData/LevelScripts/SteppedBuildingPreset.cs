using UnityEngine;

// A reusable, dimension-agnostic recipe for a stepped rooftop. Holds only the SHAPE
// numbers — no mesh, no building size — so one preset drives buildings of any footprint.
// Authored in the Stepped Building Studio window, saved as an asset, then dropped into
// the LevelSpawner's pool and applied at spawn.
[System.Serializable]
public class SteppedBuildingConfig
{
    [Tooltip("World size of one rooftop grid cell. Smaller = finer steps and thinner rim unit.")]
    public float cellSize = 1f;

    [Tooltip("How many cells thick the stepped rim wall is (its slab depth).")]
    public int rimCells = 1;

    [Tooltip("Number of plateaus around the perimeter (more = busier skyline).")]
    public int stepCount = 12;

    [Tooltip("How many discrete height tiers the steps snap to (the set 'storey' heights).")]
    public int levelCount = 4;

    [Tooltip("World-height gap between adjacent tiers.")]
    public float levelSpacing = 1.5f;

    [Range(0f, 1f)]
    [Tooltip("Chance each plateau changes to a different tier (0 = flat, 1 = restless).")]
    public float variation = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("Tendency to keep heading the same way, so it runs down-down-down then up-up " +
             "rather than zig-zagging.")]
    public float persistence = 0.6f;

    [Range(0f, 1f)]
    [Tooltip("How far the centre roof sinks below the top (0.25 = a quarter drop).")]
    public float dropFraction = 0.25f;

    public SteppedBuildingConfig Copy() => new SteppedBuildingConfig
    {
        cellSize     = cellSize,
        rimCells     = rimCells,
        stepCount    = stepCount,
        levelCount   = levelCount,
        levelSpacing = levelSpacing,
        variation    = variation,
        persistence  = persistence,
        dropFraction = dropFraction,
    };

    // Field-by-field compare so a live watcher can tell when the preset was edited.
    public bool ValueEquals(SteppedBuildingConfig o) =>
        o != null &&
        cellSize == o.cellSize && rimCells == o.rimCells && stepCount == o.stepCount &&
        levelCount == o.levelCount && levelSpacing == o.levelSpacing &&
        variation == o.variation && persistence == o.persistence && dropFraction == o.dropFraction;
}

[CreateAssetMenu(fileName = "SteppedBuildingPreset", menuName = "Waves/Stepped Building Preset")]
public class SteppedBuildingPreset : ScriptableObject
{
    public SteppedBuildingConfig config = new SteppedBuildingConfig();
}
