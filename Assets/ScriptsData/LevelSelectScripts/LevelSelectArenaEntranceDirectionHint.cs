using UnityEngine;

public class LevelSelectArenaEntranceDirectionHint : MonoBehaviour
{
    [SerializeField] private Color gizmoColor  = new Color(1f, 0.5f, 0.1f, 1f);
    [SerializeField] private float arrowLength = 1.5f;

    /// <summary>World-space entrance direction — read by the designer or boat controller.</summary>
    public Vector3 Direction => transform.forward;

    private void OnDrawGizmos()
    {
        DrawArrow(gizmoColor);
    }

    private void OnDrawGizmosSelected()
    {
        DrawArrow(Color.white);
    }

    private void DrawArrow(Color color)
    {
        Gizmos.color = color;

        Vector3 origin  = transform.position;
        Vector3 forward = transform.forward;
        Vector3 tip     = origin + forward * arrowLength;
        float   head    = arrowLength * 0.25f;

        Gizmos.DrawLine(origin, tip);

        Vector3 right = transform.right;
        Vector3 up    = transform.up;
        Vector3 back  = tip - forward * head;

        Gizmos.DrawLine(tip, back + right *  head * 0.5f);
        Gizmos.DrawLine(tip, back - right *  head * 0.5f);
        Gizmos.DrawLine(tip, back + up    *  head * 0.5f);
        Gizmos.DrawLine(tip, back - up    *  head * 0.5f);
    }
}
