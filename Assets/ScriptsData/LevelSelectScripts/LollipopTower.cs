using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A lollipop tower: a cylinder stem with an orb on top. Stands on installation outposts, and
/// optionally in the middle of a pool's island.
///
///        ___
///       /   \     <- orb (orb radius)
///       \___/
///         |
///         |       <- stem (stem radius, stem height)
///        _|_
///       |___|     <- second base (base 2 radius, base 2 height), 0 height = none
///      /_____\    <- ramp (base radius in to base 2 radius, ramp height), 0 height = none
///     |_______|   <- base (base radius, base height), standing at y = 0
///
/// The orb is seated where the stem's top edge meets its surface, so the stem runs its full
/// height into the orb with no ledge and no gap.
/// </summary>
[Serializable]
public class LollipopTower
{
    [Tooltip("Radius of the cylinder stem.")]
    [Min(0.001f)] public float stemRadius = 0.02f;

    [Tooltip("Height of the cylinder stem, from its base up to where it meets the orb.")]
    [Min(0f)] public float stemHeight = 0.35f;

    [Tooltip("Radius of the orb on top.")]
    [Min(0.001f)] public float orbRadius = 0.1f;

    [Tooltip("Radius of the round base the stem stands on.")]
    [Min(0.001f)] public float baseRadius = 0.05f;

    [Tooltip("Height of the round base. 0 = no base, and the stem stands on the ground.")]
    [Min(0f)] public float baseHeight = 0.03f;

    [Tooltip("Height of the ramp on top of the base, sloping in from the base's radius to the " +
             "second base's radius. 0 = no ramp.")]
    [Min(0f)] public float rampHeight = 0f;

    [Tooltip("Radius of the second base, on top of the ramp — and the radius the ramp slopes in to.")]
    [Min(0.001f)] public float base2Radius = 0.03f;

    [Tooltip("Height of the second base, on top of the ramp. 0 = no second base.")]
    [Min(0f)] public float base2Height = 0f;

    public LollipopTower Clone() => (LollipopTower)MemberwiseClone();

    const int Sides = 24;
    const int OrbRings = 16;

    /// <summary>
    /// The tower on its own, base at the origin, carrying the river run stone shading. Each part
    /// takes the stone colour <paramref name="shading"/> gives it; null leaves them worked out.
    /// </summary>
    public Mesh Build(RiverRunShadingSettings shading = null)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        var kinds = new List<float>();
        var parts = new List<int>();

        Append(verts, norms, uvs, tris, Vector3.zero, kinds, shading, parts);

        var mesh = new Mesh { name = "LollipopTower" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // River run stone shading, read off its base, with each part's colour as the settings say.
        return RiverMeshBuilder.ShadeAsStone(mesh, 0f, 0f, kinds, parts);
    }

