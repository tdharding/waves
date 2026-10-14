using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// The level select landscape. The hills, their rocky noise and the river edges are BAKED into
/// each tile's mesh (see <see cref="LandscapeTileBaker"/>) — nothing about their shape is worked
/// out on the GPU. A tile is re-baked only when something that reaches it changes: a hill handle
/// moved, scaled, added or removed, the Height Multiplier or noise scale, the river edges, or the
/// tile itself. Edit mode only; in play the saved meshes are just drawn.
///
/// The baked meshes are saved as assets in the generated folder, so a scene reload or a build
/// needs no bake.
/// </summary>
[ExecuteAlways]
public class LandscapeTool : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("The landscape material. Assigned by the Level Select Designer.")]
    public Material targetMaterial;
    public Transform hillHandlesParent;
    [Range(0, 10)] public float heightMultiplier = 1.0f;

    [Tooltip("Frequency of the rocky noise on hills that have a Noise amount. Set by the Level " +
             "Select Designer.")]
    public float noiseScale = 0.25f;

    [Header("Gizmo")]
    [Tooltip("The Hole Depth plane: everything below it is shaded as Holes. Alpha sets how see-" +
             "through it is. Toggle it with LandscapeTool in the Gizmos menu.")]
    public Color holeDepthGizmoColour = new Color(1f, 0.3f, 0.1f, 0.25f);

    /// <summary>Most river edge points traced into the landscape.</summary>
    public const int MaxRiverEdgePoints = 256;

    // Where the river runs and pools stand, traced by the Level Select Designer whenever they are
    // built. Every run and pool is here, as long as at least one path has Landscape Influence on:
    // the landscape is cut away inside all of them, and lifted round the ones whose path asks.
    //   xyz  a point on a run's centreline, at its rim top (a pool: its centre)
    //   w    half the outer width (a pool: its outer radius); NEGATIVE where the line ends here,
    //        so the next point starts a new one. A point on its own is a disc.
    // Reach, one per point: the path's Influence Distance, or negative for cut-only.
    // Strength, one per point: the path's Influence Strength, 0..1 (missing reads as 1).
    // Shelf, one per point: negative (or missing) for the usual lift, just under the rim top. A
    // run with its outer walls off has the ground run straight onto its rim instead: lifted to
    // the rim top exactly and held level this far past its outer edge before easing back.
    [HideInInspector] public List<Vector4> riverEdgePoints   = new();
    [HideInInspector] public List<float>   riverEdgeReach    = new();
    [HideInInspector] public List<float>   riverEdgeStrength = new();
    [HideInInspector] public List<float>   riverEdgeShelf    = new();

    /// <summary>The only thing the landscape shader still reads: the grain of the noise lean.</summary>
    static readonly int NoiseScaleId = Shader.PropertyToID("_HillNoiseScale");

    // Read back for the Hole Depth gizmo — the very values the landscape shading uses.
    static readonly int HoleDepthId = Shader.PropertyToID("_LandscapeHoleDepth");
    static readonly int BaseYId     = Shader.PropertyToID("_LandscapeBaseY");

    // Bumped whenever the bake itself changes, so every tile re-bakes once.
    const int BakeVersion = 1;

    private readonly List<Transform> _hillHandles = new();
    private readonly LandscapeHillHeight.Surface _surface = new();

    // What each tile was last baked from. Each tick compares what reaches a tile against this,
    // and only the tiles that differ re-bake.
    private readonly Dictionary<LandscapeTileMesh, ulong> _bakedFrom = new();

    void OnEnable()
    {
        PushNoiseScale();
        ClearOldPropertyBlocks();
        MarkDirty();
    }

    void OnTransformChildrenChanged() => MarkDirty();
    void OnValidate()                 => MarkDirty();

    /// <summary>
    /// Something a tile is baked from changed (a tile's size or subdivisions, the river edges):
    /// asks for an edit-mode tick, which re-bakes whatever it reaches.
    /// </summary>
    public void MarkDirty()
    {
#if UNITY_EDITOR
        EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    void Update()
    {
        // A bare global: no value is kept on the material, so it is sent every tick.
        PushNoiseScale();

        if (Application.isPlaying) return;

        // Edit mode only ticks when something in the scene changes, so every tick checks every
        // tile — cheap, as each only re-bakes if what reaches it is different. That catches a tile
        // moved (the Height Offset) as well as everything the tool is told about.
        ReadHills();

        _surface.globalHeight = heightMultiplier;
        _surface.noiseScale   = noiseScale;
        _surface.edgePoints   = riverEdgePoints;
        _surface.edgeReach    = riverEdgeReach;
        _surface.edgeStrength = riverEdgeStrength;
        _surface.edgeShelf    = riverEdgeShelf;
        _surface.edgeCount    = Mathf.Min(riverEdgePoints.Count, riverEdgeReach.Count, MaxRiverEdgePoints);

        foreach (var tile in GetComponentsInChildren<LandscapeTileMesh>())
            BakeIfChanged(tile);
    }

    void PushNoiseScale() => Shader.SetGlobalFloat(NoiseScaleId, noiseScale);

    // The landscape used to be raised in its shader from a property block on every tile. The
    // block would keep the tiles out of the SRP batcher for nothing now, so it is cleared.
    void ClearOldPropertyBlocks()
    {
        foreach (var tile in GetComponentsInChildren<LandscapeTileMesh>())
        {
            var r = tile.GetComponent<Renderer>();
            if (r != null && r.HasPropertyBlock()) r.SetPropertyBlock(null);
        }
    }

    /// <summary>
    /// Replaces the river edges the landscape is lifted to and cut round. Called by the Level
    /// Select Designer when runs or pools are built, or a path's Landscape Influence changes.
    /// </summary>
    public void SetRiverEdges(IList<Vector4> points, IList<float> reach, IList<float> strength,
                              IList<float> shelf = null)
    {
        riverEdgePoints   = points   != null ? new List<Vector4>(points) : new List<Vector4>();
        riverEdgeReach    = reach    != null ? new List<float>(reach)    : new List<float>();
        riverEdgeStrength = strength != null ? new List<float>(strength) : new List<float>();
        riverEdgeShelf    = shelf    != null ? new List<float>(shelf)    : new List<float>();
        MarkDirty();
    }

    /// <summary>A traced point's Influence Strength; 1 where none was traced (edges traced before it existed).</summary>
    public static float RiverEdgeStrengthAt(IList<float> strength, int i)
        => strength != null && i < strength.Count ? Mathf.Clamp01(strength[i]) : 1f;

    /// <summary>A traced point's shelf: negative for the usual lift, where none was traced too.</summary>
    public static float RiverEdgeShelfAt(IList<float> shelf, int i)
        => shelf != null && i < shelf.Count ? shelf[i] : -1f;

    // Reads the hill handles into the surface, so a handle dragged, scaled, added or removed is
    // caught without anything having to report it.
    void ReadHills()
    {
        _hillHandles.Clear();
        Transform source = hillHandlesParent != null ? hillHandlesParent : transform;
        foreach (Transform child in source)
            _hillHandles.Add(child);

        int count = Mathf.Min(_hillHandles.Count, LandscapeHillHeight.MaxHills);
        _surface.hillCount = count;

        for (int i = 0; i < count; i++)
        {
            var handle = _hillHandles[i];
            if (handle == null) continue;

            Vector3 pos    = handle.position;
            var     hill   = new Vector4(pos.x, pos.y, pos.z, handle.localScale.x);
            var     hp     = handle.GetComponent<HillPoint>();
            float   sharp  = hp != null ? 1f - Mathf.Clamp01(hp.smoothness) : 0f;
            float   noise  = hp != null ? Mathf.Clamp01(hp.noise) : 0f;

            _surface.hills[i]     = hill;
            _surface.sharpness[i] = sharp;
            _surface.noise[i]     = noise;
        }
    }

    // ── Baking ──

    void BakeIfChanged(LandscapeTileMesh tile)
    {
        var filter = tile.GetComponent<MeshFilter>();
        if (filter == null) return;

        Transform t     = tile.transform;
        int       cellsX = Mathf.Max(1, tile.subdivisionsX);
        int       cellsZ = Mathf.Max(1, tile.subdivisionsZ);

        Vector3 a = t.TransformPoint(Vector3.zero);
        Vector3 b = t.TransformPoint(new Vector3(tile.width, 0f, tile.depth));
        float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
        float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);

        Vector3 cell      = t.TransformVector(new Vector3(tile.width / cellsX, 0f, tile.depth / cellsZ));
        float   cellReach = new Vector2(cell.x, cell.z).magnitude;

        FindNear(minX, maxX, minZ, maxZ, cellReach);

        ulong from = Fingerprint(tile, cellsX, cellsZ);
        bool  have = _bakedFrom.TryGetValue(tile, out ulong was) && was == from && IsBaked(tile, filter);
        if (have) return;

        var mesh = MeshFor(tile, filter);
        LandscapeTileBaker.Bake(mesh, _surface, t, tile.width, tile.depth, cellsX, cellsZ);
        _bakedFrom[tile] = from;

        var r = tile.GetComponent<Renderer>();
        if (r != null) r.ResetLocalBounds();   // the mesh's own box is right now

#if UNITY_EDITOR
        if (EditorUtility.IsPersistent(mesh)) EditorUtility.SetDirty(mesh);
#endif
    }

    // The hills and river lines that can reach this tile — everything else is too far away to
    // change a single vertex of it.
    readonly List<int> _nearHills = new(), _nearLifts = new(), _nearCuts = new();

    void FindNear(float minX, float maxX, float minZ, float maxZ, float cellReach)
    {
        _nearHills.Clear();
        _nearLifts.Clear();
        _nearCuts.Clear();

        // The normal is measured a step away, so a hill just past the tile still tilts its edge.
        float pad = LandscapeHillHeight.NormalStep;
        for (int i = 0; i < _surface.hillCount; i++)
        {
            Vector4 hill   = _surface.hills[i];
            float   radius = hill.w;
            if (radius <= 0f) continue;

            float nx = Mathf.Clamp(hill.x, minX - pad, maxX + pad);
            float nz = Mathf.Clamp(hill.z, minZ - pad, maxZ + pad);
            float dx = hill.x - nx, dz = hill.z - nz;
            if (dx * dx + dz * dz < radius * radius) _nearHills.Add(i);
        }

        for (int i = 0; i < _surface.edgeCount; i++)
        {
            Vector4 p    = _surface.edgePoints[i];
            Vector4 q    = _surface.EdgeEnd(i);
            float   half = Mathf.Max(Mathf.Abs(p.w), Mathf.Abs(q.w));

            // The cut is looked at a cell past the tile (to meet the next tile cleanly), and a line
            // only matters there within a couple of cells of its edge.
            if (Overlaps(p, q, half + 3f * cellReach, minX, maxX, minZ, maxZ)) _nearCuts.Add(i);

            float reach = _surface.edgeReach[i];
            float shelf = Mathf.Max(0f, RiverEdgeShelfAt(_surface.edgeShelf, i));
            if (reach >= 0f && Overlaps(p, q, half + shelf + reach + pad, minX, maxX, minZ, maxZ)) _nearLifts.Add(i);
        }

        _surface.nearHills = _nearHills;
        _surface.nearLifts = _nearLifts;
        _surface.nearCuts  = _nearCuts;
    }

    static bool Overlaps(Vector4 p, Vector4 q, float grow, float minX, float maxX, float minZ, float maxZ)
        => Mathf.Max(p.x, q.x) + grow >= minX && Mathf.Min(p.x, q.x) - grow <= maxX &&
           Mathf.Max(p.z, q.z) + grow >= minZ && Mathf.Min(p.z, q.z) - grow <= maxZ;

    // Everything this tile's bake depends on, boiled down to one number.
    ulong Fingerprint(LandscapeTileMesh tile, int cellsX, int cellsZ)
    {
        ulong h = 14695981039346656037ul;
        void Add(float v) { unchecked { h = (h ^ (uint)System.BitConverter.SingleToInt32Bits(v)) * 1099511628211ul; } }
        void AddV(Vector4 v) { Add(v.x); Add(v.y); Add(v.z); Add(v.w); }

        Add(BakeVersion);
        Matrix4x4 m = tile.transform.localToWorldMatrix;
        for (int k = 0; k < 16; k++) Add(m[k]);
        Add(tile.width); Add(tile.depth); Add(cellsX); Add(cellsZ);
        Add(heightMultiplier); Add(noiseScale);

        foreach (int i in _nearHills)
        {
            Add(i); AddV(_surface.hills[i]); Add(_surface.sharpness[i]); Add(_surface.noise[i]);
        }
        Add(-1f);
        foreach (int i in _nearLifts)
        {
            Add(i); AddV(_surface.edgePoints[i]); AddV(_surface.EdgeEnd(i)); Add(_surface.edgeReach[i]);
            Add(RiverEdgeStrengthAt(_surface.edgeStrength, i));
            Add(RiverEdgeShelfAt(_surface.edgeShelf, i));
        }
        Add(-2f);
        foreach (int i in _nearCuts)
        {
            Add(i); AddV(_surface.edgePoints[i]); AddV(_surface.EdgeEnd(i));
        }
        return h;
    }

    // ── Where the baked mesh lives ──

