using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class LandscapeTool : MonoBehaviour
{
    [Header("Settings")]
    public Material targetMaterial;
    public Transform hillHandlesParent;
    [Range(0, 10)] public float heightMultiplier = 1.0f;

    private readonly List<Transform> _hillHandles = new();
    private readonly List<Renderer>  _renderers   = new();
    private Vector4[] _shaderData = new Vector4[100];
    private MaterialPropertyBlock _propBlock;

    void OnEnable()                    => RefreshRenderers();
    void OnTransformChildrenChanged()  => RefreshRenderers();

    void Update()
    {
        RefreshHandlesFromParent();

        if (_hillHandles.Count == 0) return;
        if (_renderers.Count == 0) RefreshRenderers();
        if (_renderers.Count == 0) return;

        int count = Mathf.Min(_hillHandles.Count, 100);

        for (int i = 0; i < count; i++)
        {
            if (_hillHandles[i] != null)
            {
                Vector3 pos    = _hillHandles[i].position;
                float   radius = _hillHandles[i].localScale.x;
                _shaderData[i] = new Vector4(pos.x, pos.y, pos.z, radius);
            }
        }

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        _propBlock.SetVectorArray("_HillPositions", _shaderData);
        _propBlock.SetFloat("_PointCount",   (float)count);
        _propBlock.SetFloat("_GlobalHeight", heightMultiplier);

        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.SetPropertyBlock(_propBlock);
        }
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
