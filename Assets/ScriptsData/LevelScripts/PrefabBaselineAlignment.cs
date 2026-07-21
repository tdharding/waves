using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Place on a child transform of the prefab root, at the height where the water line should meet the object.
// Move this transform up/down in the prefab to position the waterline disc on the object.
// At spawn, LevelSpawner offsets the prefab so this disc aligns with the BaselineMarker disc.
//
// Optional direction overrides: define the facing and up directions for this prefab at spawn.
// The forward arrow represents the direction this prefab faces when perimeter angle = 0.
// LevelSpawner rotates this by the perimeter angle to place it correctly around the arena.
public class PrefabBaselineAlignment : MonoBehaviour
{
    [SerializeField] bool showDebug = false;

    [Header("Spawn Direction Overrides")]
    [SerializeField] bool useForwardOverride = false;
    [Tooltip("Euler angles applied to Vector3.forward to define this prefab's facing direction at perimeter angle 0.")]
    [SerializeField] Vector3 forwardEuler = Vector3.zero;

    [SerializeField] bool useUpOverride = false;
    [Tooltip("Euler angles applied to Vector3.up to define this prefab's up direction at spawn.")]
    [SerializeField] Vector3 upEuler = Vector3.zero;

    [Header("Wall Intersection")]
    [Tooltip("For wall-based prefabs (doors/locks/pipes). When enabled, LevelSpawner offsets the spawned " +
             "instance along its forward axis by Wall Depth, deciding where the prefab intersects the arena radius.")]
    [SerializeField] bool useWallDepth = false;
    [Tooltip("World-units to shift the prefab along its forward direction at spawn. Positive = toward arena centre, " +
             "negative = out into the wall.")]
    [SerializeField] float wallDepth = 0f;

    [Header("Designer Scale Radius")]
    [Tooltip("When enabled, the Grid Designer draws a proportional footprint ring for this prefab and lets you scale placements up/down.")]
    [SerializeField] bool useScaleRadius = false;
    [Tooltip("World-space radius of this object's footprint at scale 1. The Grid Designer proportions the placement against the arena baseline using this value.")]
    [SerializeField] float scaleRadius = 3f;

    [Header("Prefab Top Marker")]
    [Tooltip("When enabled, marks the top of this object (above the waterline disc). Used by the Map UI to imply height (taller objects lift + draw on top) and available for other systems. Does not affect spawning.")]
    [SerializeField] bool  usePrefabTopMarker = false;
    [Tooltip("World-units from this waterline disc up to the top of the object.")]
    [SerializeField] float prefabTopHeight = 3f;

    public bool UseForwardOverride => useForwardOverride;
    public bool UseUpOverride      => useUpOverride;
    public Vector3 LocalForward    => useForwardOverride ? Quaternion.Euler(forwardEuler) * Vector3.forward : Vector3.forward;
    public Vector3 LocalUp         => useUpOverride      ? Quaternion.Euler(upEuler)      * Vector3.up      : Vector3.up;

    public bool  UseScaleRadius => useScaleRadius;
    public float ScaleRadius    => scaleRadius;

    public bool  UseWallDepth => useWallDepth;
    public float WallDepth    => wallDepth;

    // Prefab top marker. Height is world-units above the waterline disc; 0 when disabled.
    public bool    UsePrefabTopMarker => usePrefabTopMarker;
    public float   PrefabTopHeight    => usePrefabTopMarker ? Mathf.Max(0f, prefabTopHeight) : 0f;
    public Vector3 PrefabTopWorld     => transform.position + transform.up * PrefabTopHeight;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        // Scale-radius footprint ring — drawn independently of showDebug so the
        // designer footprint is always visible when the feature is enabled.
        if (useScaleRadius && scaleRadius > 0f)
        {
            Handles.color = new Color(1f, 0.55f, 0.1f, 0.85f);
            Handles.DrawWireDisc(transform.position, Vector3.up, scaleRadius);
            Handles.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Handles.Label(transform.position + Vector3.right * scaleRadius, $"scale r={scaleRadius:0.##}");
        }

        // Wall-depth offset — drawn independently of showDebug so the intersection point is visible while authoring.
        if (useWallDepth && Mathf.Abs(wallDepth) > 0.0001f)
        {
            Vector3 fwd = transform.TransformDirection(LocalForward).normalized;
            Vector3 from = transform.position;
            Vector3 to   = from + fwd * wallDepth;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.DrawLine(from, to);
            Handles.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Handles.DrawWireDisc(to, fwd, 0.5f);
            Handles.Label(to + Vector3.up * 0.2f, $"wall depth={wallDepth:0.##}");
        }

        // Prefab top marker — drawn independently of showDebug so it's visible while authoring.
        if (usePrefabTopMarker && prefabTopHeight > 0f)
        {
            Vector3 basePos = transform.position;
            Vector3 topPos  = basePos + transform.up * prefabTopHeight;
            Gizmos.color = new Color(0.7f, 0.4f, 1f, 0.9f);
            Gizmos.DrawLine(basePos, topPos);
            Handles.color = new Color(0.7f, 0.4f, 1f, 0.9f);
            Handles.DrawWireDisc(topPos, transform.up, 0.6f);
            Handles.Label(topPos + transform.up * 0.2f, $"top h={prefabTopHeight:0.##}");
        }

        if (!showDebug) return;

        Vector3 pos = transform.position;

        // Green disc — waterline
        Handles.color = new Color(0f, 0.9f, 0.5f, 0.6f);
        Handles.DrawSolidDisc(pos, Vector3.up, 1.5f);
        Handles.color = new Color(0f, 0.9f, 0.5f, 0.85f);
        Handles.Label(pos + Vector3.up * 0.2f, "WATERLINE");

        // Blue arrow — forward override
        if (useForwardOverride)
        {
            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.9f);
            BaselineMarker.DrawArrow(pos, LocalForward, 2f);
            Handles.color = new Color(0.2f, 0.5f, 1f, 0.85f);
            Handles.Label(pos + LocalForward * 2.2f, "FORWARD");
        }

        // Yellow-green arrow — up override
        if (useUpOverride)
        {
            Gizmos.color = new Color(0.6f, 1f, 0.2f, 0.9f);
            BaselineMarker.DrawArrow(pos, LocalUp, 1.5f);
            Handles.color = new Color(0.6f, 1f, 0.2f, 0.85f);
            Handles.Label(pos + LocalUp * 1.7f, "UP");
        }
    }
#endif
}
