using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;

/// <summary>
/// Root controller for a level arena in the level select scene.
/// Set GridData once here — it is pushed to all portal triggers and
/// LevelSelectWavePlane automatically. Wire component references on the prefab.
/// </summary>
public class LevelSelectArenaController : MonoBehaviour
{
    [Header("Level")]
    public GridData gridData;

    [Header("Wave Plane")]
    public LevelSelectWavePlane wavePlane;

    [System.Serializable]
    public class PortalLink
    {
        [Tooltip("Index into GridData.entrances for the door this river path leads to.")]
        public int entranceIndex = -1;

        [Tooltip("The LevelSelectEnter trigger on the river that leads to this door.")]
        public LevelSelectEnter trigger;

        [Tooltip("The baked spline path (with RiverSegmentID) that leads to this entrance. " +
                 "Used to route the boat back to the correct river segment on exit.")]
        public SplineContainer arenaPath;
    }

    [Header("Soul Fish Display")]
    [Tooltip("The single shared soul-fish display in the scene. " +
             "Assign the same reference on every arena — only one arena activates it at a time.")]
    public LevelSelectSoulFishDisplay soulFishDisplay;

    [Tooltip("Renderer on this arena whose _Alpha is faded out while the display is active.")]
    public Renderer soulFishDisplayFadeTarget;

    [Tooltip("Renderer on this arena whose _Alpha is faded in (0 → 1) while the display is active.")]
    public Renderer soulFishDisplayFadeIn;

    [Header("Portal Links")]
    [Tooltip("One entry per river path that leads into this arena. " +
             "The controller pushes gridData and entranceIndex to each trigger in Awake, " +
             "so you only need to set them here rather than on each individual trigger.")]
    public List<PortalLink> portalLinks = new List<PortalLink>();

    /// <summary>
    /// Called by SplinePathStitcher after baking. Assigns each portal link's arenaPath to the
    /// baked SplineContainer whose spline passes nearest to the link's trigger.
    /// </summary>
    public void AutoDetectPaths(List<SplineContainer> bakedContainers)
    {
        bool anyChanged = false;

        foreach (var link in portalLinks)
        {
            if (link.trigger == null) continue;

            Vector3 triggerPos = link.trigger.transform.position;
            SplineContainer nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var container in bakedContainers)
            {
                if (container == null || container.Spline == null) continue;

                Vector3 localPos = container.transform.InverseTransformPoint(triggerPos);
                SplineUtility.GetNearestPoint(container.Spline,
                    (Unity.Mathematics.float3)localPos, out _, out float t);

                Vector3 nearestWorld = container.transform.TransformPoint(
                    (Vector3)container.Spline.EvaluatePosition(t));
                float dist = Vector3.Distance(triggerPos, nearestWorld);

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = container;
                }
            }

            int linkIdx = portalLinks.IndexOf(link);

            if (nearest != null && link.arenaPath != nearest)
            {
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(this, "Auto-Detect Arena Path");
#endif
                link.arenaPath = nearest;
                anyChanged = true;
                Debug.Log($"[ArenaController] '{name}' link {linkIdx} arenaPath → '{nearest.name}' ({nearestDist:F2}m)");
            }

