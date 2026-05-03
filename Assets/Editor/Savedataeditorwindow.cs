// SaveDataEditorWindow.cs
// Place this file anywhere inside an Editor/ folder in your project.
// Open via: Tools > Save Data Monitor

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

public class SaveDataEditorWindow : EditorWindow
{
    // ─────────────────────────────────────────────
    // State
    // ─────────────────────────────────────────────

    private Vector2 _scroll;
    private bool    _confirmClearAll;
    private double  _confirmClearAllTime;
    private const double ConfirmTimeout = 3.0;

    private bool _showPreSession    = true;
    private bool _showLauncher     = true;
    private bool _showBoat         = true;
    private bool _showSouls        = true;
    private bool _showLevels       = true;
    private bool _showCaughtSouls  = true;
    private bool _showObstacles    = true;

    // Scene launcher
    private GridData[]  _allGridData;
    private string[]    _gridDataNames;
    private int         _selectedGridDataIndex;
    private int                _selectedSceneIndex;
    private string[]           _sceneNames;
    private WavePreset[]       _allWavePresets;
    private string[]           _wavePresetNames;
    private int                _selectedWavePresetIndex; // 0 = none

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

    private const string PrefScene      = "SaveDataMonitor_SceneName";
    private const string PrefGridData   = "SaveDataMonitor_GridDataName";
    private const string PrefWavePreset = "SaveDataMonitor_WavePresetName";

    // ─────────────────────────────────────────────
    // Open / Enable / Disable
    // ─────────────────────────────────────────────

    [MenuItem("Tools/Save Data Monitor #5")]
    public static void Open()
    {
        var window = GetWindow<SaveDataEditorWindow>("Save Data Monitor");
        window.minSize = new Vector2(380, 500);
    }

    private void OnEnable()
    {
        // Restore selections — indices are resolved after lists are populated in DrawSceneLauncherSection
    }

