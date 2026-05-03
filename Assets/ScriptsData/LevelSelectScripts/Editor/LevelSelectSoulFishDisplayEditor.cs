using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelSelectSoulFishDisplay))]
public class LevelSelectSoulFishDisplayEditor : Editor
{
    private int _previewCount = 3;

    private static void DestroyPreviewChildren(LevelSelectSoulFishDisplay display)
    {
        if (display.spawnRoot == null) return;

        var swimmers = display.spawnRoot.GetComponentsInChildren<DisplayFishSwimmer>();
        foreach (var s in swimmers)
            if (s != null) DestroyImmediate(s.gameObject);

        var paths = display.spawnRoot.GetComponentsInChildren<DisplayFishPath>();
        foreach (var p in paths)
            if (p != null) DestroyImmediate(p.gameObject);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var display = (LevelSelectSoulFishDisplay)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        _previewCount = EditorGUILayout.IntSlider(
            new GUIContent("Test Fish Count", "How many fish to spawn for the preview."),
            _previewCount, 1, display.maxDisplayFish);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Start Preview"))
        {
            DestroyPreviewChildren(display);
            display.StartPreview(_previewCount);
            SceneView.RepaintAll();
        }

        GUI.color = new Color(1f, 0.7f, 0.7f);
        if (GUILayout.Button("Stop Preview"))
        {
            DestroyPreviewChildren(display);
            display.StopPreview();
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Reset"))
        {
            DestroyPreviewChildren(display);
            SceneView.RepaintAll();
        }
        GUI.color = Color.white;

        EditorGUILayout.EndHorizontal();
    }
}
