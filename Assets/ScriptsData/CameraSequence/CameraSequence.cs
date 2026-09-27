using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// A run of camera shots through Cinemachine cameras placed in the scene — each shot holds for
/// its length, blends in its own way, and can carry dialogue lines played through the scene's
/// DialogueTextController. Authored in Tools/Waves/Camera Sequence (the storyboard window).
///
/// Each shot can carry its own audio clip, and the sequence one clip across all of it; the level
/// select music is held (paused, or kept from starting) until the sequence hands back.
///
/// While it plays the boat is frozen and the follow camera's control is off. At the end every
/// shot camera drops away, the brain blends back to the follow camera (End Blend), the follow
/// camera eases in and the boat is let go.
/// </summary>
public class CameraSequence : MonoBehaviour
{
    [System.Serializable]
    public class ShotLine
    {
        [TextArea(2, 5)]
        public string text;

        [Tooltip("Seconds the line holds fully shown. The fade in/out come from the DialogueTextController.")]
        [Min(0f)] public float hold = 2f;
    }

    [System.Serializable]
    public class Shot
    {
        public CinemachineCamera camera;

        [Tooltip("Seconds this shot is on screen, counted from the moment it is cut or blended to.")]
        [Min(0f)] public float duration = 3f;

        [Tooltip("How the view gets from the previous shot (or the follow camera, for the first) to this one.")]
        public CinemachineBlendDefinition blendIn =
            new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1f);

        [Tooltip("Played in order when the shot starts.")]
        public List<ShotLine> lines = new List<ShotLine>();

