using UnityEngine;
using System.Collections.Generic;

// Appearance settings for the Grid Designer's overlays. One GridMarkerStyle per themeable element —
// a colour plus a fill/outline mode and a line width (used when outlined). Serialized to JSON and
// stored in EditorPrefs by GridDesignerWindow, edited in GridDesignerSettingsWindow, and read by the
// overlay draws so every marker's colour and fill/outline is user-controlled.
//
// Adding an element: add a GridMarkerStyle field here (with its current hardcoded colour as the
// default), draw it through GridDesignerWindow.DrawMarker, and add a row in the settings window.
[System.Serializable]
public class GridMarkerStyle
{
    public Color color = Color.white;
    public bool  outline;         // false = filled disc, true = ring only
    public float width = 2f;      // ring line width when outlined

    public GridMarkerStyle() { }
    public GridMarkerStyle(Color c, bool outline = false, float width = 2f)
    {
        this.color = c; this.outline = outline; this.width = width;
    }
}

[System.Serializable]
public class GridDesignerStyle
{
    // Lines / backdrop.
    public Color gridLineColor = new Color(0f, 0f, 0f, 1f);

    // Interactive markers.
    public GridMarkerStyle selection      = new GridMarkerStyle(Color.white);
    public GridMarkerStyle spikeClimbable = new GridMarkerStyle(new Color(0.30f, 1f, 0.50f, 1f));

    // ── Angel perch points (▲ Spikes tool) ──
    public GridMarkerStyle spikeAngelPerch         = new GridMarkerStyle(new Color(1f, 0.93f, 0.55f, 1f));
    public GridMarkerStyle spikeAngelPriorityPerch = new GridMarkerStyle(new Color(1f, 0.72f, 0.10f, 1f));
    public GridMarkerStyle angelPerchRadius        = new GridMarkerStyle(new Color(1f, 0.93f, 0.55f, 0.10f));
    public GridMarkerStyle angelTalkRadius         = new GridMarkerStyle(new Color(0.50f, 0.90f, 1f, 0.16f));

    // The landing curve is a LINE, so its width is the setting that matters, not fill/outline.
    public GridMarkerStyle angelLandingCurve        = new GridMarkerStyle(new Color(1f, 0.80f, 0.35f, 0.9f), outline: true, width: 2f);

    [Tooltip("Draw the perch and talk ranges for every marked rock, not just the selected one. " +
             "Off keeps the canvas clear while you lay a level out.")]
    public bool angelRadiiOnSelectedOnly = true;

    [Range(0.05f, 1f)]
    [Tooltip("Overall strength of the two range fills, on top of their own alpha.")]
    public float angelRadiiOpacity = 1f;

    // Field overlays.
    public GridMarkerStyle whirlpool      = new GridMarkerStyle(new Color(0.70f, 0.40f, 1f, 0.55f), outline: true);
    public GridMarkerStyle waterModifier  = new GridMarkerStyle(new Color(0.40f, 0.80f, 1f, 1f));
    public GridMarkerStyle waveModifier   = new GridMarkerStyle(new Color(0.40f, 1f, 0.40f, 1f));
    public GridMarkerStyle orb            = new GridMarkerStyle(new Color(1f, 1f, 0f, 1f), outline: true);
    public GridMarkerStyle prefabRing     = new GridMarkerStyle(new Color(1f, 0.55f, 0.10f, 0.75f), outline: true);

    // NB: soul zones are deliberately NOT here — each zone is colour-coded by index via
    // SoulZoneColor(), and a single colour would flatten that. Theme those via their palette instead.

    // ── Fallback prefab icons (the round swatch drawn for prefabs with no texture icon) ──
    public Color defaultIconColor = new Color(0.5f, 0.5f, 0.5f, 1f); // used when a prefab has no override
    public float iconTextSize     = 10f;                             // overlay label size at a reference icon
    public float iconTextSizeMax  = 20f;                             // cap as the text scales up with the icon
    public List<PrefabIconOverride> iconOverrides = new List<PrefabIconOverride>();

    // Forward-direction arrow drawn on the icon circumference when the prefab's baseline alignment
    // uses a forward override. Global (all prefabs).
    public Color forwardArrowColor = new Color(1f, 0.85f, 0.10f, 1f);
    public float forwardArrowScale = 1f;
}

// Per-prefab override for a fallback (no-texture) icon: an optional colour and an optional overlay
// label. Keyed by prefab name. Prefabs whose icon is a texture ignore these.
[System.Serializable]
public class PrefabIconOverride
{
    public string prefabName;
    public bool   overrideColor;
    public Color  color = new Color(0.5f, 0.5f, 0.5f, 1f);
    public string label = "";   // empty → the auto 2-letter abbreviation
}
