// How the level select's landscape hills are shaded.
//
// The same idea as the river runs' stone, kept entirely separate from it: four stone VARIANTS
// (A, B, C, D), each a colour with its own grain, and three PARTS of the landscape that each wear one
// of them — Holes, NoiseUp and NoiseDown.
//
// A run's parts are baked into its mesh as it is built. The hills are not built — they are raised
// in the vertex shader by CalculateHills — so which part a pixel belongs to is worked out here,
// from two things only:
//   * how deep it sits below the tile base (Holes below Hole Depth), and
//   * which way the rocky noise leans (NoiseUp on the ups, NoiseDown on the downs).
// Holes win; everything else is one side of the noise or the other. Ground level carries no lean
// either way, so it lands wherever Noise Softness puts the middle — on the NoiseDown side when
// the split is hard, halfway between the two when it is not.
//
// On top of that, the ground can be PAINTED Red, Blue or Yellow on the Texture tab of the
// designer's Landscape Maps. Each colour is a designation with its own three parts; unpainted
// ground is the Landscape designation. The painting is read here, blended between its cells, so
// a pixel can be part one designation and part another (Paint Softness).
//
// Every setting is a bare $Global pushed each frame by LandscapeShadingSettings — there is no
// property block behind any of them, so nothing here has a material value to fall back on.
// Authored in Tools > Waves > Level Select Landscape Tuner.

#ifndef LANDSCAPE_SHADING_INCLUDED
#define LANDSCAPE_SHADING_INCLUDED

// What the landscape starts as, under every variant. Alpha is ignored.
float4 _LandscapeBaseColour;

// The four variants' colours. Alpha is how much each covers the base colour.
float4 _LandscapeColourA;
float4 _LandscapeColourB;
float4 _LandscapeColourC;
float4 _LandscapeColourD;

// x A, y B, z C, w D. Grain size is metres per noise cell, 0 turns it off.
float4 _LandscapeGrainSizes;
float4 _LandscapeGrainStrengths;

// Which variant each part wears: 0 A, 1 B, 2 C, 3 D. x Holes, y NoiseUp, z NoiseDown.
float4 _LandscapePartVariants;

// The same, for ground painted Red, Blue and Yellow.
float4 _LandscapeRedPartVariants;
float4 _LandscapeBluePartVariants;
float4 _LandscapeYellowPartVariants;

// The painted Texture map, pushed by LandscapeTool: one texel per cell, r Red, g Blue, b Yellow.
// Area: xy the min world X/Z it lies from, zw one over its width and depth. Size: xy the cells,
// z 1 when something is painted — at 0 everything is Landscape and the map is never read.
TEXTURE2D(_LandscapePaintMap);
float4 _LandscapePaintArea;
float4 _LandscapePaintSize;

// How wide the blend between painted colours is: 0 a crisp line, 1 faded across the cells.
float  _LandscapePaintSoftness;

// Where Holes split off. Hole Depth is metres below the tile base (negative is above it), and
// Softness is how wide the blend is as a fraction of it.
float  _LandscapeHoleDepth;
float  _LandscapeSoftness;

// How wide the blend between NoiseUp and NoiseDown is, as a fraction of the noise's own lean.
// 0 is a hard line down the middle of the noise, 1 blends across the whole of it.
float  _LandscapeNoiseSoftness;

// The tile surface's world height — the level Holes are measured down from. Pushed from the
// designer's landscape World Y + Height Offset, not tuned.
float  _LandscapeBaseY;

// The light — the scene's LevelSelectLight object, shared with the river runs: where it stands
// and how strong it is.
float4 _LevelSelectLightPosition;
float  _LevelSelectLightStrength;

// The boat's light, added over it on the hills (not the trees, nor the spikes).
#include "Assets/TextureMatShader/LevelSelectMaterials/LevelSelectBoatLight.hlsl"

// Which way the noise leans in the cell at p — arithmetic rather than a sine, so every machine
// agrees where the grain is.
float2 LandscapeNoiseDir(float2 p)
{
    p = fmod(p, 289.0);
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

// Gradient noise about zero, the same shape as the Gradient Noise node.
float LandscapeGradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);

    float d00 = dot(LandscapeNoiseDir(ip),                    fp);
    float d01 = dot(LandscapeNoiseDir(ip + float2(0.0, 1.0)), fp - float2(0.0, 1.0));
    float d10 = dot(LandscapeNoiseDir(ip + float2(1.0, 0.0)), fp - float2(1.0, 0.0));
    float d11 = dot(LandscapeNoiseDir(ip + float2(1.0, 1.0)), fp - float2(1.0, 1.0));

    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y);
}

