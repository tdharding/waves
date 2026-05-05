using UnityEngine;

// Pushes two world-space positions to the RiverRun material so the shader can
// project fragment positions onto the line between them for a dither alpha fade.
// Alpha = 0 at _FadeStartWorld, Alpha = 1 at _FadeEndWorld.
[ExecuteAlways]
public class RiverRunDitherFade : MonoBehaviour
{
    [SerializeField] private Material _riverRunMaterial;
    [SerializeField] private Transform _alphaZeroPoint;
    [SerializeField] private Transform _alphaOnePoint;

    private static readonly int FadeStartId = Shader.PropertyToID("_FadeStartWorld");
    private static readonly int FadeEndId   = Shader.PropertyToID("_FadeEndWorld");

    private void Update()
    {
        if (_riverRunMaterial == null || _alphaZeroPoint == null || _alphaOnePoint == null) return;
        _riverRunMaterial.SetVector(FadeStartId, _alphaZeroPoint.position);
        _riverRunMaterial.SetVector(FadeEndId,   _alphaOnePoint.position);
    }
}
