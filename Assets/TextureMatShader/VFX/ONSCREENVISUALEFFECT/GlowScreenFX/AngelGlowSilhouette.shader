// Drawn by GlowFXRendererFeature over every renderer on the Glow rendering layer: plain white into
// the angel glow silhouette. Depth-tested against the scene so whatever stands in front cuts it.
Shader "Hidden/AngelGlowSilhouette"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "AngelGlowSilhouette"
            ZWrite Off
            ZTest LEqual
            Cull Off
            // Pulled toward the camera so the angel never z-fights its own depth.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 1; }
            ENDHLSL
        }
    }
}
