using UnityEngine;
using Unity.Cinemachine;

public class BoatCameraZoom : MonoBehaviour
{
    public CinemachineCamera cam;

    [Header("Scroll Zoom")]
    public float zoomSpeed = 10f;
    public float minFOV = 2f;
    public float maxFOV = 60f;

    [Header("Default / Sonar")]
    public float defaultFOV = 50f;
    public float sonarFOV = 40f;
    public float sonarTweenTime = 0.25f;

[Header("Whirl (Space)")]
[Range(0f, 1f)]
public float whirlMultiplier = 0.5f;
public float whirlTweenTime = 0.25f;


    [Header("Anchor")]
    public float anchorFOV = 35f;
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
            gameplayBaseFOV - scroll * zoomSpeed,
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
