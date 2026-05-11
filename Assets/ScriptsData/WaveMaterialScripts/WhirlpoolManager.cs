using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class WhirlpoolManager : MonoBehaviour
{
    [Header("Settings")]
    public Renderer targetRenderer;
    public Transform whirlpoolHandlesParent;
    [Range(0f, 20f)] public float globalDepth = 5f;
    [Range(0f, 10f)] public float globalSwirl = 2f;

    private const int MaxWhirlpools = 8;

    private List<Transform> _handles = new List<Transform>();
    private Vector4[] _shaderData = new Vector4[MaxWhirlpools];
    private MaterialPropertyBlock _propBlock;

    [HideInInspector] public Transform wavePlaneTransform;

    public static WhirlpoolManager Instance { get; private set; }

    // When set via ApplyGridData, we skip the child-transform path
    private bool _dataOverride;
    private int  _overrideCount;

    void OnEnable()  { if (Instance == null) Instance = this; }
    void OnDisable() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (_dataOverride)
        {
            PushToShader(_overrideCount);
            return;
        }

        RefreshHandles();
        if (_handles == null || _handles.Count == 0) return;
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        if (targetRenderer == null) return;

        int count = Mathf.Min(_handles.Count, MaxWhirlpools);
        for (int i = 0; i < count; i++)
        {
            if (_handles[i] == null) continue;
            Vector3 worldPos = _handles[i].position;
            Vector3 localPos = wavePlaneTransform != null
                ? wavePlaneTransform.InverseTransformPoint(worldPos)
                : new Vector3(worldPos.x, -worldPos.z, worldPos.y);
            float scale  = wavePlaneTransform != null ? wavePlaneTransform.lossyScale.x : 1f;
            float radius = _handles[i].localScale.x / scale;
            _shaderData[i] = new Vector4(localPos.x, localPos.y, localPos.z, radius);
        }

        PushToShader(count);
    }

    // Called by LevelSpawner with world-space positions already resolved from cell indices
    public void ApplyPositions(Vector4[] data, int count, float depth, float swirl)
    {
        globalDepth = depth;
        globalSwirl = swirl;

        for (int i = 0; i < Mathf.Min(count, MaxWhirlpools); i++)
            _shaderData[i] = data[i];

        _overrideCount = Mathf.Min(count, MaxWhirlpools);
        _dataOverride  = true;

        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        PushToShader(_overrideCount);
    }

    void PushToShader(int count)
    {
        if (targetRenderer == null) return;
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

        targetRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetVectorArray("_WhirlpoolPositions", _shaderData);
        _propBlock.SetFloat("_WhirlpoolCount", (float)count);
        _propBlock.SetFloat("_WhirlpoolDepth", globalDepth);
        _propBlock.SetFloat("_WhirlpoolSwirl", globalSwirl);
        targetRenderer.SetPropertyBlock(_propBlock);

        var meshFilter = targetRenderer.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
            meshFilter.sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 5000f);
    }

    void RefreshHandles()
    {
        _handles.Clear();
        Transform source = whirlpoolHandlesParent != null ? whirlpoolHandlesParent : transform;
        foreach (Transform child in source)
            _handles.Add(child);
    }

    public void SetDepth(float depth) => globalDepth = depth;
    public void SetSwirl(float swirl) => globalSwirl = swirl;

    // Returns world-space Y depression at worldPos due to all active whirlpools.
    // meshScale converts world-space offsets to object space for radius comparisons.
    public float SampleDepthAt(Vector3 worldPos, float meshScale)
    {
        int count = _dataOverride ? _overrideCount : Mathf.Min(_handles.Count, MaxWhirlpools);
        if (count == 0) return 0f;

        float total = 0f;
        for (int i = 0; i < count; i++)
        {
            float wpWorldX, wpWorldZ, wpObjRadius;
            GetWhirlpoolWorldData(i, out wpWorldX, out wpWorldZ, out wpObjRadius);

            float dx   = (worldPos.x - wpWorldX) / meshScale;
            float dz   = (worldPos.z - wpWorldZ) / meshScale;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);

            float h       = 1f - Mathf.Clamp01(dist / Mathf.Max(wpObjRadius, 0.0001f));
            float ss      = h * h * h * (h * (h * 6f - 15f) + 10f);
            float falloff = ss * ss;
            total += falloff * globalDepth;
        }

        return total * meshScale;
    }

    public Vector3 GetPullForceAt(Vector3 worldPos, float meshScale, float radiusMultiplier = 1f)
    {
        int count = _dataOverride ? _overrideCount : Mathf.Min(_handles.Count, MaxWhirlpools);
        if (count == 0) return Vector3.zero;

        Vector3 totalPull = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            float wpWorldX, wpWorldZ, wpObjRadius;
            GetWhirlpoolWorldData(i, out wpWorldX, out wpWorldZ, out wpObjRadius);

            Vector3 toWhirlpool = new Vector3(wpWorldX - worldPos.x, 0f, wpWorldZ - worldPos.z);
            float dist = toWhirlpool.magnitude;
            float distInObjSpace = dist / meshScale;

            float h = 1f - Mathf.Clamp01(distInObjSpace / Mathf.Max(wpObjRadius * radiusMultiplier, 0.0001f));
            float ss = h * h * h * (h * (h * 6f - 15f) + 10f);
            float falloff = ss * ss;

            if (dist > 0.001f)
            {
                totalPull += (toWhirlpool / dist) * falloff;
            }
        }
        return totalPull;
    }

    private void GetWhirlpoolWorldData(int i, out float wpWorldX, out float wpWorldZ, out float wpObjRadius)
    {
        if (_dataOverride || i >= _handles.Count)
        {
            Vector3 wpWorld = wavePlaneTransform != null
                ? wavePlaneTransform.TransformPoint(new Vector3(_shaderData[i].x, _shaderData[i].y, _shaderData[i].z))
                : new Vector3(_shaderData[i].x, 0f, -_shaderData[i].y);
            wpWorldX = wpWorld.x;
            wpWorldZ = wpWorld.z;
            wpObjRadius = _shaderData[i].w;
        }
        else
        {
            wpWorldX = _handles[i].position.x;
            wpWorldZ = _handles[i].position.z;
            float lossyScale = wavePlaneTransform != null ? wavePlaneTransform.lossyScale.x : 1f;
            wpObjRadius = _handles[i].localScale.x / lossyScale;
        }
    }
    }