// What one variant's grain multiplies its colour by — about 1, so graining never darkens overall.
//
// Laid on from three sides and blended by which way the surface faces. Laid only from above, the
// grain would smear into long streaks down a cliff, which is exactly where it is most visible.
float LandscapeGrain(float3 worldPos, float3 n, float size, float strength)
{
    if (size <= 0.0 || strength <= 0.0) return 1.0;

    float3 w = abs(n);
    w = w * w * w * w;
    w /= max(w.x + w.y + w.z, 1e-5);

    float noise = LandscapeGradientNoise(worldPos.zy / size) * w.x
                + LandscapeGradientNoise(worldPos.xz / size) * w.y
                + LandscapeGradientNoise(worldPos.xy / size) * w.z;

    return max(0.0, 1.0 + noise * 2.0 * strength);
}

// 0 below the threshold, 1 above it, blended across Softness × threshold either side.
float LandscapeSplit(float value, float threshold)
{
    float band = abs(threshold) * max(_LandscapeSoftness, 0.0);
    if (band <= 1e-5) return value > threshold ? 1.0 : 0.0;
    return smoothstep(threshold - band, threshold + band, value);
}

// How much of a pixel one part hands to each of the four variants — all of its weight to the one
// it wears, nothing to the others. Parts are added up this way before any colour is worked out,
// so a variant's grain costs the same whether one part wears it or all three do.
float4 LandscapeShare(float variant, float weight)
{
    int v = (int)(variant + 0.5);
    return float4(v == 0 ? weight : 0.0, v == 1 ? weight : 0.0,
                  v == 2 ? weight : 0.0, v == 3 ? weight : 0.0);
}

// One designation's three parts, each handing its weight to the variant it wears.
float4 LandscapePartShares(float4 variants, float wHole, float wUp, float wDown)
{
    return LandscapeShare(variants.x, wHole)
         + LandscapeShare(variants.y, wUp)
         + LandscapeShare(variants.z, wDown);
}

// How much of a point each designation has: x Landscape (unpainted), y Red, z Blue, w Yellow,
// adding up to 1.
//
// The painted cells are read smoothly between their centres — Catmull-Rom, the height map's own
// curve — so a colour's edge runs in curves rather than round the cells. Paint Softness then
// keeps only what is within that much of the strongest designation here: at 0 one designation
// takes all of every pixel, a crisp line; at 1 they fade into each other across the cells.
float4 LandscapePaintWeights(float3 worldPos)
{
    if (_LandscapePaintSize.z < 0.5) return float4(1.0, 0.0, 0.0, 0.0);

    int2   cells = (int2)(_LandscapePaintSize.xy + 0.5);
    float2 uv    = (worldPos.xz - _LandscapePaintArea.xy) * _LandscapePaintArea.zw * _LandscapePaintSize.xy - 0.5;
    float2 i     = floor(uv);
    float2 t     = uv - i;

    // Catmull-Rom's four weights along each axis, for the cells at -1, 0, +1 and +2.
    float2 t2 = t * t, t3 = t2 * t;
    float2 w0 = 0.5 * (-t + 2.0 * t2 - t3);
    float2 w1 = 0.5 * (2.0 - 5.0 * t2 + 3.0 * t3);
    float2 w2 = 0.5 * (t + 4.0 * t2 - 3.0 * t3);
    float2 w3 = 0.5 * (-t2 + t3);
    float wx[4] = { w0.x, w1.x, w2.x, w3.x };
    float wz[4] = { w0.y, w1.y, w2.y, w3.y };

    float3 painted = float3(0.0, 0.0, 0.0);
    [unroll]
    for (int z = 0; z < 4; z++)
    {
        [unroll]
        for (int x = 0; x < 4; x++)
        {
            int2 c = clamp((int2)i + int2(x - 1, z - 1), int2(0, 0), cells - 1);
            painted += wx[x] * wz[z] * LOAD_TEXTURE2D(_LandscapePaintMap, c).rgb;
        }
    }

    painted = saturate(painted);
    float4 w = float4(saturate(1.0 - painted.r - painted.g - painted.b), painted);

    float top  = max(max(w.x, w.y), max(w.z, w.w));
    float band = max(_LandscapePaintSoftness, 1e-3);
    w = max(w - (top - band), 0.0);
    return w / max(w.x + w.y + w.z + w.w, 1e-5);
}

// The grained colour of variant 0 (A), 1 (B), 2 (C) or 3 (D), laid over the base colour by its
// alpha. The grain is the variant's alone; the base underneath stays clean.
float3 LandscapeVariant(float variant, float3 worldPos, float3 n)
{
    int v = (int)(variant + 0.5);

    float4 colour   = v == 1 ? _LandscapeColourB : v == 2 ? _LandscapeColourC : v == 3 ? _LandscapeColourD : _LandscapeColourA;
    float  size     = v == 1 ? _LandscapeGrainSizes.y     : v == 2 ? _LandscapeGrainSizes.z     : v == 3 ? _LandscapeGrainSizes.w     : _LandscapeGrainSizes.x;
    float  strength = v == 1 ? _LandscapeGrainStrengths.y : v == 2 ? _LandscapeGrainStrengths.z : v == 3 ? _LandscapeGrainStrengths.w : _LandscapeGrainStrengths.x;

    return lerp(_LandscapeBaseColour.rgb, colour.rgb * LandscapeGrain(worldPos, n, size, strength),
                saturate(colour.a));
}

