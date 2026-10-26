using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Model Texturing Tool — select faces of a mesh in the Scene view, group them, and give each group a
// colour fill. The groups bake into a map with its own layout (see Layout): each group's pieces of the
// model's generated unique UVs (UV channel 2 — the importer's "Generate Lightmap UVs"), scaled to the
// group's share of the map on the Map Space bar, so it never depends on how the model was unwrapped:
// RGB = the group's colour, A = 1 (0 where no group is). AngelMaterial reads it as _ShadeMap,
// on the replicant's UV1. Each group's noise is baked into the map too (see Rasterise), and so are its
// gradients: Select Lines → Group Selected Lines makes one on the face group the lines touch, fading
// out from them across that group's faces (see GradientFields). Add Decal → click a grouped face
// stamps an image there, projected along the face and kept to that group (see DecalStamps).
// The tool only works in its stage: Open in Stage dresses the copy there in the replicant (see
// ModelTexturingData.replicant), re-split live as groups' Transparency boxes change, wearing two materials
// named from the Material Title: "<title>Opaque" (AngelMaterial graph) and "<title>Transparent"
// (the model's own copy of the AngelMaterialTransparent graph, with a "<group>_Transparency" property
// per Transparency group — see EnsureTransparentGraph), and shows a live, unsaved preview of the map
// on it. Bake puts the replicant and its materials on the real model.
// Add Lighting / Add Baked Lighting / Add White Fade switch the level select's light, its baked
// shadows + River Glow (Level Select Lighting window) and white fade on, per material;
// the stage copy always shows the model without them (see HideLevelSelectEffects).
// Experimental: groups are stored as triangle indices (see ModelTexturingData).
public class ModelTexturingTool : EditorWindow
{
    const string ShadeMapProp = "_ShadeMap";
    const string BaseColourProp = "_BaseColour";
    const string DoubleSidedProp = "_DoubleSided";
    const string AddLightingProp = "_AddLighting";
    const string AddBakedLightingProp = "_AddBakedLighting";
    const string AddWhiteFadeProp = "_AddWhiteFade";
    const string OpaqueGraph = "AngelMaterial";
    const string TransparentGraph = "AngelMaterialTransparent";
    const int DilatePasses = 8;
    const float IslandPadding = 4f;   // map pixels between packed pieces, filled by the dilation
    const float MinShare = 0.01f;     // the least of the map a group can be dragged down to
    static readonly int[] TextureSizes = { 512, 1024, 2048 };
    static readonly string[] TextureSizeNames = { "512", "1024", "2048" };

    [SerializeField] Renderer _target;
    [SerializeField] Renderer _returnTarget;   // the original, while its copy is open in the stage
    [SerializeField] ModelTexturingData _data;
    [SerializeField] bool _selecting;
    [SerializeField] bool _selectingLines;
    [SerializeField] bool _addingDecal;
    Vector2 _scroll;
    const float LinePickPixels = 10f;   // how near the cursor a line must be to pick it
    const float LineThickness = 4f;
    const float CreaseThickness = 2.5f;

    // The faces currently selected — a working set, not saved, until they are grouped.
    readonly HashSet<int> _selection = new HashSet<int>();
    // The lines (wire edges) currently selected — likewise, until they are grouped into a gradient.
    readonly HashSet<int> _lineSelection = new HashSet<int>();
    // The group whose faces Select Group Faces put in the selection, and the selection as it was
    // then — a change since makes the button Update Group Faces.
    [System.NonSerialized] ModelTexturingData.Fill _editingGroup;
    [System.NonSerialized] HashSet<int> _editingSnapshot;
    readonly Dictionary<ModelTexturingData.Fill, bool> _expanded = new Dictionary<ModelTexturingData.Fill, bool>();
    readonly Dictionary<ModelTexturingData.LineGradient, bool> _gradientExpanded = new Dictionary<ModelTexturingData.LineGradient, bool>();
    readonly Dictionary<ModelTexturingData.Decal, bool> _decalExpanded = new Dictionary<ModelTexturingData.Decal, bool>();
    readonly Dictionary<ModelTexturingData.CreaseGroup, bool> _creaseExpanded = new Dictionary<ModelTexturingData.CreaseGroup, bool>();
    // The crease group whose lines Select Group Lines put in the selection, and the selection then.
    [System.NonSerialized] ModelTexturingData.CreaseGroup _editingCreaseGroup;
    [System.NonSerialized] HashSet<int> _editingCreaseSnapshot;

    // Mesh cache — rebuilt whenever the target's mesh changes shape or gains/loses its unique UVs.
    // Always the ORIGINAL mesh, even while the stage copy wears the replicant: the two share every
    // vertex, and faces are numbered by the original.
    // Not kept over a script reload: the window's private fields are, but the List/Dictionary ones
    // below come back null — so a surviving _cachedMesh would skip the rebuild that refills them.
    [System.NonSerialized] Mesh _cachedMesh;
    int _cachedVertexCount, _cachedIndexCount;
    bool _cachedHasUV2;
    [System.NonSerialized] int _cacheVersion;                 // +1 per rebuild
    [System.NonSerialized] ModelTexturingData _pinnedData;    // PinOldAnchors last ran for these
    [System.NonSerialized] int _pinnedVersion = -1;
    Vector3[] _rest;            // mesh-space (bind pose) positions
    int[] _tris;                // all submeshes, concatenated
    Vector2[] _uv2;
    Matrix4x4[] _bindposes;
    BoneWeight[] _boneWeights;
    int[] _weld;                // vertex -> id shared by every vertex at the same position
    Dictionary<Vector3Int, int> _weldLookup;   // quantised rest position -> weld id
    int _weldCount;
    int[] _weldCorner;          // weld id -> the first face corner there (face × 3 + corner), or -1
    int[] _partner;             // triangle -> the other half of its quad, or -1
    int[] _wireEdges;           // vertex pairs, one per edge, quad diagonals left out
    List<int>[] _wireOwners;    // per wire edge, the triangles either side of it
    List<int>[] _triWires;      // per triangle, the wire edges along its sides
    Dictionary<long, int> _wireByEdge;   // EdgeKey of two weld ids -> wire edge
    Vector3[] _world;           // posed world positions, refreshed per Scene view event

    // Hover / stroke
    int _hoverTri = -1;
    int _hoverLine = -1;
    Vector2 _hoverHit;          // where the cursor's ray hits the hovered face (see RayTriangle)
    bool _strokeAdd;

    // Live preview
    Texture2D _preview;
    MaterialPropertyBlock _block;
    bool _previewDirty;

    // Display — authored in the window's Display foldout, kept in EditorPrefs.
    Color _selectionColour, _hoverColour, _wireColour, _creaseColour;
    bool _displayFoldout;
    bool _showWireframe;
    bool _mirror;               // selecting a face/line also selects its twin across X = 0
    [System.NonSerialized] int[] _faceTwin, _lineTwin;   // built on first use per mesh, -1 = no twin
    Vector3[] _wireSegments;

