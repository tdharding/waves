using UnityEditor;
using UnityEngine;

/// <summary>
/// Drives the look of the level select's rivers live, and saves what you land on to a
/// <see cref="LevelSelectRiverWaterPreset"/> for the designer to hold.
///
/// The same arrangement as the Wave Effects Tuner: sliders push straight into the shader globals
/// so the scene view moves with the drag, and nothing is written to disk until Save. What is
/// different is who wins. The aesthetics pump re-pushes the designer's preset every editor tick,
/// so this window cannot simply push and hope — while Apply Live is on it hands its own numbers to
/// <see cref="RiverEdgeRippleSettings.Live"/>, which the push prefers over any preset. Turning the
/// toggle off, or closing the window, gives the world straight back to its own preset.
/// </summary>
public class LevelSelectRiverTuner : EditorWindow
{
    [MenuItem("Tools/Waves/Level Select River Tuner")]
    public static void Open() => GetWindow<LevelSelectRiverTuner>("Level Select River Tuner");

    private const string PrefPreset  = "LevelSelectRiverTuner_PresetGUID";

    [SerializeField] private LevelSelectRiverWaterPreset  activePreset;
    [SerializeField] private bool                    applyLive = true;
    [SerializeField] private RiverEdgeRippleSettings edgeRipples = new RiverEdgeRippleSettings();