// Which side of the rocky noise this pixel is on: 1 fully up, 0 fully down, blended across
// Noise Softness either side of the middle. At 0 softness it is the bare sign of the lean.
float LandscapeNoiseUp(float lean)
{
    float band = max(_LandscapeNoiseSoftness, 0.0);
    if (band <= 1e-5) return lean > 0.0 ? 1.0 : 0.0;
    return smoothstep(-band, band, lean);
}

// ── The decals ─────────────────────────────────────────────────────
//
// Hand-drawn marks stamped over the landscape, the spikes and the trees — a simpler copy of the
// river runs' decal groups (RiverRunShading.hlsl), kept entirely apart from them: no sort order,
// nothing above the lighting. Each group is laid onto the ground by its Blend Mode, the later
// group on top, under the light.
//
// Every drawing in the landscape decal folder is gathered onto ONE sheet of equal cells, because
// the drawing is picked per pixel — a texture per drawing would mean every one bound and read.
// The groups are scattered (or tiled) on all three of the world's planes and mixed by how much
// the surface faces each, so a decal carries over a hillside or round a tree arm without a cut.
//
// Which groups draw where is the designation: Landscape, Spikes or Trees, a bit per group each.
// The trees share the landscape's material, so they say which they are in UV2.x (1 a tree), set
// by NomKabarTree.Build; the tiles carry no UV2 and read 0, the landscape.

TEXTURE2D(_LandscapeDecalSheet);
SAMPLER(sampler_LandscapeDecalSheet);

// x columns, y rows, z how many cells hold a drawing, w pixels down one cell. z at zero is what
// keeps the decals inert before a sheet has been pushed.
float4 _LandscapeDecalLayout;

// Must match LandscapeShadingSettings.MaxDecalGroups and MaxDecalPicks.
#define LANDSCAPE_DECAL_GROUPS 8
#define LANDSCAPE_DECAL_PICKS  64

// x Spacing, y and z the smallest and largest Scale, w Amount.
float4 _LandscapeDecalGroupScatter[LANDSCAPE_DECAL_GROUPS];

// x Opacity, y the share given a random quarter turn, z the first of the group's picks, w how many.
float4 _LandscapeDecalGroupLook[LANDSCAPE_DECAL_GROUPS];

// x Tiled (1 on). y the Blend Tiles noise scale in metres, 0 off. z its softness. w the seed.
float4 _LandscapeDecalGroupTiling[LANDSCAPE_DECAL_GROUPS];

// Each group's Blend Mode, by the number of DecalBlendMode in RiverRunShadingSettings — the same
// list the runs use. Add new modes at the end there and here together.
float _LandscapeDecalGroupBlend[LANDSCAPE_DECAL_GROUPS];

#define LANDSCAPE_DECAL_BLEND_NORMAL    0
#define LANDSCAPE_DECAL_BLEND_MULTIPLY  1
#define LANDSCAPE_DECAL_BLEND_SCREEN    2
#define LANDSCAPE_DECAL_BLEND_ADD       3
#define LANDSCAPE_DECAL_BLEND_SUBTRACT  4
#define LANDSCAPE_DECAL_BLEND_OVERLAY   5
#define LANDSCAPE_DECAL_BLEND_SOFTLIGHT 6

// Which groups each designation takes, one bit per group as a whole number in a float.
// x Landscape, y Spikes, z Trees.
float4 _LandscapeDecalDesignations;

// The same for the painted designations: x Red, y Blue, z Yellow.
float4 _LandscapeDecalPaintDesignations;

// Whether bit g of a designation's mask is set — read by arithmetic, which every target answers
// the same.
float LandscapeDesignationBit(float mask, int g)
{
    return fmod(floor(mask / exp2((float)g)), 2.0);
}

// How much of a pixel group g draws on: each designation's weight here, if it ticks the group.
// masks and weights pair up component by component.
float LandscapeGroupWeight(float4 masks, float4 weights, int g)
{
    return weights.x * LandscapeDesignationBit(masks.x, g)
         + weights.y * LandscapeDesignationBit(masks.y, g)
         + weights.z * LandscapeDesignationBit(masks.z, g)
         + weights.w * LandscapeDesignationBit(masks.w, g);
}

// The sheet cells every group picks among, each group's a run of this list.
float _LandscapeDecalPicks[LANDSCAPE_DECAL_PICKS];

// The transparent pixels round each drawing on the sheet. Must match the library's Padding.
#define LANDSCAPE_DECAL_PADDING 2.0

// How far blended tiles' borders can wander off the cell edge, as a share of a tile — the same
// fixed number as the runs'.
#define LANDSCAPE_DECAL_TILE_WANDER 0.5

// How hard the three planes' mix is pulled toward whichever the surface faces most, and the share
// under which a plane is dropped altogether — the runs' numbers, for the runs' reasons.
#define LANDSCAPE_DECAL_SHARPEN     4.0
#define LANDSCAPE_DECAL_PLANE_FLOOR 0.02

