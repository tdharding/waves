using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
    const int    MaxTargetSize = 4096;
    const float  PanelStartWidth = 320f;

    [SerializeField] CinemachineVirtualCameraBase watched;

    Camera        previewCam;
    RenderTexture target;
    GUIStyle      messageStyle;

    // The view is drawn in IMGUI, the panel in UI Toolkit: Cinemachine's own editors are UI Toolkit
    // editors, and drawn through IMGUI they fall back to bare default fields.
    IMGUIContainer viewArea;
    ScrollView     panel;

    // What the panel was built for, so it can tell when it no longer matches the camera.
    CinemachineVirtualCameraBase panelFor;
    readonly List<Component>     panelComponents = new();
    readonly List<FloatField>    viewMoveFields  = new();

    // The renderer index lives in a serialized field with no public getter, so it costs a
    // SerializedObject to read. Cached against the camera it was read from.
    int cachedRendererIndex = -1;
    UniversalAdditionalCameraData cachedRendererSource;

    [MenuItem("Tools/Waves/Cinemachine View")]
    static void Open()
    {
        var window = GetWindow<CinemachineViewWindow>("Cm View");
        window.minSize = new Vector2(180f, 140f);
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

        // Scaled displays draw more pixels than points, so the texture is sized in pixels or the
        // view comes out soft.
        float perPoint = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
        int width  = Mathf.Clamp(Mathf.RoundToInt(view.width  * perPoint), 4, MaxTargetSize);
        int height = Mathf.Clamp(Mathf.RoundToInt(view.height * perPoint), 4, MaxTargetSize);

        EnsureTarget(width, height);
        EnsureRig();
        if (previewCam == null || target == null) return false;

        CopySettingsFrom(SourceCamera());
        bool isolated = PlaceRigForWatched();
        ApplyWatchedState(isolated);

        previewCam.targetTexture = target;
        previewCam.aspect        = (float)width / height;

        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (RenderPipeline.SupportsRenderRequest(previewCam, request))
            RenderPipeline.SubmitRenderRequest(previewCam, request);
        else
            previewCam.Render();

        return true;
    }

    void EnsureTarget(int width, int height)
    {
        if (target != null && target.width == width && target.height == height) return;

        ReleaseTarget();

        var readWrite = QualitySettings.activeColorSpace == ColorSpace.Linear
            ? RenderTextureReadWrite.sRGB
            : RenderTextureReadWrite.Default;

        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, readWrite)
        {
            name         = "Cinemachine View Target",
            hideFlags    = HideFlags.HideAndDontSave,
            antiAliasing = 1
        };
        target.Create();
    }

    // The hidden camera is disabled on purpose: it must never join the game's rendering, only render
    // when this window asks it to. It is rebuilt whenever a scene load takes it away.
    void EnsureRig()
    {
        if (previewCam != null) return;

        var go = EditorUtility.CreateGameObjectWithHideFlags(
            "Cinemachine View Preview Camera", HideFlags.HideAndDontSave, typeof(Camera));

        previewCam            = go.GetComponent<Camera>();
        previewCam.enabled    = false;
        previewCam.cameraType = CameraType.Game;
        previewCam.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;
    }

    // Prefab Mode keeps the open prefab in a preview scene of its own, which cameras in the open level
    // cannot see - left there, the rig would frame the LEVEL from the prefab camera's position, which
    // is exactly the wrong picture. Moving it into that scene and pointing it at that scene (the same
    // pair of steps Unity's own PreviewRenderUtility takes) is what makes it render the prefab.
    // Returns true when the watched camera lives in such a scene.
    bool PlaceRigForWatched()
    {
        var stage = PrefabStageUtility.GetPrefabStage(watched.gameObject);
        var scene = stage != null ? stage.scene : watched.gameObject.scene;
        bool isolated = scene.IsValid() && EditorSceneManager.IsPreviewScene(scene);

        if (scene.IsValid() && previewCam.gameObject.scene != scene)
            SceneManager.MoveGameObjectToScene(previewCam.gameObject, scene);

        previewCam.scene = isolated ? scene : default;
        return isolated;
    }

    Camera SourceCamera()
    {
        if (watched != null)
        {
            var brain = CinemachineCore.FindPotentialTargetBrain(watched);
            if (brain != null && brain.OutputCamera != null) return brain.OutputCamera;
        }
        return Camera.main;
    }

    // Matching the game camera's own settings is what makes this a preview of the game rather than of
    // a bare camera - same layers, same clear, same post stack and renderer.
    void CopySettingsFrom(Camera source)
    {
        if (source == null) return;

        previewCam.clearFlags          = source.clearFlags;
        previewCam.backgroundColor     = source.backgroundColor;
        previewCam.cullingMask         = source.cullingMask;
        previewCam.useOcclusionCulling = source.useOcclusionCulling;
        previewCam.allowHDR            = source.allowHDR;
        previewCam.allowMSAA           = source.allowMSAA;
        previewCam.depthTextureMode    = source.depthTextureMode;

        if (!source.TryGetComponent(out UniversalAdditionalCameraData sourceData)) return;

        var data = previewCam.GetUniversalAdditionalCameraData();
        data.renderType           = CameraRenderType.Base;
        data.renderShadows        = sourceData.renderShadows;
        data.renderPostProcessing = sourceData.renderPostProcessing;
        data.antialiasing         = sourceData.antialiasing;
        data.antialiasingQuality  = sourceData.antialiasingQuality;
        data.volumeLayerMask      = sourceData.volumeLayerMask;
        data.volumeTrigger        = sourceData.volumeTrigger;
        data.requiresDepthTexture = sourceData.requiresDepthTexture;
        data.requiresColorTexture = sourceData.requiresColorTexture;
        data.SetRenderer(RendererIndexOf(sourceData));
    }

    int RendererIndexOf(UniversalAdditionalCameraData data)
    {
        if (ReferenceEquals(data, cachedRendererSource)) return cachedRendererIndex;

        var property = new SerializedObject(data).FindProperty("m_RendererIndex");
        cachedRendererIndex  = property != null ? property.intValue : -1;
        cachedRendererSource = data;
        return cachedRendererIndex;
    }

    void ApplyWatchedState(bool isolated)
    {
        // An enabled vcam has its state refreshed every frame by the brain, in edit mode as well as
        // play mode. A disabled one never updates - and neither does one in a prefab stage, where
        // there is no brain to drive it - so in both cases the transform is the only truth left.
        bool live  = watched.isActiveAndEnabled && !isolated;
        var  state = watched.State;

        var position = live ? state.GetFinalPosition()    : watched.transform.position;
        var rotation = live ? state.GetFinalOrientation() : watched.transform.rotation;
        previewCam.transform.SetPositionAndRotation(position, rotation);

        var lens = live ? state.Lens : LensOf(watched);
        if (lens.FieldOfView <= 0.01f) lens = LensSettings.Default;

        previewCam.orthographic          = lens.Orthographic;
        previewCam.nearClipPlane         = Mathf.Max(0.001f, lens.NearClipPlane);
        previewCam.farClipPlane          = Mathf.Max(previewCam.nearClipPlane + 0.01f, lens.FarClipPlane);
        previewCam.orthographicSize      = Mathf.Max(0.001f, lens.OrthographicSize);
        previewCam.usePhysicalProperties = lens.IsPhysicalCamera;

        if (lens.IsPhysicalCamera)
        {
            var physical = lens.PhysicalProperties;
            previewCam.sensorSize     = physical.SensorSize;
            previewCam.gateFit        = physical.GateFit;
            previewCam.focalLength    = Camera.FieldOfViewToFocalLength(lens.FieldOfView, physical.SensorSize.y);
            previewCam.lensShift      = physical.LensShift;
            previewCam.focusDistance  = physical.FocusDistance;
            previewCam.iso            = physical.Iso;
            previewCam.shutterSpeed   = physical.ShutterSpeed;
            previewCam.aperture       = physical.Aperture;
            previewCam.bladeCount     = physical.BladeCount;
            previewCam.curvature      = physical.Curvature;
            previewCam.barrelClipping = physical.BarrelClipping;
            previewCam.anamorphism    = physical.Anamorphism;
        }
        else
        {
            previewCam.fieldOfView = Mathf.Clamp(lens.FieldOfView, 1f, 179f);
        }
    }

    static LensSettings LensOf(CinemachineVirtualCameraBase cam)
        => cam is CinemachineCamera cinemachineCamera ? cinemachineCamera.Lens : LensSettings.Default;

    // -------------------------------------------------------------------------
    // Teardown
    // -------------------------------------------------------------------------

    void ReleaseRig()
    {
        if (previewCam != null)
        {
            previewCam.targetTexture = null;
            DestroyImmediate(previewCam.gameObject);
            previewCam = null;
        }

        cachedRendererSource = null;
        cachedRendererIndex  = -1;

        ReleaseTarget();
    }

    void ReleaseTarget()
    {
        if (target == null) return;

        target.Release();
        DestroyImmediate(target);
        target = null;
    }
}
