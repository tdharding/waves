using UnityEngine;
using UnityEditor;

/// <summary>
/// Allocates fog masses across an arena — the second of Fog Studio's two tools.
///
/// The cloud designer says what a mass looks like; this says where the masses are. They are kept
/// apart because they answer different questions and change at different times: a shape is drawn
/// once and reused everywhere, a map is level geography that changes per level.
///
/// The view is the 32x32 level grid the rest of the level tools use, so a cloud placed over a
/// channel here lands on that channel in the game. Each mass draws its actual outline, not a
/// marker — the whole point of placing by hand is seeing whether two banks leave a gap you can
/// steer through, and a dot cannot tell you that.
/// </summary>
public class FogMapWindow : EditorWindow
{
    const int GRID_CELLS = 32;      // matches GridData.GridSize
    const int PREVIEW_PX = 40;      // resolution each cloud's outline is rasterised at

    [MenuItem("Waves/Fog Map")]
    public static void Open()
    {
        var w = GetWindow<FogMapWindow>("Fog Map");
        w.minSize = new Vector2(720, 560);
    }

    FogMap _map;
    SerializedObject _so;
    int _selected = -1;
    Vector2 _scroll;
    bool _dragging;

    // How much water the view shows, in world units. The view is NOT the tile: if it were, the
    // boat mask would appear to change size whenever the tile was resized, because everything
    // would be scaled by the tile. Holding the view in world units means the mask stays put and
    // the arrangement grows and shrinks against it, which is the relationship being authored.
    float _viewSpan = 120f;

    // The preview runs on its own clock so the arrangement can be watched travelling on the wind.
    // Editor windows do not tick on their own, so this is driven off the editor's wall clock and
    // the window asks for constant repaint while a wind is set.
    double _lastTick;
    bool _animate = true;

    string _savedJson;
    bool IsDirty => _map != null && EditorJsonUtility.ToJson(_map) != _savedJson;
    void TakeSnapshot() => _savedJson = _map != null ? EditorJsonUtility.ToJson(_map) : null;

    void SaveMap()
    {
        if (_map == null) return;
        EditorUtility.SetDirty(_map);
        AssetDatabase.SaveAssetIfDirty(_map);
        TakeSnapshot();
    }

    void RevertMap()
    {
        if (_map == null || _savedJson == null) return;
        EditorJsonUtility.FromJsonOverwrite(_savedJson, _map);
        EditorUtility.SetDirty(_map);
        _so = null;
    }

    // ────────────────────────────────────────────────────────────────────────
    // EditorWindow has no RequiresConstantRepaint — that is an Editor (inspector) method. A window
    // has to drive its own repaints, so it rides the editor's update tick while there is motion to
    // show, and stops asking the moment there is not.
    void OnEnable()
    {
        _lastTick = EditorApplication.timeSinceStartup;
        EditorApplication.update += OnEditorTick;
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorTick;
    }

    void OnEditorTick()
    {
        if (_animate && _map != null && _map.windSpeed > 0.0001f) Repaint();
    }

