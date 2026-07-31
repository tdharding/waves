// Shared UI material for procedurally-drawn Map UI elements (spline-wall lines,
// spline node circles, cube-building quads, and — later — generated prefab icons).
//
// Look: a per-quad horizontal gradient (LEFT = light, RIGHT = dark, driven by uv.x)
// multiplied over a designer-assigned rock texture. Unlit, double-sided, alpha-blended
// so it reads cleanly as an overlay on the world-space map surface.
Shader "Waves/MapProcedural"
{
    Properties
    {
        [MainTexture] _RockTex ("Rock Texture", 2D) = "white" {}
        _LightColor    ("Left / Light Colour", Color) = (0.85, 0.85, 0.82, 1)
        _DarkColor     ("Right / Dark Colour", Color) = (0.18, 0.18, 0.22, 1)
        _GradientPower ("Gradient Power", Range(0.1, 4)) = 1
        _RockStrength  ("Rock Overlay Strength", Range(0, 1)) = 1
        _Alpha         ("Overall Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "MapProcedural"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_RockTex);
            SAMPLER(sampler_RockTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _RockTex_ST;
                float4 _LightColor;
                float4 _DarkColor;
                float  _GradientPower;
                float  _RockStrength;
                float  _Alpha;
            CBUFFER_END

            // Global radial mask — set by UIMapController via Shader.SetGlobal* so map
            // elements fade out beyond the map bounds. Declared OUTSIDE UnityPerMaterial
            // (they're globals, not per-material). _MapMaskRadius <= 0 disables masking.
            float3 _MapMaskCenter;
            float  _MapMaskRadius;
            float  _MapMaskFeather;

            // Global fade-OUT amount, also set by UIMapController (0 = fully visible, 1 = gone).
            // Deliberately inverted so the unset default (globals start at 0) leaves the map
            // fully drawn in any scene that has no controller driving it.
            float  _MapUIFadeOut;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float4 color       : COLOR;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.uv          = IN.uv;
                OUT.color       = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Horizontal gradient: uv.x 0 = light (left), 1 = dark (right).
                float g       = pow(saturate(IN.uv.x), _GradientPower);
                half4 grad    = lerp(_LightColor, _DarkColor, g);

                half3 rock    = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, TRANSFORM_TEX(IN.uv, _RockTex)).rgb;
                half3 rgb     = lerp(grad.rgb, grad.rgb * rock, _RockStrength);

                // Per-vertex tint/alpha — lets generated icons bake shape fades (e.g. spike base).
                rgb          *= IN.color.rgb;
                float alpha   = grad.a * _Alpha * IN.color.a * saturate(1.0 - _MapUIFadeOut);

                // Radial mask: fade to nothing past _MapMaskRadius (world distance from centre).
                if (_MapMaskRadius > 0.0)
                {
                    float d    = distance(IN.positionWS, _MapMaskCenter);
                    float mask = 1.0 - smoothstep(_MapMaskRadius - max(_MapMaskFeather, 1e-4), _MapMaskRadius, d);
                    alpha     *= mask;
                    clip(alpha - 0.001);
                }

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
