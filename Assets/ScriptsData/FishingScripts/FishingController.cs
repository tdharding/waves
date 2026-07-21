using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class FishingController : MonoBehaviour
{
    [Header("Refs")]
    public Transform boatTransform;

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
    [SerializeField] private SoulWhirlDirection soulWhirlDirection;
    [SerializeField] private SecondWhirlChain secondWhirlChain;
    [SerializeField] private BoatCameraZoom cameraZoom;

    [Header("Fish Travel")]
    [SerializeField] private float fishTravelSpeed = 1.5f;

    [Header("Whirl Shader")]
    [SerializeField] private float pinchStrengthMin = 0f;
    [SerializeField] private float pinchStrengthMax = 1.5f;
    [SerializeField] private float whirlRadiusMin   = 0f;
    [SerializeField] private float whirlRadiusMax   = 6f;
    [SerializeField] private float whirlShaderTransitionSpeed = 3f;

    [Header("Souls Visuals")]
    [Tooltip("The parent object containing the static soul models on the boat")]
    public Transform soulsParent;

    [Header("Glow FX")]
    [SerializeField] private Transform captureGlowPoint;

    [Header("Toggle Visuals")]
    [Tooltip("Extra ring visual shown only while sonar mode is active")]
    [SerializeField] private GameObject sonarRing;

    [Tooltip("Inner whirl skinned mesh; disabled until the whirl activates (Space pressed)")]
    [SerializeField] private SkinnedMeshRenderer innerFishWhirlRenderer;

    [Tooltip("When sonar is closing and its normalized radius drops to/below this, the inner whirl mesh is force-disabled")]
    [SerializeField] private float sonarCloseThreshold = 0.02f;

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

    // Read-only view for systems that react to live fish positions (e.g. WindowLightManager).
    public IReadOnlyList<FishFishingBehaviour> RegisteredFish => registeredFish;
    private readonly List<Transform> tubeDeliveryTransforms = new();

    public void RegisterTubeDelivery(Transform t)   { if (t != null && !tubeDeliveryTransforms.Contains(t)) tubeDeliveryTransforms.Add(t); }
    public void UnregisterTubeDelivery(Transform t) { tubeDeliveryTransforms.Remove(t); }

    private bool _sonarRingShown;
    // True while the whirl is deployed (Space press → retract complete): hides the sonar ring
    private bool _ringSuppressedByWhirl;

    void Start()
    {
        if (soulWhirlDirection != null)
            soulWhirlDirection.travelSpeed = fishTravelSpeed;

        if (innerFishWhirlRenderer != null)
            innerFishWhirlRenderer.enabled = false;

        if (sonarRing != null)
            sonarRing.SetActive(false);
    }

    // --------------------------------------------------
    // HOOVER GLOW SHADER GLOBALS
    // --------------------------------------------------

    private static readonly int HooverFishPointsID = Shader.PropertyToID("_HooverFishPoints");
    private static readonly int HooverFishCountID  = Shader.PropertyToID("_HooverFishCount");
    private readonly Vector4[]  _hooverFishBuffer  = new Vector4[8];

    // --------------------------------------------------
    // THE CAPTURE RELAY
    // --------------------------------------------------

    public void OnFishCaptured(LinkIdentityLabel fishLabel)
    {
        if (fishLabel == null) return;

        soulCaptureSFX?.PlayRandomCapture();
        ActivateNextSoulVisual();

        if (captureGlowPoint != null)
            BrightnessGlowController.TriggerCaptureGlow(captureGlowPoint);

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
            soulWhirlDirection?.SetFishingActive(true, pinchStrengthMin, pinchStrengthMax, whirlRadiusMin, whirlRadiusMax, whirlShaderTransitionSpeed);
            secondWhirlChain?.SetActive(true);

            for (int i = 0; i < registeredFish.Count; i++)
                if (registeredFish[i] != null)
                    registeredFish[i].OnFishingStarted(this);
        }
        else
        {
            // Inner whirl mesh is NOT disabled here — it stays visible through the
            // retract animation and is switched off by OnRetractComplete() when the
            // RetractNetBoat clip fires its end-of-animation event.
            whirlFX?.Retract();
            cameraZoom?.SetWhirlZoom(false);
            soulWhirlDirection?.SetFishingActive(false, pinchStrengthMin, pinchStrengthMax, whirlRadiusMin, whirlRadiusMax, whirlShaderTransitionSpeed);
            secondWhirlChain?.SetActive(false);

            for (int i = 0; i < registeredFish.Count; i++)
                if (registeredFish[i] != null)
                    registeredFish[i].OnFishingStopped();
        }
    }

    public void StartFishing()
    {
        if (innerFishWhirlRenderer != null)
            innerFishWhirlRenderer.enabled = true;

        // Hide the sonar ring for the duration of the whirl (until retract completes)
        _ringSuppressedByWhirl = true;

        whirlFX?.Deploy();
    }

    // Called once the RetractNetBoat animation finishes (relayed via WhirlFXController).
    // Guarded so a fresh StartFishing during the retract won't wrongly hide the mesh.
    public void OnRetractComplete()
    {
        if (innerFishWhirlRenderer != null && !fishingActive)
            innerFishWhirlRenderer.enabled = false;

        // Whirl is fully retracted — allow the sonar ring back (LateUpdate re-shows it if sonar is still active)
        _ringSuppressedByWhirl = false;
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
    public void SetSoulWhirlDirection(SoulWhirlDirection swd) => soulWhirlDirection = swd;

    void LateUpdate()
    {
        // Sonar ring shows while sonar is active, but is hidden for the duration of the whirl
        bool sonarOn = sonar != null && sonar.IsSonarActive;
        bool ringShouldShow = sonarOn && !_ringSuppressedByWhirl;
        if (sonarRing != null && ringShouldShow != _sonarRingShown)
        {
            _sonarRingShown = ringShouldShow;
            sonarRing.SetActive(ringShouldShow);
        }

        // Safety: once the sonar has gone inactive and its radius has closed to ~0,
        // force the inner whirl mesh off regardless of the retract flow. Gated on
        // !sonarOn so it never fires during the brief near-zero window while opening.
        if (innerFishWhirlRenderer != null && innerFishWhirlRenderer.enabled
            && !sonarOn && sonar != null
            && sonar.CurrentNormalizedRadius <= sonarCloseThreshold)
        {
            innerFishWhirlRenderer.enabled = false;
        }

        int count = 0;
        for (int i = 0; i < registeredFish.Count && count < 8; i++)
        {
            var fish = registeredFish[i];
            if (fish != null && fish.IsTravelingTube)
                _hooverFishBuffer[count++] = fish.transform.position;
        }

        for (int i = 0; i < tubeDeliveryTransforms.Count && count < 8; i++)
        {
            if (tubeDeliveryTransforms[i] != null)
                _hooverFishBuffer[count++] = tubeDeliveryTransforms[i].position;
        }

        Shader.SetGlobalVectorArray(HooverFishPointsID, _hooverFishBuffer);
        Shader.SetGlobalFloat(HooverFishCountID, count);
    }

    void OnDisable()
    {
        fishingActive = false;
        whirlFX?.DecreaseWhirl();

        if (innerFishWhirlRenderer != null)
            innerFishWhirlRenderer.enabled = false;

        if (sonarRing != null)
        {
            sonarRing.SetActive(false);
            _sonarRingShown = false;
        }
        _ringSuppressedByWhirl = false;

        Shader.SetGlobalFloat(HooverFishCountID, 0f);
    }
}