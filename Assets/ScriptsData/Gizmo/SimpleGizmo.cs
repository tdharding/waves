using UnityEngine;

public class SimpleGizmo : MonoBehaviour
{
    public Color gizmoColor = Color.yellow;
    public float gizmoSize = 0.2f;

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, gizmoSize);
    }
}
