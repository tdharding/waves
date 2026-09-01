// Builds a world-space normal from the fog's height map, so a flat sheet lights like a mass.
//
// THE GRADIENT MUST BE TAKEN IN WORLD UNITS, NOT TEXELS. Taken per texel it comes out roughly
// twenty times too flat and the fog renders looking completely unshaded — which reads as a broken
// shader rather than a wrong constant. The texel size is fetched from the texture itself so it
// stays correct when the height resolution is changed in the manager.
//
// Feed the result to InstancedLights_float. With no sun in this game, fog away from every street
// light and beyond the boat's radius is flat and dark — that is intended. Masses read as flat
// pale shapes out in the dark and gain their lip as they drift into light.

#ifndef FOG_NORMAL_INCLUDED
#define FOG_NORMAL_INCLUDED

#ifndef FOG_SAMPLE_INCLUDED
TEXTURE2D(_FogHeight);   SAMPLER(sampler_FogHeight);
float4 _FogFieldOrigin;
#endif

void FogNormal_float(
    float3 WorldPos,
    float  HeightScale,   // exaggerates the relief without changing the height map itself
    float  Curvature,     // small = a dramatic domed lip, large = nearly flat
    out float3 Normal,
    out float  Slope)
{
    float w, h;
    _FogHeight.GetDimensions(w, h);
    float2 texel = float2(1.0 / max(w, 1.0), 1.0 / max(h, 1.0));

    // World units one texel covers. This is the conversion the whole function turns on.
    float worldPerTexel = _FogFieldOrigin.w * texel.x;

    float2 uv = (WorldPos.xz - _FogFieldOrigin.xy) * _FogFieldOrigin.z;

    float hL = SAMPLE_TEXTURE2D(_FogHeight, sampler_FogHeight, uv - float2(texel.x, 0)).r;
    float hR = SAMPLE_TEXTURE2D(_FogHeight, sampler_FogHeight, uv + float2(texel.x, 0)).r;
    float hD = SAMPLE_TEXTURE2D(_FogHeight, sampler_FogHeight, uv - float2(0, texel.y)).r;
    float hU = SAMPLE_TEXTURE2D(_FogHeight, sampler_FogHeight, uv + float2(0, texel.y)).r;

    // Central difference, divided by the world distance between the taps — not by 1, and not by
    // the texel count.
    float dx = (hR - hL) * HeightScale / (2.0 * worldPerTexel);
    float dz = (hU - hD) * HeightScale / (2.0 * worldPerTexel);

    float3 n = float3(-dx, max(Curvature, 1e-3), -dz);
    Normal = normalize(n);

    // How steeply the surface is falling here. Highest around the rim, where the lip is.
    Slope = saturate(length(float2(dx, dz)));
}

void FogNormal_half(
    half3 WorldPos, half HeightScale, half Curvature,
    out half3 Normal, out half Slope)
{
    float3 n; float s;
    FogNormal_float(WorldPos, HeightScale, Curvature, n, s);
    Normal = (half3)n; Slope = (half)s;
}

#endif
