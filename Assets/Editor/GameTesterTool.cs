// GameTesterTool.cs
// Place this file anywhere inside an Editor/ folder in your project.
// Open via: Tools > GameTesterTool

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

public class GameTesterTool : EditorWindow
{
    // ─────────────────────────────────────────────
    // State
    // ─────────────────────────────────────────────

    private Vector2 _scroll;
    private bool    _confirmClearAll;
    private double  _confirmClearAllTime;
    private const double ConfirmTimeout = 3.0;

    private bool _showPreSession    = true;
    private bool _showToolUnlocks  = true;
    private bool _showLauncher     = true;
    private bool _showBoat         = true;
    private bool _showSouls        = true;
    private bool _showLevels       = true;
    private bool _showCaughtSouls  = true;
    private bool _showObstacles    = true;

    // Tool unlock state (mirrored into BoatToolManager statics)
    private bool _unlockWhirl    = true;
    private bool _unlockCatapult = true;
    private bool _unlockLure     = true;

    // Scene launcher
    private GridData[]  _allGridData;
    private string[]    _gridDataNames;
    private int         _selectedGridDataIndex;

    // LevelSelect scene list
    private string[] _levelSelectSceneNames;
    private int      _selectedLevelSelectIndex;
    private const string PrefLevelSelectIndex = "SaveDataMonitor_LevelSelectIndex";

    // Debug soul injection
    private int _soulsToInject = 3;

    // Auto-refresh
    private bool   _autoRefresh    = true;
    private double _lastRefresh;
    private const double RefreshInterval     = 0.5;
    private const double RefreshIntervalEdit = 2.0;

    // ─────────────────────────────────────────────
    // Styles (created once)
    // ─────────────────────────────────────────────

    private GUIStyle _headerStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _keyStyle;
    private GUIStyle _valueStyle;
    private GUIStyle _dangerButton;
    private GUIStyle _warningButton;
    private GUIStyle _okButton;
    private bool     _stylesBuilt;

    // ─────────────────────────────────────────────
    // EditorPrefs keys
    // ─────────────────────────────────────────────

    private const string PrefGridData        = "SaveDataMonitor_GridDataName";
    private const string PrefUnlockWhirl     = "SaveDataMonitor_UnlockWhirl";
    private const string PrefUnlockCatapult  = "SaveDataMonitor_UnlockCatapult";
    private const string PrefUnlockLure      = "SaveDataMonitor_UnlockLure";

    // ─────────────────────────────────────────────
    // Open / Enable / Disable
    // ─────────────────────────────────────────────

    [MenuItem("Tools/GameTesterTool")]
    public static void Open()
    {
        var window = GetWindow<GameTesterTool>("GameTesterTool");
        window.minSize = new Vector2(380, 500);
    }

    private void OnEnable()
    {
        _unlockWhirl    = EditorPrefs.GetBool(PrefUnlockWhirl,    true);
        _unlockCatapult = EditorPrefs.GetBool(PrefUnlockCatapult, true);
        _unlockLure     = EditorPrefs.GetBool(PrefUnlockLure,     true);

        ApplyToolUnlocksToStatics();
    }

    private void OnDisable()
    {
        if (_gridDataNames != null && _selectedGridDataIndex < _gridDataNames.Length)
            EditorPrefs.SetString(PrefGridData, _gridDataNames[_selectedGridDataIndex]);
        EditorPrefs.SetInt(PrefLevelSelectIndex, _selectedLevelSelectIndex);

        EditorPrefs.SetBool(PrefUnlockWhirl,    _unlockWhirl);
        EditorPrefs.SetBool(PrefUnlockCatapult, _unlockCatapult);
        EditorPrefs.SetBool(PrefUnlockLure,     _unlockLure);
    }

    // ─────────────────────────────────────────────
    // Repaint
    // ─────────────────────────────────────────────

