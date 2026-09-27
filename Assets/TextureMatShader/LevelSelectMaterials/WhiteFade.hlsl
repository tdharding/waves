// The level select's white fade: colour washed towards white with distance from the boat, true
// at the boat and faded beyond it. Two rings of it, one inside the other — White Fade 1 and
// White Fade 2 as they were chained in RiverRunShader, in the same order —
//
//   Outer  — whitened by Outer Opacity, clearing to the true colour inside Radius.
//   Inner  — the result whitened again by Inner Opacity, clearing inside Radius / Inner Division.
//
// Then the white gradient on top: white ADDED below Gradient Bottom, easing to nothing by world
// Y = 1 — as it was in RiverRunShader, where the top was a Color node's 1 wired into Edge2.
//
// Each ring is smoothstep(radius, 0, distance): 1 at the boat, 0 at its radius and beyond. The
// whitening is Shader Graph's Lighten blend against white — max(white, colour) — at the ring's
// opacity, so HDR colour above 1 is left alone exactly as it was.
//
// Every setting is a bare $Global — there is no property block behind any of them, so a graph
// only has to wire its colour through the node. Pushed every frame by WhiteFadeSettings, one set
// of numbers shared by the river runs, the landscape and the spikes, and tuned in the White Fade
// foldout of either the Run Shading Tuner or the Landscape Tuner.

#ifndef WHITE_FADE_INCLUDED
#define WHITE_FADE_INCLUDED

// Where the boat is. Its own name rather than _BoatWorldCenter: RiverRunShader declares that one
// as a graph property, and a second declaration here would not compile there. Pushed alongside
// it by LevelSelectBoatToShaders.
float4 _WhiteFadeCentre;

// Metres from the boat to where the outer ring is fully faded.
float  _WhiteFadeRadius;

// How far each ring pulls towards white, 0 to 1.
float  _WhiteFadeOuterOpacity;
float  _WhiteFadeInnerOpacity;

// The inner ring's radius is Radius divided by this.
float  _WhiteFadeInnerDivision;

// World Y the white gradient is full at, easing out by WHITE_FADE_GRADIENT_TOP.
float  _WhiteFadeGradientBottom;
#define WHITE_FADE_GRADIENT_TOP 1.0

float3 WhiteFadeRing(float3 colour, float distance, float radius, float opacity)
{
    float3 whitened = lerp(colour, max(float3(1.0, 1.0, 1.0), colour), opacity);
    float  clear    = smoothstep(max(radius, 1e-5), 0.0, distance);
    return lerp(whitened, colour, clear);
}

void WhiteFade_float(float3 In, float3 WorldPos, out float3 Out)
{
    float d = length(WorldPos - _WhiteFadeCentre.xyz);

    float3 c = WhiteFadeRing(In, d, _WhiteFadeRadius, _WhiteFadeOuterOpacity);
    c        = WhiteFadeRing(c,  d, _WhiteFadeRadius / max(_WhiteFadeInnerDivision, 1e-5),
                             _WhiteFadeInnerOpacity);

    Out = c + (1.0 - smoothstep(_WhiteFadeGradientBottom, WHITE_FADE_GRADIENT_TOP, WorldPos.y));
}

#endif
