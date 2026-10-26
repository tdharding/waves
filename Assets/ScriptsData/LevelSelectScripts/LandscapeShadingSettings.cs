using System.Collections.Generic;
using UnityEngine;

/// <summary>Which of the landscape's four stone variants a part wears.</summary>
public enum LandscapeVariant
{
    A = 0,
    B = 1,
    C = 2,
    D = 3,
}

/// <summary>
/// How the level select's landscape hills are shaded: four stone variants, each a colour with its
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
/// The light is not here at all: it is the scene's <see cref="LevelSelectLight"/>, position and
/// strength both, shared with the river runs.
/// </summary>
[System.Serializable]
public class LandscapeShadingSettings
{
    [Header("Base")]
    [Tooltip("What the landscape starts as, under every variant. A variant's colour alpha is how " +
             "much of it covers this: 1 hides it completely, 0 lets it show through untouched. " +
             "The base takes no grain of its own.")]
    public Color baseColour = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("Variant A")]
    [Tooltip("Variant A's colour. Its alpha is how much it covers the Base Colour.")]
    public Color colourA = new Color(0.786f, 0.786f, 0.786f, 1f);
    [Tooltip("How wide one cell of the grain is, in metres. 0 turns it off.")]
    [Min(0f)] public float grainSizeA = 0.2f;
    [Tooltip("How far the grain pulls either side of the colour. 0 leaves it clean.")]
    [Range(0f, 1f)] public float grainStrengthA = 0.3f;

    [Header("Variant B")]
    [Tooltip("Variant B's colour. Its alpha is how much it covers the Base Colour.")]
    public Color colourB = new Color(0.6f, 0.6f, 0.6f, 1f);
    [Tooltip("How wide one cell of the grain is, in metres. 0 turns it off.")]
    [Min(0f)] public float grainSizeB = 0.2f;
    [Tooltip("How far the grain pulls either side of the colour. 0 leaves it clean.")]
    [Range(0f, 1f)] public float grainStrengthB = 0.3f;

    [Header("Variant C")]
    [Tooltip("Variant C's colour. Its alpha is how much it covers the Base Colour.")]
    public Color colourC = new Color(0.3f, 0.3f, 0.3f, 1f);
    [Tooltip("How wide one cell of the grain is, in metres. 0 turns it off.")]
    [Min(0f)] public float grainSizeC = 0.2f;
    [Tooltip("How far the grain pulls either side of the colour. 0 leaves it clean.")]
    [Range(0f, 1f)] public float grainStrengthC = 0.3f;

    [Header("Variant D")]
    [Tooltip("Variant D's colour. Its alpha is how much it covers the Base Colour.")]
    public Color colourD = new Color(0.45f, 0.45f, 0.45f, 1f);
    [Tooltip("How wide one cell of the grain is, in metres. 0 turns it off.")]
    [Min(0f)] public float grainSizeD = 0.2f;
    [Tooltip("How far the grain pulls either side of the colour. 0 leaves it clean.")]
    [Range(0f, 1f)] public float grainStrengthD = 0.3f;

    [Header("Parts")]
    [Tooltip("Everything deeper than Hole Depth below the tile base.")]
    public LandscapeVariant holes  = LandscapeVariant.C;
    [Tooltip("The ups of the rocky noise — everything the noise pushes up.")]
    public LandscapeVariant noiseUp   = LandscapeVariant.A;
    [Tooltip("The downs of the rocky noise, and ground flat enough to lean neither way.")]
    public LandscapeVariant noiseDown = LandscapeVariant.B;

    // The three painted designations — ground painted Red, Blue or Yellow on the Texture tab of the
    // designer's Landscape Maps. Each has the same three parts as the unpainted Landscape above,
    // and each starts as a copy of them (paintDesignationsSeeded). The tuner shows one designation's
    // parts at a time, picked in its Designation dropdown.

    [Header("Parts")]
    [Tooltip("Everything deeper than Hole Depth below the tile base, where the ground is painted Red.")]
    public LandscapeVariant redHoles     = LandscapeVariant.C;
    [Tooltip("The ups of the rocky noise, where the ground is painted Red.")]
    public LandscapeVariant redNoiseUp   = LandscapeVariant.A;
    [Tooltip("The downs of the rocky noise, where the ground is painted Red.")]
    public LandscapeVariant redNoiseDown = LandscapeVariant.B;

    [Header("Parts")]
    [Tooltip("Everything deeper than Hole Depth below the tile base, where the ground is painted Blue.")]
    public LandscapeVariant blueHoles     = LandscapeVariant.C;
    [Tooltip("The ups of the rocky noise, where the ground is painted Blue.")]
    public LandscapeVariant blueNoiseUp   = LandscapeVariant.A;
    [Tooltip("The downs of the rocky noise, where the ground is painted Blue.")]
    public LandscapeVariant blueNoiseDown = LandscapeVariant.B;

    [Header("Parts")]
    [Tooltip("Everything deeper than Hole Depth below the tile base, where the ground is painted Yellow.")]
    public LandscapeVariant yellowHoles     = LandscapeVariant.C;
    [Tooltip("The ups of the rocky noise, where the ground is painted Yellow.")]
    public LandscapeVariant yellowNoiseUp   = LandscapeVariant.A;
    [Tooltip("The downs of the rocky noise, where the ground is painted Yellow.")]
    public LandscapeVariant yellowNoiseDown = LandscapeVariant.B;

    // Set once Red, Blue and Yellow have been given the Landscape parts and every decal group's
    // Landscape tick, so a preset made before they existed looks the same until they are changed.
    [HideInInspector] public bool paintDesignationsSeeded;

    [Header("Where The Parts Split")]
    [Tooltip("Metres below the tile base past which everything counts as Holes. Negative " +
             "puts the line above the base instead.")]
    public float holeDepth = 0.5f;
    [Tooltip("How wide the blend into Holes is, as a fraction of Hole Depth. 0 is a hard line.")]
    [Range(0f, 1f)] public float softness = 0.2f;
    [Tooltip("How wide the blend between NoiseUp and NoiseDown is, as a fraction of the noise's " +
             "own lean. 0 is a hard line down the middle of the noise, 1 blends across the whole " +
             "of it.")]
    [Range(0f, 1f)] public float noiseSoftness = 0.2f;
    [Tooltip("How wide the blend between painted colours is — Red, Blue, Yellow and the unpainted " +
             "Landscape — on the designer's Texture map. 0 is a crisp line that still runs in " +
             "smooth curves rather than round the cells; 1 fades each colour into the next across " +
             "the cells.")]
    [Range(0f, 1f)] public float paintSoftness = 0.3f;

    [Header("Spikes")]
    [Tooltip("Which variant the level select's procedural spikes wear — its colour and its grain.")]
    public LandscapeVariant spikes = LandscapeVariant.A;
    [Tooltip("How wide the dark band along each spiral groove is. 0 hugs the groove itself; 1 " +
             "spreads most of the way to the next one.")]
    [Range(0f, 1f)] public float spikeGrooveSoftness = 0.5f;
    [Tooltip("How dark the spiral grooves go. 1 = no change, 0 = black.")]
    [Range(0f, 1f)] public float spikeGrooveDarkness = 0.35f;
    [Tooltip("Texels across, as if the groove shading were painted into a sheet this size and " +
             "wrapped on. 0 = smooth.")]
    [Min(0f)] public float spikeGrooveResolution = 256f;

    // The light — where it stands and how strong it is — is not here: it is the scene's
    // LevelSelectLight, shared with the river runs, so the two can never be lit differently.

    // ─────────────────────────────────────────────────────────────
    // DECALS
    //
    // Hand-drawn marks stamped over the landscape, the spikes and the trees: a simpler copy of the
    // river runs' decal groups, entirely separate from them. The drawings live in
    // LandscapeDecalLibrary's folder and are gathered onto one sheet; each group picks some of
    // them, scatters or tiles them, and ticks which of the three designations it draws on.
    //
    // Hidden from the tuner's field-by-field list — it draws them in a section of their own.
    // ─────────────────────────────────────────────────────────────

    // The sheet every drawing in the folder is gathered onto, and how it is laid out — written by
    // the tuner's Rebuild Sheet, read by the shader. Columns and rows of equal cells, how many
    // hold a drawing, and how many pixels one cell is.
    [HideInInspector] public Texture2D decalSheet;
    [HideInInspector] public int       decalSheetColumns;
    [HideInInspector] public int       decalSheetRows;
    [HideInInspector] public int       decalSheetHeld;
    [HideInInspector] public int       decalSheetCellPixels;

    // The asset GUID of the drawing in each cell of the sheet, in cell order — how a group's
    // ticked drawings are found on it. Written with the sheet.
    [HideInInspector] public List<string> decalSheetDrawings = new List<string>();

    /// <summary>The most decal groups the landscape can hold — the length of the shader's arrays.</summary>
    public const int MaxDecalGroups = 8;

    /// <summary>
    /// The most cells all the groups together can list — each group's ticked drawings, one after
    /// another. Must match LANDSCAPE_DECAL_PICKS in LandscapeShading.hlsl.
    /// </summary>
    public const int MaxDecalPicks = 64;

    // The decal groups, the later in the list on top. Drawn by the tuner's Decals section.
    [HideInInspector] public List<LandscapeDecalGroup> decalGroups = new List<LandscapeDecalGroup>();

    /// <summary>
    /// A new group, with a seed no other group has, so it does not stand its decals exactly on
    /// top of another group's with the same spacing.
    /// </summary>
    public LandscapeDecalGroup NewDecalGroup(string name)
    {
        int seed = 1;
        while (decalGroups.Exists(g => g != null && g.seed == seed)) seed++;
        return new LandscapeDecalGroup { name = name, seed = seed };
    }

    // ─────────────────────────────────────────────────────────────
    // THE NORMAL MAP DECALS
    //
    // Real normal maps, kept in LandscapeDecalLibrary's NormalMaps folder and gathered onto a sheet
    // of their own — a LINEAR one, because a normal map is directions rather than colour. The
    // landscape's copy of the river runs' normal map decal groups, kept apart from them. Always
    // tiled, laid on the world's three planes like the decals, and painting no colour: they bend
    // the normal the light, the boat's light and the River Glow are worked out from. Each group
    // ticks which of the three designations it bends.
    // ─────────────────────────────────────────────────────────────

    [HideInInspector] public Texture2D normalDecalSheet;
    [HideInInspector] public int       normalDecalSheetColumns;
    [HideInInspector] public int       normalDecalSheetRows;
    [HideInInspector] public int       normalDecalSheetHeld;
    [HideInInspector] public int       normalDecalSheetCellPixels;

    // The asset GUID of the normal map in each cell of that sheet, in cell order.
    [HideInInspector] public List<string> normalDecalSheetDrawings = new List<string>();

    /// <summary>The most normal map groups the landscape can hold — the length of the shader's arrays.</summary>
    public const int MaxNormalDecalGroups = 8;

    /// <summary>
    /// The most cells all the normal map groups together can list. Must match
    /// LANDSCAPE_NORMAL_DECAL_PICKS in LandscapeShading.hlsl.
    /// </summary>
    public const int MaxNormalDecalPicks = 64;

    // The normal map groups. Drawn by the tuner under the decal groups.
    [HideInInspector] public List<LandscapeNormalDecalGroup> normalDecalGroups = new List<LandscapeNormalDecalGroup>();

    /// <summary>A new normal map group, with a seed no other normal map group has.</summary>
    public LandscapeNormalDecalGroup NewNormalDecalGroup(string name)
    {
        if (normalDecalGroups == null) normalDecalGroups = new List<LandscapeNormalDecalGroup>();

        int seed = 1;
        while (normalDecalGroups.Exists(g => g != null && g.seed == seed)) seed++;
        return new LandscapeNormalDecalGroup { name = name, seed = seed };
    }

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
            grainStrengthA = 0f, grainStrengthB = 0f, grainStrengthC = 0f, grainStrengthD = 0f,
            holes = LandscapeVariant.A, noiseDown = LandscapeVariant.A,
        };

    private static readonly int BaseColourId     = Shader.PropertyToID("_LandscapeBaseColour");
    private static readonly int ColourAId        = Shader.PropertyToID("_LandscapeColourA");
    private static readonly int ColourBId        = Shader.PropertyToID("_LandscapeColourB");
    private static readonly int ColourCId        = Shader.PropertyToID("_LandscapeColourC");
    private static readonly int ColourDId        = Shader.PropertyToID("_LandscapeColourD");
    private static readonly int GrainSizeId      = Shader.PropertyToID("_LandscapeGrainSizes");
    private static readonly int GrainStrengthId  = Shader.PropertyToID("_LandscapeGrainStrengths");
    private static readonly int PartVariantsId   = Shader.PropertyToID("_LandscapePartVariants");
    private static readonly int HoleDepthId      = Shader.PropertyToID("_LandscapeHoleDepth");
    private static readonly int SoftnessId       = Shader.PropertyToID("_LandscapeSoftness");
    private static readonly int NoiseSoftnessId  = Shader.PropertyToID("_LandscapeNoiseSoftness");
    private static readonly int RedPartsId       = Shader.PropertyToID("_LandscapeRedPartVariants");
    private static readonly int BluePartsId      = Shader.PropertyToID("_LandscapeBluePartVariants");
    private static readonly int YellowPartsId    = Shader.PropertyToID("_LandscapeYellowPartVariants");
    private static readonly int PaintSoftnessId  = Shader.PropertyToID("_LandscapePaintSoftness");
    private static readonly int SpikeVariantId   = Shader.PropertyToID("_LandscapeSpikeVariant");
    private static readonly int SpikeGrooveId    = Shader.PropertyToID("_LandscapeSpikeGroove");
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

    /// <summary>
    /// Once only: Red, Blue and Yellow take the Landscape parts, and every decal and normal map
    /// group ticks them as it ticks Landscape — so painting a colour changes nothing until its
    /// designation is tuned. Left for a Save to keep.
    /// </summary>
    public void SeedPaintDesignations()
    {
        if (paintDesignationsSeeded) return;
        paintDesignationsSeeded = true;

        redHoles    = blueHoles    = yellowHoles    = holes;
        redNoiseUp  = blueNoiseUp  = yellowNoiseUp  = noiseUp;
        redNoiseDown = blueNoiseDown = yellowNoiseDown = noiseDown;

        if (decalGroups != null)
            foreach (var g in decalGroups)
                if (g != null) g.onRed = g.onBlue = g.onYellow = g.onLandscape;
        if (normalDecalGroups != null)
            foreach (var g in normalDecalGroups)
                if (g != null) g.onRed = g.onBlue = g.onYellow = g.onLandscape;
    }

    private void Apply()
    {
        SeedPaintDesignations();

        Shader.SetGlobalColor(BaseColourId, baseColour);
        Shader.SetGlobalColor(ColourAId, colourA);
        Shader.SetGlobalColor(ColourBId, colourB);
        Shader.SetGlobalColor(ColourCId, colourC);
        Shader.SetGlobalColor(ColourDId, colourD);

        Shader.SetGlobalVector(GrainSizeId,     new Vector4(grainSizeA,     grainSizeB,     grainSizeC,     grainSizeD));
        Shader.SetGlobalVector(GrainStrengthId, new Vector4(grainStrengthA, grainStrengthB, grainStrengthC, grainStrengthD));

        // x Holes, y NoiseUp, z NoiseDown.
        Shader.SetGlobalVector(PartVariantsId,
            new Vector4((float)holes, (float)noiseUp, (float)noiseDown, 0f));

        // The painted designations' parts, the same x/y/z. The painted map itself is the scene's —
        // LandscapeTool pushes it.
        Shader.SetGlobalVector(RedPartsId,
            new Vector4((float)redHoles, (float)redNoiseUp, (float)redNoiseDown, 0f));
        Shader.SetGlobalVector(BluePartsId,
            new Vector4((float)blueHoles, (float)blueNoiseUp, (float)blueNoiseDown, 0f));
        Shader.SetGlobalVector(YellowPartsId,
            new Vector4((float)yellowHoles, (float)yellowNoiseUp, (float)yellowNoiseDown, 0f));

        Shader.SetGlobalFloat(HoleDepthId,     holeDepth);
        Shader.SetGlobalFloat(SoftnessId,      softness);
        Shader.SetGlobalFloat(NoiseSoftnessId, noiseSoftness);
        Shader.SetGlobalFloat(PaintSoftnessId, paintSoftness);

        Shader.SetGlobalFloat(SpikeVariantId, (float)spikes);
        // x Softness, y Darkness, z Resolution.
        Shader.SetGlobalVector(SpikeGrooveId,
            new Vector4(spikeGrooveSoftness, spikeGrooveDarkness, spikeGrooveResolution, 0f));

        ApplyDecals();
        ApplyNormalDecals();
    }

    private static readonly int DecalSheetId       = Shader.PropertyToID("_LandscapeDecalSheet");
    private static readonly int DecalLayoutId       = Shader.PropertyToID("_LandscapeDecalLayout");
    private static readonly int DecalGroupScatterId = Shader.PropertyToID("_LandscapeDecalGroupScatter");
    private static readonly int DecalGroupLookId    = Shader.PropertyToID("_LandscapeDecalGroupLook");
    private static readonly int DecalGroupTilingId  = Shader.PropertyToID("_LandscapeDecalGroupTiling");
    private static readonly int DecalGroupBlendId   = Shader.PropertyToID("_LandscapeDecalGroupBlend");
    private static readonly int DecalDesignationsId = Shader.PropertyToID("_LandscapeDecalDesignations");
    private static readonly int DecalPaintDesignationsId = Shader.PropertyToID("_LandscapeDecalPaintDesignations");
    private static readonly int DecalPicksId        = Shader.PropertyToID("_LandscapeDecalPicks");

    // Sent at full length every push: a global array is sized by the first push to reach it, so a
    // shorter one later would leave the tail holding whatever went before.
    private static readonly Vector4[] DecalGroupScatter = new Vector4[MaxDecalGroups];
    private static readonly Vector4[] DecalGroupLook    = new Vector4[MaxDecalGroups];
    private static readonly Vector4[] DecalGroupTiling  = new Vector4[MaxDecalGroups];
    private static readonly float[]   DecalGroupBlend   = new float[MaxDecalGroups];
    private static readonly float[]   DecalPicks       = new float[MaxDecalPicks];

    /// <summary>
    /// The sheet and every group. A sheet holding nothing pushes a count of nothing, which the
    /// shader reads before it ever samples — there is no transparent default texture for an
    /// unbound sampler to fall back on, so the count is the only guard.
    /// </summary>
    private void ApplyDecals()
    {
        bool ready = decalSheet != null && decalSheetHeld > 0
                  && decalSheetColumns > 0 && decalSheetRows > 0 && decalSheetCellPixels > 0;

        Shader.SetGlobalTexture(DecalSheetId, ready ? decalSheet : Texture2D.blackTexture);
        Shader.SetGlobalVector(DecalLayoutId, ready
            ? new Vector4(decalSheetColumns, decalSheetRows, decalSheetHeld, decalSheetCellPixels)
            : Vector4.zero);

        // Which groups each designation takes, one bit per group.
        int onLandscape = 0, onSpikes = 0, onTrees = 0, onRed = 0, onBlue = 0, onYellow = 0;

        int picks = 0;
        for (int g = 0; g < MaxDecalGroups; g++)
        {
            var group = decalGroups != null && g < decalGroups.Count ? decalGroups[g] : null;
            int first = picks;

            // The group's ticked drawings, as the sheet cells they landed in.
            if (group != null && ready)
            {
                for (int cell = 0; cell < decalSheetHeld && picks < MaxDecalPicks; cell++)
                {
                    if (cell < decalSheetDrawings.Count && group.drawings != null &&
                        group.drawings.Contains(decalSheetDrawings[cell]))
                        DecalPicks[picks++] = cell;
                }
            }

            // x spacing, y smallest scale, z largest, w amount.
            DecalGroupScatter[g] = group != null
                ? new Vector4(group.spacing, group.scaleMin, group.scaleMax, group.amount)
                : Vector4.zero;

            // x opacity, y the share given a quarter turn (0 with the rotation off), z the first
            // of its picks, w how many — none drawn at 0.
            DecalGroupLook[g] = group != null
                ? new Vector4(group.opacity, group.rotate ? group.rotateAmount : 0f,
                              first, picks - first)
                : Vector4.zero;

            // x 1 tiles the group edge to edge. y the Blend Tiles noise scale in metres, 0 off —
            // which is what a group without Tiled gets. z its softness. w the seed.
            bool blend = group != null && group.tiled && group.blendTiles && group.blendNoiseScale > 0f;
            DecalGroupTiling[g] = group != null
                ? new Vector4(group.tiled ? 1f : 0f,
                              blend ? group.blendNoiseScale    : 0f,
                              blend ? group.blendNoiseSoftness : 0f,
                              group.seed)
                : Vector4.zero;

            // The Blend Mode by its number, which LandscapeShading.hlsl's LANDSCAPE_DECAL_BLEND_
            // defines must match.
            DecalGroupBlend[g] = group != null ? (float)group.blendMode : 0f;

            if (group != null)
            {
                if (group.onLandscape) onLandscape |= 1 << g;
                if (group.onSpikes)    onSpikes    |= 1 << g;
                if (group.onTrees)     onTrees     |= 1 << g;
                if (group.onRed)       onRed       |= 1 << g;
                if (group.onBlue)      onBlue      |= 1 << g;
                if (group.onYellow)    onYellow    |= 1 << g;
            }
        }

        Shader.SetGlobalVectorArray(DecalGroupScatterId, DecalGroupScatter);
        Shader.SetGlobalVectorArray(DecalGroupLookId,    DecalGroupLook);
        Shader.SetGlobalVectorArray(DecalGroupTilingId,  DecalGroupTiling);
        Shader.SetGlobalFloatArray(DecalGroupBlendId,    DecalGroupBlend);
        Shader.SetGlobalFloatArray(DecalPicksId,         DecalPicks);

        // x Landscape, y Spikes, z Trees.
        Shader.SetGlobalVector(DecalDesignationsId, new Vector4(onLandscape, onSpikes, onTrees, 0f));
        // x Red, y Blue, z Yellow — the painted designations.
        Shader.SetGlobalVector(DecalPaintDesignationsId, new Vector4(onRed, onBlue, onYellow, 0f));
    }

    private static readonly int NormalDecalSheetId        = Shader.PropertyToID("_LandscapeNormalDecalSheet");
    private static readonly int NormalDecalLayoutId       = Shader.PropertyToID("_LandscapeNormalDecalLayout");
    private static readonly int NormalDecalGroupTilesId   = Shader.PropertyToID("_LandscapeNormalDecalGroupTiles");
    private static readonly int NormalDecalGroupBlendId   = Shader.PropertyToID("_LandscapeNormalDecalGroupBlend");
    private static readonly int NormalDecalPicksId        = Shader.PropertyToID("_LandscapeNormalDecalPicks");
    private static readonly int NormalDecalDesignationsId = Shader.PropertyToID("_LandscapeNormalDecalDesignations");
    private static readonly int NormalDecalPaintDesignationsId = Shader.PropertyToID("_LandscapeNormalDecalPaintDesignations");

    // Sent at full length every push, like the decal groups' arrays.
    private static readonly Vector4[] NormalDecalGroupTiles = new Vector4[MaxNormalDecalGroups];
    private static readonly Vector4[] NormalDecalGroupBlend = new Vector4[MaxNormalDecalGroups];
    private static readonly float[]   NormalDecalPicks      = new float[MaxNormalDecalPicks];

    /// <summary>
    /// The normal map sheet and the tiles it is laid in. The same guard as the decals: the sheet's
    /// held count is what keeps an unpushed texture global from bending the ground.
    /// </summary>
    private void ApplyNormalDecals()
    {
        bool ready = normalDecalSheet != null && normalDecalSheetHeld > 0 && normalDecalSheetColumns > 0
                  && normalDecalSheetRows > 0 && normalDecalSheetCellPixels > 0;

        Shader.SetGlobalTexture(NormalDecalSheetId, ready ? normalDecalSheet : Texture2D.blackTexture);
        Shader.SetGlobalVector(NormalDecalLayoutId, ready
            ? new Vector4(normalDecalSheetColumns, normalDecalSheetRows, normalDecalSheetHeld,
                          normalDecalSheetCellPixels)
            : Vector4.zero);

        // Which groups each designation takes, one bit per group.
        int onLandscape = 0, onSpikes = 0, onTrees = 0, onRed = 0, onBlue = 0, onYellow = 0;

        int picks = 0;
        for (int g = 0; g < MaxNormalDecalGroups; g++)
        {
            var group = normalDecalGroups != null && g < normalDecalGroups.Count ? normalDecalGroups[g] : null;
            int first = picks;

            if (group != null && ready && group.drawings != null)
            {
                for (int cell = 0; cell < normalDecalSheetHeld && picks < MaxNormalDecalPicks; cell++)
                {
                    if (cell < normalDecalSheetDrawings.Count &&
                        group.drawings.Contains(normalDecalSheetDrawings[cell]))
                        NormalDecalPicks[picks++] = cell;
                }
            }

            // x spacing, y strength, z the first of its picks, w how many — none drawn at 0.
            NormalDecalGroupTiles[g] = group != null
                ? new Vector4(group.spacing, group.strength, first, picks - first)
                : Vector4.zero;

            // x the Blend Tiles noise scale in metres, 0 off; y its softness; z the seed.
            bool blend = group != null && group.blendTiles && group.blendNoiseScale > 0f;
            NormalDecalGroupBlend[g] = group != null
                ? new Vector4(blend ? group.blendNoiseScale : 0f, group.blendNoiseSoftness, group.seed, 0f)
                : Vector4.zero;

            if (group != null)
            {
                if (group.onLandscape) onLandscape |= 1 << g;
                if (group.onSpikes)    onSpikes    |= 1 << g;
                if (group.onTrees)     onTrees     |= 1 << g;
                if (group.onRed)       onRed       |= 1 << g;
                if (group.onBlue)      onBlue      |= 1 << g;
                if (group.onYellow)    onYellow    |= 1 << g;
            }
        }

        Shader.SetGlobalVectorArray(NormalDecalGroupTilesId, NormalDecalGroupTiles);
        Shader.SetGlobalVectorArray(NormalDecalGroupBlendId, NormalDecalGroupBlend);
        Shader.SetGlobalFloatArray(NormalDecalPicksId,       NormalDecalPicks);

        // x Landscape, y Spikes, z Trees.
        Shader.SetGlobalVector(NormalDecalDesignationsId, new Vector4(onLandscape, onSpikes, onTrees, 0f));
        // x Red, y Blue, z Yellow — the painted designations.
        Shader.SetGlobalVector(NormalDecalPaintDesignationsId, new Vector4(onRed, onBlue, onYellow, 0f));
    }

    /// <summary>Field-by-field copy, so the tuner can load a preset without holding on to it.</summary>
    public void CopyFrom(LandscapeShadingSettings other)
    {
        if (other == null) return;

        baseColour = other.baseColour;
        colourA = other.colourA; grainSizeA = other.grainSizeA; grainStrengthA = other.grainStrengthA;
        colourB = other.colourB; grainSizeB = other.grainSizeB; grainStrengthB = other.grainStrengthB;
        colourC = other.colourC; grainSizeC = other.grainSizeC; grainStrengthC = other.grainStrengthC;
        colourD = other.colourD; grainSizeD = other.grainSizeD; grainStrengthD = other.grainStrengthD;

        holes     = other.holes;
        noiseUp   = other.noiseUp;
        noiseDown = other.noiseDown;

        redHoles    = other.redHoles;    redNoiseUp    = other.redNoiseUp;    redNoiseDown    = other.redNoiseDown;
        blueHoles   = other.blueHoles;   blueNoiseUp   = other.blueNoiseUp;   blueNoiseDown   = other.blueNoiseDown;
        yellowHoles = other.yellowHoles; yellowNoiseUp = other.yellowNoiseUp; yellowNoiseDown = other.yellowNoiseDown;
        paintDesignationsSeeded = other.paintDesignationsSeeded;

        holeDepth     = other.holeDepth;
        softness      = other.softness;
        noiseSoftness = other.noiseSoftness;
        paintSoftness = other.paintSoftness;

        spikes                = other.spikes;
        spikeGrooveSoftness   = other.spikeGrooveSoftness;
        spikeGrooveDarkness   = other.spikeGrooveDarkness;
        spikeGrooveResolution = other.spikeGrooveResolution;

        decalSheet           = other.decalSheet;
        decalSheetColumns    = other.decalSheetColumns;
        decalSheetRows       = other.decalSheetRows;
        decalSheetHeld       = other.decalSheetHeld;
        decalSheetCellPixels = other.decalSheetCellPixels;
        decalSheetDrawings   = other.decalSheetDrawings != null
                             ? new List<string>(other.decalSheetDrawings) : new List<string>();

        // Copied group by group, so the tuner's working copy never edits the preset's underneath it.
        decalGroups = new List<LandscapeDecalGroup>();
        if (other.decalGroups != null)
        {
            foreach (var group in other.decalGroups)
            {
                if (group == null) continue;
                var copy = new LandscapeDecalGroup();
                copy.CopyFrom(group);
                decalGroups.Add(copy);
            }
        }

        normalDecalSheet           = other.normalDecalSheet;
        normalDecalSheetColumns    = other.normalDecalSheetColumns;
        normalDecalSheetRows       = other.normalDecalSheetRows;
        normalDecalSheetHeld       = other.normalDecalSheetHeld;
        normalDecalSheetCellPixels = other.normalDecalSheetCellPixels;
        normalDecalSheetDrawings   = other.normalDecalSheetDrawings != null
                                   ? new List<string>(other.normalDecalSheetDrawings) : new List<string>();

        normalDecalGroups = new List<LandscapeNormalDecalGroup>();
        if (other.normalDecalGroups != null)
        {
            foreach (var group in other.normalDecalGroups)
            {
                if (group == null) continue;
                var copy = new LandscapeNormalDecalGroup();
                copy.CopyFrom(group);
                normalDecalGroups.Add(copy);
            }
        }
    }
}

