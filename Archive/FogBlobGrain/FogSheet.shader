// The fog you actually see. Everything else in the system builds textures offscreen; this is the
// only part that goes through the depth buffer, on a plane sitting just above the waterline.
//
// Being real geometry in the transparent queue is what makes it sort correctly: a spike in front
// of a blob occludes it, a blob in front of a distant rock covers it. Where a spike passes THROUGH
// the sheet there would be a hard cut line at the waterline, so the alpha is faded by the depth
// gap the same way FoamDepth does it for the water's foam.
//
// Shading has no sun to work with — this game lights from InstancedLights plus a darkness falloff
// beyond the boat. Fog uses BOTH terms that function returns, differently:
//   Proximity (radial, no normal term)  drives the BODY. Real fog scatters, brightening as a
//                                       whole near a light rather than having a lit side.
//   Light     (with N.L)                drives the LIP only, picking out top edges so lobes read
//                                       as domed.
//
// A hand-written shader rather than a Shader Graph on purpose: the four fog subgraphs exist
// alongside this, so the same look can be rebuilt visually and art-directed without touching HLSL.
Shader "Waves/Fog/FogSheet"
{
    Properties
    {
        [Header(Shape)]
        _Threshold        ("Threshold", Range(0.05, 0.9)) = 0.26
        _EdgeSoftness     ("Edge Softness", Range(0.002, 0.2)) = 0.03
        _UndulationAmount ("Undulation Amount", Range(0, 0.4)) = 0.10
        _UndulationScale  ("Undulation Scale", Range(0.05, 6)) = 1.2

        [Header(Lip)]
        _LipWidth   ("Lip Width", Range(0.005, 0.3)) = 0.05
        _LipLight   ("Lip Lighting", Range(0, 4)) = 1.6
        _Curvature  ("Lip Curvature", Range(0.05, 4)) = 0.55
        _HeightScale("Height Scale", Range(0.1, 6)) = 1.0

        [Header(Body)]
        _FogColor   ("Fog Colour", Color) = (0.42, 0.50, 0.62, 1)
        _LightColor ("Lit Colour", Color) = (0.88, 0.92, 1.0, 1)
        _Opacity    ("Interior Fill", Range(0, 1)) = 0.75
        _Transparency ("Edge Fade Width", Range(0.02, 1.5)) = 0.25

        [Header(Grain)]
        _GrainAmount ("Grain Amount", Range(0, 2)) = 0.18
        _GrainScale  ("Grain Scale", Range(0.5, 400)) = 12
        _GrainDrift  ("Grain Drift", Range(0, 2)) = 0.3

        [Header(Intersection)]
        _DepthFade ("Soft Intersection", Range(0.01, 2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "FogSheet"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #include "Assets/ScriptsData/VisualEffectGraphScripts/FogSample.hlsl"
            #include "Assets/ScriptsData/VisualEffectGraphScripts/FogShape.hlsl"
            #include "Assets/ScriptsData/VisualEffectGraphScripts/FogNormal.hlsl"
            #include "Assets/ScriptsData/VisualEffectGraphScripts/FogGrain.hlsl"
            #include "Assets/ScriptsData/VisualEffectGraphScripts/InstancedLights.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _Threshold, _EdgeSoftness, _UndulationAmount, _UndulationScale;
                float  _LipWidth, _LipLight, _Curvature, _HeightScale;
                float4 _FogColor, _LightColor;
                float  _Opacity, _Transparency;
                float  _GrainAmount, _GrainScale, _GrainDrift;

                // Bare $Globals, pushed every frame by FogFieldManager.
                float4 _BoatWorldCenter;
                float  _FogMaskRadius;
                float  _FogMaskFeather;
                float  _DepthFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.screenPos  = ComputeScreenPos(p.positionCS);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.positionWS;

                // ── the field ────────────────────────────────────────────────
                float density, blobId, height, inField;
                float2 fieldUV;
                FogSample_float(wp, density, blobId, height, fieldUV, inField);
                clip(inField - 0.5);

                // ── the shape ────────────────────────────────────────────────
                float body, lip, fill;
                FogShape_float(density, blobId, wp,
                               _Threshold, _EdgeSoftness,
                               _UndulationAmount, _UndulationScale, _LipWidth,
                               body, lip, fill);
                clip(body - 0.001);

                // ── the surface ──────────────────────────────────────────────
                float3 normal; float slope;
                FogNormal_float(wp, _HeightScale, _Curvature, normal, slope);

                // ── the light ────────────────────────────────────────────────
                float instLight, instProx;
                InstancedLights_float(wp, normal, instLight, instProx);

                // Body brightens as a whole near a light; only the rim takes the directional term.
                float glow = saturate(instProx);
                float rim  = saturate(instLight) * lip * _LipLight;

                float3 col = lerp(_FogColor.rgb, _LightColor.rgb, saturate(glow + rim));

                // ── grain and alpha ──────────────────────────────────────────
                float grain, thin;
                FogGrain_float(wp, blobId, fill, _GrainAmount, _GrainScale,
                               _Transparency, _Time.y * _GrainDrift, grain, thin);
                col *= grain;

                float alpha = body * thin * _Opacity;

                // ── the boat mask ────────────────────────────────────────────
                // THE MASK LIVES HERE, and only here. It used to be applied on the CPU by
                // multiplying every BaseDot's strength, which made it part of the simulation —
                // and once it was part of the simulation it also sized the painted texture, so
                // widening the area you could see made the fog itself blurrier. It is a fade on
                // a material; this is where a fade on a material belongs.
                //
                // Radius and feather arrive as bare $Globals from FogFieldManager, the same way
                // the soul-fish masks and rock rings do, so a shader reimport self-heals.
                float2 toBoat = wp.xz - _BoatWorldCenter.xz;
                float  dist   = length(toBoat);
                float  inner  = _FogMaskRadius * saturate(1.0 - _FogMaskFeather);
                alpha *= 1.0 - smoothstep(inner, max(_FogMaskRadius, inner + 1e-4), dist);

                // ── soft intersection ────────────────────────────────────────
                // Without this a spike passing through the sheet slices it at the waterline.
                // Same idea as FoamDepth: fade where the scene behind is close to this fragment.
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                float sceneEye  = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float fragEye   = IN.screenPos.w;
                alpha *= saturate((sceneEye - fragEye) / max(_DepthFade, 1e-4));

                return float4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
