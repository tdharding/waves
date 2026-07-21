using UnityEngine;

// Generates a "node column" marker at each spline-wall control node at spawn time.
// LevelSpawner instantiates the node prefab at each node and calls Build() with the wall
// height at that node and the path's drop depth.
//
// The column is a bottomless box like ProceduralSplineWall, but slightly thicker and
// `extraHeight` taller than the wall, capped with a sphere sitting on top. The prefab origin
// sits on the waterline (LevelSpawner aligns it there), so the top face stands at
// +(wallHeight + extraHeight) above the water and the bottom drops -depthBelowWater below it.
//
// Requires two children (resolved by name or explicit ref):
//   • Column — receives the generated box mesh + MeshCollider.
//   • Cap    — a sphere (its own mesh/renderer); this script positions it at the column top
//              and scales it to sphereDiameter.
[DisallowMultipleComponent]
public class ProceduralSplineNode : MonoBehaviour
{
    [Header("Column dimensions")]
    [Tooltip("Fallback spline-wall thickness in world units, for editor preview / standalone use. At runtime the Grid Designer's per-path Thick value is authoritative and overrides this.")]
    [SerializeField] float wallThickness = 0.09f;
    [Tooltip("Fallback size scale for editor preview / standalone use. At runtime the Grid Designer's per-path Node × value is authoritative and overrides this.")]
    [SerializeField] float sizeScale = 1.1f;
    [Tooltip("Diameter of the sphere cap; the Cap child is uniformly scaled to this. Set 0 to keep the Cap's authored scale (e.g. when Cap holds its own pre-sized children).")]
    [SerializeField] float sphereDiameter = 0.22f;
    [Tooltip("Extra vertical offset for the sphere cap, added on top of the column height (world units). Positive raises it, negative sinks it into the column.")]
    [SerializeField] float sphereVerticalOffset = 0f;

    [Header("Child object names (auto-resolved by name if unassigned)")]
    [SerializeField] string columnChildName = "Column";
    [SerializeField] string capChildName = "Cap";

    [Header("Optional explicit references (override name lookup)")]
    [SerializeField] GameObject columnChild;
    [SerializeField] GameObject capChild;

    [Header("Editor preview dimensions (world units)")]
    [SerializeField] float previewWallHeight = 1f;
    [SerializeField] float previewDepth      = 7f;

    // wallHeight = the spline wall's height at this node (from the designer). The column is
    // scale times the wall's height and thickness (e.g. 1.1 = 10% bigger). scaleOverride > 0
    // comes from the designer's per-path Node × value and wins; otherwise the serialized
    // sizeScale is used (editor preview / standalone).
    // thicknessOverride > 0 likewise comes from the designer's per-path Thick value.
    public void Build(float wallHeight, float depthBelowWater, float scaleOverride = -1f, float thicknessOverride = -1f)
    {
        float scale = scaleOverride > 0f ? scaleOverride : sizeScale;
        float baseTh = thicknessOverride > 0f ? thicknessOverride : wallThickness;
        float top   = wallHeight * scale;
        float th    = baseTh * scale;

        Mesh mesh = ProceduralBoxMesh.Build(th, th, top, depthBelowWater);
        mesh.name = "ProceduralSplineNode";
        ApplyMesh(ResolveChild(columnChild, columnChildName), mesh);

        // Sit the sphere on top of the column and size it.
        GameObject cap = ResolveChild(capChild, capChildName);
        if (cap != null)
        {
            cap.transform.localPosition = new Vector3(0f, top + sphereVerticalOffset, 0f);
            // Leave the authored scale alone when diameter is 0 (Cap manages its own children's size).
            if (sphereDiameter > 0f)
                cap.transform.localScale = Vector3.one * sphereDiameter;
        }
    }

#if UNITY_EDITOR
    // Lets the prefab be previewed with a mesh in the editor without entering play mode.
    [ContextMenu("Rebuild Preview")]
    void RebuildPreview() => Build(previewWallHeight, previewDepth);
#endif

    GameObject ResolveChild(GameObject explicitRef, string childName)
    {
        if (explicitRef != null) return explicitRef;
        if (string.IsNullOrEmpty(childName)) return null;
        Transform t = transform.Find(childName);
        return t != null ? t.gameObject : null;
    }

    static void ApplyMesh(GameObject go, Mesh mesh)
    {
        if (go == null) return;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var col = go.GetComponent<MeshCollider>();
        if (col != null) col.sharedMesh = mesh;
    }
}
