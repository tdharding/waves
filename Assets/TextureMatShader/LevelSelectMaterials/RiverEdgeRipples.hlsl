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
// pool and which mouth records are its own: its rings are measured in world space from the pool
// object's own origin and the shore those records describe, so however the pool's water happens
// to be cut into faces, the rings cannot tell.
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
//   * the wander is a gradient noise field, a strength and a scale, like _RockRingDistort* —
//     each water has a SECOND one laid over the first, off its own strength, scale and speed,
//     which is dead at 0 and leaves the water as the one field made it;
//   * the WINDOW a line is cut with has its own Width per water — Width the river lines', Pool
//     Width the rings'. Strength and Softness are shared, so however differently the two are cut
//     they cannot drift apart in brightness or softness;
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

// Strength and Softness are shared by rivers and pools, so the two waters cannot drift apart.
// Width is the RIVER lines' — a pool's rings carry their own below.
float _RiverEdgeRippleStrength;   // peak whiteness of a line; 0 = the effect is gone
float _RiverEdgeRippleWidth;      // fraction of its cycle a river line fills
float _RiverEdgeRippleSoftness;   // 0 = a hard-edged line or ring; 1 = falls away from its centre

// Rivers.
float _RiverEdgeRippleSpacing;         // metres between one line and the next
float _RiverEdgeRippleSpeed;           // metres per second the LINES travel; 0 holds them still
float _RiverEdgeRippleReach;           // PERCENT of the river's width inward from a bank its lines carry
float _RiverEdgeRippleBevel;           // 0 = they ease away; 1 = they hold, then stop on a crease
float _RiverEdgeRippleDistortStrength; // in line-widths: 0.1 shifts a line by a tenth of a spacing
float _RiverEdgeRippleDistortScale;    // frequency of that noise field
float _RiverEdgeRippleDistortSpeed;    // metres per second the distortion travels down a channel
float _RiverEdgeRippleFlowSpace;       // 1 = the field travels along the lines; 0 = pinned to world
float _RiverEdgeRippleSecondDistortStrength; // a SECOND wander over the first, also in line-widths; 0 = off
float _RiverEdgeRippleSecondDistortScale;    // frequency of that second field
float _RiverEdgeRippleSecondDistortSpeed;    // metres per second the second wander travels down a channel

// Pools.
float _RiverEdgeRipplePoolSpacing;         // metres between one ring and the next
float _RiverEdgeRipplePoolReach;           // metres inward from the pool's waterline rings carry
float _RiverEdgeRipplePoolSpeed;           // metres per second the rings travel outward
float _RiverEdgeRipplePoolDistortStrength; // in ring-widths
float _RiverEdgeRipplePoolDistortScale;    // world-space frequency of that noise field
float _RiverEdgeRipplePoolDistortSpeed;    // turns per second the field spins round the centre
float _RiverEdgeRipplePoolWidth;           // fraction of its cycle a ring fills — the rings' own
float _RiverEdgeRipplePoolSecondDistortStrength; // a SECOND wander over the first, also in ring-widths; 0 = off
float _RiverEdgeRipplePoolSecondDistortScale;    // world-space frequency of that second field
float _RiverEdgeRipplePoolSecondDistortSpeed;    // turns per second the second field spins round the centre

// Joins.
float _RiverEdgeRippleJoinOverlap;         // metres past a butt line one water's look hands over to the next's

// Where the waters open into one another — RiverWaterMouth.cs, pushed by ApplyAesthetics every
// frame, always this long. One record per pool mouth, in world XZ:
//   A  (pool centre x, z, unit direction out along the river x, z)
//   B  (one corner x, z, the other corner x, z) — where the river's waterlines meet the pool's
//   C  (pool's outer waterline radius, metres out along A's direction to the butt line, kind, 0)
//   D  zero
// A junction — a branch leaving a river, recorded against that river — is laid out differently:
//   A  (one corner x, z, the other corner x, z) — where the branch's waterlines meet the river's near one
//   B  (branch heading x, z, a point on the branch's centreline x, z)
//   C  (branch waterline half-width, the branch's along at that point, kind 1, 1 = near bank is UV1.y)
//   D  (unit direction across the river toward the branch x, z, butt line as dot(p, that), reach)
// A water surface's UV3 says which records are its own; see RiverEdgeLines.
#define RIVER_MOUTH_MAX 64
#define RIVER_MOUTHS_PER_POOL 8
#define RIVER_JUNCTIONS_PER_RIVER 8
float4 _RiverMouthA[RIVER_MOUTH_MAX];
float4 _RiverMouthB[RIVER_MOUTH_MAX];
float4 _RiverMouthC[RIVER_MOUTH_MAX];
float4 _RiverMouthD[RIVER_MOUTH_MAX];

