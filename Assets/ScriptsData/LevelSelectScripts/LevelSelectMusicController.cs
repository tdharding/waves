using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class LevelSelectMusicController : MonoBehaviour
{
    [SerializeField] private LevelSelectDesignerData data;

    private AudioSource _source;

    private void Awake() => _source = GetComponent<AudioSource>();

    private void Start()
    {
        if (data == null) return;
        if (data.musicIntro == null && data.musicLoop == null) return;
        StartCoroutine(PlaySequence());
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
