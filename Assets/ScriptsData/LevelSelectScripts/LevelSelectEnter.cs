using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelSelectEnter : MonoBehaviour
{
    [Header("Level Select")]
    [Tooltip("Exact name of the scene to load (must be in Build Settings).")]
    public string sceneToLoad;

    [Tooltip("Level data passed to the next scene.")]
    public GridData gridData;

    [Tooltip("Index into GridData.entrances for the door this trigger leads to. " +
             "Set automatically by LevelSelectArenaController via PortalLink — no need to assign manually.")]
    public int entranceIndex = -1;

    // ─────────────────────────────────────────────
    // TRIGGER
    // ─────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("BoatPrefab")) return;

        LevelSelectBoatControl boatControl = FindObjectOfType<LevelSelectBoatControl>();

        string levelName = gridData != null && !string.IsNullOrEmpty(gridData.displayName)
            ? gridData.displayName
            : "ENTER?";

        PortalConfirmUI.Instance?.Show(
            levelName,
            onConfirm: () =>
            {
                CommitSelection();
                CacheBoatState(boatControl);
                LoadScene();
            },
            onCancel: () => { }
        );
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("BoatPrefab")) return;
        if (PortalConfirmUI.Instance != null && PortalConfirmUI.Instance.IsShowing)
            PortalConfirmUI.Instance.OnNo();
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    private void CommitSelection()
    {
        if (gridData == null)
        {
            Debug.LogWarning($"{name} triggered but no GridData assigned.");
            return;
        }

        LevelSelectionCache.SelectedGridData      = gridData;
        LevelSelectionCache.SelectedEntranceIndex = entranceIndex;
        Debug.Log($"Level selected: {gridData.displayName}, entranceIndex: {entranceIndex}");
    }

    private void CacheBoatState(LevelSelectBoatControl boatControl)
    {
        if (boatControl == null) return;
        LevelSelectionCache.BoatSegmentID = boatControl.CurrentSegmentID;
        LevelSelectionCache.BoatProgress  = boatControl.CurrentProgress;
        LevelSelectionCache.BoatIsLeftPath = boatControl.IsLeftPath;
    }

    private void LoadScene()
    {
        if (string.IsNullOrEmpty(sceneToLoad))
        {
            Debug.LogWarning($"{name} has no scene assigned.");
            return;
        }

        SceneManager.LoadScene(sceneToLoad);
    }
}
