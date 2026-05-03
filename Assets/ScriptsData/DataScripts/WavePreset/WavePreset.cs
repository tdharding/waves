using UnityEngine;

[CreateAssetMenu(menuName = "Waves/Wave Preset")]
public class WavePreset : ScriptableObject
{
    public WaveMaterialController.WaveState state;

    [Header("Gameplay")]
    [Tooltip("If false, the player cannot activate Sonar while this preset is active.")]
    public bool sonarEnabled = true;

    [Header("Music")]
    [Tooltip("Optional one-shot intro that plays before the loop.")]
    public AudioClip musicIntro;
    [Tooltip("Looping music for this wave state.")]
    public AudioClip musicLoop;

    [Header("Lights")]
    public WaveLightEntry[] lights;

    [System.Serializable]
    public class WaveLightEntry
    {
        public Color color     = Color.white;
        public float intensity = 1f;
        public float range     = 10f;
    }
}