#if UNITY_EDITOR
    const string BakedDir = "Assets/Prefab/LevelSelectPrefabs/Generated";

    static string AssetNameFor(LandscapeTileMesh tile)
    {
        // Named by scene and place, so a regenerate over the same layout reuses the same assets.
        Vector3 p = tile.transform.position;
        return $"LandscapeTile_{tile.gameObject.scene.name}_{Mathf.RoundToInt(p.x * 100f)}_{Mathf.RoundToInt(p.z * 100f)}";
    }
#endif

    static bool IsBaked(LandscapeTileMesh tile, MeshFilter filter)
    {
#if UNITY_EDITOR
        return filter.sharedMesh != null && EditorUtility.IsPersistent(filter.sharedMesh) &&
               filter.sharedMesh.name == AssetNameFor(tile);
#else
        return filter.sharedMesh != null;
#endif
    }

    // The tile's saved mesh asset, made if it has none yet, and pointed to by the tile.
    Mesh MeshFor(LandscapeTileMesh tile, MeshFilter filter)
    {
#if UNITY_EDITOR
        if (IsBaked(tile, filter)) return filter.sharedMesh;

        string name = AssetNameFor(tile);
        string path = $"{BakedDir}/{name}.asset";

        // Not saved while the scene itself has never been saved: there is no name to file it under.
        if (string.IsNullOrEmpty(tile.gameObject.scene.name) || !AssetDatabase.IsValidFolder(BakedDir))
            return LooseMesh(filter);

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = name };
            AssetDatabase.CreateAsset(mesh, path);
        }

        filter.sharedMesh = mesh;
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
        return mesh;
