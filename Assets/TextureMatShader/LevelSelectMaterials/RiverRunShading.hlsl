// The shading the level select's stone rivers are drawn with: the stone's own colour, picked by
// which part of the run a face belongs to and grained with a noise, dark gathered along every
// seam of the run, and white rising off the waterline up the inside of the channel.
//
// Custom Function node name: RiverRunShading
//
// ── What a seam is ───────────────────────────────────────────────────────────
// A hard corner of the generated piece. The rim top folding down into the channel, the rim top
// folding over into the outer wall, that wall standing on the underside, and the open ends where
// one generated piece butts onto the next. The gentle facets the channel floor is cut into are
// deliberately NOT seams — they are one curved surface described in flats, and a line down each
// of them would read as a mistake rather than as a corner.
//
// None of that can be worked out here. It is decided in RiverMeshBuilder as the piece is
// generated, where both faces of every fold are known, and baked into UV1:
//
//     xyz : how far this corner lies from each of the three nearest seams, with a slot holding
//           no seam carrying a number far too big to ever win
//     w   : how far the corner sits OFF the rim top — zero on the rim, negative below it
//
// The shader takes the smallest of the three and has metres-to-the-nearest-seam. Three of them
// rather than one is what lets a single face be shaded from both of the seams it lies between —
// the rim is a strip with a seam down each side, and the outer wall is one quad from the rim top
// all the way down to the underside. A single distance per corner could only ramp from one end of
// either to the other; three meet in the middle instead, which is what puts a line on both edges
// of the rim and both ends of the wall with the whole middle left alone.
//
// ── Which stone a face is ────────────────────────────────────────────────────
// The world holds a list of STONE TYPES — A, B, C, D and any added after — each a colour (or a
// gradient across the piece in its place) with its own grain, and every face takes one.
//
// Which one cannot be answered here. A run shows the world its OUTER faces — the outer walls, the
// underside, the open ends — a RIM lip laid flat round the top, and the INNER faces of the channel
// the water runs down, and a steep channel wall and an outer wall are both very nearly vertical:
// telling them apart needs to know which side of the piece a face is on, which only the builder
// knows. So it is settled as the piece is generated and baked into UV2:
//
//     x : the stone type, 1 for A — or, where z is 1, which of the run's own three the face is,
//         1 outer, 2 rim, 3 inner, looked up through _RiverRunRunStones so the River Runs choices
//         reach a built run without a rebuild
//     y : the width of the surface the face belongs to, which the seam extent is a percentage of
//     z : 1 on a run part, 0 on a stone type taken as it is
//     w : 1 on a mesh that carries any of this at all, so one built before it keeps its colour
//
// A piece baked before there were stone types carries 1, 2 and 3 with z at 0 — which are A, B and
// C, the colours its outer, rim and inner always took.
//
// ── The gradient ─────────────────────────────────────────────────────────────
// A stone type can be coloured by a gradient instead of its one colour, run across the WHOLE
// piece it is on: up or down from the foot of the piece to its top, or left or right along the
// world's X. "The piece" is the renderer's own bounds, so nothing has to be baked for it and a
// piece of any shape, built at any time, takes it. Every stone type's gradient is one row of one
// small sheet the settings make, read at mip 0 so changing stone across a face draws no seam.
//
// ── Why the waterline is measured off the rim and not off the world ──────────
// The water out here is not one flat sheet. It is laid in each river's channel a fixed distance
// under that river's rim top, so a run that climbs carries its water up with it. Measure the band
// off a world height and it would slide off the water the moment the river left the flat; measure
// it off the rim, as w does, and it stays glued to the surface wherever the river goes.
//
// ── The grain ────────────────────────────────────────────────────────────────
// Gradient noise — the same noise, the same hash and the same quintic fade the Gradient Noise
// node draws, moved in here off the graph. In here because the stone's colour is now a stone
// type chosen per face, and a grain laid on outside would have to be laid on before that
// choice was made.
//
// It goes on LAST, over the finished pixel, so it runs across the seams and the waterline as
// well as across the bare stone. Under them it would only ever be seen on whatever the two bands
// left uncovered, which on a run drawn with any real extent is not much of it. It is a multiply,
// so it grains a white band hard and leaves a black seam black.
//
// Size is the width of one grain cell in the units the surface is cut in, which for a generated
// piece is metres; strength is how far the noise pulls either side of the colour underneath, and
// 0 leaves it clean.
//
// ── The light ────────────────────────────────────────────────────────────────
// There is no real light out here, so the stone is shaped by one made up: a POINT standing
// somewhere in the world, with every pixel turned against the direction from itself to that point.
// This is what replaced the Simulated Lighting Basic node the run shader used to carry — it does
// the same job from the same numbers as everything else on the piece, so a run cannot be lit from
// one angle while its seams are drawn for another.
//
// A point rather than a bearing because the direction then CHANGES across the world: the runs
// nearest it turn to face it and the far side of the map falls away, which is what lets one light
// be placed to pick out a particular stretch of river. A bearing gives every piece in the world
// the same answer, which is a sun, and a sun cannot be aimed at anything.
//
// No falloff with distance. The point sets which WAY the light comes from and nothing else — how
// bright the stone is at all is Strength, one number for the whole world — so a light can be put
// close to a run to rake across it without also blowing that run out.
//
// It is a HALF lambert — the turn away from the light mapped across the whole of the range rather
// than clipped at the terminator — because faceted stone lit by a straight dot goes to pure black
// on everything past ninety degrees and the underside of the world falls out of it. Here the
// darkest face still reads as stone.
//
// ── Why the white only lands on the channel ──────────────────────────────────
// The height the waterline sits at exists all the way round a run, outside as well as in, and a
// band placed on height alone would draw a white line round the outside of every river standing
// in nothing. So the white is held to surfaces that face upward. The outer walls, the end caps
// and the underside are all built exactly vertical or facing down, so they take none of it; the
// channel, which is the only thing with water in it, takes all of it.
//
// ── The decals ─────────────────────────────────────────────────────
// Hand-drawn marks stamped over the stone — scattered rather than placed, because there is far
// more generated stone out here than anybody is going to put a decal on by hand.
//
// They are scattered on a GRID of candidate places, one every Spacing metres across the surface,
// with each place given a position anywhere inside its own cell so the grid it was picked on
// never shows as a grid. Amount is how many of those places are actually taken: it thins the
// scatter out without moving what is left, so turning it down leaves gaps rather than shrinking
// the marks. Scale is drawn between a smallest and a largest for each place, and is the metres
// across the LONGEST side of the decal, whatever shape it was drawn. Opacity is how far each
// mark sinks into the stone, which is a separate question from how many of them there are.
//
// The scatter belongs to a DECAL GROUP, not to a stone: a group is a selection of the drawings
// with its own Amount, Spacing, Scales, Opacity and rotation, and each stone type ticks the groups
// it takes. Several ticked are each scattered on their own grid and stacked, the later group on
// top — so every group ticked costs a stone one more scatter.
//
// Every decal in the folder is carried on one sheet, side by side in equal cells — one texture
// and one read per stamp rather than one sampler per drawing, which is what lets the number of
// decals grow without the shader growing with it. The sheet is built from
// Assets/TextureMatShader/LevelSelectMaterials/RiverRunDecals by the Run Shading Tuner; the
// shader is told only how the cells are laid out, so it neither knows nor cares what is in them.
//
// They are scattered on the WORLD, not on a UV. The grid is laid across world space in metres
// and read off the world position of each pixel, so a decal keeps its size and nothing about
// where it falls is decided per triangle.
//
// That last part is the whole reason for it. UV0 — which the grain still uses — is not an
// unwrap: the builder flattens each triangle's corners onto whichever axis plane the triangle
// most nearly faces, and the triangle NEXT DOOR asks the same question on its own and can get
// a different answer. A noise at two centimetres does not care; a recognisable drawing laid
// across the join is cut clean along the facet edge. The faceted channel of a run, a tower's
// orb, a pool bowl and an archway's curve all cut for that reason.
//
// World space has no such decision in it, but a position is three numbers and a decal wants
// two, so the grid still has to be laid on a plane. Picking the nearest one per pixel only
// moves the cut off the facet edges and onto a smoother line somewhere else. So all THREE are
// laid — across the world's floor, and up each of its two walls — and a pixel takes them mixed
// by how much its own surface faces each. Nothing is ever picked, so there is nothing to flip,
// and a decal crosses a curve unbroken.
//
// What that costs is nearly nothing on most of this stone, because most of it faces squarely
// along one axis — the rim lip is flat, the outer walls and end caps are built exactly
// vertical, the underside faces straight down — and a plane a surface does not face at all is
// dropped before it is scattered rather than scattered and multiplied by nothing. Only the
// faces genuinely angled between two axes pay for two, and those are exactly the faces that
// were being cut.
//
// The cost of mixing rather than picking is that a surface halfway between two planes carries
// a little of both scatters at once, which can read as one decal faintly through another. The
// mix is sharpened to hold that to as narrow a band of angles as it can without the mix
// becoming a choice again.
//
// The decal goes UNDER the light, the seams and the grain: it is a mark ON the stone, so the
// stone's light falls across it, a corner's ink line cuts over it, and the grain runs through it.
//
// ── Globals ──────────────────────────────────────────────────────────────────
// Bare $Globals, exactly like RiverEdgeRipples.hlsl and WaterlineBlackGradient.hlsl: one set of
// numbers shared by every generated piece in the world, so the look is authored once rather than
// per material, and a run, a pool and the piece between them can never drift apart. There is no
// property block behind them and nothing on disk remembers them — until something pushes the
// numbers the shaders see zero, and a reimport puts them back to zero.
//
// What pushes them is LevelSelectDesignerData.ApplyAesthetics, re-pushed EVERY frame — by
// LevelSelectDataController in play mode and by LevelSelectAestheticsPump out of it — which is
// what makes a reimport self-heal on the next tick. Authored in
// Tools > Waves > Level Select Run Shading Tuner.
//
// ── Everything here is inert at zero ─────────────────────────────────────────
// Strength at 0, Extent at 0, or a decal Amount or Opacity of 0, returns the run to exactly
// what it was,
// and a face colour
// arriving with nothing in its alpha is one nobody has pushed yet, so the stone keeps the colour
// the graph handed in. A decal sheet holding nothing draws nothing. Nothing here is on unless it is asked for — which is also what a world
// with no preset assigned gets, and what the one tick after a reimport gets.

