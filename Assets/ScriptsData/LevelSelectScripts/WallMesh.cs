using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spot on one of a wall's seam lines that the Seam Clean Up Tool has removed, said in terms of
/// the wall rather than its mesh so it is found again after every rebuild: the node it is nearest,
/// how far along the wall from that node, how far across from the centre line, and how high —
/// measured down from the wall's top where it is nearer the top, up from the base otherwise, so a
/// line along the top stays found when the wall's height changes. <see cref="direction"/> is which
/// way the seam runs there, in the same terms: x along the wall, y up, z across.
///
/// A face group's spots are the same, on a face rather than a seam: <see cref="direction"/> is
/// then the way the face looks.
/// </summary>
[System.Serializable]
public class WallSeamSpot
{
    public int     node;
    public float   along;
    public float   across;
    public float   height;
    public bool    fromTop;
    public Vector3 direction;

    // The height measured the OTHER way — up from the base for a spot taken from the top, and the
    // other way round — looked at when the first finds nothing. Neither way is right for every
    // seam: a line along the top moves with the wall's height, while an opening's lines stay put
    // above the base, and an opening near the top was taken from the top. Spots made before this
    // carry only the one height.
    public bool    measuredBoth;
    public float   otherHeight;
}

/// <summary>
/// Surfaces of a wall treated as ONE surface by the stone shading — the Seam Clean Up Tool's face
/// groups. Each spot is a face somewhere on the wall; the whole surface it is on joins the group.
/// Every surface in the group takes the group's width, so its seam fades come out even across
/// all of them, whichever chunk each is in.
/// </summary>
[System.Serializable]
public class WallFaceGroup
{
    public List<WallSeamSpot> surfaces = new List<WallSeamSpot>();
}

/// <summary>
/// Sits on a generated wall and records what the Level Select Designer needs to find it again:
/// which wall it was built for, and the name of the mesh asset it writes to. The same job
/// <see cref="PipeMesh"/> does for a pipe.
///
/// Also builds the wall: a plain slab swept along its nodes as one mesh — never tiles. Seen from
/// above, each stretch between two nodes is either curved (a Catmull-Rom through the nodes either
/// side, the same curve the Grid Designer's spline walls use) or straight, or left out as a gap.
/// The top follows each node's height; the sides run down to the one Drop, with no underside.
/// The wall comes out in chunks, one per node, each its own mesh.
/// </summary>
public class WallMesh : MonoBehaviour
{
    [Tooltip("DesignerWall this wall was built for.")]
    public string wallId;

    [Tooltip("Name of the generated mesh asset this wall writes to.")]
    public string meshAssetName;

    /// <summary>How many pieces each curved stretch is cut into.</summary>
    public const int CurveSteps = 12;

    /// <summary>One node as the builder takes it: where it stands, the wall top's height there, and the stretch on to the next.</summary>
    public struct Node
    {
        public Vector3 top;        // x, z on the map; y = the wall's top here
        public bool    curvedToNext;
        public bool    gapToNext;

        public bool  archway;      // an archway cut through the wall here, centred on the node
        public float archWidth;    // across the opening, measured along the wall
        public float archHeight;   // from the wall's base up to the top of the round

        public bool  porthole;       // a round hole bored straight across the wall here, centred on the node
        public float portholeRadius;
        public float portholeHeight; // from the wall's base up to the hole's centre

        public bool  column;      // a round column standing at the node, base to past the top
        public float columnRadius;
        public float columnHeight; // how far its top stands above the lip's top here — the wall's, with no lip

        public float lipHeight;   // the wall's lip, standing on its top — 0 with no lip

        /// <summary>Where anything standing on this node stands: the column's top, else the lip's, else the wall's.</summary>
        public Vector3 StandOn => top + Vector3.up * (Mathf.Max(0f, lipHeight) + (column ? Mathf.Max(0f, columnHeight) : 0f));
    }

    /// <summary>How many sides a node's column is built with.</summary>
    public const int ColumnSides = 32;

    /// <summary>How many pieces an archway is cut into across its width.</summary>
    public const int ArchSteps = 12;

    /// <summary>How many pieces a porthole's circle is cut into along the wall, each half round.</summary>
    public const int PortholeSteps = 16;

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
    /// A node with a porthole has a round hole bored straight across the wall, centred on it, its
    /// centre measured up from the base. Wall stands under it and over it; a porthole reaching
    /// past the base or the top opens the wall there.
    ///
    /// A node with a column has a round column standing on it, from the drop up to its own
    /// height above the wall's top — sides outer, cap rim.
    ///
    /// Given a <paramref name="lip"/>, a square band stands on the wall's top all the way along,
    /// centred, its own thickness across: wider than the wall, it overhangs both sides with an
    /// underside; narrower, the wall's top shows either side of it. It goes round every column as
    /// a ring its Column Ring out from the column's side (none at 0), the lip's own top and underside cut
    /// back where the ring stands. Each node's <see cref="Node.lipHeight"/> should be the lip's
    /// height, so columns and toppers stand on it.
    ///
    /// Comes out in chunks, one mesh each: a chunk holds one node, and ends halfway to the next —
    /// or, where an archway or porthole reaches past halfway, halfway along the wall left standing
    /// between the two, so no opening is cut in two. Two openings running into each other share a
    /// chunk. A chunk holding an archway or porthole has its sides lined across, its whole length,
    /// at the heights of the opening's corners.
    ///
    /// Comes out carrying the river run stone shading — see <see cref="RiverMeshBuilder.ShadeAsStone"/>.
    /// Each part takes the stone type <paramref name="shading"/>'s Walls section gives it; Auto, or
    /// no shading, keeps tops rim and sides outer.
    /// </summary>
    /// <param name="removedSeams">Spots on seam lines to leave undrawn — the Seam Clean Up Tool's.
    /// Each removes the line it lies on, in every chunk that has one there.</param>
    /// <param name="report">When given, filled with every seam line and surface each chunk came
    /// out with, and which spots were found.</param>
    /// <param name="faceGroups">Surfaces treated as one — the Seam Clean Up Tool's face groups.
    /// Every surface in a group takes the group's width, so its seam fades come out even across
    /// all of them, whichever chunk each is in.</param>
    public static List<Mesh> BuildChunks(IList<Node> nodes, bool closed, float thickness, float bottomY,
                                         RiverRunShadingSettings shading = null,
                                         IList<WallSeamSpot> removedSeams = null, SeamReport report = null,
                                         IList<WallFaceGroup> faceGroups = null, WallLip lip = null)
    {
        var   chunks    = new List<Chunk>();
        int   nodeCount = nodes?.Count ?? 0;
        var   nodeChunk = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++) nodeChunk[i] = -1;

        float half = Mathf.Max(0.0005f, thickness * 0.5f);

        StoneFaceKind Choice(System.Func<RiverRunShadingSettings, StoneFaceKind> pick, StoneFaceKind auto)
        {
            var kind = shading != null ? pick(shading) : StoneFaceKind.Auto;
            return kind == StoneFaceKind.Auto ? auto : kind;
        }

        var stone = new PartKinds
        {
            sides       = Choice(s => s.wallSides,       StoneFaceKind.A),
            top         = Choice(s => s.wallTop,         StoneFaceKind.B),
            ends        = Choice(s => s.wallEnds,        StoneFaceKind.A),
            openings    = Choice(s => s.wallOpenings,    StoneFaceKind.A),
            columnSides = Choice(s => s.wallColumnSides, StoneFaceKind.A),
            columnTop   = Choice(s => s.wallColumnTop,   StoneFaceKind.B),
        };
        stone.lipTop       = Choice(s => s.wallLipTop,       stone.top);
        stone.lipSides     = Choice(s => s.wallLipSides,     stone.sides);
        stone.lipUnderside = Choice(s => s.wallLipUnderside, stone.sides);

        float lipH    = lip != null ? Mathf.Max(0f, lip.height) : 0f;
        float lipHalf = lip != null ? Mathf.Max(0.0005f, lip.thickness * 0.5f) : half;

        // With a lip, it goes round every column as a ring standing its Column Ring out from it.
        float ringOut = lip != null ? Mathf.Max(0f, lip.columnRing) : 0f;
        bool  hasRing = lipH > Eps && ringOut > Eps;
        var rings = new List<LipRing>();
        if (hasRing)
            for (int i = 0; i < nodeCount; i++)
                if (nodes[i].column) rings.Add(RingOf(nodes[i], lipH, ringOut));

