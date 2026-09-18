// The lines drawn on the level select's water.
//
// There are two different waters out there and they are not the same shape, so they do not get
// the same lines. A RIVER is a channel with two banks, and its lines lie along those banks. A
// POOL is a bowl with a middle, and its lines are rings coming out of that middle. THE RIVER IS
// DRAWN HERE AND THE POOL IS NOT: a pool's water is handed straight over to PoolRings.hlsl at
// the gate below, and everything in this file after that gate belongs to rivers alone.
//
// A river reads the frame the mesh builder bakes into its surface, because its lines have to
// follow the channel round its bends. A pool reads NOTHING off its mesh but the fact that it is a
// pool: its rings are measured in world space out from the pool object's own origin, so however
// the pool's water happens to be cut into faces, the rings cannot tell.
//
// TWO MOVEMENTS ON A RIVER, and they are separate on purpose:
//   * Speed carries the LINES themselves — inward across the channel. Leave it at 0 and the
//     lines stand still.
//   * Distortion Speed carries the DISTORTION along the lines while they stand still. This is
//     the one the water is meant to read as, with Speed at 0.
// A pool is the same pair: Pool Speed carries its rings out from the middle, and Pool Distortion
// Speed spins its noise field round the pool's centre while the rings stand still.
//
// This is the same family of effects as the interior level's rock rings and wave bands, built the
// same way on purpose, so the two scenes read as the same water:
//   * a line is a WINDOW cut out of its cycle, not a sine, so widening one lights more of the
//     cycle without also brightening it;
//   * the phase is SUBTRACTED, which is what drifts the lines rather than pulsing them;
//   * the wander is ONE gradient noise field, a strength and a scale, like _RockRingDistort*;
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

// Shared by rivers and pools, so the two read as one body of water.
float _RiverEdgeRippleStrength;   // peak whiteness of a line; 0 = the effect is gone
float _RiverEdgeRippleWidth;      // fraction of its cycle a line fills
float _RiverEdgeRippleSoftness;   // 0 = a hard-edged line; 1 = falls away from its centre

// Rivers.
float _RiverEdgeRippleSpacing;         // metres between one line and the next
float _RiverEdgeRippleSpeed;           // metres per second the LINES travel; 0 holds them still
float _RiverEdgeRippleReach;           // PERCENT of the river's width inward from a bank its lines carry
float _RiverEdgeRippleBevel;           // 0 = they ease away; 1 = they hold, then stop on a crease
float _RiverEdgeRippleDistortStrength; // in line-widths: 0.1 shifts a line by a tenth of a spacing
float _RiverEdgeRippleDistortScale;    // frequency of that noise field
float _RiverEdgeRippleDistortSpeed;    // metres per second the distortion travels down a channel
float _RiverEdgeRippleFlowSpace;       // 1 = the field travels along the lines; 0 = pinned to world
float _RiverEdgeRippleBranchMouthFade;  // metres past the joined river's bank a branch's lines take on its reach; 0 = off
float _RiverEdgeRippleBranchMouthInset; // metres out into the branch that change-over starts

// Pools.
float _RiverEdgeRipplePoolSpacing;         // metres between one ring and the next
float _RiverEdgeRipplePoolReach;           // metres inward from the pool's waterline rings carry
float _RiverEdgeRipplePoolSpeed;           // metres per second the rings travel outward
float _RiverEdgeRipplePoolDistortStrength; // in ring-widths
float _RiverEdgeRipplePoolDistortScale;    // world-space frequency of that noise field
float _RiverEdgeRipplePoolDistortSpeed;    // turns per second the field spins round the centre
float _RiverEdgeRipplePoolMouthFade;       // metres past the pool circle the rings take on a river's reach; 0 = off
float _RiverEdgeRipplePoolMouthReachOverlap; // metres inside the pool circle that change-over starts
float _RiverEdgeRipplePoolMouthExtent;     // metres past the waterline the rings fade out over; 0 = no end

float _RiverEdgeRippleRiverFadeToPoolDistance;  // metres back from a pool lap's lip the alpha fades over
float _RiverEdgeRippleRiverFadeToPoolIntensity; // falloff of that fade: 1 even, higher later and harder

