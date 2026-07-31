using UnityEngine;
using UnityEditor;

// Shared rasteriser for procedural map-icon previews. Reads the same MapIconLibrary params the
// runtime mesh builders use, so previews track the authored shapes. Used by the descriptor
// inspector and the Map Icon Library window.
public static class MapIconPreview
{
    // The project's MapIconLibrary asset, or null if none exists.
    public static MapIconLibrary ResolveLibraryAsset()
    {
        string[] guids = AssetDatabase.FindAssets("t:MapIconLibrary");
        return guids.Length > 0
            ? AssetDatabase.LoadAssetAtPath<MapIconLibrary>(AssetDatabase.GUIDToAssetPath(guids[0]))
            : null;
    }

    // The project's MapIconLibrary asset, or the built-in defaults if none exists.
    public static MapIconLibrary ResolveLibrary() => ResolveLibraryAsset() != null ? ResolveLibraryAsset() : MapIconLibrary.Default;

    public static Texture2D Create(int size) =>
        new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };

    public static void Render(Texture2D tex, MapIcon icon, MapIconLibrary lib)
    {
        if (lib == null) lib = MapIconLibrary.Default;
        int n = tex.width;
        for (int py = 0; py < n; py++)
        for (int px = 0; px < n; px++)
        {
            float x = px / (float)(n - 1) * 2f - 1f;   // -1..1 left→right
            float y = py / (float)(n - 1) * 2f - 1f;   // -1 (base) at bottom row → +1 (apex) at top
            tex.SetPixel(px, py, Sample(icon, x, y, lib));
        }
        tex.Apply();
    }

    static readonly Color Backdrop = new Color(0.12f, 0.12f, 0.14f, 1f);
    static Color Fill(float x)
    {
        float g = Mathf.Pow(Mathf.Clamp01(0.5f + x * 0.5f), 0.6f);
        return Color.Lerp(new Color(0.85f, 0.85f, 0.82f), new Color(0.18f, 0.18f, 0.22f), g);
    }
    static Color Over(Color under, Color over, float a) => Color.Lerp(under, over, Mathf.Clamp01(a));

    static Color Sample(MapIcon icon, float x, float y, MapIconLibrary lib)
    {
        Color c    = Backdrop;
        Color fill = Fill(x);

        switch (icon)
        {
            case MapIcon.FishBowl:
            {
                var p = lib.fishBowl;
                float bowlY = 1f - p.bowlRadiusFactor;
                bool stick = Mathf.Abs(x) <= p.stickWidthFactor && y >= -1f && y <= bowlY;
                bool bowl  = InCircle(x, y, 0f, bowlY, p.bowlRadiusFactor);
                if (stick || bowl) c = Over(c, fill, 1f);
                break;
            }
            case MapIcon.StreetLight:
            {
                var p = lib.streetLight;
                float by = p.bulbCenterYFactor;
                if (InCircle(x, y, 0f, by, p.haloRadiusFactor)) c = Over(c, fill, p.haloAlpha);       // halo
                if (Mathf.Abs(x) <= p.stickWidthFactor && y >= -1f && y <= by) c = Over(c, fill, 1f); // stick
                if (InCircle(x, y, 0f, by, p.bulbRadiusFactor)) c = Over(c, Color.white, 1f);         // lit bulb
                break;
            }
            case MapIcon.BigSpike:
            default:
            {
                var p = lib.spike;
                if (InTriangle(x, y, 0f, 1f, -p.widthFactor, -1f, p.widthFactor, -1f))
                {
                    float fadeTop = -1f + p.baseFadeFraction * 2f;
                    float a = y < fadeTop ? Mathf.Clamp01((y + 1f) / Mathf.Max(1e-4f, p.baseFadeFraction * 2f)) : 1f;
                    c = Over(c, fill, a);
                }
                break;
            }
        }
        return c;
    }

    static bool InCircle(float x, float y, float cx, float cy, float r)
        => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

    static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
    {
        float d1 = Sign(px, py, ax, ay, bx, by);
        float d2 = Sign(px, py, bx, by, cx, cy);
        float d3 = Sign(px, py, cx, cy, ax, ay);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0;
        bool pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
    static float Sign(float px, float py, float ax, float ay, float bx, float by)
        => (px - bx) * (ay - by) - (ax - bx) * (py - by);
}
