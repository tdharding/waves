using UnityEngine;

// Drives the global "BoatRadiusShadow" shader value. Because the property is a
// GLOBAL (non-exposed) shader property, setting it once here affects every shader
// graph that reads it (currently the maze rock, spline walls and arena walls).
// Attach to a single object in the scene.
[ExecuteAlways]
public class BoatLightController : MonoBehaviour
{
    [Header("Boat Radius Shadow")]
    [Tooltip("Radius of the shadow that surrounds the boat. Written to the global shader property each frame.")]
    [SerializeField] private float _radius = 6.55f;

    [Tooltip("Softness/feather of the shadow edge. Leave unticked if the shaders don't use it.")]
    [SerializeField] private bool _driveSoftness = false;
    [SerializeField] private float _softness = 1f;

    private static readonly int RadiusId   = Shader.PropertyToID("_BoatRadiusShadow");
    private static readonly int SoftnessId = Shader.PropertyToID("_BoatRadiusShadowSoftness");

    // Public accessor so other scripts can animate the radius at runtime.
    public float Radius
    {
        get => _radius;
        set { _radius = value; Apply(); }
    }

    private void OnEnable()   => Apply();
    private void Update()     => Apply();
    private void OnValidate() => Apply(); // live-preview when tweaking in the inspector

    private void Apply()
    {
        Shader.SetGlobalFloat(RadiusId, _radius);
        if (_driveSoftness) Shader.SetGlobalFloat(SoftnessId, _softness);
    }
}
