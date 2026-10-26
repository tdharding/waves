using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gives a level the river stone's look — the job LevelSelectDesignerData.ApplyAesthetics does on
/// the map, for a scene that has no designer data. Place one in a level that uses the river run
/// material with Interior ticked.
///
/// The stone's numbers are bare $Globals with nothing on disk behind them, so with nothing pushing
/// them a level would draw whatever the last scene left there — or, in a build, plain stone with no
/// seams. Pushed every frame, which is also what brings them back after a shader reimport. The
/// Run Shading Tuner still wins while Apply Live is on, so an interior preset is tuned in place.
///
/// It also hands the stone the boat. The level's boat light is _BoatWorldCenter (BoatToWaterMaterial)
/// and _BoatRadiusShadow (BoatLightController), copied across under the stone's own name because
/// RiverRunShader already declares _BoatWorldCenter as a graph property, which the stone's hlsl
/// can't declare again. And the white fade is centred on the boat, which on the map
/// LevelSelectBoatToShaders does and in a level nothing else does.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(1000)]
public class RiverRunInteriorShading : MonoBehaviour
{
    [Header("Presets")]
    [Tooltip("The stone: stone types, seams, waterline, decals and the Interior settings. Made and " +
             "saved in the Level Select Run Shading Tuner like any other.")]
    [SerializeField] private LevelSelectRiverStructurePreset structurePreset;

    [Tooltip("The white fade round the boat. Empty = no fade.")]
    [SerializeField] private LevelSelectWhiteFadePreset whiteFadePreset;

    [Header("Aesthetics")]
    [Tooltip("The same as White Gradient Pos in the Level Select Designer's Aesthetics.")]
    [SerializeField] private float whiteGradientPos = 0f;

    [Tooltip("The same as Distance Fade Radius in the Level Select Designer's Aesthetics.")]
    [SerializeField] private float distanceFadeRadius = 10f;

    static readonly int WhiteGradientPosId   = Shader.PropertyToID("_WhiteGradientPos");
    static readonly int DistanceFadeRadiusId = Shader.PropertyToID("_DistanceFadeRadius");
    static readonly int BoatCentreId         = Shader.PropertyToID("_BoatWorldCenter");
    static readonly int BoatRadiusId         = Shader.PropertyToID("_BoatRadiusShadow");
    static readonly int StoneBoatLightId     = Shader.PropertyToID("_RiverRunBoatLight");
    static readonly int WhiteFadeCentreId    = Shader.PropertyToID("_WhiteFadeCentre");

    private bool _warned;

    private void OnEnable()  => RenderPipelineManager.beginCameraRendering += PushBeforeDraw;
    private void OnDisable() => RenderPipelineManager.beginCameraRendering -= PushBeforeDraw;

    /// <summary>
    /// The white fade's centre, in LateUpdate rather than with the rest: a Camera Sequence pins it
    /// to a shot's spot just before the camera draws, and that has to land after this does.
    /// </summary>
    private void LateUpdate()
    {
        Shader.SetGlobalVector(WhiteFadeCentreId, Shader.GetGlobalVector(BoatCentreId));
    }

    /// <summary>
    /// Everything else, just before each camera draws — after every Update and LateUpdate has
    /// moved the boat and its light, and in edit mode too, where nothing else runs every frame.
    /// </summary>
    private void PushBeforeDraw(ScriptableRenderContext context, Camera cam)
    {
        if (structurePreset == null && !_warned && Application.isPlaying)
        {
            _warned = true;
            Debug.LogWarning("[RiverRunInteriorShading] No structure preset, so the stone draws plain " +
                             "with no seams or decals.", this);
        }

        RiverRunShadingSettings.Push(structurePreset != null ? structurePreset.runShading : null);
        WhiteFadeSettings.Push(whiteFadePreset != null ? whiteFadePreset.whiteFade : null);
        WhiteFadeScreenPicture.Push(whiteFadePreset != null ? whiteFadePreset.screenPicture : null);

        Shader.SetGlobalFloat(WhiteGradientPosId,   whiteGradientPos);
        Shader.SetGlobalFloat(DistanceFadeRadiusId, distanceFadeRadius);

        Vector4 boat = Shader.GetGlobalVector(BoatCentreId);
        boat.w = Shader.GetGlobalFloat(BoatRadiusId);
        Shader.SetGlobalVector(StoneBoatLightId, boat);
    }
}