        [Tooltip("Optional. Plays when the shot starts and stops when it ends.")]
        public AudioClip audio;
    }

    [Header("Shots")]
    public List<Shot> shots = new List<Shot>();

    [Header("Audio")]
    [Tooltip("Optional. Plays from the first shot and stops when the sequence hands back to the follow camera. " +
             "The level select music is held for the whole sequence either way.")]
    public AudioClip sequenceAudio;

    [Tooltip("How the view gets from the last shot back to the follow camera.")]
    public CinemachineBlendDefinition endBlend =
        new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 2f);

    [Header("Playback")]
    public bool playOnStart = true;

    [Header("References (found in the scene when empty)")]
    [SerializeField] private LevelSelectBoatControl       boatControl;
    [SerializeField] private LevelSelectCameraController  normalCamera;
    [SerializeField] private DialogueTextController       dialogue;
    [SerializeField] private LevelSelectMusicController   music;

    // Shot cameras sit here while they are not the live one, far under the follow camera.
    private const int IdlePriority = -1000;
    // The live shot sits here, far over it.
    private const int LivePriority = 1000;

    public bool IsPlaying { get; private set; }

    private Coroutine _routine;
    private CinemachineCamera _blendTarget;
    private CinemachineBlendDefinition _blendToUse;
    private CinemachineCore.GetBlendOverrideDelegate _previousBlendOverride;

    private AudioSource _sequenceSource;
    private AudioSource _shotSource;
    private bool _holdingMusic;

    private void Awake()
    {
        if (boatControl  == null) boatControl  = FindAnyObjectByType<LevelSelectBoatControl>();
        if (normalCamera == null) normalCamera = FindAnyObjectByType<LevelSelectCameraController>();
        if (dialogue     == null) dialogue     = FindAnyObjectByType<DialogueTextController>();
        if (music        == null) music        = FindAnyObjectByType<LevelSelectMusicController>();

        _sequenceSource = AddAudioSource();
        _shotSource     = AddAudioSource();

        // Shot cameras stay out of the way until the sequence brings them on.
        foreach (var cam in ShotCameras()) cam.enabled = false;

        Debug.Log($"[CameraSequence] Awake '{name}': {shots.Count} shots, boat={(boatControl != null ? "ok" : "NULL")}, " +
                  $"followCamera={(normalCamera != null ? "ok" : "NULL")}, dialogue={(dialogue != null ? "ok" : "NULL")}, " +
                  $"music={(music != null ? "ok" : "NULL")}, sequenceAudio={(sequenceAudio != null ? sequenceAudio.name : "none")}");
    }

    private AudioSource AddAudioSource()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop        = false;
        return source;
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    /// <summary>Run the sequence from its first shot. Ignored while it is already running.</summary>
    public void Play()
    {
        if (IsPlaying) return;
        if (shots.Count == 0)
        {
            Debug.LogWarning($"[CameraSequence] Play '{name}': no shots — nothing to play.");
            return;
        }
        _routine = StartCoroutine(PlayRoutine());
    }

    /// <summary>Jump to the end: hand back to the follow camera and let the boat go.</summary>
    public void Stop()
    {
        if (!IsPlaying) return;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(EndRoutine());
    }

    /// <summary>Total length of the shots, in seconds.</summary>
    public float TotalDuration
    {
        get
        {
            float t = 0f;
            foreach (var s in shots) t += s.duration;
            return t;
        }
    }

    private IEnumerator PlayRoutine()
    {
        IsPlaying = true;
        Debug.Log($"[CameraSequence] Play '{name}': {shots.Count} shots, {TotalDuration:F1}s.");

        if (boatControl  != null) boatControl.ControlsFrozen    = true;
        if (normalCamera != null) normalCamera.IsControlEnabled = false;

        HoldMusic();
        if (sequenceAudio != null)
        {
            _sequenceSource.clip = sequenceAudio;
            _sequenceSource.Play();
        }

        _previousBlendOverride = CinemachineCore.GetBlendOverride;
        CinemachineCore.GetBlendOverride = OverrideBlend;

        foreach (var cam in ShotCameras())
        {
            cam.Priority = IdlePriority;
            cam.enabled  = true;
        }

        CinemachineCamera previous = null;
        for (int i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];
            if (shot.camera == null)
            {
                Debug.LogWarning($"[CameraSequence] Shot {i + 1}: no camera — skipped.");
                continue;
            }

            _blendTarget = shot.camera;
            _blendToUse  = shot.blendIn;
            if (previous != null && previous != shot.camera) previous.Priority = IdlePriority;
            shot.camera.Priority = LivePriority;
            shot.camera.Prioritize();
            previous = shot.camera;

            Debug.Log($"[CameraSequence] Shot {i + 1}/{shots.Count}: '{shot.camera.name}', {shot.duration:F1}s, " +
                      $"blend {shot.blendIn.Style} {shot.blendIn.Time:F1}s, {shot.lines.Count} lines, " +
                      $"audio {(shot.audio != null ? shot.audio.name : "none")}.");

            PlayLines(shot);
            PlayShotAudio(shot);
            yield return new WaitForSeconds(shot.duration);
            _shotSource.Stop();
        }

        _routine = StartCoroutine(EndRoutine());
    }

    private IEnumerator EndRoutine()
    {
        Debug.Log($"[CameraSequence] End '{name}': back to the follow camera, blend {endBlend.Style} {endBlend.Time:F1}s.");

        // Blending to the follow camera — whichever camera the brain picks once the shots drop.
        _blendTarget = null;
        _blendToUse  = endBlend;
        foreach (var cam in ShotCameras()) cam.Priority = IdlePriority;

        if (dialogue != null && AnyLines()) dialogue.HideAll();

        _shotSource.Stop();
        _sequenceSource.Stop();
        ReleaseMusic();

        if (normalCamera != null) normalCamera.TransitionToFollow();
        if (boatControl  != null) boatControl.ControlsFrozen = false;

        // Let the brain finish the blend off the shot cameras before switching them off.
        yield return new WaitForSeconds(endBlend.BlendTime + 0.1f);

        foreach (var cam in ShotCameras()) cam.enabled = false;
        CinemachineCore.GetBlendOverride = _previousBlendOverride;
        _previousBlendOverride = null;

        IsPlaying = false;
        _routine  = null;
        Debug.Log($"[CameraSequence] End '{name}': done.");
    }

    private void PlayLines(Shot shot)
    {
        if (shot.lines.Count == 0) return;
        if (dialogue == null)
        {
            Debug.LogWarning("[CameraSequence] Shot has lines but no DialogueTextController is in the scene.");
            return;
        }

        var texts = new string[shot.lines.Count];
        var holds = new float[shot.lines.Count];
        for (int i = 0; i < shot.lines.Count; i++)
        {
            texts[i] = shot.lines[i].text ?? string.Empty;
            holds[i] = shot.lines[i].hold;
        }
        dialogue.PlaySequence(texts, holds);
    }

    private void PlayShotAudio(Shot shot)
    {
        _shotSource.Stop();
        if (shot.audio == null) return;
        _shotSource.clip = shot.audio;
        _shotSource.Play();
    }

    private void HoldMusic()
    {
        if (_holdingMusic) return;
        if (music == null)
        {
            Debug.LogWarning("[CameraSequence] No LevelSelectMusicController in the scene — nothing to hold.");
            return;
        }
        music.Hold();
        _holdingMusic = true;
    }

    private void ReleaseMusic()
    {
        if (!_holdingMusic) return;
        _holdingMusic = false;
        if (music != null) music.Release();
    }

    private CinemachineBlendDefinition OverrideBlend(ICinemachineCamera from, ICinemachineCamera to,
                                                     CinemachineBlendDefinition defaultBlend, Object owner)
    {
        // A shot blend applies only into that shot; the end blend applies into anything that is
        // not a shot camera. Every other change falls through to whatever was there before.
        bool toShot = to is CinemachineCamera c && IsShotCamera(c);
        if (_blendTarget != null ? ReferenceEquals(to, _blendTarget) : !toShot)
            return _blendToUse;

        return _previousBlendOverride != null
            ? _previousBlendOverride(from, to, defaultBlend, owner)
            : defaultBlend;
    }

    private bool IsShotCamera(CinemachineCamera cam)
    {
        foreach (var s in shots) if (s.camera == cam) return true;
        return false;
    }

    private bool AnyLines()
    {
        foreach (var s in shots) if (s.lines.Count > 0) return true;
        return false;
    }

    private IEnumerable<CinemachineCamera> ShotCameras()
    {
        var seen = new HashSet<CinemachineCamera>();
        foreach (var s in shots)
            if (s.camera != null && seen.Add(s.camera))
                yield return s.camera;
    }

    private void OnDisable()
    {
        // Never leave the blend hook pointing at a dead sequence.
        if (IsPlaying)
        {
            CinemachineCore.GetBlendOverride = _previousBlendOverride;
            IsPlaying = false;
        }
        // Never leave the music held by a dead sequence either.
        ReleaseMusic();
    }
}