// Four independent numbers from one grid cell, by arithmetic alone, so every machine agrees where
// the decals stand. x whether the place is taken, yz where in its cell, w how big.
float4 LandscapeDecalHash(float2 c)
{
    float4 p = frac(c.xyxy * float4(0.1031, 0.1030, 0.0973, 0.1099));
    p += dot(p, p.wzxy + 33.33);
    return frac((p.xxyz + p.yzzw) * p.zywx);
}

// One group's decals at one point on ONE of the three planes: rgb already times its coverage,
// a the coverage. uv is the world position flattened onto the plane, in metres; footprint how
// much of it one screen pixel covers, taken by the caller before anything was skipped.
float4 LandscapeDecals(float2 uv, float footprint, float4 scatter, float4 look, float4 tiling)
{
    float4 nothing = float4(0.0, 0.0, 0.0, 0.0);

    float2 grid    = _LandscapeDecalLayout.xy;
    int    held    = (int)(_LandscapeDecalLayout.z + 0.5);
    float  cellPix = _LandscapeDecalLayout.w;

    float spacing   = scatter.x;
    float amount    = scatter.w;
    float largest   = max(scatter.y, scatter.z);
    float small     = min(scatter.y, scatter.z);
    float rotation  = look.y;
    float firstPick = look.z;
    float pickCount = look.w;

    // Tiled: every place taken and every decal exactly as wide as its cell.
    bool tile = tiling.x > 0.5;
    if (tile) { amount = 1.0; small = spacing; largest = spacing; }

    if (held <= 0 || grid.x < 1.0 || grid.y < 1.0 || cellPix < 1.0) return nothing;
    if (spacing <= 0.0 || amount <= 0.0 || largest <= 0.0)          return nothing;
    if (pickCount < 0.5)                                            return nothing;

    float2 shift = tiling.w * float2(37.0, 59.0);

    // The rings of neighbouring cells whose decals could reach this pixel, from the group's
    // uniforms only, held to three. A tile never leaves its own cell.
    int rings = tile ? 0 : clamp(1 + (int)floor(largest / (2.0 * spacing)), 1, 3);

    float2 home = floor(uv / spacing);
    int    lo   = -rings;
    int    hi   = rings;

    // Blend Tiles: which tile shows is decided at a position pushed about by gradient noise, and
    // the four tiles round the nearest corner of it share the pixel. Each is still read at this
    // pixel's own place in its grid, so the drawings need to tile.
    bool   blendOn = tile && tiling.y > 0.0;
    float2 blend   = float2(0.0, 0.0);
    if (blendOn)
    {
        float2 n = uv / tiling.y + shift;
        float2 push = float2(LandscapeGradientNoise(n),
                             LandscapeGradientNoise(n + float2(17.31, -41.7)));
        float2 q = uv / spacing + push * LANDSCAPE_DECAL_TILE_WANDER - 0.5;

        float soft = max(saturate(tiling.z) * 0.5, 1e-3);
        blend = smoothstep(0.5 - soft, 0.5 + soft, frac(q));
        home  = floor(q);
        lo    = 0;
        hi    = 1;
    }

    float4 over = nothing;

    for (int y = lo; y <= hi; y++)
    {
        for (int x = lo; x <= hi; x++)
        {
            float2 c = home + float2(x, y);
            float4 h = LandscapeDecalHash(c + shift);

            if (!tile && h.x > amount) continue;

            float size = lerp(small, largest, h.w);
            if (size <= 1e-5) continue;

            // Where this pixel falls across the decal, 0 to 1 either way.
            float2 local;
            float  share = 1.0;
            if (blendOn)
            {
                share = (x == 0 ? 1.0 - blend.x : blend.x) * (y == 0 ? 1.0 - blend.y : blend.y);
                if (share < 1e-4) continue;
                local = frac(uv / spacing);
            }
            else if (tile)
            {
                local = saturate(uv / spacing - c);
            }
            else
            {
                float2 centre = (c + h.yz) * spacing;
                local = (uv - centre) / size + 0.5;
                if (any(local < 0.0) || any(local > 1.0)) continue;
            }

            // Which of the group's drawings this place drew, and its cell on the sheet.
            float4 h2  = LandscapeDecalHash(c + shift + 19.19);
            int   at   = (int)(firstPick + min(floor(h2.x * pickCount), pickCount - 1.0));
            float slot = min(_LandscapeDecalPicks[clamp(at, 0, LANDSCAPE_DECAL_PICKS - 1)],
                             (float)held - 1.0);

            // A quarter turn for the share Rotation Amount asks for, by 90, 180 or 270.
            if (h2.y < rotation)
            {
                int turns = min(1 + (int)floor(h2.z * 3.0), 3);
                local = turns == 1 ? float2(local.y, 1.0 - local.x)
                      : turns == 2 ? 1.0 - local
                                   : float2(1.0 - local.y, local.x);
            }
            float2 cell = float2(fmod(slot, grid.x), floor(slot / grid.x));

            // An EXPLICIT mip, which is what makes a read inside all the skipping above legal.
            float lod = log2(max(footprint * cellPix / size, 1e-5));
            lod = clamp(lod, 0.0, log2(cellPix) - 1.0);

            // A tile reads only its drawing, never the transparent padding round it, or every
            // join shows as a hairline.
            if (tile)
            {
                lod = clamp(lod, 0.0, max(log2(cellPix) - 3.0, 0.0));
                float texel = exp2(ceil(lod));
                float inset = texel * (ceil(LANDSCAPE_DECAL_PADDING / texel) + 0.5);
                local = (inset + local * (cellPix - 2.0 * inset)) / cellPix;
            }

            float4 mark = SAMPLE_TEXTURE2D_LOD(_LandscapeDecalSheet, sampler_LandscapeDecalSheet,
                                               (cell + local) / grid, lod);

            // Blended tiles are shared out, not stacked.
            if (blendOn)
            {
                over += share * float4(mark.rgb * mark.a, mark.a);
                continue;
            }

            over.rgb = mark.rgb * mark.a + over.rgb * (1.0 - mark.a);
            over.a   = mark.a           + over.a   * (1.0 - mark.a);
        }
    }

    return over;
}

