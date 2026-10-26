using System;
using UnityEngine;

/// <summary>
/// The level select river water's colour, and how it darkens with depth — deep water takes on the
/// Depth Colour, water over the banks and the shallows keeps the Water Colour. Carried on the river
/// water preset beside the water lines, authored in the Level Select River Tuner, and pushed by
/// <see cref="LevelSelectDesignerData.ApplyAesthetics"/>.
///
/// The defaults are what BranchWaterMaterial held when these were material properties, so a
/// preset saved before they moved here looks the same.
/// </summary>
[Serializable]
public class RiverDepthSettings
{
    // Drawn at the top of the Level Select River Tuner rather than in its Depth section.
    [Tooltip("The water's own colour, on every river and pool. The lines, grain and depth all " +
             "go on over it.")]
    public Color waterColour = new Color(0.836478f, 0.836478f, 0.836478f, 1f);

    [Tooltip("How far below the surface the river bed has to be before the water takes on the " +
             "Depth Colour fully, in metres. Smaller reaches it closer to the banks.")]
    [Min(0f)] public float depth = 0.09f;

    [Tooltip("The colour the deep water is blended towards — overlaid on the water, so mid grey " +
             "leaves it as it is, darker darkens it and lighter lightens it.")]
    public Color depthColour = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Tooltip("How much of the depth blend shows. 0 leaves the water as it is.")]
    [Range(0f, 1f)] public float depthOpacity = 0.7f;

    // ─────────────────────────────────────────────────────────────
    // PUSHING
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The tuner, while it is driving — wins over the world's preset for the same reason
    /// <see cref="RiverEdgeRippleSettings.Live"/> does: the aesthetics pump re-pushes the preset
    /// every tick and would drag a slider straight back.
    /// </summary>
    public static RiverDepthSettings Live;

    /// <summary>What a world with no preset gets — the defaults.</summary>
    private static readonly RiverDepthSettings Default = new RiverDepthSettings();

    private static readonly int WaterColourId  = Shader.PropertyToID("_RiverWaterColour");
    private static readonly int DepthId        = Shader.PropertyToID("_RiverDepth");
    private static readonly int DepthColourId  = Shader.PropertyToID("_RiverDepthColour");
    private static readonly int DepthOpacityId = Shader.PropertyToID("_RiverDepthOpacity");

    /// <summary>The numbers in force: the tuner's while it drives, this world's otherwise.</summary>
    public static RiverDepthSettings Resolve(RiverDepthSettings settings) => Live ?? settings ?? Default;

    /// <summary>
    /// Publishes the depth to the shaders. Every frame, not once — bare globals are emptied by the
    /// next shader reimport and nothing brings them back but the next push.
    /// </summary>
    public static void Push(RiverDepthSettings settings) => Resolve(settings).Apply();

    private void Apply()
    {
        Shader.SetGlobalColor(WaterColourId,  waterColour);
        Shader.SetGlobalFloat(DepthId,        depth);
        Shader.SetGlobalColor(DepthColourId,  depthColour);
        Shader.SetGlobalFloat(DepthOpacityId, depthOpacity);
    }

    /// <summary>Field-by-field copy, so the tuner can load a preset without holding on to it.</summary>
    public void CopyFrom(RiverDepthSettings other)
    {
        if (other == null) return;

        waterColour  = other.waterColour;
        depth        = other.depth;
        depthColour  = other.depthColour;
        depthOpacity = other.depthOpacity;
    }
}
