using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Place on a child of outerWallsPrefab.
// Defines the world-space Y and forward direction used when spawning baseline-aligned prefabs.
public class BaselineMarker : MonoBehaviour
{
    public float height     = 0f;
    public float discRadius = 12f;

    [Header("Wall Top Height")]
    [Tooltip("Enable to define the wall-top height. When set it draws an extra gizmo disc and is shown in the Grid Designer beneath the wall prefab field.")]
    public bool  useWallTopHeight = false;
    [Tooltip("Absolute world Y of the wall top (where you position the gizmo). The distance from the baseline is derived as wallTopHeight - height, so a negative baseline is handled correctly. Only meaningful when useWallTopHeight is true.")]
    public float wallTopHeight    = 0f;

    // Distance from the baseline up to the wall top. Correct even when the baseline is negative.
    public float WallTopDistanceAboveBaseline => wallTopHeight - height;

    [Header("Arena Mask Settings")]
    [Range(0f, 1f)] public float maskFadeStartPct = 0.7f;
    [Range(0f, 1f)] public float maskFadeEndPct   = 0.9f;

    public float MaskFadeStartRadius => discRadius * maskFadeStartPct;
    public float MaskFadeEndRadius   => discRadius * maskFadeEndPct;

    [System.Serializable]
    public struct SpawnTier
    {
        public string name;
        public float  height;
    }

    [Header("Spawn Tiers")]
    [Tooltip("Per-tier absolute world Y heights and disc radii, indexed by yOffsetSlot. Leave empty to use TierConfig offsets (default behaviour unchanged).")]
    public SpawnTier[] spawnTiers;

    // Returns the absolute world Y for a given tier slot. Returns false if spawnTiers not defined for this slot.
    public bool TryGetTierHeight(int slot, out float h)
    {
        if (spawnTiers != null && slot >= 0 && slot < spawnTiers.Length)
        { h = spawnTiers[slot].height; return true; }
        h = 0f; return false;
    }

    // Called when the component is first added or Reset is chosen in the inspector.
    void Reset() => PopulateDefaultTiers();

    // G slot height always mirrors the baseline height field.
    void OnValidate()
    {
        if (spawnTiers == null) return;
        for (int i = 0; i < spawnTiers.Length; i++)
        {
            if (spawnTiers[i].name == "G")
            {
                var t = spawnTiers[i];
                t.height = height;
                spawnTiers[i] = t;
            }
        }
    }

    void PopulateDefaultTiers()
    {
        spawnTiers = new SpawnTier[]
        {
            new SpawnTier { name = "F2",  height = height },
            new SpawnTier { name = "F1",  height = height },
            new SpawnTier { name = "G",   height = height },
            new SpawnTier { name = "B-1", height = height },
            new SpawnTier { name = "B-2", height = height },
            new SpawnTier { name = "B-3", height = height },
        };
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Vector3 centre = new Vector3(transform.position.x, height, transform.position.z);

        // Main Arena Radius
        Handles.color = new Color(0f, 0.9f, 0.5f, 0.6f);
        Handles.DrawWireDisc(centre, Vector3.up, discRadius);

        // Mask Fade Start (Percentage-based)
        Handles.color = new Color(1f, 0f, 1f, 0.5f); // Magenta
        Handles.DrawWireDisc(centre, Vector3.up, MaskFadeStartRadius);

        // Mask Fade End (Percentage-based)
        Handles.color = new Color(0f, 1f, 1f, 0.5f); // Cyan
        Handles.DrawWireDisc(centre, Vector3.up, MaskFadeEndRadius);

        Gizmos.color = new Color(0f, 0.9f, 0.5f, 0.6f);
        Gizmos.DrawLine(centre - Vector3.right   * 1f, centre + Vector3.right   * 1f);
        Gizmos.DrawLine(centre - Vector3.forward * 1f, centre + Vector3.forward * 1f);

        Gizmos.color = new Color(0f, 0.9f, 0.5f, 0.9f);
        DrawArrow(centre, Vector3.up, 3f);

        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
        DrawArrow(centre, transform.forward, 4f);

        Handles.color = new Color(0f, 0.9f, 0.5f, 0.8f);
        Handles.Label(centre + Vector3.up * 3.6f, $"Baseline  y={height:F2}");

        // Wall Top Height — optional disc marking the top of the arena walls.
        // wallTopHeight is an absolute world Y; the distance above the baseline is
        // derived (wallTopHeight - height) so a negative baseline is handled correctly.
        if (useWallTopHeight)
        {
            float   wallTopY = wallTopHeight;
            float   wallDist = WallTopDistanceAboveBaseline;
            Vector3 wt = new Vector3(transform.position.x, wallTopY, transform.position.z);
            Handles.color = new Color(1f, 0.55f, 0.1f, 0.75f);
            Handles.DrawWireDisc(wt, Vector3.up, discRadius);
            Gizmos.color  = new Color(1f, 0.55f, 0.1f, 0.6f);
            Gizmos.DrawLine(centre, wt); // baseline → wall top
            Handles.color = new Color(1f, 0.55f, 0.1f, 0.95f);
            Handles.Label(wt + Vector3.up * 0.3f, $"Wall Top  y={wallTopY:F2}  (+{wallDist:F2} above baseline)");
        }

        // Per-tier discs (additive — only drawn when spawnTiers is populated)
        if (spawnTiers != null)
        {
            for (int i = 0; i < spawnTiers.Length; i++)
            {
                var st = spawnTiers[i];
                Vector3 tc = new Vector3(transform.position.x, st.height, transform.position.z);
                Color c = Color.HSVToRGB((i * 0.15f) % 1f, 0.7f, 1f);
                c.a = 0.55f;
                Handles.color = c;
                Handles.DrawWireDisc(tc, Vector3.up, discRadius);
                c.a = 0.85f;
                Handles.color = c;
                string label = string.IsNullOrEmpty(st.name) ? $"Tier{i}" : st.name;
                Handles.Label(tc + Vector3.up * 0.3f, $"{label}  y={st.height:F2}");
            }
        }
    }

    internal static void DrawArrow(Vector3 origin, Vector3 direction, float length)
    {
        Vector3 tip  = origin + direction.normalized * length;
        Gizmos.DrawLine(origin, tip);

        float   head  = length * 0.25f;
        Vector3 back  = -direction.normalized;
        Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.01f)
            right = Vector3.Cross(direction, Vector3.forward).normalized;
        Vector3 up2 = Vector3.Cross(right, direction).normalized;

        Gizmos.DrawLine(tip, tip + (back + right) * head * 0.5f);
        Gizmos.DrawLine(tip, tip + (back - right) * head * 0.5f);
        Gizmos.DrawLine(tip, tip + (back + up2)   * head * 0.5f);
        Gizmos.DrawLine(tip, tip + (back - up2)   * head * 0.5f);
    }
#endif
}