#ifndef RIVER_RUN_SHADING_INCLUDED
#define RIVER_RUN_SHADING_INCLUDED

float4 _RiverRunWaterlineColour;
float  _RiverRunWaterlineStrength;
float  _RiverRunWaterlineExtent;

// How many stone types there can be, and how many steps across one row of the gradient sheet.
// Both must match RiverRunShadingSettings.MaxStoneTypes and GradientWidth.
#define RIVER_RUN_STONE_TYPES  16
#define RIVER_RUN_GRADIENT_W   128.0

// What UV3's z and w carry on a face in a face group, saying x and y are its place in the group —
// RiverMeshBuilder.GroupGradientMarkZ/W. Both, so nothing else ever passes for one.
#define RIVER_RUN_GROUP_MARK_Z -7.5
#define RIVER_RUN_GROUP_MARK_W 4.25

// The stone types, A first. Alpha on the colour is not a colour — it is how a colour that has
// been pushed is told from the zeroes a shader reimport leaves behind.
float4 _RiverRunStoneColours[RIVER_RUN_STONE_TYPES];

// x how wide one cell of the grain is, in the units the surface is cut in; y how much of the
// noise reaches the stone — either at zero is no grain at all. z the gradient: 0 off, 1 bottom
// to top, 2 top to bottom, 3 left to right, 4 right to left. w the seam extent: a PERCENTAGE,
// not metres — how far the gradient reaches off a seam as a share of the width of the surface it
// runs across, which the builder bakes into UV2.y, so a wide river and a thin one, a tall wall
// and a tower stem are each shaded in proportion to themselves.
float4 _RiverRunStoneGrains[RIVER_RUN_STONE_TYPES];

// Each stone type's seams: rgb the colour gathered along them, w how much of it sits on the seam.
// All zero until pushed, which draws no seams at all.
float4 _RiverRunStoneSeams[RIVER_RUN_STONE_TYPES];

// The Seam Mask: one tiled texture taken off every stone type's seams wherever its alpha is drawn,
// laid across the world on the three planes like the decals. Repeats is how many times it goes
// round per metre — 0 until pushed, and 0 with no mask set, which leaves the seams whole and the
// texture unread.
TEXTURE2D(_RiverRunSeamMask);
SAMPLER(sampler_RiverRunSeamMask);
float _RiverRunSeamMaskRepeats;

// How the mask takes the seam away. x Erode, at that softness — the seam's faint outer edge goes
// first and its core last — or 0 for none. y 1 to Fade as well: the whole seam dims evenly under
// the drawing. Both on is Fade And Erode.
float4 _RiverRunSeamMaskMode;

// 1 lays the seams on with the decals ticked Appear Above Lighting — after the Level Select Shadow
// and the River Glow — rather than into the stone under both. 0 until pushed, which is under.
float _RiverRunSeamsAboveLight;

// Seam Blend Mode, one for every stone type, by the number of DecalBlendMode — the same numbers
// as the decal groups'. 0 until pushed, which is Normal.
float _RiverRunSeamBlend;

// Every stone type's gradient, one row each, A at the bottom.
TEXTURE2D(_RiverRunStoneGradients);
SAMPLER(sampler_RiverRunStoneGradients);

// Which stone type a river run's own outer faces, rim and channel take — x, y, z, 1 for A.
float4 _RiverRunRunStones;

// Each Designation's Scale in the Run Shading Tuner, by StoneDesignation — slot 0 is None. It
// multiplies the grain size, the decal spacing and scales, and the seam extent on every face
// built as that Designation. Must match RiverRunShadingSettings.MaxDesignations.
#define RIVER_RUN_DESIGNATIONS 32
float _RiverRunDesignationScales[RIVER_RUN_DESIGNATIONS];

// The River Runs Designation's own parts each carry a Scale too — x outer faces, y rim, z inner
// channel — multiplied on top of the River Runs Scale. Only on a face built as River Runs that
// carries the run part mark; 0 until pushed, which reads as 1. Must match
// StoneDesignation.RiverRuns.
#define RIVER_RUN_DESIGNATION_RIVER_RUNS 1
float4 _RiverRunRunScales;

// The same three parts' own Seam Extent, multiplying the seam extent after everything else — x
// outer faces, y rim, z inner channel. w is 1 once pushed: 0 is a real setting here (no seams on
// that part), so the zero an unpushed global arrives as cannot be read as "unset" the way the
// Scales are, and until then every part reads as 1.
float4 _RiverRunRunSeamExtents;

// The hand-drawn decals, all of them on one sheet: equal cells side by side, filled left to right
// and bottom to top, each holding one drawing fitted to it with a little transparent padding
// round the edge so the sheet's mips cannot bleed one cell into the next.
TEXTURE2D(_RiverRunDecalSheet);
SAMPLER(sampler_RiverRunDecalSheet);

// How that sheet is laid out: x columns, y rows, z how many of the cells actually hold a drawing,
// w how many pixels down one cell. z at zero is what makes the decals inert before anything has
// pushed a sheet — there is no such thing as a transparent default texture to fall back on, so a
// count of nothing is the only thing standing between an unpushed global and a stone covered in
// whatever Unity happened to bind.
float4 _RiverRunDecalLayout;

// How many decal groups there can be, and how many cells all of them together can list. Both
// must match RiverRunShadingSettings.MaxDecalGroups and MaxDecalPicks.
#define RIVER_RUN_DECAL_GROUPS 16
#define RIVER_RUN_DECAL_PICKS  128

// Each decal group's scatter. x Spacing: one candidate place every this many metres. y and z the
// smallest and largest Scale, the metres across the longest side of a decal, drawn between the
// two for each place. w Amount: how many of those places are taken, 0 to 1. Every stone ticking
// the same group shares its grid, so a decal carries across the line between them unbroken.
float4 _RiverRunDecalGroupScatter[RIVER_RUN_DECAL_GROUPS];

// Each group's look and drawings. x Opacity: how much of the stone a decal hides, 0 to 1 — a
// different thing from Amount, which decides how MANY marks there are. y how many are given a
// random quarter turn, 90, 180 or 270, never mirrored — 0 with the rotation off. z the first of
// the group's picks below, w how many: the group draws only the sheet cells listed there.
float4 _RiverRunDecalGroupLook[RIVER_RUN_DECAL_GROUPS];

// Moves each group's whole scatter, so two groups with the same spacing do not stand their decals
// one exactly over the other. 0 is the scatter there was before groups.
float _RiverRunDecalGroupSeeds[RIVER_RUN_DECAL_GROUPS];

// Each group's Tiled: 1 fills every cell with a decal exactly one Spacing across, so they meet
// edge to edge like tiles — Amount and Scale are not read. 0 is the scatter.
float _RiverRunDecalGroupTiled[RIVER_RUN_DECAL_GROUPS];

// Each group's Blend Tiles, read only while Tiled is on. x the Gradient Noise Scale, in metres
// across one wobble — 0 is off, and the tiles meet at straight edges. y the Gradient Noise
// Softness, 0 to 1: how wide the fade from one tile into the next is.
float4 _RiverRunDecalGroupTileBlend[RIVER_RUN_DECAL_GROUPS];

// How far the gradient noise can push the line between two blended tiles off the cell edge, as
// a share of a tile. Fixed rather than a setting: the noise peaks a little under 0.7, so this
// holds every tile's own middle as its own, and a tile is never wholly lost to its neighbours.
#define RIVER_RUN_DECAL_TILE_WANDER 0.5

// Each group's Appear Above Lighting: 1 hands the group's decals out separately, to be laid on
// after the light, the Level Select Shadow and the River Glow, so none of them touches it. 0 lays
// it on the bare stone, under all three, as decals always were.
float _RiverRunDecalGroupAboveLight[RIVER_RUN_DECAL_GROUPS];

// Each group's Blend Mode, by the number of DecalBlendMode in RiverRunShadingSettings: how the
// group, finished as one layer, mixes with the stone and the groups below it.
float _RiverRunDecalGroupBlend[RIVER_RUN_DECAL_GROUPS];

#define RIVER_RUN_DECAL_BLEND_NORMAL    0
#define RIVER_RUN_DECAL_BLEND_MULTIPLY  1
#define RIVER_RUN_DECAL_BLEND_SCREEN    2
#define RIVER_RUN_DECAL_BLEND_ADD       3
#define RIVER_RUN_DECAL_BLEND_SUBTRACT  4
#define RIVER_RUN_DECAL_BLEND_OVERLAY   5
#define RIVER_RUN_DECAL_BLEND_SOFTLIGHT 6

// The transparent pixels round each drawing on the sheet. Must match RiverRunDecalLibrary.Padding.
#define RIVER_RUN_DECAL_PADDING 2.0

// The sheet cells every group picks among, each group's a run of this list.
float _RiverRunDecalPicks[RIVER_RUN_DECAL_PICKS];

// Which groups each stone type takes: one bit per group, as a whole number in a float.
float _RiverRunStoneDecalGroups[RIVER_RUN_STONE_TYPES];

// Each stone's Intensity for each group it ticks, 0 to 3, on top of the group's own Opacity.
// Stone i's group slot g is entry i * RIVER_RUN_DECAL_GROUPS + g, four to a float4.
float4 _RiverRunStoneDecalIntensities[RIVER_RUN_STONE_TYPES * RIVER_RUN_DECAL_GROUPS / 4];

float RiverRunStoneDecalIntensity(int stoneIndex, int g)
{
    int    at   = stoneIndex * RIVER_RUN_DECAL_GROUPS + g;
    int    lane = at - (at / 4) * 4;
    return dot(_RiverRunStoneDecalIntensities[at / 4],
               float4(lane == 0, lane == 1, lane == 2, lane == 3));
}

