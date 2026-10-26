using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Rim walls — a wall standing up off a river's rim (see
/// <see cref="LevelSelectDesignerData.DesignerRimWall"/>), or right round a pool's (see
/// <see cref="LevelSelectDesignerData.PoolRimWall"/>). On a river it is ticked on a node in its
/// panel and runs from a ticked node to the next ticked node of the river on the same side,
/// built from the settings of the node it starts at; Run to End of River on the last ticked node
/// carries it on to wherever the run stops, and on across the collar onto a pool's rim.
///
/// A river's rim walls are built into the same piece under each run as its walkways, bridges and
/// archways, by <see cref="RiverMeshBuilder.BuildWalkways"/>. A pool's is a piece of its own
/// under the pool, by <see cref="RiverMeshBuilder.BuildPoolRimWall"/>. Both run straight through
/// anything else standing on the rim.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // WHERE ONE CAN GO
    // ─────────────────────────────────────────────

    /// <summary>The same nodes a rim node can stand on — plain nodes partway along one river.</summary>
    private bool CanHoldRimWall(string nodeId, out string why)
    {
        bool ok = CanHoldRimNode(nodeId, out why);
        if (!ok && why != null) why = why.Replace("Rim nodes", "Rim walls");
        return ok;
    }

    /// <summary>One stretch of rim wall on a path: node indices it runs between, its side, its settings.</summary>
    private struct RimWallStretch
    {
        public int from;
        public int to;
        public int side;   // +1 right, -1 left, looking down the path the way it was drawn
        public LevelSelectDesignerData.DesignerRimWall settings;
    }

    /// <summary>
    /// Every stretch of rim wall along a path's nodes. A stretch runs while each next node is
    /// ticked on the same side, and breaks where a node's settings differ from the node the
    /// stretch started at — the next stretch starts there. The last node ticked on a side, when
    /// it has Run to End of River on, carries its wall on to the path's last node.
    /// </summary>
    private List<RimWallStretch> RimWallStretches(IList<string> nodeIds)
    {
        var stretches = new List<RimWallStretch>();
        if (nodeIds == null || nodeIds.Count < 2 || _data.rimWalls == null || _data.rimWalls.Count == 0)
            return stretches;

        int n = nodeIds.Count;
        var held = new LevelSelectDesignerData.DesignerRimWall[n];
        for (int i = 0; i < n; i++)
        {
            var w = _data.RimWallAt(nodeIds[i]);
            held[i] = w != null && w.enabled && CanHoldRimWall(nodeIds[i], out _) ? w : null;
        }

        foreach (int side in new[] { 1, -1 })
        {
            // Run to End of River: every node past the last one ticked on this side stands in
            // as that one, so the stretch carries on to the end of the path.
            var on = held;
            int lastOn = n - 1;
            while (lastOn >= 0 && (held[lastOn] == null || !held[lastOn].Covers(side))) lastOn--;
            if (lastOn >= 0 && lastOn < n - 1 && held[lastOn].toEnd)
            {
                on = (LevelSelectDesignerData.DesignerRimWall[])held.Clone();
                for (int i = lastOn + 1; i < n; i++) on[i] = held[lastOn];
            }

            bool On(int i) => on[i] != null && on[i].Covers(side);

            int at = 0;
            while (at < n - 1)
            {
                if (!On(at) || !On(at + 1)) { at++; continue; }

                var settings = on[at];
                int end = at + 1;
                while (end < n - 1 && On(end + 1) && on[end].SameBuild(settings)) end++;

                stretches.Add(new RimWallStretch { from = at, to = end, side = side, settings = settings });
                at = end;
            }
        }
        return stretches;
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>The Rim Wall tick for the selected node, and its side, height and thickness.</summary>
    private void DrawSelectedRimWallProps()
    {
        if (string.IsNullOrEmpty(_selectedNodeId)) return;

        var path = _data.paths.Find(p => p.nodeIds.Contains(_selectedNodeId));
        if (path == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Rim Wall", EditorStyles.boldLabel);

        // The end of a river: a tick per river ending here, for that river alone.
        var ending = _data.paths.Where(p => p != null && p.nodeIds.Count >= 2 &&
                                            p.nodeIds[p.nodeIds.Count - 1] == _selectedNodeId).ToList();
        if (ending.Count > 0)
        {
            foreach (var end in ending) DrawRimWallEndTick(end, ending.Count > 1);
            return;
        }

        if (!CanHoldRimWall(_selectedNodeId, out string why))
        {
            EditorGUILayout.LabelField(why, EditorStyles.miniLabel);
            return;
        }

        var wall = _data.RimWallAt(_selectedNodeId);

        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.Toggle(
            new GUIContent("Rim Wall", "A wall standing up off the run's rim, from this node to the next " +
                                       "node of the river with Rim Wall ticked on the same side."),
            wall != null && wall.enabled);

        var   side      = wall?.side ?? LevelSelectDesignerData.WalkwaySide.Both;
        float height    = wall?.height ?? 2f;
        float thickness = wall?.thickness ?? 1f;
        bool  occlusion = wall?.occlusion ?? false;
        var   occlusionStyle = wall?.occlusionStyle ?? OcclusionStyle.WholeObject;
        bool  toEnd     = wall?.toEnd ?? false;

        if (on && wall != null)
        {
            side = (LevelSelectDesignerData.WalkwaySide)EditorGUILayout.EnumPopup(
                new GUIContent("Side", "Which rim it stands on, looking down the path the way it was " +
                                       "drawn — or both."), side);
            height = Mathf.Max(RiverMeshBuilder.RimWallMinHeight, EditorGUILayout.FloatField(
                new GUIContent("Height", "How far its top stands above the rim."), height));
            thickness = EditorGUILayout.Slider(
                new GUIContent("Thickness", "How much of the rim's width it takes, in the middle of the " +
                                            "rim — 1 is the whole rim."),
                thickness, RiverMeshBuilder.RimWallMinThickness, 1f);
            OcclusionField(ref occlusion, ref occlusionStyle);

            // Only the last node ticked on the river can carry it on to the end.
            if (!LaterRimWallOnPath(path, _selectedNodeId))
                toEnd = EditorGUILayout.Toggle(
                    new GUIContent("Run to End of River",
                        "Carries this wall on past this node to wherever the run stops at the end of " +
                        "the river — and on across onto a pool's rim, up to the pool's own wall."),
                    toEnd);

            EditorGUILayout.LabelField("Runs on to the next node with Rim Wall ticked. A stretch takes " +
                                       "the settings of the node it starts at.",
                                       EditorStyles.wordWrappedMiniLabel);
        }

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Edit Rim Wall");
        _data.rimWalls ??= new List<LevelSelectDesignerData.DesignerRimWall>();
        if (wall == null)
        {
            wall = new LevelSelectDesignerData.DesignerRimWall { nodeId = _selectedNodeId };
            _data.rimWalls.Add(wall);
        }
        wall.enabled   = on;
        wall.side      = side;
        wall.height    = height;
        wall.thickness = thickness;
        wall.occlusion = occlusion;
        wall.occlusionStyle = occlusionStyle;
        wall.toEnd     = toEnd;

        MarkDirty();
        string pathId = path.pathId;
        RebuildAfterTowerEdit("Walkway:" + pathId, () => RebuildWalkwaysOnPath(pathId, false));
        Repaint();
    }

    /// <summary>
    /// The Rim Wall tick on the end of one river. It belongs to that river only, never to
    /// another river through the same node: it is the Run to End of River tick of the last node
    /// before the end with Rim Wall ticked, which the wall runs on from with that node's settings.
    /// </summary>
    private void DrawRimWallEndTick(LevelSelectDesignerData.DesignerPath path, bool nameRiver)
    {
        string label = "Rim Wall" + (nameRiver ? $" ({path.riverName})" : "");

        LevelSelectDesignerData.DesignerRimWall from = null;
        for (int i = path.nodeIds.Count - 2; i >= 0 && from == null; i--)
        {
            var w = _data.RimWallAt(path.nodeIds[i]);
            if (w != null && w.enabled && CanHoldRimWall(path.nodeIds[i], out _)) from = w;
        }

        if (from == null)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle(new GUIContent(label), false);
            EditorGUILayout.LabelField("Tick Rim Wall on a node before this one first — it runs on to " +
                                       "here from there.", EditorStyles.wordWrappedMiniLabel);
            return;
        }

        bool on = EditorGUILayout.Toggle(
            new GUIContent(label,
                "Carries the wall on to the end of this river, to wherever the run stops — and on " +
                "across onto a pool's rim — with the settings of the last node before it with Rim " +
                "Wall ticked. Only this river: any other river through this node is left alone. The " +
                "same as Run to End of River on that node."),
            from.toEnd);
        if (on == from.toEnd) return;

        Undo.RecordObject(_data, "Edit Rim Wall");
        from.toEnd = on;
        MarkDirty();
        string pathId = path.pathId;
        RebuildAfterTowerEdit("Walkway:" + pathId, () => RebuildWalkwaysOnPath(pathId, false));
        Repaint();
    }

    /// <summary>Whether any node after this one on the path has Rim Wall ticked.</summary>
    private bool LaterRimWallOnPath(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        for (int i = path.nodeIds.IndexOf(nodeId) + 1; i < path.nodeIds.Count; i++)
        {
            var w = _data.RimWallAt(path.nodeIds[i]);
            if (w != null && w.enabled && CanHoldRimWall(path.nodeIds[i], out _)) return true;
        }
        return false;
    }

    /// <summary>The Rim Wall tick on a pool, and its height and thickness.</summary>
    private void DrawPoolRimWallProps(LevelSelectDesignerData.DesignerPool pool)
    {
        pool.rimWall ??= new LevelSelectDesignerData.PoolRimWall();
        var wall = pool.rimWall;

        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.Toggle(
            new GUIContent("Rim Wall", "A wall standing up off the pool's rim, all the way round, open " +
                                       "where each river comes in."),
            wall.enabled);

        float height    = wall.height;
        float thickness = wall.thickness;
        bool  occlusion = wall.occlusion;
        var   occlusionStyle = wall.occlusionStyle;
        float head      = wall.doorHeadRadius;
        float body      = wall.doorBodyHeight;
        float neck      = wall.doorNeckWidth;
        float doorTop   = wall.doorHeight;
        float shoulder  = wall.doorShoulderWidth;
        float round     = wall.doorShoulderRoundness;
        var   doorways  = new List<string>(wall.doorways ?? new List<string>());
        if (on)
        {
            height = Mathf.Max(RiverMeshBuilder.RimWallMinHeight, EditorGUILayout.FloatField(
                new GUIContent("Height", "How far its top stands above the rim."), height));
            thickness = EditorGUILayout.Slider(
                new GUIContent("Thickness", "How much of the rim's width it takes, in the middle of the " +
                                            "rim — 1 is the whole rim. Each opening is cut along the same " +
                                            "share of the arriving river's rim."),
                thickness, RiverMeshBuilder.RimWallMinThickness, 1f);
            OcclusionField(ref occlusion, ref occlusionStyle);

            // A tick per river mouth: closing it with the wall, a keyhole cut through.
            EditorGUILayout.Space(2);
            var arrivals = _data.PathsAtPool(pool);
            Vector3 centre = _data.NodeWorldPosition(pool.nodeId);
            foreach (var (path, neighbourId) in arrivals)
            {
                bool   shared = arrivals.Count(a => a.path.riverName == path.riverName) > 1;
                string label  = $"Doorway — {path.riverName}" +
                                (shared ? $" ({CompassWord(_data.NodeWorldPosition(neighbourId) - centre)})" : "");
                bool   had    = doorways.Contains(neighbourId);
                bool   has    = EditorGUILayout.Toggle(
                    new GUIContent(label, "Carries the wall across where this river comes in, with a " +
                                          "keyhole cut through it standing on the channel."), had);
                if (has && !had)  doorways.Add(neighbourId);
                if (!has && had)  doorways.Remove(neighbourId);
            }

            if (arrivals.Any(a => doorways.Contains(a.neighbourId)))
            {
                EditorGUI.indentLevel++;
                head = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                    new GUIContent("Head Radius", "Radius of the keyhole's round head."), head));
                body = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Body Height", "Height of the body, up from the rim top: its sides run " +
                                                  "in from the river's width at the foot to the neck " +
                                                  "here, then curve up into the head."), body));
                neck = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                    new GUIContent("Neck Width", "Width across the neck, where the body meets the head."), neck));
                shoulder = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Shoulder Width", "Width across the shoulders, at the top of the body. " +
                                                     "Wider than the neck, the side runs up from the foot " +
                                                     "to this width and curves round and in to the neck. " +
                                                     "0 leaves the sides running straight in to the neck."),
                    shoulder));
                using (new EditorGUI.DisabledScope(shoulder <= neck))
                    round = EditorGUILayout.Slider(
                        new GUIContent("Shoulder Roundness", "How round the shoulders are: 0 a flat shoulder " +
                                                             "meeting the side at a corner, 1 a full curve from " +
                                                             "the side round to the neck."),
                        round, 0f, 1f);
                doorTop = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                    new GUIContent("Total Height", "Top of the head above the rim top. Taller than the " +
                                                   "wall cuts through its top."), doorTop));
                EditorGUILayout.LabelField("The foot is the river's channel width. Every doorway on " +
                                           "this pool is cut to the same keyhole.",
                                           EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }
        }

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Edit Pool Rim Wall");
        wall.enabled        = on;
        wall.height         = height;
        wall.thickness      = thickness;
        wall.occlusion      = occlusion;
        wall.occlusionStyle = occlusionStyle;
        wall.doorways       = doorways;
        wall.doorHeadRadius = head;
        wall.doorBodyHeight = body;
        wall.doorNeckWidth  = neck;
        wall.doorHeight     = doorTop;
        wall.doorShoulderWidth     = shoulder;
        wall.doorShoulderRoundness = round;

        MarkDirty();
        RebuildAfterTowerEdit("PoolRimWall:" + pool.nodeId, () => RebuildPoolRimWall(pool));
        Repaint();
    }

    // Which way a direction points on the map, as a compass word — +z north, +x east.
    private static string CompassWord(Vector3 d)
    {
        string[] words = { "East", "North-East", "North", "North-West", "West", "South-West", "South", "South-East" };
        float a = Mathf.Repeat(Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg + 22.5f, 360f);
        return words[Mathf.Clamp((int)(a / 45f), 0, 7)];
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Each stretch of rim wall over the drawn rim, and each pool's wall round its rim with a gap
    /// where each river comes in. Drawn against the drawn path, which is close to the run but
    /// not on it.
    /// </summary>
    private void DrawRimWalls()
    {
        if (Event.current.type != EventType.Repaint) return;

        if (_data.rimWalls != null && _data.rimWalls.Count > 0)
            foreach (var path in _data.paths)
            {
                var stretches = RimWallStretches(path.nodeIds);
                if (stretches.Count == 0) continue;

                var   profile = _data.ProfileFor(path.riverName);
                float rim     = profile.rimWidth;

                var points = path.nodeIds.Select(id => _data.NodeWorldPosition(id)).ToList();
                TrimRimEndAtPool(points, path.nodeIds[0], 0, 1);
                TrimRimEndAtPool(points, path.nodeIds[points.Count - 1], points.Count - 1, points.Count - 2);

                foreach (var s in stretches)
                {
                    float inset    = rim * (1f - Mathf.Clamp(s.settings.thickness, RiverMeshBuilder.RimWallMinThickness, 1f)) * 0.5f;
                    bool  selected = false;
                    for (int i = s.from; i <= s.to; i++) selected |= path.nodeIds[i] == _selectedNodeId;

                    Handles.color = Faded(selected ? new Color(0.95f, 0.95f, 0.9f, 0.9f)
                                                   : new Color(0.3f, 0.3f, 0.3f, 0.85f));

                    for (int i = s.from; i < s.to; i++)
                    {
                        Vector3 a  = points[i];
                        Vector3 b  = points[i + 1];
                        Vector3 ra = DrawnRightAt(path, i)     * s.side;
                        Vector3 rb = DrawnRightAt(path, i + 1) * s.side;

                        // Over the rim, which a width override moves.
                        float oa = HalfOuterAtNode(profile, path.nodeIds[i])     - inset;
                        float ob = HalfOuterAtNode(profile, path.nodeIds[i + 1]) - inset;
                        float ia = oa - (rim - inset * 2f);
                        float ib = ob - (rim - inset * 2f);

                        Handles.DrawAAConvexPolygon(
                            (Vector3)WorldToCanvas(a + ra * ia),
                            (Vector3)WorldToCanvas(b + rb * ib),
                            (Vector3)WorldToCanvas(b + rb * ob),
                            (Vector3)WorldToCanvas(a + ra * oa));
                    }
                }
            }

        foreach (var pool in _data.pools)
        {
            if (pool?.rimWall == null || !pool.rimWall.enabled) continue;
            var node = _data.nodes.Find(n => n.id == pool.nodeId);
            if (node == null) continue;

            var   shape   = _data.PoolShapeFor(pool);
            float rim     = _data.ProfileFor(_data.PoolRiverName(pool)).rimWidth;
            float share   = Mathf.Clamp(pool.rimWall.thickness, RiverMeshBuilder.RimWallMinThickness, 1f);
            float inset   = rim * (1f - share) * 0.5f;
            float rIn     = shape.poolRadius + inset;
            float rOut    = shape.poolRadius + rim - inset;
            Vector3 centre = node.worldPosition;

            // Where each river comes in, as the way out to it and how far either side it reaches.
            var mouths = new List<(Vector3 dir, float reach)>();
            foreach (var (path, neighbourId) in _data.PathsAtPool(pool))
            {
                if (pool.rimWall.HasDoorway(neighbourId)) continue;   // the wall carries across it
                Vector3 outward = _data.NodeWorldPosition(neighbourId) - centre;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.0001f) continue;

                var arriving = _data.ProfileFor(path.riverName);
                mouths.Add((outward.normalized,
                            arriving.innerWidth * 0.5f + arriving.rimWidth - arriving.rimWidth * (1f - share) * 0.5f));
            }

            bool InGap(Vector3 at)
            {
                foreach (var (dir, reach) in mouths)
                {
                    Vector3 right = Vector3.Cross(Vector3.up, dir);
                    if (Vector3.Dot(at, dir) > 0f && Mathf.Abs(Vector3.Dot(at, right)) < reach) return true;
                }
                return false;
            }

            Handles.color = Faded(_selectedPoolNodeId == pool.nodeId ? new Color(0.95f, 0.95f, 0.9f, 0.9f)
                                                                      : new Color(0.3f, 0.3f, 0.3f, 0.85f));
            const int steps = 96;
            for (int k = 0; k < steps; k++)
            {
                float   a0 = 2f * Mathf.PI * k / steps, a1 = 2f * Mathf.PI * (k + 1) / steps;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                if (InGap((d0 + d1) * 0.5f * (rIn + rOut) * 0.5f)) continue;

                Handles.DrawAAConvexPolygon(
                    (Vector3)WorldToCanvas(centre + d0 * rIn),
                    (Vector3)WorldToCanvas(centre + d1 * rIn),
                    (Vector3)WorldToCanvas(centre + d1 * rOut),
                    (Vector3)WorldToCanvas(centre + d0 * rOut));
            }
        }
    }

    // ─────────────────────────────────────────────
    // BUILDING
    // ─────────────────────────────────────────────

    /// <summary>
    /// The rim walls along a run, as the builder takes them — in the run's own space. With
    /// <paramref name="occluded"/>, one entry per wall: a name for each ticked for Occlusion (the
    /// node its stretch starts at), null for the rest.
    /// </summary>
    private List<RiverMeshBuilder.RimWall> RimWallsFor(RiverRunMesh record, List<string> occluded = null,
        List<OcclusionStyle> occludedStyles = null)
    {
        var walls = new List<RiverMeshBuilder.RimWall>();
        if (record?.pathNodes == null || record.pathNodes.Count < 2) return walls;

        var ids = record.pathNodes.Select(n => n.nodeId).ToList();
        foreach (var s in RimWallStretches(ids))
        {
            walls.Add(new RiverMeshBuilder.RimWall
            {
                from      = record.pathNodes[s.from].position,
                to        = record.pathNodes[s.to].position,
                side      = s.side,
                height    = s.settings.height,
                thickness = s.settings.thickness,
            });
            occluded?.Add(s.settings.occlusion
                ? $"RimWall_{SanitiseAssetName(ids[s.from])}_{(s.side > 0 ? "R" : "L")}"
                : null);
            occludedStyles?.Add(s.settings.occlusionStyle);
        }
        return walls;
    }

    /// <summary>
    /// The pool the start (or <paramref name="end"/>) of a run meets, in the run's own space — so
    /// a rim wall reaching that end carries on onto the pool's rim. Not present where no pool is.
    /// </summary>
    private RiverMeshBuilder.RimWallPool RimWallPoolAt(RiverRunMesh record, bool end)
    {
        if (record?.pathNodes == null || record.pathNodes.Count == 0 ||
            !(end ? record.poolAtEnd : record.poolAtStart))
            return default;

        var pool = _data.PoolAt(record.pathNodes[end ? record.pathNodes.Count - 1 : 0].nodeId);
        if (pool == null) return default;

        Vector3 centre = WorldPosOfNode(pool.nodeId);
        centre.y = end ? record.poolRimYEnd : record.poolRimYStart;

        var shape = _data.PoolShapeFor(pool);
        return new RiverMeshBuilder.RimWallPool
        {
            present       = true,
            centre        = record.transform.InverseTransformPoint(centre),
            poolRadius    = shape.poolRadius,
            rimWidth      = PoolProfileFor(pool).rimWidth,
            wallThickness = pool.rimWall != null && pool.rimWall.enabled ? pool.rimWall.thickness : 0f,
        };
    }

    /// <summary>
    /// Builds the wall on a pool's rim, or takes it away when the pool has none. In the pool's
    /// own material, standing on it.
    /// </summary>
    private void RefreshPoolRimWall(RiverPoolMesh record, RiverProfile profile)
    {
        if (record == null || string.IsNullOrEmpty(record.meshAssetName)) return;

        string name     = $"RiverPoolRimWall_{record.meshAssetName.Replace("RiverPool_", string.Empty)}";
        var    existing = record.transform.Find(name);
        var    pool     = PoolForRecord(record);
        var    wall     = pool?.rimWall;

        if (wall == null || !wall.enabled)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            string asset = $"{GeneratedDir}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(asset) != null) AssetDatabase.DeleteAsset(asset);
            return;
        }

        var mesh = SaveGeneratedMesh(name, RiverMeshBuilder.BuildPoolRimWall(
            profile, record.poolRadius, ToPoolMouths(record.mouths, pool),
            wall.height, wall.thickness, MeshEdge, StoneShadingForBuild,
            DoorwaysAtMouths(record, pool),
            new RiverMeshBuilder.PoolDoorway
            {
                headRadius = wall.doorHeadRadius,
                bodyHeight = wall.doorBodyHeight,
                neckWidth  = wall.doorNeckWidth,
                height     = wall.doorHeight,
                shoulderWidth     = wall.doorShoulderWidth,
                shoulderRoundness = wall.doorShoulderRoundness,
            }));
        if (mesh == null) return;

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null) filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            EditorUtility.SetDirty(filter);
        }
        else
        {
            go = NewMeshChild(record.gameObject, name, mesh);
        }

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = RunMaterialFor(PoolPath(pool));
        EditorUtility.SetDirty(renderer);

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        ApplyOcclusion(go, wall.occlusion, wall.occlusionStyle);
    }

    /// <summary>
    /// Which of a generated pool's mouths have a doorway, in the order the pool recorded them.
    /// A mouth is matched to the river coming in the way it faces — the mouth records where the
    /// run really ended, not which node it came from.
    /// </summary>
    private List<bool> DoorwaysAtMouths(RiverPoolMesh record, LevelSelectDesignerData.DesignerPool pool)
    {
        var wall = pool?.rimWall;
        if (record?.mouths == null || wall?.doorways == null || wall.doorways.Count == 0) return null;

        var arrivals = _data.PathsAtPool(pool);
        Vector3 centre = WorldPosOfNode(pool.nodeId);

        var at = new List<bool>(record.mouths.Count);
        foreach (var mouth in record.mouths)
        {
            Vector3 facing = record.transform.TransformDirection(mouth.direction);
            facing.y = 0f;

            // The same river first; any river if none of them carries the mouth's name.
            string best = null;
            for (int pass = 0; pass < 2 && best == null; pass++)
            {
                float bestDot = -2f;
                foreach (var (path, neighbourId) in arrivals)
                {
                    if (pass == 0 && path.riverName != mouth.riverName) continue;
                    Vector3 outward = _data.NodeWorldPosition(neighbourId) - centre;
                    outward.y = 0f;
                    float dot = Vector3.Dot(outward.normalized, facing.normalized);
                    if (dot > bestDot) { bestDot = dot; best = neighbourId; }
                }
            }
            at.Add(wall.HasDoorway(best));
        }
        return at;
    }

    /// <summary>
    /// Rebuilds one pool's rim wall, and the rim walls of every river meeting it — a river's
    /// wall carries on up to the pool's own. Nothing when the pool has not been generated.
    /// </summary>
    private void RebuildPoolRimWall(LevelSelectDesignerData.DesignerPool pool)
    {
        if (pool == null || string.IsNullOrEmpty(pool.nodeId)) return;

        string poolName = PoolMeshName(pool);
        var record = FindObjectsOfType<RiverPoolMesh>().FirstOrDefault(r => r.meshAssetName == poolName);
        if (record == null)
        {
            _consoleStatusMsg = "Rim wall saved — Generate once to build it (this pool has not been generated).";
            return;
        }

        var path = PoolPath(pool);
        RefreshPoolRimWall(record, RunProfileFor(path, record.riverName));

        foreach (var pathId in _data.PathsAtPool(pool).Select(a => a.path.pathId).Distinct().ToList())
            RebuildWalkwaysOnPath(pathId, false);
        AssetDatabase.SaveAssets();
    }
}

#endif
