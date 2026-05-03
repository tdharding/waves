using UnityEngine;

public class SteeringWheelLightController : MonoBehaviour
{
    [Header("Lights (7 total: -3..0..+3)")]
    public MeshRenderer[] lightMeshes = new MeshRenderer[7];

    [Header("Emission Settings")]
    public float maxEmission = 3f;              // HDR multiplier for the "active" light
    public float falloffCurve = 1f;             // 1 = linear, >1 more sharply peaked
    public Color emissionColor = Color.white;   // Base emission color

    private Material[] lightMaterials;

    void Awake()
    {
        // Instantiate unique materials for emission control
        lightMaterials = new Material[lightMeshes.Length];
        for (int i = 0; i < lightMeshes.Length; i++)
        {
            if (lightMeshes[i] == null) continue;

            lightMaterials[i] = Instantiate(lightMeshes[i].material);
            lightMeshes[i].material = lightMaterials[i];
        }
    }

    // Called every frame by SteeringWheelControllerV2
    public void UpdateLights(float steerIndex)
    {
        for (int i = 0; i < lightMaterials.Length; i++)
        {
            // Convert array index (0..6) to steering index (−3..0..+3)
            int lightIndex = i - 3;

            float distance = Mathf.Abs(steerIndex - lightIndex);

            float brightness = Mathf.Clamp01(1f - distance);
            brightness = Mathf.Pow(brightness, falloffCurve);  // tweakable falloff profile

            Color emissive = emissionColor * (brightness * maxEmission);
            lightMaterials[i].SetColor("_EmissionColor", emissive);
        }
    }
}
