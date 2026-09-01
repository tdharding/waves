using System.Collections.Generic;
using UnityEngine;

// Shared builder for the arena boundary wall used by ArenaWallsGenerator.
//
// The mesh is a closed band following a boundary loop, built around the object origin so
// that — exactly like ProceduralBoxMesh — the top sits at +height (above the waterline /
// prefab origin) and the bottom at -drop (dropping beneath the surface so it looks bottomless).
//
// innerRadius is the gameplay boundary: the wall's INNER face sits on it and the thickness is
// added outward, so the wall's closest approach to the centre always equals the arena radius
// that doors, soul clamping and the grid frame are placed against.
//
// UVs are arc length: u = distance travelled around the perimeter, v = height from the
// waterline. 1 world unit = 1 UV unit, so texel density is identical on a small arena and a
// massive one and the material tiling never has to be retuned per size.
public static class ProceduralArenaWallMesh
{
    public enum Shape { Circle, Square }

    // Circle resolution. Fixed rather than exposed — 96 segments is smooth at every arena
    // size the game uses, and the wall is a few hundred verts either way.
    const int CircleSegments = 96;

    // Joins sharper than this get their own edge normal (a crease); gentler ones share an
    // averaged normal so the circle reads as smooth. Circle steps are 3.75°, square corners 90°.
    const float SharpCornerDegrees = 35f;

    public static Mesh Build(Shape shape, float innerRadius, float thickness, float height, float drop)
    {
        float th   = Mathf.Max(0.001f, thickness);
        float yTop = height;
        float yBot = -drop;

        List<Vector2> pts = BuildLoop(shape, innerRadius);
        int n = pts.Count;
        if (n < 3) return new Mesh { name = "ProceduralArenaWall" };

        // Outward normal and length of edge i (pts[i] → pts[i+1]).
        var edgeNormal = new Vector2[n];
        var edgeLength = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 d = pts[(i + 1) % n] - pts[i];
            edgeLength[i] = d.magnitude;
            // Rotate the edge direction by -90° so the normal points away from the centre.
            edgeNormal[i] = edgeLength[i] > 1e-6f ? new Vector2(d.y, -d.x) / edgeLength[i]
                                                  : pts[i].normalized;
        }

        var smoothNormal = new Vector2[n];
        var isSharp      = new bool[n];
        float cosSharp   = Mathf.Cos(SharpCornerDegrees * Mathf.Deg2Rad);
        for (int i = 0; i < n; i++)
        {
            Vector2 a = edgeNormal[(i - 1 + n) % n];
            Vector2 b = edgeNormal[i];
            isSharp[i]      = Vector2.Dot(a, b) < cosSharp;
            smoothNormal[i] = (a + b).sqrMagnitude > 1e-8f ? (a + b).normalized : b;
        }

        // Walk the loop emitting two stations per edge (its start and end). Coincident
        // stations at a smooth join carry the same normal so they shade as one surface; at a
        // sharp corner each carries its own edge normal and creases. Emitting a pair per edge
        // also gives the seam at point 0 the two different u values it needs.
        int stations = n * 2;
        var sPos = new Vector2[stations];
        var sNrm = new Vector2[stations];
        var sU   = new float[stations];

        float u = 0f;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int s = i * 2;

            sPos[s] = pts[i];
            sNrm[s] = isSharp[i] ? edgeNormal[i] : smoothNormal[i];
            sU[s]   = u;

            u += edgeLength[i];

