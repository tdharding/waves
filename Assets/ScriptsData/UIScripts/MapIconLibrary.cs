using UnityEngine;

/// <summary>
/// Tweakable shape parameters for the procedural map icons. Both the runtime mesh builders
/// (UIMapController.Build*Icon) and the editor previews read from here, so the Map Icon Library
/// window is the single place to author how each icon looks. Values are fractions of the icon's
/// half-size (so they're resolution/scale independent). Defaults reproduce the original shapes.
/// </summary>
[CreateAssetMenu(fileName = "MapIconLibrary", menuName = "WaveGrid/Map Icon Library")]
public class MapIconLibrary : ScriptableObject
{
    [Min(6)] public int fanSegments = 18;   // circle/fan resolution for icon curves

    [System.Serializable]
    public class SpikeParams
    {
        [Range(0.1f, 1f)]  public float widthFactor      = 0.7f;   // base half-width / size
        [Range(0f, 0.5f)]  public float baseFadeFraction = 0.1f;   // bottom portion that fades out
    }

    [System.Serializable]
    public class FishBowlParams
    {
        [Range(0.2f, 0.9f)]  public float bowlRadiusFactor = 0.55f;
        [Range(0.02f, 0.4f)] public float stickWidthFactor = 0.16f;
    }

    [System.Serializable]
    public class StreetLightParams
    {
        [Range(0.2f, 4f)]    public float sizeFactor        = 1f;    // overall size multiplier for the icon
        [Range(0.1f, 0.7f)]  public float bulbRadiusFactor  = 0.38f;
        [Range(0.3f, 1f)]    public float haloRadiusFactor  = 0.75f;
        [Range(-0.3f, 0.7f)] public float bulbCenterYFactor = 0.35f;
        [Range(0.02f, 0.3f)] public float stickWidthFactor  = 0.13f;
        [Range(0f, 1f)]      public float haloAlpha          = 0.5f;
    }

    public SpikeParams       spike       = new SpikeParams();
    public FishBowlParams    fishBowl    = new FishBowlParams();
    public StreetLightParams streetLight = new StreetLightParams();

    // Fallback instance (default values) when no library asset is assigned.
    static MapIconLibrary _default;
    public static MapIconLibrary Default =>
        _default != null ? _default : (_default = CreateInstance<MapIconLibrary>());
}
