using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Landscape Maps — the second tool under the Landscape button's arrow.
///
/// Three grids painted over the landscape tile area, held together in one
/// <see cref="LandscapeMapsPreset"/>:
///   Height   black = Low, white = High: the base the hill points (when on) add to
///   Noise    white = full rocky noise, black = smooth, pushing by Noise Height
///   Texture  Red, Blue or Yellow: the designations the Level Select Landscape Tuner shades by;
///            unpainted ground is Landscape. Draw only — no Blend, Lower or Higher.
///
/// The panel edits a working copy, like every tuner: the Active Preset row at the top, Load / Save
/// / Save as New, and Save held until the row's preset was actually loaded. The working copy drives
/// the scene's <see cref="LandscapeTool"/> as you paint, so the tiles re-bake live.
///
/// Changing a grid's density stretches what is painted to the new cell count. Changing the tiles
/// does not: each grid keeps its own world area and grows black cells wherever the tiles reach past it.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private DesignerMode _landscapeVariant = DesignerMode.Landscape;

    // The working copy and the preset its values came from — serialized so a recompile keeps them.
    [SerializeField] private LandscapeMaps       _mapsWorking = new LandscapeMaps();
    [SerializeField] private LandscapeMapsPreset _mapsLoadedPreset;
    // Set once the working copy has reached the scene (a Load or an edit), so an undo never pushes
    // a copy that was never loaded over the scene's maps.
    [SerializeField] private bool                _mapsTouched;
    private readonly TunerUnsavedReminder _mapsSaveReminder = new TunerUnsavedReminder("LevelSelectDesignerWindow_LandscapeMaps");

    private int   _mapsTab;                       // 0 Height, 1 Noise, 2 Texture
    private bool  _isPaintingLandscapeMap;
    private int   _mapsStrokeUndoGroup;
    private Vector2 _mapsLastPaintXZ;
    private Vector2 _mapsBrushXZ;                 // where the mouse is, world XZ
    private bool    _mapsBrushShown;
    private bool    _mapsPicking;                 // eye dropper armed: the next click samples Colour
    private bool    _isPickingLandscapeMap;       // eye dropper held down, sampling as it drags
    private ulong _mapsPushedHash;
    private Texture2D _mapsOverlay;
    private bool      _mapsOverlayDirty = true;
    private int       _mapsOverlayTab   = -1;
    private Color32[] _mapsOverlayPixels;

    private const float MapsOverlayAlpha = 0.8f;
    private const int   MapsMaxBrushSize = 64;

    private static readonly string[] MapsTabLabels  = { "Height", "Noise", "Texture" };
    private static readonly string[] MapsTextureColours = { "Erase", "Red", "Blue", "Yellow" };

    // How each Texture map colour shows on the canvas; index = the cell's LandscapeMaps.Texture* number.
    private static readonly Color32[] MapsTextureCanvas =
    {
        new Color32(0, 0, 0, 0), new Color32(230, 40, 40, 255), new Color32(40, 90, 235, 255), new Color32(245, 215, 30, 255),
    };
    private static readonly string[] MapsBrushModes = { "Draw", "Blend", "Lower", "Higher" };
    private static readonly string[] MapsDrawModes  = { "Normal", "Lighter Colour", "Darker Colour" };
    private static readonly List<LevelSelectDesignerData.LandscapeHillPoint> NoHillPoints = new();

    // Brush settings are the window's, not the preset's — kept per tab between sessions.
    private const string K_MapsBrushColour = "LSD_LandscapeMaps_BrushColour_";
    private const string K_MapsBrushSize   = "LSD_LandscapeMaps_BrushSize_";
    private const string K_MapsBrushMode   = "LSD_LandscapeMaps_BrushMode_";
    private const string K_MapsBlendStrength = "LSD_LandscapeMaps_BlendStrength_";
    private const string K_MapsStepAmount    = "LSD_LandscapeMaps_StepAmount_";
    private const string K_MapsWholeAmount   = "LSD_LandscapeMaps_WholeAmount_";
    private const string K_MapsDrawMode      = "LSD_LandscapeMaps_DrawMode_";

    /// <summary>
    /// Draw: 0 Normal (the cell becomes the brush grey), 1 Lighter Colour (keeps whichever is
    /// lighter, the cell or the brush), 2 Darker Colour (keeps whichever is darker).
    /// </summary>
    private int MapsDrawMode
    {
        get => MapsTextureTab ? 0 : EditorPrefs.GetInt(K_MapsDrawMode + _mapsTab, 0);   // Texture: Normal only
        set => EditorPrefs.SetInt(K_MapsDrawMode + _mapsTab, Mathf.Clamp(value, 0, MapsDrawModes.Length - 1));
    }

    private bool MapsTextureTab => _mapsTab == 2;

    /// <summary>
    /// Height and Noise: the brush grey, 0–1. Texture: the colour as a cell number — 0 Erase,
    /// 1 Red, 2 Blue, 3 Yellow (LandscapeMaps.Texture*).
    /// </summary>
    private float MapsBrushColour
    {
        get => EditorPrefs.GetFloat(K_MapsBrushColour + _mapsTab, 1f);
        set => EditorPrefs.SetFloat(K_MapsBrushColour + _mapsTab,
                                    MapsTextureTab ? Mathf.Clamp(Mathf.Round(value), 0f, 3f) : Mathf.Clamp01(value));
    }

    private int MapsBrushSize
    {
        get => EditorPrefs.GetInt(K_MapsBrushSize + _mapsTab, 3);
        set => EditorPrefs.SetInt(K_MapsBrushSize + _mapsTab, Mathf.Clamp(value, 1, MapsMaxBrushSize));
    }

    /// <summary>
    /// 0 Draw (sets cells to the brush grey), 1 Blend (blurs the cells under the brush),
    /// 2 Lower / 3 Higher (darkens / lightens the cells a stroke crosses).
    /// </summary>
    private int MapsBrushMode
    {
        get => MapsTextureTab ? 0 : EditorPrefs.GetInt(K_MapsBrushMode + _mapsTab, 0);   // Texture: Draw only
        set => EditorPrefs.SetInt(K_MapsBrushMode + _mapsTab, Mathf.Clamp(value, 0, MapsBrushModes.Length - 1));
    }

    private bool MapsUsesColour => MapsBrushMode == 0;

    /// <summary>Blend: how far each pass moves a cell toward its blurred value, 0 none to 1 all the way.</summary>
    private float MapsBlendStrength
    {
        get => EditorPrefs.GetFloat(K_MapsBlendStrength + _mapsTab, 1f);
        set => EditorPrefs.SetFloat(K_MapsBlendStrength + _mapsTab, Mathf.Clamp01(value));
    }

    /// <summary>Lower / Higher: how far one stroke darkens or lightens the cells it crosses, on the 0–1 grey.</summary>
    private float MapsStepAmount
    {
        get => EditorPrefs.GetFloat(K_MapsStepAmount + _mapsTab, 0.1f);
        set => EditorPrefs.SetFloat(K_MapsStepAmount + _mapsTab, Mathf.Clamp01(value));
    }

    /// <summary>Whole Map: how far Lower All / Raise All moves every cell, on the 0–1 grey.</summary>
    private float MapsWholeAmount
    {
        get => EditorPrefs.GetFloat(K_MapsWholeAmount + _mapsTab, 0.1f);
        set => EditorPrefs.SetFloat(K_MapsWholeAmount + _mapsTab, Mathf.Clamp01(value));
    }

    // Scratch for a Blend stamp's row blur, reused between stamps.
    private static float[] _mapsBlendRows = new float[0];
    // Lower / Higher: the cells this stroke has already stepped, so each moves once per stroke.
    private bool[] _mapsStrokeTouched;

    private LandscapeMapGrid ActiveMapGrid => _mapsTab switch
    {
        0 => _mapsWorking.height,
        1 => _mapsWorking.noise,
        _ => _mapsWorking.texture,
    };

    /// <summary>The hill points the ground is shaped by: all of them, or none while the switch is off.</summary>
    private List<LevelSelectDesignerData.LandscapeHillPoint> ActiveHillPoints
        => _data.hillPointsOn ? _data.hillPoints : NoHillPoints;

    /// <summary>The tile area the painted maps are laid over: x/y the min world X/Z, width/height its extent.</summary>
    private Rect LandscapeTileArea
        => new Rect(_data.landscapeOffset.x, _data.landscapeOffset.y,
                    _data.landscapeTilesX * _data.landscapeTileSize, _data.landscapeTilesZ * _data.landscapeTileSize);

    private bool InLandscapeMode => _mode == DesignerMode.Landscape || _mode == DesignerMode.LandscapeMaps;

    /// <summary>Painting is open while the working copy came from the row's preset, or there is none yet.</summary>
    private bool CanEditLandscapeMaps
        => _data != null && (_data.landscapeMapsPreset == null ||
                             TunerLoadedPreset.IsLoaded(_data.landscapeMapsPreset, _mapsLoadedPreset));

    // ── Mode bar ──

    /// <summary>
    /// The Landscape button, with an arrow beside it to choose between Landscape (hill points) and
    /// Landscape Maps. The button itself gives whichever was picked last.
    /// </summary>
    private void DrawLandscapeButton()
    {
        bool   on  = InLandscapeMode;
        bool   map = _landscapeVariant == DesignerMode.LandscapeMaps;
        if (GUILayout.Toggle(on, map ? "Landscape Maps" : "Landscape", EditorStyles.toolbarButton,
                             GUILayout.Width(map ? 100f : 72f), GUILayout.Height(18)) && !on)
            SetMode(_landscapeVariant);

        var  arrow = new GUIContent("", "Choose the Landscape tool: Landscape for the hill points, or Landscape Maps to paint the base height, rocky noise and texture designations.");
        Rect rect  = GUILayoutUtility.GetRect(arrow, EditorStyles.toolbarDropDown, GUILayout.Width(18f));
        if (!EditorGUI.DropdownButton(rect, arrow, FocusType.Passive, EditorStyles.toolbarDropDown)) return;

        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Landscape"), _mode == DesignerMode.Landscape,
                     () => { _pendingMode = DesignerMode.Landscape; Repaint(); });
        menu.AddItem(new GUIContent("Landscape Maps"), _mode == DesignerMode.LandscapeMaps,
                     () => { _pendingMode = DesignerMode.LandscapeMaps; Repaint(); });
        menu.DropDown(rect);
    }

    // ── Left panel ──

    private void DrawLandscapeMapsPanel()
    {
        if (_data == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Landscape Maps", EditorStyles.boldLabel);
        DrawLandscapeMapsPresetRow();

        EditorGUILayout.Space(6);
        int tab = GUILayout.Toolbar(_mapsTab, MapsTabLabels);
        if (tab != _mapsTab)
        {
            _mapsTab          = tab;
            _mapsOverlayDirty = true;
            _mapsPicking      = false;
            Repaint();
        }
        EditorGUILayout.Space(4);

        using (new EditorGUI.DisabledScope(!CanEditLandscapeMaps))
        {
            DrawMapDensity(ActiveMapGrid);

            EditorGUI.BeginChangeCheck();
            float low = _mapsWorking.low, high = _mapsWorking.high, noiseHeight = _mapsWorking.noiseHeight;
            if (_mapsTab == 0)
            {
                low  = EditorGUILayout.FloatField(new GUIContent("Low",  "Metres above the tile base where the map is black."), low);
                high = EditorGUILayout.FloatField(new GUIContent("High", "Metres above the tile base where the map is white."), high);
            }
            else if (_mapsTab == 1)
            {
                noiseHeight = EditorGUILayout.FloatField(
                    new GUIContent("Noise Height", "How far full (white) rocky noise pushes the ground, in metres."), noiseHeight);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Landscape Maps");
                _mapsWorking.low         = low;
                _mapsWorking.high        = high;
                _mapsWorking.noiseHeight = noiseHeight;
                PushLandscapeMaps(true);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            if (MapsTextureTab) DrawMapsTextureColour();
            else DrawMapsGreyBrush();
            if (!MapsUsesColour) _mapsPicking = false;
            int size = EditorGUILayout.IntSlider(new GUIContent("Size", "Brush width in cells."),
                                                 MapsBrushSize, 1, MapsMaxBrushSize);
            if (size != MapsBrushSize) MapsBrushSize = size;

            if (MapsBrushMode == 1)
            {
                float strength = EditorGUILayout.Slider(
                    new GUIContent("Blend Strength", "How much each pass smooths: low eases the cells gently, 1 blurs them fully."),
                    MapsBlendStrength, 0f, 1f);
                if (strength != MapsBlendStrength) MapsBlendStrength = strength;
            }
            else if (MapsBrushMode >= 2)
            {
                float amount = EditorGUILayout.Slider(
                    new GUIContent(MapsBrushMode == 2 ? "Lower By" : "Higher By",
                                   "How much each stroke darkens or lightens the cells it crosses: 1 is black to white."),
                    MapsStepAmount, 0f, 1f);
                if (amount != MapsStepAmount) MapsStepAmount = amount;
            }
        }

        EditorGUILayout.Space(4);
        string hint = MapsBrushMode switch
        {
            1 => "Drag over painted cells to blur them into even slopes.",
            2 => "Each stroke darkens the cells it crosses by Lower By.",
            3 => "Each stroke lightens the cells it crosses by Higher By.",
            _ when _mapsPicking => "Click on the canvas to take Colour from the painting.",
            _ when MapsTextureTab => "Click and drag on the canvas to paint. Erase takes it back to Landscape.",
            _ => "Click and drag on the canvas to paint.",
        };
        EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);

        // Lower All / Raise All shift greys; the Texture map's cells are colours.
        if (MapsTextureTab) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Whole Map", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!CanEditLandscapeMaps || ActiveMapGrid.IsEmpty))
        {
            float amount = EditorGUILayout.Slider(
                new GUIContent("Amount", "How much Lower All / Raise All darkens or lightens every cell: 1 is black to white."),
                MapsWholeAmount, 0f, 1f);
            if (amount != MapsWholeAmount) MapsWholeAmount = amount;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Lower All", "Darkens every cell of this map by Amount.")))
                    ShiftWholeLandscapeMap(-MapsWholeAmount);
                if (GUILayout.Button(new GUIContent("Raise All", "Lightens every cell of this map by Amount.")))
                    ShiftWholeLandscapeMap(MapsWholeAmount);
            }
        }
    }

    /// <summary>The Height and Noise tabs' brush: mode, Draw's blend mode, the grey and the eye dropper.</summary>
    private void DrawMapsGreyBrush()
    {
        int mode = GUILayout.Toolbar(MapsBrushMode, MapsBrushModes);
        if (mode != MapsBrushMode) MapsBrushMode = mode;

        if (MapsUsesColour)
        {
            int drawMode = GUILayout.Toolbar(MapsDrawMode, MapsDrawModes, EditorStyles.miniButton);
            if (drawMode != MapsDrawMode) MapsDrawMode = drawMode;
        }

        using (new EditorGUI.DisabledScope(!MapsUsesColour))
        using (new EditorGUILayout.HorizontalScope())
        {
            float colour = EditorGUILayout.Slider(
                new GUIContent("Colour", _mapsTab == 0 ? "Black = Low, white = High." : "Black = smooth, white = full rocky noise."),
                MapsBrushColour, 0f, 1f);
            if (colour != MapsBrushColour) MapsBrushColour = colour;

            Rect swatch = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(swatch, new Color(colour, colour, colour, 1f));

            var dropper = EditorGUIUtility.TrIconContent("EyeDropper.Large",
                "Eye dropper: click (or drag) on the canvas to take Colour from the painting, then carry on drawing.");
            bool picking = GUILayout.Toggle(_mapsPicking, dropper, EditorStyles.miniButton,
                                            GUILayout.Width(24f), GUILayout.Height(18f));
            if (picking != _mapsPicking) { _mapsPicking = picking; Repaint(); }
        }
    }

    /// <summary>
    /// The Texture tab's brush: Erase / Red / Blue / Yellow, a swatch of the picked one, and the eye
    /// dropper, which takes the colour of the cell clicked.
    /// </summary>
    private void DrawMapsTextureColour()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            int picked = Mathf.RoundToInt(MapsBrushColour);
            int colour = GUILayout.Toolbar(picked, MapsTextureColours);
            if (colour != picked) MapsBrushColour = colour;

            Rect swatch = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f));
            if (Event.current.type == EventType.Repaint)
            {
                if (colour == LandscapeMaps.TextureNone) EditorGUI.DrawRect(swatch, new Color(0.5f, 0.5f, 0.5f, 0.35f));
                else                                     EditorGUI.DrawRect(swatch, MapsTextureCanvas[colour]);
            }

            var dropper = EditorGUIUtility.TrIconContent("EyeDropper.Large",
                "Eye dropper: click (or drag) on the canvas to take the colour from the painting, then carry on drawing.");
            bool picking = GUILayout.Toggle(_mapsPicking, dropper, EditorStyles.miniButton,
                                            GUILayout.Width(24f), GUILayout.Height(18f));
            if (picking != _mapsPicking) { _mapsPicking = picking; Repaint(); }
        }
    }

    /// <summary>Moves every cell of the open tab's map by <paramref name="delta"/>, clamped 0–1.</summary>
    private void ShiftWholeLandscapeMap(float delta)
    {
        var grid = ActiveMapGrid;
        if (grid.IsEmpty || delta == 0f) return;

        Undo.RecordObject(this, delta < 0f ? "Lower All Landscape Map" : "Raise All Landscape Map");
        for (int i = 0; i < grid.cells.Length; i++)
            grid.cells[i] = Mathf.Clamp01(grid.cells[i] + delta);
        PushLandscapeMaps(true);
    }

    private void DrawMapDensity(LandscapeMapGrid grid)
    {
        EditorGUI.BeginChangeCheck();
        int density = EditorGUILayout.DelayedIntField(
            new GUIContent("Grid Density", "Cells across the painted map's width. Changing it stretches what is painted to fit."),
            grid.density);
        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(this, "Landscape Map Density");
        grid.density = Mathf.Clamp(density, 1, LandscapeMaps.MaxCells);
        var area = PinMapGrid(grid);
        grid.Resize(area.width, area.height, grid == _mapsWorking.texture);
        _mapsOverlayDirty = true;
        PushLandscapeMaps(true);
    }

    private void DrawLandscapeMapsPresetRow()
    {
        var active = _data.landscapeMapsPreset;
        var tint   = TunerLoadedPreset.TintRow(active, _mapsLoadedPreset);
        using (new EditorGUILayout.HorizontalScope())
        {
            var picked = LevelSelectRiverPresetLibrary.DrawPicker(
                new GUIContent("Active Preset",
                               "The landscape maps presets kept in " + LevelSelectRiverPresetLibrary.Folder +
                               ". Held by this level; picking one loads it."),
                active);
            var chosen = (LandscapeMapsPreset)EditorGUILayout.ObjectField(
                picked, typeof(LandscapeMapsPreset), false, GUILayout.Width(110f));

            if (chosen != active)
            {
                Undo.RecordObject(_data, "Landscape Maps Preset");
                _data.landscapeMapsPreset = chosen;
                MarkDirty();
                if (chosen != null) LoadLandscapeMaps();
            }
        }
        GUI.backgroundColor = tint;

        active = _data.landscapeMapsPreset;
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(active == null))
            {
                if (GUILayout.Button("Load")) LoadLandscapeMaps();
                using (new EditorGUI.DisabledScope(!TunerLoadedPreset.IsLoaded(active, _mapsLoadedPreset)))
                    if (GUILayout.Button("Save")) SaveLandscapeMaps();
            }
            if (GUILayout.Button("Save as New")) SaveLandscapeMapsAsNew();
        }

        TunerLoadedPreset.DrawNotLoaded(active, _mapsLoadedPreset);

        if (active == null)
            EditorGUILayout.HelpBox("Assign a preset to enable Load and Save, or paint first and Save as New.",
                                    MessageType.Info);
    }

    private void LoadLandscapeMaps()
    {
        var preset = _data.landscapeMapsPreset;
        if (preset == null) return;

        Undo.RecordObject(this, "Load Landscape Maps");
        _mapsWorking.CopyFrom(preset.maps);
        _mapsLoadedPreset = preset;
        _mapsSavedHashFor = null;
        _mapsOverlayDirty = true;
        PushLandscapeMaps(true);
        Repaint();
    }

    private void SaveLandscapeMaps(string reminder = null)
    {
        var preset = _data.landscapeMapsPreset;
        if (!TunerLoadedPreset.IsLoaded(preset, _mapsLoadedPreset)) return;

        if (!EditorUtility.DisplayDialog(
                "Save Landscape Maps",
                reminder +
                $"Overwrite the landscape maps on '{preset.name}' with the current values?\n\n" +
                TunerSaveChanges.Describe(preset, p => p.maps.CopyFrom(_mapsWorking)),
                "Save", "Cancel"))
            return;

        WriteLandscapeMaps(preset);
    }

    // Ten minutes of painting the preset hasn't got, and Save asks for itself. With no preset
    // there is nothing to save into, so it never asks.
    private void LandscapeMapsRemindToSave() => _mapsSaveReminder.Tick(
        () => _data != null && _data.landscapeMapsPreset != null && LandscapeMapsUnsaved,
        SaveLandscapeMaps);

    private void WriteLandscapeMaps(LandscapeMapsPreset preset)
    {
        Undo.RecordObject(preset, "Save Landscape Maps");
        preset.maps.CopyFrom(_mapsWorking);
        EditorUtility.SetDirty(preset);
        AssetDatabase.SaveAssetIfDirty(preset);
        _mapsSavedHashFor = null;

        Debug.Log($"[LevelSelectDesigner] Landscape maps saved to {preset.name}");
    }

    // ── Designer Save ──

    // The preset's maps as they are on disk, hashed once — what the working copy is compared with.
    private LandscapeMapsPreset _mapsSavedHashFor;
    private ulong               _mapsSavedHash;

    /// <summary>
    /// True while the painted maps hold changes no preset has: the working copy differs from the
    /// loaded preset, or there is no preset and something is painted.
    /// </summary>
    private bool LandscapeMapsUnsaved
    {
        get
        {
            if (_data == null || !_mapsTouched) return false;
            var preset = _data.landscapeMapsPreset;
            if (preset == null) return !_mapsWorking.IsEmpty;
            if (!TunerLoadedPreset.IsLoaded(preset, _mapsLoadedPreset)) return false;

            if (_mapsPushedHash == 0) _mapsPushedHash = LandscapeMapsHash(_mapsWorking);   // after a recompile
            if (_mapsSavedHashFor != preset)
            {
                _mapsSavedHash    = LandscapeMapsHash(preset.maps);
                _mapsSavedHashFor = preset;
            }
            return _mapsPushedHash != _mapsSavedHash;
        }
    }

    /// <summary>
    /// The designer's Save: when the landscape maps have changes their preset lacks, asks whether
    /// to save them too. False if the save was cancelled.
    /// </summary>
    private bool SaveLandscapeMapsWithDesigner()
    {
        if (!LandscapeMapsUnsaved) return true;

        var    preset = _data.landscapeMapsPreset;
        string msg    = preset != null
            ? $"The landscape maps (height, noise and texture) have changes not saved to '{preset.name}'.\n\n" +
              TunerSaveChanges.Describe(preset, p => p.maps.CopyFrom(_mapsWorking))
            : "The landscape maps (height, noise and texture) are painted but not saved to a preset.";
        string save = preset != null ? "Save Maps Too" : "Save as New…";

        int choice = EditorUtility.DisplayDialogComplex("Unsaved Landscape Maps", msg + "\n\nSave the maps as well?",
                                                        save, "Cancel", "Skip the Maps");
        if (choice == 1) return false;
        if (choice == 0)
        {
            if (preset != null) WriteLandscapeMaps(preset);
            else                SaveLandscapeMapsAsNew();
        }
        return true;
    }

    private void SaveLandscapeMapsAsNew()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Landscape Maps Preset", "NewLandscapeMapsPreset", "asset",
            "Choose save location", LevelSelectRiverPresetLibrary.EnsureFolder());
        if (string.IsNullOrEmpty(path)) return;

        var preset = CreateInstance<LandscapeMapsPreset>();
        preset.maps.CopyFrom(_mapsWorking);
        AssetDatabase.CreateAsset(preset, path);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(_data, "Landscape Maps Preset");
        _data.landscapeMapsPreset = preset;
        _mapsLoadedPreset         = preset;
        MarkDirty();

        Debug.Log($"[LevelSelectDesigner] Landscape maps saved as new preset at {path}");
    }

    // ── Reaching the scene ──

    /// <summary>
    /// Hands the working copy to the scene's LandscapeTool, which re-bakes the tiles it reaches.
    /// <paramref name="settle"/> also re-stands the spikes and structures on the new ground — left
    /// until a stroke ends while painting.
    /// </summary>
    private void PushLandscapeMaps(bool settle)
    {
        _mapsTouched      = true;
        _mapsOverlayDirty = true;
        _mapsPushedHash   = LandscapeMapsHash(_mapsWorking);

        var tool = FindLandscapeTool();
        if (tool == null) return;

        tool.SetMaps(_mapsWorking, LandscapeTileArea);
        if (tool.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(tool.gameObject.scene);

        if (settle)
        {
            PlaceAllSpikes();
            PlaceAllNomKabarTrees();
            PlaceAllStructures();
        }
        Repaint();
    }

    /// <summary>An undo or redo of the working copy reaches the scene too.</summary>
    private void PushLandscapeMapsAfterUndo()
    {
        _mapsSavedHashFor = null;   // the undo may have been a preset Save
        if (!_mapsTouched || !CanEditLandscapeMaps) return;
        if (LandscapeMapsHash(_mapsWorking) == _mapsPushedHash) return;
        PushLandscapeMaps(true);
    }

    /// <summary>
    /// Keeps what is painted where it is in the world as the tiles' size, count or offset change:
    /// a grid without its own area is pinned to the tile area as it is now, and a grid grows (black
    /// cells at its edges) wherever the tiles reach past it. Tiles that shrink leave the painting
    /// as it was, ready for when they grow back.
    /// </summary>
    private void KeepLandscapeMapAreaInStep()
    {
        if (_mapTool == null) return;
        var tiles = LandscapeTileArea;
        if (tiles.width <= 0f || tiles.height <= 0f) return;

        if (_mapsTouched)
        {
            bool moved = PlaceMapGrid(_mapsWorking.height,  tiles);
            moved     |= PlaceMapGrid(_mapsWorking.noise,   tiles);
            moved     |= PlaceMapGrid(_mapsWorking.texture, tiles);
            if (moved) PushLandscapeMaps(false);   // also lays them over the new tile area
        }
        else if (_mapTool.maps != null)
        {
            // The scene's copy, while the window's working copy has not reached it.
            bool moved = PlaceMapGrid(_mapTool.maps.height, tiles);
            moved     |= PlaceMapGrid(_mapTool.maps.noise,  tiles);
            if (_mapTool.maps.texture != null) moved |= PlaceMapGrid(_mapTool.maps.texture, tiles);
            if (moved) { EditorUtility.SetDirty(_mapTool); _mapTool.MarkDirty(); }
        }

        if (_mapTool.mapArea == tiles) return;
        _mapTool.mapArea = tiles;
        EditorUtility.SetDirty(_mapTool);
        _mapTool.MarkDirty();
    }

    private static bool PlaceMapGrid(LandscapeMapGrid grid, Rect tiles)
    {
        if (grid.IsEmpty) return false;
        bool moved = false;
        if (!grid.HasArea) { grid.area = tiles; moved = true; }
        return grid.GrowToCover(tiles) | moved;
    }

    /// <summary>Gives a grid without its own area the tile area as it is now; returns its area.</summary>
    private Rect PinMapGrid(LandscapeMapGrid grid)
    {
        if (!grid.HasArea) grid.area = LandscapeTileArea;
        return grid.area;
    }

    /// <summary>A grid's world area: its own, or the tile area for one without.</summary>
    private Rect MapGridArea(LandscapeMapGrid grid) => grid.AreaOr(LandscapeTileArea);

    private static ulong LandscapeMapsHash(LandscapeMaps maps)
    {
        ulong h = 14695981039346656037ul;
        void Add(float v) { unchecked { h = (h ^ (uint)System.BitConverter.SingleToInt32Bits(v)) * 1099511628211ul; } }
        void Grid(LandscapeMapGrid g)
        {
            Add(g.cellsX); Add(g.cellsZ);
            Add(g.area.x); Add(g.area.y); Add(g.area.width); Add(g.area.height);
            if (g.cells != null) foreach (float c in g.cells) Add(c);
        }
        Add(maps.low); Add(maps.high); Add(maps.noiseHeight);
        Grid(maps.height);
        Add(-7f);
        Grid(maps.noise);
        Add(-9f);
        if (maps.texture != null) Grid(maps.texture);
        return h;
    }

    // ── Canvas ──

    private void HandleLandscapeMapsMode(Event e)
    {
        if (_data == null) return;

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
        {
            var wp = CanvasToWorldPos(e.mousePosition);
            _mapsBrushXZ    = new Vector2(wp.x, wp.z);
            _mapsBrushShown = _canvasRect.Contains(e.mousePosition);
            Repaint();
        }

        // Eye dropper: press takes the cell's grey into Colour, dragging keeps taking it, letting go
        // goes back to drawing. It reads the map without changing it.
        if (e.type == EventType.MouseDown && e.button == 0 && _mapsPicking && MapsUsesColour)
        {
            _isPickingLandscapeMap = true;
            PickLandscapeMapColour(e.mousePosition);
            e.Use();
            return;
        }
        if (_isPickingLandscapeMap)
        {
            if (e.type == EventType.MouseDrag)
            {
                PickLandscapeMapColour(e.mousePosition);
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                _isPickingLandscapeMap = false;
                _mapsPicking           = false;
                Repaint();
                e.Use();
            }
            return;
        }

        if (!CanEditLandscapeMaps) return;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            Undo.IncrementCurrentGroup();
            _mapsStrokeUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint Landscape Map");

            _isPaintingLandscapeMap = true;
            _mapsStrokeTouched      = null;   // a fresh stroke steps every cell again
            var wp = CanvasToWorldPos(e.mousePosition);
            _mapsLastPaintXZ = new Vector2(wp.x, wp.z);
            PaintLandscapeMap(_mapsLastPaintXZ, _mapsLastPaintXZ);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && _isPaintingLandscapeMap)
        {
            var wp = CanvasToWorldPos(e.mousePosition);
            var xz = new Vector2(wp.x, wp.z);
            PaintLandscapeMap(_mapsLastPaintXZ, xz);
            _mapsLastPaintXZ = xz;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && _isPaintingLandscapeMap)
        {
            _isPaintingLandscapeMap = false;
            Undo.CollapseUndoOperations(_mapsStrokeUndoGroup);
            PushLandscapeMaps(true);   // the spikes and structures settle on the finished stroke
            e.Use();
        }
    }

    /// <summary>
    /// Eye dropper: sets the brush Colour to the open tab's cell under <paramref name="mouse"/>, the
    /// grey as the canvas shows it. Outside the painting (or with nothing painted) Colour is left alone.
    /// </summary>
    private void PickLandscapeMapColour(Vector2 mouse)
    {
        var grid = ActiveMapGrid;
        var area = MapGridArea(grid);
        if (grid.IsEmpty || area.width <= 0f || area.height <= 0f) return;

        var wp = CanvasToWorldPos(mouse);
        int x  = Mathf.FloorToInt((wp.x - area.xMin) / area.width  * grid.cellsX);
        int z  = Mathf.FloorToInt((wp.z - area.yMin) / area.height * grid.cellsZ);
        if (x < 0 || x >= grid.cellsX || z < 0 || z >= grid.cellsZ) return;

        MapsBrushColour = grid.cells[z * grid.cellsX + x];
        Repaint();
    }

    /// <summary>Paints the brush along the line from <paramref name="from"/> to <paramref name="to"/> (world XZ).</summary>
    private void PaintLandscapeMap(Vector2 from, Vector2 to)
    {
        var tiles = LandscapeTileArea;
        if (tiles.width <= 0f || tiles.height <= 0f) return;

        var grid = ActiveMapGrid;
        Undo.RecordObject(this, "Paint Landscape Map");
        if (grid.IsEmpty)   // a first stroke lays a fresh grid over the tiles as they are
        {
            grid.area = tiles;
            grid.Resize(tiles.width, tiles.height);
        }
        var area = PinMapGrid(grid);

        float colour = MapsBrushColour;
        float radius = MapsBrushSize * 0.5f;   // in cells
        int   mode   = MapsBrushMode;
        float blendStrength = MapsBlendStrength;
        float stepAmount    = MapsStepAmount;
        int   drawMode      = MapsDrawMode;
        if (mode >= 2 && (_mapsStrokeTouched == null || _mapsStrokeTouched.Length != grid.cells.Length))
            _mapsStrokeTouched = new bool[grid.cells.Length];

        // Stamped along the stroke at half a brush apart, so a fast drag leaves no gaps.
        float cellW = area.width / grid.cellsX;
        float step  = Mathf.Max(radius * 0.5f, 0.5f) * cellW;
        int   stamps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / step));
        bool  changed = false;
        for (int s = 0; s <= stamps; s++)
        {
            var at = Vector2.Lerp(from, to, s / (float)stamps);
            changed |= mode switch
            {
                1 => BlendLandscapeMap(grid, area, at, radius, blendStrength),
                2 => StepLandscapeMap(grid, area, at, radius, -stepAmount, _mapsStrokeTouched),
                3 => StepLandscapeMap(grid, area, at, radius,  stepAmount, _mapsStrokeTouched),
                _ => StampLandscapeMap(grid, area, at, radius, colour, drawMode),
            };
        }

        if (changed) PushLandscapeMaps(false);
    }

    private static bool StampLandscapeMap(LandscapeMapGrid grid, Rect area, Vector2 xz, float radius, float colour, int drawMode)
    {
        float u = (xz.x - area.xMin) / area.width  * grid.cellsX;
        float v = (xz.y - area.yMin) / area.height * grid.cellsZ;

        bool changed = false;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(u - radius)), x1 = Mathf.Min(grid.cellsX - 1, Mathf.FloorToInt(u + radius));
        int z0 = Mathf.Max(0, Mathf.FloorToInt(v - radius)), z1 = Mathf.Min(grid.cellsZ - 1, Mathf.FloorToInt(v + radius));
        int hx = Mathf.FloorToInt(u), hz = Mathf.FloorToInt(v);   // the cell under the brush is always painted
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            float dx = x + 0.5f - u, dz = z + 0.5f - v;
            if (dx * dx + dz * dz > radius * radius && !(x == hx && z == hz)) continue;

            int k = z * grid.cellsX + x;
            float next = drawMode switch
            {
                1 => Mathf.Max(grid.cells[k], colour),   // Lighter Colour
                2 => Mathf.Min(grid.cells[k], colour),   // Darker Colour
                _ => colour,
            };
            if (grid.cells[k] == next) continue;
            grid.cells[k] = next;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Lower / Higher: moves each cell under the brush by <paramref name="delta"/>, clamped 0–1 —
    /// once per stroke, however often the stroke passes over it.
    /// </summary>
    private static bool StepLandscapeMap(LandscapeMapGrid grid, Rect area, Vector2 xz, float radius,
                                         float delta, bool[] touched)
    {
        float u = (xz.x - area.xMin) / area.width  * grid.cellsX;
        float v = (xz.y - area.yMin) / area.height * grid.cellsZ;

        bool changed = false;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(u - radius)), x1 = Mathf.Min(grid.cellsX - 1, Mathf.FloorToInt(u + radius));
        int z0 = Mathf.Max(0, Mathf.FloorToInt(v - radius)), z1 = Mathf.Min(grid.cellsZ - 1, Mathf.FloorToInt(v + radius));
        int hx = Mathf.FloorToInt(u), hz = Mathf.FloorToInt(v);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            float dx = x + 0.5f - u, dz = z + 0.5f - v;
            if (dx * dx + dz * dz > radius * radius && !(x == hx && z == hz)) continue;

            int k = z * grid.cellsX + x;
            if (touched[k]) continue;
            touched[k] = true;

            float next = Mathf.Clamp01(grid.cells[k] + delta);
            if (grid.cells[k] == next) continue;
            grid.cells[k] = next;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Blend: each cell under the brush becomes the average of the cells round it, a box half the
    /// brush wide (at least one cell each way), so steps between painted greys ease into slopes.
    /// Dragging over the same place again smooths it further. <paramref name="strength"/> is how far
    /// each pass moves a cell toward that average.
    /// </summary>
    private static bool BlendLandscapeMap(LandscapeMapGrid grid, Rect area, Vector2 xz, float radius, float strength)
    {
        float u = (xz.x - area.xMin) / area.width  * grid.cellsX;
        float v = (xz.y - area.yMin) / area.height * grid.cellsZ;
        int   w = grid.cellsX, h = grid.cellsZ;
        int   k = Mathf.Max(1, Mathf.RoundToInt(radius * 0.5f));

        int x0 = Mathf.Max(0, Mathf.FloorToInt(u - radius)), x1 = Mathf.Min(w - 1, Mathf.FloorToInt(u + radius));
        int z0 = Mathf.Max(0, Mathf.FloorToInt(v - radius)), z1 = Mathf.Min(h - 1, Mathf.FloorToInt(v + radius));
        if (x0 > x1 || z0 > z1) return false;
        int hx = Mathf.FloorToInt(u), hz = Mathf.FloorToInt(v);

        // Rows blurred across X first, for every row the Z blur will read.
        int rz0 = Mathf.Max(0, z0 - k), rz1 = Mathf.Min(h - 1, z1 + k);
        int bw  = x1 - x0 + 1;
        int need = bw * (rz1 - rz0 + 1);
        if (_mapsBlendRows.Length < need) _mapsBlendRows = new float[need];
        for (int z = rz0; z <= rz1; z++)
        for (int x = x0; x <= x1; x++)
        {
            int   a = Mathf.Max(0, x - k), b = Mathf.Min(w - 1, x + k);
            float sum = 0f;
            for (int i = a; i <= b; i++) sum += grid.cells[z * w + i];
            _mapsBlendRows[(z - rz0) * bw + (x - x0)] = sum / (b - a + 1);
        }

        // Then down Z, written only inside the brush.
        bool changed = false;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            float dx = x + 0.5f - u, dz = z + 0.5f - v;
            if (dx * dx + dz * dz > radius * radius && !(x == hx && z == hz)) continue;

            int   a = Mathf.Max(rz0, z - k), b = Mathf.Min(rz1, z + k);
            float sum = 0f;
            for (int j = a; j <= b; j++) sum += _mapsBlendRows[(j - rz0) * bw + (x - x0)];
            int   c       = z * w + x;
            float blurred = Mathf.Lerp(grid.cells[c], sum / (b - a + 1), strength);

            if (grid.cells[c] == blurred) continue;
            grid.cells[c] = blurred;
            changed = true;
        }
        return changed;
    }

    /// <summary>The open tab's painted cells, over their own area — Landscape Maps mode only.</summary>
    private void DrawLandscapeMapsOverlay()
    {
        if (_mode != DesignerMode.LandscapeMaps || Event.current.type != EventType.Repaint) return;

        var grid = ActiveMapGrid;
        var area = MapGridArea(grid);
        if (grid.IsEmpty || area.width <= 0f || area.height <= 0f) return;

        if (_mapsOverlayDirty || _mapsOverlayTab != _mapsTab || _mapsOverlay == null ||
            _mapsOverlay.width != grid.cellsX || _mapsOverlay.height != grid.cellsZ)
            BuildLandscapeMapsOverlay(grid);

        // X is negated on the canvas: the world's max X is the left edge, its min Z the top.
        Vector2 topLeft     = WorldToCanvas(new Vector3(area.xMax, 0f, area.yMin));
        Vector2 bottomRight = WorldToCanvas(new Vector3(area.xMin, 0f, area.yMax));
        var     rect        = Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);

        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, MapsOverlayAlpha);
        GUI.DrawTexture(rect, _mapsOverlay, ScaleMode.StretchToFill, true);
        GUI.color = prev;
    }

    private void BuildLandscapeMapsOverlay(LandscapeMapGrid grid)
    {
        int w = grid.cellsX, h = grid.cellsZ;
        if (_mapsOverlay == null || _mapsOverlay.width != w || _mapsOverlay.height != h)
        {
            if (_mapsOverlay != null) DestroyImmediate(_mapsOverlay);
            _mapsOverlay = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags  = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,   // the cells as painted
                wrapMode   = TextureWrapMode.Clamp,
            };
        }
        if (_mapsOverlayPixels == null || _mapsOverlayPixels.Length != w * h) _mapsOverlayPixels = new Color32[w * h];

        // Pixel (i, j): i runs screen left to right (world X high to low), j screen bottom to top
        // (world Z high to low) — GUI.DrawTexture puts row 0 at the bottom.
        bool texture = MapsTextureTab;
        for (int j = 0; j < h; j++)
        for (int i = 0; i < w; i++)
        {
            float cell = grid.cells[(h - 1 - j) * w + (w - 1 - i)];
            if (texture)   // the colours as painted; unpainted cells left clear, so the map shows through
            {
                _mapsOverlayPixels[j * w + i] = MapsTextureCanvas[Mathf.Clamp(Mathf.RoundToInt(cell), 0, 3)];
                continue;
            }
            byte g = (byte)Mathf.RoundToInt(cell * 255f);
            _mapsOverlayPixels[j * w + i] = new Color32(g, g, g, 255);
        }
        _mapsOverlay.SetPixels32(_mapsOverlayPixels);
        _mapsOverlay.Apply(false);

        _mapsOverlayDirty = false;
        _mapsOverlayTab   = _mapsTab;
    }

    /// <summary>The brush's size round the mouse, Landscape Maps mode only.</summary>
    private void DrawLandscapeMapsBrush()
    {
        if (_mode != DesignerMode.LandscapeMaps || Event.current.type != EventType.Repaint) return;
        if (!_mapsBrushShown && !_isPaintingLandscapeMap) return;
        var   grid  = ActiveMapGrid;
        var   area  = grid.IsEmpty ? LandscapeTileArea : MapGridArea(grid);
        int   cells = grid.IsEmpty ? Mathf.Max(1, grid.density) : grid.cellsX;
        if (area.width <= 0f) return;

        Vector2 c    = WorldToCanvas(new Vector3(_mapsBrushXZ.x, 0f, _mapsBrushXZ.y));
        float   r    = MapsBrushSize * 0.5f * area.width / cells * _zoom;
        if (_mapsPicking) r = Mathf.Max(0.5f * area.width / cells * _zoom, 4f);   // the eye dropper takes one cell
        int     segs = HillRingPoints.Length - 1;
        for (int s = 0; s <= segs; s++)
        {
            float t = s * Mathf.PI * 2f / segs;
            HillRingPoints[s] = new Vector3(c.x + Mathf.Cos(t) * r, c.y + Mathf.Sin(t) * r, 0f);
        }
        Handles.color = LevelSelectDesignerSettings.HillSelectedColour;
        DrawSolidLine(LevelSelectDesignerSettings.HillRingWidth, HillRingPoints);
        Handles.color = Color.white;
    }

    private void DestroyLandscapeMapsOverlay()
    {
        if (_mapsOverlay != null) DestroyImmediate(_mapsOverlay);
        _mapsOverlay = null;
    }
}

#endif
