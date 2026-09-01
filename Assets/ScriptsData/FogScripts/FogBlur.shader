// Smooths the fog grid and gives it memory. Offscreen only — nothing here reaches the screen.
//
// Pass 0  one axis of a separable gaussian; FogFieldManager runs it twice, horizontally then
//         vertically, which is what turns a scatter of dots into one continuous mass.
// Pass 1  the heaviness blend against last frame. This is what makes fog creep rather than
//         jitter, and it does enough of the smoothing on its own that the blur above can stay
//         narrow. It is also the dial that reads as thick-and-sluggish versus wispy-and-quick.
Shader "Waves/Fog/FogBlur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _FogHistory;
        float4    _FogBlurStep;   // xy = one step along the axis being blurred, in UV
        float     _FogHeaviness;  // 0 = no memory, 0.98 = very sluggish
        float4    _FogHistoryShift; // xy = UV the window moved since last frame

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
        };

        Varyings vert(appdata_img v)
        {
            Varyings o;
            o.positionCS = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }
        ENDHLSL

        // ── Pass 0: separable gaussian ──────────────────────────────────────────
        Pass
        {
            Name "FogBlurAxis"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(Varyings i) : SV_Target
            {
                // Nine taps on a fixed 1-2-3-4 falloff. Width is carried by the step rather than
                // by the weights, so blur radius is one uniform and not a shader variant.
                const float w[5] = { 0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162 };

                float4 sum = tex2D(_MainTex, i.uv) * w[0];
                [unroll]
                for (int k = 1; k < 5; k++)
                {
                    float2 o = _FogBlurStep.xy * k;
                    sum += tex2D(_MainTex, i.uv + o) * w[k];
                    sum += tex2D(_MainTex, i.uv - o) * w[k];
                }
                return sum;
            }
            ENDHLSL
        }

        // ── Pass 1: heaviness ───────────────────────────────────────────────────
        Pass
        {
            Name "FogHeaviness"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(Varyings i) : SV_Target
            {
                float4 now = tex2D(_MainTex, i.uv);

                // REPROJECT. The window follows the boat, so this pixel was somewhere else in
                // last frame's texture and reading it at the same UV would be reading a
                // different piece of water — which drags the fog along behind the boat, and is
                // exactly what made a moving window look broken. The centre is snapped to whole
                // texels, so this offset lands on texel centres and the blend stays a memory.
                float2 hUv = i.uv + _FogHistoryShift.xy;

                // Water that has only just entered the window has no past at all. Sampling
                // outside would clamp, dragging the edge texels inward as a stripe, so those
                // pixels take this frame on its own.
                float inside = (hUv.x >= 0.0 && hUv.x <= 1.0 &&
                                hUv.y >= 0.0 && hUv.y <= 1.0) ? 1.0 : 0.0;

                float4 past = tex2D(_FogHistory, hUv);
                return lerp(now, past, _FogHeaviness * inside);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
