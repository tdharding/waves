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
[InitializeOnLoad]
public static class SceneZoomToCentre
{
    const string EnabledPref = "Waves.SceneZoomToCentre.Enabled";
    const string CrosshairPref = "Waves.SceneZoomToCentre.Crosshair";

    const float CrosshairArm = 6f;
    const float CrosshairThickness = 1f;

    // Closer than this and the hit is effectively the camera itself; retargeting would stall zoom.
    const float MinHitDistance = 0.0001f;

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

    static SceneZoomToCentre()
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

    static void OnBeforeSceneGui(SceneView sceneView)
    {
        if (!Enabled)
            return;

        Event e = Event.current;
        if (e == null)
            return;

        // Scrolling during right-mouse flythrough changes fly speed, not zoom.
        bool scrollZoom = e.type == EventType.ScrollWheel && Tools.viewTool != ViewTool.FPS;

        // Alt + left-drag orbits around the pivot. Retarget once as the button goes down, not during
        // the drag, or the centre point would slide over surfaces and the orbit would wobble.
        bool altOrbit = e.type == EventType.MouseDown && e.button == 0 && e.alt;

        if (scrollZoom || altOrbit)
            MovePivotToCentreHit(sceneView);
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

[Overlay(typeof(SceneView), "Waves/Zoom To Centre", "Zoom To Centre", true)]
class SceneZoomToCentreOverlay : ToolbarOverlay
{
    SceneZoomToCentreOverlay() : base(SceneZoomToCentreToggle.Id, SceneZoomCrosshairToggle.Id) { }
}

[EditorToolbarElement(Id, typeof(SceneView))]
class SceneZoomToCentreToggle : EditorToolbarToggle
{
    public const string Id = "Waves/Zoom To Centre/Toggle";

    public SceneZoomToCentreToggle()
    {
        text = "Zoom To Centre";
        tooltip = "Scroll zoom and Alt-drag orbit use the surface at the centre of the Scene view as their pivot.";
        SetValueWithoutNotify(SceneZoomToCentre.Enabled);
        this.RegisterValueChangedCallback(evt => SceneZoomToCentre.Enabled = evt.newValue);
    }
}

[EditorToolbarElement(Id, typeof(SceneView))]
class SceneZoomCrosshairToggle : EditorToolbarToggle
{
    public const string Id = "Waves/Zoom To Centre/Crosshair";

    public SceneZoomCrosshairToggle()
    {
        text = "Crosshair";
        tooltip = "Show a crosshair at the centre of the Scene view, where the zoom ray is cast.";
        SetValueWithoutNotify(SceneZoomToCentre.ShowCrosshair);
        this.RegisterValueChangedCallback(evt => SceneZoomToCentre.ShowCrosshair = evt.newValue);
    }
}