// The grain: one still gradient noise field over every water, river and pool alike.
float _RiverEdgeRippleGrainStrength;       // how far the grain pushes the colour either way; 0 = none
float _RiverEdgeRippleGrainSize;           // metres across one grain
float _RiverEdgeRippleGrainAlpha;          // how far the same grain thins the alpha; 0 = none, 1 = clear at its darkest

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

// EdgeData is UV1, straight off a river's generated water — how far this corner lies from the
// lines the water is really seen to end on:
//   .x  metres to the waterline on one side   .y  metres to the waterline on the other
//   .z  metres along the channel              .w  the lap's length in metres (0 = no lap)
//
// Both distances are held as straight lines across the water rather than as a distance to the
// nearer of the two, because a water ribbon is only two vertices wide and a nearest-edge distance
// folds down the middle. The fold is made here, per pixel, where it costs a min(). A pool carries
// the same numbers on every vertex: .x its outer waterline radius, .y its island's (-1 for none),
// .z the radius of its circle — where its strips set off down the rivers — and .w its lap length.
//
// FlowData is UV2:
//   .x  ALONG   river: metres down the channel
//   .y  ACROSS  river: metres off the centreline
//   .z  LAP     1 in the body of the surface, running to 0 at the far lip of an overlap onto its
//               neighbour. Times EdgeData.w it is metres back from the lip, and the alpha fades
//               over _RiverEdgeRippleRiverFadeToPoolDistance of that — never longer than the lap.
//   .w  KIND    0 none, 1 river, 2 pool
//
// BankData is UV3: the banks of ANOTHER river, whose Reach this water's lines can take on.
// On a pool's strip — the water a pool runs out down each river that meets it — it is the river
// the strip lies in; on the sheet inside the circle, the river that corner faces:
//   .x  metres to that river's waterline on one side   .y  on the other
//   .z  1 on a strip or between a river's banks on the sheet, 0 everywhere else
// On a branch's water it is the river the branch leaves, near the junction:
//   .x  metres to that river's NEAR waterline — positive inside its water, negative out in the
//       branch                                         .y  to its far waterline
//   .z  1 near the junction, 0 further up the branch
//
// Water generated before any of this existed carries no UV2 at all, which reads as zero — kind 0.
// That falls back to lines along the banks with a world-pinned distortion. Rebuild Runs is what
// gives a surface its frame.
//
// Ripples : >= 0, already scaled by Strength. 0 anywhere no line falls, so it stays neutral into
//           an Add. Not capped at 1.
// Edge    : metres to the nearer waterline (rivers; 0 on a pool).
// Mask    : 1 where the lines are at full, 0 where they are gone, on the curve they fade with.
// Fade    : THE ALPHA A LAPPED SURFACE SHOULD BE DRAWN AT — 1 in the body of the water, easing to
//           0 at the far lip of a lap. Wired into the water graph's Alpha, and the ONLY thing that
//           blends one water into the next: the ripples are not faded by it here, so the pattern
//           runs the full length of its own mesh and the alpha alone takes it away.
// Debug   : a colour showing the frame this pixel was drawn in — black while Debug is off.
// DebugMix: 1 while Debug is on, 0 otherwise. Lerp BaseColor towards Debug by this.
// Grain   : SIGNED, centred on 0 — brightens and darkens by up to about Grain Strength. Add it to
//           BaseColor. Sampled in world XZ so it runs straight across every join, and it does not
//           move. 0 everywhere at Grain Strength 0, so the water is exactly as it was.
void RiverEdgeRipples_float(
    float4 EdgeData,
    float4 FlowData,
    float3 WorldPos,
    float4 BankData,
    out float  Ripples,
    out float  Edge,
    out float  Mask,
    out float  Fade,
    out float3 Debug,
    out float  DebugMix,
    out float  Grain)
{
    Ripples  = 0.0;
    Edge     = 0.0;
    Mask     = 0.0;
    Fade     = 1.0;
    Debug    = float3(0.0, 0.0, 0.0);
    DebugMix = 0.0;
    Grain    = 0.0;

    // ── The grain ────────────────────────────────────────────────────────────
    // Before the pool gate, so pools and rivers take the same grain. The noise runs about ±0.5,
    // doubled so Strength is roughly the most it moves the colour.
    float grainNoise = 0.0;
    if (abs(_RiverEdgeRippleGrainStrength) > 0.0001 || _RiverEdgeRippleGrainAlpha > 0.0001)
    {
        grainNoise = RiverEdgeGradientNoise(WorldPos.xz / max(_RiverEdgeRippleGrainSize, 0.0001));
        Grain = grainNoise * 2.0 * _RiverEdgeRippleGrainStrength;
    }

    float kind   = FlowData.w;
    bool  framed = kind > 0.5;
    bool  isPool = kind > 1.5;

    // ── The overlap ──────────────────────────────────────────────────────────
    // Where two waters are built to lie over one another — a pool reaching out into its rivers —
    // the one on top is generated with a weight running 1 down to 0 across the lap. It goes out as the alpha and nowhere else.
    // Unframed water has no lap and stays at 1. The fade runs River Fade To Pool Distance metres
    // back from the lip, held to the lap's own length so it can never outrun the lap it is drawn on
    // and leave a step where the gradient stops short of the water's body. River Fade To Pool
    // Intensity bends that ramp: 1 even, higher comes in later and harder, lower sooner.
    float lapLength = framed ? EdgeData.w : 0.0;
    if (lapLength > 0.0001)
    {
        float fromLip = saturate(FlowData.z) * lapLength;
        Fade = saturate(fromLip / clamp(_RiverEdgeRippleRiverFadeToPoolDistance, 0.0001, lapLength));
        Fade = pow(Fade, max(_RiverEdgeRippleRiverFadeToPoolIntensity, 0.01));
    }

    // ── Grain into the alpha ─────────────────────────────────────────────────
    // The same grain field, thinning the water where it runs dark: 0 leaves the alpha alone, 1
    // takes the darkest of the grain fully clear while its lightest stays solid. Its own amount,
    // separate from Grain Strength, so the alpha can take the grain with or without the colour.
    Fade *= 1.0 - saturate(_RiverEdgeRippleGrainAlpha) * saturate(0.5 - grainNoise);

    // ── The gate ───────────────────────────────────────────────────────
    // A pool's water leaves here and never comes back. Its rings are measured from the pool
    // object's own origin in WORLD space — every pool's water object sits on its pool's centre —
    // so nothing baked into the faces reaches the drawing, and there is no line in the mesh for
    // the rings to change along.
    if (isPool)
    {
        float poolSpacing = max(_RiverEdgeRipplePoolSpacing, 0.0001);

        float3 centre = UNITY_MATRIX_M._m03_m13_m23;
        float2 offset = WorldPos.xz - centre.xz;
        float  radius = length(offset);

        // The noise field is the rock rings' — world scale — but turned round the pool's centre by
        // Pool Distortion Speed, so the wobble travels round the rings while they stand still. A
        // turn about the centre has no seam and no pinch: the middle just turns on the spot.
        float distort = 0.0;
        if (abs(_RiverEdgeRipplePoolDistortStrength) > 0.0001)
        {
            float  turn = _Time.y * _RiverEdgeRipplePoolDistortSpeed * RIVER_RIPPLE_TWO_PI;
            float  c = cos(turn), sn = sin(turn);
            float2 spun = float2(offset.x * c - offset.y * sn, offset.x * sn + offset.y * c);

            distort = RiverEdgeGradientNoise((centre.xz + spun) * _RiverEdgeRipplePoolDistortScale)
                    * _RiverEdgeRipplePoolDistortStrength;
        }

        PoolRings(radius, distort, _Time.y,
                  poolSpacing,
                  _RiverEdgeRippleStrength,
                  _RiverEdgeRipplePoolSpeed,
                  _RiverEdgeRippleWidth,
                  _RiverEdgeRippleSoftness,
                  Ripples, Mask);

        // Pool Reach: the rings belong to the waterline, strongest there and gone Reach metres
        // in, on the river lines' own curve. Out past the waterline — over a mouth and down a
        // strip into a river — they stay at full. The radii are constant across the mesh, so
        // this cannot change along a face edge.
        float e = EdgeData.x - radius;
        if (EdgeData.y >= 0.0) e = min(e, radius - EdgeData.y);
        Edge = e;

        float poolMask = RiverEdgeSmootherStep(1.0 - saturate(e / max(_RiverEdgeRipplePoolReach, 0.0001)));

        // Pool Mouth Fade: toward a river, the rings take on the Reach of that river — held along
        // its banks, gone from its middle, on the river lines' own curve — easing over from the
        // pool's own mask across this many metres. The ease starts Pool Mouth Reach Overlap metres inside
        // the pool's circle (0 = on it) and runs on out down the strip. Only the rings — the
        // water stays solid, since the river's own water does not reach in under the strip.
        if (_RiverEdgeRipplePoolMouthFade > 0.0001 && BankData.z > 0.0001)
        {
            float bankA = max(BankData.x, 0.0);
            float bankB = max(BankData.y, 0.0);
            float be    = min(bankA, bankB);
            float reach = _RiverEdgeRippleReach * 0.01 * (bankA + bankB);
            float bh    = 1.0 - saturate(be / max(reach, 0.0001));
            float bank  = lerp(RiverEdgeSmootherStep(bh), bh, saturate(_RiverEdgeRippleBevel));

            float start = EdgeData.z - max(_RiverEdgeRipplePoolMouthReachOverlap, 0.0);
            float past  = max(radius - start, 0.0);
            float blend = RiverEdgeSmootherStep(saturate(past / _RiverEdgeRipplePoolMouthFade))
                        * saturate(BankData.z);
            poolMask *= lerp(1.0, bank, blend);
        }

        // Pool Mouth Extent: past the outer waterline the only water left is a strip running out
        // down a river, and the rings end outright over this many metres of it — on top of any
        // change-over to the river's Reach above.
        if (_RiverEdgeRipplePoolMouthExtent > 0.0001)
        {
            float beyond = radius - EdgeData.x;
            poolMask *= RiverEdgeSmootherStep(1.0 - saturate(beyond / _RiverEdgeRipplePoolMouthExtent));
        }
        Ripples *= poolMask;
        Mask     = poolMask;

        if (_RiverEdgeRippleDebug > 0.5)
        {
            float b = frac(radius / poolSpacing);
            float a = frac(atan2(offset.x, offset.y) / RIVER_RIPPLE_TWO_PI);

            float mode = _RiverEdgeRippleDebug;
            if      (mode < 1.5) Debug = float3(b, b * 0.25, 0.0);              // bands, red
            else if (mode < 2.5) Debug = float3(0.0, a, a * 0.35);              // along, green
            else if (mode < 3.5) Debug = float3(Fade, Fade * 0.6, 1.0 - Fade);  // the overlap
            else                 Debug = float3(b, a, 0.35);                    // both, as a check

            DebugMix = 1.0;
        }

        return;
    }

    float spacing = max(_RiverEdgeRippleSpacing, 0.0001);

    // The fold: whichever bank this pixel is nearer to is the one it belongs to. Both banks draw
    // the same pattern from their own edge, so a channel is ripples down each side with clear
    // water between them.
    float e = min(EdgeData.x, EdgeData.y);
    Edge = e;

    // River water built before there was any edge data carries none, and a mesh with no UV1 reads
    // as zero — a waterline under every pixel at once. It draws nothing instead: rebuild the runs
    // and it comes back.
    if (EdgeData.x == 0.0 && EdgeData.y == 0.0)
    {
        Fade = 0.0;
        return;
    }

    // ── Where the lines stop ─────────────────────────────────────────────────
    // A river's lines belong to its banks, so they are strongest on the waterline and gone by
    // Reach. Reach is a PERCENTAGE of the river's width here, not metres, so every river carries
    // its lines in proportion to itself. The two distances are straight lines to the two
    // waterlines, so together they are the width at this pixel — nothing needs rebuilding.
    float width = EdgeData.x + EdgeData.y;
    float reach = _RiverEdgeRippleReach * 0.01 * width;
    float h    = 1.0 - saturate(e / max(reach, 0.0001));
    float mask = lerp(RiverEdgeSmootherStep(h), h, saturate(_RiverEdgeRippleBevel));

    // Branch Mouth Fade: a branch's water runs back across the mouth and lies over the river it
    // leaves, drawn on top of it. Going in past that river's near waterline, the branch's lines
    // take on THAT river's Reach — held along its banks, gone from its middle — easing over across
    // this many metres, so they stop carrying the branch's own banks straight out across it. The
    // ease starts Branch Mouth Inset metres back out in the branch (0 = on the waterline). The
    // branch's own mask still applies; this multiplies onto it, as the pool's does.
    if (_RiverEdgeRippleBranchMouthFade > 0.0001 && BankData.z > 0.0001)
    {
        float bankA = max(BankData.x, 0.0);
        float bankB = max(BankData.y, 0.0);
        float be    = min(bankA, bankB);
        float jr    = _RiverEdgeRippleReach * 0.01 * (BankData.x + BankData.y);
        float bh    = 1.0 - saturate(be / max(jr, 0.0001));
        float bank  = lerp(RiverEdgeSmootherStep(bh), bh, saturate(_RiverEdgeRippleBevel));

        float past  = BankData.x + max(_RiverEdgeRippleBranchMouthInset, 0.0);
        float blend = RiverEdgeSmootherStep(saturate(past / _RiverEdgeRippleBranchMouthFade))
                    * saturate(BankData.z);
        mask *= lerp(1.0, bank, blend);
    }
    Mask = mask;

    // ── The frame ────────────────────────────────────────────────────────────
    // band  — the coordinate the lines are cut across, so a step of Spacing in it is one line.
    // along — the coordinate that runs ALONG a line, which is the way the distortion travels.
    // side  — the second axis of the distortion's own space, so the noise is a field.
    float band  = e;                                 // metres in from the nearer bank
    float along = framed ? FlowData.x : EdgeData.z;  // metres down the channel
    float side  = framed ? FlowData.y : 0.0;

    // How far the distortion has been carried along the lines by now, in metres down the channel.
    float travelled = along - _Time.y * _RiverEdgeRippleDistortSpeed;

    // ── Debug ────────────────────────────────────────────────────────────────
    // Which way is the generated frame lying. Read off the frame BEFORE the distortion goes on, so
    // a wrong axis is not hidden behind a wobble.
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

    // Counted in whole lines rather than metres, so the window below is a plain fraction of a
    // cycle and the distortion is measured in line-widths.
    float s = band / spacing;

    // The distortion: one noise field. In flow space it is sampled in the line's own frame, so it
    // travels exactly along the lines and follows a channel round its bends; pinned to the world
    // instead, the field is nailed to the map.
    if (abs(_RiverEdgeRippleDistortStrength) > 0.0001)
    {
        float2 p = (framed && _RiverEdgeRippleFlowSpace > 0.5)
            ? float2(travelled, side)
            : WorldPos.xz;

        s += RiverEdgeGradientNoise(p * _RiverEdgeRippleDistortScale) * _RiverEdgeRippleDistortStrength;
    }

    // SUBTRACTING the phase is what carries the LINES themselves: a crest sits where the cycle
    // comes round, and that place is Speed metres further along every second.
    float cyc     = s - _Time.y * (_RiverEdgeRippleSpeed / spacing);
    float centred = abs(frac(cyc) - 0.5) * 2.0;   // 0 at a line's centre, 1 at the cycle edge

    // The window. Held just under 1 so neighbouring lines always keep a seam between them rather
    // than merging into a wash. Softness slides the inner edge of the falloff in toward the
    // centre. The epsilon keeps the two apart, since smoothstep divides by the gap.
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
    half4 BankData,
    out half  Ripples,
    out half  Edge,
    out half  Mask,
    out half  Fade,
    out half3 Debug,
    out half  DebugMix,
    out half  Grain)
{
    float  r, e, m, f, dm, g;
    float3 d;
    RiverEdgeRipples_float(EdgeData, FlowData, WorldPos, BankData, r, e, m, f, d, dm, g);
    Ripples  = (half)r;
    Edge     = (half)e;
    Mask     = (half)m;
    Fade     = (half)f;
    Debug    = (half3)d;
    DebugMix = (half)dm;
    Grain    = (half)g;
}

#endif
