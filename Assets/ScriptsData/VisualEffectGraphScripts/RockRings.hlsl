// Rings around rocks — concentric brightness bands travelling outward from every nearby rock.
//
// The water already paints one map-wide set of bands from _WaveCenter (WavePeaksTroughs.hlsl).
// This is a SECOND brightness source, one per rock, so a spike standing in the water leaves a
// mark of its own instead of sitting in it unnoticed. Brightness only — no displacement — so it
// rides the existing water fragment shader and costs no draw calls and no extra mesh density.
//
// ── Cost ─────────────────────────────────────────────────────────────────────
// One loop over the rocks the manager pushed, which is NOT the whole level: RockRingManager
// sorts by distance to the boat every frame and only pushes the nearest few inside its LOD
// radius. The loop breaks on the count (same shape as InstancedLights.hlsl), so it costs what
// is near the boat, not the ceiling. Pixels beyond a rock's reach `continue` before the maths.
//
// The LOD fade is worked out per ROCK on the CPU and arrives already baked into the array —
// there is no per-pixel boat distance here, because the answer is the same for every pixel of
// a given rock. It rides in the unused .y (world height) of the position, since the water is a
// horizontal plane and only .xz is ever read.
//
// ── Globals ──────────────────────────────────────────────────────────────────
// Bare $Globals, not blackboard properties. RockRingManager and WaveMaterialController re-push
// them every frame through BOTH Material.Set* and Shader.SetGlobal* — a shader reimport wipes
// them, and re-pushing is what makes that self-heal on the next frame. Names are deliberately
// distinct from anything the water graph declares, so this include can never collide with a
// property the graph already owns (which is why the boat centre is not read here at all).
// Guarded, because WaveBands.hlsl reads the same rocks rather than standing up a second manager,
// and both includes land in this shader. Only what is genuinely shared sits behind the guard —
// same reasoning as WHIRLPOOL_POSITIONS_DECLARED across the three whirlpool files.
#ifndef ROCK_RING_POSITIONS_DECLARED
#define ROCK_RING_POSITIONS_DECLARED
#define MAX_ROCK_RINGS 48

// .x/.z = world position   .y = LOD fade 0..1 (CPU, per rock)   .w = radius at the waterline
float4 _RockRingPositions[MAX_ROCK_RINGS];
float  _RockRingCount;
#endif

// The rock's own silhouette, so the ring belongs to it rather than being a circle drawn nearby:
// .x = lobe count (its carved flutes)   .y = lobe amplitude   .z = twist   .w = phase offset
// Unguarded — nothing else reads it.
float4 _RockRingShape[MAX_ROCK_RINGS];

float _RockRingPhase;         // CPU-accumulated, exactly like _WavePhase — see below
float _RockRingStrength;      // how far the bands push brightness either side of neutral
float _RockRingFrequency;     // bands between the rock's edge and the outer limit
float _RockRingSpread;        // outward reach, as a multiple of the rock's waterline radius
float _RockRingFalloffPower;  // how hard the bands fade out toward that limit
float _RockRingLobeStrength;  // 0 = plain circles; up = the ring takes the rock's cross-section

// A band is a sine, so half of every cycle pushes one way and half the other — a paired ring either
// side of neutral. Rectify flattens one of the two halves, leaving single rings on water that keeps
// its own brightness between them.
//
// SIGNED, because which half you want depends on the graph it feeds. +1 keeps the positive half,
// -1 keeps the negative half, 0 keeps the full wave. A chain where a larger value darkens wants -1
// to get bright rings; one where a larger value brightens wants +1. Making this a control rather
// than a fixed convention means the include never has to guess which way round the graph reads.
float _RockRingRectify;

// Distortion. The bands are pushed back and forth by a gradient noise field fixed in world space,
// so a ring wanders as it travels rather than staying a clean circle — and each rock's rings wander
// differently, because they are passing through different parts of the same field.
float _RockRingDistortStrength; // in band-space: 0.1 shifts a band by a tenth of the spread
float _RockRingDistortScale;    // world-space frequency of that field

