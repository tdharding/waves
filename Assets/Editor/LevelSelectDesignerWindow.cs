using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR

public partial class LevelSelectDesignerWindow : EditorWindow
{
    // ── Modes ─────────────────────────────────────────────────────
    private enum DesignerMode { Draw, Select, Junction, Obstacle, Landscape, SoulRoute, Pipe, Perch, Wall, Spike, FogZone, Structure, RiverSelect, PipeSelect }

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

        // The workspace copy is what the fields below edit, so it is what the edit-mode pump
        // should be publishing — the asset on disk is a save behind until Save is pressed.
        LevelSelectAestheticsPump.Preview = _data;

        if (_data != null)
        {
            _data.name = asset.name + " (Workspace)";
            _data.hideFlags = HideFlags.HideAndDontSave;
        }

        hasUnsavedChanges = false;
        if (_sourceData != null)
        {
            EditorPrefs.SetString(K_DataPath, AssetDatabase.GetAssetPath(_sourceData));
        }
    }

    public override void SaveChanges()
    {
        if (_sourceData == null || _data == null) return;

        Undo.RecordObject(_sourceData, "Save Level Select Designer Changes");
        string originalName = _sourceData.name;
        EditorUtility.CopySerialized(_data, _sourceData);
        _sourceData.name = originalName;
        EditorUtility.SetDirty(_sourceData);
        AssetDatabase.SaveAssets();

        hasUnsavedChanges = false;
        ChangeLogAddAction("Saved to asset");
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

    private void PlaytestLinkedScene(bool freshSave = false, bool skipOpening = false)
    {
        // Set before anything can call the launch off; erased again below if it is.
        SessionState.SetBool(CameraSequence.SkipOpeningKey, skipOpening);
        try { LaunchPlaytest(freshSave); }
        finally
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                SessionState.EraseBool(CameraSequence.SkipOpeningKey);
        }
    }

    private void LaunchPlaytest(bool freshSave)
    {
        if (_data == null || string.IsNullOrEmpty(_data.targetScenePath)) return;

        string sceneName = System.IO.Path.GetFileNameWithoutExtension(_data.targetScenePath);

        bool inBuildSettings = false;
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.path == _data.targetScenePath) { inBuildSettings = true; break; }
        }
        if (!inBuildSettings)
        {
            EditorUtility.DisplayDialog("Playtest",
                $"Scene '{sceneName}' is not in Build Settings, so the GameTesterTool cannot launch it.\n\n" +
                "Add it via File > Build Settings, then try again.", "OK");
            return;
        }

        if (hasUnsavedChanges) SaveChanges();

        // Matches the GameTesterTool "Fresh Save" button: wipe progress, then launch.
        if (freshSave) GameProgressData.ClearAll();

        GameTesterTool.Open();
        GameTesterTool.LaunchScene(sceneName, null, null);
    }

    private void MarkDirty()
    {
        MarkDirtyUnlogged();
        ChangeLogNoteEdit();
        // A node that moved takes its pinned cameras along, in the same undo step.
        SyncPinnedCinematicCameras();
        // And anything that moved takes the shot cameras clamped to it.
        SyncCameraClamps();
    }

    // Undo/redo marks dirty through here — the change log records those by their own name.
    private void MarkDirtyUnlogged()
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
    private string _selectedPoolNodeId;
    private string _selectedJunctionNodeId;
    private string _selectedShopNodeId;
    private string _selectedHillPointId;
    private bool   _isDraggingHillPoint;
    private bool   _canvasFocused;
    private int    _selectedEntranceIdx   = -1;

    // Right panel foldout states
    private bool _foldPathProps   = true;
    private bool _foldPaths       = false;
    private bool _foldJunctions   = false;
    private bool _foldArenas      = false;
    private bool _foldPools       = false;
    private bool _foldRivers      = false;
    private bool _foldObstacles   = false;
    private bool _foldStats       = false;
    private bool _foldSetup       = false;

    // ── Setup sub-foldouts ────────────────────────────────────────
    private bool _foldSetupScripts   = false;
    private bool _foldSetupUIScript  = false;
    private bool _foldSetupUICanvas  = false;
    private bool _foldSetupRiver     = false;
    private bool _foldSetupArenas    = false;
    private bool _foldSetupObstacles = false;
    private bool _foldSetupCore      = false;
    private bool _foldSetupAudio     = false;
    private bool _foldSetupFog       = false;
    private bool _foldSetupCinematics = false;
    private bool _foldAesthetics = false;
    private bool _foldConsole     = false;

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
    private const float SCROLLBAR_W = 14f;

    // ── Panel sizes ───────────────────────────────────────────────
    private float   _leftPanelWidth  = 210f;
    private float   _rightPanelWidth = 175f;

    // ── GridData resource cache ───────────────────────────────────
    private GridData[] _gridDataOptions;

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
            RemoveArenaLeadIns(arena, last.nodeId);
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

        // A new entrance on a compass arena takes a free point, and a lead-in if it has a river.
        if (arena.compassEntrances) RefreshArenaLeadIns(arena);

        MarkDirty();
    }

    private string GetArenaPresetName(LevelSelectDesignerData.DesignerArena arena)
    {
        float r = arena.gridData != null ? arena.gridData.arenaRadius : 0f;
        return r > 0f ? $"r {r:0.##}" : "None";
    }

    private int IndexOfGridData(GridData grid)
    {
        for (int i = 0; i < _gridDataOptions.Length; i++)
            if (_gridDataOptions[i] == grid) return i;
        return -1;
    }

    private static string GridDataLabel(GridData grid) =>
        string.IsNullOrEmpty(grid.displayName) ? grid.name : grid.displayName;

    /// Hands back the arena's own level untouched unless a different one is picked. A level the
    /// list doesn't hold (made or replaced since it was read) re-reads the list first, and if it
    /// still isn't there it is shown as its own entry — never as "(none)", which is what used to
    /// wipe it the next time the popup was drawn.
    private GridData DrawGridDataPopup(string label, GridData current)
    {
        EnsureGridDataCache();
        int currentIdx = IndexOfGridData(current);
        if (current != null && currentIdx < 0)
        {
            Debug.LogWarning($"[LevelSelectDesigner] Arena level '{current.name}' " +
                             $"({AssetDatabase.GetAssetPath(current)}, id {current.GetEntityId()}) " +
                             $"not in the level list — re-reading Assets/Resources/Levels.");
            RefreshGridDataCache();
            currentIdx = IndexOfGridData(current);
        }

        int  count = _gridDataOptions.Length;
        bool stray = current != null && currentIdx < 0;
        var  labels = new string[count + 1 + (stray ? 1 : 0)];
        labels[0] = "(none)";
        for (int i = 0; i < count; i++) labels[i + 1] = GridDataLabel(_gridDataOptions[i]);
        if (stray) labels[count + 1] = $"{GridDataLabel(current)}  (not in Resources/Levels)";

        int shown    = stray ? count + 1 : currentIdx + 1;
        int selected = EditorGUILayout.Popup(label, shown, labels);
        if (selected == shown)  return current;
        if (selected == 0)      return null;
        if (selected <= count)  return _gridDataOptions[selected - 1];
        return current;
    }

    // A level saved, made, renamed or deleted anywhere marks the list to be read again.
    private void OnProjectChangedGridData()
    {
        _gridDataOptions = null;
        Repaint();
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
    private const string K_ScriptsLock = "LSD_ScriptsLocked";

    // ── Constants ─────────────────────────────────────────────────
    private const float NODE_RADIUS     = 6f;
    private const float SNAP_RADIUS     = 14f;
    private const float OBSTACLE_RADIUS = 5f;
    private const float PATH_HIT_DIST   = 8f;

    // ══════════════════════════════════════════════════════════════
    // MENU / LIFECYCLE
    // ══════════════════════════════════════════════════════════════
    [MenuItem("Tools/Level Design/Level Select Designer", false, 101)]
    public static void Open() => GetWindow<LevelSelectDesignerWindow>("Level Select Designer");

    private void OnEnable()
    {
        _viewCenter      = new Vector2(EditorPrefs.GetFloat(K_ViewCX, 0f), EditorPrefs.GetFloat(K_ViewCY, 0f));
        _zoom            = EditorPrefs.GetFloat(K_Zoom,   20f);
        _leftPanelWidth  = EditorPrefs.GetFloat(K_LeftW,  210f);
        _rightPanelWidth = EditorPrefs.GetFloat(K_RightW, 175f);
        _scriptsLocked   = EditorPrefs.GetBool(K_ScriptsLock, true);

        string savedPath = EditorPrefs.GetString(K_DataPath, "");
        if (!string.IsNullOrEmpty(savedPath))
        {
            var asset = AssetDatabase.LoadAssetAtPath<LevelSelectDesignerData>(savedPath);
            if (asset != null)
                SetData(asset);
        }

        if (_data != null)
            TryAutoFillPrefabs();

        Undo.undoRedoPerformed += OnUndoRedoPerformed;
        SpikeStudio.PresetSaved += OnSpikePresetSaved;
        LevelSelectPresetEditorWindow.PresetEdited += OnPresetEdited;
        EditorApplication.projectChanged += OnProjectChangedGridData;
        ChangeLogEnable();
    }

    private void OnDisable()
    {
        SaveViewPrefs();
        if (_sourceData != null)
            EditorPrefs.SetString(K_DataPath, AssetDatabase.GetAssetPath(_sourceData));

        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        SpikeStudio.PresetSaved -= OnSpikePresetSaved;
        LevelSelectPresetEditorWindow.PresetEdited -= OnPresetEdited;
        EditorApplication.projectChanged -= OnProjectChangedGridData;
        FlushTowerRebuilds();
        ChangeLogDisable();
        DestroyLandscapeMap();

        // Hand the world back to the scene's own data, which the pump reads once this is gone.
        LevelSelectAestheticsPump.Preview = null;
    }

    private void OnUndoRedoPerformed()
    {
        // An undone or redone soul route point moves the list under the + marker, so clicks go
        // back on the end rather than in after the wrong point.
        _insertAfterPoint = -1;

        // An edit still waiting to rebuild was just undone (or redone) along with its mesh.
        DropTowerRebuilds();

        MarkDirtyUnlogged();
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
        WatchPlaytestPick();

        if (Event.current.type == EventType.Layout)
        {
            SyncPoolLeadIns();
            SyncArenaLeadIns();
            SyncJunctionLeadIns();
            SyncInBetweenNodes();
            RunValidation();
        }

        DrawToolbar();
        DrawModeBar();

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

        RememberPlaytestCanvasRect();
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

                if (GUILayout.Button(new GUIContent("Playtest",
                        "Open the GameTesterTool and enter play mode in this world's scene"),
                        EditorStyles.toolbarButton, GUILayout.Width(64)))
                    PlaytestLinkedScene();
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

        GUILayout.Space(6);

        if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(36)))
            CreateNewDataAsset();

        if (GUILayout.Button("Load", EditorStyles.toolbarButton, GUILayout.Width(38)))
            LoadDataAsset();

        GUI.enabled = _data != null;
        if (GUILayout.Button("Save As", EditorStyles.toolbarButton, GUILayout.Width(54)))
            SaveAsDataAsset();
        GUI.enabled = true;

        GUILayout.FlexibleSpace();

        DrawScriptsLockButton(EditorStyles.toolbarButton);

        if (GUILayout.Button("Frame All", EditorStyles.toolbarButton, GUILayout.Width(64)))
            FrameAll();

        EditorGUILayout.EndHorizontal();
    }

    // ── Mode bar — second row, sitting over the canvas column ─────
    // The working modes sit on the bar itself; everything that places scenery is under Add.
    // Obstacles are no longer a toolbar mode — they become a per-node / per-path feature.
    private static readonly DesignerMode[] BarModes =
        { DesignerMode.Draw, DesignerMode.Select, DesignerMode.Junction,
          DesignerMode.Landscape, DesignerMode.SoulRoute, DesignerMode.FogZone };
    private static readonly string[] BarModeLabels =
        { "Draw", "Select", "Junction", "Landscape", "Souls", "Fog Zones" };

    private static readonly (DesignerMode mode, string label, string shortLabel)[] AddModes =
    {
        (DesignerMode.Structure, "Freestanding Structures", "Structures"),
        (DesignerMode.Wall,      "Walls",                   "Walls"),
        (DesignerMode.Spike,     "Spikes",                  "Spikes"),
        (DesignerMode.Perch,     "Perches",                 "Perches"),
        (DesignerMode.Pipe,      "Pipes",                   "Pipes"),
    };

    // A mode picked from the Add menu, switched to on the next GUI pass — the menu's callback runs
    // outside OnGUI, and switching mode lets go of focus, which only works inside it.
    private DesignerMode? _pendingMode;

    private void DrawModeBar()
    {
        if (_pendingMode.HasValue && Event.current.type == EventType.Layout)
        {
            SetMode(_pendingMode.Value);
            _pendingMode = null;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        // Line up with the canvas column, past the left panel and its handle.
        GUILayout.Space(_leftPanelWidth + HANDLE_W);

        for (int i = 0; i < BarModes.Length; i++)
        {
            if (BarModes[i] == DesignerMode.Select) { DrawSelectButton(); continue; }
            bool on = _mode == BarModes[i];
            if (GUILayout.Toggle(on, BarModeLabels[i], EditorStyles.toolbarButton,
                                 GUILayout.Width(72f), GUILayout.Height(18)) && !on)
                SetMode(BarModes[i]);
        }

        GUILayout.Space(6);
        DrawAddMenu();

        GUILayout.FlexibleSpace();
        if (GUILayout.Button(new GUIContent("Settings", "Canvas display settings: the height map, hill point markers."),
                             EditorStyles.toolbarButton, GUILayout.Width(64)))
            LevelSelectDesignerSettingsWindow.Open();
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// The Select button, with an arrow beside it to choose between Select and River Select. The
    /// button itself gives whichever of the two was picked last.
    /// </summary>
    private void DrawSelectButton()
    {
        bool on    = _mode == DesignerMode.Select || _mode == DesignerMode.RiverSelect || _mode == DesignerMode.PipeSelect;
        string txt = _selectVariant == DesignerMode.RiverSelect ? "River Select"
                   : _selectVariant == DesignerMode.PipeSelect  ? "Pipe Select" : "Select";
        if (GUILayout.Toggle(on, txt, EditorStyles.toolbarButton,
                             GUILayout.Width(_selectVariant == DesignerMode.Select ? 54f : 76f),
                             GUILayout.Height(18)) && !on)
            SetMode(_selectVariant);

        var  arrow = new GUIContent("", "Choose the Select tool: Select, River Select to pick and move whole rivers, or Pipe Select to pick and move whole pipes.");
        Rect rect  = GUILayoutUtility.GetRect(arrow, EditorStyles.toolbarDropDown, GUILayout.Width(18f));
        if (!EditorGUI.DropdownButton(rect, arrow, FocusType.Passive, EditorStyles.toolbarDropDown)) return;

        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Select"), _mode == DesignerMode.Select,
                     () => { _pendingMode = DesignerMode.Select; Repaint(); });
        menu.AddItem(new GUIContent("River Select"), _mode == DesignerMode.RiverSelect,
                     () => { _pendingMode = DesignerMode.RiverSelect; Repaint(); });
        menu.AddItem(new GUIContent("Pipe Select"), _mode == DesignerMode.PipeSelect,
                     () => { _pendingMode = DesignerMode.PipeSelect; Repaint(); });
        menu.DropDown(rect);
    }

    /// <summary>
    /// The Add dropdown: every mode that places scenery on the map. It reads "Add: Walls" and is
    /// lit while one of them is the mode.
    /// </summary>
    private void DrawAddMenu()
    {
        int active = System.Array.FindIndex(AddModes, a => a.mode == _mode);
        var label  = new GUIContent(active >= 0 ? $"Add: {AddModes[active].shortLabel}" : "Add",
                                    "Place scenery on the map — structures, walls, spikes, perches, pipes.");

        Rect rect = GUILayoutUtility.GetRect(label, EditorStyles.toolbarDropDown, GUILayout.MinWidth(70f));
        var  prev = GUI.backgroundColor;
        if (active >= 0) GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
        bool open = EditorGUI.DropdownButton(rect, label, FocusType.Passive, EditorStyles.toolbarDropDown);
        GUI.backgroundColor = prev;
        if (!open) return;

        var menu = new GenericMenu();
        foreach (var add in AddModes)
        {
            var mode = add.mode;
            menu.AddItem(new GUIContent(add.label), _mode == mode, () => { _pendingMode = mode; Repaint(); });
        }
        menu.DropDown(rect);
    }

    /// <summary>Switches mode, finishing or letting go of whatever the last one was in the middle of.</summary>
    private void SetMode(DesignerMode newMode)
    {
        if (newMode == _mode) return;

        CancelPlaytestPick("you switched tool");
        _mode = newMode;
        if (_mode == DesignerMode.Select || _mode == DesignerMode.RiverSelect || _mode == DesignerMode.PipeSelect) _selectVariant = _mode;
        if (_mode != DesignerMode.RiverSelect) ClearRiverSelect();
        if (_mode != DesignerMode.PipeSelect)  ClearPipeSelect();
        if (_isDrawingPipe) FinishDrawingPipe();
        if (_isDrawingWall) FinishDrawingWall();
        _isDrawing           = false;
        _isDraggingObstacle  = false;
        _draggingObstacleId  = null;
        _isDraggingStructure = false;
        _drawingNodeIds.Clear();
        if (_mode == DesignerMode.Landscape || _mode == DesignerMode.FogZone) ClearSelectionForLandscape();
        if (_mode == DesignerMode.SoulRoute) ClearSelectionForSouls();
        if (_mode == DesignerMode.Structure) ClearSelectionForStructures();
        // A structure stays picked only where it can be worked on, so it never shares the panel.
        else if (_mode != DesignerMode.Select) _selectedStructureId = null;
        // Shot cameras are picked and clamped in Select mode only.
        if (_mode != DesignerMode.Select) { _selectedShotCamera = null; _clampPicking = false; }
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
        dirty |= TryFill(ref _data.arenaEntrancePrefab,   "LEVELSELECTARENAENTRANCE");

        if (_data.riverMaterial == null)
        {
            string[] matGuids = AssetDatabase.FindAssets("RiverRunMarbleRunMat t:Material");
            if (matGuids.Length > 0)
            {
                _data.riverMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    AssetDatabase.GUIDToAssetPath(matGuids[0]));
                dirty |= _data.riverMaterial != null;
            }
        }


        // Scan directory for junction / arena / obstacle / shop by name pattern
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            string name      = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLower();
            var    prefab    = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) continue;

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
        float canvasW = position.width - _leftPanelWidth - _rightPanelWidth - SCROLLBAR_W;
        float canvasH = position.height - EditorGUIUtility.singleLineHeight - 4f - SCROLLBAR_W;
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

        if (_data == null)
        {
            EditorGUILayout.HelpBox("Load or create a LevelSelectDesignerData asset.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        // Pinned block — sits above the scroll view so it is always reachable.
        DrawGenerateBlock();
        var sepRect = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(sepRect, new Color(0.12f, 0.12f, 0.12f, 1f));
        EditorGUILayout.Space(2);

        _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

        // River Select shows only the rivers it has picked.
        if (_mode == DesignerMode.RiverSelect)
        {
            DrawRiverSelectList();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        // Pipe Select shows only the pipes it has picked.
        if (_mode == DesignerMode.PipeSelect)
        {
            DrawPipeSelectList();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        // A shot camera picked on the map shows alone.
        if (_selectedShotCamera != null && _mode == DesignerMode.Select)
        {
            DrawShotCameraPanel();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        DrawSelectedPathProps();

        // Everything that stands on a node is drawn inside that node's row in the Nodes list
        // above. These are the same sections for the times one is picked on the canvas or in a
        // list with no river of its own selected.
        if (!DrawnUnderOpenNode(_selectedNodeId))
        {
            DrawSelectedNodeActions();
            DrawSelectedRimNodeProps();
            DrawSelectedWalkwayProps();
            DrawSelectedBridgeProps();
            DrawNodeAudio(_selectedNodeId);
            DrawNodeCinematic(_selectedNodeId);
        }
        if (!DrawnUnderOpenNode(_selectedArenaNodeId)) DrawSelectedArenaProps();
        if (!DrawnUnderOpenNode(_selectedPoolNodeId))  DrawSelectedPoolProps();
        if (!DrawnUnderOpenNode(_selectedShopNodeId))  DrawSelectedShopProps();

        var openOutpost = _data.outposts.Find(o => o.outpostId == _selectedOutpostId);
        if (openOutpost == null || !DrawnUnderOpenNode(OutpostNodeId(openOutpost)))
            DrawSelectedOutpostProps();

        DrawSelectedObstacleProps();
        DrawSelectedPipeProps();
        DrawSelectedWallProps();
        DrawSelectedPerchProps();
        DrawSelectedSpikeProps();
        DrawSelectedStructureProps();
        DrawCanvasSettingsSection();

        if (_mode == DesignerMode.Landscape)
            DrawLandscapePanel();

        if (_mode == DesignerMode.SoulRoute)
            DrawSoulRoutePanel();

        if (_mode == DesignerMode.Perch)
            DrawPerchPanel();

        if (_mode == DesignerMode.Spike)
            DrawSpikePanel();

        if (_mode == DesignerMode.Structure)
            DrawStructurePanel();

        if (_mode == DesignerMode.FogZone)
            DrawFogZonePanel();

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
            string label = (selected ? "● " : "  ") + PathDisplayName(path);
            if (GUILayout.Button(label, EditorStyles.miniButton))
            {
                _selectedPathId = selected ? null : path.pathId;
                ClearNodeSelection();
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
                segmentId = NextSegmentId("Segment"),
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
        // The ID is the system's, never typed — the lore name is the one to write in.
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.TextField(new GUIContent("Segment ID", "System-assigned and unique."),
                                      path.segmentId);
        path.loreName     = EditorGUILayout.TextField(
            new GUIContent("Lore Name", "The path's name in the world's lore. Free text, may repeat."),
            path.loreName);
        path.riverName    = EditorGUILayout.TextField("River Name",    path.riverName);
        path.segmentType  = (LevelSelectDesignerData.SegmentType)EditorGUILayout.EnumPopup("Type", path.segmentType);
        bool runTypeChanged = DrawPathRunType(path);
        path.isLeftPath    = EditorGUILayout.Toggle("Is Left Path",      path.isLeftPath);
        path.isRightPath = EditorGUILayout.Toggle("Is Right Path",   path.isRightPath);
        path.editorColor  = EditorGUILayout.ColorField("Color",        path.editorColor);
        EditorGUILayout.Space(2);
        bool leadsToArena = EditorGUILayout.Toggle("Leads to Arena", path.leadsToArena);
        if (leadsToArena != path.leadsToArena)
        {
            Undo.RecordObject(_data, leadsToArena ? "Add Arena" : "Remove Arena");
            SetLeadsToArena(path, leadsToArena);
        }
        path.arenaIsAtEnd          = EditorGUILayout.Toggle("Arena at End",          path.arenaIsAtEnd);
        path.tJunctionBidirectional = EditorGUILayout.Toggle("T-Junction Both Ways", path.tJunctionBidirectional);
        EditorGUILayout.Space(2);
        path.curveStrength     = EditorGUILayout.Slider("Curve Strength",      path.curveStrength,     0.1f, 3f);
        path.curveSubdivisions = EditorGUILayout.IntSlider("Curve Subdivisions", path.curveSubdivisions, 1,    20);

        EditorGUILayout.LabelField($"Knots: {path.nodeIds.Count}", EditorStyles.miniLabel);

        bool influenceChanged = DrawPathLandscapeInfluence(path);

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

        if (influenceChanged) RebuildAfterEdit("LandscapeInfluence", () => RefreshLandscapeInfluence());

        // A natural run's walls come and go with Landscape Influence, so a change to either
        // rebuilds it.
        if (runTypeChanged || (influenceChanged && IsNaturalRun(path) && !path.outerWalls))
            RebuildAfterEdit("RunType:" + path.pathId, () => ApplyRunType(path));

        // ── Run Shape ─────────────────────────────────────────────
        // The same per-river override Setup > River Runs lists, for this path's river.
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Run Shape", EditorStyles.boldLabel);
        // A natural run's rim settings are this path's own, not the river's shared shape below.
        if (DrawNaturalRimSettings(path)) RebuildAfterEdit("RunType:" + path.pathId, () => ApplyRunType(path));
        if (string.IsNullOrEmpty(path.riverName))
            EditorGUILayout.LabelField("Give this path a River Name to shape its river.", EditorStyles.miniLabel);
        else if (!DrawRiverShapeOverride(path.riverName) &&
                 _data.riverProfiles.Find(p => p != null && p.riverName == path.riverName) == null)
            DrawRiverProfileReadOnly("Default shape", _data.ProfileFor(null));

        // ── Arena end ─────────────────────────────────────────────
        // Which end of the river the arena stands on — the arena itself is edited inside that
        // node's row below.
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = path.nodeIds.Count > 0;
        var startBg = GUI.backgroundColor;
        GUI.backgroundColor = !path.arenaIsAtEnd ? new Color(0.3f, 0.7f, 1f) : Color.gray;
        if (GUILayout.Button("Arena at Start", EditorStyles.miniButtonLeft) && path.arenaIsAtEnd)
        {
            Undo.RecordObject(_data, "Move Arena to Start");
            MoveArenaToEnd(path, atEnd: false);
        }
        GUI.backgroundColor = path.arenaIsAtEnd ? new Color(0.3f, 0.7f, 1f) : Color.gray;
        if (GUILayout.Button("Arena at End", EditorStyles.miniButtonRight) && !path.arenaIsAtEnd)
        {
            Undo.RecordObject(_data, "Move Arena to End");
            MoveArenaToEnd(path, atEnd: true);
        }
        GUI.backgroundColor = startBg;
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        // ── Nodes: one row each, in river order ───────────────────
        // Open a row and it shows everything standing on that node — its pool, rim node,
        // arena, shop and outposts. Only one row is open at a time: the node that is selected.
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Nodes", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("",        GUILayout.Width(50));
        EditorGUILayout.LabelField("Height",  EditorStyles.miniLabel, GUILayout.Width(66 + 22 + 4));
        EditorGUILayout.LabelField("On node", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        // The end the arena stands on, whether or not there is one there yet.
        string arenaEndNodeId = path.nodeIds.Count > 0
            ? (path.arenaIsAtEnd ? path.nodeIds[path.nodeIds.Count - 1] : path.nodeIds[0])
            : null;

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

            bool isOpen = node.id == _selectedNodeId;
            var prevBg = GUI.backgroundColor;
            if (isOpen) GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = prevBg;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button($"{(isOpen ? "▼" : "▶")} {typeLabel} {i}",
                                 EditorStyles.label, GUILayout.Width(50)))
            {
                if (isOpen) ClearNodeSelection();
                else        SelectNodeInList(node.id);
                GUI.FocusControl(null);
                Repaint();
            }

            EditorGUI.BeginChangeCheck();
            // Lead-ins and compass entrances take their pool's or arena's height — edited there.
            // In-between nodes take theirs from the nodes either side.
            GUI.enabled = !IsLockedNode(node.id) && !IsPlacedInBetween(node.id);
            // The ↔ is the field's own drag handle: drag it left to lower the node, right to raise it.
            float prevLabelW = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 14f;
            float newY = EditorGUILayout.FloatField(
                new GUIContent("↔", "Drag left to lower this node, right to raise it. Press ⟳ to see it in the scene."),
                node.worldPosition.y, GUILayout.Width(66));
            EditorGUIUtility.labelWidth = prevLabelW;
            GUI.enabled = true;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data, "Edit Node Y");
                node.worldPosition = new Vector3(node.worldPosition.x, newY, node.worldPosition.z);
                MarkDirty();
            }
            if (GUILayout.Button(new GUIContent("⟳", "Regenerate the scene so it shows the node heights as they are now."),
                                 EditorStyles.miniButton, GUILayout.Width(22)))
                QueueLiveRegenerate();

            EditorGUILayout.LabelField(NodeDetails(path, node.id), EditorStyles.miniLabel);

            // The rect is taken first: once the click is used, GetLastRect hands back a dummy
            // (0,0,1,1) and the menu opened at the window's top-left corner.
            var nodeMenuLabel = new GUIContent("Node", "Add a node before or after this one, or split the river here.");
            Rect nodeMenuRect = GUILayoutUtility.GetRect(nodeMenuLabel, EditorStyles.miniPullDown, GUILayout.Width(110));
            if (EditorGUI.DropdownButton(nodeMenuRect, nodeMenuLabel, FocusType.Passive, EditorStyles.miniPullDown))
                ShowNodeMenu(path, i, nodeMenuRect);

            EditorGUILayout.EndHorizontal();

            if (isOpen)
            {
                EditorGUI.indentLevel++;
                DrawNodeContents(path, node, arenaEndNodeId);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        // Adding a pool or an outpost adds nodes of its own, so both wait until the list has
        // finished drawing rather than changing it half way down.
        if (_pendingPoolToggleNodeId != null)
        {
            TogglePoolAt(_pendingPoolToggleNodeId);
            _pendingPoolToggleNodeId = null;
        }
        if (_pendingOutpostNodeId != null)
        {
            AddOutpostAt(path, _pendingOutpostNodeId);
            _pendingOutpostNodeId = null;
        }
    }

    /// <summary>
    /// A node row's menu: a node either side of it, or the river split in two at it. Picked
    /// after the list has drawn, so each item finds its river again by ID before acting.
    /// </summary>
    private void ShowNodeMenu(LevelSelectDesignerData.DesignerPath path, int index, Rect under)
    {
        var    menu   = new GenericMenu();
        string pathId = path.pathId;

        void Item(string label, bool on, GenericMenu.MenuFunction act)
        {
            if (on) menu.AddItem(new GUIContent(label), false, act);
            else    menu.AddDisabledItem(new GUIContent(label));
        }

        // Off an end: why not, or that the river will carry on through another one starting there.
        string AddLabel(string label, int at)
        {
            var ids = path.nodeIds;
            if (ids.Count < 2 || (at > 0 && at < ids.Count)) return label;
            string endId  = at <= 0 ? ids[0] : ids[ids.Count - 1];
            string whyNot = WhyCannotCarryOnFrom(path, endId);
            if (whyNot != null) return $"{label} ({whyNot})";
            return AnotherRiverMeets(path, endId) ? $"{label} (carry on through)" : label;
        }

        Item(AddLabel("Add Node Before", index),     CanInsertNodeAt(path, index),     () => AddNodeFromMenu(pathId, index));
        Item(AddLabel("Add Node After",  index + 1), CanInsertNodeAt(path, index + 1), () => AddNodeFromMenu(pathId, index + 1));
        menu.AddSeparator("");

        string why = WhyRiverCannotSplitAt(path, index);
        Item(why == null ? "Split River Here" : $"Split River Here ({why})", why == null,
             () => SplitRiverFromMenu(pathId, index));

        // Where another river meets this node: name the meeting.
        string nodeId = path.nodeIds[index];
        if (AnotherRiverMeets(path, nodeId))
        {
            menu.AddSeparator("");
            string whyJunction = WhyCannotMakeJunctionAt(nodeId);
            Item(whyJunction == null ? "Make Junction Here" : $"Make Junction Here ({whyJunction})",
                 whyJunction == null, () => NameMeetingFromMenu(nodeId, junction: true));
            string whyJoin = WhyCannotJoinRiversAt(nodeId);
            Item(whyJoin == null ? "Join Rivers Here" : $"Join Rivers Here ({whyJoin})",
                 whyJoin == null, () => NameMeetingFromMenu(nodeId, junction: false));
        }

        menu.DropDown(under);
    }

    private void AddNodeFromMenu(string pathId, int at)
    {
        var path = _data != null ? _data.paths.Find(p => p.pathId == pathId) : null;
        if (path == null || !CanInsertNodeAt(path, at)) return;

        Undo.RecordObject(_data, "Add Node");
        bool   offEnd = at <= 0 || at >= path.nodeIds.Count;
        string oldEnd = at <= 0 ? path.nodeIds[0] : path.nodeIds[path.nodeIds.Count - 1];
        var added = InsertNodeInPath(path, at);
        if (offEnd && AnotherRiverMeets(path, oldEnd)) NameJunctionCarriedThrough(oldEnd);
        SelectNodeInList(added.id);
        MarkDirty();
        Repaint();
    }

    private void NameMeetingFromMenu(string nodeId, bool junction)
    {
        if (_data == null) return;
        if (junction ? WhyCannotMakeJunctionAt(nodeId) != null
                     : WhyCannotJoinRiversAt(nodeId)   != null) return;

        Undo.RecordObject(_data, junction ? "Make Junction" : "Join Rivers");
        if (junction) MakeJunctionAt(nodeId);
        else          JoinRiversAt(nodeId);
        MarkDirty();
        Repaint();
    }

    private void SplitRiverFromMenu(string pathId, int index)
    {
        var path = _data != null ? _data.paths.Find(p => p.pathId == pathId) : null;
        if (path == null || WhyRiverCannotSplitAt(path, index) != null) return;

        Undo.RecordObject(_data, "Split River");
        SplitRiverAt(path, index);
        MarkDirty();
        Repaint();
    }

    // Set by a node's row, carried out once the Nodes list has finished drawing.
    private string _pendingPoolToggleNodeId;
    private string _pendingOutpostNodeId;

    // A node's height reaches the scene only through a full Generate — the run curves are
    // resampled and trimmed at pools and junctions, so no knot stands for one node. Editing a
    // height doesn't regenerate; the ⟳ beside it queues one, never more than one waiting.
    private bool _liveRegenerateQueued;

    private void QueueLiveRegenerate()
    {
        if (_liveRegenerateQueued) return;
        _liveRegenerateQueued = true;
        EditorApplication.delayCall += () =>
        {
            _liveRegenerateQueued = false;
            // The same gate the GENERATE button has.
            if (_data == null || !IsValidLevelSelectScene() || _consoleHasErrors) return;
            PruneLooseNodes();
            Generate();
            Repaint();
        };
    }

    /// <summary>
    /// Everything standing on one node, drawn inside its open row in the Nodes list: what can
    /// be put here, then the arena, rim node, pool, shop and outposts that already are.
    /// </summary>
    private void DrawNodeContents(LevelSelectDesignerData.DesignerPath path,
                                  LevelSelectDesignerData.DesignerNode node,
                                  string arenaEndNodeId)
    {
        string nodeId = node.id;

        EditorGUILayout.BeginHorizontal();
        bool hasPool = _data.PoolAt(nodeId) != null;
        if (GUILayout.Button(hasPool ? "Remove Pool" : "Add Pool"))
            _pendingPoolToggleNodeId = nodeId;
        GUI.enabled = path.nodeIds.Count >= 2;
        if (GUILayout.Button("Add Outpost"))
            _pendingOutpostNodeId = nodeId;
        DrawBridgeButton(path, nodeId);
        // A node has one type, so a shop can't go on an arena or junction node.
        bool hasShop = node.type == LevelSelectDesignerData.NodeType.ShopEnd;
        GUI.enabled = hasShop || node.type == LevelSelectDesignerData.NodeType.Waypoint;
        if (GUILayout.Button(hasShop ? "Remove Shop" : "Add Shop"))
            ToggleShopAt(nodeId);
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        // The arena only ever stands on the end node the river runs into.
        if (nodeId == arenaEndNodeId)
        {
            if (!path.leadsToArena)
            {
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
                if (GUILayout.Button("Add Arena", GUILayout.Height(22)))
                {
                    Undo.RecordObject(_data, "Add Arena");
                    SetLeadsToArena(path, true);
                }
                GUI.backgroundColor = prevBg;
            }
            else if (!_data.arenas.Exists(a => a.nodeId == nodeId))
            {
                EditorGUILayout.HelpBox("No arena assigned at path endpoint.", MessageType.Warning);
            }
        }

        DrawNodeInBetween(path, node);

        if (_selectedArenaNodeId == nodeId) DrawSelectedArenaProps();
        DrawSelectedRimNodeProps();
        DrawSelectedWalkwayProps();
        DrawSelectedBridgeProps();
        DrawNodeAudio(nodeId);
        DrawNodeCinematic(nodeId);
        // A pool's controls show under its node whenever the row is open, as the rim node's do.
        if (hasPool) DrawPoolProps(nodeId);
        if (_selectedShopNodeId == nodeId) DrawSelectedShopProps();
        DrawNodeOutposts(path, nodeId);
    }

    /// <summary>
    /// The outposts standing at this node. An outpost keeps a free fraction along the river
    /// rather than a node, so each shows under the node it stands nearest — slide it past
    /// halfway with Along Path and it moves to the next node's row.
    /// </summary>
    private void DrawNodeOutposts(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        var here = _data.outposts
            .Where(o => o.pathId == path.pathId && OutpostNodeId(o) == nodeId)
            .OrderBy(o => o.pathT)
            .ToList();
        if (here.Count == 0) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Outposts", EditorStyles.boldLabel);

        foreach (var outpost in here)
        {
            bool isOpen = outpost.outpostId == _selectedOutpostId;
            string banks = outpost.side == LevelSelectDesignerData.OutpostSide.Both
                         ? "both banks" : $"{outpost.side} bank";
            if (GUILayout.Button($"{(isOpen ? "▼" : "▶")} Outpost — {banks}",
                                 EditorStyles.miniButton))
            {
                _selectedOutpostId = isOpen ? null : outpost.outpostId;
                GUI.FocusControl(null);
                Repaint();
            }

            if (isOpen)
            {
                EditorGUI.indentLevel++;
                DrawSelectedOutpostProps();
                EditorGUI.indentLevel--;
            }
        }
    }

    /// The node an outpost stands nearest along its river, or null if it has no river.
    private string OutpostNodeId(LevelSelectDesignerData.DesignerOutpost outpost)
    {
        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        int stretches = (path?.nodeIds.Count ?? 0) - 1;
        if (stretches < 1) return null;
        return path.nodeIds[Mathf.RoundToInt(Mathf.Clamp01(outpost.pathT) * stretches)];
    }

    /// <summary>
    /// Picking a node in the list opens it, and only it. Whatever stands on that node comes
    /// with it, so the panel shows that node's pool, arena or shop rather than another's.
    /// </summary>
    private void SelectNodeInList(string nodeId)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        _selectedNodeId      = nodeId;
        _selectedObstacleId  = null;
        _selectedOutpostId   = null;
        _selectedStructureId = null;
        _selectedArenaNodeId = node?.type == LevelSelectDesignerData.NodeType.ArenaEnd ? nodeId : null;
        _selectedShopNodeId  = node?.type == LevelSelectDesignerData.NodeType.ShopEnd  ? nodeId : null;
        _selectedPoolNodeId  = _data.PoolAt(nodeId) != null ? nodeId : null;
        _selectedEntranceIdx = -1;
    }

    /// Closes the open node row, letting go of everything that came with it.
    private void ClearNodeSelection()
    {
        _selectedStructureId = null;
        _selectedNodeId      = null;
        _selectedOutpostId   = null;
        _selectedArenaNodeId = null;
        _selectedShopNodeId  = null;
        _selectedPoolNodeId  = null;
        _selectedEntranceIdx = -1;
    }

    /// Whether this node's things are already drawn inside its open row in the Nodes list.
    private bool DrawnUnderOpenNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || nodeId != _selectedNodeId) return false;
        var path = _data.paths.Find(p => p.pathId == _selectedPathId);
        return path != null && path.nodeIds.Contains(nodeId);
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

        EditorGUILayout.LabelField(
            arena.compassEntrances
                ? $"Entrances on compass points  |  Lead-ins: {arena.leadIns?.Count ?? 0}"
                : "Entrances free — Refresh Lead-ins puts them on compass points",
            EditorStyles.miniLabel);
        if (GUILayout.Button("Refresh Lead-ins"))
        {
            Undo.RecordObject(_data, "Refresh Arena Lead-ins");
            _consoleStatusMsg = RefreshArenaLeadIns(arena)
                ? $"Arena lead-ins refreshed ({arena.leadIns.Count})."
                : "Arena has more than four entrances — lead-ins not refreshed.";
            MarkDirty();
            Repaint();
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

        DrawSelectedArenaProcGen(arena);
    }

    /// <summary>
    /// This arena's own share of Setup's Arena Walls and Entrances sections: its wall, each
    /// entrance's river overlap, its archways, the door in them and how deep each door sits.
    /// The same rows as Setup, so an edit here is the same edit made there. One that follows a
    /// default shows that default's numbers greyed; the defaults themselves are edited in Setup.
    /// </summary>
    private void DrawSelectedArenaProcGen(LevelSelectDesignerData.DesignerArena arena)
    {
        // ── Wall ──────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Wall", EditorStyles.boldLabel);
        DrawOneArenaWall(arena, "Wall", showDefault: true);
        if (GUILayout.Button("Rebuild Arena Wall"))
        {
            int n = RebuildArenaWallMeshes(arena.nodeId);
            AssetDatabase.SaveAssets();
            _consoleStatusMsg = $"Rebuilt {n} arena wall(s).";
        }

        // ── River overlap at each entrance ────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("River Overlap", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"Unticked entrances follow River Overlap ({_data.arenaRunOverlap:0.##}) in Setup.",
            EditorStyles.miniLabel);
        if (DrawArenaEntranceOverlaps(arena)) RebuildAfterEdit("RunMeshes", () => RebuildRunMeshes());

        // ── Archways ──────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Archways", EditorStyles.boldLabel);
        DrawOneArenaArchway(arena, "Archways on entrances", showDefault: true);

        if (arena.archwayOnEntrances)
        {
            // ── Door ──────────────────────────────────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Door in the archway", EditorStyles.boldLabel);
            DrawOneArenaDoor(arena, showDefault: true);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Door depth", EditorStyles.miniBoldLabel);
            DrawArenaEntranceAlignments(arena);
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(
            "Turning archways on or off, or moving a door, needs a full Generate.",
            EditorStyles.miniLabel);
        if (GUILayout.Button("Rebuild Archways"))
        {
            int n = RebuildArchwayMeshes(arena.nodeId);
            AssetDatabase.SaveAssets();
            _consoleStatusMsg = $"Rebuilt {n} archway(s).";
        }

        DrawBoatExitSection(arena);
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
        _data.riverMaterial            = (Material)EditorGUILayout.ObjectField("Run Material",     _data.riverMaterial,            typeof(Material), false);
        _data.waterMaterial            = (Material)EditorGUILayout.ObjectField("Water Material",   _data.waterMaterial,            typeof(Material), false);
        if (EditorGUI.EndChangeCheck()) MarkDirty();

        EditorGUILayout.LabelField("Run shape and water are authored under Procedural Generation.",
                                   EditorStyles.miniLabel);
    }

    // ════════════════════════════════════════════════════════════
    // PROCEDURAL GENERATION
    // ════════════════════════════════════════════════════════════
    //
    // Everything the level's geometry is generated from, in one place: the river runs, the
    // pools they widen into, and the walls ringing the arenas. Each is a default shape every
    // instance takes, with an override for the ones that want their own — rivers keyed by
    // name, pools and arenas by the node they sit on.

    /// <summary>
    /// Detail and water level, then the run cross-section — the default, and an override per
    /// river that wants its own.
    /// </summary>
    /// <summary>
    /// The one drop every generated piece takes, at the head of Procedural Generation because it
    /// belongs to all of them. A run and a pool are built from their rim top and a wall from the
    /// water surface, so a run carries the water level on top of the drop — and the undersides
    /// of all three land at the same height. Nothing overrides it.
    /// </summary>
    private void DrawProcGenDropField()
    {
        EditorGUI.BeginChangeCheck();
        float drop = EditorGUILayout.FloatField(
            new GUIContent("Drop",
                "How far every generated piece carries on below the water surface — runs, pools " +
                "and arena walls alike, so they all end at the same height and the world reads " +
                "as bottomless. One number for the lot; nothing overrides it."),
            _data.generatedDrop);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Drop");
            _data.generatedDrop = Mathf.Max(0f, drop);
            MarkDirty();
            RebuildAfterEdit("Drop", () =>
            {
                RebuildRunMeshes();
                RebuildPoolMeshes();
                RebuildArenaWallMeshes();
            });
        }

        EditorGUILayout.LabelField(
            $"Undersides {_data.RunDepth:F2} below the rim top",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(2);
    }

    private void DrawProcGenRiversSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.splineInstantiateSpacing = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            new GUIContent("Detail",
                "Target length of an edge in every generated mesh. Ring spacing along a run, " +
                "the columns across its section, and a pool's rings and columns all come off " +
                "this one number — so a run, a branch and a pool all carry the same density. " +
                "Smaller is finer and heavier."),
            _data.splineInstantiateSpacing));

        _data.waterFilled              = EditorGUILayout.Toggle("Water Filled",                    _data.waterFilled);
        using (new EditorGUI.DisabledScope(!_data.waterFilled))
            _data.waterLevel = Mathf.Max(0f, EditorGUILayout.FloatField("Water Level",               _data.waterLevel));
        EditorGUILayout.HelpBox(
            _data.waterFilled
                ? "Every river is generated already full of water, and nothing blocks the boat. " +
                  "The surface is each run's own inner width, held Water Level below the rim top " +
                  "on every river — so a run that climbs or drops carries its water with it."
                : "No water is generated.",
            MessageType.None);
        if (EditorGUI.EndChangeCheck())
        {
            MarkDirty();
            // The water level is part of how far a run reaches down, so the pools go with it.
            RebuildAfterEdit("RiversSection", () =>
            {
                RebuildRunMeshes();
                RebuildPoolMeshes();
            });
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Run Shape", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each river is one mesh, this section swept along its spline and carrying straight " +
            "on through its junctions. A branch is cut into the run it meets, as a mouth of the " +
            "branch's own inner width and river depth.",
            MessageType.None);

        DrawRiverProfile("Default (all rivers)", _data.ProfileFor(null), false);

        // One override per river name the designer has paths for.
        var riverNames = _data.paths
            .Select(p => p.riverName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        foreach (string riverName in riverNames)
            if (DrawRiverShapeOverride(riverName)) break;

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Rebuild Runs"))
        {
            int n = RebuildRunMeshes();
            AssetDatabase.SaveAssets();
            _consoleStatusMsg = $"Rebuilt {n} river run(s).";
            Debug.Log($"[LevelSelectDesigner] Rebuilt {n} river run(s) from the current Run Shapes.");
        }
    }

    /// <summary>
    /// Pool shape - the default every pool takes, and an override for the ones that want their
    /// own. A pool's cross-section still comes from the river running into it; these are the
    /// numbers that are the pool's own.
    /// </summary>
    private void DrawProcGenPoolsSection()
    {
        EditorGUILayout.HelpBox(
            "A pool is the river's own section revolved about its node. Island 0 leaves an open " +
            "bowl; anything larger leaves a plinth in the middle and makes it a roundabout.",
            MessageType.None);

        DrawPoolShape("Default (all pools)", _data.defaultPoolShape);

        EditorGUI.BeginChangeCheck();
        float leadInDistance = EditorGUILayout.FloatField(
            new GUIContent("Lead-in Distance",
                "How far each pool's lead-in nodes stand out past the point its rivers are cut off " +
                "at (the rim, plus the collar). 0 puts them right on the cut."),
            _data.poolLeadInDistance);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Lead-in Distance");
            _data.poolLeadInDistance = Mathf.Max(0f, leadInDistance);
            MarkDirty();
        }

        // Each pool's own override is authored on the pool itself — select it and it shows in
        // the left panel.

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Rebuild Pools"))
        {
            int n = RebuildPoolMeshes();
            AssetDatabase.SaveAssets();
            _consoleStatusMsg = $"Rebuilt {n} pool(s).";
            Debug.Log($"[LevelSelectDesigner] Rebuilt {n} pool(s) from the current shapes.");
        }
    }

    /// <summary>
    /// Everything about the selected pool, in the left panel: its canvas colour, its shape (the
    /// default, or its own override of it), its lead-ins and its tower.
    /// </summary>
    private void DrawSelectedPoolProps() => DrawPoolProps(_selectedPoolNodeId);

    private void DrawPoolProps(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        var pool = _data.PoolAt(nodeId);
        if (pool == null) return;

        int arrivals = _data.PathsAtPool(pool).Count;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Pool", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"{PoolKindLabel(pool, arrivals)}  |  {arrivals} river{(arrivals == 1 ? "" : "s")}",
            EditorStyles.miniLabel);

        EditorGUI.BeginChangeCheck();
        Color colour = EditorGUILayout.ColorField("Editor Colour", pool.editorColor);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Pool");
            pool.editorColor = colour;
            MarkDirty();
            Repaint();
        }

        // ── Shape (procedural) ────────────────────────────────────
        EditorGUILayout.Space(4);
        string poolRiver = _data.PoolRiverName(pool);
        EditorGUILayout.LabelField(
            $"Cross-section from river: {(string.IsNullOrEmpty(poolRiver) ? "(default)" : poolRiver)}",
            EditorStyles.miniLabel);

        // Always editable. A pool on the default shows the default's numbers; editing one gives
        // this pool its own shape, starting from them — the default itself is never touched here.
        var shape = _data.PoolShapeFor(pool).Clone();
        var before = shape.Clone();
        if (DrawPoolShape(pool.overrideShape ? "Shape: own" : "Shape: default", shape,
                          removable: pool.overrideShape))
        {
            Undo.RecordObject(_data, "Use Default Pool Shape");
            pool.overrideShape = false;
            MarkDirty();
            RebuildPoolMeshes();
        }
        else if (shape.poolRadius   != before.poolRadius   ||
                 shape.islandRadius != before.islandRadius ||
                 shape.floorDepth   != before.floorDepth)
        {
            LevelSelectDesignerData.ApplyPoolShape(pool, shape);
            pool.overrideShape = true;
        }

        if (GUILayout.Button("Rebuild Pools"))
        {
            int n = RebuildPoolMeshes();
            AssetDatabase.SaveAssets();
            _consoleStatusMsg = $"Rebuilt {n} pool(s).";
            Debug.Log($"[LevelSelectDesigner] Rebuilt {n} pool(s) from the current shapes.");
        }
        EditorGUILayout.LabelField(
            "The boat ring is generated but not yet wired to boat travel.",
            EditorStyles.miniLabel);

        // ── Lead-ins ──────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(
            $"Lead-ins: {pool.leadIns?.Count ?? 0} of {arrivals} river{(arrivals == 1 ? "" : "s")}",
            EditorStyles.miniLabel);
        if (GUILayout.Button("Refresh Lead-ins"))
        {
            Undo.RecordObject(_data, "Refresh Pool Lead-ins");
            int missed = RefreshPoolLeadIns(pool);
            _consoleStatusMsg = missed > 0
                ? $"Lead-ins refreshed — {missed} river(s) left without one (four compass points)."
                : $"Lead-ins refreshed ({pool.leadIns.Count}).";
            MarkDirty();
            Repaint();
        }

        // ── Tower ─────────────────────────────────────────────────
        EditorGUILayout.Space(4);
        DrawPoolTowerFields(pool);
    }

    // What the designer drew reads straight off the numbers, so name it that way.
    private static string PoolKindLabel(LevelSelectDesignerData.DesignerPool pool, int arrivals)
    {
        return pool.islandRadius <= 0f ? "Open pool"
             : arrivals >= 3           ? "Roundabout"
                                       : "Pool with a centre";
    }

    // Returns true when the designer asked to drop this pool back to the default shape.
    private bool DrawPoolShape(string header, PoolShape shape, bool removable = false)
    {
        bool remove = false;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        if (removable && GUILayout.Button("Use default", EditorStyles.miniButton, GUILayout.Width(90)))
            remove = true;
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        float radius = EditorGUILayout.FloatField(
            new GUIContent("Pool Radius", "Radius of the water. The rim goes on outside it."),
            shape.poolRadius);
        float island = EditorGUILayout.FloatField(
            new GUIContent("Island Radius",
                "Radius of the plinth in the middle, flush with the rim. 0 leaves an open pool."),
            shape.islandRadius);
        float floor = EditorGUILayout.FloatField(
            new GUIContent("Floor Depth",
                "How far the floor drops below the rim at its deepest. 0 takes the river's own depth."),
            shape.floorDepth);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Pool Shape");
            shape.poolRadius   = Mathf.Max(0.05f, radius);
            shape.islandRadius = Mathf.Clamp(island, 0f, shape.poolRadius - 0.01f);
            shape.floorDepth   = Mathf.Max(0f, floor);
            MarkDirty();
        }

        EditorGUILayout.LabelField(
            $"Boat ring radius {RiverMeshBuilder.PoolChannelRadius(shape.poolRadius, shape.islandRadius):F2}",
            EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        return remove;
    }

    /// <summary>
    /// What to call an arena in the designer: the level it leads to. The node id is what the
    /// data keys on, but it says nothing about which level you are looking at — so the GridData
    /// name leads, and the id only stands in when no level is assigned yet.
    /// </summary>
    private static string ArenaLabel(LevelSelectDesignerData.DesignerArena arena)
    {
        if (arena == null) return "(none)";

        string level = arena.gridData != null ? arena.gridData.displayName : null;
        if (string.IsNullOrWhiteSpace(level))
            level = arena.gridData != null ? arena.gridData.name : null;

        return string.IsNullOrWhiteSpace(level)
             ? $"{arena.nodeId}  (no level)"
             : $"{level}";
    }

    /// <summary>
    /// Arena wall shape - the default every arena takes, and an override per arena. The radius
    /// is the arena boundary, so an override writes it back onto the arena, and the ring on the
    /// canvas and the entrance nodes orbiting it follow the wall that is really there.
    /// </summary>
    private void DrawProcGenArenaWallsSection()
    {
        EditorGUILayout.HelpBox(
            "A round wall standing on the arena boundary. Radius is the inner face - the wall's " +
            "closest approach to the centre - with the thickness laid off outward. Height is " +
            "measured up from the water surface; how far it carries on below is the one Drop.",
            MessageType.None);

        EditorGUI.BeginChangeCheck();
        float arenaLeadIn = EditorGUILayout.FloatField(
            new GUIContent("Lead-in Distance",
                "How far each arena's lead-in nodes stand out from the entrance they lead to. " +
                "0 puts them right on the entrance."),
            _data.arenaLeadInDistance);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Arena Lead-in Distance");
            _data.arenaLeadInDistance = Mathf.Max(0f, arenaLeadIn);
            MarkDirty();
        }

        EditorGUI.BeginChangeCheck();
        float overlap = EditorGUILayout.FloatField(
            new GUIContent("River Overlap",
                "How far a river pushes into the arena past the wall's inner face. Every run is " +
                "put on that face first, carried on or pulled back as its own arena needs, so 0 " +
                "leaves them all ending flush with the inside of the wall."),
            _data.arenaRunOverlap);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit River Overlap");
            _data.arenaRunOverlap = Mathf.Max(0f, overlap);
            MarkDirty();
            RebuildAfterEdit("RunMeshes", () => RebuildRunMeshes());
        }

        DrawEntranceOverlaps();

        EditorGUILayout.Space(4);
        DrawArenaWall("Default (all arenas)", _data.defaultArenaWall);

        if (_data.arenas.Count == 0)
            EditorGUILayout.LabelField("  (no arenas)", EditorStyles.miniLabel);

        foreach (var arena in _data.arenas)
        {
            if (arena == null || string.IsNullOrEmpty(arena.nodeId)) continue;
            if (DrawOneArenaWall(arena, ArenaLabel(arena), showDefault: false)) break;
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Rebuild Arena Walls"))
        {
            int n = RebuildArenaWallMeshes();
            AssetDatabase.SaveAssets();
            _consoleStatusMsg = $"Rebuilt {n} arena wall(s).";
            Debug.Log($"[LevelSelectDesigner] Rebuilt {n} arena wall(s) from the current shapes.");
        }
    }

    /// <summary>
    /// One arena's wall: the offer to override the default, or its own shape once it has one.
    /// Drawn in Setup's Arena Walls list and under the selected arena in the left panel, where
    /// <paramref name="showDefault"/> also shows the numbers it is following, greyed.
    /// Returns true when it went back to the default, so a caller looping over arenas can stop.
    /// </summary>
    private bool DrawOneArenaWall(LevelSelectDesignerData.DesignerArena arena, string header,
                                  bool showDefault)
    {
        if (!arena.overrideWall)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(header, EditorStyles.miniLabel);
            if (GUILayout.Button("Override shape", EditorStyles.miniButton, GUILayout.Width(110)))
            {
                Undo.RecordObject(_data, "Add Arena Wall Shape");
                // Opens on the size the arena already is, not on the default's radius.
                arena.wallProfile  = _data.ArenaWallFor(arena).Clone();
                arena.overrideWall = true;
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            if (showDefault && !arena.overrideWall)
                using (new EditorGUI.DisabledScope(true))
                    DrawArenaWall("Wall: default", _data.ArenaWallFor(arena));
            return false;
        }

        if (DrawArenaWall(header, arena.wallProfile, removable: true))
        {
            Undo.RecordObject(_data, "Remove Arena Wall Shape");
            arena.overrideWall = false;
            MarkDirty();
            RebuildArenaWallMeshes();
            return true;
        }

        // The wall is the boundary, so the arena's own radius follows it.
        float wallRadius = _data.ArenaWallFor(arena).radius;
        if (Mathf.Abs(arena.arenaRadius - wallRadius) > 0.0001f)
        {
            arena.arenaRadius = wallRadius;
            SyncEntranceNodes(arena);
        }
        return false;
    }

    /// <summary>
    /// The overlap each entrance's own river takes. How far in a river wants to go varies from
    /// arena to arena, so any entrance can be given its own number here; the ones left alone
    /// follow River Overlap above and move with it.
    /// </summary>
    private void DrawEntranceOverlaps()
    {
        EditorGUI.indentLevel++;

        bool changed = false;
        foreach (var arena in _data.arenas)
        {
            if (arena == null || string.IsNullOrEmpty(arena.nodeId)) continue;

            EditorGUILayout.LabelField(ArenaLabel(arena), EditorStyles.miniLabel);
            EditorGUI.indentLevel++;
            changed |= DrawArenaEntranceOverlaps(arena);
            EditorGUI.indentLevel--;
        }

        EditorGUI.indentLevel--;
        if (changed) RebuildAfterEdit("RunMeshes", () => RebuildRunMeshes());
    }

    /// One arena's entrance overlap rows. Returns true when the runs need rebuilding.
    private bool DrawArenaEntranceOverlaps(LevelSelectDesignerData.DesignerArena arena)
    {
        bool changed = DrawOneEntranceOverlap(
            $"Entrance {arena.entranceIndex + 1}",
            ref arena.overrideRunOverlap, ref arena.runOverlap);

        foreach (var entrance in arena.secondaryEntrances)
        {
            if (entrance == null) continue;
            changed |= DrawOneEntranceOverlap(
                $"Entrance {entrance.entranceIndex + 1}",
                ref entrance.overrideRunOverlap, ref entrance.runOverlap);
        }
        return changed;
    }

    /// <summary>
    /// One entrance's row: the tick that takes it off River Overlap, and the number it uses
    /// once it is. Unticked it still shows the number it is following, greyed, so the whole
    /// list can be read at a glance. Returns true when the runs need rebuilding.
    /// </summary>
    private bool DrawOneEntranceOverlap(string label, ref bool over, ref float value)
    {
        bool changed = false;

        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        bool nowOver = EditorGUILayout.ToggleLeft(label, over, GUILayout.Width(160f));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Override Entrance Overlap");
            // Opens on the number it was already following, so ticking it moves nothing by itself.
            if (nowOver && !over) value = _data.arenaRunOverlap;
            over    = nowOver;
            changed = true;
            MarkDirty();
        }

        using (new EditorGUI.DisabledScope(!over))
        {
            EditorGUI.BeginChangeCheck();
            float shown = EditorGUILayout.FloatField(over ? value : _data.arenaRunOverlap);
            if (EditorGUI.EndChangeCheck() && over)
            {
                Undo.RecordObject(_data, "Edit Entrance Overlap");
                value   = Mathf.Max(0f, shown);
                changed = true;
                MarkDirty();
            }
        }

        EditorGUILayout.EndHorizontal();
        return changed;
    }

    /// <summary>
    /// Entrance archways - the arch standing over a river where it arrives at an arena, with a
    /// toggle per arena for whether its entrances get one at all.
    ///
    /// Width and thickness are inherited from the river arriving and depth from the arena wall,
    /// so the default shape only has to say how tall the arch stands. That is why the default
    /// is usually the only one anybody edits.
    /// </summary>
    private void DrawProcGenEntrancesSection()
    {
        EditorGUILayout.HelpBox(
            "An archway is the arriving river's own section stood up and carried over the " +
            "water: its legs are that run's rims, its opening is the channel. It stands on the " +
            "arena wall and runs out through the wall's thickness.",
            MessageType.None);

        DrawArchway("Default (all archways)", _data.defaultArchway);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Arenas with archways", EditorStyles.boldLabel);

        if (_data.arenas.Count == 0)
            EditorGUILayout.LabelField("  (no arenas)", EditorStyles.miniLabel);

        foreach (var arena in _data.arenas)
        {
            if (arena == null || string.IsNullOrEmpty(arena.nodeId)) continue;
            if (DrawOneArenaArchway(arena, ArenaLabel(arena), showDefault: false)) break;
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(
            "Turning an archway on or off adds or removes geometry, so it needs a full Generate.",
            EditorStyles.miniLabel);
        if (GUILayout.Button("Rebuild Archways"))
        {
            int n = RebuildArchwayMeshes();
            AssetDatabase.SaveAssets();
            _consoleStatusMsg = $"Rebuilt {n} archway(s).";
            Debug.Log($"[LevelSelectDesigner] Rebuilt {n} archway(s) from the current shapes.");
        }
    }

    /// <summary>
    /// One arena's archway row: the tick for whether its entrances get one, the offer to
    /// override the default shape, and its own shape once it has one. Drawn in Setup's
    /// Entrances list and under the selected arena in the left panel, where
    /// <paramref name="showDefault"/> also shows the shape it is following, greyed.
    /// Returns true when it went back to the default, so a caller looping over arenas can stop.
    /// </summary>
    private bool DrawOneArenaArchway(LevelSelectDesignerData.DesignerArena arena, string label,
                                     bool showDefault)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.ToggleLeft(label, arena.archwayOnEntrances);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Toggle Arena Archways");
            arena.archwayOnEntrances = on;
            MarkDirty();
        }

        using (new EditorGUI.DisabledScope(!arena.archwayOnEntrances))
        {
            if (!arena.overrideArchway)
            {
                if (GUILayout.Button("Override shape", EditorStyles.miniButton, GUILayout.Width(110)))
                {
                    Undo.RecordObject(_data, "Add Archway Shape");
                    arena.archwayProfile  = _data.ArchwayFor(arena).Clone();
                    arena.overrideArchway = true;
                    MarkDirty();
                }
            }
            else
            {
                GUILayout.Label("own shape", EditorStyles.miniLabel, GUILayout.Width(110));
            }
        }
        EditorGUILayout.EndHorizontal();

        if (!arena.archwayOnEntrances) return false;

        if (!arena.overrideArchway)
        {
            if (showDefault)
                using (new EditorGUI.DisabledScope(true))
                    DrawArchway("Archway: default", _data.ArchwayFor(arena));
            return false;
        }

        EditorGUI.indentLevel++;
        bool removed = DrawArchway(ArenaLabel(arena), arena.archwayProfile, removable: true);
        if (removed)
        {
            Undo.RecordObject(_data, "Remove Archway Shape");
            arena.overrideArchway = false;
            MarkDirty();
            RebuildArchwayMeshes();
        }
        EditorGUI.indentLevel--;
        return removed;
    }

    private void DrawArenaEntranceAlignments(LevelSelectDesignerData.DesignerArena arena)
    {
        DrawOneEntranceAlignment($"Entrance {arena.entranceIndex + 1}", arena, arena.entranceIndex,
                                 arena.entranceAlignment);

        foreach (var entrance in arena.secondaryEntrances)
        {
            if (entrance == null) continue;
            DrawOneEntranceAlignment($"Entrance {entrance.entranceIndex + 1}", arena,
                                     entrance.entranceIndex, entrance.alignment);
        }
    }

    /// <summary>
    /// One door's depth. Every entrance carries its own; there is no default to follow. The
    /// slider runs from the front face to the back face of the arch at that door.
    /// </summary>
    private void DrawOneEntranceAlignment(string label,
                                          LevelSelectDesignerData.DesignerArena arena,
                                          int entranceIndex, ArenaEntranceAlignment value)
    {
        if (value == null) return;

        float depth = ArchDepthAt(arena, entranceIndex);

        EditorGUI.BeginChangeCheck();
        float along = EditorGUILayout.Slider(
            new GUIContent($"{label}: Along", "How far into the archway the door sits. 0 is the " +
                                              "front face on the arena side; the far end is the " +
                                              "back face."),
            Mathf.Clamp(value.along, 0f, depth), 0f, depth);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Door Depth");
            value.along = along;
            MarkDirty();
        }
    }

    /// <summary>The depth of the arch standing at a door, with every inherited number settled.</summary>
    private float ArchDepthAt(LevelSelectDesignerData.DesignerArena arena, int entranceIndex)
    {
        if (arena == null) return 1f;

        var wall = _data.ArenaWallFor(arena);
        var site = ArenaEntranceSites(arena).FirstOrDefault(x => x.entranceIndex == entranceIndex);
        var arch = _data.ArchwayFor(arena).Resolve(_data.ProfileFor(site.riverName), wall.thickness);
        return Mathf.Max(0.01f, arch.depth);
    }

    /// <summary>
    /// Where the door sits along an arch — the entrance's Along, kept between the arch's front
    /// and back faces. The door sheet and the entrance prefab both read it from here, so they
    /// can never disagree.
    /// </summary>
    private float DoorAlong(LevelSelectDesignerData.DesignerArena arena, string entranceNodeId,
                            int entranceIndex)
    {
        float along = _data.EntranceAlignmentFor(arena, entranceNodeId).along;
        return Mathf.Clamp(along, 0f, ArchDepthAt(arena, entranceIndex));
    }

    // Returns true when the designer asked to drop this arena back to the default shape.
    private bool DrawArchway(string header, ArenaArchwayProfile profile, bool removable = false)
    {
        bool remove = false;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        if (removable && GUILayout.Button("Use default", EditorStyles.miniButton, GUILayout.Width(90)))
            remove = true;
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        float legHeight  = EditorGUILayout.FloatField(
            new GUIContent("Leg Height", "The straight part, up from the rim top."), profile.legHeight);
        float archHeight = EditorGUILayout.FloatField(
            new GUIContent("Arch Height", "The curved part above the legs. Half the opening width " +
                                          "gives a plain semicircle; more gives a taller arch."),
            profile.archHeight);
        float opening    = EditorGUILayout.FloatField(
            new GUIContent("Opening Width", "0 takes the arriving river's channel width, so the " +
                                            "arch frames the water exactly."), profile.openingWidth);
        float thickness  = EditorGUILayout.FloatField(
            new GUIContent("Thickness", "0 takes the arriving river's rim width, so the legs stand " +
                                        "on its rims."), profile.thickness);
        float depth      = EditorGUILayout.FloatField(
            new GUIContent("Depth", "How far it runs along the river. 0 takes the arena wall's " +
                                    "thickness, so the arch is the way through the wall."), profile.depth);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Archway Shape");
            profile.legHeight    = Mathf.Max(0f,     legHeight);
            profile.archHeight   = Mathf.Max(0.001f, archHeight);
            profile.openingWidth = Mathf.Max(0f,     opening);
            profile.thickness    = Mathf.Max(0f,     thickness);
            profile.depth        = Mathf.Max(0f,     depth);
            MarkDirty();
        }

        var inherited = new List<string>();
        if (profile.openingWidth <= 0.0001f) inherited.Add("opening");
        if (profile.thickness    <= 0.0001f) inherited.Add("thickness");
        if (profile.depth        <= 0.0001f) inherited.Add("depth");
        EditorGUILayout.LabelField(
            inherited.Count > 0
                ? $"Taking {string.Join(", ", inherited)} from the river and wall"
                : "Every number set here - nothing inherited",
            EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        return remove;
    }

    /// <summary>
    /// Where the door presets live. A folder of their own, as the outpost and tower presets
    /// have, so the picker is not wading through every preset in the project.
    /// </summary>
    private const string DoorPresetFolder = "Assets/ScriptsData/DataScripts/ArenaDoorPresets";

    /// <summary>
    /// The default door is the first preset in <see cref="DoorPresetFolder"/>, by name — it is
    /// no longer edited on its own. Called before doors are built, so saving over that preset
    /// is what changes every door following the default. With no preset there, the default
    /// keeps the shape it already had.
    /// </summary>
    private void FollowFirstDoorPreset()
    {
        if (_data == null) return;

        var first = LevelSelectRiverPresetLibrary.All<ArenaDoorPreset>(DoorPresetFolder).FirstOrDefault();
        if (first == null) return;

        var shape = first.ToProfile();
        if (_data.defaultDoorPreset == first && _data.defaultDoor != null &&
            _data.defaultDoor.ShapeKey == shape.ShapeKey) return;

        _data.defaultDoorPreset = first;
        if (_data.defaultDoor == null) _data.defaultDoor = new ArenaDoorProfile();
        _data.defaultDoor.CopyFrom(shape);
        EditorUtility.SetDirty(_data);
    }

    /// <summary>
    /// One arena's door: the offer to override the default shape, or its own shape and preset
    /// row once it has one. Drawn in Setup's door list and under the selected arena in the left
    /// panel, where <paramref name="showDefault"/> also shows the shape it is following, greyed.
    /// Returns true when it went back to the default, so a caller looping over arenas can stop.
    /// </summary>
    private bool DrawOneArenaDoor(LevelSelectDesignerData.DesignerArena arena, bool showDefault)
    {
        // The number leads, because it is what is about to be carved on this arena's door
        // and there is nowhere else in the designer to read it off.
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"{ArenaNumber(arena)}.  {ArenaLabel(arena)}",
                                   EditorStyles.miniLabel);
        if (!arena.overrideDoor)
        {
            if (GUILayout.Button("Override shape", EditorStyles.miniButton, GUILayout.Width(110)))
            {
                Undo.RecordObject(_data, "Add Door Shape");
                arena.doorProfile  = _data.DoorFor(arena).Clone();
                arena.overrideDoor = true;
                MarkDirty();
            }
        }
        else
        {
            GUILayout.Label("own shape", EditorStyles.miniLabel, GUILayout.Width(110));
        }
        EditorGUILayout.EndHorizontal();

        if (!arena.overrideDoor)
        {
            if (showDefault)
            {
                FollowFirstDoorPreset();
                string from = _data.defaultDoorPreset != null ? _data.defaultDoorPreset.name : "default";
                using (new EditorGUI.DisabledScope(true))
                    DrawDoor($"Door: {from} (first preset)", _data.DoorFor(arena));
            }
            return false;
        }

        EditorGUI.indentLevel++;
        DrawDoorPresetRow(ref arena.doorPreset, arena.doorProfile);
        bool removed = DrawDoor(ArenaLabel(arena), arena.doorProfile, removable: true);
        if (removed)
        {
            Undo.RecordObject(_data, "Remove Door Shape");
            arena.overrideDoor = false;
            MarkDirty();
            RebuildArchwayMeshes();
        }
        EditorGUI.indentLevel--;
        return removed;
    }

    /// <summary>
    /// Pick a door preset, save over it, or save a new one — the same row the outposts and the
    /// lollipop towers have. The shape is copied into the profile that is already there rather
    /// than swapped for the preset's own, so editing it afterwards never touches the asset.
    /// </summary>
    private void DrawDoorPresetRow(ref ArenaDoorPreset preset, ArenaDoorProfile profile)
    {
        if (profile == null) return;

        var picked = LevelSelectRiverPresetLibrary.DrawPicker(
            new GUIContent("Preset", "The door presets kept in " + DoorPresetFolder +
                                     ". Picking one puts its shape on this door."),
            preset, DoorPresetFolder);

        if (picked != preset)
        {
            Undo.RecordObject(_data, "Load Door Preset");
            preset = picked;
            if (picked != null) profile.CopyFrom(picked.ToProfile());
            MarkDirty();
            RebuildArchwayMeshes();
            GUI.FocusControl(null);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(preset == null))
            {
                if (GUILayout.Button(new GUIContent("Save", "Overwrite the preset with this door's " +
                                                            "shape, and rebuild the doors so you " +
                                                            "can see it.")) &&
                    EditorUtility.DisplayDialog("Save Door Preset",
                        $"Overwrite '{preset.name}' with this door's shape?", "Save", "Cancel"))
                {
                    Undo.RecordObject(preset, "Save Door Preset");
                    preset.CopyFrom(profile);
                    EditorUtility.SetDirty(preset);
                    AssetDatabase.SaveAssetIfDirty(preset);
                    RebuildDoorsFromShapes();
                }
            }

            if (GUILayout.Button(new GUIContent("Save as New", "Make a new preset from this door's " +
                                                                "shape, and rebuild the doors so " +
                                                                "you can see it.")))
            {
                string assetPath = EditorUtility.SaveFilePanelInProject(
                    "Save Door Preset", "NewArenaDoorPreset", "asset", "Choose save location",
                    LevelSelectRiverPresetLibrary.EnsureFolder(DoorPresetFolder));
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var made = CreateInstance<ArenaDoorPreset>();
                    made.CopyFrom(profile);
                    AssetDatabase.CreateAsset(made, assetPath);
                    AssetDatabase.SaveAssets();

                    Undo.RecordObject(_data, "Save Door Preset");
                    preset = made;
                    MarkDirty();
                    RebuildDoorsFromShapes();
                }
                GUIUtility.ExitGUI();
            }
        }
    }

    /// <summary>
    /// Rebuilds every door in the open scene against the shapes as they stand now, and writes
    /// the meshes to disk.
    ///
    /// This is what Save is for as much as the preset is: the sliders above only mark the data
    /// dirty, so saving is the moment you get to see what you have been typing. It goes through
    /// <see cref="RebuildArchwayMeshes"/>, which rebuilds each archway and its door together,
    /// because a door is built in its arch's frame and against its resolved shape.
    /// </summary>
    private void RebuildDoorsFromShapes()
    {
        int n = RebuildArchwayMeshes();
        AssetDatabase.SaveAssets();
        _consoleStatusMsg = $"Saved, and rebuilt {n} door(s).";
        Debug.Log($"[LevelSelectDesigner] Rebuilt {n} archway(s) and door(s) from the current shapes.");
    }

    // Returns true when the designer asked to drop this arena back to the default shape.
    private bool DrawDoor(string header, ArenaDoorProfile profile, bool removable = false)
    {
        bool remove = false;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        if (removable && GUILayout.Button("Use default", EditorStyles.miniButton, GUILayout.Width(90)))
            remove = true;
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("The keyhole", EditorStyles.miniBoldLabel);
        float height      = EditorGUILayout.FloatField(
            new GUIContent("Height", "Top of the bulb, up from the water. 0 takes the arch " +
                                     "opening's own height, so the door fills it."), profile.height);
        float baseWidth   = EditorGUILayout.FloatField(
            new GUIContent("Base Width", "Width across the foot. 0 takes the arch's opening " +
                                         "width, so the door spans it."), profile.baseWidth);
        float bulbRadius  = EditorGUILayout.FloatField(
            new GUIContent("Bulb Radius", "The round bulb at the top."), profile.bulbRadius);
        float waistWidth  = EditorGUILayout.FloatField(
            new GUIContent("Waist Width", "Width across the narrowest part, under the bulb."),
            profile.waistWidth);
        float waistHeight = EditorGUILayout.FloatField(
            new GUIContent("Waist Height", "How far up the waist sits. Below it the sides run " +
                                           "straight out to the foot; above it they curve into " +
                                           "the bulb."), profile.waistHeight);

        float baseCurve   = EditorGUILayout.FloatField(
            new GUIContent("Base Curve", "How far the bottom edge sags below the two base " +
                                         "corners, which sit on the water. 0 cuts the door off " +
                                         "flat at the water; more closes it underneath with a " +
                                         "curve and the soul opening sits inside it."),
            profile.baseCurve);

        EditorGUILayout.LabelField("Rims", EditorStyles.miniBoldLabel);
        float frameWidth   = EditorGUILayout.FloatField(
            new GUIContent("Frame Width", "Width of the band running round the keyhole."),
            profile.frameWidth);
        float frameDepth   = EditorGUILayout.FloatField(
            new GUIContent("Stand Proud", "How far both rims stand off the sheet, toward the river."),
            profile.frameDepth);
        float flapRimWidth = EditorGUILayout.FloatField(
            new GUIContent("Flap Rim Width", "Width of the band running round the soul opening."),
            profile.flapRimWidth);
        float panelDepth   = EditorGUILayout.FloatField(
            new GUIContent("Panel Stand Proud", "How far the panel inside the frame stands off " +
                                                "the sheet — the leaf of the door, with the soul " +
                                                "opening left out of it. Less than Stand Proud " +
                                                "sits it in the frame; 0 builds no panel."),
            profile.panelDepth);

        EditorGUILayout.LabelField("The soul opening", EditorStyles.miniBoldLabel);
        float flapWidth = EditorGUILayout.FloatField(
            new GUIContent("Flap Width", "Width of the opening cut clean through the sheet."),
            profile.flapWidth);
        float flapLeg   = EditorGUILayout.FloatField(
            new GUIContent("Flap Leg Height", "The straight part of its sides, up from the water."),
            profile.flapLegHeight);
        float flapCrown = EditorGUILayout.FloatField(
            new GUIContent("Flap Arch Height", "The curved part above them. Half the flap width " +
                                               "gives a plain semicircle; more gives a taller " +
                                               "opening."), profile.flapCrownHeight);
        float flapCurve = EditorGUILayout.FloatField(
            new GUIContent("Flap Curve", "How far the opening's bottom edge sags below the " +
                                         "water, the way the door's own Base Curve does. 0 " +
                                         "stands it flat on the water."), profile.flapCurve);

        EditorGUILayout.LabelField("The numeral disc", EditorStyles.miniBoldLabel);
        float discRadius  = EditorGUILayout.FloatField(
            new GUIContent("Disc Radius", "The round disc on the bulb carrying this arena's " +
                                          "number. 0 leaves the bulb plain and draws the numeral " +
                                          "straight on it, spanning the bulb and lifted off the " +
                                          "panel."),
            profile.discRadius);
        float discDepth   = EditorGUILayout.FloatField(
            new GUIContent("Disc Stand Proud", "How far the disc stands off the sheet. Its own, " +
                                               "so it can sit shallower than the frame round it."),
            profile.discDepth);
        float discRise    = EditorGUILayout.FloatField(
            new GUIContent("Disc Rise", "How far the disc sits above the middle of the bulb. 0 " +
                                        "centres it there; negative drops it toward the waist."),
            profile.discRise);
        int discSideSteps = EditorGUILayout.IntField(
            new GUIContent("Disc Side Steps", "How many rings the disc's side is built in, from " +
                                              "the sheet out to its face. 1 takes it in a single " +
                                              "step; more breaks the wall up without changing " +
                                              "its shape."),
            profile.discSideSteps);
        float numeralSize = EditorGUILayout.FloatField(
            new GUIContent("Numeral Size", "How much of the disc's width the drawing spans — " +
                                           "or the bulb's, with no disc. 1 fills it; 0.7 leaves " +
                                           "a margin of stone. Over 1 spills it over the edge."),
            profile.numeralSize);
        float numeralRise = EditorGUILayout.FloatField(
            new GUIContent("Numeral Rise", "How far the numeral sits above the middle of the " +
                                           "disc. 0 centres it, negative drops it. Separate " +
                                           "from Disc Rise, so the drawing moves on the disc " +
                                           "without the disc moving on the door."),
            profile.numeralRise);
        float numeralLift = EditorGUILayout.FloatField(
            new GUIContent("Numeral Lift", "How far the drawing floats off the disc's face. " +
                                           "Only enough to settle which is in front."),
            profile.numeralLift);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Door Shape");
            profile.discRadius      = Mathf.Max(0f,     discRadius);
            profile.discDepth       = Mathf.Max(0f,     discDepth);
            profile.discRise        = discRise;
            profile.discSideSteps   = Mathf.Max(1,      discSideSteps);
            profile.numeralSize     = Mathf.Max(0f,     numeralSize);
            profile.numeralRise     = numeralRise;
            profile.numeralLift     = Mathf.Max(0f,     numeralLift);
            profile.height          = Mathf.Max(0f,     height);
            profile.baseWidth       = Mathf.Max(0f,     baseWidth);
            profile.bulbRadius      = Mathf.Max(0.001f, bulbRadius);
            profile.waistWidth      = Mathf.Max(0.001f, waistWidth);
            profile.waistHeight     = Mathf.Max(0f,     waistHeight);
            profile.baseCurve       = Mathf.Max(0f,     baseCurve);
            profile.frameWidth      = Mathf.Max(0.001f, frameWidth);
            profile.frameDepth      = Mathf.Max(0f,     frameDepth);
            profile.flapRimWidth    = Mathf.Max(0.001f, flapRimWidth);
            profile.panelDepth      = Mathf.Max(0f,     panelDepth);
            profile.flapWidth       = Mathf.Max(0.001f, flapWidth);
            profile.flapLegHeight   = Mathf.Max(0f,     flapLeg);
            profile.flapCrownHeight = Mathf.Max(0.001f, flapCrown);
            profile.flapCurve       = Mathf.Max(0f,     flapCurve);
            MarkDirty();
        }

        var inherited = new List<string>();
        if (profile.height    <= 0.0001f) inherited.Add("height");
        if (profile.baseWidth <= 0.0001f) inherited.Add("base width");
        EditorGUILayout.LabelField(
            inherited.Count > 0
                ? $"Taking {string.Join(", ", inherited)} from the arch it stands in"
                : "Every number set here - nothing inherited",
            EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        return remove;
    }

    // Returns true when the designer asked to drop this arena back to the default shape.
    private bool DrawArenaWall(string header, ArenaWallProfile profile, bool removable = false)
    {
        bool remove = false;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        if (removable && GUILayout.Button("Use default", EditorStyles.miniButton, GUILayout.Width(90)))
            remove = true;
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        float radius    = EditorGUILayout.FloatField(
            new GUIContent("Radius", "Arena boundary - the wall's inner face. 0 takes the radius " +
                                     "the arena already carries."), profile.radius);
        float thickness = EditorGUILayout.FloatField(
            new GUIContent("Thickness", "Laid off outward from the radius, so the play area is " +
                                        "never eaten into."), profile.thickness);
        float height    = EditorGUILayout.FloatField(
            new GUIContent("Height", "How far the wall stands above the water surface."), profile.height);
        // No Drop here — how far the wall carries on below is the one Drop, above.

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Arena Wall Shape");
            profile.radius    = Mathf.Max(0f,    radius);
            profile.thickness = Mathf.Max(0.01f, thickness);
            profile.height    = Mathf.Max(0f,    height);
            MarkDirty();
        }

        EditorGUILayout.LabelField(
            profile.radius > 0.05f
                ? $"Outer radius {profile.OuterRadius:F2}"
                : "Radius 0 - each arena uses the radius it already carries",
            EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        return remove;
    }

    /// <summary>
    /// One river's Run Shape: its own override if it has one, otherwise an offer to make one.
    /// Drawn in Setup's River Runs list and under the selected path in the left panel.
    /// Returns true when the list of overrides changed, so a caller looping over it can stop.
    /// </summary>
    private bool DrawRiverShapeOverride(string riverName)
    {
        var profile = _data.riverProfiles.Find(p => p != null && p.riverName == riverName);
        if (profile == null)
        {
            bool added = false;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(riverName, EditorStyles.miniLabel);
            if (GUILayout.Button("Override shape", EditorStyles.miniButton, GUILayout.Width(110)))
            {
                Undo.RecordObject(_data, "Add River Shape");
                var copy = _data.ProfileFor(null).Clone();
                copy.riverName = riverName;
                _data.riverProfiles.Add(copy);
                MarkDirty();
                added = true;
            }
            EditorGUILayout.EndHorizontal();
            return added;
        }

        if (DrawRiverProfile(riverName, profile, true))
        {
            Undo.RecordObject(_data, "Remove River Shape");
            _data.riverProfiles.Remove(profile);
            MarkDirty();
            RebuildRunMeshes(riverName);
            return true;
        }
        return false;
    }

    // Returns true when the designer asked to drop this river back to the default shape.
    private bool DrawRiverProfile(string header, RiverProfile profile, bool removable)
    {
        if (profile == null) return false;

        bool remove = false;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        if (removable && GUILayout.Button("Use default", EditorStyles.miniButton, GUILayout.Width(90)))
            remove = true;
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        profile.innerWidth = Mathf.Max(0.001f, EditorGUILayout.FloatField("Inner Width", profile.innerWidth));
        profile.rimWidth   = Mathf.Max(0f,     EditorGUILayout.FloatField("Rim Width",   profile.rimWidth));
        profile.riverDepth = Mathf.Max(0.001f, EditorGUILayout.FloatField("River Depth", profile.riverDepth));
        // No Depth here — how far the run reaches down is the one Drop, above.
        if (EditorGUI.EndChangeCheck())
        {
            MarkDirty();
            // Live: the runs already in the scene take the new shape straight away.
            // A change to the default reaches every river, so nothing is filtered out.
            string riverName = profile.riverName;
            RebuildAfterEdit("RunMeshes:" + riverName, () => RebuildRunMeshes(riverName));
        }

        EditorGUILayout.LabelField("Outer Width", $"{profile.OuterWidth:F3}  (inner + 2 x rim)", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        return remove;
    }

    // The same numbers DrawRiverProfile shows, greyed out — for a river that takes the default,
    // so its shape is still readable without overriding it. Edit the default under Setup.
    private void DrawRiverProfileReadOnly(string header, RiverProfile profile)
    {
        if (profile == null) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField("Inner Width", profile.innerWidth);
            EditorGUILayout.FloatField("Rim Width",   profile.rimWidth);
            EditorGUILayout.FloatField("River Depth", profile.riverDepth);
        }
        EditorGUILayout.LabelField("Outer Width", $"{profile.OuterWidth:F3}  (inner + 2 x rim)", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawArenaPrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.arenaPrefab         = (GameObject)EditorGUILayout.ObjectField("Arena Head",     _data.arenaPrefab,         typeof(GameObject), false);
        _data.arenaEntrancePrefab = (GameObject)EditorGUILayout.ObjectField("Arena Entrance", _data.arenaEntrancePrefab, typeof(GameObject), false);
        _data.arenaWallMaterial   = (Material)EditorGUILayout.ObjectField("Wall Material",    _data.arenaWallMaterial,   typeof(Material), false);
        _data.arenaDoorMaterial   = (Material)EditorGUILayout.ObjectField("Door Material",    _data.arenaDoorMaterial,   typeof(Material), false);
        _data.arenaNumeralMaterial = (Material)EditorGUILayout.ObjectField(
            new GUIContent("Numeral Material", "What every arena's numeral is shown on. Each " +
                           "numeral gets its own variant of it carrying that drawing."),
            _data.arenaNumeralMaterial, typeof(Material), false);
        if (EditorGUI.EndChangeCheck()) MarkDirty();

        EditorGUILayout.LabelField("Wall shape is authored under Procedural Generation.",
                                   EditorStyles.miniLabel);
    }

    private void DrawObstacleShopPrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.obstaclePrefab = (GameObject)EditorGUILayout.ObjectField("Obstacle", _data.obstaclePrefab, typeof(GameObject), false);
        _data.shopPrefab     = (GameObject)EditorGUILayout.ObjectField("Shop",     _data.shopPrefab,     typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    /// <summary>
    /// One map for the whole world, the same pair GridData carries inside a level: a switch and a
    /// map, with nothing behind them. Fog on with no map is a world that gets no fog, and says so
    /// rather than quietly scattering something — an unauthored fog looks like a working map.
    /// </summary>
    private void DrawFogSection()
    {
        EditorGUI.BeginChangeCheck();
        bool on     = EditorGUILayout.Toggle("Fog Enabled", _data.fogEnabled);
        var  newMap = (FogMap)EditorGUILayout.ObjectField("Fog Map", _data.fogMap, typeof(FogMap), false);
        float height = EditorGUILayout.FloatField(
            new GUIContent("Height Offset", "How far the fog sheet hovers above the landscape surface. " +
                                            "Single mode's sheet, and the height a new fog zone starts at."),
            _data.fogHeightOffset);
        var planeMode = (FogPlaneMode)EditorGUILayout.EnumPopup(
            new GUIContent("Fog Plane Mode",
                           "Single: one sheet over the whole landscape. Zones: a sheet per fog zone " +
                           "(Fog Zones mode), each at its own height, shown while the boat is in its " +
                           "bounds. The fog itself is generated once either way."),
            _data.fogPlaneMode);
        float fadeTime = _data.fogZoneFadeTime;
        if (planeMode == FogPlaneMode.Zones)
            fadeTime = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Zone Fade Time", "Seconds a fog zone's sheet takes to fade in when the " +
                                                 "boat enters its bounds, and out when it leaves."),
                _data.fogZoneFadeTime));
        if (EditorGUI.EndChangeCheck())
        {
            bool zonesChanged = planeMode != _data.fogPlaneMode || !Mathf.Approximately(fadeTime, _data.fogZoneFadeTime)
                             || newMap != _data.fogMap;   // the map carries the zone sheets' shape

            Undo.RecordObject(_data, "Set Fog");
            _data.fogEnabled      = on;
            _data.fogMap          = newMap;
            _data.fogHeightOffset = height;
            _data.fogPlaneMode    = planeMode;
            _data.fogZoneFadeTime = fadeTime;
            MarkDirty();

            // Live: the rig already in the scene takes the new numbers (after the Rebuild Delay
            // for a value field), the same way a river run rebuilds when its profile is edited.
            RebuildAfterEdit("Fog", () =>
            {
                if (zonesChanged) RebuildFogZones();
                if (_data.fogFieldManager != null) DeployFogField();
            });
        }

        if (_data.fogEnabled && _data.fogMap == null)
        {
            EditorGUILayout.HelpBox(
                "Fog is on but no fog map is assigned, so this world gets no fog. There is no " +
                "fallback — the map is the only thing that decides where fog sits.",
                MessageType.Warning);
        }
        else if (_data.fogMap != null)
        {
            var m  = _data.fogMap;
            var ws = m.WorldBlobScale;
            EditorGUILayout.LabelField(
                $"{m.blobCount} masses   {m.properties.EffectiveLimbCount} limbs" +
                (_data.fogEnabled ? "" : "   (fog off — map ignored)"),
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"Masses {ws.x:0.##}–{ws.y:0.##} u   ·   mask {m.maskRadius:0.##} u",
                EditorStyles.miniLabel);
        }

        // The two numbers the exterior decides for itself, rather than reading off an arena.
        EditorGUILayout.LabelField(
            $"Sheet {_data.LandscapeSpan:0.#} u square, at Y {_data.LandscapeCentre.y + _data.fogHeightOffset:0.###}",
            EditorStyles.miniLabel);

        if (_data.fogFieldManager == null)
            EditorGUILayout.HelpBox(
                "No fog field in the scene. Deploy it from Script Objects > Fog Field.",
                MessageType.None);

        if (GUILayout.Button(_data.fogMap != null ? "Edit in Fog Map" : "Open Fog Map…"))
            EditorWindow.GetWindow<FogMapWindow>("Fog Map").Show();
    }

    /// <summary>
    /// How the world looks, as opposed to what is generated in it. Every number here is pushed
    /// to the shaders as a global, so it lands on every hill and every river run at once — there
    /// is no per-piece override behind it, and nothing else writes these uniforms.
    /// </summary>
    private void DrawAestheticsSection()
    {
        EditorGUI.BeginChangeCheck();
        float whiteGradientPos = EditorGUILayout.FloatField(
            new GUIContent("White Gradient Pos",
                           "Where the white gradient sits on the landscape hills and the river " +
                           "runs — the _WhiteGradientPos global."),
            _data.whiteGradientPos);
        float distanceFadeRadius = EditorGUILayout.FloatField(
            new GUIContent("Distance Fade Radius",
                           "How far from the camera the river runs fade out — the run shader's " +
                           "_DistanceFadeRadius. Exposed on the shader, so this is written onto " +
                           "the Run Material as well as pushed as a global."),
            _data.distanceFadeRadius);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Set Aesthetics");
            _data.whiteGradientPos    = whiteGradientPos;
            _data.distanceFadeRadius  = distanceFadeRadius;
            MarkDirty();

            // Straight into the shaders so the scene view moves with the field. The pump keeps
            // it there afterwards; this is only what makes the drag feel live.
            _data.ApplyAesthetics();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();

        // The river's own look. Presets rather than fields here because the numbers are tuned by
        // eye against a running scene, which is what the tuner windows are for — this end only
        // says which set of them this world wears.
        //
        // TWO of them, because the water and the stone are two materials on two pieces of
        // geometry with a tuner each. Both are picked from the presets folder by name, with an
        // object field beside each for one held from anywhere else — a stray preset is then
        // visible in the dropdown rather than silently in effect, which is how half a world's
        // look went missing from a build while the editor showed it.
        EditorGUILayout.LabelField("River Look", EditorStyles.boldLabel);

        var pickedWater = LevelSelectRiverPresetLibrary.DrawPicker(
            new GUIContent("Water Preset",
                           "How this world's WATER looks — the ripple lines along the rivers' " +
                           "banks and in rings out of a pool's middle. Authored in the Level " +
                           "Select River Tuner. None means no ripples: these are bare globals, " +
                           "with nothing behind them to fall back on."),
            _data.waterPreset);

        var waterPreset = (LevelSelectRiverWaterPreset)EditorGUILayout.ObjectField(
            pickedWater, typeof(LevelSelectRiverWaterPreset), false);

        if (waterPreset != _data.waterPreset)
        {
            Undo.RecordObject(_data, "Set River Water Preset");
            _data.waterPreset = waterPreset;
            MarkDirty();

            _data.ApplyAesthetics();
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Open River Tuner"))
            LevelSelectRiverTuner.Open();

        EditorGUILayout.Space();

        var pickedStructure = LevelSelectRiverPresetLibrary.DrawPicker(
            new GUIContent("Structure Preset",
                           "How this world's STRUCTURES look — the generated stone the rivers " +
                           "run through: the colour of each part of a run, its grain, the dark " +
                           "along its seams and the white off the waterline. Authored in the " +
                           "Level Select Run Shading Tuner. None means bare unlit stone, for " +
                           "the same reason as above."),
            _data.structurePreset);

        var structurePreset = (LevelSelectRiverStructurePreset)EditorGUILayout.ObjectField(
            pickedStructure, typeof(LevelSelectRiverStructurePreset), false);

        if (structurePreset != _data.structurePreset)
        {
            Undo.RecordObject(_data, "Set River Structure Preset");
            _data.structurePreset = structurePreset;
            MarkDirty();

            _data.ApplyAesthetics();
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Open Run Shading Tuner"))
            LevelSelectRunShadingTuner.Open();

        EditorGUILayout.Space();

        var pickedLandscape = LevelSelectRiverPresetLibrary.DrawPicker(
            new GUIContent("Landscape Preset",
                           "How this world's LANDSCAPE hills look — three stone variants and " +
                           "which part (ground, tops, cliffs, holes) wears each. Authored in the " +
                           "Level Select Landscape Tuner. None means plain unlit stone."),
            _data.landscapePreset);

        var landscapePreset = (LevelSelectLandscapePreset)EditorGUILayout.ObjectField(
            pickedLandscape, typeof(LevelSelectLandscapePreset), false);

        if (landscapePreset != _data.landscapePreset)
        {
            Undo.RecordObject(_data, "Set Landscape Preset");
            _data.landscapePreset = landscapePreset;
            MarkDirty();

            _data.ApplyAesthetics();
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Open Landscape Tuner"))
            LevelSelectLandscapeTuner.Open();

        EditorGUILayout.Space();

        var pickedWhiteFade = LevelSelectRiverPresetLibrary.DrawPicker(
            new GUIContent("White Fade Preset",
                           "The white fade out from the boat, shared by the river runs, the " +
                           "landscape and the spikes through the WhiteFade subgraph. Tuned in " +
                           "the White Fade foldout of the Run Shading Tuner or the Landscape " +
                           "Tuner. None means no fade."),
            _data.whiteFadePreset);

        var whiteFadePreset = (LevelSelectWhiteFadePreset)EditorGUILayout.ObjectField(
            pickedWhiteFade, typeof(LevelSelectWhiteFadePreset), false);

        if (whiteFadePreset != _data.whiteFadePreset)
        {
            Undo.RecordObject(_data, "Set White Fade Preset");
            _data.whiteFadePreset = whiteFadePreset;
            MarkDirty();

            _data.ApplyAesthetics();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();

        // The sky is the scene's skybox material, not a global — so picking one writes the
        // scene's lighting settings once, and the scene carries it from there.
        var pickedSky = LevelSelectRiverPresetLibrary.DrawPicker(
            new GUIContent("Sky Preset",
                           "This world's sky — a gradient and drifting clouds, authored in the " +
                           "Skybox Generator. Picking one puts its material on this scene's " +
                           "skybox. None leaves the scene's skybox as it is."),
            _data.skyboxPreset, SkyboxGeneratorWindow.Folder);

        var skyboxPreset = (SkyboxPreset)EditorGUILayout.ObjectField(
            pickedSky, typeof(SkyboxPreset), false);

        if (skyboxPreset != _data.skyboxPreset)
        {
            Undo.RecordObject(_data, "Set Sky Preset");
            _data.skyboxPreset = skyboxPreset;
            MarkDirty();

            if (skyboxPreset != null)
            {
                SkyboxPresetBuilder.Save(skyboxPreset);
                RenderSettings.skybox = skyboxPreset.material;
                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Open Skybox Generator"))
            SkyboxGeneratorWindow.Open(_data.skyboxPreset);

        // Not part of the preset: it is a way of READING the water rather than a way the water is
        // meant to look, so it belongs to the world and no preset can carry it on by accident.
        EditorGUI.BeginChangeCheck();
        var debugView = (RiverRippleDebugView)EditorGUILayout.EnumPopup(
            new GUIContent("Ripple Debug View",
                           "Draws the frame each water surface was generated in instead of the " +
                           "water: Bands is the coordinate its lines are cut from, Along the one " +
                           "they run in, Fade how much of it is really there under an overlap. " +
                           "Purple means a surface has no frame at all — Rebuild Runs. Needs the " +
                           "subgraph's Debug and DebugMix outputs wired into the water graph."),
            _data.rippleDebugView);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Set Ripple Debug View");
            _data.rippleDebugView = debugView;
            MarkDirty();

            _data.ApplyAesthetics();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();

        // Geometry, not globals — which is the whole reason this is here and not in the tuner
        // alongside everything else the water is drawn with.
        EditorGUILayout.LabelField("Where Two Waters Meet", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        float poolOverlap = EditorGUILayout.FloatField(
            new GUIContent("Water Pool Overlap",
                           "How far a pool's water carries on out into each river that meets " +
                           "it, over that river's own water, fading out as it goes. 0 butts it " +
                           "onto the line the river's water ends on as before."),
            _data.waterPoolOverlap);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Set Water Overlap");
            _data.waterPoolOverlap = Mathf.Max(0f, poolOverlap);
            MarkDirty();
        }

        EditorGUILayout.LabelField(
            "Changes the generated mesh — press Rebuild Runs to see it. How much of " +
            "the lap the alpha gradient covers is River Fade To Pool Distance, in the River Tuner.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField(
            "Pushed as shader globals — the whole world at once, no per-piece override.",
            EditorStyles.miniLabel);

        // The fade is the odd one out, and silently so if nobody says it: it is authored on the
        // material too, so whatever is typed on the Run Material is overwritten from here.
        if (_data.riverMaterial == null)
            EditorGUILayout.HelpBox(
                "No Run Material assigned (Setup > River Prefabs), so Distance Fade Radius has " +
                "no material to land on — the run shader exposes it, and its own value wins " +
                "over the global this pushes.",
                MessageType.Warning);
        else
            EditorGUILayout.LabelField(
                $"Fade radius is written onto {_data.riverMaterial.name}, overriding what that " +
                "material holds.", EditorStyles.miniLabel);
    }

    private void DrawCorePrefabsSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.dataControllerPrefab        = (GameObject)EditorGUILayout.ObjectField("Data Controller",   _data.dataControllerPrefab,        typeof(GameObject), false);
        _data.boatPrefab                  = (GameObject)EditorGUILayout.ObjectField("Boat",              _data.boatPrefab,                   typeof(GameObject), false);
        _data.landscapeTilePrefab         = (GameObject)EditorGUILayout.ObjectField("Landscape Tile",    _data.landscapeTilePrefab,          typeof(GameObject), false);
        _data.videoPlayerControllerPrefab = (GameObject)EditorGUILayout.ObjectField("Video Controller",  _data.videoPlayerControllerPrefab,  typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawAudioSection()
    {
        EditorGUI.BeginChangeCheck();
        _data.startingAudio               = (AudioClip)EditorGUILayout.ObjectField(new GUIContent("Starting Audio", "Plays from the start until the boat reaches a node with Audio on (in the node's row)."), _data.startingAudio, typeof(AudioClip), false);
        EditorGUI.indentLevel++;
        _data.startingAudioLooped         = EditorGUILayout.Toggle(new GUIContent("Looped", "Off = it plays through once."), _data.startingAudioLooped);
        _data.startingAudioFadeIn         = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Fade In", "Seconds it takes to fade in. 0 = straight in."), _data.startingAudioFadeIn));
        EditorGUI.indentLevel--;
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
        void AddBool(string key, bool v) =>
            snap.entries.Add(new SetupSnapshot.Entry { key = key, value = v.ToString() });

        // River
        AddObj  ("riverMaterial",                 _data.riverMaterial);
        AddObj  ("waterMaterial",                 _data.waterMaterial);
        AddFloat("splineInstantiateSpacing",      _data.splineInstantiateSpacing);
        AddFloat("waterLevel",                    _data.waterLevel);
        AddFloat("generatedDrop",                 _data.generatedDrop);
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
        AddObj  ("startingAudio",                 _data.startingAudio);
        AddBool ("startingAudioLooped",           _data.startingAudioLooped);
        AddFloat("startingAudioFadeIn",           _data.startingAudioFadeIn);

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
        bool LoadBool(string key, bool fallback)
        {
            var e = snap.entries.Find(x => x.key == key);
            return (e != null && bool.TryParse(e.value, out bool v)) ? v : fallback;
        }

        Undo.RecordObject(_data, "Restore Setup");

        // River
        _data.riverMaterial                   = LoadObj<Material>("riverMaterial");
        _data.waterMaterial                   = LoadObj<Material>("waterMaterial");
        _data.splineInstantiateSpacing        = LoadFloat("splineInstantiateSpacing", 0.15f);
        _data.waterLevel                      = LoadFloat("waterLevel", 0f);
        _data.generatedDrop                   = LoadFloat("generatedDrop", 8f);
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
        _data.startingAudio                   = LoadObj<AudioClip>("startingAudio") ?? LoadObj<AudioClip>("windLoop");
        _data.startingAudioLooped             = LoadBool ("startingAudioLooped", true);
        _data.startingAudioFadeIn             = LoadFloat("startingAudioFadeIn", 0f);

        MarkDirty();
        _consoleStatusMsg = $"Setup restored from {savePath}";
        Repaint();
    }

    // Draws a consistently-styled indented sub-foldout inside the Setup section.
    private bool _foldProcGen       = false;
    private bool _foldProcGenRivers = false;
    private bool _foldProcGenPools;
    private bool _foldProcGenArenas;
    private bool _foldProcGenEntrances;

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
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            _scriptsLocked ? "Held as they stand" : "Designer may spawn and clear these",
            EditorStyles.miniLabel);
        DrawScriptsLockButton(EditorStyles.miniButton);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);

        DrawDeployRow("Data Controller",   _data.dataController   != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<LevelSelectDataController>(); if (f) _data.dataController   = f; },
            () => { if (DeployScriptOnly<LevelSelectDataController>("LevelSelectDataController", out var c, _data.dataControllerPrefab)) _data.dataController = c; });

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

        // The camera's glance towards interact points, switched on the controller itself so the
        // designer and the camera's own inspector are the one setting.
        using (new EditorGUI.DisabledScope(_data.cameraController == null))
        {
            bool leanOn = _data.cameraController != null && _data.cameraController.interactLeanEnabled;
            EditorGUI.BeginChangeCheck();
            leanOn = EditorGUILayout.Toggle(
                new GUIContent("Interact Look Influence",
                    "Whether interact points pull the camera's look as the boat passes them. " +
                    "Off, the camera stays dead behind the boat."), leanOn);
            if (EditorGUI.EndChangeCheck() && _data.cameraController != null)
            {
                Undo.RecordObject(_data.cameraController, "Toggle Interact Look Influence");
                _data.cameraController.interactLeanEnabled = leanOn;
                EditorUtility.SetDirty(_data.cameraController);
            }
        }

        DrawDeployRow("Music Controller", _data.musicController != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<LevelSelectMusicController>(); if (f) _data.musicController = f; },
            () => DeployMusicController());

        DrawDeployRow("Fog Field", _data.fogFieldManager != null,
            () => { var f = UnityEngine.Object.FindObjectOfType<FogFieldManager>(); if (f) _data.fogFieldManager = f; },
            () => DeployFogField());

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
            var  prevBg      = GUI.backgroundColor;
            bool prevEnabled = GUI.enabled;
            GUI.enabled         = prevEnabled && !_scriptsLocked;
            GUI.backgroundColor = _scriptsLocked ? Color.gray : new Color(0.5f, 0.8f, 1f);

            if (GUILayout.Button(new GUIContent("Deploy", _scriptsLocked
                    ? "Script objects are locked — unlock in the toolbar to deploy this."
                    : ""), EditorStyles.miniButton, GUILayout.Width(46)))
            {
                onDeploy();
                MarkDirty();
            }

            GUI.backgroundColor = prevBg;
            GUI.enabled         = prevEnabled;
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
        // Found first: a missing prefab only matters when there is nothing standing.
        if (GameObject.Find("ArenaSoulsWindow") != null) return;

        if (_data.arenaSoulsWindowPrefab == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] Arena Souls Window prefab not assigned.");
            return;
        }
        if (ScriptsLocked("deploying the Arena Souls Window")) return;

        var parent = FindOrCreateParent("LEVELSELECT_SCRIPTS");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(_data.arenaSoulsWindowPrefab, parent.transform);
        Undo.RegisterCreatedObjectUndo(go, "Deploy Arena Souls Window");
        go.name = "ArenaSoulsWindow";
    }

    private void DeployPauseManager()
    {
        // Found first: a missing prefab only matters when there is nothing standing.
        if (_data.pauseManagerScriptPrefab == null &&
            FindExistingScript<PauseManager>(FindParent("LEVELSELECT_SCRIPTS"), "PauseManager_Script") == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] Pause Manager prefab not assigned.");
            return;
        }
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

    // ══════════════════════════════════════════════════════════════
    // SCRIPT OBJECTS LOCK
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// The containers whose contents are hand-wired in the inspector rather than generated
    /// from the canvas: the script objects, the boat, the camera, the UI canvas and the river
    /// extrusion. Nothing in the designer authors them, so respawning them can only lose work.
    /// Generated geometry — runs, water, pools, arenas, shops, obstacles, landscape — is not
    /// in here and is rebuilt every GENERATE as before.
    /// </summary>
    private static readonly string[] ScriptObjectParents =
        { "LEVELSELECT_SCRIPTS", "PlayerBoat", "CAMERA", "CANVAS", "RiverExtrusion" };

    /// <summary>Locked by default, and remembered across Unity sessions.</summary>
    private bool _scriptsLocked = true;

    /// <summary>
    /// The lock's one gate. Every spawn and every destroy of a script object asks here first,
    /// so nothing — GENERATE, Clear Generated or a Deploy button — can put one into
    /// the scene or take one out while the lock is on. What already stands is still found and
    /// re-wired as usual, so a regenerate still hooks the new splines up to the managers.
    /// Returns true when the caller must leave the scene alone.
    /// </summary>
    private bool ScriptsLocked(string action)
    {
        if (!_scriptsLocked) return false;
        Debug.Log($"[LevelSelectDesigner] Script objects are locked — skipped {action}.");
        return true;
    }

    private const string ScriptsLockTip =
        "LOCKED: the script objects, boat, camera, canvas and river extrusion are never " +
        "spawned or destroyed by the designer. GENERATE and Clear leave them standing and " +
        "only re-wire them; generated geometry still rebuilds as normal.\n\n" +
        "UNLOCKED: the designer may deploy and clear them again.";

    private void DrawScriptsLockButton(GUIStyle style)
    {
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = _scriptsLocked
            ? new Color(1f, 0.82f, 0.35f)
            : new Color(1f, 0.5f, 0.45f);

        if (GUILayout.Button(new GUIContent(
                _scriptsLocked ? "Scripts LOCKED" : "Scripts UNLOCKED", ScriptsLockTip),
                style, GUILayout.Width(118)))
        {
            _scriptsLocked = !_scriptsLocked;
            EditorPrefs.SetBool(K_ScriptsLock, _scriptsLocked);
        }

        GUI.backgroundColor = prevBg;
    }

    private bool DeployScriptOnly<T>(string goName, out T result, GameObject prefabOverride) where T : Component
    {
        result = null;

        // Script objects survive a GENERATE, so an existing one is reused rather than
        // duplicated — otherwise every regenerate stacks another copy in the scene.
        var existing = FindExistingScript<T>(FindParent("LEVELSELECT_SCRIPTS"), goName);
        if (existing != null) { result = existing; return true; }

        if (ScriptsLocked($"deploying {goName}")) return false;

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

    /// <summary>
    /// The one test for "is this already deployed?", used by every deploy. It looks in three
    /// places, widest last: a child of the scripts parent carrying the component, the named
    /// GameObject, then anywhere in the scene.
    ///
    /// It deliberately never asks the designer's own reference to the object. Those live on
    /// the workspace clone, which comes back null after a domain reload or a data switch while
    /// the object is still standing in the scene — and a deploy that trusted them put a second
    /// copy down beside the first.
    /// </summary>
    private static T FindExistingScript<T>(GameObject parent, string goName) where T : Component
    {
        if (parent != null)
        {
            var underParent = parent.GetComponentInChildren<T>(true);
            if (underParent != null) return underParent;
        }

        var named = GameObject.Find(goName);
        if (named != null)
        {
            var onNamed = named.GetComponent<T>();
            if (onNamed != null) return onNamed;
        }

        return UnityEngine.Object.FindObjectOfType<T>();
    }

    private void DeployUIPrefab(GameObject prefab, string goName)
    {
        // Found first: a missing prefab only matters when there is nothing standing.
        if (GameObject.Find(goName) != null) return;

        if (prefab == null)
        {
            Debug.LogWarning($"[LevelSelectDesigner] No prefab set for {goName}.");
            return;
        }
        if (ScriptsLocked($"deploying {goName}")) return;

        var canvasGo = GameObject.Find("CANVAS");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, $"Deploy {goName}");
        go.name = goName;
        if (canvasGo != null) go.transform.SetParent(canvasGo.transform, false);
        EditorUtility.SetDirty(go);
    }

    private void DeployBoat()
    {
        if (DeployScriptOnly<LevelSelectBoatControl>("LevelSelectBoatControl", out var c, null))
        {
            _data.boatControl = c;
            MarkDirty();
        }
    }

    // The fog rig's own assets. The paint and blur materials are the pair FogFieldManager runs
    // on and are not optional — without them LateUpdate returns on its first line and the whole
    // field silently does nothing. The sheet material is the one the gameplay scene is actually
    // running, rather than either of the other two sitting beside it in that folder.
    private const string FOG_PAINT_MAT = "Assets/ScriptsData/FogScripts/FogPaint.mat";
    private const string FOG_BLUR_MAT  = "Assets/ScriptsData/FogScripts/FogBlur.mat";
    private const string FOG_SHEET_MAT = "Assets/ScriptsData/FogScripts/FogSheet 1.mat";

    /// <summary>
    /// Stand up the fog rig for this world: the field manager, and the sheet it draws on.
    ///
    /// Two things differ from the same rig inside a level, and both come from this being an
    /// exterior rather than an arena.
    ///
    ///   THE SHEET HOVERS OVER THE LANDSCAPE. Inside a level it sits on the wave plane and finds
    ///   its height and centre from it. There is no wave plane out here, so it is sized to the
    ///   landscape tile family and stood Fog Height Offset above the tile surface instead. The
    ///   painted window is still small and still travels with the boat — only this sheet is big.
    ///
    ///   ROCK ADOPTION IS OFF. Nothing in a level select world publishes IRockRing, so the scan
    ///   would find nothing; and because it re-scans whenever it found nothing last time, leaving
    ///   it on means a whole-scene sweep of every MonoBehaviour every couple of seconds, forever,
    ///   for no result.
    /// </summary>
    private void DeployFogField()
    {
        if (!DeployScriptOnly<FogFieldManager>("FogFieldManager", out var mgr, null))
            return;

        var paint = AssetDatabase.LoadAssetAtPath<Material>(FOG_PAINT_MAT);
        var blur  = AssetDatabase.LoadAssetAtPath<Material>(FOG_BLUR_MAT);
        if (paint == null) Debug.LogWarning($"[LevelSelectDesigner] {FOG_PAINT_MAT} missing — the fog field will not paint.");
        if (blur  == null) Debug.LogWarning($"[LevelSelectDesigner] {FOG_BLUR_MAT} missing — the fog field will not paint.");

        var boatGo = GameObject.Find("LevelSelectBoat");
        if (boatGo == null)
            Debug.LogWarning("[LevelSelectDesigner] No LevelSelectBoat yet — deploy the Player Boat " +
                             "first, or the fog field has nothing to centre on. It is re-wired at " +
                             "runtime either way.");

        var so = new SerializedObject(mgr);
        so.Update();
        so.FindProperty("paintMaterial").objectReferenceValue = paint;
        so.FindProperty("blurMaterial").objectReferenceValue  = blur;
        if (boatGo != null) so.FindProperty("boat").objectReferenceValue = boatGo.transform;
        so.FindProperty("arenaWidth").floatValue = Mathf.Max(_data.LandscapeSpan, 0.01f);
        so.FindProperty("fogEnabled").boolValue  = _data.fogEnabled;
        so.FindProperty("fogMap").objectReferenceValue = _data.fogEnabled ? _data.fogMap : null;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(mgr);

        DeployFogSheet();
        RebuildFogZones();

        _data.fogFieldManager = mgr;
        MarkDirty();
    }

    /// <summary>
    /// The surface the fog is displayed on: one static square over the whole landscape, held
    /// clear of the tile surface. It never moves — the fog window travels across it — so it has
    /// to be wide enough to cover everywhere the boat can go.
    /// </summary>
    private void DeployFogSheet()
    {
        var existing = FindExistingScript<FogSheetMesh>(FindParent("LEVELSELECT_SCRIPTS"), "FogSheet");
        // A fog zone's sheet is never the landscape sheet, whatever the scene-wide search turned up.
        if (existing != null && existing.GetComponentInParent<FogZone>(true) != null) existing = null;
        GameObject go;
        if (existing != null) go = existing.gameObject;
        else
        {
            if (ScriptsLocked("deploying the Fog Sheet")) return;

            var parent = FindOrCreateParent("LEVELSELECT_SCRIPTS");
            go = new GameObject("FogSheet");
            Undo.RegisterCreatedObjectUndo(go, "Deploy Fog Sheet");
            go.transform.SetParent(parent.transform, true);
        }

        var sheet = go.GetComponent<FogSheetMesh>();
        if (sheet == null) sheet = Undo.AddComponent<FogSheetMesh>(go);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial == null)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FOG_SHEET_MAT);
            if (mat != null) mr.sharedMaterial = mat;
            else Debug.LogWarning($"[LevelSelectDesigner] {FOG_SHEET_MAT} missing — the fog sheet has no material and nothing will draw.");
        }

        // Placed rather than found. FogSheetMesh takes its height and centre from a wave plane
        // when there is one, and a level select world has none — so the landscape's own centre
        // and surface height are handed over here as the sheet's fallback waterline instead.
        Vector3 centre = _data.LandscapeCentre;
        go.transform.position = new Vector3(centre.x, centre.y + _data.fogHeightOffset, centre.z);

        var so = new SerializedObject(sheet);
        so.Update();
        so.FindProperty("matchFieldCoverage").boolValue    = true;
        so.FindProperty("matchWavePlaneCentre").boolValue  = false;
        so.FindProperty("waterlineY").floatValue           = centre.y;
        so.FindProperty("heightOffset").floatValue         = _data.fogHeightOffset;
        so.ApplyModifiedProperties();

        sheet.Generate();
        EditorUtility.SetDirty(sheet);
    }

    private void DeployMusicController()
    {
        if (!DeployScriptOnly<LevelSelectMusicController>("LevelSelectMusicController", out var ctrl, null))
            return;

        // _sourceData, not the workspace clone — a clone reference serialises out as null and
        // the built scene gets no music data. Same trap as the data controller above.
        var so = new SerializedObject(ctrl);
        so.FindProperty("data").objectReferenceValue = _sourceData;
        so.ApplyModifiedProperties();
        if (_sourceData == null)
            Debug.LogWarning("[LevelSelectDesigner] No designer asset on disk, so the music " +
                             "controller was left unwired — save the designer data first.");

        _data.musicController = ctrl;
        MarkDirty();
    }

    private void DeployPlayerBoat()
    {
        // Only deploy the boat child if it doesn't already exist under the parent
        var parent = FindParent("PlayerBoat");
        if (parent != null && parent.transform.childCount > 0) return;
        if (ScriptsLocked("deploying the Player Boat")) return;

        parent = FindOrCreateParent("PlayerBoat");

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
        var existing = FindExistingScript<VideoPlayerController>(
            FindParent("LEVELSELECT_SCRIPTS"), "VideoPlayerController");
        if (existing != null)
        {
            if (_data.videoPlayerController != existing)
            {
                _data.videoPlayerController = existing;
                MarkDirty();
            }
            return;
        }

        var prefab = _data.videoPlayerControllerPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] No VideoPlayerController prefab assigned — cannot deploy.");
            return;
        }
        if (ScriptsLocked("deploying the Video Controller")) return;

        var parent = FindOrCreateParent("LEVELSELECT_SCRIPTS");
        var go     = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
        Undo.RegisterCreatedObjectUndo(go, "Deploy Video Controller");
        go.name = "VideoPlayerController";
        _data.videoPlayerController = go.GetComponent<VideoPlayerController>();
        MarkDirty();
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
        if (ScriptsLocked("spawning the Main Camera")) return;

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
        var go = GameObject.Find("LevelSelectCamera");

        // A camera that is not an instance of the assigned prefab is reported, never replaced.
        // Deleting it took the hand-wired Cinemachine rig standing on it with it, which is one
        // of the ways the camera used to vanish mid-session.
        if (go != null && _data.cameraPrefab != null &&
            PrefabUtility.GetCorrespondingObjectFromSource(go) != _data.cameraPrefab)
            Debug.LogWarning($"[LevelSelectDesigner] '{go.name}' is not an instance of the assigned " +
                             $"camera prefab '{_data.cameraPrefab.name}'. It has been left standing and " +
                             "wired as it is — replace it yourself if that is wrong.");

        if (go == null)
        {
            if (ScriptsLocked("deploying the Camera Controller")) return;

            var parent = FindOrCreateParent("CAMERA");
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
            go.transform.SetParent(parent.transform, false);
        }
        else if (!_scriptsLocked)
        {
            var parent = FindOrCreateParent("CAMERA");
            if (go.transform.parent != parent.transform)
                go.transform.SetParent(parent.transform, false);
        }

        // Wire LevelSelectCameraController. Pre-wiring the preview fields is what makes the
        // editor's Preview Start/Transition buttons work straight away.
        var comp = go.GetComponent<LevelSelectCameraController>();
        if (comp == null) comp = Undo.AddComponent<LevelSelectCameraController>(go);
        _data.cameraController = comp;
        WireCameraPreviewTarget();
    }

    private void DeployAllSceneObjects()
    {
        // Every deploy below finds what already stands before it makes anything, and asks the
        // lock before it makes anything at all — so they are all safe to call plainly on every
        // pass. The old "if (_data.x == null)" pre-tests are gone on purpose: those references
        // live on the workspace clone and come back null after a domain reload, which is how a
        // second copy of a manager used to end up beside the first.
        AutoFindAll();
        EnsureMainCamera();

        DeployVideoController();
        if (DeployScriptOnly<LevelSelectDataController>("LevelSelectDataController",   out var dc,  _data.dataControllerPrefab)) _data.dataController  = dc;
        DeployBoat();
        DeployPlayerBoat();
        DeployCameraController();
        DeployMusicController();
        DeployArenaSoulsWindow();
        DeployPauseManager();

        // UI Canvas prefabs — only deploy if CANVAS parent prefab is assigned
        if (_data.canvasParentPrefab != null)
        {
            if (GameObject.Find("CANVAS") == null && !ScriptsLocked("deploying the CANVAS"))
            {
                var canvas = (GameObject)PrefabUtility.InstantiatePrefab(_data.canvasParentPrefab);
                Undo.RegisterCreatedObjectUndo(canvas, "Deploy CANVAS");
                canvas.name = "CANVAS";
            }
            DeployUIPrefab(_data.pauseMenuPrefab,          "PauseMenuUI");
            DeployUIPrefab(_data.boatHUDPrefab,            "BoatHUDPrompts");
            DeployUIPrefab(_data.soulsOnBoatDisplayPrefab, "SoulsDisplayBarUI");
            DeployUIPrefab(_data.orbsCounterPrefab,        "OrbsCounterUI");
            DeployUIPrefab(_data.shopTooltipPrefab,        "ShopTooltipHUD");
        }

        // Deployed after SoulsDisplayBarUI exists so the wiring can find it
        DeploySoulsOnBoatDisplay();
        WirePauseMenuPanels();
        EditorUtility.SetDirty(_data);
    }

    private void AutoFindAll()
    {
        Undo.RecordObject(_data, "Auto-Find Scene Objects");
        TryFind<LevelSelectDataController>(v => _data.dataController = v);
        TryFind<LevelSelectBoatControl>(v => _data.boatControl       = v);
        TryFind<LevelSelectCameraController>(v => _data.cameraController = v);
        TryFind<LandscapeTool>(v             => _data.landscapeTool          = v);
        TryFind<SoulDisplaySlotManager>(v       => _data.soulDisplaySlotManager    = v);
        TryFind<PauseMenuUI>(v                  => _data.pauseMenuUI               = v);
        TryFind<PauseManager>(v                 => _data.pauseManager              = v);
        TryFind<SoulsOnBoatDisplayManager>(v    => _data.soulsOnBoatDisplayManager = v);
        TryFind<VideoPlayerController>(v        => _data.videoPlayerController     = v);
        TryFind<FogFieldManager>(v              => _data.fogFieldManager           = v);
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

        if (_data.dataController != null && _data.boatControl != null)
        {
            var so = new SerializedObject(_data.dataController);
            var prop = so.FindProperty("boatControl");
            if (prop != null) { prop.objectReferenceValue = _data.boatControl; so.ApplyModifiedProperties(); }
            EditorUtility.SetDirty(_data.dataController);
        }

        // ── This data asset → LevelSelectDataController ───────────────────────
        // The controller hands the fog map over on Start and pushes the world's aesthetics every
        // frame, and this is the only way it can reach either: a level select world has no
        // GridData to read them off.
        //
        // _sourceData, NOT _data. _data is the workspace clone — Instantiate'd and flagged
        // HideAndDontSave — so a scene cannot hold a reference to it: Unity writes the field out
        // as null, the scene looks wired in the editor session that wired it, and the build gets
        // nothing. Only the asset on disk survives serialisation.
        if (_data.dataController != null)
        {
            if (_sourceData == null)
                Debug.LogWarning("[LevelSelectDesigner] No designer asset on disk, so the data " +
                                 "controller was left unwired — save the designer data first.");
            else
            {
                var so   = new SerializedObject(_data.dataController);
                var prop = so.FindProperty("designerData");
                if (prop != null) { prop.objectReferenceValue = _sourceData; so.ApplyModifiedProperties(); }
                EditorUtility.SetDirty(_data.dataController);
            }
        }

        WirePauseMenuPanels();
        WirePauseManager();
        WireCameraPreviewTarget();

        Debug.Log("[LevelSelectDesigner] Wire All complete.");
    }

    private void DrawCanvasSettingsSection()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Canvas", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _data.canvasWorldY     = EditorGUILayout.FloatField("World Y", _data.canvasWorldY);
        _data.arenaHeadOffset    = EditorGUILayout.FloatField("Arena Head Offset", _data.arenaHeadOffset);
        _data.shopHeadOffset     = EditorGUILayout.FloatField("Shop Head Offset", _data.shopHeadOffset);
        if (EditorGUI.EndChangeCheck()) MarkDirty();
    }

    private void DrawCameraSequenceSection()
    {
        // The camera sequence lives in the scene, not the data asset — found, not stored.
        var cameraSequence = FindAnyObjectByType<CameraSequence>();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Camera Sequence", cameraSequence != null
                ? $"{cameraSequence.shots.Count} shots · {cameraSequence.TotalDuration:F1}s"
                : "none in scene");
            if (GUILayout.Button("Storyboard", GUILayout.Width(90)))
                CameraSequenceWindow.Open(cameraSequence);
        }
        if (cameraSequence == null)
        {
            DrawCinematicsList(null);
            return;
        }

        // The scene's Play On Start + Start Preset — the same two the storyboard shows.
        var so          = new SerializedObject(cameraSequence);
        var playOnStart = so.FindProperty("playOnStart");
        var startPreset = so.FindProperty("startPreset");
        so.Update();

        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(playOnStart, new GUIContent("Starting Cinematic",
            "On: the scene plays the preset below when it starts. Belongs to the scene, not to any preset."));
        if (playOnStart.boolValue)
        {
            EditorGUI.indentLevel++;
            using (new EditorGUILayout.HorizontalScope())
            {
                var current = (CameraSequencePreset)startPreset.objectReferenceValue;
                var picked  = CameraSequencePresetIO.DrawPicker(new GUIContent("Preset",
                    "The camera sequence the scene starts with. Presets are kept in " + CameraSequencePresetIO.Folder + "."), current);
                startPreset.objectReferenceValue = EditorGUILayout.ObjectField(
                    picked, typeof(CameraSequencePreset), false, GUILayout.Width(120f));
            }
            if (startPreset.objectReferenceValue == null)
                EditorGUILayout.LabelField("No preset — the scene starts with whatever the storyboard holds.",
                                           EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }

        if (so.ApplyModifiedProperties())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(cameraSequence.gameObject.scene);

        DrawCinematicsList(cameraSequence);
    }

    /// <summary>
    /// Every cinematic in the map — the scene's starting one, each node's, each souls pool tower's — as one
    /// list, every row the same: name, Select, Open, and a line saying how it is set. Read only; each is
    /// edited where it lives (above, in a node's row, in a pool's Tower section).
    /// </summary>
    private void DrawCinematicsList(CameraSequence cameraSequence)
    {
        var  nodes    = _data.nodes.Where(n => n != null && n.cinematicTrigger).ToList();
        var  towers   = TowerCinematicPools().ToList();
        bool starting = cameraSequence != null && cameraSequence.playOnStart;
        int  count    = nodes.Count + towers.Count + (starting ? 1 : 0);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Cinematics ({count})", EditorStyles.boldLabel);
        if (count == 0)
        {
            EditorGUILayout.LabelField("None — switch Starting Cinematic on above, Cinematic on in a node's row, " +
                                       "or Cinematic on a souls pool tower.", EditorStyles.wordWrappedMiniLabel);
            return;
        }

        EditorGUI.indentLevel++;
        if (starting)
        {
            DrawCinematicRow("Starting Cinematic", "Selects the scene's Camera Sequence object.",
                () => { Selection.activeObject = cameraSequence.gameObject; EditorGUIUtility.PingObject(cameraSequence.gameObject); },
                cameraSequence.startPreset, "no preset — plays whatever the storyboard holds", "Scene start", false);
        }

        foreach (var node in nodes)
        {
            string trigger = node.cinematicStart == LevelSelectDesignerData.CinematicStart.PressKey
                ? $"Press E (\"{node.cinematicPrompt}\", reach {node.cinematicReach:0.##})"
                : "Sail Into Box";
            DrawCinematicRow(NodeLabel(node.id), "Selects this node, as clicking it on the canvas does.",
                () => SelectCinematicNode(node.id), node.cinematicPreset, "no preset — does nothing",
                trigger, node.cinematicPinCameras);
        }

        foreach (var pool in towers)
        {
            var    point   = FindPoolTower(pool)?.GetComponent<LevelSelectInteractPoint>();
            string trigger = point != null
                ? $"Press E (\"{point.prompt}\", reach {point.radius:0.##}), releasing the souls"
                : "Press E, releasing the souls — tower not generated yet";
            DrawCinematicRow($"{NodeLabel(pool.nodeId)} — Souls Pool Tower",
                "Selects this pool's node, as clicking it on the canvas does.",
                () => SelectCinematicNode(pool.nodeId), pool.towerCinematicPreset,
                "no preset — only lets the souls out", trigger, pool.towerCinematicPinCameras);
        }
        EditorGUI.indentLevel--;
    }

    private void DrawCinematicRow(string label, string selectTip, System.Action select,
                                  CameraSequencePreset preset, string noPreset, string trigger, bool pinned)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, EditorStyles.label);
            if (GUILayout.Button(new GUIContent("Select", selectTip), GUILayout.Width(60f)))
            {
                select();
                GUI.FocusControl(null);
                Repaint();
            }
            using (new EditorGUI.DisabledScope(preset == null || Application.isPlaying))
            {
                if (GUILayout.Button(new GUIContent("Open", "Opens the Camera Sequence storyboard on this " +
                        "preset, loading it there if another preset is active (asks first about unsaved changes)."),
                        GUILayout.Width(50f)))
                {
                    CameraSequenceWindow.OpenPresetFromDesigner(preset);
                    GUIUtility.ExitGUI();
                }
            }
        }
        EditorGUI.indentLevel++;

        string details = $"Preset: {(preset != null ? preset.name : noPreset)}   ·   Trigger: {trigger}";
        if (pinned) details += "   ·   Cameras pinned";
        if (preset != null && !preset.playOnce) details += "   ·   Plays every time";
        EditorGUILayout.LabelField(details, EditorStyles.wordWrappedMiniLabel);

        EditorGUI.indentLevel--;
    }

    // A canvas click on a lone node: just it, its river, its row opened.
    private void SelectCinematicNode(string nodeId)
    {
        _multiNodeIds.Clear();
        _multiNodeIds.Add(nodeId);
        SelectNodeInList(nodeId);
        _selectedPathId = _data.paths.Find(p => p.nodeIds.Contains(nodeId))?.pathId;
        ClearWallAndPipeSelection();
    }

    // Pinned to the top of the left panel — never scrolls away.
    private void DrawGenerateBlock()
    {
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
            EditorApplication.delayCall += () => { if (_data != null) Generate(); };
        }
        GUI.backgroundColor = prevColor;
        GUI.enabled = true;

        bool canPlaytest = _data != null && !string.IsNullOrEmpty(_data.targetScenePath);
        GUI.enabled = canPlaytest;
        GUI.backgroundColor = canPlaytest ? new Color(0.55f, 0.8f, 1f) : Color.gray;
        if (GUILayout.Button(new GUIContent("PLAYTEST (Fresh Save)",
                "Clear saved progress and enter play mode in this world's scene"),
                GUILayout.Height(26)))
            PlaytestLinkedScene(freshSave: true, skipOpening: SkipOpeningCinematic);
        GUI.backgroundColor = prevColor;
        GUI.enabled = true;

        DrawSkipOpeningToggle();
        DrawPlaytestFromPositionButton(canPlaytest);

        EditorGUILayout.Space(4);

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
    }

    // ══════════════════════════════════════════════════════════════
    // CANVAS
    // ══════════════════════════════════════════════════════════════
    private void DrawCanvas()
    {
        Rect fullRect = GUILayoutUtility.GetRect(0, 0,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // Reserve strips along the bottom and right edges for the scrollbars.
        bool showBars = fullRect.width > SCROLLBAR_W * 4f && fullRect.height > SCROLLBAR_W * 4f;
        float barW = showBars ? SCROLLBAR_W : 0f;
        _canvasRect = new Rect(fullRect.x, fullRect.y,
                               fullRect.width - barW, fullRect.height - barW);

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
            DrawPools();
            DrawPoolTowers();
            DrawWalkways();
            DrawBridges();
            DrawOutposts();
            DrawPipes();
            DrawWalls();
            DrawRimNodes();
            DrawSoulRoutes();
            DrawArenaOrbitRings();
            DrawNodes();
            DrawObstacles();
            DrawArenaEntrances();
            DrawBoatStart();
            DrawPlaytestBoat();
            DrawBoatExits();
            DrawSpikes();
            DrawStructures();
            DrawFogZones();
            DrawPerchMarkers();
            DrawShotCameras();
            DrawInProgressPath();
            DrawHillPointsOnTop();
            Handles.EndGUI();
        }

        GUI.EndClip();
        _canvasRect = absoluteCanvasRect;

        if (showBars) DrawCanvasScrollbars(fullRect);

        // Mode hint
        if (Event.current.type == EventType.Repaint)
        {
            string hint = _mode switch
            {
                DesignerMode.Draw     => _isDrawing ? "Click to place knot — Enter/double-click to finish — Esc to cancel" : "Click to start drawing a path",
                DesignerMode.Select   => "Click node or path to select — drag node to move — Delete to remove",
                DesignerMode.RiverSelect => "Click a river to select it — Shift+click to add or remove rivers — drag to move the selected rivers together",
                DesignerMode.PipeSelect  => "Click a pipe to select it — Shift+click to add or remove pipes — drag to move the selected pipes together",
                DesignerMode.Junction => "Click a node to toggle JunctionSplit",
                DesignerMode.Obstacle => "Click along a path to place an obstacle gate",
                DesignerMode.Landscape => "Landscape mode — set tile prefab & counts in left panel, then Generate Tiles",
                DesignerMode.SoulRoute => "Click points in travel order — arenas included — Shift+click sets the origin pool",
                DesignerMode.Perch     => "Click to drop an angel perch — click one to select — Delete to remove",
                DesignerMode.Spike     => "Click to drop a spike — click one to select, drag to move — Delete to remove",
                DesignerMode.Structure => "Click to drop a structure — click one to select, drag to move — D to duplicate — Delete to remove",
                DesignerMode.FogZone   => "Click to drop a fog zone — drag a sheet to move the zone, drag its bounds to move only them — Delete to remove",
                DesignerMode.Pipe      => _isDrawingPipe
                    ? "Click to place pipe nodes — Enter/Esc/double-click to finish"
                    : "Click empty space to start a pipe — Shift+click a pipe to add a support — drag to move — right-click/Delete to remove",
                DesignerMode.Wall      => _isDrawingWall
                    ? "Click to place wall nodes — Enter/Esc/double-click to finish"
                    : "Click empty space to start a wall — click a wall to select it — drag a node to move — right-click/Delete to remove",
                _ => ""
            };
            var style = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            GUI.Label(new Rect(_canvasRect.x + 6, _canvasRect.yMax - 20, _canvasRect.width - 12, 18), hint, style);
        }
    }

    // ── Canvas scrollbars ─────────────────────────────────────────
    // The horizontal bar works in canvas-space X, which is world X negated
    // (see the coordinate helpers), so dragging right moves the view right.
    private void DrawCanvasScrollbars(Rect fullRect)
    {
        Rect content  = CanvasContentBounds();
        float visW    = _canvasRect.width  / _zoom;
        float visH    = _canvasRect.height / _zoom;

        // Horizontal ─ range in negated-X space, always big enough to hold the current view
        float hVal = -_viewCenter.x - visW * 0.5f;
        float hMin = Mathf.Min(-content.xMax, hVal);
        float hMax = Mathf.Max(-content.xMin, hVal + visW);

        Rect hRect = new Rect(fullRect.x, fullRect.yMax - SCROLLBAR_W,
                              fullRect.width - SCROLLBAR_W, SCROLLBAR_W);
        EditorGUI.BeginChangeCheck();
        float newH = GUI.HorizontalScrollbar(hRect, hVal, visW, hMin, hMax);
        if (EditorGUI.EndChangeCheck())
        {
            _viewCenter.x = -(newH + visW * 0.5f);
            SaveViewPrefs();
            Repaint();
        }

        // Vertical ─ straight world Z
        float vVal = _viewCenter.y - visH * 0.5f;
        float vMin = Mathf.Min(content.yMin, vVal);
        float vMax = Mathf.Max(content.yMax, vVal + visH);

        Rect vRect = new Rect(fullRect.xMax - SCROLLBAR_W, fullRect.y,
                              SCROLLBAR_W, fullRect.height - SCROLLBAR_W);
        EditorGUI.BeginChangeCheck();
        float newV = GUI.VerticalScrollbar(vRect, vVal, visH, vMin, vMax);
        if (EditorGUI.EndChangeCheck())
        {
            _viewCenter.y = newV + visH * 0.5f;
            SaveViewPrefs();
            Repaint();
        }

        // Corner filler
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(new Rect(fullRect.xMax - SCROLLBAR_W, fullRect.yMax - SCROLLBAR_W,
                                        SCROLLBAR_W, SCROLLBAR_W),
                               new Color(0.22f, 0.22f, 0.22f));
    }

    // World-space (X,Z) rect covering everything worth scrolling to, plus a margin.
    private Rect CanvasContentBounds()
    {
        float margin = 20f;
        if (_data == null || _data.nodes.Count == 0)
            return new Rect(-margin, -margin, margin * 2f, margin * 2f);

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var n in _data.nodes)
        {
            minX = Mathf.Min(minX, n.worldPosition.x); maxX = Mathf.Max(maxX, n.worldPosition.x);
            minZ = Mathf.Min(minZ, n.worldPosition.z); maxZ = Mathf.Max(maxZ, n.worldPosition.z);
        }
        return new Rect(minX - margin, minZ - margin,
                        (maxX - minX) + margin * 2f, (maxZ - minZ) + margin * 2f);
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

        if (!inCanvas && !_canvasFocused && !_isDraggingNode && !_isDraggingPipe && !_isDraggingWall && !_isDraggingPerch && !_isDraggingSpike && !_isDraggingStructure && !_isPanning) return;

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
            Vector2 panDelta = (e.mousePosition - _panStart) / _zoom;
            if (LevelSelectDesignerSettings.InvertPanX) panDelta.x = -panDelta.x;
            if (LevelSelectDesignerSettings.InvertPanY) panDelta.y = -panDelta.y;
            _viewCenter = _viewCenterAtPanStart + panDelta;
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

        if (!inCanvas && !_isDraggingNode && !_isDraggingPipe && !_isDraggingWall && !_isDraggingPerch && !_isDraggingSpike && !_isDraggingStructure && !_isDraggingFogZone && !_isDraggingRivers && !_isDraggingPipes) return;

        // Picking the boat's start node takes the click before anything else.
        if (HandleBoatStartPick(e)) return;
        if (HandlePlaytestPick(e)) return;

        // Outpost points open their settings from any mode.
        if (HandleOutpostPointClick(e)) return;

        switch (_mode)
        {
            case DesignerMode.Draw:     HandleDrawMode(e);     break;
            case DesignerMode.Select:   HandleSelectMode(e);   break;
            case DesignerMode.RiverSelect: HandleRiverSelectMode(e); break;
            case DesignerMode.PipeSelect:  HandlePipeSelectMode(e);  break;
            case DesignerMode.Junction: HandleJunctionMode(e); break;
            case DesignerMode.Obstacle: HandleObstacleMode(e); break;
            case DesignerMode.Landscape: HandleLandscapeMode(e); break;
            case DesignerMode.SoulRoute: HandleSoulRouteMode(e); break;
            case DesignerMode.Pipe:      HandlePipeMode(e);      break;
            case DesignerMode.Perch:     HandlePerchMode(e);     break;
            case DesignerMode.Wall:      HandleWallMode(e);      break;
            case DesignerMode.Spike:     HandleSpikeMode(e);     break;
            case DesignerMode.FogZone:   HandleFogZoneMode(e);   break;
            case DesignerMode.Structure: HandleStructureMode(e); break;
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
                    // If snapping to the last node of any existing path, extend that path.
                    // Not from a pool: a pool is where rivers meet, so drawing out of one
                    // starts a river of its own instead of carrying on the one that ends there.
                    var extPath = _data.PoolAt(snapId) != null ? null : _data.paths.Find(p =>
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
                NameMeetingsAtDrawnEnds(_drawingNodeIds[0], _drawingNodeIds[_drawingNodeIds.Count - 1]);
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

        // Never into a river leaving a pool — a river drawn in to a pool stays its own river,
        // for the same reason drawing out of one starts a new one.
        var existingPath = _data.PoolAt(endNodeId) != null ? null : _data.paths.Find(p =>
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
            RefreshJunctionLeadIns(junc);
        }

        NameMeetingsAtDrawnEnds(startNodeId, endNodeId);

        _selectedPathId = path.pathId;
        _isDrawing      = false;
        _drawingNodeIds.Clear();
        MarkDirty();
        Repaint();
    }

    // ── Select mode ───────────────────────────────────────────────
    private void HandleSelectMode(Event e)
    {
        if (HandleShotCameraInput(e)) return;
        if (HandlePerchDrag(e)) return;
        if (HandleSpikeDrag(e)) return;
        if (HandleStructureDrag(e)) return;
        if (HandleStructureKeys(e)) return;

        if (e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) &&
            SelectedPerchMarker != null)
        {
            DeleteSelectedPerchMarker();
            e.Use();
            return;
        }

        if (e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) &&
            SelectedSpike != null)
        {
            DeleteSelectedSpike();
            e.Use();
            return;
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.D && SelectedSpike != null)
        {
            DuplicateSelectedSpike();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            // Perch markers are drawn over everything else, so they are tried first, then the
            // spikes drawn just beneath them.
            _selectedPerchId     = null;
            _selectedSpikeId     = null;
            _selectedStructureId = null;
            _selectedShotCamera  = null;
            if (TryPickStructure(e)) return;   // picks it alone — clears everything else itself
            if (TryPickPerchMarker(e) || TryPickSpike(e))
            {
                _selectedNodeId      = null;
                _selectedPathId      = null;
                _selectedObstacleId  = null;
                _selectedArenaNodeId = null;
                _selectedShopNodeId  = null;
                _selectedPoolNodeId  = null;
                _selectedOutpostId   = null;
                _selectedEntranceIdx = -1;
                ClearWallAndPipeSelection();
                return;
            }

            string nodeId    = FindNodeAtCanvas(e.mousePosition);
            bool   startDrag = true;
            if (nodeId != null && e.shift)
            {
                // Shift+click adds the node to the picked ones, or takes it away (no drag then).
                string shown = ToggleNodeInGroup(nodeId);
                if (shown == null)
                {
                    ClearRiverSelectionForWallOrPipe();
                    Repaint();
                    e.Use();
                    return;
                }
                startDrag = shown == nodeId;
                nodeId    = shown;
            }
            else if (nodeId != null)
            {
                PickNodeAlone(nodeId);
            }
            if (nodeId != null)
            {
                _selectedNodeId     = nodeId;
                _selectedObstacleId = null;
                _isDraggingNode     = startDrag && !BeginNodeGroupDrag(e.mousePosition);
                _draggingNodeId     = nodeId;
                var node            = _data.nodes.Find(n => n.id == nodeId);
                _dragOffset         = e.mousePosition - (Vector2)WorldToCanvas(node.worldPosition);
                _selectedPathId     = _data.paths.Find(p => p.nodeIds.Contains(nodeId))?.pathId;
                _selectedArenaNodeId = node?.type == LevelSelectDesignerData.NodeType.ArenaEnd ? nodeId : null;
                _selectedShopNodeId  = node?.type == LevelSelectDesignerData.NodeType.ShopEnd  ? nodeId : null;
                _selectedPoolNodeId  = _data.PoolAt(nodeId) != null ? nodeId : null;
                _selectedEntranceIdx = -1;
                ClearWallAndPipeSelection();
                Repaint();
                e.Use();
                return;
            }

            // Wall and pipe nodes — walls are drawn over pipes, so they are tried first.
            if (FindWallNodeAtCanvas(e.mousePosition, out string wallId, out int wallNode))
            {
                ClearRiverSelectionForWallOrPipe();
                SelectPipe(null, -1, -1);
                SelectWall(wallId, wallNode);
                _wallDragOffset = e.mousePosition - WorldToCanvas(WallNodeWorldFlat(SelectedWall.nodes[wallNode]));
                _isDraggingWall = true;
                e.Use();
                return;
            }
            // A pipe node drops straight into Pipe mode, so its tools (Shift+click supports,
            // right-click delete, drawing) are to hand; the drag carries on there.
            if (FindPipeNodeAtCanvas(e.mousePosition, out string pipeId, out int pipeNode))
            {
                SetMode(DesignerMode.Pipe);
                ClearRiverSelectionForWallOrPipe();
                SelectWall(null, -1);
                SelectPipe(pipeId, pipeNode, -1);
                _pipeDragOffset = e.mousePosition - WorldToCanvas(PipeNodeWorldFlat(SelectedPipe.nodes[pipeNode]));
                _isDraggingPipe = true;
                e.Use();
                return;
            }

            string obsId = FindObstacleAtCanvas(e.mousePosition);
            if (obsId != null)
            {
                ClearWallAndPipeSelection();
                _selectedObstacleId    = obsId;
                _selectedNodeId        = null;
                _selectedShopNodeId    = null;
                _selectedPoolNodeId    = null;
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
            _selectedPoolNodeId = null;
            ClearWallAndPipeSelection();
            Repaint();
            e.Use();
        }

        if (e.type == EventType.MouseDrag && _isDraggingWall)
        {
            var wall = SelectedWall;
            if (wall != null && _selectedWallNode >= 0 && _selectedWallNode < wall.nodes.Count)
            {
                Undo.RecordObject(_data, "Move Wall Node");
                wall.nodes[_selectedWallNode].positionXZ = CanvasToWorld2D(e.mousePosition - _wallDragOffset);
                MarkDirty();
            }
            Repaint();
            e.Use();
        }

        if (e.type == EventType.MouseUp && _isDraggingWall)
        {
            _isDraggingWall = false;
            RebuildWall(SelectedWall);
        }

        if (e.type == EventType.MouseDrag && _isDraggingPipe)
        {
            var pipe = SelectedPipe;
            if (pipe != null && _selectedPipeNode >= 0 && _selectedPipeNode < pipe.nodes.Count)
            {
                Undo.RecordObject(_data, "Move Pipe Node");
                pipe.nodes[_selectedPipeNode].positionXZ = CanvasToWorld2D(e.mousePosition - _pipeDragOffset);
                MarkDirty();
            }
            Repaint();
            e.Use();
        }

        if (e.type == EventType.MouseUp && _isDraggingPipe)
        {
            _isDraggingPipe = false;
            RebuildPipe(SelectedPipe);
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete &&
            (_selectedWallNode >= 0 && SelectedWall != null || _selectedPipeNode >= 0 && SelectedPipe != null))
        {
            // Only ever a node here — the whole wall or pipe is removed from its own mode.
            if (_selectedWallNode >= 0 && SelectedWall != null) DeleteSelectedWallPart();
            else                                                DeleteSelectedPipePart();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && _isDraggingNodeGroup)
        {
            DragNodeGroup(e.mousePosition);
            e.Use();
        }

        if (e.type == EventType.MouseUp && _isDraggingNodeGroup)
            EndNodeGroupDrag();

        if (e.type == EventType.MouseDrag && _isDraggingNode)
        {
            var node = _data.nodes.Find(n => n.id == _draggingNodeId);
            // Lead-ins and compass entrances are locked to their pool or arena — they move with it.
            // In-between nodes are placed off their neighbours.
            if (node != null && !IsLockedNode(node.id) && !IsPlacedInBetween(node.id))
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
            if (_selectedNodeId != null && IsLeadInNode(_selectedNodeId))
            {
                _consoleStatusMsg = "That node is a lead-in — it is locked to its pool, arena or " +
                                    "junction. Remove that, or use Refresh Lead-ins on it.";
                Repaint();
                e.Use();
            }
            else if (_selectedNodeId != null)
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

    // A wall or pipe node picked in Select mode stands alone — nothing on the river stays selected.
    private void ClearRiverSelectionForWallOrPipe()
    {
        _selectedNodeId      = null;
        _selectedPathId      = null;
        _selectedObstacleId  = null;
        _selectedArenaNodeId = null;
        _selectedShopNodeId  = null;
        _selectedPoolNodeId  = null;
        _selectedEntranceIdx = -1;
    }

    /// Landscape and Fog Zones modes own the left panel: drop every other selection so
    /// only hill points (or fog zones) stay picked.
    private void ClearSelectionForLandscape()
    {
        ClearRiverSelectionForWallOrPipe();
        ClearWallAndPipeSelection();
        _selectedOutpostId      = null;
        _selectedJunctionNodeId = null;
        _selectedRouteId        = null;
        _selectedPerchId        = null;
        _selectedSpikeId        = null;
        _selectedStructureId    = null;
        _selectedShotCamera     = null;
        _clampPicking           = false;
        GUI.FocusControl(null);
    }

    /// Souls mode owns the left panel the same way: everything else drops, the picked route stays.
    private void ClearSelectionForSouls()
    {
        string keepRoute = _selectedRouteId;
        ClearSelectionForLandscape();
        _selectedRouteId = keepRoute;
    }

    private void ClearWallAndPipeSelection()
    {
        _selectedWallId      = null;
        _selectedWallNode    = -1;
        _selectedPipeId      = null;
        _selectedPipeNode    = -1;
        _selectedPipeSupport = -1;
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
                RemoveJunctionLeadIns(_data.junctions.Find(j => j.nodeId == existingNodeId));
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

    /// <summary>
    /// Moves an arena to another place in the list, which is the same thing as giving it another
    /// number: the numeral carved on an arena's door is where it sits here, counting from 1.
    ///
    /// The doors are rebuilt on the spot, because two of them are now carrying the wrong number
    /// and the list would be telling you one thing while the scene showed another.
    /// </summary>
    private void MoveArena(int from, int to)
    {
        if (_data == null) return;
        if (from < 0 || from >= _data.arenas.Count) return;
        if (to   < 0 || to   >= _data.arenas.Count || to == from) return;

        Undo.RecordObject(_data, "Reorder Arenas");
        var arena = _data.arenas[from];
        _data.arenas.RemoveAt(from);
        _data.arenas.Insert(to, arena);
        MarkDirty();

        RebuildArchwayMeshes();
        Repaint();
    }

    private void RemoveArena(string nodeId)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node != null) node.type = LevelSelectDesignerData.NodeType.Waypoint;
        RemoveArenaLeadIns(_data.arenas.Find(a => a.nodeId == nodeId));
        _data.arenas.RemoveAll(a => a.nodeId == nodeId);
        if (_selectedArenaNodeId == nodeId) _selectedArenaNodeId = null;
        foreach (var p in _data.paths)
            if (p.nodeIds.Count > 0 && p.nodeIds[p.nodeIds.Count - 1] == nodeId)
                p.leadsToArena = false;
    }

    // ── Leads to Arena ────────────────────────────────────────────
    /// <summary>
    /// Turns the river's arena on or off at the end it stands on (Arena at End picks which).
    /// On makes that node an arena and gives it lead-ins; off takes the arena away.
    /// </summary>
    private void SetLeadsToArena(LevelSelectDesignerData.DesignerPath path, bool on)
    {
        path.leadsToArena = on;
        if (path.nodeIds.Count > 0)
        {
            string nodeId = path.arenaIsAtEnd ? path.nodeIds[path.nodeIds.Count - 1] : path.nodeIds[0];
            var    node   = _data.nodes.Find(n => n.id == nodeId);

            if (on && node != null)
            {
                node.type = LevelSelectDesignerData.NodeType.ArenaEnd;
                if (!_data.arenas.Exists(a => a.nodeId == nodeId))
                    _data.arenas.Add(new LevelSelectDesignerData.DesignerArena { nodeId = nodeId });
                _selectedArenaNodeId = nodeId;
                RefreshArenaLeadIns(_data.arenas.Find(a => a.nodeId == nodeId));
            }
            else if (!on && _data.arenas.Exists(a => a.nodeId == nodeId))
            {
                RemoveArena(nodeId);
            }
        }

        MarkDirty();
        Repaint();
    }

    // ── Selected node: Add Pool / Add Outpost ─────────────────────
    /// <summary>
    /// Buttons for the selected node, when it sits on a river: make it a pool (or take the pool
    /// away), or stand an outpost beside the river at it.
    /// </summary>
    private void DrawSelectedNodeActions()
    {
        if (_selectedNodeId == null) return;
        string nodeId = _selectedNodeId;

        var path = _data.paths.Find(p => p.pathId == _selectedPathId && p.nodeIds.Contains(nodeId))
                ?? _data.paths.Find(p => p.nodeIds.Contains(nodeId));
        if (path == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Selected Node", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        bool hasPool = _data.PoolAt(nodeId) != null;
        if (GUILayout.Button(hasPool ? "Remove Pool" : "Add Pool"))
            TogglePoolAt(nodeId);
        GUI.enabled = path.nodeIds.Count >= 2;
        if (GUILayout.Button("Add Outpost"))
            AddOutpostAt(path, nodeId);
        DrawBridgeButton(path, nodeId);
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
    }

    // The boat reaching this node crossfades whatever is playing (the starting audio, or another node's
    // music) into this node's music.
    private void DrawNodeAudio(string nodeId)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null) return;

        EditorGUILayout.Space(6);
        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.ToggleLeft(new GUIContent("Audio",
            "The boat sailing into the box across the river here crossfades into this node's " +
            "music. The box is built and sized off the river automatically. The starting audio " +
            "(Setup > Audio) never comes back."), node.musicTrigger, EditorStyles.boldLabel);

        var   clip      = node.musicClip;
        bool  looped    = node.musicLooped;
        float crossfade = node.musicCrossfade;
        if (on)
        {
            EditorGUI.indentLevel++;
            clip      = (AudioClip)EditorGUILayout.ObjectField("Music", clip, typeof(AudioClip), false);
            looped    = EditorGUILayout.Toggle(new GUIContent("Looped", "Off = it plays through once."), looped);
            crossfade = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Crossfade",
                "Seconds what's playing fades out while this music fades in."), crossfade));
            if (clip == null)
                EditorGUILayout.LabelField("No music — this node does nothing.", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }
        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Node Audio");
        bool switched       = on != node.musicTrigger;
        node.musicTrigger   = on;
        node.musicClip      = clip;
        node.musicLooped    = looped;
        node.musicCrossfade = crossfade;
        MarkDirty();
        // Only switching Audio moves a box — the clip and fade are read at runtime.
        if (switched) RebuildMusicTriggers();
        Repaint();
    }

    private void TogglePoolAt(string nodeId)
    {
        if (_data.PoolAt(nodeId) != null)
        {
            Undo.RecordObject(_data, "Remove Pool");
            RemovePoolLeadIns(_data.PoolAt(nodeId));
            _data.pools.RemoveAll(p => p.nodeId == nodeId);
            if (_selectedPoolNodeId == nodeId) _selectedPoolNodeId = null;
        }
        else
        {
            Undo.RecordObject(_data, "Add Pool");
            // Off, so a new pool follows the default shape. Pools drawn before there was a
            // default keep their own — see DesignerPool.overrideShape.
            var pool = new LevelSelectDesignerData.DesignerPool
            {
                nodeId        = nodeId,
                overrideShape = false,
            };
            _data.pools.Add(pool);
            _selectedPoolNodeId = nodeId;

            int missed = RefreshPoolLeadIns(pool);
            if (missed > 0)
                _consoleStatusMsg = $"Pool added — {missed} river(s) left without a lead-in (four compass points).";
        }

        MarkDirty();
        Repaint();
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

    // ── Shop ──────────────────────────────────────────────────────
    // Set per node from its row in the Nodes list: Add Shop / Remove Shop.
    private void ToggleShopAt(string nodeId)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null) return;

        Undo.RecordObject(_data, "Toggle Shop");
        if (node.type == LevelSelectDesignerData.NodeType.ShopEnd)
        {
            node.type = LevelSelectDesignerData.NodeType.Waypoint;
            _data.shops.RemoveAll(s => s.nodeId == nodeId);
            _selectedShopNodeId = null;
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
    }

    // ══════════════════════════════════════════════════════════════
    // CANVAS DRAWING
    // ══════════════════════════════════════════════════════════════
    private void DrawPaths()
    {
        foreach (var path in _data.paths)
        {
            if (path.nodeIds.Count < 2) continue;

            bool  selected = path.pathId == _selectedPathId || IsRiverSelected(path.pathId);
            Color col      = LevelSelectDesignerSettings.RiverColour(path.editorColor);
            float width    = selected ? 3f : 2f;

            if (Event.current.type == EventType.Repaint)
                DrawPathRims(path, selected);

            for (int i = 0; i < path.nodeIds.Count - 1; i++)
            {
                var nodeA = _data.nodes.Find(n => n.id == path.nodeIds[i]);
                var nodeB = _data.nodes.Find(n => n.id == path.nodeIds[i + 1]);
                if (nodeA == null || nodeB == null) continue;

                Vector2 a = WorldToCanvas(nodeA.worldPosition);
                Vector2 b = WorldToCanvas(nodeB.worldPosition);

                Handles.color = selected ? Color.white : col;
                DrawSolidLine(width, (Vector3)a, (Vector3)b);

                // Direction arrow at midpoint — tip at mid+dir, fins back toward mid
                DrawRiverArrow((a + b) * 0.5f, (b - a).normalized, selected ? Color.white : ArrowColour(path.editorColor));
            }
        }
    }

    /// <summary>
    /// The run's two rims, drawn to scale from its river's profile: each a solid band
    /// rimWidth wide, standing innerWidth apart, following the drawn path. Ends that sit on
    /// a pool stop at the pool's outer rim rather than crossing the water.
    /// </summary>
    private void DrawPathRims(LevelSelectDesignerData.DesignerPath path, bool selected)
    {
        var points = new List<Vector3>(path.nodeIds.Count);
        foreach (string id in path.nodeIds)
        {
            var node = _data.nodes.Find(n => n.id == id);
            if (node == null) return;
            points.Add(node.worldPosition);
        }

        TrimRimEndAtPool(points, path.nodeIds[0], 0, 1);
        TrimRimEndAtPool(points, path.nodeIds[path.nodeIds.Count - 1], points.Count - 1, points.Count - 2);

        var   profile = _data.ProfileFor(path.riverName);
        float water   = profile.innerWidth * 0.5f;
        float thick   = LevelSelectDesignerSettings.RiverOutlineWidth;

        // Solid fill across the whole run, outer edge to outer edge, under the rims.
        if (LevelSelectDesignerSettings.RiverFill)
        {
            float   edge  = profile.innerWidth * 0.5f + profile.rimWidth;
            var     left  = RimCanvasPoints(points, edge);
            var     right = RimCanvasPoints(points, -edge);
            Color   fill  = LevelSelectDesignerSettings.RiverColour(path.editorColor);
            fill.a = LevelSelectDesignerSettings.RiverFillOpacity;
            Handles.color = fill;
            for (int i = 0; i < left.Length - 1; i++)
                Handles.DrawAAConvexPolygon(left[i], left[i + 1], right[i + 1], right[i]);
        }

        if (!LevelSelectDesignerSettings.RiverOutline) return;

        Color col = selected ? Color.white : RiverOutlineColour(path.editorColor);
        if (LevelSelectDesignerSettings.RiverColours) col.a = 1f;
        Handles.color = col;

        // Both edges of each rim — outer and water side — the same two edges a pool's rings draw.
        float outer = water + profile.rimWidth;
        DrawRimLine(points,  outer, thick);
        DrawRimLine(points,  water, thick);
        DrawRimLine(points, -water, thick);
        DrawRimLine(points, -outer, thick);
    }

    // A path end on a pool is pulled back to where the run meets the pool's outer rim.
    private void TrimRimEndAtPool(List<Vector3> points, string nodeId, int end, int neighbour)
    {
        var pool = _data.PoolAt(nodeId);
        if (pool == null) return;

        var   shape = _data.PoolShapeFor(pool);
        float reach = shape.poolRadius + _data.ProfileFor(_data.PoolRiverName(pool)).rimWidth;

        Vector3 dir = points[neighbour] - points[end];
        dir.y = 0f;
        float length = dir.magnitude;
        if (length <= reach) return;
        points[end] += dir / length * reach;
    }

    // One rim: the path offset sideways by 'offset' world units (right is Cross(up, forward),
    // the frame the run is swept on), mitred at each bend so the band keeps its width.
    private void DrawRimLine(List<Vector3> points, float offset, float thickness)
    {
        DrawSolidLine(thickness, RimCanvasPoints(points, offset));
    }

    // The path offset sideways on the canvas, mitred at each bend — shared by the rims and the fill.
    private Vector3[] RimCanvasPoints(List<Vector3> points, float offset)
    {
        int count  = points.Count;
        var canvas = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 right = Vector3.zero;
            if (i > 0)         right += SegmentRight(points[i - 1], points[i]);
            if (i < count - 1) right += SegmentRight(points[i], points[i + 1]);
            if (right.sqrMagnitude < 1e-8f) right = i > 0 ? SegmentRight(points[i - 1], points[i]) : Vector3.zero;
            right.Normalize();

            // Mitre: push the corner out so both neighbouring edges stay 'offset' away.
            float scale = 1f;
            if (i > 0)
            {
                float cos = Vector3.Dot(right, SegmentRight(points[i - 1], points[i]));
                scale = 1f / Mathf.Max(0.25f, cos);
            }

            canvas[i] = WorldToCanvas(points[i] + right * (offset * scale));
        }

        return canvas;
    }

    private static Vector3 SegmentRight(Vector3 a, Vector3 b)
    {
        Vector3 forward = b - a;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-8f) return Vector3.zero;
        return Vector3.Cross(Vector3.up, forward.normalized);
    }

    /// <summary>
    /// Draws each pool the way the geometry will read on the ground: the outer rim, the water
    /// edge, the plinth in the middle, and the ring a boat loops the pool on — drawn in the
    /// same line and arrow style as a river so the route reads as one continuous journey.
    /// </summary>
    private void DrawPools()
    {
        if (Event.current.type != EventType.Repaint) return;

        foreach (var pool in _data.pools)
        {
            if (pool == null || string.IsNullOrEmpty(pool.nodeId)) continue;
            var node = _data.nodes.Find(n => n.id == pool.nodeId);
            if (node == null) continue;

            Vector2 centre  = WorldToCanvas(node.worldPosition);
            var     profile = _data.ProfileFor(_data.PoolRiverName(pool));
            bool    selected = pool.nodeId == _selectedPoolNodeId;

            var   shape  = _data.PoolShapeFor(pool);
            float outer  = (shape.poolRadius + profile.rimWidth) * _zoom;
            float water  = shape.poolRadius  * _zoom;
            float island = shape.islandRadius * _zoom;

            Color poolCol = LevelSelectDesignerSettings.RiverColour(pool.editorColor);

            // Solid fill to the outer rim, under the lines — matches the river fill.
            if (LevelSelectDesignerSettings.RiverFill)
            {
                Handles.color = new Color(poolCol.r, poolCol.g, poolCol.b, LevelSelectDesignerSettings.RiverFillOpacity);
                Handles.DrawSolidDisc(centre, Vector3.forward, outer);
            }

            // Rim, outer edge then water edge — the two banks a river has, carried round.
            bool  outline = LevelSelectDesignerSettings.RiverOutline;
            float lineW   = LevelSelectDesignerSettings.RiverOutlineWidth;
            Color lineCol = selected ? Color.white : RiverOutlineColour(pool.editorColor);
            if (outline)
            {
                Handles.color = lineCol;
                DrawThickCircle(centre, outer, lineW);
                DrawThickCircle(centre, water, lineW);
            }

            // The plinth in the middle, filled so it reads as land rather than another bank.
            if (island > 0.5f)
            {
                Handles.color = new Color(poolCol.r, poolCol.g, poolCol.b, 0.25f);
                Handles.DrawSolidDisc(centre, Vector3.forward, island);
                if (outline)
                {
                    Handles.color = lineCol;
                    DrawThickCircle(centre, island, lineW);
                }
            }

            DrawPoolRoute(pool, centre, selected);

            // Where each river breaks through the rim.
            foreach (var (path, neighbourId) in _data.PathsAtPool(pool))
            {
                Vector3 outward = _data.NodeWorldPosition(neighbourId) - node.worldPosition;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.0001f) continue;
                outward.Normalize();

                Vector2 dir = (WorldToCanvas(node.worldPosition + outward) - centre).normalized;
                Handles.color = LevelSelectDesignerSettings.RiverColour(path.editorColor);
                DrawSolidLine(LevelSelectDesignerSettings.RiverOutlineWidth,
                                       (Vector3)(centre + dir * water), (Vector3)(centre + dir * outer));
            }
        }
    }

    // The loop a boat travels the pool on: the pool's deepest ring, drawn like a river path
    // with arrows round it. An open bowl has no ring — there is nothing to circle.
    private void DrawPoolRoute(LevelSelectDesignerData.DesignerPool pool, Vector2 centre, bool selected)
    {
        var   shape    = _data.PoolShapeFor(pool);
        float channelR = RiverMeshBuilder.PoolChannelRadius(shape.poolRadius, shape.islandRadius) * _zoom;
        if (channelR < 1f) return;

        const int steps = 48;
        var loop = new Vector3[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float a = 2f * Mathf.PI * i / steps;
            loop[i] = centre + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * channelR;
        }

        Handles.color = selected ? Color.white : LevelSelectDesignerSettings.RiverColour(pool.editorColor);
        DrawSolidLine(selected ? 3f : 2f, loop);

        // Direction arrows at the quarters, the same shape DrawPaths puts at a path midpoint.
        Color arrowCol = selected ? Color.white : ArrowColour(pool.editorColor);
        for (int q = 0; q < 4; q++)
        {
            float   a  = 2f * Mathf.PI * q / 4f;
            Vector2 at = centre + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * channelR;
            DrawRiverArrow(at, new Vector2(Mathf.Cos(a), -Mathf.Sin(a)), arrowCol);
        }
    }

    // A direction arrow: tip at 'at' + Arrow Size along 'dir', fins back toward 'at'.
    // Size, width, colour and on/off from Level Select Settings.
    private static void DrawRiverArrow(Vector2 at, Vector2 dir, Color colour)
    {
        if (!LevelSelectDesignerSettings.RiverArrows) return;
        Vector2 tip  = dir * LevelSelectDesignerSettings.RiverArrowSize;
        Vector2 perp = new Vector2(-tip.y, tip.x) * 0.4f;
        float   w    = LevelSelectDesignerSettings.RiverArrowWidth;
        Handles.color = colour;
        DrawSolidLine(w, (Vector3)(at + tip), (Vector3)(at - perp));
        DrawSolidLine(w, (Vector3)(at + tip), (Vector3)(at + perp));
    }

    // A river or pool's arrow colour: its own colour while River Colours is on, else Arrow Colour.
    private static Color ArrowColour(Color riverColour)
        => LevelSelectDesignerSettings.RiverColours ? riverColour : LevelSelectDesignerSettings.RiverArrowColour;

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
            if (LevelSelectDesignerSettings.ArenaFill)
            {
                Handles.color = LevelSelectDesignerSettings.ArenaFillColour;
                Handles.DrawSolidDisc(center, Vector3.forward, arenaR * _zoom);
            }
            if (LevelSelectDesignerSettings.ArenaOutline)
            {
                float lineW = LevelSelectDesignerSettings.ArenaOutlineWidth;
                Handles.color = LevelSelectDesignerSettings.ArenaOutlineColour;
                DrawThickCircle(center, arenaR * _zoom, lineW);
                Handles.color = LevelSelectDesignerSettings.ArenaOuterRingColour;
                DrawThickCircle(center, arenaR * _zoom * 1.15f, lineW);
            }

            // Line from arena centre to each secondary entrance orbit node
            foreach (var sec in arena.secondaryEntrances)
            {
                var sn = _data.nodes.Find(n => n.id == sec.nodeId);
                if (sn == null) continue;
                Vector2 orbitCanvas = WorldToCanvas(sn.worldPosition);
                Handles.color = new Color(0.2f, 0.9f, 1f, 0.6f);
                DrawSolidLine(1.5f, (Vector3)center, (Vector3)orbitCanvas);
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
                DrawSolidLine(2f, (Vector3)outer, (Vector3)inner);
                DrawSolidLine(2f, (Vector3)inner, (Vector3)(back + perp));
                DrawSolidLine(2f, (Vector3)inner, (Vector3)(back - perp));
            }
        }
    }

    private readonly HashSet<string> _selectedLeadIns = new();

    private void DrawNodes()
    {
        // A node's lead-ins are drawn selected along with it.
        CollectLeadInsOf(_selectedNodeId, _selectedLeadIns);

        // Two passes: waypoints/arenas/shops first, junctions on top
        foreach (var node in _data.nodes
            .Where(n => n.type != LevelSelectDesignerData.NodeType.JunctionSplit)
            .Concat(_data.nodes.Where(n => n.type == LevelSelectDesignerData.NodeType.JunctionSplit)))
        {
            Vector2 pos = WorldToCanvas(node.worldPosition);
            if (!_canvasRect.Contains(pos)) continue;

            bool selected = node.id == _selectedNodeId || _selectedLeadIns.Contains(node.id) ||
                            IsNodeMultiSelected(node.id);
            float r       = NODE_RADIUS;

            switch (node.type)
            {
                case LevelSelectDesignerData.NodeType.JunctionSplit:
                    bool  juncHighlighted = node.id == _selectedJunctionNodeId;
                    float js = LevelSelectDesignerSettings.JunctionSize;
                    float jw = LevelSelectDesignerSettings.JunctionWidth;
                    Color jh = LevelSelectDesignerSettings.JunctionHighlightColour;
                    Handles.color = juncHighlighted ? jh : LevelSelectDesignerSettings.JunctionColour;
                    DrawDiamond(pos, js, jw);
                    if (selected)         { Handles.color = LevelSelectDesignerSettings.JunctionSelColour; DrawDiamond(pos, js + 3f, jw); }
                    if (juncHighlighted)  { Handles.color = jh; DrawDiamond(pos, js + 6f, jw);
                                           DrawThickCircle(pos, js + 10f, jw); }
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
                    Handles.color = LevelSelectDesignerSettings.RiverColours ? GetNodeColor(node.id)
                                                                             : LevelSelectDesignerSettings.NodeDotColour;
                    Handles.DrawSolidDisc(pos, Vector3.forward, LevelSelectDesignerSettings.NodeDotSize);
                    if (selected) DrawNodeSelectedRing(pos);
                    break;
            }

            if ((node.cinematicTrigger || HasTowerCinematic(_data.pools.Find(p => p != null && p.nodeId == node.id))) &&
                LevelSelectDesignerSettings.CameraSymbol) DrawCameraSymbol(pos);
        }

        DrawNodeNumbers();
        DrawRiverNames();
    }

    private static readonly Vector3[] ThickCirclePoints = new Vector3[65];

    // A circle as a thick polyline in the current Handles.color — DrawWireDisc comes out hairline here.
    private static void DrawThickCircle(Vector2 centre, float radius, float width)
    {
        int segs = ThickCirclePoints.Length - 1;
        for (int s = 0; s <= segs; s++)
        {
            float t = s * Mathf.PI * 2f / segs;
            ThickCirclePoints[s] = new Vector3(centre.x + Mathf.Cos(t) * radius, centre.y + Mathf.Sin(t) * radius, 0f);
        }
        DrawSolidLine(width, ThickCirclePoints);
    }

    // A filled line in the current Handles.color. Plain DrawAAPolyLine(width, …) uses Unity's
    // built-in anti-alias texture, whose alpha fades across the line's whole width — so thick
    // lines come out blurry. A solid texture keeps the band filled edge to edge.
    private static void DrawSolidLine(float width, params Vector3[] points)
        => Handles.DrawAAPolyLine(Texture2D.whiteTexture, width, points);

    // A river or pool's outline colour: its own colour while River Colours is on, else Outline Colour.
    private static Color RiverOutlineColour(Color riverColour)
        => LevelSelectDesignerSettings.RiverColours ? riverColour : LevelSelectDesignerSettings.RiverOutlineColour;

    // One thick ring round a selected node dot — size, width and colour from Level Select Settings.
    private void DrawNodeSelectedRing(Vector2 pos)
    {
        Handles.color = LevelSelectDesignerSettings.NodeSelColour;
        DrawThickCircle(pos, LevelSelectDesignerSettings.NodeSelRingSize, LevelSelectDesignerSettings.NodeSelRingWidth);
    }

    private static readonly Vector3[] CameraBodyPoints = new Vector3[4];
    private static readonly Vector3[] CameraLensPoints = new Vector3[4];

    // A little camera above a node with Cinematic on, clear of the selected ring — size and colour
    // from Level Select Settings. A dark copy one pixel off underneath, as the labels have.
    private static void DrawCameraSymbol(Vector2 nodePos)
    {
        float w   = LevelSelectDesignerSettings.CameraSymbolSize;
        float h   = w * 0.6f;
        float gap = Mathf.Max(LevelSelectDesignerSettings.NodeDotSize,
                              LevelSelectDesignerSettings.NodeSelRingSize + LevelSelectDesignerSettings.NodeSelRingWidth * 0.5f) + 3f;
        var   c   = new Vector2(nodePos.x, nodePos.y - gap - h * 0.5f);

        Handles.color = Color.black;
        DrawCameraShape(c + Vector2.one, w, h);
        Handles.color = LevelSelectDesignerSettings.CameraSymbolColour;
        DrawCameraShape(c, w, h);
    }

    // Body box on the left, lens widening out to the right.
    private static void DrawCameraShape(Vector2 c, float w, float h)
    {
        float x0 = c.x - w * 0.5f, x1 = x0 + w * 0.68f, x2 = x1 + w * 0.06f, x3 = c.x + w * 0.5f;
        float y0 = c.y - h * 0.5f, y1 = c.y + h * 0.5f;

        CameraBodyPoints[0] = new Vector3(x0, y0); CameraBodyPoints[1] = new Vector3(x1, y0);
        CameraBodyPoints[2] = new Vector3(x1, y1); CameraBodyPoints[3] = new Vector3(x0, y1);
        Handles.DrawAAConvexPolygon(CameraBodyPoints);

        CameraLensPoints[0] = new Vector3(x2, c.y - h * 0.15f); CameraLensPoints[1] = new Vector3(x3, y0);
        CameraLensPoints[2] = new Vector3(x3, y1);              CameraLensPoints[3] = new Vector3(x2, c.y + h * 0.15f);
        Handles.DrawAAConvexPolygon(CameraLensPoints);
    }

    private GUIStyle _nodeNumberStyle;
    private GUIStyle _riverNameStyle;

    /// <summary>
    /// River names beside the rivers — the selected river's (every picked one in River Select); in
    /// Souls mode every river's. Each sits off the middle of its river's longest stretch, clear of
    /// the rim, so it never covers a node or the direction arrow.
    /// </summary>
    private void DrawRiverNames()
    {
        if (Event.current.type != EventType.Repaint) return;

        _riverNameStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };

        foreach (var path in _data.paths)
        {
            if (path.nodeIds.Count < 2) continue;
            if (_mode != DesignerMode.SoulRoute && path.pathId != _selectedPathId && !IsRiverSelected(path.pathId)) continue;

            // The longest stretch on the canvas has the most room for the name.
            Vector2 a = default, b = default;
            float   best = -1f;
            for (int i = 0; i < path.nodeIds.Count - 1; i++)
            {
                var nodeA = _data.nodes.Find(n => n.id == path.nodeIds[i]);
                var nodeB = _data.nodes.Find(n => n.id == path.nodeIds[i + 1]);
                if (nodeA == null || nodeB == null) continue;

                Vector2 pa = WorldToCanvas(nodeA.worldPosition);
                Vector2 pb = WorldToCanvas(nodeB.worldPosition);
                float   d  = (pb - pa).sqrMagnitude;
                if (d > best) { best = d; a = pa; b = pb; }
            }
            if (best <= 0f) continue;

            Vector2 mid = (a + b) * 0.5f;
            if (!_canvasRect.Contains(mid)) continue;

            var text = new GUIContent(string.IsNullOrEmpty(path.riverName) ? path.pathId : path.riverName);
            var size = _riverNameStyle.CalcSize(text);

            // Out past the rim on one side: as far again as the label reaches towards the river,
            // so its nearest edge stands the same gap off the rim whichever way the river runs.
            var     profile = _data.ProfileFor(path.riverName);
            float   clear   = (profile.innerWidth * 0.5f + profile.rimWidth) * _zoom + 4f;
            Vector2 side    = new Vector2(-(b - a).y, (b - a).x).normalized;
            float   reach   = Mathf.Abs(side.x) * size.x * 0.5f + Mathf.Abs(side.y) * size.y * 0.5f;
            Vector2 centre  = mid + side * (clear + reach);

            DrawShadowedLabel(new Rect(centre.x - size.x * 0.5f, centre.y - size.y * 0.5f, size.x, size.y),
                              text, _riverNameStyle);
        }
    }

    // White text with a dark copy one pixel off underneath, so it reads on light ground and dark.
    private static void DrawShadowedLabel(Rect rect, GUIContent text, GUIStyle style)
    {
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
        style.normal.textColor = Color.white;
        GUI.Label(rect, text, style);
    }

    /// <summary>
    /// Node numbers beside the nodes — the same numbers the soul route's point list gives
    /// ("node 4"). The selected river's nodes; in Souls mode every river's, each node numbered on
    /// the first river holding it, as the point list names it. Drawn to the left, so they never sit
    /// on an arena or shop name.
    /// </summary>
    private void DrawNodeNumbers()
    {
        if (Event.current.type != EventType.Repaint) return;

        if (_mode == DesignerMode.SoulRoute)
        {
            var numbered = new HashSet<string>();
            foreach (var p in _data.paths)
                for (int i = 0; i < p.nodeIds.Count; i++)
                    if (numbered.Add(p.nodeIds[i])) DrawNodeNumber(p.nodeIds[i], i);
            return;
        }

        var path = _data.paths.Find(p => p.pathId == _selectedPathId);
        if (path == null) return;

        for (int i = 0; i < path.nodeIds.Count; i++)
            DrawNodeNumber(path.nodeIds[i], i);
    }

    private void DrawNodeNumber(string nodeId, int number)
    {
        // Where DrawNodes put the dot, not the junction-corrected spot the rivers are built from.
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null) return;

        Vector2 pos = WorldToCanvas(node.worldPosition);
        if (!_canvasRect.Contains(pos)) return;

        _nodeNumberStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };

        var text = new GUIContent(number.ToString());
        var size = _nodeNumberStyle.CalcSize(text);
        var rect = new Rect(pos.x - LevelSelectDesignerSettings.NodeDotSize - 4f - size.x, pos.y - size.y * 0.5f, size.x, size.y);

        DrawShadowedLabel(rect, text, _nodeNumberStyle);
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
            DrawSolidLine(2f,
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
                DrawSolidLine(width, (Vector3)outerPt, (Vector3)innerPt);

                Vector2 perp = new Vector2(-inward.y, inward.x) * 4f;
                Vector2 back = innerPt - inward * 6f;
                DrawSolidLine(width, (Vector3)innerPt, (Vector3)(back + perp));
                DrawSolidLine(width, (Vector3)innerPt, (Vector3)(back - perp));
                Handles.DrawSolidDisc(innerPt, Vector3.forward, 4f);
            }
        }
    }

    // Where the boat is put down on a save that has never seen the map: the hand-set start node,
    // else the head of the main river — or the pool sitting on that node. Read from the same answer the game places the
    // boat with, so the marker cannot drift from where the boat actually turns up.
    private void DrawBoatStart()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (!_data.TryGetBoatStart(out Vector3 world, out Vector3 forward, out _)) return;

        DrawBoatMarker(world, forward, new Color(1f, 0.93f, 0.6f), "BOAT", -6f);
    }

    // A boat-shaped marker on the canvas, its label beside the bow at labelDy.
    private void DrawBoatMarker(Vector3 world, Vector3 forward, Color fill, string label, float labelDy)
    {
        Vector2 at = WorldToCanvas(world);
        if (!_canvasRect.Contains(at)) return;

        // Taken off the canvas rather than off the world, so the hull points down the river
        // whichever way the view has the map round.
        Vector2 fwd = WorldToCanvas(world + forward) - at;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector2.up;
        fwd.Normalize();
        Vector2 side = new Vector2(-fwd.y, fwd.x);

        const float L = 10f;   // bow to stern — a fixed size on screen, as the node markers are

        Vector3 Hull(float along, float across)
            => (Vector3)(at + fwd * (along * L) + side * (across * L));

        var hull = new[]
        {
            Hull( 1.00f,  0f),
            Hull( 0.25f,  0.55f),
            Hull(-0.80f,  0.45f),
            Hull(-0.95f,  0f),
            Hull(-0.80f, -0.45f),
            Hull( 0.25f, -0.55f),
        };

        Handles.color = fill;
        Handles.DrawAAConvexPolygon(hull);

        var outline = new Vector3[hull.Length + 1];
        hull.CopyTo(outline, 0);
        outline[hull.Length] = hull[0];

        Handles.color = Color.black;
        DrawSolidLine(2f, outline);

        Handles.color = Color.white;
        Handles.Label(new Vector3(at.x + L + 4f, at.y + labelDy, 0), label, EditorStyles.miniLabel);
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
    private static void DrawDiamond(Vector2 c, float r, float width = 2f)
    {
        DrawSolidLine(width,
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
        var changes = new List<string>();

        // Fill any empty IDs first
        foreach (var p in _data.paths)
        {
            if (!string.IsNullOrEmpty(p.segmentId)) continue;
            bool hasMain = _data.paths.Any(x =>
                x.segmentType == LevelSelectDesignerData.SegmentType.MainRiver &&
                !string.IsNullOrEmpty(x.segmentId));
            p.segmentId = NextSegmentId(hasMain ? "Branch" : "Main");
            changes.Add($"(empty) → {p.segmentId}");
        }

        // Resolve duplicates — the first keeps its ID, every later one gets a fresh system ID
        // under the same prefix ("Branch_03" twice → the second becomes the next free Branch_NN).
        var grouped = _data.paths.GroupBy(p => p.segmentId).Where(g => g.Count() > 1).ToList();
        foreach (var g in grouped)
        {
            var    m      = System.Text.RegularExpressions.Regex.Match(g.Key, @"^(.+)_\d+$");
            string prefix = m.Success ? m.Groups[1].Value
                          : g.First().segmentType == LevelSelectDesignerData.SegmentType.MainRiver ? "Main"
                          : "Branch";
            foreach (var p in g.Skip(1))
            {
                p.segmentId = NextSegmentId(prefix);
                changes.Add($"{g.Key} → {p.segmentId}");
            }
        }

        Debug.Log(changes.Count == 0
            ? "[LevelSelectDesigner] Branch IDs: nothing to fix — every ID is filled and unique."
            : $"[LevelSelectDesigner] Branch IDs fixed ({changes.Count}): {string.Join(", ", changes)}. " +
              "Regenerate so scene objects pick up the new IDs.");

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
        RemoveJunctionLeadIns(_data.junctions.Find(j => j.nodeId == junc.nodeId));
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
                DrawSetupSubFoldout(ref _foldSetupArenas,    "Arena Prefabs",         DrawArenaPrefabsSection);
                DrawSetupSubFoldout(ref _foldSetupObstacles, "Obstacle & Shop",       DrawObstacleShopPrefabsSection);
                DrawSetupSubFoldout(ref _foldSetupCore,      "Core & World",          DrawCorePrefabsSection);
                DrawSetupSubFoldout(ref _foldSetupAudio,     "Audio",                 DrawAudioSection);
                DrawSetupSubFoldout(ref _foldSetupFog,       "Fog",                   DrawFogSection);
                DrawSetupSubFoldout(ref _foldSetupCinematics, "Cinematics",           DrawCameraSequenceSection);
                DrawSetupSubFoldout(ref _foldSetupPlaytest,  "Playtest",              DrawPlaytestSection);

                EditorGUI.indentLevel--;
            }
            DrawBoatStartSection();
            EditorGUILayout.Space(2);

            // ── Paths ─────────────────────────────────────────────
            // Everything the level's geometry is generated from: a default shape per kind,
            // and an override for the instances that want their own.
            _foldProcGen = EditorGUILayout.Foldout(_foldProcGen, "Procedural Generation", true, EditorStyles.foldoutHeader);
            if (_foldProcGen)
            {
                EditorGUI.indentLevel++;
                DrawProcGenDropField();
                DrawSetupSubFoldout(ref _foldProcGenRivers, "River Runs",  DrawProcGenRiversSection);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(2);

            // ── Aesthetics ────────────────────────────────────
            _foldAesthetics = EditorGUILayout.Foldout(_foldAesthetics, "Aesthetics", true, EditorStyles.foldoutHeader);
            if (_foldAesthetics)
            {
                EditorGUI.indentLevel++;
                DrawAestheticsSection();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(2);

            _foldPaths = EditorGUILayout.Foldout(_foldPaths, $"Paths ({_data.paths.Count})", true, EditorStyles.foldoutHeader);
            if (_foldPaths) DrawPathList();
            EditorGUILayout.Space(2);

            // ── Junctions ─────────────────────────────────────────
            _foldJunctions = EditorGUILayout.Foldout(_foldJunctions, "Junctions", true, EditorStyles.foldoutHeader);
            if (_foldJunctions)
            {
                DrawJunctionLeadInDistance();
                DrawJunctionsList(showHeader: false);
            }
            EditorGUILayout.Space(2);

            // ── Arenas ────────────────────────────────────────────
            _foldArenas = EditorGUILayout.Foldout(_foldArenas, $"Arenas ({_data.arenas.Count})", true, EditorStyles.foldoutHeader);
            if (_foldArenas)
            {
                // The arena walls and entrance archways every arena generates sit with the
                // arenas themselves, as the pools' defaults do with the pools.
                EditorGUI.indentLevel++;
                DrawSetupSubFoldout(ref _foldProcGenArenas,    "Arena Walls", DrawProcGenArenaWallsSection);
                DrawSetupSubFoldout(ref _foldProcGenEntrances, "Entrances",   DrawProcGenEntrancesSection);
                EditorGUI.indentLevel--;
                DrawArenasList();
            }
            EditorGUILayout.Space(2);

            // ── Pools ─────────────────────────────────────────────
            _foldPools = EditorGUILayout.Foldout(_foldPools, $"Pools ({_data.pools.Count})", true, EditorStyles.foldoutHeader);
            if (_foldPools)
            {
                // The procedural defaults every pool takes sit with the pools themselves; each
                // pool's own override shows in the left panel when it is selected.
                EditorGUI.indentLevel++;
                DrawSetupSubFoldout(ref _foldProcGenPools, "Procedural Defaults", DrawProcGenPoolsSection);
                EditorGUI.indentLevel--;
                DrawPoolsList();
            }
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

    private void DrawPoolsList()
    {
        EditorGUILayout.LabelField(
            "Select a node on a river and press Add Pool. Island 0 = open pool.",
            EditorStyles.miniLabel);

        if (_data.pools.Count == 0)
        {
            EditorGUILayout.LabelField("  (none)", EditorStyles.miniLabel);
            return;
        }

        for (int i = _data.pools.Count - 1; i >= 0; i--)
        {
            var  pool     = _data.pools[i];
            bool selected = pool.nodeId == _selectedPoolNodeId;
            int  arrivals = _data.PathsAtPool(pool).Count;

            var prevBg = GUI.backgroundColor;
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = selected ? pool.editorColor : Color.clear;

            string shape = PoolKindLabel(pool, arrivals);
            string own   = pool.overrideShape ? "  · own shape" : "";

            if (GUILayout.Button($"{shape}  ({arrivals} river{(arrivals == 1 ? "" : "s")}){own}",
                                 EditorStyles.miniButton))
            {
                _selectedPoolNodeId  = selected ? null : pool.nodeId;
                _selectedNodeId      = pool.nodeId;
                _selectedArenaNodeId = null;
                _selectedShopNodeId  = null;
                _selectedObstacleId  = null;
                Repaint();
            }
            GUI.backgroundColor = prevBg;

            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                Undo.RecordObject(_data, "Remove Pool");
                if (_selectedPoolNodeId == pool.nodeId) _selectedPoolNodeId = null;
                RemovePoolLeadIns(pool);
                _data.pools.RemoveAt(i);
                MarkDirty();
                EditorGUILayout.EndHorizontal();
                continue;
            }
            EditorGUILayout.EndHorizontal();
        }
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

        // A move is noted and acted on once the list has finished drawing, rather than the moment
        // the arrow is pressed — reordering under a loop that is still walking the list would
        // draw the rest of it against an order that no longer holds.
        int moveFrom = -1, moveTo = -1;

        // Read in numbering order, 1 at the top, because that order is the numbering: it is what
        // allocates the numeral carved on each arena's door, and a list that shows it backwards
        // cannot be reordered by eye. Walked forwards for the same reason — a row acts on the
        // list and then stops, so there is nothing here that needs the safe-delete direction.
        for (int i = 0; i < _data.arenas.Count; i++)
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

            // The arena's number leads — it is what is carved on its door, and this list's own
            // order is what allocates it.
            var row = new GUIContent(
                $"{(selected ? "● " : "  ")}{i + 1}.  {label}",
                $"Arena {i + 1}. Its door carries that numeral. Move it up or down the list to " +
                $"give it a different one.");

            if (GUILayout.Button(row, EditorStyles.miniButton))
            {
                _selectedArenaNodeId = selected ? null : arena.nodeId;
                _selectedEntranceIdx = -1;
                _foldPathProps       = true;
                Repaint();
            }

            GUI.backgroundColor = prevBg;

            // Up and down the list is up and down the numbering, so an arena is given the
            // number you want by moving it to that place. Nothing else about an arena changes:
            // only where it sits in the list, which is the whole of what a number is.
            using (new EditorGUI.DisabledScope(i == 0))
                if (GUILayout.Button(new GUIContent("▲", $"Make this arena {i}"),
                                     EditorStyles.miniButton, GUILayout.Width(20)))
                    { moveFrom = i; moveTo = i - 1; }

            using (new EditorGUI.DisabledScope(i == _data.arenas.Count - 1))
                if (GUILayout.Button(new GUIContent("▼", $"Make this arena {i + 2}"),
                                     EditorStyles.miniButton, GUILayout.Width(20)))
                    { moveFrom = i; moveTo = i + 1; }

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

        if (moveFrom >= 0) MoveArena(moveFrom, moveTo);
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
        string pathLabel   = assignedPath != null ? PathDisplayName(assignedPath) : "(none)";

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
                // A "/" in a lore name would open a submenu, so it's swapped for a lookalike.
                string name = PathDisplayName(capturedPath).Replace('/', '∕');
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

    // The one setting every junction's lead-ins share.
    private void DrawJunctionLeadInDistance()
    {
        EditorGUI.BeginChangeCheck();
        float distance = EditorGUILayout.FloatField(
            new GUIContent("Lead-in Distance",
                "How far each junction's lead-in nodes stand out past its mouth — along the river " +
                "from the edge of the opening, along the branch from where it is cut off. 0 puts " +
                "them right on the edge."),
            _data.junctionLeadInDistance);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Junction Lead-in Distance");
            _data.junctionLeadInDistance = Mathf.Max(0f, distance);
            MarkDirty();
        }
    }

    private void DrawJunctionsList(bool showHeader = true)
    {
        // Build effective junctions the same way Generate does
        var nodeCounts = new Dictionary<string, int>();
        foreach (var p in _data.paths)
            foreach (var nid in p.nodeIds)
                nodeCounts[nid] = nodeCounts.TryGetValue(nid, out int c) ? c + 1 : 1;

        // A river split is two rivers meeting end to end, not a junction.
        var allJunctions = new List<LevelSelectDesignerData.DesignerJunction>(_data.junctions);
        foreach (var kvp in nodeCounts)
            if (kvp.Value >= 2 && !allJunctions.Exists(j => j.nodeId == kvp.Key) &&
                !_data.RiverJoinPaths(kvp.Key, out _, out _))
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

            // ── Lead-ins — only a junction that's been named has any ──
            if (_data.junctions.Contains(junc))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Lead-ins: {junc.leadIns?.Count ?? 0}", EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Refresh Lead-ins",
                        "Gives every river next to this junction a lead-in at the junction's height: " +
                        "one either side on the river running through, one on the branch."),
                        EditorStyles.miniButton, GUILayout.Width(110)))
                {
                    Undo.RecordObject(_data, "Refresh Junction Lead-ins");
                    RefreshJunctionLeadIns(junc);
                    _consoleStatusMsg = $"Junction lead-ins refreshed ({junc.leadIns.Count}).";
                    MarkDirty();
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }

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
                if (GUILayout.Button(PathDisplayName(p) + role, EditorStyles.miniButton))
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

        if (GUILayout.Button(new GUIContent("Refresh Branch IDs",
                "Fills any empty ID and gives every duplicate a fresh system ID. The first path " +
                "with an ID keeps it. Lore names are untouched. Undoable."), EditorStyles.miniButton))
            RiverIdCleanup();

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
                string label = PathDisplayName(path);
                if (!string.IsNullOrEmpty(path.segmentId) && SegmentIdTaken(path.segmentId, path))
                    label += "  ⚠ duplicate ID — Refresh Branch IDs";
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
                segmentId = NextSegmentId("Segment"),
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
            EditorGUILayout.LabelField(path != null ? PathDisplayName(path) : "(unknown path)", EditorStyles.miniLabel);

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
        EditorGUILayout.LabelField($"Pools:     {_data.pools.Count}",     EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Zoom:      {_zoom:F1}px/u",          EditorStyles.miniLabel);

        DrawSceneStats();
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
            if (Vector2.Distance(WorldToCanvas(node.worldPosition), canvas) <= Mathf.Max(NODE_RADIUS, LevelSelectDesignerSettings.NodeDotSize) + 4f)
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
        RemovePoolLeadIns(_data.PoolAt(nodeId));
        RemoveArenaLeadIns(_data.arenas.Find(a => a.nodeId == nodeId));
        RemoveJunctionLeadIns(_data.junctions.Find(j => j != null && j.nodeId == nodeId));
        _data.nodes.RemoveAll(n => n.id == nodeId);
        foreach (var p in _data.paths) p.nodeIds.Remove(nodeId);
        _data.paths.RemoveAll(p => p.nodeIds.Count < 2);
        _data.junctions.RemoveAll(j => j.nodeId == nodeId);
        _data.riverJoins?.RemoveAll(j => j == null || j.nodeId == nodeId);
        _data.arenas.RemoveAll(a => a.nodeId == nodeId);
        _data.pools.RemoveAll(p => p.nodeId == nodeId);
        _data.shops.RemoveAll(s => s.nodeId == nodeId);
        _data.rimNodes?.RemoveAll(r => r.nodeId == nodeId);
    }

    // What sits on a node, for the Nodes list: pool, rim node, lead-in, arena, shop, junction,
    // and any other river that shares it.
    private string NodeDetails(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        var parts = new List<string>();
        var node  = _data.nodes.Find(n => n.id == nodeId);
        if (_data.PoolAt(nodeId) != null)                   parts.Add("Pool");
        if (_data.RimNodeAt(nodeId) != null)                parts.Add("Rim node");
        if (_data.PoolOfLeadIn(nodeId) != null)             parts.Add("Pool lead-in");
        if (_data.ArenaOfLeadIn(nodeId) != null)            parts.Add("Arena lead-in");
        if (_data.JunctionOfLeadIn(nodeId) != null)         parts.Add("Junction lead-in");
        if (_data.arenas.Exists(a => a.nodeId == nodeId))   parts.Add("Arena");
        if (node?.type == LevelSelectDesignerData.NodeType.ShopEnd) parts.Add("Shop");
        if (_data.junctions.Exists(j => j.nodeId == nodeId)) parts.Add("Junction");
        if (_data.RiverJoinAt(nodeId) != null)               parts.Add("River split");
        if (_data.outposts.Exists(o => o.pathId == path.pathId && OutpostNodeId(o) == nodeId))
            parts.Add("Outpost");
        if (_data.BridgeAt(nodeId) != null)                  parts.Add("Bridge");
        foreach (var other in _data.paths)
            if (other != path && other.nodeIds.Contains(nodeId))
                parts.Add($"Joins {other.segmentId}");
        return parts.Count > 0 ? string.Join(", ", parts) : "—";
    }

    // A river end can only be carried on when nothing is anchored to it — otherwise the new
    // node would pull the river off its pool, arena, shop or junction.
    private bool IsFreeEnd(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null || IsLockedNode(nodeId)) return false;
        if (node.type == LevelSelectDesignerData.NodeType.ArenaEnd ||
            node.type == LevelSelectDesignerData.NodeType.ShopEnd) return false;
        if (_data.PoolAt(nodeId) != null) return false;
        if (_data.arenas.Exists(a => a.nodeId == nodeId)) return false;
        if (_data.junctions.Exists(j => j.nodeId == nodeId)) return false;
        return !_data.paths.Exists(p => p != path && p.nodeIds.Contains(nodeId));
    }

    // Whether a node can go in at `at` (0 = off the start, Count = off the end, else between
    // at-1 and at). Never between a lead-in and the pool or arena it is locked to.
    private bool CanInsertNodeAt(LevelSelectDesignerData.DesignerPath path, int at)
    {
        var ids = path.nodeIds;
        if (ids.Count < 2) return false;
        if (at <= 0)         return WhyCannotCarryOnFrom(path, ids[0]) == null;
        if (at >= ids.Count) return WhyCannotCarryOnFrom(path, ids[ids.Count - 1]) == null;

        string a = ids[at - 1], b = ids[at];
        bool LockedPair(string leadIn, string owner) =>
            _data.PoolOfLeadIn(leadIn)?.nodeId == owner || _data.ArenaOfLeadIn(leadIn)?.nodeId == owner ||
            _data.JunctionOfLeadIn(leadIn)?.nodeId == owner;
        return !LockedPair(a, b) && !LockedPair(b, a);
    }

    /// <summary>
    /// Adds a waypoint at `at` in the river: halfway between the two nodes either side, or off
    /// an end by half the end stretch's length. Gates, outposts and shops keep their places.
    /// </summary>
    private LevelSelectDesignerData.DesignerNode InsertNodeInPath(LevelSelectDesignerData.DesignerPath path, int at)
    {
        var ids = path.nodeIds;

        if (at > 0 && at < ids.Count)
        {
            Vector3 mid  = (_data.NodeWorldPosition(ids[at - 1]) + _data.NodeWorldPosition(ids[at])) * 0.5f;
            var     node = AddNode(mid, LevelSelectDesignerData.NodeType.Waypoint);
            InsertNodeBetween(path, ids[at - 1], ids[at], node);
            return node;
        }

        bool    atStart = at <= 0;
        Vector3 end     = _data.NodeWorldPosition(atStart ? ids[0] : ids[ids.Count - 1]);
        Vector3 inner   = _data.NodeWorldPosition(atStart ? ids[1] : ids[ids.Count - 2]);
        Vector3 step    = end - inner; step.y = 0f;
        var     added   = AddNode(end + step * 0.5f, LevelSelectDesignerData.NodeType.Waypoint);

        // Fractions along the river are per stretch, so one more stretch slides everything;
        // re-work them so each stays where it was.
        int   segments = ids.Count - 1;
        float Remap(float t) => (t * segments + (atStart ? 1f : 0f)) / (segments + 1);
        foreach (var o in _data.obstacles) if (o.pathId == path.pathId) o.pathT = Remap(o.pathT);
        foreach (var o in _data.outposts)  if (o.pathId == path.pathId) o.pathT = Remap(o.pathT);
        foreach (var s in _data.shops)     if (s.pathId == path.pathId) s.pathT = Remap(s.pathT);

        if (atStart) ids.Insert(0, added.id);
        else         ids.Add(added.id);
        return added;
    }

    /// <summary>
    /// Why a river can't be split at the node at <paramref name="index"/>, or null when it can.
    /// Only a plain node in the river's middle: a split at an end would leave a one-node river,
    /// and a node something else is anchored to would leave that thing on the wrong river.
    /// </summary>
    private string WhyRiverCannotSplitAt(LevelSelectDesignerData.DesignerPath path, int index)
    {
        if (path == null || index <= 0 || index >= path.nodeIds.Count - 1) return "river end";

        string nodeId = path.nodeIds[index];
        if (_data.PoolAt(nodeId) != null)                       return "pool";
        if (IsLockedNode(nodeId))                               return "lead-in";
        if (_data.arenas.Exists(a => a.nodeId == nodeId) ||
            FindArenaForSecondaryNode(nodeId) != null)          return "arena";
        if (_data.junctions.Exists(j => j.nodeId == nodeId) ||
            _data.paths.Exists(p => p != path && p.nodeIds.Contains(nodeId)))
                                                                return "another river meets it";
        return null;
    }

    /// <summary>
    /// Cuts a river in two at the node at <paramref name="index"/>: this river ends there, and a
    /// new one carrying the same settings starts there and runs on to the old end. The node is
    /// marked a river join, so Generate leaves both runs open there and on one heading — the
    /// river looks just as it did, but each half can now be set on its own.
    ///
    /// Whatever was on the second half moves over with it: gates, outposts and shops (their
    /// fractions re-worked so each stays where it was), the junctions it is the river of, and
    /// the landscape influence cancelled on its nodes. The arena goes with whichever half
    /// reaches it.
    /// </summary>
    private LevelSelectDesignerData.DesignerPath SplitRiverAt(LevelSelectDesignerData.DesignerPath path, int index)
    {
        var    original = new List<string>(path.nodeIds);
        string nodeId   = original[index];
        int    segments = original.Count - 1;

        // Every setting the river has — present and future — copied as it stands.
        var tail = JsonUtility.FromJson<LevelSelectDesignerData.DesignerPath>(JsonUtility.ToJson(path));
        tail.pathId  = Guid.NewGuid().ToString();
        tail.nodeIds = original.GetRange(index, original.Count - index);
        path.nodeIds = original.GetRange(0, index + 1);

        // One main river only: the trunk keeps its head (where the boat starts), so the half
        // after the split steps down to a branch.
        if (path.segmentType == LevelSelectDesignerData.SegmentType.MainRiver)
            tail.segmentType = LevelSelectDesignerData.SegmentType.PrimaryBranch;
        var    idMatch = System.Text.RegularExpressions.Regex.Match(path.segmentId ?? "", @"^(.+)_\d+$");
        string prefix  = path.segmentType == LevelSelectDesignerData.SegmentType.MainRiver || !idMatch.Success
                       ? "Branch" : idMatch.Groups[1].Value;
        tail.segmentId = NextSegmentId(prefix);

        // The side flags and T-junction setting describe where a river LEAVES another one;
        // the new half leaves nothing — it carries straight on from the join.
        tail.isLeftPath             = false;
        tail.isRightPath            = false;
        tail.tJunctionBidirectional = false;

        // The arena stays with the half that reaches it.
        if (path.leadsToArena)
        {
            if (path.arenaIsAtEnd) { path.leadsToArena = false; path.arenaGridDataGuid = null; }
            else                   { tail.leadsToArena = false; tail.arenaGridDataGuid = null; }
        }

        path.influenceCancelledNodes?.RemoveAll(id => !path.nodeIds.Contains(id));
        tail.influenceCancelledNodes?.RemoveAll(id => !tail.nodeIds.Contains(id));

        // Fractions along the river are per stretch: one up to the split stays on this river
        // over its own stretches, one past it moves to the new river over its.
        int tailSegments = segments - index;
        void Remap(ref string pid, ref float t)
        {
            if (pid != path.pathId) return;
            float s = Mathf.Clamp01(t) * segments;
            if (s <= index) t = s / index;
            else            { pid = tail.pathId; t = (s - index) / tailSegments; }
        }
        foreach (var o in _data.obstacles) Remap(ref o.pathId, ref o.pathT);
        foreach (var o in _data.outposts)  Remap(ref o.pathId, ref o.pathT);
        foreach (var s in _data.shops)     Remap(ref s.pathId, ref s.pathT);

        // A junction past the split now sits on the new river.
        foreach (var j in _data.junctions)
        {
            if (j == null || original.IndexOf(j.nodeId) <= index) continue;
            if (j.riverPathId  == path.pathId) j.riverPathId  = tail.pathId;
            if (j.branchPathId == path.pathId) j.branchPathId = tail.pathId;
            for (int k = 0; k < j.pathIds.Count; k++)
                if (j.pathIds[k] == path.pathId) j.pathIds[k] = tail.pathId;
        }

        _data.paths.Insert(_data.paths.IndexOf(path) + 1, tail);
        if (_data.riverJoins == null) _data.riverJoins = new List<LevelSelectDesignerData.DesignerRiverJoin>();
        if (_data.RiverJoinAt(nodeId) == null)
            _data.riverJoins.Add(new LevelSelectDesignerData.DesignerRiverJoin { nodeId = nodeId });

        Debug.Log($"[LevelSelectDesigner] Split '{path.segmentId}' at node {index}: " +
                  $"'{path.segmentId}' ({path.nodeIds.Count} nodes) → '{tail.segmentId}' " +
                  $"({tail.nodeIds.Count} nodes). Generate to rebuild the runs.");
        return tail;
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
        _data.outposts.RemoveAll(o => o.pathId == pathId);
        foreach (var j in _data.junctions) j.pathIds.Remove(pathId);
    }

    private Color GetNodeColor(string nodeId)
    {
        var path = _data.paths.Find(p => p.nodeIds.Contains(nodeId));
        return path != null ? LevelSelectDesignerSettings.RiverColour(path.editorColor) : Color.gray;
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
        return NextSegmentId(hasMain ? "Branch" : "Main");
    }

    /// <summary>
    /// The next free "{prefix}_NN": one above the highest number that prefix already uses, so a
    /// deleted path's number is never handed out again and no two paths can share an ID.
    /// </summary>
    private string NextSegmentId(string prefix)
    {
        int next = 0;
        string head = prefix + "_";
        foreach (var p in _data.paths)
        {
            if (p.segmentId == null || !p.segmentId.StartsWith(head)) continue;
            if (int.TryParse(p.segmentId.Substring(head.Length), out int n) && n >= next)
                next = n + 1;
        }
        string id = $"{prefix}_{next:00}";
        while (SegmentIdTaken(id, null)) id = $"{prefix}_{++next:00}";
        return id;
    }

    private bool SegmentIdTaken(string id, LevelSelectDesignerData.DesignerPath except) =>
        _data.paths.Any(p => p != except && p.segmentId == id);

    /// <summary>How a path reads in the panels' lists: its lore name first, the ID beside it.</summary>
    private static string PathDisplayName(LevelSelectDesignerData.DesignerPath path)
    {
        string id = string.IsNullOrEmpty(path.segmentId) ? "(unnamed)" : path.segmentId;
        return string.IsNullOrWhiteSpace(path.loreName) ? id : $"{path.loreName}  ·  {id}";
    }

    // ══════════════════════════════════════════════════════════════
    // GENERATION
    // ══════════════════════════════════════════════════════════════
    private void Generate()
    {
        if (_data == null) { Debug.LogWarning("[LevelSelectDesigner] No data asset."); return; }

        SyncPoolLeadIns();
        SyncArenaLeadIns();
        SyncJunctionLeadIns();
        SyncInBetweenNodes();

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate Level Select");

        // The clear belongs inside the group the rebuild collapses into. Run from the button,
        // outside it, one Ctrl+Z undid the rebuild and left the scene emptied.
        ClearGeneratedObjects(clearScripts: false);

        // Deploy and wire all scene script objects FIRST so managers exist before path generation
        DeployAllSceneObjects();

        var mainVisuals = FindOrCreateParent("MAINRIVERVISUALS");
        var branches    = FindOrCreateParent("RIVERBRANCHES");
        var obstaclesGO = FindOrCreateParent("RIVERGATEsobstacles");
        var poolsGO     = FindOrCreateParent("RIVERPOOLS");

        int generatedRuns = 0;

        // Build effective junctions: explicit ones + any node shared between two or more paths
        var nodeCounts = new Dictionary<string, int>();
        foreach (var p in _data.paths)
            foreach (var nid in p.nodeIds)
                nodeCounts[nid] = nodeCounts.TryGetValue(nid, out int c) ? c + 1 : 1;

        // Not an explicit junction on a pool either — one clicked in Junction mode and later
        // made a pool would otherwise have the river carry straight on through the pool.
        var effectiveJunctions = _data.junctions
            .Where(j => j != null && _data.PoolAt(j.nodeId) == null)
            .ToList();
        foreach (var kvp in nodeCounts)
        {
            // A pool node is where rivers meet the pool, not each other — it gets a mouth cut
            // through the pool's rim instead of a river-to-river junction.
            if (_data.PoolAt(kvp.Key) != null) continue;

            // Nor a river split: the two rivers meet end to end there, neither runs through.
            if (_data.RiverJoinPaths(kvp.Key, out _, out _)) continue;

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
        _junctionBranchStart.Clear();
        _junctionWaterLeadIn.Clear();
        _junctionJoin.Clear();
        _riverJoinHeadings.Clear();
        _poolEdgeDistance.Clear();
        _poolCentre.Clear();
        _poolWaterReach.Clear();
        _poolArrivals.Clear();
        _poolOpenStart.Clear();
        _poolOpenEnd.Clear();

        // Pre-pass: where each pool sits, how far short of it the rivers meeting it stop, and
        // how much further their water has to carry to reach the water lying inside. All three
        // are read while the river splines are built, so they have to be known first.
        foreach (var pool in _data.pools)
        {
            if (pool == null || string.IsNullOrEmpty(pool.nodeId)) continue;
            var node = _data.nodes.Find(n => n.id == pool.nodeId);
            if (node == null) continue;

            // A pool takes its shape from the river running into it — never authored separately.
            var profile = PoolProfileFor(pool);

            _poolCentre[pool.nodeId]       = node.worldPosition;
            _poolEdgeDistance[pool.nodeId] =
                RiverMeshBuilder.PoolEdgeDistance(profile, _data.PoolShapeFor(pool).poolRadius, MeshEdge);
            _poolWaterReach[pool.nodeId]   = PoolWaterReachFor(pool);
        }

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
            // An authored wall radius wins — the wall is the boundary, and the gizmo still holds
            // whatever the LAST generate left on it, which would otherwise undo a radius just
            // changed in Procedural Generation. With none authored, the arena keeps the size it
            // already carries, read back off the gizmo exactly as before.
            float authored = _data.ArenaWallShapeOf(arena)?.radius ?? 0f;
            float radius   = authored > 0.05f
                           ? authored
                           : (gizmo != null ? gizmo.radius
                                            : (arena.arenaRadius > 0.1f ? arena.arenaRadius : 10f));
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

        // Pre-pass: river joins. Both rivers are turned to one heading where they meet — the
        // line from the node before the join to the node after it — so the last ring of one and
        // the first ring of the other are the same section on the same frame.
        if (_data.riverJoins != null)
        {
            foreach (var join in _data.riverJoins)
            {
                if (join == null) continue;
                if (!_data.RiverJoinPaths(join.nodeId, out var ending, out var starting))
                {
                    Debug.LogWarning($"[LevelSelectDesigner] River split at node '{join.nodeId}' " +
                                     "no longer has exactly one river ending there and one starting " +
                                     "there — left as it is, both ends capped.");
                    continue;
                }

                Vector3 heading = WorldPosOfNode(starting.nodeIds[1])
                                - WorldPosOfNode(ending.nodeIds[ending.nodeIds.Count - 2]);
                if (heading.sqrMagnitude > 1e-8f)
                    _riverJoinHeadings[join.nodeId] = heading.normalized;
            }
        }

        _legSourcePathId.Clear();

        foreach (var drawn in _data.paths)
        {
            if (drawn.nodeIds.Count < 2) continue;
            bool isMain = drawn.segmentType == LevelSelectDesignerData.SegmentType.MainRiver;
            var  parent = isMain ? mainVisuals : branches;

            // Split where the river is drawn through a pool, so each leg meets it at an end.
            // Legs are made as each path comes up, not all up front: an earlier river's
            // junction writes its branch's side flags, and a copy made before that would miss it.
            foreach (var path in PoolLegs(drawn))
            {
                // Collect ALL mid-path junctions for this path, sorted by node index
                var pathJunctions = effectiveJunctions
                    .Where(j => { int idx = path.nodeIds.IndexOf(j.nodeId);
                                  return idx > 0 && idx < path.nodeIds.Count - 1; })
                    .OrderBy(j => path.nodeIds.IndexOf(j.nodeId))
                    .ToList();

                if (pathJunctions.Count > 0)
                    GenerateJunctionSplit(path, pathJunctions, parent);
                else
                    GenerateSimpleSegment(path, parent);
                generatedRuns++;
            }
        }

        GeneratePools(poolsGO);
        RebuildPoolTowers();
        RebuildPerchMarkers();
        RebuildSpikes();
        RebuildStructures();
        RebuildFogZones();
        GenerateOutposts(FindOrCreateParent(OutpostsParent));
        GeneratePipes(FindOrCreateParent(PipesParent));
        GenerateWalls(FindOrCreateParent(WallsParent));
        GenerateObstacles(obstaclesGO);
        GenerateArenaWalls(FindOrCreateParent("ARENAS"));
        GenerateArenaArchways(FindOrCreateParent("ARENAS"));
        GenerateArenas();
        GenerateShops();
        RebuildMusicTriggers();
        // Generate snaps some nodes (arena entrances) — their pinned and clamped cameras follow.
        SyncPinnedCinematicCameras();
        SyncCameraClamps();
        RebuildCinematicTriggers();

        WireAllSceneObjects();

        GenerateLandscapeTiles();
        RefreshLandscapeInfluence(placeSpikes: false);   // the hill sync below places them
        SyncHillPointsToScene();

        // Last, so every perch she could open on is built and settled on the landscape first.
        // The boat first — the angel is stood on her perch looking to it.
        PlaceBoatAtStart();
        PlaceAngelOnStartPerch();

        AssetDatabase.SaveAssets();   // the run and hub meshes written during this pass

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[LevelSelectDesigner] Generated {generatedRuns} run(s), " +
                  $"{_data.obstacles.Count} obstacle(s), {_data.arenas.Count} arena(s).");
    }

    private void GenerateSimpleSegment(LevelSelectDesignerData.DesignerPath path, GameObject parent)
    {
        var go = new GameObject(path.segmentId ?? "Segment");
        Undo.RegisterCreatedObjectUndo(go, "Generate Segment");
        go.transform.SetParent(parent.transform, false);

        var spline = SplineFromPath(go.transform, path);

        BuildRunMesh(go, spline, path,
            $"RiverRun_{SanitiseAssetName(path.segmentId ?? "Segment")}", null);
    }

    /// <summary>
    /// A path cut into legs at every pool it is drawn through, so each leg meets a pool only
    /// at its ends — which is the one case the trim against a pool and its mouth handle. A
    /// river drawn through a pool goes in on one side and out on the other, and the pool is
    /// one piece of water between the two.
    ///
    /// A path with no pool in its middle comes back as itself, untouched. Otherwise each leg is
    /// a copy carrying the path's settings over its own stretch of nodes: the first keeps the
    /// segment ID, and the rest take a suffix so their meshes and segments do not collide.
    /// Only the leg that actually reaches an arena still leads to it.
    /// </summary>
    private List<LevelSelectDesignerData.DesignerPath> PoolLegs(LevelSelectDesignerData.DesignerPath path)
    {
        var legs = new List<LevelSelectDesignerData.DesignerPath>();
        int n    = path.nodeIds.Count;

        var cuts = new List<int>();
        for (int i = 1; i < n - 1; i++)
            if (_data.PoolAt(path.nodeIds[i]) != null) cuts.Add(i);

        if (cuts.Count == 0) { legs.Add(path); return legs; }
        cuts.Add(n - 1);

        int from = 0;
        for (int k = 0; k < cuts.Count; k++)
        {
            int  to    = cuts[k];
            bool first = k == 0;
            bool last  = k == cuts.Count - 1;

            var leg = new LevelSelectDesignerData.DesignerPath
            {
                pathId                 = $"{path.pathId}#pool{k}",
                segmentId              = first ? path.segmentId : $"{path.segmentId}_Pool{k}",
                nodeIds                = path.nodeIds.GetRange(from, to - from + 1),
                isLeftPath             = path.isLeftPath,
                isRightPath            = path.isRightPath,
                segmentType            = path.segmentType,
                riverName              = path.riverName,
                leadsToArena           = path.leadsToArena && (path.arenaIsAtEnd ? last : first),
                arenaIsAtEnd           = path.arenaIsAtEnd,
                tJunctionBidirectional = path.tJunctionBidirectional,
                arenaGridDataGuid      = path.arenaGridDataGuid,
                editorColor            = path.editorColor,
                curveStrength          = path.curveStrength,
                curveSubdivisions      = path.curveSubdivisions,
                landscapeInfluence     = path.landscapeInfluence,
                landscapeToOuterRim    = path.landscapeToOuterRim,
                influenceDistance      = path.influenceDistance,
                influenceCancelledNodes = new List<string>(path.influenceCancelledNodes ?? new List<string>()),
                runType                = path.runType,
                rimRounding            = path.rimRounding,
                outerWalls             = path.outerWalls,
            };

            _legSourcePathId[leg.pathId] = path.pathId;
            legs.Add(leg);
            from = to;
        }

        Debug.Log($"[LevelSelectDesigner] '{path.segmentId}' runs through {cuts.Count - 1} " +
                  $"pool(s) — generated as {legs.Count} legs.");
        return legs;
    }

    // The drawn path each generated leg was cut from, keyed by the leg's own ID.
    private readonly Dictionary<string, string> _legSourcePathId = new();

    /// <summary>
    /// The drawn path behind a generated one — itself, unless it is a leg of a river cut at a
    /// pool. Anything asking "is this some OTHER river" has to compare drawn paths, or a leg
    /// finds the very river it was cut from and takes it for a branch.
    /// </summary>
    private string SourcePathId(LevelSelectDesignerData.DesignerPath path)
        => path != null && _legSourcePathId.TryGetValue(path.pathId, out string source)
         ? source : path?.pathId;

    // Handles 1..N junctions on a single path in one pass.
    private void GenerateJunctionSplit(
        LevelSelectDesignerData.DesignerPath path,
        List<LevelSelectDesignerData.DesignerJunction> junctions,
        GameObject parent)
    {
        // ── 1. Build full spline ──────────────────────────────────
        var go = new GameObject(path.segmentId ?? "JunctionSegment");
        Undo.RegisterCreatedObjectUndo(go, "Generate Junction Segment");
        go.transform.SetParent(parent.transform, false);

        var      fullSpline = SplineFromPath(go.transform, path);
        float4x4 fl2w       = go.transform.localToWorldMatrix;

        // ── 2. Resolve each junction: where it lands, and the branch mouth ─
        // Mouths cut into this river's run where its branches meet it.
        var mouths = new List<RiverRunMesh.Mouth>();

        foreach (var junc in junctions)
        {
            Vector3 jWorld3 = WorldPosOfNode(junc.nodeId);
            float3  jLocal  = go.transform.InverseTransformPoint(jWorld3);
            SplineUtility.GetNearestPoint(fullSpline, jLocal, out _, out float nearestT);

            Vector3 actualJuncWorld = go.transform.TransformPoint(fullSpline.EvaluatePosition(nearestT));
            _correctedNodePositions[junc.nodeId] = actualJuncWorld;

            float3  jTanL   = math.normalize(fullSpline.EvaluateTangent(nearestT));
            float3  jTanW   = math.normalize(math.mul(fl2w, new float4(jTanL, 0f)).xyz);
            Vector3 tanWorld = new Vector3(jTanW.x, jTanW.y, jTanW.z);

            // ── The direction the branch actually leaves in drives everything ──
            var branchPath = _data.paths.FirstOrDefault(
                p => p.pathId != SourcePathId(path) && p.nodeIds.Contains(junc.nodeId));

            Vector3 branchDir = Vector3.zero;
            if (branchPath != null)
            {
                int    bIdx   = branchPath.nodeIds.IndexOf(junc.nodeId);
                string nextId = bIdx >= 0 && bIdx < branchPath.nodeIds.Count - 1
                    ? branchPath.nodeIds[bIdx + 1] : null;
                if (nextId != null)
                {
                    branchDir   = WorldPosOfNode(nextId) - actualJuncWorld;
                    branchDir.y = 0f;
                }
            }
            // No branch node to read — fall back to leaving at 90° right of travel.
            if (branchDir.sqrMagnitude < 0.0001f)
                branchDir = new Vector3(tanWorld.z, 0f, -tanWorld.x);
            branchDir.Normalize();

            // Top = LEFT of travel, Bottom = RIGHT — the T / B the canvas labels a branch with.
            if (branchPath != null && !branchPath.isLeftPath && !branchPath.isRightPath)
            {
                float cross = SplineSplitUtility.CrossXZ(tanWorld, branchDir);
                Undo.RecordObject(_data, "Auto-detect branch side");
                branchPath.isLeftPath  = cross < 0f;
                branchPath.isRightPath = cross > 0f;
                EditorUtility.SetDirty(_data);
            }

            // Then re-read every time off the branch's neighbouring node, as the split for the
            // boat's segments used to — so a branch keeps the side it has always been given.
            if (branchPath != null)
            {
                int    bIdx   = branchPath.nodeIds.IndexOf(junc.nodeId);
                string sideId = bIdx < branchPath.nodeIds.Count - 1
                    ? branchPath.nodeIds[bIdx + 1]
                    : bIdx > 0 ? branchPath.nodeIds[bIdx - 1] : null;
                if (sideId != null)
                {
                    float cross = SplineSplitUtility.CrossXZ(tanWorld,
                        (WorldPosOfNode(sideId) - actualJuncWorld).normalized);
                    branchPath.isRightPath = cross > 0f;
                    branchPath.isLeftPath  = cross < 0f;
                }
            }

            // The branch gets no piece of its own — it is a mouth cut down through this
            // river's rim, at its own width and depth, wherever it happens to meet.
            Quaternion juncRot     = Quaternion.LookRotation(tanWorld, Vector3.up);
            Vector3    branchLocal = Quaternion.Inverse(juncRot) * branchDir;
            float      branchAngle = Mathf.Atan2(branchLocal.x, branchLocal.z);

            var mainProfile   = _data.ProfileFor(path.riverName);
            var branchProfile = branchPath != null ? _data.ProfileFor(branchPath.riverName) : mainProfile;

            // The branch's own run stops a collar clear of this river, so the whole of its
            // last ring is outside — that ring is where the mouth patch picks the section up.
            // Both sides work off this one number, so the two always meet.
            float collar = RiverMeshBuilder.RunCollar(mainProfile, branchProfile, branchAngle,
                                                      MeshEdge);

            mouths.Add(new RiverRunMesh.Mouth
            {
                riverName = branchPath != null ? branchPath.riverName : path.riverName,
                centre    = go.transform.InverseTransformPoint(actualJuncWorld),
                direction = go.transform.InverseTransformDirection(branchDir).normalized,
                collar    = collar,
            });

            _junctionPerpDirections[junc.nodeId] = branchDir;
            _junctionBranchStart[junc.nodeId]    = collar;

            // Its water carries the whole way in to this river's centreline, where the two
            // channels are already the same depth and one surface runs into the other.
            _junctionWaterLeadIn[junc.nodeId] = collar;

            // This river's centreline across the junction, facing out toward the branch, so the
            // branch's water can be told how far each of its corners lies from this river's banks.
            Vector3 acrossWorld = Vector3.Cross(Vector3.up, new Vector3(tanWorld.x, 0f, tanWorld.z));
            if (Vector3.Dot(acrossWorld, branchDir) < 0f) acrossWorld = -acrossWorld;
            if (acrossWorld.sqrMagnitude > 1e-8f)
                _junctionJoin[junc.nodeId] = (actualJuncWorld, acrossWorld.normalized, path.riverName);
        }

        // ── 3. One unbroken run for the whole river, mouths and all ─
        BuildRunMesh(go, fullSpline, path,
            $"RiverRun_{SanitiseAssetName(path.segmentId ?? "Segment")}", mouths);
    }

    // ══════════════════════════════════════════════════════════════
    // PROCEDURAL RUN GEOMETRY
    // ══════════════════════════════════════════════════════════════

    private const string GeneratedDir = PrefabDir + "/Generated";

    // How finely the sweep walks a spline before rings are placed along it by arc length.
    private const int SplineSampleCount = 512;

    /// <summary>
    /// Walks a spline by arc length and places ring frames every <paramref name="step"/>
    /// along it, so rings sit evenly rather than bunching where the curve is tight.
    ///
    /// Where a branch's mouth crosses, the rings are placed on the lines of that branch's
    /// own section instead — the mouth ends on this run's centreline and has to land there
    /// on the run's own vertices, or the two stop lining up.
    /// </summary>
    /// <summary>
    /// How far a run has to carry on past <paramref name="nodeId"/> to reach into the arena
    /// standing there, or 0 when that node is not an arena.
    ///
    /// A run stops on the entrance node it was drawn to, and the wall stands `radius` out from
    /// the arena centre — so what has to be made up is however far that node fell from the
    /// face. Measured rather than assumed, because the primary entrance sits `arenaHeadOffset`
    /// back from the centre while the others are dragged wherever they are wanted.
    ///
    /// It goes both ways. A node short of the face carries the run on; a node already inside a
    /// wide arena pulls it back. Either way 0 lands the run's end on the inner face, and the
    /// entrance's own overlap pushes it in from there.
    /// </summary>
    private float ArenaReachAt(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return 0f;

        var arena = _data.ArenaAtEntrance(nodeId);
        if (arena == null) return 0f;

        Vector3 centre = GetTrueArenaCenter(arena);
        Vector3 at     = WorldPosOfNode(nodeId);
        centre.y = 0f;
        at.y     = 0f;

        float toWall = Vector3.Distance(at, centre) - _data.ArenaWallFor(arena).radius;
        return toWall + _data.RunOverlapFor(arena, nodeId);
    }

    /// <summary>Reach for each end of a path, whichever end its arena is at.</summary>
    private void ArenaReachFor(LevelSelectDesignerData.DesignerPath path,
                               out string atStart, out string atEnd)
    {
        atStart = null;
        atEnd   = null;
        if (path == null || !path.leadsToArena || path.nodeIds.Count == 0) return;

        string first = path.nodeIds[0];
        string last  = path.nodeIds[path.nodeIds.Count - 1];

        if (_data.ArenaAtEntrance(first) != null) atStart = first;
        if (_data.ArenaAtEntrance(last)  != null) atEnd   = last;
    }

    /// <summary>
    /// Moves one or both ends of the sampled run to where its arena wants them — on past the
    /// end it sampled to when the reach is positive, back off it when the reach is negative.
    /// Whatever is added is straight, along the heading the run finished on: it is running into
    /// a wall by this point, and holding the last heading keeps its section square to the face.
    ///
    /// The water is swept along these same rings, so it follows the run without anything
    /// further being said.
    /// </summary>
    private static void FitRunToArena(
        List<Vector3> centres, List<Vector3> forwards,
        float startReach, float endReach, float step)
    {
        if (centres == null || forwards == null || centres.Count < 2) return;
        step = Mathf.Max(step, 0.001f);

        if (endReach > 0.0001f)
        {
            int     last = centres.Count - 1;
            Vector3 fwd  = forwards[last];
            Vector3 from = centres[last];
            int     n    = Mathf.Max(1, Mathf.CeilToInt(endReach / step));

            for (int i = 1; i <= n; i++)
            {
                centres.Add(from + fwd * (endReach * i / n));
                forwards.Add(fwd);
            }
        }
        else if (endReach < -0.0001f)
        {
            // Drop every ring standing past where the end belongs, then put one exactly there
            // so the cap sits on the wall face rather than on the nearest ring to it.
            int     last   = centres.Count - 1;
            Vector3 fwd    = forwards[last];
            Vector3 target = centres[last] + fwd * endReach;

            while (centres.Count > 2 &&
                   Vector3.Dot(centres[centres.Count - 1] - target, fwd) > 0f)
            {
                centres.RemoveAt(centres.Count - 1);
                forwards.RemoveAt(forwards.Count - 1);
            }
            if (Vector3.Dot(target - centres[centres.Count - 1], fwd) > 0.0001f)
            {
                centres.Add(target);
                forwards.Add(fwd);
            }
        }

        if (startReach > 0.0001f)
        {
            Vector3 fwd  = forwards[0];
            Vector3 from = centres[0];
            int     n    = Mathf.Max(1, Mathf.CeilToInt(startReach / step));

            // Built outermost-first, so the run still reads start to end once it is spliced on.
            var pre     = new List<Vector3>(n);
            var preFwd  = new List<Vector3>(n);
            for (int i = n; i >= 1; i--)
            {
                pre.Add(from - fwd * (startReach * i / n));
                preFwd.Add(fwd);
            }
            centres.InsertRange(0, pre);
            forwards.InsertRange(0, preFwd);
        }
        else if (startReach < -0.0001f)
        {
            // Same at the head of the run, where the arena lies the other way along the heading.
            Vector3 fwd    = forwards[0];
            Vector3 target = centres[0] - fwd * startReach;

            while (centres.Count > 2 && Vector3.Dot(centres[0] - target, fwd) < 0f)
            {
                centres.RemoveAt(0);
                forwards.RemoveAt(0);
            }
            if (Vector3.Dot(centres[0] - target, fwd) > 0.0001f)
            {
                centres.Insert(0, target);
                forwards.Insert(0, fwd);
            }
        }
    }

    private static bool SampleRun(
        Spline spline, float step, RiverProfile profile,
        IList<RiverMeshBuilder.RiverNotch> notches,
        out List<Vector3> centres, out List<Vector3> forwards)
    {
        centres  = null;
        forwards = null;
        if (spline == null || spline.Count < 2) return false;

        var ts    = new float[SplineSampleCount + 1];
        var walk  = new Vector3[SplineSampleCount + 1];
        var right = new Vector3[SplineSampleCount + 1];
        var dist  = new float[SplineSampleCount + 1];

        for (int i = 0; i <= SplineSampleCount; i++)
        {
            ts[i] = (float)i / SplineSampleCount;
            float3 p = spline.EvaluatePosition(ts[i]);
            float3 d = spline.EvaluateTangent(ts[i]);

            walk[i] = new Vector3(p.x, p.y, p.z);
            dist[i] = i == 0 ? 0f : dist[i - 1] + Vector3.Distance(walk[i], walk[i - 1]);

            Vector3 fwd = new Vector3(d.x, 0f, d.z);
            fwd = fwd.sqrMagnitude < 1e-8f ? Vector3.forward : fwd.normalized;
            right[i] = Vector3.Cross(Vector3.up, fwd);
        }

        float length = dist[SplineSampleCount];
        if (length < 0.0001f) return false;

        var stops = BuildRingStops(profile, notches, walk, right, dist, length, step);

        centres  = new List<Vector3>(stops.Count);
        forwards = new List<Vector3>(stops.Count);

        int cursor = 0;
        foreach (float target in stops)
        {
            while (cursor < SplineSampleCount - 1 && dist[cursor + 1] < target) cursor++;

            float span = Mathf.Max(0.000001f, dist[cursor + 1] - dist[cursor]);
            float t    = Mathf.Lerp(ts[cursor], ts[cursor + 1],
                                    Mathf.Clamp01((target - dist[cursor]) / span));

            float3 pos = spline.EvaluatePosition(t);
            float3 tan = spline.EvaluateTangent(t);
            centres.Add(new Vector3(pos.x, pos.y, pos.z));
            forwards.Add(new Vector3(tan.x, tan.y, tan.z));
        }
        return centres.Count >= 2;
    }

    /// <summary>
    /// Arc lengths to place rings at, in order, from 0 to the run's length.
    ///
    /// A mouth ends on this run's centreline, and every line of the branch's section has to
    /// come down there on a ring of its own. So the rings across a mouth are placed exactly
    /// where those lines cross the centreline, and the even spacing is lifted out from under
    /// them — otherwise the section lands on whichever ring happened to be nearest and the
    /// two stop lining up.
    /// </summary>
    private static List<float> BuildRingStops(
        RiverProfile profile, IList<RiverMeshBuilder.RiverNotch> notches,
        Vector3[] walk, Vector3[] right, float[] dist, float length, float step)
    {
        var stops = new List<float>();

        int rings = Mathf.Max(2, Mathf.CeilToInt(length / step) + 1);
        for (int r = 0; r < rings; r++) stops.Add(length * r / (rings - 1));

        if (notches == null || notches.Count == 0) return stops;

        foreach (var n in notches)
        {
            if (n.profile == null) continue;

            var us = RiverMeshBuilder.TopColumns(n.profile, step);
            if (us.Count < 3) continue;

            var cross = new List<float>(us.Count);
            foreach (float u in us)
                if (CentreCrossing(walk, dist, n, u, out float s)) cross.Add(s);

            // A branch that does not cross this run from side to side has no mouth here.
            // Leave the even spacing alone rather than tear a hole in it.
            if (cross.Count != us.Count) continue;

            float lo = Mathf.Min(cross[0], cross[cross.Count - 1]);
            float hi = Mathf.Max(cross[0], cross[cross.Count - 1]);

            stops.RemoveAll(s => s > lo - 0.0005f && s < hi + 0.0005f
                              && s > 0.0005f && s < length - 0.0005f);
            stops.AddRange(cross);
        }

        stops.Sort();

        // Drop stops that landed on top of each other, so no ring pair is degenerate.
        var cleaned = new List<float> { stops[0] };
        for (int i = 1; i < stops.Count; i++)
            if (stops[i] - cleaned[cleaned.Count - 1] > 0.0005f) cleaned.Add(stops[i]);
        if (length - cleaned[cleaned.Count - 1] > 0.0005f) cleaned.Add(length);

        return cleaned;
    }

    // Where along the run one line of a branch's section crosses this run's centreline.
    private static bool CentreCrossing(
        Vector3[] walk, float[] dist, RiverMeshBuilder.RiverNotch n, float u, out float s)
    {
        s = 0f;
        float prev = EdgeOffset(walk[0], n, u);

        for (int i = 1; i < walk.Length; i++)
        {
            float here = EdgeOffset(walk[i], n, u);

            if (prev == 0f || (prev < 0f) != (here < 0f))
            {
                float k = prev == 0f ? 0f : prev / (prev - here);
                s = Mathf.Clamp(Mathf.Lerp(dist[i - 1], dist[i], k), 0f, dist[walk.Length - 1]);
                return true;
            }
            prev = here;
        }
        return false;
    }

    // Signed distance from one line of a branch's section, in the plane.
    private static float EdgeOffset(Vector3 at, RiverMeshBuilder.RiverNotch n, float edge)
    {
        Vector3 rel = at - n.centre;
        rel.y = 0f;
        return rel.x * n.direction.z - rel.z * n.direction.x - edge;
    }


    /// <summary>
    /// Sweeps the river's cross-section along <paramref name="spline"/> — unbroken, straight
    /// through its junctions — and hangs the result under <paramref name="segmentGO"/> as a
    /// single mesh, with each branch cut into it as a mouth. The run keeps a record of the
    /// curve and the mouths so it can be rebuilt later without a full regenerate.
    /// </summary>
    private void BuildRunMesh(
        GameObject segmentGO, Spline spline,
        LevelSelectDesignerData.DesignerPath path, string runName,
        List<RiverRunMesh.Mouth> mouths)
    {
        float step = MeshEdge;
        var profile = RunProfileFor(path, path.riverName);
        var notches = ToNotches(mouths);

        if (!SampleRun(spline, step, profile, notches, out var centres, out var forwards)) return;

        // Carry the run on into any arena it leads to, so it meets the wall instead of stopping
        // short of it. Done before the rings are counted, so the log reports what was built.
        ArenaReachFor(path, out string arenaStart, out string arenaEnd);
        FitRunToArena(centres, forwards,
                           ArenaReachAt(arenaStart), ArenaReachAt(arenaEnd), step);

        int rings = centres.Count;

        // The mouth patch in the river this one leaves picks the section up on this run's
        // first ring, so that ring has to stand exactly where the patch put it. Say so
        // rather than leave a hairline gap at the junction to be found by eye later.
        WarnIfBranchStartDrifts(segmentGO, path, centres[0], forwards[0]);

        // A run that leaves a junction or a pool is left open at that end: the mouth patch
        // there picks the section up, and a cap would be a wall across the middle of it. Both
        // runs meeting at a river split are open there too — the other run carries straight on.
        bool capStart = !_poolOpenStart.Contains(path.pathId)
                     && !(path.nodeIds.Count > 0 &&
                          (_junctionBranchStart.ContainsKey(path.nodeIds[0]) ||
                           _riverJoinHeadings.ContainsKey(path.nodeIds[0])));
        bool capEnd   = !_poolOpenEnd.Contains(path.pathId)
                     && !(path.nodeIds.Count > 0 &&
                          _riverJoinHeadings.ContainsKey(path.nodeIds[path.nodeIds.Count - 1]));

        // Unless the run at the other side of a river split is built differently (Run Type):
        // then each closes on its own flat end, and only the water carries across.
        if (path.nodeIds.Count > 0)
        {
            capStart |= RunStyleBreaksAt(path, path.nodeIds[0]);
            capEnd   |= RunStyleBreaksAt(path, path.nodeIds[path.nodeIds.Count - 1]);
        }

        var built = RiverMeshBuilder.BuildRun(profile, centres, forwards, notches, step, capStart, capEnd);
        if (IsNaturalRun(path)) ApplyLandscapeNoise(built, segmentGO.transform.localToWorldMatrix);

        var mesh = SaveGeneratedMesh(runName, built);
        if (mesh == null) return;

        // Built in the segment's own space, so it sits on it at identity.
        var runGO = NewMeshChild(segmentGO, runName, mesh);
        runGO.transform.localPosition = Vector3.zero;
        runGO.transform.localRotation = Quaternion.identity;
        runGO.GetComponent<MeshRenderer>().sharedMaterial = RunMaterialFor(path);

        var record = runGO.AddComponent<RiverRunMesh>();

        // Fog obstacles for the structure, strung from the FogMap's numbers. Added here rather
        // than left to be wired, because a run is generated and there is nothing to wire it on.
        runGO.AddComponent<RiverRunFogRepellers>();
        record.riverName     = path.riverName;
        record.pathId        = path.pathId;
        record.meshAssetName = runName;
        record.waterLeadIn       = WaterLeadInFor(path);
        record.waterLeadOut      = WaterLeadOutFor(path);
        RecordPoolWaterHeights(record, path);
        record.waterSortingOrder = WaterSortingOrder(path);
        RecordJoin(record, path, segmentGO);
        record.capStart      = capStart;
        record.capEnd        = capEnd;
        record.naturalRun    = IsNaturalRun(path);
        record.arenaAtStart  = arenaStart;
        record.arenaAtEnd    = arenaEnd;
        record.RecordSpline(spline);
        if (mouths != null) record.mouths = new List<RiverRunMesh.Mouth>(mouths);
        RecordPathNodes(record, path, segmentGO);

        RefreshWater(record, profile, centres, forwards);
        RefreshRimNodes(record, profile, centres, forwards);
        RefreshWalkways(record, profile, centres, forwards);
        RefreshBanks(record, profile, centres, forwards, notches, capStart, capEnd);

        Debug.Log($"[LevelSelectDesigner] Run '{runName}' river='{path.riverName}' " +
                  $"inner={profile.innerWidth} rim={profile.rimWidth} " +
                  $"riverDepth={profile.riverDepth} depth={profile.depth} " +
                  $"rings={rings} mouths={(mouths?.Count ?? 0)}");
    }

    /// <summary>
    /// Measures a branch's first ring against the mouth waiting for it in the river it
    /// leaves. Both are built from the same junction point, the same heading and the same
    /// collar, so anything other than zero here is the gap between the two.
    ///
    /// Does nothing for a run that does not start at a junction.
    /// </summary>
    private void WarnIfBranchStartDrifts(
        GameObject segmentGO, LevelSelectDesignerData.DesignerPath path,
        Vector3 centreLocal, Vector3 forwardLocal)
    {
        if (segmentGO == null || path == null || path.nodeIds.Count == 0) return;

        string nodeId = path.nodeIds[0];
        if (!_junctionBranchStart.TryGetValue(nodeId, out float collar))         return;
        if (!_junctionPerpDirections.TryGetValue(nodeId, out Vector3 leaveDir))  return;
        if (!_correctedNodePositions.TryGetValue(nodeId, out Vector3 juncWorld)) return;

        Vector3 want   = juncWorld + leaveDir.normalized * collar;
        Vector3 centre = segmentGO.transform.TransformPoint(centreLocal);

        Vector3 fwd = segmentGO.transform.TransformDirection(forwardLocal);
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-8f) return;

        float drift = Vector3.Distance(centre, want);
        float turn  = Vector3.Angle(fwd.normalized, leaveDir.normalized);
        if (drift < 0.0005f && turn < 0.05f) return;

        Debug.LogWarning(
            $"[LevelSelectDesigner] Branch '{path.segmentId}' starts {drift:F4} away from and " +
            $"{turn:F2}° off the mouth it leaves — that is the gap at the junction.", segmentGO);
    }

    /// <summary>
    /// Rebuilds every generated run in the open scene from its recorded curve and the
    /// river shapes as they stand now. Used for a full pass and for live edits alike.
    /// </summary>
    private int RebuildRunMeshes(string onlyRiverName = null)
    {
        if (_data == null) return 0;

        int rebuilt = 0;
        foreach (var record in FindObjectsOfType<RiverRunMesh>())
        {
            if (record.knots.Count < 2 || string.IsNullOrEmpty(record.meshAssetName)) continue;

            // The default shape carries no river name, and a change to it reaches everything.
            // A run meeting that river's pool is too: its water ends on the pool's water's edge.
            bool affected = string.IsNullOrEmpty(onlyRiverName)
                          || record.riverName == onlyRiverName
                          || record.mouths.Exists(m => m.riverName == onlyRiverName)
                          || MeetsPoolOf(record, onlyRiverName);
            if (!affected) continue;

            var filter = record.GetComponent<MeshFilter>();
            if (filter == null) continue;

            var spline  = record.ToSpline();
            var path    = PathForRunOrRiver(record);
            var profile = RunProfileFor(path, record.riverName);
            var notches = ToNotches(record.mouths);

            if (!SampleRun(spline, MeshEdge, profile, notches, out var centres, out var forwards))
                continue;

            // Measured again rather than replayed, so a run follows its arena's wall when that
            // wall is resized.
            FitRunToArena(centres, forwards,
                               ArenaReachAt(record.arenaAtStart),
                               ArenaReachAt(record.arenaAtEnd), MeshEdge);

            // A river split is open unless the two runs meeting there are built differently,
            // which a Run Type change can make or unmake without a regenerate.
            if (path != null && record.pathNodes != null && record.pathNodes.Count > 0)
            {
                string first = record.pathNodes[0].nodeId;
                string last  = record.pathNodes[record.pathNodes.Count - 1].nodeId;
                if (_data.RiverJoinAt(first) != null) record.capStart = RunStyleBreaksAt(path, first);
                if (_data.RiverJoinAt(last)  != null) record.capEnd   = RunStyleBreaksAt(path, last);
            }

            // A pool's water edge moves with its river's Run Type, so the water carried over
            // its rim is measured again too.
            RemeasurePoolWaterReach(record);

            var built = RiverMeshBuilder.BuildRun(profile, centres, forwards, notches, MeshEdge,
                                                  record.capStart, record.capEnd);
            if (IsNaturalRun(path)) ApplyLandscapeNoise(built, record.transform.localToWorldMatrix);

            var mesh = SaveGeneratedMesh(record.meshAssetName, built);
            if (mesh == null) continue;

            filter.sharedMesh = mesh;
            EditorUtility.SetDirty(filter);

            var renderer = record.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = RunMaterialFor(path);
                EditorUtility.SetDirty(renderer);
            }
            // Run Type can change without a regenerate; the fog chain reads this to leave
            // natural runs out when the FogMap says so.
            record.naturalRun = IsNaturalRun(path);
            EditorUtility.SetDirty(record);
            RefreshWater(record, profile, centres, forwards);
            RefreshRimNodes(record, profile, centres, forwards);
            RefreshWalkways(record, profile, centres, forwards);
            RefreshBanks(record, profile, centres, forwards, notches,
                         record.capStart, record.capEnd);

            // The fog chain is strung along this curve, so a shape edit leaves it standing
            // where the run used to be. Runs generated before the chain existed carry no
            // component, and are left alone rather than quietly gaining one on a shape edit.
            var fogChain = record.GetComponent<RiverRunFogRepellers>();
            if (fogChain != null) fogChain.Restring();

            rebuilt++;
        }

        // Pools are revolved from the same shapes, so a Run Shape edit has to reach them too.
        // That also re-traces the landscape's river edges, so the lift follows the new shape.
        rebuilt += RebuildPoolMeshes(onlyRiverName);

        return rebuilt;
    }

    /// <summary>
    /// How far back past the start of a run its water reaches. Only a branch has any: its run
    /// was trimmed to butt onto the side wall of the river it leaves, and its water has to
    /// carry on past that wall, over the mouth, to the edge of that river's channel.
    /// </summary>
    private float WaterLeadInFor(LevelSelectDesignerData.DesignerPath path)
    {
        if (path == null || path.nodeIds.Count == 0) return 0f;

        // A river running out of a pool only has that pool's rim to cross; its trim carried no
        // designer offset, so its water makes up none either.
        if (_poolWaterReach.TryGetValue(path.nodeIds[0], out float poolReach)) return poolReach;

        if (!_junctionWaterLeadIn.TryGetValue(path.nodeIds[0], out float leadIn)) return 0f;

        // The run was cut back by the collar, so the water makes that up and carries on to
        // the centreline of the river it leaves.
        return Mathf.Max(0f, leadIn);
    }

    /// <summary>
    /// Records the river a branch leaves, as a straight line across the junction, so its water
    /// can carry that river's banks and its lines take on that river's Reach where the two meet.
    ///
    /// The river's name and not its width, so Rebuild Runs measures the shore against whatever
    /// that river's Run Shape and the water level are now. Cleared for anything that does not
    /// start at a junction — a river leaving a pool gets its Reach from the pool's own strips.
    /// </summary>
    private void RecordJoin(
        RiverRunMesh record, LevelSelectDesignerData.DesignerPath path, GameObject segmentGO)
    {
        record.joinRiver  = null;
        record.joinPoint  = Vector3.zero;
        record.joinAcross = Vector3.zero;

        if (path == null || path.nodeIds.Count == 0 || segmentGO == null) return;

        string start = path.nodeIds[0];
        if (_poolWaterReach.ContainsKey(start)) return;
        if (!_junctionJoin.TryGetValue(start, out var join)) return;

        record.joinRiver  = join.river;
        record.joinPoint  = segmentGO.transform.InverseTransformPoint(join.point);
        record.joinAcross = segmentGO.transform.InverseTransformDirection(join.across).normalized;
    }

    /// <summary>
    /// How far past the end of a run its water carries on. Only a river arriving at a pool has
    /// any: its run was trimmed to butt onto the pool's outer wall, and its water has to cross
    /// that rim to reach the water lying in the pool.
    /// </summary>
    private float WaterLeadOutFor(LevelSelectDesignerData.DesignerPath path)
    {
        if (path == null || path.nodeIds.Count == 0) return 0f;
        if (!_poolWaterReach.TryGetValue(path.nodeIds[path.nodeIds.Count - 1], out float reach))
            return 0f;

        return Mathf.Max(0f, reach);
    }

    /// <summary>
    /// How far a run's water carries over a pool's rim to meet the pool's own, worked out from
    /// the pool as it stands now: its river's shape and Run Type, and the water level. A rounded
    /// bank carries the pool's water out past the channel edge, so this shortens with it.
    /// </summary>
    private float PoolWaterReachFor(LevelSelectDesignerData.DesignerPool pool)
    {
        var shape = _data.PoolShapeFor(pool);
        return RiverMeshBuilder.PoolWaterReach(PoolProfileFor(pool),
                                               shape.poolRadius, shape.islandRadius,
                                               shape.floorDepth, _data.waterLevel, MeshEdge);
    }

    // The same, re-measured for a run being rebuilt: the pool table is only filled on Generate,
    // and a Run Type change rebuilds without one. Keeps what it had where no pool stands now.
    private void RemeasurePoolWaterReach(RiverRunMesh record)
    {
        if (record.pathNodes == null || record.pathNodes.Count == 0) return;

        var start = record.poolAtStart ? _data.PoolAt(record.pathNodes[0].nodeId) : null;
        var end   = record.poolAtEnd
                  ? _data.PoolAt(record.pathNodes[record.pathNodes.Count - 1].nodeId)
                  : null;

        if (start != null) record.waterLeadIn  = Mathf.Max(0f, PoolWaterReachFor(start));
        if (end   != null) record.waterLeadOut = Mathf.Max(0f, PoolWaterReachFor(end));
    }

    // Whether either end of the run meets a pool that takes its shape from that river.
    private bool MeetsPoolOf(RiverRunMesh record, string riverName)
    {
        if (record.pathNodes == null || record.pathNodes.Count == 0) return false;

        bool Of(string nodeId)
        {
            var pool = _data.PoolAt(nodeId);
            return pool != null && _data.PoolRiverName(pool) == riverName;
        }

        return (record.poolAtStart && Of(record.pathNodes[0].nodeId))
            || (record.poolAtEnd   && Of(record.pathNodes[record.pathNodes.Count - 1].nodeId));
    }

    /// <summary>
    /// Records the rim height of any pool either end of this run meets, so its water can be held
    /// level with the pool's — see <see cref="LevelWaterIntoPool"/>. Kept on the record because
    /// the pool table is only filled on Generate, and Rebuild Runs needs it as well.
    /// </summary>
    private void RecordPoolWaterHeights(RiverRunMesh record, LevelSelectDesignerData.DesignerPath path)
    {
        record.poolAtStart = false;
        record.poolAtEnd   = false;
        if (path == null || path.nodeIds.Count == 0) return;

        if (_poolCentre.TryGetValue(path.nodeIds[0], out Vector3 start))
        {
            record.poolAtStart   = true;
            record.poolRimYStart = start.y;
        }

        if (_poolCentre.TryGetValue(path.nodeIds[path.nodeIds.Count - 1], out Vector3 end))
        {
            record.poolAtEnd   = true;
            record.poolRimYEnd = end.y;
        }
    }

    /// <summary>
    /// Holds the end of a run's water that meets a pool level with the pool's own water.
    ///
    /// A pool's water is one flat sheet at its rim height, and it laps out over the river's water
    /// for Water Pool Overlap metres. The river's water rides its run, and a run still climbing or
    /// dropping as it reaches the pool overshoots the pool's height a little — 0.062 on World2's
    /// MainRiver — which its straight lead across the rim then carries on flat. The two sheets lie
    /// that far apart over the whole lap, and the boat, reading its height off the water, steps
    /// between them. So the WATER is held at the pool's height from where it ends back past the
    /// lap's far lip, then eased onto the run's own height over the same distance again. The run
    /// itself is left exactly where it is.
    /// </summary>
    private void LevelWaterIntoPool(
        RiverRunMesh record, List<Vector3> centres, bool fromEnd, float poolRimY, float lead,
        string waterName)
    {
        int n = centres.Count;
        if (n < 2) return;

        // The lap reaches Water Pool Overlap back from where the river's water ends, and the lead
        // across the rim is its own length back from there; whichever is further is held.
        float hold = Mathf.Max(Mathf.Max(0f, _data.waterPoolOverlap), lead);
        if (hold <= 0.0001f) return;

        int     At(int k) => fromEnd ? n - 1 - k : k;
        Vector3 World(int k) => record.transform.TransformPoint(centres[At(k)]);

        // The lead across the rim runs dead straight along the way the river arrives, and the
        // pool lays its lap along that same line — so how far back a ring lies is measured along
        // it, which is how far the lap really reaches over that ring.
        Vector3 endW = World(0);
        Vector3 axis = endW - World(1);
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-8f) return;
        axis.Normalize();

        float gap = endW.y - poolRimY;

        // Every ring up to and including the first one at or past the lap's lip is held, so the
        // whole of the lap lies over faces that are flat at the pool's height. The ease starts
        // from that ring and runs on along the water's own length.
        float   eased = 0f;
        bool    holding = true;
        Vector3 prevW = endW;
        for (int k = 0; k < n; k++)
        {
            Vector3 w = World(k);
            float   weight;

            if (holding)
            {
                weight = 1f;
                if (Vector3.Dot(endW - w, axis) >= hold - 0.0001f) holding = false;
            }
            else
            {
                Vector3 step = w - prevW;
                step.y = 0f;
                eased += step.magnitude;

                float x = Mathf.Clamp01(eased / hold);
                weight = 1f - x * x * (3f - 2f * x);
                if (weight <= 0f) break;
            }

            prevW = w;
            w.y = Mathf.Lerp(w.y, poolRimY, weight);
            centres[At(k)] = record.transform.InverseTransformPoint(w);
        }

        Debug.Log($"[LSD] Water '{waterName}' held level with the pool at its " +
                  $"{(fromEnd ? "end" : "start")}: it was {gap:+0.000;-0.000;0.000} off the pool's " +
                  $"water, now 0 over the last {hold:F2} m, eased back over {hold:F2} m.", record);
    }

    /// <summary>
    /// Lays the water surface in a run's channel, or clears it away when the level is not
    /// water filled. The water hangs off the run itself, so it moves and rebuilds with it.
    /// </summary>
    private void RefreshWater(
        RiverRunMesh record, RiverProfile profile,
        List<Vector3> centres, List<Vector3> forwards)
    {
        if (record == null || string.IsNullOrEmpty(record.meshAssetName)) return;

        string waterName = $"RiverWater_{record.meshAssetName.Replace("RiverRun_", string.Empty)}";
        var    existing  = record.transform.Find(waterName);

        if (!_data.waterFilled)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            record.waterMeshAssetName = null;
            EditorUtility.SetDirty(record);
            return;
        }

        WaterSweep(record.waterLeadIn, record.waterLeadOut,
                   Mathf.Max(0.01f, _data.splineInstantiateSpacing),
                   centres, forwards, out var waterCentres, out var waterForwards);

        if (record.poolAtStart)
            LevelWaterIntoPool(record, waterCentres, fromEnd: false, record.poolRimYStart,
                               record.waterLeadIn, waterName);
        if (record.poolAtEnd)
            LevelWaterIntoPool(record, waterCentres, fromEnd: true, record.poolRimYEnd,
                               record.waterLeadOut, waterName);

        // Rings a whole Detail apart leave the slope turning a corner at each one, and the boat
        // rides every corner. Fill them in along a smooth curve; the rings already there stay put.
        RiverMeshBuilder.SmoothWaterSweep(waterCentres, waterForwards,
                                          RiverMeshBuilder.WaterSmoothEdge,
                                          out waterCentres, out waterForwards);

        // A branch's water carries the banks of the river it leaves, so its lines can take on
        // that river's Reach where it lies over it.
        RiverMeshBuilder.RiverJoin? join = null;
        if (!string.IsNullOrEmpty(record.joinRiver) && record.joinAcross.sqrMagnitude > 0.5f)
        {
            var joined = RunProfileForRiver(record.joinRiver);
            join = new RiverMeshBuilder.RiverJoin
            {
                point  = record.joinPoint,
                across = record.joinAcross,
                shore  = RiverMeshBuilder.WaterHalfWidth(joined, _data.waterLevel),
                extent = joined.OuterWidth,
            };
        }

        var mesh = SaveGeneratedMesh(waterName,
            RiverMeshBuilder.BuildWater(profile, _data.waterLevel, waterCentres, waterForwards,
                                        join));

        // DEBUG (branch mouth fade at pools)
        Debug.Log($"[BranchMouthDebug] water='{waterName}' joinRiver='{record.joinRiver}' " +
                  $"leadIn={record.waterLeadIn:F2} leadOut={record.waterLeadOut:F2} " +
                  $"length={RiverMeshBuilder.DebugWaterLength:F2} " +
                  (join.HasValue
                      ? $"shore={join.Value.shore:F2} extent={join.Value.extent:F2} " +
                        $"flaggedQuads={RiverMeshBuilder.DebugJoinQuads} " +
                        $"flaggedToAlong={RiverMeshBuilder.DebugJoinAlong:F2} " +
                        $"maxInsideJoined={RiverMeshBuilder.DebugJoinMaxIn:F2}"
                      : "no join"));
        if (mesh == null) return;

        GameObject waterGO;
        if (existing != null)
        {
            waterGO = existing.gameObject;
            var f = waterGO.GetComponent<MeshFilter>();
            if (f == null) f = waterGO.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            EditorUtility.SetDirty(f);

            var r = waterGO.GetComponent<MeshRenderer>();
            if (r == null) r = waterGO.AddComponent<MeshRenderer>();
            r.sharedMaterial = _data.waterMaterial;
            EditorUtility.SetDirty(r);
        }
        else
        {
            waterGO = new GameObject(waterName);
            Undo.RegisterCreatedObjectUndo(waterGO, "Generate River Water");
            waterGO.transform.SetParent(record.transform, false);
            waterGO.AddComponent<MeshFilter>().sharedMesh = mesh;
            waterGO.AddComponent<MeshRenderer>().sharedMaterial = _data.waterMaterial;
        }

        MakeWaterSampleable(waterGO, mesh);
        SetWaterSorting(waterGO, RiverWaterSortingLayer, record.waterSortingOrder);

        record.waterMeshAssetName = waterName;
        EditorUtility.SetDirty(record);
    }

    private const string RiverWaterSortingLayer = "Default";
    private const string PoolWaterSortingLayer  = "Top";

    /// <summary>
    /// Puts a water surface in its own Sorting Group, so which of two lapping waters draws on top
    /// is decided by the group rather than left to the depth buffer.
    /// </summary>
    private static void SetWaterSorting(GameObject waterGO, string layerName, int order)
    {
        // An unknown name resolves to 0, which is Default's own id.
        if (layerName != "Default" && SortingLayer.NameToID(layerName) == 0)
            Debug.LogError($"[LSD] Sorting layer '{layerName}' does not exist — add it in " +
                           $"Project Settings > Tags and Layers. '{waterGO.name}' left on Default.");

        var group = waterGO.GetComponent<UnityEngine.Rendering.SortingGroup>();
        if (group == null) group = Undo.AddComponent<UnityEngine.Rendering.SortingGroup>(waterGO);

        group.sortingLayerName = layerName;
        group.sortingOrder     = order;
        EditorUtility.SetDirty(group);
    }

    /// <summary>
    /// A river's water draws one above the river it branches off: the main river 0, a branch
    /// off it 1, a branch off that 2. A path is a branch of whichever other path its first node
    /// lies part way along — the same junction test the lead-in is taken from.
    /// </summary>
    private int WaterSortingOrder(LevelSelectDesignerData.DesignerPath path)
    {
        int depth   = 0;
        var visited = new HashSet<LevelSelectDesignerData.DesignerPath>();

        while (path != null && path.nodeIds.Count > 0 && visited.Add(path))
        {
            string start = path.nodeIds[0];

            // A river leaving a pool is not a branch of anything, even when it is the far leg
            // of a river drawn through that pool.
            if (_data.PoolAt(start) != null) break;

            var parent = _data.paths.FirstOrDefault(p =>
            {
                if (p == path || p.pathId == SourcePathId(path)) return false;
                int i = p.nodeIds.IndexOf(start);
                return i > 0 && i < p.nodeIds.Count - 1;
            });

            if (parent == null) break;
            depth++;
            path = parent;
        }

        return depth;
    }

    /// <summary>
    /// Lays the banks a run's water is held between — the walls the boat bumps off, standing
    /// on each waterline and open wherever a branch arrives. Collision only: no renderer, so
    /// nothing about the look of the river changes.
    ///
    /// Hangs off the run itself, exactly as its water does, so it moves and rebuilds with it.
    /// A river with no water in it has no banks either — there is nothing to be held between.
    /// </summary>
    private void RefreshBanks(
        RiverRunMesh record, RiverProfile profile,
        List<Vector3> centres, List<Vector3> forwards,
        List<RiverMeshBuilder.RiverNotch> notches,
        bool capStart, bool capEnd)
    {
        if (record == null || string.IsNullOrEmpty(record.meshAssetName)) return;

        string banksName = $"RiverBanks_{record.meshAssetName.Replace("RiverRun_", string.Empty)}";
        var    existing  = record.transform.Find(banksName);

        if (!_data.waterFilled)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            return;
        }

        // Water held deeper than the channel is cut has no width to be held at, so there is
        // nothing to build — and a river the boat can drive straight out of is not a thing to
        // find by eye later.
        if (RiverMeshBuilder.WaterHalfWidth(profile, _data.waterLevel) <= 0.0001f)
            Debug.LogWarning(
                $"[LevelSelectDesigner] River '{record.riverName}' has its water {_data.waterLevel} " +
                $"below the rim but its channel only {profile.riverDepth} deep — there is no " +
                $"waterline for banks to stand on, so this run has none and the boat is not " +
                $"held in it.", record);

        // Two runs built differently close on their own flat ends at a river split, but that
        // is the stone only — the water carries straight across, and so must the boat. A bank
        // capped there would be a wall across the river.
        if (record.pathNodes != null && record.pathNodes.Count > 0)
        {
            if (_data.RiverJoinPaths(record.pathNodes[0].nodeId, out _, out _))
                capStart = false;
            if (_data.RiverJoinPaths(record.pathNodes[record.pathNodes.Count - 1].nodeId, out _, out _))
                capEnd = false;
        }

        var mesh = SaveGeneratedMesh(banksName,
            RiverMeshBuilder.BuildRunBanks(profile, _data.waterLevel,
                                          centres, forwards, notches, MeshEdge,
                                          capStart, capEnd, RimNodesFor(record)));

        EnsureCollisionChild(record.transform, banksName, mesh, existing);
    }

    /// <summary>The same for a pool: a wall round the bowl's waterline, and one round the
    /// island when a roundabout has one standing out of the water.</summary>
    private void RefreshPoolBanks(RiverPoolMesh record, RiverProfile profile)
    {
        if (record == null || string.IsNullOrEmpty(record.meshAssetName)) return;

        string banksName = $"RiverPoolBanks_{record.meshAssetName.Replace("RiverPool_", string.Empty)}";
        var    existing  = record.transform.Find(banksName);

        if (!_data.waterFilled)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            return;
        }

        var mesh = SaveGeneratedMesh(banksName,
            RiverMeshBuilder.BuildPoolBanks(profile, _data.waterLevel,
                                            record.poolRadius, record.islandRadius,
                                            record.floorDepth, ToPoolMouths(record.mouths, PoolForRecord(record)),
                                            MeshEdge));

        EnsureCollisionChild(record.transform, banksName, mesh, existing);
    }

    /// <summary>
    /// Hangs a mesh off a generated piece as collision alone — a MeshCollider and nothing
    /// else. A bank that came out empty is taken away rather than left as a collider with no
    /// mesh in it, which is what a stretch of river with no water to hold amounts to.
    /// </summary>
    private static void EnsureCollisionChild(
        Transform parent, string name, Mesh mesh, Transform existing)
    {
        if (mesh == null || mesh.vertexCount == 0)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            return;
        }

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Generate Banks");
            go.transform.SetParent(parent, false);
        }

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        var col = go.GetComponent<MeshCollider>();
        if (col == null) col = go.AddComponent<MeshCollider>();

        // Cleared first: a MeshCollider handed a mesh it is already holding does not always
        // rebake it, and a rebuilt river would keep the shape it had before the edit.
        col.sharedMesh = null;
        col.convex     = false;
        col.sharedMesh = mesh;
        EditorUtility.SetDirty(col);
    }

    /// <summary>
    /// Gives a water surface a collider of its own — there to be found, never to be hit.
    ///
    /// It is how the boat knows what height the water is under it, which is the only way a
    /// river that climbs can carry the boat up with it. Every layer is excluded from it, so
    /// it takes part in no contact with anything: a raycast still finds it, and nothing in
    /// the world can bump into it. It cannot be a trigger instead — a MeshCollider only
    /// triggers when it is convex, and the water is a long curved ribbon.
    /// </summary>
    private static void MakeWaterSampleable(GameObject waterGO, Mesh mesh)
    {
        if (waterGO == null || mesh == null) return;

        int water = LayerMask.NameToLayer("Water");
        if (water >= 0 && waterGO.layer != water) waterGO.layer = water;

        var col = waterGO.GetComponent<MeshCollider>();
        if (col == null) col = waterGO.AddComponent<MeshCollider>();

        col.sharedMesh    = null;
        col.convex        = false;
        col.sharedMesh    = mesh;
        col.isTrigger     = false;
        col.excludeLayers = ~0;
        EditorUtility.SetDirty(col);
    }

    /// <summary>
    /// How far the water surface sits below the rim top the runs are swept along — Water Level
    /// when the rivers are filled, nothing when they are not. Named for the boat splines it once
    /// lowered onto the water; those are retired (Archive/LevelSelectSplines), and it now places
    /// the doors, the arena walls' shading and the boat exits on the water.
    /// </summary>
    private float BoatSplineDrop => _data != null && _data.waterFilled ? _data.waterLevel : 0f;

    /// <summary>
    /// The run's own sweep, with a straight lead bolted onto either end where the water has to
    /// reach further than the run does — back to the river this one leaves, or on into the pool
    /// this one arrives at.
    /// </summary>
    private static void WaterSweep(
        float leadIn, float leadOut, float step,
        List<Vector3> centres, List<Vector3> forwards,
        out List<Vector3> waterCentres, out List<Vector3> waterForwards)
    {
        waterCentres  = new List<Vector3>(centres);
        waterForwards = new List<Vector3>(forwards);
        if (centres.Count == 0 || forwards.Count == 0) return;

        if (leadIn > 0.0001f)
        {
            Vector3 back = forwards[0];
            back.y = 0f;
            if (back.sqrMagnitude > 1e-8f)
            {
                back.Normalize();
                int steps = Mathf.Max(1, Mathf.CeilToInt(leadIn / Mathf.Max(0.01f, step)));
                var leadCentres  = new List<Vector3>(steps);
                var leadForwards = new List<Vector3>(steps);

                for (int i = steps; i >= 1; i--)
                {
                    leadCentres.Add(centres[0] - back * (leadIn * i / steps));
                    leadForwards.Add(forwards[0]);
                }

                waterCentres.InsertRange(0, leadCentres);
                waterForwards.InsertRange(0, leadForwards);
            }
        }

        if (leadOut > 0.0001f)
        {
            int     last = centres.Count - 1;
            Vector3 on   = forwards[Mathf.Min(last, forwards.Count - 1)];
            on.y = 0f;
            if (on.sqrMagnitude > 1e-8f)
            {
                on.Normalize();
                int steps = Mathf.Max(1, Mathf.CeilToInt(leadOut / Mathf.Max(0.01f, step)));

                for (int i = 1; i <= steps; i++)
                {
                    waterCentres.Add(centres[last] + on * (leadOut * i / steps));
                    waterForwards.Add(forwards[Mathf.Min(last, forwards.Count - 1)]);
                }
            }
        }
    }

    // Resolves each recorded mouth against the branch river's shape as it stands now.
    private List<RiverMeshBuilder.RiverNotch> ToNotches(List<RiverRunMesh.Mouth> mouths)
    {
        if (mouths == null || mouths.Count == 0) return null;

        var notches = new List<RiverMeshBuilder.RiverNotch>(mouths.Count);
        foreach (var m in mouths)
        {
            var branch = RunProfileForRiver(m.riverName);
            notches.Add(new RiverMeshBuilder.RiverNotch
            {
                centre     = m.centre,
                direction  = m.direction,
                innerWidth = branch.innerWidth,
                riverDepth = branch.riverDepth,
                profile    = branch,
                collar     = m.collar,
            });
        }
        return notches;
    }

    // The same for a pool. A pool does not stamp the river's shape into its surface — it takes
    // a slice out and lets the river's own section carry on through it — so it needs the whole
    // arriving profile, not just the width and depth a notch cuts with.
    private List<RiverMeshBuilder.PoolMouth> ToPoolMouths(
        List<RiverRunMesh.Mouth> mouths, LevelSelectDesignerData.DesignerPool pool)
    {
        if (mouths == null || mouths.Count == 0) return null;

        var list = new List<RiverMeshBuilder.PoolMouth>(mouths.Count);
        foreach (var m in mouths)
            list.Add(new RiverMeshBuilder.PoolMouth
            {
                centre    = m.centre,
                direction = m.direction,
                profile   = ArrivingProfileFor(pool, m.riverName),
            });
        return list;
    }

    // Hangs a generated mesh off the segment as its own renderer child.
    private GameObject NewMeshChild(GameObject parent, string name, Mesh mesh)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Generate River Run");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _data.riverMaterial;
        return go;
    }

    // Writes the mesh into the generated folder, reusing the existing asset where there
    // is one so scenes and prefabs keep pointing at the same thing across a regenerate.
    private Mesh SaveGeneratedMesh(string name, Mesh mesh)
    {
        if (mesh == null) return null;

        if (!AssetDatabase.IsValidFolder(GeneratedDir))
            AssetDatabase.CreateFolder(PrefabDir, "Generated");

        string meshPath = $"{GeneratedDir}/{name}.asset";
        var    existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

        if (existing != null)
        {
            // Refill the existing asset rather than replacing it, so everything already
            // pointing at it keeps working — written channel by channel, because a
            // serialized copy does not reliably refresh a mesh already in memory.
            //
            // EVERY channel the builder fills has to be copied here, not just the ones the
            // marble needs. The generated meshes carry their own working data alongside the
            // texture coordinates — the water's banks and flow, the stone's seams and rim
            // height — and a channel left out of this list is silently empty on the asset while
            // being perfectly correct on the mesh that was just built. That reads as a shader
            // doing nothing, which is a long way from where the fault actually is.
            var carried = new List<Vector4>();

            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices);
            existing.SetNormals(mesh.normals);
            existing.SetUVs(0, mesh.uv);

            // Read back as Vector4 rather than through mesh.uv2 / mesh.uv3, which hand out
            // Vector2 and would quietly drop half of every one.
            for (int channel = 1; channel <= 3; channel++)
            {
                mesh.GetUVs(channel, carried);
                if (carried.Count > 0) existing.SetUVs(channel, carried);
            }

            existing.SetTriangles(mesh.triangles, 0);
            existing.RecalculateBounds();
            existing.name = name;

            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        mesh.name = name;
        AssetDatabase.CreateAsset(mesh, meshPath);
        return mesh;
    }

    // ══════════════════════════════════════════════════════════════
    // PROCEDURAL POOL GEOMETRY
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds every pool: the river cross-section revolved about the pool node, with the rivers
    /// arriving cut through its rim as mouths, and a ring spline round the channel for a boat
    /// to travel it on.
    ///
    /// The ring is left free-standing — it is NOT handed to the path stitcher and gets no
    /// junction objects, so nothing here reaches the boat's routing yet. It is the hook the
    /// travel system attaches to when it is ready.
    /// </summary>
    private void GeneratePools(GameObject poolsParent)
    {
        foreach (var pool in _data.pools)
        {
            if (pool == null || string.IsNullOrEmpty(pool.nodeId)) continue;
            if (_data.nodes.Find(n => n.id == pool.nodeId) == null) continue;

            Vector3 centre    = WorldPosOfNode(pool.nodeId);
            string  riverName = _data.PoolRiverName(pool);
            var     profile   = PoolProfileFor(pool);   // the arriving path's Run Type with it
            var     shape     = _data.PoolShapeFor(pool);
            float   channelR  = RiverMeshBuilder.PoolChannelRadius(shape.poolRadius, shape.islandRadius);
            string  poolName  = PoolMeshName(pool);

            var go = new GameObject(poolName);
            Undo.RegisterCreatedObjectUndo(go, "Generate Pool");
            go.transform.SetParent(poolsParent.transform, false);
            go.transform.position = centre;
            go.transform.rotation = Quaternion.identity;

            // One mouth per river meeting the pool, taken off where each run actually ended up
            // rather than off the node it was drawn to — so the patch that carries the section
            // round starts on the run's own last ring however the river curved in.
            var mouths = new List<RiverRunMesh.Mouth>();
            if (_poolArrivals.TryGetValue(pool.nodeId, out var arrivals))
            {
                foreach (var arrival in arrivals)
                {
                    Vector3 atRun = arrival.worldPos - centre;   // the pool sits at identity

                    // A pool is a flat basin: its rim is one plane, so a river still climbing
                    // when it gets here arrives a step off that plane. The height is kept
                    // rather than flattened away — the mouth's first row is the run's own last
                    // ring, and dropping it into the pool's plane left the run standing on a
                    // ledge that whole step high, right round the join. Keeping it, the collar
                    // takes up the difference: it is the one band that hangs clear of both, so
                    // it can start on the run and land on the rim without either end moving.
                    if (Mathf.Abs(atRun.y) > 0.0005f)
                        Debug.Log(
                            $"[LevelSelectDesigner] River '{arrival.riverName}' reaches pool " +
                            $"'{pool.nodeId}' {atRun.y:F3} off its rim height — the collar " +
                            $"takes up the difference.", go);

                    mouths.Add(new RiverRunMesh.Mouth
                    {
                        riverName = arrival.riverName,
                        centre    = atRun,
                        direction = arrival.outward,
                    });
                }
            }

            var built = RiverMeshBuilder.BuildPool(profile, shape.poolRadius, shape.islandRadius,
                                                  shape.floorDepth, ToPoolMouths(mouths, pool), MeshEdge);
            var poolPath = PoolPath(pool);
            if (IsNaturalRun(poolPath)) ApplyLandscapeNoise(built, go.transform.localToWorldMatrix);

            var mesh = SaveGeneratedMesh(poolName, built);
            if (mesh == null) continue;

            var meshGO = NewMeshChild(go, poolName, mesh);
            meshGO.transform.localPosition = Vector3.zero;
            meshGO.transform.localRotation = Quaternion.identity;
            meshGO.GetComponent<MeshRenderer>().sharedMaterial = RunMaterialFor(poolPath);

            var record = meshGO.AddComponent<RiverPoolMesh>();
            record.riverName     = riverName;
            record.meshAssetName = poolName;
            record.poolRadius    = shape.poolRadius;
            record.islandRadius  = shape.islandRadius;
            record.floorDepth    = shape.floorDepth;
            record.mouths        = mouths;

            // The pool's fog circle, sized off the record and numbered from the FogMap — the
            // pool's answer to the run's chain.
            meshGO.AddComponent<RiverPoolFogRepeller>();

            RefreshPoolWater(record, profile);
            RefreshPoolBanks(record, profile);

            Debug.Log($"[LevelSelectDesigner] Pool '{poolName}' river='{riverName}' " +
                      $"path='{poolPath?.segmentId}' runType={(IsNaturalRun(poolPath) ? "Natural" : "Structural")} " +
                      $"rimRounding={profile.rimRounding:F2} wallsOff={profile.wallsOff} " +
                      $"radius={shape.poolRadius} island={shape.islandRadius} " +
                      $"ring={channelR:F3} mouths={mouths.Count}");
        }
    }

    /// <summary>
    /// Rebuilds every generated pool in the open scene against the river shapes as they stand
    /// now — the pool's answer to <see cref="RebuildRunMeshes"/>.
    /// </summary>
    private int RebuildPoolMeshes(string onlyRiverName = null)
    {
        if (_data == null) return 0;

        int rebuilt = 0;
        foreach (var record in FindObjectsOfType<RiverPoolMesh>())
        {
            if (string.IsNullOrEmpty(record.meshAssetName)) continue;

            // The default shape carries no river name, and a change to it reaches everything.
            bool affected = string.IsNullOrEmpty(onlyRiverName)
                          || record.riverName == onlyRiverName
                          || record.mouths.Exists(m => m.riverName == onlyRiverName);
            if (!affected) continue;

            var filter = record.GetComponent<MeshFilter>();
            if (filter == null) continue;

            var pool    = PoolForRecord(record);
            var path    = pool != null ? PoolPath(pool) : PathForRiver(record.riverName);
            var profile = RunProfileFor(path, record.riverName);
            var built   = RiverMeshBuilder.BuildPool(profile, record.poolRadius, record.islandRadius,
                                                    record.floorDepth, ToPoolMouths(record.mouths, pool), MeshEdge);
            if (IsNaturalRun(path)) ApplyLandscapeNoise(built, record.transform.localToWorldMatrix);

            var mesh = SaveGeneratedMesh(record.meshAssetName, built);
            if (mesh == null) continue;

            filter.sharedMesh = mesh;
            EditorUtility.SetDirty(filter);

            var renderer = record.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = RunMaterialFor(path);
                EditorUtility.SetDirty(renderer);
            }
            RefreshPoolWater(record, profile);
            RefreshPoolBanks(record, profile);
            rebuilt++;
        }

        // An island resized past the tower's minimum either way takes its tower with it.
        RebuildPoolTowers();

        // The landscape is lifted to, and cut round, the runs and pools as they now stand.
        RefreshLandscapeInfluence();
        return rebuilt;
    }

    /// <summary>
    /// Lays the water in a pool, or clears it away when the level is not water filled — the
    /// same arrangement a run uses, so the two surfaces sit at the same level and meet.
    /// </summary>
    private void RefreshPoolWater(RiverPoolMesh record, RiverProfile profile)
    {
        if (record == null || string.IsNullOrEmpty(record.meshAssetName)) return;

        string waterName = $"RiverPoolWater_{record.meshAssetName.Replace("RiverPool_", string.Empty)}";
        var    existing  = record.transform.Find(waterName);

        if (!_data.waterFilled)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            record.waterMeshAssetName = null;
            EditorUtility.SetDirty(record);
            return;
        }

        // The mouths go in with it: the water in the pool is cut off flat where each river
        // arrives, on the very line that river's own water ends on, so the two meet without a
        // crescent of the pool's circle left open between them.
        var mesh = SaveGeneratedMesh(waterName,
            RiverMeshBuilder.BuildPoolWater(profile, _data.waterLevel,
                                           record.poolRadius, record.islandRadius, MeshEdge,
                                           ToPoolMouths(record.mouths, PoolForRecord(record)), record.floorDepth,
                                           Mathf.Max(0f, _data.waterPoolOverlap)));
        if (mesh == null) return;

        GameObject waterGO;
        if (existing != null)
        {
            waterGO = existing.gameObject;
            var f = waterGO.GetComponent<MeshFilter>();
            if (f == null) f = waterGO.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            EditorUtility.SetDirty(f);

            var r = waterGO.GetComponent<MeshRenderer>();
            if (r == null) r = waterGO.AddComponent<MeshRenderer>();
            r.sharedMaterial = _data.waterMaterial;
            EditorUtility.SetDirty(r);
        }
        else
        {
            waterGO = new GameObject(waterName);
            Undo.RegisterCreatedObjectUndo(waterGO, "Generate Pool Water");
            waterGO.transform.SetParent(record.transform, false);
            waterGO.AddComponent<MeshFilter>().sharedMesh = mesh;
            waterGO.AddComponent<MeshRenderer>().sharedMaterial = _data.waterMaterial;
        }

        MakeWaterSampleable(waterGO, mesh);
        SetWaterSorting(waterGO, PoolWaterSortingLayer, 0);

        record.waterMeshAssetName = waterName;
        EditorUtility.SetDirty(record);
    }

    // Named off the pool's node, which survives every regenerate — so the mesh asset is reused
    // in place and anything already pointing at it keeps working.
    private static string PoolMeshName(LevelSelectDesignerData.DesignerPool pool)
        => $"RiverPool_{SanitiseAssetName(pool.nodeId)}";

    private static string SanitiseAssetName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Unnamed";
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (char c in raw)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }

    private void GenerateObstacles(GameObject obstaclesParent)
    {
        // Order by path index in designer (not random UUID), then T along path
        var sorted = _data.obstacles
            .OrderBy(o => { int i = _data.paths.FindIndex(p => p.pathId == o.pathId); return i < 0 ? int.MaxValue : i; })
            .ThenBy(o => o.pathT)
            .ToList();

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
        }
    }

    // ══════════════════════════════════════════════════════════════
    // PROCEDURAL ARENA WALLS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the wall ringing every arena: a circular band standing on the arena boundary,
    /// generated the way a run or a pool is — written to a mesh asset in the generated folder
    /// with a <see cref="LevelSelectArenaWallMesh"/> record alongside it, so a shape change can
    /// be rebuilt without a full regenerate.
    ///
    /// The shape comes off <see cref="ProceduralArenaWallMesh"/> — the same builder the walls
    /// inside a level are made from, so an arena reads the same on the map as it does once you
    /// are in it.
    ///
    /// The wall is a sibling of the arena head rather than a child of it: the head is an
    /// authored prefab, and generated geometry parented into a prefab instance would live on
    /// as an override of it.
    /// </summary>
    private void GenerateArenaWalls(GameObject arenasParent)
    {
        foreach (var arena in _data.arenas)
        {
            if (arena == null || string.IsNullOrEmpty(arena.nodeId)) continue;
            if (_data.nodes.Find(n => n.id == arena.nodeId) == null) continue;

            var profile = _data.ArenaWallFor(arena);

            string wallName = ArenaWallMeshName(arena);

            // The wall stands on the water surface, and the mesh is built with its waterline at
            // local y = 0 — the same drop the doors and the boat exits take, so they all agree.
            Vector3 centre = GetTrueArenaCenter(arena);
            centre.y -= BoatSplineDrop;

            var existing = FindObjectsOfType<LevelSelectArenaWallMesh>()
                .FirstOrDefault(w => w.nodeId == arena.nodeId);

            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject(wallName);
                Undo.RegisterCreatedObjectUndo(go, "Generate Arena Wall");
                go.transform.SetParent(arenasParent.transform, false);
                go.AddComponent<LevelSelectArenaWallMesh>();
            }

            go.name                    = wallName;
            go.transform.position      = centre;
            go.transform.rotation      = Quaternion.identity;
            go.transform.localScale    = Vector3.one;

            var mesh = SaveGeneratedMesh(wallName, BuildArenaWallMesh(profile));
            if (mesh == null) continue;

            ApplyArenaWallMesh(go, mesh);

            var record = go.GetComponent<LevelSelectArenaWallMesh>();
            record.nodeId        = arena.nodeId;
            record.meshAssetName = wallName;
            record.profile       = profile.Clone();
            EditorUtility.SetDirty(record);

            // The arena's fog circle, sized off the wall record and numbered from the FogMap.
            // Checked rather than added blind because an existing wall is reused here.
            if (go.GetComponent<LevelSelectArenaFogRepeller>() == null)
                go.AddComponent<LevelSelectArenaFogRepeller>();

            Debug.Log($"[LevelSelectDesigner] Arena wall '{wallName}' radius={profile.radius} " +
                      $"thickness={profile.thickness} height={profile.height} drop={profile.drop}");
        }
    }

    /// <summary>
    /// Rebuilds every generated arena wall in the open scene against the shapes as they stand
    /// now — the arena's answer to <see cref="RebuildPoolMeshes"/>.
    /// </summary>
    private int RebuildArenaWallMeshes(string onlyNodeId = null)
    {
        if (_data == null) return 0;

        int rebuilt = 0;
        foreach (var record in FindObjectsOfType<LevelSelectArenaWallMesh>())
        {
            if (string.IsNullOrEmpty(record.meshAssetName)) continue;
            if (!string.IsNullOrEmpty(onlyNodeId) && record.nodeId != onlyNodeId) continue;

            // The designer data is the authority — a wall in the scene is rebuilt to whatever
            // its arena says now, and only falls back to its own record when the arena is gone.
            var arena   = _data.arenas.Find(a => a.nodeId == record.nodeId);
            var profile = arena != null ? _data.ArenaWallFor(arena) : record.profile;
            if (profile == null || profile.radius <= 0.05f) continue;

            var mesh = SaveGeneratedMesh(record.meshAssetName, BuildArenaWallMesh(profile));
            if (mesh == null) continue;

            ApplyArenaWallMesh(record.gameObject, mesh);
            record.profile = profile.Clone();
            EditorUtility.SetDirty(record);
            rebuilt++;
        }
        return rebuilt;
    }

    // Carries the river run stone shading. The wall is built standing on the water surface, so
    // the rivers' rim top — what the waterline is placed off — is the water level above it; the
    // wall's own lip is its top.
    // Each part takes the stone colour the Run Shading Tuner's Arena section gives it.
    private Mesh BuildArenaWallMesh(ArenaWallProfile profile)
    {
        var parts = new List<ProceduralArenaWallMesh.Part>();
        var wall  = ProceduralArenaWallMesh.Build(ProceduralArenaWallMesh.Shape.Circle,
                                                  profile.radius, profile.thickness,
                                                  profile.height, profile.drop, parts);

        var shading = StoneShadingForBuild;
        var kinds   = parts.ConvertAll(p => (float)(shading == null ? StoneFaceKind.Auto
            : p == ProceduralArenaWallMesh.Part.InnerFace ? shading.arenaInnerFace
            : p == ProceduralArenaWallMesh.Part.OuterFace ? shading.arenaOuterFace
                                                          : shading.arenaTop));

        return RiverMeshBuilder.ShadeAsStone(wall, BoatSplineDrop, profile.height, kinds);
    }

    // An archway, carrying the stone shading with each part coloured as the Run Shading Tuner's
    // Arena Archways section says.
    private Mesh BuildArchwayMesh(ArenaArchwayProfile settled)
    {
        var parts = new List<ArenaArchwayMesh.Part>();
        var arch  = ArenaArchwayMesh.Build(settled, MeshEdge, parts);

        var shading = StoneShadingForBuild;
        var kinds   = parts.ConvertAll(p => (float)(shading == null ? StoneFaceKind.Auto
            : p == ArenaArchwayMesh.Part.Inside  ? shading.archwayInside
            : p == ArenaArchwayMesh.Part.Outside ? shading.archwayOutside
            : p == ArenaArchwayMesh.Part.Faces   ? shading.archwayFaces
                                                 : shading.archwayFeet));

        return RiverMeshBuilder.ShadeAsStone(arch, 0f, null, kinds);
    }

    // A door standing in an archway, carrying the stone shading with each part coloured as the
    // Run Shading Tuner's Arena Doors section says. Both profiles come in already resolved.
    private Mesh BuildArenaDoorMesh(ArenaArchwayProfile arch, ArenaDoorProfile door,
                                    float floor, float waterY, float along)
    {
        var parts = new List<ArenaDoorMesh.Part>();
        var mesh  = ArenaDoorMesh.Build(arch, door, floor, waterY, along, MeshEdge, parts);

        var shading = StoneShadingForBuild;
        var kinds   = parts.ConvertAll(p => (float)(shading == null ? StoneFaceKind.Auto
            : p == ArenaDoorMesh.Part.Sheet   ? shading.doorSheet
            : p == ArenaDoorMesh.Part.Frame   ? shading.doorFrame
            : p == ArenaDoorMesh.Part.Panel   ? shading.doorPanel
            : p == ArenaDoorMesh.Part.FlapRim ? shading.doorFlapRim
            : p == ArenaDoorMesh.Part.Disc    ? shading.doorDisc
                                              : shading.doorEdges));

        return RiverMeshBuilder.ShadeAsStone(mesh, 0f, null, kinds);
    }

    /// <summary>
    /// The stone shading settings a piece being generated takes its part colours from — the Run
    /// Shading Tuner's while it is driving, the world's structure preset otherwise.
    /// </summary>
    private RiverRunShadingSettings StoneShadingForBuild =>
        RiverRunShadingSettings.ForBuild(
            _data != null && _data.structurePreset != null ? _data.structurePreset.runShading : null);

    private void ApplyArenaWallMesh(GameObject go, Mesh mesh)
    {
        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _data.arenaWallMaterial != null
                                ? _data.arenaWallMaterial : _data.riverMaterial;
        EditorUtility.SetDirty(renderer);

        var col = go.GetComponent<MeshCollider>();
        if (col == null) col = go.AddComponent<MeshCollider>();

        // Cleared first, as the banks are: a collider handed the mesh it already holds does not
        // always rebake, and a rebuilt wall would keep its old shape.
        col.sharedMesh = null;
        col.convex     = false;
        col.sharedMesh = mesh;
        EditorUtility.SetDirty(col);
    }

    private static string ArenaWallMeshName(LevelSelectDesignerData.DesignerArena arena) =>
        $"ArenaWall_{SanitiseAssetName(arena.nodeId)}";

    // An entrance an archway can stand at: which door it is, which way the river leaves the
    // arena there, and whose section the arch has to match.
    private struct ArchwaySite
    {
        public int     entranceIndex;
        public string  nodeId;
        public Vector3 outward;
        public string  riverName;
    }

    /// <summary>
    /// Every entrance of an arena, primary and secondary, with the direction the river runs out
    /// on. Outward points from the arena centre back down the river, so an archway turned to
    /// face it has its tunnel running out through the wall the way you travel.
    /// </summary>
    private List<ArchwaySite> ArenaEntranceSites(LevelSelectDesignerData.DesignerArena arena)
    {
        var sites = new List<ArchwaySite>();
        if (arena == null) return sites;

        var branch = _data.paths.FirstOrDefault(p =>
            p.leadsToArena &&
            p.nodeIds.Count > 0 &&
            p.nodeIds[p.nodeIds.Count - 1] == arena.nodeId);

        // The arena centre sits past its node, along the direction the branch arrives on — so
        // the river it came in on is back the other way.
        float   rad    = GetArenaArrivalYAngle(arena) * Mathf.Deg2Rad;
        Vector3 inward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

        sites.Add(new ArchwaySite
        {
            entranceIndex = arena.entranceIndex,
            nodeId        = arena.nodeId,
            outward       = -inward,
            riverName     = branch != null ? branch.riverName : null,
        });

        Vector3 centre = GetTrueArenaCenter(arena);
        foreach (var sec in arena.secondaryEntrances)
        {
            if (sec == null) continue;
            var node = _data.nodes.Find(n => n.id == sec.nodeId);
            if (node == null) continue;

            Vector3 dir = node.worldPosition - centre;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) continue;

            // A secondary door may have no river of its own yet; it then takes the section of
            // the river the arena is on, so the arch is at least consistent with the arena.
            var path = _data.paths.FirstOrDefault(p => p.nodeIds.Contains(sec.nodeId));

            sites.Add(new ArchwaySite
            {
                entranceIndex = sec.entranceIndex,
                nodeId        = sec.nodeId,
                outward       = dir.normalized,
                riverName     = path != null ? path.riverName
                                             : (branch != null ? branch.riverName : null),
            });
        }

        return sites;
    }

    /// <summary>
    /// Stands an archway over every entrance of every arena that asks for one.
    ///
    /// The arch is the arriving river's own section carried over the water: its legs are that
    /// run's rims and its opening is the channel, so it lands square on the run whatever shape
    /// that river is. It sits on the arena wall's inner face and runs out through the wall's
    /// thickness, which is what an unset depth takes.
    ///
    /// Archways stand on the rim top, not the waterline the wall is measured from — the run's
    /// own origin, so the feet meet the rims rather than floating above or sinking into them.
    /// </summary>
    private void GenerateArenaArchways(GameObject arenasParent)
    {
        FollowFirstDoorPreset();

        var wanted = new HashSet<string>();

        foreach (var arena in _data.arenas)
        {
            if (arena == null || string.IsNullOrEmpty(arena.nodeId)) continue;
            if (!arena.archwayOnEntrances) continue;
            if (_data.nodes.Find(n => n.id == arena.nodeId) == null) continue;

            var     wall   = _data.ArenaWallFor(arena);
            var     shape  = _data.ArchwayFor(arena);
            Vector3 centre = GetTrueArenaCenter(arena);

            foreach (var site in ArenaEntranceSites(arena))
            {
                var river   = _data.ProfileFor(site.riverName);
                var settled = shape.Resolve(river, wall.thickness);

                string archName = ArchwayMeshName(arena, site.entranceIndex);
                var    mesh     = SaveGeneratedMesh(archName, BuildArchwayMesh(settled));
                if (mesh == null) continue;

                var existing = FindObjectsOfType<LevelSelectArenaArchwayMesh>()
                    .FirstOrDefault(a => a.nodeId == arena.nodeId &&
                                         a.entranceIndex == site.entranceIndex);

                GameObject go;
                if (existing != null)
                {
                    go = existing.gameObject;
                }
                else
                {
                    go = new GameObject(archName);
                    Undo.RegisterCreatedObjectUndo(go, "Generate Arena Archway");
                    go.transform.SetParent(arenasParent.transform, false);
                    go.AddComponent<LevelSelectArenaArchwayMesh>();
                }

                go.name                 = archName;
                go.transform.position   = centre + site.outward * wall.radius;
                go.transform.rotation   = Quaternion.LookRotation(site.outward, Vector3.up);
                go.transform.localScale = Vector3.one;

                ApplyArenaWallMesh(go, mesh);
                ApplyArchwayDoor(go, arena, site, settled, archName);

                var record = go.GetComponent<LevelSelectArenaArchwayMesh>();
                record.nodeId        = arena.nodeId;
                record.entranceIndex = site.entranceIndex;
                record.meshAssetName = archName;
                record.profile       = settled;
                EditorUtility.SetDirty(record);

                wanted.Add(ArchwayKey(arena.nodeId, site.entranceIndex));
            }
        }

        // Anything left over belongs to an arena that has since turned archways off, lost the
        // entrance, or gone entirely.
        foreach (var stale in FindObjectsOfType<LevelSelectArenaArchwayMesh>())
        {
            if (wanted.Contains(ArchwayKey(stale.nodeId, stale.entranceIndex))) continue;
            Undo.DestroyObjectImmediate(stale.gameObject);
        }
    }

    /// <summary>
    /// Rebuilds every generated archway in the open scene against the shapes as they stand now.
    /// Shape only — an archway is not moved or removed here, since where it stands depends on
    /// the whole arena layout. A full Generate does that.
    /// </summary>
    private int RebuildArchwayMeshes(string onlyNodeId = null)
    {
        if (_data == null) return 0;
        FollowFirstDoorPreset();

        int rebuilt = 0;
        foreach (var record in FindObjectsOfType<LevelSelectArenaArchwayMesh>())
        {
            if (string.IsNullOrEmpty(record.meshAssetName)) continue;
            if (!string.IsNullOrEmpty(onlyNodeId) && record.nodeId != onlyNodeId) continue;

            var arena = _data.arenas.Find(a => a.nodeId == record.nodeId);

            ArenaArchwayProfile settled;
            ArchwaySite         site = default;
            if (arena != null)
            {
                site      = ArenaEntranceSites(arena)
                    .FirstOrDefault(x => x.entranceIndex == record.entranceIndex);
                var river = _data.ProfileFor(site.riverName);
                settled   = _data.ArchwayFor(arena).Resolve(river, _data.ArenaWallFor(arena).thickness);
            }
            else
            {
                settled = record.profile;   // the arena is gone; rebuild it as it was
            }
            if (settled == null) continue;

            var mesh = SaveGeneratedMesh(record.meshAssetName, BuildArchwayMesh(settled));
            if (mesh == null) continue;

            ApplyArenaWallMesh(record.gameObject, mesh);
            if (arena != null)
                ApplyArchwayDoor(record.gameObject, arena, site, settled, record.meshAssetName);
            record.profile = settled;
            EditorUtility.SetDirty(record);
            rebuilt++;
        }
        return rebuilt;
    }

    /// <summary>
    /// The door filling an archway's opening, as a "Door" child of the arch so it stands in the
    /// arch's own frame. It runs from the channel floor of the river at that entrance up to the
    /// underside of the crown, and stands as far back along the tunnel as the entrance's Along —
    /// the same place the entrance prefab's wall-depth disc is put, see
    /// <see cref="TryEntranceInArchway"/>, so the door and the prefab meet.
    /// </summary>
    private void ApplyArchwayDoor(GameObject arch, LevelSelectDesignerData.DesignerArena arena,
                                  ArchwaySite site, ArenaArchwayProfile settled, string archName)
    {
        var   river = _data.ProfileFor(site.riverName);
        float floor = river != null ? river.riverDepth : 0.16f;
        float along = DoorAlong(arena, site.nodeId, site.entranceIndex);

        // The door stands on the water, which sits below the arch's rim-top origin.
        float waterY = -BoatSplineDrop;
        var   shape  = _data.DoorFor(arena).Resolve(settled, waterY);

        string doorName = archName.Replace("ArenaArchway_", "ArenaDoor_");
        var    mesh     = SaveGeneratedMesh(doorName,
                              BuildArenaDoorMesh(settled, shape, floor, waterY, along));
        if (mesh == null) return;

        Transform door = arch.transform.Find(ArchwayDoorChild);
        if (door == null)
        {
            var go = new GameObject(ArchwayDoorChild);
            Undo.RegisterCreatedObjectUndo(go, "Generate Archway Door");
            go.transform.SetParent(arch.transform, false);
            door = go.transform;
        }
        door.localPosition = Vector3.zero;
        door.localRotation = Quaternion.identity;
        door.localScale    = Vector3.one;

        var filter = door.GetComponent<MeshFilter>();
        if (filter == null) filter = door.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);

        var renderer = door.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = door.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _data.arenaDoorMaterial != null ? _data.arenaDoorMaterial
                                : _data.arenaWallMaterial != null ? _data.arenaWallMaterial
                                : _data.riverMaterial;
        EditorUtility.SetDirty(renderer);

        ApplyDoorNumeral(door.gameObject, arena, shape, floor, waterY, along);
    }

    private const string ArchwayDoorChild    = "Door";
    private const string ArchwayNumeralChild = "Numeral";
    private const string NumeralQuadMeshName = "ArenaNumeralQuad";

    /// <summary>
    /// Which arena this is, as the number shown on its door: where it sits in the designer
    /// data's own list of arenas, counting from 1. Reordering that list renumbers the doors,
    /// which is the point — the list is the running order of the world.
    /// </summary>
    private int ArenaNumber(LevelSelectDesignerData.DesignerArena arena)
    {
        if (_data == null || arena == null) return 0;
        return _data.arenas.IndexOf(arena) + 1;
    }

    /// <summary>
    /// The arena's numeral, standing on the disc on its door as a flat sheet of its own — a
    /// "Numeral" child of the Door.
    ///
    /// Its own object rather than part of the door mesh, because it is a DRAWING and the door is
    /// stone: it needs its own material, and the door carries one for the whole of it. Standing
    /// it separately also means its size can be moved without rebuilding the door, and a numeral
    /// nobody has drawn yet simply leaves the disc blank instead of leaving a hole in the stone.
    ///
    /// <paramref name="door"/> is the door object, so the numeral lands in the archway's frame,
    /// which is what <see cref="ArenaDoorMesh.TryNumeralFace"/> answers in.
    /// </summary>
    private void ApplyDoorNumeral(GameObject door, LevelSelectDesignerData.DesignerArena arena,
                                  ArenaDoorProfile shape, float floor, float waterY, float along)
    {
        Transform existing = door.transform.Find(ArchwayNumeralChild);

        var mat = ArenaNumeralLibrary.MaterialFor(ArenaNumber(arena), _data.arenaNumeralMaterial,
                                                  out Texture2D drawing);

        // No disc to draw on, no drawing for this arena, or nothing to show it on: the disc is
        // left blank, and any numeral standing there from a previous generate is taken away.
        if (mat == null ||
            !ArenaDoorMesh.TryNumeralFace(shape, floor, waterY, along,
                                          out Vector3 centre, out float span))
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            return;
        }

        // The drawing's longest side spans what the shape asked for and the other follows its
        // own proportions, so a tall numeral comes out tall rather than stretched square.
        float w = drawing.width, h = drawing.height;
        float wide = w >= h ? span : span * w / Mathf.Max(1f, h);
        float tall = h >= w ? span : span * h / Mathf.Max(1f, w);

        if (existing == null)
        {
            var go = new GameObject(ArchwayNumeralChild);
            Undo.RegisterCreatedObjectUndo(go, "Generate Door Numeral");
            go.transform.SetParent(door.transform, false);
            existing = go.transform;
        }
        existing.localPosition = centre;
        existing.localRotation = Quaternion.identity;
        existing.localScale    = new Vector3(wide, tall, 1f);

        var filter = existing.GetComponent<MeshFilter>();
        if (filter == null) filter = existing.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = SaveGeneratedMesh(NumeralQuadMeshName, UnitNumeralQuad());
        EditorUtility.SetDirty(filter);

        var numeral = existing.GetComponent<MeshRenderer>();
        if (numeral == null) numeral = existing.gameObject.AddComponent<MeshRenderer>();
        numeral.sharedMaterial = mat;
        EditorUtility.SetDirty(numeral);
    }

    /// <summary>
    /// The sheet every numeral is drawn on: one unit square facing the river, scaled to the
    /// drawing it carries. One mesh for all of them, because the only thing that differs between
    /// two numerals is how big they are, and that is what a scale is for.
    /// </summary>
    private static Mesh UnitNumeralQuad()
    {
        var mesh = new Mesh { name = NumeralQuadMeshName };
        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f), new Vector3( 0.5f, -0.5f, 0f),
        });
        mesh.SetNormals(new List<Vector3>
        {
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
        });
        mesh.SetUVs(0, new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
        });
        // Wound so the face looks out along +z, the way the door's disc does — toward the river
        // and the boat coming in. From inside the arena it is culled, which is right: the number
        // is for whoever is arriving.
        mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Where an entrance prefab stands when its arena has archways, and which way it faces. False
    /// when there is no arch there — the entrance then keeps its old place at the arena centre.
    ///
    /// Placed off the prefab's PrefabBaselineAlignment. Its wall-depth disc — the aligner, carried
    /// Wall Depth along its wall axis — lands on the water surface on the arch's centreline, as
    /// far into the arch as the door sheet stands (<see cref="DoorAlong"/>). With a forward
    /// override the prefab turns so FORWARD points down the river, away from the arena, and UP
    /// points world-up — the OPPOSITE of LevelSpawner in-level, where FORWARD faces the arena
    /// centre. Without one it keeps <paramref name="rotation"/> as handed in.
    /// </summary>
    private bool TryEntranceInArchway(LevelSelectDesignerData.DesignerArena arena,
                                      string entranceNodeId, int entranceIndex,
                                      out Vector3 position, ref Quaternion rotation)
    {
        position = Vector3.zero;
        if (arena == null || !arena.archwayOnEntrances) return false;

        var site = ArenaEntranceSites(arena).FirstOrDefault(s => s.entranceIndex == entranceIndex);
        if (site.outward.sqrMagnitude < 0.0001f) return false;   // no door of that index

        // The same place the arch itself is stood: on the wall's inner face, facing out.
        Vector3    archPos = GetTrueArenaCenter(arena) + site.outward * _data.ArenaWallFor(arena).radius;
        Quaternion archRot = Quaternion.LookRotation(site.outward, Vector3.up);

        // On the centreline, on the water - the arch's y = 0 is the rim top, the water is
        // BoatSplineDrop below it - at the depth the door sheet stands.
        float   along  = DoorAlong(arena, entranceNodeId, entranceIndex);
        Vector3 target = archPos + archRot * new Vector3(0f, -BoatSplineDrop, along);

        var prefab  = _data.arenaEntrancePrefab;
        var aligner = prefab != null ? prefab.GetComponentInChildren<PrefabBaselineAlignment>(true) : null;
        if (aligner == null)
        {
            Debug.LogWarning($"[LevelSelectDesigner] {(prefab != null ? prefab.name : "Entrance prefab")} " +
                             "has no PrefabBaselineAlignment — placed by its root instead.");
            position = target;
            return true;
        }

        // What the aligner says, in the prefab root's own frame.
        Transform  root   = prefab.transform;
        Quaternion toRoot = Quaternion.Inverse(root.rotation);
        Vector3    disc   = toRoot * (aligner.transform.position - root.position);
        if (aligner.UseWallDepth)
            disc += toRoot * aligner.transform.TransformDirection(aligner.WallDepthLocalAxis).normalized
                           * aligner.WallDepth;

        if (aligner.UseForwardOverride)
        {
            Vector3 fwd = toRoot * aligner.transform.TransformDirection(aligner.LocalForward);
            Vector3 up  = toRoot * aligner.transform.TransformDirection(aligner.LocalUp);
            rotation = Quaternion.LookRotation(site.outward, Vector3.up)
                     * Quaternion.Inverse(Quaternion.LookRotation(fwd, up));
        }

        position = target - rotation * disc;
        return true;
    }

    private static string ArchwayKey(string nodeId, int entranceIndex) => $"{nodeId}#{entranceIndex}";

    private static string ArchwayMeshName(LevelSelectDesignerData.DesignerArena arena, int entranceIndex)
        => $"ArenaArchway_{SanitiseAssetName(arena.nodeId)}_{entranceIndex}";

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
            //
            // The gizmo's radius is taken from the wall that was just generated, the way
            // ArenaWallsGenerator writes back to BaselineMarker.discRadius in-level. The wall
            // IS the boundary, so everything reading the gizmo — the entrance direction lines,
            // the ring on the canvas — measures against the surface really standing there.
            var radiusGizmo = arenaGO.GetComponentInChildren<LevelSelectArenaRadiusGizmo>();
            if (radiusGizmo != null)
            {
                Undo.RecordObject(radiusGizmo, "Set Gizmo North");
                radiusGizmo.northOffset = 180f;
                radiusGizmo.radius      = _data.ArenaWallFor(arena).radius;
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
            Quaternion desiredWorld = Quaternion.Euler(-90f, arrivalYAngle, 0f);
            if (TryEntranceInArchway(arena, arena.nodeId, arena.entranceIndex, out Vector3 entPos, ref desiredWorld))
                entGO.transform.position = entPos;
            else
                entGO.transform.localPosition = Vector3.zero;
            entGO.transform.localRotation = Quaternion.Inverse(arenaGO.transform.rotation) * desiredWorld;
            entGO.name = "Entrance_0";

            if (ctrl != null)
            {
                var trigger = entGO.GetComponentInChildren<LevelSelectEnter>();
                var link    = new LevelSelectArenaController.PortalLink
                {
                    entranceIndex = 0,
                    trigger       = trigger
                };
                StampBoatExit(link, arena, arena.nodeId);
                ctrl.portalLinks.Add(link);

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
                Quaternion secRot = Quaternion.Euler(-90f, secRotationY, 0f);
                if (TryEntranceInArchway(arena, secEnt.nodeId, secEnt.entranceIndex, out Vector3 secPos, ref secRot))
                    secEntGO.transform.position = secPos;
                else
                    secEntGO.transform.localPosition = Vector3.zero;
                secEntGO.transform.rotation = secRot;
                secEntGO.name = $"Entrance_{secEnt.entranceIndex}";

                if (ctrl != null)
                {
                    var secTrigger = secEntGO.GetComponentInChildren<LevelSelectEnter>();
                    var secLink    = new LevelSelectArenaController.PortalLink
                    {
                        entranceIndex = secEnt.entranceIndex,
                        trigger       = secTrigger
                    };
                    StampBoatExit(secLink, arena, secEnt.nodeId);
                    ctrl.portalLinks.Add(secLink);

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

    private void PruneLooseNodes()
    {
        var referenced = new HashSet<string>(_data.paths.SelectMany(p => p.nodeIds));
        int before = _data.nodes.Count;
        _data.nodes.RemoveAll(n => !referenced.Contains(n.id));
        _data.pools.RemoveAll(p => !referenced.Contains(p.nodeId));
        if (_data.nodes.Count != before)
            MarkDirty();
    }

    /// <summary>
    /// Every container the designer empties. The script object parents are in the list so that
    /// an explicit, unlocked Clear can still reach them — a GENERATE never does.
    /// </summary>
    private static readonly string[] ClearableParents =
    {
        "MAINRIVERVISUALS", "RIVERBRANCHES", "RIVERJUNCTIONS", "RIVERGATEsobstacles",
        "RIVERPOOLS", "OUTPOSTS", "PIPES", "WALLS", "ARENAS", "SHOPS", "BoatPaths", "LANDSCAPETILES",
        MusicTriggersParent, CinematicTriggersParent,
        "LEVELSELECT_SCRIPTS", "PlayerBoat", "CANVAS", "RiverExtrusion", "CAMERA"
    };

    /// <summary>
    /// Wipes generated scene content, leaving the <see cref="ScriptObjectParents"/> standing
    /// unless the caller asks for a full clear (clearScripts: true) — and the lock overrides
    /// even that.
    /// </summary>
    private void ClearGeneratedObjects(bool clearScripts = true)
    {
        if (clearScripts && _scriptsLocked)
        {
            clearScripts = false;
            Debug.Log("[LevelSelectDesigner] Script objects are locked — the clear left them standing.");
        }

        foreach (var name in ClearableParents)
        {
            if (!clearScripts && Array.IndexOf(ScriptObjectParents, name) >= 0) continue;

            var go = GameObject.Find(name);
            if (go == null) continue;
            var children = Enumerable.Range(0, go.transform.childCount)
                .Select(i => go.transform.GetChild(i).gameObject).ToList();
            foreach (var child in children) Undo.DestroyObjectImmediate(child);
        }

        // Clear scene object references so deploy status resets
        if (clearScripts && _data != null)
        {
            Undo.RecordObject(_data, "Clear Scene References");
            _data.dataController   = null;
            _data.boatControl      = null;
            _data.cameraController      = null;
            _data.soulDisplaySlotManager = null;
            _data.musicController        = null;
            _data.pauseManager           = null;
            MarkDirty();
        }
    }

    /// <summary>
    /// Finds a container without making one. Asking whether something is deployed must never
    /// leave an empty container behind, which is what happened when a deploy checked through
    /// <see cref="FindOrCreateParent"/> and then bailed.
    /// </summary>
    private static GameObject FindParent(string name) => GameObject.Find(name);

    private static GameObject FindOrCreateParent(string name)
    {
        var go = GameObject.Find(name);
        if (go != null) return go;
        go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Parent");
        return go;
    }

    // ── Spline building ───────────────────────────────────────────
    private Spline SplineFromPath(Transform space, LevelSelectDesignerData.DesignerPath path)
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

        var spline = SplineFromPositions(space, positions, path.curveStrength);
        if (spline.Count < 2) return spline;

        // ── 1. Trim leading end (Junctions) ───────────────────────
        // Cut back the collar, so the whole of this branch's last ring stands clear of the
        // river it leaves and the mouth patch there picks its section up.
        string firstNodeId = path.nodeIds[0];
        string lastNodeId  = path.nodeIds[path.nodeIds.Count - 1];

        float startTrim = 0f;
        if (isJunctionStart && _junctionBranchStart.TryGetValue(junctionNodeId, out float collar))
            startTrim = collar;

        if (isJunctionStart && startTrim > 0f)
        {
            SplineUtility.GetPointAtLinearDistance(spline, 0f, startTrim, out float T_start);

            int subs  = path.curveSubdivisions > 0 ? path.curveSubdivisions : 5;
            int steps = subs * Mathf.Max(1, spline.Count - 1);

            // The mouth patch picks the section up on the branch's own last ring, so that
            // ring has to be exactly where the patch expects it: a collar out along the
            // heading the branch leaves on, not a length measured round its own curve. The
            // start tangent is forced to that same heading below, so the two agree.
            const float MinStartGap = 0.02f;

            float3 startP = spline.EvaluatePosition(T_start);
            if (_correctedNodePositions.TryGetValue(junctionNodeId, out Vector3 juncWorld) &&
                _junctionPerpDirections.TryGetValue(junctionNodeId, out Vector3 leaveDir) &&
                leaveDir.sqrMagnitude > 0.0001f)
            {
                Vector3 local = space.InverseTransformPoint(
                    juncWorld + leaveDir.normalized * startTrim);
                startP = new float3(local.x, local.y, local.z);
            }

            var trimPos = new List<float3> { startP };
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                if (t <= T_start) continue;

                float3 p = spline.EvaluatePosition(t);
                if (math.distance(trimPos[trimPos.Count - 1], p) < MinStartGap) continue;
                trimPos.Add(p);
            }
            float3 endP = spline.EvaluatePosition(1f);
            if (trimPos.Count < 2 || math.distance(trimPos[trimPos.Count - 1], endP) > 0.001f)
                trimPos.Add(endP);

            if (trimPos.Count >= 2)
                spline = SplineSplitUtility.BuildSplineFromPositions(trimPos, TangentMode.AutoSmooth);
        }

        // ── 1b. Trim either end against a pool ────────────────────
        // A river meeting a pool is cut exactly where its curve crosses the pool's outer wall,
        // not a length measured back along itself — that is what puts its rim corners on the
        // pool's rim corners however it happened to curve in. No designer offset either: the
        // two are one piece of water, so the run butts straight onto the wall.
        bool poolAtEnd   = TrimAgainstPool(ref spline, space, path, lastNodeId,  fromEnd: true);
        bool poolAtStart = TrimAgainstPool(ref spline, space, path, firstNodeId, fromEnd: false);

        if (spline.Count < 2) return spline;

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
        else if (_riverJoinHeadings.TryGetValue(startNodeId, out var joinDir))
        {
            // Carrying on from a river split: the heading the river before it ends on.
            forceDir   = joinDir;
            forceStart = true;
        }

        if (forceStart && spline.Count > 0)
        {
            // Broken FIRST, then the knot. A knot written while it is still AutoSmooth has
            // both its tangents re-derived from its neighbours on the way in, so a heading
            // assigned before the mode change is thrown straight away and the branch leaves
            // on the curve's own tangent instead — a few degrees off the mouth waiting for
            // it, which opens the hairline gap where the two rims should meet. The mouth
            // patch is built off this heading, so the heading wins over the drawn curve.
            spline.SetTangentMode(0, TangentMode.Broken);

            var   knot   = spline[0];
            float tanLen = math.length(knot.TangentOut);
            if (tanLen < 0.0001f) tanLen = 0.333f;

            // A knot holds its tangents in its OWN rotation frame — an auto-smoothed one
            // carries (0, 0, length) and keeps the heading in its rotation. So the heading
            // goes into the rotation and the tangent stays a length along local forward.
            // Writing the heading straight into TangentOut has it rotated a second time and
            // folds the first stretch of the run back over itself.
            SetKnotHeading(spline, 0, space.InverseTransformDirection(forceDir),
                           tanLen, leading: true);
        }

        // ── 3. Force end tangent (Arena Entrance or River Split) ──
        string endNodeId = path.nodeIds[path.nodeIds.Count - 1];
        if ((_arenaEntranceDirections.TryGetValue(endNodeId, out var entDir) ||
             _riverJoinHeadings.TryGetValue(endNodeId, out entDir)) && spline.Count > 0)
        {
            // Broken first, and the heading into the rotation, for the same reasons as the
            // start above. The tangent leading in points back up the curve, so it is the
            // length along local BACKWARD.
            int last = spline.Count - 1;
            spline.SetTangentMode(last, TangentMode.Broken);

            var   knot   = spline[last];
            float tanLen = math.length(knot.TangentIn);
            if (tanLen < 0.0001f) tanLen = 0.333f;

            SetKnotHeading(spline, last, space.InverseTransformDirection(entDir),
                           tanLen, leading: false);
        }

        // ── 4. Record where each end meeting a pool came to rest ──
        // Last of all, and only once every cut and every forced heading is in: the mouth patch
        // in the pool is cut on this frame, and the run's own last ring is swept on it, so it
        // has to be the frame the curve finally ends with. A river cut at both ends has its far
        // end resampled by the second cut, which is why neither is read as it is made.
        if (poolAtEnd)   RecordPoolArrival(spline, space, path, lastNodeId,  fromEnd: true);
        if (poolAtStart) RecordPoolArrival(spline, space, path, firstNodeId, fromEnd: false);

        return spline;
    }

    /// <summary>
    /// Points one knot of a spline along <paramref name="heading"/>, in the spline's own space.
    ///
    /// A <see cref="BezierKnot"/> holds its tangents in its own rotation frame: the curve
    /// through it is built from <c>Position + rotate(Rotation, Tangent)</c>, and an
    /// auto-smoothed knot carries nothing but a length along local forward, with the whole
    /// heading in its rotation. So a heading is set by turning the knot, not by writing a
    /// direction into the tangent — that would be rotated a second time and fold the curve
    /// back over itself.
    ///
    /// The knot has to already be <see cref="TangentMode.Broken"/>, or writing it re-derives
    /// both tangents from its neighbours and throws the heading away.
    /// </summary>
    private static void SetKnotHeading(
        Spline spline, int index, Vector3 heading, float tangentLength, bool leading)
    {
        if (heading.sqrMagnitude < 1e-8f) return;
        heading.Normalize();

        Vector3 up = Mathf.Abs(Vector3.Dot(heading, Vector3.up)) > 0.999f
                   ? Vector3.forward : Vector3.up;

        var knot = spline[index];
        knot.Rotation = (quaternion)Quaternion.LookRotation(heading, up);

        if (leading) knot.TangentOut = new float3(0f, 0f,  tangentLength);
        else         knot.TangentIn  = new float3(0f, 0f, -tangentLength);

        spline.SetKnot(index, knot);
    }

    /// <summary>
    /// Cuts one end of a river off at the pool it meets — exactly where its curve crosses the
    /// pool's outer wall. Returns whether it cut anything; where the run ended up is read back
    /// afterwards by <see cref="RecordPoolArrival"/>, once both ends have been cut.
    ///
    /// Does nothing when that end carries no pool.
    /// </summary>
    private bool TrimAgainstPool(
        ref Spline spline, Transform space, LevelSelectDesignerData.DesignerPath path,
        string nodeId, bool fromEnd)
    {
        if (!_poolEdgeDistance.TryGetValue(nodeId, out float edge))  return false;
        if (!_poolCentre.TryGetValue(nodeId, out Vector3 poolWorld)) return false;
        if (spline == null || spline.Count < 2) return false;

        Vector3 centreLocal = space.InverseTransformPoint(poolWorld);
        if (!SolveCircleCrossing(spline, centreLocal, edge, fromEnd, out float tCut)) return false;

        int subs  = path.curveSubdivisions > 0 ? path.curveSubdivisions : 5;
        int steps = subs * Mathf.Max(1, spline.Count - 1);

        // The cut lands wherever the circle happens to be, which is usually just past one of
        // these samples. Two knots almost on top of each other send AutoSmooth wild and the
        // last ring of the sweep comes out skewed — so a sample that close to the cut is
        // dropped rather than kept alongside it.
        const float MinKnotGap = 0.02f;

        var positions = new List<float3>();
        void Place(float3 p)
        {
            if (positions.Count > 0 &&
                math.distance(positions[positions.Count - 1], p) < MinKnotGap) return;
            positions.Add(p);
        }

        if (!fromEnd) Place(spline.EvaluatePosition(tCut));
        else          Place(spline.EvaluatePosition(0f));

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            if (fromEnd ? (t > 0f && t < tCut) : (t > tCut && t < 1f))
                Place(spline.EvaluatePosition(t));
        }

        // The cut itself is the end of the run and has to be exact, so it replaces any sample
        // that crowded up against it rather than the other way round.
        float3 last = spline.EvaluatePosition(fromEnd ? tCut : 1f);
        if (positions.Count > 0 &&
            math.distance(positions[positions.Count - 1], last) < MinKnotGap)
            positions[positions.Count - 1] = last;
        else
            positions.Add(last);

        if (positions.Count < 2) return false;

        spline = SplineSplitUtility.BuildSplineFromPositions(positions, TangentMode.AutoSmooth);

        // This end now runs into the pool, so the run is left open there.
        (fromEnd ? _poolOpenEnd : _poolOpenStart).Add(path.pathId);
        return true;
    }

    /// <summary>
    /// Records where a run trimmed against a pool actually ended up, so the mouth cut through
    /// the pool's rim is placed on that run's own last ring and bearing.
    ///
    /// Both are read off the run as it now stands, NOT off the curve it was cut from. The
    /// mouth patch starts on the run's last ring, and that ring is swept on the frame the
    /// trimmed curve really ends with — an auto-smoothed chord back to the sample before it,
    /// a degree or two off the tangent the original curve had at the cut. Reading the old
    /// curve is what left the run and the pool a sliver apart.
    ///
    /// Called after BOTH ends have been cut: a river that meets a pool at each end has its far
    /// end resampled by the second cut, so anything read during the first one is out of date.
    /// </summary>
    private void RecordPoolArrival(
        Spline spline, Transform space, LevelSelectDesignerData.DesignerPath path,
        string nodeId, bool fromEnd)
    {
        if (spline == null || spline.Count < 2) return;

        float   t        = fromEnd ? 1f : 0f;
        float3  cutLocal = spline.EvaluatePosition(t);
        float3  tanLocal = spline.EvaluateTangent(t);

        Vector3 outward = space.TransformDirection(
            new Vector3(tanLocal.x, 0f, tanLocal.z));
        if (fromEnd) outward = -outward;
        outward.y = 0f;
        if (outward.sqrMagnitude < 1e-8f) return;

        if (!_poolArrivals.TryGetValue(nodeId, out var list))
            _poolArrivals[nodeId] = list = new List<PoolArrival>();

        list.Add(new PoolArrival
        {
            riverName = path.riverName,
            worldPos  = space.TransformPoint(
                            new Vector3(cutLocal.x, cutLocal.y, cutLocal.z)),
            outward   = outward.normalized,
        });
    }

    /// <summary>
    /// Where a spline crosses a circle in the XZ plane, walking in from one end until it first
    /// gets outside. False when it never does — the caller then leaves the run alone rather than
    /// trimming it to nothing.
    /// </summary>
    private static bool SolveCircleCrossing(
        Spline spline, Vector3 centreLocal, float radius, bool fromEnd, out float t)
    {
        const int Steps = 512;
        t = fromEnd ? 1f : 0f;

        float Outside(float u)
        {
            float3  p = spline.EvaluatePosition(u);
            Vector2 d = new Vector2(p.x - centreLocal.x, p.z - centreLocal.z);
            return d.magnitude - radius;
        }

        float prevU = fromEnd ? 1f : 0f;
        float prev  = Outside(prevU);
        if (prev >= 0f) return false;               // never started inside the pool

        for (int i = 1; i <= Steps; i++)
        {
            float u = fromEnd ? 1f - (float)i / Steps : (float)i / Steps;
            if (Outside(u) < 0f) { prevU = u; continue; }

            // Straddled it — close in on the crossing.
            float inside = prevU, outside = u;
            for (int k = 0; k < 32; k++)
            {
                float mid = (inside + outside) * 0.5f;
                if (Outside(mid) < 0f) inside = mid; else outside = mid;
            }
            t = (inside + outside) * 0.5f;
            return true;
        }
        return false;
    }

    // Actual smooth-spline world positions for junction nodes, populated during generation.
    // Overrides the raw stored position so branch splines start exactly where the main
    // river split landed on the smooth curve.
    private readonly Dictionary<string, Vector3> _correctedNodePositions = new();

    // Perpendicular-to-main-river direction per junction node.
    // Used to force the branch's first segment to exit at 90° from the main river tangent.
    private readonly Dictionary<string, Vector3> _junctionPerpDirections = new();

    // How far along a branch its own run starts, so it butts onto the side wall of
    // the river it joins instead of running through it.
    private readonly Dictionary<string, float> _junctionBranchStart = new();

    // How far back past that start the branch's water reaches, to meet the water of the
    // river it joins across the mouth cut between them.
    private readonly Dictionary<string, float> _junctionWaterLeadIn = new();

    // The river a branch leaves, as a line across the junction: a point on its centreline, the
    // flat direction out toward the branch, and that river's name.
    private readonly Dictionary<string, (Vector3 point, Vector3 across, string river)> _junctionJoin = new();

    // The one heading both rivers take through a river join, keyed by the join's node. A run
    // ending or starting on one of these is left open there. Filled on Generate.
    private readonly Dictionary<string, Vector3> _riverJoinHeadings = new();

    // How far from a pool's centre a river arriving at it has to stop, so it butts onto the
    // pool's outer wall instead of running through it. Keyed by the pool's node.
    private readonly Dictionary<string, float> _poolEdgeDistance = new();

    // Where each pool sits, so an arriving river can be trimmed against the real circle rather
    // than a length measured back along its own curve.
    private readonly Dictionary<string, Vector3> _poolCentre = new();

    // How far past that stop the river's water carries on, to reach the water in the pool.
    private readonly Dictionary<string, float> _poolWaterReach = new();

    /// <summary>
    /// Where a river actually meets a pool, taken off the run once it has been trimmed — so the
    /// mouth cut through the pool's rim lands on the run's real centreline and bearing, and its
    /// corners meet the pool's rim corners however the river happened to curve in.
    /// </summary>
    private struct PoolArrival
    {
        public string  riverName;
        public Vector3 worldPos;   // centre of the run's last ring, standing off the pool's wall
        public Vector3 outward;    // unit, flat, pointing back up the river
    }

    private readonly Dictionary<string, List<PoolArrival>> _poolArrivals = new();

    // Paths whose run stops at a pool, and at which end. That end is left uncapped: the pool's
    // mouth patch picks the section up from the run's last ring and carries it round, so a cap
    // there would be a wall across the middle of the join.
    private readonly HashSet<string> _poolOpenStart = new();
    private readonly HashSet<string> _poolOpenEnd   = new();

    /// <summary>
    /// The one number every generated river mesh is built from: the length an edge is aimed at,
    /// anywhere in any of them. Ring spacing along a run, the columns across its section, a
    /// pool's rings and its columns all come off this, so nothing carries a density of its own.
    /// </summary>
    private float MeshEdge => Mathf.Max(0.01f, _data.splineInstantiateSpacing);

    // Direction INTO the arena per secondary entrance node.
    // Used to align entrance/exit paths with the spawned gate prefabs.
    private readonly Dictionary<string, Vector3> _arenaEntranceDirections = new();

    private Vector3 WorldPosOfNode(string nodeId)
    {
        if (_correctedNodePositions.TryGetValue(nodeId, out var corrected))
            return corrected;
        return _data.nodes.Find(n => n.id == nodeId)?.worldPosition ?? Vector3.zero;
    }

    private static Spline SplineFromPositions(Transform space, List<Vector3> worldPositions, float curveStrength = 1f)
    {
        float tangentScale = 0.333f * Mathf.Max(0.01f, curveStrength);

        var local = worldPositions
            .Select(p => (float3)space.InverseTransformPoint(p))
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
        return spline;
    }

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

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.D && _selectedHillPointId != null)
        {
            int index = _data.hillPoints.FindIndex(h => h.id == _selectedHillPointId);
            if (index >= 0)
            {
                DuplicateHillPoint(index);
                Repaint();
                e.Use();
            }
        }
    }

    // Copies the hill point at index, placed just beside it, and selects the copy.
    private void DuplicateHillPoint(int index)
    {
        var hp = _data.hillPoints[index];
        Undo.RecordObject(_data, "Duplicate Hill Point");
        var dupe = new LevelSelectDesignerData.LandscapeHillPoint
        {
            id         = System.Guid.NewGuid().ToString(),
            positionXZ = hp.positionXZ + new Vector2(2f, 2f),
            scale      = hp.scale,
            height     = hp.height,
            smoothness = hp.smoothness,
            noise      = hp.noise,
            omitFromFog = hp.omitFromFog,
        };
        _data.hillPoints.Insert(index + 1, dupe);
        _selectedHillPointId = dupe.id;
        MarkDirty();
        SyncHillPointsToScene();
    }

    private void DrawLandscapePanel()
    {
        if (_data == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Landscape Tiles", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(_sourceData == null))
        {
            if (GUILayout.Button(new GUIContent("Revert Landscape to Saved",
                    "Put the landscape settings and hill points below back to how they are in the " +
                    "saved designer asset. Everything else in the workspace is left as it is.")) &&
                EditorUtility.DisplayDialog("Revert Landscape",
                    $"Revert the landscape settings and hill points to those saved in '{_sourceData.name}'?",
                    "Revert", "Cancel"))
            {
                RevertLandscapeToSaved();
            }
        }

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

        EditorGUI.BeginChangeCheck();
        _data.landscapeHeightOffset = EditorGUILayout.FloatField("Height Offset", _data.landscapeHeightOffset);
        if (EditorGUI.EndChangeCheck())
        {
            MarkDirty();
            RebuildAfterEdit("LandscapeHeight", ApplyLandscapeHeight);
            Repaint();
        }
        EditorGUILayout.LabelField(
            $"Tile surface sits at Y {_data.landscapeWorldY + _data.landscapeHeightOffset:0.###}",
            EditorStyles.miniLabel);

        EditorGUI.BeginChangeCheck();
        _data.landscapeTileSubdivisions = EditorGUILayout.IntSlider(
            "Subdivisions", _data.landscapeTileSubdivisions, 1, 100);
        if (EditorGUI.EndChangeCheck())
        {
            MarkDirty();
            RebuildAfterEdit("LandscapeSubdivisions", ApplyLandscapeSubdivisions);
            Repaint();
        }

        EditorGUI.BeginChangeCheck();
        float noiseScale = EditorGUILayout.FloatField(
            new GUIContent("Noise Scale", "How fine the rocky noise on hills is — its frequency " +
                                          "across the world. Only hills with Rocky Noise show it."),
            _data.landscapeNoiseScale);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Landscape Noise Scale");
            _data.landscapeNoiseScale = noiseScale;
            MarkDirty();
            RebuildAfterEdit("HillPoints", SyncHillPointsToScene);
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
        EditorGUILayout.LabelField("Drag points on canvas  |  D duplicates  |  Delete key removes", EditorStyles.miniLabel);
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
            string kind = hp.scale > 50f ? "Base" : hp.height < 0f ? "Hole" : "Hill";
            if (GUILayout.Button(sel ? $"● {kind} {i}" : $"  {kind} {i}", EditorStyles.miniButton))
            {
                _selectedHillPointId = sel ? null : hp.id;
                Repaint();
            }
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("❐", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                DuplicateHillPoint(i);
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
                hp.scale      = EditorGUILayout.Slider("Radius",          hp.scale,  0.1f, 250f);
                hp.height     = EditorGUILayout.Slider("Height (- = hole)", hp.height, -50f, 50f);
                hp.smoothness = EditorGUILayout.Slider(
                    new GUIContent("Smoothness", "1 = smooth rounded hill. Lower flattens the top " +
                                                 "and steepens the sides; 0 is a plateau with " +
                                                 "near-vertical cliffs."),
                    hp.smoothness, 0f, 1f);
                hp.noise      = EditorGUILayout.Slider(
                    new GUIContent("Rocky Noise", "Rocky noise across this hill's shape. 0 = none."),
                    hp.noise, 0f, 1f);
                hp.omitFromFog = EditorGUILayout.Toggle(
                    new GUIContent("Omit from Fog", "Fog zone sheets bend over the ground as if " +
                                                    "this point weren't there (their Landscape " +
                                                    "Influence skips it). The landscape itself is " +
                                                    "unchanged. Takes effect when the fog zones " +
                                                    "next generate."),
                    hp.omitFromFog);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Edit Hill Point");
                    EditorUtility.SetDirty(_data);
                    RebuildAfterEdit("HillPoints", SyncHillPointsToScene);
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

    /// Copies the Landscape panel's fields back from the asset on disk into the workspace, then
    /// pushes them to the scene the same way editing them does. Tile size/count/offset still
    /// need Generate Tiles to rebuild, as when they are typed in.
    private void RevertLandscapeToSaved()
    {
        if (_sourceData == null || _data == null) return;

        Undo.RecordObject(_data, "Revert Landscape to Saved");
        _data.landscapeTileSize         = _sourceData.landscapeTileSize;
        _data.landscapeTilesX           = _sourceData.landscapeTilesX;
        _data.landscapeTilesZ           = _sourceData.landscapeTilesZ;
        _data.landscapeOffset           = _sourceData.landscapeOffset;
        _data.landscapeWorldY           = _sourceData.landscapeWorldY;
        _data.landscapeHeightOffset     = _sourceData.landscapeHeightOffset;
        _data.landscapeTileSubdivisions = _sourceData.landscapeTileSubdivisions;
        _data.landscapeNoiseScale       = _sourceData.landscapeNoiseScale;
        _data.hillPoints = _sourceData.hillPoints
            .Select(hp => JsonUtility.FromJson<LevelSelectDesignerData.LandscapeHillPoint>(JsonUtility.ToJson(hp)))
            .ToList();

        if (_selectedHillPointId != null && !_data.hillPoints.Exists(hp => hp.id == _selectedHillPointId))
            _selectedHillPointId = null;

        MarkDirty();
        ApplyLandscapeHeight();
        ApplyLandscapeSubdivisions();
        SyncHillPointsToScene();
        Repaint();
    }

    private void DrawLandscapeTilesOnCanvas()
    {
        if (_data == null || Event.current.type != EventType.Repaint) return;
        if (_data.landscapeTilesX <= 0 || _data.landscapeTilesZ <= 0 || _data.landscapeTileSize <= 0) return;

        float size = _data.landscapeTileSize;
        float ox   = _data.landscapeOffset.x;
        float oz   = _data.landscapeOffset.y;

        // The height map, when on, stands in for the flat tile fill and the hill discs.
        bool mapShown = DrawLandscapeMapOnCanvas();

        float a          = _landscapeCanvasAlpha;
        var fillColor    = mapShown ? Color.clear : new Color(0.38f, 0.38f, 0.38f, 0.55f * a);
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

        if (mapShown) DrawLandscapeContoursOnCanvas();

    }

    /// <summary>
    /// Hill point rings, Landscape mode only — drawn after everything else so they sit on top.
    /// </summary>
    private void DrawHillPointsOnTop()
    {
        if (_mode != DesignerMode.Landscape || Event.current.type != EventType.Repaint) return;
        foreach (var hp in _data.hillPoints)
            DrawHillPointMarker(hp, hp.id == _selectedHillPointId);
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
        float worldY = _data.landscapeWorldY + _data.landscapeHeightOffset;

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
                    tileMesh.width         = size;
                    tileMesh.depth         = size;
                    tileMesh.subdivisionsX = _data.landscapeTileSubdivisions;
                    tileMesh.subdivisionsZ = _data.landscapeTileSubdivisions;
                    tileMesh.GenerateMesh();
                }
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[LevelSelectDesigner] Generated {_data.landscapeTilesX * _data.landscapeTilesZ} landscape tiles under LANDSCAPETILES.");
    }

    /// <summary>
    /// Moves every already-generated tile to the current World Y + Height Offset, so the whole
    /// family shifts vertically without a regenerate. The hill handles are re-pinned to their own
    /// authored heights: the shader reads a handle's Y as the amount it lifts (or sinks) the tile
    /// surface beneath it, so leaving them where they are keeps every hill and dip exactly as
    /// authored relative to the tile base.
    /// </summary>
    private void ApplyLandscapeHeight()
    {
        if (_data == null) return;

        var parent = GameObject.Find("LANDSCAPETILES");
        if (parent == null) return;

        float y = _data.landscapeWorldY + _data.landscapeHeightOffset;

        foreach (Transform child in parent.transform)
        {
            if (child.name == "Hill_Points_Container") continue;
            Undo.RecordObject(child, "Landscape Height Offset");
            var p = child.position;
            child.position = new Vector3(p.x, y, p.z);
        }

        SyncHillPointsToScene();
    }

    /// <summary>
    /// Rebuilds every already-generated tile's mesh at the current Subdivisions, so the change
    /// shows without a regenerate.
    /// </summary>
    private void ApplyLandscapeSubdivisions()
    {
        if (_data == null) return;

        var parent = GameObject.Find("LANDSCAPETILES");
        if (parent == null) return;

        int subdivisions = _data.landscapeTileSubdivisions;

        foreach (Transform child in parent.transform)
        {
            var tileMesh = child.GetComponent<LandscapeTileMesh>();
            if (tileMesh == null) continue;
            Undo.RecordObject(tileMesh, "Landscape Subdivisions");
            tileMesh.subdivisionsX = subdivisions;
            tileMesh.subdivisionsZ = subdivisions;
            tileMesh.GenerateMesh();
            PrefabUtility.RecordPrefabInstancePropertyModifications(tileMesh);
        }
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

        var tool = container.parent.GetComponent<LandscapeTool>();
        if (tool != null && tool.noiseScale != _data.landscapeNoiseScale)
        {
            tool.noiseScale = _data.landscapeNoiseScale;
            EditorUtility.SetDirty(tool);
        }

        foreach (var hp in _data.hillPoints)
        {
            var go = new GameObject($"HillPoint_{hp.id.Substring(0, 6)}");
            go.transform.SetParent(container, false);
            go.transform.position   = new Vector3(hp.positionXZ.x, hp.height, hp.positionXZ.y);
            go.transform.localScale = Vector3.one * hp.scale;

            var shape = go.AddComponent<HillPoint>();
            shape.smoothness = hp.smoothness;
            shape.noise      = hp.noise;
        }

        // Spikes and structures stand on the hills, so they move with them.
        PlaceAllSpikes();
        PlaceAllStructures();
    }

    private Transform FindHillContainer()
    {
        var parent = GameObject.Find("LANDSCAPETILES");
        if (parent == null) return null;
        return parent.transform.Find("Hill_Points_Container");
    }
}

#endif
