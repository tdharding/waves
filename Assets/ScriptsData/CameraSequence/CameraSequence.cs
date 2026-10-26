using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Cinemachine;

/// <summary>
/// A run of camera shots through Cinemachine cameras placed in the scene — each shot holds for
/// its length, blends in its own way, and can carry dialogue lines played through the scene's
/// DialogueTextController. Authored in Tools/Waves/Camera Sequence (the storyboard window).
///
/// Each shot can carry its own audio clip, and the sequence one clip across all of it; the level
/// select music is held (paused, or kept from starting) until the sequence hands back. With Audio
/// off the sequence makes no sound of its own and leaves the music alone.
///
/// The black panel behind the lines rises with the first lines and stays up between shots; only
/// the last shot (or the hand-back) lowers it.
///
/// Presets: everything a sequence is can be saved to a <see cref="CameraSequencePreset"/> asset,
/// which links to the shot cameras here in the scene. This component is the storyboard's working
/// copy and the one player in the scene — <see cref="PlayPreset"/> loads a preset into it and plays it.
///
/// While it plays the boat is frozen and the follow camera's control is off. At the end every
/// shot camera drops away, the brain blends back to the follow camera (End Blend), the follow
/// camera eases in and the boat is let go.
/// </summary>
public class CameraSequence : MonoBehaviour
{
    public enum ShotAnimation { Zoom, Orbit, Animate2Shot }
    public enum AnimationEasing { Linear, EaseIn, EaseOut, EaseInOut }
    public enum FogCentre { View, Boat, CameraPosition }

    [System.Serializable]
    public class ShotLine
    {
        [TextArea(2, 5)]
        public string text;

        [Tooltip("Seconds the line holds fully shown. The fade in/out come from the DialogueTextController. " +
                 "Not used when Press Any Key is on.")]
        [Min(0f)] public float hold = 2f;

        [Tooltip("On: the line holds until the player presses any key. Off: it holds for its Hold.")]
        public bool pressAnyKey;

        [Tooltip("On: the line fades in. Off: it appears at once. Only used when the shot's Text Fades is on.")]
        public bool fadeIn = true;

        [Tooltip("On: the line fades out. Off: it vanishes at once. Only used when the shot's Text Fades is on.")]
        public bool fadeOut = true;
    }

    /// <summary>A character's Animator cross-fading into one of its states as the shot starts.</summary>
    [System.Serializable]
    public class CharacterAnimation
    {
        public SequenceCharacter character;

        [Tooltip("The Animator state played, by its full path (Layer.State, or Layer.SubMachine.State).")]
        public string state;

        [Tooltip("The Animator layer the state is on.")]
        public int layer;

        [Tooltip("Seconds the Animator cross-fades from what it was playing into the state.")]
        [Min(0f)] public float blend = 0.25f;
    }

    [System.Serializable]
    public class Shot
    {
        public CinemachineCamera camera;

        [Tooltip("Seconds this shot is on screen, counted from the moment it is cut or blended to. " +
                 "Not used when Press Any Key is on.")]
        [Min(0f)] public float duration = 3f;

        [Tooltip("How the view gets from the previous shot (or the follow camera, for the first) to this one.")]
        public CinemachineBlendDefinition blendIn =
            new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1f);

        [Tooltip("On: the shot holds until the player presses any key, then moves on — or, when its last line " +
                 "fades out, as soon as that line has faded. Off: it moves on after its length.")]
        public bool pressAnyKey;

        [Tooltip("Played in order when the shot starts.")]
        public List<ShotLine> lines = new List<ShotLine>();

        [Tooltip("On: this shot's lines fade in and out (timings from the DialogueTextController); the black panel fades only on the first and last shots. " +
                 "Off: nothing fades — each line swaps straight to the next after its hold, and the last one stays up until the next shot.")]
        public bool textFades = true;

        [Tooltip("Optional. Plays when the shot starts and stops when it ends.")]
        public AudioClip audio;

        [Tooltip("Where the fog's clear middle sits for this shot, set once as the shot starts and held still " +
                 "through its animation. View: the spot the camera looks at (the middle of its view on the water). " +
                 "Boat: on the boat. Camera Position: on the water under the camera.")]
        public FogCentre fogCentre = FogCentre.View;

        [Tooltip("On: for this shot the fog is full at Fog Distance from its centre, instead of the world's White Fade " +
                 "Radius. Set as the shot starts; the world's Radius comes back for shots without it, and at the hand back.")]
        public bool overrideFogDistance;

        [Tooltip("Metres from the fog's centre to where it is full, for this shot. Only used when Fog Distance Override is on.")]
        [Min(0f)] public float fogDistance;

        [Tooltip("Optional. What the camera keeps its height to. As the shot starts the camera is raised or lowered to stand " +
                 "View Target Height above it, and its whole movement goes with it. Picked in the Cm View with Set View Target.")]
        public Transform viewTarget;

        // Where the View Target sits in the hierarchy, kept up to date in the editor. GENERATE builds the
        // Level Select's pieces again as new objects — the shot finds its target's rebuilt self by this.
        [HideInInspector] public string viewTargetPath;

        [Tooltip("How far above the View Target the camera stands for this shot. Learned from where the camera is placed; " +
                 "moving the camera up or down changes it.")]
        public float viewTargetHeight;

