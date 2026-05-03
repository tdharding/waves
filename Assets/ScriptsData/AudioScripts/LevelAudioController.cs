using UnityEngine;

public class LevelAudioController : MonoBehaviour
{
    public static LevelAudioController Instance;

    [Header("References")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private PlayThenLoop musicPlayer;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Called by WaveMaterialController when a preset is applied.
    /// Loads the music clips but does not start playback.
    /// </summary>
    public void OnPresetChanged(WavePreset preset)
    {
        if (musicPlayer == null) return;
        musicPlayer.firstClip = preset != null ? preset.musicIntro : null;
        musicPlayer.loopClip  = preset != null ? preset.musicLoop  : null;
    }

    /// <summary>
    /// Starts playback of whichever preset was last loaded.
    /// Call this when the moment is right (e.g. gameplay start, post-gong).
    /// </summary>
    public void PlayMusic()
    {
        musicPlayer?.Begin();
    }

    public void StopMusic()
    {
        musicSource?.Stop();
        musicPlayer?.Stop();
    }
}
