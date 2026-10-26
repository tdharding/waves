using UnityEditor;
using UnityEngine;

using System.Collections.Generic;

/// <summary>
/// Drives how the level select's stone rivers are shaded — dark along the seams, white off the
/// waterline — and saves what you land on to a <see cref="LevelSelectRiverStructurePreset"/> for the
/// designer to hold.
///
/// The same arrangement as the Level Select River Tuner, and for the same reason: the numbers are
/// bare $Globals with nothing on disk behind them, so sliders push straight into the shader and
/// the scene view moves with the drag, with nothing written until Save. The aesthetics pump
/// re-pushes the designer's preset every editor tick, so while Apply Live is on this window hands
/// its own numbers to <see cref="RiverRunShadingSettings.Live"/>, which the push prefers over any
/// preset. Turning the toggle off, or closing the window, gives the world back to its own preset.
/// </summary>
public class LevelSelectRunShadingTuner : EditorWindow
{
    [MenuItem("Tools/Tuners/Level Select Run Shading Tuner", false, 222)]
    public static void Open() =>
        GetWindow<LevelSelectRunShadingTuner>("Level Select Run Shading");

    private const string PrefPreset  = "LevelSelectRunShadingTuner_PresetGUID";

    [SerializeField] private LevelSelectRiverStructurePreset  activePreset;
    // The preset the values below came from; Save is held until it matches activePreset.
    [SerializeField] private LevelSelectRiverStructurePreset  loadedPreset;
    [SerializeField] private bool                    applyLive  = true;
    [SerializeField] private RiverRunShadingSettings runShading = new RiverRunShadingSettings();
    [SerializeField] private int                     designation;

