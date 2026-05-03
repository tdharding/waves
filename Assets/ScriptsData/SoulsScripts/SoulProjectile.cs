using UnityEngine;
using System.Collections;

/// <summary>
/// Spawned by BoatCollisionHandler when a soul is knocked off the boat.
/// Handles parabolic arc movement toward a wave-accurate landing point,
/// obstacle avoidance via Physics.OverlapSphere resampling, splash placeholder,
/// and DroppedSoul prefab instantiation on landing.
/// </summary>
public class SoulProjectile : MonoBehaviour
{
    [Header("Debug Preview")]
    public Transform debugOrigin;

    [Header("Arc Settings")]
    [Tooltip("How long the soul takes to travel from boat to water.")]
    public float arcDuration = 1.2f;

    [Tooltip("Height of the arc peak above the launch point.")]
    public float arcPeakHeight = 3f;

    [Tooltip("Radius within which the landing point is randomly chosen around the boat.")]
    public float throwRadius = 4f;

    [Tooltip("Minimum distance from the boat the soul must land.")]
    public float minThrowDistance = 1.5f;

    [Header("Wave Sampling")]
    [Tooltip("Extra Y offset applied on top of the sampled wave height.")]
    public float extraYOffset = 0.01f;

    [Tooltip("Height multiplier applied to wave amplitude.")]
    public float heightMultiplier = 1f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Radius used in OverlapSphere check at candidate landing points.")]
    public float obstacleCheckRadius = 0.75f;

    [Tooltip("How many times to resample a landing point before falling back.")]
    public int maxResampleAttempts = 8;

    [Header("Prefabs")]
    [Tooltip("The DroppedSoul prefab to instantiate at landing.")]
    public GameObject droppedSoulPrefab;

    [Tooltip("Splash VFX prefab — leave empty until designed.")]
    public GameObject splashPrefab;

    [Header("Debug Preview")]
    [Tooltip("Draw arc preview gizmo in Scene view at all times.")]
    public bool previewInSceneView = true;

    [Tooltip("Direction the preview arc is drawn toward in edit mode.")]
    public Vector3 previewDirection = Vector3.forward;

    [Tooltip("How many segments to draw the arc preview with.")]
    public int arcGizmoSegments = 24;

    // ─────────────────────────────────────────────
    // PRIVATE STATE
    // ─────────────────────────────────────────────

    public static readonly System.Collections.Generic.List<SoulProjectile> ActiveProjectiles
        = new System.Collections.Generic.List<SoulProjectile>();

    public int IdentityToCarry => identityToCarry;
    public int LinkIDToCarry   => linkIDToCarry;

    private static readonly string[] obstacleTags =
        { "MazeWalls", "LowHMazeWall", "TallHMazeWall", "LowSpikeTrap", "BadGuySnake" };

    private int identityToCarry;
    private int linkIDToCarry;      // ← full passport preserved through the arc

    private Vector3 launchPos;
    private Vector3 landingXZ;
    private Vector3 peakPos;

    private Transform waterTransform;
    private Material  waterMat;

    private bool initialised = false;

    // ─────────────────────────────────────────────
    // REGISTRY
    // ─────────────────────────────────────────────

    private void Awake()   => ActiveProjectiles.Add(this);
    private void OnDestroy() => ActiveProjectiles.Remove(this);

    // ─────────────────────────────────────────────
    // PUBLIC INIT
    // ─────────────────────────────────────────────

    /// <summary>
    /// Launch the projectile. Now accepts linkID so the full soul passport
    /// is preserved through to the DroppedSoul on landing.
    /// </summary>
    public void Launch(Vector3 spawnWorldPos, Transform boatTransform, int soulID, int linkID)
    {
        launchPos       = spawnWorldPos;
        identityToCarry = soulID;
        linkIDToCarry   = linkID;

        if (!CacheWaterData())
        {
            Debug.LogWarning("[SoulProjectile] Could not get water data. Destroying.");
            Destroy(gameObject);
            return;
        }

        Vector3 candidateLanding = PickLandingPoint(boatTransform);
        landingXZ = new Vector3(candidateLanding.x, 0f, candidateLanding.z);

        Vector3 midXZ = Vector3.Lerp(launchPos, candidateLanding, 0.5f);
        peakPos = new Vector3(midXZ.x, launchPos.y + arcPeakHeight, midXZ.z);

        initialised = true;
        StartCoroutine(ArcRoutine());
    }

    // ─────────────────────────────────────────────
    // WATER DATA
    // ─────────────────────────────────────────────

    private bool CacheWaterData()
    {
        if (LevelDataController.Instance == null) return false;

        waterTransform = LevelDataController.Instance.GetWaveTransform();
        if (waterTransform == null) return false;

        MeshRenderer mr = waterTransform.GetComponent<MeshRenderer>();
        if (mr == null) return false;

        waterMat = mr.sharedMaterial;

        return true;
    }

    // ─────────────────────────────────────────────
    // WAVE HEIGHT
    // ─────────────────────────────────────────────

