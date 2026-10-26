using UnityEditor;
using UnityEngine;

/// <summary>
/// The level select's lighting in one window: the scene's <see cref="LevelSelectLight"/> and every
/// bake made from it — the shadows, the river glow and the soul path glow — each with how big its
/// baked map is.
///
/// It edits the light directly — the light is the working copy. The river runs, the hills and the
/// spikes are all turned against that one light, and every material reads the same bakes off it.
/// The aesthetics pump re-pushes it every tick, so an edit here, or moving the object in the scene
/// view, is live at once. A <see cref="LevelSelectLightingPreset"/> keeps a copy of its values:
/// Load puts them on the light, Save writes the light's back.
/// </summary>
public class LevelSelectLightingWindow : EditorWindow
{
    [MenuItem("Tools/Tuners/Level Select Lighting", false, 219)]
    public static void Open() => GetWindow<LevelSelectLightingWindow>("Lighting");

    private const string PrefPreset = "LevelSelectLightingWindow_PresetGUID";

    // Where a light stands if there is no world to ask — the default the designer data carries.
    private static readonly Vector3 FallbackPosition = new Vector3(362.3f, 259.8f, -3.2f);

    private static readonly string[] SizeNames =
        System.Array.ConvertAll(LevelSelectShadowBaker.Sizes, s => $"{s} × {s}");

    [SerializeField] private LevelSelectLightingPreset activePreset;
    // The preset the light's values came from; Save is held until it matches activePreset.
    [SerializeField] private LevelSelectLightingPreset loadedPreset;

    private Vector2 _scroll;
    private readonly TunerUnsavedReminder _saveReminder = new TunerUnsavedReminder(nameof(LevelSelectLightingWindow));

