using System.Collections.Generic;
using UnityEngine;

// Builds a single "stepped rooftop" building mesh: a solid box whose TOP has a stepped
// parapet running around the whole perimeter, with the centre of the roof sunk down
// (a quarter by default). From the boat you see the outer wall step up and down; the
// rim has thickness so each step reads as a solid slab; behind it the roof drops away.
// One mesh — the MeshCollider follows it for free.
//
// Height-field construction: the footprint is a grid of columns, each risen to its own
// height. Perimeter columns (within rimCells of an edge) get stepped heights from a
// random walk around the loop; interior columns sit at the sunk-roof level. Neighbour
// height differences emit the outer walls (full height, windowed), the inner parapet
// walls and the step risers automatically. Only the outer perimeter walls carry UV2, so
// WindowTiling.hlsl paints windows only there; caps / risers / roof / floor get zeroed UV2.
public static class SteppedBuildingMesh
{
    public static Mesh Build(float width, float length, float top, float bottomDepth,
                             SteppedBuildingConfig cfg, int seed)
    {
        cfg ??= new SteppedBuildingConfig();

        float hw    = Mathf.Max(0.0001f, width)  * 0.5f;
        float hl    = Mathf.Max(0.0001f, length) * 0.5f;
        float yBot  = -bottomDepth;
        float roofY = top * (1f - Mathf.Clamp01(cfg.dropFraction));   // sunk centre roof

        int nx = Mathf.Max(2, Mathf.RoundToInt(width  / Mathf.Max(0.05f, cfg.cellSize)));
        int nz = Mathf.Max(2, Mathf.RoundToInt(length / Mathf.Max(0.05f, cfg.cellSize)));
        float cw = width / nx, cl = length / nz;
        int rimCells  = Mathf.Clamp(cfg.rimCells, 1, Mathf.Max(1, Mathf.Min(nx, nz) / 2));
        int stepCount = Mathf.Max(1, cfg.stepCount);

        // Step heights around the perimeter snap to discrete tiers: tier k sits levelSpacing
        // below the top. A random walk hops between tiers — variation is the chance a plateau
        // changes tier at all, persistence keeps a run heading one way (down-down-down then up),
        // and the walk bounces off the top/bottom tiers instead of sticking. Heights never sink
        // below the sunk centre roof, so the rim always reads as a raised edge.
        int levels  = Mathf.Max(1, cfg.levelCount);
        float space = Mathf.Max(0.01f, cfg.levelSpacing);
        var rng = new System.Random(seed);
        var stepH = new float[stepCount];
        int tier = rng.Next(levels);
        int dir  = rng.NextDouble() < 0.5 ? -1 : 1;
        for (int k = 0; k < stepCount; k++)
        {
            stepH[k] = Mathf.Max(roofY, top - tier * space);
            if (rng.NextDouble() < cfg.variation)
            {
                if (rng.NextDouble() > cfg.persistence) dir = -dir;
                tier += dir;
                if (tier < 0)         { tier = 0;          dir =  1; }
                if (tier > levels - 1) { tier = levels - 1; dir = -1; }
            }
        }

        float perim = 2f * (width + length);

        // Column heights: rim cells stepped by perimeter position, interior = sunk roof.
        var H = new float[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                bool rim = i < rimCells || i >= nx - rimCells || j < rimCells || j >= nz - rimCells;
                if (!rim) { H[i, j] = roofY; continue; }

                float cx = -hw + (i + 0.5f) * cw;
                float cz = -hl + (j + 0.5f) * cl;
                float dMinX = cx + hw, dMaxX = hw - cx, dMinZ = cz + hl, dMaxZ = hl - cz;
                float m = Mathf.Min(Mathf.Min(dMinX, dMaxX), Mathf.Min(dMinZ, dMaxZ));
                float s;
                if      (m == dMinZ) s = cx + hw;                          // bottom edge, +x
                else if (m == dMaxX) s = width + (cz + hl);               // right edge,  +z
                else if (m == dMaxZ) s = width + length + (hw - cx);      // top edge,   -x
                else                 s = 2f * width + length + (hl - cz); // left edge,   -z
                int idx = Mathf.Clamp((int)(s / perim * stepCount), 0, stepCount - 1);
                H[i, j] = stepH[idx];
            }

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uv    = new List<Vector2>();
        var uv2   = new List<Vector4>();
        var tris  = new List<int>();
        Vector4 Z = Vector4.zero;

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n,
                  Vector4 ua, Vector4 ub, Vector4 uc, Vector4 ud)
        {
            int i0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uv.Add(ProceduralBoxMesh.PlanarBoxUV(a, n)); uv.Add(ProceduralBoxMesh.PlanarBoxUV(b, n));
            uv.Add(ProceduralBoxMesh.PlanarBoxUV(c, n)); uv.Add(ProceduralBoxMesh.PlanarBoxUV(d, n));
            uv2.Add(ua); uv2.Add(ub); uv2.Add(uc); uv2.Add(ud);
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
            tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2);
        }

        // A boundary wall runs full height (yBot→h) and is windowed; an interior wall/riser
        // runs from the lower neighbour up to h and stays plain. u is the distance along the
        // face from its left corner (viewed from outside); faceW is that side's full length.
        Vector4 UV(bool boundary, float u, float y, float faceW) =>
            boundary ? new Vector4(u, y, faceW, top) : Z;

        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float h  = H[i, j];
                float x0 = -hw + i * cw, x1 = x0 + cw;
                float z0 = -hl + j * cl, z1 = z0 + cl;

                // Top cap (roof / rim slab top).
                Quad(new Vector3(x0, h, z0), new Vector3(x1, h, z0),
                     new Vector3(x1, h, z1), new Vector3(x0, h, z1), Vector3.up, Z, Z, Z, Z);

                // −Z wall
                {
                    bool bnd = j == 0;
                    float nb = bnd ? yBot : H[i, j - 1];
                    if (h > nb + 1e-4f)
                        Quad(new Vector3(x0, nb, z0), new Vector3(x1, nb, z0),
                             new Vector3(x1, h, z0),  new Vector3(x0, h, z0), Vector3.back,
                             UV(bnd, x0 + hw, nb, width), UV(bnd, x1 + hw, nb, width),
                             UV(bnd, x1 + hw, h,  width), UV(bnd, x0 + hw, h,  width));
                }
                // +Z wall
                {
                    bool bnd = j == nz - 1;
                    float nb = bnd ? yBot : H[i, j + 1];
                    if (h > nb + 1e-4f)
                        Quad(new Vector3(x1, nb, z1), new Vector3(x0, nb, z1),
                             new Vector3(x0, h, z1),  new Vector3(x1, h, z1), Vector3.forward,
                             UV(bnd, hw - x1, nb, width), UV(bnd, hw - x0, nb, width),
                             UV(bnd, hw - x0, h,  width), UV(bnd, hw - x1, h,  width));
                }
                // +X wall
                {
                    bool bnd = i == nx - 1;
                    float nb = bnd ? yBot : H[i + 1, j];
                    if (h > nb + 1e-4f)
                        Quad(new Vector3(x1, nb, z0), new Vector3(x1, nb, z1),
                             new Vector3(x1, h, z1),  new Vector3(x1, h, z0), Vector3.right,
                             UV(bnd, z0 + hl, nb, length), UV(bnd, z1 + hl, nb, length),
                             UV(bnd, z1 + hl, h,  length), UV(bnd, z0 + hl, h,  length));
                }
                // −X wall
                {
                    bool bnd = i == 0;
                    float nb = bnd ? yBot : H[i - 1, j];
                    if (h > nb + 1e-4f)
                        Quad(new Vector3(x0, nb, z1), new Vector3(x0, nb, z0),
                             new Vector3(x0, h, z0),  new Vector3(x0, h, z1), Vector3.left,
                             UV(bnd, hl - z1, nb, length), UV(bnd, hl - z0, nb, length),
                             UV(bnd, hl - z0, h,  length), UV(bnd, hl - z1, h,  length));
                }
            }

        // Single bottom cap over the whole footprint.
        Quad(new Vector3(-hw, yBot, hl), new Vector3(hw, yBot, hl),
             new Vector3(hw, yBot, -hl), new Vector3(-hw, yBot, -hl), Vector3.down, Z, Z, Z, Z);

        var mesh = new Mesh { name = "SteppedBuilding" };
        mesh.indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uv);
        mesh.SetUVs(1, uv2);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
