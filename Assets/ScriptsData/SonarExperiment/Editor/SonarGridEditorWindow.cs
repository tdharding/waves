using UnityEditor;
using UnityEngine;

public class SonarGridEditorWindow : EditorWindow
{
    SonarGridType    _asset;
    SerializedObject _so;

    // scratch fields
    int   _columns              = 12;
    int   _rows                 = 12;
    int   _levels               = 4;
    int   _linesPerCell         = 1;
    int   _subdivisions         = 4;
    float _surfaceDepthOffset   = -0.5f;
    float _levelSpacingMult     = 1f;
    bool  _spawnVertical        = true;
    bool  _spawnCrossVertical   = true;
    Material _planeMaterial;

    // preview values (not saved to asset — live on SonarController)
    float _previewCoverage     = 10f;
    float _previewVerticalArea = 5f;

    static readonly Color AccentColour = new Color(0f, 0.85f, 0.9f, 1f);
    static readonly Color PanelColour  = new Color(0.16f, 0.16f, 0.16f, 1f);
    GUIStyle _headerStyle;
    GUIStyle _statStyle;
    bool     _stylesInit;
    Vector2  _scroll;

    [MenuItem("Window/Sonar/Grid Editor")]
    static void Open() => GetWindow<SonarGridEditorWindow>("Sonar Grid Editor");

    public static void OpenWith(SonarGridType asset)
    {
        var win = GetWindow<SonarGridEditorWindow>("Sonar Grid Editor");
        win.LoadAsset(asset);
    }

    void OnEnable()  => Undo.undoRedoPerformed += Repaint;
    void OnDisable() => Undo.undoRedoPerformed -= Repaint;

    void OnGUI()
    {
        InitStyles();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawAssetHeader();
        EditorGUILayout.Space(4);

        if (_asset != null) DrawAssetFields();
        else                DrawScratchFields();

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
            EditorGUILayout.HelpBox("Working in scratch mode. Save as a new asset when ready.", MessageType.Info);
    }

    // ── asset-backed fields ───────────────────────────────────────────────────

    void DrawAssetFields()
    {
        if (_so == null || _so.targetObject == null) { LoadAsset(_asset); return; }
        _so.Update();

        Section("Grid Formation");
        Field("columns", "Columns",  "Tile columns across X");
        Field("rows",    "Rows",     "Tile rows across Z");
        Field("levels",  "Levels",   "Horizontal plane levels on Y");

        Section("Appearance");
        Field("linesPerCell",  "Lines Per Cell", "Visual grid lines per tile (1 = aligned with tile edges)");
        Field("subdivisions",  "Subdivisions",   "Vertex mesh density per tile");

        Section("Depth Placement");
        Field("surfaceDepthOffset", "Surface Depth Offset", "Offset from wave surface to top of stack");
        EditorGUILayout.HelpBox("Level spacing is derived from Vertical Grid Area ÷ levels (set on SonarController).", MessageType.None);

        Section("Vertical Planes");
        Field("spawnVertical",      "XY-facing  (⊥ Z)");
        Field("spawnCrossVertical", "YZ-facing  (⊥ X)");

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
        Section("Grid Formation");
        _columns = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Columns",  "Tile columns across X"), _columns));
        _rows    = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Rows",     "Tile rows across Z"),    _rows));
        _levels  = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Levels",   "Horizontal plane levels on Y"), _levels));

        Section("Appearance");
        _linesPerCell = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Lines Per Cell", "Visual grid lines per tile"), _linesPerCell));
        _subdivisions = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Subdivisions",   "Vertex mesh density per tile"), _subdivisions));

        Section("Depth Placement");
        _surfaceDepthOffset = EditorGUILayout.FloatField(new GUIContent("Surface Depth Offset", "Offset from wave surface to top of stack"), _surfaceDepthOffset);

        Section("Vertical Planes");
        _spawnVertical      = EditorGUILayout.Toggle("XY-facing  (⊥ Z)", _spawnVertical);
        _spawnCrossVertical = EditorGUILayout.Toggle("YZ-facing  (⊥ X)", _spawnCrossVertical);

        Section("Material");
        _planeMaterial = (Material)EditorGUILayout.ObjectField("Material", _planeMaterial, typeof(Material), false);
    }

    // ── stats panel ───────────────────────────────────────────────────────────