// ── Normal map decals ────────────────────────────────────────────────────────
// Real normal maps on a sheet of their own, laid out like the decal sheet but LINEAR, so a texel
// reads back as the direction it was drawn as. Always tiled. They paint nothing: they bend the
// normal the light, the boat's light and the River Glow are worked out from.
TEXTURE2D(_RiverRunNormalDecalSheet);
SAMPLER(sampler_RiverRunNormalDecalSheet);

// x columns, y rows, z how many cells hold a normal map, w pixels down one cell. z at zero keeps
// an unpushed sheet from bending anything, as with the decal sheet.
float4 _RiverRunNormalDecalLayout;

// Both must match RiverRunShadingSettings.MaxNormalDecalGroups and MaxNormalDecalPicks.
#define RIVER_RUN_NORMAL_DECAL_GROUPS 8
#define RIVER_RUN_NORMAL_DECAL_PICKS  64

// Each normal map group: x the tile width in metres, y Strength — 1 the relief as drawn — z the
// first of its picks, w how many.
float4 _RiverRunNormalDecalGroupTiles[RIVER_RUN_NORMAL_DECAL_GROUPS];

// x the Blend Tiles noise scale in metres, 0 off. y its softness. z the seed.
float4 _RiverRunNormalDecalGroupBlend[RIVER_RUN_NORMAL_DECAL_GROUPS];

float _RiverRunNormalDecalPicks[RIVER_RUN_NORMAL_DECAL_PICKS];

// Which normal map groups each stone type takes: one bit per group.
float _RiverRunStoneNormalDecalGroups[RIVER_RUN_STONE_TYPES];

// How far the water lies beneath the rim top — the world's Water Level, pushed alongside the rest
// rather than authored a second time.
float  _RiverRunWaterDepth;

// Where the made-up light stands, in world space, and how strong it is. Every pixel works out its
// own direction to it. Both come off the scene's LevelSelectLight, shared with the landscape hills.
float4 _LevelSelectLightPosition;
float  _LevelSelectLightStrength;

// The boat's light, added over the made-up light's. Outside only — inside, the boat is already the
// light (below).
#include "Assets/TextureMatShader/LevelSelectMaterials/LevelSelectBoatLight.hlsl"

// ── Interior ─────────────────────────────────────────────────────────────────
// A material with Interior ticked (the _RIVER_RUN_INTERIOR keyword) is stone inside a level: dark,
// lit only round the boat and the street lights, like the spline walls and maze rocks beside it.
// The made-up light above is not used there, and the waterline is measured off the real water
// surface — waves, Water Level changes and all — rather than off the rim.
//
// The street lights and the water come off the same globals the rest of the level reads
// (InstancedLights.hlsl, WaterSurfaceHeight.hlsl). The boat comes in under its own name, copied
// across by RiverRunInteriorShading each frame: RiverRunShader already declares _BoatWorldCenter
// as a graph property, and a second declaration here would clash with it.
#include "Assets/ScriptsData/VisualEffectGraphScripts/InstancedLights.hlsl"
#include "Assets/TextureMatShader/MainWave/WaterSurfaceHeight.hlsl"

// xyz the boat, w the boat light's radius (BoatLightController) — where the dark starts.
float4 _RiverRunBoatLight;

// rgb what the stone falls to in the dark.
float4 _RiverRunInteriorDark;

// x metres past the boat light's radius the fade into the dark takes, y how strongly the boat
// shapes the stone, z how much the street lights add.
float4 _RiverRunInterior;

// How far off flat a face may point and still count as facing up — the whole of the channel does,
// and the outer walls, which stand exactly vertical, do not.
#define RIVER_RUN_UP_FACING 0.15

// What UV2.x says a run part is, where UV2.z marks it as one.
#define RIVER_RUN_FACE_OUTER 1
#define RIVER_RUN_FACE_RIM   2
#define RIVER_RUN_FACE_INNER 3

// Which stone type a face takes, 0 for A. A run part is looked up through the River Runs
// choices; one nobody has pushed yet reads as the A, B and C the three always were. A face with
// no kind at all — a mesh built before they were baked — reads as A.
int RiverRunStoneIndex(float4 faceData)
{
    if (faceData.w < 0.5) return 0;

    int kind = (int)(faceData.x + 0.5);

    if (faceData.z > 0.5)
    {
        float chosen = kind == RIVER_RUN_FACE_RIM   ? _RiverRunRunStones.y
                     : kind == RIVER_RUN_FACE_INNER ? _RiverRunRunStones.z
                                                    : _RiverRunRunStones.x;
        if (chosen > 0.5) kind = (int)(chosen + 0.5);
    }

    return clamp(kind - 1, 0, RIVER_RUN_STONE_TYPES - 1);
}

// Whether this face is one of a river run's own outer faces, rim or channel, built as River Runs.
// A rim node's or a walkway's run parts are built as their own Designation, so they are not: their
// one Designation's settings are all they take.
bool RiverRunOnRunPart(float4 faceData)
{
    return (int)(faceData.w + 0.5) - 1 == RIVER_RUN_DESIGNATION_RIVER_RUNS && faceData.z > 0.5;
}

// The run part's own one of a per-part River Runs setting — x outer faces, y rim, z inner channel.
float RiverRunRunPartPick(float4 faceData, float4 perPart)
{
    int kind = (int)(faceData.x + 0.5);
    return kind == RIVER_RUN_FACE_RIM   ? perPart.y
         : kind == RIVER_RUN_FACE_INNER ? perPart.z
                                        : perPart.x;
}

// The Scale of the Designation this face was built as. UV2.w is the face mark plus the
// Designation, so 1 is a face built before Designations were baked — and a Scale nobody has
// pushed yet arrives as zero — and both of those are 1, leaving the stone exactly as it was.
float RiverRunDesignationScale(float4 faceData)
{
    int designation = (int)(faceData.w + 0.5) - 1;
    if (designation <= 0) return 1.0;

    float scale = _RiverRunDesignationScales[min(designation, RIVER_RUN_DESIGNATIONS - 1)];
    if (scale <= 0.0) scale = 1.0;

    // A run's outer faces, rim and channel each take their own Scale on top.
    if (RiverRunOnRunPart(faceData))
    {
        float part = RiverRunRunPartPick(faceData, _RiverRunRunScales);
        if (part > 0.0) scale *= part;
    }

    return scale;
}

// A run part's own Seam Extent, multiplying its seam extent; 1 on every other face.
float RiverRunRunSeamExtent(float4 faceData)
{
    if (_RiverRunRunSeamExtents.w < 0.5 || !RiverRunOnRunPart(faceData)) return 1.0;
    return max(RiverRunRunPartPick(faceData, _RiverRunRunSeamExtents), 0.0);
}

// A ramp that leaves and arrives flat, so neither end of a gradient shows the line it stops on.
// An extent of nothing is nothing at all rather than a hairline on the seam itself — an effect
// turned off has to leave, not linger a pixel wide.
float RiverRunRamp(float distance, float extent)
{
    if (extent <= 0.0) return 0.0;
    float t = saturate(1.0 - distance / extent);
    return t * t * (3.0 - 2.0 * t);
}

// Which way the noise leans in the cell at p. Worked out by arithmetic rather than by a sine, so
// every machine that draws this river agrees about where the grain is.
float2 RiverRunNoiseDir(float2 p)
{
    p = fmod(p, 289.0);
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

// Gradient noise about zero: the four corners of the cell leaned into each other across a quintic
// fade — the same shape the Gradient Noise node draws, so what was authored on the graph is what
// comes back out of here.
float RiverRunGradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);

    float d00 = dot(RiverRunNoiseDir(ip),                    fp);
    float d01 = dot(RiverRunNoiseDir(ip + float2(0.0, 1.0)), fp - float2(0.0, 1.0));
    float d10 = dot(RiverRunNoiseDir(ip + float2(1.0, 0.0)), fp - float2(1.0, 0.0));
    float d11 = dot(RiverRunNoiseDir(ip + float2(1.0, 1.0)), fp - float2(1.0, 1.0));

    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y);
}

// What the grain multiplies the stone by. 1 is clean stone, so it can be multiplied in without
// asking first — which is what a grain of no size and a grain of no strength both come to.
//
// It sits ABOUT one rather than under it: the noise darkens and lightens by as much as each
// other, so graining a colour does not also drag it down. The colour picked for a face is the
// colour that face averages out at however hard the grain is turned up.
//
// Each stone type has its own grain. A face carrying no kind — a mesh built before they were
// baked — takes A's. Its size is multiplied by the face's Designation Scale.
float RiverRunGrain(float2 uv, float4 faceData, float scale)
{
    float4 grain    = _RiverRunStoneGrains[RiverRunStoneIndex(faceData)];
    float  size     = grain.x * scale;
    float  strength = grain.y;

    if (size <= 0.0 || strength <= 0.0) return 1.0;

    float noise = RiverRunGradientNoise(uv / size);
    return max(0.0, 1.0 + noise * 2.0 * strength);
}

// Four independent numbers from one grid cell, by arithmetic alone — no sine, for the same
// reason the grain's noise avoids one: every machine that draws this river has to agree about
// which places are taken and what is standing in them, or the decals move from machine to machine.
//
// x decides whether the place is taken at all, yz where in its cell the decal stands, w how big
// it is. Which drawing it is comes from a second call, so the pick cannot drift with the size.
float4 RiverRunDecalHash(float2 c)
{
    float4 p = frac(c.xyxy * float4(0.1031, 0.1030, 0.0973, 0.1099));
    p += dot(p, p.wzxy + 33.33);
    return frac((p.xxyz + p.yzzw) * p.zywx);
}

