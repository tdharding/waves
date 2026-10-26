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

/// <summary>
/// Which Designation in the Run Shading Tuner a face was built as — a river run, an outpost, a
/// lollipop tower. RiverMeshBuilder bakes it into UV2.w (as 1 + this number) so the shader can
/// look up that Designation's Scale live. The numbers are baked, so they must not change; a new
/// Designation takes the next one. None is a face built before Designations were baked, or by
/// a builder nobody has told which it is, and is never scaled.
/// </summary>
public enum StoneDesignation
{
    None                   = 0,
    RiverRuns              = 1,
    Outposts               = 2,
    FreestandingStructures = 3,
    RiverWalkways          = 4,
    RiverBridges           = 5,
    RiverArchways          = 6,
    RimNodes               = 7,
    LollipopTowers         = 8,
    TurretTowers           = 9,
    SoulJuiceTanks        = 10,
    Walls                  = 11,
    Pipes                  = 12,
    Arena                  = 13,
    ArenaArchways          = 14,
    ArenaDoors             = 15,
    RimWalls               = 16,
    RiverPortholes         = 17,
    PoolDoorways           = 18,
    SoulFishBowls          = 19,
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

    // Which decal groups are stamped on this stone, by their ids. Each group brings its own
    // drawings and its own scatter; several ticked stack, the higher Sort Order on top.
    // Drawn by the tuner as a row of ticks, so hidden from the generic run of fields.
    [HideInInspector] public List<string> decalGroups = new List<string>();

    // How strongly this stone takes each of its ticked decal groups, 0 to 3, place for place with
    // decalGroups — on top of the group's own Opacity. Past 1 it fills in a decal's soft and faint
    // parts, up to wholly covering the stone. A place missing reads as 1, so a stone saved before
    // these existed looks exactly as it did. Drawn beside each tick by the tuner.
    [HideInInspector, FormerlySerializedAs("decalGroupOpacities")]
    public List<float> decalGroupIntensities = new List<float>();

    public const float MaxDecalGroupIntensity = 3f;

    // Which normal map decal groups bend this stone's light, by their ids. Drawn by the tuner as
    // a row of ticks under the decal groups' own.
    [HideInInspector] public List<string> normalDecalGroups = new List<string>();

    // The decals as each stone carried them before there were decal groups. No longer read by
    // the shader: EnsureStoneTypes turns a stone's into a group of its own, once, and ticks it.
    [HideInInspector] public float decalAmount       = 0f;
    [HideInInspector] public float decalOpacity      = 1f;
    [HideInInspector] public float decalSpacing      = 0.15f;
    [HideInInspector] public float decalScaleMin     = 0.04f;
    [HideInInspector] public float decalScaleMax     = 0.09f;
    [HideInInspector] public bool  decalRotate;
    [HideInInspector] public float decalRotateAmount = 0.5f;

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
        decalGroups       = other.decalGroups != null ? new List<string>(other.decalGroups)
                                                      : new List<string>();
        decalGroupIntensities = other.decalGroupIntensities != null ? new List<float>(other.decalGroupIntensities)
                                                                : new List<float>();
        normalDecalGroups = other.normalDecalGroups != null ? new List<string>(other.normalDecalGroups)
                                                            : new List<string>();

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

    /// <summary>This stone's Intensity for a decal group: 1 for one ticked before the setting existed.</summary>
    public float DecalGroupIntensity(string id)
    {
        int at = decalGroups != null ? decalGroups.IndexOf(id) : -1;
        return at >= 0 && decalGroupIntensities != null && at < decalGroupIntensities.Count
            ? decalGroupIntensities[at] : 1f;
    }

    /// <summary>Takes a decal group off this stone, and its intensity with it.</summary>
    public void UntickDecalGroup(string id)
    {
        int at = decalGroups != null ? decalGroups.IndexOf(id) : -1;
        if (at < 0) return;

        decalGroups.RemoveAt(at);
        if (decalGroupIntensities != null && at < decalGroupIntensities.Count) decalGroupIntensities.RemoveAt(at);
    }
}

/// <summary>
/// A decal group: a selection of the drawings in the decal folder and the scatter they are
/// stamped with. Stone types tick the groups they take rather than carrying decal numbers of their
/// own, so one group can lie over several stones — and, sharing one scatter, carry a decal across
/// the line where two of them meet unbroken.
/// </summary>
[System.Serializable]
public class DecalGroup
{
    [Tooltip("What the group is called in the tuner and on the stone types' ticks.")]
    public string name = "Decal Group";

    [Tooltip("Which groups this one draws in front of where they cross: higher is in front. " +
             "Groups with the same number stack in list order, the later on top.")]
    public int sortOrder;

    [Tooltip("How many of the scatter's places actually hold a decal, 0 to 1. It thins the " +
             "scatter out without moving what is left, so turning it down opens gaps rather " +
             "than pulling the decals closer together. 0 draws none.")]
    [Range(0f, 1f)] public float amount = 0.5f;

    [Tooltip("How much of the stone each decal hides, 0 to 1. A different question from Amount: " +
             "that is how MANY marks there are, this is how far each of them sinks into the " +
             "stone. 0 draws none.")]
    [Range(0f, 1f)] public float opacity = 1f;

    [Tooltip("How far apart the places a decal can stand are, in metres.")]
    [Min(0f)] public float spacing = 0.15f;

    [Tooltip("Tile the decals edge to edge: every place holds one, each exactly Spacing across, " +
             "so a tileable drawing repeats without gaps or seams. Amount and Scale are not " +
             "read. Tiles are square, so a drawing that is not square leaves gaps.")]
    public bool uniformSpacing;

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

    [Tooltip("Draw this group's decals over the lighting: the light, the Level Select Shadow and " +
             "the River Glow all leave them alone, so they show at their drawn colour on the dark " +
             "side as well as the lit one. The seams, waterline and grain still go over them. " +
             "Always in front of every group without this ticked, whatever the Sort Orders.")]
    public bool aboveLighting;

    [Tooltip("How the group's decals mix with what is under them: the stone and any group below " +
             "this one. Normal lays them on as drawn. Overlay and Soft Light only work under " +
             "the lighting — a group with Appear Above Lighting ticked draws them as Normal.")]
    public DecalBlendMode blendMode = DecalBlendMode.Normal;

    // The drawings ticked into this group, by the asset GUID of each — so renaming or redrawing
    // one keeps it in the group. Ticked in the tuner, against the folder's thumbnails.
    [HideInInspector] public List<string> drawings = new List<string>();

    // What the stone types tick this group by. Made once and never shown, so renaming the group
    // leaves every stone that takes it still taking it.
    [HideInInspector] public string id;

    // Moves the whole scatter, so two groups with the same spacing stand their decals in
    // different places rather than one exactly over the other. 0 is the scatter decals had
    // before there were groups, kept by the groups made from those stones so nothing moved.
    [HideInInspector] public int seed;

    // On a group made from a stone's old decals before the tuner has seen it: every drawing on
    // the sheet, as the stone had. The tuner ticks them all for real and clears this.
    [HideInInspector] public bool allDrawings;

    public void CopyFrom(DecalGroup other)
    {
        if (other == null) return;

        name         = other.name;
        sortOrder    = other.sortOrder;
        amount       = other.amount;
        opacity      = other.opacity;
        spacing      = other.spacing;
        uniformSpacing = other.uniformSpacing;
        blendTiles   = other.blendTiles;
        blendNoiseScale    = other.blendNoiseScale;
        blendNoiseSoftness = other.blendNoiseSoftness;
        scaleMin     = other.scaleMin;
        scaleMax     = other.scaleMax;
        rotate       = other.rotate;
        rotateAmount = other.rotateAmount;
        aboveLighting = other.aboveLighting;
        blendMode    = other.blendMode;
        drawings     = other.drawings != null ? new List<string>(other.drawings)
                                              : new List<string>();
        id           = other.id;
        seed         = other.seed;
        allDrawings  = other.allDrawings;
    }
}

/// <summary>
/// A normal map decal group: a selection of the normal maps in the NormalMaps folder, tiled edge
/// to edge across the stone. It paints no colour — it bends which way the stone faces, so the
/// light, the boat's light and the River Glow all fall across the drawn relief. Stone types tick
/// the groups they take, as they do the decal groups.
/// </summary>
[System.Serializable]
public class NormalDecalGroup
{
    [Tooltip("What the group is called in the tuner and on the stone types' ticks.")]
    public string name = "Normal Map Group";

    [Tooltip("How hard the normal map bends the light. 0 is flat stone, 1 the relief as drawn, " +
             "past 1 deeper than drawn.")]
    [Min(0f)] public float strength = 1f;

    [Tooltip("How wide one tile is, in metres. A colour decal group Tiled at the same Spacing " +
             "lines up with these tiles.")]
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

    // What the stone types tick this group by. Made once and never shown.
    [HideInInspector] public string id;

    public void CopyFrom(NormalDecalGroup other)
    {
        if (other == null) return;

        name               = other.name;
        strength           = other.strength;
        spacing            = other.spacing;
        blendTiles         = other.blendTiles;
        blendNoiseScale    = other.blendNoiseScale;
        blendNoiseSoftness = other.blendNoiseSoftness;
        seed               = other.seed;
        drawings           = other.drawings != null ? new List<string>(other.drawings)
                                                    : new List<string>();
        id                 = other.id;
    }
}

/// <summary>
/// How a decal group mixes with what is under it. The number is what the shader reads, so the
/// order here must match the RIVER_RUN_DECAL_BLEND_ defines in RiverRunShading.hlsl — add new
/// modes at the end. Overlay and Soft Light need the colour under them at the time, which the
/// groups above the lighting do not have, so those draw them as Normal.
/// </summary>
public enum DecalBlendMode { Normal, Multiply, Screen, Add, Subtract, Overlay, SoftLight }

