using UnityEngine;

public class JunctionBranchDirectionHint : MonoBehaviour
{
    [SerializeField] private Color  gizmoColor   = new Color(0f, 1f, 0.5f, 1f);
    [SerializeField] private float  arrowLength  = 1.5f;

    /// <summary>World-space branch exit direction — read by the designer during generation.</summary>
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

        Vector3 origin   = transform.position;
        Vector3 forward  = transform.forward;
        Vector3 tip      = origin + forward * arrowLength;
        float   headSize = arrowLength * 0.25f;

        Gizmos.DrawLine(origin, tip);

        Vector3 right = transform.right;
        Vector3 back  = tip - forward * headSize;
        Gizmos.DrawLine(tip, back + right *  headSize * 0.5f);
        Gizmos.DrawLine(tip, back - right *  headSize * 0.5f);
        Gizmos.DrawLine(tip, back + transform.up * headSize * 0.5f);
        Gizmos.DrawLine(tip, back - transform.up * headSize * 0.5f);
    }
}