// The grain: one still gradient noise field over every water, river and pool alike.
float _RiverEdgeRippleGrainStrength;       // how far the grain pushes the colour either way; 0 = none
float _RiverEdgeRippleGrainSize;           // metres across one grain
float _RiverEdgeRippleGrainAlpha;          // how far the same grain thins the alpha; 0 = none, 1 = clear at its darkest

// Wave Bands: the interior water's family of lines running right across the map in one direction
// (WaveBands.hlsl), brought over without the rock or boat disturbance. World-space, so one field
// runs straight across every river, pool and join. Drawn UNDER the edge lines and pool rings —
// see RiverEdgeRipples_float. Same units as the interior's, so its presets' numbers carry over.
float _RiverWaveBandAngle;           // degrees — which way the family of lines runs across the map
float _RiverWaveBandFrequency;       // bands per world unit
float _RiverWaveBandSpeed;           // radians of phase per second, as the interior's; one band per 2pi
float _RiverWaveBandStrength;        // peak whiteness of a band; 0 = the bands are gone
float _RiverWaveBandWidth;           // fraction of its cycle a band fills
float _RiverWaveBandSoftness;        // 0 = a hard-edged band; 1 = falls away from its centre
float _RiverWaveBandWaviness;        // how far a band swings side to side, in band-widths
float _RiverWaveBandWavinessScale;   // how often that swing repeats along a band's length
float _RiverWaveBandMeanderStrength; // irregular drift on top of the swing, in band-widths
float _RiverWaveBandMeanderScale;    // world-space frequency of that drift
float _RiverWaveBandGrainStrength;   // how hard the water's grain eats into the bands; 0 = not at all

// Low resolution: the whole effect drawn in square blocks. See RiverEdgeBlocks below.
float _RiverEdgeRippleBlockSize;           // metres (or screen pixels) across one block; 0 = full resolution
float _RiverEdgeRippleBlockScreen;         // 1 = blocks are screen pixels; 0 = metres pinned to the world
float _RiverEdgeRippleLowResStyle;         // 0 = square blocks; 1 = scatter (Block Size is then the radius)
float _RiverEdgeRippleScatterGrain;        // scatter: size of one grain that reads from its own random point

// Contrast on the water's finished colour — see RiverWaterContrast at the bottom.
float _RiverEdgeRippleContrast;            // 1 = as it is; higher pulls lights and darks apart

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

// Two random numbers in 0-1 from a grid cell — the scatter's grain.
float2 RiverEdgeHash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// ── Low resolution ───────────────────────────────────────────────────────────
// Moves every input to the middle of the block this pixel sits in, so every pixel in a block reads
// the same numbers and draws the same colour — the whole effect comes out chunky.
//
// The world position can simply be snapped, but the frame is baked into the mesh and only exists
// where the pixel is. So the snap is turned into a step across the SCREEN — how many pixels right
// and down the block's middle lies — and every input is carried that far on its own screen
// derivatives. The frame is near-linear over a block, so that lands where the mesh would have.
//
// Blocks are metres on the world's XZ (stuck to the water, lined up across every join) or pixels
// on screen (the same size wherever the water is). Kind and lap length are not moved: they are one
// number per mesh. Nor is UV3, for the same reason — it says which mouths, and is one per mesh.
void RiverEdgeBlocks(inout float4 EdgeData, inout float4 FlowData, inout float3 WorldPos)
{
    float size = _RiverEdgeRippleBlockSize;
    if (size <= 0.0001) return;

    bool   onScreen = _RiverEdgeRippleBlockScreen > 0.5;
    float2 q;
    if (onScreen)
    {
        float4 clip = TransformWorldToHClip(WorldPos);
        q = (clip.xy / clip.w * 0.5 + 0.5) * _ScreenParams.xy;
    }
    else
    {
        q = WorldPos.xz;
    }

    // Where this pixel reads from. Blocks: the middle of its square. Scatter: a random point up
    // to Block Size away, the same for every pixel in one grain, so the effect's edges break up
    // into sand. The grain is on the same grid as the blocks would be — the world's, or the
    // screen's — so world scatter holds still on the water as the camera moves.
    float2 toMiddle;
    if (_RiverEdgeRippleLowResStyle > 0.5)
    {
        float2 cell = floor(q / max(_RiverEdgeRippleScatterGrain, 0.0001));
        float2 r    = RiverEdgeHash22(cell);
        float  a    = r.y * RIVER_RIPPLE_TWO_PI;
        toMiddle    = float2(cos(a), sin(a)) * sqrt(r.x) * size;   // even over the disc
    }
    else
    {
        toMiddle = (floor(q / size) + 0.5) * size - q;
    }

    // Solve toMiddle = sx * ddx(q) + sy * ddy(q) for the screen step (sx, sy).
    float2 qx  = ddx(q);
    float2 qy  = ddy(q);
    float  det = qx.x * qy.y - qx.y * qy.x;
    if (abs(det) < 1e-12) return;   // water seen edge-on: nothing sensible to step across

    float sx = (toMiddle.x * qy.y - toMiddle.y * qy.x) / det;
    float sy = (toMiddle.y * qx.x - toMiddle.x * qx.y) / det;

    EdgeData.xyz += ddx(EdgeData.xyz) * sx + ddy(EdgeData.xyz) * sy;
    FlowData.xyz += ddx(FlowData.xyz) * sx + ddy(FlowData.xyz) * sy;
    WorldPos    += ddx(WorldPos)     * sx + ddy(WorldPos)     * sy;

    // On the world grid the answer is known exactly; put it back so neighbouring blocks cannot
    // drift apart by the derivatives' rounding.
    if (!onScreen) WorldPos.xz = q + toMiddle;
}

