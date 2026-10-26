using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// River width overrides: a run whose channel is wider or narrower at some of its nodes.
///
/// The profile carries the width at every node of the run's path (<see cref="RiverProfile.widthKeys"/>).
/// Each node is placed on the run's centreline, as a walkway's or a rim node's is, and the width
/// eases from one node's to the next along the run — smoothly, so it is level at every node and a
/// rim node, bridge or archway standing on one sees the same width right across it.
///
/// The section keeps its columns. The channel's columns are spread to the new width and the rim's
/// are carried out with its edge, so every point of the section — the channel floor, the rim's
/// corners, the waterline — keeps its place on the shape; <see cref="AcrossAt"/> is that move.
/// </summary>
public static partial class RiverMeshBuilder
{
    /// <summary>The easing from one node's width to the next: level at both.</summary>
    public static float EaseWidth(float from, float to, float t)
    {
        t = Mathf.Clamp01(t);
        return Mathf.Lerp(from, to, t * t * (3f - 2f * t));
    }

    /// <summary>
    /// The channel's width at each of <paramref name="centres"/> — or null when the run is its
    /// profile's one width the whole way, so everything built from it is built exactly as before.
    /// </summary>
    public static float[] InnerWidthsAlong(RiverProfile profile, IList<Vector3> centres)
    {
        var keys = profile?.widthKeys;
        if (keys == null || keys.Length == 0 || centres == null || centres.Count == 0) return null;

        bool varies = false;
        foreach (var k in keys)
            if (Mathf.Abs(k.innerWidth - profile.innerWidth) > 1e-5f) { varies = true; break; }
        if (!varies) return null;

        int n   = centres.Count;
        var arc = new float[n];
        for (int j = 1; j < n; j++) arc[j] = arc[j - 1] + Vector3.Distance(centres[j - 1], centres[j]);

        // Each node at the place on the centreline nearest it, seen from above.
        var placed = new List<(float s, float w)>(keys.Length);
        foreach (var k in keys)
            placed.Add((ArcNearest(centres, arc, k.at), Mathf.Max(0.001f, k.innerWidth)));
        placed.Sort((a, b) => a.s.CompareTo(b.s));

        var widths = new float[n];
        int at = 0;
        for (int j = 0; j < n; j++)
        {
            float s = arc[j];
            while (at < placed.Count - 1 && placed[at + 1].s <= s) at++;

            if (s <= placed[0].s)                     { widths[j] = placed[0].w; continue; }
            if (at >= placed.Count - 1)               { widths[j] = placed[placed.Count - 1].w; continue; }

            var   a    = placed[at];
            var   b    = placed[at + 1];
            float span = b.s - a.s;
            widths[j]  = span < 1e-6f ? b.w : EaseWidth(a.w, b.w, (s - a.s) / span);
        }
        return widths;
    }

    // How far along the line through the centres the point nearest at lies, seen from above.
    private static float ArcNearest(IList<Vector3> centres, float[] arc, Vector3 at)
    {
        float best = float.MaxValue, s = 0f;
        for (int j = 0; j < centres.Count - 1; j++)
        {
            Vector3 a = centres[j];
            Vector3 d = centres[j + 1] - a;
            float len2 = d.x * d.x + d.z * d.z;
            float t    = len2 < 1e-12f
                       ? 0f
                       : Mathf.Clamp01(((at.x - a.x) * d.x + (at.z - a.z) * d.z) / len2);

            float px = a.x + d.x * t - at.x, pz = a.z + d.z * t - at.z;
            float dd = px * px + pz * pz;
            if (dd >= best) continue;

            best = dd;
            s    = Mathf.Lerp(arc[j], arc[j + 1], t);
        }
        return s;
    }

    /// <summary>
    /// Where a point <paramref name="x"/> across the profile's own section lands on a section whose
    /// channel is <paramref name="halfInner"/> wide each side instead of <paramref name="baseHalfInner"/>:
    /// spread with the channel inside it, carried out with the rim beyond it.
    /// </summary>
    public static float AcrossAt(float x, float baseHalfInner, float halfInner)
    {
        if (Mathf.Abs(halfInner - baseHalfInner) < 1e-7f) return x;

        float a = Mathf.Abs(x);
        if (a <= baseHalfInner)
            return baseHalfInner > 1e-7f ? x * (halfInner / baseHalfInner) : x;
        return x + Mathf.Sign(x) * (halfInner - baseHalfInner);
    }
}
