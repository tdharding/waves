using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen a poster is shown on: one image held in the middle of the view, over whatever the
/// camera was looking at. One of these in the level select canvas.
///
/// Setup in the canvas:
///   - <see cref="panel"/>       the object turned on and off, holding the backdrop.
///   - <see cref="posterImage"/> the Image the art is put into. Size its rect to the biggest
///                               poster should be allowed to get — the art is fitted inside it.
/// </summary>
public class LevelSelectPosterOverlayUI : MonoBehaviour
{
    public static LevelSelectPosterOverlayUI Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Root object toggled on and off — the backdrop and the image together.")]
    [SerializeField] private GameObject panel;

    [Tooltip("The Image the poster art is put into.")]
    [SerializeField] private Image posterImage;

    public bool IsShowing { get; private set; }

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

    /// <summary>Puts a poster up. Nothing happens without art to show.</summary>
    public void Show(Sprite poster)
    {
        if (poster == null) return;

        if (posterImage != null)
        {
            posterImage.sprite        = poster;
            posterImage.preserveAspect = true;
            posterImage.enabled       = true;
        }

        if (panel != null) panel.SetActive(true);
        IsShowing = true;
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        if (posterImage != null) posterImage.sprite = null;
        IsShowing = false;
    }
}