    void DrawStats()
    {
        int   cols    = _asset != null ? _so.FindProperty("columns").intValue : _columns;
        int   rowCount = _asset != null ? _so.FindProperty("rows").intValue   : _rows;
        int   levels  = _asset != null ? _so.FindProperty("levels").intValue  : _levels;
        bool  vert  = _asset != null ? _so.FindProperty("spawnVertical").boolValue      : _spawnVertical;
        bool  cross = _asset != null ? _so.FindProperty("spawnCrossVertical").boolValue : _spawnCrossVertical;

        cols     = Mathf.Max(1, cols);
        rowCount = Mathf.Max(1, rowCount);
        levels   = Mathf.Max(1, levels);

        // Preview inputs (not saved — live on SonarController)
        EditorGUILayout.Space(2);
        _previewCoverage     = Mathf.Max(0.1f, EditorGUILayout.FloatField(
            new GUIContent("Preview H. Grid Area", "Match SonarController Horizontal Grid Area"), _previewCoverage));
        _previewVerticalArea = Mathf.Max(0.1f, EditorGUILayout.FloatField(
            new GUIContent("Preview V. Grid Area", "Match SonarController Vertical Grid Area"),  _previewVerticalArea));

        int   maxAxis      = Mathf.Max(1, cols, rowCount);
        float cellSize     = _previewCoverage / maxAxis;
        float levelSpacing = _previewVerticalArea / levels;
        float stackHeight  = (levels - 1) * levelSpacing + cellSize;

        int poolSize = cols * rowCount * levels;
        int vTotal   = vert  ? poolSize : 0;
        int xTotal   = cross ? poolSize : 0;

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = PanelColour;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = prevBg;

        GUILayout.Label("Formation preview", _headerStyle);
        EditorGUILayout.LabelField($"Cell size       {cellSize:F3} world units", _statStyle);
        EditorGUILayout.LabelField($"Level spacing   {levelSpacing:F3} world units", _statStyle);
        EditorGUILayout.LabelField($"Stack height    {stackHeight:F3} world units", _statStyle);
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField($"Tile pools   H:{poolSize}  V:{vTotal}  X:{xTotal}  =  {poolSize + vTotal + xTotal} total", _statStyle);

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
            if (GUILayout.Button("Save", GUILayout.Height(32))) Save();
        }

        if (GUILayout.Button("Save As", GUILayout.Height(32))) SaveAs();

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
            _columns            = asset.columns;
            _rows               = asset.rows;
            _levels             = asset.levels;
            _linesPerCell       = asset.linesPerCell;
            _subdivisions       = asset.subdivisions;
            _surfaceDepthOffset = asset.surfaceDepthOffset;
            _spawnVertical      = asset.spawnVertical;
            _spawnCrossVertical = asset.spawnCrossVertical;
            _planeMaterial      = asset.planeMaterial;
        }

        Repaint();
    }

    void UnloadToScratch()
    {
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
            "Save Sonar Grid Type", defaultName, "asset",
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
            asset.columns              = _so.FindProperty("columns").intValue;
            asset.rows                 = _so.FindProperty("rows").intValue;
            asset.levels               = _so.FindProperty("levels").intValue;
            asset.linesPerCell         = _so.FindProperty("linesPerCell").intValue;
            asset.subdivisions         = _so.FindProperty("subdivisions").intValue;
            asset.surfaceDepthOffset   = _so.FindProperty("surfaceDepthOffset").floatValue;
            asset.spawnVertical        = _so.FindProperty("spawnVertical").boolValue;
            asset.spawnCrossVertical   = _so.FindProperty("spawnCrossVertical").boolValue;
            asset.planeMaterial        = _asset.planeMaterial;
        }
        else
        {
            asset.columns              = _columns;
            asset.rows                 = _rows;
            asset.levels               = _levels;
            asset.linesPerCell         = _linesPerCell;
            asset.subdivisions         = _subdivisions;
            asset.surfaceDepthOffset   = _surfaceDepthOffset;
            asset.spawnVertical        = _spawnVertical;
            asset.spawnCrossVertical   = _spawnCrossVertical;
            asset.planeMaterial        = _planeMaterial;
        }
    }

    // ── style helpers ─────────────────────────────────────────────────────────

    void InitStyles()
    {
        if (_stylesInit) return;
        _headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal   = { textColor = Color.white }
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
}
