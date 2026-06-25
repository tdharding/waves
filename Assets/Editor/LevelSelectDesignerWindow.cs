using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR

public class LevelSelectDesignerWindow : EditorWindow
{
    // ── Modes ─────────────────────────────────────────────────────
    private enum DesignerMode { Draw, Select, Junction, Arena, Obstacle, Shop, Landscape }

    // ── Data ──────────────────────────────────────────────────────
    private LevelSelectDesignerData _sourceData; // The actual asset on disk
    private LevelSelectDesignerData _data;       // The working clone

    private void SetData(LevelSelectDesignerData asset)
    {
        if (asset == _sourceData && _data != null) return;

        if (hasUnsavedChanges)
        {
            if (EditorUtility.DisplayDialog("Unsaved Changes",
                "You have unsaved changes in the current designer data. Do you want to save them before switching?",
                "Save", "Discard"))
            {
                SaveChanges();
            }
        }

        _sourceData = asset;
        _data = asset != null ? Instantiate(asset) : null;

        if (_data != null)
        {
            _data.name = asset.name + " (Workspace)";
            _data.hideFlags = HideFlags.HideAndDontSave;
        }

        hasUnsavedChanges = false;
        if (_sourceData != null)
        {
            EditorPrefs.SetString(K_DataPath, AssetDatabase.GetAssetPath(_sourceData));
            TryOpenLinkedScene();
        }
    }

    public override void SaveChanges()
    {
        if (_sourceData == null || _data == null) return;

        Undo.RecordObject(_sourceData, "Save Level Select Designer Changes");
        EditorUtility.CopySerialized(_data, _sourceData);
        EditorUtility.SetDirty(_sourceData);
        AssetDatabase.SaveAssets();

        hasUnsavedChanges = false;
        _consoleStatusMsg = "Changes saved to asset.";
        base.SaveChanges();
    }

    private void DiscardChangesWithPrompt()
    {
        if (EditorUtility.DisplayDialog("Discard Changes",
            "Are you sure you want to discard all unsaved changes?",
            "Discard", "Cancel"))
        {
            DiscardChanges();
        }
    }

    public override void DiscardChanges()
    {
        SetData(_sourceData);
        base.DiscardChanges();
    }

    private void TryOpenLinkedScene()
    {
        if (_data == null || string.IsNullOrEmpty(_data.targetScenePath)) return;
        if (!System.IO.File.Exists(_data.targetScenePath)) return;

        var active = EditorSceneManager.GetActiveScene();
        if (active.path == _data.targetScenePath) return; // already open

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(_data.targetScenePath);
    }

    private void MarkDirty()
    {
        if (_data != null) EditorUtility.SetDirty(_data);
        hasUnsavedChanges = true;
    }

    // ── Interaction state ─────────────────────────────────────────
    private DesignerMode _mode = DesignerMode.Select;
    private string _selectedPathId;
    private string _selectedNodeId;
    private string _selectedObstacleId;
    private string _selectedArenaNodeId;
    private string _selectedJunctionNodeId;
    private string _selectedShopNodeId;
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

    // ── Setup sub-foldouts ────────────────────────────────────────
    private bool _foldSetupScripts   = false;
    private bool _foldSetupUIScript  = false;
    private bool _foldSetupUICanvas  = false;
    private bool _foldSetupRiver     = false;
    private bool _foldSetupJunctions = false;
    private bool _foldSetupArenas    = false;
    private bool _foldSetupObstacles = false;
    private bool _foldSetupCore      = false;
    private bool _foldConsole     = true;

    // ── Debug console ─────────────────────────────────────────────
    // (isError=true → red error, false → yellow warning, null msg → green ok)
    private List<(bool isError, string msg, string pathId, string juncId)> _consoleEntries = new();
    private bool   _consoleHasErrors;
    private string _consoleStatusMsg;   // pinned action feedback line (save/restore etc.)

    // ── Landscape canvas ──────────────────────────────────────────
    private float _landscapeCanvasAlpha = 0.56f;
    private const float ARENA_OUTER_RING_RADIUS = 4f;   // world-space outer arena ring
    private const float ARENA_INNER_RING_RADIUS = 2f;   // world-space inner orbit ring

    // Draw mode
    private List<string> _drawingNodeIds = new();
    private bool   _isDrawing;
    private bool   _isExtending;
    private string _extendingPathId;

    // Select mode drag
    private bool   _isDraggingNode;
    private string _draggingNodeId;
    private Vector2 _dragOffset;

    // Select mode obstacle drag
    private bool   _isDraggingObstacle;
    private string _draggingObstacleId;

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

    // ── GridData resource cache ───────────────────────────────────
    private GridData[] _gridDataOptions;
    private string[]   _gridDataLabels;

    // ── Shop item prefab cache ────────────────────────────────────
    private const string ShopItemsDir = "Assets/Prefab/SHOPITEMS";
    private GameObject[] _shopItemOptions;
    private string[]     _shopItemLabels;

    private void EnsureGridDataCache()
    {
        if (_gridDataOptions != null) return;
        RefreshGridDataCache();
    }

