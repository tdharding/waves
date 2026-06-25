using UnityEngine;
using UnityEngine.Splines;
using Unity.Cinemachine;
using Unity.Mathematics;

[DefaultExecutionOrder(100)]
public class LevelSelectOpeningSequence : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LevelSelectBoatControl boatControl;
    [SerializeField] private LevelSelectCameraController normalCamera;
    [SerializeField] private Transform handTransform;
    [SerializeField] private Animator handAnimator;
    [SerializeField] private Collider barrierCollider;

    [Header("Settings")]
    [SerializeField] public bool skipIntro = false;
    [SerializeField] private Transform skipIntroStartPoint;
    [SerializeField] private float skipIntroExtrudeHeadstart = 0.05f;

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

        Debug.Log($"[OpeningSequence] Awake: boatControl={(boatControl != null ? boatControl.name : "NULL")}, normalCamera={(normalCamera != null ? normalCamera.name : "NULL")}, handTransform={(handTransform != null ? handTransform.name : "NULL")}, barrierCollider={(barrierCollider != null ? barrierCollider.name : "NULL")}, windSource={(windSource != null ? windSource.name : "NULL")}");

        if (!skipIntro && string.IsNullOrEmpty(GameProgressData.GetBoatSegmentID()))
        {
            var music = FindObjectOfType<LevelSelectMusicController>();
            if (music != null) music.playOnStart = false;
            Debug.Log($"[OpeningSequence] Awake: first-time play — music playOnStart suppressed ({(music != null ? "found" : "no music controller")}).");
        }
    }

    private void Start()
    {
        bool isFirstTime = string.IsNullOrEmpty(GameProgressData.GetBoatSegmentID());
        Debug.Log($"[OpeningSequence] Start: isFirstTime={isFirstTime}, skipIntro={skipIntro}, boatTransform={(boatControl?.BoatTransform != null ? boatControl.BoatTransform.position.ToString() : "NULL")}");

        if (!isFirstTime || skipIntro)
        {
            Debug.Log("[OpeningSequence] Start: skipping intro (returning player or skipIntro=true), calling CompleteIntroSequence(save:false).");
            CompleteIntroSequence(save: false);

            // On a fresh save with no intro, project STARTPOINTIFNOSEQUENCE onto the spline
            // so the boat begins beyond the opening-sequence colliders.
            if (skipIntro && isFirstTime && boatControl != null)
            {
                var container = boatControl.GetCurrentContainer();
                if (container != null && skipIntroStartPoint != null)
                {
                    var localPos = container.transform.InverseTransformPoint(skipIntroStartPoint.position);
                    SplineUtility.GetNearestPoint(container.Spline, (float3)localPos, out _, out float t);
                    boatControl.RestoreToSegment(container, t);
                    float extrudeT = Mathf.Clamp01(t + skipIntroExtrudeHeadstart);
                    SplineRiverManager.Instance?.ForceJumpExtrudeToT(extrudeT);
                    LevelSelectSplineManager.Instance?.RefreshAdvance();
                    Debug.Log($"[OpeningSequence] skipIntro: boat at T={t:F3}, river extruded to T={extrudeT:F3}, advance refreshed.");
                }
                else
                {
                    Debug.LogWarning($"[OpeningSequence] skipIntro: cannot position boat — container={(container != null ? "ok" : "NULL")}, skipIntroStartPoint={(skipIntroStartPoint != null ? "ok" : "NULL")}.");
                }
            }

            return;
        }

        _state = State.Idle;

        if (windSource != null && windClip != null)
        {
            windSource.clip = windClip;
            windSource.loop = true;
            windSource.Play();
            Debug.Log("[OpeningSequence] Start: wind audio started.");
        }
        else
        {
            Debug.LogWarning($"[OpeningSequence] Start: wind audio skipped — windSource={(windSource != null ? "ok" : "NULL")}, windClip={(windClip != null ? "ok" : "NULL")}");
        }

        if (handTransform != null && boatControl != null && boatControl.BoatTransform != null)
        {
            _initialOffset = handTransform.position - boatControl.BoatTransform.position;
            Debug.Log($"[OpeningSequence] Start: hand={handTransform.position}, boat={boatControl.BoatTransform.position}, initialOffset={_initialOffset}");
        }
        else
        {
            Debug.LogWarning($"[OpeningSequence] Start: could not calculate initialOffset — handTransform={(handTransform != null ? "ok" : "NULL")}, BoatTransform={(boatControl?.BoatTransform != null ? "ok" : "NULL")}");
        }

        boatControl.IntroMode      = true;
        boatControl.ControlsFrozen = true;
        Debug.Log("[OpeningSequence] Start: boat frozen, intro mode on. Waiting for Space.");

        if (normalCamera != null) normalCamera.IsControlEnabled = false;
        else Debug.LogWarning("[OpeningSequence] Start: normalCamera is NULL — camera control not disabled.");

        if (barrierCollider != null) barrierCollider.gameObject.SetActive(false);
        else Debug.LogWarning("[OpeningSequence] Start: barrierCollider is NULL — barrier not disabled.");
    }

    private void Update()
    {
        if (_state == State.Idle)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (handTransform != null && boatControl != null && boatControl.BoatTransform != null)
                {
                    _initialOffset = handTransform.position - boatControl.BoatTransform.position;
                    Debug.Log($"[OpeningSequence] Space pressed — recalculated initialOffset={_initialOffset}. Transitioning to Moving state.");
                }
                else
                {
                    Debug.LogWarning("[OpeningSequence] Space pressed but hand or boat transform is null.");
                }

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
        Debug.Log($"[OpeningSequence] NotifyEndTrigger called — current state={_state}");
        if (_state != State.Moving)
        {
            Debug.LogWarning($"[OpeningSequence] NotifyEndTrigger ignored — state is {_state}, expected Moving.");
            return;
        }

        if (handAnimator != null)
        {
            handAnimator.SetTrigger("PlayEnd");
            Debug.Log("[OpeningSequence] NotifyEndTrigger: handAnimator 'PlayEnd' triggered.");
        }
        else
        {
            Debug.LogWarning("[OpeningSequence] NotifyEndTrigger: handAnimator is NULL — end animation skipped.");
        }

        if (windSource != null) StartCoroutine(FadeOutWind());
        if (LevelSelectMusicController.Instance != null)
        {
            LevelSelectMusicController.Instance.FadeIn(audioFadeDuration);
            Debug.Log("[OpeningSequence] NotifyEndTrigger: music fade-in started.");
        }
        else
        {
            Debug.LogWarning("[OpeningSequence] NotifyEndTrigger: LevelSelectMusicController.Instance is NULL.");
        }

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
        Debug.Log($"[OpeningSequence] CompleteIntroSequence: save={save}");
        _state = State.Complete;

        boatControl.IntroMode      = false;
        boatControl.ControlsFrozen = false;

        if (normalCamera != null)
        {
            Debug.Log("[OpeningSequence] CompleteIntroSequence: calling TransitionToFollow on camera.");
            normalCamera.TransitionToFollow();
        }
        else
        {
            Debug.LogWarning("[OpeningSequence] CompleteIntroSequence: normalCamera is NULL — camera transition skipped.");
        }

        if (barrierCollider != null) barrierCollider.gameObject.SetActive(true);

        if (save)
        {
            var container = boatControl.GetCurrentContainer();
            var segID     = container?.GetComponent<RiverSegmentID>();
            if (segID != null)
            {
                GameProgressData.SaveBoatState(segID.SegmentID, boatControl.CurrentProgress, false, false);
                Debug.Log($"[OpeningSequence] CompleteIntroSequence: saved boat state — segment='{segID.SegmentID}', progress={boatControl.CurrentProgress}");
            }
            else
            {
                Debug.LogWarning($"[OpeningSequence] CompleteIntroSequence: could not save — container={(container != null ? "found" : "NULL")}, segID={(segID != null ? "found" : "NULL")}");
            }
        }
    }
}