// EdgeData is UV1, straight off a river's generated water — how far this corner lies from the
// lines the water is really seen to end on:
//   .x  metres to the waterline on one side   .y  metres to the waterline on the other
//   .z  metres along the channel              .w  unused, 0 (was a lap's length)
//
// Both distances are held as straight lines across the water rather than as a distance to the
// nearer of the two, because a water ribbon is only two vertices wide and a nearest-edge distance
// folds down the middle. The fold is made here, per pixel, where it costs a min(). A pool carries
// the same numbers on every vertex: .x its outer waterline radius, .y its island's (-1 for none),
// .z the radius of its circle — where its strips set off down the rivers — and .w 0.
//
// FlowData is UV2:
//   .x  ALONG   river: metres down the channel
//   .y  ACROSS  river: metres off the centreline
//   .z  unused, 1 (was the lap ramp — no water laps over another's now)
//   .w  KIND    0 none, 1 river, 2 pool
//
// MouthData is UV3: which of the mouth records above this water reads — the same on every
// corner of a mesh, so it never changes across a face:
//   pool   .x first record + 1   .y how many — every mouth that pool opens
//   river  .x record + 1 of the pool mouth at its start   .y the same at its end
//          .z first record + 1 of the junctions its branches open   .w how many
// 0 is none. Water built before the records existed reads 0 everywhere and draws as it always
// did: plain rings, plain bank lines.
//
// Water generated before any of this existed carries no UV2 at all, which reads as zero — kind 0.
// That falls back to lines along the banks with a world-pinned distortion. Rebuild Runs is what
// gives a surface its frame.
//
// Ripples : >= 0, already scaled by Strength. 0 anywhere no line falls, so it stays neutral into
//           an Add. Not capped at 1. Carries the Wave Bands too, under the lines.
// Edge    : metres to the nearer waterline (rivers; 0 on a pool).
// Mask    : 1 where the lines are at full, 0 where they are gone, on the curve they fade with.
// Fade    : THE ALPHA THE WATER IS DRAWN AT — wired into the water graph's Alpha. 1 on any framed
//           surface (every water butts onto the next, nothing laps), 0 on water with no frame, and
//           thinned by Grain Alpha.
// Debug   : a colour showing the frame this pixel was drawn in — black while Debug is off.
// DebugMix: 1 while Debug is on, 0 otherwise. Lerp BaseColor towards Debug by this.
// Grain   : SIGNED, centred on 0 — brightens and darkens by up to about Grain Strength. Add it to
//           BaseColor. Sampled in world XZ so it runs straight across every join, and it does not
//           move. 0 everywhere at Grain Strength 0, so the water is exactly as it was.
//
// The record a number on UV3 names, or -1 for none.
int RiverMouthRecord(float n)
{
    int k = (int)round(n) - 1;
    return (k >= 0 && k < RIVER_MOUTH_MAX) ? k : -1;
}

// Metres from a point on a pool to the shore the pool REALLY has: its outer waterline circle, with
// a gap wherever a river comes in, and that river's two waterlines running on out from the gap's
// corners. Measured from that, the rings bend round each corner and carry on down the river as
// its bank lines, rather than running on across the mouth as though the circle were whole.
//
// Away from every mouth this is exactly the circle's distance — a corner, or a waterline running
// out from one, is never nearer than the circle is — so the body of the pool is untouched.
// Negative out under the bank, as the circle's own distance is; nothing there is seen.
float PoolShoreDistance(float2 p, float2 centre, float outerShore, float4 MouthData)
{
    float2 offset = p - centre;
    float  arc    = outerShore - length(offset);

    int first = RiverMouthRecord(MouthData.x);
    if (first < 0) return arc;
    int count = min((int)round(MouthData.y), RIVER_MOUTHS_PER_POOL);

    float banks = 1e6;
    float gap   = 1e6;
    bool  inGap = false;

    [loop]
    for (int i = 0; i < count; i++)
    {
        int k = first + i;
        if (k >= RIVER_MOUTH_MAX) break;

        float2 dir = _RiverMouthA[k].zw;
        float2 ka  = _RiverMouthB[k].xy - centre;
        float2 kb  = _RiverMouthB[k].zw - centre;

        // Inside the wedge the two corners make with the pool's middle there is no circle — the
        // river has taken it — and the nearest the shore comes is one of those corners.
        float turn   = ka.x * kb.y - ka.y * kb.x;
        float ca     = ka.x * offset.y - ka.y * offset.x;
        float cb     = offset.x * kb.y - offset.y * kb.x;
        bool  within = turn >= 0.0 ? (ca >= 0.0 && cb >= 0.0) : (ca <= 0.0 && cb <= 0.0);

        float2 pa = offset - ka;
        float2 pb = offset - kb;
        float  da = length(pa);
        float  db = length(pb);
        if (within) { inGap = true; gap = min(gap, min(da, db)); }

        // The river's waterlines, each a line running out from its corner down the river: square
        // across to it once past the corner, straight to the corner before it.
        float ra = dot(pa, dir) > 0.0 ? abs(pa.x * dir.y - pa.y * dir.x) : da;
        float rb = dot(pb, dir) > 0.0 ? abs(pb.x * dir.y - pb.y * dir.x) : db;
        banks = min(banks, min(ra, rb));
    }

    return min(inGap ? gap : arc, banks);
}

