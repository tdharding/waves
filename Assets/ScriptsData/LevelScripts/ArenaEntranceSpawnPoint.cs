using UnityEngine;

/// <summary>
/// Marker placed as a child of an entrance door prefab.
/// World XZ position = boat spawn point.
/// Transform forward (blue arrow in scene) = direction the boat faces on spawn.
/// </summary>
public class ArenaEntranceSpawnPoint : MonoBehaviour
{
    [Header("Gizmo")]
    public Color gizmoColor  = Color.cyan;
    public float arrowLength = 2f;
    public float arrowRadius = 0.25f;

    public float FacingAngleY => transform.eulerAngles.y;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;

        Vector3 origin  = transform.position;
        Vector3 forward = transform.forward;
        Vector3 tip     = origin + forward * arrowLength;

        // Shaft
        Gizmos.DrawLine(origin, tip);

        // Arrowhead — two short lines splayed back from the tip
        Vector3 right = transform.right;
        Gizmos.DrawLine(tip, tip - forward * (arrowLength * 0.3f) + right  * arrowRadius);
        Gizmos.DrawLine(tip, tip - forward * (arrowLength * 0.3f) - right  * arrowRadius);

        // Disc at origin so it's easy to spot
        UnityEditor.Handles.color = gizmoColor;
        UnityEditor.Handles.DrawWireDisc(origin, Vector3.up, arrowRadius);
    }
#endif
}
