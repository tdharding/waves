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

    private readonly List<Transform> _hillHandles = new();
    private readonly List<Renderer>  _renderers   = new();
    private Vector4[] _shaderData = new Vector4[100];
    private float[]   _sharpData  = new float[100];   // 1 - smoothness: the shader wants sharpness
    private float[]   _noiseData  = new float[100];
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

        if (_hillHandles.Count == 0) return;
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

        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.SetPropertyBlock(_propBlock);
            FitBoundsToHills(r, count);
        }
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
    void FitBoundsToHills(Renderer r, int count)
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

        float scaleY = Mathf.Abs(t.lossyScale.y);
        if (scaleY < 1e-5f) scaleY = 1f;

        float bottom = flat.min.y + sink / scaleY;
        float top    = flat.max.y + rise / scaleY;

        r.localBounds = new Bounds(
            new Vector3(flat.center.x, (bottom + top) * 0.5f, flat.center.z),
            new Vector3(flat.size.x,   top - bottom,          flat.size.z));
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
