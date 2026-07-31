// Soul fish zone mask + path-space UV for the wave surface.
//
// Packed points arrive from SoulFishWaveLinker as one flat array shared by every zone on the
// level (a zone is a consecutive run of chained points):
//   .xz = world position          .y = this point's mask radius (0 = use _SoulFishRadius)
//   .w  = ±(1 + cumulative XZ arc length along the zone at this point)
//         sign:  positive = connects to the next point, negative = end of a chain / lone point
// The arc length riding in .w is what makes a consistent along-path UV possible: the shader
// recovers s = |w| - 1 + h·segLen at any pixel without a second uniform array.
//
// These are bare $Globals (NOT blackboard properties) — see SoulFishWaveLinker for why they are
// re-pushed every frame through both Material.Set* and Shader.SetGlobal*. The array is written
// with SetVectorArray; global arrays lock their size on first set, so the linker always pushes
// the full 40 slots.
#define SOULFISH_MAX_POINTS 40

float4 _SoulFishWavePositions[SOULFISH_MAX_POINTS];
float  _SoulFishCount;
float  _SoulFishRadius;
float  _SoulFishStrength;

// Edge-fade noise controls (set from WaveMaterialController / WaveEffectsLiveTuner).
float _SoulFishFadeStart;         // 0..1 of the radius where the fade band begins (core stays solid below this)
float _SoulFishEdgeNoiseStrength; // how hard the noise erodes/extends the fade edge (0 = clean edge)
float _SoulFishEdgeNoiseScale;    // world-space frequency of the large octave (used at the inner fade)
float _SoulFishEdgeNoiseScale2;   // world-space frequency of the fine octave (used at the outer edge)
float _SoulFishEdgeNoiseSpeed;    // scroll speed for the animated fringe

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

// Core evaluation shared by both graph entry points.
//
// PathUV is a path-space coordinate: x = distance travelled along the zone path (world units,
// continuous across nodes thanks to the packed arc length), y = signed sideways offset from the
// path centreline (positive = left of travel). Scrolling PathUV.x makes the texture flow down
// the river in one consistent trajectory — corners bend the texture instead of smearing it.
// For lone points (single-node zones, loose fish) PathUV falls back to the static local offset
// from the point, matching the old "circular zones don't scroll" behaviour.
void SoulFishWaveMaskCore(
    float3 WorldPos,
    float  SoulFishMaskStrength,
    out float  Mask,
    out float2 Flow,
    out float2 PathUV)
{
    float maskAccum = 0.0;
    int count = min((int)_SoulFishCount, SOULFISH_MAX_POINTS);

    // Two animated world-space noise octaves, evaluated once (they depend only on the pixel).
    // A large base octave and a finer octave; they're blended per point across the fade band below.
    float2 uvBase = WorldPos.xz * _SoulFishEdgeNoiseScale  + _Time.y * _SoulFishEdgeNoiseSpeed;
    float2 uvFine = WorldPos.xz * _SoulFishEdgeNoiseScale2 + _Time.y * _SoulFishEdgeNoiseSpeed * 1.7 + 31.4;
    float  nBase = valueNoise_SoulFish(uvBase); // 0..1
    float  nFine = valueNoise_SoulFish(uvFine); // 0..1

    float fadeStart = saturate(_SoulFishFadeStart);

    float2 flowSum = float2(0.0, 0.0); // proximity-weighted sum of segment tangents (legacy Flow)

    // Nearest feature wins the UV. bestT compares radius-normalized distances so zones with
    // different radii hand over exactly where their mask dominance changes.
    float  bestT  = 1e9;
    float2 bestUV = float2(0.0, 0.0);

    for (int i = 0; i < SOULFISH_MAX_POINTS; i++)
    {
        if (i >= count) break;

        float4 P = _SoulFishWavePositions[i];
        float2 p = P.xz;

        float r = P.y > 0.0001 ? P.y : _SoulFishRadius;
        r = max(r, 0.0001);

        bool  connects = (P.w > 0.0) && (i < count - 1);
        float cumLen   = abs(P.w) - 1.0;

        // Nearest normalized distance to this point (and its outgoing segment): 0 = centre, 1 = edge.
        float t = length(WorldPos.xz - p) / r;

        if (connects)
        {
            float2 ba      = _SoulFishWavePositions[i + 1].xz - p;
            float2 pa      = WorldPos.xz - p;
            float  segLen2 = dot(ba, ba);
            if (segLen2 > 1e-8)
            {
                float segLen = sqrt(segLen2);
                float h      = clamp(dot(pa, ba) / segLen2, 0.0, 1.0);
                float ds     = length(pa - ba * h);
                t = min(t, ds / r);

                // Accumulate the segment tangent, weighted by proximity, so Flow blends smoothly
                // between segments instead of snapping at the node joints.
                float w = saturate(1.0 - ds / r);
                flowSum += (ba / segLen) * w;

                // Path-space candidate. ds (euclidean distance to the segment) is continuous
                // through corners — beyond an open end it becomes distance to the endpoint, so
                // end caps and outer corners never tear the UV.
                float tSeg = ds / r;
                if (tSeg < bestT)
                {
                    bestT = tSeg;
                    float s      = cumLen + h * segLen;
                    float crossZ = ba.x * pa.y - ba.y * pa.x; // >0 = left of travel direction
                    bestUV = float2(s, crossZ >= 0.0 ? ds : -ds);
                }
            }
        }
        else
        {
            // Lone point: not chaining out, and nothing chains into it (chain interiors and
            // endpoints are already covered by their segment's clamped projection above).
            bool chainedInto = (i > 0) && (_SoulFishWavePositions[i - 1].w > 0.0);
            if (!chainedInto && t < bestT)
            {
                bestT  = t;
                bestUV = WorldPos.xz - p; // static local offset — circular zones don't scroll
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

    PathUV = bestUV;
}

// Legacy entry point — keeps existing Custom Function nodes (Mask + Flow outputs) compiling
// unchanged. Prefer SoulFishWaveMaskPath below for the path-space UV.
void SoulFishWaveMask_float(
    float3 WorldPos,
    float  SoulFishMaskStrength,
    out float  Mask,
    out float2 Flow)
{
    float2 pathUV;
    SoulFishWaveMaskCore(WorldPos, SoulFishMaskStrength, Mask, Flow, pathUV);
}

// Path-space entry point. Wire PathUV into Tiling And Offset (UV input) and scroll by offsetting
// x with Time × ZoneScrollSpeed — the texture then travels along the zone path everywhere.
// PathUV is in world units on both axes.
void SoulFishWaveMaskPath_float(
    float3 WorldPos,
    float  SoulFishMaskStrength,
    out float  Mask,
    out float2 Flow,
    out float2 PathUV)
{
    SoulFishWaveMaskCore(WorldPos, SoulFishMaskStrength, Mask, Flow, PathUV);
}
