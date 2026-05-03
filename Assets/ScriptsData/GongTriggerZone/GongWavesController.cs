using UnityEngine;
using Unity.Cinemachine;

public class GongWavesController : MonoBehaviour
{


    // ─────────────────────────────────────────────
    // CAMERAS + ANIMATOR
    // ─────────────────────────────────────────────
    [Header("Cinemachine Cameras")]
    public CinemachineCamera boatFollowCam;
    public CinemachineCamera gongCam;

    [Header("Gong Cinematic Animator Object")]
    public GameObject gongCamAnimatorGO;

    [Header("Gong Cinematic Animator")]
    public Animator gongAnimator;

    // ─────────────────────────────────────────────
    // WAVE MATERIAL CONTROL
    // ─────────────────────────────────────────────
    [Header("Wave Controller")]
    public WaveMaterialController waveController;

    private WavePreset gongWavePreset;




    // ─────────────────────────────────────────────
    // GAMEPLAY SCRIPTS
    // ─────────────────────────────────────────────
    [Header("Gameplay Scripts")]
    public BoatMovement BoatMovementScript;
    public SteeringWheelControllerV2 SteeringWheelControllerScript;
    public BoatAnchorController AnchorScript;

    // ─────────────────────────────────────────────
    // UI ELEMENTS
    // ─────────────────────────────────────────────
    [Header("UI Elements")]
    public GameObject WheelUIGameObject;
    public GameObject AnchorUI;

    [Header("UI Overlay Camera")]
    public UIOverlayCameraController uiOverlayCamera;

    [Header("Virtual Cursor")]
    public VirtualCursor virtualCursor;

    // ─────────────────────────────────────────────
    // CAMERA ZOOM
    // ─────────────────────────────────────────────
    [Header("Camera Zoom Controller")]
    public BoatCameraZoom zoomController;

    // ─────────────────────────────────────────────
    // AUDIO + MUSIC
    // ─────────────────────────────────────────────
    [Header("Audio")]
    public AudioSource gongAudio;


    // ─────────────────────────────────────────────
    // BOAT VARIABLE OVERRIDES
    // ─────────────────────────────────────────────
    [Header("Boat Speed Overrides (Applied After Gong)")]
    public float newBaseMoveSpeed = 0.14f;
    public float newBoostedMoveSpeed = 0.24f;
    public float newMaxTurnSpeed = 120f;

    // ─────────────────────────────────────────────
    // SKIP STATE
    // ─────────────────────────────────────────────
    private bool isGongCinematicActive = false;
    private bool hasFinishedGongSequence = false;



    // ====================================================================
    // UPDATE
    // ====================================================================
    private void Update()
    {
        if (!isGongCinematicActive || hasFinishedGongSequence)
            return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            SkipGongCinematic();
        }
    }

    // ====================================================================
    // START OF THE GONG SEQUENCE
    // ====================================================================
        public void StartGongSequence()
    {
        if (gongAnimator != null)
            gongAnimator.enabled = true;

        isGongCinematicActive = true;
        hasFinishedGongSequence = false;

        LevelAudioController.Instance?.StopMusic();

        BoatMovementScript.controlsEnabled = false;
        AnchorScript.enabled = false;
        SteeringWheelControllerScript.DisableWheel();
        AnchorUI.SetActive(false);

        uiOverlayCamera?.HideUI();

        if (waveController == null)
            return;

        if (gongWavePreset != null)
        {
            LevelDataController.Instance?.SetActiveWavePreset(gongWavePreset);
            StartCoroutine(waveController.TransitionToPreset(gongWavePreset));
        }
        else
            Debug.LogWarning("No gongWavePreset assigned.");

        boatFollowCam.gameObject.SetActive(false);
        gongCam.gameObject.SetActive(true);
        gongCamAnimatorGO.SetActive(true);
        LevelDataController.Instance?.ShowSkipInstruction();
    }


    public void SetGongWavePreset(WavePreset preset)
    {
        gongWavePreset = preset;
    }



    // ====================================================================
    // GONG STRIKE SOUND
    // ====================================================================
    public void PlayGongSound()
    {
        gongAudio?.Play();
    }

    // ====================================================================
    // END OF GONG SEQUENCE
    // ====================================================================
    public void OnSecondGongSequenceFinished()
    {
        if (hasFinishedGongSequence)
            return;

        hasFinishedGongSequence = true;
        isGongCinematicActive = false;

        gongCam.gameObject.SetActive(false);
        gongCamAnimatorGO.SetActive(false);
        boatFollowCam.gameObject.SetActive(true);

        AnchorUI.SetActive(true);
        uiOverlayCamera?.ShowUI();

        virtualCursor.ResetPosition();
        zoomController.ResetToDefaultFOV();

        BoatMovementScript.controlsEnabled = true;

        BoatMovementScript.baseMoveSpeed = newBaseMoveSpeed;
        BoatMovementScript.boostedMoveSpeed = newBoostedMoveSpeed;
        BoatMovementScript.maxTurnSpeed = newMaxTurnSpeed;

        AnchorScript.enabled = true;
        AnchorScript.InitializeAnchorState();

        SteeringWheelControllerScript.EnableWheel();

        LevelAudioController.Instance?.PlayMusic();

          LevelDataController.Instance?.HideSkipInstruction();
    }

    // ====================================================================
    // SKIP
    // ====================================================================
private void SkipGongCinematic()
{
    if (hasFinishedGongSequence)
        return;

    if (gongAnimator != null)
    {
        gongAnimator.Play(0, 0, 0f);
        gongAnimator.Update(0f);
        gongAnimator.enabled = false;
    }

    StopAllCoroutines();

    if (waveController != null && gongWavePreset != null)
    {
        LevelDataController.Instance?.SetActiveWavePreset(gongWavePreset);
        waveController.ApplyPresetInstant(gongWavePreset);
    }

    gongCam.gameObject.SetActive(false);
    gongCamAnimatorGO.SetActive(false);

    OnSecondGongSequenceFinished();

     LevelDataController.Instance?.HideSkipInstruction();
}

}
