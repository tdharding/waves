using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR

/// <summary>
/// River walkways — an extra rim along the outside of a river run, between the run and any
/// outpost on that bank (see <see cref="LevelSelectDesignerData.DesignerWalkway"/>). Ticked on a
/// node in its panel; a walkway runs from a ticked node to the next ticked node of the river on
/// the same side, built from the settings of the node it starts at. Run to End of River on the
/// last ticked node carries it on from there to wherever the run stops at the river's end.
///
/// Built as a piece of its own under each run, by <see cref="RiverMeshBuilder.BuildWalkways"/>,
/// in the river material. An outpost standing beside a walkway is moved out past it, and the
/// walkway runs straight along the outpost's face rather than following the rim's curve there.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // WHERE ONE CAN GO
    // ─────────────────────────────────────────────

    /// <summary>The same nodes a rim node can stand on — plain nodes partway along one river.</summary>
    private bool CanHoldWalkway(string nodeId, out string why)
    {
        bool ok = CanHoldRimNode(nodeId, out why);
        if (!ok && why != null) why = why.Replace("Rim nodes", "Walkways");
        return ok;
    }

    /// <summary>One stretch of walkway on a path: node indices it runs between, its side, its settings.</summary>
    private struct WalkwayStretch
    {
        public int from;
        public int to;
        public int side;   // +1 right, -1 left, looking down the path the way it was drawn
        public LevelSelectDesignerData.DesignerWalkway settings;
        public float inset; // walkway 2 only: the first walkway's width it stands out past
    }

    /// <summary>
    /// Every stretch of walkway along a path's nodes — the first walkway, or River Walkway 2
    /// when <paramref name="layer"/> is 2. A stretch runs while each next node is ticked on the
    /// same side, and breaks where a node's settings differ from the node the stretch started
    /// at — the next stretch starts there. Walkway 2 also breaks where the first walkway beside
    /// it starts, stops or changes width, since it stands out past it. The last node ticked on a
    /// side, when it has Run to End of River on, carries its walkway on to the path's last node.
    /// </summary>
    private List<WalkwayStretch> WalkwayStretches(IList<string> nodeIds, int layer = 1)
    {
        var stretches = new List<WalkwayStretch>();
        var list      = layer == 2 ? _data.walkways2 : _data.walkways;
        if (nodeIds == null || nodeIds.Count < 2 || list == null || list.Count == 0)
            return stretches;

        int n = nodeIds.Count;
        var held = new LevelSelectDesignerData.DesignerWalkway[n];
        for (int i = 0; i < n; i++)
        {
            var w = layer == 2 ? _data.Walkway2At(nodeIds[i]) : _data.WalkwayAt(nodeIds[i]);
            held[i] = w != null && w.enabled && CanHoldWalkway(nodeIds[i], out _) ? w : null;
        }

        // Walkway 2: the first walkway's width beside each span between two nodes, per side.
        var insetRight = new float[n];
        var insetLeft  = new float[n];
        if (layer == 2)
            foreach (var s in WalkwayStretches(nodeIds, 1))
                for (int i = s.from; i < s.to; i++)
                    (s.side > 0 ? insetRight : insetLeft)[i] =
                        Mathf.Max(s.settings.width, RiverMeshBuilder.WalkwayMinWidth);

        foreach (int side in new[] { 1, -1 })
        {
            // Run to End of River: every node past the last one ticked on this side stands in
            // as that one, so the stretch carries on to the end of the path.
            var on = held;
            int lastOn = n - 1;
            while (lastOn >= 0 && (held[lastOn] == null || !held[lastOn].Covers(side))) lastOn--;
            if (lastOn >= 0 && lastOn < n - 1 && held[lastOn].toEnd)
            {
                on = (LevelSelectDesignerData.DesignerWalkway[])held.Clone();
                for (int i = lastOn + 1; i < n; i++) on[i] = held[lastOn];
            }

            bool On(int i) => on[i] != null && on[i].Covers(side);
            var inset = side > 0 ? insetRight : insetLeft;

            int at = 0;
            while (at < n - 1)
            {
                if (!On(at) || !On(at + 1)) { at++; continue; }

                var settings = on[at];
                int end = at + 1;
                while (end < n - 1 && On(end + 1) && on[end].SameBuild(settings) &&
                       Mathf.Approximately(inset[end], inset[at]))
                    end++;

                stretches.Add(new WalkwayStretch
                {
                    from = at, to = end, side = side, settings = settings, inset = inset[at],
                });
                at = end;
            }
        }
        return stretches;
    }

    /// <summary>
    /// How far an outpost on this bank is moved out by the walkways it stands beside — the
    /// widths of the walkway and walkway 2 its spot along the path falls on, 0 where there are none.
    /// </summary>
    private float WalkwayPushAt(LevelSelectDesignerData.DesignerPath path, float pathT,
                                LevelSelectDesignerData.OutpostSide bank)
    {
        float push = 0f;
        foreach (int layer in new[] { 1, 2 })
        {
            var walkway = WalkwayBeside(path, pathT, bank, layer);
            if (walkway != null) push += Mathf.Max(walkway.width, RiverMeshBuilder.WalkwayMinWidth);
        }
        return push;
    }

    /// <summary>
    /// The settings of the walkway stretch a spot along the path falls on, on this bank — or
    /// null where there is none. Layer 2 is River Walkway 2.
    /// </summary>
    private LevelSelectDesignerData.DesignerWalkway WalkwayBeside(LevelSelectDesignerData.DesignerPath path,
                                                                  float pathT,
                                                                  LevelSelectDesignerData.OutpostSide bank,
                                                                  int layer = 1)
    {
        if (path == null || path.nodeIds.Count < 2) return null;

        int   side = bank == LevelSelectDesignerData.OutpostSide.Right ? 1 : -1;
        float f    = Mathf.Clamp01(pathT) * (path.nodeIds.Count - 1);

        foreach (var s in WalkwayStretches(path.nodeIds, layer))
            if (s.side == side && f >= s.from - 1e-4f && f <= s.to + 1e-4f)
                return s.settings;
        return null;
    }

    /// <summary>
    /// The walkway right in front of an outpost's face on this bank — walkway 2 where it runs
    /// there, otherwise the first walkway, or null. An outpost's slope comes down onto this one.
    /// </summary>
    private LevelSelectDesignerData.DesignerWalkway OuterWalkwayBeside(LevelSelectDesignerData.DesignerPath path,
                                                                       float pathT,
                                                                       LevelSelectDesignerData.OutpostSide bank)
    {
        return WalkwayBeside(path, pathT, bank, 2) ?? WalkwayBeside(path, pathT, bank, 1);
    }

    /// <summary>How far out from the river's centre an outpost's front face stands on this bank.</summary>
    private float OutpostStandOff(LevelSelectDesignerData.DesignerOutpost outpost,
                                  LevelSelectDesignerData.DesignerPath path,
                                  LevelSelectDesignerData.OutpostSide bank)
    {
        var profile = _data.ProfileFor(path.riverName);
        return InnerWidthAlongPath(path, profile, outpost.pathT) * 0.5f + profile.rimWidth
             + WalkwayPushAt(path, outpost.pathT, bank);
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>The River Walkway tick for the selected node, and its side, width and height.</summary>
    private void DrawSelectedWalkwayProps()
    {
        if (string.IsNullOrEmpty(_selectedNodeId)) return;

        var path = _data.paths.Find(p => p.nodeIds.Contains(_selectedNodeId));
        if (path == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("River Walkway", EditorStyles.boldLabel);

        // The end of a river: a tick per river ending here, for that river alone.
        var ending = _data.paths.Where(p => p != null && p.nodeIds.Count >= 2 &&
                                            p.nodeIds[p.nodeIds.Count - 1] == _selectedNodeId).ToList();
        if (ending.Count > 0)
        {
            foreach (var end in ending)
            foreach (int layer in new[] { 1, 2 })
                DrawWalkwayEndTick(end, layer, ending.Count > 1);
            return;
        }

        if (!CanHoldWalkway(_selectedNodeId, out string why))
        {
            EditorGUILayout.LabelField(why, EditorStyles.miniLabel);
            return;
        }

        DrawWalkwayLayerProps(path, 1);
        EditorGUILayout.Space(2);
        DrawWalkwayLayerProps(path, 2);
    }

    /// <summary>
    /// The tick, side, width and height of one walkway on the selected node — the first
    /// walkway, or River Walkway 2 when <paramref name="layer"/> is 2.
    /// </summary>
    private void DrawWalkwayLayerProps(LevelSelectDesignerData.DesignerPath path, int layer)
    {
        bool second  = layer == 2;
        var  walkway = second ? _data.Walkway2At(_selectedNodeId) : _data.WalkwayAt(_selectedNodeId);

        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.Toggle(
            second
                ? new GUIContent("River Walkway 2", "A second walkway, out past the first walkway " +
                                                    "where one runs beside it (against the run " +
                                                    "where none does), from this node to the next " +
                                                    "node of the river with River Walkway 2 ticked " +
                                                    "on the same side.")
                : new GUIContent("River Walkway", "An extra rim along the outside of the run, from this " +
                                                  "node to the next node of the river with River " +
                                                  "Walkway ticked on the same side."),
            walkway != null && walkway.enabled);

        // Ticked on just now: start from the nearest node before it on the river with this
        // walkway ticked, so a run of nodes carries the same settings on.
        bool wasOn  = walkway != null && walkway.enabled;
        var  source = on && !wasOn ? PreviousWalkwayOnPath(path, _selectedNodeId, layer) ?? walkway
                                   : walkway;

        var   side   = source?.side ?? LevelSelectDesignerData.WalkwaySide.Both;
        float width  = source?.width ?? 0.2f;
        float height = source?.height ?? 0f;
        bool  occlusion = source?.occlusion ?? false;
        var   occlusionStyle = source?.occlusionStyle ?? OcclusionStyle.WholeObject;
        bool  toEnd  = source?.toEnd ?? false;

        if (on && walkway != null)
        {
            side = (LevelSelectDesignerData.WalkwaySide)EditorGUILayout.EnumPopup(
                new GUIContent("Side", "Which bank it runs along, looking down the path the way " +
                                       "it was drawn — or both."), side);
            width = Mathf.Max(RiverMeshBuilder.WalkwayMinWidth, EditorGUILayout.FloatField(
                new GUIContent("Width", "How far it reaches out from the run's outer edge. An " +
                                        "outpost beside it moves out by this much."), width));
            height = EditorGUILayout.FloatField(
                new GUIContent("Height", "How far its top stands above the rim — below it when " +
                                         "negative, 0 for flush."), height);
            OcclusionField(ref occlusion, ref occlusionStyle);

            // Only the last node ticked on the river can carry it on to the end.
            if (!LaterWalkwayOnPath(path, _selectedNodeId, layer))
                toEnd = EditorGUILayout.Toggle(
                    new GUIContent("Run to End of River",
                        "Carries this walkway on past this node to wherever the run stops at the " +
                        "end of the river — a pool's edge, an arena's wall or a junction."),
                    toEnd);

            EditorGUILayout.LabelField((second ? "Runs on to the next node with River Walkway 2 ticked. "
                                               : "Runs on to the next node with River Walkway ticked. ") +
                                       "A stretch takes the settings of the node it starts at.",
                                       EditorStyles.wordWrappedMiniLabel);
        }

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, second ? "Edit River Walkway 2" : "Edit River Walkway");
        _data.walkways  ??= new List<LevelSelectDesignerData.DesignerWalkway>();
        _data.walkways2 ??= new List<LevelSelectDesignerData.DesignerWalkway>();

        if (walkway == null)
        {
            walkway = new LevelSelectDesignerData.DesignerWalkway { nodeId = _selectedNodeId };
            (second ? _data.walkways2 : _data.walkways).Add(walkway);
        }
        walkway.enabled = on;
        walkway.side    = side;
        walkway.width   = width;
        walkway.height  = height;
        walkway.occlusion = occlusion;
        walkway.occlusionStyle = occlusionStyle;
        walkway.toEnd   = toEnd;

        MarkDirty();
        string pathId = path.pathId;
        RebuildAfterTowerEdit("Walkway:" + pathId, () => RebuildWalkwaysOnPath(pathId, true));
        Repaint();
    }

    /// <summary>
    /// The walkway on the nearest node before this one on the path with it ticked — the first
    /// walkway, or River Walkway 2 when <paramref name="layer"/> is 2 — or null where none is.
    /// </summary>
    private LevelSelectDesignerData.DesignerWalkway PreviousWalkwayOnPath(LevelSelectDesignerData.DesignerPath path,
                                                                         string nodeId, int layer)
    {
        for (int i = path.nodeIds.IndexOf(nodeId) - 1; i >= 0; i--)
        {
            var w = layer == 2 ? _data.Walkway2At(path.nodeIds[i]) : _data.WalkwayAt(path.nodeIds[i]);
            if (w != null && w.enabled) return w;
        }
        return null;
    }

    /// <summary>
    /// The River Walkway tick on the end of one river — the walkway, or River Walkway 2 when
    /// <paramref name="layer"/> is 2. It belongs to that river only, never to another river
    /// through the same node: it is the Run to End of River tick of the last node before the end
    /// with that walkway ticked, which the walkway runs on from with that node's side, width and
    /// height.
    /// </summary>
    private void DrawWalkwayEndTick(LevelSelectDesignerData.DesignerPath path, int layer, bool nameRiver)
    {
        bool   second = layer == 2;
        string label  = (second ? "River Walkway 2" : "River Walkway") +
                        (nameRiver ? $" ({path.riverName})" : "");

        // The last node before the end with this walkway ticked — where the walkway runs on from.
        LevelSelectDesignerData.DesignerWalkway from = null;
        for (int i = path.nodeIds.Count - 2; i >= 0 && from == null; i--)
        {
            var w = second ? _data.Walkway2At(path.nodeIds[i]) : _data.WalkwayAt(path.nodeIds[i]);
            if (w != null && w.enabled && CanHoldWalkway(path.nodeIds[i], out _)) from = w;
        }

        if (from == null)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle(new GUIContent(label), false);
            EditorGUILayout.LabelField(
                $"Tick {(second ? "River Walkway 2" : "River Walkway")} on a node before this one " +
                "first — it runs on to here from there.", EditorStyles.wordWrappedMiniLabel);
            return;
        }

        bool on = EditorGUILayout.Toggle(
            new GUIContent(label,
                "Carries the walkway on to the end of this river, to wherever the run stops — a " +
                "pool's edge, an arena's wall or a junction — with the side, width and height of the " +
                "last node before it with this walkway ticked. Only this river: any other river " +
                "through this node is left alone. The same as Run to End of River on that node."),
            from.toEnd);
        if (on == from.toEnd) return;

        Undo.RecordObject(_data, second ? "Edit River Walkway 2" : "Edit River Walkway");
        from.toEnd = on;
        MarkDirty();
        string pathId = path.pathId;
        RebuildAfterTowerEdit("Walkway:" + pathId, () => RebuildWalkwaysOnPath(pathId, true));
        Repaint();
    }

    /// <summary>Whether any node after this one on the path has this walkway ticked.</summary>
    private bool LaterWalkwayOnPath(LevelSelectDesignerData.DesignerPath path, string nodeId, int layer)
    {
        for (int i = path.nodeIds.IndexOf(nodeId) + 1; i < path.nodeIds.Count; i++)
        {
            var w = layer == 2 ? _data.Walkway2At(path.nodeIds[i]) : _data.WalkwayAt(path.nodeIds[i]);
            if (w != null && w.enabled && CanHoldWalkway(path.nodeIds[i], out _)) return true;
        }
        return false;
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Each stretch of walkway beside the drawn path, out from the run's outer edge by its width.
    /// Drawn against the drawn path, which is close to the run but not on it.
    /// </summary>
    private void DrawWalkways()
    {
        if (Event.current.type != EventType.Repaint) return;
        if ((_data.walkways == null || _data.walkways.Count == 0) &&
            (_data.walkways2 == null || _data.walkways2.Count == 0))
            return;

        foreach (var path in _data.paths)
        foreach (int layer in new[] { 1, 2 })
        {
            var stretches = WalkwayStretches(path.nodeIds, layer);
            if (stretches.Count == 0) continue;

            var profile = _data.ProfileFor(path.riverName);

            // A path end on a pool pulled back to the pool's outer rim, as the river's own rims
            // are — a walkway run on to the end of the river stops there.
            var points = path.nodeIds.Select(id => _data.NodeWorldPosition(id)).ToList();
            TrimRimEndAtPool(points, path.nodeIds[0], 0, 1);
            TrimRimEndAtPool(points, path.nodeIds[points.Count - 1], points.Count - 1, points.Count - 2);

            foreach (var s in stretches)
            {
                float width = Mathf.Max(s.settings.width, RiverMeshBuilder.WalkwayMinWidth);
                bool  selected = false;
                for (int i = s.from; i <= s.to; i++) selected |= path.nodeIds[i] == _selectedNodeId;

                // Walkway 2 a shade darker, so the two read apart side by side.
                Handles.color = Faded(selected ? new Color(0.95f, 0.95f, 0.9f, 0.8f)
                              : layer == 2 ? new Color(0.55f, 0.55f, 0.52f, 0.6f)
                                           : new Color(0.7f, 0.7f, 0.66f, 0.6f));

                for (int i = s.from; i < s.to; i++)
                {
                    Vector3 a  = points[i];
                    Vector3 b  = points[i + 1];
                    Vector3 ra = DrawnRightAt(path, i)     * s.side;
                    Vector3 rb = DrawnRightAt(path, i + 1) * s.side;

                    // Out from the run's outer edge, which a width override moves.
                    float ha = HalfOuterAtNode(profile, path.nodeIds[i])     + s.inset;
                    float hb = HalfOuterAtNode(profile, path.nodeIds[i + 1]) + s.inset;

                    Handles.DrawAAConvexPolygon(
                        (Vector3)WorldToCanvas(a + ra * ha),
                        (Vector3)WorldToCanvas(b + rb * hb),
                        (Vector3)WorldToCanvas(b + rb * (hb + width)),
                        (Vector3)WorldToCanvas(a + ra * (ha + width)));
                }
            }
        }
    }

    // The drawn path's flat right at one of its nodes — the two segments either side averaged.
    private Vector3 DrawnRightAt(LevelSelectDesignerData.DesignerPath path, int index)
    {
        int     last = path.nodeIds.Count - 1;
        Vector3 prev = _data.NodeWorldPosition(path.nodeIds[Mathf.Max(0, index - 1)]);
        Vector3 next = _data.NodeWorldPosition(path.nodeIds[Mathf.Min(last, index + 1)]);
        Vector3 dir  = next - prev;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-8f) return Vector3.right;
        return Vector3.Cross(Vector3.up, dir.normalized);
    }

    // ─────────────────────────────────────────────
    // BUILDING
    // ─────────────────────────────────────────────

    /// <summary>
    /// The walkways along a run, as the builder takes them — in the run's own space. With
    /// <paramref name="occluded"/>, one entry per walkway: a name for each ticked for Occlusion
    /// (the node its stretch starts at), null for the rest.
    /// </summary>
    private List<RiverMeshBuilder.Walkway> WalkwaysFor(RiverRunMesh record, List<string> occluded = null,
        List<OcclusionStyle> occludedStyles = null)
    {
        var walkways = new List<RiverMeshBuilder.Walkway>();
        if (record?.pathNodes == null || record.pathNodes.Count < 2) return walkways;

        var ids = record.pathNodes.Select(n => n.nodeId).ToList();
        foreach (int layer in new[] { 1, 2 })
        foreach (var s in WalkwayStretches(ids, layer))
        {
            walkways.Add(new RiverMeshBuilder.Walkway
            {
                from   = record.pathNodes[s.from].position,
                to     = record.pathNodes[s.to].position,
                side   = s.side,
                width  = s.settings.width,
                height = s.settings.height,
                layer  = layer,
                inset  = s.inset,
            });
            occluded?.Add(s.settings.occlusion
                ? $"Walkway{layer}_{SanitiseAssetName(ids[s.from])}_{(s.side > 0 ? "R" : "L")}"
                : null);
            occludedStyles?.Add(s.settings.occlusionStyle);
        }
        return walkways;
    }

    /// <summary>
    /// The front face of every outpost a walkway on this run moves out — placed on this run's
    /// own curve, as the outpost itself is, in the run's own space.
    /// </summary>
    private List<RiverMeshBuilder.WalkwayFlat> WalkwayFlatsFor(RiverRunMesh record)
    {
        var flats = new List<RiverMeshBuilder.WalkwayFlat>();
        if (record == null || record.knots.Count < 2 || _data.outposts == null) return flats;

        var path = _data.paths.Find(p => p.pathId == record.pathId);
        if (path == null || path.nodeIds.Count < 2) return flats;

        Spline spline = null;
        foreach (var outpost in _data.outposts)
        {
            if (outpost == null || outpost.pathId != path.pathId) continue;

            foreach (var bank in OutpostBanks(outpost.side))
            {
                float push = WalkwayPushAt(path, outpost.pathT, bank);
                if (push <= 0f) continue;

                spline ??= record.ToSpline();
                PathFrameAt(path, outpost.pathT, out Vector3 rough, out _);

                float3 local = record.transform.InverseTransformPoint(rough);
                SplineUtility.GetNearestPoint(spline, local, out float3 nearest, out float t);

                Vector3 tangent = record.transform.TransformDirection((Vector3)spline.EvaluateTangent(t));
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 1e-8f) continue;
                tangent.Normalize();

                Vector3 site = record.transform.TransformPoint(nearest);
                Vector3 away = OutpostAway(tangent, bank);
                Vector3 face = site + away * OutpostStandOff(outpost, path, bank);

                flats.Add(new RiverMeshBuilder.WalkwayFlat
                {
                    face      = record.transform.InverseTransformPoint(face),
                    along     = record.transform.InverseTransformDirection(tangent),
                    halfWidth = Mathf.Max(0.005f, outpost.OnBank(bank).width) * 0.5f,
                    side      = bank == LevelSelectDesignerData.OutpostSide.Right ? 1 : -1,
                    beyond    = WalkwayBeside(path, outpost.pathT, bank, 2) is { } second
                              ? Mathf.Max(second.width, RiverMeshBuilder.WalkwayMinWidth) : 0f,
                });
            }
        }
        return flats;
    }

    /// <summary>
    /// Builds the walkway piece beside a run, or takes it away when the run carries none. In the
    /// river material, like the run it stands beside.
    /// </summary>
    private void RefreshWalkways(RiverRunMesh record, RiverProfile profile,
                                 List<Vector3> centres, List<Vector3> forwards)
    {
        if (record == null || string.IsNullOrEmpty(record.meshAssetName)) return;

        string name     = $"RiverWalkways_{record.meshAssetName.Replace("RiverRun_", string.Empty)}";
        var    existing = record.transform.Find(name);
        var    walkwayOcc = new List<string>();
        var    bridgeOcc  = new List<string>();
        var    archwayOcc = new List<string>();
        var    rimWallOcc = new List<string>();
        var    walkwayStyles = new List<OcclusionStyle>();
        var    bridgeStyles  = new List<OcclusionStyle>();
        var    archwayStyles = new List<OcclusionStyle>();
        var    rimWallStyles = new List<OcclusionStyle>();
        var    walkways = WalkwaysFor(record, walkwayOcc, walkwayStyles);
        var    bridges  = BridgesFor(record, bridgeOcc, bridgeStyles);
        var    archways = ArchwaysFor(record, archwayOcc, archwayStyles);
        var    rimWalls = RimWallsFor(record, rimWallOcc, rimWallStyles);
        var    flats    = WalkwayFlatsFor(record);
        var    poolAtStart = RimWallPoolAt(record, false);
        var    poolAtEnd   = RimWallPoolAt(record, true);

        // Each one ticked for Occlusion is built as a mesh of its own, so it fades on its own;
        // the rest share the one piece.
        var pieces = new List<(string name, Mesh built, OcclusionStyle style)>();
        for (int i = walkways.Count - 1; i >= 0; i--)
        {
            if (walkwayOcc[i] == null) continue;
            pieces.Add((OcclusionPieceName(name, walkwayOcc[i]), RiverMeshBuilder.BuildWalkways(
                profile, centres, forwards, new[] { walkways[i] }, flats, MeshEdge, StoneShadingForBuild),
                walkwayStyles[i]));
            walkways.RemoveAt(i);
        }
        for (int i = bridges.Count - 1; i >= 0; i--)
        {
            if (bridgeOcc[i] == null) continue;
            pieces.Add((OcclusionPieceName(name, bridgeOcc[i]), RiverMeshBuilder.BuildWalkways(
                profile, centres, forwards, null, flats, MeshEdge, StoneShadingForBuild,
                new[] { bridges[i] }), bridgeStyles[i]));
            bridges.RemoveAt(i);
        }
        for (int i = archways.Count - 1; i >= 0; i--)
        {
            if (archwayOcc[i] == null) continue;
            pieces.Add((OcclusionPieceName(name, archwayOcc[i]), RiverMeshBuilder.BuildWalkways(
                profile, centres, forwards, null, flats, MeshEdge, StoneShadingForBuild,
                null, new[] { archways[i] }), archwayStyles[i]));
            archways.RemoveAt(i);
        }
        for (int i = rimWalls.Count - 1; i >= 0; i--)
        {
            if (rimWallOcc[i] == null) continue;
            pieces.Add((OcclusionPieceName(name, rimWallOcc[i]), RiverMeshBuilder.BuildWalkways(
                profile, centres, forwards, null, flats, MeshEdge, StoneShadingForBuild,
                null, null, new[] { rimWalls[i] }, poolAtStart, poolAtEnd), rimWallStyles[i]));
            rimWalls.RemoveAt(i);
        }
        RefreshOcclusionPieces(record, name + OcclusionPiecePrefix, pieces);

        if (walkways.Count == 0 && bridges.Count == 0 && archways.Count == 0 && rimWalls.Count == 0)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            record.walkwaysMeshAssetName = null;
            EditorUtility.SetDirty(record);
            return;
        }

        var mesh = SaveGeneratedMesh(name, RiverMeshBuilder.BuildWalkways(
            profile, centres, forwards, walkways, flats, MeshEdge,
            StoneShadingForBuild, bridges, archways, rimWalls, poolAtStart, poolAtEnd));
        if (mesh == null) return;

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null) filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            EditorUtility.SetDirty(filter);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _data.riverMaterial;
            EditorUtility.SetDirty(renderer);
        }
        else
        {
            go = NewMeshChild(record.gameObject, name, mesh);
        }

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        record.walkwaysMeshAssetName = name;
        EditorUtility.SetDirty(record);
    }

    /// <summary>
    /// Rebuilds the walkways of every run of a path — and, when asked, the outposts standing on
    /// it, which a walkway moves out. Runs generated before path nodes were recorded need one
    /// Generate first.
    /// </summary>
    private void RebuildWalkwaysOnPath(string pathId, bool outposts)
    {
        int found = 0;
        foreach (var record in FindObjectsOfType<RiverRunMesh>())
        {
            // A path cut into legs at its pools gives each leg its own '#pool' id.
            if (record.pathNodes == null || record.knots.Count < 2) continue;
            if (record.pathId != pathId && !(record.pathId?.StartsWith(pathId + "#pool") ?? false)) continue;

            var profile = RunProfileFor(record);
            var notches = ToNotches(record.mouths);
            if (!SampleRun(record.ToSpline(), MeshEdge, profile, notches,
                           out var centres, out var forwards)) continue;

            FitRunToArena(centres, forwards,
                          ArenaReachAt(record.arenaAtStart), ArenaReachAt(record.arenaAtEnd),
                          MeshEdge);

            RefreshWalkways(record, profile, centres, forwards);
            found++;
        }

        if (outposts) RebuildOutpostsOnPath(pathId);
        AssetDatabase.SaveAssets();

        if (found == 0)
            _consoleStatusMsg = "Walkway saved — Generate once to build it (this river's runs " +
                                "were generated before path nodes were recorded).";
    }
}

#endif
