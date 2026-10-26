// The boat's light on the map — the level select's take on the light the boat carries inside a
// level (the darkness mask on the Waves water, InstancedLights.hlsl on everything else). Two parts,
// both added on top of the world's own light, since nothing out here is dark to begin with:
//
//   the pool   — every surface within Radius of the boat brightens by Strength, fading to nothing
//                over Softness past it (the same 1 - smoothstep shape as the boat light inside);
//   the facing — within the same pool, the faces turned towards the boat brighten by a further
//                Facing Strength, N·L against the boat, the way an instanced light lights the near
//                side of what it stands beside.
//
// The river water takes only the pool: it lies flat and faces straight up, so it would face the
// boat the same everywhere.
//
// All bare $Globals. Settings pushed by LevelSelectLight.Push() from ApplyAesthetics; the boat's
// position by LevelSelectBoatToShaders each LateUpdate, under its own name because RiverRunShader
// already declares _BoatWorldCenter as a graph property and a second declaration would clash. No
// light in the scene → Strength and Facing Strength 0 → nothing added.
//
// Included by RiverRunShading.hlsl and LandscapeShading.hlsl, and the LevelSelectBoatLight
// subgraph's node on the water, so it is guarded.

#ifndef LEVEL_SELECT_BOAT_LIGHT_INCLUDED
#define LEVEL_SELECT_BOAT_LIGHT_INCLUDED

// xyz where the boat is drawn.
float4 _LevelSelectBoatPosition;

// x Radius, y Softness, z Strength, w Facing Strength.
float4 _LevelSelectBoatLight;

// rgb the light's colour.
float4 _LevelSelectBoatLightColour;

// How much the boat lights this point, before its colour. Facing 0 leaves the facing part out.
float LevelSelectBoatLightAmount(float3 n, float3 worldPos, float facing)
{
    float3 d      = _LevelSelectBoatPosition.xyz - worldPos;
    float  dist   = length(d);
    float  radius = max(_LevelSelectBoatLight.x, 0.0);
    float  radial = 1.0 - smoothstep(radius, radius + max(_LevelSelectBoatLight.y, 1e-4), dist);

    float ndl = saturate(dot(n, d / max(dist, 1e-4)));

    return radial * (_LevelSelectBoatLight.z + ndl * _LevelSelectBoatLight.w * facing);
}

// The boat's light added onto a colour. n must be unit length.
float3 LevelSelectBoatLightOnto(float3 colour, float3 n, float3 worldPos, float facing)
{
    return colour + _LevelSelectBoatLightColour.rgb * LevelSelectBoatLightAmount(n, worldPos, facing);
}

// The subgraph's node, for the river water — the pool only.
void LevelSelectBoatLight_float(float3 In, float3 WorldPos, out float3 Out)
{
    Out = LevelSelectBoatLightOnto(In, float3(0.0, 1.0, 0.0), WorldPos, 0.0);
}

void LevelSelectBoatLight_half(half3 In, float3 WorldPos, out half3 Out)
{
    float3 o;
    LevelSelectBoatLight_float(In, WorldPos, o);
    Out = o;
}

#endif
