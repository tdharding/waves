using System;
using UnityEngine;

/// <summary>
/// How the soul zone drawn along a soul route looks. How fast it draws out once its souls are
/// released is Zone Draw Speed on <see cref="TravellingSoulFishController"/>. Carried on the river water preset beside the water lines,
/// authored in the Level Select River Tuner, and pushed by
/// <see cref="LevelSelectDesignerData.ApplyAesthetics"/>.
///
/// The band's WIDTH is not here: it is each route's own Band Width in the Level Select Designer,
/// the width the designer already draws it at. Where the band goes is pushed separately, every
/// frame, by <see cref="TravellingSoulFishController"/>.
/// </summary>
[Serializable]
public class RiverSoulZoneSettings
{
    [Tooltip("Master multiplier on the zone. 0 hides it without stopping it drawing out.")]
    [Range(0f, 1f)] public float strength = 1f;

    [Tooltip("The colour under the grain — the darkest the band gets; the grain only lightens it.")]
    public Color baseColour = Color.white;

    [Tooltip("Brightness of the band's colour. Above 1 pushes it brighter than the image. Only the " +
             "colour — how much of the water it covers stays the same.")]
    [Min(0f)] public float brightness = 1f;

    [Tooltip("Width of the band, as a multiplier on each route's own Band Width in the Level " +
             "Select Designer. 1 = exactly as authored.")]
    [Min(0f)] public float width = 1f;

    [Tooltip("Where the soft edge begins, as a fraction of the band's half-width. 0 fades all the " +
             "way from the centre line; near 1 is a hard edge.")]
    [Range(0f, 1f)] public float fadeStart = 0.6f;

    [Tooltip("Width of one grain cell in world units — the same gradient-noise grain the river " +
             "stone uses. 0 = no grain, a plain band of the Base Colour.")]
    [Min(0f)] public float grainSize = 0.1f;

    [Tooltip("How far the grain lifts the Base Colour towards white. 0 = no grain, 1 = its " +
             "lightest flecks are white.")]
    [Range(0f, 1f)] public float grainStrength = 0.5f;

    [Tooltip("World units per second the grain flows down the route.")]
    public float scrollSpeed = 0.3f;

    [Header("Souls Pool Tower Release")]
    [Tooltip("World units per second the water in a souls pool tower's bowl drains down once E is " +
             "pressed. When it is gone the pool round the tower fills.")]
    [Min(0.001f)] public float drainSpeed = 0.05f;

    [Tooltip("How far across the pool's water round the tower the zone fills: 0 = the island's " +
             "edge, 1 = the pool's outer edge. Worked out from each pool's own size. Not affected " +
             "by Width.")]
    [Range(0f, 1f)] public float poolFillRadius = 1f;

    [Tooltip("World units per second the zone grows across the pool round the tower, out to its " +
             "Pool Fill Radius. Then the souls set off along their routes.")]
    [Min(0.01f)] public float fillSpeed = 1f;

    // ─────────────────────────────────────────────────────────────
    // PUSHING
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The tuner, while it is driving — wins over the world's preset for the same reason
    /// <see cref="RiverEdgeRippleSettings.Live"/> does: the aesthetics pump re-pushes the preset
    /// every tick and would drag a slider straight back.
    /// </summary>
    public static RiverSoulZoneSettings Live;

    /// <summary>No zone drawn — what a world with no preset gets. It still draws out, unseen.</summary>
    private static readonly RiverSoulZoneSettings None = new RiverSoulZoneSettings { strength = 0f };

    private static readonly int StrengthId    = Shader.PropertyToID("_TravelSoulZoneStrength");
    private static readonly int FadeStartId   = Shader.PropertyToID("_TravelSoulZoneFadeStart");
    private static readonly int WidthId       = Shader.PropertyToID("_TravelSoulZoneWidth");
    private static readonly int BrightnessId  = Shader.PropertyToID("_TravelSoulZoneBrightness");
    private static readonly int BaseColourId  = Shader.PropertyToID("_TravelSoulZoneBaseColour");
    private static readonly int GrainSizeId     = Shader.PropertyToID("_TravelSoulZoneGrainSize");
    private static readonly int GrainStrengthId = Shader.PropertyToID("_TravelSoulZoneGrainStrength");
    private static readonly int ScrollSpeedId = Shader.PropertyToID("_TravelSoulZoneScrollSpeed");

    /// <summary>The numbers in force: the tuner's while it drives, this world's otherwise.</summary>
    public static RiverSoulZoneSettings Resolve(RiverSoulZoneSettings settings) => Live ?? settings ?? None;

    /// <summary>
    /// Publishes the look to the shaders. Every frame, not once — bare globals are emptied by the
    /// next shader reimport and nothing brings them back but the next push.
    /// </summary>
    public static void Push(RiverSoulZoneSettings settings) => Resolve(settings).Apply();

    private void Apply()
    {
        Shader.SetGlobalFloat(StrengthId,    strength);
        Shader.SetGlobalFloat(FadeStartId,   fadeStart);
        Shader.SetGlobalFloat(WidthId,       width);
        Shader.SetGlobalFloat(BrightnessId,  brightness);
        Shader.SetGlobalColor(BaseColourId,  baseColour);
        Shader.SetGlobalFloat(GrainSizeId,     grainSize);
        Shader.SetGlobalFloat(GrainStrengthId, grainStrength);
        Shader.SetGlobalFloat(ScrollSpeedId, scrollSpeed);
    }

    /// <summary>Field-by-field copy, so the tuner can load a preset without holding on to it.</summary>
    public void CopyFrom(RiverSoulZoneSettings other)
    {
        if (other == null) return;

        strength    = other.strength;
        width       = other.width;
        brightness  = other.brightness;
        baseColour  = other.baseColour;
        fadeStart   = other.fadeStart;
        grainSize     = other.grainSize;
        grainStrength = other.grainStrength;
        scrollSpeed = other.scrollSpeed;
        drainSpeed  = other.drainSpeed;
        fillSpeed   = other.fillSpeed;
        poolFillRadius = other.poolFillRadius;
    }
}
