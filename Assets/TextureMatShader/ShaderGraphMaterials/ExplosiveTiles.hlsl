#ifndef EXPLOSIVE_TILES_INCLUDED
#define EXPLOSIVE_TILES_INCLUDED

// ============================================================
// INTERNAL HELPERS
// ============================================================

float2 ET_Hash22_f(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453123);
}

half2 ET_Hash22_h(half2 p)
{
    p = half2(dot(p, half2(127.1, 311.7)), dot(p, half2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453123);
}

// ============================================================
// EXPLOSIVE TILES — float
// Per-tile independent scale, rotation, and scroll.
// Feed TiledUV into a Texture2D Sample node.
// CellID (normalised tile coords) can drive per-tile tint/mask downstream.
// ============================================================

void ExplosiveTiles_float(
    float2 UV,
    float  TileCount,
    float  MinScale,
    float  MaxScale,
    float  RotationStrength,
    float  Time,
    float  ScrollSpeed,
    float  ScrollVariance,
    out float2 TiledUV,
    out float2 CellID
)
{
#if SHADERGRAPH_PREVIEW
    TiledUV = UV;
    CellID  = float2(0.0, 0.0);
#else
    float2 scaledUV = UV * TileCount;
    float2 tileID   = floor(scaledUV);
    float2 localUV  = frac(scaledUV);           // [0,1] within the current tile

    // Two independent hashes per tile for variety
    float2 h  = ET_Hash22_f(tileID);
    float2 h2 = ET_Hash22_f(tileID + float2(37.3, 91.7));

    // Per-tile scale — zooms each tile's window into the texture differently
    float tileScale = lerp(MinScale, MaxScale, h.x);

    // Per-tile rotation — h.y seeds a full 0–360° spin, RotationStrength scales it
    float angle = h.y * 6.28318 * RotationStrength;
    float sinA  = sin(angle);
    float cosA  = cos(angle);

    // Per-tile scroll — random direction, speed varied by ScrollVariance
    float2 scrollDir = normalize(h2 * 2.0 - 1.0);
    float  speed     = ScrollSpeed * lerp(1.0 - ScrollVariance * 0.5,
                                          1.0 + ScrollVariance * 0.5, h2.y);
    float2 scroll    = scrollDir * speed * Time;

    // Transform: centre on tile, rotate, scale, then jump to a random region
    // of the texture (+ h) and apply scroll offset
    float2 uv = localUV - 0.5;
    uv = float2(cosA * uv.x - sinA * uv.y,
                sinA * uv.x + cosA * uv.y);
    uv *= tileScale;
    uv += 0.5 + h + scroll;

    TiledUV = uv;
    CellID  = tileID / TileCount;   // normalised [0,1] — use for per-tile tinting
#endif
}

// ============================================================
// EXPLOSIVE TILES — half
// ============================================================

void ExplosiveTiles_half(
    half2 UV,
    half  TileCount,
    half  MinScale,
    half  MaxScale,
    half  RotationStrength,
    half  Time,
    half  ScrollSpeed,
    half  ScrollVariance,
    out half2 TiledUV,
    out half2 CellID
)
{
#if SHADERGRAPH_PREVIEW
    TiledUV = UV;
    CellID  = half2(0.0, 0.0);
#else
    half2 scaledUV = UV * TileCount;
    half2 tileID   = floor(scaledUV);
    half2 localUV  = frac(scaledUV);

    half2 h  = ET_Hash22_h(tileID);
    half2 h2 = ET_Hash22_h(tileID + half2(37.3, 91.7));

    half tileScale = lerp(MinScale, MaxScale, h.x);

    half angle = h.y * 6.28318 * RotationStrength;
    half sinA  = sin(angle);
    half cosA  = cos(angle);

    half2 scrollDir = normalize(h2 * 2.0 - 1.0);
    half  speed     = ScrollSpeed * lerp(1.0 - ScrollVariance * 0.5,
                                         1.0 + ScrollVariance * 0.5, h2.y);
    half2 scroll    = scrollDir * speed * Time;

    half2 uv = localUV - 0.5;
    uv = half2(cosA * uv.x - sinA * uv.y,
               sinA * uv.x + cosA * uv.y);
    uv *= tileScale;
    uv += 0.5 + h + scroll;

    TiledUV = uv;
    CellID  = tileID / TileCount;
#endif
}

#endif
