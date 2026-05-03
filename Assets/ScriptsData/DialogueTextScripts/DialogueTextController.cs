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