// Band shape. A band is a WINDOW cut out of its cycle, not a sine — which is what makes width a
// control in its own right. Shaping a sine could only ever widen a crest by about half again, and
// it brightened the band as it did so, because lifting the shoulders of a curve and raising its
// average are the same act. A window separates the two: the top is flat at full height whatever
// the width, so widening changes only how much of the cycle is lit.
float _RockRingWidth;           // fraction of its cycle a band fills at the rock, before any growth
float _RockRingWidthMultiplier; // what that width has been multiplied by at the end of its life
float _RockRingSoftness;        // 0 = a hard-edged ring; 1 = falls away from the centre, sine-like

#define ROCK_RINGS_TWO_PI 6.28318530718

// The same quintic the rest of the water uses (WaterSurface.hlsl / WavesAndWhirlpools.hlsl), so
// these bands fade on the same curve as the whirlpool edges rather than a curve of their own.
float RockRingsSmootherStep(float h)
{
    return h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
}

// Gradient noise, matching Shader Graph's own Gradient Noise node so the field behaves the way it
// does when you preview one in the graph — same lattice, same gradients, same look at a given
// scale. Returns roughly -0.5..0.5, signed, which is what a push either way wants. (The node adds
// 0.5 to land in 0..1; nothing here needs that.)
float2 RockRingsNoiseDir(float2 p)
{
    p = fmod(p, 289.0);
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

float RockRingsGradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);

    float d00 = dot(RockRingsNoiseDir(ip),                     fp);
    float d01 = dot(RockRingsNoiseDir(ip + float2(0.0, 1.0)),  fp - float2(0.0, 1.0));
    float d10 = dot(RockRingsNoiseDir(ip + float2(1.0, 0.0)),  fp - float2(1.0, 0.0));
    float d11 = dot(RockRingsNoiseDir(ip + float2(1.0, 1.0)),  fp - float2(1.0, 1.0));

    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y);
}