    private void RefreshGridDataCache()
    {
        var guids = AssetDatabase.FindAssets("", new[] { "Assets/Resources/Levels" });
        var list  = new System.Collections.Generic.List<GridData>();
        foreach (var guid in guids)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GridData>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) list.Add(asset);
        }
        _gridDataOptions   = list.ToArray();
        _gridDataLabels    = new string[_gridDataOptions.Length + 1];
        _gridDataLabels[0] = "(none)";
        for (int i = 0; i < _gridDataOptions.Length; i++)
            _gridDataLabels[i + 1] = string.IsNullOrEmpty(_gridDataOptions[i].displayName)
                ? _gridDataOptions[i].name
                : _gridDataOptions[i].displayName;
        Debug.Log($"[LevelSelectDesigner] GridData cache: {_gridDataOptions.Length} asset(s) found.");
    }

    // ── Secondary entrance helpers ─────────────────────────────────

    private LevelSelectDesignerData.DesignerArena FindArenaForSecondaryNode(string nodeId)
    {
        foreach (var a in _data.arenas)
            foreach (var e in a.secondaryEntrances)
                if (e.nodeId == nodeId) return a;
        return null;
    }

    private LevelSelectDesignerData.DesignerArenaEntrance FindSecondaryEntrance(string nodeId)
    {
        foreach (var a in _data.arenas)
            foreach (var e in a.secondaryEntrances)
                if (e.nodeId == nodeId) return e;
        return null;
    }

    private bool IsSecondaryEntranceNode(string nodeId) => FindArenaForSecondaryNode(nodeId) != null;

    private void SyncEntranceNodes(LevelSelectDesignerData.DesignerArena arena)
    {
        int entranceCount = arena.gridData?.entrances?.Count ?? 1;
        int needed        = Mathf.Max(0, entranceCount - 1);

        // Remove excess secondary nodes
        while (arena.secondaryEntrances.Count > needed)
        {
            var last = arena.secondaryEntrances[arena.secondaryEntrances.Count - 1];
            _data.nodes.RemoveAll(n => n.id == last.nodeId);
            arena.secondaryEntrances.RemoveAt(arena.secondaryEntrances.Count - 1);
        }

        if (needed == 0) return;

        var primary = _data.nodes.Find(n => n.id == arena.nodeId);
        if (primary == null) return;

        // Add missing secondary nodes, spread evenly around the orbit ring
        float arenaRadius = GetArenaRadius(arena);
        while (arena.secondaryEntrances.Count < needed)
        {
            int   i     = arena.secondaryEntrances.Count;
            float angle = ((i + 1) * 360f / (needed + 1)) * Mathf.Deg2Rad;
            var offset  = new Vector3(
                Mathf.Sin(angle) * arenaRadius,
                0f,
                Mathf.Cos(angle) * arenaRadius);

            var newNode = new LevelSelectDesignerData.DesignerNode
            {
                id            = System.Guid.NewGuid().ToString(),
                type          = LevelSelectDesignerData.NodeType.ArenaEnd,
                worldPosition = primary.worldPosition + offset
            };
            _data.nodes.Add(newNode);

            arena.secondaryEntrances.Add(new LevelSelectDesignerData.DesignerArenaEntrance
            {
                nodeId        = newNode.id,
                entranceIndex = i + 1
            });
        }

        MarkDirty();
    }

    private string GetArenaPresetName(LevelSelectDesignerData.DesignerArena arena)
    {
        var profile = arena.gridData?.arenaProfile;
        return profile != null ? profile.name : "None";
    }

    private GridData DrawGridDataPopup(string label, GridData current)
    {
        EnsureGridDataCache();
        int currentIdx = 0;
        for (int i = 0; i < _gridDataOptions.Length; i++)
            if (_gridDataOptions[i] == current) { currentIdx = i + 1; break; }
        int selected = EditorGUILayout.Popup(label, currentIdx, _gridDataLabels);
        return selected == 0 ? null : _gridDataOptions[selected - 1];
    }

    private void EnsureShopItemCache()
    {
        if (_shopItemOptions != null) return;
        RefreshShopItemCache();
    }

    private void RefreshShopItemCache()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { ShopItemsDir });
        var list  = new System.Collections.Generic.List<GameObject>();
        foreach (var guid in guids)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab != null) list.Add(prefab);
        }
        _shopItemOptions    = list.ToArray();
        _shopItemLabels     = new string[_shopItemOptions.Length + 1];
        _shopItemLabels[0]  = "(none)";
        for (int i = 0; i < _shopItemOptions.Length; i++)
            _shopItemLabels[i + 1] = _shopItemOptions[i].name;
    }

    private GameObject DrawShopItemPopup(string label, GameObject current)
    {
        EnsureShopItemCache();
        int currentIdx = 0;
        for (int i = 0; i < _shopItemOptions.Length; i++)
            if (_shopItemOptions[i] == current) { currentIdx = i + 1; break; }
        int selected = EditorGUILayout.Popup(label, currentIdx, _shopItemLabels);
        return selected == 0 ? null : _shopItemOptions[selected - 1];
    }
    private bool    _isResizingLeft;
    private bool    _isResizingRight;
    private Vector2 _leftScroll;
    private Vector2 _rightScroll;

    private const float HANDLE_W    = 5f;
    private const float PANEL_MIN_W = 120f;
    private const float PANEL_MAX_W = 600f;

    // ── EditorPrefs keys ──────────────────────────────────────────
    private const string K_SetupTemplate = "Assets/Resources/Levels/_LevelSelectSetupTemplate.json";
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
        {
            var asset = AssetDatabase.LoadAssetAtPath<LevelSelectDesignerData>(savedPath);
            if (asset != null)
                SetData(asset);
        }

        _splitPreset = AssetDatabase.LoadAssetAtPath<SplineSplitterPreset>(PresetPath);

        if (_data != null)
            TryAutoFillPrefabs();

        Undo.undoRedoPerformed += OnUndoRedoPerformed;
    }

    private void OnDisable()
    {
        SaveViewPrefs();
        if (_sourceData != null)
            EditorPrefs.SetString(K_DataPath, AssetDatabase.GetAssetPath(_sourceData));

        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
    }

    private void OnUndoRedoPerformed()
    {
        MarkDirty();
        Repaint();
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
        if (Event.current.type == EventType.Layout)
            RunValidation();

        DrawToolbar();

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
        DrawLeftPanel();
        DrawPanelHandle(ref _isResizingLeft,  ref _leftPanelWidth,  +1f);
        DrawCanvas();
        DrawPanelHandle(ref _isResizingRight, ref _rightPanelWidth, -1f);
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck())
        {
            MarkDirty();
        }
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
            _sourceData, typeof(LevelSelectDesignerData), false, GUILayout.Width(220));
        if (EditorGUI.EndChangeCheck())
            SetData(newData);

        // Scene picker — stored on the data asset
        if (_data != null)
        {
            var linkedScene = string.IsNullOrEmpty(_data.targetScenePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(_data.targetScenePath);

            EditorGUI.BeginChangeCheck();
            var pickedScene = (SceneAsset)EditorGUILayout.ObjectField(
                linkedScene, typeof(SceneAsset), false, GUILayout.Width(160));
            if (EditorGUI.EndChangeCheck())
            {
                _data.targetScenePath = pickedScene != null
                    ? AssetDatabase.GetAssetPath(pickedScene) : "";
                // Also write through to source so it persists without a full Save
                if (_sourceData != null)
                {
                    _sourceData.targetScenePath = _data.targetScenePath;
                    EditorUtility.SetDirty(_sourceData);
                    AssetDatabase.SaveAssetIfDirty(_sourceData);
                }
                if (pickedScene != null) TryOpenLinkedScene();
            }

            if (!string.IsNullOrEmpty(_data.targetScenePath))
            {
                var activeScene = EditorSceneManager.GetActiveScene();
                bool isOpen = activeScene.path == _data.targetScenePath;
                GUI.enabled = !isOpen;
                if (GUILayout.Button(isOpen ? "Scene Open" : "Open Scene",
                        EditorStyles.toolbarButton, GUILayout.Width(80)))
                    TryOpenLinkedScene();
                GUI.enabled = true;
            }
        }

        if (hasUnsavedChanges)
        {
            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(40)))
                SaveChanges();
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Discard", EditorStyles.toolbarButton, GUILayout.Width(55)))
                DiscardChangesWithPrompt();
}

        if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(36)))
            CreateNewDataAsset();

        if (GUILayout.Button("Load", EditorStyles.toolbarButton, GUILayout.Width(38)))
            LoadDataAsset();

        GUI.enabled = _data != null;
        if (GUILayout.Button("Save As", EditorStyles.toolbarButton, GUILayout.Width(54)))
            SaveAsDataAsset();
        GUI.enabled = true;

        GUILayout.Space(12);

        string[] modeLabels = { "Draw", "Select", "Junction", "Arena", "Obstacle", "Shop", "Landscape" };
        var newMode = (DesignerMode)GUILayout.Toolbar((int)_mode, modeLabels,
            EditorStyles.toolbarButton, GUILayout.Height(18));
        if (newMode != _mode)
        {
            _mode = newMode;
            _isDrawing          = false;
            _isDraggingObstacle = false;
            _draggingObstacleId = null;
            _drawingNodeIds.Clear();
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Frame All", EditorStyles.toolbarButton, GUILayout.Width(64)))
            FrameAll();

        EditorGUILayout.EndHorizontal();
    }

    private void CreateNewDataAsset()
    {
        string dataPath = EditorUtility.SaveFilePanelInProject(
            "New Level Select Data", "LevelSelectWorldData", "asset",
            "Choose location for the data asset", DataDir);
        if (string.IsNullOrEmpty(dataPath)) return;

        // Derive scene name: strip trailing "Data" from filename if present
        string baseName = System.IO.Path.GetFileNameWithoutExtension(dataPath);
        string sceneName = baseName.EndsWith("Data")
            ? baseName.Substring(0, baseName.Length - 4)
            : baseName;

        // Create the data asset
        var newAsset = CreateInstance<LevelSelectDesignerData>();
        newAsset.openingSequenceStartPos = OpeningSequenceDefaultStart;
        SeedMainRiverStartNode(newAsset);

        // Create and link the scene
        string scenePath = CreateLinkedScene(sceneName, newAsset);
        newAsset.targetScenePath = scenePath;

        AssetDatabase.CreateAsset(newAsset, dataPath);
        AssetDatabase.SaveAssets();

        SetData(newAsset);
        TryAutoFillPrefabs();
        TryApplySetupTemplate();

        // Open the new scene
        if (!string.IsNullOrEmpty(scenePath))
            EditorSceneManager.OpenScene(scenePath);

        MarkDirty();
    }

    private static string CreateLinkedScene(string sceneName, LevelSelectDesignerData asset)
    {
        string destPath = $"{ScenesDir}/{sceneName}.unity";

        if (System.IO.File.Exists(destPath))
        {
            Debug.Log($"[LevelSelectDesigner] Scene already exists at {destPath} — linking without overwrite.");
            return destPath;
        }

        // Find the best template: prefer LevelSelectWorld1a, fall back to any LevelSelectWorld scene
        string templatePath = null;
        foreach (var s in EditorBuildSettings.scenes)
        {
            string n = System.IO.Path.GetFileNameWithoutExtension(s.path);
            if (n == "LevelSelectWorld1a") { templatePath = s.path; break; }
            if (n.StartsWith("LevelSelectWorld") && templatePath == null)
                templatePath = s.path;
        }

        if (templatePath == null)
        {
            // Last resort: scan the scenes folder directly
            var found = AssetDatabase.FindAssets("t:Scene LevelSelectWorld", new[] { ScenesDir });
            if (found.Length > 0)
                templatePath = AssetDatabase.GUIDToAssetPath(found[0]);
        }

        if (templatePath == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] No LevelSelectWorld template scene found — scene not created.");
            return "";
        }

        // Ensure destination directory exists
        System.IO.Directory.CreateDirectory(ScenesDir);

        if (!AssetDatabase.CopyAsset(templatePath, destPath))
        {
            Debug.LogWarning($"[LevelSelectDesigner] Failed to copy template scene to {destPath}.");
            return "";
        }

        AssetDatabase.Refresh();

        // Add to Build Settings (disabled by default so user opts in)
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == destPath))
        {
            scenes.Add(new EditorBuildSettingsScene(destPath, false));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        Debug.Log($"[LevelSelectDesigner] Created scene '{sceneName}' at {destPath} (added to Build Settings, disabled — enable when ready).");
        return destPath;
    }

    // Default main river stub nodes — derived from average of World1 + World2 data.
    // Direction: strong -X with slight -Z drift from the opening sequence start.
    private static readonly Vector3[] MainRiverSeedOffsets =
    {
        new Vector3(  0f,   0f,   0f),   // [0] opening sequence start (filled from openingSequenceStartPos)
        new Vector3(-61f,   0f,  -2.5f), // [1] ~(82, 0, -15) — first intermediate
        new Vector3(-78f,   0f,  -2.7f), // [2] ~(65, 0, -15)
        new Vector3(-93f,   0f,  -2.4f), // [3] ~(50, 0, -15)
    };

    private static void SeedMainRiverStartNode(LevelSelectDesignerData data)
    {
        // Don't add a second main river if one already exists
        if (data.paths.Exists(p => p.segmentType == LevelSelectDesignerData.SegmentType.MainRiver))
            return;

        var nodeIds = new System.Collections.Generic.List<string>();
        foreach (var offset in MainRiverSeedOffsets)
        {
            var node = new LevelSelectDesignerData.DesignerNode
            {
                id            = System.Guid.NewGuid().ToString(),
                worldPosition = data.openingSequenceStartPos + offset,
                type          = LevelSelectDesignerData.NodeType.Waypoint
            };
            data.nodes.Add(node);
            nodeIds.Add(node.id);
        }

        var path = new LevelSelectDesignerData.DesignerPath
        {
            pathId      = System.Guid.NewGuid().ToString(),
            segmentId   = "MainRiver",
            riverName   = "MainRiver",
            segmentType = LevelSelectDesignerData.SegmentType.MainRiver,
            nodeIds     = nodeIds,
            editorColor = Color.cyan
        };
        data.paths.Add(path);
    }

    private void LoadDataAsset()
    {
        string absPath = EditorUtility.OpenFilePanel("Load Designer Data",
            System.IO.Path.Combine(Application.dataPath.Replace("Assets", ""), DataDir), "asset");
        if (string.IsNullOrEmpty(absPath)) return;

        string relPath = absPath;
        if (absPath.StartsWith(Application.dataPath))
            relPath = "Assets" + absPath.Substring(Application.dataPath.Length);

        var loaded = AssetDatabase.LoadAssetAtPath<LevelSelectDesignerData>(relPath);
        if (loaded == null)
        {
            _consoleStatusMsg = $"Load failed — not a LevelSelectDesignerData asset: {relPath}";
            Repaint();
            return;
        }

        SetData(loaded);
        TryAutoFillPrefabs();
        _consoleStatusMsg = $"Loaded → {relPath}";
        Repaint();
    }

    private void SaveAsDataAsset()
    {
        if (_data == null) return;

        string sourcePath = _sourceData != null ? AssetDatabase.GetAssetPath(_sourceData) : "";
        string defaultName = string.IsNullOrEmpty(sourcePath)
            ? "LevelSelectDesignerData"
            : System.IO.Path.GetFileNameWithoutExtension(sourcePath) + "_copy";
        string defaultDir = string.IsNullOrEmpty(sourcePath) ? DataDir : System.IO.Path.GetDirectoryName(sourcePath);

        string destPath = EditorUtility.SaveFilePanelInProject(
            "Save Designer Data As", defaultName, "asset", "Choose location", defaultDir);
        if (string.IsNullOrEmpty(destPath)) return;

        var newAsset = Instantiate(_data);
        newAsset.hideFlags = HideFlags.None;
        AssetDatabase.CreateAsset(newAsset, destPath);
        AssetDatabase.SaveAssets();

        SetData(newAsset);
        _consoleStatusMsg = $"Saved As → {destPath}";
        Repaint();
    }

    private const string PrefabDir   = "Assets/Prefab/LevelSelectPrefabs";
    private const string DataDir     = "Assets/ScriptsData/DataScripts/LevelSelectWorldData";
    private const string ScenesDir   = "Assets/Scenes/LevelSelectWorlds";

    // Canonical world-space position shared by all level select opening sequences
    private static readonly Vector3 OpeningSequenceDefaultStart = new Vector3(143.3325f, 0f, -12.6230f);

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
            if (_data.videoPlayerControllerPrefab == null && name.Contains("videosphere")) { _data.videoPlayerControllerPrefab = prefab; dirty = true; }
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

    private void TryApplySetupTemplate()
    {
        // Prefer World1's backup; fall back to any _SetupBackup.json in the data folder
        string world1Backup = System.IO.Path.Combine(DataDir, "LevelSelectWorld1Data_SetupBackup.json");
        if (System.IO.File.Exists(world1Backup))
        {
            ApplySetupFromFile(world1Backup);
            _consoleStatusMsg = "Setup pre-filled from LevelSelectWorld1Data template.";
            return;
        }

        string[] candidates = System.IO.Directory.GetFiles(DataDir, "*_SetupBackup.json");
        if (candidates.Length > 0)
        {
            ApplySetupFromFile(candidates[0]);
            _consoleStatusMsg = $"Setup pre-filled from {System.IO.Path.GetFileName(candidates[0])}.";
        }
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
        Rect leftRect = EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth), GUILayout.ExpandHeight(true));
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(leftRect, new Color(0.22f, 0.22f, 0.22f, 1f));
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
        DrawSelectedShopProps();
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
        // ── River ID Cleanup ──────────────────────────────────────
        var cleanupBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.6f, 0.85f, 1f);
        if (GUILayout.Button("River ID Cleanup", EditorStyles.miniButton, GUILayout.Height(20)))
            RiverIdCleanup();
        GUI.backgroundColor = cleanupBg;
        EditorGUILayout.Space(2);

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
                MarkDirty();
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
            MarkDirty();
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
        path.leadsToArena          = EditorGUILayout.Toggle("Leads to Arena",        path.leadsToArena);
        path.arenaIsAtEnd          = EditorGUILayout.Toggle("Arena at End",          path.arenaIsAtEnd);
        path.extrudeOnExit         = EditorGUILayout.Toggle("Extrude on Exit",       path.extrudeOnExit);
        path.tJunctionBidirectional = EditorGUILayout.Toggle("T-Junction Both Ways", path.tJunctionBidirectional);
        EditorGUILayout.Space(2);
        path.curveStrength     = EditorGUILayout.Slider("Curve Strength",      path.curveStrength,     0.1f, 3f);
        path.curveSubdivisions = EditorGUILayout.IntSlider("Curve Subdivisions", path.curveSubdivisions, 1,    20);

        EditorGUILayout.LabelField($"Knots: {path.nodeIds.Count}", EditorStyles.miniLabel);

        // Arena link info — shown when path leads to an arena
        if (path.leadsToArena && path.nodeIds.Count > 0)
        {
            string terminalNodeId = path.arenaIsAtEnd
                ? path.nodeIds[path.nodeIds.Count - 1]
                : path.nodeIds[0];

            var linkedArena = _data.arenas.Find(a =>
                a.nodeId == terminalNodeId ||
                a.secondaryEntrances.Any(s => s.nodeId == terminalNodeId));

            EditorGUILayout.Space(4);
            var boxStyle = new GUIStyle(EditorStyles.helpBox);
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Arena Link", EditorStyles.miniBoldLabel);

            if (linkedArena != null)
            {
                string levelID     = linkedArena.gridData != null ? linkedArena.gridData.levelID     : "—";
                string displayName = linkedArena.gridData != null ? linkedArena.gridData.displayName : "—";

                bool isSecondary = linkedArena.nodeId != terminalNodeId;
                int  entranceIdx = isSecondary
                    ? linkedArena.secondaryEntrances.Find(s => s.nodeId == terminalNodeId)?.entranceIndex ?? -1
                    : linkedArena.entranceIndex;

                string entranceId = "—";
                if (linkedArena.gridData != null &&
                    entranceIdx >= 0 && entranceIdx < linkedArena.gridData.entrances.Count)
                    entranceId = linkedArena.gridData.entrances[entranceIdx].id;

                GUI.enabled = false;
                EditorGUILayout.ObjectField("GridData", linkedArena.gridData, typeof(GridData), false);
                GUI.enabled = true;
                EditorGUILayout.LabelField("Level ID",      levelID,     EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Display Name",  displayName, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Entrance ID",   entranceId,  EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("No arena found at terminal node", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Path");
            MarkDirty();
        }

        // ── Node Y heights ────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Node Heights (Y)", EditorStyles.boldLabel);
        for (int i = 0; i < path.nodeIds.Count; i++)
        {
            var node = _data.nodes.Find(n => n.id == path.nodeIds[i]);
            if (node == null) continue;

            string typeLabel = node.type switch
            {
                LevelSelectDesignerData.NodeType.JunctionSplit => "◆",
                LevelSelectDesignerData.NodeType.ArenaEnd      => "▲",
                LevelSelectDesignerData.NodeType.ShopEnd       => "★",
                _                                              => "●"
            };

            bool isSelected = node.id == _selectedNodeId;
            var prevBg = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{typeLabel} {i}", GUILayout.Width(36));
            EditorGUI.BeginChangeCheck();
            float newY = EditorGUILayout.FloatField(node.worldPosition.y, GUILayout.Width(52));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data, "Edit Node Y");
                node.worldPosition = new Vector3(node.worldPosition.x, newY, node.worldPosition.z);
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = prevBg;
        }

        // ── Arena info ────────────────────────────────────────────
        EditorGUILayout.Space(4);

        // End toggle — synced with arenaIsAtEnd checkbox above
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = path.nodeIds.Count > 0;
        var startBg = GUI.backgroundColor;
        GUI.backgroundColor = !path.arenaIsAtEnd ? new Color(0.3f, 0.7f, 1f) : Color.gray;
        if (GUILayout.Button("Start", EditorStyles.miniButtonLeft) && path.arenaIsAtEnd)
        {
            Undo.RecordObject(_data, "Move Arena to Start");
            MoveArenaToEnd(path, atEnd: false);
        }
        GUI.backgroundColor = path.arenaIsAtEnd ? new Color(0.3f, 0.7f, 1f) : Color.gray;
        if (GUILayout.Button("End", EditorStyles.miniButtonRight) && !path.arenaIsAtEnd)
        {
            Undo.RecordObject(_data, "Move Arena to End");
            MoveArenaToEnd(path, atEnd: true);
        }
        GUI.backgroundColor = startBg;
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (!path.leadsToArena || path.nodeIds.Count == 0)
        {
            GUI.enabled = path.nodeIds.Count > 0;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
            if (GUILayout.Button("Add Arena", GUILayout.Height(22)))
            {
                Undo.RecordObject(_data, "Add Arena");
                string targetNodeId = path.arenaIsAtEnd
                    ? path.nodeIds[path.nodeIds.Count - 1]
                    : path.nodeIds[0];
                var targetNode = _data.nodes.Find(n => n.id == targetNodeId);
                if (targetNode != null)
                {
                    targetNode.type = LevelSelectDesignerData.NodeType.ArenaEnd;
                    if (!_data.arenas.Exists(a => a.nodeId == targetNodeId))
                        _data.arenas.Add(new LevelSelectDesignerData.DesignerArena { nodeId = targetNodeId });
                    path.leadsToArena = true;
                    _selectedArenaNodeId = targetNodeId;
                    EditorUtility.SetDirty(_data);
                }
            }
            GUI.backgroundColor = prevBg;
            GUI.enabled = true;
        }

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
                    EditorGUILayout.LabelField($"Entrances: {arena.gridData.entrances?.Count ?? 0}  |  Profile: {GetArenaPresetName(arena)}", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("Arena has no GridData assigned.", MessageType.Info);
                }

                EditorGUI.BeginChangeCheck();
                arena.gridData = DrawGridDataPopup("GridData", arena.gridData);
                arena.arenaPrefabOverride = (GameObject)EditorGUILayout.ObjectField(
                    "Prefab Override", arena.arenaPrefabOverride, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Edit Arena");
                    SyncEntranceNodes(arena);
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
        obs.hasVideoOrb    = EditorGUILayout.Toggle("Has Video Orb",   obs.hasVideoOrb);
        obs.obstaclePrefab = (GameObject)EditorGUILayout.ObjectField(
            "Prefab Override", obs.obstaclePrefab, typeof(GameObject), false);

        var siblings = _data.obstacles.Where(o => o.pathId == obs.pathId).OrderBy(o => o.pathT).ToList();
        int idx = siblings.IndexOf(obs);
        EditorGUILayout.LabelField($"Chain order: {idx + 1} of {siblings.Count}", EditorStyles.miniLabel);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Obstacle");
            MarkDirty();
        }

        EditorGUILayout.Space(2);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("Delete Obstacle"))
        {
            Undo.RecordObject(_data, "Delete Obstacle");
            _data.obstacles.Remove(obs);
            _selectedObstacleId = null;
            MarkDirty();
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
        arena.gridData = DrawGridDataPopup("GridData", arena.gridData);
        arena.arenaPrefabOverride = (GameObject)EditorGUILayout.ObjectField(
            "Prefab Override", arena.arenaPrefabOverride, typeof(GameObject), false);

        if (arena.gridData != null)
        {
            EditorGUILayout.LabelField(
                $"{arena.gridData.displayName}  |  {arena.gridData.entrances?.Count ?? 0} entrance(s)  |  Profile: {GetArenaPresetName(arena)}",
                EditorStyles.miniLabel);
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Arena");
            SyncEntranceNodes(arena);
            _selectedEntranceIdx = -1;
            MarkDirty();
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

                // GridData entrance ID
                string gridEntranceId = (entrances != null && i < entrances.Count)
                    ? entrances[i].id : "—";

                // Which designer path connects to this entrance
                string linkedSegId = "—";
                if (i == arena.entranceIndex)
                {
                    var p = _data.paths.FirstOrDefault(x => x.leadsToArena &&
                        x.nodeIds.Count > 0 &&
                        (x.arenaIsAtEnd
                            ? x.nodeIds[x.nodeIds.Count - 1] == arena.nodeId
                            : x.nodeIds[0] == arena.nodeId));
                    if (p != null) linkedSegId = p.segmentId ?? p.pathId;
                }
                else
                {
                    var sec = arena.secondaryEntrances.Find(s => s.entranceIndex == i);
                    if (sec != null)
                    {
                        var p = _data.paths.FirstOrDefault(x => x.leadsToArena &&
                            x.nodeIds.Count > 0 &&
                            (x.arenaIsAtEnd
                                ? x.nodeIds[x.nodeIds.Count - 1] == sec.nodeId
                                : x.nodeIds[0] == sec.nodeId));
                        if (p != null) linkedSegId = p.segmentId ?? p.pathId;
                    }
                }

                EditorGUILayout.BeginHorizontal();
                string label = $"Entrance {i}" + (isBranchEntry ? "  ← branch" : "");
                if (GUILayout.Button(label, EditorStyles.miniLabel))
                {
                    _selectedEntranceIdx = isSelected ? -1 : i;
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("GridData ID", gridEntranceId, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Linked Path", linkedSegId,    EditorStyles.miniLabel);

                // Mark which entrance connects to the branch
                EditorGUI.BeginChangeCheck();
                bool isBranch = EditorGUILayout.Toggle("Branch Entrance", isBranchEntry);
                if (EditorGUI.EndChangeCheck() && isBranch)
                {
                    Undo.RecordObject(_data, "Set Branch Entrance");
                    arena.entranceIndex = i;
                    EditorUtility.SetDirty(_data);
                }

                // Angle from arena centre to this entrance
                var   primNode = _data.nodes.Find(n => n.id == arena.nodeId);
                float angle    = 0f;
                bool  hasAngle = false;
                if (i == 0)
                {
                    angle = GetArenaArrivalYAngle(arena); hasAngle = true;
                }
                else
                {
                    var sec = arena.secondaryEntrances.Find(s => s.entranceIndex == i);
                    var sn  = sec != null ? _data.nodes.Find(n => n.id == sec.nodeId) : null;
                    if (sn != null && primNode != null)
                    {
                        Vector3 d = sn.worldPosition - primNode.worldPosition; d.y = 0f;
                        if (d.sqrMagnitude > 0.0001f) { angle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; hasAngle = true; }
                    }
                }
                if (hasAngle)
                    EditorGUILayout.LabelField("Angle", $"{angle:F1}°", EditorStyles.miniLabel);

                EditorGUILayout.EndVertical();
            }
        }
    }

    private void DrawSelectedShopProps()
    {
        if (string.IsNullOrEmpty(_selectedShopNodeId)) return;
        var shop = _data.shops.Find(s => s.nodeId == _selectedShopNodeId);
        if (shop == null) return;

        // Ensure array is always 4 slots
        if (shop.shopItems == null || shop.shopItems.Length != 4)
        {
            var resized = new GameObject[4];
            if (shop.shopItems != null)
                for (int i = 0; i < Mathf.Min(shop.shopItems.Length, 4); i++)
                    resized[i] = shop.shopItems[i];
            shop.shopItems = resized;
            MarkDirty();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Shop Items", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        for (int i = 0; i < 4; i++)
            shop.shopItems[i] = DrawShopItemPopup($"Slot {i + 1}", shop.shopItems[i]);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Shop Items");
            MarkDirty();
        }

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.5f, 0.7f, 1f);
        if (GUILayout.Button("Refresh Item List", EditorStyles.miniButton))
        {
            _shopItemOptions = null;
            EnsureShopItemCache();
        }
        GUI.backgroundColor = prevBg;
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
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawUIScriptPrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.cameraPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Camera",             _data.cameraPrefab,              typeof(GameObject), false);
        _data.soulsOnBoatDisplayScriptPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Souls Display Mgr",  _data.soulsOnBoatDisplayScriptPrefab, typeof(GameObject), false);
        _data.arenaSoulsWindowPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Arena Souls Window", _data.arenaSoulsWindowPrefab,         typeof(GameObject), false);
        _data.pauseManagerScriptPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Pause Manager",      _data.pauseManagerScriptPrefab,       typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawUICanvasPrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.canvasParentPrefab = (GameObject)EditorGUILayout.ObjectField(
            "CANVAS Parent",        _data.canvasParentPrefab,       typeof(GameObject), false);
        _data.pauseMenuPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Pause Menu UI",        _data.pauseMenuPrefab,          typeof(GameObject), false);
        _data.boatHUDPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Boat HUD Prompts",     _data.boatHUDPrefab,             typeof(GameObject), false);
        _data.soulsOnBoatDisplayPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Souls Display Bar UI", _data.soulsOnBoatDisplayPrefab,  typeof(GameObject), false);
        _data.orbsCounterPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Orbs Counter UI",      _data.orbsCounterPrefab,         typeof(GameObject), false);
        _data.shopTooltipPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Shop Tooltip UI",      _data.shopTooltipPrefab,         typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawRiverPrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Split Preset", GUILayout.Width(EditorGUIUtility.labelWidth));
        _splitPreset = (SplineSplitterPreset)EditorGUILayout.ObjectField(_splitPreset, typeof(SplineSplitterPreset), false);
        EditorGUILayout.EndHorizontal();
        _data.pathPrefab               = (GameObject)EditorGUILayout.ObjectField("Path Prefab",    _data.pathPrefab,               typeof(GameObject), false);
        _data.branchWaterExtrudePrefab = (GameObject)EditorGUILayout.ObjectField("Branch Extrude", _data.branchWaterExtrudePrefab, typeof(GameObject), false);
        _data.barrierPrefab            = (GameObject)EditorGUILayout.ObjectField("Barrier",         _data.barrierPrefab,            typeof(GameObject), false);
        _data.riverBlockPrefab         = (GameObject)EditorGUILayout.ObjectField("Block",           _data.riverBlockPrefab,         typeof(GameObject), false);
        _data.splineInstantiateSpacing = EditorGUILayout.FloatField("Block Spacing",                _data.splineInstantiateSpacing);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawJunctionPrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.junctionScriptObject      = (GameObject)EditorGUILayout.ObjectField("Script Object", _data.junctionScriptObject,      typeof(GameObject), false);
        _data.junctionRightFacingPrefab = (GameObject)EditorGUILayout.ObjectField("Right Facing",  _data.junctionRightFacingPrefab, typeof(GameObject), false);
        _data.junctionLeftFacingPrefab  = (GameObject)EditorGUILayout.ObjectField("Left Facing",   _data.junctionLeftFacingPrefab,  typeof(GameObject), false);
        _data.junctionPrefab            = (GameObject)EditorGUILayout.ObjectField("Fallback",       _data.junctionPrefab,            typeof(GameObject), false);
        _data.junctionGapPadding        = EditorGUILayout.FloatField("Gap Padding",                 _data.junctionGapPadding);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawArenaPrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.arenaPrefab         = (GameObject)EditorGUILayout.ObjectField("Arena Head",     _data.arenaPrefab,         typeof(GameObject), false);
        _data.arenaEntrancePrefab = (GameObject)EditorGUILayout.ObjectField("Arena Entrance", _data.arenaEntrancePrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawObstacleShopPrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.obstaclePrefab = (GameObject)EditorGUILayout.ObjectField("Obstacle", _data.obstaclePrefab, typeof(GameObject), false);
        _data.shopPrefab     = (GameObject)EditorGUILayout.ObjectField("Shop",     _data.shopPrefab,     typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawCorePrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.dataControllerPrefab        = (GameObject)EditorGUILayout.ObjectField("Data Controller",   _data.dataControllerPrefab,        typeof(GameObject), false);
        _data.boatPrefab                  = (GameObject)EditorGUILayout.ObjectField("Boat",              _data.boatPrefab,                   typeof(GameObject), false);
        _data.landscapeTilePrefab         = (GameObject)EditorGUILayout.ObjectField("Landscape Tile",    _data.landscapeTilePrefab,          typeof(GameObject), false);
        _data.videoPlayerControllerPrefab = (GameObject)EditorGUILayout.ObjectField("Video Controller",  _data.videoPlayerControllerPrefab,  typeof(GameObject), false);
        _data.openingSequencePrefab       = (GameObject)EditorGUILayout.ObjectField("Opening Sequence",  _data.openingSequencePrefab,        typeof(GameObject), false);
        _data.musicIntro                  = (AudioClip)EditorGUILayout.ObjectField("Music Intro",        _data.musicIntro,                   typeof(AudioClip),  false);
        _data.musicLoop                   = (AudioClip)EditorGUILayout.ObjectField("Music Loop",         _data.musicLoop,                    typeof(AudioClip),  false);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    // ══════════════════════════════════════════════════════════════
    // SETUP SAVE / RESTORE
    // ══════════════════════════════════════════════════════════════

    [Serializable]
    private class SetupSnapshot
    {
        [Serializable]
        public class Entry { public string key; public string value; }
        public List<Entry> entries = new List<Entry>();
    }

    private void SaveSetup()
    {
        if (_data == null) return;
        var snap = new SetupSnapshot();

        void AddObj(string key, UnityEngine.Object obj)
        {
            string path = obj != null ? AssetDatabase.GetAssetPath(obj) : "";
            string guid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
            snap.entries.Add(new SetupSnapshot.Entry { key = key, value = guid });
        }
        void AddFloat(string key, float v) =>
            snap.entries.Add(new SetupSnapshot.Entry { key = key, value = v.ToString("R") });

        // River
        AddObj  ("splitPreset",                   _splitPreset);
        AddObj  ("pathPrefab",                    _data.pathPrefab);
        AddObj  ("branchWaterExtrudePrefab",      _data.branchWaterExtrudePrefab);
        AddObj  ("barrierPrefab",                 _data.barrierPrefab);
        AddObj  ("riverBlockPrefab",              _data.riverBlockPrefab);
        AddFloat("splineInstantiateSpacing",      _data.splineInstantiateSpacing);
        // Junctions
        AddObj  ("junctionScriptObject",          _data.junctionScriptObject);
        AddObj  ("junctionRightFacingPrefab",     _data.junctionRightFacingPrefab);
        AddObj  ("junctionLeftFacingPrefab",      _data.junctionLeftFacingPrefab);
        AddObj  ("junctionPrefab",                _data.junctionPrefab);
        AddFloat("junctionGapPadding",            _data.junctionGapPadding);
        // Arenas
        AddObj  ("arenaPrefab",                   _data.arenaPrefab);
        AddObj  ("arenaEntrancePrefab",           _data.arenaEntrancePrefab);
        // Obstacles & Shop
        AddObj  ("obstaclePrefab",                _data.obstaclePrefab);
        AddObj  ("shopPrefab",                    _data.shopPrefab);
        // UI Script Prefabs
        AddObj  ("cameraPrefab",                  _data.cameraPrefab);
        AddObj  ("soulsOnBoatDisplayScriptPrefab",_data.soulsOnBoatDisplayScriptPrefab);
        AddObj  ("arenaSoulsWindowPrefab",        _data.arenaSoulsWindowPrefab);
        AddObj  ("pauseManagerScriptPrefab",      _data.pauseManagerScriptPrefab);
        AddObj  ("portalConfirmUIScriptPrefab",  _data.portalConfirmUIScriptPrefab);
        AddObj  ("videoPlayerControllerPrefab",  _data.videoPlayerControllerPrefab);
        // UI Canvas Prefabs
        AddObj  ("canvasParentPrefab",            _data.canvasParentPrefab);
        AddObj  ("pauseMenuPrefab",               _data.pauseMenuPrefab);
        AddObj  ("boatHUDPrefab",                 _data.boatHUDPrefab);
        AddObj  ("soulsOnBoatDisplayPrefab",      _data.soulsOnBoatDisplayPrefab);
        AddObj  ("orbsCounterPrefab",             _data.orbsCounterPrefab);
        AddObj  ("shopTooltipPrefab",             _data.shopTooltipPrefab);
        // Core & World
        AddObj  ("dataControllerPrefab",          _data.dataControllerPrefab);
        AddObj  ("boatPrefab",                    _data.boatPrefab);
        AddObj  ("landscapeTilePrefab",           _data.landscapeTilePrefab);
        AddObj  ("openingSequencePrefab",         _data.openingSequencePrefab);
        AddObj  ("musicIntro",                    _data.musicIntro);
        AddObj  ("musicLoop",                     _data.musicLoop);

        if (System.IO.File.Exists(K_SetupTemplate))
        {
            if (!EditorUtility.DisplayDialog("Overwrite Setup Template?",
                    $"A setup template already exists.\nOverwrite it with the current data from '{_sourceData.name}'?",
                    "Overwrite", "Cancel"))
                return;
        }

        string json = JsonUtility.ToJson(snap, true);
        System.IO.File.WriteAllText(K_SetupTemplate, json);
        AssetDatabase.Refresh();
        _consoleStatusMsg = $"Setup template saved → {K_SetupTemplate}";
        Repaint();
    }

    private void RestoreSetup()
    {
        if (_sourceData == null) return;

        if (!System.IO.File.Exists(K_SetupTemplate))
        {
            Debug.LogWarning("[LevelSelectDesigner] No setup template found — open World1a and hit Save first.");
            return;
        }

        ApplySetupFromFile(K_SetupTemplate);
    }

    private void ApplySetupFromFile(string savePath)
    {
        if (!System.IO.File.Exists(savePath)) return;

        var snap = JsonUtility.FromJson<SetupSnapshot>(System.IO.File.ReadAllText(savePath));

        T LoadObj<T>(string key) where T : UnityEngine.Object
        {
            var e = snap.entries.Find(x => x.key == key);
            if (e == null || string.IsNullOrEmpty(e.value)) return null;
            string path = AssetDatabase.GUIDToAssetPath(e.value);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }
        float LoadFloat(string key, float fallback)
        {
            var e = snap.entries.Find(x => x.key == key);
            return (e != null && float.TryParse(e.value, out float v)) ? v : fallback;
        }

        Undo.RecordObject(_data, "Restore Setup");

        // River
        _splitPreset                          = LoadObj<SplineSplitterPreset>("splitPreset");
        _data.pathPrefab                      = LoadObj<GameObject>("pathPrefab");
        _data.branchWaterExtrudePrefab        = LoadObj<GameObject>("branchWaterExtrudePrefab");
        _data.barrierPrefab                   = LoadObj<GameObject>("barrierPrefab");
        _data.riverBlockPrefab                = LoadObj<GameObject>("riverBlockPrefab");
        _data.splineInstantiateSpacing        = LoadFloat("splineInstantiateSpacing", 0.15f);
        // Junctions
        _data.junctionScriptObject            = LoadObj<GameObject>("junctionScriptObject");
        _data.junctionRightFacingPrefab       = LoadObj<GameObject>("junctionRightFacingPrefab");
        _data.junctionLeftFacingPrefab        = LoadObj<GameObject>("junctionLeftFacingPrefab");
        _data.junctionPrefab                  = LoadObj<GameObject>("junctionPrefab");
        _data.junctionGapPadding              = LoadFloat("junctionGapPadding", 0f);
        // Arenas
        _data.arenaPrefab                     = LoadObj<GameObject>("arenaPrefab");
        _data.arenaEntrancePrefab             = LoadObj<GameObject>("arenaEntrancePrefab");
        // Obstacles & Shop
        _data.obstaclePrefab                  = LoadObj<GameObject>("obstaclePrefab");
        _data.shopPrefab                      = LoadObj<GameObject>("shopPrefab");
        // UI Script Prefabs
        _data.cameraPrefab                    = LoadObj<GameObject>("cameraPrefab");
        _data.soulsOnBoatDisplayScriptPrefab  = LoadObj<GameObject>("soulsOnBoatDisplayScriptPrefab");
        _data.arenaSoulsWindowPrefab          = LoadObj<GameObject>("arenaSoulsWindowPrefab");
        _data.pauseManagerScriptPrefab        = LoadObj<GameObject>("pauseManagerScriptPrefab");
        _data.portalConfirmUIScriptPrefab     = LoadObj<GameObject>("portalConfirmUIScriptPrefab");
        _data.videoPlayerControllerPrefab     = LoadObj<GameObject>("videoPlayerControllerPrefab");
        // UI Canvas Prefabs
        _data.canvasParentPrefab              = LoadObj<GameObject>("canvasParentPrefab");
        _data.pauseMenuPrefab                 = LoadObj<GameObject>("pauseMenuPrefab");
        _data.boatHUDPrefab                   = LoadObj<GameObject>("boatHUDPrefab");
        _data.soulsOnBoatDisplayPrefab        = LoadObj<GameObject>("soulsOnBoatDisplayPrefab");
        _data.orbsCounterPrefab               = LoadObj<GameObject>("orbsCounterPrefab");
        _data.shopTooltipPrefab               = LoadObj<GameObject>("shopTooltipPrefab");
        // Core & World
        _data.dataControllerPrefab            = LoadObj<GameObject>("dataControllerPrefab");
        _data.boatPrefab                      = LoadObj<GameObject>("boatPrefab");
        _data.landscapeTilePrefab             = LoadObj<GameObject>("landscapeTilePrefab");
        _data.openingSequencePrefab           = LoadObj<GameObject>("openingSequencePrefab");
        _data.musicIntro                      = LoadObj<AudioClip>("musicIntro");
        _data.musicLoop                       = LoadObj<AudioClip>("musicLoop");

        MarkDirty();
        _consoleStatusMsg = $"Setup restored from {savePath}";
        Repaint();
    }

    // Draws a consistently-styled indented sub-foldout inside the Setup section.
    private static void DrawSetupSubFoldout(ref bool state, string label, System.Action drawContent)
    {
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.28f, 0.28f, 0.28f, 1f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = prevBg;

        state = EditorGUILayout.Foldout(state, label, true, EditorStyles.foldoutHeader);
        if (state)
        {
            EditorGUILayout.Space(2);
            drawContent();
            EditorGUILayout.Space(2);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(1);
    }

    // ── Scene Deploy ──────────────────────────────────────────────
    private void DrawSceneDeploySection()
    {

        DrawDeployRow("Segment Registry",  _data.segmentRegistry  != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<RiverSegmentRegistry>();  if (f) _data.segmentRegistry  = f; },
            () => { if (DeployScriptOnly<RiverSegmentRegistry>("RiverSegmentRegistry", out var c, null)) _data.segmentRegistry = c; });

        DrawDeployRow("Data Controller",   _data.dataController   != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<LevelSelectDataController>(); if (f) _data.dataController   = f; },
            () => { if (DeployScriptOnly<LevelSelectDataController>("LevelSelectDataController", out var c, _data.dataControllerPrefab)) _data.dataController = c; });

        DrawDeployRow("Boat Path Manager", _data.boatPathManager  != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<SplinePathStitcher>(); if (f) _data.boatPathManager = f; },
            () => { if (DeployScriptOnly<SplinePathStitcher>("BoatPathManager", out var c, null)) _data.boatPathManager = c; });

        DrawDeployRow("River Manager",     _data.riverManager     != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<SplineRiverManager>();    if (f) _data.riverManager    = f; },
            () => { if (DeployScriptOnly<SplineRiverManager>("SplineRiverManager",    out var c, null)) _data.riverManager    = c; });

        bool riverExtrusionReady = GameObject.Find("RiverExtrusion") != null;
        DrawDeployRow("River Extrusion",   riverExtrusionReady,
            () => { },
            () => DeployRiverExtrusion());

        DrawDeployRow("Spline Manager",    _data.splineManager    != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<LevelSelectSplineManager>(); if (f) _data.splineManager = f; },
            () => { if (DeployScriptOnly<LevelSelectSplineManager>("LevelSelectSplineManager", out var c, null)) _data.splineManager = c; });

        DrawDeployRow("Boat Control",      _data.boatControl      != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<LevelSelectBoatControl>(); if (f) _data.boatControl = f; },
            () => DeployBoat());

        var playerBoatGo = GameObject.Find("PlayerBoat");
        bool playerBoatReady = playerBoatGo != null && playerBoatGo.transform.childCount > 0;
        DrawDeployRow("Player Boat GO", playerBoatReady,
            () => { },
            () => DeployPlayerBoat());

        DrawDeployRow("Main Camera", Camera.main != null,
            () => EnsureMainCamera(),
            () => EnsureMainCamera());

        DrawDeployRow("Camera Controller", _data.cameraController != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<LevelSelectCameraController>(); if (f) _data.cameraController = f; },
            () => DeployCameraController());

        DrawDeployRow("Music Controller", _data.musicController != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<LevelSelectMusicController>(); if (f) _data.musicController = f; },
            () => DeployMusicController());

        // UI Script Objects
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("UI Scripts", EditorStyles.miniBoldLabel);
        DrawDeployRow("SoulsOnBoatDisplay",       _data.soulsOnBoatDisplayManager != null,
            () => TryFind<SoulsOnBoatDisplayManager>(v => _data.soulsOnBoatDisplayManager = v),
            () => DeploySoulsOnBoatDisplay());

        DrawDeployRow("Arena Souls Window", GameObject.Find("ArenaSoulsWindow") != null,
            () => { },
            () => DeployArenaSoulsWindow());

        DrawDeployRow("Pause Manager", _data.pauseManager != null,
            () => TryFind<PauseManager>(v => _data.pauseManager = v),
            () => DeployPauseManager());

        DrawDeployRow("Video Controller", _data.videoPlayerController != null,
            () => TryFind<VideoPlayerController>(v => { _data.videoPlayerController = v; EditorUtility.SetDirty(_data); }),
            () => DeployVideoController());

        DrawDeployRow("Opening Sequence", _data.openingSequence != null,
            () => TryFind<LevelSelectOpeningSequence>(v => { _data.openingSequence = v; EditorUtility.SetDirty(_data); }),
            () => DeployOpeningSequence());

        EditorGUILayout.Space(4);


        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Auto-Find All", GUILayout.Height(22)))
            AutoFindAll();
        if (GUILayout.Button("Wire All", GUILayout.Height(22)))
            WireAllSceneObjects();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDeployRow(string label, bool present,
        System.Action onFind, System.Action onDeploy)
    {
        EditorGUILayout.BeginHorizontal();

        var dotStyle = new GUIStyle(EditorStyles.label) { fixedWidth = 14 };
        var prevCol = GUI.color;
        GUI.color = present ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.4f, 0.4f);
        EditorGUILayout.LabelField("●", dotStyle);
        GUI.color = prevCol;

        EditorGUILayout.LabelField(label, GUILayout.Width(104));

        if (GUILayout.Button("Find", EditorStyles.miniButton, GUILayout.Width(34)))
        {
            onFind();
            MarkDirty();
        }

        if (!present)
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
            if (GUILayout.Button("Deploy", EditorStyles.miniButton, GUILayout.Width(46)))
            {
                onDeploy();
                MarkDirty();
            }
            GUI.backgroundColor = prevBg;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DeploySoulsOnBoatDisplay()
    {
        if (!DeployScriptOnly<SoulsOnBoatDisplayManager>("SoulsDisplay_Script", out var manager, _data.soulsOnBoatDisplayScriptPrefab))
            return;
        _data.soulsOnBoatDisplayManager = manager;

        // Cache the slot manager reference for WireAll
        var barUI = GameObject.Find("SoulsDisplayBarUI");
        if (barUI != null)
        {
            _data.soulDisplaySlotManager = barUI.GetComponentInChildren<SoulDisplaySlotManager>();
            MarkDirty();

            if (_data.soulDisplaySlotManager != null && _data.soulsOnBoatDisplayManager != null)
            {
                var so       = new SerializedObject(_data.soulsOnBoatDisplayManager);
                so.Update();
                var slotProp = so.FindProperty("slotManager");
                var iconProp = so.FindProperty("iconParent");
                if (slotProp != null) slotProp.objectReferenceValue = _data.soulDisplaySlotManager;
                if (iconProp != null) iconProp.objectReferenceValue = _data.soulDisplaySlotManager.transform;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(_data.soulsOnBoatDisplayManager);
            }
        }
        else
        {
            Debug.LogWarning("[LevelSelectDesigner] SoulsDisplayBarUI not found — wiring will run after canvas is deployed.");
        }
    }

    private void DeployArenaSoulsWindow()
    {
        if (_data.arenaSoulsWindowPrefab == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] Arena Souls Window prefab not assigned.");
            return;
        }
        var existing = GameObject.Find("ArenaSoulsWindow");
        if (existing != null) return;
        var parent = FindOrCreateParent("LEVELSELECT_SCRIPTS");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(_data.arenaSoulsWindowPrefab, parent.transform);
        Undo.RegisterCreatedObjectUndo(go, "Deploy Arena Souls Window");
        go.name = "ArenaSoulsWindow";
    }

    private void DeployPauseManager()
    {
        if (_data.pauseManagerScriptPrefab == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] Pause Manager prefab not assigned.");
            return;
        }
        if (_data.pauseManager != null) return;
        if (!DeployScriptOnly<PauseManager>("PauseManager_Script", out var manager, _data.pauseManagerScriptPrefab))
            return;
        _data.pauseManager = manager;
        WirePauseManager();
        MarkDirty();
    }

    private void WirePauseManager()
    {
        if (_data.pauseManager == null) return;
        if (_data.pauseMenuUI == null) TryFind<PauseMenuUI>(v => _data.pauseMenuUI = v);
        if (_data.pauseMenuUI == null) return;
        var so   = new SerializedObject(_data.pauseManager);
        var prop = so.FindProperty("pauseMenu");
        if (prop != null) { prop.objectReferenceValue = _data.pauseMenuUI.gameObject; so.ApplyModifiedProperties(); EditorUtility.SetDirty(_data.pauseManager); }
    }

    private bool DeployScriptOnly<T>(string goName, out T result, GameObject prefabOverride) where T : Component
    {
        result = null;
        var parent = FindOrCreateParent("LEVELSELECT_SCRIPTS");

        GameObject go;
        if (prefabOverride != null)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefabOverride);
            Undo.RegisterCreatedObjectUndo(go, $"Deploy {goName}");
            go.transform.SetParent(parent.transform, false);
        }
        else
        {
            go = GameObject.Find(goName);
            if (go == null)
            {
                go = new GameObject(goName);
                Undo.RegisterCreatedObjectUndo(go, $"Deploy {goName}");
                go.transform.SetParent(parent.transform, false);
            }
        }
        var comp = go.GetComponent<T>();
        if (comp == null) comp = Undo.AddComponent<T>(go);
        result = comp;
        return comp != null;
    }

    private void DeployUIPrefab(GameObject prefab, string goName)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"[LevelSelectDesigner] No prefab set for {goName}.");
            return;
        }
        if (GameObject.Find(goName) != null) return;

        var canvasGo = GameObject.Find("CANVAS");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, $"Deploy {goName}");
        go.name = goName;
        if (canvasGo != null) go.transform.SetParent(canvasGo.transform, false);
        EditorUtility.SetDirty(go);
    }

    private void DeployRiverExtrusion()
    {
        if (_data.riverManager == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] Cannot deploy RiverExtrusion — SplineRiverManager not yet deployed.");
            return;
        }

        var parent = FindOrCreateParent("RiverExtrusion");

        // Find or create the main highway child
        var highwayT = parent.transform.Find("MainHighway");
        GameObject highwayGo;
        if (highwayT == null)
        {
            highwayGo = new GameObject("MainHighway");
            Undo.RegisterCreatedObjectUndo(highwayGo, "Deploy MainHighway");
            highwayGo.transform.SetParent(parent.transform, false);
        }
        else
        {
            highwayGo = highwayT.gameObject;
        }

        var container = highwayGo.GetComponent<SplineContainer>();
        if (container == null) container = Undo.AddComponent<SplineContainer>(highwayGo);

        var extrude = highwayGo.GetComponent<SplineExtrude>();
        if (extrude == null) extrude = Undo.AddComponent<SplineExtrude>(highwayGo);

        // Mirror SplineExtrude settings from BranchWaterExtrudePrefab
        if (_data.branchWaterExtrudePrefab != null)
        {
            var branchExtrude = _data.branchWaterExtrudePrefab.GetComponentInChildren<SplineExtrude>();
            if (branchExtrude != null)
            {
                EditorUtility.CopySerialized(branchExtrude, extrude);
                EditorUtility.SetDirty(extrude);
            }
        }

        // Wiring handled by WireAllSceneObjects
    }

    private void DeployBoat()
    {
        if (DeployScriptOnly<LevelSelectBoatControl>("LevelSelectBoatControl", out var c, null))
        {
            _data.boatControl = c;
            MarkDirty();
        }
    }

    private void DeployMusicController()
    {
        if (!DeployScriptOnly<LevelSelectMusicController>("LevelSelectMusicController", out var ctrl, null))
            return;

        var so = new SerializedObject(ctrl);
        so.FindProperty("data").objectReferenceValue = _data;
        so.ApplyModifiedProperties();

        _data.musicController = ctrl;
        MarkDirty();
    }

    private void DeployPlayerBoat()
    {
        var parent = FindOrCreateParent("PlayerBoat");

        // Only deploy the boat child if it doesn't already exist under the parent
        if (parent.transform.childCount > 0) return;

        GameObject boat;
        if (_data.boatPrefab != null)
        {
            boat = (GameObject)PrefabUtility.InstantiatePrefab(_data.boatPrefab);
            Undo.RegisterCreatedObjectUndo(boat, "Deploy Boat");
        }
        else
        {
            boat = new GameObject("LevelSelectBoat");
            Undo.RegisterCreatedObjectUndo(boat, "Deploy Boat");
        }
        boat.transform.SetParent(parent.transform, false);
    }

    private void DeployVideoController()
    {
        if (_data.videoPlayerController != null) return;

        var existing = UnityEngine.Object.FindObjectOfType<VideoPlayerController>();
        if (existing != null)
        {
            _data.videoPlayerController = existing;
            MarkDirty();
            return;
        }

        var prefab = _data.videoPlayerControllerPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] No VideoPlayerController prefab assigned — cannot deploy.");
            return;
        }

        var parent = FindOrCreateParent("LEVELSELECT_SCRIPTS");
        var go     = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
        Undo.RegisterCreatedObjectUndo(go, "Deploy Video Controller");
        go.name = "VideoPlayerController";
        _data.videoPlayerController = go.GetComponent<VideoPlayerController>();
        MarkDirty();
    }

    private void ApplyOpeningSequenceStartNode()
    {
        Undo.RecordObject(_data, "Apply Opening Sequence Start Node");

        var mainPath = _data.paths.Find(p => p.segmentType == LevelSelectDesignerData.SegmentType.MainRiver)
                    ?? _data.paths.FirstOrDefault();

        if (mainPath == null || mainPath.nodeIds.Count == 0)
        {
            // No main river at all — seed one with the start node
            SeedMainRiverStartNode(_data);
            MarkDirty();
            Debug.Log($"[LevelSelectDesigner] Opening sequence: created main river with start node at {_data.openingSequenceStartPos}");
            return;
        }

        var startNode = _data.nodes.Find(n => n.id == mainPath.nodeIds[0]);
        if (startNode == null) return;

        if (startNode.worldPosition != _data.openingSequenceStartPos)
        {
            startNode.worldPosition = _data.openingSequenceStartPos;
            MarkDirty();
            Debug.Log($"[LevelSelectDesigner] Opening sequence: moved main river node[0] to {_data.openingSequenceStartPos}");
        }
    }

    private void DeployOpeningSequence()
    {
        var existing = UnityEngine.Object.FindObjectOfType<LevelSelectOpeningSequence>();
        if (existing != null)
        {
            _data.openingSequence = existing;
            WireOpeningSequence();
            MarkDirty();
            return;
        }

        var prefab = _data.openingSequencePrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] No Opening Sequence prefab assigned — cannot deploy.");
            return;
        }

        var parent = FindOrCreateParent("LEVELSELECT_SCRIPTS");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
        Undo.RegisterCreatedObjectUndo(go, "Deploy Opening Sequence");
        go.name = "OPENING SEQUENCE CONTROLLER";
        _data.openingSequence = go.GetComponent<LevelSelectOpeningSequence>();
        WireOpeningSequence();
        MarkDirty();
    }

    private void WireOpeningSequence()
    {
        if (_data.openingSequence == null) return;

        var so = new SerializedObject(_data.openingSequence);
        so.Update();

        var bcProp    = so.FindProperty("boatControl");
        var camProp   = so.FindProperty("normalCamera");
        var skipProp  = so.FindProperty("skipIntro");

        if (bcProp  != null && _data.boatControl      != null && bcProp.objectReferenceValue  == null)
            bcProp.objectReferenceValue  = _data.boatControl;
        if (camProp != null && _data.cameraController != null && camProp.objectReferenceValue == null)
            camProp.objectReferenceValue = _data.cameraController;
        if (skipProp != null)
            skipProp.boolValue = !_data.useOpeningSequence;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(_data.openingSequence);
    }

    private void WireCameraPreviewTarget()
    {
        if (_data.cameraController == null) return;

        var so = new SerializedObject(_data.cameraController);
        so.Update();

        // Wire previewTarget to boat root transform if not already set
        var boatGo = GameObject.Find("LevelSelectBoat");
        if (boatGo != null)
        {
            var targetProp = so.FindProperty("previewTarget");
            if (targetProp != null && targetProp.objectReferenceValue == null)
                targetProp.objectReferenceValue = boatGo.transform;
        }

        // Always sync previewOrigin from the data so previews pivot at the boat's game-start position
        if (_data.useOpeningSequence && _data.openingSequenceStartPos != Vector3.zero)
        {
            var originProp = so.FindProperty("previewOrigin");
            if (originProp != null)
                originProp.vector3Value = _data.openingSequenceStartPos;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(_data.cameraController);
    }

    private void EnsureMainCamera()
    {
        if (Camera.main != null)
        {
            EnsureCinemachineBrain(Camera.main.gameObject);
            return;
        }

        // Tag any existing camera "MainCamera"
        var existing = UnityEngine.Object.FindObjectOfType<Camera>();
        if (existing != null)
        {
            Undo.RecordObject(existing.gameObject, "Tag Main Camera");
            existing.gameObject.tag = "MainCamera";
            Debug.Log($"[LevelSelectDesigner] Tagged '{existing.name}' as MainCamera.");
            EditorUtility.SetDirty(existing.gameObject);
            EnsureCinemachineBrain(existing.gameObject);
            return;
        }

        // Spawn MainCameraLevelSelect prefab
        var parent = FindOrCreateParent("CAMERA");
        var guids  = AssetDatabase.FindAssets("MainCameraLevelSelect t:Prefab");
        GameObject camGO;
        if (guids.Length == 0)
        {
            Debug.LogError("[LevelSelectDesigner] Prefab 'MainCameraLevelSelect' not found in project.");
            return;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
        camGO = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(camGO, "Create Main Camera");
        camGO.transform.SetParent(parent.transform, false);
        EnsureCinemachineBrain(camGO);
        Debug.Log("[LevelSelectDesigner] Spawned MainCameraLevelSelect prefab.");
    }

    private void EnsureCinemachineBrain(GameObject camGO)
    {
        if (camGO.GetComponent<Unity.Cinemachine.CinemachineBrain>() != null) return;
        Undo.AddComponent<Unity.Cinemachine.CinemachineBrain>(camGO);
        Debug.Log($"[LevelSelectDesigner] Added CinemachineBrain to '{camGO.name}'.");
    }

    private void DeployCameraController()
    {
        var parent = FindOrCreateParent("CAMERA");

        // Destroy stale GO if it's not a prefab instance of the assigned prefab
        var existing = GameObject.Find("LevelSelectCamera");
        if (existing != null && _data.cameraPrefab != null &&
            PrefabUtility.GetCorrespondingObjectFromSource(existing) != _data.cameraPrefab)
        {
            Undo.DestroyObjectImmediate(existing);
            existing = null;
        }

        var go = existing;
        if (go == null)
        {
            if (_data.cameraPrefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(_data.cameraPrefab);
                Undo.RegisterCreatedObjectUndo(go, "Deploy Camera");
                go.name = "LevelSelectCamera";
            }
            else
            {
                go = new GameObject("LevelSelectCamera");
                Undo.RegisterCreatedObjectUndo(go, "Deploy Camera Controller");
            }
        }

        if (go.transform.parent != parent.transform)
            go.transform.SetParent(parent.transform, false);

        // Wire LevelSelectCameraController
        var comp = go.GetComponent<LevelSelectCameraController>();
        if (comp == null) comp = Undo.AddComponent<LevelSelectCameraController>(go);
        _data.cameraController = comp;

        // Pre-wire camera preview fields so editor Preview Start/Transition work immediately.
        _data.cameraController = comp;
        WireCameraPreviewTarget();
    }

    private void DeployCinemachine()
    {
        var scriptsParent = FindOrCreateParent("LEVELSELECT_SCRIPTS");

        // 1. Add CinemachineBrain to the main camera
        var mainCam = UnityEngine.Object.FindObjectOfType<Camera>();
        if (mainCam != null)
        {
            if (mainCam.GetComponent<Unity.Cinemachine.CinemachineBrain>() == null)
                Undo.AddComponent<Unity.Cinemachine.CinemachineBrain>(mainCam.gameObject);
        }
        else
            Debug.LogWarning("[LevelSelectDesigner] No Camera found in scene — add a Main Camera first.");

        // 2. Create CinemachineCamera GO under LEVELSELECT_SCRIPTS
        var vcamGo = GameObject.Find("LevelSelectVCam");
        if (vcamGo == null)
        {
            vcamGo = new GameObject("LevelSelectVCam");
            Undo.RegisterCreatedObjectUndo(vcamGo, "Deploy CinemachineCamera");
            vcamGo.transform.SetParent(scriptsParent.transform, false);
        }

        var vcam = vcamGo.GetComponent<Unity.Cinemachine.CinemachineCamera>();
        if (vcam == null) vcam = Undo.AddComponent<Unity.Cinemachine.CinemachineCamera>(vcamGo);

        // Wiring handled by WireAllSceneObjects

        Debug.Log("[LevelSelectDesigner] Cinemachine deployed.");
    }

    private void DeployAllSceneObjects()
    {
        AutoFindAll();
        EnsureMainCamera();
        if (_data.videoPlayerController == null) DeployVideoController();
        if (_data.segmentRegistry  == null) { if (DeployScriptOnly<RiverSegmentRegistry>("RiverSegmentRegistry",         out var c, null))              _data.segmentRegistry  = c; }
        if (_data.dataController   == null) { if (DeployScriptOnly<LevelSelectDataController>("LevelSelectDataController", out var c, _data.dataControllerPrefab)) _data.dataController = c; }
        if (_data.boatPathManager  == null) { if (DeployScriptOnly<SplinePathStitcher>("BoatPathManager",                out var c, null)) _data.boatPathManager = c; }
        if (_data.riverManager     == null) { if (DeployScriptOnly<SplineRiverManager>("SplineRiverManager",             out var c, null)) _data.riverManager    = c; }
        DeployRiverExtrusion();
        if (_data.splineManager    == null) { if (DeployScriptOnly<LevelSelectSplineManager>("LevelSelectSplineManager", out var c, null)) _data.splineManager   = c; }
        if (_data.boatControl      == null) DeployBoat();
        var _playerBoatGo = GameObject.Find("PlayerBoat");
        if (_playerBoatGo == null || _playerBoatGo.transform.childCount == 0) DeployPlayerBoat();
        DeployCameraController();
        if (_data.musicController == null) DeployMusicController();
        if (GameObject.Find("ArenaSoulsWindow") == null) DeployArenaSoulsWindow();
        if (_data.pauseManager == null) DeployPauseManager();
        // UI Canvas prefabs — only deploy if CANVAS parent prefab is assigned
        if (_data.canvasParentPrefab != null)
        {
            if (GameObject.Find("CANVAS") == null)
            {
                var canvas = (GameObject)PrefabUtility.InstantiatePrefab(_data.canvasParentPrefab);
                Undo.RegisterCreatedObjectUndo(canvas, "Deploy CANVAS");
                canvas.name = "CANVAS";
            }
            if (GameObject.Find("PauseMenuUI")       == null) DeployUIPrefab(_data.pauseMenuPrefab,          "PauseMenuUI");
            if (GameObject.Find("BoatHUDPrompts")    == null) DeployUIPrefab(_data.boatHUDPrefab,            "BoatHUDPrompts");
            if (GameObject.Find("SoulsDisplayBarUI") == null) DeployUIPrefab(_data.soulsOnBoatDisplayPrefab, "SoulsDisplayBarUI");
            if (GameObject.Find("OrbsCounterUI")     == null) DeployUIPrefab(_data.orbsCounterPrefab,        "OrbsCounterUI");
            if (GameObject.Find("ShopTooltipHUD")    == null) DeployUIPrefab(_data.shopTooltipPrefab,        "ShopTooltipHUD");
            }

            // Deploy after SoulsDisplayBarUI exists so wiring can find it
            if (_data.soulsOnBoatDisplayManager == null) DeploySoulsOnBoatDisplay();
            WirePauseMenuPanels();

        if (_data.openingSequence == null)
            DeployOpeningSequence();

        EditorUtility.SetDirty(_data);
    }

    private void AutoFindAll()
    {
        Undo.RecordObject(_data, "Auto-Find Scene Objects");
        TryFind<RiverSegmentRegistry>(v   => _data.segmentRegistry   = v);
        TryFind<LevelSelectDataController>(v => _data.dataController = v);
        TryFind<SplinePathStitcher>(v     => _data.boatPathManager   = v);
        TryFind<SplineRiverManager>(v     => _data.riverManager      = v);
        TryFind<LevelSelectSplineManager>(v => _data.splineManager   = v);
        TryFind<LevelSelectBoatControl>(v => _data.boatControl       = v);
        TryFind<LevelSelectCameraController>(v => _data.cameraController = v);
        TryFind<LandscapeTool>(v             => _data.landscapeTool          = v);
        TryFind<SoulDisplaySlotManager>(v       => _data.soulDisplaySlotManager    = v);
        TryFind<PauseMenuUI>(v                  => _data.pauseMenuUI               = v);
        TryFind<PauseManager>(v                 => _data.pauseManager              = v);
        TryFind<SoulsOnBoatDisplayManager>(v    => _data.soulsOnBoatDisplayManager = v);
        TryFind<VideoPlayerController>(v        => _data.videoPlayerController     = v);
        TryFind<LevelSelectOpeningSequence>(v  => _data.openingSequence           = v);
        MarkDirty();
    }

    private void WirePauseMenuPanels()
    {
        var canvasGo = GameObject.Find("PauseMenuUI");
        if (canvasGo == null) return;


        // Wire panel children into PauseMenuUI script on the canvas
        var uiScript = canvasGo.GetComponent<PauseMenuUI>()
                    ?? canvasGo.GetComponentInChildren<PauseMenuUI>();
        if (uiScript != null)
        {
            uiScript.mainPanel     = FindChildGO(canvasGo, "MainPanel",     "Main Panel");
            uiScript.aboutPanel    = FindChildGO(canvasGo, "AboutPanel",    "About Panel");
            uiScript.controlsPanel = FindChildGO(canvasGo, "ControlsPanel", "Controls Panel");
            uiScript.settingsPanel = FindChildGO(canvasGo, "SettingsPanel", "Settings Panel");
            EditorUtility.SetDirty(uiScript);
            _data.pauseMenuUI = uiScript;
        }

        Debug.Log("[LevelSelectDesigner] PauseMenuUI panels wired.");
    }

    private static GameObject FindChildGO(GameObject root, params string[] names)
    {
        foreach (var name in names)
        {
            var t = root.transform.Find(name);
            if (t != null) return t.gameObject;
        }
        // Deep search if not found as direct child
        foreach (var name in names)
        {
            var found = FindDeep(root.transform, name);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void TryFind<T>(System.Action<T> assign) where T : Component
    {
        var found = UnityEngine.Object.FindObjectOfType<T>();
        if (found != null) assign(found);
    }

    private void WireAllSceneObjects()
    {
        // ── River extrusion → SplineRiverManager ──────────────────────────────
        var riverExtrusionGO = GameObject.Find("RiverExtrusion");
        if (riverExtrusionGO != null && _data.riverManager != null)
        {
            var mainHighway = riverExtrusionGO.transform.Find("MainHighway");
            if (mainHighway != null)
            {
                var container = mainHighway.GetComponent<SplineContainer>();
                var extrude   = mainHighway.GetComponent<SplineExtrude>();
                _data.riverManager.SetupContainers(container, extrude, _data.barrierPrefab);
                var so = new SerializedObject(_data.riverManager);
                so.Update();
                var mc = so.FindProperty("_mainContainer");
                var me = so.FindProperty("_mainExtrude");
                var mb = so.FindProperty("_barrierPrefab");
                if (mc != null) mc.objectReferenceValue = container;
                if (me != null) me.objectReferenceValue = extrude;
                if (mb != null && _data.barrierPrefab != null) mb.objectReferenceValue = _data.barrierPrefab;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(_data.riverManager);
            }
        }

        // ── Branch + barrier prefabs → SplineRiverManager ────────────────────
        if (_data.riverManager != null && (_data.branchWaterExtrudePrefab != null || _data.barrierPrefab != null))
        {
            var so = new SerializedObject(_data.riverManager);
            so.Update();
            var bp = so.FindProperty("_branchPrefab");
            var br = so.FindProperty("_barrierPrefab");
            bool dirty = false;
            if (bp != null && bp.objectReferenceValue == null && _data.branchWaterExtrudePrefab != null) { bp.objectReferenceValue = _data.branchWaterExtrudePrefab; dirty = true; }
            if (br != null && br.objectReferenceValue == null && _data.barrierPrefab            != null) { br.objectReferenceValue = _data.barrierPrefab;            dirty = true; }
            if (dirty) { so.ApplyModifiedProperties(); EditorUtility.SetDirty(_data.riverManager); }
        }

        // ── Path prefab → SplinePathStitcher ──────────────────────────────────
        if (_data.boatPathManager != null && _data.pathPrefab != null)
        {
            var so = new SerializedObject(_data.boatPathManager);
            var pp = so.FindProperty("_pathPrefab");
            if (pp != null && pp.objectReferenceValue == null) { pp.objectReferenceValue = _data.pathPrefab; so.ApplyModifiedProperties(); EditorUtility.SetDirty(_data.boatPathManager); }
        }

        // ── CinemachineCamera → LevelSelectCameraController + TrackingTarget ───
        if (_data.cameraController != null)
        {
            var camGO = GameObject.Find("LevelSelectCamera");
            var vcam  = camGO?.GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>();
            if (vcam != null)
            {
                // Wire cam field on the controller
                var so   = new SerializedObject(_data.cameraController);
                var prop = so.FindProperty("cam");
                if (prop != null) { prop.objectReferenceValue = vcam; so.ApplyModifiedProperties(); EditorUtility.SetDirty(_data.cameraController); }

                // Wire TrackingTarget on the CinemachineCamera to the boat
                var boatGo = GameObject.Find("LevelSelectBoat");
                if (boatGo != null)
                {
                    var vcamSo       = new SerializedObject(vcam);
                    var trackingProp = vcamSo.FindProperty("Target.TrackingTarget");
                    if (trackingProp != null)
                    {
                        trackingProp.objectReferenceValue = boatGo.transform;
                        vcamSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(vcam);
                    }
                    else
                        Debug.LogWarning("[LevelSelectDesigner] Could not find 'Target.TrackingTarget' on CinemachineCamera — property path may differ in this Cinemachine version.");
                }
            }
        }

        // ── SoulsOnBoatDisplayManager → slotManager + iconParent ─────────────
        if (_data.soulsOnBoatDisplayManager != null)
        {
            // Re-find slot manager in case canvas was deployed after the script
            if (_data.soulDisplaySlotManager == null)
            {
                var barUI = GameObject.Find("SoulsDisplayBarUI");
                if (barUI != null) { _data.soulDisplaySlotManager = barUI.GetComponentInChildren<SoulDisplaySlotManager>(); EditorUtility.SetDirty(_data); }
            }

            if (_data.soulDisplaySlotManager != null)
            {
                var iconParent = _data.soulDisplaySlotManager.transform;
                var so       = new SerializedObject(_data.soulsOnBoatDisplayManager);
                so.Update();
                var slotProp = so.FindProperty("slotManager");
                var iconProp = so.FindProperty("iconParent");
                if (slotProp != null) slotProp.objectReferenceValue = _data.soulDisplaySlotManager;
                if (iconProp != null) iconProp.objectReferenceValue = iconParent;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(_data.soulsOnBoatDisplayManager);
            }
        }

        // ── Auto-fill defaultSegmentID on boat control from first main river path in data
        if (_data.boatControl != null && _data.paths.Count > 0)
        {
            var mainPath = _data.paths.Find(p => p.segmentType == LevelSelectDesignerData.SegmentType.MainRiver)
                        ?? _data.paths[0];
            if (!string.IsNullOrEmpty(mainPath.segmentId))
            {
                var so = new SerializedObject(_data.boatControl);
                var prop = so.FindProperty("defaultSegmentID");
                if (prop != null && prop.stringValue != mainPath.segmentId)
                {
                    prop.stringValue = mainPath.segmentId;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_data.boatControl);
                }
            }
        }

        if (_data.dataController != null && _data.boatControl != null)
        {
            var so = new SerializedObject(_data.dataController);
            var prop = so.FindProperty("boatControl");
            if (prop != null) { prop.objectReferenceValue = _data.boatControl; so.ApplyModifiedProperties(); }
            EditorUtility.SetDirty(_data.dataController);
        }

        if (_data.splineManager != null && _data.riverManager != null)
        {
            var so = new SerializedObject(_data.splineManager);
            var prop = so.FindProperty("_riverManager");
            if (prop != null) { prop.objectReferenceValue = _data.riverManager; so.ApplyModifiedProperties(); }
            EditorUtility.SetDirty(_data.splineManager);
        }

        WirePauseMenuPanels();
        WirePauseManager();
        WireOpeningSequence();
        WireCameraPreviewTarget();

        Debug.Log("[LevelSelectDesigner] Wire All complete.");
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
        _data.shopHeadOffset     = EditorGUILayout.FloatField("Shop Head Offset", _data.shopHeadOffset);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawActionButtons()
    {
        EditorGUILayout.Space(6);

        // ── Opening Sequence ──────────────────────────────────────
        EditorGUI.BeginChangeCheck();
        bool prevUseOpening = _data.useOpeningSequence;
        _data.useOpeningSequence = EditorGUILayout.ToggleLeft("Opening Sequence", _data.useOpeningSequence);
        // Pre-fill the start position with the canonical value when first enabled
        if (_data.useOpeningSequence && !prevUseOpening && _data.openingSequenceStartPos == Vector3.zero)
            _data.openingSequenceStartPos = OpeningSequenceDefaultStart;
        if (_data.useOpeningSequence)
        {
            EditorGUI.indentLevel++;
            _data.openingSequenceStartPos = EditorGUILayout.Vector3Field("River Start Pos", _data.openingSequenceStartPos);
            EditorGUI.indentLevel--;
        }
        if (EditorGUI.EndChangeCheck()) MarkDirty();

        EditorGUILayout.Space(4);

        bool sceneOk  = IsValidLevelSelectScene();
        bool dataOk   = !_consoleHasErrors;
        bool canGenerate = sceneOk && dataOk;

        if (!sceneOk)
        {
            var s = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = new Color(1f, 0.6f, 0.3f) } };
            EditorGUILayout.LabelField("Scene must be a LevelSelectWorld scene to generate.", s);
        }
        else if (!dataOk)
        {
            var s = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = new Color(1f, 0.5f, 0.4f) } };
            EditorGUILayout.LabelField("Fix console errors before generating.", s);
        }

        var prevColor = GUI.backgroundColor;
        GUI.enabled = canGenerate;
        GUI.backgroundColor = canGenerate ? new Color(0.4f, 1f, 0.5f) : Color.gray;
        if (GUILayout.Button("GENERATE", GUILayout.Height(30)))
        {
            PruneLooseNodes();
            ClearGeneratedObjects();
            EditorApplication.delayCall += () => { if (_data != null) Generate(); };
        }
        GUI.backgroundColor = prevColor;
        GUI.enabled = true;

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
                MarkDirty();
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

        // Event handling uses absolute window coords — must run before BeginClip shifts the system
        if (_data != null)
            HandleCanvasEvents();

        // Clip all drawing to the canvas rect so nothing bleeds over the side panels.
        // Temporarily remap _canvasRect to canvas-local coords so WorldToCanvas
        // returns positions relative to the clip origin.
        var absoluteCanvasRect = _canvasRect;
        _canvasRect = new Rect(Vector2.zero, _canvasRect.size);
        GUI.BeginClip(absoluteCanvasRect);

        DrawCanvasGrid();

        if (_data != null)
        {
            Handles.BeginGUI();
            DrawLandscapeTilesOnCanvas();
            DrawPaths();
            DrawArenaOrbitRings();
            DrawNodes();
            DrawObstacles();
            DrawArenaEntrances();
            DrawInProgressPath();
            Handles.EndGUI();
        }

        GUI.EndClip();
        _canvasRect = absoluteCanvasRect;

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

            MarkDirty();
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
                MarkDirty();
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
            path.segmentType = InferBranchType(path.pathId, startNodeId);
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
        MarkDirty();
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
                _selectedShopNodeId  = node?.type == LevelSelectDesignerData.NodeType.ShopEnd  ? nodeId : null;
                _selectedEntranceIdx = -1;
                Repaint();
                e.Use();
                return;
            }

            string obsId = FindObstacleAtCanvas(e.mousePosition);
            if (obsId != null)
            {
                _selectedObstacleId    = obsId;
                _selectedNodeId        = null;
                _selectedShopNodeId    = null;
                _isDraggingObstacle    = true;
                _draggingObstacleId    = obsId;
                Repaint();
                e.Use();
                return;
            }

            string pathId = FindPathAtCanvas(e.mousePosition);
            // If the clicked path has a shop node at either end, auto-select it
            _selectedShopNodeId = null;
            if (pathId != null)
            {
                var clickedPath = _data.paths.Find(p => p.pathId == pathId);
                if (clickedPath != null)
                {
                    foreach (var nid in clickedPath.nodeIds)
                    {
                        var n = _data.nodes.Find(x => x.id == nid);
                        if (n?.type == LevelSelectDesignerData.NodeType.ShopEnd &&
                            _data.shops.Exists(s => s.nodeId == nid))
                        {
                            _selectedShopNodeId = nid;
                            break;
                        }
                    }
                }
            }
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
                var np = CanvasToWorldPos(e.mousePosition - _dragOffset);

                // Secondary entrance nodes constrained to inner ring — position IS the direction
                var ownerArena = FindArenaForSecondaryNode(node.id);
                if (ownerArena != null)
                {
                    float   clampR      = GetArenaRadius(ownerArena);
                    Vector3 trueCenter  = GetTrueArenaCenter(ownerArena);
                    Vector3 dir = np - trueCenter; dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                    np   = trueCenter + dir.normalized * clampR;
                    np.y = trueCenter.y;
                }

                node.worldPosition = new Vector3(np.x, node.worldPosition.y, np.z);
                MarkDirty();
                Repaint();
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp && _isDraggingNode)
        {
            _isDraggingNode = false;
            _draggingNodeId = null;
        }

        if (e.type == EventType.MouseDrag && _isDraggingObstacle)
        {
            var obs = _data.obstacles.Find(o => o.obstacleId == _draggingObstacleId);
            if (obs != null)
            {
                var path = _data.paths.Find(p => p.pathId == obs.pathId);
                if (path != null)
                {
                    Undo.RecordObject(_data, "Move Obstacle");
                    obs.pathT = Mathf.Clamp01(FindTOnPath(path, e.mousePosition));
                    EditorUtility.SetDirty(_data);
                    Repaint();
                }
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp && _isDraggingObstacle)
        {
            _isDraggingObstacle = false;
            _draggingObstacleId = null;
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
        {
            if (_selectedNodeId != null)
            {
                Undo.RecordObject(_data, "Delete Node");
                DeleteNode(_selectedNodeId);
                _selectedNodeId = null;
                MarkDirty();
                Repaint();
                e.Use();
            }
            else if (_selectedPathId != null)
            {
                Undo.RecordObject(_data, "Delete Path");
                DeletePath(_selectedPathId);
                _selectedPathId = null;
                MarkDirty();
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
                MarkDirty();
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

        MarkDirty();
        Repaint();
        e.Use();
    }

    private void MoveArenaToEnd(LevelSelectDesignerData.DesignerPath path, bool atEnd)
    {
        if (path.nodeIds.Count == 0) return;

        // Remove arena from old endpoint
        string oldNodeId = path.arenaIsAtEnd
            ? path.nodeIds[path.nodeIds.Count - 1]
            : path.nodeIds[0];
        var oldNode = _data.nodes.Find(n => n.id == oldNodeId);
        if (oldNode != null) oldNode.type = LevelSelectDesignerData.NodeType.Waypoint;

        // Move the DesignerArena entry to the new endpoint
        string newNodeId = atEnd
            ? path.nodeIds[path.nodeIds.Count - 1]
            : path.nodeIds[0];
        var existing = _data.arenas.Find(a => a.nodeId == oldNodeId);
        if (existing != null) existing.nodeId = newNodeId;
        else _data.arenas.Add(new LevelSelectDesignerData.DesignerArena { nodeId = newNodeId });

        var newNode = _data.nodes.Find(n => n.id == newNodeId);
        if (newNode != null) newNode.type = LevelSelectDesignerData.NodeType.ArenaEnd;

        path.arenaIsAtEnd    = atEnd;
        _selectedArenaNodeId = newNodeId;
        MarkDirty();
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

        MarkDirty();
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
        MarkDirty();
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
            if (_selectedShopNodeId == nodeId)
            {
                // Second click on selected shop — remove it
                node.type = LevelSelectDesignerData.NodeType.Waypoint;
                _data.shops.RemoveAll(s => s.nodeId == nodeId);
                _selectedShopNodeId = null;
            }
            else
            {
                // Click a different shop — select it
                _selectedShopNodeId = nodeId;
            }
        }
        else
        {
            node.type = LevelSelectDesignerData.NodeType.ShopEnd;
            if (!_data.shops.Exists(s => s.nodeId == nodeId))
                _data.shops.Add(new LevelSelectDesignerData.DesignerShop { nodeId = nodeId });
            _selectedShopNodeId = nodeId;
        }

        MarkDirty();
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

                // Direction arrow at midpoint — tip at mid+dir, fins back toward mid
                Vector2 mid  = (a + b) * 0.5f;
                Vector2 dir  = (b - a).normalized * 7f;
                Vector2 perp = new Vector2(-dir.y, dir.x) * 0.4f;
                Handles.DrawAAPolyLine(width, (Vector3)(mid + dir), (Vector3)(mid - perp));
                Handles.DrawAAPolyLine(width, (Vector3)(mid + dir), (Vector3)(mid + perp));
            }
        }
    }

    // Returns the true world-space center of the spawned arena (node + arenaHeadOffset along arrival dir)
    private Vector3 GetTrueArenaCenter(LevelSelectDesignerData.DesignerArena arena)
    {
        var primary = _data.nodes.Find(n => n.id == arena.nodeId);
        if (primary == null) return Vector3.zero;
        float y   = GetArenaArrivalYAngle(arena);
        float rad = y * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        return primary.worldPosition + dir * _data.arenaHeadOffset;
    }

    private float GetArenaArrivalYAngle(LevelSelectDesignerData.DesignerArena arena)
    {
        var branch = _data.paths.FirstOrDefault(p =>
            p.leadsToArena &&
            p.nodeIds.Count >= 2 &&
            p.nodeIds[p.nodeIds.Count - 1] == arena.nodeId);
        if (branch == null) return 0f;

        Vector3 last = WorldPosOfNode(branch.nodeIds[branch.nodeIds.Count - 1]);
        Vector3 prev = WorldPosOfNode(branch.nodeIds[branch.nodeIds.Count - 2]);
        Vector3 dir  = last - prev; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return 0f;
        return Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles.y;
    }

    private void DrawArenaOrbitRings()
    {
        if (Event.current.type != EventType.Repaint) return;
        foreach (var arena in _data.arenas)
        {
            var primary = _data.nodes.Find(n => n.id == arena.nodeId);
            if (primary == null) continue;

            float arrivalY   = GetArenaArrivalYAngle(arena);
            float arrivalRad = arrivalY * Mathf.Deg2Rad;
            Vector3 offsetDir    = new Vector3(Mathf.Sin(arrivalRad), 0f, Mathf.Cos(arrivalRad));
            Vector3 trueCenter3D = primary.worldPosition + offsetDir * _data.arenaHeadOffset;

            float   arenaR = GetArenaRadius(arena);
            Vector2 center = WorldToCanvas(trueCenter3D);
            Handles.color = new Color(1f, 0.7f, 0.2f, 0.35f);
            Handles.DrawWireDisc(center, Vector3.forward, arenaR * _zoom);
            Handles.color = new Color(1f, 0.7f, 0.2f, 0.12f);
            Handles.DrawWireDisc(center, Vector3.forward, arenaR * _zoom * 1.15f);

            // Line from arena centre to each secondary entrance orbit node
            foreach (var sec in arena.secondaryEntrances)
            {
                var sn = _data.nodes.Find(n => n.id == sec.nodeId);
                if (sn == null) continue;
                Vector2 orbitCanvas = WorldToCanvas(sn.worldPosition);
                Handles.color = new Color(0.2f, 0.9f, 1f, 0.6f);
                Handles.DrawAAPolyLine(1.5f, (Vector3)center, (Vector3)orbitCanvas);
            }

            // Entrance 1 direction arrow — only when this arena is selected
            if (arena.nodeId == _selectedArenaNodeId)
            {
                float a1rad    = arrivalY * Mathf.Deg2Rad;
                var   a1inward = new Vector2(-Mathf.Sin(a1rad), Mathf.Cos(a1rad));
                var   a1appr   = -a1inward;
                Vector2 outer  = center + a1appr * arenaR * _zoom * 1.15f;
                Vector2 inner  = center + a1appr * arenaR * _zoom;
                Vector2 back   = inner - a1inward * 6f;
                Vector2 perp   = new Vector2(-a1inward.y, a1inward.x) * 4f;
                Handles.color  = new Color(1f, 0.5f, 0.1f, 0.9f);
                Handles.DrawAAPolyLine(2f, (Vector3)outer, (Vector3)inner);
                Handles.DrawAAPolyLine(2f, (Vector3)inner, (Vector3)(back + perp));
                Handles.DrawAAPolyLine(2f, (Vector3)inner, (Vector3)(back - perp));
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
                    var secEntrance = FindSecondaryEntrance(node.id);
                    if (secEntrance != null)
                    {
                        // Secondary entrance node — smaller, cyan-tinted
                        Handles.color = new Color(0.2f, 0.9f, 1f, 0.9f);
                        Handles.DrawSolidDisc(pos, Vector3.forward, r + 1f);
                        if (selected) { Handles.color = Color.white; Handles.DrawWireDisc(pos, Vector3.forward, r + 5f); }
                        Handles.color = new Color(0.2f, 0.9f, 1f);
                        Handles.Label(new Vector3(pos.x + r + 4f, pos.y - 6f, 0),
                            $"Entrance {secEntrance.entranceIndex}", EditorStyles.miniLabel);
                    }
                    else
                    {
                        // Primary arena node
                        Handles.color = new Color(1f, 0.5f, 0.1f);
                        Handles.DrawSolidDisc(pos, Vector3.forward, r + 3f);
                        if (selected) { Handles.color = Color.white; Handles.DrawWireDisc(pos, Vector3.forward, r + 6f); }
                        var arena = _data.arenas.Find(a => a.nodeId == node.id);
                        Handles.color = Color.white;
                        Handles.Label(new Vector3(pos.x + r + 4f, pos.y - 6f, 0),
                            arena?.gridData != null ? arena.gridData.displayName : "ARENA", EditorStyles.miniLabel);
                    }
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

                float   aR       = GetArenaRadius(arena);
                Vector2 inward   = (WorldToCanvas(lastNode.worldPosition) - WorldToCanvas(prevNode.worldPosition)).normalized;
                Vector2 approach = -inward;
                Vector2 outerPt  = center + approach * aR * _zoom * 1.15f;
                Vector2 innerPt  = center + approach * aR * _zoom;

                Color col   = isArenaSelected ? new Color(1f, 0.6f, 0.1f) : new Color(0.9f, 0.7f, 0.3f, 0.7f);
                float width = 1.5f;

                Handles.color = col;
                Handles.DrawAAPolyLine(width, (Vector3)outerPt, (Vector3)innerPt);

                Vector2 perp = new Vector2(-inward.y, inward.x) * 4f;
                Vector2 back = innerPt - inward * 6f;
                Handles.DrawAAPolyLine(width, (Vector3)innerPt, (Vector3)(back + perp));
                Handles.DrawAAPolyLine(width, (Vector3)innerPt, (Vector3)(back - perp));
                Handles.DrawSolidDisc(innerPt, Vector3.forward, 4f);
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
    // ══════════════════════════════════════════════════════════════
    // SCENE GUARD
    // ══════════════════════════════════════════════════════════════
    private static bool IsValidLevelSelectScene()
    {
        // Scene name must start with "LevelSelectWorld" — add new worlds here as needed
        return EditorSceneManager.GetActiveScene().name.StartsWith("LevelSelectWorld");
    }

    // ══════════════════════════════════════════════════════════════
    // VALIDATION / DEBUG CONSOLE
    // ══════════════════════════════════════════════════════════════
    private void RunValidation()
    {
        _consoleEntries.Clear();
        _consoleHasErrors = false;
        if (_data == null) return;

        // 1. Duplicate segment IDs
        var dupGroups = _data.paths
            .Where(p => !string.IsNullOrEmpty(p.segmentId))
            .GroupBy(p => p.segmentId)
            .Where(g => g.Count() > 1);
        foreach (var g in dupGroups)
        {
            _consoleEntries.Add((true,
                $"Duplicate ID \"{g.Key}\" — {string.Join(", ", g.Select(p => p.segmentId))}",
                g.First().pathId, null));
            _consoleHasErrors = true;
        }

        // 2. Paths with fewer than 2 nodes
        foreach (var p in _data.paths)
        {
            if (p.nodeIds.Count < 2)
            {
                _consoleEntries.Add((true,
                    $"Path \"{(string.IsNullOrEmpty(p.segmentId) ? "(unnamed)" : p.segmentId)}\" has {p.nodeIds.Count} node(s) — needs 2+",
                    p.pathId, null));
                _consoleHasErrors = true;
            }
        }

        // 3. Paths with no segment ID (warning only)
        foreach (var p in _data.paths)
        {
            if (string.IsNullOrEmpty(p.segmentId))
                _consoleEntries.Add((false, "A path has no Segment ID set", p.pathId, null));
        }

        // 4. Junctions where all connected paths share the same branch depth
        //    → auto-assign will use fragile distance fallback instead of depth logic
        foreach (var junc in _data.junctions)
        {
            var connected = _data.paths.Where(p => p.nodeIds.Contains(junc.nodeId)).ToList();
            if (connected.Count < 2) continue;
            var depths = connected.Select(p => (int)p.segmentType).Distinct().ToList();
            if (depths.Count == 1)
            {
                var depthName = connected[0].segmentType.ToString();
                _consoleEntries.Add((false,
                    $"Junction at ({GetJunctionLabel(junc.nodeId)}): all paths are {depthName} — depth fallback active",
                    null, junc.nodeId));
            }
        }

        // 5. Multiple MainRiver paths
        var mainRiverPaths = _data.paths
            .Where(p => p.segmentType == LevelSelectDesignerData.SegmentType.MainRiver)
            .ToList();
        if (mainRiverPaths.Count > 1)
        {
            foreach (var p in mainRiverPaths)
            {
                _consoleEntries.Add((true,
                    $"Multiple MainRiver paths — \"{(string.IsNullOrEmpty(p.segmentId) ? "(unnamed)" : p.segmentId)}\" is also MainRiver",
                    p.pathId, null));
            }
            _consoleHasErrors = true;
        }

        // 6. No MainRiver path at all
        if (_data.paths.Count > 0 && mainRiverPaths.Count == 0)
        {
            _consoleEntries.Add((false,
                "No path is typed MainRiver — one path should be the main trunk",
                null, null));
        }

        // 7. Path with both isLeftPath and isRightPath set (contradictory)
        foreach (var p in _data.paths)
        {
            if (p.isLeftPath && p.isRightPath)
            {
                _consoleEntries.Add((false,
                    $"Path \"{(string.IsNullOrEmpty(p.segmentId) ? "(unnamed)" : p.segmentId)}\" has both Left and Right flagged",
                    p.pathId, null));
            }
        }

        if (_consoleEntries.Count == 0)
            _consoleEntries.Add((false, "No issues found", null, null));
    }

    private string GetJunctionLabel(string nodeId)
    {
        var n = _data?.nodes.Find(x => x.id == nodeId);
        return n != null ? $"{n.worldPosition.x:F0},{n.worldPosition.z:F0}" : nodeId.Substring(0, 6);
    }

    private void DrawDebugConsole()
    {
        bool hasErrors = _consoleHasErrors;
        int  errCount  = _consoleEntries.Count(e => e.isError);
        string header  = hasErrors ? $"Console  ({errCount} error{(errCount == 1 ? "" : "s")})" : "Console";

        var prevColor = GUI.color;
        GUI.color = hasErrors ? new Color(1f, 0.5f, 0.4f) : Color.white;
        _foldConsole = EditorGUILayout.Foldout(_foldConsole, header, true, EditorStyles.foldoutHeader);
        GUI.color = prevColor;

        if (!_foldConsole) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Pinned status message (save/restore feedback)
        if (!string.IsNullOrEmpty(_consoleStatusMsg))
        {
            EditorGUILayout.BeginHorizontal();
            var pc = GUI.color;
            GUI.color = new Color(0.5f, 0.9f, 1f);
            EditorGUILayout.LabelField("ℹ", GUILayout.Width(14));
            GUI.color = pc;
            var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            EditorGUILayout.LabelField(_consoleStatusMsg, style);
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(16)))
                _consoleStatusMsg = null;
            EditorGUILayout.EndHorizontal();
            EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1f), new Color(1f, 1f, 1f, 0.1f));
        }

        foreach (var (isError, msg, pathId, juncId) in _consoleEntries)
        {
            // "No issues" row is green; warnings yellow; errors red
            bool isGreen = !isError && msg == "No issues found";
            var  dotCol  = isGreen ? new Color(0.4f, 1f, 0.5f)
                         : isError ? new Color(1f, 0.4f, 0.4f)
                                   : new Color(1f, 0.85f, 0.3f);
            string dot   = isGreen ? "✓" : isError ? "✕" : "!";

            EditorGUILayout.BeginHorizontal();
            var pc = GUI.color;
            GUI.color = dotCol;
            EditorGUILayout.LabelField(dot, GUILayout.Width(14));
            GUI.color = pc;

            bool clickable = pathId != null || juncId != null;
            if (clickable)
            {
                if (GUILayout.Button(msg, EditorStyles.miniLabel))
                {
                    if (pathId  != null) _selectedPathId          = pathId;
                    if (juncId  != null) _selectedJunctionNodeId  = juncId;
                    Repaint();
                }
            }
            else
            {
                EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }

    // ══════════════════════════════════════════════════════════════
    // RIVER ID CLEANUP
    // ══════════════════════════════════════════════════════════════
    private void RiverIdCleanup()
    {
        Undo.RecordObject(_data, "River ID Cleanup");

        // Fill any empty IDs first
        int emptyIdx = 0;
        foreach (var p in _data.paths)
        {
            if (!string.IsNullOrEmpty(p.segmentId)) continue;
            bool hasMain = _data.paths.Any(x =>
                x.segmentType == LevelSelectDesignerData.SegmentType.MainRiver &&
                !string.IsNullOrEmpty(x.segmentId));
            p.segmentId = hasMain ? $"Branch_{emptyIdx:00}" : $"Main_{emptyIdx:00}";
            emptyIdx++;
        }

        // Resolve duplicates — first occurrence keeps name, subsequent get _A _B etc.
        var grouped = _data.paths.GroupBy(p => p.segmentId).Where(g => g.Count() > 1);
        foreach (var g in grouped)
        {
            int idx = 0;
            foreach (var p in g.Skip(1))
            {
                p.segmentId = g.Key + "_" + (char)('A' + idx);
                idx++;
            }
        }

        MarkDirty();
        RunValidation();
    }

    // ══════════════════════════════════════════════════════════════
    // JUNCTION DELETE
    // ══════════════════════════════════════════════════════════════
    private void DeleteJunctionNode(LevelSelectDesignerData.DesignerJunction junc)
    {
        Undo.RecordObject(_data, "Delete Junction");
        var node = _data.nodes.Find(n => n.id == junc.nodeId);
        if (node != null && node.type == LevelSelectDesignerData.NodeType.JunctionSplit)
            node.type = LevelSelectDesignerData.NodeType.Waypoint;
        _data.junctions.RemoveAll(j => j.nodeId == junc.nodeId);
        if (_selectedJunctionNodeId == junc.nodeId)
            _selectedJunctionNodeId = null;
        MarkDirty();
        Repaint();
    }

    private void DrawRightPanel()
    {
        Rect rightRect = EditorGUILayout.BeginVertical(GUILayout.Width(_rightPanelWidth), GUILayout.ExpandHeight(true));
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(rightRect, new Color(0.22f, 0.22f, 0.22f, 1f));
        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

        if (_data != null)
        {
            // ── Debug Console ─────────────────────────────────────
            DrawDebugConsole();
            EditorGUILayout.Space(2);

            // ── Setup (scene objects + prefabs) ───────────────────
            {
                var setupRect = EditorGUILayout.GetControlRect();
                float btnW = 40f, importW = 50f, gap = 3f;
                var saveRect   = new Rect(setupRect.xMax - btnW - importW - gap * 2, setupRect.y, btnW,   setupRect.height);
                var importRect = new Rect(setupRect.xMax - importW - gap,            setupRect.y, importW, setupRect.height);
                setupRect.xMax = saveRect.x - gap;
                _foldSetup = EditorGUI.Foldout(setupRect, _foldSetup, "Setup", true, EditorStyles.foldoutHeader);

                bool hasTemplate = System.IO.File.Exists(K_SetupTemplate);

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                if (GUI.Button(saveRect, new GUIContent("Save", "Save current setup as the shared template"), EditorStyles.miniButton))
                    SaveSetup();
                GUI.backgroundColor = hasTemplate ? new Color(0.5f, 1f, 0.5f) : new Color(0.6f, 0.6f, 0.6f);
                if (GUI.Button(importRect, new GUIContent("Import", hasTemplate ? "Import prefab setup from shared template" : "No template saved yet — open World1a and hit Save first"),
                        EditorStyles.miniButton))
                    RestoreSetup();
                GUI.backgroundColor = prevBg;
            }
            if (_foldSetup)
            {
                EditorGUI.indentLevel++;

                DrawSetupSubFoldout(ref _foldSetupScripts,   "Script Objects",        () =>
                {
                    DrawSceneRefsSection();
                    DrawSceneDeploySection();
                });
                DrawSetupSubFoldout(ref _foldSetupUIScript,  "UI Script Prefabs",     DrawUIScriptPrefabsSection);
                DrawSetupSubFoldout(ref _foldSetupUICanvas,  "UI Canvas Prefabs",     DrawUICanvasPrefabsSection);
                DrawSetupSubFoldout(ref _foldSetupRiver,     "River Prefabs",         DrawRiverPrefabsSection);
                DrawSetupSubFoldout(ref _foldSetupJunctions, "Junction Prefabs",      DrawJunctionPrefabsSection);
                DrawSetupSubFoldout(ref _foldSetupArenas,    "Arena Prefabs",         DrawArenaPrefabsSection);
                DrawSetupSubFoldout(ref _foldSetupObstacles, "Obstacle & Shop",       DrawObstacleShopPrefabsSection);
                DrawSetupSubFoldout(ref _foldSetupCore,      "Core & World",          DrawCorePrefabsSection);

                EditorGUI.indentLevel--;
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
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"{_gridDataOptions?.Length ?? 0} levels loaded", EditorStyles.miniLabel);
        if (GUILayout.Button("↺", EditorStyles.miniButton, GUILayout.Width(22)))
        {
            _gridDataOptions = null;
            RefreshGridDataCache();
        }
        EditorGUILayout.EndHorizontal();

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
                MarkDirty();
                GUI.backgroundColor = prevBg;
                EditorGUILayout.EndHorizontal();
                break;
            }

            GUI.backgroundColor = prevBg;
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            var newGrid = DrawGridDataPopup("", arena.gridData);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data, "Set Arena GridData");
                arena.gridData = newGrid;
                SyncEntranceNodes(arena);
                MarkDirty();
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
                MarkDirty();
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
                MarkDirty();
                Repaint();
            }
            var prevJuncBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button(new GUIContent("✕", "Delete junction — resets node to Waypoint and removes junction record"),
                    EditorStyles.miniButton, GUILayout.Width(22)))
            {
                DeleteJunctionNode(junc);
                GUI.backgroundColor = prevJuncBg;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            GUI.backgroundColor = prevJuncBg;
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
            MarkDirty();
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
            LevelSelectDesignerData.DesignerObstacle toDelete = null;
            foreach (var obs in grp.OrderBy(o => o.pathT).ToList())
            {
                bool selected = obs.obstacleId == _selectedObstacleId;
                var prev = GUI.backgroundColor;

                EditorGUILayout.BeginHorizontal();

                GUI.backgroundColor = selected ? Color.yellow : Color.clear;
                if (GUILayout.Button($"{i}. {obs.obstacleId}", EditorStyles.miniButton))
                    _selectedObstacleId = selected ? null : obs.obstacleId;

                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                    toDelete = obs;

                GUI.backgroundColor = prev;
                EditorGUILayout.EndHorizontal();
                i++;
            }
            if (toDelete != null)
            {
                Undo.RecordObject(_data, "Delete Obstacle");
                _data.obstacles.Remove(toDelete);
                if (_selectedObstacleId == toDelete.obstacleId) _selectedObstacleId = null;
                MarkDirty();
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

    private LevelSelectDesignerData.SegmentType InferBranchType(string newPathId, string startNodeId)
    {
        var parent = _data.paths.FirstOrDefault(p => p.pathId != newPathId && p.nodeIds.Contains(startNodeId));
        if (parent == null) return LevelSelectDesignerData.SegmentType.MainRiver;
        return parent.segmentType switch
        {
            LevelSelectDesignerData.SegmentType.MainRiver     => LevelSelectDesignerData.SegmentType.PrimaryBranch,
            LevelSelectDesignerData.SegmentType.PrimaryBranch => LevelSelectDesignerData.SegmentType.Secondary,
            LevelSelectDesignerData.SegmentType.Secondary     => LevelSelectDesignerData.SegmentType.Tertiary,
            _                                                  => LevelSelectDesignerData.SegmentType.Tertiary,
        };
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

        // Deploy and wire all scene script objects FIRST so managers exist before path generation
        DeployAllSceneObjects();

        // Snap main river start node to opening sequence start position if enabled
        if (_data.useOpeningSequence)
            ApplyOpeningSequenceStartNode();

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
        _arenaEntranceDirections.Clear();

        Debug.Log($"[LSD] effective junctions: {effectiveJunctions.Count} (explicit={_data.junctions.Count} auto-detected={effectiveJunctions.Count - _data.junctions.Count})");

        // Pre-pass: calculate arena shifts and correct secondary entrance nodes.
        // Secondary orbit nodes are shifted by the same vector the arena head moves (arrivalDir * offset).
        foreach (var arena in _data.arenas)
        {
            var arenaNode = _data.nodes.Find(n => n.id == arena.nodeId);
            if (arenaNode == null) continue;

            // 1. Find arrival direction (same logic as GenerateArenas)
            var branchPath = _data.paths.FirstOrDefault(p =>
                p.leadsToArena &&
                p.nodeIds.Count > 0 &&
                p.nodeIds[p.nodeIds.Count - 1] == arena.nodeId);

            Vector3 arrivalDir = Vector3.forward;
            if (branchPath != null && branchPath.nodeIds.Count >= 2)
            {
                // Note: WorldPosOfNode is safe here as arenaNode is not in _correctedNodePositions yet.
                Vector3 last = WorldPosOfNode(branchPath.nodeIds[branchPath.nodeIds.Count - 1]);
                Vector3 prev = WorldPosOfNode(branchPath.nodeIds[branchPath.nodeIds.Count - 2]);
                arrivalDir = (last - prev).normalized;
            }
            arrivalDir.y = 0f;
            if (arrivalDir.sqrMagnitude < 0.0001f) arrivalDir = Vector3.forward;
            arrivalDir.Normalize();

            Vector3 shift = arrivalDir * _data.arenaHeadOffset;

            // 2. Get radius — prefer stored arenaRadius, fall back to prefab/scene gizmo
            GameObject headPrefab = arena.arenaPrefabOverride != null ? arena.arenaPrefabOverride : _data.arenaPrefab;
            var existingArena = FindObjectsOfType<LevelSelectDesignerArenaTag>().FirstOrDefault(t => t.nodeId == arena.nodeId);
            var gizmo = existingArena?.GetComponentInChildren<LevelSelectArenaRadiusGizmo>()
                     ?? headPrefab?.GetComponentInChildren<LevelSelectArenaRadiusGizmo>();
            float radius = gizmo != null ? gizmo.radius : (arena.arenaRadius > 0.1f ? arena.arenaRadius : 10f);
            arena.arenaRadius = radius; // keep designer data in sync

            // 3. Process secondary nodes
            foreach (var sec in arena.secondaryEntrances)
            {
                var sn = _data.nodes.Find(n => n.id == sec.nodeId);
                if (sn == null) continue;

                // Snap to the true arena ring (arena center = node + arrivalShift)
                Vector3 trueArenaCenter = arenaNode.worldPosition + shift;
                Vector3 dirToSec = sn.worldPosition - trueArenaCenter; dirToSec.y = 0f;
                if (dirToSec.sqrMagnitude < 0.0001f) dirToSec = Vector3.forward;
                dirToSec.Normalize();
                sn.worldPosition = trueArenaCenter + dirToSec * radius;
                _correctedNodePositions[sec.nodeId] = sn.worldPosition;

                // Store direction INTO arena for tangent forcing
                _arenaEntranceDirections[sec.nodeId] = -dirToSec;
                }
                }

                // Pre-pass: calculate shop shifts
                foreach (var shop in _data.shops)
                {
                if (string.IsNullOrEmpty(shop.nodeId)) continue;
                var shopNode = _data.nodes.Find(n => n.id == shop.nodeId);
                if (shopNode == null) continue;

                var leadPath = _data.paths.FirstOrDefault(p =>
                p.nodeIds.Count >= 2 &&
                p.nodeIds[p.nodeIds.Count - 1] == shop.nodeId);

                if (leadPath != null)
                {
                Vector3 last = WorldPosOfNode(leadPath.nodeIds[leadPath.nodeIds.Count - 1]);
                Vector3 prev = WorldPosOfNode(leadPath.nodeIds[leadPath.nodeIds.Count - 2]);
                Vector3 arrivalDir = (last - prev).normalized;
                arrivalDir.y = 0f;
                if (arrivalDir.sqrMagnitude < 0.0001f) arrivalDir = Vector3.forward;
                arrivalDir.Normalize();

                _correctedNodePositions[shop.nodeId] = shopNode.worldPosition + arrivalDir * _data.shopHeadOffset;
                }
                }

                // Pre-pass: Tag paths connected to secondary nodes so they know if they are an Entrance or Exit.
        var secondaryNodeIds = new HashSet<string>(
            _data.arenas.SelectMany(a => a.secondaryEntrances).Select(e => e.nodeId));
        
        foreach (var path in _data.paths)
        {
            if (path.nodeIds.Count < 2) continue;
            
            // Path ENDS at secondary node -> Entrance (arena is at t=1)
            if (secondaryNodeIds.Contains(path.nodeIds[path.nodeIds.Count - 1]))
            {
                path.leadsToArena = true;
                path.arenaIsAtEnd = true;
                MarkDirty();
            }
            // Path STARTS at secondary node -> Exit (arena is at t=0)
            else if (secondaryNodeIds.Contains(path.nodeIds[0]))
            {
                path.leadsToArena = true;
                path.arenaIsAtEnd = false;
                MarkDirty();
            }
        }

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
        GenerateShops();

        WireRunInSegments(generatedContainers);
        WireSourceSegments(generatedContainers);
        WireAllSceneObjects();

        var stitcher = _data.boatPathManager;
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

        int subs  = path.curveSubdivisions > 0 ? path.curveSubdivisions
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

            // For T-junction bidirectional paths, every segment except the last is a "left half"
            // whose junction is at t=1 — it must extrude backward from the junction toward its start.
            if (path.tJunctionBidirectional && s < segBounds.Count - 1)
            {
                var so2 = new SerializedObject(segId);
                so2.Update();
                so2.FindProperty("reverseExtrude").boolValue = true;
                so2.ApplyModifiedProperties();
                EditorUtility.SetDirty(segId);
            }

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
        // Order by path index in designer (not random UUID), then T along path
        var sorted = _data.obstacles
            .OrderBy(o => { int i = _data.paths.FindIndex(p => p.pathId == o.pathId); return i < 0 ? int.MaxValue : i; })
            .ThenBy(o => o.pathT)
            .ToList();

        var spawnedGOs = new List<GameObject>();

        foreach (var obs in sorted)
        {
            GameObject prefab = obs.obstaclePrefab != null ? obs.obstaclePrefab : _data.obstaclePrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[LevelSelectDesigner] Obstacle '{obs.obstacleId}' has no prefab assigned — skipped.");
                continue;
            }

            var path = _data.paths.Find(p => p.pathId == obs.pathId);
            if (path == null || path.nodeIds.Count < 2) continue;

            Vector3? posNullable = GetWorldPosOnPath(obs.pathId, obs.pathT);
            if (!posNullable.HasValue) continue;
            Vector3 pos = posNullable.Value;
            pos.y += 0.25f;

            var     nodePositions = path.nodeIds.Select(id => (float3)WorldPosOfNode(id)).ToList();
            var     pathSpline    = SplineSplitUtility.BuildSplineFromPositions(nodePositions, TangentMode.AutoSmooth);
            float3  rawTangent    = pathSpline.EvaluateTangent(obs.pathT);
            Vector3 flowDir       = math.lengthsq(rawTangent) > 0.0001f
                                    ? (Vector3)math.normalize(rawTangent)
                                    : Vector3.forward;

            var obsGO = (GameObject)PrefabUtility.InstantiatePrefab(prefab, obstaclesParent.transform);
            Undo.RegisterCreatedObjectUndo(obsGO, "Generate Obstacle");
            obsGO.name = obs.obstacleId;
            obsGO.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-flowDir, Vector3.up));

            // Set obstacleID on both manager and obstacle-object (LevelSelectDataController reads the latter)
            var manager = obsGO.GetComponent<LevelSelectObstacleManager>()
                       ?? obsGO.GetComponentInChildren<LevelSelectObstacleManager>();
            if (manager != null)
            {
                Undo.RecordObject(manager, "Set Obstacle ID");
                manager.obstacleID = obs.obstacleId;
                EditorUtility.SetDirty(manager);
            }

            var obstacleObj = obsGO.GetComponent<LevelSelectPathObstacleObject>()
                           ?? obsGO.GetComponentInChildren<LevelSelectPathObstacleObject>();
            if (obstacleObj != null)
            {
                Undo.RecordObject(obstacleObj, "Set Obstacle ID");
                obstacleObj.obstacleID = obs.obstacleId;
                EditorUtility.SetDirty(obstacleObj);
            }

            // Enable/disable VideoOrb via the manager's designated reference
            if (manager != null)
            {
                var orbProp = new SerializedObject(manager).FindProperty("videoOrb");
                var orbGO   = orbProp?.objectReferenceValue as GameObject;
                if (orbGO != null)
                {
                    Undo.RecordObject(orbGO, "Toggle VideoOrb");
                    orbGO.SetActive(obs.hasVideoOrb);
                    EditorUtility.SetDirty(orbGO);
                }
            }

            spawnedGOs.Add(obsGO);
        }

        // Create or find RiverStopPoint child on each obstacle and wire _riverStopPoint
        var stopPoints = new List<Transform>();
        foreach (var go in spawnedGOs)
        {
            var manager = go.GetComponent<LevelSelectObstacleManager>()
                       ?? go.GetComponentInChildren<LevelSelectObstacleManager>();

            // Find or create child GO named "RiverStopPoint"
            var existing = go.transform.Find("RiverStopPoint");
            Transform stopT;
            if (existing != null)
            {
                stopT = existing;
            }
            else
            {
                var stopGO = new GameObject("RiverStopPoint");
                Undo.RegisterCreatedObjectUndo(stopGO, "Create RiverStopPoint");
                stopGO.transform.SetParent(go.transform, false);
                stopT = stopGO.transform;
            }
            stopPoints.Add(stopT);

            if (manager != null)
            {
                var so       = new SerializedObject(manager);
                var stopProp = so.FindProperty("_riverStopPoint");
                if (stopProp != null)
                {
                    stopProp.objectReferenceValue = stopT;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(manager);
                }
            }
        }

        // Build a flat list of managers in spawn order so we can chain _nextObstacle
        var managers = new List<LevelSelectObstacleManager>();
        foreach (var go in spawnedGOs)
        {
            var m = go.GetComponent<LevelSelectObstacleManager>()
                 ?? go.GetComponentInChildren<LevelSelectObstacleManager>();
            if (m != null) managers.Add(m);
        }

        // Wire _nextObstacle: each obstacle points to the next manager in sequence
        for (int i = 0; i < managers.Count; i++)
        {
            var so       = new SerializedObject(managers[i]);
            var nextProp = so.FindProperty("_nextObstacle");
            if (nextProp != null)
            {
                nextProp.objectReferenceValue = (i + 1 < managers.Count) ? managers[i + 1] : null;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(managers[i]);
            }
        }

        // Wire _firstObstacle on LevelSelectSplineManager to the first obstacle in the chain
        if (managers.Count > 0 && _data.splineManager != null)
        {
            var so   = new SerializedObject(_data.splineManager);
            var prop = so.FindProperty("_firstObstacle");
            if (prop != null)
            {
                prop.objectReferenceValue = managers[0];
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(_data.splineManager);
            }
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

            // ── Set gizmo north to canvas north (world -Z) ────────
            var radiusGizmo = arenaGO.GetComponentInChildren<LevelSelectArenaRadiusGizmo>();
            if (radiusGizmo != null)
            {
                Undo.RecordObject(radiusGizmo, "Set Gizmo North");
                radiusGizmo.northOffset = 180f;
                EditorUtility.SetDirty(radiusGizmo);
            }

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

                var proximity = entGO.GetComponentInChildren<LevelSelectArenaProximity>();
                if (proximity != null)
                {
                    proximity.arenaController = ctrl;
                    EditorUtility.SetDirty(proximity);
                }

                EditorUtility.SetDirty(ctrl);
            }

            // ── Secondary entrances ───────────────────────────────────
            foreach (var secEnt in arena.secondaryEntrances)
            {
                var secNode = _data.nodes.Find(n => n.id == secEnt.nodeId);
                if (secNode == null || _data.arenaEntrancePrefab == null) continue;

                // Canvas angle: direction from true arena center to orbit node
                Vector3 trueCenter = GetTrueArenaCenter(arena);
                Vector3 secDir3 = secNode.worldPosition - trueCenter; secDir3.y = 0f;
                if (secDir3.sqrMagnitude < 0.0001f) secDir3 = Vector3.forward;
                float canvasAngle = Mathf.Atan2(secDir3.x, secDir3.z) * Mathf.Rad2Deg;

                // Trigger always lands at rotationY - 180° (same as primary entrance).
                // To get trigger at canvasAngle: rotationY = canvasAngle + 180°
                float secRotationY = canvasAngle + 180f;

                var secEntGO = (GameObject)PrefabUtility.InstantiatePrefab(_data.arenaEntrancePrefab);
                Undo.RegisterCreatedObjectUndo(secEntGO, "Generate Secondary Arena Entrance");
                secEntGO.transform.SetParent(arenaGO.transform, false);
                secEntGO.transform.localPosition = Vector3.zero;
                secEntGO.transform.rotation = Quaternion.Euler(-90f, secRotationY, 0f);
                secEntGO.name = $"Entrance_{secEnt.entranceIndex}";

                if (ctrl != null)
                {
                    var secTrigger = secEntGO.GetComponentInChildren<LevelSelectEnter>();
                    ctrl.portalLinks.Add(new LevelSelectArenaController.PortalLink
                    {
                        entranceIndex = secEnt.entranceIndex,
                        trigger       = secTrigger
                    });

                    var secProximity = secEntGO.GetComponentInChildren<LevelSelectArenaProximity>();
                    if (secProximity != null)
                    {
                        secProximity.arenaController = ctrl;
                        EditorUtility.SetDirty(secProximity);
                    }

                    EditorUtility.SetDirty(ctrl);
                }
            }
            }
            }

            private void GenerateShops()
            {
                var shopsParent = FindOrCreateParent("SHOPS");

                foreach (var shop in _data.shops)
                {
                    GameObject prefab = shop.shopPrefabOverride != null
                        ? shop.shopPrefabOverride : _data.shopPrefab;
                    if (prefab == null) continue;

                    Vector3    pos = Vector3.zero;
                    Quaternion rot = Quaternion.identity;

                    if (!string.IsNullOrEmpty(shop.nodeId))
                    {
                        var node = _data.nodes.Find(n => n.id == shop.nodeId);
                        if (node == null) continue;
                
                        pos = WorldPosOfNode(shop.nodeId);

                        // Find arrival tangent to orient the shop
                        var leadPath = _data.paths.FirstOrDefault(p =>
                            p.nodeIds.Count >= 2 &&
                            p.nodeIds[p.nodeIds.Count - 1] == shop.nodeId);

                        if (leadPath != null)
                        {
                            Vector3 last    = WorldPosOfNode(leadPath.nodeIds[leadPath.nodeIds.Count - 1]);
                            Vector3 prev    = WorldPosOfNode(leadPath.nodeIds[leadPath.nodeIds.Count - 2]);
                            Vector3 tangent = (last - prev).normalized;
                            if (tangent != Vector3.zero)
                                rot = Quaternion.LookRotation(tangent, Vector3.up);
                        }
                    }
                    else if (!string.IsNullOrEmpty(shop.pathId))
                    {
                        Vector3? pathPos = GetWorldPosOnPath(shop.pathId, shop.pathT);
                        if (!pathPos.HasValue) continue;
                        pos = pathPos.Value;

                        // Orientation from path tangent
                        var     path      = _data.paths.Find(p => p.pathId == shop.pathId);
                        var     positions = path.nodeIds.Select(id => (float3)WorldPosOfNode(id)).ToList();
                        var     spline    = SplineSplitUtility.BuildSplineFromPositions(positions, TangentMode.AutoSmooth);
                        float3  tangent   = spline.EvaluateTangent(shop.pathT);
                        if (math.lengthsq(tangent) > 0.001f)
                            rot = Quaternion.LookRotation(math.normalize(tangent), Vector3.up);
                    }
                    else continue;

                    var shopGO = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    Undo.RegisterCreatedObjectUndo(shopGO, "Generate Shop");
                    shopGO.transform.SetParent(shopsParent.transform, true);
                    shopGO.transform.position = pos;
                    shopGO.transform.rotation = rot;
                    shopGO.name = $"Shop_{shop.nodeId ?? shop.pathId}";

                    // ── Auto-wire Proximity Script ────────────────────────
                    var proximityChild = shopGO.transform.Find("ProximityCollider");
                    if (proximityChild != null)
                    {
                        var proximity = proximityChild.GetComponent<LevelSelectShopProximity>();
                        if (proximity == null)
                            proximity = Undo.AddComponent<LevelSelectShopProximity>(proximityChild.gameObject);

                        if (proximity.shopCamera == null)
                        {
                            var camChild = shopGO.transform.Find("CinemachineCamera");
                            if (camChild != null)
                            {
                                proximity.shopCamera = camChild.GetComponent<Unity.Cinemachine.CinemachineCamera>();
                                EditorUtility.SetDirty(proximity);
                            }
                        }
                    }

                    // ── Spawn items at ShopItemPoints ─────────────────────
                    if (shop.shopItems != null)
                    {
                        var points = shopGO.GetComponentsInChildren<ShopItemPoint>(includeInactive: true);
                        foreach (var point in points)
                        {
                            int idx = point.slotIndex;
                            if (idx < 0 || idx >= shop.shopItems.Length) continue;
                            var itemPrefab = shop.shopItems[idx];
                            if (itemPrefab == null) continue;

                            var itemGO = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefab);
                            Undo.RegisterCreatedObjectUndo(itemGO, "Spawn Shop Item");
                            itemGO.transform.SetParent(point.transform, false);
                            itemGO.transform.localPosition = Vector3.zero;
                            itemGO.transform.localRotation = Quaternion.identity;
                        }
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

    // Returns the arena radius for designer ring drawing and node placement.
    // Reads from the prefab's LevelSelectArenaRadiusGizmo and caches into arenaData.arenaRadius.
    private float GetArenaRadius(LevelSelectDesignerData.DesignerArena arenaData)
    {
        if (arenaData.arenaRadius > 0.1f) return arenaData.arenaRadius;

        GameObject prefab = arenaData.arenaPrefabOverride != null
            ? arenaData.arenaPrefabOverride : _data?.arenaPrefab;
        if (prefab != null)
        {
            var gizmo = prefab.GetComponentInChildren<LevelSelectArenaRadiusGizmo>();
            if (gizmo != null)
            {
                arenaData.arenaRadius = gizmo.radius;
                MarkDirty();
                return arenaData.arenaRadius;
            }
        }
        return 10f; // matches LevelSelectArenaRadiusGizmo default
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

    private void WireRunInSegments(List<SplineContainer> generatedContainers)
    {
        var runIns = FindObjectsOfType<EntranceSplineRunIn>();
        foreach (var runIn in runIns)
        {
            var container = runIn.GetComponent<SplineContainer>();
            var segId     = runIn.GetComponent<RiverSegmentID>();
            if (container == null || segId == null || container.Spline.Count < 2) continue;

            // Knot 0 is the river-connection side.
            // Check the arena-flagged endpoint of each path — last knot for arenaIsAtEnd=true,
            // first knot for arenaIsAtEnd=false — so branches in either direction match correctly.
            Vector3 runInKnot0  = container.transform.TransformPoint((Vector3)container.Spline[0].Position);

            SplineContainer nearest      = null;
            float           nearestDist  = float.MaxValue;
            bool            matchedStart = false;

            foreach (var c in generatedContainers)
            {
                if (c == null || c.Spline.Count == 0) continue;
                var cSegId = c.GetComponent<RiverSegmentID>();
                if (cSegId == null || !cSegId.LeadsToArena) continue;

                Vector3 arenaKnot;
                bool    atStart;
                if (cSegId.ArenaIsAtEnd)
                {
                    arenaKnot = c.transform.TransformPoint((Vector3)c.Spline[c.Spline.Count - 1].Position);
                    atStart   = false;
                }
                else
                {
                    arenaKnot = c.transform.TransformPoint((Vector3)c.Spline[0].Position);
                    atStart   = true;
                }

                float dist = Vector3.Distance(runInKnot0, arenaKnot);
                if (dist < nearestDist) { nearestDist = dist; nearest = c; matchedStart = atStart; }
            }

            if (nearest == null)
            {
                Debug.LogWarning($"[LSD] EntranceSplineRunIn '{runIn.name}': no matching path found.");
                continue;
            }

            // Copy segment identity from the parent path so stitching appends to the same highway.
            // arenaIsAtEnd on the RunIn always matches the parent — the RunIn sits at the same end.
            var srcSegId = nearest.GetComponent<RiverSegmentID>();
            if (srcSegId != null)
            {
                var so = new SerializedObject(segId);
                so.Update();
                so.FindProperty("segmentID").stringValue      = srcSegId.SegmentID;
                so.FindProperty("junctionGroup").stringValue  = srcSegId.JunctionGroup;
                so.FindProperty("segmentType").enumValueIndex = (int)srcSegId.Type;
                so.FindProperty("isLeftPath").boolValue       = srcSegId.IsLeftPath;
                so.FindProperty("isRightPath").boolValue      = srcSegId.IsRightPath;
                so.FindProperty("extrudeOnExit").boolValue    = srcSegId.ExtrudeOnExit;
                so.FindProperty("leadsToArena").boolValue     = true;
                so.FindProperty("arenaIsAtEnd").boolValue     = srcSegId.ArenaIsAtEnd;
                so.FindProperty("skipRegistration").boolValue = true;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(segId);
            }

            // Replace both terminal knots with exact RunIn positions so the path
            // follows the RunIn geometry and welds correctly.
            Vector3 runInK1World    = container.transform.TransformPoint((Vector3)container.Spline[1].Position);
            int     arenaKnotIdx    = matchedStart ? 0 : nearest.Spline.Count - 1;
            int     adjacentKnotIdx = matchedStart ? 1 : nearest.Spline.Count - 2;

            var k0snap = nearest.Spline[arenaKnotIdx];
            k0snap.Position = (Unity.Mathematics.float3)nearest.transform.InverseTransformPoint(runInKnot0);
            nearest.Spline.SetTangentMode(arenaKnotIdx, TangentMode.AutoSmooth);
            nearest.Spline[arenaKnotIdx] = k0snap;

            var k1snap = nearest.Spline[adjacentKnotIdx];
            k1snap.Position = (Unity.Mathematics.float3)nearest.transform.InverseTransformPoint(runInK1World);
            nearest.Spline.SetTangentMode(adjacentKnotIdx, TangentMode.AutoSmooth);
            nearest.Spline[adjacentKnotIdx] = k1snap;

            EditorUtility.SetDirty(nearest);

            // The branch's terminal knots now carry the RunIn geometry, so the RunIn container
            // does NOT go into generatedContainers (that would duplicate those knots in the boat path).
            // SplineRiverManager picks up the RunIn independently via its RiverSegmentID in the scene.
            Debug.Log($"[LSD] RunIn '{runIn.name}' → '{nearest.name}' dist={nearestDist:F3}m — terminal knots replaced, RunIn excluded from boat path");
        }
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

        Wire(_data.riverManager     ?? FindObjectOfType<SplineRiverManager>());
        Wire(_data.boatPathManager  ?? FindObjectOfType<SplinePathStitcher>());

        // Wire SplineRiverManager → LevelSelectSplineManager._riverManager
        var splineManager = _data.splineManager ?? FindObjectOfType<LevelSelectSplineManager>();
        var riverManager  = _data.riverManager  ?? FindObjectOfType<SplineRiverManager>();
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
            MarkDirty();
    }

    private void ClearGeneratedObjects()
    {
        foreach (var name in new[] { "MAINRIVERVISUALS", "RIVERBRANCHES", "RIVERJUNCTIONS", "RIVERGATEsobstacles", "ARENAS", "SHOPS", "BoatPaths", "LANDSCAPETILES", "LEVELSELECT_SCRIPTS", "PlayerBoat", "CANVAS", "RiverExtrusion", "CAMERA" })
        {
            var go = GameObject.Find(name);
            if (go == null) continue;
            var children = Enumerable.Range(0, go.transform.childCount)
                .Select(i => go.transform.GetChild(i).gameObject).ToList();
            foreach (var child in children) Undo.DestroyObjectImmediate(child);
        }

        // Clear scene object references so deploy status resets
        if (_data != null)
        {
            Undo.RecordObject(_data, "Clear Scene References");
            _data.segmentRegistry  = null;
            _data.dataController   = null;
            _data.riverManager     = null;
            _data.splineManager    = null;
            _data.boatControl      = null;
            _data.cameraController      = null;
            _data.soulDisplaySlotManager = null;
            _data.musicController        = null;
            _data.pauseManager           = null;
            MarkDirty();
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

        SetSplineFromPositions(container, positions, path.curveStrength);

        if (container.Splines.Count == 0) return;
        var spline = container.Splines[0];
        if (spline.Count < 2) return;

        // ── 1. Trim leading end (Junctions only) ──────────────────
        if (isJunctionStart && _data.branchStartOffset > 0f)
        {
            SplineUtility.GetPointAtLinearDistance(spline, 0f, _data.branchStartOffset, out float T_start);

            int subs  = path.curveSubdivisions > 0 ? path.curveSubdivisions : 5;
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

        // ── 2. Force start tangent (Junction or Arena Exit) ───────
        string  startNodeId = path.nodeIds[0];
        Vector3 forceDir    = Vector3.zero;
        bool    forceStart  = false;

        if (_junctionPerpDirections.TryGetValue(startNodeId, out var perp))
        {
            forceDir   = perp;
            forceStart = true;
        }
        else if (_arenaEntranceDirections.TryGetValue(startNodeId, out var arenaDir))
        {
            // For an EXIT path (emanating), point AWAY from center
            forceDir   = -arenaDir;
            forceStart = true;
        }

        if (forceStart && spline.Count > 0)
        {
            var    knot      = spline[0];
            float3 localForce = container.transform.InverseTransformDirection(forceDir);
            float  tanLen    = math.length(knot.TangentOut);
            if (tanLen < 0.0001f) tanLen = 0.333f;

            knot.TangentOut = math.normalize(localForce) * tanLen;
            spline.SetKnot(0, knot);
            spline.SetTangentMode(0, TangentMode.Broken);
        }

        // ── 3. Force end tangent (Arena Entrance) ─────────────────
        string endNodeId = path.nodeIds[path.nodeIds.Count - 1];
        if (_arenaEntranceDirections.TryGetValue(endNodeId, out var entDir) && spline.Count > 0)
        {
            var    knot      = spline[spline.Count - 1];
            float3 localForce = container.transform.InverseTransformDirection(entDir);
            float  tanLen    = math.length(knot.TangentIn);
            if (tanLen < 0.0001f) tanLen = 0.333f;

            knot.TangentIn = math.normalize(localForce) * tanLen;
            spline.SetKnot(spline.Count - 1, knot);
            spline.SetTangentMode(spline.Count - 1, TangentMode.Broken);
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

    // Direction INTO the arena per secondary entrance node.
    // Used to align entrance/exit paths with the spawned gate prefabs.
    private readonly Dictionary<string, Vector3> _arenaEntranceDirections = new();

    private Vector3 WorldPosOfNode(string nodeId)
    {
        if (_correctedNodePositions.TryGetValue(nodeId, out var corrected))
            return corrected;
        return _data.nodes.Find(n => n.id == nodeId)?.worldPosition ?? Vector3.zero;
    }

    private static void SetSplineFromPositions(SplineContainer container, List<Vector3> worldPositions, float curveStrength = 1f)
    {
        float tangentScale = 0.333f * Mathf.Max(0.01f, curveStrength);

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
                tanIn  = (pos - local[i - 1]) * tangentScale;
                tanOut = (local[i + 1] - pos) * tangentScale;
            }
            else if (i == 0 && local.Count > 1)
            {
                tanOut = (local[1] - pos) * tangentScale;
                tanIn  = -tanOut;
            }
            else if (i == local.Count - 1 && local.Count > 1)
            {
                tanIn  = (pos - local[i - 1]) * tangentScale;
                tanOut = -tanIn;
            }

            spline.Add(new BezierKnot(pos, -tanIn, tanOut, quaternion.identity), TangentMode.AutoSmooth);
        }

        SplineSplitUtility.ZeroKnotRollAngles(spline);

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
                MarkDirty();
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
            MarkDirty();
            SyncHillPointsToScene();
            e.Use();
        }
    }

    private void DrawLandscapePanel()
    {
        if (_data == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Landscape Tiles", EditorStyles.boldLabel);

        // Canvas overlay alpha — window-only preference, not saved to data asset
        EditorGUI.BeginChangeCheck();
        _landscapeCanvasAlpha = EditorGUILayout.Slider("Canvas Alpha", _landscapeCanvasAlpha, 0f, 1f);
        if (EditorGUI.EndChangeCheck()) Repaint();

        EditorGUI.BeginChangeCheck();
        _data.landscapeTileSize = EditorGUILayout.FloatField("Tile Size",  _data.landscapeTileSize);
        _data.landscapeTilesX   = EditorGUILayout.IntField("Tiles X",     _data.landscapeTilesX);
        _data.landscapeTilesZ   = EditorGUILayout.IntField("Tiles Z",     _data.landscapeTilesZ);
        _data.landscapeOffset   = EditorGUILayout.Vector2Field("Offset XZ", _data.landscapeOffset);
        _data.landscapeWorldY   = EditorGUILayout.FloatField("World Y",   _data.landscapeWorldY);
        if (EditorGUI.EndChangeCheck())
        {
            MarkDirty();
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
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("❐", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                Undo.RecordObject(_data, "Duplicate Hill Point");
                var dupe = new LevelSelectDesignerData.LandscapeHillPoint
                {
                    id         = System.Guid.NewGuid().ToString(),
                    positionXZ = hp.positionXZ + new Vector2(2f, 2f),
                    scale      = hp.scale,
                    height     = hp.height,
                };
                _data.hillPoints.Insert(i + 1, dupe);
                _selectedHillPointId = dupe.id;
                MarkDirty();
                SyncHillPointsToScene();
                GUI.backgroundColor = rowBg;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                Undo.RecordObject(_data, "Delete Hill Point");
                _data.hillPoints.RemoveAt(i);
                if (_selectedHillPointId == hp.id) _selectedHillPointId = null;
                MarkDirty();
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
            MarkDirty();
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

        float a          = _landscapeCanvasAlpha;
        var fillColor    = new Color(0.38f, 0.38f, 0.38f, 0.55f * a);
        var outlineColor = new Color(0.55f, 0.55f, 0.55f, 0.8f  * a);

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
            var   fillCol    = new Color(brightness, brightness, brightness, 0.4f * a);
            var   ringCol    = sel ? Color.yellow : new Color(brightness, brightness, brightness, 0.9f * a);

            Handles.color = fillCol;
            Handles.DrawSolidDisc(new Vector3(cp.x, cp.y, 0), Vector3.forward, cr);

            Handles.color = ringCol;
            Handles.DrawWireDisc(new Vector3(cp.x, cp.y, 0), Vector3.forward, cr);

            if (_mode == DesignerMode.Landscape)
            {
                Handles.color = sel ? Color.yellow : Color.white;
                Handles.DrawSolidDisc(new Vector3(cp.x, cp.y, 0), Vector3.forward, 4f);
                Handles.color = Color.white;
            }
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

        // Auto-assign target material
        if (tool.targetMaterial == null)
        {
            var guids = AssetDatabase.FindAssets("LevelSelectLandscape t:Material");
            var mat   = guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
            if (mat != null)
            {
                tool.targetMaterial = mat;
                EditorUtility.SetDirty(tool);
                Debug.Log($"[LevelSelectDesigner] Assigned material '{mat.name}' to LandscapeTool.");
            }
            else
                Debug.LogWarning("[LevelSelectDesigner] Could not find material 'LevelSelectLandscape' in project.");
        }

        // HillPointsContainer child for hill handles
        var hillContainer = new GameObject("Hill_Points_Container");
        Undo.RegisterCreatedObjectUndo(hillContainer, "Create Hill_Points_Container");
        hillContainer.transform.SetParent(parentGo.transform, false);
        tool.hillHandlesParent = hillContainer.transform;

        // Update data's landscapeTool reference so it's wired up
        if (_data.landscapeTool == null)
        {
            _data.landscapeTool = tool;
            MarkDirty();
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
