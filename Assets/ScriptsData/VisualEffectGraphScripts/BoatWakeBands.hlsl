// Bands trailing the boat — a family of white lines that wrap the hull, cap round the bow, and
// trail away behind. The companion to WaveBands.hlsl: same window construction, same noise, so the
// two read as the same body of water rather than two effects sharing a screen.
//
// Brightness only — no displacement. The hull clearance pocket is BoatWakeDisplacement.hlsl and is
// a separate, unrelated thing; nothing here moves a vertex.
//
// ── White by construction ─────────────────────────────────────────────────────
// The output is a WINDOW, already 0..1, so it can only ever brighten whatever it feeds. Strength
// at 0 returns the water exactly to what it was.
//
// ── Globals ──────────────────────────────────────────────────────────────────
// Bare $Globals, like WaveBands and the rock rings. BoatWakeBandsController re-pushes them every
// frame through BOTH Material.Set* and Shader.SetGlobal*, so a shader reimport that wipes them
// self-heals on the next frame.

#ifndef BOAT_WAKE_BANDS_INCLUDED
#define BOAT_WAKE_BANDS_INCLUDED

// Own names, not _BoatPosition/_BoatForward — BoatProximityMask.hlsl already declares
// _BoatPosition, and both includes can land in the same shader.
float4 _BoatBandOrigin;    // .xz = boat world position
float4 _BoatBandForward;   // .xz = boat forward, normalised

// ── MASK SHAPE ───────────────────────────────────────────────────────────────
// Where the wake is. The bands are drawn from distance to a REGION, not to a point: the
// half-infinite box with a flat front edge at CapOffset, CapWidth across, running back under the
// boat. Ahead of that edge the distance is to the front, alongside it to the sides, at the corners
// to the corner — so the family wraps the hull, rounds the corners, and trails back as two arms
// without ever converging on a point.
//
// CapOffset slides the front edge along the boat's forward axis; CapWidth is how wide the flat
// front is. Wide reads as a shallow curve hugging the hull, narrow tightens toward a semicircle,
// and 0 is a single point. They place and shape the cap independently of each other.
float _BoatBandCapOffset;
float _BoatBandCapWidth;

// Beam holds the innermost line clear of the hull. Measured against a region rather than a point,
// this is a uniform collar and cannot split the wake down the middle.
float _BoatBandBeam;

// Wake Width is the span the family fills, measured outward from the beam; Flare widens it for
// every unit travelled back; Length is how far behind the boat it reaches.
float _BoatBandWakeWidth;
float _BoatBandFlare;
float _BoatBandLength;

// NoseFade is how far AHEAD of the cap the family persists before fading out — it must be positive
// or the fade range collapses and the cap is cut off entirely. TailFade is where along the length
// the family starts dying, as a fraction of it.
float _BoatBandNoseFade;
float _BoatBandTailFade;

// ── WAVE PATTERN ─────────────────────────────────────────────────────────────
// What is drawn inside that shape.
float _BoatBandStrength;   // peak whiteness of a line; 0 = the effect is gone
float _BoatBandCount;      // how many lines fill the family

// The window: flat-topped, so widening a line changes how much of the cycle is lit without also
// brightening it. Same construction as WaveBands.
float _BoatBandLineWidth;  // fraction of its cycle a line fills
float _BoatBandSoftness;   // 0 = a hard-edged line; 1 = falls away from its centre

// One distortion, two numbers. Strength is how far a line is pushed, in band-widths; Scale is how
// fine the field is. Internally this is two octaves of the same noise — a single octave always
// reads as a smooth curve however hard it is pushed, and the finer one is what makes a line read
// as water — but that is an implementation detail rather than another pair of dials.
//
// Sampled in the WAKE's own space, across it and along it, so it turns with the wake and belongs
// to it. Scale wants to be several times finer than the wake is long, or the whole wake sits inside
// one noise cell and the distortion becomes a constant sideways shift instead of a wobble.
float _BoatBandDistortStrength;
float _BoatBandDistortScale;

// Signed path curvature in radians per world unit, pushed by BoatWakeBandsController and already
// scaled by its Turn Strength. Applied as a progressive twist below.
float _BoatBandTurn;

// CPU-accumulated distance scrolled along the wake. Accumulating on the CPU means changing the
// speed mid-level slides the pattern instead of teleporting it.
float _BoatBandPhase;

// Deliberately NOT sharing WaveBands' helpers: both files can end up in one shader, and duplicate
// definitions are a compile error. Same maths, different names.
float BoatBandsSmootherStep(float h)
{
    return h * h * h * (h * (h * 6.0 - 15.0) + 10.0);
}

float2 BoatBandsNoiseDir(float2 p)
{
    p = fmod(p, 289.0);
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

float BoatBandsGradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);

    float d00 = dot(BoatBandsNoiseDir(ip),                     fp);
    float d01 = dot(BoatBandsNoiseDir(ip + float2(0.0, 1.0)),  fp - float2(0.0, 1.0));
    float d10 = dot(BoatBandsNoiseDir(ip + float2(1.0, 0.0)),  fp - float2(1.0, 0.0));
    float d11 = dot(BoatBandsNoiseDir(ip + float2(1.0, 1.0)),  fp - float2(1.0, 1.0));

    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y);
}

