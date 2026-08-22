// WaterSurface.hlsl
// ─────────────────────────────────────────────────────────────────────────────
// THE water surface. Single source of truth.
//
// Before this file existed the surface formula was copy-pasted into 8 places
// (vertex displacement, peak/trough brightness, twirl slope, wall waterlines,
// two sonar shaders, and twice in WaveUtils.cs on the CPU). They drifted. This
// include exists so there is exactly one place that answers "where is the water".
//
// Nothing here is a Custom Function entry point — this is a plain include.
// Node entry points live in WaterSurfaceNode.hlsl / WaterNormalNode.hlsl.
//
// ── Layers ───────────────────────────────────────────────────────────────────
//   Layer 0  General water   — a stack of straight wave trains crossing the map.
//   Layer 1  Localized       — radial ring pools, whirlpools. Added on top.
//
// ── Coordinate space ─────────────────────────────────────────────────────────
// Everything works in WAVE-PLANE OBJECT SPACE. The water mesh is a plane rotated
// -90° on X, so:
//     object XY = the horizontal plane        object +Z = world up
// World-space callers (walls, sonar) go through WorldToWavePlane() first.
//
// ── Sign convention ──────────────────────────────────────────────────────────
// Legacy code wrote `PositionOut.z -= wave`. Since object +Z is world up, the
// world-up offset is -wave. Height carries that already-negated value, so:
//     vertex:  PositionOut.z += Height
//     world:   surfaceY = originY + Height * planeScale
// This matches WaveUtils.SampleWave(), which has always returned -sin(...).
// ─────────────────────────────────────────────────────────────────────────────

#ifndef WATER_SURFACE_INCLUDED
#define WATER_SURFACE_INCLUDED

#define WATER_MAX_WAVE_TRAINS   6
#define WATER_MAX_RADIAL_RINGS  4
#define WATER_MAX_WHIRLPOOLS    8

// ─────────────────────────────────────────────────────────────────────────────
// Globals
// ─────────────────────────────────────────────────────────────────────────────

// Layer 0 — general water. Two float4 per train, packed by WaveMaterialController.
//   [i*2 + 0] = (dirX, dirY, frequency, amplitude)
//   [i*2 + 1] = (accumulatedPhase, steepness, stepRate, unused)
// Phase is CPU-accumulated per train, exactly like the old single _WavePhase, so
// that changing Speed or Frequency does not teleport the wave.
#ifndef WATER_WAVE_TRAINS_DECLARED
#define WATER_WAVE_TRAINS_DECLARED
uniform float4 _WaveTrains[WATER_MAX_WAVE_TRAINS * 2];
float  _WaveTrainCount;
#endif

// Layer 1 — radial ring pools. This is where the original concentric wave went.
//   [i*2 + 0] = (centreX, centreY, frequency, amplitude)
//   [i*2 + 1] = (accumulatedPhase, radius, falloffPower, stepRate)
// radius = 0 means INFINITE — no falloff, map-wide. That convention is what lets
// every legacy preset migrate to a single ring and stay pixel-identical.
#ifndef WATER_RADIAL_RINGS_DECLARED
#define WATER_RADIAL_RINGS_DECLARED
uniform float4 _RadialRings[WATER_MAX_RADIAL_RINGS * 2];
float  _RadialRingCount;
#endif

// Layer 1 — whirlpools. xyz = wave-plane position, w = radius. Pushed by
// WhirlpoolManager.PushWavePlane() as a MaterialPropertyBlock.
#ifndef WHIRLPOOL_POSITIONS_DECLARED
#define WHIRLPOOL_POSITIONS_DECLARED
uniform float4 _WhirlpoolPositions[WATER_MAX_WHIRLPOOLS];
#endif

// Published by WaveMaterialController.PublishSurfaceGlobals(). Only needed by
// world-space callers.
#ifndef WATER_SURFACE_GLOBALS_DECLARED
#define WATER_SURFACE_GLOBALS_DECLARED
float4 _WaterSurfaceOrigin;   // xyz = world origin; y = flat base surface height
float  _WaterSurfaceScale;    // water plane localScale.x
float  _WaterSurfaceFreq;     // legacy, kept for back-compat
float  _WaterSurfaceRipple;   // legacy, kept for back-compat
float  _WaterSurfaceStepRate; // legacy, kept for back-compat
#endif

// Legacy single phase. Still published for anything not yet migrated.
#ifndef WAVE_PHASE_DECLARED
#define WAVE_PHASE_DECLARED
float _WavePhase;
#endif

// ─────────────────────────────────────────────────────────────────────────────
// Sample result
// ─────────────────────────────────────────────────────────────────────────────

struct WaterSample
{
    // Full displacement to add to an object-space vertex.
    //   xy = sideways drift (Gerstner steepness + whirlpool swirl)
    //   z  = vertical (Height + WhirlpoolDepth)
    float3 Displacement;

    // True analytic surface gradient (dh/dx, dh/dy) in wave-plane space, from the
    // WAVES ONLY. Feeds real vertex normals. No finite differences, so no aliasing.
    float2 Gradient;

