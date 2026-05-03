using UnityEngine;

public class BellSoundPlayer : MonoBehaviour
{
    public AudioSource bellAudio;

    public void PlayBellSound()
    {
        if (bellAudio != null)
            bellAudio.Play();
    }
}
