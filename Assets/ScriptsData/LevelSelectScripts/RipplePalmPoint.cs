using UnityEngine;

[ExecuteAlways]
public class RipplePalmPoint : MonoBehaviour
{
    [SerializeField] private Material _material;
    [SerializeField] private Transform _palmPoint;

    private static readonly int PalmPointId = Shader.PropertyToID("_RipplePalmPoint");

    private void Update()
    {
        if (_material == null || _palmPoint == null) return;
        _material.SetVector(PalmPointId, _palmPoint.position);
    }
}
