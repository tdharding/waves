using UnityEditor;
using UnityEngine;

// Authoring front-end for spike rocks. Shows the rock in a stage of its own (SpikeStage — an empty
// scene, like Prefab Mode), lets you shape it live, and saves the result as a reusable
// SpikeShapePreset asset. The mesh is built by ProceduralSpikeMesh — the same code the level
// spawner runs — so what you see here is what ships.
//
// The stage wears one of two looks: Waves (a level's rock material, standing in water) or Level
// Select (the map's spike material, standing on the landscape). Opened from a spike in the Grid
// Designer or the Level Select Designer, it comes up on that spike's preset, at that spike's size,
// in that designer's look.
//
// Presets save into Resources/Spikes, which is where the Grid Designer lists them from: save one
// here and it turns up in the spike tool's preset picker with no wiring.
//
// Tools ▸ Waves ▸ Spike Studio. Modelled on SteppedBuildingStudio.
public class SpikeStudio : EditorWindow
{
    [SerializeField] SpikeShapeConfig cfg = new SpikeShapeConfig();
    [SerializeField] SpikeShapePreset loaded;
    [SerializeField] float previewScale = 1f;
    [SerializeField] bool  showWaterline = true;
    [SerializeField] bool  showRidgeGizmo = true;

    // The stage's look, and where its rock stands — the origin, unless it was opened from a spike
    // on the map, which stands it on that spike's own spot so the map's light falls on it the same.
    [SerializeField] SpikeStage.Look look = SpikeStage.Look.Waves;
    [SerializeField] Vector3         standAt;
    [SerializeField] Material        levelSelectMaterial;

    Vector2 scroll;
    int     _previewVerts;

    /// <summary>
    /// Raised when a preset is overwritten, so anything built from it can rebuild — the Level
    /// Select Designer's spikes are saved meshes and would otherwise keep the old shape.
    /// </summary>
    public static event System.Action<SpikeShapePreset> PresetSaved;

    [MenuItem("Tools/Waves/Spike Studio")]
    static void Open() => GetWindow<SpikeStudio>("Spike Studio");

    /// <summary>
    /// Opens the studio on a placed spike: its preset loaded, the preview at its size, and the
    /// stage in the look of the designer it came from, standing where <paramref name="at"/> says.
    /// <paramref name="material"/> is the Level Select Designer's spike material (null otherwise).
    /// </summary>
    public static void OpenForSpike(SpikeShapePreset preset, float scale, SpikeStage.Look look,
                                    Vector3 at, Material material)
    {
        var w = GetWindow<SpikeStudio>("Spike Studio");
        w.loaded              = preset;
        w.cfg                 = preset != null ? preset.config.Copy() : new SpikeShapeConfig();
        w.previewScale        = Mathf.Max(0.01f, scale);
        w.look                = look;
        w.standAt             = at;
        w.levelSelectMaterial = material;
        w.Repaint();

        // Called from a designer's button, i.e. partway through that window's GUI pass — switching
        // the Scene views over there and then would pull the ground from under the rest of it.
        EditorApplication.delayCall += () => { if (w != null) w.OpenStage(); };
    }

    void OnEnable()  { SceneView.duringSceneGui += OnSceneGUI; }
    void OnDisable() { SceneView.duringSceneGui -= OnSceneGUI; }

