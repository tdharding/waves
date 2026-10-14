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

    // Press-any-key lines of the running sequence not yet pressed past, and the frame the last one
    // was — so whoever else is waiting on a key (a Press Any Key shot) knows that press was taken.
    private int keyLinesLeft;
    private int lastKeyAdvanceFrame = -1;

    /// <summary>True while the running sequence still has lines waiting on a key press.</summary>
    public bool KeyLinesPending => keyLinesLeft > 0;

    /// <summary>True on the frame a key press moved a line on — that press belongs to the line.</summary>
    public bool KeyTakenThisFrame => lastKeyAdvanceFrame == Time.frameCount;

    /// <summary>True once the running fading sequence's last line has faded out (before the panel lowers).</summary>
    public bool LinesDone { get; private set; }

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

    /// <param name="textFades">Off: each line appears and vanishes at once instead of fading.</param>
    public void PlaySequence(string[] lines, float[] holdDurations, bool textFades = true)
    {
        PlaySequence(lines, holdDurations, null, textFades);
    }

    /// <param name="waitForKey">Per line, optional. On: the line holds until any key is pressed instead of for its hold.</param>
    /// <param name="textFades">Off: each line appears and vanishes at once instead of fading.</param>
    public void PlaySequence(string[] lines, float[] holdDurations, bool[] waitForKey, bool textFades = true)
    {
        PlaySequence(lines, holdDurations, waitForKey, null, null, textFades);
    }

    /// <param name="fadeIns">Per line, optional. Off: that line appears at once instead of fading in. Only used when textFades is on.</param>
    /// <param name="fadeOuts">Per line, optional. Off: that line vanishes at once instead of fading out. Only used when textFades is on.</param>
    /// <param name="lowerPanel">Off: the black panel stays up after the last line, for the next sequence to carry on over.</param>
    public void PlaySequence(string[] lines, float[] holdDurations, bool[] waitForKey,
                             bool[] fadeIns, bool[] fadeOuts, bool textFades = true, bool lowerPanel = true)
    {
        if (lines == null || holdDurations == null)
            return;

        if (lines.Length != holdDurations.Length
            || (waitForKey != null && waitForKey.Length != lines.Length)
            || (fadeIns    != null && fadeIns.Length    != lines.Length)
            || (fadeOuts   != null && fadeOuts.Length   != lines.Length))
        {
            Debug.LogWarning("DialogueTextController: Line and duration counts do not match.");
            return;
        }

        StopCurrentRoutine();
        keyLinesLeft = CountKeyLines(lines, waitForKey, textFades);
        currentRoutine = StartCoroutine(
            SequenceRoutine(lines, holdDurations, waitForKey, fadeIns, fadeOuts, textFades, lowerPanel)
        );
    }

    public void Hide()
    {
        StopCurrentRoutine();
        currentRoutine = StartCoroutine(FadeOutRoutine(defaultFadeOut));
    }

    /// <summary>Take the text down and leave the black panel where it is.</summary>
    public void HideText(bool textFades = true)
    {
        if (dialogueText == null) return;
        StopCurrentRoutine();
        currentRoutine = StartCoroutine(FadeOutRoutine(textFades ? defaultFadeOut : 0f));
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
    public void HideAll(bool textFades = true)
    {
        StopCurrentRoutine();
        currentRoutine = StartCoroutine(HideAllRoutine(textFades));
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

    private IEnumerator HideAllRoutine(bool textFades)
    {
        // Text already gone (a sequence that ran to its end) — straight on to the panel.
        if (dialogueText != null && dialogueText.color.a > 0f)
            yield return FadeOutRoutine(textFades ? defaultFadeOut : 0f);

        yield return FadeOutBackground(textFades ? backgroundFadeDuration : 0f);
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

    private IEnumerator SequenceRoutine(string[] lines, float[] holdDurations, bool[] waitForKey,
                                        bool[] fadeIns, bool[] fadeOuts, bool textFades, bool lowerPanel)
    {
        if (!textFades)
        {
            yield return HeldSequenceRoutine(lines, holdDurations, waitForKey);
            yield break;
        }

        // Skip the raise if the panel is already up (left there by a no-fade sequence) — FadeInBackground
        // lerps from 0 and would drop it to transparent first.
        if (fadePanelGroup == null || fadePanelGroup.alpha < 0.999f)
            yield return FadeInBackground(backgroundFadeDuration);

        int last = -1;
        for (int i = 0; i < lines.Length; i++)
            if (!string.IsNullOrEmpty(lines[i].Trim())) last = i;

        for (int i = 0; i <= last; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            // A zero duration sets the text straight to shown / hidden.
            float fadeIn  = fadeIns  == null || fadeIns[i]  ? defaultFadeIn  : 0f;
            float fadeOut = fadeOuts == null || fadeOuts[i] ? defaultFadeOut : 0f;

            yield return FadeInRoutine(line, fadeIn);
            yield return HoldLine(i, holdDurations, waitForKey);
            yield return FadeOutRoutine(fadeOut);
            if (i < last)
                yield return new WaitForSeconds(0.25f);
        }

        LinesDone = true;
        if (lowerPanel)
            yield return FadeOutBackground(backgroundFadeDuration);
        currentRoutine = null;
    }

    /// <summary>
    /// No-fade sequence: the panel snaps up, each line replaces the last once its hold is up, and
    /// the final line (and panel) stays on screen until the next PlaySequence or HideAll.
    /// </summary>
    private IEnumerator HeldSequenceRoutine(string[] lines, float[] holdDurations, bool[] waitForKey)
    {
        if (fadePanelGroup != null) fadePanelGroup.alpha = 1f;

        int last = -1;
        for (int i = 0; i < lines.Length; i++)
            if (!string.IsNullOrEmpty(lines[i].Trim())) last = i;

        for (int i = 0; i <= last; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            yield return FadeInRoutine(line, 0f);
            if (i < last)
                yield return HoldLine(i, holdDurations, waitForKey);
        }

        currentRoutine = null;
    }

    /// <summary>Keep line i up for its hold, or until any key when it is a press-any-key line.</summary>
    private IEnumerator HoldLine(int i, float[] holdDurations, bool[] waitForKey)
    {
        if (waitForKey == null || !waitForKey[i])
        {
            yield return new WaitForSeconds(holdDurations[i]);
            yield break;
        }

        // A frame first, so the key that brought this line on (or ended the last one) doesn't end it too.
        yield return null;
        while (!Input.anyKeyDown)
            yield return null;

        lastKeyAdvanceFrame = Time.frameCount;
        keyLinesLeft = Mathf.Max(0, keyLinesLeft - 1);
    }

    /// <summary>Lines that will actually wait on a key — blank lines are skipped, and a no-fade
    /// sequence's last line stays up without waiting.</summary>
    private static int CountKeyLines(string[] lines, bool[] waitForKey, bool textFades)
    {
        if (waitForKey == null) return 0;

        int last = -1;
        for (int i = 0; i < lines.Length; i++)
            if (!string.IsNullOrEmpty(lines[i].Trim())) last = i;

        int count = 0;
        for (int i = 0; i <= last; i++)
        {
            if (!waitForKey[i] || string.IsNullOrEmpty(lines[i].Trim())) continue;
            if (!textFades && i == last) continue;
            count++;
        }
        return count;
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

        // Carry on from wherever the panel is, so a raise cut short and restarted doesn't flicker.
        float start = fadePanelGroup.alpha;
        duration *= 1f - start;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            fadePanelGroup.alpha = Mathf.Lerp(start, 1f, t / duration);
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
        keyLinesLeft = 0;
        LinesDone = false;
    }
}
