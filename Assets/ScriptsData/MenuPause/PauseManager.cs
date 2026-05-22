using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }
    public static bool IsPaused { get; private set; }

    [Header("UI")]
    public GameObject pauseMenu;

    [Header("Scene Navigation")]
    [Tooltip("Scene to load when Quit/Exit is pressed")]
    public string quitDestinationScene = "LevelSelect";

    [Header("Debug / Playtest")]
    [Tooltip("When enabled, losing window focus will NOT force the pause menu")]
    public bool playtestMode = false;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        ResumeGame(); // ensure clean starting state
    }

    void Update()
    {
        // ESC only PAUSES, never resumes
        if (Input.GetKeyDown(KeyCode.Escape) && !IsPaused)
            PauseGame();

        // Auto-pause if the game window loses focus (unless in playtest mode)
        if (!playtestMode && !Application.isFocused && !IsPaused)
            PauseGame();
    }

    // ---------------------------------------------------------
    // CORE PAUSE LOGIC
    // ---------------------------------------------------------

    public void PauseGame()
    {
        IsPaused = true;

        Time.timeScale = 0f;
        AudioListener.pause = true;

        if (pauseMenu != null)
            pauseMenu.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ResumeGame()
    {
        IsPaused = false;

        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (pauseMenu != null)
            pauseMenu.SetActive(false);


    }

public void QuitGame()
{
    LevelSoulTracker.Instance?.ReturnDroppedSoulsToHome();
    LevelSoulTracker.Instance?.ClearTemporarySouls();

    GridData gridData = LevelSelectionCache.SelectedGridData;
    if (gridData != null)
        LevelSelectionCache.JustExitedLevelID = gridData.levelID;

    SaveBoatStateFromCache();
    SceneManager.LoadScene(quitDestinationScene); // ← uses inspector value
}
private void SaveBoatStateFromCache()
{
    string segmentID = LevelSelectionCache.BoatSegmentID;
    float progress   = LevelSelectionCache.BoatProgress;

    if (!string.IsNullOrEmpty(segmentID))
    {
        GameProgressData.SaveBoatState(segmentID, progress);
        Debug.Log($"PauseManager: Saved boat state — segment: {segmentID} progress: {progress}");
    }
    else
    {
        Debug.LogWarning("PauseManager: No boat segment cached — boat will return to default.");
    }
}
}
