using UnityEditor;
using UnityEngine;

public class SonarGridEditorWindow : EditorWindow
{
    // ── state ─────────────────────────────────────────────────────────────────
    SonarGridType _asset;           // currently loaded asset (null = scratch)
    SerializedObject _so;

    // scratch fields (used when no asset is loaded)
    float _viewRadius      = 10f;
    float _cellSize        = 2f;
    int   _gridDensity     = 1;
    int   _subdivisions    = 4;
    float _horizontalY     = 0f;
    int   _hLevels         = 4;
    float _hLevelSpacing   = 1.5f;
    bool  _spawnVertical      = true;
    bool  _spawnCrossVertical = true;
    Material _planeMaterial;

    // ui
    static readonly Color AccentColour = new Color(0f, 0.85f, 0.9f, 1f);
    static readonly Color PanelColour  = new Color(0.16f, 0.16f, 0.16f, 1f);
    GUIStyle _headerStyle;
    GUIStyle _statStyle;
    bool _stylesInit;
    Vector2 _scroll;

    // ── menu entry ────────────────────────────────────────────────────────────
    [MenuItem("Window/Sonar/Grid Editor")]
    static void Open() => GetWindow<SonarGridEditorWindow>("Sonar Grid Editor");

    public static void OpenWith(SonarGridType asset)
    {
        var win = GetWindow<SonarGridEditorWindow>("Sonar Grid Editor");
        win.LoadAsset(asset);
    }

    // ── lifecycle ─────────────────────────────────────────────────────────────

    void OnEnable()  => Undo.undoRedoPerformed += Repaint;
    void OnDisable() => Undo.undoRedoPerformed -= Repaint;

    void OnGUI()
    {
        InitStyles();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawAssetHeader();
        EditorGUILayout.Space(4);

        if (_asset != null)
            DrawAssetFields();
        else
            DrawScratchFields();

        EditorGUILayout.Space(6);
        DrawStats();
        EditorGUILayout.Space(6);
        DrawActions();

        EditorGUILayout.EndScrollView();
    }

    // ── asset header ──────────────────────────────────────────────────────────

    void DrawAssetHeader()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();

        var prev = GUI.color;
        GUI.color = AccentColour;
        GUILayout.Label("Sonar Grid Editor", EditorStyles.whiteLargeLabel, GUILayout.ExpandWidth(true));
        GUI.color = prev;

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        var newAsset = (SonarGridType)EditorGUILayout.ObjectField(
            _asset != null ? "Editing" : "Load Asset",
            _asset, typeof(SonarGridType), false);
        if (newAsset != _asset) LoadAsset(newAsset);

