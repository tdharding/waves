using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the archway standing over a river where it arrives at an arena.
///
/// The arch is a band swept along an arched outline: up one leg, round the crown, and down the
/// other. The band has an inner edge and an outer edge a thickness apart, and the whole thing is
/// carried along the river by <see cref="ArenaArchwayProfile.depth"/> — so it is a tunnel with a
/// front face, a back face, a soffit inside and an extrados outside.
///
/// Built in the archway's own local space, matching the run it straddles:
///   x  across the river, 0 on its centreline
///   y  up from the rim top, which is 0 — the same origin <see cref="RiverMeshBuilder"/> sweeps
///      a run along, so the arch's feet sit exactly on the rims rather than near them
///   z  along the river, 0 at the front face and running back through the wall
///
/// The crown is an ellipse rather than a circle, so the arch can stand taller than a plain
/// semicircle without the opening widening to match.
/// </summary>
public static class ArenaArchwayMesh
{
    // Fewest segments round the crown, however coarse the detail is set — a narrow arch still
    // has to read as a curve rather than a corner.
    private const int MinCrownSegments = 8;

    /// <summary>Which part of the arch a triangle belongs to — see the parts list on Build.</summary>
    public enum Part { Inside, Outside, Faces, Feet }

    /// <summary>
    /// Sweeps the arch band. <paramref name="profile"/> must already be resolved — every
    /// inherited number settled — see <see cref="ArenaArchwayProfile.Resolve"/>.
    ///
    /// <paramref name="edge"/> is the target length of an edge anywhere in the mesh, the same
    /// number the runs and pools are built from, so the arch carries their density.
    ///
    /// <paramref name="parts"/>, when given, is filled with one entry per triangle of the finished
    /// mesh saying which part of the arch it is, for the stone shading to colour each part.
    ///
    /// <paramref name="frames"/>, when given, are where the river under the arch actually runs,
    /// front face first — the tunnel is swept through them so its legs stay on the rims however
    /// the river bends, climbs or falls under it. Without them it runs straight back by the depth.
    ///
    /// <paramref name="heights"/>, when given, is filled with one entry per triangle: how high above
    /// the rim top its part of the outline stands — what tells a leg from the arch once the tunnel
    /// follows a river that climbs or falls.
    /// </summary>
    public static Mesh Build(ArenaArchwayProfile profile, float edge, List<Part> parts = null,
                             IList<Frame> frames = null, List<float> heights = null)
    {
        var b = new MeshBuild { Parts = parts, Heights = heights };
        if (profile == null) return b.ToMesh("ArenaArchway");

        edge = Mathf.Max(edge, 0.005f);

        float half  = Mathf.Max(0.005f, profile.openingWidth * 0.5f);
        float thick = Mathf.Max(0.005f, profile.thickness);
        float leg   = Mathf.Max(0f,     profile.legHeight);
        float crown = Mathf.Max(0.005f, profile.archHeight);
        float depth = Mathf.Max(0.005f, profile.depth);

        // How finely to walk the outline. Worked out ONCE, off the outer curve because it is
        // the longer of the two — so the detail asked for is met all the way round, and, far
        // more importantly, both outlines get the same number of stations. Letting each pick
        // its own count leaves station i on one meaning nothing on the other.
        int legSteps = leg <= 0.0001f ? 0 : Mathf.Max(1, Mathf.CeilToInt(leg / edge));
        int crownSeg = CrownSegments(half + thick, crown + thick, edge);

        // The two outlines the band runs between, walked in the same order so station i on one
        // pairs with station i on the other.
        var inner = Outline(half,         leg, crown,         legSteps, crownSeg);
        var outer = Outline(half + thick, leg, crown + thick, legSteps, crownSeg);
        if (inner.Count < 2 || inner.Count != outer.Count) return b.ToMesh("ArenaArchway");

        int n = inner.Count;

        // Where the outline stands along the tunnel: straight back by the depth, or through
        // the frames the river under it was swept through.
        if (frames == null || frames.Count < 2)
            frames = new[]
            {
                new Frame { origin = Vector3.zero,                right = Vector3.right, z = 0f },
                new Frame { origin = Vector3.forward * depth,     right = Vector3.right, z = depth },
            };
        int ends = frames.Count - 1;

        Vector3 At(Vector2 p, int k) => frames[k].origin + frames[k].right * p.x + Vector3.up * p.y;

        // Arc length along the band, so the texture keeps world scale round the curve the way
        // it does along a wall.
        var run = new float[n];
        for (int i = 1; i < n; i++)
            run[i] = run[i - 1] + Vector2.Distance(inner[i - 1], inner[i]);

        for (int i = 0; i < n - 1; i++)
        {
            float u0 = run[i], u1 = run[i + 1];
            b.Height = (inner[i].y + inner[i + 1].y) * 0.5f;

            for (int k = 0; k < ends; k++)
            {
                Vector3 i0 = At(inner[i], k),     i1 = At(inner[i + 1], k);
                Vector3 o0 = At(outer[i], k),     o1 = At(outer[i + 1], k);
                Vector3 j0 = At(inner[i], k + 1), j1 = At(inner[i + 1], k + 1);
                Vector3 p0 = At(outer[i], k + 1), p1 = At(outer[i + 1], k + 1);
                float   z0 = frames[k].z,         z1 = frames[k + 1].z;

                // Soffit — the surface you pass under, facing in toward the opening.
                b.Part = Part.Inside;
                b.Quad(i0, j0, j1, i1,
                       new Vector2(u0, z0), new Vector2(u0, z1),
                       new Vector2(u1, z1), new Vector2(u1, z0));

                // Extrados — the outside of the arch.
                b.Part = Part.Outside;
                b.Quad(o0, o1, p1, p0,
                       new Vector2(u0, z0), new Vector2(u1, z0),
                       new Vector2(u1, z1), new Vector2(u0, z1));
            }

            // The band itself, at each end of the tunnel.
            b.Part = Part.Faces;
            b.Quad(At(inner[i], 0), At(inner[i + 1], 0), At(outer[i + 1], 0), At(outer[i], 0),
                   new Vector2(u0, 0f), new Vector2(u1, 0f),
                   new Vector2(u1, thick), new Vector2(u0, thick));
            b.Quad(At(inner[i], ends), At(outer[i], ends), At(outer[i + 1], ends), At(inner[i + 1], ends),
                   new Vector2(u0, 0f), new Vector2(u0, thick),
                   new Vector2(u1, thick), new Vector2(u1, 0f));
        }

        // The feet the arch stands on — one at each end of the outline, closing the solid where
        // it meets the rim. Wound opposite ways at the two, so both face down out of the solid.
        b.Part = Part.Feet;
        b.Height = 0f;
        for (int k = 0; k < ends; k++)
        {
            float z0 = frames[k].z, z1 = frames[k + 1].z;
            var uv = new[]
            {
                new Vector2(0f,    z0), new Vector2(thick, z0),
                new Vector2(thick, z1), new Vector2(0f,    z1),
            };

            b.Quad(At(inner[0], k), At(outer[0], k), At(outer[0], k + 1), At(inner[0], k + 1),
                   uv[0], uv[1], uv[2], uv[3]);
            b.Quad(At(inner[n - 1], k), At(inner[n - 1], k + 1), At(outer[n - 1], k + 1), At(outer[n - 1], k),
                   uv[0], uv[3], uv[2], uv[1]);
        }

        return b.ToMesh("ArenaArchway");
    }

