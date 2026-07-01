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

    public bool UseForwardOverride => useForwardOverride;
    public bool UseUpOverride      => useUpOverride;
    public Vector3 LocalForward    => useForwardOverride ? Quaternion.Euler(forwardEuler) * Vector3.forward : Vector3.forward;
    public Vector3 LocalUp         => useUpOverride      ? Quaternion.Euler(upEuler)      * Vector3.up      : Vector3.up;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
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
