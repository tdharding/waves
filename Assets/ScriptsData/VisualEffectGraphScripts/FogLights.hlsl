// The fog's lights: every instanced light (street lights) PLUS the boat, as one more light point.
//
// Same signature as InstancedLights_float, so it drops into FogSheet in place of that node and
// FogCompose sees the boat through the same Light (rim) and Proximity (body) terms as a lamp.
//
// Why the boat is not simply registered with InstancedLightManager: the object graphs that read
// the instanced lights already light themselves from the boat separately, so they would get the
// boat twice. This keeps the extra point to the fog alone.
//
// _FogBoatLight is pushed every frame by FogFieldManager: .xyz = the boat the field centres on,
// .w = radius. A radius of 0 (nothing pushing yet) adds nothing.

#ifndef FOG_LIGHTS_INCLUDED
#define FOG_LIGHTS_INCLUDED

#include "InstancedLights.hlsl"

float4 _FogBoatLight;

void FogLights_float(
    float3 WorldPos,
    float3 WorldNormal,
    out float Light,
    out float Proximity)
{
    InstancedLights_float(WorldPos, WorldNormal, Light, Proximity);

    if (_FogBoatLight.w <= 0.0) return;

    float3 n = dot(WorldNormal, WorldNormal) > 1e-8 ? normalize(WorldNormal) : float3(0.0, 1.0, 0.0);

    float radial, ndl;
    InstancedLightPoint(WorldPos, n, _FogBoatLight.xyz, _FogBoatLight.w,
                        saturate(_InstLightFalloff), radial, ndl);

    Light     += radial * ndl * _InstLightIntensity;
    Proximity += radial       * _InstLightIntensity;
}

void FogLights_half(
    half3 WorldPos,
    half3 WorldNormal,
    out half Light,
    out half Proximity)
{
    float l, p;
    FogLights_float(WorldPos, WorldNormal, l, p);
    Light     = (half)l;
    Proximity = (half)p;
}

#endif
