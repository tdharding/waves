using UnityEngine;
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

    private Coroutine currentRoutine;

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

        if (dialogueBackground != null)
            dialogueBackground.alpha = 0f;
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
        if (dialogueBackground == null || dialogueBackground.alpha < 0.999f)
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
        if (dialogueBackground == null) yield break;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            dialogueBackground.alpha = Mathf.Lerp(0f, 1f, t / duration);
            yield return null;
        }

        dialogueBackground.alpha = 1f;
    }

    private IEnumerator FadeOutBackground(float duration)
    {
        if (dialogueBackground == null) yield break;

        float start = dialogueBackground.alpha;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            dialogueBackground.alpha = Mathf.Lerp(start, 0f, t / duration);
            yield return null;
        }

        dialogueBackground.alpha = 0f;
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
