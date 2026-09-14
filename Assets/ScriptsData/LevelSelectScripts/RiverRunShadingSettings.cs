using UnityEngine;

/// <summary>
/// How the level select's stone rivers are shaded: the stone's own colour, one for each part of a
/// run and grained with a noise, dark gathered along every seam, and white rising off the
/// waterline up the inside of the channel.
///
/// These are bare $Globals in the shader, not properties on the run material — one set of numbers
/// for every generated piece in the world, so the look is authored once rather than per material,
/// and a run, a pool and an arena wall can never drift apart. Nothing on disk remembers them:
/// there is no property block behind them, so a shader or material reimport leaves them at zero
/// until something puts them back. What puts them back is
/// <see cref="LevelSelectDesignerData.ApplyAesthetics"/>, re-pushed EVERY frame — by
/// LevelSelectDataController in play mode and by LevelSelectAestheticsPump out of it.
///
/// Authored in Tools > Waves > Level Select Run Shading Tuner, and held by the world's
/// <see cref="LevelSelectRiverStructurePreset"/> so it survives a reload. The water's ripples sit
/// on a preset of their own — they are a different material on different geometry, tuned in a
/// different window, and holding the two on one asset only gave them somewhere to drift apart.
///
/// Both extents are METRES on the mesh, measured across the surface. The seams they run from are
/// worked out by RiverMeshBuilder as each piece is generated and baked into UV1, and which part of
/// a run each face belongs to is worked out alongside them and baked into UV2 — so a change here
/// is live, and it is only the seams and the face kinds themselves that need a Rebuild Runs.
/// </summary>
[System.Serializable]
public class RiverRunShadingSettings
{
    // ─────────────────────────────────────────────────────────────
    // THE STONE
    //
    // Three colours rather than one, because a run shows three quite different things: the OUTER
    // faces it turns to the world — the outer walls, the underside, the open ends — the RIM lip
    // laid flat round the top, and the INNER faces of the channel the water runs down.
    //
    // Which of the three a face is cannot be worked out in the shader. A steep channel wall and
    // an outer wall are both very nearly vertical, and telling them apart needs to know which
    // side of the piece a face is on — so RiverMeshBuilder settles it as the piece is generated
    // and bakes it into UV2, exactly as it does the seams. A run built before that needs a
    // Rebuild Runs before any of these three can reach it, and until then it keeps the one
    // colour the run graph hands it.
    // ─────────────────────────────────────────────────────────────

    [Tooltip("The outer walls, the underside, and the open ends where one piece butts onto the " +
             "next — everything the run turns to the world rather than to the water.")]
    public Color outerColour = new Color(0.786f, 0.786f, 0.786f, 1f);

    [Tooltip("The flat lip laid round the top of the run, between the outer wall and the channel.")]
    public Color rimColour = new Color(0.786f, 0.786f, 0.786f, 1f);

    [Tooltip("The inside of the channel — the faces the water runs down, above the surface and " +
             "below it.")]
    public Color innerColour = new Color(0.786f, 0.786f, 0.786f, 1f);

    [Tooltip("How wide one cell of the grain is, in metres. The generated stone is cut in " +
             "metres, so this is the size of a speckle on the surface — the rivers are small " +
             "out here, so a couple of centimetres is already a coarse grain. 0 turns it off.")]
    [Min(0f)] public float grainSize = 0.02f;

    [Tooltip("How far the grain pulls either side of the colour underneath it. It darkens and " +
             "lightens by as much as each other, so turning this up grains a colour without " +
             "also dragging it down. It goes on last, over the seams and the waterline as well " +
             "as over the bare stone. 0 leaves the stone clean.")]
    [Range(0f, 1f)] public float grainStrength = 0.3f;

    // ─────────────────────────────────────────────────────────────
    // THE SEAMS
    // ─────────────────────────────────────────────────────────────

    [Tooltip("The colour gathered along the seams — the rim's two edges, the foot of the outer " +
             "wall, and the joints where one generated piece butts onto the next.")]
    public Color seamColour = Color.black;

    [Tooltip("How much of that colour sits on the seam itself. 0 turns the seams off entirely " +
             "and gives the stone back exactly as it was.")]
    [Min(0f)] public float seamStrength = 1f;