/// <summary>
/// One of the landscape's normal map decal groups: a selection of the normal maps in the landscape
/// NormalMaps folder, tiled edge to edge, and which designations — the landscape, the spikes, the
/// trees — it bends. It paints no colour: it bends which way the ground faces, so the light, the
/// boat's light and the River Glow fall across the drawn relief. The landscape's copy of the river
/// runs' <see cref="NormalDecalGroup"/>, kept apart from it.
/// </summary>
[System.Serializable]
public class LandscapeNormalDecalGroup
{
    [Tooltip("What the group is called in the tuner.")]
    public string name = "Normal Map Group";

    [Tooltip("Bend the landscape's hills with this group.")]
    public bool onLandscape = true;
    [Tooltip("Bend the procedural spikes with this group.")]
    public bool onSpikes;
    [Tooltip("Bend the NomKabar trees with this group. A tree placed before decals existed counts " +
             "as landscape until it is generated again.")]
    public bool onTrees;
    [Tooltip("Bend the ground painted Red on the designer's Texture map with this group.")]
    public bool onRed = true;
    [Tooltip("Bend the ground painted Blue on the designer's Texture map with this group.")]
    public bool onBlue = true;
    [Tooltip("Bend the ground painted Yellow on the designer's Texture map with this group.")]
    public bool onYellow = true;

