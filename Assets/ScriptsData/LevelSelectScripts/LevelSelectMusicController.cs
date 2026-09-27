using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class LevelSelectMusicController : MonoBehaviour
{
    public static LevelSelectMusicController Instance;

    [SerializeField] private LevelSelectDesignerData data;

    public bool playOnStart = true;
    private AudioSource _source;

    // Held while a camera sequence plays its own audio. Holds stack; the music comes back when the last one lets go.
    private int  _holds;
    private bool _playWhenReleased;
    private bool _pausedByHold;

    public bool IsHeld => _holds > 0;

    private void Awake()
    {
        Instance = this;
        _source = GetComponent<AudioSource>();
    }

    private void Start()
    {
        if (!playOnStart) return;
        Play();
    }

    public void Play()
    {
        if (data == null) return;
        if (data.musicIntro == null && data.musicLoop == null) return;
        if (IsHeld)
        {
            _playWhenReleased = true;
            Debug.Log("[LevelSelectMusic] Play: held — starts when released.");
            return;
        }
        StopAllCoroutines();
        _pausedByHold = false;
        StartCoroutine(PlaySequence());
    }

    public void FadeIn(float duration)
    {
        Play();
        if (IsHeld) return;
        StartCoroutine(FadeInRoutine(duration));
    }

    /// <summary>Pause the music (or keep it from starting) until Release.</summary>
    public void Hold()
    {
        _holds++;
        if (_holds > 1) return;
        if (_source.isPlaying)
        {
            _source.Pause();
            _pausedByHold = true;
        }
        Debug.Log($"[LevelSelectMusic] Hold: {(_pausedByHold ? "paused" : "nothing playing")}.");
    }

    /// <summary>Let go of a Hold — the music picks up where it paused, or starts if it was waiting to.</summary>
    public void Release()
    {
        if (_holds == 0) return;
        if (--_holds > 0) return;

        if (_playWhenReleased)
        {
            _playWhenReleased = false;
            Debug.Log("[LevelSelectMusic] Release: starting.");
            Play();
        }
        else if (_pausedByHold)
        {
            _pausedByHold = false;
            _source.UnPause();
            Debug.Log("[LevelSelectMusic] Release: resumed.");
        }
    }

    private IEnumerator FadeInRoutine(float duration)
    {
        float targetVolume = _source.volume;
        _source.volume = 0;
        float elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _source.volume = Mathf.Lerp(0, targetVolume, elapsed / duration);
            yield return null;
        }
        _source.volume = targetVolume;
    }

    private IEnumerator PlaySequence()
{
        if (data.musicIntro != null)
        {
            _source.clip = data.musicIntro;
            _source.loop = false;
            _source.Play();
            // Not a fixed wait — a Hold can pause the intro part way through.
            yield return new WaitUntil(() => !_source.isPlaying && !IsHeld);
        }

        if (data.musicLoop != null)
        {
            _source.clip = data.musicLoop;
            _source.loop = true;
            _source.Play();
        }
    }
}
