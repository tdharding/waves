using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// A dockable window that keeps showing what one Cinemachine camera sees - in edit mode and in play
// mode, whichever camera is live. It never touches the game's own camera: a hidden throwaway camera
// is parked on the chosen vcam's state each repaint and rendered into a texture of its own.
public class CinemachineViewWindow : EditorWindow
{
    // Which camera is being watched has to outlive a domain reload, entering play mode (the scene is
    // reloaded, so the object reference dies) and quitting Unity, so it is stored as a global id
    // rather than only as a serialized reference.
    const string WatchedIdKey = "Waves.CinemachineView.WatchedId";
    const int    MaxTargetSize = 4096;

    [SerializeField] CinemachineVirtualCameraBase watched;

    Camera        previewCam;
    RenderTexture target;
    GUIStyle      messageStyle;

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
    }

    void OnDisable()
    {
        EditorApplication.update               -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorSceneManager.sceneOpened         -= OnSceneOpened;

        ReleaseRig();
    }

    // Driving the repaint off the editor tick is what makes the view live rather than redrawing only
    // when the mouse crosses the window. The editor ticks in play mode too, so one path covers both.
    void Tick()
    {
        if (watched != null) Repaint();
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

    void OnGUI()
    {
        DrawToolbar();

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
