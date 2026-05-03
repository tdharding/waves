using UnityEngine;

public class SoulCaptureSFXPlayer : MonoBehaviour
{
    [Header("Soul Capture SFX")]
    public AudioClip[] clips;
    public AudioSource audioSource;

    public void PlayRandomCapture()
    {
        if (clips == null || clips.Length == 0 || audioSource == null)
            return;

        AudioClip c = clips[Random.Range(0, clips.Length)];
        audioSource.PlayOneShot(c);
    }
}
