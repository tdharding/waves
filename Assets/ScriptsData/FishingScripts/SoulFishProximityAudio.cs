using UnityEngine;

public class SoulFishProximityAudio : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;
    public float maxVolume = 0.9f;
    public float fadeSpeed = 2f;
    public float soundFalloffDistance = 30f;

    private Transform _boat;
    private bool _soundPlaying;

    public void Init(Transform boat)
    {
        _boat = boat;
    }

    void Update()
    {
        if (_boat == null || audioSource == null) return;

        float d = Vector3.Distance(transform.position, _boat.position);
        float target = Mathf.Lerp(0f, maxVolume,
            Mathf.InverseLerp(soundFalloffDistance, 0f, d));

        if (!_soundPlaying && target > 0.01f)
        {
            audioSource.Play();
            _soundPlaying = true;
        }

        audioSource.volume = Mathf.MoveTowards(audioSource.volume, target,
            Time.deltaTime * fadeSpeed);

        if (_soundPlaying && audioSource.volume <= 0.01f)
        {
            audioSource.Stop();
            _soundPlaying = false;
        }
    }
}
