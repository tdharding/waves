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

    // ── public API (delegates to gridType) ────────────────────────────────────
    public int   GridDensity    => gridType != null ? gridType.GridDensity    : 1;
    public float PlaneSize      => gridType != null ? gridType.PlaneSize      : 2f;
    public float GridWorldScale => gridType != null ? gridType.GridWorldScale : 0.5f;
    public int   TilesPerAxis   => gridType != null ? gridType.TilesPerAxis   : 5;

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
    Vector3Int _currentCell = new Vector3Int(int.MinValue, 0, int.MinValue);
    Mesh _sharedMesh;

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
        _sharedMesh = BuildMesh(gridType.subdivisions, gridType.cellSize);
        int t        = TilesPerAxis;
        int poolSize = t * t * gridType.hLevels;
        SpawnPool("Horizontal",    poolSize, Quaternion.identity,           _hTiles);
        if (gridType.spawnVertical)
            SpawnPool("Vertical",      poolSize, Quaternion.Euler(90f, 0f, 0f), _vTiles);
        if (gridType.spawnCrossVertical)
            SpawnPool("CrossVertical", poolSize, Quaternion.Euler(0f, 0f, 90f), _xTiles);
        _currentCell = new Vector3Int(int.MinValue, 0, int.MinValue);
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

    // ── snapping ──────────────────────────────────────────────────────────────

    public void SnapTiles(Vector3 boatWorldPos)
    {
        if (gridType == null) return;

        Vector3Int cell = new Vector3Int(
            Mathf.RoundToInt(boatWorldPos.x / gridType.cellSize),
            0,
            Mathf.RoundToInt(boatWorldPos.z / gridType.cellSize)
        );
        if (cell == _currentCell) return;
        _currentCell = cell;

        int   n       = TilesPerAxis;
        int   half    = n / 2;
        float halfLev = (gridType.hLevels - 1) * gridType.hLevelSpacing * 0.5f;

        int hIdx = 0, vIdx = 0, xIdx = 0;
        for (int lev = 0; lev < gridType.hLevels; lev++)
        {
            float y = gridType.horizontalY - halfLev + lev * gridType.hLevelSpacing;
            for (int x = 0; x < n; x++)
            {
                for (int z = 0; z < n; z++, hIdx++, vIdx++, xIdx++)
                {
                    var pos = new Vector3(
                        (cell.x + x - half) * gridType.cellSize, y,
                        (cell.z + z - half) * gridType.cellSize);
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
        int rows   = subs + 1;
        var verts  = new Vector3[rows * rows];
        var uvs    = new Vector2[rows * rows];
        var tris   = new int[subs * subs * 6];
        float cell = size / subs;
        float half = size * 0.5f;

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
        if (gridType == null || _currentCell.x == int.MinValue) return;

        int   n       = TilesPerAxis;
        int   half    = n / 2;
        float halfLev = (gridType.hLevels - 1) * gridType.hLevelSpacing * 0.5f;

        for (int x = 0; x < n; x++)
        {
            for (int z = 0; z < n; z++)
            {
                int cx = _currentCell.x + x - half;
                int cz = _currentCell.z + z - half;

                Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
                for (int lev = 0; lev < gridType.hLevels; lev++)
                {
                    float y = gridType.horizontalY - halfLev + lev * gridType.hLevelSpacing;
                    Gizmos.DrawWireCube(
                        new Vector3(cx * gridType.cellSize, y, cz * gridType.cellSize),
                        new Vector3(gridType.cellSize, 0f, gridType.cellSize));
                }

                for (int lev = 0; lev < gridType.hLevels; lev++)
                {
                    float y   = gridType.horizontalY - halfLev + lev * gridType.hLevelSpacing;
                    var   pos = new Vector3(cx * gridType.cellSize, y, cz * gridType.cellSize);
                    if (gridType.spawnVertical)
                    {
                        Gizmos.color = new Color(0f, 0.8f, 1f, 0.1f);
                        Gizmos.DrawWireCube(pos, new Vector3(gridType.cellSize, gridType.cellSize, 0f));
                    }
                    if (gridType.spawnCrossVertical)
                    {
                        Gizmos.color = new Color(0f, 0.6f, 1f, 0.08f);
                        Gizmos.DrawWireCube(pos, new Vector3(0f, gridType.cellSize, gridType.cellSize));
                    }
                }
            }
        }
    }
#endif
}
