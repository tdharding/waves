using UnityEngine;
using UnityEditor;

// A small dockable companion to the Grid Designer that gathers its UI display settings in one place,
// so they can be tuned while watching the grid update live. Dock it beside the designer and leave it
// open. Every value is backed by the designer's EditorPrefs-persisted properties, so edits apply
// immediately and survive a restart — there is no separate Save.
public class GridDesignerSettingsWindow : EditorWindow
{
    GridDesignerWindow _target;
    Vector2 _scroll;

    [MenuItem("Tools/Waves/Grid Designer Settings")]
    public static void Open() => ShowFor(FindDesigner());

    // Opens (or focuses) the settings window bound to a specific designer window.
    public static void ShowFor(GridDesignerWindow target)
    {
        var w = GetWindow<GridDesignerSettingsWindow>(false, "Grid Settings", true);
        w._target  = target;
        w.minSize  = new Vector2(320f, 260f);
        w.Show();
    }

    // The first open Grid Designer window, or null if none is open.
    static GridDesignerWindow FindDesigner()
    {
        var all = Resources.FindObjectsOfTypeAll<GridDesignerWindow>();
        return (all != null && all.Length > 0) ? all[0] : null;
    }

    void OnGUI()
    {
        if (_target == null) _target = FindDesigner();
        if (_target == null)
        {
            EditorGUILayout.HelpBox("Open the Grid Designer (Tools ▸ Waves ▸ Grid Designer) to edit its "
                                    + "display settings.", MessageType.Info);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("UI Display", EditorStyles.boldLabel);

        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 160f;

        EditorGUI.BeginChangeCheck();

        float gridLines = EditorGUILayout.Slider(
            new GUIContent("Grid lines", "Opacity of the grid lines drawn over each cell."),
            _target.GridLineOpacity, 0f, 1f);

        float backdrop = EditorGUILayout.Slider(
            new GUIContent("Backdrop", "Brightness of the disc drawn behind the grid."),
            _target.BackdropBrightness, 0f, 1f);

        float selCircle = EditorGUILayout.Slider(
            new GUIContent("Selection circle size",
                           "Radius of the white opaque selection dot, as a fraction of a grid cell. "
                           + "Applies to selected prefabs, nodes, procedural spikes and blocks."),
            _target.SelectionCircleFactor, 0.05f, 1f);

        int spikeRes = EditorGUILayout.IntSlider(
            new GUIContent("Spike display resolution",
                           "Gradient resolution the procedural spikes are drawn at in the designer — "
                           + "higher is smoother, at a little more draw cost."),
            _target.SpikeDisplayResolution, 3, 32);

        float orbSize = EditorGUILayout.Slider(
            new GUIContent("Orb size",
                           "Radius of the orb cell marker, as a fraction of a grid cell."),
            _target.OrbCircleSize, 0.1f, 1f);

        if (EditorGUI.EndChangeCheck())
        {
            _target.GridLineOpacity       = gridLines;
            _target.BackdropBrightness     = backdrop;
            _target.SelectionCircleFactor  = selCircle;
            _target.SpikeDisplayResolution = spikeRes;
            _target.OrbCircleSize          = orbSize;
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Drawing", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        bool clamp = EditorGUILayout.Toggle(
            new GUIContent("Clamp to cell",
                           "On: a newly drawn object (prefab, soul-zone node) snaps to the centre of the "
                           + "cell under the pointer. Off: it drops at the exact pointer position — free "
                           + "placement everywhere. (Spikes and blocks are already drawn free.)"),
            _target.ClampToCellWhenDrawing);
        if (EditorGUI.EndChangeCheck())
            _target.ClampToCellWhenDrawing = clamp;

        EditorGUIUtility.labelWidth = prevLW;

        EditorGUILayout.Space(8);
        DrawAppearance();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Settings save automatically.", EditorStyles.miniLabel);

        EditorGUILayout.EndScrollView();
    }

    // Per-element colour + fill/outline for every themeable overlay marker. Edits mutate the target's
    // shared style object, then SaveStyle() persists it and repaints the grid.
    void DrawAppearance()
    {
        var style = _target.Style;

        EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        // Grid lines are a plain line colour (opacity is the slider above).
        style.gridLineColor = EditorGUILayout.ColorField(
            new GUIContent("Grid lines", "Colour of the grid lines (opacity is the Grid lines slider above)."),
            style.gridLineColor);

        MarkerRow("Selection",       style.selection,
                  "The white circle on the selected prefab, node, spike or block.");
        MarkerRow("Climbable spike", style.spikeClimbable,
                  "The dot on rocks the creepy guy can climb (▲ Spikes tool).");
        MarkerRow("Whirlpool",       style.whirlpool,
                  "The whirlpool cell marker and its radius ring.");
        MarkerRow("Water modifier",  style.waterModifier, "The water-level-modifier cell disc.");
        MarkerRow("Wave modifier",   style.waveModifier,  "The wave-modifier cell disc.");
        MarkerRow("Orb",             style.orb,           "The orb cell marker.");
        MarkerRow("Prefab ring",     style.prefabRing,    "The scale-radius footprint ring around placed prefabs.");

        if (EditorGUI.EndChangeCheck())
            _target.SaveStyle();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Soul zones are colour-coded per zone, so they're not themed here.",
                                   EditorStyles.miniLabel);
    }

    // One row: a colour field plus a Fill/Outline toggle for a single marker.
    static void MarkerRow(string label, GridMarkerStyle st, string tooltip)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            st.color = EditorGUILayout.ColorField(new GUIContent(label, tooltip), st.color);
            st.outline = GUILayout.Toggle(st.outline, new GUIContent(st.outline ? "Outline" : "Fill",
                                          "Toggle between a filled disc and an outline ring."),
                                          EditorStyles.miniButton, GUILayout.Width(58));
        }
    }
}
