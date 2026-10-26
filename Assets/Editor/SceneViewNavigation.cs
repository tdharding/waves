using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

// Scroll-wheel zoom in the Scene view heads toward a fixed pivot and slows to a crawl as it nears
// it. With this on, every scroll first moves the pivot to whatever surface sits at the centre of
// the Scene view, keeping the camera exactly where it is, so the zoom always has room to travel.
// Alt + left-drag orbits around that same point. If the centre is looking at nothing, the pivot is
// left alone.
//
// The surface is whatever is drawn at the centre — the object a click there would select — hit on
// its own mesh, a skinned one in the pose it is showing. Unity's HandleUtility.PlaceObject can't do
// this: it only sees colliders, and when they miss it lands on the grid plane, so it looked straight
// through anything without a collider, stopped on invisible collision meshes, and sent the pivot to
// y = 0 when the centre was on nothing at all.
//
// Focusing an object (F, Edit > Frame Selected, double-clicking it in the Hierarchy) wins: the
// pivot stays on the focused object for zoom and orbit until the view is panned or flown away.
[InitializeOnLoad]
public static class SceneViewNavigation
{
    // Pref keys kept from when this was Zoom To Centre, so existing toggles survive the rename.
    const string EnabledPref = "Waves.SceneZoomToCentre.Enabled";
    const string CrosshairPref = "Waves.SceneZoomToCentre.Crosshair";
    const string ScrollSpeedPref = "Waves.SceneViewNavigation.ScrollSpeed";

    public const float MinScrollSpeed = 0.1f;
    public const float MaxScrollSpeed = 5f;

    const float CrosshairArm = 6f;
    const float CrosshairThickness = 1f;

    // Closer than this and the hit is effectively the camera itself; retargeting would stall zoom.
    const float MinHitDistance = 0.0001f;

    // Pivot movement smaller than this is treated as no movement.
    const float PivotMoveEpsilon = 0.00001f;

    // Finding what is drawn at the centre renders a small picking pass. Unity refuses one while the
    // Scene view's GUI is being laid out or drawn ("GUI Window tried to begin rendering…"), so the
    // crosshair asks on input events only — moves, drags, clicks, scrolls, keys — at most this often
    // (seconds), and always as a drag or key lets go, so wherever the view comes to rest is answered.
    const double CrosshairCheckInterval = 0.1;

    // Things drawn at the centre that have no surface to hit (a gizmo icon, a particle effect) are
    // passed over to whatever is behind them, up to this many deep.
    const int MaxPickTries = 3;

    class ViewState
    {
        public Vector3 lastPivot;
        public bool hasLastPivot;
        public bool mouseHeld;
        public bool arrowHeld;
        // True after a focus: leave the pivot on the focused object.
        public bool focusHold;

        // The crosshair's last answer, and when it was asked.
        public bool centreOnSurface;
        public double centreCheckedAt = double.NegativeInfinity;
    }

    static readonly Dictionary<SceneView, ViewState> States = new Dictionary<SceneView, ViewState>();

    static readonly List<GameObject> PassedOver = new List<GameObject>();
    static readonly List<Collider> Colliders = new List<Collider>();

    // A skinned mesh is hit in the pose it is showing: it is snapshotted into this, reused each time.
    static Mesh s_PosedSnapshot;

