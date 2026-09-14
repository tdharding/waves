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
// is left with the colour it came in with.
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
// Strength at 0, or Extent at 0, returns the run to exactly what it was, and a face colour
// arriving with nothing in its alpha is one nobody has pushed yet, so the stone keeps the colour
// the graph handed in. Nothing here is on unless it is asked for — which is also what a world
// with no preset assigned gets, and what the one tick after a reimport gets.

#ifndef RIVER_RUN_SHADING_INCLUDED
#define RIVER_RUN_SHADING_INCLUDED

float4 _RiverRunSeamColour;
float  _RiverRunSeamStrength;
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
float  _RiverRunGrainSize;
float  _RiverRunGrainStrength;

// How far the water lies beneath the rim top — the world's Water Level, pushed alongside the rest
// rather than authored a second time.
float  _RiverRunWaterDepth;

// Where the made-up light stands, in world space. Every pixel works out its own direction to it.
float4 _RiverRunLightPosition;
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
float RiverRunGrain(float2 uv)
{
    if (_RiverRunGrainSize <= 0.0 || _RiverRunGrainStrength <= 0.0) return 1.0;

    float noise = RiverRunGradientNoise(uv / _RiverRunGrainSize);
    return max(0.0, 1.0 + noise * 2.0 * _RiverRunGrainStrength);
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
    float3 toLight = _RiverRunLightPosition.xyz - WorldPos;
    toLight = dot(toLight, toLight) > 1e-8 ? normalize(toLight) : float3(0.0, 1.0, 0.0);

    // Half lambert: 1 square on to the light, 0.5 side on, 0 turned right away. Strength lerps out
    // of it rather than scaling it, so 0 leaves the stone at full and unlit rather than at black.
    float ndl = dot(n, toLight) * 0.5 + 0.5;
    Light = lerp(1.0, saturate(ndl), saturate(_RiverRunLightStrength));

    // The stone under everything else, and the grain that goes over the top of the lot. The
    // grain is worked out here and spent at the very end: it is the last thing laid on, so it
    // runs across the seams and the waterline as well as across the bare stone. Under them it
    // would only be visible on whatever the two bands left uncovered, which on a run drawn with
    // any real extent is not much of it.
    float3 stone = RiverRunStone(FaceData, BaseColour);
    float  grain = RiverRunGrain(GrainUV);

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

    Seam = saturate(RiverRunRamp(toSeam, _RiverRunSeamExtent) * _RiverRunSeamStrength);

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