    // Draws the rock's form and its carved grooves straight into the Scene view.
    //
    // This exists because a carved groove is INVISIBLE without lighting: only the cut edges move
    // inward, so the silhouette never changes and the indentation shows purely in shading. On a
    // material that renders flat the rock looks untouched at any depth. The gizmo reads the very
    // same rings, twist and cut depth the mesh is built from, so where it draws a groove is
    // exactly where the geometry has one.
    void OnSceneGUI(SceneView sv)
    {
        var stage = SpikeStage.Current;
        if (!showRidgeGizmo || stage == null || stage.Rock == null) return;

        var profile = SpikeProfile.From(cfg, previewScale);
        var ridge   = SpikeRidge.From(cfg, previewScale);
        Transform t = stage.Rock.transform;

        var prevZ = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

        // The form: rings at each authored height, so the shape reads whatever the material does.
        Handles.color = new Color(1f, 1f, 1f, 0.25f);
        foreach (float y in new[] { profile.bottomY, 0f, profile.midY, profile.topY })
            Handles.DrawWireDisc(t.TransformPoint(new Vector3(0f, y, 0f)), t.up,
                                 profile.RadiusAt(y) * t.lossyScale.x);

        // The grooves, in red, following the twisted edges they are cut along.
        var lines = ProceduralSpikeMesh.RidgeLines(profile, cfg.sidesAround, cfg.heightSubdivisions,
                                                   ridge, cfg.twistTurns);
        // Drawn twice: solid where it faces you, and a faint pass through the rock so the far
        // side reads as behind rather than competing with the near one. Without the depth test
        // both sides drew equally bright and the spiral was impossible to follow.
        foreach (var line in lines)
        {
            var world = new Vector3[line.Length];
            for (int i = 0; i < line.Length; i++) world[i] = t.TransformPoint(line[i]);

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
            Handles.color = new Color(1f, 0.25f, 0.2f, 0.18f);
            Handles.DrawAAPolyLine(2f, world);

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = Color.red;
            Handles.DrawAAPolyLine(4f, world);
        }
        Handles.zTest = prevZ;

        if (lines.Count == 0 && cfg.carveSpiralRidge)
            Handles.Label(t.TransformPoint(new Vector3(0f, profile.topY, 0f)),
                          "Depth is 0 — nothing carved");
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "Shape a rock here, then save it as a preset. The Grid Designer's ▲ Spikes tool lists " +
            "everything in Resources/Spikes, so a saved preset is immediately placeable.\n\n" +
            "Sizes are metres. A placement can scale the whole rock from there.", MessageType.None);

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Widths (radius, metres)", EditorStyles.boldLabel);
        cfg.radiusBelowSurface = FloatMin("Below surface", cfg.radiusBelowSurface, 0.001f,
            "The footing, deep under the water. Usually the widest part.");
        cfg.radiusWaterline    = FloatMin("Waterline", cfg.radiusWaterline, 0.001f,
            "Where the rock meets the water — the ring the boat sees.");
        cfg.radiusMid          = FloatMin("Mid", cfg.radiusMid, 0.0005f,
            "Partway up. Wider than its neighbours bulges the rock into a belly; narrower pinches it into a waist.");
        cfg.radiusTop          = FloatMin("Top", cfg.radiusTop, 0f,
            "The tip. Near zero comes to a point; larger gives a flat perch.");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Heights (metres from the waterline)", EditorStyles.boldLabel);
        cfg.heightAboveWater  = FloatMin("Above water", cfg.heightAboveWater, 0.02f,
            "How far the tip stands above the waterline.");
        cfg.depthBelowWater   = FloatMin("Below water", cfg.depthBelowWater, 0.01f,
            "How far the base drops beneath the surface so the rock looks bottomless.");
        cfg.midHeightFraction = EditorGUILayout.Slider(
            new GUIContent("Mid height", "Where the mid radius sits between the waterline and the tip."),
            cfg.midHeightFraction, 0.05f, 0.95f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Top", EditorStyles.boldLabel);
        cfg.topRoundness = EditorGUILayout.Slider(
            new GUIContent("Curved cap", "Blends the top width in so the rock doesn't end on a flat plateau. " +
                                         "0 = a flat top the width of Top (a perch); 1 = that width fully capped " +
                                         "with a curve. Widen Top to get more cap."),
            cfg.topRoundness, 0f, 1f);
        if (cfg.topRoundness > 0.001f && cfg.radiusTop <= 0.01f)
            EditorGUILayout.HelpBox("Top is already a point, so there's no plateau for the cap to blend. " +
                                    "Widen Top to see the curve.", MessageType.Info);

        cfg.headSize = EditorGUILayout.Slider(
            new GUIContent("Head size", "A ball on top, as a multiple of the Top width. 1 = off. Above 1 " +
                                        "the ball swells out wider than the neck it sits on, with a notch " +
                                        "where they meet. Replaces the curved cap while it's on."),
            cfg.headSize, 1f, 4f);
        if (cfg.headSize > 1.001f && cfg.radiusTop <= 0.01f)
            EditorGUILayout.HelpBox("The head is sized off the Top width, and Top is a point. " +
                                    "Widen Top to grow a head.", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spiral", EditorStyles.boldLabel);
        cfg.twistTurns = EditorGUILayout.FloatField(
            new GUIContent("Twist (turns)", "Turns the surface twists through, base to tip. This is what " +
                                            "carries the mesh's own vertical edges round the rock as helices — " +
                                            "and the carved ridges follow those edges."),
            cfg.twistTurns);

        cfg.carveSpiralRidge = EditorGUILayout.Toggle(
            new GUIContent("Carve ridge", "Cut the spiral into the mesh as a real indentation. The " +
                                          "generator adds the density the groove needs and works out " +
                                          "the normals, so the rock's own lighting picks it out."),
            cfg.carveSpiralRidge);
        using (new EditorGUI.DisabledScope(!cfg.carveSpiralRidge))
        {
            cfg.ridgeSpacing  = FloatMin("Spacing", cfg.ridgeSpacing, 0.005f,
                "How far apart the spiral lines sit going up the rock, in metres. Bigger = fewer, " +
                "wider-spaced wraps.");
            cfg.ridgeDepth    = FloatMin("Depth", cfg.ridgeDepth, 0f,
                "How far each groove cuts in, in metres at this preset's own size.");
            cfg.ridgeSoftness = EditorGUILayout.Slider(
                new GUIContent("Softness", "How far the cut bleeds either side of the edge it sits on. " +
                                           "0 pinches a sharp crease; 1 reaches the next ridge, making " +
                                           "the surface a rounded flute rather than a cut."),
                cfg.ridgeSoftness, 0f, 1f);
            // Spacing is snapped to a count that divides evenly into Faces around, so report what
            // actually came out rather than leaving the asked-for number looking wrong.
            int   n   = SpikeRidge.CountFor(cfg);
            float got = SpikeRidge.ActualSpacing(cfg);
            EditorGUILayout.LabelField(" ",
                $"→ {n} of {cfg.sidesAround} edges cut, lines {got:0.###} m apart", EditorStyles.miniLabel);
            // A low-poly rock's own corners are relief too, and a twisted one spirals every
            // corner up it. Under-face it and those out-shout the groove, so the rock looks
            // ridged everywhere except where the grooves actually are — no depth value fixes it.
            float facet = (1f - Mathf.Cos(Mathf.PI / Mathf.Max(3, cfg.sidesAround))) * cfg.radiusWaterline;
            if (cfg.ridgeDepth > 0f && facet > cfg.ridgeDepth)
                EditorGUILayout.HelpBox(
                    $"The {cfg.sidesAround}-sided outline has {facet * 1000f:0} mm corners, deeper than the " +
                    $"{cfg.ridgeDepth * 1000f:0} mm groove — and the twist spirals every one of them, so they " +
                    "read as ridges too. Raise Faces around until the corners fall under the groove depth.",
                    MessageType.Warning);

            if (n >= Mathf.Max(1, cfg.sidesAround / 2))
                EditorGUILayout.HelpBox("Spacing is as tight as this many faces allow — every other edge " +
                                        "is already cut. Raise Faces around for finer spacing.",
                                        MessageType.Info);
            if (Mathf.Abs(cfg.twistTurns) < 0.001f)
                EditorGUILayout.HelpBox("Twist is 0, so the edges run straight up and the grooves are " +
                                        "flutes rather than spirals. Raise Twist to wind them.",
                                        MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Mesh", EditorStyles.boldLabel);
        cfg.sidesAround        = EditorGUILayout.IntSlider(
            new GUIContent("Faces around", "Low reads as a chiselled rock; high reads as a smooth column."),
            cfg.sidesAround, 3, 64);
        cfg.heightSubdivisions = EditorGUILayout.IntSlider(
            new GUIContent("Subdivisions", "Rings between each pair of widths. Higher rounds the curve out; 1 gives straight tapers."),
            cfg.heightSubdivisions, 1, 16);

        bool changed = EditorGUI.EndChangeCheck();

        // The rock side-on, tip to footing, with its widths called out where they sit — so the
        // fields above and the shape they make are visibly the same numbers, without having to
        // orbit the scene preview to check.
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
        SpikeSilhouetteGUI.DrawDiagram(
            GUILayoutUtility.GetRect(1f, 168f, GUILayout.ExpandWidth(true)), cfg, previewScale);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        var stage = SpikeStage.Current;

        EditorGUI.BeginChangeCheck();
        look = (SpikeStage.Look)EditorGUILayout.EnumPopup(
            new GUIContent("Look", "What the stage dresses the rock as. Waves: a level's rock material, " +
                                   "standing in water. Level Select: the map's spike material, standing " +
                                   "on the landscape."),
            look);
        if (EditorGUI.EndChangeCheck() && stage != null)
            stage.Dress(look, standAt, levelSelectMaterial);

        EditorGUI.BeginChangeCheck();
        previewScale  = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            new GUIContent("Scale", "Previews what a placement's scale multiplier does. Not saved into the preset."),
            previewScale));
        showWaterline = EditorGUILayout.Toggle(
            new GUIContent("Show waterline", "Adds a flat disc at y = 0 so you can see how much of the rock stands proud."),
            showWaterline);
        showRidgeGizmo = EditorGUILayout.Toggle(
            new GUIContent("Show ridge gizmo", "Draws the carved grooves into the Scene view in red, plus rings " +
                                               "at each authored width. A groove only ever shows in shading, so " +
                                               "on an unlit material this is the only way to see one."),
            showRidgeGizmo);
        if (EditorGUI.EndChangeCheck()) changed = true;

        DrawStats();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(stage == null ? "Open Stage" : "Refresh"))
            {
                if (stage == null) OpenStage();
                else               Refresh();
            }
            if (GUILayout.Button("Frame")) Frame();
            using (new EditorGUI.DisabledScope(stage == null))
                if (GUILayout.Button("Close Stage")) SpikeStage.Close();
        }
        if (changed && stage != null) Refresh();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preset", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        loaded = (SpikeShapePreset)EditorGUILayout.ObjectField("Load from", loaded, typeof(SpikeShapePreset), false);
        if (EditorGUI.EndChangeCheck() && loaded != null)
        {
            cfg = loaded.config.Copy();
            Refresh();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save As New Preset…")) SaveAsNew();
            using (new EditorGUI.DisabledScope(loaded == null))
                if (GUILayout.Button("Overwrite Loaded")) Overwrite();
        }

