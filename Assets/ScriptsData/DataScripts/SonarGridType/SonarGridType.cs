using UnityEngine;

[CreateAssetMenu(menuName = "Sonar/Grid Type")]
public class SonarGridType : ScriptableObject
{
    [Header("Coverage")]
    public float viewRadius   = 10f;

    [Header("Grid")]
    public float cellSize     = 2f;
    public int   gridDensity  = 1;
    public int   subdivisions = 4;

    [Header("Horizontal Planes")]
    public float horizontalY      = 0f;
    public int   hLevels          = 4;
    public float hLevelSpacing    = 1.5f;

    [Header("Vertical Planes")]
    public bool spawnVertical      = true;
    public bool spawnCrossVertical = true;

    [Header("Material")]
    public Material planeMaterial;

    // ── derived ───────────────────────────────────────────────────────────────
    public int   GridDensity    => gridDensity;
    public float PlaneSize      => cellSize;
    public float GridWorldScale => gridDensity / Mathf.Max(0.001f, cellSize);
    public int   TilesPerAxis   => 2 * Mathf.Max(1, Mathf.CeilToInt(viewRadius / Mathf.Max(0.001f, cellSize))) + 1;
}
