// Sonar fade — physical objects dissolve out while sonar is running, staying solid in a screen-space
// disc around the boat. This is the node chain from SplineWallsShaderGraph (Screen Position → distance
// from the sonar centre → dither-edged smoothstep → × sonar factor) turned into one drop-in function.
//
// Everything comes from globals pushed by SonarSystemController, so no material needs its own
// _SonarCenter / _SonarRadius / _RockSonarFactor / DitherFactor properties — drop the SonarFade
// subgraph into any shader and it works. They are re-pushed every frame, so a shader reimport that
// wipes them self-heals on the next frame (same reasoning as InstancedLights.hlsl).
//
// AlphaIn      : alpha the graph has already worked out. Leave unconnected (default 1) to use the
//                sonar fade on its own.
// ScreenPosition: Screen Position node (Default mode)
// Alpha        : 1 = solid, falling toward 0 as the object fades. Feed this into OccluderFade's
//                AlphaIn so the two effects chain and dither ONCE at the end.
// ClipThreshold: the old value that used to drive Alpha Clip Threshold directly, for materials not
//                migrated to the Alpha chain yet. Do not use both on the same graph.

float4 _SonarFadeCentre;   // .xy = boat position in screen UV (0..1)
float  _SonarFadeRadius;   // screen-space radius that stays solid around the boat
float  _SonarFadeStrength; // 0 = no fade at all, 1 = fully hidden at full distance
float  _SonarFadeDither;   // scales the dither noise used as the dissolve edge

// Ordered 4x4 Bayer thresholds (0..15)/16. Named apart from OccluderFade's copy so a graph can
// include both files without a duplicate symbol.
static const float SONAR_BAYER4[16] =
{
     0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
    12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
     3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
    15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
};

void SonarFade_float(
    float  AlphaIn,
    float2 ScreenPosition,
    out float Alpha,
    out float ClipThreshold)
{
    // How far past the solid disc around the boat this fragment sits. Negative inside it.
    float dist = length(ScreenPosition - _SonarFadeCentre.xy);
    float past = dist - _SonarFadeRadius;

    // Dither noise as the near edge of the smoothstep — this is what makes the dissolve granular
    // rather than a clean ring. Held below 1 so the smoothstep edges never cross.
    float2 px  = floor(ScreenPosition * _ScreenParams.xy);
    int2   cel = (int2)fmod(px, 4.0);
    float  edge = min(SONAR_BAYER4[cel.y * 4 + cel.x] * _SonarFadeDither, 0.999);

    float hide = saturate(_SonarFadeStrength) * smoothstep(edge, 1.0, past);

    ClipThreshold = hide;
    Alpha         = saturate(AlphaIn) * (1.0 - hide);
}

// Half-precision variant. Shader Graph appends _float or _half to the function name based on the
// node/graph precision; a File custom function must supply whichever it asks for.
void SonarFade_half(
    half  AlphaIn,
    half2 ScreenPosition,
    out half Alpha,
    out half ClipThreshold)
{
    float a, c;
    SonarFade_float(AlphaIn, ScreenPosition, a, c);
    Alpha         = (half)a;
    ClipThreshold = (half)c;
}