    /// <summary>
    /// One place along the tunnel, in the archway's own space: the rim-top centre of the river
    /// there, its cross axis (the way +x runs), and how far back from the front face it is.
    /// </summary>
    public struct Frame
    {
        public Vector3 origin;
        public Vector3 right;
        public float   z;
    }

    /// <summary>
    /// How many segments to break the crown into. Public because the door built into the arch
    /// counts its own stations the same way, and the two have to agree or the door's edge and
    /// the arch's inner edge nearly meet instead of exactly meeting. Off the longer of its two axes, so neither a
    /// wide flat arch nor a tall narrow one comes out coarse. Always even, so a station lands
    /// exactly on the apex.
    /// </summary>
    public static int CrownSegments(float half, float crown, float edge)
    {
        float span = Mathf.PI * 0.5f * Mathf.Max(half, crown);
        int   n    = Mathf.Max(MinCrownSegments, Mathf.CeilToInt(2f * span / edge));
        return (n & 1) != 0 ? n + 1 : n;
    }

    /// <summary>
    /// One side of the arch band, walked from the left foot up and over to the right foot.
    /// The crown is a quarter-ellipse either side of the apex, so raising
    /// <paramref name="crown"/> makes the arch taller without opening it wider.
    ///
    /// The station counts are passed in rather than worked out here, so the inner and outer
    /// outlines come back the same length and can be paired station for station.
    /// </summary>
    private static List<Vector2> Outline(float half, float leg, float crown,
                                         int legSteps, int crownSeg)
    {
        var pts = new List<Vector2>();

        for (int i = 0; i <= legSteps; i++)
            pts.Add(new Vector2(-half, leg * i / Mathf.Max(1, legSteps)));

        // Left round to right: angle runs from pi to 0.
        for (int i = 1; i < crownSeg; i++)
        {
            float a = Mathf.PI * (1f - (float)i / crownSeg);
            pts.Add(new Vector2(Mathf.Cos(a) * half, leg + Mathf.Sin(a) * crown));
        }

        for (int i = legSteps; i >= 0; i--)
            pts.Add(new Vector2(half, leg * i / Mathf.Max(1, legSteps)));

        return pts;
    }

    // Every face gets its own vertices, so the arch stays hard-edged and faceted like the run
    // and the pool it sits against.
    private class MeshBuild
    {
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _norms = new List<Vector3>();
        private readonly List<Vector2> _uvs   = new List<Vector2>();
        private readonly List<int>     _tris  = new List<int>();

        // Where each triangle's part is recorded, if anyone asked, and the part being built now.
        public List<Part> Parts;
        public Part       Part;

        // Where each triangle's height above the rim is recorded, if anyone asked, and the
        // height of what is being built now.
        public List<float> Heights;
        public float       Height;

        // Wound a-c-b rather than a-b-c: the corners are handed in walking the arch outline,
        // which traces the solid the other way round. Taken at face value every face of the
        // archway points into it and the whole thing renders inside out.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Tri(a, c, b, ua, uc, ub);
            Tri(a, d, c, ua, ud, uc);
        }

        private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            Vector3 n = Vector3.Cross(b - a, c - b);
            if (n.sqrMagnitude < 1e-12f) return;      // collapsed, e.g. a zero-height leg
            n.Normalize();

            int i0 = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            _norms.Add(n); _norms.Add(n); _norms.Add(n);
            _uvs.Add(ua);  _uvs.Add(ub);  _uvs.Add(uc);
            _tris.Add(i0); _tris.Add(i0 + 1); _tris.Add(i0 + 2);
            Parts?.Add(Part);
            Heights?.Add(Height);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_norms);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
