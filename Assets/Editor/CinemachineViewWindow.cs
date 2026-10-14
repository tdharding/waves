using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// A dockable window that keeps showing what one Cinemachine camera sees - in edit mode and in play
// mode, whichever camera is live. It never touches the game's own camera: a hidden throwaway camera
// is parked on the chosen vcam's state each repaint and rendered into a texture of its own.
// Beside the view sits a panel with the watched camera's own inspectors (its Transform and every
// Cinemachine component on it), so its settings can be adjusted while watching the result.
public class CinemachineViewWindow : EditorWindow
{
    // Which camera is being watched has to outlive a domain reload, entering play mode (the scene is
    // reloaded, so the object reference dies) and quitting Unity, so it is stored as a global id
    // rather than only as a serialized reference.
    const string WatchedIdKey = "Waves.CinemachineView.WatchedId";
    const string SplitKey     = "Waves.CinemachineView.Split";
    const string ViewMoveKey  = "Waves.CinemachineView.ViewMove";
    const float  PanelStartWidth = 320f;

    [SerializeField] CinemachineVirtualCameraBase watched;

    CinemachinePreviewRig rig;
    RenderTexture         target;
    GUIStyle              messageStyle;

    // The view is drawn in IMGUI, the panel in UI Toolkit: Cinemachine's own editors are UI Toolkit
    // editors, and drawn through IMGUI they fall back to bare default fields.
    IMGUIContainer viewArea;
    ScrollView     panel;

    // What the panel was built for, so it can tell when it no longer matches the camera.
    CinemachineVirtualCameraBase panelFor;
    readonly List<Component>     panelComponents = new();
    readonly List<FloatField>    viewMoveFields  = new();

    [MenuItem("Tools/Camera/Cinemachine View", false, 701)]
    static void Open()
    {
        var window = GetWindow<CinemachineViewWindow>("Cm View");
        window.minSize = new Vector2(180f, 140f);
    }

    // Opens the window (or brings it forward) watching the given camera.
    public static void Watch(CinemachineVirtualCameraBase cam)
    {
        var window = GetWindow<CinemachineViewWindow>("Cm View");
        window.minSize = new Vector2(180f, 140f);
        window.SetWatched(cam);
    }

    void OnEnable()
    {
        titleContent = new GUIContent("Cm View");
        if (watched == null) RestoreWatched();

        EditorApplication.update               += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorSceneManager.sceneOpened         += OnSceneOpened;
        ObjectChangeEvents.changesPublished    += OnObjectChanges;
        Undo.undoRedoPerformed                 += OnUndoRedo;
    }

    void OnDisable()
    {
        EditorApplication.update               -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorSceneManager.sceneOpened         -= OnSceneOpened;
        ObjectChangeEvents.changesPublished    -= OnObjectChanges;
        Undo.undoRedoPerformed                 -= OnUndoRedo;

        ReleaseRig();
    }

    // Driving the repaint off the editor tick is what makes the view live rather than redrawing only
    // when the mouse crosses the window. The editor ticks in play mode too, so one path covers both.
    void Tick()
    {
        if (panel != null && PanelIsStale()) RebuildPanel();

        if (watched == null) return;
        if (viewArea != null) viewArea.MarkDirtyRepaint();
        else                  Repaint();
    }

    // Components added, removed or reordered on the camera (including through the panel's own
    // title bars) change what the panel should hold.
    void OnObjectChanges(ref ObjectChangeEventStream stream)
    {
        if (panel == null || watched == null) return;
        if (!panelComponents.SequenceEqual(PanelComponentsOf(watched))) RebuildPanel();
    }

