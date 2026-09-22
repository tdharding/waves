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
// ── Which part of the run a face is ──────────────────────────────────────────
// Three colours rather than one: the OUTER faces a run shows the world — the outer walls, the
// underside, the open ends — the RIM lip laid flat round the top, and the INNER faces of the
// channel the water runs down.
//
// Like the seams, that cannot be answered here. A steep channel wall and an outer wall are both
// very nearly vertical, and telling them apart needs to know which side of the piece a face is
// on, which is a thing only the builder knows. So it is settled as the piece is generated and
// baked into UV2.x — 1 outer, 2 rim, 3 inner, and 0 for a mesh built before any of this, which
// is left with the colour it came in with. UV2.y is the width of the surface the face belongs to,
// which the seam extent is a percentage of.
//
// ── Why the waterline is measured off the rim and not off the world ──────────
// The water out here is not one flat sheet. It is laid in each river's channel a fixed distance
// under that river's rim top, so a run that climbs carries its water up with it. Measure the band
// off a world height and it would slide off the water the moment the river left the flat; measure
// it off the rim, as w does, and it stays glued to the surface wherever the river goes.
//
// ── The grain ────────────────────────────────────────────────────────────────
// Gradient noise — the same noise, the same hash and the same quintic fade the Gradient Noise
// node draws, moved in here off the graph. In here because the stone's colour is now three
// colours chosen per face, and a grain laid on outside would have to be laid on before that
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

float4 _RiverRunSeamColour;
float  _RiverRunSeamStrength;

// A PERCENTAGE, not metres: how far the gradient reaches off a seam as a share of the width of
// the surface it runs across, which the builder bakes into UV2.y. One number for every piece, so a
// wide river and a thin one, a tall wall and a tower stem are each shaded in proportion to
// themselves.
float  _RiverRunSeamExtent;

float4 _RiverRunWaterlineColour;
float  _RiverRunWaterlineStrength;
float  _RiverRunWaterlineExtent;

// The stone itself, one colour for each part of a run. Alpha is not a colour here — it is how a
// colour that has been pushed is told from the zeroes a shader reimport leaves behind.
float4 _RiverRunOuterColour;
float4 _RiverRunRimColour;
float4 _RiverRunInnerColour;

// How wide one cell of the grain is, in the units the surface is cut in, and how much of the
// noise reaches the stone. Either of them at zero is no grain at all.
// One grain per part of the stone — x outer, y rim, z inner, the same order as the face kinds.
float4 _RiverRunGrainSizes;
float4 _RiverRunGrainStrengths;

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

// One candidate place every Spacing metres; Amount is how many of those places are taken, 0 to 1.
// Scale is the metres across the longest side of a decal, drawn between the two for each place.
float _RiverRunDecalSpacing;
float _RiverRunDecalScaleMin;
float _RiverRunDecalScaleMax;
float _RiverRunDecalAmount;

// How much of the stone underneath a decal hides, 0 to 1. A different thing from Amount:
// that decides how MANY marks there are, this how far each of the ones there are sinks into
// the stone. Turned down, every decal is still where it was and still the size it was —
// the stone simply shows through it.
float _RiverRunDecalOpacity;

// How far the water lies beneath the rim top — the world's Water Level, pushed alongside the rest
// rather than authored a second time.
float  _RiverRunWaterDepth;

// Where the made-up light stands, in world space. Every pixel works out its own direction to it.
// Authored once for the world in the designer's Aesthetics, and shared with the landscape hills.
float4 _LevelSelectLightPosition;
float  _RiverRunLightStrength;

// How far off flat a face may point and still count as facing up — the whole of the channel does,
// and the outer walls, which stand exactly vertical, do not.
#define RIVER_RUN_UP_FACING 0.15