    // Not part of the preset and never saved with one: it is a way of READING the water, not a
    // way the water is meant to look. The world holds its own on the designer data; this is the
    // tuner's, and it wins for as long as the window is open.
    [SerializeField] private RiverRippleDebugView    debugView = RiverRippleDebugView.Off;

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
                activePreset = AssetDatabase.LoadAssetAtPath<LevelSelectRiverWaterPreset>(path);
        }

        // A pump of its own, so the window still drives a scene that has no designer data and no
        // controller in it — and so the numbers survive the shader reimport that empties them.
        EditorApplication.update += Drive;
        TakeOver();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Drive;
        RiverEdgeRippleSettings.LiveDebug = null;
        HandBack();
    }

    /// <summary>
    /// Puts this window's numbers in front of whatever preset the world holds.
    ///
    /// The debug view goes over whether or not Apply Live is on — it is not one of the numbers,
    /// it is how they are being looked at, and turning the numbers back over to the world is no
    /// reason to stop looking. It goes back to the world when the window closes.
    /// </summary>
    private void TakeOver()
    {
        RiverEdgeRippleSettings.Live      = applyLive ? edgeRipples : null;
        RiverEdgeRippleSettings.LiveDebug = debugView;
        SceneView.RepaintAll();
    }

    /// <summary>Gives the world back to its own preset.</summary>
    private void HandBack()
    {
        if (RiverEdgeRippleSettings.Live == edgeRipples) RiverEdgeRippleSettings.Live = null;
        SceneView.RepaintAll();
    }

    // In play mode the controller is already calling ApplyAesthetics every frame, and that push
    // prefers Live, so the tuner is heard there without this doing anything. Out of play mode the
    // pump only runs where there is designer data or a controller to find, which is what this
    // covers — along with the reimport that empties the globals while a graph is being saved.
    private void Drive()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        // The debug view goes over on its own so it still lands in a scene the pump cannot find,
        // and whether or not this window is driving the numbers.
        RiverEdgeRippleSettings.PushDebug(debugView);

        if (!applyLive) return;

        RiverEdgeRippleSettings.Push(edgeRipples);
    }

    // ─────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (_window == null) _window = new SerializedObject(this);
        _window.Update();

        DrawToolbar();
        DrawPresetRow();

        EditorGUILayout.Space();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawDebugRow();

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Water Lines", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Lines along a river's banks, and rings out of a pool's middle. Both are drawn off a " +
            "frame baked into the generated mesh, so water built before that frame existed falls " +
            "back to bank lines and a world-pinned drift until Rebuild Runs.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUI.BeginChangeCheck();

        var ripples = _window.FindProperty("edgeRipples");
        var end     = ripples.GetEndProperty();
        var field   = ripples.Copy();
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

        EditorGUILayout.Space();

        // The one thing in the effect that is NOT a global. Where two waters lap over each other
        // is geometry, so it is authored on the world and needs a rebuild — saying so here is
        // what stops it being hunted for among the sliders above.
        EditorGUILayout.LabelField("Where Two Waters Meet", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "How far a branch's water laps over the river it leaves, and a pool's over each " +
            "river that meets it, are GEOMETRY — Water Branch Overlap and Water Pool Overlap in " +
            "the Level Select Designer's Aesthetics section, applied by Rebuild Runs. " +
            "Lap Fade above says how much of whatever lap is built the alpha gradient covers, " +
            "measured back from the far lip, and retunes without a rebuild. Which water draws " +
            "on top is each water's Sorting Group.",
            MessageType.None);

        EditorGUILayout.EndScrollView();

        _window.ApplyModifiedProperties();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.FlexibleSpace();

            bool live = GUILayout.Toggle(applyLive, "Apply Live",
                                         EditorStyles.toolbarButton, GUILayout.Width(80));
            if (live != applyLive)
            {
                applyLive = live;
                if (applyLive) TakeOver(); else HandBack();
            }
        }
    }

    /// <summary>
    /// The view that answers "which way is this surface's frame lying" — the only question worth
    /// asking of a frame that was generated rather than authored.
    ///
    /// Kept out of the preset and out of Load/Save: it is a way of reading the water, not a way
    /// the water is meant to look, and a preset that could save it on would eventually ship it.
    /// It wins over whatever the world holds for as long as this window is open.
    /// </summary>
    private void DrawDebugRow()
    {
        EditorGUI.BeginChangeCheck();
        var chosen = (RiverRippleDebugView)EditorGUILayout.EnumPopup(
            new GUIContent("Debug View",
                           "Bands draws the coordinate the lines are cut from — stripes down a " +
                           "river's banks, rings round a pool. Along draws the one they run in, " +
                           "which crosses them at a right angle and is the way the distortion " +
                           "travels. Fade shows how much of a surface is really there under an " +
                           "overlap. Purple means a surface has no frame at all — Rebuild Runs."),
            debugView);
        if (EditorGUI.EndChangeCheck())
        {
            debugView = chosen;
            _window = new SerializedObject(this);
            TakeOver();
        }

        if (debugView != RiverRippleDebugView.Off)
            EditorGUILayout.HelpBox(
                "Nothing changes until the RiverEdgeRipples subgraph's Debug and DebugMix " +
                "outputs are wired into the water graph: Lerp BaseColor towards Debug by " +
                "DebugMix. Goes back to the world's own setting when this window closes.",
                MessageType.Info);
    }

    private void DrawPresetRow()
    {
        // The folder's presets by name, and an object field beside it for one held from anywhere
        // else. Both drive the same field: the dropdown feeds the object field, so whichever was
        // touched is the one that differs from what is held.
        using (new EditorGUILayout.HorizontalScope())
        {
            var picked = LevelSelectRiverPresetLibrary.DrawPicker(
                new GUIContent("Active Preset",
                               "The presets kept in " + LevelSelectRiverPresetLibrary.Folder +
                               ". One preset carries both of the river's looks — the ripple " +
                               "lines on the water and the shading of the stone runs — so this " +
                               "is the same preset the other tuner and the designer hold."),
                activePreset);

            var chosen = (LevelSelectRiverWaterPreset)EditorGUILayout.ObjectField(
                picked, typeof(LevelSelectRiverWaterPreset), false, GUILayout.Width(150f));

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
                "Assign a preset to enable Load and Save, or tune first and Save as New. " +
                "The world reads whichever preset the Level Select Designer holds, not this one.",
                MessageType.None);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // PRESET
    // ─────────────────────────────────────────────────────────────

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

        edgeRipples.CopyFrom(activePreset.edgeRipples);
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    private void SaveToPreset()
    {
        if (activePreset == null) return;

        if (!EditorUtility.DisplayDialog(
                "Save Level Select River Water Preset",
                $"Overwrite '{activePreset.name}' with the current tuner values?\n\n" +
                "This cannot be undone from disk.",
                "Save", "Cancel"))
            return;

        Undo.RecordObject(activePreset, "Save Level Select River Water Preset");
        activePreset.edgeRipples.CopyFrom(edgeRipples);
        EditorUtility.SetDirty(activePreset);
        AssetDatabase.SaveAssetIfDirty(activePreset);

        Debug.Log($"[LevelSelectRiverTuner] Saved to {activePreset.name}");
    }

    private void SaveAsNewPreset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Level Select River Water Preset", "NewLevelSelectWaterPreset", "asset",
            "Choose save location", LevelSelectRiverPresetLibrary.EnsureFolder());
        if (string.IsNullOrEmpty(path)) return;

        var preset = CreateInstance<LevelSelectRiverWaterPreset>();
        preset.edgeRipples.CopyFrom(edgeRipples);

        AssetDatabase.CreateAsset(preset, path);
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = preset;

        activePreset = preset;
        RememberPreset();
        Repaint();

        Debug.Log($"[LevelSelectRiverTuner] Preset saved to {path}");
    }
}