            sPos[s + 1] = pts[next];
            sNrm[s + 1] = isSharp[next] ? edgeNormal[i] : smoothNormal[next];
            sU[s + 1]   = u;
        }

        // Three quads per edge: inner face, outer face, and the top cap between them.
        var verts = new List<Vector3>(stations * 6);
        var norms = new List<Vector3>(stations * 6);
        var uvs   = new List<Vector2>(stations * 6);
        var tris  = new List<int>(n * 18);

        for (int i = 0; i < n; i++)
        {
            int a = i * 2, b = i * 2 + 1;

            Vector3 nA = new Vector3(sNrm[a].x, 0f, sNrm[a].y);
            Vector3 nB = new Vector3(sNrm[b].x, 0f, sNrm[b].y);

            Vector3 inA = new Vector3(sPos[a].x, 0f, sPos[a].y);
            Vector3 inB = new Vector3(sPos[b].x, 0f, sPos[b].y);
            // Thickness is laid off along each end's own normal, so the outer face stays
            // parallel to the inner one and corners meet cleanly.
            Vector3 outA = inA + nA * th;
            Vector3 outB = inB + nB * th;

            float uA = sU[a], uB = sU[b];
            Vector3 top = Vector3.up * yTop, bot = Vector3.up * yBot;

            // Inner face — normal points back toward the arena centre.
            AddQuad(verts, norms, uvs, tris,
                    inA + bot, inB + bot, inB + top, inA + top, -nA, -nB,
                    new Vector2(uA, yBot), new Vector2(uB, yBot),
                    new Vector2(uB, yTop), new Vector2(uA, yTop));

            // Outer face.
            AddQuad(verts, norms, uvs, tris,
                    outA + bot, outB + bot, outB + top, outA + top, nA, nB,
                    new Vector2(uA, yBot), new Vector2(uB, yBot),
                    new Vector2(uB, yTop), new Vector2(uA, yTop));

            // Top cap — v runs across the wall thickness so the texture keeps world scale.
            AddQuad(verts, norms, uvs, tris,
                    inA + top, inB + top, outB + top, outA + top, Vector3.up, Vector3.up,
                    new Vector2(uA, 0f), new Vector2(uB, 0f),
                    new Vector2(uB, th), new Vector2(uA, th));
        }

        var mesh = new Mesh { name = "ProceduralArenaWall" };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Emits one quad given corners in loop order (p0→p1 along the perimeter, then up).
    // nA/nB are the normals for the two perimeter ends so a smooth join can share them.
    // The triangle order is checked against the intended normal and flipped if needed, so the
    // winding stays correct for every shape without depending on the loop's direction.
    static void AddQuad(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 nA, Vector3 nB,
                        Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        int v = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
        norms.Add(nA); norms.Add(nB); norms.Add(nB); norms.Add(nA);
        uvs.Add(uv0);  uvs.Add(uv1);  uvs.Add(uv2);  uvs.Add(uv3);

        // Unity's front face is the winding whose Cross(v1-v0, v2-v0) points along the normal.
        if (Vector3.Dot(Vector3.Cross(p2 - p0, p1 - p0), nA) < 0f)
        {
            tris.Add(v + 0); tris.Add(v + 1); tris.Add(v + 2);
            tris.Add(v + 0); tris.Add(v + 2); tris.Add(v + 3);
        }
        else
        {
            tris.Add(v + 0); tris.Add(v + 2); tris.Add(v + 1);
            tris.Add(v + 0); tris.Add(v + 3); tris.Add(v + 2);
        }
    }

    // The boundary loop in XZ with no repeated closing point. Both shapes are built so their
    // CLOSEST approach to the centre equals innerRadius — the circle everywhere, the square
    // along its flat sides.
    public static List<Vector2> BuildLoop(Shape shape, float innerRadius)
    {
        float r = Mathf.Max(0.01f, innerRadius);
        var pts = new List<Vector2>();

        if (shape == Shape.Square)
        {
            pts.Add(new Vector2( r,  r));
            pts.Add(new Vector2(-r,  r));
            pts.Add(new Vector2(-r, -r));
            pts.Add(new Vector2( r, -r));
        }
        else
        {
            for (int i = 0; i < CircleSegments; i++)
            {
                float a = i * Mathf.PI * 2f / CircleSegments;
                pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }

        return pts;
    }
}