    [Tooltip("How hard the normal map bends the light. 0 is flat ground, 1 the relief as drawn, " +
             "past 1 deeper than drawn.")]
    [Min(0f)] public float strength = 1f;

    [Tooltip("How wide one tile is, in metres. A colour decal group Tiled at the same Spacing " +
             "and Seed lines up with these tiles.")]
    [Min(0f)] public float spacing = 0.15f;

    [Tooltip("Blend each tile into the next along a wandering gradient noise line, rather than " +
             "meeting at the straight cell edge. Near a join the tile next door is read past its " +
             "own edge, so the normal maps need to tile.")]
    public bool blendTiles;

    [Tooltip("How big the gradient noise's wobbles are, in metres across one wobble. Bigger is " +
             "broader, lazier borders between the tiles.")]
    [Min(0f)] public float blendNoiseScale = 0.15f;

    [Tooltip("How soft the fade from one tile into the next is, 0 to 1. 0 is a hard wandering " +
             "line; 1 fades across the whole tile.")]
    [Range(0f, 1f)] public float blendNoiseSoftness = 0.5f;

    [Tooltip("Which normal map each tile takes, when more than one is ticked, and where the Blend " +
             "Tiles noise wanders. The same Seed as a colour decal group picks the same tiles.")]
    public int seed;

