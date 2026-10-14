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
            Debug.Log($"[LevelExitController] Exiting level — levelID='{gridData.levelID}'  portalIndex={portalIndex}");
        }
        else
        {
            Debug.LogWarning("[LevelExitController] SelectedGridData is NULL — the boat cannot be put out at this door, and goes back where it went in.");
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

    // The door this level is left by, which the level select finds when it loads and puts the
    // boat out in front of. The spot the boat went in from is written first, so it is still
    // there to fall back on if the map has no such door.
    private void SaveBoatStateForPortal()
    {
        SaveBoatStateFromCache();

        GridData gd = LevelSelectionCache.SelectedGridData;
        if (gd != null && !string.IsNullOrEmpty(gd.levelID) && portalIndex >= 0)
            GameProgressData.SaveBoatDoor(gd.levelID, portalIndex);
    }

    // No door routing to follow, so the boat goes back to the spot on the map it left from.
    private void SaveBoatStateFromCache()
    {
        if (LevelSelectionCache.BoatHasPose)
            GameProgressData.SaveBoatPose(LevelSelectionCache.BoatPosition,
                                          LevelSelectionCache.BoatHeading);
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
