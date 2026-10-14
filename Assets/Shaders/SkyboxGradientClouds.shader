// Skybox for SkyboxPreset (Tools > Level Design > Skybox Generator).
//
// The sky colour is a gradient strip read by elevation, bottom of the sky to the top. Clouds are
// placed live: the generator writes each cloud's spot into _CloudData and this shader draws each
// one as a flat card facing the centre of the sky, cut from _CloudAtlas. Later clouds in the list
// draw over earlier ones. The whole cloud layer turns about the vertical at _PanSpeed.
//
// _CloudData, one column per cloud:
//   row 0 = (azimuth rad, elevation rad, half width tan, half height tan)
//   row 1 = (atlas cell, -, -, -)
Shader "Waves/Skybox/Gradient Clouds"
{
    Properties
    {
        _GradientTex ("Gradient", 2D) = "white" {}
        _CloudAtlas  ("Cloud Atlas", 2D) = "black" {}
        _CloudData   ("Cloud Data", 2D) = "black" {}
        _CloudCount  ("Cloud Count", Float) = 0
        _AtlasGrid   ("Atlas Grid", Float) = 1
        _AtlasPad    ("Atlas Pad", Float) = 0
        _PanSpeed    ("Pan Speed (deg/s)", Float) = 1
        _CloudOpacity("Cloud Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Background"
            "RenderType"     = "Background"
            "PreviewType"    = "Skybox"
        }

        Pass
        {
            Name "SkyboxGradientClouds"
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_CLOUDS 64

            TEXTURE2D(_GradientTex);  SAMPLER(sampler_GradientTex);
            TEXTURE2D(_CloudAtlas);   SAMPLER(sampler_CloudAtlas);
            TEXTURE2D(_CloudData);

            CBUFFER_START(UnityPerMaterial)
                float4 _GradientTex_ST;
                float4 _CloudAtlas_ST;
                float4 _CloudData_ST;
                float  _CloudCount;
                float  _AtlasGrid;
                float  _AtlasPad;
                float  _PanSpeed;
                float  _CloudOpacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir        = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);

                // Elevation, not height: even steps of the gradient cover even angles of sky.
                float t = asin(clamp(d.y, -1.0, 1.0)) / PI + 0.5;
                float3 col = SAMPLE_TEXTURE2D_LOD(_GradientTex, sampler_GradientTex, float2(t, 0.5), 0).rgb;

                // Turning the direction we look up from turns the cloud layer about the vertical.
                float s, c;
                sincos(radians(_PanSpeed * _Time.y), s, c);
                float3 r = float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);

                int   count = (int)_CloudCount;
                float grid  = max(_AtlasGrid, 1.0);

                [loop]
                for (int i = 0; i < MAX_CLOUDS; i++)
                {
                    if (i >= count) break;

                    float4 p    = LOAD_TEXTURE2D(_CloudData, int2(i, 0));
                    float  cell = LOAD_TEXTURE2D(_CloudData, int2(i, 1)).x;

                    float ce, se, ca, sa;
                    sincos(p.y, se, ce);
                    sincos(p.x, sa, ca);
                    float3 fwd   = float3(sa * ce, se, ca * ce);
                    float3 right = float3(ca, 0.0, -sa);
                    float3 up    = cross(fwd, right);

                    float fd = dot(r, fwd);
                    if (fd <= 0.0) continue;

                    float2 uv = float2(dot(r, right), dot(r, up)) / (fd * p.zw) * 0.5 + 0.5;
                    if (any(uv < 0.0) || any(uv > 1.0)) continue;

                    float2 cellXY = float2(fmod(cell, grid), floor(cell / grid));
                    float2 auv    = (cellXY + lerp(_AtlasPad, 1.0 - _AtlasPad, uv)) / grid;
                    half4  cloud  = SAMPLE_TEXTURE2D_LOD(_CloudAtlas, sampler_CloudAtlas, auv, 0);

                    col = lerp(col, cloud.rgb, cloud.a * _CloudOpacity);
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