// One finished group, mixed as a single layer, laid onto the ground under it by the group's Blend
// Mode — the runs' RiverRunDecalBlendOnto. mixed is premultiplied, rgb already times its coverage
// in a, and each mode is faded in by that coverage, so where no decal is the ground is untouched.
float3 LandscapeDecalBlendOnto(float3 base, float4 mixed, int mode)
{
    float3 c = mixed.rgb;
    float  a = mixed.a;

    if (mode == LANDSCAPE_DECAL_BLEND_MULTIPLY) return base * (1.0 - a + c);
    if (mode == LANDSCAPE_DECAL_BLEND_SCREEN)   return base * (1.0 - c) + c;
    if (mode == LANDSCAPE_DECAL_BLEND_ADD)      return base + c;
    if (mode == LANDSCAPE_DECAL_BLEND_SUBTRACT) return max(base - c, 0.0);

    if (mode == LANDSCAPE_DECAL_BLEND_OVERLAY || mode == LANDSCAPE_DECAL_BLEND_SOFTLIGHT)
    {
        if (a < 1e-4) return base;
        float3 m = saturate(c / a);
        float3 b = saturate(base);

        float3 laid = mode == LANDSCAPE_DECAL_BLEND_OVERLAY
            ? lerp(2.0 * b * m, 1.0 - 2.0 * (1.0 - b) * (1.0 - m), step(0.5, b))
            : (1.0 - 2.0 * m) * b * b + 2.0 * m * b;
        return lerp(base, laid, a);
    }

    return c + base * (1.0 - a);
}

// Every group the designations' bits tick, laid onto the ground colour in list order by each
// one's Blend Mode, the later on top. masks are up to four designations' bits, weights how much of
// this pixel each has — a group fades with the share of the pixel whose designations tick it.
float3 LandscapeDecalsOnBlend(float3 ground, float3 worldPos, float3 n, float4 masks, float4 weights)
{
    // Every derivative taken up here, before a single plane or group has been skipped.
    float3 dx = ddx(worldPos);
    float3 dy = ddy(worldPos);

    float footXZ = max(length(float2(dx.x, dx.z)), length(float2(dy.x, dy.z)));
    float footZY = max(length(float2(dx.z, dx.y)), length(float2(dy.z, dy.y)));
    float footXY = max(length(float2(dx.x, dx.y)), length(float2(dy.x, dy.y)));

    float3 w = pow(max(abs(n), 1e-4), LANDSCAPE_DECAL_SHARPEN);
    w /= max(w.x + w.y + w.z, 1e-5);
    w  = step(LANDSCAPE_DECAL_PLANE_FLOOR, w) * w;
    w /= max(w.x + w.y + w.z, 1e-5);

    if (dot(step(0.5, masks), step(1e-5, weights)) < 0.5) return ground;

    [loop]
    for (int g = 0; g < LANDSCAPE_DECAL_GROUPS; g++)
    {
        float share = LandscapeGroupWeight(masks, weights, g);
        if (share <= 1e-5) continue;

        float4 scatter = _LandscapeDecalGroupScatter[g];
        float4 look    = _LandscapeDecalGroupLook[g];
        float4 tiling  = _LandscapeDecalGroupTiling[g];

        float4 mixed = float4(0.0, 0.0, 0.0, 0.0);
        if (w.y > 0.0) mixed += w.y * LandscapeDecals(worldPos.xz, footXZ, scatter, look, tiling);
        if (w.x > 0.0) mixed += w.x * LandscapeDecals(worldPos.zy, footZY, scatter, look, tiling);
        if (w.z > 0.0) mixed += w.z * LandscapeDecals(worldPos.xy, footXY, scatter, look, tiling);

        // Opacity once, on the finished mix, colour and coverage alike — and the share of the
        // pixel the group is drawn on.
        mixed *= saturate(look.x) * saturate(share);

        ground = LandscapeDecalBlendOnto(ground, mixed, (int)(_LandscapeDecalGroupBlend[g] + 0.5));
    }

    return ground;
}

