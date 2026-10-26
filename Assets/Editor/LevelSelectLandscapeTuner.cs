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
    [MenuItem("Tools/Tuners/Level Select Landscape Tuner", false, 221)]
    public static void Open() =>
        GetWindow<LevelSelectLandscapeTuner>("Level Select Landscape");

    private const string PrefPreset = "LevelSelectLandscapeTuner_PresetGUID";

    [SerializeField] private LevelSelectLandscapePreset activePreset;
    // The preset the values below came from; Save is held until it matches activePreset.
    [SerializeField] private LevelSelectLandscapePreset loadedPreset;
    [SerializeField] private bool                       applyLive        = true;
    [SerializeField] private LandscapeShadingSettings   landscapeShading = new LandscapeShadingSettings();
    // Which designation's parts are shown — an index into Designations.
    [SerializeField] private int                        designation;

    private SerializedObject _window;
    private readonly TunerUnsavedReminder _saveReminder = new TunerUnsavedReminder(nameof(LevelSelectLandscapeTuner));
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
        EditorApplication.update += RemindToSave;
        TakeOver();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Drive;
        EditorApplication.update -= RemindToSave;
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

        DrawMaterials();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Landscape Shading", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Four stone variants, each a colour with its own grain, and three parts of the " +
            "landscape that each wear one. Holes are everything below Hole Depth off the tile " +
            "base; all the rest is NoiseUp or NoiseDown, whichever way the rocky noise leans " +
            "there. Ground flat enough to lean neither way sits on the NoiseDown side, or " +
            "halfway between the two once Noise Softness is up off zero. Hills with no noise " +
            "on them are one flat colour — the noise is the only thing marking the land now. " +
            "The Designation dropdown picks whose parts are shown: Landscape is unpainted " +
            "ground; Red, Blue and Yellow are the ground painted those colours on the Texture tab " +
            "of the designer's Landscape Maps, blended into each other by Paint Softness. " +
            "All live — nothing to rebuild. The light is in the Level Select Lighting window, " +
            "shared with the river runs.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUI.BeginChangeCheck();

        var shading = _window.FindProperty("landscapeShading");
        var end     = shading.GetEndProperty();
        var field   = shading.Copy();
        int section = -1;   // -1 = the settings every designation shares
        field.NextVisible(true);
        while (!SerializedProperty.EqualContents(field, end))
        {
            // The designations' parts run from the first one's Holes to Hole Depth; only the
            // picked designation's are drawn.
            int starts = System.Array.FindIndex(Designations, d => d.firstField == field.name);
            if (starts >= 0)
            {
                if (section < 0) DrawDesignationPicker();
                section = starts;
            }
            if (field.name == nameof(LandscapeShadingSettings.holeDepth)) section = -1;

            // "Softness" alone reads as shared; it is the Holes blend only (Noise has its own).
            if (field.name == "softness")
                EditorGUILayout.PropertyField(field, new GUIContent("Hole Softness", field.tooltip), true);
            else if (section >= 0)
            {
                if (section == designation)
                    EditorGUILayout.PropertyField(field, new GUIContent(PartLabel(field.name), field.tooltip), true);
            }
            else
                EditorGUILayout.PropertyField(field, true);
            if (!field.NextVisible(false)) break;
        }

        if (EditorGUI.EndChangeCheck())
        {
            _window.ApplyModifiedProperties();
            TakeOver();
        }

        EditorGUILayout.Space();
        DrawDecals();

        EditorGUILayout.Space();
        DrawNormalDecals();

        EditorGUILayout.Space();
        WhiteFadeTunerSection.Draw();

        EditorGUILayout.EndScrollView();
        _window.ApplyModifiedProperties();

        ApplyDecalGroupChange();
        ApplyNormalDecalGroupChange();
    }

    // ─────────────────────────────────────────────────────────────
    // DESIGNATIONS
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Whose parts each take a variant, in the order their fields sit in
    /// <see cref="LandscapeShadingSettings"/> — each named with the field its parts start at, and
    /// the word its fields start with. Only the one picked in the Designation dropdown is shown.
    /// </summary>
    private static readonly (string label, string firstField, string prefix)[] Designations =
    {
        ("Landscape", nameof(LandscapeShadingSettings.holes),       ""),
        ("Red",       nameof(LandscapeShadingSettings.redHoles),    "red"),
        ("Blue",      nameof(LandscapeShadingSettings.blueHoles),   "blue"),
        ("Yellow",    nameof(LandscapeShadingSettings.yellowHoles), "yellow"),
    };

    /// <summary>A part's row name without its designation's word: "redNoiseUp" shows as "Noise Up".</summary>
    private static string PartLabel(string fieldName)
    {
        foreach (var d in Designations)
        {
            if (d.prefix.Length > 0 && fieldName.StartsWith(d.prefix))
                return ObjectNames.NicifyVariableName(fieldName.Substring(d.prefix.Length));
        }
        return ObjectNames.NicifyVariableName(fieldName);
    }

    private static GUIStyle _designationCaption, _designationPopup;

    // The Run Shading Tuner's dropdown, as it looks there.
    private void DrawDesignationPicker()
    {
        if (_designationPopup == null)
        {
            _designationCaption = new GUIStyle(EditorStyles.boldLabel)
                { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            _designationPopup = new GUIStyle(EditorStyles.popup)
                { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold, fixedHeight = 30f };
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Designation", _designationCaption);
        var labels = System.Array.ConvertAll(Designations, d => d.label);
        designation = EditorGUILayout.Popup(Mathf.Clamp(designation, 0, labels.Length - 1), labels,
                                            _designationPopup, GUILayout.Height(30f));
        EditorGUILayout.Space();
    }

    // ─────────────────────────────────────────────────────────────
    // DECALS
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The decal folder, the sheet built from it, and the decal groups — a simpler copy of the
    /// Run Shading Tuner's. The sheet is never rebuilt on its own: a window that writes assets
    /// while it draws writes them every repaint, so it is said and left to a button.
    /// </summary>
    private void DrawDecals()
    {
        EditorGUILayout.LabelField("Decals", EditorStyles.boldLabel);

        var decals = LandscapeDecalLibrary.All();

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                new GUIContent("Folder",
                               "Every drawing dropped in here can be ticked into a group once " +
                               "the sheet is rebuilt."),
                new GUIContent(LandscapeDecalLibrary.Folder));

            if (GUILayout.Button("Show", GUILayout.Width(50f)))
            {
                var folder = AssetDatabase.LoadAssetAtPath<Object>(
                    LevelSelectRiverPresetLibrary.EnsureFolder(LandscapeDecalLibrary.Folder));

                if (folder != null)
                {
                    Selection.activeObject = folder;
                    EditorGUIUtility.PingObject(folder);
                }
            }
        }

        if (decals.Count > 0) LevelSelectRunShadingTuner.DrawDecalStrip(decals);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                new GUIContent("Sheet", "All of the drawings on one texture, in equal cells."),
                landscapeShading.decalSheet, typeof(Texture2D), false);
        }

        string behind = LandscapeDecalLibrary.Behind(landscapeShading);

        if (behind != null)
        {
            EditorGUILayout.HelpBox(
                behind + " Rebuild the sheet, then Save, so the world keeps it — the sheet is " +
                "held on the preset like every other setting here.",
                MessageType.Warning);
        }
        else if (decals.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No drawings in the folder yet. Drop one in — anything drawn on transparency — " +
                "and rebuild the sheet.",
                MessageType.None);
        }

        if (GUILayout.Button("Rebuild Sheet")) RebuildDecalSheet();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Decal Groups", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Each group is a selection of the drawings — click a thumbnail to tick it in or out — " +
            "scattered or tiled, and drawn on whichever of Landscape, Spikes, Trees and the " +
            "painted Red, Blue and Yellow it ticks. " +
            "Groups stack, the later in this list on top. Trees placed before decals existed " +
            "count as Landscape until they are generated again.",
            EditorStyles.wordWrappedMiniLabel);

        var list = _window.FindProperty("landscapeShading")
                          .FindPropertyRelative(nameof(LandscapeShadingSettings.decalGroups));

        EditorGUI.BeginChangeCheck();

        for (int i = 0; i < list.arraySize; i++)
        {
            var group = list.GetArrayElementAtIndex(i);
            var name  = group.FindPropertyRelative(nameof(LandscapeDecalGroup.name));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    group.isExpanded = EditorGUILayout.Foldout(
                        group.isExpanded, name.stringValue, true, EditorStyles.foldoutHeader);

                    if (GUILayout.Button("Remove", GUILayout.Width(70f))) _decalGroupRemove = i;
                }

                if (!group.isExpanded) continue;

                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(name);

                EditorGUILayout.LabelField("Designations", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.onLandscape)),
                                              new GUIContent("Landscape"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.onSpikes)),
                                              new GUIContent("Spikes"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.onTrees)),
                                              new GUIContent("Trees"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.onRed)),
                                              new GUIContent("Red"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.onBlue)),
                                              new GUIContent("Blue"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.onYellow)),
                                              new GUIContent("Yellow"));

                // Tiled fills every place at exactly Spacing across, so Amount and Scale go unread.
                var tiled = group.FindPropertyRelative(nameof(LandscapeDecalGroup.tiled));

                using (new EditorGUI.DisabledScope(tiled.boolValue))
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.amount)));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.opacity)));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.blendMode)),
                                              new GUIContent("Blend Mode"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.spacing)));
                EditorGUILayout.PropertyField(tiled, new GUIContent("Tiled"));

                // Blend Tiles only means anything while Tiled is on; its two settings only while it is.
                EditorGUI.indentLevel++;
                var blendTiles = group.FindPropertyRelative(nameof(LandscapeDecalGroup.blendTiles));
                using (new EditorGUI.DisabledScope(!tiled.boolValue))
                {
                    EditorGUILayout.PropertyField(blendTiles, new GUIContent("Blend Tiles"));
                    EditorGUI.indentLevel++;
                    using (new EditorGUI.DisabledScope(!blendTiles.boolValue))
                    {
                        EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.blendNoiseScale)),
                                                      new GUIContent("Gradient Noise Scale"));
                        EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.blendNoiseSoftness)),
                                                      new GUIContent("Gradient Noise Softness"));
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;

                using (new EditorGUI.DisabledScope(tiled.boolValue))
                {
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.scaleMin)),
                                                  new GUIContent("Scale Min"));
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.scaleMax)),
                                                  new GUIContent("Scale Max"));
                }
                var rotate = group.FindPropertyRelative(nameof(LandscapeDecalGroup.rotate));
                EditorGUILayout.PropertyField(rotate, new GUIContent("Random 90° Rotation"));
                using (new EditorGUI.DisabledScope(!rotate.boolValue))
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeDecalGroup.rotateAmount)),
                                                  new GUIContent("Rotation Amount"));

                EditorGUILayout.LabelField("Drawings", EditorStyles.miniBoldLabel);
                var drawings = group.FindPropertyRelative(nameof(LandscapeDecalGroup.drawings));

                if (decals.Count == 0)
                {
                    EditorGUILayout.LabelField("No drawings in the folder yet.",
                                               EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    LevelSelectRunShadingTuner.DrawDrawingTicks(drawings, decals);
                    if (drawings.arraySize == 0)
                        EditorGUILayout.LabelField("Nothing ticked, so this group draws nothing.",
                                                   EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUI.indentLevel--;
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            _window.ApplyModifiedProperties();
            TakeOver();
        }

        using (new EditorGUI.DisabledScope(list.arraySize >= LandscapeShadingSettings.MaxDecalGroups))
        {
            if (GUILayout.Button("Add Decal Group")) _decalGroupAdd = true;
        }
    }

    // An Add or Remove pressed on the decal groups this repaint, carried out once the window has
    // finished drawing, so the list is not changed under the loop drawing it.
    private bool _decalGroupAdd;
    private int  _decalGroupRemove = -1;

    private void ApplyDecalGroupChange()
    {
        if (!_decalGroupAdd && _decalGroupRemove < 0) return;

        var groups = landscapeShading.decalGroups;
        Undo.RecordObject(this, _decalGroupAdd ? "Add Decal Group" : "Remove Decal Group");

        if (_decalGroupAdd && groups.Count < LandscapeShadingSettings.MaxDecalGroups)
            groups.Add(landscapeShading.NewDecalGroup("Decal Group " + (groups.Count + 1)));

        if (_decalGroupRemove >= 0 && _decalGroupRemove < groups.Count)
            groups.RemoveAt(_decalGroupRemove);

        _decalGroupAdd    = false;
        _decalGroupRemove = -1;
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    /// <summary>
    /// Gathers the folder onto the sheet and takes it into this window's numbers, so the scene
    /// shows it straight away. Saving is still separate: until Save the sheet is only here.
    /// </summary>
    private void RebuildDecalSheet()
    {
        bool built = LandscapeDecalLibrary.Build(landscapeShading, out string said);

        // The numbers changed underneath the SerializedObject, which would otherwise draw the old
        // ones back over them on the next repaint.
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();

        if (built) Debug.Log("[LevelSelectLandscapeTuner] " + said);
        else       Debug.LogWarning("[LevelSelectLandscapeTuner] " + said);
    }

    // ─────────────────────────────────────────────────────────────
    // NORMAL MAP DECALS
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The normal map decal groups: real normal maps from their own folder, on a sheet of their
    /// own, tiled over the ground — the Run Shading Tuner's, for the landscape. They paint nothing:
    /// they bend the light, the boat's light and the River Glow, so all of it is live.
    /// </summary>
    private void DrawNormalDecals()
    {
        EditorGUILayout.LabelField("Normal Map Decal Groups", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Normal maps tiled over the ground. They paint no colour: they bend which way the " +
            "ground faces, so the light, the boat's light and the River Glow fall across the " +
            "relief. All live — no rebake. Each group bends whichever of Landscape, Spikes, " +
            "Trees and the painted Red, Blue and Yellow it ticks.",
            EditorStyles.wordWrappedMiniLabel);

        var maps = LandscapeDecalLibrary.AllNormals();

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                new GUIContent("Folder", "Every normal map dropped in here can be ticked into a " +
                                         "group once the normal sheet is rebuilt."),
                new GUIContent(LandscapeDecalLibrary.NormalFolder));

            if (GUILayout.Button("Show", GUILayout.Width(50f)))
            {
                var folder = AssetDatabase.LoadAssetAtPath<Object>(
                    LevelSelectRiverPresetLibrary.EnsureFolder(LandscapeDecalLibrary.NormalFolder));

                if (folder != null)
                {
                    Selection.activeObject = folder;
                    EditorGUIUtility.PingObject(folder);
                }
            }
        }

        if (maps.Count > 0) LevelSelectRunShadingTuner.DrawDecalStrip(maps);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                new GUIContent("Normal Sheet", "All of the normal maps on one linear texture, in " +
                                               "equal cells. Built from the folder."),
                landscapeShading.normalDecalSheet, typeof(Texture2D), false);
        }

        string behind = LandscapeDecalLibrary.BehindNormals(landscapeShading);

        if (behind != null)
        {
            EditorGUILayout.HelpBox(
                behind + " Rebuild the normal sheet, then Save, so the world keeps it.",
                MessageType.Warning);
        }
        else if (maps.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No normal maps in the folder yet. Drop one in and rebuild the normal sheet.",
                MessageType.None);
        }

        if (GUILayout.Button("Rebuild Normal Sheet")) RebuildNormalDecalSheet();

        var list = _window.FindProperty("landscapeShading")
                          .FindPropertyRelative(nameof(LandscapeShadingSettings.normalDecalGroups));

        EditorGUI.BeginChangeCheck();

        for (int i = 0; i < list.arraySize; i++)
        {
            var group = list.GetArrayElementAtIndex(i);
            var name  = group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.name));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    group.isExpanded = EditorGUILayout.Foldout(
                        group.isExpanded, name.stringValue, true, EditorStyles.foldoutHeader);

                    if (GUILayout.Button("Remove", GUILayout.Width(70f))) _normalGroupRemove = i;
                }

                if (!group.isExpanded) continue;

                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(name);

                EditorGUILayout.LabelField("Designations", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.onLandscape)),
                                              new GUIContent("Landscape"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.onSpikes)),
                                              new GUIContent("Spikes"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.onTrees)),
                                              new GUIContent("Trees"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.onRed)),
                                              new GUIContent("Red"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.onBlue)),
                                              new GUIContent("Blue"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.onYellow)),
                                              new GUIContent("Yellow"));

                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.strength)));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.spacing)));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.seed)));

                var blendTiles = group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.blendTiles));
                EditorGUILayout.PropertyField(blendTiles, new GUIContent("Blend Tiles"));
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(!blendTiles.boolValue))
                {
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.blendNoiseScale)),
                                                  new GUIContent("Gradient Noise Scale"));
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.blendNoiseSoftness)),
                                                  new GUIContent("Gradient Noise Softness"));
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.LabelField("Normal Maps", EditorStyles.miniBoldLabel);
                var drawings = group.FindPropertyRelative(nameof(LandscapeNormalDecalGroup.drawings));

                if (maps.Count == 0)
                {
                    EditorGUILayout.LabelField("No normal maps in the folder yet.",
                                               EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    LevelSelectRunShadingTuner.DrawDrawingTicks(drawings, maps);
                    if (drawings.arraySize == 0)
                        EditorGUILayout.LabelField("Nothing ticked, so this group bends nothing.",
                                                   EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUI.indentLevel--;
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            _window.ApplyModifiedProperties();
            TakeOver();
        }

        using (new EditorGUI.DisabledScope(list.arraySize >= LandscapeShadingSettings.MaxNormalDecalGroups))
        {
            if (GUILayout.Button("Add Normal Map Decal Group")) _normalGroupAdd = true;
        }
    }

    // An Add or Remove pressed on the normal map groups this repaint, carried out once the window
    // has finished drawing.
    private bool _normalGroupAdd;
    private int  _normalGroupRemove = -1;

    private void ApplyNormalDecalGroupChange()
    {
        if (!_normalGroupAdd && _normalGroupRemove < 0) return;

        Undo.RecordObject(this, _normalGroupAdd ? "Add Normal Map Decal Group" : "Remove Normal Map Decal Group");

        if (landscapeShading.normalDecalGroups == null)
            landscapeShading.normalDecalGroups = new System.Collections.Generic.List<LandscapeNormalDecalGroup>();
        var groups = landscapeShading.normalDecalGroups;

        if (_normalGroupAdd && groups.Count < LandscapeShadingSettings.MaxNormalDecalGroups)
            groups.Add(landscapeShading.NewNormalDecalGroup("Normal Map Group " + (groups.Count + 1)));

        if (_normalGroupRemove >= 0 && _normalGroupRemove < groups.Count)
            groups.RemoveAt(_normalGroupRemove);

        _normalGroupAdd    = false;
        _normalGroupRemove = -1;
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    /// <summary>The normal maps onto their sheet, taken into this window's numbers like Rebuild Sheet.</summary>
    private void RebuildNormalDecalSheet()
    {
        bool built = LandscapeDecalLibrary.BuildNormals(landscapeShading, out string said);

        _window = new SerializedObject(this);
        TakeOver();
        Repaint();

        if (built) Debug.Log("[LevelSelectLandscapeTuner] " + said);
        else       Debug.LogWarning("[LevelSelectLandscapeTuner] " + said);
    }

    private const string DefaultSpikeMatPath =
        "Assets/TextureMatShader/LevelSelectMaterials/LevelSelectSpikeMat.mat";

    /// <summary>The landscape's material and the spikes' — both shaded by this window.</summary>
    private static void DrawMaterials()
    {
        var data  = TunerMaterialLinks.WorldData();
        var spike = data != null && data.spikeMaterial != null
                  ? data.spikeMaterial
                  : AssetDatabase.LoadAssetAtPath<Material>(DefaultSpikeMatPath);

        TunerMaterialLinks.Draw("LevelSelectLandscapeTuner_MaterialsOpen",
            new[] { "Landscape", "Spikes" },
            new[] { TunerMaterialLinks.LandscapeMaterial(), spike });
    }

    private void DrawPresetRow()
    {
        var tint = TunerLoadedPreset.TintRow(activePreset, loadedPreset);
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
        GUI.backgroundColor = tint;

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(activePreset == null))
            {
                if (GUILayout.Button("Load")) LoadFromPreset();
                using (new EditorGUI.DisabledScope(!TunerLoadedPreset.IsLoaded(activePreset, loadedPreset)))
                    if (GUILayout.Button("Save")) SaveToPreset();
            }
            if (GUILayout.Button("Save as New")) SaveAsNewPreset();
        }

        TunerLoadedPreset.DrawNotLoaded(activePreset, loadedPreset);

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
        loadedPreset = activePreset;
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    private void SaveToPreset(string reminder = null)
    {
        if (!TunerLoadedPreset.IsLoaded(activePreset, loadedPreset)) return;

        if (!EditorUtility.DisplayDialog(
                "Save Landscape Shading",
                reminder +
                $"Overwrite the landscape shading on '{activePreset.name}' with the current " +
                "tuner values?\n\n" +
                TunerSaveChanges.Describe(activePreset, p => p.landscapeShading.CopyFrom(landscapeShading)),
                "Save", "Cancel"))
            return;

        Undo.RecordObject(activePreset, "Save Level Select Landscape Shading");
        activePreset.landscapeShading.CopyFrom(landscapeShading);
        EditorUtility.SetDirty(activePreset);
        AssetDatabase.SaveAssetIfDirty(activePreset);

        Debug.Log($"[LevelSelectLandscapeTuner] Saved to {activePreset.name}");
    }

    // Ten minutes of changes the preset hasn't got, and Save asks for itself.
    private void RemindToSave() => _saveReminder.Tick(
        () => TunerLoadedPreset.IsLoaded(activePreset, loadedPreset) &&
              TunerSaveChanges.List(activePreset, p => p.landscapeShading.CopyFrom(landscapeShading)).Count > 0,
        SaveToPreset);

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
        loadedPreset = preset;
        RememberPreset();

        Debug.Log($"[LevelSelectLandscapeTuner] Saved new preset at {path}");
    }
}
