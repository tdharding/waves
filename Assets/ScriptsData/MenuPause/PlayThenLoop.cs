using UnityEngine;

public class PlayThenLoop : MonoBehaviour
{
    [Header("Play first, then loop second")]
    public AudioClip firstClip;
    public AudioClip loopClip;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
    }

    public void Begin()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        StopAllCoroutines();
        StartCoroutine(PlaySequence());
    }

    public void Stop()
    {
        StopAllCoroutines();
        audioSource.Stop();
    }

    private System.Collections.IEnumerator PlaySequence()
    {
        if (firstClip != null)
        {
            audioSource.clip = firstClip;
            audioSource.loop = false;
            audioSource.Play();
            yield return new WaitForSeconds(firstClip.length);
        }

        if (loopClip != null)
        {
            audioSource.clip = loopClip;
            audioSource.loop = true;
            audioSource.Play();
        }
    }
}