        foreach (var (points, nodeAt, loop) in CentreLinesWithNodes(nodes, closed))
        {
            var line = Cuts(points, nodeAt, loop, nodes, bottomY, out var cutIn, out var cutOut, out var closeAt,
                            out var along, out var bounds, out var marks);
            if (line.Count < 2) continue;

            // The chunks along this piece, and which one each leg of it falls in. A loop's last
            // chunk runs on round past its start to the first boundary.
            int first  = chunks.Count;
            int count  = loop ? Mathf.Max(1, bounds.Count) : bounds.Count + 1;
            for (int k = 0; k < count; k++) chunks.Add(new Chunk());

            int ChunkOf(float s)
            {
                int passed = 0;
                foreach (float b in bounds) if (b <= s + 2e-5f) passed++;
                if (loop) return first + (passed == 0 ? count - 1 : passed - 1);
                return first + passed;
            }

            int legs     = loop ? line.Count : line.Count - 1;
            var legChunk = new Chunk[legs];
            for (int i = 0; i < legs; i++) legChunk[i] = chunks[ChunkOf(along[i])];

            // Each node in the chunk it stands in, its opening's lines run along the whole chunk.
            foreach (var (node, at) in marks)
            {
                int k = ChunkOf(at);
                nodeChunk[node] = k;
                Rows(nodes[node], bottomY, chunks[k].rows);
            }

            AppendSlab(legChunk, stone, line, cutIn, cutOut, closeAt, loop, half, bottomY, lipH, lipHalf, rings);
        }

        for (int i = 0; i < nodeCount; i++)
        {
            if (!nodes[i].column) continue;
            if (nodeChunk[i] < 0) { nodeChunk[i] = chunks.Count; chunks.Add(new Chunk()); }   // a node standing alone between two gaps
            AppendColumn(chunks[nodeChunk[i]], stone, nodes[i], bottomY);
            if (hasRing) AppendLipRing(chunks[nodeChunk[i]], stone, nodes[i], lipH, ringOut);
        }

        // Where each removed spot is on the wall as it stands now, and which way its seam runs.
        int spotCount = removedSeams?.Count ?? 0;
        var spotAt    = new (bool ok, Vector3[] points, Vector3 dir)[spotCount];
        for (int i = 0; i < spotCount; i++)
            spotAt[i] = removedSeams[i] != null && Resolve(nodes, closed, bottomY, removedSeams[i], out Vector3[] p, out Vector3 d)
                      ? (true, p, d) : (false, null, Vector3.zero);
        if (report != null) report.found = new bool[spotCount];

        var meshes        = new List<Mesh>(chunks.Count);
        var chunkSurfaces = new List<RiverMeshBuilder.StoneSurfaces>(chunks.Count);
        foreach (var chunk in chunks)
        {
            if (chunk.tris.Count == 0) continue;

            var mesh = new Mesh { name = "Wall" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(chunk.verts);
            mesh.SetNormals(chunk.norms);
            mesh.SetTriangles(chunk.tris, 0);
            mesh.RecalculateBounds();

            // Which spots each line of this chunk was removed by, in the order the lines come.
            var removedBy = new List<List<int>>();
            RiverMeshBuilder.StoneSeamLines lines = null;
            if (spotCount > 0 || report != null)
            {
                lines = new RiverMeshBuilder.StoneSeamLines
                {
                    Remove = all =>
                    {
                        var off = new bool[all.Count];
                        for (int j = 0; j < all.Count; j++) removedBy.Add(new List<int>());
                        for (int i = 0; i < spotCount; i++)
                        {
                            if (!spotAt[i].ok) continue;
                            int line = -1;
                            foreach (Vector3 p in spotAt[i].points)
                                if ((line = NearestLine(all, p, spotAt[i].dir)) >= 0) break;
                            if (line < 0) continue;
                            off[line] = true;
                            removedBy[line].Add(i);
                            if (report != null) report.found[i] = true;
                        }
                        return off;
                    },
                };
            }

            // River run stone shading, the waterline placed off the rim top at y = 0.
            int index    = meshes.Count;
            var surfaces = new RiverMeshBuilder.StoneSurfaces();
            meshes.Add(RiverMeshBuilder.ShadeAsStone(mesh, 0f, null, chunk.kinds, chunk.parts, lines, surfaces));
            chunkSurfaces.Add(surfaces);

            if (report != null && lines != null)
                for (int j = 0; j < lines.Lines.Count; j++)
                    report.lines.Add(new SeamLine
                    {
                        chunk     = index,
                        points    = lines.Lines[j],
                        removedBy = j < removedBy.Count ? removedBy[j] : new List<int>(),
                    });
        }

        GroupSurfaces(nodes, closed, bottomY, meshes, chunkSurfaces, faceGroups, report);
        return meshes;
    }

    /// <summary>
    /// Gives every surface in a face group the group's width — the widest of them, which is the
    /// width the group has as one surface, since a surface's width is twice the deepest any part
    /// of it lies from a seam — written over UV2.y, so the seam fades come out even across the
    /// lot. Also fills the report's surfaces, for the tool to draw and pick from.
    /// </summary>
    static void GroupSurfaces(IList<Node> nodes, bool closed, float bottomY, List<Mesh> meshes,
                              List<RiverMeshBuilder.StoneSurfaces> chunkSurfaces,
                              IList<WallFaceGroup> faceGroups, SeamReport report)
    {
        int groupCount = faceGroups?.Count ?? 0;
        if (report == null && groupCount == 0) return;

        var tris  = new int[meshes.Count][];
        var verts = new Vector3[meshes.Count][];
        for (int k = 0; k < meshes.Count; k++)
        {
            tris[k]  = meshes[k].triangles;
            verts[k] = meshes[k].vertices;
        }

        // Which group each surface of each chunk is in, and by which of the group's spots.
        var grouped = new Dictionary<(int chunk, int surface), (int group, int spot)>();
        if (report != null) report.groupFound = new bool[groupCount][];

        for (int g = 0; g < groupCount; g++)
        {
            var spots = faceGroups[g]?.surfaces;
            int count = spots?.Count ?? 0;
            if (report != null) report.groupFound[g] = new bool[count];

            for (int i = 0; i < count; i++)
            {
                if (spots[i] == null || !Resolve(nodes, closed, bottomY, spots[i], out Vector3[] at, out Vector3 n)) continue;
                int chunk = -1, face = -1;
                foreach (Vector3 p in at)
                    if (NearestFace(tris, verts, chunkSurfaces, p, n, out chunk, out face)) break;
                if (face < 0) continue;

                var key = (chunk, chunkSurfaces[chunk].Surface[face]);
                if (report != null) report.groupFound[g][i] = true;
                if (!grouped.ContainsKey(key)) grouped.Add(key, (g, i));   // the first group to claim it keeps it
            }
        }

        // Each group's width: the widest surface in it.
        var width = new float[groupCount];
        foreach (var pair in grouped)
        {
            var info = chunkSurfaces[pair.Key.chunk];
            int face = System.Array.IndexOf(info.Surface, pair.Key.surface);
            if (face >= 0) width[pair.Value.group] = Mathf.Max(width[pair.Value.group], info.Width[face]);
        }

        // Written over UV2.y of every face in a group.
        if (grouped.Count > 0)
        {
            var uv2 = new List<Vector4>();
            for (int k = 0; k < meshes.Count; k++)
            {
                var info = chunkSurfaces[k];
                if (info.Surface == null) continue;

                bool changed = false;
                meshes[k].GetUVs(2, uv2);
                if (uv2.Count == 0) continue;
                for (int f = 0; f < info.Surface.Length; f++)
                {
                    if (!grouped.TryGetValue((k, info.Surface[f]), out var by)) continue;
                    for (int e = 0; e < 3; e++)
                    {
                        int     v = tris[k][f * 3 + e];
                        Vector4 d = uv2[v];
                        d.y    = width[by.group];
                        uv2[v] = d;
                    }
                    changed = true;
                }
                if (changed) meshes[k].SetUVs(2, uv2);
            }

            GroupGradients(meshes, tris, verts, chunkSurfaces, grouped, groupCount);
        }

        if (report == null) return;
        report.groupWidth = width;

        // Every surface of every chunk, its faces' corners in the wall's own frame.
        for (int k = 0; k < meshes.Count; k++)
        {
            var info = chunkSurfaces[k];
            if (info.Surface == null) continue;

            var bySurface = new Dictionary<int, SurfacePatch>();
            for (int f = 0; f < info.Surface.Length; f++)
            {
                int s = info.Surface[f];
                if (!bySurface.TryGetValue(s, out var patch))
                {
                    bool inGroup = grouped.TryGetValue((k, s), out var by);
                    patch = new SurfacePatch
                    {
                        chunk   = k,
                        width   = info.Width[f],
                        corners = new List<Vector3>(),
                        group   = inGroup ? by.group : -1,
                        spot    = inGroup ? by.spot  : -1,
                    };
                    bySurface.Add(s, patch);
                    report.surfaces.Add(patch);
                }
                for (int e = 0; e < 3; e++) patch.corners.Add(verts[k][tris[k][f * 3 + e]]);
            }
        }
    }

