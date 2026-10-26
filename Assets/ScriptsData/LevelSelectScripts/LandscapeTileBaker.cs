using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds one landscape tile's mesh with the hills already in it: the same grid the tile always
/// had, each corner raised by <see cref="LandscapeHillHeight.Surface"/>, its lighting normal, and
/// the noise lean (UV1: x the lean without its grain, y how much noise there is).
///
/// The ground is trimmed exactly where it meets a river run or pool. Grid triangles near an edge
/// are split 4×4 in their own plane — so the surface is exactly the triangle it was, nothing
/// moves — and each small piece is clipped where the cut really falls, found on the true river
/// edge rather than guessed between corners. Curves stay curves.
///
/// No cracks: a piece that is split shares its new edge points with its neighbour, whose triangle
/// is fanned to meet them — across tile borders too, since every tile decides from the same world
/// positions, looking one cell past its own edge.
/// </summary>
public static class LandscapeTileBaker
{
    const int Split = 4;   // a split triangle becomes a 4×4 lattice of small ones

    struct Corner
    {
        public Vector3 position;   // local, raised
        public Vector3 normal;     // local
        public Vector2 noise;      // lean without grain, amount
        public Vector2 uv;
    }

    enum Kind { Keep, Split_, Drop }

    /// <summary>Rebuilds <paramref name="mesh"/> as the tile at <paramref name="tile"/>.</summary>
    public static void Bake(Mesh mesh, LandscapeHillHeight.Surface surface, Transform tile,
                            float width, float depth, int cellsX, int cellsZ)
    {
        var bake = new Baking(surface, tile, width, depth, cellsX, cellsZ);
        bake.Run();
        bake.Write(mesh);
    }

    sealed class Baking
    {
        readonly LandscapeHillHeight.Surface _s;
        readonly Matrix4x4 _toWorld, _toLocal;
        readonly float _width, _depth, _stepX, _stepZ, _cellReach;
        readonly int   _cellsX, _cellsZ;
        readonly bool  _cuts;

        readonly Dictionary<long, Corner>  _corners  = new();
        readonly Dictionary<long, float>   _cut      = new();
        readonly Dictionary<long, int>     _vertexAt = new();
        readonly Dictionary<(long, long), int> _crossing = new();
        readonly HashSet<(long, long)>     _splitEdges = new();

        readonly List<Vector3> _positions = new();
        readonly List<Vector3> _normals   = new();
        readonly List<Vector2> _uv0       = new();
        readonly List<Vector2> _uv1       = new();
        readonly List<int>     _triangles = new();

        public Baking(LandscapeHillHeight.Surface s, Transform tile, float width, float depth,
                      int cellsX, int cellsZ)
        {
            _s       = s;
            _toWorld = tile.localToWorldMatrix;
            _toLocal = tile.worldToLocalMatrix;
            _width   = width;
            _depth   = depth;
            _cellsX  = Mathf.Max(1, cellsX);
            _cellsZ  = Mathf.Max(1, cellsZ);
            _stepX   = width / _cellsX;
            _stepZ   = depth / _cellsZ;
            _cuts    = s.nearCuts == null ? s.edgeCount > 0 : s.nearCuts.Count > 0;

            // No point of a grid triangle is further than this from one of its corners.
            Vector3 cell = tile.TransformVector(new Vector3(_stepX, 0f, _stepZ));
            _cellReach = new Vector2(cell.x, cell.z).magnitude;
        }

        // ── Lattice: quarter-cell steps, so every split point has whole coordinates ──

        static long Key(int i, int j) => ((long)(i + 100000) << 32) | (uint)(j + 100000);

        Vector3 FlatLocal(float i, float j) => new(i * _stepX / Split, 0f, j * _stepZ / Split);

        Vector2 WorldXZ(float i, float j)
        {
            Vector3 w = _toWorld.MultiplyPoint3x4(FlatLocal(i, j));
            return new Vector2(w.x, w.z);
        }

        float CutAt(int i, int j)
        {
            if (!_cuts) return float.MaxValue;
            long key = Key(i, j);
            if (!_cut.TryGetValue(key, out float f))
                _cut[key] = f = _s.Cut(WorldXZ(i, j));
            return f;
        }

        // A grid corner, raised exactly.
        Corner CornerAt(int i, int j)
        {
            long key = Key(i, j);
            if (_corners.TryGetValue(key, out var c)) return c;

            Vector3 flat  = FlatLocal(i, j);
            Vector3 world = _toWorld.MultiplyPoint3x4(flat);
            var     xz    = new Vector2(world.x, world.z);
            float   baseY = world.y;

            float   smooth = _s.SmoothHeight(xz, baseY);
            Vector3 noise  = _s.Noise(xz);
            float   hold   = _s.Calm(xz);
            float   height = smooth + noise.x * (1f - hold);   // noise in the geometry only

            c.position = _toLocal.MultiplyPoint3x4(new Vector3(world.x, baseY + height, world.z));
            c.normal   = _toLocal.MultiplyVector(_s.Normal(xz, baseY)).normalized;
            c.noise    = new Vector2(noise.y, noise.z);
            c.uv       = new Vector2(flat.x / _width, flat.z / _depth);
            _corners[key] = c;
            return c;
        }

