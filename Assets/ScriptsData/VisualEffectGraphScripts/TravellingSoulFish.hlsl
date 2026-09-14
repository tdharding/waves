// Soul fish travelling along the level-select water between levels.
//
// Same idea as SoulFishSurfaceSprites (the fish stamped on the arena water), but a separate set of
// globals so the two systems never fight over the same array: these fish belong to the level-select
// river, not to a soul zone.
//
// The controller that moves them does not exist yet. When it does, it should publish these as bare
// $Globals every frame (Shader.SetGlobalVectorArray / SetGlobalTexture / SetGlobalFloat). Pushing
// every frame is deliberate: a shader reimport wipes bare globals and there is no silent fallback,
// so re-pushing is what makes it self-heal on the next frame — same reasoning as the soul-fish
// masks and the instanced lights.
//
// Layout per fish, matching the arena version so a controller can share code:
//   .x/.z = world position on the water   .y = heading in radians   .w = per-fish size (0 = global)
//
// Nothing here needs wiring in the material: drop the TravellingSoulFish subgraph into the water
// graph, and the image plus its size and strength are set centrally by the controller.
#define TRAVELFISH_MAX 32

TEXTURE2D(_TravelSoulFishTex);
SAMPLER(sampler_TravelSoulFishTex);

float4 _TravelSoulFishPositions[TRAVELFISH_MAX];
float  _TravelSoulFishCount;
float  _TravelSoulFishSize;      // world-space width/height of one fish
float  _TravelSoulFishStrength;  // master multiplier on the result

// WorldPos : fragment world position (Position node, World space)
// Colour   : RGBA of the fish covering this pixel
// Alpha    : coverage on its own — lerp the water toward white with this
void TravellingSoulFish_float(
    float3 WorldPos,
    out float4 Colour,
    out float  Alpha)
{
    float4 accum = float4(0.0, 0.0, 0.0, 0.0);
    float  cover = 0.0;

    int   count = min((int)_TravelSoulFishCount, TRAVELFISH_MAX);
    float gsize = max(_TravelSoulFishSize, 1e-4);

    for (int i = 0; i < TRAVELFISH_MAX; i++)
    {
        if (i >= count) break;

        float4 P    = _TravelSoulFishPositions[i];
        float  size = max(P.w > 0.0 ? P.w : gsize, 1e-4);

        // Offset into the fish's own frame so the image turns to face the way it is swimming.
        float2 d = WorldPos.xz - P.xz;
        float  s = sin(P.y), c = cos(P.y);
        float2 r = float2(d.x * c - d.y * s, d.x * s + d.y * c);

        float2 uv = r / size + 0.5;

        // Sample unconditionally with an explicit LOD and mask afterwards: branching around a
        // texture sample puts it in non-uniform control flow, which breaks mip derivatives.
        float inside = (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) ? 1.0 : 0.0;
        float4 t = SAMPLE_TEXTURE2D_LOD(_TravelSoulFishTex, sampler_TravelSoulFishTex, saturate(uv), 0) * inside;

        // Feather the last sliver of the radius so the square edge of the sprite can never show as
        // a hard cut on the water. Full strength everywhere else.
        float radius = length(r) / (size * 0.5);
        t *= smoothstep(1.0, 0.85, radius);

        // Strongest wins, so fish passing over each other don't blow out to white.
        accum = max(accum, t);
        cover = max(cover, t.a);
    }

    Colour = accum * _TravelSoulFishStrength;
    Alpha  = cover * _TravelSoulFishStrength;
}

// Half variant — Shader Graph appends _float or _half depending on node/graph precision, and a
// File custom function must provide whichever it asks for.
void TravellingSoulFish_half(
    half3 WorldPos,
    out half4 Colour,
    out half  Alpha)
{
    float4 c; float a;
    TravellingSoulFish_float(WorldPos, c, a);
    Colour = (half4)c;
    Alpha  = (half)a;
}
