// Grain and transparency — the two things that stop cheap 3D fog reading as rubber.
//
// The test runs came out looking like moulded plastic: too solid, too glossy, too even. Squash
// cures most of it in the preset, and these two cure the rest at the shader end. Thin limb tips
// go see-through so they melt into the water instead of ending as cut-out shapes, and grain
// breaks the smooth sheen the sphere shading otherwise gives.
//
// THE FOG IS A MASK OVER THE GRAIN, not the carrier of it. One gradient-noise field covers the
// whole fog plane in world space and drifts on the fog's own wind, and the fog shape decides
// where it shows. It used to be sampled around each mass's centre (blob space) so it travelled
// with that mass — which snapped to a hard seam wherever two masses overlapped, because the
// blended blob id had to pick one owner. The old version is in Archive/FogBlobGrain.
//
// Masses drift on the same wind at the same speed, so in aggregate the grain still moves with
// the fog; it only slips where a mass is pushed off the wind by an obstacle.

#ifndef FOG_GRAIN_INCLUDED
#define FOG_GRAIN_INCLUDED

// Bare $Globals, pushed every frame by FogFieldManager: xy = how far the wind has carried the
// grain, in world XZ. Accumulated on the CPU rather than Time * wind here, so a change of wind
// mid-level turns the drift instead of jumping the whole pattern.
float4 _FogGrainDrift;

// Unity's Gradient Noise node, written out so the fog stays one readable function. The mod 289
// keeps the hash precise far from the origin.
float2 FogGrain_Dir(float2 p)
{
    p = p - floor(p / 289.0) * 289.0;
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

// Signed, roughly -0.5..0.5 — the same swing the old value noise had around its midpoint, so
// Grain Amount means about what it did.
float FogGrain_Noise(float2 p)
{
    float2 ip = floor(p), fp = frac(p);
    float d00 = dot(FogGrain_Dir(ip),                  fp);
    float d01 = dot(FogGrain_Dir(ip + float2(0, 1)),   fp - float2(0, 1));
    float d10 = dot(FogGrain_Dir(ip + float2(1, 0)),   fp - float2(1, 0));
    float d11 = dot(FogGrain_Dir(ip + float2(1, 1)),   fp - float2(1, 1));
    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    return lerp(lerp(d00, d01, fp.y), lerp(d10, d11, fp.y), fp.x);
}

void FogGrain_float(
    float3 WorldPos,
    float  Fill,                  // how far inside the body, from FogShape
    float  GrainAmount,
    float  GrainScale,
    float  TransparencyFalloff,   // how hard thin fog thins out
    out float Grain,
    out float Alpha)
{
    // One octave: the grain is meant to be large-scale, and a second octave is the cost that
    // pays for fine tooth nobody asked for.
    float2 p = (WorldPos.xz - _FogGrainDrift.xy) * GrainScale;
    float n = FogGrain_Noise(p);

    // Floored at black. Past an amount of 1 the swing reaches below zero, and a negative
    // multiplier does not darken the fog — it inverts its colour, which reads as bright wrong-
    // coloured speckle rather than as heavier grain.
    Grain = max(1.0 + n * 2.0 * GrainAmount, 0.0);

    // How wide the see-through band along the edge is, NOT a curve over the whole body.
    //
    // This was pow(Fill, falloff), which spreads the fade across the entire mass — raising it made
    // the whole blob thinner rather than tightening its edge, which is backwards from what the
    // dial is for. As a width, the body reaches full opacity immediately and only the outer
    // fraction softens, so small values hug the outline and large ones bleed inward.
    Alpha = smoothstep(0.0, max(TransparencyFalloff, 1e-3), saturate(Fill));
}

void FogGrain_half(
    half3 WorldPos, half Fill,
    half GrainAmount, half GrainScale, half TransparencyFalloff,
    out half Grain, out half Alpha)
{
    float g, a;
    FogGrain_float(WorldPos, Fill, GrainAmount, GrainScale, TransparencyFalloff, g, a);
    Grain = (half)g; Alpha = (half)a;
}

#endif
