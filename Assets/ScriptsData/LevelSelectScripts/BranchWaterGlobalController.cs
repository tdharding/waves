using UnityEngine;

// Manages the alpha-fade points for the branch water material globally.
// This allows you to control the "cut-off" point for all spawned river branches
// from a single object in your scene.
[ExecuteAlways]
public class BranchWaterGlobalController : MonoBehaviour
{
    [Header("Material to Control")]
    [SerializeField] private Material _branchMaterial;

    [Header("Fade Transforms")]
    [SerializeField] private Transform _fadeStartPoint;
    [SerializeField] private Transform _fadeEndPoint;

    private static readonly int FadeStartId = Shader.PropertyToID("_FadeStartWorld");
    private static readonly int FadeEndId   = Shader.PropertyToID("_FadeEndWorld");

    private void Update()
    {
        if (_branchMaterial == null || _fadeStartPoint == null || _fadeEndPoint == null) return;

        // Updating the shared material properties affects all objects using this material.
        _branchMaterial.SetVector(FadeStartId, _fadeStartPoint.position);
        _branchMaterial.SetVector(FadeEndId,   _fadeEndPoint.position);
    }
}
