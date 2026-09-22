using UnityEditor;
using UnityEngine;

/// <summary>
/// Drives how the level select's landscape hills are shaded — three stone variants and which part
/// of the landscape wears each — and saves what you land on to a
/// <see cref="LevelSelectLandscapePreset"/> for the designer to hold.
///
/// The same arrangement as the Level Select Run Shading Tuner: the numbers are bare $Globals, so
/// sliders push straight into the shader, and while Apply Live is on this window hands its numbers
/// to <see cref="LandscapeShadingSettings.Live"/>, which the push prefers over any preset. Turning
/// the toggle off, or closing the window, gives the world back to its own preset.
/// </summary>
public class LevelSelectLandscapeTuner : EditorWindow
{
    [MenuItem("Tools/Waves/Level Select Landscape Tuner")]
    public static void Open() =>
        GetWindow<LevelSelectLandscapeTuner>("Level Select Landscape");

    private const string PrefPreset = "LevelSelectLandscapeTuner_PresetGUID";

    [SerializeField] private LevelSelectLandscapePreset activePreset;
    [SerializeField] private bool                       applyLive        = true;
    [SerializeField] private LandscapeShadingSettings   landscapeShading = new LandscapeShadingSettings();

    private SerializedObject _window;
    private Vector2          _scroll;

    // ─────────────────────────────────────────────────────────────
    // LIFETIME
    // ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _window = new SerializedObject(this);

        if (activePreset == null && EditorPrefs.HasKey(PrefPreset))
        {
            string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PrefPreset));
            if (!string.IsNullOrEmpty(path))
                activePreset = AssetDatabase.LoadAssetAtPath<LevelSelectLandscapePreset>(path);
        }

        // A pump of its own, so the window still drives a scene with no designer data in it.
        EditorApplication.update += Drive;
        TakeOver();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Drive;
        HandBack();
    }

    private void TakeOver()
    {
        LandscapeShadingSettings.Live = applyLive ? landscapeShading : null;
        SceneView.RepaintAll();
    }

    /// <summary>The Apply Live switch — also flipped by the Editor Load Monitor.</summary>
    public bool ApplyLive
    {
        get => applyLive;
        set
        {
            if (value == applyLive) return;
            applyLive = value;
            if (applyLive) TakeOver(); else HandBack();
            Repaint();
        }
    }

    private void HandBack()
    {
        if (LandscapeShadingSettings.Live == landscapeShading) LandscapeShadingSettings.Live = null;
        SceneView.RepaintAll();
    }

    private void Drive()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!applyLive) return;

        LandscapeShadingSettings.Push(landscapeShading);
    }

    // ─────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (_window == null) _window = new SerializedObject(this);
        _window.Update();

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.FlexibleSpace();
            ApplyLive = GUILayout.Toggle(applyLive, "Apply Live",
                                         EditorStyles.toolbarButton, GUILayout.Width(80));
        }

        DrawPresetRow();

        EditorGUILayout.Space();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Landscape Shading", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Three stone variants, each a colour with its own grain, and three parts of the " +
            "landscape that each wear one. Holes are everything below Hole Depth off the tile " +
            "base; all the rest is NoiseUp or NoiseDown, whichever way the rocky noise leans " +
            "there. Ground flat enough to lean neither way sits on the NoiseDown side, or " +
            "halfway between the two once Noise Softness is up off zero. Hills with no noise " +
            "on them are one flat colour — the noise is the only thing marking the land now. " +
            "All live — nothing to rebuild. The light's position is in the Level Select " +
            "Designer's Aesthetics, shared with the river runs.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUI.BeginChangeCheck();

        var shading = _window.FindProperty("landscapeShading");
        var end     = shading.GetEndProperty();
        var field   = shading.Copy();
        field.NextVisible(true);
        while (!SerializedProperty.EqualContents(field, end))
        {
            EditorGUILayout.PropertyField(field, true);
            if (!field.NextVisible(false)) break;
        }

        if (EditorGUI.EndChangeCheck())
        {
            _window.ApplyModifiedProperties();
            TakeOver();
        }

        EditorGUILayout.EndScrollView();
        _window.ApplyModifiedProperties();
    }

    private void DrawPresetRow()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            var picked = LevelSelectRiverPresetLibrary.DrawPicker(
                new GUIContent("Active Preset",
                               "The landscape presets kept in " + LevelSelectRiverPresetLibrary.Folder +
                               ". The world wears whichever the Level Select Designer holds."),
                activePreset);

            var chosen = (LevelSelectLandscapePreset)EditorGUILayout.ObjectField(
                picked, typeof(LevelSelectLandscapePreset), false, GUILayout.Width(150f));

            if (chosen != activePreset)
            {
                activePreset = chosen;
                RememberPreset();
                if (activePreset != null) LoadFromPreset();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(activePreset == null))
            {
                if (GUILayout.Button("Load")) LoadFromPreset();
                if (GUILayout.Button("Save")) SaveToPreset();
            }
            if (GUILayout.Button("Save as New")) SaveAsNewPreset();
        }

        if (activePreset == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a preset to enable Load and Save, or tune first and Save as New. The " +
                "world reads whichever landscape preset the Level Select Designer holds, not " +
                "this one.",
                MessageType.Info);
        }
    }

    private void RememberPreset()
    {
        if (activePreset == null)
        {
            EditorPrefs.DeleteKey(PrefPreset);
            return;
        }

        EditorPrefs.SetString(
            PrefPreset,
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(activePreset)));
    }

    private void LoadFromPreset()
    {
        if (activePreset == null) return;

        landscapeShading.CopyFrom(activePreset.landscapeShading);
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    private void SaveToPreset()
    {
        if (activePreset == null) return;

        if (!EditorUtility.DisplayDialog(
                "Save Landscape Shading",
                $"Overwrite the landscape shading on '{activePreset.name}' with the current " +
                "tuner values?",
                "Save", "Cancel"))
            return;

        Undo.RecordObject(activePreset, "Save Level Select Landscape Shading");
        activePreset.landscapeShading.CopyFrom(landscapeShading);
        EditorUtility.SetDirty(activePreset);
        AssetDatabase.SaveAssetIfDirty(activePreset);

        Debug.Log($"[LevelSelectLandscapeTuner] Saved to {activePreset.name}");
    }

    private void SaveAsNewPreset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Level Select Landscape Preset", "NewLevelSelectLandscapePreset", "asset",
            "Choose save location", LevelSelectRiverPresetLibrary.EnsureFolder());
        if (string.IsNullOrEmpty(path)) return;

        var preset = CreateInstance<LevelSelectLandscapePreset>();
        preset.landscapeShading.CopyFrom(landscapeShading);

        AssetDatabase.CreateAsset(preset, path);
        AssetDatabase.SaveAssets();

        activePreset = preset;
        RememberPreset();

        Debug.Log($"[LevelSelectLandscapeTuner] Saved new preset at {path}");
    }
}
