using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// The Gizmo Manager's Fog button: every fog gizmo, drawn in the Scene view off the running fog
/// field. The one place fog repellers are drawn — every kind (structures, spikes, pools, runs,
/// arenas, sources, rocks and the boat) gets the same look — and the one place the layers are
/// switched, from the ▾ beside the button. Nothing is written to the scene; the choices live in
/// EditorPrefs.
/// </summary>
[InitializeOnLoad]
static class SceneViewFogGizmos
{
    const string Prefix = "FogTools.GizmoManager.Fog.";

    // Same opacity pref as the Gizmo Manager slider, so it thins these too.
    const string OpacityKey = "FogTools.GizmoManager.Opacity";

    public enum Layer { Repellers, Masses, BoatMask, SpawnCull, PaintedArea, Wind, ObstacleRange, Labels }

    public static readonly string[] LayerNames =
    {
        "Repellers", "Masses", "Boat Mask", "Spawn & Cull", "Painted Area", "Wind", "Obstacle Range", "Labels",
    };

    static SceneViewFogGizmos()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += OnEditorTick;
    }

    public static bool Shown
    {
        get => EditorPrefs.GetBool(Prefix + "Shown", false);
        set
        {
            EditorPrefs.SetBool(Prefix + "Shown", value);
            SceneView.RepaintAll();
        }
    }

    public static bool Get(Layer layer) => EditorPrefs.GetBool(Prefix + layer, layer == Layer.Repellers);

    public static void Set(Layer layer, bool on)
    {
        EditorPrefs.SetBool(Prefix + layer, on);
        SceneView.RepaintAll();
    }

    // The Scene view does not redraw on its own while the game runs, so live gizmos would
    // freeze on whatever frame it last happened to draw.
    static void OnEditorTick()
    {
        if (Shown && Application.isPlaying) SceneView.RepaintAll();
    }

    // ────────────────────────────────────────────────────────────────────────
    static readonly HashSet<IFogRepeller> _gzNear    = new HashSet<IFogRepeller>();
    static readonly HashSet<IFogRepeller> _gzPushing = new HashSet<IFogRepeller>();
    static readonly HashSet<IFogRepeller> _gzMasked  = new HashSet<IFogRepeller>();

    static readonly Color GzPush  = new Color(0.95f, 0.45f, 0.30f);
    static readonly Color GzMask  = new Color(0.35f, 0.72f, 1f);
    static readonly Color GzRange = new Color(0.75f, 0.75f, 0.78f);
    static readonly Color GzCull  = new Color(0.95f, 0.55f, 0.35f);
    static readonly Color GzSpawn = new Color(0.55f, 0.90f, 0.55f);
    static readonly Color GzMass  = new Color(1f, 0.95f, 0.75f);
    static readonly Color GzArea  = new Color(0.65f, 0.75f, 0.9f);
    static readonly Color GzWind  = new Color(0.7f, 0.9f, 1f);

    static void OnSceneGUI(SceneView view)
    {
        if (!Shown || Event.current.type != EventType.Repaint) return;

        var mgr = FogFieldManager.Instance;
        if (mgr == null) return;

        float opacity = Mathf.Clamp01(EditorPrefs.GetFloat(OpacityKey, 1f));
        bool  labels  = Get(Layer.Labels);
        float y = mgr.GizmoY;
        Vector2 b2 = mgr.BoatCentreXZ;
        Vector3 boat = new Vector3(b2.x, y, b2.y);

        using (new Handles.DrawingScope())
        {
            if (Get(Layer.PaintedArea))
            {
                float cov = mgr.GizmoCoverage, h = cov * 0.5f;
                Vector3 fc = new Vector3(mgr.FieldCentre.x, y, mgr.FieldCentre.y);
                Color fill = GzArea; fill.a = 0.04f * opacity;
                Color edge = GzArea; edge.a = 0.30f * opacity;
                Handles.DrawSolidRectangleWithOutline(new[]
                {
                    fc + new Vector3(-h, 0f, -h), fc + new Vector3(-h, 0f,  h),
                    fc + new Vector3( h, 0f,  h), fc + new Vector3( h, 0f, -h),
                }, fill, edge);
                if (labels)
                    GizmoLabel(fc + new Vector3(-h, 0f, h),
                        $"painted area {cov:0.#} u  @ {cov / Mathf.Max(mgr.GizmoFieldPx, 1):0.####} u/texel", opacity);
            }

            if (Get(Layer.SpawnCull))
            {
                DrawGizmoRing(boat, mgr.GizmoSpawnRadius, GzSpawn, 0.75f * opacity);
                DrawGizmoRing(boat, mgr.CullRadius,       GzCull,  0.75f * opacity);
                if (labels)
                {
                    GizmoLabel(boat + new Vector3(0f, 0f, mgr.GizmoSpawnRadius), $"spawn {mgr.GizmoSpawnRadius:0.##}", opacity);
                    GizmoLabel(boat + new Vector3(mgr.CullRadius, 0f, 0f),       $"cull {mgr.CullRadius:0.##}", opacity);
                }
            }

            if (Get(Layer.BoatMask))
            {
                float mask = mgr.GizmoMaskRadius;
                DrawGizmoRing(boat, mask, GzMask, 0.95f * opacity);
                // (1 - feather), matching FogMask.hlsl — where the fade begins.
                DrawGizmoRing(boat, mask * (1f - mgr.LodFeather), GzMask, 0.45f * opacity);
                if (labels)
                    GizmoLabel(boat + new Vector3(-mask, 0f, 0f), $"mask {mask:0.##}", opacity);
            }

            if (Get(Layer.ObstacleRange))
            {
                DrawGizmoRing(boat, mgr.ObstacleRange, GzRange, 0.5f * opacity);
                if (labels)
                    GizmoLabel(boat + new Vector3(0f, 0f, -mgr.ObstacleRange),
                               $"obstacle range {mgr.ObstacleRange:0.##}", opacity);
            }

            if (Get(Layer.Masses))
            {
                var blobs = mgr.LiveBlobs;
                for (int i = 0; i < blobs.Count; i++)
                {
                    var blob = blobs[i];
                    if (blob == null || !blob.Alive) continue;

                    // Hollow when faded: alive, drifting, holding a slot, painting nothing.
                    bool painting = blob.LodFade > 0.001f;
                    Vector3 bc = new Vector3(blob.Centre.x, y, blob.Centre.y);
                    float rad = Mathf.Max(blob.ReachRadius, 0.1f);
                    if (painting)
                    {
                        Color fill = GzMass; fill.a = (0.10f + 0.25f * blob.LodFade) * opacity;
                        Handles.color = fill;
                        Handles.DrawSolidDisc(bc, Vector3.up, rad);
                    }
                    DrawGizmoRing(bc, rad, painting ? GzMass : GzRange, (painting ? 0.7f : 0.5f) * opacity);
                }
            }

            if (Get(Layer.Wind))
            {
                float speed = mgr.GizmoWindSpeed;
                if (speed > 0.0001f)
                {
                    Vector2 w = mgr.WindVector;
                    Vector3 dir = new Vector3(w.x, 0f, w.y).normalized;
                    float len = Mathf.Max(mgr.GizmoMaskRadius, HandleUtility.GetHandleSize(boat));
                    Color c = GzWind; c.a = 0.8f * opacity;
                    Handles.color = c;
                    Handles.DrawLine(boat, boat + dir * len, 2f);
                    Handles.ConeHandleCap(0, boat + dir * len, Quaternion.LookRotation(dir),
                                          HandleUtility.GetHandleSize(boat) * 0.15f, EventType.Repaint);
                    if (labels)
                        GizmoLabel(boat + dir * len, $"wind {mgr.GizmoWindAngle:0}°  {speed:0.##} u/s", opacity);
                }
            }

            if (Get(Layer.Repellers))
            {
                _gzNear.Clear(); _gzPushing.Clear(); _gzMasked.Clear();
                foreach (var r in mgr.NearRepellers)    _gzNear.Add(r);
                foreach (var r in mgr.PushingRepellers) _gzPushing.Add(r);
                foreach (var r in mgr.MaskedRepellers)  _gzMasked.Add(r);

                DrawRepellerGizmo(mgr, mgr.BoatRepeller, y, opacity);
                foreach (var r in FogFieldManager.RegisteredRepellers) DrawRepellerGizmo(mgr, r, y, opacity);
                foreach (var r in mgr.AdoptedRocks)                   DrawRepellerGizmo(mgr, r, y, opacity);
            }
        }
    }

    static GUIStyle _gizmoLabelStyle;

    static void GizmoLabel(Vector3 at, string text, float opacity)
    {
        _gizmoLabelStyle ??= new GUIStyle(EditorStyles.miniLabel);
        _gizmoLabelStyle.normal.textColor = new Color(1f, 1f, 1f, Mathf.Max(opacity, 0.2f));
        Handles.Label(at, text, _gizmoLabelStyle);
    }

    static void DrawRepellerGizmo(FogFieldManager mgr, IFogRepeller r, float y, float opacity)
    {
        // A destroyed component still sits in the list until it unregisters.
        if (r == null || (r is Object o && o == null)) return;

        Vector3 c = r.RepelCentre; c.y = y;
        bool active  = r.RepelActive;
        bool inRange = active && _gzNear.Contains(r);
        bool pushing = _gzPushing.Contains(r);
        bool masked  = _gzMasked.Contains(r);

        float push = r.RepelRadius + r.RepelClearRadius;
        float mask = r.RepelRadius + r.MaskClearRadius;

        if (pushing && push > 0.001f)
        {
            Handles.color = new Color(GzPush.r, GzPush.g, GzPush.b, 0.12f * opacity);
            Handles.DrawSolidDisc(c, Vector3.up, push);
        }
        DrawGizmoRing(c, push, GzPush, (pushing ? 0.9f : 0.25f) * opacity);
        DrawGizmoRing(c, mask, GzMask, (masked  ? 0.9f : 0.25f) * opacity);

        // The height window, stood up through the point. Red is every fog plane height this
        // point counts at — the plane within Below under it or Above over it — and green carries
        // on past both ends, where it is skipped. The boat is never height-tested, so has none.
        // A column is tested base to top, so its red runs from Above under its base to Below over its top.
        if (!ReferenceEquals(r, mgr.BoatRepeller))
        {
            var   span    = r as IFogRepellerSpan;
            float baseY   = span != null ? span.RepelBottomY : r.RepelCentre.y;
            float topY    = span != null ? span.RepelTopY    : r.RepelCentre.y;
            float bottom  = baseY - mgr.HeightWindowAbove;
            float top     = topY  + mgr.HeightWindowBelow;
            float past   = Mathf.Max((top - bottom) * 0.25f, 1f);
            float a      = (active ? 0.8f : 0.25f) * opacity;

            Handles.color = new Color(0.35f, 0.85f, 0.35f, a);
            Handles.DrawLine(new Vector3(c.x, bottom - past, c.z), new Vector3(c.x, bottom, c.z), 2f);
            Handles.DrawLine(new Vector3(c.x, top, c.z), new Vector3(c.x, top + past, c.z), 2f);
            Handles.color = new Color(0.95f, 0.25f, 0.2f, a);
            Handles.DrawLine(new Vector3(c.x, bottom, c.z), new Vector3(c.x, top, c.z), 2f);
        }

        Color dot = inRange ? Color.white : GzRange;
        dot.a = (inRange ? 0.95f : 0.5f) * opacity;
        Handles.color = dot;
        Handles.DrawSolidDisc(c, Vector3.up, HandleUtility.GetHandleSize(c) * 0.04f);
    }

    static void DrawGizmoRing(Vector3 centre, float radius, Color colour, float alpha)
    {
        if (radius <= 0.001f) return;
        colour.a = alpha;
        Handles.color = colour;
        Handles.DrawWireDisc(centre, Vector3.up, radius, 2f);
    }

    /// <summary>The ▾ beside the Fog button: one tick per layer.</summary>
    public static void ShowLayerMenu(Rect under)
    {
        var menu = new GenericMenu();
        for (int i = 0; i < LayerNames.Length; i++)
        {
            var layer = (Layer)i;
            if (layer == Layer.Labels) menu.AddSeparator("");
            menu.AddItem(new GUIContent(LayerNames[i]), Get(layer), () => Set(layer, !Get(layer)));
        }
        menu.DropDown(under);
    }
}
