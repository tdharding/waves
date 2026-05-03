using UnityEngine;

public class CrashSFXPlayer : MonoBehaviour
{
    [Header("Random crash sounds")]
    public AudioClip[] crashClips;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        // Match behaviour of your working script
        audioSource.playOnAwake = false;
        audioSource.loop = false;

        // Important: matches PlayThenLoop behaviour
        audioSource.ignoreListenerPause = true;
        audioSource.ignoreListenerVolume = true;
    }

    public void PlayRandomCrash()
    {
        if (crashClips == null || crashClips.Length == 0)
            return;

        int index = Random.Range(0, crashClips.Length);

        // EXACTLY like your script: set clip, then Play()
        audioSource.clip = crashClips[index];
        audioSource.loop = false;
        audioSource.Play();
    }
}
