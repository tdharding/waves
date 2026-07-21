using UnityEngine;
using UnityEngine.Splines;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Wall-mounted water-level exit pipe. Placed around the arena perimeter by the Grid Designer
/// (like a locked entrance / DoorLockHub) and fed by a <see cref="SoulFishInputTube"/> that joins
/// to <see cref="splineReceiver"/> — the exact mechanism the lock hub uses via its pipeConnector.
///
/// When a soul completes its trip down the tube, <see cref="OnSoulArrived"/> fires: the LID child
/// opens (Animator trigger) and the arena water lowers to <see cref="targetWaterLevelY"/> using the
/// shared <see cref="WaveLevelTween"/> so every water-height target stays in sync.
///
/// All references are wired in the prefab — no auto-find.
/// </summary>
public class WaterLevelExitPipeController : MonoBehaviour
{
    [Header("Tube Connection")]
    [Tooltip("The pipe's SPLINE receiver. The input tube joins to this, mirroring DoorLockHubController.pipeConnector.")]
    public SplineContainer splineReceiver;

    [Header("Lid Animation")]
    [SerializeField] private Animator lidAnimator;
    [SerializeField] private string   lidOpenTrigger = "Open";

    [Header("Water Level")]
    [Tooltip("The pipe's waterline reference — its PrefabBaselineAlignment 'aligner' child. The aligner disc marks " +
             "where the arena baseline water sits (the spawner aligns it to the arena BaselineMarker), so the target " +
             "level is measured relative to it. Wire the aligner child here.")]
    [SerializeField] private PrefabBaselineAlignment aligner;
    [Tooltip("Target water level authored in the prefab's local space (same frame as the aligner disc). At runtime " +
             "the water lowers to the arena baseline offset by the distance between this level and the aligner disc.")]
    [SerializeField] private float targetWaterLevelY = 0f;
    [SerializeField] private float transitionDuration = 3f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    [Tooltip("Draw a horizontal plane gizmo at the target water level so you can line it up in-scene.")]
    [SerializeField] private bool  showLevelGizmo   = true;
    [Tooltip("Radius of the target-level disc gizmo.")]
    [SerializeField] private float levelGizmoRadius = 6f;

    // Read by the Grid Designer to show the 'lowers to' reference on a placement.
    public float TargetWaterLevelY => targetWaterLevelY;

    // Aligner disc height in the prefab's local (root) space.
    private float AlignerLocalY =>
        aligner != null ? transform.InverseTransformPoint(aligner.transform.position).y : 0f;

    // World units the water drops below the aligner/baseline. >0 = target sits below the disc.
    public float DropBelowAligner => AlignerLocalY - targetWaterLevelY;

    // World Y the water lowers to.
    //   baseline (where the aligner disc sits) − distance the target sits below that disc.
    // The spawner aligns the aligner to the arena BaselineMarker, so aligner.position.y IS the
    // arena baseline (and stays fixed — the pipe never moves when the water plane does). This is
    // equivalent to (spawn-aligned root Y + targetWaterLevelY) but expressed via the aligner + baseline
    // so it is correct regardless of the pipe's own Y, and tier-aware (the aligner reflects its tier).
    public float ComputeTargetWorldY()
    {
        if (aligner == null) return transform.position.y + targetWaterLevelY; // fallback: root-relative
        return aligner.transform.position.y - DropBelowAligner;
    }

    private bool hasTriggered;

    /// <summary>
    /// Called by SoulFishInputTube when the delivery fish reaches the pipe (mirrors DoorLockHubController.OnSoulArrived).
    /// Idempotent — a second arrival is ignored.
    /// </summary>
    public void OnSoulArrived()
    {
        if (hasTriggered) return;
        hasTriggered = true;

        if (lidAnimator != null && !string.IsNullOrEmpty(lidOpenTrigger))
            lidAnimator.SetTrigger(lidOpenTrigger);

        float worldTargetY = ComputeTargetWorldY();
        StartCoroutine(WaveLevelTween.To(worldTargetY, transitionDuration));

        if (debugLog)
            Debug.Log($"[WaterLevelExitPipe] Soul arrived — lid opening, water lowering to worldY={worldTargetY:0.###} " +
                      $"(baseline={(aligner != null ? aligner.transform.position.y : transform.position.y):0.###}, drop={DropBelowAligner:0.###}).");
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showLevelGizmo) return;

        // Horizontal plane at the absolute target water Y, centred on the pipe's XZ.
        Vector3 levelPos = new Vector3(transform.position.x, targetWaterLevelY, transform.position.z);

        Handles.color = new Color(0.2f, 0.6f, 1f, 0.12f);
        Handles.DrawSolidDisc(levelPos, Vector3.up, levelGizmoRadius);
        Handles.color = new Color(0.2f, 0.6f, 1f, 0.9f);
        Handles.DrawWireDisc(levelPos, Vector3.up, levelGizmoRadius);

        // Vertical connector from the pipe down/up to the target level.
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.7f);
        Gizmos.DrawLine(transform.position, levelPos);

        // Small centre cross on the plane.
        Gizmos.DrawLine(levelPos - Vector3.right   * 1f, levelPos + Vector3.right   * 1f);
        Gizmos.DrawLine(levelPos - Vector3.forward * 1f, levelPos + Vector3.forward * 1f);

        Handles.color = new Color(0.2f, 0.6f, 1f, 0.95f);
        Handles.Label(levelPos + Vector3.up * 0.25f, $"Water Level  y={targetWaterLevelY:0.##}");
    }
#endif
}