// What the decals lay over the stone at one point on ONE of the three planes: rgb already
// multiplied by its own coverage, a the coverage — ready to be laid straight over whatever is
// underneath, or mixed with the other two planes' answers.
//
// uv is the world position flattened onto this plane, in metres. footprint is how much of that
// plane one screen pixel covers, and is handed IN rather than taken here: it comes of a
// derivative, and this function is called inside the test that drops a plane the surface does
// not face, where a derivative would be undefined. RiverRunDecalsOnStone takes all three
// before it decides anything.
//
// Nothing here runs at all until a sheet has been pushed, a spacing and a scale given, and
// Amount turned up off zero, so a preset that has never heard of decals draws none.
//
// scatter, rotation, the run of picks, the seed and the tile blend are one decal group's — see the
// globals above.
float4 RiverRunDecals(float2 uv, float footprint, float4 scatter, float rotation,
                      float firstPick, float pickCount, float seed, float tiled, float2 tileBlend)
{
    float4 nothing = float4(0.0, 0.0, 0.0, 0.0);

    float2 grid    = _RiverRunDecalLayout.xy;
    int    held    = (int)(_RiverRunDecalLayout.z + 0.5);
    float  cellPix = _RiverRunDecalLayout.w;

    float spacing = scatter.x;
    float amount  = scatter.w;
    float largest = max(scatter.y, scatter.z);
    float small   = min(scatter.y, scatter.z);

    // Tiled: every place taken and every decal exactly as wide as its cell, so they meet edge to
    // edge. Amount and Scale are not read.
    bool tile = tiled > 0.5;
    if (tile) { amount = 1.0; small = spacing; largest = spacing; }

    if (held <= 0 || grid.x < 1.0 || grid.y < 1.0 || cellPix < 1.0) return nothing;
    if (spacing <= 0.0 || amount <= 0.0 || largest <= 0.0)          return nothing;
    if (pickCount < 0.5)                                            return nothing;

    // Where this group's scatter is moved to, so it does not stand on another group's.
    float2 shift = seed * float2(37.0, 59.0);

    // How many rings of neighbouring cells could be holding a decal that reaches this pixel. A
    // decal stands somewhere inside its own cell and reaches half its own width past it, so
    // the ring next door always counts and a further ring only once a decal is wider than two
    // cells. Held to three rings: past six times the spacing the biggest decals are cut off
    // square rather than every pixel in the world paying for a search that wide.
    //
    // Worked out from this group's uniforms rather than from anything about this pixel, so
    // every pixel of one stone takes the same number of turns round the loop and only the line
    // where two stones with different groups meet can diverge.
    //
    // A tile never reaches past its own cell, so Tiled looks at the home cell alone.
    int rings = tile ? 0 : clamp(1 + (int)floor(largest / (2.0 * spacing)), 1, 3);

    // Which cell this pixel is standing in.
    float2 home = floor(uv / spacing);
    int    lo   = -rings;
    int    hi   = rings;

    // Blend Tiles: which tile shows is decided at a position pushed about by gradient noise, so
    // the line between two tiles wanders instead of running straight down the cell edge, and the
    // tiles fade into each other across it. Only the choice wanders — each drawing is still read
    // at this pixel's own place in its grid, so the drawings stay square on it. That reads a
    // neighbour past its own edge, where a tileable drawing simply carries on.
    //
    // The four tiles round the nearest corner of the pushed position are the only ones it can
    // fall between; blend holds how far across each join it is, softened by the Softness.
    bool   blendOn = tile && tileBlend.x > 0.0;
    float2 blend   = float2(0.0, 0.0);
    if (blendOn)
    {
        float2 n = uv / tileBlend.x + shift;
        float2 push = float2(RiverRunGradientNoise(n),
                             RiverRunGradientNoise(n + float2(17.31, -41.7)));
        float2 q = uv / spacing + push * RIVER_RUN_DECAL_TILE_WANDER - 0.5;

        float soft = max(saturate(tileBlend.y) * 0.5, 1e-3);
        blend = smoothstep(0.5 - soft, 0.5 + soft, frac(q));
        home  = floor(q);
        lo    = 0;
        hi    = 1;
    }

    float4 over = nothing;

    for (int y = lo; y <= hi; y++)
    {
        for (int x = lo; x <= hi; x++)
        {
            float2 c = home + float2(x, y);
            float4 h = RiverRunDecalHash(c + shift);

            // A place that loses the draw holds nothing, and its neighbours do not close over
            // the gap. That is what makes Amount thin the scatter rather than tighten it.
            if (!tile && h.x > amount) continue;

            float size = lerp(small, largest, h.w);
            if (size <= 1e-5) continue;

            // Where this pixel falls across the decal, 0 to 1 either way. Outside that the decal
            // is not here, and the cell is dropped before it is ever read. A tile is its cell, so
            // it is measured off the cell itself — measured off a centre, rounding could drop the
            // pixels right on the join and open a hairline between two tiles.
            float2 local;
            float  share = 1.0;
            if (blendOn)
            {
                // How much of this pixel this tile has: the blend across each join, from its side.
                share = (x == 0 ? 1.0 - blend.x : blend.x) * (y == 0 ? 1.0 - blend.y : blend.y);
                if (share < 1e-4) continue;
                local = frac(uv / spacing);
            }
            else if (tile)
            {
                local = saturate(uv / spacing - c);
            }
            else
            {
                float2 centre = (c + h.yz) * spacing;
                local = (uv - centre) / size + 0.5;
                if (any(local < 0.0) || any(local > 1.0)) continue;
            }

            // Which of the group's drawings this place drew, and where that cell sits on the sheet.
            float4 h2  = RiverRunDecalHash(c + shift + 19.19);
            float pick = h2.x;
            int   at   = (int)(firstPick + min(floor(pick * pickCount), pickCount - 1.0));
            float slot = min(_RiverRunDecalPicks[clamp(at, 0, RIVER_RUN_DECAL_PICKS - 1)],
                             (float)held - 1.0);

            // A quarter turn for the share Rotation Amount asks for, by 90, 180 or 270. Turned
            // about the middle of the decal's own square, which is the cell it is read from, so
            // the turn never carries it out of its cell or changes how far it reaches.
            if (h2.y < rotation)
            {
                int turns = min(1 + (int)floor(h2.z * 3.0), 3);
                local = turns == 1 ? float2(local.y, 1.0 - local.x)
                      : turns == 2 ? 1.0 - local
                                   : float2(1.0 - local.y, local.x);
            }
            float2 cell = float2(fmod(slot, grid.x), floor(slot / grid.x));

            // The mip to read: how many of the decal's own pixels one screen pixel covers. An
            // EXPLICIT level is what makes a read inside all the skipping above legal at all —
            // there is no derivative left for the hardware to want. Held short of the level where
            // a cell has shrunk to nothing, which is where the sheet's cells would run together.
            float lod = log2(max(footprint * cellPix / size, 1e-5));
            lod = clamp(lod, 0.0, log2(cellPix) - 1.0);

            // A tile reads only its drawing, never the transparent padding round it, or every
            // join shows as a hairline. At a smaller mip one texel spans more of the sheet's
            // pixels, so the first texel clear of the padding sits further in; the read stops at
            // the middle of it. Taken at the coarser of the two mips a fractional level blends,
            // which is clear for the finer one too. Held to mips where a cell is still eight
            // texels across, so the two insets never meet in the middle.
            if (tile)
            {
                lod = clamp(lod, 0.0, max(log2(cellPix) - 3.0, 0.0));
                float texel = exp2(ceil(lod));
                float inset = texel * (ceil(RIVER_RUN_DECAL_PADDING / texel) + 0.5);
                local = (inset + local * (cellPix - 2.0 * inset)) / cellPix;
            }

            float2 sheetUV = (cell + local) / grid;
            float4 mark = SAMPLE_TEXTURE2D_LOD(_RiverRunDecalSheet, sampler_RiverRunDecalSheet,
                                               sheetUV, lod);

            // Blended tiles are shared out rather than stacked: the shares add to one, so where
            // they fade into each other neither shows through the other harder than its share.
            if (blendOn)
            {
                over += share * float4(mark.rgb * mark.a, mark.a);
                continue;
            }

            // Laid over what has gathered so far, so two decals crossing stack one on the other
            // rather than adding up into something brighter than either of them.
            over.rgb = mark.rgb * mark.a + over.rgb * (1.0 - mark.a);
            over.a   = mark.a           + over.a   * (1.0 - mark.a);
        }
    }

    return over;
}

// How hard the mix between the three planes is pulled toward whichever the surface faces most.
// Fixed rather than offered as a setting, because it is not a look — it is the one number that
// trades the two faults of this arrangement against each other, and there is a right answer.
//
// At 1 the mix is gentle and a slanted face carries a broad band of two scatters at once, which
// reads as one decal showing faintly through another. Pulled up, that band narrows; pulled up far
// enough the mix stops being a mix and becomes a choice again, and the cut it exists to remove
// comes back. Four holds the crossing to a narrow enough range of angles to pass for a hard
// surface while never actually snapping to one plane.
#define RIVER_RUN_DECAL_SHARPEN 4.0

// Under this share of a pixel, a plane is dropped rather than scattered and multiplied away to
// nothing. It is what keeps the flat rim, the vertical walls and the underside — which is most of
// this stone — scattering exactly once, as they did before there were three planes at all.
#define RIVER_RUN_DECAL_PLANE_FLOOR 0.02

// One finished group, mixed as a single layer, laid onto the colour under it by the group's Blend
// Mode. mixed is premultiplied — rgb already times its coverage in a — so every mode below is
// written in those terms and never has to divide, except the two that need the drawn colour on
// its own. Each mode is faded in by the coverage, so where a decal is not there the base is left
// exactly as it was.
float3 RiverRunDecalBlendOnto(float3 base, float4 mixed, int mode)
{
    float3 c = mixed.rgb;
    float  a = mixed.a;

    if (mode == RIVER_RUN_DECAL_BLEND_MULTIPLY) return base * (1.0 - a + c);
    if (mode == RIVER_RUN_DECAL_BLEND_SCREEN)   return base * (1.0 - c) + c;
    if (mode == RIVER_RUN_DECAL_BLEND_ADD)      return base + c;
    if (mode == RIVER_RUN_DECAL_BLEND_SUBTRACT) return max(base - c, 0.0);

    if (mode == RIVER_RUN_DECAL_BLEND_OVERLAY || mode == RIVER_RUN_DECAL_BLEND_SOFTLIGHT)
    {
        if (a < 1e-4) return base;
        float3 m = saturate(c / a);
        float3 b = saturate(base);

        float3 laid = mode == RIVER_RUN_DECAL_BLEND_OVERLAY
            ? lerp(2.0 * b * m, 1.0 - 2.0 * (1.0 - b) * (1.0 - m), step(0.5, b))
            : (1.0 - 2.0 * m) * b * b + 2.0 * m * b;
        return lerp(base, laid, a);
    }

    return c + base * (1.0 - a);
}

