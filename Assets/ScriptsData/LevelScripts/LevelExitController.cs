using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelExitController : MonoBehaviour
{
    [Header("Settings")]
    public string sceneToLoad;

    [Tooltip("Index into GridData.entrances. Stamped automatically by LevelSpawner at instantiation.")]
    public int portalIndex = -1;

    [Header("Debug")]
    public bool drawGizmo = true;

    // ─────────────────────────────────────────────
    // STATIC GUARD
    // Prevents double-commit when two portal triggers fire simultaneously.
    // ─────────────────────────────────────────────

    private static bool s_anyPortalTriggered = false;

    public static void ResetPortalGuard()
    {
        s_anyPortalTriggered = false;
    }

    // ─────────────────────────────────────────────
    // INSTANCE STATE
    // ─────────────────────────────────────────────

    private Transform boat;
    private bool hasLoaded = false;

    void Start()
    {
        if (LevelDataController.Instance != null)
            boat = LevelDataController.Instance.GetBoatRoot();
    }

    // ─────────────────────────────────────────────
    // TRIGGER
    // ─────────────────────────────────────────────

    void OnTriggerEnter(Collider other)
    {
        if (s_anyPortalTriggered) return;
        if (hasLoaded) return;
        if (boat == null || string.IsNullOrEmpty(sceneToLoad)) return;
        if (other.transform != boat) return;

        BoatMovement bm = LevelDataController.Instance?.GetBoatMovement();

        PortalConfirmUI.Instance?.Show(
            "EXIT?",
            onConfirm: () => ConfirmExit(bm),
            onCancel:  () => { }
        );
    }

    void OnTriggerExit(Collider other)
    {
        if (other.transform != boat) return;
        if (PortalConfirmUI.Instance != null && PortalConfirmUI.Instance.IsShowing)
            PortalConfirmUI.Instance.OnNo();
    }

    // ─────────────────────────────────────────────
    // CONFIRM
    // ─────────────────────────────────────────────

    private void ConfirmExit(BoatMovement bm)
    {
        if (s_anyPortalTriggered) return;
        s_anyPortalTriggered = true;
        hasLoaded = true;

        LevelSoulTracker.Instance?.ReturnDroppedSoulsToHome();
        LevelSoulTracker.Instance?.CommitSouls();

        GridData gridData = LevelSelectionCache.SelectedGridData;
        if (gridData != null)
        {
            GameProgressData.IncrementCompletionCount(gridData.levelID);
            LevelSelectionCache.JustExitedLevelID       = gridData.levelID;
            LevelSelectionCache.JustExitedEntranceIndex = portalIndex;
            Debug.Log($"[LevelExitController] Exiting level — levelID='{gridData.levelID}'  portalIndex={portalIndex}  → JustExitedLevelID set");
        }
        else
        {
            Debug.LogWarning("[LevelExitController] SelectedGridData is NULL — JustExitedLevelID will not be set. River exit extrusion will not trigger.");
        }

        SaveBoatStateForPortal();
        SceneManager.LoadScene(
            !string.IsNullOrEmpty(LevelSelectionCache.CurrentWorldScene)
                ? LevelSelectionCache.CurrentWorldScene
                : sceneToLoad);
    }

    // ─────────────────────────────────────────────
    // BOAT STATE
    // ─────────────────────────────────────────────

    // Called by whirlpool triggers — no soul commit, no completion credit.
    public void ForceExit()
    {
        if (s_anyPortalTriggered) return;
        s_anyPortalTriggered = true;
        hasLoaded = true;

        LevelSoulTracker.Instance?.ReturnDroppedSoulsToHome();
        SaveBoatStateFromCache();
        SceneManager.LoadScene(
            !string.IsNullOrEmpty(LevelSelectionCache.CurrentWorldScene)
                ? LevelSelectionCache.CurrentWorldScene
                : sceneToLoad);
    }

    private void SaveBoatStateForPortal()
    {
        GridData gd = LevelSelectionCache.SelectedGridData;

        // Use routing pushed into this entrance by LevelSelectArenaController
        if (gd != null && portalIndex >= 0 &&
            gd.entrances != null && portalIndex < gd.entrances.Count)
        {
            var entrance = gd.entrances[portalIndex];
            if (!string.IsNullOrEmpty(entrance.targetSegmentID))
            {
                GameProgressData.SaveBoatState(
                    entrance.targetSegmentID,
                    entrance.targetProgress,
                    entrance.targetIsLeftPath,
                    entrance.targetIsRightPath
                );
                return;
            }
        }

        // Fallback: return boat to the river position it entered from
        SaveBoatStateFromCache();
    }

    private void SaveBoatStateFromCache()
    {
        string segmentID = LevelSelectionCache.BoatSegmentID;
        float  progress  = LevelSelectionCache.BoatProgress;

        if (!string.IsNullOrEmpty(segmentID))
            GameProgressData.SaveBoatState(segmentID, progress, GameProgressData.GetBoatIsLeftPath());
    }

    // ─────────────────────────────────────────────
    // GIZMO
    // ─────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!drawGizmo) return;

        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;

        Gizmos.color  = Color.green;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
