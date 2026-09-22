using UnityEngine;
using TMPro;

/// <summary>
/// The little line that comes up when the boat is near something worth stopping at — the key
/// and what it does. One of these in the level select canvas; every interact point writes to it
/// through <see cref="Instance"/>.
///
/// Setup in the canvas:
///   - <see cref="panel"/>      the object turned on and off. Left empty, this object is used.
///   - <see cref="promptText"/> the line itself.
/// </summary>
public class LevelSelectInteractPromptUI : MonoBehaviour
{
    public static LevelSelectInteractPromptUI Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Root object toggled on and off. Left empty, this object is toggled instead.")]
    [SerializeField] private GameObject panel;

    [Tooltip("Where the line is written.")]
    [SerializeField] private TMP_Text promptText;

    private GameObject Root => panel != null ? panel : gameObject;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Hidden to begin with — nothing is in reach the moment the map loads.
        if (panel != null) panel.SetActive(false);
        else if (promptText != null) promptText.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Puts a line up, or changes the one already there.</summary>
    public void Show(string line)
    {
        if (promptText != null)
        {
            promptText.text = line;
            promptText.gameObject.SetActive(true);
        }

        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        else if (promptText != null) promptText.gameObject.SetActive(false);
    }
}
