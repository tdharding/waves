using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class FishingController : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Dummy boat target in soul plane (mirrors real boat movement)")]
    public Transform dummyBoatTarget;

    [Header("Sonar")]
    public SonarSystemController sonar;

    [Header("Fishing Physics")]
    [Tooltip("Units per second fish are pulled")]
    public float pullSpeed = 4f;

    [Tooltip("Distance at which fish commits and is captured")]
    public float commitDistance = 0.6f;

    [Tooltip("Maximum distance at which fish can be attracted (at full sonar)")]
    public float fishingRange = 8f;

    // --------------------------------------------------
    // FX & VISUALS
    // --------------------------------------------------

    [Header("Visual FX")]
    [SerializeField] private WhirlFXController whirlFX;
    [SerializeField] private BoatCameraZoom cameraZoom;

    [Header("Souls Visuals")]
    [Tooltip("The parent object containing the static soul models on the boat")]
    public Transform soulsParent;

    [Header("Audio")]
    public SoulCaptureSFXPlayer soulCaptureSFX;

    // --------------------------------------------------
    // STATE
    // --------------------------------------------------

    [Header("Debug State")]
    [SerializeField] private bool fishingActive;

    public bool IsFishingActive => fishingActive;

    public float CurrentFishingRange
    {
        get
        {
            if (sonar == null || !sonar.IsSonarActive)
                return 0f;

            float t = Mathf.Clamp01(sonar.CurrentNormalizedRadius);
            return fishingRange * t;
        }
    }

    private readonly List<FishFishingBehaviour> registeredFish = new();

    // --------------------------------------------------
    // THE CAPTURE RELAY
    // --------------------------------------------------

    public void OnFishCaptured(LinkIdentityLabel fishLabel)
    {
        if (fishLabel == null) return;

        soulCaptureSFX?.PlayRandomCapture();
        ActivateNextSoulVisual();

        bool videoEnabled = LevelDataController.Instance == null || LevelDataController.Instance.EnableVideoPlayback;
        if (videoEnabled)
        {
            if (VideoPlayerController.Instance != null)
                VideoPlayerController.Instance.PlaySoulVideo(fishLabel.soulDataIdentity);
            else
                Debug.LogWarning("[FishingController] VideoPlayerController.Instance is missing!");
        }

        if (LevelSoulTracker.Instance != null)
            LevelSoulTracker.Instance.AddSoulToBoat(fishLabel.linkID, fishLabel.soulDataIdentity);

        // sonar?.DeactivateSonar();
    }

    // --------------------------------------------------
    // REGISTRATION & BOAT CONTROL
    // --------------------------------------------------

    public void RegisterFish(FishFishingBehaviour fish)
    {
        if (fish == null) return;
        if (!registeredFish.Contains(fish))
            registeredFish.Add(fish);
    }

    public void UnregisterFish(FishFishingBehaviour fish)
    {
        if (fish == null) return;
        registeredFish.Remove(fish);
    }

    public void SetFishingActive(bool active)
    {
        if (fishingActive == active) return;

        fishingActive = active;

        if (active)
        {
            // Deploy is initiated via whirlFX.Deploy() externally
            // Fish are only notified once WhirlFX fires OnDeployNetComplete
            cameraZoom?.SetWhirlZoom(true);

            for (int i = 0; i < registeredFish.Count; i++)
                if (registeredFish[i] != null)
                    registeredFish[i].OnFishingStarted(this);
        }
        else
        {
            whirlFX?.Retract();
            cameraZoom?.SetWhirlZoom(false);

            for (int i = 0; i < registeredFish.Count; i++)
                if (registeredFish[i] != null)
                    registeredFish[i].OnFishingStopped();
        }
    }

    public void StartFishing()
    {
        whirlFX?.Deploy();
    }

    // --------------------------------------------------
    // SOUL VISUAL MANAGEMENT
    // --------------------------------------------------

    private void ActivateNextSoulVisual()
    {
        if (soulsParent == null) return;

        foreach (Transform child in soulsParent)
        {
            if (!child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(true);
                return;
            }
        }
    }

    public void RestoreSoulVisuals(int count)
    {
        if (soulsParent == null) return;

        int activated = 0;
        foreach (Transform child in soulsParent)
        {
            if (activated >= count) break;

            if (!child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(true);
                activated++;
            }
        }
        Debug.Log($"[FishingController] Restored {activated} soul visuals on boat.");
    }

    // --------------------------------------------------
    // SETUP & SAFETY
    // --------------------------------------------------

    public void SetWhirlFX(WhirlFXController fx) => whirlFX = fx;

    void OnDisable()
    {
        fishingActive = false;
        whirlFX?.DecreaseWhirl();
    }
}