        if (_asset != null && GUILayout.Button("Unload", GUILayout.Width(60)))
            UnloadToScratch();
        EditorGUILayout.EndHorizontal();

        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0f, 0.85f, 0.9f, 0.4f));
        EditorGUILayout.Space(2);

        if (_asset == null)
        {
            EditorGUILayout.HelpBox("Working in scratch mode. Save as a new asset when ready.", MessageType.Info);
        }
    }

    // ── asset-backed fields (via SerializedObject) ────────────────────────────

    void DrawAssetFields()
    {
        if (_so == null || _so.targetObject == null) { LoadAsset(_asset); return; }
        _so.Update();

        Section("Coverage");
        Field("viewRadius",      "View Radius",    "World-unit radius around the boat that tiles cover");

        Section("Grid");
        Field("cellSize",        "Cell Size",      "World size of each tile");
        Field("gridDensity",     "Grid Density",   "Visible grid lines per tile");
        Field("subdivisions",    "Subdivisions",   "Mesh vertex density per tile");

        float safeCell  = Mathf.Max(0.001f, _so.FindProperty("cellSize").floatValue);
        int   density   = Mathf.Max(1, _so.FindProperty("gridDensity").intValue);
        float viewR     = _so.FindProperty("viewRadius").floatValue;
        DrawDerivedGridStats(safeCell, density, viewR);

        Section("Horizontal Planes");
        Field("horizontalY",     "Centre Y",       "World Y of the middle horizontal level");
        Field("hLevels",         "Levels",         "Number of stacked horizontal layers");
        Field("hLevelSpacing",   "Level Spacing",  "Y gap between layers");

        int   levels = Mathf.Max(1, _so.FindProperty("hLevels").intValue);
        float centY  = _so.FindProperty("horizontalY").floatValue;
        float spac   = _so.FindProperty("hLevelSpacing").floatValue;
        DrawDerivedLevelStats(centY, levels, spac);

        Section("Vertical Planes");
        Field("spawnVertical",      "XY-facing  (⊥ Z lines)");
        Field("spawnCrossVertical", "YZ-facing  (⊥ X lines)");

        Section("Material");
        Field("planeMaterial", "Material");

        _so.ApplyModifiedProperties();
    }

    void Field(string propName, string label, string tooltip = "")
    {
        var prop = _so.FindProperty(propName);
        if (prop != null)
            EditorGUILayout.PropertyField(prop,
                tooltip.Length > 0 ? new GUIContent(label, tooltip) : new GUIContent(label));
    }

    // ── scratch fields ────────────────────────────────────────────────────────

    void DrawScratchFields()
    {
        Section("Coverage");
        _viewRadius = EditorGUILayout.FloatField(new GUIContent("View Radius", "World-unit radius around the boat that tiles cover"), _viewRadius);
        _viewRadius = Mathf.Max(0.1f, _viewRadius);

        Section("Grid");
        _cellSize    = Mathf.Max(0.01f, EditorGUILayout.FloatField(new GUIContent("Cell Size",    "World size of each tile"),              _cellSize));
        _gridDensity = Mathf.Max(1,     EditorGUILayout.IntField  (new GUIContent("Grid Density", "Visible grid lines per tile"),          _gridDensity));
        _subdivisions= Mathf.Max(1,     EditorGUILayout.IntField  (new GUIContent("Subdivisions", "Mesh vertex density per tile"),         _subdivisions));
        DrawDerivedGridStats(_cellSize, _gridDensity, _viewRadius);

        Section("Horizontal Planes");
        _horizontalY   = EditorGUILayout.FloatField(new GUIContent("Centre Y",      "World Y of the middle horizontal level"),  _horizontalY);
        _hLevels       = Mathf.Max(1,    EditorGUILayout.IntField  (new GUIContent("Levels",        "Number of stacked horizontal layers"),  _hLevels));
        _hLevelSpacing = Mathf.Max(0.01f,EditorGUILayout.FloatField(new GUIContent("Level Spacing", "Y gap between layers"),                _hLevelSpacing));
        DrawDerivedLevelStats(_horizontalY, _hLevels, _hLevelSpacing);

        Section("Vertical Planes");
        _spawnVertical      = EditorGUILayout.Toggle("XY-facing  (⊥ Z lines)", _spawnVertical);
        _spawnCrossVertical = EditorGUILayout.Toggle("YZ-facing  (⊥ X lines)", _spawnCrossVertical);

        Section("Material");
        _planeMaterial = (Material)EditorGUILayout.ObjectField("Material", _planeMaterial, typeof(Material), false);
    }

    // ── derived stats ─────────────────────────────────────────────────────────

    void DrawDerivedGridStats(float cellSize, int density, float viewRadius)
    {
        float lineSpacing = cellSize / Mathf.Max(1, density);
        int   tilesAxis   = 2 * Mathf.Max(1, Mathf.CeilToInt(viewRadius / Mathf.Max(0.001f, cellSize))) + 1;
        using (new EditorGUI.IndentLevelScope())
        {
            StatLine($"Line spacing    {lineSpacing:F2} world units");
            StatLine($"Tiles per axis  {tilesAxis}  ({tilesAxis}×{tilesAxis} = {tilesAxis * tilesAxis} per level)");
        }
        EditorGUILayout.Space(4);
    }

    void DrawDerivedLevelStats(float centY, int levels, float spacing)
    {
        float topY = centY + (levels - 1) * spacing * 0.5f;
        float botY = centY - (levels - 1) * spacing * 0.5f;
        using (new EditorGUI.IndentLevelScope())
            StatLine($"Y range  {botY:F2}  →  {topY:F2}");
        EditorGUILayout.Space(4);
    }

    // ── stats panel ───────────────────────────────────────────────────────────

    void DrawStats()
    {
        float viewR    = _asset != null ? _so.FindProperty("viewRadius").floatValue    : _viewRadius;
        float cell     = _asset != null ? _so.FindProperty("cellSize").floatValue      : _cellSize;
        int   density  = _asset != null ? _so.FindProperty("gridDensity").intValue     : _gridDensity;
        int   levels   = _asset != null ? _so.FindProperty("hLevels").intValue         : _hLevels;
        bool  vert     = _asset != null ? _so.FindProperty("spawnVertical").boolValue  : _spawnVertical;
        bool  cross    = _asset != null ? _so.FindProperty("spawnCrossVertical").boolValue : _spawnCrossVertical;

        int tilesAxis  = 2 * Mathf.Max(1, Mathf.CeilToInt(viewR / Mathf.Max(0.001f, cell))) + 1;
        int perLevel   = tilesAxis * tilesAxis;
        int hTotal     = perLevel * Mathf.Max(1, levels);
        int vTotal     = vert  ? hTotal : 0;
        int xTotal     = cross ? hTotal : 0;
        int grand      = hTotal + vTotal + xTotal;

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = PanelColour;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = prevBg;

        GUILayout.Label("Tile counts", _headerStyle);
        EditorGUILayout.LabelField($"Horizontal      {hTotal}  ({tilesAxis}×{tilesAxis} × {levels} levels)", _statStyle);
        EditorGUILayout.LabelField($"Vertical (XY)   {vTotal}", _statStyle);
        EditorGUILayout.LabelField($"Vertical (YZ)   {xTotal}", _statStyle);
        EditorGUILayout.LabelField($"Total renderers {grand}", _statStyle);

        EditorGUILayout.EndVertical();
    }

    // ── action buttons ────────────────────────────────────────────────────────

    void DrawActions()
    {
        var prevColour = GUI.color;
        GUI.color = AccentColour;

        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(_asset == null))
        {
            if (GUILayout.Button("Save", GUILayout.Height(32)))
                Save();
        }

        if (GUILayout.Button("Save As", GUILayout.Height(32)))
            SaveAs();

        if (_asset != null)
        {
            GUI.color = new Color(0.3f, 0.9f, 0.6f, 1f);
            if (GUILayout.Button("Ping Asset", GUILayout.Height(32), GUILayout.Width(110)))
                EditorGUIUtility.PingObject(_asset);
        }

        EditorGUILayout.EndHorizontal();

        GUI.color = prevColour;
    }

    // ── asset operations ──────────────────────────────────────────────────────

    void LoadAsset(SonarGridType asset)
    {
        _asset = asset;
        _so    = asset != null ? new SerializedObject(asset) : null;

        if (asset != null)
        {
            // mirror into scratch in case user unloads
            _viewRadius      = asset.viewRadius;
            _cellSize        = asset.cellSize;
            _gridDensity     = asset.gridDensity;
            _subdivisions    = asset.subdivisions;
            _horizontalY     = asset.horizontalY;
            _hLevels         = asset.hLevels;
            _hLevelSpacing   = asset.hLevelSpacing;
            _spawnVertical   = asset.spawnVertical;
            _spawnCrossVertical = asset.spawnCrossVertical;
            _planeMaterial   = asset.planeMaterial;
        }

        Repaint();
    }

    void UnloadToScratch()
    {
        // scratch fields already mirrored in LoadAsset
        _asset = null;
        _so    = null;
        Repaint();
    }

    void Save()
    {
        if (_asset == null) return;
        EditorUtility.SetDirty(_asset);
        AssetDatabase.SaveAssets();
    }

    void SaveAs()
    {
        string defaultName = _asset != null ? _asset.name : "NewSonarGridType";
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Sonar Grid Type",
            defaultName,
            "asset",
            "Choose where to save the SonarGridType asset",
            "Assets/ScriptsData/DataScripts/SonarGridType");

        if (string.IsNullOrEmpty(path)) return;

        var asset = CreateInstance<SonarGridType>();
        WriteCurrentValuesToAsset(asset);
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(asset);
        LoadAsset(asset);
    }

    void WriteCurrentValuesToAsset(SonarGridType asset)
    {
        if (_asset != null && _so != null)
        {
            // copy from the currently edited asset fields
            asset.viewRadius      = _so.FindProperty("viewRadius").floatValue;
            asset.cellSize        = _so.FindProperty("cellSize").floatValue;
            asset.gridDensity     = _so.FindProperty("gridDensity").intValue;
            asset.subdivisions    = _so.FindProperty("subdivisions").intValue;
            asset.horizontalY     = _so.FindProperty("horizontalY").floatValue;
            asset.hLevels         = _so.FindProperty("hLevels").intValue;
            asset.hLevelSpacing   = _so.FindProperty("hLevelSpacing").floatValue;
            asset.spawnVertical   = _so.FindProperty("spawnVertical").boolValue;
            asset.spawnCrossVertical = _so.FindProperty("spawnCrossVertical").boolValue;
            asset.planeMaterial   = _asset.planeMaterial;
        }
        else
        {
            asset.viewRadius      = _viewRadius;
            asset.cellSize        = _cellSize;
            asset.gridDensity     = _gridDensity;
            asset.subdivisions    = _subdivisions;
            asset.horizontalY     = _horizontalY;
            asset.hLevels         = _hLevels;
            asset.hLevelSpacing   = _hLevelSpacing;
            asset.spawnVertical   = _spawnVertical;
            asset.spawnCrossVertical = _spawnCrossVertical;
            asset.planeMaterial   = _planeMaterial;
        }
    }

    // ── style helpers ─────────────────────────────────────────────────────────

    void InitStyles()
    {
        if (_stylesInit) return;
        _headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal = { textColor = Color.white }
        };
        _statStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 10,
            normal   = { textColor = new Color(0.6f, 0.9f, 0.9f, 1f) }
        };
        _stylesInit = true;
    }

    void Section(string label)
    {
        EditorGUILayout.Space(2);
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0f, 0.85f, 0.9f, 0.4f));
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
    }

    void StatLine(string text) => EditorGUILayout.LabelField(text, _statStyle);
}
