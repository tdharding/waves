using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Souls mode — authoring and drawing the routes soul fish travel across the map.
///
/// A route is an ordered list of POINTS, in the order the fish take them: river nodes and the
/// arenas between them. Between two consecutive points that share a river path the band follows
/// that path's own nodes, so it hugs the river instead of cutting across it. Where they share no
/// path it draws a straight link, which is the honest reading of "these two points connect but
/// the river does not join them".
///
/// A junction therefore needs no special handling: the route goes only where the next point says,
/// so it stops at the junction until a point past it is chosen.
///
/// The drawing mirrors the Grid Designer's soul-zone display (GridDesignerWindow.DrawSoulZoneShape):
/// a filled band at the swim width, chevrons along it for the direction souls travel, and a solid
/// disc plus black dot at every point. Solid fills and dots throughout — no wire circles — so a
/// route reads as the same kind of object as the zones it connects.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private string _selectedRouteId;

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private void DrawSoulRoutePanel()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Soul Routes", EditorStyles.boldLabel);

        if (GUILayout.Button("+ New Route", EditorStyles.miniButton, GUILayout.Height(20)))
        {
            var route = new LevelSelectDesignerData.SoulRoute
            {
                routeId     = System.Guid.NewGuid().ToString("N").Substring(0, 8),
                displayName = "Route " + (_data.soulRoutes.Count + 1)
            };
            _data.soulRoutes.Add(route);
            _selectedRouteId = route.routeId;
            MarkDirty();
        }

        for (int i = 0; i < _data.soulRoutes.Count; i++)
        {
            var  route    = _data.soulRoutes[i];
            bool selected = route.routeId == _selectedRouteId;

            EditorGUILayout.BeginHorizontal();

            var bg = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = route.editorColor;
            if (GUILayout.Button($"{route.displayName}  ({route.points.Count} points)",
                                 EditorStyles.miniButton))
            {
                _selectedRouteId = selected ? null : route.routeId;
            }
            GUI.backgroundColor = bg;

            if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                if (_selectedRouteId == route.routeId) _selectedRouteId = null;
                _data.soulRoutes.RemoveAt(i);
                MarkDirty();
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();

            if (selected) DrawSelectedRouteProps(route);
        }
    }

    private void DrawSelectedRouteProps(LevelSelectDesignerData.SoulRoute route)
    {
        EditorGUI.indentLevel++;

        route.displayName  = EditorGUILayout.TextField("Name", route.displayName);
        route.editorColor  = EditorGUILayout.ColorField("Colour", route.editorColor);
        route.bandWidth    = EditorGUILayout.Slider("Band Width", route.bandWidth, 0.05f, 5f);
        route.originRadius = EditorGUILayout.Slider("Origin Pool", route.originRadius, 0.25f, 10f);

        EditorGUILayout.LabelField(string.IsNullOrEmpty(route.originNodeId)
            ? "Origin: unset — click a node on the canvas"
            : $"Origin: {NodeLabel(route.originNodeId)}", EditorStyles.miniLabel);

        DrawOriginPoolSouls(route);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Points the fish take, in order", EditorStyles.miniBoldLabel);

        if (route.points.Count == 0)
            EditorGUILayout.LabelField("None — click points on the canvas in travel order.",
                                       EditorStyles.miniLabel);

        for (int i = 0; i < route.points.Count; i++)
        {
            var point = route.points[i];

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(PointRowLabel(route, i), EditorStyles.miniLabel);

            GUI.enabled = i > 0;
            if (GUILayout.Button("Up", EditorStyles.miniButton, GUILayout.Width(26)))
            {
                route.points.RemoveAt(i);
                route.points.Insert(i - 1, point);
                MarkDirty();
            }
            GUI.enabled = true;

            if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                route.points.RemoveAt(i);
                MarkDirty();
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();

            // Gate is authored now and read later — see the plan's "deliberately not in v1".
            EditorGUI.indentLevel++;
            point.gateCondition = EditorGUILayout.TextField("Gate", point.gateCondition);
            if (point.kind == LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena)
            {
                point.entranceIn  = EditorGUILayout.IntField("Door In",  point.entranceIn);
                point.entranceOut = EditorGUILayout.IntField("Door Out", point.entranceOut);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// One row of the order list. Names the point, says whether it is an arena or a junction, and
    /// warns when the step into it has no river connecting it to the point before.
    /// </summary>
    private string PointRowLabel(LevelSelectDesignerData.SoulRoute route, int index)
    {
        var point = route.points[index];

        if (point.kind == LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena)
            return $"{index + 1}. ARENA  {ArenaLabel(point.nodeId)}";

        string label = $"{index + 1}. {NodeLabel(point.nodeId)}";

        var node = _data.nodes.Find(n => n.id == point.nodeId);
        if (node != null && node.type == LevelSelectDesignerData.NodeType.JunctionSplit)
            label += "  (junction)";

        // A step with no shared path is drawn as a straight link — usually a mis-click.
        if (index > 0 && !PointsShareAPath(route.points[index - 1], point))
            label += "  ⚠ no river";

        return label;
    }

    private string NodeLabel(string nodeId)
    {
        var arena = _data.arenas.Find(a => a.nodeId == nodeId);
        if (arena != null) return ArenaLabel(nodeId);

        var path = _data.paths.Find(p => p.nodeIds.Contains(nodeId));
        if (path != null)
        {
            string river = string.IsNullOrEmpty(path.riverName) ? path.pathId : path.riverName;
            return $"{river} [{path.nodeIds.IndexOf(nodeId)}]";
        }

        return nodeId;
    }

    private string ArenaLabel(string arenaNodeId)
    {
        var arena = _data.arenas.Find(a => a.nodeId == arenaNodeId);
        if (arena == null) return arenaNodeId;
        return arena.gridData != null ? arena.gridData.name : arenaNodeId;
    }

    // ─────────────────────────────────────────────
    // ORIGIN POOL SOULS
    // Mirrors the Grid Designer's zone souls list: a fixed number of slots, each holding one
    // SoulData, filled by Auto-Assign from the unallocated pool or picked by hand. These are the
    // souls that BEGIN on this route.
    // ─────────────────────────────────────────────

    private SoulData[] _allSouls;
    private string[]   _soulLabels;

    // A slot-count change is applied on the next Layout event rather than the moment the field is
    // committed: growing or shrinking the list mid-pass changes how many controls IMGUI sees
    // between Layout and Repaint, which throws, drops the keystroke and leaves the count unchanged.
    private int    _pendingSlotCount = -1;
    private string _pendingSlotRouteId;

    private void DrawOriginPoolSouls(LevelSelectDesignerData.SoulRoute route)
    {
        if (route.souls == null) route.souls = new List<SoulData>();

        if (Event.current.type == EventType.Layout &&
            _pendingSlotCount >= 0 && _pendingSlotRouteId == route.routeId)
        {
            while (route.souls.Count < _pendingSlotCount) route.souls.Add(null);
            while (route.souls.Count > _pendingSlotCount)
            {
                // Hand the claim back before the slot goes, or the soul stays flagged as taken
                // with nothing holding it and the unallocated pile shrinks for good.
                ReleaseSoul(route.souls[route.souls.Count - 1]);
                route.souls.RemoveAt(route.souls.Count - 1);
            }
            _pendingSlotCount   = -1;
            _pendingSlotRouteId = null;
            _allSouls           = null;
            MarkDirty();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Souls in the origin pool ({CountAssigned(route)}/{route.souls.Count})",
                                   EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        // Delayed, so the count is read once on commit rather than on every keystroke.
        int newCount = Mathf.Max(0, EditorGUILayout.DelayedIntField("Slots", route.souls.Count));
        EditorGUILayout.EndHorizontal();

        if (newCount != route.souls.Count)
        {
            _pendingSlotCount   = newCount;
            _pendingSlotRouteId = route.routeId;
            Repaint();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Auto-Assign", EditorStyles.miniButton))
            AutoAssignRouteSouls(route);
        if (GUILayout.Button("Release All", EditorStyles.miniButton))
            ReleaseRouteSouls(route);
        EditorGUILayout.EndHorizontal();

        EnsureSoulCache();

        for (int i = 0; i < route.souls.Count; i++)
        {
            var picked = (SoulData)EditorGUILayout.ObjectField($"  {i + 1}", route.souls[i],
                                                               typeof(SoulData), false);
            if (picked != route.souls[i])
            {
                // Same swap the Grid Designer does on a zone slot: the soul leaving the slot is
                // freed and the one arriving is claimed, so the flags match what the slots hold.
                ReleaseSoul(route.souls[i]);
                ClaimSoul(picked, route);
                route.souls[i] = picked;
                MarkDirty();
                _allSouls = null;
            }
        }
    }

    private static int CountAssigned(LevelSelectDesignerData.SoulRoute route)
    {
        if (route.souls == null) return 0;
        int n = 0;
        foreach (var soul in route.souls) if (soul != null) n++;
        return n;
    }

    /// <summary>Marks a soul as taken by this route's origin pool.</summary>
    private static void ClaimSoul(SoulData soul, LevelSelectDesignerData.SoulRoute route)
    {
        if (soul == null) return;
        Undo.RecordObject(soul, "Allocate Soul To Origin Pool");
        soul.allocated          = true;
        soul.allocatedToLevelID = $"pool:{route.routeId}";
        EditorUtility.SetDirty(soul);
    }

    /// <summary>Hands a soul back to the unallocated pile.</summary>
    private static void ReleaseSoul(SoulData soul)
    {
        if (soul == null) return;
        Undo.RecordObject(soul, "Release Soul From Origin Pool");
        soul.allocated          = false;
        soul.allocatedToLevelID = "";
        EditorUtility.SetDirty(soul);
    }

    /// <summary>
    /// Fills empty slots from souls nothing has claimed yet, exactly as the Grid Designer does for
    /// a zone. The claim is marked on the SoulData with the same `allocated` flag both tools read,
    /// so a soul can never sit in a level and a pool at once — `allocatedToLevelID` names the route
    /// rather than a level, which is what the Grid Designer's "(→ …)" label then shows.
    /// </summary>
    private void AutoAssignRouteSouls(LevelSelectDesignerData.SoulRoute route)
    {
        if (route.souls == null) route.souls = new List<SoulData>();

        // Auto-Assign fills empty slots, so with no slots there is nothing for it to do. Say so
        // instead of returning silently, which read as the button being broken.
        int empty = route.souls.Count - CountAssigned(route);
        if (empty == 0)
        {
            EditorUtility.DisplayDialog("Auto-Assign Souls",
                route.souls.Count == 0
                    ? "This pool has no slots yet. Set 'Slots' to the number of souls that should "
                      + "begin on this route, then Auto-Assign fills them."
                    : "Every slot in this pool is already filled. Add slots, or Release All first.",
                "OK");
            return;
        }

        EnsureSoulCache();
        if (_allSouls == null || _allSouls.Length == 0)
        {
            EditorUtility.DisplayDialog("Auto-Assign Souls",
                "No SoulData assets found in the project.", "OK");
            return;
        }

        var free = new Queue<SoulData>();
        foreach (var soul in _allSouls)
            if (soul != null && !soul.allocated) free.Enqueue(soul);

        if (free.Count == 0)
        {
            EditorUtility.DisplayDialog("Auto-Assign Souls",
                "No unallocated souls available. Free some with the Game Tester tool's soul cleanup.",
                "OK");
            return;
        }

        int assigned = 0;

        for (int i = 0; i < route.souls.Count && free.Count > 0; i++)
        {
            if (route.souls[i] != null) continue;

            SoulData soul = free.Dequeue();
            ClaimSoul(soul, route);
            route.souls[i] = soul;
            assigned++;
        }

        MarkDirty();
        // The claims live on the soul assets, not on the designer's working clone, so write them
        // out now — otherwise closing the window without saving loses the slots but keeps the
        // souls flagged as taken.
        AssetDatabase.SaveAssets();
        _allSouls = null;   // labels are stale now

        Debug.Log($"[LevelSelectDesigner] Assigned {assigned} soul(s) to '{route.displayName}' " +
                  $"origin pool. {free.Count} unallocated remaining.");

        if (assigned < empty)
            EditorUtility.DisplayDialog("Auto-Assign Souls",
                $"Filled {assigned} of {empty} empty slot(s) — the unallocated pile ran out.", "OK");
    }

    /// <summary>Empties the pool and hands its souls back to the unallocated pile.</summary>
    private void ReleaseRouteSouls(LevelSelectDesignerData.SoulRoute route)
    {
        if (route.souls == null) return;
        int released = 0;

        for (int i = 0; i < route.souls.Count; i++)
        {
            if (route.souls[i] == null) continue;

            ReleaseSoul(route.souls[i]);
            route.souls[i] = null;
            released++;
        }

        MarkDirty();
        AssetDatabase.SaveAssets();
        _allSouls = null;
        Debug.Log($"[LevelSelectDesigner] Released {released} soul(s) from '{route.displayName}'.");
    }

    private void EnsureSoulCache()
    {
        if (_allSouls != null) return;

        var found = new List<SoulData>();
        foreach (string guid in AssetDatabase.FindAssets("t:SoulData"))
        {
            var soul = AssetDatabase.LoadAssetAtPath<SoulData>(AssetDatabase.GUIDToAssetPath(guid));
            if (soul != null) found.Add(soul);
        }
        found.Sort((a, b) => a.soulDataIdentity.CompareTo(b.soulDataIdentity));
        _allSouls = found.ToArray();

        _soulLabels = new string[_allSouls.Length];
        for (int i = 0; i < _allSouls.Length; i++)
            _soulLabels[i] = $"{_allSouls[i].soulDataIdentity}: {_allSouls[i].name}";
    }

    // ─────────────────────────────────────────────
    // INPUT
    // ─────────────────────────────────────────────

    private void HandleSoulRouteMode(Event e)
    {
        if (e.type != EventType.MouseDown || e.button != 0) return;

        var route = _data.soulRoutes.Find(r => r.routeId == _selectedRouteId);
        if (route == null) return;

        string nodeId = FindNodeAtCanvas(e.mousePosition);
        if (string.IsNullOrEmpty(nodeId)) return;

        // Shift-click sets the origin pool; a plain click appends the next point on the journey.
        if (e.shift)
        {
            route.originNodeId = nodeId;
        }
        else
        {
            bool isArena = _data.arenas.Exists(a => a.nodeId == nodeId);
            var  arena   = isArena ? _data.arenas.Find(a => a.nodeId == nodeId) : null;

            route.points.Add(new LevelSelectDesignerData.SoulRoutePoint
            {
                kind = isArena
                    ? LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena
                    : LevelSelectDesignerData.SoulRoutePoint.PointKind.Node,
                nodeId      = nodeId,
                entranceIn  = arena != null ? arena.entranceIndex : -1,
                entranceOut = -1
            });
        }

        MarkDirty();
        Repaint();
        e.Use();
    }

    // ─────────────────────────────────────────────
    // DRAWING
    // ─────────────────────────────────────────────

    private void DrawSoulRoutes()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_data.soulRoutes == null || _data.soulRoutes.Count == 0) return;

        foreach (var route in _data.soulRoutes)
        {
            bool selected = route.routeId == _selectedRouteId;

            // Unselected routes sit back so they don't fight the river for attention.
            Color band = route.editorColor;
            band.a = selected ? 0.5f : 0.22f;

            var   pixelPath = BuildRoutePixelPath(route);
            float halfWidth = Mathf.Max(route.bandWidth * _zoom, 1f);

            // Band — filled quads along the path, discs at the joints keep it gapless.
            Handles.color = band;
            for (int i = 0; i < pixelPath.Count - 1; i++)
            {
                Vector2 a = pixelPath[i];
                Vector2 b = pixelPath[i + 1];
                Vector2 d = b - a;
                if (d.sqrMagnitude < 0.0001f) continue;
                d.Normalize();

                Vector2 n = new Vector2(-d.y, d.x) * halfWidth;
                Handles.DrawAAConvexPolygon((Vector3)(a + n), (Vector3)(b + n),
                                            (Vector3)(b - n), (Vector3)(a - n));
                if (i > 0) Handles.DrawSolidDisc(a, Vector3.forward, halfWidth);
            }

            // Origin pool — where this route's souls begin.
            if (!string.IsNullOrEmpty(route.originNodeId))
            {
                Vector2 origin = WorldToCanvas(WorldPosOfNode(route.originNodeId));
                Handles.color = band;
                Handles.DrawSolidDisc(origin, Vector3.forward,
                                      Mathf.Max(route.originRadius * _zoom, 4f));
                Handles.color = Color.black;
                Handles.DrawSolidDisc(origin, Vector3.forward,
                                      Mathf.Max(route.originRadius * _zoom * 0.35f, 2f));
            }

            // Solid disc + black dot at every authored point.
            float dotR = Mathf.Max(halfWidth * 0.45f, 3f);
            foreach (var point in route.points)
            {
                Vector2 p = WorldToCanvas(WorldPosOfNode(point.nodeId));
                Handles.color = band;
                Handles.DrawSolidDisc(p, Vector3.forward, halfWidth);
                Handles.color = Color.black;
                Handles.DrawSolidDisc(p, Vector3.forward, dotR);
            }

            if (selected) DrawRouteFlowArrows(pixelPath, halfWidth);
        }
    }

    /// <summary>
    /// The route as canvas points. The SHAPE comes from LevelSelectDesignerData.BuildSoulRouteNodeIds
    /// — the same walk the runtime swims — so what is drawn here cannot disagree with what the fish
    /// do. Only the id-to-position step is local, because the designer has corrected node positions
    /// the asset does not.
    /// </summary>
    private List<Vector2> BuildRoutePixelPath(LevelSelectDesignerData.SoulRoute route)
    {
        var ids = _data.BuildSoulRouteNodeIds(route);
        var pts = new List<Vector2>(ids.Count);

        foreach (string id in ids)
            pts.Add(WorldToCanvas(WorldPosOfNode(id)));

        return pts;
    }

    private bool PointsShareAPath(LevelSelectDesignerData.SoulRoutePoint a,
                                  LevelSelectDesignerData.SoulRoutePoint b)
    {
        return _data.FindPathContaining(a.nodeId, b.nodeId) != null;
    }

    /// <summary>
    /// Chevrons spaced along the band pointing in point order — the direction souls travel.
    /// Same construction as the Grid Designer's zone arrows so the two read alike.
    /// </summary>
    private static void DrawRouteFlowArrows(List<Vector2> pixelPath, float halfWidth)
    {
        if (pixelPath == null || pixelPath.Count < 2) return;

        float spacing = Mathf.Max(halfWidth * 3f, 28f);
        float size    = Mathf.Clamp(halfWidth * 0.8f, 5f, 16f);
        Handles.color = new Color(0f, 0f, 0f, 0.6f);

        float next        = spacing * 0.5f;
        float accumulated = 0f;

        for (int i = 0; i < pixelPath.Count - 1; i++)
        {
            Vector2 a   = pixelPath[i];
            Vector2 b   = pixelPath[i + 1];
            float   len = Vector2.Distance(a, b);
            if (len < 0.0001f) continue;
            Vector2 dir = (b - a) / len;

            while (next <= accumulated + len)
            {
                Vector2 p     = a + dir * (next - accumulated);
                Vector2 perp  = new Vector2(-dir.y, dir.x);
                Vector2 tip   = p + dir * size;
                Vector2 backL = p - dir * size * 0.4f + perp * size * 0.7f;
                Vector2 backR = p - dir * size * 0.4f - perp * size * 0.7f;
                Handles.DrawAAConvexPolygon((Vector3)tip, (Vector3)backL, (Vector3)backR);
                next += spacing;
            }

            accumulated += len;
        }
    }
}

#endif
