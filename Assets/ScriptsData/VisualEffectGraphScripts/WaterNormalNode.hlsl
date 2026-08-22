// WaterNormalNode.hlsl
// Custom Function node name: WaterNormal
// ─────────────────────────────────────────────────────────────────────────────
// Turns the analytic gradient from WaterSurface into a real surface normal.
//
// VertexDescription.Normal and .Tangent have always been left unconnected on the
// water graph — all shading was a fake Dot() against a normal texture. Now that
// the surface hands back an exact gradient, real normals cost almost nothing.
//
// Object space, on the -90X water plane: +Z is world up, so the normal of a
// height field h(x,y) is normalize(-dh/dx, -dh/dy, 1).
//
// Inputs
//   Gradient        Vector2  from WaterSurface
//   NormalStrength  Float    1 = true surface, <1 flattens, >1 exaggerates
//
// Outputs
//   Normal          Vector3  object-space normal -> Vertex Normal
//   Tangent         Vector3  object-space tangent -> Vertex Tangent
// ─────────────────────────────────────────────────────────────────────────────

#ifndef WATER_NORMAL_NODE_INCLUDED
#define WATER_NORMAL_NODE_INCLUDED

void WaterNormal_float(
    float2 Gradient,
    float  NormalStrength,
    out float3 Normal,
    out float3 Tangent)
{
    float2 g = Gradient * NormalStrength;

    Normal = normalize(float3(-g.x, -g.y, 1.0));

    // Tangent along object X, bent to stay in the surface plane.
    Tangent = normalize(float3(1.0, 0.0, g.x));
}

void WaterNormal_half(
    half2 Gradient,
    half  NormalStrength,
    out half3 Normal,
    out half3 Tangent)
{
    float3 n; float3 t;
    WaterNormal_float((float2)Gradient, (float)NormalStrength, n, t);
    Normal  = (half3)n;
    Tangent = (half3)t;
}

#endif // WATER_NORMAL_NODE_INCLUDED
