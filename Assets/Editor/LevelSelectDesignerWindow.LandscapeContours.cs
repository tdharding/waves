using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Contour lines over the canvas height map: a line every Interval of height above and below the
/// top of the base hill (the tile base with none — the map's middle), traced over the grid the map was drawn from, with the rocky noise always in
/// (whatever the map's own Rocky Noise toggle says; river cuts break them). Every Nth line is a major line.
///
/// They are traced again only when the map is redrawn or the Interval changes; panning and
/// zooming just move the traced lines. Settings are in <see cref="LevelSelectDesignerSettings"/>.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const int LandscapeContourMaxLevels = 400;

    private struct ContourLine
    {
        public int       level;    // height = base hill top + level × Interval; never 0 (the field itself)
        public Vector2[] world;    // world X/Z
        public Vector3[] canvas;   // filled from world every repaint
    }

    private struct ContourSegment
    {
        public int     edgeA, edgeB;   // the grid edges each end sits on, for joining segments up
        public Vector2 a, b;           // world X/Z
    }

    private readonly List<ContourLine> _contourLines = new();
    private int    _contourBuiltFromMap = -1;
    private float  _contourBuiltInterval;
    private float  _contourBuildMs;
    private bool   _contourTooMany;

    private List<ContourSegment>[] _contourBuckets;
    private readonly Dictionary<int, int> _contourEndFirst  = new();
    private readonly Dictionary<int, int> _contourEndSecond = new();
    private readonly List<Vector2> _contourForward = new(), _contourBackward = new();
    private bool[] _contourUsed;

    /// <summary>How many lines there are and how long they took to trace, for the settings window.</summary>
    public string LandscapeContourStatus
    {
        get
        {
            if (!LevelSelectDesignerSettings.MapOn) return "The Canvas Height Map is off — contours need it shown.";
            if (_contourTooMany) return $"More than {LandscapeContourMaxLevels} heights to draw — raise the Interval.";
            if (_contourBuiltFromMap < 0) return null;
            return $"{_contourLines.Count} lines, last traced in {_contourBuildMs:0.#} ms";
        }
    }

    private void UpdateLandscapeContours()
    {
        if (!LevelSelectDesignerSettings.ContourOn || _mapContour == null || _mapGridW < 2 || _mapGridH < 2)
        {
            _contourLines.Clear();
            _contourBuiltFromMap = -1;
            return;
        }

        float interval = Mathf.Max(0.001f, LevelSelectDesignerSettings.ContourInterval);
        if (_contourBuiltFromMap == _mapBuildCount && Mathf.Approximately(_contourBuiltInterval, interval)) return;

        BuildLandscapeContours(interval);
        _contourBuiltFromMap  = _mapBuildCount;
        _contourBuiltInterval = interval;
    }

    // Marching squares over the map's heights (rocky noise in), one pass over the cells for all levels,
    // then each level's segments joined end to end into lines.
    private void BuildLandscapeContours(float interval)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _contourLines.Clear();
        _contourTooMany = false;

        // Levels are counted from the map's middle (the base hill's top), so line 0 is that field.
        float middle = _mapMiddle;
        int kMin = Mathf.CeilToInt ((_mapLowest  - middle) / interval);
        int kMax = Mathf.FloorToInt((_mapHighest - middle) / interval);
        int levels = kMax - kMin + 1;
        if (levels <= 0) { _contourBuildMs = 0f; return; }
        if (levels > LandscapeContourMaxLevels) { _contourTooMany = true; _contourBuildMs = 0f; return; }

        if (_contourBuckets == null || _contourBuckets.Length < levels)
        {
            var grown = new List<ContourSegment>[levels];
            for (int n = 0; n < levels; n++)
                grown[n] = _contourBuckets != null && n < _contourBuckets.Length ? _contourBuckets[n] : new List<ContourSegment>();
            _contourBuckets = grown;
        }
        for (int n = 0; n < levels; n++) _contourBuckets[n].Clear();

        int     w = _mapGridW, h = _mapGridH;
        float[] v = _mapContour;

        // Grid point (gi, gj) → world, as the map's pixel centres.
        Vector2 World(float gi, float gj) => new Vector2(_mapWorldRect.xMax - (gi + 0.5f) * _mapStepX,
                                                         _mapWorldRect.yMax - (gj + 0.5f) * _mapStepZ);
        // Edge ids: the edge from (i, j) to (i+1, j) and the one from (i, j) to (i, j+1).
        int Across(int i, int j) => (j * w + i) * 2;
        int Up    (int i, int j) => (j * w + i) * 2 + 1;

        for (int j = 0; j < h - 1; j++)
        for (int i = 0; i < w - 1; i++)
        {
            // Corners: a (i, j), b (i+1, j), c (i+1, j+1), d (i, j+1).
            float va = v[j * w + i],       vb = v[j * w + i + 1];
            float vc = v[(j + 1) * w + i + 1], vd = v[(j + 1) * w + i];
            if (float.IsNaN(va) || float.IsNaN(vb) || float.IsNaN(vc) || float.IsNaN(vd)) continue;   // river cut

            float lo = Mathf.Min(Mathf.Min(va, vb), Mathf.Min(vc, vd));
            float hi = Mathf.Max(Mathf.Max(va, vb), Mathf.Max(vc, vd));
            int   k0 = Mathf.Max(kMin, Mathf.CeilToInt ((lo - middle) / interval));
            int   k1 = Mathf.Min(kMax, Mathf.FloorToInt((hi - middle) / interval));

            for (int k = k0; k <= k1; k++)
            {
                if (k == 0) continue;   // the field itself
                float L = middle + k * interval;

                int index = (va >= L ? 1 : 0) | (vb >= L ? 2 : 0) | (vc >= L ? 4 : 0) | (vd >= L ? 8 : 0);
                if (index == 0 || index == 15) continue;

                // The four edges: 0 bottom a–b, 1 right b–c, 2 top d–c, 3 left a–d.
                Vector2 P(int e)
                {
                    switch (e)
                    {
                        case 0:  return World(i + (L - va) / (vb - va), j);
                        case 1:  return World(i + 1, j + (L - vb) / (vc - vb));
                        case 2:  return World(i + (L - vd) / (vc - vd), j + 1);
                        default: return World(i, j + (L - va) / (vd - va));
                    }
                }
                int Id(int e) => e == 0 ? Across(i, j) : e == 1 ? Up(i + 1, j) : e == 2 ? Across(i, j + 1) : Up(i, j);

                var bucket = _contourBuckets[k - kMin];
                void Add(int e1, int e2) => bucket.Add(new ContourSegment { edgeA = Id(e1), edgeB = Id(e2), a = P(e1), b = P(e2) });

                bool centreIn = (va + vb + vc + vd) * 0.25f >= L;
                switch (index)
                {
                    case 1:  case 14: Add(3, 0); break;
                    case 2:  case 13: Add(0, 1); break;
                    case 3:  case 12: Add(3, 1); break;
                    case 4:  case 11: Add(1, 2); break;
                    case 6:  case 9:  Add(0, 2); break;
                    case 7:  case 8:  Add(3, 2); break;
                    case 5:   // a and c above the line
                        if (centreIn) { Add(0, 1); Add(2, 3); } else { Add(3, 0); Add(1, 2); }
                        break;
                    case 10:  // b and d above the line
                        if (centreIn) { Add(3, 0); Add(1, 2); } else { Add(0, 1); Add(2, 3); }
                        break;
                }
            }
        }

        for (int n = 0; n < levels; n++)
            if (_contourBuckets[n].Count > 0) JoinContourSegments(_contourBuckets[n], kMin + n);

        _contourBuildMs = (float)watch.Elapsed.TotalMilliseconds;
    }

    // One level's segments joined end to end: every inner end is shared by exactly two segments.
    private void JoinContourSegments(List<ContourSegment> segs, int level)
    {
        _contourEndFirst.Clear();
        _contourEndSecond.Clear();
        for (int s = 0; s < segs.Count; s++)
        {
            AddEnd(segs[s].edgeA, s);
            AddEnd(segs[s].edgeB, s);
        }

        if (_contourUsed == null || _contourUsed.Length < segs.Count) _contourUsed = new bool[segs.Count * 2];
        System.Array.Clear(_contourUsed, 0, segs.Count);

        for (int s = 0; s < segs.Count; s++)
        {
            if (_contourUsed[s]) continue;
            _contourUsed[s] = true;

            _contourForward.Clear();
            _contourBackward.Clear();
            _contourForward.Add(segs[s].a);
            _contourForward.Add(segs[s].b);
            Walk(segs[s].edgeB, s, _contourForward);
            Walk(segs[s].edgeA, s, _contourBackward);

            var pts = new Vector2[_contourBackward.Count + _contourForward.Count];
            int p = 0;
            for (int b = _contourBackward.Count - 1; b >= 0; b--) pts[p++] = _contourBackward[b];
            for (int f = 0; f < _contourForward.Count; f++)       pts[p++] = _contourForward[f];

            _contourLines.Add(new ContourLine { level = level, world = pts, canvas = new Vector3[pts.Length] });
        }

        void AddEnd(int edge, int s)
        {
            if (_contourEndFirst.ContainsKey(edge)) _contourEndSecond[edge] = s;
            else                                     _contourEndFirst[edge]  = s;
        }

        // From the end on `edge` of segment `from`, keep stepping onto the next segment sharing it.
        void Walk(int edge, int from, List<Vector2> into)
        {
            while (true)
            {
                int next = -1;
                if (_contourEndFirst.TryGetValue(edge, out int f) && f != from && !_contourUsed[f]) next = f;
                else if (_contourEndSecond.TryGetValue(edge, out int g) && g != from && !_contourUsed[g]) next = g;
                if (next < 0) return;

                _contourUsed[next] = true;
                var seg = segs[next];
                if (seg.edgeA == edge) { into.Add(seg.b); edge = seg.edgeB; }
                else                   { into.Add(seg.a); edge = seg.edgeA; }
                from = next;
            }
        }
    }

    /// <summary>Draws the traced contour lines; called after the tile outlines so they sit over them.</summary>
    private void DrawLandscapeContoursOnCanvas()
    {
        if (!LevelSelectDesignerSettings.ContourOn || _contourLines.Count == 0) return;

        int   every      = Mathf.Max(1, LevelSelectDesignerSettings.ContourMajorEvery);
        float width      = LevelSelectDesignerSettings.ContourWidth;
        float majorWidth = LevelSelectDesignerSettings.ContourMajorWidth;
        Color colour      = LevelSelectDesignerSettings.ContourColour;
        Color majorColour = LevelSelectDesignerSettings.ContourMajorColour;
        colour.a      *= _landscapeCanvasAlpha;
        majorColour.a *= _landscapeCanvasAlpha;

        // Colour by Height: each line wears the Height Gradient's colour at its own height, as the
        // map does; Line/Major Colour then only give its transparency.
        bool     byHeight = LevelSelectDesignerSettings.ContourByHeight;
        Gradient gradient = byHeight ? LevelSelectDesignerSettings.MapGradient : null;

        // World → canvas is a scale and an offset (X negated), so work it out once.
        Vector2 o  = WorldToCanvas(Vector3.zero);
        Vector2 ux = WorldToCanvas(Vector3.right)   - o;
        Vector2 uz = WorldToCanvas(Vector3.forward) - o;
        Rect    view = _canvasRect;

        var prev = Handles.color;
        foreach (var line in _contourLines)
        {
            bool major = line.level % every == 0;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int p = 0; p < line.world.Length; p++)
            {
                Vector2 wp = line.world[p];
                float   x  = o.x + ux.x * wp.x + uz.x * wp.y;
                float   y  = o.y + ux.y * wp.x + uz.y * wp.y;
                line.canvas[p] = new Vector3(x, y, 0f);
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            if (maxX < view.xMin || minX > view.xMax || maxY < view.yMin || minY > view.yMax) continue;

            Color c = major ? majorColour : colour;
            if (byHeight)
            {
                Color g = gradient.Evaluate(LandscapeMapGradientT(line.level * _contourBuiltInterval, _mapUp, _mapDown));
                c = new Color(g.r, g.g, g.b, c.a);
            }
            Handles.color = c;
            DrawSolidLine(major ? majorWidth : width, line.canvas);
        }
        Handles.color = prev;
    }
}

#endif
