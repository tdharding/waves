using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEngine.Splines;

public class SplineToolsWindow : EditorWindow
{
    // ── Tabs ─────────────────────────────────────────────────────
    private enum Tab { Split, Merge }
    private Tab _tab = Tab.Split;

    // ── Prefs keys ───────────────────────────────────────────────
    private const string K_Tab              = "SplineTools_Tab";
    private const string K_Subdivisions     = "SplineSplitter_Subdivisions";
    private const string K_SplitT           = "SplineSplitter_SplitT";
    private const string K_KeepOriginal     = "SplineSplitter_KeepOriginal";
    private const string K_CopyInst         = "SplineSplitter_CopyInstantiate";
    private const string K_AddJunction      = "SplineSplitter_AddJunction";
    private const string K_KnotMode         = "SplineSplitter_KnotMode";
    private const string K_Padding          = "SplineSplitter_Padding";
    private const string K_SameObject       = "SplineSplitter_SameObject";
    private const string K_RotOffX          = "SplineSplitter_RotOffX";
    private const string K_RotOffY          = "SplineSplitter_RotOffY";
    private const string K_RotOffZ          = "SplineSplitter_RotOffZ";
    private const string K_PosOffX          = "SplineSplitter_PosOffX";
    private const string K_PosOffY          = "SplineSplitter_PosOffY";
    private const string K_PosOffZ          = "SplineSplitter_PosOffZ";
    private const string K_JunctionPrefab   = "SplineSplitter_JunctionPrefabGUID";
    private const string K_MergeMode        = "SplineTools_MergeMode";
    private const string K_MergeSeparate    = "SplineTools_MergeSeparate";
    private const string K_SimplifyAfterMerge   = "SplineTools_SimplifyAfterMerge";
    private const string K_SimplifyTargetKnots  = "SplineTools_SimplifyTargetKnots";
    private const string K_SimplifyAutoSamples  = "SplineTools_SimplifyAutoSamples";

    // ── Split: Fields ────────────────────────────────────────────
    private SplineContainer _splitTarget;
    private float           _splitT       = 0.5f;
    private int             _subdivisions = 20;
    private bool            _keepOriginal;
    private bool            _copySplineInstantiate;
    private bool            _addJunction;
    private float           _padding;
    private enum KnotTangentMode { Broken, EndsBrokenMidAuto, AllAutoSmooth }
    private KnotTangentMode _knotMode;
    private bool            _splitToSameObject;
    private GameObject      _junctionPrefab;
    private Vector3         _junctionRotOffset;
    private Vector3         _junctionPosOffset;
    private SplineSplitterPreset _activePreset;

    // ── Junction: River Segment ID flags ─────────────────────────
    private bool _addRiverSegmentID;
    private bool _sideAIsLeftPath;
    private bool _sideAIsRightPath;
    private bool _sideBIsLeftPath;
    private bool _sideBIsRightPath;
    private const string K_AddSegID   = "SplineSplitter_AddSegID";
    private const string K_SideATop   = "SplineSplitter_SideATop";
    private const string K_SideABot   = "SplineSplitter_SideABot";
    private const string K_SideBTop   = "SplineSplitter_SideBTop";
    private const string K_SideBBot   = "SplineSplitter_SideBBot";

    // ── Merge: Fields ────────────────────────────────────────────
    private enum MergeMode { CrossObject, SameGameObject, SameContainer }
    private MergeMode _mergeMode;

    // Cross-object
    private SplineContainer       _mergeTarget;
    private List<SplineContainer> _mergeSources = new();

    // Same-object
    private GameObject _mergeGO;
    private bool       _mergeSeparateSplines = true;

    // Same-container
    private SplineContainer _mergeContainerTarget;

    // Post-process: simplify
    private bool _simplifyAfterMerge;
    private int  _simplifyTargetKnots  = 30;
    private bool _simplifyManyPointsAuto = true;

    private Vector2 _scroll;

    // ── Menu ─────────────────────────────────────────────────────
    [MenuItem("Tools/Spline Tools")]
    public static void OpenWindow()
    {
        var win = GetWindow<SplineToolsWindow>("Spline Tools");
        win.minSize = new Vector2(340, 520);
        win.Show();
    }

    [MenuItem("Tools/Spline Tools (Split)")]
    public static void OpenSplit() { OpenWindow(); GetWindow<SplineToolsWindow>()._tab = Tab.Split; }

    [MenuItem("Tools/Spline Tools (Merge)")]
    public static void OpenMerge() { OpenWindow(); GetWindow<SplineToolsWindow>()._tab = Tab.Merge; }

