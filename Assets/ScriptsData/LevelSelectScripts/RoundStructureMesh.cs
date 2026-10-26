using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The round column a freestanding structure can be — and, with a wall round its top, a round
/// outpost. The round twin of <see cref="InstallationOutpostMesh.Build"/>, in the structure's own
/// frame: centred on the origin, y = 0 the ground under its centre, the floor at +height, the ring
/// wall on to +height + wallHeight, and the sides down to -drop.
///
/// The outpost's two columns stand on the ring wall at +x and -x — where a block's corners would
/// be either side of its turn — centred in the wall, or on the very edge when there is no wall.
/// Each can carry a tower on its top; when any does, no tower stands on the floor.
///
/// Comes out carrying the river run stone shading, the parts tagged with the outpost's colours
/// (the designer hands it the Freestanding Structures parts in their place).
/// </summary>
public static class RoundStructureMesh
{
    const int Sides = 48;

    public static Mesh Build(float radius, float height, float drop,
                             float wallThickness, float wallHeight,
                             LollipopTower tower = null, Vector2 towerOffset = default,
                             RiverRunShadingSettings shading = null,
                             InstallationOutpostMesh.Columns columns = default,
                             float columnRadius = 0.1f, float columnHeight = 0.2f,
                             float rimTopY = 0f)
    {
        float R  = Mathf.Max(0.01f, radius);
        float yB = -Mathf.Max(0f, drop);
        float yF = Mathf.Max(0f, height);

        float t      = WallInset(R, wallThickness, wallHeight);
        bool  walled = t > 0f;
        float yT     = walled ? yF + wallHeight : yF;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        var kinds = new List<float>();
        var parts = new List<int>();   // only the tower fills this, as on the square outpost
        StoneFaceKind Choice(System.Func<RiverRunShadingSettings, StoneFaceKind> pick) =>
            shading != null ? pick(shading) : StoneFaceKind.Auto;

        // ── The outside, top of the wall down to the drop ──
        InstallationOutpostMesh.AppendColumn(verts, norms, uvs, tris, Vector3.zero, R, yB, yT, Sides);
        StoneFaces.Tag(kinds, tris, Choice(s => s.outpostOutsideWalls));
        PartLabels.Mark("Round Column", verts, tris);

        if (!walled)
        {
            InstallationOutpostMesh.AppendColumnCap(verts, norms, uvs, tris, Vector3.up * yF, R, Sides);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostFloor));
            PartLabels.Mark("Round Column", verts, tris);
        }
        else
        {
            float ri = R - t;

            // ── Top of the ring wall ──
            AppendAnnulus(verts, norms, uvs, tris, yT, ri, R);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostWallTop));
            PartLabels.Mark("Wall", verts, tris);

            // ── Inside of the ring wall, facing in across the floor ──
            AppendInnerWall(verts, norms, uvs, tris, ri, yF, yT);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostInsideWalls));
            PartLabels.Mark("Wall", verts, tris);

            // ── The floor ──
            InstallationOutpostMesh.AppendColumnCap(verts, norms, uvs, tris, Vector3.up * yF, ri, Sides);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostFloor));
            PartLabels.Mark("Floor", verts, tris);
        }

        // ── The columns on the ring, from the drop up past the floor ──
        float r = Mathf.Max(0.001f, columnRadius);
        foreach (bool plusX in new[] { true, false })
        {
            if (!(plusX ? columns.plusX : columns.minusX)) continue;
            Vector3 top = ColumnTop(radius, height, wallThickness, wallHeight, columnHeight, plusX);
            InstallationOutpostMesh.AppendColumn(verts, norms, uvs, tris, new Vector3(top.x, 0f, top.z), r, yB, top.y);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostColumnSides));
            PartLabels.Mark("Column", verts, tris);
            InstallationOutpostMesh.AppendColumnCap(verts, norms, uvs, tris, top, r);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostColumnTop));
            PartLabels.Mark("Column", verts, tris);
        }

        // The towers take the tower Designation's Scale, as they take its colours.
        var designations = new List<StoneDesignation>();
        StoneFaces.Designate(designations, tris, RiverRunShadingSettings.OutpostAs(shading));
        if (tower != null)
            foreach (var towerBase in TowerBases(radius, height, wallThickness, wallHeight,
                                                 towerOffset, columns, columnHeight))
                tower.Append(verts, norms, uvs, tris, towerBase, kinds, shading, parts);
        StoneFaces.Designate(designations, tris, RiverRunShadingSettings.TowerAs(shading));

        var mesh = new Mesh { name = "RoundStructure" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        return RiverMeshBuilder.ShadeAsStone(mesh, rimTopY, yT, kinds, parts,
                                             faceDesignations: designations);
    }

    /// <summary>
    /// Where each tower's base stands: on top of every column picked to carry one, or — when none
    /// does — on the floor, moved off its centre by the offset (x across, y along the structure's z).
    /// </summary>
    public static List<Vector3> TowerBases(float radius, float height, float wallThickness, float wallHeight,
                                           Vector2 towerOffset, InstallationOutpostMesh.Columns columns,
                                           float columnHeight)
    {
        var bases = new List<Vector3>();
        if (columns.plusX && columns.towerOnPlusX)
            bases.Add(ColumnTop(radius, height, wallThickness, wallHeight, columnHeight, true));
        if (columns.minusX && columns.towerOnMinusX)
            bases.Add(ColumnTop(radius, height, wallThickness, wallHeight, columnHeight, false));

        if (bases.Count == 0)
            bases.Add(new Vector3(towerOffset.x, Mathf.Max(0f, height), towerOffset.y));
        return bases;
    }

    /// <summary>The top centre of the column at +x or -x: in the middle of the ring wall, or on the edge.</summary>
    public static Vector3 ColumnTop(float radius, float height, float wallThickness, float wallHeight,
                                    float columnHeight, bool plusX)
    {
        float R = Mathf.Max(0.01f, radius);
        float c = WallInset(R, wallThickness, wallHeight) * 0.5f;
        float x = R - c;
        return new Vector3(plusX ? x : -x, Mathf.Max(0f, height) + Mathf.Max(0f, columnHeight), 0f);
    }

    /// <summary>The ring wall's thickness as built: short of leaving no floor, none without height.</summary>
    public static float WallInset(float radius, float wallThickness, float wallHeight)
    {
        float t = Mathf.Clamp(wallThickness, 0f, Mathf.Max(0.01f, radius) - 0.001f);
        return t > 0.0001f && wallHeight > 0.0001f ? t : 0f;
    }

    /// <summary>Radius of the floor inside the ring wall — the whole top when there is no wall.</summary>
    public static float FloorRadius(float radius, float wallThickness, float wallHeight)
        => Mathf.Max(0f, Mathf.Max(0.01f, radius) - WallInset(radius, wallThickness, wallHeight));

    // A flat ring facing up, between two radii.
    static void AppendAnnulus(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                              float y, float inner, float outer)
    {
        int start = verts.Count;
        for (int i = 0; i <= Sides; i++)
        {
            float   a   = i * Mathf.PI * 2f / Sides;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 pi  = dir * inner + Vector3.up * y;
            Vector3 po  = dir * outer + Vector3.up * y;
            verts.Add(pi); norms.Add(Vector3.up); uvs.Add(new Vector2(pi.x, pi.z));
            verts.Add(po); norms.Add(Vector3.up); uvs.Add(new Vector2(po.x, po.z));
        }
        for (int i = 0; i < Sides; i++)
        {
            int i0 = start + i * 2, o0 = i0 + 1, i1 = i0 + 2, o1 = i0 + 3;
            InstallationOutpostMesh.AddTri(tris, verts, norms, i0, o0, o1);
            InstallationOutpostMesh.AddTri(tris, verts, norms, i0, o1, i1);
        }
    }

    // The inside face of the ring wall, its normals pointing in towards the middle.
    static void AppendInnerWall(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                                float r, float yBottom, float yTop)
    {
        int start = verts.Count;
        for (int i = 0; i <= Sides; i++)
        {
            float   a = i * Mathf.PI * 2f / Sides;
            Vector3 n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float   u = a * r;

            verts.Add(n * r + Vector3.up * yBottom); norms.Add(-n); uvs.Add(new Vector2(u, yBottom));
            verts.Add(n * r + Vector3.up * yTop);    norms.Add(-n); uvs.Add(new Vector2(u, yTop));
        }
        for (int i = 0; i < Sides; i++)
        {
            int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            InstallationOutpostMesh.AddTri(tris, verts, norms, b0, t0, t1);
            InstallationOutpostMesh.AddTri(tris, verts, norms, b0, t1, b1);
        }
    }
}
