using System.Collections.Generic;
using UnityEngine;

// Shared builder for the bottomless axis-aligned box mesh used by ProceduralCubeBuilding
// (blocks) and ProceduralSplineWall (spline wall tiles).
//
// The mesh is built around the object origin so that:
//   • the top face sits at +top (above the waterline / prefab origin),
//   • the bottom face sits at -bottomDepth (dropping beneath the surface so it looks bottomless).
// width = full X extent, length = full Z extent (not half-extents).
public static class ProceduralBoxMesh
{
    // A 24-vertex axis-aligned box (per-face normals for correct flat lighting).
    // uOffset shifts the UV along the Z axis (the "length" axis) by a world distance, so a run
    // of tiles laid end-to-end along Z share one continuous texture instead of each restarting.
    public static Mesh Build(float width, float length, float top, float bottomDepth, float uOffset = 0f)
    {
        float hw = Mathf.Max(0.0001f, width) * 0.5f;
        float hl = Mathf.Max(0.0001f, length) * 0.5f;
        float yTop = top;
        float yBot = -bottomDepth;

        // 8 corners
        Vector3 c000 = new Vector3(-hw, yBot, -hl);
        Vector3 c100 = new Vector3( hw, yBot, -hl);
        Vector3 c101 = new Vector3( hw, yBot,  hl);
        Vector3 c001 = new Vector3(-hw, yBot,  hl);
        Vector3 c010 = new Vector3(-hw, yTop, -hl);
        Vector3 c110 = new Vector3( hw, yTop, -hl);
        Vector3 c111 = new Vector3( hw, yTop,  hl);
        Vector3 c011 = new Vector3(-hw, yTop,  hl);

        var verts = new Vector3[24];
        var norms = new Vector3[24];
        var uvs   = new Vector2[24];
        var uv2s  = new Vector4[24];
        var tris  = new int[36];

        int v = 0, t = 0;
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, float faceW)
        {
            verts[v + 0] = a; verts[v + 1] = b; verts[v + 2] = c; verts[v + 3] = d;
            norms[v + 0] = norms[v + 1] = norms[v + 2] = norms[v + 3] = n;
            // World-proportional planar UVs: 1 world unit = 1 UV unit, so the texture
            // tiles at the same density on every face regardless of the box's dimensions.
            uvs[v + 0] = PlanarBoxUV(a, n, uOffset); uvs[v + 1] = PlanarBoxUV(b, n, uOffset);
            uvs[v + 2] = PlanarBoxUV(c, n, uOffset); uvs[v + 3] = PlanarBoxUV(d, n, uOffset);
            // Window-tiling UV2 (read by WindowTiling.hlsl): xy = face-local coords in
            // world units (x measured from the face's left edge as seen from outside,
            // y from the waterline), zw = (face width, height above water). Side faces
            // pass their full horizontal extent as faceW and use the a=BL, b=BR, c=TR,
            // d=TL winding below; top/bottom pass 0 and keep the zeroed default so the
            // shader can early-out on w <= 0 without a normal test.
            if (faceW > 0f)
            {
                uv2s[v + 0] = new Vector4(0f,    a.y, faceW, top);
                uv2s[v + 1] = new Vector4(faceW, b.y, faceW, top);
                uv2s[v + 2] = new Vector4(faceW, c.y, faceW, top);
                uv2s[v + 3] = new Vector4(0f,    d.y, faceW, top);
            }
            tris[t + 0] = v + 0; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v + 0; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            v += 4; t += 6;
        }

        Face(c010, c110, c111, c011, Vector3.up,      0f);       // top
        Face(c001, c101, c100, c000, Vector3.down,    0f);       // bottom
        Face(c000, c100, c110, c010, Vector3.back,    hw * 2f);  // -Z
        Face(c101, c001, c011, c111, Vector3.forward, hw * 2f);  // +Z
        Face(c001, c000, c010, c011, Vector3.left,    hl * 2f);  // -X
        Face(c100, c101, c111, c110, Vector3.right,   hl * 2f);  // +X

        var mesh = new Mesh();
        mesh.vertices  = verts;
        mesh.normals   = norms;
        mesh.uv        = uvs;
        mesh.SetUVs(1, new List<Vector4>(uv2s));
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    // Planar UV for an axis-aligned face: drop the axis the face points along and use the
    // other two object-space coords directly (object space == world size here, since the
    // prefab isn't scaled). Keeps texel density constant across faces of any size.
    public static Vector2 PlanarBoxUV(Vector3 p, Vector3 n, float uOffset = 0f)
    {
        if (Mathf.Abs(n.y) > 0.5f) return new Vector2(p.x, p.z + uOffset); // top / bottom (Z = along-path)
        if (Mathf.Abs(n.z) > 0.5f) return new Vector2(p.x, p.y);           // front / back (end caps, no along-path axis)
        return new Vector2(p.z + uOffset, p.y);                            // left / right (Z = along-path)
    }
}
