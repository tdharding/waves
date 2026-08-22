using UnityEditor;
using UnityEngine;

// Draws a spike rock side-on in an editor window, from the SAME profile curve
// ProceduralSpikeMesh sweeps — so what you see is the rock you'll get.
//
// Shared by the Grid Designer (small, on the grid, showing where rocks stand) and the Spike
// Studio (large, with the widths called out, showing what a preset IS).
//
// Follows the Map UI spike icon conventions (UIMapController.BuildBigSpikeIcon): one clean
// opaque shape with a black-to-white gradient across it, dissolving away at the bottom. The
// difference is that this outline is the AUTHORED profile rather than a fixed triangle, so a
// waisted or bellied rock reads as one — which is the whole point of drawing it.
public static class SpikeSilhouetteGUI
{
    // How finely the outline is sampled up the rock. The edges are exact polygons rather than
    // stacked rectangles, so this only has to be fine enough to follow the curve.
    const int Steps = 26;

    /// <summary>
    /// Draws the rock with its waterline at `waterlinePx`, rising from there and dropping
    /// `belowDepth` world-units under it. `columns` is the gradient resolution across the shape
    /// (vertical strips); `steps` is how many horizontal bands it is stacked from up the rock —
    /// both drive the polygon count, so lowering them is a performance lever. `steps` defaults to
    /// the curve-following count used by the Spike Studio diagram.
    ///
    /// Built from trapezoid polygons that follow the profile exactly, NOT stacked rectangles:
    /// rectangles gave every sloped side a staircase of hard vertical edges, which read as bands
    /// rather than a rock. Adjacent trapezoids share their edges, so the fill stays seamless even
    /// where the profile is concave.
    ///
    /// The rock stays solid down to the waterline and darkens away below it — which is what the
    /// rock actually does, since you never see under the surface. Opaque throughout: it sinks
    /// into black rather than fading out, so it never lets the grid show through itself.
    /// </summary>
    public static void Draw(SpikeShapeConfig cfg, float scale, Vector2 waterlinePx,
                            float pxPerUnit, float belowDepth, int columns, int steps = Steps)
    {
        var   p   = SpikeProfile.From(cfg, scale);
        float top = p.topY;
        float bot = Mathf.Max(p.bottomY, -Mathf.Max(0f, belowDepth));
        if (top <= bot || pxPerUnit <= 0f) return;

        int cols  = Mathf.Max(1, columns);
        int rows  = Mathf.Max(2, steps);   // horizontal bands stacked up the rock
        var quad  = new Vector3[4];

        Vector3 Edge(float y, float t) =>
            new Vector3(waterlinePx.x + (t * 2f - 1f) * p.RadiusAt(y) * pxPerUnit,
                        waterlinePx.y - y * pxPerUnit);

        // Sampled in two runs that MEET at the waterline rather than one run spanning the lot,
        // so a strip boundary always lands exactly on y = 0 and the shape's width there is
        // exactly the rock's width there.
        int under = bot < 0f ? Mathf.Max(1, Mathf.RoundToInt(rows * (-bot) / (top - bot))) : 0;
        int over  = Mathf.Max(1, rows - under);

        for (int i = 0; i < under + over; i++)
        {
            float y0, y1;
            if (i < under)
            {
                y0 = Mathf.Lerp(bot, 0f, i       / (float)under);
                y1 = Mathf.Lerp(bot, 0f, (i + 1) / (float)under);
            }
            else
            {
                int j = i - under;
                y0 = Mathf.Lerp(0f, top, j       / (float)over);
                y1 = Mathf.Lerp(0f, top, (j + 1) / (float)over);
            }

            float mid = (y0 + y1) * 0.5f;
            if (p.RadiusAt(mid) * pxPerUnit <= 0.01f) continue;

            // Lit on one side, shadowed on the other, as the map icon is. Below the waterline
            // the whole gradient is pulled down toward black so the rock sinks out of sight.
            float sink = mid < 0f && bot < 0f ? Mathf.Clamp01(mid / bot) : 0f;
            Color a = Color.Lerp(Lit,    Color.black, sink);
            Color b = Color.Lerp(Shadow, Color.black, sink);

            for (int cx = 0; cx < cols; cx++)
            {
                float t0 = cx       / (float)cols;
                float t1 = (cx + 1) / (float)cols;

                Handles.color = cols == 1 ? a : Color.Lerp(a, b, (t0 + t1) * 0.5f);

                quad[0] = Edge(y0, t0);
                quad[1] = Edge(y1, t0);
                quad[2] = Edge(y1, t1);
                quad[3] = Edge(y0, t1);
                Handles.DrawAAConvexPolygon(quad);
            }
        }

        // The waterline itself, across the rock's full width there — the one height every other
        // measurement on the spike is quoted from.
        float wr = p.RadiusAt(0f) * pxPerUnit;
        Handles.color = Waterline;
        Handles.DrawAAPolyLine(2f,
            new Vector3(waterlinePx.x - wr * 1.35f, waterlinePx.y),
            new Vector3(waterlinePx.x + wr * 1.35f, waterlinePx.y));
    }

