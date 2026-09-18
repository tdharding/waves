using UnityEngine;
using UnityEngine.Serialization;

using System.Collections.Generic;

/// <summary>
/// Which of the three stone colours a part of a piece takes. Auto leaves it to RiverMeshBuilder to
/// work out from where the face sits, which is what every part got before it could be chosen. The
/// numbers are what RiverMeshBuilder bakes into UV2.x and RiverRunShading.hlsl reads, so they must
/// not change.
/// </summary>
public enum StoneFaceKind
{
    Auto  = 0,
    Outer = 1,
    Rim   = 2,
    Inner = 3,
}

/// <summary>Tagging the triangles of a piece with the stone colour each part takes.</summary>
public static class StoneFaces
{
    /// <summary>
    /// Brings <paramref name="kinds"/> up to one entry per triangle in <paramref name="tris"/>,
    /// the new entries carrying <paramref name="kind"/>. Call after adding each part.
    /// </summary>
    public static void Tag(List<float> kinds, List<int> tris, StoneFaceKind kind)
    {
        if (kinds == null) return;
        while (kinds.Count < tris.Count / 3) kinds.Add((float)kind);
    }

    /// <summary>
    /// Brings <paramref name="parts"/> up to one entry per triangle in <paramref name="tris"/>,
    /// the new entries carrying <paramref name="part"/>. Faces of different parts always meet at a
    /// seam — see <see cref="RiverMeshBuilder.ShadeAsStone"/>.
    /// </summary>
    public static void Part(List<int> parts, List<int> tris, int part)
    {
        if (parts == null) return;
        while (parts.Count < tris.Count / 3) parts.Add(part);
    }
}

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
/// The seam extent is a PERCENTAGE of the width of the surface a face belongs to — one number for
/// every piece, so a wide river, a thin one, an outpost wall and a tower stem each get a gradient
/// in proportion to themselves. The waterline extent is metres. The seams, the surface widths and
/// which colour each face takes are worked out as each piece is generated and baked into UV1/UV2 —
/// so the numbers here are live, while the part colour choices need the pieces rebuilt.
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

    // Each of the three carries its own grain. Grain size is how wide one cell of the noise is,
    // in metres — the rivers are small out here, so a couple of centimetres is already coarse —
    // and 0 turns it off. Strength is how far the grain pulls either side of the colour: it
    // darkens and lightens by as much as each other, so it grains a colour without dragging it
    // down, and it goes on last, over the seams and the waterline too. 0 leaves the stone clean.
    //
    // The grains were one shared pair before, so each of the three picks up that pair from a
    // preset saved then.

    [Header("Stone — every piece")]
    [Header("Outer")]
    [Tooltip("The outer walls, the underside, and the open ends where one piece butts onto the " +
             "next — everything the run turns to the world rather than to the water.")]
    public Color outerColour = new Color(0.786f, 0.786f, 0.786f, 1f);

    [Tooltip("How wide one cell of the grain is on outer faces, in metres. 0 turns it off.")]
    [FormerlySerializedAs("grainSize")]
    [Min(0f)] public float outerGrainSize = 0.02f;

    [Tooltip("How far the grain pulls either side of the outer colour. 0 leaves it clean.")]
    [FormerlySerializedAs("grainStrength")]
    [Range(0f, 1f)] public float outerGrainStrength = 0.3f;

    [Header("Rim")]
    [Tooltip("The flat lip laid round the top of the run, between the outer wall and the channel.")]
    public Color rimColour = new Color(0.786f, 0.786f, 0.786f, 1f);

    [Tooltip("How wide one cell of the grain is on rim faces, in metres. 0 turns it off.")]
    [FormerlySerializedAs("grainSize")]
    [Min(0f)] public float rimGrainSize = 0.02f;

    [Tooltip("How far the grain pulls either side of the rim colour. 0 leaves it clean.")]
    [FormerlySerializedAs("grainStrength")]
    [Range(0f, 1f)] public float rimGrainStrength = 0.3f;

    [Header("Inner")]
    [Tooltip("The inside of the channel — the faces the water runs down, above the surface and " +
             "below it.")]
    public Color innerColour = new Color(0.786f, 0.786f, 0.786f, 1f);

    [Tooltip("How wide one cell of the grain is on inner faces, in metres. 0 turns it off.")]
    [FormerlySerializedAs("grainSize")]
    [Min(0f)] public float innerGrainSize = 0.02f;

    [Tooltip("How far the grain pulls either side of the inner colour. 0 leaves it clean.")]
    [FormerlySerializedAs("grainStrength")]
    [Range(0f, 1f)] public float innerGrainStrength = 0.3f;

    // ─────────────────────────────────────────────────────────────
    // THE SEAMS
    // ─────────────────────────────────────────────────────────────

    [Header("Seams — every piece")]
    [Tooltip("The colour gathered along the seams — every hard corner, inner or outer: a rim's " +
             "two edges, the foot and top of a wall, where a tower's stem meets its base and orb.")]
    public Color seamColour = Color.black;

    [Tooltip("How much of that colour sits on the seam itself. 0 turns the seams off entirely " +
             "and gives the stone back exactly as it was.")]
    [Min(0f)] public float seamStrength = 1f;

    [Tooltip("How far the gradient reaches off each seam, as a percentage of the width of the " +
             "surface it runs across — a rim strip's width, a wall's height, a channel's width. " +
             "One number for every piece, so each is shaded in proportion to its own size. 50 " +
             "has the two edges of a surface meeting in the middle. 0 turns the seams off.")]
    [Range(0f, 100f)] public float seamExtentPercent = 20f;

    // ─────────────────────────────────────────────────────────────
    // THE WATERLINE
    // ─────────────────────────────────────────────────────────────

    [Header("River Runs — waterline")]
    [Tooltip("The colour rising off the water up the inside of the channel. River runs and pools " +
             "sort their own faces into outer, rim and inner, as they always have.")]
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

    // Where the light STANDS is not here: it is the world's, authored in the designer's Aesthetics
    // and shared with the landscape hills, so the two can never be lit from two places.

    [Header("Light — every piece")]
    [Tooltip("How hard the stone is turned against the world's light (its position is in the " +
             "designer's Aesthetics). 0 leaves it flat and unlit — the " +
             "colour exactly as it came in — and 1 is the full turn, darkest on the faces backing " +
             "away from the light. There is no falloff with distance: the position says which way " +
             "the light comes from, this says how much of it there is. Past 1 the lit result is " +
             "multiplied, up to 10x brighter on the faces toward the light.")]
    [Range(0f, 10f)] public float lightStrength = 0.6f;

    // ─────────────────────────────────────────────────────────────
    // PIECE COLOURS
    //
    // Which of the three stone colours each part of each kind of piece takes. A run's way of
    // sorting its faces — outer walls, a flat lip, a channel — does not fit an outpost or a tower,
    // so each part is told instead. Auto keeps what the builder worked out before this existed.
    // Baked into the meshes, so a change here needs the pieces rebuilt.
    // ─────────────────────────────────────────────────────────────

    [Header("Outposts — needs rebuild")]
    [Tooltip("The four walls round the outside of the block, from the top of its wall down.")]
    public StoneFaceKind outpostOutsideWalls = StoneFaceKind.Auto;

    [Tooltip("The top of the wall round the floor.")]
    public StoneFaceKind outpostWallTop = StoneFaceKind.Auto;

    [Tooltip("The inside faces of the wall, facing in across the floor.")]
    public StoneFaceKind outpostInsideWalls = StoneFaceKind.Auto;

    [Tooltip("The floor inside the wall — or the whole flat top of an outpost with no wall.")]
    public StoneFaceKind outpostFloor = StoneFaceKind.Auto;

    [Header("Lollipop Towers — needs rebuild")]
    [Tooltip("The orb on top of every lollipop tower, on outposts, pools or anywhere else.")]
    public StoneFaceKind towerOrb = StoneFaceKind.Auto;

    [Tooltip("The stem of every lollipop tower.")]
    public StoneFaceKind towerStem = StoneFaceKind.Auto;

    [Tooltip("The round side of the base every lollipop tower stands on.")]
    [FormerlySerializedAs("towerBase")]
    public StoneFaceKind towerBaseSides = StoneFaceKind.Auto;

    [Tooltip("The flat top of the base, round the foot of the stem.")]
    [FormerlySerializedAs("towerBase")]
    public StoneFaceKind towerBaseTop = StoneFaceKind.Auto;

    [Header("Arena Walls — needs rebuild")]
    [Tooltip("The face of the wall looking in toward the arena.")]
    public StoneFaceKind arenaInnerFace = StoneFaceKind.Auto;

    [Tooltip("The face of the wall looking out, away from the arena.")]
    public StoneFaceKind arenaOuterFace = StoneFaceKind.Auto;

    [Tooltip("The top of the wall.")]
    public StoneFaceKind arenaTop = StoneFaceKind.Auto;

    [Header("Arena Archways — needs rebuild")]
    [Tooltip("The inside of the arch — the surface you pass under.")]
    public StoneFaceKind archwayInside = StoneFaceKind.Auto;

    [Tooltip("The outside of the arch, over its top and down its legs.")]
    public StoneFaceKind archwayOutside = StoneFaceKind.Auto;

    [Tooltip("The front and back faces of the arch band.")]
    public StoneFaceKind archwayFaces = StoneFaceKind.Auto;

    [Tooltip("The feet the arch stands on.")]
    public StoneFaceKind archwayFeet = StoneFaceKind.Auto;

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
            outerGrainStrength = 0f, rimGrainStrength = 0f, innerGrainStrength = 0f,
            seamStrength  = 0f, waterlineStrength = 0f, lightStrength = 0f,
        };

    private static readonly int OuterColourId     = Shader.PropertyToID("_RiverRunOuterColour");
    private static readonly int RimColourId       = Shader.PropertyToID("_RiverRunRimColour");
    private static readonly int InnerColourId     = Shader.PropertyToID("_RiverRunInnerColour");
    private static readonly int GrainSizeId       = Shader.PropertyToID("_RiverRunGrainSizes");
    private static readonly int GrainStrengthId   = Shader.PropertyToID("_RiverRunGrainStrengths");
    private static readonly int SeamColourId      = Shader.PropertyToID("_RiverRunSeamColour");
    private static readonly int SeamStrengthId    = Shader.PropertyToID("_RiverRunSeamStrength");
    private static readonly int SeamExtentId      = Shader.PropertyToID("_RiverRunSeamExtent");
    private static readonly int WaterColourId     = Shader.PropertyToID("_RiverRunWaterlineColour");
    private static readonly int WaterStrengthId   = Shader.PropertyToID("_RiverRunWaterlineStrength");
    private static readonly int WaterExtentId     = Shader.PropertyToID("_RiverRunWaterlineExtent");
    private static readonly int WaterDepthId      = Shader.PropertyToID("_RiverRunWaterDepth");
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
    /// The settings a mesh being built right now should bake its choices from — the tuner's if it
    /// is driving, so a rebuild shows what the tuner shows, and this world's otherwise. Null when
    /// neither exists, which leaves every face kind worked out the ordinary way.
    /// </summary>
    public static RiverRunShadingSettings ForBuild(RiverRunShadingSettings settings) =>
        Live ?? settings;

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

        // x outer, y rim, z inner — the same order as the face kinds.
        Shader.SetGlobalVector(GrainSizeId,
            new Vector4(outerGrainSize, rimGrainSize, innerGrainSize, 0f));
        Shader.SetGlobalVector(GrainStrengthId,
            new Vector4(outerGrainStrength, rimGrainStrength, innerGrainStrength, 0f));

        Shader.SetGlobalColor(SeamColourId,    seamColour);
        Shader.SetGlobalFloat(SeamStrengthId,  seamStrength);
        Shader.SetGlobalFloat(SeamExtentId,    seamExtentPercent);

        Shader.SetGlobalColor(WaterColourId,   waterlineColour);
        Shader.SetGlobalFloat(WaterStrengthId, waterlineStrength);
        Shader.SetGlobalFloat(WaterExtentId,   waterlineExtent);

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
        outerGrainSize     = other.outerGrainSize;
        outerGrainStrength = other.outerGrainStrength;
        rimGrainSize       = other.rimGrainSize;
        rimGrainStrength   = other.rimGrainStrength;
        innerGrainSize     = other.innerGrainSize;
        innerGrainStrength = other.innerGrainStrength;

        seamColour        = other.seamColour;
        seamStrength      = other.seamStrength;
        seamExtentPercent = other.seamExtentPercent;

        waterlineColour   = other.waterlineColour;
        waterlineStrength = other.waterlineStrength;
        waterlineExtent   = other.waterlineExtent;

        lightStrength     = other.lightStrength;

        outpostOutsideWalls = other.outpostOutsideWalls;
        outpostWallTop      = other.outpostWallTop;
        outpostInsideWalls  = other.outpostInsideWalls;
        outpostFloor        = other.outpostFloor;

        towerOrb          = other.towerOrb;
        towerStem         = other.towerStem;
        towerBaseSides    = other.towerBaseSides;
        towerBaseTop      = other.towerBaseTop;

        arenaInnerFace    = other.arenaInnerFace;
        arenaOuterFace    = other.arenaOuterFace;
        arenaTop          = other.arenaTop;

        archwayInside     = other.archwayInside;
        archwayOutside    = other.archwayOutside;
        archwayFaces      = other.archwayFaces;
        archwayFeet       = other.archwayFeet;
    }
}
