using UnityEngine;
using UnityEngine.VFX;

public class WaveCrestSplashVFX : MonoBehaviour
{
    [Header("References")]
    public VisualEffect splashVFX;

    [Header("Trigger Settings")]
    public float triggerHeight = -1.3f;   // <-- Adjustable in Inspector

    private Transform waterTransform;
    private Material mat;

private float previousHeight;
private float previousDelta;

    void Start()
    {
        if (LevelDataController.Instance == null)
            return;

        waterTransform = LevelDataController.Instance.GetWaveTransform();

        if (!waterTransform)
            return;

        mat = waterTransform.GetComponent<MeshRenderer>().sharedMaterial;
    }

    void LateUpdate()
    {
        if (!waterTransform || !mat)
            return;

        var p = WaveUtils.ReadParams(waterTransform, mat);
        float height = WaveUtils.SampleHeight(transform.position, p);

        // Move emitter
        Vector3 pos = transform.position;
        pos.y = p.origin.y + height;
        transform.position = pos;

        float worldY = transform.position.y;
float delta = worldY - previousHeight;

// Crest detection: was rising, now falling
if (previousDelta > 0f && delta <= 0f)
{
    splashVFX.SendEvent("OnWaveCrest");
}

previousDelta = delta;
previousHeight = worldY;
}
}