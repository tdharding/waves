using UnityEngine;
using TMPro;

public class ButtonTextPulse : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI targetText;
    [SerializeField] private float speed = 2f;

    private Color baseColor;
    private float minA = 42f / 255f;
    private float maxA = 1f;

    void Start()
    {
        baseColor = targetText.color;
    }

    void Update()
    {
        // Ping-pong between 42 and 255 alpha
        float a = Mathf.Lerp(minA, maxA, (Mathf.Sin(Time.time * speed) + 1f) * 0.5f);
        targetText.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
    }
}
