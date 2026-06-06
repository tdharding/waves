Shader "Custom/RenderTextureContrast"
{
    Properties
    {
        _MainTex ("Render Texture", 2D) = "white" {}
        _Contrast ("Contrast", Range(0, 3)) = 1.0
        _Brightness ("Brightness", Range(-1, 1)) = 0.0

        // Overlay
        _OverlayTex ("Overlay Texture", 2D) = "white" {}
        _OverlayStrength ("Overlay Strength", Range(0, 1)) = 0.0
        _OverlayMode ("Overlay Mode (0=Multiply, 1=Add)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _MainTex;
            sampler2D _OverlayTex;

            float _Contrast;
            float _Brightness;
            float _OverlayStrength;
            float _OverlayMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 baseCol = tex2D(_MainTex, i.uv).rgb;

                // Contrast
                baseCol = (baseCol - 0.5) * _Contrast + 0.5;

                // Brightness
                baseCol += _Brightness;

                // Overlay
                float3 overlayCol = tex2D(_OverlayTex, i.uv).rgb;

                float3 multiplied = baseCol * overlayCol;
                float3 added = baseCol + overlayCol;

                float3 overlayResult = lerp(multiplied, added, _OverlayMode);

                baseCol = lerp(baseCol, overlayResult, _OverlayStrength);

                return float4(baseCol, 1);
            }
            ENDCG
        }
    }
}
