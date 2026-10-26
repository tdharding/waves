// The level select's baked shadows, cast by the LevelSelectLight.
//
// Bake Shadows (Lighting window) sends a ray out from the light through every texel of a
// shadow map and stores how far it got before it met something. Here a pixel looks up the texel
// for its own direction from the light: if it is farther away than that, something stands between
// it and the light, and it is in shadow. The light never moves, so the map is baked once.
//
// Drop the LevelSelectShadow subgraph between a graph's colour and its output: Colour in, the same
// colour out with the shadows multiplied in.
//
// The map is octahedral — the sphere of directions folded flat, straight down at its centre.
// LSShadow_Fold must stay the inverse of LevelSelectShadowBaker.Unfold.

#ifndef LEVELSELECT_SHADOW_INCLUDED
#define LEVELSELECT_SHADOW_INCLUDED

// Distance from the light to the first thing in each direction.
TEXTURE2D(_LevelSelectShadowMap);

// xyz where the light stood when the map was baked; w the map's size in texels — 0 when nothing
// is baked, which leaves everything lit.
float4 _LevelSelectShadowFrom;

// x Darkness (0 none, 1 black), y Edge Softness in texels of a 2048 map.
float4 _LevelSelectShadowParams;

// About how wide a texel is, as an angle, times the map size. The fold stretches texels towards
// its edges, so this is on the generous side; it sets how far a surface is lifted off itself
// before it is compared, so that it never shadows itself by rounding.
#define LSSHADOW_TEXEL_ANGLE 3.0

// The map size the lift and the Edge Softness were tuned on. Both are measured against it
// whatever size the map is baked at, so a bigger map only sharpens the shadows' shapes — the
// lift and the soft edge stay the same width in the world.
#define LSSHADOW_TUNED_SIZE 2048.0

// A direction from the light → its place on the map, 0..1.
float2 LSShadow_Fold(float3 d)
{
    d /= abs(d.x) + abs(d.y) + abs(d.z);
    float2 ab = d.xz;
    if (d.y > 0.0)   // above the light: folded out into the corners
    {
        float2 s = float2(ab.x >= 0.0 ? 1.0 : -1.0, ab.y >= 0.0 ? 1.0 : -1.0);
        ab = (1.0 - abs(ab.yx)) * s;
    }
    return ab * 0.5 + 0.5;
}

// How lit a point at this distance is, at one place on the map (in texels): the four nearest
// texels each say lit or not, blended by how close the place is to each — so even a hard edge is
// a texel wide rather than stepped.
float LSShadow_Lit(float2 texel, float dist, int last)
{
    float2 t = texel - 0.5;
    float2 i = floor(t);
    float2 f = t - i;
    int2   p = int2(i);

    float s00 = LOAD_TEXTURE2D(_LevelSelectShadowMap, clamp(p,              int2(0, 0), int2(last, last))).r;
    float s10 = LOAD_TEXTURE2D(_LevelSelectShadowMap, clamp(p + int2(1, 0), int2(0, 0), int2(last, last))).r;
    float s01 = LOAD_TEXTURE2D(_LevelSelectShadowMap, clamp(p + int2(0, 1), int2(0, 0), int2(last, last))).r;
    float s11 = LOAD_TEXTURE2D(_LevelSelectShadowMap, clamp(p + int2(1, 1), int2(0, 0), int2(last, last))).r;

    float l00 = dist <= s00 ? 1.0 : 0.0;
    float l10 = dist <= s10 ? 1.0 : 0.0;
    float l01 = dist <= s01 ? 1.0 : 0.0;
    float l11 = dist <= s11 ? 1.0 : 0.0;
    return lerp(lerp(l00, l10, f.x), lerp(l01, l11, f.x), f.y);
}

void LevelSelectShadow_float(float3 Colour, float3 Normal, float3 WorldPos, out float3 Out)
{
    float size = _LevelSelectShadowFrom.w;
    float darkness = saturate(_LevelSelectShadowParams.x);
    if (size < 1.0 || darkness <= 0.0)
    {
        Out = Colour;
        return;
    }

    float3 from = _LevelSelectShadowFrom.xyz;
    float  soft = max(_LevelSelectShadowParams.y, 0.0);

    // How wide a texel is out here, in metres. The surface is lifted off itself along its normal
    // and pulled towards the light by about that much (more for a softer edge, which looks wider).
    float texelWorld = length(WorldPos - from) * (LSSHADOW_TEXEL_ANGLE / LSSHADOW_TUNED_SIZE);
    float3 p = WorldPos + normalize(Normal) * texelWorld * (1.0 + soft);

    float3 v    = p - from;
    float  dist = length(v);
    float2 texel = LSShadow_Fold(v / max(dist, 1e-5)) * size;
    dist -= texelWorld * (1.0 + soft);

    int last = (int)size - 1;
    float lit;
    if (soft < 0.01)
    {
        lit = LSShadow_Lit(texel, dist, last);
    }
    else
    {
        // A 4×4 spread of lookups across the soft edge's width.
        lit = 0.0;
        float spread = soft / 1.5 * (size / LSSHADOW_TUNED_SIZE);
        [unroll] for (int y = 0; y < 4; y++)
        [unroll] for (int x = 0; x < 4; x++)
            lit += LSShadow_Lit(texel + (float2(x, y) - 1.5) * spread, dist, last);
        lit *= 1.0 / 16.0;
    }

    Out = Colour * lerp(1.0 - darkness, 1.0, lit);
}

void LevelSelectShadow_half(half3 Colour, half3 Normal, half3 WorldPos, out half3 Out)
{
    float3 colour;
    LevelSelectShadow_float(Colour, Normal, WorldPos, colour);
    Out = colour;
}

#endif