// A pool's rings at one pixel. Ring is how far out from the pool's middle the ring falling here
// lies — the radius itself in the body of the pool, bent round a mouth's corners near one. Shore
// is metres to the shore, for Pool Reach. Used by the pool's own water and by a river's first
// Join Overlap metres, so the two draw the same rings where they meet.
void PoolRingsAt(float2 p, float2 centre, float ring, float shore,
                 out float Ripples, out float Mask)
{
    float2 offset = p - centre;

    // The noise field is the rock rings' — world scale — but turned round the pool's centre by
    // Pool Distortion Speed, so the wobble travels round the rings while they stand still. A
    // turn about the centre has no seam and no pinch: the middle just turns on the spot.
    float distort = 0.0;
    if (abs(_RiverEdgeRipplePoolDistortStrength) > 0.0001)
    {
        float  turn = _Time.y * _RiverEdgeRipplePoolDistortSpeed * RIVER_RIPPLE_TWO_PI;
        float  c = cos(turn), sn = sin(turn);
        float2 spun = float2(offset.x * c - offset.y * sn, offset.x * sn + offset.y * c);

        distort = RiverEdgeGradientNoise((centre + spun) * _RiverEdgeRipplePoolDistortScale)
                * _RiverEdgeRipplePoolDistortStrength;
    }

    // The second wander, laid over the first: the two are ADDED into the one push, so what
    // this moves is the ring the spacing and the first field have already made. Its own turn
    // about the centre, at its own speed, so it can turn slower, faster or the other way
    // rather than being carried round with the first. Its field is offset far enough to be a
    // different patch of noise — at a matching scale the two would otherwise land on top of
    // each other and only ever read as one stronger wander. Its own guard, so it draws with
    // the first at 0 as readily as over it. The river's second wander is the same idea in the
    // channel's frame.
    if (abs(_RiverEdgeRipplePoolSecondDistortStrength) > 0.0001)
    {
        float  turn2 = _Time.y * _RiverEdgeRipplePoolSecondDistortSpeed * RIVER_RIPPLE_TWO_PI;
        float  c2 = cos(turn2), sn2 = sin(turn2);
        float2 spun2 = float2(offset.x * c2 - offset.y * sn2, offset.x * sn2 + offset.y * c2);

        distort += RiverEdgeGradientNoise((centre + spun2) * _RiverEdgeRipplePoolSecondDistortScale
                                          + float2(37.1, 17.3))
                 * _RiverEdgeRipplePoolSecondDistortStrength;
    }

    // Strength and Softness are the shared ones — the two waters keep the same brightness and
    // edge. The Width is the rings' own, so a pool can carry fatter lines than its rivers.
    PoolRings(ring, distort, _Time.y,
              max(_RiverEdgeRipplePoolSpacing, 0.0001),
              _RiverEdgeRippleStrength,
              _RiverEdgeRipplePoolSpeed,
              _RiverEdgeRipplePoolWidth,
              _RiverEdgeRippleSoftness,
              Ripples, Mask);

    // Pool Reach: the rings belong to the waterline, strongest there and gone Reach metres
    // in, on the river lines' own curve. Out past it, under the bank, they stay at full.
    Mask     = RiverEdgeSmootherStep(1.0 - saturate(shore / max(_RiverEdgeRipplePoolReach, 0.0001)));
    Ripples *= Mask;
}

