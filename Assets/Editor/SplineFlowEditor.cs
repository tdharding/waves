using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

public class SplineFlowEditor : EditorWindow
{
    private SplineContainer _target;

    [MenuItem("Tools/Spline Flow Flipper")]
    public static void OpenWindow()
    {
        GetWindow<SplineFlowEditor>("Spline Flow Flipper");
    }

    private void OnGUI()
    {
        GUILayout.Label("Spline Flow Flipper", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _target = (SplineContainer)EditorGUILayout.ObjectField(
            "Spline Container", _target, typeof(SplineContainer), true
        );

        EditorGUILayout.Space();

        GUI.enabled = _target != null;

        if (GUILayout.Button("Flip Flow", GUILayout.Height(40)))
        {
            Undo.RecordObject(_target, "Flip Spline Flow");
            SplineUtility.ReverseFlow(_target.Spline);
            EditorUtility.SetDirty(_target);
        }

        GUI.enabled = true;

        if (_target == null)
            EditorGUILayout.HelpBox("Assign a SplineContainer to flip.", MessageType.Info);
    }
}