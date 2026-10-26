// The level select's white fade: colour washed towards the Screen Picture with distance from the
// boat, true at the boat and faded by Radius, at Opacity.
//
// Fade Sharpness sets where the fade starts: at 0 it starts at the boat and eases all the way out
// to Radius; at 1 it is a hard edge at Radius. In between, the soft band narrows towards Radius.
//
// Then the gradient on top: the Screen Picture ADDED below Gradient Bottom, easing to nothing by
// world Y = 1 — as it was in RiverRunShader, where the top was a Color node's 1 wired into Edge2.
//
// The Screen Picture is what the fade goes to: a ramp read from the bottom of the screen to the
// top, its colour what the fade goes to and its alpha how strongly it goes there. A grain of
// gradient noise, scrolling sideways across the screen, ADDS white to it — it only ever lightens.
// It is pinned to the screen, not the world — the ground moves through it as the camera pans. An
// all white ramp with no grain gives the old plain white fade back, for colour up to 1.
//
// The fade is a straight blend to the picture, so HDR colour above 1 is pulled down to it as well.
//
// Every setting is a bare $Global — there is no property block behind any of them, so a graph
// only has to wire its colour through the node. Pushed every frame by WhiteFadeSettings, one set
// of numbers shared by the river runs, the landscape and the spikes, and tuned in the White Fade
// foldout of either the Run Shading Tuner or the Landscape Tuner.

#ifndef WHITE_FADE_INCLUDED
#define WHITE_FADE_INCLUDED

// Where the boat is. Its own name rather than _BoatWorldCenter: RiverRunShader declares that one
// as a graph property, and a second declaration here would not compile there. Pushed alongside
// it by LevelSelectBoatToShaders.
float4 _WhiteFadeCentre;

// Metres from the boat to where the fade is full.
float  _WhiteFadeRadius;

// How far the fade pulls towards the picture, 0 to 1.
float  _WhiteFadeOpacity;

// 0 = the fade eases in from the boat, 1 = a hard edge at Radius.
float  _WhiteFadeSharpness;

// World Y the gradient is full at, easing out by WHITE_FADE_GRADIENT_TOP.
float  _WhiteFadeGradientBottom;
#define WHITE_FADE_GRADIENT_TOP 1.0

// The Screen Picture's ramp, bottom of the screen at u = 0 and top at u = 1. Colour in rgb, how
// strongly the fade goes to it in a.
TEXTURE2D(_WhiteFadeRamp);
SAMPLER(sampler_WhiteFadeRamp);

// The grain: x its size as a share of the screen's height, y how much white it adds at most, z how
// fast it scrolls sideways in screen heights a second — minus goes left.
float4 _WhiteFadeGrain;

// One corner's direction for the gradient noise, from an integer hash — no sin, so it holds still
// at any grain size.
float2 WhiteFadeNoiseDir(float2 cell)
{
    uint2 q = (uint2)(int2)cell;
    uint  h = (q.x * 0x8da6b343u) ^ (q.y * 0xd8163841u);
    h = (h ^ (h >> 16)) * 0x7feb352du;
    h = (h ^ (h >> 15)) * 0x846ca68bu;
    h ^= h >> 16;
    float a = (float)(h & 0xffffu) * (6.2831853 / 65536.0);
    return float2(cos(a), sin(a));
}

// Gradient noise, about -0.7 to 0.7.
float WhiteFadeNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = p - i;
    float2 u = f * f * (3.0 - 2.0 * f);

    float n00 = dot(WhiteFadeNoiseDir(i),                    f);
    float n10 = dot(WhiteFadeNoiseDir(i + float2(1.0, 0.0)), f - float2(1.0, 0.0));
    float n01 = dot(WhiteFadeNoiseDir(i + float2(0.0, 1.0)), f - float2(0.0, 1.0));
    float n11 = dot(WhiteFadeNoiseDir(i + float2(1.0, 1.0)), f - float2(1.0, 1.0));

    return lerp(lerp(n00, n10, u.x), lerp(n01, n11, u.x), u.y);
}

// What the fade goes to at this pixel: rgb the picture with the grain's white added, a how strongly.
float4 WhiteFadeScreenPicture(float3 worldPos)
{
    float4 screen = ComputeScreenPos(TransformWorldToHClip(worldPos));
    float2 uv     = screen.xy / max(screen.w, 1e-5);

    float4 picture = SAMPLE_TEXTURE2D_LOD(_WhiteFadeRamp, sampler_WhiteFadeRamp,
                                          float2(saturate(uv.y), 0.5), 0);

    // In screen heights, so the grain stays round whatever shape the screen is.
    float  aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
    float2 p      = float2(uv.x * aspect - _Time.y * _WhiteFadeGrain.z, uv.y)
                  / max(_WhiteFadeGrain.x, 1e-4);

    // The noise taken 0 to 1, so it only ever adds.
    float grain = saturate(WhiteFadeNoise(p) * (0.5 / 0.7) + 0.5);
    picture.rgb += grain * _WhiteFadeGrain.y;
    return picture;
}

void WhiteFade_float(float3 In, float3 WorldPos, out float3 Out)
{
    float d = length(WorldPos - _WhiteFadeCentre.xyz);

    float4 picture = WhiteFadeScreenPicture(WorldPos);

    // 0 at the boat (or inside the sharp edge), 1 at Radius and beyond. Kept a hair under Radius
    // at full sharpness, where smoothstep's two edges would otherwise meet.
    float radius = max(_WhiteFadeRadius, 1e-4);
    float start  = min(radius * saturate(_WhiteFadeSharpness), radius - 1e-4);
    float fade   = smoothstep(start, radius, d) * _WhiteFadeOpacity;

    // Blends to the picture, as far as the picture's alpha lets it at this height.
    float3 c = lerp(In, lerp(In, picture.rgb, picture.a), fade);

    float rise = 1.0 - smoothstep(_WhiteFadeGradientBottom, WHITE_FADE_GRADIENT_TOP, WorldPos.y);
    Out = c + rise * picture.rgb * picture.a;
}

#endif
