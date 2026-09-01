using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Easing tab — pick a key, shape the curve leaving it on a graph, apply it to the selected bones.
//
// This tab edits the CLIP ASSET, unlike the Pose tab which only moves scene transforms. Nothing
// here shows up until you apply.
//
// The graph is a cubic bezier over a normalised segment: time left to right, progress bottom to
// top, ends pinned at (0,0) and (1,1). That maps onto Unity keyframes exactly, with no fitting or
// approximation, because a weighted Unity segment IS a cubic bezier — its control points sit at
//
//     (t0 + outWeight*dt,  v0 + outWeight*dt*outTangent)
//     (t1 - inWeight*dt,   v1 - inWeight*dt*inTangent)
//
// so feeding a handle in as weight = x and tangent = y*dv/(x*dt) puts the control point at exactly
// (t0 + x*dt, v0 + y*dv) — the handle's own place on the graph, per curve, whatever that curve's
// value range happens to be. That last part is what makes it safe on rotation: a bone's rotation is
// four curves (m_LocalRotation.x/y/z/w) with four different value ranges, and normalising per curve
// gives all four the same ease shape. Ease them independently and the rotation wobbles through the
// interpolation.
[System.Serializable]
public class EasingTool : AnimateTool
{
    [SerializeField] Vector2 easeIn  = new Vector2(0.42f, 0f);
    [SerializeField] Vector2 easeOut = new Vector2(0.58f, 1f);
    [SerializeField] int     segment;

    [System.NonSerialized] int dragHandle;   // 0 none, 1 easeIn, 2 easeOut

    static readonly Color GraphBack  = new Color(0.16f, 0.16f, 0.16f, 1f);
    static readonly Color GraphEdge  = new Color(1f, 1f, 1f, 0.22f);
    static readonly Color GraphGuide = new Color(1f, 1f, 1f, 0.10f);
    static readonly Color CurveColor = new Color(0.55f, 0.80f, 1f, 1f);
    static readonly Color HandleTint = new Color(1f, 0.75f, 0.20f, 1f);

    public override string Title => "Easing";

    public override void OnGUI(RigContext rig)
    {
        EditorGUILayout.HelpBox("Reshapes the curve between two keys. Edits the clip asset, not the scene.",
                                MessageType.None);

        if (!rig.DrawClipField()) return;

        var selected = rig.SelectedBones();
        if (selected.Count == 0)
        {
            EditorGUILayout.HelpBox("Click a bone dot in the Scene view to choose what to ease.",
                                    MessageType.Info);
            return;
        }

        // The key list comes from the first selected bone; applying then hits every selected bone
        // that has a key at the same time. Anything without one is skipped and reported.
        Transform lead  = selected[0];
        var       times = KeyTimes(rig, lead);

        if (times.Count < 2)
        {
            EditorGUILayout.HelpBox($"{lead.name} has fewer than two keys in {rig.clip.name}, " +
                                    "so there is no segment to ease.", MessageType.Info);
            return;
        }

        var labels = new string[times.Count - 1];
        for (int i = 0; i < labels.Length; i++)
            labels[i] = $"Key {i}    {times[i]:0.000}s  →  {times[i + 1]:0.000}s";

        segment = Mathf.Clamp(segment, 0, labels.Length - 1);
        segment = EditorGUILayout.Popup("Segment", segment, labels);

        DrawGraph();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Linear"))      { easeIn = new Vector2(1f / 3f, 1f / 3f); easeOut = new Vector2(2f / 3f, 2f / 3f); }
            if (GUILayout.Button("Ease In"))     { easeIn = new Vector2(0.42f, 0f);        easeOut = new Vector2(1f, 1f); }
            if (GUILayout.Button("Ease Out"))    { easeIn = new Vector2(0f, 0f);           easeOut = new Vector2(0.58f, 1f); }
            if (GUILayout.Button("Ease In-Out")) { easeIn = new Vector2(0.42f, 0f);        easeOut = new Vector2(0.58f, 1f); }
        }

        EditorGUILayout.LabelField($"({easeIn.x:0.00}, {easeIn.y:0.00})   ({easeOut.x:0.00}, {easeOut.y:0.00})",
                                   EditorStyles.miniLabel);

        EditorGUILayout.Space();

