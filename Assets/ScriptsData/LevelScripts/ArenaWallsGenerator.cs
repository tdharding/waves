using UnityEngine;

// Builds the arena boundary wall to whatever radius the level asks for, replacing the old
// fixed-size modelled wall prefabs (one per arena size, each with a hand-typed discRadius that
// only approximately matched its scaled mesh).
//
// The radius and waterline come from GridData via LevelSpawner, and the wall's inner face is
// built exactly on the radius. Both are written back onto the BaselineMarker, so discRadius —
// and therefore the wave and sonar arena masks, the boat mask fades and the dropped-soul clamp —
// lands precisely on the wall surface rather than near it.
//
// Put this on the arena walls prefab root alongside a BaselineMarker child (which supplies the
// waterline height the wall rises from) and two mesh children: the visible wall surface, and the
// warning-line overlay whose shader shows the boat closing on the edge. Both get the same mesh;
// only the visible one carries the collider.
[ExecuteAlways]
[DisallowMultipleComponent]
public class ArenaWallsGenerator : MonoBehaviour
{
    [Header("Boundary")]
    [Tooltip("Shape of the arena boundary. The wall's closest approach to the centre equals the arena radius either way.")]
    [SerializeField] ProceduralArenaWallMesh.Shape shape = ProceduralArenaWallMesh.Shape.Circle;

    [Header("Wall")]
    [Tooltip("World-units the top of the wall stands above the baseline waterline.")]
    [SerializeField] float wallHeight = 4f;
    [Tooltip("World-units the wall extends outward from the arena radius.")]
    [SerializeField] float wallThickness = 1f;
    [Tooltip("World-units the wall drops below the waterline so it reads as bottomless.")]
    [SerializeField] float wallDrop = 8f;

    [Header("Child object names (auto-resolved by name if unassigned)")]
    [SerializeField] string visibleChildName = "MeshVisible";
    [SerializeField] string warningLinesChildName = "WarningCollisionLines";

    [Header("Optional explicit references (override name lookup)")]
    [SerializeField] GameObject visibleChild;
    [SerializeField] GameObject warningLinesChild;
    [SerializeField] BaselineMarker baselineMarker;

    // Last values built, so an inspector tweak can rebuild without LevelSpawner re-running.
    float _builtRadius;
    float _builtWaterY;
    Mesh  _mesh;

    public float WallHeight => wallHeight;

    // Called by LevelSpawner immediately after the walls prefab is instantiated.
    public void Build(float arenaRadius, float waterY)
    {
        if (arenaRadius <= 0f)
        {
            Debug.LogWarning($"[ArenaWallsGenerator] '{name}' asked to build at radius {arenaRadius} — set Arena Radius in the Grid Designer.");
            return;
        }

        _builtRadius = arenaRadius;
        _builtWaterY = waterY;

        // The wall is built exactly on the requested radius and rises from the level's own
        // waterline, so the marker states both rather than approximating a scaled mesh.
        // Everything keyed off the arena — masks, fades, doors, grid frame, tier alignment —
        // reads back through this.
        var marker = ResolveMarker();
        if (marker != null)
        {
            marker.discRadius = arenaRadius;
            marker.height     = waterY;
        }

        ReleaseMesh();
        _mesh = ProceduralArenaWallMesh.Build(shape, arenaRadius, wallThickness, wallHeight, wallDrop);

        GameObject visible = ResolveChild(visibleChild, visibleChildName);
        GameObject warning = ResolveChild(warningLinesChild, warningLinesChildName);

        if (visible == null && warning == null)
        {
            Debug.LogWarning($"[ArenaWallsGenerator] '{name}' has no child named '{visibleChildName}' or " +
                             $"'{warningLinesChildName}' and no explicit references — wall not generated.");
            return;
        }

        // Both children take the SAME mesh: the visible wall surface, and the warning-line
        // overlay whose shader lights up as the boat closes on the edge. Only the visible one
        // carries the collider.
        ApplyMesh(visible, _mesh, waterY, assignCollider: true);
        ApplyMesh(warning, _mesh, waterY, assignCollider: false);
    }

    GameObject ResolveChild(GameObject explicitRef, string childName)
    {
        if (explicitRef != null) return explicitRef;
        if (string.IsNullOrEmpty(childName)) return null;
        Transform t = transform.Find(childName);
        return t != null ? t.gameObject : null;
    }

    // The mesh is built with the waterline at local y = 0, so both children sit on the baseline.
    static void ApplyMesh(GameObject go, Mesh mesh, float waterY, bool assignCollider)
    {
        if (go == null) return;

        Vector3 p = go.transform.position;
        go.transform.position = new Vector3(p.x, waterY, p.z);

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        if (assignCollider)
        {
            var col = go.GetComponent<MeshCollider>();
            if (col != null) col.sharedMesh = mesh;
        }
    }

    BaselineMarker ResolveMarker()
    {
        if (baselineMarker != null) return baselineMarker;
        baselineMarker = GetComponentInChildren<BaselineMarker>(true);
        return baselineMarker;
    }

    void ReleaseMesh()
    {
        if (_mesh == null) return;
        if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
        _mesh = null;
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild")]
    void Rebuild()
    {
        var marker = ResolveMarker();
        Build(_builtRadius > 0f ? _builtRadius : (marker != null ? marker.discRadius : 0f),
              _builtRadius > 0f ? _builtWaterY : (marker != null ? marker.height : 0f));
    }

    // Live-rebuild when a field is tweaked on the prefab or a spawned instance, reusing the
    // last radius. delayCall defers the mesh work out of OnValidate, which Unity forbids
    // modifying components from while a scene or asset is loading.
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || gameObject == null) return;
            Rebuild();
        };
    }

    void OnDrawGizmosSelected()
    {
        var marker = ResolveMarker();
        if (marker == null || marker.discRadius <= 0f) return;
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        Vector3 c = new Vector3(transform.position.x, marker.height, transform.position.z);
        Gizmos.DrawWireCube(c + Vector3.up * (wallHeight - wallDrop) * 0.5f,
                            new Vector3(marker.discRadius * 2f, wallHeight + wallDrop, marker.discRadius * 2f));
    }
#endif
}