    // ── Prefs ────────────────────────────────────────────────────
    private void OnEnable()
    {
        _tab                   = (Tab)EditorPrefs.GetInt        (K_Tab,           0);
        _subdivisions          = EditorPrefs.GetInt             (K_Subdivisions,  20);
        _splitT                = EditorPrefs.GetFloat           (K_SplitT,        0.5f);
        _keepOriginal          = EditorPrefs.GetBool            (K_KeepOriginal,  false);
        _copySplineInstantiate = EditorPrefs.GetBool            (K_CopyInst,      true);
        _addJunction           = EditorPrefs.GetBool            (K_AddJunction,   false);
        _padding               = EditorPrefs.GetFloat           (K_Padding,       0f);
        _knotMode              = (KnotTangentMode)EditorPrefs.GetInt(K_KnotMode,  0);
        _splitToSameObject     = EditorPrefs.GetBool            (K_SameObject,    false);
        _junctionRotOffset     = new Vector3(
            EditorPrefs.GetFloat(K_RotOffX, 0f),
            EditorPrefs.GetFloat(K_RotOffY, 0f),
            EditorPrefs.GetFloat(K_RotOffZ, 0f));
        _junctionPosOffset     = new Vector3(
            EditorPrefs.GetFloat(K_PosOffX, 0f),
            EditorPrefs.GetFloat(K_PosOffY, 0f),
            EditorPrefs.GetFloat(K_PosOffZ, 0f));
        _mergeMode             = (MergeMode)EditorPrefs.GetInt  (K_MergeMode,     0);
        _mergeSeparateSplines  = EditorPrefs.GetBool            (K_MergeSeparate, true);
        _simplifyAfterMerge    = EditorPrefs.GetBool            (K_SimplifyAfterMerge,  false);
        _simplifyTargetKnots   = EditorPrefs.GetInt             (K_SimplifyTargetKnots, 30);
        _simplifyManyPointsAuto= EditorPrefs.GetBool            (K_SimplifyAutoSamples, true);

        _addRiverSegmentID = EditorPrefs.GetBool(K_AddSegID, false);
        _sideAIsLeftPath    = EditorPrefs.GetBool(K_SideATop, false);
        _sideAIsRightPath = EditorPrefs.GetBool(K_SideABot, false);
        _sideBIsLeftPath    = EditorPrefs.GetBool(K_SideBTop, false);
        _sideBIsRightPath = EditorPrefs.GetBool(K_SideBBot, false);

        string prefabGUID = EditorPrefs.GetString(K_JunctionPrefab, "");
        if (!string.IsNullOrEmpty(prefabGUID))
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGUID);
            if (!string.IsNullOrEmpty(path))
                _junctionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
    }

    private void SavePrefs()
    {
        EditorPrefs.SetInt  (K_Tab,          (int)_tab);
        EditorPrefs.SetInt  (K_Subdivisions, _subdivisions);
        EditorPrefs.SetFloat(K_SplitT,       _splitT);
        EditorPrefs.SetBool (K_KeepOriginal, _keepOriginal);
        EditorPrefs.SetBool (K_CopyInst,     _copySplineInstantiate);
        EditorPrefs.SetBool (K_AddJunction,  _addJunction);
        EditorPrefs.SetFloat(K_Padding,      _padding);
        EditorPrefs.SetInt  (K_KnotMode,     (int)_knotMode);
        EditorPrefs.SetBool (K_SameObject,   _splitToSameObject);
        EditorPrefs.SetFloat(K_RotOffX,      _junctionRotOffset.x);
        EditorPrefs.SetFloat(K_RotOffY,      _junctionRotOffset.y);
        EditorPrefs.SetFloat(K_RotOffZ,      _junctionRotOffset.z);
        EditorPrefs.SetFloat(K_PosOffX,      _junctionPosOffset.x);
        EditorPrefs.SetFloat(K_PosOffY,      _junctionPosOffset.y);
        EditorPrefs.SetFloat(K_PosOffZ,      _junctionPosOffset.z);
        EditorPrefs.SetInt  (K_MergeMode,    (int)_mergeMode);
        EditorPrefs.SetBool (K_MergeSeparate,_mergeSeparateSplines);
        EditorPrefs.SetBool (K_SimplifyAfterMerge,  _simplifyAfterMerge);
        EditorPrefs.SetInt  (K_SimplifyTargetKnots, _simplifyTargetKnots);
        EditorPrefs.SetBool (K_SimplifyAutoSamples,  _simplifyManyPointsAuto);

        EditorPrefs.SetBool(K_AddSegID,  _addRiverSegmentID);
        EditorPrefs.SetBool(K_SideATop,  _sideAIsLeftPath);
        EditorPrefs.SetBool(K_SideABot,  _sideAIsRightPath);
        EditorPrefs.SetBool(K_SideBTop,  _sideBIsLeftPath);
        EditorPrefs.SetBool(K_SideBBot,  _sideBIsRightPath);

        if (_junctionPrefab != null)
        {
            string path = AssetDatabase.GetAssetPath(_junctionPrefab);
            EditorPrefs.SetString(K_JunctionPrefab, AssetDatabase.AssetPathToGUID(path));
        }
        else
        {
            EditorPrefs.SetString(K_JunctionPrefab, "");
        }
    }

    // ── Main GUI ─────────────────────────────────────────────────
    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.Space(4);
        EditorGUI.BeginChangeCheck();
        _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Split", "Merge" });
        if (EditorGUI.EndChangeCheck()) SavePrefs();
        EditorGUILayout.Space(6);

        if (_tab == Tab.Split)
            DrawSplitGUI();
        else
            DrawMergeGUI();

        EditorGUILayout.EndScrollView();
    }

    // ══════════════════════════════════════════════════════════════
    // SPLIT TAB
    // ══════════════════════════════════════════════════════════════
    private void DrawSplitGUI()
    {
        DrawHeader("Spline Splitter");
        DrawPresetBar();

        if (_splitTarget == null && Selection.activeGameObject != null)
            _splitTarget = Selection.activeGameObject.GetComponent<SplineContainer>();

        // Source
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        _splitTarget = (SplineContainer)EditorGUILayout.ObjectField(
            "Spline Container", _splitTarget, typeof(SplineContainer), true);

        if (_splitTarget != null)
        {
            var sp = _splitTarget.Splines[0];
            EditorGUILayout.LabelField(
                $"Knots: {sp.Count}  |  Closed: {sp.Closed}", EditorStyles.miniLabel);
        }

        // Split settings
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Split Settings", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _splitT = EditorGUILayout.Slider("Split Point (t)", _splitT, 0.001f, 0.999f);
        if (EditorGUI.EndChangeCheck()) SavePrefs();

        EditorGUI.BeginChangeCheck();
        _subdivisions = EditorGUILayout.IntSlider("Curve Subdivisions", _subdivisions, 1, 64);
        _knotMode     = (KnotTangentMode)EditorGUILayout.EnumPopup("Knot Tangents", _knotMode);
        _keepOriginal = EditorGUILayout.Toggle("Keep Original Spline", _keepOriginal);
        if (EditorGUI.EndChangeCheck()) SavePrefs();

        // Options
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        if (!_addJunction)
            _splitToSameObject = EditorGUILayout.Toggle("Keep On Same GameObject", _splitToSameObject);
        else
            EditorGUILayout.LabelField("Keep On Same GameObject", "Yes (junction mode)", EditorStyles.miniLabel);
        _copySplineInstantiate = EditorGUILayout.Toggle("Copy SplineInstantiate", _copySplineInstantiate);
        if (EditorGUI.EndChangeCheck()) SavePrefs();

        // Junction
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Junction", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _addJunction = EditorGUILayout.Toggle("Add Junction", _addJunction);
        if (EditorGUI.EndChangeCheck()) SavePrefs();

        if (_addJunction)
        {
            EditorGUI.BeginChangeCheck();
            _junctionPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Junction Prefab", _junctionPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck()) SavePrefs();

            EditorGUI.BeginChangeCheck();
            _padding           = EditorGUILayout.FloatField("Padding (world units)", _padding);
            _junctionPosOffset = EditorGUILayout.Vector3Field("Position Offset", _junctionPosOffset);
            _junctionRotOffset = EditorGUILayout.Vector3Field("Rotation Offset", _junctionRotOffset);
            if (EditorGUI.EndChangeCheck()) SavePrefs();

            if (_junctionPrefab != null)
            {
                float measured = SplineSplitUtility.MeasurePrefabXExtent(_junctionPrefab);
                float total    = measured + _padding * 2f;
                EditorGUILayout.HelpBox(
                    $"Prefab X extent: {measured:F3}  |  Total gap: {total:F3}\n" +
                    "Produces 3 SplineContainers on this GO: A → Junction → B.\n" +
                    "Junction gets its own SplineInstantiate (InstanceCount = 1).",
                    MessageType.None);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("River Segment IDs", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _addRiverSegmentID = EditorGUILayout.Toggle("Add RiverSegmentID", _addRiverSegmentID);
            if (_addRiverSegmentID)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Side A (before junction)", GUILayout.Width(150));
                _sideAIsLeftPath    = EditorGUILayout.ToggleLeft("Top",    _sideAIsLeftPath,    GUILayout.Width(48));
                _sideAIsRightPath = EditorGUILayout.ToggleLeft("Bottom", _sideAIsRightPath, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Side B (after junction)",  GUILayout.Width(150));
                _sideBIsLeftPath    = EditorGUILayout.ToggleLeft("Top",    _sideBIsLeftPath,    GUILayout.Width(48));
                _sideBIsRightPath = EditorGUILayout.ToggleLeft("Bottom", _sideBIsRightPath, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
            }
            if (EditorGUI.EndChangeCheck()) SavePrefs();
        }

        // Button
        EditorGUILayout.Space(12);
        bool ready = _splitTarget != null && _splitTarget.Splines.Count > 0;
        using (new EditorGUI.DisabledScope(!ready))
        {
            if (GUILayout.Button("Split Spline", GUILayout.Height(32)))
                DoSplit();
        }
        if (!ready)
            EditorGUILayout.HelpBox("Assign a SplineContainer with a valid spline.", MessageType.Info);

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Junction mode: 3 SplineContainers on the original GO — A, Junction, B.\n" +
            "Junction SplineInstantiate uses InstanceCount=1 with the assigned prefab.\n" +
            "All operations collapse into a single Ctrl+Z undo step.",
            MessageType.None);

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Refresh SplineInstantiate", GUILayout.Height(26)))
            RefreshInstantiates();
    }

    // ══════════════════════════════════════════════════════════════
    // MERGE TAB
    // ══════════════════════════════════════════════════════════════
    private void DrawMergeGUI()
    {
        DrawHeader("Spline Merger");

        EditorGUI.BeginChangeCheck();
        _mergeMode = (MergeMode)EditorGUILayout.EnumPopup("Mode", _mergeMode);
        if (EditorGUI.EndChangeCheck()) SavePrefs();

        EditorGUILayout.Space(8);

        if (_mergeMode == MergeMode.CrossObject)
            DrawCrossObjectMerge();
        else if (_mergeMode == MergeMode.SameGameObject)
            DrawSameObjectMerge();
        else
            DrawSameContainerMerge();

        DrawPostProcessSection();
    }

    private void DrawCrossObjectMerge()
    {
        EditorGUILayout.LabelField("Cross Object", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Appends knots from each source container into the target container's first spline.\n" +
            "Knots are converted from source local space into target local space.",
            MessageType.None);

        EditorGUILayout.Space(6);
        _mergeTarget = (SplineContainer)EditorGUILayout.ObjectField(
            "Target Container", _mergeTarget, typeof(SplineContainer), true);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Sources", EditorStyles.boldLabel);

        for (int i = 0; i < _mergeSources.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            _mergeSources[i] = (SplineContainer)EditorGUILayout.ObjectField(
                $"Source {i + 1}", _mergeSources[i], typeof(SplineContainer), true);
            if (GUILayout.Button("−", GUILayout.Width(24)))
            {
                _mergeSources.RemoveAt(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("+ Add Source"))
            _mergeSources.Add(null);

        EditorGUILayout.Space(10);

        bool canMerge = _mergeTarget != null && _mergeSources.Count > 0;
        using (new EditorGUI.DisabledScope(!canMerge))
        {
            if (GUILayout.Button("Merge Into Target", GUILayout.Height(30)))
                DoMergeCrossObject();
        }
        if (!canMerge)
            EditorGUILayout.HelpBox("Assign a target and at least one source.", MessageType.Info);
    }

    private void DrawSameObjectMerge()
    {
        EditorGUILayout.LabelField("Same GameObject", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Consolidates all SplineContainer components on one GameObject into the first one.\n" +
            "Extra SplineContainer (and SplineInstantiate) components are removed.",
            MessageType.None);

        EditorGUILayout.Space(6);
        _mergeGO = (GameObject)EditorGUILayout.ObjectField(
            "Game Object", _mergeGO, typeof(GameObject), true);

        if (_mergeGO == null)
        {
            if (Selection.activeGameObject != null &&
                Selection.activeGameObject.GetComponent<SplineContainer>() != null)
            {
                _mergeGO = Selection.activeGameObject;
                Repaint();
            }
            return;
        }

        var containers = _mergeGO.GetComponents<SplineContainer>();
        EditorGUILayout.LabelField(
            $"Found {containers.Length} SplineContainer(s)", EditorStyles.miniLabel);

        if (containers.Length > 1)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Containers on this GO:", EditorStyles.boldLabel);
            for (int i = 0; i < containers.Length; i++)
            {
                int splineCount = containers[i].Splines.Count;
                int knotTotal   = 0;
                foreach (var s in containers[i].Splines) knotTotal += s.Count;
                EditorGUILayout.LabelField(
                    $"  [{i}]  {splineCount} spline(s), {knotTotal} total knots",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            _mergeSeparateSplines = EditorGUILayout.Toggle(
                "Keep As Separate Splines", _mergeSeparateSplines);
            if (EditorGUI.EndChangeCheck()) SavePrefs();
            EditorGUILayout.HelpBox(
                _mergeSeparateSplines
                    ? "Each source spline is kept as a separate spline inside the first container."
                    : "All knots are appended into one continuous spline in the first container.",
                MessageType.None);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Merge All Into First Container", GUILayout.Height(30)))
                DoMergeSameObject(_mergeGO, containers);
        }
        else if (containers.Length == 1)
        {
            EditorGUILayout.HelpBox(
                "Only one SplineContainer found — nothing to merge.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No SplineContainer components found on this GameObject.", MessageType.Warning);
        }
    }

    private void DrawSameContainerMerge()
    {
        EditorGUILayout.LabelField("Same Container", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Merges all splines within one SplineContainer into a single spline.\n" +
            "Knots are appended in spline order. Extra splines are removed.",
            MessageType.None);

        EditorGUILayout.Space(6);
        _mergeContainerTarget = (SplineContainer)EditorGUILayout.ObjectField(
            "Container", _mergeContainerTarget, typeof(SplineContainer), true);

        if (_mergeContainerTarget == null)
        {
            if (Selection.activeGameObject != null)
            {
                var c = Selection.activeGameObject.GetComponent<SplineContainer>();
                if (c != null) { _mergeContainerTarget = c; Repaint(); }
            }
            return;
        }

        int splineCount = _mergeContainerTarget.Splines.Count;
        EditorGUILayout.LabelField($"Splines in container: {splineCount}", EditorStyles.miniLabel);

        if (splineCount > 1)
        {
            int knotTotal = 0;
            foreach (var s in _mergeContainerTarget.Splines) knotTotal += s.Count;
            EditorGUILayout.LabelField($"Total knots: {knotTotal}", EditorStyles.miniLabel);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Merge All Splines Into One", GUILayout.Height(30)))
                DoMergeSameContainer(_mergeContainerTarget);
        }
        else if (splineCount == 1)
        {
            EditorGUILayout.HelpBox("Only one spline in this container — nothing to merge.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("No splines found in this container.", MessageType.Warning);
        }
    }

    // ── Preset bar ───────────────────────────────────────────────
    private void DrawPresetBar()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        _activePreset = (SplineSplitterPreset)EditorGUILayout.ObjectField(
            _activePreset, typeof(SplineSplitterPreset), false);
        if (EditorGUI.EndChangeCheck() && _activePreset != null)
            ApplyPreset(_activePreset);

        if (GUILayout.Button("New", EditorStyles.miniButtonLeft,  GUILayout.Width(40)))
            CreatePreset();
        using (new EditorGUI.DisabledScope(_activePreset == null))
        {
            if (GUILayout.Button("Save", EditorStyles.miniButtonRight, GUILayout.Width(44)))
            {
                CaptureToPreset(_activePreset);
                AssetDatabase.SaveAssets();
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);
    }

    private void ApplyPreset(SplineSplitterPreset p)
    {
        _subdivisions          = p.subdivisions;
        _splitT                = p.splitT;
        _keepOriginal          = p.keepOriginal;
        _copySplineInstantiate = p.copySplineInstantiate;
        _splitToSameObject     = p.splitToSameObject;
        _knotMode              = (KnotTangentMode)p.knotMode;
        _addJunction           = p.addJunction;
        _junctionPrefab        = p.junctionPrefab;
        _padding               = p.padding;
        _junctionPosOffset     = p.junctionPosOffset;
        _junctionRotOffset     = p.junctionRotOffset;
        SavePrefs();
    }

    private void CaptureToPreset(SplineSplitterPreset p)
    {
        Undo.RecordObject(p, "Save Spline Preset");
        p.subdivisions          = _subdivisions;
        p.splitT                = _splitT;
        p.keepOriginal          = _keepOriginal;
        p.copySplineInstantiate = _copySplineInstantiate;
        p.splitToSameObject     = _splitToSameObject;
        p.knotMode              = (int)_knotMode;
        p.addJunction           = _addJunction;
        p.junctionPrefab        = _junctionPrefab;
        p.padding               = _padding;
        p.junctionPosOffset     = _junctionPosOffset;
        p.junctionRotOffset     = _junctionRotOffset;
        EditorUtility.SetDirty(p);
    }

    private void CreatePreset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Spline Preset", "SplinePreset", "asset",
            "Choose where to save the preset");
        if (string.IsNullOrEmpty(path)) return;

        var p = CreateInstance<SplineSplitterPreset>();
        CaptureToPreset(p);
        AssetDatabase.CreateAsset(p, path);
        AssetDatabase.SaveAssets();
        _activePreset = p;
    }

    private void RefreshInstantiates()
    {
        var targets = new HashSet<GameObject>();
        if (_splitTarget != null) targets.Add(_splitTarget.gameObject);
        foreach (var go in Selection.gameObjects) targets.Add(go);

        int count = 0;
        foreach (var go in targets)
            foreach (var si in go.GetComponents<SplineInstantiate>())
            {
                si.UpdateInstances();
                count++;
            }

        if (count > 0)
            Debug.Log($"[SplineTools] Refreshed {count} SplineInstantiate(s).");
        else
            Debug.Log("[SplineTools] No SplineInstantiate components found on selected objects.");
    }

    // ══════════════════════════════════════════════════════════════
    // PUBLIC API — callable by LevelSelectDesignerWindow
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Splits <paramref name="source"/> in-place using the given preset, exactly as DoSplit does.
    /// The source container becomes segment A; new SplineContainer components are added to the
    /// same GO for the junction gap (J) and the continuation (B).
    /// Pass the directional visual prefab for the gap SplineInstantiate (InstanceCount=1).
    /// Returns (A=source, J, B), or (null,null,null) on failure.
    /// </summary>
    public static (SplineContainer a, SplineContainer j, SplineContainer b) SplitInPlace(
        SplineContainer source, float splitT, SplineSplitterPreset preset, GameObject junctionVisualPrefab)
    {
        Debug.Log($"[SplitInPlace] source={source?.name ?? "NULL"}  splitT={splitT:F4}  preset={preset?.name ?? "NULL"}  prefab={junctionVisualPrefab?.name ?? "NULL"}");

        if (source == null || source.Splines.Count == 0)
        {
            Debug.LogWarning("[SplitInPlace] ABORT: source is null or has no splines.");
            return (null, null, null);
        }

        var spline = source.Splines[0];
        if (spline.Closed || spline.Count < 2)
        {
            Debug.LogWarning($"[SplitInPlace] ABORT: spline is closed={spline.Closed} or has {spline.Count} knots.");
            return (null, null, null);
        }

        if (preset == null)
        {
            Debug.LogWarning("[SplitInPlace] ABORT: preset is null — assign StandardSplineSplitter in the designer prefab panel.");
            return (null, null, null);
        }

        float halfGap = junctionVisualPrefab != null
            ? SplineSplitUtility.MeasurePrefabXExtent(junctionVisualPrefab) * 0.5f + preset.padding
            : Mathf.Max(0f, preset.padding);

        Debug.Log($"[SplitInPlace] halfGap={halfGap:F4}  subdivisions={preset.subdivisions}  knotCount={spline.Count}");

        var split = SplineSplitUtility.ComputeSplit(source, splitT, halfGap, preset.subdivisions);
        if (!split.valid)
        {
            Debug.LogWarning("[SplitInPlace] ABORT: ComputeSplit returned invalid result — reduce padding.");
            return (null, null, null);
        }

        Debug.Log($"[SplitInPlace] posA={split.posA.Count}  posB={split.posB.Count}  tangent={split.splitTangentWorld}");

        TangentMode knotMode = preset.knotMode == 0 ? TangentMode.Broken : TangentMode.AutoSmooth;

        var splineA = SplineSplitUtility.BuildSplineFromPositions(split.posA, knotMode);
        var splineB = SplineSplitUtility.BuildSplineFromPositions(split.posB, knotMode);
        var splineJ = SplineSplitUtility.BuildSplineFromPositions(
            new List<Unity.Mathematics.float3> { split.endA, split.startB }, TangentMode.Broken);

        var go = source.gameObject;

        // A — replace source spline
        Undo.RegisterCompleteObjectUndo(source, "Split Spline (Junction)");
        source.RemoveSplineAt(0);
        source.AddSpline(splineA);
        EditorUtility.SetDirty(source);
        Debug.Log($"[SplitInPlace] Segment A applied ({splineA.Count} knots)");

        // J — gap container with visual prefab SplineInstantiate
        var containerJ = Undo.AddComponent<SplineContainer>(go);
        containerJ.RemoveSplineAt(0);
        containerJ.AddSpline(splineJ);
        EditorUtility.SetDirty(containerJ);
        if (junctionVisualPrefab != null)
            SetupJunctionInstantiate(go, containerJ, junctionVisualPrefab,
                preset.junctionPosOffset, preset.junctionRotOffset);
        Debug.Log($"[SplitInPlace] Junction gap container added");

        // B — continuation container
        var containerB = Undo.AddComponent<SplineContainer>(go);
        containerB.RemoveSplineAt(0);
        containerB.AddSpline(splineB);
        EditorUtility.SetDirty(containerB);
        Debug.Log($"[SplitInPlace] Segment B applied ({splineB.Count} knots) — done.");

        return (source, containerJ, containerB);
    }

    // ══════════════════════════════════════════════════════════════
    // SPLIT LOGIC
    // ══════════════════════════════════════════════════════════════
    private void DoSplit()
    {
        var spline = _splitTarget.Splines[0];

        if (spline.Closed)    { Debug.LogWarning("[SplineTools] Closed splines not supported."); return; }
        if (spline.Count < 2) { Debug.LogWarning("[SplineTools] Need at least 2 knots."); return; }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Split Spline");

        GameObject original = _splitTarget.gameObject;
        var originalInstantiates = original.GetComponents<SplineInstantiate>();

        // ── Delegate arc-length math to shared utility ────────────
        float halfGap = 0f;
        if (_addJunction && _junctionPrefab != null)
        {
            float extent = SplineSplitUtility.MeasurePrefabXExtent(_junctionPrefab);
            halfGap = extent * 0.5f + _padding;
            Debug.Log($"[SplineTools] extent={extent:F4}  padding={_padding:F4}  halfGap={halfGap:F4}  totalGap={halfGap * 2f:F4}");
        }

        var split = SplineSplitUtility.ComputeSplit(_splitTarget, _splitT, halfGap, _subdivisions);
        if (!split.valid)
        {
            Debug.LogWarning("[SplineTools] Split produced degenerate segments. Reduce padding or increase subdivisions.");
            return;
        }

        var posA   = split.posA;
        var posB   = split.posB;
        var endA   = split.endA;
        var startB = split.startB;

        Debug.Log($"[SplineTools] posA={posA.Count}  posB={posB.Count}  halfGap={halfGap:F4}");

        var splineA = BuildSplineFromPositions(posA, _knotMode);
        var splineB = BuildSplineFromPositions(posB, _knotMode);

        Undo.RegisterCompleteObjectUndo(_splitTarget, "Split Spline");
        if (!_keepOriginal)
        {
            _splitTarget.RemoveSplineAt(0);
            _splitTarget.AddSpline(splineA);
            EditorUtility.SetDirty(_splitTarget);
        }

        if (_addJunction && _junctionPrefab != null)
        {
            var splineJunction = BuildSplineFromPositions(
                new List<float3> { endA, startB }, KnotTangentMode.Broken);

            var containerJ = Undo.AddComponent<SplineContainer>(original);
            containerJ.RemoveSplineAt(0);
            containerJ.AddSpline(splineJunction);
            EditorUtility.SetDirty(containerJ);
            SetupJunctionInstantiate(original, containerJ, _junctionPrefab, _junctionPosOffset, _junctionRotOffset);

            var containerB = Undo.AddComponent<SplineContainer>(original);
            containerB.RemoveSplineAt(0);
            containerB.AddSpline(splineB);
            EditorUtility.SetDirty(containerB);

            if (_copySplineInstantiate)
                CopySpecificInstantiates(originalInstantiates, original, containerB);

            // Optionally stamp RiverSegmentID with top/bottom path flags
            if (_addRiverSegmentID)
            {
                ApplySegmentIDFlags(original, _splitTarget,  _sideAIsLeftPath, _sideAIsRightPath);
                ApplySegmentIDFlags(original, containerB,    _sideBIsLeftPath, _sideBIsRightPath);
            }

            Debug.Log($"[SplineTools] Junction split done on same GO. halfGap={halfGap:F3}");
        }
        else if (_splitToSameObject)
        {
            _splitTarget.AddSpline(splineB);
            EditorUtility.SetDirty(_splitTarget);

            if (_copySplineInstantiate)
                CopySpecificInstantiates(originalInstantiates, original, _splitTarget);

            Debug.Log($"[SplineTools] Same-GO split done.");
        }
        else
        {
            var goB = new GameObject(original.name + "_B");
            Undo.RegisterCreatedObjectUndo(goB, "Split Spline");
            goB.transform.SetParent(original.transform.parent, false);
            goB.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
            goB.transform.localScale = original.transform.localScale;

            var containerB = goB.AddComponent<SplineContainer>();
            containerB.RemoveSplineAt(0);
            containerB.AddSpline(splineB);

            if (_copySplineInstantiate)
                CopySpecificInstantiates(originalInstantiates, goB, containerB);

            Selection.activeGameObject = goB;
            Debug.Log($"[SplineTools] Separate-GO split done.");
        }

        Undo.CollapseUndoOperations(undoGroup);

        // Refresh all SplineInstantiate components on the GO so they update immediately
        RefreshInstantiates();
    }

    // ══════════════════════════════════════════════════════════════
    // MERGE LOGIC
    // ══════════════════════════════════════════════════════════════
    private void DoMergeCrossObject()
    {
        if (_mergeTarget == null) return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Merge Splines");
        Undo.RegisterCompleteObjectUndo(_mergeTarget, "Merge Splines");

        Spline master = _mergeTarget.Spline;

        foreach (var source in _mergeSources)
        {
            if (source == null || source == _mergeTarget) continue;

            var src = source.Spline;
            for (int i = 0; i < src.Count; i++)
            {
                BezierKnot knot = src[i];
                // Convert from source local space to target local space
                float3 worldPos = source.transform.TransformPoint((Vector3)knot.Position);
                knot.Position   = _mergeTarget.transform.InverseTransformPoint(worldPos);
                master.Add(knot, src.GetTangentMode(i));
            }
            Debug.Log($"[SplineTools] Merged {src.Count} knots from '{source.name}' into '{_mergeTarget.name}'");
        }

        EditorUtility.SetDirty(_mergeTarget);
        DoSimplify(_mergeTarget);
        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log("[SplineTools] Cross-object merge complete.");
    }

    private void DoMergeSameObject(GameObject go, SplineContainer[] containers)
    {
        if (containers.Length < 2) return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Merge Spline Containers");

        var primary = containers[0];
        Undo.RegisterCompleteObjectUndo(primary, "Merge Spline Containers");

        for (int ci = 1; ci < containers.Length; ci++)
        {
            var src = containers[ci];

            if (_mergeSeparateSplines)
            {
                // Each spline from the source becomes a new spline in the primary container
                foreach (var srcSpline in src.Splines)
                {
                    var copy = new Spline();
                    for (int ki = 0; ki < srcSpline.Count; ki++)
                        copy.Add(srcSpline[ki], srcSpline.GetTangentMode(ki));
                    primary.AddSpline(copy);
                }
            }
            else
            {
                // Append all knots into the primary container's first spline
                var master = primary.Spline;
                foreach (var srcSpline in src.Splines)
                {
                    for (int ki = 0; ki < srcSpline.Count; ki++)
                        master.Add(srcSpline[ki], srcSpline.GetTangentMode(ki));
                }
            }

            // Remove the now-redundant SplineInstantiate that referenced this container
            var instComponents = go.GetComponents<SplineInstantiate>();
            foreach (var inst in instComponents)
            {
                if (inst.Container == src)
                    Undo.DestroyObjectImmediate(inst);
            }

            Undo.DestroyObjectImmediate(src);
            Debug.Log($"[SplineTools] Merged container [{ci}] into primary.");
        }

        EditorUtility.SetDirty(primary);
        DoSimplify(primary);
        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log("[SplineTools] Same-object merge complete.");

        // Refresh the GO field so the stale container refs don't linger
        _mergeGO = go;
        Repaint();
    }

    private void DoMergeSameContainer(SplineContainer container)
    {
        int splineCount = container.Splines.Count;
        if (splineCount < 2) return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Merge Splines In Container");
        Undo.RegisterCompleteObjectUndo(container, "Merge Splines In Container");

        var master = container.Splines[0];

        for (int si = 1; si < splineCount; si++)
        {
            var src = container.Splines[si];
            for (int ki = 0; ki < src.Count; ki++)
                master.Add(src[ki], src.GetTangentMode(ki));
        }

        // Remove all splines after the first
        for (int si = splineCount - 1; si >= 1; si--)
            container.RemoveSplineAt(si);

        EditorUtility.SetDirty(container);
        DoSimplify(container);
        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[SplineTools] Merged {splineCount} splines into one in '{container.name}'.");
    }

    // ══════════════════════════════════════════════════════════════
    // SIMPLIFY
    // ══════════════════════════════════════════════════════════════
    // ── Post-process section (shown for all merge modes) ─────────
    private void DrawPostProcessSection()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Post-Process", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _simplifyAfterMerge = EditorGUILayout.Toggle("Simplify", _simplifyAfterMerge);
        if (_simplifyAfterMerge)
        {
            EditorGUILayout.LabelField($"Current Knots: {GetCurrentKnotsForContext()}", EditorStyles.miniLabel);
            _simplifyTargetKnots    = EditorGUILayout.IntField ("Target Knots",      _simplifyTargetKnots);
            _simplifyManyPointsAuto = EditorGUILayout.Toggle   ("Making Points Auto", _simplifyManyPointsAuto);
        }
        if (EditorGUI.EndChangeCheck()) SavePrefs();
    }

    private int GetCurrentKnotsForContext()
    {
        int total = 0;
        switch (_mergeMode)
        {
            case MergeMode.CrossObject:
                if (_mergeTarget != null)
                    foreach (var s in _mergeTarget.Splines) total += s.Count;
                break;
            case MergeMode.SameGameObject:
                if (_mergeGO != null)
                    foreach (var c in _mergeGO.GetComponents<SplineContainer>())
                        foreach (var s in c.Splines) total += s.Count;
                break;
            case MergeMode.SameContainer:
                if (_mergeContainerTarget != null)
                    foreach (var s in _mergeContainerTarget.Splines) total += s.Count;
                break;
        }
        return total;
    }

    private void DoSimplify(SplineContainer container)
    {
        if (!_simplifyAfterMerge || container == null) return;

        int splineCount = container.Splines.Count;
        int totalBefore = 0, totalAfter = 0;

        var rebuilt = new List<Spline>(splineCount);
        for (int si = 0; si < splineCount; si++)
        {
            var src = container.Splines[si];
            totalBefore += src.Count;

            // Sample densely when Many Points Auto is on, otherwise use knot positions directly
            List<float3> pts;
            if (_simplifyManyPointsAuto)
            {
                int sampleCount = Mathf.Max(500, src.Count * 30);
                pts = new List<float3>(sampleCount + 1);
                for (int i = 0; i <= sampleCount; i++)
                    pts.Add(src.EvaluatePosition((float)i / sampleCount));
            }
            else
            {
                pts = new List<float3>(src.Count);
                for (int i = 0; i < src.Count; i++)
                    pts.Add(src[i].Position);
            }

            var reduced = SimplifyToTargetKnots(pts, _simplifyTargetKnots);
            totalAfter += reduced.Count;
            rebuilt.Add(BuildSplineFromPositions(reduced, _knotMode));
        }

        for (int si = splineCount - 1; si >= 0; si--)
            container.RemoveSplineAt(si);
        foreach (var s in rebuilt)
            container.AddSpline(s);

        EditorUtility.SetDirty(container);
        Debug.Log($"[SplineTools] Simplified '{container.name}': {totalBefore} → {totalAfter} knots.");
    }

    // Binary-search on RDP tolerance to hit a target knot count
    private static List<float3> SimplifyToTargetKnots(List<float3> pts, int targetKnots)
    {
        if (pts.Count <= targetKnots) return new List<float3>(pts);

        // Expand upper bound until result is below target
        float lo = 0f, hi = 0.001f;
        while (RamerDouglasPeucker(pts, hi).Count > targetKnots && hi < 100000f)
            hi *= 2f;

        var best    = new List<float3>(pts);
        int bestDiff = int.MaxValue;

        for (int iter = 0; iter < 32; iter++)
        {
            float mid    = (lo + hi) * 0.5f;
            var   result = RamerDouglasPeucker(pts, mid);
            int   diff   = Mathf.Abs(result.Count - targetKnots);

            if (diff < bestDiff) { bestDiff = diff; best = result; }
            if (diff == 0) break;

            if (result.Count > targetKnots) lo = mid;
            else                            hi = mid;
        }

        return best;
    }

    private static List<float3> RamerDouglasPeucker(List<float3> pts, float tolerance)
    {
        if (pts.Count <= 2) return new List<float3>(pts);

        float  maxDist  = 0f;
        int    maxIndex = 0;
        float3 a = pts[0], b = pts[pts.Count - 1];

        for (int i = 1; i < pts.Count - 1; i++)
        {
            float d = PointToSegmentDistance(pts[i], a, b);
            if (d > maxDist) { maxDist = d; maxIndex = i; }
        }

        if (maxDist > tolerance)
        {
            var left  = RamerDouglasPeucker(pts.GetRange(0, maxIndex + 1),              tolerance);
            var right = RamerDouglasPeucker(pts.GetRange(maxIndex, pts.Count - maxIndex), tolerance);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        return new List<float3> { a, b };
    }

    private static float PointToSegmentDistance(float3 p, float3 a, float3 b)
    {
        float3 ab  = b - a;
        float  len = math.length(ab);
        if (len < 1e-6f) return math.length(p - a);
        float  t   = math.clamp(math.dot(p - a, ab) / (len * len), 0f, 1f);
        return math.length(p - (a + ab * t));
    }

    // ══════════════════════════════════════════════════════════════
    // SHARED HELPERS
    // ══════════════════════════════════════════════════════════════
    public static void SetupJunctionInstantiate(
        GameObject go, SplineContainer container, GameObject prefab,
        Vector3 posOffset, Vector3 rotOffset)
    {
        var si = Undo.AddComponent<SplineInstantiate>(go);
        si.Container = container;

        var so = new SerializedObject(si);
        so.Update();

        so.FindProperty("m_Method").enumValueIndex = 0; // InstanceCount
        so.FindProperty("m_Spacing").vector2Value  = new Vector2(1f, 1f);

        if (posOffset != Vector3.zero)
        {
            var pos = so.FindProperty("m_PositionOffset");
            pos.FindPropertyRelative("setup").intValue   = 1;
            pos.FindPropertyRelative("min").vector3Value = posOffset;
            pos.FindPropertyRelative("max").vector3Value = posOffset;
        }

        if (rotOffset != Vector3.zero)
        {
            var rot = so.FindProperty("m_RotationOffset");
            rot.FindPropertyRelative("setup").intValue     = 1;
            rot.FindPropertyRelative("min").vector3Value   = rotOffset;
            rot.FindPropertyRelative("max").vector3Value   = rotOffset;
        }

        var items = so.FindProperty("m_ItemsToInstantiate");
        items.ClearArray();
        items.InsertArrayElementAtIndex(0);
        var item = items.GetArrayElementAtIndex(0);
        item.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
        item.FindPropertyRelative("Probability").floatValue      = 1f;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(si);
    }

    private static void CopySpecificInstantiates(
        SplineInstantiate[] sources, GameObject dest, SplineContainer destContainer)
    {
        if (sources == null || sources.Length == 0) return;

        foreach (var src in sources)
        {
            var copy  = Undo.AddComponent<SplineInstantiate>(dest);
            var srcSO = new SerializedObject(src);
            var dstSO = new SerializedObject(copy);

            srcSO.Update();
            dstSO.Update();

            var srcProp = srcSO.GetIterator();
            var dstProp = dstSO.GetIterator();
            srcProp.NextVisible(true);
            dstProp.NextVisible(true);

            while (srcProp.NextVisible(false))
            {
                dstProp.NextVisible(false);
                dstSO.CopyFromSerializedProperty(srcProp);
            }

            dstSO.ApplyModifiedPropertiesWithoutUndo();
            copy.Container = destContainer;
            EditorUtility.SetDirty(copy);
        }
    }

    private float MeasurePrefabXExtent(GameObject prefab)
        => SplineSplitUtility.MeasurePrefabXExtent(prefab);

    // Adds or updates a RiverSegmentID on the GO for the given SplineContainer,
    // stamping the top/bottom path flags from the split junction UI.
    private static void ApplySegmentIDFlags(
        GameObject go, SplineContainer container, bool isLeft, bool isRight)
    {
        // Reuse an existing RiverSegmentID that isn't already paired with a different container,
        // otherwise add a new one.
        var existing = go.GetComponents<RiverSegmentID>();
        RiverSegmentID seg = existing.Length > 0 ? existing[0] : Undo.AddComponent<RiverSegmentID>(go);

        var so = new SerializedObject(seg);
        so.Update();
        so.FindProperty("isLeftPath").boolValue  = isLeft;
        so.FindProperty("isRightPath").boolValue = isRight;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(seg);
    }

    private static Spline BuildSplineFromPositions(List<float3> positions, KnotTangentMode knotMode)
    {
        var spline = new Spline();

        for (int i = 0; i < positions.Count; i++)
        {
            float3 pos     = positions[i];
            float3 tangIn  = float3.zero;
            float3 tangOut = float3.zero;

            if (i > 0 && i < positions.Count - 1)
            {
                tangIn  = (pos - positions[i - 1]) * 0.333f;
                tangOut = (positions[i + 1] - pos) * 0.333f;
            }
            else if (i == 0 && positions.Count > 1)
            {
                tangOut = (positions[1] - pos) * 0.333f;
                tangIn  = -tangOut;
            }
            else if (i == positions.Count - 1 && positions.Count > 1)
            {
                tangIn  = (pos - positions[i - 1]) * 0.333f;
                tangOut = -tangIn;
            }

            bool isEndKnot = (i == 0 || i == positions.Count - 1);
            TangentMode mode = knotMode switch
            {
                KnotTangentMode.AllAutoSmooth    => TangentMode.AutoSmooth,
                KnotTangentMode.EndsBrokenMidAuto => isEndKnot ? TangentMode.Broken : TangentMode.AutoSmooth,
                _                                => TangentMode.Broken,
            };

            spline.Add(new BezierKnot(pos, -tangIn, tangOut, quaternion.identity), mode);
        }

        return spline;
    }

    private static void DrawHeader(string title)
    {
        var style = new GUIStyle(EditorStyles.largeLabel)
            { fontSize = 14, fontStyle = FontStyle.Bold };
        EditorGUILayout.LabelField(title, style);
        EditorGUILayout.Space(2);
    }

    private void OnSelectionChange() => Repaint();
}
#endif
