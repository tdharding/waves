using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The one folder the level select's hand-drawn river decals live in, the sheet they are gathered
/// onto, and the only place either path is written down.
///
/// A folder rather than a list of object fields, for the same reason the presets have one: the
/// decals are drawings that arrive one at a time over months, and a window that reads a folder
/// picks up a new one the moment it is dropped in, where a list of fields has to be extended and
/// re-wired for every drawing. Drop a PNG in the folder, press Rebuild Sheet, and it joins the
/// scatter.
///
/// ── Why they are gathered onto one sheet ──────────────────────────────────────
/// The shader stamps decals by picking one at random for every place in the scatter, which means
/// the choice is made per PIXEL, not per material. A texture per drawing would need every one of
/// them bound and read at once, because the pixel next door may have drawn a different one — so
/// the shader would grow a sampler for each drawing added and read all of them to use one.
///
/// One sheet of equal cells reads once, however many drawings there are. The shader is told only
/// how the cells are laid out — columns, rows, how many hold a drawing, and how many pixels one
/// cell is — and picks a cell; it neither knows nor cares what is in them.
///
/// ── Why the sheet is built here rather than imported ──────────────────────────
/// The cells have to be equal and the drawings will not be, so each one is fitted to its cell —
/// its longest side to the cell's, its shape kept — which means resampling, padding, and bleeding
/// the drawing's colour out into the transparent surround so the sheet's own mips cannot drag the
/// edge of a line toward black. None of that is something an importer will do to a folder of
/// loose PNGs, and all of it has to agree exactly with the arithmetic in RiverRunShading.hlsl.
///
/// The drawings themselves are read out of their FILES rather than off their imported textures,
/// so a decal needs no particular import settings to be gathered — no Read/Write Enabled, no
/// uncompressed format, nothing. That matters because the folder is meant to be somewhere a
/// drawing can simply be dropped. A file in a format that cannot be decoded that way falls back
/// to its imported texture, and is named in the tuner if that cannot be read either.
///
/// ── Why the sheet is an .asset and not a PNG ──────────────────────────────────
/// Written as a Texture2D asset, the pixels, the mips, the filtering and the wrapping are exactly
/// what was put in them, with no importer in between to have a different opinion about sRGB,
/// alpha or compression — and rebuilding writes over the same asset in place, so a preset holding
/// the sheet goes on holding it rather than being left pointing at a deleted file.
/// </summary>
public static class RiverRunDecalLibrary
{
    /// <summary>Where the hand-drawn decals are kept. Alongside the shader that stamps them.</summary>
    public const string Folder = "Assets/TextureMatShader/LevelSelectMaterials/RiverRunDecals";

    /// <summary>
    /// Where the built sheet goes. A subfolder, so scanning the folder above it for drawings can
    /// never pick the sheet up as a drawing and gather it onto itself.
    /// </summary>
    public const string SheetFolder = Folder + "/Generated";

    public const string SheetPath = SheetFolder + "/RiverRunDecalSheet.asset";

    /// <summary>
    /// The most pixels one cell of the sheet is given. A decal is stamped a few centimetres
    /// across on stone this size, so a cell beyond this is detail nothing will ever be close
    /// enough to see, paid for in a sheet that has to be held in memory whole.
    /// </summary>
    private const int MaxCell = 256;

    /// <summary>
    /// The most the whole sheet is allowed to be, either way. Past this the cells are halved
    /// instead of the sheet growing — the drawings lose detail, which is recoverable, rather than
    /// the sheet becoming something a platform may refuse to load, which is not.
    /// </summary>
    private const int MaxSheet = 1024;

    /// <summary>
    /// Transparent pixels left round the edge of every cell. Without them the sheet's mips —
    /// which know nothing about cells — average across the boundary and let one drawing's edge
    /// show faintly round another's.
    /// </summary>
    private const int Padding = 2;

