// The level select's light on anything that isn't a run or a hill — the same made-up light the
// river runs and the landscape are lit by, with the same maths: the scene's LevelSelectLight
// object, where it stands and how strong it is.
//
// Half lambert: 1 square on to the light, 0.5 side on, 0 turned right away. Strength lerps out of
// it rather than scaling it, so 0 leaves the colour at full and unlit rather than at black; past 1
// the fade is done and the rest multiplies the lit result. No light in the scene → Strength 0 →
// the colour comes through untouched.
//
// Both settings are bare $Globals, pushed by LevelSelectLight.Push() from ApplyAesthetics, so a
// graph only has to wire its colour through the node. The same two are declared in
// RiverRunShading.hlsl and LandscapeShading.hlsl, so this node can't share a graph with either of
// those (a second declaration won't compile).

#ifndef LEVEL_SELECT_LIGHTING_INCLUDED
#define LEVEL_SELECT_LIGHTING_INCLUDED

float4 _LevelSelectLightPosition;
float  _LevelSelectLightStrength;

void LevelSelectLighting_float(float3 In, float3 WorldNormal, float3 WorldPos, out float3 Out)
{
    // Interpolated across a face a normal comes out shorter than one unit, which would read as the
    // surface having turned further from the light than it really has.
    float3 n = normalize(WorldNormal);

    float3 toLight = _LevelSelectLightPosition.xyz - WorldPos;
    toLight = dot(toLight, toLight) > 1e-8 ? normalize(toLight) : float3(0.0, 1.0, 0.0);

    float ndl   = dot(n, toLight) * 0.5 + 0.5;
    float light = lerp(1.0, saturate(ndl), saturate(_LevelSelectLightStrength))
                * max(_LevelSelectLightStrength, 1.0);

    Out = In * light;
}

void LevelSelectLighting_half(half3 In, half3 WorldNormal, float3 WorldPos, out half3 Out)
{
    float3 o;
    LevelSelectLighting_float(In, WorldNormal, WorldPos, o);
    Out = o;
}

#endif
