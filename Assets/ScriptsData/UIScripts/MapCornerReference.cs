using UnityEngine;

public class MapCornerReference : MonoBehaviour
{
    [Header("Corners — match to arena grid orientation")]
    public Transform topLeft;
    public Transform topRight;
    public Transform bottomLeft;
    public Transform bottomRight;

    [Header("Scaling")]
    [Range(0f, 2f)]
    public float scalingOffset = 1f; // 1 = normal, <1 = smaller, >1 = larger

    public Vector3 GetWorldPosition(float nx, float nz)
    {
        Vector3 bottom = Vector3.Lerp(bottomLeft.position, bottomRight.position, nx);
        Vector3 top    = Vector3.Lerp(topLeft.position,    topRight.position,    nx);
        Vector3 point  = Vector3.Lerp(bottom, top, nz);

        // Scale around the center of the four corners
        Vector3 center = (topLeft.position + topRight.position + 
                          bottomLeft.position + bottomRight.position) * 0.25f;

        return Vector3.LerpUnclamped(center, point, scalingOffset);
    }

    void OnDestroy()
    {
        MapProjection.Reset();
    }

    void OnDrawGizmos()
    {
        if (topLeft)     { Gizmos.color = Color.green;  Gizmos.DrawSphere(topLeft.position,     0.02f); }
        if (topRight)    { Gizmos.color = Color.green;  Gizmos.DrawSphere(topRight.position,    0.02f); }
        if (bottomLeft)  { Gizmos.color = Color.yellow; Gizmos.DrawSphere(bottomLeft.position,  0.02f); }
        if (bottomRight) { Gizmos.color = Color.yellow; Gizmos.DrawSphere(bottomRight.position, 0.02f); }

        if (topLeft && topRight && bottomLeft && bottomRight)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(topLeft.position,    topRight.position);
            Gizmos.DrawLine(bottomLeft.position, bottomRight.position);
            Gizmos.DrawLine(topLeft.position,    bottomLeft.position);
            Gizmos.DrawLine(topRight.position,   bottomRight.position);
        }
    }
}