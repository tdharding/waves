using UnityEngine;
using UnityEngine.Serialization;

using System.Collections.Generic;

/// <summary>
/// Which stone type a part of a piece takes — A, B, C, D, and any added after them (E is 5, F is
/// 6 and so on, cast from the number; only the four every world has are named). Auto leaves it to
/// RiverMeshBuilder to work out from where the face sits: outer faces A, the rim lip B, the inside
/// of a channel C. The numbers are what RiverMeshBuilder bakes into UV2.x and RiverRunShading.hlsl
/// reads, so they must not change — A, B and C are the 1, 2 and 3 that outer, rim and inner were
/// baked as before there were stone types, so a piece built then still takes the same colours.
/// </summary>
public enum StoneFaceKind
{
    Auto = 0,
    A    = 1,
    B    = 2,
    C    = 3,
    D    = 4,
}

/// <summary>Which way a stone type's gradient runs across the piece it is on.</summary>
public enum StoneGradientDirection
{
    BottomToTop = 0,
    TopToBottom = 1,
    LeftToRight = 2,
    RightToLeft = 3,
}

/// <summary>
/// A stone type's own look: its colour, or a gradient across the whole piece in its place, and the
/// noise grain laid over it. The world holds a list of these — A, B, C, D and any added after —
/// and every part of every piece picks one.
/// </summary>
[System.Serializable]
public class StoneType
{
    [Tooltip("The stone's colour. Replaced by the gradient while Use Gradient is on.")]
    public Color colour = new Color(0.786f, 0.786f, 0.786f, 1f);

    [Tooltip("How wide one cell of the grain is, in metres. 0 turns it off.")]
    [Min(0f)] public float grainSize = 0.02f;

    [Tooltip("How far the grain pulls either side of the colour. 0 leaves it clean.")]
    [Range(0f, 1f)] public float grainStrength = 0.3f;

    [Tooltip("Colour the stone with the gradient instead of the one colour, run across the " +
             "whole of each piece this stone type is on.")]
    public bool useGradient;

    [Tooltip("The colours run across the piece, from where the direction starts to where it ends.")]
    public Gradient gradient = Grey();

    [Tooltip("Which way the gradient runs. Up and down go from the foot of the piece to its top; " +
             "left and right go across it along the world's X, which is left to right on screen " +
             "from the level select camera.")]
    public StoneGradientDirection gradientDirection = StoneGradientDirection.BottomToTop;

    [Tooltip("The colour gathered along the seams of this stone — every hard corner it has, " +
             "inner or outer. Where two stone types meet, each side draws its own.")]
    public Color seamColour = Color.black;

    [Tooltip("How much of that colour sits on the seam itself. 0 turns this stone's seams off " +
             "and gives it back exactly as it was.")]
    [Min(0f)] public float seamStrength = 1f;

    [Tooltip("How far the gradient reaches off each seam, as a percentage of the width of the " +
             "surface it runs across — a rim strip's width, a wall's height, a channel's width. " +
             "50 has the two edges of a surface meeting in the middle. 0 turns the seams off.")]
    [Range(0f, 100f)] public float seamExtentPercent = 20f;

    // Whether the seams above have been set on this stone at all. Off on a stone saved before
    // seams were per stone, which EnsureStoneTypes then hands the world's old shared seams.
    [HideInInspector] public bool seamsSet;

    [Tooltip("How many of the scatter's places on this stone actually hold a decal, 0 to 1. It " +
             "thins the scatter out without moving what is left, so turning it down opens gaps " +
             "rather than pulling the decals closer together. 0 draws none on this stone.")]
    [Range(0f, 1f)] public float decalAmount = 0f;

    [Tooltip("How much of this stone each decal hides, 0 to 1. A different question from " +
             "Amount: that is how MANY marks there are, this is how far each of them sinks " +
             "into the stone. 0 draws none.")]
    [Range(0f, 1f)] public float decalOpacity = 1f;

    [Tooltip("How far apart the places a decal can stand are on this stone, in metres. Two " +
             "stones with the same spacing share one scatter, so a decal crossing from one to " +
             "the other carries on unbroken while their scales match.")]
    [Min(0f)] public float decalSpacing = 0.15f;

    [Tooltip("The smallest a decal is drawn on this stone, in metres across its longest side.")]
    [Min(0f)] public float decalScaleMin = 0.04f;

    [Tooltip("The largest a decal is drawn on this stone, in metres across its longest side. " +
             "Past about six times the spacing the biggest decals start being cut off square.")]
    [Min(0f)] public float decalScaleMax = 0.09f;