        [Tooltip("On: as the shot starts its Depth of Field Focus Distance is set to how far the camera stands from the " +
                 "View Target, once — it stays there while the camera animates. Off: the shot's Volume's own Focus Distance.")]
        public bool focusViewTarget;

        [Tooltip("This shot's own post processing — its colour, depth of field and the rest — worn by the camera's Cinemachine " +
                 "Volume Settings while the shot is on, with Focus Tracking off. The storyboard makes it, a copy of the Level " +
                 "Select profile; edit it in the Cm View opened from the storyboard.")]
        public VolumeProfile volumeProfile;

        [Tooltip("On: the camera moves while the shot plays (see Animation). It goes back to where it was placed when the sequence ends.")]
        public bool animated;

        [Tooltip("Zoom: the camera moves forward, the way it faces. Orbit: the camera circles its Orbit Point. " +
                 "Animate 2 Shot: the camera tweens from where it stands to its End Camera.")]
        public ShotAnimation animation = ShotAnimation.Zoom;

        [Tooltip("Animate 2 Shot: the second shot — where the camera ends up, and the way it faces there. " +
                 "Its Cinemachine Camera stays switched off; it is only a marker the shot camera moves to.")]
        public CinemachineCamera endCamera;

        [Tooltip("How far the camera travels. The world scale is small, so small values go a long way.")]
        public float zoomDistance = 1f;

        [Tooltip("Orbit: what the camera circles, round the world up axis, looking at it the whole way.")]
        public Transform orbitPoint;

        [Tooltip("Orbit: where round the Orbit Point the camera starts, in degrees. 0 = on the point's south side (-Z), " +
                 "looking north; it goes clockwise seen from above. The camera keeps its own distance and height.")]
        public float orbitStartDegrees;

        [Tooltip("Orbit: how far round the camera goes over the animation's Time, in degrees. Negative goes the other way.")]
        public float orbitDegrees = 90f;

        [Tooltip("Seconds the movement takes, from the moment the shot starts. It holds at the end.")]
        [Min(0f)] public float animationTime = 3f;

        public AnimationEasing easing = AnimationEasing.Linear;

        [Tooltip("Characters whose Animators cross-fade into a state as the shot starts. Picked in the storyboard " +
                 "from the characters the shot's camera can see.")]
        public List<CharacterAnimation> characterAnimations = new List<CharacterAnimation>();
    }

    [Header("Shots")]
    public List<Shot> shots = new List<Shot>();

    [Header("Audio")]
    [Tooltip("On: the sequence plays its own audio and holds the level select music until the hand back. " +
             "Off: no sequence or shot audio, and the level music carries on untouched.")]
    public bool useAudio = true;

    [Tooltip("Optional. Plays from the first shot and stops when the sequence hands back to the follow camera. " +
             "The level select music is held for the whole sequence either way.")]
    public AudioClip sequenceAudio;

    [Tooltip("Seconds the sequence's sound (Sequence Audio and the last shot's audio) takes to fade out at the hand back, " +
             "while the level select music fades in. 0 = cut.")]
    [Min(0f)] public float audioFadeOut = 4f;

    [Tooltip("How the view gets from the last shot back to the follow camera.")]
    public CinemachineBlendDefinition endBlend =
        new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 2f);

    [Header("Playback")]
    [Tooltip("On: plays once per save — after that it never plays again until New Game. Off: plays every time. Saved with the preset.")]
    public bool playOnce = true;

    public bool playOnStart = true;

    [Tooltip("The preset Play On Start plays. Empty = the storyboard as it stands in the scene.")]
    public CameraSequencePreset startPreset;

    [Tooltip("The preset the storyboard last loaded or saved — where its Save goes. Set by the storyboard.")]
    public CameraSequencePreset preset;

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
    private Coroutine _audioFade;
    private bool _holdingMusic;

    // Fog Centre: while the sequence plays, the fog's centre is held on the shot's spot (unless it is the boat's).
    private static readonly int BoatCentreId      = Shader.PropertyToID("_BoatWorldCenter");
    private static readonly int WhiteFadeCentreId = Shader.PropertyToID("_WhiteFadeCentre");
    private bool _holdingFogCentre;
    private bool _fogPinned;
    private Vector3 _fogSpot;
    // True while a no-fade shot's last line is left on screen for the next shot to clear.
    private bool _textHeld;

    // Where each animated shot camera was placed, so the run can put it back at the end.
    private readonly Dictionary<CinemachineCamera, Pose> _placedAt = new Dictionary<CinemachineCamera, Pose>();
    private readonly Dictionary<CinemachineCamera, Coroutine> _moves = new Dictionary<CinemachineCamera, Coroutine>();

    private void Awake()
    {
        if (boatControl  == null) boatControl  = FindAnyObjectByType<LevelSelectBoatControl>();
        if (normalCamera == null) normalCamera = FindAnyObjectByType<LevelSelectCameraController>();
        if (dialogue     == null) dialogue     = FindAnyObjectByType<DialogueTextController>();
        if (music        == null) music        = FindAnyObjectByType<LevelSelectMusicController>();

        _sequenceSource = AddAudioSource();
        _shotSource     = AddAudioSource();

        // Shot cameras stay out of the way until the sequence brings them on — every preset's too,
        // since any of them can be loaded in later. End cameras never come on: they are only marks.
        foreach (var cam in ShotCameras()) cam.enabled = false;
        foreach (var s in shots) if (s.endCamera != null) s.endCamera.enabled = false;
        foreach (var link in FindObjectsByType<CameraSequenceLink>(FindObjectsInactive.Include))
            if (link.TryGetComponent<CinemachineCamera>(out var linked)) linked.enabled = false;

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
        if (!playOnStart) return;
#if UNITY_EDITOR
        if (SkipOpeningForPlaytest())
        {
            Debug.Log($"[CameraSequence] Start '{name}': opening cinematic skipped for this playtest.");
            return;
        }
#endif
        if (startPreset != null)
        {
            PlayPreset(startPreset);
            return;
        }
        if (playOnce && GameProgressData.HasSequencePlayed(SaveKey))
        {
            Debug.Log($"[CameraSequence] Start '{name}': already played on this save ({SaveKey}) — not replaying.");
            return;
        }
        Play();
    }

