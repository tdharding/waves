using UnityEngine;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

public class VideoPlayerController : MonoBehaviour
{
    public static VideoPlayerController Instance;

    [Header("References")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private Material targetMaterial;

    [Header("Reveal Settings (Capture)")]
    [SerializeField] private string factorProperty = "_Factor";
    [SerializeField] private float revealTarget = 1f;
    [SerializeField] private float defaultFactor = 0f;
    [SerializeField] private float revealDuration = 0.5f;
    [SerializeField] private float hideDuration = 0.5f;

    [Header("Preview Settings (Mouse Over)")]
    [Tooltip("Lower factor for a more subtle ghost-like preview")]
    [SerializeField] private float previewFactor = 0.4f; 
    [SerializeField] private float previewFadeTime = 0.25f;

    private Dictionary<int, SoulData> soulDatabase = new Dictionary<int, SoulData>();
    private bool isPreviewing = false;

    private void Awake()
    {
        Instance = this;
        LoadSoulDatabase();

        if (targetMaterial != null)
            targetMaterial.SetFloat(factorProperty, defaultFactor);
    }

    private void LoadSoulDatabase()
    {
        SoulData[] allSouls = Resources.LoadAll<SoulData>("Souls");
        foreach (var soul in allSouls)
        {
            if (!soulDatabase.ContainsKey(soul.soulDataIdentity))
                soulDatabase.Add(soul.soulDataIdentity, soul);
        }
        Debug.Log($"[VideoDebug] Database initialized with {soulDatabase.Count} souls.");
    }

    // ─────────────────────────────────────────────
    // CAPTURE MODE (One-shot, full brightness)
    // ─────────────────────────────────────────────
    public void PlaySoulVideo(int identity)
    {
        if (soulDatabase.TryGetValue(identity, out SoulData data))
        {
            isPreviewing = false; // Interrupt preview mode
            StopAllCoroutines();
            videoPlayer.isLooping = false; // Play once
            StartCoroutine(PlayVideoRevealRoutine(data.videoClip, revealTarget, revealDuration, true));
        }
    }

    // ─────────────────────────────────────────────
    // PREVIEW MODE (Looping, subtle)
    // ─────────────────────────────────────────────
    public void StartPreview(int identity)
    {
        if (soulDatabase.TryGetValue(identity, out SoulData data))
        {
            isPreviewing = true;
            StopAllCoroutines();
            videoPlayer.isLooping = true; // Loop the preview
            StartCoroutine(PlayVideoRevealRoutine(data.videoClip, previewFactor, previewFadeTime, false));
        }
    }

    public void StopPreview()
    {
        if (isPreviewing)
        {
            isPreviewing = false;
            StopAllCoroutines();
            StartCoroutine(FadeOutRoutine());
        }
    }

    // Generalized routine to handle both modes
    private IEnumerator PlayVideoRevealRoutine(VideoClip clip, float targetVal, float duration, bool autoHide)
    {
        if (videoPlayer == null || targetMaterial == null || clip == null) yield break;

        videoPlayer.Stop();
        videoPlayer.clip = null; // evict old clip immediately
        targetMaterial.SetFloat(factorProperty, defaultFactor);
        videoPlayer.clip = clip;
        videoPlayer.Play();

        // Only hide automatically if it's a capture reveal
        videoPlayer.loopPointReached -= OnVideoFinished;
        if (autoHide) videoPlayer.loopPointReached += OnVideoFinished;

        float t = 0f;
        float start = targetMaterial.GetFloat(factorProperty);

        while (t < duration)
        {
            t += Time.deltaTime;
            targetMaterial.SetFloat(factorProperty, Mathf.Lerp(start, targetVal, t / duration));
            yield return null;
        }
        targetMaterial.SetFloat(factorProperty, targetVal);
    }

    private void OnVideoFinished(VideoPlayer vp)
    {
        vp.loopPointReached -= OnVideoFinished;
        StartCoroutine(FadeOutRoutine());
    }

    private IEnumerator FadeOutRoutine()
    {
        if (targetMaterial == null) yield break;

        float t = 0f;
        float start = targetMaterial.GetFloat(factorProperty);
        while (t < hideDuration)
        {
            t += Time.deltaTime;
            targetMaterial.SetFloat(factorProperty, Mathf.Lerp(start, defaultFactor, t / hideDuration));
            yield return null;
        }
    targetMaterial.SetFloat(factorProperty, defaultFactor);
    videoPlayer.Stop();
    videoPlayer.clip = null; // clear the clip so no stale frame lingers
    }

    // Add this inside VideoPlayerController.cs

/// <summary>
/// Returns the VideoClip associated with a specific soul identity from the database.
/// Used by obstacles to display souls once they are unlocked.
/// </summary>
public VideoClip GetClipForSoul(int identity)
{
    if (soulDatabase.TryGetValue(identity, out SoulData data))
    {
        return data.videoClip;
    }

    Debug.LogWarning($"[VideoPlayerController] No SoulData found for identity: {identity}");
    return null;
}
}