// Join Overlap, on a river's water: for the first this-many metres past the line it butts onto a
// pool's water, the river's lines cross-fade over from the pool's rings — the pool's own rings,
// drawn on the river's own shore distance, which near a mouth is the distance the pool's strip
// draws them on too — so nothing changes on the butt line itself. 0 hands over on the line.
void RiverJoinOverlap(float2 p, float shore, float4 MouthData,
                      inout float Ripples, inout float Mask)
{
    float overlap = max(_RiverEdgeRippleJoinOverlap, 0.0);
    int   at      = -1;
    float lines   = 1.0;   // how far over to the river's own lines; 1 = wholly the river's

    [unroll]
    for (int i = 0; i < 2; i++)
    {
        int k = RiverMouthRecord(i == 0 ? MouthData.x : MouthData.y);
        if (k < 0) continue;

        // Metres on down the river past the butt line.
        float past = dot(p - _RiverMouthA[k].xy, _RiverMouthA[k].zw) - _RiverMouthC[k].y;
        float t    = overlap > 0.0001 ? RiverEdgeSmootherStep(saturate(past / overlap))
                                      : (past >= 0.0 ? 1.0 : 0.0);
        if (t < lines) { lines = t; at = k; }
    }
    if (at < 0 || lines >= 1.0) return;

    float ringRipples, ringMask;
    PoolRingsAt(p, _RiverMouthA[at].xy, _RiverMouthC[at].x - shore, shore, ringRipples, ringMask);

    Ripples = lerp(ringRipples, Ripples, lines);
    Mask    = lerp(ringMask,    Mask,    lines);
}

// Metres from a point on a river to the shore it REALLY has where branches leave it: its near
// bank with a gap wherever a branch opens, and that branch's two waterlines running on out from
// the gap's corners. Measured from that, the bank lines round each corner and carry on down the
// branch as its own bank lines, rather than running straight across the mouth.
//
// Away from every mouth this is exactly min(EdgeData.x, EdgeData.y) — a corner, or a waterline
// running out from one, is never nearer than the bank is — so the rest of the river is untouched.
//
// Toward is how far over to the BRANCH's look this pixel is: 1 on the line the branch's water
// butts onto, gone Join Overlap metres back into the river and Join Overlap out to either side of
// the branch's width. There the lines take the branch's own wander and Reach, so they arrive at
// the butt line exactly as the branch's water draws them. At names the junction it is toward.
float RiverJunctionShore(float2 p, float4 EdgeData, float4 MouthData, out float Toward, out int At)
{
    Toward = 0.0;
    At     = -1;

    float e = min(EdgeData.x, EdgeData.y);

    int first = RiverMouthRecord(MouthData.z);
    if (first < 0) return e;
    int count = min((int)round(MouthData.w), RIVER_JUNCTIONS_PER_RIVER);

    float overlap = max(_RiverEdgeRippleJoinOverlap, 0.0001);
    float corners = 1e6;    // nearest of every mouth's corners and branch waterlines
    float far     = e;      // the bank left standing once a gap has taken the near one

    [loop]
    for (int i = 0; i < count; i++)
    {
        int k = first + i;
        if (k >= RIVER_MOUTH_MAX) break;

        // A record that is not a junction — never pushed, or pushed from data older than this
        // mesh — is left out, so the river draws as it would with no junction rather than
        // reading a zero-width branch at the world's origin.
        if (abs(_RiverMouthC[k].z - 1.0) > 0.5) continue;

        float2 ka   = _RiverMouthA[k].xy;
        float2 kb   = _RiverMouthA[k].zw;
        float2 dir  = _RiverMouthB[k].xy;
        float2 o    = _RiverMouthB[k].zw;
        float  hb   = _RiverMouthC[k].x;
        float2 n    = _RiverMouthD[k].xy;
        float  cut  = _RiverMouthD[k].z;
        float2 side = float2(dir.y, -dir.x);   // the branch's right, as its own UV2.y runs

        // Toward the branch: back from the butt line, and within the branch's width either side.
        float past = dot(p, n) - cut;                  // 0 on the butt line, negative into the river
        float wide = abs(dot(p - o, side)) - hb;       // negative inside the branch's waterlines
        float t    = RiverEdgeSmootherStep(saturate(1.0 + past / overlap))
                   * (1.0 - RiverEdgeSmootherStep(saturate(wide / overlap)));
        if (t > Toward) { Toward = t; At = k; }

        if (length(p - (ka + kb) * 0.5) > _RiverMouthD[k].w) continue;

        // The branch's waterlines, each running out from its corner down the branch: square
        // across to it once past the corner, straight to the corner before it. The branch's own
        // water measures square across to the whole LINE, behind the corner too — which is only
        // different where a leaning branch has its water there — so on the way to the butt line
        // these hand over to that, and the two waters agree on it.
        float2 pa = p - ka;
        float2 pb = p - kb;
        float  la = abs(dot(pa, side));
        float  lb = abs(dot(pb, side));
        float  ra = lerp(dot(pa, dir) > 0.0 ? la : length(pa), la, t);
        float  rb = lerp(dot(pb, dir) > 0.0 ? lb : length(pb), lb, t);
        corners = min(corners, min(ra, rb));

        // Square across from the gap between the corners there is no near bank: the branch has
        // taken it, and only the far bank is left. Either side of the gap the near bank is there,
        // but measured UNSIGNED: a leaning branch's water runs out past the river's waterline
        // beyond its sharp corner, where the signed distance has already gone negative.
        bool   nearIsY  = _RiverMouthC[k].w > 0.5;
        float  nearBank = nearIsY ? EdgeData.y : EdgeData.x;
        float  farBank  = nearIsY ? EdgeData.x : EdgeData.y;
        float2 along    = float2(-n.y, n.x);
        far = dot(pa, along) * dot(pb, along) <= 0.0 ? farBank : min(farBank, abs(nearBank));
    }

    return min(far, corners);
}

