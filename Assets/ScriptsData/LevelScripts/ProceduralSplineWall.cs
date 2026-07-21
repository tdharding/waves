using UnityEngine;

// Generates a single bottomless box "tile" for a spline wall at spawn time.
// LevelSpawner walks each SplineWallPath and, every tileSpacing world-units, instantiates
// this prefab rotated to face the path tangent, then calls Build() with the tile length
// (along the path), the interpolated wall height at that point, and the path's drop depth.
//
// The prefab origin sits on the waterline (LevelSpawner aligns it there), so:
//   • the top face stands +heightAboveWater above the water,
//   • the bottom face drops -depthBelowWater beneath it so the wall looks bottomless.
// The box runs `thickness` across the path (X) and `length` along it (Z, the tangent).
//
// Mirrors ProceduralCubeBuilding: the same mesh is applied to two child renderers — the
// visible surface and the warning-line collision overlay — plus the visible child's MeshCollider.
[DisallowMultipleComponent]
public class ProceduralSplineWall : MonoBehaviour
{
    [Header("Fallback wall thickness across the path (world units); matches the old mesh (~0.09).")]
    [Tooltip("Editor preview / standalone fallback. At runtime the Grid Designer's per-path Thick value is authoritative and overrides this.")]
    [SerializeField] float thickness = 0.09f;

    [Header("Child object names (auto-resolved by name if unassigned)")]
    [SerializeField] string visibleChildName = "MeshVisible";
    [SerializeField] string warningLinesChildName = "WarningCollisionLines";

    [Header("Optional explicit references (override name lookup)")]
    [SerializeField] GameObject visibleChild;
    [SerializeField] GameObject warningLinesChild;

    [Header("Editor preview dimensions (world units)")]
    [SerializeField] float previewLength = 0.09f;
    [SerializeField] float previewHeight = 1f;
    [SerializeField] float previewDepth  = 7f;

    // length = extent along the path (≈ tileSpacing). heightAboveWater / depthBelowWater are
    // measured from the prefab origin (which LevelSpawner aligns to the waterline).
    // distanceAlongPath = this tile's centre distance from the path start (world units); it makes
    // the texture flow continuously across every tile instead of restarting on each one.
    // thicknessOverride > 0 comes from the designer's per-path Thick value and wins; otherwise
    // the serialized thickness is used (editor preview / standalone).
    public void Build(float length, float heightAboveWater, float depthBelowWater, float distanceAlongPath = 0f, float thicknessOverride = -1f)
    {
        float th = thicknessOverride > 0f ? thicknessOverride : thickness;

        Mesh mesh = ProceduralBoxMesh.Build(th, length, heightAboveWater, depthBelowWater, distanceAlongPath);
        mesh.name = "ProceduralSplineWall";

        ApplyMesh(ResolveChild(visibleChild, visibleChildName), mesh, assignCollider: true);
        ApplyMesh(ResolveChild(warningLinesChild, warningLinesChildName), mesh, assignCollider: false);
    }

#if UNITY_EDITOR
    // Lets the prefab be previewed with a mesh in the editor without entering play mode.
    [ContextMenu("Rebuild Preview")]
    void RebuildPreview() => Build(previewLength, previewHeight, previewDepth);
#endif

    GameObject ResolveChild(GameObject explicitRef, string childName)
    {
        if (explicitRef != null) return explicitRef;
        if (string.IsNullOrEmpty(childName)) return null;
        Transform t = transform.Find(childName);
        return t != null ? t.gameObject : null;
    }

    static void ApplyMesh(GameObject go, Mesh mesh, bool assignCollider)
    {
        if (go == null) return;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        if (assignCollider)
        {
            var col = go.GetComponent<MeshCollider>();
            if (col != null) col.sharedMesh = mesh;
        }
    }
}
