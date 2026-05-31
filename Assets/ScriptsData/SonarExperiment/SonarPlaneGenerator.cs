using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SonarPlaneGenerator : MonoBehaviour
{
    [SerializeField] SonarGridType gridType;

    public SonarGridType GridType => gridType;

    // ── runtime values set by SonarController ─────────────────────────────────
    float _cellSize      = 2f;
    float _levelSpacing  = 1f;
    float _waveSurfaceY  = 0f;

    // ── public API ────────────────────────────────────────────────────────────
    public int   Columns      => gridType != null ? gridType.columns    : 1;
    public int   Rows         => gridType != null ? gridType.rows       : 1;
    public int   Levels       => gridType != null ? gridType.levels     : 1;
    public int   GridDensity  => gridType != null ? gridType.GridDensity : 1;
    public float GridWorldScale => gridType != null ? gridType.GridWorldScale(_cellSize) : 0.5f;
    public float CellSize     => _cellSize;

    float LevelSpacing => _levelSpacing;
    float StackTopY    => _waveSurfaceY + (gridType != null ? gridType.surfaceDepthOffset : -0.5f);

    public static readonly int PulseOriginsID         = Shader.PropertyToID("_PulseOrigins");
    public static readonly int PulseOriginCountID     = Shader.PropertyToID("_PulseOriginCount");
    public static readonly int PulseRadiusID          = Shader.PropertyToID("_PulseRadius");
    public static readonly int PulseWidthID           = Shader.PropertyToID("_PulseWidth");
    public static readonly int DisplaceStrengthID     = Shader.PropertyToID("_DisplaceStrength");
    public static readonly int DisplaceRadiusOffsetID = Shader.PropertyToID("_DisplaceRadiusOffset");
    public static readonly int GridDensityShaderID    = Shader.PropertyToID("_GridDensity");
    public static readonly int GridWorldScaleID       = Shader.PropertyToID("_GridWorldScale");

    readonly List<Transform> _hTiles = new List<Transform>();
    readonly List<Transform> _vTiles = new List<Transform>();
    readonly List<Transform> _xTiles = new List<Transform>();
    Mesh _sharedMesh;

    // ── configure (called by SonarController) ─────────────────────────────────

    public void Configure(float cellSize, float levelSpacing, float waveSurfaceY)
    {
        _cellSize     = Mathf.Max(0.01f, cellSize);
        _levelSpacing = Mathf.Max(0.01f, levelSpacing);
        _waveSurfaceY = waveSurfaceY;
        Generate();
    }

    // Cheap per-frame update — repositions tiles without rebuilding meshes.
    public void UpdateSurfaceY(float waveSurfaceY)
    {
        if (Mathf.Approximately(_waveSurfaceY, waveSurfaceY)) return;
        _waveSurfaceY = waveSurfaceY;
        PlaceTiles();
    }

    // ── lifecycle ─────────────────────────────────────────────────────────────

    void OnEnable()
    {
        if (_hTiles.Count == 0 && gridType != null)
            Generate();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EditorApplication.delayCall += () =>
        {
            if (this != null && gameObject != null && gridType != null)
                Generate();
        };
    }
#endif

    [ContextMenu("Generate Tiles")]
    public void Generate()
    {
        if (gridType == null) return;
        Clear();
        _sharedMesh = BuildMesh(gridType.subdivisions, _cellSize);

        int poolSize = Columns * Rows * Levels;
        SpawnPool("Horizontal",    poolSize, Quaternion.identity,           _hTiles);
        if (gridType.spawnVertical)
            SpawnPool("Vertical",      poolSize, Quaternion.Euler(90f, 0f, 0f), _vTiles);
        if (gridType.spawnCrossVertical)
            SpawnPool("CrossVertical", poolSize, Quaternion.Euler(0f, 0f, 90f), _xTiles);
        PlaceTiles();
    }

    [ContextMenu("Clear Tiles")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        _hTiles.Clear();
        _vTiles.Clear();
        _xTiles.Clear();
    }

    void SpawnPool(string prefix, int count, Quaternion rot, List<Transform> pool)
    {
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"{prefix}_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = _sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (gridType.planeMaterial != null) mr.sharedMaterial = gridType.planeMaterial;
            pool.Add(go.transform);
        }
    }

    // ── static placement ──────────────────────────────────────────────────────

    void PlaceTiles()
    {
        if (gridType == null) return;

        int   cols     = Columns;
        int   rowCount = Rows;
        float cs       = _cellSize;
        float halfX    = (cols     - 1) * cs * 0.5f;
        float halfZ    = (rowCount - 1) * cs * 0.5f;
        float topY     = StackTopY;
        Vector3 org    = transform.position;

        int hIdx = 0, vIdx = 0, xIdx = 0;
        for (int lev = 0; lev < Levels; lev++)
        {
            float y = topY - lev * LevelSpacing;
            for (int col = 0; col < cols; col++)
            {
                for (int row = 0; row < rowCount; row++, hIdx++, vIdx++, xIdx++)
                {
                    var pos = new Vector3(
                        org.x - halfX + col * cs,
                        y,
                        org.z - halfZ + row * cs);
                    if (hIdx < _hTiles.Count) _hTiles[hIdx].position = pos;
                    if (vIdx < _vTiles.Count) _vTiles[vIdx].position = pos;
                    if (xIdx < _xTiles.Count) _xTiles[xIdx].position = pos;
                }
            }
        }
    }

    // ── procedural mesh ───────────────────────────────────────────────────────

    static Mesh BuildMesh(int subs, float size)
    {
        int   rows  = subs + 1;
        var   verts = new Vector3[rows * rows];
        var   uvs   = new Vector2[rows * rows];
        var   tris  = new int[subs * subs * 6];
        float cell  = size / subs;
        float half  = size * 0.5f;

        for (int j = 0; j < rows; j++)
            for (int i = 0; i < rows; i++)
            {
                int idx    = j * rows + i;
                verts[idx] = new Vector3(i * cell - half, 0f, j * cell - half);
                uvs[idx]   = new Vector2((float)i / subs, (float)j / subs);
            }

        int t = 0;
        for (int j = 0; j < subs; j++)
            for (int i = 0; i < subs; i++)
            {
                int tl = j * rows + i, tr = tl + 1;
                int bl = (j + 1) * rows + i, br = bl + 1;
                tris[t++] = tl; tris[t++] = bl; tris[t++] = tr;
                tris[t++] = tr; tris[t++] = bl; tris[t++] = br;
            }

        var mesh = new Mesh { name = "SonarTile" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (gridType == null) return;

        int   cols     = Columns;
        int   rowCount = Rows;
        float cs       = _cellSize;
        float halfX    = (cols     - 1) * cs * 0.5f;
        float halfZ    = (rowCount - 1) * cs * 0.5f;
        float topY     = StackTopY;
        Vector3 org    = transform.position;

        for (int col = 0; col < cols; col++)
        {
            for (int row = 0; row < rowCount; row++)
            {
                for (int lev = 0; lev < Levels; lev++)
                {
                    float   y   = topY - lev * LevelSpacing;
                    Vector3 pos = new Vector3(org.x - halfX + col * cs, y, org.z - halfZ + row * cs);

                    Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
                    Gizmos.DrawWireCube(pos, new Vector3(cs, 0f, cs));

                    if (gridType.spawnVertical)
                    {
                        Gizmos.color = new Color(0f, 0.8f, 1f, 0.1f);
                        Gizmos.DrawWireCube(pos, new Vector3(cs, cs, 0f));
                    }
                    if (gridType.spawnCrossVertical)
                    {
                        Gizmos.color = new Color(0f, 0.6f, 1f, 0.08f);
                        Gizmos.DrawWireCube(pos, new Vector3(0f, cs, cs));
                    }
                }
            }
        }
    }
#endif
}
