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
    bool    _showIcons;

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

        float orbSize = GridDesignerWindow.LowEndSlider(
            new GUIContent("Orb size",
                           "Radius of the orb marker, as a fraction of a grid cell. Weighted toward the low "
                           + "end for fine-tuning small orbs; type an exact value in the box."),
            _target.OrbCircleSize, 0.05f, 1f, power: 4f);

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

        EditorGUILayout.Space(8);
        DrawPrefabIcons();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Settings save automatically.", EditorStyles.miniLabel);

        EditorGUILayout.EndScrollView();
    }

    // A list of every prefab in the library and the icon it draws on the grid. Prefabs with a texture
    // icon show it read-only (the icon sets its own look). Prefabs on the round fallback swatch get a
    // per-prefab colour + overlay-text override; a global default colour and text size sit up top.
    void DrawPrefabIcons()
    {
        _showIcons = EditorGUILayout.Foldout(_showIcons, "Prefab Library Icons", true, EditorStyles.foldoutHeader);
        if (!_showIcons) return;

        var style = _target.Style;

        EditorGUIUtility.labelWidth = 130f;
        EditorGUI.BeginChangeCheck();
        style.defaultIconColor = EditorGUILayout.ColorField(
            new GUIContent("Default colour", "Colour of the round fallback swatch for prefabs with no texture "
                           + "icon and no per-prefab override below."),
            style.defaultIconColor);
        style.iconTextSize = EditorGUILayout.Slider(
            new GUIContent("Overlay text size", "Overlay label size at a reference icon; it scales up with "
                           + "bigger icons / zoom, capped by Max text size below."),
            style.iconTextSize, 4f, 40f);
        style.iconTextSizeMax = EditorGUILayout.Slider(
            new GUIContent("Max text size", "Upper limit on the overlay label size as it scales with the icon."),
            style.iconTextSizeMax, 6f, 80f);
        style.forwardArrowColor = EditorGUILayout.ColorField(
            new GUIContent("Forward arrow colour", "Colour of the forward-direction arrow drawn on the icon "
                           + "circumference for prefabs whose baseline alignment overrides forward."),
            style.forwardArrowColor);
        style.forwardArrowScale = EditorGUILayout.Slider(
            new GUIContent("Forward arrow scale", "Size of the forward-direction arrow (all prefabs)."),
            style.forwardArrowScale, 0.2f, 3f);
        if (EditorGUI.EndChangeCheck()) _target.SaveStyle();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Colour / text below apply only to prefabs without a texture icon.",
                                   EditorStyles.miniLabel);

        var entries = _target.GetPrefabLibraryIcons();
        if (entries == null || entries.Count == 0)
        {
            EditorGUILayout.LabelField("  No prefabs scanned.", EditorStyles.miniLabel);
            return;
        }

        const float sz = 20f;
        foreach (var (name, icon) in entries)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect box = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                                    GUILayout.Width(sz), GUILayout.Height(sz));
                var ov = _target.GetIconOverride(name, false);

                if (icon != null)
                {
                    GUI.DrawTexture(box, icon, ScaleMode.ScaleToFit);
                    EditorGUILayout.LabelField(name);   // icon sets its own colour/text
                    continue;
                }

                Color col = (ov != null && ov.overrideColor) ? ov.color : style.defaultIconColor;
                Handles.BeginGUI();
                Handles.color = col;
                Handles.DrawSolidDisc(box.center, Vector3.forward, sz * 0.5f);
                Handles.EndGUI();

                EditorGUILayout.LabelField(name, GUILayout.MinWidth(40));

                // Per-prefab colour override (changing it turns the override on).
                EditorGUI.BeginChangeCheck();
                Color newCol = EditorGUILayout.ColorField(col, GUILayout.Width(46));
                bool colChanged = EditorGUI.EndChangeCheck();

                // Per-prefab overlay label (empty = auto abbreviation).
                string lbl = ov != null ? ov.label : "";
                EditorGUI.BeginChangeCheck();
                string newLbl = EditorGUILayout.TextField(lbl, GUILayout.Width(44));
                bool lblChanged = EditorGUI.EndChangeCheck();

                if (colChanged || lblChanged)
                {
                    var w = _target.GetIconOverride(name, true);
                    if (colChanged) { w.overrideColor = true; w.color = newCol; }
                    if (lblChanged) w.label = newLbl;
                    _target.SaveStyle();
                }

                // Reset this prefab back to the default colour + auto label.
                using (new EditorGUI.DisabledScope(ov == null))
                    if (GUILayout.Button(new GUIContent("↺", "Clear this prefab's colour/text override"),
                                         GUILayout.Width(22)) && ov != null)
                    {
                        style.iconOverrides.Remove(ov);
                        _target.SaveStyle();
                    }
            }
        }
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
        MarkerRow("Angel perch",     style.spikeAngelPerch,
                  "The dot on the tip of rocks the angel can land on (▲ Spikes tool).");
        MarkerRow("Angel priority perch", style.spikeAngelPriorityPerch,
                  "The tip dot on perches she always comes down to, rather than only watches.");
        MarkerRow("Angel perch range", style.angelPerchRadius,
                  "The fill showing how close the boat must get for her to land on a rock.");
        MarkerRow("Angel talk range",  style.angelTalkRadius,
                  "The fill showing how close the boat must get to talk to her once she is perched.");
        LineRow("Angel landing curve", style.angelLandingCurve,
                "The path she flies in along. Check it clears the walls and blocks around the rock.");

        EditorGUI.indentLevel++;
        style.angelRadiiOnSelectedOnly = EditorGUILayout.Toggle(
            new GUIContent("Ranges on selection only",
                           "Draw the two ranges for the selected rock only. Off draws them for every " +
                           "marked rock, which reads well on a level built around a few meeting points " +
                           "and badly on one with many."),
            style.angelRadiiOnSelectedOnly);
        style.angelRadiiOpacity = EditorGUILayout.Slider(
            new GUIContent("Range opacity", "Overall strength of both range fills."),
            style.angelRadiiOpacity, 0.05f, 1f);
        EditorGUI.indentLevel--;
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
    // For overlays drawn as a LINE rather than a disc: fill/outline means nothing, thickness does.
    static void LineRow(string label, GridMarkerStyle st, string tooltip)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            st.color = EditorGUILayout.ColorField(new GUIContent(label, tooltip), st.color);
            st.width = EditorGUILayout.Slider(st.width, 0.5f, 8f, GUILayout.Width(120));
        }
    }

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