    private float SampleWaveY(Vector3 worldXZPos)
    {
        var p = WaveUtils.ReadParams(waterTransform, waterMat);
        return p.origin.y + WaveUtils.SampleHeight(worldXZPos, p, heightMultiplier) + extraYOffset;
    }

    // ─────────────────────────────────────────────
    // LANDING POINT
    // ─────────────────────────────────────────────

    private Vector3 PickLandingPoint(Transform boatTransform)
    {
        for (int attempt = 0; attempt < maxResampleAttempts; attempt++)
        {
            Vector3 candidate = SampleRandomLandingXZ(boatTransform);
            if (!HasObstacleAt(candidate))
            {
                if (attempt > 0) Debug.Log($"[SoulProjectile] Landing accepted after {attempt + 1} attempts.");
                return candidate;
            }
        }

        Debug.LogWarning("[SoulProjectile] All resample attempts failed. Falling back to near-boat landing.");
        return boatTransform.position + boatTransform.right * minThrowDistance;
    }

    private Vector3 SampleRandomLandingXZ(Transform boatTransform)
    {
        Vector2 randomCircle = Random.insideUnitCircle.normalized * Random.Range(minThrowDistance, throwRadius);
        Vector3 candidate    = boatTransform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
        candidate.y = SampleWaveY(candidate);
        return candidate;
    }

    private bool HasObstacleAt(Vector3 worldPos)
    {
        Collider[] hits = Physics.OverlapSphere(worldPos, obstacleCheckRadius);
        foreach (Collider col in hits)
            foreach (string tag in obstacleTags)
                if (col.CompareTag(tag)) return true;
        return false;
    }

    // ─────────────────────────────────────────────
    // ARC
    // ─────────────────────────────────────────────

    private IEnumerator ArcRoutine()
    {
        float t = 0f;

        while (t < arcDuration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / arcDuration);

            float   currentLandingY = SampleWaveY(new Vector3(landingXZ.x, 0f, landingXZ.z));
            Vector3 landingPos      = new Vector3(landingXZ.x, currentLandingY, landingXZ.z);

            Vector3 flatPos = Vector3.Lerp(launchPos, landingPos, n);
            float   arcY    = Mathf.Sin(n * Mathf.PI) * arcPeakHeight;

            transform.position = new Vector3(flatPos.x, flatPos.y + arcY, flatPos.z);
            yield return null;
        }

        OnLand();
    }

    // ─────────────────────────────────────────────
    // LANDING
    // ─────────────────────────────────────────────

    private void OnLand()
    {
        float   finalY     = SampleWaveY(new Vector3(landingXZ.x, 0f, landingXZ.z));
        Vector3 landingPos = new Vector3(landingXZ.x, finalY, landingXZ.z);

        if (splashPrefab != null)
            Instantiate(splashPrefab, landingPos, Quaternion.identity);

        if (droppedSoulPrefab != null)
        {
            GameObject droppedObj = Instantiate(droppedSoulPrefab, landingPos, Quaternion.identity);
            DroppedSoul ds        = droppedObj.GetComponent<DroppedSoul>();

            if (ds != null)
                ds.ConfigureDroppedSoul(identityToCarry, linkIDToCarry); // full passport handed over
        }

        Destroy(gameObject);
    }

    // ─────────────────────────────────────────────
    // GIZMOS
    // ─────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!previewInSceneView) return;

        Vector3 origin = (debugOrigin != null) ? debugOrigin.position : transform.position;
        Vector3 landingPoint;

        if (initialised)
        {
            float landingY = SampleWaveY(new Vector3(landingXZ.x, 0f, landingXZ.z));
            landingPoint   = new Vector3(landingXZ.x, landingY, landingXZ.z);
        }
        else
        {
            Vector3 dir  = previewDirection.sqrMagnitude > 0.01f ? previewDirection.normalized : Vector3.forward;
            landingPoint = origin + dir * throwRadius;
        }

        Vector3 midXZ = Vector3.Lerp(origin, landingPoint, 0.5f);
        Vector3 peak  = new Vector3(midXZ.x, origin.y + arcPeakHeight, midXZ.z);

        Gizmos.color = Color.cyan;
        Vector3 prev = origin;

        for (int i = 1; i <= arcGizmoSegments; i++)
        {
            float   n    = (float)i / arcGizmoSegments;
            Vector3 flat = Vector3.Lerp(origin, landingPoint, n);
            float   arcY = Mathf.Sin(n * Mathf.PI) * arcPeakHeight;
            Vector3 next = new Vector3(flat.x, flat.y + arcY, flat.z);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(peak, 0.15f);

        Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
        Gizmos.DrawLine(new Vector3(peak.x, origin.y, peak.z), peak);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(origin, 0.15f);

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(landingPoint, 0.2f);

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(peak + Vector3.up * 0.2f, $"Peak: +{arcPeakHeight}m");
        UnityEditor.Handles.Label(
            landingPoint + Vector3.right * 0.2f,
            initialised ? "Landing" : $"Preview\n(radius: {throwRadius}m)"
        );
    }
#endif
}