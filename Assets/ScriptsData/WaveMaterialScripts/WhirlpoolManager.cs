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

    [Header("Sonar")]
    public Material sonarMaterial;
    [Range(0f, 20f)] public float sonarWhirlpoolDepth = 5f;
    [Range(0f, 5f)] public float sonarWhirlpoolTaper = 1f;
    public bool sonarDebugLog = false;

    [Header("Active Whirlpools (read-only)")]
    public List<Transform> activeWhirlpools = new List<Transform>();

    private const int MaxWhirlpools = 8;

    private List<Transform> _handles = new List<Transform>();
    private Vector4[] _shaderData = new Vector4[MaxWhirlpools];
    private MaterialPropertyBlock _propBlock;
    private readonly Vector4[] _sonarWorldData = new Vector4[MaxWhirlpools];

    [HideInInspector] public Transform wavePlaneTransform;

    // Feeds SonarWhirlpoolDisplace.hlsl — all values set as shader globals,
    // matching the SonarVertexDisplace pattern (no material reference needed).
    static readonly int SonarWhirlpoolPositionsID = Shader.PropertyToID("_SonarWhirlpoolPositions");
    static readonly int SonarWhirlpoolCountID     = Shader.PropertyToID("_SonarWhirlpoolCount");
    static readonly int SonarWhirlpoolDepthID     = Shader.PropertyToID("_SonarWhirlpoolDepth");
    static readonly int SonarWhirlpoolTaperID     = Shader.PropertyToID("_SonarWhirlpoolTaper");

    public static WhirlpoolManager Instance { get; private set; }

    // When set via ApplyGridData, we skip the child-transform path
    private bool _dataOverride;
    private int  _overrideCount;

    void OnEnable()  { if (Instance == null) Instance = this; }
    void OnDisable() { if (Instance == this) Instance = null; }

    int _lastCount;

    void Update()
    {
        if (_dataOverride)
        {
            PushWavePlane(_overrideCount);
            _lastCount = _overrideCount;
            return;
        }

        RefreshHandles();
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        if (targetRenderer == null) return;

        int count = (_handles != null) ? Mathf.Min(_handles.Count, MaxWhirlpools) : 0;
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

        PushWavePlane(count);
        _lastCount = count;
    }

    void LateUpdate()
    {
        PushToSonarRenderers(_lastCount);
        RefreshDebugList();
    }

    void RefreshDebugList()
    {
        activeWhirlpools.Clear();
        foreach (var h in _handles)
            if (h != null) activeWhirlpools.Add(h);
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
        _lastCount     = _overrideCount;

        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        PushWavePlane(_overrideCount);
    }

    void PushWavePlane(int count)
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

    void PushToSonarRenderers(int count)
    {
        float meshScale = wavePlaneTransform != null ? wavePlaneTransform.lossyScale.x : 1f;

        for (int i = 0; i < count; i++)
        {
            GetWhirlpoolWorldData(i, out float wx, out float wz, out float objRadius);
            _sonarWorldData[i] = new Vector4(wx, 0f, wz, objRadius * meshScale);
        }

        Shader.SetGlobalVectorArray(SonarWhirlpoolPositionsID, _sonarWorldData);
        Shader.SetGlobalFloat(SonarWhirlpoolCountID,  (float)count);
        Shader.SetGlobalFloat(SonarWhirlpoolDepthID,  sonarWhirlpoolDepth);
        Shader.SetGlobalFloat(SonarWhirlpoolTaperID,  sonarWhirlpoolTaper);

        if (sonarDebugLog)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"[WhirlpoolManager] Sonar — count:{count} meshScale:{meshScale:F3} depth:{globalDepth:F3}");
            for (int i = 0; i < count; i++)
                sb.Append($"\n  [{i}] worldXZ:({_sonarWorldData[i].x:F2},{_sonarWorldData[i].z:F2}) radius:{_sonarWorldData[i].w:F3}");
            Debug.Log(sb.ToString());
        }
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

    // Returns the manager to handle-reading mode, undoing any editor test override.
    public void ResetOverride()
    {
        _dataOverride  = false;
        _overrideCount = 0;
    }

    // Fills output with world-space XZ positions and world-space radii (.w).
    // Returns the active whirlpool count.
    public int GetWorldPositions(Vector4[] output, float meshScale)
    {
        int count = _dataOverride ? _overrideCount : Mathf.Min(_handles.Count, MaxWhirlpools);
        for (int i = 0; i < count; i++)
        {
            float wx, wz, objRadius;
            GetWhirlpoolWorldData(i, out wx, out wz, out objRadius);
            output[i] = new Vector4(wx, 0f, wz, objRadius * meshScale);
        }
        return count;
    }

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

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!sonarDebugLog) return;
        float meshScale = wavePlaneTransform != null ? wavePlaneTransform.lossyScale.x : 1f;
        int count = _dataOverride ? _overrideCount : Mathf.Min(_handles != null ? _handles.Count : 0, MaxWhirlpools);
        for (int i = 0; i < count; i++)
        {
            GetWhirlpoolWorldData(i, out float wx, out float wz, out float objR);
            float worldRadius = objR * meshScale;
            Vector3 center = new Vector3(wx, 0f, wz);
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.8f);
            Gizmos.DrawWireSphere(center, worldRadius);
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.2f);
            Gizmos.DrawSphere(center, 0.1f);
        }
    }
#endif

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
