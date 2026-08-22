using UnityEngine;

// Generates the box mesh for a ProceduralCubeBuildingBlocks prefab at spawn time.
// The Grid Designer stores each block's dimensions on GridData.CubeBuilding; LevelSpawner
// instantiates this prefab and calls Build() with world-space dimensions.
//
// The mesh is built around the prefab origin so that:
//   • the top face sits at +heightAboveWater (above the waterline / PrefabBaselineAlignment disc),
//   • the bottom face sits at -depthBelowWater (dropping beneath the surface so it looks bottomless).
// The same mesh is assigned to two child renderers — the visible surface and the warning-line
// overlay — plus the visible child's MeshCollider.
[DisallowMultipleComponent]
public class ProceduralCubeBuilding : MonoBehaviour
{
    [Header("Child object names (auto-resolved by name if unassigned)")]
    [SerializeField] string visibleChildName = "MeshVisible";
    [SerializeField] string warningLinesChildName = "WarningCollisionLines";

    [Header("Optional explicit references (override name lookup)")]
    [SerializeField] GameObject visibleChild;
    [SerializeField] GameObject warningLinesChild;

    [Header("Editor preview dimensions (world units)")]
    [SerializeField] float previewWidth  = 3f;
    [SerializeField] float previewLength  = 3f;
    [SerializeField] float previewHeight  = 2f;
    [SerializeField] float previewDepth   = 5f;

    // Builds the box from world-space dimensions. width/length are the full footprint
    // extents (not half-extents). heightAboveWater / depthBelowWater are measured from
    // the prefab origin (which LevelSpawner aligns to the waterline).
    public void Build(float width, float length, float heightAboveWater, float depthBelowWater)
    {
        Mesh mesh = ProceduralBoxMesh.Build(width, length, heightAboveWater, depthBelowWater);
        mesh.name = "ProceduralCubeBuilding";

        ApplyMesh(ResolveChild(visibleChild, visibleChildName), mesh, assignCollider: true);
        ApplyMesh(ResolveChild(warningLinesChild, warningLinesChildName), mesh, assignCollider: false);
    }

    // Same as Build, but generates a stepped-rooftop mesh from a preset config + seed
    // (SteppedBuildingMesh). Used by LevelSpawner when a block has its Stepped Top flag set.
    public void BuildStepped(float width, float length, float heightAboveWater, float depthBelowWater,
                             SteppedBuildingConfig cfg, int seed)
    {
        Mesh mesh = SteppedBuildingMesh.Build(width, length, heightAboveWater, depthBelowWater, cfg, seed);
        mesh.name = "ProceduralSteppedBuilding";

        ApplyMesh(ResolveChild(visibleChild, visibleChildName), mesh, assignCollider: true);
        ApplyMesh(ResolveChild(warningLinesChild, warningLinesChildName), mesh, assignCollider: false);
    }

#if UNITY_EDITOR
    // Lets the prefab be previewed with a mesh in the editor without entering play mode.
    [ContextMenu("Rebuild Preview")]
    void RebuildPreview() => Build(previewWidth, previewLength, previewHeight, previewDepth);
#endif

    static readonly int WindowAtlasID     = Shader.PropertyToID("_WindowAtlas");
    static readonly int WindowCellSizeID  = Shader.PropertyToID("_WindowCellSize");
    static readonly int WindowAtlasGridID = Shader.PropertyToID("_WindowAtlasGrid");

    // Overrides this building's window sheet / cell size / grid on the visible renderer via a
    // MaterialPropertyBlock, so different buildings show different windows off one shared
    // material. Leaves every other property to the material. Called by LevelSpawner with a
    // random WindowFieldPreset from the pool.
    public void ApplyWindowField(Texture fieldTexture, float cellSize, Vector2 gridDims)
    {
        var go = ResolveChild(visibleChild, visibleChildName);
        if (go == null) return;
        var r = go.GetComponent<MeshRenderer>();
        if (r == null) return;

        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        if (fieldTexture != null) mpb.SetTexture(WindowAtlasID, fieldTexture);
        mpb.SetFloat(WindowCellSizeID, cellSize);
        mpb.SetVector(WindowAtlasGridID, new Vector4(gridDims.x, gridDims.y, 0f, 0f));
        r.SetPropertyBlock(mpb);
    }

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
