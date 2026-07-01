using System.Collections;
using UnityEngine;

/// <summary>
/// Manages the catapult tool on the boat.
/// Loading:  drag a soul icon onto the 3D SoulSlot (SoulFishLoadingPoint, Interaction layer).
/// Firing:   press Space to fire at a fixed distance.
/// </summary>
public class CatapultController : MonoBehaviour
{
    [Header("Refs")]
    public SoulSlot soulSlot;
    public Transform armTransform;
    public Transform launchPoint;
    public GameObject catapultProjectilePrefab;

    [Header("Arm Rotation")]
    [Tooltip("Local X degrees the arm swings through on fire.")]
    public float armFireRotation = 120f;
    [Tooltip("Duration of the forward swing.")]
    public float swingDuration = 0.2f;
    [Tooltip("Duration of the arm returning to rest.")]
    public float returnDuration = 0.4f;

    [Header("Fire Direction")]
    [Tooltip("Local-space direction the soul is launched. Adjust until the Scene gizmo arrow points where you want.")]
    public Vector3 fireDirectionLocal = Vector3.forward;

    [Header("Throw")]
    public float throwDistance = 12f;

    [Header("Projectile Arc")]
    public float arcDuration = 1.0f;
    public float arcPeakHeight = 5f;

    [Header("Projectile Collision")]
    public float obstacleCheckRadius = 0.45f;
    public string[] collidableTags = { "MazeWalls", "LowHMazeWall", "TallHMazeWall", "LowSpikeTrap", "BadGuySnake" };

    [Header("Debug")]
    public bool showDebugGizmos = true;

    [Header("Projectile Wave Sampling")]
    public float extraYOffset = 0.01f;
    public float heightMultiplier = 1f;

    [Header("Projectile Prefabs")]
    public GameObject droppedSoulPrefab;
    public GameObject explosionVFXPrefab;
    public GameObject splashPrefab;

    private int _loadedSoulIdentity = -1;
    private int _loadedLinkID = -1;
    private bool _firing;
    private Vector3 _armRestEulers;

    private void Awake()
    {
        soulSlot.onFilled.AddListener(OnSoulLoaded);
        soulSlot.onEmptied.AddListener(OnSoulEjected);
        if (armTransform != null)
            _armRestEulers = armTransform.localEulerAngles;
    }

    private void OnDestroy()
    {
        soulSlot.onFilled.RemoveListener(OnSoulLoaded);
        soulSlot.onEmptied.RemoveListener(OnSoulEjected);
    }

    // ─────────────────────────────────────────────
    // SOUL LOADING
    // ─────────────────────────────────────────────

    private void OnSoulLoaded(int soulIdentity)
    {
        _loadedSoulIdentity = soulIdentity;
        _loadedLinkID = -1;

        if (LevelSoulTracker.Instance != null)
        {
            _loadedLinkID = LevelSoulTracker.Instance.GetLinkIDForIdentity(soulIdentity);
            LevelSoulTracker.Instance.RemoveTemporarySoul(soulIdentity);
        }
    }

    private void OnSoulEjected(int soulIdentity)
    {
        // When the soul is returned to the boat (not fired), restore the tracker state.
        // The slot's own RemoveSoul already handles GameProgressData and the display icon.
        if (!_firing && soulIdentity >= 0 && LevelSoulTracker.Instance != null)
            LevelSoulTracker.Instance.ReinsertSoul(_loadedLinkID, soulIdentity);
    }

    // ─────────────────────────────────────────────
    // FIRE (called from BoatControlRouter on Space down)
    // ─────────────────────────────────────────────

    public void Fire()
    {
        if (!soulSlot.IsFilled || _firing) return;

        _firing = true;
        soulSlot.SetAllowRemoval(false);
        soulSlot.SetInteractable(false);
        StartCoroutine(FireRoutine());
    }

    public void CancelArm()
    {
        // No-op now — kept so BoatControlRouter compiles without changes
    }

    // ─────────────────────────────────────────────
    // FIRE COROUTINE
    // ─────────────────────────────────────────────

    private IEnumerator FireRoutine()
    {
        bool launched = false;

        for (float t = 0f; t < 1f; t += Time.deltaTime / swingDuration)
        {
            if (armTransform != null)
                armTransform.localEulerAngles = _armRestEulers + new Vector3(t * armFireRotation, 0f, 0f);

            if (!launched && t > 0.35f)
            {
                LaunchSoul();
                launched = true;
            }

            yield return null;
        }

        if (armTransform != null)
            armTransform.localEulerAngles = _armRestEulers + new Vector3(armFireRotation, 0f, 0f);

        if (!launched)
            LaunchSoul();

        yield return StartCoroutine(ReturnArmRoutine());

        _firing = false;
        soulSlot.SetAllowRemoval(true);
        soulSlot.SetInteractable(true);
    }