// What UV2.x says a face is. The builder writes these; 0 is a mesh that predates them.
#define RIVER_RUN_FACE_OUTER 1
#define RIVER_RUN_FACE_RIM   2
#define RIVER_RUN_FACE_INNER 3

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
// Each part of the stone has its own grain, picked by the face kind. A face carrying no kind — a
// mesh built before they were baked — takes the outer grain.
float RiverRunGrain(float2 uv, float4 faceData)
{
    int   kind     = faceData.w < 0.5 ? RIVER_RUN_FACE_OUTER : (int)(faceData.x + 0.5);
    float size     = kind == RIVER_RUN_FACE_RIM   ? _RiverRunGrainSizes.y
                   : kind == RIVER_RUN_FACE_INNER ? _RiverRunGrainSizes.z
                                                  : _RiverRunGrainSizes.x;
    float strength = kind == RIVER_RUN_FACE_RIM   ? _RiverRunGrainStrengths.y
                   : kind == RIVER_RUN_FACE_INNER ? _RiverRunGrainStrengths.z
                                                  : _RiverRunGrainStrengths.x;

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
float4 RiverRunDecals(float2 uv, float footprint)
{
    float4 nothing = float4(0.0, 0.0, 0.0, 0.0);

    float2 grid    = _RiverRunDecalLayout.xy;
    int    held    = (int)(_RiverRunDecalLayout.z + 0.5);
    float  cellPix = _RiverRunDecalLayout.w;

    float spacing = _RiverRunDecalSpacing;
    float amount  = _RiverRunDecalAmount;
    float largest = max(_RiverRunDecalScaleMin, _RiverRunDecalScaleMax);
    float small   = min(_RiverRunDecalScaleMin, _RiverRunDecalScaleMax);

    if (held <= 0 || grid.x < 1.0 || grid.y < 1.0 || cellPix < 1.0) return nothing;
    if (spacing <= 0.0 || amount <= 0.0 || largest <= 0.0)          return nothing;

    // How many rings of neighbouring cells could be holding a decal that reaches this pixel. A
    // decal stands somewhere inside its own cell and reaches half its own width past it, so
    // the ring next door always counts and a further ring only once a decal is wider than two
    // cells. Held to three rings: past six times the spacing the biggest decals are cut off
    // square rather than every pixel in the world paying for a search that wide.
    //
    // Worked out from the uniforms rather than from anything about this pixel, so every pixel
    // in the world takes the same number of turns round the loop and none of them diverges.
    int rings = clamp(1 + (int)floor(largest / (2.0 * spacing)), 1, 3);

    // Which cell this pixel is standing in.
    float2 home = floor(uv / spacing);

    float4 over = nothing;

    for (int y = -rings; y <= rings; y++)
    {
        for (int x = -rings; x <= rings; x++)
        {
            float2 c = home + float2(x, y);
            float4 h = RiverRunDecalHash(c);

            // A place that loses the draw holds nothing, and its neighbours do not close over
            // the gap. That is what makes Amount thin the scatter rather than tighten it.
            if (h.x > amount) continue;

            float size = lerp(small, largest, h.w);
            if (size <= 1e-5) continue;

            // Anywhere inside its own cell.
            float2 centre = (c + h.yz) * spacing;

            // Where this pixel falls across the decal, 0 to 1 either way. Outside that the decal
            // is not here, and the cell is dropped before it is ever read.
            float2 local = (uv - centre) / size + 0.5;
            if (any(local < 0.0) || any(local > 1.0)) continue;

            // Which of the sheet's drawings this place drew, and where that cell sits on it.
            float pick = RiverRunDecalHash(c + 19.19).x;
            float slot = min(floor(pick * (float)held), (float)held - 1.0);
            float2 cell = float2(fmod(slot, grid.x), floor(slot / grid.x));

            // The mip to read: how many of the decal's own pixels one screen pixel covers. An
            // EXPLICIT level is what makes a read inside all the skipping above legal at all —
            // there is no derivative left for the hardware to want. Held short of the level where
            // a cell has shrunk to nothing, which is where the sheet's cells would run together.
            float lod = log2(max(footprint * cellPix / size, 1e-5));
            lod = clamp(lod, 0.0, log2(cellPix) - 1.0);

            float2 sheetUV = (cell + local) / grid;
            float4 mark = SAMPLE_TEXTURE2D_LOD(_RiverRunDecalSheet, sampler_RiverRunDecalSheet,
                                               sheetUV, lod);

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

// The decals over the stone at this pixel, laid on all three of the world's planes and mixed by
// how much this surface faces each — rgb already multiplied by its own coverage.
//
// The three are the world's floor and each of its two walls, flattened the same way the builder
// flattens UV0 so the two describe the same space: a face looking up reads the world's x and z, a
// face looking along x reads z and y, a face looking along z reads x and y.
float4 RiverRunDecalsOnStone(float3 worldPos, float3 n)
{
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

    float4 mixed = float4(0.0, 0.0, 0.0, 0.0);

    // Premultiplied answers, so mixing them is a weighted sum and nothing has to be unwound
    // first. The weights add to one, so a pixel wholly covered on every plane it faces stays
    // wholly covered.
    if (w.y > 0.0) mixed += w.y * RiverRunDecals(float2(worldPos.x, worldPos.z), footXZ);
    if (w.x > 0.0) mixed += w.x * RiverRunDecals(float2(worldPos.z, worldPos.y), footZY);
    if (w.z > 0.0) mixed += w.z * RiverRunDecals(float2(worldPos.x, worldPos.y), footXY);

    // Opacity spent once, on the finished mix rather than per plane, so a slanted face fades
    // by exactly as much as a flat one. Both the colour and the coverage are scaled, because
    // the colour is already multiplied by that coverage — scaling one without the other would
    // darken the mark as it faded instead of letting the stone through it.
    return mixed * saturate(_RiverRunDecalOpacity);
}

// The colour this part of the run is drawn in. A face carrying no kind at all — a mesh built
// before the builder was baking one — keeps whatever the graph handed in, and so does one whose
// colour nobody has pushed yet.
float3 RiverRunStone(float4 faceData, float3 baseColour)
{
    // UV2.w is 1 on a piece that really carries face kinds, and nothing reads it but this line.
    // It is here because a channel the mesh does not have does NOT arrive as zero: it comes
    // through carrying whatever was left in the stream, which on these pieces is the seam data
    // — and metres-to-the-nearest-seam rounds to 1, then 2, then 3 as it climbs, so a run that
    // has not been rebuilt draws itself in bands of all three colours at once. Reading the flag
    // rather than the kind is what tells a piece with no kinds from a piece standing three
    // metres from its nearest seam.
    if (faceData.w < 0.5) return baseColour;

    int kind = (int)(faceData.x + 0.5);

    float4 chosen = kind == RIVER_RUN_FACE_OUTER ? _RiverRunOuterColour
                  : kind == RIVER_RUN_FACE_RIM   ? _RiverRunRimColour
                  : kind == RIVER_RUN_FACE_INNER ? _RiverRunInnerColour
                                                 : float4(0.0, 0.0, 0.0, 0.0);

    return chosen.a > 0.0 ? chosen.rgb : baseColour;
}

// Light     : 1 square on to the made-up light, 0 turned right away from it, and flat 1 when the
//             light is turned off — so it can be multiplied into anything without checking first.
// Seam      : 0 well away from a corner, 1 sitting on one.
// Waterline : 0 above the band, 1 at the water and below it — held rather than falling away, so
//             there is no second line drawn where the band meets the surface.
// Tint/Blend: the two gradients composited, white over black, ready for one Lerp.
// Colour    : the stone — this part of the run's own colour, grained and lit — with that Lerp
//             already done, for wiring the node straight inline.
void RiverRunShading_float(
    float4 SeamData,
    float3 WorldNormal,
    float3 WorldPos,
    float3 BaseColour,
    float2 GrainUV,
    float4 FaceData,
    out float3 Colour,
    out float3 Tint,
    out float  Blend,
    out float  Seam,
    out float  Waterline,
    out float  Light)
{
    // Interpolated across a face a normal comes out shorter than one unit, which would read as the
    // surface having turned further from the light than it really has.
    float3 n = normalize(WorldNormal);

    // Which way this pixel would have to look to see the light. Normalised because it is only the
    // direction that is wanted — the distance says nothing here.
    float3 toLight = _LevelSelectLightPosition.xyz - WorldPos;
    toLight = dot(toLight, toLight) > 1e-8 ? normalize(toLight) : float3(0.0, 1.0, 0.0);

    // Half lambert: 1 square on to the light, 0.5 side on, 0 turned right away. Strength lerps out
    // of it rather than scaling it, so 0 leaves the stone at full and unlit rather than at black.
    // Past 1 the fade is done and the rest multiplies the lit result, up to 10x brighter.
    float ndl = dot(n, toLight) * 0.5 + 0.5;
    Light = lerp(1.0, saturate(ndl), saturate(_RiverRunLightStrength))
          * max(_RiverRunLightStrength, 1.0);

    // The stone under everything else, and the grain that goes over the top of the lot. The
    // grain is worked out here and spent at the very end: it is the last thing laid on, so it
    // runs across the seams and the waterline as well as across the bare stone. Under them it
    // would only be visible on whatever the two bands left uncovered, which on a run drawn with
    // any real extent is not much of it.
    float3 stone = RiverRunStone(FaceData, BaseColour);
    float  grain = RiverRunGrain(GrainUV, FaceData);

    // The hand-drawn marks, laid on the bare stone before any of the rest of it. Under the light
    // so the stone's own shaping falls across them, under the seams so a corner's ink line cuts
    // over them, and under the grain so the surface runs through them — a mark ON the stone,
    // rather than something floating in front of it.
    //
    // Off the world position and the normal rather than off GrainUV, which the grain still
    // uses: UV0 is flattened per triangle, and a drawing laid across two triangles that
    // flattened onto different planes is cut along the edge between them.
    float4 decals = RiverRunDecalsOnStone(WorldPos, n);
    // rgb comes back already multiplied by its own coverage, so it is added rather than
    // weighted again — what the stone loses to the marks is all that has to be taken off it.
    stone = decals.rgb + stone * (1.0 - decals.a);

    Colour    = stone * Light * grain;
    Tint      = _RiverRunSeamColour.rgb;
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

    // Metres to the nearest seam. A slot with no seam in it arrives carrying a number far too
    // big to ever be the smallest, so it simply drops out of this.
    float toSeam = min(min(SeamData.x, SeamData.y), SeamData.z);

    // The extent in metres for this face: the percentage of its own surface's width. A piece
    // built before the widths were baked carries no face data, and gets no seams until rebuilt.
    float surfaceWidth = FaceData.w >= 0.5 ? FaceData.y : 0.0;
    float seamExtent   = _RiverRunSeamExtent * 0.01 * surfaceWidth;

    Seam = saturate(RiverRunRamp(toSeam, seamExtent) * _RiverRunSeamStrength);

    // How far above the water this pixel is. w is zero on the rim top and negative under it, and
    // the water lies WaterDepth beneath the rim, so the two add. Below the water the ramp holds
    // at full rather than falling away, which is what keeps a second line from being drawn along
    // the surface itself where the band meets it.
    float above  = SeamData.w + _RiverRunWaterDepth;
    float facing = saturate(n.y / RIVER_RUN_UP_FACING);

    Waterline = saturate(RiverRunRamp(above, _RiverRunWaterlineExtent)
                       * _RiverRunWaterlineStrength * facing);

    // White laid over black, so where the two overlap — the corner the rim makes with the channel,
    // when both are drawn wide — the white is the one that reads.
    Blend = Seam + Waterline - Seam * Waterline;
    Tint  = Blend > 1e-5
          ? (_RiverRunSeamColour.rgb * Seam * (1.0 - Waterline)
             + _RiverRunWaterlineColour.rgb * Waterline) / Blend
          : _RiverRunSeamColour.rgb;

    // Laid over the lit stone rather than under it. A seam is an ink line where two faces meet
    // rather than a surface of its own, so a black seam stays black on the lit side of a run
    // instead of brightening with it. The same goes for the water's white. The grain then goes
    // over the whole composite — stone, seam and waterline alike.
    Colour = lerp(stone * Light, Tint, Blend) * grain;
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
    out half3 Colour,
    out half3 Tint,
    out half  Blend,
    out half  Seam,
    out half  Waterline,
    out half  Light)
{
    float3 c, t;
    float  b, s, w, l;
    RiverRunShading_float(SeamData, WorldNormal, WorldPos, BaseColour, GrainUV, FaceData,
                          c, t, b, s, w, l);
    Colour    = (half3)c;
    Tint      = (half3)t;
    Blend     = (half)b;
    Seam      = (half)s;
    Waterline = (half)w;
    Light     = (half)l;
}

#endif