// A river line's wander at one pixel, in the frame given: along and across the channel in flow
// space, or nailed to the world. Both fields, added — see the river lines below.
float RiverLineWander(float along, float side, float2 world, bool flow)
{
    float wander  = 0.0;

    if (abs(_RiverEdgeRippleDistortStrength) > 0.0001)
    {
        float2 p = flow ? float2(along - _Time.y * _RiverEdgeRippleDistortSpeed, side) : world;
        wander += RiverEdgeGradientNoise(p * _RiverEdgeRippleDistortScale) * _RiverEdgeRippleDistortStrength;
    }

    if (abs(_RiverEdgeRippleSecondDistortStrength) > 0.0001)
    {
        float2 p2 = flow ? float2(along - _Time.y * _RiverEdgeRippleSecondDistortSpeed, side) : world;
        wander += RiverEdgeGradientNoise(p2 * _RiverEdgeRippleSecondDistortScale + float2(37.1, 17.3))
                * _RiverEdgeRippleSecondDistortStrength;
    }

    return wander;
}

// The lines and rings alone, on numbers RiverEdgeRipples_float has already put through the blocks.
// The grain and the Wave Bands go on out there, so the early returns in here cannot skip them.
void RiverEdgeLines(
    float4 EdgeData,
    float4 FlowData,
    float3 WorldPos,
    float4 MouthData,
    out float  Ripples,
    out float  Edge,
    out float  Mask,
    out float  Fade,
    out float3 Debug,
    out float  DebugMix)
{
    Ripples  = 0.0;
    Edge     = 0.0;
    Mask     = 0.0;
    Fade     = 1.0;
    Debug    = float3(0.0, 0.0, 0.0);
    DebugMix = 0.0;

    float kind   = FlowData.w;
    bool  framed = kind > 0.5;
    bool  isPool = kind > 1.5;

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

        // How far this pixel lies from the shore really seen — the circle, opened at each mouth
        // and carried on down that river's banks. The ring drawn here is the one that far in from
        // the outer waterline: the radius itself in the body of the pool, bent round a mouth's
        // corners near one. The island's side is left measured off its circle, as it was.
        float shore = PoolShoreDistance(WorldPos.xz, centre.xz, EdgeData.x, MouthData);
        float e     = shore;
        if (EdgeData.y >= 0.0) e = min(e, radius - EdgeData.y);
        Edge = e;

        PoolRingsAt(WorldPos.xz, centre.xz, EdgeData.x - shore, e, Ripples, Mask);

        if (_RiverEdgeRippleDebug > 0.5)
        {
            float b = frac((EdgeData.x - shore) / poolSpacing);   // the rings as drawn, bends and all
            float a = frac(atan2(offset.x, offset.y) / RIVER_RIPPLE_TWO_PI);

            float mode = _RiverEdgeRippleDebug;
            if      (mode < 1.5) Debug = float3(b, b * 0.25, 0.0);              // bands, red
            else if (mode < 2.5) Debug = float3(0.0, a, a * 0.35);              // along, green
            else if (mode < 3.5) Debug = float3(Fade, Fade * 0.6, 1.0 - Fade);  // the alpha
            else                 Debug = float3(b, a, 0.35);                    // both, as a check

            DebugMix = 1.0;
        }

        return;
    }

    float spacing = max(_RiverEdgeRippleSpacing, 0.0001);

    // River water built before there was any edge data carries none, and a mesh with no UV1 reads
    // as zero — a waterline under every pixel at once. It draws nothing instead: rebuild the runs
    // and it comes back.
    if (EdgeData.x == 0.0 && EdgeData.y == 0.0)
    {
        Fade = 0.0;
        return;
    }

    // The fold: whichever bank this pixel is nearer to is the one it belongs to. Both banks draw
    // the same pattern from their own edge, so a channel is ripples down each side with clear
    // water between them. Where a branch leaves, the near bank is opened and its lines round the
    // mouth's corners instead — see RiverJunctionShore.
    float toward;
    int   junction;
    float e = RiverJunctionShore(WorldPos.xz, EdgeData, MouthData, toward, junction);
    Edge = e;

    // ── Where the lines stop ─────────────────────────────────────────────────
    // A river's lines belong to its banks, so they are strongest on the waterline and gone by
    // Reach. Reach is a PERCENTAGE of the river's width here, not metres, so every river carries
    // its lines in proportion to itself. The two distances are straight lines to the two
    // waterlines, so together they are the width at this pixel — nothing needs rebuilding.
    // Near a branch's butt line it is the branch's width, which is what its own water reads there.
    float width = EdgeData.x + EdgeData.y;
    if (junction >= 0) width = lerp(width, 2.0 * _RiverMouthC[junction].x, toward);
    float reach = _RiverEdgeRippleReach * 0.01 * width;
    float h    = 1.0 - saturate(e / max(reach, 0.0001));
    float mask = lerp(RiverEdgeSmootherStep(h), h, saturate(_RiverEdgeRippleBevel));

    Mask = mask;

    // ── The frame ────────────────────────────────────────────────────────────
    // band  — the coordinate the lines are cut across, so a step of Spacing in it is one line.
    // along — the coordinate that runs ALONG a line, which is the way the distortion travels.
    // side  — the second axis of the distortion's own space, so the noise is a field.
    float band  = e;                                 // metres in from the nearer bank
    float along = framed ? FlowData.x : EdgeData.z;  // metres down the channel
    float side  = framed ? FlowData.y : 0.0;

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
        else if (mode < 3.5) Debug = float3(Fade, Fade * 0.6, 1.0 - Fade);  // the alpha
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
    //
    // The second wander is laid over the first: it pushes the ALREADY-PUSHED line rather than
    // sharing the work with it. Its field is the same frame, offset far enough that it is a
    // different patch of noise — at a matching scale the two would otherwise land on top of each
    // other and only ever read as one stronger wander. Both in RiverLineWander.
    //
    // Unframed water has no channel to follow and is always nailed to the world.
    bool  flow   = framed && _RiverEdgeRippleFlowSpace > 0.5;
    float wander = RiverLineWander(along, side, WorldPos.xz, flow);

    // Toward a branch's butt line the wander hands over to the branch's own — its frame run on
    // straight back from where its water starts, which is exactly what its water reads on the line.
    if (flow && junction >= 0 && toward > 0.0)
    {
        float2 dir    = _RiverMouthB[junction].xy;
        float2 offset = WorldPos.xz - _RiverMouthB[junction].zw;
        float  bAlong = _RiverMouthC[junction].y + dot(offset, dir);
        float  bSide  = dot(offset, float2(dir.y, -dir.x));
        wander = lerp(wander, RiverLineWander(bAlong, bSide, WorldPos.xz, true), toward);
    }

    s += wander;

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

    // Softness past 1 blurs the lines. 1 to 5.5 rounds the window into a smooth swell — a cosine
    // across the cycle — and 5.5 to 10 flattens that swell to an even glow. Both hold the line's
    // AVERAGE brightness, (inner + outer) / 2 of the cycle, so blurring spreads the light rather
    // than dimming or brightening the water. The swell's height is held inside 0..1 so it stays
    // white by construction.
    float blur = saturate((_RiverEdgeRippleSoftness - 1.0) / 9.0);
    if (blur > 0.0)
    {
        float mean  = (inner + outer) * 0.5;
        float swell = mean + min(mean, 1.0 - mean) * cos(centred * 3.14159265);
        pulse = blur < 0.5 ? lerp(pulse, swell, blur * 2.0)
                           : lerp(swell, mean, blur * 2.0 - 1.0);
    }

    Ripples = max(pulse * mask * _RiverEdgeRippleStrength, 0.0);
}

