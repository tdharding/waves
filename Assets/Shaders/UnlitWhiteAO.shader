// Flat unlit colour (white by default) darkened by URP's Screen Space Ambient Occlusion.
// Needs the SSAO renderer feature (on in PC_Renderer). The DepthNormals pass is what lets
// this object feed the AO texture; without it the object gets no AO and casts none.
Shader "Waves/UnlitWhiteAO"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _AOStrength ("AO Strength", Range(0, 10)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float  _AOStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "UnlitWhiteAO"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionHCS);

                // Returns 1 when SSAO is off, so the shader just renders flat colour then.
                half ao = GetScreenSpaceAmbientOcclusion(screenUV).indirectAmbientOcclusion;
                // Above 1 this pushes past the SSAO value, so clamp to keep it from going negative.
                ao = saturate(lerp(1.0, ao, _AOStrength));

                return half4(_Color.rgb * ao, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionHCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                return OUT;
            }

            half frag (Varyings IN) : SV_Target { return IN.positionHCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionHCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 normalWS = NormalizeNormalPerPixel(IN.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octUV = PackNormalOctRectEncode(normalWS) * 0.5 + 0.5;
                return half4(PackFloat2To888(saturate(octUV)), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