    /// <summary>
    /// Gives every face in a group where it stands within the GROUP's bounds — x left to right, y
    /// bottom to top, the same two ways the stone gradients run — on UV3, so a gradient runs once
    /// across the whole group rather than across each chunk on its own. Faces in no group are left
    /// zero, and the shader goes on spreading their gradients over their own chunk.
    /// </summary>
    static void GroupGradients(List<Mesh> meshes, int[][] tris, Vector3[][] verts,
                               List<RiverMeshBuilder.StoneSurfaces> chunkSurfaces,
                               Dictionary<(int chunk, int surface), (int group, int spot)> grouped, int groupCount)
    {
        var lo = new Vector2[groupCount];
        var hi = new Vector2[groupCount];
        for (int g = 0; g < groupCount; g++)
        {
            lo[g] = new Vector2(float.MaxValue, float.MaxValue);
            hi[g] = new Vector2(float.MinValue, float.MinValue);
        }

        for (int k = 0; k < meshes.Count; k++)
        {
            var info = chunkSurfaces[k];
            if (info.Surface == null) continue;
            for (int f = 0; f < info.Surface.Length; f++)
            {
                if (!grouped.TryGetValue((k, info.Surface[f]), out var by)) continue;
                for (int e = 0; e < 3; e++)
                {
                    Vector3 v = verts[k][tris[k][f * 3 + e]];
                    lo[by.group] = Vector2.Min(lo[by.group], new Vector2(v.x, v.y));
                    hi[by.group] = Vector2.Max(hi[by.group], new Vector2(v.x, v.y));
                }
            }
        }

        // A group with no extent one way sits in the middle of the gradient that way.
        static float Along(float v, float min, float max) => max - min > 1e-5f ? (v - min) / (max - min) : 0.5f;

        for (int k = 0; k < meshes.Count; k++)
        {
            var info = chunkSurfaces[k];
            if (info.Surface == null) continue;

            List<Vector4> uv3 = null;
            for (int f = 0; f < info.Surface.Length; f++)
            {
                if (!grouped.TryGetValue((k, info.Surface[f]), out var by)) continue;
                if (uv3 == null)
                {
                    uv3 = new List<Vector4>(verts[k].Length);
                    for (int i = 0; i < verts[k].Length; i++) uv3.Add(Vector4.zero);
                }

                int g = by.group;
                for (int e = 0; e < 3; e++)
                {
                    int     i = tris[k][f * 3 + e];
                    Vector3 v = verts[k][i];
                    uv3[i] = new Vector4(Along(v.x, lo[g].x, hi[g].x), Along(v.y, lo[g].y, hi[g].y),
                                         RiverMeshBuilder.GroupGradientMarkZ, RiverMeshBuilder.GroupGradientMarkW);
                }
            }
            if (uv3 != null) meshes[k].SetUVs(3, uv3);
        }
    }

    /// <summary>
    /// The face a spot lies on, across every chunk: the nearest one facing the spot's way, within
    /// a few millimetres.
    /// </summary>
    static bool NearestFace(int[][] tris, Vector3[][] verts, List<RiverMeshBuilder.StoneSurfaces> chunkSurfaces,
                            Vector3 p, Vector3 normal, out int chunk, out int face)
    {
        chunk = face = -1;
        float best = SpotReach;
        for (int k = 0; k < tris.Length; k++)
        {
            if (chunkSurfaces[k].Surface == null) continue;
            for (int f = 0; f * 3 + 2 < tris[k].Length; f++)
            {
                Vector3 a = verts[k][tris[k][f * 3]], b = verts[k][tris[k][f * 3 + 1]], c = verts[k][tris[k][f * 3 + 2]];
                Vector3 n = Vector3.Cross(b - a, c - b);
                if (n.sqrMagnitude < 1e-12f || Vector3.Dot(n.normalized, normal) < SpotFacesSameWay) continue;

                float d = Vector3.Distance(p, ClosestOnTriangle(p, a, b, c));
                if (d >= best) continue;
                best  = d;
                chunk = k;
                face  = f;
            }
        }
        return face >= 0;
    }

    /// <summary>How nearly a face has to look the spot's way to be its face (cosine).</summary>
    const float SpotFacesSameWay = 0.9f;

    /// <summary>The point of triangle abc nearest p.</summary>
    public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    // ─────────────────────────────────────────────
    // SEAM CLEAN UP
    // ─────────────────────────────────────────────
    //
    // A removed seam is remembered as a SPOT on it, said in terms of the wall rather than the
    // mesh — nearest node, how far along the wall from it, how far across, how high — because
    // the mesh is rebuilt from nothing every time, and a node moving drags its spots with it.
    // After a rebuild the spot removes whichever seam line runs through it the same way.

    /// <summary>
    /// Every seam line and surface a wall's chunks came out with, which removed spots and face
    /// group spots were found, and each face group's width.
    /// </summary>
    public class SeamReport
    {
        public readonly List<SeamLine>     lines    = new List<SeamLine>();
        public readonly List<SurfacePatch> surfaces = new List<SurfacePatch>();

        /// <summary>One entry per removed spot: whether it lay on a line in any chunk.</summary>
        public bool[] found = new bool[0];

        /// <summary>Per face group, per spot: whether it lay on a face.</summary>
        public bool[][] groupFound = new bool[0][];

        /// <summary>Per face group: the width every surface in it takes.</summary>
        public float[] groupWidth = new float[0];
    }

    /// <summary>One surface of one chunk: its faces' corners (three a face, wall frame) and the face group it is in.</summary>
    public class SurfacePatch
    {
        public int           chunk;
        public float         width;   // its own width, before any group's
        public List<Vector3> corners;
        public int           group;   // -1 in none
        public int           spot;    // which of the group's spots took it in
    }

    /// <summary>One seam line of one chunk, in the wall's own frame, and the removed spots lying on it.</summary>
    public struct SeamLine
    {
        public int       chunk;
        public Vector3[] points;
        public List<int> removedBy;   // empty while the line is drawn
    }

    /// <summary>How far a spot may lie off a seam line and still be on it — a few millimetres.</summary>
    const float SpotReach = 0.005f;

    /// <summary>How nearly a line has to run the spot's way there to be its seam (cosine).</summary>
    const float SpotRunsSameWay = 0.7f;

    /// <summary>The line nearest the spot that runs the same way through it, or -1 if none is within reach.</summary>
    static int NearestLine(IReadOnlyList<Vector3[]> lines, Vector3 point, Vector3 dir)
    {
        int   best     = -1;
        float bestDist = SpotReach;
        for (int j = 0; j < lines.Count; j++)
        {
            float d = DistanceToLine(lines[j], point, out Vector3 runs);
            if (d > bestDist || Mathf.Abs(Vector3.Dot(runs, dir)) < SpotRunsSameWay) continue;
            best     = j;
            bestDist = d;
        }
        return best;
    }

