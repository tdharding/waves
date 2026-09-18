uniform float4 _HillPositions[100];
uniform float  _HillSharpness[100];   // 0 = smooth (default), 1 = near-vertical
uniform float  _HillNoise[100];       // per-point rocky-noise amount (0 = none)
uniform float  _HillNoiseScale;       // global noise frequency

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
    return totalY;
}

// Rocky noise height, kept separate so it can go into the geometry (OffsetPos)
// WITHOUT contaminating the smooth Normal. Fades with each hill's falloff.
float CalculateHillNoise(float3 WorldPos, int count, float GlobalHeight)
{
    float total = 0.0;
    for (int i = 0; i < count; i++)
    {
        if (_HillNoise[i] <= 0.0) continue;

        float3 hillCenter = _HillPositions[i].xyz;
        float  radius     = _HillPositions[i].w;

        float d       = length(WorldPos.xz - hillCenter.xz);
        float t       = saturate(d / radius);
        float edge0   = min(saturate(_HillSharpness[i]), 0.999);
        float falloff = 1.0 - smoothstep(edge0, 1.0, t);

        float n = _HillGradientNoise(WorldPos.xz * _HillNoiseScale) * 2.0 - 1.0;
        total += falloff * n * _HillNoise[i] * hillCenter.y * GlobalHeight;
    }
    return total;
}

void CalculateHills_float(float3 WorldPos, float PointCount, float GlobalHeight,
                          out float3 OffsetPos, out float3 Normal)
{
    int count = (int)PointCount;

    float h0 = CalculateHillHeight(WorldPos, count, GlobalHeight);
    OffsetPos   = WorldPos;
    OffsetPos.y += h0 + CalculateHillNoise(WorldPos, count, GlobalHeight);   // noise in geometry only

    // Finite differences for surface normal — uses the SMOOTH height (no noise).
    float epsilon = 0.5;
    float hX = CalculateHillHeight(WorldPos + float3(epsilon, 0, 0), count, GlobalHeight);
    float hZ = CalculateHillHeight(WorldPos + float3(0, 0, epsilon), count, GlobalHeight);

    float3 tangentX = float3(epsilon, hX - h0, 0);
    float3 tangentZ = float3(0, hZ - h0, epsilon);
    Normal = normalize(cross(tangentZ, tangentX));
}
