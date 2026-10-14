using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The level select landscape's shape: how far the hills lift (or sink) the tile surface at a
/// point, the rocky noise on them, and the river edges the ground is drawn up to and cut round.
///
/// The ONE place this maths lives. <see cref="LandscapeTool"/> bakes the tile meshes from it, and
/// spikes stand on <see cref="At"/>, so both always agree with the ground you see. The only GPU
/// side left is the noise lean's grain (<c>LandscapeBakedNoise</c> in CalculateHills.hlsl), whose
/// noise must stay a line-for-line copy of <see cref="GradientNoise"/> — integer hash, no sin.
///
/// Near a run whose path has Landscape Influence, the ground is drawn up to just under that run's
/// rim top, and eases back to the hills over the path's Influence Distance. The rocky noise fades
/// out the same way, so the ground meets the rim clean. Inside every run and pool it is cut away.
/// </summary>
public static class LandscapeHillHeight
{
    public const int MaxHills = 100;

    /// <summary>
    /// How far under a run's rim top the lifted ground sits. The landscape reaches a little way in
    /// under the rim (<see cref="RiverEdgeTuck"/>) so no gap shows at the edge, and sitting just
    /// below the rim keeps the two from flickering there.
    ///
    /// A run with its outer walls off is the exception: it has no wall under its edge to hide
    /// that step, so its ground is lifted to the rim top exactly, and held level a SHELF past the
    /// edge before it eases off (see <see cref="LandscapeTool.riverEdgeShelf"/>) — the tile is
    /// planar between vertices, so ground already easing away inside the cell the edge falls in
    /// would sag below the rim right at it. The run wears the landscape's own material, so where
    /// the two overlap under the rim they shade the same and there is nothing to flicker.
    /// </summary>
    public const float RiverEdgeUnderRim = 0.02f;

    /// <summary>How far inside a run's outer edge the landscape is cut away.</summary>
    public const float RiverEdgeTuck = 0.05f;

    /// <summary>The step the slope is measured over for the lighting normal, in metres.</summary>
    public const float NormalStep = 0.5f;

    /// <summary>
    /// Everything the ground is shaped by: the hills (as <see cref="LandscapeTool"/> holds them)
    /// and the river edges. The near-lists, when set, limit sampling to the hills and river lines
    /// that can reach what is being sampled (one tile) — anything left out must be too far away
    /// to change the answer.
    /// </summary>
    public sealed class Surface
    {
        public readonly Vector4[] hills     = new Vector4[MaxHills];   // xyz centre (y: height), w radius
        public readonly float[]   sharpness = new float[MaxHills];     // 1 - smoothness
        public readonly float[]   noise     = new float[MaxHills];     // rocky noise amount
        public int   hillCount;
        public float globalHeight = 1f;
        public float noiseScale   = 0.25f;

        // See LandscapeTool.riverEdgePoints for the layout.
        public IList<Vector4> edgePoints;
        public IList<float>   edgeReach;
        public IList<float>   edgeStrength;
        public IList<float>   edgeShelf;
        public int            edgeCount;

        public List<int> nearHills;   // hills that can reach
        public List<int> nearLifts;   // river lines whose lift can reach
        public List<int> nearCuts;    // river lines whose cut can reach

        /// <summary>The river line starting at point i runs to the next point, or is a disc.</summary>
        public Vector4 EdgeEnd(int i)
        {
            Vector4 a = edgePoints[i];
            return a.w > 0f && i + 1 < edgeCount ? edgePoints[i + 1] : a;
        }

        /// <summary>
        /// The smooth height above <paramref name="baseY"/> (the tile base, in world space — a rim
        /// is a world height): hills, then drawn to the river rims. No noise — this is what the
        /// lighting normal is measured on, so the light stays clean.
        /// </summary>
        public float SmoothHeight(Vector2 xz, float baseY)
        {
            float total = 0f;
            int   n     = nearHills != null ? nearHills.Count : hillCount;
            for (int k = 0; k < n; k++)
            {
                int i = nearHills != null ? nearHills[k] : k;
                total += Falloff(i, xz) * hills[i].y * globalHeight;
            }

            float w = Lift(xz, out float rimY);
            return Mathf.Lerp(total, rimY - baseY, w);
        }

