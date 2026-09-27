using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on a generated wall and records what the Level Select Designer needs to find it again:
/// which wall it was built for, and the name of the mesh asset it writes to. The same job
/// <see cref="PipeMesh"/> does for a pipe.
///
/// Also builds the wall: a plain slab swept along its nodes as one mesh — never tiles. Seen from
/// above, each stretch between two nodes is either curved (a Catmull-Rom through the nodes either
/// side, the same curve the Grid Designer's spline walls use) or straight, or left out as a gap.
/// The top follows each node's height; the sides run down to the one Drop, with no underside.
/// </summary>
public class WallMesh : MonoBehaviour
{
    [Tooltip("DesignerWall this wall was built for.")]
    public string wallId;

    [Tooltip("Name of the generated mesh asset this wall writes to.")]
    public string meshAssetName;

    /// <summary>How many pieces each curved stretch is cut into.</summary>
    public const int CurveSteps = 16;

    /// <summary>One node as the builder takes it: where it stands, the wall top's height there, and the stretch on to the next.</summary>
    public struct Node
    {
        public Vector3 top;        // x, z on the map; y = the wall's top here
        public bool    curvedToNext;
        public bool    gapToNext;

        public bool  archway;      // an archway cut through the wall here, centred on the node
        public float archWidth;    // across the opening, measured along the wall
        public float archHeight;   // from the wall's base up to the top of the round

        public bool  column;       // a round column standing at the node, base to past the top
        public float columnRadius;
        public float columnHeight; // how far its top stands above the wall's top here

        /// <summary>Where anything standing on this node stands: the column's top, else the wall's.</summary>
        public Vector3 StandOn => column ? top + Vector3.up * Mathf.Max(0f, columnHeight) : top;
    }

    /// <summary>How many sides a node's column is built with.</summary>
    public const int ColumnSides = 32;

    /// <summary>How many pieces an archway is cut into across its width.</summary>
    public const int ArchSteps = 24;

    /// <summary>
    /// The wall's centre line along its top, as unbroken pieces — a gap ends one and starts the
    /// next. A closed wall with no gap comes back as one piece with <c>loop</c> set, its last
    /// point not repeating its first.
    /// </summary>
    public static List<(List<Vector3> points, bool loop)> CentreLines(IList<Node> nodes, bool closed)
    {
        var pieces = new List<(List<Vector3>, bool)>();
        foreach (var (points, _, loop) in CentreLinesWithNodes(nodes, closed)) pieces.Add((points, loop));
        return pieces;
    }