// One designation's groups, all of the pixel — mask is one of _LandscapeDecalDesignations'
// components. The spikes and trees, which are never painted.
float3 LandscapeDecalsOnGround(float3 ground, float3 worldPos, float3 n, float mask)
{
    return LandscapeDecalsOnBlend(ground, worldPos, n, float4(mask, 0.0, 0.0, 0.0), float4(1.0, 0.0, 0.0, 0.0));
}

// ── The normal map decals ──────────────────────────────────────────
//
// Real normal maps on a sheet of their own, laid out like the decal sheet but LINEAR, so a texel
// reads back as the direction it was drawn as — the landscape's copy of the river runs' normal map
// decal groups (RiverRunNormalDecalsOnStone), kept apart from them. Always tiled. They paint
// nothing: they bend the normal the light, the boat's light and the River Glow are worked out from.
// Which groups bend where is the designation, exactly as for the decals.

TEXTURE2D(_LandscapeNormalDecalSheet);
SAMPLER(sampler_LandscapeNormalDecalSheet);

// x columns, y rows, z how many cells hold a normal map, w pixels down one cell. z at zero keeps
// an unpushed sheet from bending anything, as with the decal sheet.
float4 _LandscapeNormalDecalLayout;

// Must match LandscapeShadingSettings.MaxNormalDecalGroups and MaxNormalDecalPicks.
#define LANDSCAPE_NORMAL_DECAL_GROUPS 8
#define LANDSCAPE_NORMAL_DECAL_PICKS  64

// Each normal map group: x the tile width in metres, y Strength — 1 the relief as drawn — z the
// first of its picks, w how many.
float4 _LandscapeNormalDecalGroupTiles[LANDSCAPE_NORMAL_DECAL_GROUPS];

// x the Blend Tiles noise scale in metres, 0 off. y its softness. z the seed.
float4 _LandscapeNormalDecalGroupBlend[LANDSCAPE_NORMAL_DECAL_GROUPS];

float _LandscapeNormalDecalPicks[LANDSCAPE_NORMAL_DECAL_PICKS];

// Which normal map groups each designation takes, one bit per group. x Landscape, y Spikes, z Trees.
float4 _LandscapeNormalDecalDesignations;

// The same for the painted designations: x Red, y Blue, z Yellow.
float4 _LandscapeNormalDecalPaintDesignations;

// One normal map group's tiles at one point on ONE of the three planes: the tangent normal's
// xy, -1 to 1 — how far the drawn relief leans along the plane's two axes. Zero is flat.
//
// The same tiling as a Tiled decal group: the same cells, the same pick per cell, the same Blend
// Tiles wander — so a colour group and a normal map group on the same Spacing and Seed line up.
float2 LandscapeNormalDecalTiles(float2 uv, float footprint, float spacing, float firstPick,
                                 float pickCount, float seed, float2 tileBlend)
{
    float2 grid    = _LandscapeNormalDecalLayout.xy;
    int    held    = (int)(_LandscapeNormalDecalLayout.z + 0.5);
    float  cellPix = _LandscapeNormalDecalLayout.w;

    if (held <= 0 || grid.x < 1.0 || grid.y < 1.0 || cellPix < 1.0) return float2(0.0, 0.0);
    if (spacing <= 0.0 || pickCount < 0.5)                           return float2(0.0, 0.0);

    float2 shift = seed * float2(37.0, 59.0);
    float2 home  = floor(uv / spacing);
    int    hi    = 0;

    // Blend Tiles: exactly as LandscapeDecals does it — the choice of tile wanders, the reads
    // stay square on the grid.
    bool   blendOn = tileBlend.x > 0.0;
    float2 blend   = float2(0.0, 0.0);
    if (blendOn)
    {
        float2 n = uv / tileBlend.x + shift;
        float2 push = float2(LandscapeGradientNoise(n),
                             LandscapeGradientNoise(n + float2(17.31, -41.7)));
        float2 q = uv / spacing + push * LANDSCAPE_DECAL_TILE_WANDER - 0.5;

        float soft = max(saturate(tileBlend.y) * 0.5, 1e-3);
        blend = smoothstep(0.5 - soft, 0.5 + soft, frac(q));
        home  = floor(q);
        hi    = 1;
    }

    // The mip, and the inset that keeps every read clear of the padding round the cell — the
    // tile's own, from LandscapeDecals.
    float lod = log2(max(footprint * cellPix / spacing, 1e-5));
    lod = clamp(lod, 0.0, max(log2(cellPix) - 3.0, 0.0));
    float texel = exp2(ceil(lod));
    float inset = texel * (ceil(LANDSCAPE_DECAL_PADDING / texel) + 0.5);

    float2 lean = float2(0.0, 0.0);

    for (int y = 0; y <= hi; y++)
    {
        for (int x = 0; x <= hi; x++)
        {
            float2 c = home + float2(x, y);

            float  share = 1.0;
            float2 local;
            if (blendOn)
            {
                share = (x == 0 ? 1.0 - blend.x : blend.x) * (y == 0 ? 1.0 - blend.y : blend.y);
                if (share < 1e-4) continue;
                local = frac(uv / spacing);
            }
            else
            {
                local = saturate(uv / spacing - c);
            }

            float4 h2   = LandscapeDecalHash(c + shift + 19.19);
            int    at   = (int)(firstPick + min(floor(h2.x * pickCount), pickCount - 1.0));
            float  slot = min(_LandscapeNormalDecalPicks[clamp(at, 0, LANDSCAPE_NORMAL_DECAL_PICKS - 1)],
                              (float)held - 1.0);
            float2 cell = float2(fmod(slot, grid.x), floor(slot / grid.x));

            local = (inset + local * (cellPix - 2.0 * inset)) / cellPix;
            float3 drawn = SAMPLE_TEXTURE2D_LOD(_LandscapeNormalDecalSheet, sampler_LandscapeNormalDecalSheet,
                                                (cell + local) / grid, lod).rgb;

            // Shared out like blended colour tiles, so where two fade into each other the lean is
            // the mix of the two rather than the sum.
            lean += share * (drawn.xy * 2.0 - 1.0);
        }
    }

    return lean;
}

