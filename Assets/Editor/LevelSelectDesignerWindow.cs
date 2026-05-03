using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR

public class LevelSelectDesignerWindow : EditorWindow
{
    // ── Modes ─────────────────────────────────────────────────────
    private enum DesignerMode { Draw, Select, Junction, Arena, Obstacle, Shop, Landscape }

    // ── Data ──────────────────────────────────────────────────────
    private LevelSelectDesignerData _data;

    // ── Interaction state ─────────────────────────────────────────
    private DesignerMode _mode = DesignerMode.Draw;
    private string _selectedPathId;
    private string _selectedNodeId;
    private string _selectedObstacleId;
    private string _selectedArenaNodeId;
    private string _selectedJunctionNodeId;
    private string _selectedHillPointId;
    private bool   _isDraggingHillPoint;
    private bool   _canvasFocused;
    private int    _selectedEntranceIdx   = -1;

    // Right panel foldout states
    private bool _foldPathProps   = true;
    private bool _foldPaths       = true;
    private bool _foldJunctions   = true;
    private bool _foldArenas      = true;
    private bool _foldRivers      = true;
    private bool _foldObstacles   = true;
    private bool _foldStats       = false;
    private bool _foldSetup       = false;
    private const float ARENA_CANVAS_RADIUS = 35f;

    // Draw mode
    private List<string> _drawingNodeIds = new();
    private bool   _isDrawing;
    private bool   _isExtending;
    private string _extendingPathId;

    // Select mode drag
    private bool   _isDraggingNode;
    private string _draggingNodeId;
    private Vector2 _dragOffset;

    // ── Canvas view ───────────────────────────────────────────────
    private Vector2 _viewCenter   = Vector2.zero;
    private float   _zoom         = 20f;   // pixels per world unit
    private bool    _isPanning;
    private bool    _spaceHeld;
    private Vector2 _panStart;
    private Vector2 _viewCenterAtPanStart;
    private Rect    _canvasRect;

    // ── Split preset ──────────────────────────────────────────────
    private SplineSplitterPreset _splitPreset;
    private const string PresetPath = "Assets/ScriptsData/DataScripts/Settings/SplineSplitter/StandardSplineSplitter.asset";

    // ── Panel sizes ───────────────────────────────────────────────
    private float   _leftPanelWidth  = 210f;
    private float   _rightPanelWidth = 175f;
    private bool    _isResizingLeft;
    private bool    _isResizingRight;
    private Vector2 _leftScroll;
    private Vector2 _rightScroll;

    private const float HANDLE_W    = 5f;
    private const float PANEL_MIN_W = 120f;
    private const float PANEL_MAX_W = 600f;

    // ── EditorPrefs keys ──────────────────────────────────────────
    private const string K_DataPath   = "LSD_DataPath";
    private const string K_ViewCX     = "LSD_ViewCX";
    private const string K_ViewCY     = "LSD_ViewCY";
    private const string K_Zoom       = "LSD_Zoom";
    private const string K_LeftW      = "LSD_LeftW";
    private const string K_RightW     = "LSD_RightW";

    // ── Constants ─────────────────────────────────────────────────
    private const float NODE_RADIUS     = 6f;
    private const float SNAP_RADIUS     = 14f;
    private const float OBSTACLE_RADIUS = 5f;
    private const float PATH_HIT_DIST   = 8f;

    // ══════════════════════════════════════════════════════════════
    // MENU / LIFECYCLE
    // ══════════════════════════════════════════════════════════════
    [MenuItem("Tools/Waves/Level Select Designer")]
    public static void Open() => GetWindow<LevelSelectDesignerWindow>("Level Select Designer");

    private void OnEnable()
    {
        _viewCenter      = new Vector2(EditorPrefs.GetFloat(K_ViewCX, 0f), EditorPrefs.GetFloat(K_ViewCY, 0f));
        _zoom            = EditorPrefs.GetFloat(K_Zoom,   20f);
        _leftPanelWidth  = EditorPrefs.GetFloat(K_LeftW,  210f);
        _rightPanelWidth = EditorPrefs.GetFloat(K_RightW, 175f);

        string savedPath = EditorPrefs.GetString(K_DataPath, "");
        if (!string.IsNullOrEmpty(savedPath))
            _data = AssetDatabase.LoadAssetAtPath<LevelSelectDesignerData>(savedPath);

        _splitPreset = AssetDatabase.LoadAssetAtPath<SplineSplitterPreset>(PresetPath);

        if (_data != null)
            TryAutoFillPrefabs();
    }

    private void OnDisable()
    {
        SaveViewPrefs();
        if (_data != null)
            EditorPrefs.SetString(K_DataPath, AssetDatabase.GetAssetPath(_data));
    }

    private void SaveViewPrefs()
    {
        EditorPrefs.SetFloat(K_ViewCX, _viewCenter.x);
        EditorPrefs.SetFloat(K_ViewCY, _viewCenter.y);
        EditorPrefs.SetFloat(K_Zoom,   _zoom);
        EditorPrefs.SetFloat(K_LeftW,  _leftPanelWidth);
        EditorPrefs.SetFloat(K_RightW, _rightPanelWidth);
    }

