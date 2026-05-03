using UnityEngine;
using UnityEngine.UI;

public class RenderScaleController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform renderPanel;
    [SerializeField] private Slider scaleSlider;

    [Header("Scale Settings")]
    [SerializeField] private float minScale = 0.5f;
    [SerializeField] private float maxScale = 2.5f;
    [SerializeField] private float defaultScale = 1f;

    private void Awake()
    {
        scaleSlider.minValue = minScale;
        scaleSlider.maxValue = maxScale;
        scaleSlider.value = defaultScale;

        ApplyScale(defaultScale);

        scaleSlider.onValueChanged.AddListener(ApplyScale);
    }

    private void ApplyScale(float value)
    {
        renderPanel.localScale = Vector3.one * value;
    }
}
