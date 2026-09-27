using UnityEditor;
using UnityEngine;

/// <summary>
/// The White Fade foldout, drawn in both the Run Shading Tuner and the Landscape Tuner.
///
/// Unlike the rest of either tuner it has no numbers of its own and no Load / Save: it edits the
/// world's <see cref="LevelSelectWhiteFadePreset"/> directly. The fade is one set of numbers
/// shared by the runs, the hills and the spikes, so both windows have to be looking at the same
/// thing — a copy in each would let them disagree about which one the world is wearing. The
/// aesthetics pump re-pushes the preset every tick, so an edit here is live at once, in or out of
/// play mode.
/// </summary>
public static class WhiteFadeTunerSection
{
    private const string PrefOpen = "WhiteFadeTunerSection_Open";

    private static LevelSelectDataController _controller;

    public static void Draw()
    {
        bool open = EditorGUILayout.Foldout(EditorPrefs.GetBool(PrefOpen, true),
                                            "White Fade (shared)", true, EditorStyles.foldoutHeader);
        EditorPrefs.SetBool(PrefOpen, open);
        if (!open) return;

        EditorGUILayout.LabelField(
            "Everything washed towards white out from the boat, in two rings — the outer out to " +
            "Radius, the inner out to Radius / Inner Division — and a white gradient rising from " +
            "below Gradient Bottom to world Y = 1. One set of numbers for the river " +
            "runs, the landscape and the spikes: this foldout in the Run Shading Tuner and the " +
            "Landscape Tuner edits the same preset, the one the Level Select Designer holds. " +
            "Edits land on it straight away — there is nothing to save.",
            EditorStyles.wordWrappedMiniLabel);

        var data = WorldData();
        if (data == null)
        {
            EditorGUILayout.HelpBox(
                "No Level Select Designer data found — open the Level Select Designer, or a " +
                "scene with a LevelSelectDataController in it.",
                MessageType.Info);
            return;
        }

        var preset = data.whiteFadePreset;
        if (preset == null)
        {
            EditorGUILayout.HelpBox(
                "This world holds no White Fade Preset, so nothing fades. Pick one under " +
                "Aesthetics in the Level Select Designer.",
                MessageType.Warning);
            return;
        }

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Preset", preset, typeof(LevelSelectWhiteFadePreset), false);

        var so = new SerializedObject(preset);
        so.Update();

        EditorGUI.BeginChangeCheck();

        var settings = so.FindProperty("whiteFade");
        var end      = settings.GetEndProperty();
        var field    = settings.Copy();
        field.NextVisible(true);
        while (!SerializedProperty.EqualContents(field, end))
        {
            EditorGUILayout.PropertyField(field, true);
            if (!field.NextVisible(false)) break;
        }

        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            WhiteFadeSettings.Push(preset.whiteFade);
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// The world the fade belongs to: the Level Select Designer's workspace while it is open,
    /// otherwise the scene's. The preset itself is an asset, so both hold the same one.
    /// </summary>
    private static LevelSelectDesignerData WorldData()
    {
        if (LevelSelectAestheticsPump.Preview != null) return LevelSelectAestheticsPump.Preview;

        if (_controller == null)
            _controller = Object.FindFirstObjectByType<LevelSelectDataController>();

        return _controller != null ? _controller.DesignerData : null;
    }
}
