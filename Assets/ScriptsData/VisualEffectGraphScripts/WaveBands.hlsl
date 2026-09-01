// Bands across the water — one family of long white lines running right across the map in a
// single direction, snaking as they go, and losing their composure wherever they pass a rock.
//
// This is a THIRD brightness source, alongside the map-wide rings from _WaveCenter
// (WavePeaksTroughs.hlsl) and the per-rock rings travelling outward (RockRings.hlsl). Those two
// both radiate from a point; this one does not radiate from anywhere. It is a directional field,
// which is what makes it read as the water's grain rather than as another ripple.
//
// The lines do NOT part or bow around a rock. Every rock adds into the SAME band coordinate, so
// the family stays one family — a rock simply makes the lines wobble harder as they cross it,
// and they settle back down past it. That was a deliberate choice over a radial push, which
// splits the lines apart and reads as an obstacle in a current rather than a disturbance.
//
// Brightness only — no displacement — so it rides the existing water fragment shader at no extra
// draw calls and no extra mesh density.
//
// ── White by construction ─────────────────────────────────────────────────────
// The line itself is a WINDOW, already 0..1, so there is no rectify control here as there is on
// the rock rings and none is needed. The grain is the one thing that pushes the other way — it
// subtracts on the lines — so the result is FLOORED at zero on the way out. Whatever this feeds,
// an Add can only ever brighten: a heavy grain eats a line away to nothing but never past it into
// darkening the water underneath. Turning Strength and Grain to 0 returns the water exactly to
// what it was. Note the floor is one-sided — the top is deliberately left open so Strength above
// 1 keeps its overbright headroom for bloom.
//
// ── Cost ─────────────────────────────────────────────────────────────────────
// One loop over the rocks RockRingManager pushed, which is NOT the whole level: it sorts by
// distance to the boat every frame and only pushes the nearest few inside its LOD radius. The
// loop breaks on the count (same shape as InstancedLights.hlsl), so it costs what is near the
// boat, not the ceiling. Pixels beyond a rock's reach `continue` before the maths, and a level
// with Strength at 0 skips the loop outright.
//
// ── Globals ──────────────────────────────────────────────────────────────────
// Bare $Globals, not blackboard properties. WaveMaterialController re-pushes them every frame
// through BOTH Material.Set* and Shader.SetGlobal* — a shader reimport wipes them, and re-pushing
// is what makes that self-heal on the next frame.

// The rocks come from RockRingManager, already LOD'd and sorted nearest-first — there is no second
// manager and no second upload for this effect. Guarded because RockRings.hlsl declares the same
// three, and both includes now land in the same shader; same reasoning as
// WHIRLPOOL_POSITIONS_DECLARED across the three whirlpool files.
#ifndef ROCK_RING_POSITIONS_DECLARED
#define ROCK_RING_POSITIONS_DECLARED
#define MAX_ROCK_RINGS 48

// .x/.z = world position   .y = LOD fade 0..1 (CPU, per rock)   .w = radius at the waterline
float4 _RockRingPositions[MAX_ROCK_RINGS];
float  _RockRingCount;
#endif

float _WaveBandAngle;       // degrees — which way the family of lines runs across the map
float _WaveBandFrequency;   // bands per world unit
float _WaveBandPhase;       // CPU-accumulated, exactly like _RockRingPhase — see below
float _WaveBandStrength;    // peak whiteness of a line; 0 = the effect is gone

// A band is a WINDOW cut out of its cycle, not a sine — the same construction as the rock rings,
// so both read as the same water. The top is flat at full height whatever the width, so widening
// a line changes how much of the cycle is lit without also brightening it.
float _WaveBandWidth;       // fraction of its cycle a line fills
float _WaveBandSoftness;    // 0 = a hard-edged line; 1 = falls away from its centre

// The waviness is two layers because the drawing has both. The sine gives every line the same
// steady swing, which is what makes them read as one family carried by one body of water. The
// noise then drifts them off that shared rhythm, so the field never looks stamped.
float _WaveBandWaviness;         // how far a line swings side to side, in band-widths
float _WaveBandWavinessScale;    // how often that swing repeats along a line's length
float _WaveBandMeanderStrength;  // irregular drift on top of the swing, in band-widths
float _WaveBandMeanderScale;     // world-space frequency of that drift

// Rock disturbance.
float _WaveBandRockDistort;  // how much harder the lines wobble at a rock, in band-widths
float _WaveBandRockReach;    // how far past the rock's waterline radius that reaches, as a multiple
float _WaveBandRockBevel;    // 0 = the wobble eases away; 1 = it holds, then stops on a crease

