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
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = PanelColour;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = prevBg;

            // One merged mesh per upright axis, plus one renderer per horizontal level.
            int vWalls   = gt.spawnVertical      ? gen.Rows    + 1 : 0;
            int xWalls   = gt.spawnCrossVertical ? gen.Columns + 1 : 0;
            int renderers = gen.Levels
                          + (gt.spawnVertical      ? 1 : 0)
                          + (gt.spawnCrossVertical ? 1 : 0);

            EditorGUILayout.LabelField($"Formation   {gen.Columns} cols × {gen.Rows} rows × {gen.Levels} levels", _statStyle);
            EditorGUILayout.LabelField($"Cell size   {gen.CellSize:F3}u  (set by SonarController)", _statStyle);
            EditorGUILayout.LabelField($"Footprint   {gen.Width:F2} × {gen.Depth:F2}u  ·  depth {gen.StackHeight:F2}u", _statStyle);
            EditorGUILayout.LabelField($"Walls       Z:{vWalls}  X:{xWalls}  (merged)", _statStyle);
            EditorGUILayout.LabelField($"Renderers   {renderers}", _statStyle);

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(6);

        var prevCol = GUI.color;
        GUI.color = AccentColour;

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Generate Tiles", GUILayout.Height(28)))
        {
            Undo.RecordObject(gen, "Generate Sonar Tiles");
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        GUI.color = new Color(1f, 0.4f, 0.4f, 1f);
        if (GUILayout.Button("Clear Tiles", GUILayout.Height(28), GUILayout.Width(90)))
        {
            Undo.RecordObject(gen, "Clear Sonar Tiles");
            gen.Clear();
            EditorUtility.SetDirty(gen);
        }
        GUI.color = AccentColour;

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