    private SerializedObject _window;
    private readonly TunerUnsavedReminder _saveReminder = new TunerUnsavedReminder(nameof(LevelSelectRunShadingTuner));
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
                activePreset = AssetDatabase.LoadAssetAtPath<LevelSelectRiverStructurePreset>(path);
        }

        // A pump of its own, so the window still drives a scene with no designer data and no
        // controller in it — and so the numbers survive the shader reimport that empties them.
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

    /// <summary>Puts this window's numbers in front of whatever preset the world holds.</summary>
    private void TakeOver()
    {
        RiverRunShadingSettings.Live = applyLive ? runShading : null;
        SceneView.RepaintAll();
    }

    /// <summary>
    /// The Apply Live switch. Turning it on puts this window's numbers in front of the preset;
    /// turning it off gives the world back. The toolbar button and the Editor Load Monitor both
    /// come through here.
    /// </summary>
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

    /// <summary>Gives the world back to its own preset.</summary>
    private void HandBack()
    {
        if (RiverRunShadingSettings.Live == runShading) RiverRunShadingSettings.Live = null;
        SceneView.RepaintAll();
    }

    // In play mode the controller is already calling ApplyAesthetics every frame, and that push
    // prefers Live, so the tuner is heard there without this doing anything. Out of play mode the
    // pump only runs where there is designer data or a controller to find, which is what this
    // covers — along with the reimport that empties the globals while a graph is being saved.
    private void Drive()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!applyLive) return;

        RiverRunShadingSettings.Push(runShading);
    }

    // ─────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (_window == null) _window = new SerializedObject(this);
        runShading.EnsureStoneTypes();
        RiverRunDecalLibrary.AdoptOldSheet(runShading);
        _window.Update();

        DrawToolbar();
        DrawPresetRow();

        EditorGUILayout.Space();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawMaterials();

        EditorGUILayout.Space();
        DrawSeamDataRow();

        EditorGUILayout.Space();
        DrawDecalRow();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Run Shading", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Five things, all off the same numbers. The stone itself — stone types A, B, C, D " +
            "and any added after, each a colour or a gradient across the whole piece, with its " +
            "own noise grain and its own seams — dark gathered along every corner of it: the rim's " +
            "two edges, the foot of the outer wall, the joints between pieces. White rising off the waterline up the inside " +
            "of the channel. Hand-drawn decals scattered over the lot. And the made-up " +
            "light the stone is shaped by, which is what replaced " +
            "Simulated Lighting Basic on the run shader. Grain size, the decal spacing " +
            "and scales, and the waterline extent are " +
            "metres; the seam extent is a percentage of the width of each surface, so every piece " +
            "is shaded in proportion to its own size. The Designation dropdown picks which " +
            "piece's parts are shown, and which stone type each takes. River Runs is live; the " +
            "rest are baked into the meshes, so rebuild after changing them.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUI.BeginChangeCheck();

        var shading = _window.FindProperty("runShading");
        var end     = shading.GetEndProperty();
        var field   = shading.Copy();
        int section = -1;   // -1 = the every-piece settings, above the first designation
        field.NextVisible(true);
        while (!SerializedProperty.EqualContents(field, end))
        {
            int starts = System.Array.FindIndex(Designations, d => d.firstField == field.name);
            if (starts >= 0)
            {
                if (section < 0)
                {
                    DrawDesignationPicker();
                    DrawDesignationScale(shading);
                }
                section = starts;
            }

            // The stone types are a list with rules of its own — A to D always there, only the
            // last one added removable — so they are drawn here rather than as a bare list.
            if (field.name == nameof(RiverRunShadingSettings.stoneTypes)) DrawStoneTypes(field);
            else if (section < 0 || section == designation)
            {
                DrawPart(field);
                if (field.name == nameof(RiverRunShadingSettings.seamBlendMode)) DrawSeamBlendNote(shading, field);
            }
            if (!field.NextVisible(false)) break;
        }

        if (EditorGUI.EndChangeCheck())
        {
            _window.ApplyModifiedProperties();
            TakeOver();
        }

        EditorGUILayout.Space();

        // The one number the effect needs that is NOT tuned here. Saying so is what stops it
        // being hunted for among the sliders above.
        EditorGUILayout.LabelField("Where The Water Sits", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "The waterline band is placed off Water Level in the Level Select Designer, pushed " +
            "from the world rather than carried here — it is where the water IS, not a way the " +
            "stone looks, and a second copy would only give the band somewhere to drift off the " +
            "water it is meant to be sitting on.",
            MessageType.None);

        EditorGUILayout.Space();
        WhiteFadeTunerSection.Draw();

        EditorGUILayout.EndScrollView();
        _window.ApplyModifiedProperties();
        ApplyStoneListChange();
        ApplyDecalGroupChange();
        ApplyNormalDecalGroupChange();
    }

    /// <summary>
    /// The pieces whose parts each take a stone type, in the order their sections sit in
    /// <see cref="RiverRunShadingSettings"/> — each named with the field its section starts at.
    /// Only the one picked in the Designation dropdown is shown.
    /// </summary>
    private static readonly (string label, string firstField, StoneDesignation stone)[] Designations =
    {
        ("River Runs",      nameof(RiverRunShadingSettings.runOuterFaces),       StoneDesignation.RiverRuns),
        ("Outposts",        nameof(RiverRunShadingSettings.outpostOutsideWalls), StoneDesignation.Outposts),
        ("Freestanding Structures", nameof(RiverRunShadingSettings.structureSides), StoneDesignation.FreestandingStructures),
        ("River Walkways",  nameof(RiverRunShadingSettings.walkwayTop),          StoneDesignation.RiverWalkways),
        ("River Bridges",   nameof(RiverRunShadingSettings.bridgeTop),           StoneDesignation.RiverBridges),
        ("River Archways",  nameof(RiverRunShadingSettings.riverArchTop),        StoneDesignation.RiverArchways),
        ("Rim Walls",       nameof(RiverRunShadingSettings.rimWallTop),          StoneDesignation.RimWalls),
        ("Pool Doorways",   nameof(RiverRunShadingSettings.poolDoorway),         StoneDesignation.PoolDoorways),
        ("River Portholes", nameof(RiverRunShadingSettings.riverPortholes),      StoneDesignation.RiverPortholes),
        ("Rim Nodes",       nameof(RiverRunShadingSettings.rimNodeTop),          StoneDesignation.RimNodes),
        ("Lollipop Towers", nameof(RiverRunShadingSettings.towerOrb),            StoneDesignation.LollipopTowers),
        ("Turret Towers",   nameof(RiverRunShadingSettings.turretBaseSides),     StoneDesignation.TurretTowers),
        ("Soul Juice Tanks", nameof(RiverRunShadingSettings.poolTowerBaseSides), StoneDesignation.SoulJuiceTanks),
        ("Soul Fish Bowls", nameof(RiverRunShadingSettings.fishBowlBaseSides),   StoneDesignation.SoulFishBowls),
        ("Walls",           nameof(RiverRunShadingSettings.wallSides),           StoneDesignation.Walls),
        ("Pipes",           nameof(RiverRunShadingSettings.pipeOutside),         StoneDesignation.Pipes),
        ("Arena",           nameof(RiverRunShadingSettings.arenaInnerFace),      StoneDesignation.Arena),
        ("Arena Archways",  nameof(RiverRunShadingSettings.archwayInside),       StoneDesignation.ArenaArchways),
        ("Arena Doors",     nameof(RiverRunShadingSettings.doorSheet),           StoneDesignation.ArenaDoors),
    };

    /// <summary>
    /// One row of the settings. A piece part is shown by its <see cref="PartNameAttribute"/> —
    /// the name the Designer Overlay puts on that bit in the Scene view — so the row and the bit
    /// on screen read the same.
    /// </summary>
    private static void DrawPart(SerializedProperty field)
    {
        var part = PartName(field);
        if (part == null)
        {
            EditorGUILayout.PropertyField(field, true);
            return;
        }

        if (part.Under) EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(field, new GUIContent(part.Name, field.tooltip), true);
        if (part.Under) EditorGUI.indentLevel--;
    }

    /// <summary>The <see cref="PartNameAttribute"/> on a settings field, or null.</summary>
    private static PartNameAttribute PartName(SerializedProperty field)
    {
        var info = typeof(RiverRunShadingSettings).GetField(field.name);
        return info != null
            ? (PartNameAttribute)System.Attribute.GetCustomAttribute(info, typeof(PartNameAttribute))
            : null;
    }

    private static GUIStyle _designationCaption, _designationPopup;

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

    private static readonly GUIContent DesignationScaleLabel = new GUIContent("Scale",
        "Multiplies the grain size, the decal spacing and scales, and the seam extent on every " +
        "part of this piece. 1 leaves them as the stone types say. Live — a piece built before " +
        "this existed needs one rebuild to pick it up.");

    /// <summary>
    /// Under Seam Blend Mode: Overlay and Soft Light cannot be laid on above the lighting, so say
    /// so while Seams Above Lighting is on, as the decal groups do.
    /// </summary>
    private static void DrawSeamBlendNote(SerializedProperty shading, SerializedProperty blendMode)
    {
        var mode = (DecalBlendMode)blendMode.enumValueIndex;
        if (mode != DecalBlendMode.Overlay && mode != DecalBlendMode.SoftLight) return;
        if (!shading.FindPropertyRelative(nameof(RiverRunShadingSettings.seamsAboveLighting)).boolValue) return;

        EditorGUILayout.LabelField(
            "Overlay and Soft Light only work with Seams Above Lighting off — drawn as Normal " +
            "while it is on.",
            EditorStyles.wordWrappedMiniLabel);
    }

    /// <summary>
    /// The picked Designation's one Scale, for every part of it. The list only grows when a Scale
    /// is actually changed, so looking through the Designations never marks the preset edited.
    /// </summary>
    private void DrawDesignationScale(SerializedProperty shading)
    {
        var scales  = shading.FindPropertyRelative(nameof(RiverRunShadingSettings.designationScales));
        int at      = (int)Designations[Mathf.Clamp(designation, 0, Designations.Length - 1)].stone;
        float shown = at < scales.arraySize ? scales.GetArrayElementAtIndex(at).floatValue : 1f;
        if (shown <= 0f) shown = 1f;

        float picked = Mathf.Max(0.01f, EditorGUILayout.FloatField(DesignationScaleLabel, shown));
        if (Mathf.Approximately(picked, shown)) return;

        while (scales.arraySize <= at)
        {
            scales.InsertArrayElementAtIndex(scales.arraySize);
            scales.GetArrayElementAtIndex(scales.arraySize - 1).floatValue = 1f;
        }
        scales.GetArrayElementAtIndex(at).floatValue = picked;
    }

    private static readonly GUIContent[] GradientDirections =
    {
        new GUIContent("Bottom → Top", "From the foot of the piece up to its top."),
        new GUIContent("Top → Bottom", "From the top of the piece down to its foot."),
        new GUIContent("Left → Right", "Across the piece along the world's X."),
        new GUIContent("Right → Left", "Across the piece along the world's X, the other way."),
    };

    /// <summary>
    /// Stone A, B, C, D and any added after: each a colour, or a gradient across the whole piece
    /// in its place, with its own grain. A to D are always there; one added after can be taken
    /// away again only while it is the last, so no part is left pointing at a letter that has
    /// moved.
    /// </summary>
    private void DrawStoneTypes(SerializedProperty list)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Stone Types — every piece", EditorStyles.boldLabel);

        bool removeLast = false;
        var  usage      = StoneUsage();

        for (int i = 0; i < list.arraySize; i++)
        {
            var type = list.GetArrayElementAtIndex(i);
            var useGradient = type.FindPropertyRelative(nameof(StoneType.useGradient));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    type.isExpanded = EditorGUILayout.Foldout(
                        type.isExpanded, "Stone " + RiverRunShadingSettings.StoneLetter(i), true,
                        EditorStyles.foldoutHeader);

                    var copyTo = new GUIContent("Copy To ▾",
                        "Copy this stone's settings, as they are in the tuner now, onto a stone of " +
                        "another preset. Written straight to that preset.");
                    var copyRect = GUILayoutUtility.GetRect(copyTo, EditorStyles.miniButton, GUILayout.Width(80f));
                    if (GUI.Button(copyRect, copyTo, EditorStyles.miniButton))
                        ShowCopyStoneMenu(i, copyRect);

                    if (i == list.arraySize - 1 && i >= RiverRunShadingSettings.FixedStoneTypes &&
                        GUILayout.Button("Remove", GUILayout.Width(70f)))
                        removeLast = true;
                }

                if (!type.isExpanded) continue;

                EditorGUI.indentLevel++;

                DrawStoneUsage(i, usage[i + 1]);

                if (!useGradient.boolValue)
                    EditorGUILayout.PropertyField(type.FindPropertyRelative(nameof(StoneType.colour)));

                EditorGUILayout.PropertyField(type.FindPropertyRelative(nameof(StoneType.grainSize)));
                EditorGUILayout.PropertyField(type.FindPropertyRelative(nameof(StoneType.grainStrength)));
                EditorGUILayout.PropertyField(useGradient);

                if (useGradient.boolValue)
                {
                    EditorGUILayout.PropertyField(type.FindPropertyRelative(nameof(StoneType.gradient)));

                    var direction = type.FindPropertyRelative(nameof(StoneType.gradientDirection));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PrefixLabel("Direction");
                        int indent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0;
                        direction.enumValueIndex = GUILayout.Toolbar(
                            direction.enumValueIndex, GradientDirections, EditorStyles.miniButton);
                        EditorGUI.indentLevel = indent;
                    }
                }

                EditorGUILayout.LabelField("Seams", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(type.FindPropertyRelative(nameof(StoneType.seamColour)));
                EditorGUILayout.PropertyField(type.FindPropertyRelative(nameof(StoneType.seamStrength)));
                EditorGUILayout.PropertyField(type.FindPropertyRelative(nameof(StoneType.seamExtentPercent)));

                EditorGUILayout.LabelField("Decal Groups", EditorStyles.miniBoldLabel);
                DrawStoneDecalGroups(type);

                EditorGUILayout.LabelField("Normal Map Groups", EditorStyles.miniBoldLabel);
                DrawStoneNormalDecalGroups(type.FindPropertyRelative(nameof(StoneType.normalDecalGroups)));

                EditorGUI.indentLevel--;
            }
        }

        using (new EditorGUI.DisabledScope(list.arraySize >= RiverRunShadingSettings.MaxStoneTypes))
        {
            if (GUILayout.Button("Add Stone " + RiverRunShadingSettings.StoneLetter(list.arraySize)))
                _stoneListChange = +1;
        }

        if (removeLast) _stoneListChange = -1;

        EditorGUILayout.Space();
    }

    /// <summary>
    /// The tuner's own other stones first, then every other preset in the folder, each opening
    /// onto its own stone letters. The preset being tuned is reached through the tuner's stones —
    /// they are the ones on screen, and Save takes them to disk.
    /// </summary>
    private void ShowCopyStoneMenu(int stone, Rect under)
    {
        var menu = new GenericMenu();

        for (int t = 0; t < runShading.stoneTypes.Count; t++)
        {
            if (t == stone) continue;
            int into = t;
            menu.AddItem(new GUIContent("This Preset/Stone " + RiverRunShadingSettings.StoneLetter(t)),
                         false, () => CopyStoneInTuner(stone, into));
        }
        menu.AddSeparator("");

        var presets = LevelSelectRiverPresetLibrary.All<LevelSelectRiverStructurePreset>();
        presets.Remove(activePreset);

        foreach (var preset in presets)
        {
            preset.runShading.EnsureStoneTypes();
            for (int t = 0; t < preset.runShading.stoneTypes.Count; t++)
            {
                var target = preset;
                int into   = t;
                menu.AddItem(new GUIContent(preset.name + "/Stone " + RiverRunShadingSettings.StoneLetter(t)),
                             false, () => CopyStoneToPreset(stone, target, into));
            }
        }

        if (presets.Count == 0) menu.AddDisabledItem(new GUIContent("No other presets"));
        menu.DropDown(under);
    }

    /// <summary>One of the tuner's stones onto another of its own — undoable, and kept by Save.</summary>
    private void CopyStoneInTuner(int stone, int into)
    {
        if (stone >= runShading.stoneTypes.Count || into >= runShading.stoneTypes.Count) return;

        Undo.RecordObject(this, "Copy Stone");
        runShading.stoneTypes[into].CopyFrom(runShading.stoneTypes[stone]);

        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    private void CopyStoneToPreset(int stone, LevelSelectRiverStructurePreset target, int into)
    {
        if (target == null || stone >= runShading.stoneTypes.Count) return;
        target.runShading.EnsureStoneTypes();
        if (into >= target.runShading.stoneTypes.Count) return;

        var    source = runShading.stoneTypes[stone];
        string from   = "Stone " + RiverRunShadingSettings.StoneLetter(stone);
        string to     = "Stone " + RiverRunShadingSettings.StoneLetter(into);

        // Decal groups are ticked by id, and a preset made on its own has groups of its own —
        // a tick the other preset has no group for stamps nothing there.
        var missing = new List<string>();
        foreach (var id in source.decalGroups)
            if (target.runShading.FindDecalGroup(id) == null)
            {
                var group = runShading.FindDecalGroup(id);
                missing.Add(group != null ? group.name : id);
            }
        foreach (var id in source.normalDecalGroups)
            if (target.runShading.FindNormalDecalGroup(id) == null)
            {
                var group = runShading.FindNormalDecalGroup(id);
                missing.Add(group != null ? group.name : id);
            }

        if (!EditorUtility.DisplayDialog(
                "Copy Stone",
                $"Overwrite {to} on '{target.name}' with {from} as it is in the tuner now?\n\n" +
                "Nothing else on that preset changes.\n\nThis cannot be undone from disk." +
                (missing.Count > 0
                    ? "\n\nThese ticked decal groups are not on that preset, so they will stamp " +
                      "nothing there: " + string.Join(", ", missing)
                    : ""),
                "Copy", "Cancel"))
            return;

        Undo.RecordObject(target, "Copy Stone To Preset");
        target.runShading.stoneTypes[into].CopyFrom(source);
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssetIfDirty(target);

        Debug.Log($"[LevelSelectRunShadingTuner] Copied {from} to {to} on {target.name}");
    }

    // Which stones have their Used By list open. Only for this window's session — a view, not a setting.
    private readonly HashSet<int> _usageOpen = new HashSet<int>();

    private void DrawStoneUsage(int stone, List<string> users)
    {
        bool open = _usageOpen.Contains(stone);
        bool now  = EditorGUILayout.Foldout(open, users.Count == 0 ? "Used By — nothing" : "Used By", true);
        if (now != open)
        {
            if (now) _usageOpen.Add(stone);
            else     _usageOpen.Remove(stone);
        }
        if (!now) return;

        EditorGUI.indentLevel++;
        if (users.Count == 0)
            EditorGUILayout.LabelField("No part of any piece is set to this stone.",
                                       EditorStyles.wordWrappedMiniLabel);
        foreach (var user in users)
            EditorGUILayout.LabelField(user, EditorStyles.wordWrappedMiniLabel);
        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// For each stone (indexed by its kind number, A = 1), the parts set to it across every
    /// Designation — read off the window's own copy, so it follows the dropdowns as they change.
    /// These are the settings, not what is built: a baked piece shows them only once rebuilt.
    /// Auto parts are sorted by the builder face by face — outer A, rim B, inner C — so they are
    /// listed under all three, gathered one line per piece.
    /// </summary>
    private List<string>[] StoneUsage()
    {
        var usage = new List<string>[RiverRunShadingSettings.MaxStoneTypes + 1];
        for (int i = 0; i < usage.Length; i++) usage[i] = new List<string>();

        var autoParts = new List<string>[Designations.Length];

        // Every part, by section: its name split into words, and the stone it is set to.
        var fields  = new List<(int section, string[] words, int kind)>();
        var shading = _window.FindProperty("runShading");
        var end     = shading.GetEndProperty();
        var field   = shading.Copy();
        int section = -1;
        field.NextVisible(true);
        while (!SerializedProperty.EqualContents(field, end))
        {
            int starts = System.Array.FindIndex(Designations, d => d.firstField == field.name);
            if (starts >= 0) section = starts;

            if (section >= 0 && field.propertyType == SerializedPropertyType.Enum)
            {
                int kind = field.intValue;
                if (kind == 0) kind = WallAuto(field.name);
                fields.Add((section, (PartName(field)?.Name ?? field.displayName).Split(' '), kind));
            }

            if (!field.NextVisible(false)) break;
        }

        // The section's own words lead every field's name ("Outpost Floor", "Pool Tower Ramp") —
        // the words all its fields start with are dropped.
        var lead = new int[Designations.Length];
        for (int s = 0; s < Designations.Length; s++)
        {
            string[] first = null;
            int      count = int.MaxValue;
            foreach (var f in fields)
            {
                if (f.section != s) continue;
                if (first == null) { first = f.words; count = first.Length - 1; continue; }
                int same = 0;
                while (same < count && same < f.words.Length - 1 && f.words[same] == first[same]) same++;
                count = same;
            }
            lead[s] = first == null ? 0 : count;
        }

        foreach (var f in fields)
        {
            string part = string.Join(" ", f.words, lead[f.section], f.words.Length - lead[f.section]);

            if (f.kind > 0)
            {
                if (f.kind < usage.Length) usage[f.kind].Add(Designations[f.section].label + " — " + part);
            }
            else
            {
                if (autoParts[f.section] == null) autoParts[f.section] = new List<string>();
                autoParts[f.section].Add(part);
            }
        }

        // A rim node on Auto is sorted like a run's own faces, so it takes the River Runs stones.
        int rimNodes = System.Array.FindIndex(Designations,
            d => d.firstField == nameof(RiverRunShadingSettings.rimNodeTop));
        int[] runStones =
        {
            _window.FindProperty("runShading." + nameof(RiverRunShadingSettings.runOuterFaces)).intValue,
            _window.FindProperty("runShading." + nameof(RiverRunShadingSettings.runRim)).intValue,
            _window.FindProperty("runShading." + nameof(RiverRunShadingSettings.runInnerChannel)).intValue,
        };

        // A porthole on Auto takes the River Runs' outer wall stone, whichever way its faces sit.
        int portholes = System.Array.FindIndex(Designations,
            d => d.firstField == nameof(RiverRunShadingSettings.riverPortholes));

        string[] sorted = { "outer", "rim", "inner" };
        for (int s = 0; s < Designations.Length; s++)
        {
            if (autoParts[s] == null) continue;
            if (s == portholes)
            {
                int stone = runStones[0] > 0 ? runStones[0] : 1;
                if (stone < usage.Length)
                    usage[stone].Add($"{Designations[s].label} — {string.Join(", ", autoParts[s])} " +
                                     "(Auto: as the River Runs' outer wall)");
                continue;
            }
            for (int k = 0; k < sorted.Length; k++)
            {
                if (s == rimNodes)
                {
                    int stone = runStones[k] > 0 ? runStones[k] : k + 1;
                    if (stone < usage.Length)
                        usage[stone].Add($"{Designations[s].label} — {string.Join(", ", autoParts[s])} " +
                                         $"(Auto: the faces that sit as {sorted[k]}, as the River Runs)");
                    continue;
                }

                usage[k + 1].Add($"{Designations[s].label} — {string.Join(", ", autoParts[s])} " +
                                 $"(Auto: the faces that sit as {sorted[k]})");
            }
        }

        return usage;
    }

    /// <summary>
    /// What a wall part left on Auto is built with — walls are not sorted face by face, so their
    /// Auto is a fixed letter. Mirrors the fall-backs in WallMesh.Build's shading choice; 0 for
    /// anything that is not a wall part.
    /// </summary>
    private int WallAuto(string field)
    {
        // The wall part another falls back to, as that part is itself built.
        int SameAs(string other)
        {
            int k = _window.FindProperty("runShading." + other).intValue;
            return k > 0 ? k : WallAuto(other);
        }

        switch (field)
        {
            case nameof(RiverRunShadingSettings.wallSides):
            case nameof(RiverRunShadingSettings.wallEnds):
            case nameof(RiverRunShadingSettings.wallOpenings):
            case nameof(RiverRunShadingSettings.wallColumnSides):
                return (int)StoneFaceKind.A;
            case nameof(RiverRunShadingSettings.wallTop):
            case nameof(RiverRunShadingSettings.wallColumnTop):
                return (int)StoneFaceKind.B;
            case nameof(RiverRunShadingSettings.wallLipTop):
                return SameAs(nameof(RiverRunShadingSettings.wallTop));
            case nameof(RiverRunShadingSettings.wallLipSides):
            case nameof(RiverRunShadingSettings.wallLipUnderside):
                return SameAs(nameof(RiverRunShadingSettings.wallSides));
            case nameof(RiverRunShadingSettings.wallPortholes):
            case nameof(RiverRunShadingSettings.wallDamageEdge):
                return SameAs(nameof(RiverRunShadingSettings.wallOpenings));
            case nameof(RiverRunShadingSettings.wallColumnLipTop):
                return SameAs(nameof(RiverRunShadingSettings.wallLipTop));
            case nameof(RiverRunShadingSettings.wallColumnLipSides):
                return SameAs(nameof(RiverRunShadingSettings.wallLipSides));
            case nameof(RiverRunShadingSettings.wallColumnLipUnderside):
                return SameAs(nameof(RiverRunShadingSettings.wallLipUnderside));
            case nameof(RiverRunShadingSettings.wallColumnSleeve):
                return SameAs(nameof(RiverRunShadingSettings.wallColumnLipSides));
        }
        return 0;
    }

    // An Add or Remove pressed this repaint, carried out once the window has finished drawing —
    // changing the list's length mid-draw would pull it out from under the fields still to come.
    private int _stoneListChange;

    private void ApplyStoneListChange()
    {
        if (_stoneListChange == 0) return;

        Undo.RecordObject(this, _stoneListChange > 0 ? "Add Stone Type" : "Remove Stone Type");
        if (_stoneListChange > 0) runShading.stoneTypes.Add(new StoneType());
        else if (runShading.stoneTypes.Count > RiverRunShadingSettings.FixedStoneTypes)
            runShading.stoneTypes.RemoveAt(runShading.stoneTypes.Count - 1);

        _stoneListChange = 0;
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    /// <summary>
    /// The run's material, and the arena wall and door ones — each falling back the way the
    /// designer falls back when it builds them, so what shows is what is on the stone.
    /// </summary>
    private static void DrawMaterials()
    {
        var data = TunerMaterialLinks.WorldData();
        var run  = data != null ? data.riverMaterial : null;
        var wall = data != null && data.arenaWallMaterial != null ? data.arenaWallMaterial : run;
        var door = data != null && data.arenaDoorMaterial != null ? data.arenaDoorMaterial : wall;

        TunerMaterialLinks.Draw("LevelSelectRunShadingTuner_MaterialsOpen",
            new[] { "Runs", "Arena", "Arena Doors" },
            new[] { run, wall, door });
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.FlexibleSpace();

            ApplyLive = GUILayout.Toggle(applyLive, "Apply Live",
                                         EditorStyles.toolbarButton, GUILayout.Width(80));
        }
    }

    /// <summary>
    /// Whether the stone in this scene actually carries what these numbers act on — the seams on
    /// UV1, and which part of the run each face is on UV2.
    ///
    /// Both are worked out as a piece is generated and baked into its mesh, so a run built before
    /// that was happening has nothing for the numbers to reach — and the shader deliberately
    /// leaves such a piece alone rather than guessing, which looks exactly like the effect being
    /// switched off. That is a hard thing to tell apart from a bad setting by dragging sliders,
    /// so it is answered here instead of being hunted for.
    ///
    /// The two are counted separately because a run rebuilt before the face kinds existed carries
    /// seams and no kinds: its lines are drawn but its three colours do nothing, which on its own
    /// reads as the colour fields being broken.
    /// </summary>
    private void DrawSeamDataRow()
    {
        int runs = 0, noSeams = 0, noKinds = 0;
        foreach (var record in FindObjectsByType<RiverRunMesh>(FindObjectsSortMode.None))
        {
            var filter = record.GetComponent<MeshFilter>();
            var mesh   = filter != null ? filter.sharedMesh : null;
            if (mesh == null) continue;

            runs++;
            if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1))
                noSeams++;
            if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2))
                noKinds++;
        }

        if (runs == 0)
        {
            EditorGUILayout.HelpBox(
                "No generated runs in this scene to shade. Open the level select scene.",
                MessageType.None);
            return;
        }

        if (noSeams > 0 || noKinds > 0)
        {
            string missing = noSeams > 0 && noKinds > 0
                           ? $"{noSeams} of {runs} runs carry no seams and {noKinds} carry no " +
                             "face kinds"
                           : noSeams > 0
                           ? $"{noSeams} of {runs} runs carry no seams"
                           : $"{noKinds} of {runs} runs carry no face kinds, so the stone types " +
                             "cannot reach them";

            EditorGUILayout.HelpBox(
                missing + ". Press Rebuild Runs in the Level Select Designer — both are baked " +
                "into the mesh as it is generated, along with the smoothing along the outer and " +
                "inner faces.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"All {runs} runs carry their seams and their face kinds.", MessageType.None);
        }
    }

    /// <summary>
    /// The hand-drawn decals: what is in their folder, what the sheet built from it holds, and
    /// whether the two still agree.
    ///
    /// Here rather than among the sliders because it is not a number. The scatter's spacing,
    /// scales and amount are below with everything else; this is the drawings themselves — which
    /// arrive by being dropped into a folder, not by being typed — and the one sheet they are
    /// gathered onto for the shader to read.
    ///
    /// The sheet is not rebuilt on its own, for the reason in
    /// <see cref="RiverRunDecalLibrary.Behind"/>: a window that writes assets while it is being
    /// drawn writes them on every repaint, and a sheet that rebuilt itself would move every decal
    /// in the world the moment a drawing was added to the folder. So it is counted, said, and
    /// left to a button.
    /// </summary>
    private void DrawDecalRow()
    {
        EditorGUILayout.LabelField("Decals", EditorStyles.boldLabel);

        var decals = RiverRunDecalLibrary.All();

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                new GUIContent(
                    "Folder",
                    "Every drawing dropped in here joins the scatter once the sheet is rebuilt."),
                new GUIContent(RiverRunDecalLibrary.Folder));

            if (GUILayout.Button("Show", GUILayout.Width(50f)))
            {
                var folder = AssetDatabase.LoadAssetAtPath<Object>(
                    LevelSelectRiverPresetLibrary.EnsureFolder(RiverRunDecalLibrary.Folder));

                if (folder != null)
                {
                    Selection.activeObject = folder;
                    EditorGUIUtility.PingObject(folder);
                }
            }
        }

        // The drawings themselves, in the order they are gathered — which is the order they are
        // laid onto the sheet, and so which cell each of them ends up in.
        if (decals.Count > 0) DrawDecalStrip(decals);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                new GUIContent("Sheet",
                               "All of the drawings on one texture, in equal cells. Built from " +
                               "the folder, held by these settings, and read by the shader one " +
                               "cell at a time."),
                runShading.decalSheet, typeof(Texture2D), false);
        }

        string behind = RiverRunDecalLibrary.Behind(runShading);

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
                "No drawings in the folder yet, so nothing is stamped on the stone. Drop one in " +
                "— anything drawn on transparency, any size, any shape — and rebuild the sheet.",
                MessageType.None);
        }
        else if (!runShading.stoneTypes.Exists(t => t != null && t.decalGroups != null &&
                                                    t.decalGroups.Count > 0))
        {
            EditorGUILayout.HelpBox(
                "The sheet holds " + runShading.decalSheetHeld + " of the folder's " +
                decals.Count + ", and no stone type ticks a decal group, so none of them is " +
                "drawn. Make a group below and tick it on a stone type.",
                MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "The sheet holds all " + decals.Count + " of the folder's drawings, " +
                runShading.decalSheetColumns + "x" + runShading.decalSheetRows + " cells of " +
                runShading.decalSheetCellPixels + "px.",
                MessageType.None);
        }

        if (GUILayout.Button("Rebuild Sheet")) RebuildDecalSheet();

        DrawDecalGroups(decals);

        EditorGUILayout.Space();
        DrawNormalDecals();
    }

    /// <summary>
    /// The normal map decal groups: real normal maps from their own folder, on a sheet of their
    /// own, tiled over the stone. They paint nothing — they bend the light, the boat's light and
    /// the River Glow, so all of it is live and nothing needs baking again.
    /// </summary>
    private void DrawNormalDecals()
    {
        EditorGUILayout.LabelField("Normal Map Decal Groups", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Normal maps tiled over the stone. They paint no colour: they bend which way the stone " +
            "faces, so the light, the boat's light and the River Glow fall across the relief. All " +
            "live — no rebake. Stone types tick the groups they take, below.",
            EditorStyles.wordWrappedMiniLabel);

        var maps = RiverRunDecalLibrary.All(RiverRunDecalLibrary.NormalFolder);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                new GUIContent("Folder", "Every normal map dropped in here can be ticked into a " +
                                         "group once the normal sheet is rebuilt."),
                new GUIContent(RiverRunDecalLibrary.NormalFolder));

            if (GUILayout.Button("Show", GUILayout.Width(50f)))
            {
                var folder = AssetDatabase.LoadAssetAtPath<Object>(
                    LevelSelectRiverPresetLibrary.EnsureFolder(RiverRunDecalLibrary.NormalFolder));

                if (folder != null)
                {
                    Selection.activeObject = folder;
                    EditorGUIUtility.PingObject(folder);
                }
            }
        }

        if (maps.Count > 0) DrawDecalStrip(maps);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                new GUIContent("Normal Sheet", "All of the normal maps on one linear texture, in " +
                                               "equal cells. Built from the folder."),
                runShading.normalDecalSheet, typeof(Texture2D), false);
        }

        string behind = RiverRunDecalLibrary.BehindNormals(runShading);

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

        var list = _window.FindProperty("runShading")
                          .FindPropertyRelative(nameof(RiverRunShadingSettings.normalDecalGroups));

        EditorGUI.BeginChangeCheck();

        for (int i = 0; i < list.arraySize; i++)
        {
            var group = list.GetArrayElementAtIndex(i);
            var name  = group.FindPropertyRelative(nameof(NormalDecalGroup.name));

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
                EditorGUILayout.LabelField("Ticked On", StonesTickingNormal(i), EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(NormalDecalGroup.strength)));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(NormalDecalGroup.spacing)));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(NormalDecalGroup.seed)));

                var blendTiles = group.FindPropertyRelative(nameof(NormalDecalGroup.blendTiles));
                EditorGUILayout.PropertyField(blendTiles, new GUIContent("Blend Tiles"));
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(!blendTiles.boolValue))
                {
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(NormalDecalGroup.blendNoiseScale)),
                                                  new GUIContent("Gradient Noise Scale"));
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(NormalDecalGroup.blendNoiseSoftness)),
                                                  new GUIContent("Gradient Noise Softness"));
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.LabelField("Normal Maps", EditorStyles.miniBoldLabel);
                var drawings = group.FindPropertyRelative(nameof(NormalDecalGroup.drawings));

                if (maps.Count == 0)
                {
                    EditorGUILayout.LabelField("No normal maps in the folder yet.",
                                               EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    DrawDrawingTicks(drawings, maps);
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

        using (new EditorGUI.DisabledScope(list.arraySize >= RiverRunShadingSettings.MaxNormalDecalGroups))
        {
            if (GUILayout.Button("Add Normal Map Decal Group")) _normalGroupAdd = true;
        }
    }

    /// <summary>Which stone types tick the normal map group at this place in the list.</summary>
    private string StonesTickingNormal(int groupIndex)
    {
        if (groupIndex >= runShading.normalDecalGroups.Count) return string.Empty;
        string id = runShading.normalDecalGroups[groupIndex]?.id;

        var stones = new List<string>();
        for (int s = 0; s < runShading.stoneTypes.Count; s++)
        {
            var type = runShading.stoneTypes[s];
            if (type != null && type.normalDecalGroups != null && type.normalDecalGroups.Contains(id))
                stones.Add("Stone " + RiverRunShadingSettings.StoneLetter(s));
        }

        return stones.Count == 0 ? "No stone yet" : string.Join(", ", stones);
    }

    /// <summary>A stone type's ticks: one per normal map group, in the groups' order.</summary>
    private void DrawStoneNormalDecalGroups(SerializedProperty ticked)
    {
        if (runShading.normalDecalGroups.Count == 0)
        {
            EditorGUILayout.LabelField("No normal map groups yet — make one in Normal Map Decal " +
                                       "Groups, above.", EditorStyles.wordWrappedMiniLabel);
            return;
        }

        foreach (var group in runShading.normalDecalGroups)
        {
            if (group == null) continue;

            int  at  = IndexOf(ticked, group.id);
            bool now = EditorGUILayout.ToggleLeft(group.name, at >= 0);
            if (now == at >= 0) continue;

            if (now)
            {
                ticked.InsertArrayElementAtIndex(ticked.arraySize);
                ticked.GetArrayElementAtIndex(ticked.arraySize - 1).stringValue = group.id;
            }
            else ticked.DeleteArrayElementAtIndex(at);
        }
    }

    // An Add or Remove pressed on the normal map groups this repaint, carried out once the window
    // has finished drawing.
    private bool _normalGroupAdd;
    private int  _normalGroupRemove = -1;

    private void ApplyNormalDecalGroupChange()
    {
        if (!_normalGroupAdd && _normalGroupRemove < 0) return;

        var groups = runShading.normalDecalGroups;
        Undo.RecordObject(this, _normalGroupAdd ? "Add Normal Map Decal Group" : "Remove Normal Map Decal Group");

        if (_normalGroupAdd && groups.Count < RiverRunShadingSettings.MaxNormalDecalGroups)
            groups.Add(runShading.NewNormalDecalGroup("Normal Map Group " + (groups.Count + 1)));

        if (_normalGroupRemove >= 0 && _normalGroupRemove < groups.Count)
        {
            string id = groups[_normalGroupRemove]?.id;
            groups.RemoveAt(_normalGroupRemove);
            foreach (var type in runShading.stoneTypes) type?.normalDecalGroups?.Remove(id);
        }

        _normalGroupAdd    = false;
        _normalGroupRemove = -1;
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    /// <summary>The normal maps onto their sheet, taken into this window's numbers like Rebuild Sheet.</summary>
    private void RebuildNormalDecalSheet()
    {
        bool built = RiverRunDecalLibrary.BuildNormals(runShading, out string said);

        _window = new SerializedObject(this);
        TakeOver();
        Repaint();

        if (built) Debug.Log("[LevelSelectRunShadingTuner] " + said);
        else       Debug.LogWarning("[LevelSelectRunShadingTuner] " + said);
    }

    /// <summary>
    /// The decal groups: each a selection of the folder's drawings, ticked against their
    /// thumbnails, and the scatter they are stamped with. Stone types tick the groups they take.
    /// </summary>
    private void DrawDecalGroups(List<Texture2D> decals)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Decal Groups", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Each group is a selection of the drawings — click a thumbnail to tick it in or out — " +
            "with its own scatter. Stone types tick the groups they take, below. Several ticked " +
            "stack, the later group in this list on top, and each one ticked costs that stone " +
            "another scatter.",
            EditorStyles.wordWrappedMiniLabel);

        var list = _window.FindProperty("runShading")
                          .FindPropertyRelative(nameof(RiverRunShadingSettings.decalGroups));

        EditorGUI.BeginChangeCheck();

        for (int i = 0; i < list.arraySize; i++)
        {
            var group = list.GetArrayElementAtIndex(i);
            var name  = group.FindPropertyRelative(nameof(DecalGroup.name));

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
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.sortOrder)),
                                              new GUIContent("Sort Order"));
                EditorGUILayout.LabelField("Ticked On", StonesTicking(i), EditorStyles.wordWrappedMiniLabel);

                // Tiled fills every place at exactly Spacing across, so Amount and Scale go unread.
                var tiled = group.FindPropertyRelative(nameof(DecalGroup.uniformSpacing));

                using (new EditorGUI.DisabledScope(tiled.boolValue))
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.amount)));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.opacity)));
                var blendMode = group.FindPropertyRelative(nameof(DecalGroup.blendMode));
                EditorGUILayout.PropertyField(blendMode, new GUIContent("Blend Mode"));
                var mode = (DecalBlendMode)blendMode.enumValueIndex;
                if ((mode == DecalBlendMode.Overlay || mode == DecalBlendMode.SoftLight) &&
                    group.FindPropertyRelative(nameof(DecalGroup.aboveLighting)).boolValue)
                {
                    EditorGUILayout.LabelField(
                        "Overlay and Soft Light only work under the lighting — drawn as Normal " +
                        "while Appear Above Lighting is ticked.",
                        EditorStyles.wordWrappedMiniLabel);
                }
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.spacing)));
                EditorGUILayout.PropertyField(tiled, new GUIContent("Tiled"));

                // Blend Tiles only means anything while Tiled is on; its two settings only while it is.
                EditorGUI.indentLevel++;
                var blendTiles = group.FindPropertyRelative(nameof(DecalGroup.blendTiles));
                using (new EditorGUI.DisabledScope(!tiled.boolValue))
                {
                    EditorGUILayout.PropertyField(blendTiles, new GUIContent("Blend Tiles"));
                    EditorGUI.indentLevel++;
                    using (new EditorGUI.DisabledScope(!blendTiles.boolValue))
                    {
                        EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.blendNoiseScale)),
                                                      new GUIContent("Gradient Noise Scale"));
                        EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.blendNoiseSoftness)),
                                                      new GUIContent("Gradient Noise Softness"));
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;

                using (new EditorGUI.DisabledScope(tiled.boolValue))
                {
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.scaleMin)),
                                                  new GUIContent("Scale Min"));
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.scaleMax)),
                                                  new GUIContent("Scale Max"));
                }
                var rotate = group.FindPropertyRelative(nameof(DecalGroup.rotate));
                EditorGUILayout.PropertyField(rotate, new GUIContent("Random 90° Rotation"));
                using (new EditorGUI.DisabledScope(!rotate.boolValue))
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.rotateAmount)),
                                                  new GUIContent("Rotation Amount"));
                EditorGUILayout.PropertyField(group.FindPropertyRelative(nameof(DecalGroup.aboveLighting)),
                                              new GUIContent("Appear Above Lighting"));

                EditorGUILayout.LabelField("Drawings", EditorStyles.miniBoldLabel);
                var drawings = group.FindPropertyRelative(nameof(DecalGroup.drawings));

                if (group.FindPropertyRelative(nameof(DecalGroup.allDrawings)).boolValue)
                {
                    EditorGUILayout.LabelField(
                        "Every drawing on the sheet, as the stone this group was made from had. " +
                        "Rebuild the sheet to tick them one by one.",
                        EditorStyles.wordWrappedMiniLabel);
                }
                else if (decals.Count == 0)
                {
                    EditorGUILayout.LabelField("No drawings in the folder yet.",
                                               EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    DrawDrawingTicks(drawings, decals);
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

        using (new EditorGUI.DisabledScope(list.arraySize >= RiverRunShadingSettings.MaxDecalGroups))
        {
            if (GUILayout.Button("Add Decal Group")) _decalGroupAdd = true;
        }
    }

    /// <summary>Which stone types tick the group at this place in the list, as one line.</summary>
    private string StonesTicking(int groupIndex)
    {
        if (groupIndex >= runShading.decalGroups.Count) return string.Empty;
        string id = runShading.decalGroups[groupIndex]?.id;

        var stones = new List<string>();
        for (int s = 0; s < runShading.stoneTypes.Count; s++)
        {
            var type = runShading.stoneTypes[s];
            if (type != null && type.decalGroups != null && type.decalGroups.Contains(id))
                stones.Add("Stone " + RiverRunShadingSettings.StoneLetter(s));
        }

        return stones.Count == 0 ? "No stone yet" : string.Join(", ", stones);
    }

    /// <summary>
    /// The folder's drawings as thumbnails a group ticks in and out by clicking — ticked ones
    /// framed and at full strength, the rest faded.
    /// </summary>
    internal static void DrawDrawingTicks(SerializedProperty drawings, List<Texture2D> decals)
    {
        const float Tile = 48f;

        float usable = Mathf.Max(EditorGUIUtility.currentViewWidth - 50f, Tile);
        int   perRow = Mathf.Max(1, Mathf.FloorToInt(usable / (Tile + 4f)));

        for (int i = 0; i < decals.Count; i += perRow)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);

                for (int j = i; j < Mathf.Min(i + perRow, decals.Count); j++)
                {
                    string guid  = RiverRunDecalLibrary.Guid(decals[j]);
                    int    at    = IndexOf(drawings, guid);
                    bool   on    = at >= 0;

                    Rect tile = GUILayoutUtility.GetRect(Tile, Tile,
                                                         GUILayout.Width(Tile),
                                                         GUILayout.Height(Tile));

                    if (on)
                    {
                        EditorGUI.DrawRect(tile, new Color(0.24f, 0.49f, 0.91f, 1f));
                        tile = new RectOffset(2, 2, 2, 2).Remove(tile);
                    }

                    EditorGUI.DrawTextureTransparent(tile, decals[j], ScaleMode.ScaleToFit);
                    if (!on) EditorGUI.DrawRect(tile, new Color(0.2f, 0.2f, 0.2f, 0.7f));

                    if (GUI.Button(tile, new GUIContent(string.Empty,
                                       decals[j].name + (on ? " — ticked" : " — not ticked")),
                                   GUIStyle.none))
                    {
                        if (on) drawings.DeleteArrayElementAtIndex(at);
                        else
                        {
                            drawings.InsertArrayElementAtIndex(drawings.arraySize);
                            drawings.GetArrayElementAtIndex(drawings.arraySize - 1).stringValue = guid;
                        }
                        GUI.changed = true;
                    }
                }

                GUILayout.FlexibleSpace();
            }
        }
    }

    private static int IndexOf(SerializedProperty strings, string value)
    {
        for (int i = 0; i < strings.arraySize; i++)
            if (strings.GetArrayElementAtIndex(i).stringValue == value) return i;
        return -1;
    }

    /// <summary>
    /// A stone type's ticks: one per decal group, in the groups' order, each ticked one with the
    /// stone's own Intensity for it beside the tick.
    /// </summary>
    private void DrawStoneDecalGroups(SerializedProperty type)
    {
        if (runShading.decalGroups.Count == 0)
        {
            EditorGUILayout.LabelField("No decal groups yet — make one in Decal Groups, above.",
                                       EditorStyles.wordWrappedMiniLabel);
            return;
        }

        var ticked      = type.FindPropertyRelative(nameof(StoneType.decalGroups));
        var intensities = type.FindPropertyRelative(nameof(StoneType.decalGroupIntensities));

        // A tick made before the intensities existed has none yet: it takes its group as it always did.
        while (intensities.arraySize < ticked.arraySize)
        {
            intensities.InsertArrayElementAtIndex(intensities.arraySize);
            intensities.GetArrayElementAtIndex(intensities.arraySize - 1).floatValue = 1f;
        }

        foreach (var group in runShading.decalGroups)
        {
            if (group == null) continue;

            int  at   = IndexOf(ticked, group.id);
            Rect row  = EditorGUILayout.GetControlRect();
            Rect tick = row;
            if (at >= 0) tick.width = EditorGUIUtility.labelWidth;

            bool now = EditorGUI.ToggleLeft(tick, new GUIContent(group.name,
                "Stamp this group on the stone. The slider beside it is this stone's Intensity for " +
                "the group, 0 to 3, on top of the group's own Opacity: 1 as drawn, past 1 the " +
                "decals' soft and faint parts fill in, up to wholly covering the stone."), at >= 0);

            if (now == at >= 0)
            {
                if (at < 0) continue;

                var  intensity = intensities.GetArrayElementAtIndex(at);
                Rect slider  = new Rect(row.x + EditorGUIUtility.labelWidth, row.y,
                                        row.width - EditorGUIUtility.labelWidth, row.height);
                int  indent  = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                intensity.floatValue = EditorGUI.Slider(slider, intensity.floatValue, 0f, StoneType.MaxDecalGroupIntensity);
                EditorGUI.indentLevel = indent;
                continue;
            }

            if (now)
            {
                ticked.InsertArrayElementAtIndex(ticked.arraySize);
                ticked.GetArrayElementAtIndex(ticked.arraySize - 1).stringValue = group.id;
                intensities.InsertArrayElementAtIndex(ticked.arraySize - 1);
                intensities.GetArrayElementAtIndex(ticked.arraySize - 1).floatValue = 1f;
            }
            else
            {
                ticked.DeleteArrayElementAtIndex(at);
                intensities.DeleteArrayElementAtIndex(at);
            }
        }
    }

    // An Add or Remove pressed on the decal groups this repaint, carried out once the window has
    // finished drawing, like the stone types' own.
    private bool _decalGroupAdd;
    private int  _decalGroupRemove = -1;

    private void ApplyDecalGroupChange()
    {
        if (!_decalGroupAdd && _decalGroupRemove < 0) return;

        var groups = runShading.decalGroups;
        Undo.RecordObject(this, _decalGroupAdd ? "Add Decal Group" : "Remove Decal Group");

        if (_decalGroupAdd && groups.Count < RiverRunShadingSettings.MaxDecalGroups)
            groups.Add(runShading.NewDecalGroup("Decal Group " + (groups.Count + 1)));

        // Taken off every stone that ticked it too, so nothing is left ticking a group that is gone.
        if (_decalGroupRemove >= 0 && _decalGroupRemove < groups.Count)
        {
            string id = groups[_decalGroupRemove]?.id;
            groups.RemoveAt(_decalGroupRemove);
            foreach (var type in runShading.stoneTypes) type?.UntickDecalGroup(id);
        }

        _decalGroupAdd    = false;
        _decalGroupRemove = -1;
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    /// <summary>
    /// The folder's drawings across the window, wrapping, each over a chequer rather than over
    /// the window, so a drawing made of pale strokes is no more invisible than one made of dark.
    /// </summary>
    internal static void DrawDecalStrip(System.Collections.Generic.List<Texture2D> decals)
    {
        const float Tile = 48f;

        float usable = Mathf.Max(EditorGUIUtility.currentViewWidth - 30f, Tile);
        int   perRow = Mathf.Max(1, Mathf.FloorToInt(usable / (Tile + 4f)));

        for (int i = 0; i < decals.Count; i += perRow)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int j = i; j < Mathf.Min(i + perRow, decals.Count); j++)
                {
                    Rect tile = GUILayoutUtility.GetRect(Tile, Tile,
                                                         GUILayout.Width(Tile),
                                                         GUILayout.Height(Tile));

                    EditorGUI.DrawTextureTransparent(tile, decals[j], ScaleMode.ScaleToFit);

                    if (GUI.Button(tile, new GUIContent(string.Empty, decals[j].name),
                                   GUIStyle.none))
                    {
                        Selection.activeObject = decals[j];
                        EditorGUIUtility.PingObject(decals[j]);
                    }
                }

                GUILayout.FlexibleSpace();
            }
        }
    }

    /// <summary>
    /// Gathers the folder onto the sheet and takes what it laid out into this window's numbers,
    /// so the scene shows the new drawings straight away. Saving is still separate: the sheet and
    /// its cells are held on the preset with everything else, and until Save they are only here.
    /// </summary>
    private void RebuildDecalSheet()
    {
        bool built = RiverRunDecalLibrary.Build(runShading, out string said);

        // The window's own copy of the numbers changed underneath its SerializedObject, which
        // would otherwise draw the old ones back over them on the next repaint.
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();

        if (built) Debug.Log("[LevelSelectRunShadingTuner] " + said);
        else       Debug.LogWarning("[LevelSelectRunShadingTuner] " + said);
    }

    private void DrawPresetRow()
    {
        // The folder's presets by name, and an object field beside it for one held from anywhere
        // else. Both drive the same field: the dropdown feeds the object field, so whichever was
        // touched is the one that differs from what is held.
        var tint = TunerLoadedPreset.TintRow(activePreset, loadedPreset);
        using (new EditorGUILayout.HorizontalScope())
        {
            var picked = LevelSelectRiverPresetLibrary.DrawPicker(
                new GUIContent("Active Preset",
                               "The presets kept in " + LevelSelectRiverPresetLibrary.Folder +
                               ". One preset carries both of the river's looks — the ripple " +
                               "lines on the water and the shading of the stone runs — so this " +
                               "is the same preset the other tuner and the designer hold."),
                activePreset);

            var chosen = (LevelSelectRiverStructurePreset)EditorGUILayout.ObjectField(
                picked, typeof(LevelSelectRiverStructurePreset), false, GUILayout.Width(150f));

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
                "world reads whichever preset the Level Select Designer holds, not this one. " +
                "It is the same preset the river's water lines are saved on.",
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

        runShading.CopyFrom(activePreset.runShading);
        loadedPreset = activePreset;
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    private void SaveToPreset(string reminder = null)
    {
        if (!TunerLoadedPreset.IsLoaded(activePreset, loadedPreset)) return;

        if (!EditorUtility.DisplayDialog(
                "Save Level Select Run Shading",
                reminder +
                $"Overwrite the run shading on '{activePreset.name}' with the current tuner " +
                "values?\n\nThe water lines saved on the same preset are left alone.\n\n" +
                "This cannot be undone from disk.\n\n" +
                TunerSaveChanges.Describe(activePreset, p => p.runShading.CopyFrom(runShading)),
                "Save", "Cancel"))
            return;

        Undo.RecordObject(activePreset, "Save Level Select Run Shading");
        activePreset.runShading.CopyFrom(runShading);
        EditorUtility.SetDirty(activePreset);
        AssetDatabase.SaveAssetIfDirty(activePreset);

        Debug.Log($"[LevelSelectRunShadingTuner] Saved to {activePreset.name}");
    }

    // Ten minutes of changes the preset hasn't got, and Save asks for itself.
    private void RemindToSave() => _saveReminder.Tick(
        () => TunerLoadedPreset.IsLoaded(activePreset, loadedPreset) &&
              TunerSaveChanges.List(activePreset, p => p.runShading.CopyFrom(runShading)).Count > 0,
        SaveToPreset);

    private void SaveAsNewPreset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Level Select River Structure Preset", "NewLevelSelectStructurePreset", "asset",
            "Choose save location", LevelSelectRiverPresetLibrary.EnsureFolder());
        if (string.IsNullOrEmpty(path)) return;

        var preset = CreateInstance<LevelSelectRiverStructurePreset>();
        preset.runShading.CopyFrom(runShading);

        AssetDatabase.CreateAsset(preset, path);
        AssetDatabase.SaveAssets();

        activePreset = preset;
        loadedPreset = preset;
        RememberPreset();

        Debug.Log($"[LevelSelectRunShadingTuner] Saved new preset at {path}");
    }
}
