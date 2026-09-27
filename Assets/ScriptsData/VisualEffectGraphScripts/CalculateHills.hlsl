// Included by the hill generator's vertex stage AND by the RiverEdgeCut subgraph in the same
// graph — Shader Graph normally includes a file once, the guard makes sure of it.
#ifndef CALCULATE_HILLS_INCLUDED
#define CALCULATE_HILLS_INCLUDED

uniform float4 _HillPositions[100];
uniform float  _HillSharpness[100];   // 0 = smooth (default), 1 = near-vertical
uniform float  _HillNoise[100];       // per-point rocky-noise amount (0 = none)
uniform float  _HillNoiseScale;       // global noise frequency

// ── River edges ──────────────────────────────────────────────────────────────
// Where the level select's river runs and pools stand, sent by LandscapeTool (filled in by the
// Level Select Designer). Near a run whose path has Landscape Influence on, the ground is drawn
// up to just under its rim top, easing back to the hills over the path's Influence Distance.
// Inside EVERY run and pool the landscape is cut away (RiverEdgeCut), so the ground meets the
// rim exactly however coarse the tile grid is.
//
// Mirrored line for line by LandscapeHillHeight.RiverEdgeLift — change one, change both.
#define RIVER_EDGE_MAX        256
#define RIVER_EDGE_UNDER_RIM  0.02   // lifted ground sits this far below the rim top
#define RIVER_EDGE_TUCK       0.05   // and reaches this far in under the rim before it is cut

uniform float4 _RiverEdgePoints[RIVER_EDGE_MAX]; // xyz centreline at the rim top, w half outer width (negative: line ends here)
uniform float  _RiverEdgeReach[RIVER_EDGE_MAX];  // Influence Distance; negative = cut only, no lift
uniform float  _RiverEdgeCount;

// How far outside the run between a and b the point is (negative inside), and how far along
// a to b its nearest point lies. a == b is a disc — a pool, or the end of a line.
float _RiverEdgeOutside(float4 a, float4 b, float2 xz, out float t)
{
    float2 ab   = b.xz - a.xz;
    float  len2 = dot(ab, ab);
    t = len2 > 1e-8 ? saturate(dot(xz - a.xz, ab) / len2) : 0.0;

    float2 c    = a.xz + ab * t;
    float  half_ = lerp(abs(a.w), abs(b.w), t);
    return length(xz - c) - half_;
}

// How much the river edges take the ground over here, 0..1, and the rim height of the one that
// holds it most.
float _RiverEdgeLift(float2 xz, out float rimY)
{
    rimY = 0.0;
    float best  = 0.0;
    int   count = min((int)_RiverEdgeCount, RIVER_EDGE_MAX);

    for (int i = 0; i < count; i++)
    {
        float reach = _RiverEdgeReach[i];
        if (reach < 0.0) continue;

        float4 a = _RiverEdgePoints[i];
        float4 b = (a.w > 0.0 && i + 1 < count) ? _RiverEdgePoints[i + 1] : a;

        float t;
        float outside = _RiverEdgeOutside(a, b, xz, t);
        float w = outside <= 0.0 ? 1.0
                : (reach > 0.0 ? 1.0 - smoothstep(0.0, reach, outside) : 0.0);

        if (w > best)
        {
            best = w;
            rimY = lerp(a.y, b.y, t);
        }
    }
    return best;
}

// Nearest any run or pool comes: how far outside its outer edge the point is, negative inside.
float _RiverEdgeNearest(float2 xz)
{
    float nearest = 1e6;
    int   count   = min((int)_RiverEdgeCount, RIVER_EDGE_MAX);

    for (int i = 0; i < count; i++)
    {
        float4 a = _RiverEdgePoints[i];
        float4 b = (a.w > 0.0 && i + 1 < count) ? _RiverEdgePoints[i + 1] : a;

        float t;
        nearest = min(nearest, _RiverEdgeOutside(a, b, xz, t));
    }
    return nearest;
}

// Integer-hash value noise (no sin, so it can be mirrored on the CPU).
float _HillHash(float2 ip)
{
    uint x = (uint)((int)ip.x) * 374761393u;
    uint y = (uint)((int)ip.y) * 668265263u;
    uint h = x + y;
    h = (h ^ (h >> 13)) * 1274126177u;
    h = h ^ (h >> 16);
    return (float)(h & 0x00FFFFFFu) / 16777216.0;
}

// Random unit gradient at a lattice point (hash -> angle).
float2 _HillGrad(float2 ip)
{
    float ang = _HillHash(ip) * 6.2831853;   // 0..2*pi
    return float2(cos(ang), sin(ang));
}

// Gradient (Perlin-style) noise, remapped to ~[0,1].
float _HillGradientNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);

    float d00 = dot(_HillGrad(i + float2(0.0, 0.0)), f - float2(0.0, 0.0));
    float d10 = dot(_HillGrad(i + float2(1.0, 0.0)), f - float2(1.0, 0.0));
    float d01 = dot(_HillGrad(i + float2(0.0, 1.0)), f - float2(0.0, 1.0));
    float d11 = dot(_HillGrad(i + float2(1.0, 1.0)), f - float2(1.0, 1.0));

    float n = lerp(lerp(d00, d10, u.x), lerp(d01, d11, u.x), u.y);
    return n * 0.7071 + 0.5;   // ~[-0.707,0.707] -> ~[0,1]
}