        string what = selected.Count == 1 ? lead.name : $"{selected.Count} bones";
        if (GUILayout.Button($"Apply to {what}"))
        {
            int n = Apply(rig, selected, times[segment]);
            Debug.Log($"[Animate Suite] Eased {n} curves at {times[segment]:0.000}s in {rig.clip.name}.");
        }
    }

    // ────────────────────────────────── clip reading ──────────────────────────────────

    // Every distinct key time across the bone's curves. A bone's components are normally keyed
    // together, but a hand-edited curve can drift, so times are unioned rather than taken from the
    // first curve found.
    static List<float> KeyTimes(RigContext rig, Transform bone)
    {
        var times = new List<float>();
        foreach (var binding in rig.BindingsFor(bone))
        {
            var curve = AnimationUtility.GetEditorCurve(rig.clip, binding);
            if (curve == null) continue;

            foreach (var k in curve.keys)
                if (!Holds(times, k.time)) times.Add(k.time);
        }
        times.Sort();
        return times;
    }

    static bool Holds(List<float> times, float t)
    {
        foreach (float existing in times)
            if (Mathf.Abs(existing - t) < 1e-4f) return true;
        return false;
    }

    static int IndexAt(AnimationCurve curve, float time)
    {
        for (int i = 0; i < curve.length; i++)
            if (Mathf.Abs(curve[i].time - time) < 1e-4f) return i;
        return -1;
    }

    // ────────────────────────────────── apply ──────────────────────────────────

    int Apply(RigContext rig, List<Transform> boneList, float time)
    {
        int changed = 0;

        // Clamped away from the edges: the tangent is y*dv/(x*dt), which runs away as x approaches
        // zero, and a segment with a zero-width handle is a step, not an ease.
        float x1 = Mathf.Clamp(easeIn.x,  0.01f, 0.99f);
        float x2 = Mathf.Clamp(easeOut.x, 0.01f, 0.99f);

        Undo.RecordObject(rig.clip, "Ease Keys");

        foreach (var bone in boneList)
        foreach (var binding in rig.BindingsFor(bone))
        {
            var curve = AnimationUtility.GetEditorCurve(rig.clip, binding);
            if (curve == null || curve.length < 2) continue;

            int i = IndexAt(curve, time);
            if (i < 0 || i >= curve.length - 1) continue;   // no key here, or nothing follows it

            float dt = curve[i + 1].time - curve[i].time;
            if (dt <= 1e-6f) continue;
            float dv = curve[i + 1].value - curve[i].value;

            // Free the tangents first, or Unity recomputes them the moment the keys are written.
            AnimationUtility.SetKeyRightTangentMode(curve, i,     AnimationUtility.TangentMode.Free);
            AnimationUtility.SetKeyLeftTangentMode (curve, i + 1, AnimationUtility.TangentMode.Free);

            // Re-read: setting the tangent mode rewrites the keyframes.
            Keyframe a = curve[i];
            Keyframe b = curve[i + 1];

            a.outTangent   = dv * easeIn.y / (x1 * dt);
            a.outWeight    = x1;
            a.weightedMode = a.weightedMode | WeightedMode.Out;

            b.inTangent    = dv * (1f - easeOut.y) / ((1f - x2) * dt);
            b.inWeight     = 1f - x2;
            b.weightedMode = b.weightedMode | WeightedMode.In;

            // Only the near side of each key is touched, so the neighbouring segments keep the
            // easing they were given.
            curve.MoveKey(i,     a);
            curve.MoveKey(i + 1, b);

            AnimationUtility.SetEditorCurve(rig.clip, binding, curve);
            changed++;
        }

        EditorUtility.SetDirty(rig.clip);
        return changed;
    }

    // ────────────────────────────────── graph ──────────────────────────────────

    void DrawGraph()
    {
        Rect outer = GUILayoutUtility.GetRect(10f, 190f, GUILayout.ExpandWidth(true));

        // Square, or the curve reads as skewed and the handles land where they do not look.
        float side = Mathf.Max(60f, Mathf.Min(outer.width - 20f, outer.height - 10f));
        Rect  r    = new Rect(outer.x + 10f, outer.y + 5f, side, side);

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(r, GraphBack);
            Handles.DrawSolidRectangleWithOutline(r, Color.clear, GraphEdge);

            // Straight line for reference: how far the curve pulls off it IS the easing.
            Handles.color = GraphGuide;
            Handles.DrawAAPolyLine(1f, Pixel(r, Vector2.zero), Pixel(r, Vector2.one));

            var pts = new Vector3[49];
            for (int i = 0; i <= 48; i++) pts[i] = Pixel(r, Bezier(easeIn, easeOut, i / 48f));
            Handles.color = CurveColor;
            Handles.DrawAAPolyLine(3f, pts);

            Handles.color = new Color(HandleTint.r, HandleTint.g, HandleTint.b, 0.55f);
            Handles.DrawAAPolyLine(1.5f, Pixel(r, Vector2.zero), Pixel(r, easeIn));
            Handles.DrawAAPolyLine(1.5f, Pixel(r, Vector2.one),  Pixel(r, easeOut));

            Handles.color = HandleTint;
            Handles.DrawSolidDisc(Pixel(r, easeIn),  Vector3.forward, 5f);
            Handles.DrawSolidDisc(Pixel(r, easeOut), Vector3.forward, 5f);
        }

        Drag(r);
    }

    void Drag(Rect r)
    {
        Event e  = Event.current;
        int   id = GUIUtility.GetControlID(FocusType.Passive);

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button != 0 || !r.Contains(e.mousePosition)) break;

                float d1 = Vector2.Distance(e.mousePosition, Pixel(r, easeIn));
                float d2 = Vector2.Distance(e.mousePosition, Pixel(r, easeOut));
                if (Mathf.Min(d1, d2) > 16f) break;

                dragHandle = d1 <= d2 ? 1 : 2;
                GUIUtility.hotControl = id;
                e.Use();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != id) break;

                Vector2 v = Normalized(r, e.mousePosition);
                if (dragHandle == 1) easeIn = v; else easeOut = v;
                e.Use();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != id) break;

                GUIUtility.hotControl = 0;
                dragHandle = 0;
                e.Use();
                break;
        }
    }

    static Vector3 Pixel(Rect r, Vector2 v) =>
        new Vector3(r.xMin + v.x * r.width, r.yMax - v.y * r.height, 0f);

    static Vector2 Normalized(Rect r, Vector2 p) => new Vector2(
        Mathf.Clamp01((p.x - r.xMin) / r.width),
        Mathf.Clamp01((r.yMax - p.y) / r.height));

    // Cubic bezier with the ends pinned at (0,0) and (1,1).
    static Vector2 Bezier(Vector2 p1, Vector2 p2, float u)
    {
        float iu = 1f - u;
        return 3f * iu * iu * u * p1
             + 3f * iu * u  * u * p2
             + new Vector2(u * u * u, u * u * u);
    }
}
