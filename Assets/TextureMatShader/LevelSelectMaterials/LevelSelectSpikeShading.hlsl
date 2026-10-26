// How the level select's procedural spikes are shaded.
//
// A spike on the map is part of the landscape, so it wears the landscape's stone: one of the
// three variants (A, B, C) from the Level Select Landscape Tuner — its colour, its grain, and the
// landscape's light — picked by the tuner's Spikes setting. On top of that, the spiral grooves the
// mesh carved are darkened exactly as they are on a level's rocks (SpikeSpiralShade), with the
// groove's Softness, Darkness and Resolution also in the tuner.
//
// Every setting is a bare $Global pushed each frame by LandscapeShadingSettings, like the rest of
// the landscape — nothing here has a material value to fall back on.

#ifndef LEVEL_SELECT_SPIKE_SHADING_INCLUDED
#define LEVEL_SELECT_SPIKE_SHADING_INCLUDED

#include "Assets/TextureMatShader/LevelSelectMaterials/LandscapeShading.hlsl"
#include "Assets/TextureMatShader/Maze/SpikeSpiral.hlsl"

// Which variant the spikes wear: 0 A, 1 B, 2 C.
float  _LandscapeSpikeVariant;

// The spiral grooves: x Softness, y Darkness, z Resolution (0 = smooth).
float4 _LandscapeSpikeGroove;

// Normal   : the spike's world normal. The mesh is carved, so its own normals already follow
//            the grooves — no hill maths needed.
// WorldPos : world position, for the grain and the light.
// UV2      : UV channel 2, where ProceduralSpikeMesh bakes where its grooves are.
// Colour   : the spike, in its variant, grained, lit and grooved.
// BentNormal : Normal bent by the Spikes designation's normal map decal groups — the one the
//            light is worked out from, handed out for the River Glow.
void LevelSelectSpikeShading_float(float3 Normal, float3 WorldPos, float4 UV2,
                                   out float3 Colour, out float3 BentNormal)
{
    float3 n = normalize(Normal);

    float3 stone = LandscapeVariant(_LandscapeSpikeVariant, WorldPos, n);

    // The Spikes designation's decal groups, on the stone and under the light and the grooves.
    stone = LandscapeDecalsOnGround(stone, WorldPos, n, _LandscapeDecalDesignations.y);

    // Its normal map groups bend the light only, as on the hills.
    BentNormal = LandscapeNormalDecalsOnGround(WorldPos, n, _LandscapeNormalDecalDesignations.y);

    float shade, groove;
    SpikeSpiralShade_float(UV2, _LandscapeSpikeGroove.x, _LandscapeSpikeGroove.y,
                           _LandscapeSpikeGroove.z, shade, groove);

    // Groove shade last, so the light cannot lift it back out.
    Colour = stone * LandscapeLight(BentNormal, WorldPos) * shade;
}

void LevelSelectSpikeShading_half(half3 Normal, half3 WorldPos, half4 UV2,
                                  out half3 Colour, out half3 BentNormal)
{
    float3 colour, bent;
    LevelSelectSpikeShading_float(Normal, WorldPos, UV2, colour, bent);
    Colour     = colour;
    BentNormal = bent;
}

#endif
