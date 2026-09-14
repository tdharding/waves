// The lines drawn on the level select's water.
//
// There are two different waters out there and they are not the same shape, so they do not get
// the same lines. A RIVER is a channel with two banks, and its lines lie along those banks. A
// POOL is a bowl with a middle, and its lines are rings coming out of that middle. THE RIVER IS
// DRAWN HERE AND THE POOL IS NOT: a pool's water is handed straight over to PoolRings.hlsl at
// the gate below, and everything in this file after that gate belongs to rivers alone. Both read
// the frame the mesh builder bakes into the surface as it generates it — there is no texture to
// paint either into, because both meshes are different every time the designer is touched.
//
// TWO MOVEMENTS ON A RIVER, and they are separate on purpose:
//   * Speed carries the LINES themselves — inward across the channel. Leave it at 0 and the
//     lines stand still.
//   * Flow carries the DISTORTION along the lines while they stand still: the swing and the
//     drift travel down the channel, and the lines they are bending never move. This is the one
//     the water is meant to read as, with Speed at 0.
// A pool has one movement and no distortion at all: Speed carries its rings out from the middle,
// and they stay circles the whole way. See PoolRings.hlsl.
//
// This is the same family of effects as the interior level's rock rings and wave bands, built the
// same way on purpose, so the two scenes read as the same water:
//   * a line is a WINDOW cut out of its cycle, not a sine, so widening one lights more of the
//     cycle without also brightening it;
//   * the phase is SUBTRACTED, which is what drifts the lines rather than pulsing them;
//   * the wander is TWO layers, a steady swing every line shares plus a noise drift, so the
//     family never looks stamped;
//   * the output is white by construction and floored at zero, so whatever it feeds, an Add can
//     only ever brighten. Turning Strength to 0 returns the water exactly to what it was.
// The top is deliberately left open: Strength above 1 keeps its overbright headroom for bloom.

#ifndef RIVER_EDGE_RIPPLES_INCLUDED
#define RIVER_EDGE_RIPPLES_INCLUDED

#define RIVER_RIPPLE_TWO_PI 6.28318530718

// A pool is a different shape of water and gets a different drawing, kept in its own file so
// nothing a river's lines do can reach a ring. See the gate below.
#include "PoolRings.hlsl"

// ── Globals ──────────────────────────────────────────────────────────────────
// Bare $Globals, not blackboard properties, exactly as the interior water's effects are. Nothing
// on disk remembers them: there is no entry in any material's property block, so a shader reimport
// or a scene reload leaves them at 0 until something puts them back. What puts them back is
// LevelSelectDesignerData.ApplyAesthetics, re-pushed EVERY FRAME — by LevelSelectDataController in
// play mode and by LevelSelectAestheticsPump out of it — which is what makes a reimport self-heal
// on the next tick instead of quietly emptying the rivers.
//
// The numbers themselves are authored in the Level Select River Tuner and kept on a
// LevelSelectRiverPreset, which the designer holds one of per world.

float _RiverEdgeRippleReach;      // metres inward from a bank a river's lines carry
float _RiverEdgeRippleBevel;      // 0 = they ease away; 1 = they hold, then stop on a crease
float _RiverEdgeRippleSpacing;    // metres between one line and the next
float _RiverEdgeRippleStrength;   // peak whiteness of a line; 0 = the effect is gone
float _RiverEdgeRippleSpeed;      // metres per second the LINES travel; 0 holds them still

// A line is a WINDOW cut out of its cycle, not a sine — the same construction as the rock rings,
// so both read as the same water. The top is flat at full height whatever the width, so widening
// a line changes how much of the cycle is lit without also brightening it.
float _RiverEdgeRippleWidth;      // fraction of its cycle a line fills
float _RiverEdgeRippleSoftness;   // 0 = a hard-edged line; 1 = falls away from its centre