    // The normal maps ticked into this group, by the asset GUID of each. Ticked in the tuner,
    // against the NormalMaps folder's thumbnails.
    [HideInInspector] public List<string> drawings = new List<string>();

    public void CopyFrom(LandscapeNormalDecalGroup other)
    {
        if (other == null) return;

        name               = other.name;
        onLandscape        = other.onLandscape;
        onSpikes           = other.onSpikes;
        onTrees            = other.onTrees;
        onRed              = other.onRed;
        onBlue             = other.onBlue;
        onYellow           = other.onYellow;
        strength           = other.strength;
        spacing            = other.spacing;
        blendTiles         = other.blendTiles;
        blendNoiseScale    = other.blendNoiseScale;
        blendNoiseSoftness = other.blendNoiseSoftness;
        seed               = other.seed;
        drawings           = other.drawings != null ? new List<string>(other.drawings)
                                                    : new List<string>();
    }
}

/// <summary>
/// One of the landscape's decal groups: a selection of the drawings in the landscape decal folder,
/// the scatter or tiling they are stamped with, and which designations — the landscape, the
/// spikes, the trees — it draws on. A simpler copy of the river runs' <see cref="DecalGroup"/>,
/// kept apart from it.
/// </summary>
[System.Serializable]
public class LandscapeDecalGroup
{
    [Tooltip("What the group is called in the tuner.")]
    public string name = "Decal Group";

