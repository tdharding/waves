using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Singleton Y/N confirmation prompt shown when the player reaches a portal.
/// Place one instance in the Canvas of each scene (in-level and level select).
/// Wire yesButton.onClick → OnYes() and noButton.onClick → OnNo() in the inspector.
/// Canvas Sort Order must be higher than the draggable soul panel to capture clicks correctly.
/// </summary>
public class PortalConfirmUI : MonoBehaviour
{
    public static PortalConfirmUI Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;       // root panel — toggled active/inactive
    [SerializeField] private TMP_Text   promptText;  // displays "ENTER?" or "EXIT?"
    [SerializeField] private Button     yesButton;   // wire onClick → OnYes() in inspector
    [SerializeField] private Button     noButton;    // wire onClick → OnNo()  in inspector

    private System.Action _onConfirm;
    private System.Action _onCancel;
    private bool          _isShowing;

    public bool IsShowing => _isShowing;

    // ─────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────

    public void Show(string prompt, System.Action onConfirm, System.Action onCancel, bool showCancel = true, string yesLabel = "YES", string noLabel = "NO")
    {
        if (_isShowing) return; // already showing for another portal — ignore

        _onConfirm = onConfirm;
        _onCancel  = onCancel;
        _isShowing = true;

        if (promptText != null) promptText.text = prompt;
        if (panel != null)      panel.SetActive(true);

        if (yesButton != null)
        {
            var txt = yesButton.GetComponentInChildren<TMP_Text>();
            if (txt != null) txt.text = yesLabel;
        }

        if (noButton != null)
        {
            noButton.gameObject.SetActive(showCancel);
            var txt = noButton.GetComponentInChildren<TMP_Text>();
            if (txt != null) txt.text = noLabel;
        }
    }

    public void Hide()
    {
        if (!_isShowing) return;
        _isShowing = false;
        if (panel != null) panel.SetActive(false);
        _onConfirm = null;
        _onCancel  = null;
    }

    // ─────────────────────────────────────────────
    // INPUT
    // ─────────────────────────────────────────────

    private void Update()
    {
        if (!_isShowing) return;
        if (Input.GetKeyDown(KeyCode.Y)) OnYes();
        if (Input.GetKeyDown(KeyCode.N)) OnNo();
    }

    // Called by yesButton.onClick and Y key
    public void OnYes()
    {
        Debug.Log("[PortalConfirmUI] OnYes clicked.");
        if (!_isShowing) 
        {
            Debug.Log("[PortalConfirmUI]   Not showing, ignoring click.");
            return;
        }
        System.Action cb = _onConfirm;
        Hide();
        cb?.Invoke();
    }

    // Called by noButton.onClick and N key
    public void OnNo()
    {
        Debug.Log("[PortalConfirmUI] OnNo clicked.");
        if (!_isShowing)
        {
            Debug.Log("[PortalConfirmUI]   Not showing, ignoring click.");
            return;
        }
        System.Action cb = _onCancel;
        Hide();
        cb?.Invoke();
    }
}
