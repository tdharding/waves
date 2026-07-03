using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

class GridSnapshot
{
    public int[] squareGrid;
    public int[] circleGrid;
    public List<GridData.ArenaEntrance>         entrances;
    public List<int>                            orbIndices;
    public List<GridData.SoulSpawnPoint>        soulSpawnPoints;
    public List<int>                            waterLevelModifierIndices;
    public List<int>                            waveModifierIndices;
    public List<int[]>                          tierCells;
    public List<GridData.PrefabPlacement>       prefabPlacements;
    public List<List<GridData.PrefabPlacement>> tierPrefabPlacements;
    public List<GridData.LinkedPrefabPair>      linkedPairs;

    public GridSnapshot(int[] square, int[] circle,
                        List<GridData.ArenaEntrance> ents,
                        List<int> orbs, List<GridData.SoulSpawnPoint> souls,
                        List<int> waterMods, List<int> waveMods,
                        List<GridData.GridTier> tiers,
                        List<GridData.PrefabPlacement> basePrefabs,
                        List<GridData.LinkedPrefabPair> links)
    {
        squareGrid = (int[])square.Clone();
        circleGrid = (int[])circle.Clone();

        entrances = new List<GridData.ArenaEntrance>();
        if (ents != null)
            foreach (var e in ents)
                entrances.Add(new GridData.ArenaEntrance
                {
                    id = e.id, prefab = e.prefab, soulPrefab = e.soulPrefab,
                    perimeterAngle = e.perimeterAngle, tierSlot = e.tierSlot,
                    spawnRadius = e.spawnRadius,
                    isLocked = e.isLocked, lockHubPrefab = e.lockHubPrefab,
                    lockHubAngle = e.lockHubAngle,
                    tubeSubdivisions = e.tubeSubdivisions,
                    tubePath = e.tubePath != null
                        ? new List<UnityEngine.Vector2Int>(e.tubePath)
                        : new List<UnityEngine.Vector2Int>()
                });

        orbIndices = new List<int>(orbs);
        soulSpawnPoints = new List<GridData.SoulSpawnPoint>();
        foreach (var s in souls)
            soulSpawnPoints.Add(new GridData.SoulSpawnPoint { cellIndex = s.cellIndex, soulData = s.soulData });
        waterLevelModifierIndices = new List<int>(waterMods);
        waveModifierIndices       = new List<int>(waveMods);

        tierCells            = new List<int[]>();
        tierPrefabPlacements = new List<List<GridData.PrefabPlacement>>();
        if (tiers != null)
            foreach (var t in tiers)
            {
                tierCells.Add(t.cells != null ? (int[])t.cells.Clone() : new int[GridData.CellCount]);
                tierPrefabPlacements.Add(CopyPlacements(t.prefabPlacements));
            }

        prefabPlacements = CopyPlacements(basePrefabs);
        linkedPairs = links != null ? new List<GridData.LinkedPrefabPair>(links) : new List<GridData.LinkedPrefabPair>();
    }

    public static List<GridData.PrefabPlacement> CopyPlacements(List<GridData.PrefabPlacement> src)
    {
        var copy = new List<GridData.PrefabPlacement>();
        if (src == null) return copy;
        foreach (var p in src)
            copy.Add(new GridData.PrefabPlacement
            {
                cellIndex                = p.cellIndex,
                prefab                   = p.prefab,
                isCircle                 = p.isCircle,
                isWorldSpaceProp         = p.isWorldSpaceProp,
                scale                    = p.scale,
                overrideModifierSettings = p.overrideModifierSettings,
                speedBoost               = p.speedBoost,
                frequencyBoost           = p.frequencyBoost,
                rippleDepthBoost         = p.rippleDepthBoost
            });
        return copy;
    }
}

public class GridDesignerWindow : EditorWindow
{
    const int GridSize      = GridData.GridSize;
    const int CellCount     = GridData.CellCount;
    const int CellSize      = 18;
    const int GridPixelSize = GridSize * CellSize;
    const string GridDataFolder = "Assets/Resources/Levels";

    int[] squareGrid = new int[CellCount];
    int[] circleGrid = new int[CellCount];

    List<Color>  slotColors = new List<Color>();
    List<string> slotNotes  = new List<string>();

    int  activeSlot;
    bool drawCircle;
    bool drawOrb;
    bool drawSoul;
    bool drawSoulArea;
    bool drawSelect;
    bool drawWaterLevelModifier;

    // Soul zone drawing state
    int  _activeSoulZoneIndex = -1;
    readonly List<int> _drawingNodes = new List<int>();
    bool _isDrawingSoulArea;

    // Select tool state
    enum SelectionType
    {
        None,
        SoulZoneNode,
        PrefabPlacement,
        Whirlpool,
        Orb,
        WaterModifier,
        WaveModifier,
        GridSlot,
        SplineWallNode   // index = path index, subIndex = node index within path
    }

    struct SelectionInfo
    {
        public SelectionType type;
        public int           cellIndex;
        public int           tierIndex; // -1 for base
        public int           index;     // index in list (e.g. soul zone index, whirlpool index)
        public int           subIndex;  // e.g. node index within soul zone
        public int           value;     // for GridSlot (slot id)
        public bool          isCircle;  // for GridSlot/Prefab
    }

    SelectionInfo _currentSelection;
    int  _selectedZoneIndex = -1;
    int  _selectedNodeIndex = -1;
    bool _isDraggingNode    = false;
    int  _dragCurrentCell   = -1;
    bool _dragUndoPushed    = false; // one undo snapshot per drag, pushed on first move

    // Bridge mode state
    bool        _isBridgeMode        = false;
    int         _bridgeEndZoneIndex  = -1;
    int         _bridgeEndNodeIndex  = -1;
    readonly List<int> _bridgeNodes  = new List<int>();

    static readonly Color[] ZonePalette =
    {
        new Color(1.0f, 0.55f, 0.0f),
        new Color(0.2f, 0.8f, 1.0f),
        new Color(0.6f, 1.0f, 0.3f),
        new Color(1.0f, 0.3f, 0.7f),
        new Color(0.9f, 0.9f, 0.2f),
        new Color(0.5f, 0.3f, 1.0f),
    };
    bool drawWaveModifier;
    int        activeTierIndex  = -1; // -1 = base layer
    List<bool> tierVisible      = new List<bool>();
    bool       baseLayerVisible = true;

    float[] cachedTierYOffsets; // pulled from LevelSpawner in scene

    // ── Direct Prefab Library ──
    enum PrefabLibraryTab { MazePieces, SetPieces, Statues, Modifiers }
    PrefabLibraryTab              _prefabLibTab       = PrefabLibraryTab.MazePieces;
    string                        prefabFolderPath    = "Assets/Prefab/MazePieces";
    string                        iconsFolderPath     = "";
    List<GameObject>              scannedPrefabs      = new List<GameObject>();
    Dictionary<string, Texture2D> prefabIcons         = new Dictionary<string, Texture2D>();
    // Caches the PrefabBaselineAlignment component per prefab asset so the scale-radius
    // overlay does not run GetComponentInChildren every repaint.
    Dictionary<GameObject, PrefabBaselineAlignment> _baselineAlignCache = new Dictionary<GameObject, PrefabBaselineAlignment>();
    int                           selectedPrefabIndex = -1;
    Vector2                       prefabScrollPos;
    List<GameObject>              scannedSetPiecesLib = new List<GameObject>();
    int                           selectedSetPieceIndex = -1;
    Vector2                       setpieceScrollPos;
    const string StatuesPrefabsFolder   = "Assets/Prefab/StatuesPrefabs";
    List<GameObject>              scannedStatuesLib   = new List<GameObject>();
    int                           selectedStatueIndex = -1;
    Vector2                       statueScrollPos;
    const string ModifiersPrefabsFolder = "Assets/Prefab/ModifierPrefabs";
    List<GameObject>              scannedModifiersLib = new List<GameObject>();
    int                           selectedModifierIndex = -1;
    Vector2                       modifierScrollPos;
    bool                          drawDirectPrefab    = false;
    GameObject                    _activePlacementPrefab;
    bool                          _activePlacementIsWorldSpaceProp = false;
    bool                          showPrefabLibrary   = true;
    bool isDragging;
    int  lastDraggedCellIndex = -1;

    // TypeB Modifier + Input Tube two-click placement
    bool _isWaitingForTubePlacement = false;
    int  _pendingModifierCellIndex  = -1;
    int  _pendingModifierTierIndex  = -1;

    // Spline wall mode
    bool _drawSplineWall      = false;
    bool _showSplineWalls     = true;
    int  _activeSplinePathIdx = 0;
    int  _dragSplinePathIdx   = -1;
    int  _dragSplineNodeIdx   = -1;

    // ── Tube path modes ──
    int _tubePlacingEntranceIndex = -1; // -1 = not in placement mode
    int _tubeDrawEntranceIndex    = -1; // -1 = not in edit/drag mode
    int _dragTubeNodeIndex        = -1; // index within tubePath being dragged
    int _selectedTubeNodeIndex    = -1; // highlighted node

    // ── Grid navigation ──
    float   _gridZoom      = 1f;
    Vector2 _gridPanOffset = Vector2.zero;
    bool    _isPanningGrid = false;

    float EffCell       => CellSize * _gridZoom;
    float ZoomedGridSize => GridPixelSize * _gridZoom;

    // ── Grid display settings (persisted via EditorPrefs) ──
    float _gridLineOpacity    = 1f;
    float _backdropBrightness = 0.08f;
    const string PrefKeyGridOpacity    = "GridDesigner_GridLineOpacity";
    const string PrefKeyBackdropBright = "GridDesigner_BackdropBrightness";

    Stack<GridSnapshot> undoStack = new Stack<GridSnapshot>();
    const int MaxUndoSteps = 50;

    // ── Section foldouts ──
    bool _showLevelIdentity  = true;
    bool _showCamera         = true;
    bool _showWavePresets    = true;
    bool _showEnemy          = true;
    bool _showTimeTrial      = false;
    bool _showPrefabs        = false;
    bool _showStartRitual    = false;
    bool _showSoulSpawns     = true;
    bool _showWhirlpools     = true;
    bool _showTypeBSettings  = true;
    bool drawWhirlpool       = false;

    // Soul dropdown state (cached per-load)
    SoulData[]   _allSoulData;
    string[]     _soulDropdownLabels;
    GUIStyle     _greyLabelStyle;

    // ── Debug Console ──
    List<string> _debugLog        = new List<string>();
    Vector2      _debugLogScroll;
    Vector2      _rightPanelScroll;
    const int    DebugLogMaxLines  = 200;

    // Set piece picker state
    const string SetPiecesFolder = "Assets/Prefab/SetPieces";
    GameObject[] _scannedSetPieces;
    string[]     _setPieceNames;

    GridData loadedData;

    List<GridData>  discoveredGrids     = new List<GridData>();
    string[]        discoveredGridNames = new string[0];
    int             selectedDiscoveredGridIndex;

    [MenuItem("Tools/Waves/Grid Designer #4")]
    static void Open() => GetWindow<GridDesignerWindow>("Grid Designer");

    void OnEnable()
    {
        RefreshDiscoveredGrids();
        prefabFolderPath    = EditorPrefs.GetString("GridDesigner_PrefabFolder", "Assets/Prefab/MazePieces");
        iconsFolderPath     = EditorPrefs.GetString("GridDesigner_IconsFolder",  "");
        _gridLineOpacity    = EditorPrefs.GetFloat(PrefKeyGridOpacity,    1f);
        _backdropBrightness = EditorPrefs.GetFloat(PrefKeyBackdropBright, 0.08f);
        ScanPrefabFolder();
        ScanSetPiecesLib();
        ScanStatuesLib();
        ScanModifiersLib();
        LoadPanelWidth();
    }

    void GridLog(string msg)
    {
        _debugLog.Add(msg);
        if (_debugLog.Count > DebugLogMaxLines) _debugLog.RemoveAt(0);
    }

    void PushUndoSnapshot()
    {
        List<int> orbs      = loadedData?.orbCellIndices                 ?? new List<int>();
        List<int> waterMods = loadedData?.waterLevelModifierCellIndices  ?? new List<int>();
        List<int> waveMods  = loadedData?.waveModifierCellIndices        ?? new List<int>();
        List<GridData.SoulSpawnPoint> souls     = loadedData?.soulSpawnPoints ?? new List<GridData.SoulSpawnPoint>();
        List<GridData.ArenaEntrance>  entrances = loadedData?.entrances      ?? new List<GridData.ArenaEntrance>();
        undoStack.Push(new GridSnapshot(squareGrid, circleGrid, entrances, orbs, souls, waterMods, waveMods, loadedData?.tiers, loadedData?.prefabPlacements, loadedData?.linkedPairs));
        if (undoStack.Count > MaxUndoSteps) undoStack.TrimExcess();
    }

    void UndoLastAction()
    {
        if (undoStack.Count == 0) return;
        GridSnapshot snapshot = undoStack.Pop();
        squareGrid = (int[])snapshot.squareGrid.Clone();
        circleGrid = (int[])snapshot.circleGrid.Clone();
        if (loadedData != null)
        {
            loadedData.entrances      = snapshot.entrances ?? new List<GridData.ArenaEntrance>();
            loadedData.orbCellIndices = new List<int>(snapshot.orbIndices);
            loadedData.waterLevelModifierCellIndices = new List<int>(snapshot.waterLevelModifierIndices);
            loadedData.waveModifierCellIndices       = new List<int>(snapshot.waveModifierIndices);
            
            loadedData.linkedPairs = snapshot.linkedPairs != null 
                ? new List<GridData.LinkedPrefabPair>(snapshot.linkedPairs) 
                : new List<GridData.LinkedPrefabPair>();

            if (loadedData.tiers != null && snapshot.tierCells != null)
                for (int i = 0; i < Mathf.Min(loadedData.tiers.Count, snapshot.tierCells.Count); i++)
                {
                    loadedData.tiers[i].cells = (int[])snapshot.tierCells[i].Clone();
                    if (snapshot.tierPrefabPlacements != null && i < snapshot.tierPrefabPlacements.Count)
                        loadedData.tiers[i].prefabPlacements = GridSnapshot.CopyPlacements(snapshot.tierPrefabPlacements[i]);
                }

            loadedData.prefabPlacements = GridSnapshot.CopyPlacements(snapshot.prefabPlacements);
            loadedData.soulSpawnPoints = new List<GridData.SoulSpawnPoint>();
            foreach (var s in snapshot.soulSpawnPoints)
                loadedData.soulSpawnPoints.Add(new GridData.SoulSpawnPoint { cellIndex = s.cellIndex, soulData = s.soulData });
        }
        Repaint();
    }

