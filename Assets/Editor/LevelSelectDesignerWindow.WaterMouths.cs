using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Water mouths — where the generated waters open into one another, recorded for the lines drawn
/// on them. Every pool's mouths become <see cref="RiverWaterMouth"/> records on the designer data,
/// and every water surface is told on UV3 which records are its own. See RiverWaterMouth for why
/// the shore cannot simply be baked into the faces.
///
/// Run as a pass after the waters are built, because a river's water and its pool's are built at
/// different times and neither knows the other's record number until both exist.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // How far a river's water may end from a pool mouth's butt line and still be taken as the
    // river that mouth opens to. Both are built off the same trimmed ring, so they agree to
    // millimetres; this only has to tell one mouth from the next.
    private const float WaterMouthMatch = 0.25f;

    private void RefreshWaterMouths()
    {
        if (_data == null) return;

        var records = new List<RiverWaterMouth>();

        foreach (var record in FindObjectsOfType<RiverPoolMesh>())
        {
            if (string.IsNullOrEmpty(record.waterMeshAssetName)) continue;
            var water = record.transform.Find(record.waterMeshAssetName);
            var mesh  = water != null ? water.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh == null) continue;

            var pool    = PoolForRecord(record);
            var path    = pool != null ? PoolPath(pool) : PathForRiver(record.riverName);
            var profile = RunProfileFor(path, record.riverName);

            var mouths = RiverMeshBuilder.PoolWaterMouths(
                profile, _data.waterLevel, record.poolRadius, record.islandRadius, MeshEdge,
                ToPoolMouths(record.mouths, pool), record.floorDepth, out float outerShore);

            int first = records.Count;
            if (first + mouths.Count > RiverWaterMouth.MaxRecords)
            {
                Debug.LogWarning($"[WaterMouths] More than {RiverWaterMouth.MaxRecords} mouths in " +
                                 $"the world — '{record.meshAssetName}' and any after it draw plain rings.");
                mouths.Clear();
            }

            Vector3 centre = water.position;
            foreach (var m in mouths)
            {
                Vector3 dir = Flat(water.TransformDirection(m.dir)).normalized;
                Vector3 ka  = water.TransformPoint(m.cornerA);
                Vector3 kb  = water.TransformPoint(m.cornerB);

                records.Add(new RiverWaterMouth
                {
                    a = new Vector4(centre.x, centre.z, dir.x, dir.z),
                    b = new Vector4(ka.x, ka.z, kb.x, kb.z),
                    c = new Vector4(outerShore, m.butt, RiverWaterMouth.PoolKind, 0f),
                });

                Debug.Log($"[WaterMouths] pool '{record.meshAssetName}' record {records.Count - 1} " +
                          $"centre=({centre.x:F2},{centre.z:F2}) R={outerShore:F3} " +
                          $"dir=({dir.x:F3},{dir.z:F3}) cornerA=({ka.x:F2},{ka.z:F2}) " +
                          $"cornerB=({kb.x:F2},{kb.z:F2}) butt={m.butt:F3} shore={m.shore:F3} " +
                          $"cornerGap={Vector3.Distance(Flat(ka), Flat(kb)):F3}");
            }

            WriteMouthData(mesh, new Vector4(mouths.Count > 0 ? first + 1 : 0, mouths.Count, 0f, 0f));
        }

        var runs = FindObjectsOfType<RiverRunMesh>();

        // Every branch's junction, gathered under the river it leaves — that river's water is the
        // one whose bank opens, so the records are its own.
        var junctions = new Dictionary<RiverRunMesh, List<RiverWaterMouth>>();
        foreach (var branch in runs)
        {
            if (!JunctionMouth(branch, runs, out var host, out var junction)) continue;
            if (!junctions.TryGetValue(host, out var list)) junctions[host] = list = new List<RiverWaterMouth>();
            list.Add(junction);
        }

        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.waterMeshAssetName)) continue;
            var water = run.transform.Find(run.waterMeshAssetName);
            var mesh  = water != null ? water.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh == null) continue;

            int atStart = -1, atEnd = -1;
            if ((run.poolAtStart || run.poolAtEnd) &&
                WaterEnds(mesh, water, out Vector3 start, out Vector3 end))
            {
                if (run.poolAtStart) atStart = NearestMouth(records, start, out _, run.name, "start");
                if (run.poolAtEnd)   atEnd   = NearestMouth(records, end,   out _, run.name, "end");
            }

            int firstJunction = 0, junctionCount = 0;
            if (junctions.TryGetValue(run, out var opens))
            {
                if (records.Count + opens.Count > RiverWaterMouth.MaxRecords)
                {
                    Debug.LogWarning($"[WaterMouths] More than {RiverWaterMouth.MaxRecords} mouths in " +
                                     $"the world — '{run.name}' draws its banks straight across its junctions.");
                }
                else
                {
                    firstJunction = records.Count + 1;
                    junctionCount = opens.Count;
                    records.AddRange(opens);
                    Debug.Log($"[WaterMouths] run '{run.name}' opens {opens.Count} junction(s), " +
                              $"records {firstJunction - 1}..{records.Count - 1}");
                }
            }

            WriteMouthData(mesh, new Vector4(atStart + 1, atEnd + 1, firstJunction, junctionCount));
        }

        Undo.RecordObject(_data, "Refresh Water Mouths");
        _data.waterMouths = records;
        EditorUtility.SetDirty(_data);

        // The UV3 above is already written into the mesh assets, so the records it points at
        // have to be on the asset too, not only on this window's clone: whenever the window is
        // not previewing (closed, or just after a script reload) the pump pushes the asset's
        // records, and a mesh pointing past the end of those reads zeros.
        if (_sourceData != null && _sourceData != _data)
        {
            Undo.RecordObject(_sourceData, "Refresh Water Mouths");
            _sourceData.waterMouths = new List<RiverWaterMouth>(records);
            EditorUtility.SetDirty(_sourceData);
        }
        _data.ApplyAesthetics();

        Debug.Log($"[WaterMouths] {records.Count} mouth record(s) written.");
    }

    /// <summary>The same UV3 on every vertex, so it cannot change across a face.</summary>
    private static void WriteMouthData(Mesh mesh, Vector4 value)
    {
        var uv3 = new List<Vector4>(mesh.vertexCount);
        for (int i = 0; i < mesh.vertexCount; i++) uv3.Add(value);
        mesh.SetUVs(3, uv3);
        EditorUtility.SetDirty(mesh);
    }

    /// <summary>
    /// The middle of a river water's first and last rows, in world space: the corners lying
    /// furthest back and furthest on down the channel (UV2.x), averaged.
    /// </summary>
    private static bool WaterEnds(Mesh mesh, Transform water, out Vector3 start, out Vector3 end)
    {
        start = end = Vector3.zero;

        var flows = new List<Vector4>();
        mesh.GetUVs(2, flows);
        var verts = mesh.vertices;
        if (flows.Count != verts.Length || verts.Length == 0) return false;

        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var f in flows) { lo = Mathf.Min(lo, f.x); hi = Mathf.Max(hi, f.x); }

        // Each corner counted once: the water's faces do not share corners, and one bank's corner
        // sits in more of the end faces than the other's, which would pull the middle toward it.
        var lows  = new HashSet<Vector3Int>();
        var highs = new HashSet<Vector3Int>();
        Vector3 sumLo = Vector3.zero, sumHi = Vector3.zero;
        for (int i = 0; i < verts.Length; i++)
        {
            var key = Vector3Int.RoundToInt(verts[i] * 1000f);
            if (flows[i].x <= lo + 1e-3f && lows.Add(key))  sumLo += verts[i];
            if (flows[i].x >= hi - 1e-3f && highs.Add(key)) sumHi += verts[i];
        }
        if (lows.Count == 0 || highs.Count == 0) return false;

        start = water.TransformPoint(sumLo / lows.Count);
        end   = water.TransformPoint(sumHi / highs.Count);
        return true;
    }

    /// <summary>
    /// The pool mouth whose butt line passes through <paramref name="point"/> — the middle of
    /// where a river's water ends — or -1 where none is near enough.
    /// </summary>
    private static int NearestMouth(List<RiverWaterMouth> records, Vector3 point,
                                    out float distance, string runName, string which)
    {
        int best = -1;
        distance = float.MaxValue;

        for (int i = 0; i < records.Count; i++)
        {
            var     r      = records[i];
            if (r.c.z != RiverWaterMouth.PoolKind) continue;
            Vector2 centre = new Vector2(r.a.x, r.a.y);
            Vector2 dir    = new Vector2(r.a.z, r.a.w);
            Vector2 mid    = (new Vector2(r.b.x, r.b.y) + new Vector2(r.b.z, r.b.w)) * 0.5f;

            // The butt line's middle: on the river's centreline, butt metres out from the centre.
            Vector2 butt = mid + dir * (r.c.y - Vector2.Dot(mid - centre, dir));
            float   d    = Vector2.Distance(butt, new Vector2(point.x, point.z));
            if (d < distance) { distance = d; best = i; }
        }

        Debug.Log($"[WaterMouths] run '{runName}' {which} at ({point.x:F2},{point.z:F2}) -> " +
                  (best >= 0 ? $"record {best}, {distance:F3} m off its butt line" : "no pool mouth") +
                  (best >= 0 && distance > WaterMouthMatch ? " — TOO FAR, left unmatched" : ""));

        return distance <= WaterMouthMatch ? best : -1;
    }

    /// <summary>
    /// The junction a branch opens in the river it leaves, as a record against that river — or
    /// false where the branch does not start on another river's water.
    ///
    /// Read off the two waters as built rather than worked out again: the branch's heading, centre
    /// line and along from its first corners (its lead-in is straight), and the river's near bank
    /// and how wide its water really is from the corner of its ribbon nearest the mouth. The
    /// mouth's corners are where the branch's waterlines, run back, cross the river's near one.
    /// </summary>
    private bool JunctionMouth(RiverRunMesh branch, RiverRunMesh[] runs,
                               out RiverRunMesh host, out RiverWaterMouth junction)
    {
        host     = null;
        junction = default;
        if (string.IsNullOrEmpty(branch.joinRiver) || branch.joinAcross.sqrMagnitude < 0.5f) return false;
        if (!WaterOf(branch, out var bWater, out var bMesh)) return false;

        var profile = RunProfileForRiver(branch.joinRiver);
        if (profile == null) return false;
        float ribbon = RiverMeshBuilder.WaterRibbonHalfWidth(profile, _data.waterLevel);

        Vector3 J    = Flat(branch.transform.TransformPoint(branch.joinPoint));
        Vector3 N    = Flat(branch.transform.TransformDirection(branch.joinAcross)).normalized;
        Vector3 edge = J + N * ribbon;

        // The river's water: the run of that name with a corner nearest the edge the branch was cut on.
        float   best    = float.MaxValue;
        Vector4 nearUv1 = default, nearUv2 = default;
        foreach (var run in runs)
        {
            if (run == branch || run.riverName != branch.joinRiver) continue;
            if (!WaterOf(run, out var water, out var mesh)) continue;

            var verts = mesh.vertices;
            var uv1   = new List<Vector4>();
            var uv2   = new List<Vector4>();
            mesh.GetUVs(1, uv1);
            mesh.GetUVs(2, uv2);
            if (uv1.Count != verts.Length || uv2.Count != verts.Length) continue;

            for (int i = 0; i < verts.Length; i++)
            {
                float d = (Flat(water.TransformPoint(verts[i])) - edge).sqrMagnitude;
                if (d < best) { best = d; host = run; nearUv1 = uv1[i]; nearUv2 = uv2[i]; }
            }
        }
        if (host == null) return false;

        // UV1.x is metres to the LEFT waterline and UV1.y to the right (RunEdgeData), and UV2.y is
        // signed across, right positive — so a corner on the right of the river means the branch
        // leaves its right bank. Its own waterline is that far in from the ribbon's edge.
        bool  nearIsY = nearUv2.y > 0f;
        float shore   = (nearIsY ? nearUv1.y : nearUv1.x) + Mathf.Abs(nearUv2.y);

        // The branch: its first corner on the left bank, and the next one on down it.
        var bVerts = bMesh.vertices;
        var bUv1   = new List<Vector4>();
        var bUv2   = new List<Vector4>();
        bMesh.GetUVs(1, bUv1);
        bMesh.GetUVs(2, bUv2);
        if (bUv1.Count != bVerts.Length || bUv2.Count != bVerts.Length) return false;

        int l0 = -1;
        for (int i = 0; i < bVerts.Length; i++)
            if (bUv2[i].y < 0f && (l0 < 0 || bUv2[i].x < bUv2[l0].x)) l0 = i;
        if (l0 < 0) return false;

        int l1 = -1;
        for (int i = 0; i < bVerts.Length; i++)
            if (bUv2[i].y < 0f && bUv2[i].x > bUv2[l0].x + 0.05f && (l1 < 0 || bUv2[i].x < bUv2[l1].x)) l1 = i;
        if (l1 < 0) return false;

        Vector3 L0     = Flat(bWater.TransformPoint(bVerts[l0]));
        Vector3 D      = (Flat(bWater.TransformPoint(bVerts[l1])) - L0).normalized;
        Vector3 across = new Vector3(D.z, 0f, -D.x);   // Cross(up, D): the branch's right
        Vector3 C      = L0 + across * Mathf.Abs(bUv2[l0].y);
        float   alongC = bUv2[l0].x;
        float   hb     = (bUv1[l0].x + bUv1[l0].y) * 0.5f;

        float lean = Vector3.Dot(D, N);
        if (lean < 0.1f)
        {
            Debug.LogWarning($"[WaterMouths] junction '{branch.name}' heads along '{host.name}' " +
                             $"rather than out of it (dot {lean:F3}) — left unrecorded.");
            return false;
        }

        Vector3 Q  = J + N * shore;
        Vector3 ka = C - across * hb;
        Vector3 kb = C + across * hb;
        ka += D * (Vector3.Dot(Q - ka, N) / lean);
        kb += D * (Vector3.Dot(Q - kb, N) / lean);

        float cut    = Vector3.Dot(J, N) + ribbon;
        float radius = Vector3.Distance(ka, kb) * 0.5f + 2f * ribbon + 1f;

        junction = new RiverWaterMouth
        {
            a = new Vector4(ka.x, ka.z, kb.x, kb.z),
            b = new Vector4(D.x, D.z, C.x, C.z),
            c = new Vector4(hb, alongC, RiverWaterMouth.JunctionKind, nearIsY ? 1f : 0f),
            d = new Vector4(N.x, N.z, cut, radius),
        };

        Debug.Log($"[WaterMouths] junction '{branch.name}' off '{host.name}' " +
                  $"cornerA=({ka.x:F2},{ka.z:F2}) cornerB=({kb.x:F2},{kb.z:F2}) " +
                  $"dir=({D.x:F3},{D.z:F3}) lean={lean:F3} branchShore={hb:F3} hostShore={shore:F3} " +
                  $"ribbon={ribbon:F3} near={(nearIsY ? "UV1.y" : "UV1.x")} hostCornerOff={Mathf.Sqrt(best):F3} " +
                  $"branchStartOffCut={Vector3.Dot(L0, N) - cut:F3} alongC={alongC:F3}");
        return true;
    }

    private static bool WaterOf(RiverRunMesh run, out Transform water, out Mesh mesh)
    {
        water = null;
        mesh  = null;
        if (run == null || string.IsNullOrEmpty(run.waterMeshAssetName)) return false;
        water = run.transform.Find(run.waterMeshAssetName);
        mesh  = water != null ? water.GetComponent<MeshFilter>()?.sharedMesh : null;
        return mesh != null;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