// Bands : 0..1, already scaled by _BoatBandStrength. 0 everywhere the wake does not fall, so it
//         stays neutral into an Add.
void BoatWakeBands_float(
    float3 WorldPos,
    out float Bands)
{
    Bands = 0.0;
    if (_BoatBandStrength <= 0.0) return;

    // Boat-local frame. Vertical wave displacement leaves world XZ untouched, so working here pins
    // the bands to the surface however it heaves.
    float2 fwd = _BoatBandForward.xz;
    float  fl  = length(fwd);
    if (fl < 1e-5) return;
    fwd /= fl;
    float2 rgt = float2(fwd.y, -fwd.x);

    float2 off = WorldPos.xz - _BoatBandOrigin.xz;

    // ── The turn ─────────────────────────────────────────────────────────────
    // Twist the sample point about the boat by an angle that grows with how far behind it is, so
    // the wake follows the arc the boat actually took. Everything below — envelope, cap, bands,
    // fades — is derived from lon/lat, so bending the coordinate bends the whole mask and the
    // pattern inside it together; nothing downstream needs to know this happened.
    //
    // A progressive ROTATION rather than a sideways offset: an offset leaves the lines square to
    // the old heading, so a hard turn reads as a sheared wake rather than a turned one. The angle
    // uses the UN-twisted distance behind, which keeps it a stable first-order approximation
    // instead of a coordinate that depends on itself.
    if (abs(_BoatBandTurn) > 1e-6)
    {
        float back0 = -dot(off, fwd);
        float ang   = _BoatBandTurn * max(back0, 0.0);
        float sa    = sin(ang);
        float ca    = cos(ang);
        off = float2(off.x * ca - off.y * sa,
                     off.x * sa + off.y * ca);
    }

    float  lon = dot(off, fwd);    // + ahead of the boat, - behind it
    float  lat = dot(off, rgt);
    float  back = -lon;            // + behind the boat

    float length01 = back / max(_BoatBandLength, 1e-4);
    if (length01 > 1.0) return;    // past the end of the wake entirely

    // Distance to the envelope — the exterior part of a box distance field.
    float halfW = max(_BoatBandCapWidth, 0.0) * 0.5;
    float dx    = abs(lat) - halfW;
    float dy    = lon - _BoatBandCapOffset;
    float ahead = max(dy, 0.0);
    float d     = length(float2(max(dx, 0.0), ahead));

    // The family fills from the beam outward, widening as it goes back.
    float width = max(_BoatBandWakeWidth + max(back, 0.0) * _BoatBandFlare, 1e-4);

    float u = (d - max(_BoatBandBeam, 0.0)) / width;
    if (u < 0.0) return;    // inside the envelope, where the boat itself is

    float cyc = u * max(_BoatBandCount, 1.0);

    // ── Not mirrored ─────────────────────────────────────────────────────────
    // The distance field is symmetric by nature, so drawn straight it gives two sides that are the
    // same line reflected — which reads as a kaleidoscope rather than water. Running the two sides
    // half a cycle out of step means a line on one side never has a partner opposite it, and the
    // world-space distortion below then pulls them further apart.
    cyc += (lat < 0.0) ? 0.5 : 0.0;

    // Distortion: two octaves of one field, driven by one strength and one scale. The second is
    // finer and weaker, and is what stops a line reading as a smooth curve.
    //
    // Sampled in the WAKE's own space — across it and along it — not in world space. Sampled in
    // the world, the field is nailed to the map: turning sweeps the wake across a stationary
    // pattern and the distortion visibly crawls over it, so it reads as something the wake is
    // passing through rather than something the wake is made of. In wake space it turns with the
    // wake and belongs to it.
    //
    // It does not freeze in place as a result: a fixed patch of water drifts backward through the
    // wake as the boat advances, so its `back` grows and the pattern it sits in evolves — the
    // distortion flows away down the wake, which is what it should do.
    //
    // Still asymmetric, because lat is SIGNED here: port and starboard land on different parts of
    // the field rather than mirroring, which is the job world space used to be doing.
    if (abs(_BoatBandDistortStrength) > 0.0001)
    {
        float  sc = max(_BoatBandDistortScale, 1e-4);

        // The phase scrolls the field ALONG the wake rather than moving the bands outward. A
        // feature sampled at (back - phase) appears further back as the phase grows, so the
        // distortion travels from the cap toward the tail. The band geometry is untouched, which
        // is why the look is unchanged and only the movement differs.
        float2 wp = float2(lat, back - _BoatBandPhase);
        float  n  = BoatBandsGradientNoise(wp * sc)
                  + BoatBandsGradientNoise(wp * sc * 3.0 + 31.4) * 0.45;
        cyc += n * _BoatBandDistortStrength;
    }

    // Past the outermost line there is nothing. Tested after the wobble so a displaced line is not
    // clipped off mid-swing.
    if (u > 1.0) return;

    float centred = abs(frac(cyc) - 0.5) * 2.0;   // 0 at a line's centre, 1 at the cycle edge

    // The window — identical construction to WaveBands so both families have the same edge.
    float w     = clamp(_BoatBandLineWidth, 0.01, 0.98);
    float soft  = saturate(_BoatBandSoftness);
    float inner = w * (1.0 - soft);
    float outer = max(w, inner + 1e-4);
    float pulse = 1.0 - smoothstep(inner, outer, centred);

    // Fades: out ahead of the cap, away down the length, and off the outer edge of the family so
    // the last line does not stop on a rim.
    float nose = 1.0 - smoothstep(0.0, max(_BoatBandNoseFade, 1e-4), ahead);

    float tailStart = saturate(_BoatBandTailFade);
    float tail      = BoatBandsSmootherStep(saturate(1.0 - smoothstep(tailStart, 1.0, saturate(length01))));

    float edge = 1.0 - smoothstep(0.75, 1.0, u);

    Bands = saturate(pulse) * nose * tail * edge * _BoatBandStrength;
}

// Shader Graph appends _float or _half depending on graph precision, and a File custom function has
// to supply whichever it asks for. Forwards, like WaveBands_half does.
void BoatWakeBands_half(
    half3 WorldPos,
    out half Bands)
{
    float b;
    BoatWakeBands_float(WorldPos, b);
    Bands = (half)b;
}

#endif