    void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel();
        DrawPanelResizeHandle();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();
    }

    void DrawToolbar()
    {
        // ── Row 1: Level identity bar ────────────────────────────────────────
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        string levelLabel = loadedData != null
            ? $"  ▣  {(string.IsNullOrEmpty(loadedData.displayName) ? loadedData.name : loadedData.displayName)}  [{loadedData.levelID}]"
            : "  ▣  No level loaded";

        GUIStyle levelStyle = new GUIStyle(EditorStyles.boldLabel)
            { normal = { textColor = loadedData != null ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.5f) } };
        GUILayout.Label(levelLabel, levelStyle);

        GUILayout.FlexibleSpace();

        // Load button
        if (GUILayout.Button("Load…", EditorStyles.toolbarButton, GUILayout.Width(52)))
        {
            string path = EditorUtility.OpenFilePanel("Select GridData", "Assets", "asset");
            if (!string.IsNullOrEmpty(path))
            {
                path = FileUtil.GetProjectRelativePath(path);
                var data = AssetDatabase.LoadAssetAtPath<GridData>(path);
                if (data != null) LoadGrid(data);
            }
        }

        if (loadedData != null && GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(42)))
            SaveGridInPlace();

        EditorGUILayout.EndHorizontal();
    }

    void DrawToolButtons()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        SetToolbarButton("⊕ Select",  drawSelect,    new Color(0.4f,0.8f,1f),  () => { activeSlot = -1; _drawSplineWall = false; drawSelect = true; drawSoulArea = drawSoul = drawCircle = drawOrb = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false; });
        SetToolbarButton("★ Soul",    drawSoulArea,  Color.yellow,             () => { activeSlot = -1; _drawSplineWall = false; drawSoulArea = true; drawSelect = drawSoul = drawCircle = drawOrb = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false; ClearSelectState(); LogSelection(_currentSelection); });
        SetToolbarButton("◎ Orb",     drawOrb,       Color.white,              () => { activeSlot = -1; _drawSplineWall = false; drawOrb = true; drawCircle = drawSoul = drawSoulArea = drawWaterLevelModifier = drawWaveModifier = drawWhirlpool = false; });
        SetToolbarButton("〇 Whirl",  drawWhirlpool, new Color(0.7f,0.4f,1f), () => { activeSlot = -1; _drawSplineWall = false; drawWhirlpool = true; drawCircle = drawOrb = drawSoul = drawSoulArea = drawWaterLevelModifier = drawWaveModifier = false; });
        SetToolbarButton("✕ Eraser", activeSlot == 0, new Color(1f,0.5f,0.5f), () => { activeSlot = 0; _drawSplineWall = false; drawCircle = drawOrb = drawSoul = drawSoulArea = drawWaterLevelModifier = drawWaveModifier = drawWhirlpool = drawDirectPrefab = drawSelect = false; ClearSelectState(); LogSelection(_currentSelection); _isWaitingForTubePlacement = false; });
        SetToolbarButton("≋ Walls",  _drawSplineWall, new Color(1f,0.7f,0.2f), () => { activeSlot = -1; _drawSplineWall = true; drawSelect = drawSoulArea = drawSoul = drawCircle = drawOrb = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = drawDirectPrefab = false; ClearSelectState(); _isWaitingForTubePlacement = false; });

        EditorGUILayout.EndHorizontal();

        // Status hints
        GUIStyle hint = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        if (_drawSplineWall)
        {
            hint.normal.textColor = new Color(1f, 0.7f, 0.2f);
            GUILayout.Label("Left-click: place node  |  Drag node: move  |  Right-click node: delete  |  Esc: deselect path", hint);
        }
        else if (_isDrawingSoulArea || drawSelect || _isWaitingForTubePlacement)
        {
            if (_isWaitingForTubePlacement)
            {
                hint.normal.textColor = Color.cyan;
                GUILayout.Label("Click to place the SoulFishInputTube (link to modifier)", hint);
            }
            else if (_isDrawingSoulArea)
            {
                hint.normal.textColor = Color.yellow;
                GUILayout.Label("Click cells — click first to close loop — Enter to finish — Esc to cancel", hint);
            }
            else if (_selectedZoneIndex >= 0)
            {
                hint.normal.textColor = new Color(0.4f, 0.8f, 1f);
                GUILayout.Label($"Zone {_selectedZoneIndex}  Node {_selectedNodeIndex + 1} — drag to move — Shift+click to bridge — Esc to deselect", hint);
            }
            else
            {
                hint.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                GUILayout.Label("Click to select — drag to move — click empty to deselect — Del to delete", hint);
            }
        }
    }

    void SetToolbarButton(string label, bool active, Color activeColor, System.Action onClick)
    {
        GUI.backgroundColor = active ? activeColor : Color.white;
        if (GUILayout.Toggle(active, label, EditorStyles.toolbarButton) != active)
            onClick();
        GUI.backgroundColor = Color.white;
    }

    Vector2 _leftPanelScroll;

    float   _leftPanelWidth    = 280f;
    bool    _isResizingPanel   = false;
    float   _rightPanelWidth   = 240f;
    bool    _isResizingRightPanel = false;
    const float PanelMinWidth  = 180f;
    const float PanelMaxWidth  = 600f;
    const float HandleWidth    = 5f;

    void LoadPanelWidth()
    {
        _leftPanelWidth  = EditorPrefs.GetFloat("GridDesigner_LeftPanelWidth",  280f);
        _rightPanelWidth = EditorPrefs.GetFloat("GridDesigner_RightPanelWidth", 240f);
    }

    void SavePanelWidth()
    {
        EditorPrefs.SetFloat("GridDesigner_LeftPanelWidth", _leftPanelWidth);
    }

    void SaveRightPanelWidth()
    {
        EditorPrefs.SetFloat("GridDesigner_RightPanelWidth", _rightPanelWidth);
    }

    void DrawPanelResizeHandle()
    {
        // Reserve a thin vertical strip between the two panels
        Rect handleRect = GUILayoutUtility.GetRect(HandleWidth, HandleWidth,
            GUILayout.Width(HandleWidth), GUILayout.ExpandHeight(true));

        // Tint the handle on hover/drag so it's discoverable
        bool hovering = handleRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(handleRect,
            _isResizingPanel ? new Color(0.4f, 0.7f, 1f, 0.8f) :
            hovering         ? new Color(0.6f, 0.6f, 0.6f, 0.5f) :
                               new Color(0.3f, 0.3f, 0.3f, 0.4f));

        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

        switch (Event.current.type)
        {
            case EventType.MouseDown:
                if (handleRect.Contains(Event.current.mousePosition))
                {
                    _isResizingPanel = true;
                    Event.current.Use();
                }
                break;

            case EventType.MouseDrag:
                if (_isResizingPanel)
                {
                    _leftPanelWidth += Event.current.delta.x;
                    _leftPanelWidth  = Mathf.Clamp(_leftPanelWidth, PanelMinWidth, PanelMaxWidth);
                    SavePanelWidth();
                    Repaint();
                    Event.current.Use();
                }
                break;

            case EventType.MouseUp:
                if (_isResizingPanel)
                {
                    _isResizingPanel = false;
                    Event.current.Use();
                }
                break;
        }
    }

    void DrawRightPanelResizeHandle()
    {
        Rect handleRect = GUILayoutUtility.GetRect(HandleWidth, HandleWidth,
            GUILayout.Width(HandleWidth), GUILayout.ExpandHeight(true));

        bool hovering = handleRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(handleRect,
            _isResizingRightPanel ? new Color(0.4f, 0.7f, 1f, 0.8f) :
            hovering              ? new Color(0.6f, 0.6f, 0.6f, 0.5f) :
                                    new Color(0.3f, 0.3f, 0.3f, 0.4f));

        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

        switch (Event.current.type)
        {
            case EventType.MouseDown:
                if (handleRect.Contains(Event.current.mousePosition))
                {
                    _isResizingRightPanel = true;
                    Event.current.Use();
                }
                break;

            case EventType.MouseDrag:
                if (_isResizingRightPanel)
                {
                    _rightPanelWidth -= Event.current.delta.x;
                    _rightPanelWidth  = Mathf.Clamp(_rightPanelWidth, PanelMinWidth, PanelMaxWidth);
                    SaveRightPanelWidth();
                    Repaint();
                    Event.current.Use();
                }
                break;

            case EventType.MouseUp:
                if (_isResizingRightPanel)
                {
                    _isResizingRightPanel = false;
                    Event.current.Use();
                }
                break;
        }
    }


    void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth));
        _leftPanelScroll = EditorGUILayout.BeginScrollView(_leftPanelScroll,
            GUILayout.Width(_leftPanelWidth), GUILayout.ExpandHeight(true));
        EnsureSlotCapacity(GetMaxSlotUsed());

        // ── Level file operations (top of panel) ────────────────────────────
        EditorGUILayout.LabelField("Existing Levels", EditorStyles.boldLabel);
        if (discoveredGrids.Count > 0)
        {
            selectedDiscoveredGridIndex = EditorGUILayout.Popup(
                selectedDiscoveredGridIndex, discoveredGridNames);
            if (GUILayout.Button("LOAD SELECTED"))
                LoadGrid(discoveredGrids[selectedDiscoveredGridIndex]);
        }
        else
        {
            EditorGUILayout.LabelField("No GridData assets found.");
        }
        if (GUILayout.Button("Refresh Level List")) RefreshDiscoveredGrids();
        EditorGUILayout.Space();
        DrawButtons();
        EditorGUILayout.Space();

        if (loadedData != null) DrawLevelIdentitySection();

        EditorGUILayout.LabelField("Tiers", EditorStyles.boldLabel);

        if (loadedData != null)
        {
            if (loadedData.tiers == null) loadedData.tiers = new List<GridData.GridTier>();

            RefreshCachedTierOffsets();

            EditorGUILayout.BeginHorizontal();
            baseLayerVisible = EditorGUILayout.Toggle(baseLayerVisible, GUILayout.Width(16));
            GUI.backgroundColor = activeTierIndex == -1 ? Color.cyan : Color.white;
            if (GUILayout.Button("Base Layer")) activeTierIndex = -1;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            while (tierVisible.Count < loadedData.tiers.Count) tierVisible.Add(true);
            while (tierVisible.Count > loadedData.tiers.Count) tierVisible.RemoveAt(tierVisible.Count - 1);

            int tierToRemove = -1;
            for (int i = 0; i < loadedData.tiers.Count; i++)
            {
                var tier = loadedData.tiers[i];
                EditorGUILayout.BeginHorizontal();

                tierVisible[i] = EditorGUILayout.Toggle(tierVisible[i], GUILayout.Width(16));

                GUI.backgroundColor = activeTierIndex == i ? Color.cyan : Color.white;
                if (GUILayout.Button($"T{i + 1}", GUILayout.Width(28))) activeTierIndex = i;
                GUI.backgroundColor = Color.white;

                tier.name = EditorGUILayout.TextField(tier.name, GUILayout.Width(60));

                if (cachedTierYOffsets != null && cachedTierYOffsets.Length > 0)
                {
                    string[] slotLabels = new string[cachedTierYOffsets.Length];
                    for (int s = 0; s < cachedTierYOffsets.Length; s++)
                        slotLabels[s] = $"{WaterLevelModifier.FloorLabel(s, cachedTierYOffsets)} ({cachedTierYOffsets[s]:+0.##;-0.##;0})";
                    int newSlot = EditorGUILayout.Popup(
                        Mathf.Clamp(tier.yOffsetSlot, 0, cachedTierYOffsets.Length - 1),
                        slotLabels, GUILayout.Width(90));
                    if (newSlot != tier.yOffsetSlot) { tier.yOffsetSlot = newSlot; EditorUtility.SetDirty(loadedData); }
                }
                else
                {
                    GUI.enabled = false;
                    EditorGUILayout.FloatField(tier.yOffset, GUILayout.Width(52));
                    GUI.enabled = true;
                }

                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", GUILayout.Width(24))) tierToRemove = i;
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            if (tierToRemove >= 0)
            {
                PushUndoSnapshot();
                loadedData.tiers.RemoveAt(tierToRemove);
                tierVisible.RemoveAt(tierToRemove);
                if (activeTierIndex == tierToRemove) activeTierIndex = -1;
                else if (activeTierIndex > tierToRemove) activeTierIndex--;
                EditorUtility.SetDirty(loadedData);
            }

            if (GUILayout.Button("+ Add Tier"))
            {
                PushUndoSnapshot();
                loadedData.tiers.Add(new GridData.GridTier
                {
                    name    = $"Tier {loadedData.tiers.Count + 1}",
                    yOffset = (loadedData.tiers.Count + 1) * 5f,
                    cells   = new int[GridData.CellCount]
                });
                tierVisible.Add(true);
                EditorUtility.SetDirty(loadedData);
            }

            if (activeTierIndex >= 0 && activeTierIndex < loadedData.tiers.Count)
                EditorGUILayout.HelpBox($"Drawing into: {loadedData.tiers[activeTierIndex].name}", MessageType.None);
            else
                EditorGUILayout.HelpBox("Drawing into: Base Layer", MessageType.None);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Arena", EditorStyles.boldLabel);

        if (loadedData != null)
        {
            EditorGUI.BeginChangeCheck();
            ArenaProfile newProfile = (ArenaProfile)EditorGUILayout.ObjectField(
                "Arena Profile", loadedData.arenaProfile, typeof(ArenaProfile), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Change Arena Profile");
                loadedData.arenaProfile = newProfile;
                EditorUtility.SetDirty(loadedData);
            }

            EditorGUILayout.Space();
            DrawPortalList();
        }
        else
        {
            EditorGUILayout.HelpBox("Load a grid to set arena size.", MessageType.None);
        }

        EditorGUILayout.Space();

        int slotToRemove = -1;

        for (int i = 1; i <= slotColors.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            slotColors[i - 1] = EditorGUILayout.ColorField(slotColors[i - 1], GUILayout.Width(40));
            slotNotes[i]      = EditorGUILayout.TextField(slotNotes[i]);

            if (GUILayout.Button("■", GUILayout.Width(30)))
            { activeSlot = i; drawCircle = drawOrb = drawSoul = false; }

            if (GUILayout.Button("●", GUILayout.Width(30)))
            { activeSlot = i; drawCircle = true; drawOrb = drawSoul = false; }

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(24)))
                slotToRemove = i;
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        // Defer removal to avoid modifying list mid-loop
        if (slotToRemove > 0)
        {
            bool inUse = IsSlotInUse(slotToRemove);
            if (!inUse || EditorUtility.DisplayDialog(
                "Clear Slot",
                $"Slot {slotToRemove} is painted on cells. Clear all and remove?",
                "Clear All", "Cancel"))
            {
                // Zero out all cells using this slot before RemoveSlot remaps
                for (int ci = 0; ci < CellCount; ci++)
                {
                    if (squareGrid[ci] == slotToRemove) squareGrid[ci] = 0;
                    if (circleGrid[ci] == slotToRemove) circleGrid[ci] = 0;
                }
                if (loadedData?.tiers != null)
                    foreach (var tier in loadedData.tiers)
                        if (tier.cells != null)
                            for (int ci = 0; ci < tier.cells.Length; ci++)
                                if (tier.cells[ci] == slotToRemove) tier.cells[ci] = 0;

                RemoveSlot(slotToRemove);
            }
        }


        EditorGUILayout.Space();

        // ── Level Data Sections ──────────────────────────────────────────
        if (loadedData != null)
        {
            DrawCameraSection();
            DrawWavePresetsSection();
            DrawEnemySection();
            DrawTimeTrialSection();
            DrawPrefabsSection();
            DrawStartRitualSection();
            DrawWhirlpoolsSection();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void ApplyToolToCell(int index)
    {
        if (activeSlot == 0)
        {
            squareGrid[index] = 0;
            circleGrid[index] = 0;
            if (loadedData != null)
            {
                loadedData.orbCellIndices?.Remove(index);
                loadedData.waterLevelModifierCellIndices?.Remove(index);
                loadedData.waveModifierCellIndices?.Remove(index);
                loadedData.whirlpools?.RemoveAll(w => w.cellIndex == index);
                loadedData.soulSpawnPoints?.RemoveAll(s => s.cellIndex == index);
                loadedData.prefabPlacements?.RemoveAll(p => p.cellIndex == index);
                if (loadedData.tiers != null)
                    foreach (var tier in loadedData.tiers)
                    {
                        if (tier.cells != null && index < tier.cells.Length) tier.cells[index] = 0;
                        tier.waterLevelModifierCellIndices?.Remove(index);
                        tier.waveModifierCellIndices?.Remove(index);
                        tier.prefabPlacements?.RemoveAll(p => p.cellIndex == index);
                    }
            }
            return;
        }

        if (drawSoulArea && loadedData != null)
        {
            EnsureSoulZones();
            if (_activeSoulZoneIndex < 0 || _activeSoulZoneIndex >= loadedData.soulZones.Count)
            {
                // No zone selected — create a new one and start drawing
                PushUndoSnapshot();
                var newZone = new GridData.SoulZone();
                loadedData.soulZones.Add(newZone);
                _activeSoulZoneIndex = loadedData.soulZones.Count - 1;
                _drawingNodes.Clear();
                _isDrawingSoulArea = true;
            }

            if (!_isDrawingSoulArea) return;

            var zone = loadedData.soulZones[_activeSoulZoneIndex];

            // Close loop: 3+ nodes and user clicks the first node again
            if (_drawingNodes.Count >= 3 && index == _drawingNodes[0])
            {
                _drawingNodes.Add(index);
                CommitDrawingNodes(zone);
                return;
            }

            _drawingNodes.Add(index);
            EditorUtility.SetDirty(loadedData);
            Repaint();
            return;
        }

        if (drawOrb && loadedData != null)
        {
            if (loadedData.orbCellIndices == null) loadedData.orbCellIndices = new List<int>();
            if (!loadedData.orbCellIndices.Contains(index)) loadedData.orbCellIndices.Add(index);
            return;
        }

        if (drawWaterLevelModifier && loadedData != null)
        {
            var wlList = GetActiveTierWaterModifiers();
            if (wlList.Contains(index)) wlList.Remove(index); else wlList.Add(index);
            return;
        }

        if (drawWaveModifier && loadedData != null)
        {
            var wvList = GetActiveTierWaveModifiers();
            if (wvList.Contains(index)) wvList.Remove(index); else wvList.Add(index);
            return;
        }

        if (drawWhirlpool && loadedData != null)
        {
            if (loadedData.whirlpools == null) loadedData.whirlpools = new List<GridData.WhirlpoolPoint>();
            int existing = loadedData.whirlpools.FindIndex(w => w.cellIndex == index);
            if (existing >= 0) loadedData.whirlpools.RemoveAt(existing);
            else loadedData.whirlpools.Add(new GridData.WhirlpoolPoint { cellIndex = index });
            return;
        }

        if (drawDirectPrefab && _activePlacementPrefab != null && loadedData != null)
        {
            if (_isWaitingForTubePlacement)
            {
                // Second click: Place the tube and link it
                var tubePrefab = scannedModifiersLib.Find(p => p != null && p.name == "SoulFishInputTube");
                if (tubePrefab == null)
                {
                    GridLog("[ERR] Could not find 'SoulFishInputTube' in modifiers library.");
                    _isWaitingForTubePlacement = false;
                    return;
                }

                var placements = GetActivePrefabPlacements();
                placements.RemoveAll(p => p.cellIndex == index);
                placements.Add(new GridData.PrefabPlacement
                {
                    cellIndex = index,
                    prefab = tubePrefab,
                    isCircle = drawCircle,
                    isWorldSpaceProp = _activePlacementIsWorldSpaceProp,
                });

                if (loadedData.linkedPairs == null) loadedData.linkedPairs = new List<GridData.LinkedPrefabPair>();
                loadedData.linkedPairs.Add(new GridData.LinkedPrefabPair
                {
                    modifierCellIndex = _pendingModifierCellIndex,
                    modifierTierIndex = _pendingModifierTierIndex,
                    inputTubeCellIndex = index,
                    inputTubeTierIndex = activeTierIndex
                });

                GridLog($"Linked modifier at {_pendingModifierCellIndex} (T:{_pendingModifierTierIndex}) to tube at {index} (T:{activeTierIndex})");
                _isWaitingForTubePlacement = false;
                _pendingModifierCellIndex = -1;
                _pendingModifierTierIndex = -1;
                return;
            }

            // First click (or normal placement)
            var placementsBase = GetActivePrefabPlacements();
            placementsBase.RemoveAll(p => p.cellIndex == index);
            placementsBase.Add(new GridData.PrefabPlacement
            {
                cellIndex           = index,
                prefab              = _activePlacementPrefab,
                isCircle            = drawCircle,
                isWorldSpaceProp    = _activePlacementIsWorldSpaceProp,
            });

            if (_activePlacementPrefab.name == "TypeBWaveModifier")
            {
                _isWaitingForTubePlacement = true;
                _pendingModifierCellIndex = index;
                _pendingModifierTierIndex = activeTierIndex;
                GridLog("Modifier placed. Now place the Input Tube.");
            }
            return;
        }

        if (activeTierIndex >= 0 && loadedData?.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var tier = loadedData.tiers[activeTierIndex];
            if (tier.cells == null) tier.cells = new int[GridData.CellCount];
            tier.cells[index] = activeSlot;
        }
        else if (drawCircle) circleGrid[index] = activeSlot;
        else                 squareGrid[index] = activeSlot;
    }

    void ScanPrefabFolder()
    {
        scannedPrefabs.Clear();
        prefabIcons.Clear();

        if (string.IsNullOrEmpty(prefabFolderPath)) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolderPath });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) scannedPrefabs.Add(go);
        }
        scannedPrefabs.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        ScanIcons();
    }

    void ScanIcons()
    {
        prefabIcons.Clear();

        // Search folders: explicit icons folder first, then prefab folder itself
        var searchFolders = new List<string>();
        if (!string.IsNullOrEmpty(iconsFolderPath) && AssetDatabase.IsValidFolder(iconsFolderPath))
            searchFolders.Add(iconsFolderPath);
        if (!string.IsNullOrEmpty(prefabFolderPath) && AssetDatabase.IsValidFolder(prefabFolderPath))
            searchFolders.Add(prefabFolderPath);
        if (searchFolders.Count == 0) return;

        string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", searchFolders.ToArray());
        var texByName = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);
        foreach (string guid in texGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                texByName[tex.name] = tex;
        }

        // Match icons to prefabs by name (exact, then strip common suffixes)
        foreach (var prefab in scannedPrefabs)
        {
            if (texByName.TryGetValue(prefab.name, out var icon))
            {
                prefabIcons[prefab.name] = icon;
                continue;
            }
            // Try stripping trailing digits / underscores for variant prefabs
            string stripped = prefab.name.TrimEnd('0','1','2','3','4','5','6','7','8','9','_');
            if (texByName.TryGetValue(stripped, out icon))
                prefabIcons[prefab.name] = icon;
        }
    }

    // Returns the prefab's PrefabBaselineAlignment (cached), or null if it has none.
    PrefabBaselineAlignment GetBaselineAlign(GameObject prefab)
    {
        if (prefab == null) return null;
        if (!_baselineAlignCache.TryGetValue(prefab, out var align))
        {
            align = prefab.GetComponentInChildren<PrefabBaselineAlignment>(true);
            _baselineAlignCache[prefab] = align;
        }
        return align;
    }

    Color GetPrefabColor(GameObject prefab)
    {
        int idx = scannedPrefabs.IndexOf(prefab);
        if (idx < 0) return Color.gray;
        return Color.HSVToRGB((idx * 0.618f) % 1f, 0.75f, 0.95f);
    }

    void RefreshCachedTierOffsets()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:TierConfig");
        if (guids.Length > 0)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<TierConfig>(path);
            cachedTierYOffsets = config?.offsets;
        }
        else
        {
            cachedTierYOffsets = null;
        }
    }

    List<GridData.PrefabPlacement> GetActivePrefabPlacements()
    {
        if (activeTierIndex >= 0 && loadedData?.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var t = loadedData.tiers[activeTierIndex];
            if (t.prefabPlacements == null) t.prefabPlacements = new List<GridData.PrefabPlacement>();
            return t.prefabPlacements;
        }
        if (loadedData.prefabPlacements == null) loadedData.prefabPlacements = new List<GridData.PrefabPlacement>();
        return loadedData.prefabPlacements;
    }

    List<int> GetActiveTierWaterModifiers()
    {
        if (activeTierIndex >= 0 && loadedData?.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var t = loadedData.tiers[activeTierIndex];
            if (t.waterLevelModifierCellIndices == null) t.waterLevelModifierCellIndices = new List<int>();
            return t.waterLevelModifierCellIndices;
        }
        if (loadedData.waterLevelModifierCellIndices == null) loadedData.waterLevelModifierCellIndices = new List<int>();
        return loadedData.waterLevelModifierCellIndices;
    }

    List<int> GetActiveTierWaveModifiers()
    {
        if (activeTierIndex >= 0 && loadedData?.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var t = loadedData.tiers[activeTierIndex];
            if (t.waveModifierCellIndices == null) t.waveModifierCellIndices = new List<int>();
            return t.waveModifierCellIndices;
        }
        if (loadedData.waveModifierCellIndices == null) loadedData.waveModifierCellIndices = new List<int>();
        return loadedData.waveModifierCellIndices;
    }

    // ─────────────────────────────────────────────
    // LEVEL DATA SECTIONS
    // ─────────────────────────────────────────────

    void DrawLevelIdentitySection()
    {
        _showLevelIdentity = EditorGUILayout.Foldout(_showLevelIdentity, "Level Identity", true, EditorStyles.foldoutHeader);
        if (!_showLevelIdentity) return;

        GUI.enabled = false;
        EditorGUILayout.ObjectField("Asset", loadedData, typeof(GridData), false);
        GUI.enabled = true;

        EditorGUI.BeginChangeCheck();
        string newID   = EditorGUILayout.TextField("Level ID",     loadedData.levelID);
        string newName = EditorGUILayout.TextField("Display Name", loadedData.displayName);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Edit Level Identity");
            loadedData.levelID     = newID;
            loadedData.displayName = newName;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void DrawCameraSection()
    {
        _showCamera = EditorGUILayout.Foldout(_showCamera, "Camera", true, EditorStyles.foldoutHeader);
        if (!_showCamera) return;

        EditorGUI.BeginChangeCheck();
        var newProfile = (CameraProfile)EditorGUILayout.ObjectField(
            "Camera Profile", loadedData.cameraProfile, typeof(CameraProfile), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Camera Profile");
            loadedData.cameraProfile = newProfile;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void DrawWavePresetsSection()
    {
        _showWavePresets = EditorGUILayout.Foldout(_showWavePresets, "Wave Presets", true, EditorStyles.foldoutHeader);
        if (!_showWavePresets) return;

        EditorGUI.BeginChangeCheck();
        var newGameplay = (WavePreset)EditorGUILayout.ObjectField(
            "Gameplay Preset", loadedData.gameplayWavePreset, typeof(WavePreset), false);
        DrawWavePresetInfo(loadedData.gameplayWavePreset);
        var newGong     = (WavePreset)EditorGUILayout.ObjectField(
            "Gong Preset",     loadedData.gongWavePreset,     typeof(WavePreset), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Wave Presets");
            loadedData.gameplayWavePreset = newGameplay;
            loadedData.gongWavePreset     = newGong;
            EditorUtility.SetDirty(loadedData);
        }
    }

    // Small read-only summary of a wave preset's primary values, shown under its field.
    void DrawWavePresetInfo(WavePreset preset)
    {
        if (preset == null) return;
        var s = preset.state;
        EditorGUILayout.LabelField(
            $"Freq {s.Frequency:0.##}   Speed {s.Speed:0.##}   Ripple {s.RippleDepth:0.##}",
            EditorStyles.miniLabel);
    }

    void DrawEnemySection()
    {
        _showEnemy = EditorGUILayout.Foldout(_showEnemy, "Enemy", true, EditorStyles.foldoutHeader);
        if (!_showEnemy) return;

        EditorGUI.BeginChangeCheck();
        var newProfile = (EnemyProfile)EditorGUILayout.ObjectField(
            "Enemy Profile", loadedData.enemyProfile, typeof(EnemyProfile), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Enemy Profile");
            loadedData.enemyProfile = newProfile;
            EditorUtility.SetDirty(loadedData);
        }
        if (loadedData.enemyProfile != null)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"Prefab: {loadedData.enemyProfile.prefab?.name ?? "(none)"}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Spawn on visit: {loadedData.enemyProfile.spawnOnVisit}",
                EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }
    }

    void DrawTimeTrialSection()
    {
        _showTimeTrial = EditorGUILayout.Foldout(_showTimeTrial, "Time Trial", true, EditorStyles.foldoutHeader);
        if (!_showTimeTrial) return;

        EditorGUI.BeginChangeCheck();
        bool  newIsTimeTrial      = EditorGUILayout.Toggle("Is Time Trial",   loadedData.isTimeTrial);
        float newTimeLimitSeconds = loadedData.isTimeTrial
            ? EditorGUILayout.FloatField("Time Limit (s)", loadedData.timeLimitSeconds)
            : loadedData.timeLimitSeconds;
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Edit Time Trial");
            loadedData.isTimeTrial      = newIsTimeTrial;
            loadedData.timeLimitSeconds = newTimeLimitSeconds;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void DrawPrefabsSection()
    {
        _showPrefabs = EditorGUILayout.Foldout(_showPrefabs, "Prefabs", true, EditorStyles.foldoutHeader);
        if (!_showPrefabs) return;

        EditorGUILayout.Space(2);

        // Sculpture Set Piece — ObjectField + dropdown picker from Assets/Prefab/SetPieces
        EditorGUILayout.LabelField("Sculpture Set Piece", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        var newSetPiece = (GameObject)EditorGUILayout.ObjectField(
            loadedData.sculptureSetPiecePrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Sculpture Set Piece");
            loadedData.sculptureSetPiecePrefab = newSetPiece;
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("↺", GUILayout.Width(24)))
            ScanSetPieces();

        EditorGUILayout.EndHorizontal();

        EnsureSetPieceCache();
        if (_setPieceNames != null && _setPieceNames.Length > 0)
        {
            int currentIdx = 0;
            if (loadedData.sculptureSetPiecePrefab != null)
            {
                for (int i = 1; i < _scannedSetPieces.Length + 1; i++)
                    if (_scannedSetPieces[i - 1] == loadedData.sculptureSetPiecePrefab)
                    { currentIdx = i; break; }
            }

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(currentIdx, _setPieceNames);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Set Sculpture Set Piece");
                loadedData.sculptureSetPiecePrefab = newIdx == 0 ? null : _scannedSetPieces[newIdx - 1];
                EditorUtility.SetDirty(loadedData);
            }
        }
        else
        {
            EditorGUILayout.LabelField($"  (no prefabs in {SetPiecesFolder})", EditorStyles.miniLabel);
        }
    }

    void EnsureSetPieceCache()
    {
        if (_scannedSetPieces != null) return;
        ScanSetPieces();
    }

    void ScanSetPieces()
    {
        var list  = new List<GameObject>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SetPiecesFolder });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) list.Add(go);
        }
        list.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        _scannedSetPieces = list.ToArray();

        _setPieceNames = new string[_scannedSetPieces.Length + 1];
        _setPieceNames[0] = "(none)";
        for (int i = 0; i < _scannedSetPieces.Length; i++)
            _setPieceNames[i + 1] = _scannedSetPieces[i].name;
    }

    void ScanSetPiecesLib()
    {
        scannedSetPiecesLib.Clear();
        selectedSetPieceIndex = -1;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SetPiecesFolder });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) scannedSetPiecesLib.Add(go);
        }
        scannedSetPiecesLib.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    void ScanStatuesLib()
    {
        scannedStatuesLib.Clear();
        selectedStatueIndex = -1;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { StatuesPrefabsFolder });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) scannedStatuesLib.Add(go);
        }
        scannedStatuesLib.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    void ScanModifiersLib()
    {
        scannedModifiersLib.Clear();
        selectedModifierIndex = -1;
        if (!AssetDatabase.IsValidFolder(ModifiersPrefabsFolder)) return;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ModifiersPrefabsFolder });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) scannedModifiersLib.Add(go);
        }
        scannedModifiersLib.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    void DrawStartRitualSection()
    {
        _showStartRitual = EditorGUILayout.Foldout(_showStartRitual, "Start Ritual", true, EditorStyles.foldoutHeader);
        if (!_showStartRitual) return;

        EditorGUI.BeginChangeCheck();
        var newRitual = (LevelStartRitual)EditorGUILayout.ObjectField(
            "Start Ritual", loadedData.startRitual, typeof(LevelStartRitual), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Start Ritual");
            loadedData.startRitual = newRitual;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void ClearSelectState()
    {
        _selectedZoneIndex = -1;
        _selectedNodeIndex = -1;
        _isDraggingNode    = false;
        _dragCurrentCell   = -1;
        _dragUndoPushed    = false;
        _currentSelection  = new SelectionInfo { type = SelectionType.None };
        CancelBridge();
    }

    void CancelBridge()
    {
        _isBridgeMode       = false;
        _bridgeEndZoneIndex = -1;
        _bridgeEndNodeIndex = -1;
        _bridgeNodes.Clear();
    }

    void ConnectNodes(int zoneIdx, int nodeA, int nodeB)
    {
        if (zoneIdx < 0 || zoneIdx >= loadedData.soulZones.Count) return;
        var zone = loadedData.soulZones[zoneIdx];
        if (zone.nodes == null || zone.nodes.Count < 2) return;

        int first = 0;
        int last  = zone.nodes.Count - 1;

        bool isFirstAndLast = (nodeA == first && nodeB == last)
                           || (nodeA == last  && nodeB == first);

        Undo.RecordObject(loadedData, "Connect Soul Zone Nodes");

        if (isFirstAndLast)
        {
            // Close the loop — append first cell at end (standard closed loop pattern)
            // Remove existing closing node if already there to avoid double
            if (zone.nodes[last] == zone.nodes[first])
                zone.nodes.RemoveAt(last);
            zone.nodes.Add(zone.nodes[0]);
        }
        else
        {
            // General case: remove nodes strictly between the two indices
            int startIdx    = Mathf.Min(nodeA, nodeB);
            int endIdx      = Mathf.Max(nodeA, nodeB);
            int removeCount = endIdx - startIdx - 1;
            if (removeCount > 0)
            {
                zone.nodes.RemoveRange(startIdx + 1, removeCount);
                _selectedNodeIndex = startIdx;
            }
        }

        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    bool FindNodeAtCell(int cellIndex, out int zoneIdx, out int nodeIdx)
    {
        if (loadedData?.soulZones == null) { zoneIdx = -1; nodeIdx = -1; return false; }
        for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
        {
            var zone = loadedData.soulZones[zi];
            if (zone.nodes == null) continue;
            for (int ni = 0; ni < zone.nodes.Count; ni++)
            {
                if (zone.nodes[ni] == cellIndex) { zoneIdx = zi; nodeIdx = ni; return true; }
            }
        }
        zoneIdx = -1; nodeIdx = -1; return false;
    }

    SelectionInfo FindAnythingAtCell(int cellIndex)
    {
        SelectionInfo info = new SelectionInfo { type = SelectionType.None, cellIndex = cellIndex, tierIndex = activeTierIndex };

        // 1. Soul Zone Nodes (High priority as they are small dots)
        if (FindNodeAtCell(cellIndex, out int zi, out int ni))
        {
            info.type = SelectionType.SoulZoneNode;
            info.index = zi;
            info.subIndex = ni;
            return info;
        }

        // 2. Prefab Placements
        if (activeTierIndex >= 0 && loadedData.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var tier = loadedData.tiers[activeTierIndex];
            int pIdx = tier.prefabPlacements?.FindIndex(p => p.cellIndex == cellIndex) ?? -1;
            if (pIdx >= 0)
            {
                info.type = SelectionType.PrefabPlacement;
                info.index = pIdx;
                return info;
            }
        }
        else
        {
            int pIdx = loadedData.prefabPlacements?.FindIndex(p => p.cellIndex == cellIndex) ?? -1;
            if (pIdx >= 0)
            {
                info.type = SelectionType.PrefabPlacement;
                info.tierIndex = -1;
                info.index = pIdx;
                return info;
            }
        }

        // 3. Whirlpools
        int wIdx = loadedData.whirlpools?.FindIndex(w => w.cellIndex == cellIndex) ?? -1;
        if (wIdx >= 0)
        {
            info.type = SelectionType.Whirlpool;
            info.index = wIdx;
            return info;
        }

        // 4. Modifiers
        var waterMods = GetActiveTierWaterModifiers();
        if (waterMods.Contains(cellIndex))
        {
            info.type = SelectionType.WaterModifier;
            return info;
        }

        var waveMods = GetActiveTierWaveModifiers();
        if (waveMods.Contains(cellIndex))
        {
            info.type = SelectionType.WaveModifier;
            return info;
        }

        // 5. Orbs (Base layer only)
        if (activeTierIndex == -1 && loadedData.orbCellIndices != null && loadedData.orbCellIndices.Contains(cellIndex))
        {
            info.type = SelectionType.Orb;
            return info;
        }

        // 6. Grid Slots
        if (activeTierIndex >= 0 && loadedData.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var tier = loadedData.tiers[activeTierIndex];
            if (tier.cells != null && tier.cells[cellIndex] > 0)
            {
                info.type = SelectionType.GridSlot;
                info.value = tier.cells[cellIndex];
                return info;
            }
        }
        else
        {
            if (circleGrid[cellIndex] > 0)
            {
                info.type = SelectionType.GridSlot;
                info.isCircle = true;
                info.value = circleGrid[cellIndex];
                return info;
            }
            if (squareGrid[cellIndex] > 0)
            {
                info.type = SelectionType.GridSlot;
                info.isCircle = false;
                info.value = squareGrid[cellIndex];
                return info;
            }
        }

        return new SelectionInfo { type = SelectionType.None };
    }

    void LogSelection(SelectionInfo info)
    {
        if (info.type == SelectionType.None)
        {
            GridLog("Deselected.");
            return;
        }

        string tierStr = info.tierIndex == -1 ? "Base" : $"T{info.tierIndex + 1}";
        string loc = $"at cell {info.cellIndex} ({tierStr})";

        switch (info.type)
        {
            case SelectionType.SoulZoneNode:
                GridLog($"Selected: Soul Zone {info.index} Node {info.subIndex + 1} {loc}");
                break;
            case SelectionType.PrefabPlacement:
                var placements = info.tierIndex == -1 ? loadedData.prefabPlacements : loadedData.tiers[info.tierIndex].prefabPlacements;
                string pName = (info.index >= 0 && info.index < placements.Count && placements[info.index].prefab != null) 
                    ? placements[info.index].prefab.name : "Prefab";
                GridLog($"Selected: {pName} {loc}");
                break;
            case SelectionType.Whirlpool:
                GridLog($"Selected: Whirlpool {info.index + 1} {loc}");
                break;
            case SelectionType.Orb:
                GridLog($"Selected: Orb {loc}");
                break;
            case SelectionType.WaterModifier:
                GridLog($"Selected: Water Modifier {loc}");
                break;
            case SelectionType.WaveModifier:
                GridLog($"Selected: Wave Modifier {loc}");
                break;
            case SelectionType.GridSlot:
                string note = (info.value >= 0 && info.value < slotNotes.Count) ? slotNotes[info.value] : "";
                string shape = info.isCircle ? "Circle" : "Square";
                GridLog($"Selected: Slot {info.value} ({shape}) {loc} {(string.IsNullOrEmpty(note) ? "" : " - " + note)}");
                break;
        }
    }

    void MoveSelection(SelectionInfo info, int newCellIndex, bool pushUndo = true)
    {
        if (info.type == SelectionType.None || info.cellIndex == newCellIndex) return;

        Undo.RecordObject(loadedData, "Move Selection");
        if (pushUndo) PushUndoSnapshot();

        // Suppress the per-cell log while dragging (pushUndo == false) to avoid console spam.
        if (pushUndo)
        {
            string tierStr = info.tierIndex == -1 ? "Base" : $"T{info.tierIndex + 1}";
            GridLog($"Moved selection from {info.cellIndex} to {newCellIndex} ({tierStr})");
        }

        switch (info.type)
        {
            case SelectionType.SoulZoneNode:
                var zone = loadedData.soulZones[info.index];
                zone.nodes[info.subIndex] = newCellIndex;
                _selectedNodeIndex = info.subIndex;
                _selectedZoneIndex = info.index;
                break;

            case SelectionType.PrefabPlacement:
                List<GridData.PrefabPlacement> placements = info.tierIndex == -1 
                    ? loadedData.prefabPlacements 
                    : loadedData.tiers[info.tierIndex].prefabPlacements;
                placements[info.index].cellIndex = newCellIndex;
                break;

            case SelectionType.Whirlpool:
                loadedData.whirlpools[info.index].cellIndex = newCellIndex;
                break;

            case SelectionType.Orb:
                loadedData.orbCellIndices.Remove(info.cellIndex);
                if (!loadedData.orbCellIndices.Contains(newCellIndex))
                    loadedData.orbCellIndices.Add(newCellIndex);
                break;

            case SelectionType.WaterModifier:
                var waterMods = info.tierIndex == -1 
                    ? loadedData.waterLevelModifierCellIndices 
                    : loadedData.tiers[info.tierIndex].waterLevelModifierCellIndices;
                waterMods.Remove(info.cellIndex);
                if (!waterMods.Contains(newCellIndex)) waterMods.Add(newCellIndex);
                break;

            case SelectionType.WaveModifier:
                var waveMods = info.tierIndex == -1 
                    ? loadedData.waveModifierCellIndices 
                    : loadedData.tiers[info.tierIndex].waveModifierCellIndices;
                waveMods.Remove(info.cellIndex);
                if (!waveMods.Contains(newCellIndex)) waveMods.Add(newCellIndex);
                break;

            case SelectionType.GridSlot:
                if (info.tierIndex >= 0)
                {
                    var tier = loadedData.tiers[info.tierIndex];
                    tier.cells[info.cellIndex] = 0;
                    tier.cells[newCellIndex] = info.value;
                }
                else if (info.isCircle)
                {
                    circleGrid[info.cellIndex] = 0;
                    circleGrid[newCellIndex] = info.value;
                }
                else
                {
                    squareGrid[info.cellIndex] = 0;
                    squareGrid[newCellIndex] = info.value;
                }
                break;
        }

        _currentSelection.cellIndex = newCellIndex;
        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    void CommitBridge()
    {
        if (!_isBridgeMode || _bridgeNodes.Count == 0) { CancelBridge(); return; }
        if (_selectedZoneIndex < 0 || _bridgeEndZoneIndex != _selectedZoneIndex) { CancelBridge(); return; }

        var zone = loadedData.soulZones[_selectedZoneIndex];
        int startIdx = _selectedNodeIndex;
        int endIdx   = _bridgeEndNodeIndex;

        if (startIdx > endIdx) { int tmp = startIdx; startIdx = endIdx; endIdx = tmp; }

        // Replace nodes between startIdx and endIdx with bridge cells
        Undo.RecordObject(loadedData, "Bridge Soul Zone Nodes");
        zone.nodes.RemoveRange(startIdx + 1, endIdx - startIdx - 1);
        zone.nodes.InsertRange(startIdx + 1, _bridgeNodes);
        EditorUtility.SetDirty(loadedData);
        CancelBridge();
        Repaint();
    }

    void EnsureSoulZones()
    {
        if (loadedData.soulZones == null)
            loadedData.soulZones = new List<GridData.SoulZone>();

        // Legacy migration: convert old soulSpawnPoints → soulZones on first access
        if (loadedData.soulSpawnPoints != null && loadedData.soulSpawnPoints.Count > 0
            && loadedData.soulZones.Count == 0)
        {
            foreach (var sp in loadedData.soulSpawnPoints)
            {
                loadedData.soulZones.Add(new GridData.SoulZone
                {
                    nodes  = new List<int> { sp.cellIndex },
                    souls  = sp.soulData != null ? new List<SoulData> { sp.soulData } : new List<SoulData>()
                });
            }
            loadedData.soulSpawnPoints.Clear();
            EditorUtility.SetDirty(loadedData);
            Debug.Log($"[GridDesigner] Migrated {loadedData.soulZones.Count} legacy soul spawn point(s) to soulZones.");
        }
    }

    void CommitDrawingNodes(GridData.SoulZone zone)
    {
        if (_drawingNodes.Count > 0)
        {
            zone.nodes = new List<int>(_drawingNodes);
            EditorUtility.SetDirty(loadedData);
        }
        _drawingNodes.Clear();
        _isDrawingSoulArea = false;
        Repaint();
    }

    void CancelDrawingNodes()
    {
        // If zone was just created and has no nodes yet, remove it
        if (_activeSoulZoneIndex >= 0 && _activeSoulZoneIndex < loadedData.soulZones.Count)
        {
            var zone = loadedData.soulZones[_activeSoulZoneIndex];
            if (zone.nodes == null || zone.nodes.Count == 0)
            {
                loadedData.soulZones.RemoveAt(_activeSoulZoneIndex);
                EditorUtility.SetDirty(loadedData);
            }
        }
        _drawingNodes.Clear();
        _isDrawingSoulArea = false;
        _activeSoulZoneIndex = -1;
    }

    void DrawSoulZonesSection()
    {
        EnsureSoulZones();
        EnsureSoulDataCache();

        int zoneCount = loadedData.soulZones.Count;
        _showSoulSpawns = EditorGUILayout.Foldout(_showSoulSpawns,
            $"Soul Zones ({zoneCount})", true, EditorStyles.foldoutHeader);
        if (!_showSoulSpawns) return;

        if (GUILayout.Button("+ Soul Area"))
        {
            PushUndoSnapshot();
            var newZone = new GridData.SoulZone();
            loadedData.soulZones.Add(newZone);
            _activeSoulZoneIndex = loadedData.soulZones.Count - 1;
            _drawingNodes.Clear();
            _isDrawingSoulArea = true;
            drawSoulArea       = true;
            drawSoul = drawCircle = drawOrb = drawWhirlpool = drawSelect = false;
            ClearSelectState();
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("Auto-Assign Unallocated Souls"))
            AutoAssignSouls();

        if (_isDrawingSoulArea)
        {
            EditorGUILayout.HelpBox(
                "Click cells to place nodes — click first node to close loop — Enter to finish open path — Esc to cancel",
                MessageType.Info);
        }

        EditorGUILayout.Space(4);

        int toDelete = -1;
        for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
        {
            var zone = loadedData.soulZones[zi];
            Color zc = ZonePalette[zi % ZonePalette.Length];

            bool isSelected = _activeSoulZoneIndex == zi;
            GUI.backgroundColor = isSelected ? zc : Color.white;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            // Zone header row
            EditorGUILayout.BeginHorizontal();
            Color prev = GUI.contentColor;
            GUI.contentColor = zc;
            EditorGUILayout.LabelField($"● Zone {zi}", EditorStyles.boldLabel, GUILayout.Width(70));
            GUI.contentColor = prev;
            bool zoneIsClosed = zone.nodes != null && zone.nodes.Count >= 3
                             && zone.nodes[zone.nodes.Count - 1] == zone.nodes[0];
            string closedLabel = zoneIsClosed ? "● CLOSED" : "○ OPEN";
            EditorGUILayout.LabelField($"{zone.nodes?.Count ?? 0} node(s)   {zone.souls?.Count ?? 0} soul(s)   {closedLabel}", GUILayout.ExpandWidth(true));

            if (GUILayout.Button("Select", GUILayout.Width(52)))
            {
                _activeSoulZoneIndex = zi;
                _isDrawingSoulArea   = false;
            }

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(22))) toDelete = zi;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            if (isSelected)
            {
                EditorGUI.BeginChangeCheck();
                float newRadius = EditorGUILayout.Slider("Radius", zone.radius, 0.5f, 30f);
                int   newKnots  = EditorGUILayout.IntSlider("Knot Count", zone.knotCount, 3, 32);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(loadedData, "Edit Soul Zone");
                    zone.radius     = newRadius;
                    zone.knotCount  = newKnots;
                    EditorUtility.SetDirty(loadedData);
                }

                // Souls list
                EditorGUILayout.LabelField("Souls", EditorStyles.boldLabel);
                if (zone.souls == null) zone.souls = new List<SoulData>();
                int soulToRemove = -1;
                for (int si = 0; si < zone.souls.Count; si++)
                {
                    EditorGUILayout.BeginHorizontal();
                    int currentIdx = -1;
                    if (_allSoulData != null && zone.souls[si] != null)
                        for (int s = 0; s < _allSoulData.Length; s++)
                            if (_allSoulData[s] == zone.souls[si]) { currentIdx = s; break; }

                    int popupSel = currentIdx + 1;
                    EditorGUI.BeginChangeCheck();
                    popupSel = EditorGUILayout.Popup(popupSel, _soulDropdownLabels);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(loadedData, "Assign Soul to Zone");
                        if (zone.souls[si] != null)
                        {
                            Undo.RecordObject(zone.souls[si], "Unallocate Soul");
                            zone.souls[si].allocated          = false;
                            zone.souls[si].allocatedToLevelID = "";
                            EditorUtility.SetDirty(zone.souls[si]);
                        }
                        SoulData newSoul = popupSel > 0 ? _allSoulData[popupSel - 1] : null;
                        zone.souls[si] = newSoul;
                        if (newSoul != null)
                        {
                            Undo.RecordObject(newSoul, "Allocate Soul");
                            newSoul.allocated          = true;
                            newSoul.allocatedToLevelID = loadedData.levelID;
                            if (loadedData.gameplayWavePreset != null)
                                newSoul.associatedWavePreset = loadedData.gameplayWavePreset;
                            EditorUtility.SetDirty(newSoul);
                        }
                        EditorUtility.SetDirty(loadedData);
                        _allSoulData = null;
                    }

                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("✕", GUILayout.Width(22))) soulToRemove = si;
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }

                if (soulToRemove >= 0)
                {
                    Undo.RecordObject(loadedData, "Remove Soul from Zone");
                    zone.souls.RemoveAt(soulToRemove);
                    EditorUtility.SetDirty(loadedData);
                }

                if (GUILayout.Button("+ Add Soul"))
                {
                    Undo.RecordObject(loadedData, "Add Soul to Zone");
                    if (zone.souls == null) zone.souls = new List<SoulData>();
                    zone.souls.Add(null);
                    EditorUtility.SetDirty(loadedData);
                }

                EditorGUILayout.Space(2);
                bool isClosed = zone.nodes != null && zone.nodes.Count >= 3
                             && zone.nodes[zone.nodes.Count - 1] == zone.nodes[0];
                GUI.contentColor = isClosed ? Color.green : Color.yellow;
                EditorGUILayout.LabelField(
                    isClosed ? $"Nodes: {zone.nodes.Count}  ● CLOSED LOOP" : $"Nodes: {zone.nodes?.Count ?? 0}  ○ OPEN PATH",
                    EditorStyles.miniLabel);
                GUI.contentColor = Color.white;

                // Selected node controls
                if (_selectedZoneIndex == zi && _selectedNodeIndex >= 0
                    && zone.nodes != null && _selectedNodeIndex < zone.nodes.Count)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"Selected: Node {_selectedNodeIndex + 1} of {zone.nodes.Count}  (cell {zone.nodes[_selectedNodeIndex]})", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();

                    if (GUILayout.Button("Insert Before", GUILayout.Height(20)))
                    {
                        Undo.RecordObject(loadedData, "Insert Node Before");
                        zone.nodes.Insert(_selectedNodeIndex, zone.nodes[_selectedNodeIndex]);
                        EditorUtility.SetDirty(loadedData);
                    }
                    if (GUILayout.Button("Insert After", GUILayout.Height(20)))
                    {
                        Undo.RecordObject(loadedData, "Insert Node After");
                        int insertIdx = _selectedNodeIndex + 1;
                        int cellToInsert = insertIdx < zone.nodes.Count ? zone.nodes[insertIdx] : zone.nodes[_selectedNodeIndex];
                        zone.nodes.Insert(insertIdx, cellToInsert);
                        _selectedNodeIndex = insertIdx;
                        EditorUtility.SetDirty(loadedData);
                    }
                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("Delete", GUILayout.Width(52), GUILayout.Height(20)))
                    {
                        Undo.RecordObject(loadedData, "Delete Node");
                        zone.nodes.RemoveAt(_selectedNodeIndex);
                        _selectedNodeIndex = Mathf.Clamp(_selectedNodeIndex - 1, -1, zone.nodes.Count - 1);
                        EditorUtility.SetDirty(loadedData);
                    }
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.LabelField("Shift+click another node to connect directly", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
                bool hasNodes = zone.nodes != null && zone.nodes.Count > 0;
                string drawBtnLabel = hasNodes ? "Redraw Nodes" : "Add Nodes";
                if (GUILayout.Button(drawBtnLabel))
                {
                    _activeSoulZoneIndex = zi;
                    _drawingNodes.Clear();
                    _isDrawingSoulArea   = true;
                    drawSoulArea         = true;
                    activeSlot           = -1;
                    drawSoul = drawCircle = drawOrb = drawSelect = false;
                    ClearSelectState();
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (toDelete >= 0)
        {
            PushUndoSnapshot();
            loadedData.soulZones.RemoveAt(toDelete);
            if (_activeSoulZoneIndex == toDelete) _activeSoulZoneIndex = -1;
            else if (_activeSoulZoneIndex > toDelete) _activeSoulZoneIndex--;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void DrawWhirlpoolsSection()
    {
        if (loadedData.whirlpools == null) loadedData.whirlpools = new List<GridData.WhirlpoolPoint>();

        _showWhirlpools = EditorGUILayout.Foldout(_showWhirlpools,
            $"Whirlpools ({loadedData.whirlpools.Count})", true, EditorStyles.foldoutHeader);
        if (!_showWhirlpools) return;

        EditorGUI.BeginChangeCheck();
        float newDepth = EditorGUILayout.Slider("Global Depth", loadedData.whirlpoolDepth, 0f, 20f);
        float newSwirl = EditorGUILayout.Slider("Global Swirl", loadedData.whirlpoolSwirl, 0f, 10f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Edit Whirlpool Settings");
            loadedData.whirlpoolDepth = newDepth;
            loadedData.whirlpoolSwirl = newSwirl;
            EditorUtility.SetDirty(loadedData);
        }

        EditorGUILayout.Space(2);

        if (loadedData.whirlpools.Count == 0)
        {
            EditorGUILayout.LabelField("  (click 〇 Whirlpool tool then paint on the grid)", EditorStyles.miniLabel);
        }

        int toRemove = -1;
        for (int i = 0; i < loadedData.whirlpools.Count; i++)
        {
            var wp = loadedData.whirlpools[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"#{i + 1}  Cell {wp.cellIndex}", GUILayout.Width(90));
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(22))) toRemove = i;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            float newRadius = EditorGUILayout.Slider("Radius", wp.radius, 0.1f, 30f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Whirlpool Radius");
                wp.radius = newRadius;
                loadedData.whirlpools[i] = wp;
                EditorUtility.SetDirty(loadedData);
            }
            EditorGUILayout.EndVertical();
        }

        if (toRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Remove Whirlpool");
            loadedData.whirlpools.RemoveAt(toRemove);
            EditorUtility.SetDirty(loadedData);
        }
    }

    void AutoAssignSouls()
    {
        EnsureSoulDataCache();
        if (_allSoulData == null || loadedData == null) return;

        // Build queue of unallocated souls
        var unallocated = new Queue<SoulData>();
        foreach (var s in _allSoulData)
            if (s != null && !s.allocated)
                unallocated.Enqueue(s);

        if (unallocated.Count == 0)
        {
            EditorUtility.DisplayDialog("Auto-Assign Souls", "No unallocated souls available.", "OK");
            return;
        }

        int assigned = 0;
        Undo.RecordObject(loadedData, "Auto-Assign Souls");

        foreach (var zone in loadedData.soulZones)
        {
            if (zone.souls == null) zone.souls = new List<SoulData>();
            for (int i = 0; i < zone.souls.Count; i++)
            {
                if (zone.souls[i] != null) continue; // already assigned
                if (unallocated.Count == 0) break;

                SoulData soul = unallocated.Dequeue();
                Undo.RecordObject(soul, "Allocate Soul");
                soul.allocated          = true;
                soul.allocatedToLevelID = loadedData.levelID;
                if (loadedData.gameplayWavePreset != null)
                    soul.associatedWavePreset = loadedData.gameplayWavePreset;
                EditorUtility.SetDirty(soul);
                zone.souls[i] = soul;
                assigned++;
            }
        }

        EditorUtility.SetDirty(loadedData);
        _allSoulData = null; // refresh dropdown labels
        Debug.Log($"[GridDesigner] Auto-assigned {assigned} soul(s). {unallocated.Count} unallocated soul(s) remaining.");
    }

    void EnsureSoulDataCache()
    {
        if (_allSoulData != null) return;

        string[] guids = AssetDatabase.FindAssets("t:SoulData");
        var list = new List<SoulData>();
        foreach (string g in guids)
        {
            var s = AssetDatabase.LoadAssetAtPath<SoulData>(AssetDatabase.GUIDToAssetPath(g));
            if (s != null) list.Add(s);
        }
        list.Sort((a, b) => a.soulDataIdentity.CompareTo(b.soulDataIdentity));
        _allSoulData = list.ToArray();

        // Build dropdown labels
        _soulDropdownLabels = new string[_allSoulData.Length + 1];
        _soulDropdownLabels[0] = "(none)";
        for (int i = 0; i < _allSoulData.Length; i++)
        {
            var soul = _allSoulData[i];
            string label = $"{soul.soulDataIdentity}: {soul.name}";
            if (soul.allocated && soul.allocatedToLevelID != loadedData.levelID)
                label += $"  (→ {soul.allocatedToLevelID})";
            _soulDropdownLabels[i + 1] = label;
        }
    }

    void RandomiseSoulWavePresets()
    {
        string[] guids = AssetDatabase.FindAssets("t:SoulData");
        string[] waveGuids = AssetDatabase.FindAssets("t:WavePreset");

        if (waveGuids.Length == 0)
        {
            Debug.LogWarning("[GridDesigner] No WavePreset assets found.");
            return;
        }

        var wavePresets = new List<WavePreset>();
        foreach (string g in waveGuids)
        {
            var wp = AssetDatabase.LoadAssetAtPath<WavePreset>(AssetDatabase.GUIDToAssetPath(g));
            if (wp != null) wavePresets.Add(wp);
        }

        int assigned = 0;
        foreach (string g in guids)
        {
            var soul = AssetDatabase.LoadAssetAtPath<SoulData>(AssetDatabase.GUIDToAssetPath(g));
            if (soul == null) continue;

            Undo.RecordObject(soul, "Randomise Soul Wave Presets");
            soul.associatedWavePreset = wavePresets[UnityEngine.Random.Range(0, wavePresets.Count)];
            EditorUtility.SetDirty(soul);
            assigned++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[GridDesigner] Randomised wave presets for {assigned} souls.");
    }

    // ─────────────────────────────────────────────
    // PORTAL LIST  (Arena Entrances)
    // ─────────────────────────────────────────────

    void DrawPortalList()
    {
        if (loadedData.entrances == null) loadedData.entrances = new List<GridData.ArenaEntrance>();

        string[] tierLabels = BuildTierLabels();

        // ── Entrances ──
        EditorGUILayout.LabelField("Entrances", EditorStyles.boldLabel);

        int entToRemove = -1;
        for (int i = 0; i < loadedData.entrances.Count; i++)
        {
            var ent = loadedData.entrances[i];
            bool wasLocked = ent.isLocked;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Row 1: ID / Angle / remove button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ID", GUILayout.Width(18));
            string newEntID    = EditorGUILayout.TextField(ent.id, GUILayout.Width(78));
            
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 36f;
            float newEntAngle = EditorGUILayout.FloatField("Angle", ent.perimeterAngle, GUILayout.Width(74));
            EditorGUIUtility.labelWidth = prevLabelWidth;

            EditorGUILayout.LabelField("°", GUILayout.Width(10));
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("↺", GUILayout.Width(22)))
            {
                Undo.RecordObject(loadedData, "Reset Entrance");
                loadedData.entrances[i] = new GridData.ArenaEntrance
                {
                    id             = ent.id,
                    perimeterAngle = ent.perimeterAngle,
                };
                EditorUtility.SetDirty(loadedData);
            }
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(22))) entToRemove = i;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Row 2: Tier
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Tier", GUILayout.Width(28));
            int entTierPopup   = EditorGUILayout.Popup(Mathf.Clamp(ent.tierSlot + 1, 0, tierLabels.Length - 1), tierLabels, GUILayout.Width(72));
            int newEntTierSlot = entTierPopup - 1;
            EditorGUILayout.EndHorizontal();

            // Row 3: Locked toggle + hub angle
            EditorGUILayout.BeginHorizontal();
            bool newIsLocked = EditorGUILayout.ToggleLeft("Locked", ent.isLocked, GUILayout.Width(62));
            float newHubAngle = ent.lockHubAngle;
            GameObject newHubPrefab = ent.lockHubPrefab;
            if (newIsLocked)
            {
                float prevLW2 = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 46f;
                newHubAngle = EditorGUILayout.FloatField("Hub °", ent.lockHubAngle, GUILayout.Width(88));
                EditorGUIUtility.labelWidth = prevLW2;
            }
            EditorGUILayout.EndHorizontal();

            if (newIsLocked)
            {
                // Row 4: Hub prefab
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Hub Prefab", GUILayout.Width(70));
                newHubPrefab = (GameObject)EditorGUILayout.ObjectField(ent.lockHubPrefab, typeof(GameObject), false);
                EditorGUILayout.EndHorizontal();

                // Row 5: Subdivisions + Regenerate
                if (ent.tubePath == null) ent.tubePath = new List<UnityEngine.Vector2Int>();
                EditorGUILayout.BeginHorizontal();
                float prevLW3 = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 72f;
                int newSubs = EditorGUILayout.IntField("Subdivisions", ent.tubeSubdivisions, GUILayout.Width(120));
                EditorGUIUtility.labelWidth = prevLW3;
                if (newSubs != ent.tubeSubdivisions)
                {
                    Undo.RecordObject(loadedData, "Set Tube Subdivisions");
                    ent.tubeSubdivisions = Mathf.Max(0, newSubs);
                    EditorUtility.SetDirty(loadedData);
                }
                GUI.enabled = ent.tubePath.Count >= 2;
                if (GUILayout.Button("+", GUILayout.Width(22)))
                {
                    Undo.RecordObject(loadedData, "Subdivide Tube Path");
                    var old = ent.tubePath;
                    var subdivided = new List<UnityEngine.Vector2Int>();
                    for (int si = 0; si < old.Count - 1; si++)
                    {
                        subdivided.Add(old[si]);
                        var mid = new UnityEngine.Vector2Int(
                            Mathf.RoundToInt((old[si].x + old[si + 1].x) * 0.5f),
                            Mathf.RoundToInt((old[si].y + old[si + 1].y) * 0.5f));
                        subdivided.Add(mid);
                    }
                    subdivided.Add(old[old.Count - 1]);
                    ent.tubePath = subdivided;
                    EditorUtility.SetDirty(loadedData);
                }
                if (GUILayout.Button("Regenerate", GUILayout.Width(78)))
                    GenerateTubePath(i);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                // Row 6: Place / Edit / Clear
                bool isPlacingThis = _tubePlacingEntranceIndex == i;
                bool isEditingThis = _tubeDrawEntranceIndex == i;
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = isPlacingThis ? new Color(0.4f, 1f, 0.6f) : Color.white;
                if (GUILayout.Button(isPlacingThis ? "● Placing…" : "Place Input Tube", GUILayout.Width(110)))
                {
                    if (isPlacingThis)
                    {
                        _tubePlacingEntranceIndex = -1;
                    }
                    else
                    {
                        _tubePlacingEntranceIndex = i;
                        _tubeDrawEntranceIndex    = -1;
                        _dragTubeNodeIndex        = -1;
                        _selectedTubeNodeIndex    = -1;
                    }
                }
                GUI.backgroundColor = Color.white;
                if (ent.tubePath.Count > 0)
                {
                    GUI.backgroundColor = isEditingThis ? new Color(0.5f, 0.8f, 1f) : Color.white;
                    if (GUILayout.Button(isEditingThis ? "● Editing" : "Edit Nodes", GUILayout.Width(76)))
                    {
                        if (isEditingThis)
                        {
                            _tubeDrawEntranceIndex = -1;
                            _dragTubeNodeIndex     = -1;
                            _selectedTubeNodeIndex = -1;
                        }
                        else
                        {
                            _tubeDrawEntranceIndex    = i;
                            _tubePlacingEntranceIndex = -1;
                        }
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    Undo.RecordObject(loadedData, "Clear Tube Path");
                    ent.tubePath           = new List<UnityEngine.Vector2Int>();
                    _tubePlacingEntranceIndex = -1;
                    _tubeDrawEntranceIndex    = -1;
                    _dragTubeNodeIndex        = -1;
                    _selectedTubeNodeIndex    = -1;
                    EditorUtility.SetDirty(loadedData);
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.LabelField($"{ent.tubePath.Count} nodes", GUILayout.Width(52));
                EditorGUILayout.EndHorizontal();

                // ── Knot breakdown ──
                if (ent.tubePath != null && ent.tubePath.Count >= 2)
                {
                    int tubeKnots = 0;
                    int hubKnots  = 0;
                    const int bridgeKnots = 3; // SoulFishInputTube.joinKnotCount default

                    var tubePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/ModifierPrefabs/SoulFishInputTube.prefab");
                    if (tubePrefabAsset != null)
                    {
                        var sc = tubePrefabAsset.GetComponentInChildren<UnityEngine.Splines.SplineContainer>();
                        if (sc != null) tubeKnots = sc.Spline.Count;
                    }

                    if (ent.lockHubPrefab != null)
                    {
                        var hubPrefabPath = AssetDatabase.GetAssetPath(ent.lockHubPrefab);
                        var hubAsset      = AssetDatabase.LoadAssetAtPath<GameObject>(hubPrefabPath);
                        if (hubAsset != null)
                        {
                            var sc = hubAsset.GetComponentInChildren<UnityEngine.Splines.SplineContainer>();
                            if (sc != null) hubKnots = sc.Spline.Count;
                        }
                    }

                    int waypointKnots = Mathf.Max(0, ent.tubePath.Count - 2);
                    int total         = tubeKnots + waypointKnots + bridgeKnots + hubKnots;

                    EditorGUILayout.LabelField(
                        $"Knots — tube:{tubeKnots}  path:{waypointKnots}  bridge:{bridgeKnots}  hub:{hubKnots}  = {total}",
                        EditorStyles.miniLabel);
                }
            }
            else
            {
                if (_tubeDrawEntranceIndex    == i) _tubeDrawEntranceIndex    = -1;
                if (_tubePlacingEntranceIndex == i) _tubePlacingEntranceIndex = -1;
            }

            EditorGUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Entrance");
                ent.id             = newEntID;
                ent.perimeterAngle = newEntAngle;
                ent.tierSlot       = newEntTierSlot;
                ent.isLocked       = newIsLocked;
                ent.lockHubAngle   = newHubAngle;
                ent.lockHubPrefab  = newHubPrefab;

                // When first locking an entrance, default hub angle to the door's own angle
                if (newIsLocked && !wasLocked)
                    ent.lockHubAngle = ent.perimeterAngle;

                // Auto-assign default lock hub prefab when locking an entrance
                if (newIsLocked && ent.lockHubPrefab == null)
                    ent.lockHubPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefab/LevelPrefabs/DoorLockHub.prefab");

                EditorUtility.SetDirty(loadedData);
                Repaint();
            }
        }

        if (entToRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Remove Entrance");
            loadedData.entrances.RemoveAt(entToRemove);
            if (_tubeDrawEntranceIndex    == entToRemove) { _tubeDrawEntranceIndex    = -1; _dragTubeNodeIndex = -1; _selectedTubeNodeIndex = -1; }
            else if (_tubeDrawEntranceIndex    > entToRemove) _tubeDrawEntranceIndex--;
            if (_tubePlacingEntranceIndex == entToRemove)   _tubePlacingEntranceIndex = -1;
            else if (_tubePlacingEntranceIndex > entToRemove) _tubePlacingEntranceIndex--;
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("+ Add Entrance"))
        {
            Undo.RecordObject(loadedData, "Add Entrance");
            loadedData.entrances.Add(new GridData.ArenaEntrance
                { id = $"entrance_{loadedData.entrances.Count}" });
            EditorUtility.SetDirty(loadedData);
        }

    }

    string[] BuildTierLabels()
    {
        int count = loadedData?.tiers?.Count ?? 0;
        var labels = new string[count + 1];
        labels[0] = "Base";
        for (int i = 0; i < count; i++)
            labels[i + 1] = $"T{i + 1} {loadedData.tiers[i].name}";
        return labels;
    }

    void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(false));

        // ── Grid display controls ──
        EditorGUILayout.BeginHorizontal();
        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 80f;
        EditorGUI.BeginChangeCheck();
        float newOpacity    = EditorGUILayout.Slider("Grid Lines", _gridLineOpacity,    0f, 1f);
        float newBrightness = EditorGUILayout.Slider("Backdrop",   _backdropBrightness, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            _gridLineOpacity    = newOpacity;
            _backdropBrightness = newBrightness;
            EditorPrefs.SetFloat(PrefKeyGridOpacity,    _gridLineOpacity);
            EditorPrefs.SetFloat(PrefKeyBackdropBright, _backdropBrightness);
            Repaint();
        }
        EditorGUIUtility.labelWidth = prevLW;
        EditorGUILayout.EndHorizontal();

        DrawGrid();
        EditorGUILayout.EndVertical();
        DrawRightPanelResizeHandle();
        DrawDebugConsole();
    }

    void DrawPrefabLibrarySection()
    {
        showPrefabLibrary = EditorGUILayout.Foldout(showPrefabLibrary, "Prefab Library", true, EditorStyles.foldoutHeader);
        if (!showPrefabLibrary) return;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Toggle(_prefabLibTab == PrefabLibraryTab.MazePieces, "Mazepieces", EditorStyles.miniButtonLeft))
            _prefabLibTab = PrefabLibraryTab.MazePieces;
        if (GUILayout.Toggle(_prefabLibTab == PrefabLibraryTab.SetPieces,  "Setpieces",  EditorStyles.miniButtonMid))
            _prefabLibTab = PrefabLibraryTab.SetPieces;
        if (GUILayout.Toggle(_prefabLibTab == PrefabLibraryTab.Statues,    "Statues",    EditorStyles.miniButtonMid))
            _prefabLibTab = PrefabLibraryTab.Statues;
        if (GUILayout.Toggle(_prefabLibTab == PrefabLibraryTab.Modifiers,  "Modifiers",  EditorStyles.miniButtonRight))
            _prefabLibTab = PrefabLibraryTab.Modifiers;
        EditorGUILayout.EndHorizontal();

        if (_prefabLibTab == PrefabLibraryTab.MazePieces)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(42));
            string newPath = EditorGUILayout.TextField(prefabFolderPath);
            if (newPath != prefabFolderPath) { prefabFolderPath = newPath; EditorPrefs.SetString("GridDesigner_PrefabFolder", prefabFolderPath); }
            if (GUILayout.Button("…", GUILayout.Width(22)))
            {
                string sel = EditorUtility.OpenFolderPanel("Select MazePieces Folder", prefabFolderPath, "");
                if (!string.IsNullOrEmpty(sel)) { prefabFolderPath = FileUtil.GetProjectRelativePath(sel); EditorPrefs.SetString("GridDesigner_PrefabFolder", prefabFolderPath); ScanPrefabFolder(); }
            }
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanPrefabFolder();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Icons", GUILayout.Width(42));
            string newIcons = EditorGUILayout.TextField(iconsFolderPath);
            if (newIcons != iconsFolderPath) { iconsFolderPath = newIcons; EditorPrefs.SetString("GridDesigner_IconsFolder", iconsFolderPath); }
            if (GUILayout.Button("…", GUILayout.Width(22)))
            {
                string sel = EditorUtility.OpenFolderPanel("Select Icons Folder", iconsFolderPath, "");
                if (!string.IsNullOrEmpty(sel)) { iconsFolderPath = FileUtil.GetProjectRelativePath(sel); EditorPrefs.SetString("GridDesigner_IconsFolder", iconsFolderPath); ScanIcons(); }
            }
            EditorGUILayout.EndHorizontal();

            if (scannedPrefabs.Count == 0)
            {
                EditorGUILayout.LabelField("  (no prefabs found)", EditorStyles.miniLabel);
            }
            else
            {
                const int IconSize = 32;
                prefabScrollPos = EditorGUILayout.BeginScrollView(prefabScrollPos, GUILayout.Height(160));
                for (int i = 0; i < scannedPrefabs.Count; i++)
                {
                    bool isSelected = drawDirectPrefab && _prefabLibTab == PrefabLibraryTab.MazePieces && selectedPrefabIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                    prefabIcons.TryGetValue(scannedPrefabs[i].name, out Texture2D icon);
                    var content = icon != null
                        ? new GUIContent(" " + scannedPrefabs[i].name, icon)
                        : new GUIContent(scannedPrefabs[i].name);
                    float btnHeight = icon != null ? IconSize + 4 : EditorGUIUtility.singleLineHeight + 2;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(content, GUILayout.Height(btnHeight)))
                    {
                        selectedPrefabIndex   = i;
                        selectedSetPieceIndex = -1;
                        selectedStatueIndex   = -1;
                        _activePlacementPrefab = scannedPrefabs[i];
                        _activePlacementIsWorldSpaceProp = false;
                        drawDirectPrefab = true;
                        activeSlot = -1;
                        drawCircle = drawOrb = drawSoul = drawSoulArea = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false;
                        drawSelect = false;
                        ClearSelectState();
                        LogSelection(_currentSelection);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("⊙", GUILayout.Width(22), GUILayout.Height(btnHeight)))
                        EditorGUIUtility.PingObject(scannedPrefabs[i]);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
        else if (_prefabLibTab == PrefabLibraryTab.SetPieces)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(42));
            EditorGUILayout.LabelField(SetPiecesFolder, EditorStyles.miniLabel);
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanSetPiecesLib();
            EditorGUILayout.EndHorizontal();

            if (scannedSetPiecesLib.Count == 0)
            {
                EditorGUILayout.LabelField("  (no prefabs found)", EditorStyles.miniLabel);
            }
            else
            {
                setpieceScrollPos = EditorGUILayout.BeginScrollView(setpieceScrollPos, GUILayout.Height(160));
                for (int i = 0; i < scannedSetPiecesLib.Count; i++)
                {
                    bool isSelected = drawDirectPrefab && _prefabLibTab == PrefabLibraryTab.SetPieces && selectedSetPieceIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(scannedSetPiecesLib[i].name))
                    {
                        selectedSetPieceIndex  = i;
                        selectedPrefabIndex    = -1;
                        selectedStatueIndex    = -1;
                        _activePlacementPrefab = scannedSetPiecesLib[i];
                        _activePlacementIsWorldSpaceProp = false;
                        drawDirectPrefab = true;
                        activeSlot = -1;
                        drawCircle = drawOrb = drawSoul = drawSoulArea = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false;
                        drawSelect = false;
                        ClearSelectState();
                        LogSelection(_currentSelection);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("⊙", GUILayout.Width(22)))
                        EditorGUIUtility.PingObject(scannedSetPiecesLib[i]);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
        else if (_prefabLibTab == PrefabLibraryTab.Statues)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(42));
            EditorGUILayout.LabelField(StatuesPrefabsFolder, EditorStyles.miniLabel);
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanStatuesLib();
            EditorGUILayout.EndHorizontal();

            if (scannedStatuesLib.Count == 0)
            {
                EditorGUILayout.LabelField("  (no prefabs found)", EditorStyles.miniLabel);
            }
            else
            {
                statueScrollPos = EditorGUILayout.BeginScrollView(statueScrollPos, GUILayout.Height(160));
                for (int i = 0; i < scannedStatuesLib.Count; i++)
                {
                    bool isSelected = drawDirectPrefab && _prefabLibTab == PrefabLibraryTab.Statues && selectedStatueIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(scannedStatuesLib[i].name))
                    {
                        selectedStatueIndex    = i;
                        selectedPrefabIndex    = -1;
                        selectedSetPieceIndex  = -1;
                        selectedModifierIndex  = -1;
                        _activePlacementPrefab = scannedStatuesLib[i];
                        _activePlacementIsWorldSpaceProp = true;
                        drawDirectPrefab = true;
                        activeSlot = -1;
                        drawCircle = drawOrb = drawSoul = drawSoulArea = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false;
                        drawSelect = false;
                        ClearSelectState();
                        LogSelection(_currentSelection);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("⊙", GUILayout.Width(22)))
                        EditorGUIUtility.PingObject(scannedStatuesLib[i]);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
        else // Modifiers tab
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(42));
            EditorGUILayout.LabelField(ModifiersPrefabsFolder, EditorStyles.miniLabel);
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanModifiersLib();
            EditorGUILayout.EndHorizontal();

            if (scannedModifiersLib.Count == 0)
            {
                EditorGUILayout.LabelField("  (no prefabs found)", EditorStyles.miniLabel);
            }
            else
            {
                modifierScrollPos = EditorGUILayout.BeginScrollView(modifierScrollPos, GUILayout.Height(160));
                for (int i = 0; i < scannedModifiersLib.Count; i++)
                {
                    bool isSelected = drawDirectPrefab && _prefabLibTab == PrefabLibraryTab.Modifiers && selectedModifierIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(scannedModifiersLib[i].name))
                    {
                        selectedModifierIndex  = i;
                        selectedPrefabIndex    = -1;
                        selectedSetPieceIndex  = -1;
                        selectedStatueIndex    = -1;
                        _activePlacementPrefab = scannedModifiersLib[i];
                        _activePlacementIsWorldSpaceProp = false;
                        drawDirectPrefab = true;
                        activeSlot = -1;
                        drawCircle = drawOrb = drawSoul = drawSoulArea = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false;
                        drawSelect = false;
                        ClearSelectState();
                        LogSelection(_currentSelection);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("⊙", GUILayout.Width(22)))
                        EditorGUIUtility.PingObject(scannedModifiersLib[i]);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }

        if (drawDirectPrefab && _activePlacementPrefab != null)
            EditorGUILayout.HelpBox($"Placing: {_activePlacementPrefab.name}", MessageType.None);
    }

    void DrawDebugConsole()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(_rightPanelWidth), GUILayout.ExpandHeight(true));
        _rightPanelScroll = EditorGUILayout.BeginScrollView(_rightPanelScroll, GUILayout.ExpandHeight(true));

        DrawToolButtons();
        DrawPrefabLibrarySection();
        if (loadedData != null) DrawSplineWallsSection();
        if (loadedData != null) DrawSoulZonesSection();

        if (loadedData != null && loadedData.linkedPairs != null && loadedData.linkedPairs.Count > 0)
        {
            bool hasBroken = false;
            foreach (var pair in loadedData.linkedPairs)
            {
                if (!CellContainsPrefab(pair.modifierTierIndex, pair.modifierCellIndex, "TypeBWaveModifier") ||
                    !CellContainsPrefab(pair.inputTubeTierIndex, pair.inputTubeCellIndex, "SoulFishInputTube"))
                {
                    hasBroken = true;
                    break;
                }
            }

            if (hasBroken)
            {
                EditorGUILayout.HelpBox("Broken modifier links detected!", MessageType.Warning);
                if (GUILayout.Button("Clean Broken Links", EditorStyles.miniButton))
                {
                    PushUndoSnapshot();
                    loadedData.linkedPairs.RemoveAll(p =>
                        !CellContainsPrefab(p.modifierTierIndex, p.modifierCellIndex, "TypeBWaveModifier") ||
                        !CellContainsPrefab(p.inputTubeTierIndex, p.inputTubeCellIndex, "SoulFishInputTube")
                    );
                    EditorUtility.SetDirty(loadedData);
                    Repaint();
                }
            }
            else
            {
                EditorGUILayout.LabelField($"Active Modifier Links: {loadedData.linkedPairs.Count}", EditorStyles.miniLabel);
            }
        }

        DrawTypeBModifierSettingsSection();
        DrawSelectedPrefabScaleSection();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("Grid Debug", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(42)))
            _debugLog.Clear();
        EditorGUILayout.EndHorizontal();

        _debugLogScroll = EditorGUILayout.BeginScrollView(_debugLogScroll, GUILayout.Height(140));

        GUIStyle warnStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        warnStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
        GUIStyle errStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        errStyle.normal.textColor = new Color(1f, 0.4f, 0.4f);
        GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        infoStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f);

        foreach (string line in _debugLog)
        {
            GUIStyle s = line.StartsWith("[WARN]") ? warnStyle
                       : line.StartsWith("[ERR]")  ? errStyle
                       : infoStyle;
            GUILayout.Label(line, s);
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndScrollView(); // right panel scroll
        EditorGUILayout.EndVertical();
    }

    // Per-modifier controls for TypeB wave modifiers placed in the grid. Only
    // shown when at least one TypeB modifier is present. Values entered here
    // override the prefab's default speed / frequency / ripple-depth boosts.
    void DrawTypeBModifierSettingsSection()
    {
        if (loadedData == null) return;

        var typeBPlacements = new List<(int tierIndex, GridData.PrefabPlacement placement)>();
        CollectTypeBPlacements(-1, loadedData.prefabPlacements, typeBPlacements);
        if (loadedData.tiers != null)
        {
            for (int ti = 0; ti < loadedData.tiers.Count; ti++)
                CollectTypeBPlacements(ti, loadedData.tiers[ti].prefabPlacements, typeBPlacements);
        }

        if (typeBPlacements.Count == 0) return;

        EditorGUILayout.Space(4);
        _showTypeBSettings = EditorGUILayout.Foldout(_showTypeBSettings,
            $"Wave Modifier Settings ({typeBPlacements.Count})", true, EditorStyles.foldoutHeader);
        if (!_showTypeBSettings) return;

        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 110f;

        foreach (var (tierIndex, pp) in typeBPlacements)
        {
            var defaults = pp.prefab.GetComponent<LevelWaveModifierControllerTypeB>();
            string tierLabel = tierIndex == -1 ? "Base" : $"Tier {tierIndex}";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"TypeB · {tierLabel} · Cell {pp.cellIndex}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool ov = EditorGUILayout.ToggleLeft("Override defaults", pp.overrideModifierSettings);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Toggle Wave Modifier Override");
                pp.overrideModifierSettings = ov;
                // Seed the custom values from the prefab defaults the moment
                // override is switched on, so editing starts from a sensible base.
                if (ov && defaults != null)
                {
                    pp.speedBoost       = defaults.speedBoost;
                    pp.frequencyBoost   = defaults.frequencyBoost;
                    pp.rippleDepthBoost = defaults.rippleDepthBoost;
                }
                EditorUtility.SetDirty(loadedData);
            }

            using (new EditorGUI.DisabledScope(!pp.overrideModifierSettings))
            {
                float dispSpeed  = pp.overrideModifierSettings ? pp.speedBoost       : (defaults != null ? defaults.speedBoost       : 0f);
                float dispFreq   = pp.overrideModifierSettings ? pp.frequencyBoost   : (defaults != null ? defaults.frequencyBoost   : 0f);
                float dispRipple = pp.overrideModifierSettings ? pp.rippleDepthBoost : (defaults != null ? defaults.rippleDepthBoost : 0f);

                EditorGUI.BeginChangeCheck();
                float newSpeed  = EditorGUILayout.FloatField("Speed Boost",        dispSpeed);
                float newFreq   = EditorGUILayout.FloatField("Frequency Boost",    dispFreq);
                float newRipple = EditorGUILayout.FloatField("Ripple Depth Boost", dispRipple);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(loadedData, "Edit Wave Modifier Settings");
                    pp.speedBoost       = newSpeed;
                    pp.frequencyBoost   = newFreq;
                    pp.rippleDepthBoost = newRipple;
                    EditorUtility.SetDirty(loadedData);
                }
            }

            if (!pp.overrideModifierSettings)
                EditorGUILayout.LabelField("Using prefab defaults", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        EditorGUIUtility.labelWidth = prevLW;
    }

    // Scale slider for the currently-selected prefab placement. Only shown when the
    // selected prefab has a PrefabBaselineAlignment scale radius enabled.
    void DrawSelectedPrefabScaleSection()
    {
        if (loadedData == null || _currentSelection.type != SelectionType.PrefabPlacement) return;

        List<GridData.PrefabPlacement> placements =
            _currentSelection.tierIndex == -1
                ? loadedData.prefabPlacements
                : (loadedData.tiers != null && _currentSelection.tierIndex < loadedData.tiers.Count
                    ? loadedData.tiers[_currentSelection.tierIndex].prefabPlacements
                    : null);

        if (placements == null || _currentSelection.index < 0 || _currentSelection.index >= placements.Count) return;

        var pp = placements[_currentSelection.index];
        if (pp?.prefab == null) return;

        var align = GetBaselineAlign(pp.prefab);
        if (align == null || !align.UseScaleRadius) return;

        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 110f;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"Scale · {pp.prefab.name}", EditorStyles.boldLabel);

        float cur = pp.scale > 0f ? pp.scale : 1f;
        EditorGUI.BeginChangeCheck();
        float ns = EditorGUILayout.Slider("Scale", cur, 0.25f, 5f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Scale Prefab Placement");
            pp.scale = ns;
            EditorUtility.SetDirty(loadedData);
            Repaint();
        }

        if (GUILayout.Button("Reset to 1", EditorStyles.miniButton))
        {
            Undo.RecordObject(loadedData, "Reset Prefab Scale");
            pp.scale = 1f;
            EditorUtility.SetDirty(loadedData);
            Repaint();
        }

        EditorGUILayout.LabelField($"Footprint ≈ {align.ScaleRadius * cur:0.##} world units", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUIUtility.labelWidth = prevLW;
    }

    void CollectTypeBPlacements(int tierIndex, List<GridData.PrefabPlacement> placements,
        List<(int, GridData.PrefabPlacement)> results)
    {
        if (placements == null) return;
        foreach (var pp in placements)
        {
            if (pp?.prefab == null) continue;
            if (pp.prefab.GetComponent<LevelWaveModifierControllerTypeB>() != null)
                results.Add((tierIndex, pp));
        }
    }

    void DrawGrid()
    {
        // Fixed viewport — layout size never changes
        Rect viewRect = GUILayoutUtility.GetRect(GridPixelSize, GridPixelSize,
            GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

        Event e = Event.current;

        // ── Navigation ──
        if (viewRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.ScrollWheel)
            {
                float prevZoom = _gridZoom;
                _gridZoom = Mathf.Clamp(_gridZoom - e.delta.y * 0.05f, 0.3f, 4f);
                // Zoom around mouse position
                Vector2 mouseLocal = e.mousePosition - viewRect.position - _gridPanOffset;
                _gridPanOffset    -= mouseLocal * (_gridZoom / prevZoom - 1f);
                e.Use();
                Repaint();
            }
            if (e.type == EventType.MouseDown && e.button == 2)
            {
                _isPanningGrid = true;
                e.Use();
            }
        }
        if (e.type == EventType.MouseDrag && e.button == 2 && _isPanningGrid)
        {
            _gridPanOffset += e.delta;
            e.Use();
            Repaint();
        }
        if (e.type == EventType.MouseUp && e.button == 2)
        {
            _isPanningGrid = false;
            e.Use();
        }

        // Draw rect — panned and zoomed, may extend beyond viewport
        Rect rect = new Rect(
            viewRect.x + _gridPanOffset.x,
            viewRect.y + _gridPanOffset.y,
            ZoomedGridSize, ZoomedGridSize);

        Handles.BeginGUI();
        Handles.color = new Color(1f, 1f, 1f, _backdropBrightness);
        Handles.DrawSolidDisc(rect.center, Vector3.forward, ZoomedGridSize * 0.5f);
        Handles.EndGUI();

        // Spline wall input — handled at rect level before cell loop so nodes are free-floating
        if (_drawSplineWall && loadedData != null)
            HandleSplineWallInput(rect, e);

        // Select tool — spline wall node picking (pixel-based, before cell loop)
        if (drawSelect && loadedData != null)
            HandleSelectSplineWallInput(rect, e);

        // Tube path placement mode — mouse preview + click to place
        if (_tubePlacingEntranceIndex >= 0 && loadedData != null)
            HandleTubePlacementInput(rect, e);

        // Tube path edit/drag mode
        if (_tubeDrawEntranceIndex >= 0 && loadedData != null)
            HandleTubePathInput(rect, e);

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int  index = y * GridSize + x;
                Rect cell  = new Rect(rect.x + x * EffCell, rect.y + y * EffCell, EffCell, EffCell);
                if (!viewRect.Overlaps(cell)) continue; // skip cells outside viewport

                // Base layer alpha: full when active or no tiers exist, faint when a tier is active
                float baseAlpha = (!baseLayerVisible) ? 0f :
                                  (activeTierIndex < 0) ? 1f : 0.12f;

                if (baseAlpha > 0f)
                {
                    int sq = squareGrid[index];
                    if (sq > 0 && sq <= slotColors.Count)
                    {
                        Color c = slotColors[sq - 1]; c.a = baseAlpha;
                        EditorGUI.DrawRect(cell, c);
                    }

                    int circ = circleGrid[index];
                    if (circ > 0 && circ <= slotColors.Count)
                    {
                        Color c = slotColors[circ - 1]; c.a = baseAlpha;
                        Handles.color = c;
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.3f);
                    }

                    // Soul zones — draw node markers and connecting lines
                    if (loadedData?.soulZones != null)
                    {
                        for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
                        {
                            var zone = loadedData.soulZones[zi];
                            if (zone.nodes == null) continue;
                            Color zc = ZonePalette[zi % ZonePalette.Length];
                            zc.a = baseAlpha;
                            int nodeIdx = zone.nodes.IndexOf(index);
                            if (nodeIdx >= 0)
                            {
                                Handles.color = zc;
                                Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.38f);
                                Handles.color = new Color(0f, 0f, 0f, baseAlpha);
                                Handles.Label(cell.center - new Vector2(4f, 6f), $"Z{zi}");
                            }
                        }
                    }
                    // In-progress drawing nodes
                    if (_isDrawingSoulArea && _drawingNodes.Contains(index))
                    {
                        Handles.color = new Color(1f, 1f, 1f, baseAlpha * 0.7f);
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.3f);
                    }

                    // Selection highlight (removed from loop, moved to overlay)


                    // Bridge mode — highlight endpoint and in-progress cells
                    if (_isBridgeMode)
                    {
                        if (_bridgeEndZoneIndex >= 0 && _bridgeEndZoneIndex < loadedData.soulZones.Count)
                        {
                            var endZone = loadedData.soulZones[_bridgeEndZoneIndex];
                            if (endZone.nodes != null && _bridgeEndNodeIndex < endZone.nodes.Count
                                && endZone.nodes[_bridgeEndNodeIndex] == index)
                            {
                                Handles.color = new Color(0.4f, 0.8f, 1f, baseAlpha);
                                Handles.DrawWireDisc(cell.center, Vector3.forward, EffCell * 0.44f);
                            }
                        }
                        if (_bridgeNodes.Contains(index))
                        {
                            Handles.color = new Color(0.4f, 0.8f, 1f, baseAlpha * 0.6f);
                            Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.28f);
                        }
                    }

                    // Orb
                    if (loadedData?.orbCellIndices != null && loadedData.orbCellIndices.Contains(index))
                    {
                        Handles.color = new Color(1f, 1f, 0f, baseAlpha);
                        Handles.DrawWireDisc(cell.center, Vector3.forward, EffCell * 0.35f);
                    }

                    // Water Level Modifier
                    if (loadedData?.waterLevelModifierCellIndices != null && loadedData.waterLevelModifierCellIndices.Contains(index))
                    {
                        Handles.color = new Color(0.4f, 0.8f, 1f, baseAlpha);
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.38f);
                        Handles.color = new Color(1f, 1f, 1f, baseAlpha);
                        Handles.Label(cell.center - new Vector2(4f, 6f), "W");
                    }

                    // Wave Modifier
                    if (loadedData?.waveModifierCellIndices != null && loadedData.waveModifierCellIndices.Contains(index))
                    {
                        Handles.color = new Color(0.4f, 1f, 0.4f, baseAlpha);
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.38f);
                        Handles.color = new Color(0f, 0f, 0f, baseAlpha);
                        Handles.Label(cell.center - new Vector2(4f, 6f), "~");
                    }

                    // Whirlpool
                    if (loadedData?.whirlpools != null && loadedData.whirlpools.Exists(w => w.cellIndex == index))
                    {
                        Handles.color = new Color(0.6f, 0.2f, 1f, baseAlpha);
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.38f);
                        Handles.color = new Color(1f, 1f, 1f, baseAlpha);
                        Handles.Label(cell.center - new Vector2(4f, 6f), "〇");
                    }

                    // Direct prefab placements (base layer)
                    var bp = loadedData?.prefabPlacements?.Find(p => p.cellIndex == index);
                    if (bp != null)
                    {
                        Texture2D bpIcon = bp.prefab != null && prefabIcons.TryGetValue(bp.prefab.name, out var bpi) ? bpi : null;
                        if (bpIcon != null)
                        {
                            GUI.color = new Color(1f, 1f, 1f, baseAlpha);
                            GUI.DrawTexture(cell, bpIcon, ScaleMode.ScaleToFit);
                            GUI.color = Color.white;
                        }
                        else
                        {
                            Color c = GetPrefabColor(bp.prefab); c.a = baseAlpha;
                            EditorGUI.DrawRect(cell, c);
                            Handles.color = new Color(0f, 0f, 0f, baseAlpha);
                            string label = bp.prefab != null ? bp.prefab.name.Substring(0, Mathf.Min(2, bp.prefab.name.Length)) : "?";
                            Handles.Label(cell.center - new Vector2(5f, 6f), label);
                        }
                    }
                }

                // Tiers — inset squares + modifiers, active = full opacity, others = faint if visible
                if (loadedData?.tiers != null)
                {
                    for (int ti = 0; ti < loadedData.tiers.Count; ti++)
                    {
                        bool visible = ti < tierVisible.Count && tierVisible[ti];
                        if (!visible) continue;

                        var  tier     = loadedData.tiers[ti];
                        bool isActive = activeTierIndex == ti;
                        float a       = isActive ? 1f : 0.12f;

                        // Prefab cells
                        if (tier.cells != null && index < tier.cells.Length)
                        {
                            int tc = tier.cells[index];
                            if (tc > 0 && tc <= slotColors.Count)
                            {
                                Color c = slotColors[tc - 1]; c.a = a;
                                float inset = 2f;
                                EditorGUI.DrawRect(new Rect(cell.x + inset, cell.y + inset,
                                    EffCell - inset * 2, EffCell - inset * 2), c);
                                if (isActive)
                                {
                                    Handles.color = Color.white;
                                    Handles.Label(cell.center - new Vector2(4f, 6f), $"{ti + 1}");
                                }
                            }
                        }

                        // Water Level Modifier
                        if (tier.waterLevelModifierCellIndices != null && tier.waterLevelModifierCellIndices.Contains(index))
                        {
                            Handles.color = new Color(0.4f, 0.8f, 1f, a);
                            Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.38f);
                            Handles.color = new Color(1f, 1f, 1f, a);
                            Handles.Label(cell.center - new Vector2(4f, 6f), "W");
                        }

                        // Wave Modifier
                        if (tier.waveModifierCellIndices != null && tier.waveModifierCellIndices.Contains(index))
                        {
                            Handles.color = new Color(0.4f, 1f, 0.4f, a);
                            Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.38f);
                            Handles.color = new Color(0f, 0f, 0f, a);
                            Handles.Label(cell.center - new Vector2(4f, 6f), "~");
                        }

                        // Direct prefab placements (tier)
                        var tp = tier.prefabPlacements?.Find(p => p.cellIndex == index);
                        if (tp != null)
                        {
                            Texture2D tpIcon = tp.prefab != null && prefabIcons.TryGetValue(tp.prefab.name, out var tpi) ? tpi : null;
                            if (tpIcon != null)
                            {
                                GUI.color = new Color(1f, 1f, 1f, a);
                                GUI.DrawTexture(cell, tpIcon, ScaleMode.ScaleToFit);
                                GUI.color = Color.white;
                            }
                            else
                            {
                                Color c = GetPrefabColor(tp.prefab); c.a = a;
                                EditorGUI.DrawRect(cell, c);
                                Handles.color = new Color(0f, 0f, 0f, a);
                                string label = tp.prefab != null ? tp.prefab.name.Substring(0, Mathf.Min(2, tp.prefab.name.Length)) : "?";
                                Handles.Label(cell.center - new Vector2(5f, 6f), label);
                            }
                        }
                    }
                }

                bool mouseOver = cell.Contains(e.mousePosition);

                // ── Select tool handling ──────────────────────────────────
                if (drawSelect && loadedData != null)
                {
                    if (e.type == EventType.MouseDown && mouseOver)
                    {
                        var clicked = FindAnythingAtCell(index);

                        if (clicked.type != SelectionType.None)
                        {
                            // Shift+click another node in same zone → connect directly
                            if (e.shift && clicked.type == SelectionType.SoulZoneNode &&
                                _currentSelection.type == SelectionType.SoulZoneNode &&
                                clicked.index == _currentSelection.index && clicked.subIndex != _currentSelection.subIndex)
                            {
                                ConnectNodes(clicked.index, _currentSelection.subIndex, clicked.subIndex);
                            }
                            else
                            {
                                // Select and immediately arm a drag-to-move for any
                                // movable item. Move only commits once the pointer
                                // is dragged onto a different cell.
                                _currentSelection    = clicked;
                                _selectedZoneIndex   = (clicked.type == SelectionType.SoulZoneNode) ? clicked.index : -1;
                                _selectedNodeIndex   = (clicked.type == SelectionType.SoulZoneNode) ? clicked.subIndex : -1;
                                _activeSoulZoneIndex = (clicked.type == SelectionType.SoulZoneNode) ? clicked.index : _activeSoulZoneIndex;
                                LogSelection(_currentSelection);

                                _isDraggingNode  = true;
                                _dragCurrentCell = index;
                                _dragUndoPushed  = false;
                            }
                        }
                        else
                        {
                            // Click empty cell — clear selection (no second-click move)
                            ClearSelectState();
                        }
                        e.Use();
                        Repaint();
                    }
                    else if (e.type == EventType.MouseDrag && _isDraggingNode && mouseOver && index != _dragCurrentCell
                             && _currentSelection.type != SelectionType.None)
                    {
                        // Push a single undo snapshot for the whole drag, on first move.
                        if (!_dragUndoPushed) { PushUndoSnapshot(); _dragUndoPushed = true; }
                        MoveSelection(_currentSelection, index, pushUndo: false);
                        _dragCurrentCell = index;
                        e.Use();
                        Repaint();
                    }
                    else if (e.type == EventType.MouseUp && _isDraggingNode)
                    {
                        _isDraggingNode  = false;
                        _dragCurrentCell = -1;
                        _dragUndoPushed  = false;
                        e.Use();
                    }
                }
                // ── Paint tool handling ───────────────────────────────────
                else if (!_drawSplineWall)
                {
                if (e.type == EventType.MouseDown && mouseOver)
                {
                    PushUndoSnapshot();
                    isDragging = true;
                    ApplyToolToCell(index);
                    lastDraggedCellIndex = index;
                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.MouseDrag && isDragging && mouseOver)
                {
                    if (index != lastDraggedCellIndex && !drawSoul && !drawSoulArea)
                    {
                        ApplyToolToCell(index);
                        lastDraggedCellIndex = index;
                        Repaint();
                    }
                }
                else if (e.type == EventType.MouseUp)
                {
                    isDragging = false; lastDraggedCellIndex = -1;
                }
                }

                if (_gridLineOpacity > 0f)
                {
                    Color gridLineCol = new Color(0f, 0f, 0f, _gridLineOpacity);
                    EditorGUI.DrawRect(new Rect(cell.x, cell.y, EffCell, 1), gridLineCol);
                    EditorGUI.DrawRect(new Rect(cell.x, cell.y, 1, EffCell), gridLineCol);
                }
            }
        }

        // Grid Overlays (drawn after all cells so lines sit on top)
        if (loadedData != null)
        {
            Handles.BeginGUI();

            // Radius scatter rings
            float pxPerUnit = GetPixelsPerWorldUnit();
            if (pxPerUnit > 0f && loadedData.soulZones != null)
            {
                // Soul fish zone radii
                for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
                {
                    var zone = loadedData.soulZones[zi];
                    if (zone.nodes == null || zone.nodes.Count == 0) continue;
                    bool isActive = zi == _activeSoulZoneIndex;
                    Color rc = ZonePalette[zi % ZonePalette.Length];
                    rc.a = isActive ? 0.55f : 0.18f;
                    Handles.color = rc;
                    float radiusPx = zone.radius * pxPerUnit;
                    foreach (int node in zone.nodes)
                        Handles.DrawWireDisc(CellCenter(rect, node), Vector3.forward, radiusPx, 2f);
                }

                // Whirlpool radii
                if (loadedData.whirlpools != null)
                {
                    foreach (var wp in loadedData.whirlpools)
                    {
                        Handles.color = new Color(0.7f, 0.4f, 1f, 0.55f);
                        float radiusPx = wp.radius * pxPerUnit;
                        Handles.DrawWireDisc(CellCenter(rect, wp.cellIndex), Vector3.forward, radiusPx, 2f);
                    }
                }
            }

            // Prefab scale-radius footprint rings (base + visible tiers)
            if (pxPerUnit > 0f)
            {
                DrawPrefabScaleRings(rect, loadedData.prefabPlacements, -1, pxPerUnit);
                if (loadedData.tiers != null)
                    for (int ti = 0; ti < loadedData.tiers.Count; ti++)
                        if (ti < tierVisible.Count && tierVisible[ti])
                            DrawPrefabScaleRings(rect, loadedData.tiers[ti].prefabPlacements, ti, pxPerUnit);
            }

            if (loadedData.soulZones != null)
            {
                for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
                {
                    var zone = loadedData.soulZones[zi];
                    if (zone.nodes == null || zone.nodes.Count < 2) continue;
                    Color lc = ZonePalette[zi % ZonePalette.Length];
                    lc.a = 0.85f;
                    Handles.color = lc;
                    for (int ni = 0; ni < zone.nodes.Count - 1; ni++)
                    {
                        Vector2 a = CellCenter(rect, zone.nodes[ni]);
                        Vector2 b = CellCenter(rect, zone.nodes[ni + 1]);
                        Handles.DrawLine(a, b, 3f);
                    }
                }
            }

            // Linked Prefab Pairs
            if (loadedData.linkedPairs != null)
            {
                foreach (var pair in loadedData.linkedPairs)
                {
                    bool modOk = CellContainsPrefab(pair.modifierTierIndex, pair.modifierCellIndex, "TypeBWaveModifier");
                    bool tubeOk = CellContainsPrefab(pair.inputTubeTierIndex, pair.inputTubeCellIndex, "SoulFishInputTube");

                    Vector2 a = CellCenter(rect, pair.modifierCellIndex);
                    Vector2 b = CellCenter(rect, pair.inputTubeCellIndex);

                    if (modOk && tubeOk)
                    {
                        Handles.color = new Color(0.4f, 1f, 1f, 0.8f);
                        Handles.DrawLine(a, b, 2.5f);
                        Handles.DrawSolidDisc(b, Vector3.forward, 3.5f);
                    }
                    else
                    {
                        Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
                        Handles.DrawLine(a, b, 1.5f);
                        Handles.Label((a + b) * 0.5f, "BROKEN LINK");
                    }
                }
            }

            // Selection Circle
            if (_currentSelection.type != SelectionType.None)
            {
                Vector2 center;
                if (_currentSelection.type == SelectionType.SplineWallNode
                    && loadedData?.splineWallPaths != null
                    && _currentSelection.index < loadedData.splineWallPaths.Count)
                {
                    var wPath = loadedData.splineWallPaths[_currentSelection.index];
                    center = (wPath.nodes != null && _currentSelection.subIndex < wPath.nodes.Count)
                        ? WorldXZToPixel(rect, wPath.nodes[_currentSelection.subIndex])
                        : rect.center;
                }
                else
                {
                    center = CellCenter(rect, _currentSelection.cellIndex);
                }

                Handles.color = Color.white;
                Handles.DrawWireDisc(center, Vector3.forward, EffCell * 0.55f, 3.5f);

                if (_currentSelection.type == SelectionType.SoulZoneNode)
                {
                    Handles.DrawSolidDisc(center, Vector3.forward, EffCell * 0.2f);
                }
            }

            // Bridge mode lines
            if (_isBridgeMode && _selectedZoneIndex >= 0 && _bridgeNodes.Count >= 1)
            {
                Handles.color = new Color(0.4f, 0.8f, 1f, 0.8f);
                var selZ = loadedData.soulZones[_selectedZoneIndex];
                Vector2 startPt = CellCenter(rect, selZ.nodes[_selectedNodeIndex]);
                var allBridge = new List<int> { selZ.nodes[_selectedNodeIndex] };
                allBridge.AddRange(_bridgeNodes);
                for (int bi = 0; bi < allBridge.Count - 1; bi++)
                    Handles.DrawLine(CellCenter(rect, allBridge[bi]), CellCenter(rect, allBridge[bi + 1]), 1.5f);
            }

            // In-progress drawing lines
            if (_isDrawingSoulArea && _drawingNodes.Count >= 2)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.7f);
                for (int ni = 0; ni < _drawingNodes.Count - 1; ni++)
                {
                    Vector2 a = CellCenter(rect, _drawingNodes[ni]);
                    Vector2 b = CellCenter(rect, _drawingNodes[ni + 1]);
                    Handles.DrawLine(a, b, 1.5f);
                }
            }

            // Entrance + lock hub markers on the arena circumference
            DrawEntranceOverlay(rect);

            // Tube path overlays — one colour per entrance, active entrance highlighted
            DrawTubePathOverlay(rect);

            // Spline wall overlay — drawn on top of all other overlays
            DrawSplineWallOverlay(rect);

            Handles.EndGUI();
        }

        // Select tool — Escape to deselect, Delete to remove selected node
        if (drawSelect)
        {
            Event sk = Event.current;
            if (sk.type == EventType.KeyDown)
            {
                if (sk.keyCode == KeyCode.Escape)
                {
                    ClearSelectState();
                    sk.Use();
                    Repaint();
                }
                else if (sk.keyCode == KeyCode.Delete || sk.keyCode == KeyCode.Backspace)
                {
                    if (_currentSelection.type != SelectionType.None)
                    {
                        Undo.RecordObject(loadedData, "Delete Selection");
                        PushUndoSnapshot();

                        switch (_currentSelection.type)
                        {
                            case SelectionType.SoulZoneNode:
                                var zone = loadedData.soulZones[_currentSelection.index];
                                zone.nodes.RemoveAt(_currentSelection.subIndex);
                                break;
                            case SelectionType.PrefabPlacement:
                                var placements = _currentSelection.tierIndex == -1 ? loadedData.prefabPlacements : loadedData.tiers[_currentSelection.tierIndex].prefabPlacements;
                                placements.RemoveAt(_currentSelection.index);
                                break;
                            case SelectionType.Whirlpool:
                                loadedData.whirlpools.RemoveAt(_currentSelection.index);
                                break;
                            case SelectionType.Orb:
                                loadedData.orbCellIndices.Remove(_currentSelection.cellIndex);
                                break;
                            case SelectionType.WaterModifier:
                                var waterMods = _currentSelection.tierIndex == -1 ? loadedData.waterLevelModifierCellIndices : loadedData.tiers[_currentSelection.tierIndex].waterLevelModifierCellIndices;
                                waterMods.Remove(_currentSelection.cellIndex);
                                break;
                            case SelectionType.WaveModifier:
                                var waveMods = _currentSelection.tierIndex == -1 ? loadedData.waveModifierCellIndices : loadedData.tiers[_currentSelection.tierIndex].waveModifierCellIndices;
                                waveMods.Remove(_currentSelection.cellIndex);
                                break;
                            case SelectionType.GridSlot:
                                if (_currentSelection.tierIndex >= 0) loadedData.tiers[_currentSelection.tierIndex].cells[_currentSelection.cellIndex] = 0;
                                else if (_currentSelection.isCircle) circleGrid[_currentSelection.cellIndex] = 0;
                                else squareGrid[_currentSelection.cellIndex] = 0;
                                break;
                            case SelectionType.SplineWallNode:
                                if (loadedData.splineWallPaths != null
                                    && _currentSelection.index < loadedData.splineWallPaths.Count)
                                {
                                    var wp = loadedData.splineWallPaths[_currentSelection.index];
                                    int delIdx = _currentSelection.subIndex;
                                    if (wp.nodes != null && delIdx < wp.nodes.Count)
                                    {
                                        wp.nodes.RemoveAt(delIdx);
                                        if (wp.segmentCurved != null && delIdx < wp.segmentCurved.Count)
                                            wp.segmentCurved.RemoveAt(delIdx);
                                    }
                                }
                                break;
                        }

                        ClearSelectState();
                        EditorUtility.SetDirty(loadedData);
                        sk.Use();
                        Repaint();
                    }
                }
            }
        }

        // Tube modes — consume Enter/Escape before GUI sees them (prevents toggling Locked checkbox)
        if (loadedData != null && (_tubePlacingEntranceIndex >= 0 || _tubeDrawEntranceIndex >= 0))
        {
            Event te = Event.current;
            if (te.type == EventType.KeyDown &&
                (te.keyCode == KeyCode.Return || te.keyCode == KeyCode.KeypadEnter || te.keyCode == KeyCode.Escape))
            {
                _tubePlacingEntranceIndex = -1;
                _tubeDrawEntranceIndex    = -1;
                _dragTubeNodeIndex        = -1;
                _selectedTubeNodeIndex    = -1;
                te.Use();
                Repaint();
            }
        }

        // Soul area draw mode — Enter to commit, Escape to cancel
        if (_isDrawingSoulArea && loadedData != null)
        {
            Event ke = Event.current;
            if (ke.type == EventType.KeyDown)
            {
                if (ke.keyCode == KeyCode.Return || ke.keyCode == KeyCode.KeypadEnter)
                {
                    if (_activeSoulZoneIndex >= 0 && _activeSoulZoneIndex < loadedData.soulZones.Count)
                        CommitDrawingNodes(loadedData.soulZones[_activeSoulZoneIndex]);
                    ke.Use();
                    Repaint();
                }
                else if (ke.keyCode == KeyCode.Escape)
                {
                    CancelDrawingNodes();
                    ke.Use();
                    Repaint();
                }
            }
        }

        // Portal perimeter overlay
        if (loadedData != null)
            DrawPortalOverlay(rect);

        // Spline wall mode — Escape to exit mode, Delete/Backspace to remove last node on active path
        if (_drawSplineWall && loadedData != null)
        {
            Event sw = Event.current;
            if (sw.type == EventType.KeyDown)
            {
                if (sw.keyCode == KeyCode.Escape)
                {
                    _drawSplineWall = false;
                    sw.Use();
                    Repaint();
                }
                else if ((sw.keyCode == KeyCode.Delete || sw.keyCode == KeyCode.Backspace)
                         && loadedData.splineWallPaths != null
                         && _activeSplinePathIdx < loadedData.splineWallPaths.Count)
                {
                    var activePath = loadedData.splineWallPaths[_activeSplinePathIdx];
                    if (activePath.nodes != null && activePath.nodes.Count > 0)
                    {
                        Undo.RecordObject(loadedData, "Delete Spline Wall Node");
                        int lastIdx = activePath.nodes.Count - 1;
                        activePath.nodes.RemoveAt(lastIdx);
                        if (activePath.segmentCurved != null && lastIdx < activePath.segmentCurved.Count)
                            activePath.segmentCurved.RemoveAt(lastIdx);
                        EditorUtility.SetDirty(loadedData);
                        sw.Use();
                        Repaint();
                    }
                }
            }
        }

        // Tube placement - Escape to cancel
        if (_isWaitingForTubePlacement)
        {
            Event ke = Event.current;
            if (ke.type == EventType.KeyDown && ke.keyCode == KeyCode.Escape)
            {
                _isWaitingForTubePlacement = false;
                _pendingModifierCellIndex = -1;
                GridLog("Cancelled tube placement.");
                ke.Use();
                Repaint();
            }
        }
    }

    bool CellContainsPrefab(int tierIndex, int cellIndex, string prefabName)
    {
        if (loadedData == null) return false;
        List<GridData.PrefabPlacement> placements;
        if (tierIndex == -1) placements = loadedData.prefabPlacements;
        else if (loadedData.tiers != null && tierIndex >= 0 && tierIndex < loadedData.tiers.Count) 
            placements = loadedData.tiers[tierIndex].prefabPlacements;
        else return false;

        if (placements == null) return false;
        return placements.Exists(p => p.cellIndex == cellIndex && p.prefab != null && p.prefab.name == prefabName);
    }

    Vector2 CellCenter(Rect gridRect, int cellIndex)
    {
        int x = cellIndex % GridSize;
        int y = cellIndex / GridSize;
        return new Vector2(gridRect.x + x * EffCell + EffCell * 0.5f,
                           gridRect.y + y * EffCell + EffCell * 0.5f);
    }

    // Derives how many grid pixels equal one world unit, using the arena profile's
    // reference plane prefab (or baseline radius) as the authority on real-world arena dimensions.
    float GetPixelsPerWorldUnit()
    {
        var profile = loadedData?.arenaProfile;
        if (profile == null) return -1f;

        float worldWidth = profile.WorldArenaWidth;

        if (worldWidth <= 0f) return -1f;

        return (EffCell * GridSize) / worldWidth;
    }

    // Draws a world-proportional footprint ring for every placement whose prefab has
    // a PrefabBaselineAlignment scale radius enabled. The ring grows with the
    // placement's stored scale so the designer preview matches the spawned size.
    void DrawPrefabScaleRings(Rect rect, List<GridData.PrefabPlacement> placements, int tierIndex, float pxPerUnit)
    {
        if (placements == null) return;
        bool activeLayer = tierIndex == activeTierIndex;
        foreach (var pp in placements)
        {
            if (pp?.prefab == null) continue;
            var align = GetBaselineAlign(pp.prefab);
            if (align == null || !align.UseScaleRadius) continue;

            float s        = pp.scale > 0f ? pp.scale : 1f;
            float radiusPx = align.ScaleRadius * s * pxPerUnit;
            if (radiusPx <= 0f) continue;

            bool isSelected = _currentSelection.type == SelectionType.PrefabPlacement
                           && _currentSelection.tierIndex == tierIndex
                           && _currentSelection.cellIndex == pp.cellIndex;

            Color c = new Color(1f, 0.55f, 0.1f, activeLayer ? 0.75f : 0.2f);
            Handles.color = c;
            Handles.DrawWireDisc(CellCenter(rect, pp.cellIndex), Vector3.forward, radiusPx, isSelected ? 3.5f : 2f);
        }
    }

    void DrawPortalOverlay(Rect gridRect)
{
        float ringRadius   = ZoomedGridSize * 0.5f + 14f;
        const float arrowLen     = 10f;
        const float dotRadius    = 5f;
        Vector2     centre       = gridRect.center;

        float pxPerUnit = GetPixelsPerWorldUnit();

        Handles.BeginGUI();

        if (loadedData.entrances != null)
        {
            foreach (var ent in loadedData.entrances)
            {
                // angle: 0° = up (+Z = up in 2D), clockwise
                float rad = ent.perimeterAngle * Mathf.Deg2Rad;
                Vector2 dir    = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
                Vector2 tip    = centre + dir * ringRadius;
                Vector2 inward = centre + dir * (ringRadius - arrowLen);

                // Green dot + inward arrow
                Handles.color = Color.green;
                Handles.DrawSolidDisc(tip, Vector3.forward, dotRadius);
                Handles.DrawLine(tip, inward);

                // Arrowhead
                Vector2 perp  = new Vector2(-dir.y, dir.x);
                Vector2 ahead = inward - dir * (arrowLen * 0.4f);
                Handles.DrawLine(inward, ahead + perp * 4f);
                Handles.DrawLine(inward, ahead - perp * 4f);

                // Label
                Handles.color = Color.white;
                Handles.Label(tip + dir * 4f + new Vector2(0f, -7f), ent.id);
            }
        }

        Handles.EndGUI();
    }

    void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
        if (GUILayout.Button("CLEAR ALL") && EditorUtility.DisplayDialog(
            "Clear All",
            "This will erase all cells, orbs, souls, modifiers, whirlpools, and prefab placements on every layer. This can be undone.\n\nAre you sure?",
            "Clear All", "Cancel"))
        {
            PushUndoSnapshot();
            System.Array.Clear(squareGrid, 0, CellCount);
            System.Array.Clear(circleGrid, 0, CellCount);
            if (loadedData != null)
            {
                loadedData.orbCellIndices?.Clear();
                loadedData.soulSpawnPoints?.Clear();
                loadedData.soulZones?.Clear();
                loadedData.waterLevelModifierCellIndices?.Clear();
                loadedData.waveModifierCellIndices?.Clear();
                loadedData.whirlpools?.Clear();
                loadedData.prefabPlacements?.Clear();
                if (loadedData.tiers != null)
                    foreach (var tier in loadedData.tiers)
                    {
                        if (tier.cells != null) System.Array.Clear(tier.cells, 0, tier.cells.Length);
                        tier.waterLevelModifierCellIndices?.Clear();
                        tier.waveModifierCellIndices?.Clear();
                        tier.prefabPlacements?.Clear();
                    }
            }
            _activeSoulZoneIndex = -1;
            _isDrawingSoulArea   = false;
            _drawingNodes.Clear();
            RebuildSlotsFromGrid();
            EditorUtility.SetDirty(loadedData);
            Repaint();
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("CLEAR ORBS") && loadedData != null)
        { PushUndoSnapshot(); loadedData.orbCellIndices?.Clear(); Repaint(); }

        if (GUILayout.Button("CLEAR SOULS") && loadedData != null)
        { PushUndoSnapshot(); loadedData.soulSpawnPoints?.Clear(); loadedData.soulZones?.Clear(); _activeSoulZoneIndex = -1; Repaint(); }

        if (GUILayout.Button("UNDO")) UndoLastAction();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("NEW"))     CreateNewGrid();
        if (GUILayout.Button("SAVE"))    SaveGridInPlace();
        if (GUILayout.Button("SAVE AS")) SaveGrid();
        if (GUILayout.Button("LOAD"))    LoadGrid();
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginDisabledGroup(loadedData == null);
        GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
        if (GUILayout.Button("Test Level"))
            GameTesterTool.LaunchScene("Waves1", loadedData, null);
        if (GUILayout.Button("Test Level (Fresh Save)"))
        {
            GameProgressData.ClearUnlocks();
            GameTesterTool.LaunchScene("Waves1", loadedData, null);
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();
    }

    void CreateNewGrid()
    {
        if (loadedData != null || GetMaxSlotUsed() > 0)
        {
            if (!EditorUtility.DisplayDialog("New Grid", "Discard current grid and start fresh?", "Yes", "No"))
                return;
        }

        string path = EditorUtility.SaveFilePanelInProject("Create New Grid", "NewGridData", "asset", "Select location for the new GridData asset.", GridDataFolder);
        if (string.IsNullOrEmpty(path)) return;

        GridData data = ScriptableObject.CreateInstance<GridData>();
        
        // Initialize with default empty state
        data.cells        = new int[GridData.CellCount];
        data.overlayCells = new int[GridData.CellCount];
        data.slotNotes    = new List<string> { string.Empty };
        data.slotColors   = new List<Color>();
        data.levelID      = "new_level";
        data.displayName  = "New Level";

        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();

        GridLog($"Created and loaded new grid asset at {path}");
        LoadGrid(data);
        RefreshDiscoveredGrids();
    }

    void SaveGrid()
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Grid", "GridData", "asset", "", GridDataFolder);
        if (string.IsNullOrEmpty(path)) return;

        GridData data       = ScriptableObject.CreateInstance<GridData>();
        data.cells          = (int[])squareGrid.Clone();
        data.overlayCells   = (int[])circleGrid.Clone();
        data.slotNotes      = new List<string>(slotNotes);
        data.slotColors     = new List<Color>(slotColors);
        data.orbCellIndices = loadedData?.orbCellIndices != null
            ? new List<int>(loadedData.orbCellIndices) : new List<int>();
        data.arenaProfile = loadedData?.arenaProfile;

        data.entrances = new List<GridData.ArenaEntrance>();
        if (loadedData?.entrances != null)
            foreach (var e in loadedData.entrances)
                data.entrances.Add(new GridData.ArenaEntrance
                {
                    id = e.id, prefab = e.prefab, soulPrefab = e.soulPrefab,
                    perimeterAngle = e.perimeterAngle, tierSlot = e.tierSlot,
                    spawnRadius = e.spawnRadius,
                    isLocked = e.isLocked, lockHubPrefab = e.lockHubPrefab,
                    lockHubAngle = e.lockHubAngle,
                    tubeSubdivisions = e.tubeSubdivisions,
                    tubePath = e.tubePath != null
                        ? new List<UnityEngine.Vector2Int>(e.tubePath)
                        : new List<UnityEngine.Vector2Int>()
                });

        data.soulSpawnPoints = new List<GridData.SoulSpawnPoint>();
        if (loadedData?.soulSpawnPoints != null)
            foreach (var s in loadedData.soulSpawnPoints)
                data.soulSpawnPoints.Add(new GridData.SoulSpawnPoint { cellIndex = s.cellIndex, soulData = s.soulData });

        data.prefabPlacements = loadedData?.prefabPlacements != null
            ? new List<GridData.PrefabPlacement>(loadedData.prefabPlacements)
            : new List<GridData.PrefabPlacement>();

        data.tiers = new List<GridData.GridTier>();
        if (loadedData?.tiers != null)
            foreach (var t in loadedData.tiers)
                data.tiers.Add(new GridData.GridTier
                {
                    name             = t.name,
                    yOffset          = t.yOffset,
                    cells            = t.cells != null ? (int[])t.cells.Clone() : new int[GridData.CellCount],
                    prefabPlacements = t.prefabPlacements != null
                        ? new List<GridData.PrefabPlacement>(t.prefabPlacements)
                        : new List<GridData.PrefabPlacement>()
                });

        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        RefreshDiscoveredGrids();
    }

    void SaveGridInPlace()
    {
        if (loadedData == null) { Debug.LogWarning("[GridDesigner] No grid loaded. Use Save As."); return; }
        Undo.RecordObject(loadedData, "Save Grid");
        loadedData.cells        = (int[])squareGrid.Clone();
        loadedData.overlayCells = (int[])circleGrid.Clone();
        loadedData.slotNotes    = new List<string>(slotNotes);
        loadedData.slotColors   = new List<Color>(slotColors);
        EditorUtility.SetDirty(loadedData);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GridDesigner] Saved to {AssetDatabase.GetAssetPath(loadedData)}");
    }

    void LoadGrid()
    {
        string path = EditorUtility.OpenFilePanel("Load Grid", GridDataFolder, "asset");
        if (string.IsNullOrEmpty(path)) return;
        path = FileUtil.GetProjectRelativePath(path);
        GridData data = AssetDatabase.LoadAssetAtPath<GridData>(path);
        if (!data) return;
        LoadGrid(data);
    }

    void LoadGrid(GridData data)
    {
        loadedData = data;
        if (loadedData.orbCellIndices  == null) loadedData.orbCellIndices  = new List<int>();
        if (loadedData.soulSpawnPoints == null) loadedData.soulSpawnPoints = new List<GridData.SoulSpawnPoint>();
        if (loadedData.soulZones       == null) loadedData.soulZones       = new List<GridData.SoulZone>();
        _allSoulData = null; // force re-scan on next draw
        _activeSoulZoneIndex = -1;
        _isDrawingSoulArea   = false;
        _drawingNodes.Clear();
        EnsureSoulZones(); // run legacy migration if needed

        squareGrid = (int[])loadedData.cells.Clone();
        circleGrid = loadedData.overlayCells != null
            ? (int[])loadedData.overlayCells.Clone() : new int[CellCount];

        RebuildSlotsFromGrid();

        if (loadedData.slotNotes != null)
            for (int i = 1; i < Mathf.Min(slotNotes.Count, loadedData.slotNotes.Count); i++)
                slotNotes[i] = loadedData.slotNotes[i];

        if (loadedData.slotColors != null)
            for (int i = 0; i < Mathf.Min(slotColors.Count, loadedData.slotColors.Count); i++)
                slotColors[i] = loadedData.slotColors[i];

        // Normalise legacy placements: the scale field did not exist previously,
        // so deserialisation leaves it at 0. Treat that as the default of 1.
        NormalizePlacementScales(loadedData.prefabPlacements);
        if (loadedData.tiers != null)
            foreach (var tier in loadedData.tiers)
                NormalizePlacementScales(tier.prefabPlacements);

        _baselineAlignCache.Clear();

        activeSlot = 0; drawCircle = drawSoul = drawSoulArea = false;
        _isDrawingSoulArea = false; _drawingNodes.Clear();
    }

    static void NormalizePlacementScales(List<GridData.PrefabPlacement> placements)
    {
        if (placements == null) return;
        foreach (var p in placements)
            if (p != null && p.scale <= 0f) p.scale = 1f;
    }

    void RefreshDiscoveredGrids()
    {
        discoveredGrids.Clear();
        string[] guids = AssetDatabase.FindAssets("t:GridData", new[] { GridDataFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GridData d  = AssetDatabase.LoadAssetAtPath<GridData>(path);
            if (d != null) discoveredGrids.Add(d);
        }
        discoveredGridNames = new string[discoveredGrids.Count];
        for (int i = 0; i < discoveredGrids.Count; i++)
        {
            var d = discoveredGrids[i];
            discoveredGridNames[i] = string.IsNullOrEmpty(d.displayName) ? d.name : d.displayName;
        }
        selectedDiscoveredGridIndex = Mathf.Clamp(selectedDiscoveredGridIndex, 0,
            Mathf.Max(0, discoveredGrids.Count - 1));
    }

    int GetMaxSlotUsed()
    {
        int max = 0;
        for (int i = 0; i < CellCount; i++)
        {
            max = Mathf.Max(max, squareGrid[i]);
            max = Mathf.Max(max, circleGrid[i]);
        }
        return max;
    }

    void RebuildSlotsFromGrid()
    {
        int required = GetMaxSlotUsed();
        slotColors.Clear(); slotNotes.Clear(); slotNotes.Add(string.Empty);
        for (int i = 0; i < required; i++)
        {
            slotColors.Add(Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.6f, 1f));
            slotNotes.Add(string.Empty);
        }
    }

    void EnsureSlotCapacity(int required)
    {
        while (slotColors.Count < required)
            slotColors.Add(Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.6f, 1f));
        while (slotNotes.Count < required + 1)
            slotNotes.Add(string.Empty);
    }

    // ─────────────────────────────────────────────
    // SLOT REMOVAL
    // ─────────────────────────────────────────────

    /// <summary>
    /// Returns true if any cell in either grid is currently using this slot number.
    /// Prevents removal of slots that are still painted on the grid.
    /// </summary>
    bool IsSlotInUse(int slot)
    {
        for (int i = 0; i < CellCount; i++)
            if (squareGrid[i] == slot || circleGrid[i] == slot)
                return true;
        return false;
    }

    /// <summary>
    /// Removes a slot and remaps all higher slot numbers down by one
    /// so the grid data stays consistent.
    /// </summary>
    void RemoveSlot(int slot)
    {
        PushUndoSnapshot();

        // Remove from lists (slot is 1-based, lists are 0-based)
        slotColors.RemoveAt(slot - 1);
        slotNotes.RemoveAt(slot);   // slotNotes has a leading empty entry at index 0

        // Remap grid values: anything above the removed slot shifts down by 1
        for (int i = 0; i < CellCount; i++)
        {
            if (squareGrid[i] > slot) squareGrid[i]--;
            if (circleGrid[i] > slot) circleGrid[i]--;
        }

        // If the active slot was the removed one, reset to eraser
        if (activeSlot == slot)
            activeSlot = 0;
        else if (activeSlot > slot)
            activeSlot--;

        Repaint();
    }

    // ─────────────────────────────────────────────────────────────
    // SPLINE WALL EDITOR
    // ─────────────────────────────────────────────────────────────

    void DrawSplineWallsSection()
    {
        if (loadedData == null) return;

        _showSplineWalls = EditorGUILayout.Foldout(_showSplineWalls, "Spline Walls", true, EditorStyles.foldoutHeader);
        if (!_showSplineWalls) return;

        if (loadedData.splineWallPaths == null) loadedData.splineWallPaths = new List<GridData.SplineWallPath>();

        int toRemove = -1;
        for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
        {
            var  path     = loadedData.splineWallPaths[pi];
            bool selectedNodeOnThisPath = _currentSelection.type == SelectionType.SplineWallNode
                                       && _currentSelection.index == pi;
            bool isActive = ((_drawSplineWall && pi == _activeSplinePathIdx) || selectedNodeOnThisPath);

            // Row 1: name + delete
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = isActive ? GetSplineWallColor(pi) : Color.white;
            if (GUILayout.Button($"SplineWall{pi + 1}"))
            {
                _activeSplinePathIdx = pi;
                _drawSplineWall      = true;
            }
            GUI.backgroundColor = Color.white;
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(28))) toRemove = pi;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Row 2: toggles + spacing
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool  newClosed  = GUILayout.Toggle(path.isClosed, "Loop",  GUILayout.Width(46));
            GUILayout.Label(new GUIContent("Spacing", "Tile spacing — distance between each wall piece along the path"), GUILayout.Width(52));
            float newSpacing = EditorGUILayout.FloatField(path.tileSpacing);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Spline Wall Path");
                path.isClosed    = newClosed;
                path.tileSpacing = Mathf.Max(0.05f, newSpacing);
                EditorUtility.SetDirty(loadedData);
            }
            EditorGUILayout.EndHorizontal();

            if (isActive)
            {
                EditorGUI.indentLevel++;

                // Prefab
                EditorGUI.BeginChangeCheck();
                var newPrefab = (GameObject)EditorGUILayout.ObjectField(
                    "Prefab", path.prefabOverride, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(loadedData, "Set Spline Wall Prefab");
                    path.prefabOverride = newPrefab;
                    EditorUtility.SetDirty(loadedData);
                }

                // Node list
                int nodeCount = path.nodes?.Count ?? 0;
                EditorGUILayout.LabelField($"Nodes  ({nodeCount})", EditorStyles.miniBoldLabel);
                if (path.nodes != null)
                {
                    int insertAfter = -1;

                    for (int ni = 0; ni < path.nodes.Count; ni++)
                    {
                        EditorGUILayout.BeginHorizontal();

                        bool isSelectedNode = _currentSelection.type == SelectionType.SplineWallNode
                                           && _currentSelection.index == pi
                                           && _currentSelection.subIndex == ni;

                        GUI.backgroundColor = isSelectedNode ? new Color(1f, 0.7f, 0.2f) : Color.white;
                        if (GUILayout.Button($"{ni}", GUILayout.Width(22)))
                        {
                            _currentSelection = new SelectionInfo
                            {
                                type      = SelectionType.SplineWallNode,
                                index     = pi,
                                subIndex  = ni,
                                cellIndex = -1,
                            };
                            Repaint();
                        }
                        GUI.backgroundColor = Color.white;

                        EditorGUI.BeginChangeCheck();
                        Vector2 node = path.nodes[ni];
                        float   newX = EditorGUILayout.FloatField(node.x, GUILayout.Width(52));
                        float   newZ = EditorGUILayout.FloatField(node.y, GUILayout.Width(52));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(loadedData, "Edit Spline Wall Node");
                            path.nodes[ni] = new Vector2(newX, newZ);
                            EditorUtility.SetDirty(loadedData);
                        }

                        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                        if (GUILayout.Button("✕", GUILayout.Width(20)))
                        {
                            Undo.RecordObject(loadedData, "Delete Spline Wall Node");
                            path.nodes.RemoveAt(ni);
                            if (path.segmentCurved != null && ni < path.segmentCurved.Count)
                                path.segmentCurved.RemoveAt(ni);
                            EditorUtility.SetDirty(loadedData);
                            EditorGUILayout.EndHorizontal();
                            break;
                        }
                        GUI.backgroundColor = Color.white;

                        EditorGUILayout.EndHorizontal();

                        // "+" insert and curve toggle between this node and the next
                        bool hasNext = ni < path.nodes.Count - 1;
                        bool isLoop  = path.isClosed && ni == path.nodes.Count - 1;
                        if (hasNext || isLoop)
                        {
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Space(26);
                            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
                            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(22)))
                                insertAfter = ni;
                            GUI.backgroundColor = Color.white;

                            // Per-segment curve toggle
                            if (path.segmentCurved == null) path.segmentCurved = new List<bool>();
                            while (path.segmentCurved.Count <= ni) path.segmentCurved.Add(true);
                            bool segCurved    = path.segmentCurved[ni];
                            bool newSegCurved = GUILayout.Toggle(segCurved, new GUIContent(segCurved ? "~" : "—", segCurved ? "Curved segment" : "Straight segment"), EditorStyles.miniButton, GUILayout.Width(22));
                            if (newSegCurved != segCurved)
                            {
                                Undo.RecordObject(loadedData, "Toggle Segment Curve");
                                path.segmentCurved[ni] = newSegCurved;
                                EditorUtility.SetDirty(loadedData);
                            }

                            EditorGUILayout.EndHorizontal();
                        }
                    }

                    if (insertAfter >= 0)
                    {
                        Undo.RecordObject(loadedData, "Insert Spline Wall Node");
                        int     nextIdx  = (insertAfter + 1) % path.nodes.Count;
                        Vector2 midpoint = (path.nodes[insertAfter] + path.nodes[nextIdx]) * 0.5f;
                        path.nodes.Insert(insertAfter + 1, midpoint);
                        if (path.segmentCurved == null) path.segmentCurved = new List<bool>();
                        while (path.segmentCurved.Count <= insertAfter) path.segmentCurved.Add(true);
                        bool inheritedCurve = path.segmentCurved[insertAfter];
                        path.segmentCurved.Insert(insertAfter + 1, inheritedCurve);
                        EditorUtility.SetDirty(loadedData);
                    }
                }

                EditorGUI.indentLevel--;
            }
        }

        if (toRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Remove Spline Wall Path");
            loadedData.splineWallPaths.RemoveAt(toRemove);
            _activeSplinePathIdx = Mathf.Clamp(_activeSplinePathIdx, 0, Mathf.Max(0, loadedData.splineWallPaths.Count - 1));
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("+ Add Spline Wall Path"))
        {
            Undo.RecordObject(loadedData, "Add Spline Wall Path");
            loadedData.splineWallPaths.Add(new GridData.SplineWallPath
            {
                prefabOverride = GetDefaultSplineWallPrefab()
            });
            _activeSplinePathIdx = loadedData.splineWallPaths.Count - 1;
            _drawSplineWall      = true;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void HandleSelectSplineWallInput(Rect rect, Event e)
    {
        if (loadedData?.splineWallPaths == null || loadedData.splineWallPaths.Count == 0) return;

        const float pickRadius = 9f;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            // Check proximity to any spline wall node
            int   hitPath = -1, hitNode = -1;
            float best    = pickRadius;
            for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
            {
                var path = loadedData.splineWallPaths[pi];
                if (path.nodes == null) continue;
                for (int ni = 0; ni < path.nodes.Count; ni++)
                {
                    float d = Vector2.Distance(WorldXZToPixel(rect, path.nodes[ni]), e.mousePosition);
                    if (d < best) { best = d; hitPath = pi; hitNode = ni; }
                }
            }

            if (hitPath >= 0)
            {
                _currentSelection = new SelectionInfo
                {
                    type     = SelectionType.SplineWallNode,
                    index    = hitPath,
                    subIndex = hitNode,
                    cellIndex = -1,
                };
                _isDraggingNode = true;
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0
                 && _isDraggingNode && _currentSelection.type == SelectionType.SplineWallNode)
        {
            int pi = _currentSelection.index;
            int ni = _currentSelection.subIndex;
            if (loadedData.splineWallPaths != null && pi < loadedData.splineWallPaths.Count)
            {
                var path = loadedData.splineWallPaths[pi];
                if (path.nodes != null && ni < path.nodes.Count)
                {
                    Undo.RecordObject(loadedData, "Move Spline Wall Node");
                    path.nodes[ni] = PixelToWorldXZ(rect, e.mousePosition);
                    EditorUtility.SetDirty(loadedData);
                }
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && e.button == 0
                 && _currentSelection.type == SelectionType.SplineWallNode)
        {
            _isDraggingNode = false;
            e.Use();
        }
    }

    // Converts a perimeter angle (degrees, 0=forward/+Z, clockwise) to a pixel position on the canvas circumference
    Vector2 AngleToCircumferencePixel(Rect gridRect, float angleDeg, float radiusScale = 1f)
    {
        float rad    = angleDeg * Mathf.Deg2Rad;
        float radius = ZoomedGridSize * 0.5f * radiusScale;
        return new Vector2(gridRect.center.x + Mathf.Sin(rad) * radius,
                           gridRect.center.y - Mathf.Cos(rad) * radius);
    }

    void DrawEntranceOverlay(Rect rect)
    {
        if (loadedData?.entrances == null) return;

        foreach (var ent in loadedData.entrances)
        {
            if (!ent.isLocked) continue;

            Vector2 lockPt = AngleToCircumferencePixel(rect, ent.lockHubAngle, 1f);
            Handles.color = Color.black;
            Handles.DrawSolidDisc(lockPt, Vector3.forward, 6f);
            Handles.color = new Color(1f, 0.85f, 0.1f);
            Handles.DrawSolidDisc(lockPt, Vector3.forward, 3.5f);
            Handles.Label(lockPt + new Vector2(5f, -8f), "[L]");
        }
    }

    // Returns the pixel-center of a grid cell
    Vector2 CellToGridPixel(Rect rect, int cx, int cy) =>
        new Vector2(rect.x + cx * EffCell + EffCell * 0.5f,
                    rect.y + cy * EffCell + EffCell * 0.5f);

    // Converts an angle+radius on the arena circumference to the nearest clamped grid cell
    UnityEngine.Vector2Int HubAngleToGridCell(Rect rect, float angleDeg)
    {
        Vector2 px = AngleToCircumferencePixel(rect, angleDeg, 1f);
        int cx = Mathf.Clamp(Mathf.FloorToInt((px.x - rect.x) / EffCell), 0, GridSize - 1);
        int cy = Mathf.Clamp(Mathf.FloorToInt((px.y - rect.y) / EffCell), 0, GridSize - 1);
        return new UnityEngine.Vector2Int(cx, cy);
    }

    void HandleTubePlacementInput(Rect rect, Event e)
    {
        // Repaint every frame so the preview follows the mouse
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            Repaint();

        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            int mx = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.x - rect.x) / EffCell), 0, GridSize - 1);
            int my = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.y - rect.y) / EffCell), 0, GridSize - 1);

            var ent = loadedData.entrances[_tubePlacingEntranceIndex];
            var tubeCell = new UnityEngine.Vector2Int(mx, my);
            var hubCell  = HubAngleToGridCell(rect, ent.lockHubAngle);

            Undo.RecordObject(loadedData, "Place Input Tube");
            ent.tubePath = new List<UnityEngine.Vector2Int>();

            // Path: input tube → intermediate nodes → hub
            ent.tubePath.Add(tubeCell);
            int total = ent.tubeSubdivisions + 2;
            for (int si = 1; si < total - 1; si++)
            {
                float t  = (float)si / (total - 1);
                int   cx = Mathf.RoundToInt(Mathf.Lerp(tubeCell.x, hubCell.x, t));
                int   cy = Mathf.RoundToInt(Mathf.Lerp(tubeCell.y, hubCell.y, t));
                ent.tubePath.Add(new UnityEngine.Vector2Int(cx, cy));
            }
            ent.tubePath.Add(hubCell);

            _tubePlacingEntranceIndex = -1;
            _selectedTubeNodeIndex    = -1;
            EditorUtility.SetDirty(loadedData);
            e.Use();
            Repaint();
        }
    }

    void GenerateTubePath(int entranceIndex)
    {
        var ent = loadedData.entrances[entranceIndex];
        if (ent.tubePath == null || ent.tubePath.Count < 2) return;

        Undo.RecordObject(loadedData, "Generate Tube Path");
        var first = ent.tubePath[0];
        var last  = ent.tubePath[ent.tubePath.Count - 1];

        ent.tubePath.Clear();
        ent.tubePath.Add(first);

        int total = ent.tubeSubdivisions + 2; // first + subdivisions + last
        for (int si = 1; si < total - 1; si++)
        {
            float t  = (float)si / (total - 1);
            int   cx = Mathf.RoundToInt(Mathf.Lerp(first.x, last.x, t));
            int   cy = Mathf.RoundToInt(Mathf.Lerp(first.y, last.y, t));
            ent.tubePath.Add(new UnityEngine.Vector2Int(cx, cy));
        }

        ent.tubePath.Add(last);
        _selectedTubeNodeIndex = -1;
        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    void HandleTubePathInput(Rect rect, Event e)
    {
        if (!rect.Contains(e.mousePosition)) return;

        int x = Mathf.FloorToInt((e.mousePosition.x - rect.x) / EffCell);
        int y = Mathf.FloorToInt((e.mousePosition.y - rect.y) / EffCell);
        if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) return;

        var entrance = loadedData.entrances[_tubeDrawEntranceIndex];
        if (entrance.tubePath == null) entrance.tubePath = new List<UnityEngine.Vector2Int>();

        var cell = new UnityEngine.Vector2Int(x, y);

        int lockedIdx = entrance.tubePath.Count - 1; // last node is locked to hub position

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            int hitNode = entrance.tubePath.IndexOf(cell);
            if (hitNode >= 0 && hitNode != lockedIdx)
            {
                _dragTubeNodeIndex     = hitNode;
                _selectedTubeNodeIndex = hitNode;
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _dragTubeNodeIndex >= 0)
        {
            if (_dragTubeNodeIndex < entrance.tubePath.Count && entrance.tubePath[_dragTubeNodeIndex] != cell)
            {
                Undo.RecordObject(loadedData, "Move Tube Waypoint");
                entrance.tubePath[_dragTubeNodeIndex] = cell;
                _selectedTubeNodeIndex = _dragTubeNodeIndex;
                EditorUtility.SetDirty(loadedData);
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            _dragTubeNodeIndex = -1;
            e.Use();
        }
    }

    void DrawTubePathOverlay(Rect rect)
    {
        if (loadedData?.entrances == null) return;

        Color[] tubePalette =
        {
            new Color(1f,   0.5f, 0f,   1f),
            new Color(0.2f, 0.8f, 1f,   1f),
            new Color(0.8f, 0.3f, 1f,   1f),
            new Color(0.3f, 1f,   0.4f, 1f),
        };

        // ── Placement preview ──────────────────────────────────────────
        if (_tubePlacingEntranceIndex >= 0 && _tubePlacingEntranceIndex < loadedData.entrances.Count
            && rect.Contains(Event.current.mousePosition))
        {
            var  ent  = loadedData.entrances[_tubePlacingEntranceIndex];
            Color pc  = tubePalette[_tubePlacingEntranceIndex % tubePalette.Length];

            // Hub position on canvas
            Vector2 hubPx = AngleToCircumferencePixel(rect, ent.lockHubAngle, 1f);

            // Mouse position snapped to cell center
            int mx = Mathf.Clamp(Mathf.FloorToInt((Event.current.mousePosition.x - rect.x) / EffCell), 0, GridSize - 1);
            int my = Mathf.Clamp(Mathf.FloorToInt((Event.current.mousePosition.y - rect.y) / EffCell), 0, GridSize - 1);
            Vector2 tubePx = CellToGridPixel(rect, mx, my);

            // Draw preview line hub → tube
            Handles.color = new Color(pc.r, pc.g, pc.b, 0.5f);
            Handles.DrawDottedLine(hubPx, tubePx, 4f);

            // Intermediate node previews
            int total = ent.tubeSubdivisions + 2;
            for (int si = 1; si < total - 1; si++)
            {
                float t = (float)si / (total - 1);
                Vector2 np = Vector2.Lerp(tubePx, hubPx, t);
                Handles.color = new Color(pc.r, pc.g, pc.b, 0.7f);
                Handles.DrawSolidDisc(np, Vector3.forward, EffCell * 0.22f);
            }

            // Input tube dot at cursor (bright)
            Handles.color = pc;
            Handles.DrawSolidDisc(tubePx, Vector3.forward, EffCell * 0.35f);
            Handles.color = Color.white;
            Handles.DrawWireDisc(tubePx, Vector3.forward, EffCell * 0.45f, 2f);
            Handles.Label(tubePx + new Vector2(6f, -8f), "Input Tube");
        }

        // ── Sync last node of each path to current hub position ────────
        foreach (var ent in loadedData.entrances)
        {
            if (!ent.isLocked || ent.tubePath == null || ent.tubePath.Count < 2) continue;
            var hubCell = HubAngleToGridCell(rect, ent.lockHubAngle);
            if (ent.tubePath[ent.tubePath.Count - 1] != hubCell)
            {
                ent.tubePath[ent.tubePath.Count - 1] = hubCell;
                EditorUtility.SetDirty(loadedData);
            }
        }

        // ── Placed paths ───────────────────────────────────────────────
        for (int ei = 0; ei < loadedData.entrances.Count; ei++)
        {
            var ent = loadedData.entrances[ei];
            if (!ent.isLocked || ent.tubePath == null || ent.tubePath.Count == 0) continue;

            bool active = _tubeDrawEntranceIndex == ei;
            Color c = tubePalette[ei % tubePalette.Length];
            c.a = active ? 1f : 0.55f;
            Handles.color = c;

            // Connecting lines
            for (int pi = 0; pi < ent.tubePath.Count - 1; pi++)
            {
                Vector2 a = CellToGridPixel(rect, ent.tubePath[pi].x,     ent.tubePath[pi].y);
                Vector2 b = CellToGridPixel(rect, ent.tubePath[pi + 1].x, ent.tubePath[pi + 1].y);
                Handles.DrawLine(a, b, active ? 2.5f : 1.5f);
            }

            // Node dots + labels
            for (int pi = 0; pi < ent.tubePath.Count; pi++)
            {
                Vector2 center = CellToGridPixel(rect, ent.tubePath[pi].x, ent.tubePath[pi].y);
                float radius   = pi == 0 ? EffCell * 0.35f : EffCell * 0.25f; // input tube slightly larger
                Handles.color = c;
                Handles.DrawSolidDisc(center, Vector3.forward, radius);

                if (active && pi == _selectedTubeNodeIndex)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(center, Vector3.forward, radius + EffCell * 0.1f, 2f);
                }

                Handles.color = Color.black;
                Handles.Label(center - new Vector2(3f, 6f), pi.ToString());
            }

            // Label first node as input tube
            if (ent.tubePath.Count > 0)
            {
                Vector2 labelPos = CellToGridPixel(rect, ent.tubePath[0].x, ent.tubePath[0].y);
                Handles.color = Color.white;
                Handles.Label(labelPos + new Vector2(6f, -8f), $"[Tube] {ent.id}");
            }
        }
    }

    void HandleSplineWallInput(Rect rect, Event e)
    {
        if (loadedData.splineWallPaths == null)
            loadedData.splineWallPaths = new List<GridData.SplineWallPath>();

        // Ensure at least one path exists and active index is valid
        if (loadedData.splineWallPaths.Count == 0)
        {
            loadedData.splineWallPaths.Add(new GridData.SplineWallPath
            {
                prefabOverride = GetDefaultSplineWallPrefab()
            });
            _activeSplinePathIdx = 0;
            EditorUtility.SetDirty(loadedData);
        }
        _activeSplinePathIdx = Mathf.Clamp(_activeSplinePathIdx, 0, loadedData.splineWallPaths.Count - 1);

        const float pickRadius = 9f;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            // Check if near an existing node on any path
            int hitPath = -1, hitNode = -1;
            float bestDist = pickRadius;
            for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
            {
                var path = loadedData.splineWallPaths[pi];
                if (path.nodes == null) continue;
                for (int ni = 0; ni < path.nodes.Count; ni++)
                {
                    float d = Vector2.Distance(WorldXZToPixel(rect, path.nodes[ni]), e.mousePosition);
                    if (d < bestDist) { bestDist = d; hitPath = pi; hitNode = ni; }
                }
            }

            if (hitPath >= 0)
            {
                // Start dragging existing node
                _activeSplinePathIdx = hitPath;
                _dragSplinePathIdx   = hitPath;
                _dragSplineNodeIdx   = hitNode;
            }
            else
            {
                // Add node to active path
                Undo.RecordObject(loadedData, "Add Spline Wall Node");
                var activePath = loadedData.splineWallPaths[_activeSplinePathIdx];
                if (activePath.nodes == null) activePath.nodes = new List<Vector2>();
                activePath.nodes.Add(PixelToWorldXZ(rect, e.mousePosition));
                EditorUtility.SetDirty(loadedData);
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _dragSplineNodeIdx >= 0)
        {
            if (_dragSplinePathIdx >= 0 && _dragSplinePathIdx < loadedData.splineWallPaths.Count)
            {
                var path = loadedData.splineWallPaths[_dragSplinePathIdx];
                if (path.nodes != null && _dragSplineNodeIdx < path.nodes.Count)
                {
                    Undo.RecordObject(loadedData, "Move Spline Wall Node");
                    path.nodes[_dragSplineNodeIdx] = PixelToWorldXZ(rect, e.mousePosition);
                    EditorUtility.SetDirty(loadedData);
                }
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (_dragSplineNodeIdx >= 0)
            {
                _dragSplinePathIdx = -1;
                _dragSplineNodeIdx = -1;
                e.Use();
            }
        }
        else if (e.type == EventType.MouseDown && e.button == 1 && rect.Contains(e.mousePosition))
        {
            // Right-click: delete nearest node
            int hitPath = -1, hitNode = -1;
            float bestDist = pickRadius;
            for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
            {
                var path = loadedData.splineWallPaths[pi];
                if (path.nodes == null) continue;
                for (int ni = 0; ni < path.nodes.Count; ni++)
                {
                    float d = Vector2.Distance(WorldXZToPixel(rect, path.nodes[ni]), e.mousePosition);
                    if (d < bestDist) { bestDist = d; hitPath = pi; hitNode = ni; }
                }
            }
            if (hitPath >= 0)
            {
                Undo.RecordObject(loadedData, "Delete Spline Wall Node");
                loadedData.splineWallPaths[hitPath].nodes.RemoveAt(hitNode);
                EditorUtility.SetDirty(loadedData);
                e.Use();
                Repaint();
            }
        }
    }

    void DrawSplineWallOverlay(Rect rect)
    {
        if (loadedData?.splineWallPaths == null || loadedData.splineWallPaths.Count == 0) return;

        for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
        {
            var  path     = loadedData.splineWallPaths[pi];
            if (path?.nodes == null || path.nodes.Count == 0) continue;

            bool  isActive = _drawSplineWall && pi == _activeSplinePathIdx;
            Color col      = GetSplineWallColor(pi);
            col.a = isActive ? 0.95f : 0.45f;

            int n        = path.nodes.Count;
            int segCount = path.isClosed ? n : n - 1;

            // Draw spline curve or straight segments — black outline pass then white fill pass
            if (n >= 2)
            {
                const int samplesPerSeg = 16;
                float outlineW = isActive ? 7f : 5f;
                float fillW    = isActive ? 4f : 2.5f;

                // Build one continuous polyline across all segments, then draw outline then fill
                var polyPoints = new List<Vector3>();
                for (int seg = 0; seg < segCount; seg++)
                {
                    bool curved = path.IsSegmentCurved(seg);
                    int  i2     = path.isClosed ? (seg + 1) % n : seg + 1;
                    if (seg == 0)
                    {
                        Vector2 start = curved
                            ? WorldXZToPixel(rect, SplineWallSample(path.nodes, seg, 0f, path.isClosed))
                            : WorldXZToPixel(rect, path.nodes[0]);
                        polyPoints.Add((Vector3)start);
                    }
                    if (curved)
                    {
                        for (int s = 1; s <= samplesPerSeg; s++)
                        {
                            Vector2 pt = WorldXZToPixel(rect, SplineWallSample(path.nodes, seg, (float)s / samplesPerSeg, path.isClosed));
                            polyPoints.Add((Vector3)pt);
                        }
                    }
                    else
                    {
                        polyPoints.Add((Vector3)WorldXZToPixel(rect, path.nodes[i2]));
                    }
                }
                if (path.isClosed && polyPoints.Count > 1)
                    polyPoints.Add(polyPoints[0]);

                if (polyPoints.Count >= 2)
                {
                    Vector3[] pts = polyPoints.ToArray();
                    Handles.color = Color.black;
                    Handles.DrawAAPolyLine(outlineW, pts);
                    Handles.color = Color.white;
                    Handles.DrawAAPolyLine(fillW, pts);
                }
            }

            // Draw nodes
            for (int ni = 0; ni < n; ni++)
            {
                Vector2 px          = WorldXZToPixel(rect, path.nodes[ni]);
                bool    isDragNode  = pi == _dragSplinePathIdx && ni == _dragSplineNodeIdx;
                float   r           = isActive ? 5f : 3.5f;

                float nodeOuter = isActive ? 8f : 6f;
                float nodeInner = isActive ? 5.5f : 4f;
                Handles.color = Color.black;
                Handles.DrawSolidDisc(px, Vector3.forward, nodeOuter);
                Handles.color = isDragNode ? new Color(1f, 0.4f, 0f) : Color.white;
                Handles.DrawSolidDisc(px, Vector3.forward, nodeInner);

                if (ni == 0)
                {
                    Handles.color = Color.black;
                    Handles.DrawWireDisc(px, Vector3.forward, nodeOuter + 2.5f, 2f);
                }

                if (isActive && n <= 30)
                {
                    // Label in the opposite colour so it reads on both white and black paths
                    Color labelCol = (col == Color.white) ? Color.black : Color.white;
                    GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = labelCol } };
                    Handles.Label(px + new Vector2(6f, -8f), ni.ToString(), labelStyle);
                }
            }
        }
    }

    // Catmull-Rom sample shared by editor overlay and (via LevelSpawner) runtime
    static Vector2 SplineWallSample(List<Vector2> pts, int seg, float t, bool closed)
    {
        int n  = pts.Count;
        int i0 = closed ? (seg - 1 + n) % n : Mathf.Max(seg - 1, 0);
        int i1 = seg;
        int i2 = closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
        int i3 = closed ? (seg + 2) % n : Mathf.Min(seg + 2, n - 1);
        float t2 = t * t, t3 = t2 * t;
        Vector2 p0 = pts[i0], p1 = pts[i1], p2 = pts[i2], p3 = pts[i3];
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    GameObject GetDefaultSplineWallPrefab()
    {
        // Prefer exact name match, then any name containing "SplineWall"
        foreach (var p in scannedPrefabs)
            if (p != null && p.name == "BasicSplineWall1") return p;
        foreach (var p in scannedPrefabs)
            if (p != null && p.name.Contains("SplineWall")) return p;
        return null;
    }

    Color GetSplineWallColor(int pathIdx)
    {
        // Alternate black/white so multiple paths remain distinguishable without colour
        return (pathIdx % 2 == 0) ? Color.white : Color.black;
    }

    // Nodes are stored in normalised grid space (-0.5..0.5), independent of arena size.
    // Multiply by arenaWorldWidth at runtime to get world positions.
    Vector2 WorldXZToPixel(Rect gridRect, Vector2 normXZ)
    {
        float gridPx = EffCell * GridSize;
        return new Vector2(gridRect.center.x + normXZ.x * gridPx,
                           gridRect.center.y - normXZ.y * gridPx);
    }

    Vector2 PixelToWorldXZ(Rect gridRect, Vector2 pixel)
    {
        float gridPx = EffCell * GridSize;
        return new Vector2( (pixel.x - gridRect.center.x) / gridPx,
                           -(pixel.y - gridRect.center.y) / gridPx);
    }
}