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
    /// Comes out carrying the river run stone shading — see <see cref="RiverMeshBuilder.ShadeAsStone"/>.
    /// </summary>
    public static Mesh Build(float width, float depth, float height, float drop,
                             float wallThickness, float wallHeight,
                             LollipopTower tower = null, Vector2 towerOffset = default,
                             RiverRunShadingSettings shading = null)
    {
        float hw = Mathf.Max(0.005f, width) * 0.5f;
        float d  = Mathf.Max(0.01f, depth);
        float yB = -Mathf.Max(0f, drop);
        float yF = Mathf.Max(0f, height);

        // The wall can be no thicker than leaves some floor between its two sides.
        float t = Mathf.Clamp(wallThickness, 0f, Mathf.Min(hw, d * 0.5f) - 0.001f);
        bool  walled = t > 0.0001f && wallHeight > 0.0001f;
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

        // ── Outside faces, rim top of the wall down to the drop ──
        // Facing the river.
        Quad(verts, norms, uvs, tris, Vector3.back,
             new Vector3(-hw, yB, 0f), new Vector3(hw, yB, 0f),
             new Vector3(hw, yT, 0f),  new Vector3(-hw, yT, 0f), UvX);
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

        if (!walled)
        {
            Quad(verts, norms, uvs, tris, Vector3.up,
                 new Vector3(-hw, yF, 0f), new Vector3(hw, yF, 0f),
                 new Vector3(hw, yF, d),   new Vector3(-hw, yF, d), UvTop);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostFloor));
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
            Quad(verts, norms, uvs, tris, Vector3.up,
                 new Vector3(-hw, yT, 0f),  new Vector3(hw, yT, 0f),
                 new Vector3(ix1, yT, iz0), new Vector3(ix0, yT, iz0), UvTop);
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

            // ── Inside faces of the wall, facing in across the floor ──
            Quad(verts, norms, uvs, tris, Vector3.forward,
                 new Vector3(ix1, yF, iz0), new Vector3(ix0, yF, iz0),
                 new Vector3(ix0, yT, iz0), new Vector3(ix1, yT, iz0), UvX);
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

            // ── The floor ──
            Quad(verts, norms, uvs, tris, Vector3.up,
                 new Vector3(ix0, yF, iz0), new Vector3(ix1, yF, iz0),
                 new Vector3(ix1, yF, iz1), new Vector3(ix0, yF, iz1), UvTop);
            StoneFaces.Tag(kinds, tris, Choice(s => s.outpostFloor));
        }

        // The tower's base stands on the floor, moved off the floor's centre by the offset. Its
        // parts take the stone colours the tower settings give them.
        if (tower != null)
            tower.Append(verts, norms, uvs, tris,
                         new Vector3(towerOffset.x, yF, d * 0.5f + towerOffset.y), kinds, shading, parts);

        var mesh = new Mesh { name = "InstallationOutpost" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // River run stone shading. The waterline is placed off the run's rim top, which is this
        // mesh's y = 0; the outpost's own lip is the top of its wall, which is what any part left
        // on Auto has its colour worked out from.
        return RiverMeshBuilder.ShadeAsStone(mesh, 0f, yT, kinds, parts);
    }

    // Planar UVs in world units, picked by which way the face looks.
    static Vector2 UvX(Vector3 p)   => new Vector2(p.x, p.y);
    static Vector2 UvZ(Vector3 p)   => new Vector2(p.z, p.y);
    static Vector2 UvTop(Vector3 p) => new Vector2(p.x, p.z);

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
