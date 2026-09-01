// A short band of black sitting where an object meets the water, fading out as it climbs.
//
// Custom Function node name: WaterlineBlackGradient
//
// The point of it is contact. A spike, a wall or a block dropped into the water reads as pasted on
// top of it — nothing tells you the two are touching. A little darkness gathered at the waterline
// and gone within a few inches puts the object IN the water rather than on it, and because the
// darkness is measured from the water rather than from the mesh, every object gets the same band
// at the same height whatever shape it is and however it was built.
//
// ── Works on anything ────────────────────────────────────────────────────────
// The only input is world position, so there are no UVs to author, no vertex colours to bake and
// no per-mesh setup. The procedural spikes, the cube blocks and the spline walls all take it the
// same way, and so would anything else you drop in the water later.
//
// ── It follows the real waterline, not a flat line ───────────────────────────
// The water surface rises and falls in rings from _WaveCenter, so the height it meets an object at
// depends on where that object stands and what the wave is doing this frame. This reconstructs
// that surface exactly the way WaterSurfaceHeight.hlsl does — same globals, same stepped distance,
// same sine, same _WavePhase — so the band stays glued to the water as it moves instead of drifting
// off it. A flat base line would be cheaper by one sine and would visibly separate from the water
// the moment the ripple depth came up.
//
// Below the waterline the gradient HOLDS at full rather than continuing to grow or falling away.
// The submerged part of an object is already the depth fade's business; all this has to do is not
// draw a seam at the line where the two meet.
//
// ── Globals ──────────────────────────────────────────────────────────────────
// Bare $Globals, exactly like RockRings.hlsl and WaveBands.hlsl: one height, one strength and one
// curve shared by every material using this, so raising the height in the tuner raises it on the
// spikes, the blocks and the walls together. WaveMaterialController re-pushes all three every frame
// through BOTH Material.Set* and Shader.SetGlobal* — a shader reimport wipes them, and re-pushing
// is what makes that self-heal on the next frame.

// Declared with the same guard names WaterSurfaceHeight.hlsl uses, so a shader that includes both
// (a rock doing an underwater invert AND this band, say) compiles with one copy of each.
#ifndef WATER_SURFACE_GLOBALS_DECLARED
#define WATER_SURFACE_GLOBALS_DECLARED
float4 _WaterSurfaceOrigin;   // xyz = world origin; y = flat base surface height
float  _WaterSurfaceScale;    // water plane localScale.x
float  _WaterSurfaceFreq;     // _Frequency
float  _WaterSurfaceRipple;   // _RippleDepth
float  _WaterSurfaceStepRate; // _WaveStepRate
#endif

#ifndef WAVE_PHASE_DECLARED
#define WAVE_PHASE_DECLARED
float _WavePhase;             // shared accumulated phase (WaveMaterialController / tuner)
#endif

float _WaterlineGradientHeight;    // world units above the water the black fades out over
float _WaterlineGradientStrength;  // how black it is at the line; 0 = the effect is gone
float _WaterlineGradientFalloff;   // 1 = an even ramp; higher gathers it at the line; lower spreads it

// Gradient : 0 = the object is left alone, 1 = full black. Multiply it into a Lerp toward black,
//            or feed it straight to an alpha.
// Shade    : the same thing the other way up (1 - Gradient), ready to multiply straight into a
//            Base Color. Both are handed out so neither wiring needs a One Minus in the graph.
void WaterlineBlackGradient_float(
    float3 WorldPos,
    out float Gradient,
    out float Shade)
{
    Gradient = 0.0;
    Shade    = 1.0;
    if (_WaterlineGradientStrength <= 0.0) return;

    // The water surface at this pixel's XZ. Mirrors WaterSurfaceHeight.hlsl / WaveUtils.SampleWave
    // exactly — stepped distance, same sine, driven by the shared phase — minus whirlpools, which
    // do not move the surface this band cares about.
    float  safeScale = max(_WaterSurfaceScale, 1e-4);
    float2 toOrigin  = (WorldPos.xz - _WaterSurfaceOrigin.xz) / safeScale;
    float  dist      = length(toOrigin);
    float  stepped   = floor(dist * _WaterSurfaceStepRate) / max(_WaterSurfaceStepRate, 1e-4);
    float  wave      = sin(stepped * _WaterSurfaceFreq - _WavePhase) * _WaterSurfaceRipple * safeScale;
    float  surfaceY  = _WaterSurfaceOrigin.y - wave;

    // 0 at the waterline, 1 at the top of the band. saturate is what holds the black at full below
    // the line: a submerged pixel gives a negative height and clamps to 0, which is the same value
    // the waterline itself has, so there is no step where they meet.
    float h = saturate((WorldPos.y - surfaceY) / max(_WaterlineGradientHeight, 1e-4));

    // Flipped so it is 1 at the line and 0 at the top, then shaped. The power is applied to the
    // ramp rather than to the height so raising Falloff pulls the black in tight against the water
    // without also moving where it finally reaches zero — the height stays the height.
    float ramp = pow(1.0 - h, max(_WaterlineGradientFalloff, 1e-4));

    Gradient = saturate(ramp * _WaterlineGradientStrength);
    Shade    = 1.0 - Gradient;
}

// Shader Graph appends _float or _half depending on graph precision, and a File custom function has
// to supply whichever it asks for. Forwards to the float version — the globals are float and the
// maths is one sine and one pow.
void WaterlineBlackGradient_half(
    half3 WorldPos,
    out half Gradient,
    out half Shade)
{
    float g, s;
    WaterlineBlackGradient_float(WorldPos, g, s);
    Gradient = (half)g;
    Shade    = (half)s;
}
