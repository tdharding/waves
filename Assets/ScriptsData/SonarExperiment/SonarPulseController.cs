using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class SonarPulseController : MonoBehaviour
{
    const int MaxOrigins = 8;

    [Header("References")]
    [SerializeField] SonarPlaneGenerator generator;
    [SerializeField] List<Transform>      pulseOrigins = new List<Transform>();
    [SerializeField] Material             material;

    [Header("Feathered Radius")]
    [SerializeField] float radius             = 5f;
    [SerializeField] float feather            = 1.5f;
    [SerializeField] float displaceStrength   = 0.5f;
    [SerializeField] float displaceRadiusOffset = 0f;

    MaterialPropertyBlock _block;
    readonly Vector4[]    _originBuffer = new Vector4[MaxOrigins];

    void OnEnable() => _block = new MaterialPropertyBlock();

    void Update()
    {
        int count = PackOrigins();
        if (count == 0) return;

        PushToMaterial(count);
    }

    int PackOrigins()
    {
        int count = 0;
        foreach (Transform t in pulseOrigins)
        {
            if (count >= MaxOrigins) break;
            _originBuffer[count++] = t != null ? (Vector4)t.position : Vector4.zero;
        }
        // Zero out unused slots so stale positions don't linger
        for (int i = count; i < MaxOrigins; i++)
            _originBuffer[i] = Vector4.zero;
        return count;
    }

    void PushToMaterial(int count)
    {
        if (material == null) return;
        material.SetVectorArray(SonarPlaneGenerator.PulseOriginsID,     _originBuffer);
        material.SetFloat(SonarPlaneGenerator.PulseOriginCountID,       count);
        material.SetFloat(SonarPlaneGenerator.PulseRadiusID,            radius);
        material.SetFloat(SonarPlaneGenerator.PulseWidthID,             feather);
        material.SetFloat(SonarPlaneGenerator.DisplaceStrengthID,       displaceStrength);
        material.SetFloat(SonarPlaneGenerator.DisplaceRadiusOffsetID,   displaceRadiusOffset);
    }

    void PushToRenderers(int count)
    {
        if (generator == null) return;
        _block.SetVectorArray(SonarPlaneGenerator.PulseOriginsID,    _originBuffer);
        _block.SetFloat(SonarPlaneGenerator.PulseOriginCountID,      count);
        _block.SetFloat(SonarPlaneGenerator.PulseRadiusID,           radius);
        _block.SetFloat(SonarPlaneGenerator.PulseWidthID,            feather);
        _block.SetFloat(SonarPlaneGenerator.DisplaceStrengthID,      displaceStrength);
        _block.SetFloat(SonarPlaneGenerator.DisplaceRadiusOffsetID,  displaceRadiusOffset);

        foreach (Renderer r in generator.GetComponentsInChildren<Renderer>())
            if (r != null) r.SetPropertyBlock(_block);
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