    static readonly Color Lit       = new Color(0.96f, 0.96f, 0.96f);
    static readonly Color Shadow    = new Color(0.06f, 0.06f, 0.06f);
    static readonly Color Waterline = new Color(0.45f, 0.85f, 1f);

    /// <summary>
    /// The whole rock in a box — tip to footing, waterline marked, and the three above-water
    /// widths called out where they sit. Fitted to the box rather than drawn at world scale, so
    /// a needle and a boulder are both readable while you shape them.
    /// </summary>
    public static void DrawDiagram(Rect box, SpikeShapeConfig cfg, float scale)
    {
        EditorGUI.DrawRect(box, new Color(0.13f, 0.13f, 0.16f, 1f));

        var   p      = SpikeProfile.From(cfg, scale);
        float widest = Mathf.Max(p.RadiusAt(0f), Mathf.Max(p.RadiusAt(p.midY), p.RadiusAt(p.topY)));

        // The rock that stands out of the water gets most of the box; the rest is the stub
        // sinking away below. Scaling to the full depth would leave the shape being tuned as a
        // sliver at the top — the depth is a number, not something judged by eye.
        const float AboveShare = 0.72f;
        float padY      = 10f;
        float labelRoom = 104f;
        float fitV      = (box.height * AboveShare - padY) / Mathf.Max(0.01f, p.topY);
        float fitH      = widest > 1e-4f ? ((box.width - labelRoom) * 0.5f - 14f) / widest : fitV;
        float pxPerUnit = Mathf.Max(0.01f, Mathf.Min(fitV, fitH));

        float centreX    = box.x + (box.width - labelRoom) * 0.5f;
        float waterlineY = box.y + padY + p.topY * pxPerUnit;
        float stubDepth  = (box.yMax - padY - waterlineY) / pxPerUnit;

        Handles.BeginGUI();
        Draw(cfg, scale, new Vector2(centreX, waterlineY), pxPerUnit, stubDepth, columns: 18);

        var tick = new GUIStyle(EditorStyles.miniLabel)
        { normal = { textColor = new Color(0.72f, 0.72f, 0.78f) } };

        // Widths read straight off the profile, so they already include the size multiplier —
        // these are the metres this rock actually occupies, not the preset's own numbers.
        void Callout(float y, string name)
        {
            float py = waterlineY - y * pxPerUnit;
            float rx = centreX + p.RadiusAt(y) * pxPerUnit;
            Handles.color = new Color(0.6f, 0.6f, 0.7f, 0.45f);
            Handles.DrawAAPolyLine(1f, new Vector3(rx + 2f, py), new Vector3(box.xMax - labelRoom + 4f, py));
            GUI.Label(new Rect(box.xMax - labelRoom + 8f, py - 8f, labelRoom - 10f, 16f),
                      $"{name} ⌀{p.RadiusAt(y) * 2f:0.##}", tick);
        }

        Callout(p.topY, "top");
        Callout(p.midY, "mid");
        Callout(0f,     "water");
        Handles.EndGUI();

        GUI.Label(new Rect(box.x + 6f, box.y + 3f, box.width - labelRoom - 8f, 16f),
                  $"{p.topY:0.##} m above water · base ⌀{p.radiusBelowSurface * 2f:0.##} m, {-p.bottomY:0.##} m down",
                  tick);
    }
}
