using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// The landscape height map on the canvas: the real ground, sampled from
/// <see cref="LandscapeHillHeight.Surface"/> (the same maths the tiles are baked from) onto a
/// texture over the tile area. Shading is a simple height tone from the Height Gradient, not the scene's lighting: its middle is
/// the top of the base hill (the tile base with none), its right end the highest ground on the map, its left end the lowest (shading off = all flat grey). Runs
/// and pools are cut out of it as they are in the scene.
///
/// It is only redrawn when something it is made from changes (a hill, the Height Multiplier, the
/// noise scale, the river edges, the tile area or a setting). While a hill is being dragged it can
/// drop to a low resolution, and goes back to full when the drag ends.
///
/// Its settings, and the hill point markers', are in <see cref="LevelSelectDesignerSettings"/>.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const int   LandscapeMapDragResolution = 64;
    private const int   LandscapeMapBlock          = 16;     // pixels per side sharing one list of hills and river lines
    private const float LandscapeMapFlatGrey       = 0.38f;  // the tile fill grey: flat ground

    private Texture2D     _mapTexture;
    private Color32[]     _mapPixels;
    private readonly Color32[] _mapGradientTable = new Color32[256];   // the Height Gradient, read once per build
    private float[]       _mapSmooth;
    private float[]       _mapHeight;
    private float[]       _mapContour;      // smooth height + rocky noise, for the contour lines
    private ulong         _mapBuiltFrom;
    private bool          _mapBuiltLow;
    private float         _mapBuildMs;
    private Rect          _mapWorldRect;    // x/y = min world X/Z, width/height = extent
    private LandscapeTool _mapTool;

    private readonly List<int> _mapNearHills = new(), _mapNearLifts = new(), _mapNearCuts = new();

    // The grid the map was last drawn from, which the contour lines are traced over.
    private int   _mapBuildCount, _mapGridW, _mapGridH;
    private float _mapStepX, _mapStepZ, _mapLowest, _mapHighest, _mapMiddle;
    private float _mapUp = 1f, _mapDown = 1f;   // how far the gradient's ends sit above and below the middle

    /// <summary>The map's size and how long it last took to draw, for the settings window.</summary>
    public string LandscapeMapStatus => _mapTexture != null
        ? $"{_mapTexture.width}×{_mapTexture.height}, last drawn in {_mapBuildMs:0.#} ms" : null;

    private void DestroyLandscapeMap()
    {
        if (_mapTexture != null) DestroyImmediate(_mapTexture);
        _mapTexture   = null;
        _mapBuiltFrom = 0;
    }

    // ── Canvas ──

    /// <summary>Draws the height map under the tile outlines. False when it is off or there is no tile area.</summary>
    private bool DrawLandscapeMapOnCanvas()
    {
        if (!LevelSelectDesignerSettings.MapOn) return false;
        if (_data.landscapeTilesX <= 0 || _data.landscapeTilesZ <= 0 || _data.landscapeTileSize <= 0) return false;

        bool interacting = _isDraggingHillPoint || GUIUtility.hotControl != 0;
        UpdateLandscapeMap(interacting);
        if (_mapTexture == null) return false;

        // X is negated on the canvas: the world's max X is the left edge, its min Z the top.
        Vector2 topLeft     = WorldToCanvas(new Vector3(_mapWorldRect.xMax, 0f, _mapWorldRect.yMin));
        Vector2 bottomRight = WorldToCanvas(new Vector3(_mapWorldRect.xMin, 0f, _mapWorldRect.yMax));
        var     rect        = Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);

        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, _landscapeCanvasAlpha);
        GUI.DrawTexture(rect, _mapTexture, ScaleMode.StretchToFill, true);
        GUI.color = prev;
        return true;
    }

    private void UpdateLandscapeMap(bool interacting)
    {
        if (_mapTool == null)
        {
            var container = FindHillContainer();
            _mapTool = container != null ? container.parent.GetComponent<LandscapeTool>() : null;
        }

        float size = _data.landscapeTileSize;
        _mapWorldRect = new Rect(_data.landscapeOffset.x, _data.landscapeOffset.y,
                                 _data.landscapeTilesX * size, _data.landscapeTilesZ * size);

        ulong from    = LandscapeMapFingerprint();
        bool  changed = from != _mapBuiltFrom || _mapTexture == null;

        if (_mapTexture != null && !_mapBuiltLow &&
            Mathf.Max(_mapTexture.width, _mapTexture.height) != LevelSelectDesignerSettings.MapRes)
            Debug.Log($"[MapResDebug] Repaint: setting={LevelSelectDesignerSettings.MapRes}, texture={_mapTexture.width}x{_mapTexture.height}, " +
                      $"changed={changed}, interacting={interacting}, hotControl={GUIUtility.hotControl}");

        if (changed)
            BuildLandscapeMap(from, interacting && LevelSelectDesignerSettings.MapDragLow);
        else if (_mapBuiltLow && !interacting)
            BuildLandscapeMap(from, false);   // the drag ended: back to full

        UpdateLandscapeContours();
    }

    // Everything the map is drawn from, boiled down to one number.
    private ulong LandscapeMapFingerprint()
    {
        ulong h = 14695981039346656037ul;
        void Add(float v) { unchecked { h = (h ^ (uint)System.BitConverter.SingleToInt32Bits(v)) * 1099511628211ul; } }

        Add(LevelSelectDesignerSettings.MapRes);
        Add(LevelSelectDesignerSettings.MapShade ? 1 : 0);
        Add(LevelSelectDesignerSettings.MapNoise ? 1 : 0);
        Add(LevelSelectDesignerSettings.ContourOn ? 1 : 0);   // contours need the noise worked out
        Add(LevelSelectDesignerSettings.MapCuts  ? 1 : 0);
        unchecked { h = (h ^ (uint)LevelSelectDesignerSettings.MapGradientText.GetHashCode()) * 1099511628211ul; }
        Add(_mapWorldRect.x); Add(_mapWorldRect.y); Add(_mapWorldRect.width); Add(_mapWorldRect.height);
        Add(SpikeBaselineY); Add(_data.landscapeNoiseScale);
        Add(_mapTool != null ? _mapTool.heightMultiplier : 1f);
        foreach (var hp in _data.hillPoints)
        {
            if (hp == null) { Add(-9f); continue; }
            Add(hp.positionXZ.x); Add(hp.positionXZ.y); Add(hp.scale);
            Add(hp.height); Add(hp.smoothness); Add(hp.noise);
        }
        Add(-1f);

        if (_mapTool != null)
        {
            foreach (var p in _mapTool.riverEdgePoints) { Add(p.x); Add(p.y); Add(p.z); Add(p.w); }
            Add(-2f);
            foreach (var r in _mapTool.riverEdgeReach)    Add(r);
            Add(-3f);
            foreach (var s in _mapTool.riverEdgeStrength) Add(s);
        }
        return h;
    }

    private void BuildLandscapeMap(ulong from, bool low)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        int   fullRes = LevelSelectDesignerSettings.MapRes;
        bool  shade    = LevelSelectDesignerSettings.MapShade;
        bool  noise   = LevelSelectDesignerSettings.MapNoise;
        bool  contours = LevelSelectDesignerSettings.ContourOn;
        bool  cuts     = LevelSelectDesignerSettings.MapCuts;

        int   res    = low ? Mathf.Min(LandscapeMapDragResolution, fullRes) : fullRes;
        float extent = Mathf.Max(_mapWorldRect.width, _mapWorldRect.height);
        int   w      = Mathf.Max(2, Mathf.RoundToInt(res * _mapWorldRect.width  / extent));
        int   hgt    = Mathf.Max(2, Mathf.RoundToInt(res * _mapWorldRect.height / extent));
        float stepX  = _mapWorldRect.width  / w;
        float stepZ  = _mapWorldRect.height / hgt;

        if (_mapTexture == null || _mapTexture.width != w || _mapTexture.height != hgt)
        {
            if (_mapTexture != null) DestroyImmediate(_mapTexture);
            _mapTexture = new Texture2D(w, hgt, TextureFormat.RGBA32, false)
            {
                hideFlags  = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
            };
        }
        if (_mapPixels == null || _mapPixels.Length != w * hgt) _mapPixels = new Color32[w * hgt];
        if (_mapSmooth == null || _mapSmooth.Length != w * hgt) _mapSmooth = new float[w * hgt];
        if (_mapHeight == null || _mapHeight.Length != w * hgt) _mapHeight = new float[w * hgt];
        if (contours && (_mapContour == null || _mapContour.Length != w * hgt)) _mapContour = new float[w * hgt];

        float globalHeight = _mapTool != null ? _mapTool.heightMultiplier : 1f;
        var surface = LandscapeHillHeight.BuildSurface(
            _data.hillPoints, globalHeight, _data.landscapeNoiseScale,
            _mapTool != null ? _mapTool.riverEdgePoints   : null,
            _mapTool != null ? _mapTool.riverEdgeReach    : null,
            _mapTool != null ? _mapTool.riverEdgeStrength : null);
        float baseY = SpikeBaselineY;

        // Pixel (i, j): i runs screen left to right (world X high to low), j screen bottom to top
        // (world Z high to low) — GUI.DrawTexture puts row 0 at the bottom.
        Vector2 Xz(int i, int j) => new Vector2(_mapWorldRect.xMax - (i + 0.5f) * stepX,
                                                _mapWorldRect.yMax - (j + 0.5f) * stepZ);

        // The gradient's middle: the top of the base hill, the field the rest of the ground is
        // raised or sunk from. With no base hill it is the tile base.
        float middle = LandscapeMapMiddle(globalHeight);

        // Pass 1: the heights, block by block so each pixel only looks at the hills and river
        // lines that can reach its block. NaN marks ground cut away.
        float lowest = middle, highest = middle, contourLow = middle, contourHigh = middle;
        for (int bj = 0; bj < hgt; bj += LandscapeMapBlock)
        for (int bi = 0; bi < w;   bi += LandscapeMapBlock)
        {
            int ei = Mathf.Min(bi + LandscapeMapBlock, w);
            int ej = Mathf.Min(bj + LandscapeMapBlock, hgt);
            Vector2 a = Xz(bi, bj), b = Xz(ei - 1, ej - 1);
            FindLandscapeMapNear(surface, Mathf.Min(a.x, b.x), Mathf.Max(a.x, b.x),
                                          Mathf.Min(a.y, b.y), Mathf.Max(a.y, b.y));

            for (int j = bj; j < ej; j++)
            for (int i = bi; i < ei; i++)
            {
                int     k  = j * w + i;
                Vector2 xz = Xz(i, j);

                if (cuts && surface.Cut(xz) < 0f)
                {
                    _mapSmooth[k] = _mapHeight[k] = float.NaN;
                    if (contours) _mapContour[k] = float.NaN;
                    continue;
                }

                float smooth = surface.SmoothHeight(xz, baseY);
                float rock   = noise || contours ? surface.Noise(xz).x * (1f - surface.Lift(xz, out _)) : 0f;
                float height = noise ? smooth + rock : smooth;

                _mapSmooth[k] = smooth;
                _mapHeight[k] = height;
                if (height < lowest)  lowest  = height;
                if (height > highest) highest = height;
                if (contours)
                {
                    float ch = smooth + rock;
                    _mapContour[k] = ch;
                    if (ch < contourLow)  contourLow  = ch;
                    if (ch > contourHigh) contourHigh = ch;
                }
            }
        }

        // Pass 2: with shading on, the colour comes from the Height Gradient — left the lowest
        // ground, middle the base hill's top, right the highest — scaled to what is actually on
        // the map (hills add together, so no single hill's height says how high the ground gets).
        // No light: a simple tone, not the scene's lighting. The gradient is read once into a table.
        float up = Mathf.Max(highest - middle, 1e-3f), down = Mathf.Max(middle - lowest, 1e-3f);
        if (shade)
        {
            var grad = LevelSelectDesignerSettings.MapGradient;
            for (int n = 0; n < _mapGradientTable.Length; n++)
                _mapGradientTable[n] = grad.Evaluate(n / (_mapGradientTable.Length - 1f));
        }
        byte flatG = (byte)(LandscapeMapFlatGrey * 255f);
        for (int j = 0; j < hgt; j++)
        for (int i = 0; i < w;   i++)
        {
            int   k = j * w + i;
            float c = _mapSmooth[k];
            if (float.IsNaN(c))
            {
                _mapPixels[k] = new Color32(0, 0, 0, 0);
                continue;
            }

            if (!shade)
            {
                _mapPixels[k] = new Color32(flatG, flatG, flatG, 255);
                continue;
            }

            float t = LandscapeMapGradientT(_mapHeight[k] - middle, up, down);
            _mapPixels[k] = _mapGradientTable[Mathf.RoundToInt(t * (_mapGradientTable.Length - 1))];
        }

        _mapTexture.SetPixels32(_mapPixels);
        _mapTexture.Apply(false);

        _mapBuiltFrom = from;
        _mapBuiltLow  = low;
        _mapBuildCount++;
        _mapGridW     = w;
        _mapGridH     = hgt;
        _mapStepX     = stepX;
        _mapStepZ     = stepZ;
        _mapLowest    = contourLow;   // the contours follow the height with rocky noise
        _mapHighest   = contourHigh;
        _mapMiddle    = middle;       // and count up and down from the base hill's top
        _mapUp        = up;           // and are coloured from the gradient as the map is
        _mapDown      = down;
        _mapBuildMs   = (float)watch.Elapsed.TotalMilliseconds;
        Debug.Log($"[MapResDebug] Built {w}x{hgt} (setting {fullRes}, low={low}), hotControl={GUIUtility.hotControl}, dragging hill={_isDraggingHillPoint}");
    }

    /// <summary>Where a height (above the middle) falls on the Height Gradient, 0–1.</summary>
    private static float LandscapeMapGradientT(float aboveMiddle, float up, float down)
        => Mathf.Clamp01(aboveMiddle >= 0f ? 0.5f + 0.5f * aboveMiddle / up
                                           : 0.5f - 0.5f * -aboveMiddle / down);

    /// <summary>
    /// The top of the base hill above the tile base — the biggest hill point the Landscape panel
    /// calls "Base" (Radius over 50). 0 (the tile base) when there is none.
    /// </summary>
    private float LandscapeMapMiddle(float globalHeight)
    {
        LevelSelectDesignerData.LandscapeHillPoint baseHill = null;
        foreach (var hp in _data.hillPoints)
            if (hp != null && hp.scale > 50f && (baseHill == null || hp.scale > baseHill.scale))
                baseHill = hp;
        return baseHill != null ? baseHill.height * globalHeight : 0f;
    }

    // The hills and river lines that can reach this block of pixels.
    private void FindLandscapeMapNear(LandscapeHillHeight.Surface s, float minX, float maxX, float minZ, float maxZ)
    {
        _mapNearHills.Clear();
        _mapNearLifts.Clear();
        _mapNearCuts.Clear();

        for (int i = 0; i < s.hillCount; i++)
        {
            Vector4 hill   = s.hills[i];
            float   radius = hill.w;
            if (radius <= 0f) continue;

            float nx = Mathf.Clamp(hill.x, minX, maxX);
            float nz = Mathf.Clamp(hill.z, minZ, maxZ);
            float dx = hill.x - nx, dz = hill.z - nz;
            if (dx * dx + dz * dz < radius * radius) _mapNearHills.Add(i);
        }

        for (int i = 0; i < s.edgeCount; i++)
        {
            Vector4 p    = s.edgePoints[i];
            Vector4 q    = s.EdgeEnd(i);
            float   half = Mathf.Max(Mathf.Abs(p.w), Mathf.Abs(q.w));

            if (MapOverlaps(p, q, half + 0.1f, minX, maxX, minZ, maxZ)) _mapNearCuts.Add(i);

            float reach = s.edgeReach[i];
            if (reach >= 0f && MapOverlaps(p, q, half + reach + 0.1f, minX, maxX, minZ, maxZ)) _mapNearLifts.Add(i);
        }

        s.nearHills = _mapNearHills;
        s.nearLifts = _mapNearLifts;
        s.nearCuts  = _mapNearCuts;
    }

    private static bool MapOverlaps(Vector4 p, Vector4 q, float grow, float minX, float maxX, float minZ, float maxZ)
        => Mathf.Max(p.x, q.x) + grow >= minX && Mathf.Min(p.x, q.x) - grow <= maxX &&
           Mathf.Max(p.z, q.z) + grow >= minZ && Mathf.Min(p.z, q.z) - grow <= maxZ;

    // ── Hill point markers (Landscape mode) ──

    // Enough points that a big base ring still reads as round when zoomed in.
    private static readonly Vector3[] HillRingPoints = new Vector3[129];

    /// <summary>
    /// One solid ring round the hill's centre at the hill/hole/base's own Radius, and a dot on the
    /// centre — width and colours from the Level Select Designer Settings window.
    /// </summary>
    private void DrawHillPointMarker(LevelSelectDesignerData.LandscapeHillPoint hp, bool sel)
    {
        var   cp = WorldToCanvas(new Vector3(hp.positionXZ.x, 0, hp.positionXZ.y));
        float r  = hp.scale * _zoom;

        // A thick polyline, not DrawWireDisc: its thickness is what the walls use on this canvas.
        int segs = HillRingPoints.Length - 1;
        for (int s = 0; s <= segs; s++)
        {
            float t = s * Mathf.PI * 2f / segs;
            HillRingPoints[s] = new Vector3(cp.x + Mathf.Cos(t) * r, cp.y + Mathf.Sin(t) * r, 0f);
        }
        Handles.color = sel ? LevelSelectDesignerSettings.HillSelectedColour : LevelSelectDesignerSettings.HillRingColour;
        DrawSolidLine(LevelSelectDesignerSettings.HillRingWidth, HillRingPoints);

        float dot = sel ? LevelSelectDesignerSettings.HillSelDotSize : LevelSelectDesignerSettings.HillDotSize;
        if (dot > 0f)
        {
            Handles.color = sel ? LevelSelectDesignerSettings.HillSelectedColour : LevelSelectDesignerSettings.HillDotColour;
            Handles.DrawSolidDisc(new Vector3(cp.x, cp.y, 0), Vector3.forward, dot);
        }
        Handles.color = Color.white;
    }
}

#endif