    [MenuItem("Tools/Characters/Model Texturing Tool", false, 601)]
    static void Open() => GetWindow<ModelTexturingTool>("Model Texturing Tool");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        ModelTexturingStage.Closed += OnStageClosed;
        LoadDisplay();
        if (_target == null) UseSelection();
        _previewDirty = true;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
        ModelTexturingStage.Closed -= OnStageClosed;
        ClearPreview();
        DropHideMesh();   // the copy goes back into the replicant; reopening the window hides again
        if (_preview) DestroyImmediate(_preview);
    }

    void OnUndoRedo()
    {
        _expanded.Clear();
        _gradientExpanded.Clear();
        _decalExpanded.Clear();
        _creaseExpanded.Clear();
        _previewDirty = true;
        Repaint();
    }

    void OnInspectorUpdate()
    {
        if (!_previewDirty || !TargetInStage) return;
        // A preview bake takes about half a second, so while a slider is being dragged it waits
        // for the drag to pause instead of baking on every step.
        if (EditorApplication.timeSinceStartup < _previewDueAt) return;
        _previewDirty = false;
        if (WearsLayout(_target)) ApplyLayout(_data.replicant, false);
        SplitReplicant();
        if (TransparencyProps().Count > 0 && OwnTransparentMaterial() != null) EnsureTransparentGraph();
        RefreshPreview();
    }

    // ─────────────────────────────────────────── Window

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawWindow();
        EditorGUILayout.EndScrollView();
    }

    void DrawWindow()
    {
        EditorGUI.BeginChangeCheck();
        var target = (Renderer)EditorGUILayout.ObjectField("Target", _target, typeof(Renderer), true);
        if (EditorGUI.EndChangeCheck()) SetTarget(target);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open in Stage")) OpenInStage();
        var openDataRect = GUILayoutUtility.GetRect(new GUIContent("Open Data ▾"), GUI.skin.button, GUILayout.Width(100));
        if (GUI.Button(openDataRect, "Open Data ▾")) ShowOpenDataMenu(openDataRect);
        EditorGUILayout.EndHorizontal();

        if (_target == null)
        {
            EditorGUILayout.HelpBox("Select the angel and press Open in Stage, drop its mesh renderer in Target, or pick its data from Open Data.", MessageType.Info);
            return;
        }
        // The tool only works on the copy in its stage; Bake still dresses the real model.
        if (!TargetInStage)
        {
            EditorGUILayout.HelpBox($"Press Open in Stage to work on {_target.name}, or pick its data from Open Data.", MessageType.Info);
            return;
        }
        if (!EnsureCache())
        {
            EditorGUILayout.HelpBox("The target has no mesh.", MessageType.Warning);
            return;
        }

        if ((!HasUV2 || ReordersFaces()) && GUILayout.Button("Replicate Mesh")) ReplicateMesh();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        var data = (ModelTexturingData)EditorGUILayout.ObjectField("Data", _data, typeof(ModelTexturingData), false);
        if (EditorGUI.EndChangeCheck())
        {
            _data = data;
            _expanded.Clear();
            _previewDirty = true;
            DressStageCopy();
        }
        if (GUILayout.Button("New…", GUILayout.Width(50))) CreateData();
        using (new EditorGUI.DisabledScope(_data == null || !EditorUtility.IsDirty(_data)))
            if (GUILayout.Button("Save", GUILayout.Width(50))) AssetDatabase.SaveAssetIfDirty(_data);
        EditorGUILayout.EndHorizontal();
        if (_data == null) return;

        EditorGUI.BeginChangeCheck();
        string title = EditorGUILayout.DelayedTextField("Material Title", MaterialTitle);
        if (EditorGUI.EndChangeCheck()) SetMaterialTitle(title);

        if (_data.triangleCount != 0 && _data.triangleCount != TriCount)
            EditorGUILayout.HelpBox(
                $"The model has changed since these groups were made ({_data.triangleCount} triangles then, " +
                $"{TriCount} now). Faces are remembered by number, so they may now sit on the wrong faces.",
                MessageType.Warning);

        EditorGUI.BeginChangeCheck();
        int sizeIndex = Mathf.Max(0, System.Array.IndexOf(TextureSizes, _data.textureSize));
        sizeIndex = EditorGUILayout.Popup("Texture Size", sizeIndex, TextureSizeNames);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Shade map size");
            _data.textureSize = TextureSizes[sizeIndex];
            MarkChanged();
        }
        DrawMapSpace();
        DrawSubdivision();

        EditorGUILayout.Space();
        DrawSelectTool();

        EditorGUILayout.Space();
        DrawGroups();

        EditorGUILayout.Space();
        DrawCreaseGroups();

        EditorGUILayout.Space();
        EditorGUI.BeginDisabledGroup(!HasUV2);
        if (GUILayout.Button("Bake", GUILayout.Height(28)))
        {
            Bake();
            ApplyHide();   // Bake dresses the stage copy in the replicant itself
        }
        EditorGUI.EndDisabledGroup();
        if (_data.shadeMap)
            EditorGUILayout.ObjectField("Baked Map", _data.shadeMap, typeof(Texture2D), false);

        EditorGUILayout.Space();
        DrawDisplaySettings();

        EditorGUILayout.Space();
        DrawMaterialList();
    }

    // The materials the mesh will wear, one row each: name, kind, and how many faces it draws.
    void DrawMaterialList()
    {
        var seeThrough = SeeThroughTriangles();
        var opaque = new List<int>(TriCount);
        for (int t = 0; t < TriCount; t++)
            if (!seeThrough.Contains(t)) opaque.Add(t);

        int count = seeThrough.Count > 0 ? 2 : 1;
        EditorGUILayout.LabelField($"Materials ({count})", EditorStyles.boldLabel);
        DrawMaterialRow(MaterialTitle + "Opaque", "Opaque", FaceCount(opaque), _data.material, false);
        if (seeThrough.Count > 0)
            DrawMaterialRow(MaterialTitle + "Transparent", "Transparent", FaceCount(seeThrough), _data.transparentMaterial, true);
    }

    // One row per material: its own Double Sided, Add White Fade, Add Lighting and Add Baked Lighting boxes, so the opaque
    // and transparent faces can differ. A transparent material starts with the opaque one's settings
    // (see EnsureMaterial).
    void DrawMaterialRow(string name, string kind, int faces, Material made, bool transparent)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField(name, GUILayout.MinWidth(120));
        EditorGUILayout.LabelField(kind, GUILayout.Width(80));
        EditorGUILayout.LabelField($"{faces} faces", GUILayout.Width(70));
        bool exists = made != null && AssetDatabase.GetAssetPath(made) == MaterialPath(transparent);
        // Off, faces seen from behind are not drawn.
        DrawMaterialBox(made, exists, DoubleSidedProp, "Double Sided", "Draws the faces from behind too.", 100);
        DrawMaterialBox(made, exists, AddWhiteFadeProp, "Add White Fade",
            "The level select's white fade round the boat, laid on last. Never shown in the stage.", 110);
        DrawMaterialBox(made, exists, AddLightingProp, "Add Lighting",
            "The level select's light (the scene's LevelSelectLight), the same as the river runs and hills. Never shown in the stage.", 95);
        DrawMaterialBox(made, exists, AddBakedLightingProp, "Add Baked Lighting",
            "The level select's baked Shadows and River Glow (Level Select Lighting window), laid on after Add Lighting, the same as the river runs and hills. Never shown in the stage.", 130);
        using (new EditorGUI.DisabledScope(!exists))
            if (GUILayout.Button(exists ? "Ping" : "Not made", GUILayout.Width(70))) EditorGUIUtility.PingObject(made);
        EditorGUILayout.EndHorizontal();
    }

    // A box for one of the material's Boolean properties.
    void DrawMaterialBox(Material made, bool exists, string prop, string label, string tooltip, float width)
    {
        bool has = exists && made.HasProperty(prop);
        using (new EditorGUI.DisabledScope(!has))
        {
            bool on = has && made.GetFloat(prop) > 0.5f;
            EditorGUI.BeginChangeCheck();
            on = GUILayout.Toggle(on, new GUIContent(label, tooltip), GUILayout.Width(width));
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(made, label);
            made.SetFloat(prop, on ? 1f : 0f);
            EditorUtility.SetDirty(made);
            SceneView.RepaintAll();
        }
    }

    void DrawSelectTool()
    {
        EditorGUI.BeginChangeCheck();
        bool faces = GUILayout.Toggle(_selecting, "Select Faces", "Button", GUILayout.Height(24));
        bool lines = GUILayout.Toggle(_selectingLines, "Select Lines", "Button", GUILayout.Height(24));
        bool decal = GUILayout.Toggle(_addingDecal, "Add Decal", "Button", GUILayout.Height(24));
        if (EditorGUI.EndChangeCheck())
        {
            // One tool at a time: turning one on turns the others off.
            if (faces && !_selecting) lines = decal = false;
            if (lines && !_selectingLines) faces = decal = false;
            if (decal && !_addingDecal) faces = lines = false;
            _selecting = faces;
            _selectingLines = lines;
            _addingDecal = decal;
            _hoverTri = -1;
            _hoverLine = -1;
            SceneView.RepaintAll();
        }
        if (_selecting)
            EditorGUILayout.HelpBox("Click or drag over faces to select them. Hold Shift to deselect.", MessageType.None);
        if (_selectingLines)
            EditorGUILayout.HelpBox("Click or drag along the lines between faces to select them. Hold Shift to deselect. " +
                                    "Grouped, they make a gradient on the face group they touch.", MessageType.None);
        if (_addingDecal)
            EditorGUILayout.HelpBox("Click a grouped face to place a decal there. It goes under that face's group, " +
                                    "and only paints that group's faces.", MessageType.None);

        EditorGUI.BeginChangeCheck();
        _showWireframe = EditorGUILayout.Toggle("Wireframe", _showWireframe);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(PrefPrefix + "ShowWireframe", _showWireframe);
            SceneView.RepaintAll();
        }
        EditorGUI.BeginChangeCheck();
        _mirror = EditorGUILayout.Toggle(new GUIContent("Mirror",
            "While selecting, the face or line opposite (across the model's middle, X = 0) is selected or deselected too."), _mirror);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(PrefPrefix + "Mirror", _mirror);

        EditorGUILayout.LabelField($"{FaceCount(_selection)} faces selected", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(_selection.Count == 0);
        if (GUILayout.Button("Group Selected Faces")) GroupSelection();
        if (GUILayout.Button("Clear Selection", GUILayout.Width(110)))
        {
            _selection.Clear();
            _editingGroup = null;
            SceneView.RepaintAll();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Select Ungrouped Faces")) SelectUngroupedFaces();

        EditorGUILayout.LabelField($"{_lineSelection.Count} lines selected", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(_lineSelection.Count == 0);
        if (GUILayout.Button("Group Selected Lines")) GroupLines();
        if (GUILayout.Button("Clear Selection", GUILayout.Width(110)))
        {
            _lineSelection.Clear();
            _editingCreaseGroup = null;
            SceneView.RepaintAll();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        // Blender's edge crease: the selected lines kept sharp when Subdivision smooths the model.
        using (new EditorGUI.DisabledScope(_lineSelection.Count == 0))
            if (GUILayout.Button(new GUIContent("Group Selected Lines as Crease",
                    "A new crease group: these lines stay sharp when Subdivision smooths the model, as sharp as the group's Crease.")))
                GroupCreaseLines();
    }

    // The Subdivision box and its Levels, after the map's settings.
    void DrawSubdivision()
    {
        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.Toggle(new GUIContent("Subdivision",
            "Smooths the replicant the way Blender's Subdivision Surface does, so every model wearing it is smooth. " +
            "Faces and lines are still picked on the unsmoothed shape. Off, it's the plain replicant again."),
            _data.subdivision);
        int levels;
        using (new EditorGUI.DisabledScope(!on))
        {
            EditorGUI.indentLevel++;
            levels = EditorGUILayout.IntSlider("Levels", Mathf.Clamp(_data.subdivisionLevels, 1, 2), 1, 2);
            EditorGUI.indentLevel--;
        }
        if (!EditorGUI.EndChangeCheck()) return;
        Undo.RecordObject(_data, "Subdivision");
        _data.subdivision = on;
        _data.subdivisionLevels = levels;
        MarkChanged();
    }

    // The selected lines become a new crease group at full sharpness. A line is in one crease group:
    // grouping it again moves it out of the one it was in.
    void GroupCreaseLines()
    {
        Undo.RecordObject(_data, "Crease group");
        _data.creaseGroups ??= new List<ModelTexturingData.CreaseGroup>();
        var group = new ModelTexturingData.CreaseGroup { name = "Crease Group " + (_data.creaseGroups.Count + 1) };
        group.lines.AddRange(CreaseLinesOf(_lineSelection));
        TakeCreaseLines(_lineSelection, null);
        _data.creaseGroups.Add(group);
        // Only the new crease group is left open.
        _creaseExpanded.Clear();
        _creaseExpanded[group] = true;
        _lineSelection.Clear();
        _editingCreaseGroup = null;
        MarkChanged();
        SceneView.RepaintAll();
    }

    // Lines (wire edges) as crease lines, pinned to the model's corners.
    List<ModelTexturingData.Crease> CreaseLinesOf(IEnumerable<int> wires)
    {
        var lines = new List<ModelTexturingData.Crease>();
        var sorted = new List<int>(wires);
        sorted.Sort();
        foreach (int i in sorted)
        {
            int va = _wireEdges[i * 2], vb = _wireEdges[i * 2 + 1];
            lines.Add(new ModelTexturingData.Crease
            {
                cornerA = _weldCorner[_weld[va]],
                cornerB = _weldCorner[_weld[vb]],
                a = _rest[va],
                b = _rest[vb],
            });
        }
        return lines;
    }

    // Takes these lines out of every crease group but `keep`.
    void TakeCreaseLines(IEnumerable<int> wires, ModelTexturingData.CreaseGroup keep)
    {
        var keys = new HashSet<long>();
        foreach (int i in wires) keys.Add(EdgeKey(_weld[_wireEdges[i * 2]], _weld[_wireEdges[i * 2 + 1]]));
        foreach (var g in _data.creaseGroups)
            if (g != keep) g.lines.RemoveAll(cr => CreaseKey(cr, out long key) && keys.Contains(key));
    }

    // The group's lines as wire edges (lines no longer on the model are left out).
    List<int> WiresOf(ModelTexturingData.CreaseGroup g)
    {
        var wires = new List<int>();
        if (_wireByEdge == null) return wires;
        foreach (var cr in g.lines)
            if (CreaseKey(cr, out long key) && _wireByEdge.TryGetValue(key, out int i)) wires.Add(i);
        return wires;
    }

    // The crease groups, each a foldout like the face groups: Name, Crease, and its lines.
    void DrawCreaseGroups()
    {
        _data.creaseGroups ??= new List<ModelTexturingData.CreaseGroup>();
        EditorGUILayout.LabelField("Crease Groups", EditorStyles.boldLabel);
        if (_data.creaseGroups.Count == 0)
            EditorGUILayout.LabelField("No crease groups yet — select lines and press Group Selected Lines as Crease.", EditorStyles.miniLabel);

        int remove = -1;
        for (int i = 0; i < _data.creaseGroups.Count; i++)
        {
            var g = _data.creaseGroups[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            _creaseExpanded.TryGetValue(g, out bool open);
            _creaseExpanded[g] = EditorGUILayout.Foldout(open, $"{g.name}   ({g.lines.Count} lines)", true);
            if (GUILayout.Button("✕", GUILayout.Width(22))) remove = i;
            EditorGUILayout.EndHorizontal();

            if (_creaseExpanded[g])
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField("Name", g.name);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Rename crease group");
                    g.name = name;
                    EditorUtility.SetDirty(_data);
                }

                EditorGUI.BeginChangeCheck();
                float amount = EditorGUILayout.Slider(new GUIContent("Crease",
                    "How sharp these lines stay when Subdivision smooths the model: 1 = a hard edge, 0 = no crease."),
                    g.amount, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Crease");
                    g.amount = amount;
                    MarkChanged();
                }

                if (_editingCreaseGroup == g && !_lineSelection.SetEquals(_editingCreaseSnapshot))
                {
                    using (new EditorGUI.DisabledScope(_lineSelection.Count == 0))
                        if (GUILayout.Button("Update Group Lines")) UpdateCreaseLines(g);
                }
                else if (GUILayout.Button("Select Group Lines")) SelectCreaseLines(g);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        if (remove >= 0)
        {
            Undo.RecordObject(_data, "Remove crease group");
            if (_editingCreaseGroup == _data.creaseGroups[remove]) _editingCreaseGroup = null;
            _data.creaseGroups.RemoveAt(remove);
            MarkChanged();
        }
    }

    // Adds the crease group's lines to the line selection, and starts editing it: once the selection
    // changes, its button becomes Update Group Lines.
    void SelectCreaseLines(ModelTexturingData.CreaseGroup g)
    {
        if (_target == null || !EnsureCache()) return;
        foreach (int i in WiresOf(g)) _lineSelection.Add(i);
        _editingCreaseGroup = g;
        _editingCreaseSnapshot = new HashSet<int>(_lineSelection);
        Repaint();
        SceneView.RepaintAll();
    }

    // The crease group's lines become the selection; lines taken from other crease groups leave
    // them. The selection is then cleared.
    void UpdateCreaseLines(ModelTexturingData.CreaseGroup g)
    {
        Undo.RecordObject(_data, "Update crease lines");
        TakeCreaseLines(_lineSelection, g);
        g.lines.Clear();
        g.lines.AddRange(CreaseLinesOf(_lineSelection));
        _lineSelection.Clear();
        _editingCreaseGroup = null;
        MarkChanged();
        SceneView.RepaintAll();
    }

    // Every crease group's lines.
    IEnumerable<ModelTexturingData.Crease> AllCreaseLines()
    {
        if (_data == null || _data.creaseGroups == null) yield break;
        foreach (var g in _data.creaseGroups)
            foreach (var cr in g.lines)
                yield return cr;
    }

    // The crease's line as two welded points, from its corners when it's pinned, else its rest-pose points.
    bool CreaseKey(ModelTexturingData.Crease cr, out long key)
    {
        key = 0;
        int a, b;
        if (cr.cornerA >= 0 && cr.cornerB >= 0 && cr.cornerA < _tris.Length && cr.cornerB < _tris.Length)
        {
            a = _weld[_tris[cr.cornerA]];
            b = _weld[_tris[cr.cornerB]];
        }
        else if (!_weldLookup.TryGetValue(Quantise(cr.a), out a) || !_weldLookup.TryGetValue(Quantise(cr.b), out b))
            return false;
        if (a == b) return false;
        key = EdgeKey(a, b);
        return true;
    }

    // Every creased line (welded points) and how sharp its group makes it.
    Dictionary<long, float> CreaseEdges()
    {
        var edges = new Dictionary<long, float>();
        if (_data == null || _data.creaseGroups == null) return edges;
        foreach (var g in _data.creaseGroups)
        {
            if (g.amount <= 0f) continue;
            foreach (var cr in g.lines)
                if (CreaseKey(cr, out long key)) edges[key] = Mathf.Clamp01(g.amount);
        }
        return edges;
    }

    void DrawGroups()
    {
        EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);
        if (_data.fills.Count == 0)
            EditorGUILayout.LabelField("No groups yet — select faces and press Group Selected Faces.", EditorStyles.miniLabel);

        int remove = -1;
        for (int i = 0; i < _data.fills.Count; i++)
        {
            var g = _data.fills[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            _expanded.TryGetValue(g, out bool open);
            _expanded[g] = EditorGUILayout.Foldout(open, $"{g.name}   ({FaceCount(g.triangles)} faces)", true);
            EditorGUI.BeginChangeCheck();
            bool locked = GUILayout.Toggle(g.locked, "Lock", EditorStyles.miniButton, GUILayout.Width(44));
            bool hidden = GUILayout.Toggle(g.hidden, "Hide", EditorStyles.miniButton, GUILayout.Width(44));
            if (EditorGUI.EndChangeCheck()) SetLockHide(g, locked, hidden);
            if (GUILayout.Button("✕", GUILayout.Width(22))) remove = i;
            EditorGUILayout.EndHorizontal();

            if (_expanded[g])
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField("Name", g.name);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Rename group");
                    g.name = name;
                    MarkChanged();   // a Transparency group's material property is named after it
                }

                EditorGUI.BeginChangeCheck();
                var colour = EditorGUILayout.ColorField(new GUIContent("Colour Fill"), g.colour, true, false, false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Group colour");
                    g.colour = new Color(colour.r, colour.g, colour.b, g.colour.a);
                    MarkChanged();
                }

                // Ticked, the group draws with the transparent material, as see-through as the slider
                // says — the material's "<name>_Transparency", so it can be set there too. Unticked,
                // the slider is hidden and the group is opaque; its value is kept.
                EditorGUI.BeginChangeCheck();
                bool transparentOn = EditorGUILayout.Toggle(new GUIContent("Transparency",
                    $"Draws this group with the transparent material, exposed there as {TransparencyLabel(g)} (0 = solid, 1 = gone)."),
                    g.Transparent);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Group transparency");
                    g.opacityOff = !transparentOn;
                    MarkChanged();
                }
                if (g.Transparent)
                {
                    EditorGUI.indentLevel++;
                    EditorGUI.BeginChangeCheck();
                    float amount = EditorGUILayout.Slider(" ", TransparencyOf(g), 0f, 1f);
                    if (EditorGUI.EndChangeCheck()) SetTransparency(g, amount);
                    EditorGUI.indentLevel--;
                }

                EditorGUI.BeginChangeCheck();
                // Unticked, the group bakes as a flat colour; its noise settings are kept for later.
                bool noiseOn = EditorGUILayout.Toggle("Noise", !g.noiseOff);
                float noiseStrength, noiseScale;
                using (new EditorGUI.DisabledScope(!noiseOn))
                {
                    noiseStrength = EditorGUILayout.Slider("Noise Strength", g.noiseStrength, 0f, 1f);
                    noiseScale = Mathf.Max(0f, EditorGUILayout.FloatField("Noise Scale", g.noiseScale));
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Group noise");
                    g.noiseOff = !noiseOn;
                    g.noiseStrength = noiseStrength;
                    g.noiseScale = noiseScale;
                    MarkChanged();
                }

                EditorGUILayout.BeginHorizontal();
                if (_editingGroup == g && !_selection.SetEquals(_editingSnapshot))
                {
                    using (new EditorGUI.DisabledScope(_selection.Count == 0))
                        if (GUILayout.Button("Update Group Faces")) UpdateGroupFaces(g);
                }
                else if (GUILayout.Button("Select Group Faces")) SelectGroupFaces(g);
                if (GUILayout.Button("Select Outer Lines")) SelectOuterLines(g);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2f);
                DrawOuterLinesGradient(g);
                DrawGradients(g);
                DrawDecals(g);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        if (remove >= 0)
        {
            Undo.RecordObject(_data, "Remove group");
            // Its map space goes to its neighbour on the bar.
            var shares = Shares();
            for (int i = 0; i < _data.fills.Count; i++) _data.fills[i].mapShare = shares[i];
            int neighbour = remove + 1 < _data.fills.Count ? remove + 1 : remove - 1;
            if (neighbour >= 0) _data.fills[neighbour].mapShare += shares[remove];
            if (_editingGroup == _data.fills[remove]) _editingGroup = null;
            _data.fills.RemoveAt(remove);
            MarkChanged();
        }
    }

    // The Gradient Outer Lines box: a gradient from the group's outline, with a normal gradient's
    // settings under it while ticked.
    void DrawOuterLinesGradient(ModelTexturingData.Fill g)
    {
        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.Toggle(new GUIContent("Gradient Outer Lines",
            "A gradient from the group's outer lines, kept up to date as the group's faces change."), g.gradientOuterLines);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Gradient outer lines");
            g.gradientOuterLines = on;
            MarkChanged();
        }
        if (!g.gradientOuterLines) return;
        if (g.outerLinesGradient == null) g.outerLinesGradient = new ModelTexturingData.LineGradient { name = "Outer Lines" };
        EditorGUI.indentLevel++;
        DrawGradientSettings(g.outerLinesGradient);
        EditorGUI.indentLevel--;
    }

    // Colour, Opacity, Length — shared by the gradients and the outer lines one.
    void DrawGradientSettings(ModelTexturingData.LineGradient gr)
    {
        EditorGUI.BeginChangeCheck();
        var colour = EditorGUILayout.ColorField(new GUIContent("Colour"), gr.colour, true, false, false);
        // Opacity is the colour's alpha: how much it covers the group's colour at its lines.
        colour.a = EditorGUILayout.Slider("Opacity", gr.colour.a, 0f, 1f);
        float length = EditorGUILayout.Slider(
            new GUIContent("Length", "How far it reaches from its lines — 1 = all the way to the far side of the group."),
            gr.length, 0.01f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Gradient");
            gr.colour = colour;
            gr.length = length;
            MarkChanged();
        }
    }

    // A face group's gradients, each a foldout like the groups: Name, Colour, Opacity, Length.
    void DrawGradients(ModelTexturingData.Fill g)
    {
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("Gradients", EditorStyles.boldLabel);
        if (g.gradients.Count == 0)
        {
            EditorGUILayout.LabelField("None yet — select lines on this group and press Group Selected Lines.", EditorStyles.miniLabel);
            return;
        }

        int remove = -1;
        for (int i = 0; i < g.gradients.Count; i++)
        {
            var gr = g.gradients[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            _gradientExpanded.TryGetValue(gr, out bool open);
            _gradientExpanded[gr] = EditorGUILayout.Foldout(open, $"{gr.name}   ({Mathf.Max(gr.lines.Count, gr.lineCorners?.Count ?? 0) / 2} lines)", true);
            if (GUILayout.Button("✕", GUILayout.Width(22))) remove = i;
            EditorGUILayout.EndHorizontal();

            if (_gradientExpanded[gr])
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField("Name", gr.name);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Rename gradient");
                    gr.name = name;
                    EditorUtility.SetDirty(_data);
                }

                DrawGradientSettings(gr);
            }
            EditorGUILayout.EndVertical();
        }

        if (remove >= 0)
        {
            Undo.RecordObject(_data, "Remove gradient");
            g.gradients.RemoveAt(remove);
            MarkChanged();
        }
    }

    // A face group's decals, each a foldout like the gradients: Name, Texture, Scale X/Y,
    // Offset X/Y, Opacity.
    void DrawDecals(ModelTexturingData.Fill g)
    {
        g.decals ??= new List<ModelTexturingData.Decal>();
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("Decals", EditorStyles.boldLabel);
        if (g.decals.Count == 0)
        {
            EditorGUILayout.LabelField("None yet — turn on Add Decal and click one of this group's faces.", EditorStyles.miniLabel);
            return;
        }

        int remove = -1;
        for (int i = 0; i < g.decals.Count; i++)
        {
            var d = g.decals[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            _decalExpanded.TryGetValue(d, out bool open);
            _decalExpanded[d] = EditorGUILayout.Foldout(open, d.texture ? $"{d.name}   ({d.texture.name})" : $"{d.name}   (no image)", true);
            if (GUILayout.Button("✕", GUILayout.Width(22))) remove = i;
            EditorGUILayout.EndHorizontal();

            if (_decalExpanded[d])
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField("Name", d.name);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Rename decal");
                    d.name = name;
                    EditorUtility.SetDirty(_data);
                }

                EditorGUI.BeginChangeCheck();
                var texture = (Texture2D)EditorGUILayout.ObjectField("Texture", d.texture, typeof(Texture2D), false);
                const string sizeTip = " — as a part of the model's longest side.";
                float scaleX = EditorGUILayout.Slider(new GUIContent("Scale X", "Its width" + sizeTip), d.scale.x, 0.005f, 1f);
                float scaleY = EditorGUILayout.Slider(new GUIContent("Scale Y", "Its height" + sizeTip), d.scale.y, 0.005f, 1f);
                float offsetX = EditorGUILayout.Slider(new GUIContent("Offset X", "Slides it across the surface from where it was placed" + sizeTip), d.offset.x, -0.5f, 0.5f);
                float offsetY = EditorGUILayout.Slider(new GUIContent("Offset Y", "Slides it up or down the surface from where it was placed" + sizeTip), d.offset.y, -0.5f, 0.5f);
                float opacity = EditorGUILayout.Slider("Opacity", d.opacity, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Decal");
                    d.texture = texture;
                    d.scale = new Vector2(scaleX, scaleY);
                    d.offset = new Vector2(offsetX, offsetY);
                    d.opacity = opacity;
                    MarkChanged();
                }
            }
            EditorGUILayout.EndVertical();
        }

        if (remove >= 0)
        {
            Undo.RecordObject(_data, "Remove decal");
            g.decals.RemoveAt(remove);
            MarkChanged();
        }
    }

    // ─────────────────────────────────────────── Map Space bar

    // Each group's share of the map, adding up to 1.
    float[] Shares()
    {
        var fills = _data.fills;
        var shares = new float[fills.Count];
        float total = 0f;
        foreach (var g in fills) total += Mathf.Max(0f, g.mapShare);
        for (int i = 0; i < shares.Length; i++)
            shares[i] = total > 0f ? Mathf.Max(0f, fills[i].mapShare) / total : 1f / shares.Length;
        return shares;
    }

    // The shade map split between the groups, like the LOD Group bar: each section is a group's share
    // of the map, in its own colour. Drag the line between two sections to move map space from one to
    // the other — the bar is always full, so the whole map is always used.
    void DrawMapSpace()
    {
        var fills = _data.fills;
        Rect bar = EditorGUI.PrefixLabel(GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true)),
            new GUIContent("Map Space", "How much of the shade map each group's faces get. More map = finer detail and finer noise."));
        EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.25f));
        var label = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
        if (fills.Count == 0)
        {
            GUI.Label(bar, "No groups yet", label);
            return;
        }

        float[] shares = Shares();
        float x = bar.x;
        for (int i = 0; i < fills.Count; i++)
        {
            var section = new Rect(x, bar.y, shares[i] * bar.width, bar.height);
            Color c = fills[i].colour;
            c.a = 1f;
            EditorGUI.DrawRect(section, c);
            label.normal.textColor = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f > 0.5f ? Color.black : Color.white;
            string pct = fills[i].triangles.Count == 0 ? "no faces" : $"{shares[i] * 100f:0}%";
            GUI.Label(section, $"{fills[i].name}\n{pct}", label);
            x += section.width;
        }

        Event e = Event.current;
        float start = 0f;
        for (int i = 0; i < fills.Count - 1; i++)
        {
            float at = start + shares[i];
            float lineX = bar.x + at * bar.width;
            EditorGUI.DrawRect(new Rect(lineX - 1f, bar.y, 2f, bar.height), new Color(0.1f, 0.1f, 0.1f, 1f));
            var grab = new Rect(lineX - 4f, bar.y, 8f, bar.height);
            EditorGUIUtility.AddCursorRect(grab, MouseCursor.ResizeHorizontal);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !grab.Contains(e.mousePosition)) break;
                    GUIUtility.hotControl = id;
                    Undo.RecordObject(_data, "Map space");
                    for (int j = 0; j < fills.Count; j++) fills[j].mapShare = shares[j];
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    Undo.RecordObject(_data, "Map space");
                    float right = at + shares[i + 1];
                    float to = Mathf.Clamp((e.mousePosition.x - bar.x) / bar.width, start + MinShare, right - MinShare);
                    fills[i].mapShare = to - start;
                    fills[i + 1].mapShare = right - to;
                    MarkChanged();
                    Repaint();
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
            start = at;
        }
    }

    // Lock and Hide are editor only: the bake ignores them. A locked or hidden group's faces leave
    // the selection, since they can no longer be picked.
    void SetLockHide(ModelTexturingData.Fill g, bool locked, bool hidden)
    {
        Undo.RecordObject(_data, hidden != g.hidden ? "Hide group" : "Lock group");
        g.locked = locked;
        g.hidden = hidden;
        EditorUtility.SetDirty(_data);
        if (locked || hidden) _selection.RemoveWhere(g.triangles.Contains);
        _hoverTri = -1;
        _previewDirty = true;
        SceneView.RepaintAll();
    }

    // Triangles of every group that matches.
    HashSet<int> TrianglesOf(System.Predicate<ModelTexturingData.Fill> which)
    {
        var set = new HashSet<int>();
        if (_data == null) return set;
        foreach (var g in _data.fills)
            if (which(g))
                foreach (int t in g.triangles)
                    if (t >= 0 && t < TriCount) set.Add(t);
        return set;
    }

    void DrawDisplaySettings()
    {
        _displayFoldout = EditorGUILayout.Foldout(_displayFoldout, "Display", true);
        if (!_displayFoldout) return;
        EditorGUI.BeginChangeCheck();
        _selectionColour = EditorGUILayout.ColorField("Selection", _selectionColour);
        _hoverColour = EditorGUILayout.ColorField("Hover", _hoverColour);
        _wireColour = EditorGUILayout.ColorField("Wireframe", _wireColour);
        _creaseColour = EditorGUILayout.ColorField(new GUIContent("Crease", "Creased lines on the wireframe — fainter the softer the crease."), _creaseColour);
        if (EditorGUI.EndChangeCheck())
        {
            SaveDisplay();
            SceneView.RepaintAll();
        }
    }

    // Faces as Blender counts them — a quad's two triangles are one face.
    int FaceCount(IEnumerable<int> triangles)
    {
        if (_partner == null) return 0;
        var set = triangles as ICollection<int> ?? new List<int>(triangles);
        int n = 0;
        foreach (int t in set)
        {
            if (t < 0 || t >= _partner.Length) continue;
            int p = _partner[t];
            if (p < 0 || p > t || !set.Contains(p)) n++;
        }
        return n;
    }

    // A face belongs to one group: grouping it again moves it out of the group it was in.
    void GroupSelection()
    {
        Undo.RecordObject(_data, "Group faces");
        foreach (var f in _data.fills) f.triangles.RemoveAll(t => _selection.Contains(t));

        // An equal share of the map; the others shrink to make room.
        var shares = Shares();
        int n = _data.fills.Count;
        for (int i = 0; i < n; i++) _data.fills[i].mapShare = shares[i] * n / (n + 1f);
        var group = new ModelTexturingData.Fill { name = "Group " + (n + 1), mapShare = 1f / (n + 1), opacityOff = true };
        group.triangles.AddRange(_selection);
        group.triangles.Sort();
        _data.fills.Add(group);
        // Only the new group is left open.
        _expanded.Clear();
        _gradientExpanded.Clear();
        _expanded[group] = true;

        _data.mesh = _cachedMesh;
        _data.triangleCount = TriCount;
        _selection.Clear();
        _editingGroup = null;
        MarkChanged();
    }

    // The selected lines become a gradient on the face group they touch most (a line touches the
    // groups of the faces either side of it); a tie goes to a group whose foldout is open, then to
    // the first. Lines that don't touch that group are left out.
    void GroupLines()
    {
        var groupOf = new int[TriCount];
        for (int t = 0; t < groupOf.Length; t++) groupOf[t] = -1;
        for (int g = 0; g < _data.fills.Count; g++)
            foreach (int t in _data.fills[g].triangles)
                if (t >= 0 && t < groupOf.Length) groupOf[t] = g;

        var lines = new List<int>(_lineSelection);
        lines.Sort();
        var counts = new int[_data.fills.Count];
        foreach (int i in lines)
        {
            var touched = new HashSet<int>();
            foreach (int t in _wireOwners[i])
                if (groupOf[t] >= 0) touched.Add(groupOf[t]);
            foreach (int g in touched) counts[g]++;
        }
        bool Open(int g) => _expanded.TryGetValue(_data.fills[g], out bool o) && o;
        int best = -1;
        for (int g = 0; g < counts.Length; g++)
        {
            if (counts[g] == 0) continue;
            if (best < 0 || counts[g] > counts[best] || (counts[g] == counts[best] && Open(g) && !Open(best))) best = g;
        }
        if (best < 0)
        {
            ShowNotification(new GUIContent("Those lines don't touch any face group — group the faces first"));
            return;
        }

        var group = _data.fills[best];
        Undo.RecordObject(_data, "Group lines");
        var gradient = new ModelTexturingData.LineGradient { name = "Gradient " + (group.gradients.Count + 1) };
        foreach (int i in lines)
        {
            if (!_wireOwners[i].Exists(t => groupOf[t] == best)) continue;
            gradient.lines.Add(_rest[_wireEdges[i * 2]]);
            gradient.lines.Add(_rest[_wireEdges[i * 2 + 1]]);
            gradient.lineCorners.Add(_weldCorner[_weld[_wireEdges[i * 2]]]);
            gradient.lineCorners.Add(_weldCorner[_weld[_wireEdges[i * 2 + 1]]]);
        }
        group.gradients.Add(gradient);
        // Only the new gradient (and its group) is left open.
        _expanded.Clear();
        _gradientExpanded.Clear();
        _expanded[group] = true;
        _gradientExpanded[gradient] = true;
        _lineSelection.Clear();
        _editingCreaseGroup = null;
        MarkChanged();
    }

    // A new decal on the face under the cursor, facing the way that face does, filed under the
    // face's group. Its spot is pinned to that face (and kept as a rest-pose point too), so it
    // stays put whatever the pose, and rides the face when the model is reshaped.
    void PlaceDecal(Camera cam)
    {
        if (_hoverTri < 0) return;
        var group = _data.fills.Find(f => f.triangles.Contains(_hoverTri));
        if (group == null)
        {
            ShowNotification(new GUIContent("That face isn't in a group — group it first"));
            return;
        }
        int ia = _tris[_hoverTri * 3], ib = _tris[_hoverTri * 3 + 1], ic = _tris[_hoverTri * 3 + 2];
        Vector3 a = _rest[ia], b = _rest[ib], c = _rest[ic];
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.sqrMagnitude < 1e-20f) return;
        normal = Unit(normal);
        // The image stands the way up you're looking at it: the view's up, laid flat on the face.
        // In the stage the copy stands in its rest pose, so the renderer's own space is the mesh's.
        Vector3 viewUp = cam ? _target.transform.InverseTransformDirection(cam.transform.up) : Vector3.up;
        Vector3 up = viewUp - normal * Vector3.Dot(viewUp, normal);
        if (up.sqrMagnitude < 1e-8f) up = Vector3.Cross(normal, Mathf.Abs(normal.x) < 0.9f ? Vector3.right : Vector3.forward);

        Undo.RecordObject(_data, "Add decal");
        group.decals ??= new List<ModelTexturingData.Decal>();
        var decal = new ModelTexturingData.Decal
        {
            name = "Decal " + (group.decals.Count + 1),
            point = a + (b - a) * _hoverHit.x + (c - a) * _hoverHit.y,
            normal = normal,
            up = Unit(up),
            triangle = _hoverTri,
            facePoint = new Vector2(_hoverHit.x, _hoverHit.y),
            faceUp = FaceAmounts(Unit(up), b - a, c - a),
        };
        group.decals.Add(decal);
        // Only the new decal (and its group) is left open.
        _expanded.Clear();
        _gradientExpanded.Clear();
        _decalExpanded.Clear();
        _expanded[group] = true;
        _decalExpanded[decal] = true;
        MarkChanged();
        Repaint();
    }

    // ─────────────────────────────────────────── Target / data

    static Mesh MeshOf(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = r ? r.GetComponent<MeshFilter>() : null;
        return mf ? mf.sharedMesh : null;
    }

    void UseSelection()
    {
        var go = Selection.activeGameObject;
        if (!go) return;
        // The renderer with the biggest mesh that is a real asset — skips generated meshes like the feathers.
        Renderer best = null;
        int bestCount = -1;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var m = MeshOf(r);
            if (m == null || !AssetDatabase.Contains(m)) continue;
            if (m.vertexCount > bestCount) { best = r; bestCount = m.vertexCount; }
        }
        if (best) SetTarget(best);
    }

    void SetTarget(Renderer r)
    {
        ClearPreview();
        _target = r;
        if (r == null || ModelTexturingStage.Current == null || r.gameObject.scene != ModelTexturingStage.Current.scene)
            _returnTarget = null;
        Mesh worn = WornMesh();
        if (_data == null || !Fits(_data, worn)) _data = FindDataFor(worn);
        Mesh source = SourceMesh();
        bool sameMesh = source == _cachedMesh;
        _cachedMesh = null;
        _hoverTri = -1;
        if (!sameMesh) _selection.Clear();
        _expanded.Clear();
        _previewDirty = true;
        Repaint();
    }

    // Opens the target (or the selection) in its own empty stage, like Prefab Mode, and works on
    // the copy there. Closing the stage hands the window back to the original.
    void OpenInStage()
    {
        var stage = ModelTexturingStage.Current;
        var selected = Selection.activeGameObject;
        if (selected != null && (stage == null || selected.scene != stage.scene)) UseSelection();
        // Already working on a copy in the stage: reopen from the original, not from the copy.
        Renderer source = stage != null && _target != null && _target.gameObject.scene == stage.scene && _returnTarget != null
            ? _returnTarget
            : _target;
        OpenInStage(source, source);
    }

    // Opens source's model in the stage and works on the copy there. Closing the stage hands the
    // window back to returnTo — the real model Bake dresses; null when there isn't one.
    void OpenInStage(Renderer source, Renderer returnTo)
    {
        if (source == null) return;
        GameObject root = ModelRootOf(source);
        string path = AnimationUtility.CalculateTransformPath(source.transform, root.transform);
        var stage = ModelTexturingStage.Open(root);
        if (stage == null || stage.Copy == null) return;

        Transform found = path.Length == 0 ? stage.Copy.transform : stage.Copy.transform.Find(path);
        var copy = found ? found.GetComponent<Renderer>() : null;
        if (copy == null) return;
        _returnTarget = returnTo;
        SetTarget(copy);
        DressStageCopy();
    }

    // Every Model Texturing Data in the project; picking one opens it with its model.
    void ShowOpenDataMenu(Rect below)
    {
        var menu = new GenericMenu();
        foreach (string guid in AssetDatabase.FindAssets("t:ModelTexturingData"))
        {
            var d = AssetDatabase.LoadAssetAtPath<ModelTexturingData>(AssetDatabase.GUIDToAssetPath(guid));
            if (d == null) continue;
            if (d.mesh == null && d.replicant == null) menu.AddDisabledItem(new GUIContent(d.name + "  (no model)"));
            else menu.AddItem(new GUIContent(d.name), d == _data, () => OpenData(d));
        }
        if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("No Model Texturing Data yet — press New…"));
        menu.DropDown(below);
    }

    // The data and its model together in the stage, the model wearing the data's replicant. The
    // model comes from the open scenes when one there wears the data (so Bake still dresses it),
    // else from the model file itself — then there is no real model for Bake to dress.
    void OpenData(ModelTexturingData data)
    {
        Renderer model = SceneModelFor(data);
        Renderer source = model != null ? model : ModelFileRendererFor(data.mesh);
        if (source == null)
        {
            ShowNotification(new GUIContent($"Couldn't find the model {data.name} was made on"));
            return;
        }
        _data = data;
        _expanded.Clear();
        _previewDirty = true;
        OpenInStage(source, model);
    }

    // A renderer in the open scenes wearing the data's mesh or replicant — the one the window
    // already knows first.
    Renderer SceneModelFor(ModelTexturingData d)
    {
        bool Wears(Renderer r)
        {
            Mesh m = r != null ? MeshOf(r) : null;
            return m != null && (m == d.mesh || m == d.replicant);
        }
        Renderer known = TargetInStage ? _returnTarget : _target;
        if (Wears(known) && !EditorSceneManager.IsPreviewScene(known.gameObject.scene)) return known;

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (Wears(r)) return r;
        }
        return null;
    }

    // The renderer inside the model file the mesh was imported from.
    static Renderer ModelFileRendererFor(Mesh mesh)
    {
        if (mesh == null) return null;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(mesh));
        if (model == null) return null;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            if (MeshOf(r) == mesh) return r;
        return null;
    }

    bool TargetInStage =>
        _target != null && ModelTexturingStage.Current != null && _target.gameObject.scene == ModelTexturingStage.Current.scene;

    // What the model wears — in the stage, the original's (the copy may already be in the replicant).
    Mesh WornMesh() =>
        TargetInStage && _returnTarget != null ? MeshOf(_returnTarget) : MeshOf(_target);

    // The mesh the faces are numbered by: the original, even when the model wears the replicant
    // (it does after a Bake). The two share every vertex.
    Mesh SourceMesh()
    {
        Mesh m = WornMesh();
        return _data != null && m != null && (m == _data.replicant || m == _hideMesh) && _data.mesh != null ? _data.mesh : m;
    }

    static bool Fits(ModelTexturingData d, Mesh m) => d.mesh == null || d.mesh == m || d.replicant == m;

    static void SetMesh(Renderer r, Mesh m)
    {
        if (r is SkinnedMeshRenderer smr) smr.sharedMesh = m;
        else if (r != null && r.TryGetComponent(out MeshFilter mf)) mf.sharedMesh = m;
    }

    // The stage copy wears the replicant — rebuilt fresh from the original mesh each time — and its
    // materials, so you work on the blank materials the map is baked into rather than the model's
    // finished look.
    void DressStageCopy()
    {
        if (!TargetInStage) return;
        if (_data != null)
        {
            var replicant = BuildReplicant();
            if (replicant != null) SetMesh(_target, replicant);
        }
        DressSlots(_target);
        HideLevelSelectEffects();
        _previewDirty = true;
        SceneView.RepaintAll();
    }

    // Slot 0 the opaque material, slot 1 the transparent one when the mesh has see-through faces.
    void DressSlots(Renderer r)
    {
        Mesh worn = MeshOf(r);
        if (r == null || worn == null || _data == null) return;
        var opaque = EnsureMaterial(false);
        if (opaque == null) return;
        var slots = new Material[Mathf.Max(1, worn.subMeshCount)];
        slots[0] = opaque;
        if (slots.Length > 1) slots[1] = EnsureMaterial(true);
        r.sharedMaterials = slots;
    }

    // ─────────────────────────────────────────── Materials

    // The title the materials are named from; the mesh's name until one is typed.
    string MaterialTitle
    {
        get
        {
            if (_data == null) return "";
            string t = string.IsNullOrWhiteSpace(_data.materialTitle)
                ? (_data.mesh != null ? _data.mesh.name : _data.name)
                : _data.materialTitle;
            foreach (char c in Path.GetInvalidFileNameChars()) t = t.Replace(c.ToString(), "");
            return t.Trim();
        }
    }

    string MaterialPath(bool transparent) => MaterialPath(MaterialTitle, transparent);

    string MaterialPath(string title, bool transparent) =>
        Path.GetDirectoryName(AssetDatabase.GetAssetPath(_data)).Replace('\\', '/') + "/" +
        title + (transparent ? "Transparent" : "Opaque") + ".mat";

    // The names follow the title: materials already made under the old one are renamed.
    void SetMaterialTitle(string title)
    {
        string oldTitle = MaterialTitle;
        Undo.RecordObject(_data, "Material title");
        _data.materialTitle = title.Trim();
        EditorUtility.SetDirty(_data);
        string newTitle = MaterialTitle;
        if (newTitle == oldTitle || newTitle.Length == 0) return;

        foreach (bool transparent in new[] { false, true })
        {
            var mat = transparent ? _data.transparentMaterial : _data.material;
            string from = AssetDatabase.GetAssetPath(mat);
            if (mat == null || from != MaterialPath(oldTitle, transparent)) continue;
            string error = AssetDatabase.RenameAsset(from, Path.GetFileNameWithoutExtension(MaterialPath(newTitle, transparent)));
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"Model Texturing Tool: couldn't rename {from}: {error}");
        }
        // The model's transparent graph goes with them.
        string graphFrom = Path.ChangeExtension(MaterialPath(oldTitle, true), ".shadergraph");
        if (AssetDatabase.LoadAssetAtPath<Shader>(graphFrom) != null)
        {
            string error = AssetDatabase.RenameAsset(graphFrom, Path.GetFileNameWithoutExtension(MaterialPath(newTitle, true)));
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"Model Texturing Tool: couldn't rename {graphFrom}: {error}");
        }
        _graphKey = null;
        AssetDatabase.SaveAssetIfDirty(_data);
    }

    // The opaque or transparent material, made from its graph at "<title>Opaque/Transparent.mat" next
    // to the data if it isn't there yet. A new one starts from the material the mesh wore before, so
    // its colours carry over.
    Material EnsureMaterial(bool transparent)
    {
        if (_data == null || MaterialTitle.Length == 0) return null;
        var current = transparent ? _data.transparentMaterial : _data.material;
        string path = MaterialPath(transparent);
        if (current != null && AssetDatabase.GetAssetPath(current) == path)
        {
            if (transparent) EnsureTransparentGraph();   // on the model's own graph, with every group's value
            return current;
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = transparent ? EnsureTransparentGraph() : FindGraph(OpaqueGraph);
            if (shader == null)
            {
                Debug.LogWarning($"Model Texturing Tool: no {(transparent ? TransparentGraph : OpaqueGraph)} shader graph found.");
                return null;
            }
            mat = new Material(shader);
            var from = transparent ? _data.material : current ?? BakeMaterial();
            if (from != null) mat.CopyMatchingPropertiesFromMaterial(from);
            if (transparent)
            {
                mat.SetFloat(DoubleSidedProp, 1f);   // see-through faces start with both sides showing
                foreach (var p in TransparencyProps())
                    if (mat.HasProperty(p.reference)) mat.SetFloat(p.reference, p.group.Transparency);
            }
            if (_data.shadeMap != null) mat.SetTexture(ShadeMapProp, _data.shadeMap);
            AssetDatabase.CreateAsset(mat, path);
        }

        Undo.RecordObject(_data, "Material");
        if (transparent) _data.transparentMaterial = mat;
        else _data.material = mat;
        EditorUtility.SetDirty(_data);
        if (transparent)
        {
            _graphKey = null;   // now it's the data's material, give it the groups' values
            EnsureTransparentGraph();
        }
        return mat;
    }

    static Shader FindGraph(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Shader"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name && path.EndsWith(".shadergraph"))
                return AssetDatabase.LoadAssetAtPath<Shader>(path);
        }
        return null;
    }

    // The old shared AngelMaterial — what a new opaque material copies its settings from.
    static Material BakeMaterial()
    {
        foreach (string guid in AssetDatabase.FindAssets("AngelMaterial t:Material"))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null && m.name == "AngelMaterial") return m;
        }
        return null;
    }

    // ─────────────────────────────────────────── Replicant

    string ReplicantPath() => Path.ChangeExtension(AssetDatabase.GetAssetPath(_data), null) + "_Replicant.asset";

    // A copy of the original mesh — every vertex, bone weight, blend shape and bind pose — so it is
    // skinned to the same armature, with the shade map's layout on UV1; saved next to the data.
    // Refilled in place when it already exists, so anything pointing at it keeps working.
    Mesh BuildReplicant()
    {
        if (_data == null || !EnsureCache()) return null;
        string path = AssetDatabase.GetAssetPath(_data.replicant);
        if (string.IsNullOrEmpty(path)) path = ReplicantPath();
        var replicant = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (replicant == null)
        {
            replicant = new Mesh();
            AssetDatabase.CreateAsset(replicant, path);
        }
        replicant.name = Path.GetFileNameWithoutExtension(path);
        ApplyLayout(replicant, true);

        if (_data.replicant != replicant)
        {
            Undo.RecordObject(_data, "Replicant");
            _data.replicant = replicant;
            EditorUtility.SetDirty(_data);
        }
        AssetDatabase.SaveAssetIfDirty(replicant);
        return replicant;
    }

    // Which faces are see-through, as last written into the replicant — skips rewriting it when
    // a change didn't move any face across.
    string _splitKey;

    void SplitReplicant()
    {
        if (!WearsReplicant(_target)) return;
        SplitReplicant(_data.replicant);
        ApplyHide();
    }

    // The stage copy in the replicant — or in the hide copy of it.
    bool WearsReplicant(Renderer r)
    {
        if (!TargetInStage || _data == null || _data.replicant == null) return false;
        Mesh m = MeshOf(r);
        return m == _data.replicant || (_hideMesh != null && m == _hideMesh);
    }

    // ─────────────────────────────────────────── Map layout

    // The shade map's own layout. Each group's faces keep the pieces (islands) they have on the
    // generated unique UVs, scaled up or down together so the group covers its share of the map (the
    // Map Space bar), and every group's pieces are packed onto the map side by side. Faces outside
    // every group all sit on one empty pixel in the corner — they're never coloured. Vertices are split
    // where a group's edge isn't already a seam, so each side can sit in its own place.
    class MapLayout
    {
        public int[] corner;      // triangle corner -> replicant vertex
        public int[] source;      // replicant vertex -> original vertex
        public Vector2[] uv;      // replicant vertex -> place on the map; null with no unique UVs
        public long topology;     // which faces are in which group — what the vertex split follows
        public string key;
        public int version;
    }

    class Island
    {
        public int group;
        public Vector2 min, max;
        public float w, h;        // scaled to the group's share, before the map-wide scale
        public bool turned;       // turned a quarter so it lies wider than tall
        public Vector2 place;     // map pixels
    }

    MapLayout _layout;
    int _layoutVersion;
    Mesh _layoutMesh;             // the replicant ApplyLayout last wrote, and what it wrote
    long _layoutMeshTopology;
    long _layoutMeshSubdiv;       // SubdivisionKey it was smoothed with, 0 = not smoothed
    int _layoutMeshVersion = -1;
    [System.NonSerialized] int _replicantBuilds;   // +1 per rebuild, so the hide copy is remade too

    // The model wears the replicant (or its hide copy), so the map's layout is on it.
    bool WearsLayout(Renderer r)
    {
        if (r == null || _data == null || _data.replicant == null) return false;
        Mesh m = MeshOf(r);
        return m == _data.replicant || (_hideMesh != null && m == _hideMesh);
    }

    // Writes the layout into the replicant: rebuilt when faces changed group (the vertices split
    // differently), else only its UV1 moves.
    void ApplyLayout(Mesh replicant, bool rebuild)
    {
        if (replicant == null || !EnsureCache()) return;
        var layout = Layout();
        long subdivKey = SubdivisionKey(out var creases);
        bool smooth = subdivKey != 0;
        int expected = !smooth ? layout.source.Length : _subdiv != null ? _subdiv.vertexCount : -1;
        if (rebuild || replicant != _layoutMesh || replicant.vertexCount != expected ||
            _layoutMeshTopology != layout.topology || _layoutMeshSubdiv != subdivKey)
        {
            string name = replicant.name;
            var fresh = BuildLayoutMesh(layout);
            _subdiv = null;
            if (smooth)
            {
                var smoothed = Subdivide(fresh, layout, Mathf.Clamp(_data.subdivisionLevels, 1, 2), creases);
                DestroyImmediate(fresh);
                fresh = smoothed;
                _subdiv.topology = layout.topology;
                _subdiv.key = subdivKey;
            }
            Debug.Log($"[ModelTexturingCrash] rebuilding replicant '{replicant.name}': " +
                      $"{replicant.vertexCount} → {fresh.vertexCount} vertices, worn by {(_target ? _target.name : "nothing")}");   // DEBUG: native crash hunt
            EditorUtility.CopySerialized(fresh, replicant);
            DestroyImmediate(fresh);
            replicant.name = name;
            // CopySerialized refills the mesh, but when the vertex count stays the same the GPU keeps
            // drawing its old vertices with the new faces (a blob) until a vertex channel is written
            // through the Mesh API — seen 2026-10-05. Rewriting UV1 is that write.
            if (layout.uv != null) replicant.SetUVs(1, DrawnUVs(layout, replicant));
            _layoutMesh = replicant;
            _layoutMeshTopology = layout.topology;
            _layoutMeshSubdiv = subdivKey;
            _replicantBuilds++;
            _splitKey = null;
            SplitReplicant(replicant);
        }
        else if (_layoutMeshVersion != layout.version && layout.uv != null)
            replicant.SetUVs(1, DrawnUVs(layout, replicant));
        else return;
        _layoutMeshVersion = layout.version;
        EditorUtility.SetDirty(replicant);
    }

    MapLayout Layout()
    {
        int triCount = TriCount;
        var groupOf = new int[triCount];
        for (int t = 0; t < triCount; t++) groupOf[t] = -1;
        for (int g = 0; g < _data.fills.Count; g++)
            foreach (int t in _data.fills[g].triangles)
                if (t >= 0 && t < triCount) groupOf[t] = g;

        unchecked
        {
            long topology = (long)14695981039346656037UL;
            void Mix(int v) { topology = (topology ^ v) * 1099511628211L; }
            Mix(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_cachedMesh));
            Mix(_rest.Length);
            Mix(triCount);
            Mix(HasUV2 ? 1 : 0);
            foreach (int g in groupOf) Mix(g);
            float[] shares = Shares();
            string key = topology + "|" + _data.textureSize + "|" + string.Join(",", shares);
            if (_layout != null && _layout.key == key) return _layout;

            _layout = BuildLayout(groupOf, shares, _data.textureSize);
            _layout.topology = topology;
            _layout.key = key;
            _layout.version = ++_layoutVersion;
            return _layout;
        }
    }

    MapLayout BuildLayout(int[] groupOf, float[] shares, int size)
    {
        int vertexCount = _rest.Length;
        var layout = new MapLayout();

        // Vertices: one per original vertex per group it's used in. The first use keeps the original
        // number, so a mesh without groups comes out vertex for vertex the same.
        var source = new List<int>(vertexCount);
        var groupOfVertex = new List<int>(vertexCount);
        for (int v = 0; v < vertexCount; v++)
        {
            source.Add(v);
            groupOfVertex.Add(-1);
        }
        var claimed = new bool[vertexCount];
        var made = new Dictionary<long, int>();
        layout.corner = new int[_tris.Length];
        for (int c = 0; c < _tris.Length; c++)
        {
            int v = _tris[c], g = groupOf[c / 3];
            long key = ((long)(g + 1) << 32) | (uint)v;
            if (!made.TryGetValue(key, out int r))
            {
                if (!claimed[v])
                {
                    claimed[v] = true;
                    r = v;
                }
                else
                {
                    r = source.Count;
                    source.Add(v);
                    groupOfVertex.Add(-1);
                }
                groupOfVertex[r] = g;
                made[key] = r;
            }
            layout.corner[c] = r;
        }
        layout.source = source.ToArray();
        if (!HasUV2) return layout;

        // Pieces: a group's faces joined through shared vertices — on the unique UVs, a vertex is
        // only shared inside one piece.
        int count = layout.source.Length;
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }
        for (int t = 0; t < groupOf.Length; t++)
        {
            if (groupOf[t] < 0) continue;
            int a = Find(layout.corner[t * 3]);
            parent[Find(layout.corner[t * 3 + 1])] = a;
            parent[Find(layout.corner[t * 3 + 2])] = a;
        }
        var islands = new Dictionary<int, Island>();
        for (int r = 0; r < count; r++)
        {
            int g = groupOfVertex[r];
            if (g < 0) continue;
            Vector2 p = _uv2[layout.source[r]];
            int root = Find(r);
            if (!islands.TryGetValue(root, out var island))
                islands[root] = island = new Island { group = g, min = p, max = p };
            island.min = Vector2.Min(island.min, p);
            island.max = Vector2.Max(island.max, p);
        }

        // Each group's pieces scaled together so their area is the group's share. Groups without
        // faces get nothing — their share goes round the rest, so the map stays full.
        int groups = shares.Length;
        var area = new float[groups];
        foreach (var island in islands.Values)
        {
            Vector2 s = Vector2.Max(island.max - island.min, new Vector2(1e-6f, 1e-6f));
            area[island.group] += s.x * s.y;
        }
        float shareTotal = 0f;
        for (int g = 0; g < groups; g++) if (area[g] > 0f) shareTotal += shares[g];
        var groupScale = new float[groups];
        for (int g = 0; g < groups; g++)
            groupScale[g] = area[g] > 0f && shareTotal > 0f ? Mathf.Sqrt(shares[g] / shareTotal / area[g]) : 0f;

        var list = new List<Island>(islands.Values);
        foreach (var island in list)
        {
            Vector2 s = Vector2.Max(island.max - island.min, new Vector2(1e-6f, 1e-6f)) * groupScale[island.group];
            island.turned = s.y > s.x;
            island.w = island.turned ? s.y : s.x;
            island.h = island.turned ? s.x : s.y;
        }
        list.Sort((a, b) => b.h.CompareTo(a.h));

        // Rows of pieces, tallest first; the map-wide scale is the biggest that still fits.
        bool Pack(float scale, bool place)
        {
            float x = IslandPadding, y = IslandPadding, row = 0f;
            foreach (var island in list)
            {
                float w = island.w * scale, h = island.h * scale;
                if (x + w + IslandPadding > size)
                {
                    y += row + IslandPadding;
                    x = IslandPadding;
                    row = 0f;
                }
                if (x + w + IslandPadding > size || y + h + IslandPadding > size) return false;
                if (place) island.place = new Vector2(x, y);
                x += w + IslandPadding;
                row = Mathf.Max(row, h);
            }
            return true;
        }
        float lo = 0f, hi = size;   // the pieces' area adds up to 1, so they can't be bigger than the map
        for (int i = 0; i < 40; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Pack(mid, false)) lo = mid;
            else hi = mid;
        }
        Pack(lo, true);

        layout.uv = new Vector2[count];
        var empty = new Vector2(0.5f / size, 0.5f / size);   // the corner pixel, never packed onto
        for (int r = 0; r < count; r++)
        {
            int g = groupOfVertex[r];
            if (g < 0)
            {
                layout.uv[r] = empty;
                continue;
            }
            var island = islands[Find(r)];
            Vector2 local = _uv2[layout.source[r]] - island.min;
            if (island.turned) local = new Vector2(local.y, island.max.x - island.min.x - local.x);
            layout.uv[r] = (island.place + local * (groupScale[g] * lo)) / size;
        }
        return layout;
    }

    // The original mesh with its vertices split as the layout says, and the layout on UV1.
    Mesh BuildLayoutMesh(MapLayout layout)
    {
        Mesh src = _cachedMesh;
        int[] from = layout.source;
        var m = new Mesh { indexFormat = from.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.vertices = Remap(src.vertices, from);
        if (src.HasVertexAttribute(VertexAttribute.Normal)) m.normals = Remap(src.normals, from);
        if (src.HasVertexAttribute(VertexAttribute.Tangent)) m.tangents = Remap(src.tangents, from);
        if (src.HasVertexAttribute(VertexAttribute.Color)) m.colors = Remap(src.colors, from);
        for (int ch = 0; ch < 8; ch++)
        {
            var attribute = VertexAttribute.TexCoord0 + ch;
            if (!src.HasVertexAttribute(attribute)) continue;
            int dimension = src.GetVertexAttributeDimension(attribute);
            if (dimension <= 2)
            {
                var uvs = new List<Vector2>();
                src.GetUVs(ch, uvs);
                m.SetUVs(ch, Remap(uvs.ToArray(), from));
            }
            else if (dimension == 3)
            {
                var uvs = new List<Vector3>();
                src.GetUVs(ch, uvs);
                m.SetUVs(ch, Remap(uvs.ToArray(), from));
            }
            else
            {
                var uvs = new List<Vector4>();
                src.GetUVs(ch, uvs);
                m.SetUVs(ch, Remap(uvs.ToArray(), from));
            }
        }
        if (layout.uv != null) m.SetUVs(1, layout.uv);

        // Skin: every bone weight of the vertex it came from.
        var perVertex = src.GetBonesPerVertex();
        if (perVertex.Length == src.vertexCount)
        {
            var weights = src.GetAllBoneWeights();
            var first = new int[src.vertexCount];
            for (int v = 0, at = 0; v < first.Length; v++)
            {
                first[v] = at;
                at += perVertex[v];
            }
            var newPerVertex = new NativeArray<byte>(from.Length, Allocator.Temp);
            var newWeights = new List<BoneWeight1>(weights.Length);
            for (int r = 0; r < from.Length; r++)
            {
                int v = from[r];
                newPerVertex[r] = perVertex[v];
                for (int j = 0; j < perVertex[v]; j++) newWeights.Add(weights[first[v] + j]);
            }
            var weightArray = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp);
            m.SetBoneWeights(newPerVertex, weightArray);
            newPerVertex.Dispose();
            weightArray.Dispose();
        }
        m.bindposes = src.bindposes;

        int n = src.vertexCount;
        var dv = new Vector3[n];
        var dn = new Vector3[n];
        var dt = new Vector3[n];
        for (int s = 0; s < src.blendShapeCount; s++)
            for (int f = 0; f < src.GetBlendShapeFrameCount(s); f++)
            {
                src.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                m.AddBlendShapeFrame(src.GetBlendShapeName(s), src.GetBlendShapeFrameWeight(s, f),
                    Remap(dv, from), Remap(dn, from), Remap(dt, from));
            }

        m.subMeshCount = 1;
        m.SetTriangles(layout.corner, 0, false);
        m.bounds = src.bounds;
        return m;
    }

    static T[] Remap<T>(T[] values, int[] from)
    {
        var result = new T[from.Length];
        if (values == null || values.Length == 0) return result;
        for (int i = 0; i < from.Length; i++) result[i] = values[from[i]];
        return result;
    }

    // ─────────────────────────────────────────── Subdivision
    // The replicant smoothed the way Blender's Subdivision Surface does it (Catmull-Clark), while
    // Subdivision is ticked. Built on top of the layout: positions are smoothed over the welded
    // shape, so group edges and UV seams never open, and creased lines stay sharp. Every other
    // per-vertex value — the place on the map above all — is mixed in a straight line from the
    // corners of the face the vertex came from, so each piece of a face shows that face's own part
    // of the map (the bake is unchanged). Faces are still picked on the unsmoothed shape; the
    // triangles each one became are kept, so Transparency and Hide carry over to its pieces.

    class Subdivided
    {
        public long topology, key;   // the layout and the SubdivisionKey it was built for
        public int vertexCount;
        public int[] triStart;       // original face -> its first corner in tris; triStart[t + 1] ends it
        public int[] tris;           // the smoothed triangles, grouped by the original face they came from
        public int[] from;           // smoothed vertex × 4 -> the unsmoothed replicant vertices it mixes
        public float[] weight;       //   and how much of each
    }

    [System.NonSerialized] Subdivided _subdiv;

    // 0 while Subdivision is off; else a hash of everything the smoothing depends on beyond the layout.
    long SubdivisionKey(out Dictionary<long, float> creases)
    {
        creases = null;
        if (_data == null || !_data.subdivision) return 0;
        creases = CreaseEdges();
        unchecked
        {
            long key = (long)14695981039346656037UL;
            void Mix(long v) { key = (key ^ v) * 1099511628211L; }
            Mix(Mathf.Clamp(_data.subdivisionLevels, 1, 2));
            foreach (var kv in creases)
            {
                Mix(kv.Key);
                Mix(System.BitConverter.SingleToInt32Bits(kv.Value));
            }
            return key == 0 ? 1 : key;
        }
    }

    // The smoothing as built for the replicant as it is now, or null when it's the plain one.
    Subdivided DrawnSubdivision(MapLayout layout, Mesh m) =>
        _subdiv != null && _subdiv.topology == layout.topology && m != null && m.vertexCount == _subdiv.vertexCount
            ? _subdiv : null;

    // The triangles the replicant draws for face t: its own corners, or the smoothed pieces of it.
    // (A smoothed quad's pieces all go with its first triangle.)
    void AddDrawn(MapLayout layout, Mesh m, int t, List<int> into)
    {
        var s = DrawnSubdivision(layout, m);
        if (s == null)
        {
            into.Add(layout.corner[t * 3]);
            into.Add(layout.corner[t * 3 + 1]);
            into.Add(layout.corner[t * 3 + 2]);
            return;
        }
        for (int c = s.triStart[t]; c < s.triStart[t + 1]; c++) into.Add(s.tris[c]);
    }

    // The map's layout on the replicant's vertices — mixed from the corners when it's smoothed.
    Vector2[] DrawnUVs(MapLayout layout, Mesh m)
    {
        var s = DrawnSubdivision(layout, m);
        if (s == null) return layout.uv;
        var uv = new Vector2[s.vertexCount];
        for (int v = 0; v < uv.Length; v++)
            for (int k = 0; k < 4; k++)
                uv[v] += layout.uv[s.from[v * 4 + k]] * s.weight[v * 4 + k];
        return uv;
    }

    // Which group each face is in (-1 for none).
    int[] FaceGroups()
    {
        var groupOf = new int[TriCount];
        for (int t = 0; t < groupOf.Length; t++) groupOf[t] = -1;
        for (int g = 0; g < _data.fills.Count; g++)
            foreach (int t in _data.fills[g].triangles)
                if (t >= 0 && t < groupOf.Length) groupOf[t] = g;
        return groupOf;
    }

    // One round of smoothing's mesh. Points are welded positions (what gets smoothed); corners are
    // vertices as the mesh splits them (per group, per UV piece), each standing on a point.
    class SubdivLevel
    {
        public int pointCount;
        public int[] cornerPoint;
        public List<int[]> faces;              // corners, in winding order
        public List<int> faceOf;               // the original face (triangle) each came from
        public Dictionary<long, float> crease; // by EdgeKey of two points
    }

    // The coarse replicant (BuildLayoutMesh) smoothed `levels` times; fills _subdiv.
    Mesh Subdivide(Mesh coarse, MapLayout layout, int levels, Dictionary<long, float> creases)
    {
        int corners0 = coarse.vertexCount;
        var level = new SubdivLevel
        {
            pointCount = _weldCount,
            cornerPoint = new int[corners0],
            faces = new List<int[]>(TriCount),
            faceOf = new List<int>(TriCount),
            crease = creases ?? new Dictionary<long, float>(),
        };
        for (int r = 0; r < corners0; r++) level.cornerPoint[r] = _weld[layout.source[r]];

        // Faces as Blender has them: a quad's two halves are one face, unless they're in different
        // groups or the diagonal is a seam (then each half is smoothed as a triangle).
        int[] groupOf = FaceGroups();
        var corner = layout.corner;
        var done = new bool[TriCount];
        for (int t = 0; t < TriCount; t++)
        {
            if (done[t]) continue;
            done[t] = true;
            int[] face = { corner[t * 3], corner[t * 3 + 1], corner[t * 3 + 2] };
            int p = _partner[t];
            if (p >= 0 && !done[p] && groupOf[p] == groupOf[t] &&
                QuadOf(face, new[] { corner[p * 3], corner[p * 3 + 1], corner[p * 3 + 2] }, out int[] quad))
            {
                face = quad;
                done[p] = true;
            }
            bool degenerate = false;
            for (int i = 0; i < face.Length && !degenerate; i++)
                for (int j = i + 1; j < face.Length; j++)
                    if (level.cornerPoint[face[i]] == level.cornerPoint[face[j]]) { degenerate = true; break; }
            if (degenerate) continue;   // no area — left out
            level.faces.Add(face);
            level.faceOf.Add(t);
        }

        // Per point: position, skin and blend shapes (from any corner standing on it).
        var verts = coarse.vertices;
        var pos = new Vector3[level.pointCount];
        var anyCorner = new int[level.pointCount];
        for (int i = 0; i < anyCorner.Length; i++) anyCorner[i] = -1;
        for (int r = 0; r < corners0; r++)
        {
            int pt = level.cornerPoint[r];
            if (anyCorner[pt] >= 0) continue;
            anyCorner[pt] = r;
            pos[pt] = verts[r];
        }

        var perVertex = coarse.GetBonesPerVertex();
        var allWeights = coarse.GetAllBoneWeights();
        bool skinned = perVertex.Length == corners0 && allWeights.Length > 0;
        int maxBones = 0;
        int[][] bone = null;
        float[][] boneW = null;
        if (skinned)
        {
            var first = new int[corners0];
            for (int v = 0, at = 0; v < corners0; v++)
            {
                first[v] = at;
                at += perVertex[v];
                maxBones = Mathf.Max(maxBones, perVertex[v]);
            }
            bone = new int[level.pointCount][];
            boneW = new float[level.pointCount][];
            for (int pt = 0; pt < level.pointCount; pt++)
            {
                int r = anyCorner[pt];
                int n = r >= 0 ? perVertex[r] : 0;
                bone[pt] = new int[n];
                boneW[pt] = new float[n];
                for (int j = 0; j < n; j++)
                {
                    bone[pt][j] = allWeights[first[r] + j].boneIndex;
                    boneW[pt][j] = allWeights[first[r] + j].weight;
                }
            }
        }

        // Blend shapes: each frame's vertex/normal/tangent offsets, per point.
        var shapeFrames = new List<(string name, float weight, Vector3[] dv, Vector3[] dn, Vector3[] dt)>();
        if (coarse.blendShapeCount > 0)
        {
            var dv = new Vector3[corners0];
            var dn = new Vector3[corners0];
            var dt = new Vector3[corners0];
            for (int s = 0; s < coarse.blendShapeCount; s++)
                for (int f = 0; f < coarse.GetBlendShapeFrameCount(s); f++)
                {
                    coarse.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    var pv = new Vector3[level.pointCount];
                    var pn = new Vector3[level.pointCount];
                    var pt3 = new Vector3[level.pointCount];
                    for (int pt = 0; pt < level.pointCount; pt++)
                    {
                        int r = anyCorner[pt];
                        if (r < 0) continue;
                        pv[pt] = dv[r];
                        pn[pt] = dn[r];
                        pt3[pt] = dt[r];
                    }
                    shapeFrames.Add((coarse.GetBlendShapeName(s), coarse.GetBlendShapeFrameWeight(s, f), pv, pn, pt3));
                }
        }

        // Per corner: which unsmoothed replicant vertices it mixes (at most a face's four corners).
        var mixFrom = new int[corners0][];
        var mixW = new float[corners0][];
        for (int r = 0; r < corners0; r++)
        {
            mixFrom[r] = new[] { r };
            mixW[r] = new[] { 1f };
        }

        for (int round = 0; round < levels; round++)
        {
            var next = SubdivideOnce(level, out int[][] ruleFrom, out float[][] ruleW, out int[][] cornerFrom);

            var newPos = new Vector3[next.pointCount];
            for (int pt = 0; pt < newPos.Length; pt++)
                for (int k = 0; k < ruleFrom[pt].Length; k++) newPos[pt] += pos[ruleFrom[pt][k]] * ruleW[pt][k];
            pos = newPos;

            if (skinned)
            {
                var newBone = new int[next.pointCount][];
                var newBoneW = new float[next.pointCount][];
                var sum = new Dictionary<int, float>();
                for (int pt = 0; pt < next.pointCount; pt++)
                {
                    sum.Clear();
                    for (int k = 0; k < ruleFrom[pt].Length; k++)
                    {
                        int o = ruleFrom[pt][k];
                        float w = ruleW[pt][k];
                        for (int j = 0; j < bone[o].Length; j++)
                        {
                            sum.TryGetValue(bone[o][j], out float had);
                            sum[bone[o][j]] = had + boneW[o][j] * w;
                        }
                    }
                    newBone[pt] = new int[sum.Count];
                    newBoneW[pt] = new float[sum.Count];
                    int at = 0;
                    foreach (var kv in sum)
                    {
                        newBone[pt][at] = kv.Key;
                        newBoneW[pt][at++] = kv.Value;
                    }
                }
                bone = newBone;
                boneW = newBoneW;
            }

            for (int s = 0; s < shapeFrames.Count; s++)
            {
                var (name, weight, dv, dn, dt) = shapeFrames[s];
                var nv = new Vector3[next.pointCount];
                var nn = new Vector3[next.pointCount];
                var nt = new Vector3[next.pointCount];
                for (int pt = 0; pt < next.pointCount; pt++)
                    for (int k = 0; k < ruleFrom[pt].Length; k++)
                    {
                        int o = ruleFrom[pt][k];
                        float w = ruleW[pt][k];
                        nv[pt] += dv[o] * w;
                        nn[pt] += dn[o] * w;
                        nt[pt] += dt[o] * w;
                    }
                shapeFrames[s] = (name, weight, nv, nn, nt);
            }

            var newFrom = new int[cornerFrom.Length][];
            var newW = new float[cornerFrom.Length][];
            var idx = new List<int>(8);
            var wts = new List<float>(8);
            for (int c = 0; c < cornerFrom.Length; c++)
            {
                idx.Clear();
                wts.Clear();
                float share = 1f / cornerFrom[c].Length;
                foreach (int o in cornerFrom[c])
                    for (int k = 0; k < mixFrom[o].Length; k++)
                    {
                        int at = idx.IndexOf(mixFrom[o][k]);
                        if (at < 0) { idx.Add(mixFrom[o][k]); wts.Add(mixW[o][k] * share); }
                        else wts[at] += mixW[o][k] * share;
                    }
                newFrom[c] = idx.ToArray();
                newW[c] = wts.ToArray();
            }
            mixFrom = newFrom;
            mixW = newW;
            level = next;
        }

        // The smoothed mesh.
        int count = level.cornerPoint.Length;
        var m = new Mesh { indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        var vertices = new Vector3[count];
        for (int v = 0; v < count; v++) vertices[v] = pos[level.cornerPoint[v]];
        m.vertices = vertices;

        var result = new Subdivided { vertexCount = count, from = new int[count * 4], weight = new float[count * 4] };
        for (int v = 0; v < count; v++)
        {
            // Never more than four: every piece stays inside one face of the original.
            int n = Mathf.Min(4, mixFrom[v].Length);
            float total = 0f;
            for (int k = 0; k < n; k++) total += mixW[v][k];
            for (int k = 0; k < n; k++)
            {
                result.from[v * 4 + k] = mixFrom[v][k];
                result.weight[v * 4 + k] = total > 0f ? mixW[v][k] / total : 0f;
            }
        }

        if (coarse.HasVertexAttribute(VertexAttribute.Color))
        {
            var src = coarse.colors;
            var colours = new Color[count];
            for (int v = 0; v < count; v++)
                for (int k = 0; k < 4; k++) colours[v] += src[result.from[v * 4 + k]] * result.weight[v * 4 + k];
            m.colors = colours;
        }
        for (int ch = 0; ch < 8; ch++)
        {
            var attribute = VertexAttribute.TexCoord0 + ch;
            if (!coarse.HasVertexAttribute(attribute)) continue;
            int dimension = coarse.GetVertexAttributeDimension(attribute);
            var src = new List<Vector4>();
            coarse.GetUVs(ch, src);
            var mixed = new Vector4[count];
            for (int v = 0; v < count; v++)
                for (int k = 0; k < 4; k++) mixed[v] += src[result.from[v * 4 + k]] * result.weight[v * 4 + k];
            if (dimension <= 2) m.SetUVs(ch, System.Array.ConvertAll(mixed, u => (Vector2)u));
            else if (dimension == 3) m.SetUVs(ch, System.Array.ConvertAll(mixed, u => (Vector3)u));
            else m.SetUVs(ch, mixed);
        }

        if (skinned)
        {
            // Each vertex keeps its strongest bones (as many as the original's most), strongest first.
            var newPerVertex = new NativeArray<byte>(count, Allocator.Temp);
            var weights = new List<BoneWeight1>(count * maxBones);
            var order = new List<int>(16);
            for (int v = 0; v < count; v++)
            {
                int pt = level.cornerPoint[v];
                order.Clear();
                for (int j = 0; j < bone[pt].Length; j++) order.Add(j);
                var bw = boneW[pt];
                order.Sort((a, b) => bw[b].CompareTo(bw[a]));
                int n = Mathf.Min(maxBones, order.Count);
                float total = 0f;
                for (int j = 0; j < n; j++) total += bw[order[j]];
                for (int j = 0; j < n; j++)
                    weights.Add(new BoneWeight1 { boneIndex = bone[pt][order[j]], weight = total > 0f ? bw[order[j]] / total : 0f });
                newPerVertex[v] = (byte)n;
            }
            var weightArray = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp);
            m.SetBoneWeights(newPerVertex, weightArray);
            newPerVertex.Dispose();
            weightArray.Dispose();
        }
        m.bindposes = coarse.bindposes;

        foreach (var (name, weight, dv, dn, dt) in shapeFrames)
        {
            var fv = new Vector3[count];
            var fn = new Vector3[count];
            var ft = new Vector3[count];
            for (int v = 0; v < count; v++)
            {
                int pt = level.cornerPoint[v];
                fv[v] = dv[pt];
                fn[v] = dn[pt];
                ft[v] = dt[pt];
            }
            m.AddBlendShapeFrame(name, weight, fv, fn, ft);
        }

        // Triangles, grouped by the original face they came from; each quad cut along its shorter diagonal.
        result.triStart = new int[TriCount + 1];
        foreach (int t in level.faceOf) result.triStart[t + 1] += 6;
        for (int t = 0; t < TriCount; t++) result.triStart[t + 1] += result.triStart[t];
        result.tris = new int[result.triStart[TriCount]];
        var fill = (int[])result.triStart.Clone();
        for (int f = 0; f < level.faces.Count; f++)
        {
            var q = level.faces[f];   // every face is a quad after one round
            int at = fill[level.faceOf[f]];
            fill[level.faceOf[f]] += 6;
            bool cutAC = (vertices[q[0]] - vertices[q[2]]).sqrMagnitude <= (vertices[q[1]] - vertices[q[3]]).sqrMagnitude;
            int[] cut = cutAC
                ? new[] { q[0], q[1], q[2], q[0], q[2], q[3] }
                : new[] { q[0], q[1], q[3], q[1], q[2], q[3] };
            System.Array.Copy(cut, 0, result.tris, at, 6);
        }
        m.subMeshCount = 1;
        m.SetTriangles(result.tris, 0, false);

        // Normals: smooth across every welded point, as Blender's Shade Smooth shows a subdivided model.
        var pointNormal = new Vector3[level.pointCount];
        for (int c = 0; c < result.tris.Length; c += 3)
        {
            int a = result.tris[c], b = result.tris[c + 1], d = result.tris[c + 2];
            Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[d] - vertices[a]);
            pointNormal[level.cornerPoint[a]] += n;
            pointNormal[level.cornerPoint[b]] += n;
            pointNormal[level.cornerPoint[d]] += n;
        }
        var normals = new Vector3[count];
        for (int v = 0; v < count; v++) normals[v] = Unit(pointNormal[level.cornerPoint[v]]);
        m.normals = normals;
        if (coarse.HasVertexAttribute(VertexAttribute.Tangent) && coarse.HasVertexAttribute(VertexAttribute.TexCoord0))
            m.RecalculateTangents();
        m.bounds = coarse.bounds;

        _subdiv = result;
        return m;
    }

    // The quad two triangles make (their shared side its diagonal), in the first one's winding.
    static bool QuadOf(int[] tri, int[] other, out int[] quad)
    {
        quad = null;
        for (int k = 0; k < 3; k++)
        {
            int a = tri[k], b = tri[(k + 1) % 3], c = tri[(k + 2) % 3];
            if (System.Array.IndexOf(other, a) >= 0 || System.Array.IndexOf(other, b) < 0 || System.Array.IndexOf(other, c) < 0)
                continue;
            int d = -1;
            foreach (int o in other)
                if (o != b && o != c) d = o;
            if (d < 0) return false;
            quad = new[] { a, b, d, c };
            return true;
        }
        return false;   // the halves don't share both diagonal corners — a seam runs along it
    }

    // One round of Catmull-Clark. New points: the old ones (same numbers), one per side, one per face.
    // ruleFrom/ruleW: each new point as a mix of old points. cornerFrom: each new corner as an equal
    // mix of old corners (straight across the face, for the map and the rest).
    static SubdivLevel SubdivideOnce(SubdivLevel level, out int[][] ruleFrom, out float[][] ruleW, out int[][] cornerFrom)
    {
        int points = level.pointCount;
        int faceCount = level.faces.Count;
        int[] cp = level.cornerPoint;

        // Sides between points, with the faces either side.
        var sideOf = new Dictionary<long, int>();
        var sideA = new List<int>();
        var sideB = new List<int>();
        var sideFaces = new List<List<int>>();
        var pointSides = new List<int>[points];
        var pointFaces = new List<int>[points];
        for (int f = 0; f < faceCount; f++)
        {
            var face = level.faces[f];
            for (int i = 0; i < face.Length; i++)
            {
                int a = cp[face[i]], b = cp[face[(i + 1) % face.Length]];
                long key = EdgeKey(a, b);
                if (!sideOf.TryGetValue(key, out int s))
                {
                    s = sideA.Count;
                    sideOf[key] = s;
                    sideA.Add(a);
                    sideB.Add(b);
                    sideFaces.Add(new List<int>(2));
                    (pointSides[a] ??= new List<int>()).Add(s);
                    (pointSides[b] ??= new List<int>()).Add(s);
                }
                sideFaces[s].Add(f);
                (pointFaces[a] ??= new List<int>()).Add(f);
            }
        }
        int sides = sideA.Count;

        // How sharp a side is: the mesh's open edge (and anything stranger than two faces) fully.
        float Sharp(int s) =>
            sideFaces[s].Count != 2 ? 1f : level.crease.TryGetValue(EdgeKey(sideA[s], sideB[s]), out float c) ? c : 0f;

        ruleFrom = new int[points + sides + faceCount][];
        ruleW = new float[ruleFrom.Length][];
        var acc = new Dictionary<int, float>();
        void Add(int p, float w)
        {
            acc.TryGetValue(p, out float had);
            acc[p] = had + w;
        }
        void AddFace(int f, float w)
        {
            var face = level.faces[f];
            foreach (int c in face) Add(cp[c], w / face.Length);
        }
        void Take(int p, int[][] from, float[][] wts)
        {
            from[p] = new int[acc.Count];
            wts[p] = new float[acc.Count];
            int at = 0;
            foreach (var kv in acc)
            {
                from[p][at] = kv.Key;
                wts[p][at++] = kv.Value;
            }
            acc.Clear();
        }

        // Face points: the middle of the face.
        for (int f = 0; f < faceCount; f++)
        {
            AddFace(f, 1f);
            Take(points + sides + f, ruleFrom, ruleW);
        }

        // Side points: the ends and the two faces' middles; on a crease, that much of the plain midpoint.
        for (int s = 0; s < sides; s++)
        {
            float c = Sharp(s);
            Add(sideA[s], 0.5f * c);
            Add(sideB[s], 0.5f * c);
            if (c < 1f)
            {
                float smooth = 1f - c;
                Add(sideA[s], 0.25f * smooth);
                Add(sideB[s], 0.25f * smooth);
                AddFace(sideFaces[s][0], 0.25f * smooth);
                AddFace(sideFaces[s][1], 0.25f * smooth);
            }
            Take(points + s, ruleFrom, ruleW);
        }

        // Old points move: (Q + 2R + (n − 3)S) / n; along a crease (two creased sides) towards the
        // crease's own curve, at a corner of three or more creases towards staying put. The mesh's
        // open edge keeps its line and its corners stay put (Blender's Keep Corners).
        for (int p = 0; p < points; p++)
        {
            var around = pointSides[p];
            if (around == null)
            {
                Add(p, 1f);
                Take(p, ruleFrom, ruleW);
                continue;
            }
            int Other(int s) => sideA[s] == p ? sideB[s] : sideA[s];

            int open = 0;
            foreach (int s in around) if (sideFaces[s].Count != 2) open++;
            if (open > 0)
            {
                if (open == 2 && pointFaces[p].Count > 1)
                {
                    Add(p, 0.75f);
                    foreach (int s in around)
                        if (sideFaces[s].Count != 2) Add(Other(s), 0.125f);
                }
                else Add(p, 1f);
                Take(p, ruleFrom, ruleW);
                continue;
            }

            int n = around.Count;
            if (n < 3)
            {
                Add(p, 1f);
                Take(p, ruleFrom, ruleW);
                continue;
            }
            int creased = 0, firstCrease = -1, secondCrease = -1;
            float creaseSum = 0f;
            foreach (int s in around)
            {
                float c = Sharp(s);
                if (c <= 0f) continue;
                creased++;
                creaseSum += c;
                if (firstCrease < 0) firstCrease = s;
                else if (secondCrease < 0) secondCrease = s;
            }
            float sharp = creased >= 2 ? creaseSum / creased : 0f;
            float smoothPart = 1f - sharp;
            if (smoothPart > 0f)
            {
                var faces = pointFaces[p];
                foreach (int f in faces) AddFace(f, smoothPart / (n * faces.Count));
                foreach (int s in around)
                {
                    Add(p, smoothPart / (n * n));
                    Add(Other(s), smoothPart / (n * n));
                }
                Add(p, smoothPart * (n - 3f) / n);
            }
            if (sharp > 0f)
            {
                if (creased == 2)
                {
                    Add(p, sharp * 0.75f);
                    Add(Other(firstCrease), sharp * 0.125f);
                    Add(Other(secondCrease), sharp * 0.125f);
                }
                else Add(p, sharp);
            }
            Take(p, ruleFrom, ruleW);
        }

        // Corners: every old one (same number), one per face side between two corners — shared by the
        // faces either side unless a seam splits them — and one in the middle of each face.
        var cornerList = new List<int[]>(cp.Length * 4);
        var cornerPoints = new List<int>(cp.Length * 4);
        for (int c = 0; c < cp.Length; c++)
        {
            cornerList.Add(new[] { c });
            cornerPoints.Add(cp[c]);
        }
        var sideCorner = new Dictionary<long, int>();
        int SideCorner(int ca, int cb)
        {
            long key = EdgeKey(ca, cb);
            if (sideCorner.TryGetValue(key, out int k)) return k;
            k = cornerList.Count;
            sideCorner[key] = k;
            cornerList.Add(new[] { ca, cb });
            cornerPoints.Add(points + sideOf[EdgeKey(cp[ca], cp[cb])]);
            return k;
        }
        var nextFaces = new List<int[]>(faceCount * 4);
        var nextFaceOf = new List<int>(faceCount * 4);
        for (int f = 0; f < faceCount; f++)
        {
            var face = level.faces[f];
            int k = face.Length;
            int middle = cornerList.Count;
            cornerList.Add((int[])face.Clone());
            cornerPoints.Add(points + sides + f);
            for (int i = 0; i < k; i++)
            {
                int prev = face[(i + k - 1) % k], here = face[i], after = face[(i + 1) % k];
                nextFaces.Add(new[] { here, SideCorner(here, after), middle, SideCorner(prev, here) });
                nextFaceOf.Add(level.faceOf[f]);
            }
        }
        cornerFrom = cornerList.ToArray();

        // A creased side's two halves keep its crease.
        var crease = new Dictionary<long, float>();
        foreach (var kv in level.crease)
        {
            if (!sideOf.TryGetValue(kv.Key, out int s)) continue;
            crease[EdgeKey(sideA[s], points + s)] = kv.Value;
            crease[EdgeKey(points + s, sideB[s])] = kv.Value;
        }

        return new SubdivLevel
        {
            pointCount = points + sides + faceCount,
            cornerPoint = cornerPoints.ToArray(),
            faces = nextFaces,
            faceOf = nextFaceOf,
            crease = crease,
        };
    }

    // ─────────────────────────────────────────── Hide

    // While a group is hidden, the stage copy wears a throwaway copy of the replicant with that
    // group's faces left out — same vertices, same submeshes. Never saved: the replicant asset, the
    // bake and the real model are untouched.
    Mesh _hideMesh;
    string _hideKey;

    void ApplyHide()
    {
        if (!WearsReplicant(_target) || !EnsureCache()) return;
        var replicant = _data.replicant;
        var hidden = TrianglesOf(g => g.hidden);
        if (hidden.Count == 0)
        {
            if (MeshOf(_target) != replicant) SetMesh(_target, replicant);
            DropHideMesh();
            return;
        }

        var seeThrough = SeeThroughTriangles();
        var hiddenSorted = new SortedSet<int>(hidden);
        var layout = Layout();
        string key = string.Join(",", hiddenSorted) + "|" + string.Join(",", TransparencySlots()) + "|" + layout.version +
                     "|" + _replicantBuilds;
        // Bake and Open in Stage put the replicant itself back on, so the copy is remade then.
        if (key == _hideKey && _hideMesh != null && MeshOf(_target) == _hideMesh) return;
        _hideKey = key;

        var opaque = new List<int>(layout.corner.Length);
        var clear = new List<int>(seeThrough.Count * 3);
        for (int t = 0; t < TriCount; t++)
        {
            if (hidden.Contains(t)) continue;
            AddDrawn(layout, replicant, t, seeThrough.Contains(t) ? clear : opaque);
        }

        var old = _hideMesh;
        _hideMesh = Instantiate(replicant);
        _hideMesh.name = replicant.name + " (hidden groups out)";
        _hideMesh.hideFlags = HideFlags.HideAndDontSave;
        var bounds = replicant.bounds;
        _hideMesh.subMeshCount = replicant.subMeshCount;
        _hideMesh.SetTriangles(opaque, 0, false);
        if (_hideMesh.subMeshCount > 1) _hideMesh.SetTriangles(clear, 1, false);
        _hideMesh.bounds = bounds;
        SetMesh(_target, _hideMesh);
        if (old) DestroyImmediate(old);
        if (_target.sharedMaterials.Length != _hideMesh.subMeshCount) DressSlots(_target);
        SceneView.RepaintAll();
    }

    // Back into the replicant itself (when the copy is still wearing the hide copy) and the hide copy gone.
    void DropHideMesh()
    {
        if (_hideMesh == null) return;
        if (_target != null && MeshOf(_target) == _hideMesh && _data != null && _data.replicant != null)
            SetMesh(_target, _data.replicant);
        DestroyImmediate(_hideMesh);
        _hideMesh = null;
        _hideKey = null;
    }

    // Opaque faces in submesh 0; Transparency groups' faces in submesh 1, only when there are any — and
    // UV2.x, on every vertex, its place in the list of Transparency groups (-1 for the rest), which picks
    // the group's "<name>_Transparency" on the transparent material.
    void SplitReplicant(Mesh replicant)
    {
        if (replicant == null || !EnsureCache()) return;
        var seeThrough = SeeThroughTriangles();
        var layout = Layout();
        int[] slotOf = TransparencySlots();
        string key = string.Join(",", slotOf) + "|" + layout.topology;
        if (key == _splitKey && replicant.subMeshCount == (seeThrough.Count > 0 ? 2 : 1)) return;
        _splitKey = key;

        // Each vertex belongs to one group (the layout splits them per group, and a smoothed vertex
        // comes from one face), so its slot is exact.
        var slots = new Vector2[replicant.vertexCount];
        for (int v = 0; v < slots.Length; v++) slots[v] = new Vector2(-1f, 0f);
        var opaque = new List<int>(layout.corner.Length);
        var clear = new List<int>(seeThrough.Count * 3);
        for (int t = 0; t < TriCount; t++)
        {
            var into = seeThrough.Contains(t) ? clear : opaque;
            int from = into.Count;
            AddDrawn(layout, replicant, t, into);
            for (int i = from; i < into.Count; i++)
                if (into[i] < slots.Length) slots[into[i]] = new Vector2(slotOf[t], 0f);
        }
        var bounds = replicant.bounds;
        replicant.subMeshCount = clear.Count > 0 ? 2 : 1;
        replicant.SetTriangles(opaque, 0, false);
        if (clear.Count > 0) replicant.SetTriangles(clear, 1, false);
        replicant.SetUVs(2, slots);
        replicant.bounds = bounds;
        EditorUtility.SetDirty(replicant);

        if (TargetInStage && (MeshOf(_target) == replicant || WearsReplicant(_target)) &&
            _target.sharedMaterials.Length != replicant.subMeshCount)
            DressSlots(_target);
    }

    // Triangles of every Transparency group, in order.
    SortedSet<int> SeeThroughTriangles()
    {
        var set = new SortedSet<int>();
        if (_data == null) return set;
        foreach (var g in _data.fills)
            if (g.Transparent)
                foreach (int t in g.triangles)
                    if (t >= 0 && t < TriCount) set.Add(t);
        return set;
    }

    // ─────────────────────────────────────────── Transparency

    // Every face's place in the list of Transparency groups (see TransparencyProps), -1 for the rest.
    int[] TransparencySlots()
    {
        var slotOf = new int[TriCount];
        for (int t = 0; t < slotOf.Length; t++) slotOf[t] = -1;
        if (_data == null) return slotOf;
        int slot = 0;
        foreach (var g in _data.fills)
        {
            if (!g.Transparent) continue;
            foreach (int t in g.triangles)
                if (t >= 0 && t < slotOf.Length) slotOf[t] = slot;
            slot++;
        }
        return slotOf;
    }

    struct TransparencyProp
    {
        public ModelTexturingData.Fill group;
        public string reference;   // "_FaceWindow_Transparency" — what the material stores
        public string label;       // "FaceWindow_Transparency" — what the material shows
    }

    // One property per Transparency group, in list order (the order of TransparencySlots).
    List<TransparencyProp> TransparencyProps()
    {
        var props = new List<TransparencyProp>();
        if (_data == null) return props;
        var used = new HashSet<string>();
        foreach (var g in _data.fills)
        {
            if (!g.Transparent) continue;
            string label = TransparencyLabel(g);
            string reference = "_" + Identifier(label);
            if (!used.Add(reference))
            {
                // Two groups with the same name: the second gets its place in the list on the end.
                label += "_" + (props.Count + 1);
                reference = "_" + Identifier(label);
                used.Add(reference);
            }
            props.Add(new TransparencyProp { group = g, reference = reference, label = label });
        }
        return props;
    }

    static string TransparencyLabel(ModelTexturingData.Fill g) =>
        (string.IsNullOrWhiteSpace(g.name) ? "Group" : g.name.Trim()) + "_Transparency";

    static string Identifier(string s)
    {
        var b = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) b.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
        return b.ToString();
    }

    // The transparent material, when it's this data's own (at its path) — else null.
    Material OwnTransparentMaterial()
    {
        var mat = _data != null ? _data.transparentMaterial : null;
        return mat != null && AssetDatabase.GetAssetPath(mat) == MaterialPath(true) ? mat : null;
    }

    string ReferenceOf(ModelTexturingData.Fill g)
    {
        foreach (var p in TransparencyProps())
            if (p.group == g) return p.reference;
        return null;
    }

    // What the group's slider shows: the material's value once the material has the property (it can
    // be set there too), else the group's own.
    float TransparencyOf(ModelTexturingData.Fill g)
    {
        var mat = OwnTransparentMaterial();
        string reference = ReferenceOf(g);
        return mat != null && reference != null && mat.HasProperty(reference) ? mat.GetFloat(reference) : g.Transparency;
    }

    // The slider writes both — no bake needed, the material draws it straight away.
    void SetTransparency(ModelTexturingData.Fill g, float amount)
    {
        var mat = OwnTransparentMaterial();
        string reference = ReferenceOf(g);
        bool onMaterial = mat != null && reference != null && mat.HasProperty(reference);
        Undo.RecordObjects(onMaterial ? new Object[] { _data, mat } : new Object[] { _data }, "Group transparency");
        g.colour.a = amount;
        EditorUtility.SetDirty(_data);
        if (onMaterial)
        {
            mat.SetFloat(reference, amount);
            EditorUtility.SetDirty(mat);
        }
        SceneView.RepaintAll();
    }

    string TransparentGraphPath => Path.ChangeExtension(MaterialPath(true), ".shadergraph");

    // What the model's graph was last written with: the key, and each property's group and name, so a
    // renamed group's value can be read off the material under its old name.
    [System.NonSerialized] string _graphKey;
    [System.NonSerialized] List<TransparencyProp> _graphProps;

    // The model's own transparent graph, "<title>Transparent.shadergraph" next to the data: a copy of
    // AngelMaterialTransparent with a "<group>_Transparency" slider per Transparency group, and the
    // Alpha going through a Custom Function that picks the face's one by UV2.x (see SplitReplicant):
    // Alpha × (1 − transparency). Rewritten only when the groups or their names change (the file is
    // only touched when its text does), then the transparent material is moved onto it and given every
    // group's value.
    Shader EnsureTransparentGraph()
    {
        if (_data == null || MaterialTitle.Length == 0) return null;
        string path = TransparentGraphPath;
        var props = TransparencyProps();
        var key = new System.Text.StringBuilder(path);
        foreach (var p in props) key.Append('|').Append(p.reference).Append(':').Append(p.label);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (key.ToString() == _graphKey && shader != null) return shader;

        var template = FindGraph(TransparentGraph);
        string templatePath = template != null ? AssetDatabase.GetAssetPath(template) : null;
        if (templatePath == null)
        {
            Debug.LogWarning($"Model Texturing Tool: no {TransparentGraph} shader graph found to copy.");
            return null;
        }
        if (path == templatePath) return template;   // a model titled "AngelMaterial" — never overwrite the original

        // The material's values into the groups first, under the names the graph had, so a value set
        // on the material survives the group being renamed.
        var mat = OwnTransparentMaterial();
        if (mat != null && shader != null && mat.shader == shader)
            foreach (var p in _graphProps ?? props)
                if (_data.fills.Contains(p.group) && mat.HasProperty(p.reference))
                    p.group.colour.a = mat.GetFloat(p.reference);

        string text;
        try
        {
            text = TransparentGraphText(File.ReadAllText(templatePath), props, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_data)));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Model Texturing Tool: couldn't build {path} from {templatePath}: {e.Message}");
            return shader;
        }
        if (text == null)
        {
            Debug.LogWarning($"Model Texturing Tool: {templatePath} has no Alpha block to wire the transparencies into.");
            return shader;
        }
        if (shader == null || !File.Exists(path) || File.ReadAllText(path).Replace("\r\n", "\n") != text)
        {
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        }
        _graphKey = key.ToString();
        _graphProps = props;

        if (mat != null && shader != null)
        {
            if (mat.shader != shader) mat.shader = shader;   // values of properties with the same name carry over
            foreach (var p in props)
                if (mat.HasProperty(p.reference)) mat.SetFloat(p.reference, p.group.Transparency);
            EditorUtility.SetDirty(mat);
        }
        EditorUtility.SetDirty(_data);
        return shader;
    }

    // The template graph with the transparency nodes added. Shader Graph files are JSON objects one
    // after another; new objects get ids hashed from the data and their role, so the same groups always
    // make the same text. Null when the template has no Alpha block.
    static string TransparentGraphText(string template, List<TransparencyProp> props, string seed)
    {
        var blocks = new List<JObject>();
        foreach (string part in System.Text.RegularExpressions.Regex.Split(template.Replace("\r\n", "\n").Trim(), @"\n\s*\n(?=\{)"))
        {
            using var reader = new JsonTextReader(new StringReader(part)) { DateParseHandling = DateParseHandling.None };
            blocks.Add(JObject.Load(reader));
        }
        JObject Find(string type, System.Func<JObject, bool> also = null)
        {
            foreach (var b in blocks)
                if ((string)b["m_Type"] == type && (also == null || also(b))) return b;
            return null;
        }
        var graph = Find("UnityEditor.ShaderGraph.GraphData");
        var category = Find("UnityEditor.ShaderGraph.CategoryData");
        var alpha = Find("UnityEditor.ShaderGraph.BlockNode", b => (string)b["m_SerializedDescriptor"] == "SurfaceDescription.Alpha");
        if (graph == null || alpha == null) return null;
        string alphaId = (string)alpha["m_ObjectId"];
        int alphaSlot = 0;
        if (alpha["m_Slots"] is JArray alphaSlots && alphaSlots.Count > 0)
        {
            foreach (var b in blocks)
                if ((string)b["m_ObjectId"] == (string)alphaSlots[0]["m_Id"]) alphaSlot = (int)b["m_Id"];
        }

        string Id(string role)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed + "|" + role));
            return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        JObject Ref(string id) => new JObject { ["m_Id"] = id };
        JObject Position(float x, float y, float w, float h) => new JObject
        {
            ["m_Expanded"] = true,
            ["m_Position"] = new JObject { ["serializedVersion"] = "2", ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h },
        };
        JObject Slot(string id, string type, int slotId, string name, bool output, JToken value)
        {
            var s = new JObject
            {
                ["m_SGVersion"] = 0,
                ["m_Type"] = "UnityEditor.ShaderGraph." + type,
                ["m_ObjectId"] = id,
                ["m_Id"] = slotId,
                ["m_DisplayName"] = name,
                ["m_SlotType"] = output ? 1 : 0,
                ["m_Hidden"] = false,
                ["m_HideConnector"] = false,
                ["m_ShaderOutputName"] = name,
                ["m_StageCapability"] = 3,
                ["m_CustomBinding"] = "",
                ["m_Value"] = value,
                ["m_DefaultValue"] = value.DeepClone(),
                ["m_Labels"] = new JArray(),
            };
            if (type == "Vector1MaterialSlot") s["m_LiteralMode"] = false;
            return s;
        }
        JObject Vector4() => new JObject { ["x"] = 0f, ["y"] = 0f, ["z"] = 0f, ["w"] = 0f };
        JObject Node(string type, string id, string name, JObject drawState, JArray slots) => new JObject
        {
            ["m_SGVersion"] = 0,
            ["m_Type"] = "UnityEditor.ShaderGraph." + type,
            ["m_ObjectId"] = id,
            ["m_Group"] = Ref(""),
            ["m_Name"] = name,
            ["m_DrawState"] = drawState,
            ["m_Slots"] = slots,
            ["synonyms"] = new JArray(),
            ["m_Precision"] = 0,
            ["m_PreviewExpanded"] = false,
            ["m_DismissedVersion"] = 0,
            ["m_PreviewMode"] = 0,
            ["m_CustomColors"] = new JObject { ["m_SerializableColors"] = new JArray() },
        };
        JObject Edge(string fromNode, int fromSlot, string toNode, int toSlot) => new JObject
        {
            ["m_OutputSlot"] = new JObject { ["m_Node"] = Ref(fromNode), ["m_SlotId"] = fromSlot },
            ["m_InputSlot"] = new JObject { ["m_Node"] = Ref(toNode), ["m_SlotId"] = toSlot },
        };

        var added = new List<JObject>();
        var nodes = (JArray)graph["m_Nodes"];
        var edges = (JArray)graph["m_Edges"];
        var properties = (JArray)graph["m_Properties"];
        var children = category?["m_ChildObjectList"] as JArray;

        // Custom Function: Out = AlphaIn × (1 − the transparency of the face's group).
        string fn = Id("function");
        var fnSlots = new JArray { Ref(Id("function.AlphaIn")), Ref(Id("function.Group")), Ref(Id("function.Out")) };
        added.Add(Slot(Id("function.AlphaIn"), "Vector1MaterialSlot", 0, "AlphaIn", false, 1f));
        added.Add(Slot(Id("function.Group"), "Vector4MaterialSlot", 1, "Group", false, Vector4()));
        added.Add(Slot(Id("function.Out"), "Vector1MaterialSlot", 2, "Out", true, 0f));
        var body = new System.Text.StringBuilder("float t = 0;\nint s = (int)round(Group.x);\n");
        for (int k = 0; k < props.Count; k++)
        {
            fnSlots.Add(Ref(Id("function.T" + k)));
            added.Add(Slot(Id("function.T" + k), "Vector1MaterialSlot", 3 + k, "T" + k, false, 0f));
            body.Append($"if (s == {k}) t = T{k};\n");
        }
        body.Append("Out = AlphaIn * (1 - saturate(t));");
        var function = Node("CustomFunctionNode", fn, "GroupTransparency (Custom Function)",
            Position(-700f, 300f, 240f, 160f + 24f * props.Count), fnSlots);
        function["m_SGVersion"] = 1;
        function["synonyms"] = new JArray("code", "HLSL");
        function["m_SourceType"] = 1;   // String
        function["m_FunctionName"] = "GroupTransparency";
        function["m_FunctionSource"] = "";
        function["m_FunctionSourceUsePragmas"] = true;
        function["m_FunctionBody"] = body.ToString();
        added.Add(function);
        nodes.Add(Ref(fn));

        // UV2: each vertex's place in the list of Transparency groups.
        string uv = Id("uv2");
        added.Add(Slot(Id("uv2.Out"), "Vector4MaterialSlot", 0, "Out", true, Vector4()));
        var uvNode = Node("UVNode", uv, "UV", Position(-1000f, 300f, 145f, 128f), new JArray { Ref(Id("uv2.Out")) });
        uvNode["m_OutputChannel"] = 2;
        added.Add(uvNode);
        nodes.Add(Ref(uv));
        edges.Add(Edge(uv, 0, fn, 1));

        for (int k = 0; k < props.Count; k++)
        {
            var p = props[k];
            string prop = Id("property." + p.reference);
            added.Add(new JObject
            {
                ["m_SGVersion"] = 1,
                ["m_Type"] = "UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty",
                ["m_ObjectId"] = prop,
                ["m_Guid"] = new JObject { ["m_GuidSerialized"] = new System.Guid(Id("guid." + p.reference)).ToString() },
                ["promotedFromAssetID"] = "",
                ["promotedFromCategoryName"] = "",
                ["promotedOrdering"] = -1,
                ["m_Name"] = p.label,
                ["m_DefaultRefNameVersion"] = 1,
                ["m_RefNameGeneratedByDisplayName"] = p.label,
                ["m_DefaultReferenceName"] = p.reference,
                ["m_OverrideReferenceName"] = p.reference,
                ["m_GeneratePropertyBlock"] = true,
                ["m_UseCustomSlotLabel"] = false,
                ["m_CustomSlotLabel"] = "",
                ["m_DismissedVersion"] = 0,
                ["m_Precision"] = 0,
                ["overrideHLSLDeclaration"] = false,
                ["hlslDeclarationOverride"] = 0,
                ["hideConnector"] = false,
                ["m_Hidden"] = false,
                ["m_PerRendererData"] = false,
                ["m_customAttributes"] = new JArray(),
                ["m_Value"] = 0f,
                ["m_FloatType"] = 1,   // Slider
                ["m_LiteralFloatMode"] = false,
                ["m_RangeValues"] = new JObject { ["x"] = 0f, ["y"] = 1f },
                ["m_SliderType"] = 0,
                ["m_SliderPower"] = 3f,
                ["m_EnumType"] = 0,
                ["m_CSharpEnumString"] = "",
                ["m_EnumNames"] = new JArray("Default"),
                ["m_EnumValues"] = new JArray(0),
            });
            properties.Add(Ref(prop));
            children?.Add(Ref(prop));

            string node = Id("node." + p.reference);
            string nodeSlot = Id("node.Out." + p.reference);
            added.Add(Slot(nodeSlot, "Vector1MaterialSlot", 0, p.label, true, 0f));
            var propertyNode = Node("PropertyNode", node, "Property", Position(-1000f, 450f + 40f * k, 200f, 34f), new JArray { Ref(nodeSlot) });
            propertyNode["m_Property"] = Ref(prop);
            added.Add(propertyNode);
            nodes.Add(Ref(node));
            edges.Add(Edge(node, 0, fn, 3 + k));
        }

        // Whatever fed the Alpha now feeds the function, and the function feeds the Alpha.
        foreach (var e in edges)
            if ((string)e["m_InputSlot"]["m_Node"]["m_Id"] == alphaId)
            {
                e["m_InputSlot"] = new JObject { ["m_Node"] = Ref(fn), ["m_SlotId"] = 0 };
                break;
            }
        edges.Add(Edge(fn, 2, alphaId, alphaSlot));

        blocks.AddRange(added);
        var text = new System.Text.StringBuilder();
        foreach (var b in blocks)
        {
            var sw = new StringWriter { NewLine = "\n" };
            using (var writer = new JsonTextWriter(sw) { Formatting = Formatting.Indented, Indentation = 4 })
                b.WriteTo(writer);
            text.Append(sw.ToString()).Append("\n\n");
        }
        return text.ToString();
    }

    void OnStageClosed()
    {
        DropHideMesh();
        var back = _returnTarget;
        _returnTarget = null;
        if (back != null) SetTarget(back);
        else Repaint();
    }

    // The smallest object holding the renderer AND every bone it is skinned to, so the copy keeps
    // its skeleton. A plain mesh is just its own object.
    static GameObject ModelRootOf(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr)
            for (Transform t = r.transform; t != null; t = t.parent)
                if (HoldsSkeleton(t, smr)) return t.gameObject;
        return r.gameObject;
    }

    static bool HoldsSkeleton(Transform root, SkinnedMeshRenderer smr)
    {
        if (smr.rootBone != null && !smr.rootBone.IsChildOf(root)) return false;
        foreach (var b in smr.bones)
            if (b != null && !b.IsChildOf(root)) return false;
        return true;
    }

    static ModelTexturingData FindDataFor(Mesh mesh)
    {
        if (mesh == null) return null;
        foreach (string guid in AssetDatabase.FindAssets("t:ModelTexturingData"))
        {
            var d = AssetDatabase.LoadAssetAtPath<ModelTexturingData>(AssetDatabase.GUIDToAssetPath(guid));
            if (d && (d.mesh == mesh || d.replicant == mesh)) return d;
        }
        return null;
    }

    void CreateData()
    {
        string dir = "Assets";
        string meshPath = AssetDatabase.GetAssetPath(_cachedMesh);
        if (!string.IsNullOrEmpty(meshPath)) dir = Path.GetDirectoryName(meshPath).Replace('\\', '/');
        string path = EditorUtility.SaveFilePanelInProject("New Model Texturing Data", _target.name + "ModelTexturing", "asset", "", dir);
        if (string.IsNullOrEmpty(path)) return;
        var d = CreateInstance<ModelTexturingData>();
        d.mesh = _cachedMesh;
        d.triangleCount = TriCount;
        AssetDatabase.CreateAsset(d, path);
        AssetDatabase.SaveAssets();
        _data = d;
        _expanded.Clear();
        _previewDirty = true;
        DressStageCopy();
    }

    // How long the preview waits after the last edit before it bakes (see OnInspectorUpdate).
    const double PreviewDelay = 0.35;
    double _previewDueAt;

    void MarkChanged()
    {
        if (_data) EditorUtility.SetDirty(_data);
        _previewDirty = true;
        _previewDueAt = EditorApplication.timeSinceStartup + PreviewDelay;
        SceneView.RepaintAll();
    }

    // Readies the model's import settings for the tool: Unity keeps Blender's face order (Optimize
    // Mesh off, so a reshape in Blender doesn't scramble the groups) and generates the unique UVs
    // the groups bake onto (Generate Lightmap UVs on).
    void ReplicateMesh()
    {
        if (ReordersFaces() && !KeepBlenderFaceOrder()) return;
        if (EnsureCache() && !HasUV2) GenerateUniqueUVs();
    }

    void GenerateUniqueUVs()
    {
        string path = AssetDatabase.GetAssetPath(_cachedMesh);
        if (AssetImporter.GetAtPath(path) is ModelImporter mi)
        {
            mi.generateSecondaryUV = true;
            mi.SaveAndReimport();
            _cachedMesh = null;
            _previewDirty = true;
            DressStageCopy();   // the replicant needs the new UVs too
        }
        else ShowNotification(new GUIContent("This mesh isn't from a model file"));
    }

    // The model is imported with Optimize Mesh on, which numbers its faces by their shape.
    bool ReordersFaces() =>
        _cachedMesh != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(_cachedMesh)) is ModelImporter mi &&
        mi.meshOptimizationFlags != 0;

    // Turns the model's Optimize Mesh import setting off, so Unity keeps the faces in Blender's
    // order and a reshape in Blender keeps every face's number. The reimport renumbers them once
    // (the shape hasn't changed, so each face is found again by where its corners sit), then
    // gradient lines and decals are pinned again from their rest-pose points.
    bool KeepBlenderFaceOrder()
    {
        if (!EnsureCache()) return false;
        Mesh source = _cachedMesh;
        string path = AssetDatabase.GetAssetPath(source);
        if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) return false;

        // No groups yet: nothing to find again.
        if (_data == null)
        {
            mi.meshOptimizationFlags = 0;
            mi.SaveAndReimport();
            _selection.Clear();
            _cachedMesh = null;
            _layout = null;
            _previewDirty = true;
            return true;
        }

        // Every line and decal back onto its rest-pose points, unpinned, while the numbers still hold.
        foreach (var g in _data.fills)
        {
            if (g.gradients != null)
                foreach (var gr in g.gradients)
                {
                    if (gr.lineCorners == null || gr.lineCorners.Count == 0) continue;
                    if (gr.lineCorners.TrueForAll(c => c >= 0 && c < _tris.Length))
                        gr.lines = gr.lineCorners.ConvertAll(c => _rest[_tris[c]]);
                    gr.lineCorners.Clear();
                }
            if (g.decals != null)
                foreach (var d in g.decals)
                {
                    DecalFrame(d, out d.point, out d.normal, out d.up);
                    d.triangle = -1;
                }
        }
        foreach (var cr in AllCreaseLines())
        {
            if (cr.cornerA >= 0 && cr.cornerB >= 0 && cr.cornerA < _tris.Length && cr.cornerB < _tris.Length)
            {
                cr.a = _rest[_tris[cr.cornerA]];
                cr.b = _rest[_tris[cr.cornerB]];
            }
            cr.cornerA = cr.cornerB = -1;
        }
        Vector3[] oldRest = _rest;
        int[] oldTris = _tris;
        var before = mi.meshOptimizationFlags;

        mi.meshOptimizationFlags = 0;
        mi.SaveAndReimport();
        int[] map = null;
        if (source == null) source = MeshAt(path, oldTris.Length);
        if (source != null) map = MatchFaces(oldRest, oldTris, source.vertices, source.triangles);
        if (map == null)
        {
            mi.meshOptimizationFlags = before;
            mi.SaveAndReimport();
            _cachedMesh = null;
            _layout = null;
            EditorUtility.SetDirty(_data);
            ShowNotification(new GUIContent("Couldn't find every face again — left the model as it was"));
            return false;
        }

        foreach (var g in _data.fills)
            for (int i = 0; i < g.triangles.Count; i++)
            {
                int t = g.triangles[i];
                if (t >= 0 && t < map.Length) g.triangles[i] = map[t];
            }
        _selection.Clear();
        _cachedMesh = null;
        _layout = null;
        _pinnedData = null;
        EnsureCache();   // pins the lines and decals again, on the new numbers
        EditorUtility.SetDirty(_data);
        AssetDatabase.SaveAssetIfDirty(_data);
        Undo.ClearUndo(_data);   // every earlier step is in the old numbering
        _previewDirty = true;
        Debug.Log($"[ModelTexturing] Kept Blender's face order on '{path}': {map.Length} faces renumbered, " +
                  $"{CountMoved(map)} moved");
        ShowNotification(new GUIContent("Faces now keep Blender's order"));
        return true;
    }

    static int CountMoved(int[] map)
    {
        int n = 0;
        for (int i = 0; i < map.Length; i++) if (map[i] != i) n++;
        return n;
    }

    // The model file's mesh with this many face corners, when the old reference didn't survive
    // the reimport.
    static Mesh MeshAt(string path, int indexCount)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is Mesh m && IndexCount(m) == indexCount) return m;
        return null;
    }

    // Old face number -> new, matching each face by its three corner positions (the same shape,
    // numbered differently). Null unless every face is found, each once.
    static int[] MatchFaces(Vector3[] oldRest, int[] oldTris, Vector3[] newRest, int[] newTris)
    {
        if (oldTris.Length != newTris.Length) return null;
        string Key(Vector3[] rest, int[] tris, int t)
        {
            var c = new[] { Quantise(rest[tris[t * 3]]), Quantise(rest[tris[t * 3 + 1]]), Quantise(rest[tris[t * 3 + 2]]) };
            System.Array.Sort(c, (a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
            return $"{c[0]}|{c[1]}|{c[2]}";
        }
        int count = newTris.Length / 3;
        var at = new Dictionary<string, Queue<int>>(count);
        for (int t = 0; t < count; t++)
        {
            string k = Key(newRest, newTris, t);
            if (!at.TryGetValue(k, out var q)) at[k] = q = new Queue<int>(1);
            q.Enqueue(t);
        }
        var map = new int[count];
        for (int t = 0; t < count; t++)
        {
            if (!at.TryGetValue(Key(oldRest, oldTris, t), out var q) || q.Count == 0) return null;
            map[t] = q.Dequeue();
        }
        return map;
    }

    // ─────────────────────────────────────────── Mesh cache

    bool HasUV2 => _uv2 != null && _uv2.Length == _rest.Length && _rest.Length > 0;
    int TriCount => _tris != null ? _tris.Length / 3 : 0;

    static int IndexCount(Mesh m)
    {
        long n = 0;
        for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s);
        return (int)n;
    }

    bool EnsureCache()
    {
        Mesh mesh = SourceMesh();
        if (mesh == null) { _cachedMesh = null; return false; }
        bool hasUV2 = mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
        if (mesh == _cachedMesh && mesh.vertexCount == _cachedVertexCount &&
            IndexCount(mesh) == _cachedIndexCount && hasUV2 == _cachedHasUV2)
        {
            PinOldAnchors();
            return true;
        }

        _cachedMesh = mesh;
        _cachedVertexCount = mesh.vertexCount;
        _cachedIndexCount = IndexCount(mesh);
        _cachedHasUV2 = hasUV2;
        _rest = mesh.vertices;
        _tris = mesh.triangles;
        _uv2 = hasUV2 ? mesh.uv2 : null;
        _bindposes = mesh.bindposes;
        _boneWeights = mesh.boneWeights;
        _hoverTri = -1;
        _hoverLine = -1;
        _faceTwin = _lineTwin = null;
        _selection.RemoveWhere(t => t >= TriCount);
        _lineSelection.Clear();   // lines are numbered by the mesh's edges, which may all have moved

        // Vertices are split at seams; faces are neighbours when they share a POSITION.
        var weldLookup = new Dictionary<Vector3Int, int>();
        _weld = new int[_rest.Length];
        for (int i = 0; i < _rest.Length; i++)
        {
            var k = Quantise(_rest[i]);
            if (!weldLookup.TryGetValue(k, out int id))
            {
                id = weldLookup.Count;
                weldLookup[k] = id;
            }
            _weld[i] = id;
        }
        _weldLookup = weldLookup;
        _weldCount = weldLookup.Count;

        // Quads arrive as two triangles. Each half's longest edge is the diagonal, so two
        // triangles whose longest edges are the same shared edge are one quad.
        int triCount = TriCount;
        var edgeTris = new Dictionary<long, List<int>>();
        var longest = new long[triCount];
        for (int t = 0; t < triCount; t++)
        {
            float best = -1f;
            longest[t] = -1;
            for (int e = 0; e < 3; e++)
            {
                int va = _tris[t * 3 + e], vb = _tris[t * 3 + (e + 1) % 3];
                int a = _weld[va], b = _weld[vb];
                if (a == b) continue;
                long key = EdgeKey(a, b);
                if (!edgeTris.TryGetValue(key, out var list)) edgeTris[key] = list = new List<int>(2);
                list.Add(t);
                float len = (_rest[va] - _rest[vb]).sqrMagnitude;
                if (len > best) { best = len; longest[t] = key; }
            }
        }
        _weldCorner = new int[_weldCount];
        for (int i = 0; i < _weldCount; i++) _weldCorner[i] = -1;
        for (int c = 0; c < _tris.Length; c++)
            if (_weldCorner[_weld[_tris[c]]] < 0) _weldCorner[_weld[_tris[c]]] = c;

        _partner = new int[triCount];
        for (int t = 0; t < triCount; t++)
        {
            _partner[t] = -1;
            if (longest[t] < 0 || !edgeTris.TryGetValue(longest[t], out var list) || list.Count != 2) continue;
            int n = list[0] == t ? list[1] : list[0];
            if (longest[n] == longest[t]) _partner[t] = n;
        }

        // The wireframe as Blender draws it: every edge once, quads without their diagonal.
        var seen = new HashSet<long>();
        var wire = new List<int>();
        var owners = new List<List<int>>();
        _wireByEdge = new Dictionary<long, int>();
        for (int t = 0; t < triCount; t++)
        {
            long diagonal = _partner[t] >= 0 ? longest[t] : -1;
            for (int e = 0; e < 3; e++)
            {
                int va = _tris[t * 3 + e], vb = _tris[t * 3 + (e + 1) % 3];
                int a = _weld[va], b = _weld[vb];
                if (a == b) continue;
                long key = EdgeKey(a, b);
                if (key == diagonal || !seen.Add(key)) continue;
                _wireByEdge[key] = wire.Count / 2;
                wire.Add(va);
                wire.Add(vb);
                owners.Add(edgeTris[key]);
            }
        }
        _wireEdges = wire.ToArray();
        _wireOwners = owners.ToArray();
        _triWires = new List<int>[triCount];
        for (int t = 0; t < triCount; t++) _triWires[t] = new List<int>(3);
        for (int i = 0; i < _wireOwners.Length; i++)
            foreach (int t in _wireOwners[i]) _triWires[t].Add(i);
        _cacheVersion++;
        PinOldAnchors();
        return true;
    }

    static Vector3Int Quantise(Vector3 p) =>
        new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));

    // The model file was reimported (reshaped in Blender, say). It's the same mesh with the same
    // counts, so the cache wouldn't notice on its own: drop it (and the map layout, cut from the
    // old unique UVs), and remake the replicant from the new shape.
    internal void OnModelsReimported(string[] imported)
    {
        Mesh source = _data != null && _data.mesh != null ? _data.mesh : _cachedMesh;
        if (source == null) return;
        string path = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(path) || System.Array.IndexOf(imported, path) < 0) return;
        _cachedMesh = null;
        _layout = null;
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (TargetInStage) DressStageCopy();
            else if (_data != null && _data.replicant != null) BuildReplicant();
            _previewDirty = true;
            Repaint();
        };
    }

    // ─────────────────────────────────────────── Pinned to the model
    // Gradient lines and decals are kept as the model's own vertex and face numbers (like the
    // groups' faces), so scaling or moving vertices in Blender carries them along. Ones made before
    // that only have rest-pose points; they're pinned here the first time those points are found
    // on the model, and left on their points when they aren't (the model changed in between).

    void PinOldAnchors()
    {
        if (_data == null || _rest == null) return;
        if (_pinnedData == _data && _pinnedVersion == _cacheVersion) return;
        _pinnedData = _data;
        _pinnedVersion = _cacheVersion;

        bool changed = false;
        foreach (var g in _data.fills)
        {
            if (g.gradients != null)
                foreach (var gr in g.gradients)
                {
                    if (gr.lineCorners != null && gr.lineCorners.Count > 0) continue;
                    if (gr.lines == null || gr.lines.Count < 2) continue;
                    var found = new List<int>(gr.lines.Count);
                    for (int k = 0; k + 1 < gr.lines.Count; k += 2)
                        if (_weldLookup.TryGetValue(Quantise(gr.lines[k]), out int a) &&
                            _weldLookup.TryGetValue(Quantise(gr.lines[k + 1]), out int b) &&
                            _weldCorner[a] >= 0 && _weldCorner[b] >= 0)
                        {
                            found.Add(_weldCorner[a]);
                            found.Add(_weldCorner[b]);
                        }
                    if (found.Count != gr.lines.Count) continue;   // not all there: the model changed
                    gr.lineCorners = found;
                    changed = true;
                }

            if (g.decals != null)
                foreach (var d in g.decals)
                {
                    if (d.triangle >= 0 && d.faceUp != Vector2.zero) continue;
                    int t = FaceUnder(g, d.point);
                    if (t < 0) continue;
                    Vector3 a = _rest[_tris[t * 3]];
                    Vector3 e1 = _rest[_tris[t * 3 + 1]] - a, e2 = _rest[_tris[t * 3 + 2]] - a;
                    Vector3 n = Unit(Vector3.Cross(e1, e2));
                    d.triangle = t;
                    d.facePoint = FaceAmounts(d.point - a, e1, e2);
                    d.faceUp = FaceAmounts(Unit(d.up - n * Vector3.Dot(d.up, n)), e1, e2);
                    changed = true;
                }
        }
        foreach (var cr in AllCreaseLines())
        {
            if (cr.cornerA >= 0 && cr.cornerB >= 0) continue;
            if (!_weldLookup.TryGetValue(Quantise(cr.a), out int a) || !_weldLookup.TryGetValue(Quantise(cr.b), out int b) ||
                _weldCorner[a] < 0 || _weldCorner[b] < 0) continue;
            cr.cornerA = _weldCorner[a];
            cr.cornerB = _weldCorner[b];
            changed = true;
        }
        if (changed) EditorUtility.SetDirty(_data);
    }

    // The group's face a decal's old rest-pose point sits on (it was placed on one), or -1.
    int FaceUnder(ModelTexturingData.Fill g, Vector3 p)
    {
        int best = -1;
        float bestOff = float.PositiveInfinity;
        foreach (int t in g.triangles)
        {
            if (t < 0 || t >= TriCount) continue;
            Vector3 a = _rest[_tris[t * 3]];
            Vector3 e1 = _rest[_tris[t * 3 + 1]] - a, e2 = _rest[_tris[t * 3 + 2]] - a;
            Vector3 n = Unit(Vector3.Cross(e1, e2));
            float size = Mathf.Max(e1.magnitude, e2.magnitude);
            if (n == Vector3.zero || size <= 0f) continue;
            Vector2 at = FaceAmounts(p - a, e1, e2);
            const float slack = 1e-3f;
            if (at.x < -slack || at.y < -slack || at.x + at.y > 1f + slack) continue;
            float off = Mathf.Abs(Vector3.Dot(p - a, n)) / size;   // off the face, against its size
            if (off < bestOff) { bestOff = off; best = t; }
        }
        return bestOff <= 1e-3f ? best : -1;
    }

    // A direction across a face as amounts of its two sides from the first corner
    // (dir ≈ e1·x + e2·y). These follow the face through any scaling or reshaping.
    static Vector2 FaceAmounts(Vector3 dir, Vector3 e1, Vector3 e2)
    {
        double aa = Vector3.Dot(e1, e1), ab = Vector3.Dot(e1, e2), bb = Vector3.Dot(e2, e2);
        double ra = Vector3.Dot(dir, e1), rb = Vector3.Dot(dir, e2);
        double det = aa * bb - ab * ab;
        if (det <= aa * bb * 1e-10) return Vector2.zero;
        return new Vector2((float)((ra * bb - rb * ab) / det), (float)((rb * aa - ra * ab) / det));
    }

    // A decal's spot, facing and up on the model as it is now: read off its face when it's pinned
    // to one, else its rest-pose point.
    void DecalFrame(ModelTexturingData.Decal d, out Vector3 point, out Vector3 normal, out Vector3 up)
    {
        point = d.point;
        normal = d.normal;
        up = d.up;
        int t = d.triangle;
        if (t < 0 || t >= TriCount) return;
        Vector3 a = _rest[_tris[t * 3]];
        Vector3 e1 = _rest[_tris[t * 3 + 1]] - a, e2 = _rest[_tris[t * 3 + 2]] - a;
        Vector3 n = Unit(Vector3.Cross(e1, e2));
        Vector3 u = e1 * d.faceUp.x + e2 * d.faceUp.y;
        if (n == Vector3.zero || Unit(u) == Vector3.zero) return;
        point = a + e1 * d.facePoint.x + e2 * d.facePoint.y;
        normal = n;
        up = Unit(u);
    }

    static long EdgeKey(int a, int b) =>
        a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    // Skins the rest positions by hand (bone matrix × bindpose), so picking and drawing match the
    // pose the renderer is showing without any guesswork about BakeMesh's scale handling.
    void PoseWorld()
    {
        if (_world == null || _world.Length != _rest.Length) _world = new Vector3[_rest.Length];
        var smr = _target as SkinnedMeshRenderer;
        Transform[] bones = smr ? smr.bones : null;
        bool skinned = bones != null && bones.Length > 0 && _bindposes.Length == bones.Length &&
                       _boneWeights.Length == _rest.Length;
        if (!skinned)
        {
            var m = _target.transform.localToWorldMatrix;
            for (int i = 0; i < _rest.Length; i++) _world[i] = m.MultiplyPoint3x4(_rest[i]);
            return;
        }

        var skin = new Matrix4x4[bones.Length];
        for (int b = 0; b < bones.Length; b++)
            skin[b] = bones[b] ? bones[b].localToWorldMatrix * _bindposes[b] : _target.transform.localToWorldMatrix;
        for (int i = 0; i < _rest.Length; i++)
        {
            var w = _boneWeights[i];
            Vector3 p = _rest[i];
            _world[i] = skin[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0
                      + skin[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1
                      + skin[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2
                      + skin[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3;
        }
    }

    // ─────────────────────────────────────────── Scene view

    void OnSceneGUI(SceneView view)
    {
        if (!TargetInStage || !EnsureCache()) return;
        Event e = Event.current;

        if (e.type == EventType.Repaint &&
            (_showWireframe || _selection.Count > 0 || _lineSelection.Count > 0 || _selecting || _selectingLines || _addingDecal))
        {
            PoseWorld();
            if (_showWireframe) DrawWireframe();
            DrawSelection();
        }

        if (!_selecting && !_selectingLines && !_addingDecal) return;

        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(id);
            return;
        }

        switch (e.type)
        {
            case EventType.MouseMove:
                PoseWorld();
                if (UpdateHover(e)) view.Repaint();
                break;

            case EventType.MouseDown:
                if (e.button != 0 || e.alt) break;
                PoseWorld();
                UpdateHover(e);
                _strokeAdd = !e.shift;
                if (_addingDecal) PlaceDecal(view.camera);   // one decal per click; Mirror doesn't apply
                else StrokeHovered();
                GUIUtility.hotControl = id;
                e.Use();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != id) break;
                PoseWorld();
                UpdateHover(e);
                if (!_addingDecal) StrokeHovered();
                e.Use();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != id) break;
                GUIUtility.hotControl = 0;
                e.Use();
                break;
        }
    }

    // Returns true when the face (or line) under the cursor changed.
    bool UpdateHover(Event e)
    {
        int oldTri = _hoverTri, oldLine = _hoverLine;
        _hoverTri = PickTriangle(HandleUtility.GUIPointToWorldRay(e.mousePosition));
        _hoverLine = _selectingLines ? PickLine(_hoverTri, e.mousePosition) : -1;
        return oldTri != _hoverTri || oldLine != _hoverLine;
    }

    // Selects (or with Shift deselects) whatever the active tool has under the cursor.
    void StrokeHovered()
    {
        if (_selectingLines)
        {
            if (_hoverLine < 0) return;
            SetLineSelected(_hoverLine, _strokeAdd);
            if (!_mirror) return;
            int twin = LineTwins()[_hoverLine];
            if (twin >= 0 && !_wireOwners[twin].TrueForAll(Unpickable().Contains)) SetLineSelected(twin, _strokeAdd);
        }
        else if (_hoverTri >= 0)
        {
            SetSelected(_hoverTri, _strokeAdd);
            if (!_mirror) return;
            int twin = FaceTwins()[_hoverTri];
            if (twin >= 0) SetSelected(twin, _strokeAdd);
        }
    }

    // The line nearest the cursor among the sides of the face under it — so only lines on the
    // surface you're looking at, never ones behind it. Quad diagonals aren't lines.
    int PickLine(int tri, Vector2 mouse)
    {
        if (tri < 0) return -1;
        int best = -1;
        float bestD = LinePickPixels;
        int p = _partner[tri];
        foreach (int t in p >= 0 ? new[] { tri, p } : new[] { tri })
            foreach (int i in _triWires[t])
            {
                Vector2 a = HandleUtility.WorldToGUIPoint(_world[_wireEdges[i * 2]]);
                Vector2 b = HandleUtility.WorldToGUIPoint(_world[_wireEdges[i * 2 + 1]]);
                float d = HandleUtility.DistancePointToLineSegment(mouse, a, b);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
        return best;
    }

    // Adds the group's faces to the face selection. Locked and hidden faces stay out, as when clicking.
    // The group is then being edited: once the selection changes, its button becomes Update Group Faces.
    void SelectGroupFaces(ModelTexturingData.Fill g)
    {
        if (_target == null || !EnsureCache()) return;
        if (g.locked || g.hidden)
        {
            ShowNotification(new GUIContent("That group is locked or hidden"));
            return;
        }
        foreach (int t in g.triangles)
            if (t >= 0 && t < TriCount) _selection.Add(t);
        _editingGroup = g;
        _editingSnapshot = new HashSet<int>(_selection);
        Repaint();
        SceneView.RepaintAll();
    }

    // Adds every face that isn't in a group to the face selection.
    void SelectUngroupedFaces()
    {
        if (_target == null || !EnsureCache()) return;
        var grouped = new HashSet<int>();
        if (_data != null)
            foreach (var g in _data.fills) grouped.UnionWith(g.triangles);
        for (int t = 0; t < TriCount; t++)
            if (!grouped.Contains(t)) _selection.Add(t);
        Repaint();
        SceneView.RepaintAll();
    }

    // The group's faces become the selection. Faces taken from other groups leave them, as with
    // Group Selected Faces; the selection is then cleared.
    void UpdateGroupFaces(ModelTexturingData.Fill g)
    {
        Undo.RecordObject(_data, "Update group faces");
        foreach (var f in _data.fills)
            if (f != g) f.triangles.RemoveAll(t => _selection.Contains(t));
        g.triangles.Clear();
        g.triangles.AddRange(_selection);
        g.triangles.Sort();

        _data.mesh = _cachedMesh;
        _data.triangleCount = TriCount;
        _selection.Clear();
        _editingGroup = null;
        MarkChanged();
        SceneView.RepaintAll();
    }

    // Adds the group's outline to the line selection: every line with a face of the group on one
    // side only — against another group, ungrouped faces, or the mesh's open edge.
    void SelectOuterLines(ModelTexturingData.Fill g)
    {
        if (_target == null || !EnsureCache()) return;
        int added = 0;
        foreach (int i in OuterLines(g))
            if (_lineSelection.Add(i)) added++;
        if (added == 0) ShowNotification(new GUIContent("No outer lines to add"));
        Repaint();
        SceneView.RepaintAll();
    }

    // The group's outline: every line with exactly one of the group's faces beside it.
    List<int> OuterLines(ModelTexturingData.Fill g)
    {
        var inGroup = new HashSet<int>(g.triangles);
        var lines = new List<int>();
        for (int i = 0; i < _wireOwners.Length; i++)
        {
            int sides = 0;
            foreach (int t in _wireOwners[i])
                if (inGroup.Contains(t)) sides++;
            if (sides == 1) lines.Add(i);
        }
        return lines;
    }

    void SetLineSelected(int i, bool add)
    {
        if (!(add ? _lineSelection.Add(i) : _lineSelection.Remove(i))) return;
        Repaint();
        SceneView.RepaintAll();
    }

    // Faces of locked and hidden groups can't be picked: the ray goes through to the faces behind.
    HashSet<int> Unpickable() => TrianglesOf(g => g.locked || g.hidden);

    int PickTriangle(Ray ray)
    {
        int best = -1;
        float bestT = float.MaxValue;
        var unpickable = Unpickable();
        for (int t = 0; t < TriCount; t++)
        {
            if (unpickable.Contains(t)) continue;
            if (RayTriangle(ray, _world[_tris[t * 3]], _world[_tris[t * 3 + 1]], _world[_tris[t * 3 + 2]], out float d, out float u, out float v) && d < bestT)
            {
                bestT = d;
                best = t;
                _hoverHit = new Vector2(u, v);
            }
        }
        return best;
    }

    // Möller–Trumbore, both sides.
    // u, v = where it hits: a + (b - a) u + (c - a) v.
    static bool RayTriangle(Ray r, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
    {
        t = u = v = 0f;
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 p = Vector3.Cross(r.direction, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        Vector3 s = r.origin - a;
        u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        v = Vector3.Dot(r.direction, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        t = Vector3.Dot(e2, q) * inv;
        return t > 0f;
    }

    // Selects or deselects a face — both halves of a quad together.
    void SetSelected(int t, bool add)
    {
        bool changed = false;
        int p = _partner[t];
        var unpickable = Unpickable();
        foreach (int tri in p >= 0 ? new[] { t, p } : new[] { t })
            if (!unpickable.Contains(tri))
                changed |= add ? _selection.Add(tri) : _selection.Remove(tri);
        if (!changed) return;
        Repaint();
        SceneView.RepaintAll();
    }

    // Mirror toggle: each face/line's twin across X = 0 in the model's own (bind pose) space —
    // where Blender's Mirror modifier put the other half. Built once per mesh.
    int[] FaceTwins()
    {
        if (_faceTwin != null && _faceTwin.Length == TriCount) return _faceTwin;
        int[] twin = MirrorPoints();
        // Faces by their corners, so a mirrored quad is found whichever way its diagonal was cut.
        var faces = new Dictionary<(int, int, int, int), int>();
        for (int t = 0; t < TriCount; t++)
            if (FaceKey(t, null, out var key)) faces[key] = t;
        _faceTwin = new int[TriCount];
        for (int t = 0; t < TriCount; t++)
            _faceTwin[t] = FaceKey(t, twin, out var key) && faces.TryGetValue(key, out int m) ? m : -1;
        return _faceTwin;
    }

    int[] LineTwins()
    {
        int lineCount = _wireEdges.Length / 2;
        if (_lineTwin != null && _lineTwin.Length == lineCount) return _lineTwin;
        int[] twin = MirrorPoints();
        var byEnds = new Dictionary<long, int>();
        for (int i = 0; i < lineCount; i++)
            byEnds[EdgeKey(_weld[_wireEdges[i * 2]], _weld[_wireEdges[i * 2 + 1]])] = i;
        _lineTwin = new int[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            int a = twin[_weld[_wireEdges[i * 2]]], b = twin[_weld[_wireEdges[i * 2 + 1]]];
            _lineTwin[i] = a >= 0 && b >= 0 && byEnds.TryGetValue(EdgeKey(a, b), out int m) ? m : -1;
        }
        return _lineTwin;
    }

    // Each welded point's twin across X = 0, or -1 when nothing sits there.
    int[] MirrorPoints()
    {
        var pos = new Vector3[_weldCount];
        for (int i = 0; i < _rest.Length; i++) pos[_weld[i]] = _rest[i];
        var bounds = new Bounds(pos.Length > 0 ? pos[0] : Vector3.zero, Vector3.zero);
        foreach (var p in pos) bounds.Encapsulate(p);
        float tolerance = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 1e-4f + 1e-7f;

        Vector3Int Cell(Vector3 p) => Vector3Int.FloorToInt(p / tolerance);
        var grid = new Dictionary<Vector3Int, List<int>>();
        for (int w = 0; w < pos.Length; w++)
        {
            var c = Cell(pos[w]);
            if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>(1);
            list.Add(w);
        }

        var twin = new int[pos.Length];
        for (int w = 0; w < pos.Length; w++)
        {
            var mirrored = new Vector3(-pos[w].x, pos[w].y, pos[w].z);
            var c = Cell(mirrored);
            int best = -1;
            float bestD = tolerance * tolerance;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (!grid.TryGetValue(c + new Vector3Int(x, y, z), out var list)) continue;
                foreach (int o in list)
                {
                    float d = (pos[o] - mirrored).sqrMagnitude;
                    if (d <= bestD) { bestD = d; best = o; }
                }
            }
            twin[w] = best;
        }
        return twin;
    }

    // A face's corner points (both halves of a quad), sorted — optionally swapped for their twins.
    bool FaceKey(int t, int[] twin, out (int, int, int, int) key)
    {
        key = default;
        var corners = new List<int>(4);
        int p = _partner[t];
        foreach (int tri in p >= 0 ? new[] { t, p } : new[] { t })
            for (int c = 0; c < 3; c++)
            {
                int id = _weld[_tris[tri * 3 + c]];
                if (twin != null && (id = twin[id]) < 0) return false;
                if (!corners.Contains(id)) corners.Add(id);
            }
        if (corners.Count > 4) return false;
        corners.Sort();
        while (corners.Count < 4) corners.Add(-1);
        key = (corners[0], corners[1], corners[2], corners[3]);
        return true;
    }

    void DrawSelection()
    {
        Camera cam = Camera.current;
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;
        // Nudged towards the camera so the overlay doesn't z-fight the surface it sits on.
        Vector3 N(Vector3 p) => p + (camPos - p) * 0.002f;

        var prevZ = Handles.zTest;
        Handles.zTest = CompareFunction.LessEqual;

        Handles.color = _selectionColour;
        foreach (int t in _selection)
            if (t < TriCount) DrawTriangle(t, N);

        if ((_selecting || _addingDecal) && _hoverTri >= 0)
        {
            Handles.color = _hoverColour;
            DrawTriangle(_hoverTri, N);
            int p = _partner[_hoverTri];
            if (p >= 0 && !Unpickable().Contains(p)) DrawTriangle(p, N);
        }

        // Lines in the same colours as faces, but solid so they read as lines.
        Color lineColour = _selectionColour;
        lineColour.a = 1f;
        Handles.color = lineColour;
        foreach (int i in _lineSelection)
            if (i * 2 + 1 < _wireEdges.Length) DrawWireLine(i, N);
        if (_selectingLines && _hoverLine >= 0)
        {
            Color hover = _hoverColour;
            hover.a = 1f;
            Handles.color = hover;
            DrawWireLine(_hoverLine, N);
        }

        Handles.zTest = prevZ;
    }

    void DrawWireframe()
    {
        Camera cam = Camera.current;
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;
        if (_wireSegments == null || _wireSegments.Length != _wireEdges.Length) _wireSegments = new Vector3[_wireEdges.Length];
        for (int i = 0; i < _wireEdges.Length; i++)
        {
            Vector3 p = _world[_wireEdges[i]];
            _wireSegments[i] = p + (camPos - p) * 0.002f;
        }
        // Edges with hidden faces on every side go with them.
        var segments = _wireSegments;
        var hidden = TrianglesOf(g => g.hidden);
        if (hidden.Count > 0)
        {
            var shown = new List<Vector3>(_wireSegments.Length);
            for (int i = 0; i < _wireOwners.Length; i++)
                if (!_wireOwners[i].TrueForAll(hidden.Contains))
                {
                    shown.Add(_wireSegments[i * 2]);
                    shown.Add(_wireSegments[i * 2 + 1]);
                }
            segments = shown.ToArray();
        }
        var prevZ = Handles.zTest;
        Handles.zTest = CompareFunction.LessEqual;
        Handles.color = _wireColour;
        Handles.DrawLines(segments);

        // Creased lines over it, fainter the softer they are.
        if (_data != null && _data.creaseGroups != null && _wireByEdge != null)
            foreach (var g in _data.creaseGroups)
            {
                Handles.color = Color.Lerp(_wireColour, _creaseColour, g.amount);
                foreach (int i in WiresOf(g))
                {
                    if (hidden.Count > 0 && _wireOwners[i].TrueForAll(hidden.Contains)) continue;
                    Handles.DrawLine(_wireSegments[i * 2], _wireSegments[i * 2 + 1], CreaseThickness);
                }
            }
        Handles.zTest = prevZ;
    }

    void DrawTriangle(int t, System.Func<Vector3, Vector3> nudge)
    {
        Handles.DrawAAConvexPolygon(nudge(_world[_tris[t * 3]]), nudge(_world[_tris[t * 3 + 1]]), nudge(_world[_tris[t * 3 + 2]]));
    }

    void DrawWireLine(int i, System.Func<Vector3, Vector3> nudge)
    {
        Handles.DrawLine(nudge(_world[_wireEdges[i * 2]]), nudge(_world[_wireEdges[i * 2 + 1]]), LineThickness);
    }

    // ─────────────────────────────────────────── Bake

    // Every group's colour on its faces (laid out by Layout), nothing (A = 0) everywhere else. The
    // group's noise is baked in: RGB = the colour mixed back towards the material's base colour by
    // the noise, A = 1. The noise is 3D, read at each pixel's point on the model's
    // rest pose, so its pattern runs on from one face to the next however the faces are laid out on the map.
    Color[] Rasterise(int size)
    {
        var value = new Color[size * size];
        var owned = new bool[size * size];
        var layout = Layout();
        // The corner pixel every ungrouped face sits on stays empty — the dilation never fills it.
        owned[0] = true;
        // Rest positions measured across the model's longest side, so a noise scale of 40 is 40
        // grains across the whole model, whatever its size.
        var bounds = _cachedMesh.bounds;
        _noiseOrigin = bounds.min;
        _noiseSpan = Mathf.Max(1e-5f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
        Color baseColour = BaseColour();
        foreach (var g in _data.fills)
        {
            var gradients = GradientFields(g);
            var decals = DecalStamps(g);
            foreach (int t in g.triangles)
                if (t >= 0 && t < TriCount) RasteriseTriangle(t, size, layout, owned, value, g, baseColour, gradients, decals);
            if (_logDecals) LogDecals(g, decals);   // DEBUG
        }
        Dilate(value, owned, size);
        return value;
    }

    Vector3 _noiseOrigin;
    float _noiseSpan = 1f;

    // A gradient ready to bake: the distance over the group's faces from its lines to every corner
    // (by weld id; ∞ off the group or out of reach), its lines (EdgeKeys of welded ends), and how far
    // it reaches. Pixels measure their own distance from these (see DistanceAt).
    class GradientField
    {
        public ModelTexturingData.LineGradient gradient;
        public float[] distance;
        public HashSet<long> lines;
        public float reach;
    }

    // Each of the group's gradients measured out over its faces: shortest paths along the faces'
    // sides (diagonals too) from the lines' corners. The path stays on the group, so a gradient
    // never jumps across a gap, and wraps round the model instead of cutting through it.
    List<GradientField> GradientFields(ModelTexturingData.Fill g)
    {
        var fields = new List<GradientField>();
        bool outer = g.gradientOuterLines && g.outerLinesGradient != null;
        if (g.gradients.Count == 0 && !outer) return fields;

        var links = new Dictionary<int, List<KeyValuePair<int, float>>>();
        void Link(int a, int b, float len)
        {
            if (!links.TryGetValue(a, out var list)) links[a] = list = new List<KeyValuePair<int, float>>(6);
            list.Add(new KeyValuePair<int, float>(b, len));
        }
        foreach (int t in g.triangles)
        {
            if (t < 0 || t >= TriCount) continue;
            for (int e = 0; e < 3; e++)
            {
                int va = _tris[t * 3 + e], vb = _tris[t * 3 + (e + 1) % 3];
                int a = _weld[va], b = _weld[vb];
                if (a == b) continue;
                float len = (_rest[va] - _rest[vb]).magnitude;
                Link(a, b, len);
                Link(b, a, len);
            }
        }

        // Each gradient with its lines, as the welded corners at their ends. The outer lines one goes
        // first, its lines read off the group's faces as they are now.
        var sources = new List<(ModelTexturingData.LineGradient gradient, HashSet<long> lines)>();
        if (outer)
        {
            var lines = new HashSet<long>();
            foreach (int i in OuterLines(g))
                lines.Add(EdgeKey(_weld[_wireEdges[i * 2]], _weld[_wireEdges[i * 2 + 1]]));
            sources.Add((g.outerLinesGradient, lines));
        }
        foreach (var gr in g.gradients)
        {
            var lines = new HashSet<long>();
            if (gr.lineCorners != null && gr.lineCorners.Count > 0)
            {
                for (int k = 0; k + 1 < gr.lineCorners.Count; k += 2)
                {
                    int ca = gr.lineCorners[k], cb = gr.lineCorners[k + 1];
                    if (ca < 0 || cb < 0 || ca >= _tris.Length || cb >= _tris.Length) continue;
                    int a = _weld[_tris[ca]], b = _weld[_tris[cb]];
                    if (a != b) lines.Add(EdgeKey(a, b));
                }
            }
            else
                for (int k = 0; k + 1 < gr.lines.Count; k += 2)
                    if (_weldLookup.TryGetValue(Quantise(gr.lines[k]), out int a) &&
                        _weldLookup.TryGetValue(Quantise(gr.lines[k + 1]), out int b))
                        lines.Add(EdgeKey(a, b));
            sources.Add((gr, lines));
        }

        foreach (var (gr, lines) in sources)
        {
            var distance = new float[_weldCount];
            for (int i = 0; i < distance.Length; i++) distance[i] = float.PositiveInfinity;
            var queue = new SortedSet<(float, int)>();
            foreach (long key in lines)
                foreach (int w in new[] { (int)(key >> 32), (int)(uint)key })
                    if (links.ContainsKey(w) && distance[w] > 0f)
                    {
                        distance[w] = 0f;
                        queue.Add((0f, w));
                    }
            // No lines on the group's faces (the model changed, or its faces moved to another group).
            if (queue.Count == 0) continue;

            while (queue.Count > 0)
            {
                var next = queue.Min;
                queue.Remove(next);
                (float d, int at) = next;
                foreach (var link in links[at])
                {
                    float nd = d + link.Value;
                    float old = distance[link.Key];
                    if (nd >= old) continue;
                    if (!float.IsPositiveInfinity(old)) queue.Remove((old, link.Key));
                    distance[link.Key] = nd;
                    queue.Add((nd, link.Key));
                }
            }
            var field = new GradientField { gradient = gr, distance = distance, lines = lines };

            // How far the group's faces reach from the lines — measured inside the faces as well as at
            // their corners, since a face whose corners all sit on lines still has an inside.
            float farthest = 0f;
            foreach (int t in g.triangles)
            {
                if (t < 0 || t >= TriCount) continue;
                var faceLines = FaceLines(field, t);
                Vector3 a = _rest[_tris[t * 3]], b = _rest[_tris[t * 3 + 1]], c = _rest[_tris[t * 3 + 2]];
                foreach (Vector3 p in new[] { a, b, c, (a + b) * 0.5f, (b + c) * 0.5f, (c + a) * 0.5f, (a + b + c) / 3f })
                {
                    float d = DistanceAt(field, t, p, faceLines);
                    if (!float.IsPositiveInfinity(d)) farthest = Mathf.Max(farthest, d);
                }
            }
            if (farthest <= 0f) continue;
            field.reach = farthest * gr.length;
            fields.Add(field);
        }
        return fields;
    }

    // The gradient's lines along the sides of a face (both halves of a quad), as rest-pose ends.
    List<(Vector3 a, Vector3 b)> FaceLines(GradientField f, int t)
    {
        var found = new List<(Vector3, Vector3)>();
        int p = _partner[t];
        foreach (int tri in p >= 0 ? new[] { t, p } : new[] { t })
            foreach (int i in _triWires[tri])
            {
                int va = _wireEdges[i * 2], vb = _wireEdges[i * 2 + 1];
                if (f.lines.Contains(EdgeKey(_weld[va], _weld[vb]))) found.Add((_rest[va], _rest[vb]));
            }
        return found;
    }

    // A point's distance from the gradient's lines, the point on face t (rest pose): straight to a
    // line along the face's own sides, or through one of the face's corners and on from there over
    // the surface — whichever is shorter. ∞ when neither reaches.
    float DistanceAt(GradientField f, int t, Vector3 p, List<(Vector3 a, Vector3 b)> faceLines)
    {
        float best = float.PositiveInfinity;
        for (int k = 0; k < 3; k++)
        {
            int v = _tris[t * 3 + k];
            best = Mathf.Min(best, f.distance[_weld[v]] + Vector3.Distance(p, _rest[v]));
        }
        foreach (var (a, b) in faceLines)
        {
            Vector3 ab = b - a;
            float s = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            best = Mathf.Min(best, Vector3.Distance(p, a + ab * s));
        }
        return best;
    }

    // A decal ready to bake: its image's pixels and where on the model it sits, in rest-pose space.
    class DecalStamp
    {
        public Vector3 centre, right, up, normal;
        public float invWidth, invHeight, opacity;
        public Color32[] pixels;
        public int width, height;
        // DEBUG counts, filled while rasterising
        public ModelTexturingData.Decal source;
        public int facesTried, facesFacing, facesTouching, pixelsOnImage, pixelsHit;
        public float maxAlpha;

        // Where a rest-pose point falls on the image, 0–1 across it.
        Vector2 ImageUV(Vector3 p)
        {
            Vector3 o = p - centre;
            return new Vector2(Vector3.Dot(o, right) * invWidth + 0.5f, Vector3.Dot(o, up) * invHeight + 0.5f);
        }

        // False only when the whole face is off one side of the image.
        public bool Touches(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector2 ua = ImageUV(a), ub = ImageUV(b), uc = ImageUV(c);
            return !(ua.x < 0f && ub.x < 0f && uc.x < 0f) && !(ua.x > 1f && ub.x > 1f && uc.x > 1f) &&
                   !(ua.y < 0f && ub.y < 0f && uc.y < 0f) && !(ua.y > 1f && ub.y > 1f && uc.y > 1f);
        }

        // The image at a rest-pose point, smoothed between its pixels. Colours are mixed by their
        // alpha, so clear pixels' colour doesn't fringe the edges. False off the image.
        public bool Sample(Vector3 p, out Color colour)
        {
            colour = default;
            Vector2 uv = ImageUV(p);
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return false;
            float x = uv.x * width - 0.5f, y = uv.y * height - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            Vector4 sum = Vector4.zero;
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                float w = (i == 0 ? 1f - fx : fx) * (j == 0 ? 1f - fy : fy);
                Color32 px = pixels[Mathf.Clamp(y0 + j, 0, height - 1) * width + Mathf.Clamp(x0 + i, 0, width - 1)];
                float a = px.a / 255f * w;
                sum += new Vector4(px.r / 255f * a, px.g / 255f * a, px.b / 255f * a, a);
            }
            if (sum.w <= 1e-6f) return false;
            colour = new Color(sum.x / sum.w, sum.y / sum.w, sum.z / sum.w, sum.w);
            return true;
        }
    }

    // The group's decals that have an image, ready to bake. Scale and offset are parts of the
    // model's longest side (_noiseSpan, set by Rasterise).
    List<DecalStamp> DecalStamps(ModelTexturingData.Fill g)
    {
        var stamps = new List<DecalStamp>();
        if (g.decals == null) return stamps;
        foreach (var d in g.decals)
        {
            if (d.texture == null || d.opacity <= 0f) continue;
            var image = DecalImageOf(d.texture);
            if (image == null) continue;
            DecalFrame(d, out Vector3 point, out Vector3 n, out Vector3 up);
            n = Unit(n);
            up = Unit(up - n * Vector3.Dot(up, n));
            // Looking at the surface (along -n), the image's right is to your right.
            Vector3 right = Unit(Vector3.Cross(up, -n));
            stamps.Add(new DecalStamp
            {
                source = d,
                centre = point + (right * d.offset.x + up * d.offset.y) * _noiseSpan,
                right = right,
                up = up,
                normal = n,
                invWidth = 1f / Mathf.Max(1e-6f, d.scale.x * _noiseSpan),
                invHeight = 1f / Mathf.Max(1e-6f, d.scale.y * _noiseSpan),
                opacity = d.opacity,
                pixels = image.pixels,
                width = image.width,
                height = image.height,
            });
        }
        return stamps;
    }

    // DEBUG: why a decal does or doesn't land. Logged on Bake only.
    [System.NonSerialized] bool _logDecals;

    void LogDecals(ModelTexturingData.Fill g, List<DecalStamp> stamps)
    {
        int listed = g.decals?.Count ?? 0;
        // How far the decal's spot is from the group's nearest corner, against the group's size.
        var bounds = new Bounds();
        bool first = true;
        foreach (int t in g.triangles)
            for (int c = 0; c < 3 && t >= 0 && t < TriCount; c++)
            {
                var p = _rest[_tris[t * 3 + c]];
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                else bounds.Encapsulate(p);
            }
        Debug.Log($"[ModelTexturingDecal] group '{g.name}': {g.triangles.Count} tris, transparency {g.Transparency:0.###}, " +
                  $"transparent {g.Transparent}, hidden {g.hidden}, share {g.mapShare:0.###}, {listed} decal(s) listed, " +
                  $"{stamps.Count} with an image, group size {bounds.size} (model span {_noiseSpan:0.####})");
        if (g.decals != null)
            foreach (var d in g.decals)
            {
                DecalFrame(d, out Vector3 point, out Vector3 normal, out Vector3 up);
                float nearest = float.MaxValue;
                foreach (int t in g.triangles)
                    for (int c = 0; c < 3 && t >= 0 && t < TriCount; c++)
                        nearest = Mathf.Min(nearest, (_rest[_tris[t * 3 + c]] - point).magnitude);
                Debug.Log($"[ModelTexturingDecal]   '{d.name}': texture {(d.texture ? d.texture.name : "NONE")}, opacity {d.opacity:0.###}, " +
                          $"scale {d.scale}, offset {d.offset}, point {point:F4}, normal {normal:F3}, up {up:F3}, pinned to face {d.triangle}, " +
                          $"nearest group corner {nearest:0.####}, point in group bounds {bounds.Contains(point)}");
            }
        foreach (var s in stamps)
            Debug.Log($"[ModelTexturingDecal]   stamp '{s.source.name}': image {s.width}x{s.height}, faces tried {s.facesTried}, " +
                      $"facing {s.facesFacing}, touching {s.facesTouching}, pixels on facing+touching faces {s.pixelsOnImage}, " +
                      $"pixels hit {s.pixelsHit}, max image alpha {s.maxAlpha:0.###}, size {1f / s.invWidth:0.####}x{1f / s.invHeight:0.####}");
    }

    // Unity's normalize gives zero for anything shorter than 1e-5 — and this model is small enough
    // that a face's cross product often is (the angel's head faces are ~1 mm across).
    static Vector3 Unit(Vector3 v)
    {
        float m = v.magnitude;
        return m > 1e-20f ? v / m : Vector3.zero;
    }

    class DecalImage
    {
        public Color32[] pixels;
        public int width, height;
        public Hash128 version;
    }

    // Decal images read once and kept until the image (or its import) changes.
    [System.NonSerialized] Dictionary<Texture2D, DecalImage> _decalImages;

    // A decal image's pixels, as they are in the file (sRGB, like the colours picked in the window),
    // without touching its import settings. PNG/JPG are read straight from the file; anything else
    // is copied off the GPU.
    DecalImage DecalImageOf(Texture2D tex)
    {
        _decalImages ??= new Dictionary<Texture2D, DecalImage>();
        string path = AssetDatabase.GetAssetPath(tex);
        Hash128 version = string.IsNullOrEmpty(path) ? default : AssetDatabase.GetAssetDependencyHash(path);
        if (_decalImages.TryGetValue(tex, out var cached) && cached.version == version) return cached;

        Texture2D read = null;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") && File.Exists(path))
        {
            read = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            if (!read.LoadImage(File.ReadAllBytes(path)))
            {
                DestroyImmediate(read);
                read = null;
            }
        }
        if (read == null)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
            read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
        var image = new DecalImage { pixels = read.GetPixels32(), width = read.width, height = read.height, version = version };
        DestroyImmediate(read);
        _decalImages[tex] = image;
        return image;
    }

    // The base colour the fill's noise mixes back towards: the opaque material's own.
    Color BaseColour()
    {
        var m = _data.material != null ? _data.material : BakeMaterial();
        return m != null && m.HasProperty(BaseColourProp) ? m.GetColor(BaseColourProp) : new Color(0.85f, 0.85f, 0.85f, 1f);
    }

    // Fills a triangle's pixels on the map with its group's baked colour, then its gradients over it,
    // then its decals.
    void RasteriseTriangle(int t, int size, MapLayout layout, bool[] owned, Color[] value, ModelTexturingData.Fill group,
                           Color baseColour, List<GradientField> gradients, List<DecalStamp> decals)
    {
        int ia = _tris[t * 3], ib = _tris[t * 3 + 1], ic = _tris[t * 3 + 2];
        Vector2 a = layout.uv[layout.corner[t * 3]] * size,
                b = layout.uv[layout.corner[t * 3 + 1]] * size,
                c = layout.uv[layout.corner[t * 3 + 2]] * size;
        float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
        if (Mathf.Abs(area) < 1e-12f) return;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
        int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
        int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
        const float eps = -1e-4f;

        // The triangle's corners in noise space: rest pose, scaled by the group's noise scale.
        bool noisy = !group.noiseOff && group.noiseStrength > 0f;
        Vector3 pa = default, pb = default, pc = default;
        if (noisy)
        {
            float k = group.noiseScale / _noiseSpan;
            pa = (_rest[ia] - _noiseOrigin) * k;
            pb = (_rest[ib] - _noiseOrigin) * k;
            pc = (_rest[ic] - _noiseOrigin) * k;
        }

        // Each gradient's lines along this face's sides, for the pixels to measure from.
        var faceLines = new List<(Vector3 a, Vector3 b)>[gradients.Count];
        for (int k = 0; k < gradients.Count; k++) faceLines[k] = FaceLines(gradients[k], t);
        Vector3 ra = _rest[ia], rb = _rest[ib], rc = _rest[ic];

        // The decals that reach this face: it must face the same way as the decal, and some of it
        // must fall inside the image.
        var faceDecals = new List<DecalStamp>(decals.Count);
        Vector3 faceNormal = Vector3.Cross(rb - ra, rc - ra);
        foreach (var d in decals)
        {
            d.facesTried++;   // DEBUG
            bool facing = Vector3.Dot(faceNormal, d.normal) > 0f, touches = d.Touches(ra, rb, rc);
            if (facing) d.facesFacing++;
            if (touches) d.facesTouching++;
            if (facing && touches) faceDecals.Add(d);
        }

        for (int y = y0; y <= y1; y++)
        {
            float py = y + 0.5f;
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f;
                float w0 = ((b.x - px) * (c.y - py) - (c.x - px) * (b.y - py)) / area;
                float w1 = ((c.x - px) * (a.y - py) - (a.x - px) * (c.y - py)) / area;
                float w2 = 1f - w0 - w1;
                if (w0 < eps || w1 < eps || w2 < eps) continue;
                int i = y * size + x;
                owned[i] = true;
                Color colour = group.colour;
                colour.a = 1f;      // "is grouped" — a Transparency group's amount is on the material
                if (noisy)
                {
                    // The pixel's point on the surface — the same point whichever face and map island it's on.
                    float n = GradientNoise3D(pa * w0 + pb * w1 + pc * w2);
                    float alpha = colour.a;
                    colour = Color.LerpUnclamped(baseColour, colour, Mathf.Lerp(1f, n, group.noiseStrength));
                    colour.a = alpha;
                }
                // Gradients in list order, each fading out straight from its lines to its reach. They
                // change the colour only — the map's alpha stays 1. Each pixel
                // measures its own distance, so a face with lines all round it still fades inside.
                Vector3 rp = ra * w0 + rb * w1 + rc * w2;
                for (int k = 0; k < gradients.Count; k++)
                {
                    var f = gradients[k];
                    float d = DistanceAt(f, t, rp, faceLines[k]);
                    if (float.IsPositiveInfinity(d)) continue;
                    float weight = 1f - d / f.reach;
                    if (weight <= 0f) continue;
                    float alpha = colour.a;
                    colour = Color.Lerp(colour, f.gradient.colour, Mathf.Clamp01(weight) * f.gradient.colour.a);
                    colour.a = alpha;
                }
                // Decals in list order, over everything: the image's own alpha × its opacity. Like
                // the gradients they change the colour only.
                foreach (var d in faceDecals)
                {
                    d.pixelsOnImage++;   // DEBUG
                    if (!d.Sample(rp, out Color image)) continue;
                    d.pixelsHit++;
                    d.maxAlpha = Mathf.Max(d.maxAlpha, image.a);
                    float alpha = colour.a;
                    colour = Color.Lerp(colour, image, image.a * d.opacity);
                    colour.a = alpha;
                }
                value[i] = colour;
            }
        }
    }

    // 3D gradient noise, 0–1 centred on 0.5 like Shader Graph's Gradient Noise.
    static float GradientNoise3D(Vector3 p)
    {
        int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
        float fx = p.x - ix, fy = p.y - iy, fz = p.z - iz;
        float ux = Fade(fx), uy = Fade(fy), uz = Fade(fz);
        float n000 = Corner(ix, iy, iz, fx, fy, fz);
        float n100 = Corner(ix + 1, iy, iz, fx - 1f, fy, fz);
        float n010 = Corner(ix, iy + 1, iz, fx, fy - 1f, fz);
        float n110 = Corner(ix + 1, iy + 1, iz, fx - 1f, fy - 1f, fz);
        float n001 = Corner(ix, iy, iz + 1, fx, fy, fz - 1f);
        float n101 = Corner(ix + 1, iy, iz + 1, fx - 1f, fy, fz - 1f);
        float n011 = Corner(ix, iy + 1, iz + 1, fx, fy - 1f, fz - 1f);
        float n111 = Corner(ix + 1, iy + 1, iz + 1, fx - 1f, fy - 1f, fz - 1f);
        float n = Mathf.LerpUnclamped(
            Mathf.LerpUnclamped(Mathf.LerpUnclamped(n000, n100, ux), Mathf.LerpUnclamped(n010, n110, ux), uy),
            Mathf.LerpUnclamped(Mathf.LerpUnclamped(n001, n101, ux), Mathf.LerpUnclamped(n011, n111, ux), uy), uz);
        return Mathf.Clamp01(n + 0.5f);
    }

    static float Fade(float f) => f * f * f * (f * (f * 6f - 15f) + 10f);

    // A lattice point's random unit direction, dotted with the offset from it.
    static float Corner(int x, int y, int z, float dx, float dy, float dz)
    {
        uint h = Hash(x, y, z);
        float gx = (h & 0x3FF) / 511.5f - 1f;
        float gy = ((h >> 10) & 0x3FF) / 511.5f - 1f;
        float gz = ((h >> 20) & 0x3FF) / 511.5f - 1f;
        float len = Mathf.Sqrt(gx * gx + gy * gy + gz * gz);
        return len < 1e-4f ? 0f : (gx * dx + gy * dy + gz * dz) / len;
    }

    static uint Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu;
            h ^= h >> 16; h *= 0x7feb352du;
            h ^= h >> 15; h *= 0x846ca68bu;
            h ^= h >> 16;
            return h;
        }
    }

    static void Dilate(Color[] value, bool[] owned, int size)
    {
        var filled = (bool[])owned.Clone();
        var grow = new List<KeyValuePair<int, Color>>();
        for (int pass = 0; pass < DilatePasses; pass++)
        {
            grow.Clear();
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                if (filled[i]) continue;
                Color sum = default;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                    int j = ny * size + nx;
                    if (!filled[j]) continue;
                    sum += value[j];
                    n++;
                }
                if (n > 0) grow.Add(new KeyValuePair<int, Color>(i, sum / n));
            }
            if (grow.Count == 0) break;
            foreach (var kv in grow)
            {
                value[kv.Key] = kv.Value;
                filled[kv.Key] = true;
            }
        }
    }

    static Color32[] ToPixels(Color[] value)
    {
        var px = new Color32[value.Length];
        for (int i = 0; i < value.Length; i++) px[i] = value[i];
        return px;
    }

    void Bake()
    {
        if (!EnsureCache() || !HasUV2 || _data == null) return;
        int size = _data.textureSize;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(ToPixels(Rasterise(size)));
        LogNoiseDensity(size);   // DEBUG: noise fineness vs map pixels
        _logDecals = true;   // DEBUG: decal counts on the bake below
        try { Rasterise(size); } finally { _logDecals = false; }
        tex.Apply(false);
        string path = _data.shadeMap
            ? AssetDatabase.GetAssetPath(_data.shadeMap)
            : Path.ChangeExtension(AssetDatabase.GetAssetPath(_data), null) + "_ShadeMap.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);
        // Colours are picked in sRGB, so the map is read as sRGB; alpha is always linear.
        ImportMap(path, size, true);

        Undo.RecordObject(_data, "Bake");
        _data.shadeMap = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        _data.mesh = _cachedMesh;
        _data.triangleCount = TriCount;
        EditorUtility.SetDirty(_data);
        AssetDatabase.SaveAssetIfDirty(_data);

        // The real model wears the replicant and its materials from now on (in the stage, the
        // original — the copy already does).
        Renderer model = TargetInStage ? _returnTarget : _target;
        var replicant = BuildReplicant();
        if (replicant != null)
            foreach (var r in TargetInStage ? new[] { model, _target } : new[] { model })
            {
                if (r == null) continue;
                Undo.RecordObject(r, "Bake");
                if (!(r is SkinnedMeshRenderer) && r.TryGetComponent(out MeshFilter mf)) Undo.RecordObject(mf, "Bake");
                SetMesh(r, replicant);
                DressSlots(r);
            }

        foreach (var mat in new[] { _data.material, _data.transparentMaterial })
        {
            if (mat == null || !mat.HasProperty(ShadeMapProp)) continue;
            Undo.RecordObject(mat, "Bake");
            mat.SetTexture(ShadeMapProp, _data.shadeMap);
            EditorUtility.SetDirty(mat);
        }
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent(replicant != null && model != null
            ? $"Baked — {model.name} now wears the replicant + {(replicant.subMeshCount > 1 ? 2 : 1)} material(s)"
            : "Baked — the map only (no model to dress)"));
    }

    // DEBUG: how many map pixels each noise grain gets, per group. Below ~2 the grain can't be
    // drawn — a finer scale only turns into pixel speckle that bilinear filtering smears.
    void LogNoiseDensity(int size)
    {
        foreach (var g in _data.fills)
        {
            if (g.noiseOff || g.noiseStrength <= 0f) continue;
            double mapArea = 0, grainArea = 0;
            float k = g.noiseScale / _noiseSpan;
            var layout = Layout();
            foreach (int t in g.triangles)
            {
                if (t < 0 || t >= TriCount) continue;
                int ia = _tris[t * 3], ib = _tris[t * 3 + 1], ic = _tris[t * 3 + 2];
                Vector2 a = layout.uv[layout.corner[t * 3]] * size,
                        b = layout.uv[layout.corner[t * 3 + 1]] * size,
                        c = layout.uv[layout.corner[t * 3 + 2]] * size;
                mapArea += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f;
                grainArea += Vector3.Cross((_rest[ib] - _rest[ia]) * k, (_rest[ic] - _rest[ia]) * k).magnitude * 0.5f;
            }
            double pixelsPerGrain = grainArea > 0 ? System.Math.Sqrt(mapArea / grainArea) : 0;
            Debug.Log($"[ModelTexturing] '{g.name}': scale {g.noiseScale}, map {size}px → " +
                      $"{pixelsPerGrain:0.###} map pixels per grain (faces cover {mapArea:0} px², {grainArea:0} grains²)");
        }
    }

    static void ImportMap(string path, int size, bool sRGB)
    {
        AssetDatabase.ImportAsset(path);
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
        ti.sRGBTexture = sRGB;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.mipmapEnabled = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = Mathf.Max(ti.maxTextureSize, size);
        ti.SaveAndReimport();
    }

    // ─────────────────────────────────────────── Live preview

    void RefreshPreview()
    {
        if (_target == null || _data == null || !EnsureCache() || !HasUV2) return;
        // Only a model wearing the replicant has the map's layout on it, and only the stage copy
        // is previewed on.
        if (!TargetInStage || !WearsLayout(_target))
        {
            ClearPreview();
            return;
        }
        int size = _data.textureSize;
        if (_preview == null || _preview.width != size)
        {
            if (_preview) DestroyImmediate(_preview);
            _preview = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }
        _preview.SetPixels32(ToPixels(Rasterise(size)));
        _preview.Apply(false);

        _block ??= new MaterialPropertyBlock();
        _target.GetPropertyBlock(_block);
        _block.SetTexture(ShadeMapProp, _preview);
        _target.SetPropertyBlock(_block);
        HideLevelSelectEffects();
        SceneView.RepaintAll();
    }

    void ClearPreview()
    {
        if (!_target) return;
        _target.SetPropertyBlock(null);
        HideLevelSelectEffects();
    }

    // The stage copy never shows Add Lighting / Add White Fade, whatever the materials say: both
    // read the level select's light and boat, which in the stage are whatever was last pushed. Done
    // on the copy's property block, so the materials (and the real model) keep their settings.
    void HideLevelSelectEffects()
    {
        if (!TargetInStage) return;
        _block ??= new MaterialPropertyBlock();
        _target.GetPropertyBlock(_block);
        _block.SetFloat(AddLightingProp, 0f);
        _block.SetFloat(AddBakedLightingProp, 0f);
        _block.SetFloat(AddWhiteFadeProp, 0f);
        _target.SetPropertyBlock(_block);
    }

    // ─────────────────────────────────────────── Display prefs

    const string PrefPrefix = "ModelTexturingTool.";

    void LoadDisplay()
    {
        _selectionColour = LoadColour("SelectionColour", new Color(1f, 0.55f, 0.1f, 0.45f));
        _hoverColour = LoadColour("HoverColour", new Color(1f, 1f, 1f, 0.5f));
        _wireColour = LoadColour("WireColour", new Color(0f, 0f, 0f, 0.6f));
        _creaseColour = LoadColour("CreaseColour", new Color(1f, 0f, 1f, 1f));   // Blender's crease magenta
        _showWireframe = EditorPrefs.GetBool(PrefPrefix + "ShowWireframe", false);
        _mirror = EditorPrefs.GetBool(PrefPrefix + "Mirror", false);
    }

    void SaveDisplay()
    {
        SaveColour("SelectionColour", _selectionColour);
        SaveColour("HoverColour", _hoverColour);
        SaveColour("WireColour", _wireColour);
        SaveColour("CreaseColour", _creaseColour);
    }

    static Color LoadColour(string key, Color fallback) =>
        ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString(PrefPrefix + key, ""), out var c) ? c : fallback;

    static void SaveColour(string key, Color c) =>
        EditorPrefs.SetString(PrefPrefix + key, ColorUtility.ToHtmlStringRGBA(c));
}

// Tells open Model Texturing Tool windows when model files are reimported.
class ModelTexturingReimportWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Length == 0) return;
        foreach (var window in Resources.FindObjectsOfTypeAll<ModelTexturingTool>())
            window.OnModelsReimported(imported);
    }
}
