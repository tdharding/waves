using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>One named bit of a generated piece, and the point on it the name is drawn at.</summary>
[Serializable]
public struct PartLabel
{
    public string  name;
    public Vector3 position;   // in the mesh's own space
}

/// <summary>
/// The names of the bits a generated piece is built from — Base, Upper Ramp, Stem, Orb — as the
/// Level Select Designer's shape controls call them, so the designer overlay can write each name on
/// its bit when the piece is selected.
///
/// A builder working in its own lists calls <see cref="Mark"/> after adding each bit: it names
/// every triangle added to that list since the last Mark. A builder working through the river
/// mesh plumbing sets <c>NextLabel</c> instead, the same way it sets the stone kind. Either way the
/// names reach the finished mesh in <see cref="Attach"/>, where every connected run of triangles
/// with one name becomes one label — so two columns get one each, and a column's sides and cap
/// share one.
///
/// Editor only: in a player every call does nothing.
/// </summary>
public static class PartLabels
{
#if UNITY_EDITOR
    struct Span
    {
        public string        name;
        public List<Vector3> verts;
        public List<int>     tris;
        public int           from, to;
    }

    static readonly List<Span>                 _pending = new List<Span>();
    static readonly Dictionary<List<int>, int> _cursor  = new Dictionary<List<int>, int>();
    static readonly ConditionalWeakTable<Mesh, List<PartLabel>> _byMesh =
        new ConditionalWeakTable<Mesh, List<PartLabel>>();

    // Lists that were built from but never made into a mesh — a preview, say — are let go of
    // once this many spans are waiting.
    const int MaxPending = 4096;
#endif

    /// <summary>
    /// Names every triangle added to <paramref name="tris"/> since the last Mark on it (or since
    /// it was empty). Null names them nothing — for a stretch that is not a bit of its own, such
    /// as the faces already in a list another piece is appended to.
    /// </summary>
    public static void Mark(string name, List<Vector3> verts, List<int> tris)
    {
#if UNITY_EDITOR
        if (tris == null) return;
        _cursor.TryGetValue(tris, out int from);
        if (from > tris.Count) from = 0;   // the list was cleared and refilled
        _cursor[tris] = tris.Count;
        if (string.IsNullOrEmpty(name) || verts == null || tris.Count <= from) return;

        if (_pending.Count >= MaxPending) _pending.RemoveRange(0, MaxPending / 2);
        _pending.Add(new Span { name = name, verts = verts, tris = tris, from = from, to = tris.Count });
#endif
    }

    /// <summary>
    /// The name of each face of a mesh about to be made from lists that were Marked — found by the
    /// mesh's vertex and index counts, which are the lists' own, and by the corners at each end of
    /// a Marked stretch being where the mesh has them, so two pieces built alike never swap names.
    /// Null when nothing was Marked. The spans found are used up.
    /// </summary>
    public static string[] FaceLabels(Vector3[] vertices, int[] indices)
    {
#if UNITY_EDITOR
        string[] labels = null;
        for (int i = 0; i < _pending.Count; i++)
        {
            var s = _pending[i];
            if (s.verts.Count != vertices.Length || s.tris.Count != indices.Length) continue;
            if (s.verts[s.tris[s.from]]  != vertices[indices[s.from]] ||
                s.verts[s.tris[s.to - 1]] != vertices[indices[s.to - 1]]) continue;

            labels ??= new string[indices.Length / 3];
            for (int t = s.from / 3; t < s.to / 3 && t < labels.Length; t++) labels[t] = s.name;
            _cursor.Remove(s.tris);
            _pending.RemoveAt(i--);
        }
        return labels;
#else
        return null;
#endif
    }

    /// <summary>
    /// Hangs the labels on <paramref name="mesh"/>, worked out from one name per face
    /// (<paramref name="faceLabels"/>, null where a face is no named bit). Faces with one name that
    /// share a corner are one bit; each bit's label sits on its face nearest the bit's middle.
    /// </summary>
    public static void Attach(Mesh mesh, IList<string> faceLabels, IList<Vector3> verts, IList<int> tris)
    {
#if UNITY_EDITOR
        if (mesh == null) return;
        var labels = Find(faceLabels, verts, tris);
        _byMesh.Remove(mesh);
        if (labels != null) _byMesh.Add(mesh, labels);
#endif
    }

    /// <summary>The labels <see cref="Attach"/> hangs on a mesh, worked out from its faces. Null when none is named.</summary>
    public static List<PartLabel> Find(IList<string> faceLabels, IList<Vector3> verts, IList<int> tris)
    {
        if (faceLabels == null || verts == null || tris == null) return null;
        int faces = Mathf.Min(faceLabels.Count, tris.Count / 3);

        // Union-find over the named faces, joined through corners at the same place.
        var parent = new int[faces];
        for (int f = 0; f < faces; f++) parent[f] = f;
        int Root(int f) { while (parent[f] != f) f = parent[f] = parent[parent[f]]; return f; }

        var firstAt = new Dictionary<(string, Vector3Int), int>();
        bool any = false;
        for (int f = 0; f < faces; f++)
        {
            string name = faceLabels[f];
            if (string.IsNullOrEmpty(name)) continue;
            any = true;
            for (int e = 0; e < 3; e++)
            {
                Vector3    p   = verts[tris[f * 3 + e]];
                var        key = (name, new Vector3Int(Mathf.RoundToInt(p.x * 1000f),
                                                       Mathf.RoundToInt(p.y * 1000f),
                                                       Mathf.RoundToInt(p.z * 1000f)));
                if (firstAt.TryGetValue(key, out int other)) parent[Root(f)] = Root(other);
                else firstAt[key] = f;
            }
        }
        if (!any) return null;

        // Each bit's middle, weighted by area, then the face of it nearest that middle.
        var sum  = new Dictionary<int, Vector4>();
        for (int f = 0; f < faces; f++)
        {
            if (string.IsNullOrEmpty(faceLabels[f])) continue;
            Vector3 a = verts[tris[f * 3]], b = verts[tris[f * 3 + 1]], c = verts[tris[f * 3 + 2]];
            float   w = Vector3.Cross(b - a, c - a).magnitude + 1e-9f;
            Vector3 m = (a + b + c) / 3f * w;
            int     r = Root(f);
            sum.TryGetValue(r, out var acc);
            sum[r] = acc + new Vector4(m.x, m.y, m.z, w);
        }

        var best = new Dictionary<int, (float d, Vector3 p)>();
        for (int f = 0; f < faces; f++)
        {
            if (string.IsNullOrEmpty(faceLabels[f])) continue;
            int     r      = Root(f);
            Vector4 acc    = sum[r];
            Vector3 middle = new Vector3(acc.x, acc.y, acc.z) / acc.w;
            Vector3 centre = (verts[tris[f * 3]] + verts[tris[f * 3 + 1]] + verts[tris[f * 3 + 2]]) / 3f;
            float   d      = (centre - middle).sqrMagnitude;
            if (!best.TryGetValue(r, out var held) || d < held.d) best[r] = (d, centre);
        }

        var labels = new List<PartLabel>(best.Count);
        foreach (var pair in best)
        {
            int f = pair.Key;   // a root is one of its own faces
            labels.Add(new PartLabel { name = faceLabels[f], position = pair.Value.p });
        }
        return labels;
    }

    /// <summary>The labels a freshly built mesh was given; null when it has none.</summary>
    public static List<PartLabel> Of(Mesh mesh)
    {
#if UNITY_EDITOR
        return mesh != null && _byMesh.TryGetValue(mesh, out var labels) ? labels : null;
#else
        return null;
#endif
    }
}