// Fine grain, INVERTED against the lines: it adds in the open water between them and subtracts on
// the lines themselves, so the same field speckles the gaps and erodes the bands. One field doing
// both is what stops it reading as a separate layer of noise sitting on top of the water.
//
// Static in world space, deliberately — it is the water's texture, not something travelling
// through it, and a crawling grain reads as film dirt rather than surface.
float _WaveBandGrainStrength;  // how hard it bites; 0 = no grain
float _WaveBandGrainSize;      // world-space size of one speck. Small = fine, large = coarse.

// The boat disturbs this family exactly as a rock does — same profile, same shared noise field,
// added into the SAME band coordinate, so the lines wobble as they pass it and settle again behind
// rather than parting around it. Its own controls, because a boat is not a rock: it needs its
// reach set in world units rather than as a multiple of a waterline radius it does not have.
// Pushed by BoatWakeBandsController alongside the boat's own band settings.
float4 _WaveBandBoatPos;     // .xz = boat world position
float _WaveBandBoatDistort;  // how much the lines wobble at the boat, in band-widths
float _WaveBandBoatReach;    // how far that reaches, in world units
float _WaveBandBoatBevel;    // 0 = the wobble eases away; 1 = it holds, then stops on a crease
float _WaveBandBoatScale;    // how fine the boat's disturbance is — its own, not the level's

#define WAVE_BANDS_TWO_PI 6.28318530718

// The same quintic the rest of the water uses (WaterSurface.hlsl / RockRings.hlsl), so a rock's
// disturbance dies away on the same curve as everything else rather than a curve of its own.
float WaveBandsSmootherStep(float h)
{
    return h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
}

// Gradient noise, matching Shader Graph's own Gradient Noise node so the field behaves the way it
// does when you preview one in the graph — same lattice, same gradients, same look at a given
// scale. Returns roughly -0.5..0.5, signed, which is what a push either way wants.
float2 WaveBandsNoiseDir(float2 p)
{
    p = fmod(p, 289.0);
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

float WaveBandsGradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);

    float d00 = dot(WaveBandsNoiseDir(ip),                     fp);
    float d01 = dot(WaveBandsNoiseDir(ip + float2(0.0, 1.0)),  fp - float2(0.0, 1.0));
    float d10 = dot(WaveBandsNoiseDir(ip + float2(1.0, 0.0)),  fp - float2(1.0, 0.0));
    float d11 = dot(WaveBandsNoiseDir(ip + float2(1.0, 1.0)),  fp - float2(1.0, 1.0));

    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y);
}

