using UnityEditor;
using UnityEngine;

/// <summary>
/// The Level Select Designer's canvas display settings, kept in EditorPrefs (a window preference,
/// not saved to the data asset). Every change repaints the open designers.
/// </summary>
public static class LevelSelectDesignerSettings
{
    public static readonly int[] MapResolutions = { 64, 128, 256, 512, 1024 };

    // ── Canvas height map ──
    public static bool  MapOn       { get => GetB("MapOn",      true);  set => SetB("MapOn",      value); }
    public static int   MapRes      { get => GetI("MapRes",     256);   set => SetI("MapRes",     value); }
    public static bool  MapDragLow  { get => GetB("MapDragLow", true);  set => SetB("MapDragLow", value); }
    public static bool  MapShade    { get => GetB("MapShade",   true);  set => SetB("MapShade",   value); }
    public static bool  MapNoise    { get => GetB("MapNoise",   false); set => SetB("MapNoise",   value); }
    public static bool  MapCuts     { get => GetB("MapCuts",    true);  set => SetB("MapCuts",    value); }

    // The Shading gradient: left = the lowest ground on the map, middle = the base hill's top, right = the highest.
    // Kept in EditorPrefs as "mode|t,r,g,b;t,r,g,b;…" (colour keys only — the map is opaque).
    // The default is the old fixed greys: 0.05 at the bottom, the tile grey flat, white at the top.
    const string MapGradientDefault = "0|0,0.05,0.05,0.05;0.5,0.38,0.38,0.38;1,1,1,1";
    static string   _mapGradientText;
    static Gradient _mapGradient;

    /// <summary>The prefs text the gradient is stored as; changes whenever the gradient does.</summary>
    public static string MapGradientText => EditorPrefs.GetString(Prefix + "MapGradient", MapGradientDefault);

