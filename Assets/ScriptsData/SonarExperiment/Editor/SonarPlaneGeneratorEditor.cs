using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SonarPlaneGenerator))]
public class SonarPlaneGeneratorEditor : Editor
{
    SerializedProperty _gridType;

    static readonly Color AccentColour = new Color(0f, 0.85f, 0.9f, 1f);
    static readonly Color PanelColour  = new Color(0.18f, 0.18f, 0.18f, 1f);
    GUIStyle _statStyle;
    bool _stylesInit;

    void OnEnable() => _gridType = serializedObject.FindProperty("gridType");

    void InitStyles()
    {
        if (_stylesInit) return;
        _statStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 10,
            normal   = { textColor = new Color(0.6f, 0.9f, 0.9f, 1f) }
        };
        _stylesInit = true;
    }

    public override void OnInspectorGUI()
    {
        InitStyles();
        serializedObject.Update();

        var gen = (SonarPlaneGenerator)target;

        // ── grid type slot ────────────────────────────────────────────────────
        Section("Grid Type");
        EditorGUILayout.PropertyField(_gridType, new GUIContent("Sonar Grid Type"));
        EditorGUILayout.Space(2);

        var gt = gen.GridType;
        if (gt == null)
        {
            EditorGUILayout.HelpBox("Assign a SonarGridType asset to generate tiles.", MessageType.Warning);
        }
        else
        {
            // read-only stat summary
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = PanelColour;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = prevBg;

            int  tilesAxis = gt.TilesPerAxis;
            int  perLevel  = tilesAxis * tilesAxis;
            int  hTotal    = perLevel * gt.hLevels;
            int  vTotal    = gt.spawnVertical      ? hTotal : 0;
            int  xTotal    = gt.spawnCrossVertical ? hTotal : 0;

            EditorGUILayout.LabelField($"Tiles per axis   {tilesAxis}  ×  {gt.hLevels} levels", _statStyle);
            EditorGUILayout.LabelField($"Renderers  H:{hTotal}  V:{vTotal}  X:{xTotal}  =  {hTotal + vTotal + xTotal}", _statStyle);
            EditorGUILayout.LabelField($"Cell {gt.cellSize:F2}u   Density {gt.gridDensity}   Subs {gt.subdivisions}", _statStyle);

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(6);

        // ── buttons ───────────────────────────────────────────────────────────
        var prevCol = GUI.color;
        GUI.color = AccentColour;

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Generate Tiles", GUILayout.Height(28)))
        {
            Undo.RecordObject(gen, "Generate Sonar Tiles");
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        if (GUILayout.Button("Open Grid Editor", GUILayout.Height(28), GUILayout.Width(130)))
        {
            if (gt != null)
                SonarGridEditorWindow.OpenWith(gt);
            else
                EditorWindow.GetWindow<SonarGridEditorWindow>("Sonar Grid Editor");
        }

        EditorGUILayout.EndHorizontal();
        GUI.color = prevCol;

        serializedObject.ApplyModifiedProperties();
    }

    static void Section(string label)
    {
        EditorGUILayout.Space(2);
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0f, 0.85f, 0.9f, 0.4f));
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
    }
}
