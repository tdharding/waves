#ifndef STREETLIGHT_CLOUD_FADE_INCLUDED
#define STREETLIGHT_CLOUD_FADE_INCLUDED

// Everything a street light's particle cloud needs from its material: the quads fade out at the
// edge of the lamp's radius, keep the particle system's own colour and lifetime ramps, and come out
// dithered so they can be drawn with alpha clipping rather than blended transparency.
//
// StreetLightParticles pushes CloudCentre, CloudRadius and CloudFadeStart onto the renderer through
// a MaterialPropertyBlock, so every lamp shares one material and still fades around its own centre.
// The particle's own colour arrives as vertex colour and is read inside the subgraph, so nothing
// using this has to know that.
//
//   Colour — the particle's tint. Straight into Base Color.
//   Alpha  — the dithered value. Into the Alpha block, with Alpha Clip Threshold at 0: the
//            threshold is already subtracted here, so anything below zero is meant to clip.
//   Fade   — the raw 0..1 falloff, undithered, for a transparent material instead.
void StreetLightCloudFade_float(
    float3 WorldPosition,
    float3 CloudCentre,
    float  CloudRadius,
    float  CloudFadeStart,
    float4 VertexColour,
    float4 ScreenPosition,
    out float3 Colour,
    out float  Alpha,
    out float  Fade)
{
    // Distance out from the lamp, against the two radii the fade runs between. The inner edge is
    // held just below the outer one so a fade start of 1 cannot collapse the smoothstep.
    float dist  = distance(WorldPosition, CloudCentre);
    float outer = max(CloudRadius, 1e-4);
    float inner = min(outer * saturate(CloudFadeStart), outer - 1e-4);

    // 1 through the middle of the cloud, easing to 0 exactly at the radius.
    Fade = 1.0 - smoothstep(inner, outer, dist);

    // Vertex colour is the particle system's own: start colour x colour over lifetime, which is
    // where the fade in and the birth and death ramps live. Its alpha multiplies the radius fade.
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