    // Unity's own ray-against-mesh test. It is internal, so it is looked up by name, once.
    delegate bool IntersectRayMeshFn(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit);
    static IntersectRayMeshFn s_IntersectRayMesh;
    static bool s_IntersectRayMeshLookedUp;

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPref, true);
        set => EditorPrefs.SetBool(EnabledPref, value);
    }

    public static bool ShowCrosshair
    {
        get => EditorPrefs.GetBool(CrosshairPref, true);
        set
        {
            EditorPrefs.SetBool(CrosshairPref, value);
            SceneView.RepaintAll();
        }
    }

    // Multiplies each scroll-wheel zoom step. 1 is Unity's own speed.
    public static float ScrollSpeed
    {
        get => Mathf.Clamp(EditorPrefs.GetFloat(ScrollSpeedPref, 1f), MinScrollSpeed, MaxScrollSpeed);
        set => EditorPrefs.SetFloat(ScrollSpeedPref, Mathf.Clamp(value, MinScrollSpeed, MaxScrollSpeed));
    }

    static SceneViewNavigation()
    {
        // beforeSceneGui runs ahead of the Scene view's own zoom handling, so the new pivot is in
        // place by the time this same scroll event is turned into a zoom.
        SceneView.beforeSceneGui -= OnBeforeSceneGui;
        SceneView.beforeSceneGui += OnBeforeSceneGui;
        SceneView.duringSceneGui -= OnDuringSceneGui;
        SceneView.duringSceneGui += OnDuringSceneGui;
        AssemblyReloadEvents.beforeAssemblyReload -= DropPosedSnapshot;
        AssemblyReloadEvents.beforeAssemblyReload += DropPosedSnapshot;
    }

    static void DropPosedSnapshot()
    {
        if (s_PosedSnapshot != null)
            Object.DestroyImmediate(s_PosedSnapshot);
        s_PosedSnapshot = null;
    }

    // Marks the centre of the Scene view, where the zoom ray is cast.
    static void OnDuringSceneGui(SceneView sceneView)
    {
        if (!ShowCrosshair || Event.current.type != EventType.Repaint)
            return;

        Camera cam = sceneView.camera;
        if (cam == null)
            return;

        Vector2 c = HandleUtility.WorldToGUIPoint(cam.transform.position + cam.transform.forward);
        float half = CrosshairThickness * 0.5f;

        // White when the zoom has a surface to head for, black when the centre is looking at nothing.
        Color colour = GetState(sceneView).centreOnSurface ? Color.white : Color.black;

        Handles.BeginGUI();
        EditorGUI.DrawRect(new Rect(c.x - CrosshairArm, c.y - half, CrosshairArm * 2f, CrosshairThickness), colour);
        EditorGUI.DrawRect(new Rect(c.x - half, c.y - CrosshairArm, CrosshairThickness, CrosshairArm * 2f), colour);
        Handles.EndGUI();
    }

    // Distance along the view direction to the surface drawn at the centre of the Scene view.
    static bool TryGetCentreHit(Camera cam, out float hitDistance)
    {
        hitDistance = 0f;
        Vector3 camPos = cam.transform.position;
        Vector2 centreGui = HandleUtility.WorldToGUIPoint(camPos + cam.transform.forward);

        if (!TryHitDrawnSurface(centreGui, out Vector3 hit))
            return false;

        hitDistance = Vector3.Dot(hit - camPos, cam.transform.forward);
        return hitDistance >= MinHitDistance;
    }

    // The nearest thing drawn at this point of the Scene view — what a click there would select —
    // hit on its own surface. Picking hidden or unpickable objects is off in Unity, and they are
    // passed over here too. So are fog sheets: they hover over everything, so the pivot would land
    // on the fog instead of the ground under it. They don't count against MaxPickTries, since
    // several zone sheets can stack up at the centre.
    internal static bool TryHitDrawnSurface(Vector2 guiPoint, out Vector3 point)
    {
        point = default;
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
        PassedOver.Clear();

        int tries = 0;
        while (tries < MaxPickTries)
        {
            GameObject drawn = PassedOver.Count == 0
                ? HandleUtility.PickGameObject(guiPoint, false)
                : HandleUtility.PickGameObject(guiPoint, false, PassedOver.ToArray(), null);
            if (drawn == null)
                return false;
            PassedOver.Add(drawn);
            if (drawn.TryGetComponent(out FogSheetMesh _))
                continue;
            if (TryHitObject(ray, drawn, out point))
                return true;
            tries++;
        }
        return false;
    }

    // The ray against one object: the mesh it draws, or failing that its own colliders (a terrain
    // has no mesh to hit; a shader can draw a shape the mesh doesn't have).
    internal static bool TryHitObject(Ray ray, GameObject go, out Vector3 point)
    {
        if (go.TryGetComponent(out SkinnedMeshRenderer skinned))
        {
            if (TryHitSkinned(ray, skinned, out point))
                return true;
        }
        else if (go.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && go.TryGetComponent(out Renderer renderer))
        {
            // A statically batched renderer draws its share of one combined mesh that is already in
            // world space (play mode only).
            Matrix4x4 toWorld = renderer.isPartOfStaticBatch ? Matrix4x4.identity : go.transform.localToWorldMatrix;
            if (TryHitMesh(ray, filter.sharedMesh, toWorld, out point))
                return true;
        }

        point = default;
        float nearest = Mathf.Infinity;
        go.GetComponents(Colliders);
        foreach (Collider collider in Colliders)
        {
            if (collider.enabled && !collider.isTrigger && collider.Raycast(ray, out RaycastHit hit, nearest))
            {
                nearest = hit.distance;
                point = hit.point;
            }
        }
        return nearest < Mathf.Infinity;
    }

    static bool TryHitSkinned(Ray ray, SkinnedMeshRenderer skinned, out Vector3 point)
    {
        point = default;
        Mesh mesh = skinned.sharedMesh;
        // In play mode Unity won't hand out a mesh imported without Read/Write; colliders still count.
        if (mesh == null || (EditorApplication.isPlaying && !mesh.isReadable))
            return false;

        if (s_PosedSnapshot == null)
            s_PosedSnapshot = new Mesh { name = "Scene View Navigation posed snapshot", hideFlags = HideFlags.HideAndDontSave };

        // useScale: the snapshot comes out in the renderer's own space, scale and all, so the
        // renderer's localToWorldMatrix puts it back where it is drawn.
        skinned.BakeMesh(s_PosedSnapshot, true);
        return TryHitMesh(ray, s_PosedSnapshot, skinned.transform.localToWorldMatrix, out point);
    }

    internal static bool TryHitMesh(Ray ray, Mesh mesh, Matrix4x4 toWorld, out Vector3 point)
    {
        point = default;
        IntersectRayMeshFn intersect = GetIntersectRayMesh();
        if (intersect == null || !intersect(ray, mesh, toWorld, out RaycastHit hit))
            return false;
        // The distance is along the world ray, the way Unity's own callers compare it.
        point = ray.GetPoint(hit.distance);
        return true;
    }

    static IntersectRayMeshFn GetIntersectRayMesh()
    {
        if (s_IntersectRayMeshLookedUp)
            return s_IntersectRayMesh;
        s_IntersectRayMeshLookedUp = true;

        MethodInfo method = typeof(HandleUtility).GetMethod("IntersectRayMesh",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { typeof(Ray), typeof(Mesh), typeof(Matrix4x4), typeof(RaycastHit).MakeByRefType() }, null);
        if (method != null)
            s_IntersectRayMesh = (IntersectRayMeshFn)System.Delegate.CreateDelegate(typeof(IntersectRayMeshFn), method, false);

        if (s_IntersectRayMesh == null)
            Debug.LogWarning("Scene View Navigation: this Unity version has no HandleUtility.IntersectRayMesh, so zoom and " +
                             "orbit can only find surfaces on objects with colliders.");
        return s_IntersectRayMesh;
    }

    static ViewState GetState(SceneView sceneView)
    {
        if (!States.TryGetValue(sceneView, out ViewState state))
        {
            state = new ViewState();
            States[sceneView] = state;
        }
        return state;
    }

    static bool IsArrowKey(KeyCode key) =>
        key == KeyCode.UpArrow || key == KeyCode.DownArrow || key == KeyCode.LeftArrow || key == KeyCode.RightArrow;

    // Focus is spotted by its result rather than its key: the pivot moved while no mouse button or
    // arrow key was driving the view. That catches F, the Frame Selected menu item and Hierarchy
    // double-clicks alike, and follows the pivot through the focus animation.
    static void TrackFocus(SceneView sceneView, Event e, ViewState state)
    {
        Vector3 pivot = sceneView.pivot;
        bool pivotMoved = state.hasLastPivot && (pivot - state.lastPivot).sqrMagnitude > PivotMoveEpsilon * PivotMoveEpsilon;
        if (pivotMoved && !state.mouseHeld && !state.arrowHeld)
            state.focusHold = true;

        state.lastPivot = pivot;
        state.hasLastPivot = true;

        switch (e.type)
        {
            case EventType.MouseDown:
                state.mouseHeld = true;
                // Panning (middle drag, or left drag with the Hand tool) and flying (right drag
                // without Alt) move the view off the focused object, so the hold ends. Alt + left
                // orbits and Alt + right drag-zooms; both keep the focused pivot.
                bool pan = e.button == 2 || (e.button == 0 && !e.alt && Tools.current == Tool.View);
                bool fly = e.button == 1 && !e.alt;
                if (pan || fly)
                    state.focusHold = false;
                break;
            case EventType.MouseUp:
                state.mouseHeld = false;
                break;
            case EventType.KeyDown:
                if (IsArrowKey(e.keyCode))
                {
                    state.arrowHeld = true;
                    state.focusHold = false;
                }
                break;
            case EventType.KeyUp:
                if (IsArrowKey(e.keyCode))
                    state.arrowHeld = false;
                break;
        }
    }

    static void OnBeforeSceneGui(SceneView sceneView)
    {
        Event e = Event.current;
        if (e == null)
            return;

        ViewState state = GetState(sceneView);
        TrackFocus(sceneView, e, state);

        // Scrolling during right-mouse flythrough changes fly speed, not zoom.
        bool scrollZoom = e.type == EventType.ScrollWheel && Tools.viewTool != ViewTool.FPS;

        // The Scene view turns this same event's delta into its zoom step, so scaling it here
        // scales the zoom. Applies whether or not Zoom To Centre is on.
        if (scrollZoom)
            e.delta *= ScrollSpeed;

        // Alt + left-drag orbits around the pivot. Retarget once as the button goes down, not during
        // the drag, or the centre point would slide over surfaces and the orbit would wobble.
        bool altOrbit = e.type == EventType.MouseDown && e.button == 0 && e.alt;

        if (Enabled && !state.focusHold && (scrollZoom || altOrbit))
        {
            MovePivotToCentreHit(sceneView, state);
            // Our own retarget is not a focus.
            state.lastPivot = sceneView.pivot;
        }

        CheckCrosshair(sceneView, e, state);
    }

    static void CheckCrosshair(SceneView sceneView, Event e, ViewState state)
    {
        if (!ShowCrosshair)
            return;

        bool letGo = e.type == EventType.MouseUp || e.type == EventType.KeyUp;
        bool input = letGo || e.type == EventType.MouseMove || e.type == EventType.MouseDrag ||
                     e.type == EventType.MouseDown || e.type == EventType.ScrollWheel || e.type == EventType.KeyDown;
        if (!input)
            return;
        if (!letGo && EditorApplication.timeSinceStartup - state.centreCheckedAt < CrosshairCheckInterval)
            return;

        Camera cam = sceneView.camera;
        if (cam != null)
            NoteCentre(sceneView, state, TryGetCentreHit(cam, out _));
    }

    static void NoteCentre(SceneView sceneView, ViewState state, bool onSurface)
    {
        state.centreCheckedAt = EditorApplication.timeSinceStartup;
        if (onSurface == state.centreOnSurface)
            return;
        state.centreOnSurface = onSurface;
        sceneView.Repaint();
    }

    static void MovePivotToCentreHit(SceneView sceneView, ViewState state)
    {
        // Orthographic zoom is a size change, not a move toward the pivot, so it never stalls.
        if (sceneView.orthographic || sceneView.in2DMode)
            return;

        Camera cam = sceneView.camera;
        if (cam == null || sceneView.size <= 0f)
            return;

        // The crosshair shares the answer, so it doesn't ask again on this same event.
        bool onSurface = TryGetCentreHit(cam, out float hitDistance);
        NoteCentre(sceneView, state, onSurface);
        if (!onSurface)
            return;

        Vector3 camPos = cam.transform.position;

        // The Scene view derives camera distance from size; keep that ratio so the camera stays put.
        float distancePerSize = sceneView.cameraDistance / sceneView.size;
        if (distancePerSize <= 0f)
            return;

        Vector3 pivot = camPos + cam.transform.forward * hitDistance;
        sceneView.LookAt(pivot, sceneView.rotation, hitDistance / distancePerSize, false, true);
    }
}