// The wander is two layers because a bank is not a ruler. The sine gives every line the same
// steady swing along it, which is what makes them read as one body of water; the noise then
// drifts them off that shared rhythm so the family never looks stamped.
float _RiverEdgeRippleWaviness;       // how far a line swings, in line-widths
float _RiverEdgeRippleWavinessScale;  // how often that swing repeats along the line
float _RiverEdgeRippleMeander;        // irregular drift on top of the swing, in line-widths
float _RiverEdgeRippleMeanderScale;   // frequency of that drift

// Movement ALONG the lines, which is what carries the distortion while the lines stand still.
float _RiverEdgeRippleFlow;       // metres per second down a river's channel

// A pool's rings are not bank lines: they fill the bowl rather than hugging its edge, so they
// need their own ending. This is how far in from the pool's wall they come up to full.
float _RiverEdgeRipplePoolFade;   // metres

float _RiverEdgeRippleFlowSpace;   // 1 = the drift travels along the lines; 0 = pinned to the world
float _RiverEdgeRippleLapFade;     // how much of a lap the alpha gradient covers, 0-1
                                  // from the far lip back

// 0 off. 1 bands, 2 along, 3 fade, 4 both — see the Debug output below.
float _RiverEdgeRippleDebug;

// The same quintic the rest of the water eases with (WaterSurface.hlsl / RockRings.hlsl), so the
// ripples die away on the same curve as everything else rather than a curve of their own.
float RiverEdgeSmootherStep(float h)
{
    return h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
}

// Gradient noise matching Shader Graph's own node, so the field behaves the way it does when you
// preview one in the graph. Its own copy rather than a shared include: this is the only file the
// level select water pulls in, and the interior water's copy is guarded for a different reason.
float2 RiverEdgeNoiseDir(float2 p)
{
    p = fmod(p, 289.0);
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

float RiverEdgeGradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);

    float d00 = dot(RiverEdgeNoiseDir(ip),                    fp);
    float d01 = dot(RiverEdgeNoiseDir(ip + float2(0.0, 1.0)), fp - float2(0.0, 1.0));
    float d10 = dot(RiverEdgeNoiseDir(ip + float2(1.0, 0.0)), fp - float2(1.0, 0.0));
    float d11 = dot(RiverEdgeNoiseDir(ip + float2(1.0, 1.0)), fp - float2(1.0, 1.0));

    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y);
}