#if UNITY_EDITOR
    /// <summary>Set by the Level Select Designer's playtest launch: skip Play On Start, that launch only.</summary>
    public const string SkipOpeningKey = "LevelSelectDesigner_SkipOpeningCinematic";

    // The scene the skip was taken in, so every Play On Start sequence in it skips — but coming
    // back to the world from a level in the same play runs the opening as usual.
    private static UnityEngine.SceneManagement.Scene s_skipOpeningScene;

    private bool SkipOpeningForPlaytest()
    {
        var scene = gameObject.scene;
        if (UnityEditor.SessionState.GetBool(SkipOpeningKey, false))
        {
            UnityEditor.SessionState.EraseBool(SkipOpeningKey);
            s_skipOpeningScene = scene;
        }
        return scene.IsValid() && s_skipOpeningScene == scene;
    }
#endif

    // One entry per sequence per scene in the save, so each world's cinematic plays once: the
    // preset's name when one is playing, this object's otherwise.
    private string _presetKey;
    private string SaveKey => _presetKey ?? $"{gameObject.scene.name}/{name}";

    public string PresetKey(CameraSequencePreset p) =>$"{gameObject.scene.name}/{p.name}";

    /// <summary>
    /// Load <paramref name="p"/> into this sequence and play it. Ignored while a sequence is
    /// running, and when the preset is Play Once and has already played on this save.
    /// </summary>
    public void PlayPreset(CameraSequencePreset p)
    {
        if (p == null) return;
        if (IsPlaying)
        {
            Debug.Log($"[CameraSequence] PlayPreset '{p.name}': a sequence is already playing — ignored.");
            return;
        }
        string key = PresetKey(p);
        if (p.playOnce && GameProgressData.HasSequencePlayed(key))
        {
            Debug.Log($"[CameraSequence] PlayPreset '{p.name}': already played on this save ({key}) — not replaying.");
            return;
        }

        p.ApplyTo(this);
        _presetKey = key;
        Debug.Log($"[CameraSequence] PlayPreset '{p.name}': loaded {shots.Count} shots, " +
                  $"{(p.playOnce ? "once per save" : "every time")}.");
        Play();
        if (!IsPlaying) _presetKey = null;
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

    /// <summary>Total length of the shots, in seconds (Press Any Key shots count their Length).</summary>
    public float TotalDuration
    {
        get
        {
            float t = 0f;
            foreach (var s in shots) t += s.duration;
            return t;
        }
    }

    /// <summary>A shot's settings, copied — its camera, orbit point and audio still point at the same objects.</summary>
    public static Shot CopyShot(Shot shot)
    {
        var copy = DeepCopy(shot);
        copy.camera     = shot.camera;
        copy.orbitPoint = shot.orbitPoint;
        copy.endCamera  = shot.endCamera;
        copy.viewTarget = shot.viewTarget;
        copy.audio      = shot.audio;
        copy.volumeProfile = shot.volumeProfile;
        for (int i = 0; i < copy.characterAnimations.Count; i++)
            copy.characterAnimations[i].character = shot.characterAnimations[i].character;
        return copy;
    }

    /// <summary>A copy of serializable settings that shares nothing with the original (curves included).</summary>
    public static T DeepCopy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

    private IEnumerator PlayRoutine()
    {
        IsPlaying = true;
        Debug.Log($"[CameraSequence] Play '{name}': {shots.Count} shots, {TotalDuration:F1}s.");

        if (boatControl  != null) boatControl.ControlsFrozen    = true;
        if (normalCamera != null) normalCamera.IsControlEnabled = false;

        if (useAudio) HoldMusic();
        HoldFog();
        StopAudioFade();
        if (useAudio && sequenceAudio != null)
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

            Debug.Log($"[CameraSequence] Shot {i + 1}/{shots.Count}: '{shot.camera.name}', " +
                      $"{(shot.pressAnyKey ? "until a key" : $"{shot.duration:F1}s")}, " +
                      $"blend {shot.blendIn.Style} {shot.blendIn.Time:F1}s, {shot.lines.Count} lines " +
                      $"({(shot.textFades ? "fading" : "no fade")}), " +
                      $"audio {(shot.audio != null ? shot.audio.name : "none")}, " +
                      $"{(shot.animated ? $"{AnimationSummary(shot)} over {shot.animationTime:F1}s {shot.easing}" : "still")}.");

            PlayLines(shot, i == shots.Count - 1);
            PlayShotAudio(shot);
            float lift = LiftToViewTarget(shot, i);
            WearShotVolume(shot, i);
            if (shot.animated) StartAnimation(shot, lift);
            PlayCharacterAnimations(shot, i);
            PinFog(shot, i);
            if (shot.pressAnyKey && MovesOnWithLines(shot))
            {
                // The last line fades out: that fade ends the shot, no extra key press.
                yield return new WaitUntil(() => dialogue.LinesDone);
                Debug.Log($"[CameraSequence] Shot {i + 1}: last line faded out — moving on.");
            }
            else if (shot.pressAnyKey)
            {
                // A frame first, so the key that ended the last shot doesn't end this one too.
                // Presses that move this shot's Press Any Key lines on belong to the lines.
                yield return null;
                yield return new WaitUntil(() => Input.anyKeyDown && !KeyBelongsToLines());
                Debug.Log($"[CameraSequence] Shot {i + 1}: key pressed — moving on.");
            }
            else
            {
                yield return new WaitForSeconds(shot.duration);
            }
            // The last shot's audio carries on into the hand back and fades out with the sequence audio.
            if (i < shots.Count - 1) _shotSource.Stop();
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

        if (dialogue != null && AnyLines()) dialogue.HideAll(shots[shots.Count - 1].textFades);
        _textHeld = false;

        // The sequence's sound fades out while the music fades in.
        StopAudioFade();
        _audioFade = StartCoroutine(FadeOutAudio(audioFadeOut));
        ReleaseMusic();
        ReleaseFog();

        if (normalCamera != null) normalCamera.TransitionToFollow();
        if (boatControl  != null) boatControl.ControlsFrozen = false;

        // Played (or skipped to the end): coming back into this world won't run it again.
        if (playOnce) GameProgressData.MarkSequencePlayed(SaveKey);
        _presetKey = null;

        // Let the brain finish the blend off the shot cameras before switching them off.
        yield return new WaitForSeconds(endBlend.BlendTime + 0.1f);

        foreach (var cam in ShotCameras()) cam.enabled = false;
        PutCamerasBack();
        CinemachineCore.GetBlendOverride = _previousBlendOverride;
        _previousBlendOverride = null;

        IsPlaying = false;
        _routine  = null;
        Debug.Log($"[CameraSequence] End '{name}': done.");
    }

    private void PlayLines(Shot shot, bool lastShot)
    {
        bool wasHeld = _textHeld;
        _textHeld = shot.lines.Count > 0 && !shot.textFades;

        if (shot.lines.Count == 0)
        {
            // A no-fade shot leaves its last line up until the next shot — take it down here.
            // The panel stays: only the last shot or the hand-back lowers it.
            if (wasHeld && dialogue != null) dialogue.HideText(false);
            return;
        }
        if (dialogue == null)
        {
            Debug.LogWarning("[CameraSequence] Shot has lines but no DialogueTextController is in the scene.");
            return;
        }

        var texts = new string[shot.lines.Count];
        var holds = new float[shot.lines.Count];
        var keys  = new bool[shot.lines.Count];
        var ins   = new bool[shot.lines.Count];
        var outs  = new bool[shot.lines.Count];
        for (int i = 0; i < shot.lines.Count; i++)
        {
            texts[i] = shot.lines[i].text ?? string.Empty;
            holds[i] = shot.lines[i].hold;
            keys[i]  = shot.lines[i].pressAnyKey;
            ins[i]   = shot.lines[i].fadeIn;
            outs[i]  = shot.lines[i].fadeOut;
        }
        // The panel comes down only after the last shot's lines; between shots it stays up.
        dialogue.PlaySequence(texts, holds, keys, ins, outs, shot.textFades, lastShot);
    }

    /// <summary>A fading shot whose last line fades out moves on once that fade is done.</summary>
    private bool MovesOnWithLines(Shot shot)
    {
        if (dialogue == null || !shot.textFades) return false;
        for (int i = shot.lines.Count - 1; i >= 0; i--)
            if (!string.IsNullOrWhiteSpace(shot.lines[i].text))
                return shot.lines[i].fadeOut;
        return false;
    }

    private bool KeyBelongsToLines()
    {
        return dialogue != null && (dialogue.KeyLinesPending || dialogue.KeyTakenThisFrame);
    }

    /// <summary>
    /// A shot with a View Target: its camera is raised or lowered to stand View Target Height above the target
    /// as it is now, and goes back to where it was placed at the end. Returns how far it moved, so the shot's
    /// movement can be moved with it.
    /// </summary>
    private float LiftToViewTarget(Shot shot, int index)
    {
        if (shot.viewTarget == null) return 0f;
        var cam = shot.camera;
        float lift = ViewTargetLift(shot, cam.transform.position);
        if (Mathf.Abs(lift) < 1e-4f)
        {
            Debug.Log($"[CameraSequence] Shot {index + 1}: '{cam.name}' already {shot.viewTargetHeight:F2} above '{shot.viewTarget.name}'.");
            return 0f;
        }

        if (!_placedAt.ContainsKey(cam)) _placedAt[cam] = new Pose(cam.transform.position, cam.transform.rotation);
        // An earlier shot's movement still running on this camera would undo the lift.
        if (_moves.TryGetValue(cam, out var running) && running != null) StopCoroutine(running);
        _moves.Remove(cam);

        cam.transform.position += Vector3.up * lift;
        Debug.Log($"[CameraSequence] Shot {index + 1}: '{cam.name}' moved {lift:+0.00;-0.00} to stand {shot.viewTargetHeight:F2} " +
                  $"above '{shot.viewTarget.name}' (target at y {shot.viewTarget.position.y:F2}).");
        return lift;
    }

    /// <summary>
    /// How far the shot's camera, standing at <paramref name="cameraPosition"/>, must rise (negative: drop) to stand
    /// View Target Height above its View Target. 0 with no View Target. The storyboard's Preview uses this too.
    /// </summary>
    public static float ViewTargetLift(Shot shot, Vector3 cameraPosition)
        => shot.viewTarget != null ? shot.viewTarget.position.y + shot.viewTargetHeight - cameraPosition.y : 0f;

    /// <summary>A mark of the shot's movement (Orbit Point, End Camera) that is the View Target or sits under it already moves with it.</summary>
    public static bool RidesWithViewTarget(Shot shot, Transform mark)
        => shot.viewTarget != null && mark != null && mark.IsChildOf(shot.viewTarget);

    private void StartAnimation(Shot shot, float lift)
    {
        var cam = shot.camera;
        if (shot.animation == ShotAnimation.Orbit && shot.orbitPoint == null)
        {
            Debug.LogWarning($"[CameraSequence] '{cam.name}': Orbit has no Orbit Point — the camera stays still.");
            return;
        }
        if (shot.animation == ShotAnimation.Animate2Shot && shot.endCamera == null)
        {
            Debug.LogWarning($"[CameraSequence] '{cam.name}': Animate 2 Shot has no End Camera — the camera stays still.");
            return;
        }
        if (!_placedAt.ContainsKey(cam)) _placedAt[cam] = new Pose(cam.transform.position, cam.transform.rotation);
        // The same camera in a later shot takes over from its last movement, from where it stands.
        if (_moves.TryGetValue(cam, out var running) && running != null) StopCoroutine(running);
        _moves[cam] = StartCoroutine(Animate(cam, shot, lift));
    }

    private void PlayCharacterAnimations(Shot shot, int index)
    {
        foreach (var ca in shot.characterAnimations)
        {
            if (ca.character == null || string.IsNullOrEmpty(ca.state))
            {
                Debug.LogWarning($"[CameraSequence] Shot {index + 1}: a character animation has no " +
                                 $"{(ca.character == null ? "character" : "state")} — skipped.");
                continue;
            }
            var animator = ca.character.Animator;
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[CameraSequence] Shot {index + 1}: '{ca.character.DisplayName}' has no working Animator " +
                                 $"({(animator == null ? "none" : !animator.isActiveAndEnabled ? "switched off" : "no controller")}) — '{ca.state}' not played.");
                continue;
            }
            int layer = ca.layer < animator.layerCount ? ca.layer : -1;
            if (!animator.HasState(Mathf.Max(0, layer), Animator.StringToHash(ca.state)))
            {
                Debug.LogWarning($"[CameraSequence] Shot {index + 1}: '{ca.character.DisplayName}' has no state '{ca.state}' " +
                                 $"on layer {ca.layer} — not played.");
                continue;
            }
            animator.CrossFadeInFixedTime(ca.state, ca.blend, layer);
            Debug.Log($"[CameraSequence] Shot {index + 1}: '{ca.character.DisplayName}' → '{ca.state}' (blend {ca.blend:F2}s).");
        }
    }

    private static string AnimationSummary(Shot shot)
    {
        if (shot.animation == ShotAnimation.Zoom) return $"Zoom {shot.zoomDistance:F2}";
        if (shot.animation == ShotAnimation.Animate2Shot)
            return shot.endCamera != null ? $"Animate 2 Shot to '{shot.endCamera.name}'" : "Animate 2 Shot (no end camera)";
        return shot.orbitPoint != null
            ? $"Orbit {shot.orbitDegrees:F0}° from {shot.orbitStartDegrees:F0}° round '{shot.orbitPoint.name}'"
            : "Orbit (no point)";
    }

    // Runs its own Time, even past the shot's end, then holds. Lift: how far the View Target moved the camera
    // as the shot started — the Orbit Point and End Camera move with it.
    private IEnumerator Animate(CinemachineCamera cam, Shot shot, float lift)
    {
        Transform t = cam.transform;
        var from = new Pose(t.position, t.rotation);
        Vector3 centre = OrbitCentre(shot, from, lift);

        float elapsed = 0f;
        while (elapsed < shot.animationTime)
        {
            elapsed += Time.deltaTime;
            var pose = PoseAt(shot, from, centre, elapsed, lift);
            t.SetPositionAndRotation(pose.position, pose.rotation);
            yield return null;
        }
        var end = PoseAt(shot, from, centre, shot.animationTime, lift);
        t.SetPositionAndRotation(end.position, end.rotation);
        _moves.Remove(cam);
    }

    /// <summary>
    /// Where an Orbit circles: the Orbit Point as the movement starts (the camera itself if there is none), moved
    /// up by <paramref name="lift"/> (the View Target's move) unless it already moves with the View Target.
    /// </summary>
    public static Vector3 OrbitCentre(Shot shot, Pose from, float lift = 0f)
    {
        if (shot.orbitPoint == null) return from.position;
        return shot.orbitPoint.position + (RidesWithViewTarget(shot, shot.orbitPoint) ? Vector3.zero : Vector3.up * lift);
    }

    /// <summary>
    /// Where the shot's movement has the camera <paramref name="elapsed"/> seconds in, from where it started.
    /// Zoom: forward the way it faced. Orbit: round the centre on the world up axis, looking at the
    /// centre the whole way. Animate 2 Shot: straight to the End Camera (moved up by <paramref name="lift"/>, the
    /// View Target's move, unless it already moves with the View Target), turning to face its way.
    /// The storyboard's Preview plays this same path.
    /// </summary>
    public static Pose PoseAt(Shot shot, Pose from, Vector3 orbitCentre, float elapsed, float lift = 0f)
    {
        float k = shot.animationTime > 0f
            ? Ease(Mathf.Clamp01(elapsed / shot.animationTime), shot.easing)
            : 1f;
        if (shot.animation == ShotAnimation.Orbit)
        {
            var turn = Quaternion.AngleAxis(shot.orbitDegrees * k, Vector3.up);
            Vector3 position = orbitCentre + turn * (from.position - orbitCentre);
            return new Pose(position, OrbitLook(position, orbitCentre, turn * from.rotation));
        }
        if (shot.animation == ShotAnimation.Animate2Shot)
        {
            if (shot.endCamera == null) return from;
            Transform end = shot.endCamera.transform;
            Vector3 endAt = end.position + (RidesWithViewTarget(shot, end) ? Vector3.zero : Vector3.up * lift);
            return new Pose(Vector3.Lerp(from.position, endAt, k),
                            Quaternion.Slerp(from.rotation, end.rotation, k));
        }
        return new Pose(from.position + from.forward * (shot.zoomDistance * k), from.rotation);
    }

    /// <summary>
    /// An orbiting camera at <paramref name="position"/>, facing its orbit point. Straight above or
    /// below the point there is no single way to look at it upright, so it keeps <paramref name="fallback"/>.
    /// The storyboard aims each Orbit shot's camera with this, so the camera as placed is the orbit's start.
    /// </summary>
    public static Quaternion OrbitLook(Vector3 position, Vector3 orbitCentre, Quaternion fallback)
    {
        Vector3 toCentre = orbitCentre - position;
        return Vector3.Cross(toCentre, Vector3.up).sqrMagnitude > 1e-8f
            ? Quaternion.LookRotation(toCentre, Vector3.up)
            : fallback;
    }

    /// <summary>
    /// Where round the orbit point a camera at <paramref name="position"/> stands, in Start Degrees terms:
    /// 0 = the point's south side (-Z), clockwise seen from above.
    /// </summary>
    public static float OrbitAngle(Vector3 orbitCentre, Vector3 position)
    {
        Vector3 flat = Vector3.ProjectOnPlane(position - orbitCentre, Vector3.up);
        return flat.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(Vector3.back, flat, Vector3.up) : 0f;
    }

    /// <summary>
    /// <paramref name="position"/> moved round the orbit point to <paramref name="startDegrees"/>, keeping its
    /// distance and height. Straight above or below the point there is no "round", so it stays put.
    /// The storyboard places each Orbit shot's camera with this.
    /// </summary>
    public static Vector3 OrbitStartPosition(Vector3 orbitCentre, Vector3 position, float startDegrees)
    {
        Vector3 offset = position - orbitCentre;
        float radius = new Vector2(offset.x, offset.z).magnitude;
        if (radius < 1e-4f) return position;
        Vector3 round = Quaternion.AngleAxis(startDegrees, Vector3.up) * (Vector3.back * radius);
        return orbitCentre + new Vector3(round.x, offset.y, round.z);
    }

    private static float Ease(float x, AnimationEasing easing)
    {
        switch (easing)
        {
            case AnimationEasing.EaseIn:    return x * x;
            case AnimationEasing.EaseOut:   return 1f - (1f - x) * (1f - x);
            case AnimationEasing.EaseInOut: return x * x * (3f - 2f * x);
            default:                        return x;
        }
    }

    // Animated cameras go back to where they were placed, so the next run starts the same.
    private void PutCamerasBack()
    {
        foreach (var move in _moves.Values) if (move != null) StopCoroutine(move);
        _moves.Clear();
        foreach (var placed in _placedAt)
            if (placed.Key != null) placed.Key.transform.SetPositionAndRotation(placed.Value.position, placed.Value.rotation);
        _placedAt.Clear();

        // And their own post back.
        foreach (var held in _volumeWas)
        {
            if (held.Key == null) continue;
            held.Key.Profile       = held.Value.profile;
            held.Key.FocusTracking = held.Value.tracking;
            held.Key.InvalidateCachedProfile();
        }
        _volumeWas.Clear();
        foreach (var copy in _focusCopies) DestroyFocusCopy(copy);
        _focusCopies.Clear();
    }

    // Each shot camera's own profile and Focus Tracking, from before the sequence put a shot's Volume on it.
    private readonly Dictionary<CinemachineVolumeSettings, (VolumeProfile profile, CinemachineVolumeSettings.FocusTrackingMode tracking)>
        _volumeWas = new Dictionary<CinemachineVolumeSettings, (VolumeProfile, CinemachineVolumeSettings.FocusTrackingMode)>();

    // Focus On View Target's copies of shot Volumes (the assets are never written), thrown away at the end.
    private readonly List<VolumeProfile> _focusCopies = new List<VolumeProfile>();

    /// <summary>
    /// The shot camera wears the shot's own Volume, with Focus Tracking off. Focus On View Target: a copy of it
    /// whose Focus Distance is how far the camera stands from the View Target as the shot starts — set once.
    /// A shot with no Volume on a camera an earlier shot dressed puts that camera's own back.
    /// </summary>
    private void WearShotVolume(Shot shot, int index)
    {
        var volume = shot.camera.GetComponent<CinemachineVolumeSettings>();
        if (volume == null)
        {
            Debug.LogWarning($"[CameraSequence] Shot {index + 1}: '{shot.camera.name}' has no Cinemachine Volume Settings — " +
                             "its Volume isn't worn (open the storyboard to give it them).");
            return;
        }

        if (shot.volumeProfile == null)
        {
            Debug.LogWarning($"[CameraSequence] Shot {index + 1}: no Volume of its own — '{shot.camera.name}' keeps its own post " +
                             "(open the storyboard to make one).");
            if (_volumeWas.TryGetValue(volume, out var was))
            {
                volume.Profile       = was.profile;
                volume.FocusTracking = was.tracking;
                volume.InvalidateCachedProfile();
                _volumeWas.Remove(volume);
            }
            return;
        }

        if (!_volumeWas.ContainsKey(volume)) _volumeWas[volume] = (volume.Profile, volume.FocusTracking);
        var profile = shot.volumeProfile;
        if (shot.focusViewTarget && shot.viewTarget != null)
        {
            float distance = (shot.camera.transform.position - shot.viewTarget.position).magnitude;
            profile = FocusedCopy(shot.volumeProfile, distance);
            if (profile != shot.volumeProfile) _focusCopies.Add(profile);
            Debug.Log($"[CameraSequence] Shot {index + 1}: '{shot.camera.name}' wears '{shot.volumeProfile.name}', " +
                      $"focused {distance:F2} away on '{shot.viewTarget.name}'.");
        }
        else
        {
            Debug.Log($"[CameraSequence] Shot {index + 1}: '{shot.camera.name}' wears '{shot.volumeProfile.name}'.");
        }

        volume.FocusTracking = CinemachineVolumeSettings.FocusTrackingMode.None;
        volume.Profile       = profile;
        volume.InvalidateCachedProfile();
    }

    /// <summary>
    /// A copy of <paramref name="source"/> (every override its own copy) with its Depth of Field Focus Distance set to
    /// <paramref name="distance"/>. The source itself when it has no Depth of Field.
    /// </summary>
    public static VolumeProfile FocusedCopy(VolumeProfile source, float distance)
    {
        if (!source.Has<DepthOfField>())
        {
            Debug.LogWarning($"[CameraSequence] '{source.name}' has no Depth of Field — Focus On View Target has nothing to focus.");
            return source;
        }

        var copy = ScriptableObject.CreateInstance<VolumeProfile>();
        copy.name      = source.name + " (focused)";
        copy.hideFlags = HideFlags.DontSave;
        foreach (var component in source.components)
        {
            var c = Instantiate(component);
            c.hideFlags = HideFlags.DontSave;
            copy.components.Add(c);
        }
        copy.TryGet(out DepthOfField dof);
        dof.focusDistance.overrideState = true;
        dof.focusDistance.value         = Mathf.Max(0f, distance);
        copy.isDirty = true;
        return copy;
    }

    /// <summary>Throws away a <see cref="FocusedCopy"/> and its overrides.</summary>
    public static void DestroyFocusCopy(VolumeProfile copy)
    {
        if (copy == null) return;
        foreach (var component in copy.components)
            if (component != null) DestroyImmediate(component);
        DestroyImmediate(copy);
    }

    private void PlayShotAudio(Shot shot)
    {
        _shotSource.Stop();
        if (!useAudio || shot.audio == null) return;
        _shotSource.clip = shot.audio;
        _shotSource.Play();
    }

    private IEnumerator FadeOutAudio(float time)
    {
        Debug.Log($"[CameraSequence] Audio fade out {time:F1}s (sequence {(_sequenceSource.isPlaying ? "playing" : "silent")}, " +
                  $"shot {(_shotSource.isPlaying ? "playing" : "silent")}).");
        float seqFrom  = _sequenceSource.volume;
        float shotFrom = _shotSource.volume;
        float elapsed = 0f;
        while (elapsed < time)
        {
            elapsed += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(elapsed / time);
            _sequenceSource.volume = seqFrom  * k;
            _shotSource.volume     = shotFrom * k;
            yield return null;
        }
        _audioFade = null;
        StopAudioFade();
    }

    // Stops both sources and puts their volume back for the next run.
    private void StopAudioFade()
    {
        if (_audioFade != null) StopCoroutine(_audioFade);
        _audioFade = null;
        if (_sequenceSource == null || _shotSource == null) return;
        _sequenceSource.Stop();
        _shotSource.Stop();
        _sequenceSource.volume = 1f;
        _shotSource.volume     = 1f;
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

    // Each shot's Fog Centre holds the world's fog middle until the hand back. The fog's own numbers
    // stay the world's, but for the distance of a shot with Fog Distance Override.
    private void HoldFog()
    {
        if (!_holdingFogCentre)
        {
            _holdingFogCentre = true;
            _fogPinned        = false;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += HoldFogCentre;
        }
    }

    /// <summary>
    /// Sets where the fog's middle sits for this shot — once, as it starts, from where its camera stands then
    /// (an animated camera has already taken its first step, so an Orbit is facing its point). It stays put
    /// while the camera moves. A View that never meets the water keeps the last shot's spot.
    /// </summary>
    private void PinFog(Shot shot, int index)
    {
        WhiteFadeSettings.RadiusOverride = shot.overrideFogDistance ? shot.fogDistance : (float?)null;
        if (shot.overrideFogDistance)
            Debug.Log($"[CameraSequence] Shot {index + 1}: fog distance {shot.fogDistance:F1} (override).");

        if (shot.fogCentre == FogCentre.Boat)
        {
            _fogPinned = false;
            LevelSelectBoatToShaders.Push(boatControl);
            Debug.Log($"[CameraSequence] Shot {index + 1}: fog on the boat.");
            return;
        }

        float waterY = Shader.GetGlobalVector(BoatCentreId).y;
        if (FogSpot(shot.fogCentre, shot.camera.transform, waterY, out var spot))
        {
            _fogSpot   = spot;
            _fogPinned = true;
            Shader.SetGlobalVector(WhiteFadeCentreId, _fogSpot);
            Debug.Log($"[CameraSequence] Shot {index + 1}: fog on the {(shot.fogCentre == FogCentre.View ? "view" : "camera position")} " +
                      $"at {spot:F1} (water level {waterY:F2}, '{shot.camera.name}' at {shot.camera.transform.position:F1}).");
        }
        else
        {
            Debug.LogWarning($"[CameraSequence] Shot {index + 1}: '{shot.camera.name}' looks level or up, so its view never meets " +
                             $"the water — the fog stays {(_fogPinned ? $"at {_fogSpot:F1}" : "on the boat")}.");
        }
    }

    /// <summary>
    /// Keeps the fog's centre on the shot's spot, just before the game camera draws — LevelSelectBoatToShaders
    /// writes the boat in there every LateUpdate, so it is written back over that each frame.
    /// </summary>
    private void HoldFogCentre(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
    {
        if (!_fogPinned) return;
        if (cam.cameraType != CameraType.Game || !cam.TryGetComponent<CinemachineBrain>(out _)) return;
        Shader.SetGlobalVector(WhiteFadeCentreId, _fogSpot);
    }

    /// <summary>
    /// Where a shot's fog middle sits, from its camera: View = the spot it looks at, Camera Position = the water
    /// straight under it. False for Boat, and for a View that never meets the water. Shared with the Cm View's preview.
    /// </summary>
    public static bool FogSpot(FogCentre centre, Transform camera, float waterY, out Vector3 spot)
    {
        spot = default;
        if (centre == FogCentre.Boat) return false;
        if (centre == FogCentre.CameraPosition)
        {
            Vector3 p = camera.position;
            spot = new Vector3(p.x, waterY, p.z);
            return true;
        }
        return ViewSpot(camera, waterY, out spot);
    }

    /// <summary>
    /// The spot a camera is looking at: where the middle of its view meets the water level. False when it
    /// never does — looking level or up, or from under the water. Shared with the Cm View's preview.
    /// </summary>
    public static bool ViewSpot(Transform camera, float waterY, out Vector3 spot)
    {
        Vector3 origin = camera.position;
        Vector3 dir    = camera.forward;
        spot = default;
        if (dir.y > -1e-4f) return false;

        float along = (waterY - origin.y) / dir.y;
        if (along <= 0f) return false;

        spot = origin + dir * along;
        return true;
    }

    private void ReleaseFog()
    {
        if (_holdingFogCentre)
        {
            _holdingFogCentre = false;
            _fogPinned        = false;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= HoldFogCentre;
            WhiteFadeSettings.RadiusOverride = null;
            // Straight back onto the boat, rather than waiting on its next LateUpdate.
            LevelSelectBoatToShaders.Push(boatControl);
            Debug.Log("[CameraSequence] Fog Centre released — back on the boat.");
        }
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
        // Nor an animated camera left mid-move.
        PutCamerasBack();
        // Never leave the music held by a dead sequence either.
        ReleaseMusic();
        // Nor its hold on the fog's centre.
        ReleaseFog();
        // Nor its own sound playing on, part faded.
        StopAudioFade();
    }
}