    public static Gradient MapGradient
    {
        get
        {
            string text = MapGradientText;
            if (_mapGradient == null || text != _mapGradientText)
            {
                _mapGradient     = ParseGradient(text) ?? ParseGradient(MapGradientDefault);
                _mapGradientText = text;
            }
            return _mapGradient;
        }
        set
        {
            var inv  = System.Globalization.CultureInfo.InvariantCulture;
            var keys = value.colorKeys;
            var sb   = new System.Text.StringBuilder(((int)value.mode).ToString(inv)).Append('|');
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0) sb.Append(';');
                var c = keys[i].color;
                sb.Append(keys[i].time.ToString("R", inv)).Append(',')
                  .Append(c.r.ToString("R", inv)).Append(',')
                  .Append(c.g.ToString("R", inv)).Append(',')
                  .Append(c.b.ToString("R", inv));
            }
            EditorPrefs.SetString(Prefix + "MapGradient", sb.ToString());
            Changed();
        }
    }

    static Gradient ParseGradient(string text)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sty = System.Globalization.NumberStyles.Float;
        string[] halves = text.Split('|');
        if (halves.Length != 2 || !int.TryParse(halves[0], sty, inv, out int mode)) return null;

        var keys = new System.Collections.Generic.List<GradientColorKey>();
        foreach (string part in halves[1].Split(';'))
        {
            string[] f = part.Split(',');
            if (f.Length != 4
                || !float.TryParse(f[0], sty, inv, out float t) || !float.TryParse(f[1], sty, inv, out float r)
                || !float.TryParse(f[2], sty, inv, out float g) || !float.TryParse(f[3], sty, inv, out float b)) return null;
            keys.Add(new GradientColorKey(new Color(r, g, b, 1f), t));
        }
        if (keys.Count == 0) return null;

        var grad = new Gradient { mode = (GradientMode)mode };
        grad.SetKeys(keys.ToArray(), new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return grad;
    }

    // ── Contour lines (over the canvas height map) ──
    public static bool  ContourOn          { get => GetB("ContourOn", false);          set => SetB("ContourOn", value); }
    public static float ContourInterval    { get => GetF("ContourInterval", 0.25f);    set => SetF("ContourInterval", value); }
    public static float ContourWidth       { get => GetF("ContourWidth", 1.5f);        set => SetF("ContourWidth", value); }
    public static Color ContourColour      { get => GetC("ContourColour", new Color(0.15f, 0.15f, 0.15f, 0.7f)); set => SetC("ContourColour", value); }
    public static int   ContourMajorEvery  { get => GetI("ContourMajorEvery", 4);      set => SetI("ContourMajorEvery", value); }
    public static float ContourMajorWidth  { get => GetF("ContourMajorWidth", 3f);     set => SetF("ContourMajorWidth", value); }
    public static Color ContourMajorColour { get => GetC("ContourMajorColour", new Color(0.05f, 0.05f, 0.05f, 0.9f)); set => SetC("ContourMajorColour", value); }
    public static bool  ContourByHeight    { get => GetB("ContourByHeight", false);    set => SetB("ContourByHeight", value); }

    // ── Hill points (Landscape mode) ──
    public static float HillRingWidth     { get => GetF("HillRingWidth", 3f);           set => SetF("HillRingWidth", value); }
    public static Color HillRingColour    { get => GetC("HillRingColour", Color.white);  set => SetC("HillRingColour", value); }
    public static Color HillSelectedColour{ get => GetC("HillSelColour",  Color.yellow); set => SetC("HillSelColour",  value); }
    public static float HillDotSize       { get => GetF("HillDotSize", 4f);             set => SetF("HillDotSize", value); }
    public static float HillSelDotSize    { get => GetF("HillSelDotSize", 4f);          set => SetF("HillSelDotSize", value); }
    public static Color HillDotColour     { get => GetC("HillDotColour",  Color.white);  set => SetC("HillDotColour",  value); }

    // ── Landscape mode ──
    // How see-through the river runs, pools and everything built on them draw while in Landscape mode.
    public static float LandscapeStructureOpacity { get => GetF("LandscapeStructureOpacity", 0.35f); set => SetF("LandscapeStructureOpacity", value); }
    // Off, Landscape mode leaves out the plain river node dots.
    public static bool  LandscapeShowRiverNodes   { get => GetB("LandscapeShowRiverNodes", false);   set => SetB("LandscapeShowRiverNodes", value); }
    // Off, Landscape mode leaves out the spikes.
    public static bool  LandscapeShowSpikes       { get => GetB("LandscapeShowSpikes", false);       set => SetB("LandscapeShowSpikes", value); }
    // Off, Landscape mode leaves out the junction diamonds.
    public static bool  LandscapeShowJunctions    { get => GetB("LandscapeShowJunctions", false);    set => SetB("LandscapeShowJunctions", value); }
    // Off, Landscape mode leaves out the arenas: their nodes, rings and entrances.
    public static bool  LandscapeShowArenas       { get => GetB("LandscapeShowArenas", false);       set => SetB("LandscapeShowArenas", value); }
    // Off, Landscape mode leaves out the soul paths.
    public static bool  LandscapeShowSoulPaths    { get => GetB("LandscapeShowSoulPaths", false);    set => SetB("LandscapeShowSoulPaths", value); }

    // ── Node dots ──
    public static float NodeDotSize       { get => GetF("NodeDotSize", 6f);              set => SetF("NodeDotSize", value); }
    public static Color NodeDotColour     { get => GetC("NodeDotColour", Color.gray);    set => SetC("NodeDotColour", value); }
    public static float NodeSelRingSize   { get => GetF("NodeSelRingSize", 10f);         set => SetF("NodeSelRingSize", value); }
    public static float NodeSelRingWidth  { get => GetF("NodeSelRingWidth", 3f);         set => SetF("NodeSelRingWidth", value); }
    public static Color NodeSelColour     { get => GetC("NodeSelColour", Color.white);   set => SetC("NodeSelColour", value); }
    // ── Camera ──
    // Off, the canvas draws no cameras at all: neither the Cinematic node symbols nor the shot cameras.
    public static bool  ShowCameras       { get => GetB("ShowCameras", true);            set => SetB("ShowCameras", value); }
    public static bool  CameraSymbol      { get => GetB("CameraSymbol", true);           set => SetB("CameraSymbol", value); }
    public static float CameraSymbolSize  { get => GetF("CameraSymbolSize", 12f);        set => SetF("CameraSymbolSize", value); }
    public static Color CameraSymbolColour{ get => GetC("CameraSymbolColour", Color.white); set => SetC("CameraSymbolColour", value); }
    public static bool  ShotCameras       { get => GetB("ShotCameras", true);            set => SetB("ShotCameras", value); }
    public static float ShotCameraSize    { get => GetF("ShotCameraSize", 14f);          set => SetF("ShotCameraSize", value); }
    public static Color ShotCameraColour  { get => GetC("ShotCameraColour", new Color(0.8f, 0.8f, 0.8f, 1f)); set => SetC("ShotCameraColour", value); }
    public static Color PlaytestBoatColour{ get => GetC("PlaytestBoatColour", new Color(0.45f, 0.85f, 1f, 1f)); set => SetC("PlaytestBoatColour", value); }

    // ── Junctions ──
    public static float JunctionSize      { get => GetF("JunctionSize", 8f);             set => SetF("JunctionSize", value); }
    public static float JunctionWidth     { get => GetF("JunctionWidth", 2f);            set => SetF("JunctionWidth", value); }
    public static Color JunctionColour    { get => GetC("JunctionColour", Color.yellow); set => SetC("JunctionColour", value); }
    public static Color JunctionSelColour { get => GetC("JunctionSelColour", Color.white); set => SetC("JunctionSelColour", value); }
    public static Color JunctionHighlightColour { get => GetC("JunctionHiColour", new Color(1f, 0.85f, 0f, 1f)); set => SetC("JunctionHiColour", value); }

    // ── Soul routes ──
    public static bool  SoulShow          { get => GetB("SoulShow", true);               set => SetB("SoulShow", value); }
    public static bool  SoulRouteColours  { get => GetB("SoulRouteColours", true);       set => SetB("SoulRouteColours", value); }
    public static Color SoulRouteColour   { get => GetC("SoulRouteColour", new Color(0.6f, 0.9f, 1f, 1f)); set => SetC("SoulRouteColour", value); }
    public static float SoulBandOpacity   { get => GetF("SoulBandOpacity", 0.22f);       set => SetF("SoulBandOpacity", value); }
    public static float SoulSelBandOpacity{ get => GetF("SoulSelBandOpacity", 0.5f);     set => SetF("SoulSelBandOpacity", value); }
    public static bool  SoulPoints        { get => GetB("SoulPoints", true);             set => SetB("SoulPoints", value); }
    public static Color SoulDotColour     { get => GetC("SoulDotColour", Color.black);   set => SetC("SoulDotColour", value); }
    public static bool  SoulArrows        { get => GetB("SoulArrows", true);             set => SetB("SoulArrows", value); }
    public static bool  SoulArrowsAll     { get => GetB("SoulArrowsAll", false);         set => SetB("SoulArrowsAll", value); }
    public static float SoulArrowScale    { get => GetF("SoulArrowScale", 1f);           set => SetF("SoulArrowScale", value); }
    public static Color SoulArrowColour   { get => GetC("SoulArrowColour", new Color(0f, 0f, 0f, 0.6f)); set => SetC("SoulArrowColour", value); }

    // ── Rivers ──
    public static bool  RiverColours      { get => GetB("RiverColours", true);           set => SetB("RiverColours", value); }
    public static bool  RiverOutline      { get => GetB("RiverOutline", true);           set => SetB("RiverOutline", value); }
    public static bool  RiverArrows       { get => GetB("RiverArrows", true);            set => SetB("RiverArrows", value); }
    public static float RiverArrowSize    { get => GetF("RiverArrowSize", 7f);           set => SetF("RiverArrowSize", value); }
    public static float RiverArrowWidth   { get => GetF("RiverArrowWidth", 2f);          set => SetF("RiverArrowWidth", value); }
    public static Color RiverArrowColour  { get => GetC("RiverArrowColour", Color.white); set => SetC("RiverArrowColour", value); }
    public static float RiverOutlineWidth { get => GetF("RiverOutlineWidth", 2f);        set => SetF("RiverOutlineWidth", value); }
    public static Color RiverOutlineColour{ get => GetC("RiverOutlineColour", new Color(0.75f, 0.75f, 0.75f, 1f)); set => SetC("RiverOutlineColour", value); }
    public static bool  RiverFill         { get => GetB("RiverFill", true);              set => SetB("RiverFill", value); }
    public static float RiverFillOpacity  { get => GetF("RiverFillOpacity", 0.25f);      set => SetF("RiverFillOpacity", value); }

    // ── Arenas ──
    public static bool  ArenaOutline      { get => GetB("ArenaOutline", true);           set => SetB("ArenaOutline", value); }
    public static float ArenaOutlineWidth { get => GetF("ArenaOutlineWidth", 2f);        set => SetF("ArenaOutlineWidth", value); }
    public static Color ArenaOutlineColour   { get => GetC("ArenaOutlineColour",   new Color(1f, 0.7f, 0.2f, 0.35f)); set => SetC("ArenaOutlineColour",   value); }
    public static Color ArenaOuterRingColour { get => GetC("ArenaOuterRingColour", new Color(1f, 0.7f, 0.2f, 0.12f)); set => SetC("ArenaOuterRingColour", value); }
    public static bool  ArenaFill         { get => GetB("ArenaFill", true);              set => SetB("ArenaFill", value); }
    public static Color ArenaFillColour   { get => GetC("ArenaFillColour", new Color(1f, 0.7f, 0.2f, 0.15f)); set => SetC("ArenaFillColour", value); }

    public static Color RiverSingleColour { get => GetC("RiverSingleColour", new Color(0.8f, 0.8f, 0.8f, 1f)); set => SetC("RiverSingleColour", value); }

    /// <summary>A river or pool's editor colour as the canvas draws it: its own, or the one River Colour shared by all when River Colours is off.</summary>
    public static Color RiverColour(Color c) => RiverColours ? c : RiverSingleColour;

    // ── Pipes ──
    public static float PipeNodeSize        { get => GetF("PipeNodeSize", 4.5f);          set => SetF("PipeNodeSize", value); }
    public static float PipeOpacity         { get => GetF("PipeOpacity", 1f);             set => SetF("PipeOpacity", value); }
    public static Color PipeBodyColour      { get => GetC("PipeBodyColour",    new Color(0.55f, 0.72f, 0.78f, 0.9f));  set => SetC("PipeBodyColour",    value); }
    public static Color PipeBoreColour      { get => GetC("PipeBoreColour",    new Color(0.22f, 0.3f,  0.33f, 0.9f));  set => SetC("PipeBoreColour",    value); }
    public static Color PipeSelBodyColour   { get => GetC("PipeSelBodyColour", new Color(0.92f, 0.92f, 0.92f, 0.95f)); set => SetC("PipeSelBodyColour", value); }
    public static Color PipeSelBoreColour   { get => GetC("PipeSelBoreColour", new Color(0.45f, 0.45f, 0.45f, 0.95f)); set => SetC("PipeSelBoreColour", value); }
    public static Color PipeSupportColour   { get => GetC("PipeSupportColour", new Color(1f, 0.85f, 0.4f, 1f));        set => SetC("PipeSupportColour", value); }
    public static Color PipeSelColour       { get => GetC("PipeSelColour",     Color.yellow);                          set => SetC("PipeSelColour",     value); }
    public static Color PipeOwnHeightColour { get => GetC("PipeOwnHeightColour", new Color(1f, 0.6f, 0.15f, 1f));      set => SetC("PipeOwnHeightColour", value); }
    public static Color PipeOutlineColour   { get => GetC("PipeOutlineColour", new Color(0.1f, 0.1f, 0.1f, 0.9f));     set => SetC("PipeOutlineColour", value); }
    public static Color PipeGhostColour     { get => GetC("PipeGhostColour",   new Color(1f, 1f, 1f, 0.3f));           set => SetC("PipeGhostColour",   value); }
    public static float PipeHubReach        { get => GetF("PipeHubReach", 3f);            set => SetF("PipeHubReach", value); }

    // ── Walls ──
    public static float WallNodeSize        { get => GetF("WallNodeSize", 4.5f);          set => SetF("WallNodeSize", value); }
    public static Color WallBodyColour      { get => GetC("WallBodyColour",    new Color(0.72f, 0.66f, 0.58f, 0.9f));  set => SetC("WallBodyColour",    value); }
    public static Color WallSelBodyColour   { get => GetC("WallSelBodyColour", new Color(0.92f, 0.92f, 0.92f, 0.95f)); set => SetC("WallSelBodyColour", value); }
    public static Color WallTowerColour     { get => GetC("WallTowerColour",   new Color(0.3f, 0.26f, 0.22f, 0.85f));  set => SetC("WallTowerColour",   value); }
    public static Color WallSelColour       { get => GetC("WallSelColour",     Color.yellow);                          set => SetC("WallSelColour",     value); }
    public static Color WallOwnHeightColour { get => GetC("WallOwnHeightColour", new Color(1f, 0.6f, 0.15f, 1f));      set => SetC("WallOwnHeightColour", value); }
    public static Color WallOutlineColour   { get => GetC("WallOutlineColour", new Color(0.1f, 0.1f, 0.1f, 0.9f));     set => SetC("WallOutlineColour", value); }
    public static Color WallGhostColour     { get => GetC("WallGhostColour",   new Color(1f, 1f, 1f, 0.3f));           set => SetC("WallGhostColour",   value); }

    // ── Fog Zones ──
    public static Color FogZoneFillColour    { get => GetC("FogZoneFillColour",    new Color(1f, 1f, 1f, 0.20f)); set => SetC("FogZoneFillColour",    value); }
    public static Color FogZoneSelFillColour { get => GetC("FogZoneSelFillColour", new Color(1f, 1f, 1f, 0.35f)); set => SetC("FogZoneSelFillColour", value); }
    public static float FogTriggerWidth      { get => GetF("FogTriggerWidth", 2f);                                set => SetF("FogTriggerWidth", value); }
    public static Color FogTriggerColour     { get => GetC("FogTriggerColour",     new Color(1f, 1f, 1f, 0.50f)); set => SetC("FogTriggerColour",     value); }
    public static Color FogTriggerSelColour  { get => GetC("FogTriggerSelColour",  new Color(1f, 1f, 1f, 0.90f)); set => SetC("FogTriggerSelColour",  value); }
    public static Color FogSceneMeshColour   { get => GetC("FogSceneMeshColour",   new Color(1f, 1f, 1f, 0.35f)); set => SetC("FogSceneMeshColour",   value); }
    public static Color FogSceneMeshSelColour{ get => GetC("FogSceneMeshSelColour",new Color(1f, 1f, 1f, 0.80f)); set => SetC("FogSceneMeshSelColour",value); }
    public static float FogZoneHandleSize     { get => GetF("FogZoneHandleSize", 6f);                              set => SetF("FogZoneHandleSize", value); }
    public static float FogZoneHandleSelSize  { get => GetF("FogZoneHandleSelSize", 8f);                           set => SetF("FogZoneHandleSelSize", value); }
    public static Color FogZoneHandleColour   { get => GetC("FogZoneHandleColour",    new Color(0.7f, 0.7f, 0.7f, 1f)); set => SetC("FogZoneHandleColour",    value); }
    public static Color FogZoneHandleSelColour{ get => GetC("FogZoneHandleSelColour", Color.white);                set => SetC("FogZoneHandleSelColour", value); }

    // ── Fog Repellers ──
    public static bool  ShowFogRepelRadius      { get => GetB("ShowFogRepelRadius", true);                                   set => SetB("ShowFogRepelRadius", value); }
    public static Color FogRepelFillColour      { get => GetC("FogRepelFillColour",      new Color(1f, 1f, 1f, 0.08f)); set => SetC("FogRepelFillColour",      value); }
    public static Color FogRepelSelFillColour   { get => GetC("FogRepelSelFillColour",   new Color(1f, 1f, 1f, 0.16f)); set => SetC("FogRepelSelFillColour",   value); }
    public static float FogRepelEdgeWidth       { get => GetF("FogRepelEdgeWidth", 2f);                                      set => SetF("FogRepelEdgeWidth", value); }
    public static Color FogRepelEdgeColour      { get => GetC("FogRepelEdgeColour",      new Color(1f, 1f, 1f, 0.35f)); set => SetC("FogRepelEdgeColour",      value); }
    public static Color FogRepelSelEdgeColour   { get => GetC("FogRepelSelEdgeColour",   new Color(1f, 1f, 1f, 0.70f)); set => SetC("FogRepelSelEdgeColour",   value); }

    // ── Gizmo visibility ──
    public static bool  ShowPerchRadius   { get => GetB("ShowPerchRadius", true);        set => SetB("ShowPerchRadius", value); }

    // ── Navigation ──
    public static bool  InvertPanX        { get => GetB("InvertPanX", false);            set => SetB("InvertPanX", value); }
    public static bool  InvertPanY        { get => GetB("InvertPanY", false);            set => SetB("InvertPanY", value); }

    // ── Editing ──
    // Seconds a value field waits after its last edit before what it shapes rebuilds.
    public static float RebuildDelay      { get => Mathf.Max(0f, GetF("RebuildDelay", 0.35f)); set => SetF("RebuildDelay", Mathf.Max(0f, value)); }

    const string Prefix = "LSD_Settings_";

    static bool  GetB(string k, bool d)  => EditorPrefs.GetBool (Prefix + k, d);
    static int   GetI(string k, int d)   => EditorPrefs.GetInt  (Prefix + k, d);
    static float GetF(string k, float d) => EditorPrefs.GetFloat(Prefix + k, d);
    static Color GetC(string k, Color d)
        => ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString(Prefix + k, ColorUtility.ToHtmlStringRGBA(d)), out var c) ? c : d;

    static void SetB(string k, bool v)  { EditorPrefs.SetBool (Prefix + k, v); Changed(); }
    static void SetI(string k, int v)   { EditorPrefs.SetInt  (Prefix + k, v); Changed(); }
    static void SetF(string k, float v) { EditorPrefs.SetFloat(Prefix + k, v); Changed(); }
    static void SetC(string k, Color v) { EditorPrefs.SetString(Prefix + k, ColorUtility.ToHtmlStringRGBA(v)); Changed(); }

    static void Changed()
    {
        foreach (var w in Resources.FindObjectsOfTypeAll<LevelSelectDesignerWindow>()) w.Repaint();
        SceneView.RepaintAll();
    }
}