            if (link.entranceIndex != linkIdx)
            {
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(this, "Auto-Detect Arena Path");
#endif
                link.entranceIndex = linkIdx;
                anyChanged = true;
                Debug.Log($"[ArenaController] '{name}' link {linkIdx} entranceIndex → {linkIdx}");
            }
        }

        if (anyChanged)
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }

    private void Awake()
    {
        if (gridData != null && !string.IsNullOrEmpty(gridData.displayName))
            gameObject.name = gridData.displayName;

        foreach (var link in portalLinks)
        {
            if (link.trigger == null) continue;
            link.trigger.gridData      = gridData;
            link.trigger.entranceIndex = link.entranceIndex;
            Debug.Log($"[ArenaController] Wired trigger '{link.trigger.name}' → entranceIndex: {link.entranceIndex}");

            // Push river routing into GridData so LevelExitController can read it
            // when the player exits through this entrance portal.
            if (gridData != null && link.arenaPath != null &&
                link.entranceIndex >= 0 && link.entranceIndex < gridData.entrances.Count)
            {
                var segID = link.arenaPath.GetComponent<RiverSegmentID>();
                if (segID != null)
                {
                    // Auto-compute return progress from the trigger's world position
                    float returnProgress = 0.95f; // fallback
                    if (link.trigger != null)
                    {
                        Vector3 triggerWorld = link.trigger.transform.position;
                        Vector3 triggerLocal = link.arenaPath.transform.InverseTransformPoint(triggerWorld);
                        SplineUtility.GetNearestPoint(link.arenaPath.Spline,
                            (Unity.Mathematics.float3)triggerLocal, out _, out float t);
                        returnProgress = t;
                    }

                    var entrance = gridData.entrances[link.entranceIndex];
                    entrance.targetSegmentID    = segID.SegmentID;
                    entrance.targetProgress     = returnProgress;
                    entrance.targetIsLeftPath    = segID.IsLeftPath;
                    entrance.targetIsRightPath = segID.IsRightPath;
                    gridData.entrances[link.entranceIndex] = entrance;
                    Debug.Log($"[ArenaController] Entrance {link.entranceIndex} → segment '{segID.SegmentID}' @ {returnProgress:F3} (auto)");
                }
                else
                {
                    Debug.LogWarning($"[ArenaController] arenaPath '{link.arenaPath.name}' has no RiverSegmentID component.");
                }
            }
        }

        if (wavePlane != null)
            wavePlane.SetGridData(gridData);
    }

    // ─────────────────────────────────────────────
    // SOUL FISH DISPLAY
    // ─────────────────────────────────────────────

    /// <summary>
    /// Tell the shared soul-fish display to show the fish count for this level.
    /// Called automatically by <see cref="LevelSelectArenaProximity"/> when the boat
    /// enters the arena's proximity zone; can also be called manually.
    /// </summary>
    public void ShowSoulFishDisplay()
    {
        if (soulFishDisplay != null)
            soulFishDisplay.Show(gridData, soulFishDisplayFadeTarget, soulFishDisplayFadeIn);
    }

    /// <summary>
    /// Tell the shared soul-fish display to hide.
    /// </summary>
    public void HideSoulFishDisplay()
    {
        if (soulFishDisplay != null)
            soulFishDisplay.Hide();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (gridData != null && !string.IsNullOrEmpty(gridData.displayName))
            gameObject.name = gridData.displayName;
    }


    // One colour per link so you can tell paths apart at a glance
    private static readonly Color[] _linkColours = new Color[]
    {
        new Color(0f,   1f,   1f,   1f), // cyan
        new Color(1f,   0.5f, 0f,   1f), // orange
        new Color(0.4f, 1f,   0.4f, 1f), // green
        new Color(1f,   0.3f, 0.8f, 1f), // pink
    };

    private void OnDrawGizmosSelected()
    {
        if (portalLinks == null) return;

        for (int i = 0; i < portalLinks.Count; i++)
        {
            var link = portalLinks[i];
            if (link.arenaPath == null) continue;

            var spline    = link.arenaPath.Spline;
            var transform = link.arenaPath.transform;
            Color col     = _linkColours[i % _linkColours.Length];

            // Draw the spline as a polyline by sampling 64 points
            const int steps = 64;
            Vector3 prev = transform.TransformPoint(spline.EvaluatePosition(0f));
            for (int s = 1; s <= steps; s++)
            {
                float   t    = s / (float)steps;
                Vector3 next = transform.TransformPoint(spline.EvaluatePosition(t));
                UnityEditor.Handles.color = col;
                UnityEditor.Handles.DrawLine(prev, next, 3f);
                prev = next;
            }

            // Sphere + label at the auto-computed return progress point (nearest point to trigger)
            float returnT = 0.95f;
            if (link.trigger != null)
            {
                Vector3 triggerLocal = transform.InverseTransformPoint(link.trigger.transform.position);
                SplineUtility.GetNearestPoint(spline, (Unity.Mathematics.float3)triggerLocal, out _, out returnT);
            }

            Vector3 returnPos = transform.TransformPoint(spline.EvaluatePosition(returnT));
            Gizmos.color = col;
            Gizmos.DrawSphere(returnPos, 0.5f);

            string label = $"Entrance {link.entranceIndex}";
            var segID = link.arenaPath.GetComponent<RiverSegmentID>();
            if (segID != null && !string.IsNullOrEmpty(segID.SegmentID))
                label += $"\n{segID.SegmentID}";
            label += $"\nt={returnT:F2} (auto)";

            UnityEditor.Handles.color = col;
            UnityEditor.Handles.Label(returnPos + Vector3.up * 1.5f, label);
        }
    }
#endif
}
