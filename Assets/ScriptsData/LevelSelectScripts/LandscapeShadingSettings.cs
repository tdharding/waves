using UnityEngine;

/// <summary>Which of the landscape's three stone variants a part wears.</summary>
public enum LandscapeVariant
{
    A = 0,
    B = 1,
    C = 2,
}

/// <summary>
/// How the level select's landscape hills are shaded: three stone variants, each a colour with its
/// own grain, and four parts of the landscape — Ground, Tops, Cliffs and Holes — each wearing one.
///
/// The same arrangement as <see cref="RiverRunShadingSettings"/> and entirely separate from it.
/// Bare $Globals read by LandscapeShading.hlsl, so nothing on disk remembers them: they are pushed
/// every frame by <see cref="LevelSelectDesignerData.ApplyAesthetics"/>, held on a
/// <see cref="LevelSelectLandscapePreset"/>, and tuned in Tools > Waves > Level Select Landscape
/// Tuner. Unlike the runs' part colours nothing here is baked — every change is live.
///
/// The light's POSITION is not here: it is the world's, authored in the designer's Aesthetics and
/// shared with the river runs. Only its strength on the hills is.
/// </summary>
[System.Serializable]
public class LandscapeShadingSettings
{
    [Header("Variant A")]
    public Color colourA = new Color(0.786f, 0.786f, 0.786f, 1f);
    [Tooltip("How wide one cell of the grain is, in metres. 0 turns it off.")]
    [Min(0f)] public float grainSizeA = 0.2f;
    [Tooltip("How far the grain pulls either side of the colour. 0 leaves it clean.")]
    [Range(0f, 1f)] public float grainStrengthA = 0.3f;

    [Header("Variant B")]
    public Color colourB = new Color(0.6f, 0.6f, 0.6f, 1f);
    [Tooltip("How wide one cell of the grain is, in metres. 0 turns it off.")]
    [Min(0f)] public float grainSizeB = 0.2f;
    [Tooltip("How far the grain pulls either side of the colour. 0 leaves it clean.")]
    [Range(0f, 1f)] public float grainStrengthB = 0.3f;

    [Header("Variant C")]
    public Color colourC = new Color(0.3f, 0.3f, 0.3f, 1f);
    [Tooltip("How wide one cell of the grain is, in metres. 0 turns it off.")]
    [Min(0f)] public float grainSizeC = 0.2f;
    [Tooltip("How far the grain pulls either side of the colour. 0 leaves it clean.")]
    [Range(0f, 1f)] public float grainStrengthC = 0.3f;

    [Header("Parts")]
    [Tooltip("Flat ground at the tile base.")]
    public LandscapeVariant ground = LandscapeVariant.A;
    [Tooltip("Flat ground raised on hills and plateaus, above Top Height.")]
    public LandscapeVariant tops   = LandscapeVariant.A;
    [Tooltip("Faces steeper than Cliff Angle.")]
    public LandscapeVariant cliffs = LandscapeVariant.B;
    [Tooltip("Everything deeper than Hole Depth below the tile base.")]
    public LandscapeVariant holes  = LandscapeVariant.C;

    [Header("Where The Parts Split")]
    [Tooltip("Degrees off flat past which a face counts as Cliffs.")]
    [Range(0f, 90f)] public float cliffAngle = 45f;
    [Tooltip("Metres above the tile base past which flat ground counts as Tops.")]
    [Min(0f)] public float topHeight = 2f;
    [Tooltip("Metres below the tile base past which everything counts as Holes.")]
    [Min(0f)] public float holeDepth = 0.5f;
    [Tooltip("How wide each blend between parts is, as a fraction of its own threshold. 0 is a " +
             "hard line.")]
    [Range(0f, 1f)] public float softness = 0.2f;

    [Header("Light")]
    [Tooltip("How hard the hills are turned against the world's light (its position is in the " +
             "designer's Aesthetics). 0 leaves them flat and unlit; past 1 the lit result is " +
             "multiplied, up to 10x brighter.")]
    [Range(0f, 10f)] public float lightStrength = 0.6f;

    // ─────────────────────────────────────────────────────────────
    // PUSHING
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The tuner, while it is driving — set by the Level Select Landscape Tuner with Apply Live on.
    /// Wins over the world's preset, which the aesthetics pump re-pushes every editor tick.
    /// </summary>
    public static LandscapeShadingSettings Live;

