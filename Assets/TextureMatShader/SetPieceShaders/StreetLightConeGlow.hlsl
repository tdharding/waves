#ifndef STREETLIGHT_CONE_GLOW_INCLUDED
#define STREETLIGHT_CONE_GLOW_INCLUDED

// The look of the shaft of light for the cone mesh StreetLightCone builds. The result is an opacity:
// the material blends the shaft over what is behind it rather than adding to it.
//
// Everything is driven by UV, never by the mesh's own normals, so the shading owes nothing to how
// many segments the cone is built from. U runs once around the shaft, V from the bulb to where it
// lands.
//
//   Down the shaft   — strongest at the bulb, thinning to nothing towards where it lands.
//   Base depth       — how far up from the base Base Opacity reaches, so the pool where the light
//                      lands is its own band rather than a floor under the whole shaft.
//   Across the shaft — fades at the silhouette so the mesh reads as volume, not cardboard.
//   The curtain      — bands running the length of the shaft, turning slowly around it.
//   Apex density     — holds the top solid: inside it the curtain and the edge fade are held back,
//                      so the top actually reaches Top Opacity instead of being multiplied down by
//                      both. They come in as the shaft widens.
//   Apex mask        — a feathered circle at the tip, measured on screen, that cuts the shaft away
//                      where it would otherwise visibly attach to the lamp.
void StreetLightConeGlow_float(
    float2 UV,
    float3 ViewDirWS,
    float4 ScreenPosition,
    float3 ApexWS,
    float  ConeSlope,
    float  TopOpacity,
    float  BaseOpacity,
    float  FalloffPower,
    float  EdgeSoftness,
    float  ApexDensity,
    float  ApexMaskRadius,
    float  ApexMaskFeather,
    float  CurtainBands,
    float  CurtainStrength,
    float  CurtainSpeed,
    float  CurtainSoftness,
    float  BaseDepth,
    out float Intensity)
{
    const float TAU = 6.2831853;

    float angle = UV.x * TAU;
    float t     = saturate(UV.y);

    // Down the shaft. Above 1 holds the strength near the lamp and drops it away late; below 1
    // spreads it evenly down the beam. The ramp runs out to nothing at the base.
    float down = pow(saturate(1.0 - t), max(FalloffPower, 0.001));
    float lit  = TopOpacity * down;

    // The pool at the far end. Base Opacity only reaches up as far as Base Depth, so it can be
    // tuned as a band where the light lands rather than acting as a floor under the whole shaft.
    float basin = BaseDepth <= 0.001 ? 0.0 : smoothstep(1.0 - saturate(BaseDepth), 1.0, t);
    lit = lerp(lit, BaseOpacity, basin);

    // The surface direction, rebuilt per pixel rather than interpolated: at this angle the outward
    // direction is (cos, slope, sin), slope being how far the wall leans out per unit of length.
    float3 normalOS = normalize(float3(cos(angle), ConeSlope, sin(angle)));
    float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));

    // Across the shaft: 1 head-on, 0 at the silhouette.
    float facing = saturate(dot(normalWS, normalize(ViewDirWS)));
    float soft   = saturate(EdgeSoftness);
    float across = soft <= 0.001 ? 1.0 : smoothstep(0.0, soft, facing);

    // The curtain. One wave per band around the cone, slid along by time so the set turns; speed is
    // in turns per second. Softness shapes them: 1 is the plain cosine, towards 0 pinches them into
    // narrow ribbons.
    float phase = UV.x * max(CurtainBands, 0.0) - _Time.y * CurtainSpeed;
    float band  = 0.5 + 0.5 * cos(phase * TAU);
    band = pow(saturate(band), lerp(8.0, 1.0, saturate(CurtainSoftness)));
    float curtain = lerp(1.0, band, saturate(CurtainStrength));

    // Apex density: 0 at the tip, reaching 1 once the shaft is ApexDensity of the way down. The two
    // things that thin the shaft are faded in over that distance, leaving the top at full strength.
    float dense = ApexDensity <= 0.001 ? 1.0 : smoothstep(0.0, saturate(ApexDensity), t);
    across  = lerp(1.0, across,  dense);
    curtain = lerp(1.0, curtain, dense);

    Intensity = saturate(lit * across * curtain);

    // Apex mask: a circle around the lamp on screen. Both points are taken to normalised device
    // coordinates the same way, and the horizontal is scaled by the aspect so the circle stays
    // round rather than stretching with the window.
    if (ApexMaskRadius > 0.0001)
    {
        float4 apexCS  = TransformWorldToHClip(ApexWS);
        float2 apexNDC = apexCS.xy / max(apexCS.w, 1e-5);
        apexNDC.y     *= _ProjectionParams.x;      // flipped projection targets

        float2 pixelNDC = ScreenPosition.xy * 2.0 - 1.0;

        float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
        float2 d     = (pixelNDC - apexNDC) * float2(aspect, 1.0);
        float  dist  = length(d) * 0.5;            // NDC spans 2 units, so halve to read as a fraction

        float outer = ApexMaskRadius;
        float inner = outer * (1.0 - saturate(ApexMaskFeather));
        Intensity  *= smoothstep(inner, outer, dist);
    }
}

#endif
