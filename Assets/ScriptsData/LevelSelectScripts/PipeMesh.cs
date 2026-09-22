using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on a generated pipe and records what the Level Select Designer needs to find it again:
/// which pipe it was built for, and the name of the mesh asset it writes to. The same job
/// <see cref="InstallationOutpostMesh"/> does for an outpost.
///
/// Also builds the pipe. Seen from the side:
///
///        ___                  ___
///   ====|   |================|   |====   <- the pipe (hollow, open at both ends), rounded at bends
///   ====|___|================|___|====   <- a ring round the pipe at every support
///         |                    |
///         |                    |         <- support stems, down to the one Drop
///
/// A ring stands out from the pipe by the ring overhang and is as long as the stem is thick.
/// </summary>
public class PipeMesh : MonoBehaviour
{
    [Tooltip("DesignerPipe this pipe was built for.")]
    public string pipeId;

    [Tooltip("Name of the generated mesh asset this pipe writes to.")]
    public string meshAssetName;

    const int PipeSides = 16;
    const int StemSides = 12;

    /// <summary>How many pieces each rounded bend is cut into.</summary>
    public const int BendSteps = 4;

    /// <summary>One support: which leg of the pipe it is on (node i to node i + 1), and how far along it.</summary>
    public struct Support
    {
        public int   leg;
        public float along;
    }

    /// <summary>
    /// The centre line of a pipe through <paramref name="nodes"/>, rounded at every node between
    /// its two ends. Each bend is cut into <see cref="BendSteps"/> pieces. Its radius is
    /// <paramref name="bendRadius"/>, eased tighter where the legs either side are too short to
    /// hold it, so two bends never overlap.
    /// </summary>
    public static List<Vector3> CentreLine(IList<Vector3> nodes, float bendRadius)
    {
        var line = new List<Vector3>();
        if (nodes == null || nodes.Count == 0) return line;

        line.Add(nodes[0]);
        for (int i = 1; i < nodes.Count - 1; i++)
        {
            Vector3 p  = nodes[i];
            Vector3 a  = p - nodes[i - 1];
            Vector3 b  = nodes[i + 1] - p;
            float   la = a.magnitude, lb = b.magnitude;
            if (la < 1e-5f || lb < 1e-5f) { AddPoint(line, p); continue; }

            Vector3 da = a / la, db = b / lb;
            float   turn = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f));
            if (turn < 0.01f) { AddPoint(line, p); continue; }

            // How far back up each leg the bend starts, for a round bend of this radius.
            float d = Mathf.Max(0f, bendRadius) * Mathf.Tan(Mathf.Min(turn, 3.0f) * 0.5f);
            d = Mathf.Min(d, la * 0.5f, lb * 0.5f);
            if (d < 1e-4f) { AddPoint(line, p); continue; }

