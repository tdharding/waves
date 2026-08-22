// Per-fish sprite stamped on the water surface above each soul fish.
//
// SoulFishController publishes one entry per live fish every frame, plus the texture itself, as
// bare $Globals. Because everything is re-pushed each frame a shader reimport that wipes the
// globals self-heals on the next frame (same reasoning as the soul-fish masks and instanced lights).
//
// Layout per fish:
//   .x/.z = world position of the fish   .y = heading in radians   .w = per-fish scale (0 = global)
//
// The texture is a global too, so the Custom Function needs no texture/sampler ports — drop the
// SoulFishSurfaceSprites subgraph into any water graph and it just works, with the image and its
// size/strength set centrally on the SoulFishController.
#define SOULFISH_SPRITE_MAX 24

TEXTURE2D(_SoulFishSpriteTex);
SAMPLER(sampler_SoulFishSpriteTex);

float4 _SoulFishSpritePositions[SOULFISH_SPRITE_MAX];
float  _SoulFishSpriteCount;
float  _SoulFishSpriteSize;      // world-space width/height of one sprite
float  _SoulFishSpriteStrength;  // master multiplier on the result

// WorldPos : fragment world position (Position node, World space)
// Colour   : accumulated RGBA of every sprite covering this pixel
// Alpha    : accumulated coverage on its own — lerp/mask with this
void SoulFishSurfaceSprites_float(
    float3 WorldPos,
    out float4 Colour,
    out float  Alpha)
{
    float4 accum = float4(0.0, 0.0, 0.0, 0.0);
    float  cover = 0.0;

    int   count = min((int)_SoulFishSpriteCount, SOULFISH_SPRITE_MAX);
    float gsize = max(_SoulFishSpriteSize, 1e-4);

    for (int i = 0; i < SOULFISH_SPRITE_MAX; i++)
    {
        if (i >= count) break;

        float4 P    = _SoulFishSpritePositions[i];
        float  size = max(P.w > 0.0 ? P.w : gsize, 1e-4);

        // Offset into the fish's own frame so the image turns with it.
        float2 d = WorldPos.xz - P.xz;
        float  s = sin(P.y), c = cos(P.y);
        float2 r = float2(d.x * c - d.y * s, d.x * s + d.y * c);

        float2 uv = r / size + 0.5;

        // Sample unconditionally with an explicit LOD and mask afterwards: branching around a
        // texture sample puts it in non-uniform control flow, which breaks mip derivatives.
        float inside = (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) ? 1.0 : 0.0;
        float4 t = SAMPLE_TEXTURE2D_LOD(_SoulFishSpriteTex, sampler_SoulFishSpriteTex, saturate(uv), 0) * inside;

        // Strongest wins, so overlapping fish don't blow out to white.
        accum = max(accum, t);
        cover = max(cover, t.a);
    }

    Colour = accum * _SoulFishSpriteStrength;
    Alpha  = cover * _SoulFishSpriteStrength;
}

// Half variant — Shader Graph appends _float or _half depending on node/graph precision, and a
// File custom function must provide whichever it asks for.
void SoulFishSurfaceSprites_half(
    half3 WorldPos,
    out half4 Colour,
    out half  Alpha)
{
    float4 c; float a;
    SoulFishSurfaceSprites_float(WorldPos, c, a);
    Colour = (half4)c;
    Alpha  = (half)a;
}
