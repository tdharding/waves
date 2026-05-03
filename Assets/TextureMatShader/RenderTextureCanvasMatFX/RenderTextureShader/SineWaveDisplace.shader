Shader "Unlit/SineWaveDisplaceChunky"
{
    Properties
    {
        _MainTex ("Render Texture", 2D) = "white" {}
        _Strength ("Wave Strength", Range(0, 0.1)) = 0.02
        _Frequency ("Wave Frequency", Range(0, 20)) = 8
        _Speed ("Wave Speed", Range(0, 10)) = 2
        _BlockSize ("Pixel Block Size", Float) = 64 // UNLIMITED
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Opaque" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float _Strength;
            float _Frequency;
            float _Speed;
            float _BlockSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Quantize UVs into block-size chunks
                float2 blockUV = floor(i.uv * _BlockSize) / _BlockSize;

                // Sine wave applied to quantized Y
                float wave = sin(blockUV.y * _Frequency + _Time.y * _Speed);

                float2 displacedUV = blockUV;
                displacedUV.x += wave * _Strength;

                displacedUV = clamp(displacedUV, 0.001, 0.999);

                return tex2D(_MainTex, displacedUV);
            }

            ENDHLSL
        }
    }
}
