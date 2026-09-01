// Paints BaseDots into the fog grid. Never drawn to the screen — both passes render into small
// offscreen textures that the fog sheet's material reads later.
//
// Driven by FogFieldManager.Paint() via CommandBuffer.DrawProcedural: six verts and one instance
// per dot, no mesh and no per-dot object. The vertex shader writes clip space directly, so there
// are no view or projection matrices involved — the grid is a flat world-space window, not a view.
//
// Pass 0  density and blob id, additively blended
// Pass 1  sphere height, MAX blended — a union of domes, not a sum of them
Shader "Waves/Fog/FogPaint"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "UnityCG.cginc"

        // Must match FogDotGPU in FogFieldManager.cs — the two are a matched pair, and changing
        // one without the other silently corrupts every dot rather than failing loudly.
        struct FogDotGPU
        {
            float2 pos;       // world XZ
            float2 axis;      // unit direction along its chain
            float  radius;    // world units, across the chain
            float  stretch;   // 1 is round; higher draws it out along axis
            float  height;    // sphere-cap height, after squash and height undulation
            float  strength;  // 0..1 melt
            float  blobId;    // 0..1
        };

        StructuredBuffer<FogDotGPU> _FogDots;
        float4 _FogFieldOrigin;   // xy = world min corner, z = 1/size, w = size

        // How far past the dot's radius the quad reaches, so the gaussian tail is not clipped
        // into a visible square edge.
        #define FOG_DOT_MARGIN 2.4

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 local      : TEXCOORD0;   // position in the dot's own space, in radii
            float4 dot        : TEXCOORD1;   // radius, strength, blobId, height
        };

        Varyings PaintVert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
        {
            FogDotGPU d = _FogDots[instanceID];

            // Two triangles, as a corner offset in -1..1.
            const float2 corners[6] =
            {
                float2(-1, -1), float2(1, -1), float2(-1, 1),
                float2( 1, -1), float2(1,  1), float2(-1, 1)
            };
            float2 c = corners[vertexID];

            float2 along  = d.axis;
            float2 across = float2(-d.axis.y, d.axis.x);

            // The quad is drawn out along the chain by the same stretch the falloff uses, so an
            // elliptical dot covers the ground several round ones would have.
            float2 extent = float2(d.radius * d.stretch, d.radius) * FOG_DOT_MARGIN;
            float2 world  = d.pos + along * (c.x * extent.x) + across * (c.y * extent.y);

            float2 uv = (world - _FogFieldOrigin.xy) * _FogFieldOrigin.z;

            Varyings o;
            o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
            #if UNITY_UV_STARTS_AT_TOP
                o.positionCS.y = -o.positionCS.y;
            #endif
            o.local = c * FOG_DOT_MARGIN;
            o.dot   = float4(d.radius, d.strength, d.blobId, d.height);
            return o;
        }

        // Distance from the dot's centre in radii, already accounting for the stretch — the quad
        // corners were laid out in the same space, so this is just the length of `local`.
        float FogDotFalloff(float2 local)
        {
            float r = length(local);
            // Same gaussian the Python runs used: sigma 0.55 of the radius. Softer overlaps
            // further and needs fewer dots; harder holds the shape but beads more easily.
            return exp(-(r * r) / (2.0 * 0.55 * 0.55));
        }
        ENDHLSL

        // ── Pass 0: density and blob id ─────────────────────────────────────────
        //
        // Blob id is written PRE-MULTIPLIED by density and recovered downstream as G/R. Additive
        // blending would otherwise sum the ids of overlapping blobs into nonsense; weighting them
        // this way makes the recovered id a density-weighted average, which blends sensibly right
        // where two masses fuse.
        Pass
        {
            Name "FogDensity"
            Blend One One
            BlendOp Add

            HLSLPROGRAM
            #pragma vertex PaintVert
            #pragma fragment frag
            #pragma target 4.5

            float4 frag(Varyings i) : SV_Target
            {
                float density = FogDotFalloff(i.local) * i.dot.y;
                return float4(density, density * i.dot.z, 0.0, 0.0);
            }
            ENDHLSL
        }

        // ── Pass 1: sphere height ───────────────────────────────────────────────
        //
        // MAX rather than ADD. Heights are a union of domes: where two dots overlap the surface
        // is the higher of the two, not the sum, or a densely dotted limb would tower over a
        // sparse one for no reason the shape gives.
        Pass
        {
            Name "FogHeight"
            Blend One One
            BlendOp Max

            HLSLPROGRAM
            #pragma vertex PaintVert
            #pragma fragment frag
            #pragma target 4.5

            float4 frag(Varyings i) : SV_Target
            {
                // A spherical cap rather than the gaussian: domed in the middle and falling to
                // zero at the rim, so neighbouring dots meet at the waterline instead of at a
                // ledge. Squash is already baked into `height` by the preset's curve.
                float r = saturate(length(i.local) / FOG_DOT_MARGIN);
                float cap = sqrt(max(1.0 - r * r, 0.0));
                float h = cap * i.dot.w * i.dot.y;
                return float4(h, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