    // ══════════════════════════════════════════════════════════════
    // MAIN GUI
    // ══════════════════════════════════════════════════════════════
    private void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
        DrawLeftPanel();
        DrawPanelHandle(ref _isResizingLeft,  ref _leftPanelWidth,  +1f);
        DrawCanvas();
        DrawPanelHandle(ref _isResizingRight, ref _rightPanelWidth, -1f);
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();
    }

    // ── Panel resize handle ───────────────────────────────────────
    // sign: +1 = left handle (delta grows left panel), -1 = right handle (delta shrinks right panel)
    private void DrawPanelHandle(ref bool resizing, ref float panelWidth, float sign)
    {
        Rect r = GUILayoutUtility.GetRect(HANDLE_W, HANDLE_W,
            GUILayout.Width(HANDLE_W), GUILayout.ExpandHeight(true));

        bool hot     = resizing || r.Contains(Event.current.mousePosition);
        bool hovered = r.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(r,
            resizing ? new Color(0.4f, 0.7f, 1f, 0.9f) :
            hovered  ? new Color(0.6f, 0.6f, 0.6f, 0.6f) :
                       new Color(0.2f, 0.2f, 0.2f, 0.5f));
        EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);

        Event e = Event.current;
        switch (e.type)
        {
            case EventType.MouseDown:
                if (r.Contains(e.mousePosition))
                {
                    resizing = true;
                    e.Use();
                }
                break;
            case EventType.MouseDrag:
                if (resizing)
                {
                    panelWidth = Mathf.Clamp(panelWidth + e.delta.x * sign, PANEL_MIN_W, PANEL_MAX_W);
                    EditorPrefs.SetFloat(sign > 0 ? K_LeftW : K_RightW, panelWidth);
                    Repaint();
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (resizing)
                {
                    resizing = false;
                    e.Use();
                }
                break;
        }
    }

    // ── Toolbar ───────────────────────────────────────────────────
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUI.BeginChangeCheck();
        var newData = (LevelSelectDesignerData)EditorGUILayout.ObjectField(
            _data, typeof(LevelSelectDesignerData), false, GUILayout.Width(220));
        if (EditorGUI.EndChangeCheck())
        {
            _data = newData;
            if (_data != null)
                EditorPrefs.SetString(K_DataPath, AssetDatabase.GetAssetPath(_data));
        }

        if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(36)))
            CreateNewDataAsset();

        GUILayout.Space(12);

        string[] modeLabels = { "Draw", "Select", "Junction", "Arena", "Obstacle", "Shop", "Landscape" };
        var newMode = (DesignerMode)GUILayout.Toolbar((int)_mode, modeLabels,
            EditorStyles.toolbarButton, GUILayout.Height(18));
        if (newMode != _mode)
        {
            _mode = newMode;
            _isDrawing = false;
            _drawingNodeIds.Clear();
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Frame All", EditorStyles.toolbarButton, GUILayout.Width(64)))
            FrameAll();

        EditorGUILayout.EndHorizontal();
    }

    private void CreateNewDataAsset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "New Designer Data", "LevelSelectDesignerData", "asset",
            "Choose location", "Assets");
        if (string.IsNullOrEmpty(path)) return;
        _data = CreateInstance<LevelSelectDesignerData>();
        TryAutoFillPrefabs();
        AssetDatabase.CreateAsset(_data, path);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetString(K_DataPath, path);
    }

    private const string PrefabDir = "Assets/Prefab/LevelSelectPrefabs";

    private void TryAutoFillPrefabs()
    {
        bool dirty = false;

        // Hard-coded known prefabs / assets
        dirty |= TryFill(ref _data.riverBlockPrefab,      "RiverRunBlock1");
        dirty |= TryFill(ref _data.junctionScriptObject,  "LevelSelectJunctionScriptObject");
        dirty |= TryFill(ref _data.arenaEntrancePrefab,   "LEVELSELECTARENAENTRANCE");


        // Scan directory for junction / arena / obstacle / shop by name pattern
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            string name      = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLower();
            var    prefab    = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) continue;

            if (_data.junctionRightFacingPrefab == null && name.Contains("junction") && name.Contains("down"))  { _data.junctionRightFacingPrefab = prefab; dirty = true; }
            if (_data.junctionLeftFacingPrefab   == null && name.Contains("junction") && name.Contains("up"))    { _data.junctionLeftFacingPrefab   = prefab; dirty = true; }
            if (_data.junctionPrefab  == null && name.Contains("junction") && !name.Contains("down") && !name.Contains("up")) { _data.junctionPrefab = prefab; dirty = true; }
            if (_data.arenaPrefab     == null && name.Contains("arena"))      { _data.arenaPrefab     = prefab; dirty = true; }
            if (_data.obstaclePrefab  == null && (name.Contains("obstacle") || name.Contains("gate"))) { _data.obstaclePrefab = prefab; dirty = true; }
            if (_data.shopPrefab      == null && name.Contains("shop"))       { _data.shopPrefab      = prefab; dirty = true; }
        }

        if (dirty) EditorUtility.SetDirty(_data);
    }

    private static bool TryFill(ref GameObject field, string prefabName)
    {
        if (field != null) return false;
        string[] guids = AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { PrefabDir });
        if (guids.Length == 0) return false;
        field = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
        return field != null;
    }

    private void FrameAll()
    {
        if (_data == null || _data.nodes.Count == 0) return;
        var bounds = new Bounds(new Vector3(_data.nodes[0].worldPosition.x, 0, _data.nodes[0].worldPosition.z), Vector3.zero);
        foreach (var n in _data.nodes)
            bounds.Encapsulate(new Vector3(n.worldPosition.x, 0, n.worldPosition.z));
        _viewCenter = new Vector2(bounds.center.x, bounds.center.z);
        float extentX = bounds.extents.x + 5f;
        float extentZ = bounds.extents.z + 5f;
        float canvasW = position.width - _leftPanelWidth - _rightPanelWidth;
        float canvasH = position.height - EditorGUIUtility.singleLineHeight - 4f;
        _zoom = Mathf.Min(canvasW / (2f * extentX + 1f), canvasH / (2f * extentZ + 1f));
        _zoom = Mathf.Clamp(_zoom, 1f, 200f);
        SaveViewPrefs();
        Repaint();
    }

    // ══════════════════════════════════════════════════════════════
    // LEFT PANEL
    // ══════════════════════════════════════════════════════════════
    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth), GUILayout.ExpandHeight(true));
        _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

        if (_data == null)
        {
            EditorGUILayout.HelpBox("Load or create a LevelSelectDesignerData asset.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        DrawSelectedPathProps();
        DrawSelectedObstacleProps();
        DrawSelectedArenaProps();
        DrawCanvasSettingsSection();

        if (_mode == DesignerMode.Landscape)
            DrawLandscapePanel();

        GUILayout.FlexibleSpace();
        DrawActionButtons();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawPathList()
    {
        EditorGUILayout.LabelField("Paths", EditorStyles.boldLabel);

        for (int i = 0; i < _data.paths.Count; i++)
        {
            var path     = _data.paths[i];
            bool selected = path.pathId == _selectedPathId;

            EditorGUILayout.BeginHorizontal();

            // Selection dot — keeps background as the path colour, avoids colour confusion
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = Color.Lerp(path.editorColor, Color.black, 0.3f);
            string label = (selected ? "● " : "  ")
                + (string.IsNullOrEmpty(path.segmentId) ? "(unnamed)" : path.segmentId);
            if (GUILayout.Button(label, EditorStyles.miniButton))
            {
                _selectedPathId = selected ? null : path.pathId;
                _selectedNodeId = null;
            }
            GUI.backgroundColor = prevBg;

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                Undo.RecordObject(_data, "Delete Path");
                DeletePath(path.pathId);
                if (_selectedPathId == path.pathId) _selectedPathId = null;
                EditorUtility.SetDirty(_data);
                GUI.backgroundColor = prevBg;
                break;
            }
            GUI.backgroundColor = prevBg;

            EditorGUILayout.EndHorizontal();
        }

        var newPathContent = new GUIContent("+ New Path",
            "Draw mode controls:\n" +
            "• Click to place a knot\n" +
            "• Snap to existing node to connect paths\n" +
            "• Double-click or Enter to finish\n" +
            "• Escape to cancel");
        if (GUILayout.Button(newPathContent))
        {
            Undo.RecordObject(_data, "New Path");
            var p = new LevelSelectDesignerData.DesignerPath
            {
                pathId    = Guid.NewGuid().ToString(),
                segmentId = $"Segment_{_data.paths.Count:00}",
                editorColor = Color.HSVToRGB((_data.paths.Count * 0.618f) % 1f, 0.7f, 0.9f)
            };
            _data.paths.Add(p);
            _selectedPathId = p.pathId;
            EditorUtility.SetDirty(_data);
        }
    }

    private void DrawSelectedPathProps()
    {
        var path = _data.paths.Find(p => p.pathId == _selectedPathId);
        if (path == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Path Properties", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        path.segmentId    = EditorGUILayout.TextField("Segment ID",    path.segmentId);
        path.riverName    = EditorGUILayout.TextField("River Name",    path.riverName);
        path.segmentType  = (LevelSelectDesignerData.SegmentType)EditorGUILayout.EnumPopup("Type", path.segmentType);
        path.isLeftPath    = EditorGUILayout.Toggle("Is Left Path",      path.isLeftPath);
        path.isRightPath = EditorGUILayout.Toggle("Is Right Path",   path.isRightPath);
        path.editorColor  = EditorGUILayout.ColorField("Color",        path.editorColor);
        EditorGUILayout.Space(2);
        path.leadsToArena  = EditorGUILayout.Toggle("Leads to Arena",  path.leadsToArena);
        path.arenaIsAtEnd  = EditorGUILayout.Toggle("Arena at End",    path.arenaIsAtEnd);
        path.extrudeOnExit = EditorGUILayout.Toggle("Extrude on Exit", path.extrudeOnExit);

        EditorGUILayout.LabelField($"Knots: {path.nodeIds.Count}", EditorStyles.miniLabel);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Path");
            EditorUtility.SetDirty(_data);
        }

        // ── Arena info ────────────────────────────────────────────
        if (path.leadsToArena && path.nodeIds.Count > 0)
        {
            string arenaNodeId = path.arenaIsAtEnd
                ? path.nodeIds[path.nodeIds.Count - 1]
                : path.nodeIds[0];
            var arena = _data.arenas.Find(a => a.nodeId == arenaNodeId);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Arena", EditorStyles.boldLabel);

            if (arena == null)
            {
                EditorGUILayout.HelpBox("No arena assigned at path endpoint.", MessageType.Warning);
            }
            else
            {
                if (arena.gridData != null)
                {
                    EditorGUILayout.LabelField(arena.gridData.displayName, EditorStyles.whiteBoldLabel);
                    EditorGUILayout.LabelField($"Entrances: {arena.gridData.entrances?.Count ?? 0}", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("Arena has no GridData assigned.", MessageType.Info);
                }

                EditorGUI.BeginChangeCheck();
                arena.gridData = (GridData)EditorGUILayout.ObjectField(
                    "GridData", arena.gridData, typeof(GridData), false);
                arena.arenaPrefabOverride = (GameObject)EditorGUILayout.ObjectField(
                    "Prefab Override", arena.arenaPrefabOverride, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Edit Arena");
                    EditorUtility.SetDirty(_data);
                }

                if (GUILayout.Button("Select Arena in List"))
                {
                    _selectedArenaNodeId = arena.nodeId;
                    _foldArenas = true;
                }
            }
        }
    }

    private void DrawSelectedObstacleProps()
    {
        var obs = _data.obstacles.Find(o => o.obstacleId == _selectedObstacleId);
        if (obs == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Obstacle Gate", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        obs.obstacleId     = EditorGUILayout.TextField("ID",           obs.obstacleId);
        obs.soulSlotCount  = EditorGUILayout.IntField("Soul Slots",    obs.soulSlotCount);
        obs.obstaclePrefab = (GameObject)EditorGUILayout.ObjectField(
            "Prefab Override", obs.obstaclePrefab, typeof(GameObject), false);

        var siblings = _data.obstacles.Where(o => o.pathId == obs.pathId).OrderBy(o => o.pathT).ToList();
        int idx = siblings.IndexOf(obs);
        EditorGUILayout.LabelField($"Chain order: {idx + 1} of {siblings.Count}", EditorStyles.miniLabel);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Obstacle");
            EditorUtility.SetDirty(_data);
        }

        EditorGUILayout.Space(2);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("Delete Obstacle"))
        {
            Undo.RecordObject(_data, "Delete Obstacle");
            _data.obstacles.Remove(obs);
            _selectedObstacleId = null;
            EditorUtility.SetDirty(_data);
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawSelectedArenaProps()
    {
        var arena = _data.arenas.Find(a => a.nodeId == _selectedArenaNodeId);
        if (arena == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Arena", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        arena.gridData = (GridData)EditorGUILayout.ObjectField(
            "GridData", arena.gridData, typeof(GridData), false);
        arena.arenaPrefabOverride = (GameObject)EditorGUILayout.ObjectField(
            "Prefab Override", arena.arenaPrefabOverride, typeof(GameObject), false);

        if (arena.gridData != null)
        {
            EditorGUILayout.LabelField(
                $"{arena.gridData.displayName}  |  {arena.gridData.entrances?.Count ?? 0} entrance(s)",
                EditorStyles.miniLabel);
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Arena");
            _selectedEntranceIdx = -1;
            EditorUtility.SetDirty(_data);
        }

        // ── Entrance list ─────────────────────────────────────────
        var entrances = arena.gridData?.entrances;
        if (entrances != null && entrances.Count > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Entrances", EditorStyles.boldLabel);

            for (int i = 0; i < entrances.Count; i++)
            {
                bool isBranchEntry = i == arena.entranceIndex;
                bool isSelected    = i == _selectedEntranceIdx;

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = isSelected    ? Color.yellow :
                                      isBranchEntry ? new Color(1f, 0.6f, 0.1f) : Color.clear;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = prevBg;

                EditorGUILayout.BeginHorizontal();
                string label = $"Entrance {i}" + (isBranchEntry ? "  ← branch" : "");
                if (GUILayout.Button(label, EditorStyles.miniLabel))
                {
                    _selectedEntranceIdx = isSelected ? -1 : i;
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();

                // Mark which entrance connects to the branch
                EditorGUI.BeginChangeCheck();
                bool isBranch = EditorGUILayout.Toggle("Branch Entrance", isBranchEntry);
                if (EditorGUI.EndChangeCheck() && isBranch)
                {
                    Undo.RecordObject(_data, "Set Branch Entrance");
                    arena.entranceIndex = i;
                    EditorUtility.SetDirty(_data);
                }

                EditorGUILayout.EndVertical();
            }
        }
    }

    private void DrawSceneRefsSection()
    {
        EditorGUILayout.Space(2);
        EditorGUI.BeginChangeCheck();
        _data.landscapeTool       = (LandscapeTool)EditorGUILayout.ObjectField(
            "Landscape Tool", _data.landscapeTool, typeof(LandscapeTool), true);
        _data.landscapeTilePrefab = (GameObject)EditorGUILayout.ObjectField(
            "  Tile Prefab", _data.landscapeTilePrefab, typeof(GameObject), false);
        _data.boatPathManager = (SplinePathStitcher)EditorGUILayout.ObjectField(
            "Boat Path Manager", _data.boatPathManager, typeof(SplinePathStitcher), false);
        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_data);
    }

    private void DrawPrefabsSection()
    {
        EditorGUILayout.Space(4);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Split Preset", GUILayout.Width(100));
        _splitPreset = (SplineSplitterPreset)EditorGUILayout.ObjectField(_splitPreset, typeof(SplineSplitterPreset), false);
        EditorGUILayout.EndHorizontal();
        _data.junctionScriptObject     = (GameObject)EditorGUILayout.ObjectField("Junction Script Obj", _data.junctionScriptObject,    typeof(GameObject), false);
        _data.junctionRightFacingPrefab = (GameObject)EditorGUILayout.ObjectField("Junction Right",        _data.junctionRightFacingPrefab, typeof(GameObject), false);
        _data.junctionLeftFacingPrefab   = (GameObject)EditorGUILayout.ObjectField("Junction Left",          _data.junctionLeftFacingPrefab,   typeof(GameObject), false);
        _data.junctionPrefab           = (GameObject)EditorGUILayout.ObjectField("Junction (fallback)",  _data.junctionPrefab,           typeof(GameObject), false);
        _data.arenaPrefab         = (GameObject)EditorGUILayout.ObjectField("Arena Head",     _data.arenaPrefab,         typeof(GameObject), false);
        _data.arenaEntrancePrefab = (GameObject)EditorGUILayout.ObjectField("Arena Entrance", _data.arenaEntrancePrefab, typeof(GameObject), false);
        _data.arenaRadius         = EditorGUILayout.FloatField("Arena Radius", _data.arenaRadius);
        _data.obstaclePrefab   = (GameObject)EditorGUILayout.ObjectField("Obstacle",  _data.obstaclePrefab,   typeof(GameObject), false);
        _data.shopPrefab       = (GameObject)EditorGUILayout.ObjectField("Shop",      _data.shopPrefab,       typeof(GameObject), false);
        _data.riverBlockPrefab = (GameObject)EditorGUILayout.ObjectField("Block",     _data.riverBlockPrefab, typeof(GameObject), false);
        _data.junctionGapPadding       = EditorGUILayout.FloatField("Junction Gap",  _data.junctionGapPadding);
        _data.splineInstantiateSpacing = EditorGUILayout.FloatField("Block Spacing", _data.splineInstantiateSpacing);
        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_data);
    }

    private void DrawCanvasSettingsSection()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Canvas", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _data.canvasWorldY     = EditorGUILayout.FloatField("World Y", _data.canvasWorldY);
        _data.curveSubdivisions  = EditorGUILayout.IntSlider("Curve Subdivisions", _data.curveSubdivisions, 1, 60);
        _data.branchStartOffset  = EditorGUILayout.Slider("Branch Start Offset", _data.branchStartOffset, -5f, 5f);
        _data.arenaHeadOffset    = EditorGUILayout.FloatField("Arena Head Offset", _data.arenaHeadOffset);
        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_data);
    }

    private void DrawActionButtons()
    {
        EditorGUILayout.Space(6);
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
        if (GUILayout.Button("GENERATE", GUILayout.Height(30)))
        {
            PruneLooseNodes();
            Generate();
        }
        GUI.backgroundColor = prevColor;

        if (GUILayout.Button("Respawn", GUILayout.Height(24)))
        {
            PruneLooseNodes();
            ClearGeneratedObjects();
            Generate();
        }

        if (GUILayout.Button("Clear Generated", GUILayout.Height(24)))
        {
            PruneLooseNodes();
            ClearGeneratedObjects();
        }

        if (GUILayout.Button("Clear Loose Nodes", GUILayout.Height(24)))
        {
            Undo.RecordObject(_data, "Clear Loose Nodes");
            PruneLooseNodes();
        }

        EditorGUILayout.Space(4);
        GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
        if (GUILayout.Button("Clear All", GUILayout.Height(24)))
        {
            if (EditorUtility.DisplayDialog(
                "Clear All",
                "This will delete all paths, nodes, junctions, obstacles, arenas and shops from the data asset, and clear all generated scene objects.\n\nAre you sure?",
                "Clear All", "Cancel"))
            {
                Undo.RecordObject(_data, "Clear All");
                _data.nodes.Clear();
                _data.paths.Clear();
                _data.junctions.Clear();
                _data.obstacles.Clear();
                _data.arenas.Clear();
                _data.shops.Clear();
                _selectedPathId     = null;
                _selectedNodeId     = null;
                _selectedObstacleId = null;
                _selectedArenaNodeId = null;
                _isDrawing          = false;
                _drawingNodeIds.Clear();
                EditorUtility.SetDirty(_data);
                ClearGeneratedObjects();
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);
    }

    // ══════════════════════════════════════════════════════════════
    // CANVAS
    // ══════════════════════════════════════════════════════════════
    private void DrawCanvas()
    {
        _canvasRect = GUILayoutUtility.GetRect(0, 0,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(_canvasRect, new Color(0.15f, 0.15f, 0.15f));

            if (_canvasFocused)
            {
                const float B = 2f;
                var col = new Color(0.3f, 0.7f, 1f, 0.9f);
                EditorGUI.DrawRect(new Rect(_canvasRect.x, _canvasRect.y, _canvasRect.width, B), col);
                EditorGUI.DrawRect(new Rect(_canvasRect.x, _canvasRect.yMax - B, _canvasRect.width, B), col);
                EditorGUI.DrawRect(new Rect(_canvasRect.x, _canvasRect.y, B, _canvasRect.height), col);
                EditorGUI.DrawRect(new Rect(_canvasRect.xMax - B, _canvasRect.y, B, _canvasRect.height), col);
            }
        }

        if (_canvasRect.width < 10) return;

        DrawCanvasGrid();

        if (_data != null)
        {
            HandleCanvasEvents();

            Handles.BeginGUI();
            DrawLandscapeTilesOnCanvas();
            DrawPaths();
            DrawNodes();
            DrawObstacles();
            DrawArenaEntrances();
            DrawInProgressPath();
            Handles.EndGUI();
        }

        // Mode hint
        if (Event.current.type == EventType.Repaint)
        {
            string hint = _mode switch
            {
                DesignerMode.Draw     => _isDrawing ? "Click to place knot — Enter/double-click to finish — Esc to cancel" : "Click to start drawing a path",
                DesignerMode.Select   => "Click node or path to select — drag node to move — Delete to remove",
                DesignerMode.Junction => "Click a node to toggle JunctionSplit",
                DesignerMode.Arena    => "Click an endpoint node to toggle ArenaEnd",
                DesignerMode.Obstacle => "Click along a path to place an obstacle gate",
                DesignerMode.Shop      => "Click an endpoint node to toggle ShopEnd",
                DesignerMode.Landscape => "Landscape mode — set tile prefab & counts in left panel, then Generate Tiles",
                _ => ""
            };
            var style = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            GUI.Label(new Rect(_canvasRect.x + 6, _canvasRect.yMax - 20, _canvasRect.width - 12, 18), hint, style);
        }
    }

    private void DrawCanvasGrid()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_zoom < 3f) return;

        float worldStep = ChooseGridStep();
        Vector2 topLeft  = CanvasToWorld2D(_canvasRect.min);
        Vector2 botRight = CanvasToWorld2D(_canvasRect.max);

        Handles.BeginGUI();

        Handles.color = new Color(1f, 1f, 1f, 0.05f);
        float startX = Mathf.Floor(topLeft.x / worldStep) * worldStep;
        float startZ = Mathf.Floor(topLeft.y / worldStep) * worldStep;

        for (float x = startX; x <= botRight.x + worldStep; x += worldStep)
            Handles.DrawLine(WorldToCanvas(new Vector3(x, 0, topLeft.y)), WorldToCanvas(new Vector3(x, 0, botRight.y)));
        for (float z = startZ; z <= botRight.y + worldStep; z += worldStep)
            Handles.DrawLine(WorldToCanvas(new Vector3(topLeft.x, 0, z)), WorldToCanvas(new Vector3(botRight.x, 0, z)));

        // Origin axes
        Handles.color = new Color(1f, 1f, 1f, 0.18f);
        Handles.DrawLine(WorldToCanvas(new Vector3(0, 0, topLeft.y)),  WorldToCanvas(new Vector3(0, 0, botRight.y)));
        Handles.DrawLine(WorldToCanvas(new Vector3(topLeft.x, 0, 0)),  WorldToCanvas(new Vector3(botRight.x, 0, 0)));

        Handles.EndGUI();
    }

    private float ChooseGridStep()
    {
        float raw = 80f / _zoom;
        float mag = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(raw)));
        float n   = raw / mag;
        return n < 2f ? mag : n < 5f ? 2f * mag : 5f * mag;
    }

    // ── Canvas events ─────────────────────────────────────────────
    private void HandleCanvasEvents()
    {
        Event e = Event.current;

        bool inCanvas = _canvasRect.Contains(e.mousePosition);

        // Claim / release sticky keyboard focus
        if (e.type == EventType.MouseDown)
        {
            if (inCanvas)
            {
                _canvasFocused = true;
                GUIUtility.keyboardControl = 0; // pull focus away from any text field
                Repaint();
            }
            else if (!inCanvas)
            {
                _canvasFocused = false;
                Repaint();
            }
        }

        // Track Space key — consume it so it doesn't trigger Unity shortcuts
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space && inCanvas)
        {
            _spaceHeld = true;
            Repaint();
            e.Use();
        }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space && !inCanvas)
            _spaceHeld = false; // safety: lost focus

        if (e.type == EventType.KeyUp && e.keyCode == KeyCode.Space)
        {
            _spaceHeld = false;
            Repaint();
            e.Use();
        }

        if (!inCanvas && !_canvasFocused && !_isDraggingNode && !_isPanning) return;

        // Space + left-drag  OR  middle-mouse: pan
        bool startSpacePan  = _spaceHeld && e.type == EventType.MouseDown && e.button == 0 && inCanvas;
        bool startMiddlePan = e.type == EventType.MouseDown && e.button == 2 && inCanvas;

        if (startSpacePan || startMiddlePan)
        {
            _isPanning = true;
            _panStart  = e.mousePosition;
            _viewCenterAtPanStart = _viewCenter;
            e.Use();
        }
        if (e.type == EventType.MouseDrag && _isPanning)
        {
            _viewCenter = _viewCenterAtPanStart - (e.mousePosition - _panStart) / _zoom;
            SaveViewPrefs();
            Repaint();
            e.Use();
        }
        if (e.type == EventType.MouseUp && _isPanning)
        {
            _isPanning = false;
            e.Use();
        }

        // Zoom: scroll wheel
        if (e.type == EventType.ScrollWheel && inCanvas)
        {
            Vector2 worldBefore = CanvasToWorld2D(e.mousePosition);
            _zoom *= 1f - e.delta.y * 0.05f;
            _zoom  = Mathf.Clamp(_zoom, 0.5f, 400f);
            Vector2 worldAfter = CanvasToWorld2D(e.mousePosition);
            _viewCenter += worldBefore - worldAfter;
            SaveViewPrefs();
            Repaint();
            e.Use();
        }

        // Hand cursor while Space is held
        if (_spaceHeld && inCanvas)
            EditorGUIUtility.AddCursorRect(_canvasRect,
                _isPanning ? MouseCursor.Pan : MouseCursor.Link);

        // Don't dispatch to mode handlers while space-panning
        if (_spaceHeld || _isPanning) return;

        if (!inCanvas && !_isDraggingNode) return;

        switch (_mode)
        {
            case DesignerMode.Draw:     HandleDrawMode(e);     break;
            case DesignerMode.Select:   HandleSelectMode(e);   break;
            case DesignerMode.Junction: HandleJunctionMode(e); break;
            case DesignerMode.Arena:    HandleArenaMode(e);    break;
            case DesignerMode.Obstacle: HandleObstacleMode(e); break;
            case DesignerMode.Shop:      HandleShopMode(e);      break;
            case DesignerMode.Landscape: HandleLandscapeMode(e); break;
        }
    }

    // ── Draw mode ─────────────────────────────────────────────────
    private void HandleDrawMode(Event e)
    {
        // Double-click or Enter: finish path
        if (_isDrawing)
        {
            if ((e.type == EventType.MouseDown && e.button == 0 && e.clickCount >= 2) ||
                (e.type == EventType.KeyDown   && e.keyCode == KeyCode.Return))
            {
                FinishDrawing();
                e.Use();
                return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _isDrawing       = false;
                _isExtending     = false;
                _extendingPathId = null;
                _drawingNodeIds.Clear();
                Repaint();
                e.Use();
                return;
            }
        }

        if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 1)
        {
            Undo.RecordObject(_data, "Draw Path");
            Vector3 worldPos = CanvasToWorldPos(e.mousePosition);
            string  snapId   = FindSnapNode(e.mousePosition);

            if (!_isDrawing)
            {
                if (snapId != null)
                {
                    // If snapping to the last node of any existing path, extend that path
                    var extPath = _data.paths.Find(p =>
                        p.nodeIds.Count > 0 && p.nodeIds[p.nodeIds.Count - 1] == snapId);
                    if (extPath != null)
                    {
                        _isExtending     = true;
                        _extendingPathId = extPath.pathId;
                        _selectedPathId  = extPath.pathId;
                    }
                    else
                    {
                        _isExtending     = false;
                        _extendingPathId = null;
                    }

                    _drawingNodeIds.Clear();
                    _drawingNodeIds.Add(snapId);
                    _isDrawing = true;
                }
                else
                {
                    var (hitPathId, hitSegIdx) = FindPathAndSegmentAtCanvas(e.mousePosition);
                    if (hitPathId != null)
                    {
                        // Insert a bend node into the existing path segment
                        var hitPath = _data.paths.Find(p => p.pathId == hitPathId);
                        var newNode = AddNode(worldPos, LevelSelectDesignerData.NodeType.Waypoint);
                        hitPath.nodeIds.Insert(hitSegIdx + 1, newNode.id);
                        // Don't start drawing — user can drag the node in Select mode
                    }
                    else
                    {
                        // Start a new free path
                        _drawingNodeIds.Clear();
                        _drawingNodeIds.Add(AddNode(worldPos, LevelSelectDesignerData.NodeType.Waypoint).id);
                        _isDrawing = true;
                    }
                }
            }
            else
            {
                string nodeId;
                if (snapId != null && snapId != _drawingNodeIds[_drawingNodeIds.Count - 1])
                {
                    nodeId = snapId;
                    _drawingNodeIds.Add(nodeId);
                    FinishDrawing();
                    e.Use();
                    return;
                }
                else
                {
                    nodeId = AddNode(worldPos, LevelSelectDesignerData.NodeType.Waypoint).id;
                    _drawingNodeIds.Add(nodeId);
                }
            }

            EditorUtility.SetDirty(_data);
            Repaint();
            e.Use();
        }

        // Repaint for ghost line
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            Repaint();
    }

    private void FinishDrawing()
    {
        if (_drawingNodeIds.Count < 2)
        {
            // Remove the lone node
            if (_drawingNodeIds.Count == 1)
                _data.nodes.RemoveAll(n => n.id == _drawingNodeIds[0]);
            _isDrawing = false;
            _drawingNodeIds.Clear();
            return;
        }

        // Extending an existing path — append new nodes (skip the shared start node)
        if (_isExtending && !string.IsNullOrEmpty(_extendingPathId))
        {
            var extPath = _data.paths.Find(p => p.pathId == _extendingPathId);
            if (extPath != null)
            {
                foreach (var nid in _drawingNodeIds.Skip(1))
                    extPath.nodeIds.Add(nid);
                _selectedPathId  = extPath.pathId;
                _isDrawing       = false;
                _isExtending     = false;
                _extendingPathId = null;
                _drawingNodeIds.Clear();
                EditorUtility.SetDirty(_data);
                Repaint();
                return;
            }
            _isExtending     = false;
            _extendingPathId = null;
        }

        // If the drawn path ends at the FIRST node of an existing path, merge into it
        // rather than creating a separate connector. This handles joining a junction to
        // a dangling path left over from a deleted junction.
        string endNodeId   = _drawingNodeIds[_drawingNodeIds.Count - 1];
        string startNodeId = _drawingNodeIds[0];
        var existingPath = _data.paths.Find(p =>
            p.nodeIds.Count > 0 && p.nodeIds[0] == endNodeId);

        LevelSelectDesignerData.DesignerPath path;
        if (existingPath != null)
        {
            // Prepend all drawn nodes (except the shared end node) into the existing path
            var prefix = _drawingNodeIds.Take(_drawingNodeIds.Count - 1).ToList();
            for (int i = prefix.Count - 1; i >= 0; i--)
                existingPath.nodeIds.Insert(0, prefix[i]);
            path = existingPath;
        }
        else
        {
            string sid = AutoSegmentId();
            path = new LevelSelectDesignerData.DesignerPath
            {
                pathId      = Guid.NewGuid().ToString(),
                segmentId   = sid,
                riverName   = sid,
                nodeIds     = new List<string>(_drawingNodeIds),
                editorColor = Color.HSVToRGB((_data.paths.Count * 0.618f) % 1f, 0.7f, 0.9f)
            };
            _data.paths.Add(path);
        }

        // If this path starts at a junction node, record it as the branch on that junction
        var junc = _data.junctions.Find(j => j.nodeId == startNodeId);
        if (junc != null)
        {
            junc.branchPathId = path.pathId;
            if (!junc.pathIds.Contains(path.pathId))
                junc.pathIds.Add(path.pathId);
        }

        _selectedPathId = path.pathId;
        _isDrawing      = false;
        _drawingNodeIds.Clear();
        EditorUtility.SetDirty(_data);
        Repaint();
    }

    // ── Select mode ───────────────────────────────────────────────
    private void HandleSelectMode(Event e)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            string nodeId = FindNodeAtCanvas(e.mousePosition);
            if (nodeId != null)
            {
                _selectedNodeId     = nodeId;
                _selectedObstacleId = null;
                _isDraggingNode     = true;
                _draggingNodeId     = nodeId;
                var node            = _data.nodes.Find(n => n.id == nodeId);
                _dragOffset         = e.mousePosition - (Vector2)WorldToCanvas(node.worldPosition);
                _selectedPathId     = _data.paths.Find(p => p.nodeIds.Contains(nodeId))?.pathId;
                _selectedArenaNodeId = node?.type == LevelSelectDesignerData.NodeType.ArenaEnd ? nodeId : null;
                _selectedEntranceIdx = -1;
                Repaint();
                e.Use();
                return;
            }

            string obsId = FindObstacleAtCanvas(e.mousePosition);
            if (obsId != null)
            {
                _selectedObstacleId = obsId;
                _selectedNodeId     = null;
                Repaint();
                e.Use();
                return;
            }

            string pathId = FindPathAtCanvas(e.mousePosition);
            _selectedPathId     = pathId;
            _selectedNodeId     = null;
            _selectedObstacleId = null;
            Repaint();
            e.Use();
        }

        if (e.type == EventType.MouseDrag && _isDraggingNode)
        {
            var node = _data.nodes.Find(n => n.id == _draggingNodeId);
            if (node != null)
            {
                Undo.RecordObject(_data, "Move Node");
                node.worldPosition = CanvasToWorldPos(e.mousePosition - _dragOffset);
                EditorUtility.SetDirty(_data);
                Repaint();
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp && _isDraggingNode)
        {
            _isDraggingNode = false;
            _draggingNodeId = null;
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
        {
            if (_selectedNodeId != null)
            {
                Undo.RecordObject(_data, "Delete Node");
                DeleteNode(_selectedNodeId);
                _selectedNodeId = null;
                EditorUtility.SetDirty(_data);
                Repaint();
                e.Use();
            }
            else if (_selectedPathId != null)
            {
                Undo.RecordObject(_data, "Delete Path");
                DeletePath(_selectedPathId);
                _selectedPathId = null;
                EditorUtility.SetDirty(_data);
                Repaint();
                e.Use();
            }
        }
    }

    // ── Junction mode ─────────────────────────────────────────────
    // Phase 1 (not drawing): click on a path to insert junction and begin branch.
    // Phase 2 (drawing):     subsequent clicks extend the branch; Enter/double-click finishes it.
    private void HandleJunctionMode(Event e)
    {
        // ── Phase 2: already drawing a branch ────────────────────
        if (_isDrawing)
        {
            // Finish
            if ((e.type == EventType.MouseDown && e.button == 0 && e.clickCount >= 2) ||
                (e.type == EventType.KeyDown   && e.keyCode == KeyCode.Return))
            {
                FinishDrawing();
                e.Use();
                return;
            }
            // Cancel
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _isDrawing = false;
                _drawingNodeIds.Clear();
                Repaint();
                e.Use();
                return;
            }
            // Place next knot
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 1)
            {
                Undo.RecordObject(_data, "Draw Branch");
                string snapId = FindSnapNode(e.mousePosition);
                if (snapId != null && snapId != _drawingNodeIds[_drawingNodeIds.Count - 1])
                {
                    _drawingNodeIds.Add(snapId);
                    FinishDrawing();
                }
                else
                {
                    var node = AddNode(CanvasToWorldPos(e.mousePosition), LevelSelectDesignerData.NodeType.Waypoint);
                    _drawingNodeIds.Add(node.id);
                    EditorUtility.SetDirty(_data);
                }
                Repaint();
                e.Use();
            }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                Repaint();
            return;
        }

        if (e.type != EventType.MouseDown || e.button != 0) return;

        // ── Click on existing junction diamond → remove it ────────
        string existingNodeId = FindNodeAtCanvas(e.mousePosition);
        if (existingNodeId != null)
        {
            var existingNode = _data.nodes.Find(n => n.id == existingNodeId);
            if (existingNode.type == LevelSelectDesignerData.NodeType.JunctionSplit)
            {
                Undo.RecordObject(_data, "Remove Junction");
                existingNode.type = LevelSelectDesignerData.NodeType.Waypoint;
                _data.junctions.RemoveAll(j => j.nodeId == existingNodeId);
                EditorUtility.SetDirty(_data);
                Repaint();
                e.Use();
            }
            return;
        }

        // ── Phase 1: click on a path → insert junction, start branch
        string pathId = FindPathAtCanvas(e.mousePosition);
        if (pathId == null) return;

        var path = _data.paths.Find(p => p.pathId == pathId);
        float t  = FindTOnPath(path, e.mousePosition);

        int segments = path.nodeIds.Count - 1;
        int segIdx   = Mathf.Min(Mathf.FloorToInt(t * segments), segments - 1);
        Vector3 worldPos = GetWorldPosOnPathRaw(path, t);

        Undo.RecordObject(_data, "Insert Junction");
        var junctionNode = AddNode(worldPos, LevelSelectDesignerData.NodeType.JunctionSplit);
        path.nodeIds.Insert(segIdx + 1, junctionNode.id);

        _data.junctions.Add(new LevelSelectDesignerData.DesignerJunction
        {
            junctionId   = Guid.NewGuid().ToString(),
            nodeId       = junctionNode.id,
            pathIds      = new List<string> { pathId },
            riverPathId  = pathId
        });

        // Immediately begin drawing the branch from the junction node
        _drawingNodeIds.Clear();
        _drawingNodeIds.Add(junctionNode.id);
        _isDrawing = true;

        EditorUtility.SetDirty(_data);
        Repaint();
        e.Use();
    }

    private void RemoveArena(string nodeId)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node != null) node.type = LevelSelectDesignerData.NodeType.Waypoint;
        _data.arenas.RemoveAll(a => a.nodeId == nodeId);
        if (_selectedArenaNodeId == nodeId) _selectedArenaNodeId = null;
        foreach (var p in _data.paths)
            if (p.nodeIds.Count > 0 && p.nodeIds[p.nodeIds.Count - 1] == nodeId)
                p.leadsToArena = false;
    }

    // ── Arena mode ────────────────────────────────────────────────
    private void HandleArenaMode(Event e)
    {
        if (e.type != EventType.MouseDown || e.button != 0) return;

        string nodeId = FindNodeAtCanvas(e.mousePosition);
        if (nodeId == null) return;

        Undo.RecordObject(_data, "Toggle Arena");
        var node = _data.nodes.Find(n => n.id == nodeId);

        if (node.type == LevelSelectDesignerData.NodeType.ArenaEnd)
        {
            RemoveArena(nodeId);
        }
        else
        {
            node.type = LevelSelectDesignerData.NodeType.ArenaEnd;
            if (!_data.arenas.Exists(a => a.nodeId == nodeId))
                _data.arenas.Add(new LevelSelectDesignerData.DesignerArena { nodeId = nodeId });
            _selectedArenaNodeId = nodeId;

            // Auto-set leadsToArena on any path ending at this node
            foreach (var p in _data.paths)
                if (p.nodeIds.Count > 0 && p.nodeIds[p.nodeIds.Count - 1] == nodeId)
                {
                    p.leadsToArena = true;
                    p.arenaIsAtEnd = true;
                }
        }

        EditorUtility.SetDirty(_data);
        Repaint();
        e.Use();
    }

    // ── Obstacle mode ─────────────────────────────────────────────
    private void HandleObstacleMode(Event e)
    {
        if (e.type != EventType.MouseDown || e.button != 0) return;

        string pathId = FindPathAtCanvas(e.mousePosition);
        if (pathId == null) return;

        var path = _data.paths.Find(p => p.pathId == pathId);
        float t  = FindTOnPath(path, e.mousePosition);

        Undo.RecordObject(_data, "Add Obstacle");
        string baseId = (path.segmentId ?? "gate").ToLower().Replace(" ", "_");
        int    num    = _data.obstacles.Count(o => o.pathId == pathId) + 1;

        var obs = new LevelSelectDesignerData.DesignerObstacle
        {
            obstacleId    = $"{baseId}_gate_{num:00}",
            pathId        = pathId,
            pathT         = t,
            soulSlotCount = 3
        };
        _data.obstacles.Add(obs);
        _selectedObstacleId = obs.obstacleId;
        EditorUtility.SetDirty(_data);
        Repaint();
        e.Use();
    }

    // ── Shop mode ─────────────────────────────────────────────────
    private void HandleShopMode(Event e)
    {
        if (e.type != EventType.MouseDown || e.button != 0) return;

        string nodeId = FindNodeAtCanvas(e.mousePosition);
        if (nodeId == null) return;

        Undo.RecordObject(_data, "Toggle Shop");
        var node = _data.nodes.Find(n => n.id == nodeId);

        if (node.type == LevelSelectDesignerData.NodeType.ShopEnd)
        {
            node.type = LevelSelectDesignerData.NodeType.Waypoint;
            _data.shops.RemoveAll(s => s.nodeId == nodeId);
        }
        else
        {
            node.type = LevelSelectDesignerData.NodeType.ShopEnd;
            if (!_data.shops.Exists(s => s.nodeId == nodeId))
                _data.shops.Add(new LevelSelectDesignerData.DesignerShop { nodeId = nodeId });
        }

        EditorUtility.SetDirty(_data);
        Repaint();
        e.Use();
    }

    // ══════════════════════════════════════════════════════════════
    // CANVAS DRAWING
    // ══════════════════════════════════════════════════════════════
    private void DrawPaths()
    {
        foreach (var path in _data.paths)
        {
            if (path.nodeIds.Count < 2) continue;

            bool  selected = path.pathId == _selectedPathId;
            Color col      = path.editorColor;
            float width    = selected ? 3f : 2f;

            for (int i = 0; i < path.nodeIds.Count - 1; i++)
            {
                var nodeA = _data.nodes.Find(n => n.id == path.nodeIds[i]);
                var nodeB = _data.nodes.Find(n => n.id == path.nodeIds[i + 1]);
                if (nodeA == null || nodeB == null) continue;

                Vector2 a = WorldToCanvas(nodeA.worldPosition);
                Vector2 b = WorldToCanvas(nodeB.worldPosition);

                Handles.color = selected ? Color.white : col;
                Handles.DrawAAPolyLine(width, (Vector3)a, (Vector3)b);

                // Direction arrow at midpoint
                Vector2 mid  = (a + b) * 0.5f;
                Vector2 dir  = (b - a).normalized * 7f;
                Vector2 perp = new Vector2(-dir.y, dir.x) * 0.4f;
                Handles.DrawAAPolyLine(width, (Vector3)mid, (Vector3)(mid + dir - perp));
                Handles.DrawAAPolyLine(width, (Vector3)mid, (Vector3)(mid + dir + perp));
            }
        }
    }

    private void DrawNodes()
    {
        // Two passes: waypoints/arenas/shops first, junctions on top
        foreach (var node in _data.nodes
            .Where(n => n.type != LevelSelectDesignerData.NodeType.JunctionSplit)
            .Concat(_data.nodes.Where(n => n.type == LevelSelectDesignerData.NodeType.JunctionSplit)))
        {
            Vector2 pos = WorldToCanvas(node.worldPosition);
            if (!_canvasRect.Contains(pos)) continue;

            bool selected = node.id == _selectedNodeId;
            float r       = NODE_RADIUS;

            switch (node.type)
            {
                case LevelSelectDesignerData.NodeType.JunctionSplit:
                    bool juncHighlighted = node.id == _selectedJunctionNodeId;
                    Handles.color = juncHighlighted ? new Color(1f, 0.85f, 0f) : Color.yellow;
                    DrawDiamond(pos, r + 2f);
                    if (selected)         { Handles.color = Color.white;                  DrawDiamond(pos, r + 5f); }
                    if (juncHighlighted)  { Handles.color = new Color(1f, 0.85f, 0f);    DrawDiamond(pos, r + 8f);
                                           Handles.DrawWireDisc(pos, Vector3.forward, r + 12f); }
                    break;

                case LevelSelectDesignerData.NodeType.ArenaEnd:
                    Handles.color = new Color(1f, 0.5f, 0.1f);
                    Handles.DrawSolidDisc(pos, Vector3.forward, r + 3f);
                    if (selected) { Handles.color = Color.white; Handles.DrawWireDisc(pos, Vector3.forward, r + 6f); }
                    var arena = _data.arenas.Find(a => a.nodeId == node.id);
                    Handles.color = Color.white;
                    Handles.Label(new Vector3(pos.x + r + 4f, pos.y - 6f, 0),
                        arena?.gridData != null ? arena.gridData.displayName : "ARENA", EditorStyles.miniLabel);
                    break;

                case LevelSelectDesignerData.NodeType.ShopEnd:
                    Handles.color = new Color(0.7f, 0.3f, 1f);
                    Handles.DrawSolidDisc(pos, Vector3.forward, r + 2f);
                    if (selected) { Handles.color = Color.white; Handles.DrawWireDisc(pos, Vector3.forward, r + 5f); }
                    Handles.color = Color.white;
                    Handles.Label(new Vector3(pos.x + r + 4f, pos.y - 6f, 0), "SHOP", EditorStyles.miniLabel);
                    break;

                default:
                    Handles.color = GetNodeColor(node.id);
                    Handles.DrawSolidDisc(pos, Vector3.forward, r);
                    if (selected) { Handles.color = Color.white; Handles.DrawWireDisc(pos, Vector3.forward, r + 4f); }
                    break;
            }
        }
    }

    private void DrawObstacles()
    {
        int order = 1;
        foreach (var obs in _data.obstacles.OrderBy(o => o.pathT))
        {
            var worldPos = GetWorldPosOnPath(obs.pathId, obs.pathT);
            if (!worldPos.HasValue) continue;

            Vector2 pos      = WorldToCanvas(worldPos.Value);
            if (!_canvasRect.Contains(pos)) continue;

            bool selected = obs.obstacleId == _selectedObstacleId;
            Handles.color = selected ? Color.white : new Color(1f, 0.6f, 0f);
            Handles.DrawSolidDisc(pos, Vector3.forward, OBSTACLE_RADIUS);
            Handles.color = Color.black;
            Handles.DrawAAPolyLine(2f,
                new Vector3(pos.x, pos.y - OBSTACLE_RADIUS - 2f, 0),
                new Vector3(pos.x, pos.y + OBSTACLE_RADIUS + 2f, 0));
            Handles.color = Color.white;
            Handles.Label(new Vector3(pos.x + OBSTACLE_RADIUS + 2f, pos.y - 6f, 0),
                order.ToString(), EditorStyles.miniLabel);
            order++;
        }
    }

    private void DrawArenaEntrances()
    {
        foreach (var arena in _data.arenas)
        {
            var node = _data.nodes.Find(n => n.id == arena.nodeId);
            if (node == null) continue;

            Vector2 center          = WorldToCanvas(node.worldPosition);
            bool    isArenaSelected = arena.nodeId == _selectedArenaNodeId;

            Handles.color = isArenaSelected
                ? new Color(1f, 0.5f, 0.1f, 0.5f)
                : new Color(1f, 0.5f, 0.1f, 0.15f);
            Handles.DrawWireDisc(center, Vector3.forward, ARENA_CANVAS_RADIUS);

            // Draw one arrow per path that arrives at this arena
            var leadPaths = _data.paths
                .Where(p => p.leadsToArena && p.nodeIds.Count >= 2
                            && p.nodeIds[p.nodeIds.Count - 1] == arena.nodeId)
                .ToList();

            for (int i = 0; i < leadPaths.Count; i++)
            {
                var path     = leadPaths[i];
                var lastNode = _data.nodes.Find(n => n.id == path.nodeIds[path.nodeIds.Count - 1]);
                var prevNode = _data.nodes.Find(n => n.id == path.nodeIds[path.nodeIds.Count - 2]);
                if (lastNode == null || prevNode == null) continue;

                Vector2 dir   = (WorldToCanvas(lastNode.worldPosition) - WorldToCanvas(prevNode.worldPosition)).normalized;
                Vector2 tip   = center + dir * ARENA_CANVAS_RADIUS;
                Vector2 inner = center + dir * (ARENA_CANVAS_RADIUS - 10f);

                Color col   = isArenaSelected ? new Color(1f, 0.6f, 0.1f) : new Color(0.9f, 0.7f, 0.3f, 0.7f);
                float width = 1.5f;

                Handles.color = col;
                Handles.DrawAAPolyLine(width, (Vector3)inner, (Vector3)tip);

                Vector2 perp = new Vector2(-dir.y, dir.x) * 4f;
                Vector2 back = tip - dir * 7f;
                Handles.DrawAAPolyLine(width, (Vector3)tip, (Vector3)(back + perp));
                Handles.DrawAAPolyLine(width, (Vector3)tip, (Vector3)(back - perp));
                Handles.DrawSolidDisc(tip, Vector3.forward, 4f);
            }
        }
    }

    private void DrawInProgressPath()
    {
        if (!_isDrawing || _drawingNodeIds.Count == 0) return;

        Handles.color = new Color(1f, 1f, 1f, 0.5f);
        for (int i = 0; i < _drawingNodeIds.Count - 1; i++)
        {
            var a = _data.nodes.Find(n => n.id == _drawingNodeIds[i]);
            var b = _data.nodes.Find(n => n.id == _drawingNodeIds[i + 1]);
            if (a == null || b == null) continue;
            Handles.DrawDottedLine(WorldToCanvas(a.worldPosition), WorldToCanvas(b.worldPosition), 5f);
        }

        // Ghost line to cursor
        var last = _data.nodes.Find(n => n.id == _drawingNodeIds[_drawingNodeIds.Count - 1]);
        if (last != null && _canvasRect.Contains(Event.current.mousePosition))
        {
            Handles.color = new Color(1f, 1f, 1f, 0.25f);
            Handles.DrawDottedLine(WorldToCanvas(last.worldPosition), Event.current.mousePosition, 5f);

            // Snap highlight
            string snapId = FindSnapNode(Event.current.mousePosition);
            if (snapId != null && snapId != _drawingNodeIds[_drawingNodeIds.Count - 1])
            {
                var snapNode = _data.nodes.Find(n => n.id == snapId);
                Handles.color = Color.green;
                Handles.DrawWireDisc(WorldToCanvas(snapNode.worldPosition), Vector3.forward, NODE_RADIUS + 5f);
            }
        }
    }

    // ── Diamond helper ────────────────────────────────────────────
    private static void DrawDiamond(Vector2 c, float r)
    {
        Handles.DrawAAPolyLine(2f,
            new Vector3(c.x,     c.y - r, 0),
            new Vector3(c.x + r, c.y,     0),
            new Vector3(c.x,     c.y + r, 0),
            new Vector3(c.x - r, c.y,     0),
            new Vector3(c.x,     c.y - r, 0));
    }

    // ══════════════════════════════════════════════════════════════
    // RIGHT PANEL
    // ══════════════════════════════════════════════════════════════
    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(_rightPanelWidth), GUILayout.ExpandHeight(true));
        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

        if (_data != null)
        {
            // ── Setup (scene objects + prefabs) ───────────────────
            _foldSetup = EditorGUILayout.Foldout(_foldSetup, "Setup", true, EditorStyles.foldoutHeader);
            if (_foldSetup)
            {
                DrawSceneRefsSection();
                DrawPrefabsSection();
            }
            EditorGUILayout.Space(2);

            // ── Paths ─────────────────────────────────────────────
            _foldPaths = EditorGUILayout.Foldout(_foldPaths, $"Paths ({_data.paths.Count})", true, EditorStyles.foldoutHeader);
            if (_foldPaths) DrawPathList();
            EditorGUILayout.Space(2);

            // ── Junctions ─────────────────────────────────────────
            _foldJunctions = EditorGUILayout.Foldout(_foldJunctions, "Junctions", true, EditorStyles.foldoutHeader);
            if (_foldJunctions) DrawJunctionsList(showHeader: false);
            EditorGUILayout.Space(2);

            // ── Arenas ────────────────────────────────────────────
            _foldArenas = EditorGUILayout.Foldout(_foldArenas, $"Arenas ({_data.arenas.Count})", true, EditorStyles.foldoutHeader);
            if (_foldArenas) DrawArenasList();
            EditorGUILayout.Space(2);

            // ── Rivers ────────────────────────────────────────────
            _foldRivers = EditorGUILayout.Foldout(_foldRivers, "Rivers", true, EditorStyles.foldoutHeader);
            if (_foldRivers) DrawRiverGroups(showHeader: false);
            EditorGUILayout.Space(2);

            // ── Obstacles ─────────────────────────────────────────
            if (_data.obstacles.Count > 0)
            {
                _foldObstacles = EditorGUILayout.Foldout(_foldObstacles, $"Obstacles ({_data.obstacles.Count})", true, EditorStyles.foldoutHeader);
                if (_foldObstacles) DrawObstacleList(showHeader: false);
                EditorGUILayout.Space(2);
            }

            // ── Stats ─────────────────────────────────────────────
            _foldStats = EditorGUILayout.Foldout(_foldStats, "Stats", true, EditorStyles.foldoutHeader);
            if (_foldStats) DrawStats(showHeader: false);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawArenasList()
    {
        if (_data.arenas.Count == 0)
        {
            EditorGUILayout.LabelField("  (none)", EditorStyles.miniLabel);
            return;
        }

        for (int i = _data.arenas.Count - 1; i >= 0; i--)
        {
            var arena = _data.arenas[i];
            var node = _data.nodes.Find(n => n.id == arena.nodeId);
            bool selected = arena.nodeId == _selectedArenaNodeId;

            var prevBg = GUI.backgroundColor;

            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = selected ? new Color(1f, 0.5f, 0.1f) : Color.clear;
            string label = arena.gridData != null
                ? arena.gridData.displayName
                : $"({(node != null ? $"{node.worldPosition.x:F0},{node.worldPosition.z:F0}" : "no node")})";

            if (GUILayout.Button((selected ? "● " : "  ") + label, EditorStyles.miniButton))
            {
                _selectedArenaNodeId = selected ? null : arena.nodeId;
                _selectedEntranceIdx = -1;
                _foldPathProps       = true;
                Repaint();
            }

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                Undo.RecordObject(_data, "Delete Arena");
                RemoveArena(arena.nodeId);
                EditorUtility.SetDirty(_data);
                GUI.backgroundColor = prevBg;
                EditorGUILayout.EndHorizontal();
                break;
            }

            GUI.backgroundColor = prevBg;
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            var newGrid = (GridData)EditorGUILayout.ObjectField(
                arena.gridData, typeof(GridData), false,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data, "Set Arena GridData");
                arena.gridData = newGrid;
                EditorUtility.SetDirty(_data);
            }
        }
    }

    private void RepairJunction(LevelSelectDesignerData.DesignerJunction junc)
    {
        // Fix node type
        var node = _data.nodes.Find(n => n.id == junc.nodeId);
        if (node != null && node.type != LevelSelectDesignerData.NodeType.JunctionSplit)
            node.type = LevelSelectDesignerData.NodeType.JunctionSplit;

        // Rebuild pathIds from all paths that contain this node
        junc.pathIds = _data.paths
            .Where(p => p.nodeIds.Contains(junc.nodeId))
            .Select(p => p.pathId)
            .ToList();

        // Clear invalid river/branch assignments
        if (!string.IsNullOrEmpty(junc.riverPathId) &&
            !_data.paths.Exists(p => p.pathId == junc.riverPathId))
            junc.riverPathId = null;

        if (!string.IsNullOrEmpty(junc.branchPathId) &&
            !_data.paths.Exists(p => p.pathId == junc.branchPathId))
            junc.branchPathId = null;

        Debug.Log($"[LSD] Junction '{junc.junctionId}' repaired — {junc.pathIds.Count} path(s) found.");
    }

    private void DrawJunctionPathField(LevelSelectDesignerData.DesignerJunction junc, bool isRiverField)
    {
        string assignedId  = isRiverField ? junc.riverPathId : junc.branchPathId;
        var    assignedPath = _data.paths.Find(p => p.pathId == assignedId);
        string fieldLabel  = isRiverField ? "≋ River" : "↗ Branch";
        string tooltip     = isRiverField
            ? "The continuing river path this junction sits on"
            : "The branch path extruded from the junction node";
        string pathLabel   = assignedPath != null ? (assignedPath.segmentId ?? "(unnamed)") : "(none)";

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(new GUIContent(fieldLabel, tooltip),
            EditorStyles.miniLabel, GUILayout.Width(52));

        var prev = GUI.backgroundColor;
        GUI.backgroundColor = assignedPath != null
            ? Color.Lerp(assignedPath.editorColor, Color.black, 0.3f)
            : new Color(1f, 0.5f, 0.5f);

        if (GUILayout.Button(pathLabel, EditorStyles.miniButton))
        {
            var capturedJunc    = junc;
            bool capturedIsRiver = isRiverField;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("(none)"), string.IsNullOrEmpty(assignedId), () =>
            {
                Undo.RecordObject(_data, capturedIsRiver ? "Clear Junction River" : "Clear Junction Branch");
                if (capturedIsRiver) capturedJunc.riverPathId  = null;
                else                 capturedJunc.branchPathId = null;
                EditorUtility.SetDirty(_data);
                Repaint();
            });
            menu.AddSeparator("");
            foreach (var path in _data.paths)
            {
                var  capturedPath = path;
                string name = capturedPath.segmentId ?? capturedPath.pathId.Substring(0, 8);
                bool  on    = capturedPath.pathId == assignedId;
                menu.AddItem(new GUIContent(name), on, () =>
                {
                    Undo.RecordObject(_data, capturedIsRiver ? "Set Junction River" : "Set Junction Branch");
                    if (capturedIsRiver) capturedJunc.riverPathId  = capturedPath.pathId;
                    else                 capturedJunc.branchPathId = capturedPath.pathId;
                    if (!capturedJunc.pathIds.Contains(capturedPath.pathId))
                        capturedJunc.pathIds.Add(capturedPath.pathId);
                    EditorUtility.SetDirty(_data);
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }
        GUI.backgroundColor = prev;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawJunctionsList(bool showHeader = true)
    {
        // Build effective junctions the same way Generate does
        var nodeCounts = new Dictionary<string, int>();
        foreach (var p in _data.paths)
            foreach (var nid in p.nodeIds)
                nodeCounts[nid] = nodeCounts.TryGetValue(nid, out int c) ? c + 1 : 1;

        var allJunctions = new List<LevelSelectDesignerData.DesignerJunction>(_data.junctions);
        foreach (var kvp in nodeCounts)
            if (kvp.Value >= 2 && !allJunctions.Exists(j => j.nodeId == kvp.Key))
                allJunctions.Add(new LevelSelectDesignerData.DesignerJunction
                {
                    junctionId = kvp.Key + "_auto",
                    nodeId     = kvp.Key,
                    pathIds    = _data.paths.Where(p => p.nodeIds.Contains(kvp.Key))
                                            .Select(p => p.pathId).ToList()
                });

        if (allJunctions.Count == 0) { EditorGUILayout.LabelField("  (none)", EditorStyles.miniLabel); return; }

        if (showHeader) EditorGUILayout.LabelField("Junctions", EditorStyles.boldLabel);

        foreach (var junc in allJunctions)
        {
            var    node     = _data.nodes.Find(n => n.id == junc.nodeId);
            string label    = node != null
                ? $"({node.worldPosition.x:F1}, {node.worldPosition.z:F1})"
                : junc.nodeId.Substring(0, 8);
            bool   selected = junc.nodeId == _selectedJunctionNodeId;

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = selected ? new Color(1f, 0.85f, 0f) : Color.white;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = prevBg;

            // Header row — click to select / deselect
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(selected ? "◆ " + label : "◇ " + label,
                    selected ? EditorStyles.boldLabel : EditorStyles.label))
            {
                _selectedJunctionNodeId = selected ? null : junc.nodeId;
                Repaint();
            }
            if (GUILayout.Button(new GUIContent("↺", "Repair junction — fixes node type, rebuilds path list, validates assignments"),
                    EditorStyles.miniButton, GUILayout.Width(22)))
            {
                Undo.RecordObject(_data, "Repair Junction");
                RepairJunction(junc);
                EditorUtility.SetDirty(_data);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            // ── Two permanent assignment fields ───────────────────
            DrawJunctionPathField(junc, isRiverField: true);
            DrawJunctionPathField(junc, isRiverField: false);

            // ── Path list with quick-assign buttons ───────────────
            foreach (var pid in junc.pathIds)
            {
                var p = _data.paths.Find(x => x.pathId == pid);
                if (p == null) continue;

                bool isRiver  = pid == junc.riverPathId;
                bool isBranch = pid == junc.branchPathId;
                string role   = isRiver ? " ≋" : isBranch ? " ↗" : "";

                EditorGUILayout.BeginHorizontal();

                var prev = GUI.backgroundColor;
                GUI.backgroundColor = p.pathId == _selectedPathId ? Color.cyan : Color.clear;
                if (GUILayout.Button((p.segmentId ?? pid.Substring(0, 8)) + role, EditorStyles.miniButton))
                    _selectedPathId = p.pathId == _selectedPathId ? null : p.pathId;
                GUI.backgroundColor = prev;

                GUI.backgroundColor = isRiver ? Color.Lerp(p.editorColor, Color.black, 0.3f) : Color.clear;
                if (GUILayout.Button(new GUIContent("≋", "Set as River — the continuing path the junction sits on"),
                        EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    Undo.RecordObject(_data, "Set Junction River");
                    junc.riverPathId = pid;
                    EditorUtility.SetDirty(_data);
                }
                GUI.backgroundColor = isBranch ? Color.Lerp(p.editorColor, Color.black, 0.3f) : Color.clear;
                if (GUILayout.Button(new GUIContent("↗", "Set as Branch — the path extruded from the junction"),
                        EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    Undo.RecordObject(_data, "Set Junction Branch");
                    junc.branchPathId = pid;
                    EditorUtility.SetDirty(_data);
                }
                GUI.backgroundColor = prev;

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(4);
    }

    private void DrawRiverGroups(bool showHeader = true)
    {
        if (showHeader) EditorGUILayout.LabelField("Rivers", EditorStyles.boldLabel);

        var groups = _data.paths
            .GroupBy(p => string.IsNullOrEmpty(p.riverName) ? "(unnamed)" : p.riverName)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            Color groupColor = group.First().editorColor;
            EditorGUILayout.BeginHorizontal();
            var prevBg = GUI.color;
            GUI.color = groupColor;
            EditorGUILayout.LabelField("■", GUILayout.Width(14));
            GUI.color = prevBg;
            EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            foreach (var path in group)
            {
                bool selected = path.pathId == _selectedPathId;
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = selected ? Color.cyan : Color.clear;
                string label = $"{path.segmentId ?? "(unnamed)"}";
                if (path.isLeftPath)    label += " T";
                if (path.isRightPath) label += " B";
                if (GUILayout.Button(label, EditorStyles.miniButton))
                    _selectedPathId = selected ? null : path.pathId;
                GUI.backgroundColor = prev;
            }
            EditorGUI.indentLevel--;
        }

        if (GUILayout.Button("+ New River Group"))
        {
            Undo.RecordObject(_data, "New River Group");
            var p = new LevelSelectDesignerData.DesignerPath
            {
                pathId    = Guid.NewGuid().ToString(),
                segmentId = $"Segment_{_data.paths.Count:00}",
                riverName = "NewRiver",
                editorColor = Color.HSVToRGB((_data.paths.Count * 0.618f) % 1f, 0.7f, 0.9f)
            };
            _data.paths.Add(p);
            _selectedPathId = p.pathId;
            EditorUtility.SetDirty(_data);
        }
    }

    private void DrawObstacleList(bool showHeader = true)
    {
        if (_data.obstacles.Count == 0) return;

        EditorGUILayout.Space(2);
        if (showHeader) EditorGUILayout.LabelField("Obstacles", EditorStyles.boldLabel);

        foreach (var grp in _data.obstacles.GroupBy(o => o.pathId))
        {
            var path = _data.paths.Find(p => p.pathId == grp.Key);
            EditorGUILayout.LabelField(path?.segmentId ?? "(unknown path)", EditorStyles.miniLabel);

            int i = 1;
            foreach (var obs in grp.OrderBy(o => o.pathT))
            {
                bool selected = obs.obstacleId == _selectedObstacleId;
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = selected ? Color.yellow : Color.clear;
                if (GUILayout.Button($"{i}. {obs.obstacleId}", EditorStyles.miniButton))
                    _selectedObstacleId = selected ? null : obs.obstacleId;
                GUI.backgroundColor = prev;
                i++;
            }
        }
    }

    private void DrawStats(bool showHeader = true)
    {
        EditorGUILayout.Space(8);
        if (showHeader) EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Paths:     {_data.paths.Count}",     EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Nodes:     {_data.nodes.Count}",     EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Junctions: {_data.junctions.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Obstacles: {_data.obstacles.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Arenas:    {_data.arenas.Count}",    EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Zoom:      {_zoom:F1}px/u",          EditorStyles.miniLabel);
    }

    // ══════════════════════════════════════════════════════════════
    // COORDINATE HELPERS
    // ══════════════════════════════════════════════════════════════
    // X axis is negated so the canvas matches the scene view orientation (right in scene = right in canvas).
    private Vector2 WorldToCanvas(Vector3 world)
    {
        Vector2 center = _canvasRect.center;
        return new Vector2(
            center.x - (world.x - _viewCenter.x) * _zoom,
            center.y + (world.z - _viewCenter.y) * _zoom);
    }

    private Vector3 CanvasToWorldPos(Vector2 canvas)
    {
        Vector2 center = _canvasRect.center;
        return new Vector3(
            -(canvas.x - center.x) / _zoom + _viewCenter.x,
            _data?.canvasWorldY ?? 0f,
            (canvas.y - center.y) / _zoom + _viewCenter.y);
    }

    private Vector2 CanvasToWorld2D(Vector2 canvas)
    {
        Vector2 center = _canvasRect.center;
        return new Vector2(
            -(canvas.x - center.x) / _zoom + _viewCenter.x,
            (canvas.y - center.y) / _zoom + _viewCenter.y);
    }

    // ══════════════════════════════════════════════════════════════
    // HIT TESTING
    // ══════════════════════════════════════════════════════════════
    private string FindNodeAtCanvas(Vector2 canvas)
    {
        foreach (var node in _data.nodes)
            if (Vector2.Distance(WorldToCanvas(node.worldPosition), canvas) <= NODE_RADIUS + 4f)
                return node.id;
        return null;
    }

    private string FindSnapNode(Vector2 canvas)
    {
        string lastDrawing = (_isDrawing && _drawingNodeIds.Count > 0)
            ? _drawingNodeIds[_drawingNodeIds.Count - 1] : null;

        foreach (var node in _data.nodes)
        {
            if (node.id == lastDrawing) continue;
            if (Vector2.Distance(WorldToCanvas(node.worldPosition), canvas) <= SNAP_RADIUS)
                return node.id;
        }
        return null;
    }

    private string FindPathAtCanvas(Vector2 canvas)
        => FindPathAndSegmentAtCanvas(canvas).pathId;

    private (string pathId, int segIdx) FindPathAndSegmentAtCanvas(Vector2 canvas)
    {
        string bestPath = null;
        int    bestSeg  = -1;
        float  bestDist = PATH_HIT_DIST;

        foreach (var path in _data.paths)
        {
            for (int i = 0; i < path.nodeIds.Count - 1; i++)
            {
                var a = _data.nodes.Find(n => n.id == path.nodeIds[i]);
                var b = _data.nodes.Find(n => n.id == path.nodeIds[i + 1]);
                if (a == null || b == null) continue;

                float d = DistPointToSegment(canvas,
                    WorldToCanvas(a.worldPosition), WorldToCanvas(b.worldPosition));
                if (d < bestDist) { bestDist = d; bestPath = path.pathId; bestSeg = i; }
            }
        }
        return (bestPath, bestSeg);
    }

    private string FindObstacleAtCanvas(Vector2 canvas)
    {
        foreach (var obs in _data.obstacles)
        {
            var pos = GetWorldPosOnPath(obs.pathId, obs.pathT);
            if (!pos.HasValue) continue;
            if (Vector2.Distance(WorldToCanvas(pos.Value), canvas) <= OBSTACLE_RADIUS + 4f)
                return obs.obstacleId;
        }
        return null;
    }

    private float FindTOnPath(LevelSelectDesignerData.DesignerPath path, Vector2 canvas)
    {
        float bestT = 0.5f, bestD = float.MaxValue;
        const int steps = 60;
        for (int i = 0; i <= steps; i++)
        {
            float t   = i / (float)steps;
            float d   = Vector2.Distance(WorldToCanvas(GetWorldPosOnPathRaw(path, t)), canvas);
            if (d < bestD) { bestD = d; bestT = t; }
        }
        return bestT;
    }

    private static float DistPointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab   = b - a;
        float   len2 = ab.sqrMagnitude;
        if (len2 < 0.0001f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + ab * t);
    }

    // ══════════════════════════════════════════════════════════════
    // PATH POSITION HELPERS
    // ══════════════════════════════════════════════════════════════
    private Vector3? GetWorldPosOnPath(string pathId, float t)
    {
        var path = _data?.paths.Find(p => p.pathId == pathId);
        if (path == null || path.nodeIds.Count < 2) return null;
        return GetWorldPosOnPathRaw(path, t);
    }

    private Vector3 GetWorldPosOnPathRaw(LevelSelectDesignerData.DesignerPath path, float t)
    {
        int segments = path.nodeIds.Count - 1;
        float scaled = Mathf.Clamp(t * segments, 0, segments);
        int   seg    = Mathf.Min(Mathf.FloorToInt(scaled), segments - 1);
        float local  = scaled - seg;

        var a = _data.nodes.Find(n => n.id == path.nodeIds[seg]);
        var b = _data.nodes.Find(n => n.id == path.nodeIds[seg + 1]);
        if (a == null || b == null) return Vector3.zero;
        return Vector3.Lerp(a.worldPosition, b.worldPosition, local);
    }

    // ══════════════════════════════════════════════════════════════
    // DATA HELPERS
    // ══════════════════════════════════════════════════════════════
    private LevelSelectDesignerData.DesignerNode AddNode(Vector3 world, LevelSelectDesignerData.NodeType type)
    {
        var node = new LevelSelectDesignerData.DesignerNode
            { id = Guid.NewGuid().ToString(), worldPosition = world, type = type };
        _data.nodes.Add(node);
        return node;
    }

    private void DeleteNode(string nodeId)
    {
        _data.nodes.RemoveAll(n => n.id == nodeId);
        foreach (var p in _data.paths) p.nodeIds.Remove(nodeId);
        _data.paths.RemoveAll(p => p.nodeIds.Count < 2);
        _data.junctions.RemoveAll(j => j.nodeId == nodeId);
        _data.arenas.RemoveAll(a => a.nodeId == nodeId);
        _data.shops.RemoveAll(s => s.nodeId == nodeId);
    }

    private void DeletePath(string pathId)
    {
        var path = _data.paths.Find(p => p.pathId == pathId);
        if (path == null) return;
        foreach (var nodeId in path.nodeIds)
        {
            bool shared = _data.paths.Any(p2 => p2.pathId != pathId && p2.nodeIds.Contains(nodeId));
            if (!shared) _data.nodes.RemoveAll(n => n.id == nodeId);
        }
        _data.paths.RemoveAll(p => p.pathId == pathId);
        _data.obstacles.RemoveAll(o => o.pathId == pathId);
        foreach (var j in _data.junctions) j.pathIds.Remove(pathId);
    }

    private Color GetNodeColor(string nodeId)
    {
        var path = _data.paths.Find(p => p.nodeIds.Contains(nodeId));
        return path?.editorColor ?? Color.gray;
    }

    private string AutoSegmentId()
    {
        bool hasMain = _data.paths.Any(p => p.segmentType == LevelSelectDesignerData.SegmentType.MainRiver);
        int  count   = _data.paths.Count;
        return hasMain ? $"Branch_{count:00}" : $"Main_{count:00}";
    }

    // ══════════════════════════════════════════════════════════════
    // GENERATION
    // ══════════════════════════════════════════════════════════════
    private void Generate()
    {
        if (_data == null) { Debug.LogWarning("[LevelSelectDesigner] No data asset."); return; }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate Level Select");

        var mainVisuals = FindOrCreateParent("MAINRIVERVISUALS");
        var branches    = FindOrCreateParent("RIVERBRANCHES");
        var junctionsGO = FindOrCreateParent("RIVERJUNCTIONS");
        var obstaclesGO = FindOrCreateParent("RIVERGATEsobstacles");

        var generatedContainers = new List<SplineContainer>();

        // Build effective junctions: explicit ones + any node shared between two or more paths
        var nodeCounts = new Dictionary<string, int>();
        foreach (var p in _data.paths)
            foreach (var nid in p.nodeIds)
                nodeCounts[nid] = nodeCounts.TryGetValue(nid, out int c) ? c + 1 : 1;

        var effectiveJunctions = new List<LevelSelectDesignerData.DesignerJunction>(_data.junctions);
        foreach (var kvp in nodeCounts)
        {
            if (kvp.Value >= 2 && !effectiveJunctions.Exists(j => j.nodeId == kvp.Key))
            {
                effectiveJunctions.Add(new LevelSelectDesignerData.DesignerJunction
                {
                    junctionId = kvp.Key + "_auto",
                    nodeId     = kvp.Key,
                    pathIds    = _data.paths
                        .Where(p => p.nodeIds.Contains(kvp.Key))
                        .Select(p => p.pathId).ToList()
                });
            }
        }

        _correctedNodePositions.Clear();
        _junctionPerpDirections.Clear();
        Debug.Log($"[LSD] effective junctions: {effectiveJunctions.Count} (explicit={_data.junctions.Count} auto-detected={effectiveJunctions.Count - _data.junctions.Count})");

        foreach (var path in _data.paths)
        {
            if (path.nodeIds.Count < 2) continue;
            bool isMain = path.segmentType == LevelSelectDesignerData.SegmentType.MainRiver;
            var  parent = isMain ? mainVisuals : branches;

            // Collect ALL mid-path junctions for this path, sorted by node index
            var pathJunctions = effectiveJunctions
                .Where(j => { int idx = path.nodeIds.IndexOf(j.nodeId);
                              return idx > 0 && idx < path.nodeIds.Count - 1; })
                .OrderBy(j => path.nodeIds.IndexOf(j.nodeId))
                .ToList();

            if (pathJunctions.Count > 0)
                GenerateJunctionSplit(path, pathJunctions, parent, junctionsGO, generatedContainers);
            else
            {
                var c = GenerateSimpleSegment(path, parent);
                if (c != null) generatedContainers.Add(c);
            }
        }

        GenerateObstacles(obstaclesGO);
        GenerateArenas();

        // Spawn BoatPathManager before WireSourceSegments so FindObjectOfType picks it up
        SplinePathStitcher stitcher = null;
        if (_data.boatPathManager != null)
        {
            var boatParent = FindOrCreateParent("BoatPaths");
            var bpmGO = (GameObject)PrefabUtility.InstantiatePrefab(_data.boatPathManager.gameObject);
            Undo.RegisterCreatedObjectUndo(bpmGO, "Spawn BoatPathManager");
            bpmGO.transform.SetParent(boatParent.transform, false);
            bpmGO.transform.localPosition = Vector3.zero;
            stitcher = bpmGO.GetComponent<SplinePathStitcher>();
        }

        WireSourceSegments(generatedContainers);

        if (stitcher != null)
        {
            stitcher.BakePaths();
            Debug.Log("[LevelSelectDesigner] Boat paths baked.");

            var bakedSegments = stitcher.GetComponentsInChildren<RiverSegmentID>();
            WireJunctionNodes(bakedSegments);
            Debug.Log("[LevelSelectDesigner] Junction segment IDs wired from baked paths.");
        }

        GenerateLandscapeTiles();
        SyncHillPointsToScene();

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[LevelSelectDesigner] Generated {generatedContainers.Count} segment(s), " +
                  $"{_data.obstacles.Count} obstacle(s), {_data.arenas.Count} arena(s).");
    }

    private SplineContainer GenerateSimpleSegment(LevelSelectDesignerData.DesignerPath path, GameObject parent)
    {
        var go = new GameObject(path.segmentId ?? "Segment");
        Undo.RegisterCreatedObjectUndo(go, "Generate Segment");
        go.transform.SetParent(parent.transform, false);

        var container = go.AddComponent<SplineContainer>();
        SetSplineFromPath(container, path);

        var segId = go.AddComponent<RiverSegmentID>();
        ApplySegmentID(segId, path);

        if (_data.riverBlockPrefab != null)
            SetupSplineInstantiate(go, container, _data.riverBlockPrefab, _data.splineInstantiateSpacing);

        return container;
    }

    // Handles 1..N junctions on a single path in one pass.
    private void GenerateJunctionSplit(
        LevelSelectDesignerData.DesignerPath path,
        List<LevelSelectDesignerData.DesignerJunction> junctions,
        GameObject parent, GameObject junctionsParent,
        List<SplineContainer> containers)
    {
        // ── 1. Build full spline ──────────────────────────────────
        var go = new GameObject(path.segmentId ?? "JunctionSegment");
        Undo.RegisterCreatedObjectUndo(go, "Generate Junction Segment");
        go.transform.SetParent(parent.transform, false);

        var fullContainer = go.AddComponent<SplineContainer>();
        SetSplineFromPath(fullContainer, path);

        var      fullSpline = fullContainer.Splines[0];
        float4x4 fl2w       = fullContainer.transform.localToWorldMatrix;

        int subs  = _data.curveSubdivisions > 0 ? _data.curveSubdivisions
                  : (_splitPreset != null ? _splitPreset.subdivisions : 5);
        int steps = subs * Mathf.Max(1, fullSpline.Count - 1);

        // ── 2. Resolve each junction: nearestT, gap bounds, prefab ─
        var resolved = new List<(
            float nearestT, float T_endA, float T_startB,
            float3 endALocal, float3 startBLocal,
            Vector3 tanWorld, GameObject prefab,
            LevelSelectDesignerData.DesignerJunction junction)>();

        foreach (var junc in junctions)
        {
            int     jIdx    = path.nodeIds.IndexOf(junc.nodeId);
            Vector3 jWorld3 = WorldPosOfNode(junc.nodeId);
            float3  jLocal  = fullContainer.transform.InverseTransformPoint(jWorld3);
            SplineUtility.GetNearestPoint(fullSpline, jLocal, out _, out float nearestT);

            Vector3 actualJuncWorld = go.transform.TransformPoint(fullSpline.EvaluatePosition(nearestT));
            _correctedNodePositions[junc.nodeId] = actualJuncWorld;

            float3  jTanL   = math.normalize(fullSpline.EvaluateTangent(nearestT));
            float3  jTanW   = math.normalize(math.mul(fl2w, new float4(jTanL, 0f)).xyz);
            Vector3 tanWorld = new Vector3(jTanW.x, jTanW.y, jTanW.z);

            // ── Branch flags drive prefab + offset direction ──────
            // Top  = LEFT  of river forward direction (CCW perpendicular)  → Up   prefab
            // Bottom = RIGHT of river forward direction (CW perpendicular) → Down prefab
            var branchPath = _data.paths.FirstOrDefault(
                p => p.pathId != path.pathId && p.nodeIds.Contains(junc.nodeId));

            bool isLeft = false, isRight = false;
            if (branchPath != null)
            {
                if (branchPath.isLeftPath || branchPath.isRightPath)
                {
                    // User-specified — trust it
                    isLeft    = branchPath.isLeftPath;
                    isRight = branchPath.isRightPath;
                }
                else
                {
                    // Auto-detect: cross product tells left vs right of travel
                    int bIdx = branchPath.nodeIds.IndexOf(junc.nodeId);
                    string nextId = bIdx < branchPath.nodeIds.Count - 1
                        ? branchPath.nodeIds[bIdx + 1] : null;
                    if (nextId != null)
                    {
                        float cross = SplineSplitUtility.CrossXZ(tanWorld,
                            (WorldPosOfNode(nextId) - actualJuncWorld).normalized);
                        isLeft    = cross < 0f; // left of travel
                        isRight = cross > 0f; // right of travel
                    }
                    else { isRight = true; } // fallback

                    // Write back so user can see and adjust in inspector
                    Undo.RecordObject(_data, "Auto-detect branch side");
                    branchPath.isLeftPath    = isLeft;
                    branchPath.isRightPath = isRight;
                    EditorUtility.SetDirty(_data);
                }
            }

            // Prefab from flags
            GameObject juncPrefab = isLeft
                ? (_data.junctionLeftFacingPrefab   ?? _splitPreset?.junctionLeftFacingPrefab   ?? _data.junctionPrefab)
                : (_data.junctionRightFacingPrefab ?? _splitPreset?.junctionRightFacingPrefab ?? _data.junctionPrefab);

            // Branch direction: read from JunctionBranchDirectionHint on the prefab if present,
            // transformed by the rotation the junction GO will receive (LookRotation along mainTan).
            // Falls back to CCW/CW perpendicular if the hint isn't set up.
            Quaternion juncRot  = Quaternion.LookRotation(tanWorld, Vector3.up);
            var        hint     = juncPrefab != null
                ? juncPrefab.GetComponentInChildren<JunctionBranchDirectionHint>() : null;
            Vector3 perpDir = hint != null
                ? (juncRot * hint.transform.localRotation * Vector3.forward).normalized
                : (isLeft
                    ? new Vector3(-tanWorld.z, 0f,  tanWorld.x)
                    : new Vector3( tanWorld.z, 0f, -tanWorld.x));
            _junctionPerpDirections[junc.nodeId] = perpDir;

            float halfGap = juncPrefab != null
                ? SplineSplitUtility.MeasurePrefabXExtent(juncPrefab) * 0.5f
                  + (_splitPreset != null ? _splitPreset.padding : _data.junctionGapPadding)
                : Mathf.Max(0f, _splitPreset != null ? _splitPreset.padding : _data.junctionGapPadding);
            halfGap = Mathf.Max(0f, halfGap);

            SplineUtility.GetPointAtLinearDistance(fullSpline, nearestT, -halfGap, out float T_endA);
            SplineUtility.GetPointAtLinearDistance(fullSpline, nearestT,  halfGap, out float T_startB);

            resolved.Add((nearestT, T_endA, T_startB,
                fullSpline.EvaluatePosition(T_endA),
                fullSpline.EvaluatePosition(T_startB),
                tanWorld, juncPrefab, junc));
        }

        // Sort by T so we process left-to-right along the river
        resolved.Sort((a, b) => a.nearestT.CompareTo(b.nearestT));

        // ── 3. Build segment position lists ───────────────────────
        // Boundaries: [0..T_endA[0]], [T_startB[0]..T_endA[1]], ..., [T_startB[N-1]..1]
        // N+1 segments, N gaps
        var segBounds = new List<(float from, float to)>();
        float prev = 0f;
        foreach (var r in resolved)
        {
            segBounds.Add((prev, r.T_endA));
            prev = r.T_startB;
        }
        segBounds.Add((prev, 1f));

        // Single walk — distribute points to the correct segment
        var segPos = new List<List<float3>>(segBounds.Count);
        for (int s = 0; s < segBounds.Count; s++) segPos.Add(new List<float3>());

        for (int i = 0; i <= steps; i++)
        {
            float  t = (float)i / steps;
            float3 p = fullSpline.EvaluatePosition(t);
            for (int s = 0; s < segBounds.Count; s++)
                if (t >= segBounds[s].from && t <= segBounds[s].to)
                    segPos[s].Add(p);
        }

        // Ensure exact gap endpoints are the segment boundaries
        for (int s = 0; s < segBounds.Count; s++)
        {
            float3 startP = fullSpline.EvaluatePosition(segBounds[s].from);
            float3 endP   = fullSpline.EvaluatePosition(segBounds[s].to);
            if (segPos[s].Count == 0 || math.distance(segPos[s][0], startP) > 0.001f)
                segPos[s].Insert(0, startP);
            if (segPos[s].Count < 2 || math.distance(segPos[s][segPos[s].Count - 1], endP) > 0.001f)
                segPos[s].Add(endP);
        }

        // ── 4. Create containers ──────────────────────────────────
        string[] suffixes = { "_A", "_B", "_C", "_D", "_E", "_F" };
        bool firstSeg = true;

        for (int s = 0; s < segBounds.Count; s++)
        {
            if (segPos[s].Count < 2) continue;

            var segSpline = SplineSplitUtility.BuildSplineFromPositions(segPos[s], TangentMode.AutoSmooth);

            SplineContainer segContainer;
            if (firstSeg)
            {
                Undo.RegisterCompleteObjectUndo(fullContainer, "Split Spline");
                fullContainer.RemoveSplineAt(0);
                fullContainer.AddSpline(segSpline);
                EditorUtility.SetDirty(fullContainer);
                segContainer = fullContainer;
                firstSeg = false;
            }
            else
            {
                segContainer = Undo.AddComponent<SplineContainer>(go);
                segContainer.RemoveSplineAt(0);
                segContainer.AddSpline(segSpline);
                EditorUtility.SetDirty(segContainer);
            }

            if (_data.riverBlockPrefab != null)
                SetupSplineInstantiate(go, segContainer, _data.riverBlockPrefab, _data.splineInstantiateSpacing);

            var segId = go.AddComponent<RiverSegmentID>();
            ApplySegmentID(segId, path);
            segId.SetSegmentID(path.segmentId + (s < suffixes.Length ? suffixes[s] : $"_{s}"));
            containers.Add(segContainer);

            // Create gap container after this segment (if not the last)
            if (s < resolved.Count)
            {
                var r = resolved[s];
                var gapSpline = SplineSplitUtility.BuildSplineFromPositions(
                    new List<float3> { r.endALocal, r.startBLocal }, TangentMode.Broken);

                var gapContainer = Undo.AddComponent<SplineContainer>(go);
                gapContainer.RemoveSplineAt(0);
                gapContainer.AddSpline(gapSpline);
                EditorUtility.SetDirty(gapContainer);

                if (r.prefab != null)
                    SplineToolsWindow.SetupJunctionInstantiate(go, gapContainer, r.prefab,
                        _splitPreset?.junctionPosOffset ?? Vector3.zero,
                        _splitPreset?.junctionRotOffset ?? Vector3.zero);

                // Top/bottom for branch
                var branchPath = _data.paths.FirstOrDefault(
                    p => p.pathId != path.pathId && p.nodeIds.Contains(r.junction.nodeId));
                if (branchPath != null)
                {
                    int    bIdx   = branchPath.nodeIds.IndexOf(r.junction.nodeId);
                    string nextId = bIdx < branchPath.nodeIds.Count - 1
                        ? branchPath.nodeIds[bIdx + 1]
                        : bIdx > 0 ? branchPath.nodeIds[bIdx - 1] : null;
                    if (nextId != null)
                    {
                        float cross = SplineSplitUtility.CrossXZ(r.tanWorld,
                            (WorldPosOfNode(nextId) - _correctedNodePositions[r.junction.nodeId]).normalized);
                        branchPath.isRightPath = cross > 0f;
                        branchPath.isLeftPath    = cross < 0f;
                    }
                }

                // Junction script object at gap midpoint
                if (_data.junctionScriptObject != null)
                {
                    float   T_mid     = (r.T_endA + r.T_startB) * 0.5f;
                    Vector3 scriptPos = go.transform.TransformPoint(fullSpline.EvaluatePosition(T_mid));
                    float3  tl        = math.normalize(fullSpline.EvaluateTangent(T_mid));
                    float3  tw        = math.normalize(math.mul(fl2w, new float4(tl, 0f)).xyz);
                    Vector3 scriptTan = math.lengthsq(tw) > 0.001f
                        ? new Vector3(tw.x, tw.y, tw.z) : r.tanWorld;

                    var jGO = (GameObject)PrefabUtility.InstantiatePrefab(_data.junctionScriptObject);
                    Undo.RegisterCreatedObjectUndo(jGO, "Generate Junction Script Object");
                    jGO.transform.SetParent(junctionsParent.transform, true);
                    jGO.transform.position = scriptPos;
                    if (scriptTan != Vector3.zero)
                        jGO.transform.rotation = Quaternion.LookRotation(scriptTan, Vector3.up);
                    jGO.name = $"Junction_{path.segmentId}_{s}";
                }
            }
        }
    }

    // Cross product in XZ: positive → branch is to the RIGHT of main tangent (down-facing).
    private GameObject SelectJunctionPrefab(
        LevelSelectDesignerData.DesignerPath mainPath,
        LevelSelectDesignerData.DesignerJunction junction,
        Vector3 junctionWorldPos, Vector3 mainTangent)
    {
        // Find the branch path — any path containing the junction node that isn't the main path
        var branch = _data.paths.FirstOrDefault(
            p => p.pathId != mainPath.pathId && p.nodeIds.Contains(junction.nodeId));

        if (branch == null) return _data.junctionPrefab;

        int jIdxInBranch = branch.nodeIds.IndexOf(junction.nodeId);
        // Look at the node immediately after the junction in the branch
        string nextId = jIdxInBranch < branch.nodeIds.Count - 1
            ? branch.nodeIds[jIdxInBranch + 1]
            : jIdxInBranch > 0 ? branch.nodeIds[jIdxInBranch - 1] : null;

        if (nextId == null) return _data.junctionPrefab;

        Vector3 branchDir = (WorldPosOfNode(nextId) - junctionWorldPos).normalized;

        // 2D cross product in XZ plane
        float cross = mainTangent.x * branchDir.z - mainTangent.z * branchDir.x;

        if (cross > 0f)
            return _data.junctionLeftFacingPrefab   != null ? _data.junctionLeftFacingPrefab   : _data.junctionPrefab;
        else
            return _data.junctionRightFacingPrefab != null ? _data.junctionRightFacingPrefab : _data.junctionPrefab;
    }

    private void GenerateObstacles(GameObject obstaclesParent)
    {
        var instantiated = new Dictionary<string, LevelSelectObstacleManager>();
        var sorted       = _data.obstacles.OrderBy(o => o.pathId).ThenBy(o => o.pathT).ToList();

        foreach (var obs in sorted)
        {
            GameObject prefab = obs.obstaclePrefab != null ? obs.obstaclePrefab : _data.obstaclePrefab;
            if (prefab == null) continue;

            Vector3? pos = GetWorldPosOnPath(obs.pathId, obs.pathT);
            if (!pos.HasValue) continue;

            var obsGO = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(obsGO, "Generate Obstacle");
            obsGO.transform.SetParent(obstaclesParent.transform, true);
            obsGO.transform.position = pos.Value;
            obsGO.name = obs.obstacleId;

            var mgr = obsGO.GetComponentInChildren<LevelSelectObstacleManager>();
            if (mgr != null)
            {
                var so = new SerializedObject(mgr);
                so.Update();
                so.FindProperty("obstacleID").stringValue = obs.obstacleId;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(mgr);
                instantiated[obs.obstacleId] = mgr;
            }
        }

        // Chain nextObstacleTransform per path
        foreach (var grp in sorted.GroupBy(o => o.pathId))
        {
            var ordered = grp.OrderBy(o => o.pathT).ToList();
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                if (instantiated.TryGetValue(ordered[i].obstacleId,     out var cur) &&
                    instantiated.TryGetValue(ordered[i + 1].obstacleId, out var next))
                {
                    var so = new SerializedObject(cur);
                    so.Update();
                    so.FindProperty("nextObstacleTransform").objectReferenceValue = next.transform;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(cur);
                }
            }
        }

        // Wire _firstObstacleTransform on LevelSelectSplineManager
        var splineMgr = FindObjectOfType<LevelSelectSplineManager>();
        if (splineMgr != null && sorted.Count > 0 &&
            instantiated.TryGetValue(sorted[0].obstacleId, out var first))
        {
            var so = new SerializedObject(splineMgr);
            so.Update();
            so.FindProperty("_firstObstacleTransform").objectReferenceValue = first.transform;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(splineMgr);
        }
    }

    private void GenerateArenas()
    {
        if (_data.arenaEntrancePrefab == null)
            Debug.LogWarning("[LevelSelectDesigner] arenaEntrancePrefab not set — entrance children will not be spawned.");

        var arenasParent = FindOrCreateParent("ARENAS");

        foreach (var arena in _data.arenas)
        {
            GameObject headPrefab = arena.arenaPrefabOverride != null
                ? arena.arenaPrefabOverride : _data.arenaPrefab;
            if (headPrefab == null) continue;

            var node = _data.nodes.Find(n => n.id == arena.nodeId);
            if (node == null) continue;

            // ── Find branch path leading to this arena ────────────
            var branchPath = _data.paths.FirstOrDefault(p =>
                p.leadsToArena &&
                p.nodeIds.Count > 0 &&
                p.nodeIds[p.nodeIds.Count - 1] == arena.nodeId);

            // Branch end tangent (last two nodes of branch)
            Vector3 branchEndTangent = Vector3.forward;
            Vector3 branchEndPos     = node.worldPosition;
            if (branchPath != null && branchPath.nodeIds.Count >= 2)
            {
                Vector3 last = WorldPosOfNode(branchPath.nodeIds[branchPath.nodeIds.Count - 1]);
                Vector3 prev = WorldPosOfNode(branchPath.nodeIds[branchPath.nodeIds.Count - 2]);
                branchEndTangent = (last - prev).normalized;
                branchEndPos     = last;
            }

            // ── Spawn / reuse arena head GO ───────────────────────
            var existing = FindObjectsOfType<LevelSelectDesignerArenaTag>()
                .FirstOrDefault(t => t.nodeId == arena.nodeId);

            GameObject arenaGO;
            if (existing != null)
            {
                arenaGO = existing.gameObject;
                // Destroy old entrance children so we can respawn them
                var oldEntrances = arenaGO.GetComponentsInChildren<LevelSelectArenaEntranceDirectionHint>();
                foreach (var e in oldEntrances)
                    Undo.DestroyObjectImmediate(e.transform.parent.gameObject);
            }
            else
            {
                arenaGO = (GameObject)PrefabUtility.InstantiatePrefab(headPrefab);
                Undo.RegisterCreatedObjectUndo(arenaGO, "Generate Arena");
                arenaGO.transform.SetParent(arenasParent.transform, false);
                var tag = arenaGO.AddComponent<LevelSelectDesignerArenaTag>();
                tag.nodeId = arena.nodeId;
            }

            // ── Flatten arrival tangent to XZ and compute Y angle ─
            Vector3 arrivalDir = branchEndTangent;
            arrivalDir.y = 0f;
            if (arrivalDir.sqrMagnitude < 0.0001f) arrivalDir = Vector3.forward;
            arrivalDir.Normalize();
            float arrivalYAngle = Quaternion.LookRotation(arrivalDir, Vector3.up).eulerAngles.y;

            // ── Arena head: offset along arrival direction from branch end ─
            arenaGO.transform.position = new Vector3(
                branchEndPos.x + arrivalDir.x * _data.arenaHeadOffset,
                branchEndPos.y,
                branchEndPos.z + arrivalDir.z * _data.arenaHeadOffset);
            arenaGO.transform.rotation = Quaternion.Euler(0f, arrivalYAngle, 0f);

            // ── Wire GridData on controller ───────────────────────
            var ctrl = arenaGO.GetComponentInChildren<LevelSelectArenaController>();
            if (ctrl != null && arena.gridData != null)
            {
                Undo.RecordObject(ctrl, "Set Arena GridData");
                ctrl.gridData = arena.gridData;
                ctrl.portalLinks.Clear();
                EditorUtility.SetDirty(ctrl);
            }

            // ── Spawn entrance at the branch end point ─────────────
            if (_data.arenaEntrancePrefab == null) continue;

            var entGO = (GameObject)PrefabUtility.InstantiatePrefab(_data.arenaEntrancePrefab);
            Undo.RegisterCreatedObjectUndo(entGO, "Generate Arena Entrance");
            entGO.transform.SetParent(arenaGO.transform, false);
            entGO.transform.localPosition = Vector3.zero;
            Quaternion desiredWorld = Quaternion.Euler(-90f, arrivalYAngle, 0f);
            entGO.transform.localRotation = Quaternion.Inverse(arenaGO.transform.rotation) * desiredWorld;
            entGO.name = "Entrance_0";

            if (ctrl != null)
            {
                var trigger = entGO.GetComponentInChildren<LevelSelectEnter>();
                ctrl.portalLinks.Add(new LevelSelectArenaController.PortalLink
                {
                    entranceIndex = 0,
                    trigger       = trigger
                });
                EditorUtility.SetDirty(ctrl);
            }
        }
    }

    private void WireJunctionNodes(RiverSegmentID[] bakedSegments)
    {
        var sceneJunctions = FindObjectsOfType<SplineRiverJunctionNodeV2>();

        foreach (var designerJunc in _data.junctions)
        {
            if (string.IsNullOrEmpty(designerJunc.riverPathId) ||
                string.IsNullOrEmpty(designerJunc.branchPathId)) continue;

            var riverPath  = _data.paths.Find(p => p.pathId == designerJunc.riverPathId);
            var branchPath = _data.paths.Find(p => p.pathId == designerJunc.branchPathId);
            if (riverPath == null || branchPath == null) continue;

            var juncNode = _data.nodes.Find(n => n.id == designerJunc.nodeId);
            if (juncNode == null) continue;
            Vector3 juncPos = juncNode.worldPosition;

            // Find baked highway from river path with endpoint nearest to junction
            string riverSegID  = FindBakedEndpointID(bakedSegments, riverPath.segmentId, juncPos);
            string branchSegID = FindBakedEndpointID(bakedSegments, branchPath.segmentId, juncPos);

            if (string.IsNullOrEmpty(riverSegID) || string.IsNullOrEmpty(branchSegID))
            {
                Debug.LogWarning($"[LSD] Junction '{designerJunc.junctionId}': could not find baked segments " +
                                 $"for river='{riverPath.segmentId}' branch='{branchPath.segmentId}'");
                continue;
            }

            // Find the scene junction node nearest to this designer junction position
            SplineRiverJunctionNodeV2 nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var sj in sceneJunctions)
            {
                float d = Vector3.Distance(sj.transform.position, juncPos);
                if (d < nearestDist) { nearestDist = d; nearest = sj; }
            }

            if (nearest != null)
            {
                Undo.RecordObject(nearest, "Wire Junction Segment IDs");
                nearest.AssignSegmentIDsFromBaked(new[] {
                    bakedSegments.FirstOrDefault(b => b.SegmentID == riverSegID),
                    bakedSegments.FirstOrDefault(b => b.SegmentID == branchSegID)
                }.Where(b => b != null));
                EditorUtility.SetDirty(nearest);
                Debug.Log($"[LSD] Junction wired: river='{riverSegID}' branch='{branchSegID}'");
            }
        }
    }

    private string FindBakedEndpointID(RiverSegmentID[] baked, string baseSegmentId, Vector3 juncPos)
    {
        string best     = null;
        float  bestDist = float.MaxValue;

        foreach (var seg in baked)
        {
            if (!seg.SegmentID.StartsWith(baseSegmentId)) continue;
            var container = seg.GetComponent<SplineContainer>();
            if (container == null || container.Spline == null) continue;

            Vector3 start = container.transform.TransformPoint(container.Spline.EvaluatePosition(0f));
            Vector3 end   = container.transform.TransformPoint(container.Spline.EvaluatePosition(1f));
            float dist = Mathf.Min(Vector3.Distance(juncPos, start), Vector3.Distance(juncPos, end));
            if (dist < bestDist) { bestDist = dist; best = seg.SegmentID; }
        }
        return best;
    }

    private void WireSourceSegments(List<SplineContainer> containers)
    {
        var sorted = containers
            .OrderBy(c => c.GetComponent<RiverSegmentID>()?.BranchDepth ?? 0)
            .ToList();

        void Wire(UnityEngine.Object target)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            so.Update();
            var prop = so.FindProperty("_sourceSegments");
            prop.ClearArray();
            for (int i = 0; i < sorted.Count; i++)
            {
                prop.InsertArrayElementAtIndex(i);
                prop.GetArrayElementAtIndex(i).objectReferenceValue = sorted[i];
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        Wire(FindObjectOfType<SplineRiverManager>());
        Wire(FindObjectOfType<SplinePathStitcher>());

        // Wire SplineRiverManager → LevelSelectSplineManager._riverManager
        var splineManager = FindObjectOfType<LevelSelectSplineManager>();
        var riverManager  = FindObjectOfType<SplineRiverManager>();
        if (splineManager != null && riverManager != null)
        {
            var so = new SerializedObject(splineManager);
            so.Update();
            so.FindProperty("_riverManager").objectReferenceValue = riverManager;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(splineManager);
        }
    }

    private void PruneLooseNodes()
    {
        var referenced = new HashSet<string>(_data.paths.SelectMany(p => p.nodeIds));
        int before = _data.nodes.Count;
        _data.nodes.RemoveAll(n => !referenced.Contains(n.id));
        if (_data.nodes.Count != before)
            EditorUtility.SetDirty(_data);
    }

    private void ClearGeneratedObjects()
    {
        foreach (var name in new[] { "MAINRIVERVISUALS", "RIVERBRANCHES", "RIVERJUNCTIONS", "RIVERGATEsobstacles", "ARENAS", "BoatPaths", "LANDSCAPETILES" })
        {
            var go = GameObject.Find(name);
            if (go == null) continue;
            var children = Enumerable.Range(0, go.transform.childCount)
                .Select(i => go.transform.GetChild(i).gameObject).ToList();
            foreach (var child in children) Undo.DestroyObjectImmediate(child);
        }
    }

    private static GameObject FindOrCreateParent(string name)
    {
        var go = GameObject.Find(name);
        if (go != null) return go;
        go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Parent");
        return go;
    }

    // ── Spline building ───────────────────────────────────────────
    private void SetSplineFromPath(SplineContainer container, LevelSelectDesignerData.DesignerPath path)
    {
        var    positions       = new List<Vector3>();
        bool   isJunctionStart = false;
        string junctionNodeId  = null;

        for (int i = 0; i < path.nodeIds.Count; i++)
        {
            string  nodeId = path.nodeIds[i];
            Vector3 pos    = WorldPosOfNode(nodeId);
            positions.Add(pos);

            if (i == 0 && _correctedNodePositions.ContainsKey(nodeId))
            {
                isJunctionStart = true;
                junctionNodeId  = nodeId;
            }
        }

        SetSplineFromPositions(container, positions);

        if (!isJunctionStart || container.Splines.Count == 0) return;

        var spline = container.Splines[0];
        if (spline.Count < 2) return;

        // ── Trim the leading end by branchStartOffset ─────────────
        if (_data.branchStartOffset > 0f)
        {
            SplineUtility.GetPointAtLinearDistance(spline, 0f, _data.branchStartOffset, out float T_start);

            int subs  = _data.curveSubdivisions > 0 ? _data.curveSubdivisions : 5;
            int steps = subs * Mathf.Max(1, spline.Count - 1);

            var trimPos = new List<float3>();
            trimPos.Add(spline.EvaluatePosition(T_start));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                if (t > T_start) trimPos.Add(spline.EvaluatePosition(t));
            }
            float3 endP = spline.EvaluatePosition(1f);
            if (trimPos.Count < 2 || math.distance(trimPos[trimPos.Count - 1], endP) > 0.001f)
                trimPos.Add(endP);

            if (trimPos.Count >= 2)
            {
                var trimmed = SplineSplitUtility.BuildSplineFromPositions(trimPos, TangentMode.AutoSmooth);
                container.RemoveSplineAt(0);
                container.AddSpline(trimmed);
                spline = container.Splines[0];
            }
        }

        // ── Force first knot tangent perpendicular to main river ──
        // Convert the stored world-space perpendicular into the container's local space,
        // preserve the existing tangent magnitude, then set Broken mode so it holds.
        if (spline.Count > 0
            && _junctionPerpDirections.TryGetValue(junctionNodeId, out var perpWorld))
        {
            var    knot      = spline[0];
            float3 localPerp = container.transform.InverseTransformDirection(perpWorld);
            float  tanLen    = math.length(knot.TangentOut);
            if (tanLen < 0.0001f) tanLen = 0.333f;

            knot.TangentOut = math.normalize(localPerp) * tanLen;
            spline.SetKnot(0, knot);
            spline.SetTangentMode(0, TangentMode.Broken);
        }

        EditorUtility.SetDirty(container);
    }

    // Actual smooth-spline world positions for junction nodes, populated during generation.
    // Overrides the raw stored position so branch splines start exactly where the main
    // river split landed on the smooth curve.
    private readonly Dictionary<string, Vector3> _correctedNodePositions = new();

    // Perpendicular-to-main-river direction per junction node.
    // Used to force the branch's first segment to exit at 90° from the main river tangent.
    private readonly Dictionary<string, Vector3> _junctionPerpDirections = new();

    private Vector3 WorldPosOfNode(string nodeId)
    {
        if (_correctedNodePositions.TryGetValue(nodeId, out var corrected))
            return corrected;
        return _data.nodes.Find(n => n.id == nodeId)?.worldPosition ?? Vector3.zero;
    }

    private static void SetSplineFromPositions(SplineContainer container, List<Vector3> worldPositions)
    {
        var local = worldPositions
            .Select(p => (float3)container.transform.InverseTransformPoint(p))
            .ToList();

        var spline = new Spline();
        for (int i = 0; i < local.Count; i++)
        {
            float3 pos    = local[i];
            float3 tanIn  = float3.zero;
            float3 tanOut = float3.zero;

            if (i > 0 && i < local.Count - 1)
            {
                tanIn  = (pos - local[i - 1]) * 0.333f;
                tanOut = (local[i + 1] - pos) * 0.333f;
            }
            else if (i == 0 && local.Count > 1)
            {
                tanOut = (local[1] - pos) * 0.333f;
                tanIn  = -tanOut;
            }
            else if (i == local.Count - 1 && local.Count > 1)
            {
                tanIn  = (pos - local[i - 1]) * 0.333f;
                tanOut = -tanIn;
            }

            spline.Add(new BezierKnot(pos, -tanIn, tanOut, quaternion.identity), TangentMode.AutoSmooth);
        }

        if (container.Splines.Count > 0)
            container.RemoveSplineAt(0);
        container.AddSpline(spline);
        EditorUtility.SetDirty(container);
    }

    private static void ApplySegmentID(RiverSegmentID segId, LevelSelectDesignerData.DesignerPath path)
    {
        var so = new SerializedObject(segId);
        so.Update();
        so.FindProperty("segmentID").stringValue      = path.segmentId ?? "";
        so.FindProperty("isLeftPath").boolValue        = path.isLeftPath;
        so.FindProperty("isRightPath").boolValue     = path.isRightPath;
        so.FindProperty("segmentType").enumValueIndex = (int)path.segmentType;
        so.FindProperty("junctionGroup").stringValue  = path.riverName ?? "";
        so.FindProperty("leadsToArena").boolValue     = path.leadsToArena;
        so.FindProperty("arenaIsAtEnd").boolValue     = path.arenaIsAtEnd;
        so.FindProperty("extrudeOnExit").boolValue    = path.extrudeOnExit;
        so.FindProperty("skipRegistration").boolValue = true; // visual source segments must not appear in registry
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(segId);
    }

    private static void SetupSplineInstantiate(GameObject go, SplineContainer container, GameObject prefab, float spacing)
    {
        var si = Undo.AddComponent<SplineInstantiate>(go);
        si.Container = container;

        var so = new SerializedObject(si);
        so.Update();

        // Each SetSIProp call warns if the property name is wrong for this Unity version,
        // but never throws — so ApplyModifiedPropertiesWithoutUndo always runs.
        SetSIProp(so, "m_Method",      p => p.enumValueIndex = 1);                          // SpacingDistance
        SetSIProp(so, "m_Spacing",     p => p.vector2Value   = new Vector2(spacing, spacing));
        // Rotation offset X = -90
        var rotOff = so.FindProperty("m_RotationOffset");
        if (rotOff != null)
        {
            SetSIProp(rotOff, "setup", p => p.intValue      = 1);
            SetSIProp(rotOff, "min",   p => p.vector3Value  = new Vector3(-90f, 0f, 0f));
            SetSIProp(rotOff, "max",   p => p.vector3Value  = new Vector3(-90f, 0f, 0f));
        }
        else Debug.LogWarning("[LevelSelectDesigner] SplineInstantiate: 'm_RotationOffset' not found — check Splines package version.");

        // Prefab
        var items = so.FindProperty("m_ItemsToInstantiate");
        if (items != null)
        {
            items.ClearArray();
            items.InsertArrayElementAtIndex(0);
            var item = items.GetArrayElementAtIndex(0);
            SetSIProp(item, "Prefab",       p => p.objectReferenceValue = prefab);
            SetSIProp(item, "Probability",  p => p.floatValue           = 1f);
        }
        else Debug.LogWarning("[LevelSelectDesigner] SplineInstantiate: 'm_ItemsToInstantiate' not found.");

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(si);
    }

    // Finds a property by name, applies setter if found, warns if not.
    // Uses SerializedObject overload for top-level, SerializedProperty overload for relative.
    private static void SetSIProp(SerializedObject so, string name, System.Action<SerializedProperty> setter)
    {
        var p = so.FindProperty(name);
        if (p != null) setter(p);
        else Debug.LogWarning($"[LevelSelectDesigner] SplineInstantiate property not found: '{name}'");
    }

    private static void SetSIProp(SerializedProperty parent, string name, System.Action<SerializedProperty> setter)
    {
        var p = parent.FindPropertyRelative(name);
        if (p != null) setter(p);
        else Debug.LogWarning($"[LevelSelectDesigner] SplineInstantiate relative property not found: '{name}' (parent: '{parent.name}')");
    }

    // Mirrors SplineToolsWindow.SetupJunctionInstantiate exactly, including pos/rot offsets from preset.
    private static void SetupJunctionInstantiate(
        GameObject go, SplineContainer container, GameObject prefab,
        Vector3 posOffset, Vector3 rotOffset)
    {
        var si = Undo.AddComponent<SplineInstantiate>(go);
        si.Container = container;

        var so = new SerializedObject(si);
        so.Update();
        so.FindProperty("m_Method").enumValueIndex = 0; // InstanceCount
        so.FindProperty("m_Spacing").vector2Value  = new Vector2(1f, 1f);

        if (posOffset != Vector3.zero)
        {
            var pos = so.FindProperty("m_PositionOffset");
            if (pos != null)
            {
                SetSIProp(pos, "setup", p => p.intValue      = 1);
                SetSIProp(pos, "min",   p => p.vector3Value  = posOffset);
                SetSIProp(pos, "max",   p => p.vector3Value  = posOffset);
            }
        }

        if (rotOffset != Vector3.zero)
        {
            var rot = so.FindProperty("m_RotationOffset");
            if (rot != null)
            {
                SetSIProp(rot, "setup", p => p.intValue      = 1);
                SetSIProp(rot, "min",   p => p.vector3Value  = rotOffset);
                SetSIProp(rot, "max",   p => p.vector3Value  = rotOffset);
            }
        }

        var items = so.FindProperty("m_ItemsToInstantiate");
        if (items != null)
        {
            items.ClearArray();
            items.InsertArrayElementAtIndex(0);
            var item = items.GetArrayElementAtIndex(0);
            SetSIProp(item, "Prefab",      p => p.objectReferenceValue = prefab);
            SetSIProp(item, "Probability", p => p.floatValue           = 1f);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(si);
    }

    private static float MeasurePrefabXExtent(GameObject prefab)
        => SplineSplitUtility.MeasurePrefabXExtent(prefab);

    // ══════════════════════════════════════════════════════════════
    // LANDSCAPE MODE
    // ══════════════════════════════════════════════════════════════
    private void HandleLandscapeMode(Event e)
    {
        if (_data == null) return;

        const float HIT_RADIUS = 12f;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            string hit = null;
            float  best = HIT_RADIUS;
            foreach (var hp in _data.hillPoints)
            {
                float d = Vector2.Distance(
                    WorldToCanvas(new Vector3(hp.positionXZ.x, 0, hp.positionXZ.y)),
                    e.mousePosition);
                if (d < best) { best = d; hit = hp.id; }
            }
            _selectedHillPointId = hit;
            _isDraggingHillPoint = hit != null;
            Repaint();
            if (hit != null) e.Use();
        }

        if (e.type == EventType.MouseDrag && _isDraggingHillPoint && _selectedHillPointId != null)
        {
            var hp = _data.hillPoints.Find(h => h.id == _selectedHillPointId);
            if (hp != null)
            {
                Undo.RecordObject(_data, "Move Hill Point");
                var wp = CanvasToWorldPos(e.mousePosition);
                hp.positionXZ = new Vector2(wp.x, wp.z);
                EditorUtility.SetDirty(_data);
                SyncHillPointsToScene();
                Repaint();
                e.Use();
            }
        }

        if (e.type == EventType.MouseUp)
            _isDraggingHillPoint = false;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete && _selectedHillPointId != null)
        {
            Undo.RecordObject(_data, "Delete Hill Point");
            _data.hillPoints.RemoveAll(h => h.id == _selectedHillPointId);
            _selectedHillPointId = null;
            EditorUtility.SetDirty(_data);
            SyncHillPointsToScene();
            e.Use();
        }
    }

    private void DrawLandscapePanel()
    {
        if (_data == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Landscape Tiles", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _data.landscapeTileSize = EditorGUILayout.FloatField("Tile Size",  _data.landscapeTileSize);
        _data.landscapeTilesX   = EditorGUILayout.IntField("Tiles X",     _data.landscapeTilesX);
        _data.landscapeTilesZ   = EditorGUILayout.IntField("Tiles Z",     _data.landscapeTilesZ);
        _data.landscapeOffset   = EditorGUILayout.Vector2Field("Offset XZ", _data.landscapeOffset);
        _data.landscapeWorldY   = EditorGUILayout.FloatField("World Y",   _data.landscapeWorldY);
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_data);
            Repaint();
        }

        EditorGUILayout.Space(4);
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
        if (GUILayout.Button("Generate Tiles", GUILayout.Height(28)))
            GenerateLandscapeTiles();
        GUI.backgroundColor = prevBg;

        if (GUILayout.Button("Clear Tiles", GUILayout.Height(22)))
            ClearLandscapeTiles();

        // ── Hill Points ───────────────────────────────────────────
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Hill Points", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Drag points on canvas  |  Delete key removes", EditorStyles.miniLabel);
        EditorGUILayout.Space(2);

        for (int i = 0; i < _data.hillPoints.Count; i++)
        {
            var  hp  = _data.hillPoints[i];
            bool sel = hp.id == _selectedHillPointId;

            var rowBg = GUI.backgroundColor;
            GUI.backgroundColor = sel ? new Color(1f, 0.95f, 0.4f) : Color.white;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = rowBg;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(sel ? $"● Hill {i}" : $"  Hill {i}", EditorStyles.miniButton))
            {
                _selectedHillPointId = sel ? null : hp.id;
                Repaint();
            }
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                Undo.RecordObject(_data, "Delete Hill Point");
                _data.hillPoints.RemoveAt(i);
                if (_selectedHillPointId == hp.id) _selectedHillPointId = null;
                EditorUtility.SetDirty(_data);
                SyncHillPointsToScene();
                GUI.backgroundColor = rowBg;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            GUI.backgroundColor = rowBg;
            EditorGUILayout.EndHorizontal();

            if (sel)
            {
                EditorGUI.BeginChangeCheck();
                hp.positionXZ = EditorGUILayout.Vector2Field("Position XZ", hp.positionXZ);
                hp.scale      = EditorGUILayout.Slider("Radius",          hp.scale,  0.1f, 50f);
                hp.height     = EditorGUILayout.Slider("Height (- = hole)", hp.height, -15f, 15f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Edit Hill Point");
                    EditorUtility.SetDirty(_data);
                    SyncHillPointsToScene();
                    Repaint();
                }
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("+ Add Hill Point"))
        {
            Undo.RecordObject(_data, "Add Hill Point");
            var wp = CanvasToWorldPos(_canvasRect.center);
            var hp = new LevelSelectDesignerData.LandscapeHillPoint
            {
                id         = Guid.NewGuid().ToString(),
                positionXZ = new Vector2(wp.x, wp.z),
                scale      = 5f,
                height     = 1f,
            };
            _data.hillPoints.Add(hp);
            _selectedHillPointId = hp.id;
            EditorUtility.SetDirty(_data);
            SyncHillPointsToScene();
            Repaint();
        }
    }

    private void DrawLandscapeTilesOnCanvas()
    {
        if (_data == null || Event.current.type != EventType.Repaint) return;
        if (_data.landscapeTilesX <= 0 || _data.landscapeTilesZ <= 0 || _data.landscapeTileSize <= 0) return;

        float size = _data.landscapeTileSize;
        float ox   = _data.landscapeOffset.x;
        float oz   = _data.landscapeOffset.y;

        var fillColor    = new Color(0.38f, 0.38f, 0.38f, 0.55f);
        var outlineColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);

        for (int col = 0; col < _data.landscapeTilesX; col++)
        {
            for (int row = 0; row < _data.landscapeTilesZ; row++)
            {
                float tx = ox + col * size;
                float tz = oz + row * size;

                Vector2 c00 = WorldToCanvas(new Vector3(tx,        0, tz));
                Vector2 c10 = WorldToCanvas(new Vector3(tx + size, 0, tz));
                Vector2 c11 = WorldToCanvas(new Vector3(tx + size, 0, tz + size));
                Vector2 c01 = WorldToCanvas(new Vector3(tx,        0, tz + size));

                Vector3[] corners = {
                    new Vector3(c00.x, c00.y, 0),
                    new Vector3(c10.x, c10.y, 0),
                    new Vector3(c11.x, c11.y, 0),
                    new Vector3(c01.x, c01.y, 0),
                };

                Handles.DrawSolidRectangleWithOutline(corners, fillColor, outlineColor);
            }
        }

        // Hill point circles
        foreach (var hp in _data.hillPoints)
        {
            bool  sel = hp.id == _selectedHillPointId;
            var   cp  = WorldToCanvas(new Vector3(hp.positionXZ.x, 0, hp.positionXZ.y));
            float cr  = Mathf.Max(4f, hp.scale * _zoom);

            // 0 = mid-grey, positive = white, negative = black
            float t          = Mathf.Clamp01((hp.height + 15f) / 30f); // -15..+15 → 0..1
            float brightness = Mathf.Lerp(0f, 1f, t);
            var   fillCol    = new Color(brightness, brightness, brightness, 0.4f);
            var   ringCol    = sel ? Color.yellow : new Color(brightness, brightness, brightness, 0.9f);

            Handles.color = fillCol;
            Handles.DrawSolidDisc(new Vector3(cp.x, cp.y, 0), Vector3.forward, cr);

            Handles.color = ringCol;
            Handles.DrawWireDisc(new Vector3(cp.x, cp.y, 0), Vector3.forward, cr);

            Handles.color = sel ? Color.yellow : Color.white;
            Handles.DrawSolidDisc(new Vector3(cp.x, cp.y, 0), Vector3.forward, 4f);
            Handles.color = Color.white;
        }
    }

    private void GenerateLandscapeTiles()
    {
        if (_data == null) { Debug.LogWarning("[LevelSelectDesigner] No data asset."); return; }
        if (_data.landscapeTilePrefab == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] No landscape tile prefab set in Landscape mode.");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate Landscape Tiles");

        ClearLandscapeTiles();

        // Parent with LandscapeTool component
        var parentGo = FindOrCreateParent("LANDSCAPETILES");
        var tool = parentGo.GetComponent<LandscapeTool>();
        if (tool == null)
            tool = Undo.AddComponent<LandscapeTool>(parentGo);

        // HillPointsContainer child for hill handles
        var hillContainer = new GameObject("Hill_Points_Container");
        Undo.RegisterCreatedObjectUndo(hillContainer, "Create Hill_Points_Container");
        hillContainer.transform.SetParent(parentGo.transform, false);
        tool.hillHandlesParent = hillContainer.transform;

        // Update data's landscapeTool reference so it's wired up
        if (_data.landscapeTool == null)
        {
            _data.landscapeTool = tool;
            EditorUtility.SetDirty(_data);
        }

        float size   = _data.landscapeTileSize;
        float ox     = _data.landscapeOffset.x;
        float oz     = _data.landscapeOffset.y;
        float worldY = _data.landscapeWorldY;

        for (int col = 0; col < _data.landscapeTilesX; col++)
        {
            for (int row = 0; row < _data.landscapeTilesZ; row++)
            {
                var pos = new Vector3(ox + col * size, worldY, oz + row * size);
                var go  = (GameObject)PrefabUtility.InstantiatePrefab(_data.landscapeTilePrefab);
                go.transform.position = pos;
                go.transform.SetParent(parentGo.transform, true);
                Undo.RegisterCreatedObjectUndo(go, "Create Landscape Tile");

                var tileMesh = go.GetComponent<LandscapeTileMesh>();
                if (tileMesh != null)
                {
                    tileMesh.width = size;
                    tileMesh.depth = size;
                    tileMesh.GenerateMesh();
                }
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[LevelSelectDesigner] Generated {_data.landscapeTilesX * _data.landscapeTilesZ} landscape tiles under LANDSCAPETILES.");
    }

    private void ClearLandscapeTiles()
    {
        var go = GameObject.Find("LANDSCAPETILES");
        if (go == null) return;
        var children = Enumerable.Range(0, go.transform.childCount)
            .Select(i => go.transform.GetChild(i).gameObject).ToList();
        foreach (var child in children) Undo.DestroyObjectImmediate(child);
    }

    private void SyncHillPointsToScene()
    {
        var container = FindHillContainer();
        if (container == null) return;

        for (int i = container.childCount - 1; i >= 0; i--)
            DestroyImmediate(container.GetChild(i).gameObject);

        if (_data == null) return;

        foreach (var hp in _data.hillPoints)
        {
            var go = new GameObject($"HillPoint_{hp.id.Substring(0, 6)}");
            go.transform.SetParent(container, false);
            go.transform.position   = new Vector3(hp.positionXZ.x, hp.height, hp.positionXZ.y);
            go.transform.localScale = Vector3.one * hp.scale;
        }
    }

    private Transform FindHillContainer()
    {
        var parent = GameObject.Find("LANDSCAPETILES");
        if (parent == null) return null;
        return parent.transform.Find("Hill_Points_Container");
    }
}

#endif