// The same, for a group above the lighting, whose base is not known yet: it is the lit stone
// after the shadow and the glow, which RiverRunDecalsAboveLight lays these on. Every mode offered
// there comes out as base * keep + add, so that is what is handed on — keep and add for this
// group alone, to be folded into what the groups before it made. Overlay and Soft Light cannot be
// written that way, and are laid on as Normal.
void RiverRunDecalBlendAbove(float4 mixed, int mode, out float3 keep, out float3 add)
{
    float3 c = mixed.rgb;
    float  a = mixed.a;

    if      (mode == RIVER_RUN_DECAL_BLEND_MULTIPLY) { keep = 1.0 - a + c; add = 0.0; }
    else if (mode == RIVER_RUN_DECAL_BLEND_SCREEN)   { keep = 1.0 - c;     add = c;   }
    else if (mode == RIVER_RUN_DECAL_BLEND_ADD)      { keep = 1.0;         add = c;   }
    else if (mode == RIVER_RUN_DECAL_BLEND_SUBTRACT) { keep = 1.0;         add = -c;  }
    else                                             { keep = 1.0 - a;     add = c;   }
}

// A line — a seam — laid by its Blend Mode over the decals above the lighting, which arrive as
// add, with share = 1 - keep the part of the lit stone they take the place of. The lit stone
// itself already carries the line, so only the decals' own share is laid on here.
//
// The modes that come out as keep and add are exact. Overlay and Soft Light need the colour
// under them, and the closest there is to one is the decals' own, add / share — exact where a
// mark covers the stone wholly, close where it covers part of it. Where the decals take nothing
// of the lit stone (an Add decal, or none), that falls back to laying the line as Normal.
float3 RiverRunLineOverAbove(float3 add, float3 share, float4 layer, int mode)
{
    float3 k, a;
    RiverRunDecalBlendAbove(layer, mode, k, a);
    float3 lined = add * k + a * share;

    if (mode == RIVER_RUN_DECAL_BLEND_OVERLAY || mode == RIVER_RUN_DECAL_BLEND_SOFTLIGHT)
    {
        float3 under = add / max(share, 1e-4);
        float3 exact = share * RiverRunDecalBlendOnto(under, layer, mode);
        lined = lerp(lined, exact, step(1e-3, share));
    }

    return lined;
}

// The decals over the stone at this pixel, laid on all three of the world's planes and mixed by
// how much this surface faces each, each group then blended by its own Blend Mode.
//
// The three are the world's floor and each of its two walls, flattened the same way the builder
// flattens UV0 so the two describe the same space: a face looking up reads the world's x and z, a
// face looking along x reads z and y, a face looking along z reads x and y.
//
// scale is the face's Designation Scale: every group's spacing and decal sizes are multiplied by
// it, so a Designation scaled up carries bigger marks, further apart, on a grid of its own.
//
// The groups under the lighting are laid straight onto stone, each by its Blend Mode, so a group
// blends with the stone and with every group below it. The groups ticked Appear Above Lighting
// come back as aboveKeep and above, for laying on the lit stone later as lit * aboveKeep +
// above.rgb — above.a is how much they cover, for reference. One loop does both, so a stone
// ticking groups of each kind pays for the planes once.
void RiverRunDecalsOnStone(float3 worldPos, float3 n, int stoneIndex, float scale,
                           inout float3 stone, out float3 aboveKeep, out float4 above)
{
    aboveKeep = float3(1.0, 1.0, 1.0);
    above     = float4(0.0, 0.0, 0.0, 0.0);

    // Which decal groups this stone ticks, one bit each. None, and there is nothing to do.
    float ticked = _RiverRunStoneDecalGroups[stoneIndex];

    // Every derivative taken up here, unconditionally, before a single plane has been weighed.
    // Below this line the code skips planes, and a derivative asked for inside a skip is
    // undefined — the mip it chooses would be whatever the pixels beside it happened to be doing.
    float3 dx = ddx(worldPos);
    float3 dy = ddy(worldPos);

    float footXZ = max(length(float2(dx.x, dx.z)), length(float2(dy.x, dy.z)));
    float footZY = max(length(float2(dx.z, dx.y)), length(float2(dy.z, dy.y)));
    float footXY = max(length(float2(dx.x, dx.y)), length(float2(dy.x, dy.y)));

    // How much this surface faces each of the three. Squared up hard, so a face that is nearly
    // flat is nearly all one plane and only a genuinely slanted one carries two.
    // Held off exactly zero: a component of nothing raised to a power is a thing different
    // compilers answer differently, and it costs nothing to never ask.
    float3 w = pow(max(abs(n), 1e-4), RIVER_RUN_DECAL_SHARPEN);
    w /= max(w.x + w.y + w.z, 1e-5);

    // Drop the planes this surface barely faces, and share their little out among the rest, so
    // dropping them takes nothing off the decals rather than thinning them by a few percent.
    w  = step(RIVER_RUN_DECAL_PLANE_FLOOR, w) * w;
    w /= max(w.x + w.y + w.z, 1e-5);

    if (ticked < 0.5) return;

    // Each ticked group scattered on its own grid and laid over the ones before it, so the later
    // slot sits on top — the C# side fills the slots by Sort Order. The bit is read by arithmetic rather than integer operators,
    // which every target the run graph compiles for answers the same.
    [loop]
    for (int g = 0; g < RIVER_RUN_DECAL_GROUPS; g++)
    {
        if (fmod(floor(ticked / exp2((float)g)), 2.0) < 0.5) continue;

        float4 scatter = _RiverRunDecalGroupScatter[g];
        scatter.xyz   *= scale;
        float4 look    = _RiverRunDecalGroupLook[g];
        float  seed    = _RiverRunDecalGroupSeeds[g];
        float  tiled   = _RiverRunDecalGroupTiled[g];

        // The noise scales with the Designation like the spacing does, so a scaled-up stone's
        // borders wander in the same shape across its bigger tiles.
        float2 tileBlend = _RiverRunDecalGroupTileBlend[g].xy;
        tileBlend.x     *= scale;

        float4 mixed = float4(0.0, 0.0, 0.0, 0.0);

        // Premultiplied answers, so mixing them is a weighted sum and nothing has to be unwound
        // first. The weights add to one, so a pixel wholly covered on every plane it faces stays
        // wholly covered.
        if (w.y > 0.0) mixed += w.y * RiverRunDecals(float2(worldPos.x, worldPos.z), footXZ, scatter, look.y, look.z, look.w, seed, tiled, tileBlend);
        if (w.x > 0.0) mixed += w.x * RiverRunDecals(float2(worldPos.z, worldPos.y), footZY, scatter, look.y, look.z, look.w, seed, tiled, tileBlend);
        if (w.z > 0.0) mixed += w.z * RiverRunDecals(float2(worldPos.x, worldPos.y), footXY, scatter, look.y, look.z, look.w, seed, tiled, tileBlend);

        // Opacity spent once, on the finished mix rather than per plane, so a slanted face fades
        // by exactly as much as a flat one. Both the colour and the coverage are scaled, because
        // the colour is already multiplied by that coverage — scaling one without the other would
        // darken the mark as it faded instead of letting the stone through it. The stone's own
        // Intensity for this group goes on top of the group's. Past 1 it can push the coverage no
        // further than whole, so the coverage is capped there and the colour follows it — up to
        // 1 this is exactly mixed * strength.
        float strength = saturate(look.x) * clamp(RiverRunStoneDecalIntensity(stoneIndex, g), 0.0, 3.0);
        mixed *= saturate(mixed.a * strength) / max(mixed.a, 1e-5);

        // Each kind stacks only on its own kind. Every group above the lighting lies over every
        // group under it whatever their Sort Orders, since the two are laid on at different times.
        int mode = (int)(_RiverRunDecalGroupBlend[g] + 0.5);

        if (_RiverRunDecalGroupAboveLight[g] > 0.5)
        {
            // Folded into what the groups before it made: lit * keep + add, then this group on
            // top of that, is lit * (keep * k) + (add * k + a).
            float3 k, a;
            RiverRunDecalBlendAbove(mixed, mode, k, a);
            aboveKeep *= k;
            above.rgb  = above.rgb * k + a;
            above.a    = mixed.a + above.a * (1.0 - mixed.a);
        }
        else
        {
            stone = RiverRunDecalBlendOnto(stone, mixed, mode);
        }
    }
}

