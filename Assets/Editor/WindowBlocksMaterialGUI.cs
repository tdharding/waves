using UnityEditor;
using UnityEngine;

// Custom material inspector for the window-block shader. Draws all properties as
// normal, then a cheat-sheet explaining what each packed Vector4 component does
// (the graph exposes them as bundles, so the meanings aren't obvious otherwise).
//
// To enable: open WindowBlocksShaderGraph → Graph Inspector → Graph Settings →
// set "Custom Editor GUI" to  WindowBlocksMaterialGUI
public class WindowBlocksMaterialGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor editor, MaterialProperty[] props)
    {
        editor.PropertiesDefaultGUI(props);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Window params — what each channel does", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Cell Size          world size of one field cell\n" +
            "Atlas Grid (x,y)   field size in CELLS (cols, rows) — match the generator\n" +
            "Spacing            unused (gaps are baked into the field)\n" +
            "\n" +
            "Light Params\n" +
            "  x  vertical pool scale (1 = sphere, >1 hugs water, <1 climbs higher)\n" +
            "  y  falloff exponent\n" +
            "  z  lit threshold\n" +
            "  w  threshold jitter (per-window stagger, 0..1)\n" +
            "\n" +
            "Flicker Params\n" +
            "  x  flicker amount\n" +
            "  y  flicker speed\n" +
            "  z  baseline lightness (glow with no fish near)\n" +
            "  w  baseline variation per window\n" +
            "\n" +
            "Style Params\n" +
            "  x  unlit darken (recessed-pane depth)\n" +
            "  y  edge margin (world units kept window-free around each face)\n" +
            "  z  debug (0 off / 1 mask / 2 id)\n" +
            "  w  pane border (0 = solid cells, ~0.15 = framed panes)\n" +
            "\n" +
            "Light RADIUS is on the WindowLightManager component, not here.",
            MessageType.Info);
    }
}