    [Tooltip("Draw this group on the landscape's hills.")]
    public bool onLandscape = true;
    [Tooltip("Draw this group on the procedural spikes.")]
    public bool onSpikes;
    [Tooltip("Draw this group on the NomKabar trees. A tree placed before decals existed counts as " +
             "landscape until it is generated again.")]
    public bool onTrees;
    [Tooltip("Draw this group on the ground painted Red on the designer's Texture map.")]
    public bool onRed = true;
    [Tooltip("Draw this group on the ground painted Blue on the designer's Texture map.")]
    public bool onBlue = true;
    [Tooltip("Draw this group on the ground painted Yellow on the designer's Texture map.")]
    public bool onYellow = true;

    [Tooltip("How many of the scatter's places actually hold a decal, 0 to 1. It thins the " +
             "scatter out without moving what is left. 0 draws none.")]
    [Range(0f, 1f)] public float amount;

    [Tooltip("How much of the ground each decal hides, 0 to 1. 0 draws none.")]
    [Range(0f, 1f)] public float opacity = 1f;

    [Tooltip("How far apart the places a decal can stand are, in metres.")]
    [Min(0f)] public float spacing = 0.15f;

    [Tooltip("Tile the decals edge to edge: every place holds one, each exactly Spacing across, " +
             "so a tileable drawing repeats without gaps or seams. Amount and Scale are not " +
             "read. Tiles are square, so a drawing that is not square leaves gaps.")]
    public bool tiled;