    /// <summary>
    /// Adds the tower into a mesh being built, its base standing at <paramref name="basePos"/>.
    /// <paramref name="kinds"/>, when given, is filled to one entry per triangle in
    /// <paramref name="tris"/> — any triangles already there get Auto (worked out as usual), and the
    /// tower's own get the stone colour <paramref name="shading"/> names for their part.
    /// <paramref name="parts"/>, when given, is filled the same way with which tier each face is
    /// on — base, ramp, second base — so the stone shading draws a seam at every join between
    /// them, however steep the ramp. Faces already there, and the stem and orb, are part 0.
    /// </summary>
    public void Append(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                       Vector3 basePos, List<float> kinds = null, RiverRunShadingSettings shading = null,
                       List<int> parts = null)
    {
        var sideKind = shading != null ? shading.towerBaseSides : StoneFaceKind.Auto;
        var capKind  = shading != null ? shading.towerBaseTop   : StoneFaceKind.Auto;
        var stemKind = shading != null ? shading.towerStem : StoneFaceKind.Auto;
        var orbKind  = shading != null ? shading.towerOrb  : StoneFaceKind.Auto;

        StoneFaces.Tag(kinds, tris, StoneFaceKind.Auto);
        StoneFaces.Part(parts, tris, 0);

        float r = Mathf.Max(0.001f, stemRadius);
        float h = Mathf.Max(0f, stemHeight);
        float R = Mathf.Max(0.001f, orbRadius);

        // ── Base, ramp and second base, bottom up — each off at zero height. A tier is capped
        //    unless the one on it covers its whole top ──
        float bh  = Mathf.Max(0f, baseHeight);
        float br  = Mathf.Max(0.001f, baseRadius);
        float rh  = Mathf.Max(0f, rampHeight);
        float b2h = Mathf.Max(0f, base2Height);
        float b2r = Mathf.Max(0.001f, base2Radius);

        if (bh > 0f)
            AppendRing(verts, norms, uvs, tris, kinds, parts, 1, ref basePos, br, br, bh,
                       rh <= 0f && (b2h <= 0f || b2r < br), sideKind, capKind);
        if (rh > 0f)
            AppendRing(verts, norms, uvs, tris, kinds, parts, 2, ref basePos, br, b2r, rh,
                       b2h <= 0f, sideKind, capKind);
        if (b2h > 0f)
            AppendRing(verts, norms, uvs, tris, kinds, parts, 3, ref basePos, b2r, b2r, b2h,
                       true, sideKind, capKind);

        // ── Stem: smooth sides, one column doubled at the seam so u runs round unbroken ──
        if (h > 0f)
        {
            int start = verts.Count;
            for (int i = 0; i <= Sides; i++)
            {
                float   a = i * Mathf.PI * 2f / Sides;
                Vector3 n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float   u = a * r;

                verts.Add(basePos + n * r);                   norms.Add(n); uvs.Add(new Vector2(u, 0f));
                verts.Add(basePos + n * r + Vector3.up * h);  norms.Add(n); uvs.Add(new Vector2(u, h));
            }
            for (int i = 0; i < Sides; i++)
            {
                int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                AddTri(tris, verts, norms, b0, t0, t1);
                AddTri(tris, verts, norms, b0, t1, b1);
            }

            // A stem wider than its orb pokes out past it, so it is capped.
            if (r >= R)
            {
                int centre = verts.Count;
                verts.Add(basePos + Vector3.up * h); norms.Add(Vector3.up); uvs.Add(Vector2.zero);
                for (int i = 0; i <= Sides; i++)
                {
                    float a = i * Mathf.PI * 2f / Sides;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    verts.Add(basePos + d + Vector3.up * h); norms.Add(Vector3.up); uvs.Add(new Vector2(d.x, d.z));
                }
                for (int i = 0; i < Sides; i++)
                    AddTri(tris, verts, norms, centre, centre + 1 + i, centre + 2 + i);
            }

            StoneFaces.Tag(kinds, tris, stemKind);
            StoneFaces.Part(parts, tris, 0);
        }

        // ── Orb: seated so the stem's top edge lies on its surface ──
        Vector3 orbCentre = basePos + Vector3.up * (h + Mathf.Sqrt(Mathf.Max(0f, R * R - r * r)));

        int orbStart = verts.Count;
        for (int ring = 0; ring <= OrbRings; ring++)
        {
            float lat = Mathf.PI * ring / OrbRings;           // 0 at the top, pi at the bottom
            for (int i = 0; i <= Sides; i++)
            {
                float   lon = i * Mathf.PI * 2f / Sides;
                Vector3 n   = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat),
                                          Mathf.Sin(lat) * Mathf.Sin(lon));
                verts.Add(orbCentre + n * R);
                norms.Add(n);
                uvs.Add(new Vector2(lon * R, (Mathf.PI - lat) * R));
            }
        }
        int cols = Sides + 1;
        for (int ring = 0; ring < OrbRings; ring++)
        {
            for (int i = 0; i < Sides; i++)
            {
                int a = orbStart + ring * cols + i, b = a + 1;
                int c = a + cols,                   d = c + 1;
                if (ring > 0)            AddTri(tris, verts, norms, a, b, d);
                if (ring < OrbRings - 1) AddTri(tris, verts, norms, a, d, c);
            }
        }

        StoneFaces.Tag(kinds, tris, orbKind);
        StoneFaces.Part(parts, tris, 0);
    }

    // One round tier of the base: sides running from the bottom radius to the top radius (a
    // cylinder when they match, a ramp when they don't), capped on top when nothing stands on it.
    // Moves basePos up to its top. Every face of it, cap included, is tagged as tier `part`.
    static void AppendRing(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                           List<float> kinds, List<int> parts, int part, ref Vector3 basePos, float bottomR, float topR, float height,
                           bool cap, StoneFaceKind sideKind, StoneFaceKind capKind)
    {
        float slant = Mathf.Sqrt(height * height + (bottomR - topR) * (bottomR - topR));
        float midR  = (bottomR + topR) * 0.5f;

        int start = verts.Count;
        for (int i = 0; i <= Sides; i++)
        {
            float   a   = i * Mathf.PI * 2f / Sides;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 n   = (dir * height + Vector3.up * (bottomR - topR)).normalized;
            float   u   = a * midR;

            verts.Add(basePos + dir * bottomR);                      norms.Add(n); uvs.Add(new Vector2(u, 0f));
            verts.Add(basePos + dir * topR + Vector3.up * height);   norms.Add(n); uvs.Add(new Vector2(u, slant));
        }
        for (int i = 0; i < Sides; i++)
        {
            int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            AddTri(tris, verts, norms, b0, t0, t1);
            AddTri(tris, verts, norms, b0, t1, b1);
        }
        StoneFaces.Tag(kinds, tris, sideKind);
        StoneFaces.Part(parts, tris, part);

        basePos += Vector3.up * height;
        if (!cap) return;

        int centre = verts.Count;
        verts.Add(basePos); norms.Add(Vector3.up); uvs.Add(Vector2.zero);
        for (int i = 0; i <= Sides; i++)
        {
            float   a = i * Mathf.PI * 2f / Sides;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * topR;
            verts.Add(basePos + d); norms.Add(Vector3.up); uvs.Add(new Vector2(d.x, d.z));
        }
        for (int i = 0; i < Sides; i++)
            AddTri(tris, verts, norms, centre, centre + 1 + i, centre + 2 + i);

        StoneFaces.Tag(kinds, tris, capKind);
        StoneFaces.Part(parts, tris, part);
    }

    // One triangle, wound so its face points the way its vertices' normals do — Unity's front
    // face is the winding whose Cross(v1-v0, v2-v0) points along the normal.
    static void AddTri(List<int> tris, List<Vector3> verts, List<Vector3> norms, int a, int b, int c)
    {
        Vector3 face = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        Vector3 n    = norms[a] + norms[b] + norms[c];
        if (Vector3.Dot(face, n) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
        else                            { tris.Add(a); tris.Add(c); tris.Add(b); }
    }
}