    [Tooltip("Turn some of this stone's decals by a random quarter turn — 90, 180 or 270 " +
             "degrees, never mirrored.")]
    public bool decalRotate;

    [Tooltip("How many of this stone's decals are turned, 0 to 1. The rest stand as drawn. " +
             "Only read while Random 90° Rotation is on.")]
    [Range(0f, 1f)] public float decalRotateAmount = 0.5f;

    // Whether the decals above have been set on this stone at all. Off on a stone saved before
    // decals were per stone, which EnsureStoneTypes then hands the world's old shared decals.
    [HideInInspector] public bool decalsSet;

    /// <summary>The default gradient: grey to grey, because the game is black and white.</summary>
    public static Gradient Grey()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.5f, 0.5f, 0.5f), 0f),
                    new GradientColorKey(new Color(0.786f, 0.786f, 0.786f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    public void CopyFrom(StoneType other)
    {
        if (other == null) return;

        colour            = other.colour;
        grainSize         = other.grainSize;
        grainStrength     = other.grainStrength;
        useGradient       = other.useGradient;
        gradientDirection = other.gradientDirection;
        seamColour        = other.seamColour;
        seamStrength      = other.seamStrength;
        seamExtentPercent = other.seamExtentPercent;
        seamsSet          = other.seamsSet;
        decalAmount       = other.decalAmount;
        decalOpacity      = other.decalOpacity;
        decalSpacing      = other.decalSpacing;
        decalScaleMin     = other.decalScaleMin;
        decalScaleMax     = other.decalScaleMax;
        decalRotate       = other.decalRotate;
        decalRotateAmount = other.decalRotateAmount;
        decalsSet         = other.decalsSet;

        // A Gradient is a reference, so it is copied key by key — sharing one would have the
        // tuner's working copy edit the preset's gradient underneath it.
        var g = new Gradient();
        if (other.gradient != null)
        {
            g.mode = other.gradient.mode;
            g.SetKeys(other.gradient.colorKeys, other.gradient.alphaKeys);
        }
        gradient = g;
    }
}

/// <summary>
/// On a <see cref="StoneFaceKind"/> field: the dropdown lists only the stone types, no Auto —
/// for a choice that is always one of them, like which stone a river run's outer faces take.
/// </summary>
public class StoneTypeOnlyAttribute : PropertyAttribute { }

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
/// How the level select's stone rivers are shaded: the stone's own colour, a stone type for each
/// part of a piece and grained with a noise, dark gathered along every seam, white rising off the waterline
/// up the inside of the channel, and hand-drawn decals scattered over the lot.
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
/// The seams are set per stone type. Their extent is a PERCENTAGE of the width of the surface a
/// face belongs to, so a wide river, a thin one, an outpost wall and a tower stem each get a
/// gradient in proportion to themselves. The waterline extent is metres. The seams, the surface widths and
/// which colour each face takes are worked out as each piece is generated and baked into UV1/UV2 —
/// so the numbers here are live, while the part colour choices need the pieces rebuilt.
///
/// The decals are drawings kept in a folder rather than numbers typed in — see
/// RiverRunDecalLibrary, which gathers them onto the one sheet the shader reads and which
/// names that folder. The sheet is held here with the rest, because there is nothing on the
/// material for it to live on either.
/// </summary>
[System.Serializable]
public class RiverRunShadingSettings
{
    // ─────────────────────────────────────────────────────────────
    // THE STONE
    //
    // A list of stone types — A, B, C, D, and any added after them — each a colour (or a gradient
    // across the whole piece in its place) with its own noise grain. Every part of every piece
    // picks one: a river run's outer faces, rim and channel below, every part of an outpost, a
    // tower, a wall or a door in the sections further down.
    //
    // Grain size is how wide one cell of the noise is, in metres — the rivers are small out here,
    // so a couple of centimetres is already coarse — and 0 turns it off. Strength is how far the
    // grain pulls either side of the colour: it darkens and lightens by as much as each other, so
    // it grains a colour without dragging it down, and it goes on last, over the seams and the
    // waterline too. 0 leaves the stone clean.
    //
    // Before there were stone types there were three fixed colours — outer, rim and inner — and
    // a preset saved then carries them in the hidden legacy fields at the bottom of this class.
    // They become A, B and C the first time the list is asked for (see EnsureStoneTypes), which
    // is also what a mesh built then reads: its faces were baked 1, 2 and 3.
    // ─────────────────────────────────────────────────────────────

    /// <summary>The most stone types a world can hold — the length of the shader's arrays.</summary>
    public const int MaxStoneTypes = 16;

    /// <summary>A, B, C and D: every world has these, and they cannot be removed.</summary>
    public const int FixedStoneTypes = 4;

    [Header("Stone Types — every piece")]
    [Tooltip("A, B, C, D and any added after them. Every part of every piece picks one.")]
    public List<StoneType> stoneTypes = new List<StoneType>();

    /// <summary>The letter a stone type is called by — A for the first.</summary>
    public static string StoneLetter(int index) => ((char)('A' + index)).ToString();

    /// <summary>
    /// Brings the list up to at least A–D. An empty list is a preset saved before stone types
    /// existed (or a fresh one), so A, B and C are taken from the old outer, rim and inner.
    /// Called before anything reads the list rather than from a serialization callback, where
    /// Unity forbids most of its own API.
    /// </summary>
    public void EnsureStoneTypes()
    {
        if (stoneTypes == null) stoneTypes = new List<StoneType>();

        if (stoneTypes.Count == 0)
        {
            stoneTypes.Add(new StoneType
                { colour = outerColour, grainSize = outerGrainSize, grainStrength = outerGrainStrength });
            stoneTypes.Add(new StoneType
                { colour = rimColour,   grainSize = rimGrainSize,   grainStrength = rimGrainStrength });
            stoneTypes.Add(new StoneType
                { colour = innerColour, grainSize = innerGrainSize, grainStrength = innerGrainStrength });
        }

        while (stoneTypes.Count < FixedStoneTypes) stoneTypes.Add(new StoneType());

        foreach (var type in stoneTypes)
        {
            if (type == null) continue;
            if (type.gradient == null) type.gradient = StoneType.Grey();

            // A stone saved before seams were per stone — or one just added — starts from the
            // seams the whole world shared, so nothing changes until it is tuned on its own.
            if (!type.seamsSet)
            {
                type.seamColour        = seamColour;
                type.seamStrength      = seamStrength;
                type.seamExtentPercent = seamExtentPercent;
                type.seamsSet          = true;
            }

            // Likewise the decals, from the one scatter the whole world shared before.
            if (!type.decalsSet)
            {
                type.decalAmount   = decalAmount;
                type.decalOpacity  = decalOpacity;
                type.decalSpacing  = decalSpacing;
                type.decalScaleMin = decalScaleMin;
                type.decalScaleMax = decalScaleMax;
                type.decalsSet     = true;
            }
        }
    }

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
    // THE DECALS
    //
    // Hand-drawn marks stamped over the stone. Scattered rather than placed: there is far more
    // generated stone out here than anybody is going to decorate by hand.
    //
    // Every drawing in Assets/TextureMatShader/LevelSelectMaterials/RiverRunDecals is carried on
    // ONE sheet, side by side in equal cells, so the shader reads one texture however many decals
    // there are. The sheet is built by the Run Shading Tuner and held here — the numbers beside
    // it describe its cells rather than how the scatter looks, which is why they are hidden from
    // the sliders and drawn by the tuner itself, above them.
    //
    // They are scattered across the WORLD in metres, not across a UV. UV0, which the grain
    // still uses, is flattened per triangle — each one onto whichever axis plane it most
    // nearly faces — so a drawing laid across two triangles that chose differently is cut along
    // the edge between them. A noise at two centimetres never showed that; a recognisable
    // drawing shows it at once. The scatter is laid on all three of the world's planes and
    // mixed by how much a surface faces each, so there is nothing to flip and a decal crosses
    // a curve unbroken. Spacing and the two Scales are therefore metres in the world.
    // ─────────────────────────────────────────────────────────────

    // The built sheet, and how its cells are laid out. Hidden from the tuner's own run of
    // sliders: the tuner draws them itself, above the numbers, where the folder they came from
    // can be named and a sheet that has fallen behind that folder can be said so.
    [HideInInspector] public Texture2D decalSheet;
    [HideInInspector] public int       decalSheetColumns;
    [HideInInspector] public int       decalSheetRows;
    [HideInInspector] public int       decalSheetHeld;
    [HideInInspector] public int       decalSheetCellPixels;

    // The scatter itself — amount, opacity, spacing and scales — is set per stone type, on
    // StoneType. Each face scatters with its own stone's numbers.

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

    // The light itself — where it stands and how strong it is — is not here: it is the scene's
    // LevelSelectLight, shared with the landscape hills, so the two can never be lit differently.

    // ─────────────────────────────────────────────────────────────
    // PIECE COLOURS
    //
    // Which stone type each part of each kind of piece takes.
    //
    // A river run sorts its own faces — outer walls, a flat lip, a channel — and the River Runs
    // three say which stone each of those is. They are NOT baked: the run carries which of the
    // three a face is, and the shader looks the stone up live, so they need no rebuild (beyond
    // the one a run built before this needs to carry the mark that says so).
    //
    // That way of sorting does not fit an outpost or a tower, so each of their parts is told
    // instead. Auto keeps what the builder worked out — outer A, rim B, inner C. Those ARE baked
    // into the meshes, so a change there needs the pieces rebuilt.
    // ─────────────────────────────────────────────────────────────

    [Header("River Runs")]
    [Tooltip("The outer walls, the underside, and the open ends where one piece butts onto the " +
             "next — everything a run or a pool turns to the world rather than to the water.")]
    [StoneTypeOnly] public StoneFaceKind runOuterFaces = StoneFaceKind.A;

    [Tooltip("The flat lip laid round the top of a run or a pool, between the outer wall and the " +
             "channel.")]
    [StoneTypeOnly] public StoneFaceKind runRim = StoneFaceKind.B;

    [Tooltip("The inside of the channel — the faces the water runs down, above the surface and " +
             "below it.")]
    [StoneTypeOnly] public StoneFaceKind runInnerChannel = StoneFaceKind.C;

    [Header("Outposts — needs rebuild")]
    [Tooltip("The four walls round the outside of the block, from the top of its wall down.")]
    public StoneFaceKind outpostOutsideWalls = StoneFaceKind.Auto;

    [Tooltip("The top of the wall round the floor.")]
    public StoneFaceKind outpostWallTop = StoneFaceKind.Auto;

    [Tooltip("The inside faces of the wall, facing in across the floor.")]
    public StoneFaceKind outpostInsideWalls = StoneFaceKind.Auto;

    [Tooltip("The floor inside the wall — or the whole flat top of an outpost with no wall.")]
    public StoneFaceKind outpostFloor = StoneFaceKind.Auto;

    [Header("Freestanding Structures — needs rebuild")]
    [Tooltip("The sides of the block or round column, from its top down — and the sides of its " +
             "columns.")]
    public StoneFaceKind structureSides = StoneFaceKind.Auto;

    [Tooltip("The top of the wall round an outpost top — and the tops of its columns.")]
    public StoneFaceKind structureWallTop = StoneFaceKind.Auto;

    [Tooltip("The inside faces of an outpost top's wall, facing in across the floor.")]
    public StoneFaceKind structureInsideWalls = StoneFaceKind.Auto;

    [Tooltip("The flat top of the structure — the floor inside the wall when it has an outpost top.")]
    public StoneFaceKind structureTop = StoneFaceKind.Auto;

    [Header("River Walkways — needs rebuild")]
    [Tooltip("The top of the walkway along the outside of the run.")]
    public StoneFaceKind walkwayTop = StoneFaceKind.Auto;

    [Tooltip("The walkway's outside face, away from the river, from its top down.")]
    public StoneFaceKind walkwayOuterWall = StoneFaceKind.Auto;

    [Tooltip("The step up from the rim to the walkway's top, when it stands above the rim.")]
    public StoneFaceKind walkwayStep = StoneFaceKind.Auto;

    [Tooltip("The square ends of each stretch of walkway.")]
    public StoneFaceKind walkwayEnds = StoneFaceKind.Auto;

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

    [Header("Walls — needs rebuild")]
    [Tooltip("Both long faces of every wall, from its top down.")]
    public StoneFaceKind wallSides = StoneFaceKind.Auto;

    [Tooltip("The top of the wall.")]
    public StoneFaceKind wallTop = StoneFaceKind.Auto;

    [Tooltip("The flat ends where a wall stops, or breaks off at a gap.")]
    public StoneFaceKind wallEnds = StoneFaceKind.Auto;

    [Tooltip("Inside the archways and portholes cut through the wall — their sides, roofs and floors.")]
    public StoneFaceKind wallOpenings = StoneFaceKind.Auto;

    [Tooltip("The round side of a column standing on a wall node.")]
    public StoneFaceKind wallColumnSides = StoneFaceKind.Auto;

    [Tooltip("The flat top of a column standing on a wall node.")]
    public StoneFaceKind wallColumnTop = StoneFaceKind.Auto;

    [Tooltip("The top of a wall's lip. Auto = the same as the wall's top.")]
    public StoneFaceKind wallLipTop = StoneFaceKind.Auto;

    [Tooltip("Both long faces of a wall's lip, and its flat ends. Auto = the same as the wall's sides.")]
    public StoneFaceKind wallLipSides = StoneFaceKind.Auto;

    [Tooltip("Under a wall's lip, where it overhangs the wall. Auto = the same as the wall's sides.")]
    public StoneFaceKind wallLipUnderside = StoneFaceKind.Auto;

    [Header("Arena — needs rebuild")]
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

    [Header("Arena Doors — needs rebuild")]
    [Tooltip("The sheet filling the archway behind the door, with the soul opening cut out of it.")]
    public StoneFaceKind doorSheet = StoneFaceKind.Auto;

    [Tooltip("The face of the frame running round the keyhole.")]
    public StoneFaceKind doorFrame = StoneFaceKind.Auto;

    [Tooltip("The panel filling the keyhole inside the frame, which the soul opening is cut out of.")]
    public StoneFaceKind doorPanel = StoneFaceKind.Auto;

    [Tooltip("The face of the rim running round the soul opening.")]
    public StoneFaceKind doorFlapRim = StoneFaceKind.Auto;

    [Tooltip("The face of the round disc on the bulb, which the arena's numeral is drawn on.")]
    public StoneFaceKind doorDisc = StoneFaceKind.Auto;

    [Tooltip("The sides of both rims and of the disc, where they stand off the sheet.")]
    public StoneFaceKind doorEdges = StoneFaceKind.Auto;

    // ─────────────────────────────────────────────────────────────
    // LEGACY
    //
    // The three fixed stone colours there were before stone types. Read once, by
    // EnsureStoneTypes, to give a preset saved then its A, B and C; nothing else reads them.
    // Kept under their old names so that preset still has somewhere to land.
    // ─────────────────────────────────────────────────────────────

    [HideInInspector] public Color outerColour = new Color(0.786f, 0.786f, 0.786f, 1f);
    [HideInInspector, FormerlySerializedAs("grainSize")]     public float outerGrainSize     = 0.02f;
    [HideInInspector, FormerlySerializedAs("grainStrength")] public float outerGrainStrength = 0.3f;

    [HideInInspector] public Color rimColour = new Color(0.786f, 0.786f, 0.786f, 1f);
    [HideInInspector, FormerlySerializedAs("grainSize")]     public float rimGrainSize     = 0.02f;
    [HideInInspector, FormerlySerializedAs("grainStrength")] public float rimGrainStrength = 0.3f;

    [HideInInspector] public Color innerColour = new Color(0.786f, 0.786f, 0.786f, 1f);
    [HideInInspector, FormerlySerializedAs("grainSize")]     public float innerGrainSize     = 0.02f;
    [HideInInspector, FormerlySerializedAs("grainStrength")] public float innerGrainStrength = 0.3f;

    // The one set of seams every stone shared before each stone type carried its own. Read by
    // EnsureStoneTypes to give a stone that has none yet its starting seams.
    [HideInInspector] public Color seamColour        = Color.black;
    [HideInInspector] public float seamStrength      = 1f;
    [HideInInspector] public float seamExtentPercent = 20f;

    // The one decal scatter every stone shared before each stone type carried its own. Read by
    // EnsureStoneTypes to give a stone that has none yet its starting decals.
    [HideInInspector] public float decalAmount   = 0f;
    [HideInInspector] public float decalOpacity  = 1f;
    [HideInInspector] public float decalSpacing  = 0.15f;
    [HideInInspector] public float decalScaleMin = 0.04f;
    [HideInInspector] public float decalScaleMax = 0.09f;

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
    /// No shading at all — what a world with no preset assigned gets. The stone types' colours are
    /// left at the plain stone they default to, which is the colour the run graph carries anyway,
    /// so a world with nothing authored draws bare unlit stone.
    /// </summary>
    private static RiverRunShadingSettings None
    {
        get
        {
            // Made on first use, so its stone types are brought up to A–D like any other's.
            if (_none != null) return _none;

            _none = new RiverRunShadingSettings { waterlineStrength = 0f };
            _none.EnsureStoneTypes();
            foreach (var type in _none.stoneTypes) { type.grainStrength = 0f; type.seamStrength = 0f; }
            return _none;
        }
    }
    private static RiverRunShadingSettings _none;

    private static readonly int StoneColoursId    = Shader.PropertyToID("_RiverRunStoneColours");
    private static readonly int StoneGrainsId     = Shader.PropertyToID("_RiverRunStoneGrains");
    private static readonly int StoneGradientsId  = Shader.PropertyToID("_RiverRunStoneGradients");
    private static readonly int RunStonesId       = Shader.PropertyToID("_RiverRunRunStones");
    private static readonly int StoneSeamsId      = Shader.PropertyToID("_RiverRunStoneSeams");
    private static readonly int WaterColourId     = Shader.PropertyToID("_RiverRunWaterlineColour");
    private static readonly int WaterStrengthId   = Shader.PropertyToID("_RiverRunWaterlineStrength");
    private static readonly int WaterExtentId     = Shader.PropertyToID("_RiverRunWaterlineExtent");
    private static readonly int WaterDepthId      = Shader.PropertyToID("_RiverRunWaterDepth");

    private static readonly int DecalSheetId    = Shader.PropertyToID("_RiverRunDecalSheet");
    private static readonly int DecalLayoutId   = Shader.PropertyToID("_RiverRunDecalLayout");
    private static readonly int StoneDecalsId   = Shader.PropertyToID("_RiverRunStoneDecals");
    private static readonly int StoneDecalOpacitiesId =
        Shader.PropertyToID("_RiverRunStoneDecalOpacities");
    private static readonly int StoneDecalRotationsId =
        Shader.PropertyToID("_RiverRunStoneDecalRotations");

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
    /// These settings as a freestanding structure is built from them: a copy with the Freestanding
    /// Structures parts standing in the outpost's places, because a structure's body is built by
    /// the outpost's own mesh builders. Only read, so the copy is shallow. Null stays null.
    /// </summary>
    public static RiverRunShadingSettings ForStructure(RiverRunShadingSettings settings)
    {
        if (settings == null) return null;

        var copy = (RiverRunShadingSettings)settings.MemberwiseClone();
        copy.outpostOutsideWalls = settings.structureSides;
        copy.outpostWallTop      = settings.structureWallTop;
        copy.outpostInsideWalls  = settings.structureInsideWalls;
        copy.outpostFloor        = settings.structureTop;
        return copy;
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
        ApplyStoneTypes();

        Shader.SetGlobalColor(WaterColourId,   waterlineColour);
        Shader.SetGlobalFloat(WaterStrengthId, waterlineStrength);
        Shader.SetGlobalFloat(WaterExtentId,   waterlineExtent);

        ApplyDecals();
    }

    /// <summary>
    /// The decal sheet and the scatter it is stamped with.
    ///
    /// The sheet's HELD count is the only thing standing between an unpushed global and a stone
    /// covered in marks: there is no transparent default texture for a texture global to fall
    /// back on, so an unbound sampler reads as whatever Unity last had in that slot. A count of
    /// nothing is read in the shader before the sheet is ever sampled, so a preset carrying no
    /// sheet, or one whose sheet has been deleted from under it, draws nothing rather than
    /// something arbitrary. The sheet still goes over black in that case, so nothing is left
    /// holding on to a texture that is no longer wanted.
    /// </summary>
    private void ApplyDecals()
    {
        bool  ready = decalSheet != null && decalSheetHeld > 0
                   && decalSheetColumns > 0 && decalSheetRows > 0 && decalSheetCellPixels > 0;

        Shader.SetGlobalTexture(DecalSheetId, ready ? decalSheet : Texture2D.blackTexture);
        Shader.SetGlobalVector(DecalLayoutId, ready
            ? new Vector4(decalSheetColumns, decalSheetRows, decalSheetHeld, decalSheetCellPixels)
            : Vector4.zero);

        // The scatter, per stone type — filled alongside the rest of each stone's look in
        // ApplyStoneTypes, which has already run.
        Shader.SetGlobalVectorArray(StoneDecalsId,        StoneDecals);
        Shader.SetGlobalFloatArray(StoneDecalOpacitiesId, StoneDecalOpacities);
        Shader.SetGlobalFloatArray(StoneDecalRotationsId, StoneDecalRotations);
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
    // One slot per stone type the shader can hold, filled every push. The arrays are always sent
    // at full length — a global array's length is fixed by the first push it ever gets.
    private static readonly Vector4[] StoneColours = new Vector4[MaxStoneTypes];
    private static readonly Vector4[] StoneGrains  = new Vector4[MaxStoneTypes];
    private static readonly Vector4[] StoneSeams   = new Vector4[MaxStoneTypes];
    private static readonly Vector4[] StoneDecals  = new Vector4[MaxStoneTypes];
    private static readonly float[]   StoneDecalOpacities = new float[MaxStoneTypes];
    private static readonly float[]   StoneDecalRotations = new float[MaxStoneTypes];

    /// <summary>How many steps across one stone type's row of the gradient sheet.</summary>
    public const int GradientWidth = 128;

    // Every stone type's gradient on one sheet, a row each, rebuilt only when a gradient changes.
    // Made here rather than saved: it is nothing but what the gradients already say.
    private static Texture2D _gradientSheet;
    private static int       _gradientSheetHash;

    /// <summary>
    /// The stone types: a colour, a grain, a gradient direction and seams each, and the sheet the
    /// gradients are drawn from. A slot past the last stone type takes A's look, so a part still
    /// pointing at a stone type that was removed draws as A rather than as nothing.
    /// </summary>
    private void ApplyStoneTypes()
    {
        EnsureStoneTypes();

        for (int i = 0; i < MaxStoneTypes; i++)
        {
            var type = stoneTypes[i < stoneTypes.Count ? i : 0] ?? new StoneType();

            // Alpha is not a colour here. There is no property block behind a bare global, so a
            // shader reimport leaves every channel at zero, and the shader reads an alpha of
            // nothing as "nobody has pushed this yet" and gives the stone back to the run graph's
            // own colour rather than painting it black for the tick it takes to be pushed again.
            // So it goes over solid whatever was picked in the colour field.
            StoneColours[i] = Opaque(type.colour);

            // z is the gradient: 0 off, otherwise 1 + the direction. w the seam extent percent.
            StoneGrains[i] = new Vector4(
                type.grainSize, type.grainStrength,
                type.useGradient ? 1f + (int)type.gradientDirection : 0f, type.seamExtentPercent);

            // The seam colour, with its strength where the alpha would be.
            StoneSeams[i] = new Vector4(
                type.seamColour.r, type.seamColour.g, type.seamColour.b, type.seamStrength);

            // The decal scatter: x spacing, y smallest scale, z largest, w amount; opacity apart.
            StoneDecals[i] = new Vector4(
                type.decalSpacing, type.decalScaleMin, type.decalScaleMax, type.decalAmount);
            StoneDecalOpacities[i] = type.decalOpacity;

            // The share of decals given a quarter turn; 0 with the rotation off.
            StoneDecalRotations[i] = type.decalRotate ? type.decalRotateAmount : 0f;
        }

        Shader.SetGlobalVectorArray(StoneColoursId, StoneColours);
        Shader.SetGlobalVectorArray(StoneGrainsId,  StoneGrains);
        Shader.SetGlobalVectorArray(StoneSeamsId,   StoneSeams);
        Shader.SetGlobalTexture(StoneGradientsId, GradientSheet());

        // Which stone a river run's outer faces, rim and channel take — 1-based, like the kinds
        // baked into the meshes. Auto can only arrive here from a hand-edited preset; it reads as
        // the A, B and C the three always were.
        Shader.SetGlobalVector(RunStonesId, new Vector4(
            runOuterFaces   > StoneFaceKind.Auto ? (float)runOuterFaces   : 1f,
            runRim          > StoneFaceKind.Auto ? (float)runRim          : 2f,
            runInnerChannel > StoneFaceKind.Auto ? (float)runInnerChannel : 3f,
            0f));
    }

    /// <summary>
    /// The sheet of gradients: one row per stone type slot, sRGB like the colours picked for
    /// them, clamped so neither end of a gradient wraps round into the other.
    /// </summary>
    private Texture2D GradientSheet()
    {
        int hash = 17;
        for (int i = 0; i < stoneTypes.Count && i < MaxStoneTypes; i++)
        {
            var type = stoneTypes[i];
            if (type == null || !type.useGradient || type.gradient == null) continue;

            hash = hash * 31 + i;
            hash = hash * 31 + (int)type.gradient.mode;
            foreach (var key in type.gradient.colorKeys)
                hash = hash * 31 + key.color.GetHashCode() * 7 + key.time.GetHashCode();
        }

        if (_gradientSheet != null && hash == _gradientSheetHash) return _gradientSheet;

        if (_gradientSheet == null)
        {
            _gradientSheet = new Texture2D(GradientWidth, MaxStoneTypes, TextureFormat.RGBA32,
                                           false, false)
            {
                name       = "RiverRunStoneGradients",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideAndDontSave,
            };
        }

        var pixels = new Color32[GradientWidth * MaxStoneTypes];
        for (int row = 0; row < MaxStoneTypes; row++)
        {
            var type = row < stoneTypes.Count ? stoneTypes[row] : null;
            for (int x = 0; x < GradientWidth; x++)
            {
                Color c = type != null && type.gradient != null
                        ? type.gradient.Evaluate(x / (float)(GradientWidth - 1))
                        : Color.grey;
                c.a = 1f;
                pixels[row * GradientWidth + x] = c;
            }
        }

        _gradientSheet.SetPixels32(pixels);
        _gradientSheet.Apply(false, false);
        _gradientSheetHash = hash;
        return _gradientSheet;
    }

    private static Color Opaque(Color c) =>
        c == default ? c : new Color(c.r, c.g, c.b, 1f);

    /// <summary>Field-by-field copy, so the tuner can load a preset without holding on to it.</summary>
    public void CopyFrom(RiverRunShadingSettings other)
    {
        if (other == null) return;

        // The other side's list is brought up to A–D first, so a preset saved before stone types
        // copies over as the A, B and C its old three colours become.
        other.EnsureStoneTypes();
        if (stoneTypes == null) stoneTypes = new List<StoneType>();
        while (stoneTypes.Count > other.stoneTypes.Count) stoneTypes.RemoveAt(stoneTypes.Count - 1);
        for (int i = 0; i < other.stoneTypes.Count; i++)
        {
            if (i == stoneTypes.Count) stoneTypes.Add(new StoneType());
            if (stoneTypes[i] == null) stoneTypes[i] = new StoneType();
            stoneTypes[i].CopyFrom(other.stoneTypes[i]);
        }

        runOuterFaces   = other.runOuterFaces;
        runRim          = other.runRim;
        runInnerChannel = other.runInnerChannel;

        seamColour        = other.seamColour;
        seamStrength      = other.seamStrength;
        seamExtentPercent = other.seamExtentPercent;

        waterlineColour   = other.waterlineColour;
        waterlineStrength = other.waterlineStrength;
        waterlineExtent   = other.waterlineExtent;

        decalSheet           = other.decalSheet;
        decalSheetColumns    = other.decalSheetColumns;
        decalSheetRows       = other.decalSheetRows;
        decalSheetHeld       = other.decalSheetHeld;
        decalSheetCellPixels = other.decalSheetCellPixels;
        decalAmount          = other.decalAmount;
        decalOpacity         = other.decalOpacity;
        decalSpacing         = other.decalSpacing;
        decalScaleMin        = other.decalScaleMin;
        decalScaleMax        = other.decalScaleMax;

        outpostOutsideWalls = other.outpostOutsideWalls;
        outpostWallTop      = other.outpostWallTop;
        outpostInsideWalls  = other.outpostInsideWalls;
        outpostFloor        = other.outpostFloor;

        structureSides       = other.structureSides;
        structureWallTop     = other.structureWallTop;
        structureInsideWalls = other.structureInsideWalls;
        structureTop         = other.structureTop;

        walkwayTop       = other.walkwayTop;
        walkwayOuterWall  = other.walkwayOuterWall;
        walkwayStep       = other.walkwayStep;
        walkwayEnds       = other.walkwayEnds;

        towerOrb          = other.towerOrb;
        towerStem         = other.towerStem;
        towerBaseSides    = other.towerBaseSides;
        towerBaseTop      = other.towerBaseTop;

        wallSides         = other.wallSides;
        wallTop           = other.wallTop;
        wallEnds          = other.wallEnds;
        wallOpenings      = other.wallOpenings;
        wallColumnSides   = other.wallColumnSides;
        wallColumnTop     = other.wallColumnTop;
        wallLipTop        = other.wallLipTop;
        wallLipSides      = other.wallLipSides;
        wallLipUnderside  = other.wallLipUnderside;

        arenaInnerFace    = other.arenaInnerFace;
        arenaOuterFace    = other.arenaOuterFace;
        arenaTop          = other.arenaTop;

        archwayInside     = other.archwayInside;
        archwayOutside    = other.archwayOutside;
        archwayFaces      = other.archwayFaces;
        archwayFeet       = other.archwayFeet;

        doorSheet         = other.doorSheet;
        doorFrame         = other.doorFrame;
        doorPanel         = other.doorPanel;
        doorFlapRim       = other.doorFlapRim;
        doorDisc          = other.doorDisc;
        doorEdges         = other.doorEdges;
    }
}