    [Tooltip("How far the darkness carries off a seam, in metres. The rim is about 0.43m across " +
             "at the authored shape, so anything over half of that has its two edges meeting in " +
             "the middle. 0 turns the seams off.")]
    [Min(0f)] public float seamExtent = 0.12f;

    // ─────────────────────────────────────────────────────────────
    // THE WATERLINE
    // ─────────────────────────────────────────────────────────────

    [Tooltip("The colour rising off the water up the inside of the channel.")]
    public Color waterlineColour = Color.white;

    [Tooltip("How much of that colour sits at the waterline. 0 turns the band off entirely.")]
    [Min(0f)] public float waterlineStrength = 1f;

    [Tooltip("How far the band climbs off the water, in metres. It stops at the rim whatever " +
             "this says, because there is only as much channel wall above the water as the " +
             "world's Water Level leaves standing. 0 turns the band off.")]
    [Min(0f)] public float waterlineExtent = 0.2f;

    // ─────────────────────────────────────────────────────────────
    // THE LIGHT
    //
    // There is no real light out here, so the stone is shaped by one made up: a POINT standing
    // somewhere in the world, with every pixel turned against the direction from itself to it.
    // This replaced the Simulated Lighting Basic node the run shader used to carry, so a run can
    // no longer be lit from one angle while its seams are drawn for another.
    //
    // A point rather than a bearing because the direction then CHANGES across the world — the runs
    // nearest it turn to face it and the far side of the map falls away, which is what lets one
    // light be placed to pick out a particular stretch of river. A bearing gives every piece the
    // same answer, which is a sun, and a sun cannot be aimed at anything.
    // ─────────────────────────────────────────────────────────────

    [Tooltip("Where the light stands, in world space. Every pixel works out its own direction to " +
             "it, so moving it round the map changes which runs are raked and which fall away. " +
             "Read a spot off the scene view and type it in — the rivers sit around y = 0, so a " +
             "light wants to be some way above that to reach the rim tops.")]
    public Vector3 lightPosition = new Vector3(40f, 25f, 20f);

    [Tooltip("How hard the stone is turned against that light. 0 leaves it flat and unlit — the " +
             "colour exactly as it came in — and 1 is the full turn, darkest on the faces backing " +
             "away from the light. There is no falloff with distance: the position says which way " +
             "the light comes from, this says how much of it there is.")]
    [Range(0f, 1f)] public float lightStrength = 0.6f;

    // ─────────────────────────────────────────────────────────────
    // PUSHING
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The tuner, while it is driving. Set by the Level Select Run Shading Tuner with Apply Live
    /// on, and cleared when it closes or the toggle goes off.
    ///
    /// It has to win over the designer's preset rather than sit alongside it: the aesthetics pump
    /// re-pushes that preset every editor tick, so without this a dragged slider would be dragged
    /// straight back to the saved number before the next repaint.
    /// </summary>
    public static RiverRunShadingSettings Live;

    /// <summary>
    /// No shading at all — what a world with no preset assigned gets. The three face colours are
    /// left at the plain stone they default to, which is the colour the run graph carries anyway,
    /// so a world with nothing authored draws bare unlit stone.
    /// </summary>
    private static readonly RiverRunShadingSettings None =
        new RiverRunShadingSettings
        {
            grainStrength = 0f,
            seamStrength  = 0f, waterlineStrength = 0f, lightStrength = 0f,
        };

    private static readonly int OuterColourId     = Shader.PropertyToID("_RiverRunOuterColour");
    private static readonly int RimColourId       = Shader.PropertyToID("_RiverRunRimColour");
    private static readonly int InnerColourId     = Shader.PropertyToID("_RiverRunInnerColour");
    private static readonly int GrainSizeId       = Shader.PropertyToID("_RiverRunGrainSize");
    private static readonly int GrainStrengthId   = Shader.PropertyToID("_RiverRunGrainStrength");
    private static readonly int SeamColourId      = Shader.PropertyToID("_RiverRunSeamColour");
    private static readonly int SeamStrengthId    = Shader.PropertyToID("_RiverRunSeamStrength");
    private static readonly int SeamExtentId      = Shader.PropertyToID("_RiverRunSeamExtent");
    private static readonly int WaterColourId     = Shader.PropertyToID("_RiverRunWaterlineColour");
    private static readonly int WaterStrengthId   = Shader.PropertyToID("_RiverRunWaterlineStrength");
    private static readonly int WaterExtentId     = Shader.PropertyToID("_RiverRunWaterlineExtent");
    private static readonly int WaterDepthId      = Shader.PropertyToID("_RiverRunWaterDepth");
    private static readonly int LightPosId        = Shader.PropertyToID("_RiverRunLightPosition");
    private static readonly int LightStrengthId   = Shader.PropertyToID("_RiverRunLightStrength");

