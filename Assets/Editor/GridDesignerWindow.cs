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

    public GridSnapshot(int[] square, int[] circle,
                        List<GridData.ArenaEntrance> ents,
                        List<int> orbs, List<GridData.SoulSpawnPoint> souls,
                        List<int> waterMods, List<int> waveMods,
                        List<GridData.GridTier> tiers,
                        List<GridData.PrefabPlacement> basePrefabs)
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
                    spawnRadius = e.spawnRadius
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
    }

    public static List<GridData.PrefabPlacement> CopyPlacements(List<GridData.PrefabPlacement> src)
    {
        var copy = new List<GridData.PrefabPlacement>();
        if (src == null) return copy;
        foreach (var p in src)
            copy.Add(new GridData.PrefabPlacement { cellIndex = p.cellIndex, prefab = p.prefab, isCircle = p.isCircle });
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
    bool drawWaterLevelModifier;
    bool drawWaveModifier;
    int        activeTierIndex  = -1; // -1 = base layer
    List<bool> tierVisible      = new List<bool>();
    bool       baseLayerVisible = true;

    float[] cachedTierYOffsets; // pulled from LevelSpawner in scene

    // ── Direct Prefab Library ──
    string                        prefabFolderPath    = "Assets/Prefab/MazePieces";
    string                        iconsFolderPath     = "";
    List<GameObject>              scannedPrefabs      = new List<GameObject>();
    Dictionary<string, Texture2D> prefabIcons         = new Dictionary<string, Texture2D>();
    int                           selectedPrefabIndex = -1;
    bool                          drawDirectPrefab    = false;
    Vector2                       prefabScrollPos;
    bool                          showPrefabLibrary   = true;
    bool isDragging;
    int  lastDraggedCellIndex = -1;

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
    bool drawWhirlpool       = false;

    // Soul dropdown state (cached per-load)
    SoulData[]   _allSoulData;
    string[]     _soulDropdownLabels;
    GUIStyle     _greyLabelStyle;

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
        prefabFolderPath = EditorPrefs.GetString("GridDesigner_PrefabFolder", "Assets/Prefab/MazePieces");
        iconsFolderPath  = EditorPrefs.GetString("GridDesigner_IconsFolder",  "");
        ScanPrefabFolder();
        LoadPanelWidth();
    }

    void PushUndoSnapshot()
    {
        List<int> orbs      = loadedData?.orbCellIndices                 ?? new List<int>();
        List<int> waterMods = loadedData?.waterLevelModifierCellIndices  ?? new List<int>();
        List<int> waveMods  = loadedData?.waveModifierCellIndices        ?? new List<int>();
        List<GridData.SoulSpawnPoint> souls     = loadedData?.soulSpawnPoints ?? new List<GridData.SoulSpawnPoint>();
        List<GridData.ArenaEntrance>  entrances = loadedData?.entrances      ?? new List<GridData.ArenaEntrance>();
        undoStack.Push(new GridSnapshot(squareGrid, circleGrid, entrances, orbs, souls, waterMods, waveMods, loadedData?.tiers, loadedData?.prefabPlacements));
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
        EditorGUILayout.HelpBox(
            "GRID SEMANTICS\n\n" +
            "■ Square  = Reality space\n" +
            "● Circle  = Soul plane objects (non-soul)\n" +
            "★ Soul    = Soul spawn point (assign SoulData below)\n" +
            "→ Green   = Entrance portal",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel();
        DrawPanelResizeHandle();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();
    }

    Vector2 _leftPanelScroll;

    float   _leftPanelWidth   = 280f;
    bool    _isResizingPanel  = false;
    const float PanelMinWidth = 180f;
    const float PanelMaxWidth = 600f;
    const float HandleWidth   = 5f;

    void LoadPanelWidth()
    {
        _leftPanelWidth = EditorPrefs.GetFloat("GridDesigner_LeftPanelWidth", 280f);
    }

    void SavePanelWidth()
    {
        EditorPrefs.SetFloat("GridDesigner_LeftPanelWidth", _leftPanelWidth);
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

    void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth));
        _leftPanelScroll = EditorGUILayout.BeginScrollView(_leftPanelScroll,
            GUILayout.Width(_leftPanelWidth), GUILayout.ExpandHeight(true));
        EnsureSlotCapacity(GetMaxSlotUsed());

        if (GUILayout.Button("Add Slot"))
        {
            slotColors.Add(Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.6f, 1f));
            slotNotes.Add(string.Empty);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Slots", EditorStyles.boldLabel);

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
        EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);

        GUI.backgroundColor = drawSoul ? Color.yellow : Color.white;
        if (GUILayout.Button("★  Soul Spawn"))
        { activeSlot = -1; drawSoul = true; drawCircle = drawOrb = drawWhirlpool = false; }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("Orb Tool"))
        { activeSlot = -1; drawOrb = true; drawCircle = drawSoul = drawWaterLevelModifier = drawWaveModifier = drawWhirlpool = false; }

        GUI.backgroundColor = drawWaterLevelModifier ? new Color(0.4f, 0.8f, 1f) : Color.white;
        if (GUILayout.Button("▲▼  Water Level Modifier"))
        { activeSlot = -1; drawWaterLevelModifier = true; drawCircle = drawOrb = drawSoul = drawWaveModifier = drawWhirlpool = false; }
        GUI.backgroundColor = Color.white;

        GUI.backgroundColor = drawWaveModifier ? new Color(0.6f, 1f, 0.6f) : Color.white;
        if (GUILayout.Button("〜  Wave Modifier"))
        { activeSlot = -1; drawWaveModifier = true; drawCircle = drawOrb = drawSoul = drawWaterLevelModifier = drawWhirlpool = false; }
        GUI.backgroundColor = Color.white;

        GUI.backgroundColor = drawWhirlpool ? new Color(0.6f, 0.2f, 1f) : Color.white;
        if (GUILayout.Button("〇  Whirlpool"))
        { activeSlot = -1; drawWhirlpool = true; drawCircle = drawOrb = drawSoul = drawWaterLevelModifier = drawWaveModifier = false; }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("Eraser"))
        { activeSlot = 0; drawCircle = drawOrb = drawSoul = drawWaterLevelModifier = drawWaveModifier = drawWhirlpool = drawDirectPrefab = false; }

        EditorGUILayout.Space();
        showPrefabLibrary = EditorGUILayout.Foldout(showPrefabLibrary, "Prefab Library", true, EditorStyles.foldoutHeader);
        if (showPrefabLibrary)
        {
            // Prefabs folder
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Prefabs", GUILayout.Width(46));
            string newPath = EditorGUILayout.TextField(prefabFolderPath);
            if (newPath != prefabFolderPath) { prefabFolderPath = newPath; EditorPrefs.SetString("GridDesigner_PrefabFolder", prefabFolderPath); }
            if (GUILayout.Button("…", GUILayout.Width(22)))
            {
                string sel = EditorUtility.OpenFolderPanel("Select Prefab Folder", prefabFolderPath, "");
                if (!string.IsNullOrEmpty(sel)) { prefabFolderPath = FileUtil.GetProjectRelativePath(sel); EditorPrefs.SetString("GridDesigner_PrefabFolder", prefabFolderPath); ScanPrefabFolder(); }
            }
            EditorGUILayout.EndHorizontal();

            // Icons folder
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Icons", GUILayout.Width(46));
            string newIcons = EditorGUILayout.TextField(iconsFolderPath);
            if (newIcons != iconsFolderPath) { iconsFolderPath = newIcons; EditorPrefs.SetString("GridDesigner_IconsFolder", iconsFolderPath); }
            if (GUILayout.Button("…", GUILayout.Width(22)))
            {
                string sel = EditorUtility.OpenFolderPanel("Select Icons Folder", iconsFolderPath, "");
                if (!string.IsNullOrEmpty(sel)) { iconsFolderPath = FileUtil.GetProjectRelativePath(sel); EditorPrefs.SetString("GridDesigner_IconsFolder", iconsFolderPath); ScanIcons(); }
            }
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanPrefabFolder();
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
                    bool isSelected = drawDirectPrefab && selectedPrefabIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;

                    prefabIcons.TryGetValue(scannedPrefabs[i].name, out Texture2D icon);
                    var content = icon != null
                        ? new GUIContent(" " + scannedPrefabs[i].name, icon)
                        : new GUIContent(scannedPrefabs[i].name);

                    float btnHeight = icon != null ? IconSize + 4 : EditorGUIUtility.singleLineHeight + 2;
                    if (GUILayout.Button(content, GUILayout.Height(btnHeight)))
                    {
                        selectedPrefabIndex = i;
                        drawDirectPrefab    = true;
                        activeSlot = -1;
                        drawCircle = drawOrb = drawSoul =
                        drawWaterLevelModifier = drawWaveModifier = false;
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndScrollView();

                if (drawDirectPrefab && selectedPrefabIndex >= 0 && selectedPrefabIndex < scannedPrefabs.Count)
                    EditorGUILayout.HelpBox($"Placing: {scannedPrefabs[selectedPrefabIndex].name}", MessageType.None);
            }
        }

        EditorGUILayout.Space();

        // ── Level Data Sections ──────────────────────────────────────────
        if (loadedData != null)
        {
            DrawLevelIdentitySection();
            DrawCameraSection();
            DrawWavePresetsSection();
            DrawEnemySection();
            DrawTimeTrialSection();
            DrawPrefabsSection();
            DrawStartRitualSection();
            DrawSoulSpawnPointsSection();
            DrawWhirlpoolsSection();
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
        EditorGUILayout.LabelField("Tiers", EditorStyles.boldLabel);

        if (loadedData != null)
        {
            if (loadedData.tiers == null) loadedData.tiers = new List<GridData.GridTier>();

            // Pull tier offsets from LevelSpawner in scene
            RefreshCachedTierOffsets();

            // Base layer selector
            EditorGUILayout.BeginHorizontal();
            baseLayerVisible = EditorGUILayout.Toggle(baseLayerVisible, GUILayout.Width(16));
            GUI.backgroundColor = activeTierIndex == -1 ? Color.cyan : Color.white;
            if (GUILayout.Button("Base Layer")) activeTierIndex = -1;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Keep tierVisible in sync with tier count
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

                // Dropdown to pick which Y offset slot this tier uses
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

        if (drawSoul && loadedData != null)
        {
            if (loadedData.soulSpawnPoints == null)
                loadedData.soulSpawnPoints = new List<GridData.SoulSpawnPoint>();
            int existing = loadedData.soulSpawnPoints.FindIndex(s => s.cellIndex == index);
            if (existing >= 0) loadedData.soulSpawnPoints.RemoveAt(existing);
            else loadedData.soulSpawnPoints.Add(new GridData.SoulSpawnPoint { cellIndex = index });
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

        if (drawDirectPrefab && selectedPrefabIndex >= 0 && selectedPrefabIndex < scannedPrefabs.Count && loadedData != null)
        {
            var prefab     = scannedPrefabs[selectedPrefabIndex];
            var placements = GetActivePrefabPlacements();
            placements.RemoveAll(p => p.cellIndex == index);
            placements.Add(new GridData.PrefabPlacement { cellIndex = index, prefab = prefab, isCircle = drawCircle });
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

        if (loadedData.prefabs == null) loadedData.prefabs = new List<GameObject>();

        // Prefab slots list
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField($"Prefab Slots ({loadedData.prefabs.Count})", EditorStyles.miniLabel);
        for (int i = 0; i < loadedData.prefabs.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"[{i + 1}]", GUILayout.Width(28));
            loadedData.prefabs[i] = (GameObject)EditorGUILayout.ObjectField(
                loadedData.prefabs[i], typeof(GameObject), false);
            if (GUILayout.Button("✕", GUILayout.Width(22)))
            {
                Undo.RecordObject(loadedData, "Remove Prefab Slot");
                loadedData.prefabs.RemoveAt(i);
                EditorUtility.SetDirty(loadedData);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("+ Add Prefab Slot"))
        {
            Undo.RecordObject(loadedData, "Add Prefab Slot");
            loadedData.prefabs.Add(null);
            EditorUtility.SetDirty(loadedData);
        }
        if (EditorGUI.EndChangeCheck())
            EditorUtility.SetDirty(loadedData);

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

    void DrawSoulSpawnPointsSection()
    {
        if (loadedData.soulSpawnPoints == null) loadedData.soulSpawnPoints = new List<GridData.SoulSpawnPoint>();

        int soulCount = loadedData.soulSpawnPoints.Count;
        _showSoulSpawns = EditorGUILayout.Foldout(_showSoulSpawns,
            $"Soul Spawn Points ({soulCount})", true, EditorStyles.foldoutHeader);
        if (!_showSoulSpawns) return;

        // Lazy-load all SoulData
        EnsureSoulDataCache();

        // Randomise wave presets button
        if (GUILayout.Button("Randomise Soul Wave Presets"))
            RandomiseSoulWavePresets();

        EditorGUILayout.Space(2);

        if (soulCount == 0)
        {
            EditorGUILayout.LabelField("  (no soul spawn points placed on the grid)", EditorStyles.miniLabel);
            return;
        }

        for (int i = 0; i < loadedData.soulSpawnPoints.Count; i++)
        {
            var sp = loadedData.soulSpawnPoints[i];

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Cell {sp.cellIndex}", GUILayout.Width(60));

            // Build dropdown selection index
            int currentSoulIdx = -1; // -1 = (none)
            if (_allSoulData != null && sp.soulData != null)
            {
                for (int s = 0; s < _allSoulData.Length; s++)
                    if (_allSoulData[s] == sp.soulData) { currentSoulIdx = s; break; }
            }

            int popupSel = currentSoulIdx + 1; // +1 because index 0 = "(none)"

            EditorGUI.BeginChangeCheck();
            popupSel = EditorGUILayout.Popup(popupSel, _soulDropdownLabels);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Assign Soul to Spawn Point");

                // Unmark old soul
                if (sp.soulData != null)
                {
                    Undo.RecordObject(sp.soulData, "Unallocate Soul");
                    sp.soulData.allocated        = false;
                    sp.soulData.allocatedToLevelID = "";
                    EditorUtility.SetDirty(sp.soulData);
                }

                // Assign new soul
                SoulData newSoul = popupSel > 0 ? _allSoulData[popupSel - 1] : null;
                sp.soulData = newSoul;

                if (newSoul != null)
                {
                    Undo.RecordObject(newSoul, "Allocate Soul");
                    newSoul.allocated          = true;
                    newSoul.allocatedToLevelID = loadedData.levelID;

                    // Override wave preset to this level's gameplay preset
                    if (loadedData.gameplayWavePreset != null)
                        newSoul.associatedWavePreset = loadedData.gameplayWavePreset;

                    EditorUtility.SetDirty(newSoul);
                }

                loadedData.soulSpawnPoints[i] = sp;
                EditorUtility.SetDirty(loadedData);
                _allSoulData = null; // refresh labels next frame
            }

            EditorGUILayout.EndHorizontal();
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
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Row 1: ID / Angle / remove button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ID", GUILayout.Width(18));
            string newEntID    = EditorGUILayout.TextField(ent.id, GUILayout.Width(78));
            EditorGUILayout.LabelField("Angle", GUILayout.Width(36));
            float  newEntAngle = EditorGUILayout.FloatField(ent.perimeterAngle, GUILayout.Width(38));
            EditorGUILayout.LabelField("°", GUILayout.Width(10));
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(22))) entToRemove = i;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Row 2: Tier / SpawnRadius
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Tier", GUILayout.Width(28));
            int entTierPopup   = EditorGUILayout.Popup(Mathf.Clamp(ent.tierSlot + 1, 0, tierLabels.Length - 1), tierLabels, GUILayout.Width(72));
            int newEntTierSlot = entTierPopup - 1;
            EditorGUILayout.LabelField("Inward", GUILayout.Width(42));
            float newSpawnRadius = EditorGUILayout.FloatField(ent.spawnRadius, GUILayout.Width(42));
            EditorGUILayout.EndHorizontal();

            // Row 3: Prefab
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Prefab", GUILayout.Width(46));
            GameObject newEntPrefab = (GameObject)EditorGUILayout.ObjectField(ent.prefab, typeof(GameObject), false);
            EditorGUILayout.EndHorizontal();

            // Row 4: Soul prefab
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("SoulPfb", GUILayout.Width(46));
            GameObject newEntSoulPrefab = (GameObject)EditorGUILayout.ObjectField(ent.soulPrefab, typeof(GameObject), false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Entrance");
                ent.id             = newEntID;
                ent.perimeterAngle = newEntAngle;
                ent.tierSlot       = newEntTierSlot;
                ent.spawnRadius    = newSpawnRadius;
                ent.prefab         = newEntPrefab;
                ent.soulPrefab     = newEntSoulPrefab;
                EditorUtility.SetDirty(loadedData);
                Repaint();
            }
        }

        if (entToRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Remove Entrance");
            loadedData.entrances.RemoveAt(entToRemove);
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
        EditorGUILayout.BeginVertical();
        DrawGrid();
        EditorGUILayout.EndVertical();
    }

    void DrawGrid()
    {
        Rect rect = GUILayoutUtility.GetRect(GridPixelSize, GridPixelSize,
            GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

        Handles.BeginGUI();
        Handles.color = new Color(1f, 1f, 1f, 0.08f);
        Handles.DrawSolidDisc(rect.center, Vector3.forward, GridPixelSize * 0.5f);
        Handles.EndGUI();

        Event e = Event.current;

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int  index = y * GridSize + x;
                Rect cell  = new Rect(rect.x + x * CellSize, rect.y + y * CellSize, CellSize, CellSize);

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
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, CellSize * 0.3f);
                    }

                    // Soul spawn
                    if (loadedData?.soulSpawnPoints != null &&
                        loadedData.soulSpawnPoints.Exists(s => s.cellIndex == index))
                    {
                        Handles.color = new Color(1f, 0.92f, 0.016f, baseAlpha);
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, CellSize * 0.38f);
                        Handles.color = new Color(0f, 0f, 0f, baseAlpha);
                        Handles.Label(cell.center - new Vector2(4f, 6f), "★");
                    }

                    // Orb
                    if (loadedData?.orbCellIndices != null && loadedData.orbCellIndices.Contains(index))
                    {
                        Handles.color = new Color(1f, 1f, 0f, baseAlpha);
                        Handles.DrawWireDisc(cell.center, Vector3.forward, CellSize * 0.35f);
                    }

                    // Water Level Modifier
                    if (loadedData?.waterLevelModifierCellIndices != null && loadedData.waterLevelModifierCellIndices.Contains(index))
                    {
                        Handles.color = new Color(0.4f, 0.8f, 1f, baseAlpha);
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, CellSize * 0.38f);
                        Handles.color = new Color(1f, 1f, 1f, baseAlpha);
                        Handles.Label(cell.center - new Vector2(4f, 6f), "W");
                    }

                    // Wave Modifier
                    if (loadedData?.waveModifierCellIndices != null && loadedData.waveModifierCellIndices.Contains(index))
                    {
                        Handles.color = new Color(0.4f, 1f, 0.4f, baseAlpha);
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, CellSize * 0.38f);
                        Handles.color = new Color(0f, 0f, 0f, baseAlpha);
                        Handles.Label(cell.center - new Vector2(4f, 6f), "~");
                    }

                    // Whirlpool
                    if (loadedData?.whirlpools != null && loadedData.whirlpools.Exists(w => w.cellIndex == index))
                    {
                        Handles.color = new Color(0.6f, 0.2f, 1f, baseAlpha);
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, CellSize * 0.38f);
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
                                    CellSize - inset * 2, CellSize - inset * 2), c);
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
                            Handles.DrawSolidDisc(cell.center, Vector3.forward, CellSize * 0.38f);
                            Handles.color = new Color(1f, 1f, 1f, a);
                            Handles.Label(cell.center - new Vector2(4f, 6f), "W");
                        }

                        // Wave Modifier
                        if (tier.waveModifierCellIndices != null && tier.waveModifierCellIndices.Contains(index))
                        {
                            Handles.color = new Color(0.4f, 1f, 0.4f, a);
                            Handles.DrawSolidDisc(cell.center, Vector3.forward, CellSize * 0.38f);
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
                    if (index != lastDraggedCellIndex && !drawSoul)
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

                EditorGUI.DrawRect(new Rect(cell.x, cell.y, CellSize, 1), Color.black);
                EditorGUI.DrawRect(new Rect(cell.x, cell.y, 1, CellSize), Color.black);
            }
        }

        // Portal perimeter overlay
        if (loadedData != null)
            DrawPortalOverlay(rect);
    }

    void DrawPortalOverlay(Rect gridRect)
    {
        const float ringRadius   = GridPixelSize * 0.5f + 14f;
        const float arrowLen     = 10f;
        const float dotRadius    = 5f;
        Vector2     centre       = gridRect.center;

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

        if (GUILayout.Button("CLEAR"))
        {
            PushUndoSnapshot();
            System.Array.Clear(squareGrid, 0, CellCount);
            System.Array.Clear(circleGrid, 0, CellCount);
            if (loadedData != null)
            {
                loadedData.orbCellIndices?.Clear();
                loadedData.soulSpawnPoints?.Clear();
            }
            RebuildSlotsFromGrid();
            Repaint();
        }

        if (GUILayout.Button("CLEAR ORBS") && loadedData != null)
        { PushUndoSnapshot(); loadedData.orbCellIndices?.Clear(); Repaint(); }

        if (GUILayout.Button("CLEAR SOULS") && loadedData != null)
        { PushUndoSnapshot(); loadedData.soulSpawnPoints?.Clear(); Repaint(); }

        if (GUILayout.Button("UNDO")) UndoLastAction();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("SAVE"))    SaveGridInPlace();
        if (GUILayout.Button("SAVE AS")) SaveGrid();
        if (GUILayout.Button("LOAD"))    LoadGrid();
        EditorGUILayout.EndHorizontal();
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
                    spawnRadius = e.spawnRadius
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
        _allSoulData = null; // force re-scan on next draw

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

        activeSlot = 0; drawCircle = drawSoul = false;
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
        for (int i = 0; i < discoveredGrids.Count; i++) discoveredGridNames[i] = discoveredGrids[i].name;
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
}