// The ground's normal, bent by the normal map groups the designations' bits tick. n is the
// surface's own normal, normalised; masks and weights as for LandscapeDecalsOnBlend — a group
// bends by the share of the pixel whose designations tick it.
//
// Laid on the same three planes as the decals and mixed by the same weights. Each plane's lean is
// along that plane's own two world axes and simply added to the normal — so it needs no tangents,
// since the relief is read off the world rather than off a UV that flips.
float3 LandscapeNormalDecalsOnBlend(float3 worldPos, float3 n, float4 masks, float4 weights)
{
    // Every derivative taken up here, before a single plane or group has been skipped.
    float3 dx = ddx(worldPos);
    float3 dy = ddy(worldPos);

    float footXZ = max(length(float2(dx.x, dx.z)), length(float2(dy.x, dy.z)));
    float footZY = max(length(float2(dx.z, dx.y)), length(float2(dy.z, dy.y)));
    float footXY = max(length(float2(dx.x, dx.y)), length(float2(dy.x, dy.y)));

    float3 w = pow(max(abs(n), 1e-4), LANDSCAPE_DECAL_SHARPEN);
    w /= max(w.x + w.y + w.z, 1e-5);
    w  = step(LANDSCAPE_DECAL_PLANE_FLOOR, w) * w;
    w /= max(w.x + w.y + w.z, 1e-5);

    if (dot(step(0.5, masks), step(1e-5, weights)) < 0.5) return n;

    float3 bend = float3(0.0, 0.0, 0.0);

    [loop]
    for (int g = 0; g < LANDSCAPE_NORMAL_DECAL_GROUPS; g++)
    {
        float share = LandscapeGroupWeight(masks, weights, g);
        if (share <= 1e-5) continue;

        float4 tiles     = _LandscapeNormalDecalGroupTiles[g];
        float4 blend     = _LandscapeNormalDecalGroupBlend[g];
        float  spacing   = tiles.x;
        float  strength  = tiles.y * saturate(share);
        float2 tileBlend = blend.xy;
        float  seed      = blend.z;

        if (strength <= 0.0) continue;

        // The planes as the decals flatten them: up-facing reads x and z, x-facing z and y,
        // z-facing x and y — each lean put back along the two world axes it was read across.
        float2 t;
        if (w.y > 0.0)
        {
            t = LandscapeNormalDecalTiles(worldPos.xz, footXZ, spacing, tiles.z, tiles.w, seed, tileBlend);
            bend += w.y * strength * float3(t.x, 0.0, t.y);
        }
        if (w.x > 0.0)
        {
            t = LandscapeNormalDecalTiles(worldPos.zy, footZY, spacing, tiles.z, tiles.w, seed, tileBlend);
            bend += w.x * strength * float3(0.0, t.y, t.x);
        }
        if (w.z > 0.0)
        {
            t = LandscapeNormalDecalTiles(worldPos.xy, footXY, spacing, tiles.z, tiles.w, seed, tileBlend);
            bend += w.z * strength * float3(t.x, t.y, 0.0);
        }
    }

    float3 bent = n + bend;
    return dot(bent, bent) > 1e-8 ? normalize(bent) : n;
}

// One designation's normal map groups, all of the pixel — the spikes and trees.
float3 LandscapeNormalDecalsOnGround(float3 worldPos, float3 n, float mask)
{
    return LandscapeNormalDecalsOnBlend(worldPos, n, float4(mask, 0.0, 0.0, 0.0), float4(1.0, 0.0, 0.0, 0.0));
}

