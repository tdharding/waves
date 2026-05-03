using UnityEngine;

public class SequentialAudioPlayer : MonoBehaviour
{
    public AudioClip clip1;
    public AudioClip clip2;
    public AudioClip clip3;

    private AudioSource audioSource;
    private bool isQuitting = false;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
    }

    private void OnApplicationQuit()
    {
        // Prevent false "destroyed" errors on editor quit
        isQuitting = true;
    }

    private void OnDestroy()
    {
        // Stop coroutine if this object is destroyed
        StopAllCoroutines();
    }

    private void Start()
    {
        StartCoroutine(PlaySequence());
    }

    private System.Collections.IEnumerator PlaySequence()
    {
        // Clip 1
        if (!PlayClip(clip1)) yield break;
        yield return new WaitForSeconds(clip1.length);
        if (this == null || isQuitting) yield break;

        // Clip 2
        if (!PlayClip(clip2)) yield break;
        yield return new WaitForSeconds(clip2.length);
        if (this == null || isQuitting) yield break;

        // Clip 3 (looping)
        if (!this) yield break;
        audioSource.clip = clip3;
        audioSource.loop = true;
        audioSource.Play();
    }

    private bool PlayClip(AudioClip clip)
    {
        if (clip == null || this == null) return false;
        audioSource.loop = false;
        audioSource.clip = clip;
        audioSource.Play();
        return true;
    }
}