/// <summary>
/// A small dockable companion to the Level Select Designer that gathers its canvas display
/// settings in one place, so they can be tuned while watching the canvas update live.
/// A Section dropdown at the top picks which group of settings is shown.
/// </summary>
public class LevelSelectDesignerSettingsWindow : EditorWindow
{
    Vector2 _scroll;

    static readonly string[] Sections =
    {
        "Landscape", "Node Dots", "Junctions",
        "Rivers", "Arenas", "Soul Routes", "Gizmo Visibility",
        "Pipes", "Walls", "Navigation", "Editing", "Fog", "Camera",
    };

    const string SectionPref = "LSD_Settings_Section";

    [MenuItem("Tools/Level Design/Level Select Designer Settings", false, 121)]
    public static void Open()
    {
        var w = GetWindow<LevelSelectDesignerSettingsWindow>(false, "Level Select Settings", true);
        w.minSize = new Vector2(300f, 240f);
        w.Show();
    }

    void OnGUI()
    {
        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 170f;

        EditorGUILayout.Space(4);
        int section = Mathf.Clamp(EditorPrefs.GetInt(SectionPref, 0), 0, Sections.Length - 1);
        int picked  = EditorGUILayout.Popup("Section", section, Sections);
        if (picked != section) { EditorPrefs.SetInt(SectionPref, picked); _scroll = Vector2.zero; }

        EditorGUILayout.Space(6);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        switch (picked)
        {
            case 0: DrawLandscape();  break;
            case 1: DrawNodeDots();   break;
            case 2: DrawJunctions();  break;
            case 3: DrawRivers();     break;
            case 4: DrawArenas();     break;
            case 5: DrawSoulRoutes(); break;
            case 6: DrawGizmos();     break;
            case 7: DrawPipes();      break;
            case 8: DrawWalls();      break;
            case 9: DrawNavigation(); break;
            case 10: DrawEditing();   break;
            case 11: DrawFog();       break;
            case 12: DrawCamera();    break;
        }

        EditorGUILayout.EndScrollView();
        EditorGUIUtility.labelWidth = prevLW;
    }

