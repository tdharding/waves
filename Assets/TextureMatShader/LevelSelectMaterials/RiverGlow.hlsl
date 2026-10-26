// The level select's baked river glow — the light the river water throws onto everything around
// it, the walls blocking it.
//
// Bake River Glow (Lighting window) fills the air around the water with sample points, in
// bricks of 8×8×8, and keeps at each one how much glow reaches it and which way it mostly comes
// from. There are two sets: near points, close together, over the channel, its rims and inner
// banks; and, for a long Glow Reach, far points, wider apart, out to the reach. Here a pixel looks
// for a near brick first and a far one if there is none, reads the glow at its place in that
// brick from the atlas, and turns it against its own normal: a face looking at the water gets the
// most, one looking away gets none.
//
// Drop the RiverGlow subgraph between a graph's colour and its output (after Level Select Shadow,
// so the glow still lights what the light leaves in shadow): Colour in, the same colour out,
// brightened towards white where the river lights it. The water itself is not drawn with it.
//
// The soul path glow is added here too, so every graph with this node gets it with no rewiring:
// baked round each soul route's band, it comes up behind the band's front as the band draws out.
//
// The layout must agree with RiverGlowBaker: bricks of RIVERGLOW_BRICK points spanning
// RIVERGLOW_SPAN steps, neighbouring bricks sharing their faces' points.

#ifndef RIVER_GLOW_INCLUDED
#define RIVER_GLOW_INCLUDED

// Every brick of both sets, packed together: xyz the way the glow mostly comes from (weighted by
// how much comes), w how much comes in all.
TEXTURE3D(_RiverGlowAtlas);
SAMPLER(sampler_RiverGlowAtlas);

// One cell per brick over the world, for each set: rgb where the brick sits in the atlas (in
// bricks, as bytes), a 1 where there is a brick, 0 where there is none.
TEXTURE3D(_RiverGlowBricks);
TEXTURE3D(_RiverGlowFarBricks);

// Near points: xyz the grid's corner in the world; w the spacing between points — 0 when nothing
// is baked, which leaves everything as it is.
float4 _RiverGlowOrigin;

// Near points: xyz the brick grid's size in cells; w the Glow Strength.
float4 _RiverGlowGrid;

// Far points, the same — w of the origin 0 when there are none.
float4 _RiverGlowFarOrigin;
float4 _RiverGlowFarGrid;

// xyz the atlas's size in texels.
float4 _RiverGlowAtlasSize;

// The soul path glow (Bake Soul Path Glow, Level Select River Tuner): the light each soul route's
// band throws around it, in one set of points laid out like the near points above. The atlas is
// the same as the river's (xyz the way it comes from, w how much); beside it, point by point, which
// route lights it (x, its slot), how far along that route the light comes from (y the middle, z
// the spread) and how much (w).
TEXTURE3D(_SoulGlowAtlas);
SAMPLER(sampler_SoulGlowAtlas);
TEXTURE3D(_SoulGlowRoutes);
TEXTURE3D(_SoulGlowBricks);

// xyz the grid's corner, w the point spacing — 0 when nothing is baked.
float4 _SoulGlowOrigin;
// xyz the brick grid's size in cells.
float4 _SoulGlowGrid;
// xyz the atlas's size in texels.
float4 _SoulGlowAtlasSize;

// Glow Strength, and Reveal Softness in metres — the river water preset's soul zone, live.
float _SoulGlowStrength;
float _SoulGlowSoftness;

// How far along each route its band has drawn out, world units, by the slot the bake gave it
// (SoulPathGlow.cs — MaxRoutes there must match).
#define SOULGLOW_MAX_ROUTES 32
float _SoulGlowFronts[SOULGLOW_MAX_ROUTES];

#define RIVERGLOW_BRICK 8.0
#define RIVERGLOW_SPAN  7.0

// How far, in near-point spacings, a surface is lifted off itself along its normal before it reads
// the glow. Points inside walls and the ground are filled from their neighbours at the bake, so
// this only has to clear the surface itself.
#define RIVERGLOW_LIFT 1.0

// Where in the atlas a place falls, in one set of points: false if that set has no brick there.
bool RiverGlow_Find(float3 p, float4 origin, float4 brick, out float3 texel)
{
    texel = 0.0;
    if (brick.a < 0.5) return false;

    float3 g     = (p - origin.xyz) / origin.w;
    float3 cell  = floor(g / RIVERGLOW_SPAN);
    // Place within the brick, 0..7 steps, onto the texel centres of that brick in the atlas.
    float3 local = clamp(g - cell * RIVERGLOW_SPAN, 0.0, RIVERGLOW_SPAN);
    texel = round(brick.rgb * 255.0) * RIVERGLOW_BRICK + 0.5 + local;
    return true;
}

// The brick cell a place falls in, in one set of points; inGrid false outside its grid (or when
// the set is empty), and the cell then (0, 0, 0) so the lookup stays in range.
int3 RiverGlow_Cell(float3 p, float4 origin, float3 gridSize, out bool inGrid)
{
    float3 cell = floor((p - origin.xyz) / max(origin.w, 1e-5) / RIVERGLOW_SPAN);
    inGrid = origin.w > 0.0 && all(cell >= 0.0) && all(cell < gridSize);
    return inGrid ? int3(cell) : int3(0, 0, 0);
}

