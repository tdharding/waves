using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LinkIdentityLabel))]
public class DroppedSoul : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // STATIC REGISTRY
    // ─────────────────────────────────────────────

    /// <summary>
    /// All currently active DroppedSoul instances in the scene.
    /// Automatically maintained — do not modify externally.
    /// </summary>
    public static readonly List<DroppedSoul> ActiveDroppedSouls = new List<DroppedSoul>();

    // ─────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────

    [Header("Wave Settings")]
    public float extraYOffset = 0.5f;
    public float heightMultiplier = 1f;

    [Header("LOD")]
    public float activeDistance = 25f;

    [Header("Arena Bounds (read-only — set by ArenaProfile)")]
    [SerializeField] private float debugBoundsRadius = 20f;

    // ─────────────────────────────────────────────
    // PRIVATE STATE
    // ─────────────────────────────────────────────

    private Transform waterTransform;
    private Transform boatRoot;
    private Material  mat;

    private LinkIdentityLabel identityLabel;

    private Vector3 arenaCentre;
    private float   arenaBoundsRadius;
    private bool    boundsReady;

    // ─────────────────────────────────────────────
    // REGISTRY LIFECYCLE
    // ─────────────────────────────────────────────

    void Awake()
    {
        identityLabel = GetComponent<LinkIdentityLabel>();
        ActiveDroppedSouls.Add(this);
    }

    void OnDestroy()
    {
        ActiveDroppedSouls.Remove(this);
        UIMapController.Instance?.HideDroppedSoulMarker(GetInstanceID());
    }

    // ─────────────────────────────────────────────
    // CONFIGURATION
    // ─────────────────────────────────────────────

    /// <summary>
    /// Called by SoulProjectile on landing.
    /// Preserves the full soul passport — both who it is (soulID) and
    /// where it came from (linkID) — so it can be correctly un-caught
    /// in its home level if left unrescued on exit.
    /// </summary>
    public void ConfigureDroppedSoul(int soulID, int linkID)
    {
        if (identityLabel != null)
        {
            identityLabel.soulDataIdentity = soulID;
            identityLabel.linkID           = linkID;
        }
    }

    // ─────────────────────────────────────────────
    // START
    // ─────────────────────────────────────────────

    void Start()
    {
        UIMapController.Instance?.ShowDroppedSoulMarker(GetInstanceID(), transform.position);

        if (LevelDataController.Instance == null) return;

        waterTransform = LevelDataController.Instance.GetWaveTransform();
        boatRoot       = LevelDataController.Instance.GetBoatRoot();

        if (!waterTransform || !boatRoot) return;

        mat = waterTransform.GetComponent<MeshRenderer>().sharedMaterial;

        ArenaProfile profile = LevelDataController.Instance.GetArenaProfile();
        if (profile != null)
        {
            arenaBoundsRadius = profile.droppedSoulBoundsRadius;
            debugBoundsRadius = arenaBoundsRadius;
            arenaCentre       = LevelDataController.Instance.GetArenaCentre();
            boundsReady       = true;
        }
        else
        {
            Debug.LogWarning("[DroppedSoul] No ArenaProfile found — bounds clamping disabled.");
        }
    }

    // ─────────────────────────────────────────────
    // UPDATE
    // ─────────────────────────────────────────────

    void LateUpdate()
    {
        if (!waterTransform || !boatRoot) return;

        float sqrDist = (boatRoot.position - transform.position).sqrMagnitude;
        if (sqrDist > activeDistance * activeDistance) return;

        ApplyWaveHeight();
        ClampToBounds();
    }

    // ─────────────────────────────────────────────
    // WAVE HEIGHT
    // ─────────────────────────────────────────────

    void ApplyWaveHeight()
    {
        var p = WaveUtils.ReadParams(waterTransform, mat);
        float height = WaveUtils.SampleHeight(transform.position, p, heightMultiplier) + extraYOffset;

        Vector3 pos = transform.position;
        pos.y = p.origin.y + height;
        transform.position = pos;
    }

    // ─────────────────────────────────────────────
    // BOUNDS CLAMPING
    // ─────────────────────────────────────────────

    void ClampToBounds()
    {
        if (!boundsReady) return;

        Vector3 pos     = transform.position;
        Vector2 flatPos = new Vector2(pos.x - arenaCentre.x, pos.z - arenaCentre.z);

        if (flatPos.sqrMagnitude > arenaBoundsRadius * arenaBoundsRadius)
        {
            Vector2 clamped = flatPos.normalized * arenaBoundsRadius;
            transform.position = new Vector3(
                arenaCentre.x + clamped.x,
                pos.y,
                arenaCentre.z + clamped.y
            );
        }
    }

    // ─────────────────────────────────────────────
    // CAPTURE
    // ─────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("BoatPrefab")) return;

        var fishing = LevelDataController.Instance?.FishingController;
        if (fishing != null && identityLabel != null)
            fishing.OnFishCaptured(identityLabel);

        Destroy(gameObject);
    }

    // ─────────────────────────────────────────────
    // GIZMOS
    // ─────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        DrawBoundsGizmoStatic(debugBoundsRadius, Application.isPlaying ? arenaCentre : Vector3.zero);

        UnityEditor.Handles.color = new Color(1f, 0.4f, 0f, 0.9f);
        UnityEditor.Handles.Label(
            (Application.isPlaying ? arenaCentre : Vector3.zero) + Vector3.forward * debugBoundsRadius + Vector3.up,
            $"Dropped Soul Bounds\nr = {debugBoundsRadius}m"
        );
    }

    public static void DrawBoundsGizmoStatic(float radius, Vector3 centre)
    {
        const int segments  = 64;
        float     angleStep = 360f / segments;

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.8f);

        Vector3 prev = centre + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float   angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 next  = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.4f);
        Gizmos.DrawLine(centre + Vector3.left  * radius, centre + Vector3.right   * radius);
        Gizmos.DrawLine(centre + Vector3.back  * radius, centre + Vector3.forward * radius);
    }
#endif
}