        /// <summary>
        /// The rocky noise here:
        ///   x  the height it pushes the geometry, in metres
        ///   y  the lean without its grain: the lean (-1 fully down, +1 fully up) is the noise
        ///      value here times this. Kept apart so the grain can be drawn per pixel.
        ///   z  how much noise there is here at all, 0 where the hills carry none
        /// The lean is the push measured against how far it COULD have pushed here, so it reads the
        /// same on a tall hill and a shallow one.
        /// </summary>
        public Vector3 Noise(Vector2 xz)
        {
            float lifted = 0f, potential = 0f, amount = 0f;
            int   n      = nearHills != null ? nearHills.Count : hillCount;
            for (int k = 0; k < n; k++)
            {
                int i = nearHills != null ? nearHills[k] : k;
                if (noise[i] <= 0f) continue;

                float falloff = Falloff(i, xz);
                float lift    = falloff * noise[i] * hills[i].y * globalHeight;
                lifted    += lift;
                potential += Mathf.Abs(lift);   // abs: a hole's height is negative
                amount    += falloff * noise[i];
            }

            float grain = GradientNoise(xz * noiseScale) * 2f - 1f;
            return new Vector3(grain * lifted, lifted / Mathf.Max(potential, 1e-5f), Mathf.Clamp01(amount));
        }

        /// <summary>Metres above <paramref name="baseY"/> the surface stands: smooth height plus the noise, held off near a rim.</summary>
        public float Height(Vector2 xz, float baseY)
            => SmoothHeight(xz, baseY) + Noise(xz).x * (1f - Lift(xz, out _));

        /// <summary>The lighting normal, from the smooth height's slope.</summary>
        public Vector3 Normal(Vector2 xz, float baseY)
        {
            float h0 = SmoothHeight(xz, baseY);
            float hX = SmoothHeight(xz + new Vector2(NormalStep, 0f), baseY);
            float hZ = SmoothHeight(xz + new Vector2(0f, NormalStep), baseY);

            var tangentX = new Vector3(NormalStep, hX - h0, 0f);
            var tangentZ = new Vector3(0f, hZ - h0, NormalStep);
            return Vector3.Cross(tangentZ, tangentX).normalized;
        }

        /// <summary>
        /// How much the river edges take the ground over here: 1 inside a run (or pool) whose path
        /// has Landscape Influence and out to its outer edge, easing to 0 over the Influence
        /// Distance, 0 beyond. <paramref name="rimY"/> is the height the ground is drawn to by the
        /// one that holds it most: its rim top, less how far under the rim it sits.
        /// </summary>
        public float Lift(Vector2 xz, out float rimY)
        {
            rimY = 0f;
            float best = 0f;
            int   n    = nearLifts != null ? nearLifts.Count : edgeCount;
            for (int k = 0; k < n; k++)
            {
                int   i = nearLifts != null ? nearLifts[k] : k;
                float r = edgeReach[i];
                if (r < 0f) continue;

                Vector4 a = edgePoints[i];
                Vector4 b = EdgeEnd(i);

                float shelf   = LandscapeTool.RiverEdgeShelfAt(edgeShelf, i);
                float outside = Outside(a, b, xz, out float t) - Mathf.Max(shelf, 0f);
                float w       = outside <= 0f ? 1f
                              : r > 0f       ? 1f - HlslSmoothstep(0f, r, outside)
                              :                0f;
                w *= LandscapeTool.RiverEdgeStrengthAt(edgeStrength, i);
                if (w > best)
                {
                    best = w;
                    rimY = Mathf.Lerp(a.y, b.y, t) - (shelf >= 0f ? 0f : RiverEdgeUnderRim);
                }
            }
            return best;
        }