// ── Wave Bands ───────────────────────────────────────────────────────────────
// WaveBands.hlsl's lines, minus the rocks and the boat: one direction across the whole map, a
// shared sine swing, a noise meander off it, cut as a window. In world XZ, so the one field runs
// straight across rivers, pools and every join with nothing baked into the mesh. The phase is
// _Time rather than CPU-accumulated — nothing here pushes a phase — in the interior's units, so a
// Speed copied from a WavePreset moves the same.
//
// The water's grain eats into the bands, as the interior's fine grain does on its lines — lifted
// to 0..1 so it only ever takes away, and floored so it cannot darken the water underneath. It is
// the SAME field as the colour grain, so the speckle in the gaps and the wear on the bands match.
float RiverWaveBands(float3 WorldPos, float grainNoise)
{
    if (_RiverWaveBandStrength <= 0.0) return 0.0;

    float  a    = radians(_RiverWaveBandAngle);
    float2 dir  = float2(cos(a), sin(a));
    float2 perp = float2(-dir.y, dir.x);

    float s = dot(WorldPos.xz, dir) * _RiverWaveBandFrequency;

    if (abs(_RiverWaveBandWaviness) > 0.0001)
    {
        s += sin(dot(WorldPos.xz, perp) * _RiverWaveBandWavinessScale) * _RiverWaveBandWaviness;
    }

    if (abs(_RiverWaveBandMeanderStrength) > 0.0001)
    {
        s += RiverEdgeGradientNoise(WorldPos.xz * _RiverWaveBandMeanderScale) * _RiverWaveBandMeanderStrength;
    }

    float cyc     = s - _Time.y * _RiverWaveBandSpeed * (1.0 / RIVER_RIPPLE_TWO_PI);
    float centred = abs(frac(cyc) - 0.5) * 2.0;

    float w     = clamp(_RiverWaveBandWidth, 0.01, 0.98);
    float soft  = saturate(_RiverWaveBandSoftness);
    float inner = w * (1.0 - soft);
    float outer = max(w, inner + 1e-4);
    float bandMask = saturate(1.0 - smoothstep(inner, outer, centred));

    float bands = bandMask * _RiverWaveBandStrength;

    if (_RiverWaveBandGrainStrength > 0.0001)
    {
        bands -= saturate(grainNoise + 0.5) * _RiverWaveBandGrainStrength * bandMask;
    }

    return max(bands, 0.0);
}