// Rings              : band value, already scaled by _RockRingStrength. 0 where no rock reaches, so
//                      it stays neutral into an Add no matter which way rectify is set.
// Mask               : 0..1 envelope of the affected area — for anything else that wants to fade near rocks.
// BrightnessMultiply : 1 + Rings. Multiply this straight into the water's Brightness chain, the way
//                      WavePeaksTroughs folds in the soul-fish mask. Neutral (1) where nothing reaches.
void RockRings_float(
    float3 WorldPos,
    out float Rings,
    out float Mask,
    out float BrightnessMultiply)
{
    float rings = 0.0;
    float mask  = 0.0;

    int   count   = min((int)_RockRingCount, MAX_ROCK_RINGS);
    float spreadK = max(_RockRingSpread, 0.0001);
    bool  lobed   = _RockRingLobeStrength > 0.0001;
    float rectify = clamp(_RockRingRectify, -1.0, 1.0);
    // Below 1 would narrow the band instead, which is not what the control means; clamp it off.
    float widthMul  = max(_RockRingWidthMultiplier, 1.0);
    float widthBase = clamp(_RockRingWidth, 0.01, 1.0);
    float soft      = saturate(_RockRingSoftness);

    // Sampled ONCE, before the loop. The field is world space, so the answer does not depend on
    // which rock is being drawn — every rock reads the same value at this pixel, and the noise
    // costs the same whether one rock is near or twelve.
    float distort = _RockRingDistortStrength > 0.0001
                  ? RockRingsGradientNoise(WorldPos.xz * _RockRingDistortScale) * _RockRingDistortStrength
                  : 0.0;

    for (int i = 0; i < MAX_ROCK_RINGS; i++)
    {
        if (i >= count) break;

        float4 p = _RockRingPositions[i];
        float4 s = _RockRingShape[i];

        float baseRadius = max(p.w, 0.0001);
        float spread     = baseRadius * spreadK;

        float2 offset = WorldPos.xz - p.xz;
        float  d      = length(offset);

        // Conservative cull: past the widest the lobed edge can reach, plus the full spread,
        // nothing this rock does can show. Amplitude is <= 1, so this never clips a real band.
        if (d > baseRadius * (1.0 + _RockRingLobeStrength) + spread) continue;

        // The rock's cross-section, pushed outward: a six-fluted rock throws a six-lobed ring,
        // and its twist winds those lobes round as the bands travel. atan2 is the priciest op
        // here, so a level tuned back to plain circles skips it outright.
        float r = baseRadius;
        if (lobed)
        {
            float angle = atan2(offset.y, offset.x);
            r += baseRadius * _RockRingLobeStrength * s.y * sin(s.x * angle + s.z * d + s.w);
        }

        // 0 at the rock's edge, 1 at the outer limit. Inside the rock this saturates to 0, which
        // is hidden under the rock mesh anyway.
        float t = saturate((d - r) / spread);

        // Position within this band's cycle, counted in whole cycles rather than radians so the
        // window below can be measured as a plain fraction.
        //
        // SUBTRACTING the accumulating phase is what sends the bands outward. The phase is
        // accumulated on the CPU (WaveMaterialController) rather than derived from Time * Speed,
        // so changing the speed mid-level slides the bands instead of teleporting them — the same
        // reasoning behind _WavePhase.
        // The distortion is added to the band position only, not to t itself — the envelope stays
        // clean, so the rings wander inside an outer edge that keeps its shape.
        float cyc     = (t + distort) * _RockRingFrequency
                      - (_RockRingPhase + s.w) * (1.0 / ROCK_RINGS_TWO_PI);
        float centred = abs(frac(cyc) - 0.5) * 2.0;   // 0 at the band's centre, 1 at the cycle edge

        // The window. t is the band's life — 0 as it leaves the rock, 1 as it fades — so its width
        // runs from the authored width to that times the multiplier across its journey. Held just
        // under 1 so neighbouring bands always keep a seam between them rather than merging into a
        // flat wash.
        float w = min(widthBase * lerp(1.0, widthMul, t), 0.98);

        // Soft edges. Softness slides the inner edge of the falloff in toward the band's centre:
        // at 0 the two edges nearly coincide and the ring has a hard rim, at 1 it falls away from
        // the centre point and reads much like the sine this replaced. The epsilon keeps the two
        // edges apart, since smoothstep divides by their difference.
        float inner = w * (1.0 - soft);
        float outer = max(w, inner + 1e-4);
        float pulse = 1.0 - smoothstep(inner, outer, centred);   // 1 in the band, 0 outside it

        // A window is already single-sided, so rectify now chooses what NEUTRAL means rather than
        // clipping a half away. At 0 the band is centred on zero and swings both ways, giving the
        // paired light/dark rings; at +/-1 it sits wholly to one side of neutral, so the water
        // between rings is left alone. The sign picks which side, exactly as before.
        float band = lerp(pulse * 2.0 - 1.0, pulse * sign(rectify), abs(rectify));

        float env = pow(RockRingsSmootherStep(1.0 - t), max(_RockRingFalloffPower, 0.01)) * p.y;

        // Additive, so two rocks close together interfere — which is what water does.
        rings += band * env;
        mask   = max(mask, env);
    }

    Rings              = rings * _RockRingStrength;
    Mask               = saturate(mask);
    BrightnessMultiply = 1.0 + Rings;
}

// Shader Graph appends _float or _half to the function name depending on graph precision, and a
// File custom function has to supply whichever it asks for. Forwards to the float version — the
// globals are float and the maths is cheap.
void RockRings_half(
    half3 WorldPos,
    out half Rings,
    out half Mask,
    out half BrightnessMultiply)
{
    float r, m, b;
    RockRings_float(WorldPos, r, m, b);
    Rings              = (half)r;
    Mask               = (half)m;
    BrightnessMultiply = (half)b;
}