    [Tooltip("Blend each tile into the next along a wandering gradient noise line, rather than " +
             "meeting at the straight cell edge. Each drawing still sits square on its grid; only " +
             "which tile shows wanders. Near a join the tile next door is read past its own edge, " +
             "so the drawings need to tile. Only read while Tiled is on.")]
    public bool blendTiles;

    [Tooltip("How big the gradient noise's wobbles are, in metres across one wobble. Bigger is " +
             "broader, lazier borders between the tiles.")]
    [Min(0f)] public float blendNoiseScale = 0.15f;

    [Tooltip("How soft the fade from one tile into the next is, 0 to 1. 0 is a hard wandering " +
             "line; 1 fades across the whole tile.")]
    [Range(0f, 1f)] public float blendNoiseSoftness = 0.5f;

    [Tooltip("The smallest a decal is drawn, in metres across its longest side.")]
    [Min(0f)] public float scaleMin = 0.04f;

    [Tooltip("The largest a decal is drawn, in metres across its longest side. Past about six " +
             "times the spacing the biggest decals start being cut off square.")]
    [Min(0f)] public float scaleMax = 0.09f;

    [Tooltip("Turn some of the decals by a random quarter turn — 90, 180 or 270 degrees, never " +
             "mirrored.")]
    public bool rotate;