    /// <summary>
    /// Publishes one set of shading numbers to the shaders — the tuner's if it is driving, this
    /// world's otherwise, and none at all if the world has no preset.
    ///
    /// Called every frame rather than once, for the reason in the class note above.
    /// </summary>
    public static void Push(RiverRunShadingSettings settings)
    {
        (Live ?? settings ?? None).Apply();
    }

    /// <summary>
    /// How far the water lies beneath the rim top, which is what places the waterline band.
    ///
    /// Not one of the numbers above and not the tuner's to set: it is a FACT about the world
    /// rather than a way it looks, and it is already authored once as the designer's Water Level.
    /// Carrying a second copy here would only give it somewhere to fall out of step, and a band
    /// drawn off a stale copy sits above or below the water it is meant to be sitting on.
    /// </summary>
    public static void PushWaterDepth(float metresBelowRim)
    {
        Shader.SetGlobalFloat(WaterDepthId, metresBelowRim);
    }

    private void Apply()
    {
        // Alpha is not a colour on these three. There is no property block behind a bare global,
        // so a shader reimport leaves all four channels at zero, and the shader reads an alpha of
        // nothing as "nobody has pushed this yet" and gives the stone back to the run graph's own
        // colour rather than painting it black for the tick it takes to be pushed again. So it
        // goes over solid whatever was picked in the colour field.
        Shader.SetGlobalColor(OuterColourId,   Opaque(outerColour));
        Shader.SetGlobalColor(RimColourId,     Opaque(rimColour));
        Shader.SetGlobalColor(InnerColourId,   Opaque(innerColour));

        Shader.SetGlobalFloat(GrainSizeId,     grainSize);
        Shader.SetGlobalFloat(GrainStrengthId, grainStrength);

        Shader.SetGlobalColor(SeamColourId,    seamColour);
        Shader.SetGlobalFloat(SeamStrengthId,  seamStrength);
        Shader.SetGlobalFloat(SeamExtentId,    seamExtent);

        Shader.SetGlobalColor(WaterColourId,   waterlineColour);
        Shader.SetGlobalFloat(WaterStrengthId, waterlineStrength);
        Shader.SetGlobalFloat(WaterExtentId,   waterlineExtent);

        Shader.SetGlobalVector(LightPosId,      lightPosition);
        Shader.SetGlobalFloat(LightStrengthId,  lightStrength);
    }

    /// <summary>
    /// A face colour on its way to the shader, made solid so a colour picked with its alpha
    /// pulled down is still the colour that face is drawn in — alpha is not a colour on these
    /// three, it is the flag that says one has been pushed at all.
    ///
    /// A colour with nothing in it whatsoever is the exception, and is left exactly as it is. A
    /// preset saved before these three existed has no colour on it to load, and making that
    /// solid would paint the stone black rather than leaving it to the run graph, which is what
    /// having nothing to say ought to do.
    /// </summary>
    private static Color Opaque(Color c) =>
        c == default ? c : new Color(c.r, c.g, c.b, 1f);

    /// <summary>Field-by-field copy, so the tuner can load a preset without holding on to it.</summary>
    public void CopyFrom(RiverRunShadingSettings other)
    {
        if (other == null) return;

        outerColour       = other.outerColour;
        rimColour         = other.rimColour;
        innerColour       = other.innerColour;
        grainSize         = other.grainSize;
        grainStrength     = other.grainStrength;

        seamColour        = other.seamColour;
        seamStrength      = other.seamStrength;
        seamExtent        = other.seamExtent;

        waterlineColour   = other.waterlineColour;
        waterlineStrength = other.waterlineStrength;
        waterlineExtent   = other.waterlineExtent;

        lightPosition     = other.lightPosition;
        lightStrength     = other.lightStrength;
    }
}