// EdgeData is UV1, straight off the generated water — how far this corner lies from the lines the
// water is really seen to end on:
//   .x  metres to the waterline on one side   .y  metres to the waterline on the other
//   .z  metres along the channel              .w  spare
//
// Both distances are held as straight lines across the water rather than as a distance to the
// nearer of the two, because a water ribbon is only two vertices wide and a nearest-edge distance
// folds down the middle. The fold is made here, per pixel, where it costs a min().
//
// A pool's side with no wall on it (across a mouth, or no island) carries a number far enough out
// that nothing fades there.
//
// FlowData is UV2, and it is the frame the lines are drawn in — the piece that tells a pool from
// a river, so the two can be drawn differently off one shader:
//   .x  ALONG   river: metres down the channel      pool: metres across the middle, east
//   .y  ACROSS  river: metres off the centreline    pool: metres across the middle, north
//
// A pool carries a POSITION rather than a heading and a radius, and the only thing taken off it
// is its LENGTH — how far out from the middle this pixel lies. That is the whole reason its rings
// come out round: a corner's numbers are interpolated across the face between them, a position
// interpolates exactly because the surface really is flat, and a length of one is a true distance
// at every pixel. The heading is never taken for the drawing at all (only for the debug view), and
// that is deliberate: the middle of a bowl has no heading of its own, every direction in the world
// meets at that one point, and anything measured in headings tears into a whorl there.
//   .z  LAP     1 in the body of the surface, running to 0 at the far lip of an overlap onto its
//               neighbour. The RAMP is baked; how much of it the alpha gradient uses is
//               _RiverEdgeRippleLapFade, so it can be retuned without rebuilding a mesh.
//   .w  KIND    0 none, 1 river, 2 pool
//
// Water generated before any of this existed carries no UV2 at all, which reads as zero — kind 0.
// That falls back to exactly what this drew before there was a frame to read: lines along the
// banks, wandering off the world. Rebuild Runs is what gives a surface its frame.
//
// Ripples : >= 0, already scaled by Strength. 0 anywhere no line falls, so it stays neutral into
//           an Add. Not capped at 1.
// Edge    : metres to the nearer waterline. Negative out past it, where the wall has the water
//           buried and none of it is ever seen. Handed out for whatever else wants to know.
// Mask    : 1 where the lines are at full, 0 where they are gone, on the curve they fade with.
// Fade    : THE ALPHA A LAPPED SURFACE SHOULD BE DRAWN AT — 1 in the body of the water, easing to
//           0 at the far lip of a lap. Wired into the water graph's Alpha, and the ONLY thing that
//           blends one water into the next: the ripples are not faded by it here, so the pattern
//           runs the full length of its own mesh and the alpha alone takes it away.
// Debug   : a colour showing the frame this pixel was drawn in — black while Debug is off.
// DebugMix: 1 while Debug is on, 0 otherwise. Lerp BaseColor towards Debug by this.
void RiverEdgeRipples_float(
    float4 EdgeData,
    float4 FlowData,
    float3 WorldPos,
    out float  Ripples,
    out float  Edge,
    out float  Mask,
    out float  Fade,
    out float3 Debug,
    out float  DebugMix)
{
    Ripples  = 0.0;
    Fade     = 1.0;
    Debug    = float3(0.0, 0.0, 0.0);
    DebugMix = 0.0;

    // The fold: whichever bank this pixel is nearer to is the one it belongs to. Both banks draw
    // the same pattern from their own edge, so a channel is ripples down each side with clear
    // water between them — and a channel narrower than twice the Reach has the two meet in the
    // middle at their weakest rather than on a seam.
    float e = min(EdgeData.x, EdgeData.y);
    Edge = e;

    // Water built before there was any edge data to build carries none, and a mesh with no UV1
    // reads as zero — which is a waterline lying under every pixel of it at once. Left alone that
    // floods a whole river with ripples and looks like the effect is broken rather than the mesh
    // being stale, so it draws nothing instead: rebuild the runs and it comes back. Nothing real
    // lands here, since both distances are only zero at once where a channel holds no water.
    if (EdgeData.x == 0.0 && EdgeData.y == 0.0)
    {
        Mask = 0.0;
        Fade = 0.0;
        return;
    }

    float kind   = FlowData.w;
    bool  framed = kind > 0.5;
    bool  isPool = kind > 1.5;

    // ── The overlap ──────────────────────────────────────────────────────────
    // Where two waters are built to lie over one another — a branch reaching back across the
    // river it leaves, a pool reaching out into its rivers — the one on top is generated with a
    // weight running 1 down to 0 across the lap. It goes out as the alpha and nowhere else.
    // Unframed water has no lap and stays at 1.
    // The gradient runs back from the LIP, and Lap Fade says how much of the lap it covers: 1
    // spreads it over the whole lap, a half hugs the lip and leaves the inner half at full. It
    // is a fraction of the lap rather than a distance, so it can never outrun the lap it is
    // drawn on and leave a step where the gradient stops short of the water's own body. The
    // metres are the designer's to set — the lap is geometry, and this shapes whatever is built.
    float lap = framed ? saturate(FlowData.z) : 1.0;
    Fade = saturate(lap / max(_RiverEdgeRippleLapFade, 0.0001));

    float spacing = max(_RiverEdgeRippleSpacing, 0.0001);

    // ── The gate ───────────────────────────────────────────────────────
    // A pool's water leaves here and never comes back. The kind is baked into the surface by the
    // mesh builder as it generates it, one number per corner, so this is the geometry itself
    // saying which drawing it is owed rather than anything guessed at per pixel: a pool's own
    // pieces take the rings, and every other piece of water in the world carries on down the
    // river path below without ever touching a line of the pool's maths.
    //
    // What goes over is a DISTANCE — how far this pixel lies out from the middle, taken as the
    // length of the offset the mesh bakes in. No heading goes with it, and PoolRings.hlsl has no
    // way to ask for one. That is deliberate and it is the whole of the fix: a heading is what
    // pinched the rings into a whorl at the middle of the bowl, because every direction meets at
    // that one point and there is no room there for a heading to be worth anything.
    if (isPool)
    {
        PoolRings(length(FlowData.xy), e, _Time.y,
                  spacing,
                  _RiverEdgeRippleStrength,
                  _RiverEdgeRippleSpeed,
                  _RiverEdgeRippleWidth,
                  _RiverEdgeRippleSoftness,
                  _RiverEdgeRipplePoolFade,
                  Ripples, Mask);

        // The frame reader, unchanged: rings out of the middle under Bands, the heading under
        // Along. It draws nothing and feeds nothing — the only place in this file a pool's
        // heading is ever worked out, and it is here so a frame that came out wrong can be SEEN.
        if (_RiverEdgeRippleDebug > 0.5)
        {
            float b = frac(length(FlowData.xy) / spacing);
            float a = frac(atan2(FlowData.x, FlowData.y) / RIVER_RIPPLE_TWO_PI);

            float mode = _RiverEdgeRippleDebug;
            if      (mode < 1.5) Debug = float3(b, b * 0.25, 0.0);              // bands, red
            else if (mode < 2.5) Debug = float3(0.0, a, a * 0.35);              // along, green
            else if (mode < 3.5) Debug = float3(Fade, Fade * 0.6, 1.0 - Fade);  // the overlap
            else                 Debug = float3(b, a, 0.35);                    // both, as a check

            DebugMix = 1.0;
        }

        return;
    }

    // ── Where the lines stop ─────────────────────────────────────────────────
    // A river's lines belong to its banks, so they are strongest on the waterline and gone by
    // Reach. Where a pool's rings stop is PoolRings.hlsl's own business, above.
    float h    = 1.0 - saturate(e / max(_RiverEdgeRippleReach, 0.0001));
    float mask = lerp(RiverEdgeSmootherStep(h), h, saturate(_RiverEdgeRippleBevel));
    Mask = mask;

    // ── The frame ────────────────────────────────────────────────────────────
    // band  — the coordinate the lines are cut across, so a step of Spacing in it is one line.
    // along — the coordinate that runs ALONG a line, which is the way the distortion travels.
    // side  — the second axis of the drift's own space, so the noise is a field and not a stripe.
    float band  = e;                                 // metres in from the nearer bank
    float along = framed ? FlowData.x : EdgeData.z;  // metres down the channel
    float side  = framed ? FlowData.y : 0.0;

    // How far the distortion has been carried along the lines by now, in metres down the channel.
    float travelled = along - _Time.y * _RiverEdgeRippleFlow;

    // ── Debug ────────────────────────────────────────────────────────────────
    // Answers the only question worth asking of a generated frame: which way is it lying. Bands
    // draws the coordinate the lines are cut from, so a river shows stripes down its banks and a
    // pool shows rings; Along draws the coordinate they run in, so its stripes cross them at a
    // right angle. Read off the frame BEFORE the wander goes on, so a wrong axis is not hidden
    // behind a wobble.
    if (_RiverEdgeRippleDebug > 0.5)
    {
        float b = frac(band / spacing);
        float a = frac(along / spacing);

        float mode = _RiverEdgeRippleDebug;
        if      (mode < 1.5) Debug = float3(b, b * 0.25, 0.0);              // bands, red
        else if (mode < 2.5) Debug = float3(0.0, a, a * 0.35);              // along, green
        else if (mode < 3.5) Debug = float3(Fade, Fade * 0.6, 1.0 - Fade);  // the overlap weight
        else                 Debug = float3(b, a, 0.35);                    // both, as a check

        // Unframed water is the one thing worth calling out rather than drawing: it has no frame
        // at all, and every mode above would draw it as though it had one.
        if (!framed) Debug = float3(0.5, 0.0, 0.5);

        DebugMix = 1.0;
    }

    if (_RiverEdgeRippleStrength <= 0.0 || mask <= 0.0) return;

    // Counted in whole lines rather than radians, so the window below is a plain fraction of a
    // cycle and both wander amounts are measured in line-widths.
    float s = band / spacing;

    // The steady swing every line shares, running ALONG the line — so a line wanders over its own
    // course down the river rather than every line wobbling in place. Both banks of one channel
    // swing together, which is what keeps them one body of water; the drift below is what stops
    // that reading as a mirror.
    if (abs(_RiverEdgeRippleWaviness) > 0.0001)
    {
        s += sin(travelled * _RiverEdgeRippleWavinessScale) * _RiverEdgeRippleWaviness;
    }

    // The irregular drift on top of it, and the layer the movement really rides on. In flow space
    // it is sampled in the line's own frame, so it travels exactly along the lines and follows a
    // channel round its bends; pinned to the world instead it is the older reading, where the
    // field is nailed to the map and two runs meeting at a junction drift as one piece.
    if (abs(_RiverEdgeRippleMeander) > 0.0001)
    {
        float2 p = _RiverEdgeRippleFlowSpace > 0.5
            ? float2(travelled, side)
            : WorldPos.xz;

        s += RiverEdgeGradientNoise(p * _RiverEdgeRippleMeanderScale) * _RiverEdgeRippleMeander;
    }

    // SUBTRACTING the phase is what carries the LINES themselves: a crest sits where the cycle
    // comes round, and that place is Speed metres further along every second. Negative Speed runs
    // them back the other way. This is the movement to leave at 0 when the distortion above is
    // meant to be the only thing travelling. Read off the clock rather than accumulated on the
    // CPU, because nothing out here is animating Speed while the scene runs.
    float cyc     = s - _Time.y * (_RiverEdgeRippleSpeed / spacing);
    float centred = abs(frac(cyc) - 0.5) * 2.0;   // 0 at a line's centre, 1 at the cycle edge

    // The window. Held just under 1 so neighbouring lines always keep a seam between them rather
    // than merging into a wash. Softness slides the inner edge of the falloff in toward the
    // centre: at 0 the line has a hard rim, at 1 it falls away from its middle. The epsilon keeps
    // the two apart, since smoothstep divides by the gap.
    float w     = clamp(_RiverEdgeRippleWidth, 0.01, 0.98);
    float soft  = saturate(_RiverEdgeRippleSoftness);
    float inner = w * (1.0 - soft);
    float outer = max(w, inner + 1e-4);
    float pulse = 1.0 - smoothstep(inner, outer, centred);

    Ripples = max(pulse * mask * _RiverEdgeRippleStrength, 0.0);
}

// Shader Graph appends _float or _half to the function name depending on graph precision, and a
// File custom function has to supply whichever it asks for. Forwards to the float version — the
// globals are float and the maths is cheap.
void RiverEdgeRipples_half(
    half4 EdgeData,
    half4 FlowData,
    half3 WorldPos,
    out half  Ripples,
    out half  Edge,
    out half  Mask,
    out half  Fade,
    out half3 Debug,
    out half  DebugMix)
{
    float  r, e, m, f, dm;
    float3 d;
    RiverEdgeRipples_float(EdgeData, FlowData, WorldPos, r, e, m, f, d, dm);
    Ripples  = (half)r;
    Edge     = (half)e;
    Mask     = (half)m;
    Fade     = (half)f;
    Debug    = (half3)d;
    DebugMix = (half)dm;
}

#endif