// One normal map group's tiles at one point on ONE of the three planes: the tangent normal's
// xy, -1 to 1 — how far the drawn relief leans along the plane's two axes. Zero is flat.
//
// The same tiling as a Tiled decal group: the same cells, the same pick per cell, the same Blend
// Tiles wander — so a colour group and a normal map group on the same Spacing and Seed line up.
// footprint is handed in for the same reason as there.
float2 RiverRunNormalDecalTiles(float2 uv, float footprint, float spacing, float firstPick,
                                float pickCount, float seed, float2 tileBlend)
{
    float2 grid    = _RiverRunNormalDecalLayout.xy;
    int    held    = (int)(_RiverRunNormalDecalLayout.z + 0.5);
    float  cellPix = _RiverRunNormalDecalLayout.w;

    if (held <= 0 || grid.x < 1.0 || grid.y < 1.0 || cellPix < 1.0) return float2(0.0, 0.0);
    if (spacing <= 0.0 || pickCount < 0.5)                           return float2(0.0, 0.0);

    float2 shift = seed * float2(37.0, 59.0);
    float2 home  = floor(uv / spacing);
    int    hi    = 0;

    // Blend Tiles: exactly as RiverRunDecals does it — the choice of tile wanders, the reads stay
    // square on the grid.
    bool   blendOn = tileBlend.x > 0.0;
    float2 blend   = float2(0.0, 0.0);
    if (blendOn)
    {
        float2 n = uv / tileBlend.x + shift;
        float2 push = float2(RiverRunGradientNoise(n),
                             RiverRunGradientNoise(n + float2(17.31, -41.7)));
        float2 q = uv / spacing + push * RIVER_RUN_DECAL_TILE_WANDER - 0.5;

        float soft = max(saturate(tileBlend.y) * 0.5, 1e-3);
        blend = smoothstep(0.5 - soft, 0.5 + soft, frac(q));
        home  = floor(q);
        hi    = 1;
    }

    // The mip, and the inset that keeps every read clear of the padding round the cell — the
    // tile's own, from RiverRunDecals.
    float lod = log2(max(footprint * cellPix / spacing, 1e-5));
    lod = clamp(lod, 0.0, max(log2(cellPix) - 3.0, 0.0));
    float texel = exp2(ceil(lod));
    float inset = texel * (ceil(RIVER_RUN_DECAL_PADDING / texel) + 0.5);

    float2 lean = float2(0.0, 0.0);

    for (int y = 0; y <= hi; y++)
    {
        for (int x = 0; x <= hi; x++)
        {
            float2 c = home + float2(x, y);

            float  share = 1.0;
            float2 local;
            if (blendOn)
            {
                share = (x == 0 ? 1.0 - blend.x : blend.x) * (y == 0 ? 1.0 - blend.y : blend.y);
                if (share < 1e-4) continue;
                local = frac(uv / spacing);
            }
            else
            {
                local = saturate(uv / spacing - c);
            }

            float4 h2   = RiverRunDecalHash(c + shift + 19.19);
            int    at   = (int)(firstPick + min(floor(h2.x * pickCount), pickCount - 1.0));
            float  slot = min(_RiverRunNormalDecalPicks[clamp(at, 0, RIVER_RUN_NORMAL_DECAL_PICKS - 1)],
                              (float)held - 1.0);
            float2 cell = float2(fmod(slot, grid.x), floor(slot / grid.x));

            local = (inset + local * (cellPix - 2.0 * inset)) / cellPix;
            float3 drawn = SAMPLE_TEXTURE2D_LOD(_RiverRunNormalDecalSheet, sampler_RiverRunNormalDecalSheet,
                                                (cell + local) / grid, lod).rgb;

            // Shared out like blended colour tiles, so where two fade into each other the lean is
            // the mix of the two rather than the sum.
            lean += share * (drawn.xy * 2.0 - 1.0);
        }
    }

    return lean;
}

// This stone's normal, bent by the normal map groups it ticks. n is the surface's own normal,
// normalised; scale its Designation Scale, which multiplies the tiles like the decals'.
//
// Laid on the same three planes as the decals and mixed by the same weights. Each plane's lean is
// along that plane's own two world axes and simply added to the normal — so it needs no tangents,
// and a face and the face behind it (the underside of the rim, say) lean the same correct way,
// since the relief is read off the world rather than off a UV that flips.
float3 RiverRunNormalDecalsOnStone(float3 worldPos, float3 n, int stoneIndex, float scale)
{
    float ticked = _RiverRunStoneNormalDecalGroups[stoneIndex];

    // Taken up here, unconditionally, for the reason in RiverRunDecalsOnStone.
    float3 dx = ddx(worldPos);
    float3 dy = ddy(worldPos);

    float footXZ = max(length(float2(dx.x, dx.z)), length(float2(dy.x, dy.z)));
    float footZY = max(length(float2(dx.z, dx.y)), length(float2(dy.z, dy.y)));
    float footXY = max(length(float2(dx.x, dx.y)), length(float2(dy.x, dy.y)));

    float3 w = pow(max(abs(n), 1e-4), RIVER_RUN_DECAL_SHARPEN);
    w /= max(w.x + w.y + w.z, 1e-5);
    w  = step(RIVER_RUN_DECAL_PLANE_FLOOR, w) * w;
    w /= max(w.x + w.y + w.z, 1e-5);

    if (ticked < 0.5) return n;

    float3 bend = float3(0.0, 0.0, 0.0);

    [loop]
    for (int g = 0; g < RIVER_RUN_NORMAL_DECAL_GROUPS; g++)
    {
        if (fmod(floor(ticked / exp2((float)g)), 2.0) < 0.5) continue;

        float4 tiles    = _RiverRunNormalDecalGroupTiles[g];
        float4 blend    = _RiverRunNormalDecalGroupBlend[g];
        float  spacing  = tiles.x * scale;
        float  strength = tiles.y;
        float2 tileBlend = float2(blend.x * scale, blend.y);
        float  seed     = blend.z;

        if (strength <= 0.0) continue;

        // The planes as the decals flatten them: up-facing reads x and z, x-facing z and y,
        // z-facing x and y — each lean put back along the two world axes it was read across.
        float2 t;
        if (w.y > 0.0)
        {
            t = RiverRunNormalDecalTiles(float2(worldPos.x, worldPos.z), footXZ, spacing, tiles.z, tiles.w, seed, tileBlend);
            bend += w.y * strength * float3(t.x, 0.0, t.y);
        }
        if (w.x > 0.0)
        {
            t = RiverRunNormalDecalTiles(float2(worldPos.z, worldPos.y), footZY, spacing, tiles.z, tiles.w, seed, tileBlend);
            bend += w.x * strength * float3(0.0, t.y, t.x);
        }
        if (w.z > 0.0)
        {
            t = RiverRunNormalDecalTiles(float2(worldPos.x, worldPos.y), footXY, spacing, tiles.z, tiles.w, seed, tileBlend);
            bend += w.z * strength * float3(t.x, t.y, 0.0);
        }
    }

    float3 bent = n + bend;
    return dot(bent, bent) > 1e-8 ? normalize(bent) : n;
}

// How much of the seam the Seam Mask takes away here: 0 none, 1 all of it. Read before anything
// can return early, so the derivatives and mips it takes come from neighbouring pixels that all
// read it too. toSeam is metres to the nearest seam, extent how far this face's seam reaches.
//
// World: each plane read straight off the world, the same planes as the decals, and mixed by how
// much the surface faces each.
//
// Along Seam: the texture's middle sits on the seam and its width reaches out to the extent, so
// across is the seam's own distance — 0.5 on the seam, 1 at the extent. Only the distance is
// baked, not which side of the seam a face is on, so both sides take the right half. Along is
// read off the WORLD rather than off the seam: which way the seam runs is worked out here from how
// the distance changes from pixel to pixel (it climbs straight away from the seam, so the seam
// runs square to that), and the texture is read once along each world axis and mixed by how much
// the seam runs along each. A world axis never jumps, so the mask flows on round a curved run
// rather than restarting at every piece of it; a seam running diagonally takes two readings
// crossed over each other.
float RiverRunSeamMask(float3 worldPos, float3 n, float toSeam, float extent)
{
    if (_RiverRunSeamMaskRepeats <= 0.0) return 0.0;

    float3 p = worldPos * _RiverRunSeamMaskRepeats;

    if (_RiverRunSeamMaskMode.z < 0.5)
    {
        float3 w = pow(max(abs(n), 1e-4), RIVER_RUN_DECAL_SHARPEN);
        w /= max(w.x + w.y + w.z, 1e-5);

        float xz = SAMPLE_TEXTURE2D(_RiverRunSeamMask, sampler_RiverRunSeamMask, p.xz).a;
        float zy = SAMPLE_TEXTURE2D(_RiverRunSeamMask, sampler_RiverRunSeamMask, p.zy).a;
        float xy = SAMPLE_TEXTURE2D(_RiverRunSeamMask, sampler_RiverRunSeamMask, p.xy).a;

        return saturate(w.y * xz + w.x * zy + w.z * xy);
    }

    // Which way the distance climbs across the surface, in the world: the screen's two steps in
    // the world and in the distance, solved for the one direction in the surface that gives both.
    // Only the direction is wanted, and the solve's divisor is a square (always positive), so it
    // is left off — a far-off pixel's steps are tiny, and dividing by their square would be lost
    // in the float. The surface is the one the derivatives see, so it agrees with them exactly.
    float3 dPx = ddx(worldPos);
    float3 dPy = ddy(worldPos);
    float  dDx = ddx(toSeam);
    float  dDy = ddy(toSeam);

    float3 face   = cross(dPx, dPy);
    float  faceSq = dot(face, face);
    if (faceSq <= 1e-30 || extent <= 0.0) return 0.0;
    face *= rsqrt(faceSq);

    float3 away = dDx * cross(dPy, face) + dDy * cross(face, dPx);

    // The seam runs square to that, in the surface. Where the distance does not change at all
    // there is no telling, and the mask stays off rather than guessing.
    float3 along   = cross(face, away);
    float  alongSq = dot(along, along);
    if (alongSq <= 1e-30) return 0.0;
    along *= rsqrt(alongSq);

    float3 w = pow(max(abs(along), 1e-4), RIVER_RUN_DECAL_SHARPEN);
    w /= max(w.x + w.y + w.z, 1e-5);

    float u = 0.5 + 0.5 * saturate(toSeam / extent);

    float onX = SAMPLE_TEXTURE2D(_RiverRunSeamMask, sampler_RiverRunSeamMask, float2(u, p.x)).a;
    float onY = SAMPLE_TEXTURE2D(_RiverRunSeamMask, sampler_RiverRunSeamMask, float2(u, p.y)).a;
    float onZ = SAMPLE_TEXTURE2D(_RiverRunSeamMask, sampler_RiverRunSeamMask, float2(u, p.z)).a;

    return saturate(w.x * onX + w.y * onY + w.z * onZ);
}