[Overlay(typeof(SceneView), "Waves/Scene View Navigation", "Scene View Navigation", true)]
class SceneViewNavigationOverlay : ToolbarOverlay
{
    SceneViewNavigationOverlay() : base(SceneViewNavigationZoomToggle.Id, SceneViewNavigationCrosshairToggle.Id,
        SceneViewNavigationScrollSpeedSlider.Id) { }
}

[EditorToolbarElement(Id, typeof(SceneView))]
class SceneViewNavigationScrollSpeedSlider : VisualElement
{
    public const string Id = "Waves/Scene View Navigation/Scroll Speed";

    public SceneViewNavigationScrollSpeedSlider()
    {
        style.flexDirection = FlexDirection.Row;
        style.alignItems = Align.Center;
        style.marginLeft = 4f;
        style.marginRight = 4f;
        tooltip = "Scroll-wheel zoom speed. 1 is Unity's own speed.";

        var slider = new Slider("Scroll", SceneViewNavigation.MinScrollSpeed, SceneViewNavigation.MaxScrollSpeed)
        {
            showInputField = true
        };
        slider.style.width = 170f;
        slider.labelElement.style.minWidth = 0f;
        slider.labelElement.style.paddingRight = 4f;
        slider.SetValueWithoutNotify(SceneViewNavigation.ScrollSpeed);
        slider.RegisterValueChangedCallback(evt => SceneViewNavigation.ScrollSpeed = evt.newValue);
        Add(slider);
    }
}

