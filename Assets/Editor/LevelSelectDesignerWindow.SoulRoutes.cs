using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Souls mode — authoring and drawing the routes soul fish travel across the map.
///
/// A route is an ordered list of WAYPOINTS, in the order the fish take them: river nodes and the
/// arenas between them. Between two waypoints the band takes the shortest way along the rivers —
/// through junctions, pools and river joins — so it hugs the river instead of cutting across it.
/// Where no river joins them it draws a straight link, which is the honest reading of "these two
/// points connect but the river does not join them". Two ways round a loop of rivers? Add a
/// waypoint on the side wanted. Waypoints drag node to node on the canvas.
///
/// The drawing mirrors the Grid Designer's soul-zone display (GridDesignerWindow.DrawSoulZoneShape):
/// a filled band at the swim width, chevrons along it for the direction souls travel, and a solid
/// disc plus black dot at every point. Solid fills and dots throughout — no wire circles — so a
/// route reads as the same kind of object as the zones it connects.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private string _selectedRouteId;

    // The point new canvas clicks go in after, on _insertRouteId's route — set by a row's + and
    // moved on one with every click, so points go in in the order clicked. -1 = clicks append.
    private int    _insertAfterPoint = -1;
    private string _insertRouteId;

    // The route canvas clicks add destinations to — set by + Add Destination, a row's +, or a click
    // on the route's end; cleared by Done or Enter. Editor-session only.
    private string _addingRouteId;

    // The All Route Points dropdown under the destinations. Editor-session only.
    private bool _showAllRoutePoints;

    // The route whose band is being drawn out on the canvas as it will be in game, and when that
    // started. Null = no preview running.
    private string _previewRouteId;
    private double _previewStartTime;

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
            ClearSelectionForSouls();
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
                ClearSelectionForSouls();
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

        DrawRouteOriginTower(route);

        // A tower's bowl is the pool now, so the old origin radius only means something while
        // the route has no tower to start from. The saved value is left alone either way.
        if (!_data.RouteStartsAtSoulJuiceTank(route))
            route.originRadius = EditorGUILayout.Slider("Origin Pool", route.originRadius, 0.25f, 10f);

        // A soul juice tank carries no fish, so its route has no souls to list. Any assigned before
        // the tank lost its fish can be handed back from here.
        if (_data.RouteStartsAtSoulJuiceTank(route))
        {
            int assigned = CountAssigned(route);
            if (assigned > 0)
            {
                EditorGUILayout.HelpBox($"A soul juice tank carries no fish — {assigned} soul(s) are still " +
                                        "assigned to this route and won't appear.", MessageType.Info);
                if (GUILayout.Button("Release All", EditorStyles.miniButton))
                    ReleaseRouteSouls(route);
            }
        }
        else
        {
            DrawOriginPoolSouls(route);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Destinations, in travel order", EditorStyles.miniBoldLabel);

        // Plays the band drawing out from the origin, at the water preset's Draw Speed, so the
        // route can be watched the way it opens in game.
        bool previewing = _previewRouteId == route.routeId;
        if (GUILayout.Button(previewing ? "■ Stop Preview" : "▶ Preview Path Drawing",
                             EditorStyles.miniButton, GUILayout.Height(20)))
        {
            _previewRouteId   = previewing ? null : route.routeId;
            _previewStartTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        // The origin pool heads the list so every pool the band touches has a row. It is set by the
        // Origin Tank field (or shift-click), so the row has no + or x of its own.
        if (!string.IsNullOrEmpty(route.originNodeId))
            EditorGUILayout.LabelField($"Start. POOL  {NodeLabel(route.originNodeId)}  (soul juice tank)",
                                       EditorStyles.miniLabel);

        if (route.points.Count == 0)
            EditorGUILayout.LabelField("None — press + Add Destination and click nodes on the canvas; " +
                                       "the route follows the rivers between them.", EditorStyles.miniLabel);

        for (int i = 0; i < route.points.Count; i++)
        {
            var point = route.points[i];

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(PointRowLabel(route, i), EditorStyles.miniLabel);

            // + : the next canvas clicks go in after this point. Done puts clicks back on the end.
            bool inserting = InsertingAfter(route) == i;
            if (GUILayout.Button(inserting ? "Done" : "+", EditorStyles.miniButton,
                                 GUILayout.Width(inserting ? 40 : 20)))
            {
                _insertAfterPoint = inserting ? -1 : i;
                _insertRouteId    = route.routeId;
                _addingRouteId    = inserting ? null : route.routeId;
            }

            if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                Undo.RecordObject(_data, "Remove Soul Route Point");
                route.points.RemoveAt(i);
                _insertAfterPoint = -1;
                MarkDirty();
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            if (point.kind == LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena)
            {
                point.entranceIn  = EditorGUILayout.IntField("Door In",  point.entranceIn);
                point.entranceOut = EditorGUILayout.IntField("Door Out", point.entranceOut);
            }
            EditorGUI.indentLevel--;
        }

        // + Add Destination: canvas clicks go on the end of the list until Done (or Enter).
        bool adding = _addingRouteId == route.routeId && InsertingAfter(route) < 0;
        var  addBg  = GUI.backgroundColor;
        if (adding) GUI.backgroundColor = route.editorColor;
        if (GUILayout.Button(adding ? "Done — click nodes on the canvas to add" : "+ Add Destination",
                             EditorStyles.miniButton, GUILayout.Height(20)))
        {
            _addingRouteId    = adding ? null : route.routeId;
            _insertAfterPoint = -1;
        }
        GUI.backgroundColor = addBg;

        DrawAllRoutePoints(route);

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// Every node the route passes through, filled-in ones included, in a dropdown under the
    /// destinations — the walk the fish actually swim. Destinations are marked ◆.
    /// </summary>
    private void DrawAllRoutePoints(LevelSelectDesignerData.SoulRoute route)
    {
        var pointIds = new List<int>();
        var ids      = _data.BuildSoulRouteNodeIds(route, pointIds);

        _showAllRoutePoints = EditorGUILayout.Foldout(_showAllRoutePoints,
                                                      $"All Route Points ({ids.Count})", true);
        if (!_showAllRoutePoints) return;

        var destinations = new HashSet<int>(pointIds);
        bool hasOrigin   = !string.IsNullOrEmpty(route.originNodeId);

        EditorGUI.indentLevel++;
        for (int i = 0; i < ids.Count; i++)
        {
            string id   = ids[i];
            string mark = hasOrigin && i == 0 ? "Start" : destinations.Contains(i) ? "◆" : "·";

            string label = $"{mark}  {NodeLabel(id)}";
            if (_data.PoolAt(id) != null) label += "  (pool)";
            var node = _data.nodes.Find(n => n.id == id);
            if (node != null && node.type == LevelSelectDesignerData.NodeType.JunctionSplit)
                label += "  (junction)";

            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
        }
        EditorGUI.indentLevel--;
    }

    /// <summary>The point canvas clicks on this route go in after, or -1 when they go on the end.</summary>
    private int InsertingAfter(LevelSelectDesignerData.SoulRoute route)
        => route != null && route.routeId == _insertRouteId &&
           _insertAfterPoint >= 0 && _insertAfterPoint < route.points.Count
         ? _insertAfterPoint : -1;

    /// <summary>
    /// One row of the order list. Names the point, says whether it is an arena, a pool or a junction, and
    /// warns when the step into it has no river connecting it to the point before.
    /// </summary>
    private string PointRowLabel(LevelSelectDesignerData.SoulRoute route, int index)
    {
        var point = route.points[index];

        if (point.kind == LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena)
            return $"{index + 1}. ARENA  {ArenaLabel(point.nodeId)}";

        string label = _data.PoolAt(point.nodeId) != null
            ? $"{index + 1}. POOL  {NodeLabel(point.nodeId)}"
            : $"{index + 1}. {NodeLabel(point.nodeId)}";

        var node = _data.nodes.Find(n => n.id == point.nodeId);
        if (node != null && node.type == LevelSelectDesignerData.NodeType.JunctionSplit)
            label += "  (junction)";

        // A step with no shared path is drawn as a straight link — usually a mis-click.
        if (index > 0 && !PointsShareAPath(route.points[index - 1], point))
            label += "  ⚠ no river";

        return label;
    }

    /// <summary>
    /// The route's origin: one of the pools standing a soul juice tank. A route begins nowhere
    /// else — its soul juice waits in that bowl until the tank is released. With no tank picked (or
    /// the tank since removed or changed kind) the route shows why it can't start; nothing is
    /// cleared, so putting the tank back puts the route back as it was.
    /// </summary>
    private void DrawRouteOriginTower(LevelSelectDesignerData.SoulRoute route)
    {
        var towers = _data.pools.FindAll(p => p != null && _data.SoulJuiceTankPoolAt(p.nodeId) != null);

        if (towers.Count == 0)
        {
            EditorGUILayout.HelpBox("Place a Soul Juice Tank on a pool first — a route has to begin at one.",
                                    MessageType.Warning);
            return;
        }

        var labels = new GUIContent[towers.Count + 1];
        labels[0] = new GUIContent("— none —");
        int current = 0;
        for (int i = 0; i < towers.Count; i++)
        {
            labels[i + 1] = new GUIContent($"Tank at {NodeLabel(towers[i].nodeId)}");
            if (towers[i].nodeId == route.originNodeId) current = i + 1;
        }

        int picked = EditorGUILayout.Popup(
            new GUIContent("Origin Tank", "The soul juice tank this route begins at. Its path draws out from " +
                                           "there when the tank is released."),
            current, labels);

        if (picked != current && picked > 0)
        {
            Undo.RecordObject(_data, "Set Soul Route Origin");
            route.originNodeId = towers[picked - 1].nodeId;
            MarkDirty();
        }

        if (!_data.RouteStartsAtSoulJuiceTank(route))
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(route.originNodeId)
                    ? "No origin tank — this route can't start. Pick one above."
                    : $"The origin ({NodeLabel(route.originNodeId)}) has no Soul Juice Tank — this route " +
                      "can't start. Pick a tank above, or put one back on that pool.",
                MessageType.Warning);
        else if (route.points.Count > 0 &&
                 _data.NodesBetween(route.originNodeId, route.points[0].nodeId) == null)
            EditorGUILayout.LabelField("⚠ No river joins this pool to the first point.",
                                       EditorStyles.miniLabel);
    }

    private string NodeLabel(string nodeId)
    {
        var arena = _data.arenas.Find(a => a.nodeId == nodeId);
        if (arena != null) return ArenaLabel(nodeId);

        var path = _data.paths.Find(p => p.nodeIds.Contains(nodeId));
        if (path != null)
        {
            string river = string.IsNullOrEmpty(path.riverName) ? path.pathId : path.riverName;
            return $"{river} node {path.nodeIds.IndexOf(nodeId)}";
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

    // The waypoint being dragged on the selected route (-1 = none), and whether it has moved off
    // the node it was pressed on — a press that never moves is a click.
    private int  _routeDragPoint = -1;
    private bool _routeDragMoved;

    private void HandleSoulRouteMode(Event e)
    {
        var route = _data.soulRoutes.Find(r => r.routeId == _selectedRouteId);
        if (route == null) return;

        // Enter is Done: clicks add no more destinations until adding is turned back on.
        if (e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            _addingRouteId    = null;
            _insertAfterPoint = -1;
            Repaint();
            e.Use();
            return;
        }

        if (HandleRouteWaypointDrag(e, route)) return;

        if (e.type != EventType.MouseDown || e.button != 0) return;

        string nodeId = FindNodeAtCanvas(e.mousePosition);
        if (string.IsNullOrEmpty(nodeId)) return;

        AddRoutePointAt(route, nodeId, e.shift);
        e.Use();
    }

    /// <summary>
    /// Press on one of the selected route's waypoints and drag: it jumps node to node under the
    /// mouse and the route redraws along the rivers. A press that never leaves its node is a
    /// click — on the route's end waypoint that turns adding on from there, anywhere else it adds
    /// a destination while adding is on.
    /// </summary>
    private bool HandleRouteWaypointDrag(Event e, LevelSelectDesignerData.SoulRoute route)
    {
        if (e.type == EventType.MouseDown && e.button == 0 && !e.shift)
        {
            _routeDragPoint = -1;

            string nodeId = FindNodeAtCanvas(e.mousePosition);
            if (string.IsNullOrEmpty(nodeId)) return false;

            int index = route.points.FindLastIndex(p => p.nodeId == nodeId);
            if (index < 0) return false;

            _routeDragPoint = index;
            _routeDragMoved = false;
            e.Use();
            return true;
        }

        if (_routeDragPoint < 0 || _routeDragPoint >= route.points.Count)
        {
            _routeDragPoint = -1;
            return false;
        }

        if (e.type == EventType.MouseDrag)
        {
            string nodeId = FindNodeAtCanvas(e.mousePosition);
            var    point  = route.points[_routeDragPoint];
            if (!string.IsNullOrEmpty(nodeId) && nodeId != point.nodeId)
            {
                if (!_routeDragMoved) Undo.RecordObject(_data, "Move Soul Route Point");
                _routeDragMoved = true;
                route.points[_routeDragPoint] = MakeRoutePoint(nodeId);
                MarkDirty();
                Repaint();
            }
            e.Use();
            return true;
        }

        if (e.type == EventType.MouseUp)
        {
            int  index = _routeDragPoint;
            bool moved = _routeDragMoved;
            _routeDragPoint = -1;
            e.Use();
            if (moved) return true;

            if (index == route.points.Count - 1)
            {
                // The end clicked again while already adding would only repeat it.
                if (_addingRouteId == route.routeId && InsertingAfter(route) < 0) return true;

                _addingRouteId    = route.routeId;
                _insertAfterPoint = -1;
                ShowNotification(new GUIContent("Carrying on from the end — click the next destination"));
                Repaint();
            }
            else
            {
                AddRoutePointAt(route, route.points[index].nodeId, false);
            }
            return true;
        }

        return false;
    }

    /// <summary>A waypoint on a node — an arena point when the node is an arena's, arriving by its door.</summary>
    private LevelSelectDesignerData.SoulRoutePoint MakeRoutePoint(string nodeId)
    {
        var arena = _data.arenas.Find(a => a.nodeId == nodeId);
        return new LevelSelectDesignerData.SoulRoutePoint
        {
            kind = arena != null
                ? LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena
                : LevelSelectDesignerData.SoulRoutePoint.PointKind.Node,
            nodeId      = nodeId,
            entranceIn  = arena != null ? arena.entranceIndex : -1,
            entranceOut = -1
        };
    }

    private void AddRoutePointAt(LevelSelectDesignerData.SoulRoute route, string nodeId, bool shift)
    {
        if (!shift && _addingRouteId != route.routeId)
        {
            ShowNotification(new GUIContent("Press + Add Destination (or click the route's end) to add"));
            return;
        }

        // Shift-click sets the origin — only onto a soul juice tank, since a route begins nowhere
        // else; a plain click appends the next point on the journey.
        if (shift)
        {
            if (_data.SoulJuiceTankPoolAt(nodeId) == null)
            {
                ShowNotification(new GUIContent("A route has to begin at a Soul Juice Tank"));
                return;
            }
            route.originNodeId = nodeId;
        }
        else
        {
            var point = MakeRoutePoint(nodeId);

            Undo.RecordObject(_data, "Add Soul Route Point");

            int after = InsertingAfter(route);
            if (after >= 0)
            {
                route.points.Insert(after + 1, point);
                _insertAfterPoint = after + 1;
            }
            else
            {
                route.points.Add(point);
            }
        }

        MarkDirty();
        Repaint();
    }

    // ─────────────────────────────────────────────
    // DRAWING
    // ─────────────────────────────────────────────

    private void DrawSoulRoutes()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_data.soulRoutes == null || _data.soulRoutes.Count == 0) return;
        if (!LevelSelectDesignerSettings.SoulShow) return;

        Color dotCol = LevelSelectDesignerSettings.SoulDotColour;

        foreach (var route in _data.soulRoutes)
        {
            bool selected = route.routeId == _selectedRouteId;

            // Unselected routes sit back so they don't fight the river for attention.
            Color band = LevelSelectDesignerSettings.SoulRouteColours ? route.editorColor : LevelSelectDesignerSettings.SoulRouteColour;
            band.a = selected ? LevelSelectDesignerSettings.SoulSelBandOpacity : LevelSelectDesignerSettings.SoulBandOpacity;

            var   pixelPath = BuildRoutePixelPath(route);
            float halfWidth = Mathf.Max(route.bandWidth * _zoom, 1f);

            if (route.routeId == _previewRouteId)
                pixelPath = PreviewDrawnPath(pixelPath);

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

            // Origin — where this route's souls begin. At a soul juice tank that is its orb; a
            // route without one keeps its old marker, drawn grey so it reads as not able to start.
            if (!string.IsNullOrEmpty(route.originNodeId))
            {
                Vector2 origin    = WorldToCanvas(WorldPosOfNode(route.originNodeId));
                var     towerPool = _data.SoulJuiceTankPoolAt(route.originNodeId);

                float radius = towerPool != null
                    ? towerPool.soulJuiceTank.Scaled(towerPool.towerScale).orbRadius
                    : route.originRadius;

                Color fill = band;
                if (towerPool == null) fill = new Color(0.5f, 0.5f, 0.5f, band.a);

                Handles.color = fill;
                Handles.DrawSolidDisc(origin, Vector3.forward, Mathf.Max(radius * _zoom, 4f));
                Handles.color = dotCol;
                Handles.DrawSolidDisc(origin, Vector3.forward, Mathf.Max(radius * _zoom * 0.35f, 2f));
            }

            // Solid disc + black dot at every authored point.
            float dotR = Mathf.Max(halfWidth * 0.45f, 3f);
            if (LevelSelectDesignerSettings.SoulPoints)
            {
                foreach (var point in route.points)
                {
                    Vector2 p = WorldToCanvas(WorldPosOfNode(point.nodeId));
                    Handles.color = band;
                    Handles.DrawSolidDisc(p, Vector3.forward, halfWidth);
                    Handles.color = dotCol;
                    Handles.DrawSolidDisc(p, Vector3.forward, dotR);
                }
            }

            if (LevelSelectDesignerSettings.SoulArrows && (selected || LevelSelectDesignerSettings.SoulArrowsAll))
                DrawRouteFlowArrows(pixelPath, halfWidth);
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

        // DEBUG [SoulRouteStale] — which route nodes draw at a position held over from the last
        // Generate rather than where the node now stands. Remove once confirmed.
        var stale = new System.Text.StringBuilder();
        foreach (string id in ids)
        {
            if (!_correctedNodePositions.TryGetValue(id, out var held)) continue;
            var live = _data.nodes.Find(n => n.id == id);
            if (live == null) continue;
            float gap = Vector2.Distance(new Vector2(held.x, held.z),
                                         new Vector2(live.worldPosition.x, live.worldPosition.z));
            if (gap > 0.05f) stale.Append($" {NodeLabel(id)} ({live.type}) off by {gap:0.00};");
        }
        string staleKey = route.routeId + stale;
        if (stale.Length > 0 && staleKey != _debugStaleKey)
            Debug.Log($"[SoulRouteStale] '{route.displayName}':{stale}");
        if (stale.Length > 0) _debugStaleKey = staleKey;

        return pts;
    }

    private string _debugStaleKey;

    /// <summary>
    /// The part of the route drawn so far in a preview: as far from the origin as the scene's
    /// Travelling Soul Fish Controller's Zone Draw Speed has carried it since Preview was pressed.
    /// Keeps the window repainting until the band reaches the end, then ends the preview.
    /// </summary>
    private List<Vector2> PreviewDrawnPath(List<Vector2> pixelPath)
    {
        var   fish   = UnityEngine.Object.FindFirstObjectByType<TravellingSoulFishController>();
        float draw   = fish != null ? fish.ZoneDrawSpeed : 1.2f;
        float speed  = Mathf.Max(draw, 0.01f) * _zoom;   // world units/s → canvas pixels/s
        float length = (float)(EditorApplication.timeSinceStartup - _previewStartTime) * speed;

        var drawn = new List<Vector2>(pixelPath.Count);
        if (pixelPath.Count > 0) drawn.Add(pixelPath[0]);

        float travelled = 0f;
        for (int i = 0; i < pixelPath.Count - 1; i++)
        {
            float seg = Vector2.Distance(pixelPath[i], pixelPath[i + 1]);
            if (travelled + seg >= length)
            {
                float t = seg > 0.0001f ? (length - travelled) / seg : 1f;
                drawn.Add(Vector2.Lerp(pixelPath[i], pixelPath[i + 1], t));
                Repaint();
                return drawn;
            }
            travelled += seg;
            drawn.Add(pixelPath[i + 1]);
        }

        _previewRouteId = null;
        Repaint();
        return drawn;
    }

    private bool PointsShareAPath(LevelSelectDesignerData.SoulRoutePoint a,
                                  LevelSelectDesignerData.SoulRoutePoint b)
    {
        return _data.NodesBetween(a.nodeId, b.nodeId) != null;
    }

    /// <summary>
    /// Chevrons spaced along the band pointing in point order — the direction souls travel.
    /// Same construction as the Grid Designer's zone arrows so the two read alike.
    /// </summary>
    private static void DrawRouteFlowArrows(List<Vector2> pixelPath, float halfWidth)
    {
        if (pixelPath == null || pixelPath.Count < 2) return;

        float scale   = LevelSelectDesignerSettings.SoulArrowScale;
        float spacing = Mathf.Max(halfWidth * 3f, 28f) * Mathf.Max(scale, 0.1f);
        float size    = Mathf.Clamp(halfWidth * 0.8f, 5f, 16f) * scale;
        Handles.color = LevelSelectDesignerSettings.SoulArrowColour;

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
