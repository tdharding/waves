// Mixes the finished fog pixel: body, lip, light and grain into a colour and an alpha.
//
// Kept as one function rather than a raft of maths nodes so the graph stays readable, and so the
// two rules that matter are stated once in code instead of being implied by a node layout anyone
// could rewire by accident:
//
//   PROXIMITY drives the body. Real fog scatters — it brightens as a whole near a light rather
//   than having a lit side, so the radial term does most of the work.
//
//   LIGHT drives the lip only. The directional term picks out the top edges, which is what makes
//   lobes read as domed rather than as flat cut-outs.
//
// AMBIENT is the fix for fog rendering completely flat. This game has no sun: away from every lit
// street light both light terms are zero, so without a floor the body renders at exactly its base
// colour with the height field multiplied by nothing, and there is no volume to see anywhere a
// lamp is not. Set it to 0 for the old behaviour.
//
//   EDGE SHADOW darkens the base colour toward the outline, measured on Fill (0 at the edge, 1
//   deep inside). It goes on UNDER the light: the lit rim and the lamp glow lerp over it, so a
//   lit edge still reads bright and the shadow only shows where the light does not reach.

#ifndef FOG_COMPOSE_INCLUDED
#define FOG_COMPOSE_INCLUDED

void FogCompose_float(
    float  Body,
    float  Lip,
    float  Fill,
    float  Light,        // InstancedLights, N.L term
    float  Proximity,    // InstancedLights, radial term
    float  Grain,
    float  Thin,         // transparency from FogGrain
    float  Slope,        // how steeply the surface falls here, from FogNormal
    float3 FogColour,
    float3 LitColour,
    float  LipLight,
    float  Ambient,
    float  Opacity,
    float  EdgeShadowWidth,      // how far in from the edge the shadow reaches, as a fraction of Fill
    float  EdgeShadowStrength,   // 0 = no shadow, 1 = black at the very edge, >1 = black reaches further in
    out float3 Colour,
    out float  Alpha)
{
    float glow = saturate(Proximity);
    float rim  = saturate(Light) * Lip * LipLight;

    // Ambient is weighted by the surface slope rather than applied flat, so with no lamp anywhere
    // near, a mass still reads as domed: the steep rim catches it and the flat middle does not.
    // Applied uniformly it would just raise the brightness and stay as flat as before.
    float ambient = Ambient * (0.35 + 0.65 * saturate(Slope));

    float lit = saturate(glow + rim + ambient);

    float edge  = 1.0 - smoothstep(0.0, max(EdgeShadowWidth, 1e-3), saturate(Fill));
    float shade = 1.0 - saturate(max(EdgeShadowStrength, 0.0) * edge);

    Colour = lerp(FogColour * shade, LitColour, lit) * Grain;
    Alpha  = saturate(Body * Thin * Opacity);
}

void FogCompose_half(
    half Body, half Lip, half Fill, half Light, half Proximity, half Grain, half Thin, half Slope,
    half3 FogColour, half3 LitColour, half LipLight, half Ambient, half Opacity,
    half EdgeShadowWidth, half EdgeShadowStrength,
    out half3 Colour, out half Alpha)
{
    float3 c; float a;
    FogCompose_float(Body, Lip, Fill, Light, Proximity, Grain, Thin, Slope,
                     FogColour, LitColour, LipLight, Ambient, Opacity,
                     EdgeShadowWidth, EdgeShadowStrength, c, a);
    Colour = (half3)c; Alpha = (half)a;
}

#endif
