using UnityEngine;

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

    // Field overlays.
    public GridMarkerStyle whirlpool      = new GridMarkerStyle(new Color(0.70f, 0.40f, 1f, 0.55f), outline: true);
    public GridMarkerStyle waterModifier  = new GridMarkerStyle(new Color(0.40f, 0.80f, 1f, 1f));
    public GridMarkerStyle waveModifier   = new GridMarkerStyle(new Color(0.40f, 1f, 0.40f, 1f));
    public GridMarkerStyle orb            = new GridMarkerStyle(new Color(1f, 1f, 0f, 1f), outline: true);
    public GridMarkerStyle prefabRing     = new GridMarkerStyle(new Color(1f, 0.55f, 0.10f, 0.75f), outline: true);

    // NB: soul zones are deliberately NOT here — each zone is colour-coded by index via
    // SoulZoneColor(), and a single colour would flatten that. Theme those via their palette instead.
}