    /// <summary>
    /// <see cref="CentreLines"/>, each point also carrying the node it stands on (-1 for a point
    /// on the curve between two nodes) — so an archway can find where its node falls on a piece.
    /// </summary>
    static List<(List<Vector3> points, List<int> nodeAt, bool loop)> CentreLinesWithNodes(IList<Node> nodes, bool closed)
    {
        var pieces = new List<(List<Vector3>, List<int>, bool)>();
        int n = nodes?.Count ?? 0;
        if (n < 2) return pieces;

        int stretches = closed ? n : n - 1;

        // A closed wall with a gap starts just after its first gap, so no piece is cut in two
        // where the loop wraps round.
        int start = 0;
        bool anyGap = false;
        for (int s = 0; s < stretches; s++)
        {
            if (!nodes[s].gapToNext) continue;
            anyGap = true;
            start  = closed ? (s + 1) % n : 0;
            break;
        }

        List<Vector3> piece = null;
        List<int>     marks = null;
        for (int k = 0; k < stretches; k++)
        {
            int s = (start + k) % n;
            if (nodes[s].gapToNext) { piece = null; continue; }

            if (piece == null)
            {
                piece = new List<Vector3>();
                marks = new List<int>();
                pieces.Add((piece, marks, false));
            }

            int a = s, b = (s + 1) % n;
            if (nodes[s].curvedToNext)
            {
                int before = closed ? (a - 1 + n) % n : Mathf.Max(a - 1, 0);
                int after  = closed ? (b + 1) % n     : Mathf.Min(b + 1, n - 1);
                for (int step = 0; step <= CurveSteps; step++)
                {
                    float   t  = step / (float)CurveSteps;
                    Vector3 xz = CatmullRom(nodes[before].top, nodes[a].top, nodes[b].top, nodes[after].top, t);
                    xz.y = Mathf.Lerp(nodes[a].top.y, nodes[b].top.y, t);
                    AddPoint(piece, marks, xz, step == 0 ? a : step == CurveSteps ? b : -1);
                }
            }
            else
            {
                AddPoint(piece, marks, nodes[a].top, a);
                AddPoint(piece, marks, nodes[b].top, b);
            }
        }

        // Closed all the way round: one loop, the point back at the start dropped.
        if (closed && !anyGap && pieces.Count == 1)
        {
            var (loop, loopMarks, _) = pieces[0];
            if (loop.Count > 2 && Flat(loop[loop.Count - 1] - loop[0]).sqrMagnitude < 1e-10f)
            {
                loop.RemoveAt(loop.Count - 1);
                loopMarks.RemoveAt(loopMarks.Count - 1);
            }
            pieces[0] = (loop, loopMarks, true);
        }

        return pieces;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                       (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    static void AddPoint(List<Vector3> line, List<int> marks, Vector3 p, int node)
    {
        if (line.Count == 0 || Flat(line[line.Count - 1] - p).sqrMagnitude > 1e-10f)
        {
            line.Add(p);
            marks.Add(node);
        }
        else if (node >= 0)
        {
            marks[marks.Count - 1] = node;
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>
    /// Builds a wall in its own frame, where y = 0 is the rim top the rivers are built from.
    /// <paramref name="bottomY"/> is how far down its sides go; <paramref name="thickness"/> is
    /// across. Tops take the rim colour, sides the outer.
    ///
    /// A node with an archway has an opening cut clean through the wall, centred on it: straight
    /// sides up from the base, a round top. An archway reaching past the top cuts the wall open.
    ///
    /// A node with a column has a round column standing on it, from the drop up to its own
    /// height above the wall's top — sides outer, cap rim.
    ///
    /// Comes out carrying the river run stone shading — see <see cref="RiverMeshBuilder.ShadeAsStone"/>.
    /// </summary>
    public static Mesh Build(IList<Node> nodes, bool closed, float thickness, float bottomY)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris  = new List<int>();
        var kinds = new List<float>();
        var parts = new List<int>();

        float half = Mathf.Max(0.0005f, thickness * 0.5f);

        foreach (var (points, nodeAt, loop) in CentreLinesWithNodes(nodes, closed))
        {
            var line = Archways(points, nodeAt, loop, nodes, bottomY, out var openIn, out var openOut);
            AppendSlab(verts, norms, tris, kinds, parts, line, openIn, openOut, loop, half, bottomY);
        }

        for (int i = 0; i < (nodes?.Count ?? 0); i++)
            if (nodes[i].column)
                AppendColumn(verts, norms, tris, kinds, parts, nodes[i], bottomY);

        var mesh = new Mesh { name = "Wall" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // River run stone shading, the waterline placed off the rim top at y = 0.
        return RiverMeshBuilder.ShadeAsStone(mesh, 0f, null, kinds, parts);
    }

    struct Arch
    {
        public float at;       // how far along the piece its node is
        public float halfW;    // half the width, along the wall
        public float round;    // how tall the round top is
        public float height;   // base to the top of the round
    }

    /// <summary>
    /// The piece's centre line with extra points wherever an archway needs them — at its two
    /// sides and across its round top — and how high the opening reaches at each point: once
    /// looking back along the line (<paramref name="openIn"/>) and once looking on
    /// (<paramref name="openOut"/>). The two differ only at an archway's straight side. Where
    /// there is no opening both are <paramref name="bottomY"/>; neither goes above the wall's top.
    /// </summary>
    static List<Vector3> Archways(List<Vector3> line, List<int> nodeAt, bool loop, IList<Node> nodes,
                                  float bottomY, out float[] openIn, out float[] openOut)
    {
        int count = line.Count;

        // How far along the piece each point is — a loop's last entry is its whole length.
        var along = new float[count + 1];
        int legs  = loop ? count : count - 1;
        for (int i = 0; i < legs; i++)
            along[i + 1] = along[i] + Flat(line[(i + 1) % count] - line[i]).magnitude;
        float length = along[legs];

        var arches = new List<Arch>();
        for (int i = 0; i < count; i++)
        {
            int k = nodeAt[i];
            if (k < 0 || !nodes[k].archway) continue;
            float halfW  = nodes[k].archWidth * 0.5f;
            float height = nodes[k].archHeight;
            if (halfW <= 1e-4f || height <= 0f) continue;
            arches.Add(new Arch { at = along[i], halfW = halfW, round = Mathf.Min(halfW, height), height = height });
        }

        if (arches.Count == 0 || length <= 0f)
        {
            openIn  = new float[count];
            openOut = new float[count];
            for (int i = 0; i < count; i++) openIn[i] = openOut[i] = bottomY;
            return line;
        }

        // Where the extra points go: the two sides of every archway, and across its round.
        var extra = new List<float>();
        foreach (var arch in arches)
        {
            for (int step = 0; step <= ArchSteps; step++)
            {
                float s = arch.at + Mathf.Lerp(-arch.halfW, arch.halfW, step / (float)ArchSteps);
                if (loop) s = Mathf.Repeat(s, length);
                else if (s < 0f || s > length) continue;
                extra.Add(s);
            }
        }
        extra.Sort();

        // The original points and the extra ones, merged in order along the piece.
        var outLine  = new List<Vector3>(count + extra.Count);
        var outAlong = new List<float>(count + extra.Count);

        void Add(Vector3 p, float s)
        {
            if (outAlong.Count > 0 && s - outAlong[outAlong.Count - 1] < 1e-5f) return;
            outLine.Add(p);
            outAlong.Add(s);
        }

        int e = 0;
        for (int i = 0; i < count; i++)
        {
            Add(line[i], along[i]);
            if (i >= legs) break;
            while (e < extra.Count && extra[e] <= along[i]) e++;
            for (; e < extra.Count && extra[e] < along[i + 1]; e++)
            {
                float t = (extra[e] - along[i]) / Mathf.Max(along[i + 1] - along[i], 1e-9f);
                Add(Vector3.Lerp(line[i], line[(i + 1) % count], t), extra[e]);
            }
        }

        // A loop's last point must not sit on top of its first.
        if (loop && outLine.Count > 2 && length - outAlong[outAlong.Count - 1] < 1e-5f)
        {
            outLine.RemoveAt(outLine.Count - 1);
            outAlong.RemoveAt(outAlong.Count - 1);
        }

        int outCount = outLine.Count;
        openIn  = new float[outCount];
        openOut = new float[outCount];
        for (int i = 0; i < outCount; i++)
        {
            openIn[i]  = Opening(outAlong[i], -1, arches, length, loop, bottomY, outLine[i].y);
            openOut[i] = Opening(outAlong[i], +1, arches, length, loop, bottomY, outLine[i].y);
        }
        return outLine;
    }

    /// <summary>
    /// How high the opening reaches at a point <paramref name="s"/> along the piece. Right on an
    /// archway's side, <paramref name="side"/> says which way is looked at — back along the line
    /// (-1) or on (+1) — so the wall beside the arch and the arch itself each get their own height.
    /// </summary>
    static float Opening(float s, int side, List<Arch> arches, float length, bool loop,
                         float bottomY, float topY)
    {
        const float tol = 1e-5f;
        float open = bottomY;
        foreach (var arch in arches)
        {
            float d = s - arch.at;
            if (loop) d = Mathf.Repeat(d + length * 0.5f, length) - length * 0.5f;

            bool inside = side > 0 ? d >= -arch.halfW - tol && d < arch.halfW - tol
                                   : d > -arch.halfW + tol && d <= arch.halfW + tol;
            if (!inside) continue;

            float u = Mathf.Clamp(d / arch.halfW, -1f, 1f);
            float y = bottomY + arch.height - arch.round + arch.round * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
            open = Mathf.Max(open, y);
        }
        return Mathf.Min(open, topY);
    }

    // ── One unbroken piece: a side each way, the top, and a flat end at each open end ──
    static void AppendSlab(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                           List<float> kinds, List<int> parts,
                           List<Vector3> line, float[] openIn, float[] openOut,
                           bool loop, float half, float bottomY)
    {
        int count = line.Count;
        if (count < 2) return;

        // Out to the left of the line at each point, mitred where two stretches meet so the
        // slab keeps its thickness round a corner. A very sharp corner is held back from
        // shooting its mitre off to nothing.
        var offsets = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            bool hasBack = loop || i > 0;
            bool hasFwd  = loop || i < count - 1;
            Vector3 back = hasBack ? Flat(line[i] - line[(i - 1 + count) % count]).normalized : Vector3.zero;
            Vector3 fwd  = hasFwd  ? Flat(line[(i + 1) % count] - line[i]).normalized         : Vector3.zero;

            Vector3 nBack = Left(back), nFwd = Left(fwd);
            Vector3 n     = nBack + nFwd;
            if (n.sqrMagnitude < 1e-8f) n = hasFwd ? nFwd : nBack;
            n.Normalize();

            Vector3 side = hasFwd ? nFwd : nBack;
            float   cos  = Mathf.Max(0.25f, Vector3.Dot(n, side));
            offsets[i]   = n * (half / cos);
        }

        int legs = loop ? count : count - 1;
        for (int i = 0; i < legs; i++)
        {
            int     j   = (i + 1) % count;
            Vector3 a   = line[i], b = line[j];
            Vector3 dir = Flat(b - a).normalized;
            Vector3 nL  = Left(dir);

            Vector3 aL = a + offsets[i], bL = b + offsets[j];
            Vector3 aR = a - offsets[i], bR = b - offsets[j];

            // How high an archway opens under each end — the drop where there is none.
            float oa = openOut[i], ob = openIn[j];

            // Left side, right side — down to the drop, or to an archway's opening.
            Quad(verts, norms, tris, Down(aL, oa), Down(bL, ob), bL, aL, nL);
            Quad(verts, norms, tris, Down(aR, oa), Down(bR, ob), bR, aR, -nL);

            // Under an archway's round, across. An archway past the top leaves nothing here.
            bool cutOpen = oa >= a.y - 1e-6f && ob >= b.y - 1e-6f;
            if ((oa > bottomY + 1e-6f || ob > bottomY + 1e-6f) && !cutOpen)
            {
                Vector3 under = Vector3.Cross(Down(b, ob) - Down(a, oa), nL).normalized;
                Quad(verts, norms, tris, Down(aL, oa), Down(bL, ob), Down(bR, ob), Down(aR, oa), under);
            }

            // An archway's straight side, across, where the opening starts or ends at this point.
            if (loop || i > 0) Jamb(verts, norms, tris, line, offsets, openIn, openOut, i, count);

            StoneFaces.Tag(kinds, tris, StoneFaceKind.Outer);
            StoneFaces.Part(parts, tris, 0);

            // The top, across.
            if (!cutOpen) Quad(verts, norms, tris, aL, bL, bR, aR, Vector3.up);
            StoneFaces.Tag(kinds, tris, StoneFaceKind.Rim);
            StoneFaces.Part(parts, tris, 1);
        }

        if (loop) return;

        // Flat ends.
        Vector3 startDir = Flat(line[1] - line[0]).normalized;
        Vector3 endDir   = Flat(line[count - 1] - line[count - 2]).normalized;
        Vector3 s = line[0], e = line[count - 1];

        // Each from the drop — or from an archway's opening, if one reaches the end.
        float so = openOut[0], eo = openIn[count - 1];
        Quad(verts, norms, tris, Down(s - offsets[0], so), Down(s + offsets[0], so),
             s + offsets[0], s - offsets[0], -startDir);
        Quad(verts, norms, tris, Down(e - offsets[count - 1], eo), Down(e + offsets[count - 1], eo),
             e + offsets[count - 1], e - offsets[count - 1], endDir);
        StoneFaces.Tag(kinds, tris, StoneFaceKind.Outer);
        StoneFaces.Part(parts, tris, 0);
    }

    // The face across the wall at point i, between the opening on one side of it and the
    // opening on the other — facing into the archway.
    static void Jamb(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                     List<Vector3> line, Vector3[] offsets, float[] openIn, float[] openOut, int i, int count)
    {
        float lo = Mathf.Min(openIn[i], openOut[i]), hi = Mathf.Max(openIn[i], openOut[i]);
        if (hi - lo < 1e-6f) return;

        Vector3 p      = line[i];
        Vector3 facing = openOut[i] > openIn[i]
            ? Flat(line[(i + 1) % count] - p).normalized
            : Flat(line[(i - 1 + count) % count] - p).normalized;
        Quad(verts, norms, tris, Down(p + offsets[i], lo), Down(p - offsets[i], lo),
             Down(p - offsets[i], hi), Down(p + offsets[i], hi), facing);
    }

    // ── A node's column: smooth round sides from the drop up to its top, and a flat cap ──
    static void AppendColumn(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                             List<float> kinds, List<int> parts, Node node, float bottomY)
    {
        float   r      = Mathf.Max(0.001f, node.columnRadius);
        Vector3 centre = node.StandOn;

        int start = verts.Count;
        for (int i = 0; i <= ColumnSides; i++)
        {
            float   a = i * Mathf.PI * 2f / ColumnSides;
            Vector3 n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts.Add(Down(centre + n * r, bottomY)); norms.Add(n);
            verts.Add(centre + n * r);                norms.Add(n);
        }
        for (int i = 0; i < ColumnSides; i++)
        {
            int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            Vector3 n = norms[b0] + norms[b1];
            AddTri(tris, verts, n, b0, t0, t1);
            AddTri(tris, verts, n, b0, t1, b1);
        }
        StoneFaces.Tag(kinds, tris, StoneFaceKind.Outer);
        StoneFaces.Part(parts, tris, 2);

        int mid = verts.Count;
        verts.Add(centre); norms.Add(Vector3.up);
        for (int i = 0; i <= ColumnSides; i++)
        {
            float a = i * Mathf.PI * 2f / ColumnSides;
            verts.Add(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r); norms.Add(Vector3.up);
        }
        for (int i = 0; i < ColumnSides; i++)
            AddTri(tris, verts, Vector3.up, mid, mid + 1 + i, mid + 2 + i);
        StoneFaces.Tag(kinds, tris, StoneFaceKind.Rim);
        StoneFaces.Part(parts, tris, 3);
    }

    static Vector3 Left(Vector3 dir) => new Vector3(-dir.z, 0f, dir.x);

    static Vector3 Down(Vector3 p, float bottomY) => new Vector3(p.x, bottomY, p.z);

    static void Quad(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                     Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n)
    {
        int v = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
        norms.Add(n);  norms.Add(n);  norms.Add(n);  norms.Add(n);
        AddTri(tris, verts, n, v, v + 1, v + 2);
        AddTri(tris, verts, n, v, v + 2, v + 3);
    }

    // One triangle, wound so its face points the way n does — the same test PipeMesh uses, so
    // the corner order above only has to go round.
    static void AddTri(List<int> tris, List<Vector3> verts, Vector3 n, int a, int b, int c)
    {
        Vector3 face = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        if (face.sqrMagnitude < 1e-14f) return;
        if (Vector3.Dot(face, n) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
        else                            { tris.Add(a); tris.Add(c); tris.Add(b); }
    }
}
