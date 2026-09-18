using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What is keeping the editor busy, and the switches to stop it.
///
/// Open Shader Graph windows animate their previews all the time, and while any are open the Scene
/// and Game views redraw about thirty times a second — that is what ran the laptop hot. The tuners
/// push their numbers every editor tick while Apply Live is on. This window counts both and turns
/// them off without having to hunt through the docked tabs.
/// </summary>
public class EditorLoadMonitor : EditorWindow
{
    [MenuItem("Tools/Waves/Editor Load Monitor")]
    public static void Open() => GetWindow<EditorLoadMonitor>("Load Monitor");

    const string ShaderGraphWindowType = "MaterialGraphEditWindow";

    Vector2 _scroll;
    string  _lastState;

    // Polled ten times a second, but the window only redraws when something it shows has changed.
    // A monitor that repainted constantly would be part of the problem it is watching for.
    void OnInspectorUpdate()
    {
        string state = DescribeState();
        if (state == _lastState) return;
        _lastState = state;
        Repaint();
    }

    void OnFocus() => Repaint();

    static List<EditorWindow> ShaderGraphWindows() =>
        Resources.FindObjectsOfTypeAll<EditorWindow>()
                 .Where(w => w != null && w.GetType().Name == ShaderGraphWindowType)
                 .ToList();

    static T[] OpenWindows<T>() where T : EditorWindow => Resources.FindObjectsOfTypeAll<T>();

    static string DescribeState()
    {
        var graphs = ShaderGraphWindows();
        return string.Join("|",
            graphs.Select(w => w.titleContent.text + w.hasUnsavedChanges)
            .Concat(OpenWindows<LevelSelectRiverTuner>().Select(t => "river" + t.ApplyLive))
            .Concat(OpenWindows<LevelSelectRunShadingTuner>().Select(t => "shading" + t.ApplyLive))
            .Concat(OpenWindows<LevelSelectLandscapeTuner>().Select(t => "landscape" + t.ApplyLive))
            .Concat(OpenWindows<WaveEffectsLiveTuner>().Select(t => "wave" + t.ApplyLive + t.TunerActive)));
    }

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawShaderGraphs();
        EditorGUILayout.Space(12);
        DrawTuners();
        EditorGUILayout.EndScrollView();
    }

    // ─────────────────────────────────────────────────────────────
    // Shader Graph windows
    // ─────────────────────────────────────────────────────────────

    void DrawShaderGraphs()
    {
        var graphs = ShaderGraphWindows();

        EditorGUILayout.LabelField($"Shader Graph windows open: {graphs.Count}", EditorStyles.boldLabel);

        if (graphs.Count == 0)
        {
            EditorGUILayout.HelpBox("None open.", MessageType.None);
            return;
        }

        EditorGUILayout.HelpBox(
            "While any Shader Graph window is open, the Scene and Game views keep redrawing. " +
            "Close them when you are not editing a graph.", MessageType.Warning);

        foreach (var w in graphs)
        {
            string name = w.titleContent.text.TrimEnd('*').Trim();
            EditorGUILayout.LabelField("•  " + name + (w.hasUnsavedChanges ? "   (unsaved changes)" : ""));
        }

        int unsaved = graphs.Count(w => w.hasUnsavedChanges);
        if (GUILayout.Button($"Close all ({graphs.Count})", GUILayout.Height(24)))
            // After this draw, not during it: closing windows mid-layout breaks the layout pass.
            EditorApplication.delayCall += () => CloseGraphs(graphs);

        if (unsaved > 0)
        {
            string it = unsaved == 1 ? "it" : "them";
            EditorGUILayout.HelpBox(
                $"{unsaved} with unsaved changes will be left open by Close all. " +
                $"Save or close {it} in the graph window itself.", MessageType.Info);
        }
    }

    static void CloseGraphs(List<EditorWindow> graphs)
    {
        // A graph with unsaved changes is never closed from here. Closing it by code bypasses the
        // window's own Save / Discard prompt, and an edit lost at the press of a tidy-up button is
        // a far worse outcome than a window left open.
        foreach (var w in graphs)
            if (w != null && !w.hasUnsavedChanges) w.Close();
    }

    // ─────────────────────────────────────────────────────────────
    // Tuners
    // ─────────────────────────────────────────────────────────────

    void DrawTuners()
    {
        var river   = OpenWindows<LevelSelectRiverTuner>();
        var shading   = OpenWindows<LevelSelectRunShadingTuner>();
        var landscape = OpenWindows<LevelSelectLandscapeTuner>();
        var wave      = OpenWindows<WaveEffectsLiveTuner>();

        int open = river.Length + shading.Length + landscape.Length + wave.Length;
        int live = river.Count(t => t.ApplyLive) + shading.Count(t => t.ApplyLive)
                 + landscape.Count(t => t.ApplyLive) + wave.Count(t => t.ApplyLive);

        EditorGUILayout.LabelField($"Tuners open: {open}   Applying live: {live}", EditorStyles.boldLabel);

        if (open == 0)
        {
            EditorGUILayout.HelpBox("No tuners open.", MessageType.None);
            return;
        }

        foreach (var t in river)   t.ApplyLive = TunerRow("Level Select River Tuner", t.ApplyLive, null);
        foreach (var t in shading) t.ApplyLive = TunerRow("Level Select Run Shading", t.ApplyLive, null);
        foreach (var t in landscape) t.ApplyLive = TunerRow("Level Select Landscape", t.ApplyLive, null);
        foreach (var t in wave)    t.ApplyLive = TunerRow("Wave Effects Tuner", t.ApplyLive,
                                                          t.TunerActive ? null : "inactive until Load");

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("All Apply Live On",  GUILayout.Height(24))) SetAll(true);
            if (GUILayout.Button("All Apply Live Off", GUILayout.Height(24))) SetAll(false);
        }
    }

    static bool TunerRow(string name, bool applyLive, string note)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(name + (note != null ? $"   ({note})" : ""));
            return GUILayout.Toggle(applyLive, "Apply Live", EditorStyles.miniButton, GUILayout.Width(80));
        }
    }

    static void SetAll(bool on)
    {
        foreach (var t in OpenWindows<LevelSelectRiverTuner>())      t.ApplyLive = on;
        foreach (var t in OpenWindows<LevelSelectRunShadingTuner>()) t.ApplyLive = on;
        foreach (var t in OpenWindows<LevelSelectLandscapeTuner>())  t.ApplyLive = on;
        foreach (var t in OpenWindows<WaveEffectsLiveTuner>())       t.ApplyLive = on;
    }
}