// The river's glow on a face with normal n, times the Glow Strength.
float RiverGlow_River(float3 WorldPos, float3 n)
{
    float spacing  = _RiverGlowOrigin.w;
    float strength = _RiverGlowGrid.w;
    if (spacing <= 0.0 || strength <= 0.0) return 0.0;

    float3 p = WorldPos + n * spacing * RIVERGLOW_LIFT;

    float3 texel;
    bool   inGrid;
    int3   cell  = RiverGlow_Cell(p, _RiverGlowOrigin, _RiverGlowGrid.xyz, inGrid);
    float4 brick = inGrid ? LOAD_TEXTURE3D(_RiverGlowBricks, cell) : float4(0.0, 0.0, 0.0, 0.0);
    if (!RiverGlow_Find(p, _RiverGlowOrigin, brick, texel))
    {
        cell  = RiverGlow_Cell(p, _RiverGlowFarOrigin, _RiverGlowFarGrid.xyz, inGrid);
        brick = inGrid ? LOAD_TEXTURE3D(_RiverGlowFarBricks, cell) : float4(0.0, 0.0, 0.0, 0.0);
        if (!RiverGlow_Find(p, _RiverGlowFarOrigin, brick, texel)) return 0.0;
    }

    float4 glow = SAMPLE_TEXTURE3D_LOD(_RiverGlowAtlas, sampler_RiverGlowAtlas,
                                       texel / _RiverGlowAtlasSize.xyz, 0);

    // How much reaches a face with this normal: all-round glow plus what comes from its side,
    // never below nothing.
    float lit = max(0.0, 0.25 * glow.w + 0.5 * dot(n, glow.xyz));
    return lit * strength;
}

// The soul path glow on a face with normal n, times its Glow Strength, and only as much of it as
// has come up behind its route's band front.
float RiverGlow_Soul(float3 WorldPos, float3 n)
{
    float spacing  = _SoulGlowOrigin.w;
    float strength = _SoulGlowStrength;
    if (spacing <= 0.0 || strength <= 0.0) return 0.0;

    float3 p = WorldPos + n * spacing * RIVERGLOW_LIFT;

    float3 texel;
    bool   inGrid;
    int3   cell  = RiverGlow_Cell(p, _SoulGlowOrigin, _SoulGlowGrid.xyz, inGrid);
    float4 brick = inGrid ? LOAD_TEXTURE3D(_SoulGlowBricks, cell) : float4(0.0, 0.0, 0.0, 0.0);
    if (!RiverGlow_Find(p, _SoulGlowOrigin, brick, texel)) return 0.0;

    float4 glow = SAMPLE_TEXTURE3D_LOD(_SoulGlowAtlas, sampler_SoulGlowAtlas,
                                       texel / _SoulGlowAtlasSize.xyz, 0);
    float lit = max(0.0, 0.25 * glow.w + 0.5 * dot(n, glow.xyz));
    if (lit <= 0.0) return 0.0;

    // How much of it has come up: each of the eight points round this place is lit by one route,
    // from about so far along it (give or take its spread), and comes up as that route's front
    // passes, over Reveal Softness more. Blended between the eight by how much each holds, so a
    // point with no glow says nothing. Read point by point — a route's slot cannot be blended.
    float3 brickStart = round(brick.rgb * 255.0) * RIVERGLOW_BRICK;
    float3 corner     = texel - 0.5;
    float3 base       = min(floor(corner), brickStart + (RIVERGLOW_SPAN - 1.0));
    float3 f          = saturate(corner - base);
    float  softness   = max(_SoulGlowSoftness, 0.001);

    float revealed = 0.0;
    float weight   = 0.0;
    [unroll]
    for (int c = 0; c < 8; c++)
    {
        float3 o     = float3(c & 1, (c >> 1) & 1, (c >> 2) & 1);
        float4 route = LOAD_TEXTURE3D(_SoulGlowRoutes, int3(base + o));
        float3 t     = lerp(1.0 - f, f, o);
        float  w     = t.x * t.y * t.z * route.w;

        int   slot  = clamp((int)round(route.x), 0, SOULGLOW_MAX_ROUTES - 1);
        float front = _SoulGlowFronts[slot];
        revealed += w * smoothstep(route.y - route.z, route.y + route.z + softness, front);
        weight   += w;
    }
    revealed = weight > 0.0 ? revealed / weight : 0.0;

    return lit * strength * revealed;
}

void RiverGlow_float(float3 Colour, float3 Normal, float3 WorldPos, out float3 Out)
{
    Out = Colour;

    float3 n      = normalize(Normal);
    float  amount = saturate(RiverGlow_River(WorldPos, n) + RiverGlow_Soul(WorldPos, n));
    if (amount <= 0.0) return;

    Out = 1.0 - (1.0 - Colour) * (1.0 - amount);
}

void RiverGlow_half(half3 Colour, half3 Normal, half3 WorldPos, out half3 Out)
{
    float3 colour;
    RiverGlow_float(Colour, Normal, WorldPos, colour);
    Out = colour;
}

#endif
