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
/// own grain, and three parts of the landscape — Holes, NoiseUp and NoiseDown — each wearing one.
///
/// Nothing is judged by steepness or height above the base any more: away from Holes, the hills
/// are coloured purely by which way their rocky noise leans.
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
    [Tooltip("Everything deeper than Hole Depth below the tile base.")]
    public LandscapeVariant holes  = LandscapeVariant.C;
    [Tooltip("The ups of the rocky noise — everything the noise pushes up.")]
    public LandscapeVariant noiseUp   = LandscapeVariant.A;
    [Tooltip("The downs of the rocky noise, and ground flat enough to lean neither way.")]
    public LandscapeVariant noiseDown = LandscapeVariant.B;

    [Header("Where The Parts Split")]
    [Tooltip("Metres below the tile base past which everything counts as Holes.")]
    [Min(0f)] public float holeDepth = 0.5f;
    [Tooltip("How wide the blend into Holes is, as a fraction of Hole Depth. 0 is a hard line.")]
    [Range(0f, 1f)] public float softness = 0.2f;
    [Tooltip("How wide the blend between NoiseUp and NoiseDown is, as a fraction of the noise's " +
             "own lean. 0 is a hard line down the middle of the noise, 1 blends across the whole " +
             "of it.")]
    [Range(0f, 1f)] public float noiseSoftness = 0.2f;

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
            holes = LandscapeVariant.A, noiseDown = LandscapeVariant.A, lightStrength = 0f,
        };

    private static readonly int ColourAId        = Shader.PropertyToID("_LandscapeColourA");
    private static readonly int ColourBId        = Shader.PropertyToID("_LandscapeColourB");
    private static readonly int ColourCId        = Shader.PropertyToID("_LandscapeColourC");
    private static readonly int GrainSizeId      = Shader.PropertyToID("_LandscapeGrainSizes");
    private static readonly int GrainStrengthId  = Shader.PropertyToID("_LandscapeGrainStrengths");
    private static readonly int PartVariantsId   = Shader.PropertyToID("_LandscapePartVariants");
    private static readonly int HoleDepthId      = Shader.PropertyToID("_LandscapeHoleDepth");
    private static readonly int SoftnessId       = Shader.PropertyToID("_LandscapeSoftness");
    private static readonly int NoiseSoftnessId  = Shader.PropertyToID("_LandscapeNoiseSoftness");
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

        // x Holes, y NoiseUp, z NoiseDown.
        Shader.SetGlobalVector(PartVariantsId,
            new Vector4((float)holes, (float)noiseUp, (float)noiseDown, 0f));

        Shader.SetGlobalFloat(HoleDepthId,     holeDepth);
        Shader.SetGlobalFloat(SoftnessId,      softness);
        Shader.SetGlobalFloat(NoiseSoftnessId, noiseSoftness);
        Shader.SetGlobalFloat(LightStrengthId, lightStrength);
    }

    /// <summary>Field-by-field copy, so the tuner can load a preset without holding on to it.</summary>
    public void CopyFrom(LandscapeShadingSettings other)
    {
        if (other == null) return;

        colourA = other.colourA; grainSizeA = other.grainSizeA; grainStrengthA = other.grainStrengthA;
        colourB = other.colourB; grainSizeB = other.grainSizeB; grainStrengthB = other.grainStrengthB;
        colourC = other.colourC; grainSizeC = other.grainSizeC; grainStrengthC = other.grainStrengthC;

        holes     = other.holes;
        noiseUp   = other.noiseUp;
        noiseDown = other.noiseDown;

        holeDepth     = other.holeDepth;
        softness      = other.softness;
        noiseSoftness = other.noiseSoftness;
        lightStrength = other.lightStrength;
    }
}