        EditorGUILayout.EndScrollView();
    }

    static float FloatMin(string label, float value, float min, string tooltip) =>
        Mathf.Max(min, EditorGUILayout.FloatField(new GUIContent(label, tooltip), value));

    // The numbers you actually design against: how big the rock reads, and how much of it shows.
    void DrawStats()
    {
        var p = SpikeProfile.From(cfg, previewScale);
        EditorGUILayout.LabelField(
            $"⌀{p.radiusWaterline * 2f:0.##} m at the water · {p.topY:0.##} m proud · " +
            $"{-p.bottomY:0.##} m under · " +
            (p.HeadActive ? $"head ⌀{p.headRadius * 2f:0.##} m"
             : p.CapHeight > 1e-5f ? $"curved cap ⌀{p.radiusTop * 2f:0.##} m, {p.CapHeight:0.##} m tall"
                                 : p.TipIsClosed ? "tip closed"
                                                 : $"flat top ⌀{p.radiusTop * 2f:0.##} m"),
            EditorStyles.miniLabel);

        // Carving decides the density, so the cost is worth seeing while you tune the groove.
        if (_previewVerts > 0)
        {
            string note = cfg.carveSpiralRidge
                ? $"{_previewVerts:n0} verts — raised to resolve the carved groove"
                : $"{_previewVerts:n0} verts";
            EditorGUILayout.LabelField(note, EditorStyles.miniLabel);
            if (cfg.carveSpiralRidge && _previewVerts > 12000)
                EditorGUILayout.HelpBox("Heavy for a rock you'll place many of. Widen the groove or " +
                                        "raise Softness — both cost far fewer vertices than a narrow cut.",
                                        MessageType.Warning);
        }
    }

    // Opens the stage in the current look (or re-dresses the one open), builds the rock into it
    // and frames it.
    void OpenStage()
    {
        var stage = SpikeStage.Open(look, standAt, levelSelectMaterial);
        if (stage == null) return;
        Refresh();

        // After the Scene views have finished switching over — framing during the switch is
        // undone by the view state the switch restores.
        EditorApplication.delayCall += () =>
        {
            if (SpikeStage.Current == stage) stage.Frame();
        };
    }

    // Rebuilds the stage's rock from the fields. Nothing happens while the stage is closed.
    void Refresh()
    {
        var stage = SpikeStage.Current;
        if (stage == null || stage.Rock == null) return;

        var profile = SpikeProfile.From(cfg, previewScale);
        var mesh    = ProceduralSpikeMesh.Build(profile, cfg.sidesAround, cfg.heightSubdivisions,
                                                SpikeRidge.From(cfg, previewScale), cfg.twistTurns);
        mesh.name = "SpikeStudioPreview";
        _previewVerts = mesh.vertexCount;
        stage.SetMesh(mesh, profile.RadiusAt(0f));

        // Nothing to push onto the material: the mesh carries where its grooves are, so the
        // preview shades exactly as a spawned rock will.
        UpdateWaterline(stage);
        Selection.activeObject = stage.Rock;
    }

    // A thin disc at y = 0 marking the waterline, so the split between what shows and what's
    // submerged is visible while shaping. Preview furniture only — never part of the preset.
    void UpdateWaterline(SpikeStage stage)
    {
        const string DiscName = "Waterline";
        var rock     = stage.Rock;
        var existing = rock.transform.Find(DiscName);

        if (!showWaterline)
        {
            if (existing != null) DestroyImmediate(existing.gameObject);
            return;
        }

        GameObject disc;
        if (existing != null) disc = existing.gameObject;
        else
        {
            disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = DiscName;
            DestroyImmediate(disc.GetComponent<Collider>());
            stage.Adopt(disc);
            disc.transform.SetParent(rock.transform, false);
        }

        float r = Mathf.Max(cfg.WidestRadius * previewScale * 2.5f, 0.5f);
        disc.transform.localPosition = Vector3.zero;
        disc.transform.localScale    = new Vector3(r, 0.002f, r);   // Unity's cylinder is 2 units tall
    }

    void Frame()
    {
        var stage = SpikeStage.Current;
        if (stage == null) OpenStage();
        else               stage.Frame();
    }

    // Creates the target folder (and any missing parents) so the save panel opens there.
    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string[] parts = folder.Split('/');
        string path = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = path + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(path, parts[i]);
            path = next;
        }
    }

    void SaveAsNew()
    {
        EnsureFolder(SpikeShapePreset.AssetFolder);
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Spike Shape Preset", "SpikeShapePreset1", "asset",
            "Choose where to save the preset. Keep it under Resources/Spikes so the Grid Designer lists it.",
            SpikeShapePreset.AssetFolder);
        if (string.IsNullOrEmpty(path)) return;

        var asset = ScriptableObject.CreateInstance<SpikeShapePreset>();
        asset.config = cfg.Copy();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        loaded = asset;
        EditorGUIUtility.PingObject(asset);
    }

    void Overwrite()
    {
        if (loaded == null) return;
        loaded.config = cfg.Copy();
        EditorUtility.SetDirty(loaded);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(loaded);
        PresetSaved?.Invoke(loaded);
    }
}
