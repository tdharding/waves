#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

// ============================================================
// PRIMARY POINT LIGHT (uses Additional Light 0)
// ============================================================

void PrimaryPointLight_float(
    float3 WorldPos,
    out float3 Direction,
    out float3 Color,
    out float DistanceAtten,
    out float ShadowAtten
)
{
#if SHADERGRAPH_PREVIEW
    Direction = normalize(float3(0, 1, 0));
    Color = 1;
    DistanceAtten = 1;
    ShadowAtten = 1;
#else
    int lightCount = GetAdditionalLightsCount();

    if (lightCount > 0)
    {
        Light light = GetAdditionalLight(0, WorldPos);

        Direction = light.direction;
        Color = light.color;
        DistanceAtten = light.distanceAttenuation;
        ShadowAtten = light.shadowAttenuation;
    }
    else
    {
        Direction = 0;
        Color = 0;
        DistanceAtten = 0;
        ShadowAtten = 1;
    }
#endif
}

void PrimaryPointLight_half(
    half3 WorldPos,
    out half3 Direction,
    out half3 Color,
    out half DistanceAtten,
    out half ShadowAtten
)
{
#if SHADERGRAPH_PREVIEW
    Direction = normalize(half3(0, 1, 0));
    Color = 1;
    DistanceAtten = 1;
    ShadowAtten = 1;
#else
    int lightCount = GetAdditionalLightsCount();

    if (lightCount > 0)
    {
        Light light = GetAdditionalLight(0, WorldPos);

        Direction = light.direction;
        Color = light.color;
        DistanceAtten = light.distanceAttenuation;
        ShadowAtten = light.shadowAttenuation;
    }
    else
    {
        Direction = 0;
        Color = 0;
        DistanceAtten = 0;
        ShadowAtten = 1;
    }
#endif
}

// ============================================================
// MAIN LIGHT COMPATIBILITY WRAPPER (IMPORTANT)
// Shader Graph still calls MainLight_*
// ============================================================

void MainLight_float(
    float3 WorldPos,
    out float3 Direction,
    out float3 Color,
    out float DistanceAtten,
    out float ShadowAtten
)
{
    PrimaryPointLight_float(
        WorldPos,
        Direction,
        Color,
        DistanceAtten,
        ShadowAtten
    );
}

void MainLight_half(
    half3 WorldPos,
    out half3 Direction,
    out half3 Color,
    out half DistanceAtten,
    out half ShadowAtten
)
{
    PrimaryPointLight_half(
        WorldPos,
        Direction,
        Color,
        DistanceAtten,
        ShadowAtten
    );
}

// ============================================================
// DIRECT SPECULAR
// ============================================================

void DirectSpecular_float(
    float3 Specular,
    float Smoothness,
    float3 Direction,
    float3 Color,
    float3 WorldNormal,
    float3 WorldView,
    out float3 Out
)
{
#if SHADERGRAPH_PREVIEW
    Out = 0;
#else
    Smoothness = exp2(10 * Smoothness + 1);
    WorldNormal = normalize(WorldNormal);
    WorldView = SafeNormalize(WorldView);

    Out = LightingSpecular(
        Color,
        Direction,
        WorldNormal,
        WorldView,
        float4(Specular, 0),
        Smoothness
    );
#endif
}

void DirectSpecular_half(
    half3 Specular,
    half Smoothness,
    half3 Direction,
    half3 Color,
    half3 WorldNormal,
    half3 WorldView,
    out half3 Out
)
{
#if SHADERGRAPH_PREVIEW
    Out = 0;
#else
    Smoothness = exp2(10 * Smoothness + 1);
    WorldNormal = normalize(WorldNormal);
    WorldView = SafeNormalize(WorldView);

    Out = LightingSpecular(
        Color,
        Direction,
        WorldNormal,
        WorldView,
        half4(Specular, 0),
        Smoothness
    );
#endif
}

// ============================================================
// ADDITIONAL LIGHTS (unchanged)
// ============================================================

void AdditionalLights_float(
    float3 SpecColor,
    float Smoothness,
    float3 WorldPosition,
    float3 WorldNormal,
    float3 WorldView,
    out float3 Diffuse,
    out float3 Specular
)
{
    float3 diffuseColor = 0;
    float3 specularColor = 0;

#ifndef SHADERGRAPH_PREVIEW
    Smoothness = exp2(10 * Smoothness + 1);
    WorldNormal = normalize(WorldNormal);
    WorldView = SafeNormalize(WorldView);

    int pixelLightCount = GetAdditionalLightsCount();
    for (int i = 0; i < pixelLightCount; ++i)
    {
        Light light = GetAdditionalLight(i, WorldPosition);
        float3 attenuatedLightColor =
            light.color * (light.distanceAttenuation * light.shadowAttenuation);

        diffuseColor += LightingLambert(
            attenuatedLightColor,
            light.direction,
            WorldNormal
        );

        specularColor += LightingSpecular(
            attenuatedLightColor,
            light.direction,
            WorldNormal,
            WorldView,
            float4(SpecColor, 0),
            Smoothness
        );
    }
#endif

    Diffuse = diffuseColor;
    Specular = specularColor;
}

void AdditionalLights_half(
    half3 SpecColor,
    half Smoothness,
    half3 WorldPosition,
    half3 WorldNormal,
    half3 WorldView,
    out half3 Diffuse,
    out half3 Specular
)
{
    half3 diffuseColor = 0;
    half3 specularColor = 0;

#ifndef SHADERGRAPH_PREVIEW
    Smoothness = exp2(10 * Smoothness + 1);
    WorldNormal = normalize(WorldNormal);
    WorldView = SafeNormalize(WorldView);

    int pixelLightCount = GetAdditionalLightsCount();
    for (int i = 0; i < pixelLightCount; ++i)
    {
        Light light = GetAdditionalLight(i, WorldPosition);
        half3 attenuatedLightColor =
            light.color * (light.distanceAttenuation * light.shadowAttenuation);

        diffuseColor += LightingLambert(
            attenuatedLightColor,
            light.direction,
            WorldNormal
        );

        specularColor += LightingSpecular(
            attenuatedLightColor,
            light.direction,
            WorldNormal,
            WorldView,
            half4(SpecColor, 0),
            Smoothness
        );
    }
#endif

    Diffuse = diffuseColor;
    Specular = specularColor;
}

#endif
