// Instanced point lights — the multi-point generalization of the boat light.
//
// The boat casts ONE light: a radial falloff (1 - smoothstep) around _BoatWorldCenter, plus a
// lit/dark side from dot(normal, dir-to-boat). This does the same for an ARRAY of light points
// (street lights first, but any registered light source) and accumulates their contribution, so
// objects pick up extra light from every nearby lit point.
//
// Feed by InstancedLightManager, which pushes these as bare $Globals every frame via
// Shader.SetGlobalVectorArray / SetGlobalFloat. Because they are re-pushed each frame, a shader
// reimport that wipes them self-heals on the next frame (same reasoning as the soul-fish masks).
// A global array locks its size on first set, so the manager always pushes the full slot count.
#define INST_LIGHT_MAX 16

float4 _InstLightPositions[INST_LIGHT_MAX]; // .xyz = world position, .w = radius
float  _InstLightCount;
float  _InstLightIntensity;                 // global brightness multiplier
float  _InstLightFalloff;                   // 0..1: fraction of the radius that stays full before fading

// WorldPos   : fragment world position (Position node, World space)
// WorldNormal: fragment world normal   (Normal Vector node, World space)
// Light      : accumulated LIT-SIDE light — radial falloff × saturate(N·L). Add to emission/albedo.
// Proximity  : accumulated radial falloff only (no normal term) — for a flat proximity glow.
void InstancedLights_float(
    float3 WorldPos,
    float3 WorldNormal,
    out float Light,
    out float Proximity)
{
    float light = 0.0;
    float prox  = 0.0;

    int   count = min((int)_InstLightCount, INST_LIGHT_MAX);
    float inner = saturate(_InstLightFalloff);

    // Guard against zero-length normals (e.g. unlit/flat use) so N·L stays well-defined.
    float3 n = dot(WorldNormal, WorldNormal) > 1e-8 ? normalize(WorldNormal) : float3(0.0, 1.0, 0.0);

    for (int i = 0; i < INST_LIGHT_MAX; i++)
    {
        if (i >= count) break;

        float3 lp   = _InstLightPositions[i].xyz;
        float  r    = max(_InstLightPositions[i].w, 0.0001);
        float3 d    = lp - WorldPos;
        float  dist = length(d);

        // Radial falloff: 1 inside inner·r, easing to 0 at r — the same shape as the boat's
        // (1 - smoothstep). Lights past their radius contribute nothing.
        float radial = 1.0 - smoothstep(r * inner, r, dist);

        // Lit side: surfaces facing this light brighten. dir points fragment → light, so a normal
        // pointing at the light gives N·L > 0. (The boat uses dir-to-boat for its dark side; this
        // is the same term, oriented to add light on the near-facing side.)
        float ndl = saturate(dot(n, d / max(dist, 1e-4)));

        light += radial * ndl;
        prox  += radial;
    }

    Light     = light * _InstLightIntensity;
    Proximity = prox  * _InstLightIntensity;
}

// Half-precision variant. Shader Graph appends _float or _half to the function name based on the
// node/graph precision; a File custom function must supply whichever it asks for. This just
// forwards to the float implementation (the globals are float, and the maths is cheap).
void InstancedLights_half(
    half3 WorldPos,
    half3 WorldNormal,
    out half Light,
    out half Proximity)
{
    float l, p;
    InstancedLights_float(WorldPos, WorldNormal, l, p);
    Light     = (half)l;
    Proximity = (half)p;
}