        /// <summary>
        /// How far the ground at <paramref name="xz"/> is from being cut: positive where it stays,
        /// negative inside a run or pool (past the tuck under its rim). Large where none is near.
        /// </summary>
        public float Cut(Vector2 xz)
        {
            float nearest = float.MaxValue;
            int   n       = nearCuts != null ? nearCuts.Count : edgeCount;
            for (int k = 0; k < n; k++)
            {
                int i = nearCuts != null ? nearCuts[k] : k;
                nearest = Mathf.Min(nearest, Outside(edgePoints[i], EdgeEnd(i), xz, out _));
            }
            return nearest == float.MaxValue ? nearest : nearest + RiverEdgeTuck;
        }

        float Falloff(int i, Vector2 xz)
        {
            Vector4 hill   = hills[i];
            float   radius = hill.w;
            float   d      = Vector2.Distance(xz, new Vector2(hill.x, hill.z));
            float   t      = radius > 0f ? Mathf.Clamp01(d / radius) : 1f;
            float   edge0  = Mathf.Min(Mathf.Clamp01(sharpness[i]), 0.999f);
            return 1f - HlslSmoothstep(edge0, 1f, t);
        }
    }

    /// <summary>Metres the surface at <paramref name="xz"/> sits above the tile base.</summary>
    public static float At(IList<LevelSelectDesignerData.LandscapeHillPoint> hills, Vector2 xz,
                           float globalHeight, float noiseScale)
        => At(hills, xz, globalHeight, noiseScale, null, null, null, 0f);

    /// <summary>
    /// Metres the surface at <paramref name="xz"/> sits above the tile base, with the river
    /// edges drawn from <see cref="LandscapeTool"/>. <paramref name="baseY"/> is the tile base in
    /// world space, since a rim's height is a world height.
    /// </summary>
    public static float At(IList<LevelSelectDesignerData.LandscapeHillPoint> hills, Vector2 xz,
                           float globalHeight, float noiseScale,
                           IList<Vector4> edgePoints, IList<float> edgeReach,
                           IList<float> edgeStrength, float baseY,
                           IList<float> edgeShelf = null)
        => BuildSurface(hills, globalHeight, noiseScale, edgePoints, edgeReach, edgeStrength,
                        edgeShelf)
               .Height(xz, baseY);

    /// <summary>
    /// The surface the static samplers read, built from the designer's hills and the river edges.
    /// Build once and sample it many times when laying something over a whole area.
    /// </summary>
    public static Surface BuildSurface(IList<LevelSelectDesignerData.LandscapeHillPoint> hills,
                                       float globalHeight, float noiseScale,
                                       IList<Vector4> edgePoints, IList<float> edgeReach,
                                       IList<float> edgeStrength, IList<float> edgeShelf = null)
    {
        var surface = new Surface { globalHeight = globalHeight, noiseScale = noiseScale };

        int count = hills != null ? Mathf.Min(hills.Count, MaxHills) : 0;
        for (int i = 0; i < count; i++)
        {
            var hill = hills[i];
            if (hill == null) continue;   // left at zero radius: no lift
            surface.hills[i]     = new Vector4(hill.positionXZ.x, hill.height, hill.positionXZ.y, hill.scale);
            surface.sharpness[i] = 1f - Mathf.Clamp01(hill.smoothness);
            surface.noise[i]     = Mathf.Clamp01(hill.noise);
        }
        surface.hillCount = count;

        if (edgePoints != null && edgeReach != null)
        {
            surface.edgePoints   = edgePoints;
            surface.edgeReach    = edgeReach;
            surface.edgeStrength = edgeStrength;
            surface.edgeShelf    = edgeShelf;
            surface.edgeCount    = Mathf.Min(Mathf.Min(edgePoints.Count, edgeReach.Count),
                                             LandscapeTool.MaxRiverEdgePoints);
        }

        return surface;
    }

    // How far outside the run between a and b the point is — negative inside — and how far
    // along from a to b its nearest point lies. a == b is a disc.
    static float Outside(Vector4 a, Vector4 b, Vector2 xz, out float t)
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
    static float HlslSmoothstep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    static float Hash(float ipx, float ipy)
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

    static Vector2 Grad(float ipx, float ipy)
    {
        float ang = Hash(ipx, ipy) * 6.2831853f;
        return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
    }

    // Gradient noise remapped to ~[0,1] — _HillGradientNoise in CalculateHills.hlsl.
    static float GradientNoise(Vector2 p)
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