    /// <summary>What a world with no landscape preset gets: plain ungrained, unlit stone.</summary>
    private static readonly LandscapeShadingSettings None =
        new LandscapeShadingSettings
        {
            grainStrengthA = 0f, grainStrengthB = 0f, grainStrengthC = 0f,
            cliffs = LandscapeVariant.A, holes = LandscapeVariant.A, lightStrength = 0f,
        };

    private static readonly int ColourAId        = Shader.PropertyToID("_LandscapeColourA");
    private static readonly int ColourBId        = Shader.PropertyToID("_LandscapeColourB");
    private static readonly int ColourCId        = Shader.PropertyToID("_LandscapeColourC");
    private static readonly int GrainSizeId      = Shader.PropertyToID("_LandscapeGrainSizes");
    private static readonly int GrainStrengthId  = Shader.PropertyToID("_LandscapeGrainStrengths");
    private static readonly int PartVariantsId   = Shader.PropertyToID("_LandscapePartVariants");
    private static readonly int CliffAngleId     = Shader.PropertyToID("_LandscapeCliffAngle");
    private static readonly int TopHeightId      = Shader.PropertyToID("_LandscapeTopHeight");
    private static readonly int HoleDepthId      = Shader.PropertyToID("_LandscapeHoleDepth");
    private static readonly int SoftnessId       = Shader.PropertyToID("_LandscapeSoftness");
    private static readonly int LightStrengthId  = Shader.PropertyToID("_LandscapeLightStrength");
    private static readonly int BaseYId          = Shader.PropertyToID("_LandscapeBaseY");

    /// <summary>The tuner's numbers if it is driving, this world's otherwise, none without a preset.</summary>
    public static void Push(LandscapeShadingSettings settings)
    {
        (Live ?? settings ?? None).Apply();
    }

    /// <summary>
    /// The tile surface's world height, which Tops and Holes are measured from. A fact about the
    /// world — the designer's landscape World Y + Height Offset — so pushed from there, not tuned.
    /// </summary>
    public static void PushBaseY(float worldY)
    {
        Shader.SetGlobalFloat(BaseYId, worldY);
    }

    private void Apply()
    {
        Shader.SetGlobalColor(ColourAId, colourA);
        Shader.SetGlobalColor(ColourBId, colourB);
        Shader.SetGlobalColor(ColourCId, colourC);

        Shader.SetGlobalVector(GrainSizeId,     new Vector4(grainSizeA,     grainSizeB,     grainSizeC,     0f));
        Shader.SetGlobalVector(GrainStrengthId, new Vector4(grainStrengthA, grainStrengthB, grainStrengthC, 0f));

        // x Ground, y Tops, z Cliffs, w Holes.
        Shader.SetGlobalVector(PartVariantsId,
            new Vector4((float)ground, (float)tops, (float)cliffs, (float)holes));

        Shader.SetGlobalFloat(CliffAngleId,    cliffAngle);
        Shader.SetGlobalFloat(TopHeightId,     topHeight);
        Shader.SetGlobalFloat(HoleDepthId,     holeDepth);
        Shader.SetGlobalFloat(SoftnessId,      softness);
        Shader.SetGlobalFloat(LightStrengthId, lightStrength);
    }

    /// <summary>Field-by-field copy, so the tuner can load a preset without holding on to it.</summary>
    public void CopyFrom(LandscapeShadingSettings other)
    {
        if (other == null) return;

        colourA = other.colourA; grainSizeA = other.grainSizeA; grainStrengthA = other.grainStrengthA;
        colourB = other.colourB; grainSizeB = other.grainSizeB; grainStrengthB = other.grainStrengthB;
        colourC = other.colourC; grainSizeC = other.grainSizeC; grainStrengthC = other.grainStrengthC;

        ground = other.ground;
        tops   = other.tops;
        cliffs = other.cliffs;
        holes  = other.holes;

        cliffAngle    = other.cliffAngle;
        topHeight     = other.topHeight;
        holeDepth     = other.holeDepth;
        softness      = other.softness;
        lightStrength = other.lightStrength;
    }
}