// What is left of a seam once the mask has had its way. ramp is the seam's own shape, 1 on the
// corner falling to 0 at the edge of its extent, before its strength; mask is RiverRunSeamMask.
//
// Erode is a threshold walked up the ramp: the seam stands only where its ramp is above the mask.
// The threshold starts one softness BELOW nothing, so a mask of 0 leaves the seam exactly whole,
// and ends at 1, so a mask of 1 takes all of it — in between, the faint outer edge goes first.
//
// Fade And Erode multiplies the two, so what the erode leaves standing is dimmed by the fade too.
float RiverRunSeamMaskKeep(float ramp, float mask)
{
    float keep = _RiverRunSeamMaskMode.y > 0.5 ? 1.0 - mask : 1.0;

    float soft = _RiverRunSeamMaskMode.x;
    if (soft > 0.0)
    {
        float threshold = mask * (1.0 + soft) - soft;
        keep *= saturate((ramp - threshold) / soft);
    }
    return keep;
}

// How far across the whole piece this pixel is along a gradient's direction, 0 where it starts
// and 1 where it ends. The piece is the renderer's bounds in the world; a piece with no extent
// that way at all sits in the middle of the gradient.
//
// A face in a face group carries its place in the GROUP on UV3 instead (see
// RIVER_RUN_GROUP_MARK_Z), worked out over every mesh the group spans — so the gradient runs
// once across the group rather than starting again in each separate mesh it is built in.
float RiverRunGradientT(float3 worldPos, float direction, float4 gradientData)
{
    bool across = direction > 2.5;
    float t;

    if (abs(gradientData.z - RIVER_RUN_GROUP_MARK_Z) < 0.01 && abs(gradientData.w - RIVER_RUN_GROUP_MARK_W) < 0.01)
    {
        t = saturate(across ? gradientData.x : gradientData.y);
    }
    else
    {
        float3 lo = unity_RendererBounds_Min.xyz;
        float3 hi = unity_RendererBounds_Max.xyz;

        float span = across ? hi.x - lo.x : hi.y - lo.y;
        if (span <= 1e-5) return 0.5;

        t = saturate(across ? (worldPos.x - lo.x) / span : (worldPos.y - lo.y) / span);
    }

    // 1 bottom to top and 3 left to right run as measured; 2 and 4 run back the other way.
    bool reversed = (direction > 1.5 && direction < 2.5) || direction > 3.5;
    return reversed ? 1.0 - t : t;
}

// The colour this part of the run is drawn in. A face carrying no kind at all — a mesh built
// before the builder was baking one — keeps whatever the graph handed in, and so does one whose
// colour nobody has pushed yet.
float3 RiverRunStone(float4 faceData, float3 worldPos, float3 baseColour, float4 gradientData)
{
    // UV2.w is 1 or more on a piece that really carries face kinds — 1 plus the Designation the
    // face was built as, which RiverRunDesignationScale reads.
    // It is here because a channel the mesh does not have does NOT arrive as zero: it comes
    // through carrying whatever was left in the stream, which on these pieces is the seam data
    // — and metres-to-the-nearest-seam rounds to 1, then 2, then 3 as it climbs, so a run that
    // has not been rebuilt draws itself in bands of all three colours at once. Reading the flag
    // rather than the kind is what tells a piece with no kinds from a piece standing three
    // metres from its nearest seam.
    if (faceData.w < 0.5) return baseColour;

    int    index  = RiverRunStoneIndex(faceData);
    float4 chosen = _RiverRunStoneColours[index];
    if (chosen.a <= 0.0) return baseColour;

    float direction = _RiverRunStoneGrains[index].z;
    if (direction < 0.5) return chosen.rgb;

    // Half a step in from each end, so the two ends of the row read as the gradient's own ends
    // rather than as halfway to the next pixel. Mip 0: the sheet has no others, and a level the
    // hardware picked from derivatives would jump wherever the stone type changes across a face.
    float  t  = RiverRunGradientT(worldPos, direction, gradientData);
    float2 uv = float2((0.5 + t * (RIVER_RUN_GRADIENT_W - 1.0)) / RIVER_RUN_GRADIENT_W,
                       (index + 0.5) / RIVER_RUN_STONE_TYPES);
    return SAMPLE_TEXTURE2D_LOD(_RiverRunStoneGradients, sampler_RiverRunStoneGradients, uv, 0).rgb;
}

// Half lambert: 1 square on to the light, 0.5 side on, 0 turned right away. Strength lerps out
// of it rather than scaling it, so 0 leaves the stone at full and unlit rather than at black.
// Past 1 the fade is done and the rest multiplies the lit result, up to 10x brighter.
float RiverRunHalfLambert(float3 n, float3 worldPos, float3 lightPos, float strength)
{
    // Which way this pixel would have to look to see the light. Normalised because it is only the
    // direction that is wanted — the distance says nothing here.
    float3 toLight = lightPos - worldPos;
    toLight = dot(toLight, toLight) > 1e-8 ? normalize(toLight) : float3(0.0, 1.0, 0.0);

    float ndl = dot(n, toLight) * 0.5 + 0.5;
    return lerp(1.0, saturate(ndl), saturate(strength)) * max(strength, 1.0);
}

// How the stone is shaped by light. Outside, by the made-up light. Inside, by the boat in the same
// way, with the street lights added on the faces turned to them.
float RiverRunLight(float3 n, float3 worldPos)
{
#if defined(_RIVER_RUN_INTERIOR)
    float lamps, lampsNear;
    InstancedLights_float(worldPos, n, lamps, lampsNear);
    return RiverRunHalfLambert(n, worldPos, _RiverRunBoatLight.xyz, _RiverRunInterior.y)
         + lamps * _RiverRunInterior.z;
#else
    return RiverRunHalfLambert(n, worldPos, _LevelSelectLightPosition.xyz, _LevelSelectLightStrength);
#endif
}

// Metres above the water. Outside, off the rim: w on the seam data is zero on the rim top and
// negative under it, and the water lies WaterDepth beneath the rim, so the two add. Inside, off
// the real water surface, so the band rides the waves and follows the water level as it moves.
float RiverRunAboveWater(float4 seamData, float3 worldPos)
{
#if defined(_RIVER_RUN_INTERIOR)
    float surfaceY, below;
    WaterSurfaceHeight_float(worldPos, 0.01, surfaceY, below);
    return worldPos.y - surfaceY;
#else
    return seamData.w + _RiverRunWaterDepth;
#endif
}

// 1 where the stone can be seen, 0 where it has fallen into the dark. Only inside: round the boat
// out to its light's radius, then fading over the softness, and anywhere a street light reaches.
// The same shape as the spline walls' darkness, so stone and wall go dark together.
float RiverRunSeen(float3 n, float3 worldPos)
{
#if defined(_RIVER_RUN_INTERIOR)
    float  radius = _RiverRunBoatLight.w;
    float  boat   = 1.0 - smoothstep(radius, radius + max(_RiverRunInterior.x, 1e-4),
                                     length(worldPos - _RiverRunBoatLight.xyz));
    float lamps, lampsNear;
    InstancedLights_float(worldPos, n, lamps, lampsNear);
    return max(boat, saturate(lampsNear));
#else
    return 1.0;
#endif
}