        // ── Grid triangles, as the flat tile always laid them out ──

        // Corners of triangle t (0 or 1) of cell (ci, cj), in lattice coordinates.
        static void TriangleCorners(int ci, int cj, int t, out Vector2Int a, out Vector2Int b, out Vector2Int c)
        {
            int x = ci * Split, z = cj * Split;
            if (t == 0) { a = new(x, z);         b = new(x, z + Split); c = new(x + Split, z); }
            else        { a = new(x + Split, z); b = new(x, z + Split); c = new(x + Split, z + Split); }
        }

        Kind KindOf(int ci, int cj, int t)
        {
            if (!_cuts) return Kind.Keep;
            TriangleCorners(ci, cj, t, out var a, out var b, out var c);
            float fa = CutAt(a.x, a.y), fb = CutAt(b.x, b.y), fc = CutAt(c.x, c.y);

            if (Mathf.Min(fa, Mathf.Min(fb, fc)) >  _cellReach) return Kind.Keep;   // nowhere near a cut
            if (Mathf.Max(fa, Mathf.Max(fb, fc)) <= -_cellReach) return Kind.Drop;  // wholly inside one
            return Kind.Split_;
        }

        static (long, long) EdgeKey(Vector2Int p, Vector2Int q)
        {
            long a = Key(p.x, p.y), b = Key(q.x, q.y);
            return a < b ? (a, b) : (b, a);
        }

        public void Run()
        {
            // Which grid edges are split: any edge of a split triangle, looking one cell past the
            // tile so a neighbouring tile's split triangle is met too.
            if (_cuts)
            {
                for (int ci = -1; ci <= _cellsX; ci++)
                for (int cj = -1; cj <= _cellsZ; cj++)
                for (int t = 0; t < 2; t++)
                {
                    if (KindOf(ci, cj, t) != Kind.Split_) continue;
                    TriangleCorners(ci, cj, t, out var a, out var b, out var c);
                    _splitEdges.Add(EdgeKey(a, b));
                    _splitEdges.Add(EdgeKey(b, c));
                    _splitEdges.Add(EdgeKey(c, a));
                }
            }

            for (int ci = 0; ci < _cellsX; ci++)
            for (int cj = 0; cj < _cellsZ; cj++)
            for (int t = 0; t < 2; t++)
            {
                TriangleCorners(ci, cj, t, out var a, out var b, out var c);
                switch (KindOf(ci, cj, t))
                {
                    case Kind.Keep:   EmitKept(a, b, c);  break;
                    case Kind.Split_: EmitSplit(a, b, c); break;
                }
            }
        }

        // ── Points in one triangle's plane ──

        // The point at (u, v) quarters along a→b and a→c, its height, normal and noise carried
        // across the triangle's plane from the corners.
        int LatticeVertex(Vector2Int a, Vector2Int b, Vector2Int c, int u, int v)
        {
            int i = a.x + (u * (b.x - a.x) + v * (c.x - a.x)) / Split;
            int j = a.y + (u * (b.y - a.y) + v * (c.y - a.y)) / Split;

            long key = Key(i, j);
            if (_vertexAt.TryGetValue(key, out int index)) return index;

            float wb = (float)u / Split, wc = (float)v / Split, wa = 1f - wb - wc;
            index = AddBlend(CornerAt(a.x, a.y), CornerAt(b.x, b.y), CornerAt(c.x, c.y), wa, wb, wc);
            _vertexAt[key] = index;
            return index;
        }

        int AddBlend(Corner ca, Corner cb, Corner cc, float wa, float wb, float wc)
        {
            _positions.Add(ca.position * wa + cb.position * wb + cc.position * wc);
            _normals.Add((ca.normal * wa + cb.normal * wb + cc.normal * wc).normalized);
            _uv1.Add(ca.noise * wa + cb.noise * wb + cc.noise * wc);
            _uv0.Add(ca.uv * wa + cb.uv * wb + cc.uv * wc);
            return _positions.Count - 1;
        }

        // A kept triangle: whole, or fanned from its middle to meet split points on its edges.
        void EmitKept(Vector2Int a, Vector2Int b, Vector2Int c)
        {
            bool ab = _splitEdges.Contains(EdgeKey(a, b));
            bool bc = _splitEdges.Contains(EdgeKey(b, c));
            bool ca = _splitEdges.Contains(EdgeKey(c, a));

            if (!ab && !bc && !ca)
            {
                AddTriangle(LatticeVertex(a, b, c, 0, 0), LatticeVertex(a, b, c, Split, 0),
                            LatticeVertex(a, b, c, 0, Split));
                return;
            }

            // Round the edge in order a → b → c, so the fan keeps the triangle's winding.
            var ring = new List<int>();
            for (int k = 0; k < Split; k++)
                if (k == 0 || ab) ring.Add(LatticeVertex(a, b, c, k, 0));
            for (int k = 0; k < Split; k++)
                if (k == 0 || bc) ring.Add(LatticeVertex(a, b, c, Split - k, k));
            for (int k = 0; k < Split; k++)
                if (k == 0 || ca) ring.Add(LatticeVertex(a, b, c, 0, Split - k));

            float third = 1f / 3f;
            int   mid   = AddBlend(CornerAt(a.x, a.y), CornerAt(b.x, b.y), CornerAt(c.x, c.y), third, third, third);
            for (int k = 0; k < ring.Count; k++)
                AddTriangle(mid, ring[k], ring[(k + 1) % ring.Count]);
        }