    /// <summary>
    /// Advance the preview along the wind. Held in fractions of a tile and wrapped, so it never
    /// grows without bound however long the window is left open.
    /// </summary>
    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTick);
        _lastTick = now;
        if (!_animate || _map == null) return;
        if (dt <= 0f || dt > 0.5f) return;      // ignore the first frame and any editor stall

        AdvanceSample(dt);
    }

    /// <summary>
    /// Run the spawner's own loop on the sample: drift on the wind, drop what leaves the cull
    /// radius, throw replacements into the birth ring.
    ///
    /// This is the whole point of the canvas now. It used to slide a fixed arrangement across a
    /// tile and wrap it, which showed the pattern travelling but could never show the thing that
    /// actually decides what fog looks like — masses arriving and leaving. Watch it for a minute
    /// and the behaviour you get on the water is on screen, including the ways it can go wrong:
    /// masses bunching downwind, or the ring failing to keep the middle populated.
    /// </summary>
    void AdvanceSample(float dt)
    {
        EnsureSample();

        // Spawn puts them there, cull takes them away. The mask does neither.
        float cullSq  = _map.cullRadius * _map.cullRadius;
        float spawnSq = _map.spawnRadius * _map.spawnRadius;
        Vector2 wind = _map.WindVector;

        for (int i = _sample.Count - 1; i >= 0; i--)
        {
            var s = _sample[i];
            s.pos += wind * dt;
            if (s.pos.sqrMagnitude > cullSq) _sample.RemoveAt(i);
            else _sample[i] = s;
        }

        // Births happen in the ring only, exactly as the manager does it, so the canvas shows fog
        // arriving from out of sight rather than appearing in front of you.
        int want = _map.blobCount;
        float gapSq = _map.spacing * _map.spacing;

        while (_sample.Count < want)
        {
            bool placed = false;
            for (int attempt = 0; attempt < 24 && !placed; attempt++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Mathf.Lerp(spawnSq * 0.81f, spawnSq, Random.value));
                Vector2 q = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;

                bool clear = true;
                for (int j = 0; j < _sample.Count; j++)
                    if ((_sample[j].pos - q).sqrMagnitude < gapSq) { clear = false; break; }
                if (!clear) continue;

                _sample.Add(new Sample
                {
                    pos   = q,
                    rot   = Random.value * 360f,
                    scale = Random.Range(_map.blobScaleMin, _map.blobScaleMax),
                });
                placed = true;
            }
            if (!placed) break;   // no room at this spacing
        }

        Repaint();
    }


    void OnGUI()
    {
        Tick();
        DrawToolbar();

        if (_map == null)
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "Pick a Fog Map, or make one with Create > Waves > Fog Map. Assign it " +
                "to a level on its GridData, next to the sonar grid.", MessageType.Info);
            if (GUILayout.Button("Create a new map", GUILayout.Height(28))) CreateMap();
            return;
        }

        _so ??= new SerializedObject(_map);
        _so.Update();

        // The panel first, then the edits applied, THEN the canvas. Drawing the canvas before
        // ApplyModifiedProperties meant it read last frame's values, so a ring appeared frozen
        // until something else forced a repaint.
        EditorGUILayout.BeginHorizontal();
        DrawPanel();
        if (_so.ApplyModifiedProperties()) EditorUtility.SetDirty(_map);
        DrawSplitter();
        DrawArena();
        EditorGUILayout.EndHorizontal();

        string want = IsDirty ? "Fog Map *" : "Fog Map";
        if (titleContent.text != want) titleContent = new GUIContent(want);
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        var picked = (FogMap)EditorGUILayout.ObjectField(
            _map, typeof(FogMap), false, GUILayout.Width(220));
        if (picked != _map)
        {
            if (ConfirmDiscard()) { _map = picked; _so = null; _selected = -1; TakeSnapshot(); }
        }

        using (new EditorGUI.DisabledScope(_map == null || !IsDirty))
        {
            if (GUILayout.Button(IsDirty ? "Save *" : "Save", EditorStyles.toolbarButton,
                                 GUILayout.Width(60))) SaveMap();
            if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(58)))
            {
                if (EditorUtility.DisplayDialog("Revert map",
                        $"Discard every change to {_map.name} since it was last saved?",
                        "Revert", "Keep editing")) RevertMap();
            }
        }

        GUILayout.FlexibleSpace();

        GUILayout.Label("View", EditorStyles.miniLabel, GUILayout.Width(32));
        // Down to 4 units: a boat-centred simulation can run far smaller than a level, and a
        // floor of 30 made the tightest arrangements unreadable — every mass a few pixels wide.
        _viewSpan = GUILayout.HorizontalSlider(_viewSpan, 4f, 400f, GUILayout.Width(80));
        GUILayout.Label($"{_viewSpan:0} u", EditorStyles.miniLabel, GUILayout.Width(42));

        GUILayout.Space(8);

        _animate = GUILayout.Toggle(_animate, new GUIContent(_animate ? "Wind ▶" : "Wind ⏸",
            "Run the spawner: masses drift on the wind, drop out at the cull radius, and new " +
            "ones are born in the ring beyond the mask. What you see here is what the water does."),
            EditorStyles.toolbarButton, GUILayout.Width(64));

        using (new EditorGUI.DisabledScope(_map == null))
        {
            if (GUILayout.Button(new GUIContent("Refresh Preview",
                    "Reset the fog in the open scene to this map. Existing masses are cleared " +
                    "and refilled from the map as it now stands, so the scene shows exactly what " +
                    "is placed here and nothing left over."),
                    EditorStyles.toolbarButton))
                PreviewInScene();
        }

        EditorGUILayout.LabelField(
            _map != null ? $"{_map.blobCount} masses" : "", EditorStyles.miniLabel,
            GUILayout.Width(90));

        // Live state, in the manner of the Wave Effects Tuner: while a level is running this map
        // is not a document you save and reload, it is the thing on the water.
        if (Application.isPlaying && _map != null)
        {
            var live = Object.FindAnyObjectByType<FogFieldManager>();
            bool on  = live != null && live.ActiveMap == _map;
            GUILayout.Label(on ? "● live" : "○ not deployed", EditorStyles.miniLabel,
                            GUILayout.Width(80));

            using (new EditorGUI.DisabledScope(live == null))
                if (GUILayout.Button(new GUIContent("Deploy",
                        "Hand this map to the running fog field, replacing whatever it holds."),
                        EditorStyles.toolbarButton, GUILayout.Width(55)))
                    FogFieldManager.ApplyArenaMap(_map);
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Push this map into the open scene's fog field so it can be judged on the water.
    ///
    /// The designer is the authority on placement, so previewing has to mean "the scene now shows
    /// exactly this map" — not "this map plus whatever was already drifting about". Existing
    /// masses are retired rather than deleted so they dissolve, and the field is switched on,
    /// because a map that appears to do nothing because fog was off is a bad half-hour.
    ///
    /// Saves first: the manager holds an asset reference, so unsaved edits would not be what the
    /// scene reads.
    /// </summary>
    void PreviewInScene()
    {
        if (_map == null) return;
        if (IsDirty) SaveMap();

        var mgr = Object.FindAnyObjectByType<FogFieldManager>();
        if (mgr == null)
        {
            EditorUtility.DisplayDialog("No fog field in this scene",
                "There is no FogFieldManager in the open scene, so there is nothing to preview on.\n\n" +
                "Build one with Waves > Fog Studio Scene > Build Rig.", "OK");
            return;
        }

        Undo.RecordObject(mgr, "Preview Fog Map");
        var so = new SerializedObject(mgr);
        so.FindProperty("fogMap").objectReferenceValue = _map;
        so.FindProperty("fogEnabled").boolValue = true;
        so.ApplyModifiedProperties();

        // Takes effect on this frame rather than waiting for a domain reload, clears the slot
        // bookkeeping so masses refill against the map as it now stands, and pushes the look.
        FogFieldManager.ApplyArenaMap(_map);
        _map.ApplyLook();

        EditorUtility.SetDirty(mgr);
        Selection.activeGameObject = mgr.gameObject;
        SceneView.RepaintAll();
    }

    bool ConfirmDiscard()
    {
        if (!IsDirty) return true;
        int choice = EditorUtility.DisplayDialogComplex("Unsaved fog arena map",
            $"{_map.name} has changes that are not saved to disk.", "Save", "Cancel", "Discard");
        if (choice == 0) { SaveMap(); return true; }
        if (choice == 2) { RevertMap(); return true; }
        return false;
    }

    void OnDestroy()
    {
        if (!IsDirty) return;
        if (EditorUtility.DisplayDialog("Unsaved fog arena map",
                $"{_map.name} has unsaved changes. Closing will not save them.", "Save", "Discard"))
            SaveMap();
        else RevertMap();
    }

    void CreateMap()
    {
        const string dir = "Assets/Resources/FogMaps";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/Resources", "FogMaps");

        string path = EditorUtility.SaveFilePanelInProject(
            "New Fog Map", "FogMap", "asset", "", dir);
        if (string.IsNullOrEmpty(path)) return;

        var m = CreateInstance<FogMap>();
        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.SaveAssets();
        _map = m; _so = null; _selected = -1;
        TakeSnapshot();
    }

    // ────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// A field, or nothing at all when the name no longer resolves. PropertyField throws on a null
    /// property, and one bad name inside OnGUI takes the entire window down — blank panel, no
    /// arena, hundreds of exceptions a second. Renaming a serialized field should cost a missing
    /// row, not the tool.
    /// </summary>
    void Field(string name, string label = null)
    {
        // Guarded as well as fixed at the source: a null here throws mid-layout, and an exception
        // inside OnGUI unbalances the Begin/End stack and buries the real error under GUI noise.
        if (_so == null) return;

        var p = _so.FindProperty(name);
        if (p == null)
        {
            EditorGUILayout.LabelField(label ?? name, "— missing —", EditorStyles.miniLabel);
            return;
        }
        if (label == null) EditorGUILayout.PropertyField(p);
        else EditorGUILayout.PropertyField(p, new GUIContent(label));
    }

    // Panel width, dragged by the splitter. Kept in EditorPrefs rather than on the window, so it
    // survives the domain reload every recompile causes — otherwise it snaps back to the default
    // several times an hour while you are working on the fog scripts.
    const string PanelWidthKey = "FogMap.PanelWidth";
    float _panelWidth = -1f;
    bool  _resizing;

    void LoadPanelWidth()
    {
        if (_panelWidth < 0f)
            _panelWidth = Mathf.Clamp(EditorPrefs.GetFloat(PanelWidthKey, 300f), 220f, 900f);
    }

    /// <summary>
    /// The drag handle between the controls and the canvas.
    ///
    /// Hit area is wider than the line it draws: a two-pixel target is findable only by accident,
    /// and this sits between two things you are constantly moving the mouse across.
    /// </summary>
    void DrawSplitter()
    {
        Rect r = GUILayoutUtility.GetRect(6f, 6f, GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));

        Rect hit = new Rect(r.x - 3f, r.y, r.width + 6f, r.height);
        EditorGUIUtility.AddCursorRect(hit, MouseCursor.ResizeHorizontal);

        Event e = Event.current;
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && hit.Contains(e.mousePosition):
                _resizing = true; e.Use();
                break;

            case EventType.MouseDrag when _resizing:
                // Clamped so neither side can be dragged away entirely — a canvas of nothing and a
                // panel of nothing are both states you cannot drag your way back out of.
                _panelWidth = Mathf.Clamp(e.mousePosition.x, 220f, Mathf.Max(260f, position.width - 260f));
                Repaint(); e.Use();
                break;

            case EventType.MouseUp when _resizing:
                _resizing = false;
                EditorPrefs.SetFloat(PanelWidthKey, _panelWidth);
                e.Use();
                break;
        }
    }

    /// <summary>
    /// The blob on its own, at a size you can read. Drawn from the same generator the water uses,
    /// so the limbs here are the limbs you get.
    /// </summary>
    void DrawBlobPortrait()
    {
        Rect r = GUILayoutUtility.GetRect(10, 10000, 150, 150);
        EditorGUI.DrawRect(r, new Color(0.09f, 0.11f, 0.14f));

        Handles.BeginGUI();
        Handles.color = new Color(0.78f, 0.86f, 0.98f, 0.9f);
        // Sized to the box rather than to world units: this is a portrait of the shape, not a
        // measurement of it. The Fog Scale section is where size is judged.
        DrawCloudOutline(r.center, Mathf.Min(r.width, r.height) * 0.62f, 0f, _map.properties);
        Handles.EndGUI();
    }

    void DrawPanel()
    {
        LoadPanelWidth();
        EditorGUILayout.BeginVertical(GUILayout.Width(_panelWidth));
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Blob", EditorStyles.boldLabel);

        // The mass itself, drawn once at a readable size. With a list of blobs there was nothing
        // sensible to show here and the only view of a shape was as one of a dozen scattered
        // across the canvas at whatever size the distribution gave it.
        DrawBlobPortrait();

        Field("properties");

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Distribution", EditorStyles.boldLabel);
        Field("blobCount", "Blob Count");
        Field("spacing", "Spacing");

        // Spacing is world units now and measured between masses, so it can be read against blob
        // size directly — the pairing that decides whether fog reads as a bank or as separate puffs.
        var sz = _map.WorldBlobScale;
        EditorGUILayout.LabelField(" ",
            $"masses are {sz.x:0.##}–{sz.y:0.##} across, kept {_map.spacing:0.##} apart",
            EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Preview", EditorStyles.miniLabel, GUILayout.Width(60));
        if (GUILayout.Button(new GUIContent("Resample",
                "Throw another set. The canvas shows ONE outcome of the spawner, not a stored " +
                "arrangement — nothing on the asset decides where masses land any more, so this " +
                "changes nothing on disk."), GUILayout.Width(80)))
        {
            _previewSeed = Random.Range(1, int.MaxValue);
            Repaint();
        }
        EditorGUILayout.EndHorizontal();

        // The count asked for and the count achieved are different numbers whenever spacing is too
        // wide to fit them all, and silently getting fewer masses than you typed is the kind of
        // thing you would otherwise chase for an hour.
        EnsureSample();
        int want = _map.blobCount;
        if (_sample.Count < want)
            EditorGUILayout.HelpBox(
                $"Asked for {want} masses, fitted {_sample.Count}. Spacing is too wide for the " +
                "water inside the cull radius — lower Spacing, or widen the mask to give them room.",
                MessageType.Warning);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Wind", EditorStyles.boldLabel);
        Field("windAngle");
        Field("windSpeed");
        EditorGUILayout.HelpBox(
            "The wind carries the masses and the whole tiled arrangement together, so fog travels " +
            "without the pattern emptying out behind it. It is now the ONLY motion fog has. " +
            "These are the starting values. On level load they are copied to the FogFieldManager, " +
            "which is what the fog then reads — but editing them here during play pushes straight " +
            "through to it, so either end works.", MessageType.None);


        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Boat Mask", EditorStyles.boldLabel);
        Field("fogOpacity", "Overall Opacity");
        Field("maskRadius");
        Field("maskFeather");

        Field("spawnRadius", "Spawn Radius");
        Field("cullRadius", "Cull Radius");
        EditorGUILayout.HelpBox(
            "Three radii, one job each. Spawn Radius is where masses are born. Cull Radius is " +
            "where they are deleted. Mask Radius is where they are faded out, on the material. " +
            "None of them reads either of the others.", MessageType.None);
        EditorGUILayout.HelpBox(
            "The blue rings. How far from the boat fog is drawn at all, and how much of that is a " +
            "fade band rather than a hard edge. Starting values — copied to the FogFieldManager " +
            "on level load, and editing them here during play pushes straight through, so either " +
            "end works. " +
            "The radius carries more than it looks: cull is derived from it, the painted texture " +
            "window from cull, and masses may only be born outside it. Widening it widens all " +
            "three together.", MessageType.None);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Boat Push", EditorStyles.boldLabel);
        Field("boatRepelRadius", "Hull Radius");
        Field("boatRepelClearRadius", "Clear Radius");
        Field("boatRepelStrength", "Strength");
        EditorGUILayout.LabelField(" ",
            $"clears {_map.boatRepelRadius + _map.boatRepelClearRadius:0.##} world units",
            EditorStyles.miniLabel);
        EditorGUILayout.HelpBox(
            "How fog parts around the hull. On the arena rather than on the boat, because how " +
            "readily fog gives way is a property of the weather — thin haze barely notices a " +
            "hull, a thick bank shoulders well clear. Strength 0 lets fog close straight over you.",
            MessageType.None);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Fog Scale", EditorStyles.boldLabel);
        // blobScale is a C# property over blobScaleMin/Max, so it has no SerializedProperty of
        // its own — asking for one returns null and PropertyField throws.
        Field("blobScaleMin", "Min");
        Field("blobScaleMax", "Max");
        EditorGUILayout.LabelField(" ",
            $"world units, against {_map.spacing:0.##} of spacing", EditorStyles.miniLabel);
        EditorGUILayout.HelpBox(
            "Spine length in world units. Starting values: copied to the FogFieldManager on level " +
            "load, and editing them here during play pushes straight through, so either end works." +
            "\n\nRead these against Spacing. Sizes near the spacing give a continuous bank; sizes " +
            "well under it give separate puffs with water between them.", MessageType.None);

        if (_map.blobScaleMax > _viewSpan)
            EditorGUILayout.HelpBox(
                $"A mass is {_map.blobScaleMax:0} units across — wider than the {_viewSpan:0}-unit " +
                "view. Zoom out, or check this is the size you meant.", MessageType.Warning);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Simulation", EditorStyles.boldLabel);
        Field("unitsPerTexel", "Detail (u/texel)");
        Field("maxGridResolution", "Max Resolution");

        // Resolution is derived from the detail figure and how much water the mask covers, so
        // widening the mask allocates a bigger texture rather than coarsening the fog. Shown
        // because it is the number that actually costs, and because hitting the cap is silent.
        float window = _map.cullRadius * 2f * 1.45f;
        int wanted = Mathf.CeilToInt(window / Mathf.Max(_map.unitsPerTexel, 0.001f));
        int cap    = Mathf.Clamp(_map.maxGridResolution, 32, 2048);
        int capped = Mathf.Clamp(wanted, 32, cap);
        EditorGUILayout.LabelField(" ",
            $"{capped} px across {window:0.#} u" + (capped < wanted ? "   (capped)" : ""),
            EditorStyles.miniLabel);
        if (capped < wanted)
            EditorGUILayout.HelpBox(
                $"This mask wants {wanted} px at {_map.unitsPerTexel:0.###} u/texel but the cap is " +
                $"{cap}, so the fog is coarser than asked for. Raise the cap, or the detail figure.",
                MessageType.Warning);

        Field("heaviness", "Heaviness");
        Field("blurRadius", "Blur");
        Field("heightBlurRadius", "Height Blur");

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Pushing", EditorStyles.boldLabel);
        Field("repelStrength", "Repel Strength");
        Field("rockClearRadius", "Rock Clear Radius");
        Field("rockStrength", "Rock Strength");
        Field("rockRescanInterval", "Rock Rescan");
        Field("lampClearFraction", "Lamp Clear Fraction");
        Field("lampClearRadius", "Lamp Clear Radius");
        Field("lampStrength", "Lamp Strength");

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Look", EditorStyles.boldLabel);
        Field("fogMaterial");
        Field("overrideLook");

        using (new EditorGUI.DisabledScope(_map.fogMaterial == null))
            if (GUILayout.Button(new GUIContent("Pull From Material",
                    "Read every Look value off the material and into this map. For when the " +
                    "material is ahead — one press and the map matches what is on screen.")))
            {
                Undo.RecordObject(_map, "Pull Fog Look From Material");
                _map.PullFromMaterial();
                EditorUtility.SetDirty(_map);

                // Re-read, do NOT drop the SerializedObject. Nulling it mid-draw left every later
                // Field() in this same pass dereferencing null, which threw inside the layout and
                // took the window's Begin/End balance down with it.
                _so.Update();
            }

        if (_map.fogMaterial == null)
            EditorGUILayout.HelpBox("No material assigned, so the Look values reach nothing.",
                                    MessageType.Warning);

        if (_map.overrideLook)
        {
            if (_map.fogMaterial == null)
                EditorGUILayout.HelpBox(
                    "Override is on but no material is assigned, so nothing is pushed.",
                    MessageType.Warning);

            foreach (var name in new[] {
                "threshold", "edgeSoftness", "undulationAmount", "undulationScale",
                "lipWidth", "lipLighting", "lipCurvature", "heightScale",
                "fogColour", "litColour", "ambient", "interiorFill", "transparencyFalloff",
                "grainAmount", "grainScale" })
                Field(name);

            EditorGUILayout.HelpBox(
                "Pushed to the material on Refresh Preview and at level start — not every frame, " +
                "so the material stays tunable while you look at it.", MessageType.None);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Masses are not placed by hand any more — the distribution above lays them out. Click " +
            "one to see which blob it came from; edit that blob to change every mass drawn from " +
            "it. To move masses, reroll the seed.", MessageType.None);

        EnsureSample();
        if (_selected >= 0 && _selected < _sample.Count)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"Mass {_selected + 1}", EditorStyles.boldLabel);
            var s = _sample[_selected];

            // Read-only, and for a different reason than before: this mass does not exist on the
            // asset at all. It is one throw of the spawner, and editing it would be editing a
            // sample rather than the thing that produces samples.
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Limbs", $"{_map.properties.EffectiveLimbCount}");
                EditorGUILayout.LabelField("Spine", $"{s.scale:0.##} world units");
                EditorGUILayout.LabelField("Rotation", $"{s.rot:0}\u00B0");
                EditorGUILayout.LabelField("Distance",
                    $"{s.pos.magnitude:0.##} from the boat");
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ────────────────────────────────────────────────────────────────────────
    void DrawArena()
    {
        EditorGUILayout.BeginVertical();
        Rect r = GUILayoutUtility.GetRect(10, 10000, 10, 10000);
        int side = Mathf.FloorToInt(Mathf.Min(r.width, r.height)) - 8;
        Rect box = new Rect(r.x + (r.width - side) * 0.5f, r.y + (r.height - side) * 0.5f, side, side);

        EditorGUI.DrawRect(box, new Color(0.09f, 0.11f, 0.14f));
        DrawBoatMask(box);
        DrawClouds(box);
        DrawWind(box);
        HandleInput(box);

        EditorGUILayout.EndVertical();
    }


    /// <summary>
    /// The boat, and the range fog is visible within.
    ///
    /// The tile follows the boat, so the boat sits at the centre of a tile by definition — that is
    /// what the middle of this view means. The two discs are the LOD: fog inside the inner one is
    /// at full strength, fog between them is fading, and beyond the outer one nothing paints at
    /// all. Masses allocated outside it are not wrong, they are simply what you meet later.
    ///
    /// Read from the FogFieldManager in the open scene so it is the range the game will use. With
    /// no manager to ask, nothing is drawn rather than a made-up circle.
    /// </summary>
    void DrawBoatMask(Rect box)
    {
        if (_map == null) return;

        // Read from THE MAP BEING EDITED, not from a FogFieldManager in the open scene. Reading
        // the scene meant these rings described whatever map that manager happened to hold — or,
        // with none assigned, a fallback constant. Editing maskRadius moved nothing on screen and
        // the label showed a number that appeared nowhere in the asset.
        Vector2 centre = box.center;                 // the boat sits at the middle of the view
        float perUnit = PixelsPerUnit(box);

        Handles.BeginGUI();

        // One thick solid ring per radius. Nothing here multiplies one radius by another.
        Ring(centre, _map.maskRadius  * perUnit, new Color(0.40f, 0.70f, 1.00f, 1f));
        Ring(centre, _map.spawnRadius * perUnit, new Color(0.45f, 0.95f, 0.50f, 1f));
        Ring(centre, _map.cullRadius  * perUnit, new Color(1.00f, 0.45f, 0.25f, 1f));

        Handles.color = new Color(1f, 0.85f, 0.4f, 1f);
        Handles.DrawSolidDisc(centre, Vector3.forward, 4f);
        Handles.EndGUI();

        var l1 = new Rect(centre.x + 8f, centre.y - 20f, 260f, 14f);
        var l2 = new Rect(centre.x + 8f, centre.y - 6f, 260f, 14f);
        GUI.Label(l1, "boat", EditorStyles.miniLabel);
        GUI.Label(l2, $"mask {_map.maskRadius:0.#}   spawn {_map.spawnRadius:0.#}   cull {_map.cullRadius:0.#}",
                  EditorStyles.miniLabel);
    }

    static void Ring(Vector2 centre, float radius, Color colour)
    {
        if (radius <= 0.5f) return;
        Handles.color = colour;

        const int seg = 96;
        var pts = new Vector3[seg + 1];
        for (int i = 0; i <= seg; i++)
        {
            float a = (i / (float)seg) * Mathf.PI * 2f;
            pts[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        Handles.DrawAAPolyLine(3f, pts);
    }

    /// <summary>
    /// Which way the wind carries the fog. Worth seeing while placing: masses travel along it, so
    /// a gap you leave upwind is a gap that arrives later, and a bank placed downwind of the boat
    /// is one the player has already passed.
    /// </summary>
    void DrawWind(Rect box)
    {
        if (_map == null || _map.windSpeed <= 0.0001f) return;

        float a = _map.windAngle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(a), -Mathf.Sin(a));   // GUI y runs down
        Vector2 from = new Vector2(box.x + 34f, box.yMax - 34f);
        Vector2 to = from + dir * 44f;

        Handles.BeginGUI();
        Handles.color = new Color(0.75f, 0.85f, 1f, 0.85f);
        Handles.DrawAAPolyLine(3f, from, to);
        Vector2 side = new Vector2(-dir.y, dir.x) * 7f;
        Handles.DrawAAConvexPolygon(to, (Vector3)(to - dir * 12f + side),
                                    (Vector3)(to - dir * 12f - side));
        Handles.EndGUI();

        GUI.Label(new Rect(box.x + 8f, box.yMax - 20f, 200f, 18f),
                  $"wind {_map.windAngle:0}\u00B0  {_map.windSpeed:0.##} u/s", EditorStyles.miniLabel);
    }

    /// <summary>One mass in the preview. A sample of what the spawner would do, not a record.</summary>
    struct Sample { public Vector2 pos; public float rot, scale; }

    readonly System.Collections.Generic.List<Sample> _sample =
        new System.Collections.Generic.List<Sample>();
    int _sampleKey, _previewSeed = 1;

    /// <summary>
    /// Throw a set of masses the way FogFieldManager does, so the canvas shows what the water
    /// would actually look like.
    ///
    /// This is a SIMULATION, not a stored arrangement. There is nothing on the asset saying where
    /// masses go any more — the spawner throws points into the ring around the boat and keeps the
    /// ones far enough from what is already there. So the canvas can only ever show one possible
    /// outcome, and Resample rolls another. Anything it draws that you want to keep has to be
    /// achieved through Count and Spacing, because those are the only things that carry over.
    /// </summary>
    void EnsureSample()
    {
        int key = _map.blobCount * 92821
                  ^ Mathf.RoundToInt(_map.spacing * 1000f) * 6151
                  ^ Mathf.RoundToInt(_map.maskRadius * 1000f) * 13
                  ^ Mathf.RoundToInt(_map.maskFeather * 1000f) * 17
                  ^ Mathf.RoundToInt(_map.spawnRadius * 1000f) * 31
                  // Blob scale was missing from this key, so dragging Min or Max changed nothing
                  // on screen until something else happened to rebuild the sample.
                  ^ Mathf.RoundToInt(_map.blobScaleMin * 1000f) * 271
                  ^ Mathf.RoundToInt(_map.blobScaleMax * 1000f) * 353
                  ^ _previewSeed * 40503;
        if (key == _sampleKey && _sample.Count > 0) return;
        _sampleKey = key;

        _sample.Clear();
        var rng = new System.Random(_previewSeed);
        int want = _map.blobCount;
        float gapSq = _map.spacing * _map.spacing;
        float outerSq = _map.spawnRadius * _map.spawnRadius;

        for (int n = 0; n < want; n++)
        {
            bool placed = false;
            for (int attempt = 0; attempt < 24 && !placed; attempt++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                // Area-weighted, exactly as ThrowPoint does: a uniform radius crowds the middle.
                float r = Mathf.Sqrt(Mathf.Lerp(0f, outerSq, (float)rng.NextDouble()));
                Vector2 q = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;

                bool clear = true;
                for (int i = 0; i < _sample.Count; i++)
                    if ((_sample[i].pos - q).sqrMagnitude < gapSq) { clear = false; break; }
                if (!clear) continue;

                _sample.Add(new Sample
                {
                    pos   = q,
                    rot   = (float)rng.NextDouble() * 360f,
                    scale = Mathf.Lerp(_map.blobScaleMin, _map.blobScaleMax,
                                       (float)rng.NextDouble()),
                });
                placed = true;
            }
            if (!placed) break;   // the water is full at this spacing
        }
    }

    float PixelsPerUnit(Rect box) => box.width / Mathf.Max(_viewSpan, 1f);

    /// <summary>World XZ, relative to the boat at the view centre, to screen pixels.</summary>
    Vector2 WorldToPixels(Rect box, Vector2 world)
    {
        float k = PixelsPerUnit(box);
        return new Vector2(box.center.x + world.x * k, box.center.y - world.y * k);
    }

    Vector2 PixelsToWorld(Rect box, Vector2 px)
    {
        float k = PixelsPerUnit(box);
        return new Vector2((px.x - box.center.x) / k, (box.center.y - px.y) / k);
    }







    void DrawClouds(Rect box)
    {
        EnsureSample();
        Handles.BeginGUI();
        float k = PixelsPerUnit(box);

        for (int i = 0; i < _sample.Count; i++)
        {
            var s = _sample[i];
            bool sel = i == _selected;

            Vector2 centre = WorldToPixels(box, s.pos);
            float px = s.scale * k;

            // Faded by the mask, using the same curve FogMask.hlsl runs — radius and feather, with
            // feather a fraction of the radius. Without this the canvas drew every mass at one
            // opacity, so maskFeather had no representation here at all and the mask read as a
            // ring rather than as the fade it actually is.
            float inner = _map.maskRadius * Mathf.Clamp01(1f - _map.maskFeather);
            float span  = Mathf.Max(_map.maskRadius - inner, 1e-4f);
            float mask  = 1f - Mathf.SmoothStep(0f, 1f,
                              Mathf.Clamp01((s.pos.magnitude - inner) / span));
            mask *= Mathf.Clamp01(_map.fogOpacity);

            // Never quite zero: a mass outside the mask still exists and is still drifting, and a
            // canvas that hid it entirely would look like the spawner had stopped.
            float a = Mathf.Lerp(0.06f, 0.55f, mask);

            Handles.color = sel ? new Color(1f, 0.85f, 0.35f, Mathf.Max(a, 0.3f))
                                : new Color(0.72f, 0.80f, 0.92f, a);
            DrawCloudOutline(centre, px, s.rot, _map.properties);

            // Solid dot at the mass centre, never a ring — the ring is the mass itself.
            Handles.color = sel ? new Color(1f, 0.85f, 0.35f, 0.95f)
                                : new Color(0.72f, 0.80f, 0.92f, Mathf.Max(a, 0.25f));
            Handles.DrawSolidDisc(centre, Vector3.forward, sel ? 4f : 2.5f);
        }

        Handles.EndGUI();
    }

    void DrawCloudOutline(Vector2 centre, float px, float rotationDeg, FogProperties shape)
    {
        float rot = rotationDeg * Mathf.Deg2Rad;
        Vector2 Map(Vector2 blobSpace)
        {
            float s = Mathf.Sin(rot), co = Mathf.Cos(rot);
            var v = new Vector2(blobSpace.x * co - blobSpace.y * s,
                                blobSpace.x * s + blobSpace.y * co);
            // Y flips because GUI pixels run downward while the arena runs up.
            return centre + new Vector2(v.x * px, -v.y * px);
        }

        var spine = new Vector3[24];
        for (int i = 0; i < spine.Length; i++)
            spine[i] = Map(shape.SpineAt(i / (float)(spine.Length - 1)));
        Handles.DrawAAPolyLine(3.5f, spine);

        // Generated exactly as FogBlob generates them, so the preview cannot drift from the water.
        int limbs = shape.EffectiveLimbCount;
        for (int L = 0; L < limbs; L++)
        {
            float along  = shape.LimbAlong(L);
            float length = shape.LimbLengthOf(L);
            if (length <= 0.001f) continue;

            Vector2 root = shape.SpineAt(along);
            Vector2 dir  = shape.SpineDirectionAt(along);
            Vector2 perp = new Vector2(dir.y, -dir.x) * Mathf.Sign(shape.LimbSide(L));
            float a = shape.limbAngle * Mathf.Deg2Rad, s = Mathf.Sin(a), co = Mathf.Cos(a);
            perp = new Vector2(perp.x * co - perp.y * s, perp.x * s + perp.y * co);

            var pts = new Vector3[12];
            for (int i = 0; i < pts.Length; i++)
            {
                float u = i / (float)(pts.Length - 1);
                pts[i] = Map(root + perp * (length * u)
                                  + dir * (shape.limbDroop * length * u * u));
            }
            Handles.DrawAAPolyLine(2.5f, pts);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    void HandleInput(Rect box)
    {
        Event e = Event.current;
        if (!box.Contains(e.mousePosition) && e.type != EventType.MouseUp) return;

        switch (e.type)
        {
            // Selecting only. Masses are generated, so there is nothing here to drag: moving one
            // by hand would be overwritten the moment anything regenerated the arrangement.
            case EventType.MouseDown when e.button == 0:
            {
                _selected = CloudAt(box, e.mousePosition);
                e.Use(); Repaint();
                break;
            }
            case EventType.ScrollWheel:
            {
                // Multiplicative, so each notch changes the view by the same proportion whether
                // you are at 8 units or 300. Linear steps crawl when zoomed out and overshoot
                // wildly when zoomed in.
                float factor = Mathf.Pow(1.12f, e.delta.y);
                _viewSpan = Mathf.Clamp(_viewSpan * factor, 4f, 400f);
                e.Use(); Repaint();
                break;
            }

        }
    }

    int CloudAt(Rect box, Vector2 mouse)
    {
        EnsureSample();
        int best = -1; float bestDist = 14f;
        for (int i = 0; i < _sample.Count; i++)
        {
            float d = Vector2.Distance(mouse, WorldToPixels(box, _sample[i].pos));
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

}