// The stone before the dark is laid over it — everything RiverRunShading_float hands back.
void RiverRunShadeStone(
    float4 SeamData,
    float3 WorldNormal,
    float3 WorldPos,
    float3 BaseColour,
    float2 GrainUV,
    float4 FaceData,
    float4 GradientData,
    out float3 Colour,
    out float3 Tint,
    out float  Blend,
    out float  Seam,
    out float  Waterline,
    out float  Light,
    out float4 DecalsAbove,
    out float3 DecalsAboveKeep,
    out float3 Normal)
{
    // Interpolated across a face a normal comes out shorter than one unit, which would read as the
    // surface having turned further from the light than it really has.
    float3 n = normalize(WorldNormal);

    // The Designation's Scale goes on the grain, the decals, the normal maps and the seam extent alike.
    float  scale = RiverRunDesignationScale(FaceData);

    // The normal bent by this stone's normal map groups — the one the light is worked out from,
    // and handed out for the River Glow. Everything else here (which plane a decal is laid on, the
    // seam mask, which faces take the waterline) goes on reading the surface's own n, so the
    // relief changes how the stone is lit and nothing about where things sit on it.
    Normal = RiverRunNormalDecalsOnStone(WorldPos, n, RiverRunStoneIndex(FaceData), scale);

    Light = RiverRunLight(Normal, WorldPos);

    // The stone under everything else, and the grain that goes over the top of the lot. The
    // grain is worked out here and spent at the very end: it is the last thing laid on, so it
    // runs across the seams and the waterline as well as across the bare stone. Under them it
    // would only be visible on whatever the two bands left uncovered, which on a run drawn with
    // any real extent is not much of it.

    float3 stone = RiverRunStone(FaceData, WorldPos, BaseColour, GradientData);
    float  grain = RiverRunGrain(GrainUV, FaceData, scale);

    // The hand-drawn marks, laid on the bare stone before any of the rest of it. Under the light
    // so the stone's own shaping falls across them, under the seams so a corner's ink line cuts
    // over them, and under the grain so the surface runs through them — a mark ON the stone,
    // rather than something floating in front of it.
    //
    // Off the world position and the normal rather than off GrainUV, which the grain still
    // uses: UV0 is flattened per triangle, and a drawing laid across two triangles that
    // flattened onto different planes is cut along the edge between them.
    //
    // Scattered with this face's own stone type's numbers, so each stone carries its own.
    //
    // The groups ticked Appear Above Lighting are not laid on here. They go out on DecalsAbove,
    // still taking the seams, the waterline and the grain over them exactly as these do, so the
    // light is the only thing they miss.
    //
    // They come back as keep and add, the lit stone to be laid on as lit * keep + add. 1 - keep
    // is how much of the lit stone they take the place of, and stands in below wherever the
    // coverage did when every group was Normal.
    int    stoneIndex = RiverRunStoneIndex(FaceData);
    float4 decalsAbove;
    float3 aboveKeep;
    RiverRunDecalsOnStone(WorldPos, n, stoneIndex, scale, stone, aboveKeep, decalsAbove);
    DecalsAbove     = float4(decalsAbove.rgb * grain, decalsAbove.a);
    DecalsAboveKeep = aboveKeep;

    // This face's own stone type's seams — where two types meet, each side draws its own.
    float4 seams      = _RiverRunStoneSeams[stoneIndex];

    // Metres to the nearest seam. A slot with no seam in it arrives carrying a number far too
    // big to ever be the smallest, so it simply drops out of this.
    float toSeam = min(min(SeamData.x, SeamData.y), SeamData.z);

    // The extent in metres for this face: the percentage of its own surface's width. A piece
    // built before the widths were baked carries no face data, and gets no seams until rebuilt.
    float surfaceWidth = FaceData.w >= 0.5 ? FaceData.y : 0.0;
    float seamExtent   = _RiverRunStoneGrains[stoneIndex].w * 0.01 * surfaceWidth * scale
                       * RiverRunRunSeamExtent(FaceData);

    // Up here, before the early way out below, because it takes derivatives.
    float seamMask = RiverRunSeamMask(WorldPos, n, toSeam, seamExtent);

    Colour    = stone * Light * grain;
    Tint      = seams.rgb;
    Blend     = 0.0;
    Seam      = 0.0;
    Waterline = 0.0;

    // A piece generated before the seams were being baked has no UV1 at all, and a missing
    // channel reads as zeros — which would otherwise say "every corner is sitting on a seam" and
    // flood the whole run with the seam colour. On a piece that really was baked at least one of
    // the three slots is above zero, because no triangle has three different seams running
    // through all three of its corners. So all zero means no data, and no data means leave the
    // stone alone: an un-rebuilt run then looks exactly like one with the effect turned off,
    // which is a far easier thing to recognise than a river gone flat black.
    if (max(SeamData.x, max(SeamData.y, SeamData.z)) <= 0.0) return;

    float seamRamp = RiverRunRamp(toSeam, seamExtent);
    Seam = saturate(seamRamp * seams.w) * RiverRunSeamMaskKeep(seamRamp, seamMask);

    // How far above the water this pixel is. Below the water the ramp holds at full rather than
    // falling away, which is what keeps a second line from being drawn along the surface itself
    // where the band meets it.
    float above  = RiverRunAboveWater(SeamData, WorldPos);

    // Outside, only the channel ever stands in the water, so only faces looking up take the band.
    // Inside, the water runs right up to the stone's edges, so every face it meets takes it.
#if defined(_RIVER_RUN_INTERIOR)
    float facing = 1.0;
#else
    float facing = saturate(n.y / RIVER_RUN_UP_FACING);
#endif

    Waterline = saturate(RiverRunRamp(above, _RiverRunWaterlineExtent)
                       * _RiverRunWaterlineStrength * facing);

    // White laid over black, so where the two overlap — the corner the rim makes with the channel,
    // when both are drawn wide — the white is the one that reads.
    Blend = Seam + Waterline - Seam * Waterline;
    Tint  = Blend > 1e-5
          ? (seams.rgb * Seam * (1.0 - Waterline)
             + _RiverRunWaterlineColour.rgb * Waterline) / Blend
          : seams.rgb;

    // Laid over the lit stone rather than under it. A seam is an ink line where two faces meet
    // rather than a surface of its own, so a black seam stays black on the lit side of a run
    // instead of brightening with it. The same goes for the water's white. The grain then goes
    // over the whole composite — stone, seam and waterline alike.
    //
    // The seam by Seam Blend Mode, then the waterline over it as Normal. With the seam Normal this
    // is exactly the one lerp by Tint and Blend above, which still go out as they were.
    int    seamMode  = (int)(_RiverRunSeamBlend + 0.5);
    float4 seamLayer = float4(seams.rgb * Seam, Seam);
    float3 waterTint = _RiverRunWaterlineColour.rgb;
    Colour = lerp(RiverRunDecalBlendOnto(stone * Light, seamLayer, seamMode), waterTint, Waterline)
           * grain;

    // The same lines over the decals above the lighting, on only what the decals take the place
    // of, 1 - keep: the lit stone under them already carries the lines — and a seam crossing half
    // a mark only covers that half.
    float3 aboveShare = 1.0 - aboveKeep;
    DecalsAbove.rgb = lerp(RiverRunLineOverAbove(decalsAbove.rgb, aboveShare, seamLayer, seamMode),
                           waterTint * aboveShare, Waterline) * grain;

    // Seams Above Lighting: the stone and the decals above it take only the waterline, and the
    // seam goes out on DecalsAbove instead, laid over the lot after the shadow and the glow. Its
    // coverage leaves out what the waterline holds, so the white still reads over the black where
    // the two meet, exactly as when both go into the stone.
    //
    // Laid on by Seam Blend Mode, carried as keep and add like a decal group above the lighting,
    // so Overlay and Soft Light are laid on as Normal here.
    if (_RiverRunSeamsAboveLight > 0.5)
    {
        Colour      = lerp(stone * Light, waterTint, Waterline) * grain;
        DecalsAbove = float4(lerp(decalsAbove.rgb, waterTint * aboveShare, Waterline) * grain,
                             decalsAbove.a);

        float  seamOver = Seam * (1.0 - Waterline);
        float3 seamKeep, seamAdd;
        RiverRunDecalBlendAbove(float4(seams.rgb * grain * seamOver, seamOver), seamMode,
                                seamKeep, seamAdd);
        DecalsAbove     = float4(DecalsAbove.rgb * seamKeep + seamAdd,
                                 seamOver + DecalsAbove.a * (1.0 - seamOver));
        DecalsAboveKeep = aboveKeep * seamKeep;
    }
}

// Light     : 1 square on to the light, 0 turned right away from it, and flat 1 when the light is
//             turned off — so it can be multiplied into anything without checking first. Inside,
//             the boat is the light, with the street lights added.
// Seam      : 0 well away from a corner, 1 sitting on one.
// Waterline : 0 above the band, 1 at the water and below it — held rather than falling away, so
//             there is no second line drawn where the band meets the surface.
// Tint/Blend: the two gradients composited, white over black, ready for one Lerp.
// Colour    : the stone — this face's stone type, grained and lit — with that Lerp
//             already done, for wiring the node straight inline. Inside, faded into the dark away
//             from the boat and the street lights, seams and waterline with it. Outside, with the
//             boat's light added round the boat (LevelSelectBoatLight.hlsl).
// DecalsAbove: the decal groups ticked Appear Above Lighting, what they add in rgb and how much
//             they cover in a. Seamed and grained like the stone but never lit, and never faded
//             into the dark inside either — for RiverRunDecalsAboveLight, after the shadow and
//             the glow.
// DecalsAboveKeep: how much of the lit stone those groups leave, per channel — 1 where there are
//             none. The lit stone comes out as lit * DecalsAboveKeep + DecalsAbove.rgb, which is
//             what lets them blend by Multiply, Screen, Add and Subtract as well as Normal.
// Normal    : the world normal bent by the stone's normal map groups, normalised — the surface's
//             own where it ticks none. For the River Glow's Normal, so the glow falls across the
//             relief as the light does. The Level Select Shadow keeps the plain Normal Vector: it
//             only lifts the point it looks up off the surface, and the relief is no surface.
void RiverRunShading_float(
    float4 SeamData,
    float3 WorldNormal,
    float3 WorldPos,
    float3 BaseColour,
    float2 GrainUV,
    float4 FaceData,
    float4 GradientData,
    out float3 Colour,
    out float3 Tint,
    out float  Blend,
    out float  Seam,
    out float  Waterline,
    out float  Light,
    out float4 DecalsAbove,
    out float3 DecalsAboveKeep,
    out float3 Normal)
{
    RiverRunShadeStone(SeamData, WorldNormal, WorldPos, BaseColour, GrainUV, FaceData, GradientData,
                       Colour, Tint, Blend, Seam, Waterline, Light, DecalsAbove, DecalsAboveKeep,
                       Normal);

    // Laid over everything, after the early way out a piece with no seam data takes, so an
    // un-rebuilt piece still goes dark with the rest. The boat's light faces the bent normal like
    // the made-up light does; how far into the dark the stone is seen does not.
#if defined(_RIVER_RUN_INTERIOR)
    Colour = lerp(_RiverRunInteriorDark.rgb, Colour, RiverRunSeen(normalize(WorldNormal), WorldPos));
#else
    Colour = LevelSelectBoatLightOnto(Colour, Normal, WorldPos, 1.0);
#endif
}

// Shader Graph appends _float or _half depending on graph precision, and a File custom function
// has to supply whichever it asks for. Forwards to the float version — the maths is a handful of
// multiplies and there is nothing here worth halving.
void RiverRunShading_half(
    half4 SeamData,
    half3 WorldNormal,
    half3 WorldPos,
    half3 BaseColour,
    half2 GrainUV,
    half4 FaceData,
    half4 GradientData,
    out half3 Colour,
    out half3 Tint,
    out half  Blend,
    out half  Seam,
    out half  Waterline,
    out half  Light,
    out half4 DecalsAbove,
    out half3 DecalsAboveKeep,
    out half3 Normal)
{
    float3 c, t, k, nb;
    float  b, s, w, l;
    float4 d;
    RiverRunShading_float(SeamData, WorldNormal, WorldPos, BaseColour, GrainUV, FaceData, GradientData,
                          c, t, b, s, w, l, d, k, nb);
    Normal    = (half3)nb;
    Colour    = (half3)c;
    Tint      = (half3)t;
    Blend     = (half)b;
    Seam      = (half)s;
    Waterline = (half)w;
    Light     = (half)l;
    DecalsAbove = (half4)d;
    DecalsAboveKeep = (half3)k;
}

// The decals ticked Appear Above Lighting laid over the finished, lit stone: wired in after the
// Level Select Shadow and the River Glow, so neither of them, nor the light, reaches the marks.
// Decals and Keep are River Run Shading's DecalsAbove and DecalsAboveKeep. Held off below zero,
// where a Subtract group can take more than there was.
void RiverRunDecalsAboveLight_float(float3 Colour, float4 Decals, float3 Keep, out float3 Out)
{
    Out = max(Colour * Keep + Decals.rgb, 0.0);
}

void RiverRunDecalsAboveLight_half(half3 Colour, half4 Decals, half3 Keep, out half3 Out)
{
    Out = max(Colour * Keep + Decals.rgb, 0.0);
}

#endif
