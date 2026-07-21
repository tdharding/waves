using UnityEngine;

// Publishes the cube-building window shader's lighting inputs as shader globals:
// _WindowFishPoints / _WindowFishCount (the light sources) and _WindowLightRadius
// (their reach). All three are bare $Globals uniforms in WindowTiling.hlsl — the same
// class as _SoulFishRadius in SoulFishWaveMask.hlsl — so they must be pushed with
// Shader.SetGlobal* every frame, never material.SetFloat alone and never set once.
//
// Light sources each frame:
//   • every live soul fish registered with FishingController (weight = fishWeight),
//   • the boat, once it carries souls — weight scales with LevelSoulTracker.SoulsOnBoat
//     up to fullBoatWeight at soulsForFullBoat.
//
// Add this component to the FishingController GameObject in the gameplay scene.
// When absent (menus, level select) the count global stays 0 and windows simply
// remain unlit apart from the shader's ambient fraction.
// Runs after the default execution order so fish have already applied their own LateUpdate
// water-snap for this frame before their positions are published — otherwise the published
// points can be a frame stale.
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class WindowLightManager : MonoBehaviour
{
    // Must match WINDOW_MAX_LIGHTS in WindowTiling.hlsl. SetGlobalVectorArray locks
    // the array length on first publish — never change one without the other.
    const int MaxLights = 16;

    static readonly int WindowFishPointsID  = Shader.PropertyToID("_WindowFishPoints");
    static readonly int WindowFishCountID   = Shader.PropertyToID("_WindowFishCount");
    static readonly int WindowLightRadiusID = Shader.PropertyToID("_WindowLightRadius");

    [Header("Window Light Radius")]
    [Tooltip("World-unit radius of each light source's influence on the windows. " +
             "For reference, BoatLightController's own light pool is 6.55.")]
    [SerializeField] float _radius = 10f;

    [Tooltip("Optional: the block window material. When assigned it also receives the radius " +
             "via material.SetFloat, covering the non-SRP-batched path (belt-and-braces, the " +
             "same dual-write WaveMaterialController.SetGlobalsBackedFloat uses for the " +
             "soul-fish edge-noise globals). Leave empty to rely on the global alone.")]
    [SerializeField] Material windowMaterial;

    // Public accessor so other scripts can animate the radius at runtime,
    // mirroring BoatLightController.Radius.
    public float Radius
    {
        get => _radius;
        set { _radius = value; ApplyRadius(); }
    }

    [Header("Light weights")]
    [Tooltip("Signal weight contributed by each free-swimming soul fish.")]
    [SerializeField] float fishWeight = 1f;

    [Tooltip("Souls on the boat at which the boat reaches its full light weight.")]
    [SerializeField] float soulsForFullBoat = 6f;

    [Tooltip("Boat light weight when carrying soulsForFullBoat souls (scales linearly below).")]
    [SerializeField] float fullBoatWeight = 3f;

    [Tooltip("Clamp each light to at most this far above the water surface. Soul-fish spawn " +
             "splines are authored ABOVE the water and FishFishingBehaviour only pulls a fish " +
             "under while it is within activeDistance of the boat — without this clamp, every " +
             "un-snapped fish reports its spline height and lights the TOP storeys instead of " +
             "the ones by the water. Genuine depth below the surface is preserved.")]
    [SerializeField] float maxHeightAboveWater = 0f;

    readonly Vector4[] pointBuffer = new Vector4[MaxLights];

    FishingController fishing;
    Transform boatRoot;
    Transform waterTransform;

    void LateUpdate()
    {
        if (fishing == null) fishing = FindObjectOfType<FishingController>();
        if (waterTransform == null && LevelDataController.Instance != null)
            waterTransform = LevelDataController.Instance.GetWaveTransform();

        // Lights never sit above the water surface (see maxHeightAboveWater). float.MaxValue
        // when there is no water reference, so the clamp is a no-op rather than a wrong guess.
        float ceilingY = waterTransform != null
            ? waterTransform.position.y + maxHeightAboveWater
            : float.MaxValue;

        int count = 0;

        if (fishing != null)
        {
            var fish = fishing.RegisteredFish;
            for (int i = 0; i < fish.Count && count < MaxLights - 1; i++)
            {
                if (fish[i] == null) continue;
                Vector3 p = fish[i].transform.position;
                pointBuffer[count++] = new Vector4(p.x, Mathf.Min(p.y, ceilingY), p.z, fishWeight);
            }
        }

        int souls = LevelSoulTracker.Instance != null ? LevelSoulTracker.Instance.SoulsOnBoat : 0;
        if (souls > 0)
        {
            if (boatRoot == null && LevelDataController.Instance != null)
                boatRoot = LevelDataController.Instance.GetBoatRoot();

            if (boatRoot != null)
            {
                float w = Mathf.Clamp01(souls / Mathf.Max(1f, soulsForFullBoat)) * fullBoatWeight;
                Vector3 p = boatRoot.position;
                pointBuffer[count++] = new Vector4(p.x, Mathf.Min(p.y, ceilingY), p.z, w);
            }
        }

        // Clear stale entries so a shrinking count never leaves ghost lights behind.
        for (int i = count; i < MaxLights; i++)
            pointBuffer[i] = Vector4.zero;

        Shader.SetGlobalVectorArray(WindowFishPointsID, pointBuffer);
        Shader.SetGlobalFloat(WindowFishCountID, count);

        // Re-published every frame, never set once: a set-once global is the timing trap
        // the soul-fish edge-noise fringe was mis-diagnosed on.
        ApplyRadius();
    }

    // _WindowLightRadius is a bare $Globals uniform in WindowTiling.hlsl (not a blackboard
    // property), so write it through both routes — per-material for the non-batched path
    // and globally for the SRP-batched path. Same shape as
    // WaveMaterialController.SetGlobalsBackedFloat, which fixed the soul-fish edge-noise
    // globals not reaching the batched runtime draw.
    void ApplyRadius()
    {
        if (windowMaterial != null) windowMaterial.SetFloat(WindowLightRadiusID, _radius);
        Shader.SetGlobalFloat(WindowLightRadiusID, _radius);
    }

    // Live-preview the radius while scrubbing the inspector (editor only).
    void OnValidate() => ApplyRadius();

    void OnDisable()
    {
        // Zero out so windows go dark cleanly when the manager is off.
        System.Array.Clear(pointBuffer, 0, pointBuffer.Length);
        Shader.SetGlobalVectorArray(WindowFishPointsID, pointBuffer);
        Shader.SetGlobalFloat(WindowFishCountID, 0f);
    }
}