    void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
        {
            ReleaseRig();          // the hidden camera does not survive the scene reload
            RestoreWatched();
        }
    }

    void OnSceneOpened(Scene scene, OpenSceneMode mode) => RestoreWatched();

    // -------------------------------------------------------------------------
    // Which camera to watch
    // -------------------------------------------------------------------------

    void SetWatched(CinemachineVirtualCameraBase cam)
    {
        watched = cam;

        if (cam == null)
            EditorPrefs.DeleteKey(WatchedIdKey);
        else
            EditorPrefs.SetString(WatchedIdKey, GlobalObjectId.GetGlobalObjectIdSlow(cam).ToString());

        Repaint();
    }

    void RestoreWatched()
    {
        if (watched != null) return;

        var id = EditorPrefs.GetString(WatchedIdKey, string.Empty);
        if (string.IsNullOrEmpty(id) || !GlobalObjectId.TryParse(id, out var parsed)) return;

        watched = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as CinemachineVirtualCameraBase;
    }

    // -------------------------------------------------------------------------
    // GUI
    // -------------------------------------------------------------------------

    // The toolbar spans the top; below it the view and the panel share a splitter, the panel on the
    // right. The split width is remembered through the window's view data.
    void CreateGUI()
    {
        var root = rootVisualElement;
        root.Add(new IMGUIContainer(DrawToolbar));

        var split = new TwoPaneSplitView(1, PanelStartWidth, TwoPaneSplitViewOrientation.Horizontal)
        {
            viewDataKey = SplitKey
        };
        split.style.flexGrow = 1f;

        viewArea = new IMGUIContainer(DrawView);
        viewArea.style.flexGrow = 1f;

        panel = new ScrollView(ScrollViewMode.Vertical);

        split.Add(viewArea);
        split.Add(panel);
        root.Add(split);

        RebuildPanel();
    }

    void DrawView()
    {
        var view = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (watched == null)
        {
            DrawMessage(view, "Pick a Cinemachine camera to watch.");
            return;
        }

        if (EditorUtility.IsPersistent(watched))
        {
            DrawMessage(view, "That camera is on a prefab asset. Open the prefab and pick the camera "
                            + "from the prefab's own hierarchy.");
            return;
        }

        if (Event.current.type != EventType.Repaint) return;

        if (!TryRender(view))
        {
            DrawMessage(view, "Nothing to show yet.");
            return;
        }

        GUI.DrawTexture(view, target, ScaleMode.StretchToFill, false);
    }

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            EditorGUI.BeginChangeCheck();
            var picked = (CinemachineVirtualCameraBase)EditorGUILayout.ObjectField(
                watched, typeof(CinemachineVirtualCameraBase), true, GUILayout.MinWidth(80f));
            if (EditorGUI.EndChangeCheck()) SetWatched(picked);

            var sequence = CameraSequenceFogPreview.SequenceFor(watched);
            if (sequence != null)
                GUILayout.Label(new GUIContent($"Fog: {sequence.name} ({CameraSequenceFogPreview.CentreFor(sequence, watched)})",
                    "This camera is a shot in that Camera Sequence, so the view wears its Override Fog."),
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
        }
    }

    void DrawMessage(Rect rect, string text)
    {
        messageStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap  = true
        };
        EditorGUI.LabelField(rect, text, messageStyle);
    }

    // -------------------------------------------------------------------------
    // Panel - the watched camera's own inspectors
    // -------------------------------------------------------------------------

    bool PanelIsStale()
    {
        // Identity, not Unity's null-equality: a new camera after a scene load must count as a change.
        if (!ReferenceEquals(panelFor, watched)) return true;

        foreach (var component in panelComponents)
            if (component == null) return true;    // destroyed under the panel

        return false;
    }

    void RebuildPanel()
    {
        panel.Clear();
        panelComponents.Clear();
        viewMoveFields.Clear();
        panelFor = watched;

        if (watched == null) return;

        panel.Add(ViewMoveSection());

        panelComponents.AddRange(PanelComponentsOf(watched));
        foreach (var component in panelComponents)
            panel.Add(ComponentSection(component));
    }

    // The Transform plus every Cinemachine component on the camera: the camera itself, its
    // position/rotation components, noise and extensions.
    static List<Component> PanelComponentsOf(CinemachineVirtualCameraBase cam)
    {
        var list = new List<Component>();
        foreach (var component in cam.GetComponents<Component>())
        {
            if (component == null) continue;                                         // missing script
            if ((component.hideFlags & HideFlags.HideInInspector) != 0) continue;
            if (component is Transform || IsCinemachine(component)) list.Add(component);
        }
        return list;
    }

    static bool IsCinemachine(Component component)
    {
        var ns = component.GetType().Namespace;
        return ns != null && ns.StartsWith("Unity.Cinemachine");
    }

    // The same title bar the Inspector draws (fold, enable toggle, context menu), over the
    // component's own inspector. The fold state is shared with the Inspector window.
    static VisualElement ComponentSection(Component component)
    {
        var section   = new VisualElement();
        var inspector = new InspectorElement(component);

        section.Add(new IMGUIContainer(() =>
        {
            if (component == null) return;

            bool expanded = InternalEditorUtility.GetIsInspectorExpanded(component);
            bool picked   = EditorGUILayout.InspectorTitlebar(expanded, component, true);
            if (picked != expanded) InternalEditorUtility.SetIsInspectorExpanded(component, picked);

            inspector.style.display = picked ? DisplayStyle.Flex : DisplayStyle.None;
        }));
        section.Add(inspector);

        inspector.style.display = InternalEditorUtility.GetIsInspectorExpanded(component)
            ? DisplayStyle.Flex
            : DisplayStyle.None;

        return section;
    }

    // -------------------------------------------------------------------------
    // View Move - moving the camera along the way it is looking
    // -------------------------------------------------------------------------

    // One field per view axis: X across the view, Y up the view, Z towards what the camera points
    // at. Each field reads how far the camera has been moved along that axis since the panel was
    // built; dragging its label or typing a number moves the camera by the change. The axes are
    // re-read on every change, so after turning the camera they follow the new view.
    VisualElement ViewMoveSection()
    {
        var foldout = new Foldout { text = "View Move", viewDataKey = ViewMoveKey };
        foldout.Add(ViewMoveField("X  Right",   Vector3.right));
        foldout.Add(ViewMoveField("Y  Up",      Vector3.up));
        foldout.Add(ViewMoveField("Z  Forward", Vector3.forward));
        return foldout;
    }

    FloatField ViewMoveField(string label, Vector3 viewAxis)
    {
        var field = new FloatField(label)
        {
            tooltip = "Drag the label to move the camera along its own view. Shift = faster, Alt = slower."
        };
        field.AddToClassList(BaseField<float>.alignedFieldUssClassName);
        viewMoveFields.Add(field);

        field.RegisterValueChangedCallback(evt =>
        {
            float amount = evt.newValue - evt.previousValue;
            if (watched == null || amount == 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;

            var t = watched.transform;
            Undo.RecordObject(t, "Move Camera Along View");
            t.position += ViewRotation() * viewAxis * amount;
        });

        return field;
    }

    // An undo puts the camera back without the fields knowing, so they go back to 0 rather than
    // show a distance the camera is no longer at.
    void OnUndoRedo()
    {
        foreach (var field in viewMoveFields) field.SetValueWithoutNotify(0f);
    }

    // The way the camera is looking as the view shows it - noise left out, so a shaking camera still
    // moves along steady axes. Same live/not-live rule as ApplyWatchedState.
    Quaternion ViewRotation()
    {
        var stage = PrefabStageUtility.GetPrefabStage(watched.gameObject);
        var scene = stage != null ? stage.scene : watched.gameObject.scene;
        bool isolated = scene.IsValid() && EditorSceneManager.IsPreviewScene(scene);
        bool live     = watched.isActiveAndEnabled && !isolated;

        return live ? watched.State.RawOrientation : watched.transform.rotation;
    }

    // -------------------------------------------------------------------------
    // Rendering
    // -------------------------------------------------------------------------

    bool TryRender(Rect view)
    {
        if (view.width < 4f || view.height < 4f) return false;

        CinemachinePreviewRig.EnsureTarget(ref target, view, "Cinemachine View Target");
        rig ??= new CinemachinePreviewRig();

        // A Camera Sequence shot camera shows that sequence's fog, when it overrides the world's.
        var sequence = CameraSequenceFogPreview.SequenceFor(watched);
        var saved    = default(CameraSequenceFogPreview.Saved);
        try
        {
            return rig.Render(watched, target, previewCam =>
            {
                if (sequence != null) saved = CameraSequenceFogPreview.Wear(sequence, watched, previewCam);
            });
        }
        finally
        {
            CameraSequenceFogPreview.Restore(saved);
        }
    }

    // -------------------------------------------------------------------------
    // Teardown
    // -------------------------------------------------------------------------

    void ReleaseRig()
    {
        rig?.Dispose();
        rig = null;
        CinemachinePreviewRig.ReleaseTarget(ref target);
    }
}
