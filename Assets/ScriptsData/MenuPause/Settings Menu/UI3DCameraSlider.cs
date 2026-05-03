using UnityEngine;
using UnityEngine.UI;

public class UI3DCameraSlider : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Slider slider;
    [SerializeField] private UIOverlayCameraController uiOverlay;

    [Header("Settings")]
    [SerializeField] private float minX = -6f;
    [SerializeField] private float maxX = -2f;

    private void Awake()
    {
        if (slider == null)
        {
            Debug.LogWarning("UI3DCameraSlider: No slider assigned.");
            return;
        }

        slider.minValue = minX;
        slider.maxValue = maxX;

        float saved = SettingsControls.GetUI3DCameraX();

        // If the saved value is outside the valid range it's stale/unset —
        // read the camera's actual position as the true default instead.
        if (saved < minX || saved > maxX)
        {
            saved = (uiOverlay != null && uiOverlay.uiCamera != null)
                ? uiOverlay.uiCamera.transform.localPosition.x
                : Mathf.Lerp(minX, maxX, 0.5f);

            SettingsControls.SetUI3DCameraX(saved);
        }

        slider.value = saved;
        ApplyCameraX(saved);

        slider.onValueChanged.AddListener(OnSliderChanged);
    }

    private void OnSliderChanged(float value)
    {
        ApplyCameraX(value);
        SettingsControls.SetUI3DCameraX(value);
    }

    private void ApplyCameraX(float x)
    {
        if (uiOverlay == null || uiOverlay.uiCamera == null) return;
        Vector3 pos = uiOverlay.uiCamera.transform.localPosition;
        pos.x = x;
        uiOverlay.uiCamera.transform.localPosition = pos;
    }
}