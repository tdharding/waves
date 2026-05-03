using UnityEngine;

public class SoulWhirlDirection : MonoBehaviour
{
    [Header("Sector Shape (Top-Down)")]
    public float radius = 5f;

    [Range(0f, 180f)]
    public float halfAngle = 30f;

    [Header("Forward Offset (Local Y Rotation)")]
    public float forwardYawOffset = 0f;

    [Header("Direction Source")]
    [Tooltip("Optional explicit direction source (overrides tag lookup)")]
    public Transform directionSource;

    [Tooltip("Tag used to find yaw-only direction source at runtime")]
    public string directionSourceTag = "Boat";

    [Header("Debug")]
    public int arcResolution = 24;

    Transform resolvedSource;

    // --------------------------------------------------
    // UNITY
    // --------------------------------------------------

    void Awake()
    {
        ResolveDirectionSource();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ResolveDirectionSource();
    }
#endif

    // --------------------------------------------------
    // PUBLIC QUERY
    // --------------------------------------------------

    public bool IsInSector(Vector3 worldPosition)
    {
        Vector3 origin = transform.position;

        Vector3 toTarget = worldPosition - origin;
        toTarget.y = 0f;

        float sqrDist = toTarget.sqrMagnitude;
        if (sqrDist > radius * radius)
            return false;

        Vector3 forward = GetForwardXZ();
        if (forward.sqrMagnitude < 0.001f)
            return false;

        toTarget.Normalize();

        float dot = Vector3.Dot(forward, toTarget);
        float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;

        return angle <= halfAngle;
    }

    // --------------------------------------------------
    // INTERNAL
    // --------------------------------------------------

    public void ResolveDirectionSource()
    {
        if (directionSource)
        {
            resolvedSource = directionSource;
            return;
        }

        if (!string.IsNullOrEmpty(directionSourceTag))
        {
            GameObject go = GameObject.FindGameObjectWithTag(directionSourceTag);
            if (go)
            {
                resolvedSource = go.transform;
                return;
            }
        }

        resolvedSource = transform;
    }

    Vector3 GetForwardXZ()
    {
        if (!resolvedSource)
            ResolveDirectionSource();

        Quaternion yawOffset =
            Quaternion.Euler(0f, forwardYawOffset, 0f);

        Vector3 forward = yawOffset * resolvedSource.forward;
        forward.y = 0f;

        return forward.normalized;
    }

#if UNITY_EDITOR
    // --------------------------------------------------
    // DEBUG VISUAL
    // --------------------------------------------------

    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        Vector3 forward = GetForwardXZ();

        if (forward.sqrMagnitude < 0.001f)
            return;

        Quaternion leftRot  = Quaternion.AngleAxis(-halfAngle, Vector3.up);
        Quaternion rightRot = Quaternion.AngleAxis(halfAngle, Vector3.up);

        Vector3 leftDir  = leftRot * forward;
        Vector3 rightDir = rightRot * forward;

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, forward * radius);
        Gizmos.DrawRay(origin, leftDir * radius);
        Gizmos.DrawRay(origin, rightDir * radius);

        Vector3 prev = origin + leftDir * radius;

        for (int i = 1; i <= arcResolution; i++)
        {
            float t = i / (float)arcResolution;
            float angle = Mathf.Lerp(-halfAngle, halfAngle, t);

            Vector3 dir =
                Quaternion.AngleAxis(angle, Vector3.up) * forward;

            Vector3 next = origin + dir * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}
