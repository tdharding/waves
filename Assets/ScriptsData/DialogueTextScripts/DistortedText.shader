Shader "TMP/DistortedText"
{
    Properties
    {
        // Required TMP properties
        _FaceTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width", Range(0,1)) = 0.0
        _CullMode ("Cull Mode", Float) = 0         // <-- REQUIRED by TMP

        // Our properties
        _DisplaceAmount ("Displacement Amount", Float) = 0.02
        _DistortionSpeed ("Distortion Speed", Float) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Lighting Off
        Cull [_CullMode]
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _FaceTex;

            float4 _FaceColor;
            float4 _OutlineColor;
            float  _OutlineWidth;
            float  _CullMode;

            float _DisplaceAmount;
            float _DistortionSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // TMP per-vertex color (essential)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                // Built-in Unity time
                float t = _Time.y * _DistortionSpeed;

                float2 offset = float2(
                    sin(v.uv.y * 20 + t),
                    cos(v.uv.x * 20 + t)
                ) * _DisplaceAmount;

                o.uv = v.uv + offset;
                o.color = v.color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 samp = tex2D(_FaceTex, i.uv);
                float sdf = samp.a;

                float face = smoothstep(0.5, 0.5 + _OutlineWidth, sdf);
                float outline = smoothstep(0.5 - _OutlineWidth, 0.5, sdf);

                float4 faceCol    = face    * _FaceColor;
                float4 outlineCol = outline * (1 - face) * _OutlineColor;

                float4 finalCol = (faceCol + outlineCol);

                // TMP vertex color (alpha, gradients, selection, etc.)
                finalCol *= i.color;

                return finalCol;
            }
            ENDCG
        }
    }

    FallBack "Transparent"
}