    private void OnDisable()
    {
        if (_sceneNames != null && _selectedSceneIndex < _sceneNames.Length)
            EditorPrefs.SetString(PrefScene, _sceneNames[_selectedSceneIndex]);

        if (_gridDataNames != null && _selectedGridDataIndex < _gridDataNames.Length)
            EditorPrefs.SetString(PrefGridData, _gridDataNames[_selectedGridDataIndex]);

        if (_wavePresetNames != null && _selectedWavePresetIndex < _wavePresetNames.Length)
            EditorPrefs.SetString(PrefWavePreset, _wavePresetNames[_selectedWavePresetIndex]);
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
        GUILayout.Label("Save Data Monitor", _headerStyle, GUILayout.ExpandWidth(true));

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
        DrawSceneLauncherSection();
        DrawBoatSection();
        DrawSoulsSection();
        DrawLevelCompletionsSection();
        DrawCaughtSoulsSection();
        DrawObstaclesSection();
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

    private void DrawSceneLauncherSection()
    {
        _showLauncher = DrawSectionHeader("▶  Scene Launcher", _showLauncher);
        if (!_showLauncher) return;

        // Populate scene list from build settings
        if (_sceneNames == null)
        {
            var scenes = UnityEditor.EditorBuildSettings.scenes;
            _sceneNames = new string[scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
                _sceneNames[i] = System.IO.Path.GetFileNameWithoutExtension(scenes[i].path);

            string savedScene = EditorPrefs.GetString(PrefScene, "");
            if (!string.IsNullOrEmpty(savedScene))
            {
                int idx = System.Array.IndexOf(_sceneNames, savedScene);
                if (idx >= 0) _selectedSceneIndex = idx;
            }
        }

        // Populate level grid data
        if (_allGridData == null)
            RefreshGridData();

        // Scene picker
        EditorGUILayout.LabelField("Scene", EditorStyles.miniLabel);
        if (_sceneNames.Length > 0)
            _selectedSceneIndex = EditorGUILayout.Popup(_selectedSceneIndex, _sceneNames);
        else
            EditorGUILayout.LabelField("  (no scenes in Build Settings)", EditorStyles.miniLabel);

        EditorGUILayout.Space(4);

        // GridData picker
        EditorGUILayout.LabelField("Level (gameplay only)", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        if (_allGridData != null && _gridDataNames != null && _gridDataNames.Length > 0)
            _selectedGridDataIndex = EditorGUILayout.Popup(_selectedGridDataIndex, _gridDataNames);
        else
            EditorGUILayout.LabelField("  (no GridData assets found in Resources/Levels/)", EditorStyles.miniLabel);

        if (GUILayout.Button("↺", GUILayout.Width(24)))
            RefreshGridData();
        EditorGUILayout.EndHorizontal();

        // Wave preset override picker
        EditorGUILayout.LabelField("Wave Preset Override (optional)", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        if (_allWavePresets == null) RefreshWavePresets();
        if (_wavePresetNames != null && _wavePresetNames.Length > 0)
            _selectedWavePresetIndex = EditorGUILayout.Popup(_selectedWavePresetIndex, _wavePresetNames);
        else
            EditorGUILayout.LabelField("  (no WavePreset assets found)", EditorStyles.miniLabel);
        if (GUILayout.Button("↺", GUILayout.Width(24)))
            RefreshWavePresets();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Play Scene", _okButton))
        {
            if (_sceneNames.Length > 0)
                LaunchScene(_sceneNames[_selectedSceneIndex], (GridData)null, null);
        }

        EditorGUI.BeginDisabledGroup(_allGridData == null || _allGridData.Length == 0);
        if (GUILayout.Button("Play Scene + Level", _okButton))
        {
            if (_sceneNames.Length > 0 && _allGridData != null && _allGridData.Length > 0)
            {
                WavePreset waveOverride = (_allWavePresets != null && _selectedWavePresetIndex > 0)
                    ? _allWavePresets[_selectedWavePresetIndex - 1]
                    : null;
                LaunchScene(_sceneNames[_selectedSceneIndex], _allGridData[_selectedGridDataIndex], waveOverride);
            }
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();
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

    private void RefreshWavePresets()
    {
        var guids = AssetDatabase.FindAssets("t:WavePreset");
        var list  = new List<WavePreset>();
        foreach (string guid in guids)
        {
            var preset = AssetDatabase.LoadAssetAtPath<WavePreset>(AssetDatabase.GUIDToAssetPath(guid));
            if (preset != null) list.Add(preset);
        }
        _allWavePresets  = list.ToArray();
        _wavePresetNames = new string[_allWavePresets.Length + 1];
        _wavePresetNames[0] = "(none)";
        for (int i = 0; i < _allWavePresets.Length; i++)
            _wavePresetNames[i + 1] = _allWavePresets[i].name;

        string savedPreset = EditorPrefs.GetString(PrefWavePreset, "");
        if (!string.IsNullOrEmpty(savedPreset))
        {
            int idx = System.Array.IndexOf(_wavePresetNames, savedPreset);
            if (idx >= 0) _selectedWavePresetIndex = idx;
        }
        _selectedWavePresetIndex = Mathf.Clamp(_selectedWavePresetIndex, 0, _wavePresetNames.Length - 1);
    }

    private void LaunchScene(string sceneName, GridData levelData, WavePreset waveOverride)
    {
        if (levelData != null)
        {
            string path = AssetDatabase.GetAssetPath(levelData);
            EditorPrefs.SetString("SaveDataMonitor_OverrideLevel", path);
            Debug.Log($"[SaveDataMonitor] Override level set: '{levelData.levelID}'");
        }

        if (waveOverride != null)
        {
            string path = AssetDatabase.GetAssetPath(waveOverride);
            EditorPrefs.SetString("SaveDataMonitor_OverrideWavePreset", path);
            Debug.Log($"[SaveDataMonitor] Override wave preset set: '{waveOverride.name}'");
        }

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