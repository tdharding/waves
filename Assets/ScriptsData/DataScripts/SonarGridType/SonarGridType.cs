using UnityEngine;

[CreateAssetMenu(menuName = "Sonar/Grid Type")]
public class SonarGridType : ScriptableObject
{
    [Header("Grid Formation")]
    [Tooltip("Tile columns across X")]
    public int columns = 12;
    [Tooltip("Tile rows across Z")]
    public int rows    = 12;
    [Tooltip("Horizontal plane levels stacked on Y")]
    public int levels  = 4;

    [Header("Appearance")]
    [Tooltip("Visual grid lines per tile. 1 = one line per tile edge, aligned with geometry.")]
    public int linesPerCell = 1;
    [Tooltip("Vertex subdivisions per tile mesh")]
    public int subdivisions = 4;

    [Header("Depth Placement")]
    [Tooltip("Offset from wave surface Y to the TOP of the level stack. Negative = below surface.")]
    public float surfaceDepthOffset     = -0.5f;
    [Header("Vertical Planes")]
    public bool spawnVertical      = true;
    public bool spawnCrossVertical = true;

    [Header("Material")]
    public Material planeMaterial;

    // ── derived — cellSize supplied by SonarController at runtime ─────────────
    public int   GridDensity                    => linesPerCell;
    public float GridWorldScale(float cellSize) => linesPerCell / Mathf.Max(0.001f, cellSize);
}
