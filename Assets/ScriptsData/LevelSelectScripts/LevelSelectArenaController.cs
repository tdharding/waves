using UnityEngine;
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

        [Tooltip("Where the boat comes out when the player leaves the level by this door: out " +
                 "past the door by its Boat Spawn Distance, on the water. Written by the Level " +
                 "Select Designer.")]
        public Vector3 boatExitPosition;

        [Tooltip("Which way the boat faces when it comes out — straight out from the arena, away " +
                 "from the door. Written by the Level Select Designer; zero means not written yet.")]
        public Vector3 boatExitForward;

        public bool HasBoatExit => boatExitForward.sqrMagnitude > 1e-6f;
    }

    [Header("Soul Fish Display")]
    [Tooltip("The single shared soul-fish display in the scene. " +
             "Assign the same reference on every arena — only one arena activates it at a time.")]
    public LevelSelectSoulFishDisplay soulFishDisplay;

    [Header("Portal Links")]
    [Tooltip("One entry per river path that leads into this arena. " +
             "The controller pushes gridData and entranceIndex to each trigger in Awake, " +
             "so you only need to set them here rather than on each individual trigger.")]
    public List<PortalLink> portalLinks = new List<PortalLink>();

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
        }

        if (wavePlane != null)
            wavePlane.SetGridData(gridData);
    }

    // ─────────────────────────────────────────────
    // BOAT EXIT
    // ─────────────────────────────────────────────

    /// <summary>
    /// Where the boat comes out of door <paramref name="entranceIndex"/> of this arena, and the
    /// way it faces. False when there is no such door, or the designer has not written its exit.
    /// </summary>
    public bool TryGetBoatExit(int entranceIndex, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        var link = portalLinks.Find(l => l != null && l.entranceIndex == entranceIndex);
        if (link == null || !link.HasBoatExit) return false;

        Vector3 forward = link.boatExitForward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) return false;

        position = link.boatExitPosition;
        rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        return true;
    }

    /// <summary>
    /// The boat exit of door <paramref name="entranceIndex"/> of the arena showing
    /// <paramref name="levelID"/>, from every arena in the open scene. False when none has it.
    /// </summary>
    public static bool TryFindBoatExit(string levelID, int entranceIndex,
                                       out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (string.IsNullOrEmpty(levelID)) return false;

        foreach (var arena in FindObjectsByType<LevelSelectArenaController>(FindObjectsSortMode.None))
        {
            if (arena.gridData == null || arena.gridData.levelID != levelID) continue;
            if (arena.TryGetBoatExit(entranceIndex, out position, out rotation)) return true;
        }
        return false;
    }

    // ─────────────────────────────────────────────
    // SOUL FISH DISPLAY
    // ─────────────────────────────────────────────

    /// <summary>
    /// Tell the shared soul-fish display to show the fish count for this level.
    /// Called automatically by <see cref="LevelSelectArenaProximity"/> when the boat
    /// enters the arena's proximity zone; can also be called manually.
    /// </summary>
    public void ShowSoulFishDisplay(Renderer fadeTarget = null, Renderer fadeInTarget = null)
    {
        if (soulFishDisplay != null)
            soulFishDisplay.Show(gridData, fadeTarget, fadeInTarget);
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


    // One colour per link so you can tell doors apart at a glance
    private static readonly Color[] _linkColours = new Color[]
    {
        new Color(0f,   1f,   1f,   1f), // cyan
        new Color(1f,   0.5f, 0f,   1f), // orange
        new Color(0.4f, 1f,   0.4f, 1f), // green
        new Color(1f,   0.3f, 0.8f, 1f), // pink
    };

    // Where each door lets the boat out, and which way it faces.
    private void OnDrawGizmosSelected()
    {
        if (portalLinks == null) return;

        for (int i = 0; i < portalLinks.Count; i++)
        {
            var link = portalLinks[i];
            if (link == null || !link.HasBoatExit) continue;

            Color   col = _linkColours[i % _linkColours.Length];
            Vector3 at  = link.boatExitPosition;
            Vector3 fwd = link.boatExitForward.normalized;

            Gizmos.color = col;
            Gizmos.DrawSphere(at, 0.3f);

            UnityEditor.Handles.color = col;
            UnityEditor.Handles.DrawLine(at, at + fwd * 1.5f, 3f);
            UnityEditor.Handles.Label(at + Vector3.up * 1f, $"Entrance {link.entranceIndex}\nboat exit");
        }
    }
#endif
}
