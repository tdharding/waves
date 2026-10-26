// Occluder fade — an object that blocks the camera's view of the boat alphas out.
//
// CameraOccluderFader (C#) casts a ray from the camera to the boat every frame. Every renderer the
// ray passes through gets a 0..1 blocking amount pushed to it through a MaterialPropertyBlock as
// _OccluderFade (0 = clear, 1 = fully blocking, ramped in and out so it never pops). This turns that
// amount into an alpha.
//
// _OccluderFadedAlpha is the shared knob — the alpha a fully blocking object is left at (0.25 = 25%).
// It is a bare $Globals uniform pushed every frame by the same script, so a shader reimport that
// wipes it self-heals on the next frame (same reasoning as InstancedLights.hlsl).
//
// AlphaIn       : any alpha already worked out by the graph — sonar mask, destructible mask, etc.
//                 The occluder fade multiplies into it, so the dither happens ONCE over the combined
//                 result. Leave it unconnected (default 1) to use the occluder fade on its own.
// ScreenPosition: Screen Position node (Default mode) — only used for the dithered output
// Alpha         : the combined alpha, undithered. For a Transparent graph, into the Alpha port.
// DitherAlpha   : 0/1 screen-door mask. For an Opaque graph, into Alpha with Alpha Clipping on and
//                 the clip threshold at 0.5 — fades without needing transparency or sorting.
//
// Both uniforms are bare $Globals, so nothing has to be declared on the graph or the material:
// _OccluderFade is written per renderer by the property block (renderers the script never touches
// keep the global 0 = solid), _OccluderFadedAlpha is pushed globally.
//
// Screen Circle — a piece set to Screen Circle in the designer gets _OccluderCircle = 1 in its property
// block too (the global stays 0). Its fade then reaches only a soft circle round the boat on screen:
// _OccluderCircleCentre is the boat in the same 0..1 screen space as ScreenPosition (built per camera,
// in beginCameraRendering), _OccluderCircleRadius and _OccluderCircleFeather are shares of the screen's
// height, so the circle stays round whatever the aspect. Outside it the piece stays solid.

// Per-renderer, NOT per-material. Declared bare (outside UnityPerMaterial) which is what lets a
// MaterialPropertyBlock override it for one renderer at a time — objects sharing the material are
// untouched, and the script holds the global at 0 so anything without a block stays solid.
float _OccluderFade;        // 0 = clear, 1 = fully blocking
float _OccluderFadedAlpha;  // alpha left at full block, e.g. 0.25 = 25%
float  _OccluderCircle;         // 1 = Screen Circle piece, 0 = the whole object fades
float2 _OccluderCircleCentre;   // the boat on screen, 0..1
float  _OccluderCircleRadius;   // share of the screen's height
float  _OccluderCircleFeather;  // soft edge, inside the radius, share of the screen's height

// Ordered 4x4 Bayer thresholds (0..15)/16 — the screen-door pattern for the dithered output.
static const float OCCLUDER_BAYER4[16] =
{
     0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
    12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
     3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
    15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
};

void OccluderFade_float(
    float  AlphaIn,
    float2 ScreenPosition,
    out float Alpha,
    out float DitherAlpha)
{
    // Screen Circle: 1 inside the circle round the boat, feathering to 0 at its edge. Measured in
    // screen heights, so x is stretched by the aspect to keep it round.
    float2 toBoat = ScreenPosition - _OccluderCircleCentre;
    toBoat.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);
    float  inner  = _OccluderCircleRadius - max(_OccluderCircleFeather, 1e-4);
    float  inside = 1.0 - smoothstep(inner, _OccluderCircleRadius, length(toBoat));
    float  reach  = lerp(1.0, inside, saturate(_OccluderCircle));

    // 1 when clear, easing down to the faded alpha when fully blocking.
    float a = lerp(1.0, saturate(_OccluderFadedAlpha), saturate(_OccluderFade) * reach);

    // Fold in whatever the graph already worked out, so one dither covers the lot.
    a *= saturate(AlphaIn);

    // Screen Position (Default) is 0..1 across the target — back to pixels for a stable pattern.
    float2 px  = floor(ScreenPosition * _ScreenParams.xy);
    int2   cel = (int2)fmod(px, 4.0);
    float  threshold = OCCLUDER_BAYER4[cel.y * 4 + cel.x];

    Alpha       = a;
    DitherAlpha = step(threshold, a);
}

// Half-precision variant. Shader Graph appends _float or _half to the function name based on the
// node/graph precision; a File custom function must supply whichever it asks for.
void OccluderFade_half(
    half  AlphaIn,
    half2 ScreenPosition,
    out half Alpha,
    out half DitherAlpha)
{
    float a, d;
    OccluderFade_float(AlphaIn, ScreenPosition, a, d);
    Alpha       = (half)a;
    DitherAlpha = (half)d;
}