    private IEnumerator ReturnArmRoutine()
    {
        float startAngle = armFireRotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / returnDuration)
        {
            if (armTransform != null)
                armTransform.localEulerAngles = _armRestEulers + new Vector3(Mathf.Lerp(startAngle, 0f, t), 0f, 0f);
            yield return null;
        }
        ResetArmRotation();
    }

    private void ResetArmRotation()
    {
        if (armTransform != null)
            armTransform.localEulerAngles = _armRestEulers;
    }

    // ─────────────────────────────────────────────
    // LAUNCH
    // ─────────────────────────────────────────────

    private void LaunchSoul()
    {
        Vector3 fireDirection = transform.TransformDirection(fireDirectionLocal.normalized);
        Vector3 startPos      = launchPoint != null ? launchPoint.position : transform.position;

        if (catapultProjectilePrefab != null)
        {
            GameObject proj       = Object.Instantiate(catapultProjectilePrefab, startPos, Quaternion.identity);
            var        projectile = proj.GetComponent<CatapultProjectile>();
            if (projectile != null)
            {
                projectile.arcDuration          = arcDuration;
                projectile.arcPeakHeight        = arcPeakHeight;
                projectile.obstacleCheckRadius  = obstacleCheckRadius;
                projectile.collidableTags       = collidableTags;
                projectile.extraYOffset         = extraYOffset;
                projectile.heightMultiplier     = heightMultiplier;
                projectile.droppedSoulPrefab    = droppedSoulPrefab;
                projectile.explosionVFXPrefab   = explosionVFXPrefab;
                projectile.splashPrefab         = splashPrefab;
                projectile.Launch(startPos, fireDirection, throwDistance, _loadedSoulIdentity, _loadedLinkID);
            }
        }

        soulSlot.EjectSoul();

        _loadedSoulIdentity = -1;
        _loadedLinkID = -1;
    }

    // ─────────────────────────────────────────────
    // GIZMO
    // ─────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        Vector3 origin   = launchPoint != null ? launchPoint.position : transform.position;
        Vector3 worldDir = transform.TransformDirection(fireDirectionLocal.normalized);
        Vector3 right    = Vector3.Cross(worldDir, Vector3.up).normalized;
        Vector3 landing  = origin + worldDir * throwDistance;

        // Arc preview
        int segments = 32;
        Vector3 prev = origin;
        for (int i = 1; i <= segments; i++)
        {
            float n    = (float)i / segments;
            Vector3 flat = Vector3.Lerp(origin, landing, n);
            float arcY   = Mathf.Sin(n * Mathf.PI) * arcPeakHeight;
            Vector3 next = new Vector3(flat.x, flat.y + arcY, flat.z);
            Gizmos.color = Color.Lerp(Color.green, Color.yellow, n);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        // Peak marker
        Vector3 midFlat = Vector3.Lerp(origin, landing, 0.5f);
        Vector3 peak    = new Vector3(midFlat.x, origin.y + arcPeakHeight, midFlat.z);
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(peak, 0.15f);

        // Landing marker + arrowhead
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(landing, 0.2f);
        float headSize = throwDistance * 0.06f;
        Gizmos.DrawLine(landing, landing - worldDir * headSize + right * headSize * 0.5f);
        Gizmos.DrawLine(landing, landing - worldDir * headSize - right * headSize * 0.5f);

        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.Label(peak + Vector3.up * 0.2f, $"Peak +{arcPeakHeight}m");
        UnityEditor.Handles.color = Color.yellow;
        UnityEditor.Handles.Label(landing + Vector3.up * 0.2f, $"{throwDistance}m");

        // Obstacle check radius — shown at launch, peak, and landing
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
        Gizmos.DrawSphere(origin,  obstacleCheckRadius);
        Gizmos.DrawSphere(peak,    obstacleCheckRadius);
        Gizmos.DrawSphere(landing, obstacleCheckRadius);
        Gizmos.color = new Color(1f, 0.4f, 0f, 1f);
        Gizmos.DrawWireSphere(origin,  obstacleCheckRadius);
        Gizmos.DrawWireSphere(peak,    obstacleCheckRadius);
        Gizmos.DrawWireSphere(landing, obstacleCheckRadius);
        UnityEditor.Handles.color = new Color(1f, 0.4f, 0f, 1f);
        UnityEditor.Handles.Label(landing + Vector3.up * (obstacleCheckRadius + 0.25f), $"r={obstacleCheckRadius}");
    }
#endif
}