    /// <summary>How far <paramref name="p"/> lies from a line, and which way the line runs at its nearest point.</summary>
    public static float DistanceToLine(Vector3[] line, Vector3 p, out Vector3 runs)
    {
        float best = float.MaxValue;
        runs = Vector3.right;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector3 a = line[i], ab = line[i + 1] - a;
            float   len2 = ab.sqrMagnitude;
            if (len2 < 1e-12f) continue;
            float   t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            float   d = Vector3.Distance(a + ab * t, p);
            if (d < best) { best = d; runs = ab.normalized; }
        }
        return best;
    }

    /// <summary>One piece of the wall's centre line, measured: how far along each point is, and where each node falls.</summary>
    class Measured
    {
        public List<Vector3> points;
        public List<int>     nodeAt;
        public bool          loop;
        public float[]       along;   // per point; a loop's extra last entry is its whole length
        public float         length;

        public int Legs => loop ? points.Count : points.Count - 1;
    }

    static List<Measured> MeasuredLines(IList<Node> nodes, bool closed)
    {
        var list = new List<Measured>();
        foreach (var (points, nodeAt, loop) in CentreLinesWithNodes(nodes, closed))
        {
            if (points.Count < 2) continue;
            var m = new Measured { points = points, nodeAt = nodeAt, loop = loop, along = new float[points.Count + 1] };
            for (int i = 0; i < m.Legs; i++)
                m.along[i + 1] = m.along[i] + Flat(points[(i + 1) % points.Count] - points[i]).magnitude;
            m.length = m.along[m.Legs];
            list.Add(m);
        }
        return list;
    }

    /// <summary>
    /// The centre line at <paramref name="s"/> along a piece: the point (y = the wall's top there)
    /// and which way it runs. Past either end of an open piece it carries straight on, so a spot
    /// on an end cap or a column standing out past the end is placed the same way both ways round.
    /// </summary>
    static void At(Measured m, float s, out Vector3 centre, out Vector3 runs)
    {
        if (m.loop && m.length > Eps) s = Mathf.Repeat(s, m.length);

        int leg = 0;
        while (leg < m.Legs - 1 && s > m.along[leg + 1]) leg++;

        Vector3 a   = m.points[leg], b = m.points[(leg + 1) % m.points.Count];
        float   len = m.along[leg + 1] - m.along[leg];
        float   t   = len > Eps ? (s - m.along[leg]) / len : 0f;
        if (m.loop) t = Mathf.Clamp01(t);

        centre = a + (b - a) * t;
        runs   = Flat(b - a);
        runs   = runs.sqrMagnitude > Eps ? runs.normalized : Vector3.right;
    }

    /// <summary>Where <paramref name="p"/> falls along a piece seen from above, carried on past an open piece's ends.</summary>
    static float Project(Measured m, Vector3 p, out float flatDistance)
    {
        flatDistance = float.MaxValue;
        float best = 0f;
        for (int leg = 0; leg < m.Legs; leg++)
        {
            Vector3 a = m.points[leg], ab = Flat(m.points[(leg + 1) % m.points.Count] - a);
            float   len2 = ab.sqrMagnitude;
            if (len2 < 1e-12f) continue;

            float t = Vector3.Dot(Flat(p - a), ab) / len2;
            float lo = !m.loop && leg == 0          ? float.MinValue : 0f;
            float hi = !m.loop && leg == m.Legs - 1 ? float.MaxValue : 1f;
            t = Mathf.Clamp(t, lo, hi);

            float d = Flat(p - (a + ab * t)).magnitude;
            if (d < flatDistance)
            {
                flatDistance = d;
                best         = m.along[leg] + Mathf.Sqrt(len2) * t;
            }
        }
        return best;
    }

    static Vector3 Across(Vector3 runs) => new Vector3(runs.z, 0f, -runs.x);

    /// <summary>
    /// The spot a point on a seam is, on this wall — the point and the way its seam runs in the
    /// wall's own frame (the frame <see cref="BuildChunks"/> builds in). Null for a wall with no
    /// line to measure along.
    /// </summary>
    public static WallSeamSpot SpotAt(IList<Node> nodes, bool closed, float bottomY, Vector3 point, Vector3 dir)
    {
        var lines = MeasuredLines(nodes, closed);

        // The piece the point stands beside, and how far along it.
        Measured on = null;
        float    s  = 0f, nearest = float.MaxValue;
        foreach (var m in lines)
        {
            float at = Project(m, point, out float d);
            if (d < nearest) { nearest = d; on = m; s = at; }
        }

        // A node standing on its own between two gaps — a column with no wall either side — is
        // measured from the node itself, along x.
        int   node   = -1;
        float nodeAt = 0f;
        for (int i = 0; i < (nodes?.Count ?? 0); i++)
        {
            if (!nodes[i].column || OnALine(lines, i)) continue;
            float d = Flat(point - nodes[i].top).magnitude;
            if (d < nearest) { nearest = d; on = null; node = i; }
        }

        Vector3 centre, runs;
        if (on != null)
        {
            // The node nearest along the piece.
            float bestGap = float.MaxValue;
            for (int i = 0; i < on.points.Count; i++)
            {
                if (on.nodeAt[i] < 0) continue;
                float gap = s - on.along[i];
                if (on.loop && on.length > Eps) gap = Mathf.Repeat(gap + on.length * 0.5f, on.length) - on.length * 0.5f;
                if (Mathf.Abs(gap) < Mathf.Abs(bestGap)) { bestGap = gap; node = on.nodeAt[i]; nodeAt = gap; }
            }
            if (node < 0) return null;
            At(on, s, out centre, out runs);
        }
        else if (node >= 0)
        {
            centre = nodes[node].top;
            runs   = Vector3.right;
            nodeAt = point.x - centre.x;
        }
        else return null;

        Vector3 across = Across(runs);
        float   top    = centre.y;
        bool    fromTop = top - point.y < point.y - bottomY;
        return new WallSeamSpot
        {
            node      = node,
            along     = nodeAt,
            across    = Vector3.Dot(point - centre, across),
            fromTop      = fromTop,
            height       = fromTop ? top - point.y : point.y - bottomY,
            measuredBoth = true,
            otherHeight  = fromTop ? point.y - bottomY : top - point.y,
            direction = new Vector3(Vector3.Dot(dir, runs), dir.y, Vector3.Dot(dir, across)).normalized,
        };
    }

    static bool OnALine(List<Measured> lines, int node)
    {
        foreach (var m in lines) if (m.nodeAt.Contains(node)) return true;
        return false;
    }

    /// <summary>
    /// Where a spot is on the wall as it stands now, and which way its seam runs. False if its
    /// node is gone. <paramref name="points"/> is where to look, in order: at its height measured
    /// the way it was taken, then — for a spot that carries both — measured the other way.
    /// </summary>
    public static bool Resolve(IList<Node> nodes, bool closed, float bottomY, WallSeamSpot spot,
                               out Vector3[] points, out Vector3 dir)
    {
        points = new Vector3[0];
        dir    = Vector3.zero;
        if (spot == null || nodes == null || spot.node < 0 || spot.node >= nodes.Count) return false;

        Vector3 centre, runs;
        Measured on = null;
        float    s  = 0f;
        foreach (var m in MeasuredLines(nodes, closed))
        {
            int i = m.nodeAt.IndexOf(spot.node);
            if (i < 0) continue;
            on = m;
            s  = m.along[i] + spot.along;
            break;
        }

        if (on != null)
        {
            At(on, s, out centre, out runs);
        }
        else
        {
            centre = nodes[spot.node].top + Vector3.right * spot.along;
            centre.y = nodes[spot.node].top.y;
            runs   = Vector3.right;
        }

        Vector3 across = Across(runs);
        Vector3 point  = centre + across * spot.across;
        point.y = spot.fromTop ? centre.y - spot.height : bottomY + spot.height;

        if (spot.measuredBoth)
        {
            Vector3 other = point;
            other.y = spot.fromTop ? bottomY + spot.otherHeight : centre.y - spot.otherHeight;
            points  = new[] { point, other };
        }
        else points = new[] { point };

        dir = (runs * spot.direction.x + Vector3.up * spot.direction.y + across * spot.direction.z).normalized;
        return true;
    }

    /// <summary>One chunk of the wall as it is built, and the heights its sides are lined across at.</summary>
    class Chunk
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector3> norms = new List<Vector3>();
        public readonly List<int>     tris  = new List<int>();
        public readonly List<float>   kinds = new List<float>();
        public readonly List<int>     parts = new List<int>();
        public readonly List<float>   rows  = new List<float>();   // low to high
    }

    /// <summary>
    /// Adds the heights of a node's openings' corners to <paramref name="rows"/>, kept low to
    /// high: across an archway's round top at every step, and round a porthole's circle.
    /// </summary>
    static void Rows(Node node, float bottomY, List<float> rows)
    {
        void Row(float y)
        {
            foreach (float r in rows) if (Mathf.Abs(r - y) < RowSnap) return;
            rows.Add(y);
        }

        if (node.archway && node.archWidth * 0.5f > 1e-4f && node.archHeight > 0f)
        {
            float halfW = node.archWidth * 0.5f;
            float round = Mathf.Min(halfW, node.archHeight);
            for (int step = 0; step <= ArchSteps / 2; step++)
            {
                float u = Mathf.Lerp(-1f, 1f, step / (float)ArchSteps);
                Row(bottomY + node.archHeight - round + round * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)));
            }
        }

        if (node.porthole && node.portholeRadius > 1e-4f)
        {
            float c = bottomY + node.portholeHeight;
            for (int step = 0; step <= PortholeSteps / 2; step++)
            {
                float h = node.portholeRadius * Mathf.Sin(Mathf.PI * step / PortholeSteps);
                Row(c - h);
                Row(c + h);
            }
        }

        rows.Sort();
    }

    /// <summary>The stone type each part of the wall is tagged with.</summary>
    struct PartKinds
    {
        public StoneFaceKind sides, top, ends, openings, columnSides, columnTop;
        public StoneFaceKind lipTop, lipSides, lipUnderside;
    }

    /// <summary>An archway or a porthole, placed along one piece of the wall.</summary>
    struct Cut
    {
        public bool  porthole; // a round hole with wall under it; else an archway up from the base
        public float at;       // how far along the piece its node is
        public float halfW;    // half its width along the wall — a porthole's radius
        public float round;    // archway: how tall its round top is
        public float height;   // archway: base to the top of the round. porthole: base to its centre
        public float topY;     // the wall's top at its node
    }

    /// <summary>How far one cut reaches at one point: from <c>lo</c> up to <c>hi</c>, where <c>on</c>.</summary>
    struct Open
    {
        public bool  on;
        public float lo, hi;
    }

    const float Eps = 1e-6f;

    /// <summary>
    /// How close a corner of a side has to be to a row to count as standing on it. The rows and
    /// the openings' outlines work the same heights out by different sums, so they disagree by a
    /// few hundredths of a millimetre; cutting across at the row then left a sliver beside the
    /// corner, which the stone shading's millimetre weld closed into a seam the size of a point.
    /// A millimetre, the same as that weld.
    /// </summary>
    const float RowSnap = 1e-3f;

    /// <summary>
    /// The piece's centre line with extra points wherever a cut needs them — an archway's two
    /// sides and across its round top, round a porthole's circle and where it crosses the base
    /// or the top — and how far each cut reaches at each point: once looking back along the line
    /// (<paramref name="cutIn"/>) and once looking on (<paramref name="cutOut"/>). The two differ
    /// only at an archway's straight side. Every point carries every cut, ordered low to high up
    /// the wall; <paramref name="closeAt"/> is the height each cut closes to where it doesn't reach.
    ///
    /// Also hands back how far along the piece each point is (<paramref name="alongOut"/>), where
    /// one chunk ends and the next begins (<paramref name="bounds"/>, low to high, each with a
    /// point of its own on the line), and how far along each node stands (<paramref name="marks"/>).
    /// </summary>
    static List<Vector3> Cuts(List<Vector3> line, List<int> nodeAt, bool loop, IList<Node> nodes,
                              float bottomY, out Open[][] cutIn, out Open[][] cutOut, out float[] closeAt,
                              out List<float> alongOut, out List<float> bounds, out List<(int node, float at)> marks)
    {
        int count = line.Count;

        // How far along the piece each point is — a loop's last entry is its whole length.
        var along = new float[count + 1];
        int legs  = loop ? count : count - 1;
        for (int i = 0; i < legs; i++)
            along[i + 1] = along[i] + Flat(line[(i + 1) % count] - line[i]).magnitude;
        float length = along[legs];

        var cuts = new List<Cut>();
        marks = new List<(int, float)>();
        var reach = new List<float>();   // how far each node's openings reach either side of it
        for (int i = 0; i < count; i++)
        {
            int k = nodeAt[i];
            if (k < 0) continue;
            var node = nodes[k];
            marks.Add((k, along[i]));
            float widest = 0f;

            if (node.archway)
            {
                float halfW  = node.archWidth * 0.5f;
                float height = node.archHeight;
                if (halfW > 1e-4f && height > 0f)
                {
                    cuts.Add(new Cut { at = along[i], halfW = halfW, round = Mathf.Min(halfW, height),
                                       height = height, topY = node.top.y });
                    widest = Mathf.Max(widest, halfW);
                }
            }

            if (node.porthole && node.portholeRadius > 1e-4f)
            {
                cuts.Add(new Cut { porthole = true, at = along[i], halfW = node.portholeRadius,
                                   height = node.portholeHeight, topY = node.top.y });
                widest = Mathf.Max(widest, node.portholeRadius);
            }
            reach.Add(widest);
        }

        // Low to high: archways first (they stand on the base), portholes by their centres.
        cuts.Sort((x, y) => CloseAt(x, bottomY).CompareTo(CloseAt(y, bottomY)));
        closeAt = new float[cuts.Count];
        for (int k = 0; k < cuts.Count; k++) closeAt[k] = CloseAt(cuts[k], bottomY);

        // Where one chunk ends and the next begins: halfway along the wall left standing between
        // two nodes' openings — halfway between the nodes, where neither has one. None where the
        // openings run into each other.
        bounds = new List<float>();
        int pairs = loop ? marks.Count : marks.Count - 1;
        for (int m = 0; m < pairs && length > 0f; m++)
        {
            int   next = (m + 1) % marks.Count;
            float from = marks[m].at + reach[m];
            float to   = marks[next].at - reach[next];
            if (next <= m) to += length;   // round past a loop's start
            if (to - from <= 1e-4f) continue;
            float b = (from + to) * 0.5f;
            bounds.Add(loop ? Mathf.Repeat(b, length) : b);
        }
        bounds.Sort();

        if (length <= 0f)
        {
            cutIn = cutOut = new Open[count][];
            for (int i = 0; i < count; i++) cutIn[i] = new Open[cuts.Count];
            alongOut = new List<float>(along);
            return line;
        }

        // Where the extra points go.
        var extra = new List<float>();
        void Extra(float s)
        {
            if (loop) s = Mathf.Repeat(s, length);
            else if (s < 0f || s > length) return;
            extra.Add(s);
        }
        foreach (var cut in cuts)
        {
            if (!cut.porthole)
            {
                // The two sides, and evenly across the round.
                for (int step = 0; step <= ArchSteps; step++)
                    Extra(cut.at + Mathf.Lerp(-cut.halfW, cut.halfW, step / (float)ArchSteps));
                continue;
            }

            // Evenly round the circle, and where it crosses the base or the top — so no stretch
            // has wall standing over the hole at one end and none at the other.
            float r = cut.halfW, c = bottomY + cut.height;
            for (int step = 0; step <= PortholeSteps; step++)
                Extra(cut.at - r * Mathf.Cos(Mathf.PI * step / PortholeSteps));
            foreach (float y in new[] { bottomY, cut.topY })
            {
                float dy = Mathf.Abs(y - c);
                if (dy >= r) continue;
                float dx = Mathf.Sqrt(r * r - dy * dy);
                Extra(cut.at - dx);
                Extra(cut.at + dx);
            }
        }
        foreach (float b in bounds) Extra(b);   // every chunk ends on a point of its own
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
        cutIn  = new Open[outCount][];
        cutOut = new Open[outCount][];
        for (int i = 0; i < outCount; i++)
        {
            cutIn[i]  = new Open[cuts.Count];
            cutOut[i] = new Open[cuts.Count];
            for (int k = 0; k < cuts.Count; k++)
            {
                cutIn[i][k]  = Reach(cuts[k], outAlong[i], -1, length, loop, bottomY);
                cutOut[i][k] = Reach(cuts[k], outAlong[i], +1, length, loop, bottomY);
            }
        }
        alongOut = outAlong;
        return outLine;
    }

    // Where a cut sits in the low-to-high order, and the height it closes to where it doesn't reach.
    static float CloseAt(Cut cut, float bottomY) => cut.porthole ? bottomY + cut.height : bottomY;

    /// <summary>
    /// How far one cut reaches at a point <paramref name="s"/> along the piece. Right on an
    /// archway's side, <paramref name="side"/> says which way is looked at — back along the line
    /// (-1) or on (+1) — so the wall beside the arch and the arch itself each get their own reach.
    /// A porthole closes to a point at its sides, so it reaches the same both ways.
    /// </summary>
    static Open Reach(Cut cut, float s, int side, float length, bool loop, float bottomY)
    {
        const float tol = 1e-5f;
        float d = s - cut.at;
        if (loop) d = Mathf.Repeat(d + length * 0.5f, length) - length * 0.5f;

        float u     = Mathf.Clamp(d / cut.halfW, -1f, 1f);
        float round = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));

        if (cut.porthole)
        {
            if (Mathf.Abs(d) > cut.halfW + tol) return default;
            float c = bottomY + cut.height, h = cut.halfW * round;
            return new Open { on = true, lo = c - h, hi = c + h };
        }

        bool inside = side > 0 ? d >= -cut.halfW - tol && d < cut.halfW - tol
                               : d > -cut.halfW + tol && d <= cut.halfW + tol;
        if (!inside) return default;
        return new Open { on = true, lo = bottomY, hi = bottomY + cut.height - cut.round + cut.round * round };
    }

    /// <summary>
    /// The wall left standing at one point, low to high, around the cuts in <paramref name="use"/>
    /// (x = the band's foot, y = its head): a band under each cut and one over the last. A band
    /// can be nothing high — where a cut reaches the base or the top, or runs into the next — so
    /// both ends of a stretch, given the same cuts, always come out with the same bands to join.
    /// </summary>
    static void Bands(Open[] open, List<int> use, float[] closeAt, float bottomY, float topY,
                      List<Vector2> bands)
    {
        bands.Clear();
        float at = bottomY;
        foreach (int k in use)
        {
            float lo = open[k].on ? open[k].lo : closeAt[k];
            float hi = open[k].on ? open[k].hi : lo;
            lo = Mathf.Clamp(lo, bottomY, topY);
            hi = Mathf.Clamp(hi, bottomY, topY);
            bands.Add(new Vector2(at, Mathf.Max(at, lo)));
            at = Mathf.Max(at, hi);
        }
        bands.Add(new Vector2(at, topY));
    }

    // The cuts reaching either of two points — the ones to band both round.
    static void Using(Open[] a, Open[] b, List<int> use)
    {
        use.Clear();
        for (int k = 0; k < a.Length; k++)
            if (a[k].on || b[k].on) use.Add(k);
    }

    // ── One unbroken piece: a side each way, the top, and a flat end at each open end ──
    // Each leg goes into its own chunk (legChunk), the piece worked out whole so they meet.
    // With a lip (lipH > 0), a square band lipHalf either side of the line stands on the top,
    // its top and underside cut back wherever a column's lip ring stands (rings).
    static void AppendSlab(Chunk[] legChunk, PartKinds stone,
                           List<Vector3> line, Open[][] cutIn, Open[][] cutOut, float[] closeAt,
                           bool loop, float half, float bottomY, float lipH = 0f, float lipHalf = 0f,
                           IList<LipRing> rings = null)
    {
        bool  hasLip   = lipH > Eps;
        float lipScale = hasLip ? lipHalf / half : 1f;   // the lip's mitred offsets are the wall's, scaled
        Vector3 lift   = Vector3.up * lipH;

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

        var use    = new List<int>(closeAt.Length);
        var bandsA = new List<Vector2>();
        var bandsB = new List<Vector2>();

        int legs = loop ? count : count - 1;
        for (int i = 0; i < legs; i++)
        {
            Chunk   c   = legChunk[i];
            int     j   = (i + 1) % count;
            Vector3 a   = line[i], b = line[j];
            Vector3 dir = Flat(b - a).normalized;
            Vector3 nL  = Left(dir);

            Vector3 aL = a + offsets[i], bL = b + offsets[j];
            Vector3 aR = a - offsets[i], bR = b - offsets[j];

            // The wall standing at each end, round the cuts reaching either.
            Using(cutOut[i], cutIn[j], use);
            Bands(cutOut[i], use, closeAt, bottomY, a.y, bandsA);
            Bands(cutIn[j],  use, closeAt, bottomY, b.y, bandsB);

            int last = bandsA.Count - 1;
            for (int m = 0; m <= last; m++)
            {
                Vector2 ba = bandsA[m], bb = bandsB[m];
                if (ba.y - ba.x <= Eps && bb.y - bb.x <= Eps) continue;   // nothing standing here

                // Left side, right side — lined across at the chunk's rows.
                Side(c, aL, bL, ba, bb, nL);
                Side(c, aR, bR, ba, bb, -nL);
                StoneFaces.Tag(c.kinds, c.tris, stone.sides);

                // The cut under this band: its roof, across, facing down into it. None on the drop.
                if (m > 0 && (ba.x - bandsA[m - 1].y > Eps || bb.x - bandsB[m - 1].y > Eps))
                {
                    Vector3 down = Vector3.Cross(Down(b, bb.x) - Down(a, ba.x), nL).normalized;
                    Quad(c, Down(aL, ba.x), Down(bL, bb.x), Down(bR, bb.x), Down(aR, ba.x), down);
                    StoneFaces.Tag(c.kinds, c.tris, stone.openings);
                }

                // The cut over this band: its floor, across, facing up into it.
                if (m < last && (bandsA[m + 1].x - ba.y > Eps || bandsB[m + 1].x - bb.y > Eps))
                {
                    Vector3 up = -Vector3.Cross(Down(b, bb.y) - Down(a, ba.y), nL).normalized;
                    Quad(c, Down(aL, ba.y), Down(bL, bb.y), Down(bR, bb.y), Down(aR, ba.y), up);
                    StoneFaces.Tag(c.kinds, c.tris, stone.openings);
                }
            }

            // An archway's straight side, across, where the opening starts or ends at this point.
            if (loop || i > 0) Jambs(c, line, offsets, cutIn, cutOut, closeAt, i, count, bottomY);

            StoneFaces.Tag(c.kinds, c.tris, stone.openings);
            StoneFaces.Part(c.parts, c.tris, 0);

            // The top, across — unless a cut runs out through it the whole stretch. Under a lip,
            // only what shows either side of a lip narrower than the wall.
            Vector2 ta = bandsA[last], tb = bandsB[last];
            Vector3 aLL = a + offsets[i] * lipScale, bLL = b + offsets[j] * lipScale;
            Vector3 aLR = a - offsets[i] * lipScale, bLR = b - offsets[j] * lipScale;
            if (ta.y - ta.x > Eps || tb.y - tb.x > Eps)
            {
                if (!hasLip) Quad(c, aL, bL, bR, aR, Vector3.up);
                else if (lipHalf < half - Eps)
                {
                    Quad(c, aL, bL, bLL, aLL, Vector3.up);
                    Quad(c, aLR, bLR, bR, aR, Vector3.up);
                }
            }
            StoneFaces.Tag(c.kinds, c.tris, stone.top);
            StoneFaces.Part(c.parts, c.tris, 1);

            if (!hasLip) continue;

            // The lip: its two sides, its top, and its underside where it overhangs the wall.
            Quad(c, aLL, bLL, bLL + lift, aLL + lift, nL);
            Quad(c, aLR, bLR, bLR + lift, aLR + lift, -nL);
            StoneFaces.Tag(c.kinds, c.tris, stone.lipSides);
            StoneFaces.Part(c.parts, c.tris, 4);

            FlatLessRings(c, rings, aLL + lift, bLL + lift, bLR + lift, aLR + lift, Vector3.up);
            StoneFaces.Tag(c.kinds, c.tris, stone.lipTop);
            StoneFaces.Part(c.parts, c.tris, 5);

            if (lipHalf > half + Eps)
            {
                FlatLessRings(c, rings, aL, bL, bLL, aLL, Vector3.down);
                FlatLessRings(c, rings, aR, bR, bLR, aLR, Vector3.down);
                StoneFaces.Tag(c.kinds, c.tris, stone.lipUnderside);
                StoneFaces.Part(c.parts, c.tris, 6);
            }
        }

        if (loop) return;

        // Flat ends, one face for each band of wall standing there.
        Vector3 startDir = Flat(line[1] - line[0]).normalized;
        Vector3 endDir   = Flat(line[count - 1] - line[count - 2]).normalized;
        Chunk   startC   = legChunk[0], endC = legChunk[legs - 1];
        EndFaces(startC, line[0], offsets[0], cutOut[0], closeAt, bottomY, -startDir);
        StoneFaces.Tag(startC.kinds, startC.tris, stone.ends);
        StoneFaces.Part(startC.parts, startC.tris, 0);
        EndFaces(endC, line[count - 1], offsets[count - 1], cutIn[count - 1], closeAt, bottomY, endDir);
        StoneFaces.Tag(endC.kinds, endC.tris, stone.ends);
        StoneFaces.Part(endC.parts, endC.tris, 0);

        if (!hasLip) return;

        // The lip's flat ends, over the wall's.
        Vector3 start = line[0], end = line[count - 1];
        Across(startC, start, offsets[0] * lipScale, start.y, start.y + lipH, -startDir);
        StoneFaces.Tag(startC.kinds, startC.tris, stone.lipSides);
        StoneFaces.Part(startC.parts, startC.tris, 4);
        Across(endC, end, offsets[count - 1] * lipScale, end.y, end.y + lipH, endDir);
        StoneFaces.Tag(endC.kinds, endC.tris, stone.lipSides);
        StoneFaces.Part(endC.parts, endC.tris, 4);
    }

    /// <summary>
    /// One band of one side of a leg, from <paramref name="a"/> to <paramref name="b"/>: one face
    /// where the chunk has no rows crossing it, else cut across at each row — the slice between
    /// two rows taking whatever shape the band's slanted foot or head leaves it.
    /// </summary>
    static void Side(Chunk c, Vector3 a, Vector3 b, Vector2 ba, Vector2 bb, Vector3 n)
    {
        float lo = Mathf.Min(ba.x, bb.x), hi = Mathf.Max(ba.y, bb.y);

        bool crossed = false;
        foreach (float r in c.rows)
            if (r > lo + RowSnap && r < hi - RowSnap) { crossed = true; break; }
        if (!crossed)
        {
            Quad(c, Down(a, ba.x), Down(b, bb.x), Down(b, bb.y), Down(a, ba.y), n);
            return;
        }

        // The band flattened out: x = 0 at a to 1 at b, y = height.
        var shape = new List<Vector2> { new Vector2(0f, ba.x), new Vector2(1f, bb.x),
                                        new Vector2(1f, bb.y), new Vector2(0f, ba.y) };
        float from = lo;
        foreach (float r in c.rows)
        {
            if (r <= lo + RowSnap || r >= hi - RowSnap) continue;
            Slice(c, a, b, Clip(Clip(shape, from, true), r, false), n);
            from = r;
        }
        Slice(c, a, b, Clip(shape, from, true), n);
    }

    // What of a flat outline lies above (keepAbove) or below a height — the outline stays convex.
    // A corner within RowSnap of the height counts as on it: kept on both sides, with no second
    // point cut in beside it.
    static List<Vector2> Clip(List<Vector2> shape, float y, bool keepAbove)
    {
        // 1 = kept, 0 = on the height, -1 = cut away.
        int Where(Vector2 p) => Mathf.Abs(p.y - y) <= RowSnap ? 0 : (p.y > y) == keepAbove ? 1 : -1;

        var kept = new List<Vector2>(shape.Count + 2);
        for (int i = 0; i < shape.Count; i++)
        {
            Vector2 prev = shape[(i - 1 + shape.Count) % shape.Count], cur = shape[i];
            int wPrev = Where(prev), wCur = Where(cur);
            if (wPrev * wCur < 0) kept.Add(Vector2.Lerp(prev, cur, (y - prev.y) / (cur.y - prev.y)));
            if (wCur >= 0) kept.Add(cur);
        }
        return kept;
    }

    // One slice of a side, stood back up between a and b and fanned into triangles.
    static void Slice(Chunk c, Vector3 a, Vector3 b, List<Vector2> shape, Vector3 n)
    {
        if (shape.Count < 3) return;
        int v = c.verts.Count;
        foreach (var p in shape)
        {
            c.verts.Add(new Vector3(Mathf.Lerp(a.x, b.x, p.x), p.y, Mathf.Lerp(a.z, b.z, p.x)));
            c.norms.Add(n);
        }
        for (int k = 1; k < shape.Count - 1; k++) AddTri(c.tris, c.verts, n, v, v + k, v + k + 1);
    }

    static void EndFaces(Chunk c, Vector3 p, Vector3 offset, Open[] open, float[] closeAt, float bottomY, Vector3 facing)
    {
        var use   = new List<int>();
        var bands = new List<Vector2>();
        Using(open, open, use);
        Bands(open, use, closeAt, bottomY, p.y, bands);
        foreach (var band in bands)
            Across(c, p, offset, band.x, band.y, facing);
    }

    // The faces across the wall at point i wherever it stands on one side of the point and is
    // open on the other — an archway's straight side — each facing into the opening.
    static void Jambs(Chunk c, List<Vector3> line, Vector3[] offsets, Open[][] cutIn, Open[][] cutOut,
                      float[] closeAt, int i, int count, float bottomY)
    {
        Open[] oi = cutIn[i], oo = cutOut[i];
        bool differs = false;
        for (int k = 0; k < oi.Length && !differs; k++)
            differs = oi[k].on != oo[k].on || oi[k].lo != oo[k].lo || oi[k].hi != oo[k].hi;
        if (!differs) return;

        var use = new List<int>();
        var bandsIn  = new List<Vector2>();
        var bandsOut = new List<Vector2>();
        Using(oi, oo, use);
        Bands(oi, use, closeAt, bottomY, line[i].y, bandsIn);
        Bands(oo, use, closeAt, bottomY, line[i].y, bandsOut);

        Vector3 p    = line[i];
        Vector3 on   = Flat(line[(i + 1) % count] - p).normalized;
        Vector3 back = Flat(line[(i - 1 + count) % count] - p).normalized;
        Uncovered(c, p, offsets[i], bandsIn,  bandsOut, on);    // standing behind, open ahead
        Uncovered(c, p, offsets[i], bandsOut, bandsIn,  back);  // standing ahead, open behind
    }

    // A face across at p for every part of `standing` that `other` leaves open. Both are low to
    // high and never overlap themselves, so one walk up through `other` does each band.
    static void Uncovered(Chunk c, Vector3 p, Vector3 offset, List<Vector2> standing, List<Vector2> other, Vector3 facing)
    {
        foreach (var s in standing)
        {
            float lo = s.x;
            foreach (var o in other)
            {
                if (o.y - o.x <= Eps || o.y <= lo) continue;
                if (o.x >= s.y) break;
                if (o.x > lo) Across(c, p, offset, lo, o.x, facing);
                lo = Mathf.Max(lo, o.y);
            }
            Across(c, p, offset, lo, s.y, facing);
        }
    }

    // One face across the wall at p, from lo up to hi.
    static void Across(Chunk c, Vector3 p, Vector3 offset, float lo, float hi, Vector3 facing)
    {
        if (hi - lo <= Eps) return;
        Quad(c, Down(p + offset, lo), Down(p - offset, lo), Down(p - offset, hi), Down(p + offset, hi), facing);
    }

    // ── A node's column: smooth round sides from the drop up to its top, and a flat cap ──
    static void AppendColumn(Chunk c, PartKinds stone, Node node, float bottomY)
    {
        var verts = c.verts;
        var norms = c.norms;
        var tris  = c.tris;

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
        StoneFaces.Tag(c.kinds, tris, stone.columnSides);
        StoneFaces.Part(c.parts, tris, 2);

        int mid = verts.Count;
        verts.Add(centre); norms.Add(Vector3.up);
        for (int i = 0; i <= ColumnSides; i++)
        {
            float a = i * Mathf.PI * 2f / ColumnSides;
            verts.Add(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r); norms.Add(Vector3.up);
        }
        for (int i = 0; i < ColumnSides; i++)
            AddTri(tris, verts, Vector3.up, mid, mid + 1 + i, mid + 2 + i);
        StoneFaces.Tag(c.kinds, tris, stone.columnTop);
        StoneFaces.Part(c.parts, tris, 3);
    }

    // ── The lip going round a column: a ring standing on the wall's top at the node, `thick` out
    // from the column's side — its outer side, top and underside; its inside is against the column ──
    static void AppendLipRing(Chunk c, PartKinds stone, Node node, float lipH, float thick)
    {
        float   r  = Mathf.Max(0.001f, node.columnRadius);
        float   rO = r + thick;
        Vector3 lo = node.top, hi = node.top + Vector3.up * lipH;

        int start = c.verts.Count;
        for (int i = 0; i <= ColumnSides; i++)
        {
            Vector3 n = RoundDir(i);
            c.verts.Add(lo + n * rO); c.norms.Add(n);
            c.verts.Add(hi + n * rO); c.norms.Add(n);
        }
        for (int i = 0; i < ColumnSides; i++)
        {
            int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            Vector3 n = c.norms[b0] + c.norms[b1];
            AddTri(c.tris, c.verts, n, b0, t0, t1);
            AddTri(c.tris, c.verts, n, b0, t1, b1);
        }
        StoneFaces.Tag(c.kinds, c.tris, stone.lipSides);
        StoneFaces.Part(c.parts, c.tris, 4);

        FlatRing(c, hi, r, rO, Vector3.up);
        StoneFaces.Tag(c.kinds, c.tris, stone.lipTop);
        StoneFaces.Part(c.parts, c.tris, 5);

        FlatRing(c, lo, r, rO, Vector3.down);
        StoneFaces.Tag(c.kinds, c.tris, stone.lipUnderside);
        StoneFaces.Part(c.parts, c.tris, 6);
    }

    static void FlatRing(Chunk c, Vector3 centre, float rIn, float rOut, Vector3 n)
    {
        for (int i = 0; i < ColumnSides; i++)
        {
            Vector3 d0 = RoundDir(i), d1 = RoundDir(i + 1);
            Quad(c, centre + d0 * rIn, centre + d0 * rOut, centre + d1 * rOut, centre + d1 * rIn, n);
        }
    }

    // Out from a column's centre towards its i-th side, the same steps round as AppendColumn takes.
    static Vector3 RoundDir(int i)
    {
        float a = i * Mathf.PI * 2f / ColumnSides;
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    /// <summary>A column's lip ring as the lip's own faces are cut back to it.</summary>
    struct LipRing
    {
        public List<Vector2> outline; // its outer edge on the map (x, z), anticlockwise
        public Vector2 centre;
        public float   radius;
        public float   lo, hi;        // the heights it stands between
    }

    static LipRing RingOf(Node node, float lipH, float thick)
    {
        float rO = Mathf.Max(0.001f, node.columnRadius) + thick;
        var   ring = new LipRing
        {
            outline = new List<Vector2>(ColumnSides),
            centre  = Plan(node.top),
            radius  = rO,
            lo      = node.top.y,
            hi      = node.top.y + lipH,
        };
        for (int i = 0; i < ColumnSides; i++) ring.outline.Add(Plan(node.top + RoundDir(i) * rO));
        return ring;
    }

    // A flat face like Quad, less wherever a column's lip ring stands at its height — so the
    // ring's top and underside are the only faces there, not two fighting over the same place.
    static void FlatLessRings(Chunk c, IList<LipRing> rings, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n)
    {
        List<LipRing> cutBy = null;
        if (rings != null)
        {
            float lo = Mathf.Min(Mathf.Min(p0.y, p1.y), Mathf.Min(p2.y, p3.y));
            float hi = Mathf.Max(Mathf.Max(p0.y, p1.y), Mathf.Max(p2.y, p3.y));
            Vector2 min = Vector2.Min(Vector2.Min(Plan(p0), Plan(p1)), Vector2.Min(Plan(p2), Plan(p3)));
            Vector2 max = Vector2.Max(Vector2.Max(Plan(p0), Plan(p1)), Vector2.Max(Plan(p2), Plan(p3)));
            foreach (var ring in rings)
            {
                // A face off the ring's height isn't fighting it — cut, it would leave a hole.
                if (lo < ring.lo - RowSnap || hi > ring.hi + RowSnap) continue;
                if (ring.centre.x + ring.radius < min.x || ring.centre.x - ring.radius > max.x ||
                    ring.centre.y + ring.radius < min.y || ring.centre.y - ring.radius > max.y) continue;
                (cutBy ??= new List<LipRing>()).Add(ring);
            }
        }
        if (cutBy == null) { Quad(c, p0, p1, p2, p3, n); return; }

        // Quad's own two triangles, so where a ring's edge crosses between them both cut it alike.
        TriangleLessRings(c, cutBy, p0, p1, p2, n);
        TriangleLessRings(c, cutBy, p0, p2, p3, n);
    }

    static void TriangleLessRings(Chunk c, List<LipRing> rings, Vector3 a, Vector3 b, Vector3 d, Vector3 n)
    {
        Vector2 a2 = Plan(a), b2 = Plan(b), d2 = Plan(d);
        float   area = Cross2(b2 - a2, d2 - a2);
        if (Mathf.Abs(area) < 1e-14f) return;

        var pieces = new List<List<Vector2>> { area > 0f ? new List<Vector2> { a2, b2, d2 }
                                                         : new List<Vector2> { a2, d2, b2 } };
        foreach (var ring in rings)
        {
            var next = new List<List<Vector2>>();
            foreach (var piece in pieces)
                next.AddRange(Subtract(piece, ring.outline) ?? new List<List<Vector2>> { piece });
            pieces = next;
        }

        // Every corner left takes its height off the triangle, so a sloping lip stays flat.
        foreach (var piece in pieces)
        {
            int v = c.verts.Count;
            foreach (var p in piece)
            {
                c.verts.Add(new Vector3(p.x, HeightOn(a, b, d, p), p.y));
                c.norms.Add(n);
            }
            var tris = EarClip(piece);
            for (int k = 0; k + 2 < tris.Count; k += 3)
                AddTri(c.tris, c.verts, n, v + tris[k], v + tris[k + 1], v + tris[k + 2]);
        }
    }

    static Vector2 Plan(Vector3 p) => new Vector2(p.x, p.z);

    static float Cross2(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    // The height over p (x, z) of the flat triangle a, b, d.
    static float HeightOn(Vector3 a, Vector3 b, Vector3 d, Vector2 p)
    {
        Vector2 ab = Plan(b) - Plan(a), ad = Plan(d) - Plan(a), ap = p - Plan(a);
        float   den = Cross2(ab, ad);
        float   wb  = Cross2(ap, ad) / den, wd = Cross2(ab, ap) / den;
        return a.y + wb * (b.y - a.y) + wd * (d.y - a.y);
    }

    /// <summary>
    /// What of the outline <paramref name="shape"/> lies outside <paramref name="hole"/> — both
    /// anticlockwise, the hole convex: the shape's sides outside the hole and the hole's sides
    /// inside the shape, chained back into outlines, anticlockwise. Where the two cross is worked
    /// out the same whichever way round the shape's side runs, so two faces sharing a side are cut
    /// at the same point on it. Null when they touch in a way that won't chain (a corner exactly
    /// on the other's side), for the caller to keep the shape whole.
    /// </summary>
    static List<List<Vector2>> Subtract(List<Vector2> shape, List<Vector2> hole)
    {
        int nS = shape.Count, nH = hole.Count;
        var pts = new List<Vector2>(shape);
        pts.AddRange(hole);

        var onShape = new List<(float t, int id)>[nS];
        var onHole  = new List<(float t, int id)>[nH];
        for (int i = 0; i < nS; i++) onShape[i] = new List<(float, int)>();
        for (int k = 0; k < nH; k++) onHole[k]  = new List<(float, int)>();

        for (int i = 0; i < nS; i++)
        {
            Vector2 a0 = shape[i], a1 = shape[(i + 1) % nS];
            bool    flip = a1.x < a0.x || (a1.x == a0.x && a1.y < a0.y);
            Vector2 s0 = flip ? a1 : a0, r = (flip ? a0 : a1) - s0;
            for (int k = 0; k < nH; k++)
            {
                Vector2 p = hole[k], s = hole[(k + 1) % nH] - p;
                float   den = Cross2(r, s);
                if (Mathf.Abs(den) < 1e-14f) continue;
                Vector2 sp = p - s0;
                float   t  = Cross2(sp, s) / den, u = Cross2(sp, r) / den;
                if (t <= 0f || t >= 1f || u < 0f || u >= 1f) continue;
                int id = pts.Count;
                pts.Add(p + s * u);
                onShape[i].Add((flip ? 1f - t : t, id));
                onHole[k].Add((u, id));
            }
        }

        // No crossings: the shape is wholly inside the hole, or wholly clear of it.
        if (pts.Count == nS + nH)
            return Inside(hole, shape[0]) ? new List<List<Vector2>>() : new List<List<Vector2>> { shape };

        var next = new Dictionary<int, int>();
        bool Walk(List<(float t, int id)> cuts, int from, int to, bool keepInside, List<Vector2> other, bool reverse)
        {
            cuts.Sort((x, y) => x.t.CompareTo(y.t));
            cuts.Add((1f, to));
            int prev = from;
            foreach (var (_, id) in cuts)
            {
                if (Inside(other, (pts[prev] + pts[id]) * 0.5f) == keepInside)
                {
                    int s = reverse ? id : prev, e = reverse ? prev : id;
                    if (next.ContainsKey(s)) return false;
                    next[s] = e;
                }
                prev = id;
            }
            return true;
        }
        for (int i = 0; i < nS; i++)
            if (!Walk(onShape[i], i, (i + 1) % nS, false, hole, false)) return null;
        for (int k = 0; k < nH; k++)
            if (!Walk(onHole[k], nS + k, nS + (k + 1) % nH, true, shape, true)) return null;

        var loops = new List<List<Vector2>>();
        var used  = new HashSet<int>();
        foreach (int start in new List<int>(next.Keys))
        {
            if (used.Contains(start)) continue;
            var loop = new List<Vector2>();
            int at   = start;
            do
            {
                if (!used.Add(at) || !next.TryGetValue(at, out int on)) return null;
                loop.Add(pts[at]);
                at = on;
            } while (at != start);
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }

    static bool Inside(List<Vector2> outline, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            Vector2 a = outline[i], b = outline[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    // An anticlockwise outline cut into triangles, corner by corner — never across a corner
    // lying on a straight side, which would leave that corner hanging off a neighbouring face.
    static List<int> EarClip(List<Vector2> outline)
    {
        var left = new List<int>(outline.Count);
        for (int i = 0; i < outline.Count; i++) left.Add(i);
        var tris = new List<int>((outline.Count - 2) * 3);

        while (left.Count > 3)
        {
            int ear = -1;
            for (int k = 0; k < left.Count && ear < 0; k++)
            {
                int ia = left[(k - 1 + left.Count) % left.Count], ib = left[k], id = left[(k + 1) % left.Count];
                Vector2 a = outline[ia], b = outline[ib], d = outline[id];
                if (Cross2(b - a, d - b) <= 1e-6f * (b - a).magnitude * (d - b).magnitude) continue;   // reflex, or flat
                bool blocked = false;
                foreach (int o in left)
                {
                    if (o == ia || o == ib || o == id) continue;
                    Vector2 p = outline[o];
                    if (Cross2(b - a, p - a) >= 0f && Cross2(d - b, p - b) >= 0f && Cross2(a - d, p - d) >= 0f)
                    { blocked = true; break; }
                }
                if (!blocked) ear = k;
            }
            if (ear < 0) break;   // nothing clean left to cut — leave the rest open rather than tear it

            tris.Add(left[(ear - 1 + left.Count) % left.Count]);
            tris.Add(left[ear]);
            tris.Add(left[(ear + 1) % left.Count]);
            left.RemoveAt(ear);
        }
        if (left.Count == 3) { tris.Add(left[0]); tris.Add(left[1]); tris.Add(left[2]); }
        return tris;
    }

    static Vector3 Left(Vector3 dir) => new Vector3(-dir.z, 0f, dir.x);

    static Vector3 Down(Vector3 p, float bottomY) => new Vector3(p.x, bottomY, p.z);

    static void Quad(Chunk c, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n)
        => Quad(c.verts, c.norms, c.tris, p0, p1, p2, p3, n);

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
