using UnityEditor;
using UnityEngine;

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
    [MenuItem("Tools/Waves/Level Select Run Shading Tuner")]
    public static void Open() =>
        GetWindow<LevelSelectRunShadingTuner>("Level Select Run Shading");

    private const string PrefPreset  = "LevelSelectRunShadingTuner_PresetGUID";

    [SerializeField] private LevelSelectRiverStructurePreset  activePreset;
    [SerializeField] private bool                    applyLive  = true;
    [SerializeField] private RiverRunShadingSettings runShading = new RiverRunShadingSettings();

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
                activePreset = AssetDatabase.LoadAssetAtPath<LevelSelectRiverStructurePreset>(path);
        }

        // A pump of its own, so the window still drives a scene with no designer data and no
        // controller in it — and so the numbers survive the shader reimport that empties them.
        EditorApplication.update += Drive;
        TakeOver();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Drive;
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
        _window.Update();

        DrawToolbar();
        DrawPresetRow();

        EditorGUILayout.Space();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawSeamDataRow();

        EditorGUILayout.Space();
        DrawDecalRow();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Run Shading", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Five things, all off the same numbers. The stone itself — a colour each for the " +
            "outer faces, the rim lip and the inside of the channel, each with its own noise " +
            "grain. Dark gathered along every seam of it — the rim's two edges, the foot of the " +
            "outer wall, the joints between pieces. White rising off the waterline up the inside " +
            "of the channel. Hand-drawn decals scattered over the lot. And the made-up " +
            "light the stone is shaped by, which is what replaced " +
            "Simulated Lighting Basic on the run shader. Grain size, the decal spacing " +
            "and scales, and the waterline extent are " +
            "metres; the seam extent is a percentage of the width of each surface, so every piece " +
            "is shaded in proportion to its own size. The sections below choose which of the three " +
            "stone colours each part of an outpost, tower, arena wall and archway takes — those " +
            "are baked into the meshes, so rebuild after changing them.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUI.BeginChangeCheck();

        var shading = _window.FindProperty("runShading");
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

        EditorGUILayout.EndScrollView();
        _window.ApplyModifiedProperties();
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
                           : $"{noKinds} of {runs} runs carry no face kinds, so the three stone " +
                             "colours cannot reach them";

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
        else if (runShading.decalAmount <= 0f)
        {
            EditorGUILayout.HelpBox(
                "The sheet holds " + runShading.decalSheetHeld + " of the folder's " +
                decals.Count + ", and Amount is 0, so none of them is drawn. Turn Amount up, " +
                "below.",
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
    }

    /// <summary>
    /// The folder's drawings across the window, wrapping, each over a chequer rather than over
    /// the window, so a drawing made of pale strokes is no more invisible than one made of dark.
    /// </summary>
    private void DrawDecalStrip(System.Collections.Generic.List<Texture2D> decals)
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
        _window = new SerializedObject(this);
        TakeOver();
        Repaint();
    }

    private void SaveToPreset()
    {
        if (activePreset == null) return;

        if (!EditorUtility.DisplayDialog(
                "Save Level Select Run Shading",
                $"Overwrite the run shading on '{activePreset.name}' with the current tuner " +
                "values?\n\nThe water lines saved on the same preset are left alone.\n\n" +
                "This cannot be undone from disk.",
                "Save", "Cancel"))
            return;

        Undo.RecordObject(activePreset, "Save Level Select Run Shading");
        activePreset.runShading.CopyFrom(runShading);
        EditorUtility.SetDirty(activePreset);
        AssetDatabase.SaveAssetIfDirty(activePreset);

        Debug.Log($"[LevelSelectRunShadingTuner] Saved to {activePreset.name}");
    }

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
        RememberPreset();

        Debug.Log($"[LevelSelectRunShadingTuner] Saved new preset at {path}");
    }
}