        // A triangle the cut passes near: 16 small ones, each clipped where the cut falls.
        void EmitSplit(Vector2Int a, Vector2Int b, Vector2Int c)
        {
            for (int u = 0; u < Split; u++)
            for (int v = 0; u + v < Split; v++)
            {
                Clip(a, b, c, (u, v), (u + 1, v), (u, v + 1));
                if (u + v < Split - 1)
                    Clip(a, b, c, (u + 1, v), (u + 1, v + 1), (u, v + 1));
            }
        }

        (int i, int j) LatticeOf(Vector2Int a, Vector2Int b, Vector2Int c, (int u, int v) p)
            => (a.x + (p.u * (b.x - a.x) + p.v * (c.x - a.x)) / Split,
                a.y + (p.u * (b.y - a.y) + p.v * (c.y - a.y)) / Split);

        readonly List<int> _poly = new(4);

        void Clip(Vector2Int a, Vector2Int b, Vector2Int c,
                  (int u, int v) p0, (int u, int v) p1, (int u, int v) p2)
        {
            var pts = new[] { p0, p1, p2 };
            var ij  = new (int i, int j)[3];
            var f   = new float[3];
            for (int k = 0; k < 3; k++)
            {
                ij[k] = LatticeOf(a, b, c, pts[k]);
                f[k]  = CutAt(ij[k].i, ij[k].j);
            }

            if (f[0] > 0f && f[1] > 0f && f[2] > 0f)
            {
                AddTriangle(LatticeVertex(a, b, c, p0.u, p0.v), LatticeVertex(a, b, c, p1.u, p1.v),
                            LatticeVertex(a, b, c, p2.u, p2.v));
                return;
            }
            if (f[0] <= 0f && f[1] <= 0f && f[2] <= 0f) return;

            _poly.Clear();
            for (int k = 0; k < 3; k++)
            {
                int n = (k + 1) % 3;
                if (f[k] > 0f) _poly.Add(LatticeVertex(a, b, c, pts[k].u, pts[k].v));
                if ((f[k] > 0f) != (f[n] > 0f))
                    _poly.Add(Crossing(a, b, c, pts[k], ij[k], f[k], pts[n], ij[n]));
            }
            for (int k = 1; k + 1 < _poly.Count; k++)
                AddTriangle(_poly[0], _poly[k], _poly[k + 1]);
        }

        // Where the cut crosses between two lattice points, found on the true river edge.
        int Crossing(Vector2Int a, Vector2Int b, Vector2Int c,
                     (int u, int v) pp, (int i, int j) p, float fp,
                     (int u, int v) qq, (int i, int j) q)
        {
            // Always searched from the same end, so both triangles sharing the edge agree.
            long kp = Key(p.i, p.j), kq = Key(q.i, q.j);
            var  key = kp < kq ? (kp, kq) : (kq, kp);
            if (_crossing.TryGetValue(key, out int index)) return index;

            int vp = LatticeVertex(a, b, c, pp.u, pp.v);
            int vq = LatticeVertex(a, b, c, qq.u, qq.v);
            if (kp > kq) { (p, q) = (q, p); (vp, vq) = (vq, vp); fp = CutAt(p.i, p.j); }

            Vector2 xp = WorldXZ(p.i, p.j), xq = WorldXZ(q.i, q.j);
            bool keepP = fp > 0f;
            float lo = 0f, hi = 1f;
            for (int k = 0; k < 18; k++)
            {
                float mid = 0.5f * (lo + hi);
                if ((_s.Cut(Vector2.Lerp(xp, xq, mid)) > 0f) == keepP) lo = mid; else hi = mid;
            }
            float s = 0.5f * (lo + hi);

            _positions.Add(Vector3.Lerp(_positions[vp], _positions[vq], s));
            _normals.Add(Vector3.Lerp(_normals[vp], _normals[vq], s).normalized);
            _uv1.Add(Vector2.Lerp(_uv1[vp], _uv1[vq], s));
            _uv0.Add(Vector2.Lerp(_uv0[vp], _uv0[vq], s));
            index = _positions.Count - 1;
            _crossing[key] = index;
            return index;
        }

        void AddTriangle(int a, int b, int c)
        {
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }

        public void Write(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uv0);
            mesh.SetUVs(1, _uv1);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}
