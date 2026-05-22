using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class SonarController : MonoBehaviour
{
    const int MaxOrigins = 8;

    [Header("References")]
    [SerializeField] SonarPlaneGenerator generator;
    [SerializeField] Material             material;
    [SerializeField] Transform            boatTransform;

    [Header("Grid Coverage")]
    [SerializeField] float horizontalGridArea = 10f;
    [SerializeField] float verticalGridArea   = 5f;

    [Header("Wave Mask")]
    [SerializeField] Transform waterTransform;
    [SerializeField] Material  waterMaterial;
    [SerializeField] float     waveMaskBias    = 0.05f;
    [SerializeField] float     maskOriginOffset = 0f;

    [Header("Soul Fish Points")]
    [SerializeField] float soulFishAlpha = 1f;

    [Header("Feathered Radius")]
    [SerializeField] float radius               = 5f;
    [SerializeField] float feather              = 1.5f;
    [SerializeField] float displaceStrength     = 0.5f;
    [SerializeField] float displaceRadiusOffset = 0f;

    [Header("Debug")]
    [SerializeField] bool debugLog = false;

    MaterialPropertyBlock _hBlock;
    MaterialPropertyBlock _vBlock;
    MaterialPropertyBlock _xBlock;
    readonly Vector4[]    _originBuffer = new Vector4[MaxOrigins];

    float _debugTimer;

    static readonly int SoulFishAlphaID = Shader.PropertyToID("_SoulFishAlpha");

    static readonly int WaveOriginID  = Shader.PropertyToID("_WaveOrigin");
    static readonly int WaveFreqID    = Shader.PropertyToID("_WaveFrequency");
    static readonly int WaveSpeedID   = Shader.PropertyToID("_WaveSpeed");
    static readonly int WaveRippleID  = Shader.PropertyToID("_WaveRipple");
    static readonly int WaveScaleID   = Shader.PropertyToID("_WaveMeshScale");
    static readonly int WaveBiasID    = Shader.PropertyToID("_WaveMaskBias");

    public void SetBoat(Transform boat) => boatTransform = boat;

    void OnEnable()
    {
        _hBlock = new MaterialPropertyBlock();
        _vBlock = new MaterialPropertyBlock();
        _xBlock = new MaterialPropertyBlock();
        ConfigureGenerator();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) ConfigureGenerator();
        };
    }