// Bands : >= 0, already scaled by _WaveBandStrength. 0 wherever no line falls and no grain lands,
//         so it stays neutral into an Add — the water keeps its own brightness between the lines,
//         and the effect can only ever brighten, never darken. Not capped at 1: Strength above 1
//         is real overbright, for bloom to pick up.
void WaveBands_float(
    float3 WorldPos,
    out float Bands)
{
    Bands = 0.0;
    if (_WaveBandStrength <= 0.0) return;

    // Across the lines, and along their length. The waviness runs on the ALONG axis, so a line
    // swings side to side over its own course rather than every line wobbling in place.
    float  a    = radians(_WaveBandAngle);
    float2 dir  = float2(cos(a), sin(a));
    float2 perp = float2(-dir.y, dir.x);

    // Counted in whole cycles rather than radians, so the window below can be measured as a plain
    // fraction and every amplitude here is in band-widths.
    float s = dot(WorldPos.xz, dir) * _WaveBandFrequency;

    // The steady swing every line shares.
    if (abs(_WaveBandWaviness) > 0.0001)
    {
        s += sin(dot(WorldPos.xz, perp) * _WaveBandWavinessScale) * _WaveBandWaviness;
    }

    // The irregular drift off it. Sampled once — the field is world space, so the answer does not
    // depend on which line is being drawn.
    if (abs(_WaveBandMeanderStrength) > 0.0001)
    {
        s += WaveBandsGradientNoise(WorldPos.xz * _WaveBandMeanderScale) * _WaveBandMeanderStrength;
    }

    // Rocks. Every one of them adds into s, the same coordinate the whole family is drawn from —
    // nothing here displaces a line sideways or splits it, which is what keeps them one family.
    if (abs(_WaveBandRockDistort) > 0.0001)
    {
        int   count  = min((int)_RockRingCount, MAX_ROCK_RINGS);
        float reachK = max(_WaveBandRockReach, 0.0001);
        float bevel  = saturate(_WaveBandRockBevel);

        for (int i = 0; i < MAX_ROCK_RINGS; i++)
        {
            if (i >= count) break;

            float4 p     = _RockRingPositions[i];
            float2 off   = WorldPos.xz - p.xz;
            float  d     = length(off);
            float  reach = max(p.w, 0.0001) * reachK;
            if (d > reach) continue;

            // 1 at the rock, 0 at the limit. Bevel picks the profile between the two: the quintic
            // eases the wobble away so you cannot see where it stops, while the straight line holds
            // its strength most of the way out and then stops on a visible crease.
            float h       = 1.0 - saturate(d / reach);
            float profile = lerp(WaveBandsSmootherStep(h), h, bevel);

            // The SAME field as the meander, sampled finer and offset by this rock's own position.
            // Reusing it is what makes the disturbance read as this water misbehaving rather than a
            // second effect laid over the top, and the offset is what stops every rock disturbing
            // the lines in the same way.
            float local = WaveBandsGradientNoise(WorldPos.xz * _WaveBandMeanderScale * 3.0 + p.xz);

            // p.y is the LOD fade the manager worked out on the CPU, so a rock leaving its budget
            // fades its disturbance out instead of snapping it off.
            s += local * profile * _WaveBandRockDistort * p.y;
        }
    }

    // The boat, on the same terms as the rocks above — into s, not sideways, so the family stays
    // one family. No LOD fade here: there is only ever one boat, and it is always near the camera.
    if (abs(_WaveBandBoatDistort) > 0.0001)
    {
        float2 boff  = WorldPos.xz - _WaveBandBoatPos.xz;
        float  bd    = length(boff);
        float  reach = max(_WaveBandBoatReach, 0.0001);

        if (bd <= reach)
        {
            float h       = 1.0 - saturate(bd / reach);
            float profile = lerp(WaveBandsSmootherStep(h), h, saturate(_WaveBandBoatBevel));

            // The same NOISE as the meander and the rocks — so it reads as this water misbehaving
            // rather than a second effect laid over the top — but at the boat's OWN scale, so how
            // fine its disturbance is does not ride on the level's meander setting.
            //
            // The offset is a FIXED seed, not the boat's position. A rock offsets by its own p.xz
            // because that is a constant — it gives each rock its own look while the field stays
            // pinned to the world. Doing the same with a boat feeds a moving value into the noise,
            // so the field slides under itself every frame and the disturbance churns instead of
            // sitting in the water the boat is passing through.
            float local = WaveBandsGradientNoise(WorldPos.xz * max(_WaveBandBoatScale, 1e-4) + 17.3);

            s += local * profile * _WaveBandBoatDistort;
        }
    }

    // SUBTRACTING the accumulating phase is what drifts the lines along. The phase is accumulated
    // on the CPU (WaveMaterialController) rather than derived from Time * Speed, so changing the
    // speed mid-level slides them instead of teleporting them — the same reasoning behind
    // _WavePhase, and it wraps at 2pi, which is exactly one band cycle.
    float cyc     = s - _WaveBandPhase * (1.0 / WAVE_BANDS_TWO_PI);
    float centred = abs(frac(cyc) - 0.5) * 2.0;   // 0 at a line's centre, 1 at the cycle edge

    // The window. Held just under 1 so neighbouring lines always keep a seam between them rather
    // than merging into a flat wash. Softness slides the inner edge of the falloff in toward the
    // centre: at 0 the two edges nearly coincide and the line has a hard rim, at 1 it falls away
    // from the centre point. The epsilon keeps them apart, since smoothstep divides by the gap.
    float w     = clamp(_WaveBandWidth, 0.01, 0.98);
    float soft  = saturate(_WaveBandSoftness);
    float inner = w * (1.0 - soft);
    float outer = max(w, inner + 1e-4);
    float pulse = 1.0 - smoothstep(inner, outer, centred);   // 1 in the line, 0 outside it
    float bandMask = saturate(pulse);

    float bands = bandMask * _WaveBandStrength;

    // The grain, flipped through the line mask: (1 - 2 * bandMask) runs from +1 in open water to -1 on
    // a line, so one field speckles the gaps and eats into the bands at the same time. Lifted to
    // 0..1 rather than left signed, because a signed field would spend half its area pushing the
    // gaps below zero — where there is nothing to darken — and the grain would show up sparse and
    // lopsided instead of even.
    if (_WaveBandGrainStrength > 0.0001)
    {
        float grain = saturate(WaveBandsGradientNoise(WorldPos.xz / max(_WaveBandGrainSize, 0.0001)) + 0.5);
        bands += grain * _WaveBandGrainStrength * (1.0 - 2.0 * bandMask);
    }

    // Floored, not saturated. Flooring is what keeps the promise that this can only ever brighten:
    // a heavy grain can eat a line away to nothing but never past it into darkening the water
    // underneath. Clamping the TOP as well would quietly cap Strength at 1 and cost the overbright
    // headroom that feeds bloom, which is why max() is doing this rather than saturate().
    Bands = max(bands, 0.0);
}

// Shader Graph appends _float or _half to the function name depending on graph precision, and a
// File custom function has to supply whichever it asks for. Forwards to the float version — the
// globals are float and the maths is cheap.
void WaveBands_half(
    half3 WorldPos,
    out half Bands)
{
    float b;
    WaveBands_float(WorldPos, b);
    Bands = (half)b;
}