    // World-up offset from the waves alone (general + rings). Excludes whirlpools,
    // matching what WaterSurfaceHeight and WaveUtils.SampleWave have always meant.
    float  Height;

    // Normalized -1..1 wave signal driving peak/trough brightness.
    //
    // NOTE ON SIGN: this deliberately carries the LEGACY convention, which is
    // inverted relative to geometry. +1 is where sin(arg) peaks, but the surface
    // is pushed DOWN there. So "PeakBrightness" has always shaded the geometric
    // troughs. Preserved on purpose — flipping it would change the look of every
    // existing preset. Crest == -Height / totalAmplitude.
    float  Crest;

    // Normalized 0..1 steepness. 0 = flat crest or trough, 1 = steepest incline.
    // Matches the old abs(cos(arg)) exactly for a single wave, so TwirlScale and
    // TwirlSlopeBoost keep their tuned meaning.
    float  Slope;

    // World-up offset from whirlpool depressions alone. Negative = pulled down.
    float  WhirlpoolDepth;
};

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

// Quintic smootherstep on an already-normalized 0..1 edge value.
float WaterSmootherStep(float h)
{
    return h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
}

// The one whirlpool falloff. Every consumer routes through this, so the
// depression, the trough mask and the darkness ring finally share an edge.
// `taper` is the exponent applied to the squared smootherstep.
float WaterWhirlpoolFalloff(float dist, float radius, float taper)
{
    float h  = 1.0 - saturate(dist / max(radius, 0.0001));
    float ss = WaterSmootherStep(h);
    return pow(ss * ss, max(taper, 0.01));
}

// Optional band quantisation. stepRate = 0 means OFF (smooth), which is cleaner
// than the old "set it very high until it goes inert".
float WaterQuantise(float v, float stepRate)
{
    return stepRate > 0.0001 ? floor(v * stepRate) / stepRate : v;
}

// World position -> wave-plane XY, reusing the globals the controller already
// publishes. Ends the old object-XY vs world-XZ split.
float2 WorldToWavePlane(float3 worldPos)
{
    float safeScale = max(_WaterSurfaceScale, 1e-4);
    return (worldPos.xz - _WaterSurfaceOrigin.xz) / safeScale;
}

// ─────────────────────────────────────────────────────────────────────────────
// Layer 0 + Layer 1 (waves only — no whirlpools)
// ─────────────────────────────────────────────────────────────────────────────

WaterSample SampleWaterWaves(float2 planePos)
{
    WaterSample s;
    s.Displacement    = float3(0.0, 0.0, 0.0);
    s.Gradient        = float2(0.0, 0.0);
    s.Height          = 0.0;
    s.Crest           = 0.0;
    s.Slope           = 0.0;
    s.WhirlpoolDepth  = 0.0;

    float wave       = 0.0;  // legacy sign: the value that used to be subtracted
    float2 sideways  = float2(0.0, 0.0);
    float2 gradient  = float2(0.0, 0.0);
    float  ampSum    = 0.0;  // normaliser for Crest
    float  slopeSum  = 0.0;  // normaliser for Slope

    // ── Layer 0: general water ───────────────────────────────────────────────
    int trainCount = min((int)_WaveTrainCount, WATER_MAX_WAVE_TRAINS);
    for (int t = 0; t < trainCount; t++)
    {
        float4 a = _WaveTrains[t * 2 + 0];
        float4 b = _WaveTrains[t * 2 + 1];

        float2 dir       = normalize(a.xy + float2(1e-6, 0.0)); // guard zero dir
        float  freq      = a.z;
        float  amp       = a.w;
        float  phase     = b.x;
        float  steepness = b.y;
        float  stepRate  = b.z;

        float proj = WaterQuantise(dot(dir, planePos), stepRate);
        float arg  = proj * freq - phase;
        float sn   = sin(arg);
        float cs   = cos(arg);

        wave     += amp * sn;
        gradient -= amp * freq * cs * dir;   // d(-amp*sin(arg))/dp

        // Gerstner sideways drift. Points slide toward the crest, so crests
        // sharpen into ridges and troughs spread out flat. steepness 0 = plain
        // sine. The 1/(freq*amp*count) normalisation keeps steepness = 1 at the
        // edge of self-intersection rather than past it.
        if (steepness > 0.0001)
        {
            float Q = steepness / max(freq * amp * trainCount, 1e-4);
            sideways -= dir * (Q * amp * cs);
        }

        ampSum   += amp;
        slopeSum += amp * freq;
    }

    // ── Layer 1: radial ring pools ───────────────────────────────────────────
    int ringCount = min((int)_RadialRingCount, WATER_MAX_RADIAL_RINGS);
    for (int r = 0; r < ringCount; r++)
    {
        float4 a = _RadialRings[r * 2 + 0];
        float4 b = _RadialRings[r * 2 + 1];

        float2 centre       = a.xy;
        float  freq         = a.z;
        float  amp          = a.w;
        float  phase        = b.x;
        float  radius       = b.y;   // 0 = infinite
        float  falloffPower = b.z;
        float  stepRate     = b.w;

        float2 toCentre = planePos - centre;
        float  dist     = length(toCentre);
        float2 dir      = dist > 1e-5 ? toCentre / dist : float2(0.0, 0.0);

        // radius 0 = no falloff, map-wide. This is the legacy behaviour and what
        // makes migrated presets pixel-identical.
        float falloff = 1.0;
        if (radius > 0.0001)
        {
            float h = 1.0 - saturate(dist / radius);
            falloff = pow(WaterSmootherStep(h), max(falloffPower, 0.01));
        }

        float proj = WaterQuantise(dist, stepRate);
        float arg  = proj * freq - phase;

        float effAmp = amp * falloff;
        wave     += effAmp * sin(arg);
        gradient -= effAmp * freq * cos(arg) * dir;

        ampSum   += effAmp;
        slopeSum += effAmp * freq;
    }

    s.Height       = -wave;                                   // legacy negation
    s.Gradient     = gradient;
    s.Crest        = wave / max(ampSum, 1e-4);                // legacy sign, -1..1
    s.Slope        = saturate(length(gradient) / max(slopeSum, 1e-4));
    s.Displacement = float3(sideways, s.Height);

    return s;
}

