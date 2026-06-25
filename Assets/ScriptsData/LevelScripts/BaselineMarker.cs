using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Place on a child of outerWallsPrefab.
// Defines the world-space Y and forward direction used when spawning baseline-aligned prefabs.
public class BaselineMarker : MonoBehaviour
{
    public float height     = 0f;
    public float discRadius = 12f;

    [Header("Arena Mask Settings")]
    [Range(0f, 1f)] public float maskFadeStartPct = 0.7f;
    [Range(0f, 1f)] public float maskFadeEndPct   = 0.9f;

    public float MaskFadeStartRadius => discRadius * maskFadeStartPct;
    public float MaskFadeEndRadius   => discRadius * maskFadeEndPct;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Vector3 centre = new Vector3(transform.position.x, height, transform.position.z);

        // Main Arena Radius
        Handles.color = new Color(0f, 0.9f, 0.5f, 0.6f);
        Handles.DrawWireDisc(centre, Vector3.up, discRadius);

        // Mask Fade Start (Percentage-based)
        Handles.color = new Color(1f, 0f, 1f, 0.5f); // Magenta
        Handles.DrawWireDisc(centre, Vector3.up, MaskFadeStartRadius);

        // Mask Fade End (Percentage-based)
        Handles.color = new Color(0f, 1f, 1f, 0.5f); // Cyan
        Handles.DrawWireDisc(centre, Vector3.up, MaskFadeEndRadius);

        Gizmos.color = new Color(0f, 0.9f, 0.5f, 0.6f);
        Gizmos.DrawLine(centre - Vector3.right   * 1f, centre + Vector3.right   * 1f);
        Gizmos.DrawLine(centre - Vector3.forward * 1f, centre + Vector3.forward * 1f);

        Gizmos.color = new Color(0f, 0.9f, 0.5f, 0.9f);
        DrawArrow(centre, Vector3.up, 3f);

        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
        DrawArrow(centre, transform.forward, 4f);

        Handles.color = new Color(0f, 0.9f, 0.5f, 0.8f);
        Handles.Label(centre + Vector3.up * 3.6f, $"Baseline  y={height:F2}");
    }

    internal static void DrawArrow(Vector3 origin, Vector3 direction, float length)
    {
        Vector3 tip  = origin + direction.normalized * length;
        Gizmos.DrawLine(origin, tip);

        float   head  = length * 0.25f;
        Vector3 back  = -direction.normalized;
        Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.01f)
            right = Vector3.Cross(direction, Vector3.forward).normalized;
        Vector3 up2 = Vector3.Cross(right, direction).normalized;

        Gizmos.DrawLine(tip, tip + (back + right) * head * 0.5f);
        Gizmos.DrawLine(tip, tip + (back - right) * head * 0.5f);
        Gizmos.DrawLine(tip, tip + (back + up2)   * head * 0.5f);
        Gizmos.DrawLine(tip, tip + (back - up2)   * head * 0.5f);
    }
#endif
}