    [Tooltip("How many of the decals are turned, 0 to 1. The rest stand as drawn. Only read " +
             "while Random 90° Rotation is on.")]
    [Range(0f, 1f)] public float rotateAmount = 0.5f;

    [Tooltip("How the group's decals mix with what is under them: the ground and any group above " +
             "this one in the list. Normal lays them on as drawn.")]
    public DecalBlendMode blendMode = DecalBlendMode.Normal;

    // The drawings ticked into this group, by the asset GUID of each — so renaming or redrawing
    // one keeps it in the group. Ticked in the tuner, against the folder's thumbnails.
    [HideInInspector] public List<string> drawings = new List<string>();

    // Moves the whole scatter, so two groups with the same spacing stand their decals in
    // different places rather than one exactly over the other.
    [HideInInspector] public int seed;

    public void CopyFrom(LandscapeDecalGroup other)
    {
        if (other == null) return;

        name               = other.name;
        onLandscape        = other.onLandscape;
        onSpikes           = other.onSpikes;
        onTrees            = other.onTrees;
        onRed              = other.onRed;
        onBlue             = other.onBlue;
        onYellow           = other.onYellow;
        amount             = other.amount;
        opacity            = other.opacity;
        spacing            = other.spacing;
        tiled              = other.tiled;
        blendTiles         = other.blendTiles;
        blendNoiseScale    = other.blendNoiseScale;
        blendNoiseSoftness = other.blendNoiseSoftness;
        scaleMin           = other.scaleMin;
        scaleMax           = other.scaleMax;
        rotate             = other.rotate;
        rotateAmount       = other.rotateAmount;
        blendMode          = other.blendMode;
        drawings          = other.drawings != null ? new List<string>(other.drawings)
                                                    : new List<string>();
        seed               = other.seed;
    }
}
