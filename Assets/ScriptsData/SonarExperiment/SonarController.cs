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

    [Header("Pulse Origins")]
    [SerializeField] List<Transform> pulseOrigins = new List<Transform>();

    [Header("Feathered Radius")]
    [SerializeField] float radius               = 5f;
    [SerializeField] float feather              = 1.5f;
    [SerializeField] float displaceStrength     = 0.5f;
    [SerializeField] float displaceRadiusOffset = 0f;

    static readonly int GridOffsetID = Shader.PropertyToID("_GridOffset");

    MaterialPropertyBlock _hBlock;
    MaterialPropertyBlock _vBlock;
    readonly Vector4[]    _originBuffer = new Vector4[MaxOrigins];

    void OnEnable()
    {
        _hBlock = new MaterialPropertyBlock();
        _vBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        FollowBoat();
        int count = PackOrigins();
        PushToMaterial(count);
        PushToRenderers(count);
    }

    void FollowBoat()
    {
        if (boatTransform == null || generator == null) return;
        Vector3 pos = generator.transform.position;
        pos.x = boatTransform.position.x;
        pos.z = boatTransform.position.z;
        generator.transform.position = pos;
    }

    int PackOrigins()
    {
        int count = 0;
        foreach (Transform t in pulseOrigins)
        {
            if (count >= MaxOrigins) break;
            _originBuffer[count++] = t != null ? (Vector4)t.position : Vector4.zero;
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

        // Keep shader GridDensity and GridWorldScale locked to generator values
        // so scroll speed always matches the grid pattern frequency
        if (generator != null)
        {
            material.SetFloat(SonarPlaneGenerator.GridDensityShaderID, generator.GridDensity);
            material.SetFloat(SonarPlaneGenerator.GridWorldScaleID,    generator.GridWorldScale);
        }
    }

    void PushToRenderers(int count)
    {
        if (generator == null) return;

        float scale = generator.GridWorldScale;
        float bx    = boatTransform != null ? boatTransform.position.x * scale : 0f;
        float bz    = boatTransform != null ? boatTransform.position.z * scale : 0f;

        SetPulseProperties(_hBlock, count);
        SetPulseProperties(_vBlock, count);
        _hBlock.SetVector(GridOffsetID, new Vector2(bx, bz));
        _vBlock.SetVector(GridOffsetID, new Vector2(bx, 0f));

        foreach (Renderer r in generator.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            r.SetPropertyBlock(r.gameObject.name.StartsWith("Horizontal") ? _hBlock : _vBlock);
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
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        foreach (Transform t in pulseOrigins)
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
