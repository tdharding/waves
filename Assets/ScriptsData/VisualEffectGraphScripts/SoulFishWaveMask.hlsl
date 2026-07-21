// Declare individual position properties
float4 _SoulFishPosition1;
float4 _SoulFishPosition2;
float4 _SoulFishPosition3;
float4 _SoulFishPosition4;
float4 _SoulFishPosition5;
float4 _SoulFishPosition6;
float4 _SoulFishPosition7;
float4 _SoulFishPosition8;
float4 _SoulFishPosition9;
float4 _SoulFishPosition10;
float4 _SoulFishPosition11;
float4 _SoulFishPosition12;
float4 _SoulFishPosition13;
float4 _SoulFishPosition14;
float4 _SoulFishPosition15;
float4 _SoulFishPosition16;
float4 _SoulFishPosition17;
float4 _SoulFishPosition18;
float4 _SoulFishPosition19;
float4 _SoulFishPosition20;

float _SoulFishCount;
float _SoulFishRadius;
float _SoulFishStrength;

// Edge-fade noise controls (set from WaveMaterialController / WaveEffectsLiveTuner).
float _SoulFishFadeStart;         // 0..1 of the radius where the fade band begins (core stays solid below this)
float _SoulFishEdgeNoiseStrength; // how hard the noise erodes/extends the fade edge (0 = clean edge)
float _SoulFishEdgeNoiseScale;    // world-space frequency of the large octave (used at the inner fade)
float _SoulFishEdgeNoiseScale2;   // world-space frequency of the fine octave (used at the outer edge)
float _SoulFishEdgeNoiseSpeed;    // scroll speed for the animated fringe

float distToSegment_SoulFish(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

// --- Procedural value noise (self-contained; no texture bindings) ---
float hash_SoulFish(float2 p)
{
    p = frac(p * float2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return frac(p.x * p.y);
}

float valueNoise_SoulFish(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);        // smootherstep interpolation
    float a = hash_SoulFish(i + float2(0.0, 0.0));
    float b = hash_SoulFish(i + float2(1.0, 0.0));
    float c = hash_SoulFish(i + float2(0.0, 1.0));
    float d = hash_SoulFish(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

void SoulFishWaveMask_float(
    float3 WorldPos,
    float  SoulFishMaskStrength,
    out float  Mask,
    out float2 Flow)   // world-space XZ direction along the nearest river segment (0 for circular zones)
{
    float4 positions[20];
    positions[0]  = _SoulFishPosition1;
    positions[1]  = _SoulFishPosition2;
    positions[2]  = _SoulFishPosition3;
    positions[3]  = _SoulFishPosition4;
    positions[4]  = _SoulFishPosition5;
    positions[5]  = _SoulFishPosition6;
    positions[6]  = _SoulFishPosition7;
    positions[7]  = _SoulFishPosition8;
    positions[8]  = _SoulFishPosition9;
    positions[9]  = _SoulFishPosition10;
    positions[10] = _SoulFishPosition11;
    positions[11] = _SoulFishPosition12;
    positions[12] = _SoulFishPosition13;
    positions[13] = _SoulFishPosition14;
    positions[14] = _SoulFishPosition15;
    positions[15] = _SoulFishPosition16;
    positions[16] = _SoulFishPosition17;
    positions[17] = _SoulFishPosition18;
    positions[18] = _SoulFishPosition19;
    positions[19] = _SoulFishPosition20;

    float maskAccum = 0.0;
    int count = (int)_SoulFishCount;

    // Two animated world-space noise octaves, evaluated once (they depend only on the pixel).
    // A large base octave and a finer octave; they're blended per point across the fade band below.
    float2 uvBase = WorldPos.xz * _SoulFishEdgeNoiseScale  + _Time.y * _SoulFishEdgeNoiseSpeed;
    float2 uvFine = WorldPos.xz * _SoulFishEdgeNoiseScale2 + _Time.y * _SoulFishEdgeNoiseSpeed * 1.7 + 31.4;
    float  nBase = valueNoise_SoulFish(uvBase); // 0..1
    float  nFine = valueNoise_SoulFish(uvFine); // 0..1

    float fadeStart = saturate(_SoulFishFadeStart);

    float2 flowSum = float2(0.0, 0.0); // proximity-weighted sum of segment tangents

    for (int i = 0; i < 20; i++)
    {
        if (i >= count) break;

        float2 p = positions[i].xz;

        // Per-point mask radius rides in the otherwise-unused .y channel, authored by the
        // Grid Designer soul-zone radius. Points with y == 0 (e.g. loose fish) fall back to
        // the global _SoulFishRadius, preserving the old behaviour.
        float r = positions[i].y > 0.0001 ? positions[i].y : _SoulFishRadius;
        r = max(r, 0.0001);

        // Nearest normalized distance to this point (and its outgoing segment): 0 = centre, 1 = edge.
        float t = length(WorldPos.xz - p) / r;
        if (i < 19 && i < count - 1 && positions[i].w > 1.5)
        {
            float2 seg    = positions[i+1].xz - p;
            float  segLen = length(seg);
            float  ds     = distToSegment_SoulFish(WorldPos.xz, p, positions[i+1].xz);
            t = min(t, ds / r);

            // Accumulate the segment tangent, weighted by proximity, so Flow blends smoothly
            // between segments instead of snapping at the node joints.
            if (segLen > 1e-4)
            {
                float w = saturate(1.0 - ds / r);
                flowSum += (seg / segLen) * w;
            }
        }

        // Smoothstep fade over [fadeStart .. 1]. edgeW is 0 in the solid core and 1 at the outer edge.
        // The noise scale blends from the large base octave (inner fade) to the fine octave (outer edge)
        // by edgeW, so the fringe "starts large then breaks into finer noise" along the fade-out. edgeW
        // also weights the strength, keeping the core untouched.
        float edgeW = smoothstep(fadeStart, 1.0, t);
        float noise = lerp(nBase, nFine, edgeW) * 2.0 - 1.0; // -1..1
        float tN    = saturate(t + noise * _SoulFishEdgeNoiseStrength * edgeW);
        float contribution = 1.0 - smoothstep(fadeStart, 1.0, tN);

        maskAccum = max(maskAccum, contribution);
    }

    // Per-point distance-based zone falloff
    float distMask = maskAccum * _SoulFishStrength;

    Mask = distMask * SoulFishMaskStrength;

    // Normalized flow direction along the path (0 for single-node / circular zones).
    float flowLen = length(flowSum);
    Flow = flowLen > 1e-4 ? flowSum / flowLen : float2(0.0, 0.0);
}
