using System.Collections.Generic;
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
// Focusing an object (F, Edit > Frame Selected, double-clicking it in the Hierarchy) wins: the
// pivot stays on the focused object for zoom and orbit until the view is panned or flown away.
[InitializeOnLoad]
public static class SceneViewNavigation
{
    // Pref keys kept from when this was Zoom To Centre, so existing toggles survive the rename.
    const string EnabledPref = "Waves.SceneZoomToCentre.Enabled";
    const string CrosshairPref = "Waves.SceneZoomToCentre.Crosshair";

    const float CrosshairArm = 6f;
    const float CrosshairThickness = 1f;

    // Closer than this and the hit is effectively the camera itself; retargeting would stall zoom.
    const float MinHitDistance = 0.0001f;

    // Pivot movement smaller than this is treated as no movement.
    const float PivotMoveEpsilon = 0.00001f;

    class ViewState
    {
        public Vector3 lastPivot;
        public bool hasLastPivot;
        public bool mouseHeld;
        public bool arrowHeld;
        // True after a focus: leave the pivot on the focused object.
        public bool focusHold;
    }

    static readonly Dictionary<SceneView, ViewState> States = new Dictionary<SceneView, ViewState>();

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

    static SceneViewNavigation()
    {
        // beforeSceneGui runs ahead of the Scene view's own zoom handling, so the new pivot is in
        // place by the time this same scroll event is turned into a zoom.
        SceneView.beforeSceneGui -= OnBeforeSceneGui;
        SceneView.beforeSceneGui += OnBeforeSceneGui;
        SceneView.duringSceneGui -= OnDuringSceneGui;
        SceneView.duringSceneGui += OnDuringSceneGui;
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
        Color colour = TryGetCentreHit(cam, out _) ? Color.white : Color.black;

        Handles.BeginGUI();
        EditorGUI.DrawRect(new Rect(c.x - CrosshairArm, c.y - half, CrosshairArm * 2f, CrosshairThickness), colour);
        EditorGUI.DrawRect(new Rect(c.x - half, c.y - CrosshairArm, CrosshairThickness, CrosshairArm * 2f), colour);
        Handles.EndGUI();
    }

    // Distance along the view direction to the surface at the centre of the Scene view.
    static bool TryGetCentreHit(Camera cam, out float hitDistance)
    {
        hitDistance = 0f;
        Vector3 camPos = cam.transform.position;
        Vector2 centreGui = HandleUtility.WorldToGUIPoint(camPos + cam.transform.forward);

        if (!HandleUtility.PlaceObject(centreGui, out Vector3 hit, out _))
            return false;

        hitDistance = Vector3.Dot(hit - camPos, cam.transform.forward);
        return hitDistance >= MinHitDistance;
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

        if (!Enabled || state.focusHold)
            return;

        // Scrolling during right-mouse flythrough changes fly speed, not zoom.
        bool scrollZoom = e.type == EventType.ScrollWheel && Tools.viewTool != ViewTool.FPS;

        // Alt + left-drag orbits around the pivot. Retarget once as the button goes down, not during
        // the drag, or the centre point would slide over surfaces and the orbit would wobble.
        bool altOrbit = e.type == EventType.MouseDown && e.button == 0 && e.alt;

        if (scrollZoom || altOrbit)
        {
            MovePivotToCentreHit(sceneView);
            // Our own retarget is not a focus.
            state.lastPivot = sceneView.pivot;
        }
    }

    static void MovePivotToCentreHit(SceneView sceneView)
    {
        // Orthographic zoom is a size change, not a move toward the pivot, so it never stalls.
        if (sceneView.orthographic || sceneView.in2DMode)
            return;

        Camera cam = sceneView.camera;
        if (cam == null || sceneView.size <= 0f)
            return;

        if (!TryGetCentreHit(cam, out float hitDistance))
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
    SceneViewNavigationOverlay() : base(SceneViewNavigationZoomToggle.Id, SceneViewNavigationCrosshairToggle.Id) { }
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
