using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How far the landscape's hills lift (or sink) the tile surface at a point, worked out on the
/// CPU — the same sum <c>CalculateHills.hlsl</c> does in the vertex shader, smooth height plus
/// rocky noise, so something stood on the landscape lands on the surface you actually see.
///
/// A line-for-line mirror: change the shader's hill maths and this must change with it. The
/// shader's noise hash is integer-only precisely so it can be repeated here exactly.
///
/// Reads the designer's hill list directly, in the same order the shader gets it (the first
/// 100), with the landscape's Height Multiplier and noise scale.
///
/// Also mirrors the river edges: near a run whose path has Landscape Influence, the ground is
/// drawn up to just under that run's rim top, and eases back to the hills over the path's
/// Influence Distance. The rocky noise fades out the same way, so the ground meets the rim clean.
/// </summary>
public static class LandscapeHillHeight
{
    private const int MaxHills = 100;

    /// <summary>
    /// How far under a run's rim top the lifted ground sits — RIVER_EDGE_UNDER_RIM in the shader.
    /// The landscape reaches a little way in under the rim (<see cref="RiverEdgeTuck"/>) so no
    /// gap shows at the edge, and sitting just below the rim keeps the two from flickering there.
    /// </summary>
    public const float RiverEdgeUnderRim = 0.02f;

    /// <summary>How far inside a run's outer edge the landscape is cut away — RIVER_EDGE_TUCK.</summary>
    public const float RiverEdgeTuck = 0.05f;

    /// <summary>Metres the surface at <paramref name="xz"/> sits above the tile base.</summary>
    public static float At(IList<LevelSelectDesignerData.LandscapeHillPoint> hills, Vector2 xz,
                           float globalHeight, float noiseScale)
        => At(hills, xz, globalHeight, noiseScale, null, null, 0f);

    /// <summary>
    /// Metres the surface at <paramref name="xz"/> sits above the tile base, with the river
    /// edges drawn from <see cref="LandscapeTool"/>. <paramref name="baseY"/> is the tile base in
    /// world space, since a rim's height is a world height.
    /// </summary>
    public static float At(IList<LevelSelectDesignerData.LandscapeHillPoint> hills, Vector2 xz,
                           float globalHeight, float noiseScale,
                           IList<Vector4> edgePoints, IList<float> edgeReach, float baseY)
    {
        float smooth = 0f;
        float rocky  = 0f;

        int count = hills != null ? Mathf.Min(hills.Count, MaxHills) : 0;

        float n = GradientNoise(xz * noiseScale) * 2f - 1f;

        for (int i = 0; i < count; i++)
        {
            var hill = hills[i];
            if (hill == null) continue;

            float radius  = hill.scale;
            float d       = Vector2.Distance(xz, hill.positionXZ);
            float t       = radius > 0f ? Mathf.Clamp01(d / radius) : 1f;
            float edge0   = Mathf.Min(1f - Mathf.Clamp01(hill.smoothness), 0.999f);
            float falloff = 1f - HlslSmoothstep(edge0, 1f, t);
            float lift    = falloff * hill.height * globalHeight;

            // Smooth height, then the rocky noise on top — both reach the geometry.
            smooth += lift;
            float noise = Mathf.Clamp01(hill.noise);
            if (noise > 0f) rocky += n * lift * noise;
        }

        float w = RiverEdgeLift(edgePoints, edgeReach, xz, out float rimY);
        return Mathf.Lerp(smooth, rimY - RiverEdgeUnderRim - baseY, w) + rocky * (1f - w);
    }

    /// <summary>
    /// How much the river edges take the ground over at <paramref name="xz"/>: 1 inside a run
    /// (or pool) whose path has Landscape Influence and out to its outer edge, easing to 0 over
    /// the Influence Distance, 0 beyond. <paramref name="rimY"/> is the rim height of the one that
    /// holds it most. Mirrors <c>_RiverEdgeLift</c> in CalculateHills.hlsl.
    /// </summary>
    public static float RiverEdgeLift(IList<Vector4> points, IList<float> reach, Vector2 xz,
                                      out float rimY)
    {
        rimY = 0f;
        if (points == null || reach == null) return 0f;

        int   count = Mathf.Min(Mathf.Min(points.Count, reach.Count), LandscapeTool.MaxRiverEdgePoints);
        float best  = 0f;

        for (int i = 0; i < count; i++)
        {
            float r = reach[i];
            if (r < 0f) continue;

            Vector4 a = points[i];
            Vector4 b = a.w > 0f && i + 1 < count ? points[i + 1] : a;

            float outside = RiverEdgeOutside(a, b, xz, out float t);
            float w       = outside <= 0f ? 1f
                          : r > 0f       ? 1f - HlslSmoothstep(0f, r, outside)
                          :                0f;
            if (w > best)
            {
                best = w;
                rimY = Mathf.Lerp(a.y, b.y, t);
            }
        }
        return best;
    }

    // How far outside the run between a and b the point is — negative inside — and how far
    // along from a to b its nearest point lies. a == b is a disc.
    private static float RiverEdgeOutside(Vector4 a, Vector4 b, Vector2 xz, out float t)
    {
        float abx = b.x - a.x, abz = b.z - a.z;
        float len2 = abx * abx + abz * abz;
        t = len2 > 1e-8f ? Mathf.Clamp01(((xz.x - a.x) * abx + (xz.y - a.z) * abz) / len2) : 0f;

        float cx = a.x + abx * t, cz = a.z + abz * t;
        float dx = xz.x - cx,     dz = xz.y - cz;
        float half = Mathf.Lerp(Mathf.Abs(a.w), Mathf.Abs(b.w), t);
        return Mathf.Sqrt(dx * dx + dz * dz) - half;
    }

    // HLSL's smoothstep. Mathf.SmoothStep is a different function (it interpolates between its
    // first two arguments), so it cannot stand in here.
    private static float HlslSmoothstep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    private static float Hash(float ipx, float ipy)
    {
        unchecked
        {
            uint x = (uint)(int)ipx * 374761393u;
            uint y = (uint)(int)ipy * 668265263u;
            uint h = x + y;
            h = (h ^ (h >> 13)) * 1274126177u;
            h = h ^ (h >> 16);
            return (h & 0x00FFFFFFu) / 16777216f;
        }
    }

    private static Vector2 Grad(float ipx, float ipy)
    {
        float ang = Hash(ipx, ipy) * 6.2831853f;
        return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
    }

    // Gradient noise remapped to ~[0,1], as _HillGradientNoise.
    private static float GradientNoise(Vector2 p)
    {
        float ix = Mathf.Floor(p.x), iy = Mathf.Floor(p.y);
        float fx = p.x - ix,         fy = p.y - iy;
        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);

        float d00 = Vector2.Dot(Grad(ix,      iy),      new Vector2(fx,      fy));
        float d10 = Vector2.Dot(Grad(ix + 1f, iy),      new Vector2(fx - 1f, fy));
        float d01 = Vector2.Dot(Grad(ix,      iy + 1f), new Vector2(fx,      fy - 1f));
        float d11 = Vector2.Dot(Grad(ix + 1f, iy + 1f), new Vector2(fx - 1f, fy - 1f));

        float n = Mathf.Lerp(Mathf.Lerp(d00, d10, ux), Mathf.Lerp(d01, d11, ux), uy);
        return n * 0.7071f + 0.5f;
    }
}