float CalculateHillHeight(float3 WorldPos, int count, float GlobalHeight)
{
    float totalY = 0.0;
    for (int i = 0; i < count; i++)
    {
        float3 hillCenter = _HillPositions[i].xyz;
        float  radius     = _HillPositions[i].w;

        float2 distVector = WorldPos.xz - hillCenter.xz;
        float  d          = length(distVector);

        // Per-point profile: sharpness pushes the slope toward the rim.
        // sharpness 0 -> smoothstep(0,1) = original rounded hill.
        // sharpness 1 -> transition crammed against the edge = near-vertical wall.
        float t       = saturate(d / radius);
        float edge0   = min(saturate(_HillSharpness[i]), 0.999);
        float falloff = 1.0 - smoothstep(edge0, 1.0, t);

        // Smooth base height only (no noise) — this is what drives the Normal,
        // so lighting stays clean even though the geometry gets noise below.
        totalY += falloff * hillCenter.y * GlobalHeight;
    }

    // Drawn up to the river edges. Here too rather than only in the geometry, so the Normal
    // below sees the slope down from a rim and lights it.
    float rimY;
    float w = _RiverEdgeLift(WorldPos.xz, rimY);
    return lerp(totalY, rimY - RIVER_EDGE_UNDER_RIM - WorldPos.y, w);
}

// How much the river edges hold the ground here — the rocky noise fades out by the same amount,
// so the ground meets a rim clean.
float RiverEdgeHold(float3 WorldPos)
{
    float rimY;
    return _RiverEdgeLift(WorldPos.xz, rimY);
}

// The rocky noise at WorldPos, told in three numbers rather than one:
//   x  the height it pushes the geometry, in metres
//   y  which way it leans here, -1 fully down to +1 fully up
//   z  how much noise there is here at all, 0 where the hills carry none
//
// Kept separate from the smooth height so x can go into the geometry (OffsetPos) WITHOUT
// contaminating the Normal, and fades with each hill's falloff like everything else.
//
// The lean is the push measured against how far it COULD have pushed at this point, so it reads
// the same on a tall hill and a shallow one. The amount is what tells ground with no noise in it
// from ground that merely happens to lie level — without it, flat ground would sit exactly on the
// line between up and down.
float3 CalculateHillNoise(float3 WorldPos, int count, float GlobalHeight)
{
    float push      = 0.0;
    float potential = 0.0;
    float amount    = 0.0;

    for (int i = 0; i < count; i++)
    {
        if (_HillNoise[i] <= 0.0) continue;

        float3 hillCenter = _HillPositions[i].xyz;
        float  radius     = _HillPositions[i].w;

        float d       = length(WorldPos.xz - hillCenter.xz);
        float t       = saturate(d / radius);
        float edge0   = min(saturate(_HillSharpness[i]), 0.999);
        float falloff = 1.0 - smoothstep(edge0, 1.0, t);

        float n    = _HillGradientNoise(WorldPos.xz * _HillNoiseScale) * 2.0 - 1.0;
        float lift = falloff * _HillNoise[i] * hillCenter.y * GlobalHeight;

        push      += n * lift;
        potential += abs(lift);            // abs: a hole's height is negative
        amount    += falloff * _HillNoise[i];
    }

    return float3(push, push / max(potential, 1e-5), saturate(amount));
}

// Noise : how the rocky noise leans here (x) and how much of it there is (y) — see
//         CalculateHillNoise. Feeds Landscape Shading, which wears one variant on the ups and
//         another on the downs.
void CalculateHills_float(float3 WorldPos, float PointCount, float GlobalHeight,
                          out float3 OffsetPos, out float3 Normal, out float2 Noise)
{
    int count = (int)PointCount;

    float  h0    = CalculateHillHeight(WorldPos, count, GlobalHeight);
    float3 noise = CalculateHillNoise(WorldPos, count, GlobalHeight);

    OffsetPos   = WorldPos;
    OffsetPos.y += h0 + noise.x * (1.0 - RiverEdgeHold(WorldPos));   // noise in geometry only
    Noise       = noise.yz;

    // Finite differences for surface normal — uses the SMOOTH height (no noise).
    float epsilon = 0.5;
    float hX = CalculateHillHeight(WorldPos + float3(epsilon, 0, 0), count, GlobalHeight);
    float hZ = CalculateHillHeight(WorldPos + float3(0, 0, epsilon), count, GlobalHeight);

    float3 tangentX = float3(epsilon, hX - h0, 0);
    float3 tangentZ = float3(0, hZ - h0, epsilon);
    Normal = normalize(cross(tangentZ, tangentX));
}

// The landscape's Alpha, for the RiverEdgeCut subgraph: 0 inside any river run or pool, a
// little way in from its outer edge (under the rim), 1 everywhere else. With Alpha Clipping on
// and the threshold at 0.5, the ground is cut away where a run stands, to the pixel — so the
// ground meets the rim exactly however coarse the tile grid is, and never covers a channel.
// Nothing is cut while no path has Landscape Influence on (no edges are sent then).
void RiverEdgeCut_float(float3 WorldPos, out float Alpha)
{
    Alpha = _RiverEdgeNearest(WorldPos.xz) > -RIVER_EDGE_TUCK ? 1.0 : 0.0;
}

void RiverEdgeCut_half(half3 WorldPos, out half Alpha)
{
    float alpha;
    RiverEdgeCut_float(WorldPos, alpha);
    Alpha = alpha;
}

#endif // CALCULATE_HILLS_INCLUDED
