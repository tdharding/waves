using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class LevelSelectMusicController : MonoBehaviour
{
    public static LevelSelectMusicController Instance;

    [SerializeField] private LevelSelectDesignerData data;

    public bool playOnStart = true;
    private AudioSource _source;

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
        StopAllCoroutines();
        StartCoroutine(PlaySequence());
    }

    public void FadeIn(float duration)
    {
        Play();
        StartCoroutine(FadeInRoutine(duration));
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
            yield return new WaitForSeconds(data.musicIntro.length);
        }

        if (data.musicLoop != null)
        {
            _source.clip = data.musicLoop;
            _source.loop = true;
            _source.Play();
        }
    }
}
