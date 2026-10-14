using UnityEditor;
using UnityEngine;

/// <summary>
/// The Light foldout, drawn in both the Run Shading Tuner and the Landscape Tuner.
///
/// Like the White Fade foldout it has no numbers of its own and no Load / Save: it edits the
/// scene's <see cref="LevelSelectLight"/> directly. The river runs, the hills and the spikes are
/// all turned against that one light, so both windows have to be looking at the same object — a
/// copy in each would let them disagree. The aesthetics pump re-pushes it every tick, so an edit
/// here, or moving the object in the scene view, is live at once.
/// </summary>
public static class LightTunerSection
{
    private const string PrefOpen = "LightTunerSection_Open";

    // Where a light stands if there is no world to ask — the default the designer data carries.
    private static readonly Vector3 FallbackPosition = new Vector3(362.3f, 259.8f, -3.2f);

    public static void Draw()
    {
        bool open = EditorGUILayout.Foldout(EditorPrefs.GetBool(PrefOpen, true),
                                            "Light (shared)", true, EditorStyles.foldoutHeader);
        EditorPrefs.SetBool(PrefOpen, open);
        if (!open) return;

        EditorGUILayout.LabelField(
            "The made-up light the river runs, the landscape and the spikes are all turned " +
            "against. It is the LevelSelectLight object in the scene: where it stands is where " +
            "the light comes from, and its Strength is the one strength all three share. This " +
            "foldout in the Run Shading Tuner and the Landscape Tuner edits the same object — " +
            "it is saved with the scene, not in a preset.",
            EditorStyles.wordWrappedMiniLabel);

        var light = LevelSelectLight.Current;
        if (light == null)
        {
            EditorGUILayout.HelpBox(
                "No LevelSelectLight in the scene, so the runs, hills and spikes are flat and " +
                "unlit.",
                MessageType.Warning);
            if (GUILayout.Button("Spawn Light")) Spawn();
            return;
        }

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Light", light, typeof(LevelSelectLight), true);

        EditorGUI.BeginChangeCheck();
        Vector3 position = EditorGUILayout.Vector3Field(
            new GUIContent("Position", "Where the light stands, in world space — the object's " +
                                       "position."),
            light.transform.position);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(light.transform, "Move Level Select Light");
            light.transform.position = position;
            Refresh();
        }

        var so = new SerializedObject(light);
        so.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(so.FindProperty("strength"));
        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            Refresh();
        }

        if (GUILayout.Button("Select Light Object")) Selection.activeGameObject = light.gameObject;
    }

    private static void Refresh()
    {
        LevelSelectLight.Push();
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Stands a new light under LEVELSELECT_SCRIPTS, where the world was lit from before the
    /// light became an object, so spawning it changes nothing on screen.
    /// </summary>
    private static void Spawn()
    {
        var data = TunerMaterialLinks.WorldData();

        var parent = GameObject.Find("LEVELSELECT_SCRIPTS");
        if (parent == null)
        {
            parent = new GameObject("LEVELSELECT_SCRIPTS");
            Undo.RegisterCreatedObjectUndo(parent, "Spawn Level Select Light");
        }

        var go = new GameObject("LevelSelectLight");
        Undo.RegisterCreatedObjectUndo(go, "Spawn Level Select Light");
        go.transform.SetParent(parent.transform, false);
        go.transform.position = data != null ? data.lightPosition : FallbackPosition;
        Undo.AddComponent<LevelSelectLight>(go);

        Selection.activeGameObject = go;
        Refresh();
    }
}