    private void OnEnable()
    {
        if (activePreset == null && EditorPrefs.HasKey(PrefPreset))
        {
            string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PrefPreset));
            if (!string.IsNullOrEmpty(path))
                activePreset = AssetDatabase.LoadAssetAtPath<LevelSelectLightingPreset>(path);
        }

        EditorApplication.update += RemindToSave;
    }

    private void OnDisable() => EditorApplication.update -= RemindToSave;

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField(
            "The made-up light the river runs, the landscape and the spikes are all turned " +
            "against. It is the LevelSelectLight object in the scene: where it stands is where " +
            "the light comes from, and its Strength is the one strength all three share. The " +
            "scene's light is what the world is lit by; a preset keeps a copy of its position " +
            "and numbers (not the bakes, which are saved with the scene).",
            EditorStyles.wordWrappedMiniLabel);

        var light = LevelSelectLight.Current;
        if (light == null)
        {
            EditorGUILayout.HelpBox(
                "No LevelSelectLight in the scene, so the runs, hills and spikes are flat and " +
                "unlit.",
                MessageType.Warning);
            if (GUILayout.Button("Spawn Light")) Spawn();
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawPresetRow(light);
        DrawLight(light);
        DrawBoatLight(light);
        DrawShadowBake(light);
        DrawRiverGlow(light);
        DrawSoulPathGlow(light);

        EditorGUILayout.Space();
        if (GUILayout.Button("Select Light Object")) Selection.activeGameObject = light.gameObject;

        EditorGUILayout.EndScrollView();
    }

    // The scene view moving the light, or a bake finishing, should show here without a hover.
    private void OnInspectorUpdate() => Repaint();

    private static void DrawLight(LevelSelectLight light)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Light", EditorStyles.boldLabel);

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
    }

    /// <summary>
    /// The light the boat carries round the map, added over the world's light on the river runs,
    /// the hills and the river water. Live — it follows the boat.
    /// </summary>
    private static void DrawBoatLight(LevelSelectLight light)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Boat Light", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "A pool of light round the boat on the river runs, the landscape hills and the river " +
            "water, with the faces turned towards the boat brightening more. Not on the spikes " +
            "or the trees. Facing Strength leaves the water out — it faces straight up.",
            EditorStyles.wordWrappedMiniLabel);

        var so = new SerializedObject(light);
        so.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(so.FindProperty("boatLightRadius"), new GUIContent("Radius"));
        EditorGUILayout.PropertyField(so.FindProperty("boatLightSoftness"), new GUIContent("Softness"));
        EditorGUILayout.PropertyField(so.FindProperty("boatLightStrength"), new GUIContent("Strength"));
        EditorGUILayout.PropertyField(so.FindProperty("boatLightFacing"), new GUIContent("Facing Strength"));
        EditorGUILayout.PropertyField(so.FindProperty("boatLightColour"), new GUIContent("Colour"));
        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            Refresh();
        }
    }

    /// <summary>
    /// The shadows are baked, not live — the map's size, the button, and whether the bake still
    /// matches where the light stands and how big the map is meant to be.
    /// </summary>
    private static void DrawShadowBake(LevelSelectLight light)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Shadows", EditorStyles.boldLabel);

        var so = new SerializedObject(light);
        so.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(so.FindProperty("shadowDarkness"),
                                      new GUIContent("Shadow Darkness"));
        EditorGUILayout.PropertyField(so.FindProperty("shadowEdgeSoftness"),
                                      new GUIContent("Shadow Edge Softness"));

        var sizeProp = so.FindProperty("shadowMapSize");
        int index = System.Array.IndexOf(LevelSelectShadowBaker.Sizes, LevelSelectShadowBaker.SizeOf(light));
        int picked = EditorGUILayout.Popup(new GUIContent("Shadow Map Size", sizeProp.tooltip), index, SizeNames);
        if (picked != index) sizeProp.intValue = LevelSelectShadowBaker.Sizes[picked];
        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            Refresh();
        }

        int size = LevelSelectShadowBaker.SizeOf(light);
        if (light.shadowMap == null)
            EditorGUILayout.HelpBox("No shadows baked yet, so there are none.", MessageType.Info);
        else if (Vector3.Distance(light.shadowBakedFrom, light.transform.position) > 0.001f)
            EditorGUILayout.HelpBox("The light has moved since the shadows were baked — they are " +
                                    "still cast from where it stood. Bake again to move them.",
                                    MessageType.Warning);
        else if (light.shadowMap.width != size)
            EditorGUILayout.HelpBox($"The baked map is {light.shadowMap.width} × {light.shadowMap.height}. " +
                                    $"Bake again to make it {size} × {size}.", MessageType.Warning);
        else
            EditorGUILayout.LabelField("Shadows baked from where the light stands. Bake again after " +
                                       "moving anything that casts them.",
                                       EditorStyles.wordWrappedMiniLabel);

        if (GUILayout.Button("Bake Shadows")) LevelSelectShadowBaker.Bake(light);
    }

    /// <summary>
    /// The light the river water throws onto everything around it, walls blocking it. Strength is
    /// live; Reach and Resolution are baked, so the section says when the bake no longer matches
    /// them.
    /// </summary>
    private static void DrawRiverGlow(LevelSelectLight light)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("River Glow", EditorStyles.boldLabel);

        var so = new SerializedObject(light);
        so.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(so.FindProperty("glowStrength"), new GUIContent("Glow Strength"));
        EditorGUILayout.PropertyField(so.FindProperty("glowReach"), new GUIContent("Glow Reach"));
        EditorGUILayout.PropertyField(so.FindProperty("riverGlowResolution"), new GUIContent("Resolution"));
        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            Refresh();
        }

        if (light.riverGlowAtlas == null || light.riverGlowBricks == null)
            EditorGUILayout.HelpBox("No river glow baked yet, so there is none.", MessageType.Info);
        else if (Mathf.Abs(light.riverGlowBakedReach - light.glowReach) > 0.0001f)
            EditorGUILayout.HelpBox($"Glow Reach has changed since the glow was baked — it still " +
                                    $"reaches {light.riverGlowBakedReach:0.##} m. Bake again to " +
                                    "change it.", MessageType.Warning);
        else if (Mathf.Abs(light.riverGlowBakedResolution - light.riverGlowResolution) > 0.0001f)
            EditorGUILayout.HelpBox($"Resolution has changed since the glow was baked — it was baked " +
                                    $"at {light.riverGlowBakedResolution:0.##}. Bake again to change it.",
                                    MessageType.Warning);
        else
            EditorGUILayout.LabelField("Glow baked. Bake again after changing the rivers or " +
                                       "anything that blocks the glow.",
                                       EditorStyles.wordWrappedMiniLabel);

        if (GUILayout.Button("Bake River Glow")) RiverGlowBaker.Bake(light);
    }

    /// <summary>
    /// Bake Soul Path Glow: bakes with the river water preset's Glow Reach and Width (the River
    /// Tuner's, while it is applying live) and this light's Resolution, and says when what is
    /// baked no longer matches them or the routes.
    /// </summary>
    private static void DrawSoulPathGlow(LevelSelectLight light)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Soul Path Glow", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "The soul path glow is baked round every soul route's band and comes up behind the " +
            "band's front as it draws out. Out of play mode every route shows whole. Glow Reach and " +
            "Width are set in the Soul Zone section of the Level Select River Tuner. Bake again " +
            "after changing them, the Resolution, the routes or the rivers.",
            EditorStyles.wordWrappedMiniLabel);

        var so = new SerializedObject(light);
        so.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(so.FindProperty("soulGlowResolution"), new GUIContent("Resolution"));
        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            Refresh();
        }

        var data = TunerMaterialLinks.WorldData();
        if (data == null)
        {
            EditorGUILayout.HelpBox("No Level Select designer data found in the scene — the routes " +
                                    "come from it.", MessageType.Info);
            return;
        }

        var look = RiverSoulZoneSettings.Resolve(data.waterPreset != null ? data.waterPreset.soulZone : null);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField("Glow Reach", look.glowReach);
            EditorGUILayout.FloatField("Width", look.width);
        }

        if (light.soulGlowAtlas == null || light.soulGlowBricks == null || light.soulGlowRoutes == null)
            EditorGUILayout.HelpBox("No soul path glow baked yet.", MessageType.Info);
        else if (Mathf.Abs(light.soulGlowBakedReach - look.glowReach) > 0.0001f)
            EditorGUILayout.HelpBox($"Glow Reach is {look.glowReach:0.##} m but the baked glow " +
                                    $"reaches {light.soulGlowBakedReach:0.##} m. Bake again to " +
                                    "match.", MessageType.Warning);
        else if (Mathf.Abs(light.soulGlowBakedResolution - light.soulGlowResolution) > 0.0001f)
            EditorGUILayout.HelpBox($"Resolution has changed since the glow was baked — it was baked " +
                                    $"at {light.soulGlowBakedResolution:0.##}. Bake again to change it.",
                                    MessageType.Warning);
        else if (!SoulPathGlowBaker.RoutesMatch(light, data))
            EditorGUILayout.HelpBox("The soul routes have changed since the glow was baked. Bake " +
                                    "again to match.", MessageType.Warning);

        if (GUILayout.Button("Bake Soul Path Glow")) SoulPathGlowBaker.Bake(light, data, look);
    }

    private static void Refresh()
    {
        LevelSelectLight.Push();
        SceneView.RepaintAll();
    }

    // ─────────────────────────────────────────────────────────────
    // PRESET
    // ─────────────────────────────────────────────────────────────

    private void DrawPresetRow(LevelSelectLight light)
    {
        EditorGUILayout.Space();

        var tint = TunerLoadedPreset.TintRow(activePreset, loadedPreset);
        using (new EditorGUILayout.HorizontalScope())
        {
            var picked = LevelSelectRiverPresetLibrary.DrawPicker(
                new GUIContent("Active Preset",
                               "The lighting presets kept in " + LevelSelectRiverPresetLibrary.Folder +
                               ". Picking one loads it onto the scene's light."),
                activePreset);

            var chosen = (LevelSelectLightingPreset)EditorGUILayout.ObjectField(
                picked, typeof(LevelSelectLightingPreset), false, GUILayout.Width(150f));

            if (chosen != activePreset)
            {
                activePreset = chosen;
                RememberPreset();
                if (activePreset != null) LoadFromPreset(light);
            }
        }
        GUI.backgroundColor = tint;

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(activePreset == null))
            {
                if (GUILayout.Button("Load")) LoadFromPreset(light);
                using (new EditorGUI.DisabledScope(!TunerLoadedPreset.IsLoaded(activePreset, loadedPreset)))
                    if (GUILayout.Button("Save")) SaveToPreset(light);
            }
            if (GUILayout.Button("Save as New")) SaveAsNewPreset(light);
        }

        TunerLoadedPreset.DrawNotLoaded(activePreset, loadedPreset);

        if (activePreset == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a preset to enable Load and Save, or tune first and Save as New.",
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

    private void LoadFromPreset(LevelSelectLight light)
    {
        if (activePreset == null) return;

        Undo.RecordObjects(new Object[] { light, light.transform }, "Load Level Select Lighting");
        activePreset.ApplyTo(light);
        EditorUtility.SetDirty(light);
        loadedPreset = activePreset;
        Refresh();
        Repaint();
    }

    private void SaveToPreset(LevelSelectLight light, string reminder = null)
    {
        if (!TunerLoadedPreset.IsLoaded(activePreset, loadedPreset)) return;

        if (!EditorUtility.DisplayDialog(
                "Save Lighting",
                reminder +
                $"Overwrite '{activePreset.name}' with the current light values?\n\n" +
                TunerSaveChanges.Describe(activePreset, p => p.CopyFrom(light)),
                "Save", "Cancel"))
            return;

        Undo.RecordObject(activePreset, "Save Level Select Lighting");
        activePreset.CopyFrom(light);
        EditorUtility.SetDirty(activePreset);
        AssetDatabase.SaveAssetIfDirty(activePreset);

        Debug.Log($"[LevelSelectLightingWindow] Saved to {activePreset.name}");
    }

    // Ten minutes of changes the preset hasn't got, and Save asks for itself.
    private void RemindToSave()
    {
        var light = LevelSelectLight.Current;
        if (light == null) return;

        _saveReminder.Tick(
            () => TunerLoadedPreset.IsLoaded(activePreset, loadedPreset) &&
                  TunerSaveChanges.List(activePreset, p => p.CopyFrom(light)).Count > 0,
            reminder => SaveToPreset(light, reminder));
    }

    private void SaveAsNewPreset(LevelSelectLight light)
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Level Select Lighting Preset", "NewLevelSelectLightingPreset", "asset",
            "Choose save location", LevelSelectRiverPresetLibrary.EnsureFolder());
        if (string.IsNullOrEmpty(path)) return;

        var preset = CreateInstance<LevelSelectLightingPreset>();
        preset.CopyFrom(light);

        AssetDatabase.CreateAsset(preset, path);
        AssetDatabase.SaveAssets();

        activePreset = preset;
        loadedPreset = preset;
        RememberPreset();

        Debug.Log($"[LevelSelectLightingWindow] Saved new preset at {path}");
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
