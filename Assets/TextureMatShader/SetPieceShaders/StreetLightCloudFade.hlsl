#ifndef STREETLIGHT_CLOUD_FADE_INCLUDED
#define STREETLIGHT_CLOUD_FADE_INCLUDED

// Everything a street light's particle cloud needs from its material: the quads sit inside the
// lamp's shaft of light, fade out at its walls and towards its far end, keep the particle system's
// own colour and lifetime ramps, and come out dithered so they can be drawn with alpha clipping
// rather than blended transparency.
//
// StreetLightParticles reads the shaft off the StreetLightCone dropped into it and pushes it here
// through a MaterialPropertyBlock, so every lamp shares one material and still fades around its own
// cone. Both the emitter and this fade take their numbers from that one component, so the quads
// cannot drift out of the shape you can see in the scene.
//
// With no cone dropped in, ConeHeight arrives as 0 and this falls back to a plain radial fade
// around the apex, ConeBaseRadius standing in as the cloud's radius.
//
//   Colour   - the particle's tint. Straight into Base Color.
//   Alpha    - the dithered value. Into the Alpha block, with Alpha Clip Threshold at 0: the
//              threshold is already subtracted here, so anything below zero is meant to clip.
//   Fade     - the same falloff undithered, for a transparent material instead.
void StreetLightCloudFade_float(
    float3 WorldPosition,
    float3 ConeApex,
    float3 ConeAxis,
    float  ConeHeight,
    float  ConeBaseRadius,
    float  ConeEdgeSoftness,
    float  FadeStart,
    float4 VertexColour,
    float4 ScreenPosition,
    out float3 Colour,
    out float  Alpha,
    out float  Fade)
{
    float3 fromApex = WorldPosition - ConeApex;

    if (ConeHeight > 1e-4)
    {
        float3 axis = normalize(ConeAxis);

        // Split the offset into how far down the shaft the quad is and how far off its centre line.
        float  along  = dot(fromApex, axis);
        float  radial = length(fromApex - axis * along);

        float  t      = saturate(along / ConeHeight);
        float  wall   = max(ConeBaseRadius * t, 1e-4);   // the shaft's width at this height

        // Across the shaft: full strength down the middle, easing off before the wall so the cloud
        // has no hard rim. Softness is a fraction of the width here, so the feather narrows towards
        // the tip along with the cone itself.
        float  inner  = wall * (1.0 - saturate(ConeEdgeSoftness));
        float  across = 1.0 - smoothstep(inner, wall, radial);

        // Along the shaft: held bright near the lamp, thinning towards where it lands, and cut off
        // behind the bulb so nothing shows above the lamp.
        float  hold   = saturate(FadeStart);
        float  down   = 1.0 - smoothstep(hold, 1.0, t);
        float  infront = step(0.0, along);

        Fade = across * down * infront;
    }
    else
    {
        // No shaft: fade out at ConeBaseRadius from the apex, holding full strength to FadeStart.
        float dist  = length(fromApex);
        float outer = max(ConeBaseRadius, 1e-4);
        float inner = min(outer * saturate(FadeStart), outer - 1e-4);
        Fade = 1.0 - smoothstep(inner, outer, dist);
    }

    // Vertex colour is the particle system's own: start colour x colour over lifetime, which is
    // where the fade in and the birth and death ramps live.
    Colour = VertexColour.rgb;
    float a = saturate(Fade * VertexColour.a);

    // Ordered 4x4 dither, matching Shader Graph's own Dither node: subtract a per-pixel threshold
    // so alpha clipping turns a smooth fade into a stable screen-door pattern rather than a hard
    // on/off edge. Screen position is the default (normalised) mode, scaled back up to pixels.
    float2 pixel = ScreenPosition.xy * _ScreenParams.xy;
    const float thresholds[16] =
    {
        1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
       13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
        4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
       16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
    };
    uint index = (uint(pixel.x) % 4) * 4 + uint(pixel.y) % 4;

    Alpha = a - thresholds[index];
}

#endif