#else
        return LooseMesh(filter);
#endif
    }

    static Mesh LooseMesh(MeshFilter filter)
    {
        var mesh = filter.sharedMesh;
        if (mesh == null || IsAsset(mesh))
        {
            mesh = new Mesh { name = "LandscapeTile_Baked" };
            filter.sharedMesh = mesh;
        }
        return mesh;
    }

    static bool IsAsset(Mesh mesh)
    {
#if UNITY_EDITOR
        return EditorUtility.IsPersistent(mesh);
#else
        return false;
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// A plane across the whole landscape at Hole Depth below the tile surface — everything under
    /// it is shaded as Holes. Read from the globals the shading reads, so it follows the tuner
    /// while it drives and the world's preset otherwise.
    /// </summary>
    void OnDrawGizmos()
    {
        var tiles = GetComponentsInChildren<LandscapeTileMesh>();
        if (tiles.Length == 0) return;

        bool   any    = false;
        Bounds extent = default;
        foreach (var tile in tiles)
        {
            var r = tile.GetComponent<Renderer>();
            if (r == null) continue;
            if (!any) { extent = r.bounds; any = true; }
            else        extent.Encapsulate(r.bounds);
        }
        if (!any) return;

        float holeDepth = Shader.GetGlobalFloat(HoleDepthId);
        float y         = Shader.GetGlobalFloat(BaseYId) - holeDepth;

        var centre = new Vector3(extent.center.x, y, extent.center.z);
        var size   = new Vector3(extent.size.x, 0f, extent.size.z);

        Gizmos.color = holeDepthGizmoColour;
        Gizmos.DrawCube(centre, size);

        var outline = holeDepthGizmoColour;
        outline.a = 1f;
        Gizmos.color = outline;
        Gizmos.DrawWireCube(centre, size);

        Handles.color = outline;
        Handles.Label(new Vector3(extent.min.x, y, extent.min.z),
                      $"Hole Depth {holeDepth:0.##}  (Y {y:0.##})");
    }
#endif
}
