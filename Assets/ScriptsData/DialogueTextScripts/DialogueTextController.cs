using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class DialogueTextController : MonoBehaviour
{
    public static DialogueTextController Instance;

    [Header("Linked Text Object")]
    [SerializeField] private TMP_Text dialogueText;

    [Header("Default Durations")]
    [SerializeField] private float defaultFadeIn = 1f;
    [SerializeField] private float defaultFadeOut = 1f;
    [SerializeField] private float defaultHold = 2f;

    [Header("Background Panel")]
    [SerializeField] private CanvasGroup dialogueBackground;
    [SerializeField] private float backgroundFadeDuration = 1f;

    [Header("Generated Fade Panel")]
    [Tooltip("How far up the screen the black fade reaches, as a share of screen height.")]
    [Range(0f, 1f)] [SerializeField] private float fadeHeight = 0.4f;
    [Tooltip("How dark the fade is at the bottom of the screen.")]
    [Range(0f, 1f)] [SerializeField] private float fadeBottomOpacity = 0.85f;
    [Tooltip("How quickly the fade clears going up. X: 0 = bottom, 1 = top of the fade. Y: darkness (scaled by Bottom Opacity).")]
    [SerializeField] private AnimationCurve fadeFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    private const int FadeTextureHeight = 256;

    private Coroutine currentRoutine;

    // The generated panel. Replaces the hand-built dialogueBackground, which is kept hidden.
    private CanvasGroup fadePanelGroup;
    private RectTransform fadePanelRect;
    private Texture2D fadeTexture;

    // ---------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (dialogueText != null)
        {
            Color c = dialogueText.color;
            c.a = 0f;
            dialogueText.color = c;
            dialogueText.gameObject.SetActive(true);
        }

        // The old hand-built panel stays hidden; the generated one takes its place.
        if (dialogueBackground != null)
            dialogueBackground.alpha = 0f;

        BuildFadePanel();
    }

    private void OnDestroy()
    {
        if (fadeTexture != null)
            Destroy(fadeTexture);
    }

    // Lets the fade be tuned live in play mode.
    private void OnValidate()
    {
        if (fadePanelRect != null)
            RefreshFadePanel();
    }

    // ---------------------------------------------------------
    // GENERATED FADE PANEL
    // ---------------------------------------------------------
    private void BuildFadePanel()
    {
        if (dialogueText == null) return;

        Canvas canvas = dialogueText.canvas;
        if (canvas == null)
        {
            Debug.LogWarning("DialogueTextController: dialogue text is not under a Canvas, no fade panel built.");
            return;
        }

        var go = new GameObject("DialogueFadePanel (generated)", typeof(RectTransform), typeof(CanvasGroup), typeof(RawImage));
        fadePanelRect = go.GetComponent<RectTransform>();
        fadePanelRect.SetParent(canvas.transform, false);
        fadePanelRect.SetAsFirstSibling(); // drawn first = behind the text

        fadePanelGroup = go.GetComponent<CanvasGroup>();
        fadePanelGroup.alpha = 0f;
        fadePanelGroup.interactable = false;
        fadePanelGroup.blocksRaycasts = false;

        fadeTexture = new Texture2D(1, FadeTextureHeight, TextureFormat.RGBA32, false)
        {
            name = "DialogueFadeGradient",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        RawImage image = go.GetComponent<RawImage>();
        image.texture = fadeTexture;
        image.raycastTarget = false;

        RefreshFadePanel();
    }

    private void RefreshFadePanel()
    {
        // Full width, from the bottom edge up to fadeHeight of the screen.
        fadePanelRect.anchorMin = Vector2.zero;
        fadePanelRect.anchorMax = new Vector2(1f, fadeHeight);
        fadePanelRect.offsetMin = Vector2.zero;
        fadePanelRect.offsetMax = Vector2.zero;

        // Row 0 is the bottom of the panel.
        var pixels = new Color32[FadeTextureHeight];
        for (int y = 0; y < FadeTextureHeight; y++)
        {
            float v = y / (float)(FadeTextureHeight - 1);
            float a = Mathf.Clamp01(fadeFalloff.Evaluate(v)) * fadeBottomOpacity;
            pixels[y] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
        }
        fadeTexture.SetPixels32(pixels);
        fadeTexture.Apply(false);
    }

    // ---------------------------------------------------------
    // PUBLIC API — EXTERNAL CONTROL
    // ---------------------------------------------------------

    public void PlayLine(string message)
    {
        PlayLine(message, defaultHold);
    }

    public void PlayLine(string message, float holdDuration)
    {
        StopCurrentRoutine();
        currentRoutine = StartCoroutine(
            ShowForRoutine(message, defaultFadeIn, holdDuration, defaultFadeOut)
        );
    }

    public void PlayLineFor(string message, float fadeIn, float hold, float fadeOut)
    {
        StopCurrentRoutine();
        currentRoutine = StartCoroutine(
            ShowForRoutine(message, fadeIn, hold, fadeOut)
        );
    }

    public void PlaySequence(string[] lines, float holdDuration)
    {
        if (lines == null || lines.Length == 0)
            return;

        StopCurrentRoutine();
        currentRoutine = StartCoroutine(
            SequenceRoutine(lines, holdDuration)
        );
    }

    public void PlaySequence(string[] lines, float[] holdDurations)
    {
        if (lines == null || holdDurations == null)
            return;

        if (lines.Length != holdDurations.Length)
        {
            Debug.LogWarning("DialogueTextController: Line and duration counts do not match.");
            return;
        }

        StopCurrentRoutine();
        currentRoutine = StartCoroutine(
            SequenceRoutine(lines, holdDurations)
        );
    }

    public void Hide()
    {
        StopCurrentRoutine();
        currentRoutine = StartCoroutine(FadeOutRoutine(defaultFadeOut));
    }

    /// <summary>
    /// Show a line and leave it up until something takes it down, rather than for a set beat.
    /// For dialogue whose length is the player's to decide — the angel's conversations hold until
    /// the talk key ends them. Brings the background panel up with it.
    /// </summary>
    public void ShowHeld(string message)
    {
        if (dialogueText == null) return;

        StopCurrentRoutine();
        currentRoutine = StartCoroutine(ShowHeldRoutine(message));
    }

    /// <summary>
    /// Take the whole thing down — text AND background panel.
    /// Hide() fades only the text, which is right mid-sequence (the sequence lowers the panel
    /// itself once it is done), but would leave the panel stranded on screen when dialogue is
    /// ended early. Anything that opens with ShowHeld should close with this.
    /// </summary>
    public void HideAll()
    {
        StopCurrentRoutine();
        currentRoutine = StartCoroutine(HideAllRoutine());
    }

    private IEnumerator ShowHeldRoutine(string message)
    {
        // Only raise the panel if it is not already up. FadeInBackground lerps from a hardcoded 0,
        // so calling it on an open panel would drop it to transparent and bring it back — a flicker
        // between every line of a conversation that steps through several.
        if (fadePanelGroup == null || fadePanelGroup.alpha < 0.999f)
            yield return FadeInBackground(backgroundFadeDuration);

        yield return FadeInRoutine(message, defaultFadeIn);

        // Nothing further to run: the line simply stays up until HideAll.
        currentRoutine = null;
    }

    private IEnumerator HideAllRoutine()
    {
        if (dialogueText != null)
            yield return FadeOutRoutine(defaultFadeOut);

        yield return FadeOutBackground(backgroundFadeDuration);
        currentRoutine = null;
    }

    // ---------------------------------------------------------
    // SEQUENCE ROUTINES
    // ---------------------------------------------------------
    private IEnumerator SequenceRoutine(string[] lines, float holdDuration)
    {
        yield return FadeInBackground(backgroundFadeDuration);

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            yield return FadeInRoutine(line, defaultFadeIn);
            yield return new WaitForSeconds(holdDuration);
            yield return FadeOutRoutine(defaultFadeOut);
            yield return new WaitForSeconds(0.25f);
        }

        yield return FadeOutBackground(backgroundFadeDuration);
    }

    private IEnumerator SequenceRoutine(string[] lines, float[] holdDurations)
    {
        yield return FadeInBackground(backgroundFadeDuration);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            yield return FadeInRoutine(line, defaultFadeIn);
            yield return new WaitForSeconds(holdDurations[i]);
            yield return FadeOutRoutine(defaultFadeOut);
            yield return new WaitForSeconds(0.25f);
        }

        yield return FadeOutBackground(backgroundFadeDuration);
    }

    // ---------------------------------------------------------
    // INTERNAL ROUTINES
    // ---------------------------------------------------------
    private IEnumerator FadeInRoutine(string message, float duration)
    {
        dialogueText.text = message;

        Color c = dialogueText.color;
        c.a = 0f;
        dialogueText.color = c;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Clamp01(t / duration);
            dialogueText.color = c;
            yield return null;
        }

        c.a = 1f;
        dialogueText.color = c;
    }

    private IEnumerator FadeOutRoutine(float duration)
    {
        Color c = dialogueText.color;
        float startA = c.a;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(startA, 0f, t / duration);
            dialogueText.color = c;
            yield return null;
        }

        c.a = 0f;
        dialogueText.color = c;
    }

    private IEnumerator FadeInBackground(float duration)
    {
        if (fadePanelGroup == null) yield break;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            fadePanelGroup.alpha = Mathf.Lerp(0f, 1f, t / duration);
            yield return null;
        }

        fadePanelGroup.alpha = 1f;
    }

    private IEnumerator FadeOutBackground(float duration)
    {
        if (fadePanelGroup == null) yield break;

        float start = fadePanelGroup.alpha;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            fadePanelGroup.alpha = Mathf.Lerp(start, 0f, t / duration);
            yield return null;
        }

        fadePanelGroup.alpha = 0f;
    }

    private IEnumerator ShowForRoutine(string message, float fadeIn, float hold, float fadeOut)
    {
        yield return FadeInRoutine(message, fadeIn);
        yield return new WaitForSeconds(hold);
        yield return FadeOutRoutine(fadeOut);
    }

    private void StopCurrentRoutine()
    {
        if (currentRoutine != null)
        {
            StopCoroutine(currentRoutine);
            currentRoutine = null;
        }
    }
}
