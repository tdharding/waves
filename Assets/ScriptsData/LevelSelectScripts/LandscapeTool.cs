using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class LandscapeTool : MonoBehaviour
{
    [Header("Settings")]
    public Material targetMaterial;
    public Transform hillHandlesParent;
    [Range(0, 10)] public float heightMultiplier = 1.0f;

    [Tooltip("Frequency of the rocky noise on hills that have a Noise amount. Set by the Level " +
             "Select Designer.")]
    public float noiseScale = 0.25f;

    /// <summary>Most river edge points the shader holds — RIVER_EDGE_MAX in CalculateHills.hlsl.</summary>
    public const int MaxRiverEdgePoints = 256;

    // Where the river runs and pools stand, traced by the Level Select Designer whenever they are
    // built. Every run and pool is here, as long as at least one path has Landscape Influence on:
    // the landscape is cut away inside all of them, and lifted round the ones whose path asks.
    //   xyz  a point on a run's centreline, at its rim top (a pool: its centre)
    //   w    half the outer width (a pool: its outer radius); NEGATIVE where the line ends here,
    //        so the next point starts a new one. A point on its own is a disc.
    // Reach, one per point: the path's Influence Distance, or negative for cut-only.
    [HideInInspector] public List<Vector4> riverEdgePoints = new();
    [HideInInspector] public List<float>   riverEdgeReach  = new();

    private readonly List<Transform> _hillHandles = new();
    private readonly List<Renderer>  _renderers   = new();
    private Vector4[] _shaderData = new Vector4[100];
    private float[]   _sharpData  = new float[100];   // 1 - smoothness: the shader wants sharpness
    private float[]   _noiseData  = new float[100];
    private Vector4[] _edgePoints = new Vector4[MaxRiverEdgePoints];
    private float[]   _edgeReach  = new float[MaxRiverEdgePoints];
    private MaterialPropertyBlock _propBlock;

    // Set when something the tiles are drawn from has changed. The renderers are only written to
    // then, not every tick: the hills only move when a handle or a setting is edited.
    private bool  _dirty = true;
    private int   _appliedCount = -1;
    private float _appliedHeight;
    private float _appliedNoiseScale;

    void OnEnable()                    { RefreshRenderers(); _dirty = true; }
    void OnTransformChildrenChanged()  { RefreshRenderers(); _dirty = true; }
    void OnValidate()                  { RefreshRenderers(); _dirty = true; }

    void Update()
    {
        RefreshHandlesFromParent();

        // With no hills there is still the river edges to send when they change.
        if (_hillHandles.Count == 0 && !_dirty) return;
        if (_renderers.Count == 0) { RefreshRenderers(); _dirty = true; }
        if (_renderers.Count == 0) return;

        int count = Mathf.Min(_hillHandles.Count, 100);
        if (count != _appliedCount || heightMultiplier != _appliedHeight ||
            noiseScale != _appliedNoiseScale) _dirty = true;

        // Compared against what was last sent, so a handle dragged, scaled, added or removed is
        // caught without anything having to report it.
        for (int i = 0; i < count; i++)
        {
            if (_hillHandles[i] != null)
            {
                Vector3 pos    = _hillHandles[i].position;
                float   radius = _hillHandles[i].localScale.x;
                var     data   = new Vector4(pos.x, pos.y, pos.z, radius);
                if (_shaderData[i] != data) { _shaderData[i] = data; _dirty = true; }

                var   hp    = _hillHandles[i].GetComponent<HillPoint>();
                float sharp = hp != null ? 1f - Mathf.Clamp01(hp.smoothness) : 0f;
                float noise = hp != null ? Mathf.Clamp01(hp.noise) : 0f;
                if (_sharpData[i] != sharp) { _sharpData[i] = sharp; _dirty = true; }
                if (_noiseData[i] != noise) { _noiseData[i] = noise; _dirty = true; }
            }
        }

        if (!_dirty) return;
        _dirty         = false;
        _appliedCount  = count;
        _appliedHeight = heightMultiplier;
        _appliedNoiseScale = noiseScale;

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        _propBlock.SetVectorArray("_HillPositions", _shaderData);
        _propBlock.SetFloatArray("_HillSharpness",  _sharpData);
        _propBlock.SetFloatArray("_HillNoise",      _noiseData);
        _propBlock.SetFloat("_HillNoiseScale", noiseScale);
        _propBlock.SetFloat("_PointCount",   (float)count);
        _propBlock.SetFloat("_GlobalHeight", heightMultiplier);

        int edges = FillRiverEdges();
        _propBlock.SetVectorArray("_RiverEdgePoints", _edgePoints);
        _propBlock.SetFloatArray("_RiverEdgeReach",   _edgeReach);
        _propBlock.SetFloat("_RiverEdgeCount", edges);

        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.SetPropertyBlock(_propBlock);
            FitBoundsToHills(r, count, edges);
        }
    }

    /// <summary>
    /// Replaces the river edges the landscape is lifted to and cut round. Called by the Level
    /// Select Designer when runs or pools are built, or a path's Landscape Influence changes.
    /// </summary>
    public void SetRiverEdges(IList<Vector4> points, IList<float> reach)
    {
        riverEdgePoints = points != null ? new List<Vector4>(points) : new List<Vector4>();
        riverEdgeReach  = reach  != null ? new List<float>(reach)    : new List<float>();
        _dirty = true;
    }

    // The arrays are always sent full length: a property block locks an array's size the first
    // time it is set.
    int FillRiverEdges()
    {
        int count = Mathf.Min(riverEdgePoints.Count, riverEdgeReach.Count, MaxRiverEdgePoints);
        for (int i = 0; i < MaxRiverEdgePoints; i++)
        {
            _edgePoints[i] = i < count ? riverEdgePoints[i] : Vector4.zero;
            _edgeReach[i]  = i < count ? riverEdgeReach[i]  : -1f;
        }
        return count;
    }

    // The hills are raised in the shader, after Unity has already decided from the tile's box
    // whether it is on screen. The mesh's own box is the flat tile, so a tile whose hill was in
    // view but whose base was not got skipped and the hill popped out. Grow each tile's box to
    // the highest hill and deepest dip that can reach it.
    //
    // Every hill touching the tile is counted at full height, as the shader adds overlapping
    // hills together — never too small, only ever a little too tall. Rocky noise can push a hill
    // up to (1 + noise) of its height, so that is what is counted. Assumes tiles are not rotated,
    // which the designer never does.
    void FitBoundsToHills(Renderer r, int count, int edges)
    {
        var filter = r.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;

        Bounds    flat = filter.sharedMesh.bounds;
        Transform t    = r.transform;

        Vector3 a = t.TransformPoint(new Vector3(flat.min.x, 0f, flat.min.z));
        Vector3 b = t.TransformPoint(new Vector3(flat.max.x, 0f, flat.max.z));
        float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
        float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);

        float rise = 0f, sink = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector4 hill   = _shaderData[i];
            float   radius = hill.w;
            if (radius <= 0f) continue;

            // Nearest point of the tile to the hill's centre
            float nx = Mathf.Clamp(hill.x, minX, maxX);
            float nz = Mathf.Clamp(hill.z, minZ, maxZ);
            float dx = hill.x - nx, dz = hill.z - nz;
            if (dx * dx + dz * dz >= radius * radius) continue;

            float height = hill.y * heightMultiplier * (1f + _noiseData[i]);
            if (height > 0f) rise += height; else sink += height;
        }

        // Ground lifted to a river's rim replaces the hills there rather than adding to them, so
        // the box only has to reach the highest rim whose influence touches the tile.
        rise = Mathf.Max(rise, RiverEdgeRise(minX, maxX, minZ, maxZ, a.y, edges));

        float scaleY = Mathf.Abs(t.lossyScale.y);
        if (scaleY < 1e-5f) scaleY = 1f;

        float bottom = flat.min.y + sink / scaleY;
        float top    = flat.max.y + rise / scaleY;

        r.localBounds = new Bounds(
            new Vector3(flat.center.x, (bottom + top) * 0.5f, flat.center.z),
            new Vector3(flat.size.x,   top - bottom,          flat.size.z));
    }

    // How far above the tile base the highest rim reaching this tile stands. Each line is
    // checked by its box, grown by its half width and reach, so it is never too small.
    float RiverEdgeRise(float minX, float maxX, float minZ, float maxZ, float baseY, int edges)
    {
        float rise = 0f;
        for (int i = 0; i < edges; i++)
        {
            float reach = _edgeReach[i];
            if (reach < 0f) continue;

            Vector4 p = _edgePoints[i];
            Vector4 q = p.w > 0f && i + 1 < edges ? _edgePoints[i + 1] : p;
            float   grow = Mathf.Max(Mathf.Abs(p.w), Mathf.Abs(q.w)) + reach;

            if (Mathf.Max(p.x, q.x) + grow < minX || Mathf.Min(p.x, q.x) - grow > maxX) continue;
            if (Mathf.Max(p.z, q.z) + grow < minZ || Mathf.Min(p.z, q.z) - grow > maxZ) continue;

            rise = Mathf.Max(rise, Mathf.Max(p.y, q.y) - baseY);
        }
        return rise;
    }

    void RefreshRenderers()
    {
        _renderers.Clear();
        if (targetMaterial == null) return;

        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == targetMaterial) { _renderers.Add(r); break; }
            }
        }
    }

    void RefreshHandlesFromParent()
    {
        _hillHandles.Clear();
        Transform source = hillHandlesParent != null ? hillHandlesParent : transform;
        foreach (Transform child in source)
            _hillHandles.Add(child);
    }
}