    // Everything the landscape shows on the canvas, one header per group.
    static void DrawLandscape()
    {
        Header("Canvas Height Map", first: true);
        DrawHeightMap();
        Header("Contours");
        DrawContours();
        Header("Hill Points");
        DrawHillPoints();
        Header("Landscape Mode");
        DrawRiverStructures();
    }

    static void Header(string title, bool first = false)
    {
        if (!first) EditorGUILayout.Space(12);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    static void DrawHeightMap()
    {
        bool on = EditorGUILayout.Toggle(
            new GUIContent("Show", "The real ground, sampled from the same maths the tiles are baked from."),
            LevelSelectDesignerSettings.MapOn);
        if (on != LevelSelectDesignerSettings.MapOn) LevelSelectDesignerSettings.MapOn = on;

        using (new EditorGUI.DisabledScope(!on))
        {
            var res  = LevelSelectDesignerSettings.MapResolutions;
            int pick = Mathf.Max(0, System.Array.IndexOf(res, LevelSelectDesignerSettings.MapRes));
            int next = EditorGUILayout.Popup("Resolution", pick, System.Array.ConvertAll(res, r => r.ToString()));
            if (next != pick)
            {
                Debug.Log($"[MapResDebug] Resolution picked: {res[next]} (was {res[pick]}), hotControl={GUIUtility.hotControl}, " +
                          $"designers={Resources.FindObjectsOfTypeAll<LevelSelectDesignerWindow>().Length}");
                LevelSelectDesignerSettings.MapRes = res[next];
            }

            Toggle("Low Res While Dragging", "Drops to 64 while a hill is dragged or a slider held, full again on release.",
                   LevelSelectDesignerSettings.MapDragLow, v => LevelSelectDesignerSettings.MapDragLow = v);
            Toggle("Shading", "Colours the ground by its height from the gradient below: a simple height tone, not the scene's lighting. Off = flat grey.",
                   LevelSelectDesignerSettings.MapShade, v => LevelSelectDesignerSettings.MapShade = v);
            using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.MapShade))
            {
                EditorGUI.BeginChangeCheck();
                var grad = EditorGUILayout.GradientField(
                    new GUIContent("Height Gradient", "Left = the lowest ground on the map, middle = the top of the " +
                                                      "base hill (the tile base with none), right = the highest."),
                    LevelSelectDesignerSettings.MapGradient, false);
                if (EditorGUI.EndChangeCheck()) LevelSelectDesignerSettings.MapGradient = grad;
            }
            Toggle("Rocky Noise", "Adds the hills' rocky noise to the height.",
                   LevelSelectDesignerSettings.MapNoise, v => LevelSelectDesignerSettings.MapNoise = v);
            Toggle("River Cuts", "Cuts the runs and pools out, as the scene does.",
                   LevelSelectDesignerSettings.MapCuts, v => LevelSelectDesignerSettings.MapCuts = v);

            var designers = Resources.FindObjectsOfTypeAll<LevelSelectDesignerWindow>();
            string status = designers.Length > 0 ? designers[0].LandscapeMapStatus : null;
            if (status != null) EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }
    }

    static void DrawContours()
    {
        EditorGUILayout.LabelField("Lines of equal height over the Canvas Height Map, rocky noise included " +
                                   "(even with the map's own Rocky Noise off). Needs the height map shown.", EditorStyles.wordWrappedMiniLabel);

        Toggle("Show Contours", "A line every Interval of height above and below the top of the base hill (the tile base with none).",
               LevelSelectDesignerSettings.ContourOn, v => LevelSelectDesignerSettings.ContourOn = v);

        using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.ContourOn))
        {
            Slider(new GUIContent("Interval (m)", "The height between one line and the next."),
                   LevelSelectDesignerSettings.ContourInterval, 0.01f, 5f, v => LevelSelectDesignerSettings.ContourInterval = v);
            Slider("Line Width (px)", LevelSelectDesignerSettings.ContourWidth, 0.5f, 10f, v => LevelSelectDesignerSettings.ContourWidth = v);
            Toggle("Colour by Height", "Each line takes the Height Gradient's colour at its own height; Line and Major " +
                                       "Line Colour then only set how see-through it is. Turn the map's Shading off to " +
                                       "see them — on a shaded map each line matches the ground under it.",
                   LevelSelectDesignerSettings.ContourByHeight, v => LevelSelectDesignerSettings.ContourByHeight = v);
            Colour("Line Colour", LevelSelectDesignerSettings.ContourColour, v => LevelSelectDesignerSettings.ContourColour = v);

            EditorGUILayout.Space(6);
            int every = EditorGUILayout.IntSlider(
                new GUIContent("Major Line Every", "Every Nth line is drawn as a major line, counted from the top of the base hill. 1 = all lines major."),
                LevelSelectDesignerSettings.ContourMajorEvery, 1, 20);
            if (every != LevelSelectDesignerSettings.ContourMajorEvery) LevelSelectDesignerSettings.ContourMajorEvery = every;
            Slider("Major Line Width (px)", LevelSelectDesignerSettings.ContourMajorWidth, 0.5f, 10f, v => LevelSelectDesignerSettings.ContourMajorWidth = v);
            Colour("Major Line Colour", LevelSelectDesignerSettings.ContourMajorColour, v => LevelSelectDesignerSettings.ContourMajorColour = v);

            var designers = Resources.FindObjectsOfTypeAll<LevelSelectDesignerWindow>();
            string status = designers.Length > 0 ? designers[0].LandscapeContourStatus : null;
            if (status != null) EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }
    }

    static void DrawHillPoints()
    {
        EditorGUILayout.LabelField("Shown in Landscape mode.", EditorStyles.miniLabel);

        Slider("Ring Width (px)", LevelSelectDesignerSettings.HillRingWidth, 1f, 20f, v => LevelSelectDesignerSettings.HillRingWidth = v);

        Colour("Ring Colour",     LevelSelectDesignerSettings.HillRingColour,     v => LevelSelectDesignerSettings.HillRingColour     = v);
        Colour("Selected Colour", LevelSelectDesignerSettings.HillSelectedColour, v => LevelSelectDesignerSettings.HillSelectedColour = v);

        Slider("Centre Dot Size (px)", LevelSelectDesignerSettings.HillDotSize, 0f, 20f, v => LevelSelectDesignerSettings.HillDotSize = v);
        Slider("Selected Dot Size (px)", LevelSelectDesignerSettings.HillSelDotSize, 0f, 20f, v => LevelSelectDesignerSettings.HillSelDotSize = v);
        Colour("Centre Dot Colour", LevelSelectDesignerSettings.HillDotColour, v => LevelSelectDesignerSettings.HillDotColour = v);
    }

    static void DrawNodeDots()
    {
        Slider("Dot Size (px)", LevelSelectDesignerSettings.NodeDotSize, 1f, 30f, v => LevelSelectDesignerSettings.NodeDotSize = v);

        using (new EditorGUI.DisabledScope(LevelSelectDesignerSettings.RiverColours))
            Colour(new GUIContent("Dot Colour", "Used while River Colours (Rivers section) is off; on, each dot takes its river's colour."),
                   LevelSelectDesignerSettings.NodeDotColour, v => LevelSelectDesignerSettings.NodeDotColour = v);

        Slider("Selected Ring Size (px)",  LevelSelectDesignerSettings.NodeSelRingSize,  2f, 40f, v => LevelSelectDesignerSettings.NodeSelRingSize  = v);
        Slider("Selected Ring Width (px)", LevelSelectDesignerSettings.NodeSelRingWidth, 1f, 20f, v => LevelSelectDesignerSettings.NodeSelRingWidth = v);
        Colour("Selected Colour", LevelSelectDesignerSettings.NodeSelColour, v => LevelSelectDesignerSettings.NodeSelColour = v);

        EditorGUILayout.Space(6);
        Colour(new GUIContent("Playtest Boat Colour", "The second boat on the map, standing where Play From Playtest Position starts the boat (placed with the ⊙ beside it, under PLAYTEST)."),
               LevelSelectDesignerSettings.PlaytestBoatColour, v => LevelSelectDesignerSettings.PlaytestBoatColour = v);
    }

    static void DrawJunctions()
    {
        Slider("Size (px)",       LevelSelectDesignerSettings.JunctionSize,  2f, 40f, v => LevelSelectDesignerSettings.JunctionSize  = v);
        Slider("Line Width (px)", LevelSelectDesignerSettings.JunctionWidth, 1f, 20f, v => LevelSelectDesignerSettings.JunctionWidth = v);

        Colour("Colour",          LevelSelectDesignerSettings.JunctionColour,    v => LevelSelectDesignerSettings.JunctionColour    = v);
        Colour("Selected Colour", LevelSelectDesignerSettings.JunctionSelColour, v => LevelSelectDesignerSettings.JunctionSelColour = v);
        Colour(new GUIContent("Highlight Colour", "The junction picked in the Junctions list."),
               LevelSelectDesignerSettings.JunctionHighlightColour, v => LevelSelectDesignerSettings.JunctionHighlightColour = v);
    }

    static void DrawRivers()
    {
        Toggle("River Colours", "Draws each river and pool in its own colour. Off, they all share River Colour.",
               LevelSelectDesignerSettings.RiverColours, v => LevelSelectDesignerSettings.RiverColours = v);
        using (new EditorGUI.DisabledScope(LevelSelectDesignerSettings.RiverColours))
            Colour(new GUIContent("River Colour", "The one colour every river and pool is drawn in while River Colours is off."),
                   LevelSelectDesignerSettings.RiverSingleColour, v => LevelSelectDesignerSettings.RiverSingleColour = v);

        EditorGUILayout.Space(6);
        Toggle("Outlines", "Every river edge: both sides of each run's rims, and the rings round each pool.",
               LevelSelectDesignerSettings.RiverOutline, v => LevelSelectDesignerSettings.RiverOutline = v);
        using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.RiverOutline))
        {
            Slider("Outline Width (px)", LevelSelectDesignerSettings.RiverOutlineWidth, 1f, 20f, v => LevelSelectDesignerSettings.RiverOutlineWidth = v);

            using (new EditorGUI.DisabledScope(LevelSelectDesignerSettings.RiverColours))
                Colour(new GUIContent("Outline Colour", "Used while River Colours is off; on, each outline takes its river's colour."),
                       LevelSelectDesignerSettings.RiverOutlineColour, v => LevelSelectDesignerSettings.RiverOutlineColour = v);
        }

        EditorGUILayout.Space(6);
        Toggle("Arrows", "The direction arrows along each run and round each pool.",
               LevelSelectDesignerSettings.RiverArrows, v => LevelSelectDesignerSettings.RiverArrows = v);
        using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.RiverArrows))
        {
            Slider("Arrow Size (px)",  LevelSelectDesignerSettings.RiverArrowSize,  2f, 40f, v => LevelSelectDesignerSettings.RiverArrowSize  = v);
            Slider("Arrow Width (px)", LevelSelectDesignerSettings.RiverArrowWidth, 1f, 20f, v => LevelSelectDesignerSettings.RiverArrowWidth = v);

            using (new EditorGUI.DisabledScope(LevelSelectDesignerSettings.RiverColours))
                Colour(new GUIContent("Arrow Colour", "Used while River Colours is off; on, each arrow takes its river's colour."),
                       LevelSelectDesignerSettings.RiverArrowColour, v => LevelSelectDesignerSettings.RiverArrowColour = v);
        }

        EditorGUILayout.Space(6);
        Toggle("Fill", "A solid fill across each run and pool, under its lines, in the river's colour.",
               LevelSelectDesignerSettings.RiverFill, v => LevelSelectDesignerSettings.RiverFill = v);
        using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.RiverFill))
            Slider("Fill Opacity", LevelSelectDesignerSettings.RiverFillOpacity, 0f, 1f, v => LevelSelectDesignerSettings.RiverFillOpacity = v);
    }

    static void DrawArenas()
    {
        Toggle("Outlines", "The arena ring and the faint ring just outside it.",
               LevelSelectDesignerSettings.ArenaOutline, v => LevelSelectDesignerSettings.ArenaOutline = v);
        using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.ArenaOutline))
        {
            Slider("Outline Width (px)", LevelSelectDesignerSettings.ArenaOutlineWidth, 1f, 20f, v => LevelSelectDesignerSettings.ArenaOutlineWidth = v);

            Colour("Outline Colour",    LevelSelectDesignerSettings.ArenaOutlineColour,   v => LevelSelectDesignerSettings.ArenaOutlineColour   = v);
            Colour("Outer Ring Colour", LevelSelectDesignerSettings.ArenaOuterRingColour, v => LevelSelectDesignerSettings.ArenaOuterRingColour = v);
        }

        EditorGUILayout.Space(6);
        Toggle("Fill", "A solid fill inside each arena ring, under its lines.",
               LevelSelectDesignerSettings.ArenaFill, v => LevelSelectDesignerSettings.ArenaFill = v);
        using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.ArenaFill))
            Colour("Fill Colour", LevelSelectDesignerSettings.ArenaFillColour, v => LevelSelectDesignerSettings.ArenaFillColour = v);
    }

    static void DrawSoulRoutes()
    {
        Toggle("Show", "Draws the soul route bands on the canvas.",
               LevelSelectDesignerSettings.SoulShow, v => LevelSelectDesignerSettings.SoulShow = v);

        using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.SoulShow))
        {
            EditorGUILayout.Space(6);
            Toggle("Route Colours", "Each route in its own colour. Off, they all share Route Colour.",
                   LevelSelectDesignerSettings.SoulRouteColours, v => LevelSelectDesignerSettings.SoulRouteColours = v);
            using (new EditorGUI.DisabledScope(LevelSelectDesignerSettings.SoulRouteColours))
                Colour("Route Colour", LevelSelectDesignerSettings.SoulRouteColour, v => LevelSelectDesignerSettings.SoulRouteColour = v);

            Slider("Band Opacity",          LevelSelectDesignerSettings.SoulBandOpacity,    0f, 1f, v => LevelSelectDesignerSettings.SoulBandOpacity    = v);
            Slider("Selected Band Opacity", LevelSelectDesignerSettings.SoulSelBandOpacity, 0f, 1f, v => LevelSelectDesignerSettings.SoulSelBandOpacity = v);

            EditorGUILayout.Space(6);
            Toggle("Points", "The disc and dot at each authored point.",
                   LevelSelectDesignerSettings.SoulPoints, v => LevelSelectDesignerSettings.SoulPoints = v);
            Colour(new GUIContent("Dot Colour", "The centre dot on each point and on the origin."),
                   LevelSelectDesignerSettings.SoulDotColour, v => LevelSelectDesignerSettings.SoulDotColour = v);

            EditorGUILayout.Space(6);
            Toggle("Arrows", "The chevrons along a route, pointing the way souls travel.",
                   LevelSelectDesignerSettings.SoulArrows, v => LevelSelectDesignerSettings.SoulArrows = v);
            using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.SoulArrows))
            {
                Toggle("Arrows On All Routes", "Off, only the selected route shows its arrows.",
                       LevelSelectDesignerSettings.SoulArrowsAll, v => LevelSelectDesignerSettings.SoulArrowsAll = v);
                Slider("Arrow Scale", LevelSelectDesignerSettings.SoulArrowScale, 0.25f, 4f, v => LevelSelectDesignerSettings.SoulArrowScale = v);
                Colour("Arrow Colour", LevelSelectDesignerSettings.SoulArrowColour, v => LevelSelectDesignerSettings.SoulArrowColour = v);
            }
        }
    }

    static void DrawCamera()
    {
        Toggle("Show Cameras", "Off, the canvas draws no cameras: neither the Cinematic node symbols nor the shot cameras.",
               LevelSelectDesignerSettings.ShowCameras, v => LevelSelectDesignerSettings.ShowCameras = v);

        EditorGUILayout.Space(6);
        using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.ShowCameras))
        {
            Toggle("Camera Symbol", "A little camera above each node with Cinematic on (a camera sequence plays there).",
                   LevelSelectDesignerSettings.CameraSymbol, v => LevelSelectDesignerSettings.CameraSymbol = v);
            using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.CameraSymbol))
            {
                Slider("Camera Symbol Size (px)", LevelSelectDesignerSettings.CameraSymbolSize, 4f, 40f, v => LevelSelectDesignerSettings.CameraSymbolSize = v);
                Colour("Camera Symbol Colour", LevelSelectDesignerSettings.CameraSymbolColour, v => LevelSelectDesignerSettings.CameraSymbolColour = v);
            }

            EditorGUILayout.Space(6);
            Toggle("Shot Cameras", "A camera where each camera sequence preset's shot camera stands. Click one in Select mode to clamp it.",
                   LevelSelectDesignerSettings.ShotCameras, v => LevelSelectDesignerSettings.ShotCameras = v);
            using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.ShotCameras))
            {
                Slider("Shot Camera Size (px)", LevelSelectDesignerSettings.ShotCameraSize, 4f, 40f, v => LevelSelectDesignerSettings.ShotCameraSize = v);
                Colour("Shot Camera Colour", LevelSelectDesignerSettings.ShotCameraColour, v => LevelSelectDesignerSettings.ShotCameraColour = v);
            }
        }
    }

    static void DrawGizmos()
    {
        Toggle("Perch Radius", "The washed disc round each perch — markers, spikes and structures.",
               LevelSelectDesignerSettings.ShowPerchRadius, v => LevelSelectDesignerSettings.ShowPerchRadius = v);
    }

    static void DrawPipes()
    {
        Slider(new GUIContent("Opacity", "How solid the pipes draw on the map: body, bore, supports and nodes. 1 = fully solid."),
               LevelSelectDesignerSettings.PipeOpacity, 0f, 1f, v => LevelSelectDesignerSettings.PipeOpacity = v);

        EditorGUILayout.Space(6);
        Colour("Body Colour",          LevelSelectDesignerSettings.PipeBodyColour,    v => LevelSelectDesignerSettings.PipeBodyColour    = v);
        Colour(new GUIContent("Bore Colour", "The darker line down the middle for the hollow."),
               LevelSelectDesignerSettings.PipeBoreColour, v => LevelSelectDesignerSettings.PipeBoreColour = v);
        Colour("Selected Body Colour", LevelSelectDesignerSettings.PipeSelBodyColour, v => LevelSelectDesignerSettings.PipeSelBodyColour = v);
        Colour("Selected Bore Colour", LevelSelectDesignerSettings.PipeSelBoreColour, v => LevelSelectDesignerSettings.PipeSelBoreColour = v);

        EditorGUILayout.Space(6);
        Colour(new GUIContent("Support Colour", "The bar laid across the pipe at each support."),
               LevelSelectDesignerSettings.PipeSupportColour, v => LevelSelectDesignerSettings.PipeSupportColour = v);

        EditorGUILayout.Space(6);
        Slider("Node Dot Size (px)", LevelSelectDesignerSettings.PipeNodeSize, 1f, 20f, v => LevelSelectDesignerSettings.PipeNodeSize = v);
        Colour(new GUIContent("Own Height Colour", "A node that overrides the pipe's height."),
               LevelSelectDesignerSettings.PipeOwnHeightColour, v => LevelSelectDesignerSettings.PipeOwnHeightColour = v);
        Colour(new GUIContent("Selected Colour", "The selected node or support."),
               LevelSelectDesignerSettings.PipeSelColour, v => LevelSelectDesignerSettings.PipeSelColour = v);
        Colour(new GUIContent("Outline Colour", "The dark edge round each node and support."),
               LevelSelectDesignerSettings.PipeOutlineColour, v => LevelSelectDesignerSettings.PipeOutlineColour = v);

        EditorGUILayout.Space(6);
        Colour(new GUIContent("Drawing Line Colour", "The dotted line out to the cursor while a pipe is being laid."),
               LevelSelectDesignerSettings.PipeGhostColour, v => LevelSelectDesignerSettings.PipeGhostColour = v);

        EditorGUILayout.Space(6);
        EditorGUI.BeginChangeCheck();
        float reach = EditorGUILayout.FloatField(
            new GUIContent("Hub Reach (m)", "How far from a soul juice tank a pipe hub can be and still be " +
                                            "offered in the tank's Intake Hub list."),
            LevelSelectDesignerSettings.PipeHubReach);
        if (EditorGUI.EndChangeCheck()) LevelSelectDesignerSettings.PipeHubReach = Mathf.Max(0f, reach);
    }

    static void DrawWalls()
    {
        Colour("Body Colour",          LevelSelectDesignerSettings.WallBodyColour,    v => LevelSelectDesignerSettings.WallBodyColour    = v);
        Colour("Selected Body Colour", LevelSelectDesignerSettings.WallSelBodyColour, v => LevelSelectDesignerSettings.WallSelBodyColour = v);
        Colour(new GUIContent("Tower Base Colour", "The wider disc under a node carrying a tower."),
               LevelSelectDesignerSettings.WallTowerColour, v => LevelSelectDesignerSettings.WallTowerColour = v);

        EditorGUILayout.Space(6);
        Slider("Node Dot Size (px)", LevelSelectDesignerSettings.WallNodeSize, 1f, 20f, v => LevelSelectDesignerSettings.WallNodeSize = v);
        Colour(new GUIContent("Own Height Colour", "A node that overrides the wall's height."),
               LevelSelectDesignerSettings.WallOwnHeightColour, v => LevelSelectDesignerSettings.WallOwnHeightColour = v);
        Colour("Selected Colour", LevelSelectDesignerSettings.WallSelColour, v => LevelSelectDesignerSettings.WallSelColour = v);
        Colour(new GUIContent("Outline Colour", "The dark edge round each node."),
               LevelSelectDesignerSettings.WallOutlineColour, v => LevelSelectDesignerSettings.WallOutlineColour = v);

        EditorGUILayout.Space(6);
        Colour(new GUIContent("Drawing Line Colour", "The dotted line out to the cursor while a wall is being laid."),
               LevelSelectDesignerSettings.WallGhostColour, v => LevelSelectDesignerSettings.WallGhostColour = v);
    }

    // Fog on the canvas: the zones, then the repellers' radii.
    static void DrawFog()
    {
        Header("Zones", first: true);
        DrawFogZones();
        Header("Repellers");
        DrawFogRepellers();
    }

    static void DrawFogRepellers()
    {
        Toggle("Show Radius", "The disc fog is held off from round each structure with Fog Repeller ticked.",
               LevelSelectDesignerSettings.ShowFogRepelRadius, v => LevelSelectDesignerSettings.ShowFogRepelRadius = v);

        using (new EditorGUI.DisabledScope(!LevelSelectDesignerSettings.ShowFogRepelRadius))
        {
            Colour(new GUIContent("Fill Colour", "The radius, filled. Alpha sets how solid it draws."),
                   LevelSelectDesignerSettings.FogRepelFillColour, v => LevelSelectDesignerSettings.FogRepelFillColour = v);
            Colour("Selected Fill Colour",
                   LevelSelectDesignerSettings.FogRepelSelFillColour, v => LevelSelectDesignerSettings.FogRepelSelFillColour = v);

            EditorGUILayout.Space(6);
            Slider(new GUIContent("Edge Width (px)", "The line round the radius. 0 = no line."),
                   LevelSelectDesignerSettings.FogRepelEdgeWidth, 0f, 20f, v => LevelSelectDesignerSettings.FogRepelEdgeWidth = v);
            Colour("Edge Colour",
                   LevelSelectDesignerSettings.FogRepelEdgeColour, v => LevelSelectDesignerSettings.FogRepelEdgeColour = v);
            Colour("Selected Edge Colour",
                   LevelSelectDesignerSettings.FogRepelSelEdgeColour, v => LevelSelectDesignerSettings.FogRepelSelEdgeColour = v);
        }
    }

    static void DrawFogZones()
    {
        Colour(new GUIContent("Zone Fill Colour", "The fog sheet, filled. Alpha sets how solid it draws."),
               LevelSelectDesignerSettings.FogZoneFillColour, v => LevelSelectDesignerSettings.FogZoneFillColour = v);
        Colour("Selected Zone Fill Colour",
               LevelSelectDesignerSettings.FogZoneSelFillColour, v => LevelSelectDesignerSettings.FogZoneSelFillColour = v);

        EditorGUILayout.Space(6);
        Slider(new GUIContent("Handle Size (px)", "The dot on each sheet's centre that picks up the whole zone."),
               LevelSelectDesignerSettings.FogZoneHandleSize, 1f, 20f, v => LevelSelectDesignerSettings.FogZoneHandleSize = v);
        Slider("Selected Handle Size (px)",
               LevelSelectDesignerSettings.FogZoneHandleSelSize, 1f, 20f, v => LevelSelectDesignerSettings.FogZoneHandleSelSize = v);
        Colour("Handle Colour",
               LevelSelectDesignerSettings.FogZoneHandleColour, v => LevelSelectDesignerSettings.FogZoneHandleColour = v);
        Colour("Selected Handle Colour",
               LevelSelectDesignerSettings.FogZoneHandleSelColour, v => LevelSelectDesignerSettings.FogZoneHandleSelColour = v);

        EditorGUILayout.Space(6);
        Slider(new GUIContent("Trigger Line Width (px)", "The trigger bounds, drawn as an outline only."),
               LevelSelectDesignerSettings.FogTriggerWidth, 1f, 20f, v => LevelSelectDesignerSettings.FogTriggerWidth = v);
        Colour("Trigger Line Colour",
               LevelSelectDesignerSettings.FogTriggerColour, v => LevelSelectDesignerSettings.FogTriggerColour = v);
        Colour("Selected Trigger Line Colour",
               LevelSelectDesignerSettings.FogTriggerSelColour, v => LevelSelectDesignerSettings.FogTriggerSelColour = v);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Scene view: the trigger box shows on the selected zone only, " +
                                   "in the line width and Selected Trigger Line Colour above.",
                                   EditorStyles.wordWrappedMiniLabel);
        Colour(new GUIContent("Scene Mesh Colour", "Each sheet's mesh, drawn as a wireframe in the Scene view while in Fog Zones mode."),
               LevelSelectDesignerSettings.FogSceneMeshColour, v => LevelSelectDesignerSettings.FogSceneMeshColour = v);
        Colour("Selected Scene Mesh Colour",
               LevelSelectDesignerSettings.FogSceneMeshSelColour, v => LevelSelectDesignerSettings.FogSceneMeshSelColour = v);
    }

    static void DrawNavigation()
    {
        Toggle("Invert Pan Horizontal", "Space + drag and middle-mouse drag, left/right: " +
                                        "off, the map follows the mouse; on, the map moves against it.",
               LevelSelectDesignerSettings.InvertPanX, v => LevelSelectDesignerSettings.InvertPanX = v);
        Toggle("Invert Pan Vertical", "Space + drag and middle-mouse drag, up/down: " +
                                      "off, the map follows the mouse; on, the map moves against it.",
               LevelSelectDesignerSettings.InvertPanY, v => LevelSelectDesignerSettings.InvertPanY = v);
    }

    static void DrawEditing()
    {
        EditorGUI.BeginChangeCheck();
        float delay = EditorGUILayout.FloatField(
            new GUIContent("Rebuild Delay (s)",
                "How long a value field waits after its last edit before what it shapes rebuilds, so " +
                "dragging or typing a value rebuilds once when you pause rather than on every step. " +
                "0 = rebuild on every step. Toggles, dropdowns and buttons always rebuild at once."),
            LevelSelectDesignerSettings.RebuildDelay);
        if (EditorGUI.EndChangeCheck()) LevelSelectDesignerSettings.RebuildDelay = delay;
    }

    static void DrawRiverStructures()
    {
        Slider(new GUIContent("River Structure Opacity",
                   "While in Landscape mode: how solid the river runs, pools, pool towers, walkways, bridges, " +
                   "archways, outposts, pipes, walls and rim nodes draw, so the ground shows through them. " +
                   "1 = as in every other mode."),
               LevelSelectDesignerSettings.LandscapeStructureOpacity, 0f, 1f,
               v => LevelSelectDesignerSettings.LandscapeStructureOpacity = v);
        Toggle("River Nodes", "While in Landscape mode: shows the river node dots.",
               LevelSelectDesignerSettings.LandscapeShowRiverNodes, v => LevelSelectDesignerSettings.LandscapeShowRiverNodes = v);
        Toggle("Junctions", "While in Landscape mode: shows the junction diamonds.",
               LevelSelectDesignerSettings.LandscapeShowJunctions, v => LevelSelectDesignerSettings.LandscapeShowJunctions = v);
        Toggle("Arenas", "While in Landscape mode: shows the arenas — their nodes, rings and entrances.",
               LevelSelectDesignerSettings.LandscapeShowArenas, v => LevelSelectDesignerSettings.LandscapeShowArenas = v);
        Toggle("Soul Paths", "While in Landscape mode: shows the soul paths.",
               LevelSelectDesignerSettings.LandscapeShowSoulPaths, v => LevelSelectDesignerSettings.LandscapeShowSoulPaths = v);
        Toggle("Spikes", "While in Landscape mode: shows the spikes.",
               LevelSelectDesignerSettings.LandscapeShowSpikes, v => LevelSelectDesignerSettings.LandscapeShowSpikes = v);
    }

    static void Toggle(string label, string tip, bool value, System.Action<bool> set)
    {
        bool v = EditorGUILayout.Toggle(new GUIContent(label, tip), value);
        if (v != value) set(v);
    }

    static void Slider(string label, float value, float min, float max, System.Action<float> set)
        => Slider(new GUIContent(label), value, min, max, set);

    static void Slider(GUIContent label, float value, float min, float max, System.Action<float> set)
    {
        float v = EditorGUILayout.Slider(label, value, min, max);
        if (!Mathf.Approximately(v, value)) set(v);
    }

    static void Colour(string label, Color value, System.Action<Color> set) => Colour(new GUIContent(label), value, set);

    static void Colour(GUIContent label, Color value, System.Action<Color> set)
    {
        Color v = EditorGUILayout.ColorField(label, value);
        if (v != value) set(v);
    }
}
