using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on a generated installation outpost and records what the Level Select Designer needs to
/// find it again: which outpost it was built for, and the name of the mesh asset it writes to.
///
/// The same job <see cref="LevelSelectArenaWallMesh"/> does for an arena wall — an edit to an
/// outpost rebuilds the one in the scene from this, without a full regenerate.
/// </summary>
public class InstallationOutpostMesh : MonoBehaviour
{
    [Tooltip("DesignerOutpost this block was built for.")]
    public string outpostId;

    [Tooltip("Which bank this block stands on. An outpost on both banks has one block for each.")]
    public LevelSelectDesignerData.OutpostSide bank;

    [Tooltip("Name of the generated mesh asset this block writes to.")]
    public string meshAssetName;

    /// <summary>
    /// Builds the block in its own frame: x runs along the river centred on the origin, z runs
    /// away from the river starting at the run's outer edge (z = 0), and y = 0 is the run's rim
    /// top. The floor stands at +height, the wall round the top carries on to +height +
    /// wallHeight, and the sides go down to -drop.
    ///
    /// UVs are world units, planar per face, the same density as the arena walls. A wall with no thickness or no height leaves a flat top.
    /// A <paramref name="tower"/> stands on the floor, moved off its centre by
    /// <paramref name="towerOffset"/> (x along the river, y away from it).
    ///
    /// <paramref name="columns"/> stands a round column on either or both back corners, centred in
    /// the wall's corner, from the drop up to <paramref name="columnHeight"/> above the floor. Each
    /// can carry a tower on its top; when any does, no tower stands on the floor and the offset is
    /// not used.
    ///
    /// <paramref name="opening"/> cuts a gap in the wall facing the river, and can stand a slope on
    /// the walkway in front of it (z below 0): flat in front of the gap, then straight down along
    /// the walkway to its top.
    ///
    /// Comes out carrying the river run stone shading — see <see cref="RiverMeshBuilder.ShadeAsStone"/>.
    /// </summary>
    public static Mesh Build(float width, float depth, float height, float drop,
                             float wallThickness, float wallHeight,
                             LollipopTower tower = null, Vector2 towerOffset = default,
                             RiverRunShadingSettings shading = null,
                             Columns columns = default, float columnRadius = 0.1f, float columnHeight = 0.2f,
                             float rimTopY = 0f, Opening opening = default)
    {
        float hw = Mathf.Max(0.005f, width) * 0.5f;
        float d  = Mathf.Max(0.01f, depth);
        float yB = -Mathf.Max(0f, drop);
        float yF = Mathf.Max(0f, height);

        // The wall can be no thicker than leaves some floor between its two sides.
        float t = WallInset(hw, d, wallThickness, wallHeight);
        bool  walled = t > 0f;
        float yT = walled ? yF + wallHeight : yF;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        // The stone colour each part takes, tagged after each part is added. Auto where there are
        // no settings, which leaves it worked out off the lip as before.
        var kinds = new List<float>();
        var parts = new List<int>();   // only the tower fills this — its tiers' joins are seams
        StoneFaceKind Choice(System.Func<RiverRunShadingSettings, StoneFaceKind> pick) =>
            shading != null ? pick(shading) : StoneFaceKind.Auto;

        // The opening's span along x, kept clear of the wall's corners. Only a walled top has a
        // gap cut in it; the slope still stands in front of the span without one.
        bool  spanned = OpeningSpan(hw, t, opening, out float g0, out float g1) && opening.on;
        bool  cut     = spanned && walled;

        // ── Outside faces, rim top of the wall down to the drop ──
        // Facing the river.
        if (!cut)
        {
            Quad(verts, norms, uvs, tris, Vector3.back,
                 new Vector3(-hw, yB, 0f), new Vector3(hw, yB, 0f),
                 new Vector3(hw, yT, 0f),  new Vector3(-hw, yT, 0f), UvX);
        }
        else
        {
            // Under the gap the face stops at the floor; either side it carries on up to the
            // wall's top. Split level with the floor all the way across, so every piece meets the
            // next edge to edge — a piece ending partway along its neighbour's edge reads as a
            // seam drawn across the face.
            Quad(verts, norms, uvs, tris, Vector3.back,
                 new Vector3(-hw, yB, 0f), new Vector3(g0, yB, 0f),
                 new Vector3(g0, yF, 0f),  new Vector3(-hw, yF, 0f), UvX);
            Quad(verts, norms, uvs, tris, Vector3.back,
                 new Vector3(g0, yB, 0f), new Vector3(g1, yB, 0f),
                 new Vector3(g1, yF, 0f), new Vector3(g0, yF, 0f), UvX);
            Quad(verts, norms, uvs, tris, Vector3.back,
                 new Vector3(g1, yB, 0f), new Vector3(hw, yB, 0f),
                 new Vector3(hw, yF, 0f), new Vector3(g1, yF, 0f), UvX);
            Quad(verts, norms, uvs, tris, Vector3.back,
                 new Vector3(-hw, yF, 0f), new Vector3(g0, yF, 0f),
                 new Vector3(g0, yT, 0f),  new Vector3(-hw, yT, 0f), UvX);
            Quad(verts, norms, uvs, tris, Vector3.back,
                 new Vector3(g1, yF, 0f), new Vector3(hw, yF, 0f),
                 new Vector3(hw, yT, 0f), new Vector3(g1, yT, 0f), UvX);
        }
        // Back, away from the river.
        Quad(verts, norms, uvs, tris, Vector3.forward,
             new Vector3(hw, yB, d),  new Vector3(-hw, yB, d),
             new Vector3(-hw, yT, d), new Vector3(hw, yT, d), UvX);
        // The two ends, up and down the river.
        Quad(verts, norms, uvs, tris, Vector3.left,
             new Vector3(-hw, yB, d),  new Vector3(-hw, yB, 0f),
             new Vector3(-hw, yT, 0f), new Vector3(-hw, yT, d), UvZ);
        Quad(verts, norms, uvs, tris, Vector3.right,
             new Vector3(hw, yB, 0f), new Vector3(hw, yB, d),
             new Vector3(hw, yT, d),  new Vector3(hw, yT, 0f), UvZ);
        StoneFaces.Tag(kinds, tris, Choice(s => s.outpostOutsideWalls));
        PartLabels.Mark("Block", verts, tris);

        if (!walled)
        {
            Quad(verts, norms, uvs, tris, Vector3.up,
                 new Vector3(-hw, yF, 0f), new Vector3(hw, yF, 0f),
                 new Vector3(hw, yF, d),   new Vector3(-hw, yF, d), UvTop);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostFloor));
            PartLabels.Mark("Block", verts, tris);
        }
        else
        {
            float ix0 = -hw + t, ix1 = hw - t;   // the floor, inside the wall
            float iz0 = t,       iz1 = d - t;

            // ── Top of the wall: four strips mitred at the corners ──
            // Mitred rather than overlapped, so every edge of the wall top is shared exactly with
            // the face beside it. The stone shading finds its seams from which edges meet, and a
            // strip ending partway along its neighbour's edge reads as a seam drawn across the
            // wall top at every corner.
            if (!cut)
            {
                Quad(verts, norms, uvs, tris, Vector3.up,
                     new Vector3(-hw, yT, 0f),  new Vector3(hw, yT, 0f),
                     new Vector3(ix1, yT, iz0), new Vector3(ix0, yT, iz0), UvTop);
            }
            else
            {
                // The front strip either side of the gap, each still mitred at its corner.
                Quad(verts, norms, uvs, tris, Vector3.up,
                     new Vector3(-hw, yT, 0f), new Vector3(g0, yT, 0f),
                     new Vector3(g0, yT, iz0), new Vector3(ix0, yT, iz0), UvTop);
                Quad(verts, norms, uvs, tris, Vector3.up,
                     new Vector3(g1, yT, 0f),  new Vector3(hw, yT, 0f),
                     new Vector3(ix1, yT, iz0), new Vector3(g1, yT, iz0), UvTop);
            }
            Quad(verts, norms, uvs, tris, Vector3.up,
                 new Vector3(ix0, yT, iz1), new Vector3(ix1, yT, iz1),
                 new Vector3(hw, yT, d),    new Vector3(-hw, yT, d), UvTop);
            Quad(verts, norms, uvs, tris, Vector3.up,
                 new Vector3(-hw, yT, 0f),  new Vector3(ix0, yT, iz0),
                 new Vector3(ix0, yT, iz1), new Vector3(-hw, yT, d), UvTop);
            Quad(verts, norms, uvs, tris, Vector3.up,
                 new Vector3(ix1, yT, iz0), new Vector3(hw, yT, 0f),
                 new Vector3(hw, yT, d),    new Vector3(ix1, yT, iz1), UvTop);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostWallTop));
            PartLabels.Mark("Wall", verts, tris);

            // ── Inside faces of the wall, facing in across the floor ──
            if (!cut)
            {
                Quad(verts, norms, uvs, tris, Vector3.forward,
                     new Vector3(ix1, yF, iz0), new Vector3(ix0, yF, iz0),
                     new Vector3(ix0, yT, iz0), new Vector3(ix1, yT, iz0), UvX);
            }
            else
            {
                Quad(verts, norms, uvs, tris, Vector3.forward,
                     new Vector3(g0, yF, iz0),  new Vector3(ix0, yF, iz0),
                     new Vector3(ix0, yT, iz0), new Vector3(g0, yT, iz0), UvX);
                Quad(verts, norms, uvs, tris, Vector3.forward,
                     new Vector3(ix1, yF, iz0), new Vector3(g1, yF, iz0),
                     new Vector3(g1, yT, iz0),  new Vector3(ix1, yT, iz0), UvX);
                StoneFaces.Tag(kinds, tris, Choice(s => s.outpostInsideWalls));
                PartLabels.Mark("Wall", verts, tris);

                // The two sides of the gap, facing each other across it.
                Quad(verts, norms, uvs, tris, Vector3.right,
                     new Vector3(g0, yF, 0f),  new Vector3(g0, yF, iz0),
                     new Vector3(g0, yT, iz0), new Vector3(g0, yT, 0f), UvZ);
                Quad(verts, norms, uvs, tris, Vector3.left,
                     new Vector3(g1, yF, iz0), new Vector3(g1, yF, 0f),
                     new Vector3(g1, yT, 0f),  new Vector3(g1, yT, iz0), UvZ);
                StoneFaces.Tag(kinds, tris, Choice(s => s.outpostOpeningSides));
                PartLabels.Mark("Opening", verts, tris);
            }
            Quad(verts, norms, uvs, tris, Vector3.back,
                 new Vector3(ix0, yF, iz1), new Vector3(ix1, yF, iz1),
                 new Vector3(ix1, yT, iz1), new Vector3(ix0, yT, iz1), UvX);
            Quad(verts, norms, uvs, tris, Vector3.right,
                 new Vector3(ix0, yF, iz0), new Vector3(ix0, yF, iz1),
                 new Vector3(ix0, yT, iz1), new Vector3(ix0, yT, iz0), UvZ);
            Quad(verts, norms, uvs, tris, Vector3.left,
                 new Vector3(ix1, yF, iz1), new Vector3(ix1, yF, iz0),
                 new Vector3(ix1, yT, iz0), new Vector3(ix1, yT, iz1), UvZ);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostInsideWalls));
            PartLabels.Mark("Wall", verts, tris);

            // ── The floor ──
            if (!cut)
            {
                Quad(verts, norms, uvs, tris, Vector3.up,
                     new Vector3(ix0, yF, iz0), new Vector3(ix1, yF, iz0),
                     new Vector3(ix1, yF, iz1), new Vector3(ix0, yF, iz1), UvTop);
            }
            else
            {
                // Carried out through the gap to the face, in pieces that meet edge to edge — the
                // strip in the gap is its own piece, so the floor behind it lines up with the
                // inside of the wall either side.
                Quad(verts, norms, uvs, tris, Vector3.up,
                     new Vector3(ix0, yF, iz0), new Vector3(g0, yF, iz0),
                     new Vector3(g0, yF, iz1),  new Vector3(ix0, yF, iz1), UvTop);
                Quad(verts, norms, uvs, tris, Vector3.up,
                     new Vector3(g0, yF, iz0), new Vector3(g1, yF, iz0),
                     new Vector3(g1, yF, iz1), new Vector3(g0, yF, iz1), UvTop);
                Quad(verts, norms, uvs, tris, Vector3.up,
                     new Vector3(g1, yF, iz0),  new Vector3(ix1, yF, iz0),
                     new Vector3(ix1, yF, iz1), new Vector3(g1, yF, iz1), UvTop);
                StoneFaces.Tag(kinds, tris, Choice(s => s.outpostFloor));
                PartLabels.Mark("Floor", verts, tris);

                // The strip of floor through the gap itself.
                Quad(verts, norms, uvs, tris, Vector3.up,
                     new Vector3(g0, yF, 0f),  new Vector3(g1, yF, 0f),
                     new Vector3(g1, yF, iz0), new Vector3(g0, yF, iz0), UvTop);
                StoneFaces.Tag(kinds, tris, Choice(s => s.outpostOpeningFloor));
                PartLabels.Mark("Opening", verts, tris);
            }
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostFloor));
            PartLabels.Mark("Floor", verts, tris);
        }

        // ── The slope on the walkway, in front of the opening ──
        // Flat at the floor's height in front of the gap, then straight down along the walkway to
        // its top. A solid standing on the walkway: no underside, it sits on the walkway's top.
        if (spanned && opening.slope)
        {
            float yW = opening.walkwayHeight;
            float zO = -Mathf.Max(0.001f, opening.slopeWidth);
            if (yF - yW > 0.001f)
            {
                float s  = opening.slopeSign >= 0f ? 1f : -1f;
                float xA = s > 0f ? g0 : g1;                                    // far edge of the gap
                float xL = xA + s * Mathf.Max(opening.landing, g1 - g0);       // end of the flat
                float L  = Mathf.Max(0.01f, opening.slopeLength);
                float xS = xL + s * L;                                          // foot of the slope
                Vector3 slopeUp = new Vector3(s * (yF - yW), L, 0f).normalized;

                Quad(verts, norms, uvs, tris, Vector3.up,
                     new Vector3(xA, yF, zO), new Vector3(xL, yF, zO),
                     new Vector3(xL, yF, 0f), new Vector3(xA, yF, 0f), UvTop);
                Quad(verts, norms, uvs, tris, slopeUp,
                     new Vector3(xL, yF, zO), new Vector3(xS, yW, zO),
                     new Vector3(xS, yW, 0f), new Vector3(xL, yF, 0f), UvTop);
                StoneFaces.Tag(kinds, tris, Choice(st => st.outpostSlopeTop));
                PartLabels.Mark("Slope", verts, tris);

                // Its two long sides: the river side, and the side against the block.
                foreach (var (z, n) in new[] { (zO, Vector3.back), (0f, Vector3.forward) })
                {
                    Quad(verts, norms, uvs, tris, n,
                         new Vector3(xA, yW, z), new Vector3(xL, yW, z),
                         new Vector3(xL, yF, z), new Vector3(xA, yF, z), UvX);
                    Tri(verts, norms, uvs, tris, n,
                        new Vector3(xL, yW, z), new Vector3(xS, yW, z), new Vector3(xL, yF, z), UvX);
                }
                StoneFaces.Tag(kinds, tris, Choice(st => st.outpostSlopeSides));
                PartLabels.Mark("Slope", verts, tris);

                // The square end behind the flat.
                Quad(verts, norms, uvs, tris, s > 0f ? Vector3.left : Vector3.right,
                     new Vector3(xA, yW, zO), new Vector3(xA, yW, 0f),
                     new Vector3(xA, yF, 0f), new Vector3(xA, yF, zO), UvZ);
                StoneFaces.Tag(kinds, tris, Choice(st => st.outpostSlopeEnd));
                PartLabels.Mark("Slope", verts, tris);
            }
        }

        // ── The columns on the back corners, from the drop up past the floor ──
        float r = Mathf.Max(0.001f, columnRadius);
        foreach (bool plusX in new[] { true, false })
        {
            if (!(plusX ? columns.plusX : columns.minusX)) continue;
            Vector3 top = ColumnTop(width, depth, height, wallThickness, wallHeight, columnHeight, plusX);
            AppendColumn(verts, norms, uvs, tris, new Vector3(top.x, 0f, top.z), r, yB, top.y);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostColumnSides));
            PartLabels.Mark("Column", verts, tris);
            AppendColumnCap(verts, norms, uvs, tris, top, r);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostColumnTop));
            PartLabels.Mark("Column", verts, tris);
        }

        // The towers stand on the columns picked to carry one — or, when none does, the one tower
        // stands on the floor, moved off its centre by the offset. Their parts take the stone
        // colours the tower settings give them, and the tower Designation's Scale.
        var designations = new List<StoneDesignation>();
        StoneFaces.Designate(designations, tris, RiverRunShadingSettings.OutpostAs(shading));
        if (tower != null)
            foreach (var towerBase in TowerBases(width, depth, height, wallThickness, wallHeight,
                                                 towerOffset, columns, columnHeight))
                tower.Append(verts, norms, uvs, tris, towerBase, kinds, shading, parts);
        StoneFaces.Designate(designations, tris, RiverRunShadingSettings.TowerAs(shading));

        var mesh = new Mesh { name = "InstallationOutpost" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // River run stone shading. The waterline is placed off the run's rim top, which is this
        // mesh's y = 0; the outpost's own lip is the top of its wall, which is what any part left
        // on Auto has its colour worked out from.
        return RiverMeshBuilder.ShadeAsStone(mesh, rimTopY, yT, kinds, parts,
                                             faceDesignations: designations);
    }

    /// <summary>
    /// Which back corners carry a column, and which of those columns carry a tower — each end
    /// given as the mesh's +x or -x.
    /// </summary>
    public struct Columns
    {
        public bool plusX, minusX;
        public bool towerOnPlusX, towerOnMinusX;
    }

    /// <summary>
    /// A gap in the wall facing the river, and the slope in front of it — everything given in the
    /// mesh's own x, already turned for the bank it stands on.
    /// </summary>
    public struct Opening
    {
        public bool  on;
        public float centre, width;          // the gap, along x
        public bool  slope;
        public float slopeSign;              // +1 runs toward +x from the gap
        public float landing, slopeLength;   // the flat part, then the slope, along x
        public float slopeWidth;             // how far the slope reaches out in front of the face
        public float walkwayHeight;          // the walkway's top, which the slope runs down to
    }

    // Where the gap reaches along x: inside the wall's corners on a walled top, so a sliver of
    // the front wall always stays at each end, or anywhere along the face without a wall.
    static bool OpeningSpan(float hw, float t, Opening opening, out float g0, out float g1)
    {
        const float corner = 0.002f;   // over the millimetre the stone shading welds corners on
        float lo = t > 0f ? -hw + t + corner : -hw;
        float hi = t > 0f ?  hw - t - corner :  hw;
        float half = Mathf.Max(0f, opening.width) * 0.5f;
        g0 = Mathf.Clamp(opening.centre - half, lo, hi);
        g1 = Mathf.Clamp(opening.centre + half, lo, hi);
        return g1 - g0 > 0.001f;
    }

    /// <summary>
    /// Where each tower's base stands, in the block's frame (see <see cref="Build"/>): on top of
    /// every column picked to carry one, or — when none does — on the floor, moved off its centre
    /// by the offset.
    /// </summary>
    public static List<Vector3> TowerBases(float width, float depth, float height,
                                           float wallThickness, float wallHeight,
                                           Vector2 towerOffset, Columns columns, float columnHeight)
    {
        var bases = new List<Vector3>();
        if (columns.plusX && columns.towerOnPlusX)
            bases.Add(ColumnTop(width, depth, height, wallThickness, wallHeight, columnHeight, true));
        if (columns.minusX && columns.towerOnMinusX)
            bases.Add(ColumnTop(width, depth, height, wallThickness, wallHeight, columnHeight, false));

        if (bases.Count == 0)
            bases.Add(new Vector3(towerOffset.x, Mathf.Max(0f, height),
                                  Mathf.Max(0.01f, depth) * 0.5f + towerOffset.y));
        return bases;
    }

    /// <summary>
    /// The top centre of the column on one back corner, in the block's frame. It stands in the
    /// middle of the wall's corner — the square where the back wall meets the end wall — or on the
    /// block's very corner when there is no wall.
    /// </summary>
    public static Vector3 ColumnTop(float width, float depth, float height,
                                    float wallThickness, float wallHeight, float columnHeight, bool plusX)
    {
        float hw = Mathf.Max(0.005f, width) * 0.5f;
        float d  = Mathf.Max(0.01f, depth);
        float yF = Mathf.Max(0f, height);
        float c  = WallInset(hw, d, wallThickness, wallHeight) * 0.5f;

        return new Vector3(plusX ? hw - c : -(hw - c), yF + Mathf.Max(0f, columnHeight), d - c);
    }

    /// <summary>
    /// The wall's thickness as the block is built with it: capped short of leaving no floor, and
    /// none at all when the wall has no thickness or no height.
    /// </summary>
    public static float WallInset(float halfWidth, float depth, float wallThickness, float wallHeight)
    {
        float t = Mathf.Clamp(wallThickness, 0f, Mathf.Min(halfWidth, depth * 0.5f) - 0.001f);
        return t > 0.0001f && wallHeight > 0.0001f ? t : 0f;
    }

    internal const int ColumnSides = 24;

    // The column's round sides, smooth, one column of vertices doubled at the seam so u runs round unbroken.
    internal static void AppendColumn(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                                      Vector3 centre, float r, float yBottom, float yTop, int sides = ColumnSides)
    {
        int start = verts.Count;
        for (int i = 0; i <= sides; i++)
        {
            float   a = i * Mathf.PI * 2f / sides;
            Vector3 n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float   u = a * r;

            verts.Add(centre + n * r + Vector3.up * yBottom); norms.Add(n); uvs.Add(new Vector2(u, yBottom));
            verts.Add(centre + n * r + Vector3.up * yTop);    norms.Add(n); uvs.Add(new Vector2(u, yTop));
        }
        for (int i = 0; i < sides; i++)
        {
            int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            AddTri(tris, verts, norms, b0, t0, t1);
            AddTri(tris, verts, norms, b0, t1, b1);
        }
    }

    // The flat top of the column, which the tower stands on.
    internal static void AppendColumnCap(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                                         Vector3 top, float r, int sides = ColumnSides)
    {
        int centre = verts.Count;
        verts.Add(top); norms.Add(Vector3.up); uvs.Add(new Vector2(top.x, top.z));
        for (int i = 0; i <= sides; i++)
        {
            float   a = i * Mathf.PI * 2f / sides;
            Vector3 p = top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            verts.Add(p); norms.Add(Vector3.up); uvs.Add(new Vector2(p.x, p.z));
        }
        for (int i = 0; i < sides; i++)
            AddTri(tris, verts, norms, centre, centre + 1 + i, centre + 2 + i);
    }

    // One triangle, wound so its face points the way its vertices' normals do.
    internal static void AddTri(List<int> tris, List<Vector3> verts, List<Vector3> norms, int a, int b, int c)
    {
        Vector3 face = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        Vector3 n    = norms[a] + norms[b] + norms[c];
        if (Vector3.Dot(face, n) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
        else                            { tris.Add(a); tris.Add(c); tris.Add(b); }
    }

    // Planar UVs in world units, picked by which way the face looks.
    static Vector2 UvX(Vector3 p)   => new Vector2(p.x, p.y);
    static Vector2 UvZ(Vector3 p)   => new Vector2(p.z, p.y);
    static Vector2 UvTop(Vector3 p) => new Vector2(p.x, p.z);

    // One flat triangle, wound the same way Quad is.
    static void Tri(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                    Vector3 n, Vector3 p0, Vector3 p1, Vector3 p2, System.Func<Vector3, Vector2> uv)
    {
        int v = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2);
        norms.Add(n);  norms.Add(n);  norms.Add(n);
        uvs.Add(uv(p0)); uvs.Add(uv(p1)); uvs.Add(uv(p2));

        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) > 0f) { tris.Add(v); tris.Add(v + 1); tris.Add(v + 2); }
        else                                                       { tris.Add(v); tris.Add(v + 2); tris.Add(v + 1); }
    }

    // One flat quad. The winding is checked against the normal and flipped where it disagrees,
    // the same test ProceduralArenaWallMesh uses, so the corner order above only has to go round.
    static void Quad(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                     Vector3 n, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                     System.Func<Vector3, Vector2> uv)
    {
        int v = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
        norms.Add(n);  norms.Add(n);  norms.Add(n);  norms.Add(n);
        uvs.Add(uv(p0)); uvs.Add(uv(p1)); uvs.Add(uv(p2)); uvs.Add(uv(p3));

        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) > 0f)
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
}
