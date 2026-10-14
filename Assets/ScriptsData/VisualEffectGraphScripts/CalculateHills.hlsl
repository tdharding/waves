// What is left of the landscape's hill shader. The hills, their rocky noise, the lighting normal
// and the river edges are all BAKED into the tile meshes now (LandscapeTool / LandscapeTileBaker,
// maths in LandscapeHillHeight.cs). The last version that worked them out here is in
// Archive/LiveHillShader.
//
// Only the noise lean's grain is drawn per pixel, so Landscape Shading's NoiseUp / NoiseDown keep
// their full detail between vertices. The noise MUST stay a line-for-line copy of
// LandscapeHillHeight.GradientNoise — integer hash, no sin — or the lean and the geometry part.
#ifndef CALCULATE_HILLS_INCLUDED
#define CALCULATE_HILLS_INCLUDED

uniform float _HillNoiseScale;   // global noise frequency, sent by LandscapeTool every tick

// Integer-hash value noise (no sin, so it can be mirrored on the CPU).
float _HillHash(float2 ip)
{
    uint x = (uint)((int)ip.x) * 374761393u;
    uint y = (uint)((int)ip.y) * 668265263u;
    uint h = x + y;
    h = (h ^ (h >> 13)) * 1274126177u;
    h = h ^ (h >> 16);
    return (float)(h & 0x00FFFFFFu) / 16777216.0;
}

// Random unit gradient at a lattice point (hash -> angle).
float2 _HillGrad(float2 ip)
{
    float ang = _HillHash(ip) * 6.2831853;   // 0..2*pi
    return float2(cos(ang), sin(ang));
}

// Gradient (Perlin-style) noise, remapped to ~[0,1].
float _HillGradientNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);

    float d00 = dot(_HillGrad(i + float2(0.0, 0.0)), f - float2(0.0, 0.0));
    float d10 = dot(_HillGrad(i + float2(1.0, 0.0)), f - float2(1.0, 0.0));
    float d01 = dot(_HillGrad(i + float2(0.0, 1.0)), f - float2(0.0, 1.0));
    float d11 = dot(_HillGrad(i + float2(1.0, 1.0)), f - float2(1.0, 1.0));

    float n = lerp(lerp(d00, d10, u.x), lerp(d01, d11, u.x), u.y);
    return n * 0.7071 + 0.5;   // ~[-0.707,0.707] -> ~[0,1]
}

// Baked : the tile's UV1 — x the lean without its grain, y how much noise there is here.
// Noise : how the rocky noise leans here (x, -1 fully down to +1 fully up) and how much of it
//         there is (y), for Landscape Shading.
void LandscapeBakedNoise_float(float3 WorldPos, float2 Baked, out float2 Noise)
{
    float grain = _HillGradientNoise(WorldPos.xz * _HillNoiseScale) * 2.0 - 1.0;
    Noise = float2(grain * Baked.x, Baked.y);
}

void LandscapeBakedNoise_half(half3 WorldPos, half2 Baked, out half2 Noise)
{
    float2 noise;
    LandscapeBakedNoise_float(WorldPos, Baked, noise);
    Noise = noise;
}

#endif // CALCULATE_HILLS_INCLUDED
