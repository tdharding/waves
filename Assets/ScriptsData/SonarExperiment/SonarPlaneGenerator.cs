using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SonarPlaneGenerator : MonoBehaviour
{
    // Safety cap — a high `subdivisions` on the grid type can otherwise build a merged mesh
    // large enough to hang the editor. Set well above what the existing grid types need
    // (10x10x4 at 100 subdivisions peaks around 3.3M) so it only catches runaway values.
    const long MaxVertsPerMesh = 4_000_000;

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

    // Lattice footprint, centred on this transform. Matches the arena width fed in by SonarController.
    public float Width => Columns * _cellSize;
    public float Depth => Rows    * _cellSize;

    float LevelSpacing => _levelSpacing;
    float StackTopY    => _waveSurfaceY + (gridType != null ? gridType.surfaceDepthOffset : -0.5f);

    // Upright walls run from the top horizontal level down to the bottom one so the lattice closes
    // into a box. A single-level grid has no gap to span, so it falls back to one level's spacing.
    public float StackHeight => Levels > 1 ? (Levels - 1) * _levelSpacing : _levelSpacing;

    public static readonly int PulseOriginsID         = Shader.PropertyToID("_PulseOrigins");
    public static readonly int PulseOriginCountID     = Shader.PropertyToID("_PulseOriginCount");
    public static readonly int PulseRadiusID          = Shader.PropertyToID("_PulseRadius");
    public static readonly int PulseWidthID           = Shader.PropertyToID("_PulseWidth");
    public static readonly int DisplaceStrengthID     = Shader.PropertyToID("_DisplaceStrength");
    public static readonly int DisplaceRadiusOffsetID = Shader.PropertyToID("_DisplaceRadiusOffset");
    public static readonly int GridDensityShaderID    = Shader.PropertyToID("_GridDensity");
    public static readonly int GridWorldScaleID       = Shader.PropertyToID("_GridWorldScale");

    readonly List<Transform> _hLevels = new List<Transform>();
    Transform _vWalls;   // walls spanning X, normal Z — one per Z grid line
    Transform _xWalls;   // walls spanning Z, normal X — one per X grid line
    Mesh _hMesh, _vMesh, _xMesh;

    // ── configure (called by SonarController) ─────────────────────────────────

    // Swaps the grid formation at runtime (e.g. LevelDataController applying the active
    // level's SonarGridType on spawn). Rebuilds with the current cell/level spacing.
    public void SetGridType(SonarGridType type)
    {
        if (type == gridType) return;
        gridType = type;
        Generate();
    }

    public void Configure(float cellSize, float levelSpacing, float waveSurfaceY)
    {
        _cellSize     = Mathf.Max(0.01f, cellSize);
        _levelSpacing = Mathf.Max(0.01f, levelSpacing);
        _waveSurfaceY = waveSurfaceY;
        Generate();
    }

    // Cheap per-frame update — repositions the meshes without rebuilding them.
    public void UpdateSurfaceY(float waveSurfaceY)
    {
        if (Mathf.Approximately(_waveSurfaceY, waveSurfaceY)) return;
        _waveSurfaceY = waveSurfaceY;
        PlaceMeshes();
    }

    // ── lifecycle ─────────────────────────────────────────────────────────────

    void OnEnable()
    {
        if (_hLevels.Count == 0 && gridType != null)
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

        int subs      = ResolveSubdivisions();
        int depthSubs = Mathf.Max(1, (Levels - 1) * subs);

        _hMesh = BuildHorizontalMesh(Columns, Rows, subs, _cellSize);
        for (int lev = 0; lev < Levels; lev++)
            _hLevels.Add(SpawnMeshObject($"Horizontal_{lev}", _hMesh));

        if (gridType.spawnVertical)
        {
            _vMesh  = BuildWallMesh(true, Columns, Rows, subs, depthSubs, _cellSize, StackHeight, "SonarWallsZ");
            _vWalls = SpawnMeshObject("Vertical", _vMesh);
        }

        if (gridType.spawnCrossVertical)
        {
            _xMesh  = BuildWallMesh(false, Columns, Rows, subs, depthSubs, _cellSize, StackHeight, "SonarWallsX");
            _xWalls = SpawnMeshObject("CrossVertical", _xMesh);
        }

        PlaceMeshes();
    }

    [ContextMenu("Clear Tiles")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        _hLevels.Clear();
        _vWalls = null;
        _xWalls = null;

        ReleaseMesh(ref _hMesh);
        ReleaseMesh(ref _vMesh);
        ReleaseMesh(ref _xMesh);
    }

    Transform SpawnMeshObject(string objectName, Mesh mesh)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        if (gridType.planeMaterial != null) mr.sharedMaterial = gridType.planeMaterial;
        return go.transform;
    }

    static void ReleaseMesh(ref Mesh mesh)
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh);
        else                       DestroyImmediate(mesh);
        mesh = null;
    }

    // Subdivisions only control vertex density for the shader's displacement — grid line density
    // comes from SonarGridType.linesPerCell. Merged meshes make a high value very expensive, so
    // clamp rather than let it build something unusable.
    int ResolveSubdivisions()
    {
        int requested = Mathf.Max(1, gridType.subdivisions);
        int subs      = requested;

        while (subs > 1 && PeakVertCount(subs) > MaxVertsPerMesh)
            subs--;

        if (subs != requested)
            Debug.LogWarning(
                $"[SonarPlaneGenerator] '{gridType.name}' subdivisions {requested} would build " +
                $"{PeakVertCount(requested):N0} verts in a single mesh — clamped to {subs}. " +
                "Line density comes from Lines Per Cell, not Subdivisions.", this);

        return subs;
    }

    long PeakVertCount(int subs)
    {
        long depthVerts = Mathf.Max(1, (Levels - 1) * subs) + 1;
        long horizontal = (long)(Columns * subs + 1) * (Rows * subs + 1);
        long wallsZ     = (long)(Rows    + 1) * (Columns * subs + 1) * depthVerts;
        long wallsX     = (long)(Columns + 1) * (Rows    * subs + 1) * depthVerts;
        return System.Math.Max(horizontal, System.Math.Max(wallsZ, wallsX));
    }

    // ── placement ─────────────────────────────────────────────────────────────

    void PlaceMeshes()
    {
        if (gridType == null) return;

        float   topY = StackTopY;
        Vector3 org  = transform.position;

        // The generator transform can carry a scene scale (SonarGenerator sits at 3,3,3). The
        // lattice is authored in world units off the arena radius, so cancel it out — otherwise
        // the whole footprint is multiplied and stops matching the arena.
        Vector3 inv = InverseLossyScale();

        for (int lev = 0; lev < _hLevels.Count; lev++)
        {
            _hLevels[lev].position   = new Vector3(org.x, topY - lev * LevelSpacing, org.z);
            _hLevels[lev].localScale = inv;
        }

        // Wall meshes are authored with y = 0 at the stack top, running downwards.
        var wallPos = new Vector3(org.x, topY, org.z);
        if (_vWalls != null) { _vWalls.position = wallPos; _vWalls.localScale = inv; }
        if (_xWalls != null) { _xWalls.position = wallPos; _xWalls.localScale = inv; }
    }

    // Local scale that lands the children at world scale 1, whatever the generator is scaled to.
    Vector3 InverseLossyScale()
    {
        Vector3 s = transform.lossyScale;
        return new Vector3(
            Mathf.Approximately(s.x, 0f) ? 1f : 1f / s.x,
            Mathf.Approximately(s.y, 0f) ? 1f : 1f / s.y,
            Mathf.Approximately(s.z, 0f) ? 1f : 1f / s.z);
    }

    // ── procedural mesh ───────────────────────────────────────────────────────

    static Mesh BuildHorizontalMesh(int cols, int rows, int subs, float cellSize)
    {
        int vertsX = cols * subs + 1;
        int vertsZ = rows * subs + 1;
        var verts  = new Vector3[vertsX * vertsZ];
        var uvs    = new Vector2[vertsX * vertsZ];

        float halfW = cols * cellSize * 0.5f;
        float halfD = rows * cellSize * 0.5f;
        float stepX = cellSize / subs;
        float stepZ = cellSize / subs;

        for (int j = 0; j < vertsZ; j++)
            for (int i = 0; i < vertsX; i++)
            {
                int idx    = j * vertsX + i;
                verts[idx] = new Vector3(i * stepX - halfW, 0f, j * stepZ - halfD);
                uvs[idx]   = new Vector2((float)i / subs, (float)j / subs);
            }

        int quadW = vertsX - 1, quadZ = vertsZ - 1;
        var tris  = new int[quadW * quadZ * 6];
        int t     = 0;
        for (int j = 0; j < quadZ; j++)
            for (int i = 0; i < quadW; i++)
            {
                int tl = j * vertsX + i, tr = tl + 1;
                int bl = (j + 1) * vertsX + i, br = bl + 1;
                tris[t++] = tl; tris[t++] = bl; tris[t++] = tr;
                tris[t++] = tr; tris[t++] = bl; tris[t++] = br;
            }

        var mesh = new Mesh { name = "SonarHorizontalLevel" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices    = verts;
        mesh.uv          = uvs;
        mesh.triangles   = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Every upright wall for one axis, merged into a single mesh. Walls sit ON the grid lines
    // (cols + 1 of them) so they meet the horizontal levels at the same coordinates.
    // spanX true  → walls span X, flat in Z, one per Z grid line.
    // spanX false → walls span Z, flat in X, one per X grid line.
    static Mesh BuildWallMesh(bool spanX, int cols, int rows, int subs, int depthSubs,
                              float cellSize, float height, string meshName)
    {
        float halfW = cols * cellSize * 0.5f;
        float halfD = rows * cellSize * 0.5f;

        int   spanCells = spanX ? cols  : rows;
        float spanHalf  = spanX ? halfW : halfD;
        int   wallCount = (spanX ? rows : cols) + 1;
        float wallStart = spanX ? -halfD : -halfW;

        int spanVerts  = spanCells * subs + 1;
        int depthVerts = depthSubs + 1;
        int perWall    = spanVerts * depthVerts;

        var verts = new Vector3[perWall * wallCount];
        var uvs   = new Vector2[perWall * wallCount];
        var tris  = new int[(spanVerts - 1) * (depthVerts - 1) * 6 * wallCount];

        float spanStep  = cellSize / subs;
        float depthStep = height / depthSubs;

        int t = 0;
        for (int w = 0; w < wallCount; w++)
        {
            float wallOffset = wallStart + w * cellSize;
            int   baseIdx    = w * perWall;

            for (int d = 0; d < depthVerts; d++)
            {
                float y = -d * depthStep;   // 0 at the stack top, running down
                for (int s = 0; s < spanVerts; s++)
                {
                    float along = s * spanStep - spanHalf;
                    int   idx   = baseIdx + d * spanVerts + s;

                    verts[idx] = spanX
                        ? new Vector3(along, y, wallOffset)
                        : new Vector3(wallOffset, y, along);

                    // UVs in cell units, matching the horizontal mesh
                    uvs[idx] = new Vector2((float)s / subs, (d * depthStep) / cellSize);
                }
            }

            for (int d = 0; d < depthVerts - 1; d++)
                for (int s = 0; s < spanVerts - 1; s++)
                {
                    int tl = baseIdx + d * spanVerts + s, tr = tl + 1;
                    int bl = tl + spanVerts,              br = bl + 1;
                    tris[t++] = tl; tris[t++] = bl; tris[t++] = tr;
                    tris[t++] = tr; tris[t++] = bl; tris[t++] = br;
                }
        }

        var mesh = new Mesh { name = meshName };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices    = verts;
        mesh.uv          = uvs;
        mesh.triangles   = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (gridType == null) return;

        float   w     = Width;
        float   d     = Depth;
        float   halfW = w * 0.5f;
        float   halfD = d * 0.5f;
        float   topY  = StackTopY;
        float   midY  = topY - StackHeight * 0.5f;
        Vector3 org   = transform.position;

        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        for (int lev = 0; lev < Levels; lev++)
        {
            float y = topY - lev * LevelSpacing;
            Gizmos.DrawWireCube(new Vector3(org.x, y, org.z), new Vector3(w, 0f, d));
        }

        if (gridType.spawnVertical)
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.2f);
            for (int k = 0; k <= Rows; k++)
            {
                float z = org.z - halfD + k * _cellSize;
                Gizmos.DrawWireCube(new Vector3(org.x, midY, z), new Vector3(w, StackHeight, 0f));
            }
        }

        if (gridType.spawnCrossVertical)
        {
            Gizmos.color = new Color(0f, 0.6f, 1f, 0.2f);
            for (int k = 0; k <= Columns; k++)
            {
                float x = org.x - halfW + k * _cellSize;
                Gizmos.DrawWireCube(new Vector3(x, midY, org.z), new Vector3(0f, StackHeight, d));
            }
        }
    }
#endif
}