    // ─────────────────────────────────────────────────────────────
    // WHAT IS THERE
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The drawings in the folder, by name. Read fresh each time rather than cached, for the same
    /// reason the preset picker is: drawings are dropped in and renamed while the window is open.
    ///
    /// The folder ITSELF only — not the Generated subfolder under it, which is where the built
    /// sheet lives.
    ///
    /// By name rather than by whatever order the asset database hands them back, because the name
    /// order is what decides which cell each drawing lands in, and a sheet whose cells moved
    /// about between rebuilds would move every decal in the world with them.
    /// </summary>
    public static List<Texture2D> All()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) return new List<Texture2D>();

        return AssetDatabase.FindAssets("t:Texture2D", new[] { Folder })
                            .Select(AssetDatabase.GUIDToAssetPath)
                            .Where(p => !string.IsNullOrEmpty(p))
                            .Distinct()
                            .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == Folder)
                            .OrderBy(Path.GetFileNameWithoutExtension,
                                     StringComparer.OrdinalIgnoreCase)
                            .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                            .Where(t => t != null)
                            .ToList();
    }

    /// <summary>The built sheet, or null if none has been built.</summary>
    public static Texture2D Sheet() => AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);

    /// <summary>
    /// Why the sheet no longer matches the folder, in a sentence — or null when it does.
    ///
    /// Said rather than silently fixed. Rebuilding writes an asset, and a window that writes
    /// assets while it is being drawn is a window that writes them on every repaint; and a sheet
    /// that quietly rebuilt itself would also quietly move every decal in the world the moment a
    /// drawing was added. So the state is counted and reported, exactly as the seam data above it
    /// is, and the rebuild is a button.
    /// </summary>
    public static string Behind(RiverRunShadingSettings settings)
    {
        if (settings == null) return null;

        var decals = All();
        var sheet  = Sheet();

        if (decals.Count == 0)
        {
            return settings.decalSheetHeld > 0 || settings.decalSheet != null
                 ? "There are no drawings in the folder, but the sheet still holds " +
                   Mathf.Max(settings.decalSheetHeld, 0) + ". Rebuild to clear it."
                 : null;
        }

        if (sheet == null)
            return decals.Count + Some(decals.Count, " drawing", " drawings") +
                   " in the folder, and no sheet built from them yet.";

        if (settings.decalSheet != sheet)
            return "The sheet in the folder is not the one these settings are holding. Rebuild " +
                   "to point them at it.";

        if (settings.decalSheetHeld != decals.Count)
            return "The folder holds " + decals.Count +
                   Some(decals.Count, " drawing", " drawings") + " and the sheet was built from " +
                   settings.decalSheetHeld + ".";

        // A drawing changed without the count changing — redrawn rather than added. Compared by
        // file time because that is the only thing that moves when a PNG is overwritten in place.
        DateTime built = LastWrite(SheetPath);
        foreach (var decal in decals)
        {
            if (LastWrite(AssetDatabase.GetAssetPath(decal)) > built)
                return "'" + decal.name + "' has been redrawn since the sheet was built.";
        }

        return null;
    }

    private static string Some(int n, string one, string many) => n == 1 ? one : many;

    private static DateTime LastWrite(string assetPath) =>
        !string.IsNullOrEmpty(assetPath) && File.Exists(assetPath)
            ? File.GetLastWriteTimeUtc(assetPath)
            : DateTime.MinValue;

    // ─────────────────────────────────────────────────────────────
    // BUILDING THE SHEET
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Gathers the folder's drawings onto the sheet and writes what it laid out into
    /// <paramref name="into"/>, so the push has the cells to tell the shader about.
    ///
    /// Returns false, with <paramref name="message"/> saying why, when there is nothing to gather
    /// or nothing could be read. The settings are still brought up to date in that case — an
    /// empty folder leaves them holding no sheet at all, which is what turns the decals off.
    /// </summary>
    public static bool Build(RiverRunShadingSettings into, out string message)
    {
        if (into == null) { message = "No settings to build into."; return false; }

        var decals = All();
        if (decals.Count == 0)
        {
            Forget(into);
            message = "There are no drawings in " + Folder + " to gather.";
            return false;
        }

        // Read every drawing first. One that cannot be read is left out rather than stopping the
        // build, and named, so a single awkward file does not hold up the rest.
        var read   = new List<Drawing>();
        var closed = new List<string>();

        foreach (var decal in decals)
        {
            if (TryRead(decal, out Drawing drawing)) read.Add(drawing);
            else                                    closed.Add(decal.name);
        }

        if (read.Count == 0)
        {
            Forget(into);
            message = "None of the drawings in " + Folder + " could be read: " +
                      string.Join(", ", closed);
            return false;
        }

        // One cell size for every drawing, taken from the biggest of them, so the one with the
        // most in it is the one that is not resampled down. A power of two because that is what
        // mips halve cleanly.
        int biggest = read.Max(d => Mathf.Max(d.Width, d.Height));
        int cell    = Mathf.Clamp(Mathf.NextPowerOfTwo(biggest), 16, MaxCell);

        // As square a sheet as the count allows, so neither side runs away from the other.
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(read.Count)));
        int rows    = Mathf.Max(1, Mathf.CeilToInt(read.Count / (float)columns));

        // Halve the cells rather than let the sheet grow past what a platform will take.
        while ((columns * cell > MaxSheet || rows * cell > MaxSheet) && cell > 16) cell /= 2;

        int width  = columns * cell;
        int height = rows * cell;
        int inner  = Mathf.Max(1, cell - Padding * 2);

        var pixels = new Color[width * height];   // transparent, which is what a default Color is

        for (int i = 0; i < read.Count; i++)
        {
            Drawing d = read[i];

            // Its longest side fills the cell and its shape is kept, so Scale in the tuner means
            // the same thing for a tall drawing and a wide one: the metres across whichever way
            // round it happened to be drawn.
            float fit = inner / (float)Mathf.Max(d.Width, d.Height);
            int   fw  = Mathf.Clamp(Mathf.RoundToInt(d.Width  * fit), 1, inner);
            int   fh  = Mathf.Clamp(Mathf.RoundToInt(d.Height * fit), 1, inner);

            Color[] fitted = Resample(d, fw, fh);

            // Cell i sits at column i-across, row i-up — filled left to right and bottom to top,
            // which is the order RiverRunShading.hlsl reads them back in. Bottom to top because a
            // texture's first pixel is its bottom left one, both here and in a UV.
            int cx = (i % columns) * cell + (cell - fw) / 2;
            int cy = (i / columns) * cell + (cell - fh) / 2;

            for (int y = 0; y < fh; y++)
            for (int x = 0; x < fw; x++)
                pixels[(cy + y) * width + cx + x] = fitted[y * fw + x];
        }

        // Push each drawing's colour out into the transparent padding round it. The sheet's mips
        // average colour and alpha together and know nothing about which pixels were meant to be
        // there, so a line drawn against pure transparent black darkens as it shrinks; against
        // its own colour bled outward it keeps it.
        Bleed(pixels, width, height);

        Texture2D sheet = Write(pixels, width, height);
        if (sheet == null)
        {
            message = "The sheet could not be written to " + SheetPath + ".";
            return false;
        }

        into.decalSheet           = sheet;
        into.decalSheetColumns    = columns;
        into.decalSheetRows       = rows;
        into.decalSheetHeld       = read.Count;
        into.decalSheetCellPixels = cell;

        message = "Gathered " + read.Count + Some(read.Count, " drawing", " drawings") +
                  " onto a " + width + "x" + height + " sheet, " + columns + "x" + rows +
                  " cells of " + cell + "px.";

        if (closed.Count > 0) message += " Could not read: " + string.Join(", ", closed) + ".";

        return true;
    }

    /// <summary>
    /// Leaves the settings holding no sheet, which is what the shader reads as no decals — it
    /// checks the count before it ever samples, because there is no such thing as a transparent
    /// default texture for an unbound sampler to fall back on.
    /// </summary>
    private static void Forget(RiverRunShadingSettings into)
    {
        into.decalSheet           = null;
        into.decalSheetColumns    = 0;
        into.decalSheetRows       = 0;
        into.decalSheetHeld       = 0;
        into.decalSheetCellPixels = 0;
    }

    // ─────────────────────────────────────────────────────────────
    // READING A DRAWING
    // ─────────────────────────────────────────────────────────────

    private struct Drawing
    {
        public Color[] Pixels;
        public int     Width;
        public int     Height;
    }

    /// <summary>
    /// A drawing's pixels, out of its FILE where the file is one that can be decoded, so a decal
    /// needs no particular import settings to be gathered.
    ///
    /// Anything else falls back to the imported texture, which only answers if it happens to be
    /// readable — and if it is not, the drawing is named in the tuner rather than quietly going
    /// missing from the sheet.
    /// </summary>
    private static bool TryRead(Texture2D decal, out Drawing drawing)
    {
        drawing = default;

        string path = AssetDatabase.GetAssetPath(decal);
        string kind = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();

        if ((kind == ".png" || kind == ".jpg" || kind == ".jpeg") && File.Exists(path))
        {
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            bool ok = false;

            try
            {
                ok = ImageConversion.LoadImage(loaded, File.ReadAllBytes(path), false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RiverRunDecals] " + decal.name + ": " + e.Message);
            }

            if (ok)
            {
                drawing = new Drawing
                {
                    Pixels = loaded.GetPixels(),
                    Width  = loaded.width,
                    Height = loaded.height,
                };
            }

            UnityEngine.Object.DestroyImmediate(loaded);
            if (ok) return true;
        }

        if (!decal.isReadable) return false;

        drawing = new Drawing
        {
            Pixels = decal.GetPixels(),
            Width  = decal.width,
            Height = decal.height,
        };
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // FITTING IT TO A CELL
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A drawing at the size its cell wants it.
    ///
    /// Both ways of getting there weight colour by ALPHA. A transparent pixel in a hand-drawn PNG
    /// carries whatever colour was left under it, very often black, and averaging that in
    /// unweighted is what puts a dark halo round every line the moment it is resampled. Weighted,
    /// only the colour that was actually drawn counts toward the answer.
    /// </summary>
    private static Color[] Resample(Drawing d, int fw, int fh)
    {
        // Averaging a block of the original is right when the cell is smaller than the drawing
        // and wrong when it is larger — a block under a pixel across collapses to picking one.
        // Which way round it is depends on the BIGGEST drawing in the folder rather than on this
        // one, so both cases really do come up.
        return fw < d.Width || fh < d.Height ? Blocks(d, fw, fh) : Corners(d, fw, fh);
    }

    /// <summary>Each new pixel the average of the block of old ones it covers.</summary>
    private static Color[] Blocks(Drawing d, int fw, int fh)
    {
        var made = new Color[fw * fh];

        for (int y = 0; y < fh; y++)
        {
            int y0 = y * d.Height / fh;
            int y1 = Mathf.Max(y0 + 1, (y + 1) * d.Height / fh);

            for (int x = 0; x < fw; x++)
            {
                int x0 = x * d.Width / fw;
                int x1 = Mathf.Max(x0 + 1, (x + 1) * d.Width / fw);

                float r = 0f, g = 0f, b = 0f, a = 0f;
                int   n = 0;

                for (int sy = y0; sy < y1; sy++)
                for (int sx = x0; sx < x1; sx++)
                {
                    Color c = d.Pixels[sy * d.Width + sx];
                    r += c.r * c.a;
                    g += c.g * c.a;
                    b += c.b * c.a;
                    a += c.a;
                    n++;
                }

                made[y * fw + x] = a > 1e-6f
                    ? new Color(r / a, g / a, b / a, a / n)
                    : new Color(0f, 0f, 0f, 0f);
            }
        }

        return made;
    }

    /// <summary>Each new pixel leaned between the four old ones around it.</summary>
    private static Color[] Corners(Drawing d, int fw, int fh)
    {
        var made = new Color[fw * fh];

        for (int y = 0; y < fh; y++)
        for (int x = 0; x < fw; x++)
        {
            float px = (x + 0.5f) * d.Width  / fw - 0.5f;
            float py = (y + 0.5f) * d.Height / fh - 0.5f;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(px), 0, d.Width  - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(py), 0, d.Height - 1);
            int x1 = Mathf.Min(x0 + 1, d.Width  - 1);
            int y1 = Mathf.Min(y0 + 1, d.Height - 1);

            float fx = Mathf.Clamp01(px - x0);
            float fy = Mathf.Clamp01(py - y0);

            float r = 0f, g = 0f, b = 0f, a = 0f, weight = 0f;

            Lean(d, x0, y0, (1f - fx) * (1f - fy), ref r, ref g, ref b, ref a, ref weight);
            Lean(d, x1, y0,        fx  * (1f - fy), ref r, ref g, ref b, ref a, ref weight);
            Lean(d, x0, y1, (1f - fx) *        fy,  ref r, ref g, ref b, ref a, ref weight);
            Lean(d, x1, y1,        fx  *       fy,  ref r, ref g, ref b, ref a, ref weight);

            made[y * fw + x] = a > 1e-6f
                ? new Color(r / a, g / a, b / a, a / Mathf.Max(weight, 1e-6f))
                : new Color(0f, 0f, 0f, 0f);
        }

        return made;
    }

    private static void Lean(Drawing d, int x, int y, float w,
                             ref float r, ref float g, ref float b, ref float a, ref float weight)
    {
        Color c  = d.Pixels[y * d.Width + x];
        float wa = w * c.a;

        r += c.r * wa;
        g += c.g * wa;
        b += c.b * wa;
        a += wa;
        weight += w;
    }

    // ─────────────────────────────────────────────────────────────
    // BLEEDING THE COLOUR OUT
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fills the colour of every transparent pixel from the drawn pixels beside it, outward, a
    /// ring at a time — leaving the alpha exactly as it was, so nothing new is drawn. Only what
    /// sits UNDER the transparency changes, and that shows through nowhere but in a mip.
    /// </summary>
    private static void Bleed(Color[] pixels, int width, int height)
    {
        var drawn = new bool[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) drawn[i] = pixels[i].a > 0f;

        // Far enough to carry the colour across the padding and a little way past it, which is as
        // far as any mip small enough to matter will reach.
        const int Rings = 6;

        for (int ring = 0; ring < Rings; ring++)
        {
            var next = (bool[])drawn.Clone();
            bool grew = false;

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (drawn[i]) continue;

                float r = 0f, g = 0f, b = 0f;
                int   n = 0;

                Borrow(pixels, drawn, width, height, x - 1, y, ref r, ref g, ref b, ref n);
                Borrow(pixels, drawn, width, height, x + 1, y, ref r, ref g, ref b, ref n);
                Borrow(pixels, drawn, width, height, x, y - 1, ref r, ref g, ref b, ref n);
                Borrow(pixels, drawn, width, height, x, y + 1, ref r, ref g, ref b, ref n);

                if (n == 0) continue;

                pixels[i] = new Color(r / n, g / n, b / n, pixels[i].a);
                next[i]   = true;
                grew      = true;
            }

            drawn = next;
            if (!grew) break;
        }
    }

    private static void Borrow(Color[] pixels, bool[] drawn, int width, int height,
                               int x, int y, ref float r, ref float g, ref float b, ref int n)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return;

        int i = y * width + x;
        if (!drawn[i]) return;

        r += pixels[i].r;
        g += pixels[i].g;
        b += pixels[i].b;
        n++;
    }

    // ─────────────────────────────────────────────────────────────
    // WRITING IT
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The sheet on disk, written over IN PLACE where one is already there — same asset, same
    /// guid — so a preset holding the sheet goes on holding it through a rebuild rather than
    /// being left pointing at nothing.
    /// </summary>
    private static Texture2D Write(Color[] pixels, int width, int height)
    {
        LevelSelectRiverPresetLibrary.EnsureFolder(SheetFolder);

        var  sheet = Sheet();
        bool made  = sheet == null;

        if (made)
        {
            sheet = new Texture2D(width, height, TextureFormat.RGBA32, true, false);
        }
        else if (sheet.width != width || sheet.height != height)
        {
            sheet.Reinitialize(width, height, TextureFormat.RGBA32, true);
        }

        sheet.name       = Path.GetFileNameWithoutExtension(SheetPath);
        sheet.filterMode = FilterMode.Bilinear;

        // Clamped rather than repeated: every read is inside one cell, and a read straying a
        // fraction of a texel past the sheet's own edge should hold that edge rather than arrive
        // back at the opposite corner in a different drawing.
        sheet.wrapMode = TextureWrapMode.Clamp;

        sheet.SetPixels(pixels);
        sheet.Apply(true, false);

        if (made) AssetDatabase.CreateAsset(sheet, SheetPath);
        else      EditorUtility.SetDirty(sheet);

        AssetDatabase.SaveAssets();

        return Sheet();
    }
}