void RiverEdgeRipples_float(
    float4 EdgeData,
    float4 FlowData,
    float3 WorldPos,
    float4 MouthData,
    out float  Ripples,
    out float  Edge,
    out float  Mask,
    out float  Fade,
    out float3 Debug,
    out float  DebugMix,
    out float  Grain)
{
    Grain = 0.0;

    // First, so everything after — grain, fades, bands, lines and rings — reads the block's numbers.
    RiverEdgeBlocks(EdgeData, FlowData, WorldPos);

    // ── The grain ────────────────────────────────────────────────────────────
    // Before anything else, so pools and rivers take the same grain. The noise runs about ±0.5,
    // doubled so Strength is roughly the most it moves the colour.
    float grainNoise = 0.0;
    if (abs(_RiverEdgeRippleGrainStrength) > 0.0001 || _RiverEdgeRippleGrainAlpha > 0.0001
        || (_RiverWaveBandStrength > 0.0 && _RiverWaveBandGrainStrength > 0.0001))
    {
        grainNoise = RiverEdgeGradientNoise(WorldPos.xz / max(_RiverEdgeRippleGrainSize, 0.0001));
        Grain = grainNoise * 2.0 * _RiverEdgeRippleGrainStrength;
    }

    RiverEdgeLines(EdgeData, FlowData, WorldPos, MouthData, Ripples, Edge, Mask, Fade, Debug, DebugMix);

    // A river's first stretch past a pool hands over from the pool's rings. Only on a framed
    // river with edge data — a pool's water, or water with no frame, has nothing to hand over.
    bool river = FlowData.w > 0.5 && FlowData.w < 1.5;
    if (river && Fade > 0.0) RiverJoinOverlap(WorldPos.xz, Edge, MouthData, Ripples, Mask);

    // ── Grain into the alpha ─────────────────────────────────────────────────
    // The same grain field, thinning the water where it runs dark: 0 leaves the alpha alone, 1
    // takes the darkest of the grain fully clear while its lightest stays solid. Its own amount,
    // separate from Grain Strength, so the alpha can take the grain with or without the colour.
    Fade *= 1.0 - saturate(_RiverEdgeRippleGrainAlpha) * saturate(0.5 - grainNoise);

    // ── The edges over the bands ─────────────────────────────────────────────
    // The edge lines and pool rings sit ON TOP: the bands are taken out wherever a line is lit, by
    // as much as it is lit, so where the two cross the line draws rather than the two adding up
    // brighter. Between the lines the bands show at full.
    float bands = RiverWaveBands(WorldPos, grainNoise);
    if (bands > 0.0)
    {
        float cover = saturate(Ripples / max(_RiverEdgeRippleStrength, 0.0001));
        Ripples += bands * (1.0 - cover);
    }
}

// Shader Graph appends _float or _half to the function name depending on graph precision, and a
// File custom function has to supply whichever it asks for. Forwards to the float version — the
// globals are float and the maths is cheap.
void RiverEdgeRipples_half(
    half4 EdgeData,
    half4 FlowData,
    half3 WorldPos,
    half4 MouthData,
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
    RiverEdgeRipples_float(EdgeData, FlowData, WorldPos, MouthData, r, e, m, f, d, dm, g);
    Ripples  = (half)r;
    Edge     = (half)e;
    Mask     = (half)m;
    Fade     = (half)f;
    Debug    = (half3)d;
    DebugMix = (half)dm;
    Grain    = (half)g;
}

// ── Final colour ─────────────────────────────────────────────────────────────
// Contrast on the water's FINISHED colour, so it goes last in the water graph, just before Base
// Color, and takes everything — the water, the lines, the fish, the wake — together. Pushed out
// from mid-grey; 1 hands the colour back untouched. Floored at 0 so it cannot go negative into
// whatever follows. Its own subgraph, River Water Contrast.
void RiverWaterContrast_float(float3 In, out float3 Out)
{
    Out = max((In - 0.5) * _RiverEdgeRippleContrast + 0.5, 0.0);
}

void RiverWaterContrast_half(half3 In, out half3 Out)
{
    float3 o;
    RiverWaterContrast_float(In, o);
    Out = (half3)o;
}

#endif