#endif

    void ConfigureGenerator()
    {
        if (generator == null || generator.GridType == null) return;
        var   gt           = generator.GridType;
        int   maxAxis      = Mathf.Max(1, gt.columns, gt.rows);
        float cellSize     = horizontalGridArea / maxAxis;
        float levelSpacing = verticalGridArea   / Mathf.Max(1, gt.levels);
        float surfaceY     = waterTransform != null ? waterTransform.position.y : 0f;
        generator.Configure(cellSize, levelSpacing, surfaceY);
    }

    void Update()
    {
        int count = PackOrigins();

        if (waterTransform != null && waterMaterial != null)
        {
            WaveUtils.WaveParams wp = WaveUtils.ReadParams(waterTransform, waterMaterial);
            PushWaveParams(_hBlock, wp);
            PushWaveParams(_vBlock, wp);
            PushWaveParams(_xBlock, wp);

            if (debugLog)
            {
                _debugTimer += Time.deltaTime;
                if (_debugTimer >= 1f)
                {
                    _debugTimer = 0f;
                    Debug.Log($"[SonarController] WaveParams — origin:{wp.origin} freq:{wp.frequency} speed:{wp.speed} ripple:{wp.ripple} scale:{wp.meshScale} bias:{waveMaskBias}");
                }
            }
        }
        else if (debugLog)
{
            _debugTimer += Time.deltaTime;
            if (_debugTimer >= 1f)
            {
                _debugTimer = 0f;
                Debug.LogWarning($"[SonarController] Wave mask not pushing — waterTransform:{(waterTransform == null ? "NULL" : "OK")} waterMaterial:{(waterMaterial == null ? "NULL" : "OK")}");
            }
        }

        PushToMaterial(count);
        PushToRenderers(count);
    }

    void PushWaveParams(MaterialPropertyBlock block, WaveUtils.WaveParams wp)
    {
        block.SetVector(WaveOriginID, new Vector4(wp.origin.x, wp.origin.y + maskOriginOffset, wp.origin.z, 0f));
        block.SetFloat(WaveFreqID,   wp.frequency);
        block.SetFloat(WaveSpeedID,  wp.speed);
        block.SetFloat(WaveRippleID, wp.ripple);
        block.SetFloat(WaveScaleID,  wp.meshScale);
        block.SetFloat(WaveBiasID,   waveMaskBias);
    }

    int PackOrigins()
    {
        var fish  = SoulFishMapLinker.ActiveFish;
        int count = 0;
        if (fish != null)
        {
            for (int i = 0; i < fish.Count && count < MaxOrigins; i++)
            {
                if (fish[i] != null)
                    _originBuffer[count++] = (Vector4)fish[i].position;
            }
        }
        for (int i = count; i < MaxOrigins; i++)
            _originBuffer[i] = Vector4.zero;
        return count;
    }

    void PushToMaterial(int count)
    {
        if (material == null) return;
        material.SetVectorArray(SonarPlaneGenerator.PulseOriginsID,   _originBuffer);
        material.SetFloat(SonarPlaneGenerator.PulseOriginCountID,     count);
        material.SetFloat(SonarPlaneGenerator.PulseRadiusID,          radius);
        material.SetFloat(SonarPlaneGenerator.PulseWidthID,           feather);
        material.SetFloat(SonarPlaneGenerator.DisplaceStrengthID,     displaceStrength);
        material.SetFloat(SonarPlaneGenerator.DisplaceRadiusOffsetID, displaceRadiusOffset);
        material.SetFloat(SoulFishAlphaID,                            soulFishAlpha);

        if (generator != null)
        {
            material.SetFloat(SonarPlaneGenerator.GridDensityShaderID, generator.GridDensity);
            material.SetFloat(SonarPlaneGenerator.GridWorldScaleID,    generator.GridWorldScale);
        }

    }

    void PushToRenderers(int count)
    {
        if (generator == null) return;

        float dens = (float)generator.GridDensity;

        SetPulseProperties(_hBlock, count);
        SetPulseProperties(_vBlock, count);
        SetPulseProperties(_xBlock, count);
        _hBlock.SetFloat(SonarPlaneGenerator.GridDensityShaderID, dens);
        _vBlock.SetFloat(SonarPlaneGenerator.GridDensityShaderID, dens);
        _xBlock.SetFloat(SonarPlaneGenerator.GridDensityShaderID, dens);

        foreach (Renderer r in generator.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            string name = r.gameObject.name;
            MaterialPropertyBlock block = name.StartsWith("Horizontal")    ? _hBlock
                                        : name.StartsWith("CrossVertical") ? _xBlock
                                        : _vBlock;
            r.SetPropertyBlock(block);
        }
    }

    void SetPulseProperties(MaterialPropertyBlock block, int count)
    {
        block.SetVectorArray(SonarPlaneGenerator.PulseOriginsID,   _originBuffer);
        block.SetFloat(SonarPlaneGenerator.PulseOriginCountID,     count);
        block.SetFloat(SonarPlaneGenerator.PulseRadiusID,          radius);
        block.SetFloat(SonarPlaneGenerator.PulseWidthID,           feather);
        block.SetFloat(SonarPlaneGenerator.DisplaceStrengthID,     displaceStrength);
        block.SetFloat(SonarPlaneGenerator.DisplaceRadiusOffsetID, displaceRadiusOffset);
        block.SetFloat(SoulFishAlphaID,                            soulFishAlpha);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        var fish = SoulFishMapLinker.ActiveFish;
        if (fish == null) return;
        foreach (Transform t in fish)
        {
            if (t == null) continue;
            Gizmos.color = new Color(0f, 1f, 0.8f, 0.3f);
            Gizmos.DrawWireSphere(t.position, radius);
            Gizmos.color = new Color(0f, 1f, 0.8f, 0.1f);
            Gizmos.DrawWireSphere(t.position, Mathf.Max(0f, radius - feather));
        }
    }
#endif
}
