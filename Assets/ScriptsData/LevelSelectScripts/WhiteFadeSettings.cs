using UnityEngine;

/// <summary>
/// The level select's white fade: everything washed towards white with distance from the boat,
/// true at the boat and faded beyond it, in two rings — an outer one out to Radius and an inner
/// one out to Radius / Inner Division, each with its own strength — and a white gradient rising
/// from the bottom of the world, full below Gradient Bottom and gone by world Y = 1.
///
/// ONE set of numbers for the whole world. The river runs, the landscape hills and the spikes all
/// wear the same fade, through the one WhiteFade subgraph — so a graph only has to wire its colour
/// through the node, and nothing has to be typed in again per material.
///
/// Bare $Globals read by WhiteFade.hlsl, so nothing on disk remembers them: pushed every frame by
/// <see cref="LevelSelectDesignerData.ApplyAesthetics"/> from the world's
/// <see cref="LevelSelectWhiteFadePreset"/>. Tuned in the White Fade foldout of the Run Shading
/// Tuner or the Landscape Tuner — both edit that same preset, so they can never disagree.
///
/// Where the boat is comes separately, from LevelSelectBoatToShaders.
/// </summary>
[System.Serializable]
public class WhiteFadeSettings
{
    [Header("From The Boat")]
    [Tooltip("Metres from the boat to where the outer ring is fully faded. Inside it the colour " +
             "clears smoothly back to itself at the boat.")]
    [Min(0f)] public float radius = 105.15f;

    [Tooltip("How far the outer ring pulls towards white. 0 = no fade, 1 = white.")]
    [Range(0f, 1f)] public float outerOpacity = 0.6f;

    [Tooltip("The inner ring's radius is Radius divided by this — 2 puts it halfway out.")]
    [Min(1f)] public float innerDivision = 8.4f;

    [Tooltip("How far the inner ring pulls towards white, on top of the outer one. 0 = no fade, " +
             "1 = white.")]
    [Range(0f, 1f)] public float innerOpacity = 0.34f;

    [Header("Rising From The Bottom")]
    [Tooltip("World Y the white gradient is full at. It eases to nothing by world Y = 1, so " +
             "the further below 1 this sits, the taller and softer the gradient.")]
    public float gradientBottom = -43.13f;

    // ─────────────────────────────────────────────────────────────
    // PUSHING
    // ─────────────────────────────────────────────────────────────

    /// <summary>What a world with no white fade preset gets: no fade at all.</summary>
    private static readonly WhiteFadeSettings None =
        new WhiteFadeSettings { outerOpacity = 0f, innerOpacity = 0f };

    private static readonly int RadiusId        = Shader.PropertyToID("_WhiteFadeRadius");
    private static readonly int OuterOpacityId  = Shader.PropertyToID("_WhiteFadeOuterOpacity");
    private static readonly int InnerDivisionId = Shader.PropertyToID("_WhiteFadeInnerDivision");
    private static readonly int InnerOpacityId  = Shader.PropertyToID("_WhiteFadeInnerOpacity");
    private static readonly int GradientBottomId = Shader.PropertyToID("_WhiteFadeGradientBottom");

    /// <summary>This world's numbers, or no fade without a preset.</summary>
    public static void Push(WhiteFadeSettings settings)
    {
        (settings ?? None).Apply();
    }

    private void Apply()
    {
        Shader.SetGlobalFloat(RadiusId,        radius);
        Shader.SetGlobalFloat(OuterOpacityId,  outerOpacity);
        Shader.SetGlobalFloat(InnerDivisionId, innerDivision);
        Shader.SetGlobalFloat(InnerOpacityId,  innerOpacity);
        Shader.SetGlobalFloat(GradientBottomId, gradientBottom);
    }
}
