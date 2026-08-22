using UnityEngine;
using Unity.Cinemachine;

public class BoatCameraZoom : MonoBehaviour
{
    [Tooltip("Camera whose lens is driven. Assigned at spawn by CameraController.SetTargets.")]
    public CinemachineCamera cam;

    [Header("Scroll Zoom")]
    [Tooltip("Degrees of FOV per notch of the scroll wheel, before Scroll FOV Weight is applied.")]
    public float zoomSpeed = 10f;
    [Tooltip("Narrowest lens. Also the FOV the whirl (Space) zoom drives toward.")]
    public float minFOV = 2f;
    [Tooltip("Widest lens the scroll zoom can reach.")]
    public float maxFOV = 60f;

    [Tooltip("How much of the scroll zoom comes from narrowing the lens. The rest is the camera " +
             "actually moving closer (CameraController's Manual Distance). 0 = pure camera movement.")]
    [Range(0f, 1f)] public float scrollFOVWeight = 0.2f;

    [Header("Default / Sonar")]
    [Tooltip("Starting FOV for normal play. Narrow values look telephoto and flatten the sense of " +
             "moving closer — raise this if the dolly zoom feels weak.")]
    public float defaultFOV = 14f;
    [Tooltip("FOV while sonar is running — wider, to take in more of the revealed level.")]
    public float sonarFOV = 40f;
    [Tooltip("Seconds to tween in and out of the sonar FOV.")]
    public float sonarTweenTime = 0.25f;

[Header("Whirl (Space)")]
[Tooltip("How far toward Min FOV the whirl pushes the lens. 1 = all the way in.")]
[Range(0f, 1f)]
public float whirlMultiplier = 0.5f;
[Tooltip("Seconds to tween the whirl zoom in and out.")]
public float whirlTweenTime = 0.25f;


    [Header("Anchor")]
    [Tooltip("FOV held while the anchor is down.")]
    public float anchorFOV = 35f;
    [Tooltip("Seconds to tween in and out of the anchor FOV.")]
    public float anchorTweenTime = 0.25f;

    [Header("Soul Camera")]
    public Camera soulCamera;
    [Tooltip("Soul camera FOV as a multiplier of the main camera FOV (e.g. 0.66 = soul tracks at 66% of main)")]
    public float soulFOVScale = 0.66f;

    // ─────────────────────────────
    float gameplayBaseFOV;

    float sonarT, sonarFrom, sonarTo, sonarTimer;
    float preSonarFOV;
    float whirlT, whirlFrom, whirlTo, whirlTimer;

    bool anchorMode;
    float anchorFrom, anchorTo, anchorTimer;

    public float ZoomT { get; private set; }

    // Current camera FOV, and a 0..1 zoom amount (0 = fully zoomed out at maxFOV,
    // 1 = fully zoomed in at minFOV). Used by the Map UI to scale its imagery with zoom.
    public float CurrentFOV    => cam != null ? cam.Lens.FieldOfView : defaultFOV;
    public float NormalizedZoom => Mathf.InverseLerp(maxFOV, minFOV, CurrentFOV);

    void Start()
    {
        gameplayBaseFOV = defaultFOV;
        ApplyFinalFOV();
    }

    void Update()
    {
        if (PauseManager.IsPaused || cam == null)
            return;

        UpdateScroll();
        UpdateSonar();
        UpdateWhirl();
        UpdateAnchor();

        ApplyFinalFOV();
    }

    // ─────────────────────────────
    void UpdateScroll()
    {
        if (anchorMode || sonarT > 0f)
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.01f)
            return;

        gameplayBaseFOV = Mathf.Clamp(
            gameplayBaseFOV - scroll * zoomSpeed * scrollFOVWeight,
            minFOV,
            maxFOV
        );
    }

    public void AssignCamera(CinemachineCamera assignedCam)
{
    cam = assignedCam;
}

    // ─────────────────────────────
    public void SetSonarZoom(bool active)
    {
        if (active)
            preSonarFOV = gameplayBaseFOV; // remember current zoom before sonar

        sonarFrom  = sonarT;
        sonarTo    = active ? 1f : 0f;
        sonarTimer = 0f;
    }

    void UpdateSonar()
    {
        if (Mathf.Approximately(sonarT, sonarTo))
            return;

        sonarTimer += Time.deltaTime;
        float t = Mathf.Clamp01(sonarTimer / sonarTweenTime);
        sonarT = Mathf.Lerp(sonarFrom, sonarTo, t);

        gameplayBaseFOV = Mathf.Lerp(preSonarFOV, sonarFOV, sonarT);
    }

    // ─────────────────────────────
    public void SetWhirlZoom(bool active)
    {
        whirlFrom = whirlT;
        whirlTo = active ? 1f : 0f;
        whirlTimer = 0f;
    }

    void UpdateWhirl()
    {
        if (Mathf.Approximately(whirlT, whirlTo))
            return;

        whirlTimer += Time.deltaTime;
        float t = Mathf.Clamp01(whirlTimer / whirlTweenTime);
        whirlT = Mathf.Lerp(whirlFrom, whirlTo, t);
    }

    // ─────────────────────────────
    public void ApplyAnchorZoom(bool down)
    {
        anchorMode = down;
        anchorFrom = gameplayBaseFOV;
        anchorTo = down ? anchorFOV : defaultFOV;
        anchorTimer = 0f;
    }

    void UpdateAnchor()
    {
        if (!anchorMode && anchorTimer <= 0f)
            return;

        anchorTimer += Time.deltaTime;
        float t = Mathf.Clamp01(anchorTimer / anchorTweenTime);

        ZoomT = t;
        gameplayBaseFOV = Mathf.Lerp(anchorFrom, anchorTo, t);

        if (t >= 1f)
            anchorTimer = 0f;
    }

    // ─────────────────────────────
    void ApplyFinalFOV()
    {
        float whirlAmount = whirlMultiplier * whirlT;

float finalFOV = Mathf.Lerp(
    gameplayBaseFOV,
    minFOV,
    whirlAmount
);


        finalFOV = Mathf.Clamp(finalFOV, minFOV, maxFOV);

        cam.Lens.FieldOfView = finalFOV;
        SyncSoulCamera(finalFOV);
    }

    void SyncSoulCamera(float fov)
    {
        if (!soulCamera)
            return;

        soulCamera.fieldOfView = fov * soulFOVScale;
    }

    // ─────────────────────────────
    public void ResetToDefaultFOV()
    {
        sonarT = whirlT = 0f;
        gameplayBaseFOV = defaultFOV;
        ApplyFinalFOV();
    }
}
