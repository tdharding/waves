using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelSelectCameraController))]
public class LevelSelectCameraControllerEditor : Editor
{
    private bool autoPreviewFollow = false;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        LevelSelectCameraController controller = (LevelSelectCameraController)target;

        // 1. Follow Pose — the shot the camera holds behind the boat under way
        EditorGUILayout.LabelField("Follow Pose Settings", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("followDistance"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("followHeight"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("followTargetVerticalOffset"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultZoom"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("followCatchUpTime"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultVerticalFOV"));
        bool followSettingsChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();

        // 1b2. Downhill Rise — the camera lifting over a drop
        EditorGUILayout.LabelField("Downhill Rise Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("downhillRise"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("downhillRiseFullSlope"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("downhillRiseTime"));

        EditorGUILayout.Space();

        // 1c. Interact Lean — the glance the camera gives a point it is passing
        EditorGUILayout.LabelField("Interact Lean Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("interactLeanEnabled"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("interactLean"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxInteractLeanAngle"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("interactLeanStayBehind"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("interactLeanTime"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("interactLeanZoom"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("interactLeanVertical"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxInteractLeanPitch"));

        EditorGUILayout.Space();

        // 2. Other Properties
        EditorGUILayout.LabelField("Base Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cam"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("minDistance"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxDistance"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("zoomSpeed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("boatControl"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("orbitSpeed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pitchMin"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pitchMax"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lockCursorWhileOrbiting"));

        EditorGUILayout.Space();

        // 2b. Low Angle Zoom + Height Floor — keeping the Q orbit out of the water
        EditorGUILayout.LabelField("Low Angle Zoom (While Anchored)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lowAngleZoomEnabled"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lowAngleZoomStartHeight"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lowAngleZoomDistance"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lowAngleZoomFollowOffset"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lowAngleZoomSmoothing"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("limitCameraHeight"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("heightFloor"));

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("previewTarget"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("previewOrigin"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Editor Tools", EditorStyles.boldLabel);

        // Preview Buttons & Toggles
        EditorGUILayout.BeginHorizontal();
        autoPreviewFollow = EditorGUILayout.ToggleLeft("Auto Preview Follow", autoPreviewFollow, GUILayout.Width(150));
        if (GUILayout.Button("Preview Follow") || (autoPreviewFollow && followSettingsChanged))
        {
            controller.EditorPreviewFollow();
            MarkDirty(controller);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("Shows the resting shot behind the boat. Opening shots belong to Camera Sequences, which hand back to this.", MessageType.Info);
        
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