/// <summary>
/// On a <see cref="StoneFaceKind"/> field: the dropdown lists only the stone types, no Auto —
/// for a choice that is always one of them, like which stone a river run's outer faces take.
/// </summary>
public class StoneTypeOnlyAttribute : PropertyAttribute { }

/// <summary>
/// On a piece part: the name the Run Shading Tuner shows it by — the name the Designer Overlay
/// puts on that bit of the selected piece, so a row can be matched to what is on screen. Only
/// the shown name: the field keeps its own, which presets are saved under. With
/// <see cref="Under"/>, the row is drawn tucked under the part above it.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Field)]
public class PartNameAttribute : System.Attribute
{
    public readonly string Name;
    public bool Under;
    public PartNameAttribute(string name) { Name = name; }
}

/// <summary>
/// How the Seam Mask takes the seams away. Fade dims the whole seam evenly under the drawing;
/// Erode eats the seam's faint outer edge first and its core last, so it breaks up rather than dims.
/// Fade And Erode does both: the edge is eaten and what is left of the seam is dimmed as well.
/// </summary>
public enum SeamMaskMode { Fade, Erode, FadeAndErode }

/// <summary>
/// How the Seam Mask is laid on. World tiles it across the world like the decals. Along Seam
/// lays the texture's middle on the seam, its width across to the seam's Extent either side, and
/// repeats it along the seam's length.
/// </summary>
public enum SeamMaskMapping { World, AlongSeam }

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

    /// <summary>
    /// Brings <paramref name="designations"/> up to one entry per triangle in
    /// <paramref name="tris"/>, the new entries carrying <paramref name="designation"/> — for a
    /// piece that carries another inside it, like an outpost with a lollipop tower on it.
    /// </summary>
    public static void Designate(List<StoneDesignation> designations, List<int> tris,
                                 StoneDesignation designation)
    {
        if (designations == null) return;
        while (designations.Count < tris.Count / 3) designations.Add(designation);
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
        SeedNewParts();

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

            if (type.decalGroups == null) type.decalGroups = new List<string>();
            if (type.decalGroupIntensities == null) type.decalGroupIntensities = new List<float>();
            if (type.normalDecalGroups == null) type.normalDecalGroups = new List<string>();
        }

        EnsureDecalGroups();

        if (normalDecalGroups == null)        normalDecalGroups        = new List<NormalDecalGroup>();
        if (normalDecalSheetDrawings == null) normalDecalSheetDrawings = new List<string>();
    }

    /// <summary>
    /// Brings the decal groups up. A preset saved before there were groups carried its decals on
    /// each stone, so every stone with decals on is given a group of its own with the same
    /// numbers, every drawing the sheet holds, and the scatter it already had — and is ticked
    /// into it, so nothing moves or changes look.
    /// </summary>
    private void EnsureDecalGroups()
    {
        if (decalGroups == null)        decalGroups        = new List<DecalGroup>();
        if (decalSheetDrawings == null) decalSheetDrawings = new List<string>();

        if (decalGroupsSet) return;
        decalGroupsSet = true;

        for (int i = 0; i < stoneTypes.Count && decalGroups.Count < MaxDecalGroups; i++)
        {
            var type = stoneTypes[i];
            if (type == null || type.decalAmount <= 0f) continue;

            var group = new DecalGroup
            {
                name         = "Stone " + StoneLetter(i),
                amount       = type.decalAmount,
                opacity      = type.decalOpacity,
                spacing      = type.decalSpacing,
                scaleMin     = type.decalScaleMin,
                scaleMax     = type.decalScaleMax,
                rotate       = type.decalRotate,
                rotateAmount = type.decalRotateAmount,
                id           = System.Guid.NewGuid().ToString("N"),
                seed         = 0,
                allDrawings  = true,
            };

            decalGroups.Add(group);
            type.decalGroups.Add(group.id);
        }
    }

    /// <summary>The group a stone type ticks by this id, or null.</summary>
    public DecalGroup FindDecalGroup(string id)
    {
        if (decalGroups == null || string.IsNullOrEmpty(id)) return null;
        return decalGroups.Find(g => g != null && g.id == id);
    }

    /// <summary>A new group, with a scatter of its own that no other group stands on.</summary>
    public DecalGroup NewDecalGroup(string name)
    {
        int seed = 1;
        while (decalGroups.Exists(g => g != null && g.seed == seed)) seed++;

        return new DecalGroup
        {
            name = name,
            id   = System.Guid.NewGuid().ToString("N"),
            seed = seed,
        };
    }

    /// <summary>The normal map group a stone type ticks by this id, or null.</summary>
    public NormalDecalGroup FindNormalDecalGroup(string id)
    {
        if (normalDecalGroups == null || string.IsNullOrEmpty(id)) return null;
        return normalDecalGroups.Find(g => g != null && g.id == id);
    }

    /// <summary>A new normal map group, with a seed no other normal map group has.</summary>
    public NormalDecalGroup NewNormalDecalGroup(string name)
    {
        int seed = 1;
        while (normalDecalGroups.Exists(g => g != null && g.seed == seed)) seed++;

        return new NormalDecalGroup
        {
            name = name,
            id   = System.Guid.NewGuid().ToString("N"),
            seed = seed,
        };
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
    // THE SEAM MASK
    //
    // One tiled texture over the whole preset, taken off every stone type's seams: where the
    // drawing is, the seam is not. Laid across the WORLD like the decals, on all three planes and
    // mixed by how much a surface faces each, so it crosses a corner unbroken.
    // ─────────────────────────────────────────────────────────────

    [Header("Seam Mask")]
    [Tooltip("Taken off the seams wherever it is drawn — its alpha, so a drawing on a clear " +
             "background takes the seam away under the strokes. Repeats across the world. " +
             "None leaves the seams whole.")]
    public Texture2D seamMask;

    [Tooltip("Metres the tiling covers.")]
    [Min(0f)] public float seamMaskScale = 1f;

    [Tooltip("How many times the texture repeats across the Scale. 0 turns the mask off.")]
    [Min(0f)] public float seamMaskTiles = 1f;

    [Tooltip("World tiles the texture across the world. Along Seam lays the texture's middle on " +
             "the seam with its width reaching out to the seam's Extent either side, and repeats " +
             "it along the seam by Scale and Tiles.")]
    public SeamMaskMapping seamMaskMapping = SeamMaskMapping.World;

    [Tooltip("Fade dims the seam evenly under the drawing. Erode eats the seam's faint outer edge " +
             "first and its core last, so it looks chipped rather than dimmed. Fade And Erode " +
             "does both at once.")]
    public SeamMaskMode seamMaskMode = SeamMaskMode.Fade;

    [Tooltip("Erode, and Fade And Erode: how soft the eaten edge is. Small is a crisp chipped edge, large fades " +
             "into it.")]
    [Range(0.01f, 3f)] public float seamMaskSoftness = 0.2f;

    [Header("Seams")]
    [Tooltip("Lays every stone type's seams on after the Level Select Shadow and the River Glow, " +
             "like a decal ticked Appear Above Lighting, so neither reaches them. Off, they sit " +
             "under both as they always have.")]
    public bool seamsAboveLighting;

    [Tooltip("How every stone type's seams mix with the stone under them. Normal lays them on in " +
             "their colour. The waterline is still laid over them as Normal. Overlay and Soft " +
             "Light only work with Seams Above Lighting off — on, they draw as Normal.")]
    public DecalBlendMode seamBlendMode = DecalBlendMode.Normal;

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

    // The asset GUID of the drawing in each cell of the sheet, in cell order — how a group's
    // ticked drawings are found on it. Written with the sheet.
    [HideInInspector] public List<string> decalSheetDrawings = new List<string>();

    /// <summary>The most decal groups a world can hold — the length of the shader's arrays.</summary>
    public const int MaxDecalGroups = 16;

    /// <summary>
    /// The most cells all the groups together can list — each group's ticked drawings, one after
    /// another, in one array the shader picks from.
    /// </summary>
    public const int MaxDecalPicks = 128;

    // The decal groups: each a selection of the drawings and the scatter they are stamped with.
    // The stone types tick which of them they take. Drawn by the tuner in the Decals section.
    [HideInInspector] public List<DecalGroup> decalGroups = new List<DecalGroup>();

    // Whether the groups have been made at all. Off on a preset saved before there were groups,
    // whose stones' own decals EnsureStoneTypes then turns into a group each.
    [HideInInspector] public bool decalGroupsSet;

    // ─────────────────────────────────────────────────────────────
    // THE NORMAL MAP DECALS
    //
    // Real normal maps, kept in RiverRunDecals/NormalMaps and gathered onto a sheet of their own —
    // a LINEAR one, because a normal map is directions rather than colour, and the colour sheet's
    // sRGB would bend every one of them. Always tiled, laid on the world's three planes and mixed
    // like the colour decals, and painting no colour: they bend the normal the light is worked out
    // from, and the River Glow is read against that same bent normal.
    // ─────────────────────────────────────────────────────────────

    [HideInInspector] public Texture2D normalDecalSheet;
    [HideInInspector] public int       normalDecalSheetColumns;
    [HideInInspector] public int       normalDecalSheetRows;
    [HideInInspector] public int       normalDecalSheetHeld;
    [HideInInspector] public int       normalDecalSheetCellPixels;

    // The asset GUID of the normal map in each cell of that sheet, in cell order.
    [HideInInspector] public List<string> normalDecalSheetDrawings = new List<string>();

    /// <summary>The most normal map groups a world can hold — the length of the shader's arrays.</summary>
    public const int MaxNormalDecalGroups = 8;

    /// <summary>The most cells all the normal map groups together can list.</summary>
    public const int MaxNormalDecalPicks = 64;

    // The normal map groups. Drawn by the tuner under the decal groups.
    [HideInInspector] public List<NormalDecalGroup> normalDecalGroups = new List<NormalDecalGroup>();

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
    // INTERIOR
    //
    // Only read by a material with Interior ticked — stone inside a level rather than out on the
    // map. There the stone is dark, lit only round the boat and the street lights, the same way
    // the spline walls and maze rocks are; the made-up light above is not used. Where the dark
    // starts is the boat light's own radius (BoatLightController), so the stone and the walls
    // beside it go dark at the same distance.
    // ─────────────────────────────────────────────────────────────

    [Header("Interior — materials with Interior ticked")]
    [Tooltip("What the stone falls to away from the boat and the lights — seams and waterline too.")]
    public Color interiorDarkColour = Color.black;

    [Tooltip("Metres past the boat light's radius over which the stone fades into the dark.")]
    [Min(0f)] public float interiorDarkSoftness = 1f;

    [Tooltip("How strongly the boat shapes the stone it lights — faces turned to it brighter, faces " +
             "turned away darker. 0 lights the stone flat.")]
    [Min(0f)] public float interiorBoatLight = 1f;

    [Tooltip("How much light the street lights add on the faces turned to them.")]
    [Min(0f)] public float interiorLampLight = 1f;

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
    [PartName("Outer Wall, Underside, End")]
    [StoneTypeOnly] public StoneFaceKind runOuterFaces = StoneFaceKind.A;

    [Tooltip("Multiplies the grain size, the decal spacing and scales, and the seam extent on the " +
             "outer faces, on top of the River Runs Scale above. 1 leaves them as the stone type says. Live.")]
    [PartName("Scale", Under = true)]
    [Min(0.01f)] public float runOuterFacesScale = 1f;

    [Tooltip("Multiplies the seam extent on the outer faces, after the stone type's Seam Extent % " +
             "and the Scales. 1 leaves it as it is, 0 takes their seams away. Live.")]
    [PartName("Seam Extent", Under = true)]
    [Min(0f)] public float runOuterFacesSeamExtent = 1f;

    [Tooltip("The flat lip laid round the top of a run or a pool, between the outer wall and the " +
             "channel.")]
    [PartName("Rim")]
    [StoneTypeOnly] public StoneFaceKind runRim = StoneFaceKind.B;

    [Tooltip("Multiplies the grain size, the decal spacing and scales, and the seam extent on the " +
             "rim, on top of the River Runs Scale above. 1 leaves them as the stone type says. Live.")]
    [PartName("Scale", Under = true)]
    [Min(0.01f)] public float runRimScale = 1f;

    [Tooltip("Multiplies the seam extent on the rim, after the stone type's Seam Extent % and the " +
             "Scales. 1 leaves it as it is, 0 takes its seams away. Live.")]
    [PartName("Seam Extent", Under = true)]
    [Min(0f)] public float runRimSeamExtent = 1f;

    [Tooltip("The inside of the channel — the faces the water runs down, above the surface and " +
             "below it.")]
    [PartName("Channel")]
    [StoneTypeOnly] public StoneFaceKind runInnerChannel = StoneFaceKind.C;

    [Tooltip("Multiplies the grain size, the decal spacing and scales, and the seam extent on the " +
             "channel, on top of the River Runs Scale above. 1 leaves them as the stone type says. Live.")]
    [PartName("Scale", Under = true)]
    [Min(0.01f)] public float runInnerChannelScale = 1f;

    [Tooltip("Multiplies the seam extent on the channel, after the stone type's Seam Extent % and " +
             "the Scales. 1 leaves it as it is, 0 takes its seams away. Live.")]
    [PartName("Seam Extent", Under = true)]
    [Min(0f)] public float runInnerChannelSeamExtent = 1f;

    [Header("Outposts — needs rebuild")]
    [Tooltip("The four walls round the outside of the block, from the top of its wall down.")]
    [PartName("Block Sides")]
    public StoneFaceKind outpostOutsideWalls = StoneFaceKind.Auto;

    [Tooltip("The top of the wall round the floor.")]
    [PartName("Wall Top")]
    public StoneFaceKind outpostWallTop = StoneFaceKind.Auto;

    [Tooltip("The inside faces of the wall, facing in across the floor.")]
    [PartName("Wall Inside")]
    public StoneFaceKind outpostInsideWalls = StoneFaceKind.Auto;

    [Tooltip("The floor inside the wall — or the whole flat top of an outpost with no wall.")]
    [PartName("Floor")]
    public StoneFaceKind outpostFloor = StoneFaceKind.Auto;

    [Tooltip("The round sides of the columns on the back corners, from the drop up.")]
    [PartName("Column Sides")]
    public StoneFaceKind outpostColumnSides = StoneFaceKind.Auto;

    [Tooltip("The flat tops of the columns on the back corners.")]
    [PartName("Column Top")]
    public StoneFaceKind outpostColumnTop = StoneFaceKind.Auto;

    [Tooltip("The two sides of the opening cut through the wall, facing each other across it.")]
    [PartName("Opening Sides")]
    public StoneFaceKind outpostOpeningSides = StoneFaceKind.Auto;

    [Tooltip("The strip of floor running out through the opening.")]
    [PartName("Opening Floor")]
    public StoneFaceKind outpostOpeningFloor = StoneFaceKind.Auto;

    [Tooltip("The top of the slope down to the walkway in front of the opening — its flat and its slope.")]
    [PartName("Slope Top")]
    public StoneFaceKind outpostSlopeTop = StoneFaceKind.Auto;

    [Tooltip("The two long sides of the slope: the river side and the side against the block.")]
    [PartName("Slope Sides")]
    public StoneFaceKind outpostSlopeSides = StoneFaceKind.Auto;

    [Tooltip("The square end of the slope, behind its flat.")]
    [PartName("Slope End")]
    public StoneFaceKind outpostSlopeEnd = StoneFaceKind.Auto;

    [Header("Freestanding Structures — needs rebuild")]
    [Tooltip("The sides of the block or round column, from its top down.")]
    [PartName("Block / Round Column Sides")]
    public StoneFaceKind structureSides = StoneFaceKind.Auto;

    [Tooltip("The top of the wall round an outpost top.")]
    [PartName("Wall Top")]
    public StoneFaceKind structureWallTop = StoneFaceKind.Auto;

    [Tooltip("The inside faces of an outpost top's wall, facing in across the floor.")]
    [PartName("Wall Inside")]
    public StoneFaceKind structureInsideWalls = StoneFaceKind.Auto;

    [Tooltip("The flat top of the structure — the floor inside the wall when it has an outpost top.")]
    [PartName("Block / Round Column Top, Floor")]
    public StoneFaceKind structureTop = StoneFaceKind.Auto;

    [Tooltip("The round sides of the structure's columns, from the drop up.")]
    [PartName("Column Sides")]
    public StoneFaceKind structureColumnSides = StoneFaceKind.Auto;

    [Tooltip("The flat tops of the structure's columns.")]
    [PartName("Column Top")]
    public StoneFaceKind structureColumnTop = StoneFaceKind.Auto;

    [Header("River Walkways — needs rebuild")]
    [Tooltip("The top of the walkway along the outside of the run.")]
    [PartName("River Walkway Top")]
    public StoneFaceKind walkwayTop = StoneFaceKind.Auto;

    [Tooltip("The walkway's outside face, away from the river, from its top down.")]
    [PartName("River Walkway Outer Wall")]
    public StoneFaceKind walkwayOuterWall = StoneFaceKind.Auto;

    [Tooltip("The step up from the rim to the walkway's top, when it stands above the rim.")]
    [PartName("River Walkway Step")]
    public StoneFaceKind walkwayStep = StoneFaceKind.Auto;

    [Tooltip("The square ends of each stretch of walkway.")]
    [PartName("River Walkway Ends")]
    public StoneFaceKind walkwayEnds = StoneFaceKind.Auto;

    [Header("River Bridges — needs rebuild")]
    [Tooltip("The top of the bridge — the deck and the ramps down either side.")]
    [PartName("Deck / Ramp Top")]
    public StoneFaceKind bridgeTop = StoneFaceKind.Auto;

    [Tooltip("Under the deck over the channel, and the faces standing up off the rim to it.")]
    [PartName("Deck Underside")]
    public StoneFaceKind bridgeUnderside = StoneFaceKind.Auto;

    [Tooltip("The upright faces at the feet of the ramps, where they reach past the run's outer edge.")]
    [PartName("Ramp Ends")]
    public StoneFaceKind bridgeRampEnds = StoneFaceKind.Auto;

    [Tooltip("The two flat sides of the bridge, facing up and down the river.")]
    [PartName("Deck / Ramp Sides")]
    public StoneFaceKind bridgeSides = StoneFaceKind.Auto;

    [Header("River Archways — needs rebuild")]
    [Tooltip("The flat top of the archway over the run.")]
    [PartName("Arch Top")]
    public StoneFaceKind riverArchTop = StoneFaceKind.Auto;

    [Tooltip("The outsides of the pillars, and the rounded corners up to the top.")]
    [PartName("Pillar / Arch Outside")]
    public StoneFaceKind riverArchOutside = StoneFaceKind.Auto;

    [Tooltip("The opening — the inside faces of the pillars and under the arch.")]
    [PartName("Pillar / Arch Opening")]
    public StoneFaceKind riverArchOpening = StoneFaceKind.Auto;

    [Tooltip("The two flat faces of the archway, facing up and down the river.")]
    [PartName("Pillar / Arch Faces")]
    public StoneFaceKind riverArchFaces = StoneFaceKind.Auto;

    [Header("Rim Walls — needs rebuild")]
    [Tooltip("The flat top of the wall standing on a river's or a pool's rim.")]
    public StoneFaceKind rimWallTop = StoneFaceKind.Auto;

    [Tooltip("The wall's face on the river's side, from the rim up.")]
    public StoneFaceKind rimWallRiverFace = StoneFaceKind.Auto;

    [Tooltip("The wall's face away from the river, from the rim up.")]
    public StoneFaceKind rimWallOuterFace = StoneFaceKind.Auto;

    [Tooltip("The square ends of each stretch of wall, and where a pool's wall opens for a river.")]
    public StoneFaceKind rimWallEnds = StoneFaceKind.Auto;

    [Header("Pool Doorways — needs rebuild")]
    [Tooltip("Inside the keyhole cut through a pool's rim wall where a doorway closes a river's " +
             "mouth. The wall round it is the Rim Walls'.")]
    [PartName("Door")]
    public StoneFaceKind poolDoorway = StoneFaceKind.Auto;

    [Header("River Portholes — needs rebuild")]
    [Tooltip("Inside the portholes bored across a river run under its channel, through the run " +
             "and any walkways beside it. Auto = as the River Runs' outer wall, live.")]
    [PartName("Porthole")]
    public StoneFaceKind riverPortholes = StoneFaceKind.Auto;

    [Header("Rim Nodes — needs rebuild")]
    [Tooltip("The flat top of the platform. Auto = as the River Runs, live.")]
    public StoneFaceKind rimNodeTop = StoneFaceKind.Auto;

    [Tooltip("The platform's wall above the rim and out past the run. Where it drops into the " +
             "channel it is the channel, as the River Runs say. Auto = as the River Runs, live.")]
    public StoneFaceKind rimNodeSides = StoneFaceKind.Auto;

    [Tooltip("The sill, header and sides running back from the wall to a screen. Auto = as the " +
             "River Runs, live.")]
    public StoneFaceKind rimNodeScreenEdges = StoneFaceKind.Auto;

    [Tooltip("The flat top of the plinth standing on the platform. Auto = as the River Runs, live.")]
    [PartName("Plinth Top")]
    public StoneFaceKind rimNodePlinthTop = StoneFaceKind.Auto;

    [Tooltip("The plinth's round side. Auto = as the River Runs, live.")]
    [PartName("Plinth Sides")]
    public StoneFaceKind rimNodePlinthSides = StoneFaceKind.Auto;

    [Header("Lollipop Towers — needs rebuild")]
    [Tooltip("The orb on top of every lollipop tower, on outposts, pools or anywhere else.")]
    [PartName("Orb")]
    public StoneFaceKind towerOrb = StoneFaceKind.Auto;

    [Tooltip("The stem of every lollipop tower.")]
    [PartName("Stem")]
    public StoneFaceKind towerStem = StoneFaceKind.Auto;

    [Tooltip("The round side of the base every lollipop tower stands on.")]
    [FormerlySerializedAs("towerBase")]
    [PartName("Base Sides")]
    public StoneFaceKind towerBaseSides = StoneFaceKind.Auto;

    [Tooltip("The flat top of the base, round the foot of the stem.")]
    [FormerlySerializedAs("towerBase")]
    [PartName("Base Top")]
    public StoneFaceKind towerBaseTop = StoneFaceKind.Auto;

    [Tooltip("The lower ramp flaring out under the base.")]
    [PartName("Lower Ramp")]
    public StoneFaceKind towerLowerRamp = StoneFaceKind.Auto;

    [Tooltip("The upper ramp sloping in from the base to the second base.")]
    [PartName("Upper Ramp")]
    public StoneFaceKind towerUpperRamp = StoneFaceKind.Auto;

    [Tooltip("The flat top of the lower ramp — only there when nothing stands on it (no base).")]
    [PartName("Lower Ramp Top")]
    public StoneFaceKind towerLowerRampTop = StoneFaceKind.Auto;

    [Tooltip("The flat top of the upper ramp — only there when there is no second base on it.")]
    [PartName("Upper Ramp Top")]
    public StoneFaceKind towerUpperRampTop = StoneFaceKind.Auto;

    [Tooltip("The round side of the second base, on top of the upper ramp.")]
    [PartName("Base 2 Sides")]
    public StoneFaceKind towerSecondBaseSides = StoneFaceKind.Auto;

    [Tooltip("The flat top of the second base, round the foot of the stem.")]
    [PartName("Base 2 Top")]
    public StoneFaceKind towerSecondBaseTop = StoneFaceKind.Auto;

    [Tooltip("The inside of the orb, seen through its hole — only there with Destruction on.")]
    [PartName("Orb Inside")]
    public StoneFaceKind towerOrbInside = StoneFaceKind.D;

    [Tooltip("The band round the orb's hole, the shell's thickness — only there with Destruction on.")]
    [PartName("Orb Hole Edge")]
    public StoneFaceKind towerOrbHoleEdge = StoneFaceKind.D;

    [Tooltip("The orb of the small lollipop standing inside a broken orb — only there with Inner Lollipop on.")]
    [PartName("Inner Lollipop Orb")]
    public StoneFaceKind towerInnerLollipopOrb = StoneFaceKind.D;

    [Tooltip("The stem of the small lollipop standing inside a broken orb — only there with Inner Lollipop on.")]
    [PartName("Inner Lollipop Stem")]
    public StoneFaceKind towerInnerLollipopStem = StoneFaceKind.D;

    [Tooltip("Every tier the small lollipop inside a broken orb stands on — its ramps and bases, sides and " +
             "tops. Only there with Inner Lollipop on.")]
    [PartName("Inner Lollipop Base")]
    public StoneFaceKind towerInnerLollipopBase = StoneFaceKind.D;

    [Header("Turret Towers — needs rebuild")]
    [Tooltip("The round sides of the base tiers every turret stands on.")]
    [PartName("Base / Base 2 Sides")]
    public StoneFaceKind turretBaseSides = StoneFaceKind.Auto;

    [Tooltip("The flat tops of the base tiers.")]
    [PartName("Base / Base 2 Top")]
    public StoneFaceKind turretBaseTop = StoneFaceKind.Auto;

    [Tooltip("The lower ramp flaring out under the base.")]
    [PartName("Lower Ramp")]
    public StoneFaceKind turretLowerRamp = StoneFaceKind.Auto;

    [Tooltip("The upper ramp sloping in from the base to the second base.")]
    [PartName("Upper Ramp")]
    public StoneFaceKind turretUpperRamp = StoneFaceKind.Auto;

    [Tooltip("The stem up to the head.")]
    [PartName("Stem")]
    public StoneFaceKind turretStem = StoneFaceKind.Auto;

    [Tooltip("The round head, which turns.")]
    [PartName("Head")]
    public StoneFaceKind turretHead = StoneFaceKind.Auto;

    [Tooltip("The nose out of the head, which tilts — its outside, its end and its bore.")]
    [PartName("Nose")]
    public StoneFaceKind turretNose = StoneFaceKind.Auto;

    [Header("Soul Juice Tanks — needs rebuild")]
    [Tooltip("The round sides of the base tiers.")]
    [PartName("Base / Base 2 Sides")]
    public StoneFaceKind poolTowerBaseSides = StoneFaceKind.Auto;

    [Tooltip("The flat tops of the base tiers.")]
    [PartName("Base / Base 2 Top")]
    public StoneFaceKind poolTowerBaseTop = StoneFaceKind.Auto;

    [Tooltip("The lower ramp flaring out under the base.")]
    [PartName("Lower Ramp")]
    public StoneFaceKind poolTowerLowerRamp = StoneFaceKind.Auto;

    [Tooltip("The upper ramp sloping in from the base to the second base.")]
    [PartName("Upper Ramp")]
    public StoneFaceKind poolTowerRamp = StoneFaceKind.Auto;

    [Tooltip("The stem up to the orb.")]
    [PartName("Stem")]
    public StoneFaceKind poolTowerColumn = StoneFaceKind.Auto;

    [Tooltip("The orb on top of the stem.")]
    [PartName("Orb")]
    public StoneFaceKind poolTowerOrb = StoneFaceKind.Auto;

    [Tooltip("The round hub on top of the orb, the intake comes up out of.")]
    [PartName("Top Hub")]
    public StoneFaceKind poolTowerTopHub = StoneFaceKind.Auto;

    [Tooltip("The round hub on the side of the orb, the tap comes out of.")]
    [PartName("Side Hub")]
    public StoneFaceKind poolTowerSideHub = StoneFaceKind.Auto;

    [Tooltip("The tap out of the side hub — its outside, its mouth and its bore.")]
    [PartName("Tap")]
    public StoneFaceKind poolTowerTap = StoneFaceKind.Auto;

    [Tooltip("The intake pipe from the top hub up to the hub on a pipe.")]
    [PartName("Intake")]
    public StoneFaceKind poolTowerIntake = StoneFaceKind.Auto;

    // The bowl the tank had until 2026-10-26. Kept, hidden, only to seed the Soul Fish Bowls parts.
    [HideInInspector] public StoneFaceKind poolTowerBowlOutside = StoneFaceKind.Auto;
    [HideInInspector] public StoneFaceKind poolTowerBowlRim = StoneFaceKind.Auto;
    [HideInInspector] public StoneFaceKind poolTowerBowlInside = StoneFaceKind.Auto;

    [Header("Soul Fish Bowls — needs rebuild")]
    [Tooltip("The round sides of the base tiers.")]
    [PartName("Base / Base 2 Sides")]
    public StoneFaceKind fishBowlBaseSides = StoneFaceKind.Auto;

    [Tooltip("The flat tops of the base tiers.")]
    [PartName("Base / Base 2 Top")]
    public StoneFaceKind fishBowlBaseTop = StoneFaceKind.Auto;

    [Tooltip("The ramp sloping in from the base to the second base.")]
    [PartName("Ramp")]
    public StoneFaceKind fishBowlRamp = StoneFaceKind.Auto;

    [Tooltip("The column up to the bowl.")]
    [PartName("Column")]
    public StoneFaceKind fishBowlColumn = StoneFaceKind.Auto;

    [Tooltip("The outside of the bowl — its curved underside and its band.")]
    [PartName("Underside / Band")]
    public StoneFaceKind fishBowlOutside = StoneFaceKind.Auto;

    [Tooltip("The flat rim round the top of the bowl.")]
    [PartName("Rim")]
    public StoneFaceKind fishBowlRim = StoneFaceKind.Auto;

    [Tooltip("The inside of the bowl — the basin the water sits in, and its hole.")]
    [PartName("Basin / Hole")]
    public StoneFaceKind fishBowlInside = StoneFaceKind.Auto;

    [Header("Walls — needs rebuild")]
    [Tooltip("Both long faces of every wall, from its top down.")]
    [PartName("Wall Sides")]
    public StoneFaceKind wallSides = StoneFaceKind.Auto;

    [Tooltip("The top of the wall.")]
    [PartName("Wall Top")]
    public StoneFaceKind wallTop = StoneFaceKind.Auto;

    [Tooltip("The flat ends where a wall stops, or breaks off at a gap.")]
    [PartName("Wall Ends")]
    public StoneFaceKind wallEnds = StoneFaceKind.Auto;

    [Tooltip("Inside the archways and portholes cut through the wall — their sides, roofs and floors.")]
    [PartName("Archway")]
    public StoneFaceKind wallOpenings = StoneFaceKind.Auto;

    [Tooltip("The round side of a column standing on a wall node.")]
    [PartName("Column Sides")]
    public StoneFaceKind wallColumnSides = StoneFaceKind.Auto;

    [Tooltip("The flat top of a column standing on a wall node.")]
    [PartName("Column Top")]
    public StoneFaceKind wallColumnTop = StoneFaceKind.Auto;

    [Tooltip("The top of a wall's lip. Auto = the same as the wall's top.")]
    [PartName("Lip Top")]
    public StoneFaceKind wallLipTop = StoneFaceKind.Auto;

    [Tooltip("Both long faces of a wall's lip, and its flat ends. Auto = the same as the wall's sides.")]
    [PartName("Lip Sides")]
    public StoneFaceKind wallLipSides = StoneFaceKind.Auto;

    [Tooltip("Under a wall's lip, where it overhangs the wall. Auto = the same as the wall's sides.")]
    [PartName("Lip Underside")]
    public StoneFaceKind wallLipUnderside = StoneFaceKind.Auto;

    [Tooltip("Inside the portholes bored through the wall — round their circle. Auto = the same as " +
             "the openings.")]
    [PartName("Porthole")]
    public StoneFaceKind wallPortholes = StoneFaceKind.Auto;

    [Tooltip("The broken faces round a Destruction hole, down from the wall's top and through its " +
             "lip. Auto = the same as the openings.")]
    [PartName("Damage Edge")]
    public StoneFaceKind wallDamageEdge = StoneFaceKind.Auto;

    [Tooltip("The top of the lip ring round a column. Auto = the same as the lip's top.")]
    [PartName("Column Ring Top")]
    public StoneFaceKind wallColumnLipTop = StoneFaceKind.Auto;

    [Tooltip("The outer side of the lip ring round a column. Auto = the same as the lip's sides.")]
    [PartName("Column Ring Sides")]
    public StoneFaceKind wallColumnLipSides = StoneFaceKind.Auto;

    [Tooltip("Under the lip ring round a column. Auto = the same as the lip's underside.")]
    [PartName("Column Ring Underside")]
    public StoneFaceKind wallColumnLipUnderside = StoneFaceKind.Auto;

    [Tooltip("The round sleeve carrying a column's lip ring down to the wall's base. Auto = the same " +
             "as the lip ring's outer side.")]
    [PartName("Sleeve")]
    public StoneFaceKind wallColumnSleeve = StoneFaceKind.Auto;

    [Header("Pipes — needs rebuild")]
    [Tooltip("The outside of the pipe, and its flat ends.")]
    [PartName("Pipe Outside")]
    public StoneFaceKind pipeOutside = StoneFaceKind.Auto;

    [Tooltip("The inside of a hollow pipe.")]
    [PartName("Pipe Inside")]
    public StoneFaceKind pipeInside = StoneFaceKind.Auto;

    [Tooltip("The rings round the pipe at each support.")]
    [PartName("Ring")]
    public StoneFaceKind pipeRings = StoneFaceKind.Auto;

    [Tooltip("The support stems the pipe stands on.")]
    [PartName("Support")]
    public StoneFaceKind pipeStems = StoneFaceKind.Auto;

    [Tooltip("The sleeve round the pipe at a hub.")]
    [PartName("Hub Sleeve")]
    public StoneFaceKind pipeHubSleeve = StoneFaceKind.Auto;

    [Tooltip("The ring standing out of a hub's sleeve at a right angle, the intake goes into.")]
    [PartName("Hub Ring")]
    public StoneFaceKind pipeHubRing = StoneFaceKind.Auto;

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
    // DESIGNATION SCALES
    //
    // One Scale per Designation, multiplying every part of that piece's grain size, decal
    // spacing and scales, and seam extent — so a tower can carry a coarser stone than the river
    // it stands by while both use the same stone type. Indexed by StoneDesignation; a slot that
    // is missing or not above zero is 1. Live: each face carries which Designation it was built
    // as, and the shader looks its Scale up, so only a piece built before that needs a rebuild.
    // Drawn by the tuner under the Designation dropdown rather than here.
    // ─────────────────────────────────────────────────────────────

    /// <summary>The most Designations the shader's Scale array holds.</summary>
    public const int MaxDesignations = 32;

    [HideInInspector] public List<float> designationScales = new List<float>();

    /// <summary>This Designation's Scale — 1 when it has never been set.</summary>
    public float DesignationScale(StoneDesignation designation)
    {
        int i = (int)designation;
        return designationScales != null && i > 0 && i < designationScales.Count && designationScales[i] > 0f
             ? designationScales[i] : 1f;
    }

    // Which Designation the outpost builders and the lollipop tower builder are building as.
    // Not saved: only the For* copies below change them, because a freestanding structure is
    // built by the outpost's builders and a turret or pool tower body by the lollipop tower's.
    [System.NonSerialized] public StoneDesignation outpostAs = StoneDesignation.Outposts;
    [System.NonSerialized] public StoneDesignation towerAs   = StoneDesignation.LollipopTowers;

    /// <summary>What an outpost builder handed <paramref name="settings"/> is building as.</summary>
    public static StoneDesignation OutpostAs(RiverRunShadingSettings settings) =>
        settings != null ? settings.outpostAs : StoneDesignation.Outposts;

    /// <summary>What a lollipop tower builder handed <paramref name="settings"/> is building as.</summary>
    public static StoneDesignation TowerAs(RiverRunShadingSettings settings) =>
        settings != null ? settings.towerAs : StoneDesignation.LollipopTowers;

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

    // Whether the parts added 2026-10-15 have been given a choice yet. Off on a preset saved
    // before them, which SeedNewParts then hands the choices those parts were built from until
    // then — so adding them changes nothing until they are tuned on their own.
    [HideInInspector] public bool partsSeeded;

    // The same for the lollipop tower's ramp tops and second base, added 2026-10-17 — until then
    // every tier's top took Base Top and the second base's side took Base Sides.
    [HideInInspector] public bool towerTiersSeeded;

    // The same for the Soul Fish Bowls parts, added 2026-10-26 — until then the bowl was the soul
    // juice tank's, and took the Soul Juice Tanks parts.
    [HideInInspector] public bool fishBowlSeeded;

    /// <summary>
    /// Gives the parts that used to borrow another part's stone that part's current choice, once.
    /// Rim nodes, pipes and the pool tower's bowl had no choice before, so they stay on Auto.
    /// Called by EnsureStoneTypes and ForBuild, before anything reads them.
    /// </summary>
    public void SeedNewParts()
    {
        if (!towerTiersSeeded)
        {
            towerTiersSeeded = true;
            towerLowerRampTop    = towerBaseTop;
            towerUpperRampTop    = towerBaseTop;
            towerSecondBaseSides = towerBaseSides;
            towerSecondBaseTop   = towerBaseTop;
        }

        if (!fishBowlSeeded)
        {
            fishBowlSeeded = true;
            fishBowlBaseSides = poolTowerBaseSides;
            fishBowlBaseTop   = poolTowerBaseTop;
            fishBowlRamp      = poolTowerRamp;
            fishBowlColumn    = poolTowerColumn;
            fishBowlOutside   = poolTowerBowlOutside;
            fishBowlRim       = poolTowerBowlRim;
            fishBowlInside    = poolTowerBowlInside;
        }

        if (partsSeeded) return;
        partsSeeded = true;

        outpostColumnSides  = outpostOutsideWalls;
        outpostColumnTop    = outpostWallTop;
        outpostOpeningSides = outpostInsideWalls;
        outpostOpeningFloor = outpostFloor;
        outpostSlopeTop     = walkwayTop;
        outpostSlopeSides   = walkwayOuterWall;
        outpostSlopeEnd     = walkwayEnds;

        structureColumnSides = structureSides;
        structureColumnTop   = structureWallTop;

        bridgeTop       = walkwayTop;
        bridgeUnderside = walkwayOuterWall;
        bridgeRampEnds  = walkwayOuterWall;
        bridgeSides     = walkwayEnds;

        riverArchTop     = walkwayTop;
        riverArchOutside = walkwayOuterWall;
        riverArchOpening = walkwayOuterWall;
        riverArchFaces   = walkwayEnds;

        towerLowerRamp = towerBaseSides;
        towerUpperRamp = towerBaseSides;

        turretBaseSides = towerBaseSides;
        turretBaseTop   = towerBaseTop;
        turretLowerRamp = towerBaseSides;
        turretUpperRamp = towerBaseSides;
        turretStem      = towerStem;
        turretHead      = towerOrb;
        turretNose      = towerStem;

        poolTowerBaseSides = towerBaseSides;
        poolTowerBaseTop   = towerBaseTop;
        poolTowerRamp      = towerBaseSides;
        poolTowerColumn    = towerStem;

        wallPortholes          = wallOpenings;
        wallColumnLipTop       = wallLipTop;
        wallColumnLipSides     = wallLipSides;
        wallColumnLipUnderside = wallLipUnderside;
    }

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
    private static readonly int RunScalesId       = Shader.PropertyToID("_RiverRunRunScales");
    private static readonly int RunSeamExtentsId  = Shader.PropertyToID("_RiverRunRunSeamExtents");
    private static readonly int StoneSeamsId      = Shader.PropertyToID("_RiverRunStoneSeams");
    private static readonly int WaterColourId     = Shader.PropertyToID("_RiverRunWaterlineColour");
    private static readonly int WaterStrengthId   = Shader.PropertyToID("_RiverRunWaterlineStrength");
    private static readonly int WaterExtentId     = Shader.PropertyToID("_RiverRunWaterlineExtent");
    private static readonly int WaterDepthId      = Shader.PropertyToID("_RiverRunWaterDepth");
    private static readonly int SeamMaskId        = Shader.PropertyToID("_RiverRunSeamMask");
    private static readonly int SeamMaskRepeatsId = Shader.PropertyToID("_RiverRunSeamMaskRepeats");
    private static readonly int SeamMaskModeId    = Shader.PropertyToID("_RiverRunSeamMaskMode");
    private static readonly int SeamsAboveLightId = Shader.PropertyToID("_RiverRunSeamsAboveLight");
    private static readonly int SeamBlendId       = Shader.PropertyToID("_RiverRunSeamBlend");
    private static readonly int InteriorDarkId    = Shader.PropertyToID("_RiverRunInteriorDark");
    private static readonly int InteriorId        = Shader.PropertyToID("_RiverRunInterior");

    private static readonly int DecalSheetId    = Shader.PropertyToID("_RiverRunDecalSheet");
    private static readonly int DecalLayoutId   = Shader.PropertyToID("_RiverRunDecalLayout");
    private static readonly int DecalGroupScatterId = Shader.PropertyToID("_RiverRunDecalGroupScatter");
    private static readonly int DecalGroupLookId    = Shader.PropertyToID("_RiverRunDecalGroupLook");
    private static readonly int DecalPicksId        = Shader.PropertyToID("_RiverRunDecalPicks");
    private static readonly int StoneDecalGroupsId  = Shader.PropertyToID("_RiverRunStoneDecalGroups");
    private static readonly int StoneDecalIntensitiesId = Shader.PropertyToID("_RiverRunStoneDecalIntensities");
    private static readonly int DecalGroupSeedsId   = Shader.PropertyToID("_RiverRunDecalGroupSeeds");
    private static readonly int DecalGroupTiledId   = Shader.PropertyToID("_RiverRunDecalGroupTiled");
    private static readonly int DecalGroupTileBlendId = Shader.PropertyToID("_RiverRunDecalGroupTileBlend");
    private static readonly int DecalGroupAboveLightId = Shader.PropertyToID("_RiverRunDecalGroupAboveLight");
    private static readonly int DecalGroupBlendId   = Shader.PropertyToID("_RiverRunDecalGroupBlend");

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
    public static RiverRunShadingSettings ForBuild(RiverRunShadingSettings settings)
    {
        var chosen = Live ?? settings;
        chosen?.SeedNewParts();
        return chosen;
    }

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
        copy.outpostColumnSides  = settings.structureColumnSides;
        copy.outpostColumnTop    = settings.structureColumnTop;
        copy.outpostAs           = StoneDesignation.FreestandingStructures;
        return copy;
    }

    /// <summary>
    /// These settings as a turret's body is built from them: a copy with the Turret Towers parts
    /// standing in the lollipop tower's places, because the body is a lollipop tower's own tiers
    /// and stem. Only read, so the copy is shallow. Null stays null.
    /// </summary>
    public static RiverRunShadingSettings ForTurret(RiverRunShadingSettings settings)
    {
        if (settings == null) return null;

        var copy = (RiverRunShadingSettings)settings.MemberwiseClone();
        copy.towerBaseSides = settings.turretBaseSides;
        copy.towerBaseTop   = settings.turretBaseTop;
        copy.towerLowerRamp = settings.turretLowerRamp;
        copy.towerUpperRamp = settings.turretUpperRamp;
        copy.towerStem      = settings.turretStem;
        copy.towerOrb       = settings.turretHead;

        // The turret has no parts of its own for these yet, so they take what they always did.
        copy.towerLowerRampTop    = settings.turretBaseTop;
        copy.towerUpperRampTop    = settings.turretBaseTop;
        copy.towerSecondBaseSides = settings.turretBaseSides;
        copy.towerSecondBaseTop   = settings.turretBaseTop;
        copy.towerAs              = StoneDesignation.TurretTowers;
        return copy;
    }

    /// <summary>
    /// These settings as the small lollipop inside a broken orb is built from them: a copy with
    /// the Inner Lollipop parts standing in the lollipop tower's places — every tier as Inner
    /// Lollipop Base. Only read, so the copy is shallow. Null stays null.
    /// </summary>
    public static RiverRunShadingSettings ForInnerLollipop(RiverRunShadingSettings settings)
    {
        if (settings == null) return null;

        var copy = (RiverRunShadingSettings)settings.MemberwiseClone();
        copy.towerOrb             = settings.towerInnerLollipopOrb;
        copy.towerStem            = settings.towerInnerLollipopStem;
        copy.towerBaseSides       = settings.towerInnerLollipopBase;
        copy.towerBaseTop         = settings.towerInnerLollipopBase;
        copy.towerLowerRamp       = settings.towerInnerLollipopBase;
        copy.towerUpperRamp       = settings.towerInnerLollipopBase;
        copy.towerLowerRampTop    = settings.towerInnerLollipopBase;
        copy.towerUpperRampTop    = settings.towerInnerLollipopBase;
        copy.towerSecondBaseSides = settings.towerInnerLollipopBase;
        copy.towerSecondBaseTop   = settings.towerInnerLollipopBase;
        return copy;
    }

    /// <summary>
    /// These settings as a soul juice tank's lollipop is built from them: a copy with the Soul
    /// Juice Tanks parts standing in the lollipop tower's places. Only read, so the copy is
    /// shallow. Null stays null.
    /// </summary>
    public static RiverRunShadingSettings ForPoolTower(RiverRunShadingSettings settings)
    {
        if (settings == null) return null;

        var copy = (RiverRunShadingSettings)settings.MemberwiseClone();
        copy.towerBaseSides = settings.poolTowerBaseSides;
        copy.towerBaseTop   = settings.poolTowerBaseTop;
        copy.towerLowerRamp = settings.poolTowerLowerRamp;
        copy.towerUpperRamp = settings.poolTowerRamp;
        copy.towerStem      = settings.poolTowerColumn;
        copy.towerOrb       = settings.poolTowerOrb;

        // The pool tower has no parts of its own for these yet, so they take what they always did.
        copy.towerLowerRampTop    = settings.poolTowerBaseTop;
        copy.towerUpperRampTop    = settings.poolTowerBaseTop;
        copy.towerSecondBaseSides = settings.poolTowerBaseSides;
        copy.towerSecondBaseTop   = settings.poolTowerBaseTop;
        copy.towerAs              = StoneDesignation.SoulJuiceTanks;
        return copy;
    }

    /// <summary>
    /// These settings as a soul fish bowl's tiers are built from them: a copy with the Soul Fish
    /// Bowls parts standing in the lollipop tower's places. The tiers have no lower ramp, stem or
    /// orb. Only read, so the copy is shallow. Null stays null.
    /// </summary>
    public static RiverRunShadingSettings ForFishBowl(RiverRunShadingSettings settings)
    {
        if (settings == null) return null;

        var copy = (RiverRunShadingSettings)settings.MemberwiseClone();
        copy.towerBaseSides       = settings.fishBowlBaseSides;
        copy.towerBaseTop         = settings.fishBowlBaseTop;
        copy.towerUpperRamp       = settings.fishBowlRamp;
        copy.towerLowerRampTop    = settings.fishBowlBaseTop;
        copy.towerUpperRampTop    = settings.fishBowlBaseTop;
        copy.towerSecondBaseSides = settings.fishBowlBaseSides;
        copy.towerSecondBaseTop   = settings.fishBowlBaseTop;
        copy.towerAs              = StoneDesignation.SoulFishBowls;
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

        // Repeats per metre, and the texture. The repeats are what the shader checks before it
        // reads the texture at all, so with no mask they go out as nothing and the texture
        // global, which has no "none" of its own, is never sampled.
        bool maskOn = seamMask != null && seamMaskScale > 0f && seamMaskTiles > 0f;
        Shader.SetGlobalTexture(SeamMaskId, maskOn ? seamMask : Texture2D.blackTexture);
        Shader.SetGlobalFloat(SeamMaskRepeatsId, maskOn ? seamMaskTiles / seamMaskScale : 0f);

        // x the erode's softness, 0 with no erode; y 1 to fade as well; z 1 to lay it along the seam.
        bool erode = seamMaskMode != SeamMaskMode.Fade;
        bool fade  = seamMaskMode != SeamMaskMode.Erode;
        Shader.SetGlobalVector(SeamMaskModeId,
            new Vector4(erode ? Mathf.Max(seamMaskSoftness, 0.01f) : 0f, fade ? 1f : 0f,
                        seamMaskMapping == SeamMaskMapping.AlongSeam ? 1f : 0f, 0f));

        Shader.SetGlobalFloat(SeamsAboveLightId, seamsAboveLighting ? 1f : 0f);
        Shader.SetGlobalFloat(SeamBlendId, (float)seamBlendMode);

        Shader.SetGlobalColor(InteriorDarkId, interiorDarkColour);
        Shader.SetGlobalVector(InteriorId,
            new Vector4(interiorDarkSoftness, interiorBoatLight, interiorLampLight, 0f));

        // Sent at full length every push, like the stone types' arrays. Slot 0 is None: a face
        // built before Designations were baked, which stays at 1.
        for (int i = 0; i < MaxDesignations; i++) DesignationScales[i] = DesignationScale((StoneDesignation)i);
        Shader.SetGlobalFloatArray(DesignationScalesId, DesignationScales);

        ApplyDecals();
        ApplyNormalDecals();
    }

    private static readonly int     DesignationScalesId = Shader.PropertyToID("_RiverRunDesignationScales");
    private static readonly float[] DesignationScales   = new float[MaxDesignations];

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

        // The shader lays each slot over the ones before it, so the groups go into the slots by
        // Sort Order, lowest first — ties in list order. Slot, not list place, from here on.
        DecalDrawOrder.Clear();
        for (int g = 0; g < decalGroups.Count && g < MaxDecalGroups; g++) DecalDrawOrder.Add(g);
        DecalDrawOrder.Sort((a, b) =>
        {
            int sa = decalGroups[a] != null ? decalGroups[a].sortOrder : 0;
            int sb = decalGroups[b] != null ? decalGroups[b].sortOrder : 0;
            return sa != sb ? sa.CompareTo(sb) : a.CompareTo(b);
        });

        // Every group: its scatter, its look, and the cells of the sheet it picks among — its
        // ticked drawings, one run of the picks list each.
        int picks = 0;
        for (int g = 0; g < MaxDecalGroups; g++)
        {
            var group = g < DecalDrawOrder.Count ? decalGroups[DecalDrawOrder[g]] : null;
            int first = picks;

            if (group != null && ready)
            {
                for (int cell = 0; cell < decalSheetHeld && picks < MaxDecalPicks; cell++)
                {
                    bool ticked = group.allDrawings ||
                                  (cell < decalSheetDrawings.Count && group.drawings != null &&
                                   group.drawings.Contains(decalSheetDrawings[cell]));
                    if (ticked) DecalPicks[picks++] = cell;
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

            DecalGroupSeeds[g] = group != null ? group.seed : 0f;

            // 1 tiles the group edge to edge, 0 scatters it.
            DecalGroupTiled[g] = group != null && group.uniformSpacing ? 1f : 0f;

            // Blend Tiles: x the gradient noise's scale in metres, y its softness. x 0 is off —
            // straight joins — which is what a group without Tiled, or with no scale, gets.
            bool blend = group != null && group.uniformSpacing && group.blendTiles
                      && group.blendNoiseScale > 0f;
            DecalGroupTileBlend[g] = blend
                ? new Vector4(group.blendNoiseScale, group.blendNoiseSoftness, 0f, 0f)
                : Vector4.zero;

            // 1 hands the group out to be laid on after the lighting, 0 lays it on the bare stone.
            DecalGroupAboveLight[g] = group != null && group.aboveLighting ? 1f : 0f;

            // The Blend Mode by its number, 0 Normal.
            DecalGroupBlend[g] = group != null ? (float)group.blendMode : 0f;
        }

        // Which groups each stone takes: one bit per group, by its slot. A float holds sixteen
        // bits exactly. And its Intensity for each, stone i's slot g at i * groups + g, four
        // to a vector — 1 wherever it takes nothing, so no slot is ever left reading 0.
        for (int i = 0; i < MaxStoneTypes; i++)
        {
            var type  = i < stoneTypes.Count ? stoneTypes[i] : null;
            int mask  = 0;

            for (int g = 0; g < MaxDecalGroups; g++)
            {
                var group = type != null && type.decalGroups != null && g < DecalDrawOrder.Count
                          ? decalGroups[DecalDrawOrder[g]] : null;
                bool takes = group != null && type.decalGroups.Contains(group.id);
                if (takes) mask |= 1 << g;

                int at = i * MaxDecalGroups + g;
                StoneDecalIntensities[at >> 2][at & 3] = takes ? type.DecalGroupIntensity(group.id) : 1f;
            }

            StoneDecalGroups[i] = mask;
        }

        Shader.SetGlobalVectorArray(DecalGroupScatterId, DecalGroupScatter);
        Shader.SetGlobalVectorArray(DecalGroupLookId,    DecalGroupLook);
        Shader.SetGlobalFloatArray(DecalGroupSeedsId,    DecalGroupSeeds);
        Shader.SetGlobalFloatArray(DecalGroupTiledId,    DecalGroupTiled);
        Shader.SetGlobalVectorArray(DecalGroupTileBlendId, DecalGroupTileBlend);
        Shader.SetGlobalFloatArray(DecalGroupAboveLightId, DecalGroupAboveLight);
        Shader.SetGlobalFloatArray(DecalGroupBlendId,    DecalGroupBlend);
        Shader.SetGlobalFloatArray(DecalPicksId,       DecalPicks);
        Shader.SetGlobalFloatArray(StoneDecalGroupsId,   StoneDecalGroups);
        Shader.SetGlobalVectorArray(StoneDecalIntensitiesId, StoneDecalIntensities);
    }

    // Sent at full length every push, for the same reason as the stone types' arrays below.
    private static readonly Vector4[] DecalGroupScatter = new Vector4[MaxDecalGroups];
    private static readonly Vector4[] DecalGroupLook    = new Vector4[MaxDecalGroups];
    private static readonly float[]   DecalGroupSeeds   = new float[MaxDecalGroups];
    private static readonly float[]   DecalGroupTiled   = new float[MaxDecalGroups];
    private static readonly Vector4[] DecalGroupTileBlend = new Vector4[MaxDecalGroups];
    private static readonly float[]   DecalGroupAboveLight = new float[MaxDecalGroups];
    private static readonly float[]   DecalGroupBlend   = new float[MaxDecalGroups];

    // Which list place goes in each shader slot, by Sort Order. Kept to save a list a push.
    private static readonly List<int> DecalDrawOrder    = new List<int>(MaxDecalGroups);
    private static readonly float[]   DecalPicks        = new float[MaxDecalPicks];
    private static readonly float[]   StoneDecalGroups  = new float[MaxStoneTypes];
    private static readonly Vector4[] StoneDecalIntensities = new Vector4[MaxStoneTypes * MaxDecalGroups / 4];

    private static readonly int NormalDecalSheetId      = Shader.PropertyToID("_RiverRunNormalDecalSheet");
    private static readonly int NormalDecalLayoutId     = Shader.PropertyToID("_RiverRunNormalDecalLayout");
    private static readonly int NormalDecalGroupTilesId = Shader.PropertyToID("_RiverRunNormalDecalGroupTiles");
    private static readonly int NormalDecalGroupBlendId = Shader.PropertyToID("_RiverRunNormalDecalGroupBlend");
    private static readonly int NormalDecalPicksId      = Shader.PropertyToID("_RiverRunNormalDecalPicks");
    private static readonly int StoneNormalDecalGroupsId = Shader.PropertyToID("_RiverRunStoneNormalDecalGroups");

    /// <summary>
    /// The normal map sheet and the tiles it is laid in. The same guard as the colour decals: the
    /// sheet's held count is what keeps an unpushed texture global from bending the stone.
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
        }

        // Which normal map groups each stone takes: one bit per group, by its place in the list.
        for (int i = 0; i < MaxStoneTypes; i++)
        {
            var type = i < stoneTypes.Count ? stoneTypes[i] : null;
            int mask = 0;

            if (type != null && type.normalDecalGroups != null && normalDecalGroups != null)
            {
                for (int g = 0; g < normalDecalGroups.Count && g < MaxNormalDecalGroups; g++)
                {
                    var group = normalDecalGroups[g];
                    if (group != null && type.normalDecalGroups.Contains(group.id)) mask |= 1 << g;
                }
            }

            StoneNormalDecalGroups[i] = mask;
        }

        Shader.SetGlobalVectorArray(NormalDecalGroupTilesId, NormalDecalGroupTiles);
        Shader.SetGlobalVectorArray(NormalDecalGroupBlendId, NormalDecalGroupBlend);
        Shader.SetGlobalFloatArray(NormalDecalPicksId,       NormalDecalPicks);
        Shader.SetGlobalFloatArray(StoneNormalDecalGroupsId, StoneNormalDecalGroups);
    }

    // Sent at full length every push, like the decal groups' arrays.
    private static readonly Vector4[] NormalDecalGroupTiles  = new Vector4[MaxNormalDecalGroups];
    private static readonly Vector4[] NormalDecalGroupBlend  = new Vector4[MaxNormalDecalGroups];
    private static readonly float[]   NormalDecalPicks       = new float[MaxNormalDecalPicks];
    private static readonly float[]   StoneNormalDecalGroups = new float[MaxStoneTypes];

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

        // Each of those three's own Scale, on top of the River Runs Designation Scale. One a preset
        // saved before they existed never set reads as 1.
        Shader.SetGlobalVector(RunScalesId, new Vector4(
            runOuterFacesScale   > 0f ? runOuterFacesScale   : 1f,
            runRimScale          > 0f ? runRimScale          : 1f,
            runInnerChannelScale > 0f ? runInnerChannelScale : 1f,
            0f));

        // And their own Seam Extent. 0 is a real choice here, so w = 1 is what says it was pushed.
        Shader.SetGlobalVector(RunSeamExtentsId, new Vector4(
            Mathf.Max(0f, runOuterFacesSeamExtent),
            Mathf.Max(0f, runRimSeamExtent),
            Mathf.Max(0f, runInnerChannelSeamExtent),
            1f));
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

        runOuterFacesScale   = other.runOuterFacesScale;
        runRimScale          = other.runRimScale;
        runInnerChannelScale = other.runInnerChannelScale;

        runOuterFacesSeamExtent   = other.runOuterFacesSeamExtent;
        runRimSeamExtent          = other.runRimSeamExtent;
        runInnerChannelSeamExtent = other.runInnerChannelSeamExtent;

        seamColour        = other.seamColour;
        seamStrength      = other.seamStrength;
        seamExtentPercent = other.seamExtentPercent;

        waterlineColour   = other.waterlineColour;
        waterlineStrength = other.waterlineStrength;
        waterlineExtent   = other.waterlineExtent;

        seamMask      = other.seamMask;
        seamMaskScale = other.seamMaskScale;
        seamMaskTiles = other.seamMaskTiles;
        seamMaskMapping  = other.seamMaskMapping;
        seamMaskMode     = other.seamMaskMode;
        seamMaskSoftness = other.seamMaskSoftness;
        seamsAboveLighting = other.seamsAboveLighting;
        seamBlendMode    = other.seamBlendMode;

        interiorDarkColour   = other.interiorDarkColour;
        interiorDarkSoftness = other.interiorDarkSoftness;
        interiorBoatLight    = other.interiorBoatLight;
        interiorLampLight    = other.interiorLampLight;

        decalSheet          = other.decalSheet;
        decalSheetColumns    = other.decalSheetColumns;
        decalSheetRows       = other.decalSheetRows;
        decalSheetHeld       = other.decalSheetHeld;
        decalSheetCellPixels = other.decalSheetCellPixels;
        decalSheetDrawings   = new List<string>(other.decalSheetDrawings);
        decalGroupsSet       = other.decalGroupsSet;

        // Copied group by group, so the tuner's working copy never edits the preset's underneath it.
        decalGroups = new List<DecalGroup>();
        foreach (var group in other.decalGroups)
        {
            var copy = new DecalGroup();
            copy.CopyFrom(group);
            decalGroups.Add(copy);
        }

        normalDecalSheet           = other.normalDecalSheet;
        normalDecalSheetColumns    = other.normalDecalSheetColumns;
        normalDecalSheetRows       = other.normalDecalSheetRows;
        normalDecalSheetHeld       = other.normalDecalSheetHeld;
        normalDecalSheetCellPixels = other.normalDecalSheetCellPixels;
        normalDecalSheetDrawings   = new List<string>(other.normalDecalSheetDrawings);

        normalDecalGroups = new List<NormalDecalGroup>();
        foreach (var group in other.normalDecalGroups)
        {
            var copy = new NormalDecalGroup();
            copy.CopyFrom(group);
            normalDecalGroups.Add(copy);
        }

        decalAmount          = other.decalAmount;
        decalOpacity         = other.decalOpacity;
        decalSpacing         = other.decalSpacing;
        decalScaleMin        = other.decalScaleMin;
        decalScaleMax        = other.decalScaleMax;

        designationScales = other.designationScales != null
                          ? new List<float>(other.designationScales) : new List<float>();

        outpostOutsideWalls = other.outpostOutsideWalls;
        outpostWallTop      = other.outpostWallTop;
        outpostInsideWalls  = other.outpostInsideWalls;
        outpostFloor        = other.outpostFloor;
        outpostColumnSides  = other.outpostColumnSides;
        outpostColumnTop    = other.outpostColumnTop;
        outpostOpeningSides = other.outpostOpeningSides;
        outpostOpeningFloor = other.outpostOpeningFloor;
        outpostSlopeTop     = other.outpostSlopeTop;
        outpostSlopeSides   = other.outpostSlopeSides;
        outpostSlopeEnd     = other.outpostSlopeEnd;

        structureSides       = other.structureSides;
        structureWallTop     = other.structureWallTop;
        structureInsideWalls = other.structureInsideWalls;
        structureTop         = other.structureTop;
        structureColumnSides = other.structureColumnSides;
        structureColumnTop   = other.structureColumnTop;

        walkwayTop       = other.walkwayTop;
        walkwayOuterWall  = other.walkwayOuterWall;
        walkwayStep       = other.walkwayStep;
        walkwayEnds       = other.walkwayEnds;

        bridgeTop         = other.bridgeTop;
        bridgeUnderside   = other.bridgeUnderside;
        bridgeRampEnds    = other.bridgeRampEnds;
        bridgeSides       = other.bridgeSides;

        riverArchTop      = other.riverArchTop;
        riverArchOutside  = other.riverArchOutside;
        riverArchOpening  = other.riverArchOpening;
        riverArchFaces    = other.riverArchFaces;

        rimWallTop        = other.rimWallTop;
        rimWallRiverFace  = other.rimWallRiverFace;
        rimWallOuterFace  = other.rimWallOuterFace;
        rimWallEnds       = other.rimWallEnds;
        poolDoorway       = other.poolDoorway;
        riverPortholes    = other.riverPortholes;

        rimNodeTop         = other.rimNodeTop;
        rimNodeSides       = other.rimNodeSides;
        rimNodeScreenEdges = other.rimNodeScreenEdges;
        rimNodePlinthTop   = other.rimNodePlinthTop;
        rimNodePlinthSides = other.rimNodePlinthSides;

        towerOrb          = other.towerOrb;
        towerStem         = other.towerStem;
        towerBaseSides    = other.towerBaseSides;
        towerBaseTop      = other.towerBaseTop;
        towerLowerRamp    = other.towerLowerRamp;
        towerUpperRamp    = other.towerUpperRamp;
        towerLowerRampTop    = other.towerLowerRampTop;
        towerUpperRampTop    = other.towerUpperRampTop;
        towerSecondBaseSides = other.towerSecondBaseSides;
        towerSecondBaseTop   = other.towerSecondBaseTop;
        towerOrbInside       = other.towerOrbInside;
        towerOrbHoleEdge     = other.towerOrbHoleEdge;
        towerInnerLollipopOrb  = other.towerInnerLollipopOrb;
        towerInnerLollipopStem = other.towerInnerLollipopStem;
        towerInnerLollipopBase = other.towerInnerLollipopBase;

        turretBaseSides   = other.turretBaseSides;
        turretBaseTop     = other.turretBaseTop;
        turretLowerRamp   = other.turretLowerRamp;
        turretUpperRamp   = other.turretUpperRamp;
        turretStem        = other.turretStem;
        turretHead        = other.turretHead;
        turretNose        = other.turretNose;

        poolTowerBaseSides   = other.poolTowerBaseSides;
        poolTowerBaseTop     = other.poolTowerBaseTop;
        poolTowerRamp        = other.poolTowerRamp;
        poolTowerColumn      = other.poolTowerColumn;
        poolTowerBowlOutside = other.poolTowerBowlOutside;
        poolTowerBowlRim     = other.poolTowerBowlRim;
        poolTowerBowlInside  = other.poolTowerBowlInside;
        poolTowerLowerRamp   = other.poolTowerLowerRamp;
        poolTowerOrb         = other.poolTowerOrb;
        poolTowerTopHub      = other.poolTowerTopHub;
        poolTowerSideHub     = other.poolTowerSideHub;
        poolTowerTap         = other.poolTowerTap;
        poolTowerIntake      = other.poolTowerIntake;

        fishBowlBaseSides = other.fishBowlBaseSides;
        fishBowlBaseTop   = other.fishBowlBaseTop;
        fishBowlRamp      = other.fishBowlRamp;
        fishBowlColumn    = other.fishBowlColumn;
        fishBowlOutside   = other.fishBowlOutside;
        fishBowlRim       = other.fishBowlRim;
        fishBowlInside    = other.fishBowlInside;

        wallSides         = other.wallSides;
        wallTop           = other.wallTop;
        wallEnds          = other.wallEnds;
        wallOpenings      = other.wallOpenings;
        wallColumnSides   = other.wallColumnSides;
        wallColumnTop     = other.wallColumnTop;
        wallLipTop        = other.wallLipTop;
        wallLipSides      = other.wallLipSides;
        wallLipUnderside  = other.wallLipUnderside;
        wallPortholes          = other.wallPortholes;
        wallDamageEdge         = other.wallDamageEdge;
        wallColumnLipTop       = other.wallColumnLipTop;
        wallColumnLipSides     = other.wallColumnLipSides;
        wallColumnLipUnderside = other.wallColumnLipUnderside;
        wallColumnSleeve       = other.wallColumnSleeve;

        pipeOutside       = other.pipeOutside;
        pipeInside        = other.pipeInside;
        pipeRings         = other.pipeRings;
        pipeStems         = other.pipeStems;
        pipeHubSleeve     = other.pipeHubSleeve;
        pipeHubRing       = other.pipeHubRing;

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

        partsSeeded       = other.partsSeeded;
        towerTiersSeeded  = other.towerTiersSeeded;
        fishBowlSeeded    = other.fishBowlSeeded;
    }
}
