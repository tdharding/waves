// Fish bowl shell for the FishBowlTower.
// Renders BACK FACES only (Cull Front) as flat black — you see through the near side
// onto the black far interior, framing the fish inside the bowl.
Shader "Waves/FishBowlBackface"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
        }

        Pass
        {
            Name "FishBowlBackface"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return half4(_Color.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
