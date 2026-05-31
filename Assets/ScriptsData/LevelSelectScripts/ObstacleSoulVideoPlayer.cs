using UnityEngine;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

public class ObstacleSoulVideoPlayer : MonoBehaviour
{
    [Header("Slots (match order in ObstacleManager)")]
    [SerializeField] private List<SoulSlot> slots = new List<SoulSlot>();

    [Header("Video Player")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Header("Material")]
    [SerializeField] private Renderer obstacleRenderer;
    [SerializeField] private string blendProperty = "_VideoBlend";
    [SerializeField] private string fresnelProperty = "_FresnelPower";
    [SerializeField] private float targetIntensity = 3f;
    [SerializeField] private float targetFresnelPower = 2f;

    [Header("Timing")]
    [SerializeField] private float switchInterval = 5f;
    [SerializeField] private float crossfadeDuration = 0.5f;
    [SerializeField] private float fadeInDuration = 1f;

    private int blendPropID;
    private int fresnelPropID;
    private Material matInstance;
    private List<VideoClip> clips = new List<VideoClip>();
    private int currentIndex = 0;
    private Coroutine cycleRoutine;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (obstacleRenderer != null)
        {
            matInstance = obstacleRenderer.material;
            blendPropID = Shader.PropertyToID(blendProperty);
            fresnelPropID = Shader.PropertyToID(fresnelProperty);
            matInstance.SetFloat(blendPropID, 0f);
            matInstance.SetFloat(fresnelPropID, 0f);
        }
    }

    public void RefreshState()
    {
        if (!gameObject.activeInHierarchy) return;

        List<VideoClip> newClips = new List<VideoClip>();

        foreach (SoulSlot slot in slots)
        {
            if (slot != null && slot.IsFilled)
            {
                VideoClip clip = VideoPlayerController.Instance?.GetClipForSoul(slot.SoulIdentity);
                if (clip != null) newClips.Add(clip);
            }
        }

        // Case 1: All souls removed
        if (newClips.Count == 0)
        {
            clips.Clear();
            StopCycle();
            TriggerFade(0f, crossfadeDuration);
            return;
        }

        // Case 2: New souls added or changed
        clips = newClips;

        if (cycleRoutine != null) StopCoroutine(cycleRoutine);
        cycleRoutine = StartCoroutine(CycleRoutine());
    }

    private IEnumerator CycleRoutine()
    {
        float currentBlend = matInstance.GetFloat(blendPropID);
        if (currentBlend < 0.1f)
        {
            PlayClip(clips[0]);
            yield return StartCoroutine(FadeTo(targetIntensity, targetFresnelPower, fadeInDuration));
        }

        currentIndex = 0;

        if (clips.Count == 1)
        {
            PlayClip(clips[0]);
            videoPlayer.isLooping = true;
            yield break;
        }

        while (true)
        {
            yield return new WaitForSeconds(switchInterval);
            currentIndex = (currentIndex + 1) % clips.Count;
            PlayClip(clips[currentIndex]);
        }
    }

    private void PlayClip(VideoClip clip)
    {
        if (videoPlayer.clip == clip && videoPlayer.isPlaying) return;
        videoPlayer.Stop();
        videoPlayer.clip = clip;
        videoPlayer.isLooping = (clips.Count == 1);
        videoPlayer.Play();
    }

    private void TriggerFade(float target, float duration)
    {
        float fresnelTarget = target > 0f ? targetFresnelPower : 0f;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeTo(target, fresnelTarget, duration));
    }

    private IEnumerator FadeTo(float blendTarget, float fresnelTarget, float duration)
    {
        if (matInstance == null) yield break;
        float elapsed = 0f;
        float startBlend = matInstance.GetFloat(blendPropID);
        float startFresnel = matInstance.GetFloat(fresnelPropID);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            matInstance.SetFloat(blendPropID, Mathf.Lerp(startBlend, blendTarget, t));
            matInstance.SetFloat(fresnelPropID, Mathf.Lerp(startFresnel, fresnelTarget, t));
            yield return null;
        }

        matInstance.SetFloat(blendPropID, blendTarget);
        matInstance.SetFloat(fresnelPropID, fresnelTarget);
    }

    private void StopCycle()
    {
        if (cycleRoutine != null) StopCoroutine(cycleRoutine);
        videoPlayer.Stop();
    }
}