            Vector3 from = p - da * d, to = p + db * d;
            for (int k = 0; k <= BendSteps; k++)
            {
                float t = k / (float)BendSteps, s = 1f - t;
                AddPoint(line, s * s * from + 2f * s * t * p + t * t * to);
            }
        }
        AddPoint(line, nodes[nodes.Count - 1]);
        return line;
    }

    static void AddPoint(List<Vector3> line, Vector3 p)
    {
        if (line.Count == 0 || (line[line.Count - 1] - p).sqrMagnitude > 1e-10f) line.Add(p);
    }

    /// <summary>
    /// Where a support stands on the rounded centre line, and the way the pipe runs there. The
    /// support is placed on its straight leg first and then carried onto the nearest point of
    /// the centre line, so one sitting in a bend lands on the bend.
    /// </summary>
    public static void SupportOnLine(IList<Vector3> nodes, List<Vector3> line, Support support,
                                     out Vector3 position, out Vector3 tangent)
    {
        int     leg      = Mathf.Clamp(support.leg, 0, nodes.Count - 2);
        Vector3 straight = Vector3.Lerp(nodes[leg], nodes[leg + 1], Mathf.Clamp01(support.along));

        position = straight;
        tangent  = (nodes[leg + 1] - nodes[leg]).normalized;

        float best = float.MaxValue;
        for (int i = 0; i < line.Count - 1; i++)
        {
            Vector3 a = line[i], ab = line[i + 1] - a;
            float   len2 = ab.sqrMagnitude;
            if (len2 < 1e-10f) continue;

            float   t = Mathf.Clamp01(Vector3.Dot(straight - a, ab) / len2);
            Vector3 q = a + ab * t;
            float   dist = (q - straight).sqrMagnitude;
            if (dist >= best) continue;

            best     = dist;
            position = q;
            tangent  = ab / Mathf.Sqrt(len2);
        }
    }

    /// <summary>
    /// Builds a pipe in its own frame, where y = 0 is the rim top the rivers are built from.
    /// <paramref name="nodes"/> are the pipe's centre-line points; <paramref name="bottomY"/> is
    /// how far down the support stems go. Thicknesses are across: the pipe's outside, its wall,
    /// and the stem. The bend radius follows the pipe's thickness.
    ///
    /// Comes out carrying the river run stone shading — see <see cref="RiverMeshBuilder.ShadeAsStone"/>.
    /// </summary>
    public static Mesh Build(IList<Vector3> nodes, IList<Support> supports,
                             float pipeThickness, float wallThickness,
                             float supportThickness, float ringOverhang, float bottomY)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris  = new List<int>();
        var parts = new List<int>();

        float outer = Mathf.Max(0.005f, pipeThickness * 0.5f);
        float inner = outer - Mathf.Max(0f, wallThickness);
        float stemR = Mathf.Max(0.0025f, supportThickness * 0.5f);

        var line = CentreLine(nodes, outer * 2f);
        if (line.Count >= 2)
        {
            AppendTube(verts, norms, tris, line, outer, inner);
            StoneFaces.Part(parts, tris, 0);

            if (supports != null)
            {
                foreach (var support in supports)
                {
                    SupportOnLine(nodes, line, support, out Vector3 at, out Vector3 along);

                    if (ringOverhang > 0.0001f)
                    {
                        AppendRing(verts, norms, tris, at, along, outer, outer + ringOverhang, stemR);
                        StoneFaces.Part(parts, tris, 1);
                    }

                    // Up to the underside of the pipe; the ring covers the join. A pipe on a slope
                    // is deeper measured straight down, so the stem reaches further to meet it.
                    float level = Mathf.Max(0.2f, new Vector2(along.x, along.z).magnitude);
                    float top   = at.y - outer / level;
                    if (top > bottomY)
                    {
                        AppendStem(verts, norms, tris, new Vector3(at.x, bottomY, at.z), top - bottomY, stemR);
                        StoneFaces.Part(parts, tris, 2);
                    }
                }
            }
        }

        var mesh = new Mesh { name = "Pipe" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // River run stone shading, the waterline placed off the rim top at y = 0.
        return RiverMeshBuilder.ShadeAsStone(mesh, 0f, 0f, null, parts);
    }

    /// <summary>
    /// One straight piece of a pipe's collision: a capsule of this radius running from
    /// <see cref="from"/> to <see cref="to"/>, in the same frame the mesh is built in.
    /// </summary>
    public struct Rod
    {
        public Vector3 from;
        public Vector3 to;
        public float   radius;
    }

    /// <summary>
    /// The pipe as a handful of capsules rather than its mesh: one down each leg, one down
    /// each support stem. Takes the same arguments <see cref="Build"/> does, so a pipe and
    /// its collision always come out of the same numbers.
    ///
    /// The legs are taken straight from the nodes, not from the rounded centre line, so a
    /// bend is covered by the two capsules meeting at the node rather than cut into pieces.
    /// It reaches a little past the outside of a sharp bend, which is what wanting few
    /// capsules costs.
    /// </summary>
    public static List<Rod> CollisionRods(IList<Vector3> nodes, IList<Support> supports,
                                          float pipeThickness, float supportThickness,
                                          float bottomY)
    {
        var rods = new List<Rod>();
        if (nodes == null || nodes.Count < 2) return rods;

        float outer = Mathf.Max(0.005f, pipeThickness * 0.5f);
        float stemR = Mathf.Max(0.0025f, supportThickness * 0.5f);

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            if ((nodes[i + 1] - nodes[i]).sqrMagnitude < 1e-8f) continue;
            rods.Add(new Rod { from = nodes[i], to = nodes[i + 1], radius = outer });
        }

        if (supports == null) return rods;

        var line = CentreLine(nodes, outer * 2f);
        if (line.Count < 2) return rods;

        foreach (var support in supports)
        {
            SupportOnLine(nodes, line, support, out Vector3 at, out Vector3 along);

            // The same reach up to the pipe's underside the stem itself is built with.
            float level = Mathf.Max(0.2f, new Vector2(along.x, along.z).magnitude);
            float top   = at.y - outer / level;
            if (top <= bottomY) continue;

            rods.Add(new Rod
            {
                from   = new Vector3(at.x, bottomY, at.z),
                to     = new Vector3(at.x, top,     at.z),
                radius = stemR,
            });
        }

        return rods;
    }

    // ── The pipe: an outside, an inside, and a flat ring closing each open end ──
    static void AppendTube(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                           List<Vector3> line, float outer, float inner)
    {
        int count = line.Count;

        // The way the pipe runs at each point: along the leg at the two ends, halfway between
        // the pieces either side everywhere else.
        var tangents = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 back = i > 0         ? (line[i] - line[i - 1]).normalized : Vector3.zero;
            Vector3 fwd  = i < count - 1 ? (line[i + 1] - line[i]).normalized : Vector3.zero;
            Vector3 t    = back + fwd;
            tangents[i]  = t.sqrMagnitude > 1e-8f ? t.normalized : (fwd.sqrMagnitude > 0f ? fwd : back);
        }

        // A frame carried along the pipe without twisting, starting from straight up.
        var ups = new Vector3[count];
        ups[0] = StartUp(tangents[0]);
        for (int i = 1; i < count; i++)
        {
            Vector3 u = Quaternion.FromToRotation(tangents[i - 1], tangents[i]) * ups[i - 1];
            ups[i]    = Vector3.ProjectOnPlane(u, tangents[i]).normalized;
        }

        bool hollow = inner > 0.001f;

        for (int i = 0; i < count - 1; i++)
        {
            for (int k = 0; k < PipeSides; k++)
            {
                Vector3 n0a = Around(tangents[i],     ups[i],     k),     n0b = Around(tangents[i],     ups[i],     k + 1);
                Vector3 n1a = Around(tangents[i + 1], ups[i + 1], k),     n1b = Around(tangents[i + 1], ups[i + 1], k + 1);

                Quad(verts, norms, tris,
                     line[i] + n0a * outer, line[i] + n0b * outer,
                     line[i + 1] + n1b * outer, line[i + 1] + n1a * outer,
                     n0a, n0b, n1b, n1a);

                if (hollow)
                    Quad(verts, norms, tris,
                         line[i] + n0a * inner, line[i] + n0b * inner,
                         line[i + 1] + n1b * inner, line[i + 1] + n1a * inner,
                         -n0a, -n0b, -n1b, -n1a);
            }
        }

        EndFace(verts, norms, tris, line[0],         -tangents[0],         tangents[0],         ups[0],         outer, inner);
        EndFace(verts, norms, tris, line[count - 1],  tangents[count - 1], tangents[count - 1], ups[count - 1], outer, inner);
    }

    // The flat end of the pipe: a ring between the outside and inside, or a disc when solid.
    static void EndFace(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                        Vector3 centre, Vector3 facing, Vector3 tangent, Vector3 up,
                        float outer, float inner)
    {
        for (int k = 0; k < PipeSides; k++)
        {
            Vector3 a = Around(tangent, up, k), b = Around(tangent, up, k + 1);
            if (inner > 0.001f)
                Quad(verts, norms, tris,
                     centre + a * inner, centre + b * inner, centre + b * outer, centre + a * outer,
                     facing, facing, facing, facing);
            else
                Tri(verts, norms, tris, centre, centre + a * outer, centre + b * outer, facing);
        }
    }

    // ── A ring round the pipe: its outside, and a flat face at each end down onto the pipe ──
    static void AppendRing(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                           Vector3 centre, Vector3 along, float pipeOuter, float ringOuter, float halfLength)
    {
        Vector3 up = StartUp(along);
        Vector3 a0 = centre - along * halfLength, a1 = centre + along * halfLength;

        // The ends tuck just under the pipe's flats, so no slit shows between them.
        float tuck = pipeOuter * Mathf.Cos(Mathf.PI / PipeSides) * 0.98f;

        for (int k = 0; k < PipeSides; k++)
        {
            Vector3 na = Around(along, up, k), nb = Around(along, up, k + 1);
            Quad(verts, norms, tris,
                 a0 + na * ringOuter, a0 + nb * ringOuter, a1 + nb * ringOuter, a1 + na * ringOuter,
                 na, nb, nb, na);
            Quad(verts, norms, tris,
                 a0 + na * tuck, a0 + nb * tuck, a0 + nb * ringOuter, a0 + na * ringOuter,
                 -along, -along, -along, -along);
            Quad(verts, norms, tris,
                 a1 + na * tuck, a1 + nb * tuck, a1 + nb * ringOuter, a1 + na * ringOuter,
                 along, along, along, along);
        }
    }

    // ── A support stem: an upright cylinder, open at both ends (the bottom is lost below, the
    //    top inside the ring) ──
    static void AppendStem(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                           Vector3 basePos, float height, float radius)
    {
        Vector3 top = basePos + Vector3.up * height;
        for (int k = 0; k < StemSides; k++)
        {
            float   a0 = k * Mathf.PI * 2f / StemSides, a1 = (k + 1) * Mathf.PI * 2f / StemSides;
            Vector3 n0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            Vector3 n1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Quad(verts, norms, tris,
                 basePos + n0 * radius, basePos + n1 * radius, top + n1 * radius, top + n0 * radius,
                 n0, n1, n1, n0);
        }
    }

    // Straight up, flattened onto the plane across the pipe — or across the world when the pipe
    // itself runs straight up.
    static Vector3 StartUp(Vector3 tangent)
    {
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, tangent);
        if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, tangent);
        return up.normalized;
    }

    // The direction out from the centre to corner k of the pipe's cross-section. The corners
    // start at straight up, so no flat of a level pipe faces straight up — a dead-flat face
    // would read to the stone shading as a rim and draw a seam down the pipe.
    static Vector3 Around(Vector3 tangent, Vector3 up, int k)
    {
        float   a    = k * Mathf.PI * 2f / PipeSides;
        Vector3 side = Vector3.Cross(tangent, up);
        return up * Mathf.Cos(a) + side * Mathf.Sin(a);
    }

    static void Quad(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                     Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                     Vector3 n0, Vector3 n1, Vector3 n2, Vector3 n3)
    {
        int v = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
        norms.Add(n0); norms.Add(n1); norms.Add(n2); norms.Add(n3);
        AddTri(tris, verts, norms, v, v + 1, v + 2);
        AddTri(tris, verts, norms, v, v + 2, v + 3);
    }

    static void Tri(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                    Vector3 p0, Vector3 p1, Vector3 p2, Vector3 n)
    {
        int v = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2);
        norms.Add(n);  norms.Add(n);  norms.Add(n);
        AddTri(tris, verts, norms, v, v + 1, v + 2);
    }

    // One triangle, wound so its face points the way its vertices' normals do — the same test
    // LollipopTower uses, so the corner order above only has to go round.
    static void AddTri(List<int> tris, List<Vector3> verts, List<Vector3> norms, int a, int b, int c)
    {
        Vector3 face = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        if (face.sqrMagnitude < 1e-14f) return;
        Vector3 n = norms[a] + norms[b] + norms[c];
        if (Vector3.Dot(face, n) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
        else                            { tris.Add(a); tris.Add(c); tris.Add(b); }
    }
}
