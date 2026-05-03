using UnityEngine;
using UnityEngine.UI;

public class VolumeSliderController : MonoBehaviour
{
    [Header("References")]
    public Slider volumeSlider;

    private void Awake()
    {
        if (volumeSlider == null)
        {
            Debug.LogWarning("VolumeSliderController: No slider assigned in inspector.");
            return;
        }

        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.value = SettingsControls.GetMasterVolume();

        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
    }

    private void OnVolumeChanged(float value)
    {
        SettingsControls.SetMasterVolume(value);
    }
}