[EditorToolbarElement(Id, typeof(SceneView))]
class SceneViewNavigationZoomToggle : EditorToolbarToggle
{
    public const string Id = "Waves/Scene View Navigation/Zoom To Centre";

    public SceneViewNavigationZoomToggle()
    {
        text = "Zoom To Centre";
        tooltip = "Scroll zoom and Alt-drag orbit use the surface at the centre of the Scene view as their pivot. " +
                  "After focusing an object (F), the pivot stays on it until you pan or fly away.";
        SetValueWithoutNotify(SceneViewNavigation.Enabled);
        this.RegisterValueChangedCallback(evt => SceneViewNavigation.Enabled = evt.newValue);
    }
}

[EditorToolbarElement(Id, typeof(SceneView))]
class SceneViewNavigationCrosshairToggle : EditorToolbarToggle
{
    public const string Id = "Waves/Scene View Navigation/Crosshair";

    public SceneViewNavigationCrosshairToggle()
    {
        text = "Crosshair";
        tooltip = "Show a crosshair at the centre of the Scene view, where the zoom ray is cast.";
        SetValueWithoutNotify(SceneViewNavigation.ShowCrosshair);
        this.RegisterValueChangedCallback(evt => SceneViewNavigation.ShowCrosshair = evt.newValue);
    }
}
