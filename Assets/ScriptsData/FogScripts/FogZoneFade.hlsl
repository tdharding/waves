// Fog zone fade — one level select fog zone's sheet fading at its own edge and in and out as a whole.
//
// Every fog sheet reads the SAME painted field (FogSample.hlsl looks it up by world XZ only), so
// several sheets at different heights all show one fog for the cost of one simulation. What makes
// each zone's sheet its own is this: a per-renderer _FogZone pushed by FogZone.cs through a
// MaterialPropertyBlock — the same route CameraOccluderFader takes with _OccluderFade.
//
//   _FogZone.x  edge fade width in world units, measured in from the sheet's border. 0 = no fade.
//   _FogZone.y  the sheet's size in world units, so UV 0..1 can be turned into a distance.
//   _FogZone.z  how far the whole sheet is faded out, 0 = fully shown, 1 = gone.
//   _FogZone.w  1 = a round sheet, the fade measured in from its rim; 0 = a square, from its sides.
//
// Bare $Globals, deliberately, and ALL ZERO MEANS NO EFFECT. A sheet nobody pushes a block to —
// the single landscape-wide sheet, and every interior level's sheet — reads 0,0,0 and passes its
// alpha through untouched. A shader reimport that wipes the uniform lands on that same no-op.
//
// UV      : the sheet's UV0, 0..1 across the square (FogSheetMesh builds it that way).
// AlphaIn : the alpha the graph has already worked out.
// Alpha   : AlphaIn with the zone's edge and fade taken off.

#ifndef FOG_ZONE_FADE_INCLUDED
#define FOG_ZONE_FADE_INCLUDED

float4 _FogZone;   // x = edge fade width (u), y = sheet size (u), z = faded-out amount 0..1, w = round

void FogZoneFade_float(float2 UV, float AlphaIn, out float Alpha)
{
    float edge = 1.0;
    if (_FogZone.x > 0.0)
    {
        // Distance in from the rim (round) or the nearest side (square), in world units. A round
        // sheet's UV is laid flat across it the same way, so its centre is 0.5, 0.5.
        float2 d    = min(UV, 1.0 - UV) * _FogZone.y;
        float  inward = _FogZone.w > 0.5 ? (0.5 - length(UV - 0.5)) * _FogZone.y : min(d.x, d.y);
        edge = smoothstep(0.0, _FogZone.x, inward);
    }

    Alpha = AlphaIn * edge * (1.0 - saturate(_FogZone.z));
}

void FogZoneFade_half(half2 UV, half AlphaIn, out half Alpha)
{
    float a;
    FogZoneFade_float(UV, AlphaIn, a);
    Alpha = (half)a;
}

#endif
