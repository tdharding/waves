// The soul zone drawn along a soul route on the level-select water — the band the travelling
// fish swim in. It draws out along the route from its origin when the souls are released, and
// the fish follow behind its front.
//
// The arena's SoulFishWaveMask with the noise taken out: a clean band, soft over its outer edge,
// with the same path-space UV so its grain flows down the route and bends round its corners.
// Its own globals, never the arena's _SoulFish* ones — a global outlives the scene that set it,
// so sharing them would leave the arena's zones painted over the map and the map's over the arena.
//
// Published every frame by TravellingSoulFishController (the points) and by
// RiverSoulZoneSettings through ApplyAesthetics (the look, from the river water preset). Bare
// $Globals: nothing on disk remembers them and a shader reimport empties them, so the every-frame
// push is what brings them back.
//
// Packed points, one flat array shared by every route (a route is a consecutive run of points):
//   .xz = world position on the centre of the river
//   .y  = half-width of the band here (the route's Band Width)
//   .w  = ±(1 + arc length along the route to this point)
//         sign: positive = joins the next point, negative = last point of its route
// A souls pool tower's bowl is one lone point (.w negative) — a disc .y wide filling its water.
#define TRAVELZONE_MAX_POINTS 128

float4 _TravelSoulZonePositions[TRAVELZONE_MAX_POINTS];
float  _TravelSoulZoneCount;
float  _TravelSoulZoneStrength;   // master multiplier; 0 hides the band
float  _TravelSoulZoneFadeStart;  // 0..1 of the half-width where the soft edge begins
float  _TravelSoulZoneWidth;      // multiplier on every route's Band Width (the river tuner's Width)
float  _TravelSoulZoneBrightness; // multiplier on Colour's rgb only — coverage (alpha, Mask) untouched
float4 _TravelSoulZoneBaseColour; // the colour under the grain (rgb only)

float  _TravelSoulZoneGrainSize;     // world units per grain cell; 0 = no grain
float  _TravelSoulZoneGrainStrength; // how far the grain lifts the base colour towards white, 0..1; 0 = no grain
float  _TravelSoulZoneScrollSpeed;   // world units per second the grain flows down the route

// The band's middle swung from side to side of the route's line along it, an even wave:
// x how far either side (world units; 0 = straight), y the distance along the route per swing.
float4 _TravelSoulZoneMeander;

// The river stone's gradient noise (RiverRunShading.hlsl), copied under its own names so both
// can sit in one graph: the same hash and quintic fade as the Gradient Noise node, about zero.
float2 TravelZoneNoiseDir(float2 p)
{
    p = fmod(p, 289.0);
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

float TravelZoneGradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);

    float d00 = dot(TravelZoneNoiseDir(ip),                    fp);
    float d01 = dot(TravelZoneNoiseDir(ip + float2(0.0, 1.0)), fp - float2(0.0, 1.0));
    float d10 = dot(TravelZoneNoiseDir(ip + float2(1.0, 0.0)), fp - float2(1.0, 0.0));
    float d11 = dot(TravelZoneNoiseDir(ip + float2(1.0, 1.0)), fp - float2(1.0, 1.0));

    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y);
}

// Mask   : 0..1 coverage of the band (already times Strength)
// PathUV : x = distance along the route, y = signed distance across it — world units, unscrolled
// Colour : the base colour, lightened by the grain, scrolling along the route, times Mask (alpha included)
void TravellingSoulZone_float(
    float3 WorldPos,
    out float  Mask,
    out float2 PathUV,
    out float4 Colour)
{
    int   count = min((int)_TravelSoulZoneCount, TRAVELZONE_MAX_POINTS);
    float width = max(_TravelSoulZoneWidth, 0.0001);

    float meanderWidth = max(_TravelSoulZoneMeander.x, 0.0);
    float meanderSpace = max(_TravelSoulZoneMeander.y, 0.1);

    // Nearest point of any route, as a fraction of the band's half-width there: 0 = centre, 1 = edge.
    float  bestT  = 1e9;
    float2 bestUV = float2(0.0, 0.0);

    for (int i = 0; i < TRAVELZONE_MAX_POINTS; i++)
    {
        if (i >= count) break;

        float4 P = _TravelSoulZonePositions[i];
        float  r = max(P.y * width, 0.0001);
        float  s = abs(P.w) - 1.0;

        float2 pa = WorldPos.xz - P.xz;

        if (P.w > 0.0 && i < count - 1)
        {
            float2 ba      = _TravelSoulZonePositions[i + 1].xz - P.xz;
            float  segLen2 = dot(ba, ba);
            if (segLen2 > 1e-8)
            {
                float segLen = sqrt(segLen2);
                float h      = saturate(dot(pa, ba) / segLen2);
                float ds     = length(pa - ba * h);
                float crossZ = ba.x * pa.y - ba.y * pa.x;   // > 0 = left of travel
                float along  = s + h * segLen;

                // Across from the band's middle, which the meander swings off the line.
                float across = (crossZ >= 0.0 ? ds : -ds)
                             - meanderWidth * sin(6.28318530718 * along / meanderSpace);

                // The width eases from this point's to the next one's, so two routes of different
                // Band Width meeting at a shared stretch do not step.
                float rSeg = lerp(r, max(_TravelSoulZonePositions[i + 1].y * width, 0.0001), h);
                float t    = abs(across) / rSeg;

                if (t < bestT)
                {
                    bestT  = t;
                    bestUV = float2(along, across);
                }
                continue;
            }
        }

        // A lone point: a souls pool tower's bowl filled with the zone, or a route that is only
        // one point so far — the moment it starts drawing. Its UV is the offset from the point,
        // so the grain still lies across the disc and flows over it.
        float tPoint = length(pa) / r;
        if (tPoint < bestT)
        {
            bestT  = tPoint;
            bestUV = float2(s + pa.x, pa.y);
        }
    }

    float fadeStart = saturate(_TravelSoulZoneFadeStart);
    Mask   = (1.0 - smoothstep(fadeStart, 1.0, bestT)) * _TravelSoulZoneStrength;
    PathUV = bestUV;

    // The grain only lightens: it lifts the base colour towards white and never below it, so the
    // base colour is the darkest the band gets. Scrolled down the route in path space, so it
    // bends round the corners.
    float3 colour = _TravelSoulZoneBaseColour.rgb;
    if (_TravelSoulZoneGrainSize > 0.0 && _TravelSoulZoneGrainStrength > 0.0)
    {
        float2 p = float2(bestUV.x - _Time.y * _TravelSoulZoneScrollSpeed, bestUV.y)
                 / _TravelSoulZoneGrainSize;
        float lift = saturate(TravelZoneGradientNoise(p) + 0.5) * saturate(_TravelSoulZoneGrainStrength);
        colour = lerp(colour, float3(1.0, 1.0, 1.0), lift);
    }

    Colour = float4(colour * _TravelSoulZoneBrightness, 1.0) * Mask;
}

// Half variant — Shader Graph appends _float or _half depending on node/graph precision, and a
// File custom function must provide whichever it asks for.
void TravellingSoulZone_half(
    half3 WorldPos,
    out half  Mask,
    out half2 PathUV,
    out half4 Colour)
{
    float  m;
    float2 uv;
    float4 c;
    TravellingSoulZone_float(WorldPos, m, uv, c);
    Mask   = m;
    PathUV = uv;
    Colour = c;
}
