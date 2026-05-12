using UnityEngine;

public class LureController : MonoBehaviour
{
    [Header("Lure")]
    public GameObject lurePrefab;
    public int        maxLures          = 1;

    [Header("Timing")]
    public float duration               = 10f;

    [Header("Sink")]
    public float sinkSpeed              = 2f;
    public float sinkDistance           = 4f;
    public float finalSinkDistance      = 4f;

    [Header("Attraction")]
    public float attractionRadius       = 5f;

    [Header("Fish Orbit")]
    public float orbitRadius            = 1.2f;
    public float orbitSpeed             = 1.5f;
    public float moveTowardSpeed        = 3f;

    [Header("Drop Point")]
    [Tooltip("Where the lure spawns. Defaults to this transform if unassigned.")]
    public Transform dropPoint;

    [Header("Debug")]
    public bool showGizmoPreview = true;

    private int _activeLureCount = 0;

    void OnEnable()
    {
        _activeLureCount = 0;
    }

    public bool CanDrop => true;

    public void DropLure()
    {
        if (!CanDrop || lurePrefab == null) return;

        Transform spawnFrom = dropPoint != null ? dropPoint : transform;
        GameObject lureGO   = Instantiate(lurePrefab, spawnFrom.position, spawnFrom.rotation);

        var behaviour = lureGO.GetComponent<LureBehaviour>();
        if (behaviour != null)
        {
            behaviour.duration          = duration;
            behaviour.sinkSpeed         = sinkSpeed;
            behaviour.sinkDistance      = sinkDistance;
            behaviour.finalSinkDistance = finalSinkDistance;
            behaviour.attractionRadius  = attractionRadius;
            behaviour.orbitRadius       = orbitRadius;
            behaviour.orbitSpeed        = orbitSpeed;
            behaviour.moveTowardSpeed   = moveTowardSpeed;
            behaviour.OnLureExpired    += OnLureExpired;
        }

        _activeLureCount++;
        Debug.Log($"[LureController] Lure dropped at {spawnFrom.position}. Active: {_activeLureCount}/{maxLures}");
    }

    void OnLureExpired()
    {
        _activeLureCount = Mathf.Max(0, _activeLureCount - 1);
        Debug.Log($"[LureController] Lure expired. Active: {_activeLureCount}/{maxLures}");
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showGizmoPreview) return;
        Transform spawnFrom = dropPoint != null ? dropPoint : transform;
        Vector3 down = -spawnFrom.up;
        Vector3 up   =  spawnFrom.up;

        // Attraction radius circle
        UnityEditor.Handles.color = new Color(1f, 0.6f, 0f, 0.6f);
        UnityEditor.Handles.DrawWireDisc(spawnFrom.position, up, attractionRadius);

        // Drop arrow
        Vector3 dropTip    = spawnFrom.position + down * 0.6f;
        Vector3 arrowLeft  = dropTip + up * 0.15f + spawnFrom.right *  0.15f;
        Vector3 arrowRight = dropTip + up * 0.15f + spawnFrom.right * -0.15f;
        UnityEditor.Handles.color = new Color(0f, 1f, 0.5f, 0.9f);
        UnityEditor.Handles.DrawLine(spawnFrom.position, dropTip);
        UnityEditor.Handles.DrawLine(dropTip, arrowLeft);
        UnityEditor.Handles.DrawLine(dropTip, arrowRight);

        // Initial sink point + orbit radius
        Vector3 sinkEnd = spawnFrom.position + down * sinkDistance;
        UnityEditor.Handles.color = new Color(1f, 0.4f, 0f, 0.9f);
        UnityEditor.Handles.DrawLine(spawnFrom.position, sinkEnd);
        UnityEditor.Handles.DrawWireDisc(sinkEnd, up, 0.2f);
        UnityEditor.Handles.Label(sinkEnd + down * 0.1f, $"Active depth ({sinkDistance}m)");

        // Orbit radius at active depth
        UnityEditor.Handles.color = new Color(0.4f, 0.8f, 1f, 0.7f);
        UnityEditor.Handles.DrawWireDisc(sinkEnd, up, orbitRadius);

        // Orbit speed — animated dot travelling around orbit circle
        float orbitT = (float)(UnityEditor.EditorApplication.timeSinceStartup * orbitSpeed);
        Vector3 orbitDot = sinkEnd
                         + spawnFrom.right   * Mathf.Cos(orbitT) * orbitRadius
                         + spawnFrom.forward * Mathf.Sin(orbitT) * orbitRadius;
        UnityEditor.Handles.color = new Color(0.4f, 0.8f, 1f, 1f);
        UnityEditor.Handles.DrawSolidDisc(orbitDot, up, 0.15f);

        // Sink speed — dot dropping from spawn to active depth, looping
        float dropDuration = sinkSpeed > 0f ? sinkDistance / sinkSpeed : 1f;
        float dropFraction = (float)(UnityEditor.EditorApplication.timeSinceStartup % dropDuration) / dropDuration;
        Vector3 sinkDot = spawnFrom.position + down * (dropFraction * sinkDistance);
        UnityEditor.Handles.color = new Color(0f, 1f, 0.5f, 1f);
        UnityEditor.Handles.DrawSolidDisc(sinkDot, up, 0.12f);

        // Final sink speed — dot dropping from active depth to delete point, looping
        float finalDuration = sinkSpeed > 0f ? finalSinkDistance / sinkSpeed : 1f;
        float finalFraction = (float)(UnityEditor.EditorApplication.timeSinceStartup % finalDuration) / finalDuration;
        Vector3 finalSinkDot = sinkEnd + down * (finalFraction * finalSinkDistance);
        UnityEditor.Handles.color = new Color(1f, 0.2f, 0.2f, 1f);
        UnityEditor.Handles.DrawSolidDisc(finalSinkDot, up, 0.12f);

        // Move toward speed — dot travelling from attraction radius edge to orbit radius, looping
        float approachDist     = Mathf.Max(0f, attractionRadius - orbitRadius);
        float approachDuration = moveTowardSpeed > 0f ? approachDist / moveTowardSpeed : 1f;
        float approachFraction = approachDuration > 0f
            ? (float)(UnityEditor.EditorApplication.timeSinceStartup % approachDuration) / approachDuration
            : 1f;
        float approachRadius   = Mathf.Lerp(attractionRadius, orbitRadius, approachFraction);
        Vector3 approachDot    = sinkEnd + spawnFrom.right * approachRadius;
        UnityEditor.Handles.color = new Color(1f, 1f, 0f, 1f);
        UnityEditor.Handles.DrawSolidDisc(approachDot, up, 0.12f);

        UnityEditor.SceneView.RepaintAll();

        // Final sink point
        Vector3 finalSinkEnd = sinkEnd + down * finalSinkDistance;
        UnityEditor.Handles.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        UnityEditor.Handles.DrawLine(sinkEnd, finalSinkEnd);
        UnityEditor.Handles.DrawWireDisc(finalSinkEnd, up, 0.15f);
        UnityEditor.Handles.Label(finalSinkEnd + down * 0.1f, $"Delete ({finalSinkDistance}m)");
    }
#endif
}
