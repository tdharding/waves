using UnityEngine;
using Unity.Cinemachine;

public class LevelSelectOpeningSequence : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LevelSelectBoatControl boatControl;
    [SerializeField] private LevelSelectCameraController normalCamera;
    [SerializeField] private Transform handTransform;
    [SerializeField] private Animator handAnimator;
    [SerializeField] private Collider barrierCollider;

    [Header("Audio")]
    [SerializeField] private AudioSource windSource;
    [SerializeField] private AudioClip windClip;
    [SerializeField] private float audioFadeDuration = 2f;

    private enum State { Idle, Moving, Complete }
    private State _state = State.Complete;
    private Vector3 _initialOffset;

    private void Awake()
    {
        if (boatControl == null)
            boatControl = FindFirstObjectByType<LevelSelectBoatControl>();
        if (normalCamera == null)
            normalCamera = FindFirstObjectByType<LevelSelectCameraController>();

        // If this is the first time, prevent the normal music from starting automatically
        if (string.IsNullOrEmpty(GameProgressData.GetBoatSegmentID()))
        {
            var music = FindObjectOfType<LevelSelectMusicController>();
            if (music != null) music.playOnStart = false;
        }
    }

    private void Start()
    {
        bool isFirstTime = string.IsNullOrEmpty(GameProgressData.GetBoatSegmentID());

        if (!isFirstTime)
        {
            CompleteIntroSequence(save: false);
            return;
        }

        _state = State.Idle;

        if (windSource != null && windClip != null)
        {
            windSource.clip = windClip;
            windSource.loop = true;
            windSource.Play();
        }

        if (handTransform != null && boatControl != null)
        {
            _initialOffset = handTransform.position - boatControl.BoatTransform.position;
        }

        boatControl.IntroMode    = true;
        boatControl.ControlsFrozen = true;

        if (normalCamera != null) normalCamera.IsControlEnabled = false;
        if (barrierCollider != null) barrierCollider.enabled = false;
    }

    private void Update()
    {
        if (_state == State.Idle)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _state = State.Moving;
                boatControl.ControlsFrozen = false;
            }
            return;
        }
    }

    private void LateUpdate()
    {
        if (_state == State.Moving && handTransform != null && boatControl != null)
        {
            handTransform.position = boatControl.BoatTransform.position + _initialOffset;
        }
    }

    public void NotifyEndTrigger()
    {
        if (_state != State.Moving) return;

        if (handAnimator != null)
        {
            handAnimator.SetTrigger("PlayEnd");
        }

        // Start fading audio
        if (windSource != null) StartCoroutine(FadeOutWind());
        if (LevelSelectMusicController.Instance != null)
            LevelSelectMusicController.Instance.FadeIn(audioFadeDuration);

        CompleteIntroSequence(save: true);
    }

    private System.Collections.IEnumerator FadeOutWind()
    {
        float startVolume = windSource.volume;
        float elapsed = 0;
        while (elapsed < audioFadeDuration)
        {
            elapsed += Time.deltaTime;
            windSource.volume = Mathf.Lerp(startVolume, 0, elapsed / audioFadeDuration);
            yield return null;
        }
        windSource.Stop();
        windSource.volume = startVolume;
    }

    private void CompleteIntroSequence(bool save)
    {
        _state = State.Complete;

        boatControl.IntroMode      = false;
        boatControl.ControlsFrozen = false;

        if (normalCamera != null)
        {
            normalCamera.TransitionToFollow();
            // normalCamera.IsControlEnabled = true; // This is now handled by the camera transition's end
        }

        if (barrierCollider != null) barrierCollider.enabled = true;

        if (save)
        {
            var container = boatControl.GetCurrentContainer();
            var segID = container?.GetComponent<RiverSegmentID>();
            if (segID != null)
                GameProgressData.SaveBoatState(segID.SegmentID, boatControl.CurrentProgress, false, false);
        }
    }
}