// ─────────────────────────────────────────────────────────────────────────────
// Layer 1: whirlpools
// ─────────────────────────────────────────────────────────────────────────────

// Adds the depression and the swirl to an existing sample. Kept separate from
// SampleWaterWaves because the waterline masks, the peak/trough brightness and
// the CPU boat sampler all deliberately exclude whirlpools.
void ApplyWhirlpools(
    float2 planePos,
    float  whirlpoolCount,
    float  whirlpoolDepth,
    float  whirlpoolSwirl,
    float  whirlpoolTaper,
    inout  WaterSample s)
{
    int count = min((int)whirlpoolCount, WATER_MAX_WHIRLPOOLS);
    for (int i = 0; i < count; i++)
    {
        float2 offset = planePos - _WhirlpoolPositions[i].xy;
        float  radius = _WhirlpoolPositions[i].w;
        float  dist   = length(offset);

        float falloff = WaterWhirlpoolFalloff(dist, radius, whirlpoolTaper);

        s.WhirlpoolDepth -= falloff * whirlpoolDepth;

        // Swirl rotates the vertex around the drain — the mesh itself twists.
        float  angle = whirlpoolSwirl * falloff;
        float  cosA  = cos(angle);
        float  sinA  = sin(angle);
        float2 rot   = float2(offset.x * cosA - offset.y * sinA,
                              offset.x * sinA + offset.y * cosA);
        s.Displacement.xy += rot - offset;
    }

    s.Displacement.z = s.Height + s.WhirlpoolDepth;
}

// Strength mask of the whirlpool field, 0..1. Uses the same falloff as the
// depression, so the mask edge and the geometry edge now agree.
float WhirlpoolFieldMask(float2 planePos, float whirlpoolCount, float taper)
{
    float mask = 0.0;
    int count = min((int)whirlpoolCount, WATER_MAX_WHIRLPOOLS);
    for (int i = 0; i < count; i++)
    {
        float2 offset = planePos - _WhirlpoolPositions[i].xy;
        float  dist   = length(offset);
        mask = max(mask, WaterWhirlpoolFalloff(dist, _WhirlpoolPositions[i].w, taper));
    }
    return mask;
}

// Darkening area. Deliberately keeps its own radius multiplier and falloff power
// — a dark pool wider than the geometry is authored intent, not drift. It shares
// the falloff helper, so the curve shape still matches.
// FalloffPower is halved because the helper squares the smootherstep internally,
// preserving the previously authored pow(ss, Power) curve exactly.
float WhirlpoolFieldDarkness(
    float2 planePos, float whirlpoolCount,
    float  radiusMult, float darkStrength, float falloffPower)
{
    float dark = 0.0;
    int count = min((int)whirlpoolCount, WATER_MAX_WHIRLPOOLS);
    for (int i = 0; i < count; i++)
    {
        float2 offset = planePos - _WhirlpoolPositions[i].xy;
        float  radius = _WhirlpoolPositions[i].w * max(radiusMult, 0.001);
        float  dist   = length(offset);

        float falloff = WaterWhirlpoolFalloff(dist, radius, falloffPower * 0.5);
        dark = max(dark, falloff * darkStrength);
    }
    return dark;
}

// ─────────────────────────────────────────────────────────────────────────────
// Convenience: the full surface in one call
// ─────────────────────────────────────────────────────────────────────────────

WaterSample SampleWater(
    float2 planePos,
    float  whirlpoolCount,
    float  whirlpoolDepth,
    float  whirlpoolSwirl,
    float  whirlpoolTaper)
{
    WaterSample s = SampleWaterWaves(planePos);
    ApplyWhirlpools(planePos, whirlpoolCount, whirlpoolDepth, whirlpoolSwirl, whirlpoolTaper, s);
    return s;
}

#endif // WATER_SURFACE_INCLUDED
