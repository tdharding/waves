using UnityEngine;

// Place in scene alongside the wave plane. Holds the scene lights that are
// driven by WavePreset data at runtime when WaveMaterialController applies a preset.
public class WaveLightController : MonoBehaviour
{
    public static WaveLightController Instance { get; private set; }

    [Tooltip("Scene lights to be driven by the active WavePreset.")]
    public Light[] lights;

    void OnEnable()  { if (Instance == null) Instance = this; }
    void OnDisable() { if (Instance == this) Instance = null; }

    public void ApplyPreset(WavePreset preset)
    {
        if (preset?.lights == null || lights == null) return;

        int count = Mathf.Min(lights.Length, preset.lights.Length);
        for (int i = 0; i < count; i++)
        {
            if (lights[i] == null) continue;
            lights[i].color     = preset.lights[i].color;
            lights[i].intensity = preset.lights[i].intensity;
            lights[i].range     = preset.lights[i].range;
        }
    }

    public void ApplyPresetLerped(WavePreset preset, float t)
    {
        if (preset?.lights == null || lights == null) return;

        int count = Mathf.Min(lights.Length, preset.lights.Length);
        for (int i = 0; i < count; i++)
        {
            if (lights[i] == null) continue;
            lights[i].color     = Color.Lerp(lights[i].color,     preset.lights[i].color,     t);
            lights[i].intensity = Mathf.Lerp(lights[i].intensity, preset.lights[i].intensity, t);
            lights[i].range     = Mathf.Lerp(lights[i].range,     preset.lights[i].range,     t);
        }
    }
}