// Half lambert toward the world's light. Strength lerps out of it, so 0 is flat and unlit;
// past 1 the lit result is multiplied, up to 10x brighter. Shared with the spikes, which stand
// in the landscape and so are lit the same way.
float LandscapeLight(float3 n, float3 worldPos)
{
    float3 toLight = _LevelSelectLightPosition.xyz - worldPos;
    toLight = dot(toLight, toLight) > 1e-8 ? normalize(toLight) : float3(0.0, 1.0, 0.0);

    float ndl = dot(n, toLight) * 0.5 + 0.5;
    return lerp(1.0, saturate(ndl), saturate(_LevelSelectLightStrength))
         * max(_LevelSelectLightStrength, 1.0);
}

// Normal : the smooth hill normal, from CalculateHills' Normal output. The mesh's own normal is
//          flat — the hills are raised in the shader — so it has to come in from there. Nothing
//          is judged by steepness any more, but the grain is still laid along it.
// Noise  : CalculateHills' Noise output — x which way the rocky noise leans here (-1 down to
//          +1 up). The y, how much noise there is here at all, is left on the wire but unused:
//          with no Ground or Tops behind it there is nothing for unnoisy ground to fall back to.
// UV2    : UV channel 2, read inside the subgraph. x is 1 on a NomKabar tree, which shares this
//          material; the tiles carry none and read 0 — the landscape. Picks which designation's
//          decal groups draw here. A tree is never painted; the landscape takes the painted
//          Texture map's designations.
// Colour : the landscape, each part in its variant, grained, decaled and lit.
// BentNormal : Normal bent by the normal map decal groups — the one the light is worked out from,
//          handed out for the River Glow.
void LandscapeShading_float(float3 Normal, float3 WorldPos, float2 Noise, float4 UV2,
                            out float3 Colour, out float3 BentNormal)
{
    float3 n = normalize(Normal);

    // Which part this pixel is.
    float height = WorldPos.y - _LandscapeBaseY;

    float hole = LandscapeSplit(-height, _LandscapeHoleDepth);
    float up   = LandscapeNoiseUp(Noise.x);

    // Holes take the pixel first; all the rest is one side of the noise or the other.
    float wHole = hole;
    float wUp   = (1.0 - hole) * up;
    float wDown = (1.0 - hole) * (1.0 - up);

    // Which designations this pixel is — x Landscape, y Red, z Blue, w Yellow — each wearing its
    // own parts' variants by its share.
    bool   tree  = UV2.x > 0.5;
    float4 paint = tree ? float4(1.0, 0.0, 0.0, 0.0) : LandscapePaintWeights(WorldPos);

    float4 share = paint.x * LandscapePartShares(_LandscapePartVariants,       wHole, wUp, wDown)
                 + paint.y * LandscapePartShares(_LandscapeRedPartVariants,    wHole, wUp, wDown)
                 + paint.z * LandscapePartShares(_LandscapeBluePartVariants,   wHole, wUp, wDown)
                 + paint.w * LandscapePartShares(_LandscapeYellowPartVariants, wHole, wUp, wDown);

    float3 stone = LandscapeVariant(0.0, WorldPos, n) * share.x
                 + LandscapeVariant(1.0, WorldPos, n) * share.y
                 + LandscapeVariant(2.0, WorldPos, n) * share.z
                 + LandscapeVariant(3.0, WorldPos, n) * share.w;

    // The decals go on the stone, under the light. A tree takes the Trees groups, the rest the
    // Landscape and painted designations' ones, each by its share of the pixel.
    float4 decalMasks = tree
        ? float4(_LandscapeDecalDesignations.z, 0.0, 0.0, 0.0)
        : float4(_LandscapeDecalDesignations.x, _LandscapeDecalPaintDesignations.xyz);
    stone = LandscapeDecalsOnBlend(stone, WorldPos, n, decalMasks, paint);

    // The normal bent by the designation's normal map groups. Only the lighting reads it: the
    // grain and the decals go on being laid along the surface's own n, so the relief changes how
    // the ground is lit and nothing about where things sit on it.
    float4 bendMasks = tree
        ? float4(_LandscapeNormalDecalDesignations.z, 0.0, 0.0, 0.0)
        : float4(_LandscapeNormalDecalDesignations.x, _LandscapeNormalDecalPaintDesignations.xyz);
    BentNormal = LandscapeNormalDecalsOnBlend(WorldPos, n, bendMasks, paint);

    Colour = stone * LandscapeLight(BentNormal, WorldPos);

    // The boat lights the hills only — a tree shares this material but keeps out of it.
    if (UV2.x <= 0.5)
        Colour = LevelSelectBoatLightOnto(Colour, BentNormal, WorldPos, 1.0);
}

void LandscapeShading_half(half3 Normal, half3 WorldPos, half2 Noise, half4 UV2,
                           out half3 Colour, out half3 BentNormal)
{
    float3 colour, bent;
    LandscapeShading_float(Normal, WorldPos, Noise, UV2, colour, bent);
    Colour     = colour;
    BentNormal = bent;
}

#endif
