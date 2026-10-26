using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// The level select's white fade: everything washed towards the Screen Picture with distance from
/// the boat, true at the boat and full by Radius, at Opacity — Fade Sharpness sets how soft its
/// edge is — and a gradient rising from the bottom of the world, full below Gradient Bottom and
/// gone by world Y = 1.
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
    [Tooltip("Metres from the boat to where the fade is full.")]
    [Min(0f)] public float radius = 105.15f;

    [Tooltip("How far the fade pulls towards the Screen Picture. 0 = no fade, 1 = all the way.")]
    [FormerlySerializedAs("outerOpacity")]
    [Range(0f, 1f)] public float opacity = 0.6f;

    [Tooltip("How sharp the fade's edge is. 0 = it eases in all the way from the boat out to " +
             "Radius; 1 = a hard edge at Radius, with the colour true inside it.")]
    [Range(0f, 1f)] public float fadeSharpness = 0f;

    [Header("Rising From The Bottom")]
    [Tooltip("World Y the white gradient is full at. It eases to nothing by world Y = 1, so " +
             "the further below 1 this sits, the taller and softer the gradient.")]
    public float gradientBottom = -43.13f;

    // ─────────────────────────────────────────────────────────────
    // PUSHING
    // ─────────────────────────────────────────────────────────────

    /// <summary>What a world with no white fade preset gets: no fade at all.</summary>
    private static readonly WhiteFadeSettings None = new WhiteFadeSettings { opacity = 0f };

    private static readonly int RadiusId         = Shader.PropertyToID("_WhiteFadeRadius");
    private static readonly int OpacityId        = Shader.PropertyToID("_WhiteFadeOpacity");
    private static readonly int SharpnessId      = Shader.PropertyToID("_WhiteFadeSharpness");
    private static readonly int GradientBottomId = Shader.PropertyToID("_WhiteFadeGradientBottom");

    /// <summary>
    /// Numbers that win over the world's while set — a Camera Sequence's fog, for as long as it
    /// plays. ApplyAesthetics pushes every frame, so the override goes in here rather than being
    /// written once and stomped on the next frame.
    /// </summary>
    public static WhiteFadeSettings Override;

    /// <summary>
    /// A Radius that wins over the pushed numbers' while set — a Camera Sequence shot's Fog Distance
    /// Override, for as long as the shot plays. Setting it (or clearing it) takes effect at once.
    /// </summary>
    public static float? RadiusOverride
    {
        get => _radiusOverride;
        set
        {
            _radiusOverride = value;
            Shader.SetGlobalFloat(RadiusId, value ?? _pushedRadius);
        }
    }
    private static float? _radiusOverride;

    // The Radius last pushed, for clearing RadiusOverride without waiting for the next push.
    private static float _pushedRadius = None.radius;

    /// <summary>The world's Radius as last pushed, whatever RadiusOverride says.</summary>
    public static float PushedRadius => _pushedRadius;

    /// <summary>This world's numbers, or no fade without a preset — unless an override is set.</summary>
    public static void Push(WhiteFadeSettings settings)
    {
        (Override ?? settings ?? None).Apply();
    }

    /// <summary>Push these numbers now, without waiting for the next ApplyAesthetics.</summary>
    public void PushNow() => Apply();

    private void Apply()
    {
        _pushedRadius = radius;
        Shader.SetGlobalFloat(RadiusId,         _radiusOverride ?? radius);
        Shader.SetGlobalFloat(OpacityId,        opacity);
        Shader.SetGlobalFloat(SharpnessId,      fadeSharpness);
        Shader.SetGlobalFloat(GradientBottomId, gradientBottom);
    }
}