    private void OnInspectorUpdate()
    {
        if (_autoRefresh)
        {
            double interval = EditorApplication.isPlaying ? RefreshInterval : RefreshIntervalEdit;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRefresh > interval)
            {
                _lastRefresh = now;
                Repaint();
            }
        }
    }

    // ─────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────

    private void OnGUI()
    {
        BuildStyles();

        // Toolbar
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("GameTesterTool", _headerStyle, GUILayout.ExpandWidth(true));

        _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto-Refresh", EditorStyles.toolbarButton, GUILayout.Width(90));

        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            Repaint();

        EditorGUILayout.EndHorizontal();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Showing saved data from disk (read-only outside Play Mode).", MessageType.Info);
        }

        EditorGUILayout.Space(4);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawPreSessionSection();
        DrawToolUnlocksSection();
        DrawSceneLauncherSection();
        DrawBoatSection();
        DrawSoulsSection();
        DrawLevelCompletionsSection();
        DrawCaughtSoulsSection();
        DrawObstaclesSection();
        DrawSoulAssetCleanupSection();
        DrawDangerZone();

        EditorGUILayout.EndScrollView();
    }

    // ─────────────────────────────────────────────
    // Sections
    // ─────────────────────────────────────────────

    private void DrawPreSessionSection()
    {
        _showPreSession = DrawSectionHeader("⚙  Pre-Session Setup", _showPreSession);
        if (!_showPreSession) return;

        EditorGUILayout.HelpBox("These actions work before or during play mode.", MessageType.None);
        EditorGUILayout.Space(2);

        // Snake toggle
        bool snakeDisabled = LevelSpawner.ForceDisableSnake;
        bool newSnakeDisabled = EditorGUILayout.ToggleLeft(" Force Disable Snake Spawn", snakeDisabled);
        if (newSnakeDisabled != snakeDisabled)
        {
            LevelSpawner.ForceDisableSnake = newSnakeDisabled;
            Debug.Log($"[SaveDataMonitor] Force disable snake: {newSnakeDisabled}");
        }

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Clear All Save Data Now", _warningButton))
        {
            GameProgressData.ClearAll();
            Debug.Log("[SaveDataMonitor] All save data cleared (pre-session).");
        }

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Inject Random Souls:", GUILayout.Width(140));
        _soulsToInject = EditorGUILayout.IntSlider(_soulsToInject, 1, 20);
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button($"Add {_soulsToInject} Random Soul(s) to Boat", _okButton))
            InjectRandomSouls(_soulsToInject);

        EditorGUILayout.Space(6);
    }

    private void DrawToolUnlocksSection()
    {
        _showToolUnlocks = DrawSectionHeader("🔧  Tool Unlocks", _showToolUnlocks);
        if (!_showToolUnlocks) return;

        EditorGUI.BeginChangeCheck();

        _unlockWhirl    = EditorGUILayout.ToggleLeft(" Whirl Sucker", _unlockWhirl);
        _unlockCatapult = EditorGUILayout.ToggleLeft(" Catapult",     _unlockCatapult);
        _unlockLure     = EditorGUILayout.ToggleLeft(" Lure",         _unlockLure);

        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(PrefUnlockWhirl,    _unlockWhirl);
            EditorPrefs.SetBool(PrefUnlockCatapult, _unlockCatapult);
            EditorPrefs.SetBool(PrefUnlockLure,     _unlockLure);
            ApplyToolUnlocksToStatics();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Shop Purchases", EditorStyles.boldLabel);

        bool figureheadOwned    = GameProgressData.IsItemPurchased(ShopItemUnlockToggle.FigureheadItemID);
        bool newFigureheadOwned = EditorGUILayout.ToggleLeft(" Figurehead Purchased", figureheadOwned);
        if (newFigureheadOwned != figureheadOwned)
        {
            if (newFigureheadOwned) GameProgressData.PurchaseItem(ShopItemUnlockToggle.FigureheadItemID);
            else                    GameProgressData.RevokeItem(ShopItemUnlockToggle.FigureheadItemID);
            Debug.Log($"[SaveDataMonitor] Figurehead purchased: {newFigureheadOwned}");
        }

        EditorGUILayout.Space(6);
    }

    private void ApplyToolUnlocksToStatics()
    {
        BoatToolManager.EditorOverrideWhirl    = _unlockWhirl;
        BoatToolManager.EditorOverrideCatapult = _unlockCatapult;
        BoatToolManager.EditorOverrideLure     = _unlockLure;
    }

    private void DrawSceneLauncherSection()
    {
        _showLauncher = DrawSectionHeader("▶  Scene Launcher", _showLauncher);
        if (!_showLauncher) return;

        if (_allGridData == null) RefreshGridData();

        // ── Test Level (Waves1) ─────────────────────────────────────────────
        EditorGUILayout.LabelField("Test Level  (Waves1)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (_allGridData != null && _gridDataNames != null && _gridDataNames.Length > 0)
            _selectedGridDataIndex = EditorGUILayout.Popup(_selectedGridDataIndex, _gridDataNames);
        else
            EditorGUILayout.LabelField("  (no GridData assets found in Resources/Levels/)", EditorStyles.miniLabel);
        if (GUILayout.Button("↺", GUILayout.Width(24))) RefreshGridData();
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginDisabledGroup(_allGridData == null || _allGridData.Length == 0);
        if (GUILayout.Button("Test Level", _okButton))
        {
            if (_allGridData != null && _allGridData.Length > 0)
                LaunchScene("Waves1", _allGridData[_selectedGridDataIndex], null);
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(12);

        // ── Test LevelSelect ────────────────────────────────────────────────
        EditorGUILayout.LabelField("Test LevelSelect", EditorStyles.boldLabel);

        if (_levelSelectSceneNames == null) RefreshLevelSelectScenes();

        EditorGUILayout.BeginHorizontal();
        if (_levelSelectSceneNames != null && _levelSelectSceneNames.Length > 0)
        {
            _selectedLevelSelectIndex = EditorGUILayout.Popup(_selectedLevelSelectIndex, _levelSelectSceneNames);
        }
        else
        {
            EditorGUILayout.LabelField("  (no LevelSelectWorld scenes in Build Settings)", EditorStyles.miniLabel);
        }
        if (GUILayout.Button("↺", GUILayout.Width(24))) RefreshLevelSelectScenes();
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginDisabledGroup(_levelSelectSceneNames == null || _levelSelectSceneNames.Length == 0);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Test LevelSelect", _okButton))
            LaunchScene(_levelSelectSceneNames[_selectedLevelSelectIndex], null, null);
        if (GUILayout.Button("Fresh Save", _okButton))
        {
            GameProgressData.ClearAll();
            LaunchScene(_levelSelectSceneNames[_selectedLevelSelectIndex], null, null);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(6);
    }

    private void RefreshGridData()
    {
        var guids = AssetDatabase.FindAssets("t:GridData", new[] { "Assets/Resources/Levels" });
        if (guids.Length == 0)
            guids = AssetDatabase.FindAssets("t:GridData"); // fallback: search everywhere
        var list = new System.Collections.Generic.List<GridData>();
        foreach (string guid in guids)
        {
            var data = AssetDatabase.LoadAssetAtPath<GridData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null) list.Add(data);
        }
        _allGridData   = list.ToArray();
        _gridDataNames = new string[_allGridData.Length];
        for (int i = 0; i < _allGridData.Length; i++)
            _gridDataNames[i] = !string.IsNullOrEmpty(_allGridData[i].levelID)
                ? _allGridData[i].levelID
                : _allGridData[i].name;

        string savedName = EditorPrefs.GetString(PrefGridData, "");
        if (!string.IsNullOrEmpty(savedName))
        {
            int idx = System.Array.IndexOf(_gridDataNames, savedName);
            if (idx >= 0) _selectedGridDataIndex = idx;
        }
        _selectedGridDataIndex = Mathf.Clamp(_selectedGridDataIndex, 0, Mathf.Max(0, _allGridData.Length - 1));
    }

    private void RefreshLevelSelectScenes()
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes)
        {
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(s.path);
            if (sceneName.StartsWith("LevelSelectWorld"))
                names.Add(sceneName);
        }
        _levelSelectSceneNames = names.ToArray();
        _selectedLevelSelectIndex = Mathf.Clamp(
            EditorPrefs.GetInt(PrefLevelSelectIndex, 0), 0, Mathf.Max(0, _levelSelectSceneNames.Length - 1));
    }

    public static void LaunchScene(string sceneName, GridData levelData, WavePreset waveOverride)
    {
        if (levelData != null)
        {
            string path = AssetDatabase.GetAssetPath(levelData);
            EditorPrefs.SetString("SaveDataMonitor_OverrideLevel", path);
            Debug.LogWarning($"[SaveDataMonitor] SETTING OVERRIDE: Level '{levelData.levelID}', Path: '{path}'");
        }

        if (waveOverride != null)
        {
            string path = AssetDatabase.GetAssetPath(waveOverride);
            EditorPrefs.SetString("SaveDataMonitor_OverrideWavePreset", path);
            Debug.LogWarning($"[SaveDataMonitor] SETTING OVERRIDE: WavePreset '{waveOverride.name}', Path: '{path}'");
        }

        WavePreset presetForTuner = waveOverride ?? levelData?.gameplayWavePreset;
        if (presetForTuner != null)
            WaveEffectsLiveTuner.InstallPreset(presetForTuner);

        // Open the scene then enter play mode
        string scenePath = null;
        foreach (var s in UnityEditor.EditorBuildSettings.scenes)
        {
            if (System.IO.Path.GetFileNameWithoutExtension(s.path) == sceneName)
            {
                scenePath = s.path;
                break;
            }
        }

        if (scenePath == null)
        {
            Debug.LogWarning($"[SaveDataMonitor] Scene '{sceneName}' not found in Build Settings.");
            return;
        }

        // Save any dirty scenes before switching to avoid losing unsaved changes
        bool canProceed = UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        if (!canProceed) return;

        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
        EditorApplication.isPlaying = true;
    }

    private void DrawBoatSection()
    {
        _showBoat = DrawSectionHeader("⛵  Boat / World Map", _showBoat);
        if (!_showBoat) return;

        SaveData data = LoadData();

        DrawRow("Segment ID",       data?.boatSegmentID      ?? "—");
        DrawRow("Spline Progress",  data?.boatSplineProgress.ToString("F4") ?? "—");
        DrawRow("River Progress",   data?.riverExtrudeProgress.ToString("F4") ?? "—");
        DrawRow("Is Left Path",     data?.boatIsLeftPath.ToString()    ?? "—");
        DrawRow("Is Right Path",    data?.boatIsRightPath.ToString()   ?? "—");

        EditorGUILayout.Space(4);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("Clear Boat Progress", _warningButton))
        {
            GameProgressData.ClearBoatProgress();
            Debug.Log("[SaveDataMonitor] Boat progress cleared.");
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(6);
    }

    private void DrawSoulsSection()
    {
        _showSouls = DrawSectionHeader("👻  Souls", _showSouls);
        if (!_showSouls) return;

        SaveData data = LoadData();

        int onBoat = data?.soulsOnBoat ?? 0;
        DrawRow("Souls on Boat",    onBoat.ToString());
        DrawRow("Permanent Souls",  data?.permanentSouls.ToString() ?? "—");

        if (data?.soulsOnBoatIdentities != null && data.soulsOnBoatIdentities.Count > 0)
        {
            EditorGUILayout.LabelField("  Boat IDs:", string.Join(", ", data.soulsOnBoatIdentities), _keyStyle);
        }

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Clear Souls on Boat", _warningButton))
        {
            GameProgressData.ClearSoulsOnBoat();
            Debug.Log("[SaveDataMonitor] Souls on boat cleared.");
        }

        EditorGUILayout.Space(6);
    }

    private void DrawLevelCompletionsSection()
    {
        _showLevels = DrawSectionHeader("🏁  Level Completions", _showLevels);
        if (!_showLevels) return;

        SaveData data = LoadData();
        var completions = data?.levelCompletions;

        if (completions == null || completions.Count == 0)
        {
            EditorGUILayout.LabelField("  (none)", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var entry in completions)
            {
                EditorGUILayout.BeginHorizontal();
                DrawRow($"  {entry.levelID}", $"× {entry.count}");
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(42)))
                {
                    GameProgressData.ClearCompletionCount(entry.levelID);
                    Debug.Log($"[SaveDataMonitor] Cleared completion count for '{entry.levelID}'.");
                    break; // list may have changed
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.Space(6);
    }

    private void DrawCaughtSoulsSection()
    {
        _showCaughtSouls = DrawSectionHeader("🎣  Caught Souls (per Level)", _showCaughtSouls);
        if (!_showCaughtSouls) return;

        SaveData data = LoadData();
        var caughtSouls = data?.caughtSouls;

        if (caughtSouls == null || caughtSouls.Count == 0)
        {
            EditorGUILayout.LabelField("  (none)", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var entry in caughtSouls)
            {
                EditorGUILayout.BeginHorizontal();
                string ids = entry.caughtLinkIDs.Count > 0
                    ? string.Join(", ", entry.caughtLinkIDs)
                    : "(empty)";
                DrawRow($"  {entry.levelID}", $"[{ids}]");
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(42)))
                {
                    // Clear all caught souls for this level
                    var ids2 = new List<int>(entry.caughtLinkIDs);
                    foreach (int id in ids2)
                        GameProgressData.UnrecordCaughtSoul(entry.levelID, id);
                    Debug.Log($"[SaveDataMonitor] Cleared caught souls for '{entry.levelID}'.");
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.Space(6);
    }

    private void DrawObstaclesSection()
    {
        _showObstacles = DrawSectionHeader("🚧  Unlocked Obstacles", _showObstacles);
        if (!_showObstacles) return;

        SaveData data = LoadData();
        var obstacles = data?.unlockedObstacles;

        if (obstacles == null || obstacles.Count == 0)
        {
            EditorGUILayout.LabelField("  (none)", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var id in obstacles)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"  {id}", _keyStyle);
                if (GUILayout.Button("Lock", EditorStyles.miniButton, GUILayout.Width(42)))
                {
                    GameProgressData.LockObstacle(id);
                    Debug.Log($"[SaveDataMonitor] Locked obstacle '{id}'.");
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.Space(6);
    }

    private void DrawSoulAssetCleanupSection()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("— Soul Asset Cleanup —", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.Space(2);
        EditorGUILayout.HelpBox(
            "Rebuilds allocated/allocatedToLevelID on every SoulData asset from the actual soulZones in each GridData level asset. Fixes stale entries left when souls are removed from a zone.",
            MessageType.None);

        if (GUILayout.Button("Fix Soul Allocation Fields", _warningButton))
            RunSoulAllocationCleanup();

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "FULL WIPE: empties soulZones[].souls in every GridData level asset (zone geometry kept) and deallocates every SoulData. Frees all soul IDs for reassignment. Experimental reset — destroys existing soul assignments across all levels.",
            MessageType.Warning);

        if (GUILayout.Button("Clear All Soul IDs From Levels", _dangerButton))
            ClearAllSoulIdsFromLevels();

        EditorGUILayout.Space(6);
    }

    private void ClearAllSoulIdsFromLevels()
    {
        if (!EditorUtility.DisplayDialog(
                "Clear All Soul IDs From Levels",
                "This empties the soul assignments in EVERY level and deallocates EVERY soul asset. "
                + "Zone geometry is kept, but every soul ID becomes free again.\n\nThis cannot be undone. Continue?",
                "Wipe Everything", "Cancel"))
            return;

        // 1. Clear soul references from every level's zones
        var levelGuids = AssetDatabase.FindAssets("t:GridData", new[] { "Assets/Resources/Levels" });
        if (levelGuids.Length == 0)
            levelGuids = AssetDatabase.FindAssets("t:GridData"); // fallback: search everywhere

        int levelsTouched = 0, zonesCleared = 0;
        foreach (string guid in levelGuids)
        {
            var gridData = AssetDatabase.LoadAssetAtPath<GridData>(AssetDatabase.GUIDToAssetPath(guid));
            if (gridData == null || gridData.soulZones == null) continue;

            bool changed = false;
            foreach (var zone in gridData.soulZones)
            {
                if (zone.souls != null && zone.souls.Count > 0)
                {
                    zone.souls.Clear();
                    zonesCleared++;
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(gridData);
                levelsTouched++;
            }
        }

        // 2. Deallocate every SoulData asset
        var soulGuids = AssetDatabase.FindAssets("t:SoulData");
        int deallocated = 0;
        foreach (string guid in soulGuids)
        {
            var soul = AssetDatabase.LoadAssetAtPath<SoulData>(AssetDatabase.GUIDToAssetPath(guid));
            if (soul == null) continue;

            if (soul.allocated || !string.IsNullOrEmpty(soul.allocatedToLevelID))
            {
                soul.allocated          = false;
                soul.allocatedToLevelID = "";
                EditorUtility.SetDirty(soul);
                deallocated++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SoulCleanup] Wiped soul IDs. Levels touched: {levelsTouched}, zones cleared: {zonesCleared}, souls deallocated: {deallocated}.");
        EditorUtility.DisplayDialog("Clear All Soul IDs",
            $"Levels touched: {levelsTouched}\nZones cleared: {zonesCleared}\nSouls deallocated: {deallocated}", "OK");
    }

    private void RunSoulAllocationCleanup()
    {
        // Build map: soul asset instance ID -> levelID it actually lives in
        var soulToLevel = new Dictionary<SoulData, string>();

        var levelGuids = AssetDatabase.FindAssets("t:GridData", new[] { "Assets/Resources/Levels" });
        foreach (string guid in levelGuids)
        {
            var gridData = AssetDatabase.LoadAssetAtPath<GridData>(AssetDatabase.GUIDToAssetPath(guid));
            if (gridData == null || string.IsNullOrEmpty(gridData.levelID)) continue;

            foreach (var zone in gridData.soulZones)
            {
                if (zone.souls == null) continue;
                foreach (var soul in zone.souls)
                {
                    if (soul != null)
                        soulToLevel[soul] = gridData.levelID;
                }
            }
        }

        // Fix every SoulData asset
        var soulGuids = AssetDatabase.FindAssets("t:SoulData", new[] { "Assets/Resources/Souls" });
        int fixed_ = 0, cleared = 0;

        foreach (string guid in soulGuids)
        {
            var soul = AssetDatabase.LoadAssetAtPath<SoulData>(AssetDatabase.GUIDToAssetPath(guid));
            if (soul == null) continue;

            if (soulToLevel.TryGetValue(soul, out string levelID))
            {
                if (!soul.allocated || soul.allocatedToLevelID != levelID)
                {
                    soul.allocated          = true;
                    soul.allocatedToLevelID = levelID;
                    EditorUtility.SetDirty(soul);
                    fixed_++;
                }
            }
            else
            {
                if (soul.allocated || !string.IsNullOrEmpty(soul.allocatedToLevelID))
                {
                    soul.allocated          = false;
                    soul.allocatedToLevelID = "";
                    EditorUtility.SetDirty(soul);
                    cleared++;
                }
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[SoulCleanup] Done. Fixed: {fixed_}, Cleared stale: {cleared}");
        EditorUtility.DisplayDialog("Soul Cleanup", $"Fixed: {fixed_}\nCleared stale: {cleared}", "OK");
    }

    private void DrawDangerZone()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("— Danger Zone —", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.Space(2);

        // Confirm-click pattern: first click arms, second click within timeout fires
        if (_confirmClearAll)
        {
            double remaining = ConfirmTimeout - (EditorApplication.timeSinceStartup - _confirmClearAllTime);
            if (remaining > 0)
            {
                EditorGUILayout.HelpBox($"Click again within {remaining:F1}s to confirm FULL CLEAR.", MessageType.Warning);
                if (GUILayout.Button("⚠  CONFIRM CLEAR ALL SAVE DATA", _dangerButton))
                {
                    GameProgressData.ClearAll();
                    _confirmClearAll = false;
                    Debug.Log("[SaveDataMonitor] ALL save data cleared.");
                }
            }
            else
            {
                _confirmClearAll = false; // timed out
            }
        }
        else
        {
            if (GUILayout.Button("Clear All Save Data", _warningButton))
            {
                _confirmClearAll     = true;
                _confirmClearAllTime = EditorApplication.timeSinceStartup;
            }
        }

        EditorGUILayout.Space(8);
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private bool DrawSectionHeader(string label, bool foldout)
    {
        EditorGUILayout.BeginVertical(_sectionStyle);
        bool result = EditorGUILayout.Foldout(foldout, label, true, EditorStyles.foldoutHeader);
        EditorGUILayout.EndVertical();
        return result;
    }

    private void DrawRow(string key, string value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(key,   _keyStyle,   GUILayout.Width(180));
        EditorGUILayout.LabelField(value, _valueStyle, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
    }

    private void InjectRandomSouls(int count)
    {
        SoulData[] allSouls = Resources.LoadAll<SoulData>("Souls");

        if (allSouls.Length == 0)
        {
            Debug.LogWarning("[SaveDataMonitor] No SoulData assets found.");
            return;
        }

        var ids = new List<int>();
        for (int i = 0; i < count; i++)
        {
            SoulData picked = allSouls[Random.Range(0, allSouls.Length)];
            ids.Add(picked.soulDataIdentity);
        }

        List<int> existing = GameProgressData.GetSoulsOnBoatIdentities();
        existing.AddRange(ids);
        GameProgressData.SaveSoulsToBoat(existing);
        Debug.Log($"[SaveDataMonitor] Injected {ids.Count} soul(s): [{string.Join(", ", ids)}]");
    }

    // ─────────────────────────────────────────────
    // Data Loading
    // ─────────────────────────────────────────────

    private SaveData LoadData()
    {
        if (Application.isPlaying)
            return SaveManager.Load();

        // Edit-time: read directly from disk, bypassing SaveManager's runtime cache
        string path = System.IO.Path.Combine(Application.persistentDataPath, "save.json");
        if (!System.IO.File.Exists(path)) return new SaveData();
        try   { return JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(path)); }
        catch { return new SaveData(); }
    }

    // ─────────────────────────────────────────────
    // Style Builder
    // ─────────────────────────────────────────────

    private void BuildStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        _headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 12,
            alignment = TextAnchor.MiddleLeft
        };

        _sectionStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(4, 4, 2, 2),
            margin  = new RectOffset(0, 0, 2, 2)
        };

        _keyStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Bold,
            normal    = { textColor = new Color(0.7f, 0.85f, 1f) }
        };

        _valueStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true
        };

        _dangerButton = new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.75f, 0.15f, 0.1f)) },
            hover     = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.85f, 0.2f, 0.15f)) }
        };

        _warningButton = new GUIStyle(GUI.skin.button)
        {
            normal = { textColor = new Color(1f, 0.85f, 0.3f) }
        };

        _okButton = new GUIStyle(GUI.skin.button)
        {
            normal = { textColor = new Color(0.5f, 1f, 0.6f) }
        };
    }

    private static Texture2D MakeTex(int w, int h, Color col)
    {
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }
}
#endif