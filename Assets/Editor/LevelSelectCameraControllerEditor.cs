using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelSelectCameraController))]
public class LevelSelectCameraControllerEditor : Editor
{
    private bool autoPreviewStart = false;
    private bool autoPreviewTransition = false;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        LevelSelectCameraController controller = (LevelSelectCameraController)target;

        // 1. Starting State Section
        EditorGUILayout.LabelField("Starting State Settings", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useManualStartingState"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("manualStartYaw"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("manualStartPitch"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("manualStartDistance"));
        bool startSettingsChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();

        // 2. Transition Section
        EditorGUILayout.LabelField("Transition Target Settings", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionYaw"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionPitch"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionDistance"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionDuration"));
        bool transitionSettingsChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();

        // 3. Other Properties
        EditorGUILayout.LabelField("Base Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cam"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("followDistance"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("minDistance"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxDistance"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("zoomSpeed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("orbitSpeed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pitchMin"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pitchMax"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("previewTarget"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("previewOrigin"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Editor Tools", EditorStyles.boldLabel);

        // Preview Buttons & Toggles
        EditorGUILayout.BeginHorizontal();
        autoPreviewStart = EditorGUILayout.ToggleLeft("Auto Preview Start", autoPreviewStart, GUILayout.Width(150));
        if (GUILayout.Button("Preview Start") || (autoPreviewStart && startSettingsChanged))
        {
            controller.EditorPreview();
            MarkDirty(controller);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        autoPreviewTransition = EditorGUILayout.ToggleLeft("Auto Preview Transition", autoPreviewTransition, GUILayout.Width(150));
        if (GUILayout.Button("Preview Transition") || (autoPreviewTransition && transitionSettingsChanged))
        {
            controller.EditorPreviewTransition();
            MarkDirty(controller);
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.HelpBox("Use these tools to visualize where the camera starts and where it ends up after the transition trigger.", MessageType.Info);
        
        serializedObject.ApplyModifiedProperties();
    }

    private void MarkDirty(LevelSelectCameraController controller)
    {
        if (controller.cam != null)
        {
            EditorUtility.SetDirty(controller.cam);
            EditorUtility.SetDirty(controller);
        }
        SceneView.RepaintAll();
    }
}
